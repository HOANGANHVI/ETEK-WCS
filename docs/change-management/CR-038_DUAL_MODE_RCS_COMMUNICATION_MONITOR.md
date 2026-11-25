# CR-038 – Dual mode Simulator / Real AGV and RCS Communication Monitor

**Status:** IMPLEMENTED IN SOURCE, NOT DEPLOYED, NOT VERIFIED ON A REAL RCS/AGV. Requested by the project owner as an implementation task (2026-11-25). Real dispatch is verified only against the CR-030 mock RCS server.

## 1. What changes
| Area | Before | After |
|---|---|---|
| Choosing the dispatcher | At start-up by DI (`Rcs:RealDispatch:Enabled`) | **At run time** by a persisted, audited mode (Simulator / Real), pinned on every task and order when it is created |
| `IRcsClient` | `RcsSimulator` or `HikRcsClient`, one for the process | `RoutedRcsClient`: asks the resolver which mode the task was created in and hands the call to the matching adapter; the real HTTP code stays only in `HikRcsClient` |
| `ILegDispatcher` | `SimulatedLegDispatcher` or `RealLegDispatcher` | `RoutedLegDispatcher` (same idea) |
| Production guard (CR-008A) | "no real adapter in Production" | Mode-aware: Simulator only after an Admin chose it (a fresh Production DB is "not chosen" and keeps refusing tasks with `RCS_NOT_CONNECTED`), Real needs `Rcs:RealDispatch:Enabled`, `RECOVERY_REQUIRED` blocks everything |
| RCS traffic | Logs only | Table `rcs_messages`: every OUT / IN message, each marked `SIMULATED` or `REAL`, secrets masked before storing |

`Rcs:RealDispatch:Enabled` keeps its meaning: **the deployment's permission for Real AGV**. Without it the mode cannot be switched to Real and the real adapter refuses every call. Nothing switches to Real by itself.

## 2. Rules implemented (all enforced by the backend)
* New environment = **Simulator, not chosen**. Outside Production Simulator is open; in Production nothing is dispatched until an Admin chooses a mode.
* **Simulator → Real** is refused unless: deployment allows Real; global dispatch config valid; RCS connection saved and on; every approved flow has a process → `taskTyp` mapping; every location of the flows has a `positionCode`; callback enabled with a secret and/or allowed sources; the real fleet is fresh (source Real, not simulated, Connected); **no unfinished Simulator task/order**; no recovery pending; explicit confirmation ("the system may control real AGVs"), a reason and a RequestId.
* **Real → Simulator** is refused while a Real task/order is unfinished, a dispatch has no determined outcome (`Sending` / `SyncError`) or a finished task still needs physical reconciliation. Nothing is cancelled to make a switch possible; the blocking tasks are listed.
* A task / order **keeps its mode**; `PinExecutionMode` refuses a different value. Task creation reads the mode `FOR SHARE` in its own transaction; a switch takes `FOR UPDATE` plus an advisory lock, so no task slips between check and switch, and concurrent switches are serialised.
* RequestId is unique: repeating a request returns the first answer and changes nothing (also for a refusal).
* **Restart:** the saved mode is restored. If the saved mode is Real and unresolved Real dispatches exist, the state becomes `RECOVERY_REQUIRED` (mode stays Real, never silently Simulator); new dispatch is blocked; nothing is resent. An Admin acknowledges it, and only when no unresolved dispatch is left.
* **Messages:** Real = the request body as built (without the token) and the response exactly as received, with HTTP status, timeout / connection error / HTTP error / RCS code recorded separately; a call refused before any network use is recorded as `NotSent` with the reason. Simulator = the real request object and the real result object of the simulated call. Authenticated callbacks are recorded as received. A recorder failure never changes the outcome of a dispatch. Payloads are cut at 32 000 characters.

## 3. API
`GET /api/execution-mode` (Admin, Operator) · `GET /api/execution-mode/readiness?target=` · `GET /api/execution-mode/history` · `POST /api/execution-mode/transition` · `POST /api/execution-mode/recovery/acknowledge` (Admin) · `GET /api/tasks/{id}/rcs-messages` · `GET /api/rcs-messages/{id}` (Admin, Operator). SignalR: event `RcsMessageRecorded` on the existing hub.

## 4. Database migration `AddExecutionModeAndRcsMessages` (additive, NOT applied anywhere by this CR)
`execution_mode_state` (single row), `execution_mode_transitions` (unique `RequestId`), `rcs_messages`, and a nullable `ExecutionMode` column on `transport_tasks` and `transport_orders` (NULL = created before this CR; treated as the mode of the deployment configuration). `Down` drops only these.

