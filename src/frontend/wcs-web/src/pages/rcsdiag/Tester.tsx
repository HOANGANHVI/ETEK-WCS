import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { api } from '../../api/client'
import { RCS_GROUPS, type RcsApiDef, type RcsCatalog, type RcsExecution, type RcsPreview } from '../../api/rcsDiagTypes'
import { Button, EmptyState, Panel, Pill } from '../../components/ui'
import { hasMessage, useT, type MsgKey } from '../../i18n'
import { describeError } from '../../i18n/vi'
import { FieldEditor } from './FieldEditor'
import { Inspector } from './Inspector'
import { parseJsonObject, type JsonObject } from './formUtil'
import { EFFECT_TONE } from './tones'

const COMMON = ['reqCode', 'reqTime', 'clientCode', 'tokenCode']

/** CR-032 – API Catalog + Request Builder + Inspector. Selecting an API sends nothing; only the Execute button does, and it sends at once (no approval step). */
export function Tester({ catalog, onExecuted }: { catalog: RcsCatalog; onExecuted: () => void }) {
  const t = useT()
  const [search, setSearch] = useState('')
  const [group, setGroup] = useState('')
  const [adapter, setAdapter] = useState<'' | 'yes' | 'no'>('')
  const [selected, setSelected] = useState<string | null>(null)
  const def = useMemo(() => catalog.apis.find((a) => a.name === selected) ?? null, [catalog, selected])

  const list = useMemo(() => {
    const q = search.trim().toLowerCase()
    return catalog.apis.filter((a) => (!group || a.group === group) && (adapter === '' || (adapter === 'yes') === a.executable)
      && (!q || a.name.toLowerCase().includes(q) || a.function.toLowerCase().includes(q) || a.path.toLowerCase().includes(q)))
  }, [catalog, search, group, adapter])

  return (
    <div className="rd-layout">
      <Panel title={t('rcsDiag.catalogTitle')} subtitle={t('rcsDiag.catalogSub', { n: catalog.apis.length })}>
        <div className="rd-filters">
          <input type="search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t('rcsDiag.searchPh')} aria-label={t('rcsDiag.searchLabel')} />
          <select value={group} onChange={(e) => setGroup(e.target.value)} aria-label={t('rcsDiag.groupLabel')}>
            <option value="">{t('rcsDiag.allGroups')}</option>
            {RCS_GROUPS.map((g) => <option key={g} value={g}>{t(`rcsDiag.group_${g}` as MsgKey)}</option>)}
          </select>
          <select value={adapter} onChange={(e) => setAdapter(e.target.value as typeof adapter)} aria-label={t('rcsDiag.adapterLabel')}>
            <option value="">{t('rcsDiag.allAdapters')}</option>
            <option value="yes">{t('rcsDiag.adapterYes')}</option>
            <option value="no">{t('rcsDiag.adapterNo')}</option>
          </select>
        </div>
        {list.length === 0 && <p className="muted pad">{t('rcsDiag.noMatch')}</p>}
        {RCS_GROUPS.map((g) => {
          const items = list.filter((a) => a.group === g)
          if (!items.length) return null
          return (
            <div key={g} className="rd-cat-group">
              <h3>{t(`rcsDiag.group_${g}` as MsgKey)} <span className="muted small">({items.length})</span></h3>
              <ul className="rd-cat-list">
                {items.map((a) => (
                  <li key={a.name}>
                    <button type="button" className={`rd-cat-item ${a.name === selected ? 'active' : ''}`} aria-pressed={a.name === selected} onClick={() => setSelected(a.name)}>
                      <span className="rd-cat-name mono">{a.name}</span>
                      <Pill tone={EFFECT_TONE[a.effect]} dot={false}>{t(`rcsDiag.effectShort_${a.effect}` as MsgKey)}</Pill>
                      <span className="rd-cat-fn muted small">{a.function}</span>
                      <span className="rd-cat-meta muted small mono">{a.method} {a.path}</span>
                      {!a.executable && <Pill tone="muted" dot={false}>{t('rcsDiag.noAdapter')}</Pill>}
                    </button>
                  </li>
                ))}
              </ul>
            </div>
          )
        })}
      </Panel>
      <div className="stack">
        {def ? <Builder key={def.name} def={def} onExecuted={onExecuted} /> : <Panel><EmptyState title={t('rcsDiag.pickApi')} hint={t('rcsDiag.pickApiHint')} /></Panel>}
      </div>
    </div>
  )
}

