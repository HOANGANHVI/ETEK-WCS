# PLC Interface Change Impact Analysis – giao diện 10/11/2026 → 20/11/2026

> **IO MAP HIỆN HÀNH (2026-11-26, CR-045).** Dự án chỉ dùng một IO Map: [AGV_PLC Interface_List_22_11_2026.xlsx](../PLC_IO_Map/AGV_PLC%20Interface_List_22_11_2026.xlsx) (SHA-256 `405586c9…34da3`). Các workbook cũ (IO Map v2, 10/11, 20/11) **đã ngừng sử dụng và đã gỡ khỏi repository**; nội dung dựa trên chúng trong tài liệu này chỉ còn giá trị lịch sử. IO Map 22/11 **chưa** được kỹ sư PLC xác nhận là hợp đồng IO. [Trạng thái từng tín hiệu](../PLC_IO_Map/README.md).

> **ĐÍNH CHÍNH 2026-11-26 (OW-4).** Tài liệu này phân tích bản workbook cũ (`3c5da74b…`), trong đó `PLC_MissionCompleted` là BOOL `0` thất bại / `1` thành công. Bản đang dùng làm căn cứ (`a291d28f…`) là INT **`1` = Thất bại, `2` = Thành công**. Mọi câu dưới đây coi `1` là thành công **không còn đúng**. Một giá trị INT không đủ để kết luận hoàn thành khi chưa xác minh đúng giao dịch (ADR-0011 D-4). Hợp đồng IO vẫn chờ kỹ sư PLC xác nhận. Xem [báo cáo Phase 2](PLC_PHASE2_REVIEW_2026-11-26.md).

| | |
|---|---|
| **Trạng thái** | `SPECIFICATION – PENDING PLC CLARIFICATION` (báo cáo tác động cho CR-029) |
| **Phiên bản cũ** | `AGV_PLC_IO_Interface_List_10_11_2026.xlsx` (sha256 `936271db…bca53`): 22 tín hiệu, địa chỉ M9000…M9123/D1500…D1610, sơ đồ 9 bước; kèm IO Map v2 (mẫu 23 tín hiệu/trạm, 368 điểm) |
| **Phiên bản mới** | `AGV_PLC Interface_List_20_11_2026.xlsx` (sha256 `3c5da74b…aedddf`): 6 tín hiệu/trạm + `HeartBit` + 5 `Spair`, **không có địa chỉ**, sơ đồ 6 bước |
| **Nhãn** | [N] nguồn · [SL] suy luận · [CX] chưa xác định · [HT] hiện trạng mã/tài liệu · [KT] nguyên tắc đã thống nhất |

## 1. Tóm tắt các khác biệt quan trọng

1. **Cấu trúc**: từ mô tả **theo PLC** (22 tín hiệu, một bộ) sang **mỗi trạm một bộ 6 tín hiệu** + `HeartBit`/`Spair` liệt kê một lần.
2. **Yêu cầu**: từ hai bit `Pickup_Request`/`Delivery_Request` + dữ liệu `Request_ID`, `Rack_Full`, `Rack_Empty`, `Destination_Available`, `Rack_Type` → **một INT** `PLC_MissionRequest` (1 Pick / 2 Drop); **mất toàn bộ dữ liệu đi kèm**.
3. **Phản hồi của phần mềm**: từ `Request_Ack`, `AGV_Assigned`, `AGV_ID`, `AGV_Arrived`, `Transfer_Permit`, `Error_Code` → **một INT** `Mission_Feedback` (0 / AGV1–3 / >10).
4. **Quy trình**: từ **9 bước** → **6 bước**; mất bước "kẹp đã đóng/nhả" (`Station_Ready`) và tín hiệu "WCS đã nhận" độc lập.
5. **Kết quả**: `Rack_Present` ("giao nhận thành công") → `PLC_MissionCompleted` có thêm giá trị **0 = Thất bại**.
6. **Mất tín hiệu an toàn/trạng thái trạm**: `Safety_Ready`, `Station_Online`, `Station_Ready`, `Rack_Present`, `Station_Fault` (IO Map v2).
7. **Địa chỉ**: giao diện cũ có địa chỉ cụ thể (M/D); **mới để trống hoàn toàn** → mọi địa chỉ `NEEDS_CONFIGURATION`.
8. **Heartbeat**: `Heartbeat_SW` (chu kỳ TBD) → `HeartBit` **chu kỳ 1 s, timeout 10 s** (có giá trị cụ thể).
9. **Tên trạm khối `EF-G`**: E1, E2, **G1, G2** (workbook) ↔ E1, E2, **G3, G7** (baseline đã duyệt).
10. **AGV**: cố định AGV1–AGV3 ↔ đội AMR động do RCS chọn (CR-028).

