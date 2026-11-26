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
| CR-031 | Real RCS fleet monitoring (queryAgvStatus every 5 s), no simulated AMR data in operation (business AGV cards hidden, no simulator status, Production refuses a simulated fleet, per-poll logs) | Project owner instruction (2026-11-21) | **IMPLEMENTED – MOCK VERIFIED; real acceptance NOT done (RCS unreachable from the dev machine)** (branch `feature/cr-031-real-fleet-monitoring`; not merged; no migration) | after real acceptance | `CR-031_REAL_FLEET_MONITORING.md` |
| CR-032 | RCS Diagnostics & Manual API Tester: Admin-only tool to call the 23 outbound RCS-2000 V3.3 APIs by hand (catalog, builder, inspector, history, callback monitor), immediate execute, masked, audited | Project owner instruction (2026-11-23) | **IMPLEMENTED – MOCK VERIFIED; real RCS NOT observed; not merged** (`feature/cr-032-rcs-diagnostics`, `CR-032_RCS_DIAGNOSTICS.md`) |
| CR-035 | AGV Phase 2 first runnable increment: Operational Readiness gate (D-11/D-12), real-RCS leg template, Admin manual PLC simulation (PLC only, real RCS) | Project owner instruction (2026-11-24) | **IN PROGRESS – PARTIAL** (Queue Request/Held/Scope and Pick/Drop triggers NOT implemented; real RCS/AMR end-to-end not verified) – `CR-035_PH2_FIRST_RUNNABLE.md` |
| CR-039 | Default transport process catalog: published-but-not-ready defaults for A↔B (M5), X1/X2↔Y1/Y2 (M6) and X3/X4↔Z1/Z2, locations catalog v2, request forms list every published process | Project owner instruction and decisions (2026-11-25) | **IMPLEMENTED – TESTED (Simulator) – NOT DEPLOYED** – `CR-039_DEFAULT_TRANSPORT_PROCESS_CATALOG.md` |
| CR-040 | Transport queue (D-12/D-13, Held), linked operation pages, Admin PLC signal simulation, guarded rack records | Project owner instruction (2026-11-25) | **IMPLEMENTED – TESTED (Simulator) – NOT DEPLOYED**; OQ-45/49/54/55/58 and OQ-40-01 deferred – `CR-040_QUEUE_LINKED_OPERATIONS.md` |
| CR-041 | One request list/create path, catalog apply for existing processes, shared PLC input handler, Real-mode protection of the simulation, rack type and lock reason | Project owner instruction (2026-11-26) | **IMPLEMENTED – TESTED (Simulator) – NOT DEPLOYED**; OQ-40-01/39-08/41-01/41-02 deferred – `CR-041_UNIFIED_OPERATIONS.md` |
| CR-042 | Approved decisions after the initial review: header lock, catalog publication guard, withdrawn versions, stalled queue warning, stock adjustment protection, one engine per station, repeated dispatch event, rack dialog, rack ownership of transport orders (OQ-42-08) | Project owner (2026-11-26) | **MERGED (2026-11-26)** – TESTED (Simulator) – NOT DEPLOYED; OQ-54 on hold; OQ-41-02/55/42-05/42-06 BLOCKED; OQ-42-08 FIXED (awaiting review); open findings F1 (PER_AGV across engines), F2 (queue deadlock) – `CR-042_APPROVED_DECISIONS.md` |
| CR-044 | PLC write guard (deny by default, declared points, reserved registers, audit) and correction of the `PLC_MissionCompleted` success value | Project owner (2026-11-26, OW-2, OW-4) | **MERGED (2026-11-26)** – TESTED (mock / simulator) – NOT DEPLOYED – `CR-044_PLC_WRITE_GUARD_AND_MISSION_COMPLETED.md` |
| CR-036 | Product Model isolation and Manual QC protection (Batch 1): product models, location role, rack model and disposition, publication and run-time guards, one-way manual handling | Project owner instruction (2026-11-25) | **IMPLEMENTED (BATCH 1) – TESTED – NOT DEPLOYED** – flag `ModelIsolation:Mode` default Off – `CR-036_MODEL_ISOLATION_MANUAL_QC_BATCH1.md` |
| CR-030 | Real RCS dispatch adapter (HIKROBOT RCS-2000 API V3.3: genAgvSchedulingTask, queryTaskStatus, continueTask, cancelTask, agvCallback); mapping as configuration; fail closed | Project owner approval (2026-11-21) | **IMPLEMENTED – MOCK VERIFIED – PENDING REAL RCS COMMISSIONING** (branch `feature/cr-030-real-rcs-dispatch`; not merged; Real Dispatch OFF by default; no migration) | after commissioning | `CR-030_REAL_RCS_DISPATCH.md`; ADR-0012; vendor questions 16-21 |
| CR-029 | PLC interface & handshake specification update: new interface `AGV_PLC Interface_List_20_11_2026.xlsx` (6 signals/Station, `HeartBit`, `Spair`, 6-step handshake, no addresses) supersedes the 10/11 interface basis | Project owner instruction (2026-11-21) + `AGV_PLC Interface_List_20_11_2026.xlsx` | **PENDING_APPROVAL (documentation merged into main 2026-11-21)** – awaiting PLC confirmation (not Approved, not Ready for Implementation); specification documents written; owner architectural decisions D-1…D-4 recorded 2026-11-21 in ADR-0011; **no code** (branch `feature/cr-029-plc-interface-20-11-2026`); open: PQ-01…PQ-29, Station naming (PQ-20) | after approval | `docs/change-management/CR-029_PLC_INTERFACE_20_11_2026.md`; documents in `docs/02_Interface_Specification/PLC_20_11_2026/` |
| CR-010 | PLC communication, per-PLC IO mapping and the 9-step PLC-WCS exchange process (replaces the per-Station IO model of CR-002 in part) | `AGV_PLC_IO_Interface_List_10_11_2026.xlsx` (sheets `IO Matrix`, `Trang_tính1`) and project owner instruction (2026-11-17) | **IMPLEMENTED – PARTIAL** (scope approved by the project owner instruction of 2026-11-18; PLC live signalling and a real RCS leg adapter are NOT implemented) | 2026-11-18 | Route Engine, multi-leg Transport Task, per-port Station Transaction, configurable Business Process, PLC provisioning, API, UI, migration `AddRoutingEngine`, 50 tests. See ADR-0008 and `reviews/CR010_ROUTE_ENGINE_IMPLEMENTATION_REPORT.md` |
| CR-011 | Bulk READ approval of the IO points of the PLC under commissioning (default PLC-04) | Project owner instruction (2026-11-18) | **IMPLEMENTED** (code only; not yet run on the deployment database) | 2026-11-18 | Admin API + dialog on the IO mapping page; uses the existing approval; no migration |
| CR-012 | A valid IO address makes the point pollable without approval (supersedes the CR-003 rule "entered AND approved" for reading) | Project owner instruction (2026-11-18) | **IMPLEMENTED** (code only; not deployed) | 2026-11-18 | `IsPollable`, `PlcPollablePolicy`, runtime and IO list; bulk approval deprecated; no migration |
| CR-013 | PLC Diagnostic Logging (P0) and Diagnostic Export (P1) | Project owner approval (2026-11-18, "P0 + P1") after the diagnostics audit | **IMPLEMENTED** (not deployed; not verified against a real PLC) | 2026-11-18 | Standard codes/stages, MC exchange history (300 per PLC), Admin ZIP export, System > Diagnostics page; migration `AddPlcDiagnostics` |
| CR-014 | PLC write approval per IO point (separate from reading) and data-type specific, validated write input | Project owner instruction (2026-11-18) | **IMPLEMENTED** (code only; not deployed; no real PLC written) | 2026-11-18 | `WriteApproval` state, `write-approval` API, strict value validation, IO mapping UI; migration `AddPlcWriteApproval` |
| CR-015 | Enable PLC write: remove the environment-based blocks, RealReadWrite as the supported mode, write keys in the deployment | Project owner instruction (2026-11-18) | **IMPLEMENTED** (code, configuration; not deployed; no real PLC written) | 2026-11-18 | Production no longer refuses the write; write dialog in every build; compose write keys; write exchanges always recorded |
| CR-016 | PLC write UI: always show the write action with the concrete reason when it cannot be used; runtime write readiness | Project owner instruction (2026-11-18) | **IMPLEMENTED** (code; not deployed; no real PLC written) | 2026-11-18 | "Ghi PLC" button always shown for writable IO, readiness banner, runtime info with missing key names |
| CR-017 | Why the Deploy runtime stays read-only: configuration chain, .env.example, diagnostics | Project owner instruction (2026-11-18) | **IMPLEMENTED** (code, configuration, tools; not deployed; no real PLC written) | 2026-11-18 | `.env.example` fixed, `plc-write-config.ps1`, `MissingWriteKeys`, key checklist in the banner, binding tests |
| CR-019 | Simplify PLC write approval: remove PLC_WRITE_APPROVAL_REFERENCE and PLC_WRITE_HANDSHAKE_APPROVED | Project owner instruction (2026-11-18) | **IMPLEMENTED** (not deployed; no real PLC written) | 2026-11-18 | Single approval = Admin per-point write approval; runtime keys reduced to PLC_MODE, PLC_ALLOW_LIVE_CONNECTION, PLC_WRITE_ENABLED |
| CR-020 | Unified PLC monitoring and I/O operations: Read/Write moved to PLC Monitoring; IO Address Mapping is configuration only | Project owner instruction (2026-11-18) | **IMPLEMENTED** (verified against the MC mock only; not deployed; no real PLC touched) | 2026-11-18 | New "I/O monitoring" tab, `monitor/points`, `signals/{s}/read`, read-back after write |
| CR-021 | System-wide diagnostic logging and export (batches A-D) | Project owner instruction (2026-11-18; decisions D1-D8 approved) | **IMPLEMENTED** (batches A-D; verified on the RCS simulator and the MC mock only; not deployed) | 2026-11-18 | Survey: `docs/reviews/CR021_PHASE1_LOGGING_SURVEY_AND_PROPOSAL.md` |
| CR-022 | Diagnostics screen: one export area (whole system); separate PLC export removed from the UI; SignalR upgrade no longer counted as a slow request | Project owner instruction (2026-11-19) | **IMPLEMENTED** (not deployed; DEV database migrated) | 2026-11-19 | PLC export endpoint and the PLC/ folder of the package unchanged |
| CR-028 | Dynamic fleet dispatch – RCS-managed dispatch (RCS chooses the AMR; `AgvId` nullable; PER_AGV for Legacy only) | Project owner instruction (2026-11-21) | **MERGED into main (2026-11-21), simulator only** (not yet deployed; no real RCS create-task adapter exists; RcsManaged must not be used in operation) | 2026-11-21 | One migration (`transport_tasks`), config `Rcs:Dispatch:*`; real adapter / callback blocked: see `CR-028_DYNAMIC_FLEET_DISPATCH.md` section 7 |
| CR-027 | Dynamic AMR discovery and monitoring from the RCS – Phase 1 (read model, queryAgvStatus client, AGV Monitoring; Phase 2 task assignment deferred) | Project owner instruction (2026-11-19) | **Phase 1 IMPLEMENTED and MERGED into main (2026-11-21)** (not yet deployed; real RCS not tested; Phase 2 not approved) | 2026-11-19 | Additive: 2 tables, 1 endpoint, polling loop extended; no change to Task, AgvId, PER_AGV, statistics. See `CR-027_DYNAMIC_AMR_MONITORING.md` |
| CR-026 | Configuration Export / Import (Phase 2 MVP: Location, AGV, PLC Station, Business Process) | Project owner instruction (2026-11-19, decisions D1–D7) | **IMPLEMENTED on feature/cr-026-config-transfer** (Phase 2; owner approved CR and OQ-1..5; awaiting owner confirmation, NOT merged, NOT deployed) | 2026-11-19 | Additive: new Admin API/UI, 2 new tables, maintenance flag (OQ-1). See `CR-026_CONFIG_TRANSFER.md` |
| CR-025 | Modern Industrial / Logistics Control Dashboard visual upgrade (presentation only) | Project owner instruction (2026-11-19) | **IMPLEMENTED** (frontend only; verified in a real browser; not deployed) | 2026-11-19 | No API, database, PLC, RCS, SignalR or permission change |
| CR-024 | Bilingual (VI/EN) Description column of IO Address Mapping | Project owner instruction (2026-11-19) | **IMPLEMENTED** (frontend i18n only; verified in a real browser; not deployed) | 2026-11-19 | No API, database, catalog, PLC or permission change |
| CR-023 | Compact layout of PLC connections and PLC monitoring screens; permanent explanation texts removed | Project owner instruction (2026-11-19) | **IMPLEMENTED** (UI only; verified in a real browser; not deployed) | 2026-11-19 | No API, database, PLC or permission change |

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
- **Decision:** APPROVED for implementation within the scope of the project owner instruction of 2026-11-18 (business principles of sections 2-3 of that instruction); the items that instruction leaves open stay OPEN (see the implementation addendum below).
- **Approved by / date:** Project owner / 2026-11-18 (implementation instruction). The formal sign-off field is left to the owner.

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
- **B-4:** no migration; `InventoryService.MarkPhysicalUnprovenAsync` added (protected module, approved by CR-008B section 7).
- **Sub-CRs:** B-1 documentation correction; B-2 PLC station intake reliability; B-3 CancelPending reconciliation (+ Admin reconciliation screen); B-4 rack/inventory uncertainty; B-5 AGV reconciliation hold - ANALYSIS ONLY until D-7 is decided. Each has its own report in `docs/reviews/`.

