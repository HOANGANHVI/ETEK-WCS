// Pure helpers of the Server Resource Monitoring panel (no DOM, no React) so they can be tested with `npm run monitor:test`.
// The frontend never judges numbers (the server sends the status) and never estimates a missing one; it only decides how fresh the data is.
import type { MetricStatus, ResourceHistoryPoint, ResourceMetric, ResourceSnapshot } from '../api/types'

export type ConnectionState = 'loading' | 'live' | 'disconnected' | 'disabled'

/**
 * How trustworthy the figures on screen are. Data is "live" only while the last successful answer is younger than three polling intervals (+2 s) and the last
 * request did not fail. Anything older is NOT shown as a current value: the panel treats it as disconnected.
 */
export function connectionState(a: { snapshot: ResourceSnapshot | null; lastOkAt: number | null; now: number; lastFailed: boolean }): ConnectionState {
  if (!a.snapshot || a.lastOkAt === null) return a.lastFailed ? 'disconnected' : 'loading'
  if (!a.snapshot.enabled) return 'disabled'
  const interval = Math.max(2, a.snapshot.intervalSeconds)
  const limit = (interval * 3 + 2) * 1000
  // two independent clocks: how long ago the answer ARRIVED, and how old the sample was when the server sent it (a sampler that stalled behind a still-answering API)
  if (a.lastFailed || a.now - a.lastOkAt > limit || (a.snapshot.sampleAgeSeconds ?? 0) * 1000 > limit) return 'disconnected'
  return 'live'
}

/** Seconds between two polls: the server's sampling interval, kept within 3-30 s (one polling loop for the whole page). */
export const pollSeconds = (snapshot: ResourceSnapshot | null): number => Math.min(30, Math.max(3, snapshot?.intervalSeconds ?? 5))

export type ByteUnit = 'B' | 'KB' | 'MB' | 'GB' | 'TB'
/** Binary scaling (1 KB = 1024 B), the way container tools report memory and disks. */
export function scaleBytes(n: number): { value: number; unit: ByteUnit } {
  const units: ByteUnit[] = ['B', 'KB', 'MB', 'GB', 'TB']
  let v = n, i = 0
  while (Math.abs(v) >= 1024 && i < units.length - 1) { v /= 1024; i++ }
  return { value: v, unit: units[i] }
}

export function splitUptime(seconds: number): { days: number; hours: number; minutes: number } {
  const s = Math.max(0, Math.floor(seconds))
  return { days: Math.floor(s / 86400), hours: Math.floor((s % 86400) / 3600), minutes: Math.floor((s % 3600) / 60) }
}

export type HistoryKey = 'cpu' | 'memory'
/** The values of one series for the chosen scope; a point without a measurement stays null (a gap in the chart, never a zero). */
export function seriesOf(points: ResourceHistoryPoint[], scope: 'container' | 'host', key: HistoryKey): (number | null)[] {
  return points.map((p) => (scope === 'container' ? (key === 'cpu' ? p.containerCpu : p.containerMemory) : (key === 'cpu' ? p.hostCpu : p.hostMemory)))
}

/** Metrics of a group whose server-side status is Warning / Critical, worst first. */
export function alertsOf(group: Record<string, ResourceMetric>, keys: string[]): { key: string; status: Extract<MetricStatus, 'Warning' | 'Critical'> }[] {
  const out: { key: string; status: 'Warning' | 'Critical' }[] = []
  for (const k of keys) { const s = group[k]?.status; if (s === 'Warning' || s === 'Critical') out.push({ key: k, status: s }) }
  return out.sort((a, b) => (a.status === b.status ? 0 : a.status === 'Critical' ? -1 : 1))
}

/** Polyline segments of a series: consecutive non-null points; a null breaks the line so nothing is interpolated across a gap. */
export function segmentsOf(values: (number | null)[]): { i: number; v: number }[][] {
  const segs: { i: number; v: number }[][] = []
  let cur: { i: number; v: number }[] = []
  values.forEach((v, i) => { if (v === null) { if (cur.length) segs.push(cur); cur = [] } else cur.push({ i, v }) })
  if (cur.length) segs.push(cur)
  return segs
}

/** "3 d 4 h 12 min" / "4 h 12 min" / "12 min" through the caller's translator (keeps this file free of i18n imports). */
export function formatUptime(seconds: number, t: (key: 'monitor.uptimeDhm' | 'monitor.uptimeHm' | 'monitor.uptimeM', params: Record<string, number>) => string): string {
  const u = splitUptime(seconds)
  return u.days > 0 ? t('monitor.uptimeDhm', { d: u.days, h: u.hours, m: u.minutes }) : u.hours > 0 ? t('monitor.uptimeHm', { h: u.hours, m: u.minutes }) : t('monitor.uptimeM', { m: u.minutes })
}
