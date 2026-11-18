import { useState, type FormEvent } from 'react'
import { api } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { checkPassword } from '../auth/passwordPolicy'
import { Button, Panel } from '../components/ui'
import { Field } from '../pages/config/FormKit'
import { fromApiError, type Errors } from '../pages/config/formUtils'
import { useT } from '../i18n'

/**
 * Section B: change the signed-in user's own password. It uses the existing change-password API (current password verified on the server, policy enforced
 * there, hash stored only; the other sessions of the account end). The password is never kept anywhere but in these fields, which are cleared afterwards.
 */
export function SecurityPanel() {
  const t = useT()
  const { user, setUser } = useAuth()
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [again, setAgain] = useState('')
  const [busy, setBusy] = useState(false)
  const [errors, setErrors] = useState<Errors>({})
  const [general, setGeneral] = useState<string | null>(null)
  const [done, setDone] = useState(false)

  const problem = next ? checkPassword(next, user?.username ?? '') : null
  const mismatch = again && again !== next ? t('auth.cpMismatch') : null

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setBusy(true); setErrors({}); setGeneral(null); setDone(false)
    try {
      setUser(await api.changePassword(current, next))
      setDone(true); setCurrent(''); setNext(''); setAgain('')
    } catch (err) {
      const r = fromApiError(err); setErrors(r.fields); setGeneral(r.general)
    } finally { setBusy(false) }
  }

  return (
    <Panel title={t('account.secSecurity')}>
      <form className="pad pw-form" noValidate onSubmit={(e) => void submit(e)}>
        <p className="muted small">{t('account.secSecurityHint')}</p>
        {done && <div className="notice notice-ok" role="status">{t('auth.cpDone')}</div>}
        {general && <div className="notice notice-bad" role="alert">{general}</div>}
        <div className="form-grid single">
          <Field label={t('auth.cpCurrent')} error={errors.currentPassword}>
            <input type="password" autoComplete="current-password" value={current} onChange={(e) => setCurrent(e.target.value)} />
          </Field>
          <Field label={t('auth.cpNew')} hint={t('auth.cpHint')} error={errors.newPassword ?? problem ?? undefined}>
            <input type="password" autoComplete="new-password" value={next} onChange={(e) => setNext(e.target.value)} />
          </Field>
          <Field label={t('auth.cpAgain')} error={mismatch ?? undefined}>
            <input type="password" autoComplete="new-password" value={again} onChange={(e) => setAgain(e.target.value)} />
          </Field>
        </div>
        <div className="btn-row">
          <Button type="submit" variant="primary" disabled={busy || !current || !next || !!problem || again !== next}>{busy ? t('account.saving') : t('account.pwSubmit')}</Button>
        </div>
      </form>
    </Panel>
  )
}
