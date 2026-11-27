# AGV Phase 2 – Đánh giá Rack / Inventory / Buffer

> Nhãn: [NV] đã xác nhận (`docs/specification/inventory-management.md`, CLAUDE.md) · [NGUỒN] · [CODE] · [ĐX] · [HỎI]. Trạng thái: Implemented / Partially / Not Implemented / Not Verified.
> Đường dẫn từ `src/backend/`.

## 1. Mô hình dữ liệu [CODE]

| Thực thể | File | Điểm chính |
|---|---|---|
| `Rack` | `Domain/Inventory/Rack.cs:90-141` | Id nội bộ; `ExternalRackId` **tùy chọn** ("No business logic may depend on ExternalRackId being present", `:85-88`); `Status` Unknown/Full/Empty/InTransit (`Domain/Common/Enums.cs:24`); một `CurrentLocationId`; `BeginTransit` (`:114`), `PlaceAt` (`:125`), `Bind` (`:136`) |
| `Location` | `Domain/Locations/Location.cs` | `RackCapacity`, `AllowedRackKind` (Any/Full/Empty), `Type` (Station, Buffer, ManualBuffer, Waypoint, WaitingPosition, PickupLocation, DeliveryLocation – `Enums.cs:11-19`), `ReconciliationRequired`, `HasFreeSlot = capacity − occupied − reserved` (`:50`), `Touch()` phục vụ xung đột xmin (`:63`) |
| `Reservation` | `Domain/Inventory/Reservation.cs:154-183` | `SourceRack` (khóa một rack) và `DestinationLocation` (giữ một slot); trạng thái Active/Consumed/Released |
| `InventoryTransaction` | `Domain/Inventory/InventoryTransaction.cs` | Nhật ký PickConfirmed/DropConfirmed/Reconciliation… kèm nguồn xác nhận, người dùng, `BindOutcome` |

[NV] "WCS owns business rack inventory and location state" (CLAUDE.md §2) – được phản ánh: mọi thay đổi vị trí rack chỉ qua `ConfirmPick/ConfirmDrop`, kiểm kê PDA, đối soát.

## 2. Bất biến nhất quán (đối chiếu `inventory-management.md` 4.4–4.8)

| Quy tắc [NV] | Bằng chứng code | Trạng thái |
|---|---|---|
| Rack có tối đa một vị trí hiện tại | `Rack.CurrentLocationId` đơn; `BeginTransit` đặt null (`Rack.cs:114-122`) | Implemented |
| Reserve rack nguồn + đích trước khi cấp task | `RouteEngine.CreateLegAsync`: `Reservation.ForSourceRack` (`:276`) và `ReserveDestinationAsync` (`:278`, `:294-299`) khi đích `BeforePick`; đích `AfterPick` được reserve sau khi pick (`:331`) | Implemented (đích `AfterPick` có khoảng thời gian chưa giữ chỗ trước khi pick) |
| Khóa theo `Rack_ID` (không hai task cùng lấy một rack) | Chỉ mục duy nhất một Reservation SourceRack Active mỗi rack: `Infrastructure/Persistence/Configurations/EntityConfigurations.cs:76-77` | Implemented (có ở mức DB) |
| Khóa theo `Location_ID` (không vượt capacity) | `CountActiveDestinationAsync` + `HasFreeSlot` (`RouteConditionEvaluator.cs:119-120`) và `Touch()` để hai giao dịch cùng chiếm slot cuối xung đột `xmin` (`RouteEngine.cs:298`) | Implemented (qua optimistic concurrency, không có ràng buộc DB tổng số). Test: `Cr010MultiLegTests.cs:179`, `ConcurrencyTests.cs` |
| Không đánh dấu vị trí đích OCCUPIED khi mới tạo task | Chỉ `Reservation`; Occupied khi có rack tại vị trí (`Location.GetOccupancy :52-57`) | Implemented |
| Sau pick → `IN_TRANSIT`; sau drop → gán vị trí đích | `RouteEngine.cs:585, 632` | Implemented |
| Task không thực hiện được: **không** tự giải phóng reservation trước khi đối soát | Khi `Rejected` (nhiệm vụ chưa gửi): giải phóng ngay (`RouteEngine.cs:544-547`) – hợp lý vì chưa gửi; `CancelAsync` chỉ cho Leg chưa chạy (`:662-668`); sự cố sau khi đã gửi → `ReconciliationRequired`, **không** giải phóng | Implemented (nhất quán với 4.4) |
| Đối soát sau sự cố 4.8 (UNKNOWN/RECONCILIATION_REQUIRED, chặn dùng tài nguyên, Admin xác nhận, giải phóng sau đối soát) | Đánh dấu: `PlcRequestService.cs:104`, `RouteEngine.cs:251,432,601`; chặn dùng: `RouteConditionEvaluator.cs:40,57-60` | **Partially** – xem §3 |

