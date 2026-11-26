# CR-029 – PLC Interface & Handshake Specification update (giao diện 20/11/2026)

> **ĐÍNH CHÍNH 2026-11-26 (OW-4).** `PLC_MissionCompleted`: **`1` = Failure, `2` = Success** (kiểu INT). Các câu trong tài liệu này viết BOOL `0` thất bại / `1` thành công, hoặc "`PLC_MissionCompleted = 1` là điều kiện cần", thuộc bản workbook cũ và **không còn đúng**. Điều kiện cần là `2`, và chỉ khi đã xác minh đúng giao dịch (ADR-0011 D-4). Trạng thái CR giữ `PENDING_APPROVAL`.

> **IO MAP HIỆN HÀNH (2026-11-26, CR-045).** Dự án chỉ dùng một IO Map: [AGV_PLC Interface_List_22_11_2026.xlsx](../02_Interface_Specification/PLC_IO_Map/AGV_PLC%20Interface_List_22_11_2026.xlsx) (SHA-256 `405586c9…34da3`). Các workbook cũ (IO Map v2, 10/11, 20/11) **đã ngừng sử dụng và đã gỡ khỏi repository**; nội dung dựa trên chúng trong tài liệu này chỉ còn giá trị lịch sử. IO Map 22/11 **chưa** được kỹ sư PLC xác nhận là hợp đồng IO. [Trạng thái từng tín hiệu](../02_Interface_Specification/PLC_IO_Map/README.md).

| | |
|---|---|
| **Trạng thái** | **PENDING_APPROVAL** – chờ xác nhận của bên PLC (**không** phải `APPROVED`, **không** phải `READY FOR IMPLEMENTATION`). Quyết định kiến trúc D-1…D-4 đã được chủ dự án chốt (2026-11-21) và ghi ở [ADR-0011](../decisions/ADR/ADR-0011-plc-interface-20-11-2026-architectural-decisions.md); **chưa triển khai mã**. `SPECIFICATION UPDATE COMPLETED – PENDING PLC CLARIFICATION` |
| **Nguồn** | `docs/02_Interface_Specification/PLC_IO_Map/AGV_PLC Interface_List_20_11_2026.xlsx` (sheets `IO Matrix`, `Trang_tính1`; sha256 `3c5da74b310fec46aed7d5a939ec60e8743d80c1b61c098c0b00ae48dd9aeddf`), nhận ngày 2026-11-20/21 |
| **Thay thế (cơ sở giao diện)** | `AGV_PLC_IO_Interface_List_10_11_2026.xlsx` – nền của CR-010 (9 bước) và của mẫu 23 tín hiệu trong IO Map v2 (CR-002) |
| **Nhánh** | `feature/cr-029-plc-interface-20-11-2026` (từ `main`; **độc lập** với CR-027/CR-028) |
| **Phạm vi thay đổi** | Chỉ tài liệu. Không mã nghiệp vụ, không database/migration, không Docker/`.env`, không IP/địa chỉ/IO mapping đang chạy. |

## 1. Statements mới/đổi (đọc từ nguồn)

Toàn bộ chi tiết có dẫn chiếu ô: [Interface Specification](../02_Interface_Specification/PLC_20_11_2026/PLC_INTERFACE_SPECIFICATION_V2_20_11_2026.md).

- Mỗi trạm (E1, E2, G1, G2 trong khối `EF-G`/192.168.10.131) có 6 tín hiệu: `PLC_MissionRequest` (INT 0/1 Pick/2 Drop/>10), `PLC_ReaddyTranfer` (BOOL), `PLC_MissionCompleted` (BOOL 0 thất bại/1 thành công), `AGV_RequestTranfer` (BOOL), `AGV_TranferCompleted` (BOOL), `Mission_Feedback` (INT 0/AGV1–3/>10); một `HeartBit` (1 s, timeout 10 s) và 5 `Spair` liệt kê một lần.
- Địa chỉ thanh ghi: **trống hoàn toàn**. Mapping PLC/trạm còn lại: "tương tự" (không liệt kê).
- Quy trình 6 bước (`Trang_tính1`) thay cho 9 bước.
- Mất: `Request_ID`, `Rack_Full/Rack_Empty/Rack_Type/Destination_Available`, `Safety_Ready`, `Station_Online/Station_Ready/Rack_Present`, `Request_Ack`, `AGV_Assigned/AGV_ID/AGV_Arrived`, `Transfer_Permit`, `Error_Code`, M9123, các quy tắc reset.

