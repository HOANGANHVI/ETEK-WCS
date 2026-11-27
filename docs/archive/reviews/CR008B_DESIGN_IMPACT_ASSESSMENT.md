# CR-008B – Design & Impact Assessment

**Ngày:** 2026-11-16 · **Loại:** DESIGN & IMPACT ASSESSMENT (không triển khai) · **Nguồn:** `docs/archive/reviews/CR008A_POST_IMPLEMENTATION_REVIEW.md`
**Baseline:** `develop` = `3626812`, `main` = `d5d692f`. Không sửa mã, không migration, không commit/push, không chạy test, không đụng `wcs_db`.

Nhãn: **FACT** (đọc mã/tài liệu, có đường dẫn), **INFERENCE** (suy luận), **NEEDS_DECISION** (cần chủ dự án), **NOT VERIFIED**.
Mọi "đề xuất" dưới đây chỉ là ý kiến để chủ dự án cân nhắc; **không phương án nào được chọn thay chủ dự án**.

## Mục lục
0. Tóm tắt và hiệu chỉnh các nhận định trước
1. REV-001 – `CancelPending` hết giới hạn retry
2. REV-002 – Yêu cầu PLC bị ACK khi guard từ chối
3. REV-003 – Rack/Inventory khi Completed/Interrupted
4. REV-004 – Điều kiện trả AGV
5. REV-005 và REV-006 – Idempotency RCS và thời gian retry thực tế
6. Ràng buộc regression
7. Danh sách file/module/method dự kiến thay đổi
8. Test case cần bổ sung
9. Chia phạm vi thành các CR nhỏ
10. DECISIONS REQUIRED

---

## 0. Tóm tắt và hiệu chỉnh

Trong khi phân tích, đối chiếu lại mã phát hiện **ba điểm cần hiệu chỉnh** so với tài liệu CR-008A / review trước:

| # | Hiệu chỉnh | Bằng chứng |
|---|---|---|
| H-1 | ADR-0007 và báo cáo CR-008A nói yêu cầu PLC **không bị ack sẽ được PLC "trình lại"**. **Điều này không đúng với `PlcStationSignalService` hiện tại**: dịch vụ dựa trên *cạnh lên* (`wasHigh`/`_high`), và `_high[key]` được cập nhật ngay khi poll. Một yêu cầu đã được trả ra một lần sẽ **không bao giờ được trả lại** chừng nào bit PLC còn bật; `AcknowledgeRequestAsync` chỉ ghi log (không ghi `Request_Ack`). Vì vậy việc ack hay không ack **không làm PLC gửi lại** – yêu cầu bị mất ở cả hai trường hợp | `WCS.Application/Plc/PlcStationSignalService.cs` (`PollRequestsAsync`: `if (!high \|\| wasHigh) continue;`; `AcknowledgeRequestAsync` chỉ `events.Log`) |
| H-2 | Review CR-008A viết hạn mức 30 lần ≈ 30 phút. Tính lại: các lần thử cách nhau 2, 4, 8, 16, 32 rồi 60 s ⇒ lần thứ 30 xảy ra sau **≈ 1 502 s ≈ 25 phút**; nhánh "hết hạn mức" của continue/cancel được khai báo **ngay tick sau lần thử thứ 30** (không chờ khoảng backoff cuối) | `RcsRetryPolicy.DelayAfter`; `TaskOrchestrator.ReconcileCancelPendingAsync`/`SendContinueAsync` (kiểm tra `>= RcsMaxAttempts` đặt **trước** kiểm tra `IsDue`) |
| H-3 | Nhánh `NotFound` ×2 → `Cancelled` (CR-008A) kết thúc task **Cancelled** mà RCS **chưa xác nhận hủy**. Spec `task-state-machine.md §3.9`: "Chỉ đánh dấu CANCELLED khi RCS xác nhận hủy **hoặc có cơ chế đối soát tương đương**". Cơ chế "2 lần NotFound liên tiếp" chưa được chủ dự án xác nhận là tương đương | `TaskOrchestrator.ReconcileCancelPendingAsync` (case `NotFound`); spec §3.9 |

Ngoài ra (FACT): **không có thao tác "đối soát" nào trên giao diện** – endpoint `POST /api/tasks/{id}/reconcile` (Admin) tồn tại (`Endpoints.cs`), nhưng `api/client.ts` không có hàm gọi và `TasksPage.tsx` chỉ hiện thông báo "Cần đối soát vị trí rack thực tế…". Admin hiện chỉ đối soát được bằng gọi API trực tiếp.

---

## 1. REV-001 – `CancelPending` hết giới hạn retry

### 1.1 Luồng xử lý hiện tại (FACT)
1. Admin hủy (`POST /tasks/{id}/cancel` → `CancelTaskAsync`) hoặc `EnforceTimeout` (đang tắt) → task `CancelPending`; `TransitionTo` đặt `RcsRetryCount=0`. `SendCancelToRcsAsync(registerAttempt:true)` → count 1.
2. Mỗi tick scheduler gọi `ReconcileCancelPendingAsync`: nếu `RcsRetryCount ≥ RcsMaxAttempts` (30) → ghi `RCS_CANCEL_RETRY_EXHAUSTED` **một lần**, `RequireReconciliation`, rồi **`return null` ở mọi tick sau**; ngược lại chờ đến hạn, tăng bộ đếm, hỏi RCS và xử lý theo trạng thái.
3. Lệnh Cancel lặp lại khi task đã `CancelPending` (`CancelTaskAsync`, `case CancelPending`) chỉ ghi `CANCEL_RETRY` và gửi cancel thêm một lần (`registerAttempt:true`); **bộ đếm không reset**, đánh dấu "exhausted" vẫn còn.
4. `ReconcileTaskAsync` (`InventoryService.cs:211`) chỉ chạy khi `task.IsTerminal && RequiresReconciliation`.

