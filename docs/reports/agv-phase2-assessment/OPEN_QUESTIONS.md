# AGV Phase 2 – Câu hỏi cần người dùng xác nhận

> Mỗi câu hỏi nêu: bối cảnh có chứng cứ, tại sao chặn, và phương án (nếu có) – **mọi phương án là [ĐX], không phải quyết định**. Không câu nào được tự giải quyết trong bản audit này.
> Mức: **B** = chặn thiết kế/triển khai; **H** = ảnh hưởng lớn nhưng có thể làm song song; **M** = cần biết trước khi hoàn thiện.
> Câu hỏi gửi bên PLC/vendor đã có sẵn ở `docs/02_Interface_Specification/PLC_20_11_2026/PLC_VENDOR_CLARIFICATION_QUESTIONS_20_11_2026.md` (không lặp lại; xem Q-09…Q-13 là phần bổ sung phát sinh từ audit này).

## A. Phạm vi và yêu cầu

| ID | Mức | Câu hỏi | Bối cảnh / chứng cứ | Phương án [ĐX] |
|---|---|---|---|---|
| Q-01 | **B** | Tài liệu yêu cầu **AGV Phase 2** chính thức nằm ở đâu? Phase 2 gồm những tính năng nào? | Repo không có đặc tả Phase 2 được duyệt (`docs/specification/process-flows.md:133`: không suy ra Phase 2). Chỉ có matrix R8–R13 + `PH2-01…06` dạng dữ liệu nháp | Cung cấp tài liệu hoặc xác nhận danh sách phạm vi; sau đó mở CR (CLAUDE.md §3) |
| Q-02 | **B** | "Nhiều Pick/Drop operation trong một Transport Job" nghĩa là gì: (a) chuỗi Leg tuần tự (đã có), (b) một nhiệm vụ RCS nhiều điểm dừng, (c) AMR mang nhiều rack cùng lúc? | `TransportLeg` chỉ có 1 nguồn/1 đích/1 rack (`Domain/Routing/TransportOrder.cs:111-129`); mỗi Leg là một nhiệm vụ RCS độc lập (`RouteEngine.cs:475`) | (a) không cần phát triển; (b)(c) cần đổi mô hình (CR lớn, ảnh hưởng RCS contract) |
| Q-03 | H | "Origin-based Rack selection" định nghĩa thế nào (chọn rack theo nơi rack/yêu cầu xuất phát; trả rack về nơi xuất phát; ưu tiên theo nguồn)? | Code hiện chọn theo vị trí ứng viên + rack cũ nhất (`Repositories.cs:59-62`); không có trường "origin" | Có thể mô hình hóa bằng điều kiện/thuộc tính mới sau khi có định nghĩa |
| Q-04 | H | Phạm vi của "FIFO": chỉ giao dịch handshake mỗi PLC (D-1, đã chốt) hay còn **chờ buffer/chờ rack** giữa các Transport Task? Tie-break giữa B1 vs B2, P1 vs P2 là gì? | Hiện: gần FIFO theo `CreatedAt` nhưng không có hợp đồng (`RoutingRepository.cs:62-70`); tie-break theo thứ tự tài liệu "NEEDS_CONFIRMATION" (`BusinessProcess.cs:205-206`) | Quyết định phạm vi; nếu cần FIFO chờ tài nguyên thì cần hàng đợi bền |
| Q-05 | H | Phase 2 chạy ở chế độ **RcsManaged** (RCS chọn AMR; không PER_AGV) hay **WCS chọn AGV** (PER_AGV, role Full/Empty)? Quy tắc PER_AGV được bảo vệ áp dụng thế nào khi RCS tự chọn xe? | `RouteEngine.cs:473-491` vs `:493-520`; PER_AGV chỉ ở nhánh WCS-chọn; CLAUDE.md §2 | Xác nhận chế độ vận hành mục tiêu và cách diễn giải PER_AGV với đội AMR động |

## B. Trạm, PLC, buffer

