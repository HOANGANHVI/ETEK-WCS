# CR-010 – Hợp nhất quy trình nghiệp vụ và đặc tả giao tiếp PLC: mô hình Transport Task / Leg / Station Transaction (DRAFT)

**Trạng thái:** `DRAFT – chờ chủ dự án review/phê duyệt (CR-010)`. Chỉ là tài liệu. Không có mã nguồn, database, migration, API, UI hay cấu hình runtime nào bị thay đổi. Không sửa đặc tả baseline đã duyệt (xem §11).
**Đọc cùng:** [Đặc tả giao tiếp PLC](PLC_COMMUNICATION_SPECIFICATION_DRAFT.md) · [Khảo sát & khoảng cách](PLC_REVIEW_CURRENT_STATE_AND_GAP_ANALYSIS.md) · [Câu hỏi cần xác nhận](PLC_SPEC_OPEN_QUESTIONS.md).

Nhãn: **[YC2]** = chủ dự án xác nhận trong yêu cầu hợp nhất (2026-11-18) · **[XLS]**, **[YC]**, **[HT]**, **[ĐX]**, **TBD** như trong đặc tả PLC. **[BL]** = có trong đặc tả baseline đã duyệt (`process-flows.md`, `business-rules.md`, `task-state-machine.md`, `inventory-management.md`).

---

## 0. Giới hạn nguồn dữ liệu (đọc trước)

| Nguồn | Tình trạng |
|---|---|
| `7. FFC AMR/Process in detail.pptx` (11 slide, "Process AGV update", 20/1/2026) | **Đã đọc** toàn bộ văn bản slide 1–11 (không có ghi chú trình chiếu). Slide 5–10 khớp BP-01…BP-06 trong baseline. Chỉ trích **văn bản**; sơ đồ mũi tên/hình vẽ không đọc được từ văn bản, nên thứ tự nhánh chỉ lấy từ chữ trong slide |
| `7. FFC AMR/ETEK Process AGV Phase 2 12.08.26.pptx` (10 slide, 20/1/2026) | **Đã đọc** toàn bộ văn bản. Tệp trên đĩa tên **không có "(1)"** như trong yêu cầu; giả định đây là tệp được nhắc |
| `process-flows.md`, `business-rules.md`, `inventory-management.md`, `task-state-machine.md`, bảng truy vết | Đã đọc (bản đã duyệt) |

Các tệp .pptx nằm ở **thư mục cha** của dự án WCS, không nằm trong `docs/`. Ghi chú trung thực: lần tìm kiếm đầu của tôi bị cắt kết quả và đã báo nhầm "không có"; đã sửa.

**Phát hiện lớn từ đối chiếu slide** (chi tiết §3.1): (a) câu "tại một thời điểm chỉ có 1 task" nằm ở slide 4 của `Process in detail`; (b) trong các luồng đã duyệt, **buffer F1–F5 luôn là điểm kết thúc của task**, không có chặng "buffer → đích" tự động; chặng đó chỉ xuất hiện ở luồng BP-04 (`REVIEW`) hoặc do người vận hành gọi (BP-05); (c) tệp **Phase 2** dùng tên vị trí khác (P1–P4, M5, M6) và có nhánh "chờ ở vị trí trung gian", chưa thuộc baseline Phase 1.

---

## 1. Nguyên tắc đã được xác nhận **[YC2]** (không hỏi lại)

1. Hai cấp độc lập: **Station Transaction** (một lần AMR vào một cổng để lấy/giao rack) và **Transport Task** (toàn bộ nhiệm vụ, có thể nhiều chặng).
2. Mỗi cổng (một bộ I/O) có tối đa một Station Transaction đang hoạt động; kết thúc khi AMR hoàn thành lấy/giao và đã **ra khỏi cổng**, lúc đó cổng được giải phóng; PLC quyết định thời điểm phát request kế tiếp của cổng.
3. Các cổng hoạt động độc lập, không khóa toàn PLC, cả cùng PLC lẫn khác PLC.
4. Một Transport Task có thể gồm nhiều chặng (Leg), liên kết nhiều Station Transaction; **kết thúc một Station Transaction ≠ Transport Task hoàn thành**.
5. Phải hỗ trợ: giao trực tiếp A→B; A→buffer→B khi B chưa có chỗ; rack nằm ở buffer rồi tiếp tục tới B; cùng cơ chế theo chiều cấp rack trống **theo Business Process đã duyệt**. Không mặc định "một lấy – một giao".
6. Buffer là vị trí vật lý thật: thả rack ở buffer ⇒ WCS cập nhật vị trí và tồn kho; **không** mặc nhiên kết thúc Transport Task; chỉ dùng khi BP cho phép; không tự chọn vị trí ngoài danh sách cấu hình + BP cho phép.
7. Giữ nguyên 9 bước, tên/địa chỉ/nghĩa tín hiệu. M9101 ACK, M9120, mapping handshake↔pick/drop, handshake tại cổng đích và vấn đề giao thức: **tiếp tục OPEN**.
8. Không đổi BP-01/02/03/05/06; BP-04 `REVIEW`; A3/A4 và D1/D2 `UNCHANGED`; không đổi `TaskStateMachine`/`InventoryService`; WCS sở hữu tồn kho.

---

## 2. Phát hiện chính khi hợp nhất (xung đột được ghi nhận, không tự giải quyết)

