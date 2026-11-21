# PLC IO Mapping Specification – Phiên bản 2 (cơ sở: giao diện 20/11/2026)

| | |
|---|---|
| **Trạng thái** | `SPECIFICATION – PENDING PLC CLARIFICATION`. Mọi địa chỉ: `NEEDS_CONFIGURATION`. |
| **Nguồn** | `AGV_PLC Interface_List_20_11_2026.xlsx`, sheet `IO Matrix` (sha256 `3c5da74b…aedddf`) |
| **Thay thế** | Mẫu 23 tín hiệu/trạm của `ETEK_WCS_IO_Map_4PLC_16Stations_v2_Separate_IO_Sets.xlsx` và `plc-io-catalog.json` (sinh từ workbook đó) **như cơ sở tín hiệu**; cấu trúc "mỗi trạm một bộ IO độc lập" được **giữ** [KT] |
| **Nhãn** | [N] [SL] [CX] [HT] [KT] – xem [Interface Specification](PLC_INTERFACE_SPECIFICATION_V2_20_11_2026.md) |

## 1. Nguyên tắc mapping (không đổi / giữ)

1. Khóa điểm IO = `PLC_ID + Station_ID + Signal_Name` **[KT]**. Cùng một tên tín hiệu ở hai trạm là hai điểm IO khác nhau.
2. `Template_Address_Reference` chỉ là tham chiếu từ nguồn; `Actual_PLC_Address` **để trống** cho đến khi kỹ sư PLC xác nhận. Không suy ra offset, không tính toán thanh ghi **[KT]**.
3. Địa chỉ giống nhau trên hai PLC vật lý khác nhau là hợp lệ (mỗi PLC một không gian bộ nhớ); trong **một** PLC, địa chỉ của các trạm không được trùng nhau **[KT]** (đã có kiểm tra chồng lấn).
4. Không dùng địa chỉ M9000…M9123, D1500…D1610 của giao diện 10/11 cho giao diện mới **[N: cột địa chỉ trống]**.
5. Giữ tên, chiều, kiểu dữ liệu, mô tả theo workbook; không dịch/sửa chính tả **[KT]**.
6. Số PLC và danh sách trạm là **dữ liệu cấu hình**, không hardcode IP/trạm **[KT]**.

## 2. Mẫu tín hiệu mới

### 2.1. Mẫu theo trạm (6 tín hiệu – lặp cho từng trạm) **[N]**

| Seq | Nhóm | Tín hiệu | Chiều | Kiểu | Địa chỉ thực |
|---|---|---|---|---|---|
| 1 | Command | `PLC_MissionRequest` | PlcToSoftware | INT | NEEDS_CONFIGURATION |
| 2 | Command | `PLC_ReaddyTranfer` | PlcToSoftware | BOOL | NEEDS_CONFIGURATION |
| 3 | Command | `PLC_MissionCompleted` | PlcToSoftware | BOOL | NEEDS_CONFIGURATION |
| 4 | Command | `AGV_RequestTranfer` | SoftwareToPlc | BOOL | NEEDS_CONFIGURATION |
| 5 | Command | `AGV_TranferCompleted` | SoftwareToPlc | BOOL | NEEDS_CONFIGURATION |
| 6 | Command | `Mission_Feedback` | SoftwareToPlc | INT | NEEDS_CONFIGURATION |

### 2.2. Tín hiệu liệt kê **một lần** sau các trạm **[N]**

| Tín hiệu | Nhóm | Chiều | Kiểu | Phạm vi | Địa chỉ thực |
|---|---|---|---|---|---|
| `HeartBit` | Status | SoftwareToPlc | BOOL | **Chưa phân loại** – [SL] theo PLC, [CX] PQ-19 | NEEDS_CONFIGURATION |
| `Spair` #1 | Spair | (không ghi) | INT | **Chưa phân loại** [CX] PQ-21 | NEEDS_CONFIGURATION |
| `Spair` #2 | Spair | (không ghi) | INT | idem | NEEDS_CONFIGURATION |
| `Spair` #3 | Spair | (không ghi) | BOOL | idem | NEEDS_CONFIGURATION |
| `Spair` #4 | Spair | (không ghi) | BOOL | idem | NEEDS_CONFIGURATION |
| `Spair` #5 | Spair | (không ghi) | BOOL | idem | NEEDS_CONFIGURATION |

