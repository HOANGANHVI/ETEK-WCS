// Logic of the Server Resource Monitoring panel (freshness, gaps, alerts, scaling): pure functions, no DOM. Run: npm run monitor:test
import { alertsOf, connectionState, formatUptime, pollSeconds, scaleBytes, segmentsOf, seriesOf, splitUptime } from '../src/lib/resourceMonitor.ts'
import type { ResourceHistoryPoint, ResourceMetric, ResourceSnapshot } from '../src/api/types.ts'

const results: string[] = []
const check = (name: string, ok: boolean, info = '') => { results.push(`${ok ? 'PASS' : 'FAIL'} ${name}${ok || !info ? '' : ' — ' + info}`); if (!ok) process.exitCode = 1 }

const snap = (over: Partial<ResourceSnapshot> = {}): ResourceSnapshot => ({
  time: '2026-11-17T10:00:00Z', sampledAt: '2026-11-17T10:00:00Z', sampleAgeSeconds: 0, intervalSeconds: 5, enabled: true,
  environment: { platform: 'Linux', operatingSystem: 'x', architecture: 'X64', inContainer: true, containerRuntime: 'Docker', hostname: null, containerHostname: 'abc', hostScope: 'DockerVm', hostAgentRequired: true, processorNote: null },
  container: {}, host: {}, history: [], ...over,
})
const T = 1_000_000

// ---- freshness: data older than 3 intervals (+2 s) is never "live"
check('before the first answer: loading', connectionState({ snapshot: null, lastOkAt: null, now: T, lastFailed: false }) === 'loading')
check('first request failed: disconnected', connectionState({ snapshot: null, lastOkAt: null, now: T, lastFailed: true }) === 'disconnected')
check('fresh data is live', connectionState({ snapshot: snap(), lastOkAt: T - 4000, now: T, lastFailed: false }) === 'live')
check('data at the edge (3 intervals + 2 s) is still live', connectionState({ snapshot: snap(), lastOkAt: T - 17000, now: T, lastFailed: false }) === 'live')
check('data older than that is disconnected, not live', connectionState({ snapshot: snap(), lastOkAt: T - 17001, now: T, lastFailed: false }) === 'disconnected')
check('a failed request makes even fresh data disconnected', connectionState({ snapshot: snap(), lastOkAt: T - 1000, now: T, lastFailed: true }) === 'disconnected')
check('a sample that was already old when the server answered is not live', connectionState({ snapshot: snap({ sampleAgeSeconds: 120 }), lastOkAt: T, now: T, lastFailed: false }) === 'disconnected')
check('monitoring turned off is reported as disabled', connectionState({ snapshot: snap({ enabled: false }), lastOkAt: T, now: T, lastFailed: false }) === 'disabled')
check('a longer server interval widens the freshness window', connectionState({ snapshot: snap({ intervalSeconds: 30 }), lastOkAt: T - 60000, now: T, lastFailed: false }) === 'live')

// ---- one polling interval, within 3-30 s
check('poll follows the server interval', pollSeconds(snap({ intervalSeconds: 5 })) === 5)
check('poll is never faster than 3 s', pollSeconds(snap({ intervalSeconds: 2 })) === 3)
check('poll is never slower than 30 s', pollSeconds(snap({ intervalSeconds: 60 })) === 30)
check('poll defaults to 5 s before the first answer', pollSeconds(null) === 5)

// ---- scaling and uptime
check('bytes scale in binary units', scaleBytes(3 * 1024 ** 3).unit === 'GB' && scaleBytes(3 * 1024 ** 3).value === 3 && scaleBytes(512).unit === 'B' && scaleBytes(1536).value === 1.5)
check('uptime split', JSON.stringify(splitUptime(90061)) === '{"days":1,"hours":1,"minutes":1}' && splitUptime(-5).minutes === 0)
const tr = (k: string, p: Record<string, number>) => `${k}:${JSON.stringify(p)}`
check('uptime format picks the shortest form', formatUptime(30, tr as never).startsWith('monitor.uptimeM') && formatUptime(7300, tr as never).startsWith('monitor.uptimeHm') && formatUptime(200000, tr as never).startsWith('monitor.uptimeDhm'))

// ---- history: gaps stay gaps
const pt = (i: number, cc: number | null, hc: number | null): ResourceHistoryPoint => ({ t: `2026-11-17T10:00:0${i}Z`, containerCpu: cc, containerMemory: cc === null ? null : 50, hostCpu: hc, hostMemory: hc === null ? null : 60 })
const hist = [pt(0, 10, null), pt(1, null, null), pt(2, 30, 40), pt(3, 40, 50)]
check('series of the container scope', JSON.stringify(seriesOf(hist, 'container', 'cpu')) === '[10,null,30,40]')
check('series of the host scope keeps its gaps (no zero filling)', JSON.stringify(seriesOf(hist, 'host', 'cpu')) === '[null,null,40,50]' && JSON.stringify(seriesOf(hist, 'host', 'memory')) === '[null,null,60,60]')
check('a gap breaks the polyline into segments', JSON.stringify(segmentsOf([10, null, 30, 40, null])) === '[[{"i":0,"v":10}],[{"i":2,"v":30},{"i":3,"v":40}]]')
check('no data gives no segments', segmentsOf([null, null]).length === 0 && segmentsOf([]).length === 0)

// ---- alerts use the server's status only
const m = (status: ResourceMetric['status']): ResourceMetric => ({ value: status === 'Unavailable' ? null : 90, unit: '%', source: 'Container', status, lastUpdatedAt: 'x', reason: status === 'Unavailable' ? 'READ_FAILED' : null })
const a = alertsOf({ cpuPercent: m('Warning'), memoryPercent: m('Critical'), diskPercent: m('Normal'), temperatureC: m('Unavailable') }, ['cpuPercent', 'memoryPercent', 'diskPercent', 'temperatureC'])
check('alerts list Warning and Critical only, worst first', JSON.stringify(a) === '[{"key":"memoryPercent","status":"Critical"},{"key":"cpuPercent","status":"Warning"}]', JSON.stringify(a))
check('an unavailable metric raises no alert', alertsOf({ cpuPercent: m('Unavailable') }, ['cpuPercent']).length === 0)
check('a metric that is missing raises no alert', alertsOf({}, ['cpuPercent']).length === 0)

console.log(results.join('\n'))
console.log(`summary: ${results.filter((r) => r.startsWith('PASS')).length} pass, ${results.filter((r) => r.startsWith('FAIL')).length} fail`)
