// Operations console (CR-036 A.4): when a request may be created, the seven stages of a task without invented progress, the task filters, single-flight creation with no retry,
// and the first render of the overview / monitor / detail / confirmation (Real vs Simulator, stale / NoData). No backend, no DOM. Run: npm run ops:test
const store = new Map<string, string>()
;(globalThis as Record<string, unknown>).window = { localStorage: { getItem: (k: string) => store.get(k) ?? null, setItem: (k: string, v: string) => { store.set(k, v) } } }
;(globalThis as Record<string, unknown>).document = { documentElement: { lang: '' } }
const results: string[] = []
const check = (name: string, ok: boolean, info = '') => { results.push(`${ok ? 'PASS' : 'FAIL'} ${name}${ok || !info ? '' : ' - ' + info}`); if (!ok) process.exitCode = 1 }

import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { MemoryRouter } from 'react-router-dom'
import { ApiRequestError } from '../src/api/errors.ts'
import type { FleetView } from '../src/api/fleetTypes.ts'
import type { Location, SystemHealth, TaskHistory, TransportTask } from '../src/api/types.ts'
import {
  applyCreate, checkOrderOutcome, checkOutcome, sendProcessCreate, classifyCreateError, createStep, dispatchGate, filterTasks, NO_TASK_FILTER, sendCreate, taskStages,
  type CreateApi, type CreateHolder, type CreateState,
} from '../src/lib/opsConsole.ts'
import { readFileSync } from 'node:fs'
import { requestRows } from '../src/lib/requestRows.ts'
import { waitCause } from '../src/lib/waitReason.ts'
import { processName } from '../src/i18n/processNames.ts'
import { RemovalDialog } from '../src/components/RemovalDialog.tsx'
import { ImportPlanView } from '../src/components/ImportPlanView.tsx'
import { BlockerList, WarningList } from '../src/pages/config/BlockerList.tsx'
import { RequestOptions } from '../src/components/RequestOptions.tsx'
import { filterWork, isWaitingToBeSent, isWithRcs, orderCode, orderRoute, workLink, workOfAgv, workRows } from '../src/lib/work.ts'
import { RcsWorkTable } from '../src/components/RcsWorkTable.tsx'
import { canRecover } from '../src/lib/amrSim.ts'
import { blockerAction, blockerLink, orderInMotion, orderIsOpen, type RackBlocker } from '../src/lib/rackBlock.ts'
import { RequestTable } from '../src/components/RequestTable.tsx'
import { QueueAlertList } from '../src/components/QueueAlertList.tsx'
import { ConnectionStrip } from '../src/pages/ops/ConnectionStrip.tsx'
import { CreateRequest } from '../src/pages/ops/CreateRequest.tsx'
import { OpsTaskDetail } from '../src/pages/ops/OpsTaskDetail.tsx'
import { TaskMonitor } from '../src/pages/ops/TaskMonitor.tsx'
const i18n = await import('../src/i18n/index.ts')
const T = (k: string, p?: Record<string, string | number>) => (i18n.t as (k: string, p?: Record<string, string | number>) => string)(k, p)

const NOW = '2026-11-25T08:00:00Z'
const health = (over: Partial<SystemHealth> = {}, rcs: { dispatchAllowed: boolean; code: string } | undefined = { dispatchAllowed: true, code: 'OK' }, mode = 'Production'): SystemHealth => ({
  status: 'Healthy', time: NOW, database: { connected: true }, scheduler: { lastRunAt: NOW, lastError: null, stale: false }, integration: { mode, plc: 'x', rcs }, ...over,
})
const fleet = (over: Partial<FleetView> = {}): FleetView => ({
  source: 'Real', simulatedData: false, connection: 'Connected', lastAttemptAt: NOW, lastSuccessAt: NOW, lastErrorCode: null, lastErrorMessage: null, consecutiveFailures: 0,
  intervalSeconds: 5, staleAfterSeconds: 30, count: 2, freshCount: 2, amrs: [], dispatchMode: 'RcsManaged', ...over,
})
const task = (over: Partial<TransportTask> = {}): TransportTask => ({
  id: '11111111-aaaa-bbbb-cccc-000000000001', requestKey: 'RK-1', flow: 'BP01', rackKind: 'Full', agvId: null, robotCode: null, rcsDispatchState: null, assignedAt: null,
  requestLocationId: 'L-A1', sourceLocationId: 'L-A1', destinationLocationId: 'L-B1', rackId: null, state: 'Pending', leg: 'Pick', plcPermissionGranted: false,
  stateReason: null, rcsRequestCode: 'REQ-1', rcsTaskCode: null, requiresReconciliation: false, pickConfirmedAt: null, dropConfirmedAt: null,
  createdAt: '2026-11-25T07:00:00', updatedAt: '2026-11-25T07:00:00', finishedAt: null, ...over,
})
const hist = (toState: string | null, at: string, event = 'StateChanged'): TaskHistory => ({ id: `${toState}-${at}`, fromState: null, toState, event, detail: null, source: 'WCS', occurredAt: at })
const st = (stages: ReturnType<typeof taskStages>, k: string) => stages.find((s) => s.key === k)?.status

