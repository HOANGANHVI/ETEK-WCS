# CR-030 – Real RCS dispatch adapter (HIKROBOT RCS-2000 API V3.3)

| | |
|---|---|
| **Status** | `CR-030 IMPLEMENTED – MOCK VERIFIED – PENDING REAL RCS COMMISSIONING` |
| **Approved by** | Project owner (2026-11-21) |
| **Branch** | `feature/cr-030-real-rcs-dispatch` (from `main` `f64a8b0`, which contains CR-027, CR-028, CR-029). **Not merged.** |
| **Source** | RCS-2000 API Developer Guide V3.3 (`docs/03_RCS_Interface/UD35865B_…pdf`): 5.2.1 genAgvSchedulingTask, 5.2.2 continueTask, 5.2.3 cancelTask, 5.2.15 queryTaskStatus, 5.3.1 agvCallback, Appendix A (status codes), chapter 3.1 (tokenCode) |
| **Verified with** | A **MOCK** RCS server (`tests/WCS.Tests/Support/MockRcsServer.cs`) that answers as the document describes. **Nothing was verified against a real RCS-2000, a real AMR or real mappings.** |
| **Real Dispatch is** | **OFF by default.** Never switched on automatically; never enabled in the running environment by this CR. |

## 1. Three separate things (kept apart)

| | Code | Switch |
|---|---|---|
| **Simulator** (unchanged) | `RcsSimulator`, `SimulatedLegDispatcher` | default |
| **Real Fleet Monitoring** (CR-027) | `HikFleetQueryClient` – read-only `queryAgvStatus` | `Rcs:Fleet:Source=Real` |
| **Real Fleet Dispatch** (CR-030) | `HikRcsClient` (`IRcsClient`), `RcsCallbackService` + endpoint, `RealLegDispatcher` | `Rcs:RealDispatch:Enabled=true` |

CR-030 does not change CR-027 monitoring, the simulator or Legacy dispatch. With `Enabled=false` (default) the registered `IRcsClient` is the simulator exactly as before (guarded by a test and by the full regression run).

## 2. API implemented (`HikRcsClient`)

All calls: `POST http://<host>:<port><ServicePath>/<api>`, JSON; `host`/`port`/token come from the **RCS connection saved in the UI** (token stored encrypted, sent only as `tokenCode`, never logged, never in an error text). `reqCode` ≤ 32 characters, derived **deterministically** from the WCS request code (a repeated request keeps the same `reqCode`, as the document requires).

| API | Section | What WCS sends | Result mapping |
|---|---|---|---|
| `genAgvSchedulingTask` | 5.2.1 | `reqCode`, `reqTime`, `clientCode` (if set), `tokenCode`, `taskTyp` (from the process mapping), `positionCodePath` (from the location mapping), `taskCode` = WCS request code (≤ 64), `podDir`/`podTyp`/`priority`/`taskMode` (if mapped). **No `agvCode`** unless a Legacy task names its AGV (≤ 5 characters) – the RCS chooses the AMR | code `0` → Accepted; **`6`** (same reqCode, task not completed) → Accepted; **`1`** → Rejected (certain failure); **`99`**, undefined codes, HTTP error, non-JSON, timeout, connection error → **Unknown** (never a certain failure) |
| `queryTaskStatus` | 5.2.15 | `taskCodes:[<request code>]` (a new `reqCode` per poll) | `data[]` matched by `taskCode`; `taskStatus` 1/3/6 → Created, 2 → Executing, 4 → Cancelling, 5 → Cancelled, 9 → Completed, 10 → Interrupted, 0/other → Unknown; **`agvCode` present = assigned AMR** (empty = none yet); empty `data` → NotFound; any failure → Unknown |
| `continueTask` | 5.2.2 | **one** identifier (`taskCode`, the recommended one), `nextPositionCode` {`positionCode`,`type`} from the location mapping | as above |
| `cancelTask` | 5.2.3 | `taskCode` only (by task id; `agvCode` would take precedence and free the AMR – not used), `forceCancel` only if `CancelMode` is set | as above |
| `agvCallback` | 5.3.1 | (RCS → WCS) see section 4 | answer `{code, message, reqCode}` |

The mapping of `taskStatus` values 0/3/6 is a technical reading of the document (vendor question 7b), not a business rule.

## 3. Mapping configuration (data, never code) – `Rcs:RealDispatch`

