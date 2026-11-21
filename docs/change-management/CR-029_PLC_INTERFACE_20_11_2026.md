# CR-029 – PLC Interface & Handshake Specification update (giao diện 20/11/2026)

| | |
|---|---|
| **Trạng thái** | **PENDING_APPROVAL** – đặc tả/tài liệu đã lập; **chưa triển khai mã**. `SPECIFICATION UPDATE COMPLETED – PENDING PLC CLARIFICATION` |
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
| C-1 | "Mỗi PLC tối đa một giao dịch; FIFO theo PLC" (yêu cầu 2026-11-17) ↔ ADR-0008 "một giao dịch hoạt động mỗi cổng/Trạm" (2026-11-18, đã triển khai) ↔ bộ tín hiệu riêng từng trạm (20/11) | Chưa chọn (D-1) |
| C-2 | RCS tự chọn AMR trong đội động (CR-028) ↔ `Mission_Feedback` cố định AGV1–AGV3 | Chưa chọn (D-2) |
| C-3 | CLAUDE.md §7.1 coi IO Map v2 (mẫu 23 tín hiệu) là tham chiếu IO chính thức ↔ workbook 20/11 thay cơ sở giao diện | **CLAUDE.md không được sửa khi chưa có phê duyệt** (D-3) |
| C-4 | CLAUDE.md §7.2: PLC-04 = E1, E2, G3, G7 ↔ workbook: E1, E2, G1, G2 | Cấu hình giữ nguyên (PQ-20) |
| C-5 | Mất đối chiếu vật lý PLC↔tồn kho (không còn dữ liệu rack/`Rack_Present`) | Chủ dự án cần chấp nhận hoặc yêu cầu bổ sung tín hiệu (D-4) |

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

1. Chủ dự án xem xét Impact Analysis và các quyết định D-1…D-4.
2. Gửi `PLC_VENDOR_CLARIFICATION_QUESTIONS` cho kỹ sư PLC; ghi lại câu trả lời (không tự suy ra).
3. Sau khi có câu trả lời + phê duyệt: tạo ADR mới, sửa CLAUDE.md §7 (nếu được duyệt), cập nhật các tài liệu đặc tả đã duyệt, rồi mới lập kế hoạch chuyển đổi mã (thứ tự ở Impact Analysis §7).
4. Không triển khai mã cho đến khi có đánh giá tác động và kế hoạch chuyển đổi được duyệt.

## 7. Phạm vi **không** bao gồm
Mã nguồn, database, migration, Docker, `.env`, cấu hình kết nối PLC, địa chỉ/IO mapping đang chạy, CR-027/CR-028, file IO Map v2 đang bị xóa trong working tree (không đưa vào commit).