// ---------------------------------------------------------------- 1. when a request may be created
check('gate: everything real, fresh and allowed -> open', dispatchGate(health(), fleet()).allowed)
check('gate: no health data -> blocked (never assumed open)', dispatchGate(null, fleet()).blockers.includes('HEALTH_UNKNOWN'))
check('gate: no fleet data -> blocked', dispatchGate(health(), null).blockers.includes('FLEET_UNKNOWN'))
check('gate: simulated fleet -> blocked', dispatchGate(health(), fleet({ source: 'Simulator', simulatedData: true })).blockers.includes('FLEET_SIMULATED'))
check('gate: NoData / Degraded / Disconnected each block with their own reason',
  dispatchGate(health(), fleet({ connection: 'NoData' })).blockers.includes('RCS_NODATA') && dispatchGate(health(), fleet({ connection: 'Degraded' })).blockers.includes('RCS_DEGRADED') && dispatchGate(health(), fleet({ connection: 'Disconnected' })).blockers.includes('RCS_DISCONNECTED'))
check('gate: the server refuses dispatch -> blocked', dispatchGate(health({}, { dispatchAllowed: false, code: 'RCS_NOT_CONNECTED' }), fleet()).blockers.includes('GUARD_REFUSES'))
check('gate: an older server that does not report the guard -> blocked, not assumed allowed', dispatchGate({ ...health(), integration: { mode: 'Production', plc: 'x' } }, fleet()).blockers.includes('GUARD_UNKNOWN'))
check('gate: backend degraded -> blocked', dispatchGate(health({ status: 'Degraded' }), fleet()).blockers.includes('BACKEND_DEGRADED'))
{
  const simMode = dispatchGate(health({}, { dispatchAllowed: true, code: 'OK' }, 'Simulator'), fleet())
  check('gate: Integration Mode alone never opens it; Simulator mode with a real RCS only warns', !dispatchGate(health(), null).allowed && simMode.allowed && simMode.warnings.includes('INTEGRATION_SIMULATOR'))
}
check('gate: connected with zero AMR opens but warns', (() => { const g = dispatchGate(health(), fleet({ count: 0, freshCount: 0 })); return g.allowed && g.warnings.includes('NO_AMR') })())

// ---------------------------------------------------------------- 2. stages: nothing is invented
{
  const s = taskStages(task(), [])
  check('stages: a new task is only "created"; everything after is pending', st(s, 'created') === 'done' && ['dispatched', 'rcsAccepted', 'agvAssigned', 'pickup', 'drop', 'completed'].every((k) => st(s, k) === 'pending'))
}
{
  const s = taskStages(task({ state: 'Accepted', rcsDispatchState: 'Accepted', rcsTaskCode: 'RT-9' }), [hist('Dispatching', '2026-11-25T07:01:00'), hist('Accepted', '2026-11-25T07:02:00')])
  check('RCS accepted is NOT agv assigned / pickup / drop / completed', st(s, 'dispatched') === 'done' && st(s, 'rcsAccepted') === 'done' && st(s, 'agvAssigned') === 'pending' && st(s, 'pickup') === 'pending' && st(s, 'drop') === 'pending' && st(s, 'completed') === 'pending')
}
{
  const s = taskStages(task({ state: 'Executing', rcsDispatchState: 'Assigned', rcsTaskCode: 'RT-9', robotCode: 'AMR-7', assignedAt: NOW }), [hist('Dispatching', '2026-11-25T07:01:00')])
  check('an assigned AMR does not imply pickup or drop', st(s, 'agvAssigned') === 'done' && st(s, 'pickup') === 'pending' && st(s, 'drop') === 'pending')
}
{
  const s = taskStages(task({ state: 'Completed', rcsTaskCode: 'RT-9', robotCode: 'AMR-7', pickConfirmedAt: '2026-11-25T07:10:00', dropConfirmedAt: '2026-11-25T07:20:00', finishedAt: '2026-11-25T07:21:00' }), [hist('Dispatching', '2026-11-25T07:01:00')])
  check('a completed task with pickup and drop recorded: every stage done', s.every((x) => x.status === 'done'))
}
{
  const s = taskStages(task({ state: 'Completed', rcsTaskCode: 'RT-9', robotCode: 'AMR-7', finishedAt: '2026-11-25T07:21:00' }), [])
  check('completed WITHOUT a recorded drop is flagged unknown, never "done"', st(s, 'completed') === 'unknown' && st(s, 'drop') === 'unknown' && st(s, 'pickup') === 'unknown')
}
{
  const s = taskStages(task({ state: 'Failed', rcsDispatchState: 'SyncError', finishedAt: NOW }), [hist('Dispatching', '2026-11-25T07:01:00')])
  check('sent, then failed with a sync error: assignment is unknown, completion did not happen', st(s, 'dispatched') === 'done' && st(s, 'agvAssigned') === 'unknown' && st(s, 'completed') === 'failed')
}
{
  const s = taskStages(task({ state: 'Cancelled', finishedAt: NOW }), [])
  check('cancelled before dispatch: nothing after "created" is claimed', st(s, 'dispatched') === 'failed' && st(s, 'pickup') === 'failed' && st(s, 'completed') === 'failed')
}

