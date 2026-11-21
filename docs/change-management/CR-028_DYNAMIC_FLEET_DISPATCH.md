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
2. **The AMR that carried a leg is not recorded** (a leg has no `RobotCode`; no `queryTaskStatus` for legs; the fleet view does not link legs to an AMR). Proposal: add `RobotCode / AssignedAt` to `transport_legs` and extend `ILegDispatcher` with a status query, exactly as done for tasks – needs an owner decision (one more additive migration).
3. **`MaxInFlightTasks` is not applied to legs** (the limit counts tasks only).
4. **`AfterPick` flows depend on vendor question 6:** does `continueTask` work without a robot code, and does the RCS keep the rack on the same AMR between pick and continue? Until answered, flows with a destination chosen after pick are *simulator-verified only*. Same for the cancel of a leg that has no AMR yet (question 7c).
5. The legs of one order may be carried by different AMRs; whether the business accepts that for a given process is a business statement the owner should confirm per process.

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
