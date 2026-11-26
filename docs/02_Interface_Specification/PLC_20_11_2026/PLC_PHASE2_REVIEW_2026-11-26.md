# PLC Interface 20/11/2026 – Giai đoạn 2: đối chiếu workbook ↔ hệ thống

| | |
|---|---|
| **Ngày** | 2026-11-26 |
| **Trạng thái** | `BÁO CÁO CHỜ REVIEW – CHỜ KỸ SƯ PLC XÁC NHẬN HỢP ĐỒNG IO`. Không `APPROVED`, không `READY FOR IMPLEMENTATION`. Không sửa mã, mapping, workbook, CR-029, `CLAUDE.md`, database |
| **Bản sửa** | Rev 3 (2026-11-26): ghi trạng thái OW-1…OW-8, kết quả kiểm tra OW-6. Rev 2: PD-1…PD-4 |
| **Căn cứ phân tích** | `PLC_IO_Map/AGV_PLC Interface_List_20_11_2026.xlsx`, SHA-256 `a291d28fac907c3dbf5e3620425abf62cb718d058ab472eec9ccf8c0701babb3` (Git blob `5799a155`) |
| **Phạm vi kiểm chứng** | Đọc đủ hai sheet (giá trị ô, ô gộp, đường nối); đọc source; `SELECT` trên database trên máy. Không kết nối PLC. Không xem workbook bằng Excel |
| **Quản lý source** | Tài liệu này **không thuộc CR-042** (đang HOLD) và phải tách khi commit |

Chỉ đạo dùng workbook làm căn cứ **phân tích** không phê duyệt handshake nào để triển khai.

**Ba loại nội dung** được ghi tách nhau: **[QĐ]** quyết định của chủ dự án · **[WB]** nội dung thể hiện trong workbook · **[PLC]** cần kỹ sư PLC xác nhận (`OPEN`).

## 0. Quyết định của chủ dự án (2026-11-26)

| Mã | Quyết định [QĐ] | Còn cần [PLC] |
|---|---|---|
| **PD-1** | `AGV_RequestTranfer` là **yêu cầu** WCS gửi PLC, không trực tiếp điều khiển cửa/kẹp. PLC tự xử lý theo logic và interlock của PLC. Việc WCS ghi yêu cầu **không** chứng minh cửa/kẹp đã mở. `PLC_ReaddyTranfer` là phản hồi của PLC theo hợp đồng IO | Xác nhận bằng văn bản; cách PLC từ chối (PLC-OQ-16) |
| **PD-2** | Tín hiệu handshake **giữ trạng thái**, không mặc định là xung. Bên ghi chịu trách nhiệm OFF/reset tín hiệu của mình; không bên nào tự reset tín hiệu của bên kia | Điều kiện và thời điểm OFF, giao dịch kế tiếp, lỗi, mất kết nối (PLC-OQ-03). **Chưa triển khai logic reset** khi chưa có bảng handshake được xác nhận |
| **PD-3** | `AGV_RequestTranfer` và `AGV_TranferCompleted` **dùng chung một thanh ghi INT theo từng trạm**, phân biệt bằng giá trị: `1` yêu cầu Pick, `4` yêu cầu Drop, `3` xong Pick, **`5` xong Drop** (trước là `4`). Không thiết kế hai địa chỉ. Mapping và enum cấu hình được theo PLC/trạm, không hard-code. Không suy diễn địa chỉ mà workbook không thể hiện rõ | Địa chỉ từng PLC/trạm; chấp nhận bảng mã, gồm `5` (PLC-OQ-01) |
| **PD-4** | Không chỉ dựa vào giá trị INT để kết luận giao dịch đã hoàn thành khi trình tự handshake chưa được xác nhận | Phân biệt giá trị theo trạng thái, điều kiện nhận, chuyển bước, giá trị sai/sai thứ tự, chống lặp, kết nối lại (PLC-OQ-21) |

Hai bộ mã khác nhau: **`PD-n`** là quyết định về hợp đồng IO (bảng trên); **`OW-n`** là quyết định quản lý công việc, trạng thái ở §10. Quyết định của chủ dự án **không** thay cho xác nhận của kỹ sư PLC.

## 1. Kết luận chính