## 2. Đối chiếu từng tín hiệu (cũ → mới)

Cột "Tương đương gần nhất" là **suy luận [SL]** để đánh giá tác động, **không** phải mapping chính thức.

### 2.1. PLC → Software

| # | Tín hiệu cũ (địa chỉ, kiểu) | Ý nghĩa cũ | Tương đương gần nhất trong 20/11 | Ảnh hưởng |
|---|---|---|---|---|
| 1 | `Safety_Ready` (M9000, BOOL) | Điều kiện an toàn cho AGV đến lấy hàng | **Không có** | Mất điều kiện an toàn; PQ-18. Không được tự coi là luôn an toàn |
| 2 | `Pickup_Request` (M9001) | Trạm yêu cầu AGV lấy rack | `PLC_MissionRequest` = 1 (Pick) | Gộp thành INT |
| 3 | `Delivery_Request` (M9002) | Trạm yêu cầu AGV cấp rack | `PLC_MissionRequest` = 2 (Drop) | Gộp thành INT |
| 4 | `Station_Online` (M9003) | Xác nhận cửa rack đã mở (tên không phản ánh nghĩa) | `PLC_ReaddyTranfer` = 1 (cửa **và kẹp**) | Đổi tên, thêm kẹp |
| 5 | `Station_Ready` (M9004) | Xác nhận kẹp đã đóng/nhả | **Không có** (gộp trong `PLC_ReaddyTranfer`?) [SL] | Mất bước 6 cũ; PQ-07 |
| 6 | `Rack_Present` (M9005) | Xác nhận giao/nhận thành công | `PLC_MissionCompleted` = 1 | Thêm giá trị thất bại (0) |
| 7 | `Request_ID` (D1500, STRING 254) | Mã duy nhất chống trùng/đối soát | **Không có** | **Mất khóa chống trùng và đối soát**; PQ-10 |
| 8 | `Rack_Type` (D1520, INT) | Loại rack/hàng | **Không có** | PQ-17 |
| 9 | `Rack_Full` (D1510, INT 1–4 → A1–A4) | Vị trí đang có rack đầy | **Không có** (trạm = bộ tín hiệu) | Vị trí suy ra từ trạm; mất đối chiếu PLC↔tồn kho |
| 10 | `Rack_Empty` (D1512, INT 1–4) | Vị trí đang có rack rỗng | **Không có** | idem |
| 11 | `Destination_Available` (D1514, INT) | Vị trí đích có khả năng nhận rack | **Không có** | Đích do WCS (BP/RouteEngine) quyết định; PQ-17 |
| 12 | `Station_Fault` (IO Map v2, BOOL) | Trạm lỗi | **Không có** | PQ-18 |
| 13 | `Transfer_Complete` (IO Map v2, BOOL) | Hoàn tất chuyển | `PLC_MissionCompleted` [SL] | Tên/nghĩa khác |

### 2.2. Software → PLC

