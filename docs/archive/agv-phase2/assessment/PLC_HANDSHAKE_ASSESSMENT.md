# AGV Phase 2 – Đánh giá PLC handshake

> Nhãn: [NV] đã xác nhận · [NGUỒN] tài liệu nguồn · [CODE] hành vi code · [ĐX] đề xuất · [HỎI] cần xác nhận. Trạng thái: Implemented / Partially / Not Implemented / Not Verified.
> **Không có địa chỉ PLC hay mapping Station nào được tạo/suy ra trong tài liệu này.** Các giá trị trích từ workbook là **trích dẫn nguồn**, không phải cấu hình được duyệt (CLAUDE.md §7.3, §7.5, §7.8).
> Đường dẫn code từ `src/backend/`.

## 1. Cổng triển khai hiện hành [NV]

- CR-029 (`docs/change-management/CR-029_PLC_INTERFACE_20_11_2026.md`) ở trạng thái `PENDING_APPROVAL`; **không được** code/migration/cấu hình cho giao diện 20/11 cho tới khi: CR-029 được duyệt, bên PLC trả lời các câu hỏi chặn, và kế hoạch migration được duyệt (CLAUDE.md §7.8 "Implementation gate").
- D-1…D-4 (ADR-0011) là quyết định kiến trúc, **chưa triển khai** (`PLC_COMMUNICATION_ARCHITECTURE_V2_20_11_2026.md` dòng 16-17, 82).

## 2. Phát hiện quan trọng: workbook 20/11 ở working tree ≠ bản đã phân tích