| # | Phát hiện | Mức |
|---|---|---|
| **K-1** | Địa chỉ trong mẫu cũ (v2) **đè lên tín hiệu mới**: `AGV_ID` mẫu `D1600` và `Error_Code` mẫu `D1610` là thanh ghi dùng chung `AGV_RequestTranfer`/`TranferCompleted` của trạm 1 và 2. Nếu ai nhập tay địa chỉ mẫu cho tín hiệu cũ, được duyệt ghi và chạy Real với PLC đã lập trình theo bản mới, WCS có thể gửi **yêu cầu mở cửa/kẹp hoặc báo hoàn thành không chủ đích** (ví dụ ghi `1` hoặc `3`). Theo PD-1, PLC tự xử lý theo interlock; interlock đó **chưa được xác nhận bằng văn bản** | **An toàn – nghiêm trọng** |
| **K-2** | `CLAUDE.md` §7.8 và ADR-0011 D-4 viết "`PLC_MissionCompleted = 1` là điều kiện cần". Bản hiện tại: **`1` = Thất bại, `2` = Thành công**. Làm theo câu chữ cũ sẽ coi thất bại là hoàn thành. **OW-4 `APPROVED`** việc sửa diễn giải; hai văn bản đó **chưa được sửa** | **Nghiêm trọng** |
| **K-3** | File cùng tên đã **đổi nội dung** sau khi CR-029 phân tích. Bộ tài liệu CR-029 mô tả bản cũ | Cao |
| **K-4** | Ô địa chỉ **gộp** thể hiện `AGV_RequestTranfer` và `AGV_TranferCompleted` **dùng chung thanh ghi** ở cả 16 trạm [WB], được xác nhận là chủ đích [QĐ PD-3]. Bảng mã trong workbook còn dùng `4` cho cả yêu cầu Drop và xong Drop; quyết định đổi xong Drop thành `5`. **Workbook chưa cập nhật, PLC chưa xác nhận**: nếu một bên làm theo `4`, báo xong Drop bị hiểu là yêu cầu Drop | Cao |
| **K-5** | **Không tín hiệu nào** trong 7 tín hiệu của workbook có trong hệ thống. Mô hình tiếp nhận hiện tại cần `Request_ID`, bit Pick/Drop và mã vị trí rack, bản mới không có | Cao |
| **K-6** | Danh sách tín hiệu cấm ghi chỉ gồm tên cũ; `AGV_RequestTranfer` (yêu cầu tác động tới cơ cấu, dù PLC quyết định theo PD-1) sẽ **không bị chặn** nếu thêm vào catalog mà không sửa chính sách. Lớp chặn ghi mặc định: **OW-2 `APPROVED`, chưa hiện thực** | An toàn |
| **K-7** | IO Map v2 (xlsx) **đã bị xóa khỏi Git** trong merge `a8c8787`, trái `CLAUDE.md` §7.6/7.8. Còn trong commit `34c45b2`; catalog đang chạy sinh từ file này. **OW-5 `APPROVED`** khôi phục làm tài liệu `LEGACY/HISTORICAL`, **chưa thực hiện** | Cao – quản lý tài liệu |

**Hiện trạng an toàn trên máy này:** PLC mode `Simulator`; bảng trao đổi MC Protocol trống; không có kết nối tới `192.168.10.*`. K-1 chưa xảy ra ở đây. Máy vận hành khác **chưa kiểm tra**. Đề nghị: không bật `Plc:Write:Enabled` ở bất kỳ đâu cho tới khi mapping thống nhất.

## 2. Phiên bản của workbook

| Mục | Bản CR-029 phân tích (`d4c5030`, 21/11) | Bản hiện tại (`a8c8787`, 25/11) |
|---|---|---|
| SHA-256 | `3c5da74b…aedddf` | `a291d28f…babb3` |
| PLC / trạm liệt kê | Chỉ khối `EF-G` (E1, E2, G1, G2); còn lại "tương tự" | Đủ 4 PLC, 16 trạm |
| Địa chỉ | Trống | Có `D15xx`, `D16xx`, `M92xx` |
| `PLC_ReaddyTranfer` | BOOL `0` đóng, `1` mở | INT `1` Pick, `2` Drop, `10` lỗi |
| `PLC_MissionCompleted` | BOOL `0` thất bại, `1` thành công | INT `1` thất bại, `2` thành công |
| `AGV_RequestTranfer` | BOOL `0` rời, `1` đến | INT `1` Pick, `4` Drop |
| `AGV_TranferCompleted` | BOOL `0` rời, `1` đến | INT `3` xong Pick, `4` xong Drop |
| Lỗi | `>10` | `10` |
| Dự phòng | INT×2, BOOL×3, không địa chỉ | BOOL×5 `M9201–M9205` |
| Sheet `Trang_tính1` | 6 bước | Không đổi |

Tên file không có số phiên bản. `PLC_MAPPING_COMPARISON_CR-035.md` đã ghi nhận một phần bản mới; các tài liệu khác chưa.

## 3. Nội dung workbook (bản hiện tại)