| # | Tín hiệu cũ | Ý nghĩa cũ | Tương đương gần nhất | Ảnh hưởng |
|---|---|---|---|---|
| 14 | `Heartbeat_SW` (M9100) | Heartbeat phần mềm, chu kỳ TBD | `HeartBit` (1 s, timeout 10 s) | Có giá trị; **chưa có bộ ghi trong mã** |
| 15 | `Request_Ack` (M9101) | WCS đã nhận và ghi nhận request | `Mission_Feedback` (một phần) | Mất ack riêng; Q-ORD-1 thay đổi nghĩa |
| 16 | `AGV_Assigned` (M9102) | Đã phân công AGV | `Mission_Feedback` 1–3 | Gộp |
| 17 | `AGV_ID` (D1600) | ID AGV | `Mission_Feedback` 1–3 | AGV1–AGV3 cố định ↔ đội động |
| 18 | `AGV_Arrived` (M9103) | AGV đang xử lý task liên quan trạm | `AGV_RequestTranfer` / `Mission_Feedback` [SL] | Nghĩa khác |
| 19 | `Transfer_Permit` (M9104) | **Chỉ báo lỗi** task AGV ảnh hưởng trạm; WCS reset khi hết lỗi | `Mission_Feedback` > 10 [SL] | Quy tắc reset cũ không còn |
| 20 | `Error_Code` (D1610) | Mã lỗi | `Mission_Feedback` > 10 | Bảng mã chưa có |
| 21 | `AGV_Departure_Permit` (M9120) | Yêu cầu mở cửa rack | `AGV_RequestTranfer` | Gộp với "đã đến" |
| 22 | `AGV_Busy` (M9121) | Xác nhận AGV ở vị trí lấy/trả | `AGV_RequestTranfer` [SL] | Gộp |
| 23 | `AGV_Fault` (M9122) | Hoàn tất lấy/trả **(không phải lỗi)** | `AGV_TranferCompleted` [SL] | Gộp; tên cũ gây hiểu lầm đã biến mất |
| 24 | M9123 (không tên) | AGV đã rời trạm; **PLC reset** | `AGV_TranferCompleted` [SL] | Quy tắc "PLC reset" không còn được nêu |

### 2.3. Tín hiệu **chỉ có** trong 20/11
`PLC_MissionCompleted`=0 (thất bại, không có tương đương rõ), `Mission_Feedback`=0 ("Chưa có AGV nhận"), `PLC_MissionRequest`>10 (lỗi, chỉ E1), `Spair`×5, thêm giá trị `HeartBit` timeout 10 s.

## 3. Khác biệt ngữ nghĩa quan trọng cho thiết kế

| Chủ đề | 10/11 | 20/11 |
|---|---|---|
| Nhận diện yêu cầu | `Request_ID` STRING (≤254) | Không có → chống trùng chưa có cơ chế (PQ-10) |
| Xác định vị trí/loại rack | `Rack_Full/Rack_Empty/Rack_Type` | Chỉ trạm + Pick/Drop (PQ-17) |
| Phạm vi tín hiệu | Theo PLC (22) | Theo trạm (6) + chung (HeartBit/Spair) |
| Ack | Có `Request_Ack` | Chỉ `Mission_Feedback` |
| Bàn giao | 3 xác nhận riêng (cửa mở, kẹp, thành công) + 2 tín hiệu SW (xác nhận ở vị trí; hoàn tất) | 2 xác nhận PLC (`PLC_ReaddyTranfer`, `PLC_MissionCompleted`) + 2 tín hiệu SW |
| An toàn | `Safety_Ready` | Không có |
| Reset | Có quy tắc cho M9123/M9104 | Không có quy tắc nào |
| Thất bại | Không có tín hiệu | `PLC_MissionCompleted` = 0; `Mission_Feedback` > 10 |
| AGV | AGV1/AGV2 (một số) | AGV1–AGV3 cố định |

## 4. Ảnh hưởng đến mã hiện tại (phân loại)

Phân loại: **G** giữ nguyên · **Đ** điều chỉnh · **T** thiết kế lại · **?** chưa đủ thông tin. Không sửa mã trong CR này.

