# WCS Work Log – Session Handover

**Last updated:** 2026-11-06 (UI redesign – section 8; PLC/RCS configuration module – section 9). Sections 1-7 describe the state at the end of 2026-11-05.  
**Date of first hand-over:** 2026-11-05  
**Baseline:** WCS Specification v1.3 (unchanged – no file under `docs/specification/` was modified)  
**Purpose:** hand-over so the next session can continue without re-deriving context. Read `CLAUDE.md`, `README.md`, `docs/README.md`
and `docs/decisions/ADR/ADR-0002-phase1-technical-decisions.md` first.

---

## 1. Summary of completed work

| # | Work | State |
|---|---|---|
| 1 | Project analysis (scaffold, docs, backend/frontend structure) | Done |
| 2 | Step 0 – solution normalised (`WCS.slnx` removed, `WCS.sln` only), Git initialised, `WCS.Tests` (xUnit) created, `appsettings.example.json` | Done |
| 3 | Phase 1 – Domain model (AGV, Location, Rack, Reservation, InventoryTransaction, TransportTask, TaskHistory, 13-state machine) | Done, compiled, unit-tested |
| 4 | Phase 1 – Persistence (EF Core 10 + Npgsql, `WcsDbContext`, configurations, repositories, unit of work with retry, migration `InitialCreate`) | Code done; **migration generated but NOT applied** |
| 5 | Phase 1 – Application services (task orchestrator, PER_AGV scheduler, inventory/reservation/reconciliation, station-request intake, AGV status refresh, queries) | Done, compiled; **integration tests written but never executed** |
| 6 | Phase 1 – Adapters/simulators (`IPlcAdapter`, `IRcsClient`, `IStationSignalService` + in-memory simulators, `SimulationDriver`) | Done |
| 7 | Phase 1 – API (minimal API, unified response envelope, validation, exception mapping, Serilog, SignalR hub, health, simulator endpoints) | Done, compiled; **never run against a database** |
| 8 | Phase 1 – Runtime hosted services (scheduler loop, AGV status loop, seed) | Done |
| 9 | Phase 1 – Frontend foundation (routing, API client, SignalR provider, Dashboard / AGV / Task / Location-Inventory pages, loading/error/empty states) | Done, build + lint clean |
| 10 | NuGet advisories NU1902/NU1903 (26 warnings) | **Resolved** by patch updates (EF Design/Relational 10.0.14, Npgsql EF 10.0.3); audit not disabled |
| 11 | Frontend Demo Mode (`VITE_DEMO_MODE=true`) | **Done** (see section 4) |
| 12 | ADR-0002 recording Phase 1 technical decisions and `NEEDS_CONFIRMATION` items | Done (new file only) |

Protected rules were respected: PER_AGV, WCS-owned inventory, BP-04 stays REVIEW (not implemented; a `Bp04` config key makes startup fail),
A3/A4 and D1/D2 untouched, single PLC connection, no bind at documented "Không bind rack" points.

## 2. Git

Branch `main`. Latest commits (newest first):

```
7fd0059 2026-11-05 21:04 feat(frontend): add isolated demo mode
53f56b9 2026-11-05 20:58 chore: seed of dev placeholder locations is opt-in (Seed__Enabled=true)
def970d 2026-11-05 20:53 feat(phase1): frontend foundation (routing, API client, SignalR) and ADR-0002
a6b70e6 2026-11-05 20:48 feat(phase1): domain model, persistence, task orchestration, inventory, scheduler, simulators, API and tests
37f561b 2026-11-05 20:29 chore: add test foundation and example config
b6bb2ce 2026-11-05 20:23 baseline: WCS project scaffold before implementation
```

Uncommitted changes: **none** apart from this file (`docs/WORK_LOG.md`, new). Working tree was clean at `7fd0059` before it was created.
Git prints "LF will be replaced by CRLF" warnings on Windows; harmless but a `.gitattributes` would silence them (not added).

## 3. Verified vs. NOT verified

