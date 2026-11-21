# CR-028 – Results

Branch `feature/cr-028-dynamic-fleet-dispatch` (stacked on CR-027). Lean test strategy (approved): build both sides, tests of the changed code, Legacy regression guard; no full-suite run was required.

## 1. Tests

| Check | Result |
|---|---|
| Backend build | succeeded |
| `Cr028DynamicDispatchTests` (new, 22 cases) | **22 / 22**, repeated 10 times after fixing a tie in the test clock (no flake) |
| Legacy + related: `Cr027*` (golden statistics, fleet), `Cr008*` (dispatch reliability, intake, hold), `Concurrency`, `ProcessFlow`, `Cr026*`, `Robustness`, `ConfigApi` | **197 / 197** |
| Frontend: `tsc -b`, ESLint, `i18n:check`, `build` | clean |
| Frontend smoke: i18n 76, dashboard 20 (+2 new), preferences 10, monitor 23, plcwrite 20, plcmonitor 29 | all passed |
| Real backend + browser check (RcsManaged, simulator, 3 AMRs): two tasks created without an AGV → both Accepted, then assigned SIM-001 / SIM-002 by the simulated RCS; `agvId` null; fleet list shows the tasks by robot; no AGV1/AGV2 cards | as expected (screenshots in `Downloads/CR028_screenshots`, not committed) |

Not run (deferred by decision): the complete backend suite (~9 minutes); the previous full run of CR-027 was 901 / 901.

## 2. What the new tests prove

1, 2, 3 and 7 AMRs and **no AGV row at all**: tasks are sent without a robot, stay `Accepted` (not assigned), then get the AMR the simulated RCS chose (spread least-loaded first) · one AMR can have four active tasks (no PER_AGV) · robot chosen by the RCS even when AGV1/AGV2 rows exist (never `R-FULL`) · no AMR available → `AwaitingAssignment`, never attributed; assigned later · failing status query → `SyncError`, task not failed, recovers · robot not in the fleet read model kept raw + flagged, no AGV created; AgvId set only when an AGV row has that robot code · robot change followed and recorded · callbacks carry the robot and the task completes with inventory updated · timeout after create / before create: one RCS task, same request code, create calls 1 / 2 · same request key twice → one task, one send · `MaxInFlightTasks` FIFO and release on completion; no limit → no WCS queue · Legacy unchanged (AGV by role, robot code sent, PER_AGV queue) · the PER_AGV index still stops two Legacy tasks and does not stop two RcsManaged tasks · invalid mode stops the start · fleet view links by robot code and counts awaiting tasks · tasks without a robot are never attributed (and `agvs` untouched).

## 3. Before / after

Golden statistics (`Cr027StatisticsBaselineTests`, Legacy) identical. Schema fingerprints: only `transport_tasks` changed. PLC, Station, Inventory, Reservation code: not modified (the only touch is the shared orchestrator, whose Legacy paths are unchanged).

## 4. Not verified with a real RCS

Everything real: the create request without `agvCode`, the `queryTaskStatus` answer and when `agvCode` appears, callbacks, behaviour on a repeated `reqCode`, robot changes, queued tasks per robot, cancel before assignment, limits of the status query. See `VENDOR_QUESTIONS_CR-028.md`. Also untested: long-run / load tests, switching the mode with active tasks.

## 5. Follow-up (owner request 2026-11-21): legs, rollback guard, mode switch

| Check | Result |
|---|---|
| Backend build; frontend `tsc -b`, ESLint, `i18n:check`, `build` | clean |
| `Cr028ModeAndLegTests` (new, 11 cases) | **11 / 11** |
| Focused regression (`Cr028*`, `Cr027*`, `Cr010*` routing / multi-leg / ports, `Cr008*`, `Cr026*`, `Concurrency`, `ProcessFlow`, `Routing`, `Robustness`, `ConfigApi`, `Correlation`, `Station*`) | **315 / 315** |
| Full suite | not run (decision: lean testing) |

New cases: RcsManaged legs sent with no robot, **no AGV row at all**, two legs of one rack kind not serialised, Station Transaction not "AgvAssigned" · legs ignore `DispatchHold` and never use AGV1 / `R-FULL` even when AGV rows exist · multi-leg order (buffer storage) with no AGV on any leg · `AfterPick` destination continued by task code · Legacy legs unchanged (AGV1, `R-FULL`, `AGV_BUSY` queue, `AgvAssigned`) · migration rollback on a scratch database: refused with an active unassigned task, refused with a finished task without AGV (task and history kept, schema unchanged), allowed when nothing blocks (rows kept, `AgvId` NOT NULL again, forward again) · mode switch refused in both directions (tasks and legs), allowed after completion, nothing converted · startup check refuses to start · changed configuration reported as restart pending.

Still not verified with a real RCS: everything listed in section 4; additionally the leg flows of section 7a (no real leg dispatcher exists).

## 6. Follow-up 2 (owner decision 2026-11-21): RobotCode per leg

| Check | Result |
|---|---|
| Backend build; frontend `tsc -b`, ESLint, `i18n:check`, `build` | clean |
| `Cr028LegRobotTests` (new, 9 cases) | **9 / 9** |
| Focused regression (same set as section 5, now also running the changed simulated leg dispatcher: `Cr010*`, `Cr008*`, `Cr026*`, `Cr027*`, `Cr028*`, `Concurrency`, `ProcessFlow`, `Routing`, `Robustness`, `ConfigApi`, `Correlation`, `Station*`) | **324 / 324** |
| Full suite | not run (lean testing) |

Cases: each leg stores its own AMR (two legs over two AMRs, `AgvId` empty, no AGV created, station transaction moves to `AgvAssigned`) · sent is not assigned, assigned later · failing query = one `LEG_ROBOT_SYNC_ERROR`, leg not failed, recovers · robot not in the fleet read model kept and flagged · the Transport Task's robot never overwrites the leg's robot (and the reverse) · a change before the pick is followed; after the pick it is refused and recorded once · consecutive legs keep their own AMR, a difference is only reported · Legacy legs unchanged (AGV1, no `RobotCode`, no RCS query) · rollback of `AddLegRobotCode` on a scratch database keeps every leg, saves the robot codes in `transport_leg_robot_backup` and restores them on re-apply.

Not verified with a real RCS: whether `queryTaskStatus` returns `agvCode` for a leg that waits for `continueTask`, and every question 9–13 of the vendor list. Not implemented (no approved design): hand-over or pinning of the AMR between legs.
