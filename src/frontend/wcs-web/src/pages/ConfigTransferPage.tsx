import { useCallback, useMemo, useState } from 'react'
import { api } from '../api/client'
import type { ApplyResult, Blocker, ExportDependencyReport, ImportPlan, MaintenanceView, PlanAction, PlanItem, TransferGroup, TransferGroupId, UploadResult } from '../api/configTransferTypes'
import { Modal } from '../components/Modal'
import { Async, Button, EmptyState, PageHeader, Panel, Pill } from '../components/ui'
import { useApi } from '../hooks/useApi'
import { hasMessage, useT, type MsgKey } from '../i18n'
import { formatDateTime } from '../i18n/format'
import { describeError } from '../i18n/vi'
import type { Tone } from '../i18n/vi'

const ACTION_TONE: Record<PlanAction, Tone> = { Create: 'ok', Update: 'info', Unchanged: 'muted', Skip: 'muted', Conflict: 'bad' }
const refKey = (g: string, k: string) => `${g}/${k}`

/** CR-026 – System → Configuration transfer (Admin). The page only drives the server: the server decides, validates and writes. */
export function ConfigTransferPage() {
  const t = useT()
  const status = useApi(useCallback(() => api.transferStatus(), []))
  const groups = useApi(useCallback(() => api.transferGroups(), []))
  const maint = useApi(useCallback(() => api.maintenance(), []), ['MaintenanceChanged'])
  return (
    <>
      <PageHeader title={t('transfer.title')} description={t('transfer.description')} />
      <Async state={status} empty={{ title: t('transfer.notEnabled'), hint: t('transfer.notEnabledHint') }}>
        {(s) => (
          <div className="stack">
            <div className="transfer-top">
              <MaintenancePanel maint={maint.data} onChanged={maint.reload} />
              <Panel title={t('transfer.backupTitle')} subtitle={t('transfer.backupHint')}>
                <Pill tone={s.backupReady ? 'ok' : 'bad'}>{s.backupReady ? t('transfer.backupReady') : t('transfer.backupNotReady')}</Pill>
                {s.backupProblem && <p className="muted small mt">{s.backupProblem}</p>}
              </Panel>
            </div>
            <Async state={groups} empty={{ title: t('transfer.notEnabled') }} isEmpty={(g) => g.length === 0}>
              {(gs) => (<><ExportPanel groups={gs} /><ImportPanel maint={maint.data} /></>)}
            </Async>
          </div>
        )}
      </Async>
    </>
  )
}

// ------------------------------------------------------------------------------------------------ maintenance
function MaintenancePanel({ maint, onChanged }: { maint: MaintenanceView | null; onChanged: () => void }) {
  const t = useT()
  const [reason, setReason] = useState('')
  const [confirm, setConfirm] = useState<boolean | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState(false)
  const on = maint?.enabled === true
  const apply = async (enabled: boolean) => {
    setBusy(true); setError(null); setDone(false)
    try { await api.setMaintenance(enabled, enabled ? reason.trim() || null : null); setConfirm(null); setDone(true); onChanged() }
    catch (e) { setError(describeError(e)); setConfirm(null) }
    finally { setBusy(false) }
  }
  return (
    <Panel title={t('transfer.maintTitle')} subtitle={t('transfer.maintSubtitle')} className={on ? 'transfer-maint-on' : undefined}>
      {error && <div className="notice notice-bad" role="alert">{error}</div>}
      {done && !error && <div className="notice notice-ok" role="status">{t('transfer.maintDone')}</div>}
      <div className="btn-row">
        <Pill tone={on ? 'warn' : 'ok'}>{on ? t('transfer.maintOn') : t('transfer.maintOff')}</Pill>
        {maint?.updatedAt && maint.changedBy && <span className="muted small">{t('transfer.maintBy', { user: maint.changedBy, time: formatDateTime(maint.updatedAt) })}</span>}
      </div>
      <p className="muted small">{on ? t('transfer.maintOnHint') : t('transfer.maintOffHint')}</p>
      {on && maint?.reason && <p className="small">{t('transfer.bannerReason', { reason: maint.reason })}</p>}
      {!on && (
        <label className="field"><span>{t('transfer.maintReason')}</span>
          <input value={reason} maxLength={300} placeholder={t('transfer.maintReasonPh')} onChange={(e) => setReason(e.target.value)} /></label>
      )}
      <div className="btn-row">
        <Button variant={on ? 'secondary' : 'primary'} disabled={busy || maint === null} onClick={() => setConfirm(!on)}>{on ? t('transfer.maintTurnOff') : t('transfer.maintTurnOn')}</Button>
      </div>
      {confirm !== null && (
        <Modal title={confirm ? t('transfer.maintTurnOn') : t('transfer.maintTurnOff')} onClose={() => setConfirm(null)}
          footer={<><Button onClick={() => setConfirm(null)}>{t('common.close')}</Button><Button variant="primary" disabled={busy} onClick={() => void apply(confirm)}>{confirm ? t('transfer.maintTurnOn') : t('transfer.maintTurnOff')}</Button></>}>
          <p>{confirm ? t('transfer.maintConfirmOn') : t('transfer.maintConfirmOff')}</p>
        </Modal>
      )}
    </Panel>
  )
}

