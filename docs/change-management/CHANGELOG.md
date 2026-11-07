# Change Log

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
