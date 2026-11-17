# Change Request Register

## Status definitions

- `DRAFT`: request captured but not analyzed.
- `ANALYSIS`: impact and conflicts being assessed.
- `PENDING_APPROVAL`: proposal ready for project owner decision.
- `APPROVED`: authorized for specification/code change.
- `REJECTED`: not accepted.
- `IMPLEMENTED`: change implemented and verified.
- `CLOSED`: evidence and release documentation complete.

## Rules

- Every change to approved behavior requires a CR ID.
- A new source document is evidence/input, not automatic approval.
- Keep original source files immutable and record filename/version/date.
- One CR should represent one coherent scope.
- If a request changes multiple independent behaviors, split it into multiple CRs.
- Do not close a CR until specification, code (if applicable), tests and changelog are updated.

## Register

| CR ID | Title | Source | Status | Target version | Decision |
|---|---|---|---|---|---|
| CR-000 | Establish v1.3 change-management framework | Project owner confirmation | APPROVED | 1.3 | Framework only; no business behavior change |
| CR-001 | Authentication, Admin/Operator authorization, account management, audit trail, Operator task edit/hard-delete, Operator inventory edit | Project owner messages (2026-11-06) | **APPROVED** (2026-11-06) | Phase 1 | Final business rules confirmed by the project owner; implemented; see docs/specification/access-control.md |
| CR-002 | Replace single-PLC topology with 4 PLC connections and 16 independent Station IO sets using the common 23-signal template | Project owner confirmation and `ETEK_WCS_IO_Map_4PLC_16Stations_v2_Separate_IO_Sets.xlsx` (2026-11-06) | **IMPLEMENTED** (Simulator/Mock; real PLC untested) | Phases 1-7 | Specification addenda and controlled IO workbook updated; actual device addresses/protocol/handshake remain unconfirmed |
| CR-003 | Integrate the proven MC Protocol reference library into the WCS PLC driver (frame/response parity, devices M/X/Y/D/W, strict validation, ambiguous writes, per-PLC timeouts and address bases, diagnostics) | Project owner instruction + `reference/Kết nối MCprotocol.zip` (2026-11-07) | **IMPLEMENTED** (Simulator/Mock; real PLC untested) | CR-003 | Driver hardened; X/Y/W numbering base, QJ71E71-100 settings, addresses and handshake remain unconfirmed |

## New Change Request template

### CR-XXX – <title>

- **Status:** DRAFT
- **Requested by:**
- **Date:**
- **Source document:** filename, revision, date, page/slide/section
- **Current baseline:** vX.Y.Z
- **Problem / business reason:**
- **Requested behavior:**
- **Current behavior:**
- **Proposed behavior:**
- **Conflicts with existing rules:**
- **Affected specification files:**
- **Affected software components:**
- **Database impact:**
- **PLC / RCS / external integration impact:**
- **Security / safety / operational impact:**
- **Acceptance criteria:**
- **Regression tests:**
- **Rollback approach:**
- **Open questions:**
- **Decision:** PENDING
- **Approved by / date:**

## Example – do not treat as approved

### CR-EXAMPLE – Add buffer F6

- **Status:** EXAMPLE ONLY – NOT A REAL REQUEST
- **Requested behavior:** Add a new full-rack buffer location F6.
- **Potential impact:** process flows, inventory model, location configuration, UI and tests.
- **Decision:** Not approved. This example must not be implemented.

### CR-001 – Authentication, authorization, accounts, audit, Operator task and inventory rights

- **Status:** APPROVED
- **Approval date:** 2026-11-06 (project owner; the rules below are final decisions, no longer proposals)
- **Implementation:** implemented and verified (see WORK_LOG sections 11–13). Specification: docs/specification/access-control.md.
- **Requested by:** Project owner
- **Source:** project owner messages "Implementation Package – Authentication & Authorization", "Triển khai đăng nhập thực tế và quản lý tài khoản", "CR-001 – Final business rules & implementation" (2026-11-06; messages, no files)
- **Current baseline:** v1.3 (no authentication existed)
- **Not affected:** BP-01/02/03/05/06, BP-04 (REVIEW), A3/A4, D1/D2, PER_AGV policy, task state machine edges, PLC adapter, RCS adapter, rack model, "Không bind rack", initial stock at F3/F4.
- **Affected:** API (all endpoints need a session), UI, database (tables app_users, audit_log; migration AddAuthentication), tests.

