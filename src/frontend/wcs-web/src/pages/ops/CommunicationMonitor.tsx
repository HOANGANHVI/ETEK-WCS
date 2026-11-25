import { useEffect, useState } from 'react'
import type { RcsMessageDetail, RcsMessageSummary } from '../../api/modeTypes'
import { Pill } from '../../components/ui'
import { useT, hasMessage, type MsgKey } from '../../i18n'
import { formatDateTime, formatTime } from '../../i18n/format'
import { describeError } from '../../i18n/vi'
import { copyText, readPayload, resultTone, timelineOrder } from '../../lib/modeConsole'
import { ModeBadge } from './ModePanel'

const resultLabel = (t: ReturnType<typeof useT>, r: string | null): string => (r === null ? '—' : hasMessage(`ops.res_${r}` as MsgKey) ? t(`ops.res_${r}` as MsgKey) : r)

/** One JSON value as a collapsible tree (keys and values exactly as stored). */
export function JsonTree({ value, name, open }: { value: unknown; name?: string; open: boolean }) {
  const label = name === undefined ? null : <span className="json-key">{name}: </span>
  if (value !== null && typeof value === 'object') {
    const entries = Array.isArray(value) ? value.map((v, i) => [String(i), v] as const) : Object.entries(value as Record<string, unknown>)
    return (
      <details open={open} className="json-node">
        <summary>{label}<span className="muted">{Array.isArray(value) ? `[${entries.length}]` : `{${entries.length}}`}</span></summary>
        <div className="json-children">{entries.map(([k, v]) => <JsonTree key={k} name={k} value={v} open={open} />)}</div>
      </details>
    )
  }
  return <div className="json-leaf">{label}<span className={typeof value === 'string' ? 'json-str' : 'json-val'}>{typeof value === 'string' ? JSON.stringify(value) : String(value)}</span></div>
}

interface Props {
  title?: string
  note?: string
  messages: RcsMessageSummary[] | null
  error: string | null
  loadDetail: (id: string) => Promise<RcsMessageDetail>
}

/**
 * CR-038 communication monitor of one task: what WCS sent to the RCS (OUT) and what came back (IN), in time order, each row marked SIMULATED or REAL. The detail is read from the
 * server (payload already masked there); nothing is guessed on this page, and a timeout is shown as unknown, never as a failure or a success.
 */
