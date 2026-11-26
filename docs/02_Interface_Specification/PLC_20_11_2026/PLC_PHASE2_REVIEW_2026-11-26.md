# PLC Interface – Giai đoạn 2: tổng kết

| | |
|---|---|
| **Bản** | **Rev 6** (2026-11-26, CR-046). Các bản trước trong lịch sử Git |
| **Trạng thái** | Giai đoạn 2 **hoàn tất**. Hợp đồng IO **chờ kỹ sư PLC xác nhận**: không `APPROVED`, không `READY FOR IMPLEMENTATION` |
| **Development** | Catalog, API, giao diện theo IO Map 22/11; handshake chưa hiện thực |
| **Deploy readiness** | Image và Compose đã dựng, chạy, kiểm tra trên Linux (GitHub Actions). **Chưa** chạy trên máy kiểm thử đích |
| **Real PLC verification** | **Chưa thực hiện** |
| **IO Map duy nhất** | [`AGV_PLC Interface_List_22_11_2026.xlsx`](../PLC_IO_Map/AGV_PLC%20Interface_List_22_11_2026.xlsx), SHA-256 `405586c9e4d2893f1d3a33922259ecfe1c400ca877710877b3b482e049b34da3`. Trạng thái từng tín hiệu: [README](../PLC_IO_Map/README.md) |
| **Kiểm chứng** | Workbook, source, test mục tiêu, trình duyệt, Docker (xem [CR-046](../../change-management/CR-046_IO_MAP_CATALOG_UI_F1_F2_DOCKER.md) §7). Không kết nối PLC |

Ký hiệu: **[QĐ]** chủ dự án quyết định · **[WB]** workbook thể hiện · **[PLC]** chờ kỹ sư PLC.

## 1. Đã tích hợp vào `main`

| Hạng mục | Trạng thái |
|---|---|
| CR-042, CR-044, CR-045, tài liệu Phase 2 | Trong `main` (`f503f0c`) |
| **OW-2** lớp chặn ghi PLC mặc định | Đã triển khai và kiểm thử (CR-044, CR-045); CR-046 thêm cấm ghi mọi tín hiệu chưa xác nhận |
| **OW-4** `PLC_MissionCompleted`: `1` Failure, `2` Success | Đã sửa trong tài liệu |
| PLC catalog theo IO Map 22/11 | **Đã cập nhật** (CR-046); dòng cũ giữ trong database, không dùng |
| Giao diện IO | **Đã sửa** (CR-046 §3), kiểm tra trên trình duyệt |
| Docker | Dựng và chạy được trên Linux (CR-046 §5); chưa trên máy đích |
| F1 (`PER_AGV` giữa hai engine) | **Đã sửa** |
| F2 (bế tắc do giữ chỗ) | Chuỗi đã tái hiện **đã sửa** cho chặng đầu; phần còn lại ở CR-046 §4 |
| Handshake theo IO Map | **Chưa hiện thực** (Giai đoạn 3) |

## 2. Quyết định

### 2.1 Hợp đồng IO (PD)

| Mã | [QĐ] | Còn chờ [PLC] |
|---|---|---|
| PD-1 | `AGV_RequestTranfer` là yêu cầu gửi PLC, không điều khiển trực tiếp cửa/kẹp; PLC tự xử lý theo logic và interlock; `PLC_ReaddyTranfer` là phản hồi | Xác nhận interlock, cách từ chối (PLC-OQ-16) |
| PD-2 | Tín hiệu handshake giữ trạng thái; bên ghi tự OFF tín hiệu của mình. Chưa triển khai logic reset | Bảng handshake (PLC-OQ-03) |
| PD-3 | `AGV_RequestTranfer` và `AGV_TranferCompleted` dùng chung một thanh ghi INT mỗi trạm; yêu cầu 1 / 4, xong Pick 3, **xong Drop 5** | Địa chỉ, bảng mã (PLC-OQ-01). **IO Map 22/11 ghi xong Drop = `6`** – xem §7, Q-1 |
| PD-4 | Một giá trị INT không đủ kết luận hoàn thành khi chưa xác minh trình tự | PLC-OQ-21 |

### 2.2 Quản lý công việc (OW)