#### Final rules (APPROVED)

1. Real sign-in with PostgreSQL accounts; roles Admin and Operator; passwords hashed; HttpOnly/SameSite cookie; forced password change for the bootstrap account. No simulated sign-in anywhere (Demo Mode keeps simulated DATA only).
2. **Operator may create tasks.**
3. **Operator may edit ALL request information of a task** while it is waiting, not accepted by an AGV and not sent/accepted by RCS. In this system the user-supplied request data are the process flow and the requesting station; AGV, rack kind, source rack and destination follow from the approved flow rules and are re-evaluated. Identity fields (id, request key, requester, RCS codes) never change.
4. **Operator may HARD-DELETE a task** under the same conditions. It is not converted into a cancel. The state is re-read from the database inside the transaction; a concurrent dispatch makes the delete fail (row version) and be refused.
5. The audit entry (task code, actor, time, task data, action DELETE, result) is committed in the same transaction as the delete and survives it.
6. **Operator may view and edit inventory through the existing APIs** (initial stock declaration, quantity adjustment); Admin has full rights. Backend-enforced, audited, business guards (capacity, allowed rack kind, reserved racks) unchanged. No new warehouse operations were invented; missing pieces are listed in access-control.md section 5.
7. Admin account rules: cannot delete the signed-in account (even if other Admins exist); cannot delete or disable the last active Admin; an account with history is disabled, never hard-deleted; checks are in the backend.
8. Password policy and session lifetime: NOT approved yet; the current technical defaults stay unchanged (min 10 chars with letters and digits, lockout 5 attempts / 5 minutes, 30 min idle / 12 h absolute).
9. Admin keeps managing tasks under the existing safe-state rules (waiting tasks: edit/hard-delete; running tasks: cancel / reconcile). Cancel and reconcile stay Admin-only (not delegated by the decision).

#### SUPERSEDED (do not apply)

- ~~"Edit task" = change only the requesting station~~ → rule 3.
- ~~"Delete task" = soft delete to Cancelled~~ → rules 4–5.
- ~~Operator is read-only for inventory; stock changes Admin-only~~ → rule 6.
- ~~Self-deletion refused only if last Admin~~ (earlier, looser reading) → rule 7 (always refused).

#### Still open (not decided by the owner)

HTTPS/TLS and Data Protection key backup (deployment), MFA, e-mail password reset, per-IP rate limiting, UI for stock editing, whether Operator should also get cancel/reconcile, password/session policy approval.

#### Tests

TST-AUTH-* (unit/API with in-memory fakes) and TST-AUTH-DB-* (PostgreSQL integration: task edit/hard delete/race, inventory, accounts, bootstrap).


### CR-002 – Four PLCs and 16 independent Station IO sets

- **Status:** APPROVED (architecture direction; actual PLC commissioning parameters remain open)
- **Approval date:** 2026-11-06
- **Requested by:** Project owner
- **Source:** Project owner confirmation in conversation (2026-11-06) and `ETEK_WCS_IO_Map_4PLC_16Stations_v2_Separate_IO_Sets.xlsx`.
- **Current baseline:** WCS v1.5; earlier baseline assumed one PLC connection.
- **Problem / business reason:** Four physical PLCs each manage four Stations. Each Station needs an independent IO set so PLC programming, testing and maintenance can be performed independently without using a shared request/response block across four doors.
- **Approved architecture:**
  1. Four physical PLCs and four WCS PLC connections.
  2. PLC-01 `192.168.10.101`: A1, A2, A3, A4.
  3. PLC-02 `192.168.10.111`: B1, B2, D1, D2.
  4. PLC-03 `192.168.10.121`: X1, X2, X3, X6.
  5. PLC-04 `192.168.10.131`: E1, E2, G3, G7.
  6. Each Station has an independent instance of the common 23-signal IO template; use reusable PLC/Station logic rather than four unrelated implementations.
  7. Actual addresses for Stations on the same PLC must be separately allocated. Address reuse across different physical PLCs is allowed.
  8. Signal identity is `PLC_ID + Station_ID + Signal_Name`.
