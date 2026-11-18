import { useCallback, useEffect, useMemo, useState } from 'react'
import { api } from '../api/client'
import { LOG_CATEGORIES, LOG_LEVELS, type LogCategory, type LogDetail, type LogEntry, type LogLevel } from '../api/logTypes'
import { Modal } from '../components/Modal'
import { Async, Button, EmptyState, Kpi, PageHeader, Panel, Pill } from '../components/ui'
import { DEMO_MODE } from '../config'
import { useApi } from '../hooks/useApi'
import { formatDateTime, formatNumber } from '../i18n/format'
import { localizeLog } from '../i18n/logCatalog'
import { useT, type MsgKey } from '../i18n'
import { LOG_CATEGORY_HINT, LOG_CATEGORY_VI, LOG_LEVEL_TONE, LOG_LEVEL_VI } from '../i18n/logs'
import { AUDIT_RESULT_VI, AUDIT_TARGET_VI, describeError } from '../i18n/vi'

const PAGE_SIZES = [25, 50, 100, 200]
const RANGES: { key: '1h' | '24h' | '7d' | '30d' | 'custom'; label: MsgKey; hours: number }[] = [
  { key: '1h', label: 'logs.range1h', hours: 1 }, { key: '24h', label: 'logs.range24h', hours: 24 },
  { key: '7d', label: 'logs.range7d', hours: 24 * 7 }, { key: '30d', label: 'logs.range30d', hours: 24 * 30 }, { key: 'custom', label: 'logs.rangeCustom', hours: 0 },
]

const LevelPill = ({ level }: { level: LogLevel }) => <Pill tone={LOG_LEVEL_TONE[level] ?? 'muted'}>{LOG_LEVEL_VI[level] ?? level}</Pill>
const toIso = (local: string) => (local ? new Date(local).toISOString() : undefined)

/**
 * CR-007: Logging & Diagnostics - one screen for SYSTEM, PLC, DATABASE, DEPLOYMENT, RCS/AGV and SECURITY/AUDIT events (Admin only; the API enforces it as well).
 * Everything shown comes from the backend (no sample data): an empty database shows an empty list, an unreachable backend shows an error.
 */
export function LogsPage() {
  const t = useT()
  if (DEMO_MODE) {
    return (
      <>
        <PageHeader title={t('logs.title')} />
        <EmptyState title={t('logs.noData')} hint={t('logs.noDataHint')} />
      </>
    )
  }
  return <LogsScreen />
}