### 3.1 PLC và trạm

| Nhãn cột A | IP | Trạm | `CLAUDE.md` §7.2 | Kết quả |
|---|---|---|---|---|
| `A1` | 192.168.10.101 | A1, A2, A3, A4 | A1–A4 | Khớp |
| `BCD` | 192.168.10.111 | B1, B2, D1, D2 | B1, B2, D1, D2 | Khớp |
| `A2` | 192.168.10.121 | X1, X2, X3, **X4** | X1, X2, X3, **X6** | **Khác** |
| `EF-G` | 192.168.10.131 | E1, E2, **G1, G2** | E1, E2, **G3, G7** | **Khác** (PQ-20) |

Nhãn `A1`, `A2` của PLC trùng tên trạm; `A2` là PLC phục vụ nhóm X.

### 3.2 Địa chỉ (giống nhau ở cả 4 PLC, theo thứ tự trạm n = 0…3)

| Tín hiệu | Chiều | Kiểu | Địa chỉ | Giá trị | Phạm vi |
|---|---|---|---|---|---|
| `PLC_MissionRequest` | PLC → SW | INT | `D15n0` | 0 không, 1 Pick, 2 Drop, 10 lỗi | Trạm |
| `PLC_ReaddyTranfer` | PLC → SW | INT | `D15n1` | 1 cho Pick, 2 cho Drop, 10 lỗi | Trạm |
| `PLC_MissionCompleted` | PLC → SW | INT | `D15n2` | 1 thất bại, 2 thành công | Trạm |
| `AGV_RequestTranfer` | SW → PLC | INT | `D16n0` | 1 mở cho Pick, 4 mở cho Drop | Trạm |
| `AGV_TranferCompleted` | SW → PLC | INT | `D16n0` (ô gộp, chung thanh ghi) | 3 xong Pick, 4 xong Drop **[WB]** → **5 xong Drop [QĐ PD-3]** | Trạm |
| `Mission_Feedback` | SW → PLC | INT | `D16n1` | 0 chưa có AGV, 1–3 AGV1–3, 10 lỗi | Trạm |
| `HeartBit` | SW → PLC | BOOL | `M9200` | on/off chu kỳ 1 s, timeout 10 s | PLC (cột trạm trống) |
| `Spair` ×5 | – | BOOL | `M9201–M9205` | dự phòng | PLC |

Bảng trên là nội dung workbook, trừ chỗ ghi [QĐ]. Kiểm tra bằng script trên 120 dòng: 96 dòng lệnh, 4 heartbeat, 20 dự phòng; cùng định nghĩa ở 16 trạm; không địa chỉ riêng nào trùng trong cùng một PLC. Danh tính một IO phải là PLC (IP) + trạm + tên tín hiệu.

**Thanh ghi dùng chung `AGV_RequestTranfer` / `AGV_TranferCompleted`:**

| Giá trị | Ý nghĩa | Tín hiệu | Workbook [WB] | Quyết định [QĐ PD-3] | PLC [PLC] |
|---:|---|---|---|---|---|
| 1 | Yêu cầu mở cửa/kẹp để Pick | `AGV_RequestTranfer` | 1 | 1 | `OPEN` |
| 4 | Yêu cầu mở cửa/kẹp để Drop | `AGV_RequestTranfer` | 4 | 4 | `OPEN` |
| 3 | Hoàn thành Pick | `AGV_TranferCompleted` | 3 | 3 | `OPEN` |
| 5 | Hoàn thành Drop | `AGV_TranferCompleted` | **4** | **5** | `OPEN` |

Địa chỉ theo từng PLC + trạm: workbook thể hiện `D1600`, `D1610`, `D1620`, `D1630` cho trạm thứ 1–4 của mỗi PLC qua ô gộp; kỹ sư PLC xác nhận từng dòng. Giá trị `0` và mọi giá trị khác **chưa được định nghĩa**. Ghi tới PLC thật vẫn **BLOCKED**.

### 3.3 Trình tự (`Trang_tính1`, chiều lấy theo mũi tên)

| Bước | Chiều | Tín hiệu | Chú thích trên sheet |
|---|---|---|---|
| 1 | PLC → AGV | `PLC_MissionRequest` | "Gửi yêu cầu Lấy/Cấp Rack"; "thay đổi theo `PLC_MissionCompleted`" |
| 2 | AGV → PLC | `Mission_Feedback` | "Phần mềm nhận nhiệm vụ"; "PLC quan sát AGV đã nhận hay gặp lỗi" |
| 3 | AGV → PLC | `AGV_RequestTranfer` | "AGV đã đến vị trí trước cửa trạm" |
| 4 | PLC → AGV | `PLC_ReaddyTranfer` | "Xác nhận đã mở cửa, mở kẹp" |
| 5 | AGV → PLC | `AGV_TranferCompleted` | "AGV đã lấy và ra vị trí ngoài" |
| 6 | PLC → AGV | `PLC_MissionCompleted` | "Xác nhận AGV đã hoàn thành"; "thay đổi theo `Mission_Feedback` và `AGV_TranferCompleted`" |

