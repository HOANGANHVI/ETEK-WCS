# AGV Phase 2 – Quy tắc Rack, Buffer và Queue

| | |
|---|---|
| **Trạng thái** | **DRAFT – chờ chủ dự án duyệt (Revision 1.2).** Các phần [QĐ] là quyết định đã duyệt; các phần [ĐX] là diễn giải/đề xuất **cần xác nhận**; các phần [OPEN] chưa quyết. |
| **Căn cứ** | D-02, D-03, D-04, D-06; `docs/specification/inventory-management.md §4.4–4.8`; code `Domain/Inventory/*`, `Domain/Locations/Location.cs`, `RouteEngine.cs`, `RouteConditionEvaluator.cs`, `Repositories.cs` |
| **Lưu ý số liệu** | Mọi con số trong ví dụ §10 là **minh họa giả định**, *không* phải cấu hình hay dữ liệu thật (dung lượng Buffer: OQ-11) |
| **Revision 1** | 2026-11-24 – thêm §13 truy vết quyết định Queue/FIFO/Reservation/chọn Rack; tương tác với process động (đề bài Revision 1 bị cắt – phần này suy từ R1-D/R1-E). Nhãn **[QĐ R1-A…E]** = yêu cầu bổ sung đã duyệt (xem `PHASE2_OPERATIONAL_SPEC.md` §15–§19). Không đổi quyết định D-01…D-08. |
| **Revision 1.1** | 2026-11-24 – **D-09:** PH2-04 (B1/B2 cần Full Rack) là baseline đã chấp thuận → thay V9 bằng các ví dụ V9a–V9d (§10); **D-10:** Process Version ghim tại Queue intake → bản ghi `ProcessVersionPinned`/`NeedsDecision`, Order/Leg truy ngược Queue Request (§13.1, §13.3); append-only theo Spec §16.7 và ghi bền theo §16.8 (không tuyên bố zero-loss). |
| **Revision 1.2** | 2026-11-24 – **D-13:** strict FIFO **theo Queue Ordering Scope** (OQ-01 đóng; §6, §6.1 viết lại, §6.2 Scope mới); **D-12:** Request chỉ vào Queue khi Published + Ready; `Waiting → Held` khi Readiness Suspended, giữ `QueueSequence`, không tự Dispatch khi hồi phục (§7.3); bản ghi `QueueHeld`/`QueueResumed`/`ScopeEvaluation`/`ReadinessRevalidated`, `QueueBypass` bị bỏ (§13.1). Không đổi quy tắc Origin/Reservation. |

## 1. Mô hình dữ liệu Rack [QĐ D-02/D-06]

WCS quản lý **riêng** các thuộc tính sau (không gộp, không thay thế lẫn nhau):

| Thuộc tính | Ý nghĩa | Hiện có trong code [CODE] | Kết luận |
|---|---|---|---|
| **Rack ID** | Định danh Rack. Nguồn lấy ID (barcode/RFID/PLC/RCS/nhập tay): OQ-12 | `Rack.Id` (nội bộ, surrogate) + `ExternalRackId` **tùy chọn** (`Rack.cs:105`; "No business logic may depend on ExternalRackId being present") | **MODIFY** nếu Phase 2 yêu cầu ID bắt buộc (OQ-12) |
| **Full/Empty** | Rack có hàng hay rỗng | Gộp chung vào `Rack.Status` ∈ {Unknown, Full, Empty, InTransit} (`Enums.cs:24`); khi `BeginTransit` Status → `InTransit` và **mất** thông tin Full/Empty trên Rack (chỉ còn `Leg.RackKind`, `TransportOrder.cs:115`) | **MODIFY**: tách `Content (Full/Empty)` khỏi `Status` |
| **Origin Station** | Trạm/luồng nguồn gốc của Rack | **Không có** | **MODIFY** (thêm; migration cộng thêm) |
| **Current Location** | Vị trí hiện tại | `Rack.CurrentLocationId` (null khi InTransit) | **KEEP** |
| **Rack Status** | Trạng thái: trong kho/đang vận chuyển/chưa rõ (cần đối soát) | `Status` (xem trên) | **MODIFY** theo tách Content/Status |
| **Buffer occupancy** | Số Rack đang ở Buffer; slot đã giữ chỗ; slot trống | Suy ra: `racks.CountAtLocationAsync` + `reservations.CountActiveDestinationAsync`; `Location.HasFreeSlot` (`Location.cs:50`), `GetOccupancy` (`:52-57`) | **KEEP** (view); bổ sung báo cáo theo Origin |
| **Reservation** | Khóa Rack nguồn / giữ chỗ slot đích | `Reservation.ForSourceRack/ForDestination` (`Reservation.cs:154-164`); unique ACTIVE theo Rack (`EntityConfigurations.cs:76-77`) | **KEEP** |
| **Lịch sử Pick/Drop/reconciliation** | Nhật ký **không sửa/xóa qua đường nghiệp vụ trong thời gian retention**; hiệu chỉnh = sự kiện mới (Spec §16.7) | `InventoryTransaction` (`Domain/Inventory/InventoryTransaction.cs`) | **KEEP** |