| Key | Meaning | Default |
|---|---|---|
| `Enabled` | master switch | **false** |
| `Port` | override of the RCS port (0 = use the port of the saved RCS connection) | 0 |
| `ServicePath` | `/rcms/services/rest/hikRpcService` (document 5.2) | as shown |
| `ClientCode`, `TimeoutSeconds`, `CancelMode` | `clientCode` (≤ 16), call timeout (default 10), `forceCancel` (empty = not sent) | empty / 10 / empty |
| `Locations:<WCS code>:{PositionCode, Type}` | WCS Location → RCS `positionCode` (≤ 16) + `type` "00"–"13" | **empty** |
| `Processes:<Bp01…Bp06>:{TaskTyp, PositionPath[], PodDir, PodTyp, Priority, TaskMode}` | WCS process flow → `taskTyp` (≤ 16) and which locations form `positionCodePath` (`Source`, `Request`) | **empty** |
| `ConfigFile` | optional JSON file (not in Git) that carries the two mappings above | empty |

Validation (`RealDispatchReadiness`): `type` must be "00"–"13", `podDir` ∈ {"180","0","90","-90"}, `priority` 1–127, `taskMode` 0–3, lengths per the document. **Fail closed:** a call that lacks any item it needs (RCS connection, port, location, process, `taskTyp`, path) sends **nothing** and returns `RCS_NOT_READY` / `RCS_MAPPING_MISSING` / `RCS_NOT_CONFIGURED` with the missing item named. WCS never generates a `positionCode` or `taskTyp`. At start with `Enabled=true` the missing items are logged (never a secret).

A destination chosen after the pick (BP-01, BP-03, BP-06) is sent later by `continueTask` with the destination's mapped position; which positions the *first* request carries is the process `PositionPath` (vendor template dependent – question 17).

## 4. Callback (`agvCallback`) and its authentication

- Endpoint: `POST <Callback:Path>` (default `/rcs/agv/agvCallbackService/agvCallback`; the document's `/xxx/…` prefix is the integrator's choice). It is **not mapped at all** unless `Enabled` **and** `Callback:Enabled`; it must not be under `/api` (that prefix needs the browser client header). Anonymous at the ASP.NET level; protected only by the checks below. Nginx forwards `/rcs/` to the backend.
- **The API document V3.3 defines NO authentication for RCS → WCS calls** (only `tokenCode` for WCS → RCS). Therefore **none was invented as "RCS compatible"**. WCS-side checks, **all configured ones must pass**: (a) a shared secret expected in the header `X-Rcs-Callback-Token` (`Callback:SharedSecret`, constant-time comparison; from the environment, never Git), (b) an allowed source address list / CIDR (`Callback:AllowedSources`; blank entries are ignored). **With neither configured every callback is refused (401)**; callbacks with a wrong or missing credential get 401 with no detail; the body is limited (default 64 KB, 413). Whether the RCS can send such a header (or a reverse proxy adds it) is a vendor / deployment question (18).
- Payload: JSON object with `reqCode`, `method` (`start`, `outbin`, `end`, `cancel`, `ctu`), `robotCode`, `taskCode` required (values may arrive as text or number). Invalid → `{"code":"1"}`; unknown task / robot mismatch → `{"code":"1"}`; applied, duplicate, ignored → `{"code":"0","message":"successful","reqCode":…}`.
- Effects (existing CR-028 / task rules, nothing new): `start` and `outbin` → the AMR is executing (`AgvStarted`), and the **`robotCode` is recorded on the task** (RcsManaged); `end` → `RcsEnded` (**does not complete the task** without a confirmed drop); `cancel` → `RcsCancelConfirmed`; `ctu` (CTU only) → acknowledged, no change. A callback **never confirms a pick or a drop and never changes Rack or Inventory**.
- **Duplicates:** the event key `rcs-cb:<reqCode>` (existing `processed_events` table): the same `reqCode` is applied once. **Out of order:** the existing state machine ignores and notes an event that does not fit the state (`OUT_OF_ORDER_EVENT_IGNORED`); a late event after the task ended is noted (`LATE_EVENT_IGNORED`).

## 5. Lifecycle (unchanged decisions of CR-028 kept)

Create (no `agvCode`) → Accepted (**sent ≠ assigned**) → polling `queryTaskStatus` (`AssignmentCheckSeconds`) → `agvCode` → `RobotCode` recorded on the Task; a changed `agvCode` is followed and recorded; callbacks name the robot as well. Timeouts and connection errors are **unknown outcomes**: the task stays Dispatching, the retry first asks the RCS and re-sends only when the RCS does not know the task, with the **same `reqCode`/`taskCode`**; a repeat answered with code `6` is accepted. `continueTask` and `cancelTask` follow the existing orchestrator rules. `forceCancel` mode `0` (RCS default) puts the rack down where the AMR is: **the rack position then becomes uncertain** and the existing cancel reconciliation (CR-008B) applies.