Sheet **không** nêu: giữ mức hay xung, ai reset, thời hạn, nhánh lỗi, khác biệt của thao tác Drop.

## 4. Xung đột và tình trạng

| Mã | Bên A | Bên B | Tình trạng |
|---|---|---|---|
| C-6 | ADR-0011 D-2, `CLAUDE.md` §7.8: `Mission_Feedback` 1–3 **không** là định danh AMR | Workbook: 1 AGV1, 2 AGV2, 3 AGV3 | `DECIDED` (OW-3): giữ D-2. Cách PLC nhận phản hồi: PLC-OQ-06 `OPEN` |
| C-7 | ADR-0011 D-4: `MissionCompleted = 1` cần nhưng chưa đủ | Workbook: 1 = thất bại, 2 = thành công | `APPROVED` (OW-4); văn bản cũ chưa sửa. Nguyên tắc "chưa đủ bằng chứng" của D-4 giữ nguyên |
| C-8 | `CLAUDE.md` §7.8: cột địa chỉ trống | Bản hiện tại có địa chỉ | Căn cứ phân tích: `DECIDED` (OW-1). Bản chính thức: PLC-OQ-19 `OPEN`. Sửa câu chữ `CLAUDE.md`: chưa có quyết định |
| C-4 | `CLAUDE.md` §7.2: X6; G3, G7 | Workbook: X4; G1, G2 | PLC-OQ-14 `OPEN` |
| C-9 | Chỉ đạo trước: `AGV_TranferCompleted` chưa có địa chỉ | Ô gộp cho thấy `D16n0` | `DECIDED` (PD-3): dùng chung. Địa chỉ từng trạm: PLC-OQ-01 `OPEN` |
| C-12 | Workbook: xong Drop = `4` | PD-3: xong Drop = `5` | `DECIDED` (PD-3). PLC xác nhận và bản workbook mới: PLC-OQ-01 `OPEN` |
| C-10 | §7.3: tín hiệu cấp PLC phải được phân loại rõ | `HeartBit` cấp PLC; mô hình dữ liệu chỉ có điểm theo trạm | Thiết kế Giai đoạn 3; PLC-OQ-11 `OPEN` |
| C-5 (còn) | D-4: cần xác nhận rack có mặt, danh tính, chống trùng, an toàn | Workbook không có tín hiệu nào cho các việc này | PLC-OQ-04, 15, 16 `OPEN` |
| C-11 | Quy tắc bảo vệ: A3/A4, D1/D2 giữ quy trình cũ | Workbook cấp đủ bộ tín hiệu cho 4 trạm này | `DECIDED` (OW-7): chỉ giám sát |

## 5. Đối chiếu từng tín hiệu

Phân loại: **Khớp** · **Khác biệt** · **Chưa có mapping** · **Thiếu địa chỉ** (sau PD-3 không còn mục nào) · **Chưa rõ handshake**.

