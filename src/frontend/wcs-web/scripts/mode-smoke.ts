// CR-038 – operating mode panel and RCS communication monitor: gate with the mode, one-send mode changes, payload reading, and the first render of the panel and the monitor
// (SIMULATED vs REAL, recovery, deployment without real dispatch). No backend, no DOM. Run: npm run mode:test
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
import type { ExecutionModeView, ModeReadiness, ModeTransitionResult, RcsMessageDetail, RcsMessageSummary } from '../src/api/modeTypes.ts'
import type { SystemHealth } from '../src/api/types.ts'
import { dispatchGate } from '../src/lib/opsConsole.ts'
import { applyTransition, canConfirmSwitch, classifyTransitionError, copyText, messageLine, modeState, modeTag, readPayload, resultTone, sendTransition, timelineOrder, transitionStep, type ModeApi, type TransitionHolder, type TransitionState } from '../src/lib/modeConsole.ts'
import { CommunicationMonitor, JsonTree } from '../src/pages/ops/CommunicationMonitor.tsx'
import { ModePanel, type ModePanelApi } from '../src/pages/ops/ModePanel.tsx'
const i18n = await import('../src/i18n/index.ts')
const T = (k: string, p?: Record<string, string | number>) => (i18n.t as (k: string, p?: Record<string, string | number>) => string)(k, p)

const NOW = '2026-11-25T08:00:00Z'
const health = (): SystemHealth => ({ status: 'Healthy', time: NOW, database: { connected: true }, scheduler: { lastRunAt: NOW, lastError: null, stale: false }, integration: { mode: 'Simulator', plc: 'x', rcs: { dispatchAllowed: true, code: 'OK' } } })
const fleet = (over: Partial<FleetView> = {}): FleetView => ({ source: 'Real', simulatedData: false, connection: 'Connected', lastAttemptAt: NOW, lastSuccessAt: NOW, lastErrorCode: null, lastErrorMessage: null, consecutiveFailures: 0, intervalSeconds: 5, staleAfterSeconds: 30, count: 2, freshCount: 2, amrs: [], ...over })
const mv = (over: Partial<ExecutionModeView> = {}): ExecutionModeView => ({ mode: 'Simulator', isExplicit: true, recoveryRequired: false, recoveryReason: null, updatedAt: NOW, updatedBy: 'admin', realDispatchConfigured: true, ...over });

// ---------------------------------------------------------------- 1. the create gate follows the mode
{
  const simFleet = fleet({ source: 'Simulator', simulatedData: true })
  const g = dispatchGate(health(), simFleet, mv())
  check('gate: Simulator mode lets a request through even though the fleet is simulated (nothing goes to the RCS)', g.allowed && g.executionMode === 'Simulator' && g.warnings.includes('SIMULATOR_MODE'))
  check('gate: Simulator mode needs no fleet data at all', dispatchGate(health(), null, mv()).allowed)
  check('gate: Simulator mode still obeys the server guard and the health', !dispatchGate({ ...health(), integration: { mode: 'Simulator', plc: 'x', rcs: { dispatchAllowed: false, code: 'RCS_NOT_CONNECTED' } } }, null, mv()).allowed && !dispatchGate(null, null, mv()).allowed)
  check('gate: Real mode keeps every fleet check (simulated / disconnected / unknown block)',
    dispatchGate(health(), simFleet, mv({ mode: 'Real' })).blockers.includes('FLEET_SIMULATED') && dispatchGate(health(), fleet({ connection: 'Disconnected' }), mv({ mode: 'Real' })).blockers.includes('RCS_DISCONNECTED')
    && dispatchGate(health(), null, mv({ mode: 'Real' })).blockers.includes('FLEET_UNKNOWN') && dispatchGate(health(), fleet(), mv({ mode: 'Real' })).allowed)
  check('gate: RECOVERY_REQUIRED blocks in both modes', dispatchGate(health(), fleet(), mv({ recoveryRequired: true })).blockers.includes('RECOVERY_REQUIRED') && dispatchGate(health(), fleet(), mv({ mode: 'Real', recoveryRequired: true })).blockers.includes('RECOVERY_REQUIRED'))
  check('gate: an unreadable mode blocks', dispatchGate(health(), fleet(), null).blockers.includes('MODE_UNKNOWN'))
  check('gate: without the mode argument the behaviour is the one before CR-038', dispatchGate(health(), simFleet).blockers.includes('FLEET_SIMULATED') && dispatchGate(health(), fleet()).allowed)
}

