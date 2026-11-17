# Change Log

## v2.7 – CR-008B-5 AGV dispatch hold (2026-11-17)

- Change Request: CR-008B (sub-CR B-5). Status: IMPLEMENTED on `develop`; not merged. Owner decisions D-5 (M2), D-6, D-7 (P2), D-8 applied.
- Added: `agvs.DispatchHold` / `DispatchHoldReason` / `DispatchHoldAt` / `DispatchHoldTaskId` (migration `AddAgvDispatchHold`, additive); the hold is set in the same transaction as the terminal transition for Interrupted, Completed-while-cancelling without a drop, NotFound after accept, cancel retry limit, RCS_DISPATCH_UNCERTAIN, and on RcsEnded without a drop. `ProcessTaskAsync`, `TryDispatchNextAsync` and `DispatchToRcsAsync` honour it; queued tasks stay queued and are never moved to the other AGV.
- Added: Admin-only `POST /api/agvs/{id}/release-hold` (both confirmations + note, audited `AGV_DISPATCH_HOLD_RELEASE`), AGV card banner and Admin release form.
- Changed (owner decision): the cancel retry limit (D-1) now also marks the rack Unknown and the locations ReconciliationRequired (D-5/D-6); `InventoryService.ConfirmDropAsync` accepts a PLC drop for a rack that is Unknown but still belongs to the task (needed after RcsEnded).
- Unchanged: TaskStateMachine, PER_AGV, FlowOptions, priority, PLC/MC/handshake, retry defaults, StateTimeoutsSeconds, Agv.Enabled semantics.
- Tests: `Cr008b5AgvDispatchHoldTests` (15); four earlier tests updated to the new decisions; backend 506/506.
- Approved by: project owner (2026-11-17).

## v2.6 – CR-008B-4 Rack and inventory uncertainty (2026-11-17)

- Change Request: CR-008B (sub-CR B-4). Status: IMPLEMENTED on `develop`; not merged. No migration. Touches the protected `InventoryService` (one added method) with the owner's approval of CR-008B section 7.
- Added: `IInventoryService.MarkPhysicalUnprovenAsync`: when RCS reports Completed / Interrupted / no record after accepting a task and WCS cannot prove the physical result, the rack becomes `Unknown` (its WCS location kept) and the source / destination locations require reconciliation; nothing is released. Called through `FinishAsync(physicalUnproven: true)`.
- Unchanged: reservation rules, pick/drop/reconcile logic, `MarkUncertainAsync`, the cancel-confirmed and normal-completion paths, the retry-limit outcome (D-1: resource state kept), `Rack`/`Location`, no new state.
- Open: decisions D-5/D-6 (model and scope) were not chosen explicitly; the narrowest reading of CR-008B section 7 was applied. `RcsEnded` signal and `RCS_DISPATCH_UNCERTAIN` keep the old behaviour.
- Tests: `Cr008b4InventoryUncertaintyTests` (9); backend 491/491.
- Approved by: project owner (2026-11-17).

## v2.5 – CR-008B-3 CancelPending reconciliation (2026-11-17)

- Change Request: CR-008B (sub-CR B-3). Status: IMPLEMENTED on `develop`; not merged. No migration.
- Decision applied: D-1 - a task in CancelPending that reaches 30 automatic attempts stops retrying and becomes `Failed` with `RequiresReconciliation=true` (reason `CANCEL_RETRY_LIMIT_REACHED`); reservations, rack and location state are kept; `Failed` is not evidence that RCS cancelled.
- Removed: "two consecutive NotFound answers = Cancelled" (D-9 not approved; `task-state-machine.md` 3.9). NotFound is recorded (`RCS_CANCEL_NOTFOUND_NOT_CONFIRMED`) and never turned into Cancelled.
- Added: Admin reconciliation - `GET /api/tasks/reconciliation`, `GET /api/tasks/{id}/reconciliation`, `POST /api/tasks/{id}/reconciliation-check` (records an inconclusive check, releases nothing); web `ReconciliationPanel` + "Cần đối soát" filter; terminal tasks keep their final retry counter. The existing `POST /api/tasks/{id}/reconcile` is unchanged.
- Unchanged: `TaskStateMachine`, `TaskScheduler`, `InventoryService`, PER_AGV, `StateTimeoutsSeconds`, retry values.
- Tests: `Cr008b3CancelReconciliationTests` (7) + adapted CR-008A tests.
- Approved by: project owner (2026-11-17).

