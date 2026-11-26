# Câu hỏi cho kỹ sư PLC và nhà cung cấp RCS

| | |
|---|---|
| **Dự án** | ETEK WCS – Phase 2 |
| **Ngày lập** | 2026-11-26 |
| **Cách trả lời** | Trả lời bằng văn bản theo mã câu hỏi; kèm tài liệu, mẫu bản tin hoặc ảnh chụp cấu hình. Câu chưa xác định được: ghi "chưa xác định" và người chịu trách nhiệm |
| **Quy ước** | Phần mềm WCS không giả định giá trị nào. Chức năng liên quan được giữ khóa cho tới khi có câu trả lời |

Tài liệu bổ sung, không thay thế [PQ-01…PQ-29](../02_Interface_Specification/PLC_20_11_2026/PLC_VENDOR_CLARIFICATION_QUESTIONS_20_11_2026.md) và [câu hỏi RCS CR-028](../03_RCS_Interface/VENDOR_QUESTIONS_CR-028.md). Tên tín hiệu theo IO Map v2 (bản cũ, dùng để đối chiếu); bản giao diện 20/11/2026 chưa được phê duyệt. Xin cho biết tên tương ứng trong bản 20/11/2026.

## 1. Hiện trạng phía WCS

**PLC → WCS (13 tín hiệu mỗi trạm, 16 trạm)**

| Tín hiệu | Kiểu | WCS đang dùng |
|---|---|---|
| Pickup_Request, Delivery_Request | Bool | Cạnh lên, chỉ cho luồng Phase 1 khi tích hợp được bật |
| Request_ID | String | Khóa chống trùng |
| Rack_Full, Rack_Empty, Destination_Available, Rack_Type | Int | Chỉ dùng trong mô phỏng luồng Phase 2 (mã vị trí 1–4 = A1–A4) |
| Safety_Ready, Station_Online, Station_Ready, Rack_Present, Station_Fault, Transfer_Complete | Bool | Không dùng để tiếp nhận yêu cầu |

**WCS → PLC (10 tín hiệu):** Heartbeat_SW, Request_Ack, AGV_Assigned, AGV_ID, AGV_Arrived, Transfer_Permit, AGV_Departure_Permit, AGV_Busy, AGV_Fault, Error_Code. WCS không ghi Request_Ack và Transfer_Permit vì chưa có quy tắc được duyệt.

**RCS:** WCS gọi `genAgvSchedulingTask`, `continueTask`, `cancelTask`, `queryTaskStatus` và truy vấn đội AMR. Callback nhận `start`, `outbin`, `end`, `cancel`, `ctu`, hiện chỉ áp dụng cho task một chặng của Phase 1. `end` không hoàn tất task nếu chưa xác nhận đặt rack. Quy trình nhiều chặng (Phase 2) chưa nhận callback và chưa chạy với RCS thật.

## 2. Kỹ sư PLC

### 2.1 Tiếp nhận yêu cầu (OQ-41-02)

| Mã | Câu hỏi |
|---|---|
| PLC-01 | `Pickup_Request` / `Delivery_Request` là tín hiệu mức hay xung? Khi nào được tính là yêu cầu mới? |
| PLC-02 | `Request_ID`: ai sinh, đổi lúc nào, có bảo đảm không lặp sau khi PLC khởi động lại? Kiểu và độ dài thực? |
| PLC-03 | PLC ghi `Request_ID` trước hay sau khi bật bit yêu cầu? Khoảng ổn định tối thiểu? |
| PLC-04 | Ai reset bit yêu cầu, theo điều kiện nào? WCS có phải bật `Request_Ack`, giữ bao lâu, ai reset? |
| PLC-05 | `Request_ID` đổi trong khi bit vẫn ON có phải là yêu cầu mới? (WCS hiện bỏ qua.) |
| PLC-06 | Sau mất kết nối hoặc WCS khởi động lại, bit còn ON được xử lý thế nào? PLC có phân biệt được yêu cầu cũ? |
| PLC-07 | Thời hạn chờ của một yêu cầu chưa được xử lý; PLC làm gì khi hết hạn? |
| PLC-08 | `Rack_Full`, `Rack_Empty`, `Destination_Available` mã hóa vị trí thế nào ngoài A1–A4? |

### 2.2 Một giao dịch handshake mỗi PLC (OQ-55)

Đã quyết định: tại một thời điểm mỗi PLC chỉ có một giao dịch handshake, các giao dịch khác xếp hàng. Chưa hiện thực vì cần:

