# AGV Phase 2 – Complete Handoff

> **IO MAP HIỆN HÀNH (2026-11-26, CR-045).** Dự án chỉ dùng một IO Map: [AGV_PLC Interface_List_22_11_2026.xlsx](../02_Interface_Specification/PLC_IO_Map/AGV_PLC%20Interface_List_22_11_2026.xlsx) (SHA-256 `405586c9…34da3`). Các workbook cũ (IO Map v2, 10/11, 20/11) **đã ngừng sử dụng và đã gỡ khỏi repository**; nội dung dựa trên chúng trong tài liệu này chỉ còn giá trị lịch sử. IO Map 22/11 **chưa** được kỹ sư PLC xác nhận là hợp đồng IO. [Trạng thái từng tín hiệu](../02_Interface_Specification/PLC_IO_Map/README.md).


---

# 00 – Project Context

## 1. Mục tiêu nghiệp vụ
Phần mềm điều phối AGV/AMR cho quy trình vận chuyển Rack giữa các trạm sản xuất, QC và các vị trí đệm (Buffer). Hệ thống phần mềm phải:
- Nhận yêu cầu vận chuyển từ PLC tại các trạm có PLC.
- Quản lý các yêu cầu và điều phối nhiệm vụ vận chuyển.
- Theo dõi Rack: Rack ID, vị trí hiện tại, trạng thái, hàng/rỗng, nguồn gốc (Origin) và vị trí đích.
- Quản lý các Buffer và QC thủ công không có PLC.
- Phối hợp với hệ thống điều khiển đội xe/AMR (RCS) và cập nhật trạng thái nghiệp vụ.
- Chỉ đóng nhiệm vụ khi đã có đủ xác nhận cho thao tác lấy/đặt Rack ở các điểm liên quan.

## 2. Hai loại trạm – bắt buộc phân biệt

### 2.1 PLC-managed stations
Các trạm có PLC và dùng IO handshake theo workbook `AGV_PLC_Interface_List_20_11_2026.xlsx`.

Danh sách nhóm trạm xuất hiện trong IO Matrix:
- A1, A2, A3, A4
- B1, B2
- D1, D2
- X1, X2, X3, X4
- E1, E2
- G1, G2

Tổng cộng 16 station endpoints theo danh sách IO Matrix. Workbook tổ chức theo các nhóm PLC/IP và lặp lại cấu trúc tín hiệu cho từng station.

### 2.2 WCS-managed manual stations
Các điểm không có PLC IO, phần mềm WCS tự quản lý trạng thái vị trí và tồn kho Rack:
- Buffer P1, P2, P3, P4
- QC thủ công M5, M6

Các điểm này không được gán PLC handshake chỉ vì chúng xuất hiện trong sơ đồ quy trình. WCS phải lưu được:
- Rack nào đang nằm ở Buffer/QC nào.
- Rack đang chứa hàng hay rỗng.
- Origin Station của Rack/luồng sản xuất.
- Trạng thái vị trí: trống, có Rack, đang được reserve, đang có nhiệm vụ đến/đi.
- Lịch sử nhập/xuất và nguồn xác nhận thay đổi trạng thái.

Nếu không có cảm biến hoặc thiết bị nhận dạng được mô tả trong tài liệu, cần thiết kế bước xác nhận của operator/PDA hoặc xác nhận khác sau khi được người dùng chốt. Không được mặc định AMR báo hoàn thành là đủ để chứng minh Rack đã được đặt đúng vị trí vật lý.

## 3. Các thuật ngữ nghiệp vụ
- **Rack**: giá/kệ được AMR vận chuyển.
- **Full Rack**: Rack có hàng.
- **Empty Rack**: Rack rỗng.
- **Origin Station**: trạm/luồng nguồn gốc của Rack; được dùng trong quy tắc lựa chọn Rack đã xác nhận.
- **Buffer**: vị trí đệm P1–P4, do WCS quản lý.
- **Manual QC**: vị trí QC thủ công M5/M6, do WCS quản lý.
- **Pick**: AMR lấy Rack khỏi station/source.
- **Drop**: AMR đặt Rack vào station/destination.
- **PLC handshake**: trao đổi tín hiệu giữa PLC station và AGV Interface software tại một thao tác Pick hoặc Drop.
- **Mission/Transport Job**: nhiệm vụ vận chuyển nghiệp vụ có thể gồm một hoặc nhiều thao tác Pick/Drop. Cần phân biệt với một lần handshake đơn lẻ tại station.