| Thành phần (tệp chính) | Phân loại | Lý do |
|---|---|---|
| PLC Adapter / MC Protocol: `Infrastructure/Plc/Mc/McFrame.cs`, `McTcpClient.cs`, `McPlcEndpoint.cs`, `AddressValidator.cs` | **G** | Codec độc lập tín hiệu; kiểm chứng với thư viện tham chiếu (CR-003). Thông số kết nối vẫn `NEEDS_CONFIGURATION` |
| PLC Worker / Runtime: `Application/Plc/PlcWorker.cs`, `PlcRuntime.cs`, `PlcMonitor.cs`, `PlcExchangeRecorder.cs`, `PlcDiagnosticsCodes.cs` | **G** (**Đ** để bổ sung ghi heartbeat chu kỳ) | Một worker mỗi PLC, hàng đợi ghi tuần tự, kết nối lại, ghi mơ hồ. **Mã hiện không có bộ ghi heartbeat** |
| PLC IO Mapping – catalog/seeder/DB: `Infrastructure/Plc/Catalog/plc-io-catalog.json` (+ `scripts/generate-plc-io-catalog.mjs`), `PlcCatalogSeeder`, `PlcTemplateProvider`, bảng `station_io_points`, `PlcEntities.cs` | **T** | Mẫu 23 tín hiệu → 6 (+ chung); cần phiên bản catalog mới, chiến lược di chuyển điểm IO đã có địa chỉ/phê duyệt (không xóa/đổi tên âm thầm), mô hình PLC-scoped (PQ-19) |
| Cấu hình IO / phê duyệt: `PlcConfigService.cs`, `PlcBulkApproval.cs`, `PlcProvisioningService.cs`, `PlcConfigService.WritePolicy` | **Đ** | Cơ chế duyệt/allow-list giữ; danh sách chặn **theo tên** (`Safety_Ready`, `Transfer_Permit`, `AGV_Departure_Permit`) lỗi thời; phân loại an toàn tín hiệu mới (PQ-18) |
| Station IO handler: `StationIoHandler.cs` | **G** | Theo tên tín hiệu, không phụ thuộc mẫu; cần xử lý điểm PLC-scoped nếu được duyệt |
| Handshake State Machine: `Handshake.cs` (`HandshakeCoordinator.Observe` dùng `Station_Online/Fault`, `Pickup_Request`, `Delivery_Request`, `Transfer_Complete`; `TemplateSignals = 23`; `HandshakePhase`) | **T** | Phụ thuộc trực tiếp tín hiệu cũ; khung quy tắc "set when/reset when" (`HandshakeDefinition`) có thể tái sử dụng nhưng 6 bước phụ thuộc sự kiện nghiệp vụ (**?**) |
| Station Signal Service: `PlcStationSignalService.cs` | **T** | Cạnh lên `Pickup_Request`/`Delivery_Request` + `Request_ID` |
| Tiếp nhận yêu cầu/giao dịch: `Routing/PlcRequestService.cs` (đọc `Request_ID`, `Rack_Full/Empty`, `PlcLocationCodes` 1–4, đối chiếu PLC↔tồn kho `RECON_PLC_INVENTORY_MISMATCH`), `Services/StationRequestProcessor.cs`, `Domain/Intake/StationRequestRecord.cs` (khóa `PLC:Station:RequestID`) | **T** | Mất `Request_ID` và dữ liệu vị trí; khóa chống trùng và xác định Location phải thay |
| Transaction Orchestrator: `Domain/Routing/StationTransaction.cs`, `Routing/StationTransactionService.cs` | **Đ** / **?** | Khung giao dịch theo cổng, treo/tiếp tục, đối soát **giữ**; 9 trạng thái/`HandshakeStep` 1–9 ↔ 6 tín hiệu cần ánh xạ; ràng buộc "một giao dịch hoạt động mỗi cổng" phụ thuộc C-1 |
| Route Engine: `Routing/RouteEngine.cs`, `PlcRequestService`→`IRouteEngine.StartAsync` | **G** (**Đ** nguồn sự kiện) | Engine không ghi PLC; `ConfirmPick/ConfirmDrop` hiện từ simulator/Admin, **chưa có nguồn quan sát PLC thật** (`IPlcObservationSource` không có dữ liệu) |
| Rack và Inventory: `InventoryService`, `Rack`, `Reservation` | **G** | WCS sở hữu; không đổi. Rủi ro: mất khả năng đối chiếu vật lý PLC↔WCS |
| AGV/RCS integration: `TaskOrchestrator`, CR-028 (`RobotCode` theo Task/Leg), `ILegDispatcher` | **?** | `Mission_Feedback` cần RobotCode thực tế và ánh xạ sang 1–3 (PQ-06); nguồn "AGV đã đến" (PQ-23). CR-028 chưa merge |
| Logging, Timeout, Retry, Recovery: `PlcEventEnricher`, `RoutingHostedService`, `PlcRequestService.ResumeOrphansAsync`, `Station Transaction Suspend/Resume` | **G** (khung) / **?** (giá trị) | Cơ chế giữ nguyên; các giá trị timeout/retry chưa có (PQ-11); chỉ `HeartBit` timeout 10 s được nêu |
| API/UI: `PlcEndpoints.cs`, các màn hình *Giám sát PLC*, *Cấu hình địa chỉ IO*, *Giao dịch trạm* | **Đ** | Hiển thị 23 điểm/trạm; cần số điểm/tín hiệu mới; nhãn tiếng Việt/Anh cho tín hiệu mới |
| Config seeding/transfer (CR-026): nhóm cấu hình PLC | **Đ** | Gói cấu hình chứa điểm IO theo tên tín hiệu cũ |
| Test: `HandshakeTests`, `PlcWcsIntegrationTests`, `Cr008b2PlcIntakeTests`, `Cr010*`, `PlcConfigTests`… | **Đ/T** | Xem [Test Specification](PLC_TEST_SPECIFICATION_V2_20_11_2026.md) mục 6 |

