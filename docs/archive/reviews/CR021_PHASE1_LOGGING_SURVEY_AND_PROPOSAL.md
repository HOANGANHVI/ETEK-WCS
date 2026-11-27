# CR-021 – Giai đoạn 1: Khảo sát logging toàn hệ thống và đề xuất kiến trúc

Ngày: 2026-11-18. Baseline: `main` = `4c7e48c`. Trạng thái: **ĐỀ XUẤT – chưa sửa code, chưa tạo migration, chờ chủ dự án duyệt** (CLAUDE.md §3, §5).
Phạm vi khảo sát: toàn bộ `src/backend` (Api, Application, Domain, Infrastructure, Runtime) và trang Diagnostics của frontend. Mọi số liệu dưới đây lấy từ mã nguồn hiện tại.

## 1. Những điều khác với giả định trong CR-021

| Giả định trong yêu cầu | Thực tế trong mã nguồn |
|---|---|
| Export hiện có lựa chọn "toàn bộ thời gian lưu trữ" | **Không có.** Chỉ có `1h`, `24h` và khoảng tùy chọn, tối đa 31 ngày (`DiagnosticExportOptions.MaxRangeDays`) – [DiagnosticEndpoints.cs](../../../src/backend/WCS.Api/Api/DiagnosticEndpoints.cs), [DiagnosticsPage.tsx:8](../../../src/frontend/wcs-web/src/pages/DiagnosticsPage.tsx#L8) |
| Có thể xuất lượng lớn | ZIP dựng **trong bộ nhớ**, trên luồng request: tối đa 5 000 dòng/file, 20 MB tổng ([DiagnosticExportService.cs:12-21](../../../src/backend/WCS.Application/Diagnostics/DiagnosticExportService.cs#L12-L21)) |
| Có RCS/AGV thật để ghi log request/response | **Chỉ có simulator** (`RcsSimulator`, `ILegDispatcher` mô phỏng). Adapter thật chưa tồn tại (Q-MLEG-1, RCS vendor questions). Không có gì để "ghi request/response" ngoài giao diện `IRcsClient` |
| Có CorrelationId/TraceId thống nhất | **Chỉ trong phạm vi HTTP request**: `Program.cs:69-70` đẩy `ctx.TraceIdentifier` vào `LogContext` và header `X-Correlation-Id`. Worker nền, PLC, Scheduler, Routing không có |

## 2. Các nguồn log và dữ liệu hiện có

| Nguồn | Nơi lưu | Giữ bao lâu | Ai ghi | Ghi chú |
|---|---|---|---|---|
| Log ứng dụng (Serilog) | stdout + file tùy chọn | Docker 5×10 MB | toàn bộ `ILogger` (21 file có gọi log) | Mọi mức |
| `system_log` | PostgreSQL | 90 ngày (`Logging:SystemLog:RetentionDays`, tối thiểu 7) | `SystemLogSink` | **Chỉ ghi Warning trở lên hoặc sự kiện có `EventCode`** ([SystemLogSink.cs:ShouldPersist](../../../src/backend/WCS.Api/Logging/SystemLogSink.cs)); bỏ qua EF/Npgsql; bỏ qua nhóm PLC |
| `plc_comm_log` | PostgreSQL | 30 ngày (`Plc:LogRetentionDays`) | `PlcRuntime` events | Có `Stage`, `Code`, args JSON; **không có CorrelationId/TaskId** |
| `plc_exchange_log` | PostgreSQL | 300 dòng/PLC | `PlcExchangeRecorder` | Hex request/response MC; OK đọc lấy mẫu 1/5 s |
| `audit_log` | PostgreSQL | **không có purge** | `Audited.RunAsync`, `AuthService` | 47 hành động khác nhau; có Actor, IP, Before/After; **không có CorrelationId** |
| `task_history` | PostgreSQL | vĩnh viễn | `TransportTask` state machine | Mã `DetailCode` + args; có `TaskId`; **không có CorrelationId**; chỉ ghi chuyển trạng thái |
| `routing_events` | PostgreSQL | vĩnh viễn | RouteEngine, StationTransaction | Có `OrderId`, `LegId`, `TransactionId`, `Kind` |
| `inventory_transactions` | PostgreSQL | vĩnh viễn | `InventoryService` | Thay đổi Rack/Location |
| `station_requests`, `processed_events` | PostgreSQL | vĩnh viễn | `StationRequestProcessor` | Trạng thái xử lý yêu cầu từ PLC; idempotency |
| `v_log_entries` (view) | PostgreSQL | — | — | Hợp nhất `system_log` + `plc_comm_log` + `audit_log` cho màn hình Logs; **chưa gồm** `task_history`, `routing_events`, `inventory_transactions` |
| Log triển khai | `deployment-events-*.jsonl` | theo script | `deploy.ps1` → `DeploymentLogImporter` | Nhập vào `system_log` nhóm DEPLOYMENT |

Thành phần chạy nền: `SchedulerHostedService`, `AgvStatusHostedService`, `PlcRuntimeHostedService` (+ 4 `PlcWorker`), `HandshakeHostedService`, `RoutingHostedService`, `DatabaseSeedHostedService`, `LogMaintenanceHostedService`, `DbHealthMonitorHostedService`, `ResourceMonitor`.
Hai miền nghiệp vụ song song: **Task kiểu cũ** (`TransportTask`, `TaskOrchestrator`, `IRcsClient`) và **Routing cấu hình được** (`TransportOrder` / `TransportLeg` / `StationTransaction`, `ILegDispatcher`). Domain Routing **không giữ `TaskId`** nên chưa có khóa nối Leg ↔ Task.

## 3. Ma trận độ phủ logging hiện tại

Mức: **Đủ** = đủ để tái dựng; **Một phần** = có nhưng thiếu ngữ cảnh/ID/không xuất được; **Thiếu** = không có.

| # | Nhóm | Hiện có | Mức | Khoảng trống chính |
|---|---|---|---|---|
| 3.1 | System / Runtime | `STARTUP_SUMMARY`, worker start/stop (một phần), unhandled exception (`ApiPlumbing:60`), `RUNTIME_START` (PLC) | Một phần | Không có log start/stop thống nhất cho 9 hosted service; không có Fatal/unhandled cho exception ngoài HTTP (worker crash chỉ có ở PLC); version chỉ ở startup summary |
| 3.2 | AGV / AMR | `AgvStatusService` (1 log), `AgvHoldService` (1 log), `task_history` | Một phần | Không có log chuyển trạng thái AGV; không có pin/vị trí (dữ liệu chưa có trong `RcsAgvStatus`); không có reconnect (chỉ simulator) |
| 3.3 | RCS | `TaskOrchestrator` log lỗi create/continue/cancel/query; `RcsDispatchGuard` | Một phần | Không có log Request/Response, thời lượng, từng lần gọi thành công; không có mã lỗi RCS (adapter thật chưa tồn tại) |
| 3.4 | PLC | `plc_comm_log` có Stage/Code, `plc_exchange_log` hex, export P1 | **Đủ (cho PLC)** | Chưa nằm trong gói toàn hệ thống; không có CorrelationId/TaskId/TransactionId; retention 30 ngày ≠ 90 ngày |
| 3.5 | Task / Dispatching | `task_history` (chuyển trạng thái, nguồn), `TaskOrchestrator` 14 log | Một phần | Quyết định phân công AGV, lý do loại AGV, kiểm tra trước khi tạo task **không được ghi dạng có cấu trúc**; log Information không vào `system_log` (không có EventCode); retry có đếm nhưng không có từng lần |
| 3.6 | Business Logic | `RouteEngine` 3 log, `StationRequestProcessor` 9 log, `RouteConditionEvaluator` | Một phần | Lý do từ chối hành động có ở một số chỗ (mã lỗi API), nhưng không có sự kiện chẩn đoán thống nhất |
| 3.7 | Station / IO / Rack | `routing_events`, `station_requests`, `PlcRequestService`, handshake log PLC | Một phần | Không nối được sự kiện PLC của một Station với giao dịch/Task (chỉ trùng thời gian + StationId) |
| 3.8 | Location / Inventory | `inventory_transactions`, `InventoryService` 1 log | Một phần | Có dữ liệu giao dịch nhưng không nằm trong luồng log/export; không có log cho trường hợp không nhất quán ngoài `ReconciliationService` |
| 3.9 | Database | `DbHealthMonitor` (`DB_*`), exception Npgsql phân loại DATABASE, `EfUnitOfWork` 1 log | Một phần | Không đo thời gian truy vấn; không log deadlock/constraint theo mã; migration lỗi chỉ ở log migrator/deploy |
| 3.10 | API / SignalR | Serilog request logging (có thời gian xử lý, **chỉ stdout** vì là Information), `X-Correlation-Id`, exception 5xx | Một phần | Không lưu request log vào DB; `WcsHub` không log connect/disconnect/lỗi gửi |
| 3.11 | Auth / Audit | `audit_log` (login ok/fail, logout, đổi mật khẩu, 47 hành động có Actor/IP) | **Đủ** | Không có CorrelationId; không purge |
| 3.12 | Config / Deployment | `STARTUP_SUMMARY`, `ConnectionConfigService`/`OperationalConfigService` (6 log), deploy events, audit cấu hình | Một phần | Không có snapshot cấu hình đã che bí mật trong gói; không ghi giá trị cấu hình không hợp lệ/thiếu một cách thống nhất ngoài PLC |

Kết luận khảo sát: dữ liệu chẩn đoán **đã nằm rải rác ở 8 bảng**, nhưng (a) không có mô hình sự kiện chung, (b) không có khóa liên kết xuyên module ngoài `TaskId` trong `task_history` và `OrderId/LegId/TransactionId` trong `routing_events`, (c) các sự kiện mức Information (phần lớn quyết định nghiệp vụ) **không được lưu** vì `SystemLogSink` lọc theo mức.

## 4. Kiến trúc đề xuất (mở rộng, không tạo hệ thống logging thứ hai)

**Nguyên tắc:** giữ Serilog → `SystemLogSink` → `system_log`, `plc_comm_log`, `audit_log`, `task_history`, `routing_events`, `inventory_transactions` làm nguồn sự thật. Không thêm nơi ghi mới cho dữ liệu đã có.

### 4.1 Mô hình sự kiện chẩn đoán chuẩn hóa (đọc từ các nguồn)
Một lớp đọc `IDiagnosticEventSource` cho từng nguồn, mỗi nguồn ánh xạ sang một bản ghi chung với các trường ở mục 4 của CR-021 (TimestampUtc, Level, EventName, Category, Module, Component, CorrelationId, TaskId, AgvId, PlcId, StationId, RackId, Actor, Operation, Result, DurationMs, ErrorCode, ExceptionType/Message/StackTrace, Context…). Trường không có dữ liệu để **null**, không suy diễn. Chuẩn hóa tại thời điểm **xuất** cho dữ liệu cũ (không mất dữ liệu cũ), và tại thời điểm **ghi** cho dữ liệu mới.

### 4.2 Correlation xuyên module (cần migration cộng thêm, chờ duyệt)
- `DiagnosticContext` (AsyncLocal) mang `CorrelationId`, `TaskId`, `AgvId`, `PlcId`, `StationId`, `RackId`; một Serilog enricher đưa vào mọi log.
- HTTP: dùng `X-Correlation-Id` đến nếu hợp lệ, nếu không dùng `TraceIdentifier` như hiện nay.
- **Task:** lưu `CorrelationId` lúc tạo `TransportTask` (cột mới, nullable). Mọi chu kỳ Scheduler/Orchestrator xử lý task đó đặt lại `CorrelationId` + `TaskId` từ task. Nhờ vậy chuỗi *tạo (HTTP) → phân công (Scheduler) → gọi RCS → cập nhật trạng thái → hoàn thành* cùng một ID, không phụ thuộc request ban đầu còn sống.
- **Routing:** lưu `CorrelationId` ở `TransportOrder`; `StationTransaction` kế thừa; sự kiện PLC thuộc giao dịch đó mang `TransactionId`.
- **PLC:** chỉ gắn ID khi có căn cứ (sự kiện do một `StationTransaction`/handshake cụ thể gây ra). Poll định kỳ **không** gắn CorrelationId. Nơi không có khóa, ghi nhận liên hệ bằng `StationId` + thời gian, **không tự tạo liên kết**.
- Thêm các cột nullable (`TaskId`, `AgvId`, `PlcId`, `StationId`, `RackId`, `Module`, `Operation`, `Result`, `DurationMs`, `ErrorCode`) vào `system_log` (có index cho `CorrelationId`, `TaskId`) – **hoặc** giữ trong `DetailsJson` (xem quyết định D1).

### 4.3 Bổ sung sự kiện còn thiếu (ưu tiên quyết định và chuyển trạng thái, không log mọi dòng)
Phát bằng `ILogger` + `EventCode` (để vào `system_log`), mức Information cho quyết định, Warning/Error cho bất thường:
- Task: tạo (người/module, loại, điểm đi/đến, rack), kết quả kiểm tra điều kiện, **quyết định chọn/loại AGV kèm lý do**, từng lần gửi RCS (kết quả, thời lượng), retry, timeout, hoàn thành/thất bại/hủy + nguyên nhân.
- RCS: một bao bọc `IRcsClient` (decorator) ghi operation, requestCode, thời lượng, outcome, mã lỗi; payload đi qua `LogSanitizer`. Hiện chỉ áp dụng cho simulator; adapter thật dùng lại cùng decorator.
- System: start/stop của từng hosted service, worker crash, trạng thái runtime.
- DB: bộ chặn lệnh EF (`DbCommandInterceptor`) chỉ ghi lệnh **chậm hơn ngưỡng** và lỗi theo mã Npgsql (deadlock `40P01`, vi phạm ràng buộc `23xxx`, timeout); **không** ghi tham số/SQL đầy đủ.
- API/SignalR: ghi request log có thời lượng ở mức cần (≥ ngưỡng chậm hoặc ≥ 400, trừ probe) vào `system_log`; `WcsHub` connect/disconnect/lỗi gửi.
- Inventory/Location: ghi sự kiện bất thường (không nhất quán, từ chối) – giao dịch bình thường đã có ở `inventory_transactions`.
- Ngân sách khối lượng: mọi sự kiện mới đi qua `ThrottlingSink` hiện có; sự kiện lặp chỉ ghi khi đổi trạng thái.

### 4.4 Xuất gói toàn hệ thống
- Giữ nguyên endpoint và định dạng `PLC/` của export PLC hiện tại (không suy giảm).
- Thêm export toàn hệ thống dạng **tác vụ nền**: `POST /api/diagnostics/exports` (tạo job) → `GET …/{id}` (tiến độ + trạng thái từng nguồn) → `GET …/{id}/download`. ZIP ghi ra **thư mục tạm có quyền hạn chế** (không nằm trong web root), dựng bằng streaming theo trang, giới hạn dung lượng/thời gian, tự xóa sau N giờ.
- Phạm vi: toàn hệ thống (mặc định) / nhóm / module / thiết bị / mức lỗi / `CorrelationId` / `TaskId`. Xuất theo module vẫn kéo thêm các sự kiện cùng `CorrelationId`/`TaskId` từ module khác.
- Khoảng thời gian: 1 h, 24 h, **toàn bộ thời gian lưu trữ** (giới hạn thực tế = retention của từng nguồn, ghi rõ trong Manifest), tùy chọn; hiển thị múi giờ và khoảng thực xuất.
- Nội dung ZIP: `Summary.txt`, `Manifest.json`, `System_Info.txt`, `Health_Snapshot.json`, `Events/*.jsonl` (chia theo ngày/module), `Errors/errors.jsonl`, `Timeline/timeline.csv`, `Modules/<module>/…`, `PLC/…` (nguyên định dạng cũ), `Audit/audit.jsonl`, `Configuration/config.json` (đã che bí mật).
- Mỗi nguồn được bọc `Guard`: nguồn lỗi không làm hỏng gói; Summary và Manifest ghi rõ nguồn nào thiếu và vì sao, số dòng xuất/số dòng có trong khoảng, dòng bị cắt.
- Bảo mật: Admin-only (như hiện tại), mọi chuỗi qua `LogSanitizer` (mở rộng cho JSON payload), `Cache-Control: no-store`, Audit `DIAGNOSTIC_EXPORT` cho cả tạo và tải.
- UI: mở rộng `DiagnosticsPage` (phạm vi, khoảng thời gian, trạng thái job, tải về), song ngữ.

### 4.5 Những thứ **không** thay đổi
Logic nghiệp vụ, thuật toán dispatch, MC protocol, polling/handshake, quy tắc bảo vệ ghi PLC, các quy tắc được bảo vệ ở CLAUDE.md §2.

## 5. Kế hoạch triển khai đề xuất (chia lát, mỗi lát có thể merge độc lập)

| Lát | Nội dung | Migration | Rủi ro |
|---|---|---|---|
| A | Mô hình sự kiện chuẩn + các `IDiagnosticEventSource` + export toàn hệ thống dạng job (đọc dữ liệu **hiện có**) + Manifest/Summary/Health/Config + UI | Không | Thấp (chỉ đọc) |
| B | `DiagnosticContext` + enricher + `CorrelationId` ở `TransportTask`/`TransportOrder` + cột ID ở `system_log` | **Có (cộng thêm, nullable)** | Trung bình (chạm schema, cần backfill = null) |
| C | Sự kiện còn thiếu: Task/dispatch decision, RCS decorator, hosted service lifecycle, DB interceptor, API/SignalR, Inventory | Không | Trung bình (khối lượng log, hiệu năng) |
| D | Kiểm thử xuyên suốt (Tạo Task → AGV → RCS → Station/PLC → lỗi → xuất → tái dựng) + hiệu năng + rò rỉ bí mật | Không | Thấp |

Đề xuất chia CR-021 thành 4 lát A–D trên **một nhánh feature** nhưng commit riêng từng lát; có thể merge sau lát A nếu bạn muốn dùng sớm.

## 6. Quyết định cần chủ dự án duyệt

| ID | Câu hỏi | Đề xuất |
|---|---|---|
| D1 | Thêm cột ID vào `system_log` (migration cộng thêm, truy vấn nhanh theo `TaskId`/`CorrelationId`) hay chỉ giữ trong `DetailsJson` (không migration, truy vấn chậm hơn)? | Thêm cột nullable + index (an toàn, additive) |
| D2 | Thêm `CorrelationId` nullable vào `TransportTask` và `TransportOrder` (migration cộng thêm)? Đây là thay đổi schema của bảng nghiệp vụ, không đổi hành vi | Có |
| D3 | "Toàn bộ thời gian lưu trữ": chấp nhận giới hạn thực tế theo retention từng nguồn (PLC 30 ngày, system 90 ngày, audit/task không giới hạn) và ghi rõ trong Manifest? Hay đồng bộ retention lên 90 ngày? | Giữ nguyên retention, ghi rõ giới hạn; không đổi retention nếu chưa duyệt |
| D4 | Retention của `audit_log`/`task_history` đang vô hạn: với "toàn bộ thời gian" gói có thể rất lớn. Đặt trần (ví dụ 90 ngày gần nhất hoặc tối đa N dòng/nguồn) và ghi rõ? | Trần theo dung lượng + số dòng, cấu hình được |
| D5 | Thư mục tạm cho ZIP của job nền và thời gian giữ file (ví dụ 24 giờ) | Thư mục dữ liệu ứng dụng, giữ 24 giờ, dọn tự động |
| D6 | RCS/AGV thật chưa có adapter: chấp nhận chỉ chuẩn hóa log cho giao diện `IRcsClient` (decorator) và áp dụng cho simulator, thông tin AGV (pin/vị trí/mã lỗi) ghi nhận khi adapter thật có dữ liệu? | Có, và nêu rõ giới hạn này trong báo cáo cuối |
| D7 | Request log API vào `system_log`: ngưỡng chậm và có ghi mọi 4xx hay không (khối lượng) | ≥ 1 000 ms hoặc 5xx hoặc 401/403; 4xx nghiệp vụ không ghi |
| D8 | Bắt đầu từ lát A (không migration) rồi mới sang B? | Có |

## 7. Giới hạn đã biết trước khi triển khai
- Không thể thu thập từ RCS/AGV thật những gì adapter thật chưa tồn tại (request/response thật, mã lỗi RCS, pin, vị trí).
- Dữ liệu trước CR-021 không có `CorrelationId`: không thể dựng chuỗi xuyên module cho các giao dịch cũ ngoài những gì `TaskId`/`OrderId`/`TransactionId` đã nối được.
- `plc_exchange_log` chỉ giữ 300 dòng mỗi PLC và lấy mẫu lệnh đọc OK: gói không thể có toàn bộ lịch sử khung MC.
- Đo thời gian truy vấn DB chỉ có từ lúc bật interceptor.
