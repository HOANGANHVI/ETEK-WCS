// CR-020: the PLC Monitoring list rules - filter, sort, what Read / Write offer for each row and role, multi-PLC separation. Pure functions, no DOM. Run: npm run plcmonitor:test
import fs from 'node:fs'
import type { MonitorPoint } from '../src/api/plcTypes.ts'
import { filterPoints, readReadiness, rowWrite, sortPoints } from '../src/lib/plcMonitor.ts'

const results: string[] = []
const check = (name: string, ok: boolean, info = '') => { results.push(`${ok ? 'PASS' : 'FAIL'} ${name}${ok || !info ? '' : ' — ' + info}`); if (!ok) process.exitCode = 1 }

const row = (o: Partial<MonitorPoint>): MonitorPoint => ({
  id: 'x', plcId: 'PLC-01', stationId: 'A1', seq: 1, group: 'Command', signalName: 'Request_Ack', description: 'ack to PLC', direction: 'SoftwareToPlc', dataType: 'Bool',
  value: null, quality: 'Good', status: 'Good', lastUpdated: '2026-11-18T10:00:00Z', address: 'M101', error: null, writeApproval: 'APPROVED', writeSupported: true, writeBlocked: false, wordLength: null, encoding: null, ...o,
})
const rows = [
  row({ id: '1', plcId: 'PLC-01', stationId: 'A1', seq: 1, signalName: 'Request_Ack', status: 'Good', lastUpdated: '2026-11-18T10:00:05Z' }),
  row({ id: '2', plcId: 'PLC-01', stationId: 'A2', seq: 1, signalName: 'Rack_Full', direction: 'PlcToSoftware', status: 'Bad', lastUpdated: '2026-11-18T10:00:01Z' }),
  row({ id: '3', plcId: 'PLC-02', stationId: 'B1', seq: 1, signalName: 'Request_Ack', status: 'Offline', lastUpdated: null }),
  row({ id: '4', plcId: 'PLC-03', stationId: 'X1', seq: 1, signalName: 'AGV_ID', description: 'AGV number', dataType: 'Int', status: 'Stale', lastUpdated: '2026-11-18T09:00:00Z' }),
  row({ id: '5', plcId: 'PLC-04', stationId: 'E1', seq: 1, signalName: 'Transfer_Permit', writeBlocked: true, status: 'NotConfigured', address: null }),
]
const ids = (r: MonitorPoint[]) => r.map((x) => x.id).join(',')
const none = { plc: '', station: '', status: '', text: '' }

// filters (existing search/filter behaviour is kept) and multi-PLC separation
check('no filter keeps every row of every PLC', filterPoints(rows, none).length === 5)
check('filter by PLC returns only that PLC', ids(filterPoints(rows, { ...none, plc: 'PLC-02' })) === '3')
check('the same signal name on two PLCs stays two distinct rows', filterPoints(rows, { ...none, text: 'request_ack' }).map((r) => r.plcId).join() === 'PLC-01,PLC-02')
check('filter by station', ids(filterPoints(rows, { ...none, station: 'X1' })) === '4')
check('filter by each communication status', ['Good', 'Stale', 'Bad', 'Offline', 'NotConfigured'].every((s) => filterPoints(rows, { ...none, status: s }).length === 1))
check('text search also matches the description', ids(filterPoints(rows, { ...none, text: 'agv number' })) === '4')
check('filters combine', ids(filterPoints(rows, { ...none, plc: 'PLC-01', status: 'Bad' })) === '2')

// sorting
check('no sort key keeps the order received', ids(sortPoints(rows, null, 'asc')) === '1,2,3,4,5')
check('sort by status uses the severity order, not the alphabet', ids(sortPoints(rows, 'status', 'asc')) === '1,4,2,3,5')
check('sort by last update: the never-updated row is the oldest, the newest read comes first descending', sortPoints(rows, 'updated', 'asc')[0].id === '3' && sortPoints(rows, 'updated', 'desc')[0].id === '1')
check('sort by signal is stable for equal keys', ids(sortPoints(rows, 'signal', 'asc')) === '4,2,1,3,5')
check('sorting never mutates its input', ids(rows) === '1,2,3,4,5')

