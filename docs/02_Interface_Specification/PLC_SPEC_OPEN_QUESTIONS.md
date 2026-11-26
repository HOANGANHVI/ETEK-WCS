# PLC Communication – Điểm cần xác nhận (TBD / Open Questions)

> **IO MAP HIỆN HÀNH (2026-11-26, CR-045).** Dự án chỉ dùng một IO Map: [AGV_PLC Interface_List_22_11_2026.xlsx](PLC_IO_Map/AGV_PLC%20Interface_List_22_11_2026.xlsx) (SHA-256 `405586c9…34da3`). Các workbook cũ (IO Map v2, 10/11, 20/11) **đã ngừng sử dụng và đã gỡ khỏi repository**; nội dung dựa trên chúng trong tài liệu này chỉ còn giá trị lịch sử. IO Map 22/11 **chưa** được kỹ sư PLC xác nhận là hợp đồng IO. [Trạng thái từng tín hiệu](PLC_IO_Map/README.md).

> **⚠ SUPERSEDED một phần – CR-029, 2026-11-21.** Các câu hỏi gắn với tín hiệu của giao diện 10/11 (M9101, M9120, M9122, M9123, `Rack_Full`…) được thay bằng danh sách mới [PLC_VENDOR_CLARIFICATION_QUESTIONS_20_11_2026.md](PLC_20_11_2026/PLC_VENDOR_CLARIFICATION_QUESTIONS_20_11_2026.md) (PQ-01…PQ-25). Các câu hỏi **không** phụ thuộc tín hiệu (Q-LEG-1, Q-GATE-*, Q-MLEG-*, Q-AGV-1, Q-BUF-1, Q-INV-2, Q-LOC-1, Q-PH2-1…) **vẫn mở**. Giữ nguyên để truy vết.

**Trạng thái:** `DRAFT – CR-010`. Danh sách này chỉ gồm những thông tin **thực sự chưa có căn cứ** trong `AGV_PLC_IO_Interface_List_10_11_2026.xlsx` hoặc trong yêu cầu ngày 2026-11-17. Những điều đã được xác nhận (mục 1) **không** được hỏi lại.
**Cách dùng:** mỗi mục có mã (`Q-…`) được trích dẫn trong [Đặc tả đề xuất](PLC_COMMUNICATION_SPECIFICATION_DRAFT.md). Cột "Quyết định bởi" là đề xuất người chịu trách nhiệm. Mọi giá trị timeout/retry **chưa có** và **không được tự đặt**.

## 1. Đã được xác nhận – không hỏi lại

- Tên và địa chỉ 22 tín hiệu (M9000–M9005, M9100–M9104, M9120–M9123, D1500, D1510, D1512, D1514, D1520, D1600, D1610) và kiểu dữ liệu.
- M9122 `AGV_Fault` = hoàn tất giao nhận thành công (không phải lỗi); giữ tên. M9123 chưa có tên; không đặt tên.
- M9104 `Transfer_Permit` = chỉ báo lỗi task AGV ảnh hưởng trạm; WCS tự reset khi điều kiện lỗi hết trong lúc AMR đang làm task.
- M9123: WCS bật khi AGV vào trạm, lấy rack xong và rời trạm; PLC reset để xác nhận đã nhận.
- `Request_ID`: STRING, độ dài 254; chỉ cần duy nhất và không xử lý trùng; chưa cần quy định format sinh.
- Mapping `Rack_Full`/`Rack_Empty` = 1..4 → A1..A4; không tự mở rộng cho vị trí khác.
- Thứ tự và nghĩa 9 bước; WCS phải đọc D1500/D1510/D1512/D1514/D1520 trước khi điều phối AGV; không dispatch khi chưa xác định vị trí.
- Mỗi PLC tối đa 1 transaction, PLC khác nhau song song; queue FIFO riêng từng PLC, không ưu tiên Pickup/Delivery; Request_ID chống trùng.
- Mất kết nối: giữ transaction, không hủy, không tự reset, kết nối lại đọc IO thực và đối chiếu DB, tiếp tục đúng bước; transaction lưu DB, khôi phục khi WCS restart; không đủ dữ liệu ⇒ chờ reconciliation, không phát lệnh vật lý mới.
- Mapping IO theo PLC, bản sao độc lập từ Default Template, Admin chỉnh được; số PLC động, bảo toàn thông số kết nối.