| Mã | Nội dung | Trạng thái |
|---|---|---|
| OW-1 | Workbook 20/11 làm căn cứ phân tích | `CLOSED` – thay bằng IO Map 22/11 (CR-045) |
| OW-2 | Lớp chặn ghi mặc định | `DONE` (CR-044, CR-045); chưa deploy |
| OW-3 | Không dùng enum 1–3 làm định danh AGV cố định khi chưa xác nhận | `DECIDED` |
| OW-4 | `1` thất bại, `2` thành công | `DONE` |
| OW-5 | Khôi phục IO Map v2 làm lịch sử | `CLOSED` – quyết định mới: chỉ dùng 22/11, **không khôi phục** file cũ |
| OW-6 | File 10/11 bị xóa trong working tree | `CLOSED` – quyết định mới không dùng bản 10/11; file đã gỡ khỏi repository, còn ở commit `cce0d1e` |
| OW-7 | A3/A4, D1/D2 chỉ giám sát | `DECIDED` |
| OW-8 | Giai đoạn 3 làm trên nhánh mới từ `main` | `DECIDED` |

C-8 (câu "cột địa chỉ trống" trong `CLAUDE.md` §7.8): giữ nguyên theo chỉ đạo.

## 3. Nội dung IO Map 22/11

Bảng tín hiệu, địa chỉ, giá trị và trạm: [README của IO Map](../PLC_IO_Map/README.md). So với bản 20/11 (`a291d28f`): xong Drop `4` → `6` ở cả 16 trạm; dòng heartbeat và dự phòng ghi "dùng chung cho PLC 1…4". Địa chỉ, ô gộp, trình tự `Trang_tính1` không đổi.

Trình tự trên sheet: `PLC_MissionRequest` → `Mission_Feedback` → `AGV_RequestTranfer` → `PLC_ReaddyTranfer` → `AGV_TranferCompleted` → `PLC_MissionCompleted`. Sheet không nêu reset, thời hạn, nhánh lỗi, khác biệt của Drop. Không suy diễn các điểm này.

## 4. Đối chiếu với hệ thống (sau CR-046)

| IO | Trong hệ thống | Khoảng trống |
|---|---|---|
| 6 tín hiệu mỗi trạm | Có trong catalog, API, giao diện; địa chỉ tham chiếu; đọc được khi Admin nhập địa chỉ; cấm ghi thật | Chưa có xử lý nghiệp vụ, enum, reset |
| `AGV_RequestTranfer` / `AGV_TranferCompleted` | Đánh dấu dùng chung thanh ghi | Chỉ một tín hiệu nhận được địa chỉ cấu hình |
| `HeartBit`, dự phòng | Hiển thị cấp PLC | Chưa phát heartbeat (PLC-OQ-11) |
| Trạm | Theo workbook: X4, G1, G2. X6, G3, G7 giữ trong database cũ, không ghép | PLC-OQ-14 |
| Tiếp nhận yêu cầu | Chưa theo IO Map; mô phỏng dùng tín hiệu thử trong bộ nhớ | Giai đoạn 3 |
| Sự kiện RCS "đến trước cửa", "đã ra ngoài" | Chưa có | Chặn bước 3 và 5 |
| Kết nối 4 PLC, IP | Dùng lại được | Tham số MC: `NEEDS_CONFIGURATION` |

## 5. Rủi ro

| Mã | Rủi ro | Hiện trạng |
|---|---|---|
| K-1 | Địa chỉ mẫu cũ (`D1600`, `D1610`) đè lên thanh ghi yêu cầu mới | Chặn ghi Real ở thanh ghi dành riêng (CR-044); chỉ có hiệu lực nơi đã deploy bản mới |
| K-2 | Văn bản cũ coi `MissionCompleted = 1` là thành công | Đã sửa và đính chính |
| K-4 | Giá trị xong Drop chưa thống nhất (`5` / `6`) | Mở: Q-1 |
| K-5 | Chưa có handshake cho IO Map | Mở: Giai đoạn 3 |
| K-6 | Tín hiệu mới không nằm trong danh sách cấm theo tên | Đã chuyển sang chặn mặc định |

K-3 và K-7 (nhiều phiên bản workbook, file bị xóa không kiểm soát): đã đóng bằng quyết định IO Map duy nhất.

## 6. Câu hỏi cho kỹ sư PLC

Bản gửi đi: [PLC_ENGINEER_QUESTIONS_2026-11-26.md](PLC_ENGINEER_QUESTIONS_2026-11-26.md). **21/21 đang `OPEN`** (chưa có trả lời bằng văn bản).