## 3. Phát hiện: lối thoát đối soát cho mô hình CR-010 còn thiếu

1. `Location.SetReconciliationRequired(false, …)` chỉ được gọi **một nơi**: `Services/InventoryService.cs:251`, trong `ReconcileTaskAsync`, vốn chỉ chạy cho `TransportTask` **legacy** (`:224-232`, yêu cầu `task.RequiresReconciliation`). Không có đường tương đương cho `TransportOrder/TransportLeg` (grep: không caller khác).
2. Hệ quả [CODE]: khi `PlcRequestService` thấy PLC báo có rack mà WCS không có, nó đánh dấu Location `ReconciliationRequired` (`PlcRequestService.cs:98-114`); điều kiện `RackPresent`/`InventoryState` sau đó trả `false` cho vị trí này (`RouteConditionEvaluator.cs:40,57-60`) và không có API trong phần đã đọc để xóa cờ cho đường CR-010 → vị trí **bị khóa vĩnh viễn** cho tới khi có can thiệp trực tiếp DB hoặc luồng legacy.
3. Tương tự cho Order `ReconciliationRequired` (xem MISSION §6).
→ **Partially Implemented**; là blocker vận hành nếu đưa PLC→Order vào chạy thật. [HỎI Q-15]

## 4. Chọn rack theo "origin" (Origin-based Rack selection)

- [HỎI Q-03] Không có định nghĩa "origin" trong `docs/specification/*`, ma trận, hay code.
- [CODE] Cách code chọn rack hiện nay:
  1. Chọn **vị trí** nguồn: `SourceMode.Request` (vị trí gửi yêu cầu), `Candidates` (theo priority/order, ứng viên đầu tiên "dùng được" – `RouteEngine.cs:236-240`), `PreviousDestination` (rack đã lưu ở vị trí đích của Leg trước, kiểm đúng chỗ – `:241-257`).
  2. Trong vị trí đó chọn rack **chưa đặt chỗ** theo `LastConfirmedAt` tăng dần rồi `Id` (`Infrastructure/Persistence/Repositories/Repositories.cs:59-62`) – tức rack nằm lâu nhất (FIFO theo thời điểm xác nhận cuối).
  3. Loại rack: `Full`/`Empty` theo `order.RackKind`; `RackType` (loại hàng) chỉ khớp qua điều kiện `RackTypeMatch` (`RouteConditionEvaluator.cs:41-47`).
- Không có logic nào chọn rack dựa trên nơi rack đến từ đâu (vd. trả rack về đúng trạm đã sinh ra nó), không lưu "origin" trên `Rack`/`Order` (ngoài `RequestLocationCode`, `TransportOrder.cs:45`). → **Not Implemented** nếu "origin-based" có nghĩa chính sách riêng; **Partially** nếu chỉ là chọn nguồn theo vị trí gửi yêu cầu.

## 5. Buffer reservation và các loại điểm dừng

