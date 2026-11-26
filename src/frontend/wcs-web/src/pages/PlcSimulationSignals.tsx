import { useCallback, useState } from 'react'
import { api } from '../api/client'
import type { SimStartResult, SimStation } from '../api/simTypes'
import { Async, Button, Pill } from '../components/ui'
import { useApi } from '../hooks/useApi'
import { useT } from '../i18n'
import { formatDateTime } from '../i18n/format'
import { describeError } from '../i18n/vi'
import { signalDescription } from '../lib/plcSignals'

/** CR-040: set one PLC → WCS signal at a time (bit ON / OFF or a value), then hand the station's signals to the request intake. The Station Transaction shows the handshake state. */
export function PlcSimulationSignals({ active, real, onResult }: { active: boolean; real: boolean; onResult: (r: SimStartResult) => void }) {
  const t = useT()
  const stations = useApi(useCallback(() => api.simStations(), []), ['OrderChanged'])
  const history = useApi(useCallback(() => api.simHistory(), []))
  const [id, setId] = useState('')
  const [ack, setAck] = useState(false)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const run = async (fn: () => Promise<unknown>) => {
    setBusy(true); setMessage(null)
    try { await fn() } catch (e) { setMessage(describeError(e)) } finally { setBusy(false); stations.reload(); history.reload() }
  }
  return (
    <div className="card pad stack">
      <strong>{t('linked.simTitle')}</strong>
      <span className="muted">{t('linked.simIntro')}</span>
      {real && <div className="notice notice-warn" role="note">{t('linked.simRealBlocked')}</div>}
      {message && <div className="notice notice-info" role="status">{t('linked.simResponse')} {message}</div>}
      <Async state={stations} empty={{ title: t('linked.simNoStations') }} isEmpty={(s) => s.length === 0}>
        {(list: SimStation[]) => {
          const s = list.find((x) => x.stationId === id)
          return (
            <>
              <select value={id} aria-label={t('linked.simStation')} onChange={(e) => { setId(e.target.value); setAck(false) }}>
                <option value="">{t('linked.simStation')}</option>
                {list.map((x) => <option key={x.stationId} value={x.stationId}>{x.stationId} · {x.locationCode} · {x.plcId}{x.plcOnline ? ` · ${t('linked.simPlcOnline')}` : ''}</option>)}
              </select>
              {s && (
                <>
                  <div className={s.engine === 'Invalid' ? 'notice notice-bad' : 'muted'}>{t(`linked.engine_${s.engine ?? 'Unassigned'}` as never)}</div>
                  <div className="muted">{s.activeTransaction ? t('linked.simTx', { state: s.activeTransaction.state, step: s.activeTransaction.handshakeStep, request: s.activeTransaction.requestId ?? '—' }) : t('linked.simNoTx')}</div>
                  <div className="table-wrap">
                    <table className="grid">
                      <thead><tr><th>{t('linked.simSignal')}</th><th>{t('linked.simAddress')}</th><th>{t('linked.simDescription')}</th><th>{t('linked.simValue')}</th><th /></tr></thead>
                      <tbody>
                        {s.signals.map((x) => <SignalRow key={`${s.stationId}:${x.signal}:${x.value ?? ''}`} signal={x} disabled={!active || busy || !!s.plcOnline} onSet={(v) => void run(async () => { await api.simSetSignal(s.stationId, x.signal, v); setMessage(t('linked.simStored', { signal: x.signal })) })} />)}
                      </tbody>
                    </table>
                  </div>
                  <label><input type="checkbox" checked={ack} onChange={(e) => setAck(e.target.checked)} /> {t('linked.simAck')}</label>
                  <div className="btn-row">
                    <Button variant="primary" disabled={!active || real || busy || !ack || !!s.plcOnline} onClick={() => void run(async () => { const r = await api.simEvaluate(s.stationId, ack); onResult(r); setMessage(`${r.outcome} / ${r.code} – ${r.message}`) })}>{t('linked.simEvaluate')}</Button>
                    <Button disabled={!active || busy || !!s.plcOnline} onClick={() => void run(async () => { await api.simReset(s.stationId); setMessage(t('linked.simResetDone')) })}>{t('linked.simReset')}</Button>
                  </div>
                </>
              )}
            </>
          )
        }}
      </Async>
      <strong>{t('linked.simHistory')}</strong>
      <Async state={history} empty={{ title: t('linked.simNoHistory') }} isEmpty={(h) => h.length === 0}>
        {(rows) => (
          <div className="table-wrap capped">
            <table className="grid">
              <thead><tr><th>{t('linked.colCreated')}</th><th>{t('linked.simWho')}</th><th>{t('linked.simAction')}</th><th>{t('linked.simSignal')}</th><th>{t('linked.cmdResult')}</th></tr></thead>
              <tbody>
                {rows.map((h) => (
                  <tr key={h.id}>
                    <td className="nowrap">{formatDateTime(h.occurredAt)}</td>
                    <td>{h.actor ?? '—'}</td>
                    <td className="mono">{h.action}</td>
                    <td className="mono">{h.targetId ?? '—'}</td>
                    <td>{h.result}{h.afterJson ? <div className="muted small mono">{h.afterJson}</div> : null}{h.detail ? <div className="muted small">{h.detail}</div> : null}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Async>
    </div>
  )
}

function SignalRow({ signal, disabled, onSet }: { signal: SimStation['signals'][number]; disabled: boolean; onSet: (value: string) => void }) {
  const t = useT()
  const [draft, setDraft] = useState(signal.value ?? '')
  return (
    <tr>
      <td><strong className="mono">{signal.signal}</strong>{!signal.readByIntake && <div className="muted small">{t('linked.simNotRead')}</div>}</td>
      <td className="mono">{signal.testOnly ? <Pill tone="warn" dot={false}>{t('linked.simTestOnly')}</Pill> : signal.address ?? <span className="muted">NEEDS_CONFIGURATION</span>}</td>
      <td className="muted small">{signalDescription(t, signal.signal, signal.description)}</td>
      <td className="mono">{signal.dataType === 'Bool' ? (signal.value === 'true' ? 'ON' : signal.value === 'false' ? 'OFF' : '—') : signal.value ?? '—'}</td>
      <td className="nowrap">
        {signal.dataType === 'Bool'
          ? <><Button size="sm" disabled={disabled} onClick={() => onSet('true')}>ON</Button> <Button size="sm" disabled={disabled} onClick={() => onSet('false')}>OFF</Button></>
          : <><input value={draft} disabled={disabled} aria-label={signal.signal} onChange={(e) => setDraft(e.target.value)} /> <Button size="sm" disabled={disabled || draft.trim() === ''} onClick={() => onSet(draft)}>{t('linked.simSet')}</Button></>}
      </td>
    </tr>
  )
}