### CR-010 – PLC communication, per-PLC IO mapping and the 9-step PLC–WCS exchange process

- **Status:** PENDING_APPROVAL (analysis and draft specification complete; **no implementation, no baseline change**)
- **Requested by:** Project owner
- **Date:** 2026-11-17
- **Source documents:** `docs/02_Interface_Specification/PLC_IO_Map/AGV_PLC_IO_Interface_List_10_11_2026.xlsx` (sheet `IO Matrix` - 22 signals + unnamed M9123; sheet `Trang_tính1` - 9-step exchange). Project owner instruction of 2026-11-17 (signal semantics of M9104/M9122/M9123, Request_ID length 254, per-PLC mapping, per-PLC FIFO queue and single transaction, disconnect and restart recovery, mandatory reading of D1500/D1510/D1512/D1514/D1520).
- **Current baseline:** WCS v2.7 (CR-002 per-Station IO sets, ADR-0004, workbook `ETEK_WCS_IO_Map_4PLC_16Stations_v2_Separate_IO_Sets.xlsx` kept in Git history).
- **Problem / business reason:** the controlled workbook models 16 independent Station IO sets with a 23-signal template whose signal semantics differ from the new AGV-PLC interface list; the new interface needs a PLC-level mapping, a 9-step handshake, data-register based location resolution, a per-PLC queue/transaction and recoverable state. None of these exists in WCS today (handshake framework is observational with no approved rule; `Request_Ack` and `Heartbeat_SW` are never written; the PLC registers for rack position are not read).
- **Requested behavior:** see the draft specification `PLC_COMMUNICATION_SPECIFICATION_DRAFT.md` (sections 1-17).
- **Current behavior:** see `PLC_REVIEW_CURRENT_STATE_AND_GAP_ANALYSIS.md` (section 4 and the 21-row gap table).
- **Proposed behavior:** the draft specification. Signal names and addresses unchanged from the source; the 9 steps unchanged in order and meaning; everything the sources do not state is recorded as TBD, never filled.
- **Conflicts with existing rules (recorded, not silently resolved):**
  1. CR-002 / ADR-0004 / `integration-requirements.md` 5.10: *16 independent Station IO sets, identity `PLC_ID + Station_ID + Signal_Name`* versus the new *per-PLC mapping with the Station taken from register values*. The new request supersedes this in part - needs explicit approval.
  2. CR-002: *`Actual_PLC_Address` stays empty until the PLC engineer confirms it; no addresses are assigned* versus the new *Default IO Mapping Template carries the addresses of the Excel and is copied to every new PLC* (open question Q-IO-2).
  3. The new list changes the meaning of signals that exist in the controlled workbook (M9003, M9004, M9005, M9103, M9104 inverted, M9105->M9120, M9106->M9121, M9107->M9122) and drops `Station_Fault` (M9009) and `Transfer_Complete` (M9010). Code and UI texts currently carry the old meaning.
  4. Protected rule *WCS owns rack inventory and location state* versus *PLC registers state which position holds a rack* (Q-INV-1).
  5. Protected rules A3/A4 and D1/D2 `UNCHANGED` and BP-04 `REVIEW`: PLC values 3-4 (A3, A4) and a Delivery request at B1/B2 reach exactly those flows; they must stay monitor-only/REVIEW.
  6. `Request_ID` STRING length 254 overlaps the next data registers (Q-STR-1).
  7. Working-tree state: the controlled workbook v2 is shown as deleted and the new Excel is untracked (not caused by this analysis). Not resolved here; see section 2 of the gap analysis.
