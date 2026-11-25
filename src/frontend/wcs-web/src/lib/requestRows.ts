import type { Order } from '../api/routingTypes'
import type { TransportTask } from '../api/types'

export interface RequestRow {
  id: string; requestKey: string; kind: 'task' | 'order'; process: string; state: string; createdAt: string; link: string
  queue: { status: string; position: number; scope: string | null } | null
  legs: number | null; robots: string; source: TransportTask | Order
}

/** CR-040: legacy tasks and transport orders as one list of root requests, newest first. A value the server does not give stays empty; nothing is guessed. */
export function requestRows(tasks: TransportTask[], orders: Order[]): RequestRow[] {
  const fromTask = (x: TransportTask): RequestRow => ({ id: x.id, requestKey: x.requestKey, kind: 'task', process: x.flow, state: x.state, createdAt: x.createdAt, link: `/tasks?id=${x.id}`, queue: null, legs: null, robots: x.robotCode ?? '', source: x })
  const fromOrder = (o: Order): RequestRow => ({
    id: o.id, requestKey: o.requestKey, kind: 'order', process: o.processCode, state: o.state, createdAt: o.createdAt, link: `/routing?order=${o.id}`,
    queue: o.queueSequence != null && o.queueStatus ? { status: o.queueStatus, position: o.queueSequence, scope: o.queueScope ?? null } : null,
    legs: o.legs.length, robots: [...new Set(o.legs.map((l) => l.robotCode).filter(Boolean))].join(', '), source: o,
  })
  return [...tasks.map(fromTask), ...orders.map(fromOrder)].sort((a, b) => b.createdAt.localeCompare(a.createdAt))
}