Verified (actually executed):
- `dotnet build WCS.sln`: 0 errors, 0 warnings.
- 41 xUnit tests that need no database (state machine, domain/inventory rules, flow catalog) – pass.
- `dotnet ef migrations script`: only `CREATE` statements; PER_AGV partial unique index and rack-lock index present.
- Frontend `npm run build`, `npm run build:demo`, `npm run lint`: clean.
- Demo Mode: 12-point engine script passed; all 4 pages + 404 rendered in headless Chrome with the backend down; real mode with backend
  down shows an error card with Retry (no blank page).

**NOT verified – no database password was available:**
- Migration has **not** been applied to `wcs_db`.
- The ~25 PostgreSQL integration tests (`tests/WCS.Tests/Integration/*`: concurrency, BP-01/02/03/05/06, idempotency, RCS faults, cancel,
  reconciliation, timeouts) have **never run**. They may expose logic bugs; do not claim Phase 1 "tested" until they pass.
- The API has never been started with a database: endpoints, validation, exception mapping, SignalR events and `/api/system/health`
  are untested at runtime.
- Real mode of the frontend against a live backend has not been seen (only the error state).

## 4. Demo Mode – COMPLETE

- Enable: `npm run dev:demo` (or `$env:VITE_DEMO_MODE="true"; npm run dev`). Build: `npm run build:demo`. Documented in `README.md`.
- In-browser engine (`src/frontend/wcs-web/src/demo/engine.ts`) + `demoApi.ts` + floating `DemoPanel.tsx`; orange banner
  "DEMO – DỮ LIỆU GIẢ". No request to backend/PostgreSQL (and `main.tsx` blocks `/api` / `/hubs` fetches in demo). SignalR is not opened.
- Scenarios: BP-01, BP-01 without destination (WAITING_FOR_DESTINATION), BP-02, BP-03, BP-05, BP-06; step/auto-run; interrupt, cancel,
  AGV disconnect, reconcile, reset.
- Limits: data lost on refresh; rules are a hand copy of the backend rules (can drift); not a substitute for the PostgreSQL tests;
  capacities and starting stock are illustrative.
- `VITE_DEMO_MODE` unset/`false` leaves real behaviour unchanged.

## 5. Open problems and risks

1. **Blocked on PostgreSQL password.** The user must provide it via User Secrets (preferred) or `WCS_DB_PASSWORD`:
   `cd src/backend/WCS.Api; dotnet user-secrets set "Database:Password" "<password>"`. The test project shares the same `UserSecretsId`.
   Without it the API process starts but every call returns 500, and DB tests fail.
2. Port **5173** was occupied by an earlier Vite process (node PID 8580 at the time) – stop it or use the already-running one.
3. `dotnet ef` tool is 10.0.0 while the runtime/packages are 10.0.14 (warning only; `dotnet tool update -g dotnet-ef` fixes it).
4. `npm audit`: moderate `minimatch` advisory inside the ESLint toolchain (dev-only). `npm audit fix --force` would downgrade ESLint
   (breaking) – deliberately not run.
5. Seeding: AGV1/AGV2 rows are always created on API start (defined by the spec). The nine placeholder locations are seeded only with
   `Seed__Enabled=true` (default off, per "no fake data in PostgreSQL"). Location capacities are placeholders, not business data.
6. No authentication/authorisation (not requested; design does not prevent adding it). Simulator endpoints exist only in
   Development (`Simulator:ExposeApi`).
7. No RCS callback endpoint, no real PLC/RCS adapters (intentional – nothing confirmed). `Integration:Mode` other than `Simulator`
   throws at startup.
8. WCS does not know which rack sits at machine stations A1/A2/B1/B2 unless stock is declared first; otherwise a request fails with
   `NO_SOURCE_AVAILABLE` (strict reading of "WCS owns inventory").

