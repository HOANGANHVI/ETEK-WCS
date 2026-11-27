// CR-050: pure helpers of the AMR simulation panel.
import type { AmrRun } from '../api/simTypes'

export const AMR_FAULTS = ['RejectDispatch', 'DispatchTimeout', 'MoveFailure', 'PickFailure', 'DropFailure', 'ExecutionStall', 'ResponseDelay', 'DuplicateResponse'] as const

/** What "recover" can do for a run: a stalled or failed leg goes on; a failed task is already ended by WCS; a leg that waits for a lost destination trigger asks again. */
export function canRecover(r: Pick<AmrRun, 'kind' | 'phase' | 'stalled'>): boolean {
  return r.stalled || (r.kind === 'Leg' && (r.phase === 'Failed' || r.phase === 'WaitingForTrigger'))
}