| # | Phát hiện | Căn cứ | Mức |
|---|---|---|---|
| **C-1** | **Cổng (bộ I/O) là gì?** Yêu cầu mới: mỗi cổng có một bộ I/O và nhiều cổng cùng PLC chạy đồng thời. Nhưng Excel `IO Matrix` có **một** bộ tín hiệu cho mỗi PLC (M9001/M9002… một `Request_ID` D1500, **một** khối dữ liệu D1510/D1512…); Trạm chỉ được suy ra từ giá trị thanh ghi. Yêu cầu [YC] trước đó: mỗi PLC tối đa **một** transaction. Ba cách hiểu loại trừ nhau (xem §4.1) | Excel; [YC] 2026-11-17; [YC2] | **Chặn** |
| **C-2** | **Nhiều chặng vs. một task RCS.** `task-state-machine.md` §3.6: chờ đích bằng cách giữ **một** task RCS ở waypoint rồi `continueTask`; nếu RCS không hỗ trợ thì "phải thiết kế cơ chế nhiều chặng task và **được duyệt**". Yêu cầu mới (A→buffer→B với tồn kho cập nhật tại buffer) là **nhiều chặng thực sự** | [BL] §3.6; [YC2] | **Chặn** |
| **C-3** | **PER_AGV khi rack đã thả ở buffer.** Baseline: `WAITING_FOR_DESTINATION` vẫn **chiếm AGV** (§3.3, §3.6). Yêu cầu mới: rack nằm ở buffer, Transport Task chờ, và (kịch bản 4) cổng A nhận request mới. AGV có được giải phóng giữa các chặng không? Nếu có, đó là thay đổi cách `PER_AGV` áp dụng (task còn chờ nhưng AGV rảnh) | [BL] §3.3, BR-01; [YC2] | **Chặn** |
| **C-4** | **Hai mô hình "transaction" trong cùng CR.** Bản nháp trước gọi *PLC transaction* kết thúc ở bước 9 và dùng khóa theo PLC. Yêu cầu mới thay bằng *Station Transaction theo cổng*. Bản nháp §9 phải được hiểu lại (xem §10) | Đặc tả PLC §9.1 | Sửa tài liệu |
| **C-5** | **Mốc "AMR đã ra khỏi cổng".** Chủ dự án định nghĩa kết thúc Station Transaction = AMR đã rời cổng. Trong 9 bước, bước 8 (M9123, ghi khi AGV đã rời) có đúng nghĩa này, nhưng bước 9 (M9005, PLC xác nhận giao nhận thành công) đến **sau**. Giải phóng cổng ở bước 8 hay 9? | Excel; [YC2] | Cần xác nhận (Q-REL-1) |
| **C-6** | **Chặng ở cổng khác cổng phát request** (Q-LEG-1) vẫn mở; cổng buffer có thể không có PLC (F1…F5/KV là vị trí, không nằm trong 16 Trạm PLC) | [BL] §1.5; Q-LEG-1 | **Chặn** |
| **C-7** | **PLC báo vị trí khác tồn kho WCS** (Q-INV-1) vẫn mở; kịch bản 9 | Q-INV-1 | **Chặn** |
| **C-8** | **BP-04 `REVIEW`**: Transport Task nào có điểm giao tại B1/B2 để lấy "rack đầy" bị loại khỏi phạm vi; mô hình chỉ **chuẩn bị chỗ**, không mô tả BP-04 | [BL] | Giữ |
| **C-10** | **Buffer→cổng máy tự động không có trong luồng đã duyệt.** Slide 5–10: buffer F là điểm kết thúc của task; chặng buffer→B1/B2 (rack đầy) chỉ có ở BP-04 `REVIEW` | Slide 5–10, 8 | **Chặn** (Q-MLEG-2) |
| **C-9** | **Nhân bản/thiếu nguồn:** F1–F5, KV là vị trí; baseline gọi F3/F4 trong BP-06 là "buffer rack trống" nhưng BP-02 lại dùng F3/F4 làm **nguồn** rack trống; thứ tự ưu tiên nguồn/đích **chưa được xác nhận** ([BL] §1.7) | [BL] | Giữ `NEEDS_CONFIRMATION` |

---

## 3. Bảng phân tích quy trình (theo yêu cầu §3)

Căn cứ: `process-flows.md` [BL], **đã đối chiếu với văn bản slide 5–10 của `Process in detail.pptx`** (§3.1). `NC` = `NEEDS_CONFIRMATION` (slide cũng không nêu). Các chặng "Next leg" được diễn giải theo **Leg**; ở baseline mỗi quy trình là một task AGV với `continueTask`.

