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
  applyCreate, checkOutcome, classifyCreateError, createStep, dispatchGate, filterTasks, NO_TASK_FILTER, sendCreate, taskStages,
  type CreateApi, type CreateHolder, type CreateState,
} from '../src/lib/opsConsole.ts'
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
  check(`${lang}: an unproven stage shows the check-by-hand hint`, unk.includes('data-status="unknown"') && unk.includes(T('ops.stageUnknownHint')))
  check(`${lang}: detail has no confirm-pickup / confirm-drop / retry button`, !/<button/i.test(det))

  const flows = [{ flow: 'BP01', rackKind: 'Full', stations: ['A1', 'A2'] }]
  const api: CreateApi = { create: async () => task(), list: async () => [] }
  const closed = wrap(createElement(CreateRequest, { flows, gate: dispatchGate(health(), fleet({ connection: 'Disconnected' })), api, onCreated: () => {} }))
  check(`${lang}: create button is disabled while the gate is closed`, closed.includes(`disabled=""`) && closed.includes(T('ops.createBtn')))
  const open = wrap(createElement(CreateRequest, { flows, gate: dispatchGate(health(), fleet()), api, onCreated: () => {} }))
  check(`${lang}: create form states that the server picks pick-up and drop-off and shows the request key`, open.includes(T('ops.createRoute')) && open.includes('OPS-'))
  check(`${lang}: nothing is sent or confirmed on first render`, !open.includes(T('ops.createConfirmTitle')) && !open.includes('data-create='))
}
i18n.setLang('vi')
for (const r of results) console.log(r)
const failed = results.filter((r) => r.startsWith('FAIL')).length
console.log(`\n${results.length - failed}/${results.length} passed`)
