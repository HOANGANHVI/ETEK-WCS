import { Link } from 'react-router-dom'
import type { Agv, TransportTask } from '../api/types'
import { formatDateTime, timeAgo } from '../i18n/format'
import { t, useT, type MsgKey } from '../i18n'
import type { RcsWork } from '../api/workTypes'
import { orderCode, RCS_STATE_TONE, workLink } from '../lib/work'
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
  /** CR-051: the transport order leg this AGV has with the RCS now, and when its latest work changed */
  leg?: RcsWork | null
  lastWorkAt?: string | null
}

function activity(agv: Agv, hasLeg: boolean) {
  if (agv.dispatchHold) return <Pill tone="bad">{t('agv.dispatchHold')}</Pill>
  if (agv.connectionStatus === 'Offline' || agv.connectionStatus === 'Error') return <Pill tone="bad">{t('agv.unavailable')}</Pill>
  return agv.busy || hasLeg ? <Pill tone="ok">{t('agv.active')}</Pill> : <Pill tone="warn">{t('agv.awaitingTask')}</Pill>
}

/** Operational card of one AGV. No position is shown: the API does not provide AGV coordinates yet. */
export function AgvCard({ agv, task, lastTask, code, now, compact, onChanged, leg, lastWorkAt }: Props) {
  const t = useT()
  const flow = task ? flowInfo(task.flow) : null
  const last = [lastTask?.updatedAt, lastWorkAt].filter((x): x is string => !!x).sort().at(-1) ?? agv.lastStatusAt
  return (
    <article className={`agv-card ${compact ? 'compact' : ''}`}>
      <header className="agv-head">
        <div className="agv-name">
          <span className={`agv-badge ${agv.role === 'FullRack' ? 'full' : 'empty'}`}>{agv.code}</span>
          <div><strong>{AGV_ROLE_VI[agv.role]}</strong>{!compact && <small>{t('agv.rcsRobotCode', { code: agv.rcsRobotCode ?? t('common.notSet') })}</small>}</div>
        </div>
        <div className="agv-states">{activity(agv, !!leg)}<ConnectionPill value={agv.connectionStatus} /></div>
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
            <div><small>{t('agv.from')}</small><strong>{code(task.sourceLocationId ?? task.requestLocationId)}</strong></div>
            <span className="route-arrow" aria-hidden="true">➜</span>
            <div><small>{t('agv.to')}</small><strong>{code(task.destinationLocationId)}</strong></div>
            <div className="route-leg"><small>{t('agv.leg')}</small><strong>{LEG_VI[task.leg] ?? task.leg}</strong></div>
          </div>
          {!compact && <FlowStepper task={task} />}
        </div>
      ) : leg ? (
        <div className="agv-task">
          <div className="agv-task-top">
            <Link to={workLink(leg)} className="mono">{orderCode(leg.orderId ?? leg.id)}</Link>
            <span>{leg.processCode} · {t('linked.rcsLeg', { n: leg.legSequence ?? 0 })}</span>
            <Pill tone={RCS_STATE_TONE[leg.rcsState]}>{t(`linked.rcsState_${leg.rcsState}` as MsgKey)}</Pill>
          </div>
          <div className="route">
            <div><small>{t('agv.from')}</small><strong>{leg.source ?? '—'}</strong></div>
            <span className="route-arrow" aria-hidden="true">➜</span>
            <div><small>{t('agv.to')}</small><strong>{leg.destination ?? '—'}</strong></div>
          </div>
        </div>
      ) : (
        <div className="agv-idle">{t('agv.idle')}</div>
      )}

      <footer className="agv-foot">
        <span title={formatDateTime(last)}>{t('agv.lastActivity', { when: timeAgo(last, now) })}</span>
      </footer>
    </article>
  )
}