| ID | Mức | Câu hỏi | Bối cảnh / chứng cứ | Phương án [ĐX] |
|---|---|---|---|---|
| Q-06 | **B** | P1/P2 (buffer hàng), M5 (QC thủ công), F5, vị trí chờ trung gian: là Location có Port PLC hay chỉ vị trí WCS/AGV? PLC nào, giao thức nào? | Matrix R8–R13 cột "PLC đích": "chưa rõ"; OP-01/02; Location không có Port được coi là luôn sẵn sàng (`RouteConditionEvaluator.cs:106,115`) | Không tự gán PLC. Khai báo Location không-Port tạm thời nếu chấp nhận bỏ qua handshake tại đó |
| Q-07 | M | Mức độ ưu tiên/dung lượng của P1/P2/M5 (capacity, loại rack được phép)? | `Location.RackCapacity/AllowedRackKind` cần cấu hình từ thực tế (`Location.cs:29`) | Người dùng cung cấp |
| Q-08 | H | Phase 2 có điểm "Không bind rack" nào (vd. thả tại B1/B2, A1/A2, P1/P2)? Ma trận R2 "B1/B2 drop không bind rack"; PH2-01 B1/B2 hiện **không** `noBind` | `reference-processes.json` PH2-01…06 không có `noBind`; BP-01 có (`process-flows.md:36,74,120`) | Xác nhận từng điểm; nếu quên sẽ vi phạm quy tắc "không tự thêm bind rack" |
| Q-16 | H | Vị trí chờ trung gian (AMR đứng chờ khi chưa có đích): ai điều AMR tới đó (WCS gửi lệnh hay RCS/slide tự xử lý)? Mã vị trí là gì? | `WaitingPositionCode` chỉ mô tả (`BusinessProcess.cs:166-167`); PH2-03/05 ghi NEEDS_CONFIRMATION | Cần quy ước giữa WCS–RCS |

## C. PLC handshake (bổ sung cho bộ câu hỏi PLC có sẵn)

| ID | Mức | Câu hỏi | Bối cảnh / chứng cứ |
|---|---|---|---|
| Q-09 | **B** | Giao diện 20/11 không còn `Request_ID`. Định danh duy nhất của một yêu cầu để chống tạo lệnh trùng (D-4) lấy từ đâu (counter, tổ hợp PLC+trạm+thời điểm, thiết bị khác)? | `Request_ID` hiện là khóa chống trùng: `RoutingConfigurations.cs:167`, `PlcRequestService.cs:39-52`; bản 10/11 có `Request_ID` STRING (D1500) |
| Q-10 | **B** | Workbook 20/11 ở working tree (sha `a291d28f…`, đã điền địa chỉ D1500… và đổi giá trị tín hiệu) khác bản đã commit/CR-029 (`3c5da74b…`). Ai thay đổi? Bản nào là chuẩn? Có mở CR để cập nhật CR-029 không? | `git status`: `M AGV_PLC Interface_List_20_11_2026.xlsx`; CLAUDE.md §7.6 yêu cầu CR cho mọi thay đổi |
| Q-11 | **B** | "Một Handshake hoạt động mỗi PLC" bắt đầu và kết thúc ở thời điểm nào: toàn bộ Station Transaction (đến khi AMR rời cổng) hay chỉ 6 bước handshake (từ `PLC_MissionRequest` đến `PLC_MissionCompleted`)? | `StationTransaction` hiện kéo dài đến `AgvExited` (`StationTransaction.cs:71-89`); ADR-0011 nêu rõ mốc **chưa được định nghĩa** |
| Q-12 | **B** | Ai reset từng tín hiệu và khi nào (`PLC_MissionRequest`, `PLC_ReaddyTranfer`, `AGV_RequestTranfer`, `AGV_TranferCompleted`, `PLC_MissionCompleted`)? Thời gian chờ/timeout/retry? | `Trang_tính1` không ghi; `Handshake.cs:9-27` không có rule được duyệt |
| Q-13 | **B** | Với D-4: PLC có cung cấp rack presence/identity/interlock không? Nếu không, thiết bị/hệ thống nào? | `PLC_MissionCompleted` không đủ (D-4); giao diện 20/11 không có tín hiệu hiện diện/định danh |
| Q-17 | H | `PLC_MissionCompleted`: 1 = Thất bại, 2 = Thành công (bản working tree) – đúng chủ ý? `AGV_RequestTranfer` dùng 1 và 4, `AGV_TranferCompleted` dùng 3 và 4 – có giá trị 2 không? `AGV_TranferCompleted` địa chỉ ở đâu? | Workbook working tree: mọi `AGV_TranferCompleted` không có địa chỉ (16/16) |
| Q-18 | H | Heartbeat `M9200` (+ spare `M9201–M9205`) là PLC-scoped hay Station-scoped? WCS ghi bit này khi nào được phép (commissioning)? | CLAUDE.md §7.3 yêu cầu phân loại; §7.5 chỉ ghi sau khi PLC engineer duyệt |
| Q-19 | H | Mâu thuẫn tên trạm: PLC3 `X6` (catalog/matrix) vs `X4` (workbook 20/11 WT); PLC4 `G3,G7` vs `G1,G2` (PQ-20). Nhãn PLC3 trong workbook là "A2" – lỗi gõ? | `plc-io-catalog.json`; matrix OP-03/04 |

