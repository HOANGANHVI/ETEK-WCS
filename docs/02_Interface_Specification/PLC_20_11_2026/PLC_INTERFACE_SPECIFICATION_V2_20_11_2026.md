# PLC Interface Specification – Phiên bản 2 (cơ sở: giao diện 20/11/2026)

| | |
|---|---|
| **Trạng thái** | `SPECIFICATION – PENDING PLC CLARIFICATION` (chưa phải baseline đã duyệt; xem CR-029) |
| **Thay thế** | Cơ sở giao diện `AGV_PLC_IO_Interface_List_10_11_2026.xlsx` (xem mục 8 và [Change Impact Analysis](PLC_INTERFACE_CHANGE_IMPACT_ANALYSIS_20_11_2026.md)) |
| **Nguồn duy nhất** | `docs/02_Interface_Specification/PLC_IO_Map/AGV_PLC Interface_List_20_11_2026.xlsx` – sheet `IO Matrix` và `Trang_tính1` (sha256 `3c5da74b310fec46aed7d5a939ec60e8743d80c1b61c098c0b00ae48dd9aeddf`) |
| **Phạm vi** | Chỉ đặc tả/tài liệu. Không có thay đổi mã, database, migration, Docker, `.env`, IP PLC hay mapping đang chạy. |
| **Đọc cùng** | [Handshake](PLC_HANDSHAKE_SPECIFICATION_V2_20_11_2026.md) · [IO Mapping](PLC_IO_MAPPING_SPECIFICATION_V2_20_11_2026.md) · [Kiến trúc](PLC_COMMUNICATION_ARCHITECTURE_V2_20_11_2026.md) · [Vòng đời giao dịch](PLC_TRANSACTION_LIFECYCLE_V2_20_11_2026.md) · [Lỗi & phục hồi](PLC_ERROR_HANDLING_AND_RECOVERY_V2_20_11_2026.md) · [Test](PLC_TEST_SPECIFICATION_V2_20_11_2026.md) · [Câu hỏi cho bên PLC](PLC_VENDOR_CLARIFICATION_QUESTIONS_20_11_2026.md) |

## Nhãn độ chắc chắn (dùng cho mọi mệnh đề trong bộ tài liệu này)

| Nhãn | Nghĩa |
|---|---|
| **[N]** | **Nguồn** – được ghi rõ trong workbook 20/11 (có dẫn chiếu ô). Giữ nguyên chữ, kể cả chính tả. |
| **[SL]** | **Suy luận** từ trình tự mô tả/ngữ cảnh. *Không* phải yêu cầu chính thức; chỉ dùng để đặt câu hỏi. |
| **[CX]** | **Chưa xác định** – workbook không nói; phải được bên PLC xác nhận (mã `PQ-nn` trong danh sách câu hỏi). Không được tự điền. |
| **[HT]** | **Hiện trạng** của mã/tài liệu repository (đã kiểm tra). |
| **[KT]** | **Nguyên tắc kiến trúc đã thống nhất** (CLAUDE.md, ADR). |

## 1. Phạm vi và nguyên tắc

1. Tên tín hiệu giữ **nguyên văn nguồn**, không sửa chính tả: `PLC_MissionRequest`, `PLC_ReaddyTranfer`, `PLC_MissionCompleted`, `AGV_RequestTranfer`, `AGV_TranferCompleted`, `Mission_Feedback`, `HeartBit`, `Spair` **[N]**.
2. Cột "Địa chỉ thanh ghi" của workbook **để trống** ở mọi dòng **[N]** → `Actual_PLC_Address = NEEDS_CONFIGURATION`. Không tự gán, không suy ra từ giao diện 10/11 (M9xxx/Dxxxx) hay từ offset **[KT]**.
3. Các tín hiệu của giao diện cũ **không** được coi là còn hiệu lực nếu workbook 20/11 không nhắc tới (xem mục 6).
4. Khóa nhận diện mọi điểm IO: `PLC_ID + Station_ID + Signal_Name` **[KT]** (CLAUDE.md §7.3).
5. Giữ các nguyên tắc: 4 PLC độc lập chạy song song; không hardcode IP; không đổi cấu hình kết nối hiện có; RCS quản lý đội AMR và chọn AMR, WCS quản lý nghiệp vụ và giao dịch **[KT]**. Mâu thuẫn giữa tài liệu mới và các nguyên tắc này được **ghi nhận ở mục 7, không tự giải quyết**.