- **Affected specification files:** `specification/business-rules.md` (section 1.8), `specification/integration-requirements.md` (section 5.10), `README.md`.
- **Controlled interface artifact:** `02_Interface_Specification/PLC_IO_Map/ETEK_WCS_IO_Map_4PLC_16Stations_v2_Separate_IO_Sets.xlsx`.
- **Not approved / still open:** actual per-Station M/D addresses, TCP ports, MC Protocol settings, QJ71E71-100 parameters, STRING encoding/length, PLC-wide versus Station-scoped heartbeat, handshake/reset ownership, timeout/retry values.
- **Business flows affected:** No change to BP-01/02/03/05/06 behavior. BP-04 remains REVIEW. A3/A4 and D1/D2 process behavior remains UNCHANGED; only their PLC connection/IO architecture is covered by this CR.
- **Database impact:** No schema change is authorized by this documentation CR. Implementation may propose a migration only after assessing the existing config model and obtaining approval if it changes persistent contracts.
- **Acceptance criteria:** Four configurable PLC connections; 16 Station IO instances; no address collision between Stations on the same PLC; identical device addresses allowed across different PLCs; no invented address or handshake behavior; PLC simulator and real-hardware verification reported separately.
- **Rollback:** Revert CR-002-specific config/code/document changes through Git; retain prior files/commit for traceability.
- **Decision:** APPROVED for architecture and documentation direction. Live PLC commissioning remains blocked on PLC engineer confirmation of the open parameters.
- **Implementation note (2026-11-06):** Full implementation approved and delivered (Phases 2-7). Migration `AddPlcTopology` (4 new tables, CREATE only) exists and is NOT applied to production. SOURCE CODE IMPLEMENTED: yes. SIMULATOR/MOCK TESTED: yes (302 backend tests). REAL PLC TESTED: no. Commissioning remains blocked on PLC engineer confirmation; see ADR-0004 and TESTING_REPORT_CR-002.md.

### CR-003 – Integrate the proven MC Protocol reference library into the WCS PLC driver

- **Status:** IMPLEMENTED for SIMULATOR/MOCK. REAL PLC TESTED = NO.
- **Approval:** project owner instruction of 2026-11-07 (full task description "CR-003"), recorded here; no business rule is changed.
- **Source:** `reference/Kết nối MCprotocol.zip` (MitsubishiMc.Client v1.1.0, used successfully on real PLCs). Full analysis: `02_Interface_Specification/MC_PROTOCOL_REFERENCE_ANALYSIS.md`.
- **Scope:** MC 3E binary driver (frame builder, response validation, devices M/X/Y bits and D/W words, nibble/endian rules, limits, ambiguous writes), per-PLC connect/read/write timeouts, per-PLC X/Y and W numbering base, Admin-only exchange diagnostics (request/response hex), polling restricted to APPROVED addresses, UI for the new parameters.
- **Architecture:** unchanged (4 PLC, 4 TCP connections, 16 Station, 368 IO, `PlcWorker`, write queue, `IPlcEndpoint`, SignalR Admin group, three modes).
- **Conflicts recorded (no silent choice):**
  1. The reference states X/Y are octal. Its source contains **no** octal conversion (documentation only), and MELSEC-Q normally writes X/Y/W in hexadecimal. The base is a per-PLC parameter with **no default**; X/Y/W addresses are refused until the PLC engineer confirms it (NEEDS_CONFIRMATION).
  2. The reference hard-codes Module I/O 0x03FF and Station 0x00 and its default port/network/PC values. They are reference values only, not WCS defaults.
- **Behaviour changes:** (a) a real PLC is read/written only at addresses that are entered **and approved** (previously "entered" was enough); (b) the response must echo module I/O and station and carry exactly the requested data length; (c) a write that may have reached the PLC without confirmation is reported ambiguous and never resent; (d) the bit limits are the proven 3584 (read) / 1440 (write).
- **Database impact:** migration `AddMcAddressBasesAndTimeouts` adds five NULLABLE columns to `plc_connections` (ConnectTimeoutMs, ReadTimeoutMs, WriteTimeoutMs, XyAddressBase, WAddressBase). Additive; Down() drops only those columns.
- **Unchanged:** PER_AGV, BP-01/02/03/05/06, BP-04 (REVIEW), A3/A4 and D1/D2 (UNCHANGED), F5, inventory ownership, Admin/Operator permissions, WCS integration (still off), no handshake rule shipped.
- **Rollback:** revert the CR-003 commit and run the migration `Down()` (or leave the nullable columns; the previous build ignores them).
- **Open:** see section 10 of the analysis document.