export function CommunicationMonitor({ title, note, messages, error, loadDetail }: Props) {
  const t = useT()
  const [selected, setSelected] = useState<string | null>(null)
  const [detail, setDetail] = useState<RcsMessageDetail | null>(null)
  const [detailError, setDetailError] = useState<string | null>(null)
  const [openAll, setOpenAll] = useState(true)
  const [copied, setCopied] = useState(false)
  const rows = messages ? timelineOrder(messages) : []

  useEffect(() => {
    if (!selected) return
    let off = false
    loadDetail(selected).then((d) => { if (!off) setDetail(d) }).catch((e: unknown) => { if (!off) setDetailError(describeError(e)) })
    return () => { off = true }
  }, [selected, loadDetail])

  const select = (id: string) => { setSelected(id); setDetail(null); setDetailError(null); setCopied(false) }
  const copy = async () => {
    if (!detail) return
    try { await navigator.clipboard.writeText(copyText(detail.payload)); setCopied(true) } catch { setCopied(false) }
  }
  const payload = detail ? readPayload(detail.payload) : null

  return (
    <section className="panel ops-comm" aria-label={title ?? t('ops.comTitle')}>
      <h2>{title ?? t('ops.comTitle')}</h2>
      {note && <p className="muted small">{note}</p>}
      {error && <div className="notice notice-bad" role="alert">{error}</div>}
      {messages !== null && rows.length === 0 && <p className="muted">{t('ops.comNone')}</p>}
      {rows.length > 0 && (
        <table className="data-table ops-timeline">
          <thead><tr><th>{t('ops.comColTime')}</th><th>{t('ops.comColMode')}</th><th>{t('ops.comColDir')}</th><th>{t('ops.comColType')}</th><th>{t('ops.comColResult')}</th></tr></thead>
          <tbody>
            {rows.map((m) => (
              <tr key={m.id} data-msg={m.id} data-direction={m.direction} data-mode={m.mode} className={m.id === selected ? 'selected' : ''} onClick={() => select(m.id)}>
                <td className="num">{formatTime(m.at)}</td>
                <td><ModeBadge mode={m.mode} /></td>
                <td><strong>{t(m.direction === 'Out' ? 'ops.comOut' : 'ops.comIn')}</strong></td>
                <td><button type="button" className="link" onClick={() => select(m.id)}>{m.messageType}</button></td>
                <td><Pill tone={resultTone(m.result)}>{resultLabel(t, m.result)}</Pill>{m.httpStatus ? <span className="muted small"> HTTP {m.httpStatus}</span> : null}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <h3>{t('ops.comDetail')}</h3>
      {!selected && <p className="muted">{t('ops.comPick')}</p>}
      {detailError && <div className="notice notice-bad" role="alert">{t('ops.comLoadFailed')} {detailError}</div>}
      {detail && (
        <div className="ops-comm-detail" data-detail={detail.id} data-detail-mode={detail.mode}>
          <div className={detail.mode === 'Real' ? 'notice notice-warn' : 'notice notice-info'} role="note">{t(detail.mode === 'Real' ? 'ops.comRealNote' : 'ops.comSimulatedNote')}</div>
          {(detail.result === 'Timeout' || detail.result === 'ConnectionError') && <div className="notice notice-warn" role="note">{t('ops.comUnknownNote')}</div>}
          {detail.result === 'NotSent' && <div className="notice notice-warn" role="note">{t('ops.comNotSentNote')}</div>}
          <dl className="kv2">
            <dt>{t('ops.comMode')}</dt><dd><ModeBadge mode={detail.mode} /></dd>
            <dt>{t('ops.comDirection')}</dt><dd>{detail.direction === 'Out' ? 'OUT (WCS → RCS)' : 'IN (RCS → WCS)'}</dd>
            <dt>{t('ops.comProtocol')}</dt><dd>{detail.protocol} · {detail.messageType}</dd>
            {detail.endpoint && (<><dt>{t('ops.comEndpoint')}</dt><dd className="mono">{detail.httpMethod} {detail.endpoint}</dd></>)}
            {detail.httpStatus !== null && (<><dt>{t('ops.comHttpStatus')}</dt><dd>{detail.httpStatus}</dd></>)}
            <dt>{t('ops.comRequestKey')}</dt><dd className="mono">{detail.requestKey ?? '—'}</dd>
            <dt>{t('ops.comRequestCode')}</dt><dd className="mono">{detail.requestCode ?? '—'}</dd>
            <dt>{t('ops.comRcsTask')}</dt><dd className="mono">{detail.rcsTaskCode ?? '—'}</dd>
            <dt>{t('ops.comResultRow')}</dt><dd><Pill tone={resultTone(detail.result)}>{resultLabel(t, detail.result)}</Pill></dd>
            {(detail.errorCode || detail.errorMessage) && (<><dt>{t('ops.comError')}</dt><dd>{detail.errorCode} {detail.errorMessage}</dd></>)}
            {detail.durationMs !== null && (<><dt>{t('ops.comDuration')}</dt><dd>{detail.durationMs} ms</dd></>)}
            <dt>{t('ops.comCorrelation')}</dt><dd className="mono">{detail.correlationId ?? '—'}</dd>
            <dt>{formatDateTime(detail.at)}</dt><dd />
          </dl>
          <h4>{t('ops.comPayload')}</h4>
          <p className="muted small">{t('ops.comMasked')}{detail.payloadTruncated ? ` ${t('ops.comTruncated')}` : ''}</p>
          {payload?.kind === 'none' && <p className="muted">{t('ops.comNoPayload')}</p>}
          {payload && payload.kind !== 'none' && (
            <>
              <div className="btn-row">
                <button type="button" onClick={() => void copy()}>{copied ? t('ops.comCopied') : t('ops.comCopy')}</button>
                {payload.kind === 'json' && <button type="button" onClick={() => setOpenAll(!openAll)}>{t(openAll ? 'ops.comCollapse' : 'ops.comExpand')}</button>}
              </div>
              <div className="ops-json" key={String(openAll)}>
                {payload.kind === 'json' ? <JsonTree value={payload.value} open={openAll} /> : <pre>{payload.text}</pre>}
              </div>
            </>
          )}
        </div>
      )}
    </section>
  )
}