> Ghi chú: `Origin ≠ Current Location` (D-02). Ví dụ: Rack sinh ra từ A1 đang nằm ở P1 có `Origin = A1` và `Current Location = P1`; khi đưa tới M5 thì `Current Location = M5`, `Origin = A1` không đổi.

## 2. Origin

### 2.1 Quy tắc đã duyệt [QĐ D-02]

1. Khi chọn Rack **từ Buffer**: ưu tiên Rack có Origin **phù hợp** với trạm/luồng đang yêu cầu.
2. Trong nhóm Rack phù hợp Origin: **FIFO**.
3. **Không** lấy Current Location thay cho Origin.
4. Không có Rack đúng Origin → **không tự chọn Rack khác Origin**; giữ yêu cầu **chờ** cho đến khi chủ dự án duyệt quy tắc fallback.

### 2.2 Chưa quyết [OPEN]

| Mục | Câu hỏi | Mã |
|---|---|---|
| Định nghĩa "Origin" và "phù hợp" | Khớp đúng Station, theo nhóm (A1/A2), hay bảng cấu hình Origin→Requester? Slide 2 cho thấy M5 là "QC thủ công cho A1, A2", M6 cho X1, X2 (gợi ý quan hệ luồng nhưng **chưa được duyệt** làm quy tắc) | OQ-16 |
| Ai/gì gán Origin | Khi Rack lần đầu xuất hiện (nhập tồn kho ban đầu, PDA, từ PLC trạm A) | OQ-16 |
| Áp Origin cho Rack ở trạm không phải Buffer (B1/B2 trong PH2-02) | D-02 chỉ nêu "từ Buffer" | OQ-16 |
| Origin sau khi Rack rỗng quay về/được tái sử dụng | Giữ hay đổi Origin khi Rack đi qua QC rồi trở lại | OQ-16 |
| Fallback khi không có Rack đúng Origin | Chủ dự án **chưa duyệt** (không được tự cài) | OQ-16/OQ-17 |

### 2.3 Diễn giải đề xuất [ĐX – NEED USER CONFIRMATION]

- Origin được lưu trên Rack, **bất biến qua các Leg** (chỉ đổi qua thao tác có audit); không suy từ Current Location.
- Quan hệ "Origin phù hợp" là **bảng cấu hình** (không hard-code mã trạm – CLAUDE §4).

## 3. Buffer và occupancy

- Buffer P1–P4 là **WCS-managed** [QĐ D-07]: WCS tự quản lý occupancy/tồn kho; không handshake PLC; xác nhận vật lý theo D-05.
- **Slot trống** = `capacity − occupied − reserved` (`Location.cs:50`; `RouteConditionEvaluator.cs:119-120`) [CODE/HOẠT ĐỘNG]; Location chấp nhận Full/Empty theo `AllowedRackKind` (`Location.cs:41`) – Phase 2 cần biết Buffer chứa Full, Empty hay cả hai (OQ-11; slide 4–9 cho thấy P có cả Full và Empty).
- Buffer lưu Rack (cập nhật tồn kho khi Drop xác nhận) **khác** vị trí chờ trung gian của AMR (không lưu Rack, không cập nhật tồn kho) [HOẠT ĐỘNG] `business-rules.md §1.9`.
- Location `ReconciliationRequired` **không** được cấp cho yêu cầu mới (`Location.HasFreeSlot`: `!ReconciliationRequired`; `RouteConditionEvaluator.cs:40,57-60`).

## 4. Reservation

| Loại | Khi tạo | Giải phóng/tiêu thụ | Nguồn |
|---|---|---|---|
| Rack nguồn | Khi chọn Rack cho Leg | Consumed khi Pick xác nhận; Released khi Leg bị từ chối/hủy **trước khi gửi** | [CODE] `RouteEngine.cs:276,586,544-547,661-668` |
| Slot đích | Khi chọn đích (BeforePick) hoặc sau Pick (AfterPick) | Consumed khi Drop xác nhận; Released khi hủy/từ chối | [CODE] `:294-299,331,636` |

Quy tắc [HOẠT ĐỘNG] `inventory-management.md §4.4`: sự cố → **không tự giải phóng** Reservation trước khi đối soát; không đánh dấu đích `OCCUPIED` khi mới tạo task. **Thời điểm giữ chỗ đích** (trước Pick: Rack còn ở nguồn; sau Pick: AMR giữ Rack): **OPEN OQ-18**. Không có hạn giữ chỗ trong code → OQ-19.

## 5. Thuật toán chọn Rack theo Origin + FIFO [ĐX – diễn giải D-02]

Đầu vào: yêu cầu cần Rack loại `K` (Full/Empty) cho trạm yêu cầu `R`; danh sách nguồn ứng viên **theo nhóm ưu tiên** (từ process, vd. B1/B2 rồi P1/P2).

*Áp dụng cho PH2-04 (B1/B2 cần Full Rack, D-09):* `K = Full`; nhóm ưu tiên 1 = **A1/A2** (trạm PLC-managed: chỉ Full Rack phù hợp chưa đặt chỗ; việc áp Origin tại A1/A2: OQ-16), nhóm 2 = **P1/P2** (Buffer: Origin phù hợp + FIFO). **Không bao giờ chọn Empty Rack** cho yêu cầu Full; không có nguồn phù hợp → chờ (vòng kiểm tra A1/A2 → P1/P2), không chọn nguồn/Rack ngoài tập đã nêu.

