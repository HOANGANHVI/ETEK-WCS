# Giao tiếp PLC ↔ phần mềm AGV (ETEK WCS) – Đề nghị kỹ sư PLC xác nhận

| | |
|---|---|
| **Ngày lập** | 2026-11-26 |
| **Tài liệu đối chiếu** | `AGV_PLC Interface_List_22_11_2026.xlsx`, sheet `IO Matrix` và `Trang_tính1`; mã kiểm tra SHA-256 `405586c9e4d2893f1d3a33922259ecfe1c400ca877710877b3b482e049b34da3` |
| **Tình trạng** | Hợp đồng IO **chưa được chốt**. Mọi câu hỏi đang `OPEN`. Phần mềm chưa ghi gì tới PLC theo tài liệu này |
| **Cách trả lời** | Trả lời bằng văn bản theo mã câu hỏi, điền các bảng ở mục 2 và 3; gửi kèm tài liệu hoặc sơ đồ. Nội dung chưa xác định được: ghi "chưa xác định" và người phụ trách |

Trong tài liệu: **"Theo workbook"** là nội dung file ở trên; **"Phía dự án đã chọn"** là định hướng đã chốt phía dự án, xin kỹ sư PLC xác nhận khả thi; **"Gợi ý để thảo luận"** chưa được thống nhất. Nếu một định hướng không phù hợp với chương trình PLC, xin nêu lý do và phương án thay thế.

## 1. Định hướng phía dự án, xin xác nhận

1. `AGV_RequestTranfer` là **yêu cầu** phần mềm gửi tới PLC, không trực tiếp điều khiển cửa hay kẹp. PLC tiếp nhận và tự điều khiển cửa/kẹp theo logic và interlock của PLC. Phần mềm không coi việc đã ghi yêu cầu là cửa/kẹp đã mở; phản hồi từ PLC là `PLC_ReaddyTranfer`.
2. Tín hiệu handshake **giữ trạng thái** (không phải xung). Bên nào ghi tín hiệu thì bên đó đưa tín hiệu về trạng thái OFF; không bên nào reset tín hiệu của bên kia.
3. `AGV_RequestTranfer` và `AGV_TranferCompleted` **dùng chung một thanh ghi INT cho mỗi trạm**, phân biệt bằng giá trị theo bảng ở mục 2.1.
4. Các trạm A3, A4, D1, D2: phía dự án chỉ giám sát (đọc), không ghi tín hiệu của các trạm này.
5. Khi chưa có thỏa thuận khác, phần mềm không dùng giá trị `1–3` của `Mission_Feedback` làm mã định danh cố định của xe AGV (xem PLC-OQ-06).

**Câu hỏi trọng tâm: PLC-OQ-01, PLC-OQ-03 và PLC-OQ-21** (mục 4.1).

### Cần trả lời trước (bản ngắn)

| Mã | Xin xác nhận |
|---|---|
| PLC-OQ-14 | Trạm của PLC `.121` và `.131`: **X4, G1, G2** (workbook) hay **X6, G3, G7** (tài liệu cũ)? Nếu là trạm khác nhau, mỗi trạm ở đâu trên mặt bằng? |
| PLC-OQ-01, 02 | Giá trị hoàn thành Drop là `6`? Chương trình PLC đang dùng giá trị nào? |
| PLC-OQ-21, 03 | `D16n0` dùng chung: trình tự đổi giá trị và reset; điều kiện chấp nhận mỗi giá trị |
| PLC-OQ-13 | Cổng, loại frame và tham số MC của 4 PLC |
| PLC-OQ-11 | `M9200`: chu kỳ và hành vi khi quá 10 giây |

## 2. Thanh ghi dùng chung (PLC-OQ-01)

### 2.1 Bảng mã

| Giá trị | Ý nghĩa | Tín hiệu | Kỹ sư PLC xác nhận |
|---:|---|---|---|
| 1 | Yêu cầu mở cửa/kẹp để Pick | `AGV_RequestTranfer` | |
| 4 | Yêu cầu mở cửa/kẹp để Drop | `AGV_RequestTranfer` | |
| 3 | Hoàn thành Pick | `AGV_TranferCompleted` | |
| 6 | Hoàn thành Drop | `AGV_TranferCompleted` | |
| 0 và giá trị khác | Chưa định nghĩa | – | Xin định nghĩa |

