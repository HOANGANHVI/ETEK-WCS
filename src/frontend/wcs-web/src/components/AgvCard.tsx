import { Link } from 'react-router-dom'
import type { Agv, TransportTask } from '../api/types'
import { formatDateTime, timeAgo } from '../i18n/format'
import { AGV_ROLE_VI, flowInfo, LEG_VI, taskCode } from '../i18n/vi'
import { AgvHoldPanel } from './AgvHoldPanel'
import { ConnectionPill, FlowStepper, Pill, TaskStatePill } from './ui'

interface Props {
  agv: Agv
  task: TransportTask | null          // the task currently occupying the AGV, if any
  lastTask: TransportTask | null      // most recent task of this AGV (for "last activity")
  code: (locationId: string | null) => string
  now: number
  compact?: boolean
  onChanged?: () => void
}

function activity(agv: Agv) {
  if (agv.dispatchHold) return <Pill tone="bad">Dispatch Hold</Pill>
  if (agv.connectionStatus === 'Offline' || agv.connectionStatus === 'Error') return <Pill tone="bad">Không khả dụng</Pill>
  return agv.busy ? <Pill tone="ok">Đang hoạt động</Pill> : <Pill tone="warn">Đang chờ nhiệm vụ</Pill>
}

/** Operational card of one AGV. No position is shown: the API does not provide AGV coordinates yet. */
export function AgvCard({ agv, task, lastTask, code, now, compact, onChanged }: Props) {
  const flow = task ? flowInfo(task.flow) : null
  return (
    <article className={`agv-card ${compact ? 'compact' : ''}`}>
      <header className="agv-head">
        <div className="agv-name">
          <span className={`agv-badge ${agv.role === 'FullRack' ? 'full' : 'empty'}`}>{agv.code}</span>
          <div><strong>{AGV_ROLE_VI[agv.role]}</strong>{!compact && <small>Mã robot RCS: {agv.rcsRobotCode ?? 'chưa cấu hình'}</small>}</div>
        </div>
        <div className="agv-states">{activity(agv)}<ConnectionPill value={agv.connectionStatus} /></div>
      </header>

      <AgvHoldPanel agv={agv} onChanged={onChanged} compact={compact} />

      {task && flow ? (
        <div className="agv-task">
          <div className="agv-task-top">
            <Link to={`/tasks?id=${task.id}`} className="mono">{taskCode(task.id)}</Link>
            <span>{flow.code} · {flow.label}</span>
            <TaskStatePill state={task.state} />
          </div>
          <div className="route">
            <div><small>Điểm xuất phát</small><strong>{code(task.sourceLocationId ?? task.requestLocationId)}</strong></div>
            <span className="route-arrow" aria-hidden="true">➜</span>
            <div><small>Điểm đích</small><strong>{code(task.destinationLocationId)}</strong></div>
            <div className="route-leg"><small>Công đoạn</small><strong>{LEG_VI[task.leg] ?? task.leg}</strong></div>
          </div>
          {!compact && <FlowStepper task={task} />}
        </div>
      ) : (
        <div className="agv-idle">Không có nhiệm vụ đang thực hiện.</div>
      )}

      <footer className="agv-foot">
        <span title={formatDateTime(lastTask?.updatedAt ?? agv.lastStatusAt)}>Hoạt động gần nhất: {timeAgo(lastTask?.updatedAt ?? agv.lastStatusAt, now)}</span>
        <span className="muted">Vị trí hiện tại: chưa có dữ liệu định vị</span>
      </footer>
    </article>
  )
}