| Loại | Hành vi hiện tại [CODE] | Trạng thái |
|---|---|---|
| Buffer lưu rack (F1–F4, P1/P2 trong Phase 2) | Giữ chỗ đích + cập nhật tồn kho khi drop (`RouteEngine.cs:632`); `LocationAvailable` kiểm `Accepts(kind)` + slot (`RouteConditionEvaluator.cs:32-38`) | Implemented ở engine; **P1/P2 chưa là Location** trong baseline (`reference-processes.json` PH2-01) → cần cấu hình |
| `ManualBuffer` (F5/M5) | Là một `LocationType`; quy trình `ManualRequest` | Partially – không có luồng PDA/PLC (xem REQUIREMENT R-11) |
| Vị trí chờ trung gian (AMR giữ rack) | `LocationType.WaitingPosition`/`Waypoint` không lưu rack (`RouteConditionEvaluator.cs:18-21`), tồn kho giữ `IN_TRANSIT` (`RouteEngine.cs:341-348`) | Partially – chưa có Location khai báo, AMR không được điều tới đó |
| Thứ tự hàng đợi chờ buffer | Không có (xem MISSION §4) | Not Implemented |
| Buffer đầy khi một Order đang chờ | Order `Waiting` với `WaitKind` tương ứng, thử lại mỗi tick | Implemented |
| Giữ chỗ đích kéo dài khi Leg chờ | Reservation Active cho tới khi drop (Consume) hoặc hủy/từ chối (Release) | Implemented; không có timeout giữ chỗ → có thể giữ slot vô hạn nếu Order treo (**Not Verified** có cơ chế quét) |

## 6. Bind rack

- [NV] `inventory-management.md:74-83`: không áp dụng quy tắc bind chung; B1/B2 (BP-01) và A1/A2 (BP-03/06) không bind.
- [CODE] `RouteCandidate.NoBind` → `BindOutcome.NotBoundByProcess` (`RouteEngine.cs:633-635`); khi không `NoBind` và có `externalRackId` thì bind (`:635`). Process Phase 2 (`PH2-01…06`) không đặt `noBind` ở bất kỳ đâu → mặc định có thể bind. [HỎI Q-08]
- Điều kiện `RackBinding` (Bound/Unbound) có sẵn (`RouteConditionEvaluator.cs:64-74`) nhưng không process tham chiếu nào dùng; "kiểm tra bind rack" của F1–F5 được thay bằng truy vấn tồn kho WCS theo `inventory-management.md:76-79`.

## 7. D-4: đối soát vật lý ↔ tồn kho

| Yêu cầu D-4 [NV] | Hiện trạng | Trạng thái |
|---|---|---|
| Rack hiện diện tại trạm | Chỉ so PLC báo Pickup với tồn kho khi tạo yêu cầu (`PlcRequestService.cs:90-115`); không có tín hiệu "rack present" trong giao diện 20/11 | Not Implemented (chờ PLC) |
| Danh tính rack khi quy trình yêu cầu | `ExternalRackId` tùy chọn, không có nguồn đọc tự động | Not Implemented |
| Định danh giao dịch duy nhất/chống trùng | `Request_ID` unique theo PLC (`RoutingConfigurations.cs:167`) – nhưng giao diện 20/11 **không còn** `Request_ID` | Partially → [HỎI Q-09] |
| Điều kiện an toàn/interlock | Không có trong giao diện 20/11 (thiếu `Safety_Ready`…); `PlcConfigService.cs:311` liệt kê tín hiệu bị chặn ghi | Not Implemented/Unknown |
| WCS chỉ xác nhận bàn giao khi vị trí đúng + cập nhật rack + lịch sử | `ConfirmDropAsync`: `PlaceAt` + `InventoryTransaction` + `OnHandoverAsync` trong một unit of work (`RouteEngine.cs:632-642`) | Implemented (nhưng chỉ do toán tử kích hoạt) |

## 8. Kết luận

| Hạng mục | Trạng thái |
|---|---|
| Mô hình rack/location/reservation, khóa rack ở DB | Implemented |
| Chọn rack nguồn theo vị trí ứng viên + FIFO trong vị trí | Implemented |
| "Origin-based" selection | Not Implemented / chưa định nghĩa |
| Buffer reservation (đích) | Implemented (engine) – P1/P2/M5 chưa cấu hình |
| Hàng đợi chờ buffer, timeout giữ chỗ | Not Implemented / Not Verified |
| Lối thoát đối soát cho Location/Order trong mô hình CR-010 | **Not Implemented** (blocker vận hành) |
| Quản lý trạm thủ công F5/M5 | Partially |
