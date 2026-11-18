// CR-016: whether the "Ghi PLC" action of one IO point can be used right now, and if not, exactly why. Pure (no DOM, no i18n) so it can be tested; the page maps the reason code to text.
// It mirrors what the backend will enforce (it never replaces it): runtime mode -> address -> supported type -> write approval -> PLC connection.

export interface WriteRuntime { effectiveMode: string; missingWriteKeys?: string[] | null }
export interface WritePoint { actualAddress: string | null; writeApproval: string; writeSupported: boolean | null }

export type WriteBlock =
  | { ok: true }
  | { ok: false; reason: 'RUNTIME_UNKNOWN' | 'READ_ONLY' | 'NO_ADDRESS' | 'UNSUPPORTED' | 'NOT_APPROVED' | 'REJECTED' | 'PLC_NOT_CONNECTED'; missing?: string[] }

/** The runtime writes to the simulator or - only with every key - to a real PLC; every other effective mode is read-only. */
export const runtimeAllowsWrite = (r: WriteRuntime | null | undefined): boolean => !!r && (r.effectiveMode === 'Simulator' || r.effectiveMode === 'RealReadWrite')

export function writeReadiness(runtime: WriteRuntime | null | undefined, plcState: string | undefined, p: WritePoint): WriteBlock {
  if (!runtime) return { ok: false, reason: 'RUNTIME_UNKNOWN' }                                           // not loaded yet: never assume it is allowed
  if (!runtimeAllowsWrite(runtime)) return { ok: false, reason: 'READ_ONLY', missing: runtime.missingWriteKeys ?? [] }
  if (!p.actualAddress) return { ok: false, reason: 'NO_ADDRESS' }
  if (p.writeSupported === false) return { ok: false, reason: 'UNSUPPORTED' }
  if (p.writeApproval === 'REJECTED') return { ok: false, reason: 'REJECTED' }
  if (p.writeApproval !== 'APPROVED') return { ok: false, reason: 'NOT_APPROVED' }
  if (plcState !== 'Connected') return { ok: false, reason: 'PLC_NOT_CONNECTED' }                         // the simulator reports Connected too
  return { ok: true }
}

export interface WriteKeyRuntime { mode?: string; allowLiveConnection?: boolean; writeEnabled?: boolean; writeApprovalReferenceSet?: boolean; writeHandshakeApproved?: boolean }
export interface WriteKeyState { key: string; ok: boolean; current?: string }

/**
 * CR-017: the five deployment settings a real write needs, each judged by what the BACKEND reports it received (never by what the .env is supposed to contain).
 * The approval reference is only ever "set" or not: its value is not available to the browser.
 */
export function writeKeyChecklist(r: WriteKeyRuntime): WriteKeyState[] {
  return [
    { key: 'PLC_MODE', ok: r.mode === 'RealReadWrite', current: r.mode },
    { key: 'PLC_ALLOW_LIVE_CONNECTION', ok: r.allowLiveConnection === true },
    { key: 'PLC_WRITE_ENABLED', ok: r.writeEnabled === true },
    { key: 'PLC_WRITE_APPROVAL_REFERENCE', ok: r.writeApprovalReferenceSet === true },
    { key: 'PLC_WRITE_HANDSHAKE_APPROVED', ok: r.writeHandshakeApproved === true },
  ]
}