// Read: every role; only an unconfigured point or a running read disables it
check('Read is available for a Good, Bad, Stale or Offline point (an offline PLC shows its error on click)', ['Good', 'Bad', 'Stale', 'Offline', 'Unknown'].every((s) => readReadiness({ status: s as MonitorPoint['status'] }, false).ok))
check('Read is refused without an address', !readReadiness({ status: 'NotConfigured' }, false).ok)
check('a second Read of the same point is refused while one runs', (() => { const r = readReadiness({ status: 'Good' }, true); return !r.ok && r.reason === 'PENDING' })())

// Write: Admin only, the CR-019 readiness, never for inbound or blocked signals
const writable = { effectiveMode: 'RealReadWrite', missingWriteKeys: [] }
const readOnly = { effectiveMode: 'RealReadOnly', missingWriteKeys: ['PLC_WRITE_ENABLED'] }
const kind = (w: ReturnType<typeof rowWrite>) => (w.kind === 'action' ? (w.gate.ok ? 'OK' : w.gate.reason) : w.kind)
check('an Operator is never offered Write', kind(rowWrite(false, writable, 'Connected', rows[0])) === 'hidden')
check('Admin + approved + writable runtime + connected PLC: Write enabled', kind(rowWrite(true, writable, 'Connected', rows[0])) === 'OK')
check('PLC -> Software points are never writable', kind(rowWrite(true, writable, 'Connected', rows[1])) === 'inbound')
check('Safety / Interlock / Permit signals are never writable', kind(rowWrite(true, writable, 'Connected', rows[4])) === 'blocked')
check('a read-only runtime disables Write and names it', kind(rowWrite(true, readOnly, 'Connected', rows[0])) === 'READ_ONLY')
check('a point without write approval is refused', kind(rowWrite(true, writable, 'Connected', row({ writeApproval: 'NOT_APPROVED' }))) === 'NOT_APPROVED')
check('a rejected point is refused', kind(rowWrite(true, writable, 'Connected', row({ writeApproval: 'REJECTED' }))) === 'REJECTED')
check('an offline PLC disables Write', kind(rowWrite(true, writable, 'Faulted', rows[0])) === 'PLC_NOT_CONNECTED')
check('an unknown runtime never allows Write', kind(rowWrite(true, null, 'Connected', rows[0])) === 'RUNTIME_UNKNOWN')
check('a type the driver cannot write is refused', kind(rowWrite(true, writable, 'Connected', row({ writeSupported: false }))) === 'UNSUPPORTED')

// the IO address mapping screen is configuration only: no operational Read / Write / simulator action is reachable from it (static check of its source; run from the package root)
const mapping = fs.readFileSync('src/pages/PlcIoMappingPage.tsx', 'utf8')
check('IO mapping has no write call, write dialog, read-now call, simulator dialog or write readiness', !/plcTestWrite|TypedWriteDialog|plcReadNow|SimDialog|writeReadiness|runtimeAllowsWrite|plcSim/.test(mapping))
check('IO mapping keeps address configuration and write APPROVAL', /AddressDialog/.test(mapping) && /WriteApprovalDialog/.test(mapping))
const monitor = fs.readFileSync('src/pages/plc/PlcIoMonitor.tsx', 'utf8')
check('PLC monitoring reuses the CR-019 write dialog and the immediate read API', /TypedWriteDialog/.test(monitor) && /plcReadNow/.test(monitor))
check('PLC monitoring adds no second write path (no direct plcTestWrite call; only the dialog writes)', !/plcTestWrite/.test(monitor))

console.log(results.join('\n'))