### CR-004 – Reorganize PLC configuration, PLC monitoring and IO mapping

- **Status:** IMPLEMENTED (UI/API organization). REAL PLC TESTED = NO (none involved).
- **Approval:** project owner instruction of 2026-11-07 (full task description "CR-004"), recorded here. No business rule or PLC technical parameter is changed.
- **Problem:** two screens edited PLC connections inconsistently (*Cấu hình hệ thống → Kết nối PLC*: one PLC, legacy `/api/config/plc`; *Giám sát PLC → Cấu hình*: 4 PLC, `/api/plc/connections`), and there was no IO-mapping screen (addresses were edited inside the station monitoring table).
- **Result:** (1) **Kết nối PLC** – 4 PLC, one edit form each, per-PLC connection test; (2) **Giám sát PLC** – read-only; (3) **Cấu hình địa chỉ IO** – 368 IO, filters (PLC, Station, status, signal search), Pollable and approval columns, address dialog, conflict check. Menu: *Giám sát PLC* under Vận hành; *Cấu hình PLC* (Kết nối PLC, Cấu hình địa chỉ IO) Admin-only.
- **Data source:** one only – `plc_connections` / `station_io_points`. The legacy `/api/config/plc` endpoint and its data are RETAINED but no screen uses them any more (candidate for a later deprecation CR).
- **Decisions / conflicts recorded (no silent choice):**
  1. *Operator "chỉ được xem cấu hình"* vs the CR-002 rule that Operators never see IPs/addresses: the stricter CR-002 rule is kept. Operators monitor only; connection/IO configuration APIs return 403. **Needs owner confirmation** if a read-only configuration view (without IP/addresses) is wanted.
  2. The simulator controls (offline/restore, value injection) and the guarded test-write were removed from the monitoring screens (read-only requirement) and moved to *Kết nối PLC* (offline/restore) and *Cấu hình địa chỉ IO* (Giả lập / Ghi thử). Their rules are unchanged.
  3. W (word) stays supported as the CR-003 driver feature (needs the W base confirmed per PLC); the owner's list "D, W, M" is applied literally.
- **X/Y – OUT_OF_SCOPE / NEEDS_CONFIRMATION:** not supported by IO mapping (API rejects them; UI does not offer them). No default octal/hexadecimal is assumed and none is taken from the reference library. The CR-003 driver/validator X/Y code is left untouched but unreachable from the UI/API for IO addresses. **If X/Y is added later it MUST:** (1) have the base confirmed with the PLC engineer per PLC (or one approved convention); (2) have an explicit Octal/Hexadecimal setting; (3) validate addresses per the chosen base; (4) convert only where the MC frame is built; (5) never convert again in UI, API or database; (6) have unit tests for X7, X10, X20 and boundaries (octal X10=8, X20=16; hex X10=16, X20=32); (7) have frame-level tests before any real PLC communication.
- **Database impact:** none (no migration). **API impact:** `GET/PUT /api/plc/points…` returns `pollable`; `PUT …/address` rejects X/Y; all other APIs unchanged.
- **Unchanged:** PlcWorker, StationIoHandler, polling, MC driver, TaskStateMachine, FlowOptions, InventoryService, Scheduler, RCS client, PER_AGV, BP-04 (REVIEW), A3/A4, D1/D2, F5, roles.
- **Rollback:** revert the CR-004 commit (no data change to undo).

