# CR-028 – Dynamic fleet dispatch (RCS-managed dispatch, Phase 2 of the AMR work)

- **Status:** `IMPLEMENTED` on `feature/cr-028-dynamic-fleet-dispatch`, **on the RCS simulator only**. Stacked on `feature/cr-027-dynamic-amr-monitoring` (`00d24b0`, not merged); `main` is unchanged. Not merged, not deployed, no migration run on a deployed database.
- **Approved by the project owner (2026-11-21):** A1-B (branch from CR-027), `RcsManaged` on the Simulator, data model (`AgvId` nullable, `RobotCode`, `AssignedAt`, tracking fields, index adjustment), integration states, `MaxInFlightTasks`, robot-code mapping rules, UI/Dashboard rules, vendor questions.
- **Baseline (never edited):** `CR-028_BASELINE.md`. **Vendor questions:** `docs/03_RCS_Interface/VENDOR_QUESTIONS_CR-028.md`. **API reference:** RCS-2000 API Developer Guide V3.3 (`docs/03_RCS_Interface/`).

## 1. Architecture before and after

| | Before (Legacy) | After, `Rcs:Dispatch:Mode = RcsManaged` |
|---|---|---|
| Who chooses the AMR | WCS: the AGV row of the flow's **role** (FullRack → AGV1, EmptyRack → AGV2) at task creation | the **RCS** (`agvCode` is optional in `genAgvSchedulingTask`, 5.2.1 p. 21: "if not configured RCS-2000 selects an optimal AMR") |
| `RcsCreateTaskRequest.RobotCode` | the AGV's `RcsRobotCode` | **null** (no `agvCode` is sent) |
| `TransportTask.AgvId` | NOT NULL, set at creation | **nullable**; empty until the RCS reports an AMR **and** that robot code belongs to an `agvs` row; never "AGV1" |
| Identity of the real AMR | `AgvId` → `agvs` | **`RobotCode`** (as the RCS reported it) + `AssignedAt` |
| Concurrency | PER_AGV: one active task per AGV (unique index), per-AGV FIFO queue, `DispatchHold` | no AGV queue, no PER_AGV, no dispatch hold; the RCS spreads the work; optional **fleet-level** limit `MaxInFlightTasks` (default 0 = WCS sets no limit) |
| Fleet on the screen | AGV1 / AGV2 cards | one fleet list 0..N (CR-027) with the tasks linked by robot code |

**Legacy is the default and is unchanged** (guarded by the CR-027 golden statistics test and the existing suites). The mode is changed only by the procedure in section 9: WCS refuses to start in a mode while tasks or legs of the other mode are still active, and a configuration edited while WCS runs is reported as "restart pending", never silently applied.

## 2. `AgvId` and `PER_AGV`

**`AgvId`** keeps its foreign key to `agvs` and every historical value; it only became nullable. Meaning: "the business AGV row linked to the task" – set by WCS in Legacy, set only from a RCS-reported robot code that matches `agvs.RcsRobotCode` in RcsManaged. **PER_AGV** is not applied to RcsManaged tasks: the unique index `ux_transport_tasks_one_active_per_agv` now has the filter `"AgvId" IS NOT NULL AND "RcsDispatchState" IS NULL AND "State" IN (…)`, i.e. it protects exactly the tasks it protected before (all older tasks and all Legacy tasks have `RcsDispatchState` NULL) and leaves RcsManaged tasks outside, because the RCS may queue several tasks on one robot (vendor question 4). The old configuration keys keep their meaning; the new behaviour is selected only by the new key `Rcs:Dispatch:Mode` (an invalid value stops the start).

## 3. Task lifecycle

The **13 task states are unchanged**. A separate tracking attribute `RcsDispatchState` (null for Legacy / old tasks): `NotSent` → `Sending` (a create call is under way or being retried; a timeout stays here) → `Accepted` (RCS answered "successful"; robot unknown) → `AwaitingAssignment` (asked, no `agvCode` yet) → `Assigned` (RCS reported an AMR); `SyncError` when the status query fails or the RCS does not know the task (cleared by the next successful query; an already known robot is kept). **Accepted is never Assigned.**

Flow: create (no AGV) → Process (reservations as before; limit check) → Dispatching → `DispatchToRcs` (no robot code; request code `WCS-<taskId>` never changes) → Accepted (+ `RcsDispatchState.Accepted`) → each scheduler tick `SyncAssignmentsAsync`: `QueryTaskAsync(requestCode)` → `agvCode` → robot recorded (`RCS_ASSIGNED_AMR`) → … → callbacks (`agvStarted` etc. carry `robotCode`, which also records or changes the robot) → Completed.