## v2.4 – CR-008B-2 PLC station intake reliability (2026-11-17)

- Change Request: CR-008B (sub-CR B-2). Status: IMPLEMENTED on `develop`; not merged.
- Added: `StationRequestRecord` + table `station_requests` (migration `AddStationRequests`, additive); `IStationRequestRepository`; every detected station request is stored before processing and kept until a task exists.
- Changed: `StationRequestProcessor` processes from the store: RCS not connected keeps the request (no attempt used); technical failures retry with backoff up to `Scheduler:RcsMaxAttempts`; accepted only after the task is committed; permanent business rejections unchanged (rejected + acknowledged).
- Unchanged: PLC handshake, addresses, `Request_Ack` (still not written), MC protocol, `PlcStationSignalService`, `TaskOrchestrator`.
- Tests: `Cr008b2PlcIntakeTests` (12); backend 474/474.
- Approved by: project owner (2026-11-17).

## v2.3 – CR-008B-1 Documentation correction (2026-11-17)

- Change Request: CR-008B (sub-CR B-1). Documentation only; no source, schema or configuration change.
- Corrected (history kept, corrections marked): CR-008A dispatch retry time (about 62 s, not 30 s) and continue/cancel limit (about 25 minutes); the false statement that an unacknowledged PLC station request is presented again by the PLC (`PlcStationSignalService` is edge-based, acknowledgement only logs); the unapproved "two NotFound = Cancelled" rule (decision D-9, withdrawn by B-3).
- Added: CR-008B entry with the owner's decisions (D-1 taken; D-7, D-9, REV-005 open).
- Files: CHANGE_REQUESTS.md, CHANGELOG.md, ADR-0007, CR-008A report, `docs/reviews/CR008B-1_DOCUMENTATION_CORRECTION.md`.
- Approved by: project owner (2026-11-17).

## v2.2 – CR-008A WCS dispatch and scheduler reliability (2026-11-16)

- Change Request: CR-008A (AUDIT-001, 002, 003, 004, 006 of `report/ETEK_WCS_FULL_CODE_REVIEW_2026-11-16.md`). Status: IMPLEMENTED on `develop`; not merged.
- Added: `IRcsDispatchGuard` / `RcsDispatchGuard` (Production + simulator => `RCS_NOT_CONNECTED`); `RcsRetryPolicy` (spaced retries); `TransportTask.RcsRetryCount/RcsLastAttemptAt/NoteIfChanged/HasHistory`; options `RcsRetryInitialDelayMs`, `RcsRetryMaxDelayMs`, `RcsMaxAttempts`; `Rcs:AllowSimulatorInProduction`; RCS simulator switches (`Unreachable`, `SetStatus`, `Forget`); migration `AddRcsRetryTracking`; health field `integration.rcs`.
- Changed: scheduler hosted service runs station intake and scheduler pass in separate exception scopes; station intake isolates each request; dispatch / continue / cancel follow-up is spaced and bounded; cancel reconciliation handles every RCS status explicitly; AGV status is Unknown (`RCS_NOT_CONNECTED`) instead of the simulator's Online when Production has no RCS adapter.
- Unchanged by design: TaskStateMachine, InventoryService rules, PER_AGV, FlowOptions, priority, PLC handshake/polling/timeout/retry, MC protocol, AGV Offline policy, `StateTimeoutsSeconds`.
- Software components affected: WCS.Domain (TransportTask), WCS.Application (TaskOrchestrator, StationRequestProcessor, AgvStatusService, options, guard interface), WCS.Infrastructure (guard, simulator, migration, DI), WCS.Runtime (SchedulerHostedService), WCS.Api (health, start-up warning), deployment/docker-compose.yml (one optional variable).
- Tests added/updated: `Cr008aReliabilityTests` (25); Harness gains `configure` hook and spacing 0 default for the pre-existing tests. Full backend suite 463/463.
- Approved by: project owner ("EXECUTE CR-008A"). Date: 2026-11-16.

