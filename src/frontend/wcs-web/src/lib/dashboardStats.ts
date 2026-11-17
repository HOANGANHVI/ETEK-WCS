/**
 * Dashboard figures computed ONLY from timestamps of tasks that really exist (no sampling, no estimates).
 *  - A task counts in a period by the time it FINISHED (`finishedAt`), in the browser's local time.
 *  - Failure rate = Failed / (Completed + Failed) of the tasks finished in the period; Cancelled, waiting and running tasks are not part of it (no approved rule).
 *  - Cycle time = finishedAt - createdAt of Completed tasks (request received -> task completed, queue waiting included). Unfinished tasks are not counted.
 */
export type RangeKey = 'today' | '7d' | '30d'
export const RANGES: readonly RangeKey[] = ['today', '7d', '30d']

export interface Bucket { start: number; end: number }
export interface FinishedTask { state: string; createdAt: string; finishedAt: string | null; agvId: string }

const HOUR = 3_600_000

/** Start of the period: local midnight today / 6 days ago / 29 days ago (so the period covers 1 / 7 / 30 calendar days including today). */
export function rangeStart(range: RangeKey, now: Date): Date {
  const back = range === 'today' ? 0 : range === '7d' ? 6 : 29
  return new Date(now.getFullYear(), now.getMonth(), now.getDate() - back)
}

/** Hourly buckets from midnight to the current hour for "today", daily buckets (local calendar days) for the longer periods. */
export function buildBuckets(range: RangeKey, now: Date): Bucket[] {
  const start = rangeStart(range, now)
  const out: Bucket[] = []
  if (range === 'today') {
    const hours = now.getHours() + 1
    for (let h = 0; h < hours; h++) out.push({ start: start.getTime() + h * HOUR, end: start.getTime() + (h + 1) * HOUR })
    return out
  }
  const days = range === '7d' ? 7 : 30
  for (let i = 0; i < days; i++) {
    const s = new Date(start.getFullYear(), start.getMonth(), start.getDate() + i).getTime()
    const e = new Date(start.getFullYear(), start.getMonth(), start.getDate() + i + 1).getTime()
    out.push({ start: s, end: e })
  }
  return out
}

const finishedMs = (t: FinishedTask) => (t.finishedAt ? Date.parse(t.finishedAt) : NaN)
const indexOf = (buckets: Bucket[], ms: number) => buckets.findIndex((b) => ms >= b.start && ms < b.end)

export function countByBucket(tasks: FinishedTask[], buckets: Bucket[]): number[] {
  const counts = buckets.map(() => 0)
  for (const t of tasks) { const i = indexOf(buckets, finishedMs(t)); if (i >= 0) counts[i]++ }
  return counts
}

/** Failed / (Completed + Failed) in percent; null when no task finished (the UI shows "no data", never 0%). */
export function failureRate(completed: number, failed: number): number | null {
  const total = completed + failed
  return total === 0 ? null : (failed / total) * 100
}

const cycleMs = (t: FinishedTask) => finishedMs(t) - Date.parse(t.createdAt)
const valid = (t: FinishedTask) => Number.isFinite(cycleMs(t)) && cycleMs(t) >= 0

/** Mean cycle time (ms) of the Completed tasks per bucket; null for a bucket without completed tasks. */
export function meanCycleByBucket(completed: FinishedTask[], buckets: Bucket[]): (number | null)[] {
  const sums = buckets.map(() => ({ n: 0, ms: 0 }))
  for (const t of completed) {
    if (!valid(t)) continue
    const i = indexOf(buckets, finishedMs(t)); if (i < 0) continue
    sums[i].n++; sums[i].ms += cycleMs(t)
  }
  return sums.map((s) => (s.n === 0 ? null : s.ms / s.n))
}

export function meanCycle(completed: FinishedTask[]): number | null {
  const ok = completed.filter(valid)
  return ok.length === 0 ? null : ok.reduce((a, t) => a + cycleMs(t), 0) / ok.length
}

/** Number of Completed tasks per AGV (the only per-AGV figure the available data supports; utilisation needs AGV status history). */
export function completedPerAgv(completed: FinishedTask[]): Map<string, number> {
  const m = new Map<string, number>()
  for (const t of completed) m.set(t.agvId, (m.get(t.agvId) ?? 0) + 1)
  return m
}
