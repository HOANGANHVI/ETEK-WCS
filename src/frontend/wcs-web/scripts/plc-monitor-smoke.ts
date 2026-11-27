// CR-020: the PLC Monitoring list rules - filter, sort, what Read / Write offer for each row and role, multi-PLC separation. Pure functions, no DOM. Run: npm run plcmonitor:test
import fs from 'node:fs'
import type { MonitorPoint } from '../src/api/plcTypes.ts'
import { filterPoints, readReadiness, rowWrite, sortPoints } from '../src/lib/plcMonitor.ts'
import { addressState, canRestore, addressedCounts, filterSheetRows, mergeSpans, plcOptions, readState, sheetRows, shownValue, stationOptions, writeState } from '../src/lib/plcIoMap.ts'

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

// CR-046: the IO map views take their lists from the rows and never offer a write of an unconfirmed or retired signal
const pt = (actualAddress: string | null, addressOrigin: 'None' | 'WorkbookDefault' | 'Custom', defaultAddress: string | null) => ({ actualAddress, addressOrigin, defaultAddress })
const states = [pt('D1500', 'WorkbookDefault', 'D1500'), pt('D1700', 'Custom', 'D1500'), pt(null, 'Custom', 'D1500'), pt(null, 'None', 'D1500'), pt(null, 'None', null)]
check('address state: default, edited, removed, blocked, none', states.map(addressState).join() === 'default,custom,removed,blocked,none')
check('restore is offered whenever the map has an address that the point does not hold as its default', states.map(canRestore).join() === 'false,true,true,true,false')
const cfg = (o: Partial<Parameters<typeof writeState>[0]>) => ({ inIoMap: true, direction: 'SoftwareToPlc' as const, confirmed: true, writeApproval: 'APPROVED' as const, signalName: 'Mission_Feedback', ...o })
const mapRows = [{ plcId: 'PLC-03', stationId: 'X4' }, { plcId: 'PLC-04', stationId: 'G2' }, { plcId: 'PLC-04', stationId: 'G1' }, { plcId: 'PLC-03', stationId: 'X4' }]
check('PLC and station options come from the rows, per PLC, without duplicates', plcOptions(mapRows).join() === 'PLC-03,PLC-04' && stationOptions(mapRows, 'PLC-04').join() === 'G1,G2' && stationOptions(mapRows, '').join() === 'G1,G2,X4')
check('write state: retired, input, not confirmed, protected, approved', [cfg({ inIoMap: false }), cfg({ direction: 'PlcToSoftware' }), cfg({ confirmed: false }), cfg({ writeApproval: 'NOT_APPROVED' }), cfg({})].map((p) => writeState(p, [])).join() === 'retired,input,notConfirmed,protected,approved')
check('an approval never lifts "not confirmed", and a safety signal stays protected', writeState(cfg({ confirmed: false }), []) === 'notConfirmed' && writeState(cfg({ signalName: 'Transfer_Permit' }), ['Transfer_Permit']) === 'protected')
check('read state needs a configured, valid address (an IO map address alone reads nothing)', readState({ actualAddress: null, pollable: false }) === 'noAddress' && readState({ actualAddress: 'D1500', pollable: false }) === 'blocked' && readState({ actualAddress: 'D1500', pollable: true }) === 'enabled')
check('monitor: no write action for an unconfirmed signal on a real PLC; the simulator keeps it', rowWrite(true, writable, 'Connected', row({ confirmed: false })).kind === 'notConfirmed' && rowWrite(true, { ...writable, effectiveMode: 'Simulator' }, 'Connected', row({ confirmed: false })).kind === 'action')
check('a point without an address never shows a placeholder as a device value; a configured zero and a set simulator value are shown', [shownValue({ hasAddress: false, value: 0 }), shownValue({ hasAddress: false, value: false }), shownValue({ hasAddress: false, value: '' })].every((v) => v === null) && shownValue({ hasAddress: true, value: 0 }) === 0 && shownValue({ hasAddress: false, value: 5 }) === 5 && shownValue({ value: false }) === false)
check('station counts ignore points without an address', JSON.stringify(addressedCounts([{ hasAddress: true, quality: 'Simulated' }, { hasAddress: false, quality: 'Simulated' }, { hasAddress: true, quality: 'Bad' }])) === '{"configured":2,"good":1}')
// CR-047: the IO map table is the workbook sheet - checked on the catalog the product ships
const catalog = JSON.parse(fs.readFileSync('../../backend/WCS.Infrastructure/Plc/Catalog/plc-io-catalog.json', 'utf8'))
const shown = sheetRows({ plcs: catalog.plcs, sheet: catalog.sheet })
check('the sheet keeps its 11 columns and 120 rows in workbook order', catalog.sheet.columns.length === 11 && shown.length === 120 && shown.every((r, i) => i === 0 || r.row > shown[i - 1].row))
check('every row belongs to a PLC; 96 rows are station signals, 24 are PLC rows without a station', shown.every((r) => r.plcId) && shown.filter((r) => r.stationId).length === 96 && shown.filter((r) => !r.stationId).length === 24)
const spans = mergeSpans(shown)
check('merged cells span as in the workbook: PLC over 30 rows, station over 6, the shared register over 2', spans[0][0] === 30 && spans[0][1] === 6 && spans[3][7] === 2 && spans[4][7] === 0 && shown[3].signalName === 'AGV_RequestTranfer' && shown[4].signalName === 'AGV_TranferCompleted')
const x4 = filterSheetRows(shown, { plc: 'PLC-03', station: 'X4', text: '' })
check('a station filter shows its 6 rows and keeps the shared register merged', x4.length === 6 && mergeSpans(x4)[3][7] === 2 && mergeSpans(x4)[0][1] === 6)
const one = filterSheetRows(shown, { plc: '', station: '', text: 'AGV_TranferCompleted' })
check('a search that cuts a merged range never leaves a hole', one.length === 16 && mergeSpans(one).every((row) => row[3] === 1 && row[7] === 1))

const mappingSrc = fs.readFileSync('src/pages/PlcIoMappingPage.tsx', 'utf8')
check('IO mapping hard-codes no station or PLC list', !/'(X[0-9]|G[0-9]|E[0-9]|PLC-0[0-9])'/.test(mappingSrc))

console.log(results.join('\n'))
