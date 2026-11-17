import { useState, type FormEvent } from 'react'
import { useAuth } from '../auth/AuthContext'
import { DEMO_MODE } from '../config'
import { Button } from '../components/ui'
import { useT } from '../i18n'
import { describeError } from '../i18n/vi'
import { LangSwitch } from '../components/LangSwitch'

const SITE_NAME = (import.meta.env.VITE_SITE_NAME as string | undefined)?.trim()

export function LoginPage() {
  const t = useT()
  const { login, notice } = useAuth()
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [show, setShow] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setBusy(true); setError(null)
    try { await login(username.trim(), password) }
    catch (err) { setError(describeError(err)); setPassword('') }
    finally { setBusy(false) }
  }

  return (
    <div className="login-page">
      <div className="login-card">
        <div className="login-lang"><LangSwitch onLight /></div>
        <div className="login-brand"><span className="brand-mark" aria-hidden="true">E</span><span>ETEK <b>WCS</b></span></div>
        <h1>{t('auth.loginTitle')}</h1>
        <p className="login-sub">{t('auth.loginSub')}{SITE_NAME ? ` · ${SITE_NAME}` : ''}</p>

        {notice && <div className="notice notice-warn" role="status">{t(notice)}</div>}
        {error && <div className="notice notice-bad" role="alert">{error}</div>}

        {DEMO_MODE && <div className="notice notice-info">{t('auth.demoNotice')}</div>}
        <form onSubmit={(e) => void submit(e)} noValidate>
          <label className="field">
            <span className="field-label">{t('auth.username')}</span>
            <input autoFocus autoComplete="username" value={username} onChange={(e) => setUsername(e.target.value)} />
          </label>
          <label className="field">
            <span className="field-label">{t('auth.password')}</span>
            <span className="pw-row">
              <input type={show ? 'text' : 'password'} autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} />
              <button type="button" className="pw-toggle" onClick={() => setShow((v) => !v)} aria-pressed={show}>{show ? t('common.hide') : t('common.show')}</button>
            </span>
          </label>
          <Button type="submit" variant="primary" className="login-submit" disabled={busy || !username.trim() || !password}>
            {busy ? t('auth.authenticating') : t('auth.login')}
          </Button>
        </form>
        <p className="login-foot">{t('auth.loginFoot')}</p>
      </div>
    </div>
  )
}