- **Affected specification files (to be updated ONLY after approval):** `specification/integration-requirements.md` (5.2, 5.3, 5.9, 5.10), `specification/business-rules.md` (1.8), `README.md`, `decisions/ADR/ADR-0004-plc-communication-architecture.md`, the IO workbook (new version; v2 preserved), `change-management/CHANGELOG.md`, `IMPACT_MATRIX.md`, `REQUIREMENT_TRACEABILITY.md`. `task-state-machine.md` only if Q-MAP-1 / Q-TSK-1 require it.
- **Affected software components (impact assessment only, nothing changed):** PLC runtime / worker / handshake coordinator, PLC station signal service and station-request intake, IO catalog and seeder, PLC configuration service and API, write policy, simulator, PLC UI (connections, IO mapping, monitoring), new UI for PLC lifecycle, template, location mapping and transactions. Not touched: TaskStateMachine, FlowOptions, InventoryService, Scheduler, RCS client.
- **Database impact:** not authorized by this CR in its present state. Implementation would need additive migrations (PLC lifecycle, per-PLC mapping and template, location mapping, request/transaction/queue/write-intent log) and a decision on the existing per-Station data (Q-MIG-1). Existing PLC connection parameters must be preserved.
- **PLC / RCS / external integration impact:** PLC: new signal set, new handshake behaviour, heartbeat; all writes remain gated by mode, allow-list and engineer approval, starting with read-only commissioning. RCS: no protocol change.
- **Security / safety / operational impact:** safety/interlock classification of M9000, M9104, M9120 must be re-done by the PLC engineer (Q-SAFE-1/2); no automatic cancel, reset or re-issue of physical commands during recovery; Admin-only configuration and reconciliation; audit of configuration changes.
- **Acceptance criteria (for the later implementation CR):** an approved specification; PLC count configurable without code change; independent per-PLC mapping verified (editing one PLC changes neither another PLC nor the template); the 9 steps reproduced in the simulator in the documented order; duplicate Request_ID never creates a second task; per-PLC FIFO and one active transaction per PLC with PLCs running concurrently; disconnect/reconnect and WCS restart resume at the correct step without repeating a completed step or a physical command; inconsistent state ends in PendingReconciliation; simulator/mock and real-hardware results reported separately.
- **Regression tests:** existing PLC suites (CR-002, CR-003, CR-004, CR-008B-2), station intake, PER_AGV, inventory and task state machine tests must remain green; new tests per the acceptance criteria.
- **Rollback approach:** this step adds documents only; revert the commit. A later implementation must be additive and reversible.
- **Open questions:** `PLC_SPEC_OPEN_QUESTIONS.md` (about 38 numbered items; the blocking ones are Q-ORD-1, Q-LEG-1, Q-MAP-1, Q-INV-1, Q-STR-1, Q-IO-2, Q-PLC-1 and, from the 2026-11-18 consolidation, Q-GATE-1, Q-MLEG-1, Q-MLEG-2, Q-AGV-1).
- **Deliverables of this step:** `PLC_REVIEW_CURRENT_STATE_AND_GAP_ANALYSIS.md`, `PLC_COMMUNICATION_SPECIFICATION_DRAFT.md`, `PLC_SPEC_OPEN_QUESTIONS.md` (all under `docs/02_Interface_Specification/`). Added 2026-11-18: `PLC_BUSINESS_PROCESS_AND_TRANSACTION_MODEL_DRAFT.md`.
- **Decision:** PENDING
- **Approved by / date:**

#### CR-010 addendum – 2026-11-18 consolidation of business process and PLC communication (specification only)

