# CR-008A – Dispatch & Scheduler Reliability: báo cáo triển khai và kiểm thử

| Mục | Giá trị |
|---|---|
| Ngày | 2026-11-16 |
| Branch | `develop` (base `939b453`); `main` = `d5d692f` không đổi |
| Nguồn | `report/ETEK_WCS_FULL_CODE_REVIEW_2026-11-16.md` – AUDIT-001, 002, 003, 004, 006 |
| Trạng thái | IMPLEMENTED trên `develop`; **chưa merge `main`**. Docker/RCS/PLC thật: NOT VERIFIED. REAL PLC TESTED = NO |

## 1. Hành vi đã thay đổi

| Audit | Trước | Sau |
|---|---|---|
| 001 | Production + simulator: task được RcsSimulator "chấp nhận" rồi treo; AGV có mã robot hiện Online giả | Production + simulator: tạo task bị từ chối `RCS_NOT_CONNECTED` (422), không đặt chỗ, không gọi RCS; dispatch task đã có bị bỏ qua; AGV hiển thị `Unknown / RCS_NOT_CONNECTED`; cảnh báo khi khởi động; `/api/system/health` có `integration.rcs`. Cho phép thử có giám sát bằng `Rcs:AllowSimulatorInProduction=true`. Development/Testing/Staging/Demo không đổi |
| 002 | Intake + scheduler trong một `try`: lỗi intake bỏ qua cả lượt scheduler; một request lỗi không phải `AppException` thoát khỏi vòng | Hai phạm vi exception độc lập; mỗi request intake được cô lập (lỗi → log, **không ack** để PLC trình lại; khoá `RequestKey` giữ idempotency); lỗi ack cũng được cô lập |
| 003 | Mỗi tick thử lại dispatch; 5 lần ≈ 5 s → Failed "uncertain" | Thử lại cách nhau 2 s, 4 s, 8 s … (tối đa 60 s, cấu hình); `MaxDispatchAttempts` giữ 5 (≈ 30 s mặc định); vẫn query trước khi gửi lại, `RcsRequestCode` không đổi; hết hạn mức → Failed uncertain, **giữ reservation** |
| 004 | Continue/cancel gọi RCS mỗi tick, mỗi vòng ghi một dòng lịch sử, không giới hạn | Cách nhau theo backoff; tối đa `RcsMaxAttempts` (30) lần tự động rồi dừng, ghi 1 dòng `RCS_*_RETRY_EXHAUSTED`, đặt `RequiresReconciliation`, giao cho người vận hành; kết quả giống nhau chỉ ghi một lần (`NoteIfChanged`); RCS sống lại trong hạn mức → tự phục hồi |
| 006 | Chỉ xử lý Cancelled/NotFound/Executing/Created | Xử lý tường minh 8 trạng thái (bảng bên dưới) |

| Trạng thái RCS khi CANCEL_PENDING | Xử lý |
|---|---|
| Cancelled | Hủy xác nhận (như cũ) |
| Created / Executing | Gửi lại cancel, cách nhau theo backoff |
| Cancelling | Chờ, ghi 1 dòng `RCS_CANCEL_IN_PROGRESS` |
| Completed | `Failed` `CANCEL_RACE_RCS_COMPLETED`, **đối soát bắt buộc, giữ reservation/rack**, AGV được trả |
| Interrupted | `Failed` `CANCEL_RACE_RCS_INTERRUPTED`, như trên |
| NotFound, đã từng có mã task RCS | `Failed` `RCS_NOTFOUND_AFTER_ACCEPT` (bất thường, không coi là hủy), đối soát bắt buộc |
| NotFound, chưa từng có mã task RCS | Chỉ sau **2 lần liên tiếp** NotFound; nếu từng có lần dispatch → vẫn đối soát bắt buộc |
| Unknown / lỗi truy vấn | Chờ và thử lại theo backoff; ghi 1 dòng |

Mọi nhánh không có ghi nhận vật lý đều **không giải phóng rack/reservation**.

## 2. File thay đổi

**Mới:** `WCS.Application/Services/RcsRetryPolicy.cs`, `WCS.Infrastructure/Simulation/RcsDispatchGuard.cs`, migration `20261116132649_AddRcsRetryTracking` (+Designer), `tests/WCS.Tests/Integration/Cr008aReliabilityTests.cs`, `docs/decisions/ADR/ADR-0007-dispatch-and-scheduler-reliability.md`, báo cáo này.
**Sửa:** `TransportTask.cs`, `Abstractions/Integration.cs` (+`IRcsDispatchGuard`), `Abstractions/Services.cs` (+3 option), `TaskOrchestrator.cs`, `StationRequestProcessor.cs`, `AgvStatusService.cs`, `Infrastructure/DependencyInjection.cs`, `Simulators.cs` (công tắc kiểm thử `Unreachable/SetStatus/Forget`), `WcsDbContextModelSnapshot.cs`, `HostedServices.cs`, `WCS.Api/Program.cs`, `Api/Endpoints.cs` (health), `deployment/docker-compose.yml` (1 biến tuỳ chọn), `tests/.../Harness.cs` (hook `configure`; khoảng cách retry mặc định 0 cho test cũ), `CHANGE_REQUESTS.md`, `CHANGELOG.md`, `IMPACT_MATRIX.md`, `REQUIREMENT_TRACEABILITY.md`.