## 2. Danh sách cần xác nhận

### 2.1. Ưu tiên cao – chặn việc viết đặc tả cuối

| Mã | Câu hỏi | Vì sao chưa có căn cứ | Ảnh hưởng | Phương án / đề xuất | Quyết định bởi |
|---|---|---|---|---|---|
| **Q-ORD-1** | Thứ tự giữa **xác thực dữ liệu vị trí/loại rack** và **ghi M9101**: ack khi đã *ghi nhận* (A) hay khi đã *hợp lệ* (B)? | Sheet đặt M9101 ở bước 2; IO Matrix định nghĩa M9101 là "đã nhận và ghi nhận yêu cầu", sheet gọi là "Phần mềm nhận nhiệm vụ". Cả hai đọc được theo A hoặc B. Việc *đọc* thanh ghi trước M9101 là chắc chắn; việc *xác thực* thì không | Quy định bit M9101 có thể phải reset (A) hay không (B); cách PLC biết request bị từ chối | Xem Đặc tả §8.4. Bản nháp dùng B để không phải thu hồi ack; cần kỹ sư PLC xác nhận PLC có chấp nhận không thấy ack kèm `Error_Code` hay không | Kỹ sư PLC + chủ dự án |
| **Q-LEG-1** | 9 bước áp dụng thế nào cho **chặng ở Trạm khác Trạm phát request** (BP-02 lấy rack trống tại B1/B2 cho A1/A2; BP-01 thả tại B1/B2; điểm thả cần PLC cho phép)? | Sheet chỉ mô tả Trạm tự phát M9001/M9002. Các chặng do WCS khởi tạo ở Trạm khác không có trình tự tín hiệu | Không thể hoàn chỉnh đặc tả luồng BP-01/02/03 ở Trạm thứ hai; liên quan `WaitingForPlc` hiện có | Hỏi: Trạm đó cũng dùng 9 bước (WCS khởi tạo thay vì PLC) hay PLC của Trạm đó tự phát request? | Chủ dự án + kỹ sư PLC |
| **Q-ORD-2** | **Khi nào** WCS bật M9120 (yêu cầu mở cửa rack): ngay sau ack, khi AGV bắt đầu tới, hay khi AGV sắp tới? Có phụ thuộc `Safety_Ready` (M9000) không? | Sheet đặt M9120 ở bước 3, trước khi AGV tới vị trí (M9121, bước 5), nhưng không nêu điều kiện hay thời điểm; thao tác này làm cửa mở thật | Quyết định thời gian cửa rack mở khi chưa có AGV (an toàn/vận hành) và điểm chặn bước 3 trong state machine | Bản nháp chỉ ghi "sau khi task AGV đã được tạo/liên kết"; không tự chọn thêm điều kiện | Kỹ sư PLC + chủ dự án |
| **Q-GATE-1** | "Cổng (một bộ I/O)" là gì: **G-A** một PLC/một bộ I/O trong Excel, **G-B** một Trạm có bộ I/O riêng, hay **G-C** một Trạm nhưng các Trạm cùng PLC dùng chung bộ tín hiệu/thanh ghi request? Hai Trạm cùng PLC có thể đang ở giữa 9 bước cùng lúc không? | Excel có **một** bộ tín hiệu mỗi PLC (Trạm suy từ D1510/D1512); [YC] 2026-11-17 giới hạn 1 transaction/PLC; yêu cầu 2026-11-18 muốn nhiều cổng cùng PLC độc lập. Ba cách hiểu loại trừ nhau (Mô hình §4.1) | Quyết định kịch bản AT-05 có áp dụng không, đơn vị tuần tự hóa, và có phải quay lại 16 bộ I/O (CR-002) | Không chọn; mọi mô tả dùng "Gate" | Kỹ sư PLC + chủ dự án |
| **Q-MLEG-1** | Chặng nhiều bước (A→buffer→B) thực hiện bằng **một task RCS có waypoint + continueTask**, hay **nhiều task RCS (mỗi Leg một task)**? | `task-state-machine.md` §3.6: nếu RCS không hỗ trợ chờ tại waypoint, "phải thiết kế cơ chế nhiều chặng task và được duyệt trước khi triển khai"; chưa có xác nhận từ RCS | Quyết định có cần đổi `TaskStateMachine` (được bảo vệ) và cách hiển thị Leg | Đặc tả chỉ mô tả Leg ở lớp điều phối bổ sung; không sửa `TaskStateMachine` | Chủ dự án + nhà cung cấp RCS |
| **Q-AGV-1** | AGV có được **giải phóng giữa hai chặng** khi rack nằm ở buffer (P-2), hay vẫn gắn với task cho đến cuối (P-1, đúng baseline §3.3/3.6)? | Baseline: `WAITING_FOR_DESTINATION` vẫn chiếm AGV; yêu cầu mới cho cổng A nhận request mới khi task chờ ở buffer | Cách áp dụng `PER_AGV` (task hay chặng); kịch bản AT-02/03/04 | Mặc định P-1 (bảo thủ) | Chủ dự án |
| **Q-GATE-2** | Station Transaction "đang hoạt động" tính từ lúc nào: khi request mới `Recorded/Queued`, khi đã gán AGV, hay khi AMR đã tới cổng? | Yêu cầu chỉ nói "một Transaction đang hoạt động/cổng, kết thúc khi AMR ra khỏi cổng" | Liên quan hàng đợi theo PER_AGV và FIFO theo cổng | Đề xuất: bắt đầu từ `Accepted` nhưng không giữ cổng khi chỉ xếp hàng | Chủ dự án |
| **Q-REL-1** | Cổng giải phóng ở **bước 8** (AMR rời, M9123) hay **bước 9** (PLC xác nhận M9005)? | Định nghĩa "AMR đã đi ra" khớp bước 8, nhưng bước 9 đến sau | Thời điểm PLC được phép phát request kế | Chưa chọn | Kỹ sư PLC + chủ dự án |
| **Q-BUF-1** | Buffer nào được dùng cho từng BP, và vị trí buffer (F1–F5, KV3/KV4) được **xác nhận thả/lấy** bằng cách nào (không có PLC 9 bước)? | Baseline nêu vai trò buffer, nhưng không gắn với từng BP; xác nhận lấy/thả ở buffer chưa mô tả | Tính hợp lệ của chặng trung gian; sự kiện `DROP_CONFIRMED` ở buffer | Chỉ dùng danh sách cấu hình + BP đã duyệt | Chủ dự án |
| **Q-INV-2** | Chuỗi `PICK_CONFIRMED`→`DROP_CONFIRMED`(buffer)→`PICK_CONFIRMED`→`DROP_CONFIRMED` trong **một** Transport Task có được `InventoryService` hiện có chấp nhận (`RackStatus`, `CurrentTaskId` giữa hai chặng)? | Chưa kiểm tra vì đợt này chỉ viết tài liệu | Có cần sửa `InventoryService` (được bảo vệ) | Kiểm tra khi thiết kế triển khai | Đội kỹ thuật |
| **Q-MLEG-2** | Việc WCS **tự động** lấy rack từ buffer (F1–F5) đưa tới cổng máy khi chỗ trống xuất hiện có phải **quy trình nghiệp vụ mới** cần được duyệt không? Với rack đầy vào B1/B2 nó có đồng nhất với BP-04 (`REVIEW`) không? | `Process in detail` slide 5/7: thả vào buffer F **kết thúc task**; chặng buffer→B1/B2 (rack đầy) chỉ ở slide 8 (BP-04, REVIEW); BP-05 do người vận hành gọi | Nếu đồng nhất BP-04: kịch bản AT-03 bị chặn; nếu quy trình mới: cần Change Request/quy trình riêng, không được suy từ yêu cầu hợp nhất | Giữ mô hình Leg như khả năng mở rộng, **không** kích hoạt luồng nào ngoài baseline | Chủ dự án |
| **Q-SLD-1** | Câu "Tại một thời điểm chỉ có 1 task được thực hiện" (`Process in detail` slide 4) có còn giới hạn nào ngoài `PER_AGV` (BR-01), ví dụ trên một cổng hoặc một khu vực? | BR-01 đã duyệt: PER_AGV, không giới hạn toàn hệ thống; thẩm quyền BR-01 cao hơn slide nguồn | Phạm vi song song Station Transaction/Task | Hiểu là đã được BR-01 thay thế ở cấp task; không bỏ/không thêm giới hạn nào | Chủ dự án |
| **Q-LOC-1** | Quan hệ tên vị trí giữa baseline Phase 1 (F1–F5, KV1–KV4) và tệp Phase 2 (P1–P4 buffer hàng, M5/M6 QC thủ công/buffer manual)? | `business-rules.md` §1.5 chỉ có F/KV; Phase 2 dùng P/M5/M6; chưa có bảng tương ứng | Danh sách buffer cho Location Mapping/BP | Không tự ánh xạ | Chủ dự án |
| **Q-PH2-1** | Tệp `ETEK Process AGV Phase 2` có nằm trong phạm vi CR-010/Phase 1 không? (Nó mô tả nhánh "vị trí chờ trung gian", B1/B2 cần rack đầy từ P1/P2…, nhãn "Model A (Phase 1)") | `process-flows.md` §2.3: không suy ra quy trình Phase 2 khi chưa có tài liệu; nay có tài liệu nhưng chưa được duyệt là baseline | Có thể đưa nhánh "chờ trung gian" vào đặc tả hay không | Chỉ dùng làm bối cảnh, không thiết kế theo | Chủ dự án |
| ~~**Q-SRC-1**~~ | *Đã giải quyết 2026-11-18:* hai tệp .pptx nằm ở `7. FFC AMR/` (thư mục cha của dự án) và đã được đọc; tệp Phase 2 trên đĩa tên không có "(1)". | – | – | – | – |
| **Q-MAP-1** | M9003 (cửa mở), M9004 (kẹp), M9005 (thành công) tương ứng với tín hiệu nghiệp vụ nào của task WCS (`PlcPermissionGranted`, `PickConfirmed`, `DropConfirmed`)? | Tài liệu chỉ có hai hệ thống tín hiệu riêng; chưa có bảng ánh xạ | Quyết định ai xác nhận pick/drop (RCS/AGV hay PLC) và vì thế liên quan `InventoryService` (được bảo vệ) | Đặc tả §14 đưa gợi ý; **không** sửa `TaskStateMachine` | Chủ dự án |
| **Q-INV-1** | Nếu vị trí PLC báo (D1510/D1512) **khác** tồn kho WCS thì ai thắng? | WCS sở hữu tồn kho là quy tắc được bảo vệ; PLC báo vị trí là yêu cầu mới | Xung đột tiềm ẩn quy tắc được bảo vệ | Đề xuất: dữ liệu PLC là bằng chứng nhận diện; khác nhau ⇒ chờ reconciliation; không ghi đè | Chủ dự án |
| **Q-STR-1** | `Request_ID` "dài 254": **254 ký tự (≈127 word) hay 254 word**? Vùng thực tế dành cho chuỗi là bao nhiêu word? Có vùng `D1500…` chồng lên `D1510…D1610`? | Excel không ghi độ dài; 254 chỉ có trong yêu cầu; với bất kỳ nghĩa nào vùng chuỗi chạm `D1510`, `D1512`, `D1514`, `D1520`, `D1600`, `D1610` (trừ khi vùng chuỗi ≤ 10 word) | Validator địa chỉ sẽ báo giao nhau; đọc một chuỗi 254 sẽ đọc lẫn các thanh ghi dữ liệu | Hỏi kỹ sư PLC độ dài **đọc được** và encoding (ASCII?), thứ tự byte, kết thúc NUL. Không tự sửa địa chỉ | Kỹ sư PLC |
| **Q-IO-2** | Bản sao template mang địa chỉ mặc định của Excel có được coi là **đã duyệt** (pollable) hay vẫn cần kỹ sư PLC duyệt từng điểm? | CR-002: "không tự gán địa chỉ; `Actual_PLC_Address` để trống chờ kỹ sư". Yêu cầu mới: template có địa chỉ mặc định theo Excel | Quyết định có đọc/ghi PLC thật khi vừa thêm PLC hay không (an toàn) | Đề xuất: sao chép ở `Configured`, **không** `Approved` | Chủ dự án + kỹ sư PLC |
| **Q-PLC-1** | Hành vi PLC: (a) PLC chỉ trình bày **một** request (một bộ D1500…) khi 4 Trạm cùng cần xe thì phân xử thế nào? (b) PLC giữ **dữ liệu và bit request ổn định** cho tới khi nhận ack? (c) Khi request thứ hai xuất hiện trong lúc transaction đang chạy, PLC giữ bit/ghi đè thanh ghi? | Excel chỉ liệt kê tín hiệu; không mô tả logic PLC | Quyết định cơ chế snapshot, queue thực tế, và việc đối chiếu `Request_ID` lúc kích hoạt | Hỏi kỹ sư PLC | Kỹ sư PLC |

