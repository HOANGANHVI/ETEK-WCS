import type { MapDataSet } from '../api/rcsMapTypes'
import { useT, type MsgKey } from '../i18n'
import { countByStatus, repeatedCallCodes } from '../lib/mapData'

/** CR-067: the result of matching the Excel rows to the XML points. Every row that was not attached is listed with its reason. */
export function MapDataReport({ data, pointsWithoutRow }: { data: MapDataSet; pointsWithoutRow: number[] }) {
  const t = useT()
  const problems = data.rows.filter((r) => r.status !== 'Matched')
  const repeated = repeatedCallCodes(data)
  return (
    <>
      <p>{t('config.mdSummary', { rows: data.rows.length, file: data.fileName })}</p>
      <ul>
        {countByStatus(data).filter((c) => c.n > 0 || c.status === 'Matched').map((c) => <li key={c.status} data-status={c.status}>{t(`config.mdStatus_${c.status}` as MsgKey)}: <strong>{c.n}</strong></li>)}
        <li data-status="PointsWithoutRow">{t('config.mdNoRow')}: <strong>{pointsWithoutRow.length}</strong>{pointsWithoutRow.length > 0 ? ` (#${pointsWithoutRow.slice(0, 50).join(', #')}${pointsWithoutRow.length > 50 ? ', …' : ''})` : ''}</li>
      </ul>
      {repeated.length > 0 && <div className="notice notice-warn">{t('config.mdRepeated', { codes: repeated.join(', ') })}</div>}
      {problems.length > 0 && (
        <details>
          <summary>{t('config.mdProblems', { n: problems.length })}</summary>
          <div className="table-wrap capped">
            <table className="grid">
              <thead><tr><th>{t('config.mdRow')}</th><th>{'Map Data ID'}</th><th>{'Call code'}</th><th>{'Data type'}</th><th>{t('config.mdReason')}</th></tr></thead>
              <tbody>{problems.map((r) => <tr key={r.rowNumber}><td className="num">{r.rowNumber}</td><td className="mono">{r.mapDataId ?? '–'}</td><td className="mono">{r.callCode ?? '–'}</td><td>{r.dataType ?? '–'}</td><td>{t(`config.mdStatus_${r.status}` as MsgKey)}</td></tr>)}</tbody>
            </table>
          </div>
        </details>
      )}
    </>
  )
}