## 5. Tài liệu bị ảnh hưởng

| Tài liệu | Trạng thái đề xuất | Ghi chú |
|---|---|---|
| `PLC_COMMUNICATION_SPECIFICATION_DRAFT.md` | **SUPERSEDED (phần tín hiệu, 9 bước, M9xxx)**; §1.2 nguyên tắc, §9 (queue), §11–13 (phục hồi) vẫn là tài liệu tham khảo cho nguyên tắc đã duyệt | Đánh dấu bằng banner; không xóa |
| `PLC_REVIEW_CURRENT_STATE_AND_GAP_ANALYSIS.md` | **SUPERSEDED (cơ sở tín hiệu cũ)** | Hiện trạng mã vẫn đúng một phần |
| `PLC_SPEC_OPEN_QUESTIONS.md` | **SUPERSEDED một phần**: câu hỏi gắn với M9101/M9120/M9122/M9123/Rack_Full… được thay bằng `PQ-nn`; Q-ORD-*, Q-LEG-1, Q-GATE-*, Q-MLEG-*, Q-BUF-1 (không phụ thuộc tín hiệu) **vẫn mở** | Không xóa |
| `PLC_BUSINESS_PROCESS_AND_TRANSACTION_MODEL_DRAFT.md` | **Hiệu lực một phần**: mô hình Task/Leg/Station Transaction giữ; mô tả tín hiệu/9 bước thay bằng tài liệu mới | Banner |
| `MC_PROTOCOL_REFERENCE_ANALYSIS.md` | **Còn hiệu lực** (không phụ thuộc tín hiệu) | Không đổi |
| ADR-0004 (kiến trúc giao tiếp PLC) | **Còn hiệu lực về kiến trúc**; #6 (danh sách chặn theo tên), #8 (khóa `Request_ID`), #5 (cơ chế duyệt đã được CR-019 đơn giản hóa) lỗi thời | Ghi chú trạng thái; chờ ADR mới sau khi duyệt CR-029 |
| ADR-0008 (Station Transaction theo cổng) | **Còn hiệu lực**; C-1 là điểm xung đột với nguyên tắc "một giao dịch mỗi PLC" | Chờ quyết định |
| `specification/integration-requirements.md` §5.2–5.10, `process-flows.md` | Ghi chú tham chiếu giao diện mới; **không sửa nội dung đã duyệt** khi CR-029 chưa duyệt | |
| CLAUDE.md §7.1–7.4 (IO Map v2 là tham chiếu chính thức; E1,E2,G3,G7) | **Cần chủ dự án duyệt sửa** (C-3, C-4) | Không tự sửa |
| `PLC_IO_Map/AGV_PLC_IO_Interface_List_10_11_2026.xlsx`, IO Map v2 `.xlsx` | Giữ nguyên làm lịch sử (Superseded) | Không xóa; file IO Map v2 đang bị xóa trong working tree — **ngoài phạm vi, không đưa vào commit** |

