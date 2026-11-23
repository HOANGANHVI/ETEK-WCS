import type { RcsField, RcsIssue, RcsParameters } from '../../api/rcsDiagTypes'
import { Button } from '../../components/ui'
import { useT, type MsgKey } from '../../i18n'
import { fromLines, fromText, isObject, issuesFor, toText, type JsonObject } from './formUtil'

interface Props {
  field: RcsField
  value: unknown
  onChange: (v: unknown) => void
  path: string
  errors: RcsIssue[]
  warnings: RcsIssue[]
  secret?: boolean
  /** The configuration parameters (confirmed defaults, user input, undetermined fields) of the API this field belongs to. */
  info?: { api: string; params: RcsParameters | null }
}

/** One field of the request, drawn from the API document's field table: name, type, maximum length, Required / Optional, description, enumeration. The server validates; this only places its answers under the field. */
export function FieldEditor({ field, value, onChange, path, errors, warnings, secret, info }: Props) {
  const t = useT()
  const param = info?.params?.items.find((p) => p.fields.includes(field.name)) ?? null
  const undetermined = !!info && !param && (info.params?.undetermined.includes(`${info.api}.${path}`) ?? false)
  const empty = value === undefined || value === null || value === ''
  const effective = param?.effectiveValue ?? null
  const cfg = (<>
    {param && <span className={`rd-badge rd-badge-${param.status}`}>{t(`rcsDiag.badge_${param.status}` as MsgKey)}</span>}
    {undetermined && <span className="rd-badge rd-badge-Undetermined">{t('rcsDiag.badge_Undetermined')}</span>}
  </>)
  const cfgHint = (<>
    {param?.status === 'Confirmed' && effective && <span className="field-hint">{t('rcsDiag.hintConfirmed', { value: effective })}</span>}
    {param?.status === 'UserInput' && empty && (effective
      ? <span className="field-hint">{t('rcsDiag.hintUserInputSaved')} <button type="button" className="rd-link" onClick={() => onChange(effective)}>{t('rcsDiag.useSaved', { value: effective })}</button></span>
      : <span className="field-hint rd-warn">{t('rcsDiag.hintUserInput')}</span>)}
    {undetermined && <span className="field-hint rd-warn">{t('rcsDiag.hintUndetermined')}</span>}
  </>)
  const errs = issuesFor(errors, path)
  const warns = issuesFor(warnings, path)
  const label = (
    <span className="field-label">
      <span className="mono">{field.name}</span>{' '}
      <span className={field.required ? 'rd-req' : 'rd-opt'}>{field.required ? t('rcsDiag.required') : t('rcsDiag.optional')}</span>{' '}
      <span className="muted small">{field.type}{field.maxLength ? ` ≤ ${field.maxLength}` : ''}{field.maxItems ? ` ≤ ${field.maxItems}` : ''}</span>{' '}
      {cfg}
    </span>
  )
  const meta = (<>
    <span className="field-hint">{field.description}</span>
    {cfgHint}
    {errs.map((e) => <span key={e.code + e.message} className="field-error" role="alert">{e.message}</span>)}
    {warns.map((e) => <span key={e.code + e.message} className="field-hint rd-warn">{e.message}</span>)}
  </>)

  if (field.type === 'object') {
    const obj = isObject(value) ? value : {}
    return (
      <fieldset className="rd-group">
        <legend>{label}</legend>
        <span className="field-hint">{field.description}</span>
        {errs.map((e) => <span key={e.code + e.message} className="field-error" role="alert">{e.message}</span>)}
        {(field.children ?? []).map((c) => (
          <FieldEditor key={c.name} field={c} value={obj[c.name]} path={`${path}.${c.name}`} errors={errors} warnings={warnings} info={info}
            onChange={(v) => { const next: JsonObject = { ...obj }; if (v === undefined) delete next[c.name]; else next[c.name] = v; onChange(Object.keys(next).length ? next : undefined) }} />
        ))}
      </fieldset>
    )
  }

  if (field.type === 'object[]') {
    const rows = Array.isArray(value) ? (value as unknown[]) : []
    const set = (next: unknown[]) => onChange(next.length ? next : undefined)
    return (
      <fieldset className="rd-group">
        <legend>{label}</legend>
        <span className="field-hint">{field.description}</span>
        {errs.map((e) => <span key={e.code + e.message} className="field-error" role="alert">{e.message}</span>)}
        {rows.map((row, i) => {
          const obj = isObject(row) ? row : {}
          return (
            <div key={i} className="rd-row">
              <div className="rd-row-head"><strong>#{i + 1}</strong><Button size="sm" variant="ghost" onClick={() => set(rows.filter((_, j) => j !== i))}>{t('rcsDiag.removeRow')}</Button></div>
              {(field.children ?? []).map((c) => (
                <FieldEditor key={c.name} field={c} value={obj[c.name]} path={`${path}[${i}].${c.name}`} errors={errors} warnings={warnings} info={info}
                  onChange={(v) => { const next: JsonObject = { ...obj }; if (v === undefined) delete next[c.name]; else next[c.name] = v; set(rows.map((r, j) => (j === i ? next : r))) }} />
              ))}
            </div>
          )
        })}
        <Button size="sm" onClick={() => set([...rows, {}])} disabled={field.maxItems !== null && rows.length >= field.maxItems}>{t('rcsDiag.addRow')}</Button>
      </fieldset>
    )
  }

  if (field.type === 'string[]') {
    const lines = Array.isArray(value) ? (value as unknown[]).map(String).join('\n') : ''
    return (
      <label className={`field wide ${errs.length ? 'has-error' : ''}`}>
        {label}
        <textarea rows={3} value={lines} placeholder={t('rcsDiag.onePerLine')} onChange={(e) => onChange(fromLines(e.target.value))} />
        {meta}
      </label>
    )
  }

  return (
    <label className={`field ${errs.length ? 'has-error' : ''}`}>
      {label}
      {field.enum && field.enum.length > 0 ? (
        <select value={toText(value)} onChange={(e) => onChange(fromText(field.type, e.target.value))}>
          <option value="">{t('rcsDiag.notSet')}</option>
          {field.enum.map((v) => <option key={v} value={v}>{v}</option>)}
        </select>
      ) : (
        <>
          <input type={secret ? 'password' : 'text'} autoComplete="off" spellCheck={false} value={toText(value)} onChange={(e) => onChange(fromText(field.type, e.target.value))}
            list={field.suggestions?.length ? `rd-sg-${field.name}` : undefined}
            placeholder={field.name === 'reqCode' || field.name === 'reqTime' ? t('rcsDiag.autoFilled') : field.suggestions?.length ? t('rcsDiag.typeOrPick') : undefined} />
          {field.suggestions?.length ? <datalist id={`rd-sg-${field.name}`}>{field.suggestions.map((v) => <option key={v} value={v} />)}</datalist> : null}
        </>
      )}
      {meta}
    </label>
  )
}