1. Lần lượt theo **nhóm ưu tiên**; trong nhóm, xét tất cả nguồn ứng viên.
2. Nguồn hợp lệ: Location `Enabled`, không `ReconciliationRequired`; (nếu có PLC) trạm online/sẵn sàng.
3. **Rack đủ điều kiện**: `Content = K`, `Status` = trong kho tại Location đó, **không có Reservation ACTIVE**, không Unknown.
4. **Tập Origin-phù-hợp** = Rack đủ điều kiện có Origin phù hợp với `R` theo quan hệ cấu hình (OQ-16).
5. Nếu tập không rỗng → chọn Rack **cũ nhất theo khóa FIFO** (đề xuất: thời điểm Drop vào Buffer được xác nhận; phạm vi FIFO: OQ-30) → tạo Reservation nguồn.
6. Nếu tập rỗng trong **nhóm hiện tại** → sang nhóm ưu tiên kế (nếu có); nếu hết nhóm → **giữ yêu cầu chờ** [QĐ D-02]. **Không** chọn Rack ngoài tập Origin-phù-hợp (kể cả khi Buffer còn Rack khác Origin).
7. Chọn nguồn/Rack và tạo Reservation trong **một giao dịch DB** (chống cấp trùng; unique ACTIVE theo Rack).

So với code: bước 3 có (`Repositories.cs:59-62`: không Reservation ACTIVE); **bước 4–6 không có**; thứ tự hiện tại là `LastConfirmedAt` tăng dần, `Id` (rack nằm lâu nhất tại Location, **bất kể Origin**) → **REPLACE** phần chọn Rack.

## 6. FIFO – hai tầng, không nhầm lẫn

| Tầng | Nội dung | Căn cứ | Trạng thái |
|---|---|---|---|
| **FIFO Rack** | Trong nhóm Rack Origin phù hợp, Rack vào Buffer sớm nhất ra trước | D-02 | [QĐ]; khóa/phạm vi: OQ-30 |
| **FIFO yêu cầu theo Queue Ordering Scope** | **Strict FIFO trong một Scope**: không Request nào vượt Request đứng trước **trong cùng Scope**; đầu Scope `Blocked` thì các Request sau **chờ**; Scope độc lập chạy song song; **một giao dịch hoạt động/PLC** là ràng buộc khe riêng, **không** phải Scope (§6.2) | D-03, **D-13**, ADR-0011 D-1 | [QĐ D-13]; cấu hình Scope: **OQ-54**; khe PLC giữa Scope: **OQ-55**; mốc kết thúc giao dịch: OQ-02 |
| **FIFO chờ tài nguyên** (đích/nguồn) | Thứ tự các Request cùng chờ một tài nguyên nằm trong Scope chứa tài nguyên đó (strict theo Scope); Request ở Scope khác không bị ràng buộc thứ tự | D-03, D-13 | [QĐ D-13]; phạm vi Scope: OQ-54 |

### 6.1 Strict FIFO theo Scope [QĐ D-13] (thay bảng so sánh Strict/Controlled của Revision 1.1)

Chủ dự án đã chọn **strict FIFO trong từng Queue Ordering Scope** (phương án B). **Controlled FIFO / bypass không được chọn**; không có đường tắt bypass trong một Scope.

| Quy tắc | Nội dung |
|---|---|
| Không vượt hàng | Trong **cùng Scope**, Request có `QueueSequence` lớn hơn **không** được Dispatch trước Request đứng trước nó |
| Đầu Scope bị Blocked | Các Request sau trong Scope **chờ** (ví dụ: A1 chờ đích vì tất cả đầy ⇒ A2 cùng Scope phải chờ, dù A2 cần tài nguyên khác) |
| Scope độc lập | Request thuộc **Scope khác nhau** có thể Dispatch **song song**; không Request nào bị chặn bởi đầu Scope khác |
| Scope chưa xác định | Không đủ dữ liệu Scope → **không Dispatch**, trạng thái lý do `SCOPE_UNDETERMINED` (đề xuất [ĐX]); không tự suy ra thuật toán/cấu hình (OQ-54) |
| Held | Request `Held` giữ `QueueSequence`; hồi phục → trở lại **vị trí FIFO ban đầu**; Held ở đầu Scope có chặn các Request sau không: **OPEN OQ-58** |
| Cùng PLC | Một giao dịch hoạt động/PLC (D-03); hai Scope cùng cần PLC → khe PLC xử lý riêng: **OPEN OQ-55** |

**Ưu/nhược đã biết của quyết định:** đơn giản, dễ chứng minh, công bằng trong Scope; một Request bị chặn (ví dụ không có đích) chặn các Request sau **cùng Scope**. Thông lượng phụ thuộc cách chia Scope (OQ-54).

### 6.2 Queue Ordering Scope [QĐ D-13; cấu hình OPEN OQ-54]

