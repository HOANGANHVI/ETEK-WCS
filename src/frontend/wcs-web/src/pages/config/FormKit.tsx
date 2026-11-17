import type { ReactNode } from 'react'
import type { LastTest } from '../../api/configTypes'
import { Pill } from '../../components/ui'
import { formatDateTime } from '../../i18n/format'
import { useT } from '../../i18n'
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
  const t = useT()
  if (running) return <div className="test-result"><span className="spinner" aria-hidden="true" />{t('config.testRunning')}</div>
  if (!last) return <div className="test-result muted">{t('config.testNever')}</div>
  return (
    <div className={`test-result outcome-${last.outcome}`} aria-live="polite">
      <div className="test-head">
        <Pill tone={TEST_OUTCOME_TONE[last.outcome]}>{TEST_OUTCOME_VI[last.outcome] ?? last.outcome}</Pill>
        <span>{formatDateTime(last.testedAt)}</span>
      </div>
      <dl className="kv2">
        <dt>{t('config.testMethod')}</dt><dd>{TEST_MODE_VI[last.mode] ?? last.mode}</dd>
        <dt>{t('config.testResult')}</dt><dd>{last.message}</dd>
        <dt>{t('config.testLatency')}</dt><dd>{last.latencyMs === null ? '—' : `${last.latencyMs} ms`}</dd>
        <dt>{t('config.testLastError')}</dt><dd className={last.error ? 'text-bad' : 'muted'}>{last.error ?? t('config.testNoError')}</dd>
      </dl>
    </div>
  )
}