function Builder({ def, onExecuted }: { def: RcsApiDef; onExecuted: () => void }) {
  const t = useT()
  const [mode, setMode] = useState<'form' | 'json'>('form')
  const [body, setBody] = useState<JsonObject>({})
  const [text, setText] = useState('{}')
  const [jsonError, setJsonError] = useState<string | null>(null)
  const [preview, setPreview] = useState<RcsPreview | null>(null)
  const [previewError, setPreviewError] = useState<string | null>(null)
  const [running, setRunning] = useState(false)
  const [result, setResult] = useState<RcsExecution | null>(null)
  const [runError, setRunError] = useState<string | null>(null)
  const seq = useRef(0)

  const commonFields = def.fields.filter((f) => COMMON.includes(f.name))
  const ownFields = def.fields.filter((f) => !COMMON.includes(f.name))

  // form -> JSON text (only while the form is the editor)
  const setFormBody = useCallback((next: JsonObject) => { setBody(next); setText(JSON.stringify(next, null, 2)); setJsonError(null) }, [])
  const onText = (v: string) => {
    setText(v)
    const p = parseJsonObject(v)
    if (p.value) { setBody(p.value); setJsonError(null) } else setJsonError(p.error)
  }

  // the server checks the body against the document and shows exactly what would be sent (it never calls the RCS)
  useEffect(() => {
    if (jsonError) return
    const mine = ++seq.current
    const timer = setTimeout(() => {
      api.rcsDiagPreview(def.name, body)
        .then((p) => { if (mine === seq.current) { setPreview(p); setPreviewError(null) } })
        .catch((e: unknown) => { if (mine === seq.current) setPreviewError(describeError(e)) })
    }, 350)
    return () => clearTimeout(timer)
  }, [def.name, body, jsonError])

  const errors = preview?.errors ?? []
  const warnings = preview?.warnings ?? []
  const canRun = !running && !jsonError && !!preview && preview.valid && !preview.configProblem

  const run = async () => {
    setRunning(true); setRunError(null)
    try {
      setResult(await api.rcsDiagExecute(def.name, body))
    } catch (e) {
      setResult(null); setRunError(describeError(e))
    } finally {
      setRunning(false); onExecuted()
    }
  }

  const set = (name: string, v: unknown) => { const next = { ...body }; if (v === undefined) delete next[name]; else next[name] = v; setFormBody(next) }
  const knownRoot = (path: string) => def.fields.some((f) => path === f.name || path.startsWith(f.name + '.') || path.startsWith(f.name + '['))

  return (
    <>
      <Panel title={def.name} subtitle={`${t('rcsDiag.docSection', { s: def.section })} · ${def.method} ${def.path}`}
        actions={<Pill tone={EFFECT_TONE[def.effect]}>{t(`rcsDiag.effectShort_${def.effect}` as MsgKey)}</Pill>}>
        <p>{def.function}</p>
        <ImpactNotice def={def} body={body} />
        {def.gaps.length > 0 && (
          <details className="rd-gaps">
            <summary>{t('rcsDiag.gapsTitle', { n: def.gaps.length })}</summary>
            <ul>{def.gaps.map((g) => <li key={g}>{g}</li>)}</ul>
          </details>
        )}
      </Panel>

      <Panel title={t('rcsDiag.builderTitle')} subtitle={t('rcsDiag.builderSub')}
        actions={<div className="rd-tabs" role="tablist">
          <button type="button" role="tab" aria-selected={mode === 'form'} className={mode === 'form' ? 'active' : ''} onClick={() => setMode('form')}>{t('rcsDiag.modeForm')}</button>
          <button type="button" role="tab" aria-selected={mode === 'json'} className={mode === 'json' ? 'active' : ''} onClick={() => setMode('json')}>{t('rcsDiag.modeJson')}</button>
        </div>}>
        <div className="rd-toolbar">
          <Button size="sm" onClick={() => { try { setFormBody(JSON.parse(def.sample) as JsonObject) } catch { setFormBody({}) } }}>{t('rcsDiag.fillSample')}</Button>
          <Button size="sm" variant="ghost" onClick={() => setFormBody({})}>{t('rcsDiag.clearForm')}</Button>
        </div>
        {mode === 'form' ? (
          <div className="rd-form">
            <details className="rd-common">
              <summary>{t('rcsDiag.commonFields')}</summary>
              <div className="form-grid">
                {commonFields.map((f) => (
                  <FieldEditor key={f.name} field={f} value={body[f.name]} path={f.name} errors={errors} warnings={warnings} secret={f.name === 'tokenCode'} onChange={(v) => set(f.name, v)} />
                ))}
              </div>
              <p className="muted small">{t('rcsDiag.commonHint')}</p>
            </details>
            <div className="form-grid">
              {ownFields.map((f) => <FieldEditor key={f.name} field={f} value={body[f.name]} path={f.name} errors={errors} warnings={warnings} onChange={(v) => set(f.name, v)} />)}
            </div>
            {errors.filter((e) => !knownRoot(e.path) || e.path.includes('|')).map((e) => (
              <div key={e.path + e.code} className="notice notice-bad" role="alert"><span className="mono">{e.path}</span>: {e.message}</div>
            ))}
          </div>
        ) : (
          <label className="field wide">
            <span className="field-label">{t('rcsDiag.jsonLabel')}</span>
            <textarea className="rd-json mono" rows={14} spellCheck={false} value={text} onChange={(e) => onText(e.target.value)} aria-invalid={!!jsonError} />
            {jsonError && <span className="field-error" role="alert">{t('rcsDiag.jsonInvalid', { error: jsonError })}</span>}
            {errors.map((e) => <span key={e.path + e.code} className="field-error" role="alert"><span className="mono">{e.path}</span>: {e.message}</span>)}
          </label>
        )}
      </Panel>

      <Panel title={t('rcsDiag.previewTitle')} subtitle={t('rcsDiag.previewSub')}>
        {previewError && <div className="notice notice-bad" role="alert">{previewError}</div>}
        {preview?.configProblem && <div className="notice notice-warn" role="alert">{t('rcsDiag.configProblem')} {preview.configProblem}</div>}
        {preview ? (
          <>
            <p className="mono small">{preview.method} {preview.endpoint ?? '—'}</p>
            <pre className="rd-code" tabIndex={0}>{preview.body}</pre>
            <p className="small">
              {preview.valid ? <Pill tone="ok">{t('rcsDiag.valid')}</Pill> : <Pill tone="bad">{t('rcsDiag.invalid', { n: preview.errors.length })}</Pill>}
              {warnings.length > 0 && <> <Pill tone="warn">{t('rcsDiag.warnCount', { n: warnings.length })}</Pill></>}
            </p>
          </>
        ) : <p className="muted pad">{t('rcsDiag.previewWait')}</p>}
        <ImpactNotice def={def} body={body} />
        <div className="rd-run">
          <Button variant={def.effect === 'ReadOnly' ? 'primary' : 'danger'} disabled={!canRun} onClick={() => void run()}>
            {running ? t('rcsDiag.running') : t('rcsDiag.execute')}
          </Button>
          <span className="muted small">{t('rcsDiag.executeHint')}</span>
        </div>
        {runError && <div className="notice notice-bad" role="alert">{runError}</div>}
      </Panel>

      {result && <Panel title={t('rcsDiag.resultTitle')}><Inspector exec={result} /></Panel>}
    </>
  )
}

const NOTICE_CLASS: Record<RcsApiDef['effect'], string> = { ReadOnly: 'notice-info', Unverified: 'notice-warn', Mutating: 'notice-warn', Critical: 'notice-bad' }

/** The risk class of the API, shown BEFORE the Execute button (the button still sends at once: this is information, not a second step). */
function ImpactNotice({ def, body }: { def: RcsApiDef; body: JsonObject }) {
  const t = useT()
  const extra = `rcsDiag.apiWarn_${def.name}` as MsgKey
  const allAmr = (def.name === 'stopRobot' || def.name === 'resumeRobot') && String(body.robotCount ?? '') === '-1'
  return (
    <div className={`notice ${NOTICE_CLASS[def.effect]} rd-impact rd-impact-${def.effect}`} role="note" data-effect={def.effect}>
      <strong>{t(`rcsDiag.effectShort_${def.effect}` as MsgKey)}.</strong> {t(`rcsDiag.effect_${def.effect}` as MsgKey)}
      {hasMessage(extra) && <div className="rd-impact-extra">{t(extra)}</div>}
      {allAmr && <div className="rd-impact-extra rd-impact-all"><strong>{t('rcsDiag.allAmrWarn')}</strong></div>}
    </div>
  )
}
