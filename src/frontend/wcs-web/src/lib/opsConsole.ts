// CR-036 (A.4) – Operations Console rules that need no DOM: when a request may be created, how a task is staged WITHOUT inventing progress, how a task list is filtered, how a create call
// is protected against a second send, and how its outcome is classified. Everything is derived from data the server already provides; nothing is guessed. Tested by `npm run ops:test`.
import type { FleetView } from '../api/fleetTypes'
import { ApiRequestError } from '../api/errors'
import type { ExecMode, ExecutionModeView } from '../api/modeTypes'
import type { SystemHealth, TaskHistory, TransportTask } from '../api/types'

// ------------------------------------------------------------------------------------------------ may a request be created?
export type GateReason =
  | 'HEALTH_UNKNOWN' | 'BACKEND_DEGRADED' | 'GUARD_UNKNOWN' | 'GUARD_REFUSES'
  | 'FLEET_UNKNOWN' | 'FLEET_SIMULATED' | 'RCS_NODATA' | 'RCS_DEGRADED' | 'RCS_DISCONNECTED' | 'MODE_UNKNOWN' | 'RECOVERY_REQUIRED' | 'MODE_NOT_CHOSEN'
export type GateWarning = 'NO_AMR' | 'INTEGRATION_SIMULATOR' | 'SIMULATOR_MODE' | 'BACKEND_DEGRADED_SIMULATOR'

export interface Gate { allowed: boolean; blockers: GateReason[]; warnings: GateWarning[]; integrationMode: string | null; fleetSource: 'Real' | 'Simulator' | null; executionMode: ExecMode | null }

/**
 * The page lets a request be created only when the server data it already shows says the RCS side is real, fresh and willing. This is an advisory gate for the operator: the server stays the
 * authority and refuses what it must refuse. What no API tells the page (for example whether the dispatch adapter itself is real) is NOT assumed: it is said in the confirmation instead.
 * CR-038: with the operating mode known, Simulator mode is a deliberate choice that sends nothing to the RCS, so the state of the real fleet does not block it (it is still said: SIMULATOR_MODE);
 * Real mode keeps every check below; a mode that is unknown, or a pending recovery, blocks. Without the third argument the behaviour is exactly the one before CR-038.
 * `health.integration.mode` is informational (it describes the PLC integration, which may legitimately be simulated while the RCS is real), so it warns but never blocks.
 */
export function dispatchGate(health: SystemHealth | null, fleet: FleetView | null, mode?: ExecutionModeView | null): Gate {
  const blockers: GateReason[] = []; const warnings: GateWarning[] = []
  const simulatorMode = mode?.mode === 'Simulator'
  if (mode === null) blockers.push('MODE_UNKNOWN')
  else if (mode?.recoveryRequired) blockers.push('RECOVERY_REQUIRED')
  if (simulatorMode) warnings.push('SIMULATOR_MODE')
  if (!health) blockers.push('HEALTH_UNKNOWN')
  else {
    // Real keeps this check. In Simulator mode nothing leaves WCS and the server accepts the request (the Task page does too), so a degraded backend is said, not blocked.
    if (health.status !== 'Healthy') { if (simulatorMode) warnings.push('BACKEND_DEGRADED_SIMULATOR'); else blockers.push('BACKEND_DEGRADED') }
    const rcs = health.integration.rcs
    if (rcs === undefined) blockers.push('GUARD_UNKNOWN')
    else if (!rcs.dispatchAllowed) blockers.push(simulatorMode && mode?.isExplicit === false ? 'MODE_NOT_CHOSEN' : 'GUARD_REFUSES')
    if (health.integration.mode === 'Simulator') warnings.push('INTEGRATION_SIMULATOR')
  }
  if (simulatorMode) { /* nothing is sent to the RCS: the real fleet is not a condition */ }
  else if (!fleet) blockers.push('FLEET_UNKNOWN')
  else {
    if (fleet.simulatedData || fleet.source !== 'Real') blockers.push('FLEET_SIMULATED')
    else if (fleet.connection === 'NoData') blockers.push('RCS_NODATA')
    else if (fleet.connection === 'Degraded') blockers.push('RCS_DEGRADED')
    else if (fleet.connection === 'Disconnected') blockers.push('RCS_DISCONNECTED')
    else if (fleet.count === 0) warnings.push('NO_AMR')
  }
  return { allowed: blockers.length === 0, blockers, warnings, integrationMode: health?.integration.mode ?? null, fleetSource: fleet?.source ?? null, executionMode: mode?.mode ?? null }
}

