# Change Log

## CR-062 Confirmation before loading reference processes (2026-11-28) – branch feature/cr-062-import-confirmation, NOT MERGED

- The button shows which processes would be added or kept before anything is loaded. No migration.

## CR-061 Location map as a floor plan (2026-11-28) – MERGED into main (f15743b)

- The location map of "Locations and inventory" draws the machine, buffer and manual QC zones around a path. Frontend only; cards and data unchanged.

## CR-060 BP-04 removable; PH2-04 guidance (2026-11-28) – MERGED into main (c6d5a65)

- BP-04 is no longer protected from removal (it stays REVIEW and unpublishable). The REVIEW error explains how to apply the default catalog (PH2-04). No migration.

## CR-059 Replaced process versions keep no location (2026-11-28) – MERGED into main (cb9cb0d)

- A disabled process version no longer stops a location from being removed unless an unfinished order follows it. The removal dialog names the version. No migration.

## CR-058 Configured locations can be removed (2026-11-27) – MERGED into main (1ee4fe9)

- A location named by the deployment configuration is no longer kept for that reason: the removal dialog warns which flows and PLC codes are affected. The flow list no longer offers a removed station. No migration.

## CR-057 Location removal (2026-11-27) – MERGED into main (f27fa1b)

- An Admin can remove a location nothing depends on any more; past records keep showing its code and the code can be used again. Migration AddLocationRemovedAt.

## CR-056 Process deletion keeping history (2026-11-27) – MERGED into main (f27fa1b)

- A process with finished orders can be deleted; orders and history stay. BP-04 is the only protected process. Migration DropOrderProcessForeignKeys (two foreign keys).

## CR-055 Process removal (2026-11-27) – merged into main (e3a8aae)

- An Admin can delete a process that never had an order, retire one with finished orders and delete an unpublished version; BP-01..06 are protected. Reason and audit required. No migration.

## CR-054 Process names language (2026-11-27) – merged into main (e3a8aae)

- Delivered reference process names are shown in the chosen language. Frontend only; stored names unchanged.

## CR-053 Dashboard counts (2026-11-27) – merged into main (e3a8aae)

- The dashboard counts multi-leg transport orders together with the single tasks. Frontend only.

## CR-052 Destination waiting reason (2026-11-27) – merged into main (e07bebc), not deployed to production

- A waiting order with no usable destination names the cause of every candidate; a held port is shown as such. No change of any rule. No migration.

## CR-051 Task visibility across modules (2026-11-27) – merged into main (ae48ccd), not deployed

- Task management lists Phase 1 tasks and transport orders together; AGV monitoring shows what both engines sent to the RCS, with the RCS state. Read only; no migration.

## CR-050 Simulator execution (2026-11-27) – merged into main (105549a), not deployed

- Follow-up: optional names of the simulated fleet (`Rcs:Fleet:SimulatorRobotCodes`), fault "AMR does not leave the port", acceptance report.

- PLC simulation usable with the simulator PLC runtime (real modes unchanged). AMR simulation behind the simulated RCS, off by default and limited to work created after it is switched on: travel, pick, drop, port exit, faults, panel on the PLC simulation page.
- Fix (business behaviour, also in real mode): a leg rejected by the AGV system frees its station ports only when nothing has happened on them; a port with a confirmed pick or a further advanced transaction stays held and needs reconciliation. No migration.

## CR-049 QA fixes (2026-11-27) – merged into main (0db229a), not deployed

- A waiting order names the real cause when its station port is held; the IO address page says what 96 and 120 count. No migration, no rule change.

## CI of CR-048 and documentation cleanup (2026-11-27) – no version change