## 6. Rủi ro chính

| # | Rủi ro | Mức | Giảm thiểu |
|---|---|---|---|
| R-1 | Không có `Request_ID` → xử lý trùng hoặc bỏ sót yêu cầu | Cao | PQ-02/03/10 trước mọi mã |
| R-2 | Không có `Safety_Ready` → WCS gửi AGV/mở cửa khi trạm không an toàn | Cao (an toàn) | PQ-18; không tự thay thế bằng điều kiện suy diễn |
| R-3 | `AGV_RequestTranfer` gộp "đã đến" + "mở cửa/kẹp" → lệnh chuyển động ghi nhầm | Cao (an toàn) | Commissioning chỉ-đọc; duyệt từng điểm; không ghi để thử |
| R-4 | `PLC_MissionCompleted`=0 trùng trạng thái nghỉ → thất bại bị bỏ sót hoặc báo giả | Cao | PQ-09 |
| R-5 | Mất đối chiếu PLC↔tồn kho; tồn kho lệch không phát hiện sớm | Trung bình | Chính sách đối soát WCS; PQ-17 |
| R-6 | `Mission_Feedback` AGV1–3 sai/không ánh xạ được với đội AMR động | Trung bình | PQ-06; không mặc định AGV1 |
| R-7 | Xung đột "một giao dịch mỗi PLC" ↔ ADR-0008 | Trung bình | C-1 |
| R-8 | Tên trạm G1/G2 ↔ G3/G7 → cấu hình sai trạm | Trung bình | PQ-20; không đổi cấu hình |
| R-9 | Bỏ `HeartBit` hoặc chu kỳ sai → PLC coi WCS mất kết nối | Trung bình | PQ-19; bộ ghi heartbeat tuân chính sách ghi |
| R-10 | Dữ liệu IO hiện có (địa chỉ/phê duyệt) bị mất khi đổi catalog | Trung bình | Không xóa/đổi tên âm thầm; kế hoạch di chuyển |

## 7. Thứ tự triển khai code đề xuất (giai đoạn sau, **chưa bắt đầu**)

Điều kiện bắt đầu: CR-029 được duyệt, có kế hoạch chuyển đổi, và PQ-01…PQ-10, PQ-18, PQ-19 có câu trả lời.

