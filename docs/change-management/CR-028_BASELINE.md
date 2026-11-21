# CR-028 – Baseline recorded BEFORE any code change

Historical record – never edited to follow the code. Results are in `CR-028_RESULTS.md`.

## 1. Git baseline and dependency between CR-027 and CR-028 (decision A1-B)

| Item | Value |
|---|---|
| Branch | `feature/cr-028-dynamic-fleet-dispatch` |
| Created from | `feature/cr-027-dynamic-amr-monitoring` @ `00d24b0` ("CR-027 … fleet panel, docs, traceability and results (Phase 1)") |
| `main` | `a0c4c70` – does **not** contain CR-027 (CR-027 is not merged) |
| Relation | CR-028 is **stacked on CR-027**: it uses the fleet read model (`rcs_amr_snapshots`, `FleetService`, `IRcsFleetQuery`, migration `AddRcsFleetSnapshots`) that exists only on the CR-027 branch. CR-027 is not merged and its history is not changed by this CR; merging CR-028 into `main` later requires CR-027 to be merged first (or together). |
| Working tree | only the uncommitted deletion of `docs/02_Interface_Specification/PLC_IO_Map/ETEK_WCS_IO_Map_4PLC_16Stations_v2_Separate_IO_Sets.xlsx` (never committed) |

## 2. Test baseline (CR-027 branch, `00d24b0`)

Backend **901 / 901** passed (full run, 2026-11-19). Frontend: `tsc -b`, ESLint, `i18n:check`, `build` clean; smoke suites i18n 76, dashboard 18, preferences 10, monitor 23, plcwrite 20, plcmonitor 29. The CR-027 golden statistics test (`Cr027StatisticsBaselineTests`) is the regression guard for Legacy mode in this CR.

## 3. Task lifecycle (unchanged by this CR)

13 states (`WCS.Domain/Tasks/TaskState.cs`, `task-state-machine.md` 3.2): Pending, Validating, Queued, Dispatching, Accepted, Executing, WaitingForPlc, WaitingForDestination, Continuing, Completed, Failed, CancelPending, Cancelled. AGV-occupying states: Dispatching, Accepted, Executing, WaitingForPlc, WaitingForDestination, Continuing, CancelPending. Terminal: Completed, Failed, Cancelled.

## 4. Dispatch behaviour at baseline

- **Assignment at creation:** `TaskOrchestrator.CreateTaskCoreAsync` / `UpdateTaskAsync` choose the AGV by role (`IAgvRepository.GetByRoleAsync`: FullRack → AGV1, EmptyRack → AGV2); `TransportTask.AgvId` is NOT NULL and a foreign key to `agvs`; the AGV's `RcsRobotCode` is sent in `RcsCreateTaskRequest.RobotCode`.
- **PER_AGV:** unique index `ux_transport_tasks_one_active_per_agv` (`AgvId` where `State` in the AGV-occupying states); per-AGV FIFO queue (`TryDispatchNextAsync`, `NextQueuedForAgvAsync`); `DispatchHold` per AGV (`AgvHoldService`); scheduler loop "for each AGV: TryDispatchNext".
- **RCS dispatch:** `DispatchToRcsAsync`: prepare (tx) → RCS call outside any transaction → apply (tx); after an unknown outcome it queries `QueryTaskStatusAsync(requestCode)` before re-sending; backoff by `RcsRetryPolicy`; `MaxDispatchAttempts` then Failed + reconciliation. The request code `WCS-<taskId>` never changes (idempotent). Only implementation of `IRcsClient`: the simulator.
- **Routing legs:** `RouteEngine.DispatchLegAsync` also picks the AGV by role and applies PER_AGV for legs. (Changed for RcsManaged in the follow-up, see `CR-028_DYNAMIC_FLEET_DISPATCH.md` 7a.)
- **Config:** `Scheduler:*` (tick, `MaxDispatchAttempts`, state timeouts, RCS retry delays). No dispatch-mode setting existed.

## 5. Statistics baseline

As in `CR-027_BASELINE.md` section 4 and the golden scenario (CR-027). Dashboard figure "completed tasks per AGV" groups by `TransportTask.AgvId`. Dashboard code hashes: `lib/dashboardStats.ts` sha256 `8dec69567ae01df5f644a3d82cee7a4c6262449cbf3e6d82259670133000ab7f`, `DashboardPage.tsx` `2a265fc591dc2cfae980c663081a295f5fc16fc6b5ebae9ee24d61796231a34a` (of `a0c4c70`; CR-027 did not change them).

## 6. Schema fingerprints (EF snapshot at `00d24b0`)

