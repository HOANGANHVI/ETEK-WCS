# AGV Phase 2 – Bộ kiểm thử nghiệm thu nghiệp vụ và kỹ thuật

> **IO MAP HIỆN HÀNH (2026-11-26, CR-045).** Dự án chỉ dùng một IO Map: [AGV_PLC Interface_List_22_11_2026.xlsx](../../02_Interface_Specification/PLC_IO_Map/AGV_PLC%20Interface_List_22_11_2026.xlsx) (SHA-256 `405586c9…34da3`). Các workbook cũ (IO Map v2, 10/11, 20/11) **đã ngừng sử dụng và đã gỡ khỏi repository**; nội dung dựa trên chúng trong tài liệu này chỉ còn giá trị lịch sử. IO Map 22/11 **chưa** được kỹ sư PLC xác nhận là hợp đồng IO. [Trạng thái từng tín hiệu](../../02_Interface_Specification/PLC_IO_Map/README.md).

| | |
|---|---|
| **Trạng thái** | **DRAFT – chờ chủ dự án duyệt (Revision 1.2).** Đây là **đặc tả test**, chưa test nào được viết hay chạy. **Mọi test: `NOT RUN`.** Không test nào được ghi Passed. |
| **Quy ước** | **[QĐ]** căn cứ quyết định đã duyệt · **[ĐX]** hành vi kỳ vọng là diễn giải cần xác nhận · **BLOCKED(OQ-xx)**: kỳ vọng chưa thể xác định vì câu hỏi mở – test chỉ được hoàn thiện sau khi OQ được trả lời (không đoán kết quả) |
| **Loại** | `INT` = tích hợp tự động (xUnit + PostgreSQL + simulator RCS/PLC, theo hạ tầng `tests/WCS.Tests/Support/*`) · `UNIT` · `COMM` = commissioning/UAT có thiết bị (read-only trước) · `E2E` |
| **Phụ thuộc** | Các test đánh dấu `Gate` chỉ chạy được sau khi CR liên quan được duyệt và triển khai (xem `PHASE2_OPERATIONAL_SPEC.md` §13) |
| **Dữ liệu** | Số liệu trong test là **minh họa**; Location P1–P4/M5–M6, Origin, capacity là dữ liệu test, **không** phải cấu hình sản xuất (OQ-11). Không địa chỉ PLC thật trong test tự động |
| **Revision 1** | 2026-11-24 – thêm §14–§19: test cho quy trình động, Execution Step, truy vết giao tiếp, observability, gói chẩn đoán (đề bài Revision 1 bị cắt – phần này suy từ R1-A…E). **Mọi test NOT RUN.** Nhãn **[QĐ R1-A…E]** = yêu cầu bổ sung đã duyệt (xem `PHASE2_OPERATIONAL_SPEC.md` §15–§19). Không đổi quyết định D-01…D-08. |
| **Revision 1.1** | 2026-11-24 – áp dụng **D-09** (PH2-04 gỡ REVIEW – thay T-FL-05, T-LG-03, T-DP-01; thêm T-P4-01…12), **D-10** (ghim Version tại Queue intake – T-PIN-A…I, sửa T-DP-08), và Issue 2–5 (T-RD, T-RT, T-DU, T-EV; sửa T-CT-03/12, T-OB-05). **Giữ nguyên ID**; không xóa test; **mọi test vẫn NOT RUN**. |
| **Revision 1.2** | 2026-11-24 – áp dụng **D-11** (Two-Gate: Publication ⟂ Readiness), **D-12** (Queue Intake chỉ Published + Ready; Intake Record; Held), **D-13** (strict FIFO theo Queue Ordering Scope): thêm §29–§36 (T-PG, T-RG, T-QI, T-HD, T-SF, T-DR – 59 test); sửa T-FI-01, T-FI-02, T-FI-04, T-QU-01, T-DP-01, T-DP-08, T-P4-01, T-P4-07, T-P4-09, T-P4-10, T-PIN-A/B/C/G. **Giữ nguyên ID**; không xóa test; **mọi test vẫn NOT RUN**. |

## 0. Các test hiện có liên quan (đã đọc, **không chạy** trong P0)

| Chủ đề | Test hiện có (file:dòng) | Còn phù hợp Phase 2? |
|---|---|---|
| A1→B1 trực tiếp; buffer; tiếp tục | `Integration/Cr010MultiLegTests.cs:33,57,81` | Phù hợp khung; cần mở rộng Origin/Queue |
| Giao dịch trạm xong khi Job còn Waiting | `Cr010MultiLegTests.cs:108,125` | Phù hợp |
| **Hai cổng cùng PLC giữ hai giao dịch đồng thời** | `Cr010MultiLegTests.cs:145` (Test07) | **Mâu thuẫn D-03** – phải cập nhật bằng CR (P2-B), không sửa lặng lẽ |
| Hai PLC song song | `Cr010MultiLegTests.cs:162` | Phù hợp |
| Hai yêu cầu cùng cổng | `Cr010MultiLegTests.cs:179` | Hành vi "reject" sẽ đổi thành "xếp hàng" |
| Trùng `Request_ID` | `Cr010MultiLegTests.cs:193` | Phụ thuộc OQ-05 |
| Lệch PLC–tồn kho → đối soát | `Cr010MultiLegTests.cs:210` | Phù hợp |
| Không đích: AMR giữ Rack | `Cr010MultiLegTests.cs:228` | Cần đối chiếu D-04/OQ-18 |
| Không dùng vị trí ngoài ứng viên; PER_AGV | `Cr010MultiLegTests.cs:257,273,295` | Phù hợp |
| Restart giữa chừng | `Cr010MultiLegTests.cs:318` | Phù hợp |
| Hủy bị từ chối khi Rack trên AMR | `Cr010MultiLegTests.cs:340` | Phù hợp |
| Process config, REVIEW, protected, "Phase 2 cần Location riêng" | `Cr010ProcessConfigTests.cs:175,189,202,214` | Phù hợp |
| Slot đích cuối chỉ cấp cho một | `ConcurrencyTests.cs:126` | Phù hợp |
| Khung handshake 10/11 | `Plc/HandshakeTests.cs` (12) | Không phủ 20/11 |

## 1. Quy tắc chung cho mọi test

- **Pass tổng quát:** không có thay đổi `Current Location` khi mới tạo/dispatch; không Job `Completed` khi thiếu xác nhận bàn giao; không Reservation bị giải phóng trước đối soát khi bất định; không lệnh ghi PLC nào ngoài danh sách được duyệt; mọi chuyển trạng thái có bản ghi lịch sử/audit.
- Mỗi test ghi: **Preconditions**, **Chuỗi sự kiện**, **Chuyển trạng thái kỳ vọng**, **Rack/Inventory kỳ vọng**, **Queue/Reservation kỳ vọng**, **Tiêu chí Pass**.

---

## 2. Transport Job lifecycle

### T-LC-01 – Job một Leg hoàn tất khi Drop được xác nhận [QĐ D-01, D-05] · INT · Status: NOT RUN
- **Pre:** process một Leg đã Published (dữ liệu test); Rack `R1` Full tại A1 (Origin=A1); đích B1 trống; RCS/PLC mô phỏng online.
- **Chuỗi:** yêu cầu hợp lệ → Job tạo → Leg Ready → Dispatch → Pick xác nhận → Drop xác nhận.
- **Trạng thái:** Job Created→Accepted→InProgress→Completed; Leg Pending→Ready→InProgress→Completed; Station Transaction nguồn/đích Completed sau khi AMR rời.
- **Rack/Inventory:** sau Pick `R1` InTransit, Current Location=null, Origin=A1; sau Drop Current Location=B1, Full, Origin=A1.
- **Queue/Reservation:** Reservation nguồn Consumed (Pick), đích Consumed (Drop); Queue rỗng.
- **Pass:** các điều kiện trên + `InventoryTransaction` Pick/Drop.

### T-LC-02 – Station Transaction hoàn tất không hoàn tất Job [HOẠT ĐỘNG] · INT · NOT RUN
- **Pre:** Job nhiều Leg (xem T-ML-01). **Chuỗi:** Leg 1 Drop xác nhận, AMR rời trạm.
- **Kỳ vọng:** Transaction Leg 1 Completed; Job **chưa** Completed (còn Leg 2). **Pass:** Job vẫn InProgress/Waiting.

### T-LC-03 – Idempotency tạo Job [QĐ INV-14] · INT · NOT RUN
- **Chuỗi:** cùng `RequestKey` gửi 2 lần. **Kỳ vọng:** một Job; lần hai trả Job cũ. **Pass:** không có Leg/Reservation trùng.

### T-LC-04 – Maintenance không tạo Job mới [HOẠT ĐỘNG] · INT · NOT RUN
- **Chuỗi:** bật Maintenance → yêu cầu mới. **Kỳ vọng:** từ chối trước khi ghi giao dịch; Job đang chạy tiếp tục. **Pass:** không Transaction/port bị chiếm.

## 3. Multi-Leg

### T-ML-01 – Job hai Leg tuần tự [QĐ D-01] · INT · NOT RUN
- **Pre:** process 2 bước di chuyển (vd. lưu Buffer rồi lấy lại – cấu trúc đã được engine hỗ trợ). 
- **Chuỗi:** Leg 1 xong (Drop xác nhận) → Leg 2 tạo → Leg 2 xong.
- **Kỳ vọng:** Leg 2 chỉ tạo sau Leg 1 Completed; mỗi Leg đúng 1 Rack/1 Pick/1 Drop; mỗi Leg có RCS Mission riêng. **Pass:** Job Completed khi cả hai Leg có Drop xác nhận.

### T-ML-02 – Rack không ở vị trí đã lưu khi bắt đầu Leg 2 [HOẠT ĐỘNG] · INT · NOT RUN
- **Chuỗi:** sau Leg 1, tồn kho báo Rack không ở đích Leg 1 → tạo Leg 2. **Kỳ vọng:** Job→ReconciliationRequired; **không** chọn vị trí khác. **Pass:** không Leg 2 được tạo.

### T-ML-03 – RCS đổi AMR khi Rack đang trên AMR [ADR-0010] · INT · NOT RUN
- **Kỳ vọng:** lần đầu cảnh báo; lặp lại liên tiếp → Job ReconciliationRequired; không cập nhật `RobotCode`/vị trí tự động. **Pass:** như kỳ vọng.

## 4. Các luồng vận chuyển chính

### T-FL-01 – A1 → B1 [PH2-01, nhóm 1] · INT/COMM · NOT RUN · Gate: P1-C, P2-C
- **Pre:** `R1` Full tại A1; B1 còn slot; P1/P2/M5 tồn tại (dữ liệu test). 
- **Chuỗi/Kỳ vọng:** như T-LC-01; đích **B1** (không dùng P/M); handshake Drop tại B1 theo giao diện được duyệt (khởi tạo: **BLOCKED(OQ-27)** cho COMM; INT dùng simulator). **Pass:** đích = B1, Origin giữ nguyên.

### T-FL-02 – A1 → Buffer (B1/B2 hết chỗ) [PH2-01, nhóm 2] · INT · NOT RUN · Gate: P1-B, P1-C
- **Pre:** B1, B2 không slot; P1 còn 1 slot. **Chuỗi:** yêu cầu A1 Full.
- **Kỳ vọng:** chọn **P1**; Reservation slot P1 (+1 reserved) → Drop xác nhận thủ công có audit (P1 WCS-managed, **không** handshake PLC). Rack: Current Location=P1, Origin=A1; P1 occupied +1.
- **Pass:** không có tín hiệu PLC nào gắn với P1; bản ghi audit có người thao tác.

### T-FL-03 – A1 → M5 (B và P đầy) [PH2-01, nhóm 3] · INT · NOT RUN
- **Pre:** B1,B2,P1,P2 không slot; M5 còn slot. **Kỳ vọng:** chọn M5; Drop thủ công có audit. **Pass:** Rack Current Location=M5.

### T-FL-04 – Buffer → A1 (A1 cần Empty) [PH2-02] · INT · NOT RUN · Gate: P2-A
- **Pre:** B1/B2 không có Empty khả dụng; P1 có `E1`(Empty, Origin=A1, t1), `E2`(Empty, Origin=A1, t2), `E3`(Empty, Origin khác, t0<t1).
- **Kỳ vọng:** chọn **E1** (Origin-phù-hợp, FIFO); `E3` **không** được chọn dù cũ nhất. Reservation nguồn `E1`. Pick tại P1 (thủ công) → Drop tại A1 (PLC). `E1`: Current Location=A1, Empty, Origin=A1. **Phụ thuộc định nghĩa "phù hợp": BLOCKED(OQ-16)** – test hoàn thiện sau khi có quan hệ Origin.

### T-FL-05 – B1/B2 cần Full Rack: A1/A2 là nguồn ưu tiên [PH2-04, D-09] · INT · NOT RUN · Gate: P1-G, P2-A *(thay kỳ vọng REVIEW cũ)*
- **Pre:** PH2-04 Published (dữ liệu test); A2 có Full Rack `F7` phù hợp chưa đặt chỗ; P1 có Full Rack `F1`; B1 còn chỗ nhận. **Chuỗi:** PLC B1 (mô phỏng) yêu cầu Full Rack.
- **Trạng thái:** chọn nguồn **A2** (nhóm 1); Job InProgress; Pick tại A2 (PLC) → Drop tại B1 (PLC). **Rack/Inv:** `F7` Full → InTransit (Current Location = null) → Current Location = B1, Full, Origin giữ nguyên; `F1` **không bị đụng**. **Queue/Res:** Reservation Rack chỉ cho `F7`; slot B1 giữ chỗ.
- **Pass:** nguồn = A1/A2; không truy cập P1/P2; Job Completed chỉ sau Drop xác nhận (E6).

### T-FL-06 – M5 trả Rack rỗng [PH2-05] · INT · NOT RUN · Gate: P3-C
- **Chuỗi:** Operator ghi yêu cầu qua PDA (có người thao tác, thời gian) → Pick tại M5 xác nhận thủ công → đích A1/A2 (nhóm 1) → P1/P2 (nhóm 2).
- **Kỳ vọng:** `Empty`, Origin giữ nguyên; Job không Completed khi còn chờ đích. Chờ ở vị trí trung gian: **BLOCKED(OQ-14/18)**.