## v2.1 – CR-007 Centralized logging and diagnostics (2026-11-16)

- Change Request: CR-007 (IMPLEMENTED on `develop`; Docker runtime, real PLC and real RCS NOT VERIFIED).
- **Screen:** *Logging & Diagnostics / Nhật ký hệ thống* (`/logs`, Admin only): dashboard (total, Information/Warning/Error/Critical, six groups, latest errors, devices with problems), filters (time, group, level, device, source, event code, keyword), backend-paged table and detail dialog (exception/stack trace, correlation id, PLC IP/port and previous/new state, audit before/after).
- **API (Admin only, read-only):** `GET /api/logs`, `GET /api/logs/stats`, `GET /api/logs/{id}`.
- **Database (migration `AddSystemLog`, additive):** table `system_log` (SYSTEM, DATABASE, DEPLOYMENT, RCS_AGV events), nullable `plc_comm_log.DetailsJson`, read-only view `v_log_entries` over `system_log` + `plc_comm_log` + `audit_log` (no data copied). Retention of `system_log` 90 days (floor 7); `audit_log` and `plc_comm_log` retention unchanged.
- **Logging core:** bootstrap logger + structured `STARTUP_FATAL`, `STARTUP_SUMMARY` (no secrets), `SHUTDOWN`, correlation id (`X-Correlation-Id`); request log written before the exception handler (handled 4xx are no longer logged as 500; `/healthz` is Debug); repeated Warning+ failures throttled (60 s) with a suppressed count; secret masking (`LogSanitizer`) on write and on read; database monitor (`DB_LOST` / `DB_RESTORED`, pending migrations).
- **PLC:** every PLC event also goes to the application log (stdout) with PLC id, station, IP/port, previous/new state, timeout / MC end-code classification and reconnect duration; overflow and persist-failure warnings. Implemented in `PlcEventDispatcher`; `PlcWorker`, MC driver, polling, timeout, retry and reconnect are unchanged.
- **Deployment logging:** scripts write `deploy-YYYYMMDD.log` and `deployment-events-YYYYMMDD.jsonl` to `C:\ETEK-WCS\logs`, save last-command and container logs on failure, mask secrets, never fail a deployment because of logging; the folder is mounted read-only into the backend, which imports the events (no network endpoint). `prune-logs.ps1` is manual and dry-run by default.
- Tests: backend 438/438 (410 + 28); `tests/deployment/Test-DeploymentLogging.ps1` 20/20; frontend lint/tsc/build/build:demo clean.
- Not changed: TaskStateMachine, TaskOrchestrator, InventoryService, TaskScheduler, FlowOptions, RCS client, PlcWorker, McPlcEndpoint/McTcpClient, StationIoHandler, HandshakeCoordinator, `HostedServices.cs`.

## v2.0 – CR-006 Dockerization and Windows 11 deployment (2026-11-07)

- Change Request: CR-006 (IMPLEMENTED as source; Docker build/runtime NOT yet executed - no Docker on the development machine; REAL PLC TESTED = NO).
- New `deployment/` package: `docker-compose.yml` (postgres 16, one-shot EF migrator, backend, Nginx frontend), Dockerfiles (multi-stage, non-root runtime, healthcheck), `nginx.conf` (SPA fallback, /api and SignalR WebSocket proxy), `.env.example`, PowerShell scripts (deploy, start, stop, status, backup, restore, plc-route-test) and `README.md`.
- Backend (approved): `Auth:CookieSecure` (Always | SameAsRequest; unset keeps the previous rule); anonymous `GET /healthz` (application and database reported separately, no sensitive data); Production start-up guard - `Plc:Mode=Simulator` is refused unless `Plc:AllowSimulatorInProduction=true` (and never together with `Plc:Integration:Enabled`); optional trusted `X-Forwarded-*` (`Deployment:TrustForwardedHeaders`, enabled only inside the compose network).
- Frontend: a notice on every screen while RCS/PLC are simulator-only ("chua ket noi RCS va PLC that").
- Forwarded headers are trusted only from the configured proxy (`Deployment:TrustedProxyNetworks`; compose pins Nginx to a fixed address; no "trust all" mode). Production logs a warning when `Auth:CookieSecure=SameAsRequest`. Documentation: key-ring protection and HTTPS + Always recommendation.
- Tests: backend 410/410 (398 + 12 new). Three existing Production-environment tests received the explicit simulator exception in their set-up (no assertion weakened).
- No database migration. No change to TaskStateMachine, FlowOptions, InventoryService, Scheduler, RCS client, MC driver, StationIoHandler or business rules. MQTT is not used by the source and has no container.