Quy tắc xử lý (không đổi so với CLAUDE.md §7.3): `HeartBit` và `Spair` **không** được nhân bản âm thầm cho từng trạm và **không** được gộp âm thầm; phải được duyệt là *PLC-scoped* hoặc *Station-scoped* (PQ-19, PQ-21). `Spair` **không được đọc hay ghi** (dự phòng) cho đến khi được xác nhận; chiều tín hiệu không nêu.

## 3. Các bộ IO theo PLC/trạm

Chỉ khối `EF-G` được liệt kê **[N]**; các PLC/trạm còn lại "mapping tương tự" **[N]** (A35) nhưng **không** được nêu cụ thể **[CX]** (PQ-20).

| Khối trong workbook | IP (ô A4) | Trạm trong workbook | Trạm trong baseline đã duyệt (CLAUDE.md §7.2) | Ghi nhận |
|---|---|---|---|---|
| `EF-G` | 192.168.10.131 | E1, E2, **G1, G2** | PLC-04: E1, E2, **G3, G7** | **Xung đột G1/G2 ↔ G3/G7.** Giữ nguyên cấu hình đang chạy; không đổi cho đến khi PQ-20 được trả lời. `WCS_BP_Port_PLC_Transaction_Matrix.xlsx` (OP-03) đã ghi sự khác biệt này từ trước. |
| (không liệt kê) | 192.168.10.101 | – | PLC-01: A1, A2, A3, A4 | "tương tự" **[N]**; trạm cụ thể [CX] |
| (không liệt kê) | 192.168.10.111 | – | PLC-02: B1, B2, D1, D2 | idem |
| (không liệt kê) | 192.168.10.121 | – | PLC-03: X1, X2, X3, X6 | idem |

Số điểm IO theo mô hình mới, **nếu** mỗi PLC có 4 trạm và `HeartBit`/`Spair` theo PLC: 4 × (4×6 + 1 + 5) = **120** điểm; nếu `HeartBit`/`Spair` theo trạm: 16 × 12 = **192**; **không** khẳng định trước (PQ-19, PQ-21). (Hiện trạng: 23 × 16 = 368 điểm.)

## 4. Đối chiếu với IO Map hiện hành (hiện trạng [HT])

| Mục | Hiện trạng (IO Map v2) | Giao diện 20/11 |
|---|---|---|
| Số tín hiệu/trạm | 23 | 6 (+ `HeartBit`/`Spair` một lần sau 4 trạm) |
| Tên tín hiệu | `Safety_Ready`, `Pickup_Request`, `Delivery_Request`, `Request_ID`, `Station_Online`, `Station_Ready`, `Rack_Present`, `Rack_Full`, `Rack_Empty`, `Destination_Available`, `Station_Fault`, `Transfer_Complete`, `Rack_Type`, `Heartbeat_SW`, `Request_Ack`, `AGV_Assigned`, `AGV_ID`, `AGV_Arrived`, `Transfer_Permit`, `AGV_Departure_Permit`, `AGV_Busy`, `AGV_Fault`, `Error_Code` | `PLC_MissionRequest`, `PLC_ReaddyTranfer`, `PLC_MissionCompleted`, `AGV_RequestTranfer`, `AGV_TranferCompleted`, `Mission_Feedback`, `HeartBit`, `Spair` |
| Kiểu dữ liệu | BOOL, INT, STRING | BOOL, INT (không còn STRING) |
| `Station_Fault`, `Transfer_Complete` | có (thêm trong IO Map v2, không có ở workbook 10/11) | không có |
| Địa chỉ | `Template_Address_Reference` M9000…; `Actual_PLC_Address` NULL | trống toàn bộ |

Hệ quả cho dữ liệu/cấu hình (**chưa thực hiện** – xem [Impact Analysis](PLC_INTERFACE_CHANGE_IMPACT_ANALYSIS_20_11_2026.md)):