**Module bảo vệ được sửa (đã được CR cho phép, phạm vi hẹp):** `TaskOrchestrator`, `TaskScheduler` (không đổi), `HostedServices.cs`. **Không đổi:** `TaskStateMachine`, `InventoryService`, `FlowOptions`, `PlcWorker`, `McPlcEndpoint`, `McTcpClient`, MC frame, `StationIoHandler`, `HandshakeCoordinator`, `TaskScheduler`, PER_AGV, `StateTimeoutsSeconds` (vẫn rỗng), chính sách ưu tiên, chính sách AGV Offline.

## 3. Migration
`AddRcsRetryTracking` – cộng thêm, không mất dữ liệu: `transport_tasks.RcsRetryCount integer NOT NULL DEFAULT 0`, `RcsLastAttemptAt timestamptz NULL`. `Down` chỉ xoá hai cột. **Chưa áp dụng lên `wcs_db` hay Production** (test áp dụng lên DB tạm `wcs_test_*` đã bị xoá sau khi chạy).

## 4. Kiểm thử

| Bộ | Kết quả |
|---|---|
| `Cr008aReliabilityTests` (mới) | **25/25 đạt** |
| Toàn bộ backend (`dotnet test`, DB test cô lập `wcs_test_<guid>`, không dùng `wcs_db`) | **463/463 đạt**, 0 lỗi, 0 bỏ qua (1 m 16 s) |
| Frontend `eslint .` | 0 lỗi |
| Frontend `tsc -p tsconfig.app.json --noEmit` | 0 lỗi |
| Frontend `npm run build` | thành công (chỉ cảnh báo kích thước chunk có sẵn) |

Test mới phủ: Production+Simulator bị từ chối (không có task, không reservation, 0 lần gọi RCS); opt-in; Development/Testing/Staging và không có host environment giữ hành vi cũ; dispatch dừng khi guard báo không kết nối; AGV Unknown ở Production/Online ở Dev; một request intake lỗi không làm mất request khác; scheduler vẫn chạy khi intake lỗi mọi tick (SchedulerHostedService thật); backoff (25 tick không thời gian → 0 lần gọi thêm; mốc 1 s/2 s chính xác); timeout sau khi RCS đã tạo task không tạo bản sao; giới hạn dispatch + giữ reservation; lịch sử không bị lặp; continue/cancel bị chặn, dừng ở hạn mức, phục hồi khi RCS trở lại; cancel với Completed/Interrupted/Unknown/NotFound (có & chưa có mã RCS)/Executing/Cancelling; PER_AGV và AGV1+AGV2 đồng thời.

**Lỗi tìm thấy bởi test trong lúc làm và đã sửa:** nhánh "retry exhausted" ghi lại ghi chú mỗi tick (phép so sánh với dòng lịch sử cuối bị lệch vì `RequireReconciliation` thêm dòng khác) → dùng điểm đánh dấu một lần (`HasHistory`) và so sánh không phụ thuộc thứ tự.

## 5. Điểm chưa xác minh / giới hạn
- RCS HIK RCS-2000, PLC, AGV và Docker thật: NOT VERIFIED; hành vi với RCS thật (mã trạng thái, ngữ nghĩa NotFound sau khi huỷ) cần xác nhận khi có tài liệu API.
- Các giá trị mặc định (2 s, 60 s, 5 lần dispatch, 30 lần continue/cancel) là kỹ thuật, **chưa được chủ dự án/RCS engineer xác nhận**; `StateTimeoutsSeconds` vẫn rỗng nên task kẹt ở trạng thái khác vẫn không tự giải phóng.
- Task đã hết hạn mức retry (hoặc kết thúc Completed/Interrupted/NotFound-sau-accept) cần Admin đối soát qua chức năng có sẵn; khối lượng thực tế chưa đo.
- Khi `PollRequestsAsync` của simulator trả yêu cầu rồi xử lý lỗi, yêu cầu của simulator không được trình lại (hàng đợi bộ nhớ); với PLC thật yêu cầu được trình lại (chưa kiểm thử với PLC thật).
- AUDIT-005 (retry yêu cầu quyền PLC), 007, 008, 009, 010… **ngoài phạm vi CR-008A**, không đổi.
- Frontend không đổi: chỉ báo hiện có đã hiển thị "Chưa kết nối" khi mode Simulator; hiển thị thẻ AGV trên Docker/Production không được kiểm tra trực quan.
- Ba DB thử nghiệm cũ (`wcs_e2e`, `wcs_test_0e4898eb…`, `wcs_test_65783ebf…`) vẫn còn, không bị đụng tới.