function LogsScreen() {
  const t = useT()
  const [range, setRange] = useState<(typeof RANGES)[number]['key']>('24h')
  const [customFrom, setCustomFrom] = useState('')
  const [customTo, setCustomTo] = useState('')
  const [category, setCategory] = useState<LogCategory | ''>('')
  const [level, setLevel] = useState<LogLevel | ''>('')
  const [device, setDevice] = useState('')
  const [source, setSource] = useState('')
  const [eventCode, setEventCode] = useState('')
  const [keyword, setKeyword] = useState('')
  const [applied, setApplied] = useState({ device: '', source: '', eventCode: '', keyword: '' })   // text filters apply on Enter / "Tìm"
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(50)
  const [now, setNow] = useState(() => Date.now())
  const [openId, setOpenId] = useState<string | null>(null)

  // The window is computed when the user changes the range (or presses refresh), not on every render, so the request key stays stable.
  const window = useMemo(() => {
    if (range === 'custom') return { from: toIso(customFrom), to: toIso(customTo) }
    const hours = RANGES.find((r) => r.key === range)?.hours ?? 24
    return { from: new Date(now - hours * 3600_000).toISOString(), to: new Date(now).toISOString() }
  }, [range, customFrom, customTo, now])

  const stats = useApi(useCallback(() => api.logsStats(window.from, window.to), [window]))
  const list = useApi(useCallback(
    () => api.logsSearch({ from: window.from, to: window.to, category: category || undefined, level: level || undefined, device: applied.device.trim() || undefined,
      source: applied.source.trim() || undefined, eventCode: applied.eventCode.trim() || undefined, keyword: applied.keyword.trim() || undefined, page, pageSize }),
    [window, category, level, applied, page, pageSize]))

  const refresh = () => { setNow(Date.now()); stats.reload(); list.reload() }
  const apply = () => { setApplied({ device, source, eventCode, keyword }); setPage(1) }
  const clear = () => { setCategory(''); setLevel(''); setDevice(''); setSource(''); setEventCode(''); setKeyword(''); setApplied({ device: '', source: '', eventCode: '', keyword: '' }); setPage(1) }

  const s = stats.data
  const totalPages = list.data ? Math.max(1, Math.ceil(list.data.total / list.data.pageSize)) : 1

  return (
    <>
      <PageHeader title={t('logs.title')} actions={<Button onClick={refresh}>{t('logs.refresh')}</Button>} />

      {/* ------------------------------------------------------------------ dashboard */}
      <div className="kpi-row logs-kpis" aria-label={t('logs.statsLabel')}>
        <Kpi label={t('logs.kpiTotal')} value={s ? formatNumber(s.total) : '—'} hint={t(RANGES.find((r) => r.key === range)?.label ?? 'logs.range24h')} tone="info" />
        <Kpi label={t('logs.kpiInfo')} value={s ? s.byLevel.Information : '—'} tone="info" />
        <Kpi label={t('logs.kpiWarn')} value={s ? s.byLevel.Warning : '—'} tone="warn" />
        <Kpi label={t('logs.kpiError')} value={s ? s.byLevel.Error : '—'} tone="bad" />
        <Kpi label={t('logs.kpiCritical')} value={s ? s.byLevel.Critical : '—'} tone="bad" />
      </div>
      {stats.error && !s && <div className="notice notice-bad" role="alert">{t('logs.statsFailed', { error: stats.error })}</div>}

      <div className="logs-dash">
        <Panel title={t('logs.byGroup')}>
          <div className="log-groups">
            {LOG_CATEGORIES.map((c) => (
              <button key={c} type="button" className={`log-group ${category === c ? 'active' : ''}`} aria-pressed={category === c} onClick={() => { setCategory(category === c ? '' : c); setPage(1) }}>
                <strong>{LOG_CATEGORY_VI[c]}</strong><span className="log-group-count num">{s ? s.byCategory[c] : '—'}</span>
                <small className="muted">{LOG_CATEGORY_HINT[c]}</small>
              </button>
            ))}
          </div>
        </Panel>
        <Panel title={t('logs.latestErrors')}>
          {!s ? <p className="muted pad">{t('logs.loading')}</p> : s.latestErrors.length === 0 ? <p className="muted pad">{t('logs.noErrors')}</p> : (
            <ul className="log-latest">
              {s.latestErrors.map((e) => (
                <li key={e.id}><button type="button" onClick={() => setOpenId(e.id)}>
                  <LevelPill level={e.level} /> <span className="nowrap num muted">{formatDateTime(e.occurredAt)}</span> <small className="muted">{LOG_CATEGORY_VI[e.category]}{e.device ? ` · ${e.device}` : ''}</small>
                  <span className="log-latest-msg" title={e.message}>{localizeLog(e).text}</span>
                </button></li>
              ))}
            </ul>
          )}
        </Panel>
        <Panel title={t('logs.problemDevices')}>
          {!s ? <p className="muted pad">{t('logs.loading')}</p> : s.problemDevices.length === 0 ? <p className="muted pad">{t('logs.noProblemDevices')}</p> : (
            <div className="table-wrap"><table className="grid clickable">
              <thead><tr><th>{t('logs.colDevice')}</th><th>{t('logs.colGroup')}</th><th>{t('logs.colErrors')}</th><th>{t('logs.colWarnings')}</th><th>{t('logs.colLatest')}</th></tr></thead>
              <tbody>
                {s.problemDevices.map((d) => (
                  <tr key={d.device + d.category} onClick={() => { setDevice(d.device); setApplied((a) => ({ ...a, device: d.device })); setCategory(d.category); setPage(1) }}>
                    <td><strong>{d.device}</strong></td><td>{LOG_CATEGORY_VI[d.category]}</td>
                    <td className={d.errors ? 'text-bad num' : 'num'}>{d.errors}</td><td className="num">{d.warnings}</td><td className="nowrap num">{formatDateTime(d.lastAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table></div>
          )}
        </Panel>
      </div>

      {/* ------------------------------------------------------------------ filters */}
      <Panel title={t('logs.filters')}>
        <form className="form-grid logs-filters" onSubmit={(e) => { e.preventDefault(); apply() }}>
          <label className="field"><span className="field-label">{t('logs.fRange')}</span>
            <select value={range} onChange={(e) => { setNow(Date.now()); setRange(e.target.value as typeof range); setPage(1) }}>{RANGES.map((r) => <option key={r.key} value={r.key}>{t(r.label)}</option>)}</select></label>
          {range === 'custom' && <>
            <label className="field"><span className="field-label">{t('logs.fFrom')}</span><input type="datetime-local" value={customFrom} onChange={(e) => { setCustomFrom(e.target.value); setPage(1) }} /></label>
            <label className="field"><span className="field-label">{t('logs.fTo')}</span><input type="datetime-local" value={customTo} onChange={(e) => { setCustomTo(e.target.value); setPage(1) }} /></label>
          </>}
          <label className="field"><span className="field-label">{t('logs.fGroup')}</span>
            <select value={category} onChange={(e) => { setCategory(e.target.value as LogCategory | ''); setPage(1) }}><option value="">{t('logs.fAll')}</option>{LOG_CATEGORIES.map((c) => <option key={c} value={c}>{LOG_CATEGORY_VI[c]}</option>)}</select></label>
          <label className="field"><span className="field-label">{t('logs.fLevel')}</span>
            <select value={level} onChange={(e) => { setLevel(e.target.value as LogLevel | ''); setPage(1) }}><option value="">{t('logs.fAll')}</option>{LOG_LEVELS.map((l) => <option key={l} value={l}>{LOG_LEVEL_VI[l]}</option>)}</select></label>
          <label className="field"><span className="field-label">{t('logs.fDevice')}</span>
            <input list="log-devices" value={device} onChange={(e) => setDevice(e.target.value)} placeholder={t('logs.fDevicePh')} />
            <datalist id="log-devices">{(s?.devices ?? []).map((d) => <option key={d} value={d} />)}</datalist></label>
          <label className="field"><span className="field-label">{t('logs.fSource')}</span><input value={source} onChange={(e) => setSource(e.target.value)} placeholder={t('logs.fSourcePh')} /></label>
          <label className="field"><span className="field-label">{t('logs.fEvent')}</span><input value={eventCode} onChange={(e) => setEventCode(e.target.value)} placeholder={t('logs.fEventPh')} /></label>
          <label className="field"><span className="field-label">{t('logs.fKeyword')}</span><input value={keyword} onChange={(e) => setKeyword(e.target.value)} placeholder={t('logs.fKeywordPh')} /></label>
        </form>
        <div className="btn-row">
          <Button variant="primary" onClick={apply}>{t('logs.search')}</Button>
          <Button onClick={clear}>{t('logs.clear')}</Button>
        </div>
      </Panel>

      {/* ------------------------------------------------------------------ table */}
      <Panel title={t('logs.tablePanel')} subtitle={list.data ? t('logs.tableSub', { n: formatNumber(list.data.total) }) : undefined} flush
        actions={<label className="inline-field">{t('logs.rowsPerPage')} <select aria-label={t('logs.rowsPerPageAria')} value={pageSize} onChange={(e) => { setPageSize(Number(e.target.value)); setPage(1) }}>{PAGE_SIZES.map((n) => <option key={n} value={n}>{n}</option>)}</select></label>}>
        <Async state={list} isEmpty={(d) => d.items.length === 0} empty={{ title: t('logs.tableNone'), hint: t('logs.tableNoneHint') }}>
          {(data) => (
            <>
              <div className="table-wrap">
                <table className="grid clickable log-table" aria-label={t('logs.title')}>
                  <thead><tr><th>{t('logs.colTime')}</th><th>{t('logs.colLevel')}</th><th>{t('logs.colGroup')}</th><th>{t('logs.colSource')}</th><th>{t('logs.colDevice')}</th><th>{t('logs.colEvent')}</th><th>{t('logs.colMessage')}</th></tr></thead>
                  <tbody>
                    {data.items.map((e: LogEntry) => (
                      <tr key={e.id} className={`log-row log-${e.level.toLowerCase()} ${openId === e.id ? 'selected' : ''}`} tabIndex={0} onClick={() => setOpenId(e.id)}
                        onKeyDown={(k) => { if (k.key === 'Enter') setOpenId(e.id) }}>
                        <td className="nowrap num">{formatDateTime(e.occurredAt)}</td>
                        <td><LevelPill level={e.level} /></td>
                        <td className="nowrap">{LOG_CATEGORY_VI[e.category]}</td>
                        <td className="small" title={e.source}>{e.source.split('.').slice(-1)[0]}</td>
                        <td className="nowrap">{e.device ?? <span className="muted">—</span>}</td>
                        <td className="mono nowrap">{e.event ?? <span className="muted">—</span>}</td>
                        <td className="log-msg" title={e.message}>{localizeLog(e).text}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <div className="pad pager">
                <Button size="sm" disabled={page <= 1} onClick={() => setPage(page - 1)}>{t('logs.prev')}</Button>
                <span className="muted">{t('logs.page', { page: data.page, total: totalPages })}</span>
                <Button size="sm" disabled={page >= totalPages} onClick={() => setPage(page + 1)}>{t('logs.next')}</Button>
              </div>
            </>
          )}
        </Async>
      </Panel>

      {openId && <LogDetailDialog id={openId} onClose={() => setOpenId(null)} />}
    </>
  )
}

function pretty(json: string | null) {
  if (!json) return null
  try { return JSON.stringify(JSON.parse(json), null, 2) } catch { return json }
}

function LogDetailDialog({ id, onClose }: { id: string; onClose: () => void }) {
  const t = useT()
  const [detail, setDetail] = useState<LogDetail | null>(null)
  const [error, setError] = useState<string | null>(null)
  useEffect(() => {
    let cancelled = false
    api.logDetail(id).then((d) => { if (!cancelled) setDetail(d) }).catch((e: unknown) => { if (!cancelled) setError(describeError(e)) })
    return () => { cancelled = true }
  }, [id])

  const rows: [string, string | number | null | undefined][] = detail ? [
    [t('logs.dTime'), formatDateTime(detail.occurredAt)], [t('logs.dCategory'), LOG_CATEGORY_VI[detail.category]], [t('logs.dLevel'), LOG_LEVEL_VI[detail.level]],
    [t('logs.dModule'), detail.source], [t('logs.dDeviceId'), detail.device], [t('logs.dStation'), detail.station], [t('logs.dEventCode'), detail.event],
    [t('logs.dCorrelation'), detail.correlationId], [t('logs.dHost'), detail.host],
    [t('logs.dIp'), detail.ip], [t('logs.dPort'), detail.port],
    [t('logs.dPrevState'), detail.previousState], [t('logs.dNewState'), detail.newState], [t('logs.dFailureKind'), detail.failureKind], [t('logs.dEndCode'), detail.endCode], [t('logs.dReason'), detail.reason],
    [t('logs.dActor'), detail.actor ? `${detail.actor}${detail.actorRole ? ` (${detail.actorRole})` : ''}` : null], [t('logs.dResult'), detail.result ? (AUDIT_RESULT_VI[detail.result] ?? detail.result) : null],
    [t('logs.dTarget'), [detail.targetType ? (AUDIT_TARGET_VI[detail.targetType] ?? detail.targetType) : null, detail.targetId].filter(Boolean).join(' ') || null],
  ] : []

  const shown = detail ? localizeLog(detail) : null
  return (
    <Modal title={t('logs.detailTitle')} onClose={onClose} footer={<Button onClick={onClose}>{t('common.close')}</Button>}>
      {error && <div className="notice notice-bad" role="alert">{error}</div>}
      {!detail && !error && <p className="muted">{t('logs.loading')}</p>}
      {detail && (
        <div className="log-detail">
          <div className="log-detail-head"><LevelPill level={detail.level} /> <strong>{LOG_CATEGORY_VI[detail.category]}</strong></div>
          {shown?.translated ? <p className="log-detail-msg">{shown.text}</p> : <p className="muted small">{t('logs.notTranslated')}</p>}
          <details className="log-original" open={!shown?.translated}>
            <summary>{t('logs.viewOriginal')}</summary>
            <p className="muted small"><strong>{t('logs.rawMessage')}:</strong> <span className="mono">{detail.message}</span></p>
          </details>
          <dl className="kv2">
            {rows.filter(([, v]) => v !== null && v !== undefined && v !== '').map(([k, v]) => <><dt key={k + 'k'}>{k}</dt><dd key={k + 'v'}>{v}</dd></>)}
          </dl>
          {detail.exception && <><strong>{t('logs.exceptionTrace')}</strong><pre className="log-pre">{detail.exception}</pre></>}
          {detail.beforeJson && <><strong>{t('logs.before')}</strong><pre className="log-pre">{pretty(detail.beforeJson)}</pre></>}
          {detail.afterJson && <><strong>{t('logs.after')}</strong><pre className="log-pre">{pretty(detail.afterJson)}</pre></>}
          {detail.detailsJson && <><strong>{t('logs.dProperties')}</strong><pre className="log-pre">{pretty(detail.detailsJson)}</pre></>}
          <p className="muted small">{t('logs.hidden')}</p>
        </div>
      )}
    </Modal>
  )
}