- Catalog `plc-io-catalog.json` (+ script sinh, SHA của workbook, seeding `PlcCatalogSeeder`, bảng `station_io_points`) mô tả 23 tín hiệu; mẫu mới cần phiên bản catalog mới và chiến lược nhập: các điểm đã có địa chỉ/phê duyệt **không được xóa hay đổi tên âm thầm** **[KT]**; cách di chuyển dữ liệu IO hiện có là quyết định thiết kế cho giai đoạn sau.
- Cần mô hình hóa tín hiệu **PLC-scoped** (nếu được duyệt) – hiện mọi điểm IO thuộc một trạm.
- Chính sách ghi (`WritePolicy`) chặn theo **tên** (`Safety_Ready`, `Transfer_Permit`, `AGV_Departure_Permit`, nhóm Safety/Interlock) – các tên này không còn; việc phân loại an toàn cho `AGV_RequestTranfer` (lệnh mở cửa/kẹp) và `Mission_Feedback` thuộc kỹ sư PLC (PQ-18).

## 5. Quy tắc xác thực mapping đề xuất [ĐX – chưa được duyệt]

1. Mỗi trạm phải có đủ 6 tín hiệu mẫu; thiếu/thừa ⇒ cấu hình không hợp lệ (không polling).
2. Kiểu dữ liệu và chiều theo workbook; INT 16 bit có dấu **[CX]** (giả định cũ trong ADR-0004 phải được xác nhận lại).
3. Không trùng địa chỉ trong cùng PLC; địa chỉ cho phép lặp giữa các PLC.
4. Chỉ điểm đã nhập, đã duyệt mới được polling/ghi trên PLC thật (cơ chế hiện có – CR-004/CR-019).
5. Bắt đầu commissioning chỉ-đọc cho: `PLC_MissionRequest`, `PLC_ReaddyTranfer`, `PLC_MissionCompleted`. Ghi `HeartBit` chỉ sau khi kỹ sư PLC duyệt (tín hiệu "xác nhận kết nối", không phải an toàn/chuyển động nhưng vẫn là ghi lên PLC thật). Ghi `AGV_RequestTranfer`/`AGV_TranferCompleted`/`Mission_Feedback` chỉ sau khi được duyệt từng điểm + quy trình thử an toàn.

## 6. Việc cần có từ bên PLC để hoàn tất mapping

PQ-01 (địa chỉ thực từng tín hiệu từng trạm: thanh ghi, bit/word), PQ-19, PQ-20, PQ-21; thông số kết nối (cổng TCP, MC Protocol, QJ71E71-100…) vẫn là `NEEDS_CONFIGURATION` theo CLAUDE.md §7.5 và không thuộc phạm vi tài liệu này.

## 7. Cập nhật theo D-2, D-3, D-4 (2026-11-21, [ADR-0011](../../decisions/ADR/ADR-0011-plc-interface-20-11-2026-architectural-decisions.md))

- **D-3:** workbook 20/11 là **giao diện mới chờ xác nhận/phê duyệt**, **không** phải mapping đã duyệt; IO Map v2 (mẫu 23 tín hiệu) là **baseline cũ**, chỉ để tham khảo/đối chiếu (CLAUDE.md §7.8). Không dùng IO Map v2 làm căn cứ mặc định cho thiết kế mapping mới.
- **D-2:** enum của `Mission_Feedback` (0/AGV1–3/>10) **giữ nguyên nguyên văn**, không sửa. Mapping IO không được dùng nó như định danh AMR; mọi vùng dữ liệu truyền `RobotCode` thật chỉ được thêm sau khi hai bên thống nhất (IF-P3, PQ-29).
- **D-4:** mẫu 6 tín hiệu/trạm **không đủ** cho rack có mặt, danh tính rack, định danh giao dịch, điều kiện an toàn. Mẫu mapping phải mở rộng hoặc thêm nguồn dữ liệu ngoài PLC (IF-P1, IF-P2, IF-P4; PQ-27, PQ-28); **chưa** thêm tín hiệu nào vào mapping khi chưa có thỏa thuận (không đặt tên/địa chỉ).
