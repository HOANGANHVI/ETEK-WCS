import { useCallback, useState } from 'react'
import { api } from '../../api/client'
import { RCS_OUTCOMES, type RcsApiDef, type RcsExecution, type RcsHistoryItem, type RcsHistoryFilter } from '../../api/rcsDiagTypes'
import { Modal } from '../../components/Modal'
import { Async, Button, Panel } from '../../components/ui'
import { useApi } from '../../hooks/useApi'
import { useT, type MsgKey } from '../../i18n'
import { formatDateTime } from '../../i18n/format'
import { describeError } from '../../i18n/vi'
import { Inspector, OutcomePill } from './Inspector'

const PAGE = 25
const toIso = (local: string) => (local ? new Date(local).toISOString() : undefined)

/** A history row shown in the Inspector. The history keeps the masked request and response, not the HTTP headers. */
const asExecution = (h: RcsHistoryItem): RcsExecution => ({
  id: h.id, executedAt: h.executedAt, api: h.api, group: h.group, user: h.user, requestId: h.requestId, method: 'POST', endpoint: h.endpoint, requestHeaders: {}, requestBody: h.requestBody,
  httpStatus: h.httpStatus, responseHeaders: {}, responseBody: h.responseBody, responseTruncated: h.responseTruncated, rcsCode: h.rcsCode, rcsMessage: h.rcsMessage, durationMs: h.durationMs,
  outcome: h.outcome, success: h.success, errorMessage: h.errorMessage, warnings: [],
})

/** CR-032 – Execution History: every manual call (who, when, which API, request id, masked request / response, HTTP status, RCS code, duration, result) with filters and an export for the RCS engineer. */
export function History({ apis, refreshKey }: { apis: RcsApiDef[]; refreshKey: number }) {
  const t = useT()
  const [apiName, setApiName] = useState('')
  const [user, setUser] = useState('')
  const [status, setStatus] = useState('')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [applied, setApplied] = useState<RcsHistoryFilter>({})
  const [skip, setSkip] = useState(0)
  const [open, setOpen] = useState<RcsHistoryItem | null>(null)
  const [exportError, setExportError] = useState<string | null>(null)

  const list = useApi(useCallback(() => api.rcsDiagHistory({ ...applied, skip, take: PAGE }) as Promise<{ total: number; items: RcsHistoryItem[] }>, [applied, skip, refreshKey]))   // eslint-disable-line react-hooks/exhaustive-deps

  const apply = () => { setApplied({ api: apiName || undefined, user: user.trim() || undefined, status: status || undefined, from: toIso(from), to: toIso(to) }); setSkip(0) }
  const clear = () => { setApiName(''); setUser(''); setStatus(''); setFrom(''); setTo(''); setApplied({}); setSkip(0) }
  const doExport = async () => {
    setExportError(null)
    try { await api.rcsDiagExport(applied) } catch (e) { setExportError(describeError(e)) }
  }

  return (
    <>
      <Panel title={t('rcsDiag.filters')}>
        <form className="form-grid" onSubmit={(e) => { e.preventDefault(); apply() }}>
          <label className="field"><span className="field-label">{t('rcsDiag.hApi')}</span>
            <select value={apiName} onChange={(e) => setApiName(e.target.value)}><option value="">{t('rcsDiag.hAll')}</option>{apis.map((a) => <option key={a.name} value={a.name}>{a.name}</option>)}</select></label>
          <label className="field"><span className="field-label">{t('rcsDiag.hUser')}</span><input value={user} onChange={(e) => setUser(e.target.value)} /></label>
          <label className="field"><span className="field-label">{t('rcsDiag.hStatus')}</span>
            <select value={status} onChange={(e) => setStatus(e.target.value)}>
              <option value="">{t('rcsDiag.hAll')}</option><option value="success">{t('rcsDiag.hSuccess')}</option><option value="failure">{t('rcsDiag.hFailure')}</option>
              {RCS_OUTCOMES.filter((o) => o !== 'API_SUCCESS').map((o) => <option key={o} value={o}>{t(`rcsDiag.outcome_${o}` as MsgKey)}</option>)}
            </select></label>
          <label className="field"><span className="field-label">{t('rcsDiag.hFrom')}</span><input type="datetime-local" value={from} onChange={(e) => setFrom(e.target.value)} /></label>
          <label className="field"><span className="field-label">{t('rcsDiag.hTo')}</span><input type="datetime-local" value={to} onChange={(e) => setTo(e.target.value)} /></label>
          <div className="rd-toolbar">
            <Button variant="primary" type="submit">{t('rcsDiag.hApply')}</Button>
            <Button onClick={clear}>{t('rcsDiag.hClear')}</Button>
            <Button onClick={() => list.reload()}>{t('rcsDiag.hRefresh')}</Button>
            <Button onClick={() => void doExport()}>{t('rcsDiag.hExport')}</Button>
          </div>
        </form>
        {exportError && <div className="notice notice-bad" role="alert">{exportError}</div>}
      </Panel>

      <Panel title={t('rcsDiag.historyTitle')} subtitle={t('rcsDiag.historySub')} flush>
        <Async state={list} empty={{ title: t('rcsDiag.historyEmpty'), hint: t('rcsDiag.historyEmptyHint') }} isEmpty={(p) => p.items.length === 0}>
          {(p) => (
            <>
              <div className="table-wrap">
                <table className="grid clickable">
                  <thead><tr><th>{t('rcsDiag.hcTime')}</th><th>{t('rcsDiag.hcUser')}</th><th>{t('rcsDiag.hcApi')}</th><th>{t('rcsDiag.hcRequestId')}</th><th>{t('rcsDiag.hcHttp')}</th><th>{t('rcsDiag.hcRcs')}</th><th>{t('rcsDiag.hcMs')}</th><th>{t('rcsDiag.hcResult')}</th></tr></thead>
                  <tbody>
                    {p.items.map((h) => (
                      <tr key={h.id} onClick={() => setOpen(h)} tabIndex={0} onKeyDown={(e) => { if (e.key === 'Enter') setOpen(h) }}>
                        <td className="nowrap num">{formatDateTime(h.executedAt)}</td><td>{h.user}</td><td><strong>{h.api}</strong></td>
                        <td className="mono rd-trunc" title={h.requestId}>{h.requestId}</td><td className="num">{h.httpStatus ?? '—'}</td><td className="num">{h.rcsCode ?? '—'}</td><td className="num">{h.durationMs}</td>
                        <td><OutcomePill outcome={h.outcome} /></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <div className="rd-pager">
                <span className="muted small">{t('rcsDiag.hRange', { from: p.total === 0 ? 0 : skip + 1, to: Math.min(skip + PAGE, p.total), total: p.total })}</span>
                <Button size="sm" disabled={skip === 0} onClick={() => setSkip(Math.max(0, skip - PAGE))}>{t('rcsDiag.hPrev')}</Button>
                <Button size="sm" disabled={skip + PAGE >= p.total} onClick={() => setSkip(skip + PAGE)}>{t('rcsDiag.hNext')}</Button>
              </div>
            </>
          )}
        </Async>
      </Panel>

      {open && (
        <Modal title={`${open.api} · ${formatDateTime(open.executedAt)}`} onClose={() => setOpen(null)} footer={<Button onClick={() => setOpen(null)}>{t('common.close')}</Button>}>
          <Inspector exec={asExecution(open)} />
        </Modal>
      )}
    </>
  )
}
