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

**Legacy is the default and is unchanged** (guarded by the CR-027 golden statistics test and the existing suites). Switching the mode while tasks are active is not supported: drain first (a Legacy task stays under Legacy rules, a task created in RcsManaged mode stays RcsManaged).

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

`transport_tasks`: `AgvId` NOT NULL → nullable; new nullable columns `RobotCode` (64), `AssignedAt`, `RcsDispatchState` (40), `AssignmentCheckedAt`, and `AssignmentSyncFailures` (int, default 0); index on `RobotCode`; the PER_AGV index recreated with the narrower filter above. No row is changed or deleted. **Rollback:** `dotnet ef database update <previous migration>` (`AddRcsFleetSnapshots`) – `Down` restores NOT NULL, therefore it **fails while any task has `AgvId` NULL** (RcsManaged tasks): they must be handled first (they are new data, created only after the mode was switched). Schema fingerprints: `transport_tasks` is the only existing table block that changed; `agvs`, `transport_legs`, `task_history`, `transport_orders`, `station_transactions` and the fleet tables are identical to the baseline.

## 5. Historical data

Not touched. Old tasks keep `AgvId` (AGV1 / AGV2), `RobotCode` null, `RcsDispatchState` null; the `agvs` table and its rows stay. "Completed per AGV" still groups by `AgvId` (a task without an AGV row is never counted for an AGV); in RcsManaged mode the dashboard shows "completed per robot" by `RobotCode` and reports the tasks without a robot separately. Nothing is converted or re-attributed.

## 6. UI and statistics

RcsManaged: AGV Monitoring shows only the fleet list (any number of AMRs, fresh / stale / missing, last-seen time) with the counters tasks in progress / awaiting assignment / queued; the AGV summary, the AGV1/AGV2 cards and the per-AGV history are not rendered. Tasks page: the robot (or the business AGV for old tasks) and the hand-over state ("Waiting for the RCS to assign an AMR", …). Dashboard: per-robot figure and "no AMR assigned". Legacy: unchanged.

## 7. Not implemented / needs the vendor (nothing was assumed)

- **Real `genAgvSchedulingTask` adapter:** needs the RCS position code (`positionCodePath`, `type`) of every WCS location and `taskTyp` / `podTyp` / `podDir` / `priority` for BP01-BP06 – not in any project document.
- **Real `agvCallback` endpoint:** the authentication of RCS → WCS calls is not described (vendor question 3).
- **Routing legs (`RouteEngine`) in RcsManaged mode:** still select the AGV by role; RcsManaged applies to the transport **tasks** (`TaskOrchestrator`). Multi-leg orders keep the Legacy behaviour until a decision on them.
- `continueTask` with an RCS-chosen robot (question 6); `cancelTask` for a task without a robot (question 7c); behaviour of a repeated `reqCode` (question 1).