## v1.9 – CR-004 acceptance decisions and production UI cleanup (2026-11-07)

- Owner decisions applied: Operators monitor only (no IP, port, address or configuration; configuration APIs return 403); `/api/config/plc` is **DEPRECATED** (kept for compatibility, unused by every screen, no new work; removal needs its own CR after a usage review); device W stays in the supported list but its address base is NEEDS_CONFIRMATION (no polling/read/write of W until the PLC engineer confirms).
- Frontend: engineering tools (simulator controls, "Giả lập", "Ghi thử", "Chạy bước thiết bị tiếp theo") are shown only to Admin in Development builds (`import.meta.env.DEV`) or with the explicit `VITE_ENABLE_ENGINEERING_TOOLS=true`; a production build (`npm run build`) never shows them.
- Status honesty: with no real PLC/RCS attached the header shows **Chưa kết nối** (was "Mô phỏng"); PLC monitoring cards show "Chưa kết nối" and "Không có dữ liệu từ thiết bị" (no green "92 tốt" bar); the connection-test outcome "Mô phỏng" became "Chưa kiểm tra thiết bị".
- Backend (API layer only): `POST /api/plc/simulator/*` and `POST /api/plc/stations/{station}/signals/{signal}/test-write` return 409 `NOT_AVAILABLE_IN_PRODUCTION` when `ASPNETCORE_ENVIRONMENT=Production`. Development/Testing behaviour is unchanged.
- No migration, no change to driver, PlcWorker, StationIoHandler, polling, TaskStateMachine, FlowOptions, InventoryService, Scheduler or RCS client. Simulator, mock and test infrastructure are retained.

## v1.8 – CR-004 PLC configuration reorganized (2026-11-07)

- Change Request: CR-004 (IMPLEMENTED; no real PLC involved).
- Three independent functions: **Kết nối PLC** (`/plc/connections`, Admin, 4 PLC, the only place connection parameters are edited), **Giám sát PLC** (`/plc`, read-only: overview + communication log) and **Cấu hình địa chỉ IO** (`/plc/io-mapping`, Admin, 368 IO).
- The single-PLC form in *Cấu hình hệ thống → Kết nối PLC* is removed from the UI; its endpoint `/api/config/plc` and data are retained (legacy, no longer used by any screen).
- IO mapping accepts only D, W (word) and M (bit). X/Y are rejected by the API (`OUT_OF_SCOPE / NEEDS_CONFIRMATION`); the driver is unchanged.
- `GET /api/plc/points` now also returns `pollable`. Entering an address never makes a point pollable; only the existing approval step does.
- No database migration. No change to PLC driver, PlcWorker, StationIoHandler, polling, business rules or roles. Operators still have no access to connection/IO configuration (API 403).

## v1.7 – CR-003 MC Protocol reference library integrated (2026-11-07)

- Change Request: CR-003 (IMPLEMENTED in Simulator/Mock; REAL PLC NOT TESTED).
- MC 3E driver verified byte-for-byte against the proven reference library; devices extended to M/X/Y (bits) and D/W (words); X can never be written.
- Strict response validation (subheader, length cap, echoed network/PC/module I/O/station, exact data length); proven limits 960/3584/1440.
- Writes whose confirmation is missing are reported ambiguous (`WRITE_AMBIGUOUS`) and never resent. Reads keep one retry on a fresh connection.
- New per-PLC parameters: connect/read/write timeouts, X/Y base, W base (all optional, no defaults). Migration `AddMcAddressBasesAndTimeouts` (nullable columns).
- Admin-only exchange diagnostics (request/response hex, command, device, duration, end code). Operators never receive them.
- A real PLC is polled/written only at APPROVED addresses.
- No change to business rules, PER_AGV, BP-01/02/03/05/06, BP-04, A3/A4, D1/D2, F5, roles. WCS integration still off. See `02_Interface_Specification/MC_PROTOCOL_REFERENCE_ANALYSIS.md`.