// ------------------------------------------------------------------------------------------------ export
function GroupLabel({ g }: { g: TransferGroup }) {
  const t = useT()
  return (
    <span className="transfer-group">
      <strong>{t(`transfer.group_${g.id}` as MsgKey)}</strong>
      <span className="muted small">{t(`transfer.groupDesc_${g.id}` as MsgKey)}</span>
      <span className="muted small">{t('transfer.existing', { n: g.targetCount })}{g.dependsOn.length > 0 ? ' · ' + t('transfer.dependsOn', { groups: g.dependsOn.map((d) => t(`transfer.group_${d}` as MsgKey)).join(', ') }) : ''}</span>
    </span>
  )
}

function ExportPanel({ groups }: { groups: TransferGroup[] }) {
  const t = useT()
  const [sel, setSel] = useState<TransferGroupId[]>([])
  const [deps, setDeps] = useState<ExportDependencyReport | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState(false)
  const toggle = (id: TransferGroupId) => { setSel((c) => (c.includes(id) ? c.filter((x) => x !== id) : [...c, id])); setDeps(null); setDone(false) }
  const run = async (ids: TransferGroupId[]) => {
    setBusy(true); setError(null); setDone(false)
    try { await api.transferExport(ids); setDone(true); setDeps(null) } catch (e) { setError(describeError(e)) } finally { setBusy(false) }
  }
  const start = async () => {
    if (sel.length === 0) { setError(t('transfer.exportNone')); return }
    setBusy(true); setError(null)
    try {
      const report = await api.transferExportCheck(sel)
      if (report.missing.length > 0) { setDeps(report); setBusy(false); return }   // the user decides whether the groups it depends on travel along
    } catch (e) { setError(describeError(e)); setBusy(false); return }
    setBusy(false)
    await run(sel)
  }
  return (
    <Panel title={t('transfer.exportTitle')} subtitle={t('transfer.exportIntro')}>
      {error && <div className="notice notice-bad" role="alert">{error}</div>}
      {done && <div className="notice notice-ok" role="status">{t('transfer.exportDone')}</div>}
      <div className="btn-row"><Button size="sm" onClick={() => { setSel(groups.map((g) => g.id)); setDeps(null) }}>{t('transfer.exportSelectAll')}</Button></div>
      <ul className="transfer-groups">
        {groups.map((g) => (
          <li key={g.id}><label className="check"><input type="checkbox" checked={sel.includes(g.id)} onChange={() => toggle(g.id)} /><GroupLabel g={g} /></label></li>
        ))}
      </ul>
      {deps && (
        <div className="notice notice-warn" role="alert">
          <strong>{t('transfer.exportDepsTitle')}</strong>
          <p>{t('transfer.exportDepsText', { groups: deps.missing.map((m) => t(`transfer.group_${m}` as MsgKey)).join(', ') })}</p>
          <div className="btn-row">
            <Button variant="primary" disabled={busy} onClick={() => { const all = deps.selected.concat(deps.missing); setSel(all); void run(all) }}>{t('transfer.exportInclude')}</Button>
            <Button disabled={busy} onClick={() => void run(sel)}>{t('transfer.exportWithout')}</Button>
          </div>
        </div>
      )}
      <div className="btn-row"><Button variant="primary" disabled={busy || sel.length === 0} onClick={() => void start()}>{t('transfer.exportBtn')}</Button></div>
    </Panel>
  )
}

