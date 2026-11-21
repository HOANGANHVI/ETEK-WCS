# PLC Vendor Clarification Questions – giao diện 20/11/2026

| | |
|---|---|
| **Gửi tới** | Kỹ sư/bên PLC (và, với mục "nội bộ", chủ dự án) |
| **Nguồn** | `AGV_PLC Interface_List_20_11_2026.xlsx` (sheet `IO Matrix`, `Trang_tính1`) |
| **Trạng thái** | `OPEN` – chưa có câu trả lời nào. **WCS không tự trả lời thay bên PLC; không có giá trị mặc định nào được đặt cho các mục này.** |
| **Cách dùng** | Mã `PQ-nn` được trích dẫn trong các tài liệu `PLC_*_V2_20_11_2026.md`. Cột "Chặn" nêu việc không thể làm/triển khai khi chưa có trả lời. |

## 0. Cập nhật 2026-11-21 – phân loại nội dung (đọc trước)

Từ ngày 2026-11-21 chủ dự án đã chốt D-1…D-4 ([ADR-0011](../../decisions/ADR/ADR-0011-plc-interface-20-11-2026-architectural-decisions.md)). Tài liệu này tách rõ **ba loại** nội dung:

| Loại | Ý nghĩa | Nằm ở |
|---|---|---|
| **1. Câu hỏi chờ xác nhận** | Bên PLC phải trả lời; WCS không tự trả lời | Mục A, B, E (PQ-01…PQ-29) |
| **2. Quyết định kiến trúc đã chốt** | Chủ dự án đã quyết; không còn là câu hỏi mở của WCS (một số cần bên PLC *xác nhận khả thi*) | Mục C (D-1…D-4) |
| **3. Đề xuất thay đổi giao diện cần bên PLC phê duyệt** | WCS đề xuất; **không** phải tên tín hiệu/địa chỉ chính thức; chỉ có hiệu lực khi hai bên thống nhất bằng văn bản | Mục F (IF-P1…IF-P5) |

Thay đổi trạng thái do D-1…D-4: PQ-13, PQ-14, PQ-22 chuyển từ "chọn mô hình" sang **"xác nhận khả thi mô hình D-1"**; PQ-06 chuyển thành **thỏa thuận giao diện phản hồi mới** (D-2); PQ-10, PQ-17, PQ-18 gắn với yêu cầu D-4; thêm PQ-26…PQ-29. PQ-20 (tên trạm) **vẫn mở**.

## A. Danh sách bắt buộc (15 câu)

