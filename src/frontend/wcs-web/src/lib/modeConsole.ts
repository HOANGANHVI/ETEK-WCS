// CR-038 – rules of the mode panel and the communication monitor that need no DOM: what a mode change may offer, how one change is sent exactly once (and why a lost answer may be asked
// again with the SAME RequestId), how a message is labelled and how its payload is read. Tested by `npm run ops:test`.
import { ApiRequestError } from '../api/errors'
import type { ExecMode, ExecutionModeView, ModeReadiness, ModeTransitionBody, ModeTransitionResult, RcsMessageSummary } from '../api/modeTypes'

export const newRequestId = (): string => `MODE-${globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(16).slice(2)}`}`

export const otherMode = (m: ExecMode): ExecMode => (m === 'Simulator' ? 'Real' : 'Simulator')

/** The label shown on every message and next to the mode: never lets a simulated message pass for a real one. */
export const modeTag = (m: ExecMode): 'SIMULATED' | 'REAL' => (m === 'Real' ? 'REAL' : 'SIMULATED')

export type ModeState = 'ready' | 'blocked' | 'recovery' | 'notChosen'

/** What the panel says about the current mode. Recovery wins over everything; "not chosen" is the fresh-environment default. */
export function modeState(view: ExecutionModeView | null): ModeState | null {
  if (!view) return null
  if (view.recoveryRequired) return 'recovery'
  if (!view.isExplicit) return 'notChosen'
  return 'ready'
}

/** A switch is only offered when the server said it can be done; the button never decides by itself. */
export const canConfirmSwitch = (r: ModeReadiness | null, reason: string, acknowledged: boolean): boolean =>
  !!r && r.canSwitch && reason.trim().length > 0 && reason.trim().length <= 500 && (r.target !== 'Real' || acknowledged)

// ------------------------------------------------------------------------------------------------ one change, one send
export type TransitionPhase = 'idle' | 'confirming' | 'sending' | 'done' | 'refused' | 'unknown'
export interface TransitionState { phase: TransitionPhase; requestId: string }
export type TransitionEvent = 'ask' | 'cancel' | 'send' | 'done' | 'refused' | 'unknown' | 'retry' | 'reset'

/**
 * `fire` is true only for the transitions that may call the server. 'send' fires once from "confirming". After a lost answer ('unknown') the SAME request may be asked again with the
 * SAME RequestId ('retry'): the server answers a repeated RequestId with the first result and changes nothing, so this can never switch twice.
 */
export function transitionStep(s: TransitionState, e: TransitionEvent, newId: () => string): { state: TransitionState; fire: boolean } {
  switch (e) {
    case 'ask': return { state: s.phase === 'idle' ? { ...s, phase: 'confirming' } : s, fire: false }
    case 'cancel': return { state: s.phase === 'confirming' ? { ...s, phase: 'idle' } : s, fire: false }
    case 'send': return s.phase === 'confirming' ? { state: { ...s, phase: 'sending' }, fire: true } : { state: s, fire: false }
    case 'retry': return s.phase === 'unknown' ? { state: { ...s, phase: 'sending' }, fire: true } : { state: s, fire: false }
    case 'done': case 'refused': case 'unknown': return { state: s.phase === 'sending' ? { ...s, phase: e } : s, fire: false }
    case 'reset': return { state: s.phase === 'sending' || s.phase === 'unknown' ? s : { phase: 'idle', requestId: newId() }, fire: false }
  }
}

export interface TransitionHolder { get: () => TransitionState; set: (s: TransitionState) => void }
export interface ModeApi { transition: (body: ModeTransitionBody) => Promise<ModeTransitionResult> }

export function applyTransition(h: TransitionHolder, e: TransitionEvent, newId: () => string = newRequestId): boolean {
  const r = transitionStep(h.get(), e, newId)
  h.set(r.state)
  return r.fire
}

/** A refusal by the server (4xx or a result with `succeeded: false`) is final for this RequestId; a network / 5xx failure leaves the outcome unknown. */
export const classifyTransitionError = (e: unknown): 'refused' | 'unknown' =>
  e instanceof ApiRequestError && e.status > 0 && e.code !== 'NETWORK_ERROR' && e.status < 500 ? 'refused' : 'unknown'

export async function sendTransition(h: TransitionHolder, api: ModeApi, target: ExecMode, reason: string, confirmRealAgv: boolean, event: 'send' | 'retry' = 'send'): Promise<{ result: ModeTransitionResult | null; error: unknown }> {
  if (!applyTransition(h, event)) return { result: null, error: null }
  try {
    const result = await api.transition({ requestId: h.get().requestId, target, reason: reason.trim(), confirmRealAgv: target === 'Real' && confirmRealAgv })
    applyTransition(h, result.succeeded ? 'done' : 'refused')
    return { result, error: null }
  } catch (error) {
    applyTransition(h, classifyTransitionError(error))
    return { result: null, error }
  }
}

// ------------------------------------------------------------------------------------------------ messages
/** "10:00:02 · IN · Create Task Response · Accepted" – the timeline row as text (used for the accessible label and the tests). */
export const messageLine = (m: RcsMessageSummary, time: (iso: string) => string): string =>
  [time(m.at), m.direction === 'Out' ? 'OUT' : 'IN', m.messageType, m.result ?? '—'].join(' · ')

/** Rows are shown in the order the server received them (`seq`), which is the time order even when two messages carry the same timestamp. */
export const timelineOrder = (rows: readonly RcsMessageSummary[]): RcsMessageSummary[] =>
  [...rows].sort((a, b) => a.seq - b.seq || a.at.localeCompare(b.at))

export type ResultTone = 'ok' | 'bad' | 'warn' | 'muted'
/** What the colour means: only a real answer of "Accepted" is good; a timeout / connection error is NOT a failure and NOT a success: it is unknown. */
export function resultTone(result: string | null): ResultTone {
  switch (result) {
    case 'Accepted': case 'Applied': case 'Received': return 'ok'
    case 'Rejected': case 'HttpError': case 'Error': return 'bad'
    case 'Timeout': case 'ConnectionError': case 'Unknown': case 'NotSent': return 'warn'
    default: return 'muted'
  }
}

export type PayloadView = { kind: 'none' } | { kind: 'json'; value: unknown; pretty: string } | { kind: 'text'; text: string }
export function readPayload(payload: string | null): PayloadView {
  if (payload === null || payload === '') return { kind: 'none' }
  try { const value: unknown = JSON.parse(payload); return { kind: 'json', value, pretty: JSON.stringify(value, null, 2) } } catch { return { kind: 'text', text: payload } }
}

/** Text to copy for a message: the payload exactly as stored (already masked by the server), pretty-printed when it is JSON. */
export const copyText = (payload: string | null): string => { const v = readPayload(payload); return v.kind === 'json' ? v.pretty : v.kind === 'text' ? v.text : '' }
