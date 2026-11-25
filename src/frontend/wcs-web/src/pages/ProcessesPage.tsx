import { useCallback, useMemo, useState } from 'react'
import { api } from '../api/client'
import type { CandidateDef, ConditionDef, Process, ProcessHeaderBody, ProcessIssue, ProcessVersion, StepDef, VersionDefinition } from '../api/routingTypes'
import { Modal } from '../components/Modal'
import { Async, Button, PageHeader, Panel, Pill } from '../components/ui'
import { useApi } from '../hooks/useApi'
import { useT } from '../i18n'
import { formatDateTime } from '../i18n/format'
import {
  CONDITION_LABEL, CONDITION_PARAMS, CONDITION_TYPES, DEST_MODES, DEST_MODE_LABEL, ISSUE_LABEL, RACK_KIND_LABEL, REQUEST_TYPES, REQUEST_TYPE_LABEL, RESOLUTIONS,
  RESOLUTION_LABEL, SOURCE_MODES, SOURCE_MODE_LABEL, STEP_TYPES, STEP_TYPE_LABEL, VERSION_STATE_LABEL, VERSION_STATE_TONE, WAIT_BEHAVIORS, WAIT_BEHAVIOR_LABEL,
} from '../i18n/routing'
import { describeError } from '../i18n/vi'
import { ApiRequestError } from '../api/errors'
import { Field, Switch } from './config/FormKit'

const MOVEMENT = ['Pickup', 'Delivery', 'BufferStorage', 'BufferRetrieval']
const isMovement = (type: string) => MOVEMENT.includes(type)

/**
 * CR-010 administration: Business Process, its versions and the route of each version (steps, candidates in priority order, conditions, buffers, waiting).
 * Everything edited here is data. Conditions are chosen from a fixed list; no free script is accepted. A Published version is read-only.
 */