### 2.2. Thời gian, thử lại, mức/xung

| Mã | Câu hỏi | Ghi chú |
|---|---|---|
| **Q-TMO-1** | Timeout từng bước: **TMO-1** (sau ack, PLC phản ứng), **TMO-2** (chờ M9003 sau M9120), **TMO-3** (chờ M9004 sau M9121), **TMO-4** (chờ PLC sau M9122), **TMO-5** (PLC reset M9123), **TMO-6** (chờ M9005) và hành động khi hết hạn | **TBD**: không có giá trị nào được duyệt. Mặc định đề xuất: chỉ cảnh báo, không tự hủy, không tự lùi bit |
| **Q-RETRY-1** | Quy tắc retry (ghi lại bit, đọc lại, kích hoạt lại request) | **TBD**. Hiện có: đọc thử lại một lần trên kết nối mới; ghi mơ hồ không gửi lại |
| **Q-RST-1** | Chủ reset và thời điểm reset của M9101, M9102, M9103, M9120, M9121, M9122, D1600, D1610; và các bit PLC→WCS sau khi hoàn tất | Chỉ M9123 (PLC) và M9104 (WCS) đã rõ |
| **Q-LVL-1** | Các bit là **mức** (giữ đến khi reset) hay **xung**? Đặc biệt M9005 và M9003/M9004 khi WCS bị mất kết nối: có thể bỏ lỡ xung | Ảnh hưởng độ tin cậy của phát hiện bước 4, 6, 9 sau khi kết nối lại |
| **Q-HB-1** | `Heartbeat_SW`: chu kỳ, PLC chờ bao lâu thì coi WCS mất, PLC phản ứng thế nào, có phát khi đang khôi phục/đối chiếu hay không, và khác nhau theo PLC không | Chưa có; hiện **không có** vòng heartbeat nào trong mã |