| Mã | Câu hỏi | Căn cứ trong tài liệu nguồn | Chặn |
|---|---|---|---|
| **PQ-01** | **Địa chỉ thanh ghi cụ thể của từng tín hiệu** cho **từng trạm** và các tín hiệu chung: thiết bị (M/D/…), số, chỉ số bit/word, kiểu INT (16 bit có dấu?), vùng mỗi trạm/PLC, không chồng lấn | Cột H "Địa chỉ thanh ghi" trống ở mọi dòng (H4:H33) | Toàn bộ đọc/ghi PLC thật; commissioning |
| **PQ-02** | `PLC_MissionRequest` được **giữ giá trị (mức)** đến khi nào, hay chỉ **phát xung**? Nếu xung: độ rộng và WCS phải bắt như thế nào | J4 "0: Không nhiệm vụ, 1: Pick, 2:Drop" | Cơ chế phát hiện yêu cầu, chống mất/trùng |
| **PQ-03** | **Điều kiện và thời điểm reset** `PLC_MissionRequest` về 0: sau `PLC_MissionCompleted`? Ý nghĩa chính xác của ghi chú "thay đổi theo PLC_MissionCompleted" | `Trang_tính1` K9 | Kết thúc giao dịch; yêu cầu kế tiếp |
| **PQ-04** | `Mission_Feedback` được **giữ đến khi nào** và **ai reset** (WCS hay PLC), vào thời điểm nào | J9 | Vòng đời phản hồi; khôi phục |
| **PQ-05** | **Ý nghĩa chính xác** các giá trị: `0`, `1`, `2`, `3`, `>10` (và 4–10 có dự trữ hay không); bảng mã lỗi `>10` cho cả `Mission_Feedback` và `PLC_MissionRequest`; vì sao ">10 lỗi" chỉ ghi ở dòng E1 (J4) mà không ở E2/G1/G2 | J4, J9, J10, J16, J22 | Phân tích giá trị; xử lý lỗi |
| **PQ-06** | `Mission_Feedback` dùng `AGV1/AGV2/AGV3` **có còn phù hợp** khi **RCS quản lý đội AMR động** (số xe có thể >3, mã robot do RCS đặt)? Là ID AGV thật hay chỉ cờ "đã có AGV nhận"? Nếu cần số: quy ước ánh xạ nào; xử lý khi xe >3 | J9; CR-028 | Ghi `Mission_Feedback`; ánh xạ RCS↔PLC |
| **PQ-07** | `PLC_ReaddyTranfer` được phát **khi nào** (sau `AGV_RequestTranfer`?), giữ mức hay xung, **ai reset** và khi nào PLC tự "Đóng" (0)? Có thể báo thất bại/timeout khi cơ cấu không mở được? | J5, K5, G18 | Bước 4; an toàn đóng kẹp |
| **PQ-08** | `AGV_RequestTranfer` và `AGV_TranferCompleted` là **trạng thái duy trì (mức)** hay **xung cạnh**? Vì sao `AGV_TranferCompleted` mang nhãn giá trị "0:Rời đi, 1:Đã đến" (giống `AGV_RequestTranfer`)? Giá trị 0/1 của nó nghĩa gì | J7, J8, K8 | Ghi tín hiệu; khôi phục sau khởi động lại |
| **PQ-09** | `PLC_MissionCompleted` được xác định theo **điều kiện nào** (cảm biến, kẹp, rack có mặt…)? Khi **0 = Thất bại** trùng trạng thái nghỉ: **làm sao phân biệt "chưa có kết quả" với "thất bại"**? Thời điểm PLC đặt giá trị; ý nghĩa "thay đổi theo Mission_Feedback và AGV_TranferCompleted" | J6, K6, K22 | Kết quả giao dịch; xử lý thất bại |
| **PQ-10** | Cách **phân biệt hai giao dịch liên tiếp** và **chống xử lý trùng** khi không còn `Request_ID`: PLC có sinh số thứ tự/ID giao dịch không (nếu có thì ở đâu)? Hai yêu cầu liên tiếp cùng loại tại cùng trạm xuất hiện thế nào? | Không có `Request_ID` (giao diện 10/11 có D1500, STRING 254) | Chống trùng; đối soát |
| **PQ-11** | **Timeout, retry và phục hồi khi mất kết nối** cho từng bước (chỉ `HeartBit` có timeout 10 s). Thời gian chờ tối đa mỗi bước; PLC có tự hủy/hoàn tác không | K28 | Giá trị timeout/retry; cảnh báo |
| **PQ-12** | Khi **PLC khởi động lại giữa giao dịch**: các tín hiệu PLC→SW giữ hay về 0? PLC mong đợi WCS làm gì (ghi lại tín hiệu SW→PLC, khởi tạo lại)? Trạng thái cơ cấu cửa/kẹp sau khởi động | – | Khôi phục an toàn |
| **PQ-13** | Có cho phép **nhiều Station trên cùng PLC phát yêu cầu gần như đồng thời** không? PLC xử lý song song hay tuần tự (một trạm hoạt động mỗi PLC)? | Mỗi trạm có bộ tín hiệu riêng (IO Matrix) | Quyết định C-1 (hàng đợi PLC) |
| **PQ-14** | **Quy tắc ưu tiên** khi nhiều Station cùng có yêu cầu (FIFO theo thời điểm? ưu tiên Pick/Drop? trạm?) | – | Hàng đợi/ưu tiên |
| **PQ-15** | **Cách xử lý khi `Mission_Feedback` báo lỗi** (`>10`): PLC làm gì (hủy yêu cầu, giữ cơ cấu, đóng cửa/kẹp)? Ai reset lỗi? WCS có phải hủy giao dịch/AGV task không? | J9 | Xử lý lỗi; an toàn |

