# CR-007 – Logging & Diagnostics: Gap Analysis và Implementation Results

| Mục | Giá trị |
|---|---|
| Dự án | ETEK WCS |
| Ngày audit | 2026-11-16 |
| Commit được audit | `d5d692fd4c6a4879b2872ce38049833984d01036` (branch `main`) |
| Bản chất tài liệu | **PHẦN A – Implementation Results (as built, 2026-11-16, sau khi triển khai).** **PHẦN B – Gap Analysis và đề xuất ban đầu (giữ nguyên như đã nộp trước khi triển khai).** |
| Bằng chứng | Đọc mã nguồn trong repo (đường dẫn ghi kèm từng kết luận); một phần có đối chiếu log thực của lần chạy smoke test trước đó trong phiên. **Chưa chạy ứng dụng để quan sát log trong audit này** |

**Quy ước phân loại:** `ALREADY IMPLEMENTED` · `NEEDS IMPROVEMENT` · `NOT IMPLEMENTED` · `NOT REQUIRED`.

---

# PHẦN A – IMPLEMENTATION RESULTS (as built)

| Mục | Giá trị |
|---|---|
| Trạng thái | **IMPLEMENTED trên `develop`**; chưa merge `main` (chờ nghiệm thu) |
| Commit CR-007 trên `develop` | `1d67433` Logging Core · `f6dd12c` Database & API · `47dd803` fix(logging) · `84be299` UI · `f52e87d` Deployment Logging · (+ commit Tests & Documentation) |
| Xác minh | Backend 438/438; script 20/20; trình duyệt 29/29; runtime thật 16/16; migration Up/Down/Up; hiệu năng đo trên 450.000 dòng |
| REAL PLC / RCS / DOCKER | **NOT VERIFIED** (không có thiết bị / Docker) |

## A1. Đã hoàn thành

| Hạng mục yêu cầu | Kết quả | Bằng chứng |
|---|---|---|
| Trang quản lý tập trung *Logging & Diagnostics / Nhật ký hệ thống* (Admin) | DONE | `pages/LogsPage.tsx`, route `/logs`, menu *Hệ thống*; trình duyệt 29/29 |
| Dashboard: tổng log, Information/Warning/Error/Critical, 6 nhóm, lỗi gần nhất, thiết bị có sự cố | DONE | `GET /api/logs/stats`; ảnh chụp `logs_overview.png` |
| Sáu nhóm SYSTEM, PLC, DATABASE, DEPLOYMENT, RCS_AGV, SECURITY_AUDIT | DONE | `LogCategories`, view `v_log_entries`, `LogClassifier` |
| Bộ lọc: from/to, nhóm, mức độ, thiết bị, nguồn, mã sự kiện, từ khóa | DONE | `LogSearchQuery`, `LogQueryService`; `LogStorageTests` |
| Bảng log + phân trang backend (mới nhất trước) | DONE | 25–200 dòng/trang, tổng số và số trang do API trả; test so khớp bảng UI = API |
| Chi tiết: thời gian, nhóm, mức độ, module, thiết bị, event, message, exception/stack trace, correlation id, IP/cổng, trạng thái trước/sau | DONE | `GET /api/logs/{id}`, hộp thoại chi tiết |
| Loading / Empty / Error; không dữ liệu giả | DONE | `Async`; build demo chỉ hiện "Không có dữ liệu" |
| Operator không truy cập | DONE | Không có menu, route bị chặn, API 403 (test + trình duyệt) |
| Serilog: bootstrap logger, startup summary, fatal | DONE | `Program.cs`, `LogSetup.cs`; runtime: dòng `FTL … STARTUP_FATAL`, `STARTUP_SUMMARY` |
| 4xx không bị ghi thành 500; giảm nhiễu `/healthz` | DONE | request log đặt trước exception handler; `LogSetup.RequestLevel`; runtime: `responded 401`, healthz chỉ ở Debug |
| PLC event → Serilog, kèm PLC ID, station, IP/cổng, event, level, trạng thái trước/sau, nguyên nhân | DONE | `PlcEventDispatcher` + `PlcEventEnricher`; `LoggingCoreTests` |
| Phân loại Timeout và MC End Code | DONE (Phương án A) | `PlcEventEnricher` (kind TIMEOUT / END_CODE / RECONNECTED / CONNECTION / WRITE_AMBIGUOUS) |
| Cảnh báo queue PLC bị drop / persist thất bại | DONE | `PLC_LOG_QUEUE_OVERFLOW`, `PLC_LOG_PERSIST_FAILED` |
| Lỗi Database, startup, shutdown, Background Services | DONE | `DbHealthMonitorHostedService` (`DB_LOST`, `DB_RESTORED`, `DB_MIGRATIONS_PENDING`), `STARTUP_FATAL`, `SHUTDOWN`, sink lưu Warning+ của hosted services |
| Throttle lỗi lặp | DONE | `ThrottlingSink` (60 s, đếm số lần bị bỏ qua); runtime: 28 s DB down → 1 dòng "Scheduler tick failed" |
| Lưu trữ + truy vấn tập trung, index, retention, không xóa audit | DONE | migration `AddSystemLog`; `system_log` 90 ngày (sàn 7); `audit_log`, `plc_comm_log` không đổi |
| API: stats, search, detail, lọc, phân trang backend, Admin only | DONE | `LogEndpoints`: 3 GET, không có endpoint ghi/xóa (405) |
| Deployment logging (7 script) tại `C:\ETEK-WCS\logs` | DONE | `_common.ps1` + script; `Test-DeploymentLogging.ps1` 20/20 |
| Lưu log Migrator + container khi thất bại | DONE (script) / NOT VERIFIED (Docker) | `Save-FailureLogs`, `failure-<run>-*.log` |
| Sự kiện Deployment tra cứu được trong trang | DONE | file JSONL → mount chỉ-đọc → `DeploymentLogImporter`; trình duyệt: nhóm *Triển khai* hiện sự kiện của `stop.ps1` thật |
| Không log secrets | DONE | `LogSanitizer` (ghi + đọc), `Hide-Secrets`; test 7 dạng secret + script 6 dạng |
| Lỗi ghi log không ảnh hưởng WCS | DONE | sink không ném lỗi, giữ hàng đợi khi DB lỗi, cô lập entry hỏng; runtime: bảng log hỏng nhưng API/`/healthz` vẫn 200 |

## A2. Khác với đề xuất ban đầu (có chủ ý)

| Đề xuất (Phần B) | Thực tế | Lý do |
|---|---|---|
| D1: throttle trong `HostedServices.cs` (cần duyệt riêng) | **Không sửa `HostedServices.cs`**; throttle là thành phần pipeline Serilog | Tránh chạm lớp bọc Scheduler; áp dụng cho mọi nguồn lỗi lặp |
| B4' (Phương án B: sửa `PlcWorker`) | Không dùng; Phương án A trong `PlcEventDispatcher` | Giữ nguyên `PlcWorker`/MC driver |
| "Không cần migration/API/UI" (mục 8 Phần B) | **Có**: 1 migration additive, 3 GET endpoint, 1 màn hình | Lệnh triển khai CR-007 yêu cầu trang tra cứu tập trung; thiết kế vẫn tận dụng `plc_comm_log`/`audit_log` (view, không sao chép) |
| Cột mới IP/Port/ErrorCode cho `plc_comm_log` | Một cột nullable `DetailsJson` | Cấu trúc linh hoạt, không đổi dữ liệu cũ |
| Đồng bộ deployment qua endpoint | Qua file JSONL + mount chỉ-đọc + importer | Không mở endpoint ghi từ bên ngoài |
| Tùy chọn: SignalR connect/disconnect, biến log size `.env` | **Chưa làm** | LOW, tùy chọn |

## A3. File thay đổi (46 file, +3.868 / −78 so với `d5d692f`, chưa tính tài liệu)