## 4b. Hardening (post-implementation review)
* **Work created before CR-038** (`ExecutionMode` NULL) is routed to NEITHER dispatcher: `RoutedRcsClient` answers "unknown outcome" (`LEGACY_TASK_MODE_UNKNOWN`) to create / continue / cancel and "inconclusive" to queries; `RoutedLegDispatcher` answers Blocked. The earlier fallback "use the deployment configuration" was removed: it could send an old simulator task to the real RCS once `Rcs:RealDispatch:Enabled` was turned on. Unpinned unfinished work blocks BOTH mode changes. An Admin can adopt it as **Simulator** (`POST /api/execution-mode/legacy/adopt-simulator`, reason required, audited); anything with an RCS task code that is not a simulator code (`SIM-…`) is NOT adopted. Nothing is ever pinned to Real automatically.
* **Simulator lifecycle:** create, accepted, assigned (RcsManaged), started, pick, drop, completed and cancel are supported. The simulator's device events (`AgvStarted`, `PickConfirmed`, `DropConfirmed`, `RcsCancelConfirmed`) are now recorded as IN messages "Simulated AGV Event: …" by `SimulationDriver`, only when the step really ran. `SimulationDriver` refuses a Real or unpinned task (`SIMULATION_NOT_ALLOWED_FOR_TASK`).
* **Communication monitor:** `queryTaskStatus` (Real and Simulator) is recorded when the answer CHANGES (first answer, status, AMR, or a different failure), not on every poll; cancel is recorded both ways; callbacks are recorded by `RcsCallbackService` (linked by `taskCode`); messages carry a database arrival order (`Seq`) so the timeline is exact even with equal timestamps. A message whose request code matches no task has no task: it is listed under "Messages not linked to a task" (`GET /api/execution-mode/unlinked-messages`, Admin) and never attached to a task by guesswork.
* **Configurable processes (Q-MLEG-1):** Real supports a leg only through the single generic leg template (Source → Destination path). Without a valid template (`LEG_TEMPLATE`) or a positionCode for every location of a published process (`PROCESS_LEG_LOCATIONS`) the mode switch to Real is refused, and `RouteEngine` refuses to create a Real order whose legs could not all be sent (`REAL_LEG_MAPPING_INCOMPLETE`): no order is half sent. The Q1PICK trigger-driven template is still not implemented.

## 5. Not done / limits
* The simulator has no callback transport of its own: its device events are recorded as "Simulated AGV Event" messages. Not verified against a real RCS-2000.
* The browser pages (mode panel, monitor, JSON tree, copy, SignalR refresh, responsive layout) were NOT verified in a browser: no browser tool was available. Only render and logic tests exist.
* Legs of configurable processes are still only as real as `RealLegDispatcher` is (leg template unconfirmed: Q-MLEG-1).
* The mode event is not pushed over SignalR (the page reloads after its own change).
* Open questions OQ-28 / OQ-32 and the protected rules (PER_AGV, BP-04 REVIEW, A3/A4/D1/D2 UNCHANGED, WCS owns inventory) are untouched.

## 6. Deploying
1. Back up the database (`deployment/scripts/backup.ps1`). 2. Apply the migration with the normal deploy (`deploy.ps1` runs the migrator). 3. Start: the mode row is created as Simulator / not chosen (Production keeps refusing tasks until an Admin chooses). 4. To use Real AGV: set `Rcs__RealDispatch__Enabled=true` and the mapping/callback configuration (see `.env.example`), save the RCS connection in the UI, then use the mode panel on `/operations` (readiness list, reason, confirmation). 5. Do the first Real task with the area confirmed safe and someone at the AGV.

## 7. Tests
Backend `Cr038DualModeTests` (21) and `Cr038HardeningTests` (17): messages in both modes, HTTP errors, timeout, connection error, non-JSON, `NotSent`, masking, transitions (all refusals), idempotency, concurrency, pinning, restart, recovery, guard. Frontend `npm run mode:test` (41) and `npm run ops:test`.

## 8. Final review (before merge) – limits to handle BEFORE deploying
* **Deploy with no unfinished tasks.** A task / order created before CR-038 has no pinned mode, is sent nowhere and cannot be tracked (queries are inconclusive). On a system that already runs Real dispatch, unfinished Real tasks must be finished or reconciled by hand BEFORE the upgrade: they cannot be adopted (their RCS task code is not a simulator code) and they block the switch to Real until they are final. Unfinished simulator-looking work can be adopted as Simulator by an Admin.
* **After the upgrade the mode is "Simulator, not chosen".** Production keeps refusing tasks (`RCS_NOT_CONNECTED`) until an Admin chooses a mode. A deployment that already ran with `Rcs:RealDispatch:Enabled=true` must switch to Real again through the readiness list (a deliberate, audited step).
* **Migration** `AddExecutionModeAndRcsMessages`: additive (two nullable columns, three new tables). Checked on a COPY of the development database (applied and reverted, existing rows unchanged); NOT applied to any operational database by this CR. Take a backup first.
* **queryTaskStatus is recorded only when the answer changes.** Consequence: a failure that repeats unchanged (for example the same timeout for ten minutes) shows once, so the monitor does not show how long it lasted; the first answer after a restart is recorded again (the memory of "last answer" is not persisted). The policy was assessed and NOT changed.
* **No retention yet** for `rcs_messages` (payload cut at 32 000 characters, a handful of rows per task). Plan a retention job before the table grows large.
* The test `PlcWorkerTests.One_PLC_going_offline…` is timing-dependent: it fails when run alone (also on the commit before CR-038) and passes inside the full parallel run. It is not related to this CR; it should be made deterministic separately.
* Browser pages: NOT VERIFIED (no browser available in the review environment).

## 9. Fix after deployment preparation: creating a request from Operations
* **Defect:** a fresh Production database is "Simulator, not chosen" and refuses tasks (`RCS_NOT_CONNECTED`), but choosing the mode the system is already in was refused with `ALREADY_IN_MODE` and the page offered only "switch to Real". Simulator could not be chosen at all. **Fix:** an Admin can confirm the never-chosen Simulator (reason required, audited, recovery still blocks); a mode already chosen is refused as before; Real keeps every condition.
* **Operations gate:** the health and the mode were read once and only again on a task event, so the create button could stay disabled on an old answer. They are now re-read every 5 s. In Simulator mode a degraded backend warns instead of blocking (the server accepts the request, as the Task page does); Real mode is unchanged. The refusal for a never-chosen mode now says what to do.
* "Cách điều phối: Legacy" is the RCS AMR-selection setting (`Rcs:Dispatch:Mode`, CR-028), not the Simulator / Real mode; the label says so now.
* Both pages create through the same `POST /api/tasks`; no second pipeline.