Giá trị "hoàn thành Drop" theo workbook 22/11 là `6`. Các bản trước dùng `4`, và trong trao đổi từng có đề nghị `5`: xin cho biết giá trị chương trình PLC đang dùng hoặc sẽ dùng.

### 2.2 Địa chỉ theo PLC và trạm

Địa chỉ theo ô gộp hai dòng trong workbook. Xin xác nhận hoặc sửa từng dòng.

| PLC (IP) | Trạm | Thanh ghi theo workbook | Xác nhận / địa chỉ đúng |
|---|---|---|---|
| 192.168.10.101 | A1 | D1600 | |
| 192.168.10.101 | A2 | D1610 | |
| 192.168.10.101 | A3 | D1620 | |
| 192.168.10.101 | A4 | D1630 | |
| 192.168.10.111 | B1 | D1600 | |
| 192.168.10.111 | B2 | D1610 | |
| 192.168.10.111 | D1 | D1620 | |
| 192.168.10.111 | D2 | D1630 | |
| 192.168.10.121 | X1 | D1600 | |
| 192.168.10.121 | X2 | D1610 | |
| 192.168.10.121 | X3 | D1620 | |
| 192.168.10.121 | X4 | D1630 | |
| 192.168.10.131 | E1 | D1600 | |
| 192.168.10.131 | E2 | D1610 | |
| 192.168.10.131 | G1 | D1620 | |
| 192.168.10.131 | G2 | D1630 | |

### 2.3 Hệ quả của việc dùng chung thanh ghi

Mỗi lần ghi giá trị mới sẽ **ghi đè** giá trị cũ:

- khi phần mềm ghi `3` hoặc `6`, giá trị yêu cầu (`1` hoặc `4`) không còn được giữ;
- khi phần mềm ghi `0` (nếu `0` là trạng thái OFF), cả yêu cầu và báo hoàn thành cùng mất.

Xin kỹ sư PLC xác nhận **trình tự chuyển giá trị và reset**, ví dụ `1 → 3 → 0` hay `1 → 0 → 3 → 0`, ai chờ điều kiện nào giữa hai lần ghi, và PLC cần giữ mỗi giá trị tối thiểu bao lâu để đọc được (PLC-OQ-21).

## 3. Bảng handshake (PLC-OQ-03)

Xin điền đầy đủ cho cả Pick và Drop. Phần đã điền lấy từ workbook và mục 1; ô trống là nội dung chưa được thống nhất. Cột "Bên OFF" theo nguyên tắc 2 ở mục 1.

### 3.1 Pick

| Bước | Tín hiệu | Bên ghi / bên OFF | Giá trị | Điều kiện ghi | Điều kiện sang bước sau | Điều kiện và thời điểm OFF; giá trị OFF | Thời hạn | Khi lỗi / mất kết nối |
|---|---|---|---|---|---|---|---|---|
| 1 | `PLC_MissionRequest` | PLC | 1 | | | | | |
| 2 | `Mission_Feedback` | Phần mềm | (xem PLC-OQ-06) | | | | | |
| 3 | `AGV_RequestTranfer` | Phần mềm | 1 | AGV ở vị trí trước cửa (chi tiết?) | | | | |
| 4 | `PLC_ReaddyTranfer` | PLC | 1 (10 = lỗi) | | | | | |
| 5 | `AGV_TranferCompleted` | Phần mềm | 3 | AGV đã lấy và ra vị trí ngoài (chi tiết?) | | | | |
| 6 | `PLC_MissionCompleted` | PLC | 2 thành công, 1 thất bại | | | | | |

### 3.2 Drop

| Bước | Tín hiệu | Bên ghi / bên OFF | Giá trị | Điều kiện ghi | Điều kiện sang bước sau | Điều kiện và thời điểm OFF; giá trị OFF | Thời hạn | Khi lỗi / mất kết nối |
|---|---|---|---|---|---|---|---|---|
| 1 | `PLC_MissionRequest` | PLC | 2 | | | | | |
| 2 | `Mission_Feedback` | Phần mềm | (xem PLC-OQ-06) | | | | | |
| 3 | `AGV_RequestTranfer` | Phần mềm | 4 | | | | | |
| 4 | `PLC_ReaddyTranfer` | PLC | 2 (10 = lỗi) | | | | | |
| 5 | `AGV_TranferCompleted` | Phần mềm | 6 | | | | | |
| 6 | `PLC_MissionCompleted` | PLC | 2 thành công, 1 thất bại | | | | | |

