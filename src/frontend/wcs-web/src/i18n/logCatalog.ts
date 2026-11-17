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
import { AUDIT_ACTION_VI, errorTextForCode } from './vi'

const key = (k: string) => k as MsgKey

/* ------------------------------------------------------------------ coded texts */
export function parseCoded(text: string): { code: string | null; rest: string } {
  const m = /^\[([A-Z][A-Z0-9_]+)\]\s*([\s\S]*)$/.exec(text)
  return m ? { code: m[1], rest: m[2] } : { code: null, rest: text }
}

/** Notes / reasons / connection-check messages: the translated sentence for a known code; otherwise the text as stored (without the bracket). */
export function describeCoded(text: string | null | undefined): string {
  if (!text) return ''
  const { code, rest } = parseCoded(text)
  if (code && hasMessage(key(`codes.text_${code}`))) return t(key(`codes.text_${code}`))
  return rest
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
    if (text) return text
  }
  return detail
}

/** Headline of an audit action (the action code is the stable event code). */
export const auditActionLabel = (action: string) => AUDIT_ACTION_VI[action] ?? action

/* ------------------------------------------------------------------ log entries */
export interface LogLike {
  category: string; source: string; device: string | null; event: string | null; message: string
  messageTemplate?: string | null; detailsJson?: string | null
}

function parseJson(text: string | null | undefined): Record<string, unknown> | null {
  if (!text) return null
  try { const v: unknown = JSON.parse(text); return v && typeof v === 'object' ? (v as Record<string, unknown>) : null } catch { return null }
}

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
    const args = details?.args as Record<string, unknown> | undefined
    if (args && hasMessage(key(`codes.plcEvt_${base}`))) {
      const p = str(args)
      if ('reason' in p) p.reason = p.reason ? ` – ${describeCoded(String(p.reason))}` : ''
      return { text: t(key(`codes.plcEvt_${base}`), p), translated: true }
    }
    if (hasMessage(key(`codes.plcEvtTitle_${base}`))) {
      // Older entries carry no parameters: the translated headline is shown, the stored text follows unchanged.
      return { text: `${t(key(`codes.plcEvtTitle_${base}`))} (${e.message})`, translated: true }
    }
    return raw
  }

  if (e.messageTemplate) {
    const tpl = SYSTEM_LOG_TEMPLATES[e.messageTemplate]
    if (tpl) {
      const params: Params = { ...str(details), Device: e.device ?? '' }
      const sentence = (getLang() === 'en' ? tpl.en : tpl.vi).replace(/\{(\w+)\}/g, (m, k: string) => (k in params ? String(params[k]) : m))
      return { text: sentence.trim(), translated: true }
    }
  }
  return raw
}
