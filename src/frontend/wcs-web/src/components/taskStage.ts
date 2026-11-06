import type { TransportTask } from '../api/types'

/** Position of a task in the logical flow, derived only from fields the API already returns. */
export function taskStage(t: TransportTask): number {
  if (t.state === 'Completed') return 5
  if (['Pending', 'Validating', 'Queued', 'Dispatching'].includes(t.state)) return 0
  if (!t.pickConfirmedAt) return 1
  if (t.state === 'WaitingForPlc' && t.leg === 'Drop') return 3
  return 2
}