- **Scope added:** two-level model (Station Transaction per gate; Transport Task with Legs), multi-leg flows through buffers, 10 specification-level acceptance scenarios. Document: `docs/02_Interface_Specification/PLC_BUSINESS_PROCESS_AND_TRANSACTION_MODEL_DRAFT.md`.
- **Sources read (text only):** `7. FFC AMR/Process in detail.pptx` (11 slides) and `7. FFC AMR/ETEK Process AGV Phase 2 12.08.26.pptx` (10 slides; the file on disk has no "(1)" in its name). Both sit in the parent folder of the project. Diagrams/arrows were not interpretable from text. Slides 5-10 of the first file match BP-01..BP-06. The Phase 2 file uses other location names (P1-P4, M5, M6) and is not part of the approved baseline (Q-PH2-1, Q-LOC-1).
- **Findings from the slides:** (a) "only 1 task at a time" is on slide 4 and is superseded by BR-01 PER_AGV (Q-SLD-1); (b) in approved flows a drop at buffer F ends the task - an automatic buffer-to-gate leg exists only in BP-04 (REVIEW) and is therefore not approvable (Q-MLEG-2); (c) PLC door-open handshake is present at A1/A2 and B1/B2 for both pick and drop, absent at F buffers.
- **New conflicts recorded:** (C-1) a "gate with its own I/O set" versus the workbook's single signal set per PLC and the earlier "one transaction per PLC" decision (Q-GATE-1, blocking); (C-2) multi-leg flows versus `task-state-machine.md` 3.6, which says a multi-leg task mechanism must be approved first (Q-MLEG-1, blocking); (C-3) AGV occupancy between legs versus PER_AGV/`WAITING_FOR_DESTINATION` (Q-AGV-1, blocking); gate release at step 8 or 9 (Q-REL-1).
- **Unchanged and still OPEN:** M9101 ACK timing (Q-ORD-1), M9120 timing (Q-ORD-2), handshake↔pick/drop mapping (Q-MAP-1), destination-gate handshake (Q-LEG-1), PLC protocol items.
- **Protected rules:** BP-01/02/03/05/06 not changed; BP-04 REVIEW; A3/A4 and D1/D2 UNCHANGED; PER_AGV not changed; `TaskStateMachine` and `InventoryService` not changed; WCS owns inventory.
- **Approved baseline files NOT edited** (CLAUDE.md section 3, steps 5-6). To be updated only after approval: business-rules.md (BR-01 clarification of Task/Leg/Station Transaction), process-flows.md (Leg concept and any approved buffer legs), integration-requirements.md (5.2/5.3/5.10), task-state-machine.md (only if Q-MLEG-1 requires it), ADR-0004 (supersede) plus a new ADR for the two-level model, CHANGELOG.
- **Proposed Impact Matrix rows (not applied):** "Multi-leg / buffer flows" – Spec: affected; Backend: affected (orchestration layer, not TaskStateMachine); Database: additive; PLC: handshake per gate; UI: transaction/leg views; Tests: AT-01..AT-10. "Gate independence" – Spec/PLC/Backend affected, depends on Q-GATE-1.
- **Proposed Traceability rows (not applied):** CR010-REQ-01 two-level model; CR010-REQ-02 gate independence; CR010-REQ-03 multi-leg and buffer inventory; CR010-REQ-04 recovery per leg; each traced to the model document and tests AT-01..AT-10 (status: PROPOSED, no implementation).
- **Not approvable yet:** items that depend on Q-GATE-1, Q-MLEG-1, Q-MLEG-2, Q-AGV-1 and the earlier blocking questions.

#### CR-010 implementation addendum – 2026-11-18

- **Status:** IMPLEMENTED – PARTIAL. Authority: ADR-0008, `reviews/CR010_ROUTE_ENGINE_IMPLEMENTATION_REPORT.md`.
- **Owner decisions applied (formerly open):**
  - Q-GATE-1: independence is per **port** (a PLC Station of CR-002, one IO set each); several ports of one PLC may hold transactions at the same time; there is no PLC-wide lock. A port is the unit of the transaction; the PLC-wide handshake serialisation question stays with the PLC engineer.
  - Q-MLEG-1 / Q-MLEG-2: multi-leg flows are executed by a configurable Route Engine; routes (including buffers and waiting) are data. BP-04 remains REVIEW and every flow equivalent to it is review-blocked.
  - Q-SLD-1: the "one task at a time" statement of the old slide is not a system-wide limit; PER_AGV stays.
  - Q-GATE-2 / Q-REL-1: a Station Transaction ends when the physical handover is done and the AMR has left the port.
  - Q-BUF-1: buffers are physical storage locations (inventory updated on the confirmed drop); an intermediate waiting position is not a storage location.
- **Assumptions recorded for confirmation:** the AGV is free between two legs (PER_AGV applied per leg, O-1); a destination port transaction opens at dispatch (O-3); the within-priority order of candidates follows the document order.
- **Still OPEN (no implementation, no write to any PLC):** M9101 ACK timing, M9120 timing and Safety_Ready, M9003/M9004/M9005 mapping to pick/drop, destination-port handshake, D1500 length/overlap, D1514 and Rack_Type encodings, reset ownership/timeouts/retries, heartbeat cycle, Phase 2 location mapping (P1-P4, M5, M6), E/G and X port discrepancies.
- **Not implemented:** live reading of the PLC registers into `PlcRequestService` and live handshake observation (`IPlcObservationSource` has only a "no data" adapter), a real RCS adapter for configurable legs, hardware verification.
- **Migration:** `AddRoutingEngine` (additive: 8 tables). `wcs_db` backed up before applying (`wcs_db_before_AddRoutingEngine_20261118-094032.dump`, verified with `pg_restore -l`).

### CR-011 – Bulk READ approval of the IO points of the PLC under commissioning

- **Status:** IMPLEMENTED (code, tests). **Not executed on any database**: the Admin runs it once on the deployment database after the new version is deployed.
- **Approval:** project owner instruction of 2026-11-18. Related: CR-003 (an address is polled only when entered AND approved), ADR-0006 (PLC runtime protections).
- **Problem:** entering an address leaves a point CONFIGURED (not pollable); approving the 92 points of PLC-04 one by one blocks read-only commissioning.
- **Behavior:** `GET /api/plc/connections/{plcId}/bulk-approval` (preview, changes nothing) and `POST` (body `{confirm:true, expectedEligible:n}`), Admin only. Allowed PLCs: configuration `Plc:BulkApproval:AllowedPlcIds`, default `PLC-04`; any other PLC is refused (`BULK_APPROVAL_NOT_ALLOWED`). Eligible = status CONFIGURED, address present and valid (format, type/device, no overlap on the PLC), STRING with encoding. The approval is the existing `StationIoPoint.Approve`; the result is re-read from the database on a fresh scope and a point that is not Approved/Pollable is reported FAILED. One audit record `PLC_IO_BULK_APPROVE`. UI: button and confirmation dialog (preview counts, result) on **Cấu hình địa chỉ IO** when PLC-04 is selected.
- **Not changed:** addresses, data types, write permission (`WriteAllowed` stays false), handshake rules, WritePolicy, AllowLiveConnection, PLC connection configuration, RealReadOnly, polling logic, MC Protocol, task orchestration, inventory, business process, other PLCs. Nothing runs at startup; no PLC write.
- **Database impact:** none (no migration, no seed).
- **Rollback:** revert the commit; approvals already recorded stay valid and can be withdrawn by clearing/re-entering an address.

### CR-012 – A valid IO address makes the point pollable without approval

