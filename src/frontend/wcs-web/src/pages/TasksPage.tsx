import { useCallback, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { TASK_STATES, type TaskState, type TransportTask } from '../api/types'
import { CreateTaskDialog, DeleteTaskDialog, EditTaskDialog } from './TaskDialogs'
import { USER_EDITABLE } from './taskRules'
import { Async, Button, FlowStepper, PageHeader, Panel, Pill, TaskStatePill } from '../components/ui'
import { useApi } from '../hooks/useApi'
import { useCodeMaps } from '../hooks/useLookups'
import { formatDateTime, formatTime } from '../i18n/format'
import {
  AGV_ROLE_VI, DEVICE_EVENT_RE, describeError, flowInfo, HISTORY_EVENT_VI, LEG_VI, reasonVi, SOURCE_VI, TASK_STATE_VI, taskCode,
} from '../i18n/vi'

const CANCELLABLE: TaskState[] = [
  'Pending', 'Validating', 'Queued', 'Dispatching', 'Accepted', 'Executing', 'WaitingForPlc', 'WaitingForDestination', 'Continuing',
]
const EVENTS = ['TaskCreated', 'TaskStateChanged', 'TaskDeleted'] as const

type Dialog = { kind: 'create' } | { kind: 'edit'; task: TransportTask } | { kind: 'delete'; task: TransportTask } | null

export function TasksPage() {
  const { user } = useAuth()
  const isAdmin = user?.role === 'Admin'
  const [dialog, setDialog] = useState<Dialog>(null)
  const [params, setParams] = useSearchParams()
  const [stateFilter, setStateFilter] = useState<TaskState | ''>('')
  const [agvFilter, setAgvFilter] = useState('')
  const [search, setSearch] = useState('')
  const [cancelFor, setCancelFor] = useState<string | null>(null)
  const urlId = params.get('id')

  const tasks = useApi(useCallback(() => api.tasks(stateFilter || undefined, 0, 200), [stateFilter]), [...EVENTS])
  const agvs = useApi(useCallback(() => api.agvs(), []))
  const locations = useApi(useCallback(() => api.locations(), []))
  const codes = useCodeMaps(locations.data, agvs.data)

  const rows = useMemo(() => {
    const q = search.trim().toLowerCase()
    return (tasks.data?.items ?? []).filter((t) => (!agvFilter || t.agvId === agvFilter) && (!q || taskCode(t.id).toLowerCase().includes(q)))
  }, [tasks.data, agvFilter, search])

  // Nothing chosen yet: show the newest task so the detail area is never an empty void.
  const selectedId = urlId ?? rows[0]?.id ?? null

  const select = (id: string, cancel = false) => { setParams({ id }); setCancelFor(cancel ? id : null) }

  return (
    <>
      <PageHeader
        title="Quản lý nhiệm vụ"
        description="Theo dõi toàn bộ nhiệm vụ vận chuyển, vòng đời trạng thái và các sự kiện PLC/RCS liên quan."
        actions={<Button variant="primary" onClick={() => setDialog({ kind: 'create' })}>Tạo nhiệm vụ</Button>}
      />
      <div className={`task-layout ${selectedId ? 'with-detail' : ''}`}>
        <Panel
          title="Danh sách nhiệm vụ"
          subtitle={tasks.data ? `${rows.length} / ${tasks.data.total} nhiệm vụ` : undefined}
          flush
          actions={
            <>
              <input type="search" placeholder="Tìm mã nhiệm vụ" aria-label="Tìm mã nhiệm vụ" value={search} onChange={(e) => setSearch(e.target.value)} />
              <select aria-label="Lọc theo AGV" value={agvFilter} onChange={(e) => setAgvFilter(e.target.value)}>
                <option value="">Tất cả AGV</option>
                {(agvs.data ?? []).map((a) => <option key={a.id} value={a.id}>{a.code}</option>)}
              </select>
              <select aria-label="Lọc theo trạng thái" value={stateFilter} onChange={(e) => setStateFilter(e.target.value as TaskState | '')}>
                <option value="">Tất cả trạng thái</option>
                {TASK_STATES.map((s) => <option key={s} value={s}>{TASK_STATE_VI[s]}</option>)}
              </select>
            </>
          }
        >
          <Async state={tasks} isEmpty={(d) => d.items.length === 0} empty={{ title: 'Chưa có nhiệm vụ nào', hint: stateFilter ? 'Thử chọn trạng thái khác.' : 'Nhiệm vụ xuất hiện khi trạm yêu cầu vận chuyển.' }}>
            {() => rows.length === 0 ? (
              <div className="state state-empty"><div><strong>Không có nhiệm vụ khớp bộ lọc</strong></div></div>
            ) : (
              <div className="table-wrap task-table">
                <table className="grid clickable">
                  <thead>
                    <tr><th>Mã nhiệm vụ</th><th>Thời gian tạo</th><th>Quy trình</th><th>AGV</th><th>Xuất phát</th><th>Đích</th><th>Trạng thái</th><th>Thao tác</th></tr>
                  </thead>
                  <tbody>
                    {rows.map((t) => (
                      <tr key={t.id} className={t.id === selectedId ? 'selected' : ''} onClick={() => select(t.id)}>
                        <td className="mono nowrap">{taskCode(t.id)}</td>
                        <td className="nowrap num">{formatTime(t.createdAt)}</td>
                        <td className="nowrap"><strong>{flowInfo(t.flow).code}</strong></td>
                        <td>{codes.agv(t.agvId)}</td>
                        <td>{codes.loc(t.sourceLocationId ?? t.requestLocationId)}</td>
                        <td>{codes.loc(t.destinationLocationId)}</td>
                        <td><TaskStatePill state={t.state} />{t.requiresReconciliation && <span className="flag" title="Cần đối soát vị trí rack"> ⚠</span>}</td>
                        <td className="nowrap" onClick={(e) => e.stopPropagation()}>
                          <Button size="sm" onClick={() => select(t.id)}>Chi tiết</Button>{' '}
                          {USER_EDITABLE.includes(t.state) && <><Button size="sm" onClick={() => setDialog({ kind: 'edit', task: t })}>Sửa</Button>{' '}<Button size="sm" variant="danger" onClick={() => setDialog({ kind: 'delete', task: t })}>Xóa</Button>{' '}</>}
                          {isAdmin && !USER_EDITABLE.includes(t.state) && CANCELLABLE.includes(t.state) && <Button size="sm" variant="danger" onClick={() => select(t.id, true)}>Hủy</Button>}
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

      {dialog?.kind === 'create' && <CreateTaskDialog onClose={() => setDialog(null)} onCreated={(t) => { setDialog(null); setParams({ id: t.id }); tasks.reload() }} />}
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
    <Panel title={`Chi tiết nhiệm vụ ${taskCode(id)}`} className="task-detail">
      <Async state={task} empty={{ title: 'Không tìm thấy nhiệm vụ' }}>
        {(t: TransportTask) => {
          const agv = (agvs.data ?? []).find((a) => a.id === t.agvId)
          const flow = flowInfo(t.flow)
          const editable = USER_EDITABLE.includes(t.state)
          // Cancelling a task an AGV already took, and the simulator step, are Admin-only (also enforced by the API).
          const canCancel = isAdmin && !editable && CANCELLABLE.includes(t.state)
          const simulator = isAdmin && health.data?.integration.mode === 'Simulator'
          const events = history.data ?? []
          const deviceEvents = events.filter((h) => h.source === 'PLC' || h.source === 'RCS' || DEVICE_EVENT_RE.test(h.event))
          return (
            <>
              {t.requiresReconciliation && (
                <div className="notice notice-warn">Cần đối soát vị trí rack thực tế trước khi giải phóng giữ chỗ và tài nguyên (tồn kho không tự điều chỉnh).</div>
              )}

              <section className="detail-sec">
                <h3>Thông tin chung</h3>
                <dl className="kv2">
                  <dt>Quy trình</dt><dd><strong>{flow.code}</strong> · {flow.label}</dd>
                  <dt>Trạng thái</dt><dd><TaskStatePill state={t.state} /> <span className="muted">{reasonVi(t.stateReason)}</span></dd>
                  <dt>Trạm yêu cầu</dt><dd>{codes.loc(t.requestLocationId)}</dd>
                  <dt>Khóa yêu cầu</dt><dd className="mono">{t.requestKey}</dd>
                  <dt>Thời gian tạo</dt><dd>{formatDateTime(t.createdAt)}</dd>
                  <dt>Cập nhật gần nhất</dt><dd>{formatDateTime(t.updatedAt)}</dd>
                  <dt>Kết thúc</dt><dd>{formatDateTime(t.finishedAt)}</dd>
                </dl>
              </section>

              <section className="detail-sec">
                <h3>AGV thực hiện</h3>
                <dl className="kv2">
                  <dt>AGV</dt><dd><strong>{codes.agv(t.agvId)}</strong>{agv && <> · {AGV_ROLE_VI[agv.role]}</>}</dd>
                  <dt>Công đoạn hiện tại</dt><dd>{LEG_VI[t.leg] ?? t.leg}</dd>
                  <dt>PLC cho phép</dt><dd>{t.plcPermissionGranted ? <Pill tone="ok">Đã cho phép</Pill> : <Pill tone="muted">Chưa cấp</Pill>}</dd>
                  <dt>Mã yêu cầu RCS</dt><dd className="mono">{t.rcsRequestCode}</dd>
                  <dt>Mã nhiệm vụ RCS</dt><dd className="mono">{t.rcsTaskCode ?? 'RCS chưa tiếp nhận'}</dd>
                </dl>
              </section>

              <section className="detail-sec">
                <h3>Luồng xử lý</h3>
                <FlowStepper task={t} />
                <div className="route route-line">
                  <div><small>Điểm xuất phát</small><strong>{codes.loc(t.sourceLocationId ?? t.requestLocationId)}</strong></div>
                  <span className="route-arrow" aria-hidden="true">➜</span>
                  <div><small>Điểm đích</small><strong>{codes.loc(t.destinationLocationId)}</strong></div>
                  <div><small>Đã lấy rack</small><strong>{formatTime(t.pickConfirmedAt)}</strong></div>
                  <div><small>Đã thả rack</small><strong>{formatTime(t.dropConfirmedAt)}</strong></div>
                </div>
              </section>

              {(editable || canCancel || (simulator && !['Completed', 'Failed', 'Cancelled'].includes(t.state))) && (
                <section className="detail-sec">
                  <h3>Thao tác</h3>
                  {askCancel || confirming ? (
                    <div className="cancel-box">
                      <label>Lý do hủy nhiệm vụ
                        <input type="search" value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Nhập lý do (bắt buộc)" />
                      </label>
                      <div className="btn-row">
                        <Button variant="danger" disabled={busy || !reason.trim()} onClick={() => void run(() => api.cancelTask(t.id, reason.trim()), 'Đã gửi yêu cầu hủy. Nhiệm vụ chỉ chuyển sang “Đã hủy” sau khi RCS xác nhận.').then(() => { setReason(''); setConfirming(false); onCancelDone() })}>Xác nhận hủy</Button>
                        <Button onClick={() => { setReason(''); setConfirming(false); onCancelDone() }}>Bỏ qua</Button>
                      </div>
                    </div>
                  ) : (
                    <div className="btn-row">
                      {editable && <Button disabled={busy} onClick={() => onEdit(t)}>Sửa nhiệm vụ</Button>}
                      {editable && <Button variant="danger" disabled={busy} onClick={() => onDelete(t)}>Xóa nhiệm vụ</Button>}
                      {canCancel && <Button variant="danger" disabled={busy} onClick={() => setConfirming(true)}>Hủy nhiệm vụ</Button>}
                      {simulator && <Button variant="primary" disabled={busy} onClick={() => void run(() => api.simulateNext(t.id))}>Chạy bước thiết bị tiếp theo (mô phỏng)</Button>}
                    </div>
                  )}
                  {actionMsg && <div className="notice notice-info">{actionMsg}</div>}
                  {actionError && <div className="notice notice-bad" role="alert">{actionError}</div>}
                </section>
              )}

              <section className="detail-sec">
                <h3>Lịch sử chuyển trạng thái</h3>
                <Async state={history} isEmpty={(d) => d.length === 0} empty={{ title: 'Chưa có lịch sử' }}>
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
                              {h.detail && <small>{reasonVi(h.detail)} <span className="muted">· {SOURCE_VI[h.source] ?? h.source}</span></small>}
                            </span>
                          </li>
                        )
                      })}
                    </ol>
                  )}
                </Async>
              </section>

              <section className="detail-sec">
                <h3>Sự kiện PLC / RCS</h3>
                {deviceEvents.length === 0 ? (
                  <p className="muted">Chưa có sự kiện PLC/RCS nào cho nhiệm vụ này.</p>
                ) : (
                  <ul className="event-list">
                    {deviceEvents.map((h) => (
                      <li key={h.id}>
                        <span className="num muted">{formatTime(h.occurredAt)}</span>
                        <Pill tone="info" dot={false}>{SOURCE_VI[h.source] ?? h.source}</Pill>
                        <span>{h.toState && h.fromState !== h.toState ? `→ ${TASK_STATE_VI[h.toState as TaskState] ?? h.toState}` : HISTORY_EVENT_VI[h.event] ?? h.event}{h.detail ? ` · ${reasonVi(h.detail)}` : ''}</span>
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