- **Backend mới:** `WCS.Domain/Logging/SystemLogEntry.cs`; `WCS.Application/Logging/{LogQuery,LogSanitizer}.cs`; `WCS.Infrastructure/Logging/{LogRow,LogQueryService,DeploymentLogImporter}.cs`; `WCS.Infrastructure/Plc/PlcEventEnricher.cs`; `WCS.Infrastructure/Persistence/Migrations/20261116105823_AddSystemLog{,.Designer}.cs`; `WCS.Runtime/LoggingHostedServices.cs`; `WCS.Api/Logging/{LogSetup,SystemLogSink,ThrottlingSink}.cs`; `WCS.Api/Api/LogEndpoints.cs`.
- **Backend sửa (additive):** `Program.cs`, `appsettings.json`, `appsettings.example.json`, `WcsDbContext.cs`, `WcsDbContextModelSnapshot.cs`, `Infrastructure/DependencyInjection.cs`, `Runtime/DependencyInjection.cs`, `Domain/Plc/PlcEntities.cs` (`DetailsJson`), `Infrastructure/Plc/PlcRuntimeInfrastructure.cs` (`PlcEventDispatcher`).
- **Frontend:** `pages/LogsPage.tsx`, `api/logTypes.ts`, `i18n/logs.ts` (mới); `App.tsx`, `api/client.ts`, `components/Layout.tsx`, `styles/pages.css`.
- **Deployment:** `_common.ps1`, `deploy|start|stop|backup|restore|status|plc-route-test.ps1`, `prune-logs.ps1` (mới), `docker-compose.yml`, `.env.example`, `README.md`, `docs/DEPLOYMENT_TEST_CHECKLIST.md`.
- **Test:** `tests/WCS.Tests/Logging/{LoggingCoreTests,LogStorageTests}.cs` (mới); `tests/deployment/Test-DeploymentLogging.ps1` (mới); `Support/Harness.cs` (reset thêm `system_log`); `Integration/AuthIntegrationTests.cs` (host test tắt sink DB mặc định).
- **Không sửa:** `TaskStateMachine`, `TaskOrchestrator`, `InventoryService`, `TaskScheduler`, `FlowOptions`, RCS Client, `PlcWorker`, `McPlcEndpoint`, `McTcpClient`, `Plc/Mc/*`, `StationIoHandler`, `HandshakeCoordinator`, `HostedServices.cs` (kiểm bằng `git diff --name-only d5d692f..HEAD`).

## A4. Migration, API, màn hình

- **Migration:** `20261116105823_AddSystemLog` – tạo `system_log` (index: OccurredAt; Category+OccurredAt; Level+OccurredAt; Device+OccurredAt; EventCode; ExternalId duy nhất khi có), thêm cột nullable `plc_comm_log.DetailsJson`, tạo view `v_log_entries`. Đã kiểm trên DB tạm có sẵn dữ liệu PLC/audit: Up → Down → Up đều đạt, dữ liệu cũ giữ nguyên.
- **API mới (Admin, chỉ đọc):** `GET /api/logs`, `GET /api/logs/stats`, `GET /api/logs/{id}` (`sys:`/`plc:`/`aud:` + guid). Header phản hồi `X-Correlation-Id` cho mọi request.
- **Màn hình:** `/logs` – menu **Hệ thống → Logging & Diagnostics** (chỉ Admin).
- **Cấu hình mới:** `Logging:ThrottleSeconds` (60), `Logging:SystemLog:Enabled` (true) / `RetentionDays` (90), `Logging:File:Path` (tùy chọn), `DeploymentLogs:Directory`, `WCS_LOG_DIR`.

## A5. Kết quả kiểm thử

| Nhóm | Kết quả |
|---|---|
| Backend (unit + tích hợp, PostgreSQL thật tạm) | **438/438** (410 cũ + 28 mới); chạy lặp 3 lần ổn định |
| Frontend | ESLint, `tsc -b`, `npm run build`, `npm run build:demo`: sạch |
| Trình duyệt (Chrome, DB/API tạm) | **29/29**: menu, 6 nhóm, KPI, bộ lọc, phân trang backend (bảng = API), chi tiết, trạng thái rỗng, sự kiện deployment thật, Operator bị chặn (menu/route/API 403), không lỗi JS |
| Runtime ứng dụng thật (DB tạm) | **16/16**: fatal có cấu trúc, startup summary không secret, đăng nhập sai = 401 (không ERR/stack), `/healthz` chỉ Debug, sự kiện PLC ở stdout, tìm được qua API, bảng log hỏng nhưng nghiệp vụ vẫn chạy, DB mất kết nối: 1 `DB_LOST` + throttle + không lộ mật khẩu |
| Script deployment (không cần Docker) | **20/20**: sự kiện/mức độ/id duy nhất, che 6 dạng secret, thư mục log không ghi được vẫn không làm hỏng script, `prune-logs` dry-run |
| Migration | Up/Down/Up đạt; dữ liệu PLC và audit cũ còn nguyên |
| Hiệu năng (450.000 dòng) | Trang mới nhất 0,20 s; lọc 0,08 s; đếm theo thiết bị 0,29 s; thống kê 24 h 0,11 s; từ khóa trang sâu 0,34 s |

**Lỗi do chính các bài test thật phát hiện và đã sửa (commit `47dd803`):** (1) timestamp Serilog mang offset cục bộ bị PostgreSQL từ chối → sink từng không lưu được gì (đã chuẩn hóa UTC); (2) một entry không lưu được có thể chặn mọi entry sau (nay cô lập từng entry khi DB còn sống); (3) thuộc tính phụ `IsEmpty` lọt vào JSON; (4) tiền tố "[]" rỗng ở thông điệp import.

## A6. Tiêu chí nghiệm thu của Phần B (mục 12) – trạng thái sau triển khai

| # | Tiêu chí | Trạng thái |
|---|---|---|
| 1 | Khởi động thất bại → dòng Fatal nêu nguyên nhân, exit ≠ 0 | PASS (runtime) |
| 2 | Startup summary đầy đủ, không secret | PASS (runtime) |
| 3 | Mọi sự kiện PLC chính xuất hiện ở stdout | PASS (unit + runtime simulator); **PLC thật: NOT VERIFIED** |
| 4 | `STATE` có trước→sau và IP/cổng | PASS (unit, detail API) |
| 5 | Polling thành công không sinh log; lỗi lặp ≤ 1 dòng/phút/khóa | PASS (logic hiện có + test mới) |
| 6 | `WRITE_AMBIGUOUS` luôn ở stdout dù DB lỗi | PASS (unit: dispatcher ghi stdout trước khi lưu DB) |
| 7 | Đăng nhập sai: 401, không stack trace | PASS (runtime) |
| 8 | `/healthz` không làm đầy log | PASS (runtime) |
| 9 | DB mất kết nối: ≤ ~1 dòng/phút/nguồn, có dòng khôi phục | PASS cho mất kết nối (runtime); dòng `DB_RESTORED` chỉ kiểm bằng logic (không kiểm trực tiếp khôi phục trong runtime) |
| 10 | Script ghi log file đầy đủ, lưu log khi lỗi, không có mật khẩu | PASS (không Docker); lưu log container: **NOT VERIFIED** |
| 11 | Lỗi ghi log không làm hỏng deploy/WCS | PASS |
| 12 | `status.ps1` hiện RestartCount/OOMKilled | Có trong mã; **NOT VERIFIED** (cần Docker) |
| 13 | Không đổi PLC/MC/Scheduler/Orchestrator/State machine; test cũ vẫn pass | PASS (diff + 410 test cũ) |
| 14 | Docker thật hiển thị sự kiện PLC và startup summary | **NOT VERIFIED** |

## A7. Chưa kiểm chứng / giới hạn

- **Docker:** việc gắn thư mục log chỉ-đọc vào container, importer trong container, `docker compose logs` khi thất bại, `status.ps1` đọc `RestartCount`/health: NOT VERIFIED (checklist H1–H6).
- **PLC/RCS/AGV thật:** MC End Code, timeout, reconnect thật: NOT VERIFIED. RCS chưa có adapter thật nên chưa có sự kiện RCS thật (hiện chỉ lỗi gọi RCS mô phỏng).
- **`DB_RESTORED`:** logic có, nhưng kịch bản khôi phục chưa chạy trực tiếp trong runtime test.
- **Quy mô:** view sắp xếp ba bảng khi truy vấn; ổn (≈0,2 s) tới ~450.000 dòng; cần xem lại nếu vượt ~1 triệu dòng. `audit_log` không có retention (ngoài phạm vi).
- **Chưa làm (tùy chọn, LOW):** log connect/disconnect SignalR phía server; biến `.env` điều chỉnh dung lượng log Docker.
- **Ràng buộc bảo vệ business logic:** không có yêu cầu nào buộc phải đổi các module bị cấm; mọi thay đổi ở `PlcEventDispatcher`, `Program.cs`, lớp mới và script. Không phát sinh yêu cầu cần xin phê duyệt riêng.

---

# PHẦN B – GAP ANALYSIS VÀ ĐỀ XUẤT BAN ĐẦU (nguyên văn trước khi triển khai)

---

## 1. Executive Summary

**Kết luận chung:** hệ thống đã có nền tảng logging tốt cho **PLC** (sự kiện có giới hạn tần suất, lưu `plc_comm_log`, hiển thị cho Admin, retention 30 ngày) và cho **hành vi người dùng** (`audit_log`). Các khoảng trống nằm ở **khả năng chẩn đoán vận hành** (khi cài đặt, khởi động, chạy trong Docker):