- **Status:** IMPLEMENTED (code, tests). Not deployed; no real PLC was touched.
- **Approval:** project owner instruction of 2026-11-18. **Supersedes**, for READING only, CR-003 ("a real PLC is read only at addresses that are entered AND approved") and makes the CR-011 bulk approval unnecessary for polling.
- **Behavior:** `StationIoPoint.IsPollable` = the point has an entered address. The runtime and the IO list additionally apply the existing `AddressValidator` through `PlcPollablePolicy` (format, type/device, no overlap with another point of the same PLC, STRING length/encoding): an invalid or overlapping point is not polled. Entry validation is unchanged (an invalid address is refused with an error and nothing is stored). Clearing an address makes the point non-pollable; changing it re-validates.
- **Impact analysis (where `Approved` is still used, all UNCHANGED):** (1) `SetWriteAllowed` requires `Approved` - write permission still needs the approval; (2) `WritePolicy` and the three modes (Simulator / RealReadOnly / RealReadWrite with all write keys); (3) `IsPortMappingApprovedAsync` - a process cannot be published until the port mapping is approved; (4) approval is withdrawn when an address changes (as before). Reading no longer depends on it.
- **Bulk approval (CR-011):** the UI button/dialog is removed; `GET/POST /api/plc/connections/{plcId}/bulk-approval` stays for backward compatibility and answers with `Deprecation: true`. No data, field or audit history is removed; points approved earlier stay approved.
- **Not changed:** PLC IP/port/protocol, IO mapping, MC Protocol, WritePolicy, `WriteAllowed`, handshake approval (still NEEDS_CONFIRMATION until rules are approved), `Plc:AllowLiveConnection`, `Enabled`, RealReadOnly, Task Orchestrator, Inventory, Business Process. No PLC write.
- **Database impact:** none.
- **Risks:** (a) every address already entered on a deployment becomes readable on the next runtime reload/restart (intended); (b) an address entered wrongly but valid in form is now read - reading is harmless but the value shown may be meaningless, so addresses still need the PLC engineer's confirmation; (c) the diagnostics wording "not approved" (uncommitted diagnostics work) is outdated and should read "no valid address".
- **Rollback:** revert the commit; no data change is involved.

### CR-013 – PLC Diagnostic Logging (P0) and Diagnostic Export (P1)

- **Status:** IMPLEMENTED (code, tests). Not deployed. **Not verified against a real PLC.** P2 (full WCS export), Docker container status/restart count and background Correlation IDs are NOT implemented.
- **Approval:** project owner instruction of 2026-11-18. Related: ADR-0006 (logging), ADR-0004. `PlcWorker` / `McTcpClient` received **instrumentation only** (no change to connection, MC protocol, polling, timeout, retry or read/write behaviour).
- **P0:** standard codes (`PlcDiagCodes`) and stages in the searchable columns `plc_comm_log.DiagCode/Stage`; configuration snapshot at start/reload; SocketError/InnerException classification; `IO_POINT_NOT_APPROVED` (an IO point that is not polled: no address, or an invalid/overlapping address - wording updated by CR-012); `PLC_WRITE_BLOCKED`; TCP connection duration, retries and recovery; MC request/response history in PostgreSQL (`plc_exchange_log`, newest 300 per PLC, capped HEX, successes sampled one per 5 s, failures always kept, written by a side channel that never blocks or throws into the PLC path).
- **P1:** `GET /api/diagnostics/plc/export` (Admin only, audited `DIAGNOSTIC_EXPORT`) builds a ZIP in memory (Summary, System/WCS info, masked PLC configuration, Connection/Runtime/Communication/Read-Write logs, Error Summary) for one or all PLCs, last 1 h / 24 h or a custom range; `LogSanitizer` is applied before the ZIP; row/size limits; a failed section is reported inside the ZIP, total failure returns `DIAGNOSTIC_EXPORT_FAILED`. UI: **System > Diagnostics** (VI/EN).
- **Database impact:** migration `AddPlcDiagnostics` (additive: 2 columns, 1 table, indexes; `Down` only drops them). Applied by the deployment migrator; back up `wcs_db` first (deploy.ps1 does).
- **Not changed:** PLC IP/port/protocol, IO mapping, approvals, WritePolicy, AllowLiveConnection, RealReadOnly.

### CR-014 – PLC write approval per IO point and data-type specific write input

- **Status:** IMPLEMENTED (code, tests). Not deployed. No write was sent to a real PLC (tests use the MC mock server).
- **Approval:** project owner instruction of 2026-11-18. Related: CR-002 (WritePolicy, three modes), CR-012 (reading no longer needs approval).
- **Design:** the write decision is its own state `WriteApproval` (NotApproved = pending / Approved / Rejected) with who/when/note, independent of the read confirmation (`IoConfigStatus.Approved`, still used for port-mapping approval before a Business Process is published) and of pollability. `WriteAllowed` stays as the effective flag (true exactly when Approved) so the runtime and WritePolicy are unchanged. Approving needs an entered address and a Software -> PLC direction (not the read approval); Safety/Interlock/Transfer_Permit/AGV_Departure_Permit and unsupported types/encodings are refused. A new or cleared address withdraws the decision.
- **API:** `POST /api/plc/points/{id}/write-approval` {decision: Approve|Reject, note} (Admin; audited `PLC_IO_WRITE_APPROVE` / `PLC_IO_WRITE_REJECT`); `PUT .../write-allowed` kept (same decision). The IO list returns `writeApproval`, `writeDecidedBy/At/Note`, `writeSupported`.
- **Value validation (backend, before anything is queued):** `WriteValueValidator` - BOOL true/false only (the numbers 0/1 are no longer accepted), INT = signed 16-bit (`VALUE_OUT_OF_RANGE`), no fractions or text, STRING = ASCII only, at most WordLength x 2 characters, otherwise `WRITE_TYPE_UNSUPPORTED`. Only BOOL, INT and STRING exist in the IO model; the other types of the request (UINT, DINT, WORD, FLOAT ...) do not exist in the workbook/IO model and are NOT added (that would be a separate change).
- **UI (IO mapping):** "Quyền ghi" column and "Duyệt ghi" dialog (details, scope, confirm, withdraw); the write dialog has an input per data type (ON/OFF choice, numeric field with range, ASCII text area with counter), then a confirmation of PLC / Station / signal / address / type / current value / new value / user, then the real result. The write dialog remains an engineering tool (hidden in production builds; the API refuses it in the Production environment) - unchanged.
- **Not changed:** WritePolicy and the three modes (a write needs RealReadWrite with every key; RealReadOnly blocks all writes), handshake approval, addresses, connection configuration, Production refusal of the test write.
- **Database impact:** migration `AddPlcWriteApproval` (4 nullable/defaulted columns on `station_io_points`; existing `WriteAllowed = true` rows are backfilled to Approved). Additive; back up first.
- **Open:** whether the write facility should become available in Production builds (it is deliberately not).

### CR-015 – Enable PLC write for the operator's own test on the real PLC