| Mã | Chủ đề | Ghi chú | Trạng thái |
|---|---|---|---|
| PLC-OQ-01 | Địa chỉ thanh ghi chung và bảng mã | Trọng tâm; xong Drop `6` theo IO Map | `OPEN` |
| PLC-OQ-02 | Mã Drop khác nhau giữa các tín hiệu | | `OPEN` |
| PLC-OQ-03 | Bảng handshake Pick và Drop | Trọng tâm | `OPEN` |
| PLC-OQ-04 | Chống trùng yêu cầu | | `OPEN` |
| PLC-OQ-05 | Thời điểm ghi, huỷ yêu cầu | | `OPEN` |
| PLC-OQ-06 | `Mission_Feedback` | | `OPEN` |
| PLC-OQ-07 | PLC báo lỗi hoặc không trả lời | | `OPEN` |
| PLC-OQ-08 | Điều kiện báo hoàn thành | | `OPEN` |
| PLC-OQ-09 | Thất bại của `MissionCompleted` | | `OPEN` |
| PLC-OQ-10 | Hai chú thích "thay đổi theo" | | `OPEN` |
| PLC-OQ-11 | `HeartBit` | IO Map đã ghi một tín hiệu mỗi PLC; còn chu kỳ và hành vi | `OPEN` |
| PLC-OQ-12 | Mất kết nối, khởi động lại | | `OPEN` |
| PLC-OQ-13 | Kết nối và tham số MC | | `OPEN` |
| PLC-OQ-14 | Danh sách trạm | | `OPEN` |
| PLC-OQ-15 | Dữ liệu rack | | `OPEN` |
| PLC-OQ-16 | Interlock, từ chối yêu cầu | | `OPEN` |
| PLC-OQ-17 | Một handshake mỗi PLC | | `OPEN` |
| PLC-OQ-18 | Định dạng INT | | `OPEN` |
| PLC-OQ-19 | Chương trình PLC theo đúng IO Map này | | `OPEN` |
| PLC-OQ-20 | Chuyển đổi chương trình PLC | | `OPEN` |
| PLC-OQ-21 | Hành vi thanh ghi dùng chung | Trọng tâm | `OPEN` |

## 7. Điểm cần chủ dự án quyết định

| Mã | Câu hỏi | Đề xuất | Nếu chưa quyết |
|---|---|---|---|
| **Q-1** | Xong Drop là `5` (PD-3) hay `6` (IO Map 22/11)? | **`6`**: là giá trị của IO Map duy nhất, không trùng mã khác; ghi nhận PD-3 được sửa | Không cản build và deploy (không mã nào gửi giá trị; tín hiệu bị cấm ghi thật); cản Giai đoạn 3 và việc chốt với kỹ sư PLC |

## 8. Điều kiện bắt đầu tích hợp PLC thật

| # | Điều kiện | Trạng thái |
|---|---|---|
| 1 | Kỹ sư PLC trả lời PLC-OQ-01, 03, 21 và xác nhận interlock (16) | Chưa |
| 2 | Q-1 được chốt | Chưa |
| 3 | Tham số kết nối MC và danh sách trạm (13, 14) | Chưa |
| 4 | CR-029 được phê duyệt; Change Request Giai đoạn 3 và kế hoạch chuyển đổi mapping | Chưa |
| 5 | Hiện thực handshake theo IO Map 22/11 và kiểm thử với PLC giả lập; gỡ trạng thái "chưa xác nhận" | Chưa (catalog đã xong) |
| 6 | Bản đã deploy lên máy kiểm thử; chạy checklist triển khai | Chưa |
| 7 | Bắt đầu **chỉ đọc**; kỹ sư PLC phê duyệt từng điểm trước khi khai báo ghi | Chưa |

Kiểm tra đường truyền ở chế độ chỉ đọc (điều kiện 3 và 6) có thể làm trước các điều kiện còn lại khi kỹ sư PLC đồng ý; không ghi tín hiệu nào.

## 9. Đề xuất cập nhật CR-029 (trạng thái giữ `PENDING_APPROVAL`)

1. Lấy IO Map 22/11 làm tài liệu nguồn; lưu lịch sử các bản cũ.
2. Cập nhật statements và xung đột theo §3–§5; ghi PD-1…PD-4, OW-1…OW-8 và Q-1.
3. Liên kết PLC-OQ-01…21 với PQ-01…29; chỉ đóng sau khi có trả lời.