### NEEDS_CONFIRMATION (no business decision taken – see ADR-0002)
- Rack model: individual Rack ID vs. quantity per type (implemented neutrally: surrogate id + optional `ExternalRackId`).
- `continueTask` timing: BP-01 step 8 (right after pick) vs. `task-state-machine.md` 3.6 (after destination reserved) – 3.6 implemented.
- Priority policy for sources/destinations/queue (config order = document order; queue = FIFO) – not approved.
- Location capacities; whether `NoSourceBehavior` should be `Fail` (default) or `Wait`.
- PLC: addresses, handshake, timeouts, Load/Unload direction semantics. RCS: URL/auth/version, robot codes AGV1/AGV2, templates, `continueTask`
  support, callback contract, status-code mapping.
- Legacy A3/A4 and D1/D2 process documents; BP-04 (slide 8) confirmation; F5 branch that may cancel an AGV1 task (BP-05 note).
- Meaning of "bind rack" in the legacy process.

## 6. Next tasks (in order)

1. **Get the DB password set** (see 5.1), then:
   - `cd src/backend/WCS.Api; dotnet ef database update --project ..\WCS.Infrastructure --startup-project .` (additive; creates tables only;
     `wcs_db` must already exist).
   - Verify tables/indexes with `psql` (`\d transport_tasks`, check `ux_transport_tasks_one_active_per_agv`).
2. Run the full suite: `dotnet test src/backend/WCS.sln`. Fix any failures **in code, not by weakening tests**; re-run until green.
   Expect possible issues around EF tracking/retry in `EfUnitOfWork`, queue/dispatch ordering, and the parallel-creation test.
3. Run the API (`dotnet run --launch-profile http` in `WCS.Api`, optionally `$env:Seed__Enabled="true"`) and the frontend (`npm run dev`) in two
   terminals; exercise Dashboard, AGV, Task, Inventory pages against the live API; check `GET /api/system/health`, SignalR events
   (`TaskCreated`, `TaskStateChanged`, `InventoryChanged`, `AgvStatusChanged`), and the simulator endpoints.
4. Add API-level tests (e.g. `WebApplicationFactory`, with `Runtime:EnableWorkers=false`) for the response envelope, validation (400),
   BP-04 rejection (`FLOW_IN_REVIEW`), conflict (409).
5. Update `CHANGELOG`, `IMPACT_MATRIX`, `REQUIREMENT_TRACEABILITY` (test references TST-CONC-001, TST-INV-*, TST-BP0x-*) once tests are green –
   these are change-management records, not specification; do it per CLAUDE.md section 3 step 7.
6. Take the `NEEDS_CONFIRMATION` list (section 5) to the project owner; open Change Requests (`docs/change-management/CHANGE_REQUESTS.md`)
   for anything that would alter approved behaviour. Do not implement BP-04, A3/A4, D1/D2.
7. Later phases (need confirmed inputs): real PLC adapter (MC protocol 3E/binary), real RCS client + callback endpoint, authentication/roles,
   PDA screens for initial stock, `.gitattributes`, CI.

## 7. Quick reference

```powershell
# Backend
cd "H:\TUAN.ND\DCA\2026\7. FFC AMR\WCS\src\backend\WCS.Api"; dotnet run --launch-profile http     # http://localhost:5007
# Frontend (real)   -> http://localhost:5173, proxies /api and /hubs to :5007 (VITE_DEV_API_TARGET)
cd "H:\TUAN.ND\DCA\2026\7. FFC AMR\WCS\src\frontend\wcs-web"; npm run dev
# Frontend (demo, no backend/DB needed)
npm run dev:demo
# Tests / build
cd "H:\TUAN.ND\DCA\2026\7. FFC AMR\WCS\src\backend"; dotnet build WCS.sln; dotnet test WCS.sln
```

Key locations: `src/backend/WCS.Domain` (entities, state machine) · `WCS.Application` (orchestrator, scheduler, inventory, flow rules, adapter
interfaces) · `WCS.Infrastructure` (EF, migrations in `Persistence/Migrations`, simulators) · `WCS.Runtime` (hosted services) ·
`WCS.Api` (endpoints, config; flow rules in `appsettings.json` → `ProcessFlows`) · `tests/WCS.Tests` · `src/frontend/wcs-web`.

---

## 8. Update 2026-11-06 – Full UI redesign (Vietnamese, industrial WCS look)