### 1.2 Trạng thái sau khi hết retry (trả lời câu 1–2)

| Đối tượng | Trạng thái |
|---|---|
| Task | `CancelPending` (không terminal), `RequiresReconciliation=true`, lịch sử có `RCS_CANCEL_RETRY_EXHAUSTED` |
| AGV | Bị chiếm (`CancelPending` ∈ `AgvOccupyingStates`); PER_AGV chặn mọi task khác của AGV đó (task mới chỉ vào `Queued`); AGV còn lại vẫn chạy |
| Reservation | Mọi reservation `Active` của task được giữ (rack nguồn nếu chưa pick; ô đích nếu đã giữ) |
| Rack / Location | Không đổi (rack ở nguồn nếu chưa pick; nếu đã pick: `Unknown` + hai vị trí `ReconciliationRequired` theo đường có sẵn) |
| Tự thoát? | Không: timeout tắt; reconcile cần terminal; cancel lặp không reset. **Chỉ có thể sửa trực tiếp DB** |
| Bền qua restart | Có: `RcsRetryCount`, `RcsLastAttemptAt`, `RequiresReconciliation`, dòng lịch sử đều trong DB |

### 1.3 Câu hỏi cần làm rõ

| # | Câu hỏi | Phân tích |
|---|---|---|
| 3 | Điều kiện Admin được phép đối soát | Hiện: task phải **terminal** và `RequiresReconciliation`. Task `CancelPending` hết hạn mức chưa terminal ⇒ chưa được đối soát. Mọi phương án dưới đây cần một bước "kết thúc có kiểm soát" trước khi đối soát |
| 4 | Cần API/UI riêng? | **UI đối soát chưa tồn tại** (xem mục 0) nên cần dù chọn phương án nào. API `reconcile` đã có (Admin, có audit qua `Audited.RunAsync`). Cần thêm: (a) hành động cho `CancelPending` hết hạn mức (phương án A/B), (b) hộp thoại đối soát ở UI (chọn vị trí thực tế, ghi chú) |
| 5 | Khi nào được retry lại Cancel? | Đề xuất điều kiện: Admin yêu cầu tường minh **và** truy vấn RCS vừa thành công (không `Unknown`) hoặc Admin ghi nhận đã liên hệ RCS; mỗi lần retry thủ công bắt đầu một chuỗi có hạn mức mới (reset `RcsRetryCount`, xoá dấu "exhausted") và được ghi audit + lịch sử |
| 6 | Khi nào giữ nguyên chờ xác minh RCS? | Khi RCS báo `Created/Executing/Cancelling` (robot còn chạy), hoặc `Unknown` kéo dài: **không** được đóng task, vì rack/AGV đang thực sự bị điều khiển bởi RCS |
| 7 | Tránh giải phóng nhầm rack / giao task cho AGV khi chưa an toàn | Tách làm hai bước bắt buộc: **(1) đóng task** (terminal, giữ reservation, giữ cờ đối soát), **(2) đối soát** (Admin nêu vị trí thực tế; hệ thống kiểm sức chứa/loại rack – logic có sẵn). Việc có trả AGV ở bước (1) hay không là REV-004 |
| 8 | Bền qua restart | Mọi lựa chọn dùng cột/ lịch sử sẵn có hoặc 1 cột mới; **không dùng bộ nhớ** |

### 1.4 Phương án

**Phương án A – "Cho phép thử lại" (re-arm), task vẫn `CancelPending`**
- Admin bấm "Thử hủy lại" → reset bộ đếm + xoá cờ exhausted + ghi audit/lịch sử; chuỗi retry mới.
- Ưu: nhỏ; không đổi bảng chuyển trạng thái; không đóng task khi chưa biết số phận robot; không cần migration (reset bộ đếm bằng phương thức domain).
- Nhược: nếu RCS **vĩnh viễn** không trả lời hoặc mất bản ghi, task vẫn kẹt, AGV vẫn bị chiếm; chỉ dời vấn đề.
- Rủi ro: lặp vô hạn do thao tác tay; cần giới hạn/ghi nhận số lần Admin retry.

**Phương án B – "Đóng có kiểm soát" (operator close), đi qua cạnh `CancelPending → Failed` sẵn có**
- Admin chọn "Đóng chờ đối soát" với **lý do bắt buộc** và xác nhận đã kiểm tra hiện trường/RCS → `FinishAsync(Failed, "OPERATOR_CLOSED_AFTER_RETRY_LIMIT", forceUncertain:true)`: terminal, `RequiresReconciliation`, reservation **giữ**, rack `Unknown` + vị trí `ReconciliationRequired` nếu đã pick (hoặc theo REV-003), rồi dùng `reconcile` có sẵn.
- Ưu: có đường thoát hoàn chỉnh; không đổi `TaskStateMachine` (cạnh đã có); mọi thứ có audit.
- Nhược: nếu RCS **thật sự vẫn chạy task**, WCS đã đóng → RCS và WCS lệch; cần REV-004 (giữ AGV) để không giao task mới cho AGV đang chạy.
- Rủi ro: thao tác nhầm; cần xác nhận hai bước, quyền Admin, lý do bắt buộc, cảnh báo nếu truy vấn RCS gần nhất cho thấy `Executing`.