#### CR-004 acceptance addendum (2026-11-07)
- **Decisions confirmed by the project owner:** (1) Operator keeps the CR-002 rule: monitoring only, no IP/port/address/configuration, configuration APIs 403. (2) `/api/config/plc` is deprecated: kept, unused, no further development; removal by a separate CR. (3) Device W stays architecturally supported; address base NEEDS_CONFIRMATION; no polling, read or write of W until the PLC engineer confirms.
- **Production UI cleanup (in scope of this acceptance):** engineering tools hidden from operating UI outside Development; device/link status wording made truthful; Production API guard for simulator controls and test-write. Recorded history (audit trail action names, PLC communication log text, transaction "source" label) keeps its original wording because it documents what actually happened.
- **Not changed:** business rules, driver, polling, DB schema/data, LoginPage.tsx, Demo build (`npm run build:demo`, a separate non-production build whose own banner says it is local data).
- **Rollback:** revert the follow-up commit; no data to undo.

### CR-006 – Dockerization and Windows 11 deployment

- **Status:** IMPLEMENTED (source). **Not executed on Docker yet** – acceptance checklist: `docs/DEPLOYMENT_TEST_CHECKLIST.md`. REAL PLC TESTED = NO.
- **Approval:** project owner, 2026-11-07 (full scope; decisions A1 cookie flag, B /healthz, C Production simulator guard).
- **Scope:** run ETEK WCS on one Windows 11 machine (Docker Desktop + WSL2) from the GitHub `main` branch with a controlled deploy/backup/restore tooling.
- **Architecture:** `frontend` (Nginx, the only published port) → `backend` (not published) → `postgres` (internal network only); one-shot `migrator` (EF migration bundle, forward only) runs before the backend; volumes `etek_wcs_pgdata` and `etek_wcs_keys` (Data Protection key ring). Single backend instance (scheduler and PLC workers live in the process). No MQTT (the source does not use it).
- **Decisions:** (A1) `Auth:CookieSecure` = Always | SameAsRequest, default unchanged; SameAsRequest only for deliberate plain-HTTP trusted LAN. (B) anonymous `/healthz`. (C) Production refuses `Plc:Mode=Simulator` unless `Plc:AllowSimulatorInProduction=true`; the compose default is `PLC_MODE=RealReadOnly` with `PLC_ALLOW_LIVE_CONNECTION=false` (no simulator, no socket to a physical PLC). RCS remains simulator-only and this is shown in the UI and documented.
- **Impact:** API (+`GET /healthz`; start-up validation), configuration (+`Auth:CookieSecure`, `Plc:AllowSimulatorInProduction`, `Deployment:TrustForwardedHeaders`), UI (notice), DB none, PLC driver none.
- **Open items / assumptions:** Docker build and runtime, persistence, backup/restore and SignalR through Nginx are unverified until the checklist is run. PLC TCP port and MC parameters remain NEEDS_CONFIRMATION; the PLC sees the Windows host IP (NAT), the proposed host IP 192.168.10.50 is unconfirmed. HTTPS termination is not included (use SameAsRequest on a trusted LAN or add a TLS terminator in a later CR). The key ring is stored unencrypted on its volume. Failed-login attempts are logged at error level by the existing code (not changed).
- **Rollback:** previous commit + `restore.ps1` of the automatic `pre-<commit>` backup (README section 6); source rollback = revert the CR-006 commits.

#### CR-006 completion addendum (2026-11-07)
- **Owner decisions:** (1) `Deployment:TrustForwardedHeaders` kept, but trusted only from the designated Nginx address: new `Deployment:TrustedProxyNetworks` (required, 0.0.0.0/0 and ::/0 refused), compose pins Nginx to a fixed address on `edge_net` and trusts only that /32; Nginx overwrites X-Forwarded-For. (2) Key ring stays on the persistent volume; warning, protection requirements and backup guidance documented; encryption mechanism unchanged pending an impact assessment. (3) HTTP + SameAsRequest only for a trusted test LAN; HTTPS + Always recommended for operation (documented; backend warns in Production when SameAsRequest is used).
- **Verification:** backend 410/410; forwarded-header trust verified outside containers (header from an untrusted sender ignored, honoured from the trusted one, start-up refused without a proxy list). Docker runtime remains unverified (checklist G1-G4 added).
- **Unchanged:** business logic, MC driver, RCS client, database schema.

### CR-007 – Centralized logging and diagnostics