- The Compose smoke checks the default IO addresses (CR-048) instead of "no configured address"; green on main `5916d05` (https://github.com/HOANGANHVI/ETEK-WCS/actions/runs/45933643008). No product code change.
- Documentation: historical reports moved to `docs/archive/`, two unreferenced screenshot sets deleted, index rewritten. See `docs/DOCUMENTATION_CLEANUP_REPORT.md`.

## v3.38 – CR-048 Default IO addresses from the IO map (2026-11-26) – version 2026.11.26.4

- Every IO point starts with the address written in the workbook; an address an Admin enters or removes is never overwritten; "restore default" brings the workbook address back. No write permission follows from an address.
- Upgrade at start: rows of signals outside the IO map move to `retired_io_points`, stations X6/G3/G7 are retired and their location links released. Migration `AddIoDefaultAddress` (run on the local development database on 2026-11-27 with a backup; not on an operating system's database).

## v3.37 – CR-047 Hotfix: IO map as the workbook, rack removal (2026-11-26) – version 2026.11.26.3

- The IO address page shows the workbook sheets as written (11 columns, 120 rows, merged cells, sequence); no signal or station of an earlier catalog; the 7 simulation test inputs are removed.
- A rack that cannot be removed says which task or order holds it and leads there; the order screen offers the cancellation that the API already had. Refusal rules unchanged; no migration.

## v3.36 – CR-046 IO map catalog, IO screens, F1/F2, Docker (2026-11-26) – version 2026.11.26.2

- The PLC catalog comes from the IO map of 22/11/2026 (16 stations incl. X4, G1, G2; 6 signals per station). Rows of the earlier catalog stay in the database, unused. No migration. The signals are unconfirmed: no write approval, no real write.
- IO screens: lists from the backend, IO map address apart from the configured one, read / write state, PLC-level signals, no placeholder values for points without an address.
- F1: PER_AGV counts transport legs. F2: an order that has not started reserves no destination while its AGV has a leg without destination (`RouteEngine:DeferDestinationWhileAgvNeedsOne`).
- Docker images and Compose checked on Linux by the `docker-smoke` workflow. Not deployed; no real PLC / RCS test.

## v3.35 – CR-045 Single IO map and test deployment (2026-11-26) – version 2026.11.26.1

- The only PLC IO map is `AGV_PLC Interface_List_22_11_2026.xlsx` (sha256 `405586c9…34da3`); the 20/11 and 10/11 workbooks are removed (IO Map v2 stays removed). Running catalog, database and communication logic unchanged; the IO contract is not confirmed by the PLC engineer. OW-5 and OW-6 closed.
- Docker: `PLC_WRITE_ALLOWLIST` reaches the backend (`Plc:Write:AllowListText`, empty by default; one unreadable entry declares nothing). Test-machine instructions; `deploy.ps1` summary shows the real default PLC mode.
- No migration. Not deployed; Docker images not built and no real PLC test.

## v3.34 – CR-044 PLC write guard and MissionCompleted correction (2026-11-26)

- **Breaking for real writes (OW-2):** a real PLC write now also needs an explicit declaration in `Plc:Write:AllowList` (PLC, station, signal, address; empty by default) and must not reach a register of `Plc:Write:ReservedAddresses` (the unconfirmed 20/11/2026 interface). Second lock in the connection worker; every real write decision is stored in `audit_log` and a write whose authorization cannot be stored is not sent; refused write requests (401/403, lookup, value) are audited. Reading and the simulator are unchanged. `docker-compose.yml` does not yet pass an allow list: Docker deployments cannot write until it is declared.
- **Correction (OW-4):** `PLC_MissionCompleted` `1` = Failure, `2` = Success (CLAUDE.md D-4, ADR-0011, traceability, correction banners on the CR-029 V2 documents). No code reads this signal.
- No migration. Merged into main 2026-11-26, not deployed, tested against the mock PLC and the simulator only. See CR-044.

## v3.33 – CR-042 Approved review decisions (2026-11-26)

- Header of a published process locked (also for catalog drafts and configuration import); catalog processes not approved for publication refused; versions disabled by an Admin hold their waiting requests; stalled queue warning (10 min, `RouteEngine:QueueStallWarningMinutes`); stock adjustment keeps racks of unfinished orders; one engine per station (`Plc:Integration:StationEngines`, default empty); one dispatch-pending event per reason; rack dialog. Corrections to CR-040/CR-041 statements about the physical PLC path. Migration `AddVersionWithdrawnByAdmin` (not run on a deployment). Review follow-up (OQ-42-08): a rack of an unfinished transport order belongs to it (also at a buffer) and is offered to no other order or task; a failed order that moved a rack keeps it until an Admin releases it (`release-racks`, migration `AddOrderRacksHeld`). Open, reproduced, not fixed: PER_AGV is not checked by the task engine against route legs (F1); a reservation of a leg without a free AGV can deadlock (F2). OQ-54 values not applied (deadlock risk). Not deployed; not verified in a browser or with real devices. See CR-042.

## v3.32 – CR-041 Unified operations, process catalog, PLC input handler, rack records (2026-11-26)

- One request option list and create dialog on the three operation pages; one RequestKey across the task and order engines. Admin can create a Draft from the confirmed catalog for an existing default process. Shared PLC input handler for physical and simulated signals (physical trigger rule not approved: nothing processed); a simulated request creates no work in Real mode (behaviour change); station reset, IO address and simulation history. Rack type and lock reason. Migration `AddSimulatedStationTransaction` (not run on a deployment). Not deployed; not verified in a browser or with real devices. See CR-041.

## v3.31 – CR-040 Transport queue, linked operations, PLC simulation, rack records (2026-11-25)

- Queue: Published + Ready admission, fixed queue position, strict FIFO per declared scope, Held; no scope declared by default (OQ-54). New publications require Ready. PLC simulation needs only Admin + confirmation and simulates single PLC → WCS signals through the one request intake. Guarded rack create / edit / delete (Admin). One request list on the three operation pages, RCS commands per leg, readiness and scope controls. Migration `AddTransportQueue` (not run on a deployment). Not deployed; not verified with real devices. See CR-040.

## v3.30 – CR-039 Default transport process catalog (2026-11-25)

- Default processes PH2-01..04, 06..12 published once per database and NotReady (PH2-05 Draft); PH2-07..12 added to the reference data; PH2-03 overflows to M5; PH2-04 review flag removed (D-09; BP-04 stays REVIEW). Location catalog v2 (X1-X4, Y1/Y2, Z1/Z2, P1-P4, M5, M6). Device conditions moved from Publication to Readiness for flagged processes (migration `AddProcessRequiresReadiness`, not run on a deployment). Both request forms list every published process. A3/A4-D1/D2 unchanged. Not deployed; not verified with real devices. See CR-039.

## v3.28 – CR-036 Product Model isolation and Manual QC protection, Batch 1 (2026-11-25)

- Additive schema (product models, location model/role, rack model/disposition, process model); guards behind `ModelIsolation:Mode` (default Off); one-way `ManualHandling` disposition at manual QC stations; rollback guard. See CR-036. Not deployed; re-intake, UI, ConfigTransfer export and Queue not included.

## v3.27 – CR-035 AGV Phase 2 first runnable increment (2026-11-24)

- Operational Readiness axis (`NotReady`/`Ready`/`Suspended`) per process version + migration `AddProcessVersionReadiness`; intake gate behind `RouteEngine:EnforceReadiness` (default off). Real-RCS leg template (`Rcs:RealDispatch:LegTemplate`, empty = Blocked as before). Admin manual PLC simulation (`/api/plc-simulation/*`, real RCS only, `PlcSimulation:Allowed` default off). Queue Request/Held/Scope and Pick/Drop triggers not implemented (see CR-035). PLC mapping comparison document added; no workbook changed.

## v3.26 – CR-033 RCS integration log category fix (2026-11-23)

- `LogClassifier` now classifies RCS integration events (`RCS_*`, `AGV_STATUS*`) as RCS / AGV by event code, whichever class writes them; `FleetService` events (`RCS_FLEET_POLL_SUCCESS`, `RCS_FLEET_QUERY_RECOVERED`, …) used to fall into SYSTEM, so the RCS / AGV KPI read 0. Audit rows (RCS Diagnostics, RCS settings) stay in SECURITY_AUDIT. No migration; historical rows unchanged (optional reviewed backfill documented in `CR-033_RCS_LOG_CATEGORY.md`). Query, KPI, paging and export unchanged (they read the stored category).

## v3.25 – CR-032 addendum: MAP ID TT and RCS diagnostics parameters (2026-11-23)

- `Rcs:Diagnostics:MapId` (default `TT`) is the central MAP ID, applied only to the request field `mapShortName`; Admin-editable saved parameters (`rcs_diag_params`, migration `AddRcsDiagParams`) for `clientCode`, `agvTyp`, `wbCode`, `ctnrTyp`, `roadWayCode`, area codes; fields grouped Confirmed / User input needed / Not determined; saved values reload in the Request Builder. No change to CR-031 fleet monitoring, Real Dispatch, PLC, Inventory or Legacy dispatch. See `CR-032_RCS_DIAGNOSTICS.md` section 10.

## v3.24 – CR-032 RCS Diagnostics & Manual API Tester (2026-11-23) – feature branch, mock verified, not merged

- New Admin-only module (menu System, route `/rcs-diagnostics`, API `/api/rcs-diagnostics`): catalog of the 23 outbound RCS-2000 V3.3 APIs, document-driven request builder with server validation and preview, immediate execute (no approval), request / response inspector with outcome classes (TCP, timeout, HTTP, RCS code ≠ 0, JSON), masked execution history with filters / export, TCP check kept apart from API check, honest Callback Monitor (no WCS receiver on this build). Additive migration `AddRcsDiagnostics` (`rcs_api_tests`, `rcs_callback_log`). No change to Real Dispatch, fleet monitoring, PLC, inventory or Legacy dispatch. See `CR-032_RCS_DIAGNOSTICS.md`.
## v3.21 – CR-030 Real RCS dispatch adapter (2026-11-21) – feature branch, MOCK verified, not merged

- `HikRcsClient` (`IRcsClient`) for RCS-2000 API V3.3: `genAgvSchedulingTask`, `queryTaskStatus`, `continueTask`, `cancelTask`; RCS → WCS `agvCallback` endpoint (not mapped unless enabled; authenticated by WCS-side checks only; refused when none is configured; never completes a task or confirms a drop).
- **OFF by default** (`Rcs:RealDispatch:Enabled=false`); location / process mapping is configuration and empty by default; missing mapping, RCS connection or port → fail closed (nothing sent, clear error, no token in logs). No `agvCode` in RcsManaged requests; `taskCode` = WCS request code; deterministic `reqCode` (≤ 32); code 6 accepted; 99 / timeouts / connection errors = unknown outcome.
- Routing legs are blocked with the real RCS (no confirmed leg mapping); the simulator API is not exposed in real mode. Compose / `.env.example` / nginx (`/rcs/`) / `appsettings.example.json` carry the (default-off) keys. No database change. Verified with a mock RCS server only.

## v3.20 – Deployment configuration for the real RCS fleet source (2026-11-21)

- `deployment/docker-compose.yml` and `.env.example` pass `Rcs:Fleet:Source` (`RCS_FLEET_SOURCE`, default `Simulator`), `Rcs:Fleet:IntervalSeconds` (`RCS_FLEET_INTERVAL_SECONDS`, default 5) and `Rcs:Dispatch:Mode` (`RCS_DISPATCH_MODE`, default `Legacy`) to the backend. Defaults keep the previous behaviour; `Integration__Mode` stays `Simulator` (no real create-task adapter exists). No PLC/RCS address or credential is stored in the repository.
- `docs/reviews/REAL_DEVICE_COMMISSIONING_PLAN_2026-11-21.md`: what the code supports in Real mode, environment limits found, deployment steps for the Deploy machine, commissioning levels, missing integration.

## v3.23 – CR-031 fix: every RCS fleet poll is logged (2026-11-23) – feature branch, not merged

- A non-success RCS answer (HTTP 200, business code ≠ 0) is logged as `RCS_FLEET_POLL_FAILED` with endpoint, HTTP status, RCS code and message, duration, request id and consecutive failures; new class `RCS_CONNECTION_FAILED`; the console line bypasses the 60-second log throttle; the `system_log` copy is rate-limited; "Chưa nhận được dữ liệu AMR từ RCS" when no poll ever succeeded. Cause: the loop logged only success → failure transitions and the pipeline throttled identical warnings. Details: CR-031 section 7.

## v3.22 – CR-031 Real RCS fleet monitoring (2026-11-21) – feature branch, mock verified, not merged

- `Rcs:Fleet:Source=Real`: WCS calls `queryAgvStatus` every 5 s (single-flight, timeout, early-tick tolerance); the AMR count is `data[]`; the port is the saved RCS connection port (no assumed 8182); NO authentication: the request carries no tokenCode/username/password (the RCS provides none for this API) and polling never depends on a token.
- Logs per poll: `RCS_FLEET_POLL_STARTED`, `RCS_FLEET_POLL_SUCCESS`, `RCS_FLEET_POLL_FAILED`, `RCS_FLEET_SNAPSHOT_UPDATED` (credentials masked).
- No simulated AMR data in operation: Real source hides the business AGV cards (one list from the RCS), business AGV status is never taken from the simulator (`Unknown / RCS_FLEET_REAL`), "RCS DISCONNECTED" with the last good time, Production refuses a simulated fleet (`Rcs:Fleet:AllowSimulatorInProduction`), deployment default `RCS_FLEET_SOURCE=Real`. No database change.

## v3.19 – CR-029 PLC interface 20/11/2026 specification (2026-11-21) – merged into main, documentation only, PENDING_APPROVAL (pending PLC clarification)

- New specification set `docs/02_Interface_Specification/PLC_20_11_2026/` (Interface, Handshake, IO Mapping, Architecture, Transaction Lifecycle, Error Handling & Recovery, Test Specification, Change Impact Analysis, Vendor Questions PQ-01…PQ-25) based on `AGV_PLC Interface_List_20_11_2026.xlsx`.
- Older PLC drafts marked SUPERSEDED / partly obsolete (kept, not deleted); `PLC_IO_Map/README.md` records the status of the three workbooks.
- Conflicts recorded, not resolved: one transaction per PLC vs per Station (ADR-0008), `Mission_Feedback` AGV1–3 vs dynamic AMR fleet (CR-028), CLAUDE.md §7 vs the new interface, G1/G2 vs G3/G7.
- No code, database, migration, Docker, `.env`, PLC address or running mapping changed.
- **2026-11-21 decisions recorded (ADR-0011):** D-1 at most one active Handshake per PLC with one FIFO queue per PLC (not implemented; the current code has no PLC-level FIFO; ADR-0008 decision 4 superseded in part); D-2 AGV1–3 are not an AMR identity, WCS stores the real `RobotCode`, `Mission_Feedback` = request status, new feedback interface to be agreed; D-3 CLAUDE.md section 7 updated (20/11 interface = new, pending; IO Map v2 = old baseline); D-4 loss of physical rack reconciliation not accepted (rack presence, identity, transaction uniqueness, interlock; `PLC_MissionCompleted=1` is not sufficient). Vendor questions reorganised (pending questions / decisions / interface proposals IF-P1…IF-P5), PQ-26…PQ-29 added. CR-029 stays PENDING_APPROVAL.


## v3.18 – CR-028 Dynamic fleet dispatch (2026-11-21) – merged into main (RcsManaged verified on the RCS simulator only)

- **`Rcs:Dispatch:Mode`** `Legacy` (default, unchanged) | `RcsManaged`: the task is sent **without** an AMR code and the (simulated) RCS chooses; WCS learns the robot from `queryTaskStatus` / the callback (`RobotCode`, `AssignedAt`); `RcsDispatchState` tracks NotSent / Sending / Accepted / AwaitingAssignment / Assigned / SyncError (the 13 task states are unchanged).
- **Data:** `TransportTask.AgvId` nullable; new columns; PER_AGV index narrowed to Legacy tasks (migration `AddRcsManagedDispatch`).
- **Fleet-level `MaxInFlightTasks`** (default no limit); retries keep the request code and query before re-sending.
- **UI:** one fleet list in RcsManaged mode (no AGV1/AGV2 cards); tasks show robot / hand-over state; dashboard per-robot figure.
- **Follow-up:** `RouteEngine` legs in RcsManaged mode no longer choose an AGV by role (no robot code, no PER_AGV / hold, Station Transaction not "AgvAssigned"); rollback of `AddRcsManagedDispatch` stops with `ROLLBACK_REFUSED` while tasks have no AGV or RcsManaged tasks are active; WCS refuses to start in a dispatch mode while the other mode has active tasks/legs, and a configuration edited at run time is shown as "restart pending".
- **Follow-up 2 (approved):** `transport_legs.RobotCode` / `AssignedAt` (migration `AddLegRobotCode`, additive, rollback keeps the data in `transport_leg_robot_backup`): each leg stores the AMR the RCS chose for it; a change while the rack is on the AMR is not followed (`LEG_ROBOT_CHANGE_REFUSED`); classification of BP-01…06 and the open decisions about AMR change between legs (CR-028 sections 11-12); vendor questions 9-13.
- **Follow-up 3:** a different AMR reported while the rack is on the AMR is a warning first; the same different AMR in `RobotChangeConfirmChecks` (default 2) consecutive queries sends the Order to `ReconciliationRequired` and updates nothing automatically; RCS "completed" without a confirmed drop completes nothing (`LEG_RCS_COMPLETED_WITHOUT_DROP`); principles for AMR change between legs (CR-028 section 13); `AmrContinuity` deferred to a separate CR.
- **Not done:** real create-task adapter, real leg dispatcher and real callback (vendor questions); hand-over / pinning of the AMR between legs (no approved design).

## v3.17 – CR-027 Dynamic AMR monitoring from the RCS, Phase 1 (2026-11-19) – merged into main

- **RCS fleet read model** (`rcs_amr_snapshots`, `rcs_fleet_state`; additive migration `AddRcsFleetSnapshots`), separate from `agvs`; simulator and real rows stored apart.
- **`queryAgvStatus` client** (RCS-2000 API V3.3, 5.2.14; read-only) and parser; `IRcsClient.QueryFleetAsync` (default not supported); simulator fleet 0..N; `Rcs:Fleet:*` options (default simulator, 5 s). One polling loop (`AgvStatusHostedService`).
- **AGV Monitoring:** fleet panel with any number of AMRs, fresh / stale / missing, last-seen time, VI/EN; `GET /api/agvs/fleet`; realtime `FleetChanged`.
- **Unchanged (verified):** Task lifecycle, `AgvId`, `PER_AGV`, AGV1/AGV2 roles, dispatch, `genAgvSchedulingTask`, PLC, Station, Inventory, Rack binding, Dashboard / KPI / statistics (golden before/after), existing schema blocks (byte-identical). Phase 2 deferred.
- Baseline: `CR-027_BASELINE.md`; RCS API PDF added under `docs/03_RCS_Interface/`.

## v3.16 – CR-026 Configuration Export / Import, Phase 2 (2026-11-19) – feature branch, not merged

- **Maintenance flag** (`system_maintenance`, Admin only, audited, persistent): refuses new Task/Order/PLC requests, holds work that has not started, lets running work finish; race closed by a row lock. API `GET/PUT /api/system/maintenance`; banner for every user.
- **Batch unit of work** (`IUnitOfWork.ExecuteBatchAsync`, `ExecuteSnapshotAsync`): one transaction the existing services join; unchanged behaviour outside a batch.
- **Configuration transfer** (`/api/config-transfer/*`, Admin only, **on by default**; `ConfigTransfer:Enabled=false` is only an emergency switch): Location, AGV (update only), PLC Station (link/enabled, update only), Business Process (header + new Draft). Package format 1.0 (`docs/02_Interface_Specification/CONFIG_PACKAGE_FORMAT.md`), Preview with Create/Update/Unchanged/Skip/Conflict, Preview token bound to package + database state, system-wide active-work gate, verified backup in a persistent folder, one transaction with rollback. Migrations `AddSystemMaintenance`, `AddConfigTransferSessions` (additive).
- Not in this phase: PLC connections, IO points/addresses, RCS settings, history, restore from backup (Phases 3-4). File configuration and user accounts are out of scope by decision.
- No PLC call, no PLC runtime reload, no Publish, no deletion, no secret or approval state in a package.

## v3.15 – CR-025 Modern Industrial UI (2026-11-19)

- **Style layer** (`styles/modern.css`, tokens): cool light background, borderless cards with soft shadows, one 8/12 px radius scale, gradient navy header/sidebar with a blue active marker, KPI cards with tinted icon tile and tone accent, quiet tables (small uppercase headers, soft separators), softer pills, rounded form controls with a blue focus ring, segmented range switch, rounded notices. Both themes keep working (tokens only).
- **Shared components:** `EmptyState` (icon), `ChartEmpty`, `StackedSummary` (stacked status bar + counts/share), `IconInbox` / `IconChart`.
- **Charts from existing data only:** tasks by status (Task Management), AGV status (AGV Monitoring), racks by status (Locations & Stock), PLC connection state, Station address configuration and, for Admin in a real PLC runtime, PLC communication counters (PLC Monitoring). No new API; no statistic that the system does not already hold (the error-trend / history statistics that need new data are NOT added). Empty data shows an explicit empty state.
- CR-023 compact PLC layout kept (only the narrow-card container breakpoint 300 → 250 px, because the wider page padding would otherwise have stacked the PLC card rows); CR-024 bilingual unchanged.
- **Alignment follow-up (CR-025):** login VI/EN switch moved to the card top-right corner on the logo row; Sidebar collapse button row reduced from 40 to 32 px and the header-to-first-group gap from 56 to 38 px. CSS only (`styles/modern.css`).
- No backend, API, database, migration, PLC, RCS, SignalR, authorization or business-logic change. Not deployed.

## v3.14 – CR-024 Bilingual Description of IO signals (2026-11-19)

- The Description column of **IO Address Mapping** (and the same text in the **PLC monitoring IO tab**) now follows the selected language: VI shows the Vietnamese text of the controlled IO workbook, EN an English text; the language switch updates the table immediately, without reload.
- Source: the description is one fixed text per template signal (23 signals, identical for all 4 PLCs / 16 Stations = 368 points) stored once and not editable in the application, so it is translated centrally by Signal Name through the i18n dictionary (`plc.sigDesc_<Signal_Name>`, helper `lib/plcSignals.ts`). A signal without an entry shows the stored text unchanged.
- The monitoring text search now matches the description as displayed (current language).
- No change to database, API, IO catalog / workbook, Signal Name, address, data type, configuration or PLC communication. Not deployed.

## v3.13 – Fix: start-up failure with Auth:CookieSecure=Always (defect of CR-021 batch C) (2026-11-19)

- **Defect:** the start-up configuration warning added by CR-021 batch C read `Auth:CookieSecure` with `GetValue<bool>`, but the setting is the text `Always` | `SameAsRequest` (docker-compose default `Always`): every Production start failed with `FormatException: String 'Always' was not recognized as a valid Boolean`. The tests did not run the start-up summary with a Production configuration, which is why it was not caught.
- **Fix:** `LogSetup.LogStartupSummary` now uses `AuthSetup.ResolveSecurePolicy` (the resolver authentication uses) and logs the resolved policy; an invalid value fails with the same `InvalidOperationException` as authentication (no weaker fallback). The duplicate warning was removed (Program.cs already warns for SameAsRequest in Production) together with its translation. Cookie policy and authentication are unchanged.
- **Tests:** `StartupCookiePolicyTests` (12): valid values, unset defaults, invalid values, one resolver for both, and the real host with `Auth__CookieSecure` Always / SameAsRequest including the Set-Cookie Secure flag. 11 of the 12 fail without the fix.
- No .env, database or volume change. Not deployed.

## v3.12 – CR-023 Compact PLC screens (2026-11-19)

- PLC connection cards (2-column parameters, groups, state in title, one action row), PLC monitoring (compact cards, 16 compact Station tiles in 8 columns on wide screens), responsive rules, four explanation texts removed, icon-only menu on phones. Frontend only; no API/database/PLC change. Not deployed.
- Approved by: project owner. Date: 2026-11-19.

## v3.11 – CR-022 Diagnostics screen: single export (2026-11-19)

- Diagnostics page shows only the whole-system export; PLC-only export area and its frontend helper removed; backend PLC export kept. SignalR upgrades (HTTP 101) no longer counted as slow requests. Test added: PLC module / one PLC / task scope / other scopes and the `PLC/` folder. DEV database migrated (AddCorrelationIds). Not deployed.
- Approved by: project owner. Date: 2026-11-19.

## v3.10 – CR-021 batch D End-to-end verification and performance (2026-11-18)

- `SystemDiagnosticScenarioTests` (2): task reconstruction across modules on the real logging pipeline + RCS simulator + MC mock; 500 000-event export (time, size, memory) and per-source limit. Module classification by event-code prefix (fixes TASK_FINISHED being filed under RCS because its text says "RCS"). PLC folder "Partial" only when PLC events older than its cap exist. Not deployed. CR-021 closed as IMPLEMENTED.
- Approved by: project owner. Date: 2026-11-18.

## v3.9 – CR-021 batch C Missing diagnostic events (2026-11-18)

- RCS call logging (decorator), task/dispatch/cancel/finish decisions, station requests, AGV status changes, inventory, database slow/failed commands, API (D7), SignalR, application lifecycle, unhandled exceptions, startup config warnings. 30 log templates translated (vi/en). The 401/403 request level changed from Information to Warning (D7); the existing test was updated. Not deployed. No migration.
- Tests added: `DiagnosticLoggingTests` (11). Found by them: task completion and queue-cancel did not pass through the finish path (now logged), and the first-task AGV decision is made in ProcessTask, not in the queue pass.
- Approved by: project owner. Date: 2026-11-18.

## v3.8 – CR-021 batch B Correlation and tracing (2026-11-18)

- Migration `AddCorrelationIds` (additive, nullable, indexed) on `transport_tasks` and `transport_orders`. `DiagnosticContext`, log enricher, inbound `X-Correlation-Id`, task/order frames. No business logic changed. Not deployed.
- Tests added: `CorrelationTests` (9). Found by them: an AsyncLocal frame created inside an async helper is lost on return, so every step now enters its frame itself.
- Approved by: project owner. Date: 2026-11-18.

## v3.7 – CR-021 batch A System diagnostic export (2026-11-18)

- Change Request: CR-021 (in progress). Batch A: whole-system diagnostic export as a background job, normalised events, per-source status, masked configuration, Admin UI. No migration; PLC export unchanged; not deployed.
- Tests added: `SystemDiagnosticExportTests` (8). Found and fixed by them: secrets inside nested JSON (audit before/after) escaped past the masking; the Manifest now writes explicit nulls.
- Approved by: project owner. Date: 2026-11-18.

## v3.6 – CR-020 Unified PLC monitoring and operations (2026-11-18)

- Change Request: CR-020. Status: IMPLEMENTED; mock-verified only; not deployed; no migration.
- Added: I/O monitoring tab, `GET /api/plc/monitor/points`, `POST /api/plc/stations/{s}/signals/{sig}/read`, write read-back, `Plc:StaleAfterSeconds`. Changed: IO Address Mapping is configuration only.
- Tests added: `PlcMonitoringTests` (7), `plc-monitor-smoke.ts` (29).
- Approved by: project owner. Date: 2026-11-18.

## v3.5 – CR-019 Simplified PLC write approval (2026-11-18)

- Change Request: CR-019. Status: IMPLEMENTED; not deployed; no real PLC written; no migration.
- Removed PLC_WRITE_APPROVAL_REFERENCE and PLC_WRITE_HANDSHAKE_APPROVED (options, policy, Runtime Info, banner, compose, .env.example, README, plc-write-config.ps1, tests). The Admin per-point write approval is the only approval; all safety guards are unchanged.
- Approved by: project owner. Date: 2026-11-18.

## v3.4 – CR-017 Runtime write configuration (2026-11-18)

- Change Request: CR-017. Status: IMPLEMENTED; not deployed; no real PLC written.
- Fixed: `deployment/.env.example` forced `PLC_MODE=RealReadOnly` and lacked the write keys. Added: `plc-write-config.ps1`, `WritePolicy.MissingWriteKeys`, banner key checklist, RUNTIME_START diagnostics, deployment README section 10.
- Tests added: `PlcWriteConfigBindingTests` (14), `tests/deployment/Test-PlcWriteConfig.ps1` (16), 6 more checks in `plc-write-smoke.ts`.
- Approved by: project owner. Date: 2026-11-18.

## v3.3 – CR-016 PLC write UI readiness (2026-11-18)

- Change Request: CR-016. Status: IMPLEMENTED (code); not deployed; no real PLC written.
- Changed: runtime info names the missing write keys; IO mapping shows a write-status banner and always shows "Ghi PLC" with the concrete reason when disabled; the approval refreshes points and runtime at once.
- Tests added: `plc-write-smoke.ts` (13 checks), extra assertions in `PlcWriteEnablementTests`.
- Approved by: project owner. Date: 2026-11-18.

## v3.2 – CR-015 Enable PLC write (2026-11-18)

- Change Request: CR-015. Status: IMPLEMENTED (code, configuration); not deployed; no real PLC written.
- Changed: the write endpoint is no longer refused in Production; the write dialog is in every build; compose default `PLC_MODE=RealReadWrite` and the write keys are settable (`PLC_WRITE_ENABLED`, `PLC_WRITE_APPROVAL_REFERENCE`, `PLC_WRITE_HANDSHAKE_APPROVED`); every WRITE exchange is kept in the MC history.
- Unchanged: Admin role, per-point write approval, value validation, WritePolicy (all keys needed), safety signals blocked, handshake approval.
- Tests added: `PlcWriteEnablementTests` (4); harness now truncates `plc_exchange_log`.
- Approved by: project owner. Date: 2026-11-18.

## v3.1 – CR-014 PLC write approval and typed write input (2026-11-18)

- Change Request: CR-014. Status: IMPLEMENTED (code); not deployed.
- Added: `WriteApproval` state per IO point, `POST /api/plc/points/{id}/write-approval`, `WriteValueValidator`, IO mapping write-permission column and dialogs, migration `AddPlcWriteApproval`.
- Changed: write permission no longer needs the read approval; BOOL write values accept true/false only.
- Tests added: `PlcWriteApprovalTests` (3 + real MC mock write path), `WriteValueValidatorTests`.
- Approved by: project owner. Date: 2026-11-18.

## v3.0 – CR-013 PLC Diagnostic Logging and Export (2026-11-18)

- Change Request: CR-013. Status: IMPLEMENTED (code); not deployed, not verified against a real PLC.
- Added: standard diagnostic codes/stages, `plc_exchange_log` (300 per PLC), configuration snapshot, Admin ZIP export API and System > Diagnostics page; migration `AddPlcDiagnostics`.
- Changed: instrumentation only in `PlcWorker`, `PlcRuntime`, `McTcpClient`, `McPlcEndpoint`.
- Tests added: `PlcDiagnosticsTests` (25), `PlcDiagnosticsIntegrationTests` (11).
- Approved by: project owner. Date: 2026-11-18.

## v2.9 – CR-012 Pollable without approval (2026-11-18)

- Change Request: CR-012. Status: IMPLEMENTED (code); not deployed.
- Changed: `StationIoPoint.IsPollable` no longer needs `Approved`; `PlcPollablePolicy` (existing `AddressValidator`) decides in the runtime and the IO list; bulk-approval API marked deprecated (`Deprecation: true`), its UI removed.
- Unchanged: write permission (still needs approval), WritePolicy, handshake approval, connection guards, MC Protocol, no migration.
- Tests added/updated: `PlcPollingPolicyTests` (5); updated `McAddressTests`-area test, `PlcMcIntegrationTests`, `PlcCr004Tests`.
- Approved by: project owner. Date: 2026-11-18.

## v2.8 – CR-011 Bulk READ approval of the PLC under commissioning (2026-11-18)

- Change Request: CR-011. Status: IMPLEMENTED (code); not yet executed on the deployment database.
- Added: Admin-only `GET/POST /api/plc/connections/{plcId}/bulk-approval`, dialog on the IO mapping page, audit action `PLC_IO_BULK_APPROVE`, configuration `Plc:BulkApproval:AllowedPlcIds` (default PLC-04).
- Changed: none. The existing single-point approval, `IsPollable` and the PLC driver are unchanged; no migration.
- Tests added: `PlcBulkApprovalTests` (7).
- Approved by: project owner. Date: 2026-11-18.

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
- Files: CHANGE_REQUESTS.md, CHANGELOG.md, ADR-0007, CR-008A report, `docs/archive/reviews/CR008B-1_DOCUMENTATION_CORRECTION.md`.
- Approved by: project owner (2026-11-17).

## v2.2 – CR-008A WCS dispatch and scheduler reliability (2026-11-16)

- Change Request: CR-008A (AUDIT-001, 002, 003, 004, 006 of `docs/archive/audits-2026-11-16/ETEK_WCS_FULL_CODE_REVIEW_2026-11-16.md`). Status: IMPLEMENTED on `develop`; not merged.
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
- See decisions/ADR/ADR-0004 and archive/status/TESTING_REPORT_CR-002.md.

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

## v2.8 – CR-010: Configurable Business Process, Route Engine, multi-leg Transport Task and per-port Station Transaction (2026-11-18)

- Change Request: CR-010 (IMPLEMENTED – PARTIAL).
- Status: scope approved by the project owner instruction of 2026-11-18; PLC live signalling and a real RCS leg adapter remain open.
- Source: owner instruction of 2026-11-18; `Process in detail.pptx`, `ETEK Process AGV Phase 2 12.08.26.pptx`, `AGV_PLC_IO_Interface_List_10_11_2026.xlsx`, `WCS_BP_Port_PLC_Transaction_Matrix.xlsx` (working reference).
- Added: Transport Task with Legs; Station Transaction per port (one active per port, database-enforced); Business Process / Route Step / Candidate / condition configuration with versions (Draft, Validated, Published, Disabled), publish validation and snapshots on every task; Route Engine; leg-level PER_AGV; inventory updates for legs and buffers; PLC disconnect/restart recovery; PLC provisioning (any number of PLCs, independent copy of the default IO template per port); Admin API and UI; reference process drafts; migration `AddRoutingEngine`; ADR-0008.
- Changed: `LocationType` gained WaitingPosition, PickupLocation, DeliveryLocation (additive); audit query gained an optional action prefix; `PlcConfigService.ToView` made public. No existing behaviour was changed.
- Deprecated: none.
- Specification files changed: `decisions/ADR/ADR-0008` (new), `specification/business-rules.md` (addendum 1.9), `specification/process-flows.md` (addendum 2.4), `specification/integration-requirements.md` (addendum), PLC specification drafts (statuses).
- Software components affected: Domain (Routing), Application (Routing, Plc provisioning), Infrastructure (repository, simulator leg dispatcher, template provider), Runtime (RoutingHostedService), API (RoutingEndpoints), Frontend (Routing, Processes, PLC provisioning). Not touched: TaskStateMachine, TransportTask, InventoryService, TaskOrchestrator, FlowOptions, Scheduler, RCS client, PLC worker/driver.
- Tests added/updated: 50 CR-010 tests (domain, integration, API); existing suites unchanged.
- Approved by: Project owner (implementation instruction).
- Date: 2026-11-18.

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
