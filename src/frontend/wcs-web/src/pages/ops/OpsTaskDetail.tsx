import type { Location, TaskHistory, TaskState, TransportTask } from '../../api/types'
import { Pill, TaskStatePill } from '../../components/ui'
import { useT, type MsgKey } from '../../i18n'
import { formatDateTime } from '../../i18n/format'
import { describeHistory } from '../../i18n/logCatalog'
import { DEVICE_EVENT_RE, flowInfo, HISTORY_EVENT_VI, SOURCE_VI, TASK_STATE_VI, taskCode } from '../../i18n/vi'
import { taskStages, type StageStatus } from '../../lib/opsConsole'
import { ModeBadge } from './ModePanel'

const TONE: Record<StageStatus, 'ok' | 'muted' | 'warn' | 'bad'> = { done: 'ok', pending: 'muted', unknown: 'warn', failed: 'bad' }

interface Props { task: TransportTask; history: TaskHistory[]; locations: Location[] }

/**
 * Read-only detail of one task. The seven steps are derived by `taskStages` from the task and its history only: "RCS accepted" is a different row from "AGV picked",
 * "AGV dropped" and "WCS confirmed completion", and a step the data cannot prove is shown as unknown, never as done. The actions (cancel, reconciliation) are added by the page.
 */
export function OpsTaskDetail({ task, history, locations }: Props) {
  const t = useT()
  const code = (id: string | null) => (id ? locations.find((l) => l.id === id)?.code ?? id.slice(0, 8) : t('ops.noValue'))
  const stages = taskStages(task, history)
  const never = t('ops.rowNever')
  const events = history.filter((h) => h.source === 'PLC' || h.source === 'RCS' || DEVICE_EVENT_RE.test(h.event))
  const at = (iso: string | null | undefined) => (iso ? formatDateTime(iso) : never)
  const flow = flowInfo(task.flow)

  return (
    <section className="panel ops-detail" aria-label={t('ops.detTitle', { code: taskCode(task.id) })}>
      <h2>{t('ops.detTitle', { code: taskCode(task.id) })}</h2>

      <h3>{t('ops.secStages')}</h3>
      <p className="muted small">{t('ops.stagesNote')}</p>
      <ol className="ops-stages">
        {stages.map((s) => (
          <li key={s.key} data-stage={s.key} data-status={s.status}>
            <Pill tone={TONE[s.status]}>{t(`ops.stageState_${s.status}` as MsgKey)}</Pill> <strong>{t(`ops.stage_${s.key}` as MsgKey)}</strong>
            {s.at && <span className="muted"> · {formatDateTime(s.at)}</span>}
            {s.status === 'unknown' && <div className="notice notice-warn" role="note">{t('ops.stageUnknownHint')}</div>}
          </li>
        ))}
      </ol>

      <h3>{t('ops.secGeneral')}</h3>
      <dl className="kv2">
        <dt>{t('ops.rowTaskId')}</dt><dd className="mono">{task.id}</dd>
        <dt>{t('ops.rowFlow')}</dt><dd>{flow.code} · {flow.label}</dd>
        <dt>{t('ops.rowState')}</dt><dd><TaskStatePill state={task.state} /> <span className="muted">{describeHistory(task.stateReasonCode, task.stateReasonArgs, task.stateReason)}</span></dd>
        <dt>{t('ops.rowStation')}</dt><dd>{code(task.requestLocationId)}</dd>
        <dt>{t('ops.rowRoute')}</dt><dd>{code(task.sourceLocationId ?? task.requestLocationId)} → {code(task.destinationLocationId)}</dd>
        <dt>{t('ops.rowRack')}</dt><dd className="mono">{task.rackId ?? t('ops.noValue')}</dd>
        <dt>{t('ops.rowModel')}</dt><dd className="muted">{t('ops.modelNotProvided')}</dd>
        <dt>{t('ops.rowExecMode')}</dt><dd data-exec-mode={task.executionMode ?? 'none'}>{task.executionMode ? <ModeBadge mode={task.executionMode} /> : <span className="muted">{t('ops.execModeLegacy')}</span>}</dd>
        <dt>{t('ops.rowRequestKey')}</dt><dd className="mono">{task.requestKey}</dd>
        <dt>{t('ops.rowCreated')}</dt><dd>{at(task.createdAt)}</dd>
        <dt>{t('ops.rowUpdated')}</dt><dd>{at(task.updatedAt)}</dd>
        <dt>{t('ops.rowFinished')}</dt><dd>{at(task.finishedAt)}</dd>
      </dl>

      <h3>{t('ops.secDispatch')}</h3>
      <dl className="kv2">
        <dt>{t('ops.rowRcsRequest')}</dt><dd className="mono">{task.rcsRequestCode}</dd>
        <dt>{t('ops.rowRcsTask')}</dt><dd className="mono">{task.rcsTaskCode ?? t('ops.rowRcsTaskNone')}</dd>
        <dt>{t('ops.rowDispatch')}</dt><dd>{task.rcsDispatchState ? t(`tasks.dispatchState_${task.rcsDispatchState}` as MsgKey) : t('ops.dispatchNone')}</dd>
        <dt>{t('ops.rowAgv')}</dt><dd>{task.robotCode ?? (task.agvId ? task.agvId.slice(0, 8) : never)}</dd>
        <dt>{t('ops.rowAssignedAt')}</dt><dd>{at(task.assignedAt)}</dd>
        <dt>{t('ops.rowPickup')}</dt><dd>{at(task.pickConfirmedAt)}</dd>
        <dt>{t('ops.rowDrop')}</dt><dd>{at(task.dropConfirmedAt)}</dd>
      </dl>

      <h3>{t('ops.secHistory')}</h3>
      {history.length === 0 ? <p className="muted">{t('ops.noDevice')}</p> : (
        <ol className="timeline">
          {history.map((h) => (
            <li key={h.id}>
              <span className="tl-time num">{formatDateTime(h.occurredAt)}</span>
              <span className="tl-body">
                <strong>{h.toState && h.fromState !== h.toState ? `→ ${TASK_STATE_VI[h.toState as TaskState] ?? h.toState}` : HISTORY_EVENT_VI[h.event] ?? h.event}</strong>
                {h.detail && <small> {describeHistory(h.detailCode, h.detailArgs, h.detail)} <span className="muted">· {SOURCE_VI[h.source] ?? h.source}</span></small>}
              </span>
            </li>
          ))}
        </ol>
      )}

      <h3>{t('ops.secDevice')}</h3>
      {events.length === 0 ? <p className="muted">{t('ops.noDevice')}</p> : (
        <ul className="event-list">
          {events.map((h) => (
            <li key={h.id}><span className="num muted">{formatDateTime(h.occurredAt)}</span> <Pill tone="info" dot={false}>{SOURCE_VI[h.source] ?? h.source}</Pill> {HISTORY_EVENT_VI[h.event] ?? h.event}{h.detail ? ` · ${describeHistory(h.detailCode, h.detailArgs, h.detail)}` : ''}</li>
          ))}
        </ul>
      )}
    </section>
  )
}