- **Định nghĩa:** tập các Request mà thứ tự FIFO giữa chúng có ràng buộc; thành viên Scope xác định bởi **tài nguyên dùng chung** (Rack nguồn, Buffer/đích, Station, khe PLC…) theo cấu hình được phê duyệt (Spec §22.3).
- **Không mặc định:** **không** coi mỗi PLC là một Scope; **không** coi cả hệ thống là một Scope; **không** giả định hai Request độc lập khi chưa có cấu hình được duyệt.
- **Quan hệ với Station/Rack/Buffer/Đích:** hai Request có **tập tài nguyên giao nhau** thuộc cùng ràng buộc thứ tự; chưa đủ dữ liệu → ghi OQ/phụ thuộc kỹ thuật, **không** bịa thuật toán hay cấu hình (OQ-54).
- **Bảo toàn FIFO qua Held:** `Waiting → Held → Waiting` giữ `QueueSequence`; Request quay về vị trí cũ, không xuống cuối hàng (Spec §22.2). Khóa FIFO và thời điểm cấp `QueueSequence`: OQ-56.
- **Song song:** nhiều Scope độc lập có thể Dispatch đồng thời **nhưng** không vi phạm một giao dịch hoạt động/PLC; PLC độc lập chạy song song khi không có tài nguyên/ràng buộc thứ tự dùng chung.
- **Hiện trạng code:** không có PLC-level FIFO và không có Scope (busy port bị từ chối, không xếp hàng); mọi nội dung trên là **đặc tả mục tiêu**, chưa hiện thực.
## 7. Không có đích khả dụng và điều kiện đánh thức Queue [QĐ D-04]

### 7.1 Quy tắc

Không có đích phù hợp → yêu cầu **ở lại Queue**, chờ đến khi có đích khả dụng; **không** tự hủy, **không** tự báo lỗi nghiệp vụ, **không** tự chuyển đến đích ngoài danh sách/quy tắc đã duyệt. Cảnh báo cho operator (không phải lỗi nghiệp vụ) là thông tin hiển thị; timeout/thông báo: OQ-19.

### 7.2 Điều kiện đánh thức [diễn giải từ D-04 + quy tắc tồn kho – cần xác nhận]

Request ở trạng thái `Waiting` (Spec §22.1; Revision 1.1 gọi `Queued/Waiting`) phải được **đánh giá lại** khi có một trong các sự kiện:

| Nhóm | Sự kiện | Ảnh hưởng |
|---|---|---|
| Có chỗ ở **đích** | Rack được Pick/rời khỏi Buffer hoặc trạm đích (occupied −1, Pick xác nhận) | Slot trống |
| | Reservation đích được Released (Leg bị hủy/từ chối/đối soát xong) | Slot trống |
| | Location đích được bật `Enabled`, tăng capacity, đổi `AllowedRackKind` phù hợp, hoặc gỡ `ReconciliationRequired` | Đích hợp lệ |
| | Trạm đích PLC-managed: PLC online lại; giao dịch hoạt động của PLC kết thúc (tới lượt) | Trạm sẵn sàng |
| | Điều chỉnh tồn kho thủ công (PDA/stock adjust) làm giảm occupancy | Slot trống |
| Có **nguồn** (Rack) | Rack mới vào Buffer (Drop xác nhận) có Origin phù hợp | Có Rack đúng Origin |
| | Reservation nguồn Released; Rack hết đối soát | Rack khả dụng |
| Hệ thống | Hết Maintenance; RCS link phục hồi; WCS restart | Tiếp tục xử lý |
| Readiness | Process Version chuyển `Suspended → Ready` | Request `Held` **không** tự Dispatch: chạy **revalidate** (§7.3) rồi mới về `Waiting` |

**Cơ chế [ĐX]:** đánh thức theo sự kiện (ghi sự kiện cùng giao dịch cập nhật tồn kho) **kết hợp** quét định kỳ an toàn (như `RoutingHostedService` hiện tại, mỗi tick – `RoutingHostedService.cs:22-29`), luôn **kiểm lại điều kiện trong giao dịch** trước khi chiếm tài nguyên (chống race); thứ tự đánh thức theo **`QueueSequence` trong từng Scope** (strict FIFO, §6.1); Request ở Scope độc lập được đánh giá song song.

### 7.3 Held và hồi phục Readiness [QĐ D-12; chi tiết Spec §22.2]

| Bước | Quy tắc |
|---|---|
| Vào Held | Readiness của Process Version → `Suspended` khi Request `Waiting` ⇒ `Waiting → Held`; **không hủy**; giữ `ProcessVersionId` và `QueueSequence`; **không** đổi Rack/tồn kho; không tạo/đổi Reservation (Reservation khi Held: OQ-57) |
| Trong Held | Không Dispatch; không tự chọn Version khác; vẫn hiển thị lý do cho operator |
| Hồi phục | Readiness về `Ready` ⇒ **không** tự Dispatch; **revalidate**: Rack còn hợp lệ, đích còn hợp lệ, PLC, điều kiện vận hành; đạt ⇒ `Waiting` ở **vị trí FIFO ban đầu** (không xuống cuối); không đạt ⇒ ở lại `Held` hoặc `Blocked` theo lý do (ghi nhận) |
| Đã Dispatch/InProgress | **Không** tự hủy, **không** giả định dừng an toàn; xử lý theo trạng thái RCS/AMR thực và quy trình sự cố (Emergency Revocation ≠ Disabled: OQ-52) |

