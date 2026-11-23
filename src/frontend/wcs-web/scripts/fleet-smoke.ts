// AMR monitoring: RCS status classification, KPIs, freshness and filters, checked on hand-made data (no backend). Run: npm run fleet:test
import { classifyStatus, DOCUMENTED_STATUS } from '../src/lib/rcsStatus.ts'
import { batteryPercent, computeKpis, distinctMaps, filterAmrs, isMoving, NO_FILTER } from '../src/lib/fleetModel.ts'
import type { FleetAmr, FleetView } from '../src/api/fleetTypes.ts'

const results: string[] = []
const check = (name: string, ok: boolean, info = '') => { results.push(`${ok ? 'PASS' : 'FAIL'} ${name}${ok || !info ? '' : ' - ' + info}`); if (!ok) process.exitCode = 1 }
const amr = (o: Partial<FleetAmr>): FleetAmr => ({ robotCode: 'R', state: 'Fresh', robotDir: null, robotIp: null, battery: null, posX: null, posY: null, mapCode: null, speed: null, status: null, exclType: null, stop: null, podCode: null, podDir: null, path: [], firstSeenAt: '', lastSeenAt: '', linkedAgvCode: null, activeTaskId: null, activeTaskState: null, ...o })
const fleet = (amrs: FleetAmr[]): FleetView => ({ source: 'Real', simulatedData: false, connection: 'Connected', lastAttemptAt: null, lastSuccessAt: null, lastErrorCode: null, lastErrorMessage: null, consecutiveFailures: 0, intervalSeconds: 5, staleAfterSeconds: 15, count: amrs.length, freshCount: 0, amrs })

// status classification
const s246 = classifyStatus('246')
check('246 keeps the raw text', s246.raw === '246')
check('246 is not documented and is Unknown (not idle, not fault)', !s246.documented && s246.category === 'Unknown')
check('documented 4 is Idle, 7 Charging, 2 Working, 5 Stopped, 1 Done', [['4', 'Idle'], ['7', 'Charging'], ['2', 'Working'], ['5', 'Stopped'], ['1', 'Done']].every(([c, k]) => classifyStatus(c).category === k))
check('documented exception codes are Fault', ['3', '11', '16', '34'].every((c) => classifyStatus(c).category === 'Fault'))
check('codes missing from the document (10, 19, 22, 32) are Unknown', ['10', '19', '22', '32'].every((c) => classifyStatus(c).category === 'Unknown'))
check('empty or missing status has no category', classifyStatus(null).category === null && classifyStatus('').category === null && classifyStatus('  ').category === null)
check('prototype names are not codes', classifyStatus('constructor').category === 'Unknown' && classifyStatus('toString').category === 'Unknown')
check('the dictionary has exactly the 30 documented codes', Object.keys(DOCUMENTED_STATUS).length === 30, String(Object.keys(DOCUMENTED_STATUS).length))

// battery and movement
check('battery 0-100 is valid, others are not', batteryPercent('75') === 75 && batteryPercent('0') === 0 && batteryPercent('100') === 100 && batteryPercent('101') === null && batteryPercent('-1') === null && batteryPercent('abc') === null && batteryPercent('') === null && batteryPercent(null) === null)
check('moving needs fresh data and speed above zero', isMoving(amr({ speed: '300' })) && !isMoving(amr({ speed: '0' })) && !isMoving(amr({ speed: null })) && !isMoving(amr({ speed: '300', state: 'Stale' })))

// KPIs
const list = [
  amr({ robotCode: '22445', status: '246', battery: '75', speed: '0', mapCode: 'TT' }),
  amr({ robotCode: '1', status: '2', battery: '50', speed: '500', mapCode: 'TT' }),
  amr({ robotCode: '2', status: '4', battery: '100', speed: '0', mapCode: 'TT' }),
  amr({ robotCode: '3', status: '16', battery: null, state: 'Stale', mapCode: 'AA' }),
  amr({ robotCode: '4', status: null, battery: 'x', state: 'Missing' }),
]
const k = computeKpis(fleet(list))
check('total', k.total === 5)
check('updating = fresh only, old = stale + missing', k.updating === 3 && k.old === 2)
check('moving counted from speed of fresh AMRs', k.moving === 1)
check('abnormal = unknown (246) + documented fault (16)', k.abnormal === 2 && k.unknownStatus === 1)
check('average battery uses only valid values', k.avgBattery === 75 && k.batterySamples === 3, String(k.avgBattery))
const empty = computeKpis(fleet([]))
check('empty fleet: zeros and no average', empty.total === 0 && empty.avgBattery === null && empty.batterySamples === 0)

// filters
check('no filter keeps all', filterAmrs(list, NO_FILTER).length === 5)
check('text matches robot code', filterAmrs(list, { ...NO_FILTER, text: '224' }).map((a) => a.robotCode).join() === '22445')
check('text matches the raw status code', filterAmrs(list, { ...NO_FILTER, text: '246' }).length === 1)
check('category Unknown finds 246', filterAmrs(list, { ...NO_FILTER, category: 'Unknown' }).map((a) => a.robotCode).join() === '22445')
check('category NoStatus finds the AMR without a status', filterAmrs(list, { ...NO_FILTER, category: 'NoStatus' }).map((a) => a.robotCode).join() === '4')
check('map filter', filterAmrs(list, { ...NO_FILTER, map: 'AA' }).map((a) => a.robotCode).join() === '3')
check('data freshness filter', filterAmrs(list, { ...NO_FILTER, data: 'Missing' }).map((a) => a.robotCode).join() === '4')
check('distinct maps', distinctMaps(list).join() === 'AA,TT')

console.log(results.join('\n'))
