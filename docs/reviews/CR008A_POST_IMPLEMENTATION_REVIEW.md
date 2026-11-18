# CR-008A – Post-Implementation Review (kiểm chứng độc lập)

**Ngày:** 2026-11-16 · **Loại:** AUDIT ONLY · **Đối tượng:** commit `3626812` (CR-008A) · **Tác giả review:** Claude (cùng phiên với người triển khai – xem lưu ý độc lập ở mục 8)

Nhãn: **FACT** (đọc mã/diff/lệnh Git), **TEST** (test đã chạy ở review này), **INFERENCE** (suy luận từ mã), **NOT VERIFIED**. Kết luận: PASS / FAIL / NOT VERIFIED.

## 1. Git state

| Mục | Kết quả |
|---|---|
| Branch hiện tại | `develop` (FACT) |
| HEAD | `36268120716aac0df0c828c35a58e8751987bdcd` = `3626812` (FACT) |
| Commit nằm trên `develop` | Có (`git branch --contains` → `develop`) |
| `main` / `origin/main` | `d5d692f` / `d5d692f` – không đổi (FACT) |
| Working tree | Chỉ 7 mục untracked có sẵn (`docs/docker-acceptance/`, 4 báo cáo trong `report/`, `tests/deployment/Test-DockerAcceptancePackage.ps1`, `tests/deployment/fake-docker/`); **không có file tracked bị sửa**; giữ nguyên |
| Hành động Git/Source của review này | Không sửa/xóa file nào; chỉ tạo báo cáo này (chưa commit); không checkout/merge/push |

## 2. Commit và phạm vi file

25 file, +2089/−27. **Mã nguồn sản xuất (14):** `TransportTask.cs`, `Abstractions/Integration.cs`, `Abstractions/Services.cs`, `RcsRetryPolicy.cs` (mới), `TaskOrchestrator.cs`, `StationRequestProcessor.cs`, `AgvStatusService.cs`, `Infrastructure/DependencyInjection.cs`, `RcsDispatchGuard.cs` (mới), `Simulators.cs`, migration + Designer (mới) + snapshot, `HostedServices.cs`, `WCS.Api/Program.cs`, `Api/Endpoints.cs`; `deployment/docker-compose.yml` (+2 dòng). **Test:** `Cr008aReliabilityTests.cs` (mới), `Harness.cs`. **Tài liệu:** CR, CHANGELOG, IMPACT_MATRIX, TRACEABILITY, ADR-0007, báo cáo CR-008A. Không file frontend nào đổi (FACT).

## 3. Kết quả từng mục A–F

### A. Production Simulator Guard – **PASS** (kèm 3 ghi chú)

| Kiểm tra | Kết quả | Bằng chứng |
|---|---|---|
| Production + simulator từ chối tạo task `RCS_NOT_CONNECTED` | PASS | `TaskOrchestrator.CreateTaskAsync` gọi `rcsGuard.Check()` trước mọi truy cập DB (diff); TEST `Production_with_only_the_simulator_refuses_...` |
| Không tạo reservation / không task / không gọi RCS | PASS | cùng test: 0 task, 0 reservation, 0 cuộc gọi RCS |
| AGV không Online giả | PASS | `AgvStatusService` đặt `Unknown/RCS_NOT_CONNECTED` khi guard chặn, không query simulator; TEST `Production_shows_the_agv_as_unknown_...` (QueryCalls=0; Development vẫn Online) |
| `Rcs:AllowSimulatorInProduction` mặc định false | PASS | `DependencyInjection.cs`: `GetValue("Rcs:AllowSimulatorInProduction", false)`; compose `${RCS_ALLOW_SIMULATOR_IN_PRODUCTION:-false}` |
| Development/Testing/Staging/Demo không đổi | PASS | TEST (3 môi trường + không có host environment); Demo là engine trình duyệt, không đổi mã |
| API | PASS (mã) / NOT VERIFIED (HTTP) | `AppException.Rejected` → 422 (`ExceptionMapping`); không có test HTTP cho trường hợp này |
| UI | PASS với lưu ý | Không đổi frontend; banner "chưa kết nối RCS" và chỉ báo "Chưa kết nối" đã có; mã `RCS_NOT_CONNECTED` **không** có trong `ERROR_VI` nên người dùng thấy câu chung "Yêu cầu không thành công (mã RCS_NOT_CONNECTED)" – REV-007 |