// ---------------------------------------------------------------- 3. filters
const locs: Location[] = [{ id: 'L-A1', code: 'A1' }, { id: 'L-B1', code: 'B1' }, { id: 'L-C1', code: 'C1' }] as Location[]
const code = (id: string | null) => locs.find((l) => l.id === id)?.code ?? ''
const tasks = [
  task({ id: 't1', requestKey: 'RK-ONE', state: 'Completed', createdAt: '2026-11-20T10:00:00' }),
  task({ id: 't2', requestKey: 'RK-TWO', state: 'Executing', sourceLocationId: 'L-C1', destinationLocationId: 'L-B1', createdAt: '2026-11-24T10:00:00', rcsTaskCode: 'RT-77', robotCode: 'AMR-3' }),
  task({ id: 't3', requestKey: 'RK-THREE', state: 'Failed', destinationLocationId: 'L-C1', createdAt: '2026-11-25T10:00:00' }),
]
const f = (p: Partial<typeof NO_TASK_FILTER>) => filterTasks(tasks, { ...NO_TASK_FILTER, ...p }, (x) => `NV-${x.id}`, code).map((x) => x.id).join(',')
check('filter: no filter returns every task', f({}) === 't1,t2,t3')
check('filter: by task id (display code), request key, RCS task code and robot code', f({ text: 'nv-t2' }) === 't2' && f({ text: 'rk-three' }) === 't3' && f({ text: 'rt-77' }) === 't2' && f({ text: 'amr-3' }) === 't2')
check('filter: by state', f({ state: 'Failed' }) === 't3')
check('filter: by date range, both ends inclusive', f({ from: '2026-11-24', to: '2026-11-24' }) === 't2' && f({ from: '2026-11-21' }) === 't2,t3' && f({ to: '2026-11-20' }) === 't1')
check('filter: by source and by destination', f({ source: 'C1' }) === 't2' && f({ destination: 'C1' }) === 't3' && f({ source: 'A1', destination: 'B1' }) === 't1')
check('filter: combined filters narrow, none matching gives an empty list', f({ state: 'Executing', destination: 'B1' }) === 't2' && f({ state: 'Completed', destination: 'C1' }) === '')

// ---------------------------------------------------------------- 4. single-flight create
const newKey = () => 'KEY-NEW'
check('state machine: a send without a confirmation never fires', !createStep({ phase: 'idle', requestKey: 'K' }, 'send', newKey).fire)
{
  let s: CreateState = { phase: 'idle', requestKey: 'K' }
  s = createStep(s, 'ask', newKey).state
  const first = createStep(s, 'send', newKey); s = first.state
  const second = createStep(s, 'send', newKey)
  check('state machine: the first confirmed send fires once, a second send while sending does not', first.fire && !second.fire && second.state.phase === 'sending')
  check('state machine: a new key is refused while sending and after an unknown outcome', createStep(s, 'reset', newKey).state.phase === 'sending' && createStep({ phase: 'unknown', requestKey: 'K' }, 'reset', newKey).state.phase === 'unknown')
  check('state machine: results are accepted only while sending', createStep({ phase: 'idle', requestKey: 'K' }, 'created', newKey).state.phase === 'idle')
  check('state machine: after a refusal a new key is issued for the next request', createStep({ phase: 'refused', requestKey: 'K' }, 'reset', newKey).state.requestKey === 'KEY-NEW')
}
check('errors: network / server fault = unknown; 4xx answers = refused; anything else = unknown',
  classifyCreateError(new ApiRequestError(0, { code: 'NETWORK_ERROR', message: '' })) === 'unknown' && classifyCreateError(new ApiRequestError(503, { code: 'X', message: '' })) === 'unknown'
  && classifyCreateError(new ApiRequestError(409, { code: 'RCS_NOT_CONNECTED', message: '' })) === 'refused' && classifyCreateError(new ApiRequestError(400, { code: 'V', message: '' })) === 'refused' && classifyCreateError(new Error('boom')) === 'unknown')