| IO trong workbook | Gần nhất trong source (v2) | Kết quả | Sai lệch | Thay đổi cần làm |
|---|---|---|---|---|
| `PLC_MissionRequest` | `Pickup_Request`, `Delivery_Request` (BOOL), `Request_ID` (STRING) | Chưa có mapping · Chưa rõ handshake | Một INT thay hai bit; không có mã yêu cầu; không có mã vị trí/loại rack | Tín hiệu mới; thiết kế lại tiếp nhận và chống trùng |
| `PLC_ReaddyTranfer` | Không có (`Station_Ready` BOOL khác nghĩa) | Chưa có mapping · Chưa rõ handshake | – | Tín hiệu mới; không dùng làm bằng chứng an toàn khi chưa xác nhận |
| `PLC_MissionCompleted` | `Transfer_Complete` (BOOL) | Chưa có mapping · Khác biệt kiểu, giá trị · Chưa rõ handshake | BOOL 1 = xong ↔ INT 2 = thành công, 1 = thất bại | Tín hiệu mới; kết hợp bằng chứng D-4 |
| `AGV_RequestTranfer` | `AGV_Arrived`, `Transfer_Permit` (BOOL) | Chưa có mapping · Chưa rõ handshake | Một INT **yêu cầu** (PD-1), dùng chung thanh ghi (PD-3) | Tín hiệu mới; **đưa vào danh sách cấm ghi** cho tới khi được xác nhận |
| `AGV_TranferCompleted` | `AGV_Departure_Permit` (BOOL) | Chưa có mapping · Khác workbook ở mã Drop (C-12) · Chưa rõ handshake | Cùng thanh ghi với `RequestTranfer`; xong Drop `5` | Tín hiệu mới; mô hình phải biểu diễn hai tín hiệu một thanh ghi; ghi BLOCKED chờ PLC-OQ-01, 03, 21 |
| `Mission_Feedback` | `AGV_Assigned` (BOOL), `AGV_ID` (INT) | Chưa có mapping · Chưa rõ handshake | Enum AGV cố định trái D-2 | Không ghi 1–3 làm định danh AGV (OW-3); ghi BLOCKED chờ PLC-OQ-06 |
| `HeartBit` | `Heartbeat_SW` (BOOL, mỗi trạm) | Khác biệt phạm vi, địa chỉ | Cấp PLC; **hệ thống chưa có chức năng phát heartbeat** | Điểm cấp PLC và bộ phát; chờ PLC-OQ-11 |
| `Spair` ×5 | Không có | Khớp (không đọc, không ghi) | – | Không đưa vào danh sách ghi |
| 23 tín hiệu v2 | Không còn trong workbook | Khác biệt | Gồm `Safety_Ready`, `Station_Online`, `Rack_Present`, `Error_Code`… | Giữ làm lịch sử; không xóa dữ liệu đã nhập |

## 6. Đối chiếu từng thành phần

| Thành phần | Hiện trạng | Kết quả |
|---|---|---|
| Kết nối PLC (4 kết nối, IP) | Catalog 4 PLC, IP đúng | **Khớp** |
| Tham số MC (cổng, frame, network…) | Workbook không nêu; để `NEEDS_CONFIGURATION` | Khớp nguyên tắc |
| Trạm | Catalog v2: X6, G3, G7 | Khác biệt 3/16 |
| Thiết bị `D`, `M`; kiểu BOOL, INT 16 bit | Driver MC hỗ trợ; INT đang giải mã **có dấu** (chưa xác nhận) | Khớp khả năng |
| Đọc PLC → SW | Đọc theo điểm có địa chỉ hợp lệ, gộp địa chỉ liền nhau | Tái sử dụng được |
| Ghi SW → PLC | Cần mode, khóa triển khai và duyệt từng điểm | Tái sử dụng; phải sửa danh sách cấm (K-6) |
| Kiểm tra chồng địa chỉ | Hai điểm cùng thanh ghi → `ADDRESS_CONFLICT`, cả hai không được đọc | **Khác yêu cầu**: chưa hỗ trợ thanh ghi dùng chung đã quyết định (PD-3) |
| Kiểm tra giá trị hợp lệ | Chỉ kiểm tra kiểu và dải 16 bit | Chưa có enum theo tín hiệu |
| Điểm cấp PLC | `StationIoPoint` bắt buộc có trạm | Chưa hỗ trợ |
| Handshake | Khung theo luật được duyệt (chưa có luật); đọc tên v2; hằng số 23 tín hiệu; chỉ đặt/xóa BOOL | Không dùng được cho trình tự INT giữ trạng thái (PD-2) |
| Timeout, reset | Chỉ cảnh báo timeout của luật BOOL | Nguyên tắc đã quyết (PD-2); điều kiện chưa rõ |
| Tiếp nhận yêu cầu thật | `PlcStationSignalService`: cạnh lên bit v2, tắt mặc định; đường sang Transport Order chưa có | Phải thiết kế lại |
| Mô phỏng PLC | Dùng tên v2 (`PlcRequestAssembler`) và trang mô phỏng riêng | Phải thay (Giai đoạn 4–5) |
| Sự kiện RCS "đến trước cửa", "đã ra ngoài" | Chưa có (callback chỉ cho task Phase 1) | Chặn bước 3 và 5 (PQ-23, OQ-42-06) |
| Log MC, log giao tiếp | Độc lập tên tín hiệu | Tái sử dụng |
| API, giao diện IO | Chung theo bảng IO; tên, mô tả, cấm ghi cứng theo v2 (`plcSignals.ts`, `plc.ts`, `plcFormat.ts`) | Phải cập nhật |
| Xuất/nhập cấu hình | Có nhóm IO | Cần xét tương thích gói cũ |
| Test | 28 file test dùng tên v2 | Phải cập nhật khi đổi |