Trong cả hai bảng, bước 5 ghi vào **cùng thanh ghi** với bước 3 (mục 2.3).

Xin trả lời thêm: điều kiện để **bắt đầu giao dịch tiếp theo** tại cùng trạm; tín hiệu nào phải về OFF trước đó.

## 4. Câu hỏi

### 4.1 Câu hỏi trọng tâm

| Mã | Câu hỏi | Gợi ý để thảo luận | Tình trạng |
|---|---|---|---|
| PLC-OQ-01 | Xác nhận địa chỉ dùng chung cho từng PLC/trạm (mục 2.2) và bảng mã `1`, `4`, `3`, `6` (mục 2.1) | – | OPEN |
| PLC-OQ-03 | Điền bảng handshake mục 3 cho Pick và Drop: bên ON/OFF, điều kiện chuyển bước, điều kiện và thời điểm OFF/reset, thời hạn, lỗi và phục hồi; ý nghĩa giá trị `0` của từng tín hiệu; điều kiện bắt đầu giao dịch tiếp theo | – | OPEN |
| PLC-OQ-21 | (a) PLC phân biệt `1`, `4`, `3`, `6` theo trạng thái giao dịch thế nào? (b) Điều kiện PLC chấp nhận giá trị yêu cầu và giá trị hoàn thành? (c) Điều kiện chuyển từ yêu cầu sang hoàn thành; ghi đè trực tiếp hay phải qua `0`? (d) Đưa thanh ghi về giá trị nào sau từng bước, khi nào; mỗi giá trị cần giữ tối thiểu bao lâu? (e) Nhận giá trị không hợp lệ hoặc sai thứ tự (ví dụ `3` khi đang Drop, `6` khi chưa yêu cầu) thì xử lý và báo lại thế nào? (f) Chống nhận lặp cùng một giá trị; sau khi mất kết nối rồi nối lại, giá trị đang giữ được hiểu thế nào? | Giá trị chỉ có hiệu lực khi đúng trạng thái giao dịch; giá trị lạ hoặc sai thứ tự không gây hành động và được báo lỗi | OPEN |

### 4.2 Trình tự

| Mã | Câu hỏi | Gợi ý để thảo luận | Tình trạng |
|---|---|---|---|
| PLC-OQ-02 | Thao tác Drop dùng mã `2` ở `PLC_ReaddyTranfer`, `4` ở yêu cầu và `6` ở hoàn thành. Xác nhận từng tín hiệu có bảng mã riêng như vậy | Giữ bảng mã riêng, ghi rõ trong workbook | OPEN |
| PLC-OQ-04 | Không còn mã yêu cầu duy nhất. Làm sao phân biệt hai yêu cầu liên tiếp cùng loại tại một trạm và tránh xử lý lặp, đặc biệt sau khởi động lại? | (a) PLC cung cấp số thứ tự giao dịch. (b) Chỉ coi là yêu cầu mới khi đã thấy tín hiệu về `0` | OPEN |
| PLC-OQ-05 | Chính xác khi nào phần mềm được ghi `AGV_RequestTranfer`? Khi nào được rút yêu cầu (AGV huỷ nhiệm vụ)? | Chỉ ghi khi hệ thống điều khiển AGV xác nhận AGV đã đến điểm chờ trước cửa | OPEN |
| PLC-OQ-08 | Điều kiện ghi `AGV_TranferCompleted` cho Pick và cho Drop? Sơ đồ trong workbook chỉ mô tả "AGV đã lấy và ra ngoài" | Cung cấp sơ đồ Drop | OPEN |
| PLC-OQ-10 | Chú thích "thay đổi theo `PLC_MissionCompleted`" và "thay đổi theo `Mission_Feedback` và `AGV_TranferCompleted`" nghĩa là gì: giá trị trước, giá trị sau, điều kiện? | – | OPEN |
| PLC-OQ-17 | Phía dự án dự kiến mỗi PLC chỉ có một giao dịch handshake tại một thời điểm. Có khả thi không? Giao dịch bắt đầu và kết thúc ở bước nào? Trạm đang chờ giữ yêu cầu thế nào? | Tính từ bước 3 đến bước 6 | OPEN |