Scope: **frontend only**. No change to Backend code, database, PLC/RCS adapters or the state machine; no change to `docs/specification/`.
(The only non-frontend edit: the *placeholder* location names in `appsettings.Development.json` were translated to Vietnamese; the seed stays opt-in.)

Delivered:
- **Design system** (`src/styles/tokens.css`, `base.css`, `layout.css`, `pages.css`; components in `src/components/ui.tsx`): navy / white / light-grey palette;
  green = normal, amber = warning/waiting, red = error/stopped; larger type for operator screens. Shared pieces: `PageHeader`, `Panel`, `Kpi`,
  status pills (`TaskStatePill`, `ConnectionPill`, `OccupancyPill`, `RackPill`), `Button`, `Async` (loading/error/empty), `FlowStepper`; inline SVG
  icons in `icons.tsx` (no new packages).
- **One layout** for all pages: header (ETEK WCS, optional area name from `VITE_SITE_NAME`, PLC status, RCS status, live clock/date), sidebar with
  icons and active state, content area and – demo only – a bottom simulation dock.
- **100 % Vietnamese UI** through `src/i18n/vi.ts` (state/role/occupancy/rack/flow/event/error vocabularies, `reasonVi()` for backend/simulator free
  text, `describeError()`); technical codes (AGV1, BP-01, PLC, RCS, API, Rack ID, backend state names) stay unchanged in the data layer. Dates use vi-VN.
- **Tổng quan**: 6 KPIs, AGV supervision cards, task bar chart (grouped states), inventory overview with per-location fill bars, latest activity log
  (tasks + inventory transactions).
- **Giám sát AGV**: AGV1/AGV2 cards with role, RCS connection, activity, current task, route, 5-step flow stepper, last activity, recent tasks.
  *No AGV position is shown – the API has no position data; the card says so.*
- **Quản lý nhiệm vụ**: table (code, created, flow, AGV, from, to, state, actions), filters (search/AGV/state), detail pane (general info, AGV, flow,
  actions incl. cancel with mandatory reason, state history, PLC/RCS events). The newest task is auto-selected so the pane is never empty.
- **Vị trí và tồn kho**: schematic grouped by data (location type / accepted rack kind, not by hard-coded codes) with slot squares
  (full / empty / reserved / free / unknown), legend, location detail pane, rack table, transaction history. Rack model untouched
  (Rack ID shown only when present, otherwise "Chưa gán Rack ID").
- **Demo**: the orange banner and the floating panel are gone. A small header chip "Chế độ mô phỏng" plus a **bottom simulation dock** (a layout row,
  never overlays content): Bắt đầu / Tạm dừng / Chạy từng bước / tốc độ / Khởi tạo lại, and an expandable "Tình huống và sự cố" with BP-01,
  BP-01 không có vị trí đích, BP-02, BP-03, BP-05, BP-06, gián đoạn, hủy, đối soát, ngắt/nối AGV, giải phóng vị trí F1. BP-04 is not simulated.
- Bug found and fixed during testing: pausing auto-run did not refresh the dock (store version was not bumped in `stopAutoplay`).

Verified (executed): `npm run lint` clean; `npm run build` and `npm run build:demo` clean; headless-Chrome screenshots of all 4 pages at 1920x1080
and 1366x768; a Chrome DevTools Protocol script drove the demo UI (20 checks: pages render, no demo banner, all five BP scenarios present and BP-04
absent, step, auto-run, pause, interrupt → Thất bại, reconcile, reset, cancel needs a reason, location details); the engine script (12 checks) still
passes; real mode with the backend down shows Vietnamese error cards with "Thử lại", header PLC/RCS = "Không có dữ liệu", KPIs "—".

Not verified: real mode against a live backend + PostgreSQL (still blocked on the DB password, see section 6); only 1920x1080 and 1366x768 were
checked, only in Chrome.