## 2. Xung đột cần quyết định (không tự giải quyết)

| ID | Xung đột | Hiện trạng |
|---|---|---|
| C-1 | "Mỗi PLC tối đa một giao dịch; FIFO theo PLC" (yêu cầu 2026-11-17) ↔ ADR-0008 "một giao dịch hoạt động mỗi cổng/Trạm" (2026-11-18, đã triển khai) ↔ bộ tín hiệu riêng từng trạm (20/11) | **Đã giải quyết bằng D-1**: tối đa một Handshake hoạt động mỗi PLC, FIFO riêng từng PLC. **Chưa triển khai** (mã hiện chưa có FIFO cấp PLC); ADR-0008 quyết định 4 bị thay thế một phần; ranh giới "Handshake hoạt động" chưa xác định (PQ-26) |
| C-2 | RCS tự chọn AMR trong đội động (CR-028) ↔ `Mission_Feedback` cố định AGV1–AGV3 | **Đã giải quyết bằng D-2**: không dùng AGV1–3 làm định danh AMR; WCS lưu `RobotCode` thật; `Mission_Feedback` = trạng thái tiếp nhận/xử lý; đề xuất giao diện phản hồi mới + vùng dữ liệu `RobotCode` (PQ-06, PQ-29) |
| C-3 | CLAUDE.md §7.1 coi IO Map v2 (mẫu 23 tín hiệu) là tham chiếu IO chính thức ↔ workbook 20/11 thay cơ sở giao diện | **Đã giải quyết bằng D-3**: CLAUDE.md mục 7 đã cập nhật (§7 ghi chú trạng thái, §7.1 đánh dấu baseline cũ, §7.8 mới); IO Map v2 = baseline cũ; workbook 20/11 = giao diện mới **chưa được duyệt**, không phải mapping đã phê duyệt |
| C-4 | CLAUDE.md §7.2: PLC-04 = E1, E2, G3, G7 ↔ workbook: E1, E2, G1, G2 | **Vẫn mở** – D-1…D-4 không quyết định (PQ-20); cấu hình giữ nguyên |
| C-5 | Mất đối chiếu vật lý PLC↔tồn kho (không còn dữ liệu rack/`Rack_Present`) | **Đã giải quyết bằng D-4 dưới dạng yêu cầu**: **không chấp nhận** mất khả năng đối chiếu; cần xác nhận rack có mặt, nhận diện rack khi quy trình yêu cầu, định danh/chống trùng giao dịch, điều kiện an toàn; nếu PLC không cung cấp thì xác định thiết bị/hệ thống khác (PQ-27, PQ-28). `PLC_MissionCompleted=1` không đủ làm bằng chứng |

## 3. Tài liệu đã tạo (thư mục `docs/02_Interface_Specification/PLC_20_11_2026/`)

| # | Tài liệu |
|---|---|
| 1 | `PLC_INTERFACE_SPECIFICATION_V2_20_11_2026.md` |
| 2 | `PLC_HANDSHAKE_SPECIFICATION_V2_20_11_2026.md` (sơ đồ dựng lại; phân loại [N]/[SL]/[CX]) |
| 3 | `PLC_IO_MAPPING_SPECIFICATION_V2_20_11_2026.md` |
| 4 | `PLC_COMMUNICATION_ARCHITECTURE_V2_20_11_2026.md` |
| 5 | `PLC_TRANSACTION_LIFECYCLE_V2_20_11_2026.md` |
| 6 | `PLC_ERROR_HANDLING_AND_RECOVERY_V2_20_11_2026.md` |
| 7 | `PLC_TEST_SPECIFICATION_V2_20_11_2026.md` |
| 8 | `PLC_INTERFACE_CHANGE_IMPACT_ANALYSIS_20_11_2026.md` (đối chiếu cũ/mới, thành phần mã, tài liệu, rủi ro, thứ tự triển khai) |
| 9 | `PLC_VENDOR_CLARIFICATION_QUESTIONS_20_11_2026.md` (PQ-01…PQ-25 + D-1…D-4) |