## 8. Nhiều yêu cầu và đồng thời

| Tình huống | Quy tắc | Hiện thực |
|---|---|---|
| Hai yêu cầu cùng tranh slot cuối của một Buffer | Chỉ một yêu cầu giữ được; yêu cầu còn lại tiếp tục chờ (không lỗi) | `Location.Touch` + xmin buộc xung đột và đánh giá lại (`RouteEngine.cs:298`); test `ConcurrencyTests.cs:126` ✔ |
| Hai yêu cầu cùng muốn một Rack | Chỉ một Reservation ACTIVE | unique index (`EntityConfigurations.cs:76-77`) ✔ |
| Nhiều yêu cầu cùng PLC | Một giao dịch hoạt động/PLC (D-03); strict FIFO theo Scope (D-13); khe PLC giữa các Scope: OQ-55 | **Chưa có** (per-port reject; không có Scope) → P2-B, P1-I |
| PLC khác nhau | Song song độc lập | ✔ (`Cr010MultiLegTests.cs:162`) |
| Tránh deadlock | Thứ tự khóa nhất quán (inventory §4.5): Rack → Location (đề xuất) | [ĐX] |
| Yêu cầu trùng (PLC giữ tín hiệu) | Chỉ tạo một yêu cầu | Hiện theo `Request_ID`; giao diện 20/11 không có → OQ-05 |

## 9. Timeout và emergency override – chỉ là Open Questions

- Timeout/expiry yêu cầu trong Queue, cảnh báo operator: **OQ-19** (đề xuất: chỉ cảnh báo, không tự hủy – phù hợp D-04, *chưa duyệt*).
- Ưu tiên khẩn cấp/manual override vượt FIFO: **OQ-20**.
- Fallback Origin: **OQ-16/17**.

## 10. Ví dụ (giả định minh họa – không phải cấu hình)

Giả định chung: `Origin hợp lệ cho X` = quan hệ cấu hình OQ-16; Buffer P1, P2 dung lượng giả định 2 Rack/Buffer; B1/B2 là trạm PLC-managed; không Maintenance, RCS/PLC online. Mỗi ví dụ nêu: chọn gì → Reservation → trạng thái → Rack/Inventory → Queue.

**V1 – A1 có Full Rack, B1/B2 không còn chỗ nhận, Buffer còn chỗ (PH2-01)**  
- Điều kiện: B1, B2 `capacity−occupied−reserved = 0` (không "cần Rack"); P1 trống 1 slot; Rack `R10` (Full, Origin=A1) tại A1.  
- Chọn: nhóm 1 (B1/B2) không dùng được → nhóm 2: P1 (còn slot). Reserve slot P1 (+1 reserved); Reservation nguồn khóa `R10`.  
- Trạng thái: Job InProgress; Leg Ready→InProgress; Station Transaction tại A1 (PLC) mở; RCS Mission gửi.  
- Sau Pick xác nhận: `R10` InTransit, Current Location=null, Origin=A1. Sau Drop xác nhận tại P1 (thủ công có audit): `R10` Current Location=P1, Full, Origin=A1; P1 occupied=1, reserved=0.  
- Queue: không phát sinh.

**V2 – A1 có Full Rack, B1 còn chỗ**  
- Chọn B1 (nhóm 1). Handshake Drop tại B1 theo giao diện PLC được duyệt (khởi tạo: OQ-27). Hoàn tất khi Drop xác nhận (D-05). Buffer P không bị đụng.

**V3 – A1 có Full Rack, tất cả đích đầy (B1,B2,P1,P2,M5 đều không còn slot)**  
- Không có đích → yêu cầu **ở lại Queue** (D-04): không hủy, không lỗi, không đổi đích ngoài danh sách. `R10` vẫn tại A1 hay trên AMR chờ: OQ-18.  
- Khi `R20` rời P1 (Pick xác nhận, occupied−1) → sự kiện đánh thức → đánh giá lại theo thứ tự Queue → nếu tới lượt: reserve P1.

**V4 – A1 cần Empty Rack, lấy từ Buffer (PH2-02), nhiều Rack trong P1**  
- B1/B2 không có Rack Empty khả dụng → nhóm 2: P1/P2. P1 có: `E1` (Empty, Origin=A1, vào lúc t1), `E2` (Empty, Origin=A1, t2), `E3` (Empty, Origin khác, t0 < t1).  
- Tập Origin-phù-hợp = {E1, E2}; **E3 bị loại dù cũ nhất**. FIFO trong tập → chọn `E1`. Reserve `E1`. Pick xác nhận → `E1` InTransit; Drop tại A1 → Current Location=A1, Empty, Origin=A1 (không đổi).

**V5 – A1 cần Empty Rack, Buffer có nhiều Rack khác Origin**  
- P1 và P2 chỉ có Rack Empty Origin khác. Tập Origin-phù-hợp rỗng → **không** chọn Rack khác Origin (D-02); yêu cầu **chờ**; không báo lỗi; không hủy. Khi Rack Origin phù hợp được Drop vào Buffer (sự kiện đánh thức) → chọn theo FIFO. Fallback: OQ-16/17 (chờ duyệt).

