import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import type { ExecMode, ExecutionModeView, LegacyAdoptionResult, ModeReadiness, ModeTransitionBody, ModeTransitionResult, ModeTransitionView } from '../../api/modeTypes'
import { Button, Pill } from '../../components/ui'
import { useT, type MsgKey } from '../../i18n'
import { formatDateTime } from '../../i18n/format'
import { describeError } from '../../i18n/vi'
import { canConfirmSwitch, modeState, modeTag, newRequestId, otherMode, applyTransition, sendTransition, type TransitionState } from '../../lib/modeConsole'

/** The server calls the panel makes (the page passes the real client, the tests a fake). The panel never chooses a dispatcher: it asks, and the server decides. */
export interface ModePanelApi {
  readiness: (target: ExecMode) => Promise<ModeReadiness>
  transition: (body: ModeTransitionBody) => Promise<ModeTransitionResult>
  acknowledge: (requestId: string, reason: string) => Promise<ModeTransitionResult>
  history: () => Promise<ModeTransitionView[]>
  adoptLegacy: (reason: string) => Promise<LegacyAdoptionResult>
}

interface Props { view: ExecutionModeView | null; loadFailed: boolean; isAdmin: boolean; api: ModePanelApi; onChanged: () => void }

export function ModeBadge({ mode }: { mode: ExecMode }) {
  const t = useT()
  return <Pill tone={mode === 'Real' ? 'bad' : 'info'}>{t(mode === 'Real' ? 'ops.modeTagReal' : 'ops.modeTagSimulated')}</Pill>
}

const modeName = (t: ReturnType<typeof useT>, m: ExecMode) => t(m === 'Real' ? 'ops.modeReal' : 'ops.modeSimulator')

function Conditions({ r }: { r: ModeReadiness }) {
  const t = useT()
  return (
    <>
      <p className={r.canSwitch ? 'ops-ok' : 'ops-bad'} data-can-switch={r.canSwitch ? 'yes' : 'no'}>{t(r.canSwitch ? 'ops.modeCanSwitch' : 'ops.modeCannotSwitch')}</p>
      <ul className="ops-conditions">
        {r.conditions.map((c) => (
          <li key={c.code} data-condition={c.code} data-ok={c.ok ? 'yes' : 'no'}>
            <Pill tone={c.ok ? 'ok' : 'bad'}>{t(c.ok ? 'ops.condOk' : 'ops.condNo')}</Pill> <strong>{t(`ops.cond_${c.code}` as MsgKey)}</strong>
            {!c.ok && <div className="muted small">{c.message}</div>}
          </li>
        ))}
      </ul>
      {r.blockingWork.length > 0 && (
        <>
          <p><strong>{t('ops.modeBlockingTitle')}</strong></p>
          <ul>
            {r.blockingWork.map((w) => (
              <li key={`${w.kind}-${w.id}`}>{w.kind === 'Task' ? <Link to={`/operations?task=${w.id}`}>{w.code}</Link> : w.code} · {w.state} · <span className="muted">{w.reason}</span></li>
            ))}
          </ul>
        </>
      )}
    </>
  )
}

/**
 * CR-038: the operating mode (Simulator / Real AGV). Everything shown here comes from the server: the mode, whether Real is allowed by the deployment, the checks of a switch and the
 * work that blocks it. A switch needs a reason, an explicit confirmation for Real AGV and a RequestId; it is sent once, a lost answer is asked again with the SAME RequestId (the server
 * never switches twice), and nothing is cancelled to make a switch possible.
 */