### 4.3 Lỗi, an toàn, phục hồi

| Mã | Câu hỏi | Gợi ý để thảo luận | Tình trạng |
|---|---|---|---|
| PLC-OQ-07 | `PLC_ReaddyTranfer = 10` hoặc PLC không trả lời: thời hạn chờ, phần mềm và AGV làm gì? | AGV chờ ngoài cửa, báo người vận hành; không tự lặp yêu cầu | OPEN |
| PLC-OQ-09 | `PLC_MissionCompleted = 1` (thất bại): các nguyên nhân, phần mềm làm gì, PLC đưa tín hiệu về OFF khi nào? | Dừng giao dịch, người vận hành đối soát vị trí rack | OPEN |
| PLC-OQ-12 | Một bên mất kết nối hoặc khởi động lại giữa giao dịch: giá trị nào được giữ, đối soát thế nào? | Giữ giá trị; hai bên đối chiếu trước khi tiếp tục; mỗi bên chỉ sửa tín hiệu của mình | OPEN |
| PLC-OQ-16 | Xác nhận PLC tự điều khiển cửa/kẹp và tự bảo đảm interlock khi nhận yêu cầu. Khi điều kiện an toàn không đạt, PLC phản hồi thế nào? | Văn bản xác nhận kèm danh sách điều kiện | OPEN |

### 4.4 Phản hồi, heartbeat, dữ liệu rack

| Mã | Câu hỏi | Gợi ý để thảo luận | Tình trạng |
|---|---|---|---|
| PLC-OQ-06 | `Mission_Feedback`: ghi khi nào, giữ đến khi nào? Workbook ghi `1–3` = AGV1–3, trong khi hệ thống điều khiển AGV tự chọn xe trong một đội có thể thay đổi. PLC có cần biết xe nào không? | Chỉ báo trạng thái tiếp nhận/lỗi; nếu PLC cần mã xe thì dùng vùng dữ liệu riêng | OPEN |
| PLC-OQ-11 | `HeartBit` `M9200` (workbook ghi dùng chung cho mỗi PLC): "chu kỳ 1 giây" là đổi trạng thái mỗi 1 giây hay mỗi 0,5 giây? Khi quá 10 giây PLC làm gì? Có heartbeat chiều PLC → phần mềm không? | Đổi trạng thái mỗi 1 giây | OPEN |
| PLC-OQ-15 | Phần mềm cần biết loại rack, rack có mặt tại trạm và mã rack. Nguồn nào cung cấp? | PLC bổ sung tín hiệu, hoặc chỉ định thiết bị khác | OPEN |

### 4.5 Kết nối, trạm, phiên bản, chuyển đổi

| Mã | Câu hỏi | Gợi ý để thảo luận | Tình trạng |
|---|---|---|---|
| PLC-OQ-13 | Mỗi IP là một CPU riêng? Cổng TCP, loại frame, network number, PC number, module I/O, station number, monitoring timer? Tên chính thức của từng PLC (cột PLC ghi `A1`, `BCD`, `A2`, `EF-G`; `A1`, `A2` trùng tên trạm) | – | OPEN |
| PLC-OQ-14 | Workbook ghi trạm X4, G1, G2; tài liệu trước ghi X6, G3, G7. Danh sách nào đúng? Các vị trí Y1, Y2, Z1, Z2, M5, M6 có tín hiệu PLC không? | Một danh sách chính thức | OPEN |
| PLC-OQ-18 | Mỗi giá trị INT là một word 16 bit có dấu? | – | OPEN |
| PLC-OQ-19 | Phía dự án dùng workbook 22/11 làm danh sách IO duy nhất. Chương trình PLC đang theo đúng bản này không? Nếu khác, khác ở đâu? | Ghi số hiệu và ngày phiên bản trong file | OPEN |
| PLC-OQ-20 | Thời điểm và cách chuyển chương trình PLC sang giao tiếp mới; thanh ghi cũ còn hiệu lực không? Lưu ý: một số địa chỉ trong mẫu giao tiếp cũ trùng thanh ghi mới (ví dụ `D1500`, `D1600`, `D1610`) | Chuyển theo từng PLC, khóa chức năng ghi trong lúc chuyển | OPEN |