Ghi chú: guard chỉ đặt ở `CreateTaskAsync`, `DispatchToRcsAsync`, `AgvStatusService`; continue/cancel/reconcile của task đã tồn tại không qua guard (REV-008). Guard loại trừ bằng kiểm tra kiểu `rcs is RcsSimulator` (đúng với mục tiêu hiện tại).

### B. Dispatch Retry – **PASS có điều kiện** (không có bằng chứng gửi trùng; một rủi ro phụ thuộc RCS thật)

| Kiểm tra | Kết quả | Bằng chứng |
|---|---|---|
| Backoff 2 s, 4 s, 8 s… | PASS | `RcsRetryPolicy.DelayAfter`: `initial × 2^(n−1)`, giới hạn `max`; TEST mốc 1 s / +1999 ms (chưa) / +1 ms (đến hạn) với initial 1000 |
| Giới hạn 60 s | PASS (mã) / test không khẳng định số 60 000 | Công thức `Math.Min(ms, max)` đúng khi tính tay (n=6 → 64 s → 60 s); các test dùng max 4–8 s; **không có unit test trực tiếp cho `RcsRetryPolicy`** (REV-010) |
| `RcsRequestCode` giữ nguyên | PASS | gán một lần ở `TransportTask.Create` (`:30`), không có setter; `Cr008a` không đổi |
| Query trước khi gửi lại | PASS | `AttemptsBefore > 0` → `QueryTaskStatusAsync`; TEST `Timeout_after_creation_...` (CreateCalls=1, CreatedTaskCount=1) và `Dispatch_retries_are_spaced...` (lần 2 là query, không phải create) |
| Task trùng khi RCS đã nhận nhưng phản hồi mất | PASS với simulator; **NOT VERIFIED với RCS thật** | `TimeoutAfterCreate` được ánh xạ bằng query. Rủi ro còn lại: nhánh `status != NotFound && code is not null` (`TaskOrchestrator.cs:181`) – nếu query trả `NotFound` (hoặc status khác `NotFound` nhưng không có code) thì **gửi `CreateTask` lần nữa**; điều này chỉ an toàn nếu RCS idempotent theo `RcsRequestCode`. Không do CR-008A tạo ra (có từ trước) nhưng chưa được chứng minh – REV-005 |
| Retry count/timestamp bền vững | PASS | cột DB `RcsRetryCount`, `RcsLastAttemptAt` (migration, snapshot); mỗi `TickAsync` trong test mở scope mới và đọc lại từ DB, nên trạng thái không nằm trong bộ nhớ. **Khởi động lại tiến trình thật: NOT VERIFIED** (không có test restart; suy luận chắc chắn vì không có trạng thái in-memory) |
| Đạt giới hạn → đối soát + giữ reservation | PASS | TEST `Dispatch_budget_is_bounded...`: `Failed` `RCS_DISPATCH_UNCERTAIN`, `RequiresReconciliation=true`, reservation còn |
| Race: hai tick liên tiếp / retry vs. phản hồi muộn / restart | PASS (suy luận) / NOT VERIFIED (test) | Pha 1 là một transaction có xmin: hai đường chạy đồng thời đều ghi `RegisterRcsAttempt`; bên thua chạy lại và thấy `RcsRetryCount>0` chưa đến hạn → không gọi. Phản hồi muộn: pha 3 bỏ qua nếu `State != Dispatching` (đã có từ trước; mã RCS có thể không được gắn – REV-005). Không có test song song |

Điều không được mặc định: query thành công **không** được coi là chắc chắn – mã chỉ chấp nhận `Unknown` bằng cách chờ; `NotFound` vẫn dẫn tới tạo mới (xem trên).

### C. Continue / Cancel Retry – **FAIL (một hạng mục: phục hồi sau khi hết hạn mức)**