Known limits / follow-ups:
- The RCS header status is **derived** (simulator → "Mô phỏng"; otherwise from AGV statuses) because there is no RCS health endpoint yet.
- Dashboard task counts use the latest 200 tasks (completed/failed totals use separate count queries).
- The demo engine still duplicates backend rules by hand (may drift).
- The NEEDS_CONFIRMATION list (section 5) is unchanged – the redesign resolved none of it.
- Next step is still section 6 (DB password → migration → full test run → run API + frontend together).

---

## 9. Update 2026-11-06 – System configuration module (PLC / RCS connections, operational parameters)

New menu **Cấu hình hệ thống** (`/config`) with 3 tabs: **Kết nối PLC**, **Kết nối RCS**, **Tham số vận hành**. All Vietnamese, same design system.
State machine, BP flows, scheduler logic and adapters' behaviour are **unchanged**; no new business rule was introduced.

### Backend (Domain → Application → Infrastructure → Api)
- `ConnectionSetting` entity (table `connection_settings`, one row per `Plc` / `Rcs`): non-secret fields as JSON, secrets only as an **encrypted blob**
  (ASP.NET Core Data Protection, purpose `ETEK.WCS.ConnectionSecrets.v1`), last-check result, `UpdatedAt/By`, xmin concurrency token.
  **Migration `AddConnectionSettings`** (generated, NOT applied: still no DB password) also adds a unique filtered index on `agvs.RcsRobotCode`.
- `ConnectionConfigService` (config only; it never drives a device): get / save / safe check. Secrets are write-only (`null` = keep, value = replace,
  `Clear*` = remove), dropped when the auth mode no longer uses them, never returned (GET exposes only `hasPassword` / `hasToken`), never logged.
  Validation happens before anything is changed, so a rejected save leaves the stored configuration untouched.
- `ConnectionValidator`: strict IPv4/IPv6 (rejects shorthand such as `192.168.1`), port 1-65535, station numbers 0-255, timeouts, base-URL rules
  (http/https, no credentials in the URL, host/port must match the fields). Enabling requires a complete configuration; a disabled one may be saved partially.
- `OperationalConfigService`: read-only view of policy / scheduler / flows (from deployment config) + editable **AGV RCS robot code & enabled**
  (unique code; an AGV with an active task cannot be disabled) and **locations** (create/edit; capacity cannot drop below stored + reserved racks; a
  location with a reservation cannot be disabled).
- Safe connectivity check (`IConnectionProbe`): default **`SimulatedConnectionProbe` – no network at all**. Only with `Integration:AllowNetworkProbe=true`
  the `TcpReachabilityProbe` opens a TCP connection to host:port and closes it **without sending a byte** (no MC Protocol frame, no HTTP request, no
  command). The check always uses the **saved** configuration (never half-edited form values) and is skipped (`NotExecuted`) when nothing is saved, the
  connection is switched off, or the saved data is incomplete.
- API (all under the unified response envelope; 400 returns `error.details = [{field,message}]`):
  `GET|PUT /api/config/plc`, `POST /api/config/plc/test`, `GET|PUT /api/config/rcs`, `POST /api/config/rcs/test`, `GET /api/config/operations`,
  `PUT /api/config/agvs/{id}`, `POST /api/config/locations`, `PUT /api/config/locations/{code}`.
- Config files: `Integration:AllowNetworkProbe` (default false), optional `DataProtection:KeysPath`; `appsettings.example.json` updated (the old unused
  `Plc`/`Rcs` sections were removed – connection settings now live in the database).

### Frontend
- `ConfigPage` + `PlcTab`, `RcsTab`, `OperationsTab`: dirty tracking, "Hoàn tác", per-field errors (client mirror of the backend validator **and** server
  details), write-only password/token inputs, "Xóa mật khẩu/token đã lưu", last-check panel (outcome, method, message, latency, time, last error), test
  button disabled until the form is saved and clean. Operations tab: policy/scheduler/flows read-only with "Chưa được xác nhận" badges where applicable,
  editable robot codes and locations.
- **Demo Mode** has its own in-memory configuration store (`demo/demoConfig.ts`): saving/testing never touches the network; checks are labelled
  "MÔ PHỎNG (chế độ Demo)". "Khởi tạo lại" also resets the demo configuration.