- **Status:** IMPLEMENTED on `develop` (not merged to `main`). Docker runtime, real PLC and real RCS: NOT VERIFIED. REAL PLC TESTED = NO.
- **Approval:** project owner, 2026-11-16 (analysis `report/CR-007_Logging_Diagnostics_Gap_Analysis.md`, then "EXECUTE CR-007").
- **Scope:** Admin screen *Logging & Diagnostics*; backend logging core; storage and query API; PLC events in the application log; deployment script logging with import into the screen.
- **Design decisions:** (1) reuse Serilog, `audit_log` and `plc_comm_log`; one new table `system_log` only for events that had no store (SYSTEM, DATABASE, DEPLOYMENT, RCS_AGV); a read-only view unifies the three sources without copying data. (2) PLC events are enriched and mirrored to stdout in `PlcEventDispatcher` (Option A): timeout / end-code classification from the existing messages, **no change to `PlcWorker` or the MC driver** (Option B was not used). (3) Throttling is a Serilog pipeline component, so `HostedServices.cs` and the Scheduler wrapper were **not** modified. (4) Deployment events reach the screen through a read-only file mount and an idempotent importer, not through an HTTP ingestion endpoint. (5) The log API is Admin-only and read-only.
- **Database impact:** migration `AddSystemLog` (additive: new table, one nullable column, one view); Down verified (drops only CR-007 objects; history rows kept). **API impact:** +3 GET endpoints. **UI impact:** +1 Admin screen and menu entry. **Configuration:** `Logging:ThrottleSeconds`, `Logging:SystemLog:Enabled/RetentionDays`, `Logging:File:Path` (optional), `DeploymentLogs:Directory`, `WCS_LOG_DIR`.
- **Defects found and fixed during implementation (by browser/runtime tests):** Serilog local-offset timestamps rejected by PostgreSQL; one un-storable entry could block later entries (now isolated); a helper property leaked into the stored JSON.
- **Limits / not done:** SignalR connect/disconnect server logging (LOW, optional); real RCS events (no adapter exists); `audit_log` retention (out of scope); Docker bind-mount import not run on Docker; the unified view sorts in PostgreSQL (≈0.2 s for a page at 450 000 rows; revisit above ~1 million rows).
- **Rollback:** revert the CR-007 commits; `dotnet ef database update 20261107023009_AddMcAddressBasesAndTimeouts` removes the CR-007 objects without touching other data.

### CR-008A – WCS dispatch and scheduler reliability