## 2. Cấu trúc nguồn

Sheet `IO Matrix` (A1 "AGV – PLC Interface"): cột `PLC | Station | Nhóm | Tên tín hiệu | Chiều tín hiệu | Nguồn | Đích | Địa chỉ thanh ghi | Kiểu dữ liệu | Nội dung | Ý nghĩa` (hàng 3).

- Khối PLC `EF-G` / `192.168.10.131` (ô A4, gộp A4:A33): trạm **E1** (hàng 4–9), **E2** (10–15), **G1** (16–21), **G2** (22–27) **[N]**.
- Mỗi trạm có **6 tín hiệu nhóm `Command`** (mục 3.1).
- Sau trạm cuối, **một lần** (không lặp theo trạm): `HeartBit` (nhóm `Status`, hàng 28) và **5 `Spair`** (nhóm `Spair`, hàng 29–33) **[N]**.
- Ô A35: "Mapping tương tự với các PLC và trạm còn lại" **[N]** – các PLC/trạm khác **không** được liệt kê; mapping của chúng chỉ "tương tự" **[CX]** (PQ-20).

Sheet `Trang_tính1`: sơ đồ trình tự hai làn `AGV` (cột D, phần mềm) ↔ `PLC` (cột G) với 6 mũi tên và chú thích; xem [Handshake](PLC_HANDSHAKE_SPECIFICATION_V2_20_11_2026.md).

## 3. Danh mục tín hiệu

### 3.1. Mỗi trạm (E1, E2, G1, G2 – lặp giống hệt) **[N]**

| # | Tín hiệu (nguyên văn) | Chiều | Kiểu | Nội dung (cột J) | Ý nghĩa (cột K) |
|---|---|---|---|---|---|
| 1 | `PLC_MissionRequest` | PLC → Software | INT | `0: Không nhiệm vụ, 1: Pick, 2:Drop, >10 lỗi` (**chỉ trên dòng E1**, J4; dòng E2/G1/G2 là `0: Không nhiệm vụ, 1: Pick, 2:Drop`, không có ">10 lỗi") | "PLC gửi yêu cầu" |
| 2 | `PLC_ReaddyTranfer` | PLC → Software | BOOL | `0: Đóng, 1:Mở` | "Xác nhận đã mở cửa Rack, mở Kẹp" |
| 3 | `PLC_MissionCompleted` | PLC → Software | BOOL | `0: Thất Bại, 1:Thành Công` | "Xác nhận Giao/Nhận thành công" |
| 4 | `AGV_RequestTranfer` | Software → PLC | BOOL | `0:Rời đi, 1:Đã đến` | "Yêu cầu mở cửa mở kẹp ,AGV ở vị trí trước cửa" |
| 5 | `AGV_TranferCompleted` | Software → PLC | BOOL | `0:Rời đi, 1:Đã đến` (giống hệt tín hiệu 4) | "AGV đã lấy Rack và ra ngoài" |
| 6 | `Mission_Feedback` | Software → PLC | INT | `0:Chưa có AGV nhận, 1:AGV1, 2:AGV2, 3:AGV3, >10 Lỗi` | "Trạng thái nhiệm vụ của AGV" |

Nguồn/đích ghi trong workbook: PLC→SW: nguồn "PLC máy/trạm", đích "Phần mềm AGV Interface"; SW→PLC: ngược lại **[N]**.

### 3.2. Dùng chung (một lần sau trạm cuối) **[N]**

| Tín hiệu | Chiều | Kiểu | Nội dung | Ý nghĩa |
|---|---|---|---|---|
| `HeartBit` (nhóm `Status`) | Software → PLC | BOOL | "on/off chu kì 1s" | "Heart bit từ phần mềm xác nhận đã kết nối,timeout 10s" |
| `Spair` ×5 (nhóm `Spair`) | – (không ghi chiều/nguồn/đích) | INT, INT, BOOL, BOOL, BOOL | – | "dự phòng" |

Phạm vi của `HeartBit`/`Spair` (theo PLC hay theo trạm): **workbook chỉ liệt kê một lần sau 4 trạm** → [SL] theo PLC; **chưa được nêu** → [CX] PQ-19, PQ-21. CLAUDE.md §7.3 yêu cầu phân loại tường minh, không âm thầm nhân bản hay gộp.

### 3.3. Tổng số theo PLC