### Verified (executed)
- `dotnet build WCS.sln`: 0 errors, 0 warnings; no vulnerable packages.
- **101 xUnit tests without a database pass** (new: validator, config service with fakes, secrets/logging, probes incl. loopback TCP, Data Protection
  round trip, and **HTTP-level API tests with `WebApplicationFactory`**: empty GET is un-prefilled, secrets never in any response, 400 with field details,
  check never calls the probe when nothing is saved, default container wiring uses the simulated probe).
  A real bug found by the tests and fixed: `IPAddress.TryParse` accepts `192.168.1`; validation is now strict.
- `npm run lint`, `build`, `build:demo`: clean. A Chrome DevTools script drove the Demo UI: **30 checks pass** (menu/tabs, nothing pre-filled, field errors,
  incomplete enable refused, save/test flow, secret never present in text/inputs/DOM, Base URL mismatch, duplicate robot code, add location, no stray
  English, no `/api` or `/hubs` request in Demo Mode).
- `dotnet ef migrations script`: new migration only `CREATE TABLE` + `CREATE INDEX` (no DROP of existing data).

### NOT verified / limits
- Still blocked on the PostgreSQL password: migration `AddConnectionSettings` not applied; `ConfigIntegrationTests` (real DB: encrypted blob in the table,
  unique robot code, capacity guards) and all earlier integration tests have **never run**; the config API was exercised over HTTP only with in-memory fakes.
- The `Enabled` switch is **stored only**: there are no real PLC/RCS adapters yet, so nothing connects because of it.
- **No authentication/authorization exists**: the config endpoints (which accept secrets) are open. Add auth before any non-local deployment.
- Data Protection key ring: by default stored in the Windows user profile of the account running the API. If it is lost, saved passwords/tokens become
  unreadable and must be re-entered (the UI then shows "Chưa có mật khẩu"). Set `DataProtection:KeysPath` to a backed-up folder outside the repository.
- Updating a configuration (not just the check) relies on the xmin token; concurrent edits by two operators end with a conflict, not a silent overwrite.
- Scheduler / policy / timeouts are **read-only in the UI** (no approved runtime-change mechanism); they are edited in `appsettings.json`.

### To confirm with the PLC engineer (Mitsubishi MELSEC-Q, QJ71E71-100)
IP and TCP port of the Ethernet module · protocol/frame/data code (spec says "expected": MC Protocol, 3E frame, binary, TCP) · whether **Network number,
PC number and "PLC station number"** (i.e. request-destination module I/O / station) are needed by the final adapter and their values · connection timeout ·
polling interval · device/address mapping and handshake (not part of this form yet) · whether one connection is kept open or opened per request.

### To confirm with the RCS (HIK RCS-2000) team
Host and port (HTTP or HTTPS), **Base URL / context path** · **authentication model** (none / token / user+password / other, header or body, token lifetime) ·
exact endpoints and payloads of `genAgvSchedulingTask`, `continueTask`, `cancelTask`, `queryTaskStatus`, `queryAgvStatus` (none were assumed) ·
callback URL, authentication and retry behaviour (not in this form yet) · robot codes of AGV1/AGV2 (enter them on the Operations tab) · workstation codes
and task-template codes (not modelled yet) · whether a TCP-open check is meaningful for the deployed RCS or whether it needs a dedicated health call.

### Next steps for this module
1. Provide the DB password → apply both migrations → run the whole suite (including `ConfigIntegrationTests`).
2. Run the API and exercise `/api/config/*` against PostgreSQL (check the stored blob is not readable text).
3. Once PLC/RCS parameters are confirmed: finalise the field list, then write the real adapters behind `IPlcAdapter` / `IRcsClient` (not before).
4. Add authentication and role checks for `/api/config/*`.

---

## 10. Update 2026-11-06 – First run against PostgreSQL (migrations, full suite, real API)

Executed with the password from .NET User Secrets (`Database:Password`); it was never printed and does not appear in the API log.

