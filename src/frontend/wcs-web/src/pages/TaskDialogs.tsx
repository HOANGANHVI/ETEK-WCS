import { useCallback, useState } from 'react'
import { api } from '../api/client'
import { ApiRequestError } from '../api/errors'
import type { TransportTask } from '../api/types'
import { Button, Loading } from '../components/ui'
import { Modal } from '../components/Modal'
import { useApi } from '../hooks/useApi'
import { useT } from '../i18n'
import { Rich } from '../components/Rich'
import { describeError, flowInfo, taskCode, TASK_STATE_VI } from '../i18n/vi'
import { Field } from './config/FormKit'
import { processOf } from '../lib/requestOptions'
import { RequestOptions } from '../components/RequestOptions'

function useAction(onDone: () => void, onStale: () => void) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const run = async (action: () => Promise<unknown>) => {
    setBusy(true); setError(null)
    try { await action(); onDone() }
    catch (e) {
      setError(describeError(e))
      // The task moved on (or the account lost the right) while the dialog was open: refresh what the screen shows.
      if (e instanceof ApiRequestError && [403, 404, 409].includes(e.status)) onStale()
    } finally { setBusy(false) }
  }
  return { busy, error, run }
}

export function CreateTaskDialog({ onClose, onCreated, onOrderCreated }: { onClose: () => void; onCreated: (t: TransportTask) => void; onOrderCreated: () => void }) {
  const t = useT()
  const flows = useApi(useCallback(() => api.flows(), []))
  const processes = useApi(useCallback(() => api.routingStartable(), []))   // CR-039: every published process, not only the Phase 1 flows
  const [flow, setFlow] = useState('')
  const [station, setStation] = useState('')
  // One key per opened dialog: double-clicking "Tạo" cannot create two tasks (the API is idempotent on this key).
  const [requestKey] = useState(() => crypto.randomUUID())
  const process = processOf(flow, processes.data ?? [])
  const stations = process ? process.triggerLocationCodes : flows.data?.find((f) => f.flow === flow)?.stations ?? []
  const a = useAction(() => undefined, () => undefined)
  const create = async () => {
    if (process) {
      await api.routingStart({ requestKey, locationCode: station, requestType: process.requestType, rackKind: process.rackKind, processCode: process.code })
      onOrderCreated()
    } else onCreated(await api.createTask(flow, station, requestKey))
  }

  return (
    <Modal title={t('tasks.createTitle')} onClose={onClose} footer={<>
      <Button onClick={onClose}>{t('tasks.skip')}</Button>
      <Button variant="primary" disabled={a.busy || !flow || !station || (!!process && !process.acceptsRequests)}
        onClick={() => void a.run(create)}>{t('tasks.create')}</Button>
    </>}>
      {a.error && <div className="notice notice-bad" role="alert">{a.error}</div>}
      {processes.error && !processes.data && <div className="notice notice-bad" role="alert">{t('ops.createProcessesFailed')} {processes.error}</div>}
      {process && !process.acceptsRequests && (
        <div className="notice notice-warn" role="status">
          <p>{t('ops.createNotReady', { code: process.code, readiness: process.readiness })}</p>
          {process.blockedDetail && <p className="muted small">{process.blockedDetail}</p>}
        </div>
      )}
      {process && <p className="muted small">{t('ops.createIsOrder')}</p>}
      {flows.loading && !flows.data ? <Loading /> : flows.error && !flows.data ? <div className="notice notice-bad">{flows.error}</div> : (
        <div className="form-grid single">
          <Field label={t('tasks.flowField')} hint={t('tasks.flowHint')}>
            <select value={flow} onChange={(e) => { setFlow(e.target.value); setStation('') }}>
              <option value="">{t('tasks.chooseFlow')}</option>
              <RequestOptions flows={flows.data ?? []} processes={processes.data ?? []} />
            </select>
          </Field>
          <Field label={t('tasks.stationField')} hint={t('tasks.stationHint')}>
            <select value={station} disabled={!flow} onChange={(e) => setStation(e.target.value)}>
              <option value="">{t('tasks.chooseStation')}</option>
              {stations.map((s) => <option key={s} value={s}>{s}</option>)}
            </select>
          </Field>
        </div>
      )}
    </Modal>
  )
}

export function EditTaskDialog({ task, stationCode, onClose, onChanged }: {
  task: TransportTask; stationCode: string; onClose: () => void; onChanged: () => void
}) {
  const t = useT()
  const flows = useApi(useCallback(() => api.flows(), []))
  const currentFlow = flows.data?.find((f) => f.flow.toLowerCase() === task.flow.toLowerCase())?.flow ?? task.flow
  const [flow, setFlow] = useState(currentFlow)
  const [station, setStation] = useState(stationCode)
  const stations = flows.data?.find((f) => f.flow === flow)?.stations ?? []
  const a = useAction(() => { onChanged(); onClose() }, onChanged)
  const unchanged = flow === currentFlow && station === stationCode
  return (
    <Modal title={t('tasks.editTitle', { code: taskCode(task.id) })} onClose={onClose} footer={<>
      <Button onClick={onClose}>{t('tasks.skip')}</Button>
      <Button variant="primary" disabled={a.busy || !flow || !station || unchanged}
        onClick={() => void a.run(() => api.updateTask(task.id, flow, station, task.state))}>{t('tasks.saveChanges')}</Button>
    </>}>
      <p className="dialog-hint">{t('tasks.editHint', { state: TASK_STATE_VI[task.state] })}</p>
      {a.error && <div className="notice notice-bad" role="alert">{a.error}</div>}
      <div className="form-grid single">
        <Field label={t('tasks.flowField')}>
          <select value={flow} disabled={flows.loading && !flows.data} onChange={(e) => {
            const next = e.target.value; setFlow(next)
            const allowed = flows.data?.find((f) => f.flow === next)?.stations ?? []
            if (!allowed.includes(station)) setStation(allowed[0] ?? '')
          }}>
            {(flows.data ?? [{ flow: currentFlow, rackKind: '', stations: [] }]).map((f) => <option key={f.flow} value={f.flow}>{flowInfo(f.flow).code} · {flowInfo(f.flow).label}</option>)}
          </select>
        </Field>
        <Field label={t('tasks.stationField')}>
          <select value={station} onChange={(e) => setStation(e.target.value)} disabled={flows.loading && !flows.data}>
            {(stations.length ? stations : [stationCode]).map((s) => <option key={s} value={s}>{s}</option>)}
          </select>
        </Field>
      </div>
    </Modal>
  )
}

export function DeleteTaskDialog({ task, onClose, onChanged }: { task: TransportTask; onClose: () => void; onChanged: () => void }) {
  const t = useT()
  const a = useAction(() => { onChanged(); onClose() }, onChanged)
  return (
    <Modal title={t('tasks.deleteTitle', { code: taskCode(task.id) })} onClose={onClose} footer={<>
      <Button onClick={onClose}>{t('tasks.skip')}</Button>
      <Button variant="danger" disabled={a.busy} onClick={() => void a.run(() => api.deleteTask(task.id, task.state))}>{t('tasks.confirmDelete')}</Button>
    </>}>
      {a.error && <div className="notice notice-bad" role="alert">{a.error}</div>}
      <p className="dialog-hint">
        {t('tasks.deleteBody1', { flow: flowInfo(task.flow).code, state: TASK_STATE_VI[task.state] })}{' '}
        <Rich text={t('tasks.deleteBody2')} parts={{ bold: <strong>{t('tasks.deleteBold')}</strong> }} />
      </p>
    </Modal>
  )
}