**Robot code mapping.** `RcsRobotMapper`: (1) `agvs.RcsRobotCode` match → `AgvId`; (2) presence in the fleet read model of the **current source only** (simulator rows never vouch for a real robot). A code that is not in the fleet read model is stored raw and flagged (`RCS_ROBOT_NOT_IN_FLEET`); no AGV is created, AGV1 is never assumed. A different code later is followed and recorded (`RCS_ROBOT_CHANGED`).

**Timeout / retry / duplicates.** A timeout leaves the task in Dispatching / `Sending` (not failed). The retry (existing backoff) first queries the status by the same request code and sends a create only when the RCS does not know the task, so no second RCS task is made (tests for "task created, answer lost" and "not created"); the same API request key returns the same task.

## 3a. Fleet-level limit

`Rcs:Dispatch:MaxInFlightTasks` (default 0 = WCS sets no limit): tasks in an AGV-occupying state count; the next tasks are queued FIFO by creation time (history code `QUEUED_FLEET_LIMIT`); when one finishes the oldest queued is sent (`TryDispatchFleetQueueAsync`); Maintenance (CR-026) still holds new work. Also `AssignmentCheckSeconds` (5) and `AssignedRecheckSeconds` (60, detects a robot change).

## 4. Migration `AddRcsManagedDispatch` (additive; DEV only)

`transport_tasks`: `AgvId` NOT NULL → nullable; new nullable columns `RobotCode` (64), `AssignedAt`, `RcsDispatchState` (40), `AssignmentCheckedAt`, and `AssignmentSyncFailures` (int, default 0); index on `RobotCode`; the PER_AGV index recreated with the narrower filter above. No row is changed or deleted. **Rollback:** `dotnet ef database update AddRcsFleetSnapshots`. `Down` now starts with a **safety check** and stops with `ROLLBACK_REFUSED` (nothing is changed; the migration is one transaction) while any task has `AgvId` NULL or an RcsManaged task is still active. WCS never assigns AGV1/AGV2 to force it and never deletes tasks or history. Procedure: section 8. Schema fingerprints: `transport_tasks` is the only existing table block that changed; `agvs`, `transport_legs`, `task_history`, `transport_orders`, `station_transactions` and the fleet tables are identical to the baseline.

## 5. Historical data

Not touched. Old tasks keep `AgvId` (AGV1 / AGV2), `RobotCode` null, `RcsDispatchState` null; the `agvs` table and its rows stay. "Completed per AGV" still groups by `AgvId` (a task without an AGV row is never counted for an AGV); in RcsManaged mode the dashboard shows "completed per robot" by `RobotCode` and reports the tasks without a robot separately. Nothing is converted or re-attributed.

## 6. UI and statistics

RcsManaged: AGV Monitoring shows only the fleet list (any number of AMRs, fresh / stale / missing, last-seen time) with the counters tasks in progress / awaiting assignment / queued; the AGV summary, the AGV1/AGV2 cards and the per-AGV history are not rendered. Tasks page: the robot (or the business AGV for old tasks) and the hand-over state ("Waiting for the RCS to assign an AMR", …). Dashboard: per-robot figure and "no AMR assigned". Legacy: unchanged.

## 7. Not implemented / needs the vendor (nothing was assumed)

- **Real `genAgvSchedulingTask` adapter:** needs the RCS position code (`positionCodePath`, `type`) of every WCS location and `taskTyp` / `podTyp` / `podDir` / `priority` for BP01-BP06 – not in any project document.
- **Real `agvCallback` endpoint:** the authentication of RCS → WCS calls is not described (vendor question 3).
- **Routing legs (`RouteEngine`) in RcsManaged mode:** converted in the follow-up (section 7a). Still open: the real leg dispatcher, recording the AMR that the RCS chose for a leg, and the fleet-level limit for legs.
- `continueTask` with an RCS-chosen robot (question 6); `cancelTask` for a task without a robot (question 7c); behaviour of a repeated `reqCode` (question 1).

## 7a. RouteEngine / multi-leg orders in RcsManaged mode (follow-up, owner request 2026-11-21)

**Survey.** Besides the Transport **Task** path (`TaskOrchestrator`, BP01-BP06 single transport), the `RouteEngine` executes published Business Processes as an Order of Legs. Before this follow-up `RouteEngine.DispatchLegAsync` chose the AGV by role (`GetByRoleAsync`: full rack → AGV1, empty rack → AGV2), applied PER_AGV (one active leg / task per AGV, queue `AGV_BUSY`, `DispatchHold`), stored `leg.AgvId`, sent the robot code and moved the Station Transaction to `AgvAssigned`. A leg was already **one independent dispatch** (`ILegDispatcher.DispatchAsync`), so no multi-leg API of the RCS is assumed.

