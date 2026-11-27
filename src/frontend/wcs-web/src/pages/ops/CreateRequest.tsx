import { useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import type { Order, StartableProcess } from '../../api/routingTypes'
import type { FlowOption, TransportTask } from '../../api/types'
import { Button } from '../../components/ui'
import { Field } from '../config/FormKit'
import { Modal } from '../../components/Modal'
import { useT } from '../../i18n'
import { describeError, flowInfo, RACK_KIND_VI, TASK_STATE_VI, taskCode } from '../../i18n/vi'
import { applyCreate, checkOrderOutcome, checkOutcome, newRequestKey, sendCreate, sendProcessCreate, type CreateApi, type CreateEvent, type CreateState, type Gate, type ProcessCreateApi } from '../../lib/opsConsole'
import { processOf } from '../../lib/requestOptions'
import { RequestOptions } from '../../components/RequestOptions'
import { processName } from '../../i18n/processNames'


interface Props {
  flows: FlowOption[]; gate: Gate; api: CreateApi; onCreated: (t: TransportTask) => void
  /** CR-039: every published process, and the calls that start one. */
  processes?: StartableProcess[]; processApi?: ProcessCreateApi
}

/**
 * Creates a request through the normal WCS path only (POST /api/tasks): the server picks pick-up and drop-off, reserves them and decides whether to dispatch.
 * A published process is started through POST /api/routing/orders (a Transport Order) with the same one-send rule.
 * One send per RequestKey: the state machine (`createStep`) lets exactly one call out, a result is never retried, and an unknown outcome is checked by RequestKey, never resent.
 */
export function CreateRequest({ flows, gate, api, onCreated, processes = [], processApi }: Props) {
  const t = useT()
  const [flow, setFlow] = useState('')
  const [station, setStation] = useState('')
  const [state, setState] = useState<CreateState>(() => ({ phase: 'idle', requestKey: newRequestKey() }))
  const ref = useRef(state)        // the latest state, so two quick clicks cannot both see "confirming"
  const [task, setTask] = useState<TransportTask | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [found, setFound] = useState<TransportTask | null | 'none'>(null)
  const [order, setOrder] = useState<Order | null>(null)
  const [foundOrder, setFoundOrder] = useState<Order | null>(null)

  const holder = { get: () => ref.current, set: (s: CreateState) => { ref.current = s; setState(s) } }
  const apply = (e: CreateEvent): boolean => applyCreate(holder, e)
  const option = flows.find((f) => f.flow === flow)
  const process = processApi ? processOf(flow, processes) : undefined
  const stations = process ? process.triggerLocationCodes : option?.stations ?? []
  const ready = gate.allowed && flow !== '' && station !== '' && (!process || process.acceptsRequests)

  const send = async () => {
    if (process && processApi) {
      const p = await sendProcessCreate(holder, processApi, process, station)
      if (p.order) setOrder(p.order); else if (p.error) setError(describeError(p.error))
      return
    }
    const r = await sendCreate(holder, api, flow, station)
    if (r.task) { setTask(r.task); onCreated(r.task) } else if (r.error) setError(describeError(r.error))
  }
  const check = async () => {
    if (process && processApi) { const o = await checkOrderOutcome(holder, processApi); setFoundOrder(o); setFound(o ? null : 'none'); return }
    setFound((await checkOutcome(holder, api)) ?? 'none')
  }
  const again = () => { setTask(null); setOrder(null); setFoundOrder(null); setError(null); setFound(null); apply('reset') }
  const phase = state.phase
  const info = process ? { code: process.code, label: processName(process.code, process.name) } : flow ? flowInfo(flow) : null
  const rackKind = process?.rackKind ?? option?.rackKind

  return (
    <section className="panel ops-create" aria-label={t('ops.createTitle')}>
      <h2>{t('ops.createTitle')}</h2>
      <p className="muted">{t('ops.createIntro')}</p>
      <div className="form-grid">
        <Field label={t('ops.createFlow')}>
          <select value={flow} disabled={phase !== 'idle'} onChange={(e) => { setFlow(e.target.value); setStation('') }}>
            <option value="">{t('ops.createChooseFlow')}</option>
            <RequestOptions flows={flows} processes={processApi ? processes : []} />
          </select>
        </Field>
        <Field label={t('ops.createStation')}>
          <select value={station} disabled={!flow || phase !== 'idle'} onChange={(e) => setStation(e.target.value)}>
            <option value="">{t('ops.createChooseStation')}</option>
            {stations.map((s) => <option key={s} value={s}>{s}</option>)}
          </select>
        </Field>
      </div>
      {rackKind && <p className="muted small">{t('ops.createRack', { kind: RACK_KIND_VI[rackKind] ?? rackKind })}</p>}
      {process && !process.acceptsRequests && (
        <div className="notice notice-warn" role="status" data-process-blocked={process.code}>
          <p>{t('ops.createNotReady', { code: process.code, readiness: process.readiness })}</p>
          {process.blockedDetail && <p className="muted small">{process.blockedDetail}</p>}
        </div>
      )}
      {process && <p className="muted small">{t('ops.createIsOrder')}</p>}
      <p className="muted small">{t('ops.createRoute')}</p>
      <p className="muted small">{t('ops.createKey')}: <span className="mono">{state.requestKey}</span></p>
      <div className="btn-row">
        <Button variant="primary" disabled={!ready || phase !== 'idle'} onClick={() => apply('ask')}>{t('ops.createBtn')}</Button>
      </div>

      {phase === 'confirming' && info && (
        <Modal title={t('ops.createConfirmTitle')} onClose={() => apply('cancelAsk')}
          footer={<><Button onClick={() => apply('cancelAsk')}>{t('ops.createCancel')}</Button><Button variant="danger" disabled={!gate.allowed} onClick={() => void send()}>{t('ops.createSend')}</Button></>}>
          <p><strong>{t('ops.createConfirmBody', { flow: `${info.code} · ${info.label}`, station })}</strong></p>
          {gate.executionMode === 'Simulator' ? (
            <>
              <p data-exec-mode="Simulator"><strong>{t('ops.createModeTag', { mode: t('ops.modeTagSimulated') })}</strong></p>
              <div className="notice notice-info" role="note">{t('ops.createConfirmSim')}</div>
            </>
          ) : gate.executionMode === 'Real' ? (
            <>
              <p data-exec-mode="Real"><strong>{t('ops.createModeTag', { mode: t('ops.modeTagReal') })}</strong></p>
              <div className="notice notice-bad" role="note">{t('ops.createConfirmRealWarn')} {t('ops.createConfirmSafe')}</div>
            </>
          ) : (
            <>
              <p>{t('ops.createConfirmSource', { source: gate.fleetSource === 'Real' ? t('ops.sourceReal') : t('ops.sourceSimulator'), mode: gate.integrationMode ?? t('ops.noValue') })}</p>
              <div className="notice notice-warn" role="note">{t('ops.createConfirmLimit')}</div>
              <div className="notice notice-bad" role="note">{t('ops.createConfirmSafe')}</div>
            </>
          )}
        </Modal>
      )}
      {phase === 'sending' && <div className="notice notice-info" role="status">{t('ops.createSending')}</div>}
      {phase === 'created' && task && (
        <div className="notice notice-ok" role="status" data-create="created">
          <strong>{t('ops.createdTitle', { code: taskCode(task.id) })}</strong>
          <p>{t('ops.createdState', { state: TASK_STATE_VI[task.state] })}</p>
          <p>{t('ops.createdNote')}</p>
          <div className="btn-row"><Link to={`/operations?task=${task.id}`}>{t('ops.createdOpen')}</Link><Button onClick={again}>{t('ops.createAnother')}</Button></div>
        </div>
      )}
      {phase === 'created' && order && (
        <div className="notice notice-ok" role="status" data-create="created-order">
          <strong>{t('ops.createdOrderTitle', { code: order.processCode })}</strong>
          <p>{t('ops.createdOrderState', { state: order.state })}</p>
          <div className="btn-row"><Link to="/routing">{t('ops.ordersLink')}</Link><Button onClick={again}>{t('ops.createAnother')}</Button></div>
        </div>
      )}
      {phase === 'refused' && (
        <div className="notice notice-bad" role="alert" data-create="refused">
          <strong>{t('ops.refusedTitle')}</strong>
          {error && <p>{error}</p>}
          <div className="btn-row"><Button onClick={again}>{t('ops.createAnother')}</Button></div>
        </div>
      )}
      {phase === 'unknown' && (
        <div className="notice notice-warn" role="alert" data-create="unknown">
          <strong>{t('ops.unknownTitle')}</strong>
          <p>{t('ops.unknownBody', { key: state.requestKey })}</p>
          {error && <p className="muted small">{error}</p>}
          <div className="btn-row"><Button onClick={() => void check()}>{t('ops.unknownCheck')}</Button></div>
          {found === 'none' && <p>{t('ops.unknownNotFound')}</p>}
          {foundOrder && <p>{t('ops.createdOrderTitle', { code: foundOrder.processCode })} <Link to="/routing">{t('ops.ordersLink')}</Link></p>}
          {found && found !== 'none' && (
            <p>{t('ops.unknownFound', { code: taskCode(found.id), state: TASK_STATE_VI[found.state] })} <Link to={`/operations?task=${found.id}`}>{t('ops.createdOpen')}</Link></p>
          )}
        </div>
      )}
    </section>
  )
}
