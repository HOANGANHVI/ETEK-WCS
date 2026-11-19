# CR-027 – Baseline recorded BEFORE any code change

Recorded on the new branch `feature/cr-027-dynamic-amr-monitoring` before the first source change. **This file is a historical record: it is never edited to follow the code.** Later results are written to `CR-027_DYNAMIC_AMR_MONITORING.md`.

## 1. Git baseline

| Item | Value |
|---|---|
| `main` (and `origin/main`) | `a0c4c70` – "Merge branch 'develop' into main: CR-026 configuration export/import, Maintenance" |
| `develop` | `5b9f3ba` (same content as `main`) |
| Branch created from | `main` @ `a0c4c70` |
| Working tree | only the (uncommitted, never to be committed) deletion of `docs/02_Interface_Specification/PLC_IO_Map/ETEK_WCS_IO_Map_4PLC_16Stations_v2_Separate_IO_Sets.xlsx`; `docs/03_RCS_Interface/` (the API PDF) is new in this CR |

## 2. Test baseline (unchanged code, `a0c4c70`)

- Backend: **869 / 869 passed** (`dotnet test tests/WCS.Tests`, run on `main` after the CR-026 merge, 2026-11-19).
- Frontend on the same commit: `tsc -b` 0 errors, ESLint 0 problems, `npm run i18n:check` OK, `npm run build` OK, six smoke suites passed (i18n 76, dashboard 18, preferences 10, monitor 23, plcwrite 20, plcmonitor 29).
- Added in this baseline step (test only, no production code): `Cr027StatisticsBaselineTests` = 1 test → backend total **870** before the first CR-027 code change.

## 3. Modules relevant to CR-027 (state at baseline)

| Module | State at baseline |
|---|---|
| AGV domain (`WCS.Domain/Agvs/Agv.cs`) | Configuration row per AGV: `Code`, `Role` (FullRack = AGV1, EmptyRack = AGV2; unique), `RcsRobotCode`, `Enabled`, `ConnectionStatus`, `DispatchHold*`. Seed: AGV1 / AGV2 (`WcsDataSeeder`). |
| Task assignment | `TaskOrchestrator` (create + update) and `RouteEngine.DispatchLegAsync` choose the AGV by role (`IAgvRepository.GetByRoleAsync`); `TransportTask.AgvId` required FK; unique "one active task per AGV" indexes on tasks and legs; per-AGV queue and `DispatchHold`. **Not touched by CR-027 Phase 1.** |
| RCS client | `IRcsClient` (create / continue / cancel / query task / query ONE agv by robot code). Only implementation: `RcsSimulator` (+ `LoggingRcsClient` decorator). No real RCS HTTP adapter. `RcsCreateTaskRequest.RobotCode` carries the AGV's `RcsRobotCode`. |
| AGV status polling | `AgvStatusHostedService` (10 s) → `AgvStatusService.RefreshAsync`: for each `agvs` row with a robot code, `QueryAgvStatusAsync`, updates `agvs.ConnectionStatus`, publishes `AgvStatusChanged`. |
| AGV Monitoring UI | `AgvPage` lists the `agvs` rows (`GET /api/agvs`). |
| Dashboard / KPI | Frontend only, computed from task timestamps (`lib/dashboardStats.ts`, sha256 `8dec69567ae01df5f644a3d82cee7a4c6262449cbf3e6d82259670133000ab7f`; `DashboardPage.tsx` sha256 `2a265fc591dc2cfae980c663081a295f5fc16fc6b5ebae9ee24d61796231a34a`). |

## 4. Dashboard / KPI / transport-statistics formulas at baseline (`dashboardStats.ts`)

- A task counts in a period by the time it **finished** (`finishedAt`, browser-local time).
- Failure rate = Failed / (Completed + Failed) of tasks finished in the period; Cancelled, waiting and running tasks are excluded; null (not 0 %) when none finished.
- Cycle time = `finishedAt − createdAt` of Completed tasks (queue waiting included); unfinished tasks are not counted.
- Per-AGV figure: number of Completed tasks per `agvId` of the task (`completedPerAgv`).
- Trips / legs: `transport_legs` rows (multi-leg orders) and tasks; neither uses the number of AMRs.
- No backend statistics table exists; every figure is derived from `transport_tasks`, `transport_orders`, `transport_legs`, `station_transactions`, `inventory_transactions`.

