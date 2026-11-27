import { useCallback, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { CreateTaskDialog } from './TaskDialogs'
import { RequestOverview } from '../components/RequestOverview'
import { api } from '../api/client'
import type { Leg, OrderState, StationTransaction, WaitKind } from '../api/routingTypes'
import { ORDER_STATES } from '../api/routingTypes'
import { hasRole, useAuth } from '../auth/AuthContext'
import { orderInMotion, orderIsOpen } from '../lib/rackBlock'
import { waitCause } from '../lib/waitReason'
import { Modal } from '../components/Modal'
import { Async, Button, PageHeader, Panel, Pill } from '../components/ui'
import { useApi } from '../hooks/useApi'
import { useT, type MsgKey } from '../i18n'
import { formatDateTime, formatTime } from '../i18n/format'
import { LEG_STATE_LABEL, LEG_STATE_TONE, ORDER_STATE_LABEL, ORDER_STATE_TONE, RACK_KIND_LABEL, STEP_TYPE_LABEL, TX_RESUME_STATES, TX_STATE_LABEL, TX_STATE_TONE } from '../i18n/routing'
import { describeError } from '../i18n/vi'

const EVENTS = ['OrderChanged', 'InventoryChanged', 'AgvStatusChanged'] as const

const WAIT_KEY: Record<WaitKind, MsgKey> = {
  None: 'routing.waitNone',
  DestinationUnavailable_AgvHoldingRack: 'routing.waitHolding',
  AtBuffer: 'routing.waitAtBuffer',
  SourceUnavailable: 'routing.waitSource',
  AgvBusy: 'routing.waitAgvBusy',
  ConditionNotMet: 'routing.waitCondition',
  DispatchBlocked: 'routing.waitDispatch',
  DestinationUnavailable: 'routing.waitDestination',
  QueueScopeUndetermined: 'linked.waitScope',
  QueueBehind: 'linked.waitBehind',
  QueueHeld: 'linked.waitHeld',
}

export const OrderStatePill = ({ state }: { state: string }) => <Pill tone={ORDER_STATE_TONE[state] ?? 'muted'}>{ORDER_STATE_LABEL[state] ?? state}</Pill>
export const TxStatePill = ({ state }: { state: string }) => <Pill tone={TX_STATE_TONE[state] ?? 'muted'}>{TX_STATE_LABEL[state] ?? state}</Pill>

/**
 * CR-010 monitoring. A finished Station Transaction, a Transport Task that is still InProgress, a task waiting at a buffer and a task waiting while the AMR
 * still holds the rack are four different things and are shown as such. Operators only look; physical-event inputs are Admin-only (the API enforces it).
 */
export function RoutingPage() {
  const t = useT()
  const [tab, setTab] = useState<'tasks' | 'tx'>('tasks')
  const [starting, setStarting] = useState(false)
  const [created, setCreated] = useState(0)
  const navigate = useNavigate()
  return (
    <>
      <PageHeader title={t('routing.title')} description={t('linked.roleRouting')} actions={<Button variant="primary" onClick={() => setStarting(true)}>{t('routing.startBtn')}</Button>} />
      <div className="tabs" role="tablist">
        <button role="tab" aria-selected={tab === 'tasks'} className={tab === 'tasks' ? 'tab active' : 'tab'} onClick={() => setTab('tasks')}>{t('routing.tabTasks')}</button>
        <button role="tab" aria-selected={tab === 'tx'} className={tab === 'tx' ? 'tab active' : 'tab'} onClick={() => setTab('tx')}>{t('routing.tabTransactions')}</button>
      </div>
      {tab === 'tasks' ? <><OrdersTab key={created} /><RequestOverview key={`all-${created}`} /></> : <TransactionsTab />}
      {starting && <CreateTaskDialog onClose={() => setStarting(false)} onCreated={(x) => { setStarting(false); void navigate(`/tasks?id=${x.id}`) }} onOrderCreated={() => { setStarting(false); setCreated((n) => n + 1) }} />}
    </>
  )
}

// ====================================================================================================================== Transport Tasks
function OrdersTab() {
  const t = useT()
  const [state, setState] = useState<OrderState | ''>('')
  const [params, setParams] = useSearchParams()   // CR-040: /routing?order=<id> opens that request (links from the other pages)
  const selected = params.get('order')
  const setSelected = (id: string) => setParams({ order: id })
  const list = useApi(useCallback(() => api.routingOrders(state || undefined), [state]), [...EVENTS])
  return (
    <div className="split">
      <Panel flush actions={
        <label className="inline-filter">{t('routing.filterState')}{' '}
          <select value={state} onChange={(e) => setState(e.target.value as OrderState | '')}>
            <option value="">{t('routing.filterAll')}</option>
            {ORDER_STATES.map((s) => <option key={s} value={s}>{ORDER_STATE_LABEL[s]}</option>)}
          </select>
        </label>}>
        <Async state={list} isEmpty={(d) => d.items.length === 0} empty={{ title: t('routing.none'), hint: t('routing.noneHint') }}>
          {(page) => (
            <div className="table-wrap">
              <table className="grid clickable">
                <thead><tr><th>{t('routing.colProcess')}</th><th>{t('routing.colRack')}</th><th>{t('routing.colRequest')}</th><th>{t('routing.colState')}</th><th>{t('routing.colWait')}</th><th>{t('routing.colLegs')}</th><th>{t('routing.colCreated')}</th></tr></thead>
                <tbody>
                  {page.items.map((o) => (
                    <tr key={o.id} className={o.id === selected ? 'selected' : ''} onClick={() => setSelected(o.id)}>
                      <td className="nowrap"><strong>{o.processCode}</strong> <span className="muted">{t('routing.versionShort', { n: o.processVersion })}</span></td>
                      <td>{RACK_KIND_LABEL[o.rackKind] ?? o.rackKind}</td>
                      <td className="mono">{o.requestLocationCode}</td>
                      <td><OrderStatePill state={o.state} /></td>
                      <td>{o.waitKind !== 'None' ? t(waitCause(o) === 'portHeld' ? 'routing.waitPortHeld' : WAIT_KEY[o.waitKind]) : ''}</td>
                      <td className="num">{o.legs.length}</td>
                      <td className="nowrap num">{formatTime(o.createdAt)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Async>
      </Panel>
      {selected && <OrderDetailPanel key={selected} id={selected} />}
    </div>
  )
}

function OrderDetailPanel({ id }: { id: string }) {
  const t = useT()
  const { user } = useAuth()
  const isAdmin = hasRole(user, 'Admin')
  const detail = useApi(useCallback(() => api.routingOrder(id), [id]), [...EVENTS])
  const [dialog, setDialog] = useState<{ kind: 'pick' | 'drop'; leg: Leg } | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [releaseReason, setReleaseReason] = useState<string | null>(null)   // CR-042: null = dialog closed
  const [cancelReason, setCancelReason] = useState<string | null>(null)   // CR-047: null = dialog closed

  const run = async (fn: () => Promise<unknown>) => {
    setBusy(true); setError(null)
    try { await fn(); setDialog(null); setReleaseReason(null); setCancelReason(null); detail.reload() } catch (e) { setError(describeError(e)) } finally { setBusy(false) }
  }

  return (
    <Panel title={t('routing.detailTitle')}>
      <Async state={detail} empty={{ title: t('routing.none') }}>
        {({ order, events, commands }) => (
          <>
            {order.state === 'ReconciliationRequired' && <div className="notice notice-warn" role="alert">{t('routing.reconNotice')}</div>}
            {error && <div className="notice notice-error" role="alert">{error}</div>}
            {isAdmin && orderIsOpen(order.state) && (
              <div className="btn-row">
                <Button size="sm" variant="danger" disabled={orderInMotion(order.legs)} onClick={() => { setError(null); setCancelReason('') }}>{t('linked.btnCancelOrder')}</Button>
                {orderInMotion(order.legs) && <span className="muted small">{t('linked.cancelOrderInMotion')}</span>}
              </div>
            )}
            {order.racksHeld && (
              <div className="notice notice-warn" role="alert">
                {t('linked.racksHeldNotice')}{' '}
                {isAdmin && <Button size="sm" variant="danger" onClick={() => { setError(null); setReleaseReason('') }}>{t('linked.btnReleaseRacks')}</Button>}
              </div>
            )}
            <section className="detail-sec">
              <h3>{t('routing.secGeneral')}</h3>
              <dl className="kv2">
                <dt>{t('routing.rowProcess')}</dt><dd><strong>{order.processCode}</strong> {t('routing.versionShort', { n: order.processVersion })}</dd>
                <dt>{t('routing.colState')}</dt><dd><OrderStatePill state={order.state} /></dd>
                <dt>{t('routing.colWait')}</dt><dd>{order.waitKind !== 'None' ? t(waitCause(order) === 'portHeld' ? 'routing.waitPortHeld' : WAIT_KEY[order.waitKind]) : <span className="muted">—</span>}</dd>
                <dt>{t('routing.colStep')}</dt><dd className="mono">{order.currentStepKey ?? '—'}</dd>
                <dt>{t('routing.colRack')}</dt><dd>{RACK_KIND_LABEL[order.rackKind] ?? order.rackKind}</dd>
                <dt>{t('routing.colRequest')}</dt><dd className="mono">{order.requestLocationCode}</dd>
                <dt>{t('routing.rowRequestKey')}</dt><dd className="mono">{order.requestKey}</dd>
                <dt>{t('routing.colCreated')}</dt><dd>{formatDateTime(order.createdAt)}</dd>
                <dt>{t('linked.rowRequestedBy')}</dt><dd>{order.requestedBy ?? '—'}</dd>
                <dt>{t('linked.colQueue')}</dt><dd>{order.queueSequence != null ? t('linked.queueCell', { status: t(`linked.queue_${order.queueStatus}` as MsgKey), position: order.queueSequence, scope: order.queueScope ?? t('linked.scopeNone') }) : '—'}</dd>
                {order.stateReason && <><dt>{t('linked.rowReason')}</dt><dd>{order.stateReason}</dd></>}
              </dl>
            </section>

            <section className="detail-sec">
              <h3>{t('routing.secLegs')}</h3>
              {order.legs.length === 0 ? <p className="muted">{t('routing.legsNone')}</p> : (
                <div className="table-wrap">
                  <table className="grid">
                    <thead><tr><th>{t('routing.legSeq')}</th><th>{t('routing.legStep')}</th><th>{t('routing.legRoute')}</th><th>{t('routing.legState')}</th><th>{t('routing.legRack')}</th><th>{t('routing.legAgv')}</th><th>{t('routing.legRobot')}</th>{isAdmin && <th />}</tr></thead>
                    <tbody>
                      {order.legs.map((l) => (
                        <tr key={l.id}>
                          <td className="num">{l.sequence}</td>
                          <td>{STEP_TYPE_LABEL[l.stepType] ?? l.stepType}</td>
                          <td className="mono nowrap">{l.sourceLocationCode ?? '—'} → {l.destinationLocationCode ?? t('routing.legNoDestination')}</td>
                          <td>
                            <Pill tone={LEG_STATE_TONE[l.state] ?? 'muted'}>{LEG_STATE_LABEL[l.state] ?? l.state}</Pill>
                            {l.waitKind !== 'None' && <div className="muted">{t(WAIT_KEY[l.waitKind])}</div>}
                          </td>
                          <td>{l.rackOnAgv ? <Pill tone="warn">{t('routing.rackOnAmr')}</Pill> : <span className="muted">{t('routing.rackNotOnAmr')}</span>}</td>
                          <td className="mono">{l.agvTaskCode ?? '—'}</td>
                          <td className="mono">{l.robotCode ?? (l.agvTaskCode && !l.agvId ? <span className="muted">{t('routing.legRobotWaiting')}</span> : '—')}</td>
                          {isAdmin && (
                            <td className="nowrap">
                              {l.state === 'InProgress' && !l.pickConfirmedAt && <Button size="sm" onClick={() => setDialog({ kind: 'pick', leg: l })}>{t('routing.btnPick')}</Button>}
                              {l.pickConfirmedAt && !l.dropConfirmedAt && l.destinationLocationCode && <Button size="sm" onClick={() => setDialog({ kind: 'drop', leg: l })}>{t('routing.btnDrop')}</Button>}
                            </td>
                          )}
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </section>

            <section className="detail-sec">
              <h3>{t('routing.secTransactions')}</h3>
              <TransactionTable rows={order.transactions} onChanged={detail.reload} compact />
            </section>

            <section className="detail-sec">
              <h3>{t('linked.secCommands')}</h3>
              {!commands || commands.length === 0 ? <p className="muted">{t('linked.commandsNone')}</p> : (
                <div className="table-wrap">
                  <table className="grid">
                    <thead><tr><th>{t('linked.colCreated')}</th><th>{t('linked.cmdLeg')}</th><th>{t('linked.cmdMessage')}</th><th>{t('linked.cmdMode')}</th><th>{t('linked.cmdResult')}</th></tr></thead>
                    <tbody>
                      {commands.map((c) => (
                        <tr key={c.id}>
                          <td className="nowrap num">{formatTime(c.at)}</td>
                          <td className="num">{order.legs.find((l) => c.requestCode && l.agvRequestCode === c.requestCode)?.sequence ?? '—'}</td>
                          <td>{c.direction} · {c.messageType}</td>
                          <td>{c.mode}</td>
                          <td>{c.result ?? '—'}{c.errorCode ? ` (${c.errorCode})` : ''}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </section>

            <section className="detail-sec">
              <h3>{t('routing.secEvents')}</h3>
              {events.length === 0 ? <p className="muted">{t('routing.eventsNone')}</p> : (
                <ol className="timeline">
                  {events.map((e) => <li key={e.id}><span className="tl-time num">{formatTime(e.occurredAt)}</span><span className="tl-body"><strong>{e.kind}</strong>{e.detail ? <small>{e.detail}</small> : null}</span></li>)}
                </ol>
              )}
            </section>
          </>
        )}
      </Async>
      {cancelReason !== null && (
        <Modal title={t('linked.cancelOrderTitle')} onClose={() => setCancelReason(null)}
          footer={<><Button onClick={() => setCancelReason(null)}>{t('routing.closeDialog')}</Button>
            <Button variant="danger" disabled={busy || !cancelReason.trim()} onClick={() => run(() => api.routingCancel(id, cancelReason.trim()))}>{t('routing.confirm')}</Button></>}>
          <p>{t('linked.cancelOrderBody')}</p>
          {error && <div className="notice notice-error" role="alert">{error}</div>}
          <label>{t('linked.releaseRacksReason')}<input type="text" value={cancelReason} maxLength={500} onChange={(e) => setCancelReason(e.target.value)} /></label>
        </Modal>
      )}
      {releaseReason !== null && (
        <Modal title={t('linked.releaseRacksTitle')} onClose={() => setReleaseReason(null)}
          footer={<><Button onClick={() => setReleaseReason(null)}>{t('routing.closeDialog')}</Button>
            <Button variant="danger" disabled={busy || !releaseReason.trim()} onClick={() => run(() => api.routingReleaseRacks(id, releaseReason.trim()))}>{t('routing.confirm')}</Button></>}>
          <p>{t('linked.releaseRacksBody')}</p>
          {error && <div className="notice notice-error" role="alert">{error}</div>}
          <label>{t('linked.releaseRacksReason')}<input type="text" value={releaseReason} maxLength={500} onChange={(e) => setReleaseReason(e.target.value)} /></label>
        </Modal>
      )}
      {dialog && (
        <Modal title={t(dialog.kind === 'pick' ? 'routing.confirmPickTitle' : 'routing.confirmDropTitle')} onClose={() => setDialog(null)}
          footer={<><Button onClick={() => setDialog(null)}>{t('routing.closeDialog')}</Button>
            <Button variant="primary" disabled={busy} onClick={() => run(() => (dialog.kind === 'pick' ? api.routingPick(dialog.leg.id) : api.routingDrop(dialog.leg.id)))}>{t('routing.confirm')}</Button></>}>
          <p>{t(dialog.kind === 'pick' ? 'routing.confirmPickBody' : 'routing.confirmDropBody')}</p>
          <p className="mono">{dialog.leg.sourceLocationCode} → {dialog.leg.destinationLocationCode ?? '…'}</p>
        </Modal>
      )}
    </Panel>
  )
}

// ====================================================================================================================== Station Transactions
function TransactionsTab() {
  const t = useT()
  const [activeOnly, setActiveOnly] = useState(true)
  const list = useApi(useCallback(() => api.routingTransactions(activeOnly), [activeOnly]), [...EVENTS])
  return (
    <Panel flush actions={<label className="inline-filter"><input type="checkbox" checked={activeOnly} onChange={(e) => setActiveOnly(e.target.checked)} /> {t('routing.txActiveOnly')}</label>}>
      <Async state={list} isEmpty={(d) => d.length === 0} empty={{ title: t('routing.txNone'), hint: t('routing.txNoneHint') }}>
        {(rows) => <TransactionTable rows={rows} onChanged={list.reload} />}
      </Async>
      <p className="muted" style={{ padding: '8px 14px' }}>{t('routing.txHandshakeNote')}</p>
    </Panel>
  )
}

function TransactionTable({ rows, onChanged, compact }: { rows: StationTransaction[]; onChanged: () => void; compact?: boolean }) {
  const t = useT()
  const { user } = useAuth()
  const isAdmin = hasRole(user, 'Admin')
  const [error, setError] = useState<string | null>(null)
  const [resolving, setResolving] = useState<StationTransaction | null>(null)
  const exit = async (tx: StationTransaction) => {
    setError(null)
    try { await api.routingTxExit(tx.id); onChanged() } catch (e) { setError(describeError(e)) }
  }
  if (rows.length === 0) return <p className="muted">{t('routing.txNone')}</p>
  return (
    <>
      {error && <div className="notice notice-error" role="alert">{error}</div>}
      <div className="table-wrap">
        <table className="grid">
          <thead><tr><th>{t('routing.txColPort')}</th><th>{t('routing.txColPlc')}</th><th>{t('routing.txColOp')}</th>{!compact && <th>{t('routing.txColRequestId')}</th>}<th>{t('routing.txColHandshake')}</th><th>{t('routing.txColState')}</th>{isAdmin && <th />}</tr></thead>
          <tbody>
            {rows.map((x) => (
              <tr key={x.id}>
                <td className="mono nowrap"><strong>{x.portId}</strong> <span className="muted">{x.locationCode}</span></td>
                <td className="mono">{x.plcId}</td>
                <td>{t(x.operation === 'Pickup' ? 'routing.txOpPickup' : 'routing.txOpDelivery')}</td>
                {!compact && <td className="mono">{x.requestId ?? '—'}</td>}
                <td className="num">{x.handshakeStep > 0 ? `${x.handshakeStep}/9` : '—'}</td>
                <td>
                  <TxStatePill state={x.state} />
                  {x.suspended && <div className="muted" role="status">{t('routing.txSuspended')}</div>}
                  {x.reconciliationReason && <div className="flag">{t('routing.txReconTitle')}: {x.reconciliationReason}</div>}
                </td>
                {isAdmin && (
                  <td className="nowrap">
                    {x.state === 'HandoverCompleted' && <Button size="sm" title={t('routing.txBtnExitHint')} onClick={() => exit(x)}>{t('routing.txBtnExit')}</Button>}
                    {x.state === 'ReconciliationRequired' && <Button size="sm" variant="danger" onClick={() => setResolving(x)}>{t('routing.txBtnResolve')}</Button>}
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {resolving && <ResolveDialog tx={resolving} onClose={() => setResolving(null)} onDone={() => { setResolving(null); onChanged() }} />}
    </>
  )
}

function ResolveDialog({ tx, onClose, onDone }: { tx: StationTransaction; onClose: () => void; onDone: () => void }) {
  const t = useT()
  const [mode, setMode] = useState<'resume' | 'close'>('resume')
  const [state, setState] = useState<string>(TX_RESUME_STATES[0])
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const submit = async () => {
    setBusy(true); setError(null)
    try { await api.routingTxResolve(tx.id, mode === 'resume' ? state : null); onDone() } catch (e) { setError(describeError(e)) } finally { setBusy(false) }
  }
  return (
    <Modal title={t('routing.txResolveTitle', { port: tx.portId })} onClose={onClose}
      footer={<><Button onClick={onClose}>{t('routing.closeDialog')}</Button><Button variant="primary" disabled={busy} onClick={submit}>{t('routing.confirm')}</Button></>}>
      <p>{t('routing.txResolveBody')}</p>
      {error && <div className="notice notice-error" role="alert">{error}</div>}
      <label className="radio"><input type="radio" checked={mode === 'resume'} onChange={() => setMode('resume')} /> {t('routing.txResolveResume')}{' '}
        <select value={state} disabled={mode !== 'resume'} onChange={(e) => setState(e.target.value)}>
          {TX_RESUME_STATES.map((s) => <option key={s} value={s}>{TX_STATE_LABEL[s]}</option>)}
        </select>
      </label>
      <label className="radio"><input type="radio" checked={mode === 'close'} onChange={() => setMode('close')} /> {t('routing.txResolveClose')}</label>
    </Modal>
  )
}
