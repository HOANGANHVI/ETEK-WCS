// CR-035 – Admin manual PLC simulation (PLC only; the RCS is always the real one).
export interface SimStatus {
  active: boolean
  activatedBy: string | null
  activatedAt: string | null
  executionMode: 'Simulator' | 'Real'
  realRcsDispatchEnabled: boolean
  enforceReadiness: boolean
  banner: string
  mappingSource: string
  notes: string[]
}

/** CR-040: one station with its PLC → WCS signals and their simulated values */
export interface SimSignal { signal: string; dataType: string; description: string; value: string | null; changedAt: string | null; readByIntake: boolean; address?: string | null }
export interface SimStation {
  engine?: 'Unassigned' | 'Task' | 'TransportOrder' | 'Invalid'
  stationId: string; plcId: string; locationCode: string; plcOnline: boolean | null; portMappingApproved: boolean
  /** CR-050: a physical PLC of the port is connected: the station is not simulated. A simulated PLC does not block. */
  blocked?: boolean
  activeTransaction: { id: string; state: string; handshakeStep: number; requestId: string | null; operation: string } | null
  signals: SimSignal[]
}

export interface SimTrigger {
  locationCode: string
  portId: string | null
  plcId: string | null
  portMappingApproved: boolean
  plcOnline: boolean | null
  portBusy: boolean
  blocked?: boolean
}

export interface SimFlow {
  code: string
  name: string
  requestType: string
  rackKind: string
  publicationState: string
  readiness: string | null
  publishedVersion: number | null
  triggers: SimTrigger[]
  startableNow: boolean
  notStartableReason: string | null
}

export interface SimStartResult {
  outcome: string
  code: string
  message: string
  requestId: string
}

/** CR-050: the AMR simulation behind the simulated RCS. */
export type AmrFault = 'RejectDispatch' | 'DispatchTimeout' | 'MoveFailure' | 'PickFailure' | 'DropFailure' | 'ExecutionStall' | 'ResponseDelay' | 'DuplicateResponse'
export interface AmrRobot { robotCode: string; x: number; y: number; battery: number; state: string; requestCode: string | null }
export interface AmrRun { requestCode: string; kind: 'Leg' | 'Task'; parentTask: string; robotCode: string | null; source: string | null; destination: string | null; phase: string; stalled: boolean; failure: string | null; events: string[] }
export interface AmrSimulation { enabled: boolean; speedMultiplier: number; connected: boolean; responseDelaySeconds: number; armedFaults: Record<string, number>; robots: AmrRobot[]; runs: AmrRun[] }