**Phương án C – "A rồi B" (khuyến nghị để chủ dự án cân nhắc)**
- Hết hạn mức → Admin được **thử lại** (A) khi RCS trả lời được; chỉ khi RCS không thể xác minh hoặc đã xác nhận ngoài hệ thống mới được **đóng** (B). Có hai hành động, hai cấp xác nhận.
- Ưu: an toàn nhất; Nhược: nhiều việc hơn (2 API + UI), nhiều test.

**Phương án D – Tự động leo thang bằng timeout** – cần `StateTimeoutsSeconds` đã bị loại (chưa được phê duyệt) → **không xét**.
**Phương án E – Chỉ cảnh báo + hướng dẫn sửa DB** – giữ nguyên rủi ro REV-001; không khuyến nghị.

### 1.5 Tác động
- **AGV:** A giữ AGV bị chiếm đến khi RCS trả lời; B giải phóng task (AGV có được trả hay không do REV-004).
- **Rack/Inventory:** A không đổi; B giữ reservation và dựa trên REV-003 để đánh dấu không chắc chắn.
- **PLC:** không ảnh hưởng (không dùng tín hiệu PLC).
- **Module có thể thay đổi:** `TaskOrchestrator.CancelTaskAsync` (nhánh `CancelPending`), `ReconcileCancelPendingAsync`, thêm phương thức mới (vd. `CloseCancelPendingAsync`), `TransportTask` (reset bộ đếm), `Endpoints.cs` (1–2 endpoint Admin), frontend (`client.ts`, `TasksPage.tsx`/`TaskDialogs.tsx`, `vi.ts`). Không cần đổi `TaskStateMachine` (cạnh `CancelPending → Failed/Cancelled/Completed` đã có).

---

## 2. REV-002 – Yêu cầu PLC bị ACK khi Production guard từ chối

### 2.1 Luồng hiện tại (FACT)
`SchedulerHostedService` → `StationRequestProcessor.RunOnceAsync`:
1. `stations.PollRequestsAsync()` (danh sách yêu cầu; `PlcStationSignalService` phát hiện **cạnh lên** của `Pickup_Request`/`Delivery_Request`, khoá idempotency `PlcId:Station:Request_ID`, bỏ qua nếu `Request_ID` chưa đọc được).
2. Với từng yêu cầu: `CreateTaskAsync` → nếu thành công `AcknowledgeRequestAsync`.
3. `catch (AppException)` (mọi lỗi nghiệp vụ, **kể cả `RCS_NOT_CONNECTED`**) → ghi cảnh báo **và ack**.
4. `catch (Exception)` (CR-008A) → ghi log, **không ack**.
5. `AcknowledgeRequestAsync` của `PlcStationSignalService` **không ghi `Request_Ack`** (chưa có quy tắc handshake được duyệt) – chỉ ghi log `REQUEST_HANDLED`. `StationSignalSimulator` đặt vào hàng đợi `_acknowledged` (đã bị lấy khỏi `_requests` từ lúc poll).

### 2.2 Vấn đề
- `RCS_NOT_CONNECTED` là lỗi **tạm thời của hạ tầng** nhưng bị đối xử như từ chối nghiệp vụ vĩnh viễn → ack → yêu cầu coi như đã xử lý.
- Quan trọng hơn (H-1): **không ack cũng không làm PLC gửi lại**, vì dịch vụ chỉ nhìn cạnh lên và đã ghi nhận trạng thái "cao". ⇒ Sửa đúng phải làm cho **chính `PlcStationSignalService` (và simulator) giữ yêu cầu chưa được chấp nhận** cho đến khi được ack.
- Thời điểm ack hiện tại (sau khi `CreateTaskAsync` commit) là đúng yêu cầu "không ack trước khi WCS chấp nhận".

### 2.3 Idempotency và Request_ID (FACT/INFERENCE)
- `RequestKey = PlcId:Station:Request_ID` (không phân tích Request_ID); `CreateTaskAsync` trả task cũ (`Created=false`) nếu khoá đã tồn tại ⇒ gửi lại nhiều lần **không tạo task trùng**; processor đếm là đã xử lý và ack.
- Sau khi WCS khởi động lại, `_high` rỗng: nếu bit PLC vẫn cao sẽ có cạnh lên giả ⇒ cùng `Request_ID` ⇒ idempotent (an toàn).
- Rủi ro cần xác nhận với kỹ sư PLC (NEEDS_CONFIRMATION): `Request_ID` có **duy nhất theo thời gian** hay có thể quay vòng/tái sử dụng? Nếu tái sử dụng, một yêu cầu mới có thể bị coi là trùng với task cũ (bị bỏ qua im lặng).