// ---------------------------------------------------------------- 2. what the panel offers
check('state: recovery wins, then "not chosen", then ready', modeState(mv({ recoveryRequired: true, isExplicit: false })) === 'recovery' && modeState(mv({ isExplicit: false })) === 'notChosen' && modeState(mv()) === 'ready' && modeState(null) === null)
const ready: ModeReadiness = { current: 'Simulator', target: 'Real', canSwitch: true, conditions: [], blockingWork: [] }
check('switch: only when the server said it can be done, with a reason, and Real needs the confirmation', canConfirmSwitch(ready, 'why', true) && !canConfirmSwitch(ready, 'why', false) && !canConfirmSwitch(ready, '  ', true)
  && !canConfirmSwitch({ ...ready, canSwitch: false }, 'why', true) && !canConfirmSwitch(null, 'why', true) && canConfirmSwitch({ ...ready, target: 'Simulator' }, 'why', false) && !canConfirmSwitch(ready, 'x'.repeat(501), true))
check('tags: Real is REAL and everything else SIMULATED', modeTag('Real') === 'REAL' && modeTag('Simulator') === 'SIMULATED')

// ---------------------------------------------------------------- 3. one change, one send
const newId = () => 'NEW'
{
  let s: TransitionState = { phase: 'idle', requestId: 'R1' }
  check('transition: a send without a confirmation never fires', !transitionStep(s, 'send', newId).fire)
  s = transitionStep(s, 'ask', newId).state
  const a = transitionStep(s, 'send', newId); const b = transitionStep(a.state, 'send', newId)
  check('transition: the first send fires once; a second while sending does not', a.fire && !b.fire && b.state.phase === 'sending')
  check('transition: a new id is refused while sending and after an unknown outcome', transitionStep(a.state, 'reset', newId).state.phase === 'sending' && transitionStep({ phase: 'unknown', requestId: 'R1' }, 'reset', newId).state.phase === 'unknown')
  const r = transitionStep({ phase: 'unknown', requestId: 'R1' }, 'retry', newId)
  check('transition: after a lost answer the SAME request id may be asked again, once', r.fire && r.state.requestId === 'R1' && !transitionStep(r.state, 'retry', newId).fire && !transitionStep({ phase: 'idle', requestId: 'R1' }, 'retry', newId).fire)
  check('transition: after a result a new id is issued', transitionStep({ phase: 'done', requestId: 'R1' }, 'reset', newId).state.requestId === 'NEW' && transitionStep({ phase: 'refused', requestId: 'R1' }, 'reset', newId).state.requestId === 'NEW')
}
const holder = (s: TransitionState): TransitionHolder & { s: TransitionState } => { const h = { s, get: () => h.s, set: (n: TransitionState) => { h.s = n } }; return h }
const okResult = (to: 'Simulator' | 'Real'): ModeTransitionResult => ({ succeeded: true, repeated: false, from: to === 'Real' ? 'Simulator' : 'Real', to, failed: [], blockingWork: [] })
function fakeModeApi(kind: 'ok' | 'refuse' | 'network' | 'http500' | 'denied') {
  const bodies: unknown[] = []
  const api: ModeApi = {
    transition: async (body) => {
      bodies.push(body); await Promise.resolve()
      if (kind === 'refuse') return { succeeded: false, repeated: false, from: 'Simulator', to: 'Real', failed: [{ code: 'RCS_CONNECTION', ok: false, message: 'x' }], blockingWork: [] }
      if (kind === 'network') throw new ApiRequestError(0, { code: 'NETWORK_ERROR', message: '' })
      if (kind === 'http500') throw new ApiRequestError(500, { code: 'X', message: '' })
      if (kind === 'denied') throw new ApiRequestError(403, { code: 'FORBIDDEN', message: '' })
      return okResult(body.target)
    },
  }
  return { api, bodies }
}
{
  const h = holder({ phase: 'idle', requestId: 'RID-1' }); const { api, bodies } = fakeModeApi('ok')
  applyTransition(h, 'ask')
  const [x, y] = await Promise.all([sendTransition(h, api, 'Real', ' because ', true), sendTransition(h, api, 'Real', ' because ', true)])   // a double click
  check('double click: ONE call, carrying the request id, the trimmed reason and the Real confirmation', bodies.length === 1 && (bodies[0] as { requestId: string }).requestId === 'RID-1' && (bodies[0] as { reason: string }).reason === 'because' && (bodies[0] as { confirmRealAgv: boolean }).confirmRealAgv && !!(x.result ?? y.result) && h.s.phase === 'done')
}
{
  const h = holder({ phase: 'idle', requestId: 'RID-2' }); const { api, bodies } = fakeModeApi('ok')
  applyTransition(h, 'ask'); await sendTransition(h, api, 'Simulator', 'back', true)
  check('Simulator target never carries the Real confirmation', (bodies[0] as { confirmRealAgv: boolean }).confirmRealAgv === false)
}
{
  const h = holder({ phase: 'idle', requestId: 'RID-3' }); const { api } = fakeModeApi('refuse')
  applyTransition(h, 'ask'); const r = await sendTransition(h, api, 'Real', 'why', true)
  check('a refusal by the server is final for this id (phase refused, the failed checks are returned)', r.result?.succeeded === false && r.result.failed[0].code === 'RCS_CONNECTION' && h.s.phase === 'refused')
}
for (const kind of ['network', 'http500'] as const) {
  const h = holder({ phase: 'idle', requestId: 'RID-4' }); const { api, bodies } = fakeModeApi(kind)
  applyTransition(h, 'ask'); await sendTransition(h, api, 'Real', 'why', true)
  await sendTransition(h, api, 'Real', 'why', true)                       // a second plain send does nothing
  check(`${kind}: the outcome is unknown, nothing is resent by itself, the request id is kept`, h.s.phase === 'unknown' && h.s.requestId === 'RID-4' && bodies.length === 1)
  await sendTransition(h, api, 'Real', 'why', true, 'retry')              // the operator asks again
  check(`${kind}: an explicit retry uses the SAME request id`, bodies.length === 2 && (bodies[1] as { requestId: string }).requestId === 'RID-4')
}
{
  const h = holder({ phase: 'idle', requestId: 'RID-5' }); const { api } = fakeModeApi('denied')
  applyTransition(h, 'ask'); await sendTransition(h, api, 'Real', 'why', true)
  check('errors: 4xx = refused, network / 5xx = unknown', h.s.phase === 'refused' && classifyTransitionError(new ApiRequestError(409, { code: 'C', message: '' })) === 'refused' && classifyTransitionError(new ApiRequestError(0, { code: 'NETWORK_ERROR', message: '' })) === 'unknown' && classifyTransitionError(new Error('x')) === 'unknown')
}