1. **Quyết định kiến trúc**: C-1 (một giao dịch mỗi PLC vs mỗi cổng), C-2 (Mission_Feedback ↔ đội AMR), C-3/C-4 (CLAUDE.md, tên trạm) → ADR mới.
2. **Catalog/mapping**: phiên bản catalog mới (6 tín hiệu + PLC-scoped), nhập có kiểm soát, giữ dữ liệu cũ; UI hiển thị. *Không* tự gán địa chỉ.
3. **Commissioning chỉ-đọc**: đọc `PLC_MissionRequest`, `PLC_ReaddyTranfer`, `PLC_MissionCompleted` trên simulator rồi trên PLC thật (khi địa chỉ có).
4. **Quan sát & chống trùng**: thay `PlcStationSignalService`/`PlcRequestService`/khóa chống trùng theo cơ chế được xác nhận (PQ-10), tạo giao dịch trạm; không ghi PLC.
5. **Ánh xạ giao dịch**: máy trạng thái Station Transaction ↔ 6 sự kiện; nguồn xác nhận pick/drop; nguồn "AGV đã đến" từ RCS (cần CR-028).
6. **Ghi tín hiệu (sau duyệt kỹ sư PLC)**: `HeartBit` → `Mission_Feedback` → `AGV_RequestTranfer` → `AGV_TranferCompleted`, mỗi tín hiệu qua duyệt riêng và quy trình thử an toàn.
7. **Lỗi/phục hồi/timeout**: theo giá trị được duyệt (PQ-11, PQ-12, PQ-15).
8. **Test & chuyển đổi**: viết/chỉnh test theo [Test Specification](PLC_TEST_SPECIFICATION_V2_20_11_2026.md); đánh dấu test cũ `Obsolete`; kế hoạch cắt chuyển (không chạy song song hai giao diện trên cùng PLC nếu bên PLC không xác nhận).

## 8. Những gì **không** thay đổi bởi CR này
Mã nguồn, database/migration, Docker/`.env`, IP/cấu hình kết nối PLC, địa chỉ/IO mapping đang chạy, các CR khác (CR-027/CR-028 chưa merge không bị đụng).

## 9. Ảnh hưởng của quyết định D-1…D-4 (2026-11-21, [ADR-0011](../../decisions/ADR/ADR-0011-plc-interface-20-11-2026-architectural-decisions.md)) – đánh giá, **chưa sửa mã**

### 9.1. D-1 – tối đa một Handshake hoạt động mỗi PLC, FIFO riêng từng PLC

| Thành phần | Hiện trạng đã kiểm tra | Ảnh hưởng | Phân loại |
|---|---|---|---|
| **ADR-0008** QĐ 4 ("một giao dịch hoạt động mỗi cổng; không có khóa cấp PLC"), §4 ("một khóa mỗi PLC" bị loại) | Đã triển khai theo cổng | **Bị thay thế một phần** bởi ADR-0011 (khóa/hàng đợi cấp PLC cho Handshake) | Thiết kế lại |
| `StationTransaction` + chỉ mục duy nhất giao dịch hoạt động theo cổng (`StationTransactionConfiguration`) | Một hoạt động mỗi cổng | Cần ràng buộc/khóa **mỗi PLC** (thực thể hàng đợi + khóa hoạt động); có thể migration | Thiết kế lại |
| `StationTransactionService` (`CanOpenAsync`, `OpenForLegAsync`) | Kiểm tra theo cổng | Kiểm tra theo PLC; cấp lượt | Điều chỉnh/thiết kế lại |
| `PlcRequestService.HandleRequestAsync` | Từ chối `PORT_BUSY` khi cổng bận; không có FIFO | Xếp hàng thay vì từ chối; **cần thêm FIFO cấp PLC (chưa có)** | Thiết kế lại |
| `StationRequestProcessor`, `PlcStationSignalService` | Cạnh lên; mỗi lần quét đọc lại | Ghi nhận bền yêu cầu chờ (nếu PLC phát xung); thứ tự nhận ra | Thiết kế lại |
| `HandshakeCoordinator` | Mỗi Station độc lập | Chỉ Station `Active` được ghi/điều phối | Thiết kế lại |
| RouteEngine (`DispatchLegAsync`, `CanOpenAsync` ở cổng nguồn/đích) | Chặng chờ `PORT_BUSY` theo cổng | Chặng chờ theo PLC; tương tác PER_AGV/RCS | Điều chỉnh |
| Test `Cr010MultiLegTests.Test07` (hai cổng cùng PLC có hai giao dịch hoạt động), `Test09`, `Cr008b2PlcIntakeTests`, `PlcWcsIntegrationTests` | Mô tả mô hình theo cổng | Viết lại/đổi kỳ vọng theo D-1 và định nghĩa cửa sổ | Điều chỉnh |
| UI giao dịch trạm / giám sát PLC | Hiển thị theo cổng | Hiển thị hàng đợi và lượt của PLC | Điều chỉnh |
| **Không đổi** | Các PLC độc lập, song song; worker mỗi PLC | – | Giữ nguyên |