**V6 – P1 và P2 đều có Rack Origin phù hợp**  
- P1: `E4` (vào t5); P2: `E5` (vào t3). Nếu FIFO gộp theo nhóm ưu tiên (đề xuất [ĐX], OQ-30) → chọn `E5` (cũ nhất). Nếu FIFO trong từng Buffer theo thứ tự P1→P2 → chọn `E4`. **Chưa quyết.**

**V7 – Hai yêu cầu (A1 và A2) cùng cần Empty Rack, chỉ còn một Rack phù hợp**  
- Thứ tự Queue (strict FIFO theo Scope – D-13): A1 đến trước → được Reservation Rack; A2 **chờ nếu cùng Scope với A1**; nếu A2 thuộc Scope độc lập thì Dispatch song song (Scope: OQ-54). Reservation Rack unique đảm bảo không cấp trùng.

**V8 – M5 yêu cầu Full Rack (PH2-06), P1 có Full Rack Origin phù hợp và không phù hợp**  
- Chọn Full Rack Origin phù hợp theo FIFO; Pick tại P1 và Drop tại M5 đều WCS-managed → xác nhận thủ công có audit. Nếu P1/P2 chỉ có Full Rack sai Origin → chờ (D-02); nếu **không có Full Rack nào**: slide 9 báo lỗi vs D-02 chờ → OQ-17.

**V9a – B1 cần Full Rack, A1/A2 có Full Rack phù hợp (PH2-04, D-09)**
- A2 có Full Rack `F7` phù hợp (chưa đặt chỗ); P1 cũng có Full Rack `F1`. **Chọn từ A1/A2** (nhóm 1 ưu tiên): Reserve `F7`; P1/P2 **không** bị đụng. Pick tại A2 (handshake PLC), Drop tại B1 (PLC). Rack: `F7` InTransit → Current Location = B1, Full, Origin giữ nguyên.

**V9b – A1/A2 không có Full Rack phù hợp; P1 có Full Rack Origin phù hợp và có Empty Rack**
- P1 có: `F1` (Full, Origin phù hợp, vào t1), `F2` (Full, Origin phù hợp, vào t2), `E9` (Empty, vào t0 < t1). **Chọn `F1`** (Origin phù hợp + FIFO); **không** chọn `E9` (Empty) dù cũ nhất. Pick tại P1 (thủ công), Drop tại B1 (PLC).

**V9c – P1/P2 chỉ có Full Rack sai Origin (và Empty Rack)**
- Tập Origin-phù-hợp rỗng → **không** chọn Rack sai Origin, **không** chọn Empty; yêu cầu **ở lại Queue/vòng chờ** (kiểm tra lại A1/A2 → P1/P2); không báo lỗi, không hủy. Khi A1/A2 có Full Rack phù hợp hoặc Rack Origin phù hợp xuất hiện ở P (sự kiện đánh thức) → xử lý.

**V9d – Không có nguồn nào; hoặc tranh chấp Rack với PH2-01**
- Không nguồn: như V9c (nhánh chờ/vị trí chờ của quy trình; vị trí chờ: OQ-14; Rack ở nguồn hay trên AMR: OQ-18).
- Tranh chấp: Full Rack `F7` tại A2 vừa được A2 báo để đi ra (PH2-01) vừa được B1 yêu cầu (PH2-04) → Reservation Rack **duy nhất** (D-06): yêu cầu được xử lý trước theo thứ tự Queue (strict FIFO theo Scope – D-13) giữ `F7`; yêu cầu còn lại chọn nguồn/đích khác theo quy tắc của process mình hoặc chờ.

## 11. Đối chiếu baseline ↔ code

| Nội dung | Baseline | Tài liệu hiện tại | Code hiện tại | Kết luận |
|---|---|---|---|---|
| Thuộc tính Rack tách riêng | D-02/D-06 | Handoff 03-5 | Full/Empty gộp vào Status; không Origin | **MODIFY** |
| Chọn Rack theo Origin + FIFO | D-02 | Handoff 03-2 | Rack cũ nhất theo `LastConfirmedAt`, không Origin | **REPLACE** |
| Không chọn Rack khác Origin | D-02 | — | Không có | **REPLACE/MODIFY** |
| Reservation Rack/đích | D-06 | inventory §4.5 | Có, unique Rack | **KEEP** |
| Giữ chỗ đích kéo dài/timeout | OPEN | — | Không hạn | **OPEN** OQ-19 |
| Buffer occupancy | D-06/D-07 | — | Suy ra từ count | **KEEP** (view) |
| Queue bền + FIFO/PLC | D-03 | ADR-0011 D-1 | Không có | **REPLACE** |
| Đánh thức Queue | D-04 | `task-state-machine.md §3.6` | Quét mỗi tick | **MODIFY** |
| Đối soát Location/Order CR-010 | D-06/D-08 | inventory §4.8 | Thiếu lối thoát (`InventoryService.cs:251` chỉ legacy) | **MODIFY** (OQ-15) |

## 12. Open Questions liên quan

OQ-11, OQ-12, OQ-15, OQ-16, OQ-17, OQ-18, OQ-19, OQ-20, OQ-26, OQ-30, **OQ-54, OQ-55, OQ-56, OQ-57, OQ-58** (đầy đủ: `PHASE2_OPERATIONAL_SPEC.md` §12). **OQ-01 đã đóng (D-13).**

