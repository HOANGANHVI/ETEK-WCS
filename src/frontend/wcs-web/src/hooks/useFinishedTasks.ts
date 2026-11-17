import { useCallback } from 'react'
import { api } from '../api/client'
import type { TransportTask } from '../api/types'
import { rangeStart, type RangeKey } from '../lib/dashboardStats'
import { useApi, type ApiState } from './useApi'

export interface FinishedTasks { completed: TransportTask[]; failed: TransportTask[]; truncated: boolean }

const PAGE = 200
const MAX_PAGES = 10          // at most 2000 tasks per state: a bound on dashboard cost, reported to the user when it is reached
const DAY = 86_400_000

/**
 * Completed and Failed tasks that finished since `since`, read through the existing task API (newest created first). Paging stops when a page is not full or when its oldest task
 * was created more than a day before the period (a task is never open that long between creation and finish in a healthy plant; anything older could not change the figures).
 */
export async function loadFinishedSince(since: Date): Promise<FinishedTasks> {
  const result: FinishedTasks = { completed: [], failed: [], truncated: false }
  for (const state of ['Completed', 'Failed'] as const) {
    const bucket = state === 'Completed' ? result.completed : result.failed
    for (let page = 0; page < MAX_PAGES; page++) {
      const r = await api.tasks(state, page * PAGE, PAGE)
      for (const t of r.items) if (t.finishedAt && Date.parse(t.finishedAt) >= since.getTime()) bucket.push(t)
      const oldest = r.items.at(-1)
      if (r.items.length < PAGE || (oldest && Date.parse(oldest.createdAt) < since.getTime() - DAY)) break
      if (page === MAX_PAGES - 1) result.truncated = true
    }
  }
  return result
}

/** Finished tasks of the selected period; refreshes when a task changes state. */
export function useFinishedTasks(range: RangeKey): ApiState<FinishedTasks> {
  const fetcher = useCallback(() => loadFinishedSince(rangeStart(range, new Date())), [range])
  return useApi(fetcher, ['TaskStateChanged'])
}