### 2.4 Phương án
- **A – Giữ yêu cầu chờ trong dịch vụ tín hiệu (in-memory) đến khi ack**: `PlcStationSignalService` giữ từ điển `RequestKey → StationRequest` đã phát ra nhưng chưa ack; `PollRequestsAsync` trả cả yêu cầu mới và yêu cầu chờ (đến hạn thử lại, có backoff); `AcknowledgeRequestAsync` xoá khỏi từ điển. Processor chỉ ack (a) thành công/trùng, (b) từ chối nghiệp vụ **vĩnh viễn** (danh sách mã: `FLOW_NOT_SUPPORTED`, `FLOW_IN_REVIEW`, `STATION_NOT_FOUND`, `STATION_NOT_ALLOWED`…); lỗi tạm thời (`RCS_NOT_CONNECTED`, `DataConflict`, ngoại lệ lạ) **không ack** → thử lại. Restart: mất từ điển nhưng PLC level + `Request_ID` tái tạo yêu cầu (an toàn nhờ idempotency).
- **B – Không poll khi RCS chưa kết nối**: processor kiểm tra `IRcsDispatchGuard` trước `PollRequestsAsync`; do `_high` chỉ cập nhật trong poll nên cạnh lên được phát hiện ngay khi RCS sẵn sàng. Rất nhỏ, nhưng chỉ xử lý `RCS_NOT_CONNECTED`, không xử lý lỗi tạm thời khác.
- **C – Hộp thư yêu cầu bền vững (DB)**: bảng `station_requests`; chắc chắn qua restart nhưng nặng, cần migration, và PLC level đã cho phép tái tạo nên **không cần** ở giai đoạn này.
- **D – Giữ nguyên + thêm mã `RCS_NOT_CONNECTED` vào danh sách không ack (chỉ log)**: không giải quyết H-1 (yêu cầu vẫn mất do không còn cạnh lên).

Có thể kết hợp **B + A**: B ngăn mất yêu cầu khi RCS chưa sẵn sàng; A xử lý lỗi tạm thời khác.

**Không đổi handshake PLC:** cả bốn phương án giữ `Request_Ack` không được ghi (chỉ "ack nội bộ WCS"). Hành vi PLC khi không nhận `Request_Ack` (giữ bit cao? tự hết hạn?) là **NEEDS_CONFIRMATION**.

### 2.5 Tác động và module
- **PLC/handshake:** không đổi; chỉ thay đổi cách WCS nhớ yêu cầu chưa chấp nhận.
- **Task:** không tạo task trùng (khoá cố định). **Module:** `StationRequestProcessor.RunOnceAsync` (phân loại lỗi), `PlcStationSignalService` (`PollRequestsAsync`, `AcknowledgeRequestAsync`, từ điển chờ), `StationSignalSimulator` (đối xứng cho test), có thể `IStationSignalService` (giữ nguyên chữ ký nếu dùng A/B).
- Hiện chưa có tác động vận hành vì `Plc:Integration:Enabled=false` và chưa có `StationFlows` được duyệt.

---

## 3. REV-003 – Rack và Inventory khi Completed/Interrupted

### 3.1 Mô hình hiện có (FACT) – không cần thêm trạng thái mới
| Khái niệm | Biểu diễn hiện có |
|---|---|
| Vị trí do WCS ghi nhận | `Rack.CurrentLocationId`, `Rack.Status` (`Full/Empty/InTransit/Unknown`) |
| Vị trí vật lý **đã xác minh** | Không có trường riêng; được thể hiện bằng `InventoryTransaction` nguồn `Operator`/`WcsReconciliation` và `Rack.LastConfirmedAt` |
| Reservation | `Reservation` (`SourceRack`/`DestinationLocation`, `Active/Consumed/Released`) |
| Không chắc chắn | `RackStatus.Unknown`; `Location.ReconciliationRequired` (chặn cấp slot, `GetOccupancy=Unknown`); `TransportTask.RequiresReconciliation` |

Spec `inventory-management.md §4.8`: khi "task interrupted, AGV dừng giữa đường hoặc không rõ kết quả" phải (1) đánh dấu rack/location `UNKNOWN`/`RECONCILIATION_REQUIRED`, (2) **không cho task khác dùng tài nguyên chưa rõ**, (3–6) đối chiếu, xác nhận thực tế, ghi giao dịch, rồi mới giải phóng. `InventoryService.MarkUncertainAsync` hiện **chỉ** thực hiện bước (1) khi `PickConfirmedAt != null && DropConfirmedAt == null` (`:85`).

### 3.2 Tình huống

| # | Tình huống | Hiện nay | Khoảng trống so với spec §4.8 |
|---|---|---|---|
| S1 | RCS `Completed`, WCS chưa ghi nhận Pick | task `Failed` + đối soát; reservation giữ; rack vẫn `Full` ở nguồn; **vị trí không bị đánh dấu** | Rack có thể đã được chuyển thật; các rack khác cùng vị trí vẫn được chọn; UI hiện tồn kho như bình thường |
| S2 | RCS `Interrupted` trước Pick | giống S1 (đường tín hiệu `RcsInterrupted` cũ cũng vậy) | Robot có thể dừng giữa đường / đã nâng rack |
| S3 | `Interrupted` sau Pick | rack `Unknown`, nguồn & đích `ReconciliationRequired` (đúng spec) | Không có |
| S4 | Không xác định rack ở đâu | Admin dùng `reconcile` với vị trí thực tế; không có lựa chọn "rack mất/không ở trong hệ thống" | Cần quyết định có hỗ trợ "rack không tìm thấy" (xoá/điều chỉnh) hay dùng điều chỉnh tồn kho thủ công |
| S5 | Task terminal nhưng chưa xác nhận vật lý | `RequiresReconciliation=true`, reservation `Active` | Cần danh sách/bộ lọc "cần đối soát" và hành động UI (hiện chỉ có biểu tượng cảnh báo) |