export function ModePanel({ view, loadFailed, isAdmin, api, onChanged }: Props) {
  const t = useT()
  const [open, setOpen] = useState(false)
  const [readiness, setReadiness] = useState<ModeReadiness | null>(null)
  const [reason, setReason] = useState('')
  const [ack, setAck] = useState(false)
  const [tState, setTState] = useState<TransitionState>(() => ({ phase: 'idle', requestId: newRequestId() }))
  const ref = useRef(tState)
  const [result, setResult] = useState<ModeTransitionResult | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [history, setHistory] = useState<ModeTransitionView[]>([])
  const [recoveryNote, setRecoveryNote] = useState('')
  const [recoveryResult, setRecoveryResult] = useState<ModeTransitionResult | null>(null)
  const [adopted, setAdopted] = useState<LegacyAdoptionResult | null>(null)
  const holder = { get: () => ref.current, set: (s: TransitionState) => { ref.current = s; setTState(s) } }

  useEffect(() => {
    if (!isAdmin) return
    let off = false
    api.history().then((h) => { if (!off) setHistory(h) }).catch(() => { /* history is a convenience */ })
    return () => { off = true }
  }, [api, isAdmin, view?.mode, view?.updatedAt])

  if (!view) return <section className="panel ops-mode" aria-label={t('ops.modeTitle')}><h2>{t('ops.modeTitle')}</h2><div className={loadFailed ? 'notice notice-bad' : 'muted'}>{t('ops.modeLoadFailed')}</div></section>

  const target = otherMode(view.mode)
  const state = modeState(view)
  const realBlockedByDeployment = target === 'Real' && !view.realDispatchConfigured
  const phase = tState.phase

  const openSwitch = async () => {
    setOpen(true); setResult(null); setError(null); setReadiness(null)
    applyTransition(holder, 'reset')
    try { setReadiness(await api.readiness(target)) } catch (e) { setError(describeError(e)) }
  }
  const close = () => { setOpen(false); setReason(''); setAck(false); setResult(null); setError(null); applyTransition(holder, 'reset') }
  const confirm = async (event: 'send' | 'retry') => {
    const r = await sendTransition(holder, api, target, reason, ack, event)
    if (r.result) { setResult(r.result); setError(null); if (r.result.succeeded) onChanged() }
    else if (r.error) setError(describeError(r.error))
  }
  const adoptLegacy = async () => {
    try { setAdopted(await api.adoptLegacy(reason.trim())); setReadiness(await api.readiness(target)); setError(null) } catch (e) { setError(describeError(e)) }
  }
  const acknowledge = async () => {
    try { const r = await api.acknowledge(`ACK-${newRequestId()}`.slice(0, 64), recoveryNote.trim()); setRecoveryResult(r); if (r.succeeded) onChanged() } catch (e) { setError(describeError(e)) }
  }

  return (
    <section className="panel ops-mode" aria-label={t('ops.modeTitle')} data-mode={view.mode}>
      <h2>{t('ops.modeTitle')}</h2>
      <div className="ops-mode-now">
        <span className="muted">{t('ops.modeCurrent')}:</span> <ModeBadge mode={view.mode} /> <strong>{modeName(t, view.mode)}</strong>
        <span className="muted small"> · {t('ops.modeSince', { at: formatDateTime(view.updatedAt), by: view.updatedBy })}</span>
      </div>
      <p className={view.mode === 'Real' ? 'notice notice-warn' : 'muted'}>{t(view.mode === 'Real' ? 'ops.modeRealNote' : 'ops.modeSimulatorNote')}</p>
      {state === 'notChosen' && <div className="notice notice-warn" role="note">{t('ops.modeNotChosen')}</div>}
      {state === 'recovery' && (
        <div className="notice notice-bad" role="alert" data-recovery="yes">
          <strong>{t('ops.modeRecoveryTitle')}</strong>
          <p>{t('ops.modeRecoveryBody', { reason: view.recoveryReason ?? '' })}</p>
          {isAdmin && (
            <>
              <label>{t('ops.modeRecoveryNote')} <input type="text" value={recoveryNote} maxLength={400} onChange={(e) => setRecoveryNote(e.target.value)} /></label>
              <div className="btn-row"><Button disabled={!recoveryNote.trim()} onClick={() => void acknowledge()}>{t('ops.modeRecoveryAck')}</Button></div>
              {recoveryResult && !recoveryResult.succeeded && (
                <div><p>{t('ops.modeRecoveryStill')}</p><ul>{recoveryResult.blockingWork.map((w) => <li key={w.id}><Link to={`/operations?task=${w.id}`}>{w.code}</Link> · {w.state}</li>)}</ul></div>
              )}
            </>
          )}
        </div>
      )}

      {!isAdmin ? <p className="muted small">{t('ops.modeAdminOnly')}</p> : (
        <>
          {realBlockedByDeployment && <div className="notice notice-warn" role="note">{t('ops.modeRealNotAllowed')}</div>}
          {!open && <div className="btn-row"><Button disabled={realBlockedByDeployment || state === 'recovery'} onClick={() => void openSwitch()}>{t('ops.modeSwitchTo', { mode: modeName(t, target) })}</Button></div>}
          {open && (
            <div className="ops-switch" data-target={target}>
              <h3>{t('ops.modeReadinessTitle', { mode: modeName(t, target) })}</h3>
              {readiness ? <Conditions r={readiness} /> : !error && <p className="muted">{t('ops.modeSending')}</p>}
              {readiness?.blockingWork.some((w) => w.reason.startsWith('LEGACY')) && (
                <div className="notice notice-warn" role="note" data-legacy="yes">
                  <p>{t('ops.modeLegacyNote')}</p>
                  <div className="btn-row"><Button disabled={!reason.trim() || phase === 'sending'} onClick={() => void adoptLegacy()}>{t('ops.modeAdoptLegacy')}</Button></div>
                  {adopted && <p>{t('ops.modeAdoptDone', { tasks: adopted.adoptedTasks, orders: adopted.adoptedOrders })}{adopted.notAdopted.length > 0 ? ' ' + t('ops.modeAdoptNot', { n: adopted.notAdopted.length }) : ''}</p>}
                </div>
              )}
              {phase !== 'done' && phase !== 'refused' && (
                <>
                  <label className="field"><span className="field-label">{t('ops.modeReason')}</span><input type="text" value={reason} maxLength={500} disabled={phase === 'sending' || phase === 'unknown'} onChange={(e) => setReason(e.target.value)} /></label>
                  {target === 'Real' && <label><input type="checkbox" checked={ack} disabled={phase === 'sending' || phase === 'unknown'} onChange={(e) => setAck(e.target.checked)} /> {t('ops.modeRealAck')}</label>}
                  <div className="btn-row">
                    {phase === 'unknown'
                      ? <Button variant="danger" onClick={() => void confirm('retry')}>{t('ops.modeRetry')}</Button>
                      : <Button variant={target === 'Real' ? 'danger' : 'primary'} disabled={phase === 'sending' || !canConfirmSwitch(readiness, reason, ack)} onClick={() => { applyTransition(holder, 'ask'); void confirm('send') }}>{phase === 'sending' ? t('ops.modeSending') : t('ops.modeConfirm')}</Button>}
                    <Button disabled={phase === 'sending'} onClick={close}>{t('ops.modeCancel')}</Button>
                  </div>
                </>
              )}
              {phase === 'unknown' && <div className="notice notice-warn" role="alert" data-transition="unknown">{t('ops.modeUnknown', { id: tState.requestId })}</div>}
              {result?.succeeded && <div className="notice notice-ok" role="status" data-transition="done">{t('ops.modeDone', { mode: modeName(t, result.to) })} <Button onClick={close}>{t('common.close')}</Button></div>}
              {result && !result.succeeded && (
                <div className="notice notice-bad" role="alert" data-transition="refused">
                  <strong>{t('ops.modeRefused')}</strong>
                  <ul>{result.failed.map((c) => <li key={c.code}>{t(`ops.cond_${c.code}` as MsgKey)}{c.message ? <span className="muted small"> · {c.message}</span> : null}</li>)}</ul>
                  <Button onClick={close}>{t('ops.modeNew')}</Button>
                </div>
              )}
              {error && <div className="notice notice-bad" role="alert">{error}</div>}
            </div>
          )}
          <h3>{t('ops.modeHistoryTitle')}</h3>
          {history.length === 0 ? <p className="muted small">{t('ops.modeHistoryNone')}</p> : (
            <ul className="ops-mode-history">
              {history.slice(0, 8).map((h) => (
                <li key={h.id}><span className="num muted">{formatDateTime(h.at)}</span> <Pill tone={h.succeeded ? 'ok' : 'warn'}>{t(h.succeeded ? 'ops.modeHistoryOk' : 'ops.modeHistoryNo')}</Pill> {modeTag(h.from)} → {modeTag(h.to)} · {h.actor} · <span className="muted">{h.reason}</span></li>
              ))}
            </ul>
          )}
        </>
      )}
    </section>
  )
}