// ---------------------------------------------------------------- 4. messages
let seqCounter = 0
const msg = (id: string, at: string, direction: 'Out' | 'In', type: string, result: string | null, m: 'Simulator' | 'Real' = 'Simulator', http: number | null = null): RcsMessageSummary =>
  ({ seq: ++seqCounter, id, at, mode: m, direction, messageType: type, protocol: 'p', result, httpStatus: http, errorCode: null, requestCode: 'WCS-1' })
check('timeline: the server arrival order (seq) decides, so equal timestamps keep their order', timelineOrder([{ ...msg('b', NOW, 'In', 'x', null), seq: 3 }, { ...msg('a', NOW, 'Out', 'y', null), seq: 2 }, { ...msg('c', NOW, 'In', 'z', null), seq: 1 }]).map((m) => m.id).join('') === 'cab')
check('line: time, direction, type and result', messageLine(msg('a', NOW, 'In', 'Create Task Response', 'Accepted'), () => '10:00:02') === '10:00:02 · IN · Create Task Response · Accepted')
check('tone: only an answer of the RCS is good; timeout / connection error / not sent are unknown, not failures', resultTone('Accepted') === 'ok' && resultTone('Rejected') === 'bad' && resultTone('Timeout') === 'warn' && resultTone('ConnectionError') === 'warn' && resultTone('NotSent') === 'warn' && resultTone(null) === 'muted')
check('payload: JSON is parsed and pretty-printed, text stays text, empty is none', readPayload('{"a":1}').kind === 'json' && copyText('{"a":1}') === '{\n  "a": 1\n}' && readPayload('not json').kind === 'text' && copyText('not json') === 'not json' && readPayload(null).kind === 'none' && copyText(null) === '')