### 3.3 Phương án mô hình
- **M1 – Chỉ đánh dấu vị trí (không đổi trạng thái rack):** khi kết thúc không chắc chắn mà **chưa có pick**, `SetReconciliationRequired(true)` cho vị trí nguồn (và đích nếu đã giữ). Rack vẫn `Full/Empty` "theo WCS". Ưu: ít xâm lấn; chặn cấp tài nguyên tại vị trí. Nhược: vị trí bị chặn cả khi chỉ một rack nghi ngờ; rack vẫn hiển thị bình thường.
- **M2 – Đánh dấu rack + vị trí:** như M1 và `rack.MarkUnknown` (rack vẫn giữ `CurrentLocationId` để đếm chiếm chỗ – an toàn về sức chứa). Ưu: đúng nhất với §4.8 (1)–(2). Nhược: rack `Unknown` không còn `BeginTransit` được (cần đối soát) – hành vi mong muốn; ảnh hưởng báo cáo tồn kho.
- **M3 – Giữ nguyên, chỉ bổ sung UI/quy trình:** không đổi `InventoryService`; vận hành đối soát bằng UI. Ưu: không chạm module bảo vệ. Nhược: không thoả §4.8 (2).
- **Phạm vi áp dụng (quyết định chủ dự án):** (a) chỉ các kết thúc "chưa chứng minh vật lý" mới (Completed/Interrupted/NotFound-sau-accept/retry-exhausted) hay (b) mọi kết thúc sau khi RCS đã nhận task (kể cả hủy được RCS xác nhận → sẽ chặn vị trí ở mọi lần hủy, ảnh hưởng vận hành lớn). Đề xuất xem xét (a) bằng một tham số/biến thể riêng của `MarkUncertainAsync`, không đổi hành vi hiện tại của các đường khác.

### 3.4 Tác động
- **Rack/Inventory:** thêm cờ trên `Location`/`Rack` (đã có sẵn trường, không cần migration); `ReserveSourceAsync`/`ReserveDestinationAsync` đã bỏ qua vị trí `ReconciliationRequired` ⇒ **không cần đổi** logic đặt chỗ.
- **Task/AGV:** vị trí bị chặn làm các task cần vị trí đó chờ/ `NO_SOURCE_AVAILABLE`; cần chủ dự án chấp nhận mức chặn.
- **PLC:** không ảnh hưởng.
- **Module (bảo vệ – cần phê duyệt riêng):** `InventoryService.MarkUncertainAsync` (hoặc phương thức mới), `TaskOrchestrator.FinishAsync` (truyền loại không chắc chắn), `ReconcileTaskAsync` (đã xoá cờ cho nguồn/đích/thực tế – kiểm chứng đủ cho vị trí mới); không đổi `Rack`/`Location` domain.

---

## 4. REV-004 – Điều kiện trả AGV

### 4.1 Vì sao AGV được trả hiện nay (FACT)
`FinishAsync` luôn `TransitionTo(terminal)` rồi `after.Add(TryDispatchNextAsync(agvId))`. Tính sẵn sàng của AGV được suy từ **số task chiếm AGV** (`GetActiveForAgvAsync`) và `Agv.Enabled`; **không có** khái niệm "AGV bị giữ chờ xác minh". Đường tín hiệu cũ `RcsInterrupted` cũng trả AGV; CR-008A thêm `CANCEL_RACE_RCS_COMPLETED/INTERRUPTED`, `RCS_NOTFOUND_AFTER_ACCEPT` đi cùng đường đó.

### 4.2 Rủi ro
- `Interrupted`/NotFound-sau-accept: robot có thể đang dừng giữa đường có/không có rack; task kế tiếp có thể được gán ngay cho AGV đó.
- `Completed` (RCS báo xong): thường an toàn hơn nhưng chưa có xác nhận vật lý từ WCS.

### 4.3 Phương án (không đổi PER_AGV, AGV1/AGV2 độc lập)
- **P1 – Giữ nguyên:** trả AGV ngay sau khi task terminal. Rủi ro như trên; không thay đổi mã.
- **P2 – Cờ giữ điều phối theo AGV (`DispatchHold`)**: thêm cột trên `agvs` (hoặc suy ra từ task); đặt khi task kết thúc không chắc chắn; `TryDispatchNextAsync`/`ProcessTaskAsync` bỏ qua AGV đang giữ (task mới vào `Queued`); Admin gỡ giữ sau đối soát (hoặc tự gỡ khi đối soát xong). Ưu: rõ ràng, độc lập từng AGV, không đổi `TaskStateMachine`/PER_AGV. Nhược: cần migration, UI, quyền, test; thêm một khái niệm mới (cần phê duyệt).
- **P3 – Suy ra không cần cột:** AGV bị giữ nếu tồn tại task terminal của AGV có `RequiresReconciliation && !ReconciliationCompleted` và lý do thuộc nhóm "không chắc chắn" (cần dấu hiệu trên task, vd. một cột/enum lý do). Ưu: tự gỡ khi đối soát (`MarkReconciled`); Nhược: nếu dùng điều kiện rộng (mọi task cần đối soát) thì **mọi lần huỷ có xác nhận cũng giữ AGV** (ảnh hưởng vận hành lớn); cần phân loại chặt hoặc cột mới.
- **P4 – Tự đặt `Agv.Enabled=false`:** không cần cột; nhưng trộn cấu hình với trạng thái vận hành, chặn cả `CreateTask` (`AGV_DISABLED`), khó phân biệt với Admin tắt chủ ý. Không khuyến nghị.
- **Phân loại cần chủ dự án quyết định (mỗi kết cục thuộc nhóm nào):** (i) trả ngay, (ii) giữ chờ Admin đối soát, (iii) giữ chờ xác minh vị trí vật lý, (iv) trả nhưng hạn chế điều phối (vd. chỉ cho task không dùng vị trí liên quan).

