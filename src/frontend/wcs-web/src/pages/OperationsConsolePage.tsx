import { useCallback, useEffect, useState } from 'react'
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
import { CommunicationMonitor } from './ops/CommunicationMonitor'
import { ModePanel, type ModePanelApi } from './ops/ModePanel'
import '../styles/ops.css'

const EVENTS = ['TaskCreated', 'TaskStateChanged', 'TaskDeleted'] as const
const CANCELLABLE: TaskState[] = ['Pending', 'Validating', 'Queued', 'Dispatching', 'Accepted', 'Executing', 'WaitingForPlc', 'WaitingForDestination', 'Continuing']
const FINISHED: TaskState[] = ['Completed', 'Failed', 'Cancelled']

const GATE_REFRESH_MS = 5000

const modeApi: ModePanelApi = {
  readiness: (target) => api.executionModeReadiness(target),
  transition: (body) => api.executionModeTransition(body),
  acknowledge: (requestId, reason) => api.executionModeAcknowledge(requestId, reason),
  history: () => api.executionModeHistory(20),
  adoptLegacy: (reason) => api.executionModeAdoptLegacy(reason),
}

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
  const mode = useApi(useCallback(() => api.executionMode(), []))
  const gate = dispatchGate(health.data, fleet.data, mode.error && !mode.data ? null : mode.data)
  const select = (id: string) => setParams({ task: id })
  // The health (server guard) and the mode raise no event of their own: they are read again regularly, so the create gate never stays on an old answer (for example right after a restart)
  const { reload: reloadHealth } = health
  const { reload: reloadMode } = mode
  useEffect(() => {
    const timer = setInterval(() => { reloadHealth(); reloadMode() }, GATE_REFRESH_MS)
    return () => clearInterval(timer)
  }, [reloadHealth, reloadMode])

  return (
    <>
      <PageHeader title={t('ops.title')} description={t('ops.description')} actions={<Link to="/routing">{t('ops.ordersLink')}</Link>} />
      <ModePanel view={mode.data} loadFailed={!!mode.error} isAdmin={isAdmin} api={modeApi} onChanged={() => { mode.reload(); health.reload() }} />
      <ConnectionStrip health={health.data} fleet={fleet.data} loadFailed={!!(health.error || fleet.error)} gate={gate} />
      <Async state={flows} isEmpty={(d) => d.length === 0} empty={{ title: t('ops.createLoadFailed') }}>
        {(list) => <CreateRequest flows={list} gate={gate} api={createApi} onCreated={(x) => { tasks.reload(); select(x.id) }} />}
      </Async>
      <Async state={tasks} empty={{ title: t('ops.monNone') }}>
        {(page) => <TaskMonitor tasks={page.items} locations={locations.data ?? []} selectedId={selected} onSelect={select} />}
      </Async>
      <p className="muted small">{t('ops.ordersNote')}</p>
      {isAdmin && <UnlinkedMessages />}
      {selected ? <SelectedTask id={selected} isAdmin={isAdmin} locations={locations.data ?? []} /> : <p className="muted">{t('ops.detPick')}</p>}
    </>
  )
}

/** CR-038: messages that belong to no task (for example a callback for an unknown task code), shown as they are and never attached to a task by guesswork. */
function UnlinkedMessages() {
  const t = useT()
  const msgs = useApi(useCallback(() => api.unlinkedRcsMessages(), []), ['RcsMessageRecorded'])
  const load = useCallback((id: string) => api.rcsMessage(id), [])
  if (!msgs.data || msgs.data.length === 0) return null
  return <CommunicationMonitor title={t('ops.comUnlinkedTitle')} note={t('ops.comUnlinkedNote')} messages={msgs.data} error={msgs.error} loadDetail={load} />
}

function SelectedTask({ id, isAdmin, locations }: { id: string; isAdmin: boolean; locations: import('../api/types').Location[] }) {
  const t = useT()
  const task = useApi(useCallback(() => api.task(id), [id]), [...EVENTS])
  const history = useApi(useCallback(() => api.taskHistory(id), [id]), [...EVENTS])
  const messages = useApi(useCallback(() => api.taskRcsMessages(id), [id]), ['RcsMessageRecorded', 'TaskStateChanged'])   // CR-038: refreshed by the SignalR event of the hub that already exists
  const loadMessage = useCallback((mid: string) => api.rcsMessage(mid), [])
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
          <CommunicationMonitor messages={messages.data} error={messages.error} loadDetail={loadMessage} />
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
