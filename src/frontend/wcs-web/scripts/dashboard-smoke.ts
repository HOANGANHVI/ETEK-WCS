// Dashboard figures: formulas checked on hand-made tasks (no backend). Run: npm run dashboard:test
import { buildBuckets, completedPerAgv, completedPerRobot, countByBucket, failureRate, finishedFromOrder, isRunningOrder, meanCycle, meanCycleByBucket, rangeStart, type FinishedTask } from '../src/lib/dashboardStats.ts'
import { readFileSync } from 'node:fs'

const results: string[] = []
const check = (name: string, ok: boolean, info = '') => { results.push(`${ok ? 'PASS' : 'FAIL'} ${name}${ok || !info ? '' : ' — ' + info}`); if (!ok) process.exitCode = 1 }
const at = (y: number, m: number, d: number, h = 0, min = 0) => new Date(y, m - 1, d, h, min).toISOString()
const task = (state: string, created: string, finished: string | null, agvId = 'A'): FinishedTask => ({ state, createdAt: created, finishedAt: finished, agvId })

const NOWISH = () => at(2026, 11, 17, 12)
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
// CR-028: a task without an AGV row / robot is never attributed to one
const mixed: FinishedTask[] = [...completed, { state: 'Completed', createdAt: at(2026, 11, 17, 9), finishedAt: at(2026, 11, 17, 9, 5), agvId: null, robotCode: null }, { state: 'Completed', createdAt: at(2026, 11, 17, 9), finishedAt: at(2026, 11, 17, 9, 6), agvId: null, robotCode: '1001' }, { state: 'Completed', createdAt: at(2026, 11, 17, 9), finishedAt: at(2026, 11, 17, 9, 7), agvId: null, robotCode: '1001' }]
const perMixed = completedPerAgv(mixed)
check('per AGV: tasks without an AGV are not counted for any AGV', perMixed.get('A') === 2 && perMixed.get('B') === 2 && perMixed.size === 2)
const perRobot = completedPerRobot(mixed)
check('per robot: by robotCode, tasks without a robot reported separately', perRobot.byRobot.get('1001') === 2 && perRobot.withoutRobot === 5 && perRobot.byRobot.size === 1, JSON.stringify([...perRobot.byRobot]) + perRobot.withoutRobot)
// CR-053: transport orders are counted with the tasks
const order = finishedFromOrder({ state: 'Completed', waitKind: 'None', createdAt: at(2026, 11, 17, 11), finishedAt: at(2026, 11, 17, 11, 20), legs: [{ sequence: 2, agvId: 'B', robotCode: null }, { sequence: 1, agvId: 'A', robotCode: '1001' }] })
const both = [...completed, order]
check('CR-053: a completed order counts once, in its day, in the cycle time and for the AGV of its last leg', countByBucket(both, b7)[6] === 4 && completedPerAgv(both).get('B') === 3 && completedPerAgv(both).get('A') === 2 && meanCycle([order]) === 20 * 60_000 && order.robotCode === '1001')
check('CR-053: an order without a leg is counted but attributed to no AGV', finishedFromOrder({ state: 'Failed', waitKind: 'None', createdAt: NOWISH(), finishedAt: NOWISH(), legs: [] }).agvId === null)
check('CR-053: running = accepted, in progress or holding a rack; a waiting order that has not started is not', [['Accepted', 'None'], ['InProgress', 'None'], ['Waiting', 'DestinationUnavailable_AgvHoldingRack'], ['Waiting', 'DestinationUnavailable'], ['Completed', 'None'], ['Cancelled', 'None']].map(([state, waitKind]) => isRunningOrder({ state, waitKind })).join() === 'true,true,true,false,false,false')
const loader = readFileSync('src/hooks/useFinishedTasks.ts', 'utf8')
check('CR-053: the dashboard reads finished orders and refreshes on their changes', loader.includes('api.routingOrders(state') && loader.includes("'OrderChanged'") && readFileSync('src/pages/DashboardPage.tsx', 'utf8').includes('isRunningOrder'))
console.log(results.join(String.fromCharCode(10)))