| Process ID | Request source | Rack type | Pickup location | Destination candidates (theo thứ tự trong baseline) | Buffer được phép | Waiting condition | Next leg | Completion condition | PLC handshake |
|---|---|---|---|---|---|---|---|---|---|
| **BP-01** | PLC tại **A1/A2** (yêu cầu lấy) | Rack **đầy** (AGV1) | A1/A2 | 1) B1/B2 có chỗ trống; 2) F1/F2 (WCS inventory); 3) F5 (WCS inventory) | F1/F2, F5 *(trong baseline cũng là "đích")* | Không có đích ⇒ `WAITING_FOR_DESTINATION`, không hủy/không tạo task mới | Giao tại đích đã chỉ định; thả tại **B1/B2 "Không bind rack"** | WCS xác nhận kết quả, cập nhật tồn kho, hoàn tất task; AGV1 về điểm chờ | **Pick tại A1/A2: có** (9 bước). Thả tại B1/B2: có "nếu điểm cần PLC cho phép" **NC (Q-LEG-1)**; F1/F2/F5: kiểm tra bằng WCS inventory, handshake PLC **NC** |
| **BP-02** | PLC tại **A1/A2** (cần rack trống) | Rack **trống** (AGV2) | 1) rack trống tại B1/B2; 2) F3/F4 (inventory) | A1/A2 (nơi yêu cầu) | F3/F4 *(nguồn)* | Không có rack trống ở cả B1/B2 và F3/F4 ⇒ chờ/lỗi nghiệp vụ **theo cấu hình**; không tự tạo nguồn thay thế | Chặng lấy → `continueTask` → chặng giao tại A1/A2 | WCS xác nhận giao nhận, cập nhật tồn kho; AGV2 về điểm chờ | Lấy tại B1/B2 và thả tại A1/A2 đều "yêu cầu PLC cho phép nếu cần". Với A1/A2 là cổng phát request: 9 bước. Với B1/B2 là cổng nguồn: **NC (Q-LEG-1)** |
| **BP-03** | PLC tại **B1/B2** (yêu cầu lấy rack trống) | Rack **trống** (AGV2) | B1/B2 | 1) A1/A2 có chỗ; 2) F3/F4 (inventory) | F3/F4 | Không có đích ⇒ `WAITING_FOR_DESTINATION` | Giao tại đích; thả tại **A1/A2 "Không bind rack"** | Cập nhật inventory theo kết quả thực tế; AGV2 về điểm chờ | Pick tại B1/B2: 9 bước. Thả tại A1/A2: **có PLC mở cửa (slide 7)**; trình tự tín hiệu **NC (Q-LEG-1)** |
| **BP-04** | B1/B2 có chỗ trống, cần rack đầy | – | – | – | – | – | – | – | **REVIEW – NGOÀI PHẠM VI**; không phân tích thêm, không triển khai |
| **BP-05** | **F5** (beeper, người vận hành) – **không phải PLC** | Rack **đầy** (AGV1) | F1/F2 (có rack đầy theo inventory) | F5 (nếu F5 có chỗ trống theo inventory) | F1/F2 *(nguồn)* | F5 không có chỗ: baseline **không nêu** (ghi chú về nhánh hủy task AGV1 cần đối chiếu sơ đồ gốc) **NC** | – (một chặng) | WCS xác nhận, cập nhật inventory; AGV1 về điểm chờ | **Không** (F5 không phải Trạm PLC trong baseline); phụ thuộc nhánh hủy **NC** |
| **BP-06** | **F5** (beeper trả rack trống) | Rack **trống** (AGV2) | F5 | 1) A1/A2 có chỗ; 2) F3/F4 (inventory) | F3/F4 | Baseline không nêu điều kiện chờ ⇒ **NC** | Giao tại đích; thả tại **A1/A2 "Không bind rack"** | Cập nhật inventory, hoàn tất; AGV2 về điểm chờ | Thả tại A1/A2: **có PLC mở cửa (slide 10)**; trình tự tín hiệu **NC (Q-LEG-1)** |
| **A3/A4, D1/D2** | – | – | – | – | – | – | – | – | **UNCHANGED** – theo process cũ; PLC chỉ giám sát (monitor-only) |

**Ghi chú quan trọng cho Multi-leg:**

- Trong `Process in detail` (đã duyệt), khi B1/B2 không có chỗ, AGV **thả rack tại F1/F2 rồi F5 và task kết thúc ("AGV 1 trở về điểm chờ")**; tương tự F3/F4 cho rack trống. Không có slide nào mô tả WCS **tự động** lấy rack từ buffer đưa tới B/A khi chỗ trống xuất hiện. Chặng "buffer → cổng máy" chỉ có ở: BP-04 (slide 8 – `REVIEW`, "AGV … lấy rack đầy từ F1/F2 … → B1/B2"), BP-02 (lấy rack trống từ F3/F4 **khi A1/A2 yêu cầu**), BP-05 (F5, người vận hành gọi).
- Vì vậy kịch bản "A → buffer → (tự động) → B" ở [YC2] là **quy trình mới**, và với rack **đầy** đi vào B1/B2 nó **trùng BP-04 (REVIEW)** ⇒ không được thiết kế/triển khai cho đến khi BP-04 được duyệt (Q-MLEG-2). Với rack **trống**, chặng F3/F4 → A1/A2 đã có trong BP-02 nhưng được kích hoạt bởi yêu cầu của A1/A2, không phải tự động sau khi thả vào buffer.
- Danh sách buffer cho từng BP: lấy từ bảng trên (F1/F2, F5 cho rack đầy; F3/F4 cho rack trống). KV3/KV4 chỉ có vai trò trong `business-rules.md` §1.4–1.5, không xuất hiện trong luồng slide 5–10.

### 3.1. Đối chiếu văn bản slide (bằng chứng)

| Mục | Slide | Nội dung văn bản trích | Hệ quả cho đặc tả |
|---|---|---|---|
| Handshake PLC tại cổng máy | 5, 6, 7 | "PLC mở cửa và cho phép lấy hàng", "PLC mở cửa và cho phép vào thả hàng" (A1/A2, B1/B2 cả khi lấy lẫn thả) | **Xác nhận** cổng đích B1/B2 (BP-01) và A1/A2 (BP-02/03/06) **có** PLC mở cửa. Trình tự tín hiệu ở cổng thứ hai vẫn không có (Q-LEG-1) |
| Không có PLC ở buffer F | 5, 7, 9, 10 | "AGV đi vào thả hàng tại F1/F2 / F5 / F3/F4 và đi ra"; xác định bằng "bind rack", không có bước "PLC mở cửa" | **Xác nhận** buffer F không có handshake PLC; vị trí xác định bằng bind rack/WCS inventory (Q-BUF-1 còn lại: cách xác nhận lấy/thả) |
| "Chờ nhận vị trí thả" | 5, 7 | "Chờ nhận vị trí thả hàng/thả rack trống", AGV đã lấy rack (đã `continue task`) | Đây là `WAITING_FOR_DESTINATION`: **AGV giữ rack**, không phải rack ở buffer |
| "Không bind rack" | 5, 7, 10 | Thả tại B1/B2 (BP-01), A1/A2 (BP-03, BP-06) | Giữ nguyên |
| Một task một thời điểm | 4 | "**Tại một thời điểm chỉ có 1 task được thực hiện**" (trong trang Trạng thái ban đầu) | Xem §6.1 |
| Phân công AGV | 4 | AGV1: KV1, KV3 → KV2, KV4, F5. AGV2: KV2, KV4 → KV1, KV3 | Khớp `business-rules.md` §1.4 |
| F5 nhánh hủy task | 9 | "F5 không có rack? … Gửi tín hiệu xóa task của AGV 1" | Vẫn `NEEDS_CONFIRMATION`; không tự gọi cancel |
| Tồn kho ban đầu | 4 | "Khai báo số rack ở F3, F4 bằng PDA" | Khớp inventory §4.7 |
| **Tệp Phase 2** (`ETEK Process AGV Phase 2`) | 2–9 | Vị trí mới **P1–P4** (buffer hàng), **M5/M6** (QC thủ công/buffer manual), "AGV ra **vị trí chờ trung gian**", dòng B1/B2 cần rack đầy "lấy từ P1/P2", nhãn "Model A (Phase 1)" | **Không thuộc baseline**; `process-flows.md` §2.3 cấm suy ra quy trình Phase 2. Tên P1–P4/M5/M6 chưa có trong `business-rules.md`; quan hệ với F1–F5 chưa biết (Q-LOC-1, Q-PH2-1). Chỉ ghi nhận như **căn cứ cho khái niệm "vị trí chờ trung gian"**, không thiết kế theo nó |