## 4. Phân vùng luồng theo PPTX
Trang 2–3 của PPTX thể hiện các nhóm trạm/Buffer:
- F-Bal 10R80: CNC A1, A2; QC B1, B2.
- F-Bal 10R60: CNC A3, A4; QC D1, D2.
- F-Pis 10R80: CNC X1, X2; bảng tổng quan ghi QC Y1, Y2.
- F-Pis 10R60: CNC X3, X4; bảng tổng quan ghi QC Z1, Z2.
- Buffer hàng cho A1/A2: P1, P2.
- Buffer hàng cho X1/X2: P3, P4.
- QC thủ công cho A1/A2: M5.
- QC thủ công cho X1/X2: M6.

**Cần xác minh:** sơ đồ layout có thể hiển thị thêm các nhãn station như E1/E2, G1/G2 và các nhãn khác; bảng tổng quan và nhãn sơ đồ không hoàn toàn tương đương. Không tự suy luận Y/Z là D/E/G hoặc tự đổi tên station. Workbook IO Matrix xác nhận các endpoint PLC A/B/D/X/E/G; PPTX trang tổng quan có các tên QC Y/Z. Cần người dùng xác nhận mapping chính thức giữa các nhóm.

## 5. Nguyên tắc thiết kế
1. Tách lớp quản lý yêu cầu, điều phối nhiệm vụ, trạng thái Rack/vị trí, adapter PLC và adapter RCS.
2. Không để logic riêng của từng PLC station bị hard-code rải rác nếu các station dùng chung cấu trúc handshake.
3. Tất cả chuyển trạng thái Rack và station phải có nguồn xác nhận và lịch sử/audit.
4. Có khả năng phục hồi khi timeout, mất kết nối PLC/RCS, AMR lỗi hoặc thao tác Pick/Drop thất bại; chi tiết cần được chốt trong đặc tả.
5. Không đóng Mission chỉ dựa trên tín hiệu `PLC_MissionCompleted` tại điểm nguồn nếu điểm đích chưa hoàn tất.


---

# 01 – Process Flows from ETEK Process AGV Phase 2 PPTX

Nguồn: `ETEK Process AGV Phase 2 12.08.26.pptx`.

## 1. Overview
PPTX mô tả nhóm CNC, QC, Buffer và QC thủ công. Các luồng chi tiết ở trang 4–9 tập trung vào A1/A2, B1/B2 và M5. Không có mô tả chi tiết tương đương cho toàn bộ nhóm X, P3/P4, M6, E/G.

## 2. Flow: A1/A2 có Full Rack (PPTX trang 4)
Theo sơ đồ:
1. PLC tại A1 hoặc A2 phát tín hiệu báo có Full Rack.
2. AGV/AMR đến lấy Full Rack tại A1/A2.
3. Xét điểm nhận theo thứ tự thể hiện trong sơ đồ:
   - B1/B2 nếu có vị trí.
   - P1/P2 nếu có vị trí.
   - M5 nếu có vị trí.
4. Rack được chuyển đến vị trí có thể nhận theo logic sơ đồ.

Lưu ý: PPTX thể hiện nhánh lựa chọn đích nhưng chưa đặc tả đầy đủ thuật toán reserve, xử lý đồng thời, xác nhận vật lý và tình huống tất cả đích đều đầy.

## 3. Flow: A1/A2 cần Empty Rack (PPTX trang 5)
1. PLC tại A1/A2 phát yêu cầu cần Empty Rack.
2. AGV/AMR tìm Empty Rack từ B1/B2 nếu có.
3. Nếu B1/B2 không có Empty Rack phù hợp, sơ đồ xét P1/P2.
4. Rack được đưa đến A1/A2.