## B. Câu hỏi bổ sung phát sinh từ rà soát (10 câu)

| Mã | Câu hỏi | Căn cứ | Chặn |
|---|---|---|---|
| **PQ-16** | **Thao tác Drop** (`PLC_MissionRequest`=2): có dùng cùng 6 bước? `AGV_TranferCompleted` ("AGV đã lấy Rack và ra ngoài") nghĩa gì khi **thả** rack (có phải "đã đặt Rack và ra ngoài")? `PLC_ReaddyTranfer` cho Drop có nghĩa giống Pick? | K8; sơ đồ chỉ mô tả "lấy" | Quy trình Drop (BP-02, BP-05, BP-04) |
| **PQ-17** | **Dữ liệu vị trí/loại rack**: giao diện cũ có `Rack_Full/Rack_Empty/Rack_Type/Destination_Available`; giao diện mới không. WCS xác định Location, loại rack và điểm đích thế nào? Xác nhận ánh xạ trạm+hướng ↔ quy trình: A1/A2 Pick=BP-01, Drop=BP-02; B1/B2 Pick=BP-03, Drop=BP-04 (REVIEW – không triển khai); F5 Drop=BP-05, Pick=BP-06; còn trạm E/G/X/D thì sao? | Không có tín hiệu dữ liệu trong 20/11 | Tạo task/BP; xác thực vị trí |
| **PQ-18** | **An toàn**: các tín hiệu `Safety_Ready`, `Station_Online`, `Station_Ready`, `Rack_Present` (và `Station_Fault` của IO Map v2) không còn. PLC có tự bảo đảm an toàn (interlock) không? `AGV_RequestTranfer` là lệnh **mở cửa/kẹp** – phân loại an toàn của từng tín hiệu SW→PLC (`AGV_RequestTranfer`, `AGV_TranferCompleted`, `Mission_Feedback`, `HeartBit`); điều kiện WCS **không được** ghi | K7; giao diện 10/11 | Chính sách ghi; commissioning |
| **PQ-19** | **`HeartBit`**: theo **PLC** hay theo **trạm**? Dung sai chu kỳ 1 s; "timeout 10 s" do ai đo (PLC đo phần mềm?) và PLC làm gì khi hết; có heartbeat PLC→WCS không; WCS có phát heartbeat khi đang khôi phục hoặc khi không có giao dịch? | K28 | Bộ ghi heartbeat; mô hình PLC-scoped |
| **PQ-20** | **Mapping trạm/PLC**: khối `EF-G` (192.168.10.131) liệt kê **E1, E2, G1, G2**; baseline đã duyệt: PLC-04 = **E1, E2, G3, G7**. Tên đúng là gì? "EF-G" có nghĩa gì? Các PLC/trạm còn lại ("tương tự") là trạm nào (A1–A4, B1/B2/D1/D2, X1/X2/X3/X6)? | A4, B4:B22, A35; `WCS_BP_Port_PLC_Transaction_Matrix` OP-03 | Cấu hình trạm; không đổi khi chưa trả lời |
| **PQ-21** | **`Spair`** (INT×2, BOOL×3): theo PLC hay theo trạm; chiều tín hiệu; xác nhận **không dùng** (không đọc/ghi) | Hàng 29–33 | Mô hình IO |
| **PQ-22** | **Một giao dịch tối đa mỗi PLC** (nguyên tắc FIFO theo PLC) **hay độc lập theo trạm** (bộ tín hiệu riêng từng trạm, hai trạm cùng PLC cùng lúc)? Cơ cấu vật lý (chung hàng đợi/cơ cấu kẹp) có buộc tuần tự không? | Xung đột C-1 | Kiến trúc hàng đợi/DB |
| **PQ-23** *(nội bộ – RCS)* | **Nguồn sự kiện "AGV đã đến vị trí trước cửa"** (cho `AGV_RequestTranfer`) và "đã lấy và ra ngoài" (cho `AGV_TranferCompleted`) lấy từ đâu: callback RCS `start/outbin/end`, trạng thái task, hay xác nhận khác? | D16, D20; RCS API V3.3 (5.3.1) | Ghi tín hiệu SW→PLC |
| **PQ-24** | **Chặng nhiều bước**: PLC chỉ biết trạm; với chặng mà trạm đích khác trạm phát yêu cầu (ví dụ BP-01 thả tại B1/B2; điểm thả cần PLC cho phép), trạm đích có phát `PLC_MissionRequest`/dùng các tín hiệu này không? (liên quan Q-LEG-1) | Sơ đồ chỉ mô tả một trạm | Bàn giao tại trạm đích |
| **PQ-25** | **Chuyển đổi**: thời điểm PLC chuyển từ giao diện 10/11 (M9xxx/Dxxxx) sang 20/11; có chạy song song không; các thanh ghi cũ còn hiệu lực/bị thu hồi? | – | Kế hoạch cắt chuyển |

