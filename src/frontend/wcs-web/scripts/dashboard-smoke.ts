// Dashboard figures: formulas checked on hand-made tasks (no backend). Run: npm run dashboard:test
import { buildBuckets, completedPerAgv, countByBucket, failureRate, meanCycle, meanCycleByBucket, rangeStart, type FinishedTask } from '../src/lib/dashboardStats.ts'

const results: string[] = []
const check = (name: string, ok: boolean, info = '') => { results.push(`${ok ? 'PASS' : 'FAIL'} ${name}${ok || !info ? '' : ' — ' + info}`); if (!ok) process.exitCode = 1 }
const at = (y: number, m: number, d: number, h = 0, min = 0) => new Date(y, m - 1, d, h, min).toISOString()
const task = (state: string, created: string, finished: string | null, agvId = 'A'): FinishedTask => ({ state, createdAt: created, finishedAt: finished, agvId })

const now = new Date(2026, 10, 17, 14, 30)   // 17 Nov 2026 14:30 local
check('today starts at local midnight', rangeStart('today', now).getTime() === new Date(2026, 10, 17).getTime())
check('7 days = 7 calendar days including today', rangeStart('7d', now).getTime() === new Date(2026, 10, 11).getTime())
check('30 days = 30 calendar days including today', rangeStart('30d', now).getTime() === new Date(2026, 9, 19).getTime())
check('today has hourly buckets up to the current hour', buildBuckets('today', now).length === 15)
check('7d has 7 daily buckets', buildBuckets('7d', now).length === 7)
check('30d has 30 daily buckets', buildBuckets('30d', now).length === 30)
const b7 = buildBuckets('7d', now)
check('buckets are contiguous', b7.every((b, i) => i === 0 || b.start === b7[i - 1].end))

const completed = [
  task('Completed', at(2026, 11, 17, 9, 0), at(2026, 11, 17, 9, 10)),
  task('Completed', at(2026, 11, 17, 9, 30), at(2026, 11, 17, 9, 50), 'B'),
  task('Completed', at(2026, 11, 16, 23, 50), at(2026, 11, 17, 0, 0)),       // finishes exactly at midnight: counts in the new day
  task('Completed', at(2026, 11, 15, 8, 0), at(2026, 11, 15, 8, 30), 'B'),
]
const failed = [task('Failed', at(2026, 11, 17, 10, 0), at(2026, 11, 17, 10, 5))]
const day = countByBucket(completed, b7)
check('completed tasks are counted by the day they finished', day[6] === 3 && day[4] === 1 && day.reduce((a, b) => a + b, 0) === 4, day.join(','))
check('failed tasks are counted separately', countByBucket(failed, b7)[6] === 1)
const hours = buildBuckets('today', now)
check('hourly buckets', countByBucket(completed, hours)[9] === 2 && countByBucket(completed, hours)[0] === 1)

check('failure rate = failed / (completed + failed)', failureRate(3, 1) === 25)
check('no finished task: no rate (never 0%)', failureRate(0, 0) === null)
check('all failed = 100%', failureRate(0, 2) === 100)

const mean = meanCycle(completed)
check('mean cycle = mean of finishedAt - createdAt of completed tasks', mean !== null && Math.abs(mean - ((10 + 20 + 10 + 30) / 4) * 60_000) < 1, String(mean))
check('no completed task: no cycle time', meanCycle([]) === null)
check('invalid timestamps are ignored', meanCycle([task('Completed', at(2026, 11, 17, 9), at(2026, 11, 17, 8)), task('Completed', at(2026, 11, 17, 9), null)]) === null)
const byBucket = meanCycleByBucket(completed, b7)
check('mean cycle per day, gaps stay null', byBucket[5] === null && byBucket[4] === 30 * 60_000 && byBucket[6] === (10 + 20 + 10) / 3 * 60_000, JSON.stringify(byBucket))
const per = completedPerAgv(completed)
check('completed per AGV', per.get('A') === 2 && per.get('B') === 2)
console.log(results.join(String.fromCharCode(10)))