## 5. Schema fingerprints at baseline (EF model snapshot of `a0c4c70`)

The sha256 is that of the table's block in `WcsDbContextModelSnapshot.cs`. After CR-027 the blocks of these tables must be **byte-identical** (proof that no existing table changed).

```
agvs
  sha256 f358152278432105bdb3fdc9dfd59b2446f6facd8184d6c7bc69fa889b26242e
  columns (13): Id:Guid, Code:string, ConnectionStatus:string, DispatchHold:bool, DispatchHoldAt:DateTimeOffset?, DispatchHoldReason:string, DispatchHoldTaskId:Guid?, Enabled:bool, LastStatusAt:DateTimeOffset?, RcsRobotCode:string, Role:string, StatusDetail:string, Version:uint
  indexes: Code | RcsRobotCode | Role
transport_tasks
  sha256 5136c9d4af7e2210af262e20b56d4ba361ea78b40b7bd65a3f19d40162e31a87
  columns (31): Id:Guid, AgvId:Guid, CorrelationId:string, CreatedAt:DateTimeOffset, CurrentLeg:string, DestinationLocationId:Guid?, DispatchAttempts:int, DropConfirmedAt:DateTimeOffset?, FinishedAt:DateTimeOffset?, Flow:string, PickConfirmedAt:DateTimeOffset?, PlcPermissionGranted:bool, RackId:Guid?, RackKind:string, RcsLastAttemptAt:DateTimeOffset?, RcsRequestCode:string, RcsRetryCount:int, RcsTaskCode:string, ReconciliationCompleted:bool, RequestKey:string, RequestLocationId:Guid, RequestedBy:string, RequiresReconciliation:bool, SourceLocationId:Guid?, State:string, StateEnteredAt:DateTimeOffset, StateReason:string, StateReasonArgsJson:string, StateReasonCode:string, UpdatedAt:DateTimeOffset, Version:uint
  indexes: AgvId | CorrelationId | DestinationLocationId | RcsRequestCode | RcsTaskCode | RequestKey | RequestLocationId | SourceLocationId | State | AgvId, State, CreatedAt
task_history
  sha256 0d110e179bd250140f9b1ea0c891ed547bf28dbb888d5afed6293a5fe99b1b05
  columns (10): Id:Guid, Detail:string, DetailArgsJson:string, DetailCode:string, EventName:string, FromState:string, OccurredAt:DateTimeOffset, Source:string, TaskId:Guid, ToState:string
  indexes: TaskId, OccurredAt
transport_orders
  sha256 c6f6cd8d611b60ea389a3f9397a4ae18a4485b935f6b1222e539357207bbe232
  columns (22): Id:Guid, CorrelationId:string, CreatedAt:DateTimeOffset, CurrentStepKey:string, FinishedAt:DateTimeOffset?, LegCount:int, ProcessCode:string, ProcessId:Guid, ProcessVersionId:Guid, ProcessVersionNumber:int, RackId:Guid?, RackKind:string, RequestKey:string, RequestLocationCode:string, RequestedBy:string, SnapshotJson:string, State:string, StateEnteredAt:DateTimeOffset, StateReason:string, UpdatedAt:DateTimeOffset, Version:uint, WaitKind:string
  indexes: CorrelationId | ProcessId | ProcessVersionId | RequestKey | State
transport_legs
  sha256 57f14466ede25522d27901a9eda2c33f942084718b14940c21064711f2e6f81d
  columns (23): Id:Guid, AgvId:Guid?, AgvRequestCode:string, AgvTaskCode:string, CreatedAt:DateTimeOffset, DestinationLocationCode:string, DestinationNoBind:bool, DropConfirmedAt:DateTimeOffset?, FinishedAt:DateTimeOffset?, OrderId:Guid, PickConfirmedAt:DateTimeOffset?, RackId:Guid?, RackKind:string, Sequence:int, SourceLocationCode:string, State:string, StateEnteredAt:DateTimeOffset, StateReason:string, StepKey:string, StepType:string, UpdatedAt:DateTimeOffset, Version:uint, WaitKind:string
  indexes: AgvId | AgvRequestCode | OrderId, Sequence | State, CreatedAt
station_transactions
  sha256 65fc972cf113461a562309f88415f15df63704426edddd2229f98d04748ba6a2
  columns (21): Id:Guid, AgvId:Guid?, CreatedAt:DateTimeOffset, FailureReason:string, FinishedAt:DateTimeOffset?, HandshakeStep:int, LegId:Guid?, LocationCode:string, Operation:string, OrderId:Guid?, PlcId:string, PortId:string, RackKind:string, ReconciliationReason:string, RequestId:string, State:string, Suspended:bool, SuspendedAt:DateTimeOffset?, SuspendedReason:string, UpdatedAt:DateTimeOffset, Version:uint
  indexes: LegId | OrderId | PortId | PlcId, RequestId | PlcId, State
reservations
  sha256 1bd8332ba10215b9b310f15e46b1fde8c58b9fcf47eb9e382dce5f135362643e
  columns (8): Id:Guid, ClosedAt:DateTimeOffset?, CreatedAt:DateTimeOffset, Kind:string, LocationId:Guid?, RackId:Guid?, Status:string, TaskId:Guid
  indexes: RackId | TaskId | LocationId, Status
racks
  sha256 ae0b7bc13d957adfa1bec5d0fd8d2168a8206873ece8a59738a859b8f0ed0605
  columns (8): Id:Guid, CurrentLocationId:Guid?, CurrentTaskId:Guid?, ExternalRackId:string, LastConfirmedAt:DateTimeOffset, RackType:string, Status:string, Version:uint
  indexes: CurrentLocationId | ExternalRackId
locations
  sha256 9c77ef72440fc7c033aae93b4fd6c94f96324358e57fa34fee0d2a348bc91c7d
  columns (11): Id:Guid, AllowedRackKind:string, Code:string, Enabled:bool, LastInventoryChangeAt:DateTimeOffset, Name:string, RackCapacity:int, ReconciliationRequired:bool, RequiresPlcPermission:bool, Type:string, Version:uint
  indexes: Code
inventory_transactions
  sha256 ac6d1d6a54246cfbdee20b039819e93bca0fa23819fbb9ef011d3ccde3d28b71
  columns (13): Id:Guid, Bind:string, EventType:string, FromLocationId:Guid?, Note:string, OccurredAt:DateTimeOffset, RackId:Guid?, Source:string, StatusAfter:string, StatusBefore:string, TaskId:Guid?, ToLocationId:Guid?, UserName:string
  indexes: FromLocationId | OccurredAt | TaskId | ToLocationId
station_requests
  sha256 adf5835a633034857ac08bf9967013b1ad0df1da1ab8c8e8ed05d91af52f779e
  columns (12): Id:Guid, Attempts:int, DecidedAt:DateTimeOffset?, FirstSeenAt:DateTimeOffset, Flow:string, LastAttemptAt:DateTimeOffset?, LastError:string, RequestKey:string, StationCode:string, Status:string, TaskId:Guid?, Version:uint
  indexes: RequestKey | Status, FirstSeenAt
```