| Kết cục | Gợi ý nhóm để chủ dự án cân nhắc |
|---|---|
| RCS `Cancelled` xác nhận | (i) như hiện nay |
| RCS `Completed` khi hủy | (ii) hoặc (i) tuỳ chính sách |
| RCS `Interrupted` | (iii) |
| NotFound-sau-accept | (iii) |
| Hết hạn mức retry rồi bị đóng thủ công | (iii) |

### 4.4 Tác động và module
- **AGV:** AGV bị giữ không nhận task; AGV kia chạy bình thường. **PER_AGV không đổi** (giữ ≠ task đang hoạt động).
- **Module:** `TaskOrchestrator.FinishAsync` (điều kiện hậu xử lý), `TryDispatchNextAsync`, `ProcessTaskAsync`; `Agv` domain + `IAgvRepository` + migration (P2); `Endpoints.cs`/UI AGV (gỡ giữ); `TaskScheduler` **không** phải đổi (nó chỉ gọi `TryDispatchNextAsync`).

---

## 5. REV-005 và REV-006

### 5.1 REV-005 – Idempotency RCS theo `RcsRequestCode`
- **Điều kiện bắt buộc xác minh với nhà cung cấp trước khi tích hợp RCS thật:** (1) RCS có từ chối/hợp nhất khi nhận lại cùng `RequestCode` không; (2) `QueryTask` ngay sau create có thể trả "không tìm thấy" trong khi task đang được tạo (bất đồng bộ) không; (3) ngữ nghĩa trạng thái sau khi hủy (task còn tồn tại hay bị xoá → `NotFound`); (4) mã trạng thái 0–10 → trạng thái WCS (`task-state-machine.md §3.8`: spec nêu rõ "khi nhận timeout, không mặc định rằng RCS chưa tạo task").
- Hiện trạng mã: `DispatchToRcsAsync` gọi create lại khi query trả `NotFound` (hoặc trả trạng thái khác `NotFound` nhưng không có mã) – đúng với spec §3.7 chỉ khi (1) được xác nhận. Đề xuất tài liệu hoá đây là **điều kiện chặn (gate) của CR tích hợp RCS thật**, và xem xét "hai lần NotFound cách nhau" trước khi tạo lại.

### 5.2 REV-006 – Thời gian retry thực tế (tính từ `RcsRetryPolicy`, mặc định 2 s / 60 s / 5 lần dispatch / 30 lần continue-cancel)

| Chuỗi | Lần thử tại t (s) | Kết cục |
|---|---|---|
| Dispatch (MaxDispatchAttempts=5) | 0, 2, 6, 14, 30 | Sau lần 5, **chờ thêm `DelayAfter(5)=32 s`** rồi mới khai báo `RCS_DISPATCH_UNCERTAIN` ⇒ **≈ 62 s** (không phải ≈ 30 s) |
| Continue / Cancel (RcsMaxAttempts=30) | độ trễ 2, 4, 8, 16, 32, rồi 60 s | Lần 30 tại **≈ 1 502 s ≈ 25 phút**; khai báo exhausted ngay ở tick kế tiếp (nhánh này kiểm hạn mức trước khi kiểm `IsDue`, **khác** nhánh dispatch – cần thống nhất, quyết định thiết kế nhỏ) |

**Tài liệu cần sửa (không đổi mã/thông số):**

| Tài liệu | Sửa |
|---|---|
| `docs/change-management/CHANGE_REQUESTS.md` (CR-008A, "Behaviour changes" b) | "≈30 s" → "≈62 s (2+4+8+16 s đến lần thử thứ 5, rồi chờ 32 s)"; thêm mô tả continue/cancel ≈25 phút |
| `docs/decisions/ADR/ADR-0007…` (Consequences; Decision 2) | "about 30 s" → "about 62 s"; **đính chính Decision 2** (không ack ≠ PLC trình lại – xem H-1) |
| `docs/change-management/CHANGELOG.md` (v2.2) | Bổ sung ghi chú thời gian và đính chính |
| `docs/archive/audits-2026-11-16/CR-008A_Dispatch_Scheduler_Reliability_Report.md` | "≈ 30 s" → "≈ 62 s"; đính chính câu "PLC trình lại" |
| `docs/archive/reviews/CR008A_POST_IMPLEMENTATION_REVIEW.md` | REV-001 "≈30 phút" → "≈25 phút"; REV-006 giữ ≈62 s; ghi chú H-1/H-3 |
| `docs/change-management/IMPACT_MATRIX.md`, `REQUIREMENT_TRACEABILITY.md` | Thêm dòng cho các CR-008B con; REL-04/REL-05 ghi trạng thái "có điều kiện – chờ CR-008B" |

---

## 6. Ràng buộc regression (bảo toàn)