| Kiểm tra | Kết quả | Bằng chứng |
|---|---|---|
| Backoff giới hạn tần suất | PASS | TEST: 20 tick không trôi thời gian → 0 cuộc gọi thêm (continue và cancel) |
| Giới hạn 30 lần chính xác | PASS | `RcsRetryCount >= Math.Max(1, RcsMaxAttempts)` → dừng; TEST với 4: `ContinueCalls ≤ 4`, `QueryCalls ≤ 4` |
| `RequiresReconciliation` lưu xuống DB | PASS | cùng transaction (`task.Note` + `RequireReconciliation`), TEST đọc lại từ DB |
| Restart làm retry lại vô hạn / mất đối soát | PASS (suy luận) | điểm đánh dấu `RCS_*_RETRY_EXHAUSTED` và bộ đếm nằm trong DB; `HasHistory` bảo đảm ghi một lần |
| TaskHistory không trùng | PASS | TEST: `RCS_QUERY_UNKNOWN`, `RCS_CANCEL_REQUESTED`, `RCS_CANCEL_IN_PROGRESS`, `RCS_*_RETRY_EXHAUSTED` đều một bản ghi. Lưu ý: so sánh chỉ dựa trên các bản ghi có timestamp mới nhất |
| Task Completed nhưng Cancel vẫn gửi | PASS gần như | `SendCancelToRcsAsync` kiểm tra `State == CancelPending` ở pha 1; còn cửa sổ rất ngắn giữa pha 1 và cuộc gọi RCS (REV-011) |
| **Phục hồi sau khi hết hạn mức** | **FAIL** | **REV-001** |

### D. CancelPending – 8 trạng thái RCS

| Trạng thái | Xử lý (mã) | Test | Kết luận |
|---|---|---|---|
| Cancelled | `HandleSignalAsync(RcsCancelConfirmed)` → `FinishAsync` | có sẵn từ trước + `Cancel_pending_with_executing_...` | PASS |
| NotFound (đã có mã RCS) | `Failed RCS_NOTFOUND_AFTER_ACCEPT`, `forceUncertain` | `..._notfound_after_rcs_accepted_...` | PASS (không coi là hủy) |
| NotFound (chưa có mã RCS) | cần 2 lần liên tiếp, kiểm bằng `LastHistory == RCS_CANCEL_NOTFOUND_SEEN` (lưu trong DB nên đúng qua restart); nếu từng có dispatch → `forceUncertain` | `..._two_consecutive_confirmations_...` | PASS; khoảng cách hai lần chỉ là backoff (REV-009) |
| Created | gửi lại cancel theo backoff | `..._executing_resends_...` (Executing; Created cùng nhánh) | PASS (Created không có test riêng) |
| Executing | như trên | có | PASS |
| Cancelling | chờ, ghi 1 dòng | có | PASS |
| Completed | `Failed CANCEL_RACE_RCS_COMPLETED`, đối soát, giữ reservation | có | PASS (rack/reservation); xem REV-003, REV-004 |
| Interrupted | tương tự | có | PASS (rack/reservation); xem REV-003, REV-004 |
| Unknown / lỗi truy vấn | chờ, ghi 1 dòng, thử lại theo backoff | có | PASS |

Tiêu chí con: "không giải phóng rack/reservation khi chưa xác định vị trí vật lý" – **PASS** (reservation giữ ở mọi nhánh không chắc chắn; xác nhận bằng test). "AGV chỉ được trả khi điều kiện thực tế cho phép" – **FAIL/NEEDS_CONFIRMATION**: Completed, Interrupted và NotFound-sau-accept kết thúc bằng `FinishAsync`, mà `FinishAsync` luôn trả AGV và gọi `TryDispatchNextAsync` – xem REV-004.

### E. Migration `AddRcsRetryTracking` – **PASS**

- `Up`: đúng hai `AddColumn` trên `transport_tasks`: `RcsLastAttemptAt timestamptz NULL`; `RcsRetryCount integer NOT NULL DEFAULT 0` (FACT, file migration).
- Không có DROP/TRUNCATE/đổi tên/đổi kiểu trong `Up`; `Down` chỉ `DropColumn` hai cột đó.
- Snapshot: diff chỉ thêm đúng hai property (`+6` dòng); Designer chứa hai property tại dòng 796 và 804. Đồng bộ với model (build + `dotnet test` đạt, migration áp dụng thành công trên DB test).
- **Không apply lên `wcs_db`**; test chỉ áp dụng lên DB tạm `wcs_test_<guid>`. Dữ liệu cũ: hàng hiện có nhận `RcsRetryCount=0`, `RcsLastAttemptAt=NULL` ⇒ `IsDue` luôn đúng, hành vi như trước.

### F. Regression nghiệp vụ – **PASS**

