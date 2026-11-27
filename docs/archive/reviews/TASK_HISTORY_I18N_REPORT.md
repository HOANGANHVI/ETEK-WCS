# Song ngữ lịch sử nhiệm vụ (Task History)

**Ngày:** 2026-11-17 · **Branch:** `develop` · nền `c156739`. Không đổi TaskStateMachine, vòng đời, scheduler, RCS/PLC, quyền, quy tắc tồn kho.

## 1. Khảo sát
- Nơi ghi: `TransportTask.TransitionTo` (chi tiết = lý do chuyển trạng thái, đồng thời lưu `StateReason`), `Note`, `NoteIfChanged`, `RequireReconciliation` — hơn 30 điểm gọi trong `TaskOrchestrator`, `InventoryService`, `AgvHoldService`, `ReconciliationService`.
- Lược đồ trước đó: `task_history(Id, TaskId, FromState, ToState, EventName, Detail, Source, OccurredAt)` và `transport_tasks.StateReason`. `EventName` đã là mã ổn định (đã có bản dịch); `Detail` / `StateReason` là câu tiếng Anh dựng sẵn.
- Dữ liệu thật: DB cục bộ `wcs_db` có **0** dòng `task_history` (đã kiểm tra, chỉ đọc). DB vận hành không truy cập được từ đây → chiến lược lịch sử dựa trên nguồn phát sinh (mọi định dạng đều do chính WCS ghi).

## 2. Thiết kế
- **Mã chi tiết + tham số** (`HistoryDetail`): `task_history.DetailCode` / `DetailArgsJson` và `transport_tasks.StateReasonCode` / `StateReasonArgsJson`. Câu tiếng Anh gốc **vẫn được ghi nguyên** vào `Detail`/`StateReason` (chẩn đoán, audit).
- Lý do vốn đã là mã (`NO_SOURCE_AVAILABLE`, `TIMEOUT_<State>`, `RCS_REJECTED: <msg>`…) được nhận bằng tập mã cố định, không đoán.
- Nội dung từ bên ngoài (câu trả lời của RCS, ngoại lệ, chi tiết tín hiệu `PICK_CONFIRMED`) **không có mã** → hiển thị nguyên văn.
- API: `TaskHistoryDto` thêm `detailCode`, `detailArgs`; `TaskDto` thêm `stateReasonCode`, `stateReasonArgs` (thêm cuối, mặc định null).
- Frontend: `describeHistory(code, args, raw)` dựng câu từ từ điển `taskHistory.d_<MÃ>`; mã lạ hoặc không có mã → văn bản gốc. Tham số kỹ thuật (ID, mã vị trí, tên người dùng, ghi chú) chèn nguyên; nhãn enum (`leg`, `state`) và lý do giữ AGV được dịch bằng bảng nhãn có sẵn.

## 3. Bản ghi lịch sử
- **Không sửa dữ liệu.** Khi đọc, `HistoryDetail.Legacy(event, detail)` nhận diện bản ghi cũ **chỉ** qua câu/định dạng cố định chính WCS đã ghi: khớp nguyên cả chuỗi (neo `^…$`), giới hạn theo tên sự kiện khi định dạng mơ hồ (`location=…`, `X after Y`…). Không khớp → mã rỗng → hiển thị nguyên văn. Không có thay thế chuỗi con.
- Test xác nhận: định dạng đúng → có mã + tham số; chuỗi sửa đổi / văn bản người nhập / thông điệp RCS / định dạng của sự kiện khác → giữ nguyên văn.
- Tồn tại hạn chế: bản ghi cũ mà người dùng đã gõ ghi chú có dạng trùng hoàn toàn một định dạng của WCS vẫn chỉ được hiểu theo định dạng đó (phần ghi chú giữ nguyên).

## 4. Migration
`AddTaskHistoryDetailCodes`: 4 cột nullable (`DetailCode varchar(100)`, `DetailArgsJson text`, `StateReasonCode varchar(100)`, `StateReasonArgsJson text`). Cộng thêm, có `Down`, không chạm dữ liệu cũ. Chưa áp lên `wcs_db` (cùng với `AddLogLocalization`): `dotnet ef database update --project WCS.Infrastructure --startup-project WCS.Api` (sao lưu trước).