---

## 13. Truy vết quyết định Queue, FIFO, Reservation và chọn Rack [QĐ R1-D]

> Phần này suy từ yêu cầu R1-D ("Queue, FIFO và Reservation decisions" phải truy vết được) và R1-E (gói chẩn đoán gồm "Queue and reservation decisions", "Rack inventory history"). Đề bài Revision 1 bị cắt trước khi nêu riêng tài liệu này; nội dung dưới đây là diễn giải cần xác nhận.

### 13.1 Bản ghi quyết định (không sửa/xóa qua đường nghiệp vụ trong retention – Spec §16.7; tầng 1, ghi bền theo Spec §16.8; không tuyên bố zero-loss)

Mỗi quyết định ghi **cùng giao dịch DB** với thay đổi tồn kho/Reservation/Queue (outbox), kèm `CorrelationId`, `OrderId`, `LegId`, `StepId`, `QueueRequestId`, `RackId`, `LocationId`, `Seq`, `At`.

| Loại quyết định | Nội dung bắt buộc | Mục đích điều tra |
|---|---|---|
| `QueueEnqueued` | Thời điểm WCS **nhận** (khóa FIFO), nguồn yêu cầu (PLC/PDA), PLC/trạm, vị trí trong hàng | Thứ tự đến |
| `ProcessVersionPinned` | **Process ID + Process Version + content hash** được ghim tại intake (D-10), thời điểm, cách chọn (tự động/mã tường minh/quyết định operator) | Truy ngược Order/Leg về Version đã ghim |
| `QueueNeedsDecision` / `QueueDecisionResolved` | Lý do chưa xác định process (mơ hồ/không khớp); sau giải quyết: người, thời điểm, process được chọn, **Version được dùng**, lý do | Audit giải quyết `NeedsDecision` (không gán latest) |
| `QueueBlocked` | Lý do (không đích/không nguồn/PLC có giao dịch hoạt động/Rack sai Origin…), điều kiện chờ | Vì sao yêu cầu chưa chạy (D-04) |
| `QueueWake` | Sự kiện đánh thức (`§7.2`), danh sách yêu cầu được đánh giá lại và thứ tự | Đánh thức có đúng thứ tự |
| `QueueActivated` | Yêu cầu được cấp lượt (PLC), lý do chọn, **các yêu cầu đứng trước/sau** | Kiểm FIFO/chống vượt hàng |
| `QueueBypass` *(Revision 1.2: **không còn** – D-13 strict FIFO, không bypass trong Scope)* | — | Loại bỏ; nếu thấy sự kiện vượt hàng trong cùng Scope là **lỗi** cần điều tra |
| `RackSelection` | Yêu cầu (trạm, loại Rack), **danh sách ứng viên theo thứ tự** với **kết quả từng ứng viên và lý do loại** (Reservation ACTIVE, sai Origin, sai loại, bất định/đối soát, Location tắt), tập Origin-phù-hợp, **khóa FIFO của từng Rack**, Rack được chọn | Vì sao chọn Rack này mà không phải Rack khác (D-02) |
| `OriginMatchEvaluation` | Origin của Rack, quan hệ "phù hợp" đã dùng (phiên bản cấu hình – OQ-16), kết quả | Kiểm D-02 (không dùng Current Location thay Origin) |
| `DestinationSelection` | Ứng viên theo nhóm ưu tiên, **số liệu tại thời điểm quyết định** (`capacity`, `occupied`, `reserved`, slot trống), đích được chọn/loại | Vì sao đích đầy/trống |
| `ReservationCreated/Consumed/Released` | Loại (Rack nguồn/slot đích), lý do, liên kết Leg, thời điểm | Vòng đời giữ chỗ; chứng minh không giải phóng khi bất định (D-06) |
| `ReservationConflict` | Hai yêu cầu tranh một slot/Rack; bên thắng/thua; cơ chế (xmin/unique) | Phân tích race |
| `QueueHeld` / `QueueResumed` | Lý do Suspended, `QueueSequence`, `ProcessVersionId`; khi hồi phục: kết quả revalidate (Rack/đích/PLC/điều kiện), vị trí FIFO được khôi phục | Chứng minh Held không đổi thứ tự/tồn kho, không tự Dispatch |
| `ReadinessCheck` | Kết quả kiểm tra Published + Ready lúc intake/pin/revalidate (đạt/không, lý do, phiên bản Process) | Vì sao Request được/không được nhận vào Queue |
| `ScopeEvaluation` | Scope của Request, thành viên Scope và thứ tự, Request đầu Scope, lý do chờ (`BlockedByHead`, `SCOPE_UNDETERMINED`…), tài nguyên giao nhau đã dùng | Kiểm strict FIFO, chống vượt hàng, giải thích Scope |

### 13.2 Tái hiện quyết định

Mỗi `RackSelection`/`DestinationSelection` lưu **dữ liệu đầu vào tại thời điểm** (đếm occupancy/reservation, thời điểm Rack vào Buffer, Origin, cấu hình quan hệ Origin, **Process Version + hash** đã ghim) để dựng lại và kiểm chứng sau này mà **không** phụ thuộc trạng thái hiện tại.