| Hạng mục | Kết quả |
|---|---|
| `TaskStateMachine`, `InventoryService`, `TaskScheduler`, `FlowOptions`, `Application/Plc/**`, `Infrastructure/Plc/**` (MC, handshake, worker) | `git diff 939b453 3626812` rỗng cho tất cả (FACT) |
| PER_AGV, AGV1/AGV2 song song | TEST `Per_agv_is_still_enforced_...` + ProcessFlowTests/ConcurrencyTests (toàn bộ 463 đạt); index DB `ux_transport_tasks_one_active_per_agv` không đổi |
| A3/A4, D1/D2, F5 | không có thay đổi mã liên quan; `FlowOptions`/`Flows` không đổi |
| `StateTimeoutsSeconds` | `appsettings.json` không nằm trong commit (vẫn `{}`) |
| Thay đổi gián tiếp | (1) `TransportTask.TransitionTo` giờ reset `RcsRetryCount/RcsLastAttemptAt` mỗi lần chuyển trạng thái – vô hại với bộ máy trạng thái; (2) `RegisterDispatchAttempt` đăng ký thêm lần thử RCS; (3) `TaskScheduler` không đổi mã nhưng các hàm nó gọi (`DispatchToRcsAsync`, `SendContinueAsync`, `ReconcileCancelPendingAsync`) đổi hành vi (đã được đánh giá ở B–D); (4) `Harness` đặt khoảng cách retry = 0 cho 438 test cũ, nên các test cũ **không** kiểm tra giá trị mặc định 2 s (chỉ 25 test mới kiểm) |

## 4. Kết quả test thực tế (ở review này)

| Bộ | Kết quả |
|---|---|
| Build `tests/WCS.Tests` | thành công |
| `Cr008aReliabilityTests` | **25/25 đạt** |
| Toàn bộ backend (DB tạm `wcs_test_<guid>` do harness tạo và xoá; không dùng `wcs_db`) | **463/463 đạt**, 0 lỗi |
| Không chạy | Docker, migration thật, frontend (không đổi trong commit) |

Lưu ý an toàn: harness tạo/xoá DB `wcs_test_*` trên chính máy chủ PostgreSQL phát triển (cùng máy chủ với `wcs_db`). Việc này do harness thực hiện (tên GUID, `DROP ... WITH (FORCE)` chỉ tên đó, `TRUNCATE` chỉ trên DB test); **không kiểm tra lại danh sách database sau khi chạy** (không có công cụ truy vấn không cần mật khẩu) – NOT VERIFIED rằng không còn DB rác. Không có thao tác nào nhắm tới `wcs_db`.

## 5. Findings (0 CRITICAL · 1 HIGH · 4 MEDIUM · 6 LOW)

### REV-001 · HIGH · `CancelPending` hết hạn mức thì không còn đường thoát
- **File/method:** `TaskOrchestrator.ReconcileCancelPendingAsync` (khối `RcsRetryCount >= RcsMaxAttempts`), `CancelTaskAsync` (`case TaskState.CancelPending`, dòng 466-468), `SendCancelToRcsAsync`.
- **Bằng chứng (FACT, đọc mã):** khi `RcsRetryCount ≥ 30` hàm reconcile chỉ ghi một lần rồi `return null` ở mọi tick sau. Lệnh Cancel của người dùng khi task đã `CancelPending` chỉ ghi `CANCEL_RETRY` và gửi cancel (`registerAttempt:true`, **không reset bộ đếm**, vì reset chỉ xảy ra trong `TransitionTo`). `ReconcileTaskAsync` yêu cầu task đã terminal. `EnforceTimeout` bị tắt (`StateTimeoutsSeconds` rỗng). Chưa chạy kịch bản này (không được thêm test vào source); lập luận dựa trên mã.
- **Kịch bản:** RCS không phản hồi ≥ 30 lần (≈25 phút với trần 60 s; *đính chính CR-008B-1: ban đầu ghi ≈30 phút*) → task kẹt `CancelPending` dù RCS sống lại.
- **Ảnh hưởng:** AGV bị chiếm vĩnh viễn (PER_AGV), rack/reservation bị giữ, WCS lệch trạng thái với RCS; người vận hành không có thao tác nào từ UI/API để kết thúc (chỉ sửa DB). Tương tự "Continuing" có đường thoát (Cancel → `CancelPending` reset bộ đếm), nhưng `CancelPending` thì không.
- **Đề xuất (cần quyết định nghiệp vụ):** cho Admin hành động tường minh để xử lý (ví dụ reset bộ đếm khi người dùng yêu cầu thử lại, hoặc một thao tác "đóng task chờ đối soát" tạo `Failed` + reconciliation theo cách đã có), có kiểm thử.