### T-FL-07 – M5 yêu cầu Full Rack [PH2-06] · INT · NOT RUN · Gate: P2-A, P3-C
- **Pre:** P1 có Full `F1`(Origin phù hợp), `F2`(Origin khác). **Kỳ vọng:** chọn `F1`; Pick tại P1 và Drop tại M5 đều thủ công có audit. Khi chỉ có `F2`: **chờ** (D-02). Khi **không có Full Rack nào**: **BLOCKED(OQ-17)** (slide báo lỗi vs D-02 chờ).

## 5. Origin, FIFO, Queue, Buffer

### T-OR-01 – Origin phù hợp ưu tiên, FIFO trong nhóm [QĐ D-02] · INT · NOT RUN · BLOCKED(OQ-16, OQ-30)
- Như T-FL-04; thêm: hai Rack Origin phù hợp ở P1 và P2 – kết quả phụ thuộc OQ-30 (FIFO gộp hay theo Buffer).

### T-OR-02 – Không có Rack đúng Origin → không chọn Rack khác, chờ [QĐ D-02] · INT · NOT RUN
- **Pre:** P1/P2 chỉ có Empty Origin khác.
- **Kỳ vọng:** yêu cầu **ở lại Queue**; không Reservation nguồn; không Leg; không lỗi; không hủy; Rack khác Origin giữ nguyên Current Location. **Pass:** sau khi Rack Origin phù hợp được Drop vào Buffer (sự kiện đánh thức) → yêu cầu được xử lý.

### T-OR-03 – Origin ≠ Current Location [QĐ D-02] · UNIT · NOT RUN · Gate: P1-B
- **Chuỗi:** Rack Origin=A1 di chuyển qua P1 → M5 → A1. **Kỳ vọng:** Origin không đổi ở mọi bước; chọn Rack không dùng Current Location thay Origin.

### T-FI-01 – Strict FIFO theo Queue Ordering Scope [QĐ D-03, D-13] · INT · NOT RUN · Gate: P2-B · BLOCKED(OQ-54 cho cấu hình Scope)
- **Kỳ vọng (D-13 đã quyết, OQ-01 đóng):** trong **cùng Scope** không Request nào vượt Request đứng trước; đầu Scope Blocked thì Request sau chờ; Scope độc lập chạy song song (chi tiết: T-SF-01…03). Cấu hình Scope sản xuất: **BLOCKED(OQ-54)** – fixture dùng Scope giả lập.

### T-FI-02 – Một giao dịch hoạt động/PLC [QĐ D-03] · INT · NOT RUN · Gate: P2-B · BLOCKED(OQ-02 cho mốc kết thúc)
- **Pre:** PLC-01 có yêu cầu từ A1 và A2 (cùng PLC).
- **Kỳ vọng:** chỉ **một** giao dịch `Active` trên PLC-01; yêu cầu còn lại `Waiting` (bền, theo `QueueSequence`; khe PLC giữa các Scope: **BLOCKED(OQ-55)**); không `PORT_BUSY` reject. Khi giao dịch đầu kết thúc (mốc: OQ-02) → yêu cầu kế `Active`.
- **Pass:** không bao giờ có hai giao dịch hoạt động cùng PLC (kiểm bằng truy vấn + ràng buộc). *(Thay thế `Cr010MultiLegTests.cs:145` có kiểm soát.)*

### T-FI-03 – Nhiều PLC song song [QĐ D-03] · INT · NOT RUN
- **Chuỗi:** yêu cầu đồng thời ở PLC-01 (A1) và PLC-02 (B1). **Kỳ vọng:** mỗi PLC một giao dịch hoạt động độc lập, không khóa chéo. **Pass:** cả hai `Active` cùng lúc.

### T-FI-04 – Queue bền qua restart [QĐ D-03] · INT · NOT RUN · Gate: P2-B
- **Chuỗi:** ghi 3 yêu cầu cùng PLC (1 Active, 2 Waiting) → restart WCS. **Kỳ vọng:** thứ tự và nội dung Queue giữ nguyên; không mất/không trùng; yêu cầu Active tiếp tục/đối chiếu theo trạng thái thật. **Pass:** thứ tự FIFO bảo toàn.

### T-QU-01 – Không có đích → Queue, không hủy/lỗi [QĐ D-04] · INT · NOT RUN
- **Pre:** B1,B2,P1,P2,M5 đều không slot. **Chuỗi:** A1 báo Full Rack.
- **Kỳ vọng:** yêu cầu `Waiting` (Queue Request)/Job `Waiting` (+`WaitKind`); **không** Failed/Cancelled; **không** đổi đích ngoài danh sách. Vị trí Rack (A1 hay trên AMR) khi chờ: **BLOCKED(OQ-18)**.

### T-QU-02 – Đánh thức Queue khi có chỗ [QĐ D-04] · INT · NOT RUN
- **Chuỗi:** từ T-QU-01, `R20` rời P1 (Pick xác nhận). **Kỳ vọng:** sự kiện đánh thức → yêu cầu được đánh giá lại; nếu tới lượt → Reservation slot P1 → tiếp tục. **Pass:** không cần can thiệp thủ công; không race (xem T-RS-02).

### T-QU-03 – Điều kiện đánh thức khác [ĐX] · INT · NOT RUN
- Lặp lại cho: Reservation đích Released; Location bật `Enabled`/tăng capacity; gỡ `ReconciliationRequired`; PLC online lại; hết Maintenance. **Pass:** mỗi sự kiện đánh thức đúng yêu cầu đang chờ liên quan, đúng thứ tự Queue.

### T-BF-01 – Buffer đầy [QĐ D-04] · INT · NOT RUN
- **Pre:** P1 `capacity−occupied−reserved=0`. **Kỳ vọng:** P1 không được chọn; chuyển ứng viên kế; nếu hết → Queue. **Pass:** `reserved` không vượt capacity.

## 6. Reservation, đồng thời

### T-RS-01 – Reservation Rack nguồn duy nhất [QĐ D-06] · INT · NOT RUN
- **Chuỗi:** hai Job cùng chọn Rack `R1`. **Kỳ vọng:** chỉ một Reservation ACTIVE; Job kia chọn Rack khác/chờ. **Pass:** ràng buộc DB không bị vi phạm (index unique).

### T-RS-02 – Hai yêu cầu tranh slot cuối [HOẠT ĐỘNG] · INT · NOT RUN
- **Chuỗi:** hai Job cùng giữ slot cuối P1 đồng thời. **Kỳ vọng:** chỉ một thành công; Job kia đánh giá lại và chờ (không lỗi). **Pass:** reserved ≤ capacity. *(Mở rộng `ConcurrencyTests.cs:126`.)*

### T-RS-03 – Không giải phóng Reservation khi bất định [HOẠT ĐỘNG] · INT · NOT RUN
- **Chuỗi:** Leg đã gửi RCS rồi lỗi/Interrupted. **Kỳ vọng:** Rack/Location → bất định/ReconciliationRequired; Reservation **giữ** đến đối soát. **Pass:** không tài nguyên bị cấp cho Job khác.

## 7. Handshake và PLC

### T-PL-01 – Yêu cầu PLC trùng (duplicate) [QĐ ADR-0011 D-4] · INT · NOT RUN · BLOCKED(OQ-05)
- Kỳ vọng phụ thuộc cơ chế chống trùng khi không có `Request_ID`. **Không viết kết quả** cho tới khi OQ-05 được trả lời.

### T-PL-02 – PLC mất kết nối giữa chừng [HOẠT ĐỘNG] · INT · NOT RUN
- **Chuỗi:** giao dịch đang `Handling` → PLC mất link → nối lại. **Kỳ vọng:** giao dịch giữ trạng thái, đánh dấu `Suspended`; không hủy/reset/ghi tín hiệu; khi nối lại: đọc trạng thái thật → tiếp tục hoặc `ReconciliationRequired`; khi không đọc được → giữ nguyên. **Pass:** như kỳ vọng. *(Mở rộng `Cr010ProcessConfigTests.cs:294`.)*

### T-PL-03 – `PLC_MissionCompleted` đơn lẻ không đủ [QĐ D-05] · INT · NOT RUN · BLOCKED(OQ-03, OQ-12)
- **Chuỗi:** PLC báo hoàn tất nhưng không có bằng chứng Rack. **Kỳ vọng:** Leg **không** Completed; chờ bằng chứng/xác nhận thủ công có audit. Giá trị tín hiệu (1/2) theo bản workbook được duyệt – **chưa chốt**.

### T-PL-04 – Handshake đọc-chỉ khi commissioning [CLAUDE §7.5] · COMM · NOT RUN
- Kỳ vọng: không ghi tín hiệu an toàn/cho phép; chỉ đọc; ghi chỉ sau khi PLC engineer duyệt tín hiệu và quy trình thử. **Pass:** nhật ký không có lệnh ghi ngoài danh sách duyệt.

### T-PL-05 – Heartbeat [NGUỒN] · COMM · NOT RUN · BLOCKED(OQ-03/06)
- Chu kỳ 1 s/timeout 10 s theo workbook; phạm vi PLC hay Station chưa phân loại.

## 8. RCS

### T-RC-01 – RCS mất kết nối [HOẠT ĐỘNG] · INT · NOT RUN
- **Chuỗi:** Leg Ready khi RCS link không cho phép. **Kỳ vọng:** không dispatch; Leg giữ Ready; không mất; sau khi link phục hồi → dispatch tiếp. **Pass:** không Mission trùng.

### T-RC-02 – RCS Completed nhưng chưa có Drop confirmation [QĐ D-05] · INT · NOT RUN
- **Chuỗi:** RCS báo `Completed` cho Leg; chưa có Drop xác nhận.
- **Kỳ vọng:** ghi sự kiện `LEG_RCS_COMPLETED_WITHOUT_DROP`; Leg **không** Completed; **không** cập nhật Current Location; **không** chạy Leg kế; Reservation giữ. Chỉ khi có bằng chứng bàn giao (PLC chu trình + presence/identity hoặc PDA có audit) → Drop confirmed. **Pass:** như kỳ vọng. *(Đã có hành vi trong code `RouteEngine.cs:398-402`; cần test tự động.)*

### T-RC-03 – RCS từ chối Mission [HOẠT ĐỘNG] · INT · NOT RUN
- **Kỳ vọng:** Reservation (chưa gửi) được giải phóng; Leg Failed; Job Failed hoặc xử lý theo chính sách (OQ-17 nếu thiếu nguồn). **Pass:** không đổi tuyến âm thầm.

### T-RC-04 – Chế độ RCS [QĐ D-08] · INT · NOT RUN · BLOCKED(OQ-13)
- RcsManaged: không PER_AGV, `AgvId` rỗng, `RobotCode` do RCS; WCS-chọn-AGV: PER_AGV bảo toàn. Test theo chế độ được chọn.

## 9. Rack mismatch, reconciliation, restart

### T-RM-01 – PLC báo Rack nhưng tồn kho không có [HOẠT ĐỘNG] · INT · NOT RUN
- **Kỳ vọng:** `RECON_PLC_INVENTORY_MISMATCH`; Location đánh dấu; không Job/dispatch; tồn kho **không** bị PLC ghi đè. **Pass:** như kỳ vọng. *(Mở rộng `Cr010MultiLegTests.cs:210`.)*

### T-RM-02 – Pick tại Buffer nhưng Rack không có [QĐ D-06] · INT · NOT RUN
- **Chuỗi:** Operator xác nhận Pick tại P1 nhưng WCS không có Rack tương ứng. **Kỳ vọng:** từ chối; ghi audit; không đổi tồn kho. 

### T-RC-05 – Reconciliation Location/Order CR-010 [QĐ D-06/D-08] · INT · NOT RUN · Gate: P1-D · BLOCKED(OQ-15)
- Kỳ vọng phụ thuộc thiết kế đối soát: Admin xác nhận vị trí thực → `InventoryTransaction` điều chỉnh → giải phóng Reservation **một lần** → gỡ cờ.

### T-RS-04 – Restart WCS [HOẠT ĐỘNG] · INT · NOT RUN
- **Chuỗi:** restart giữa Job nhiều Leg và giữa lúc có yêu cầu đã ghi chưa tạo Job. **Kỳ vọng:** Leg đã xong không lặp; yêu cầu đã ghi được tạo Job đúng một lần; Queue (nếu có) bảo toàn. *(Mở rộng `Cr010MultiLegTests.cs:318`.)*

## 10. Trạm thủ công (WCS-managed)

### T-MN-01 – Không handshake PLC cho P1–P4, M5–M6 [QĐ D-07] · INT · NOT RUN · Gate: P1-C
- **Kỳ vọng:** validator từ chối gán Port PLC cho Location WCS-managed; Station Transaction (nếu có) không có `HandshakeStep`; không tín hiệu PLC nào được đọc/ghi cho các điểm này.

### T-MN-02 – F5 ≠ M5 [QĐ D-07] · UNIT · NOT RUN
- **Kỳ vọng:** cấu hình/process không đồng nhất F5 với M5; quan hệ chỉ do cấu hình tường minh được duyệt (OQ-10).

### T-MN-03 – Xác nhận thủ công có kiểm soát và audit [QĐ D-05] · INT · NOT RUN · Gate: P3-A/P3-C
- **Chuỗi:** Operator xác nhận Pick/Drop tại P/M. **Kỳ vọng:** `ConfirmationSource=Operator`; ghi người, thời gian, Rack, Location; quyền theo `access-control.md`; xác nhận nhầm Rack/Buffer có quy trình sửa sai có audit (**BLOCKED(OQ-21)**).

## 11. Legacy và bảo vệ

### T-LG-01 – Luồng legacy A3/A4, D1/D2 không bị thay đổi [QĐ D-08] · INT · NOT RUN
- **Kỳ vọng:** validator vẫn chặn process cấu hình dùng A3/A4/D1/D2 (`LOCATION_PROTECTED`); các test legacy (`Cr008*`, `ProcessFlowTests`, `ConcurrencyTests`, `TaskStateMachineTests`) vẫn đạt khi chạy hồi quy.

### T-LG-02 – PER_AGV bảo toàn [QĐ D-08] · INT · NOT RUN
- **Kỳ vọng:** hai task/Leg cho cùng AGV không chạy đồng thời (chế độ WCS-chọn-AGV); DB từ chối vi phạm (`ux_transport_legs_one_active_per_agv`). 

