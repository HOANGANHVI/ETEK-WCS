import type { ReactNode } from 'react'
import type { ApiState } from '../hooks/useApi'

export function Loading({ label = 'Loading…' }: { label?: string }) {
  return <div className="state state-loading" role="status"><span className="spinner" aria-hidden="true" />{label}</div>
}

export function ErrorState({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <div className="state state-error" role="alert">
      <strong>Something went wrong</strong>
      <span>{message}</span>
      {onRetry && <button type="button" onClick={onRetry}>Retry</button>}
    </div>
  )
}

export function EmptyState({ title, hint }: { title: string; hint?: string }) {
  return (
    <div className="state state-empty">
      <strong>{title}</strong>
      {hint && <span>{hint}</span>}
    </div>
  )
}

/** Renders loading / error / empty / content for an ApiState. */
export function Async<T>({
  state, isEmpty, empty, children,
}: {
  state: ApiState<T>
  isEmpty?: (data: T) => boolean
  empty: { title: string; hint?: string }
  children: (data: T) => ReactNode
}) {
  if (state.loading && state.data === null) return <Loading />
  if (state.error && state.data === null) return <ErrorState message={state.error} onRetry={state.reload} />
  if (state.data === null) return <Loading />
  if (isEmpty?.(state.data)) return <EmptyState {...empty} />
  return (
    <>
      {state.error && <ErrorState message={`Showing last known data. ${state.error}`} onRetry={state.reload} />}
      {children(state.data)}
    </>
  )
}

const TONES: Record<string, string> = {
  Completed: 'ok', Online: 'ok', Healthy: 'ok', Empty: 'ok', Connected: 'ok',
  Failed: 'bad', Error: 'bad', Offline: 'bad', Disconnected: 'bad', Unknown: 'warn',
  Cancelled: 'muted', Degraded: 'warn',
  WaitingForDestination: 'warn', WaitingForPlc: 'warn', CancelPending: 'warn', Reserved: 'warn', Queued: 'muted', Pending: 'muted',
}

export function Badge({ value }: { value: string }) {
  return <span className={`badge badge-${TONES[value] ?? 'info'}`}>{value}</span>
}

export function Card({ title, actions, children }: { title: string; actions?: ReactNode; children: ReactNode }) {
  return (
    <section className="card">
      <header className="card-header"><h2>{title}</h2>{actions}</header>
      {children}
    </section>
  )
}
