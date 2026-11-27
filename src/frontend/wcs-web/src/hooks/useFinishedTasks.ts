import { useCallback } from 'react'
import { api } from '../api/client'
import { finishedFromOrder, rangeStart, type FinishedTask, type RangeKey } from '../lib/dashboardStats'
import { useApi, type ApiState } from './useApi'

/** CR-053: finished work of both engines; `orders` = how many of the completed ones are transport orders. */
export interface FinishedTasks { completed: FinishedTask[]; failed: FinishedTask[]; completedOrders: number; truncated: boolean }

const PAGE = 200
const MAX_PAGES = 10          // at most 2000 records per state and kind: a bound on dashboard cost, reported to the user when it is reached
const DAY = 86_400_000
/** A task is never open longer than a day in a healthy plant; a transport order may wait (queue, buffer) much longer. */
const TASK_SLACK = DAY
const ORDER_SLACK = 30 * DAY

/**
 * Completed and Failed work that finished since `since`, read through the existing task and order APIs (newest created first). Paging stops when a page is not full or when its oldest
 * record was created longer before the period than that kind of work can stay open.
 */
export async function loadFinishedSince(since: Date): Promise<FinishedTasks> {
  const result: FinishedTasks = { completed: [], failed: [], completedOrders: 0, truncated: false }
  const inPeriod = (x: { finishedAt: string | null }) => !!x.finishedAt && Date.parse(x.finishedAt) >= since.getTime()
  async function read<T extends { createdAt: string; finishedAt: string | null }>(page: (skip: number) => Promise<T[]>, slack: number, take: (x: T) => void) {
    for (let n = 0; n < MAX_PAGES; n++) {
      const items = await page(n * PAGE)
      items.filter(inPeriod).forEach(take)
      const oldest = items.at(-1)
      if (items.length < PAGE || (oldest && Date.parse(oldest.createdAt) < since.getTime() - slack)) return
    }
    result.truncated = true
  }
  for (const state of ['Completed', 'Failed'] as const) {
    const bucket = state === 'Completed' ? result.completed : result.failed
    await read(async (skip) => (await api.tasks(state, skip, PAGE)).items, TASK_SLACK, (x) => bucket.push(x))
    await read(async (skip) => (await api.routingOrders(state, PAGE, skip)).items, ORDER_SLACK, (x) => {
      bucket.push(finishedFromOrder(x))
      if (state === 'Completed') result.completedOrders++
    })
  }
  return result
}

/** Finished tasks and transport orders of the selected period; refreshes when one of them changes state. */
export function useFinishedTasks(range: RangeKey): ApiState<FinishedTasks> {
  const fetcher = useCallback(() => loadFinishedSince(rangeStart(range, new Date())), [range])
  return useApi(fetcher, ['TaskStateChanged', 'OrderChanged'])
}