// ---------------------------------------------------------------- 5. rendering
const wrap = (el: ReturnType<typeof createElement>) => renderToStaticMarkup(createElement(MemoryRouter, null, el))
const modeApi: ModePanelApi = { readiness: async () => ready, transition: async () => okResult('Real'), acknowledge: async () => okResult('Real'), history: async () => [], adoptLegacy: async () => ({ adoptedTasks: 0, adoptedOrders: 0, notAdopted: [] }) }
const panel = (view: ExecutionModeView | null, isAdmin = true, loadFailed = false) => wrap(createElement(ModePanel, { view, loadFailed, isAdmin, api: modeApi, onChanged: () => {} }))
{
  const sim = panel(mv())
  check('panel: Simulator is shown as SIMULATED with the note that nothing goes to the real RCS', sim.includes('data-mode="Simulator"') && sim.includes(T('ops.modeTagSimulated')) && sim.includes(T('ops.modeSimulatorNote')) && sim.includes(T('ops.modeSwitchTo', { mode: T('ops.modeReal') })))
  const real = panel(mv({ mode: 'Real' }))
  check('panel: Real is shown as REAL with the warning that AMRs may move', real.includes('data-mode="Real"') && real.includes(T('ops.modeTagReal')) && real.includes(T('ops.modeRealNote')) && real.includes(T('ops.modeSwitchTo', { mode: T('ops.modeSimulator') })))
  const deploy = panel(mv({ realDispatchConfigured: false }))
  check('panel: a deployment without real dispatch says so and disables the switch to Real', deploy.includes(T('ops.modeRealNotAllowed')) && /<button[^>]*disabled=""[^>]*>[^<]*(Chuyển sang|Switch to)/.test(deploy))
  check('panel: a fresh environment says that no mode was chosen', panel(mv({ isExplicit: false })).includes(T('ops.modeNotChosen')))
  const rec = panel(mv({ mode: 'Real', recoveryRequired: true, recoveryReason: 'One dispatch has no answer.' }))
  check('panel: RECOVERY_REQUIRED is loud, keeps the mode Real (never Simulator) and offers only the acknowledgement', rec.includes('data-recovery="yes"') && rec.includes(T('ops.modeRecoveryTitle')) && rec.includes('One dispatch has no answer.') && rec.includes(T('ops.modeRecoveryAck')) && rec.includes('data-mode="Real"'))
  check('panel: an Operator sees the mode but gets no switch', !panel(mv(), false).includes(T('ops.modeSwitchTo', { mode: T('ops.modeReal') })) && panel(mv(), false).includes(T('ops.modeAdminOnly')))
  check('panel: an unreadable mode says so', panel(null, true, true).includes(T('ops.modeLoadFailed')))
}
{
  const rows = [msg('m1', '2026-11-25T08:00:01Z', 'Out', 'Create Task', 'Sent', 'Real'), msg('m2', '2026-11-25T08:00:02Z', 'In', 'Create Task Response', 'Accepted', 'Real', 200), msg('s1', '2026-11-25T08:00:03Z', 'In', 'Create Task Response', 'Timeout', 'Simulator')]
  const html = wrap(createElement(CommunicationMonitor, { messages: rows, error: null, loadDetail: async () => ({}) as RcsMessageDetail }))
  check('monitor: every row carries its mode, direction and result; REAL and SIMULATED are both visible', html.includes('data-direction="Out"') && html.includes('data-direction="In"') && html.includes('data-mode="Real"') && html.includes('data-mode="Simulator"') && html.includes(T('ops.modeTagReal')) && html.includes(T('ops.modeTagSimulated')) && html.includes('HTTP 200'))
  check('monitor: a timeout is labelled as such (not as a failure or a success)', html.includes(T('ops.res_Timeout')) && html.includes(T('ops.res_Accepted')))
  check('monitor: no message selected yet says so; an empty log says so', html.includes(T('ops.comPick')) && wrap(createElement(CommunicationMonitor, { messages: [], error: null, loadDetail: async () => ({}) as RcsMessageDetail })).includes(T('ops.comNone')))
  check('monitor: a load error is shown', wrap(createElement(CommunicationMonitor, { messages: null, error: 'boom', loadDetail: async () => ({}) as RcsMessageDetail })).includes('boom'))
  check('monitor: no resend / confirm-pickup button exists', !/resend|retry|gửi lại|xác nhận lấy|xác nhận thả/i.test(html.replace(T('ops.comCopy'), '')))
}
{
  const tree = renderToStaticMarkup(createElement(JsonTree, { value: { a: 1, list: [{ k: 'v' }], n: null }, open: true }))
  check('json tree: objects and arrays are collapsible nodes, values are shown as stored', (tree.match(/<details/g) ?? []).length === 3 && tree.includes('&quot;v&quot;') && tree.includes('null') && tree.includes('open=""'))
  check('json tree: collapsed rendering has no open attribute', !renderToStaticMarkup(createElement(JsonTree, { value: { a: { b: 1 } }, open: false })).includes('open=""'))
}
for (const r of results) console.log(r)
const failed = results.filter((r) => r.startsWith('FAIL')).length
console.log(`\n${results.length - failed}/${results.length} passed`)