**Database trên máy (chỉ đọc):** 368 điểm theo 23 tên v2; 29 điểm có địa chỉ (6 ở PLC-01/A1, 23 ở PLC-03/X1), 23 đã duyệt đọc, 9 được duyệt ghi; địa chỉ dạng `M1…`, `D1…D7`, `D1200` (không trùng bản mới). PLC-01 và PLC-03 có cổng 5000 và đang bật; trạm `X6` được gắn với vị trí `X4`. Các giá trị này do người dùng nhập; ý nghĩa chưa xác nhận.

## 7. Kiến trúc IO Definition Catalog (đề xuất, chưa triển khai)

| Nội dung | Đề xuất |
|---|---|
| Nguồn | Một catalog có số phiên bản, **sinh từ workbook** bằng script có kiểm tra (ô gộp, trùng địa chỉ, hash); script thất bại khi gặp ô mơ hồ |
| Định danh | `SignalId` ổn định; `ContractName` giữ nguyên chính tả (`Readdy`, `Tranfer`) |
| Phạm vi | `Plc` hoặc `Station` |
| Cấu hình theo PLC/trạm | Địa chỉ và bảng enum **cấu hình được theo từng PLC/trạm**, không hard-code giá trị trong mã (PD-3) |
| Nguồn của giá trị | Mỗi giá trị ghi rõ nguồn: workbook, quyết định (ví dụ `5`), hoặc xác nhận PLC; khác biệt với workbook phải hiển thị, không ghi đè âm thầm |
| Trường | Endpoint, trạm, thiết bị/địa chỉ, kiểu, chiều, enum giá trị, ý nghĩa, quyền ghi, nhóm an toàn, timeout/reset (trống khi chưa xác nhận), trạng thái xác nhận từng trường |
| Địa chỉ | Địa chỉ từ workbook nhập ở trạng thái **chưa duyệt**; không tự duyệt đọc hay ghi |
| Thanh ghi dùng chung | Khai báo tường minh nhóm hai tín hiệu trên một thanh ghi; hai bộ giá trị không giao nhau; mọi chồng địa chỉ khác vẫn là lỗi (PD-3) |
| Giải mã | Đọc giá trị thanh ghi rồi đối chiếu với trạng thái giao dịch; giá trị đơn lẻ không đủ kết luận hoàn thành (PD-4) |
| Quyền ghi | Danh sách **cho phép** theo catalog thay cho danh sách cấm theo tên |
| Chuyển đổi dữ liệu | Thêm mới, không xóa/đổi tên điểm đã nhập; điểm v2 đánh dấu ngừng dùng và không được ghi |
| Dùng chung | Driver thật, simulator, kiểm tra backend, giao diện, test |
| Trạm chỉ giám sát | A3, A4, D1, D2: đọc được, không cho ghi, không tạo yêu cầu (OW-7) |

**File dự kiến thay đổi (Giai đoạn 3):** `scripts/generate-plc-io-catalog.mjs`, `plc-io-catalog.json`, `PlcTemplateProvider.cs`, `PlcPersistence.cs` (seeder), `PlcEntities.cs` + migration, `PlcModels.cs`, `AddressValidator.cs`, `WriteValueValidator.cs`, `PlcConfigService.cs` (`WritePolicy`), `Handshake.cs`, `PlcStationSignalService.cs`, `PlcRequestService.cs`, `PlcSignalSimulation.cs`, `PlcSimulationService.cs`, `ConfigGroups.cs`, `PlcEndpoints.cs`, giao diện PLC, test, CHANGELOG, Impact và Traceability Matrix.

## 8. Việc đã được phép, việc còn chờ

**Đã được cho phép, chưa thực hiện** (đợt cập nhật tài liệu này không sửa source, workbook hay văn bản ngoài hai file Phase 2):

| Việc | Căn cứ | Ghi chú |
|---|---|---|
| Lớp chặn ghi mặc định: mọi tín hiệu không nằm trong danh sách cho phép đều bị chặn (xử lý K-1, K-6) | OW-2 | Tách khỏi Giai đoạn 3; sửa source |
| Sửa diễn giải D-4 trong ADR-0011 và `CLAUDE.md` §7.8 | OW-4 | Xử lý K-2 |
| Khôi phục IO Map v2 từ `34c45b2`, đánh dấu `LEGACY/HISTORICAL` | OW-5 | Xử lý K-7 |

**Thuộc Giai đoạn 3 (chưa bắt đầu; nhánh theo OW-8):**