Cũng thêm `PLC_IO_Map/README.md` (trạng thái các workbook) và `README.md` của thư mục mới.

## 4. Tài liệu cũ (không xóa; đánh dấu)

Banner `SUPERSEDED / OBSOLETE (một phần)` ở đầu: `PLC_COMMUNICATION_SPECIFICATION_DRAFT.md`, `PLC_REVIEW_CURRENT_STATE_AND_GAP_ANALYSIS.md`, `PLC_SPEC_OPEN_QUESTIONS.md`, `PLC_BUSINESS_PROCESS_AND_TRANSACTION_MODEL_DRAFT.md`; ghi chú trạng thái ở `ADR-0004`. Không sửa nội dung bên dưới banner. `MC_PROTOCOL_REFERENCE_ANALYSIS.md`, ADR-0008 **còn hiệu lực**. Các tệp `specification/*.md` đã duyệt **không bị sửa** (chờ phê duyệt CR). Hai workbook cũ giữ nguyên làm lịch sử.

## 5. Ảnh hưởng (tóm tắt – chi tiết ở Impact Analysis)

Giữ nguyên: codec MC, `PlcWorker`/runtime, `StationIoHandler`, Rack/Inventory, khung logging/phục hồi. Điều chỉnh: chính sách ghi theo tên, giao dịch trạm, UI, cấu hình/gói chuyển. **Thiết kế lại**: catalog/mapping 23→6 tín hiệu, `HandshakeCoordinator`, `PlcStationSignalService`, `PlcRequestService`/khóa chống trùng. Chưa đủ thông tin: ghi `Mission_Feedback` (AMR), hàng đợi theo PLC, giá trị timeout/retry. API/Database/Migration/RCS/PLC address: **chưa thay đổi**; mọi thay đổi tương lai cần CR triển khai riêng.

## 6. Quy trình tiếp theo

1. ~~Chủ dự án xem xét Impact Analysis và các quyết định D-1…D-4.~~ **Đã chốt D-1…D-4 (2026-11-21)** – xem mục 8.
2. Gửi `PLC_VENDOR_CLARIFICATION_QUESTIONS` cho kỹ sư PLC; ghi lại câu trả lời (không tự suy ra).
3. Sau khi có câu trả lời + phê duyệt: tạo ADR mới, sửa CLAUDE.md §7 (nếu được duyệt), cập nhật các tài liệu đặc tả đã duyệt, rồi mới lập kế hoạch chuyển đổi mã (thứ tự ở Impact Analysis §7).
4. Không triển khai mã cho đến khi có đánh giá tác động và kế hoạch chuyển đổi được duyệt.

## 7. Phạm vi **không** bao gồm
Mã nguồn, database, migration, Docker, `.env`, cấu hình kết nối PLC, địa chỉ/IO mapping đang chạy, CR-027/CR-028, file IO Map v2 đang bị xóa trong working tree (không đưa vào commit).

## 8. Quyết định kiến trúc D-1…D-4 (chủ dự án, 2026-11-21) – ADR-0011

Chi tiết và hệ quả: [ADR-0011](../decisions/ADR/ADR-0011-plc-interface-20-11-2026-architectural-decisions.md). Chỉ ghi nhận quyết định và cập nhật tài liệu; **không** sửa mã, migration, database, Docker, `.env`, IP/địa chỉ PLC, workbook nguồn; **không** merge/deploy; CR-029 **không** chuyển sang Approved / Ready for Implementation.