### REV-002 · MEDIUM · Yêu cầu từ trạm bị ack và mất khi guard từ chối
- **File/method:** `StationRequestProcessor.RunOnceAsync` (`catch (AppException)` ack mọi lỗi nghiệp vụ) + `CreateTaskAsync` ném `RCS_NOT_CONNECTED`.
- **Kịch bản:** khi tích hợp PLC được bật, Production chưa có RCS: yêu cầu thật bị ack "đã xử lý" nhưng không có task; PLC không trình lại. *(Đính chính CR-008B-1: yêu cầu mất cả khi không ack, vì dịch vụ chỉ nhận cạnh lên.)*
- **Ảnh hưởng:** mất yêu cầu thật; hiện chưa xảy ra vì `Plc:Integration:Enabled=false`.
- **Đề xuất:** không ack khi lý do là trạng thái hạ tầng tạm thời (`RCS_NOT_CONNECTED`), chỉ ack từ chối nghiệp vụ vĩnh viễn.

### REV-003 · MEDIUM · Rack không được đánh dấu "không chắc chắn" khi RCS báo Completed/Interrupted/NotFound-sau-accept trước khi WCS ghi nhận pick
- **File/method:** `InventoryService.MarkUncertainAsync` (`:85` trả về khi `PickConfirmedAt is null`), được `FinishAsync` gọi.
- **Ảnh hưởng:** reservation vẫn giữ (rack không bị dùng lại – đúng yêu cầu) nhưng tồn kho vẫn hiện rack "đang ở nguồn", vị trí không có cờ `ReconciliationRequired`, rack khác cùng vị trí vẫn có thể được chọn; chỉ có `RequiresReconciliation` trên task. File bảo vệ (`InventoryService`) – chỉ đề xuất, chưa sửa.

### REV-004 · MEDIUM · AGV được trả và task kế tiếp được gán ngay sau Completed/Interrupted/NotFound-sau-accept
- **File/method:** `FinishAsync` (`after.Add(TryDispatchNextAsync)`), `EndUncertainAsync`.
- **Ảnh hưởng:** với `Interrupted` (và bất thường NotFound), AGV có thể đang dừng giữa đường với rack; hệ thống vẫn dispatch task tiếp theo cho AGV đó. Mẫu này đã có trước CR (`RcsInterrupted` signal) nhưng CR-008A mở rộng sang nhánh mới. Chính sách AGV chưa được duyệt (NEEDS_CONFIRMATION).

### REV-005 · MEDIUM (NOT VERIFIED) · Gửi lại `CreateTask` sau `NotFound` phụ thuộc tính idempotent của RCS thật
- **File/method:** `DispatchToRcsAsync` (`TaskOrchestrator.cs:181`).
- **Kịch bản:** RCS xử lý bất đồng bộ nên query ngay sau create trả `NotFound`; WCS gọi create lần hai. Chỉ an toàn nếu HIK RCS-2000 từ chối/hợp nhất theo `RcsRequestCode`. Không do CR-008A tạo ra; backoff 2 s+ có thể giảm xác suất nhưng không chứng minh.
- **Đề xuất:** xác nhận hành vi với tài liệu/ môi trường RCS trước khi tích hợp thật; cân nhắc yêu cầu xác nhận hai lần `NotFound` giống nhánh hủy.

