import { useCallback, useEffect, useState } from 'react'
import { api } from '../../api/client'
import type { DiagExportBody, DiagExportJob } from '../../api/diagTypes'
import type { PlcConnection } from '../../api/plcTypes'
import { Async, Button, Panel, Pill } from '../../components/ui'
import { useApi } from '../../hooks/useApi'
import { useT } from '../../i18n'
import { formatDateTime } from '../../i18n/format'
import { DIAG_MODULE_VI, DIAG_SOURCE_TONE, DIAG_SOURCE_VI, DIAG_STATE_TONE, DIAG_STATE_VI } from '../../i18n/plc'
import { ApiRequestError } from '../../api/errors'
import { describeError } from '../../i18n/vi'

type Mode = '1h' | '24h' | 'all' | 'custom'
const POLL_MS = 1500
const terminal = (s: string) => s === 'Completed' || s === 'Failed' || s === 'Expired'
const mb = (n: number) => (n / 1024 / 1024).toFixed(n > 10 * 1024 * 1024 ? 0 : 1)

/** CR-021: whole-system diagnostic export. The page only creates the job, shows what the server reports (per-source status, limits, notes) and offers the download; it never reads log data itself. */
export function SystemExportPanel({ plcs }: { plcs: PlcConnection[] }) {
  const t = useT()
  const options = useApi(useCallback(() => api.diagExportOptions(), []))
  const history = useApi(useCallback(() => api.diagListExports(), []))
  const [mode, setMode] = useState<Mode>('24h')
  const [from, setFrom] = useState(''); const [to, setTo] = useState('')
  const [scopeKind, setScopeKind] = useState<'all' | 'modules'>('all')
  const [modules, setModules] = useState<string[]>([])
  const [minLevel, setMinLevel] = useState(''); const [plcId, setPlcId] = useState('')
  const [taskId, setTaskId] = useState(''); const [correlationId, setCorrelationId] = useState('')
  const [job, setJob] = useState<DiagExportJob | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const tz = Intl.DateTimeFormat().resolvedOptions().timeZone
  const { reload } = history

  const jobId = job?.id; const running = job !== null && !terminal(job.state)
  useEffect(() => {
    if (!jobId || !running) return
    const id = setInterval(() => {
      api.diagGetExport(jobId).then((j) => { setJob(j); if (terminal(j.state)) reload() }).catch((e: unknown) => setError(describeError(e)))
    }, POLL_MS)
    return () => clearInterval(id)
  }, [jobId, running, reload])

  const start = async () => {
    setError(null)
    if (mode === 'custom' && (!from || !to || new Date(to) <= new Date(from))) { setError(t('diagnostics.rangeInvalid')); return }
    const body: DiagExportBody = { mode, modules: scopeKind === 'modules' ? modules : [], minLevel: minLevel || undefined, plcId: plcId || undefined, taskId: taskId.trim() || undefined, correlationId: correlationId.trim() || undefined }
    if (mode === 'custom') { body.from = new Date(from).toISOString(); body.to = new Date(to).toISOString() }
    setBusy(true)
    try { setJob(await api.diagStartExport(body)); reload() } catch (e) { setError(describeError(e)) } finally { setBusy(false) }
  }
  const download = async (id: string) => { setError(null); try { await api.downloadDiagExport(id) } catch (e) { setError(describeError(e)) } }
  const toggle = (m: string) => setModules((cur) => (cur.includes(m) ? cur.filter((x) => x !== m) : [...cur, m]))

  return (
    <Panel title={t('diagnostics.sysTitle')} subtitle={t('diagnostics.sysIntro')}>
      {error && <div className="notice notice-bad" role="alert">{error}</div>}
      <div className="form-grid">
        <fieldset className="field">
          <legend>{t('diagnostics.range')}</legend>
          <div role="radiogroup" aria-label={t('diagnostics.rangeAria')} className="btn-row">
            {(['1h', '24h', 'all', 'custom'] as Mode[]).map((m) => (
              <Button key={m} variant={mode === m ? 'primary' : 'secondary'} role="radio" aria-checked={mode === m} onClick={() => setMode(m)}>
                {m === '1h' ? t('diagnostics.quick1h') : m === '24h' ? t('diagnostics.quick24h') : m === 'all' ? t('diagnostics.sysModeAll') : t('diagnostics.custom')}
              </Button>
            ))}
          </div>
          <span className="muted small">{t('diagnostics.sysTz', { tz })}</span>
        </fieldset>
        {mode === 'custom' && (
          <div className="btn-row">
            <label className="field"><span>{t('diagnostics.from')}</span><input type="datetime-local" value={from} onChange={(e) => setFrom(e.target.value)} /></label>
            <label className="field"><span>{t('diagnostics.to')}</span><input type="datetime-local" value={to} onChange={(e) => setTo(e.target.value)} /></label>
          </div>
        )}
        <fieldset className="field">
          <legend>{t('diagnostics.sysScope')}</legend>
          <div role="radiogroup" className="btn-row">
            <Button variant={scopeKind === 'all' ? 'primary' : 'secondary'} role="radio" aria-checked={scopeKind === 'all'} onClick={() => setScopeKind('all')}>{t('diagnostics.sysScopeWhole')}</Button>
            <Button variant={scopeKind === 'modules' ? 'primary' : 'secondary'} role="radio" aria-checked={scopeKind === 'modules'} onClick={() => setScopeKind('modules')}>{t('diagnostics.sysScopeModules')}</Button>
          </div>
          {scopeKind === 'modules' && (
            <div className="btn-row" aria-label={t('diagnostics.sysModules')}>
              {(options.data?.modules ?? []).map((m) => (
                <label key={m} className="check"><input type="checkbox" checked={modules.includes(m)} onChange={() => toggle(m)} /> {DIAG_MODULE_VI[m] ?? m}</label>
              ))}
            </div>
          )}
        </fieldset>
        <div className="btn-row">
          <label className="field"><span>{t('diagnostics.sysMinLevel')}</span>
            <select value={minLevel} onChange={(e) => setMinLevel(e.target.value)}>
              <option value="">{t('diagnostics.sysAnyLevel')}</option>{(options.data?.levels ?? []).map((l) => <option key={l} value={l}>{l}</option>)}
            </select></label>
          <label className="field"><span>PLC</span>
            <select value={plcId} onChange={(e) => setPlcId(e.target.value)}>
              <option value="">{t('diagnostics.scopeAll')}</option>{plcs.map((p) => <option key={p.plcId} value={p.plcId}>{p.plcId}</option>)}
            </select></label>
          <label className="field"><span>TaskId</span><input value={taskId} onChange={(e) => setTaskId(e.target.value)} placeholder="GUID" /></label>
          <label className="field"><span>CorrelationId</span><input value={correlationId} onChange={(e) => setCorrelationId(e.target.value)} /></label>
        </div>
        <p className="muted small">{t('diagnostics.sysPlcHint')}</p>
        <div><Button variant="primary" disabled={busy || running || (scopeKind === 'modules' && modules.length === 0)} onClick={() => void start()}>{busy ? t('diagnostics.sysStarting') : t('diagnostics.sysStart')}</Button></div>
      </div>

      {job && (
        <div className="stack" aria-live="polite">
          <h3>{t('diagnostics.sysJob')} <Pill tone={DIAG_STATE_TONE[job.state] ?? 'muted'} dot={false}>{DIAG_STATE_VI[job.state] ?? job.state}</Pill>
            {job.completeness && <> <Pill tone={job.completeness === 'Complete' ? 'ok' : 'warn'} dot={false}>{job.completeness === 'Complete' ? t('diagnostics.sysComplete') : t('diagnostics.sysPartial')}</Pill></>}</h3>
          {running && <progress value={job.progress} max={100} aria-label={t('diagnostics.sysProgress')} />}
          <p className="muted">{t('diagnostics.sysStage', { stage: job.stage, progress: job.progress })}</p>
          {job.state === 'Failed' && <div className="notice notice-bad" role="alert">{describeError(new ApiRequestError(0, { code: job.errorCode ?? 'DIAGNOSTIC_EXPORT_FAILED', message: '' }))}</div>}
          {job.state === 'Completed' && (
            <>
              <p>{t('diagnostics.sysResult', { events: job.events, errors: job.errors, warnings: job.warnings, mb: mb(job.bytes), expires: job.expiresAt ? formatDateTime(job.expiresAt) : '—' })}</p>
              <div><Button variant="primary" onClick={() => void download(job.id)}>{t('diagnostics.sysDownload')}</Button></div>
              {job.completeness === 'Partial' && <div className="notice notice-warn" role="alert">{t('diagnostics.sysPartialWarn')}</div>}
              {job.notes.map((n) => <div key={n} className="notice notice-info">{n}</div>)}
              <div className="table-wrap">
                <table className="grid" aria-label={t('diagnostics.sysSources')}>
                  <thead><tr><th>{t('diagnostics.sysColSource')}</th><th>{t('diagnostics.sysColStatus')}</th><th>{t('diagnostics.sysColRows')}</th><th>{t('diagnostics.sysColOldest')}</th><th>{t('diagnostics.sysColNote')}</th></tr></thead>
                  <tbody>
                    {job.sources.map((s) => (
                      <tr key={s.name}>
                        <td className="mono">{s.name}</td>
                        <td><Pill tone={DIAG_SOURCE_TONE[s.status] ?? 'muted'} dot={false}>{DIAG_SOURCE_VI[s.status] ?? s.status}</Pill></td>
                        <td className="num">{s.rowsExported} / {s.rowsInRange}</td>
                        <td className="nowrap">{s.oldestStored ? formatDateTime(s.oldestStored) : '—'}</td>
                        <td className="small">{s.error ?? s.note ?? s.retention}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </>
          )}
        </div>
      )}

      <h3>{t('diagnostics.sysHistory')}</h3>
      <Async state={history} isEmpty={(d) => d.length === 0} empty={{ title: t('diagnostics.sysNoHistory') }}>
        {(rows) => (
          <div className="table-wrap">
            <table className="grid" aria-label={t('diagnostics.sysHistory')}>
              <tbody>
                {rows.map((j) => (
                  <tr key={j.id}>
                    <td className="nowrap">{formatDateTime(j.createdAt)}</td><td>{j.requestedBy}</td><td>{j.request.mode}</td>
                    <td><Pill tone={DIAG_STATE_TONE[j.state] ?? 'muted'} dot={false}>{DIAG_STATE_VI[j.state] ?? j.state}</Pill></td>
                    <td>{j.state === 'Completed' ? <Button size="sm" onClick={() => void download(j.id)}>{t('diagnostics.sysDownload')}</Button> : null}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Async>
      <p className="muted small">{t('diagnostics.privacy')} {t('diagnostics.sysAutoDelete')}</p>
    </Panel>
  )
}
