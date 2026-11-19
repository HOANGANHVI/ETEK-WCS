import type { ButtonHTMLAttributes, ReactNode } from 'react'
import { taskStage } from './taskStage'
import { IconInbox } from './icons'
import { useT, type MsgKey } from '../i18n'
import type { ApiState } from '../hooks/useApi'
import type { TaskState, TransportTask } from '../api/types'
import {
  AGV_CONNECTION_TONE, AGV_CONNECTION_VI, OCCUPANCY_TONE, OCCUPANCY_VI, RACK_STATUS_TONE, RACK_STATUS_VI, TASK_STATE_TONE,
  TASK_STATE_VI, type Tone,
} from '../i18n/vi'

/* ------------------------------------------------------------------ page structure */
export function PageHeader({ title, description, actions }: { title: string; description?: string; actions?: ReactNode }) {
  return (
    <div className="page-header">
      <div>
        <h1>{title}</h1>
        {description && <p>{description}</p>}
      </div>
      {actions && <div className="page-actions">{actions}</div>}
    </div>
  )
}

export function Panel({ title, subtitle, actions, children, flush, className = '' }: {
  title?: string; subtitle?: string; actions?: ReactNode; children: ReactNode; flush?: boolean; className?: string
}) {
  return (
    <section className={`panel ${className}`}>
      {(title || actions) && (
        <header className="panel-head">
          <div>
            {title && <h2>{title}</h2>}
            {subtitle && <span className="panel-sub">{subtitle}</span>}
          </div>
          {actions && <div className="panel-actions">{actions}</div>}
        </header>
      )}
      <div className={flush ? 'panel-body flush' : 'panel-body'}>{children}</div>
    </section>
  )
}

export function Kpi({ label, value, hint, tone = 'info', icon }: { label: string; value: ReactNode; hint?: string; tone?: Tone; icon?: ReactNode }) {
  return (
    <div className={`kpi kpi-${tone}`}>
      {icon && <span className="kpi-icon">{icon}</span>}
      <div className="kpi-text">
        <span className="kpi-label">{label}</span>
        <strong className="kpi-value">{value}</strong>
        {hint && <span className="kpi-hint">{hint}</span>}
      </div>
    </div>
  )
}

/* ------------------------------------------------------------------ status */
export function Pill({ tone = 'muted', children, dot = true }: { tone?: Tone; children: ReactNode; dot?: boolean }) {
  return <span className={`pill pill-${tone}`}>{dot && <i aria-hidden="true" />}{children}</span>
}
export const TaskStatePill = ({ state }: { state: TaskState }) => <Pill tone={TASK_STATE_TONE[state]}>{TASK_STATE_VI[state]}</Pill>
export const ConnectionPill = ({ value }: { value: string }) => <Pill tone={AGV_CONNECTION_TONE[value] ?? 'muted'}>{AGV_CONNECTION_VI[value] ?? value}</Pill>
export const OccupancyPill = ({ value }: { value: string }) => <Pill tone={OCCUPANCY_TONE[value] ?? 'muted'}>{OCCUPANCY_VI[value] ?? value}</Pill>
export const RackPill = ({ value }: { value: string }) => <Pill tone={RACK_STATUS_TONE[value] ?? 'muted'}>{RACK_STATUS_VI[value] ?? value}</Pill>

export function Button({ variant = 'secondary', size = 'md', className = '', ...rest }: ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: 'primary' | 'secondary' | 'danger' | 'ghost'; size?: 'sm' | 'md'
}) {
  return <button type="button" className={`btn btn-${variant} btn-${size} ${className}`} {...rest} />
}

/* ------------------------------------------------------------------ async states */
export function Loading({ label }: { label?: string }) {
  const t = useT()
  return <div className="state" role="status"><span className="spinner" aria-hidden="true" />{label ?? t('common.loading')}</div>
}

export function ErrorState({ message, onRetry }: { message: string; onRetry?: () => void }) {
  const t = useT()
  return (
    <div className="state state-error" role="alert">
      <div><strong>{t('common.loadFailed')}</strong><span>{message}</span></div>
      {onRetry && <Button size="sm" onClick={onRetry}>{t('common.retry')}</Button>}
    </div>
  )
}

export function EmptyState({ title, hint }: { title: string; hint?: string }) {
  return (
    <div className="state state-empty">
      <span className="empty-icon" aria-hidden="true"><IconInbox /></span>
      <div><strong>{title}</strong>{hint && <span>{hint}</span>}</div>
    </div>
  )
}

/** Renders loading / error / empty / content of an ApiState; keeps the last data visible when a refresh fails. */
export function Async<T>({ state, isEmpty, empty, children }: {
  state: ApiState<T>
  isEmpty?: (data: T) => boolean
  empty: { title: string; hint?: string }
  children: (data: T) => ReactNode
}) {
  const t = useT()
  if (state.loading && state.data === null) return <Loading />
  if (state.error && state.data === null) return <ErrorState message={state.error} onRetry={state.reload} />
  if (state.data === null) return <Loading />
  if (isEmpty?.(state.data)) return <EmptyState {...empty} />
  return (
    <>
      {state.error && <ErrorState message={t('common.staleData', { error: state.error })} onRetry={state.reload} />}
      {children(state.data)}
    </>
  )
}

/* ------------------------------------------------------------------ task flow stepper */
const STEPS: MsgKey[] = ['common.stepAccepted', 'common.stepPick', 'common.stepTransport', 'common.stepDrop', 'common.stepDone']

export function FlowStepper({ task }: { task: TransportTask }) {
  const t = useT()
  const stage = taskStage(task)
  const failed = task.state === 'Failed'
  const cancelled = task.state === 'Cancelled'
  return (
    <ol className="stepper" aria-label={t('common.flowLabel')}>
      {STEPS.map((label, i) => {
        const done = i < stage || stage === 5
        const current = i === stage && stage < 5
        const cls = done ? 'done' : current ? (failed ? 'fail' : cancelled ? 'stop' : 'current') : ''
        return (
          <li key={label} className={cls}>
            <span className="step-dot">{done ? '✓' : failed && current ? '!' : i + 1}</span>
            <span className="step-label">{t(label)}</span>
          </li>
        )
      })}
    </ol>
  )
}
