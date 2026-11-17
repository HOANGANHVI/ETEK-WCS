/**
 * Language-aware presentation of backend-produced text that is NOT a ready translated sentence:
 *  - coded texts (`[CODE] detail`) of runtime notes, reasons and connection checks,
 *  - field validation errors (`code` + `args`),
 *  - log entries: audit (action code + fixed detail constants), PLC events (event code + `args`), system events (stable message template + parameters).
 *
 * Nothing here guesses a translation from free text: an entry without a known code / template is shown exactly as stored. The raw text is always
 * returned too (`raw`) so the original technical message stays available for diagnosis.
 */
import type { FieldError } from '../api/configTypes'
import { getLang, type Params } from './core'
import { hasMessage, t, type MsgKey } from './index'
import { SYSTEM_LOG_TEMPLATES } from './systemLogTemplates'
import { MISSING_PARAM_VI } from './plc'
import { AUDIT_ACTION_VI, errorTextForCode, LEG_VI, TASK_STATE_VI } from './vi'

const key = (k: string) => k as MsgKey

/* ------------------------------------------------------------------ coded texts */
export function parseCoded(text: string): { code: string | null; rest: string } {
  const m = /^\[([A-Z][A-Z0-9_]+)\]\s*([\s\S]*)$/.exec(text)
  return m ? { code: m[1], rest: m[2] } : { code: null, rest: text }
}

/**
 * Notes / reasons / connection-check messages: the translated sentence for a known code (its technical detail, e.g. a list of parameter names, is inserted as `{list}`);
 * a text without a known code is returned as stored (without the bracket) because it cannot be translated reliably.
 */
export function describeCoded(text: string | null | undefined): string {
  if (!text) return ''
  const { code, rest } = parseCoded(text)
  if (code && hasMessage(key(`codes.text_${code}`))) {
    const list = code === 'PLC_PARAMS_MISSING' ? rest.split(',').map((x) => MISSING_PARAM_VI[x.trim()] ?? x.trim()).join(', ') : rest
    return t(key(`codes.text_${code}`), { list })
  }
  return rest
}

/** The `error` of a connection check: a list of field errors (code + parameters) is phrased in the selected language; any other technical text is shown as stored. */
export function describeTestError(error: string | null | undefined): string {
  if (!error) return ''
  const { code, rest } = parseCoded(error)
  if (code === 'CONFIG_INVALID') {
    try {
      const items = JSON.parse(rest) as { field: string; code?: string | null; args?: Record<string, unknown> | null }[]
      return items.map((e) => localizeFieldError({ field: e.field, message: e.field, code: e.code, args: e.args })).join('; ')
    } catch { return rest }
  }
  return describeCoded(error)
}

/* ------------------------------------------------------------------ field errors */
const str = (o: Record<string, unknown> | null | undefined): Params =>
  Object.fromEntries(Object.entries(o ?? {}).filter(([, v]) => v !== null && v !== undefined).map(([k, v]) => [k, typeof v === 'number' ? v : String(v)]))

/** The translated message of a field error; the server's own text when the error has no (known) code. */
export function localizeFieldError(e: FieldError): string {
  if (!e.code) return e.message
  const args = e.args as Record<string, unknown> | null | undefined
  let k = `codes.field_${e.code}`
  if (e.code === 'RADIX_UNCONFIRMED' || e.code === 'TYPE_DEVICE') k += `_${String(args?.kind ?? '')}`
  if (!hasMessage(key(k))) return e.message
  const params = str(args)
  if (e.code === 'RANGE') params.label = hasMessage(key(`codes.fieldLabel_${e.field}`)) ? t(key(`codes.fieldLabel_${e.field}`)) : e.field
  return t(key(k), params)
}

/* ------------------------------------------------------------------ audit */
const AUDIT_CONSTANTS: Record<string, string> = {
  'unknown account': 'unknown_account', 'wrong password': 'wrong_password', locked: 'locked', 'disabled account': 'disabled_account',
  'current password incorrect': 'current_password_incorrect', 'first administrator created (bootstrap)': 'bootstrap_admin',
  online: 'online', offline: 'offline', failing: 'failing', healthy: 'healthy',
}

/** Audit "detail": fixed constants and `CODE: message` failures are translated; text typed by a user (a reason, a note) is shown as written. */
export function localizeAuditDetail(detail: string | null | undefined): string {
  if (!detail) return ''
  const c = AUDIT_CONSTANTS[detail]
  if (c) return t(key(`codes.auditDetail_${c}`))
  const flow = /^flow=(\w+), station=([\w-]+)$/.exec(detail)
  if (flow) return t('codes.auditDetail_flow', { flow: flow[1], station: flow[2] })
  const del = /^(?:Đã xóa cứng nhiệm vụ|Task hard-deleted) (NV-[0-9A-F]+)$/.exec(detail)
  if (del) return t('codes.auditDetail_taskDeleted', { code: del[1] })
  const failed = /^([A-Z][A-Z0-9_]+): ([\s\S]*)$/.exec(detail)
  if (failed) {
    const text = errorTextForCode(failed[1])
    // a failure whose code has no wording yet: the code itself (technical, identical in every language); the stored sentence stays in the tooltip
    return text ?? failed[1]
  }
  return detail
}