PPTX không định nghĩa đầy đủ cơ chế chọn giữa nhiều Rack cùng thỏa điều kiện hoặc trạng thái reserve chống cấp trùng.

## 4. Flow: B1/B2 có Empty Rack (PPTX trang 6)
1. PLC tại B1/B2 phát tín hiệu có Empty Rack.
2. AGV/AMR lấy Empty Rack tại B1/B2.
3. Đưa đến A1/A2 nếu có vị trí nhận.
4. Nếu A1/A2 không có vị trí, sơ đồ xét P1/P2.

## 5. Flow: B1/B2 cần Full Rack (PPTX trang 7)
1. PLC tại B1/B2 phát yêu cầu cần Full Rack.
2. Hệ thống kiểm tra Full Rack tại A1/A2.
3. Nếu không có/không phù hợp, sơ đồ xét Full Rack tại P1/P2.
4. Rack được lấy và chuyển đến B1/B2.

## 6. Flow: Worker trả Rack tại M5 (PPTX trang 8)
1. Công nhân lấy hàng khỏi Rack tại M5.
2. Công nhân dùng PDA gọi AMR đến lấy Rack rỗng.
3. AMR lấy Rack tại M5 và di chuyển đến vị trí chờ trung gian.
4. Hệ thống kiểm tra vị trí trống tại A1/A2.
5. Nếu không có vị trí phù hợp, xét P1/P2.
6. AMR đặt Rack tại vị trí nhận phù hợp.

M5 là điểm thủ công, không có PLC IO theo phân loại đã xác nhận. Cần định nghĩa chính xác cách WCS nhận xác nhận worker đã yêu cầu và Rack thực sự đã được lấy/đặt.

## 7. Flow: Worker yêu cầu Full Rack tại M5 (PPTX trang 9)
1. Công nhân dùng PDA yêu cầu Full Rack đến M5.
2. AMR lấy Full Rack từ P1/P2.
3. AMR đưa Rack đến M5.
4. Nếu P1/P2 không có Full Rack, sơ đồ thể hiện thông báo lỗi.

M5 là điểm thủ công. Cần bổ sung kiểm tra Rack theo Origin, reserve Rack và xác nhận Drop tại M5.

## 8. Những gì PPTX chưa mô tả
- Luồng chi tiết tương đương cho X1–X4, P3/P4, M6.
- Luồng cho E1/E2, G1/G2 và mapping QC Y/Z.
- Quy tắc lựa chọn Rack khi nhiều Rack phù hợp (ngoại trừ quyết định Origin đã được người dùng xác nhận sau đó).
- Quy tắc xử lý đồng thời, reserve station/Rack và deadlock.
- Xác nhận Rack ID và trạng thái hàng/rỗng ở manual station.
- Chi tiết queue, timeout, retry, hủy nhiệm vụ và recovery.
- Điều kiện hoàn tất toàn bộ transport job xuyên suốt source → destination.

Các nội dung này phải được xem là khoảng trống đặc tả, không phải là hành vi đã có trong PPTX.


---

# 02 – PLC Interface

Nguồn: workbook `AGV_PLC_Interface_List_20_11_2026.xlsx`, sheet `IO Matrix` và `Trang_tính1`.

## 1. Phạm vi workbook
Workbook định nghĩa các tín hiệu PLC ↔ AGV Interface software tại station. Nó không phải đặc tả đầy đủ cho WCS mission lifecycle hoặc RCS/AMR fleet API.

Các endpoint trong IO Matrix:
`A1, A2, A3, A4, B1, B2, D1, D2, X1, X2, X3, X4, E1, E2, G1, G2`.

## 2. Các tín hiệu chính