| # | Phát hiện chính | Mức |
|---|---|---|
| 1 | **Sự kiện PLC chỉ vào bảng `plc_comm_log` + SignalR, không vào Serilog/stdout.** Trong Docker, `docker compose logs backend` không thấy PLC Online/Offline/Timeout/WRITE_AMBIGUOUS. Nếu DB lỗi thì sự kiện PLC mất hẳn (chỉ có cảnh báo "PLC event delivery failed") | HIGH |
| 2 | **Lỗi nghiệp vụ 4xx bị ghi ở mức ERROR kèm stack trace**, và request log báo `500` cho request thực chất trả `401/4xx` (đã thấy trong log smoke test: đăng nhập sai). Nguyên nhân: `ExceptionHandlerMiddleware` luôn ghi Error và `UseSerilogRequestLogging` đặt sau `UseExceptionHandler`. Gây nhiễu, che lỗi thật | MEDIUM |
| 3 | **Khởi động lỗi chỉ ra dạng unhandled exception thô** (không có bootstrap logger, không `Log.Fatal`, không `CloseAndFlush`); không có dòng tóm tắt cấu hình khi khởi động (môi trường, chế độ PLC, cookie, version, DB host/db name) | MEDIUM |
| 4 | **Nguy cơ tràn log khi DB mất kết nối**: `SchedulerHostedService` ghi ERROR kèm stack trace mỗi tick (mặc định 1 giây), không chống trùng. Docker chỉ giữ 5×10 MB/container → lịch sử cũ bị cuốn trôi nhanh | MEDIUM |
| 5 | **Request log mức Information cho mọi request** (kể cả `/healthz` mỗi 15 giây và polling UI) làm tốn dung lượng log Docker | MEDIUM |
| 6 | **Script deployment không có log file**: chỉ `Write-Host`; `deployments.log` chỉ ghi commit trước/sau. Migrator chạy `--rm` nên log biến mất sau khi chạy | HIGH (cho cài đặt/triển khai) |
| 7 | Log PLC thiếu IP/Port, trạng thái trước→sau, và **không có sự kiện riêng cho Timeout / MC End Code** (chỉ nằm trong text `READ_BAD_*`/`CONNECT_FAILED`; End Code/trao đổi chi tiết chỉ lưu trong bộ nhớ, bản ghi cuối cùng, mất khi restart) | MEDIUM |
| 8 | SignalR server không ghi connect/disconnect | LOW |

**Hướng đề xuất (tối thiểu, additive, không migration, không API mới):**
1. Cầu nối **một chỗ** `PlcEventDispatcher.Log` → Serilog (structured, cùng nguồn đã được giới hạn tần suất) và gắn nhãn IP/Port + trạng thái trước→sau tại đây, **không sửa `PlcWorker` và MC driver**.
2. Chỉnh `Program.cs` + `appsettings.json`: bootstrap logger/`Log.Fatal`/tóm tắt khởi động, đặt request logging trước exception handler, hạ mức nhiễu (Diagnostics, `/healthz`).
3. Script deployment: log file ngoài repo `C:\ETEK-WCS\logs`, ghi bước/kết quả/exit code, lưu log migrator và container khi thất bại; `status.ps1` thêm `RestartCount`/`OOMKilled`/health log.
4. Chống trùng lỗi khi DB mất kết nối: cần chạm `HostedServices.cs` (lớp bọc Scheduler) → **cần phê duyệt riêng** vì liên quan tên "Scheduler" trong danh sách hạn chế.

Không cần: migration, API mới, thay đổi UI, hệ thống logging mới (ELK/Seq), retry/recovery mới.

---

## 2. Existing Logging Architecture

### 2.1 Các kênh log hiện có

| Kênh | Công nghệ | Vị trí | Nội dung | Lưu ở đâu | Retention |
|---|---|---|---|---|---|
| Application log | Serilog (Console sink) | `WCS.Api/Program.cs:13` (`UseSerilog`, `ReadFrom.Configuration`, `FromLogContext`); `appsettings.json` (Information; override `Microsoft.AspNetCore`/`EntityFrameworkCore` = Warning) | Dòng log text ra stdout; request log (`UseSerilogRequestLogging`, `Program.cs`) | stdout → Docker `json-file` 10 MB × 5 file/container (`deployment/docker-compose.yml:8-10`) | Xoay vòng theo Docker (~50 MB/container); không file log riêng |
| Audit log | `IAuditLog`/`Audited.RunAsync` | `Infrastructure/Auth/AuthInfrastructure.cs`; entity `Domain/Users/AuditEntry.cs`; bảng `audit_log` | Đăng nhập (thành công/thất bại), thao tác cấu hình, tài khoản, task, PLC (kèm before/after JSON, IP) | PostgreSQL | **Không có purge** (grep không thấy) |
| PLC communication log | `IPlcEvents` → `PlcEventDispatcher` (`Infrastructure/Plc/PlcRuntimeInfrastructure.cs:90`) | `plc_comm_log` (`PlcCommLogEntry`: OccurredAt, PlcId, StationId, Level, Event, Message ≤1000 ký tự) | Sự kiện PLC (xem mục 4) | PostgreSQL + đẩy SignalR `PlcLog` cho Admin | `Plc:LogRetentionDays` = 30, purge mỗi 6 giờ (`PlcRuntimeInfrastructure.cs:131,147`) |
| PLC diagnostics (bộ nhớ) | `McTcpClient.Record/Diagnostics` (`Infrastructure/Plc/Mc/McTcpClient.cs:192`) | Bộ đếm Requests/Failures/Timeouts/Reconnects, LastEndCode, LastError, **LastExchange + LastFailedExchange (hex request/response)** | Chỉ bản ghi gần nhất, trong RAM, Admin xem được | Mất khi restart | – |
| Health | `GET /healthz` (ẩn danh), `GET /api/system/health` (đăng nhập) | `Program.cs`, `Api/Endpoints.cs` | Trạng thái app/DB, scheduler, PLC runtime | Phản hồi HTTP | – |
| Nginx | access/error log | `deployment/docker/frontend/nginx.conf` (`/nginx-health`, `/healthz` tắt access_log) | Request qua proxy | stdout/stderr → Docker | Như trên |
| Frontend | `RealtimeProvider.tsx` | `onreconnecting/onreconnected/onclose` chỉ đổi trạng thái UI | Trạng thái "Thời gian thực" ở sidebar | Không ghi log | – |

### 2.2 Các điểm ghi log ứng dụng đã có (rà soát `ILogger` trong `src/backend`)

| File | Số lệnh | Nội dung chính (mức) |
|---|---|---|
| `WCS.Runtime/HostedServices.cs` | 6 | Scheduler tick failed (Error), AGV status refresh failed (Error), PLC runtime chờ catalog (Info)/không khởi động được (Warning), Handshake step failed (Error), Seeding failed (Error) |
| `WCS.Application/Services/TaskOrchestrator.cs` | 9 | Lỗi gọi RCS create/continue/cancel/query (Warning), duplicate request (Info), Realtime publish failed (Warning), Post-commit action failed (Error) |
| `WCS.Application/Services/TaskScheduler.cs` | 1 | Scheduler step failed (Error) |
| `WCS.Application/Services/AgvStatusService.cs` | 1 | AGV status query failed (Warning) |
| `WCS.Application/Services/StationRequestProcessor.cs`, `InventoryService.cs` | 1+1 | Station request rejected (Warning); task reconciled (Info) |
| `WCS.Application/Plc/PlcRuntime.cs` | 1 | PLC runtime started (Info) |
| `WCS.Application/Config/*` | 6 | Cập nhật cấu hình PLC/RCS/AGV/Location, kết quả Connection check, không giải mã được secrets (Info/Warning) — không ghi địa chỉ/secret |
| `WCS.Infrastructure/Auth/AuthInfrastructure.cs` | 7 | Audit write failed (Error), các trường hợp bootstrap admin (Error/Warning/Info) |
| `WCS.Infrastructure/Plc/PlcRuntimeInfrastructure.cs`, `PlcPersistence.cs` | 1+1 | PLC event delivery failed (Warning); PLC catalog seeded (Info) |
| `WCS.Infrastructure/Seeding/WcsDataSeeder.cs` | 2 | Seeded AGV/location (Info) |
| `WCS.Infrastructure/Persistence/EfUnitOfWork.cs` | 1 | Concurrency retry (Debug) |
| `WCS.Api/Api/ApiPlumbing.cs` | 2 | `Unhandled exception {TraceId}` (Error, cho 5xx); `Request rejected {Status} {Code}` (Info) |
| `WCS.Api/Program.cs` | 2 | Cảnh báo `SameAsRequest` ở Production; cảnh báo simulator ở Production (Warning) |

