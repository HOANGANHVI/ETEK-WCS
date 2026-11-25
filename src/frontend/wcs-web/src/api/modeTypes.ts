// CR-038 – operating mode (Simulator / Real AGV) and the RCS message log. Shapes of /api/execution-mode and /api/tasks/{id}/rcs-messages.
export type ExecMode = 'Simulator' | 'Real'
export type MsgDirection = 'Out' | 'In'

export interface ExecutionModeView {
  mode: ExecMode
  /** false until an Admin has chosen a mode (the default row is only a starting value) */
  isExplicit: boolean
  recoveryRequired: boolean
  recoveryReason: string | null
  updatedAt: string
  updatedBy: string
  /** Rcs:RealDispatch:Enabled in the deployment: without it Real AGV cannot be chosen */
  realDispatchConfigured: boolean
}

export interface ModeCondition { code: string; ok: boolean; message: string }
export interface BlockingWork { kind: string; id: string; code: string; state: string; reason: string }
export interface ModeReadiness { current: ExecMode; target: ExecMode; canSwitch: boolean; conditions: ModeCondition[]; blockingWork: BlockingWork[] }
export interface ModeTransitionResult { succeeded: boolean; repeated: boolean; from: ExecMode; to: ExecMode; failed: ModeCondition[]; blockingWork: BlockingWork[] }
export interface ModeTransitionView { id: string; requestId: string; from: ExecMode; to: ExecMode; actor: string; reason: string; succeeded: boolean; detail: string | null; at: string }
export interface ModeTransitionBody { requestId: string; target: ExecMode; reason: string; confirmRealAgv: boolean }

export interface RcsMessageSummary {
  /** arrival order assigned by the server (the timeline order, even for equal timestamps) */
  seq: number; id: string; at: string; mode: ExecMode; direction: MsgDirection; messageType: string; protocol: string
  result: string | null; httpStatus: number | null; errorCode: string | null; requestCode: string | null
}
export interface RcsMessageDetail {
  id: string; taskId: string | null; requestKey: string | null; requestCode: string | null; rcsTaskCode: string | null; mode: ExecMode; direction: MsgDirection
  messageType: string; protocol: string; endpoint: string | null; httpMethod: string | null; httpStatus: number | null; payload: string | null; payloadTruncated: boolean
  result: string | null; errorCode: string | null; errorMessage: string | null; correlationId: string | null; at: string; durationMs: number | null
}
export interface LegacyAdoptionResult { adoptedTasks: number; adoptedOrders: number; notAdopted: BlockingWork[] }