| Việc | Điều kiện |
|---|---|
| Khung catalog (định danh, phạm vi PLC/trạm, enum cấu hình được, trạng thái xác nhận), script sinh và test mapping | Phê duyệt bắt đầu Giai đoạn 3 |
| Nhập 6 tín hiệu trạm (5 địa chỉ, một thanh ghi dùng chung) ở trạng thái chưa duyệt, chỉ cho Simulator | PLC-OQ-01, 14 và phê duyệt CR-029 |

| BLOCKED | Chờ |
|---|---|
| Ghi thanh ghi dùng chung (`RequestTranfer`, `TranferCompleted`) | PLC-OQ-01, 03, 21 |
| Ghi `Mission_Feedback` | PLC-OQ-06 |
| Mọi lệnh ghi tới PLC thật | Hợp đồng IO được xác nhận: PLC-OQ-01, 02, 03, 04, 05, 07, 16, 21 |
| Tiếp nhận yêu cầu, chống trùng | PLC-OQ-03, 04, 15 |
| Logic OFF/reset | Bảng handshake được xác nhận (PLC-OQ-03) |
| Xác nhận bàn giao | PLC-OQ-09, 15 |
| Phát `HeartBit` thật | PLC-OQ-11 |
| Bước 3 và 5 gắn với AGV | Sự kiện RCS (PQ-23, OQ-42-06) |
| Một handshake mỗi PLC | PLC-OQ-17 |

## 9. Câu hỏi cho kỹ sư PLC

Nội dung đầy đủ để gửi: [PLC_ENGINEER_QUESTIONS_2026-11-26.md](PLC_ENGINEER_QUESTIONS_2026-11-26.md). Đây là **bản duy nhất**; bảng dưới chỉ theo dõi trạng thái.

| Mã (PQ) | Chủ đề | Đã quyết định | Trạng thái |
|---|---|---|---|
| PLC-OQ-01 (01, 08) | Địa chỉ thanh ghi dùng chung và bảng mã 1, 4, 3, 5 | PD-3: dùng chung | `OPEN` |
| PLC-OQ-02 (16) | Mã Drop khác nhau giữa các tín hiệu (2, 4, 5) | PD-3: 4 và 5 | `OPEN` |
| PLC-OQ-03 (03, 04, 07) | Bảng handshake Pick và Drop | PD-2: giữ trạng thái, bên ghi tự OFF | `OPEN` |
| PLC-OQ-04 (02, 10) | Phân biệt hai yêu cầu liên tiếp, chống trùng | PD-2: giữ trạng thái | `OPEN` |
| PLC-OQ-05 (08) | Thời điểm ghi và huỷ `AGV_RequestTranfer` | PD-1: là yêu cầu | `OPEN` |
| PLC-OQ-06 (06, 29) | `Mission_Feedback` | OW-3: giữ D-2 | `OPEN` |
| PLC-OQ-07 (07, 11) | PLC báo lỗi hoặc không trả lời | – | `OPEN` |
| PLC-OQ-08 (16) | Điều kiện báo hoàn thành, nhất là Drop | PD-3: Drop = 5 | `OPEN` |
| PLC-OQ-09 (09) | `MissionCompleted` thất bại | OW-4: 1 thất bại, 2 thành công | `OPEN` |
| PLC-OQ-10 (03) | Hai chú thích "thay đổi theo" | – | `OPEN` |
| PLC-OQ-11 (19) | `HeartBit` | – | `OPEN` |
| PLC-OQ-12 (11, 12) | Mất kết nối, khởi động lại | PD-2: không reset hộ | `OPEN` |
| PLC-OQ-13 (01) | Kết nối và tham số MC | – | `OPEN` |
| PLC-OQ-14 (20) | Danh sách trạm | – | `OPEN` |
| PLC-OQ-15 (17, 27, 28) | Dữ liệu rack | – | `OPEN` |
| PLC-OQ-16 (18) | Interlock; PLC từ chối yêu cầu | PD-1: PLC tự điều khiển | `OPEN` |
| PLC-OQ-17 (22, 26) | Một handshake mỗi PLC | D-1 | `OPEN` |
| PLC-OQ-18 (01) | Định dạng INT | – | `OPEN` |
| PLC-OQ-19 (25) | Phiên bản workbook | – | `OPEN` |
| PLC-OQ-20 (25) | Chuyển đổi chương trình PLC | – | `OPEN` |
| **PLC-OQ-21** (mới) | Hành vi thanh ghi dùng chung | PD-3, PD-4 | `OPEN` |

## 10. Quyết định quản lý công việc (OW)

`DECIDED` = chủ dự án đã quyết định · `APPROVED` = đã được cho phép thực hiện · `OPEN` = còn thiếu thông tin hoặc chưa có quyết định.