| Hạng mục | Cam kết thiết kế | Ghi chú |
|---|---|---|
| `TaskStateMachine` | Không đổi; mọi phương án dùng cạnh sẵn có (`CancelPending → Failed/Cancelled/Completed`) | Phương án B của REV-001 không thêm cạnh |
| `InventoryService` | Chỉ **REV-003 (M1/M2)** cần chạm (`MarkUncertainAsync`); phải được phê duyệt riêng. M3 không chạm | Quy tắc đặt chỗ không đổi |
| `TaskScheduler` | Không đổi (REV-004 dùng `TryDispatchNextAsync` bên trong orchestrator) | |
| `FlowOptions` | Không đổi | |
| PER_AGV / AGV1–AGV2 song song | Không đổi; `DispatchHold` chỉ chặn nhận task của **một** AGV | Cần test song song giữ nguyên |
| A3/A4, D1/D2, F5 | Không đụng tới | Không có luồng liên quan |
| PLC/MC, handshake, polling, timeout/retry PLC | Không đổi; REV-002 chỉ đổi cách WCS nhớ yêu cầu | `Request_Ack` vẫn không được ghi |
| `StateTimeoutsSeconds` | Vẫn chưa cấu hình | Không dùng timeout để giải quyết REV-001 |
| Thông số retry (2 s/60 s/5/30) | Không đổi trong CR này; chỉ sửa tài liệu | Muốn đổi → quyết định riêng |

---

## 7. File/module/method dự kiến thay đổi

| REV | Module (⚑ = bảo vệ, cần phê duyệt) | Method / tệp | Migration |
|---|---|---|---|
| 001-A/B/C | `TaskOrchestrator` ⚑, `TransportTask`, `Endpoints.cs`, frontend | `CancelTaskAsync` (nhánh `CancelPending`), `ReconcileCancelPendingAsync`, mới `CloseCancelPendingAsync`/`RearmCancelAsync`; `TransportTask.ResetRcsRetry`; endpoint Admin; `client.ts`, `TasksPage.tsx`, `TaskDialogs.tsx`, `vi.ts` | Không (A/B); có thể thêm 1 cột "lý do đóng" tuỳ thiết kế |
| 002 | `StationRequestProcessor`, `PlcStationSignalService`, `StationSignalSimulator`, (`IRcsDispatchGuard` đã có) | `RunOnceAsync`, `PollRequestsAsync`, `AcknowledgeRequestAsync` | Không |
| 003 | `InventoryService` ⚑, `TaskOrchestrator.FinishAsync` ⚑ | `MarkUncertainAsync` (hoặc phương thức mới); lời gọi trong `FinishAsync`/`EndUncertainAsync` | Không |
| 004 (P2) | `Agv` domain, `IAgvRepository`/`AgvRepository`, `TaskOrchestrator` ⚑, `Endpoints.cs`, frontend AGV | `FinishAsync`, `TryDispatchNextAsync`, `ProcessTaskAsync`; endpoint gỡ giữ | Có (cột trên `agvs`) |
| 005/006 | Tài liệu | Bảng mục 5.2 | Không |

---

## 8. Test case cần bổ sung

**REV-001:** hết hạn mức → Admin thử lại → bộ đếm 0, dấu exhausted xoá, có audit + lịch sử; thử lại khi RCS `Executing` tiếp tục gửi cancel có backoff; đóng thủ công (B) → `Failed`, `RequiresReconciliation`, reservation còn, vị trí/rack theo REV-003; đóng khi truy vấn RCS gần nhất là `Executing` → bị chặn hoặc cảnh báo; Operator gọi → 403; restart (khởi tạo `ServiceProvider` mới) giữ nguyên bộ đếm/đánh dấu; đóng rồi `reconcile` thành công và giải phóng đúng reservation; hai Admin thao tác đồng thời (một thắng).
**REV-002:** `RCS_NOT_CONNECTED` không ack; yêu cầu giữ chờ; RCS sẵn sàng → tạo đúng một task; cùng `Request_ID` gửi lại nhiều lần → một task; từ chối vĩnh viễn → ack một lần; ngoại lệ lạ → không ack, vẫn xử lý yêu cầu khác; restart: bit vẫn cao → cạnh lên mới → vẫn idempotent; `Request_ID` rỗng → không tạo; PLC hai trạm cùng `Request_ID` → khoá khác nhau.
**REV-003:** từng S1–S5; vị trí đánh dấu chặn `ReserveSource/Destination`; `reconcile` xoá cờ cả nguồn, đích, vị trí thực tế; rack `Unknown` vẫn tính chiếm chỗ; không đổi hành vi với hủy được RCS xác nhận.
**REV-004:** AGV bị giữ không nhận task Queued/mới; AGV kia vẫn nhận (song song); gỡ giữ sau đối soát; PER_AGV vẫn một task hoạt động; Operator không gỡ được; restart giữ cờ.
**REV-005/006:** unit test `RcsRetryPolicy` (chuỗi 2,4,8,16,32,60; trần; giá trị 0); test mô phỏng tổng thời gian dispatch ≈ 62 s và continue/cancel ≈ 1 502 s bằng đồng hồ test; kiểm tra tài liệu không còn "≈30 s".
**Chung:** test HTTP cho 422 `RCS_NOT_CONNECTED`; test song song hai tick; thông điệp UI cho mã mới.

---

## 9. Chia phạm vi thành CR nhỏ

