import { useCallback, useState } from 'react'
import { api } from '../../api/client'
import { Async, Panel, Pill } from '../../components/ui'
import { useApi } from '../../hooks/useApi'
import { useT } from '../../i18n'
import { LOG_LEVEL_TONE, LOG_LEVEL_VI } from '../../i18n/plc'
import { fmtTime } from './plcFormat'

const PLC_IDS = ['PLC-01', 'PLC-02', 'PLC-03', 'PLC-04']

/** Admin-only communication log (state changes, connect errors, refused writes, handshake actions). Never contains data payloads or secrets. */
export function PlcLogsTab() {
  const t = useT()
  const [plcId, setPlcId] = useState('')
  const [level, setLevel] = useState('')
  const [take, setTake] = useState(100)
  const logs = useApi(useCallback(() => api.plcLogs({ plcId: plcId || undefined, minLevel: level || undefined, take }), [plcId, level, take]), ['PlcLog'])
  return (
    <Panel title={t('plc.commLog')} flush actions={
      <>
        <select aria-label={t('plc.filterPlc')} value={plcId} onChange={(e) => { setPlcId(e.target.value); setTake(100) }}>
          <option value="">{t('plc.allPlc')}</option>{PLC_IDS.map((p) => <option key={p} value={p}>{p}</option>)}
        </select>
        <select aria-label={t('plc.filterLevel')} value={level} onChange={(e) => { setLevel(e.target.value); setTake(100) }}>
          <option value="">{t('plc.allLevels')}</option>{Object.entries(LOG_LEVEL_VI).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
        </select>
      </>
    }>
      <Async state={logs} isEmpty={(d) => d.length === 0} empty={{ title: t('plc.noLogs'), hint: t('plc.noLogsHint') }}>
        {(rows) => (
          <>
            <div className="table-wrap">
              <table className="grid">
                <thead><tr><th>{t('plc.logTime')}</th><th>{t('plc.logLevel')}</th><th>PLC</th><th>{t('plc.logStation')}</th><th>{t('plc.logEvent')}</th><th>{t('plc.logMessage')}</th></tr></thead>
                <tbody>
                  {rows.map((l) => (
                    <tr key={l.id}>
                      <td className="nowrap num">{fmtTime(l.occurredAt)}</td>
                      <td><Pill tone={LOG_LEVEL_TONE[l.level]} dot={false}>{LOG_LEVEL_VI[l.level]}</Pill></td>
                      <td>{l.plcId ?? '—'}</td><td>{l.stationId ?? '—'}</td><td className="mono">{l.event}</td><td>{l.message}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            {rows.length >= take && <div className="pad"><button type="button" className="btn btn-secondary btn-sm" onClick={() => setTake(take + 100)}>{t('common.loadMore')}</button></div>}
          </>
        )}
      </Async>
    </Panel>
  )
}
