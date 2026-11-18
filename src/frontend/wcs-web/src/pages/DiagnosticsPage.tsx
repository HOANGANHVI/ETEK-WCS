import { useCallback, useState } from 'react'
import { api } from '../api/client'
import { Async, Button, PageHeader, Panel } from '../components/ui'
import { useApi } from '../hooks/useApi'
import { useT } from '../i18n'
import { describeError } from '../i18n/vi'

type Mode = '1h' | '24h' | 'custom'

/** System → Diagnostics (Admin): downloads the PLC diagnostic ZIP. Read-only; the API enforces the Admin role. */
export function DiagnosticsPage() {
  const t = useT()
  const plcs = useApi(useCallback(() => api.plcConnections(), []))
  const [plcId, setPlcId] = useState('')
  const [mode, setMode] = useState<Mode>('24h')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<{ ok: boolean; text: string } | null>(null)

  const run = async () => {
    setResult(null)
    let range: { quick?: string; from?: string; to?: string }
    if (mode === 'custom') {
      if (!from || !to || new Date(to) <= new Date(from)) { setResult({ ok: false, text: t('diagnostics.rangeInvalid') }); return }
      range = { from: new Date(from).toISOString(), to: new Date(to).toISOString() }
    } else range = { quick: mode }
    setBusy(true)
    try {
      await api.downloadPlcDiagnostics({ plcId: plcId || undefined, ...range })
      setResult({ ok: true, text: t('diagnostics.done') })
    } catch (e) {
      setResult({ ok: false, text: `${t('diagnostics.failed')} ${describeError(e)}` })
    } finally { setBusy(false) }
  }

  return (
    <>
      <PageHeader title={t('diagnostics.title')} description={t('diagnostics.description')} />
      {result && <div className={`notice ${result.ok ? 'notice-ok' : 'notice-bad'}`} role={result.ok ? 'status' : 'alert'}>{result.text}</div>}
      <Panel title={t('diagnostics.panel')}>
        <Async state={plcs} isEmpty={(d) => d.length === 0} empty={{ title: t('diagnostics.noPlc') }}>
          {(rows) => (
            <div className="form-grid">
              <label className="field">
                <span>{t('diagnostics.scope')}</span>
                <select value={plcId} onChange={(e) => setPlcId(e.target.value)}>
                  <option value="">{t('diagnostics.scopeAll')}</option>
                  {rows.map((p) => <option key={p.plcId} value={p.plcId}>{p.plcId} — {p.name}</option>)}
                </select>
              </label>
              <fieldset className="field">
                <legend>{t('diagnostics.range')}</legend>
                <div role="radiogroup" aria-label={t('diagnostics.rangeAria')} className="btn-row">
                  {(['1h', '24h', 'custom'] as Mode[]).map((m) => (
                    <Button key={m} variant={mode === m ? 'primary' : 'secondary'} role="radio" aria-checked={mode === m} onClick={() => setMode(m)}>
                      {m === '1h' ? t('diagnostics.quick1h') : m === '24h' ? t('diagnostics.quick24h') : t('diagnostics.custom')}
                    </Button>
                  ))}
                </div>
              </fieldset>
              {mode === 'custom' && (
                <div className="btn-row">
                  <label className="field"><span>{t('diagnostics.from')}</span><input type="datetime-local" value={from} onChange={(e) => setFrom(e.target.value)} /></label>
                  <label className="field"><span>{t('diagnostics.to')}</span><input type="datetime-local" value={to} onChange={(e) => setTo(e.target.value)} /></label>
                </div>
              )}
              <div><Button variant="primary" disabled={busy} onClick={run}>{busy ? t('diagnostics.exporting') : t('diagnostics.export')}</Button></div>
            </div>
          )}
        </Async>
      </Panel>
      <Panel title={t('diagnostics.containsTitle')}>
        <ul>
          <li>{t('diagnostics.contains1')}</li><li>{t('diagnostics.contains2')}</li><li>{t('diagnostics.contains3')}</li><li>{t('diagnostics.contains4')}</li>
        </ul>
        <p className="muted">{t('diagnostics.privacy')}</p>
      </Panel>
    </>
  )
}