- **Status:** IMPLEMENTED on `develop` (not merged to `main`). Docker runtime, real PLC and real RCS: NOT VERIFIED. REAL PLC TESTED = NO.
- **Approval:** project owner, 2026-11-16 ("EXECUTE CR-008A"), based on `report/ETEK_WCS_FULL_CODE_REVIEW_2026-11-16.md` (AUDIT-001, 002, 003, 004, 006).
- **Scope:** (1) Production never dispatches a task to the RCS simulator (task refused with `RCS_NOT_CONNECTED`; AGV status not shown from the simulator). (2) Station intake and scheduler pass run in independent exception scopes; one failing intake request does not stop the others. (3) Dispatch retry is spaced by a backoff and bounded (idempotency and `RcsRequestCode` unchanged; query before any re-send). (4) Continue/cancel follow-up is spaced, bounded by `Scheduler:RcsMaxAttempts` and writes identical outcomes to the task history once. (5) Cancel reconciliation reacts explicitly to every RCS status (Cancelled, NotFound, Created, Executing, Cancelling, Completed, Interrupted, Unknown).
- **Decisions taken inside the approved scope (technical, not business rules):** NotFound is accepted as a cancellation only when RCS never reported a task code AND two consecutive checks agree; NotFound after RCS accepted the task, Completed and Interrupted end the task as Failed WITH reconciliation required (reservations and rack state kept, AGV freed). When the automatic retry limit is reached the task is flagged for reconciliation and left for an operator (no state is changed by a timer). See ADR-0007.
- **NOT changed (protected / not approved):** TaskStateMachine edges, InventoryService rules, PER_AGV, FlowOptions, task priority, PLC handshake / polling / timeout / retry, MC protocol, AGV Offline policy, `StateTimeoutsSeconds` (still empty), RCS interface contract (only an additive guard interface).
- **Database impact:** migration `AddRcsRetryTracking` (additive: `transport_tasks.RcsRetryCount` int default 0, `RcsLastAttemptAt` nullable timestamptz). Down drops only these two columns.
- **API impact:** task creation can answer 422 `RCS_NOT_CONNECTED` in Production; `/api/system/health` gains `integration.rcs`. **UI impact:** none required (existing banner and "RCS: chưa kết nối" indicator already cover Simulator mode). **Configuration:** `Scheduler:RcsRetryInitialDelayMs` (2000), `RcsRetryMaxDelayMs` (60000), `RcsMaxAttempts` (30), existing `MaxDispatchAttempts` (5); `Rcs:AllowSimulatorInProduction` (default false; compose `RCS_ALLOW_SIMULATOR_IN_PRODUCTION`).
- **Behaviour changes to be aware of:** (a) in Production without an RCS adapter no task can be created (previously it was accepted by the simulator and hung); (b) RCS outages are now retried with spacing (2 s, 4 s, ... 60 s) instead of every tick, so the dispatch budget of 5 attempts now lasts about **62 s** instead of about 5 s (attempts at 0, 2, 6, 14 and 30 s, then a further 32 s wait before the dispatch is declared uncertain). Continue/cancel follow-up reaches its limit of 30 attempts after about **25 minutes** (delays 2, 4, 8, 16, 32 then 60 s). *Correction (CR-008B-1, 2026-11-17): this line originally said "about 30 s"; the 30 s figure ignored the final backoff wait.*
- **Corrections recorded by CR-008B-1, 2026-11-17 (history kept):** (1) retry timings as above; (2) the statement that an unacknowledged station request "is presented again by the PLC" was WRONG: `PlcStationSignalService` detects the rising edge of a bit only, `AcknowledgeRequestAsync` only writes a log line, so a request that is not acknowledged is not re-presented - it can be lost whether it is acknowledged or not (fixed by CR-008B-2); (3) the rule "two consecutive NotFound answers = Cancelled" was never approved by the project owner (decision D-9) and conflicts with `task-state-machine.md` 3.9; it is removed by CR-008B-3.
- **Rollback:** revert the CR-008A commit; `dotnet ef database update 20261116105823_AddSystemLog` drops the two columns.

### CR-008B – Reliability, PLC intake and physical reconciliation

- **Status:** IN PROGRESS on `develop` (small CRs, one commit each). Not merged to `main`. Docker, real PLC, real RCS: NOT VERIFIED. REAL PLC TESTED = NO.
- **Approval:** project owner, 2026-11-17 ("CR-008B - Reliability, PLC Intake & Physical Reconciliation"), based on `docs/reviews/CR008A_POST_IMPLEMENTATION_REVIEW.md` and `docs/reviews/CR008B_DESIGN_IMPACT_ASSESSMENT.md`.
- **Decisions already taken by the owner:** **D-1** - when CancelPending reaches the limit of 30 automatic attempts: stop the automatic retry, move the task to `Failed`, set `RequiresReconciliation=true`, record the reason, keep the reservations and resource state, never release a rack automatically, never treat `Failed` as proof that RCS cancelled; the Admin must reconcile before the resources are used again. The retry limit is not raised or reset automatically.
- **Decisions NOT taken (must not be assumed):** **D-7** (conditions for handing an AGV back to the scheduler) - no new AGV policy, flag or migration is introduced; **D-9** - two NotFound answers are NOT a cancellation; **REV-005** - RCS idempotency by `RcsRequestCode` is NOT VERIFIED (RCS vendor confirmation required before CreateTask is retried against a real RCS).
- **Migrations so far:** B-2 `AddStationRequests` (new table `station_requests`, additive).
- **B-3:** no migration; new Admin endpoints under `/api/tasks/.../reconciliation*`.
- **Sub-CRs:** B-1 documentation correction; B-2 PLC station intake reliability; B-3 CancelPending reconciliation (+ Admin reconciliation screen); B-4 rack/inventory uncertainty; B-5 AGV reconciliation hold - ANALYSIS ONLY until D-7 is decided. Each has its own report in `docs/reviews/`.
