# PLC Test Specification – theo quy trình giao diện 20/11/2026

| | |
|---|---|
| **Trạng thái** | `SPECIFICATION – PENDING PLC CLARIFICATION`. Đặc tả kiểm thử cho giai đoạn triển khai sau; **không có test nào được viết hoặc sửa trong CR này**. |
| **Cập nhật cho** | Các test PLC/handshake/giao dịch hiện có (mục 6) |
| **Phụ thuộc** | Mỗi nhóm có cột "Điều kiện" – test chỉ hợp lệ sau khi câu hỏi liên quan (PQ-nn) được trả lời |
| **Nguyên tắc** | Test trên **simulator** trước; không bao giờ ghi lên PLC thật để kiểm thử kết nối; commissioning bắt đầu **chỉ-đọc**; ghi chỉ sau khi kỹ sư PLC duyệt tín hiệu và quy trình thử an toàn **[KT]** |

Mã test: `T20-<nhóm>-<số>`. Loại: **U** đơn vị · **I** tích hợp (PostgreSQL + simulator) · **C** commissioning với PLC thật (cần kỹ sư PLC).

## 1. Mapping IO (điều kiện: PQ-20; địa chỉ PQ-01 cho nhóm C)

| ID | Kịch bản | Kỳ vọng | Loại |
|---|---|---|---|
| T20-MAP-01 | Mỗi trạm có đúng 6 tín hiệu mẫu, tên nguyên văn (`PLC_ReaddyTranfer`, `AGV_TranferCompleted`… không sửa chính tả) | Cấu hình hợp lệ; thiếu/thừa ⇒ từ chối | U |
| T20-MAP-02 | Khóa `PLC_ID + Station_ID + Signal_Name` duy nhất; hai trạm cùng tên tín hiệu không ghi đè nhau | Đúng | U |
| T20-MAP-03 | Địa chỉ trùng trong **cùng** PLC bị từ chối; trùng giữa 2 PLC được chấp nhận | Đúng | U |
| T20-MAP-04 | Không có địa chỉ ⇒ `NEEDS_CONFIGURATION`; không polling/ghi (chất lượng `NotConfigured`) | Không có truy cập PLC | U/I |
| T20-MAP-05 | Địa chỉ cũ M9xxx/Dxxxx không được dùng làm mặc định | Không suy ra | U |
| T20-MAP-06 | Phạm vi `HeartBit`/`Spair` theo quyết định (PLC- hoặc trạm-scoped); không nhân bản/gộp âm thầm | Theo quyết định | U |
| T20-MAP-07 | Nhập catalog mới không xóa/đổi tên điểm IO đã có địa chỉ và phê duyệt | Dữ liệu cũ nguyên vẹn | I |

## 2. Giá trị và phân tích tín hiệu (điều kiện: PQ-05, PQ-08, PQ-09)

| ID | Kịch bản | Kỳ vọng | Loại |
|---|---|---|---|
| T20-SIG-01 | `PLC_MissionRequest` 0/1/2 → Không/Pick/Drop | Đúng; 3…10 = **không xác định** ⇒ không tạo yêu cầu, ghi nhật ký | U |
| T20-SIG-02 | `PLC_MissionRequest` > 10 | Theo PQ-05 (lỗi); chưa có ⇒ không hành động | U |
| T20-SIG-03 | `Mission_Feedback`: 0, 1–3, >10 | Chỉ ghi giá trị được duyệt | U |
| T20-SIG-04 | `PLC_MissionCompleted` 0 vs "chưa có kết quả" | Theo PQ-09 | U |
| T20-SIG-05 | Giá trị INT âm/ngoài miền | Không tạo yêu cầu, nhật ký | U |

## 3. Handshake – luồng chính (điều kiện: PQ-02…PQ-10, PQ-16, C-1)

| ID | Kịch bản | Kỳ vọng | Loại |
|---|---|---|---|
| T20-HS-01 | **Pick** hoàn chỉnh: `PLC_MissionRequest`=1 → `Mission_Feedback` → `AGV_RequestTranfer`=1 → `PLC_ReaddyTranfer`=1 → `AGV_TranferCompleted`=1 → `PLC_MissionCompleted`=1 | Giao dịch `Completed`; đúng thứ tự; mọi bước ghi lịch sử | I |
| T20-HS-02 | **Drop** hoàn chỉnh (cùng chuỗi) | Theo PQ-16 | I |
| T20-HS-03 | Đảo thứ tự (ví dụ `PLC_ReaddyTranfer`=1 khi chưa `AGV_RequestTranfer`) | Không dùng tín hiệu bất thường; nhật ký; đối soát nếu quy tắc được duyệt | I |
| T20-HS-04 | `PLC_MissionCompleted`=0 (thất bại) | Giao dịch không `Completed`; tồn kho không bị thay đổi bởi tín hiệu | I |
| T20-HS-05 | `Mission_Feedback` ghi **chỉ khi** AMR đã được RCS gán (CR-028 "sent ≠ assigned") | Không ghi AGV khi chưa gán; không mặc định AGV1 | I |
| T20-HS-06 | Hai yêu cầu liên tiếp ở cùng trạm | Không xử lý trùng; cơ chế theo PQ-10 | I |
| T20-HS-07 | Yêu cầu mới khi cổng đang có giao dịch | Không tạo giao dịch thứ hai | I |
| T20-HS-08 | Trình tự không được hoàn thành bằng "RCS báo Completed" mà thiếu xác nhận thả rack | Leg/giao dịch không hoàn thành; không dispatch chặng kế | I |

