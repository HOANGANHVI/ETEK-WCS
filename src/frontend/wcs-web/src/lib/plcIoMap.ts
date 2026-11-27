import type { IoMapInfo } from '../api/plcTypes'

export const SHEET_LETTERS = ['A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K'] as const
type SheetRow = NonNullable<IoMapInfo['sheet']>['rows'][number]
/** A row of the sheet with what identifies it in the running system. A row of a PLC (heartbeat, spare) has no station. */
export interface ShownSheetRow extends SheetRow { plcId: string; stationId: string | null; signalName: string }

/** CR-047: the sheet rows, each tied to its PLC (by the IP written in column A) and - for a station row - to its station and signal (columns B and D). Order and texts are untouched. */
export function sheetRows(map: Pick<IoMapInfo, 'plcs' | 'sheet'>): ShownSheetRow[] {
  return (map.sheet?.rows ?? []).map((r) => {
    const plc = map.plcs.find((p) => r.cells[0].includes(p.ipAddress))
    const station = plc?.stations.includes(r.cells[1]) ? r.cells[1] : null
    return { ...r, plcId: plc?.plcId ?? '', stationId: station, signalName: r.cells[3] }
  })
}

export function filterSheetRows(rows: ShownSheetRow[], f: { plc: string; station: string; text: string }, keep: (r: ShownSheetRow) => boolean = () => true): ShownSheetRow[] {
  const q = f.text.trim().toLowerCase()
  return rows.filter((r) => (!f.plc || r.plcId === f.plc) && (!f.station || r.stationId === f.station)
    && (!q || r.signalName.toLowerCase().includes(q) || r.cells[10].toLowerCase().includes(q) || r.cells[7].toLowerCase().includes(q)) && keep(r))
}

/** The row span of every cell of the rows shown: a merged cell of the workbook spans its consecutive shown rows (0 = covered by the cell above). */
export function mergeSpans(rows: Pick<SheetRow, 'merged'>[]): number[][] {
  const spans = rows.map(() => SHEET_LETTERS.map(() => 1))
  SHEET_LETTERS.forEach((letter, c) => {
    for (let i = 0; i < rows.length;) {
      const key = rows[i].merged[letter]
      let j = i + 1
      while (key && j < rows.length && rows[j].merged[letter] === key) j++
      spans[i][c] = j - i
      for (let k = i + 1; k < j; k++) spans[k][c] = 0
      i = j
    }
  })
  return spans
}

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