## 5. Danh sách mã (45) — đều có VI + EN
Chuyển trạng thái: `VALIDATING`, `QUEUED_AGV_BUSY`, `QUEUED_AGV_HOLD`, `DISPATCHING_CLAIMED`, `DISPATCHING_FROM_QUEUE`, `RCS_ACCEPTED`, `AGV_EXECUTING`, `WAITING_PLC_PERMISSION`{leg}, `PLC_PERMISSION_GRANTED`, `DESTINATION_ASSIGNED`{location}, `NO_DESTINATION_AVAILABLE`, `RCS_CONTINUED`, `DROP_CONFIRMED`, `RCS_REPORTED_CANCELLATION`, `CANCEL_CONFIRMED_BY_RCS`, `CANCEL_CONFIRMED_BY_RCS_QUERY`, `RCS_REPORTED_INTERRUPTED`, `CANCELLED_BEFORE_RCS`, `CANCEL_REQUESTED`{user,reason}, `TIMEOUT`{state}, `RCS_REJECTED`{message}, `RCS_CONTINUE_REJECTED`{message}.
Lý do dạng mã: `AGV_UNAVAILABLE`, `NO_SOURCE_AVAILABLE`, `RCS_DISPATCH_UNCERTAIN`, `CANCEL_RETRY_LIMIT_REACHED`, `CANCEL_RACE_RCS_COMPLETED`, `CANCEL_RACE_RCS_INTERRUPTED`, `RCS_NOTFOUND_AFTER_ACCEPT`, `RCS_ENDED_WITHOUT_DROP`.
Ghi chú sự kiện: `WAITING_FOR_SOURCE`, `RCS_QUERY_INCONCLUSIVE`, `RCS_CANCEL_IN_PROGRESS`, `RECON_PICK_WHILE_CANCELLING`, `RECON_RCS_ENDED_WITHOUT_DROP`, `LATE_EVENT_IGNORED`{kind,state}, `OUT_OF_ORDER_EVENT_IGNORED`{kind,state}, `RCS_CONTINUE_RETRY_EXHAUSTED`{attempts}, `RCS_CANCEL_RETRY_EXHAUSTED`{attempts,lastAttempt,limitAt}, `AGV_DISPATCH_HOLD`{agv,reason}, `AGV_DISPATCH_HOLD_RELEASED`{actor,note}, `SOURCE_RESERVED`{location}, `DESTINATION_RESERVED`{location}, `RECONCILED`{actual,by,note}, `RECONCILIATION_CHECK_UNRESOLVED`{actor,note}.

## 6. Kiểm thử
- Backend: `TaskHistoryCodesTests` (21 ca): nhận diện định dạng cũ, văn bản không nhận diện giữ nguyên, định dạng theo sự kiện, mục mới có mã + tham số + văn bản gốc, DTO, bản ghi không có mã vẫn đọc được. Toàn bộ suite **540/540** (519 cũ + 21 mới) — hành vi xử lý nhiệm vụ không đổi.
- Frontend: `npm run i18n:test` (54 phép kiểm tra, gồm đổi ngôn ngữ trên cùng một bản ghi, nội suy tham số, mã lạ/không mã, nhãn enum, mọi mã có VI+EN cùng tham số); `npm run i18n:check` (mọi mã backend của lịch sử phải có bản dịch); ESLint, `tsc -b`, `npm run build` sạch.

## 7. Giới hạn còn lại
1. Văn bản bên ngoài (RCS, ngoại lệ, chi tiết tín hiệu PLC `PICK_CONFIRMED`) không có mã → nguyên văn.
2. Ghi chú của bản ghi **tồn kho** (`inventory_transactions.Note`) vẫn dùng quy tắc cũ (`reasonVi`), chưa chuyển sang mã.
3. Chế độ DEMO tạo lịch sử không có mã → hiển thị nguyên văn tiếng Anh.
4. Chưa kiểm tra trình duyệt thật; chưa có dữ liệu vận hành thật để đối chiếu bản ghi cũ.