### 2.3. Ý nghĩa tín hiệu và dữ liệu

| Mã | Câu hỏi | Ghi chú |
|---|---|---|
| **Q-DEST-1** | `Destination_Available` D1514: bảng giá trị; có cùng mã hóa với D1510/D1512 không? Giá trị "không có đích" là gì? | Excel ghi "làm rõ"; **không** tự mở rộng quy tắc `1..4=A1..A4` |
| **Q-TYPE-1** | `Rack_Type` D1520: bảng giá trị; ánh xạ sang loại rack của WCS (Full/Empty) hay loại sản phẩm? | Excel ghi "làm rõ" |
| **Q-POS-1** | Ý nghĩa giá trị `0` (hoặc ngoài 1–4) trong `Rack_Full`/`Rack_Empty`; mã vị trí của PLC-02/03/04 (B1, B2, D1, D2, X…, E…, G…) | Chỉ có A1–A4 |
| **Q-ANC-1** | Thời điểm bật/tắt `AGV_Assigned` M9102 và `AGV_Arrived` M9103 (nghĩa "đang xử lý task liên quan trạm") | Không thuộc 9 bước |
| **Q-ANC-2** | Mã số `AGV_ID` D1600 cho AGV1/AGV2 và khi không có AGV | Chưa có bảng |
| **Q-ERR-1** | Bảng `Error_Code` D1610 (mã giao tiếp và vận chuyển); điều kiện làm M9104 bật; thời điểm xóa `Error_Code`; có báo lỗi từ chối request qua kênh này không | Chưa có bảng mã |
| **Q-SAFE-1** | `Safety_Ready` M9000 có là điều kiện bắt buộc trước bước 3/5 (mở cửa/đến vị trí) không? | Excel chỉ mô tả nghĩa |
| **Q-SAFE-2** | Phân loại lại danh sách chặn ghi theo tên (hiện: `Safety_Ready`, `Transfer_Permit`, `AGV_Departure_Permit`, nhóm Safety/Interlock) theo nghĩa mới | M9104 và M9120 đã đổi nghĩa |
| **Q-ID-1** | `Request_ID` duy nhất theo PLC hay toàn hệ thống? PLC có tái sử dụng ID sau khi hoàn tất? Request trùng của một request đã `Rejected` xử lý thế nào? | Chỉ yêu cầu "duy nhất" |