### T-LG-03 – BP-04 (Phase 1) không bị D-09 thay đổi [QĐ D-08, D-09] · INT · NOT RUN · BLOCKED(OQ-46)
- **Phạm vi:** chỉ **BP-04 Phase 1**. **PH2-04 không thuộc test này** (D-09: không còn REVIEW; xem T-P4-01…12).
- **Kỳ vọng:** hành vi hiện tại của BP-04 Phase 1 (REVIEW-blocked) **không đổi** cho tới khi có quyết định riêng/CR (OQ-46); `Cr010ProcessConfigTests.cs:186-198` chỉ áp cho BP-04 Phase 1. Dòng khẳng định `PH2-04 ReviewBlocked` (`:183-184`) được cập nhật bằng CR P1-G, **không** dùng làm expected cho PH2-04.

### T-LG-04 – Không phá dữ liệu/migration [QĐ D-08] · INT · NOT RUN · Gate: mọi CR có migration
- **Kỳ vọng:** migration cộng thêm; dữ liệu cũ nguyên vẹn; `Down` không xóa dữ liệu nghiệp vụ; có phương án rollback.

### T-LG-05 – Cutover BP ↔ PH2 [HOẠT ĐỘNG] · INT · NOT RUN · BLOCKED(OQ-28)
- Publish đồng thời BP-01 và PH2-01 cho cùng trigger → hiện `PROCESS_AMBIGUOUS`; kỳ vọng cutover do OQ-28 quyết định.

## 12. Ma trận phủ

| Yêu cầu D-xx | Test |
|---|---|
| D-01 | T-LC-01/02, T-ML-01…03 |
| D-02 | T-FL-04, T-FL-07, T-OR-01…03 |
| D-03 | T-FI-01…04 |
| D-04 | T-QU-01…03, T-BF-01 |
| D-05 | T-LC-01, T-RC-02, T-PL-03, T-MN-03 |
| D-06 | T-RS-01…03, T-RM-01/02, T-RC-05 |
| D-07 | T-FL-02/03, T-MN-01…03 |
| D-08 | T-LG-01…05, T-RC-04 *(PH2-04 không còn thuộc D-08 – xem D-09)* |
| D-09 | T-FL-05, T-P4-01…12 |
| D-10 | T-PIN-A…I, T-DP-04, T-DP-08 |
| Issue 2 (`PLC_ReaddyTranfer`) | T-RD-01…06 |
| Issue 3 (append-only/retention) | T-RT-01…08, T-CT-12, T-OB-05 |
| Issue 4 (bằng chứng bền vững) | T-DU-01…13 |
| Issue 5 (E0–E6) | T-EV-01…07, T-CT-03 |
| D-11 (Two-Gate) | T-PG-01…10, T-RG-01…08, T-DR-01…08 |
| D-12 (Queue Intake, Held) | T-QI-01…11, T-HD-01…10 |
| D-13 (Strict FIFO theo Scope) | T-SF-01…12, T-FI-01, T-FI-02, T-FI-04, T-P4-10 |

## 13. Tổng hợp trạng thái

| | Số lượng |
|---|---|
| Test được đặc tả | 219 (49 ban đầu + 56 Revision 1 + 55 Revision 1.1 + 59 Revision 1.2; ID giữ nguyên, không xóa test) |
| Chạy | **0** |
| Passed | **0** |
| BLOCKED bởi Open Question (63; tính theo tiêu đề – một số test còn ghi BLOCKED trong thân) | T-OR-01, T-FI-01, T-FI-02, T-PL-01, T-PL-03, T-PL-05, T-RC-04, T-RC-05, T-LG-03, T-LG-05, T-DP-01, T-DP-05, T-DP-07, T-DP-08, T-DP-10, T-ES-12, T-CT-11, T-CT-13, T-OB-05, T-OB-07, T-DG-06, T-P4-01, T-P4-03, T-P4-05, T-P4-06, T-P4-08, T-P4-09, T-P4-10, T-PIN-C, T-PIN-F, T-RD-01, T-RD-02, T-RD-03, T-RD-04, T-RD-05, T-RD-06, T-RT-03, T-RT-07, T-DU-01, T-DU-02, T-DU-03, T-DU-06, T-DU-09, T-EV-07, T-PG-05, T-PG-10, T-RG-05, T-RG-07, T-QI-05, T-QI-10, T-QI-11, T-HD-08, T-HD-09, T-SF-04, T-SF-05, T-SF-06, T-SF-07, T-SF-08, T-SF-09, T-SF-12, T-DR-03, T-DR-05, T-DR-06 |
| Gate (cần CR triển khai trước) | Các test ghi `Gate` |
| Test sửa ở Revision 1.1 | T-FL-05, T-LG-03, T-DP-01, T-DP-08, T-CT-03, T-CT-12, T-OB-05 |
| Test mới ở Revision 1.1 | 55 (T-P4-01…12, T-PIN-A…I, T-RD-01…06, T-RT-01…08, T-DU-01…13, T-EV-01…07) |
| Test sửa ở Revision 1.2 | T-FI-01, T-FI-02, T-FI-04, T-QU-01, T-DP-01, T-DP-08, T-P4-01, T-P4-07, T-P4-09, T-P4-10, T-PIN-A, T-PIN-B, T-PIN-C, T-PIN-G |
| Test mới ở Revision 1.2 | 59 (T-PG-01…10, T-RG-01…08, T-QI-01…11, T-HD-01…10, T-SF-01…12, T-DR-01…08) |

---

# PHẦN BỔ SUNG REVISION 1 – Quy trình động, Execution Step, giao tiếp, observability, gói chẩn đoán

> Quy ước giữ nguyên: **mọi test `NOT RUN`**; **BLOCKED(OQ-xx)** = kỳ vọng chưa xác định vì câu hỏi mở; `Gate` = cần CR triển khai. Trường: **Pre** (điều kiện đầu) · **Chuỗi** (đầu vào/sự kiện) · **Trạng thái** (chuyển trạng thái kỳ vọng) · **Rack/Inv** · **Queue/Res** · **Pass**. "Không đổi/N.A." = bất biến cần kiểm hoặc không áp dụng. Dữ liệu test (Station, Location, process) là **giả lập**, không phải cấu hình sản xuất; không dùng địa chỉ PLC thật.

## 14. Quản lý quy trình động (R1-A)

### T-DP-01 – Khởi tạo gói mặc định Published (PH2-01…06, gồm PH2-04) [R1-A, D-09, D-11] · INT · NOT RUN · Gate: P1-E, P1-G · BLOCKED(OQ-32)
- **Pre:** Location P1/P2/M5 và ánh xạ IO test đã có; DB trống process. **Chuỗi:** chạy khởi tạo gói mặc định (CR được duyệt).
- **Trạng thái:** cả sáu process (**gồm PH2-04**) → **Published qua Publication Gate** (Revision 1.2: Publication ⟂ Readiness; DC-01 đã giải quyết bởi D-11); Readiness ban đầu = **NotReady** (chưa nhận Request). **Rack/Inv:** không đổi. **Queue/Res:** không đổi.
- **Pass:** mỗi process Published có bản ghi phê duyệt/audit (actor `SEED`, người duyệt); chạy lại khởi tạo **idempotent**, không ghi đè chỉnh sửa người dùng; **không** có process nào bị loại khỏi gói chỉ vì cờ REVIEW của PH2-04.

### T-DP-02 – Vòng đời hợp lệ và chuyển trạng thái bị cấm [R1-A] · UNIT/INT · NOT RUN · Gate: P1-E
- **Chuỗi:** Draft → Pending Approval → Approved → Published; thử nhảy cóc (Draft → Published; Pending → Published; Approved → sửa nội dung).
- **Trạng thái:** chỉ đường hợp lệ thành công; ngoại lệ bị từ chối với mã rõ. **Pass:** version đã Pending Approval **không sửa được**; mọi chuyển có bản ghi lịch sử.

### T-DP-03 – Chỉ Published được dùng cho yêu cầu mới [R1-A] · INT · NOT RUN · Gate: P1-E
- **Pre:** các version ở Draft, Pending, Approved, Published, Disabled. **Chuỗi:** tạo yêu cầu khớp từng process.
- **Trạng thái:** chỉ Published tạo được Order; còn lại bị từ chối (`PROCESS_NOT_PUBLISHED`). **Pass:** không Order nào dùng version không Published.

### T-DP-04 – Sửa process Published không đổi Order đang chạy [R1-A] · INT · NOT RUN
- **Pre:** Order O1 đang chạy trên version v1. **Chuỗi:** tạo v2 (Draft) từ v1, sửa đích, đi qua vòng đời tới Published.
- **Trạng thái:** O1 vẫn dùng snapshot v1 (kèm hash); Order mới dùng v2; v1 → Disabled. **Pass:** `ProcessVersionId`/hash của O1 không đổi; đường đi của O1 theo v1. *(Mở rộng `Cr010ProcessConfigTests.cs:25`.)*

### T-DP-05 – Lịch sử phê duyệt và audit [R1-A] · INT · NOT RUN · Gate: P1-E · BLOCKED(OQ-33 cho quyền)
- **Chuỗi:** soạn, gửi duyệt, từ chối (có lý do), sửa, gửi lại, duyệt, publish.
- **Pass:** bản ghi append-only gồm người, thời điểm, hành động, lý do, kết quả validate/xung đột, content hash; không xóa/sửa; người duyệt ≠ người soạn **nếu** OQ-33 chọn maker-checker.

### T-DP-06 – Các cổng validation (bảng trường hợp) [R1-A] · UNIT/INT · NOT RUN
- **Chuỗi:** gửi duyệt các process lỗi: Station không tồn tại/tắt; Station PROTECTED (A3/A4); gán handshake PLC cho Location WCS-managed (P1); Rack không tương thích; không có ứng viên; vòng lặp; không tới completion; PLC ánh xạ chưa duyệt; năng lực RCS thiếu ánh xạ (**BLOCKED(OQ-36)**); thiếu bước bắt buộc (không xác nhận Drop).
- **Pass:** mỗi trường hợp bị từ chối với **mã lỗi tương ứng** (đã có: `LOCATION_NOT_FOUND/DISABLED/PROTECTED`, `RACK_TYPE_MISMATCH`, `ROUTE_CYCLE`, `NO_PATH_TO_COMPLETION`, `PORT_MAPPING_NOT_APPROVED`…; mới: phân loại D-07, bước bắt buộc, năng lực).

### T-DP-07 – Phát hiện chồng lấn trigger khi duyệt/kích hoạt [R1-A] · INT · NOT RUN · Gate: P1-E · BLOCKED(OQ-34)
- **Pre:** PH2-01 Published (A1/A2, Pickup, Full). **Chuỗi:** gửi duyệt process "A1 → X1" (dữ liệu test) cùng trigger.
- **Pass:** **bị chặn** ở Pending/Approved/Published với lý do chồng lấn trigger, nêu process xung đột; không có hai process Published khớp cùng yêu cầu.

### T-DP-08 – Chốt chặn lúc chạy khi mơ hồ → `NeedsDecision` [R1-A, D-10] · INT · NOT RUN · BLOCKED(OQ-35 cho chủ thể giải quyết)
- **Pre:** (cưỡng bức qua dữ liệu test) hai process Published khớp cùng yêu cầu. **Chuỗi:** yêu cầu PLC mô phỏng.
- **Trạng thái:** Intake Record `NeedsDecision` (D-12; không phải Queue Request chính thức) (giữ bền, **không** ghim, **không** gán latest Version, **không** tạo Order); cảnh báo operator.
- **Pass:** không Order; không tự chọn; không mất yêu cầu; sau giải quyết ghi audit (người, process, Version) – xem T-PIN-C. *(Hành vi hiện tại: `PROCESS_AMBIGUOUS` rồi từ chối.)*

### T-DP-09 – Khởi tạo thủ công bằng mã process tường minh [R1-A] · INT · NOT RUN
- **Chuỗi:** yêu cầu thủ công `ProcessCode=X` tại trạm được phép; và tại trạm không nằm trong trigger. **Pass:** trường hợp 1 tạo Order đúng process; trường hợp 2 bị từ chối (`LOCATION_NOT_ALLOWED`).

### T-DP-10 – Process do người dùng tạo chạy end-to-end (cơ chế) [R1-A] · INT · NOT RUN · Gate: P1-E, P1-F · BLOCKED(OQ-34)
- **Pre:** Station test S-src → S-dst (dữ liệu giả lập, **không** phải X1 thật; OQ-09/29); trigger không chồng lấn hoặc thủ công. **Chuỗi:** soạn → duyệt → Published → yêu cầu → hoàn tất.
- **Trạng thái:** Order Completed khi mọi Step bắt buộc + xác nhận vật lý (D-05). **Rack/Inv:** Current Location = S-dst sau Drop xác nhận. **Pass:** quy tắc Rack/Buffer/Queue giống process mặc định.

### T-DP-11 – Ranh giới cấu hình [R1-A] · UNIT · NOT RUN
- **Chuỗi:** thử cấu hình: script/biểu thức tự do; loại điều kiện không hỗ trợ; gỡ bước bắt buộc; tắt đặt chỗ; hạ yêu cầu bằng chứng Drop; ghi tín hiệu an toàn; retry vượt trần.
- **Pass:** mọi thử nghiệm bị từ chối khi lưu/validate; không có logic tùy ý được thực thi. *(Mở rộng `Cr010ProcessConfigTests.cs:133`.)*

### T-DP-12 – Disable process [R1-A] · INT · NOT RUN
- **Chuỗi:** Disable process khi có Order đang chạy. **Pass:** Order đang chạy hoàn tất bình thường; yêu cầu mới bị từ chối.

### T-DP-13 – Rollback bằng version mới [R1-A] · INT · NOT RUN · Gate: P1-E
- **Chuỗi:** từ v2 tạo v3 sao chép nội dung v1 → vòng đời → Published. **Pass:** v1/v2 không bị sửa; lịch sử đầy đủ; Order cũ không đổi.

## 15. Execution Step (R1-B)

### T-ES-01 – Step sinh theo Process Version [R1-B] · INT · NOT RUN · Gate: P1-F
- **Pre:** hai process khác kế hoạch (A có bước tùy chọn, B không). **Chuỗi:** tạo Order cho mỗi process.
- **Pass:** tập Step của Order = kế hoạch của **version đã ghim**; hai Order khác process có tập Step khác nhau; Step cấp Leg chỉ tạo khi Leg được tạo.

### T-ES-02 – Định danh, trạng thái, thời gian, lịch sử Step [R1-B] · INT · NOT RUN
- **Pass:** mỗi Step có `StepId` duy nhất, `Status`, `CreatedAt/StartedAt/CompletedAt/Duration`, `History[]` **append-only** (từ→đến, thời điểm, tác nhân, lý do); không sửa lịch sử.

