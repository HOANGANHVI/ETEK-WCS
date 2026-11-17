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
  agvId: string
  requestLocationId: string
  sourceLocationId: string | null
  destinationLocationId: string | null
  rackId: string | null
  state: TaskState
  leg: string
  plcPermissionGranted: boolean
  stateReason: string | null
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
  database: { connected: boolean }
  scheduler: { lastRunAt: string | null; lastError: string | null; stale: boolean }
  integration: { mode: string; plc: string; plcRuntime?: { mode: string; running: boolean; connected: number; total: number } }
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