- **Status:** IMPLEMENTED (code, configuration, tests). Not deployed. **Nothing was written to a real PLC**: every test uses the MC mock server; no write is sent at build, test or start-up.
- **Approval:** project owner instruction of 2026-11-18. **Supersedes** the CR-004 rule "test write is refused in the Production environment and hidden in production builds" and the CR-006 default `PLC_MODE=RealReadOnly`. Related: CR-002 (WritePolicy, three modes), CR-014 (write approval), CR-013 (diagnostics).
- **What was blocking a write (layer by layer):** (1) API: `RequireNotProduction` on the write endpoint; (2) UI: the write button needed `ENGINEERING_TOOLS` (never true in a production build); (3) deployment: `PLC_MODE` defaulted to RealReadOnly and the three write keys (`Plc:Write:Enabled`, `EngineerApprovalReference`, `HandshakeApproved`) could not be set from the Docker environment at all; (4) the exchange history sampled away successful writes.
- **Changes:** the endpoint no longer depends on the environment (the simulator controls still do: they only drive the in-memory simulator); the write dialog is part of every build and is offered only when the runtime is really in a write mode (otherwise the column says "Runtime chỉ đọc"), and the button is disabled with a reason until the point has a write approval; `docker-compose.yml` now has `PLC_MODE` default `RealReadWrite` and passes `PLC_WRITE_ENABLED`, `PLC_WRITE_APPROVAL_REFERENCE`, `PLC_WRITE_HANDSHAKE_APPROVED` (defaults false/empty); `deploy` validation warns on an incomplete or complete write configuration; every WRITE exchange is stored in `plc_exchange_log` (only routine successful reads are sampled); the write dialog shows the PLC end code / ambiguity detail.
- **What still guards a write (unchanged, in this order):** Admin role -> per-point write approval (CR-014) -> value validation (BOOL true/false, INT 16-bit, ASCII STRING within WordLength) -> WritePolicy: effective mode must be RealReadWrite, which needs `AllowLiveConnection` AND `Write.Enabled` AND a non-empty `EngineerApprovalReference` AND `HandshakeApproved`; Safety/Interlock, Transfer_Permit and AGV_Departure_Permit are never written; PLC -> Software signals are never written -> MC driver: success only on a confirming response; no confirmation = `WRITE_AMBIGUOUS`, never retried. Every call is audited (`PLC_TEST_WRITE`).
- **Deliberately NOT done:** the three keys are not pre-filled or defaulted to true: they are the operator's written attestations (a PLC engineer approval reference must come from a person), and the handshake sheet is not approved by this change. Nothing is approved automatically; no write on start-up, connect or screen open.
- **Database impact:** none (migration `AddPlcWriteApproval` of CR-014 is the only one pending).
- **Deployment:** set in `.env`: `PLC_ALLOW_LIVE_CONNECTION=true`, `PLC_WRITE_ENABLED=true`, `PLC_WRITE_APPROVAL_REFERENCE=<reference>`, `PLC_WRITE_HANDSHAKE_APPROVED=true` (and `PLC_MODE=RealReadWrite`, now the default); then in the UI approve the write of each point.
- **Risks:** with the keys set, an Admin can drive a real PLC output from the UI: only for signals the PLC engineer has approved; start with one non-control signal.
- **Rollback:** unset the keys (the runtime returns to read-only at the next start) or revert the commit.

### CR-016 – PLC write UI: readiness and reasons instead of a hidden action

