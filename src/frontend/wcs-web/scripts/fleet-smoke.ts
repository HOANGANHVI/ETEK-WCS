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


// ---- header link status (the same fleet view as the monitoring) ----
import { rcsView } from '../src/components/linkStatus.ts'
import agv from '../src/i18n/messages/agv.ts'
import nav from '../src/i18n/messages/nav.ts'
import about from '../src/i18n/messages/about.ts'
const withConn = (connection: FleetView['connection'], simulatedData = false): FleetView => ({ ...fleet([]), connection, simulatedData, source: simulatedData ? 'Simulator' : 'Real' })
const view = (f: FleetView | null, failed = false) => { const v = rcsView(f, failed); return `${v.tone}:${typeof v.text === 'string' ? v.text : v.text.raw}` }
check('Connected fleet = header connected', view(withConn('Connected')) === 'ok:nav.connected')
check('Degraded fleet = header interrupted (warning, not disconnected)', view(withConn('Degraded')) === 'warn:nav.interrupted')
check('Disconnected fleet = header disconnected', view(withConn('Disconnected')) === 'bad:nav.disconnected')
check('No poll yet = header not checked', view(withConn('NoData')) === 'muted:nav.notChecked')
check('Fleet not loaded yet = not checked, load failed = no data', view(null) === 'muted:nav.notChecked' && view(null, true) === 'bad:nav.noData')
check('A simulated fleet is never shown as a connection', view(withConn('Connected', true)) === 'warn:nav.notConnected')
check('Recovery: Disconnected then Connected gives the Connected header again', view(withConn('Disconnected')) !== view(withConn('Connected')) && view(withConn('Connected')) === 'ok:nav.connected')

// ---- no internal explanation left in the operator texts (VI and EN) ----
const banned = [/mô phỏng thay thế/i, /not.*simulat.*(replace|instead)/i, /giữ nguyên mã gốc/i, /không suy diễn/i, /keeps the raw code/i, /does not guess/i, /parser/i, /fallback/i, /WCS không tạo AMR/i, /never creates an AMR/i, /không có trong tài liệu/i, /not in the (RCS|API) document/i, /hệ thống không xóa/i, /does not delete it/i, /Số AMR do RCS quyết định/i]
const texts = [agv, nav, about].flatMap((d) => [...Object.values(d.vi), ...Object.values(d.en)] as string[])
const hits = texts.filter((s) => banned.some((b) => b.test(s)))
check('no developer-facing explanation remains in the AMR / header / about texts', hits.length === 0, hits.join(' | '))
check('Unclassified status text is short and carries the raw code', agv.vi.amrStatusUnknown === 'Chưa phân loại ({code})' && agv.en.amrStatusUnknown === 'Unclassified ({code})')
check('every status text of the fleet screen is at most 90 characters (VI and EN)', Object.entries({ ...agv.vi, ...agv.en }).filter(([k]) => /^(fleet|fMissing|fOld|amrStatus|kpi|fPoll)/.test(k)).every(([, v]) => (v as string).length <= 90))
console.log(results.join('\n'))
