// CR-020: pure helpers of the PLC Monitoring list (filter, sort, what each row may do). No DOM, no i18n: testable without a browser.
// The backend decides the communication status and enforces every guard; this only decides what to offer and, for a disabled action, why.
import type { MonitorPoint, MonitorStatus } from '../api/plcTypes'
import { writeReadiness, type WriteBlock, type WriteRuntime } from './plcWrite'

export const MONITOR_STATUSES: MonitorStatus[] = ['Good', 'Stale', 'Bad', 'Offline', 'Unknown', 'NotConfigured', 'Simulated']

export type SortKey = 'plc' | 'station' | 'signal' | 'status' | 'updated'
export interface MonitorFilter { plc: string; station: string; status: string; text: string }

/** `describe` returns the description as DISPLAYED (in the screen language), so a search finds what the user reads. */
export function filterPoints(rows: MonitorPoint[], f: MonitorFilter, describe: (p: MonitorPoint) => string = (p) => p.description): MonitorPoint[] {
  const q = f.text.trim().toLowerCase()
  return rows.filter((p) =>
    (!f.plc || p.plcId === f.plc) && (!f.station || p.stationId === f.station) && (!f.status || p.status === f.status)
    && (!q || p.signalName.toLowerCase().includes(q) || describe(p).toLowerCase().includes(q)))
}

const STATUS_ORDER = Object.fromEntries(MONITOR_STATUSES.map((s, i) => [s, i])) as Record<string, number>

/** Stable sort; the default order (PLC, station, sequence) is the order the backend sends, so no key means "as received". */
export function sortPoints(rows: MonitorPoint[], key: SortKey | null, dir: 'asc' | 'desc'): MonitorPoint[] {
  if (!key) return rows
  const sign = dir === 'asc' ? 1 : -1
  const cmp = (a: MonitorPoint, b: MonitorPoint): number => {
    switch (key) {
      case 'plc': return a.plcId.localeCompare(b.plcId)
      case 'station': return a.stationId.localeCompare(b.stationId)
      case 'signal': return a.signalName.localeCompare(b.signalName)
      case 'status': return (STATUS_ORDER[a.status] ?? 99) - (STATUS_ORDER[b.status] ?? 99)
      case 'updated': return (a.lastUpdated ? Date.parse(a.lastUpdated) : 0) - (b.lastUpdated ? Date.parse(b.lastUpdated) : 0)
    }
  }
  return rows.map((p, i) => ({ p, i })).sort((x, y) => sign * cmp(x.p, y.p) || x.i - y.i).map((x) => x.p)
}

/** An immediate read needs only a point with an address; it is offered to every role (it only reads) and refused while a read of the same point is already running. A PLC that is offline is NOT a reason to disable it: the click then shows the communication error. */
export type ReadBlock = { ok: true } | { ok: false; reason: 'NO_ADDRESS' | 'PENDING' }
export function readReadiness(p: Pick<MonitorPoint, 'status'>, pending: boolean): ReadBlock {
  if (pending) return { ok: false, reason: 'PENDING' }
  if (p.status === 'NotConfigured') return { ok: false, reason: 'NO_ADDRESS' }
  return { ok: true }
}

/**
 * What the Write action of a row does. Only an Admin ever sees it; PLC -> Software points and the safety/permit signals are never writable (shown as a fixed marker, not a disabled button),
 * every other row reuses the CR-016/CR-019 readiness (runtime mode, address, supported type, write approval, PLC connection) and names the first thing missing.
 */
export type RowWrite = { kind: 'hidden' } | { kind: 'inbound' } | { kind: 'blocked' } | { kind: 'action'; gate: WriteBlock }
export function rowWrite(isAdmin: boolean, runtime: WriteRuntime | null | undefined, plcState: string | undefined, p: MonitorPoint): RowWrite {
  if (!isAdmin) return { kind: 'hidden' }
  if (p.direction === 'PlcToSoftware') return { kind: 'inbound' }
  if (p.writeBlocked) return { kind: 'blocked' }
  return { kind: 'action', gate: writeReadiness(runtime, plcState, { actualAddress: p.address, writeApproval: p.writeApproval ?? 'NOT_APPROVED', writeSupported: p.writeSupported }) }
}
