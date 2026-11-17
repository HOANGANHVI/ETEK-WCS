import { getLang } from './core'
import { t } from './index'

/** Date/number locale follows the interface language. */
const locale = () => (getLang() === 'en' ? 'en-GB' : 'vi-VN')

export function formatNumber(n: number) {
  return n.toLocaleString(locale())
}

export function formatTime(iso: string | null | undefined) {
  if (!iso) return '—'
  return new Date(iso).toLocaleTimeString(locale(), { hour12: false })
}

export function formatDateTime(iso: string | null | undefined) {
  if (!iso) return '—'
  return new Date(iso).toLocaleString(locale(), { hour12: false, day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit', second: '2-digit' })
}

export function formatDate(d: Date) {
  return d.toLocaleDateString(locale(), { weekday: 'long', day: '2-digit', month: '2-digit', year: 'numeric' })
}

/** "12 giây trước" / "12s ago" … */
export function timeAgo(iso: string | null | undefined, now = Date.now()) {
  if (!iso) return '—'
  const s = Math.max(0, Math.round((now - new Date(iso).getTime()) / 1000))
  if (s < 5) return t('common.justNow')
  if (s < 60) return t('common.secondsAgo', { n: s })
  if (s < 3600) return t('common.minutesAgo', { n: Math.floor(s / 60) })
  if (s < 86400) return t('common.hoursAgo', { n: Math.floor(s / 3600) })
  return t('common.daysAgo', { n: Math.floor(s / 86400) })
}
