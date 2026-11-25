import { useCallback, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { api } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import type { TaskState, TransportTask } from '../api/types'
import { Async, Button, PageHeader } from '../components/ui'
import { useFleet } from '../fleet/fleetState'
import { useApi } from '../hooks/useApi'
import { useT } from '../i18n'
import { describeError } from '../i18n/vi'
import { dispatchGate, type CreateApi } from '../lib/opsConsole'
import { ConnectionStrip } from './ops/ConnectionStrip'
import { CreateRequest } from './ops/CreateRequest'
import { OpsTaskDetail } from './ops/OpsTaskDetail'
import { TaskMonitor } from './ops/TaskMonitor'
import { ReconciliationPanel } from './ReconciliationPanel'

const EVENTS = ['TaskCreated', 'TaskStateChanged', 'TaskDeleted'] as const
const CANCELLABLE: TaskState[] = ['Pending', 'Validating', 'Queued', 'Dispatching', 'Accepted', 'Executing', 'WaitingForPlc', 'WaitingForDestination', 'Continuing']
const FINISHED: TaskState[] = ['Completed', 'Failed', 'Cancelled']

const createApi: CreateApi = {
  create: (flow, station, key) => api.createTask(flow, station, key),
  list: async () => (await api.tasks(undefined, 0, 200)).items,
}

/**
 * Operations console for the real RCS: one place to see the RCS link and fleet, create a request through WCS, follow the tasks and read one task step by step.
 * It only calls WCS. It starts no second dispatch path, polls nothing of its own for the fleet (it uses the shared fleet data of the header and the AMR page) and never
 * confirms a pick / drop or changes inventory: those facts come from the RCS / PLC through the backend.
 */
export function OperationsConsolePage() {
  const t = useT()
  const { user } = useAuth()
  const isAdmin = user?.role === 'Admin'
  const [params, setParams] = useSearchParams()
  const selected = params.get('task')
  const fleet = useFleet()
  const health = useApi(useCallback(() => api.health(), []), ['TaskStateChanged'])
  const flows = useApi(useCallback(() => api.flows(), []))
  const tasks = useApi(useCallback(() => api.tasks(undefined, 0, 200), []), [...EVENTS])
  const locations = useApi(useCallback(() => api.locations(), []))
  const gate = dispatchGate(health.data, fleet.data)
  const select = (id: string) => setParams({ task: id })

  return (
    <>
      <PageHeader title={t('ops.title')} description={t('ops.description')} actions={<Link to="/routing">{t('ops.ordersLink')}</Link>} />
      <ConnectionStrip health={health.data} fleet={fleet.data} loadFailed={!!(health.error || fleet.error)} gate={gate} />
      <Async state={flows} isEmpty={(d) => d.length === 0} empty={{ title: t('ops.createLoadFailed') }}>
        {(list) => <CreateRequest flows={list} gate={gate} api={createApi} onCreated={(x) => { tasks.reload(); select(x.id) }} />}
      </Async>
      <Async state={tasks} empty={{ title: t('ops.monNone') }}>
        {(page) => <TaskMonitor tasks={page.items} locations={locations.data ?? []} selectedId={selected} onSelect={select} />}
      </Async>
      <p className="muted small">{t('ops.ordersNote')}</p>
      {selected ? <SelectedTask id={selected} isAdmin={isAdmin} locations={locations.data ?? []} /> : <p className="muted">{t('ops.detPick')}</p>}
    </>
  )
}

function SelectedTask({ id, isAdmin, locations }: { id: string; isAdmin: boolean; locations: import('../api/types').Location[] }) {
  const t = useT()
  const task = useApi(useCallback(() => api.task(id), [id]), [...EVENTS])
  const history = useApi(useCallback(() => api.taskHistory(id), [id]), [...EVENTS])
  const [reason, setReason] = useState('')
  const [asking, setAsking] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const cancel = async () => {
    if (busy || !reason.trim()) return
    setBusy(true); setError(null)
    try { await api.cancelTask(id, reason.trim()); setAsking(false); setReason(''); task.reload(); history.reload() }
    catch (e) { setError(describeError(e)) }
    finally { setBusy(false) }
  }

  return (
    <Async state={task} empty={{ title: t('ops.detLoadFailed') }}>
      {(x: TransportTask) => (
        <>
          <OpsTaskDetail task={x} history={history.data ?? []} locations={locations} />
          <section className="panel ops-actions">
            <h3>{t('ops.secActions')}</h3>
            {FINISHED.includes(x.state) ? <p className="muted">{t('ops.actionNone')}</p> : (
              isAdmin && CANCELLABLE.includes(x.state) && (asking ? (
                <div className="cancel-box">
                  <input type="search" value={reason} onChange={(e) => setReason(e.target.value)} placeholder={t('tasks.cancelReasonPh')} aria-label={t('tasks.cancelReasonLabel')} />
                  <div className="btn-row">
                    <Button variant="danger" disabled={busy || !reason.trim()} onClick={() => void cancel()}>{t('tasks.confirmCancel')}</Button>
                    <Button disabled={busy} onClick={() => { setAsking(false); setReason('') }}>{t('tasks.skip')}</Button>
                  </div>
                </div>
              ) : <div className="btn-row"><Button variant="danger" onClick={() => setAsking(true)}>{t('ops.actionCancel')}</Button></div>)
            )}
            {error && <div className="notice notice-bad" role="alert">{error}</div>}
            <p className="muted small">{t('ops.actionNoRetry')}</p>
            {isAdmin && x.requiresReconciliation && FINISHED.includes(x.state) && <ReconciliationPanel task={x} onChanged={() => { task.reload(); history.reload() }} />}
          </section>
        </>
      )}
    </Async>
  )
}