## v1.6 – CR-002 PLC communication implemented (2026-11-06)

- Change Request: CR-002 (IMPLEMENTED in Simulator/Mock; REAL PLC NOT TESTED).
- 4 PLC workers (one TCP connection each), 16 Station IO handlers, 368 IO points, MC 3E binary driver, mock server, write queue, reconnect, SignalR (admin group for sensitive data), handshake framework (no rules shipped), opt-in WCS integration, Vietnamese PLC monitoring UI.
- Modes SIMULATOR (default) / REAL_READ_ONLY / REAL_READ_WRITE; no automatic switch; Safety and Transfer Permit never written for testing.
- New tables plc_connections, plc_stations, station_io_points, plc_comm_log (migration AddPlcTopology, not auto-applied). Actual addresses stay NULL.
- No change to PER_AGV, BP-01/02/03/05/06, BP-04 (REVIEW), A3/A4, D1/D2, inventory rules or roles.
- See decisions/ADR/ADR-0004 and TESTING_REPORT_CR-002.md.

## v1.5 – CR-001 final business rules (approved 2026-11-06)

- Change Request: CR-001 (APPROVED).
- Operator: edit ALL request information of waiting tasks, HARD delete waiting tasks (audit committed with the delete and kept), view and edit inventory through the existing APIs.
- SUPERSEDED: station-only edit, soft delete to Cancelled, Operator read-only inventory.
- New specification file docs/specification/access-control.md. No migration. State machine, BP flows and scheduling rules unchanged.

## v1.4 – Authentication and authorization (CR-001)

- Change Request: CR-001 (IMPLEMENTED; see the register for open decisions).
- Added cookie-based sign-in, Admin/Operator roles enforced in the backend, account management, audit trail.
- Operators may create tasks and edit/delete tasks that are still waiting (not yet accepted by an AGV).
- New tables app_users and audit_log (migration AddAuthentication, not applied automatically).
- No change to BP-01/02/03/05/06, BP-04 (still REVIEW), A3/A4, D1/D2, PER_AGV or the task state machine.

## v1.3 – Change-management framework

**Status:** Framework release; no approved business behavior change.

- Added controlled Change Request process.
- Added impact matrix and requirement traceability.
- Added Claude Code guardrails.
- Preserved the v1.2 business baseline without changing its approved decisions.

## v1.2 – Initial business baseline

- Task concurrency: `PER_AGV`.
- BP-04 / slide 8: `REVIEW`.
- Rack inventory ownership: `WCS`.
- A3/A4 and D1/D2: `UNCHANGED`.
- One PLC connection; no station clustering.
- Process flows BP-01 to BP-06 documented with unresolved source ambiguities retained.

## Change log entry template

### vX.Y.Z – <short title>

- Change Request:
- Status:
- Source:
- Added:
- Changed:
- Deprecated:
- Specification files changed:
- Software components affected:
- Tests added/updated:
- Approved by:
- Date:

## v1.6 – CR-002: Four PLCs and 16 independent Station IO sets (approved architecture, 2026-11-06)

- Replaced the earlier single-PLC/single-connection topology assumption with four physical PLCs and four independent WCS PLC connections.
- Defined four Stations per PLC and 16 independent Station IO sets using the common 23-signal template.
- Added the controlled IO workbook under `02_Interface_Specification/PLC_IO_Map/`.
- Added business-rule and integration-specification addenda; earlier text is retained and explicitly superseded only where it conflicts with CR-002.
- Actual per-Station device addresses, TCP ports, MC Protocol parameters, data encoding and handshake/reset details remain `NEEDS_CONFIGURATION` / `NEEDS_CONFIRMATION`.
- No process-flow, inventory, task-state-machine, RCS or database behavior is changed by this documentation update.