| Signal | Chiều | Giá trị / ý nghĩa theo workbook |
|---|---|---|
| `PLC_MissionRequest` | PLC → Software | `0`: Không nhiệm vụ; `1`: Pick; `2`: Drop; `10`: lỗi. PLC gửi yêu cầu. |
| `PLC_ReaddyTranfer` | PLC → Software | `1`: Cho phép Pick; `2`: Cho phép Drop; `10`: Lỗi. Ghi chú: xác nhận đã mở cửa Rack, mở kẹp. |
| `PLC_MissionCompleted` | PLC → Software | `1`: Thất bại; `2`: Thành công. Ghi chú: xác nhận giao/nhận thành công tại station. |
| `AGV_RequestTranfer` | Software → PLC | `1`: yêu cầu mở cửa/kẹp để Pick; `4`: yêu cầu mở cửa/kẹp để Drop. Ghi chú: AGV ở vị trí trước cửa. |
| `AGV_TranferCompleted` | Software → PLC | `3`: Hoàn thành Pick; `4`: Hoàn thành Drop. Ghi chú: AGV đã lấy Rack và ra ngoài. **Cột địa chỉ đang để trống trong workbook.** |
| `Mission_Feedback` | Software → PLC | `0`: Chưa có AGV nhận; `1`: AGV1; `2`: AGV2; `3`: AGV3; `10`: Lỗi. Trạng thái nhiệm vụ AGV. |
| `HeartBit` | Software → PLC | `M9200`, BOOL, on/off chu kỳ 1 giây; timeout 10 giây. |
| `Spair` | – | Các bit dự phòng M9201–M9205 được liệt kê trong một số nhóm PLC. |

Chú ý giữ nguyên spelling của tên signal như workbook khi mapping PLC: `PLC_ReaddyTranfer`, `AGV_RequestTranfer`, `AGV_TranferCompleted`.

## 3. Ví dụ địa chỉ theo station
Một số địa chỉ được thể hiện theo pattern:
- A1: `PLC_MissionRequest D1500`, `PLC_ReaddyTranfer D1501`, `PLC_MissionCompleted D1502`; software side `AGV_RequestTranfer D1600`, `Mission_Feedback D1601`.
- A2: PLC side D1510/D1511/D1512; software side D1610/D1611.
- A3: PLC side D1520/D1521/D1522; software side D1620/D1621.
- A4: PLC side D1530/D1531/D1532; software side D1630/D1631.
- Các nhóm station khác lặp lại cấu trúc địa chỉ theo bảng IO Matrix và PLC group.

Không tự động suy rộng địa chỉ cho mọi PLC nếu chưa đối chiếu đúng dòng/nhóm PLC trong workbook. Đặc biệt `AGV_TranferCompleted` không có địa chỉ trong sheet.

## 4. Trình tự handshake theo sheet Trang_tính1
Sheet mô tả trình tự khái quát:
1. PLC gửi yêu cầu lấy/cấp Rack qua `PLC_MissionRequest`.
2. Software phản hồi tình trạng nhận nhiệm vụ qua `Mission_Feedback`; PLC quan sát AGV đã nhận hay gặp lỗi.
3. `AGV_RequestTranfer`: AGV đã đến vị trí trước cửa trạm.
4. PLC xác nhận đã mở cửa/mở kẹp qua `PLC_ReaddyTranfer`.
5. `AGV_TranferCompleted`: AGV đã lấy Rack và ra vị trí ngoài.
6. PLC xác nhận AGV đã hoàn thành thao tác qua `PLC_MissionCompleted`. Ghi chú trong workbook nói giá trị này thay đổi theo `Mission_Feedback` và `AGV_TranferCompleted`.

## 5. Diễn giải tích hợp – cần phân biệt nguồn và thiết kế
Ví dụ vận chuyển A1 → B1 có thể được mô hình hóa thành:
- Tại A1: PLC yêu cầu Pick; software nhận/điều phối; AMR đến trước cửa; software yêu cầu PLC mở cửa/kẹp; PLC cho phép; AMR lấy Rack và rời station; handshake Pick tại A1 hoàn tất.
- WCS cập nhật Rack đang trên đường.
- Tại B1: AMR đến trước cửa; software yêu cầu PLC mở cửa/kẹp cho Drop; PLC cho phép; AMR đặt Rack và rời station; handshake Drop tại B1 hoàn tất.
- WCS chỉ đóng transport job khi xác nhận thao tác Drop tại B1 và cập nhật tồn kho/vị trí đích thành công.