### T-ES-03 – Chuyển trạng thái (bảng hợp lệ/cấm) [R1-B] · UNIT · NOT RUN
- **Chuỗi:** áp từng cặp chuyển. **Pass:** theo LIFECYCLE §10.5 – đặc biệt cấm: `Completed→*`, `Skipped→*`, bỏ qua Step bắt buộc, `Completed` khi thiếu bằng chứng.

### T-ES-04 – Blocked không phải lỗi [R1-B, D-04] · INT · NOT RUN
- **Pre:** mọi đích đầy. **Chuỗi:** yêu cầu A1 Full; sau đó Rack rời P1.
- **Trạng thái:** Step chọn đích `Blocked(NO_DESTINATION)` – **không** Failed, **không** tiêu thụ lần thử; sau đánh thức → Waiting → Running. **Queue/Res:** yêu cầu ở lại Queue; Reservation đích chỉ tạo khi có đích. **Pass:** Order `Waiting`, không Failed/Cancelled.

### T-ES-05 – Lịch sử retry và lý do lỗi [R1-B] · INT · NOT RUN
- **Chuỗi:** RCS tạo nhiệm vụ: lần 1 Timeout, lần 2 thành công (cùng `reqCode`). **Pass:** Step có `Attempts[]` 2 lần với kết quả/lý do từng lần, mỗi lần có giao dịch giao tiếp riêng; không tạo Mission trùng.

### T-ES-06 – Ghi PLC mơ hồ không retry tự động [R1-B, R1-C] · INT · NOT RUN
- **Chuỗi:** ghi PLC (mô phỏng): gửi xong, không phản hồi. **Pass:** kết quả `AmbiguousWrite`; **không** retry tự động; Step Blocked/Failed chờ kiểm tra/đối soát; bằng chứng giữ nguyên. *(Khớp `PlcWriteAmbiguousException`.)*

### T-ES-07 – Liên kết tài nguyên của Step [R1-B] · INT · NOT RUN
- **Pass:** Step hiển thị đúng Station, PLC, `StationTransactionId`, `MissionId`/`AgvTaskCode`, `RobotCode`/`AgvId`, Rack, Location, Reservation, QueueRequest liên quan.

### T-ES-08 – Trạng thái Order suy từ Step (bảng) [R1-B, D-05] · INT · NOT RUN · Gate: P1-F
- **Chuỗi/Kỳ vọng:** (a) mọi Step bắt buộc Completed + Drop xác nhận + tồn kho cập nhật → **Completed**; (b) còn 1 Step bắt buộc chưa xong → **không** Completed; (c) RCS Completed nhưng Step "xác nhận Drop" chưa Completed → Order **không** Completed; (d) Step Running → InProgress; (e) Step Blocked không Step Running → Waiting; (f) Step bắt buộc Failed hết retry → Failed; (g) Step bất định vật lý → ReconciliationRequired.
- **Pass:** Order luôn nhất quán với Step (không đặt tay mâu thuẫn).

### T-ES-09 – Skipped và hủy Order [R1-B] · INT · NOT RUN
- **Chuỗi:** Step tùy chọn có điều kiện bỏ qua đúng; hủy Order trước Pick. **Pass:** Step tùy chọn Skipped (có `SkipReason`); Step chưa chạy → Skipped `ORDER_TERMINATED`; Step bắt buộc **không** bị Skipped bởi điều kiện thường.

### T-ES-10 – Người dùng xem mọi Step và lịch sử [R1-B] · E2E · NOT RUN · Gate: P1-F, P4-C
- **Chuỗi:** mở Order trên UI/API. **Pass:** hiển thị toàn bộ Step với trạng thái, mốc thời gian, thời lượng, lần thử, lý do, tài nguyên, lịch sử, Communication History.

### T-ES-11 – Step ≠ API call [R1-B] · INT · NOT RUN
- **Chuỗi:** Step "Dispatch" thực hiện 1 lệnh tạo + nhiều truy vấn trạng thái. **Pass:** **một** Step với nhiều giao dịch giao tiếp; không ép 1–1.

### T-ES-12 – Order cũ không có Step [R1-B] · INT · NOT RUN · BLOCKED(OQ-43)
- Kỳ vọng hiển thị/backfill chưa xác định; không bịa bằng chứng.

## 16. Truy vết giao tiếp mức Step (R1-C)

### T-CT-01 – RCS tạo nhiệm vụ: lưu đủ request/response thô [R1-C] · INT · NOT RUN · Gate: P2-D
- **Chuỗi:** Step Dispatch gọi tạo nhiệm vụ (RCS mô phỏng).
- **Pass:** có Message **Outbound** (payload thô, endpoint, method, `reqCode`) và **Inbound** (payload thô, HTTP status, `code`/`message`, `taskCode`); parsed + diễn giải; `SentAt/ReceivedAt`, `Duration`, `TimeoutMs`; `MessageId` duy nhất; liên kết `StepId`/`LegId`/`OrderId`/`MissionId`.

### T-CT-02 – PLC ghi/đọc: lưu hex thô [R1-C] · INT · NOT RUN · Gate: P2-D
- **Chuỗi:** WCS đọc/ghi một thanh ghi (PLC mô phỏng MC).
- **Pass:** lưu `RequestHex/ResponseHex`, `Device`, `StartAddress`, `Points`, `EndCode`, hướng, `PlcSeq`, giá trị parsed, diễn giải + `InterfaceVersionRef`; gắn Step.

### T-CT-03 – Phân biệt các mức bằng chứng E0–E6 [R1-C, Issue 5] · INT · NOT RUN
- **Chuỗi:** (E0) lệnh chuẩn bị; (E1) gửi thành công nhưng không phản hồi; (E2) nhận phản hồi giao thức; (E3) parse xong; (E4) WCS áp dụng xử lý; (E5) hệ thống ngoài xác nhận nghiệp vụ; (E6) WCS xác nhận hoàn tất vật lý.
- **Pass:** mỗi mức được ghi **riêng** theo thang chuẩn (E0 chuẩn bị · E1 đã gửi · E2 phản hồi giao thức · E3 đã phân tích · E4 WCS áp dụng · E5 ngoài xác nhận nghiệp vụ · E6 vật lý); **không** nhãn "đã nhận" ở E1; **không** suy bậc cao từ bậc thấp; Step chỉ Completed khi đạt mức yêu cầu. *(Chi tiết: T-EV-01…07.)*

### T-CT-04 – Timeout và retry [R1-C] · INT · NOT RUN
- **Chuỗi:** đối tác không phản hồi, nhiều lần thử. **Pass:** mỗi lần thử có giao dịch/message riêng, `TimeoutMs`, kết quả `Timeout`; không ghi đè lần trước.

### T-CT-05 – Lỗi phân tích phản hồi [R1-C] · INT · NOT RUN
- **Chuỗi:** RCS trả JSON sai schema; MC trả frame cụt. **Pass:** `ResponseParseError`; **raw được giữ nguyên**; lỗi + phiên bản parser; Step xử lý theo chính sách (Blocked/Failed) không mất dữ liệu.

### T-CT-06 – Đối tác từ chối [R1-C] · INT · NOT RUN
- **Chuỗi:** MC `EndCode`≠0; RCS `code`≠0/HTTP≥400. **Pass:** `PeerRejected` với raw, mã lỗi, diễn giải.

### T-CT-07 – Callback RCS và Inbound không khớp [R1-C] · INT · NOT RUN
- **Chuỗi:** callback có `taskCode` khớp Leg; callback không khớp. **Pass:** callback khớp gắn đúng Step; callback không khớp lưu `Unmatched` (không bỏ, không đoán).

### T-CT-08 – Quan sát tín hiệu PLC làm đổi Step [R1-C] · INT · NOT RUN
- **Chuỗi:** Step chờ `PLC_ReaddyTranfer`; PLC mô phỏng đổi 0→1. **Pass:** lần đọc phát hiện thay đổi được lưu **đầy đủ** (raw, địa chỉ, trước/sau, thời điểm) gắn Step; polling nền không gắn Step thuộc tầng 2.

### T-CT-09 – Communication History theo thứ tự thời gian và mở từng message [R1-C] · E2E · NOT RUN · Gate: P4-C
- **Pass:** danh sách theo thời gian; mở message xem đủ chi tiết (raw, parsed, diễn giải, lần thử, lỗi, tương quan, danh sách đã che, mức E0–E6).

### T-CT-10 – Ghi bị chặn không tạo frame [R1-C] · INT · NOT RUN
- **Chuỗi:** ghi `AGV_TranferCompleted` (không có địa chỉ cấu hình); ghi bị chính sách chặn. **Pass:** bản ghi `Refused/Blocked` với lý do; **không** frame, không tính "đã gửi"; không suy đoán địa chỉ.

### T-CT-11 – Credential không nằm trong bằng chứng lưu [R1-C/E] · INT · NOT RUN · BLOCKED(OQ-40 cho chính sách at-rest)
- **Chuỗi:** request RCS có header `Authorization`/token. **Pass:** giá trị bị che khi bắt giữ; danh sách che ghi nhận; raw body nghiệp vụ nguyên văn.

### T-CT-12 – Bất biến message [R1-C/D] · INT · NOT RUN
- **Chuỗi:** thử UPDATE/DELETE message qua mọi **đường nghiệp vụ** (API, dịch vụ điều phối) trong thời gian retention. **Pass:** bị từ chối; sửa sai = sự kiện hiệu chỉnh mới; chỉ Retention Service được purge theo policy có audit (T-RT-03).

### T-CT-13 – Ghi phiên bản giao diện dùng để diễn giải [R1-C] · UNIT · NOT RUN · BLOCKED(OQ-03)
- Kỳ vọng phụ thuộc workbook được duyệt; test chỉ kiểm rằng **mọi diễn giải** mang `InterfaceVersionRef`.

## 17. Observability đầu–cuối (R1-D)

### T-OB-01 – Correlation xuyên suốt và ánh xạ ID ngoài [R1-D] · INT · NOT RUN · Gate: P2-D
- **Chuỗi:** một Order hai Leg. **Pass:** một `CorrelationId` nối Order/Leg/Step/Transaction/Message/tồn kho/audit; bảng ánh xạ: `reqCode ↔ AgvRequestCode`, `taskCode ↔ AgvTaskCode`, `robotCode`; PLC không có ID ngoài → `StationTransactionId`/`CommTransactionId` do WCS sinh.

### T-OB-02 – Ghi log không chặn điều khiển [R1-D] · INT/PERF · NOT RUN · Gate: P2-D
- **Chuỗi:** DB log chậm/ngắt (giả lập); thực hiện chu kỳ PLC/RCS. **Pass:** độ trễ đường điều khiển **không tăng đáng kể** (ngưỡng đo được xác định trong CR); không timeout do log.

### T-OB-03 – Độ bền tầng 1 và đếm mất mát tầng 2 [R1-D] · INT · NOT RUN · Gate: P2-D
- **Chuỗi:** ngắt DB giữa chừng rồi phục hồi. **Pass:** **không mất** bằng chứng tầng 1 (spool bền, ghi bù đúng thứ tự); mất mát tầng 2 (nếu có) được **đếm và hiển thị**; không âm thầm.

### T-OB-04 – Bản ghi `Prepared` trước khi gửi và crash [R1-D] · INT · NOT RUN
- **Chuỗi:** tiến trình dừng ngay sau khi gửi, trước phản hồi. **Pass:** sau restart vẫn có bản ghi E0/E1 của message; trạng thái "chưa rõ" được đối chiếu; không lặp lệnh nguy hiểm.

### T-OB-05 – Append-only và retention có dấu vết [R1-D] · INT · NOT RUN · BLOCKED(OQ-39 giá trị)
- **Pass:** không UPDATE/DELETE nội dung bởi đường nghiệp vụ; **Retention Service** (ngoại lệ duy nhất) ghi audit phạm vi/khoảng thời gian/số bản ghi/thời điểm/kết quả/actor; archive trước khi xóa (nếu được duyệt); dữ liệu hold không bị xóa (T-RT-05).

### T-OB-06 – Tìm kiếm [R1-D] · INT · NOT RUN
- **Pass:** tìm theo `OrderId/LegId/StepId/MessageId/TransactionId/MissionId/CorrelationId`, Station/PLC, `RobotCode`, khoảng thời gian, loại/kết quả/mã lỗi, trả kết quả đúng và có phân trang.

### T-OB-07 – Thứ tự sự kiện [R1-D] · INT · NOT RUN · BLOCKED(OQ-41)
- **Pass:** `Seq` đơn điệu theo Order, `PlcSeq` đơn điệu theo PLC; dựng lại xen kẽ nhiều Station cùng PLC đúng thứ tự.

### T-OB-08 – Bản ghi quyết định Queue/FIFO/Reservation/chọn Rack [R1-D] · INT · NOT RUN · Gate: P2-A, P2-B
- **Chuỗi:** V4/V5/V7 (RACK doc §10). **Pass:** `RackSelection` có ứng viên theo thứ tự, lý do loại, khóa FIFO, kết quả; `QueueActivated` liệt kê yêu cầu đứng trước/sau; tái hiện được từ dữ liệu lưu.

### T-OB-09 – Lỗi dịch vụ nền gắn Order [R1-D] · INT · NOT RUN
- **Chuỗi:** ép lỗi trong `RoutingHostedService` khi xử lý Order. **Pass:** exception ghi kèm `OrderId/CorrelationId`; vòng lặp tiếp tục.

### T-OB-10 – Hành động người dùng và lịch sử phê duyệt liên kết [R1-D] · INT · NOT RUN
- **Pass:** `audit_log`/lịch sử phê duyệt liên kết `ProcessVersionId`, `CorrelationId`, người, thời điểm.

## 18. Gói chẩn đoán (R1-E)

### T-DG-01 – Xuất theo Order đủ nội dung [R1-E] · INT · NOT RUN · Gate: P4-C
- **Pre:** Order có sự cố (RCS Completed nhưng thiếu Drop). **Chuỗi:** xuất theo Order.
- **Pass:** gói có: tóm tắt Order/Leg, **Process Version chính xác + hash**, timeline Step, giao tiếp thô PLC/RCS (parsed + diễn giải), lịch sử AGV, quyết định Queue/Reservation, lịch sử tồn kho, lỗi hệ thống, snapshot cấu hình, bản đồ ID, `Manifest.json`.

