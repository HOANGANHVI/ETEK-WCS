import { useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import type { AmrLive, AmrLiveRun } from '../api/simTypes'
import { useT, type MsgKey } from '../i18n'
import { formatDateTime } from '../i18n/format'
import { describeError } from '../i18n/vi'
import { isOpenRun } from '../lib/simMap'
import { Button, Pill } from './ui'

const SPEEDS = [1, 2, 5, 10]

/** CR-069: control of the AMR simulation (Admin) and the work it is executing. Cancelling a task is the existing order action; the server decides whether it is allowed. */
export function SimPanel({ live, isAdmin, selected, onSelect, onChanged }: { live: AmrLive; isAdmin: boolean; selected: string | null; onSelect: (run: AmrLiveRun) => void; onChanged: () => void }) {
  const t = useT()
  const [speed, setSpeed] = useState(1)
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)
  const run = async (fn: () => Promise<unknown>) => { setError(null); try { await fn() } catch (e) { setError(describeError(e)) } finally { onChanged() } }
  const open = live.runs.filter(isOpenRun)
  const chosen = live.runs.find((r) => r.requestCode === selected) ?? null
  const current = (r: AmrLiveRun) => {
    const robot = live.robots.find((x) => x.robotCode === r.robotCode)
    return robot ? `${robot.x}, ${robot.y} mm` : '–'
  }
  return (
    <section className="sim-panel" aria-label={t('agv.simTitle')}>
      <div className="sim-bar">
        <strong>{t('agv.simTitle')}</strong>
        <Pill tone={!live.active ? 'muted' : live.paused ? 'warn' : 'ok'}>{t(!live.active ? 'agv.simOff' : live.paused ? 'agv.simPaused' : 'agv.simRunning')}</Pill>
        {isAdmin && (
          <>
            <label>{t('agv.simSpeed')}{' '}
              <select value={live.active ? live.speedMultiplier : speed} onChange={(e) => { const v = Number(e.target.value); setSpeed(v); if (live.active) void run(() => api.amrConfig(true, v)) }}>
                {SPEEDS.map((s) => <option key={s} value={s}>{s}x</option>)}
              </select>
            </label>
            {!live.active && <Button size="sm" variant="primary" onClick={() => void run(() => api.amrConfig(true, speed))}>{t('agv.simStart')}</Button>}
            {live.active && <Button size="sm" variant="primary" onClick={() => void run(() => api.amrPause(!live.paused))}>{t(live.paused ? 'agv.simResume' : 'agv.simPause')}</Button>}
            {live.active && <Button size="sm" variant="danger" onClick={() => { if (open.length === 0 || window.confirm(t('agv.simStopConfirm', { n: open.length }))) void run(() => api.amrConfig(false, live.speedMultiplier)) }}>{t('agv.simStop')}</Button>}
            <Link to="/tasks">{t('agv.simNewTask')}</Link>
          </>
        )}
      </div>
      {error && <div className="notice notice-bad" role="alert">{error}</div>}
      {live.active && !live.mapId && <div className="notice notice-warn">{t('agv.simNoMap')}</div>}
      {live.active && live.runs.length === 0 && <p className="muted small">{t('agv.simNoRun')}</p>}
      {live.runs.length > 0 && (
        <div className="sim-runs">
          {live.runs.slice(0, 8).map((r) => (
            <button type="button" key={r.requestCode} data-run={r.requestCode} className={`sim-run${r.requestCode === selected ? ' selected' : ''}`} onClick={() => onSelect(r)}>
              <span className="mono">{r.order || r.requestCode}</span>
              <span>{r.pick ?? '–'} → {r.drop ?? '?'}</span>
              <span className="mono">{r.robotCode ?? '–'}</span>
              <Pill tone={r.phase === 'Completed' ? 'ok' : r.phase === 'Failed' || r.stalled ? 'bad' : r.phase === 'Cancelled' ? 'muted' : 'info'}>{t(`agv.simAction_${r.action}` as MsgKey)}</Pill>
              <span className="sim-progress" role="progressbar" aria-valuenow={Math.round(r.progress)} aria-valuemin={0} aria-valuemax={100}><i style={{ width: `${r.progress}%` }} /></span>
              <span className="num">{Math.round(r.progress)}%</span>
            </button>
          ))}
        </div>
      )}
      {chosen && (
        <div className="sim-detail" data-run-detail={chosen.requestCode}>
          <dl className="kv2">
            <dt>{t('agv.simOrder')}</dt><dd className="mono">{chosen.orderId ? <Link to={`/routing?order=${chosen.orderId}`}>{chosen.order}</Link> : chosen.order}{chosen.processCode ? ` · ${chosen.processCode}` : ''}</dd>
            <dt>{t('agv.simOrderId')}</dt><dd className="mono">{chosen.orderId ?? chosen.requestCode}</dd>
            <dt>{t('agv.simAmr')}</dt><dd className="mono">{chosen.robotCode ?? '–'}</dd>
            <dt>{t('agv.simRack')}</dt><dd className="mono">{chosen.rackId ?? '–'}{chosen.rackKind ? ` · ${t(chosen.rackKind === 'Full' ? 'agv.simFull' : 'agv.simEmpty')}` : ''}{chosen.carrying ? ` · ${t('agv.simOnAmr')}` : ''}</dd>
            <dt>{t('agv.simPick')}</dt><dd>{chosen.pick ?? '–'}</dd>
            <dt>{t('agv.simDrop')}</dt><dd>{chosen.drop ?? '–'}</dd>
            <dt>{t('agv.simPosition')}</dt><dd className="mono">{current(chosen)}</dd>
            <dt>{t('agv.simStep')}</dt><dd>{chosen.legSequence !== null ? t('agv.simLeg', { n: chosen.legSequence, step: chosen.stepKey ?? '–' }) : '–'}</dd>
            <dt>{t('agv.simProgress')}</dt><dd className="num">{Math.round(chosen.progress)}%</dd>
            <dt>{t('agv.simCurrent')}</dt><dd>{t(`agv.simAction_${chosen.action}` as MsgKey)}{chosen.stalled ? ` · ${t('agv.simStalled')}` : ''}</dd>
            <dt>{t('agv.simCreated')}</dt><dd>{chosen.createdAt ? formatDateTime(chosen.createdAt) : '–'}</dd>
            <dt>{t('agv.simPickedAt')}</dt><dd>{chosen.pickedAt ? formatDateTime(chosen.pickedAt) : '–'}</dd>
            <dt>{t('agv.simDroppedAt')}</dt><dd>{chosen.droppedAt ? formatDateTime(chosen.droppedAt) : '–'}</dd>
          </dl>
          {chosen.failure && <div className="notice notice-bad" role="alert">{chosen.failure}</div>}
          {isAdmin && chosen.phase === 'Failed' && <Button size="sm" onClick={() => void run(() => api.amrRecover(chosen.requestCode))}>{t('agv.simRecover')}</Button>}
          {isAdmin && chosen.orderId && isOpenRun(chosen) && (
            <div className="btn-row">
              <input value={reason} maxLength={500} placeholder={t('agv.simCancelReason')} aria-label={t('agv.simCancelReason')} onChange={(e) => setReason(e.target.value)} />
              <Button size="sm" variant="danger" disabled={!reason.trim()} onClick={() => void run(() => api.routingCancel(chosen.orderId as string, reason.trim()))}>{t('agv.simCancel')}</Button>
            </div>
          )}
        </div>
      )}
    </section>
  )
}