export function ProcessesPage() {
  const t = useT()
  const list = useApi(useCallback(() => api.processes(), []))
  const [selected, setSelected] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)
  const [busy, setBusy] = useState(false)

  const importReference = async () => {
    setBusy(true); setError(null); setMessage(null)
    try { const r = await api.processImportReference(); setMessage(t('routing.procImportDone', { imported: r.imported, skipped: r.skipped })); list.reload() }
    catch (e) { setError(describeError(e)) } finally { setBusy(false) }
  }

  return (
    <>
      <PageHeader title={t('routing.procTitle')}
        actions={<><Button disabled={busy} onClick={importReference}>{t('routing.procImport')}</Button> <Button variant="primary" onClick={() => setCreating(true)}>{t('routing.procNew')}</Button></>} />
      {message && <div className="notice notice-ok" role="status">{message}</div>}
      {error && <div className="notice notice-error" role="alert">{error}</div>}
      <Panel flush>
        <Async state={list} isEmpty={(d) => d.length === 0} empty={{ title: t('routing.procNone'), hint: t('routing.procNoneHint') }}>
          {(rows) => (
            <div className="table-wrap">
              <table className="grid clickable">
                <thead><tr><th>{t('routing.procColCode')}</th><th>{t('routing.procColName')}</th><th>{t('routing.procColRequest')}</th><th>{t('routing.procColRack')}</th><th>{t('routing.procColTriggers')}</th><th>{t('routing.procColPublished')}</th><th>{t('routing.procColStatus')}</th></tr></thead>
                <tbody>
                  {rows.map((p) => (
                    <tr key={p.id} className={p.id === selected ? 'selected' : ''} onClick={() => setSelected(p.id)}>
                      <td className="mono nowrap"><strong>{p.code}</strong></td>
                      <td>{p.name}</td>
                      <td>{REQUEST_TYPE_LABEL[p.requestType] ?? p.requestType}</td>
                      <td>{RACK_KIND_LABEL[p.rackKind] ?? p.rackKind}</td>
                      <td className="mono">{p.triggerLocationCodes.join(', ')}</td>
                      <td>{p.publishedVersion ? <Pill tone="ok">{t('routing.versionShort', { n: p.publishedVersion })}</Pill> : <span className="muted">{t('routing.procNotPublished')}</span>}</td>
                      <td>{p.reviewBlocked ? <Pill tone="bad">{t('routing.procReview')}</Pill> : p.enabled ? <Pill tone="ok">{t('routing.procEnabled')}</Pill> : <Pill tone="muted">{t('routing.procDisabled')}</Pill>}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Async>
      </Panel>
      {selected && <ProcessDetail key={selected} id={selected} onChanged={list.reload} />}
      {creating && <NewProcessDialog onClose={() => setCreating(false)} onCreated={(p) => { setCreating(false); list.reload(); setSelected(p.id) }} />}
    </>
  )
}

// ====================================================================================================================== create
function NewProcessDialog({ onClose, onCreated }: { onClose: () => void; onCreated: (p: Process) => void }) {
  const t = useT()
  const [body, setBody] = useState<ProcessHeaderBody>({ code: '', name: '', description: null, requestType: REQUEST_TYPES[0], rackKind: 'Full', triggerLocationCodes: [] })
  const [triggers, setTriggers] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const submit = async () => {
    setBusy(true); setError(null)
    try { onCreated(await api.processCreate({ ...body, triggerLocationCodes: splitCodes(triggers) })) } catch (e) { setError(describeError(e)) } finally { setBusy(false) }
  }
  return (
    <Modal title={t('routing.procNew')} onClose={onClose} footer={<><Button onClick={onClose}>{t('routing.closeDialog')}</Button><Button variant="primary" disabled={busy || !body.code || !body.name} onClick={submit}>{t('routing.procNew')}</Button></>}>
      {error && <div className="notice notice-error" role="alert">{error}</div>}
      <HeaderFields body={body} setBody={setBody} triggers={triggers} setTriggers={setTriggers} codeEditable />
    </Modal>
  )
}

const splitCodes = (s: string) => s.split(',').map((x) => x.trim()).filter(Boolean)

function HeaderFields({ body, setBody, triggers, setTriggers, codeEditable }: {
  body: ProcessHeaderBody; setBody: (b: ProcessHeaderBody) => void; triggers: string; setTriggers: (s: string) => void; codeEditable?: boolean
}) {
  const t = useT()
  return (
    <div className="form-grid">
      <Field label={t('routing.procFieldCode')}><input value={body.code} disabled={!codeEditable} maxLength={60} onChange={(e) => setBody({ ...body, code: e.target.value })} /></Field>
      <Field label={t('routing.procFieldName')}><input value={body.name} maxLength={200} onChange={(e) => setBody({ ...body, name: e.target.value })} /></Field>
      <Field label={t('routing.procFieldRequestType')}>
        <select value={body.requestType} onChange={(e) => setBody({ ...body, requestType: e.target.value })}>{REQUEST_TYPES.map((r) => <option key={r} value={r}>{REQUEST_TYPE_LABEL[r]}</option>)}</select>
      </Field>
      <Field label={t('routing.procFieldRackKind')}>
        <select value={body.rackKind} onChange={(e) => setBody({ ...body, rackKind: e.target.value })}>{['Full', 'Empty'].map((r) => <option key={r} value={r}>{RACK_KIND_LABEL[r]}</option>)}</select>
      </Field>
      <Field label={t('routing.procFieldTriggers')} wide><input value={triggers} onChange={(e) => setTriggers(e.target.value)} placeholder="A1, A2" /></Field>
      <Field label={t('routing.procFieldDescription')} wide><textarea rows={2} value={body.description ?? ''} onChange={(e) => setBody({ ...body, description: e.target.value || null })} /></Field>
    </div>
  )
}

// ====================================================================================================================== one process
function ProcessDetail({ id, onChanged }: { id: string; onChanged: () => void }) {
  const t = useT()
  const process = useApi(useCallback(() => api.process(id), [id]))
  const [tab, setTab] = useState<'config' | 'audit'>('config')
  const [versionId, setVersionId] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const reload = () => { process.reload(); onChanged() }
  const newVersion = async (p: Process) => {
    setError(null); setMessage(null)
    try { const v = await api.processNewVersion(p.id, p.versions[0]?.id); setMessage(t('routing.procVersionNewDone', { n: v.number })); setVersionId(v.id); reload() } catch (e) { setError(describeError(e)) }
  }

  return (
    <Panel title={process.data ? t('routing.procDetail', { code: process.data.code }) : undefined}>
      <div className="tabs" role="tablist">
        <button role="tab" aria-selected={tab === 'config'} className={tab === 'config' ? 'tab active' : 'tab'} onClick={() => setTab('config')}>{t('routing.procTabConfig')}</button>
        <button role="tab" aria-selected={tab === 'audit'} className={tab === 'audit' ? 'tab active' : 'tab'} onClick={() => setTab('audit')}>{t('routing.procTabAudit')}</button>
      </div>
      {message && <div className="notice notice-ok" role="status">{message}</div>}
      {error && <div className="notice notice-error" role="alert">{error}</div>}
      {tab === 'audit' ? <AuditList /> : (
        <Async state={process} empty={{ title: t('routing.procNone') }}>
          {(p) => (
            <>
              <HeaderPanel p={p} onSaved={(msg) => { setMessage(msg); reload() }} onError={setError} />
              <OperationPanel key={p.queueScope ?? ''} p={p} onSaved={(msg) => { setError(null); setMessage(msg); reload() }} onError={setError} />
              <section className="detail-sec">
                <h3>{t('routing.procVersions')} <Button size="sm" onClick={() => newVersion(p)}>{t('routing.procVersionNew')}</Button></h3>
                <div className="table-wrap">
                  <table className="grid clickable">
                    <thead><tr><th>{t('routing.procVersionColNumber')}</th><th>{t('routing.procVersionColState')}</th><th>{t('routing.procVersionColSteps')}</th><th>{t('routing.procVersionColBy')}</th><th>{t('routing.procVersionColPublished')}</th><th>{t('routing.procVersionNote')}</th></tr></thead>
                    <tbody>
                      {p.versions.map((v) => (
                        <tr key={v.id} className={v.id === (versionId ?? p.versions[0]?.id) ? 'selected' : ''} onClick={() => setVersionId(v.id)}>
                          <td className="num">{v.number}</td>
                          <td><Pill tone={VERSION_STATE_TONE[v.state] ?? 'muted'}>{VERSION_STATE_LABEL[v.state] ?? v.state}</Pill></td>
                          <td className="num">{v.stepCount}</td>
                          <td>{v.createdBy ?? '—'}</td>
                          <td className="nowrap">{formatDateTime(v.publishedAt)}</td>
                          <td>{v.note ?? ''}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </section>
              {(versionId ?? p.versions[0]?.id) && <VersionEditor key={versionId ?? p.versions[0]?.id} versionId={(versionId ?? p.versions[0]?.id)!} onChanged={reload} />}
            </>
          )}
        </Async>
      )}
    </Panel>
  )
}

/** CR-040: what a published process needs before its requests run - Operational Readiness of the published version (D-12) and the queue ordering scope (D-13). The server decides. */
function OperationPanel({ p, onSaved, onError }: { p: Process; onSaved: (msg: string) => void; onError: (m: string) => void }) {
  const t = useT()
  const published = p.versions.find((v) => v.state === 'Published')
  const [scope, setScope] = useState(p.queueScope ?? '')
  const [reason, setReason] = useState('')
  const [shared, setShared] = useState<string[]>([])
  const [busy, setBusy] = useState(false)
  const run = async (fn: () => Promise<unknown>) => {
    setBusy(true)
    try { await fn(); setReason(''); onSaved(t('linked.opSaved')) } catch (e) { onError(describeError(e)) } finally { setBusy(false) }
  }
  const ready = reason.trim() !== '' && !busy
  return (
    <section className="detail-sec">
      <h3>{t('linked.opTitle')}</h3>
      <p className="muted small">{t(p.requiresReadiness ? 'linked.opQueued' : 'linked.opNotQueued')}</p>
      <dl className="kv2">
        <dt>{t('linked.opReadiness')}</dt><dd>{published ? `${t('routing.versionShort', { n: published.number })} · ${t(`linked.readiness_${published.readiness ?? 'NotReady'}` as never)}` : t('routing.procNotPublished')}</dd>
        <dt>{t('linked.opScope')}</dt><dd className="mono">{p.queueScope ?? t('linked.scopeNone')}</dd>
      </dl>
      <div className="form-grid">
        <Field label={t('linked.opScopeField')} hint={t('linked.opScopeHint')}><input value={scope} maxLength={60} onChange={(e) => setScope(e.target.value)} /></Field>
        <Field label={t('linked.reason')}><input value={reason} maxLength={500} onChange={(e) => setReason(e.target.value)} /></Field>
      </div>
      <div className="btn-row">
        <Button disabled={!ready || scope.trim().toUpperCase() === (p.queueScope ?? '')} onClick={() => void run(async () => setShared((await api.processQueueScope(p.id, scope.trim() || null, reason)).sharedLocationsWithOtherScopes))}>{t('linked.opSaveScope')}</Button>
        <Button variant="primary" disabled={!ready || !published || published.readiness === 'Ready'} onClick={() => void run(() => api.processReadiness(published!.id, 'Ready', reason))}>{t('linked.opSetReady')}</Button>
        <Button disabled={!ready || !published || published.readiness !== 'Ready'} onClick={() => void run(() => api.processReadiness(published!.id, 'Suspended', reason))}>{t('linked.opSuspend')}</Button>
      </div>
      {shared.length > 0 && <div className="notice notice-warn" role="status"><p>{t('linked.opShared')}</p><ul>{shared.map((s) => <li key={s}>{s}</li>)}</ul></div>}
    </section>
  )
}

function HeaderPanel({ p, onSaved, onError }: { p: Process; onSaved: (msg: string) => void; onError: (m: string) => void }) {
  const t = useT()
  const [body, setBody] = useState<ProcessHeaderBody>({ code: p.code, name: p.name, description: p.description, requestType: p.requestType, rackKind: p.rackKind, triggerLocationCodes: p.triggerLocationCodes, enabled: p.enabled })
  const [triggers, setTriggers] = useState(p.triggerLocationCodes.join(', '))
  const save = async () => {
    try { await api.processUpdate(p.id, { ...body, triggerLocationCodes: splitCodes(triggers) }); onSaved(t('routing.procSaved')) } catch (e) { onError(describeError(e)) }
  }
  return (
    <section className="detail-sec">
      <h3>{t('routing.procHeaderTitle')}</h3>
      {p.reviewBlocked && <div className="notice notice-warn">{p.reviewReason ?? t('routing.procReview')}</div>}
      {p.sourceReference && <p className="muted">{p.sourceReference}</p>}
      <HeaderFields body={body} setBody={setBody} triggers={triggers} setTriggers={setTriggers} />
      <Switch checked={body.enabled ?? true} onChange={(v) => setBody({ ...body, enabled: v })} label={t('routing.procFieldEnabled')} />
      <p><Button onClick={save}>{t('routing.procSave')}</Button></p>
    </section>
  )
}

function AuditList() {
  const t = useT()
  const audit = useApi(useCallback(() => api.processAudit(), []))
  return (
    <Async state={audit} isEmpty={(d) => d.length === 0} empty={{ title: t('routing.procAuditNone') }}>
      {(rows) => (
        <div className="table-wrap">
          <table className="grid">
            <thead><tr><th>{t('routing.procAuditColTime')}</th><th>{t('routing.procAuditColActor')}</th><th>{t('routing.procAuditColAction')}</th><th>{t('routing.procAuditColTarget')}</th><th>{t('routing.procAuditColResult')}</th></tr></thead>
            <tbody>{rows.map((r) => (
              <tr key={r.id}><td className="nowrap">{formatDateTime(r.occurredAt)}</td><td>{r.actor ?? '—'}</td><td className="mono">{r.action}</td><td className="mono">{r.targetType ?? ''} {r.targetId ?? ''}</td><td>{r.result}</td></tr>
            ))}</tbody>
          </table>
        </div>
      )}
    </Async>
  )
}

// ====================================================================================================================== version editor
function cloneDef(d: VersionDefinition): VersionDefinition { return JSON.parse(JSON.stringify(d)) as VersionDefinition }

function VersionEditor({ versionId, onChanged }: { versionId: string; onChanged: () => void }) {
  const t = useT()
  // the notices live here, not in the editor body: the body is re-created after every reload and would lose them
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const version = useApi(useCallback(() => api.processVersion(versionId), [versionId]))
  const locations = useApi(useCallback(() => api.locations(), []))
  return (
    <Async state={version} empty={{ title: t('routing.edNoSteps') }}>
      {(v) => <EditorBody message={message} setMessage={setMessage} error={error} setError={setError} key={`${v.id}|${v.state}|${v.validatedAt}|${v.note}|${v.issues.length}|${JSON.stringify(v.definition)}`} v={v} locationCodes={(locations.data ?? []).map((l) => l.code)} reload={() => { version.reload(); onChanged() }} />}
    </Async>
  )
}

function EditorBody({ v, locationCodes, reload, message, setMessage, error, setError }: {
  v: ProcessVersion; locationCodes: string[]; reload: () => void; message: string | null; setMessage: (m: string | null) => void; error: string | null; setError: (m: string | null) => void
}) {
  const t = useT()
  const [draft, setDraft] = useState<VersionDefinition>(() => cloneDef(v.definition))
  const [dirty, setDirty] = useState(false)
  const [issues, setIssues] = useState<ProcessIssue[]>(v.issues)
  const [busy, setBusy] = useState(false)
  const editable = v.editable
  const stepKeys = useMemo(() => draft.steps.map((s) => s.stepKey), [draft])

  const change = (fn: (d: VersionDefinition) => void) => { const next = cloneDef(draft); fn(next); setDraft(next); setDirty(true) }
  const run = async (fn: () => Promise<void>) => { setBusy(true); setError(null); setMessage(null); try { await fn() } catch (e) { setError(describeError(e)) } finally { setBusy(false) } }

  const save = () => run(async () => { await api.processSaveDefinition(v.id, draft); setMessage(t('routing.edSaved')); reload() })
  const validate = () => run(async () => {
    const r = await api.processValidate(v.id)
    setIssues(r.issues); setMessage(r.state === 'Validated' ? t('routing.edValidated') : t('routing.edNotValid')); reload()
  })
  const publish = () => run(async () => {
    if (!window.confirm(t('routing.edPublishConfirm'))) return
    try { await api.processPublish(v.id); setMessage(t('routing.edPublished')); reload() }
    catch (e) {
      if (e instanceof ApiRequestError && Array.isArray(e.details)) setIssues(e.details as ProcessIssue[])   // the failing checks come with the refusal
      throw e
    }
  })
  const disable = () => run(async () => { await api.processDisable(v.id); setMessage(t('routing.edDisabledDone')); reload() })

  const addStep = () => change((d) => {
    const n = d.steps.length + 1
    d.steps.push({ stepKey: `step${n}`, order: n, type: 'Delivery', sourceMode: 'Request', destinationMode: 'Candidates', resolution: 'BeforePick', waitBehavior: 'Wait', errorBehavior: 'Fail', candidates: [] })
    if (!d.firstStepKey) d.firstStepKey = `step${n}`
  })

  return (
    <section className="detail-sec">
      <h3>{t('routing.procVersionNumber', { n: v.number })} <Pill tone={VERSION_STATE_TONE[v.state] ?? 'muted'}>{VERSION_STATE_LABEL[v.state] ?? v.state}</Pill></h3>
      {!editable && <div className="notice notice-warn">{t('routing.procPublishedLocked')}</div>}
      {message && <div className="notice notice-ok" role="status">{message}</div>}
      {error && <div className="notice notice-error" role="alert">{error}</div>}
      {dirty && <div className="notice notice-warn" role="status">{t('routing.edUnsaved')}</div>}

      <datalist id="route-locations">{locationCodes.map((c) => <option key={c} value={c} />)}</datalist>

      <div className="form-grid">
        <Field label={t('routing.edFirstStep')}>
          <select disabled={!editable} value={draft.firstStepKey} onChange={(e) => change((d) => { d.firstStepKey = e.target.value })}>
            {stepKeys.map((k) => <option key={k} value={k}>{k}</option>)}
          </select>
        </Field>
      </div>

      {draft.steps.length === 0 && <p className="muted">{t('routing.edNoSteps')}</p>}
      {draft.steps.map((s, i) => (
        <StepCard key={i} step={s} stepKeys={stepKeys} editable={editable}
          onChange={(fn) => change((d) => fn(d.steps[i]))} onRemove={() => change((d) => { d.steps.splice(i, 1) })} />
      ))}
      {editable && <p><Button onClick={addStep}>{t('routing.edAddStep')}</Button></p>}

      <div className="page-actions">
        {editable && <Button variant="primary" disabled={busy || !dirty} onClick={save}>{t('routing.edSave')}</Button>}
        {editable && <Button disabled={busy || dirty} title={dirty ? t('routing.edSaveBeforeValidate') : undefined} onClick={validate}>{t('routing.edValidate')}</Button>}
        {v.state === 'Validated' && <Button variant="primary" disabled={busy || dirty} onClick={publish}>{t('routing.edPublish')}</Button>}
        {v.state === 'Published' && <Button variant="danger" disabled={busy} onClick={disable}>{t('routing.edDisable')}</Button>}
      </div>

      <h4>{t('routing.edIssues')}</h4>
      {issues.length === 0 ? <p className="muted">{t('routing.edIssuesNone')}</p> : (
        <ul className="issues">
          {issues.map((i, k) => (
            <li key={k}><Pill tone={i.severity === 'Error' ? 'bad' : 'warn'}>{i.severity === 'Error' ? t('routing.edIssueError') : t('routing.edIssueWarning')}</Pill>{' '}
              {i.stepKey && <span className="mono">[{i.stepKey}] </span>}{ISSUE_LABEL[i.code] ?? i.message} <span className="muted mono">{i.code}</span></li>
          ))}
        </ul>
      )}
    </section>
  )
}

function SelectOf({ value, options, labels, onChange, disabled, blank }: {
  value: string | null | undefined; options: readonly string[]; labels?: Record<string, string>; onChange: (v: string) => void; disabled?: boolean; blank?: string
}) {
  return (
    <select disabled={disabled} value={value ?? ''} onChange={(e) => onChange(e.target.value)}>
      {blank !== undefined && <option value="">{blank}</option>}
      {options.map((o) => <option key={o} value={o}>{labels?.[o] ?? o}</option>)}
    </select>
  )
}

function StepCard({ step, stepKeys, editable, onChange, onRemove }: {
  step: StepDef; stepKeys: string[]; editable: boolean; onChange: (fn: (s: StepDef) => void) => void; onRemove: () => void
}) {
  const t = useT()
  const movement = isMovement(step.type)
  const others = stepKeys.filter((k) => k !== step.stepKey)
  const cands = step.candidates ?? []
  const sources = cands.filter((c) => c.role === 'Source')
  const dests = cands.filter((c) => c.role === 'Destination')
  const setCands = (role: 'Source' | 'Destination', list: CandidateDef[]) => onChange((s) => { s.candidates = [...(s.candidates ?? []).filter((c) => c.role !== role), ...list] })
  const addCand = (role: 'Source' | 'Destination') => {
    const list = role === 'Source' ? sources : dests
    setCands(role, [...list, { role, priority: 1, order: list.length + 1, locationCode: '', noBind: false, conditions: [] }])
  }
  return (
    <div className="step-card panel">
      <div className="form-grid">
        <Field label={t('routing.edStepKey')}><input disabled={!editable} value={step.stepKey} maxLength={60} onChange={(e) => onChange((s) => { s.stepKey = e.target.value })} /></Field>
        <Field label={t('routing.edOrder')}><input type="number" disabled={!editable} value={step.order} onChange={(e) => onChange((s) => { s.order = Number(e.target.value) })} /></Field>
        <Field label={t('routing.edType')}><SelectOf disabled={!editable} value={step.type} options={STEP_TYPES} labels={STEP_TYPE_LABEL} onChange={(v) => onChange((s) => { s.type = v })} /></Field>
        {step.type !== 'ProcessCompletion' && <Field label={t('routing.edNext')}><SelectOf disabled={!editable} value={step.nextStepKey} options={others} blank="—" onChange={(v) => onChange((s) => { s.nextStepKey = v || null })} /></Field>}
        {(step.type === 'Condition' || step.waitBehavior === 'Alternative') && <Field label={t('routing.edAlt')}><SelectOf disabled={!editable} value={step.alternativeNextStepKey} options={others} blank="—" onChange={(v) => onChange((s) => { s.alternativeNextStepKey = v || null })} /></Field>}
        {movement && <>
          <Field label={t('routing.edSourceMode')}><SelectOf disabled={!editable} value={step.sourceMode} options={SOURCE_MODES} labels={SOURCE_MODE_LABEL} onChange={(v) => onChange((s) => { s.sourceMode = v })} /></Field>
          <Field label={t('routing.edDestMode')}><SelectOf disabled={!editable} value={step.destinationMode} options={DEST_MODES} labels={DEST_MODE_LABEL} onChange={(v) => onChange((s) => { s.destinationMode = v })} /></Field>
          <Field label={t('routing.edResolution')}><SelectOf disabled={!editable} value={step.resolution} options={RESOLUTIONS} labels={RESOLUTION_LABEL} onChange={(v) => onChange((s) => { s.resolution = v })} /></Field>
          <Field label={t('routing.edWait')}><SelectOf disabled={!editable} value={step.waitBehavior} options={WAIT_BEHAVIORS} labels={WAIT_BEHAVIOR_LABEL} onChange={(v) => onChange((s) => { s.waitBehavior = v })} /></Field>
          <Field label={t('routing.edWaitingPosition')}><input disabled={!editable} list="route-locations" value={step.waitingPositionCode ?? ''} onChange={(e) => onChange((s) => { s.waitingPositionCode = e.target.value || null })} /></Field>
        </>}
      </div>

      {(step.type === 'Condition' || step.type === 'Waiting') && (
        <div><h4>{t('routing.edConditions')}</h4><ConditionsEditor list={step.conditions ?? []} editable={editable} needsLocation onChange={(l) => onChange((s) => { s.conditions = l })} /></div>
      )}

      {movement && (
        <>
          <h4>{t('routing.edCandidates')}</h4>
          <p className="muted">{t('routing.edHintPriority')}</p>
          {step.sourceMode === 'Candidates' && <CandidateTable role="Source" list={sources} editable={editable} onChange={(l) => setCands('Source', l)} onAdd={() => addCand('Source')} />}
          {step.destinationMode === 'Candidates' && <CandidateTable role="Destination" list={dests} editable={editable} onChange={(l) => setCands('Destination', l)} onAdd={() => addCand('Destination')} />}
        </>
      )}
      {editable && <p><Button size="sm" variant="danger" onClick={onRemove}>{t('routing.edRemoveStep')}</Button></p>}
    </div>
  )
}

function CandidateTable({ role, list, editable, onChange, onAdd }: { role: 'Source' | 'Destination'; list: CandidateDef[]; editable: boolean; onChange: (l: CandidateDef[]) => void; onAdd: () => void }) {
  const t = useT()
  const set = (i: number, fn: (c: CandidateDef) => void) => { const next = list.map((c) => ({ ...c })); fn(next[i]); onChange(next) }
  return (
    <div className="table-wrap">
      <table className="grid">
        <thead><tr><th>{t('routing.edRole')}</th><th>{t('routing.edPriority')}</th><th>{t('routing.edCandOrder')}</th><th>{t('routing.edLocation')}</th>{role === 'Destination' && <th>{t('routing.edNoBind')}</th>}<th>{t('routing.edConditions')}</th><th /></tr></thead>
        <tbody>
          {list.map((c, i) => (
            <tr key={i}>
              <td>{role === 'Source' ? t('routing.edAddSource') : t('routing.edAddDest')}</td>
              <td><input type="number" min={1} disabled={!editable} value={c.priority} style={{ width: 64 }} onChange={(e) => set(i, (x) => { x.priority = Number(e.target.value) })} /></td>
              <td><input type="number" min={1} disabled={!editable} value={c.order} style={{ width: 64 }} onChange={(e) => set(i, (x) => { x.order = Number(e.target.value) })} /></td>
              <td><input disabled={!editable} list="route-locations" value={c.locationCode} style={{ width: 110 }} onChange={(e) => set(i, (x) => { x.locationCode = e.target.value.trim() })} /></td>
              {role === 'Destination' && <td><input type="checkbox" disabled={!editable} checked={!!c.noBind} onChange={(e) => set(i, (x) => { x.noBind = e.target.checked })} /></td>}
              <td><ConditionsEditor list={c.conditions ?? []} editable={editable} onChange={(l) => set(i, (x) => { x.conditions = l })} /></td>
              <td>{editable && <Button size="sm" onClick={() => onChange(list.filter((_, k) => k !== i))}>{t('routing.edRemove')}</Button>}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {editable && <p><Button size="sm" onClick={onAdd}>{role === 'Source' ? t('routing.edAddSource') : t('routing.edAddDest')}</Button></p>}
    </div>
  )
}

function ConditionsEditor({ list, editable, onChange, needsLocation }: { list: ConditionDef[]; editable: boolean; onChange: (l: ConditionDef[]) => void; needsLocation?: boolean }) {
  const t = useT()
  const set = (i: number, fn: (c: ConditionDef) => void) => { const next = list.map((c) => ({ ...c })); fn(next[i]); onChange(next) }
  return (
    <div className="conditions">
      {list.map((c, i) => {
        const params = CONDITION_PARAMS[c.type]
        return (
          <div key={i} className="condition-row">
            <select aria-label={t('routing.edCondType')} disabled={!editable} value={c.type} onChange={(e) => set(i, (x) => { x.type = e.target.value; x.parameter = CONDITION_PARAMS[e.target.value]?.[0] ?? null })}>
              {CONDITION_TYPES.map((k) => <option key={k} value={k}>{CONDITION_LABEL[k]}</option>)}
            </select>
            {params && (
              <select aria-label={t('routing.edCondParam')} disabled={!editable} value={c.parameter ?? ''} onChange={(e) => set(i, (x) => { x.parameter = e.target.value })}>
                {params.map((p) => <option key={p} value={p}>{p}</option>)}
              </select>
            )}
            <input aria-label={t('routing.edCondLocation')} placeholder={needsLocation ? t('routing.edLocation') : t('routing.edCondLocation')} disabled={!editable} list="route-locations" value={c.locationCode ?? ''} style={{ width: 90 }}
              onChange={(e) => set(i, (x) => { x.locationCode = e.target.value.trim() || null })} />
            {editable && <Button size="sm" onClick={() => onChange(list.filter((_, k) => k !== i))}>{t('routing.edRemove')}</Button>}
          </div>
        )
      })}
      {editable && <Button size="sm" onClick={() => onChange([...list, { type: 'LocationAvailable', parameter: null, locationCode: null }])}>{t('routing.edAddCondition')}</Button>}
    </div>
  )
}