### T-DG-02 – Chọn theo Leg/Step/khoảng thời gian [R1-E] · INT · NOT RUN
- **Pass:** phạm vi đúng; Manifest ghi phạm vi, thời điểm, phiên bản exporter, hash tệp.

### T-DG-03 – Sanitize [R1-E] · INT · NOT RUN
- **Pre:** cài sẵn bí mật thử (password, token, `Authorization`, connection string, cookie) trong cấu hình, log, raw. **Pass:** **không** bí mật nào xuất hiện trong gói; `RedactionReport.json` liệt kê vị trí (không giá trị); raw không chứa bí mật được giữ nguyên.

### T-DG-04 – Giữ thứ tự và quan hệ [R1-E] · INT · NOT RUN
- **Pass:** `Timeline.ndjson` sắp theo `Seq/At`; `IdMap.json` nhất quán; một script độc lập dựng lại chuỗi sự cố từ gói **không cần** truy cập hệ thống.

### T-DG-05 – Báo cáo đầy đủ/cắt bớt [R1-E] · INT · NOT RUN
- **Chuỗi:** nguồn không đọc được; vượt giới hạn kích thước. **Pass:** Manifest đánh dấu **Partial/Failed** theo nguồn; cắt ở đầu cũ có báo cáo; không im lặng (như CR-021).

### T-DG-06 – Phân quyền và audit xuất [R1-E] · INT · NOT RUN · BLOCKED(OQ-42)
- **Pass:** Admin xuất được; Operator bị từ chối; hành động xuất được ghi `audit_log` kèm phạm vi.

### T-DG-07 – Chỉ đọc, giới hạn và dọn gói [R1-E] · INT · NOT RUN
- **Pass:** xuất **không** thay đổi dữ liệu; job bất đồng bộ không chặn hệ thống; gói hoàn tất bị xóa sau thời gian giữ (CR-021: 24 h) – giá trị chính thức OQ-42.

### T-DG-08 – Dựng lại sự cố "RCS Completed chưa có Drop" từ gói [R1-E, D-05] · INT · NOT RUN
- **Pass:** từ gói chứng minh được: RCS báo Completed (E5), Step xác nhận Drop **chưa** Completed, Order **không** Completed, Reservation giữ, không đổi `Current Location`; thứ tự và thời điểm đúng.

## 19. Ma trận phủ bổ sung

| Yêu cầu | Test |
|---|---|
| R1-A | T-DP-01…13 |
| R1-B | T-ES-01…12 |
| R1-C | T-CT-01…13, T-ES-05/06 |
| R1-D | T-OB-01…10 |
| R1-E | T-DG-01…08 |

## 20. Revision 1 – thay đổi trong file này

Thêm: dòng Revision (đầu file); §14–§19 (test mới R1-A…R1-E); §13 được cập nhật tổng số. **Không đổi** T-LC… T-LG (49 test đầu) và quy ước "mọi test NOT RUN". Test **không** được ghi Passed.
---

# PHẦN BỔ SUNG REVISION 1.1 – PH2-04 (D-09), ghim Version (D-10), PLC permission, retention, bằng chứng bền vững, thang E0–E6

> Quy ước không đổi: **mọi test `NOT RUN`**; **BLOCKED(OQ-xx)** = kỳ vọng chưa xác định vì câu hỏi mở; `Gate` = cần CR triển khai trước. Trường: **Pre** · **Chuỗi** · **Trạng thái** · **Rack/Inv** · **Queue/Res** · **Pass**. Dữ liệu test (Station, Location, process, Version) là **giả lập**, không phải cấu hình sản xuất; không dùng địa chỉ PLC thật; không thêm địa chỉ/tín hiệu.

## 21. PH2-04 – baseline đã chấp thuận (D-09)

> Thay thế kỳ vọng cũ `PROCESS_REVIEW_BLOCKED` của PH2-04. **Không** có test nào ở đây kỳ vọng PH2-04 bị chặn chỉ vì REVIEW. Cơ chế `ReviewBlocked` chung vẫn được kiểm bằng một process thử khác (T-P4-02).

### T-P4-01 – PH2-04 tham gia vòng đời process thông thường [D-09, R1-A, D-11] · INT · NOT RUN · Gate: P1-E, P1-G · BLOCKED(OQ-32 cho người duyệt gói mặc định)
- **Pre:** dữ liệu tham chiếu PH2-04 đã gỡ `reviewBlocked` (CR P1-G); Location P1/P2 và ánh xạ IO test có sẵn. **Chuỗi:** Draft → validate → Pending Approval → Approved → Published.
- **Trạng thái:** mọi chuyển thành công; **không** phát sinh `PROCESS_REVIEW_BLOCKED`; PH2-04 nằm trong gói mặc định cùng PH2-01/02/03/05/06. **Rack/Inv, Queue/Res:** không đổi.
- **Pass:** lịch sử phê duyệt đầy đủ; PH2-04 không có ngoại lệ vòng đời so với process khác.

### T-P4-02 – Không còn cờ REVIEW trên PH2-04; cơ chế REVIEW chung vẫn hoạt động [D-09] · UNIT/INT · NOT RUN · Gate: P1-G
- **Chuỗi:** (a) kiểm dữ liệu PH2-04: `reviewBlocked=false`; (b) validate PH2-04 với Location/ánh xạ test hợp lệ; (c) tạo một process thử khác gắn cờ REVIEW rồi validate/publish.
- **Pass:** (a)(b) không có `PROCESS_REVIEW_BLOCKED`; (c) vẫn bị chặn (cơ chế chung KEEP). **Test hiện có** `Cr010ProcessConfigTests.cs:183-184` (khẳng định `PH2-04 ReviewBlocked`) phải được cập nhật bằng CR, không sửa lặng lẽ.

### T-P4-03 – P1/P2 chỉ là nguồn thay thế [D-09] · INT · NOT RUN · Gate: P2-A · BLOCKED(OQ-16, OQ-30)
- **Pre:** A1/A2 không có Full Rack phù hợp; P1 có `F1`(Full, Origin phù hợp, t1), `F2`(Full, Origin phù hợp, t2). **Chuỗi:** B1 yêu cầu Full.
- **Trạng thái:** chọn nguồn P1 → `F1` (Origin phù hợp + FIFO). Pick tại P1 (thủ công, E6 có audit) → Drop tại B1 (PLC). **Pass:** không chọn nguồn nào ngoài A1/A2/P1/P2.

### T-P4-04 – Không bao giờ chọn Empty Rack cho yêu cầu Full [D-09] · INT · NOT RUN
- **Pre:** A1/A2 và P1/P2 **chỉ** có Empty Rack (và/hoặc ô trống). **Chuỗi:** B1 yêu cầu Full. **Pass:** không Rack Empty nào được Reservation/Pick; không Order dùng Empty cho Full; yêu cầu vào nhánh chờ. *(Kiểm rằng nhãn nguồn "có vị trí trống?" **không** biến điều kiện thành "có slot trống" – điều kiện là `RackPresent(Full)`.)*

### T-P4-05 – Không chọn Rack sai Origin [D-02, D-09] · INT · NOT RUN · BLOCKED(OQ-16)
- **Pre:** P1 có Full Rack chỉ với Origin không phù hợp; A1/A2 không có. **Pass:** không chọn Rack đó; yêu cầu **chờ** (không báo lỗi); khi Rack Origin phù hợp được Drop vào P hoặc A1/A2 có Full → được xử lý.

### T-P4-06 – Không có nguồn phù hợp: nhánh chờ, không tự chọn nguồn khác [D-09, D-04] · INT · NOT RUN · BLOCKED(OQ-14, OQ-18 cho vị trí chờ)
- **Pre:** không có Full Rack phù hợp ở A1/A2 và P1/P2. **Chuỗi:** B1 yêu cầu Full; sau đó một Full Rack phù hợp xuất hiện ở P2.
- **Trạng thái:** Step chọn nguồn `Blocked(NO_SOURCE)` (không Failed); vòng kiểm tra lại **A1/A2 → P1/P2** (đúng slide 7: NG của P nối ngược về A); Order `Waiting`, không Cancelled/Failed. **Queue/Res:** yêu cầu ở Queue (đã ghim Version); không Reservation Rack.
- **Pass:** sự kiện đánh thức → chọn P2; **không** chọn nguồn chưa được phê duyệt trong lúc chờ.

### T-P4-07 – Operational Readiness độc lập với business approval [D-09, D-11] · INT · NOT RUN · Gate: P1-E
- **Pre:** PH2-04 đã Approved và **Published** (qua Publication Gate); **chưa** có Location P1/P2 xác minh hoặc ánh xạ IO PLC A1/A2/B1/B2 chưa duyệt. **Chuỗi:** kiểm tra báo cáo trạng thái và gửi yêu cầu B1.
- **Trạng thái:** báo cáo hiển thị **hai trục riêng**: Publication = `Published`, Readiness = `NotReady` (không gộp); hệ thống **không** chuyển PH2-04 về REVIEW; yêu cầu **không** vào Queue chính thức (Intake Record lý do `NOT_READY`, lý do thiếu Location/mapping rõ ràng). **Pass:** không trạng thái nào đại diện cho trạng thái khác; Publication Gate không bị bỏ qua; Readiness Gate không bị bỏ qua.

### T-P4-08 – Trigger conflict với BP-0x/PH2-0x [D-09, R1-A] · INT · NOT RUN · Gate: P1-E · BLOCKED(OQ-28, OQ-46)
- **Pre:** một process khác Published cùng trigger (B1/B2, Delivery, Full). **Chuỗi:** gửi duyệt/kích hoạt PH2-04. **Pass:** bị chặn bởi **xung đột trigger** (lý do nêu process xung đột), **không** bởi REVIEW; khi không chồng lấn thì qua gate này.

### T-P4-09 – Cổng ánh xạ IO PLC đúng chỗ [D-09, D-11] · INT · NOT RUN · BLOCKED(OQ-03)
- **Pre:** ánh xạ IO A1/A2, B1/B2 chưa duyệt. **Pass:** `PORT_MAPPING_NOT_APPROVED` được hiển thị là điều kiện **Readiness Gate** (Readiness = NotReady), **không** chặn Publication; Publication Status và business approval không đổi.

### T-P4-10 – Tranh chấp cùng một Full Rack với PH2-01 [D-06, D-13] · INT · NOT RUN · BLOCKED(OQ-54)
- **Pre:** Full Rack `F7` tại A2: A2 báo đi ra (PH2-01) và B1 yêu cầu (PH2-04) cùng lúc. **Pass:** chỉ **một** Reservation Rack ACTIVE (unique); yêu cầu còn lại chọn nguồn/đích khác theo quy tắc process của nó hoặc chờ; thứ tự theo `QueueSequence` **nếu cùng Scope** (strict FIFO – D-13; Scope: OQ-54).

### T-P4-11 – Hoàn tất PH2-04 theo D-05 [D-09, D-05] · INT · NOT RUN
- **Pass:** Leg Completed chỉ sau Drop tại B1/B2 được xác nhận (E6) và tồn kho cập nhật; RCS Completed hoặc `PLC_MissionCompleted` đơn lẻ không đủ.

### T-P4-12 – PH2-04 dùng Process Version đã ghim [D-09, D-10] · INT · NOT RUN · Gate: P1-H
- **Pre:** PH2-04 v1 Published; yêu cầu B1 vào Queue (ghim v1); v2 Published khi đang chờ. **Pass:** Order dùng v1; nguồn/đích theo v1.

## 22. Ghim Process Version tại Queue intake (D-10)

> **Luồng:** Request Intake → Process Selection → **Check Publication + Readiness** → Process Version Pinning → Queue Waiting → Queue Dispatch → Transport Order Creation (D-10, D-12).

### T-PIN-A – Version không đổi [D-10] · INT · NOT RUN · Gate: P1-H
- **Pre:** process P có version V1 Published. **Chuỗi:** tiếp nhận Queue Request → chọn P → **ghim V1** → dispatch → tạo Order.
- **Trạng thái:** Intake Record `Received` → (Published + Ready) → Queue Request `Waiting` → … → Order được tạo (ranh giới Prepared/Dispatched: [ĐX], OQ-49); Order Created → Accepted. **Rack/Inv:** không đổi trước dispatch.
- **Pass:** Queue Request ghi V1 (id, số, hash, thời điểm); **Transport Order dùng V1** (snapshot = V1).

### T-PIN-B – Publish Version mới khi Request đang chờ [D-10] · INT · NOT RUN · Gate: P1-H
- **Chuỗi:** Request ghim V1 (đang `Waiting`); V2 được Publish; Request tới lượt dispatch. **Pass:** Request **vẫn** dùng V1; Order snapshot = V1; Request **không tự chuyển** sang V2; yêu cầu **mới** sau khi V2 Published ghim V2.

### T-PIN-C – Process chưa xác định → `NeedsDecision` [D-10] · INT · NOT RUN · Gate: P1-H · BLOCKED(OQ-35 cho chủ thể giải quyết)
- **Pre:** hai process Published khớp cùng yêu cầu (cưỡng bức bằng dữ liệu test). **Chuỗi:** tiếp nhận yêu cầu; sau đó operator giải quyết.
- **Trạng thái:** Intake Record `NeedsDecision` – **không ghim**, **không** gán latest, **không** tạo Order; cảnh báo operator. Sau quyết định: **kiểm tra lại Published + Ready** (D-12) → ghi **audit** (người, thời điểm, process chọn, Version dùng, lý do) → ghim → Queue Request `Waiting` (nếu không còn Published + Ready: không ghim, xem T-QI-05).
- **Pass:** không bước nào dùng latest Version; audit trail có quyết định và Version.

### T-PIN-D – Truy vết [D-10] · INT · NOT RUN · Gate: P1-H
- **Pass:** từ Queue Request truy được `ProcessId`/`ProcessVersionId`/hash; từ Order và Leg truy ngược `QueueRequestId` và Version đã ghim; các giá trị khớp nhau xuyên suốt.

### T-PIN-E – Intake trùng không ghim lại [D-10] · INT · NOT RUN
- **Chuỗi:** cùng yêu cầu (cùng khóa chống trùng) tới hai lần, V2 được Publish giữa hai lần. **Pass:** một Queue Request; Version giữ V1 (không đổi theo lần tới sau).