```
agvs
  sha256 f358152278432105bdb3fdc9dfd59b2446f6facd8184d6c7bc69fa889b26242e
  columns (13): Id:Guid, Code:string, ConnectionStatus:string, DispatchHold:bool, DispatchHoldAt:DateTimeOffset?, DispatchHoldReason:string, DispatchHoldTaskId:Guid?, Enabled:bool, LastStatusAt:DateTimeOffset?, RcsRobotCode:string, Role:string, StatusDetail:string, Version:uint
  indexes: Code | RcsRobotCode | Role
transport_tasks
  sha256 5136c9d4af7e2210af262e20b56d4ba361ea78b40b7bd65a3f19d40162e31a87
  columns (31): Id:Guid, AgvId:Guid, CorrelationId:string, CreatedAt:DateTimeOffset, CurrentLeg:string, DestinationLocationId:Guid?, DispatchAttempts:int, DropConfirmedAt:DateTimeOffset?, FinishedAt:DateTimeOffset?, Flow:string, PickConfirmedAt:DateTimeOffset?, PlcPermissionGranted:bool, RackId:Guid?, RackKind:string, RcsLastAttemptAt:DateTimeOffset?, RcsRequestCode:string, RcsRetryCount:int, RcsTaskCode:string, ReconciliationCompleted:bool, RequestKey:string, RequestLocationId:Guid, RequestedBy:string, RequiresReconciliation:bool, SourceLocationId:Guid?, State:string, StateEnteredAt:DateTimeOffset, StateReason:string, StateReasonArgsJson:string, StateReasonCode:string, UpdatedAt:DateTimeOffset, Version:uint
  indexes: AgvId | CorrelationId | DestinationLocationId | RcsRequestCode | RcsTaskCode | RequestKey | RequestLocationId | SourceLocationId | State | AgvId, State, CreatedAt
transport_legs
  sha256 57f14466ede25522d27901a9eda2c33f942084718b14940c21064711f2e6f81d
  columns (23): Id:Guid, AgvId:Guid?, AgvRequestCode:string, AgvTaskCode:string, CreatedAt:DateTimeOffset, DestinationLocationCode:string, DestinationNoBind:bool, DropConfirmedAt:DateTimeOffset?, FinishedAt:DateTimeOffset?, OrderId:Guid, PickConfirmedAt:DateTimeOffset?, RackId:Guid?, RackKind:string, Sequence:int, SourceLocationCode:string, State:string, StateEnteredAt:DateTimeOffset, StateReason:string, StepKey:string, StepType:string, UpdatedAt:DateTimeOffset, Version:uint, WaitKind:string
  indexes: AgvId | AgvRequestCode | OrderId, Sequence | State, CreatedAt
task_history
  sha256 0d110e179bd250140f9b1ea0c891ed547bf28dbb888d5afed6293a5fe99b1b05
  columns (10): Id:Guid, Detail:string, DetailArgsJson:string, DetailCode:string, EventName:string, FromState:string, OccurredAt:DateTimeOffset, Source:string, TaskId:Guid, ToState:string
  indexes: TaskId, OccurredAt
transport_orders
  sha256 c6f6cd8d611b60ea389a3f9397a4ae18a4485b935f6b1222e539357207bbe232
  columns (22): Id:Guid, CorrelationId:string, CreatedAt:DateTimeOffset, CurrentStepKey:string, FinishedAt:DateTimeOffset?, LegCount:int, ProcessCode:string, ProcessId:Guid, ProcessVersionId:Guid, ProcessVersionNumber:int, RackId:Guid?, RackKind:string, RequestKey:string, RequestLocationCode:string, RequestedBy:string, SnapshotJson:string, State:string, StateEnteredAt:DateTimeOffset, StateReason:string, UpdatedAt:DateTimeOffset, Version:uint, WaitKind:string
  indexes: CorrelationId | ProcessId | ProcessVersionId | RequestKey | State
station_transactions
  sha256 65fc972cf113461a562309f88415f15df63704426edddd2229f98d04748ba6a2
  columns (21): Id:Guid, AgvId:Guid?, CreatedAt:DateTimeOffset, FailureReason:string, FinishedAt:DateTimeOffset?, HandshakeStep:int, LegId:Guid?, LocationCode:string, Operation:string, OrderId:Guid?, PlcId:string, PortId:string, RackKind:string, ReconciliationReason:string, RequestId:string, State:string, Suspended:bool, SuspendedAt:DateTimeOffset?, SuspendedReason:string, UpdatedAt:DateTimeOffset, Version:uint
  indexes: LegId | OrderId | PortId | PlcId, RequestId | PlcId, State
rcs_amr_snapshots
  sha256 935ec8681dc7d0358eb48620ff3bc16a0b8c7fcf4c306ed93485bc0e49a495c1
  columns (18): Source:string, RobotCode:string, Battery:string, ExclType:string, FirstSeenAt:DateTimeOffset, LastSeenAt:DateTimeOffset, MapCode:string, PathJson:string, PodCode:string, PodDir:string, PosX:string, PosY:string, RobotDir:string, RobotIp:string, Speed:string, Status:string, Stop:string, UpdatedAt:DateTimeOffset
  indexes: Source, LastSeenAt
rcs_fleet_state
  sha256 f75a2aee21acec0e33d0d332f28aab6f478bb4d4bc4ff8f8a708dc4f4ac1285b
  columns (9): Source:string, ConsecutiveFailures:int, LastAmrCount:int, LastAttemptAt:DateTimeOffset?, LastAttemptOk:bool, LastErrorCode:string, LastErrorMessage:string, LastLatencyMs:long?, LastSuccessAt:DateTimeOffset?
  indexes:
```

## 7. RCS API facts used by this CR (RCS-2000 API Developer Guide V3.3, `docs/03_RCS_Interface/`)

`agvCode` of `genAgvSchedulingTask` is optional: when not set the RCS selects the AMR (5.2.1, p. 21). The response `data` is "custom content, such as task ID" – it does not carry the robot. The robot is obtained from `queryTaskStatus` (`agvCode`, "exists when the task has been assigned", 5.2.15, p. 51) or from the `robotCode` of `agvCallback` (5.3.1, p. 68). A repeated request must keep the same `reqCode`; Appendix A code 6 = "no need to resend". Open points are listed in `docs/03_RCS_Interface/VENDOR_QUESTIONS_CR-028.md`.
