/** CR-051: one unit of RCS work as the server derives it: a Phase 1 task, or one leg of a transport order. Read only. */
export type RcsWorkState = 'NotSent' | 'NoAnswer' | 'Rejected' | 'Accepted' | 'Executing' | 'Interrupted' | 'Completed' | 'Failed' | 'Cancelled'
export interface RcsWork {
  kind: 'Task' | 'OrderLeg'
  /** the task id, or the leg id (then orderId is the transport order) */
  id: string; orderId: string | null; legSequence: number | null
  requestKey: string; processCode: string; requestedBy: string | null; executionMode: string | null
  agvId: string | null; agvCode: string | null; robotCode: string | null
  source: string | null; destination: string | null
  workState: string; rcsState: RcsWorkState
  rcsRequestCode: string; rcsTaskCode: string | null; detail: string | null
  createdAt: string; sentAt: string | null; updatedAt: string; pickConfirmedAt: string | null; dropConfirmedAt: string | null; finishedAt: string | null
}