## D. Vận hành và kết quả đối soát

| ID | Mức | Câu hỏi | Bối cảnh / chứng cứ | Phương án [ĐX] |
|---|---|---|---|---|
| Q-14 | **B** | Ai/hệ thống nào xác nhận Pick/Drop (toán tử, PLC `PLC_MissionCompleted`, RCS callback, kết hợp)? Điều kiện đủ để WCS chốt bàn giao? | Hiện chỉ toán tử (`RoutingEndpoints.cs:72,75`); RCS Completed không đủ (`RouteEngine.cs:398-402`); D-4 | Quyết định nguồn xác nhận và quy tắc "đủ" (presence + identity + vị trí) |
| Q-15 | **B** | Cách giải quyết `ReconciliationRequired` cho **Location** và **Order/Leg** trong mô hình CR-010 (hiện không có lối thoát; xem RACK_INVENTORY §3, MISSION §6)? Ai có quyền? | `Location.SetReconciliationRequired(false)` chỉ gọi trong luồng legacy `InventoryService.cs:251`; `PlcRequestService.cs:104` khóa Location | Cần CR bổ sung chức năng đối soát cho Order/Leg/Location (ảnh hưởng quyền, UI, audit) |
| Q-20 | M | Có cần timeout/giải phóng giữ chỗ buffer khi Order treo? Có cảnh báo hết hạn? | Reservation không có hạn (`Reservation.cs:146-183`) | |
| Q-21 | M | Chiến lược cutover: khi Phase 2 bắt đầu chạy trên main, luồng legacy A1/A2… (`ProcessFlowOptions`) còn chạy song song hay được thay bằng BP/PH2 (CR-010)? | Hai mô hình cùng tồn tại (ARCHITECTURE §2); A3/A4, D1/D2 UNCHANGED | |
| Q-22 | M | Merge `feature/cr-030-real-rcs-dispatch` lên `main` khi nào? Phase 2 phụ thuộc bộ gửi thật | `Infrastructure/DependencyInjection.cs:90` chỉ có simulator trên main | |

## E. Câu hỏi đã có sẵn trong tài liệu khác (không lặp lại)

- Bộ câu hỏi gửi bên PLC: `PLC_VENDOR_CLARIFICATION_QUESTIONS_20_11_2026.md` (mục A 15 câu bắt buộc, B 10 câu bổ sung, E sau khi chốt D-1…D-4).
- Câu hỏi RCS: `docs/03_RCS_Interface/VENDOR_QUESTIONS_CR-028.md`, `docs/reviews/CR008B-REV005_RCS_VENDOR_QUESTIONS.md`.
- Câu hỏi PLC cũ: `docs/02_Interface_Specification/PLC_SPEC_OPEN_QUESTIONS.md`.

## F. Những điều **không thể xác minh** trong audit này

| Điều | Lý do |
|---|---|
| Process BP/PH2 nào đang ở trạng thái Published trong DB triển khai | Không truy cập DB thật; `reference-processes.json` chỉ là dữ liệu import Draft |
| Hành vi PLC thật (MC Protocol, thanh ghi, thời gian) | Không có PLC; read-only/simulated; mọi giao diện 20/11 chưa duyệt |
| Hành vi RCS thật khi dispatch nhiều Leg | Main chỉ có bộ gửi giả lập; nhánh CR-030 chưa merge, chưa được đọc kỹ trong audit này |
| Hiệu năng/độ trễ dưới tải (quét 200 Order/50 Leg mỗi tick) | Không chạy test tải |
| Kết quả chạy test | Audit không chạy test (không đổi gì; xem TEST_ASSESSMENT) |
| Giao diện frontend quản lý trạm thủ công F5/M5 | Không đọc frontend trong audit này |
| Luồng legacy A3/A4, D1/D2 chi tiết | Giữ UNCHANGED, không rà từng dòng |