## 6. Statistics baseline (golden)

Scenario (`StatsScenario` in `Cr027StatisticsBaselineTests`): AGV1 task completed, AGV2 task completed, a queued AGV1 task cancelled (RCS confirms), one routing order completed (1 leg), one routing order cancelled. Result captured on unchanged code (identical in two runs):

```json
{"tasks":{"Cancelled":1,"Completed":2},"completedPerAgv":{"AGV1":1,"AGV2":1},"history":{"DESTINATION_RESERVED":2,"PICK_CONFIRMED":2,"RCS_CANCEL_REQUESTED":1,"RECONCILIATION_REQUIRED":1,"SOURCE_RESERVED":3,"STATE_CHANGED":22,"TASK_CREATED":3},"orders":{"Cancelled":1,"Completed":1},"legs":{"Completed":1},"tx":{"HandoverCompleted":2},"inventoryTx":14,"racks":{"Empty":3,"Full":5},"reservations":{"Active":1,"Consumed":6},"agvs":["AGV1/FullRack/R-FULL/True/False","AGV2/EmptyRack/R-EMPTY/True/False"]}
```

## 7. RCS API document

`docs/03_RCS_Interface/UD35865B_RCS-2000 API_Developer Guide_V3.3_20231204.pdf` – sha256 `8f73359f8136ab214bf855c7ebd41b6c76686253bc94b3571137984656b76d63`, 1 956 625 bytes (file date 2025-01-21; document version V3.3, 2023-12-04). Added to the repository by this CR.