| ID | Quyết định | Ảnh hưởng kỹ thuật cần ở CR triển khai kế tiếp (chưa làm) |
|---|---|---|
| **D-1** | Tối đa **một giao dịch Handshake hoạt động mỗi PLC**; mỗi PLC một **FIFO queue** riêng; các PLC độc lập và song song; các Station cùng PLC **không** Handshake đồng thời | Thiết kế lại điều phối cấp PLC; **mã hiện chưa có FIFO cấp PLC** (`PlcRequestService` từ chối `PORT_BUSY`, DB chỉ ràng buộc một giao dịch hoạt động mỗi **cổng**); ảnh hưởng ADR-0008 (QĐ 4 thay thế một phần), `StationTransaction` + chỉ mục duy nhất, `StationTransactionService`, `PlcRequestService`, `StationRequestProcessor`, `HandshakeCoordinator`, RouteEngine, UI, test (`Cr010MultiLegTests.Test07`…); có thể cần thực thể hàng đợi + migration; ranh giới cửa sổ Handshake (PQ-26) |
| **D-2** | Không dùng AGV1/2/3 làm định danh AMR; RCS quản lý đội động và chọn AMR; WCS lưu `RobotCode` thật; `Mission_Feedback` = trạng thái tiếp nhận/xử lý, **không** mặc định là `RobotCode`; không sửa enum workbook; đề xuất giao diện phản hồi mới + vùng dữ liệu `RobotCode` nếu PLC cần | Không ghi 1–3 làm định danh; thiết kế giao diện phản hồi mới với PLC; phụ thuộc CR-028 (`RobotCode` theo Task/Leg) |
| **D-3** | Cập nhật CLAUDE.md mục 7: 20/11 = giao diện mới chờ xác nhận/phê duyệt; IO Map v2 = baseline cũ chỉ tham khảo; workbook 20/11 **không** là mapping đã duyệt; bảo toàn lịch sử | **Đã thực hiện** (CLAUDE.md §7 ghi chú, §7.1, §7.8) |
| **D-4** | **Không chấp nhận** mất đối chiếu rack vật lý ↔ Inventory: cần xác nhận rack có mặt, nhận diện rack khi quy trình yêu cầu, định danh/chống trùng giao dịch, điều kiện an toàn/interlock; nếu PLC không có dữ liệu nhận diện rack thì xác định thiết bị/hệ thống khác; `PLC_MissionCompleted=1` **không đủ** | Mở rộng giao diện hoặc thêm nguồn dữ liệu (tín hiệu PLC, đầu đọc/cảm biến, mã rack từ RCS `podCode`, quét/xác nhận của người vận hành – chưa chọn); điều kiện xác nhận bàn giao ở WCS; tương thích nguyên tắc CR-028 §13 |

### Tài liệu bổ sung/cập nhật theo các quyết định
ADR-0011 (mới); CLAUDE.md §7; `PLC_VENDOR_CLARIFICATION_QUESTIONS_20_11_2026.md` (tách: câu hỏi chờ xác nhận / quyết định đã chốt / đề xuất thay đổi giao diện cần PLC phê duyệt; thêm PQ-26…PQ-29); Interface Specification §7.1; Architecture §6; Transaction Lifecycle §7; Impact Analysis §9; Test Specification §9; sổ CR, CHANGELOG, IMPACT_MATRIX, TRACEABILITY.

### Còn chờ bên PLC
Địa chỉ thanh ghi; mức/xung và reset từng tín hiệu; ý nghĩa giá trị (kể cả lỗi); ranh giới "Handshake hoạt động" và cách giữ yêu cầu của Station đang chờ (PQ-26); định danh giao dịch duy nhất (PQ-10); dữ liệu rack có mặt/danh tính (PQ-27, PQ-28); an toàn/interlock (PQ-18); giao diện phản hồi mới và vùng `RobotCode` (PQ-06, PQ-29); heartbeat; tên trạm G1/G2 ↔ G3/G7 (PQ-20, **vẫn mở**); timeout/retry/khởi động lại.