| CR | Nội dung | Phụ thuộc | Cần phê duyệt |
|---|---|---|---|
| **CR-008B-1** | Sửa tài liệu REV-005/006, H-1, H-2; ghi điều kiện RCS idempotency vào checklist tích hợp RCS | – | Không (chỉ tài liệu) |
| **CR-008B-2** | REV-002: intake không mất yêu cầu (+test) | – | D-3, D-4 |
| **CR-008B-3** | REV-001: đường thoát `CancelPending` (API + UI đối soát/đóng) | quyết định D-1, D-2; nên cùng B-4 | D-1, D-2 (+ module bảo vệ) |
| **CR-008B-4** | REV-003: đánh dấu rack/vị trí không chắc chắn | D-5, D-6 | D-5, D-6 (`InventoryService` bảo vệ) |
| **CR-008B-5** | REV-004: điều kiện giữ/trả AGV | B-3 (thao tác đóng), B-4; D-7, D-8 | D-7, D-8 (migration nếu P2) |

Thứ tự đề xuất: **B-1 → B-2 → (B-3 + B-4) → B-5**. CR-009 chỉ nên bắt đầu sau B-3 (không còn HIGH về trạng thái RCS) và quyết định D-7 cho REV-004.

---

## 10. DECISIONS REQUIRED

| ID | Câu hỏi | Lựa chọn | Ghi chú tác động |
|---|---|---|---|
| **D-1** | `CancelPending` hết hạn mức: cho phép gì? | (A) thử lại; (B) đóng chờ đối soát; (C) A rồi B (hai cấp xác nhận); (E) không làm gì | A không giải quyết RCS mất vĩnh viễn; B cần REV-004 |
| **D-2** | Ai được đóng/thử lại và cần xác nhận gì? | Admin + lý do bắt buộc; thêm tích "đã xác minh hiện trường/RCS"; hoặc yêu cầu truy vấn RCS gần nhất không `Executing` | Ảnh hưởng UI, audit |
| **D-3** | Lỗi nào được ack (loại bỏ yêu cầu PLC) và lỗi nào phải giữ chờ? | Danh sách vĩnh viễn: {FLOW_NOT_SUPPORTED, FLOW_IN_REVIEW, STATION_NOT_FOUND, STATION_NOT_ALLOWED, …} vs tạm thời: {RCS_NOT_CONNECTED, DataConflict, lỗi lạ} | Cần chủ dự án xác nhận danh sách, nhất là `AGV_DISABLED`, `AGV_NOT_CONFIGURED`, `NO_SOURCE_*` |
| **D-4** | Cách giữ yêu cầu chưa chấp nhận | (A) in-memory trong dịch vụ tín hiệu; (B) không poll khi RCS chưa nối; (A+B); (C) bảng DB | C cần migration; PLC level đã tái tạo yêu cầu sau restart |
| **D-5** | Mô hình không chắc chắn của tồn kho | M1 (vị trí), M2 (rack + vị trí), M3 (giữ nguyên + UI) | M2 gần nhất với spec §4.8; ảnh hưởng báo cáo tồn kho |
| **D-6** | Phạm vi áp dụng đánh dấu | (a) chỉ kết cục mới (Completed/Interrupted/NotFound-sau-accept/hết hạn mức); (b) mọi task đã được RCS nhận | (b) chặn vị trí ở mọi lần hủy |
| **D-7** | Điều kiện trả AGV theo từng kết cục | P1 giữ nguyên; P2 `DispatchHold`; P3 suy ra; (i)–(iv) theo bảng 4.3 | P2/P3 là khái niệm mới, cần phê duyệt; PER_AGV không đổi |
| **D-8** | Ai gỡ giữ AGV | Admin thủ công; tự gỡ khi đối soát xong; kết hợp | Ảnh hưởng UI và audit |
| **D-9** | `NotFound` ×2 → `Cancelled`: có chấp nhận là "cơ chế đối soát tương đương" (spec §3.9) không? | Giữ `Cancelled`; hoặc đổi thành `Failed` + đối soát (không bao giờ `Cancelled` nếu RCS chưa xác nhận) | H-3 |
| **D-10** | Hạn mức retry & tính nhất quán | Giữ 2 s/60 s/5/30 và chỉ sửa tài liệu; hoặc thay đổi (giá trị/thứ tự kiểm tra hạn mức giữa dispatch và continue/cancel) | Mọi thay đổi cần xác nhận với kỹ sư RCS |
| **D-11** | `Request_ID` của PLC có duy nhất theo thời gian không? Hành vi PLC khi không nhận `Request_Ack`? | Xác nhận với kỹ sư PLC | Quyết định mức rủi ro bỏ sót/trùng; không đổi handshake |
| **D-12** | Điều kiện chặn khi tích hợp RCS thật | Ghi vào CR tích hợp: xác minh idempotency theo `RcsRequestCode`, ngữ nghĩa NotFound/hủy, ánh xạ mã 0–10 | REV-005 |
| **D-13** | "Rack không tìm thấy" khi đối soát | Cho phép chọn "không tồn tại" (ghi giao dịch điều chỉnh) hay yêu cầu dùng điều chỉnh tồn kho thủ công | S4 |
| **D-14** | UI đối soát | Có xây UI đối soát/đóng trong CR-008B-3 hay để dùng API trực tiếp | Hiện chỉ có API |

*Không có thay đổi mã/DB/cấu hình nào được thực hiện; báo cáo này chưa commit.*
