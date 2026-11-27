// CR-051: pure helpers that put Phase 1 tasks and transport orders side by side. Nothing is copied or created: both stay the records of their own engine.
import type { Order } from '../api/routingTypes'
import type { TransportTask } from '../api/types'
import type { RcsWork, RcsWorkState } from '../api/workTypes'

export type WorkRow =
  | { kind: 'task'; id: string; createdAt: string; task: TransportTask }
  | { kind: 'order'; id: string; createdAt: string; order: Order }

export const orderCode = (id: string) => `LVC-${id.replaceAll('-', '').slice(0, 8).toUpperCase()}`

export function workRows(tasks: TransportTask[], orders: Order[]): WorkRow[] {
  return [
    ...tasks.map((task): WorkRow => ({ kind: 'task', id: task.id, createdAt: task.createdAt, task })),
    ...orders.map((order): WorkRow => ({ kind: 'order', id: order.id, createdAt: order.createdAt, order })),
  ].sort((a, b) => b.createdAt.localeCompare(a.createdAt))
}

/** First source and last known destination of an order; before a leg exists the requesting location is the only fact. */
export function orderRoute(o: Order): { source: string; destination: string | null } {
  const legs = [...o.legs].sort((a, b) => a.sequence - b.sequence)
  return { source: legs[0]?.sourceLocationCode ?? o.requestLocationCode, destination: legs.at(-1)?.destinationLocationCode ?? null }
}

/** The AMRs of an order: what the RCS reported per leg, otherwise the AGV WCS assigned. */
export function orderRobots(o: Order, agvCode: (id: string | null) => string): string {
  return [...new Set(o.legs.map((l) => l.robotCode ?? (l.agvId ? agvCode(l.agvId) : null)).filter((x): x is string => !!x))].join(', ')
}

export interface WorkFilter { agvId: string; agvRobotCode: string | null; search: string; reconOnly: boolean; taskCodeOf: (id: string) => string }

export function filterWork(rows: WorkRow[], f: WorkFilter): WorkRow[] {
  const q = f.search.trim().toLowerCase()
  return rows.filter((r) => {
    if (f.agvId) {
      const mine = r.kind === 'task' ? r.task.agvId === f.agvId : r.order.legs.some((l) => l.agvId === f.agvId || (!!f.agvRobotCode && l.robotCode === f.agvRobotCode))
      if (!mine) return false
    }
    if (f.reconOnly && !(r.kind === 'task' ? r.task.requiresReconciliation : r.order.state === 'ReconciliationRequired' || !!r.order.racksHeld)) return false
    if (!q) return true
    const code = r.kind === 'task' ? f.taskCodeOf(r.id) : orderCode(r.id)
    const key = r.kind === 'task' ? r.task.requestKey : r.order.requestKey
    return [code, key, r.id].some((x) => x.toLowerCase().includes(q))
  })
}

const OPEN_RCS: RcsWorkState[] = ['NoAnswer', 'Accepted', 'Executing', 'Interrupted']
/** Work that is with the RCS now. */
export const isWithRcs = (w: RcsWork) => OPEN_RCS.includes(w.rcsState)
/** Created in WCS, not finished, and never sent. */
export const isWaitingToBeSent = (w: RcsWork) => w.rcsState === 'NotSent' && !['Completed', 'Failed', 'Cancelled'].includes(w.workState)
/** The work of one AGV: by the AGV WCS assigned, otherwise by the robot code the RCS reported. */
export const workOfAgv = (work: RcsWork[], agv: { id: string; rcsRobotCode: string | null }) =>
  work.filter((w) => (w.agvId ? w.agvId === agv.id : !!agv.rcsRobotCode && w.robotCode === agv.rcsRobotCode))

export const RCS_STATE_TONE: Record<RcsWorkState, 'ok' | 'info' | 'warn' | 'bad' | 'muted'> = {
  NotSent: 'muted', NoAnswer: 'warn', Rejected: 'bad', Accepted: 'info', Executing: 'info', Interrupted: 'bad', Completed: 'ok', Failed: 'bad', Cancelled: 'muted',
}
/** Where the record of a unit of work is opened (task management lists both). */
export const workLink = (w: RcsWork) => (w.kind === 'Task' ? `/tasks?id=${w.id}` : `/tasks?order=${w.orderId}`)