### T-PIN-F – Version đã ghim bị Disabled trước dispatch [D-10] · INT · NOT RUN · BLOCKED(OQ-45)
- Kỳ vọng (dispatch với V1 kèm kiểm lại điều kiện, hay về `NeedsDecision`) **chưa xác định**; không viết kết quả.

### T-PIN-G – Ghim được giữ qua restart WCS [D-10, D-03] · INT · NOT RUN · Gate: P1-H, P2-B
- **Chuỗi:** Request `Waiting` (ghim V1) → restart WCS → dispatch. **Pass:** Version V1 còn nguyên; thứ tự Queue nguyên; không ghim lại.

### T-PIN-H – Khởi tạo thủ công bằng mã process cũng ghim tại intake [D-10] · INT · NOT RUN
- **Pass:** yêu cầu thủ công chỉ định `ProcessCode` được ghim Version Published **tại intake** (`PinSource` = mã tường minh).

### T-PIN-I – Snapshot Order khớp hash Version đã ghim [D-10] · UNIT · NOT RUN
- **Pass:** `content hash` trong snapshot Order = hash Version đã ghim trên Queue Request.

## 23. Diễn giải `PLC_ReaddyTranfer` (Issue 2)

### T-RD-01 – `PLC_ReaddyTranfer = 1` là permission Pick [Issue 2] · INT · NOT RUN · BLOCKED(OQ-03 cho bản workbook)
- **Pre:** Step "Chờ permission Pick" `Running/Blocked`. **Chuỗi:** PLC mô phỏng đặt giá trị 1.
- **Trạng thái:** Step "Chờ permission" → Completed ở **E4** (permission đã ghi nhận); Step "Xác nhận Pick vật lý" **vẫn Waiting**. **Rack/Inv:** **không đổi** (Current Location giữ nguyên). **Pass:** không có trạng thái/nhãn "cửa/kẹp đã mở" nào được ghi từ tín hiệu này.

### T-RD-02 – `PLC_ReaddyTranfer = 2` là permission Drop [Issue 2] · INT · NOT RUN · BLOCKED(OQ-03)
- **Pass:** tương tự T-RD-01 cho Drop; không suy ra "vị trí đích đã trống"; Current Location không đổi.

### T-RD-03 – Permission không khớp thao tác [Issue 2] · INT · NOT RUN · BLOCKED(OQ-38)
- **Chuỗi:** Step đang chờ permission Drop nhưng PLC báo 1 (Pick). **Pass:** **không** coi là permission Drop; ghi nhận bất khớp; xử lý (Blocked/Failed) theo chính sách chưa duyệt.

### T-RD-04 – `PLC_ReaddyTranfer = 10` (lỗi) [Issue 2] · INT · NOT RUN · BLOCKED(OQ-38)
- **Pass:** permission **không** được cấp; Step không Completed; bằng chứng lưu; hành vi tiếp theo theo chính sách chưa duyệt.

### T-RD-05 – Không suy trạng thái vật lý; không thêm địa chỉ/tín hiệu [Issue 2] · UNIT/DOC · NOT RUN · BLOCKED(OQ-48)
- **Pass:** rà code/cấu hình/giao diện: không có nhãn "cửa mở/kẹp mở" suy từ `PLC_ReaddyTranfer`; không địa chỉ/tín hiệu PLC mới được tạo; nếu cần xác nhận cửa/kẹp thì chỉ khi có tín hiệu riêng do PLC engineer xác nhận.

### T-RD-06 – Diễn giải ghi kèm phiên bản giao diện [Issue 2] · UNIT · NOT RUN · BLOCKED(OQ-03)
- **Pass:** mọi diễn giải giá trị mang `InterfaceVersionRef`; HEAD (BOOL 0/1) và working tree (INT 1/2/10) không bị lẫn.

## 24. Append-only và Retention Service (Issue 3)

### T-RT-01 – Đường nghiệp vụ không sửa/xóa bằng chứng [Issue 3] · INT · NOT RUN
- **Chuỗi:** thử UPDATE/DELETE sự kiện/message/Step history qua API và dịch vụ điều phối trong thời gian retention. **Pass:** bị từ chối; dữ liệu nguyên vẹn.

### T-RT-02 – Hiệu chỉnh tạo sự kiện mới [Issue 3] · INT · NOT RUN
- **Chuỗi:** sửa kết luận (ví dụ `OutcomeResolved` cho `Unknown outcome`). **Pass:** sự kiện **mới** liên kết sự kiện cũ; bản gốc không đổi.

### T-RT-03 – Retention Service purge theo policy và có audit [Issue 3] · INT · NOT RUN · BLOCKED(OQ-39 cho giá trị)
- **Pre:** policy giả lập (dữ liệu test). **Chuỗi:** chạy Retention Service.
- **Pass:** chỉ bản ghi quá hạn và **không bị hold** bị xóa/archive; audit có đủ **phạm vi, khoảng thời gian, số bản ghi, thời điểm, kết quả, actor/nguồn**; số bản ghi trong audit = số thực bị xóa.

### T-RT-04 – Không có policy được duyệt thì không purge [Issue 3] · INT · NOT RUN
- **Pass:** Retention Service không xóa bằng chứng; cơ chế system log 90 ngày hiện hữu không đổi.

### T-RT-05 – Bảo vệ sự cố/đối soát/điều tra chưa đóng [Issue 3] · INT · NOT RUN
- **Pre:** Order `ReconciliationRequired`/giao dịch `Suspended`/gói chẩn đoán đang xuất/cờ điều tra, dữ liệu quá hạn. **Pass:** **không** bị purge; sau khi đóng sự cố → được purge ở lần chạy sau; cờ hold có người/lý do/thời điểm.

### T-RT-06 – Purge thất bại/một phần [Issue 3] · INT · NOT RUN
- **Pass:** audit ghi `Partial/Failed` kèm nguyên nhân; không im lặng; chạy lại an toàn (idempotent); không xóa dở dang ngoài phạm vi audit.

### T-RT-07 – Archive trước purge (nếu policy yêu cầu) [Issue 3] · INT · NOT RUN · BLOCKED(OQ-39)
- **Pass:** purge chỉ sau khi archive được xác minh (số lượng/checksum).

### T-RT-08 – Audit purge được bảo vệ [Issue 3] · INT · NOT RUN
- **Pass:** bản ghi audit purge không bị sửa/xóa bởi đường nghiệp vụ và không bị purge cùng dữ liệu nó mô tả.

## 25. Bằng chứng bền vững và recovery (Issue 4)

> Không test nào xác nhận "zero-loss". Mỗi test kiểm hành vi đã nêu và **giới hạn**; mức bảo đảm: OQ-47.

### T-DU-01 – Database không sẵn sàng [Issue 4] · INT · NOT RUN · Gate: P2-D · BLOCKED(OQ-47 cho dung lượng spool)
- **Chuỗi:** ngắt DB; chạy giao tiếp PLC/RCS; phục hồi DB. **Pass:** độ trễ đường điều khiển **không tăng đáng kể**; bằng chứng tầng 1 vào spool theo thứ tự; tầng 2 hạ cấp có **đếm mất mát**; sau phục hồi ghi bù + đối chiếu (Reconciled), không trùng.

### T-DU-02 – Spool/outbox không ghi được [Issue 4] · INT · NOT RUN · BLOCKED(OQ-47)
- **Chuỗi:** ép lỗi ghi spool. **Pass:** hệ thống **không giả định** đã ghi bền; ghi nhận "evidence gap"; cảnh báo; lệnh ghi PLC mới theo chính sách (chặn/cho phép – chưa duyệt) – test chỉ kiểm rằng **không im lặng**.

### T-DU-03 – Đĩa đầy [Issue 4] · INT · NOT RUN · BLOCKED(OQ-47)
- **Pass:** không xóa bằng chứng tầng 1 để lấy chỗ; ưu tiên giải phóng tầng 2/spool đã Persisted; cảnh báo; phục hồi khi có chỗ.

### T-DU-04 – Crash sau Prepared, trước Sent (PLC write) [Issue 4] · INT · NOT RUN
- **Chuỗi:** dừng tiến trình ngay sau khi Prepared flush. **Pass:** sau restart giao dịch ở `Unknown outcome` (không biết đã gửi hay chưa); **không retry tự động**; Step Blocked `UNKNOWN_OUTCOME`; yêu cầu đọc-lại/đối soát.

### T-DU-05 – Crash sau Sent, trước Response (PLC write) [Issue 4] · INT · NOT RUN
- **Pass:** như T-DU-04; bằng chứng E1 còn; **không** coi là đã thực thi; **không** retry.

### T-DU-06 – Crash sau Response, trước Persist [Issue 4] · INT · NOT RUN · BLOCKED(OQ-47)
- **Pass:** nếu response đã Spooled → ghi bền và áp dụng; nếu chưa → `Unknown outcome`; không tuyên bố đã nhận.

### T-DU-07 – Evidence persist xong, control chưa cập nhật Step [Issue 4] · INT · NOT RUN
- **Pass:** recovery **áp dụng lại idempotent** theo `MessageId`/`CommandId`; Step chuyển E4 **đúng một lần**; không chuyển trạng thái hai lần.

### T-DU-08 – RCS create-task kết quả không xác định [Issue 4] · INT · NOT RUN
- **Chuỗi:** crash sau Sent. **Pass:** restart query theo `reqCode`: nếu RCS đã có nhiệm vụ → tiếp tục, **không tạo trùng**; nếu không có → gửi lại idempotent; nếu **không query được** → Blocked `UNKNOWN_OUTCOME`, giữ Leg.

### T-DU-09 – Có response nhưng chưa persist được Evidence [Issue 4] · INT · NOT RUN · BLOCKED(OQ-47)
- **Pass:** Step **không** chuyển lên E4 trước khi bằng chứng đạt mức ghi bền (hoặc đánh dấu `evidence-pending`); không mất âm thầm.

### T-DU-10 – Enqueue trong bộ nhớ ≠ ghi bền [Issue 4] · UNIT/INT · NOT RUN
- **Pass:** mục chỉ Enqueued **không** được báo là durable; trạng thái durable chỉ đặt khi có ack Spooled/Persisted; health hiển thị `enqueued/spooled/persisted/lost`.

### T-DU-11 – Không retry PLC write khi kết quả trước chưa xác định [Issue 4, INV-28] · INT · NOT RUN
- **Pre:** bật retry tối đa trong cấu hình thử. **Pass:** với `Unknown outcome` **không** có lần gửi lại tự động; chỉ có đọc-lại có kiểm soát; kết quả giải quyết là sự kiện mới.

### T-DU-12 – Tương quan Command ↔ Transaction ↔ Evidence [Issue 4, Issue 5] · INT · NOT RUN
- **Pass:** ở mọi kịch bản T-DU-04…09: `CommandId` ↔ `CommTransactionId` ↔ `MessageId`/`EvidenceId` ↔ `StepId`/Leg/`StationTransactionId` ↔ tham chiếu ngoài đều truy được (hoặc null có chủ đích).

### T-DU-13 – Không tuyên bố zero-loss [Issue 4] · DOC · NOT RUN
- **Pass:** rà tài liệu/UI/health: không câu nào tuyên bố mất mát bằng không; giới hạn (cửa sổ Enqueued→Spooled, độ trễ Prepared đồng bộ, hỏng media) được nêu; điểm thiếu cơ sở ghi OPEN (OQ-47).

## 26. Ngữ nghĩa thang bằng chứng E0–E6 (Issue 5)

### T-EV-01 – HTTP 200/TCP response không phải RCS chấp nhận nghiệp vụ [Issue 5] · INT · NOT RUN
- **Chuỗi:** RCS mô phỏng trả HTTP 200 với body `code` lỗi/rỗng. **Pass:** bằng chứng dừng ở **E2/E3**; không E5; Step "Dispatch" **không** Completed.

### T-EV-02 – Phản hồi giao thức và kết quả nghiệp vụ RCS tách biệt [Issue 5] · INT · NOT RUN
- **Chuỗi:** HTTP 200 + `code` thành công + `taskCode` → sau đó query/callback trạng thái nhiệm vụ. **Pass:** E2/E3/E4 ghi ở lần gọi; **E5** chỉ ghi khi có xác nhận nghiệp vụ (trạng thái nhiệm vụ từ query/callback); hai bản ghi riêng.

### T-EV-03 – PLC permission không phải bàn giao vật lý [Issue 5, Issue 2] · INT · NOT RUN
- **Chuỗi:** `PLC_ReaddyTranfer` hợp lệ và `PLC_MissionCompleted` thành công nhưng **không** có presence/identity/PDA. **Pass:** Step "Xác nhận Pick/Drop vật lý" **không** Completed; không có E6; `Current Location` không đổi. *(Mở rộng T-PL-03.)*

### T-EV-04 – RCS Completed không chứng minh bàn giao [Issue 5, D-05] · INT · NOT RUN
- **Pass:** như T-RC-02: ghi E5 nhưng **không** E6; Leg không Completed; không đổi Current Location.

### T-EV-05 – Không suy bậc cao từ bậc thấp [Issue 5] · UNIT · NOT RUN
- **Chuỗi:** các tổ hợp bản ghi (chỉ E1; E2 không parse; E3 không áp dụng; E5 không E6). **Pass:** mức bằng chứng của Step = tập các mức **có bản ghi riêng**; không có mức nào được ngầm cộng thêm.

### T-EV-06 – Tương quan bằng chứng [Issue 5] · INT · NOT RUN
- **Pass:** mỗi bản ghi bằng chứng mang Job, Leg, Station Transaction, Command và tham chiếu ngoài (nếu có; nếu không → null có chủ đích).

### T-EV-07 – Nguồn E6 được ghi rõ, không giả lập [Issue 5] · INT · NOT RUN · BLOCKED(OQ-12)
- **Pass:** E6 luôn ghi **nguồn** (PDA/scan/PLC presence…); không cho E6 với nguồn rỗng; không khẳng định cảm biến chưa được xác nhận.

## 27. Ma trận phủ Revision 1.1 và tổng hợp

| Yêu cầu | Test |
|---|---|
| D-09 (PH2-04) | T-FL-05, T-P4-01…12 |
| D-10 (ghim Version) | T-PIN-A…I, T-P4-12, T-DP-04, T-DP-08 |
| Issue 1 | T-P4-01, T-P4-02, T-P4-07, T-P4-08, T-P4-09, T-DP-01 |
| Issue 2 | T-RD-01…06 |
| Issue 3 | T-RT-01…08, T-CT-12, T-OB-05 |
| Issue 4 | T-DU-01…13, T-OB-02/03/04 |
| Issue 5 | T-EV-01…07, T-CT-03, T-CT-01 |
| Issue 6 | T-PIN-A…I |