- **Database**: PostgreSQL 16.15, `wcs_db` was empty (no tables, no `__EFMigrationsHistory`). Both migrations were applied in order:
  `20261105134238_InitialCreate`, `20261106050711_AddConnectionSettings`. No data was dropped or changed by them.
  On first start the API's base seeder created the AGV1/AGV2 rows in `wcs_db` (2 rows; locations/racks/tasks/connection_settings are empty).
- **Bugs found only by running against PostgreSQL (fixed, no test weakened)**
  1. EF treated new child rows with an application-assigned Guid (e.g. task history) as *Modified* -> UPDATE hit 0 rows -> 38 integration tests failed with
     "Operation failed after repeated concurrent modification". Fix: `ValueGeneratedNever()` on all Guid keys (no schema/model-snapshot change).
  2. `continueTask` retry after an unknown (timeout) outcome did not query RCS first. Fix: `SendContinueAsync` now queries the RCS task status before
     any retry (task-state-machine.md 3.5).
  3. Test `Queue_is_fifo...` was flaky: the frozen test clock gave all tasks the same `CreatedAt`, so the Guid tie-breaker decided the order. The test now
     advances the test clock between creations. Note: production ties on `CreatedAt` would also fall back to Guid order (very unlikely, still unapproved FIFO).
- **Tests**: 153 total, 153 passed, 0 failed, 0 skipped (5 consecutive full runs). Integration tests use a throw-away database `wcs_test_<guid>` that is dropped
  afterwards; `wcs_db` is not touched by tests.
- **Real API** (`http://localhost:5007`, Development): GET/PUT/test for `/api/config/plc|rcs` and GET `/api/config/operations` verified. Write calls were run
  against a scratch database (`wcs_api_check`, since dropped) to keep `wcs_db` clean. Secret never in responses or logs; check result is "Simulated".
- Still open: the real-PostgreSQL run of the frontend against this API, authentication on `/api/config/*`, items listed in sections 6 and 9.

---

## 11. Update 2026-11-06 – Authentication, authorization, accounts, audit (CR-001)

Everything below is **committed but NOT applied to `wcs_db`**: run `dotnet ef database update --project WCS.Infrastructure --startup-project WCS.Api` (from `src/backend`) after reviewing the migration `AddAuthentication` (CREATE TABLE app_users / audit_log + indexes only; Down() drops only those two new tables).

### Architecture
- **No ASP.NET Identity user store was present**, so no existing component was replaced. Accounts are a small `AppUser` entity (`app_users`) with the ASP.NET Core Identity `PasswordHasher` (PBKDF2, per-password salt, format v3). Passwords are never stored or logged in clear.
- **Session**: cookie authentication (`wcs.auth`, HttpOnly, SameSite=Strict, `Secure` outside Development, sliding idle timeout 30 min, absolute 12 h; `Auth:*` config). On **every request** the account is re-read from the database: a disabled account, a role change or a password reset (security stamp) ends the session immediately.
- **Authorization** is enforced in the backend: fallback policy = authenticated; policies `Operations` (Admin or Operator) for the whole `/api` group and the SignalR hub; `AdminOnly` for configuration, simulator, accounts, audit, stock changes, task cancel/reconcile. The frontend only hides what the API would refuse anyway.
- **Request guards**: state-changing `/api` calls must carry `X-WCS-Client` (cross-site forms cannot add it); a session flagged "must change password" can only use `/api/auth/*`.
- **Lockout**: 5 failed sign-ins lock the account for 5 minutes; unknown user / wrong password / disabled account return the same message (a dummy hash is computed for unknown names).
- **Audit**: table `audit_log` (actor, role, action, target, result Success/Denied/Failed, detail, before/after JSON, IP). Written through its own DbContext; secret-looking string fields are blanked before storing; config changes record only the fact "password replaced", never the value.

