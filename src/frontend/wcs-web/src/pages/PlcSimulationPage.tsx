import { useCallback, useState } from 'react'
import { api } from '../api/client'
import type { SimFlow, SimStartResult } from '../api/simTypes'
import { Modal } from '../components/Modal'
import { Async, Button, EmptyState, PageHeader } from '../components/ui'
import { DEMO_MODE } from '../config'
import { useApi } from '../hooks/useApi'
import { useT } from '../i18n'
import { describeError } from '../i18n/vi'
import { PlcSimulationSignals as SignalPanel } from './PlcSimulationSignals'
import { AmrSimulatorPanel } from './AmrSimulatorPanel'

/**
 * CR-035 / CR-040 – Admin manual PLC simulation, opened by an explicit confirmation. Only PLC → WCS signals are simulated: requests go through the same WCS intake a PLC would use.
 * The AMR simulation panel (CR-050) is separate; where an order is sent is decided by the operating mode. Every state shown here is read from the backend; pressing a button never changes a state by
 * itself (the server decides and may refuse). Hiding this page for other roles is a convenience: the server enforces Admin on every call.
 */
export function PlcSimulationPage() {
  const t = useT()
  if (DEMO_MODE) {
    return (
      <>
        <PageHeader title={t('linked.psTitle')} />
        <EmptyState title={t('linked.psDemoTitle')} hint={t('linked.psDemoHint')} />
      </>
    )
  }
  return <Screen />
}

