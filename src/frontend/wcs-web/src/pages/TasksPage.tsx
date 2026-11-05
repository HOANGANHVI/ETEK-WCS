import { useCallback, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api } from '../api/client'
import { TASK_STATES, type TaskState, type TransportTask } from '../api/types'
import { formatDateTime, formatTime } from '../components/format'
import { Async, Badge, Card } from '../components/ui'
import { useApi } from '../hooks/useApi'

const CANCELLABLE: TaskState[] = [
  'Pending', 'Validating', 'Queued', 'Dispatching', 'Accepted', 'Executing', 'WaitingForPlc', 'WaitingForDestination', 'Continuing', 'CancelPending',
]

export function TasksPage() {
  const [params, setParams] = useSearchParams()
  const [filter, setFilter] = useState<TaskState | ''>('')
  const selectedId = params.get('id')

  const tasks = useApi(useCallback(() => api.tasks(filter || undefined, 0, 100), [filter]), ['TaskCreated', 'TaskStateChanged'])
  const locations = useApi(useCallback(() => api.locations(), []))
  const agvs = useApi(useCallback(() => api.agvs(), []))

  const locCode = useMemo(() => new Map(locations.data?.map((l) => [l.id, l.code]) ?? []), [locations.data])
  const agvCode = useMemo(() => new Map(agvs.data?.map((a) => [a.id, a.code]) ?? []), [agvs.data])
  const code = (id: string | null) => (id ? locCode.get(id) ?? id.slice(0, 8) : '—')

  return (
    <>
      <h1>Task Monitoring</h1>
      <div className="split">
        <Card
          title="Tasks"
          actions={
            <label className="inline">
              State{' '}
              <select value={filter} onChange={(e) => setFilter(e.target.value as TaskState | '')}>
                <option value="">All</option>
                {TASK_STATES.map((s) => <option key={s} value={s}>{s}</option>)}
              </select>
            </label>
          }
        >
          <Async state={tasks} isEmpty={(d) => d.items.length === 0} empty={{ title: 'No tasks', hint: filter ? 'Try another state filter.' : 'Tasks appear when a station requests a transport.' }}>
            {(page) => (
              <table className="selectable">
                <thead><tr><th>Created</th><th>Flow</th><th>AGV</th><th>From → To</th><th>State</th></tr></thead>
                <tbody>
                  {page.items.map((t) => (
                    <tr key={t.id} className={t.id === selectedId ? 'selected' : ''} onClick={() => setParams({ id: t.id })}>
                      <td>{formatTime(t.createdAt)}</td>
                      <td>{t.flow}</td>
                      <td>{agvCode.get(t.agvId) ?? '—'}</td>
                      <td>{code(t.sourceLocationId ?? t.requestLocationId)} → {code(t.destinationLocationId)}</td>
                      <td><Badge value={t.state} />{t.requiresReconciliation && <span className="flag" title="Needs reconciliation"> ⚠</span>}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </Async>
        </Card>

        <Card title="Task detail">
          {selectedId ? <TaskDetail key={selectedId} id={selectedId} code={code} /> : <div className="state state-empty"><strong>Select a task</strong><span>Its history and actions appear here.</span></div>}
        </Card>
      </div>
    </>
  )
}

function TaskDetail({ id, code }: { id: string; code: (id: string | null) => string }) {
  const task = useApi(useCallback(() => api.task(id), [id]), ['TaskStateChanged'])
  const history = useApi(useCallback(() => api.taskHistory(id), [id]), ['TaskStateChanged'])
  const health = useApi(useCallback(() => api.health(), []))
  const [busy, setBusy] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)

  const run = async (action: () => Promise<unknown>) => {
    setBusy(true); setActionError(null)
    try { await action(); task.reload(); history.reload() }
    catch (e) { setActionError(e instanceof Error ? e.message : 'Action failed') }
    finally { setBusy(false) }
  }

  return (
    <Async state={task} empty={{ title: 'Task not found' }}>
      {(t: TransportTask) => (
        <>
          <dl className="kv">
            <dt>Id</dt><dd className="mono">{t.id}</dd>
            <dt>State</dt><dd><Badge value={t.state} /> <span className="muted">{t.stateReason}</span></dd>
            <dt>Flow / rack</dt><dd>{t.flow} · {t.rackKind}</dd>
            <dt>Request</dt><dd>{code(t.requestLocationId)} <span className="muted">({t.requestKey})</span></dd>
            <dt>Source → destination</dt><dd>{code(t.sourceLocationId)} → {code(t.destinationLocationId)}</dd>
            <dt>Leg / PLC permission</dt><dd>{t.leg} · {t.plcPermissionGranted ? 'granted' : 'not granted'}</dd>
            <dt>RCS</dt><dd className="mono">{t.rcsTaskCode ?? 'not accepted yet'}</dd>
            <dt>Pick / drop confirmed</dt><dd>{formatDateTime(t.pickConfirmedAt)} / {formatDateTime(t.dropConfirmedAt)}</dd>
            {t.requiresReconciliation && (<><dt>Attention</dt><dd className="text-bad">Physical outcome must be reconciled before resources are released.</dd></>)}
          </dl>

          <div className="actions">
            {CANCELLABLE.includes(t.state) && (
              <button type="button" disabled={busy} onClick={() => {
                const reason = window.prompt('Reason for cancelling this task?')
                if (reason) void run(() => api.cancelTask(t.id, reason, 'operator'))
              }}>Cancel task</button>
            )}
            {health.data?.integration.mode === 'Simulator' && !['Completed', 'Failed', 'Cancelled'].includes(t.state) && (
              <button type="button" className="secondary" disabled={busy} onClick={() => void run(() => api.simulateNext(t.id))}>
                Simulate next device step
              </button>
            )}
          </div>
          {actionError && <div className="state state-error" role="alert">{actionError}</div>}

          <h3>History</h3>
          <Async state={history} isEmpty={(d) => d.length === 0} empty={{ title: 'No history' }}>
            {(rows) => (
              <ol className="timeline">
                {rows.map((h) => (
                  <li key={h.id}>
                    <span className="muted">{formatTime(h.occurredAt)}</span>{' '}
                    {h.fromState && h.toState && h.fromState !== h.toState ? <><Badge value={h.fromState} /> → <Badge value={h.toState} /></> : <strong>{h.event}</strong>}
                    {h.detail && <div className="muted">{h.detail} · {h.source}</div>}
                  </li>
                ))}
              </ol>
            )}
          </Async>
        </>
      )}
    </Async>
  )
}