### Protected API
| Area | Admin | Operator |
|---|---|---|
| `POST /api/auth/login` | anonymous | anonymous |
| `/api/auth/logout, me, change-password` | yes | yes |
| `GET /api/agvs, locations, inventory/*, tasks*, flows, system/health` | yes | yes |
| `POST /api/tasks` | yes | yes (requester taken from the session) |
| `PUT/DELETE /api/tasks/{id}` | waiting tasks only | waiting tasks only (403 `TASK_LOCKED` otherwise) |
| `POST /api/tasks/{id}/cancel, /reconcile` | yes | 403 |
| `POST /api/inventory/initial-stock, adjustments` | yes | 403 |
| `/api/config/*`, `/api/simulator/*`, `/api/users*`, `/api/audit` | yes | 403 |
| `/hubs/wcs` (SignalR) | yes | yes (same cookie) |

Task edit/delete: the permission and the state are checked **inside the transaction on the freshly loaded task** (xmin concurrency + retry). The state the screen showed (`expectedState`) is only a hint: mismatch gives 409 `TASK_STATE_CHANGED` ("reload"); it can never grant access.

### First administrator / creating operators
- No built-in account and no default password. When **no active Admin exists** at API start, the bootstrapper creates one from configuration: `Bootstrap:Admin:Username` and `Bootstrap:Admin:Password` (user-secrets or env `Bootstrap__Admin__Username/Password`; password must satisfy the policy). It is flagged "must change password" and is audited (`USER_BOOTSTRAP_ADMIN`). Once an Admin exists the setting is ignored; remove it afterwards.
- Admin creates Operators in the UI (`Quản lý tài khoản`) or `POST /api/users`; they must change the initial password at first sign-in.

### Verified (executed)
- Backend build 0 warnings/0 errors, no vulnerable packages. **213 tests, 213 passed, 0 skipped** (3 consecutive full runs; concurrency tests stress-run 40x without failure). New: 60 API tests with in-memory fakes through the real cookie pipeline (401/403 matrix, lockout, idle + absolute expiry via a fake TimeProvider, disabled account / role change ends the session, forced password change, last-admin and self-protection rules, audit without secrets) and 12 PostgreSQL tests (Operator create/edit/move/delete, locked after AGV accepted, forged `expectedState`, delete-vs-dispatch race, two admins demoting each other, hashed passwords + audit rows in the real DB, bootstrap).
- Frontend: `npm run lint`, `build`, `build:demo` clean. Headless-Chrome checks: **Demo Mode 31/31**, **real mode (API + PostgreSQL scratch DB + Vite proxy) 28/28**: bootstrap admin forced to change password, cookie HttpOnly/SameSite=Strict, SignalR connects with the cookie, operator sees only 4 menus, direct URLs to admin pages redirect with a message, admin APIs return 403 for the operator, audit page shows entries and never a password.
- Direct SQL check on the scratch DB: hashes are `AQAAAAIAAYag…` (Identity v3), 0 audit rows contain any password, API log contains no password.

### Bugs found while doing this (fixed)
- `Location.Touch` only changed a timestamp: two concurrent allocations of the last slot sharing the same clock reading issued **no UPDATE**, so the xmin conflict that serializes them never fired (seen as 3/40 failures of `Last_free_destination_slot...`; harmless with the real clock but fragile). `Touch` now always changes the value; 0/40 failures afterwards.
- Test arrangement: the harness and the API host each own a different RCS simulator instance; tests that drive tasks through the API now use the API host's simulator.

### Not done / open (see CR-001 for the decision list)
- `wcs_db` migration `AddAuthentication` not applied (production rule: never automatic).
- Interpretations to confirm: what "edit task" means (only the requesting station today), "delete" = soft delete to Cancelled, Operator read-only inventory, Admin handling of failed/cancelled tasks, password/lockout/session defaults.
- Security items still open: TLS/HTTPS termination and `Secure` cookie in production, backup of the Data Protection key ring, no MFA, no e-mail password reset, no per-IP rate limiting, `X-WCS-Client` is defence in depth (not a secret).
- Demo Mode has its own in-browser demo accounts (`demo-admin`, `demo-operator`, no passwords). They exist only in `demo/demoAuth.ts`, are reachable only when `VITE_DEMO_MODE=true`, and never touch the backend.
