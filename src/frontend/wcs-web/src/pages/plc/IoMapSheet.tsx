import type { ReactNode } from 'react'
import type { IoMapInfo } from '../../api/plcTypes'
import { useT } from '../../i18n'
import { mergeSpans, SHEET_LETTERS, type ShownSheetRow } from '../../lib/plcIoMap'

/**
 * CR-047: the "IO Matrix" sheet of the IO map as it is written - the same eleven columns in the same order, the same texts (never translated or reworded) and the same merged cells.
 * What the running system knows about a row (configured address, read and write state) is added to the right, clearly apart; the caller renders it.
 */
export function IoMapSheetTable({ sheet, rows, systemHeaders, system }: {
  sheet: NonNullable<IoMapInfo['sheet']>; rows: ShownSheetRow[]; systemHeaders: string[]; system: (row: ShownSheetRow) => ReactNode
}) {
  const t = useT()
  const spans = mergeSpans(rows)
  return (
    <div className="table-wrap">
      <table className="grid iomap" aria-label={sheet.title}>
        <thead>
          <tr><th colSpan={sheet.columns.length}>{sheet.title}</th><th colSpan={systemHeaders.length} className="iomap-sys">{t('plc.mapSystemColumns')}</th></tr>
          <tr>{sheet.columns.map((c) => <th key={c}>{c}</th>)}{systemHeaders.map((h) => <th key={h} className="iomap-sys">{h}</th>)}</tr>
        </thead>
        <tbody>
          {rows.map((r, i) => (
            <tr key={r.row} data-row={r.row}>
              {SHEET_LETTERS.map((letter, c) => {
                const span = spans[i][c]
                return span === 0 ? null : <td key={letter} rowSpan={span} className={letter === 'H' ? 'mono' : letter === 'D' ? 'iomap-signal' : undefined}>{r.cells[c]}</td>
              })}
              {system(r)}
            </tr>
          ))}
          {rows.length === 0 && <tr><td colSpan={sheet.columns.length + systemHeaders.length} className="muted">{t('plc.noMatch')}</td></tr>}
        </tbody>
      </table>
    </div>
  )
}

/** The sequence sheet: the AGV side on the left, the PLC side on the right, the arrow as drawn. */
export function IoMapSequence({ sequence }: { sequence: NonNullable<IoMapInfo['sequence']> }) {
  const [head, ...steps] = sequence.rows
  const left = ['B', 'C', 'D'], right = ['G', 'J', 'K']
  return (
    <div className="table-wrap">
      <table className="grid iomap" aria-label={sequence.name}>
        <thead><tr><th colSpan={3}>{head?.cells.D}</th><th /><th colSpan={3}>{head?.cells.G}</th></tr></thead>
        <tbody>
          {steps.map((r) => (
            <tr key={r.row}>
              {left.map((c) => <td key={c}>{r.cells[c]}</td>)}
              <td className="iomap-arrow" aria-label={r.arrow ?? ''}>{r.arrow === 'toPlc' ? '⟶' : r.arrow === 'toAgv' ? '⟵' : ''}</td>
              {right.map((c) => <td key={c}>{r.cells[c]}</td>)}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