**Change (RcsManaged only, Legacy untouched).**

| Flow | RcsManaged behaviour |
|---|---|
| One-leg order | the leg is sent with **no robot code**, `AgvId` stays null, no role lookup, no AGV row needed |
| Multi-leg order (e.g. A→buffer, buffer→B) | every leg is its own RCS task; the RCS may choose another AMR for each leg (the rack is stored at the buffer between legs, confirmed by drop) |
| Two orders of the same rack kind | not serialised: no PER_AGV for legs, no `AGV_BUSY` |
| `DispatchHold` / per-AGV queue | not consulted for legs |
| Port check (`PORT_BUSY`) | kept: it is a PLC / Station rule, not an AGV rule |
| Station Transaction | opened as before but **not** moved to `AgvAssigned` (no AMR is known); it advances at handover |
| WaitingForDestination / `AfterPick` | unchanged logic; `continueTask` is addressed by the **task code** of the leg (it never carried a robot) |
| BP01-BP06 single-transport tasks | already RcsManaged (CR-028 main part) |

**Not done (needs information or a decision; nothing guessed).**
1. **Real leg dispatcher:** still only the simulator (open question Q-MLEG-1: the RCS template of a configurable leg, the Location→RCS position code and `taskTyp` mapping are not confirmed). Affects **every** RouteEngine flow on a real RCS.
2. **The AMR that carried a leg** is recorded per leg since the approved follow-up (section 10). The fleet view still links only tasks (not legs) to an AMR.
3. **`MaxInFlightTasks` is not applied to legs** (the limit counts tasks only).
4. **`AfterPick` flows depend on vendor question 6:** does `continueTask` work without a robot code, and does the RCS keep the rack on the same AMR between pick and continue? Until answered, flows with a destination chosen after pick are *simulator-verified only*. Same for the cancel of a leg that has no AMR yet (question 7c).
5. The legs of one order may be carried by different AMRs; whether and where this is acceptable is **not decided** – see sections 11 and 12 (classification and open decisions).

## 8. Rollback of `AddRcsManagedDispatch` – procedure

1. **Back up** the database. Never run on a deployed database without an approved window.
2. Set `Rcs:Dispatch:Mode = Legacy`, drain (section 9) and restart so that no RcsManaged task or leg is active.
3. Inspect: `SELECT COUNT(*) FROM transport_tasks WHERE "AgvId" IS NULL;` and `SELECT COUNT(*) FROM transport_tasks WHERE "RcsDispatchState" IS NOT NULL AND "State" NOT IN ('Completed','Failed','Cancelled');`
4. `dotnet ef database update AddRcsFleetSnapshots`. If a count above is not 0 the migration stops with `ROLLBACK_REFUSED (AddRcsManagedDispatch): n task(s) have no AgvId and m RcsManaged task(s) are still active. Nothing was changed.`
5. Active RcsManaged tasks (m): finish them or cancel them in WCS **and** in the RCS (never delete them).
6. Finished tasks without an AGV (n): this is a **business decision of the owner**. Recommended: do **not** roll back (keep the migration; it is additive). If the owner decides otherwise, the AGV of each task is set explicitly and traceably by the owner / DBA; WCS never defaults to AGV1/AGV2 and never removes tasks or history.
7. Verified by test (`Cr028ModeAndLegTests`): refused with an active unassigned task; refused with a finished task without AGV (nothing changed, task and history kept); allowed once no task lacks an AGV (rows kept, AgvId NOT NULL again, forward migration works again).

## 9. Changing `Rcs:Dispatch:Mode` – procedure and guard

There is no runtime switch (no API/UI): the mode is configuration read at start. Procedure:
1. Enable **Maintenance** (CR-026) so no new work starts; let the active work finish or cancel it in WCS and the RCS. Legacy → RcsManaged: no unfinished Legacy task and no Legacy leg on its way (an AGV chosen by WCS). RcsManaged → Legacy: no unfinished RcsManaged task and no RcsManaged leg that was sent.
2. Change the setting and **restart** WCS.
3. **Guard at start** (`DispatchModeStartupCheck`): if the other mode still has active work, WCS **does not start** and logs `DISPATCH_MODE_CHANGE_REFUSED: Rcs:Dispatch:Mode is 'X' but n unfinished task(s) and m leg(s) on their way still belong to 'Y'…`. Nothing is converted, assigned or deleted. Restore the old setting, restart, finish the work, repeat. (A database that cannot be read yet, for example before the migration, is logged and skipped.)
4. **Edited while running:** the process keeps the mode it started with (`IOptions` is read once). `DispatchModeState` logs an error and `GET /api/agvs/fleet` returns `dispatchModePending`; AGV Monitoring shows a warning "the change is not active until restart". The operator is never left believing the new mode runs.
5. Limits: legs that are not yet sent (Ready, no AGV, no task code) do not block the switch (they are sent under the new mode); a Failed task is not "active".