### 13.3 Process động và quy tắc Rack/Buffer

- Process **mới** do người dùng tạo (ví dụ A1 → X1) dùng **cùng** quy tắc Rack/Buffer/Queue: Origin + FIFO khi nguồn là Buffer; Queue khi không có đích (D-02, D-04); đặt chỗ nguồn/đích (D-06). Quy tắc **không** đổi theo process.
- Danh sách ứng viên nguồn/đích và thứ tự ưu tiên lấy từ **Process Version đã ghim tại Queue intake (D-10)**; quyết định ghi kèm version + hash (không đọc cấu hình sống). Version mới Publish trong lúc yêu cầu chờ **không** đổi danh sách này.
- Validation khi phê duyệt process kiểm: Buffer tồn tại/bật/tương thích loại Rack/có capacity (Spec §15.6); Origin-rule hợp lệ (OQ-16).
- Hai process Published khớp cùng yêu cầu bị chặn (Spec §15.7) nên không phát sinh hai cách chọn Rack khác nhau cho cùng yêu cầu.

### 13.4 Lịch sử tồn kho phục vụ gói chẩn đoán

Gói chẩn đoán (Spec §18) phải gồm, theo `RackId` và `LocationId`: lịch sử Pick/Drop/reconciliation (`inventory_transactions`) **kèm Origin** (khi thuộc tính Origin có), Current Location theo thời gian, Reservation, Buffer occupancy theo thời gian trong khoảng chọn. Không ghi đè lịch sử.

### 13.5 Đối chiếu code

| Nội dung | Code hiện tại | Kết luận |
|---|---|---|
| Sự kiện Queue/Wait | `routing_events`: `LEG_QUEUED`, `ORDER_WAITING`, `WAITING_FOR_DESTINATION` (`RouteEngine.cs:481,503,315,345`) – chuỗi mô tả, không cấu trúc ứng viên/lý do loại | **MODIFY** (bản ghi quyết định có cấu trúc) |
| Chọn Rack | Không ghi ứng viên/lý do loại; chọn Rack cũ nhất theo `LastConfirmedAt` | **REPLACE** logic + thêm bản ghi |
| Reservation | Bảng `reservations` (Active/Consumed/Released, có thời điểm) | **KEEP**; thêm bản ghi lý do/xung đột |
| Giữ lịch sử tồn kho | `inventory_transactions` (có nguồn xác nhận) | **KEEP**; thêm Origin |
| Queue bền + khóa FIFO | Không có | **REPLACE** (D-03) |

## 14. Open Questions liên quan Revision 1

OQ-16, OQ-19, OQ-30, OQ-34, OQ-39, OQ-41, **OQ-54…58** (đầy đủ: `PHASE2_OPERATIONAL_SPEC.md` §12). OQ-01 đã đóng (D-13).

## 15. Revision 1 – thay đổi trong file này

Thêm: dòng Revision (đầu file); §13 truy vết quyết định Queue/FIFO/Reservation/chọn Rack, tái hiện quyết định, tương tác với process động, lịch sử tồn kho cho gói chẩn đoán. **Không đổi** §1–§12 (quy tắc Origin/FIFO/Queue/đánh thức và các ví dụ V1–V9).
## 16. Revision 1.1 – thay đổi trong file này

| Mục | Thay đổi | Issue/Decision |
|---|---|---|
| Đầu file | Hàng Revision 1.1 | — |
| §5 | Ghi chú áp dụng cho PH2-04: chỉ Full Rack, không Empty | Decision 1 |
| §10 | V9 (cũ: "không đặc tả – REVIEW") → **V9a–V9d** | Decision 1 |
| §1 | Lịch sử Pick/Drop: không sửa/xóa qua đường nghiệp vụ trong retention | Issue 3 |
| §13.1, §13.3 | `ProcessVersionPinned`, `QueueNeedsDecision`/`QueueDecisionResolved`; Version ghim tại intake | Decision 2; Issue 6 |
## 17. Revision 1.2 – thay đổi trong file này

| Mục | Thay đổi | Decision |
|---|---|---|
| Đầu file | Trạng thái, hàng Revision 1.2 | — |
| §6 | Hàng FIFO theo Scope (thay FIFO theo PLC); đóng OQ-01 | D-13 |
| §6.1 | Thay bảng Strict/Controlled bằng quy tắc strict FIFO theo Scope | D-13 |
| §6.2 | **Mới:** Queue Ordering Scope (định nghĩa, không mặc định, bảo toàn FIFO qua Held, song song, hiện trạng) | D-13 |
| §7.2 | `Waiting` thay `Queued/Waiting`; thứ tự theo Scope; sự kiện Readiness Suspended→Ready | D-12, D-13 |
| §7.3 | **Mới:** Held và hồi phục (revalidate, giữ vị trí FIFO, Reservation OPEN) | D-12 |
| §8, §10 | Hàng PLC; ví dụ V thứ tự Queue theo Scope | D-13 |
| §13.1 | Thêm `QueueHeld`/`QueueResumed`, `ReadinessCheck`, `ScopeEvaluation`; bỏ `QueueBypass` | D-12, D-13 |
| §12, §14 | Danh sách OQ | — |