---

## 4. Mô hình khái niệm đề xuất **[ĐX]**

```
Transport Task (1 yêu cầu nghiệp vụ, PER_AGV áp dụng khi AGV đang thực thi)
  ├── Leg 1  (lấy tại cổng nguồn → điểm đích của chặng)
  │     └── Station Transaction tại cổng nguồn   (9 bước nếu cổng có PLC)
  ├── Leg 2  (nếu cần: buffer)        — chỉ khi BP cho phép
  │     └── Station Transaction / xác nhận vị trí tại buffer
  └── Leg 3  (buffer → đích cuối)
        └── Station Transaction tại cổng đích
```

Đây là mô hình minh họa. Không quy định bảng, lớp hay schema.

### 4.1. "Cổng" (Gate) – ba cách hiểu, **chưa chọn** (Q-GATE-1, chặn)

| Cách hiểu | Mô tả | Hệ quả | Phù hợp với |
|---|---|---|---|
| **G-A** | Cổng = **một PLC / một bộ I/O trong Excel** (một Gate ⇔ một `PlcId`) | Mỗi PLC tối đa 1 Station Transaction (khớp [YC] cũ); "hai cổng cùng PLC đồng thời" **không xảy ra**; kịch bản 5 không áp dụng; PLC khác nhau vẫn song song | Excel hiện tại + [YC] 2026-11-17 |
| **G-B** | Cổng = **một Trạm (A1, A2, …)** có bộ I/O riêng trong PLC (bộ M/D riêng từng Trạm) | Cần **16 bộ tín hiệu** như CR-002/ADR-0004; trái Excel mới (một bộ mỗi PLC) và trái "mapping theo PLC" | Yêu cầu [YC2] "cùng PLC đồng thời", CR-002 |
| **G-C** | Cổng = **một Trạm**, nhưng các Trạm cùng PLC **dùng chung một bộ tín hiệu/thanh ghi request** (Trạm suy từ D1510/D1512) | Nhiều Station Transaction cùng PLC có thể tồn tại ở mức logic (đang được AGV phục vụ), nhưng **handshake** phần dùng chung tín hiệu phải tuần tự ⇒ không thật sự độc lập; cần quy tắc giành quyền dùng tín hiệu | Excel + [YC2] cho kết quả lai |

Đề xuất: **không chọn** trước khi kỹ sư PLC cho biết cùng một PLC có thể có hai Trạm đang ở giữa 9 bước cùng lúc hay không. Phần còn lại của tài liệu được viết theo khái niệm **Gate** để đúng với cả ba; mỗi kịch bản acceptance nêu rõ điều kiện áp dụng.

### 4.2. Năm loại trạng thái độc lập (không trộn)

| Loại trạng thái | Sở hữu | Ghi chú |
|---|---|---|
| **Transport Task state** | WCS | Đã có `TaskStateMachine` [HT, **không đổi**]; trạng thái *chặng* dưới đây là lớp **bổ sung**, không thêm giá trị vào enum hiện có |
| **Leg state** | WCS | Mới (đề xuất) |
| **Station Transaction state** | WCS, đồng bộ với tín hiệu PLC | Là máy trạng thái trong đặc tả PLC §10, **thu hẹp phạm vi** thành theo-cổng |
| **AGV execution state** | RCS/AGV (WCS chỉ phản chiếu) | Không đổi |
| **Rack/Inventory state** | WCS (`InventoryService` [HT, **không đổi**]) | `FULL/EMPTY/UNKNOWN/IN_TRANSIT`; vị trí; reservation |

Nguyên tắc: một sự kiện của tầng này **không tự động** làm đổi trạng thái tầng khác; mỗi chuyển trạng thái nêu điều kiện (§4.4).

### 4.3. Leg state **[ĐX]**

| Leg state | Ý nghĩa | Chuyển tiếp |
|---|---|---|
| `Planned` | Chặng đã được xác định (nguồn/đích hoặc buffer) nhưng chưa dispatch | → `Dispatching` khi tài nguyên đã khóa và Station Transaction cổng nguồn đủ điều kiện |
| `WaitingForResource` | Chưa có đích/buffer hợp lệ (kể cả "chờ cổng giải phóng") | → `Planned` khi có đích; **không giữ khóa cổng đã hoàn thành** |
| `Dispatching` / `InProgress` | AGV đang thực hiện chặng (RCS) | → `AtSource` … |
| `PickConfirmed` | Rack đã lấy (xác nhận theo Q-MAP-1) | → `InTransit` |
| `InTransit` | Rack đang vận chuyển (`IN_TRANSIT`) | → `DropConfirmed` |
| `DropConfirmed` | Rack đã thả tại điểm kết thúc chặng, tồn kho đã cập nhật | → `Completed` |
| `Completed` | Chặng kết thúc; Station Transaction liên quan đã `Completed` | – |
| `Suspended` / `PendingReconciliation` | Mất kết nối, dữ liệu không nhất quán | → tiếp tục đúng bước hoặc đối soát |
| `Failed` / `Cancelled` | Theo quy tắc hủy/lỗi hiện có | – |