| Mã | Nội dung | Trạng thái | Thực hiện |
|---|---|---|---|
| OW-1 | Workbook hash `a291d28f…babb3` là căn cứ phân tích | `DECIDED` | Đã áp dụng trong báo cáo. Không phải xác nhận hợp đồng IO |
| OW-2 | Lớp chặn ghi mặc định, tách khỏi Giai đoạn 3 | `APPROVED` | Chưa hiện thực |
| OW-3 | Giữ D-2; không dùng enum 1–3 làm định danh AGV cố định khi chưa xác nhận | `DECIDED` | Đã áp dụng trong báo cáo |
| OW-4 | Sửa diễn giải D-4: 1 = thất bại, 2 = thành công | `APPROVED` | ADR-0011, `CLAUDE.md` chưa sửa |
| OW-5 | Khôi phục IO Map v2 từ commit `34c45b2` làm tài liệu `LEGACY/HISTORICAL` | `APPROVED` | Chưa thực hiện |
| OW-6 | File `AGV_PLC_IO_Interface_List_10_11_2026.xlsx` bị xóa trong working tree | `OPEN` – cần xác nhận chủ ý | Đã kiểm tra, chưa khôi phục (bên dưới) |
| OW-7 | A3/A4, D1/D2 chỉ giám sát | `DECIDED` | Áp dụng khi lập catalog |
| OW-8 | Giai đoạn 3 tạo nhánh mới từ `main` sau khi CR-042 được xử lý | `DECIDED` | Chưa tạo nhánh |

### OW-6 – Kết quả kiểm tra (2026-11-26)

| Mục | Kết quả |
|---|---|
| Đường dẫn | `docs/02_Interface_Specification/PLC_IO_Map/AGV_PLC_IO_Interface_List_10_11_2026.xlsx` |
| Trạng thái Git | Xóa trong working tree, **chưa stage**, không có stash |
| Lịch sử | Thêm ở `cce0d1e` (2026-11-18), vào `main` qua `99e74d3`; không commit nào xóa hoặc sửa. Còn ở `HEAD`, `main` và mọi nhánh cục bộ |
| Nội dung | SHA-256 `936271db…bca53`, 15 085 byte; trùng giá trị ghi trong `PLC_IO_Map/README.md` |
| Thời điểm, cách xóa | Có trong Recycle Bin, xóa khỏi đúng thư mục này lúc **14:38 ngày 26/11/2026** (xóa kiểu thao tác trên Windows, không phải lệnh Git) |
| Bản sao | Một file giống hệt nằm **ngoài repository**, trong thư mục cha của dự án (ngày tạo 17/11) |
| Khôi phục | Làm được mà không ghi đè gì (đường dẫn đang trống): `git restore -- "<đường dẫn>"` |
| Chủ ý | **Chưa xác định được** từ dữ liệu. Không để mục này chặn việc khác; không đưa thay đổi xóa vào commit khi chưa xác nhận |

## 11. Đề xuất cập nhật CR-029 (chưa thực hiện; trạng thái giữ `PENDING_APPROVAL`)

1. Ghi bản workbook `a291d28f` (commit `a8c8787`) và bảng khác biệt §2; giữ bản `3c5da74b` làm lịch sử.
2. Cập nhật §1 "Statements mới/đổi" theo §3 của báo cáo này.
2a. Ghi PD-1…PD-4 và OW-1…OW-8 (có thể thành ADR mới), gồm khác biệt với workbook ở mã xong Drop (`5`) và việc cần phát hành bản workbook mới.
3. Thêm xung đột C-6…C-12 kèm tình trạng ở §4; cập nhật C-4 (thêm X4/X6).
4. Thêm rủi ro K-1, K-2, K-6, K-7.
5. Đánh dấu phần lỗi thời trong chín tài liệu `PLC_*_V2_20_11_2026.md` và `PLC_IO_Map/README.md` (địa chỉ trống, kiểu BOOL, hash cũ).
6. Liên kết PLC-OQ-01…21 với PQ-01…29; đóng PQ nào đã được bản mới trả lời **sau khi** kỹ sư PLC xác nhận.
7. Sửa diễn giải D-4 trong ADR-0011 và `CLAUDE.md` §7.8 (OW-4, `APPROVED`). Sửa câu "cột địa chỉ trống" (C-8): đề xuất, chưa có quyết định.
8. `docs/reviews/ENGINEER_QUESTIONS_PLC_RCS_2026-11-26.md` (thuộc CR-042, không sửa đợt này) dùng tên tín hiệu v2; khi gửi kỹ sư PLC, dùng [bộ câu hỏi mới](PLC_ENGINEER_QUESTIONS_2026-11-26.md) thay cho phần PLC của tài liệu đó.
