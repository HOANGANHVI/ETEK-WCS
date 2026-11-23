import { useState } from 'react'
import { api } from '../../api/client'
import type { RcsParameter, RcsParameters } from '../../api/rcsDiagTypes'
import { Button, Panel, Pill } from '../../components/ui'
import { useT, type MsgKey } from '../../i18n'
import { formatDateTime } from '../../i18n/format'
import { describeError } from '../../i18n/vi'

function ParamRow({ p, onSaved }: { p: RcsParameter; onSaved: () => void }) {
  const t = useT()
  const [value, setValue] = useState(p.savedValue ?? '')
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null)
  const save = async (v: string) => {
    setBusy(true); setMsg(null)
    try {
      await api.rcsDiagSaveParameter(p.key, v)
      setMsg({ ok: true, text: t('rcsDiag.paramSaved') })
      onSaved()
    } catch (e) {
      setMsg({ ok: false, text: describeError(e) })
    } finally {
      setBusy(false)
    }
  }
  return (
    <div className="rd-param">
      <div>
        <strong>{t(`rcsDiag.param_${p.key}` as MsgKey)}</strong> <span className="muted small mono">{p.key}</span>
        <div className="muted small">{t(`rcsDiag.paramHelp_${p.key}` as MsgKey)}</div>
        <div className="muted small">{t('rcsDiag.paramAppliesTo')} <span className="mono">{p.fields.join(', ')}</span></div>
        <div className="small">
          <Pill tone={p.source === 'None' ? 'warn' : 'ok'}>{t(`rcsDiag.paramSource_${p.source}` as MsgKey)}</Pill>
          {p.effectiveValue && <> <span className="mono">{p.effectiveValue}</span></>}
          {p.source === 'Saved' && p.updatedBy && p.updatedAt && <span className="muted"> · {t('rcsDiag.paramUpdated', { user: p.updatedBy, time: formatDateTime(p.updatedAt) })}</span>}
          {p.configuredDefault && p.source === 'Saved' && <span className="muted"> · {t('rcsDiag.paramConfigured', { value: p.configuredDefault })}</span>}
        </div>
      </div>
      <form className="rd-param-edit" onSubmit={(e) => { e.preventDefault(); void save(value) }}>
        <input value={value} maxLength={p.maxLength} placeholder={p.configuredDefault ?? t('rcsDiag.paramPh')} aria-label={t(`rcsDiag.param_${p.key}` as MsgKey)} onChange={(e) => setValue(e.target.value)} autoComplete="off" spellCheck={false} />
        <Button type="submit" variant="primary" size="sm" disabled={busy}>{t('rcsDiag.paramSave')}</Button>
        <Button size="sm" variant="ghost" disabled={busy || p.source !== 'Saved'} onClick={() => { setValue(''); void save('') }}>{t('rcsDiag.paramClear')}</Button>
      </form>
      {msg && <div className={`field-hint ${msg.ok ? 'text-ok' : 'field-error'}`} role={msg.ok ? 'status' : 'alert'}>{msg.text}</div>}
    </div>
  )
}

/** CR-032 – the deployment-wide parameters of the RCS APIs, in three groups: confirmed defaults (MAP ID), values the project has not given (the Admin enters and may save them), and fields whose meaning is not determined. */
export function Params({ params, onSaved }: { params: RcsParameters; onSaved: () => void }) {
  const t = useT()
  const confirmed = params.items.filter((p) => p.status === 'Confirmed')
  const userInput = params.items.filter((p) => p.status === 'UserInput')
  return (
    <div className="stack">
      <div className="notice notice-info" role="note">{t('rcsDiag.paramsNote')}</div>
      <Panel title={t('rcsDiag.paramsConfirmed')} subtitle={t('rcsDiag.paramsConfirmedSub')}>
        {confirmed.map((p) => <ParamRow key={p.key} p={p} onSaved={onSaved} />)}
      </Panel>
      <Panel title={t('rcsDiag.paramsUserInput')} subtitle={t('rcsDiag.paramsUserInputSub')}>
        {userInput.map((p) => <ParamRow key={p.key} p={p} onSaved={onSaved} />)}
      </Panel>
      <Panel title={t('rcsDiag.paramsUndetermined')} subtitle={t('rcsDiag.paramsUndeterminedSub')}>
        <ul className="rd-undet">
          {params.undetermined.map((u) => <li key={u}><span className="mono">{u}</span></li>)}
        </ul>
      </Panel>
    </div>
  )
}