## 28. Revision 1.1 – thay đổi trong file này

| Mục | Thay đổi | Issue/Decision |
|---|---|---|
| T-FL-05 | **Thay** kỳ vọng `PROCESS_REVIEW_BLOCKED` bằng "A1/A2 là nguồn ưu tiên của PH2-04" | Decision 1 |
| T-LG-03 | Giới hạn ở BP-04 **Phase 1** (ngoài D-09); không dùng cho PH2-04 | Decision 1; OQ-46 |
| T-DP-01 | Gói mặc định gồm PH2-04; BLOCKED(OQ-32, OQ-44) thay OQ-31 | Issue 1 |
| T-DP-08 | `NeedsDecision` theo D-10 | Decision 2 |
| T-CT-03, T-CT-12, T-OB-05 | Thang E0–E6 chuẩn; append-only trừ Retention Service | Issue 3, 5 |
| §21–§27 mới | T-P4 (12), T-PIN (9), T-RD (6), T-RT (8), T-DU (13), T-EV (7) | Tất cả |
| Giữ nguyên | Mọi test còn lại và ID; **mọi test NOT RUN**, không test nào Passed | — |
---
# PHẦN BỔ SUNG REVISION 1.2 – Two-Gate, Queue Intake, Held, Strict FIFO theo Scope (D-11, D-12, D-13)

> Quy ước không đổi: **mọi test `NOT RUN`**; **BLOCKED(OQ-xx)** = kỳ vọng chưa xác định vì câu hỏi mở (không đoán kết quả); `Gate` = cần CR triển khai trước (Spec §13: P1-E, P1-H, P1-I, P2-B, P2-E). Trường: **Pre** · **Chuỗi** · **Trạng thái** · **Pass**. Dữ liệu test (Station, Location, process, Version, Scope) là **giả lập**, không phải cấu hình sản xuất; không địa chỉ PLC thật; không thêm địa chỉ/tín hiệu. Trạng thái Queue theo `PHASE2_TRANSPORT_JOB_LIFECYCLE.md` §2.5; **ranh giới Prepared/Dispatched là đề xuất [ĐX] (OQ-49)** nên test chỉ kiểm các trạng thái đã quyết (Waiting, Held, NeedsDecision) và không khẳng định điều kiện chuyển Prepared/Dispatched ngoài Lifecycle §2.5.2.

## 29. Publication Gate (D-11)

### T-PG-01 – Process hợp lệ qua Publication Gate → Published dù chưa Ready [D-11] · INT · NOT RUN · Gate: P1-E
- **Pre:** process đã Approved; Location/Station/Rack Type/Step tham chiếu tồn tại và hợp lệ; không xung đột trigger; **chưa** có ánh xạ IO PLC được duyệt, **chưa** commissioning. **Chuỗi:** Approved → Published.
- **Trạng thái:** Publication Status = `Published`; Readiness = `NotReady`. **Pass:** Publish thành công; không đòi PLC/RCS/commissioning; Version có `ProcessVersionId` và hash.

### T-PG-02 – Publication Gate không dùng điều kiện PLC/RCS/vật lý [D-11] · INT · NOT RUN · Gate: P1-E
- **Pre:** `PORT_MAPPING_NOT_APPROVED`, `PORT_DISABLED`, `PLC_DISABLED` đang xảy ra. **Pass:** các mã này **không** chặn Publish (chúng thuộc Readiness Gate – Spec §15.2b); chúng chặn Ready.

### T-PG-03 – Tham chiếu không tồn tại/không hợp lệ chặn Publish [D-11] · UNIT/INT · NOT RUN · Gate: P1-E
- **Chuỗi:** (a) Location không tồn tại (`LOCATION_NOT_FOUND`); (b) Location PROTECTED; (c) Rack Type không khớp; (d) đích không phải Storage; (e) Step tham chiếu không tồn tại. **Pass:** mỗi trường hợp **không Published**, mã lỗi tương ứng; **không** tạo ngoại lệ cho PH2-04.

### T-PG-04 – Lỗi cấu trúc chặn Publish [D-11] · UNIT · NOT RUN · Gate: P1-E
- **Chuỗi:** `NO_STEPS`, `FIRST_STEP_MISSING`, `ROUTE_CYCLE`, `NO_PATH_TO_COMPLETION`, `DUPLICATE_*`, `SAME_ENDS`. **Pass:** không Published; lý do hiển thị.

### T-PG-05 – Xung đột nghiệp vụ chưa giải quyết chặn Publish [D-11] · INT · NOT RUN · Gate: P1-E · BLOCKED(OQ-28)
- **Pre:** process khác Published cùng trigger. **Pass:** bị chặn bởi xung đột trigger (lý do nêu process xung đột); điều kiện “xung đột nghiệp vụ chưa giải quyết” gồm những gì khác ngoài trigger: **chưa xác định (OQ-28)** – không viết thêm kỳ vọng.

### T-PG-06 – Không có phê duyệt nghiệp vụ → không Published [D-11] · INT · NOT RUN · Gate: P1-E
- **Chuỗi:** Draft/Pending Approval → Published trực tiếp. **Pass:** bị từ chối (đường chuyển cấm); chỉ `Approved → Published`.

### T-PG-07 – Version được định danh khi Published [D-11, D-10] · UNIT · NOT RUN · Gate: P1-E
- **Pass:** Published ghi `ProcessVersionId` + số version + content hash; Version không sửa tại chỗ.

### T-PG-08 – Published không tự tạo Ready [D-11] · INT · NOT RUN · Gate: P1-E
- **Pass:** ngay sau Publish, Readiness = `NotReady` (mặc định, [ĐX] – xem OQ-53 về giá trị khởi tạo); không Request nào được nhận.

### T-PG-09 – Thay đổi Readiness không đổi Publication Status [D-11] · INT · NOT RUN · Gate: P1-E
- **Chuỗi:** Published + Ready → Suspended → Ready. **Pass:** Publication Status luôn `Published`; không có trạng thái gộp; mỗi thay đổi Readiness có audit riêng.

### T-PG-10 – PH2-04 không REVIEW và BP-04 Phase 1 không mở khóa [D-09, D-11] · INT · NOT RUN · Gate: P1-G · BLOCKED(OQ-46)
- **Pass:** PH2-04 qua Publication Gate như process thường (không `PROCESS_REVIEW_BLOCKED`); BP-04 Phase 1 vẫn **không** thay đổi bởi D-09/D-11 (test T-LG-03 giữ nguyên).

## 30. Operational Readiness Gate (D-11)

### T-RG-01 – Published + NotReady: không tiếp nhận vào Queue [D-11, D-12] · INT · NOT RUN · Gate: P1-E, P1-H
- **Pre:** process Published, Readiness = NotReady. **Chuỗi:** gửi yêu cầu khớp trigger. **Pass:** **không** có Queue Request chính thức; có Intake Record ghi lý do `NOT_READY`; không Order, không Reservation, không đổi tồn kho.

### T-RG-02 – Published + Ready: đủ điều kiện tiếp nhận (vẫn qua kiểm tra khác) [D-11, D-12] · INT · NOT RUN · Gate: P1-E, P1-H
- **Pre:** Published + Ready. **Pass:** yêu cầu qua Process Selection và business checks rồi vào Queue (`Waiting`); **Ready không thay thế** Process Selection/kiểm tra nghiệp vụ (yêu cầu sai loại Rack vẫn bị từ chối).

### T-RG-03 – Published + Suspended: không tiếp nhận [D-11, D-12] · INT · NOT RUN · Gate: P1-E, P1-H
- **Pass:** như T-RG-01 với lý do `SUSPENDED`; không Queue Request mới.

### T-RG-04 – Disabled với mọi Readiness: không tiếp nhận [D-11] · INT · NOT RUN · Gate: P1-E
- **Chuỗi:** Disabled × {NotReady, Ready, Suspended}. **Pass:** cả ba **không** tiếp nhận; lý do `DISABLED`.

### T-RG-05 – Thiếu từng điều kiện Readiness → không Ready [D-11] · INT · NOT RUN · Gate: P1-E · BLOCKED(OQ-53)
- **Chuỗi:** từng điều kiện bị thiếu: Location/Station xác minh; ánh xạ IO PLC được duyệt (nếu dùng PLC); kết nối + handshake đã kiểm thử; tích hợp RCS; cơ chế xác nhận Pick/Drop; commissioning; không bị đình chỉ an toàn/vận hành. **Pass:** mỗi trường hợp Readiness ≠ Ready. Độ mịn gắn Readiness (theo Process Version hay theo Station/PLC), ai xác nhận, và `LOCATION_DISABLED` thuộc Gate nào: **chưa xác định (OQ-53)**.

### T-RG-06 – Workbook PLC chưa duyệt không là căn cứ Ready [D-11, CR-029] · INT · NOT RUN · Gate: P1-E
- **Pre:** chỉ có workbook 20/11 (CR-029 `PENDING_APPROVAL`). **Pass:** Readiness không thể đặt Ready dựa trên workbook; process không dùng PLC (thủ công P/M5) không bị đòi ánh xạ PLC.

### T-RG-07 – Chuyển trạng thái Readiness [D-11, D-12] · INT · NOT RUN · Gate: P1-E · BLOCKED(OQ-51)
- **Chuỗi:** NotReady → Ready; Ready → Suspended; Suspended → Ready (đã quyết trong D-12). **Pass (phần đã quyết):** ba chuyển trên được phép và có audit. Ready → NotReady trực tiếp và Suspended → NotReady: **chưa xác định (OQ-51)** – không viết kết quả.

### T-RG-08 – Audit thay đổi Readiness [D-11] · INT · NOT RUN · Gate: P1-E
- **Pass:** mỗi thay đổi ghi người/nguồn (hệ thống hay operator), lý do, thời điểm, giá trị trước/sau; bản ghi append-only (Spec §16.7).

## 31. Queue Intake và ghim Version (D-12)

### T-QI-01 – Published + Ready → Intake Record → pin → Queue Request `Waiting` [D-12] · INT · NOT RUN · Gate: P1-H
- **Chuỗi:** Request Intake → Process Selection → kiểm tra Published + Ready → ghim `ProcessVersionId` + cấp `QueueSequence` → `Waiting`. **Pass:** Queue Request có `ProcessVersionId` hợp lệ và `QueueSequence`; Intake Record liên kết Queue Request.

### T-QI-02 – Chưa Published/NotReady/Suspended/Disabled → không có Queue Request chính thức [D-12] · INT · NOT RUN · Gate: P1-H
- **Pass:** không Queue Request dispatchable; Intake Record ghi lý do tương ứng; **không** bỏ qua Readiness; không Order/Reservation.

### T-QI-03 – Không dùng Version thay thế [D-12, D-10] · INT · NOT RUN · Gate: P1-H
- **Pre:** Version cũ V1 Published + Ready; Version mới V2 Published + NotReady (process cùng mã). **Pass:** Request mới **không** tự ghim V1 để lách Readiness của V2 và **không** tự ghim latest; kết quả theo quy tắc “một Version Published/process” hiện hành – nếu hệ thống cho hai Version Published cùng lúc thì hành vi là **[ĐX]**, cần xác nhận (không viết kỳ vọng).

### T-QI-04 – `NeedsDecision` là Intake Record, không dispatchable [D-10, D-12] · INT · NOT RUN · Gate: P1-H
- **Pre:** hai process khớp cùng yêu cầu. **Pass:** Intake Record `NeedsDecision` giữ bền; không ghim; không `QueueSequence`; không Order/Reservation; cảnh báo operator; không Dispatch.

### T-QI-05 – Sau giải quyết `NeedsDecision` kiểm tra lại Published + Ready [D-12] · INT · NOT RUN · Gate: P1-H · BLOCKED(OQ-35 cho chủ thể giải quyết)
- **Chuỗi:** operator chọn process P; (a) P Published + Ready → ghim → `Waiting`; (b) P đã chuyển Suspended/NotReady/Disabled giữa lúc chờ quyết định. **Pass:** (a) vào Queue với Version dùng được ghi audit; (b) **không** ghim, **không** vào Queue; Intake Record ghi lý do; không chọn Version khác.

### T-QI-06 – Audit quyết định, người, Version [D-10, D-12] · INT · NOT RUN · Gate: P1-H
- **Pass:** audit có process được chọn, Version dùng, người quyết định, thời điểm, lý do; truy ngược Queue Request → Order → Leg (T-PIN-D).

### T-QI-07 – Kiểm tra + ghim là một thao tác nhất quán [D-12] · INT · NOT RUN · Gate: P1-H
- **Chuỗi:** Readiness chuyển Suspended **đồng thời** với intake (đua). **Pass:** kết quả luôn là một trong hai: (i) Queue Request `Waiting` có `ProcessVersionId` + `QueueSequence` hợp lệ, hoặc (ii) không có Queue Request (Intake Record từ chối); **không** tồn tại Queue Request chính thức thiếu `ProcessVersionId` hợp lệ; Request vào Queue rồi bị Suspended ngay sau đó → `Held` (T-HD-01). Cơ chế khóa/cô lập giao dịch: **[ĐX]**, không quy định trong P0.

### T-QI-08 – Intake Record không đủ điều kiện Dispatch [D-12] · INT · NOT RUN · Gate: P1-H
- **Pass:** không đường nào (gồm retry/wake-up) tạo Order từ Intake Record chưa chuyển thành Queue Request.

### T-QI-09 – Mã process tường minh (thủ công/PDA) không bỏ qua Readiness [D-12] · INT · NOT RUN · Gate: P1-H
- **Chuỗi:** yêu cầu chỉ định `ProcessCode` của process Published + NotReady. **Pass:** bị từ chối như T-QI-02; không ghim.

### T-QI-10 – Xử lý Intake Record bị từ chối [D-12] · INT · NOT RUN · BLOCKED(OQ-50)
- **Pass (phần đã quyết):** không mất dấu yêu cầu (Intake Record bền). Ghi nhận/thông báo/nhận lại khi Ready trở lại: **chưa xác định (OQ-50)** – không viết kết quả.

### T-QI-11 – Khóa FIFO của Request từng NeedsDecision/bị từ chối rồi được nhận [D-13] · INT · NOT RUN · BLOCKED(OQ-56)
- **Pass:** `QueueSequence` cấp theo `ReceivedAt` hay theo thời điểm vào Queue chính thức: **chưa xác định (OQ-56)**.