| Mã | Câu hỏi |
|---|---|
| PLC-09 | Sự kiện nào bắt đầu và sự kiện nào kết thúc một giao dịch đang hoạt động? |
| PLC-10 | Trạm thứ hai cùng PLC gửi yêu cầu trong lúc đó: PLC giữ bit bao lâu? |
| PLC-11 | Handshake lỗi hoặc treo: ai giải phóng, bằng tín hiệu nào? |
| PLC-12 | Thứ tự hàng chờ theo thời điểm WCS đọc được hay theo dữ liệu của PLC? |

### 2.3 Bằng chứng bàn giao rack (OQ-42-05)

Tín hiệu "hoàn thành nhiệm vụ" một mình không đủ để WCS cập nhật vị trí rack.

| Mã | Câu hỏi |
|---|---|
| PLC-13 | Tín hiệu nào chứng minh rack đã được lấy đi / đặt xuống? `Transfer_Complete` bật khi nào, reset ra sao? |
| PLC-14 | `Rack_Present` có tin cậy ở mọi trạm không? Nó đổi trước hay sau `Transfer_Complete`? |
| PLC-15 | Có thiết bị nhận diện rack không? Nếu PLC không cung cấp mã rack, thiết bị nào cung cấp? |
| PLC-16 | Điều kiện an toàn nào phải đúng trước khi WCS xác nhận bàn giao? |

### 2.4 Trạm và chuyển đổi

| Mã | Câu hỏi |
|---|---|
| PLC-17 | Quy trình Phase 2 dùng vị trí X4, Y1, Y2, Z1, Z2, M5, M6; danh sách 16 trạm có X1, X2, X3, X6 nhưng không có các vị trí đó. Vị trí nào có tín hiệu PLC, thuộc PLC nào? (liên quan PQ-20) |
| PLC-18 | Khi chuyển một trạm sang cách xử lý mới, PLC có thể tạm khóa phát yêu cầu tại trạm và phát lại sau không? |

## 3. Nhà cung cấp RCS

### 3.1 Chặng vận chuyển (OQ-42-06)

| Mã | Câu hỏi |
|---|---|
| RCS-01 | Với task tạo cho một chặng (lấy tại một điểm, đặt tại một điểm), RCS gửi những `method` nào, thứ tự và điều kiện từng cái? |
| RCS-02 | Callback nào chứng minh đã lấy rack và đã đặt rack? Có cần cấu hình trigger (`needTrigger`, `triggerTyp`) trong mẫu task? Xin gửi mẫu thực tế. |
| RCS-03 | `taskTyp`, định dạng vị trí và tham số bắt buộc cho từng nhóm tuyến? |
| RCS-04 | Có tạo được task khi chưa biết điểm đặt (chọn sau khi đã lấy rack) không? Bổ sung điểm đặt bằng lời gọi nào? AMR làm gì trong lúc chờ? |
| RCS-05 | `reqCode` gửi lại có được khử trùng không? Trong bao lâu? |
| RCS-06 | `queryTaskStatus` trả những trạng thái nào; sau bao lâu task vừa gửi mới truy vấn được? |
| RCS-07 | Callback có gửi lại khi WCS không trả lời? Thứ tự có bảo đảm? Callback đến sau khi hủy được xử lý thế nào? |
| RCS-08 | `cancelTask`: rack ở đâu sau khi hủy, theo từng giá trị `forceCancel`? |
| RCS-09 | Xác thực callback: header, secret, dải IP nguồn? |

### 3.2 Điều khiển giao thông (OQ-54)

| Mã | Câu hỏi |
|---|---|
| RCS-10 | RCS có bảo đảm hai AMR không xung đột trên lối đi hai chiều, giao lộ và điểm giao nhận không? Cần cấu hình gì trên bản đồ? Xin xác nhận bằng văn bản. |
| RCS-11 | Khi hai task đối đầu (mỗi AMR cần chỗ AMR kia đang đứng hoặc đang giữ), RCS xử lý thế nào? Có báo cho WCS không? |
| RCS-12 | Một AMR đang giữ rack chờ điểm đặt có chặn đường AMR khác không? Có điểm chờ được cấu hình không? |
| RCS-13 | Một AMR có thể được RCS giao hai task cùng lúc không? RCS xử lý task thứ hai thế nào? |

## 4. Chức năng đang khóa

| Chức năng | Chờ |
|---|---|
| PLC thật tạo lệnh Phase 2; chuyển trạm PLC thật sang cách xử lý mới | PLC-01…08 |
| Hàng đợi một giao dịch mỗi PLC | PLC-09…12 |
| Tự xác nhận lấy/đặt rack từ PLC | PLC-13…16 |
| Chạy lệnh Phase 2 với RCS thật | RCS-01…09 |
| Cho phép vận chuyển hai chiều đồng thời | RCS-10…13 |