- **Status:** IMPLEMENTED (code, tests). Not deployed. No write was sent to a real PLC.
- **Finding (cause):** the write dialog existed, but the "Ghi giá trị" action was rendered only when the effective runtime mode was Simulator or RealReadWrite. In a read-only runtime (any of `PLC_ALLOW_LIVE_CONNECTION`, `PLC_WRITE_ENABLED`, `PLC_WRITE_APPROVAL_REFERENCE`, `PLC_WRITE_HANDSHAKE_APPROVED` missing, or `PLC_MODE=RealReadOnly` left in the machine's `.env`) the page showed only a small grey "Runtime chỉ đọc" label: after an Admin approved a write there was no visible write action and no hint which setting was missing. The backend exposed three of the four conditions and never said which were missing; the page also treated an unloaded runtime as "simulator" and did not re-read the runtime after an approval. The actual runtime of the Deploy machine was NOT inspected (no access): this is the cause established from the code and from tests that reproduce the read-only case.
- **Changes:** `/api/plc/overview` runtime info now also returns `writeApprovalReferenceSet`, `writeHandshakeApproved` and `missingWriteKeys` (names only - the approval reference is never sent). The IO mapping page shows a status banner (writes allowed / read-only and which settings are missing), always shows "Ghi PLC" for a writable point and disables it with the concrete reason beside it (runtime read-only + missing keys, no address, unsupported type, not approved / rejected, PLC not connected), treats an unknown runtime as not writable, and re-reads points and runtime right after an approval. `src/lib/plcWrite.ts` holds the decision (tested by `npm run plcwrite:test`).
- **Not changed:** Admin-only write and approval, per-point write approval, value validation, audit, safety/permit signals refused, no automatic write, the write keys (not set by this change), handshake approval.
- **Database impact:** none. **Deployment:** none beyond CR-015 (the keys must be set in `.env` for the runtime to leave read-only; the banner now names the missing ones).

### CR-017 – Why the Deploy runtime stays read-only: configuration chain and diagnostics

- **Status:** IMPLEMENTED (code, configuration, tools, tests). Not deployed. No write sent to a PLC. The Deploy machine itself was NOT inspected (no access): the cause below is established from the repository and from tests of the whole chain.
- **Chain checked (all by tests, with the real EnvironmentVariables provider):** `.env` -> `docker-compose.yml` substitution -> container variables (`Plc__Mode`, `Plc__AllowLiveConnection`, `Plc__Write__Enabled`, `Plc__Write__EngineerApprovalReference`, `Plc__Write__HandshakeApproved`) -> ASP.NET configuration -> `PlcRuntimeOptions` -> effective mode / `missingWriteKeys` -> Runtime Info API. Names, binding, boolean spellings (true/True/TRUE), and the API are correct: with every key the runtime is RealReadWrite; each missing key alone is reported alone; the reference is never returned. No default overrides a deployment value.
- **Cause found:** `deployment/.env.example` - the file the README tells the operator to copy to `C:\ETEK-WCS\config\.env` - still contained `PLC_MODE=RealReadOnly` (set explicitly, so it overrides the new compose default of CR-015) and none of the three write keys. A `.env` created from it (or from the earlier README instructions) keeps the runtime read-only whatever else is done, and `PLC_MODE` appears first in the banner - exactly the reported message. Secondary causes that the tools now expose: a key missing/false/empty, an `.env` in another folder, a backend container not recreated after the `.env` change.
- **Changes:** `.env.example` now has `PLC_MODE=RealReadWrite` and the three write keys (false/empty, with the explanation); `_common.ps1` warns when the `.env` forces `PLC_MODE=RealReadOnly`; new read-only `deployment/scripts/plc-write-config.ps1` compares `.env`, the compose resolution and the RUNNING container's environment (the reference only as set/unset); `WritePolicy.MissingWriteKeys` is the single source of the missing list; the `RUNTIME_START` log line names the requested mode and missing keys; the IO mapping banner shows the five settings as received by the backend (✓/✗, `PLC_MODE` with its current value). README section 10 explains the checks.
- **Not changed / not done:** no approval reference, no key set to true, read-only checks, write approval, Admin authorization, validation, audit and the safety/permit ban unchanged. The operator must still edit `.env` (the keys stay false/empty by default) and run `deploy.ps1`.
- **Database impact:** none.

### CR-019 – Simplify PLC write approval (remove the dual approval)

- **Status:** IMPLEMENTED. Not deployed. No write sent to a real PLC (tests use the MC mock).
- **Removed:** `PlcWriteOptions.EngineerApprovalReference` / `HandshakeApproved` (PLC_WRITE_APPROVAL_REFERENCE, PLC_WRITE_HANDSHAKE_APPROVED), their use in `WritePolicy.EffectiveMode/WriteKeysComplete/MissingWriteKeys`, the Runtime Info fields `writeApprovalReferenceSet` / `writeHandshakeApproved`, the banner checklist entries, the compose variables, the .env.example lines, the diagnostic-export fields. Old lines in an existing `.env` are ignored (no longer passed by compose; unknown configuration keys are ignored by binding): no error, no warning.
- **Write conditions now:** Mode RealReadWrite + AllowLiveConnection + Write.Enabled, Admin, address, WriteApproval=Approved, valid type/value, PLC connected, not a blocked signal, user confirmation.
- **Kept unchanged:** Admin-only, per-point write approval, AddressValidator, WriteValueValidator, safety/permit ban, PLC->Software not writable, confirmation step, audit, WRITE_AMBIGUOUS / no automatic retry, no write on start/connect/open. The protocol handshake rules (`Handshake.cs`, Plc:Handshake) are a separate control function and were NOT touched.
- **Database impact:** none (no migration; WriteApproval/WriteAllowed data and audit untouched).
- **Note:** supersedes the key lists of CR-015 and CR-017; the earlier text is kept as history.

### CR-020 – Unified PLC monitoring and I/O operations

- **Status:** IMPLEMENTED. Verified with the MC mock server only. Not deployed. No real PLC was read or written.
- **IO Address Mapping:** configuration only. Removed from it: the "Ghi PLC" button, the write-status banner, the simulator button. Kept: address, data type, direction, polling status, write approval (decision), all existing filters.
- **PLC Monitoring:** new tab "I/O monitoring": PLC, station, signal, description, address (Admin), data type, current value, status (Good / Stale / Bad / Offline / Unknown, plus NotConfigured and Simulated), last update, write approval (Admin), Read, Write (Admin). Filter by PLC/station/status/text, sort by column.
- **Read:** immediate read of one point through the PLC's own worker and connection (`PlcWorker.EnqueueReadAsync`; no second channel, no second polling loop). Concurrent requests for one point share one PLC read. Both roles may read (it never writes).
- **Write:** the CR-019 dialog and `test-write` API, unchanged guards. New: after a successful write the point is read back once and compared (VERIFIED / MISMATCH / UNAVAILABLE); the outcome is audited and shown. A successful write request is never presented as proof. WRITE_AMBIGUOUS is not read back automatically and never resent.
- **Status rules:** Good only when the last read succeeded and is not older than `Plc:StaleAfterSeconds` (default 15); the list is recomputed on the server each time it is read.
- **Not changed:** MC protocol, register mapping, PLC configuration, polling mechanism, CR-019 guards, handshake, diagnostics. No migration. No new PLC traffic from the monitoring list (it reads values the workers already hold).
- **Not verified against a real PLC:** read/write/read-back timing and values on real hardware.

### CR-021 – System-wide diagnostic logging and export

- **Source:** project owner (2026-11-18). Phase-1 survey and proposal: `docs/reviews/CR021_PHASE1_LOGGING_SURVEY_AND_PROPOSAL.md`. Decisions D1-D8 approved: CorrelationId nullable on `system_log` (the column already exists), `TransportTask` and `TransportOrder`; export "all" follows each source's real retention (no 31-day cap); ZIP kept 24 h in a private temp folder then deleted; logging standardised at `IRcsClient` on the simulator only (no real RCS/AGV exists); API requests logged when >= 1000 ms or 5xx/401/403; order A -> B -> C -> D.
- **Batch A (implemented):** normalised event model (`DiagEvent`) read at export time from `system_log`, `plc_comm_log`, `audit_log`, `task_history`, `routing_events`, `inventory_transactions`; streaming background export job (`POST/GET /api/diagnostics/exports`, download endpoint, Admin only, audited as DIAGNOSTIC_EXPORT_SYSTEM / DIAGNOSTIC_EXPORT_DOWNLOAD); ZIP with Summary, Manifest, System_Info, Health_Snapshot, Events, Errors, Timeline, Modules, PLC (existing PLC export unchanged), Audit, Configuration (masked); each source guarded, failures reported (Manifest and Summary); ranges 1h / 24h / all / custom; scopes: whole system, modules, level, PLC, device, CorrelationId, TaskId (related rows of other modules pulled in by shared ids); cleanup service; Admin UI on the Diagnostics page. No migration. PLC export endpoint and format untouched.
- **Batch B (implemented):** `DiagnosticContext` (AsyncLocal frame: CorrelationId, TaskId, AgvId, PlcId, StationId, RackId) + Serilog enricher (every log event carries the frame; an explicit property is never overwritten); HTTP keeps a valid inbound `X-Correlation-Id` (8-64 harmless characters) else the request id; migration `AddCorrelationIds` (**additive**: nullable `CorrelationId varchar(100)` + index on `transport_tasks` and `transport_orders`; `system_log.CorrelationId` already existed); tasks and orders store the id of the creating request (or a new one) once; every task step (process, dispatch, continue, cancel, reconcile, timeout, signal) and every order/leg step re-enters the task's / order's own id, so a task keeps ONE id across requests and workers; the export sources join the id (task_history, inventory, audit via the task; routing_events via the order). PLC events are not tagged (no basis). Rows created before CR-021 keep a null id: they export with a null correlationId and remain reachable by TaskId.
- **Batch C (implemented):** selective diagnostic events, all through the existing logging pipeline (`ILogger` + `EventCode` -> `system_log`), carrying the batch-B ids: `LoggingRcsClient` (decorator of `IRcsClient`: operation, request code, outcome, duration, adapter; the Production simulator guard looks through it with `IRcsClientDecorator.Unwrap`, so the guard is unchanged); task decisions (TASK_CREATED, TASK_CREATE_REJECTED, TASK_DISPATCH_DECISION / TASK_DISPATCH_WAIT with the reason, logged on CHANGE not per tick, TASK_CANCEL_REQUESTED, TASK_FINISHED for Completed/Failed/Cancelled); station requests (STATION_REQUEST_ACCEPTED / REJECTED / FAILED / ABANDONED, each request gets its own CorrelationId which the task it creates keeps); AGV connection status changes; inventory (INVENTORY_UNCERTAIN, INVENTORY_RECONCILED); database (`DbDiagnosticsInterceptor`: slow commands >= Diagnostics:SlowQueryMs (1000) and failures classified by PostgreSQL code - deadlock, constraint, timeout, connection - never parameter values or server detail text); API (D7: >= 1000 ms, 5xx, 401, 403 are stored with a stable event code; 400/404/409/422 are not); SignalR connect/disconnect (error = warning); application and worker lifecycle (APP_STARTED / APP_STOPPING / APP_STOPPED with the worker list), unhandled and unobserved exceptions outside requests; startup configuration warnings. No decision, state or retry logic changed.
- **Batch D (implemented):** end-to-end scenario on the real logging pipeline (Serilog + enricher + persisting sink -> system_log) with the real task/scheduler/inventory code, the RCS simulator and the MC mock server: a task whose RCS continue is refused is reconstructed from the package alone (created -> AGV decision -> RCS create -> RCS refusal -> finished Failed -> inventory uncertain, all under one CorrelationId; exception + stack trace with module/task/correlation; PLC end-code errors present but NOT linked to the task; secrets masked; manifest honest). Volume test: 500 000 events exported in about 25 s into a 13 MB ZIP with about 22 MB managed-heap growth (streaming), and a per-source limit keeps the newest rows and says how many were left out. Module of a system_log event now follows its event-code prefix before its text. For mode "all" the PLC folder counts as cut only if stored PLC events older than its 31-day cap exist. Full report: `docs/reviews/CR021_COMPLETION_REPORT.md`.
- **Limits not verifiable without real devices:** real RCS request/response and error codes, AGV battery/position, MC traffic of a real PLC, behaviour under the real Docker/PostgreSQL deployment, export of months of production data.
- **Not changed:** business logic, dispatch, PLC/RCS/AGV behaviour, protected business rules.

### CR-022 – Diagnostics screen: single export, PLC folder kept

- **Source:** project owner (2026-11-19), after reviewing a package exported from the DEV machine.
- **UI:** the PLC-only export area (PLC scope, its own range selector, its button, its contents list) is removed; one whole-system export area remains with: whole system / by module (including PLC) / a specific PLC / TaskId / CorrelationId / level, and ranges 1 hour, 24 hours, all stored data, custom. A hint explains how to get the PLC logs alone. The frontend download helper of the PLC-only export was removed; the backend endpoint `GET /api/diagnostics/plc/export` is kept and the system export still builds its `PLC/` folder from it.
- **Finding in the exported package:** 44 DB_COMMAND_FAILED (SQL state 42703) and 141 API_REQUEST_FAILED (HTTP 500 on /api/tasks and /api/agvs) between 01:50 and 02:00 UTC were caused by the DEV database missing migration `20261118140420_AddCorrelationIds` (reported by the package itself as DB_MIGRATIONS_PENDING). The migration is additive and valid; it was applied to the DEV database only (`dotnet ef database update`); no error after that. The package diagnosed the problem correctly.
- **Second finding:** a SignalR connection (`/hubs/wcs`, HTTP 101) that stays open for hours was reported as API_REQUEST_SLOW. HTTP 101 is now never a slow request and is not stored as a request event (the hub logs connect/disconnect).
- **Not changed:** the Production environment; no migration was run on it.

### CR-023 – Compact layout of the PLC connection and PLC monitoring screens

- **Scope:** presentation only. No change to PLC communication, MC protocol, polling, Read/Write, SignalR, APIs, database or permissions.
- **PLC connections:** two PLC columns on a wide screen (one on a tablet/phone); inside each card the parameters are in two columns (label and value on one row, labels aligned) under light group titles (Connection, MC Protocol routing, Communication, Data types); connection state and configuration state both in the card title; the missing-configuration warning right under the title; the four actions in one row of small buttons. A card narrower than 520 px stacks its two columns; narrower than 300 px puts each label above its value.
- **PLC monitoring:** tighter PLC cards (IP next to the name; all information kept); 16 Station tiles in 2 rows of 8 from 1600 px wide, 4 columns from a tablet, 2 on a phone; each tile shows station, PLC, IO count/quality, phase and a short handshake marker (full text as tooltip); selected tile highlighted; the IO detail panel is unchanged below the grid (tile height does not change when a station is open); IO table rows slightly tighter.
- **Texts removed from the screens** (i18n keys deleted; business rules and data untouched): the IO-mapping "configuration only / read-write on monitoring" note, the routing-configuration subtitle of the process page, the Transport Task/Leg/Station Transaction subtitle of the routing page, the AGV-status-history explanation on the dashboard (the "not available" label stays).
- **Shell:** on a phone (<= 640 px) the menu is always the icon-only rail (it took half of the screen); the stored preference is not changed.
- **Verification:** real browser (Chrome driven by the DevTools protocol) on a scratch backend in Simulator mode and a scratch database, at 1920x1080, 1600x900, tablet 820 and phone 390: no horizontal overflow; station selection, realtime refresh through SignalR, Read and Write (Admin: Write present and disabled until write approval; Operator: Read only, no Write, no addresses, no write banner, no access to connections) all checked. Screenshots before/after: `Downloads/CR023_screenshots/` (not committed).

### CR-024 – Bilingual Description of IO signals
- **Request:** the Description column of the IO Mapping table must show Vietnamese in VI and English in EN for all signals of all PLCs/Stations; central translation; Signal Name, address, data type and configuration unchanged; no database / PLC change unless necessary.
- **Analysis:** the 368 points are instances of 23 template signals; the description is set once from the controlled catalog (`StationIoPoint.Description`, private set, no edit path), so the text is a function of the Signal Name. The database and the catalog are therefore left untouched; the translation is display-only.
- **Implementation:** 23 keys `plc.sigDesc_*` (vi = the existing text verbatim, en = new) in `i18n/messages/plc.ts`; `lib/plcSignals.ts` `signalDescription` with fallback to the stored text; used by IO Address Mapping and the PLC monitoring IO tab; `check-i18n` knows the dynamic prefix.
- **Verification:** type check, ESLint, i18n check, build, 6 smoke suites; real browser (scratch backend/DB, Simulator): all 368 rows VI = catalog text, EN = English, back to VI, no reload (page marker kept), Signal Name unchanged; screenshots in `Downloads/CR024_screenshots/` (not committed).

### CR-025 – Modern Industrial UI
- **Request:** modern, less boxed look; KPI cards; charts from real data only; unified components; responsive; keep VI/EN, CR-023 and CR-024; no logic change.
- **Survey:** React 19 + Vite, custom tokens (`tokens.css`, light + dark), shared `ui.tsx` / `charts.tsx` (dependency-free SVG charts), 10 CSS files, every page already uses Panel / Kpi / Pill / table.grid, so the upgrade was done in the shared layer first, then per page.
- **Implementation:** see CHANGELOG v3.15. Pages with a new status chart: Tasks, AGV, Inventory, PLC monitoring. The Dashboard, Logs, Diagnostics, Users, Settings, PLC connections / IO mapping, Processes and Account pages are restyled through the shared layer.
- **Not done on purpose (no data):** error trend over time, PLC communication history, inventory history; no backend was added for them.
- **Verification:** type check, ESLint, i18n check, build, 6 smoke suites; real browser (scratch backend + DB, Simulator; Demo mode for populated data) at 1600x900, tablet 820 and phone 390 on 16 screens: no horizontal overflow; VI/EN switch without reload; Admin and Operator menus and route guards; dark theme. Screenshots before/after in `Downloads/CR025_screenshots/` (not committed).

### CR-026 – Configuration Export / Import
- **Request:** export/import of WCS configuration between systems; scope and decisions D1–D7 approved by the project owner after the Phase 1 survey (no secrets, no approval state, IO at Configured level only in Phase 3, file configuration out of scope, users out of scope, maintenance mode mandatory for Apply, no deletion).
- **Status:** PENDING_APPROVAL – impact report and Phase 2 plan are in `CR-026_CONFIG_TRANSFER.md`; five open questions (OQ-1 … OQ-5) need an owner decision before code.
- **Key finding:** identifiers are Guid or string, not auto-increment; transfer uses natural keys (Code, PlcId, StationId, StepKey) and new Guids are generated on the target.