### Các finding LOW
| ID | Nội dung | Bằng chứng |
|---|---|---|
| REV-006 | **Tài liệu sai số liệu:** báo cáo/ADR/CHANGELOG ghi dispatch uncertain sau "≈30 s". Thực tế do cổng backoff đặt **trước** kiểm tra hạn mức, task chỉ Failed sau lần thử thứ 5 + `DelayAfter(5)=32 s` ⇒ ≈ **62 s** (2+4+8+16 = 30 s đến lần thử 5, rồi 32 s) | `DispatchToRcsAsync`: cổng `IsDue` trước `DispatchAttempts >= Max`; công thức `RcsRetryPolicy` |
| REV-007 | `RCS_NOT_CONNECTED` chưa có thông điệp tiếng Việt trong `ERROR_VI` | `i18n/vi.ts:154` |
| REV-008 | Guard không chặn task đã tồn tại trước khi nâng cấp: `ProcessTaskAsync`/`TryDispatchNextAsync` vẫn chuyển sang `Dispatching`, `DispatchToRcsAsync` bỏ qua im lặng (log mức Debug) → task giữ AGV mà không có dấu hiệu rõ; continue/cancel không qua guard | `DispatchToRcsAsync` đầu hàm |
| REV-009 | Hai lần xác nhận `NotFound` chỉ cách nhau một khoảng backoff (mặc định 2 s); với RCS nhất quán muộn có thể chưa đủ. Chưa được xác nhận | `ReconcileCancelPendingAsync` |
| REV-010 | Khoảng trống test: không có unit test cho `RcsRetryPolicy` (trần 60 s, giá trị mặc định), không test HTTP 422, không test song song hai tick/đường chạy, không test khởi động lại thật; 438 test cũ chạy với khoảng cách retry 0 | `Harness.cs`, `Cr008aReliabilityTests.cs` |
| REV-011 | Cửa sổ hẹp giữa pha 1 (kiểm `CancelPending`) và cuộc gọi `CancelTaskAsync` tới RCS: task có thể vừa hoàn thành; RCS thường từ chối "đã kết thúc" (không gây hỏng dữ liệu) | `SendCancelToRcsAsync` |

## 6. Những phần chưa thể xác minh (NOT VERIFIED)
- Mọi hành vi với RCS HIK RCS-2000, PLC, AGV, Docker thật; tính idempotent theo `RcsRequestCode` của RCS thật (REV-005).
- HTTP 422 `RCS_NOT_CONNECTED` qua API thật và hiển thị trên UI/thẻ AGV.
- Khởi động lại tiến trình thật giữa các lần retry; chạy đồng thời nhiều tick/tiến trình.
- Kịch bản REV-001 chưa chạy (không được thêm test vào source trong nhiệm vụ audit).
- Danh sách database PostgreSQL sau khi chạy test (không kiểm tra).
- Giá trị mặc định 2 s / 60 s / 5 / 30 chưa được chủ dự án hoặc kỹ sư RCS xác nhận.

## 7. Đề xuất: có chuyển sang CR-009 chưa?

**Chưa đủ điều kiện.**

| Điều kiện | Trạng thái |
|---|---|
| Không còn CRITICAL | Đạt (0) |
| Không còn HIGH liên quan gửi trùng / sai trạng thái RCS / giải phóng rack không an toàn | **Không đạt:** REV-001 (WCS lệch trạng thái với RCS vĩnh viễn, AGV kẹt) |
| Migration được xác minh | Đạt |
| Quy tắc nghiệp vụ hiện tại được bảo toàn | Đạt |

Đề xuất **CR-008B (nhỏ, trước CR-009)**:
1. REV-001 – đường thoát có kiểm soát cho `CancelPending` đã hết hạn mức (cần quyết định của chủ dự án về hành động Admin).
2. REV-002 – không ack khi `RCS_NOT_CONNECTED`.
3. REV-003/004 – quyết định chính sách đánh dấu rack/vị trí và trả AGV sau Completed/Interrupted (có thể cần sửa `InventoryService`; cần phê duyệt riêng vì là module bảo vệ).
4. REV-006/007 – sửa tài liệu (≈62 s) và thông điệp UI.
5. REV-010 – bổ sung test: `RcsRetryPolicy`, 422, song song, khôi phục sau hết hạn mức.
6. REV-005 – ghi nhận là điều kiện bắt buộc khi viết adapter RCS thật (kiểm chứng idempotency).

## 8. Giới hạn về tính độc lập
Review được thực hiện bởi cùng mô hình/phiên đã triển khai CR-008A. Để giảm thiên lệch, kết luận dựa trên đọc lại diff từ Git, đối chiếu mã với từng yêu cầu, chạy lại test từ đầu (không dùng kết quả cũ) và ghi rõ những gì chỉ suy luận. Nên có người khác xem xét REV-001 và REV-003/004 trước khi quyết định.