## C. Quyết định kiến trúc đã chốt (2026-11-21, ADR-0011) – không còn là câu hỏi mở của WCS

| Mã | Quyết định cần chủ dự án | Liên quan |
|---|---|---|
| **D-1 – ĐÃ CHỐT** | Tối đa **một giao dịch Handshake hoạt động mỗi PLC**; mỗi PLC một FIFO queue; các PLC độc lập, song song; Station cùng PLC không Handshake đồng thời. Thay thế một phần ADR-0008 QĐ 4. **Chưa triển khai** (mã hiện chưa có FIFO cấp PLC) | Giải quyết C-1; bên PLC **xác nhận khả thi** (PQ-13, PQ-14, PQ-22) và ranh giới cửa sổ Handshake (PQ-26) |
| **D-2 – ĐÃ CHỐT** | AGV1/2/3 **không** là định danh cố định của AMR; RCS chọn AMR; WCS lưu `RobotCode` thật; `Mission_Feedback` = trạng thái tiếp nhận/xử lý; không sửa enum workbook; cần giao diện phản hồi mới và (nếu PLC cần) vùng dữ liệu `RobotCode` do hai bên thống nhất | Giải quyết C-2; PQ-06, PQ-29; đề xuất IF-P3 |
| **D-3 – ĐÃ THỰC HIỆN** | CLAUDE.md mục 7 cập nhật: workbook 20/11 = giao diện mới chờ xác nhận/phê duyệt, **không** phải mapping đã duyệt; IO Map v2 = baseline cũ chỉ tham khảo; bảo toàn lịch sử | Giải quyết C-3 |
| **D-4 – ĐÃ CHỐT (yêu cầu)** | **Không chấp nhận** mất đối chiếu rack vật lý ↔ Inventory: cần rack có mặt, danh tính rack khi quy trình yêu cầu, định danh/chống trùng giao dịch, điều kiện an toàn/interlock; nếu PLC không có dữ liệu thì xác định thiết bị/hệ thống khác; `PLC_MissionCompleted=1` không đủ | Giải quyết C-5 dưới dạng yêu cầu; PQ-10, PQ-17, PQ-18, PQ-27, PQ-28; đề xuất IF-P1, IF-P2, IF-P4 |

## D. Đã xác định rõ trong tài liệu nguồn – không cần hỏi lại [N]

Tên 6 tín hiệu mỗi trạm, chiều, kiểu BOOL/INT; giá trị `PLC_MissionRequest` 0/1/2; `Mission_Feedback` 0/1/2/3/>10; `PLC_ReaddyTranfer` 0/1; `PLC_MissionCompleted` 0/1; `HeartBit` bật/tắt 1 s, timeout 10 s; thứ tự 6 mũi tên của sơ đồ; mô tả ý nghĩa từng tín hiệu (cột K).

## E. Câu hỏi bổ sung sau khi chốt D-1…D-4 (chờ xác nhận)

