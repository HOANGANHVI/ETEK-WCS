// Mirrors the DTOs of the WCS API (camelCase JSON, enums as strings).

export interface ApiError {
  code: string
  message: string
  details?: unknown
}

export interface ApiResponse<T> {
  success: boolean
  data: T | null
  error: ApiError | null
  traceId: string
}

export type TaskState =
  | 'Pending' | 'Validating' | 'Queued' | 'Dispatching' | 'Accepted' | 'Executing'
  | 'WaitingForPlc' | 'WaitingForDestination' | 'Continuing' | 'Completed' | 'Failed'
  | 'CancelPending' | 'Cancelled'

export const TASK_STATES: TaskState[] = [
  'Pending', 'Validating', 'Queued', 'Dispatching', 'Accepted', 'Executing', 'WaitingForPlc',
  'WaitingForDestination', 'Continuing', 'Completed', 'Failed', 'CancelPending', 'Cancelled',
]

export interface Agv {
  id: string
  code: string
  role: 'FullRack' | 'EmptyRack'
  rcsRobotCode: string | null
  enabled: boolean
  connectionStatus: 'Unknown' | 'Online' | 'Offline' | 'Error'
  statusDetail: string | null
  lastStatusAt: string | null
  activeTaskId: string | null
  busy: boolean
  dispatchHold: boolean                 // CR-008B-5
  dispatchHoldReason: string | null
  dispatchHoldAt: string | null
  dispatchHoldTaskId: string | null
}

export interface Location {
  id: string
  code: string
  name: string
  type: string
  rackCapacity: number
  allowedRackKind: string
  enabled: boolean
  requiresPlcPermission: boolean
  occupied: number
  reserved: number
  occupancy: 'Empty' | 'Occupied' | 'Unknown' | 'Reserved'
}

export interface Rack {
  id: string
  externalRackId: string | null
  rackType: string | null
  status: 'Unknown' | 'Full' | 'Empty' | 'InTransit'
  currentLocationId: string | null
  currentTaskId: string | null
  lastConfirmedAt: string
}

export interface InventoryTransaction {
  id: string
  eventType: string
  rackId: string | null
  fromLocationId: string | null
  toLocationId: string | null
  taskId: string | null
  statusBefore: string | null
  statusAfter: string | null
  source: string
  userName: string | null
  bind: string
  note: string | null
  occurredAt: string
}

export interface TransportTask {
  id: string
  requestKey: string
  flow: string
  rackKind: string
  /** CR-028: null while no business AGV row is linked (RcsManaged dispatch: the RCS chooses the AMR). Never means "AGV1". */
  agvId: string | null
  /** CR-028: the AMR the RCS reported for the task (exactly as reported); null until the RCS assigned one. */
  robotCode?: string | null
  /** CR-028: NotSent | Sending | Accepted | AwaitingAssignment | Assigned | SyncError; null for Legacy dispatch and for older tasks. */
  rcsDispatchState?: string | null
  assignedAt?: string | null
  requestLocationId: string
  sourceLocationId: string | null
  destinationLocationId: string | null
  rackId: string | null
  state: TaskState
  leg: string
  plcPermissionGranted: boolean
  stateReason: string | null
  /** Stable code of the state reason and its parameters (null: show `stateReason` as it is). */
  stateReasonCode?: string | null
  stateReasonArgs?: Record<string, string> | null
  rcsRequestCode: string
  rcsTaskCode: string | null
  requiresReconciliation: boolean
  pickConfirmedAt: string | null
  dropConfirmedAt: string | null
  createdAt: string
  updatedAt: string
  finishedAt: string | null
}

/** CR-008B-3: what an Admin needs to reconcile a task (GET /api/tasks/{id}/reconciliation). */
export interface ReconciliationDetail {
  task: TransportTask
  rcsRetryCount: number
  rcsLastAttemptAt: string | null
  rcsEvents: TaskHistory[]
  reservations: { kind: string; status: string; locationCode: string | null; rackId: string | null }[]
  rack: { id: string; status: string; currentLocationCode: string | null; lastConfirmedAt: string } | null
  locations: { code: string; reconciliationRequired: boolean }[]
  canReconcile: boolean
}

export interface TaskHistory {
  id: string
  fromState: string | null
  toState: string | null
  event: string
  detail: string | null
  /** Stable code of the detail + parameters (null: show `detail` as it is). */
  detailCode?: string | null
  detailArgs?: Record<string, string> | null
  source: string
  occurredAt: string
}

export interface Paged<T> {
  items: T[]
  total: number
  skip: number
  take: number
}

export interface SystemHealth {
  status: 'Healthy' | 'Degraded'
  time: string
  /** Application health only. PLC / RCS / AGV state is in `integration` and the AGV list; server resources are a separate API. */
  backend?: { startedAt: string; uptimeSeconds: number }
  database: { connected: boolean; latencyMs?: number | null }
  scheduler: { lastRunAt: string | null; lastError: string | null; stale: boolean }
  integration: { mode: string; plc: string; /** what the server says about dispatching to the RCS (undefined on older servers) */ rcs?: { dispatchAllowed: boolean; code: string }; plcRuntime?: { mode: string; running: boolean; connected: number; total: number } }
}

export interface SimulationStep {
  signal: string | null
  message: string
  task: TransportTask
}

/** A process flow that users may request, with the stations allowed to request it (read from the deployed configuration). */
export interface FlowOption {
  flow: string
  rackKind: string
  stations: string[]
}

// ---- Server Resource Monitoring (GET /api/system/resources, Admin only)
export type MetricStatus = 'Normal' | 'Warning' | 'Critical' | 'Unavailable'
/** Container = the WCS container; DockerVm = the Linux VM that runs containers on Docker Desktop (NOT the Windows/macOS host); Host = the machine the OS really runs on. */
export type MetricSource = 'Container' | 'DockerVm' | 'Host'

export interface ResourceMetric {
  value: number | null
  unit: string
  source: MetricSource
  status: MetricStatus
  lastUpdatedAt: string
  /** Stable reason code when the metric is Unavailable (translated in the UI). */
  reason: string | null
}

export interface ResourceEnvironment {
  platform: string
  operatingSystem: string
  architecture: string
  inContainer: boolean
  containerRuntime: string | null
  hostname: string | null
  containerHostname: string | null
  hostScope: MetricSource
  hostAgentRequired: boolean
  processorNote: string | null
}

export interface ResourceHistoryPoint { t: string; containerCpu: number | null; containerMemory: number | null; hostCpu: number | null; hostMemory: number | null }

export interface ResourceSnapshot {
  time: string
  sampledAt: string | null
  sampleAgeSeconds: number | null
  intervalSeconds: number
  enabled: boolean
  environment: ResourceEnvironment
  container: Record<string, ResourceMetric>
  host: Record<string, ResourceMetric>
  history: ResourceHistoryPoint[]
}