// ------------------------------------------------------------------------------------------------ the stages of one task
export type StageKey = 'created' | 'dispatched' | 'rcsAccepted' | 'agvAssigned' | 'pickup' | 'drop' | 'completed'
/** done = the data proves it; pending = not yet; unknown = the data cannot say (an operator must check); failed = the task ended before this stage. */
export type StageStatus = 'done' | 'pending' | 'unknown' | 'failed'
export interface Stage { key: StageKey; status: StageStatus; at: string | null }

const FINISHED = ['Completed', 'Failed', 'Cancelled']
const firstAt = (h: readonly TaskHistory[], pred: (e: TaskHistory) => boolean): string | null => {
  const hit = h.filter(pred).sort((a, b) => a.occurredAt.localeCompare(b.occurredAt))[0]
  return hit?.occurredAt ?? null
}

/**
 * Seven separate facts, each from its own evidence. "RCS accepted the request" is not "the AGV picked" and neither is "completed": WCS completes a task only after it recorded the drop.
 * Anything the data does not prove stays pending or unknown; a task that ended without evidence for a stage that should precede its end shows that stage as unknown, never as done.
 */
export function taskStages(t: TransportTask, history: readonly TaskHistory[]): Stage[] {
  const ended = FINISHED.includes(t.state)
  const sentAt = firstAt(history, (e) => e.toState === 'Dispatching')
  const acceptedAt = firstAt(history, (e) => e.toState === 'Accepted')
  const dispatchedEvidence = sentAt !== null || t.rcsTaskCode !== null || (t.rcsDispatchState !== null && t.rcsDispatchState !== undefined && t.rcsDispatchState !== 'NotSent')
  const acceptedEvidence = t.rcsTaskCode !== null || acceptedAt !== null || ['Accepted', 'AwaitingAssignment', 'Assigned'].includes(t.rcsDispatchState ?? '')
  const assignedEvidence = !!t.robotCode || t.agvId !== null
  const syncError = t.rcsDispatchState === 'SyncError'

  const stage = (key: StageKey, evidence: boolean, at: string | null, precedes: boolean): Stage => {
    if (evidence) return { key, status: 'done', at }
    if (ended) return { key, status: precedes ? 'unknown' : 'failed', at: null }
    return { key, status: 'pending', at: null }
  }
  return [
    { key: 'created', status: 'done', at: t.createdAt },
    stage('dispatched', dispatchedEvidence, sentAt, acceptedEvidence || assignedEvidence || t.pickConfirmedAt !== null),
    // sent but no answer recorded, and the task already ended: whether the RCS got it is NOT known
    stage('rcsAccepted', acceptedEvidence, acceptedAt, assignedEvidence || t.pickConfirmedAt !== null),
    syncError && !assignedEvidence ? { key: 'agvAssigned', status: 'unknown', at: null } : stage('agvAssigned', assignedEvidence, t.assignedAt ?? null, t.pickConfirmedAt !== null),
    stage('pickup', t.pickConfirmedAt !== null, t.pickConfirmedAt, t.dropConfirmedAt !== null || t.state === 'Completed'),
    stage('drop', t.dropConfirmedAt !== null, t.dropConfirmedAt, t.state === 'Completed'),
    t.state === 'Completed' && t.dropConfirmedAt !== null ? { key: 'completed', status: 'done', at: t.finishedAt }
      : t.state === 'Completed' ? { key: 'completed', status: 'unknown', at: t.finishedAt }   // completed without a recorded drop: the data is inconsistent, say so
      : ended ? { key: 'completed', status: 'failed', at: null } : { key: 'completed', status: 'pending', at: null },
  ]
}

// ------------------------------------------------------------------------------------------------ the task list
export interface TaskFilter { text: string; state: string; from: string; to: string; source: string; destination: string }
export const NO_TASK_FILTER: TaskFilter = { text: '', state: '', from: '', to: '', source: '', destination: '' }

