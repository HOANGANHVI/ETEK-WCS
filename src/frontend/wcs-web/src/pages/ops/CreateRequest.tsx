import { useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import type { FlowOption, TransportTask } from '../../api/types'
import { Button } from '../../components/ui'
import { Field } from '../config/FormKit'
import { Modal } from '../../components/Modal'
import { useT } from '../../i18n'
import { describeError, flowInfo, RACK_KIND_VI, TASK_STATE_VI, taskCode } from '../../i18n/vi'
import { applyCreate, checkOutcome, newRequestKey, sendCreate, type CreateApi, type CreateEvent, type CreateState, type Gate } from '../../lib/opsConsole'


interface Props { flows: FlowOption[]; gate: Gate; api: CreateApi; onCreated: (t: TransportTask) => void }

/**
 * Creates a request through the normal WCS path only (POST /api/tasks): the server picks pick-up and drop-off, reserves them and decides whether to dispatch.
 * One send per RequestKey: the state machine (`createStep`) lets exactly one call out, a result is never retried, and an unknown outcome is checked by RequestKey, never resent.
 */
export function CreateRequest({ flows, gate, api, onCreated }: Props) {
  const t = useT()
  const [flow, setFlow] = useState('')
  const [station, setStation] = useState('')
  const [state, setState] = useState<CreateState>(() => ({ phase: 'idle', requestKey: newRequestKey() }))
  const ref = useRef(state)        // the latest state, so two quick clicks cannot both see "confirming"
  const [task, setTask] = useState<TransportTask | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [found, setFound] = useState<TransportTask | null | 'none'>(null)

  const holder = { get: () => ref.current, set: (s: CreateState) => { ref.current = s; setState(s) } }
  const apply = (e: CreateEvent): boolean => applyCreate(holder, e)
  const option = flows.find((f) => f.flow === flow)
  const ready = gate.allowed && flow !== '' && station !== ''

  const send = async () => {
    const r = await sendCreate(holder, api, flow, station)
    if (r.task) { setTask(r.task); onCreated(r.task) } else if (r.error) setError(describeError(r.error))
  }
  const check = async () => { setFound((await checkOutcome(holder, api)) ?? 'none') }
  const again = () => { setTask(null); setError(null); setFound(null); apply('reset') }
  const phase = state.phase
  const info = flow ? flowInfo(flow) : null

  return (
    <section className="panel ops-create" aria-label={t('ops.createTitle')}>
      <h2>{t('ops.createTitle')}</h2>
      <p className="muted">{t('ops.createIntro')}</p>
      <div className="form-grid">
        <Field label={t('ops.createFlow')}>
          <select value={flow} disabled={phase !== 'idle'} onChange={(e) => { setFlow(e.target.value); setStation('') }}>
            <option value="">{t('ops.createChooseFlow')}</option>
            {flows.map((f) => <option key={f.flow} value={f.flow}>{flowInfo(f.flow).code} · {flowInfo(f.flow).label}</option>)}
          </select>
        </Field>
        <Field label={t('ops.createStation')}>
          <select value={station} disabled={!flow || phase !== 'idle'} onChange={(e) => setStation(e.target.value)}>
            <option value="">{t('ops.createChooseStation')}</option>
            {(option?.stations ?? []).map((s) => <option key={s} value={s}>{s}</option>)}
          </select>
        </Field>
      </div>
      {option && <p className="muted small">{t('ops.createRack', { kind: RACK_KIND_VI[option.rackKind] ?? option.rackKind })}</p>}
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
          {found && found !== 'none' && (
            <p>{t('ops.unknownFound', { code: taskCode(found.id), state: TASK_STATE_VI[found.state] })} <Link to={`/operations?task=${found.id}`}>{t('ops.createdOpen')}</Link></p>
          )}
        </div>
      )}
    </section>
  )
}