## 4. Song song, hàng đợi, ưu tiên (điều kiện: C-1, PQ-13, PQ-14, PQ-22)

| ID | Kịch bản | Kỳ vọng | Loại |
|---|---|---|---|
| T20-PAR-01 | 4 PLC cùng có giao dịch | Chạy đồng thời, không khóa chung | I |
| T20-PAR-02 | Hai trạm cùng PLC phát yêu cầu gần như đồng thời | **Theo D-1 (đã chốt):** chỉ một Handshake `Active`; trạm kia `Queued` theo FIFO; không Handshake đồng thời | I |
| T20-PAR-03 | FIFO trong phạm vi PLC theo thời điểm WCS tiếp nhận; không ưu tiên Pick/Drop | Thứ tự đúng; khóa phá thế hòa theo thiết kế | I |
| T20-PAR-04 | Một PLC offline | Ba PLC còn lại không bị ảnh hưởng | I (đã có test tương tự) |

## 5. Heartbeat, lỗi, phục hồi (điều kiện: PQ-11, PQ-12, PQ-19)

| ID | Kịch bản | Kỳ vọng | Loại |
|---|---|---|---|
| T20-HB-01 | `HeartBit` đổi trạng thái mỗi 1 s (dung sai theo PQ-19), độc lập với chu kỳ polling | Đúng | I |
| T20-HB-02 | WCS không ghi heartbeat khi chế độ không phải ghi-thật-đã-duyệt | Không ghi | I |
| T20-HB-03 | Mất kết nối PLC > timeout 10 s | Giao dịch `Suspended`, không hủy/reset | I |
| T20-REC-01 | Kết nối lại giữa giao dịch | Đọc lại 6 tín hiệu, đối chiếu; tiếp tục hoặc đối soát | I |
| T20-REC-02 | PLC khởi động lại (tín hiệu PLC→SW về 0) | Bất thường ⇒ đối soát; không tự ghi lại lệnh điều khiển | I |
| T20-REC-03 | WCS khởi động lại giữa giao dịch | Khôi phục từ DB; không ghi lặp | I |
| T20-REC-04 | Ghi mơ hồ (timeout sau khi gửi) | Không gửi lại; `WRITE_AMBIGUOUS` | I (đã có) |
| T20-ERR-01 | `Mission_Feedback`>10 / `PLC_MissionRequest`>10 | Theo PQ-05, PQ-15 | I |

## 6. Test hiện có bị ảnh hưởng (chưa sửa)

| Nhóm test hiện có | Lý do ảnh hưởng | Dự kiến |
|---|---|---|
| `HandshakeTests` (Plc) | Dùng `Station_Online`, `Station_Fault`, `Pickup_Request`, `Delivery_Request`, `Transfer_Complete`, `Request_Ack` | Viết lại theo 6 tín hiệu |
| `PlcWcsIntegrationTests`, `Cr008b2PlcIntakeTests` | `Pickup_Request`/`Delivery_Request` + `Request_ID` làm khóa chống trùng | Viết lại theo cơ chế mới |
| `Cr010*` (PlcRequestService, cổng/transaction), `Cr010MultiLegTests.Test07` (hai cổng cùng PLC đồng thời) | `Rack_Full/Rack_Empty` → vị trí; kiểm tra song song theo cổng | Điều chỉnh; `Test07` phụ thuộc quyết định C-1 |
| `PlcConfigTests`, `PlcCr004Tests`, `PlcBulkApprovalTests`, `PlcWrite*Tests` | Danh mục 23 tín hiệu; `WritePolicy` theo tên | Điều chỉnh theo catalog mới và danh sách chặn mới |
| `PlcMcIntegrationTests`, `PlcWorkerTests`, `PlcPollingPolicyTests`, `PlcDiagnostics*` | Codec/worker độc lập tín hiệu | **Giữ nguyên** (có thể chạy làm hồi quy) |
| `PlcMonitoringTests`, giao diện giám sát PLC | Số điểm/trạm | Điều chỉnh |

