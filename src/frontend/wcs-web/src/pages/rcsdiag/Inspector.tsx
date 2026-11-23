import { useState } from 'react'
import type { RcsExecution, RcsOutcome } from '../../api/rcsDiagTypes'
import { Button, Pill } from '../../components/ui'
import { useT, type MsgKey } from '../../i18n'
import { formatDateTime } from '../../i18n/format'
import { copyText, supportText } from './formUtil'
import { OUTCOME_TONE } from './tones'

export const OutcomePill = ({ outcome }: { outcome: RcsOutcome }) => {
  const t = useT()
  return <Pill tone={OUTCOME_TONE[outcome] ?? 'muted'}>{t(`rcsDiag.outcome_${outcome}` as MsgKey)}</Pill>
}

const Block = ({ title, text }: { title: string; text: string | null }) => (
  <div className="rd-block">
    <h4>{title}</h4>
    <pre className="rd-code" tabIndex={0}>{text && text.length > 0 ? text : '—'}</pre>
  </div>
)

const Headers = ({ title, items }: { title: string; items: Record<string, string> }) => {
  const rows = Object.entries(items)
  return (
    <div className="rd-block">
      <h4>{title}</h4>
      <pre className="rd-code" tabIndex={0}>{rows.length ? rows.map(([k, v]) => `${k}: ${v}`).join('\n') : '—'}</pre>
    </div>
  )
}

/**
 * The Request / Response Inspector: what was sent (credentials are masked by the server before anything reaches the browser), what came back, and WHY it counts as success or failure.
 * HTTP 200 is never shown as success on its own: the outcome class is decided by the server from the HTTP status, the JSON and the RCS "code".
 */
export function Inspector({ exec }: { exec: RcsExecution }) {
  const t = useT()
  const [copied, setCopied] = useState<'ok' | 'fail' | null>(null)
  const copy = async () => {
    const ok = await copyText(supportText({
      api: exec.api, endpoint: exec.endpoint, at: exec.executedAt, user: exec.user, requestId: exec.requestId, method: exec.method, requestBody: exec.requestBody, httpStatus: exec.httpStatus,
      rcsCode: exec.rcsCode, rcsMessage: exec.rcsMessage, durationMs: exec.durationMs, outcome: exec.outcome, responseBody: exec.responseBody, errorMessage: exec.errorMessage,
    }))
    setCopied(ok ? 'ok' : 'fail')
    setTimeout(() => setCopied(null), 3000)
  }
  return (
    <div className="rd-inspector">
      <div className="rd-result-head">
        <OutcomePill outcome={exec.outcome} />
        <span className="muted small">{t(`rcsDiag.outcomeHint_${exec.outcome}` as MsgKey)}</span>
        <span className="rd-spacer" />
        <Button size="sm" onClick={() => void copy()}>{t('rcsDiag.copyForEngineer')}</Button>
        {copied && <span className={copied === 'ok' ? 'text-ok small' : 'text-bad small'} role="status">{copied === 'ok' ? t('rcsDiag.copied') : t('rcsDiag.copyFailed')}</span>}
      </div>
      {exec.errorMessage && <div className="notice notice-warn" role="alert">{exec.errorMessage}</div>}
      <dl className="kv2 rd-facts">
        <dt>{t('rcsDiag.fApi')}</dt><dd><strong>{exec.api}</strong></dd>
        <dt>{t('rcsDiag.fMethodEndpoint')}</dt><dd className="mono">{exec.method} {exec.endpoint ?? '—'}</dd>
        <dt>{t('rcsDiag.fHttp')}</dt><dd className="mono">{exec.httpStatus ?? '—'}</dd>
        <dt>{t('rcsDiag.fRcsCode')}</dt><dd className="mono">{exec.rcsCode ?? '—'}</dd>
        <dt>{t('rcsDiag.fRcsMessage')}</dt><dd>{exec.rcsMessage ?? '—'}</dd>
        <dt>{t('rcsDiag.fDuration')}</dt><dd className="mono">{exec.durationMs} ms</dd>
        <dt>{t('rcsDiag.fRequestId')}</dt><dd className="mono">{exec.requestId}</dd>
        <dt>{t('rcsDiag.fUserTime')}</dt><dd>{exec.user} · {formatDateTime(exec.executedAt)}</dd>
      </dl>
      {exec.warnings.length > 0 && (
        <ul className="rd-warnings">{exec.warnings.map((w) => <li key={w.path + w.code}><Pill tone="warn">{w.path}</Pill> {w.message}</li>)}</ul>
      )}
      <div className="rd-two">
        <div>
          <Headers title={t('rcsDiag.reqHeaders')} items={exec.requestHeaders} />
          <Block title={t('rcsDiag.reqBody')} text={exec.requestBody} />
        </div>
        <div>
          <Headers title={t('rcsDiag.resHeaders')} items={exec.responseHeaders} />
          <Block title={t('rcsDiag.resBody')} text={exec.responseBody} />
          {exec.responseTruncated && <p className="muted small">{t('rcsDiag.truncated')}</p>}
        </div>
      </div>
      <p className="muted small">{t('rcsDiag.maskedNote')}</p>
    </div>
  )
}