Đây là **mô hình tích hợp đề xuất**, không phải một trình tự end-to-end được workbook định nghĩa trọn vẹn. `PLC_MissionCompleted` tại A1 chỉ xác nhận thao tác tại A1; không được coi là hoàn tất toàn bộ hành trình A1 → B1.

## 6. Các điểm workbook chưa định nghĩa
- Mission ID/correlation ID.
- Edge-trigger hay level-trigger; latch/hold semantics.
- Quy tắc reset/clear từng signal và thứ tự reset.
- Timeout từng giai đoạn handshake (ngoài HeartBit timeout 10 giây).
- Retry, abort, recovery và xử lý lỗi PLC.
- Xác nhận RCS đã nhận mission, AMR đến, AMR lỗi hoặc hủy mission.
- Xác nhận Rack ID, trạng thái full/empty và physical occupancy.
- Địa chỉ của `AGV_TranferCompleted`.
- Quy tắc PLC signal giữ giá trị bao lâu, cách chống đọc lặp cùng một request.
- Quy tắc xử lý PLC gửi request mới khi request trước chưa kết thúc.

Các mục trên phải được xác minh với PLC/nhà cung cấp hoặc chốt thành interface specification bổ sung.


---

# 03 – Confirmed Business Decisions

Các mục dưới đây là quyết định người dùng đã xác nhận trong trao đổi trước. Không được thay đổi ngầm khi triển khai.

## 1. Request priority
**FIFO** – yêu cầu được xếp hàng theo thứ tự đến.

**Chưa chốt:** Khi request đầu queue bị block (ví dụ chưa có đích nhận) nhưng request sau không xung đột tài nguyên, hệ thống có được xử lý request sau hay phải strict FIFO tuyệt đối? Không được tự chọn một trong hai cách. Cần hỏi người dùng.

## 2. Buffer Rack selection
**Origin** – khi chọn Rack từ Buffer, ưu tiên/chọn Rack có `Origin Station` phù hợp với station/luồng đang yêu cầu.

Cần triển khai rõ quy tắc match Origin và xử lý khi không có Rack cùng Origin; phần fallback chưa được xác nhận.

## 3. No receiving destination available
**Queue** – nếu chưa có vị trí nhận khả dụng, giữ request trong queue và chờ đến khi có vị trí.

Không tự đổi thành báo lỗi hoặc hủy request. Các điều kiện timeout/expiry và thông báo operator chưa được xác nhận.

## 4. Station ownership
- PLC-managed: các station có IO trong workbook.
- WCS-managed manual: Buffer P1–P4 và manual QC M5–M6.
- WCS quản lý rack identity, occupancy và inventory tại các điểm manual.

## 5. Rack tracking
WCS cần nhớ Rack nào đang ở Buffer nào, Rack đang chứa hàng hay rỗng và Origin của Rack. Đây là yêu cầu nghiệp vụ được người dùng nhấn mạnh.

## 6. Những nội dung không phải quyết định đã chốt
- Strict FIFO hay controlled FIFO.
- Origin fallback nếu không tìm được Rack cùng Origin.
- Có cho phép chuyển Rack qua Buffer khác Origin hay không.
- Timeout khi request nằm trong queue.
- Có cho phép ưu tiên emergency/manual override hay không.
- Cách xác nhận vật lý tại P1–P4/M5/M6.


---

# 04 – Open Questions and Specification Gaps

Tài liệu này là danh sách điểm chưa đủ căn cứ để Claude không tự bịa logic.

## A. Station mapping
1. PPTX overview ghi F-Pis 10R80 QC là Y1/Y2 và F-Pis 10R60 QC là Z1/Z2; workbook IO Matrix lại có D1/D2, E1/E2, G1/G2. Cần xác nhận mapping chính thức.
2. Vai trò chính xác của E1/E2 và G1/G2 trong process layout là gì?
3. Các luồng X1–X4, P3/P4 và M6 có giống logic A1/A2, P1/P2, M5 hay có khác biệt?

