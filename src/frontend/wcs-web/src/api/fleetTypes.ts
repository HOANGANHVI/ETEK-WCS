/** CR-027 – AMR fleet as reported by the RCS (queryAgvStatus). Values are the API strings; units: X/Y mm, speed mm/s, direction degrees, battery percent. */
export type FleetDataState = 'Fresh' | 'Stale' | 'Missing'
export type FleetConnection = 'Connected' | 'Degraded' | 'Disconnected' | 'NoData'

export interface FleetAmr {
  robotCode: string; state: FleetDataState
  robotDir: string | null; robotIp: string | null; battery: string | null; posX: string | null; posY: string | null; mapCode: string | null; speed: string | null
  status: string | null; exclType: string | null; stop: string | null; podCode: string | null; podDir: string | null; path: string[]
  firstSeenAt: string; lastSeenAt: string
  linkedAgvCode: string | null; activeTaskId: string | null; activeTaskState: string | null
}

export interface FleetView {
  source: 'Simulator' | 'Real'; simulatedData: boolean; connection: FleetConnection
  lastAttemptAt: string | null; lastSuccessAt: string | null; lastErrorCode: string | null; lastErrorMessage: string | null; consecutiveFailures: number
  intervalSeconds: number; staleAfterSeconds: number; count: number; freshCount: number; amrs: FleetAmr[]
  /** CR-028 */
  dispatchMode?: 'Legacy' | 'RcsManaged'; maxInFlightTasks?: number; tasksInFlight?: number; tasksAwaitingAssignment?: number; tasksQueued?: number
  /** CR-028: the mode configured now when it differs from the running one (restart pending) */
  dispatchModePending?: string | null
}