// ------------------------------------------------------------------------------------------------ import
function ImportPanel({ maint }: { maint: MaintenanceView | null }) {
  const t = useT()
  const [file, setFile] = useState<File | null>(null)
  const [up, setUp] = useState<UploadResult | null>(null)
  const [included, setIncluded] = useState<TransferGroupId[]>([])
  const [excluded, setExcluded] = useState<string[]>([])
  const [plan, setPlan] = useState<ImportPlan | null>(null)
  const [stale, setStale] = useState(false)
  const [confirmHeader, setConfirmHeader] = useState(false)
  const [skipConflicts, setSkipConflicts] = useState(false)
  const [result, setResult] = useState<ApplyResult | null>(null)
  const [asking, setAsking] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const reset = () => { setUp(null); setPlan(null); setResult(null); setStale(false); setExcluded([]); setConfirmHeader(false); setSkipConflicts(false); setError(null) }
  const upload = async () => {
    if (!file) return
    reset(); setBusy(true)
    try { const r = await api.transferUpload(file); setUp(r); setIncluded(r.manifest.groups.map((g) => g.group)) } catch (e) { setError(describeError(e)) } finally { setBusy(false) }
  }
  const preview = useCallback(async (inc = included, exc = excluded) => {
    if (!up) return
    setBusy(true); setError(null)
    try {
      const p = await api.transferPreview(up.importId, { groups: inc, exclude: exc.map((k) => { const i = k.indexOf('/'); return { group: k.slice(0, i) as TransferGroupId, key: k.slice(i + 1) } }) })
      setPlan(p); setStale(false)
    } catch (e) { setError(describeError(e)) } finally { setBusy(false) }
  }, [up, included, excluded])

  const maintOn = maint?.enabled === true

  const toggleGroup = (g: TransferGroupId) => { setIncluded((c) => (c.includes(g) ? c.filter((x) => x !== g) : [...c, g])); setStale(true) }
  const toggleRecord = (g: string, k: string) => { const id = refKey(g, k); setExcluded((c) => (c.includes(id) ? c.filter((x) => x !== id) : [...c, id])); setStale(true) }

  const headerItems = useMemo(() => plan?.items.filter((i) => i.headerChange) ?? [], [plan])
  const conflicts = plan?.summary.conflict ?? 0
  const blockers = plan ? [...(maintOn ? [] : [{ code: 'MAINTENANCE_REQUIRED', message: '', count: 1, samples: [] as string[] }]), ...plan.readiness.blockers.filter((b) => b.code !== 'MAINTENANCE_REQUIRED')] : []
  const canApply = !!plan && !stale && blockers.length === 0 && !!plan.previewToken && (headerItems.length === 0 || confirmHeader) && (conflicts === 0 || skipConflicts) && !result
  const apply = async () => {
    if (!up || !plan?.previewToken) return
    setAsking(false); setBusy(true); setError(null)
    try { setResult(await api.transferApply(up.importId, { previewToken: plan.previewToken, confirmHeaderChanges: confirmHeader, skipConflicts })) }
    catch (e) { setError(describeError(e)) }
    finally { setBusy(false) }
  }

  return (
    <Panel title={t('transfer.importTitle')} subtitle={t('transfer.importIntro')}>
      {error && <div className="notice notice-bad" role="alert">{error}</div>}
      <div className="btn-row">
        <label className="btn btn-secondary btn-md transfer-file">
          <input type="file" accept=".zip,application/zip" onChange={(e) => { setFile(e.target.files?.[0] ?? null); reset() }} />
          {file ? file.name : t('transfer.chooseFile')}
        </label>
        <Button variant="primary" disabled={!file || busy} onClick={() => void upload()}>{t('transfer.uploadBtn')}</Button>
      </div>

      {up && (
        <div className="stack">
          <div className="notice notice-ok" role="status">{t('transfer.uploaded', { sha: up.packageSha256.slice(0, 12) })}</div>
          <p className="muted small">{t('transfer.packageFrom', { site: up.manifest.sourceSite || '–', by: up.manifest.exportedBy, time: formatDateTime(up.manifest.exportedAt), format: up.manifest.formatVersion })}</p>
          {up.warnings.length > 0 && (
            <div className="notice notice-warn"><strong>{t('transfer.packageWarnings')}</strong><ul>{up.warnings.map((w) => <li key={w}>{w}</li>)}</ul></div>
          )}
          <fieldset className="field"><legend>{t('transfer.packageGroups')}</legend>
            <ul className="transfer-groups">
              {up.manifest.groups.map((g) => (
                <li key={g.group}><label className="check"><input type="checkbox" checked={included.includes(g.group)} onChange={() => toggleGroup(g.group)} />
                  <span><strong>{t(`transfer.group_${g.group}` as MsgKey)}</strong> <span className="muted small">({g.count})</span></span></label></li>
              ))}
            </ul>
          </fieldset>
          <div className="btn-row">
            <Button variant="primary" disabled={busy || included.length === 0} onClick={() => void preview()}>{plan ? t('transfer.previewAgain') : t('transfer.previewBtn')}</Button>
            <span className="muted small">{t('transfer.previewHint')}</span>
          </div>
        </div>
      )}

      {plan && <PlanView plan={plan} stale={stale} excluded={excluded} onToggle={toggleRecord} known={up?.warnings ?? []} />}

      {plan && !result && (
        <div className="stack">
          <Readiness plan={plan} stale={stale} maintOn={maintOn} blockers={blockers} />
          {headerItems.length > 0 && <label className="check"><input type="checkbox" checked={confirmHeader} onChange={(e) => setConfirmHeader(e.target.checked)} /><span>{t('transfer.confirmHeader')}</span></label>}
          {conflicts > 0 && <label className="check"><input type="checkbox" checked={skipConflicts} onChange={(e) => setSkipConflicts(e.target.checked)} /><span>{t('transfer.skipConflicts')}</span></label>}
          <p className="muted small">{t('transfer.reviewNote')}</p>
          <div className="btn-row">
            <Button variant="primary" disabled={!canApply || busy} onClick={() => setAsking(true)}>{t('transfer.applyBtn')}</Button>
            {!maintOn && <span className="muted small">{t('transfer.applyHintMaint')}</span>}
            {plan.previewExpiresAt && <span className="muted small">{t('transfer.previewExpires', { time: formatDateTime(plan.previewExpiresAt) })}</span>}
          </div>
        </div>
      )}

      {result && (
        <div className="notice notice-ok" role="status">
          <strong>{t('transfer.resultTitle')}</strong>
          {result.alreadyApplied && <p>{t('transfer.resultAlready')}</p>}
          <p>{t('transfer.resultCounts', { created: result.created, updated: result.updated, unchanged: result.unchanged, skipped: result.skipped, conflicts: result.conflictsSkipped })}</p>
          <p className="small">{t('transfer.resultBackup', { file: result.backupFile || '–' })}</p>
          <p>{t('transfer.resultMaint')}</p>
        </div>
      )}

      {asking && (
        <Modal title={t('transfer.applyBtn')} onClose={() => setAsking(false)}
          footer={<><Button onClick={() => setAsking(false)}>{t('common.close')}</Button><Button variant="primary" disabled={busy} onClick={() => void apply()}>{t('transfer.applyBtn')}</Button></>}>
          <p>{t('transfer.applyConfirm')}</p>
        </Modal>
      )}
    </Panel>
  )
}

