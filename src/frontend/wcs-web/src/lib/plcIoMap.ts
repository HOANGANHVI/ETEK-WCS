// CR-046: pure helpers of the IO-map views (no DOM, no i18n: testable without a browser). The lists come from the backend; no station or signal is hard-coded here.
import type { IoPointConfig } from '../api/plcTypes'

/**
 * The value to SHOW. A point without a configured address has no device value: what the simulator answers for it by default (0 / off / empty) is a placeholder and is not shown. A value that was
 * deliberately set in the simulator is still shown (its status says it is simulated).
 */
export function shownValue(p: { hasAddress?: boolean; value: boolean | number | string | null }): boolean | number | string | null {
  if (p.hasAddress === false && (p.value === 0 || p.value === false || p.value === '')) return null
  return p.value
}

/** Counts of a station as shown: only points with a configured address are "configured" or "good" (the simulator also answers for the others). */
export function addressedCounts(points: { hasAddress?: boolean; quality: string }[]): { configured: number; good: number } {
  const mine = points.filter((p) => p.hasAddress !== false)
  return { configured: mine.length, good: mine.filter((p) => p.quality === 'Good' || p.quality === 'Simulated').length }
}

/** The PLC filter options: the PLCs that actually have rows. */
export const plcOptions = (rows: Pick<IoPointConfig, 'plcId'>[]): string[] => [...new Set(rows.map((p) => p.plcId))].sort()

/** The station filter options of one PLC (or of all), from the rows. */
export const stationOptions = (rows: Pick<IoPointConfig, 'plcId' | 'stationId'>[], plc: string): string[] =>
  [...new Set(rows.filter((p) => !plc || p.plcId === plc).map((p) => p.stationId))].sort()

/** Reading needs a configured address that passes validation (the backend's "pollable"). */
export type ReadState = 'enabled' | 'noAddress' | 'blocked'
export const readState = (p: Pick<IoPointConfig, 'actualAddress' | 'pollable'>): ReadState => (!p.actualAddress ? 'noAddress' : p.pollable ? 'enabled' : 'blocked')

/**
 * What can be said about WRITING a row, in the order the backend refuses: a row of an earlier map, a PLC input, a signal the PLC engineer has not confirmed, a safety/permit signal,
 * no Admin approval. "approved" is NOT "will be written": a real write also needs the runtime mode and the deployment declaration.
 */
export type WriteState = 'retired' | 'input' | 'notConfirmed' | 'protected' | 'approved'
export function writeState(p: Pick<IoPointConfig, 'inIoMap' | 'direction' | 'confirmed' | 'writeApproval' | 'signalName'>, blockedSignals: readonly string[]): WriteState {
  if (p.inIoMap === false) return 'retired'
  if (p.direction === 'PlcToSoftware') return 'input'
  if (p.confirmed === false) return 'notConfirmed'
  if (blockedSignals.includes(p.signalName) || p.writeApproval !== 'APPROVED') return 'protected'
  return 'approved'
}