Mỗi PLC mô tả có **4 trạm × 6 + 1 `HeartBit` + 5 `Spair` = 30 dòng** **[N]** (so với 22 tín hiệu của giao diện 10/11, và 23 × 4 = 92 điểm/PLC của IO Map v2).

## 4. Nội dung đã xác định rõ [N]

- Một yêu cầu của trạm được báo bằng **một INT** `PLC_MissionRequest` (1 = Pick, 2 = Drop) – không còn hai bit `Pickup_Request`/`Delivery_Request`.
- Phần mềm phản hồi bằng `Mission_Feedback` (0 chưa có AGV nhận; 1–3 AGV1–AGV3; >10 lỗi).
- Phần mềm báo `AGV_RequestTranfer` khi AGV ở vị trí trước cửa trạm; PLC trả lời `PLC_ReaddyTranfer` = đã mở cửa Rack và mở kẹp.
- Phần mềm báo `AGV_TranferCompleted` khi AGV đã lấy Rack và ra ngoài; PLC xác nhận `PLC_MissionCompleted` (1 thành công / 0 thất bại).
- `HeartBit`: phần mềm bật/tắt chu kỳ 1 s; timeout 10 s.
- Chú thích sơ đồ: `PLC_MissionRequest` "thay đổi theo `PLC_MissionCompleted`" (K9); `PLC_MissionCompleted` "thay đổi theo `Mission_Feedback` và `AGV_TranferCompleted`" (K22) – **không** nói thay đổi thế nào (xem [Handshake](PLC_HANDSHAKE_SPECIFICATION_V2_20_11_2026.md)).

## 5. Điểm không nhất quán / mơ hồ ngay trong tài liệu nguồn

| # | Quan sát | Ô | Xử lý |
|---|---|---|---|
| 1 | `>10 lỗi` của `PLC_MissionRequest` chỉ có ở E1 | J4 so với J10, J16, J22 | Hỏi PQ-05; không áp dụng riêng cho E1 |
| 2 | `AGV_TranferCompleted` dùng nhãn giá trị của `AGV_RequestTranfer` ("0:Rời đi, 1:Đã đến") dù ý nghĩa là "đã lấy Rack và ra ngoài" | J8 | PQ-08 |
| 3 | `PLC_MissionCompleted`: giá trị **0 = Thất bại** trùng với trạng thái nghỉ/chưa xong; không có cách phân biệt "chưa" với "thất bại" | J6 | PQ-09 |
| 4 | `AGV_TranferCompleted` mô tả "đã **lấy** Rack"; không nói gì cho thao tác **Drop** (yêu cầu = 2) | K8 | PQ-16 |
| 5 | `Mission_Feedback` cố định AGV1–AGV3; đội AMR của RCS là động | J9 | PQ-06 |
| 6 | Cột `Địa chỉ thanh ghi` trống toàn bộ | H4:H33 | PQ-01 |
| 7 | Tên trạm khối `EF-G`: **E1, E2, G1, G2**; baseline đã duyệt: PLC-04 = **E1, E2, G3, G7** (CLAUDE.md §7.2; `WCS_BP_Port_PLC_Transaction_Matrix` OP-03 đã ghi sự khác biệt E1/E2/G1/G2 ↔ G3/G7) | B4:B22 | PQ-20; **không** đổi cấu hình đang chạy |
| 8 | Nhóm `Command` cho cả tín hiệu PLC→SW; `HeartBit` thuộc nhóm `Status`, `Spair` nhóm riêng | cột C | Giữ nguyên |

## 6. Phân loại tín hiệu giao diện 10/11 so với 20/11 (tóm tắt)

Chi tiết từng tín hiệu: [Change Impact Analysis](PLC_INTERFACE_CHANGE_IMPACT_ANALYSIS_20_11_2026.md). Nguyên tắc: **tín hiệu cũ vắng mặt trong 20/11 = không còn là phần của giao diện** (không đọc, không ghi) cho đến khi bên PLC xác nhận khác. Mất đáng kể: `Request_ID` (chống trùng/đối soát), `Rack_Full`/`Rack_Empty`/`Rack_Type`/`Destination_Available` (xác định vị trí/loại rack), `Safety_Ready`, `Station_Online`/`Station_Ready`/`Rack_Present`, `Error_Code`, `AGV_ID`. Hậu quả thiết kế: [Vòng đời](PLC_TRANSACTION_LIFECYCLE_V2_20_11_2026.md) và [Lỗi & phục hồi](PLC_ERROR_HANDLING_AND_RECOVERY_V2_20_11_2026.md).