function fieldLabel(t: ReturnType<typeof useT>, f: string): string {
  const k = `transfer.field_${f}` as MsgKey
  return hasMessage(k) ? t(k) : f
}

function Changes({ item }: { item: PlanItem }) {
  const t = useT()
  if (item.action === 'Create') return <span className="muted small">{t('transfer.newRecord')}</span>
  if (item.changes.length === 0) return null
  return (
    <ul className="transfer-changes">
      {item.changes.map((c) => (
        <li key={c.field}><span className="muted">{fieldLabel(t, c.field)}:</span> <code>{c.from ?? '∅'}</code> → <code>{c.to ?? '∅'}</code>{c.field === 'definition' ? '' : ''}</li>
      ))}
    </ul>
  )
}

function PlanView({ plan, stale, excluded, onToggle, known }: { plan: ImportPlan; stale: boolean; excluded: string[]; onToggle: (g: string, k: string) => void; known: string[] }) {
  const t = useT()
  const [showSame, setShowSame] = useState(false)
  const s = plan.summary
  const warnings = plan.warnings.filter((w) => !known.includes(w))
  if (plan.items.length === 0) return <EmptyState title={t('transfer.noRecords')} />
  return (
    <div className={stale ? 'transfer-plan stale' : 'transfer-plan'}>
      <div className="btn-row transfer-summary">
        <Pill tone="ok">{t('transfer.sumCreate')}: {s.create}</Pill>
        <Pill tone="info">{t('transfer.sumUpdate')}: {s.update}</Pill>
        <Pill tone="muted">{t('transfer.sumUnchanged')}: {s.unchanged}</Pill>
        <Pill tone="muted">{t('transfer.sumSkip')}: {s.skip}</Pill>
        <Pill tone={s.conflict > 0 ? 'bad' : 'muted'}>{t('transfer.sumConflict')}: {s.conflict}</Pill>
      </div>
      {warnings.length > 0 && <div className="notice notice-warn"><ul>{warnings.map((w) => <li key={w}>{w}</li>)}</ul></div>}
      {s.unchanged > 0 && <label className="check"><input type="checkbox" checked={showSame} onChange={(e) => setShowSame(e.target.checked)} /><span>{t('transfer.showUnchanged', { n: s.unchanged })}</span></label>}
      <div className="table-wrap">
        <table className="grid">
          <thead><tr><th>{t('transfer.colGroup')}</th><th>{t('transfer.colKey')}</th><th>{t('transfer.colAction')}</th><th>{t('transfer.colChanges')}</th><th>{t('transfer.colInclude')}</th></tr></thead>
          <tbody>
            {plan.items.filter((i) => showSame || i.action !== 'Unchanged').map((i) => {
              const off = excluded.includes(refKey(i.group, i.key))
              const conflictKey = `transfer.conflict_${i.conflictCode}` as MsgKey
              return (
                <tr key={refKey(i.group, i.key)}>
                  <td>{t(`transfer.group_${i.group}` as MsgKey)}</td>
                  <td><code>{i.key}</code></td>
                  <td><Pill tone={ACTION_TONE[i.action]}>{t(`transfer.action_${i.action}` as MsgKey)}</Pill>{i.headerChange && <> <Pill tone="warn" dot={false}>{t('transfer.headerBadge')}</Pill></>}</td>
                  <td>
                    <Changes item={i} />
                    {i.conflictCode && <p className="small transfer-conflict">{hasMessage(conflictKey) ? t(conflictKey) : i.conflictCode}<br /><span className="muted">{i.conflictMessage}</span></p>}
                  </td>
                  <td>{i.action !== 'Unchanged' && (
                    <input type="checkbox" aria-label={t('transfer.includeAria', { key: i.key })} checked={!off}
                      onChange={() => onToggle(i.group, i.key)} />
                  )}</td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
    </div>
  )
}

function Readiness({ plan, stale, maintOn, blockers }: { plan: ImportPlan; stale: boolean; maintOn: boolean; blockers: Blocker[] }) {
  const t = useT()
  const r = plan.readiness
  const ok = blockers.length === 0 && !stale
  return (
    <div className={ok ? 'notice notice-ok' : 'notice notice-warn'} role="status">
      <strong>{t('transfer.readinessTitle')}</strong>
      <p className="small">{t('transfer.readinessMaint', { state: maintOn ? t('transfer.stateOn') : t('transfer.stateOff') })} · {t('transfer.readinessBackup', { state: r.backupReady ? t('transfer.stateReady') : t('transfer.stateNotReady') })}</p>
      {ok ? <p>{t('transfer.readinessOk')}</p> : (
        <>
          <ul>{blockers.map((b) => (
            <li key={b.code}>{hasMessage(`transfer.blocker_${b.code}` as MsgKey) ? t(`transfer.blocker_${b.code}` as MsgKey) : b.message}{b.count > 1 || b.samples.length > 0 ? ` (${b.count}${b.samples.length ? ': ' + b.samples.join(', ') : ''})` : ''}</li>
          ))}</ul>
          {blockers.some((b) => b.code.startsWith('ACTIVE_')) && <p className="muted small">{t('transfer.blockerNote')}</p>}
        </>
      )}
    </div>
  )
}
