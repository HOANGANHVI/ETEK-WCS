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

Everything real: the create request without `agvCode`, the `queryTaskStatus` answer and when `agvCode` appears, callbacks, behaviour on a repeated `reqCode`, robot changes, queued tasks per robot, cancel before assignment, limits of the status query. See `VENDOR_QUESTIONS_CR-028.md`. Also untested: multi-leg orders in RcsManaged mode (not implemented), long-run / load tests, switching the mode with active tasks.