### 4.4. Điều kiện hoàn thành / chờ / lỗi / phục hồi

| Tình huống | Quy tắc đề xuất **[ĐX]** |
|---|---|
| **Station Transaction kết thúc** | Sau khi AMR đã rời cổng (bước 8) và PLC xác nhận (bước 9) — **mốc chính xác Q-REL-1**. Giải phóng cổng; PLC quyết định request kế |
| **Transport Task hoàn thành** | Chỉ khi Leg **cuối** `DropConfirmed` tại **đích cuối cùng** theo BP, hoặc theo kết quả đã duyệt khi BP kết thúc ở buffer. Không bao giờ chỉ vì một Station Transaction kết thúc |
| **Chờ chặng kế** | Transport Task ở trạng thái chờ phù hợp; **cổng cũ đã được giải phóng**; rack ở buffer có vị trí/tồn kho thật; khóa buffer/rack giữ theo `inventory-management.md` §4.5 |
| **Lỗi trong chặng** | Không tự hủy, không tự giải phóng reservation; rack `UNKNOWN`/`IN_TRANSIT`; đối soát theo §4.8 của inventory (Admin xác nhận) |
| **Phục hồi** | Theo đặc tả PLC §11/§12 (đọc IO thật, đối chiếu DB, tiếp tục đúng bước); mở rộng: Leg và Transport Task lưu bền, khôi phục theo Leg đang dở |
| **Thả rack ở buffer** | Tồn kho cập nhật bằng sự kiện `DROP_CONFIRMED` (đã có [BL]); **không** đánh dấu đích khi mới tạo task |

### 4.5. AGV và PER_AGV (xung đột C-3 – chưa quyết)

- Phương án **P-1** (bảo thủ, đúng baseline): AGV được giữ cho đến khi Transport Task kết thúc hoặc đến điểm chờ cho phép; chặng buffer vẫn do cùng task/AGV tiếp tục. Không thể "giải phóng AGV khi rack ở buffer".
- Phương án **P-2**: mỗi Leg là một lần chiếm AGV; giữa các chặng AGV rảnh và có thể nhận task khác; chặng kế được xếp lại hàng đợi `QUEUED`.

P-2 là thay đổi cách hiểu `PER_AGV` ("tối đa một task đang hoạt động", **task** chứ không phải **chặng**). Không tự chọn (Q-AGV-1). Mọi kịch bản ở §7 liên quan nêu rõ phương án.

---

## 5. Quan hệ giữa Station Transaction và 9 bước

- Mỗi lần AMR vào một cổng **có PLC** để lấy/giao ⇒ một Station Transaction theo 9 bước ([XLS], **không đổi thứ tự**).
- **Phân biệt năm mốc [YC2]** (mỗi mốc có điều kiện riêng, mọi mốc ghi lại thời điểm):
  1. *Request được PLC phát* (M9001/M9002 + dữ liệu) – bước 1.
  2. *WCS tiếp nhận và ghi nhận* – `Recorded` (đặc tả PLC §8.4; ack M9101 timing: **OPEN, Q-ORD-1**).
  3. *Station Transaction được xử lý* – các bước 3–7.
  4. *AMR hoàn thành giao nhận và rời cổng* – bước 7–8 (cổng giải phóng: Q-REL-1).
  5. *Transport Task tiếp tục sang chặng kế hoặc hoàn thành* – quyết định ở tầng Task/Leg, **sau** bước 9.
- M9120 (bước 3), mapping bước ↔ pick/drop, và handshake tại **cổng đích** (cổng thứ hai của một task, vd. A1/A2 khi giao rack trống) **vẫn OPEN** (Q-ORD-2, Q-MAP-1, Q-LEG-1).
- Cổng không có PLC (buffer F1–F5, KV…) không có Station Transaction theo 9 bước; vị trí được xác nhận bằng cách khác (WCS inventory/RCS) — **cách xác nhận chưa được baseline nêu rõ** cho từng buffer (Q-BUF-1).

---

## 6. Phân tích xung đột / giới hạn yêu cầu §4

### 6.1. "Một thời điểm chỉ có một task" (tài liệu process cũ)

- Vị trí: `Process in detail.pptx`, **slide 4** (Trạng thái ban đầu), cùng trang với phân công AGV1/AGV2: "Tại một thời điểm chỉ có 1 task được thực hiện". Nó **không** được chép vào `process-flows.md`/`business-rules.md`.
- Thứ tự thẩm quyền (`CLAUDE.md` §1): quyết định nghiệp vụ đã duyệt **BR-01 = PER_AGV, "không áp dụng giới hạn một task cho toàn hệ thống"** đứng **trên** tài liệu nguồn. Do đó câu trên là giả định của bản slide cũ về hệ thống một-task, **được thay thế** bởi BR-01 ở cấp task. Điều chưa biết là liệu chủ dự án có ý giữ một giới hạn nào khác (ví dụ trên **một cổng** hoặc **một khu vực**) — đưa vào Q-SLD-1, không tự suy diễn.
- Theo BR-01, giới hạn "một task" chỉ có thể hiểu là *một task đang hoạt động mỗi AGV*, và **không** mâu thuẫn với "các cổng độc lập" **ở cấp Station Transaction**: hai cổng có thể cùng có Station Transaction đang hoạt động, nhưng task AGV1 (rack đầy) hay AGV2 (rack trống) vẫn tuần tự theo từng AGV.
- Hệ quả logic: hai cổng cùng yêu cầu rack **đầy** (AGV1) ⇒ chỉ một Task chạy; Station Transaction của cổng còn lại **chưa** ở `AgvAssigned`, chờ trong hàng đợi (§7 kịch bản 5/6). Điều này vẫn mở: một Station Transaction "đang hoạt động" có tính khi chỉ mới `Queued` hay chỉ khi AMR đã tới cổng? ⇒ **Q-GATE-2**.
- Không tự bỏ giới hạn; không đổi `PER_AGV`.