**Routing legs (`RouteEngine`) are NOT dispatched** with the real adapter: `RealLegDispatcher` returns *Blocked* (`LEG_DISPATCH_NOT_AVAILABLE_FOR_REAL_RCS`) – there is no confirmed mapping of a configurable leg to a task type (Q-MLEG-1). Hence **no `RobotCode` is synchronised for legs with the real RCS yet**; CR-028 leg tracking works with the simulator only. With the real adapter on, the **simulator API is not exposed**.

## 6. Safety

Real Dispatch is off by default; never switched from Simulator to Real automatically; fail closed on missing configuration/mapping; no PLC is written; no real transport command was sent by this work; the token and the callback secret never appear in logs, history or error messages (tested for the history and the call results); no host, token or password in Git; no running environment was changed.

## 7. Database / migration

**None.** No schema change, no migration. (Callback deduplication uses the existing `processed_events` table.) Rollback = set `Rcs:RealDispatch:Enabled=false` (back to the simulator); nothing else to undo.

## 8. Tests (MOCK; not real RCS tests)

`Cr030RealRcsDispatchTests` – **31 cases** against the mock: off by default and the client switch; legs blocked; documented request fields (no `agvCode`, `taskCode`, `reqCode` ≤ 32, `tokenCode`, path, `podDir`/`priority`); sent ≠ assigned; polling records the robot, follows a change; unassigned wait; code 99 → sync error and recovery; all `taskStatus` mappings; query NotFound vs Unknown (not JSON, unreachable); missing location/process/RCS connection/AMR code > 5 → nothing sent, clear message, no token; readiness report never generates a mapping; timeout after create (one RCS task), failure before create (same `reqCode`, one task); code 6 duplicate; lost connection → unknown, not failed; refusal code 1; continue/cancel bodies and unmapped destination; callback authentication fails closed (secret, CIDR incl. IPv4-mapped, both, none, disabled); callback start/end/duplicate; out-of-order, malformed, unknown task/method, CTU, numeric robot code; HTTP endpoint 401/200/413 and not served when disabled; Legacy task with the real adapter sends the AGV code. The earlier suites (Legacy, CR-027, CR-028) run unchanged.

**Results (2026-11-21):** backend build succeeded; `Cr030RealRcsDispatchTests` 31/31; full backend suite **977 passed, 1 failed of 978** – the failure was a 31-second Npgsql read timeout in `Cr028ModeAndLegTests.Rollback_of_AddRcsManagedDispatch…` (a scratch-database migration test) while the 9-minute batch was running; the whole `Cr028ModeAndLegTests` class passed 2 of 2 when re-run alone (11/11 each). No frontend file changed.

## 9. Still depends on the vendor / not verified

See `VENDOR_QUESTIONS_CR-028.md` (questions 1–15) and the new 16–21: the position/task-type mapping and what the *first* request must carry for each BP template; callback authentication and the RCS retry policy; whether `taskCode` is honoured and returned; `cancelTask` modes; `queryTaskStatus` rate limits and its answer for an unknown task; the meaning of "end" relative to the physical drop; leg mapping.

## 10. Conditions before Real Dispatch may be switched on (commissioning gate)

1. The RCS connection saved by an Admin in the UI (host, port, token) and **Real Fleet Monitoring** (CR-027) verified read-only against the real RCS first.
2. The **location mapping and process mapping** (Bp01, Bp02, Bp03, Bp05, Bp06; BP-04 stays REVIEW) supplied and **confirmed by the RCS engineer** (positionCode/type, taskTyp, path, podDir…); the startup log shows no missing item.
3. The callback: the RCS engineer confirms how the RCS authenticates (or a reverse proxy adds the secret header); `SharedSecret`/`AllowedSources` set from the environment; the route reachable from the RCS only.
4. PLC side ready for the physical handover (CR-029 pending): the handshake, interlocks and rack presence/identity (D-4) accepted; **no real transport command before that**.
5. A supervised commissioning plan with the RCS and PLC engineers (`docs/reviews/REAL_DEVICE_COMMISSIONING_PLAN_2026-11-21.md`): first a read-only run, then **one** task with the AMR in a safe state, with a cancel procedure agreed.
6. `Rcs:Dispatch:Mode` decided (Legacy needs AGV rows whose RCS robot codes are ≤ 5 characters; RcsManaged applies to tasks only, legs are blocked).
7. A rollback decision: `Enabled=false` returns to the simulator (guarded in Production by `RCS_NOT_CONNECTED`).
