import { useCallback, useEffect, useState } from 'react'
import { api } from '../api/client'
import type { AmrSimulation } from '../api/simTypes'
import { Async, Button, Pill } from '../components/ui'
import { useApi } from '../hooks/useApi'
import { useT, type MsgKey } from '../i18n'
import { describeError } from '../i18n/vi'
import { AMR_FAULTS, canRecover } from '../lib/amrSim'

const SPEEDS = [1, 2, 5, 10, 25]

/** CR-050 – the AMRs behind the simulated RCS (Admin). Simulation only: it drives the simulator; every business state shown elsewhere is changed by WCS from the events the simulator reports. */
export function AmrSimulatorPanel() {
  const t = useT()
  const state = useApi(useCallback(() => api.amrStatus(), []))
  const [message, setMessage] = useState<string | null>(null)
  const [speed, setSpeed] = useState(5)
  const { reload } = state
  useEffect(() => { const id = setInterval(reload, 1000); return () => clearInterval(id) }, [reload])
  const call = async (fn: () => Promise<unknown>) => { setMessage(null); try { await fn() } catch (e) { setMessage(describeError(e)) } finally { reload() } }

  return (
    <div className="card pad stack">
      <strong>{t('linked.amrTitle')}</strong>
      <span className="muted">{t('linked.amrIntro')}</span>
      {message && <div className="notice notice-bad" role="alert">{message}</div>}
      <Async state={state} empty={{ title: t('linked.amrUnavailable') }}>
        {(a: AmrSimulation) => (
          <div className="stack">
            <div className="btn-row">
              <Pill tone={a.enabled ? 'ok' : 'muted'} dot={false}>{t(a.enabled ? 'linked.amrOn' : 'linked.amrOff')}</Pill>
              <label>{t('linked.amrSpeed')} <select value={a.enabled ? a.speedMultiplier : speed} onChange={(e) => { const v = Number(e.target.value); setSpeed(v); if (a.enabled) void call(() => api.amrConfig(true, v)) }}>
                {SPEEDS.map((s) => <option key={s} value={s}>×{s}</option>)}
              </select></label>
              <Button variant={a.enabled ? 'secondary' : 'primary'} onClick={() => void call(() => api.amrConfig(!a.enabled, a.enabled ? a.speedMultiplier : speed))}>{t(a.enabled ? 'linked.amrSwitchOff' : 'linked.amrSwitchOn')}</Button>
              {a.enabled && <Button variant={a.connected ? 'secondary' : 'primary'} onClick={() => void call(() => api.amrConnection(!a.connected))}>{t(a.connected ? 'linked.amrDisconnect' : 'linked.amrReconnect')}</Button>}
              {a.enabled && !a.connected && <Pill tone="bad" dot={false}>{t('linked.amrDisconnected')}</Pill>}
            </div>
            {a.enabled && (
              <>
                <div className="muted">{t('linked.amrFaultHint')}</div>
                <div className="btn-row">
                  {AMR_FAULTS.map((f) => <Button key={f} size="sm" onClick={() => void call(() => api.amrFault(f, f === 'ResponseDelay' ? 10 : undefined))}>{t(`linked.amrFault_${f}` as MsgKey)}{a.armedFaults[f] ? ` (${a.armedFaults[f]})` : ''}</Button>)}
                  <Button size="sm" variant="danger" onClick={() => void call(() => api.amrClearFaults())}>{t('linked.amrClearFaults')}</Button>
                </div>
              </>
            )}
            <div className="table-wrap">
              <table className="grid" aria-label={t('linked.amrRobots')}>
                <thead><tr><th>{t('linked.amrRobot')}</th><th>{t('linked.amrState')}</th><th>X, Y</th><th>{t('linked.amrBattery')}</th><th>{t('linked.amrRequest')}</th></tr></thead>
                <tbody>
                  {a.robots.map((r) => <tr key={r.robotCode}><td className="mono">{r.robotCode}</td><td>{r.state}</td><td className="num">{r.x}, {r.y}</td><td className="num">{r.battery}%</td><td className="mono">{r.requestCode ?? '—'}</td></tr>)}
                  {a.robots.length === 0 && <tr><td colSpan={5} className="muted">{t('linked.amrNoRobot')}</td></tr>}
                </tbody>
              </table>
            </div>
            <div className="table-wrap">
              <table className="grid" aria-label={t('linked.amrRuns')}>
                <thead><tr><th>{t('linked.amrParent')}</th><th>{t('linked.amrRequest')}</th><th>{t('linked.amrRobot')}</th><th>{t('linked.amrRoute')}</th><th>{t('linked.amrPhase')}</th><th>{t('linked.amrEvents')}</th><th /></tr></thead>
                <tbody>
                  {a.runs.map((r) => (
                    <tr key={r.requestCode}>
                      <td className="mono">{r.parentTask || '—'}</td><td className="mono">{r.requestCode}</td><td className="mono">{r.robotCode ?? '—'}</td>
                      <td>{r.source ?? '—'} → {r.destination ?? '?'}</td>
                      <td><Pill tone={r.phase === 'Completed' ? 'ok' : r.phase === 'Failed' || r.stalled ? 'bad' : 'muted'} dot={false}>{r.phase}{r.failure ? ` · ${r.failure}` : ''}{r.stalled ? ` · ${t('linked.amrStalled')}` : ''}</Pill></td>
                      <td><details><summary>{r.events.at(-1)?.slice(13) ?? '—'}</summary><ol className="small">{r.events.map((e, i) => <li key={i} className="mono">{e}</li>)}</ol></details></td>
                      <td>{canRecover(r) && <Button size="sm" onClick={() => void call(() => api.amrRecover(r.requestCode))}>{t('linked.amrRecover')}</Button>}</td>
                    </tr>
                  ))}
                  {a.runs.length === 0 && <tr><td colSpan={7} className="muted">{t('linked.amrNoRun')}</td></tr>}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </Async>
    </div>
  )
}