/** Headline of an audit action (the action code is the stable event code). */
export const auditActionLabel = (action: string) => AUDIT_ACTION_VI[action] ?? action

/* ------------------------------------------------------------------ task history */
/**
 * Task history detail / state reason in the selected language, from the stable code and its parameters. Without a code (an external RCS message, or text that could not
 * be identified reliably) the original text is returned unchanged. The same function serves entries written before and after codes existed.
 */
export function describeHistory(code: string | null | undefined, args: Record<string, string> | null | undefined, raw: string | null | undefined): string {
  if (!code || !hasMessage(key(`taskHistory.d_${code}`))) return raw ?? ''
  const p = str(args)
  if (typeof p.leg === 'string') p.leg = LEG_VI[p.leg] ?? p.leg
  if (typeof p.state === 'string') p.state = TASK_STATE_VI[p.state as keyof typeof TASK_STATE_VI] ?? p.state
  // the hold reason is itself a reason code (e.g. RCS_ENDED_WITHOUT_DROP): shown in words when known
  if (code === 'AGV_DISPATCH_HOLD' && typeof p.reason === 'string') p.reason = describeReasonCode(p.reason)
  return t(key(`taskHistory.d_${code}`), p)
}

/** A reason that is itself a backend reason code (the AGV hold reason): translated when the code is known, otherwise as stored. */
export function describeReasonCode(text: string | null | undefined): string {
  if (!text) return ''
  return /^[A-Z][A-Z0-9_]+$/.test(text) && hasMessage(key(`taskHistory.d_${text}`)) ? t(key(`taskHistory.d_${text}`)) : text
}

/* ------------------------------------------------------------------ log entries */
export interface LogLike {
  category: string; source: string; device: string | null; event: string | null; message: string
  messageTemplate?: string | null; detailsJson?: string | null
}

function parseJson(text: string | null | undefined): Record<string, unknown> | null {
  if (!text) return null
  try { const v: unknown = JSON.parse(text); return v && typeof v === 'object' ? (v as Record<string, unknown>) : null } catch { return null }
}

/**
 * PLC events stored before the parameters were kept: the parameters are read back from the exact text WCS itself wrote for that event code (anchored on the whole message,
 * scoped to the event). A message that does not match is never interpreted; the entry then shows the headline of the event only.
 */
const LEGACY_PLC: Record<string, { re: RegExp; names: string[] }> = {
  STATION_ERROR: { re: /^(?:Lỗi xử lý trạm|Station processing error): ([\s\S]+)$/, names: ['error'] },
  CONNECT_FAILED: { re: /^(?:Kết nối thất bại|Connection failed): ([\s\S]+?)\. (?:Thử lại sau|Retrying in) (\d+) ms\.$/, names: ['error', 'backoffMs'] },
  LINK_LOST: { re: /^(?:Mất kết nối|Connection lost): ([\s\S]+?)\. (?:Thử kết nối lại sau|Reconnecting in) (\d+) ms\.$/, names: ['error', 'backoffMs'] },
  READ_BAD: { re: /^(\d+) IO point(?:\(s\))? (?:đọc lỗi|failed to read): ([\s\S]+)$/, names: ['count', 'error'] },
  STATE: { re: /^(\S+): (\w+)(?: - ([\s\S]+))?$/, names: ['plc', 'state', 'reason'] },
  WRITE: { re: /^(\S+)\/(\S+) = ([\s\S]*)$/, names: ['station', 'signal', 'value'] },
  WRITE_FAILED: { re: /^(\S+)\/(\S+): ([\s\S]+)$/, names: ['station', 'signal', 'error'] },
  WRITE_AMBIGUOUS: { re: /^(\S+)\/(\S+): ([\s\S]*?) (?:KHÔNG tự động ghi lại|Not retried automatically)/, names: ['station', 'signal', 'error'] },
  WRITE_DENIED: { re: /^(\S+): (\S+)\/(\S+) (?:bị chặn|blocked) \((\w+)\)\.$/, names: ['actor', 'station', 'signal', 'code'] },
  RELOAD: { re: /^(\S+): (?:cấu hình được nạp lại|configuration reloaded)\.$/, names: ['plc'] },
  RUNTIME_START: { re: /^PLC runtime started in mode (\w+) with (\d+) PLC workers\.$/, names: ['mode', 'workers'] },
  HANDSHAKE: { re: /^(\w+) = (\d)$/, names: ['signal', 'value'] },
  HANDSHAKE_WRITE_REFUSED: { re: /^(\w+): (\w+)$/, names: ['signal', 'code'] },
  HANDSHAKE_TIMEOUT: { re: /^(\w+) (?:vẫn bật sau|still on after) (\d+) ms/, names: ['signal', 'ms'] },
  REQUEST_ID_MISSING: { re: /^(\w+) (?:bật nhưng|is on but)/, names: ['signal'] },
  REQUEST: { re: /^(\w+) -> (\w+), Request_ID ([\s\S]+)$/, names: ['signal', 'flow', 'requestId'] },
  REQUEST_HANDLED: { re: /^(\S+) (?:đã được WCS ghi nhận|recorded by WCS)/, names: ['requestKey'] },
  TRANSFER_PERMIT_NOT_APPROVED: { re: /^(?:Nhiệm vụ|Task) (\S+) (?:xin phép|requests) (\w+)(?: permission)? (?:tại|at) (\S+):/, names: ['taskId', 'direction', 'station'] },
}