function Screen() {
  const t = useT()
  const status = useApi(useCallback(() => api.simStatus(), []))
  const flows = useApi(useCallback(() => api.simFlows(), []))
  const orders = useApi(useCallback(() => api.routingOrders(undefined, 20), []), ['OrderChanged'])
  const [msg, setMsg] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [flowCode, setFlowCode] = useState('')
  const [location, setLocation] = useState('')
  const [ack, setAck] = useState(false)
  const [last, setLast] = useState<SimStartResult | null>(null)
  const [confirming, setConfirming] = useState(false)

  const run = async (fn: () => Promise<unknown>, ok: string) => {
    setBusy(true); setMsg(null)
    try { await fn(); setMsg(ok) } catch (e) { setMsg(describeError(e)) } finally { setBusy(false); status.reload(); flows.reload(); orders.reload() }
  }

  return (
    <>
      <PageHeader title={t('linked.psTitle')} description={t('linked.psDesc')} />
      <div className="stack">
        <Async state={status} empty={{ title: t('linked.psNoData') }}>
          {(s) => (
            <div className="card pad stack">
              <strong className={s.active ? 'text-bad' : undefined}>{s.banner} – {t(s.active ? 'linked.psOn' : 'linked.psOff')}</strong>
              <span className="muted">{s.mappingSource}</span>
              <strong>{t(s.executionMode === 'Real' ? 'linked.psModeReal' : 'linked.psModeSim')}</strong>
              <ul><li>{t(s.executionMode === 'Real' ? 'linked.psNoteReal' : 'linked.psNoteSim')}</li><li>{t('linked.psNoteScope')}</li></ul>
              <div>
                {!s.active
                  ? <Button variant="primary" disabled={busy} onClick={() => setConfirming(true)}>{t('linked.psActivate')}</Button>
                  : <Button disabled={busy} onClick={() => run(() => api.simDeactivate(), t('linked.psDeactivated'))}>{t('linked.psDeactivate')}</Button>}
              </div>
              {confirming && (
                <Modal title={t('linked.psConfirmTitle')} onClose={() => setConfirming(false)} footer={<>
                  <Button onClick={() => setConfirming(false)}>{t('linked.cancel')}</Button>
                  <Button variant="danger" disabled={busy} onClick={() => { setConfirming(false); void run(() => api.simActivate(true), t('linked.psActivated')) }}>{t('linked.psConfirmYes')}</Button>
                </>}>
                  <p>{t('linked.psConfirmBody')}</p>
                  <div className="notice notice-bad" role="note">{t(s.executionMode === 'Real' ? 'linked.psNoteReal' : 'linked.psNoteSim')}</div>
                </Modal>
              )}
            </div>
          )}
        </Async>

        {msg && <div className="card pad" role="status">{msg}</div>}

        <Async state={flows} empty={{ title: t('linked.psNoFlow') }} isEmpty={(f) => f.length === 0}>
          {(list: SimFlow[]) => {
            const sel = list.find((f) => f.code === flowCode)
            return (
              <div className="card pad stack">
                <strong>{t('linked.psStep1')}</strong>
                <select value={flowCode} aria-label={t('linked.psStep1')} onChange={(e) => { setFlowCode(e.target.value); setLocation('') }}>
                  <option value="">{t('linked.psChooseFlow')}</option>
                  {list.map((f) => <option key={f.code} value={f.code}>{f.code} – {f.name}{f.startableNow ? '' : ` (${t('linked.psNotRunnable')})`}</option>)}
                </select>
                {sel && (
                  <>
                    <div className="muted">
                      {t('linked.psFlowInfo', { type: sel.requestType, rack: sel.rackKind, state: `${sel.publicationState}${sel.publishedVersion ? ` v${sel.publishedVersion}` : ''}`, readiness: sel.readiness ?? '—' })}
                      {sel.notStartableReason ? ` · ${t('linked.psBlockedWhy', { why: sel.notStartableReason })}` : ''}
                    </div>
                    <select value={location} aria-label={t('linked.psChooseLocation')} onChange={(e) => setLocation(e.target.value)}>
                      <option value="">{t('linked.psChooseLocation')}</option>
                      {sel.triggers.map((x) => (
                        <option key={x.locationCode} value={x.locationCode}>{x.locationCode} · {x.plcId ?? t('linked.psNoPlc')} · {t(x.portBusy ? 'linked.psBusy' : 'linked.psFree')}{x.blocked ? ` · ${t('linked.psOnlineBlocked')}` : ''}</option>
                      ))}
                    </select>
                    <label><input type="checkbox" checked={ack} onChange={(e) => setAck(e.target.checked)} /> {t('linked.psAck')}</label>
                    <div>
                      <Button variant="primary" disabled={busy || !ack || !location || !sel.startableNow || !status.data?.active || status.data.executionMode === 'Real'}
                        onClick={() => run(async () => setLast(await api.simStart(sel.code, location, ack)), t('linked.psSent'))}>{t('linked.psCreate')}</Button>
                    </div>
                  </>
                )}
                {last && <div className="muted">{t('linked.psResult', { outcome: last.outcome, code: last.code, message: last.message, id: last.requestId })}</div>}
              </div>
            )
          }}
        </Async>

        <AmrSimulatorPanel />

        <SignalPanel active={!!status.data?.active} real={status.data?.executionMode === 'Real'} onResult={(r) => { setLast(r); orders.reload() }} />

        <Async state={orders} empty={{ title: t('linked.psNoJob') }} isEmpty={(o) => o.items.length === 0}>
          {(o) => (
            <div className="card pad stack">
              <strong>{t('linked.psStep3')}</strong>
              {o.items.map((order) => (
                <div key={order.id} className="stack">
                  <div><strong>{order.processCode} v{order.processVersion}</strong> · {order.state}{order.stateReason ? ` (${order.stateReason})` : ''} · {order.requestKey}</div>
                  {order.legs.map((l) => (
                    <div key={l.id} className="muted">
                      {t('linked.psLeg', { n: l.sequence, from: l.sourceLocationCode ?? '—', to: l.destinationLocationCode ?? '—', state: l.state, task: l.agvTaskCode ?? t('linked.psNoTask'), amr: l.robotCode ?? '—' })}
                      {' '}
                      {l.state !== 'Completed' && l.state !== 'Failed' && l.state !== 'Cancelled' && l.agvTaskCode && !l.pickConfirmedAt &&
                        <Button size="sm" disabled={busy} onClick={() => run(() => api.simConfirm(l.id, false), t('linked.psPicked'))}>{t('linked.psPick')}</Button>}
                      {l.pickConfirmedAt && !l.dropConfirmedAt && l.state !== 'Failed' && l.state !== 'Cancelled' &&
                        <Button size="sm" disabled={busy} onClick={() => run(() => api.simConfirm(l.id, true), t('linked.psDropped'))}>{t('linked.psDrop')}</Button>}
                    </div>
                  ))}
                </div>
              ))}
            </div>
          )}
        </Async>
      </div>
    </>
  )
}