### 6.2. Một PLC nhiều cổng, mỗi cổng một transaction

Xem C-1/§4.1. **Không dùng khóa toàn PLC** ngoài phần buộc phải tuần tự do thanh ghi dùng chung (nếu G-C), và khi đó khóa chỉ bao phần ghi/đọc tín hiệu dùng chung, **không** bao trùm cả vòng đời Station Transaction.

### 6.3. Một Transport Task qua nhiều PLC và nhiều cổng

Hỗ trợ ở mức mô hình (Transport Task ⇄ nhiều Leg ⇄ nhiều Station Transaction, mỗi cái thuộc đúng một `PlcId` + một cổng). Khóa chống trùng Station Transaction `(PlcId, Request_ID)` giữ nguyên. Một chặng do WCS khởi tạo ở cổng khác (Q-LEG-1) **chưa** có `Request_ID` do PLC phát; WCS không tự sinh Request_ID.

### 6.4. Chờ chặng kế không giữ khóa cổng đã hoàn thành

Đã nêu §4.4. Điều kiện kèm theo: khóa rack/vị trí (buffer, đích dự kiến) vẫn giữ theo inventory §4.5; khóa **cổng** thuộc Station Transaction nên được giải phóng khi giao nhận xong.

### 6.5. Nhất quán rack khi nguồn → AMR → buffer → đích

Dùng đúng chuỗi sự kiện [BL]: `PICK_CONFIRMED` (rack `IN_TRANSIT`, rời nguồn) → `DROP_CONFIRMED` ở buffer (rack gán vị trí buffer, trạng thái FULL/EMPTY theo loại) → rack ở buffer là **hàng tồn thật** → chặng kế lại `PICK_CONFIRMED` tại buffer → `DROP_CONFIRMED` tại đích. Một rack tối đa một vị trí; không đánh dấu đích `OCCUPIED` khi mới tạo task. **Không sửa `InventoryService`**: việc chuỗi sự kiện này đã đủ cho hai lần pick/drop trong một Task hay không, cần kiểm tra khi thiết kế triển khai (Q-INV-2: `RackStatus`/`CurrentTaskId` giữa hai chặng).

### 6.6. WCS sở hữu tồn kho; không đổi TaskStateMachine/InventoryService

Giữ. Mọi trạng thái Leg/Station Transaction là **lớp điều phối bổ sung**; việc nó có cần thay đổi `TaskStateMachine` (vd. cho C-2) là quyết định riêng cần Change Request riêng.

---

## 7. Kịch bản acceptance (mức đặc tả)

Ký hiệu: TT = Transport Task; Leg; ST = Station Transaction; AGV; Rack/Inv. Với mọi kịch bản: nếu PLC mất kết nối/WCS restart ⇒ §11–12 đặc tả PLC (giữ trạng thái, đọc IO thật, không phát lệnh vật lý mới khi không chắc). "Điều kiện áp dụng" = phụ thuộc quyết định đang mở.

### AT-01 – A lấy hàng, B sẵn sàng, giao trực tiếp *(vd. BP-01 với B1/B2 có chỗ)*
| | |
|---|---|
| **Điều kiện áp dụng** | Q-MAP-1 (xác nhận pick/drop), Q-LEG-1 (handshake tại B1/B2) |
| **TT** | `PENDING→…→EXECUTING→(WAITING_FOR_PLC)→COMPLETED` khi `DROP_CONFIRMED` tại B |
| **Leg** | Leg 1 (A→B): `Planned→InProgress→PickConfirmed→InTransit→DropConfirmed→Completed` |
| **ST** | ST tại A: bước 1→9 `Completed`; (nếu B có PLC & cần: ST tại B theo Q-LEG-1) |
| **AGV** | AGV1: chiếm trong cả Leg; về điểm chờ sau cùng |
| **Rack/Inv** | `PICK_CONFIRMED` (IN_TRANSIT, rời A) → `DROP_CONFIRMED` (gán B, "Không bind rack" tại B1/B2) |
| **Giải phóng cổng** | Cổng A khi ST tại A kết thúc (Q-REL-1); cổng B khi ST tại B kết thúc |
| **Lỗi/mất kết nối** | Mất PLC của A giữa bước: giữ ST, không hủy TT; kết nối lại → tiếp tục đúng bước; không dispatch lại |

### AT-02 – A lấy hàng, B chưa sẵn sàng, chuyển buffer
| | |
|---|---|
| **Điều kiện áp dụng** | C-2, C-3, C-10 (đa chặng/giải phóng AGV), Q-BUF-1; chỉ khi BP cho phép buffer đó. **Lưu ý:** trong luồng đã duyệt (slide 5, 7) thả vào buffer F **kết thúc task**; "task còn chờ chặng kế" là yêu cầu mới (Q-MLEG-2) |
| **TT** | Chờ chặng kế (không `COMPLETED`); không hủy, không tạo task trùng |
| **Leg** | Leg 1 (A→buffer) `DropConfirmed→Completed`; Leg 2 `Planned/WaitingForResource` |
| **ST** | ST tại A `Completed` (cổng A giải phóng); buffer: không có ST 9 bước (cổng không PLC) hoặc theo Q-LEG-1 |
| **AGV** | P-1: vẫn gắn task; P-2: được giải phóng giữa hai chặng (Q-AGV-1) |
| **Rack/Inv** | `DROP_CONFIRMED` tại buffer: vị trí = buffer, trạng thái FULL; reservation đích B giữ/giải theo quy tắc |
| **Giải phóng cổng** | Cổng A được giải phóng khi AMR đã rời |
| **Lỗi** | Không có buffer hợp lệ → xem AT-10; rack bị rơi giữa đường → `UNKNOWN`, đối soát |

