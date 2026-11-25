import { useCallback, useMemo, useState } from 'react'
import { useSearchParams, useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { TASK_STATES, type TaskState, type TransportTask } from '../api/types'
import { CreateTaskDialog, DeleteTaskDialog, EditTaskDialog } from './TaskDialogs'
import { ReconciliationPanel } from './ReconciliationPanel'
import { USER_EDITABLE } from './taskRules'
import { Async, Button, FlowStepper, PageHeader, Panel, Pill, TaskStatePill } from '../components/ui'
import { StackedSummary, type SummaryItem } from '../components/charts'
import { DEMO_MODE, ENGINEERING_TOOLS } from '../config'
import { useApi } from '../hooks/useApi'
import { useCodeMaps } from '../hooks/useLookups'
import { useT, type MsgKey } from '../i18n'
import { describeHistory } from '../i18n/logCatalog'
import { formatDateTime, formatTime } from '../i18n/format'
import {
  AGV_ROLE_VI, DEVICE_EVENT_RE, OCCUPYING_STATES, describeError, flowInfo, HISTORY_EVENT_VI, LEG_VI, SOURCE_VI, TASK_STATE_VI, taskCode,
} from '../i18n/vi'

const CANCELLABLE: TaskState[] = [
  'Pending', 'Validating', 'Queued', 'Dispatching', 'Accepted', 'Executing', 'WaitingForPlc', 'WaitingForDestination', 'Continuing',
]
const EVENTS = ['TaskCreated', 'TaskStateChanged', 'TaskDeleted'] as const

type Dialog = { kind: 'create' } | { kind: 'edit'; task: TransportTask } | { kind: 'delete'; task: TransportTask } | null

export function TasksPage() {
  const tr = useT()
  const { user } = useAuth()
  const isAdmin = user?.role === 'Admin'
  const [dialog, setDialog] = useState<Dialog>(null)
  const [params, setParams] = useSearchParams()
  const navigate = useNavigate()
  const [stateFilter, setStateFilter] = useState<TaskState | ''>('')
  const [agvFilter, setAgvFilter] = useState('')
  const [search, setSearch] = useState('')
  const [reconOnly, setReconOnly] = useState(false)
  const [cancelFor, setCancelFor] = useState<string | null>(null)
  const urlId = params.get('id')

  const tasks = useApi(useCallback(() => api.tasks(stateFilter || undefined, 0, 200), [stateFilter]), [...EVENTS])
  const allTasks = useApi(useCallback(() => api.tasks(undefined, 0, 200), []), [...EVENTS])   // unfiltered, for the status summary only
  const agvs = useApi(useCallback(() => api.agvs(), []))
  const locations = useApi(useCallback(() => api.locations(), []))
  const codes = useCodeMaps(locations.data, agvs.data)

  const rows = useMemo(() => {
    const q = search.trim().toLowerCase()
    return (tasks.data?.items ?? []).filter((t) => (!agvFilter || t.agvId === agvFilter) && (!reconOnly || t.requiresReconciliation) && (!q || taskCode(t.id).toLowerCase().includes(q)))
  }, [tasks.data, agvFilter, search, reconOnly])

  const summary = useMemo<SummaryItem[]>(() => {
    const items = allTasks.data?.items ?? []
    const n = (f: (s: TaskState) => boolean) => items.filter((x) => f(x.state)).length
    return [
      { key: 'run', label: tr('tasks.sumInProgress'), tone: 'info', value: n((x) => OCCUPYING_STATES.includes(x)) },
      { key: 'queue', label: tr('tasks.sumQueued'), tone: 'warn', value: n((x) => x === 'Pending' || x === 'Validating' || x === 'Queued') },
      { key: 'done', label: tr('tasks.sumCompleted'), tone: 'ok', value: n((x) => x === 'Completed') },
      { key: 'fail', label: tr('tasks.sumFailed'), tone: 'bad', value: n((x) => x === 'Failed') },
      { key: 'cancel', label: tr('tasks.sumCancelled'), tone: 'muted', value: n((x) => x === 'Cancelled') },
    ]
  }, [allTasks.data, tr])

  // Nothing chosen yet: show the newest task so the detail area is never an empty void.
  const selectedId = urlId ?? rows[0]?.id ?? null

  const select = (id: string, cancel = false) => { setParams({ id }); setCancelFor(cancel ? id : null) }

  return (
    <>
      <PageHeader
        title={tr('tasks.title')}
        actions={<Button variant="primary" onClick={() => setDialog({ kind: 'create' })}>{tr('tasks.create')}</Button>}
      />
      <Panel title={tr('tasks.sumTitle')} subtitle={tr('tasks.sumSub', { n: allTasks.data?.items.length ?? 0 })} className="mb">
        <StackedSummary items={summary} ariaLabel={tr('tasks.sumTitle')} emptyTitle={tr('tasks.sumEmpty')} />
      </Panel>
      <div className={`task-layout ${selectedId ? 'with-detail' : ''}`}>
        <Panel
          title={tr('tasks.listPanel')}
          subtitle={tasks.data ? tr('tasks.listCount', { shown: rows.length, total: tasks.data.total }) : undefined}
          flush
          actions={
            <>
              <input type="search" placeholder={tr('tasks.searchCode')} aria-label={tr('tasks.searchCode')} value={search} onChange={(e) => setSearch(e.target.value)} />
              {isAdmin && !DEMO_MODE && <label className="inline-check"><input type="checkbox" checked={reconOnly} onChange={(e) => setReconOnly(e.target.checked)} /> {tr('tasks.needsRecon')}</label>}
              <select aria-label={tr('tasks.filterAgv')} value={agvFilter} onChange={(e) => setAgvFilter(e.target.value)}>
                <option value="">{tr('tasks.allAgv')}</option>
                {(agvs.data ?? []).map((a) => <option key={a.id} value={a.id}>{a.code}</option>)}
              </select>
              <select aria-label={tr('tasks.filterState')} value={stateFilter} onChange={(e) => setStateFilter(e.target.value as TaskState | '')}>
                <option value="">{tr('tasks.allStates')}</option>
                {TASK_STATES.map((s) => <option key={s} value={s}>{TASK_STATE_VI[s]}</option>)}
              </select>
            </>
          }
        >
          <Async state={tasks} isEmpty={(d) => d.items.length === 0} empty={{ title: tr('tasks.none'), hint: stateFilter ? tr('tasks.noneHintFiltered') : tr('tasks.noneHint') }}>
            {() => rows.length === 0 ? (
              <div className="state state-empty"><div><strong>{tr('tasks.noMatch')}</strong></div></div>
            ) : (
              <div className="table-wrap task-table">
                <table className="grid clickable">
                  <thead>
                    <tr><th>{tr('tasks.colCode')}</th><th>{tr('tasks.colCreated')}</th><th>{tr('tasks.colFlow')}</th><th>{tr('tasks.colAgv')}</th><th>{tr('tasks.colSource')}</th><th>{tr('tasks.colDestination')}</th><th>{tr('tasks.colState')}</th><th>{tr('tasks.colActions')}</th></tr>
                  </thead>
                  <tbody>
                    {rows.map((t) => (
                      <tr key={t.id} className={t.id === selectedId ? 'selected' : ''} onClick={() => select(t.id)}>
                        <td className="mono nowrap">{taskCode(t.id)}</td>
                        <td className="nowrap num">{formatTime(t.createdAt)}</td>
                        <td className="nowrap"><strong>{flowInfo(t.flow).code}</strong></td>
                        <td>{t.robotCode ?? codes.agv(t.agvId)}{t.rcsDispatchState && t.rcsDispatchState !== 'Assigned' && !['Completed', 'Failed', 'Cancelled'].includes(t.state) && <> <Pill tone={t.rcsDispatchState === 'SyncError' ? 'bad' : 'muted'} dot={false}>{tr(`tasks.dispatchState_${t.rcsDispatchState}` as MsgKey)}</Pill></>}</td>
                        <td>{codes.loc(t.sourceLocationId ?? t.requestLocationId)}</td>
                        <td>{codes.loc(t.destinationLocationId)}</td>
                        <td><TaskStatePill state={t.state} />{t.requiresReconciliation && <span className="flag" title={tr('tasks.reconFlag')}> ⚠</span>}</td>
                        <td className="nowrap" onClick={(e) => e.stopPropagation()}>
                          <Button size="sm" onClick={() => select(t.id)}>{tr('tasks.btnDetail')}</Button>{' '}
                          {USER_EDITABLE.includes(t.state) && <><Button size="sm" onClick={() => setDialog({ kind: 'edit', task: t })}>{tr('tasks.btnEdit')}</Button>{' '}<Button size="sm" variant="danger" onClick={() => setDialog({ kind: 'delete', task: t })}>{tr('tasks.btnDelete')}</Button>{' '}</>}
                          {isAdmin && !USER_EDITABLE.includes(t.state) && CANCELLABLE.includes(t.state) && <Button size="sm" variant="danger" onClick={() => select(t.id, true)}>{tr('tasks.btnCancel')}</Button>}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Async>
        </Panel>

        {selectedId && (
          <TaskDetail key={selectedId} id={selectedId} codes={codes} askCancel={isAdmin && cancelFor === selectedId} isAdmin={isAdmin}
            onCancelDone={() => setCancelFor(null)} onEdit={(t) => setDialog({ kind: 'edit', task: t })} onDelete={(t) => setDialog({ kind: 'delete', task: t })} />
        )}
      </div>

      {dialog?.kind === 'create' && <CreateTaskDialog onClose={() => setDialog(null)} onCreated={(t) => { setDialog(null); setParams({ id: t.id }); tasks.reload() }} onOrderCreated={() => { setDialog(null); void navigate('/routing') }} />}
      {dialog?.kind === 'edit' && <EditTaskDialog task={dialog.task} stationCode={codes.loc(dialog.task.requestLocationId)} onClose={() => setDialog(null)} onChanged={() => tasks.reload()} />}
      {dialog?.kind === 'delete' && <DeleteTaskDialog task={dialog.task} onClose={() => setDialog(null)} onChanged={() => tasks.reload()} />}
    </>
  )
}

interface DetailProps {
  id: string
  codes: ReturnType<typeof useCodeMaps>
  askCancel: boolean
  isAdmin: boolean
  onCancelDone: () => void
  onEdit: (t: TransportTask) => void
  onDelete: (t: TransportTask) => void
}

function TaskDetail({ id, codes, askCancel, isAdmin, onCancelDone, onEdit, onDelete }: DetailProps) {
  const tr = useT()
  const task = useApi(useCallback(() => api.task(id), [id]), [...EVENTS])
  const history = useApi(useCallback(() => api.taskHistory(id), [id]), [...EVENTS])
  const health = useApi(useCallback(() => api.health(), []))
  const agvs = useApi(useCallback(() => api.agvs(), []))
  const [busy, setBusy] = useState(false)
  const [reason, setReason] = useState('')
  const [confirming, setConfirming] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [actionMsg, setActionMsg] = useState<string | null>(null)

  const run = async (action: () => Promise<unknown>, ok?: string) => {
    setBusy(true); setActionError(null); setActionMsg(null)
    try { await action(); if (ok) setActionMsg(ok); task.reload(); history.reload() }
    catch (e) { setActionError(describeError(e)) }
    finally { setBusy(false) }
  }

  return (
    <Panel title={tr('tasks.detailTitle', { code: taskCode(id) })} className="task-detail">
      <Async state={task} empty={{ title: tr('tasks.notFound') }}>
        {(t: TransportTask) => {
          const agv = (agvs.data ?? []).find((a) => a.id === t.agvId)
          const flow = flowInfo(t.flow)
          const editable = USER_EDITABLE.includes(t.state)
          // Cancelling a task an AGV already took, and the simulator step, are Admin-only (also enforced by the API).
          const canCancel = isAdmin && !editable && CANCELLABLE.includes(t.state)
          const simulator = ENGINEERING_TOOLS && isAdmin && health.data?.integration.mode === 'Simulator'
          const events = history.data ?? []
          const deviceEvents = events.filter((h) => h.source === 'PLC' || h.source === 'RCS' || DEVICE_EVENT_RE.test(h.event))
          return (
            <>
              {t.requiresReconciliation && (
                <div className="notice notice-warn">{tr('tasks.reconNotice')}</div>
              )}

              <section className="detail-sec">
                <h3>{tr('tasks.secGeneral')}</h3>
                <dl className="kv2">
                  <dt>{tr('tasks.rowFlow')}</dt><dd><strong>{flow.code}</strong> · {flow.label}</dd>
                  <dt>{tr('tasks.rowState')}</dt><dd><TaskStatePill state={t.state} /> <span className="muted" title={t.stateReason ?? undefined}>{describeHistory(t.stateReasonCode, t.stateReasonArgs, t.stateReason)}</span></dd>
                  <dt>{tr('tasks.rowStation')}</dt><dd>{codes.loc(t.requestLocationId)}</dd>
                  <dt>{tr('tasks.rowRequestKey')}</dt><dd className="mono">{t.requestKey}</dd>
                  <dt>{tr('tasks.rowCreated')}</dt><dd>{formatDateTime(t.createdAt)}</dd>
                  <dt>{tr('tasks.rowUpdated')}</dt><dd>{formatDateTime(t.updatedAt)}</dd>
                  <dt>{tr('tasks.rowFinished')}</dt><dd>{formatDateTime(t.finishedAt)}</dd>
                </dl>
              </section>

              <section className="detail-sec">
                <h3>{tr('tasks.secAgv')}</h3>
                <dl className="kv2">
                  <dt>{tr('tasks.rowAgv')}</dt><dd><strong>{t.robotCode ?? codes.agv(t.agvId)}</strong>{agv && <> · {AGV_ROLE_VI[agv.role]}</>}{t.rcsDispatchState && <> · {tr(`tasks.dispatchState_${t.rcsDispatchState}` as MsgKey)}</>}</dd>
                  <dt>{tr('tasks.rowLeg')}</dt><dd>{LEG_VI[t.leg] ?? t.leg}</dd>
                  <dt>{tr('tasks.rowPlcPermission')}</dt><dd>{t.plcPermissionGranted ? <Pill tone="ok">{tr('tasks.plcGranted')}</Pill> : <Pill tone="muted">{tr('tasks.plcNotGranted')}</Pill>}</dd>
                  <dt>{tr('tasks.rowRcsRequest')}</dt><dd className="mono">{t.rcsRequestCode}</dd>
                  <dt>{tr('tasks.rowRcsTask')}</dt><dd className="mono">{t.rcsTaskCode ?? tr('tasks.rcsNotAccepted')}</dd>
                </dl>
              </section>

              <section className="detail-sec">
                <h3>{tr('tasks.secFlow')}</h3>
                <FlowStepper task={t} />
                <div className="route route-line">
                  <div><small>{tr('tasks.from')}</small><strong>{codes.loc(t.sourceLocationId ?? t.requestLocationId)}</strong></div>
                  <span className="route-arrow" aria-hidden="true">➜</span>
                  <div><small>{tr('tasks.to')}</small><strong>{codes.loc(t.destinationLocationId)}</strong></div>
                  <div><small>{tr('tasks.pickedAt')}</small><strong>{formatTime(t.pickConfirmedAt)}</strong></div>
                  <div><small>{tr('tasks.droppedAt')}</small><strong>{formatTime(t.dropConfirmedAt)}</strong></div>
                </div>
              </section>

              {(editable || canCancel || (simulator && !['Completed', 'Failed', 'Cancelled'].includes(t.state))) && (
                <section className="detail-sec">
                  <h3>{tr('tasks.secActions')}</h3>
                  {askCancel || confirming ? (
                    <div className="cancel-box">
                      <label>{tr('tasks.cancelReasonLabel')}
                        <input type="search" value={reason} onChange={(e) => setReason(e.target.value)} placeholder={tr('tasks.cancelReasonPh')} />
                      </label>
                      <div className="btn-row">
                        <Button variant="danger" disabled={busy || !reason.trim()} onClick={() => void run(() => api.cancelTask(t.id, reason.trim()), tr('tasks.cancelSent')).then(() => { setReason(''); setConfirming(false); onCancelDone() })}>{tr('tasks.confirmCancel')}</Button>
                        <Button onClick={() => { setReason(''); setConfirming(false); onCancelDone() }}>{tr('tasks.skip')}</Button>
                      </div>
                    </div>
                  ) : (
                    <div className="btn-row">
                      {editable && <Button disabled={busy} onClick={() => onEdit(t)}>{tr('tasks.editTask')}</Button>}
                      {editable && <Button variant="danger" disabled={busy} onClick={() => onDelete(t)}>{tr('tasks.deleteTask')}</Button>}
                      {canCancel && <Button variant="danger" disabled={busy} onClick={() => setConfirming(true)}>{tr('tasks.cancelTask')}</Button>}
                      {simulator && <Button variant="primary" disabled={busy} onClick={() => void run(() => api.simulateNext(t.id))}>{tr('tasks.simNext')}</Button>}
                    </div>
                  )}
                  {actionMsg && <div className="notice notice-info">{actionMsg}</div>}
                  {actionError && <div className="notice notice-bad" role="alert">{actionError}</div>}
                </section>
              )}

              {isAdmin && !DEMO_MODE && t.requiresReconciliation && ['Failed', 'Cancelled', 'Completed'].includes(t.state) && (
                <ReconciliationPanel task={t} onChanged={() => { task.reload(); history.reload() }} />
              )}

              <section className="detail-sec">
                <h3>{tr('tasks.secHistory')}</h3>
                <Async state={history} isEmpty={(d) => d.length === 0} empty={{ title: tr('tasks.noHistory') }}>
                  {(rows) => (
                    <ol className="timeline">
                      {rows.map((h) => {
                        const change = h.fromState && h.toState && h.fromState !== h.toState
                        return (
                          <li key={h.id}>
                            <span className="tl-time num">{formatTime(h.occurredAt)}</span>
                            <span className="tl-body">
                              {change ? (
                                <span className="tl-change">
                                  <TaskStatePill state={h.fromState as TaskState} /><span aria-hidden="true">➜</span><TaskStatePill state={h.toState as TaskState} />
                                </span>
                              ) : <strong>{HISTORY_EVENT_VI[h.event] ?? h.event}</strong>}
                              {h.detail && <small title={h.detail}>{describeHistory(h.detailCode, h.detailArgs, h.detail)} <span className="muted">· {SOURCE_VI[h.source] ?? h.source}</span></small>}
                            </span>
                          </li>
                        )
                      })}
                    </ol>
                  )}
                </Async>
              </section>

              <section className="detail-sec">
                <h3>{tr('tasks.secDevice')}</h3>
                {deviceEvents.length === 0 ? (
                  <p className="muted">{tr('tasks.noDevice')}</p>
                ) : (
                  <ul className="event-list">
                    {deviceEvents.map((h) => (
                      <li key={h.id}>
                        <span className="num muted">{formatTime(h.occurredAt)}</span>
                        <Pill tone="info" dot={false}>{SOURCE_VI[h.source] ?? h.source}</Pill>
                        <span>{h.toState && h.fromState !== h.toState ? `→ ${TASK_STATE_VI[h.toState as TaskState] ?? h.toState}` : HISTORY_EVENT_VI[h.event] ?? h.event}{h.detail ? ` · ${describeHistory(h.detailCode, h.detailArgs, h.detail)}` : ''}</span>
                      </li>
                    ))}
                  </ul>
                )}
              </section>
            </>
          )
        }}
      </Async>
    </Panel>
  )
}