## 10. RobotCode per Transport Leg (approved 2026-11-21)

**Data.** Migration `AddLegRobotCode` (additive; `AddRcsManagedDispatch` is untouched): `transport_legs.RobotCode` (varchar 64, nullable, indexed) and `transport_legs.AssignedAt` (nullable). Nothing else changes in the schema; existing rows get NULL (Legacy legs keep NULL forever: their AMR is the `AgvId` AGV row).

**Behaviour (RcsManaged only).**
- After a leg is sent, each routing pass (`RouteEngine.RunOnceAsync → SyncLegRobotsAsync`) asks the RCS which AMR carries **that** leg (`ILegDispatcher.QueryAsync` = `queryTaskStatus` by the leg's request code; `agvCode` "exists when the task has been assigned"). Unassigned legs are asked every `AssignmentCheckSeconds` (5), assigned legs every `AssignedRecheckSeconds` (60); the interval is kept in memory (`LegAssignmentThrottle`, no extra column).
- **Only a valid `agvCode`** is recorded (`TransportLeg.RecordRobot`). Empty / not yet assigned → nothing is stored, the leg is not failed.
- **No default, no copy:** the robot of the Transport Task or of an earlier leg is never copied to a leg; AGV1/AGV2 are never assumed; `transport_legs.AgvId` stays empty for RcsManaged legs (so PER_AGV and its unique index do not apply; the mapping to an `agvs` row is **not** used for legs).
- **Robot not in the fleet read model** (current source only): stored as reported, event `LEG_ROBOT_NOT_IN_FLEET`; no AGV is created.
- **Query failure / unknown task:** event `LEG_ROBOT_SYNC_ERROR` once (not at every tick); the leg is not failed; recovers by itself.
- **Station Transaction:** when the AMR becomes known the active transactions of the leg move to `AgvAssigned` (forward only; `AgvId` of the transaction stays empty).
- **A different AMR reported:** *before the pick* it is followed (`LEG_ROBOT_CHANGED`); *while the rack is on the AMR* it is **not followed** – the leg keeps its robot and `LEG_ROBOT_CHANGE_REFUSED` is recorded once (no hand-over is approved; reconcile in the RCS). A leg that is finished keeps its robot as history.
- **Consecutive legs:** each leg keeps its own robot. If a later leg is carried by another AMR than the previous leg of the order, `LEG_ROBOT_DIFFERS_FROM_PREVIOUS_LEG` is recorded as **information only**; no state change, no action.

**API / DTO / UI.** `LegDto` (inside `OrderDto`, `/api/routing/orders…`) gets trailing optional `RobotCode`, `AssignedAt` (older clients ignore them). Routing page: new column "AMR (chosen by the RCS)" ("Waiting for the RCS" until reported). Repositories: `ListManagedLegIdsToSyncAsync`. No other consumer of legs exists (statistics and the fleet view use tasks). Simulator: a leg sent without a robot code is registered with the simulated RCS, which chooses the AMR exactly as for tasks (Legacy legs are unchanged).

**Rollback of `AddLegRobotCode`:** `dotnet ef database update AddRcsManagedDispatch`. `Down` first **copies** every leg's `RobotCode` / `AssignedAt` to `transport_leg_robot_backup` (outside the EF model, kept after the rollback) and then drops the two columns; if the migration is applied again, `Up` restores the values from that table. No leg or history is deleted, no AGV is assigned. Rolling back both CR-028 migrations: this one first, then `AddRcsManagedDispatch` (section 8). Verified by test.

## 11. Classification of the processes: is the rack carried continuously, is there a hand-over, may the AMR change?

Source: `reference-processes.json` (reference drafts derived from the approved matrix), `docs/specification/process-flows.md`, RCS-2000 API V3.3 (5.2.2, 4.2). **"May the AMR change" is "not decided" for every row: nothing in the approved documents says that an AMR may be replaced while a rack is carried, and nothing describes a hand-over between AMRs.**

| Process | Legs (RouteEngine steps) | Rack carried without interruption? | Hand-over point? | AMR change allowed? | Missing / to confirm |
|---|---|---|---|---|---|
| BP-01 A1/A2 full rack | **1 leg**: pick at A1/A2 → destination chosen **after pick** (B1/B2 no-bind, F1/F2, F5) | Yes, pick to drop, one leg (the rack is IN_TRANSIT on the AMR; WAITING_FOR_DESTINATION keeps it there) | Only the physical pick (PLC handshake at A) and drop (at the destination); none between AMRs | **No** inside the leg (rack on the AMR); not decided otherwise | Q9, Q10, Q11 (`continueTask` same AMR; AMR keeps the rack; re-plan) |
| BP-02 A1/A2 needs an empty rack | 1 leg: source B1/B2/F3/F4 → A1/A2 (destination known before pick) | Yes | pick / drop only | **No** inside the leg | Q9, Q11 |
| BP-03 B1/B2 empty rack | 1 leg: pick at B1/B2 → destination after pick (A1/A2 no-bind, F3, F4) | Yes | pick / drop only | **No** inside the leg | Q9, Q10, Q11 |
| BP-04 B1/B2 needs a full rack | 1 leg: source A1/A2/F1/F2 → B1/B2 | Yes | pick / drop only | — | **REVIEW – blocked, not executable** (protected rule); nothing is concluded |
| BP-05 F5 requests a full rack | 1 leg: source F1/F2 → F5 | Yes | pick / drop only | **No** inside the leg | Q9, Q11 |
| BP-06 F5 returns an empty rack | 1 leg: pick at F5 → destination after pick (A1/A2 no-bind, F3, F4) | Yes | pick / drop only | **No** inside the leg | Q9, Q10, Q11 |
| PH2-01…PH2-06 (Phase 2 drafts) | 1 leg each in the draft; the slides mention an "intermediate waiting position" (P1-P4, M5) that is **not** modelled | Not determinable | The waiting position could become a hand-over point | Not decided | The draft is not approved; whether the waiting position means a second leg / another AMR needs the owner (PH2-04 is REVIEW) |
| Multi-leg process, e.g. "A → B, else buffer, wait, buffer → B" (exists only as **test data** and as what the engine supports; it is **not** an approved or reference process) | 2+ legs: BufferStorage, (Waiting), BufferRetrieval | **No**: the rack is dropped and stored at the buffer (drop confirmed, inventory updated) before the next leg | **Yes**: a confirmed drop at a Location (the buffer) – a hand-over of the **rack to the location**, not of AMR to AMR | **Not decided.** Independent legs may technically run on another AMR (the RCS chooses), but no business statement allows it | Owner: may the retrieval leg use another AMR? If continuity is required: Q12 (pinning by `agvCode`) |

Conclusion: **every approved flow today (BP-01…06) is a single continuous leg**, so no approved flow depends on an AMR change; the multi-leg question concerns processes that an Admin may configure later (and the Phase 2 drafts).

## 12. Rule for the AMR between legs and what is enforced / still open

| Case | Rule (owner decision 2026-11-21) | State |
|---|---|---|
| **Continuous leg**, rack on the AMR (pick done, drop not done), including AfterPick / `continueTask` | No automatic change of AMR; any change of AMR requires an approved hand-over procedure | **Enforced:** a different AMR reported by the RCS is not followed and is recorded (`LEG_ROBOT_CHANGE_REFUSED`). WCS cannot prevent the RCS from re-planning, nor perform a hand-over: vendor questions 9–11. |
| **Independent legs** with a clear hand-over (rack dropped and confirmed at a Location) | May be carried by different AMRs; the hand-over must be confirmed in a suitable state | The confirmation exists for the rack/location (drop confirmed, inventory updated, station transaction handover). **No per-process setting** says that a change is allowed; WCS only observes (`LEG_ROBOT_DIFFERS_FROM_PREVIOUS_LEG`) and does not default to "allowed" or "forbidden". |
| Automatic rack hand-over between AMRs | Not approved | **Not implemented.** |

**Business decisions needed before anything more is built (work stopped here):**
1. Is the AMR change allowed for a given multi-leg process? A per-process / per-step attribute (for example `AmrContinuity = Continuous | HandoverAllowed`) would have to be added to the Business Process definition (a separate CR; processes are data).
2. If continuity is required between two legs, may WCS send the previous leg's `agvCode` in the next `genAgvSchedulingTask`? (This is WCS choosing the AMR for that leg, an exception to "only the RCS chooses"; it also needs vendor question 12.)
3. What must happen on `LEG_ROBOT_CHANGE_REFUSED` (a rack on an AMR that the RCS says is another AMR): Order to `ReconciliationRequired`, alarm only, …?
4. What exactly confirms a hand-over (the confirmed drop at the Location, or an additional event)?
