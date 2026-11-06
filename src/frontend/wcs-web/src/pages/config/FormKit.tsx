import type { ReactNode } from 'react'
import type { LastTest } from '../../api/configTypes'
import { Pill } from '../../components/ui'
import { formatDateTime } from '../../i18n/format'
import { TEST_MODE_VI, TEST_OUTCOME_TONE, TEST_OUTCOME_VI } from '../../i18n/config'

export function Field({ label, hint, error, children, wide }: { label: string; hint?: string; error?: string; children: ReactNode; wide?: boolean }) {
  return (
    <label className={`field ${error ? 'has-error' : ''} ${wide ? 'wide' : ''}`}>
      <span className="field-label">{label}</span>
      {children}
      {error ? <span className="field-error" role="alert">{error}</span> : hint ? <span className="field-hint">{hint}</span> : null}
    </label>
  )
}

export function Switch({ checked, onChange, label }: { checked: boolean; onChange: (v: boolean) => void; label: string }) {
  return (
    <label className="switch">
      <input type="checkbox" role="switch" checked={checked} onChange={(e) => onChange(e.target.checked)} />
      <span className="switch-track" aria-hidden="true"><i /></span>
      <span>{label}</span>
    </label>
  )
}

/** Shows the persisted result of the last connectivity check of the SAVED configuration. */
export function TestResultPanel({ last, running }: { last: LastTest | null; running: boolean }) {
  if (running) return <div className="test-result"><span className="spinner" aria-hidden="true" />Đang kiểm tra…</div>
  if (!last) return <div className="test-result muted">Chưa kiểm tra lần nào với cấu hình hiện tại.</div>
  return (
    <div className={`test-result outcome-${last.outcome}`} aria-live="polite">
      <div className="test-head">
        <Pill tone={TEST_OUTCOME_TONE[last.outcome]}>{TEST_OUTCOME_VI[last.outcome] ?? last.outcome}</Pill>
        <span>{formatDateTime(last.testedAt)}</span>
      </div>
      <dl className="kv2">
        <dt>Cách kiểm tra</dt><dd>{TEST_MODE_VI[last.mode] ?? last.mode}</dd>
        <dt>Kết quả</dt><dd>{last.message}</dd>
        <dt>Độ trễ</dt><dd>{last.latencyMs === null ? '—' : `${last.latencyMs} ms`}</dd>
        <dt>Lỗi gần nhất</dt><dd className={last.error ? 'text-bad' : 'muted'}>{last.error ?? 'Không có'}</dd>
      </dl>
    </div>
  )
}