### AT-03 – Rack tại buffer, B sẵn sàng, tiếp tục giao
| | |
|---|---|
| **Điều kiện áp dụng** | như AT-02, **và Q-MLEG-2**: với rack **đầy** vào B1/B2 kịch bản này **trùng BP-04 (REVIEW)** ⇒ **không được triển khai**; với rack **trống** chỉ khớp BP-02 (F3/F4→A1/A2) khi **A1/A2 yêu cầu**, không tự động sau khi thả vào buffer |
| **TT** | Tiếp tục từ chờ → thực thi → `COMPLETED` khi `DROP_CONFIRMED` tại B |
| **Leg** | Leg 2 (buffer→B) `Planned→…→Completed`; xác nhận rack **đang ở buffer** trước khi dispatch (đối chiếu inventory) |
| **ST** | ST tại B (nếu cổng B có PLC) theo 9 bước |
| **AGV** | P-1: cùng AGV tiếp tục; P-2: xếp hàng rồi cấp AGV (PER_AGV) |
| **Rack/Inv** | `PICK_CONFIRMED` tại buffer (vị trí buffer được giải phóng) → `DROP_CONFIRMED` tại B |
| **Giải phóng cổng** | Cổng B khi ST tại B kết thúc |
| **Lỗi** | Rack không còn ở buffer (Q-INV-1) → `PendingReconciliation`, **không dispatch** |

### AT-04 – Trong khi TT chờ tại buffer, cổng A nhận request mới
| | |
|---|---|
| **Điều kiện áp dụng** | Q-GATE-1, Q-GATE-2; với P-1 + `PER_AGV`: request mới cùng loại AGV phải `QUEUED` |
| **TT** | TT-1 chờ chặng kế; TT-2 mới được tạo (`QUEUED` nếu AGV bận, hoặc chạy nếu AGV khác/được giải phóng) |
| **Leg** | Leg của TT-1 `WaitingForResource`; Leg 1 của TT-2 độc lập |
| **ST** | ST mới tại A (ST cũ đã `Completed`; cổng A đã được giải phóng) |
| **AGV** | P-1: TT-1 giữ AGV ⇒ TT-2 cùng loại `QUEUED`; P-2: AGV rảnh ⇒ TT-2 chạy |
| **Rack/Inv** | Rack của TT-1 ở buffer, **không** bị TT-2 sử dụng (khóa rack/vị trí) |
| **Giải phóng cổng** | Cổng A rảnh **vì** ST đã kết thúc, không phụ thuộc TT-1 còn chờ |
| **Lỗi** | Request mới trùng Request_ID cũ → coi trùng, không tạo task |

### AT-05 – Hai cổng cùng PLC đồng thời xử lý hai ST
| | |
|---|---|
| **Điều kiện áp dụng** | Chỉ khi Q-GATE-1 chọn G-B hoặc G-C. **Với G-A: không áp dụng** (1 ST / PLC) |
| **TT** | Hai TT độc lập; nếu cùng loại AGV ⇒ tuần tự theo PER_AGV (hai AGV khác loại ⇒ song song) |
| **Leg** | Mỗi TT có Leg riêng |
| **ST** | Hai ST độc lập; trạng thái cổng này không ảnh hưởng cổng kia; riêng thao tác trên tín hiệu **dùng chung** (G-C) phải tuần tự, không khóa cả vòng đời |
| **AGV** | AGV1 + AGV2 song song nếu khác loại |
| **Rack/Inv** | Hai rack khác nhau; không xung đột reservation |
| **Giải phóng cổng** | Mỗi cổng giải phóng riêng |
| **Lỗi** | Mất kết nối PLC: **cả hai** ST cùng `Suspended`; kết nối lại: đối chiếu từng cổng độc lập; không suy ra trạng thái cổng này từ cổng kia |

### AT-06 – Hai cổng thuộc hai PLC khác nhau đồng thời
| | |
|---|---|
| **Điều kiện áp dụng** | Q-GATE-1 (mọi phương án đều cho phép) |
| **TT / Leg / ST** | Hai bộ độc lập; không có khóa/queue chung giữa PLC |
| **AGV** | PER_AGV (khác loại: song song; cùng loại: tuần tự ở tầng task) |
| **Rack/Inv** | Độc lập |
| **Giải phóng cổng** | Riêng từng cổng |
| **Lỗi** | Một PLC mất kết nối **không** ảnh hưởng PLC kia (đúng [HT]) |

### AT-07 – PLC mất kết nối khi AMR đang ở trong cổng
| | |
|---|---|
| **Điều kiện áp dụng** | Timeout/Q-TMO-1, Q-RST-1 TBD |
| **TT** | Giữ nguyên trạng thái; **không** tự hủy (Q-TSK-1: tương tác `StateTimeoutsSeconds`) |
| **Leg** | `Suspended` nhưng không đổi Leg |
| **ST** | `Suspended`; không ghi tín hiệu; không reset |
| **AGV** | AGV/RCS không bị hủy; AGV giữ nguyên chờ |
| **Rack/Inv** | Rack ở `IN_TRANSIT`/vị trí cũ, không đổi; không giải phóng reservation |
| **Giải phóng cổng** | **Không** giải phóng khi mất kết nối |
| **Khi kết nối lại** | Đọc IO thật → đối chiếu DB → tiếp tục đúng bước hoặc `PendingReconciliation`; **không** phát lệnh vật lý mới khi không chắc (đặc tả PLC §11) |

### AT-08 – WCS restart giữa Multi-leg Process
| | |
|---|---|
| **Điều kiện áp dụng** | Dữ liệu Leg/ST/Task đã lưu bền |
| **TT** | Khôi phục từ DB; không tạo task trùng (khóa idempotent) |
| **Leg** | Khôi phục Leg đang dở (vd. Leg 1 `Completed`, Leg 2 `WaitingForResource`) và **không lặp** Leg đã hoàn thành |
| **ST** | ST đang dở: đối chiếu IO thật (đặc tả PLC §12); ST đã `Completed` không lặp |
| **AGV** | Đồng bộ lại với RCS trước khi dispatch lại (3.5/3.7 [BL]: truy vấn trước khi gửi lại) |
| **Rack/Inv** | Không đổi cho đến khi có sự kiện xác nhận; rack ở buffer vẫn là tồn kho thật |
| **Giải phóng cổng** | Chỉ cổng có ST `Completed` được xem là rảnh; ST dở giữ cho đến khi đối chiếu xong |
| **Lỗi** | Không đủ dữ liệu ⇒ `PendingReconciliation` |