function legacyPlcArgs(base: string, message: string): Record<string, string> | undefined {
  const rule = LEGACY_PLC[base]
  const m = rule?.re.exec(message)
  if (!rule || !m) return undefined
  return Object.fromEntries(rule.names.map((n, i) => [n, m[i + 1]]).filter(([, v]) => v !== undefined))
}

const VIETNAMESE = /[àáạảãâầấậẩẫăằắặẳẵèéẹẻẽêềếệểễìíịỉĩòóọỏõôồốộổỗơờớợởỡùúụủũưừứựửữỳýỵỷỹđ]/i

const PLC_EVENT_PREFIXES = ['READ_BAD', 'STATION_ERROR']
const plcBase = (event: string) => PLC_EVENT_PREFIXES.find((p) => event === p || event.startsWith(`${p}_`)) ?? event

export interface LocalizedLog { text: string; /** true when the sentence was built from a code / template; false when the stored text is shown as it is. */ translated: boolean }

/** The log entry text in the language currently selected, for new and historical entries alike. */
export function localizeLog(e: LogLike): LocalizedLog {
  const raw: LocalizedLog = { text: e.message, translated: false }
  const details = parseJson(e.detailsJson)

  if (e.category === 'SECURITY_AUDIT' && e.source === 'Audit' && e.event) {
    const noDetail = new RegExp(`^${e.event} (Success|Failed|Denied)$`).test(e.message)
    const head = auditActionLabel(e.event)
    const detail = noDetail ? '' : localizeAuditDetail(e.message)
    return { text: detail ? `${head} — ${detail}` : head, translated: e.event in AUDIT_ACTION_VI }
  }

  if (e.category === 'PLC' && e.event) {
    const base = plcBase(e.event)
    const args = (details?.args as Record<string, unknown> | undefined) ?? legacyPlcArgs(base, e.message)
    const title = hasMessage(key(`codes.plcEvtTitle_${base}`)) ? t(key(`codes.plcEvtTitle_${base}`)) : null
    if (args && hasMessage(key(`codes.plcEvt_${base}`))) {
      const p = str(args)
      // STATE / DISABLED carry an optional reason: the event stores no parameter when there is none, so the placeholder must still be filled
      if (base === 'STATE' || base === 'DISABLED' || 'reason' in p) p.reason = p.reason ? ` – ${describeCoded(String(p.reason))}` : ''
      const sentence = t(key(`codes.plcEvt_${base}`), p)
      // English never carries a Vietnamese fragment: if a stored parameter is Vietnamese, only the headline is shown (the stored text stays available as the original message)
      if (!(getLang() === 'en' && VIETNAMESE.test(sentence))) return { text: sentence, translated: true }
    }
    // Entries whose parameters cannot be read back: only the translated headline; the stored text is shown separately as the original message.
    if (title) return { text: title, translated: true }
    return raw
  }

  if (e.messageTemplate) {
    const tpl = SYSTEM_LOG_TEMPLATES[e.messageTemplate]
    if (tpl) {
      const params: Params = { ...str(details), Device: e.device ?? '' }
      if (typeof params.Reason === 'string') params.Reason = describeReasonCode(params.Reason)   // a reason that is a known code is shown in words
      const sentence = (getLang() === 'en' ? tpl.en : tpl.vi).replace(/\{(\w+)\}/g, (m, k: string) => (k in params ? String(params[k]) : m))
      if (!(getLang() === 'en' && VIETNAMESE.test(sentence))) return { text: sentence.trim(), translated: true }
    }
  }
  return raw
}