Quy tắc: **không xóa** test cũ khi chưa có test mới tương đương; test của giao diện 10/11 được đánh dấu `Obsolete` sau khi giao diện mới được duyệt.

## 7. Commissioning với PLC thật (loại C) – thứ tự bắt buộc

1. Xác nhận thông số kết nối (cổng, MC Protocol, mạng/PC/trạm, phạm vi thiết bị) – CLAUDE.md §7.5.
2. **Chỉ đọc**: `PLC_MissionRequest`, `PLC_ReaddyTranfer`, `PLC_MissionCompleted` ở từng trạm; so sánh với quan sát tại hiện trường.
3. Chỉ sau khi kỹ sư PLC duyệt từng tín hiệu và quy trình thử an toàn: thử ghi `HeartBit` (không phải an toàn/chuyển động).
4. Thử `Mission_Feedback`, `AGV_RequestTranfer`, `AGV_TranferCompleted` **chỉ** với quy trình thử an toàn đã duyệt, AMR/cơ cấu ở trạng thái an toàn. Không bao giờ ghi để "thử kết nối".
5. `Spair`: không đọc/ghi.

## 8. Tiêu chí hoàn thành đặc tả test
Mỗi test có (a) PQ liên quan đã trả lời, (b) dữ liệu/tín hiệu rõ ràng, (c) kết quả mong đợi không dựa trên giả định chưa duyệt.

## 9. Test bổ sung theo D-1…D-4 (2026-11-21, [ADR-0011](../../decisions/ADR/ADR-0011-plc-interface-20-11-2026-architectural-decisions.md)) – đặc tả, chưa viết

Điều kiện chung: định nghĩa cửa sổ Handshake (PQ-26) và nguồn dữ liệu rack (PQ-27/PQ-28) cho nhóm tương ứng.

| ID | Kịch bản | Kỳ vọng | Loại |
|---|---|---|---|
| T20-D1-01 | Hai Station cùng PLC có yêu cầu; Station A tới trước | A `Active`; B `Queued`; khi A xong B được cấp lượt; **không** có hai `Active` trong một PLC | I |
| T20-D1-02 | Ba yêu cầu liên tiếp trên một PLC | Phục vụ đúng FIFO (theo thời điểm WCS nhận ra) | I |
| T20-D1-03 | Hai PLC khác nhau có yêu cầu | Mỗi PLC một `Active`, **song song**, không khóa chung | I |
| T20-D1-04 | WCS khởi động lại khi có `Active` và hàng đợi | Hàng đợi và lượt khôi phục từ DB; thứ tự giữ nguyên; không cấp hai lượt | I |
| T20-D1-05 | PLC mất kết nối khi `Active` | Lượt giữ nguyên (không hủy/reset); yêu cầu chờ giữ nguyên | I |
| T20-D1-06 | Yêu cầu của Station đang `Queued` bị PLC hạ xuống (nếu PLC phát xung/hủy) | Hành vi theo PQ-02/PQ-26 | I |
| T20-D1-07 | Mã hiện tại **không** có FIFO cấp PLC: test hiện có (ví dụ `Cr010MultiLegTests.Test07`) được đánh dấu để viết lại, không được dùng làm bằng chứng cho D-1 | Danh sách test cần sửa nêu rõ | – |
| T20-D2-01 | Không bao giờ ghi 1–3 làm định danh AMR; không ánh xạ `RobotCode`→AGV1–3 | Không có ghi | I |
| T20-D2-02 | `Mission_Feedback` chỉ phản ánh trạng thái tiếp nhận/xử lý theo giao diện phản hồi được duyệt; `RobotCode` thật được WCS lưu (CR-028) | Đúng | I |
| T20-D2-03 | Đội AMR >3 xe hoặc `RobotCode` ngoài bảng AGV | Không tạo AGV giả, không lỗi do enum | I |
| T20-D4-01 | `PLC_MissionCompleted`=1 nhưng thiếu xác nhận rack có mặt | Không hoàn thành bàn giao; `ReconciliationRequired`; không dispatch chặng kế | I |
| T20-D4-02 | Danh tính rack không khớp (khi quy trình yêu cầu) | Dừng, đối soát; không cập nhật vị trí rack | I |
| T20-D4-03 | Cùng giao dịch báo hai lần / yêu cầu lặp | Không xử lý trùng (cơ chế định danh được duyệt) | I |
| T20-D4-04 | Điều kiện an toàn/interlock không thỏa | WCS không ra lệnh mở cửa/kẹp | I/C |
| T20-D4-05 | Rack có mặt và danh tính khớp, WCS cập nhật vị trí/trạng thái thành công, lịch sử ghi | Bàn giao hoàn tất, cho phép chặng kế | I |
| T20-D4-06 | Nguồn dữ liệu rack (PLC bổ sung/thiết bị khác) mất kết nối | Giao dịch dừng/đối soát; không giả định có rack | I |