## 32. Held và hồi phục Readiness (D-12)

### T-HD-01 – Suspended → `Waiting → Held` [D-12] · INT · NOT RUN · Gate: P1-H, P1-I
- **Pre:** Request `Waiting` (ghim V1, `QueueSequence`=n). **Chuỗi:** Readiness → Suspended. **Pass:** Request `Held`; **không** Cancelled/Failed; `ProcessVersionId` và `QueueSequence` giữ nguyên; audit `QueueHeld`.

### T-HD-02 – Held không đổi Rack/tồn kho [D-12, INV] · INT · NOT RUN · Gate: P1-I
- **Pass:** `CurrentLocation`, trạng thái Rack, tồn kho, Buffer occupancy **không đổi** khi Held; không Order mới; không lệnh RCS.

### T-HD-03 – Hồi phục Ready không tự Dispatch [D-12] · INT · NOT RUN · Gate: P1-I
- **Chuỗi:** Suspended → Ready. **Pass:** Request `Held` **không** được Dispatch ngay; phải qua revalidate (T-HD-04) trước khi về `Waiting`; không lệnh RCS/PLC phát sinh trong lúc Held.

### T-HD-04 – Revalidate: Rack, đích, PLC, điều kiện vận hành [D-12] · INT · NOT RUN · Gate: P1-I
- **Chuỗi:** sau Ready: (a) tất cả còn hợp lệ → `Waiting`; (b) Rack không còn khả dụng/đích không còn hợp lệ/PLC offline. **Pass:** (a) về `Waiting`; (b) **không** về Waiting như thể không có gì; trạng thái sau revalidate thất bại (ở lại `Held` hay chuyển `Blocked` với lý do) là **[ĐX]** (Spec §22.2) – chờ xác nhận, không khẳng định trong test.

### T-HD-05 – Trở lại đúng vị trí FIFO ban đầu [D-12, D-13] · INT · NOT RUN · Gate: P1-I
- **Pre:** cùng một Scope: A (seq 1), B (seq 2), C (seq 3), cả ba `Waiting`. **Chuỗi:** B → Held → Waiting. **Pass:** thứ tự sau hồi phục vẫn A, B, C (B **không** xuống cuối); `QueueSequence` của B không đổi. Trong lúc B Held, C có được Dispatch trước B hay không: **BLOCKED(OQ-58)**.

### T-HD-06 – Dispatched/InProgress không tự hủy khi Suspended [D-12] · INT · NOT RUN · Gate: P1-I
- **Pre:** Request đã `Dispatched`/`InProgress`. **Chuỗi:** Readiness → Suspended. **Pass:** **không** tự hủy, **không** giả định đã dừng an toàn; xử lý theo trạng thái RCS/AMR thực (đồng bộ) và quy trình sự cố; có cảnh báo operator. Hành động an toàn cụ thể: **ngoài phạm vi P0** (OQ-52).

### T-HD-07 – Held bền qua restart WCS [D-12, D-03] · INT · NOT RUN · Gate: P1-I, P2-B
- **Pass:** sau restart, Request vẫn `Held` với đúng `ProcessVersionId`, `QueueSequence`; không Dispatch tự động.

### T-HD-08 – Reservation khi Held [D-12] · INT · NOT RUN · BLOCKED(OQ-57)
- **Pass:** Request Held có giữ Reservation Rack/slot hay không: **chưa xác định (OQ-57)**. Phần đã quyết: Held **không** đổi tồn kho.

### T-HD-09 – Held ở đầu Scope có chặn Request sau không [D-13] · INT · NOT RUN · BLOCKED(OQ-58)
- **Pass:** kỳ vọng **không** viết cho tới khi OQ-58 được trả lời (đề xuất [ĐX]: chặn như Blocked).

### T-HD-10 – Audit `QueueHeld`/`QueueResumed` [D-12] · INT · NOT RUN · Gate: P1-I
- **Pass:** mỗi lần vào/ra Held ghi lý do Suspended, `QueueSequence`, `ProcessVersionId`, kết quả revalidate, vị trí FIFO khôi phục (Rack doc §13.1).

## 33. Strict FIFO theo Queue Ordering Scope (D-13)

> **Dữ liệu cấu hình Scope chưa có (OQ-54).** Các test dưới đây dùng Scope **giả lập** trong fixture; chúng kiểm **quy tắc thứ tự**, không kiểm cấu hình sản xuất.

### T-SF-01 – Không vượt hàng trong cùng Scope [D-13] · INT · NOT RUN · Gate: P2-B
- **Pre:** A (seq 1), B (seq 2) cùng Scope, cả hai Waiting; cả hai đủ điều kiện. **Pass:** A được Dispatch trước B; B **không** vượt A kể cả khi B sẵn sàng sớm hơn.

### T-SF-02 – Đầu Scope Blocked → Request sau chờ [D-13] · INT · NOT RUN · Gate: P2-B
- **Pre:** A (đầu) Blocked (không đích); B cùng Scope có tài nguyên sẵn. **Pass:** B **chờ**; không `QueueBypass`; khi A hết Blocked thì A trước B.

### T-SF-03 – Scope độc lập chạy song song [D-13] · INT · NOT RUN · Gate: P2-B
- **Pre:** X thuộc Scope S1, Y thuộc Scope S2; X Blocked. **Pass:** Y Dispatch bình thường, không bị chặn bởi X.

### T-SF-04 – Một giao dịch hoạt động/PLC khi Dispatch song song [D-03, D-13, ADR-0011 D-1] · INT · NOT RUN · Gate: P2-B · BLOCKED(OQ-55, OQ-02)
- **Pre:** hai Request thuộc **hai Scope khác nhau** cùng cần PLC-01. **Pass (đã quyết):** không bao giờ có hai giao dịch hoạt động cùng PLC. Ai trước (FIFO theo PLC toàn cục hay theo Scope) và mốc kết thúc giao dịch: **chưa xác định (OQ-55, OQ-02)**.

### T-SF-05 – PLC độc lập chạy song song khi không có ràng buộc chung [D-13] · INT · NOT RUN · Gate: P2-B · BLOCKED(OQ-54)
- **Pre:** Request ở PLC-01 và PLC-03; không chia sẻ Rack/Buffer/đích; Scope được fixture khai báo độc lập. **Pass:** Dispatch song song. Điều kiện “không tài nguyên chung” do cấu hình Scope được duyệt xác định (OQ-54) – test không suy luận điều này từ ID PLC.

### T-SF-06 – Không mặc định mỗi PLC là một Scope [D-13] · UNIT/INT · NOT RUN · Gate: P2-B · BLOCKED(OQ-54)
- **Pre:** hai Request cùng PLC-01 nhưng fixture khai báo Scope khác nhau (và ngược lại: khác PLC cùng Scope). **Pass:** thứ tự theo **Scope khai báo**, không theo PLC.

### T-SF-07 – Không mặc định một Scope toàn cục [D-13] · UNIT/INT · NOT RUN · Gate: P2-B · BLOCKED(OQ-54)
- **Pass:** hai Request ở Scope độc lập khai báo không bị buộc thứ tự toàn cục.

### T-SF-08 – Scope chưa xác định → không Dispatch [D-13] · INT · NOT RUN · Gate: P2-B · BLOCKED(OQ-54)
- **Pre:** không có cấu hình Scope cho Request. **Pass (đề xuất [ĐX]):** Request ở `Waiting` với lý do `SCOPE_UNDETERMINED`, **không** Dispatch, **không** tự suy Scope; hành vi chính thức chờ OQ-54.

### T-SF-09 – Tập tài nguyên giao nhau tạo ràng buộc thứ tự [D-13] · INT · NOT RUN · Gate: P2-B · BLOCKED(OQ-54)
- **Pre:** Request X dùng Rack R và Buffer B1; Request Y dùng Buffer B1; Z dùng Buffer B2. **Pass:** X và Y thuộc cùng ràng buộc thứ tự (giao nhau ở B1); Z độc lập. Bao gồm cả giao nhau gián tiếp (X–Y, Y–W ⇒ chung Scope hay không): **chưa xác định (OQ-54)**.

### T-SF-10 – Bảo toàn FIFO qua `Waiting → Held → Waiting` [D-12, D-13] · INT · NOT RUN · Gate: P1-I, P2-B
- **Pass:** xem T-HD-05; thêm kiểm tra khi Request mới tới trong lúc B Held: Request mới nhận `QueueSequence` lớn hơn C (không chen vào trước B).

### T-SF-11 – Bản ghi `ScopeEvaluation` và không có `QueueBypass` [D-13] · INT · NOT RUN · Gate: P2-B
- **Pass:** mỗi quyết định thứ tự ghi Scope, thành viên, Request đầu Scope, lý do chờ; **không** phát sinh sự kiện vượt hàng trong cùng Scope (nếu có ⇒ lỗi điều tra).

### T-SF-12 – Dispatch song song nhiều Scope không vượt giới hạn PLC [D-13] · INT · NOT RUN · Gate: P2-B · BLOCKED(OQ-55)
- **Pre:** 3 Scope; hai Scope cùng PLC-01, một Scope ở PLC-02. **Pass:** tại mọi thời điểm ≤ 1 giao dịch hoạt động/PLC và ≥ 1 giao dịch ở PLC-02 song song; thứ tự giữa hai Scope cùng PLC-01: OQ-55.

## 34. Disabled và Emergency Revocation

### T-DR-01 – Disabled ngừng tiếp nhận Request mới [D-11] · INT · NOT RUN · Gate: P1-E
- **Pass:** sau Disabled, intake mới → Intake Record `DISABLED` (T-RG-04); không Queue Request mới.

### T-DR-02 – Disabled: Order đang chạy hoàn tất bình thường [D-11, Spec §15.3] · INT · NOT RUN · Gate: P1-E
- **Pass:** Order/Leg đang chạy dùng snapshot cũ, không bị hủy; hoàn tất theo D-05.

### T-DR-03 – Disabled: Request đã ghim đang Waiting/Held [D-10] · INT · NOT RUN · BLOCKED(OQ-45)
- **Pass:** **chưa xác định (OQ-45)** (dispatch với Version đã ghim, về `NeedsDecision`, hay bị hủy). Tuyệt đối **không** tự chuyển sang Version khác – điều này đã quyết (D-10).

### T-DR-04 – Hai trục độc lập khi Disabled [D-11] · UNIT · NOT RUN · Gate: P1-E
- **Pass:** Disabled là giá trị của Publication Status; Readiness ghi nhận riêng (không bị xóa/ghi đè bởi Disabled) và không có trạng thái gộp; hành vi **Re-enable** (Disabled → Published) và Readiness sau đó: **[ĐX]**, cần xác nhận (không có trong D-11).

### T-DR-05 – Emergency Revocation khác Disabled [D-11] · INT · NOT RUN · BLOCKED(OQ-52)
- **Pass:** định nghĩa, thẩm quyền, và trạng thái (đề xuất [ĐX]: Suspended với lý do khẩn cấp ⇒ Held ngay) **chưa chốt (OQ-52)**. Phần đã quyết kiểm được: Disabled thường (Publication) **không** dùng để biểu diễn sự cố an toàn khẩn; hai thao tác có audit/lý do phân biệt được.

### T-DR-06 – Revocation/Suspension khẩn không tự hủy lệnh đã Dispatch [D-12] · INT · NOT RUN · BLOCKED(OQ-52)
- **Pass (đã quyết):** Request Dispatched/InProgress không tự hủy, không giả định dừng an toàn (như T-HD-06). Hành động an toàn cụ thể: ngoài phạm vi, theo quy trình sự cố.

### T-DR-07 – Audit Disabled/Revocation [D-11] · INT · NOT RUN · Gate: P1-E
- **Pass:** ghi người, lý do, thời điểm, Version bị ảnh hưởng, số Request Waiting/Held/Dispatched tại thời điểm đó.

### T-DR-08 – Version đã ghim và snapshot không bị thay đổi bởi Disabled [D-10] · INT · NOT RUN · Gate: P1-E
- **Pass:** snapshot/hash của Version đã ghim và Order đang chạy không đổi; truy vết (T-PIN-D) vẫn hoạt động sau Disabled.

## 35. Ma trận phủ Revision 1.2 và tổng hợp

| Yêu cầu | Test |
|---|---|
| D-11 Publication Gate | T-PG-01…10 |
| D-11 Readiness Gate | T-RG-01…08 |
| D-11 Disabled/Revocation | T-DR-01…08, T-RG-04 |
| D-12 Queue Intake/Pinning | T-QI-01…11, T-PIN-A…I |
| D-12 Held/Recovery | T-HD-01…10, T-SF-10 |
| D-13 Strict FIFO/Scope | T-SF-01…12, T-FI-01, T-FI-02, T-FI-03, T-P4-10 |
| Giữ D-09/D-10 | T-P4-01…12, T-PIN-A…I, T-PG-10 |

## 36. Revision 1.2 – thay đổi trong file này

| Mục | Thay đổi | Decision |
|---|---|---|
| Đầu file | Trạng thái, hàng Revision 1.2 | — |
| T-FI-01 | Viết lại theo strict FIFO theo Scope; **bỏ** BLOCKED(OQ-01) (OQ-01 đóng); BLOCKED(OQ-54) cho cấu hình Scope | D-13 |
| T-FI-02, T-FI-04, T-QU-01 | `Queued` → `Waiting`; ghi chú khe PLC giữa Scope BLOCKED(OQ-55) | D-12, D-13 |
| T-DP-01, T-P4-01, T-P4-07, T-P4-09 | Bỏ BLOCKED(OQ-44) (đóng); kỳ vọng theo Two-Gate (Published + NotReady) | D-11 |
| T-P4-10 | Bỏ BLOCKED(OQ-01); BLOCKED(OQ-54) | D-13 |
| T-DP-08, T-PIN-A, T-PIN-C | Intake Record `NeedsDecision`; kiểm tra lại Published + Ready sau giải quyết; `Waiting` thay `Queued/Active` | D-12 |
| §12, §13 | Ma trận và tổng hợp cập nhật | — |
| §29–§36 mới | T-PG (10), T-RG (8), T-QI (11), T-HD (10), T-SF (12), T-DR (8) = 59 test | D-11…D-13 |
| Giữ nguyên | **Mọi ID cũ, không xóa test; mọi test NOT RUN, không test nào Passed** | — |