### AT-09 – Vị trí rack thực tế không khớp inventory WCS
| | |
|---|---|
| **Điều kiện áp dụng** | Q-INV-1 (ai thắng) – chưa quyết; mặc định đề xuất: không ghi đè |
| **TT** | Chờ đối soát; **không dispatch** |
| **Leg** | `PendingReconciliation` |
| **ST** | `Rejected`/`PendingReconciliation` (lý do `RECON_…`), không phát lệnh vật lý |
| **AGV** | Không được giao nhiệm vụ mới liên quan rack đó |
| **Rack/Inv** | Rack/vị trí `UNKNOWN`/`RECONCILIATION_REQUIRED`; chỉ Admin xác nhận vị trí thực tế mới điều chỉnh (inventory §4.8) |
| **Giải phóng cổng** | Theo kết quả đối soát |
| **Lỗi** | Giữ bằng chứng (snapshot thanh ghi) để Admin đối chiếu |

### AT-10 – Không có vị trí đích hoặc buffer hợp lệ
| | |
|---|---|
| **Điều kiện áp dụng** | Thứ tự ưu tiên đích/buffer `NEEDS_CONFIRMATION` (baseline §1.7) |
| **TT** | `WAITING_FOR_DESTINATION` (hoặc chờ/lỗi cấu hình ở BP-02); không hủy; không tạo task thay thế |
| **Leg** | `WaitingForResource`; **không** chọn vị trí thay thế ngoài danh sách cấu hình + BP |
| **ST** | Nếu ST ở cổng nguồn đã xong: `Completed`; nếu chưa lấy rack: không dispatch lấy rack khi chưa có đích (theo BP-01 baseline rack đã lấy rồi mới chờ) |
| **AGV** | P-1: giữ; P-2: tùy Q-AGV-1 |
| **Rack/Inv** | Không đổi cho đến khi có xác nhận; đánh giá lại khi inventory thay đổi (§1.7 baseline) |
| **Giải phóng cổng** | Theo ST |
| **Lỗi** | Quá hạn chờ: không có timeout nào được tự đặt (TBD) |

### Độ bao phủ

| Yêu cầu | Kịch bản | Ghi chú |
|---|---|---|
| Giao trực tiếp | AT-01 | Cần Q-MAP-1, Q-LEG-1 |
| Buffer & tiếp tục | AT-02, AT-03 | Cần C-2, C-3, C-10, Q-BUF-1, Q-MLEG-2; AT-03 (rack đầy→B1/B2) bị BP-04 chặn |
| Cổng độc lập | AT-04, AT-05, AT-06 | AT-05 phụ thuộc Q-GATE-1 |
| Mất kết nối/restart | AT-07, AT-08 | Timeout TBD |
| Không nhất quán/Không đích | AT-09, AT-10 | Q-INV-1 |
| Không phủ (cố ý) | – | BP-04 (REVIEW), A3/A4, D1/D2 (UNCHANGED), giá trị mã vị trí ngoài A1–A4 |

Các kịch bản này chỉ ở mức đặc tả; **chưa có kiểm thử nào được chạy** (không có triển khai).

---

## 8. Mở rộng so với đặc tả PLC (tóm tắt)

| Nội dung | Thay đổi so với bản nháp ngày 2026-11-17 |
|---|---|
| Đơn vị tuần tự hóa | Từ "một transaction/PLC" sang "một Station Transaction/**cổng**" (phụ thuộc Q-GATE-1; G-A ≡ bản nháp cũ) |
| Quan hệ Task–Transaction | Thêm tầng **Leg**; Transport Task ⇄ nhiều Leg ⇄ nhiều Station Transaction |
| Giải phóng cổng | Khi AMR rời cổng (Q-REL-1), **không** chờ Transport Task |
| Buffer | Vị trí vật lý thật với cập nhật tồn kho và không kết thúc Task |
| Trạng thái | Năm loại tách biệt; không đổi `TaskStateMachine` |

## 9. Câu hỏi mới phát sinh (đã thêm vào `PLC_SPEC_OPEN_QUESTIONS.md`)

Q-GATE-1, Q-GATE-2, Q-REL-1, Q-AGV-1, Q-BUF-1, Q-INV-2, Q-MLEG-1, Q-MLEG-2, Q-SLD-1, Q-LOC-1, Q-PH2-1 (xem tài liệu câu hỏi). Q-SRC-1 đã được giải quyết: hai tệp .pptx đã tìm thấy và đọc.

## 10. Điều chỉnh cần đọc lại trong đặc tả PLC (đã ghi chú tại chỗ)

§9.1 (một transaction/PLC) và §1.1 (PLC Transaction State) nay hiểu là *Station Transaction theo cổng*; trong lúc Q-GATE-1 mở, G-A là nghĩa tương thích với yêu cầu trước đó.

## 11. Tài liệu baseline **không** sửa trong đợt này (theo `CLAUDE.md` §3 bước 5)

Dự kiến cập nhật **sau khi duyệt**: `business-rules.md` (BR-01 làm rõ Task vs Leg vs Station Transaction), `process-flows.md` (thêm khái niệm Leg và, nếu được duyệt, các chặng buffer), `integration-requirements.md` (§5.2/5.3/5.10), `task-state-machine.md` (chỉ nếu C-2 yêu cầu), `ADR-0004` (supersede), `ADR` mới cho mô hình hai cấp, `IMPACT_MATRIX.md`, `REQUIREMENT_TRACEABILITY.md`, `CHANGELOG.md`. Các dòng đề xuất cho Impact Matrix/Traceability nằm trong mục CR-010 của `CHANGE_REQUESTS.md`.