### 2.4. Kiến trúc, dữ liệu, vận hành

| Mã | Câu hỏi | Ghi chú |
|---|---|---|
| **Q-SCOPE-1** | Xác nhận tất cả tín hiệu (kể cả `Heartbeat_SW`) là **PLC-scoped** (một bản cho mỗi PLC) | CLAUDE.md §7.3 yêu cầu phân loại tường minh |
| **Q-LIFE-1** | Vô hiệu hóa/xóa PLC: chỉ vô hiệu hóa (giữ truy vết) hay cho phép xóa khi chưa có lịch sử? Điều gì xảy ra với transaction đang chạy khi vô hiệu hóa? | Chưa có |
| **Q-TSK-1** | Khi PLC mất kết nối lâu, task ở `WaitingForPlc` có chịu `StateTimeoutsSeconds` (CR-008A) hay phải miễn? | Tương tác với cơ chế được bảo vệ; cần CR riêng nếu phải đổi |
| **Q-MIG-1** | Chuyển đổi/loại bỏ dữ liệu `station_io_points` hiện có (368 điểm; dev DB có 2 điểm kiểm thử); số phận API/UI theo Trạm; có giữ chế độ tương thích không? | Quyết định lúc thiết kế triển khai |
| **Q-DEV-1** | Các thiết bị `M9000…M9123` có tồn tại trong dải điểm thiết bị `M` đã cấu hình và không thuộc vùng rơ-le đặc biệt của họ PLC dùng? Cơ số địa chỉ `D`/`M` | Cần kỹ sư PLC xác nhận khi commissioning |
| **Q-RECON-1** | Khi PLC và database không nhất quán ngoài các trường hợp §11.1: ai có quyền quyết định, và các lựa chọn hợp lệ cuối cùng (tiếp tục bước N / đánh dấu hoàn tất / hủy) | Đề xuất ở Đặc tả §13 |
| **Q-PROTO-1** | Các thông số giao thức vẫn `NEEDS_CONFIRMATION`: TCP port, frame, cài đặt QJ71E71-100, mã hóa INT (có dấu?), STRING (ASCII, thứ tự byte, kết thúc NUL), cơ số X/Y/W | Đã mở từ CR-002/003; không đổi trong CR-010 |

## 3. Những điều **không** được đưa vào đặc tả (để tránh suy diễn)

- Không đặt tên cho M9123; không đổi tên M9122; không đổi nghĩa M9104 theo tên.
- Không có giá trị timeout/retry/chu kỳ heartbeat nào.
- Không mở rộng mã hóa vị trí `1..4 = A1..A4` sang vị trí hoặc PLC khác; không gán bảng giá trị cho `Destination_Available` và `Rack_Type`.
- Không định nghĩa format `Request_ID`.
- Không chọn vị trí thay thế khi dữ liệu PLC không hợp lệ.
- Không thêm hoặc bỏ bước trong 9 bước; không ép `AGV_Assigned`, `AGV_Arrived`, `Heartbeat_SW`, `Safety_Ready` vào một bước cụ thể.
- Không thay đổi `TaskStateMachine`, FlowOptions, `InventoryService`, Scheduler, RCS client, PER_AGV, BP-04 (`REVIEW`), A3/A4 và D1/D2 (`UNCHANGED`).