## B. Rack data model
4. Rack ID được lấy từ barcode, RFID, PLC, RCS hay nhập liệu?
5. Rack có các thuộc tính bắt buộc nào: ID, Full/Empty, Product, Origin Station, current location, status, quality status?
6. Ai/cái gì xác nhận Rack đã được lấy khỏi hoặc đặt vào manual Buffer/QC?
7. Có cần quản lý loại Rack/compatibility theo từng station không?

## C. Buffer and destination rules
8. Khi chọn Buffer P1–P4, mapping Origin → Buffer cụ thể là cố định hay cấu hình?
9. Nếu không có Rack đúng Origin, hệ thống chờ hay được fallback sang Origin khác?
10. Nếu nhiều vị trí đích đều trống, thứ tự chọn đích là gì?
11. Nếu tất cả đích đầy, FIFO Queue đã chốt; cần làm rõ điều kiện đánh thức request khi có chỗ trống.
12. Có cần reserve destination trước khi dispatch AMR không? Khuyến nghị kỹ thuật là có, nhưng cần chốt nghiệp vụ và transaction behavior.

## D. Queue and concurrency
13. FIFO là strict FIFO hay cho phép bypass khi head request bị block?
14. Một PLC station có thể phát request mới khi request trước đang xử lý không?
15. Có thể có đồng thời bao nhiêu nhiệm vụ Pick/Drop tại một station?
16. Cơ chế chống duplicate PLC request là gì nếu PLC giữ nguyên signal?
17. Có mức ưu tiên khẩn cấp hoặc manual dispatch không?

## E. PLC handshake
18. Địa chỉ `AGV_TranferCompleted` đang trống trong workbook: cần PLC xác nhận địa chỉ.
19. Reset/clear signal do PLC hay software thực hiện? Reset value và thứ tự?
20. Các signal là pulse hay maintained state? Cạnh lên/cạnh xuống?
21. Timeout cho từng bước; phản ứng khi `PLC_ReaddyTranfer=10`, `PLC_MissionCompleted=1`, hoặc không phản hồi?
22. Ý nghĩa chính xác của `PLC_MissionCompleted` theo từng thao tác Pick/Drop và điều kiện PLC được phép set?
23. HeartBit M9200 áp dụng cho từng PLC hay từng nhóm PLC? Workbook thể hiện lặp lại theo một số nhóm.
24. Có cần handshake sequence number/mission ID để correlate?

## F. RCS / AMR
25. API/transport protocol với RCS, payload tạo mission, map ID, station/node ID, mission status mapping là gì?
26. Trạng thái nào từ RCS chứng minh AMR đã đến đúng node và hoàn tất Pick/Drop?
27. Cơ chế hủy mission, pause, resume, reroute, manual recovery?
28. Một transport job có thể có nhiều segment hay nhiều AMR không?

## G. Manual PDA / operator
29. PDA có những màn hình/thao tác nào: request full rack, return empty rack, confirm rack loaded/unloaded, scan Rack ID?
30. Operator có cần xác nhận trước khi gọi AMR hay sau khi AMR đến?
31. Khi operator xác nhận nhầm Rack hoặc nhầm Buffer, quy trình sửa sai/audit?

## H. Operational / software
32. User roles, permissions, audit log và alarm/notification requirements?
33. Dashboard cần hiển thị queue, missions, AMR, rack inventory, buffer occupancy và station status ở mức nào?
34. Yêu cầu về database, backend/frontend, deployment, logging, test strategy cần được xác nhận từ codebase hiện có; không tự suy ra chỉ từ hai file nguồn.

## Hướng dẫn cho Claude
- Trước tiên đọc codebase hiện tại và xác định kiến trúc/stack thực tế.
- Đối chiếu code với các tài liệu này.
- Ghi nhận các câu hỏi còn mở thành `OPEN_QUESTIONS.md` hoặc issue list.
- Không tự chốt các câu hỏi trên bằng phỏng đoán.
- Có thể đề xuất default kỹ thuật, nhưng phải đánh dấu `PROPOSAL – NEED USER CONFIRMATION`.