/** `codeOf` turns a location id into its code. Dates are local calendar days (`yyyy-mm-dd`), inclusive. Text matches the display code, the request key and the RCS codes. */
export function filterTasks(tasks: readonly TransportTask[], f: TaskFilter, display: (t: TransportTask) => string, codeOf: (id: string | null) => string): TransportTask[] {
  const q = f.text.trim().toLowerCase()
  const from = f.from ? new Date(`${f.from}T00:00:00`).getTime() : null
  const to = f.to ? new Date(`${f.to}T23:59:59.999`).getTime() : null
  return tasks.filter((t) => {
    if (q && ![display(t), t.requestKey, t.rcsRequestCode, t.rcsTaskCode ?? '', t.robotCode ?? ''].some((s) => s.toLowerCase().includes(q))) return false
    if (f.state && t.state !== f.state) return false
    const created = new Date(t.createdAt).getTime()
    if (from !== null && created < from) return false
    if (to !== null && created > to) return false
    if (f.source && codeOf(t.sourceLocationId ?? t.requestLocationId) !== f.source) return false
    if (f.destination && codeOf(t.destinationLocationId) !== f.destination) return false
    return true
  })
}

// ------------------------------------------------------------------------------------------------ creating a request: one send, never a silent retry
export type CreatePhase = 'idle' | 'confirming' | 'sending' | 'created' | 'refused' | 'unknown'
export interface CreateState { phase: CreatePhase; requestKey: string }
export type CreateEvent = 'ask' | 'cancelAsk' | 'send' | 'created' | 'refused' | 'unknown' | 'reset'

/** `fire` is true only for the ONE transition that may call the server. A second 'send' while sending, after a result, or without a confirmation never fires. */
export function createStep(s: CreateState, e: CreateEvent, newKey: () => string): { state: CreateState; fire: boolean } {
  switch (e) {
    case 'ask': return { state: s.phase === 'idle' ? { ...s, phase: 'confirming' } : s, fire: false }
    case 'cancelAsk': return { state: s.phase === 'confirming' ? { ...s, phase: 'idle' } : s, fire: false }
    case 'send': return s.phase === 'confirming' ? { state: { ...s, phase: 'sending' }, fire: true } : { state: s, fire: false }
    case 'created': case 'refused': case 'unknown': return { state: s.phase === 'sending' ? { ...s, phase: e } : s, fire: false }
    case 'reset': return { state: s.phase === 'sending' || s.phase === 'unknown' ? s : { phase: 'idle', requestKey: newKey() }, fire: false }   // a new key only when the last one is settled
  }
}

/**
 * refused = the server answered "no" (nothing was created). unknown = no usable answer (network, gateway, server fault): the request may or may not have reached WCS, so it is NOT sent again;
 * the operator checks the list by the RequestKey. The create API is idempotent on that key, but the page still never repeats it on its own.
 */
export function classifyCreateError(e: unknown): 'refused' | 'unknown' {
  if (e instanceof ApiRequestError) {
    if (e.status === 0 || e.code === 'NETWORK_ERROR' || e.status >= 500) return 'unknown'
    return 'refused'
  }
  return 'unknown'
}

/** The task a create call made, found in a list by its RequestKey (the only safe way to learn the outcome of an unknown one). */
export const findByRequestKey = (tasks: readonly TransportTask[], key: string): TransportTask | null => tasks.find((t) => t.requestKey === key) ?? null

export const newRequestKey = (): string => `OPS-${globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(16).slice(2)}`}`

/** The server calls the create form makes; the page passes the real client, the tests pass a fake. */
export interface CreateApi {
  create: (flow: string, station: string, requestKey: string) => Promise<TransportTask>
  list: () => Promise<TransportTask[]>
}

/** Where the create state lives (a ref in the page, a plain object in the tests). `get` must return the LATEST state, so two quick clicks cannot both see "confirming". */
export interface CreateHolder { get: () => CreateState; set: (s: CreateState) => void }

export function applyCreate(h: CreateHolder, e: CreateEvent): boolean {
  const r = createStep(h.get(), e, newRequestKey)
  h.set(r.state)
  return r.fire
}

/**
 * The only place that calls the create API. It runs after the operator confirmed; a second call while one is in flight, or after a result, does nothing. A failure is classified
 * (refused / unknown) and is NEVER retried here. Returns the created task, or null.
 */
export async function sendCreate(h: CreateHolder, api: CreateApi, flow: string, station: string): Promise<{ task: TransportTask | null; error: unknown }> {
  if (!applyCreate(h, 'send')) return { task: null, error: null }
  try {
    const task = await api.create(flow, station, h.get().requestKey)
    applyCreate(h, 'created')
    return { task, error: null }
  } catch (error) {
    applyCreate(h, classifyCreateError(error))
    return { task: null, error }
  }
}

/** After an unknown outcome: look the RequestKey up in the list. The call never creates anything. */
export async function checkOutcome(h: CreateHolder, api: CreateApi): Promise<TransportTask | null> {
  try { return findByRequestKey(await api.list(), h.get().requestKey) } catch { return null }
}
