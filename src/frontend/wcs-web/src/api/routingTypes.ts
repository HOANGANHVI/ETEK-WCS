// CR-010 DTOs (camelCase JSON, enums as strings). Mirrors WCS.Application.Routing.

export type OrderState = 'Created' | 'Accepted' | 'InProgress' | 'Waiting' | 'Completed' | 'Failed' | 'Cancelled' | 'ReconciliationRequired'
export type LegState = 'Pending' | 'Ready' | 'InProgress' | 'Waiting' | 'Completed' | 'Failed' | 'Cancelled'
export type WaitKind = 'None' | 'DestinationUnavailable_AgvHoldingRack' | 'AtBuffer' | 'SourceUnavailable' | 'AgvBusy' | 'ConditionNotMet' | 'DispatchBlocked' | 'DestinationUnavailable'
export type TransactionState =
  | 'Created' | 'Requested' | 'Accepted' | 'AgvAssigned' | 'AgvApproaching' | 'AtStation' | 'Handling'
  | 'HandoverCompleted' | 'AgvExited' | 'Completed' | 'Failed' | 'ReconciliationRequired'

export const ORDER_STATES: OrderState[] = ['Created', 'Accepted', 'InProgress', 'Waiting', 'Completed', 'Failed', 'Cancelled', 'ReconciliationRequired']

export interface Leg {
  id: string
  sequence: number
  stepKey: string
  stepType: string
  sourceLocationCode: string | null
  destinationLocationCode: string | null
  rackKind: string
  rackId: string | null
  agvId: string | null
  agvTaskCode: string | null
  /** CR-028: the AMR the RCS chose for this leg (RcsManaged); null until the RCS reports it, always null for Legacy legs */
  robotCode?: string | null
  assignedAt?: string | null
  state: LegState
  waitKind: WaitKind
  stateReason: string | null
  rackOnAgv: boolean
  pickConfirmedAt: string | null
  dropConfirmedAt: string | null
  createdAt: string
  finishedAt: string | null
}

export interface StationTransaction {
  id: string
  portId: string
  plcId: string
  locationCode: string
  operation: 'Pickup' | 'Delivery'
  orderId: string | null
  legId: string | null
  requestId: string | null
  state: TransactionState
  handshakeStep: number
  agvId: string | null
  suspended: boolean
  suspendedReason: string | null
  reconciliationReason: string | null
  failureReason: string | null
  createdAt: string
  updatedAt: string
  finishedAt: string | null
}

export interface Order {
  id: string
  requestKey: string
  processCode: string
  processVersion: number
  rackKind: string
  requestLocationCode: string
  state: OrderState
  waitKind: WaitKind
  stateReason: string | null
  currentStepKey: string | null
  rackId: string | null
  legs: Leg[]
  transactions: StationTransaction[]
  createdAt: string
  updatedAt: string
  finishedAt: string | null
}

export interface RoutingEvent { id: string; legId: string | null; transactionId: string | null; kind: string; detail: string | null; source: string; occurredAt: string }
export interface OrderDetail { order: Order; events: RoutingEvent[] }
export interface OrderPage { total: number; items: Order[] }

// ---- process configuration
export interface ConditionDef { type: string; parameter?: string | null; locationCode?: string | null }
export interface CandidateDef { role: 'Source' | 'Destination'; priority: number; order: number; locationCode: string; conditions?: ConditionDef[] | null; noBind?: boolean; note?: string | null }
export interface StepDef {
  stepKey: string
  order: number
  type: string
  sourceMode?: string | null
  destinationMode?: string | null
  resolution?: string | null
  nextStepKey?: string | null
  alternativeNextStepKey?: string | null
  waitBehavior?: string | null
  errorBehavior?: string | null
  conditions?: ConditionDef[] | null
  waitingPositionCode?: string | null
  description?: string | null
  candidates?: CandidateDef[] | null
}
export interface VersionDefinition { firstStepKey: string; steps: StepDef[]; note?: string | null }
export interface ProcessIssue { severity: 'Warning' | 'Error'; code: string; message: string; stepKey?: string | null }

export interface VersionSummary {
  id: string; number: number; state: 'Draft' | 'Validated' | 'Published' | 'Disabled'; createdAt: string; createdBy: string | null
  validatedAt: string | null; publishedAt: string | null; publishedBy: string | null; disabledAt: string | null; note: string | null; stepCount: number
}
export interface Process {
  id: string; code: string; name: string; description: string | null; requestType: string; rackKind: string; triggerLocationCodes: string[]
  enabled: boolean; reviewBlocked: boolean; reviewReason: string | null; sourceReference: string | null
  publishedVersion: number | null; latestVersion: number; versions: VersionSummary[]
}
export interface ProcessVersion {
  id: string; processId: string; processCode: string; number: number; state: VersionSummary['state']; editable: boolean; note: string | null
  definition: VersionDefinition; issues: ProcessIssue[]; validatedAt: string | null
}
export interface ProcessHeaderBody { code: string; name: string; description: string | null; requestType: string; rackKind: string; triggerLocationCodes: string[]; enabled?: boolean }
export interface ImportResult { imported: number; skipped: number; importedCodes: string[] }
export interface AddPlcBody { plcId: string; name: string; ipAddress: string; stations: { stationId: string; locationCode: string | null }[] }
export interface PortView { stationId: string; plcId: string; order: number; locationCode: string | null; enabled: boolean; role: string; pointCount: number; configuredPoints: number; approvedPoints: number }
/** A published process as the request forms see it (CR-039). `acceptsRequests` is the server's answer; the form only shows it. */
export interface StartableProcess {
  code: string; name: string; requestType: string; rackKind: string; triggerLocationCodes: string[]
  readiness: string; acceptsRequests: boolean; blockedCode: string | null; blockedDetail: string | null
}