## 7. Điểm mâu thuẫn với nguyên tắc đã thống nhất – ghi nhận, chưa quyết định

| # | Nguyên tắc đã thống nhất | Tài liệu mới / hiện trạng | Ghi nhận |
|---|---|---|---|
| C-1 | "Mỗi PLC chỉ xử lý tối đa **một** giao dịch tại một thời điểm; FIFO theo PLC" (đặc tả nháp 2026-11-17, `PLC_COMMUNICATION_SPECIFICATION_DRAFT` §9.1 theo cách hiểu G-A) | ADR-0008 (chấp nhận 2026-11-18) đã chuyển sang **một giao dịch hoạt động mỗi cổng/Trạm** (ràng buộc DB), hai trạm cùng PLC chạy độc lập; workbook 20/11 cho **mỗi trạm bộ tín hiệu riêng** (hỗ trợ cổng độc lập về mặt vật lý) | Hai cách hiểu cùng tồn tại trong repository. **Cần quyết định của chủ dự án + xác nhận PLC (PQ-13, PQ-14, PQ-22).** Chưa thay đổi gì. |
| C-2 | RCS tự chọn AMR trong đội động (CR-028) | `Mission_Feedback` chỉ biểu diễn AGV1–AGV3 | PQ-06 |
| C-3 | CLAUDE.md §7.1: IO Map v2 là tham chiếu IO chính thức | Workbook 20/11 thay thế cơ sở giao diện; IO Map v2 (mẫu 23 tín hiệu) dựa trên giao diện 10/11 | Sửa CLAUDE.md §7 **cần chủ dự án phê duyệt**; đề xuất ở CR-029. |
| C-4 | CLAUDE.md §7.2: PLC-04 = E1, E2, G3, G7 | Workbook: E1, E2, **G1, G2** | PQ-20 |
| C-5 | Tên PLC "PLC-01…PLC-04" | Workbook gọi khối này "EF-G" | PQ-20 |

### 7.1. Giải quyết theo quyết định D-1…D-4 (2026-11-21, [ADR-0011](../../decisions/ADR/ADR-0011-plc-interface-20-11-2026-architectural-decisions.md))

| ID | Kết quả | Còn lại |
|---|---|---|
| C-1 | **Giải quyết bằng D-1**: tối đa một Handshake hoạt động mỗi PLC, FIFO riêng từng PLC. **Chưa triển khai**; ADR-0008 QĐ 4 bị thay thế một phần | PLC xác nhận khả thi (PQ-13/14/22); ranh giới cửa sổ Handshake (PQ-26) |
| C-2 | **Giải quyết bằng D-2**: không dùng AGV1–3 làm định danh AMR; `Mission_Feedback` = trạng thái tiếp nhận/xử lý; WCS lưu `RobotCode` thật | Giao diện phản hồi mới/vùng `RobotCode` (PQ-06, PQ-29, IF-P3) |
| C-3 | **Giải quyết bằng D-3**: CLAUDE.md §7 đã cập nhật; IO Map v2 = baseline cũ; workbook 20/11 = giao diện mới **chưa được duyệt** | – |
| C-4 | **Vẫn mở** (G1/G2 ↔ G3/G7) | PQ-20 |
| C-5 | **Giải quyết bằng D-4 như một yêu cầu**: không chấp nhận mất đối chiếu rack; `PLC_MissionCompleted=1` không đủ | Nguồn dữ liệu rack/danh tính (PQ-27, PQ-28; IF-P1, IF-P2, IF-P4) |

**Điều chỉnh cách đọc các mục trước:** mọi chỗ trong bộ tài liệu này ghi "C-1 chưa quyết định", "chờ quyết định D-1…D-4" phải đọc theo bảng trên. Workbook 20/11 **không** phải mapping đã phê duyệt để triển khai lên PLC thật (CLAUDE.md §7.8).

## 8. Trạng thái các tài liệu cũ

Không xóa. Các bản nháp cũ được đánh dấu `SUPERSEDED / OBSOLETE (một phần)` bằng một khối ghi chú ở đầu tài liệu (xem CR-029 mục "Tài liệu bị ảnh hưởng"). Workbook 10/11 và IO Map v2 được giữ nguyên làm lịch sử; `PLC_IO_Map/README.md` ghi trạng thái từng tệp.