function holder(initial: CreateState): CreateHolder & { s: CreateState } {
  const h = { s: initial, get: () => h.s, set: (n: CreateState) => { h.s = n } }
  return h
}
function fakeApi(mode: 'ok' | 'refuse' | 'unknown', listed: TransportTask[] = []) {
  const calls: string[] = []
  const api: CreateApi = {
    create: async (flow, station, key) => {
      calls.push(`create:${flow}:${station}:${key}`)
      await Promise.resolve()
      if (mode === 'refuse') throw new ApiRequestError(409, { code: 'RCS_NOT_CONNECTED', message: 'x' })
      if (mode === 'unknown') throw new ApiRequestError(0, { code: 'NETWORK_ERROR', message: 'x' })
      return task({ requestKey: key })
    },
    list: async () => { calls.push('list'); return listed },
  }
  return { api, calls }
}
{
  const h = holder({ phase: 'idle', requestKey: 'K1' }); const { api, calls } = fakeApi('ok')
  applyCreate(h, 'ask')
  const [a, b] = await Promise.all([sendCreate(h, api, 'BP01', 'A1'), sendCreate(h, api, 'BP01', 'A1')])   // a double click
  check('double click: the server is called exactly once, with the page request key', calls.length === 1 && calls[0] === 'create:BP01:A1:K1' && !!(a.task ?? b.task))
  const c = await sendCreate(h, api, 'BP01', 'A1')
  check('after the result a further send does nothing (no duplicate task)', calls.length === 1 && c.task === null && h.s.phase === 'created')
}
{
  const h = holder({ phase: 'idle', requestKey: 'K2' }); const { api, calls } = fakeApi('refuse')
  applyCreate(h, 'ask'); const r = await sendCreate(h, api, 'BP01', 'A1')
  check('refusal: no task, phase refused, the error is passed on, ONE call, no retry', r.task === null && r.error instanceof ApiRequestError && h.s.phase === 'refused' && calls.length === 1)
}
{
  const h = holder({ phase: 'idle', requestKey: 'K3' }); const { api, calls } = fakeApi('unknown')
  applyCreate(h, 'ask'); await sendCreate(h, api, 'BP01', 'A1')
  await sendCreate(h, api, 'BP01', 'A1')
  check('unknown outcome: phase unknown, nothing is resent (no auto retry), the request key is kept', h.s.phase === 'unknown' && h.s.requestKey === 'K3' && calls.length === 1)
  const none = await checkOutcome(h, api)
  check('checking an unknown outcome only reads the list', none === null && calls.join() === 'create:BP01:A1:K3,list')
  const { api: api2 } = fakeApi('ok', [task({ id: 'x', requestKey: 'K3' })])
  check('checking finds the task by its request key', (await checkOutcome(h, api2))?.id === 'x')
}