| | Bản HEAD (đã commit, CR-029 phân tích) | Bản working tree (**chưa commit**, `M` trong git) |
|---|---|---|
| sha256 (đầu) | `3c5da74b…` | `a291d28f…` |
| Phạm vi | 1 khối PLC `EF-G` 192.168.10.131: E1, E2, G1, G2 (4 trạm); "các PLC khác tương tự" | 4 khối đủ 16 trạm: PLC "A1" .101 (A1–A4), "BCD" .111 (B1,B2,D1,D2), nhãn "A2" .121 (X1,X2,X3,**X4**), "EF-G" .131 (E1,E2,G1,G2) |
| Địa chỉ thanh ghi | **Trống** | **Có** (D1500…D1631) cho hầu hết tín hiệu; `AGV_TranferCompleted` **không có địa chỉ ở cả 16 trạm** |
| `PLC_ReaddyTranfer` | BOOL 0 đóng/1 mở | INT 1 cho phép Pick, 2 cho phép Drop, 10 lỗi |
| `PLC_MissionCompleted` | BOOL 0 thất bại/1 thành công | INT **1 thất bại / 2 thành công** (đảo nghĩa của giá trị 1 so với HEAD) |
| `AGV_RequestTranfer` | BOOL 0 rời đi/1 đã đến | INT 1 mở cửa-kẹp để Pick, **4** mở cửa-kẹp để Drop |
| `AGV_TranferCompleted` | BOOL | INT 3 hoàn thành Pick, 4 hoàn thành Drop |
| `PLC_MissionRequest` | INT 0/1 Pick/2 Drop/**>10** lỗi | INT 0/1/2/**10** lỗi |
| `Mission_Feedback` | 0 chưa có AGV, 1–3 = AGV1–3, >10 lỗi | Như cũ (1–3 = AGV1–3, 10 lỗi) |
| Heartbeat | 1 BOOL | `HeartBit` `M9200` BOOL (chu kỳ 1 s, timeout 10 s) + 5 spare `M9201–M9205`, **mỗi khối PLC** |

- **[CODE/Git]** Thay đổi này là chỉnh sửa tài liệu được kiểm soát (CLAUDE.md §7.6) chưa có Change Request/cập nhật CR-029. Audit này **không** lấy địa chỉ trong bản working tree làm mapping; không có gì được cấu hình theo nó. [HỎI Q-10: ai/CR nào sở hữu thay đổi này?]
- **[NGUỒN]** Cùng địa chỉ `D1500/1510/1520/1530` lặp lại ở cả 4 PLC: hợp lệ theo CLAUDE.md §7.3 (mỗi PLC có không gian riêng); khóa định danh vẫn là `PLC_ID + Station_ID + Signal_Name`.
- **[NGUỒN]** Sheet `Trang_tính1` (sơ đồ 6 bước) giống nhau ở HEAD và working tree: (1) PLC gửi `PLC_MissionRequest` (Lấy/Cấp Rack) → (2) phần mềm nhận nhiệm vụ, PLC quan sát `Mission_Feedback` → (3) `AGV_RequestTranfer` khi AGV tới trước cửa trạm → (4) PLC mở cửa/kẹp, `PLC_ReaddyTranfer` → (5) `AGV_TranferCompleted` AGV đã lấy/đã ra ngoài → (6) `PLC_MissionCompleted`. Chiều của vài mũi tên/thời điểm reset **không được ghi** → không suy diễn.

## 3. Hiện trạng code theo giao diện

### 3.1 Giao diện cũ 10/11 (9 bước, `M9000…`) – nền của CR-010

| Thành phần | File:dòng | Trạng thái | Ghi chú |
|---|---|---|---|
| Nhận Pickup/Delivery từ snapshot PLC (Request_ID D1500, Rack_Full D1510, Rack_Empty D1512, Destination_Available D1514, Rack_Type D1520) | `WCS.Application/Routing/PlcRequestService.cs:10-14, 34-137` | **Partially** – logic có, **không có nguồn tự động** gọi nó | Chỉ có điểm vào HTTP `POST …/plc-requests` (`WCS.Api/Api/RoutingEndpoints.cs:99-103`, Admin); không có vòng đọc PLC |
| Ghi `Request_Ack`/`Transfer_Permit`/… | `Plc/PlcStationSignalService.cs:12-14,62-64`; `PlcRequestService.cs:28-29` | **Not Implemented (cố ý)** | Ghi chú: "never writes … acknowledgement timing is an open decision" |
| Quan sát pha trạm | `Plc/Handshake.cs:33,64-79` (`HandshakePhase`, `Observe`) | **Implemented** (chỉ quan sát) | Dùng tên tín hiệu cũ (`Station_Online`, `Pickup_Request`…) |
| Bộ phối hợp handshake (set/clear theo rule) | `Plc/Handshake.cs:45-147` | **Partially** | Rule chỉ có hiệu lực khi `Approved=true` + `ApprovalReference` (`:81-82`); mặc định rỗng (`:149-153`) → **không ghi gì** |
| Khôi phục khi PLC nối lại | `Routing/StationTransactionService.cs:133-170, 220-235`; `RoutingHostedService.cs:32-53` | **Partially** | `IPlcObservationSource` đăng ký là `UnavailablePlcObservationSource` (`Infrastructure/DependencyInjection.cs:92`, `Simulation/RoutingAdapters.cs:48`) → mọi PLC nối lại cho kết quả `NoData` |
| Heartbeat phần mềm | – | **Not Implemented** | grep `Heartbeat_SW/HeartBit/M9200/M9100` ngoài mẫu: không có writer |
| Đọc/poll thanh ghi, chất lượng điểm, kết nối MC | `Plc/PlcWorker.cs`, `Plc/StationIoHandler.cs`, `Infrastructure/Plc/Mc/*` | **Implemented** (khung read-only/giám sát) | Hàng đợi I/O `Channel` cho một worker/PLC (`PlcWorker.cs:42-43`) – tuần tự hóa **đọc/ghi** thanh ghi, **không** phải hàng đợi giao dịch |

### 3.2 Giao diện mới 20/11

| Tín hiệu | Có trong code? | Trạng thái |
|---|---|---|
| `PLC_MissionRequest`, `PLC_ReaddyTranfer`, `PLC_MissionCompleted`, `AGV_RequestTranfer`, `AGV_TranferCompleted`, `Mission_Feedback` | **Không** (grep toàn `src/` không khớp). Catalog mẫu `Infrastructure/Plc/Catalog/plc-io-catalog.json` chứa 23 tín hiệu cũ (Safety_Ready … Error_Code) | **Not Implemented** |
| Mapping Station cho PLC4 | Catalog: E1,E2,**G3,G7**; workbook 20/11: E1,E2,**G1,G2** | Mâu thuẫn mở (PQ-20) |
| Mapping Station cho PLC3 | Catalog: X1,X2,X3,**X6**; workbook 20/11 (WT): X1–X3,**X4** | Mâu thuẫn mở (OP-04) |

→ Nhận định: đúng theo cổng triển khai, **chưa có gì** của giao diện 20/11 trong code. Đây không phải thiếu sót mà là trạng thái hợp lệ.

## 4. Giới hạn một giao dịch tại mỗi PLC (D-1) và FIFO

| Mục | Mục tiêu [NV] D-1 | Hiện trạng [CODE] | Trạng thái |
|---|---|---|---|
| Tối đa **một Handshake hoạt động mỗi PLC** | Các Station cùng PLC không handshake đồng thời | Ràng buộc DB là **mỗi cổng**: `ux_station_transactions_one_active_per_port` (`Infrastructure/Persistence/Configurations/RoutingConfigurations.cs:164-165`); `StationTransaction.IsActive` (`Domain/Routing/StationTransaction.cs:65`) | **Not Implemented** |
| **FIFO riêng mỗi PLC** (bền qua khởi động lại) | Hàng đợi bền theo thời điểm WCS nhận | **Không có**. Yêu cầu mới tới cổng đang bận bị **từ chối** `PORT_BUSY` (`PlcRequestService.cs:85-86`, `:124-133`), không xếp hàng. Legs sẵn sàng chỉ được quét lại theo `CreatedAt` (`Infrastructure/Persistence/Repositories/RoutingRepository.cs:67-70`) và bị bỏ qua khi cổng bận (`RouteEngine.cs:478-483`) | **Not Implemented** |
| Các PLC chạy độc lập, song song | Có | Có: mỗi PLC một `PlcWorker`; test `Cr010MultiLegTests.cs:162` | **Implemented** |
| Test hiện hành mâu thuẫn D-1 | – | `Cr010MultiLegTests.cs:145` `Test07_two_ports_of_the_same_PLC_hold_two_station_transactions_at_the_same_time` khẳng định hai trạm cùng PLC giữ hai giao dịch **đồng thời** | Test sẽ phải đổi khi triển khai D-1 (cần CR) |
| Mốc bắt đầu/kết thúc "Handshake hoạt động" | Không nguồn nào định nghĩa | `StationTransactionState` có 9 trạng thái tiến (`RoutingEnums.cs:86-90`); ánh xạ bit PLC → trạng thái **chưa quyết** (Q-MAP-1, comment `RoutingEnums.cs:82-85`) | **[HỎI Q-11]** không được tự giả định |

**Lưu ý riêng:** `Station Transaction` của CR-010 kéo dài đến khi AMR rời cổng (`StationTransaction.cs:5-9, 71-89` – `Completed` chỉ sau `AgvExited`), tức là bao phủ cả thời gian AMR di chuyển tới trạm. Nếu D-1 lấy "giao dịch hoạt động" = toàn bộ Station Transaction, mỗi PLC sẽ bị khóa suốt hành trình AMR; nếu lấy = chỉ 6 bước handshake thì phạm vi ngắn hơn rất nhiều. Hai cách hiểu cho hiệu năng khác hẳn. [HỎI Q-11]

## 5. Đối chiếu ngữ nghĩa D-4 (rack presence/identity) và D-2 (RobotCode)

- **D-4** [NV]: `PLC_MissionCompleted` cần nhưng không đủ để xác nhận bàn giao. Hiện `ConfirmDropAsync` chỉ nhận nguồn `Operator` (`RoutingEndpoints.cs:72,75`); chưa có đường xác nhận từ PLC/RCS (RCS báo hoàn thành mà chưa có drop bị **chặn đúng** `RouteEngine.cs:398-402`). Presence/identity từ thiết bị: **Not Implemented**; chưa biết PLC có cung cấp được không (PQ chờ trả lời).
- **D-2** [NV]: `Mission_Feedback` **không** là `RobotCode`. Code chưa ghi `Mission_Feedback`; `RobotCode` thật đã lưu ở Leg (`TransportOrder.cs:120,158`). Cần kênh/thanh ghi mới nếu muốn báo robot cho PLC (chưa thống nhất).

## 6. Rủi ro riêng của bản working tree workbook 20/11 (để xử lý khi có CR)

1. `PLC_MissionCompleted`: **1 = thất bại, 2 = thành công**; trong khi HEAD (và các bool nói chung) 1 = thành công. Lập trình viên PLC/phần mềm có thể hiểu nhầm. [HỎI]
2. `AGV_RequestTranfer` dùng 1 và **4**; `AGV_TranferCompleted` dùng 3 và 4 – cùng giá trị 4 mang nghĩa khác nhau theo tín hiệu; không có giá trị 2. Cần bảng định nghĩa chính thức.
3. `AGV_TranferCompleted` **không có địa chỉ** ở mọi trạm (16/16): WCS không thể ghi tín hiệu này khi chưa có địa chỉ.
4. `Mission_Feedback` mã hóa AGV1–AGV3: mâu thuẫn D-2.
5. Heartbeat `M9200` nằm trong mỗi khối PLC: chưa được phân loại PLC-scoped hay Station-scoped (CLAUDE.md §7.3 yêu cầu phân loại tường minh).
6. Ghi PLC chưa được duyệt: heartbeat, request-transfer, completed đều là ghi liên quan chuyển động; theo §7.5 chỉ commissioning read-only trước; không ghi tín hiệu an toàn/permit để thử kết nối.

## 7. Câu hỏi/blocker PLC (tham chiếu OPEN_QUESTIONS)

Q-06 (mapping P1/P2/M5), Q-09 (ai là bên đặt `Request_ID`/định danh duy nhất – workbook 20/11 **không còn** `Request_ID`), Q-10 (kiểm soát bản workbook), Q-11 (định nghĩa "Handshake hoạt động"), Q-12 (thứ tự reset), Q-13 (D-4 presence/identity). Danh sách PQ gốc: `PLC_VENDOR_CLARIFICATION_QUESTIONS_20_11_2026.md` mục A (15 câu), B (10 câu), E.

## 8. Kết luận

| Hạng mục | Trạng thái |
|---|---|
| Khung giám sát PLC (đọc, chất lượng, kết nối, mỗi PLC một worker) | Implemented |
| Handshake theo 10/11 (observation + recovery framework) | Partially (không ghi, không nguồn đọc tự động) |
| Handshake theo 20/11 | **Not Implemented** (đúng cổng, chờ CR-029) |
| Một giao dịch/PLC + FIFO/PLC (D-1) | **Not Implemented** (và test hiện tại mô tả mô hình cũ) |
| Xác nhận bàn giao từ PLC/RCS (D-4) | **Not Implemented** |
| Heartbeat | **Not Implemented** |