| Mã | Câu hỏi | Liên quan | Chặn |
|---|---|---|---|
| **PQ-26** | **Cửa sổ "Handshake đang hoạt động" (D-1)**: bắt đầu từ lúc nào (khi WCS nhận `PLC_MissionRequest`? khi `AGV_RequestTranfer`? khi AMR đến cổng?) và kết thúc khi nào (khi `PLC_MissionCompleted` có kết quả? khi AMR đã rời trạm? khi PLC reset?). Trong lúc một Station đang Handshake, các Station khác cùng PLC **được phép giữ yêu cầu** (`PLC_MissionRequest` ≠ 0) hay phải chờ? Nếu PLC chỉ phát xung, yêu cầu chờ bị mất? PLC xử lý thế nào khi WCS chưa cấp lượt | D-1, PQ-02, PQ-13 | Thiết kế hàng đợi/điều phối cấp PLC |
| **PQ-27** | **Rack có mặt tại Station (D-4)**: có cảm biến/tín hiệu nào cho biết rack đang ở Station (trước khi giao/nhận) và sau khi giao/nhận? Là tín hiệu PLC (giao diện hiện không có) hay thiết bị khác? | D-4, PQ-17 | Đối chiếu vật lý; xác nhận bàn giao |
| **PQ-28** | **Danh tính rack (D-4)**: ai/cái gì xác định rack thực tế tại Station (RFID/barcode/đầu đọc, nhập tay, mã do RCS báo `podCode`)? Nếu PLC không cung cấp, thiết bị/hệ thống nào cung cấp, qua giao diện nào, dạng dữ liệu nào, và quy trình nào bắt buộc kiểm tra? | D-4 | Kiểm tra đúng rack; đối soát |
| **PQ-29** | **Truyền `RobotCode` thật (D-2)**: PLC có cần nhận định danh AMR không? Nếu có: kiểu dữ liệu, độ dài, mã hóa, vùng dữ liệu (không dùng AGV1–3), thời điểm ghi/xóa, ai reset; nếu không: PLC chỉ cần trạng thái (đề xuất IF-P3)? | D-2, PQ-06 | Giao diện phản hồi mới |

## F. Đề xuất thay đổi giao diện – **cần bên PLC phê duyệt** (không phải tên/địa chỉ chính thức)

Các mục dưới đây là **đề xuất của WCS** để đáp ứng D-1…D-4. Chúng mô tả **dữ liệu logic cần có**, **không** đặt tên tín hiệu, địa chỉ hay giá trị; tên/địa chỉ/giá trị chỉ do hai bên thống nhất. Workbook nguồn **không** bị sửa.

| Mã | Đề xuất | Đáp ứng | Cần bên PLC |
|---|---|---|---|
| **IF-P1** | Dữ liệu logic **"rack có mặt tại Station"** (trước và sau giao/nhận), cho từng Station | D-4 (có mặt) | Xác nhận cảm biến/nguồn; chiều; mức/xung |
| **IF-P2** | Dữ liệu logic **định danh giao dịch duy nhất** do PLC phát (hoặc cơ chế tương đương) và quy tắc chống xử lý trùng | D-4 (chống trùng); thay vai trò của `Request_ID` cũ | Xác nhận khả năng sinh ID, độ dài, đơn vị, quy tắc lặp (PQ-10) |
| **IF-P3** | **Giao diện phản hồi mới** của WCS: trạng thái tiếp nhận/xử lý yêu cầu (ví dụ các trạng thái: đã nhận / đang xử lý / đã có AMR nhận / lỗi) **tách khỏi** định danh AMR; kèm (nếu PLC cần) **vùng dữ liệu riêng cho `RobotCode` thật** | D-2 | Xác nhận trạng thái cần thiết, giá trị, vùng dữ liệu (PQ-29), không dùng AGV1–3 làm định danh |
| **IF-P4** | **Điều kiện an toàn/interlock** cần xác nhận trước khi WCS ra lệnh mở cửa/kẹp (và cờ "Station sẵn sàng/lỗi") | D-4 (an toàn) | Danh sách điều kiện, nguồn, chiều (PQ-18) |
| **IF-P5** | Dữ liệu logic **"Handshake đang hoạt động / đang được phép của Station này"** để PLC và WCS cùng biết lượt hiện tại của PLC | D-1 | Xác nhận khả thi; định nghĩa cửa sổ (PQ-26) |

Nếu bên PLC không thể cung cấp IF-P1/IF-P2 thì phải xác định **thiết bị/hệ thống khác** (PQ-27, PQ-28) – không chấp nhận bỏ đối chiếu rack (D-4).