// ---------------------------------------------------------------- 5. rendering
const wrap = (el: ReturnType<typeof createElement>) => renderToStaticMarkup(createElement(MemoryRouter, null, el))
// server-side rendering always uses the default language (vi); the English dictionary is covered by the key-parity check of i18n:test
for (const lang of ['vi'] as const) {
  i18n.setLang(lang)
  const real = wrap(createElement(ConnectionStrip, { health: health(), fleet: fleet(), loadFailed: false, gate: dispatchGate(health(), fleet()) }))
  check(`${lang}: real fleet shows "Real" and Connected, gate open`, real.includes(T('ops.sourceReal')) && real.includes(T('nav.connected')) && real.includes('data-gate="open"'))
  const simFleet = fleet({ source: 'Simulator', simulatedData: true })
  const sim = wrap(createElement(ConnectionStrip, { health: health({}, { dispatchAllowed: true, code: 'OK' }, 'Simulator'), fleet: simFleet, loadFailed: false, gate: dispatchGate(health(), simFleet) }))
  check(`${lang}: simulated data is labelled, is NOT shown as connected and the gate is blocked`, sim.includes(T('ops.simulatedData')) && !sim.includes(`>${T('nav.connected')}<`) && sim.includes('data-gate="blocked"'))
  const lost = fleet({ connection: 'Disconnected', lastSuccessAt: '2026-11-25T06:00:00Z' })
  const stale = wrap(createElement(ConnectionStrip, { health: health(), fleet: lost, loadFailed: false, gate: dispatchGate(health(), lost) }))
  check(`${lang}: a lost link says disconnected and the gate is blocked`, stale.includes(T('nav.disconnected')) && !stale.includes('data-gate="open"'))
  const nd = fleet({ connection: 'NoData', lastAttemptAt: null, lastSuccessAt: null })
  const nodata = wrap(createElement(ConnectionStrip, { health: health(), fleet: nd, loadFailed: false, gate: dispatchGate(health(), nd) }))
  check(`${lang}: NoData says "none yet", never connected`, nodata.includes(T('ops.connNever')) && !nodata.includes(`>${T('nav.connected')}<`))
  const nothing = wrap(createElement(ConnectionStrip, { health: null, fleet: null, loadFailed: true, gate: dispatchGate(null, null) }))
  check(`${lang}: nothing loaded shows the load failure and a blocked gate`, nothing.includes(T('ops.connLoadFailed')) && nothing.includes('data-gate="blocked"'))

  const mon = wrap(createElement(TaskMonitor, { tasks, locations: locs, selectedId: 't2', onSelect: () => {} }))
  check(`${lang}: monitor lists the tasks with source, destination and the dispatch column`, mon.includes('data-task="t1"') && mon.includes('data-task="t3"') && mon.includes('>C1<') && mon.includes(T('ops.colDispatch')))
  check(`${lang}: monitor shows "no tasks" for an empty list`, wrap(createElement(TaskMonitor, { tasks: [], locations: locs, selectedId: null, onSelect: () => {} })).includes(T('ops.monNone')))

  const det = wrap(createElement(OpsTaskDetail, { task: task({ state: 'Accepted', rcsDispatchState: 'Accepted', rcsTaskCode: 'RT-9' }), history: [hist('Dispatching', '2026-11-25T07:01:00')], locations: locs }))
  check(`${lang}: detail shows seven separate stages; RCS accepted done, pickup / drop / completion not done`,
    ['created', 'dispatched', 'rcsAccepted', 'agvAssigned', 'pickup', 'drop', 'completed'].every((k) => det.includes(`data-stage="${k}"`)) && det.includes('data-stage="rcsAccepted" data-status="done"')
    && det.includes('data-stage="pickup" data-status="pending"') && det.includes('data-stage="drop" data-status="pending"') && det.includes('data-stage="completed" data-status="pending"'))
  check(`${lang}: detail says the product model is not provided instead of inventing one`, det.includes(T('ops.modelNotProvided')))
  const unk = wrap(createElement(OpsTaskDetail, { task: task({ state: 'Completed', robotCode: 'AMR-7', finishedAt: NOW }), history: [], locations: locs }))
  const realDetail = wrap(createElement(OpsTaskDetail, { task: task({ executionMode: 'Real' }), history: [], locations: locs }))
  const legacyDetail = wrap(createElement(OpsTaskDetail, { task: task({ executionMode: null }), history: [], locations: locs }))
  check(`${lang}: the task shows its execution mode (REAL) and an old task says it has none and that nothing is sent`, realDetail.includes('data-exec-mode="Real"') && realDetail.includes(T('ops.modeTagReal')) && legacyDetail.includes('data-exec-mode="none"') && legacyDetail.includes(T('ops.execModeLegacy')))
  check(`${lang}: an unproven stage shows the check-by-hand hint`, unk.includes('data-status="unknown"') && unk.includes(T('ops.stageUnknownHint')))
  check(`${lang}: detail has no confirm-pickup / confirm-drop / retry button`, !/<button/i.test(det))

  const flows = [{ flow: 'BP01', rackKind: 'Full', stations: ['A1', 'A2'] }]
  const api: CreateApi = { create: async () => task(), list: async () => [] }
  const closed = wrap(createElement(CreateRequest, { flows, gate: dispatchGate(health(), fleet({ connection: 'Disconnected' })), api, onCreated: () => {} }))
  check(`${lang}: create button is disabled while the gate is closed`, closed.includes(`disabled=""`) && closed.includes(T('ops.createBtn')))
  const open = wrap(createElement(CreateRequest, { flows, gate: dispatchGate(health(), fleet()), api, onCreated: () => {} }))
  check(`${lang}: create form states that the server picks pick-up and drop-off and shows the request key`, open.includes(T('ops.createRoute')) && open.includes('OPS-'))
  const processes = [
    { code: 'PH2-07', name: 'X to Y', requestType: 'PickupRequest', rackKind: 'Full', triggerLocationCodes: ['X1', 'X2'], readiness: 'Ready', acceptsRequests: true, blockedCode: null, blockedDetail: null },
    { code: 'MY-ROUTE', name: 'user route', requestType: 'ManualRequest', rackKind: 'Empty', triggerLocationCodes: ['NEW-1'], readiness: 'NotReady', acceptsRequests: false, blockedCode: 'PROCESS_NOT_READY', blockedDetail: null },
  ]
  const listed = wrap(createElement(CreateRequest, { flows, processes, processApi: { start: async () => { throw new Error('not called') }, list: async () => [] }, gate: dispatchGate(health(), fleet()), api, onCreated: () => {} }))
  check(`${lang}: every published process is listed beside the Phase 1 flows, whatever its stations or code`, listed.includes('process:PH2-07') && listed.includes('process:MY-ROUTE') && listed.includes('value="BP01"'))
  check(`${lang}: a process that accepts no request is listed and marked, not hidden`, listed.includes(T('ops.createNotReadyTag')) && listed.includes('MY-ROUTE · user route'))
  check(`${lang}: nothing is sent or confirmed on first render`, !open.includes(T('ops.createConfirmTitle')) && !open.includes('data-create='))
}
{
  const proc = { code: 'PH2-07', name: 'X to Y', requestType: 'PickupRequest', rackKind: 'Full', triggerLocationCodes: ['X1'], readiness: 'Ready', acceptsRequests: true, blockedCode: null, blockedDetail: null }
  let state: CreateState = { phase: 'idle', requestKey: 'OPS-p' }
  const h: CreateHolder = { get: () => state, set: (s) => { state = s } }
  const sent: string[] = []
  const orders = { start: async (p: { code: string }, station: string, key: string) => { sent.push(`${p.code}/${station}/${key}`); return { id: 'o1', requestKey: key, processCode: p.code } as never }, list: async () => [{ id: 'o1', requestKey: 'OPS-p' } as never] }
  const before = await sendProcessCreate(h, orders, proc, 'X1')
  check('a process request is not sent before the operator confirms', before.order === null && sent.length === 0)
  applyCreate(h, 'ask')
  const [a, b] = await Promise.all([sendProcessCreate(h, orders, proc, 'X1'), sendProcessCreate(h, orders, proc, 'X1')])
  check('a double click starts exactly one order with the request key', sent.length === 1 && sent[0] === 'PH2-07/X1/OPS-p' && [a, b].filter((r) => r.order).length === 1)
  check('an order is found again by its request key without creating anything', (await checkOrderOutcome(h, orders))?.id === 'o1' && sent.length === 1)
}
for (const lang of ['vi'] as const) {   // server-side rendering uses the default language
  i18n.setLang(lang)
  const order = { id: 'o-1', requestKey: 'OPS-ORDER-1', processCode: 'PH2-07', state: 'Waiting', createdAt: '2026-11-25T09:00:00Z', legs: [{ robotCode: 'AMR-9' }], queueSequence: 12, queueScope: 'AREA-A', queueStatus: 'Held' } as never
  const rows = requestRows([task({ id: 't-1', requestKey: 'OPS-TASK-1', createdAt: '2026-11-25T08:00:00Z', robotCode: 'AMR-1' })], [order])
  check(`${lang}: tasks and transport orders are one list identified by request key, newest first`, rows.map((x) => x.requestKey).join() === 'OPS-ORDER-1,OPS-TASK-1')
  check(`${lang}: each request links to the page that owns its detail`, rows[0].link === '/routing?order=o-1' && rows[1].link === '/tasks?id=t-1')
  const html = renderToStaticMarkup(createElement(MemoryRouter, null, createElement(RequestTable, { rows })))
  check(`${lang}: the list shows queue state, position, scope and the AMR`, html.includes(T('linked.queue_Held')) && html.includes('AREA-A') && html.includes('12') && html.includes('AMR-9') && html.includes('data-request="OPS-TASK-1"'))
}
check('task management, RCS operations and multi-leg dispatch show the same request list', ['TasksPage', 'OperationsConsolePage', 'RoutingPage'].every((f) => readFileSync(`src/pages/${f}.tsx`, 'utf8').includes('<RequestOverview')))
{ // CR-042: the stalled queue warning shows what the server reports and nothing when there is no stall
  const alert = { orderId: 'o-7', requestKey: 'OPS-STALL', processCode: 'PH2-07', scope: 'S', waitKind: 'QueueBehind', reason: null, waitingSince: NOW, minutes: 12, behind: 3 }
  const html = renderToStaticMarkup(createElement(MemoryRouter, null, createElement(QueueAlertList, { alerts: [alert] })))
  check('a stalled queue head is shown with its minutes, the requests behind and a link to it', html.includes('OPS-STALL') && html.includes('12') && html.includes('/routing?order=o-7') && html.includes(T('linked.alertNote')))
  check('no stall, no warning', renderToStaticMarkup(createElement(MemoryRouter, null, createElement(QueueAlertList, { alerts: [] }))) === '')
}
// CR-041: one option list and one create path for the three pages
const src = (f: string) => readFileSync('src/' + f, 'utf8')
check('both request forms take their options from the one shared list', ['pages/TaskDialogs.tsx', 'pages/ops/CreateRequest.tsx'].every((f) => src(f).includes('<RequestOptions ')))
check('multi-leg dispatch creates through the task management dialog, not through a form of its own', src('pages/RoutingPage.tsx').includes('<CreateTaskDialog ') && !src('pages/RoutingPage.tsx').includes('routingStart('))
check('no request form writes a process code', ['pages/TaskDialogs.tsx', 'pages/ops/CreateRequest.tsx', 'components/RequestOptions.tsx', 'pages/RoutingPage.tsx'].every((f) => !/['"](BP|PH2)-dd['"]/.test(src(f))))
check('a task row leads to the RCS monitor and an order row to its legs', renderToStaticMarkup(createElement(MemoryRouter, null, createElement(RequestTable, { rows: requestRows([task({ id: 't-9', requestKey: 'K9' })], []) }))).includes('/operations?task=t-9'))
i18n.setLang('vi')
check('QA-001: a held port is not shown as a missing rack', waitCause({ waitKind: 'SourceUnavailable', stateReason: 'PORT_HELD: port A1 is held' }) === 'portHeld'
  && waitCause({ waitKind: 'SourceUnavailable', stateReason: 'No usable rack at the request location' }) === 'kind' && waitCause({ waitKind: 'AgvBusy', stateReason: 'PORT_HELD: x' }) === 'kind')
check('CR-050: recovery is offered for a stalled run and a failed or waiting leg, never for an ended task', [{ kind: 'Leg', phase: 'Failed', stalled: false }, { kind: 'Leg', phase: 'WaitingForTrigger', stalled: false }, { kind: 'Task', phase: 'ToSource', stalled: true }, { kind: 'Task', phase: 'Failed', stalled: false }, { kind: 'Leg', phase: 'Completed', stalled: false }].map((r) => canRecover(r as never)).join() === 'true,true,true,false,false')
{ // CR-051: tasks and transport orders side by side, and the work the RCS has
  const order = { id: 'abcd1234-0000', requestKey: 'OPS-O', createdAt: '2026-11-25T09:00:00Z', state: 'InProgress', requestLocationCode: 'A1', racksHeld: false, legs: [{ sequence: 2, sourceLocationCode: 'F1', destinationLocationCode: 'B1', agvId: 'g1', robotCode: null }, { sequence: 1, sourceLocationCode: 'A1', destinationLocationCode: 'F1', agvId: 'g1', robotCode: null }] } as never
  const rows = workRows([task({ id: 't-1', requestKey: 'OPS-T', createdAt: '2026-11-25T08:00:00Z', agvId: 'g2' })], [order])
  const f = { agvId: '', agvRobotCode: null, search: '', reconOnly: false, taskCodeOf: (id: string) => 'NV-' + id }
  check('CR-051: one list of both engines, newest first, each once', rows.map((r) => r.kind + ':' + r.id).join() === 'order:abcd1234-0000,task:t-1')
  check('CR-051: an order is found by its code, request key or id, and by the AGV of a leg', [orderCode('abcd1234-0000'), 'ops-o', 'abcd1234'].every((s) => filterWork(rows, { ...f, search: s }).length === 1) && filterWork(rows, { ...f, agvId: 'g1' }).map((r) => r.kind).join() === 'order' && filterWork(rows, { ...f, agvId: 'g2' }).map((r) => r.kind).join() === 'task')
  check('CR-051: the route of an order runs from its first source to its last destination', orderRoute(order).source === 'A1' && orderRoute(order).destination === 'B1')
  const w = (o: object) => ({ kind: 'OrderLeg', id: 'l1', orderId: 'abcd1234-0000', legSequence: 1, processCode: 'BP-01', agvId: 'g1', robotCode: '2048', rcsState: 'Executing', workState: 'InProgress', rcsRequestCode: 'LEG-1', rcsTaskCode: 'SIM-LEG-1', source: 'A1', destination: 'B1', updatedAt: NOW, sentAt: NOW, createdAt: NOW, ...o }) as never
  check('CR-051: created is not sent; only what the RCS has counts as working', isWaitingToBeSent(w({ rcsState: 'NotSent' })) && !isWaitingToBeSent(w({ rcsState: 'NotSent', workState: 'Cancelled' })) && !isWithRcs(w({ rcsState: 'NotSent' })) && isWithRcs(w({})) && !isWithRcs(w({ rcsState: 'Completed' })))
  check('CR-051: the work of an AGV is by its assignment, otherwise by the reported robot', workOfAgv([w({}), w({ agvId: null })], { id: 'g1', rcsRobotCode: '2048' }).length === 2 && workOfAgv([w({ agvId: 'g9' })], { id: 'g1', rcsRobotCode: '2048' }).length === 0)
  const html = renderToStaticMarkup(createElement(MemoryRouter, null, createElement(RcsWorkTable, { rows: [w({})] })))
  check('CR-051: a sent leg shows its order, RCS task, AMR and state and leads to task management', html.includes(orderCode('abcd1234-0000')) && html.includes('SIM-LEG-1') && html.includes('2048') && html.includes(T('linked.rcsState_Executing')) && html.includes('/tasks?order=abcd1234-0000') && workLink(w({ kind: 'Task', id: 't2' })) === '/tasks?id=t2')
  check('CR-051: task management lists orders and the AGV page reads the RCS work of both engines', readFileSync('src/pages/TasksPage.tsx', 'utf8').includes('api.routingOrders(') && readFileSync('src/pages/AgvPage.tsx', 'utf8').includes('api.rcsWork('))
}
check('CR-052: a held destination port is named before and after the pick; other causes keep their own wording', ['DestinationUnavailable', 'DestinationUnavailable_AgvHoldingRack'].every((k) => waitCause({ waitKind: k, stateReason: 'PORT_HELD: No usable destination - A1: port A1 is held' }) === 'portHeld')
  && waitCause({ waitKind: 'DestinationUnavailable', stateReason: 'No usable destination - B1: no free slot' }) === 'kind')
{ // CR-054: reference process names follow the language; an operator's own name is left as it is
  const opts = (lang: 'vi' | 'en') => { i18n.setLang(lang); return renderToStaticMarkup(createElement(RequestOptions, { flows: [], processes: [{ code: 'BP-01', name: 'A1/A2 has a full rack', acceptsRequests: true }, { code: 'PH2-08', name: 'X1/X2 cần rack rỗng từ buffer', acceptsRequests: true }, { code: 'BP-02', name: 'Tên do người dùng đặt', acceptsRequests: true }] as never })) }
  const vi = opts('vi'), en = opts('en'); i18n.setLang('vi')
  check('CR-054: Vietnamese shows no English reference name', vi.includes('BP-01 · A1/A2 có rack đầy') && !vi.includes('has a full rack') && vi.includes('X1/X2 cần rack rỗng từ buffer'))
  check('CR-054: English shows the English names', en.includes('A1/A2 has a full rack') && en.includes('X1/X2 needs an empty rack from the buffer'))
  check('CR-054: a name the operator chose is shown as stored', vi.includes('Tên do người dùng đặt') && en.includes('Tên do người dùng đặt') && processName('X-9', 'A1/A2 has a full rack') === 'A1/A2 has a full rack')
  check('CR-054: every delivered reference process is covered', (() => { const delivered = JSON.parse(readFileSync('../../backend/WCS.Application/Routing/reference-processes.json', 'utf8').slice(readFileSync('../../backend/WCS.Application/Routing/reference-processes.json', 'utf8').charCodeAt(0) === 0xfeff ? 1 : 0)) as { code: string; name: string }[]; i18n.setLang('vi'); return delivered.length >= 18 && delivered.every((p) => { const inVi = processName(p.code, p.name); i18n.setLang('en'); const inEn = processName(p.code, p.name); i18n.setLang('vi'); return inVi !== inEn }) })())
}
{ // CR-055: removal of process configuration
  const page = readFileSync('src/pages/ProcessesPage.tsx', 'utf8')
  check('CR-055: the page asks the server what removing does, requires a reason and hides switched-off processes', page.includes('api.processRemoval(') && page.includes('api.processRemove(') && page.includes('api.processVersionDelete(') && page.includes('showOff || p.enabled') && readFileSync('src/components/RemovalDialog.tsx', 'utf8').includes('!reason.trim()'))
  const html = renderToStaticMarkup(createElement(RemovalDialog, { title: 'T', body: 'B', confirm: 'XOA', onClose: () => {}, onConfirm: async () => {} }))
  check('CR-055: the confirmation starts with the action disabled until a reason is given', /disabled=""[^>]*>XOA|<button[^>]*disabled[^>]*>XOA/.test(html), html)
  const typed = renderToStaticMarkup(createElement(RemovalDialog, { title: 'T', body: 'B', confirm: 'XOA', typeToConfirm: 'BP-01', onClose: () => {}, onConfirm: async () => {} }))
  check('CR-056: deleting a process asks for its code and a reason, and sends both', typed.includes(T('routing.removeTypeCode', { code: 'BP-01' })) && page.includes('typeToConfirm={p.code}') && readFileSync('src/api/client.ts', 'utf8').includes('qs({ reason, confirmCode })'))
}
{ // CR-062: loading the reference processes is confirmed first
  const some = renderToStaticMarkup(createElement(ImportPlanView, { plan: { add: [{ code: 'BP-04', name: 'x', reviewBlocked: true, missingLocations: ['F1', 'F2'] }], keep: ['PH2-01'] } }))
  check('CR-062: what is added, what is missing and what is kept are shown', some.includes(T('routing.impNoOverwrite')) && some.includes('BP-04') && some.includes('F1, F2') && some.includes('PH2-01') && some.includes(T('routing.impReview')))
  check('CR-062: nothing to load is said', renderToStaticMarkup(createElement(ImportPlanView, { plan: { add: [], keep: ['PH2-01'] } })).includes(T('routing.impNothing')))
  const page = readFileSync('src/pages/ProcessesPage.tsx', 'utf8')
  check('CR-062: the button previews; the import runs only from the confirmation', page.includes('onClick={previewImport}') && page.includes('disabled={busy || plan.add.length === 0}') && !page.includes('onClick={importReference}'))
}
{ // CR-057: removal of a location
  const html = renderToStaticMarkup(createElement(BlockerList, { blockers: [{ code: 'LOCATION_DELETE_RACKS', detail: '2' }, { code: 'LOCATION_DELETE_PROCESS', detail: 'BP-01, QT001' }] }))
  check('CR-057: every reason a location is kept is shown in words', html.includes(T('config.blk_LOCATION_DELETE_RACKS', { detail: '2' })) && html.includes('BP-01, QT001') && !html.includes('LOCATION_DELETE'))
  const warned = renderToStaticMarkup(createElement(WarningList, { warnings: [{ code: 'LOCATION_WARN_FLOW_STATION', detail: 'BP-05, BP-06' }] }))
  check('CR-058: a use by the deployment configuration is shown as a warning in words', warned.includes(T('config.warn_LOCATION_WARN_FLOW_STATION', { detail: 'BP-05, BP-06' })) && !warned.includes('LOCATION_WARN') && renderToStaticMarkup(createElement(WarningList, { warnings: [] })) === '')
  const dialog = readFileSync('src/pages/config/LocationRemovalDialog.tsx', 'utf8')
  check('CR-057: the deletion asks the server first and needs the typed code', dialog.includes('api.locationRemoval(') && dialog.includes('typeToConfirm={code}') && readFileSync('src/pages/config/OperationsTab.tsx', 'utf8').includes('<LocationRemovalDialog'))
}
// CR-047: a rack that cannot be removed says who holds it and where to go; nothing is cancelled from the rack screen
const held = (o: Partial<RackBlocker>): RackBlocker => ({ kind: 'Order', id: 'a b', reference: 'R1', state: 'Waiting', process: 'BP-05', canCancel: true, racksHeld: false, ...o })
check('next step of a held rack: cancel, finish, release, task', [held({}), held({ canCancel: false }), held({ canCancel: false, racksHeld: true }), held({ kind: 'Task' })].map(blockerAction).join() === 'cancelOrder,finishOrder,releaseRacks,openTask')
check('an order can be cancelled while open and nothing of it moves', orderIsOpen('Waiting') && !orderIsOpen('Cancelled') && !orderInMotion([{ state: 'Ready', rackOnAgv: false, agvTaskCode: null }]) && orderInMotion([{ state: 'InProgress', rackOnAgv: false, agvTaskCode: 'T' }]) && orderInMotion([{ state: 'Waiting', rackOnAgv: true, agvTaskCode: null }]))
check('the order screen offers the cancellation of the API', readFileSync('src/pages/RoutingPage.tsx', 'utf8').includes('api.routingCancel('))
check('the link leads to the order or the task', blockerLink(held({})) === '/routing?order=a%20b' && blockerLink(held({ kind: 'Task', id: 't1' })) === '/tasks?id=t1')

for (const r of results) console.log(r)
const failed = results.filter((r) => r.startsWith('FAIL')).length
console.log(`\n${results.length - failed}/${results.length} passed`)