### 2.3 Ngăn chặn lộ thông tin nhạy cảm (đã xác minh trong mã)
- Chuỗi kết nối được dựng trong mã, không log (`Program.cs:12` chú thích; không có lệnh log nào in connection string/password).
- Cấu hình PLC/RCS: log chỉ ghi `enabled/auth mode`, "never log addresses or secrets" (`ConnectionConfigService.cs:46`).
- Mật khẩu bootstrap không được log (`AuthInfrastructure.cs`).
- Serilog request logging mặc định không ghi header/cookie/body.

---

## 3. Logging Gap Analysis (18 tình huống bắt buộc)

| # | Tình huống | Đã có log? | Vị trí hiện tại | Thiếu gì? | Đề xuất | Phân loại |
|---|---|---|---|---|---|---|
| 1 | Backend khởi động thành công/thất bại | Một phần | Log mặc định của host (`Hosting started`, `Now listening`), `PLC runtime started`, cảnh báo Production ở `Program.cs`. Thất bại: ngoại lệ chưa xử lý ra stderr | Không có bootstrap logger; không `Log.Fatal`/`CloseAndFlush`; không có tóm tắt khởi động (môi trường, version, chế độ PLC, cookie policy, `Integration:Mode`, DB host/port/tên DB – không password) | Bao `Program.cs` bằng try/catch + `Log.Fatal`, bootstrap logger, một dòng "Startup summary" | NEEDS IMPROVEMENT |
| 2 | Thiếu/sai cấu hình | Một phần | `BuildConnectionString` ném `InvalidOperationException` (thiếu password/connection); `ProductionSafety`, `ForwardedHeadersSetup`, `ResolveSecurePolicy` ném ngoại lệ có thông điệp rõ | Lỗi chỉ ra dạng stack trace thô, không có định dạng/tiền tố thống nhất, không liệt kê **tất cả** vấn đề cấu hình một lần | Bắt tại `Program.cs`, ghi `Fatal` với thông điệp rõ; (tùy chọn) gom kiểm tra cấu hình thành danh sách | NEEDS IMPROVEMENT |
| 3 | Database mất kết nối | Một phần | `/healthz` 503; `PlcRuntimeHostedService` Warning khi chưa khởi động được; mỗi tick scheduler "Scheduler tick failed" (Error + stack); `Audit write failed` | Không có sự kiện "DB lost/restored" (chuyển trạng thái); **không chống trùng** → ghi mỗi giây (xem #17); sự kiện PLC bị mất khi DB lỗi | Log chuyển trạng thái DB (1 lần khi mất/1 lần khi hồi phục); throttle lỗi lặp (cần phê duyệt, xem mục 6) | NEEDS IMPROVEMENT |
| 4 | Database migration thất bại | Một phần | Migrator (`efbundle`) in lỗi ra console container; backend `Seeding failed. Has the database migration been applied…` (Error) | Container chạy bằng `run --rm`: log biến mất sau khi chạy; `deploy.ps1` chỉ in ra màn hình; không có file log | Script ghi toàn bộ output migrator vào file log deployment; khi lỗi lưu thêm `docker compose logs` | NEEDS IMPROVEMENT |
| 5 | Backend service dừng/crash | Một phần | Lỗi từng hosted service được bắt và log (Scheduler/AGV/Handshake); worker PLC: `WORKER_CRASH`; Docker `restart: unless-stopped` | Không có log khi host **tắt** (shutdown reason); crash toàn tiến trình chỉ có stderr; `status.ps1` không hiện `RestartCount`/`OOMKilled` | `Log.Fatal` + flush; `status.ps1` hiển thị `RestartCount`, `OOMKilled`, `ExitCode` | NEEDS IMPROVEMENT |
| 6 | Docker deployment thất bại | Một phần | `Write-Host`/`Fail` trong `_common.ps1` + `deploy.ps1`; `deployments.log` (from/to commit) | Không có log file bước-theo-bước, exit code, thời gian; không lưu log container khi lỗi | Log file `C:\ETEK-WCS\logs\deploy-*.log` (mục 5, 6) | NOT IMPLEMENTED (log file) |
| 7 | PLC TCP connect thành công/thất bại | Có (DB) | `PlcWorker.SetState` → `STATE` (Info/Warning); `CONNECT_FAILED` (Warning, giới hạn 60 s/khóa) → `plc_comm_log`, SignalR (`PlcWorker.cs:108,196`) | Không có trong stdout; thiếu IP/Port; thiếu trạng thái trước→sau | Cầu nối Serilog + gắn IP/Port, prev→new tại `PlcEventDispatcher` | NEEDS IMPROVEMENT |
| 8 | PLC mất kết nối | Có (DB) | `LINK_LOST` (Warning, giới hạn) + `STATE Faulted` + `MarkAllDisconnected` (`PlcWorker.cs:~236`) | Như #7; chưa có ngữ cảnh thời gian từ lần thành công cuối | Như #7 (`LastSuccessAt` có sẵn trong `PlcWorkerStatus`; dùng được tại dispatcher) | NEEDS IMPROVEMENT |
| 9 | PLC timeout | Gián tiếp | Timeout làm tăng `Timeouts` (diagnostics RAM); văn bản lỗi "no response within N ms" lọt vào `READ_BAD_<station>`/`CONNECT_FAILED`/`LINK_LOST` | Không có sự kiện `TIMEOUT` riêng; không đếm/tổng hợp theo thời gian | Phân loại tại dispatcher theo dấu hiệu văn bản (Phương án A) hoặc thêm khóa sự kiện ở `PlcWorker` (Phương án B – cần phê duyệt) | NEEDS IMPROVEMENT |
| 10 | PLC reconnect thành công/thất bại | Một phần | Thất bại: `CONNECT_FAILED`/`LINK_LOST` (kèm backoff trong message); thành công: `STATE Connected`; bộ đếm `Reconnects` | Không có sự kiện "RECONNECTED" phân biệt với lần kết nối đầu; backoff hiện chỉ trong text | Tại dispatcher: nếu `Faulted→Connected` thì ghi "reconnected after N s" | NEEDS IMPROVEMENT |
| 11 | PLC trả MC End Code lỗi | Gián tiếp | `McTcpClient.SendAsync` ghi `LastEndCode`, `LastFailedExchange` (hex, outcome `END_CODE`) **chỉ RAM**; lỗi đọc xuất hiện dưới `READ_BAD_<station>` (text có `end code 0x....`) | Không có sự kiện `END_CODE` riêng; End Code không có cột riêng; mất khi restart | Giữ End Code trong message có cấu trúc ở Serilog; không đổi schema (Phương án A/B như #9) | NEEDS IMPROVEMENT |
| 12 | Đọc IO thất bại | Có (DB) | `READ_BAD_<station>` (Warning, giới hạn 60 s/khóa) và `STATION_ERROR_<station>` (Error) (`PlcWorker.cs:~208-217`) | Không ở stdout; chỉ phần tử đầu tiên được nêu trong message; khóa theo trạm nên thay đổi nguyên nhân trong 60 s bị che | Cầu nối Serilog; (Phương án B) đưa nguyên nhân vào khóa | NEEDS IMPROVEMENT |
| 13 | Ghi IO thất bại | Có (DB) | `WRITE_FAILED` (Error), `WRITE_DENIED` (Warning, `PlcRuntime.cs:143`), `WRITE` (Info); test-write có `audit_log` `PLC_TEST_WRITE` (Admin) | Không ở stdout | Cầu nối Serilog | NEEDS IMPROVEMENT |
| 14 | WRITE_AMBIGUOUS | Có (DB) | `WRITE_AMBIGUOUS` (Error, thông điệp cảnh báo "KHÔNG tự động ghi lại") (`PlcWorker.cs:136`); `PlcWriteAmbiguousException` ở `McTcpClient` | **Chỉ ở DB** – mất nếu DB lỗi tại thời điểm đó; không ở stdout | Cầu nối Serilog (Error) – đây là sự kiện an toàn quan trọng nhất | NEEDS IMPROVEMENT |
| 15 | PLC chuyển Online/Offline | Có (DB) | `STATE` + SignalR `PlcStatusChanged`; UI hiện trạng thái | Thiếu prev→new, IP/Port; không ở stdout | Cầu nối Serilog + làm giàu | NEEDS IMPROVEMENT |
| 16 | RCS lỗi/mất kết nối | Một phần | `TaskOrchestrator` Warning khi create/continue/cancel/query RCS lỗi (có `RequestCode`/`TaskId`); `AgvStatusService` Warning. RCS thật chưa có | Không có sự kiện chuyển trạng thái RCS Online/Offline; AGV status refresh nối RcsSimulator luôn "Online" | Chuẩn bị khuôn log khi có RCS adapter thật (CR RCS); hiện **NOT REQUIRED** cho CR-007 | NEEDS IMPROVEMENT (khi có RCS thật) |
| 17 | Scheduler/Background Service exception | Có | `SchedulerHostedService` (Error, mỗi tick), `TaskScheduler` Safe (Error theo bước), AGV status (Error), Handshake (Error) | **Không chống trùng**: DB lỗi → "Scheduler tick failed" 1 lần/giây với stack trace (tick mặc định 1000 ms, `appsettings.json` `Scheduler:TickIntervalMs`); AGV 10 s | Throttle "first + mỗi 60 s kèm số lần" ở lớp bọc `HostedServices.cs` | NEEDS IMPROVEMENT |
| 18 | SignalR disconnect/reconnect | Phía client có UI, phía server không | `RealtimeProvider.tsx:36-38` đổi badge; `WcsHub` không override `OnDisconnectedAsync` | Không có log server | Log Information connect/disconnect (không cookie, chỉ role); Warning khi có exception | NEEDS IMPROVEMENT (LOW) |

**Các tình huống bổ sung phát hiện trong audit:**

| # | Tình huống | Hiện trạng | Đề xuất | Phân loại |
|---|---|---|---|---|
| A | Lỗi nghiệp vụ 4xx (vd đăng nhập sai) | `UseExceptionHandler` (`Program.cs`) → `ExceptionHandlerMiddleware` luôn ghi `ERR An unhandled exception…` kèm stack; `UseSerilogRequestLogging` đặt **sau** nên ghi `responded 500` dù phản hồi thực là 401 (xác nhận bằng log smoke test của phiên) | Đặt `UseSerilogRequestLogging` trước `UseExceptionHandler`; đặt override `Microsoft.AspNetCore.Diagnostics` = Critical (lỗi 5xx đã được `WCS.Api.Errors` ghi Error) | NEEDS IMPROVEMENT |
| B | Request log mọi request ở Information | Bao gồm `/healthz` (Docker healthcheck 15 s), polling UI | `GetLevel` hạ `/healthz` xuống Debug; đo dung lượng; có thể nâng toàn bộ lên Warning | NEEDS IMPROVEMENT |
| C | Hàng đợi sự kiện PLC tràn (`BoundedChannel` 5000, `DropOldest`) | Mất sự kiện **không báo** | Dùng overload `itemDropped` để đếm và ghi Warning có giới hạn | NOT IMPLEMENTED |
| D | Ghi `plc_comm_log` thất bại | Chỉ Warning "PLC event delivery failed"; lô sự kiện bị bỏ, không thử lại | Cầu nối Serilog bảo toàn bản ghi trong stdout; **không** thêm retry (ngoài phạm vi) | NEEDS IMPROVEMENT |
| E | Retention `audit_log` | Không có purge | Ghi nhận; quyết định chính sách ở CR riêng (audit thường cần giữ lâu) | NOT REQUIRED (trong CR-007) |

---

## 4. PLC Logging Analysis

### 4.1 Luồng hiện tại (đã xác minh bằng mã)

```
PlcWorker (SetState / LogLimited / writes / crash)
        │  _events.Log(level, plcId, stationId, eventName, message)   ← fire-and-forget (TryWrite), không chặn worker
        ▼
PlcEventDispatcher  (Channel bounded 5000, DropOldest; batch ≤100)
        ├─► PersistAsync → bảng plc_comm_log (UoW)        ← mất nếu DB lỗi (chỉ Warning "PLC event delivery failed")
        ├─► IWcsNotifier → SignalR "PlcLog" (Admin only)
        └─► Purge mỗi 6 giờ (LogRetentionDays=30)
        ✗  KHÔNG đi tới Serilog/stdout
```

### 4.2 Đánh giá từng thành phần

| Thành phần | Có log gì | Đánh giá |
|---|---|---|
| `PlcWorker` (`Application/Plc/PlcWorker.cs`) | `STATE` (Info/Warning khi Faulted, chỉ khi đổi trạng thái), `CONNECT_FAILED`, `LINK_LOST`, `READ_BAD_<station>`, `STATION_ERROR_<station>` (đều qua `LogLimited`: tối đa 1/phút/khóa), `WRITE`, `WRITE_FAILED`, `WRITE_AMBIGUOUS`, `DISABLED`, `WORKER_CRASH` | **ALREADY IMPLEMENTED** cho nội dung và chống ngập log; thiếu prev→new, IP/Port |
| `McPlcEndpoint` / `McTcpClient` | Không dùng `ILogger`; ghi `PlcDiagnostics` + `PlcExchange` trong RAM (`Record`) | Chi tiết trao đổi (hex, End Code, latency) tốt cho Admin nhưng **không bền vững** |
| `SimulatedPlcEndpoint` | Không log riêng | **NOT REQUIRED** |
| TCP lifecycle | Connect/Disconnect phản ánh qua `STATE`; `OpenAsync` retry `attempt>0` tăng `Reconnects` | Không có thời gian kết nối/ngắt, không có "reconnected after N s" |
| Timeout | `TimeoutException("no response within N ms")` → `Timeouts++`; text trong `READ_BAD`/`CONNECT_FAILED` | Không có sự kiện riêng |
| End Code | `McEndCodeException`, `LastEndCode`, `LastFailedExchange` | Không có sự kiện riêng; không bền vững |
| Ambiguous write | `PlcWriteAmbiguousException` → `WRITE_AMBIGUOUS` (Error) | **Đúng nghiệp vụ**; cần bản sao ở stdout |
| `plc_comm_log` schema | `Id, OccurredAt, PlcId, StationId, Level, Event, Message(≤1000)` (`Domain/Plc/PlcEntities.cs:186`) | Không có cột IP/Port/ErrorCode/prev-state. **Không nên thêm cột** (cần migration) – đặt dữ liệu có cấu trúc ở Serilog |
| API `GET /api/plc/logs` | Lọc `plcId`, `stationId`, `minLevel`, `skip`, `take` (`Api/PlcEndpoints.cs:90`); Admin only | Đủ dùng; thiếu lọc theo thời gian/Event |
| UI *Nhật ký giao tiếp* (`pages/plc/PlcLogsTab.tsx`) | Lọc theo PLC, mức; cập nhật qua SignalR `PlcLog` | Đủ dùng; chưa lọc theo trạm/Event/thời gian, chưa xuất file (**NOT REQUIRED** cho CR-007) |
| Không ghi từng lần polling thành công | Đúng: chỉ ghi khi đổi trạng thái hoặc lỗi (giới hạn 60 s) | **ALREADY IMPLEMENTED** – yêu cầu CR-007 đã thỏa |
| Không để lỗi ghi log ảnh hưởng PLC | `TryWrite` không chặn, dispatcher có `try/catch` | **ALREADY IMPLEMENTED** |

### 4.3 Khoảng trống PLC cụ thể

1. Không có bản sao ở stdout/Docker log (HIGH).
2. `STATE` thiếu trạng thái **trước** (chỉ có trạng thái mới + lý do) và thiếu IP/Port.
3. Timeout và End Code không phải sự kiện độc lập; End Code/exchange chi tiết không được lưu bền.
4. `LogLimited` khóa theo `READ_BAD_<station>`: nếu nguyên nhân đổi trong 60 s thì sự kiện mới bị bỏ (chấp nhận được; ghi nhận).
5. Mất sự kiện âm thầm khi hàng đợi đầy hoặc DB lỗi.

---

## 5. Deployment Logging Analysis

### 5.1 Hiện trạng

| Thành phần | Log hiện có | Thiếu | Phân loại |
|---|---|---|---|
| `deploy.ps1` | `Write-Step/Write-Ok/Fail` ra console; `deployments.log` (`from=…to=…`) trong `C:\ETEK-WCS\config\` | Không log file bước-theo-bước, thời gian, exit code từng lệnh; không lưu output `docker compose build/run` | NEEDS IMPROVEMENT |
| `start.ps1`, `stop.ps1` | Console | Không log file | NOT IMPLEMENTED |
| `backup.ps1` | Console (đường dẫn file, kích thước) | Không log file/lịch sử backup | NOT IMPLEMENTED |
| `restore.ps1` | Console; tạo backup `pre-restore` | Không log file; lệnh `RESTORE` xác nhận không được ghi lại | NOT IMPLEMENTED |
| `status.ps1` | Hiện state/health/uptime/ExitCode/image | Thiếu `RestartCount`, `OOMKilled`, `Health.Log` (lệnh kiểm tra cuối + output) | NEEDS IMPROVEMENT |
| `plc-route-test.ps1` | Console (reachable/NOT reachable) | Không log file | NOT IMPLEMENTED |
| Docker Compose logging | `json-file`, `max-size 10m`, `max-file 5` cho cả 4 service | Chưa điều chỉnh được qua `.env`; migrator `run --rm` không lưu | NEEDS IMPROVEMENT |
| Backend container stdout/stderr | Serilog Console + stderr cho crash | Xem mục 3 | NEEDS IMPROVEMENT |
| Migrator errors | stdout container (mất sau `--rm`) | Cần chụp lại | NEEDS IMPROVEMENT |
| Nginx errors | stderr container; `access_log off` cho `/nginx-health`,`/healthz` | Không có `error_log` mức tùy chỉnh (mặc định `error` của image) | ALREADY IMPLEMENTED (đủ dùng) |
| Secrets trong log script | Script không in `.env`; `Read-EnvFile` không echo giá trị | Phải giữ nguyên khi thêm log | ALREADY IMPLEMENTED (cần giữ) |

### 5.2 Đề xuất log deployment

- Thư mục: `C:\ETEK-WCS\logs\` (ngoài repo, cạnh `config\` và `backups\`; biến `WCS_LOG_DIR` ghi đè).
- Tệp: `deploy-YYYYMMDD.log` (một file/ngày/script gộp) với dòng: `timestamp | script | step | result | exit code | message`.
- Hàm `Write-WcsLog` trong `_common.ps1`; `Write-Step/Write-Ok/Write-Warn2/Fail` gọi hàm này (không đổi chữ ký hàm hiện có).
- Lấy output lệnh Docker: `Invoke-Compose` hiện đẩy ra `Out-Host`; đổi sang `Tee-Object` vào file (bỏ qua giá trị bí mật vì không có trong output).
- Khi bước **thất bại**: tự lưu `docker compose logs --tail 200` của `migrator`, `backend`, `postgres`, `frontend` vào `C:\ETEK-WCS\logs\failure-<time>\`.
- **Không ghi** giá trị `.env`, mật khẩu, nội dung `POSTGRES_PASSWORD`; chỉ ghi **tên biến** thiếu/sai.
- Retention: **không tự xóa**; cung cấp tùy chọn thủ công `-PruneLogsOlderThanDays <n>` (mặc định không chạy) để tuân thủ "không tự ý xóa log lịch sử".
- Lỗi ghi log phải bị nuốt (try/catch) để không làm hỏng quy trình deploy.

---

## 6. Proposed Minimal Architecture

**Nguyên tắc:** dùng lại Serilog + `audit_log` + `plc_comm_log`; mỗi dữ liệu có **một nguồn sự thật**, bản sao ở stdout chỉ phục vụ vận hành (không phải hệ thống logging thứ hai).

### 6.1 A – Application logging (Serilog)

| ID | Thay đổi | Vị trí | Ghi chú |
|---|---|---|---|
| A1 | Bootstrap logger + `try/catch` toàn `Program.cs` + `Log.Fatal` + `Log.CloseAndFlush()` | `WCS.Api/Program.cs` | Lỗi cấu hình/khởi động có thông điệp có cấu trúc trong Docker log |
| A2 | Dòng "Startup summary" (Information): `Environment`, `Version` (assembly/`WCS_VERSION`), `Plc:Mode`, `AllowLiveConnection`, `Integration:Mode`, cookie policy, `DataProtection` có/không, DB host/port/tên DB (**không password**) | `WCS.Api/Program.cs` | Giúp chẩn đoán cài đặt |
| A3 | Đặt `UseSerilogRequestLogging` **trước** `UseExceptionHandler`; `GetLevel`: `/healthz` → Debug, 5xx → Error, 4xx → Warning/Information | `WCS.Api/Program.cs` | Sửa lệch status (500 so với 401) |
| A4 | Override `Microsoft.AspNetCore.Diagnostics` = `Critical` | `WCS.Api/appsettings.json` | 5xx vẫn được `WCS.Api.Errors` ghi Error (`ApiPlumbing.cs:60`) |
| A5 | Thông tin cấu trúc: dùng message template, không nối chuỗi; thêm property `Module` | Các điểm log mới | Lọc theo module trong Docker log |
| A6 | (Tùy chọn) `WcsHub.OnConnectedAsync/OnDisconnectedAsync`: Information (role, không cookie), Warning khi có exception | `WCS.Api/Api/ApiPlumbing.cs` | LOW |

### 6.2 B – PLC communication logging

| ID | Thay đổi | Vị trí | Ghi chú |
|---|---|---|---|
| B1 | **Cầu nối Serilog:** trong `PlcEventDispatcher.Log(...)` ghi thêm `_logger.Log(mappedLevel, "PLC {PlcId}/{StationId} {Event}: {Message}", …)` | `Infrastructure/Plc/PlcRuntimeInfrastructure.cs` | Nguồn đã giới hạn tần suất (`LogLimited` + chỉ khi đổi trạng thái) → không ngập log; độc lập với DB. Level: Info→Information, Warning→Warning, Error→Error (`WORKER_CRASH`/`WRITE_AMBIGUOUS` → Error) |
| B2 | **Làm giàu tại dispatcher:** IP/Port (lấy lazy từ `IPlcRuntime`/cấu hình tại thời điểm xử lý, tránh vòng phụ thuộc), trạng thái **trước→sau** (dispatcher nhớ trạng thái gần nhất theo `plcId` từ sự kiện `PlcStatusChanged`), "reconnected after N s" khi `Faulted→Connected` | `PlcRuntimeInfrastructure.cs` | Không đổi `PlcWorker`; không đổi schema (thông tin nằm ở Serilog; có thể thêm vào `Message` ≤1000 ký tự) |
| B3 | Đếm và cảnh báo khi hàng đợi bỏ sự kiện (`itemDropped` của `Channel.CreateBounded`) và khi `PersistAsync` lỗi | `PlcRuntimeInfrastructure.cs` | Giới hạn 1 cảnh báo/phút |
| B4 (**Phương án A**, mặc định) | Phân loại Timeout/End Code tại dispatcher theo mẫu văn bản (`no response within`, `end code 0x`) để gắn nhãn `TIMEOUT`/`END_CODE` vào dòng Serilog | `PlcRuntimeInfrastructure.cs` | Không chạm `PlcWorker`/`McTcpClient`; phụ thuộc định dạng thông điệp (cần test) |
| B4' (**Phương án B**, cần phê duyệt riêng) | Thêm khóa sự kiện `READ_TIMEOUT`/`READ_END_CODE`/`RECONNECTED` ngay tại các lệnh `LogLimited`/`SetState` hiện có | `Application/Plc/PlcWorker.cs` | Chạm file bị hạn chế; chính xác hơn nhưng **không làm trong CR-007 nếu không được duyệt** |
| B5 | Không ghi từng lần polling thành công; không đổi giao thức, polling, timeout, retry, reconnect | – | Giữ nguyên |

### 6.3 C – Deployment logging
Mục 5.2 (hàm `Write-WcsLog`, log file ngoài repo, lưu log khi thất bại, `status.ps1` bổ sung `RestartCount`/`OOMKilled`/`Health.Log`). Tùy chọn: biến `.env` `WCS_LOG_MAX_SIZE`/`WCS_LOG_MAX_FILE` cho compose (mặc định 10m/5) để chỉnh dung lượng mà không sửa compose.

### 6.4 D – System diagnostics (không đổi hành vi)

| ID | Thay đổi | Vị trí | Ghi chú |
|---|---|---|---|
| D1 | Throttle lỗi lặp của hosted service: ghi lần đầu, sau đó 1 lần/60 s kèm số lần lặp và thời điểm hồi phục ("recovered after N failures") | `WCS.Runtime/HostedServices.cs` (lớp bọc) | **Cần phê duyệt riêng** vì liên quan Scheduler wrapper. Không sửa `TaskScheduler.RunOnceAsync`, không thêm retry/recovery |
| D2 | Log chuyển trạng thái DB (lost/restored) dựa trên kết quả các thao tác hiện có | `HostedServices.cs` hoặc `/healthz` | Không thêm cơ chế phục hồi |
| D3 | Không tạo hệ thống log mới (ELK/Seq/file sink) | – | **NOT REQUIRED** ở bước này |

---

## 7. File Impact List

### 7.1 Cần thay đổi (nếu được phê duyệt)

| File | Class / method | Thay đổi | Mức rủi ro |
|---|---|---|---|
| `src/backend/WCS.Api/Program.cs` | Top-level | Bootstrap logger, try/catch + `Log.Fatal`, startup summary, thứ tự request logging, `GetLevel` | Thấp |
| `src/backend/WCS.Api/appsettings.json` | `Serilog.MinimumLevel.Override` | Thêm override `Microsoft.AspNetCore.Diagnostics` | Rất thấp (cấu hình) |
| `src/backend/WCS.Infrastructure/Plc/PlcRuntimeInfrastructure.cs` | `PlcEventDispatcher.Log`, `RunAsync/PersistAsync`, khởi tạo `Channel` | Cầu nối Serilog, làm giàu, cảnh báo drop/persist lỗi | Thấp–trung bình (đường nóng nhưng không chặn) |
| `src/backend/WCS.Api/Api/ApiPlumbing.cs` | `WcsHub` | (Tùy chọn) override connect/disconnect | Rất thấp |
| `src/backend/WCS.Runtime/HostedServices.cs` | `SchedulerHostedService`, `AgvStatusHostedService`, `HandshakeHostedService` | Throttle log lỗi lặp (**cần phê duyệt riêng**) | Thấp (chỉ log) |
| `deployment/scripts/_common.ps1` | `Write-Step/Write-Ok/Write-Warn2/Fail/Invoke-Compose` + hàm mới `Write-WcsLog` | Log file | Thấp |
| `deployment/scripts/deploy.ps1`, `start.ps1`, `stop.ps1`, `backup.ps1`, `restore.ps1`, `status.ps1`, `plc-route-test.ps1` | – | Gọi logging, lưu log khi lỗi, `status.ps1` thêm trường | Thấp |
| `deployment/docker-compose.yml`, `deployment/.env.example` | `x-logging` | (Tùy chọn) biến kích thước log | Rất thấp |
| `deployment/README.md`, `docs/change-management/*`, `docs/decisions/ADR/` | – | Tài liệu CR-007 | – |
| `tests/WCS.Tests/...` | File test mới | Xem mục 10 | – |

### 7.2 Tuyệt đối KHÔNG cần sửa

`TaskStateMachine`, `TaskOrchestrator`, `InventoryService`, `TaskScheduler` (logic), `PlcWorker` (trừ Phương án B nếu được duyệt), `McPlcEndpoint`/`McTcpClient`/mọi file `Plc/Mc/*`, `StationIoHandler`, `HandshakeCoordinator`, `PlcRuntime`, Domain, migration, `docs/specification/*`, frontend (không bắt buộc).

---

## 8. Database / API / UI Impact

| Hạng mục | Cần không | Chi tiết |
|---|---|---|
| Migration / thay đổi DB | **Không** | Dùng nguyên `plc_comm_log`, `audit_log`; thông tin có cấu trúc nằm ở Serilog |
| API mới | **Không** | `GET /api/plc/logs` đã đủ; (tùy chọn sau) thêm tham số lọc thời gian/Event là additive |
| Thay đổi UI | **Không bắt buộc** | (Tùy chọn, CR sau) lọc theo trạm/Event/thời gian cho *Nhật ký giao tiếp* |
| Tận dụng code hiện tại | ~90% | Mọi thay đổi bổ sung tại các điểm chặn sẵn có (`Program.cs`, `PlcEventDispatcher`, `_common.ps1`) |
| Thay đổi cấu hình runtime | Rất ít | `appsettings.json` (override), `.env.example` (tùy chọn) |

---

## 9. Performance & Security Considerations

### 9.1 Hiệu năng / dung lượng

| Vấn đề | Đánh giá | Biện pháp |
|---|---|---|
| Không ghi mọi chu kỳ polling thành công | Đã đúng (chỉ khi đổi trạng thái/lỗi) | Giữ nguyên; test chặn hồi quy |
| Tránh ghi trùng lỗi liên tục | PLC: đã có (`LogLimited` 60 s). Hosted service: **chưa** | D1 (cần phê duyệt) |
| Dung lượng log Docker | Cố định ~50 MB/container (5×10 MB), mất lịch sử cũ khi ngập | Hạ nhiễu (A3/A4, healthz), throttle (D1), cho phép chỉnh qua `.env` |
| Ước tính ngập log (giả định, chưa đo) | Với ~5 request/s × ~250 byte/dòng ≈ 100 MB/ngày → 50 MB chỉ giữ ~12 giờ; DB mất kết nối + tick 1 giây × ~2–3 KB stack ≈ 170–250 MB/ngày | Cần đo thực tế khi nghiệm thu |
| Chi phí cầu nối PLC → Serilog | Thấp: chỉ vài sự kiện/phút/PLC (đã giới hạn) | – |
| Lỗi ghi log không được ảnh hưởng WCS | PLC đã không chặn (`TryWrite`); script mới phải nuốt lỗi ghi file | Bắt buộc trong thiết kế |

### 9.2 Bảo mật
- Không ghi password, token, cookie, connection string, nội dung `.env`: giữ nguyên quy tắc hiện có; log mới chỉ dùng property thuần (không `Destructure` object cấu hình).
- Startup summary chỉ có host/port/tên DB, **không** có password, không dump toàn bộ `IConfiguration`.
- Exception: ghi `ex.ToString()` cho `WORKER_CRASH` đã có; không có secret trong exception của socket/Npgsql ở các luồng này (kiểm tra bằng test chuỗi cấm).
- Script deployment: chỉ ghi **tên** biến lỗi, không ghi giá trị.
- Quyền truy cập: `C:\ETEK-WCS\logs\` nên cùng ACL với `config\`; log container chỉ truy cập được bởi người có quyền Docker.

### 9.3 Retention / rotation

| Nguồn | Hiện tại | Đề xuất |
|---|---|---|
| Docker log | 10 MB × 5 | Giữ; cho chỉnh qua `.env` |
| `plc_comm_log` | 30 ngày (`Plc:LogRetentionDays`), purge/6 giờ | Giữ nguyên |
| `audit_log` | Không purge | Ngoài phạm vi; CR riêng nếu cần |
| Log script deployment | Chưa có | File theo ngày, **không tự xóa**; dọn thủ công `-PruneLogsOlderThanDays` |
| Không tự ý xóa log lịch sử | Tuân thủ | Mọi purge mới phải có phê duyệt |

---

## 10. Regression Risk

| Rủi ro | Mức | Vùng ảnh hưởng | Biện pháp / cách kiểm thử |
|---|---|---|---|
| Cầu nối Serilog làm chậm/chặn `PlcEventDispatcher` | Thấp | Đường nóng PLC | Ghi log là thao tác đồng bộ nhẹ trong vòng lặp dispatcher (không nằm trong `PlcWorker`); test hiệu năng: 10.000 sự kiện không làm thay đổi thời gian poll; chạy lại toàn bộ `PlcWorkerTests`, `PlcRuntimeIntegrationTests` |
| Làm giàu dispatcher gây vòng phụ thuộc DI | Thấp | `PlcRuntime`↔`IPlcEvents` | Resolve lazy qua `IServiceProvider` trong vòng xử lý; test khởi động host |
| Đổi thứ tự middleware (request logging/exception handler) làm đổi phản hồi | Thấp | Pipeline HTTP | Test tích hợp: mã trạng thái và body lỗi không đổi (bộ `AuthIntegrationTests`, `RobustnessTests`, `ConfigIntegrationTests`) |
| Bootstrap logger/`Log.Fatal` ảnh hưởng `WebApplicationFactory` | Trung bình | Test host | `Program` là `partial`; thử trong test factory; không nuốt ngoại lệ khởi động (rethrow sau `Fatal`) |
| Throttle hosted service che lỗi thật | Thấp | Log | Luôn ghi lần đầu + định kỳ kèm số lần; test với clock giả |
| Script log làm hỏng deploy khi ổ đĩa đầy/ACL | Thấp | Deploy | Bọc try/catch; chỉ cảnh báo, không dừng deploy |
| Thay đổi mức log làm mất thông tin hữu ích | Thấp | Vận hành | Giữ mức Information cho sự kiện nghiệp vụ; chỉ hạ `/healthz`/Diagnostics |
| Phương án B (PlcWorker) | Trung bình | PLC | **Không làm** trừ khi được phê duyệt riêng; nếu làm: chỉ thêm khóa sự kiện, giữ nguyên luồng điều khiển; chạy lại toàn bộ test PLC |

**Kế hoạch kiểm thử đề xuất:**
1. Unit: cầu nối PLC → logger giả (`ILogger` fake): đúng mức, đúng property, không chứa chuỗi bí mật; giới hạn tần suất giữ nguyên.
2. Unit: phân loại Timeout/End Code (Phương án A) với các thông điệp thực của `McTcpClient`.
3. Integration: `Program` Production không có DB password → `Fatal` có thông điệp, host không khởi động.
4. Integration: đăng nhập sai → phản hồi 401, log request ghi 401 (không ghi 500, không stack trace).
5. Integration: `/healthz` không sinh log Information.
6. Chạy lại toàn bộ 410 test hiện có (không giảm coverage).
7. PowerShell: parse + chạy `Write-WcsLog` với thư mục tạm (ASCII, 5.1); thử khi thư mục không ghi được (không làm deploy thất bại); kiểm tra không có chuỗi mật khẩu trong file log.
8. Kiểm chứng trên Docker thật (khi có): `docker compose logs backend` hiển thị sự kiện PLC; mô phỏng DB down ghi tối đa ~1 dòng/phút.

---

## 11. Implementation Plan (chờ phê duyệt)

| Giai đoạn | Nội dung | Phụ thuộc | Ước lượng phạm vi | Phê duyệt cần |
|---|---|---|---|---|
| 0 | Chủ dự án duyệt báo cáo; quyết định các điểm ở mục 13 | – | – | Có |
| 1 | Cấu hình + `Program.cs` (A1–A5), test tích hợp | 0 | ~1 file code, ~1 file cấu hình, ~4 test | CR-007 |
| 2 | Cầu nối PLC → Serilog + làm giàu + cảnh báo drop (B1–B3, B4 Phương án A) | 1 | 1 file code, ~8 test | CR-007 |
| 3 | Deployment logging (`_common.ps1` + 7 script, README) | – (độc lập) | 8 file `.ps1`, README | CR-007 |
| 4 | Throttle hosted service (D1, D2) | 0 | 1 file code | **Phê duyệt riêng** (Scheduler wrapper) |
| 5 | (Tùy chọn) SignalR connect/disconnect, biến log size `.env` | 1 | Nhỏ | CR-007 |
| 6 | Cập nhật CHANGELOG, CHANGE_REQUESTS, ADR, Testing Report, Traceability, Impact Matrix | 1–5 | Tài liệu | – |
| 7 | Chạy toàn bộ test; kiểm chứng trên Docker khi có máy | 1–6 | – | – |

Mỗi giai đoạn là một commit riêng trên `develop`; không merge `main` nếu chưa được xác nhận.

---

## 12. Acceptance Criteria

| # | Tiêu chí | Cách kiểm chứng |
|---|---|---|
| 1 | Khởi động thất bại do cấu hình sai → Docker log có dòng `Fatal` nêu **tên** tham số sai (không giá trị bí mật), exit code ≠ 0 | Test tích hợp + chạy container |
| 2 | Khởi động thành công → có dòng "Startup summary" đầy đủ trường, **không** có password/secret | Test + kiểm tra chuỗi cấm |
| 3 | Mọi sự kiện PLC (`STATE`, `CONNECT_FAILED`, `LINK_LOST`, `READ_BAD_*`, `WRITE_FAILED`, `WRITE_AMBIGUOUS`, `WORKER_CRASH`) xuất hiện ở stdout với `PlcId`, thời gian, mức, nguyên nhân | Unit (logger giả) + chạy simulator |
| 4 | `STATE` chuyển đổi hiển thị trạng thái trước→sau và IP/Port (khi có) | Unit |
| 5 | Polling thành công không sinh log DB hay stdout; lỗi lặp ≤ 1 dòng/phút/khóa | Test giữ nguyên + test mới |
| 6 | `WRITE_AMBIGUOUS` luôn có mặt ở stdout ngay cả khi DB ghi lỗi | Unit (DB giả lỗi) |
| 7 | Đăng nhập sai: 1 dòng log mức Warning/Information, status 401, không stack trace | Integration |
| 8 | `/healthz` không làm đầy log Information | Integration |
| 9 | DB mất kết nối: không quá ~1 dòng lỗi/phút/nguồn kèm số lần lặp; có dòng khôi phục (nếu D1 được duyệt) | Test clock giả |
| 10 | Mọi script deployment tạo log file trong `C:\ETEK-WCS\logs`, có timestamp, bước, kết quả, exit code; lỗi được lưu kèm log container; không có mật khẩu/giá trị `.env` | Chạy script + `Select-String` chuỗi cấm |
| 11 | Lỗi ghi log (đĩa đầy/ACL) **không** làm hỏng deploy/WCS | Test cố ý lỗi |
| 12 | `status.ps1` hiện `RestartCount`, `OOMKilled` | Chạy script |
| 13 | Không có migration/API mới; 410 test hiện có vẫn pass; PLC/MC driver/Scheduler/Orchestrator/State machine không đổi (kiểm tra bằng `git diff --stat`) | CI/chạy test, diff |
| 14 | Docker thật: `docker compose logs backend` hiện sự kiện PLC và startup summary | Checklist trên Windows 11 |

---

## 13. Bảng phân loại tổng hợp

| Hạng mục | Phân loại |
|---|---|
| Serilog structured logging (Console) | ALREADY IMPLEMENTED |
| Docker thu log stdout/stderr + rotation (10 MB × 5) | ALREADY IMPLEMENTED |
| `audit_log` (đăng nhập, cấu hình, thao tác) | ALREADY IMPLEMENTED |
| `plc_comm_log` + API + UI + retention 30 ngày | ALREADY IMPLEMENTED |
| Giới hạn tần suất log PLC (không log từng chu kỳ), fire-and-forget | ALREADY IMPLEMENTED |
| Không lộ secrets trong log hiện có | ALREADY IMPLEMENTED |
| Global exception handling trả lỗi chuẩn + log 5xx | ALREADY IMPLEMENTED (nhưng gây nhiễu → xem A) |
| Health check `/healthz` | ALREADY IMPLEMENTED |
| Startup/shutdown/config-validation logging | NEEDS IMPROVEMENT |
| Mức log và status trong request logging | NEEDS IMPROVEMENT |
| Sự kiện PLC ra stdout; prev→new; IP/Port; reconnected | NEEDS IMPROVEMENT |
| Timeout / End Code là sự kiện riêng, bền vững | NEEDS IMPROVEMENT (Phương án A) / NOT IMPLEMENTED (cột riêng) |
| Chống trùng lỗi hosted service khi DB mất | NEEDS IMPROVEMENT |
| Sự kiện PLC bị rớt (queue đầy/DB lỗi) | NOT IMPLEMENTED |
| SignalR server connect/disconnect | NOT IMPLEMENTED |
| Log file cho script deployment | NOT IMPLEMENTED |
| Lưu log migrator/container khi deploy thất bại | NOT IMPLEMENTED |
| `status.ps1` RestartCount/OOMKilled/Health.Log | NOT IMPLEMENTED |
| Log từng chu kỳ polling thành công | NOT REQUIRED |
| Hệ thống logging mới (ELK/Seq/file sink) | NOT REQUIRED |
| Retry/recovery mới | NOT REQUIRED |
| Thêm cột vào `plc_comm_log` (migration) | NOT REQUIRED |
| API/UI mới | NOT REQUIRED |
| Retention `audit_log` | NOT REQUIRED (CR riêng) |
| Log riêng cho `SimulatedPlcEndpoint` | NOT REQUIRED |

---

## 14. Câu hỏi cần chủ dự án quyết định

1. Có đồng ý **cầu nối Serilog tại `PlcEventDispatcher`** (không sửa `PlcWorker`) và chấp nhận Phương án A (phân loại Timeout/End Code theo văn bản) thay vì Phương án B (sửa `PlcWorker`)?
2. Có cho phép **sửa `HostedServices.cs`** (chỉ phần ghi log, throttle) mặc dù liên quan tên "Scheduler"? (Nếu không: giảm nhẹ bằng tăng dung lượng log Docker.)
3. Có đồng ý đặt `UseSerilogRequestLogging` trước `UseExceptionHandler` và override `Microsoft.AspNetCore.Diagnostics` = Critical (đổi hành vi **log**, không đổi phản hồi HTTP)?
4. Thư mục log deployment: `C:\ETEK-WCS\logs` (mặc định đề xuất) và chính sách dọn log thủ công — đồng ý?
5. Có đưa các mục tùy chọn (SignalR connect/disconnect, biến log size `.env`) vào CR-007 hay để sau?

---

## 15. Giới hạn của audit này (NOT VERIFIED)

- Chưa chạy ứng dụng để quan sát log thực; các kết luận về mức độ nhiễu/ngập log là suy ra từ mã và từ một lần log smoke test trước đó (đăng nhập sai hiện `ERR … responded 500` dù trả 401).
- Ước tính dung lượng log ở mục 9.1 dựa trên giả định, **chưa đo**.
- Docker, PLC thật, RCS thật: **NOT VERIFIED** (không có thiết bị/Docker).
- Chưa kiểm tra index/hiệu năng truy vấn của `plc_comm_log` (không tìm thấy cấu hình bảng trong lần grep).
- `HandshakeHostedService` tick 250 ms: mã `StepAsync` không dùng DB (chỉ bộ nhớ) nên nguy cơ ngập log khi DB lỗi thấp, nhưng chưa chạy để xác nhận.

*Hết báo cáo. Không có thay đổi nào đối với source code, cấu hình, database hay tài liệu khác.*
