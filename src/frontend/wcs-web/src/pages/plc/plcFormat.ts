import type { IoPointState, PlcWorkerStatus } from '../../api/plcTypes'
import { formatDateTime } from '../../i18n/format'
import { t } from '../../i18n'

export const fmtTime = (iso: string | null) => (iso ? formatDateTime(iso) : '—')

export function qualitySummary(w: Pick<PlcWorkerStatus, 'goodPoints' | 'configuredPoints' | 'totalPoints'>) {
  return t('plc.qualitySummary', { good: w.goodPoints, configured: w.configuredPoints, total: w.totalPoints })
}


/** Parses the text typed into a value box according to the data type of the signal (strict). */
export function parseTyped(type: IoPointState['dataType'], text: string): { ok: true; value: boolean | number | string } | { ok: false; message: string } {
  if (type === 'Bool') {
    if (['1', 'true', 'bật', 'on'].includes(text.trim().toLowerCase())) return { ok: true, value: true }
    if (['0', 'false', 'tắt', 'off'].includes(text.trim().toLowerCase())) return { ok: true, value: false }
    return { ok: false, message: t('plc.enterBool') }
  }
  if (type === 'Int') {
    if (!/^-?\d+$/.test(text.trim())) return { ok: false, message: t('plc.enterInt') }
    const n = Number(text.trim())
    if (n < -32768 || n > 32767) return { ok: false, message: t('plc.intRange') }
    return { ok: true, value: n }
  }
  return { ok: true, value: text }
}

/** Signals that can never be written by the test facility (mirrors the backend rule; the backend enforces it). */
export const BLOCKED_SIGNALS = ['Safety_Ready', 'Transfer_Permit', 'AGV_Departure_Permit']

/** The device letter of an address (D / W / M), or null when none is assigned. X/Y are not supported by IO mapping (CR-004). */
export const deviceOf = (address: string | null): string | null => (address ? address.trim().charAt(0).toUpperCase() : null)