Rủi ro: dùng nguyên vòng đời Station Transaction (mở từ lúc dispatch) làm "Handshake hoạt động" sẽ tuần tự hóa cả hành trình AMR trong một PLC → giảm thông lượng; cần định nghĩa cửa sổ (PQ-26) trước khi thiết kế. Không được ghi nhận rằng mã hiện hỗ trợ FIFO cấp PLC.

### 9.2. D-2 – `Mission_Feedback` và đội AMR động
`TaskOrchestrator`/CR-028 (`RobotCode` theo Task/Leg) giữ nguyên và là nguồn `RobotCode`; **không** thêm ánh xạ AGV1–3. Cần: giao diện phản hồi mới (trạng thái), tuỳ chọn vùng `RobotCode` thật (PQ-29), ghi tín hiệu phản hồi bởi bộ ghi mới (chưa có) chỉ sau khi được duyệt. Ảnh hưởng mã: bộ ghi `Mission_Feedback` (mới), chính sách ghi (tên mới), UI (nhãn). Phụ thuộc CR-028 được duyệt/merge.

### 9.3. D-3 – CLAUDE.md
Đã cập nhật (§7 ghi chú trạng thái, §7.1 đánh dấu baseline cũ, §7.8). Catalog `plc-io-catalog.json` và ADR-0004 vẫn mô tả mẫu cũ (xem mục 4–5); không đổi cho đến CR triển khai.

### 9.4. D-4 – đối chiếu rack vật lý
Rack có mặt, danh tính rack, định danh giao dịch duy nhất, điều kiện an toàn: **chưa có nguồn** trong giao diện 20/11. Ảnh hưởng: `PlcRequestService` (đối chiếu PLC↔tồn kho `RECON_PLC_INVENTORY_MISMATCH` hiện dùng `Rack_Full/Rack_Empty`), điều kiện xác nhận bàn giao (`ConfirmDropAsync` – WCS), RouteEngine, `InventoryService` (không đổi nguyên tắc: WCS sở hữu), giao diện/thiết bị cung cấp dữ liệu rack (chưa xác định; ứng viên: tín hiệu PLC bổ sung, đầu đọc/cảm biến, `podCode` do RCS báo, quét/xác nhận của người vận hành – chưa chọn). Không thể triển khai xác nhận bàn giao dựa chỉ vào `PLC_MissionCompleted`.

### 9.5. Bảng giải quyết xung đột
C-1 → D-1 (đã giải quyết, chưa triển khai) · C-2 → D-2 · C-3 → D-3 (đã thực hiện) · **C-4 vẫn mở (PQ-20)** · C-5 → D-4 (như yêu cầu; nguồn dữ liệu mở).

### 9.6. Điều chỉnh thứ tự triển khai (mục 7)
Bước 1 (quyết định kiến trúc) **đã hoàn tất** (ADR-0011). Thêm các điều kiện trước bước 4–6: PQ-26 (cửa sổ Handshake), PQ-27/PQ-28 (nguồn dữ liệu rack), PQ-29/IF-P3 (giao diện phản hồi), PQ-10 (định danh giao dịch). Thiết kế bộ điều phối cấp PLC + hàng đợi bền là hạng mục mới của bước 4–5.