---

# 05 – Prompt to Start the New Chat / Claude Handoff

Bạn là technical lead/solution architect hỗ trợ phát triển phần mềm điều phối AGV/AMR cho dự án AGV Phase 2.

Tôi đã cung cấp bộ tài liệu handoff gồm:
- `00_PROJECT_CONTEXT.md`
- `01_PROCESS_FLOWS.md`
- `02_PLC_INTERFACE.md`
- `03_CONFIRMED_DECISIONS.md`
- `04_OPEN_QUESTIONS_AND_GAPS.md`
- Hai file nguồn PPTX và XLSX trong thư mục `sources/`.

## Nhiệm vụ đầu tiên
1. Đọc toàn bộ tài liệu handoff và mở trực tiếp hai file nguồn khi cần đối chiếu.
2. Đọc toàn bộ repository/codebase hiện tại trước khi đề xuất thay đổi.
3. Tóm tắt kiến trúc hiện tại: frontend, backend, database, PLC communication, RCS integration, mission orchestration, rack inventory, station model, tests và deployment.
4. Lập bảng mapping:
   - Requirement / process step
   - Source evidence (file, slide/sheet)
   - Existing implementation (file/class/function/test)
   - Gap
   - Proposed change
   - Risk / dependency
5. Phân biệt rõ:
   - Confirmed requirement
   - Source-documented behavior
   - Existing code behavior
   - Technical proposal
   - Open question requiring user confirmation
6. Chưa sửa code ngay. Trước hết đưa ra assessment và implementation plan theo các phase nhỏ, có acceptance criteria và test plan.

## Nguyên tắc nghiệp vụ không được vi phạm
- FIFO là quyết định đã xác nhận. Cách xử lý bypass khi request đầu queue bị block vẫn cần hỏi.
- Buffer rack selection theo Origin là quyết định đã xác nhận. Fallback khi không có rack cùng Origin vẫn cần hỏi.
- Nếu không có destination nhận được, giữ request trong Queue.
- PLC-managed station và WCS-managed manual station là hai loại khác nhau.
- P1–P4 và M5–M6 là WCS-managed manual stations, không có PLC IO theo phân loại nghiệp vụ đã thống nhất.
- WCS phải quản lý Rack ID, Full/Empty, Origin, current location và occupancy tại Buffer.
- PLC handshake hoàn tất tại một station không đồng nghĩa toàn bộ transport job hoàn tất.
- Không được tự tạo register address; `AGV_TranferCompleted` đang thiếu địa chỉ trong workbook.
- Không tự suy đoán mapping Y1/Y2/Z1/Z2 với D/E/G station.

## Cách làm việc với Claude
Khi được giao một task:
1. Trước khi code, nêu rõ files sẽ đọc/sửa và lý do.
2. Tìm implementation hiện tại, tránh tạo module trùng.
3. Đề xuất thay đổi nhỏ, tương thích kiến trúc.
4. Viết/điều chỉnh unit/integration tests.
5. Chạy test phù hợp và báo kết quả thực tế; không tuyên bố test pass nếu chưa chạy.
6. Cập nhật tài liệu kỹ thuật và decision/open-question log.
7. Báo rõ file thay đổi, hành vi mới, migration/config cần thiết, rủi ro và phần chưa làm.
8. Không tự ý thay đổi các quyết định nghiệp vụ đã chốt.

## Kết quả mong đợi ở lượt đầu tiên
Hãy trả về:
- Executive summary của codebase hiện tại.
- Architecture map (text/Mermaid nếu phù hợp).
- Requirement-to-code traceability matrix.
- Danh sách blocker và câu hỏi cần người dùng xác nhận, ưu tiên theo mức ảnh hưởng.
- Roadmap triển khai theo phase.
- Danh sách test cần có.

Chỉ bắt đầu sửa code sau khi người dùng duyệt implementation plan hoặc giao một task cụ thể.
