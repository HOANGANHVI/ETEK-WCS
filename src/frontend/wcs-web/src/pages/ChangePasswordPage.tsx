import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { checkPassword } from '../auth/passwordPolicy'
import { Button, PageHeader, Panel } from '../components/ui'
import { useT } from '../i18n'
import { describeError } from '../i18n/vi'
import { displayNameOf } from '../i18n/format'

export function ChangePasswordPage({ forced = false }: { forced?: boolean }) {
  const t = useT()
  const { user, setUser, logout } = useAuth()
  const navigate = useNavigate()
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [again, setAgain] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState(false)

  const problem = next ? checkPassword(next, user?.username ?? '') : null
  const mismatch = again && again !== next ? t('auth.cpMismatch') : null

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setBusy(true); setError(null)
    try {
      setUser(await api.changePassword(current, next))
      setDone(true); setCurrent(''); setNext(''); setAgain('')
      if (forced) navigate('/', { replace: true })
    } catch (err) { setError(describeError(err)) }
    finally { setBusy(false) }
  }

  const form = (
    <form onSubmit={(e) => void submit(e)} className="pw-form" noValidate>
      {forced && <div className="notice notice-warn">{t('auth.cpForced')}</div>}
      {done && !forced && <div className="notice notice-ok" role="status">{t('auth.cpDone')}</div>}
      {error && <div className="notice notice-bad" role="alert">{error}</div>}
      <label className="field"><span className="field-label">{t('auth.cpCurrent')}</span>
        <input type="password" autoComplete="current-password" value={current} onChange={(e) => setCurrent(e.target.value)} /></label>
      <label className={`field ${problem ? 'has-error' : ''}`}><span className="field-label">{t('auth.cpNew')}</span>
        <input type="password" autoComplete="new-password" value={next} onChange={(e) => setNext(e.target.value)} />
        {problem ? <span className="field-error">{problem}</span> : <span className="field-hint">{t('auth.cpHint')}</span>}</label>
      <label className={`field ${mismatch ? 'has-error' : ''}`}><span className="field-label">{t('auth.cpAgain')}</span>
        <input type="password" autoComplete="new-password" value={again} onChange={(e) => setAgain(e.target.value)} />
        {mismatch && <span className="field-error">{mismatch}</span>}</label>
      <div className="btn-row">
        <Button type="submit" variant="primary" disabled={busy || !current || !next || !!problem || again !== next}>{busy ? t('common.saving') : t('auth.cpTitle')}</Button>
        {forced && <Button disabled={busy} onClick={() => void logout()}>{t('auth.logout')}</Button>}
      </div>
    </form>
  )

  if (forced) {
    return <div className="login-page"><div className="login-card"><h1>{t('auth.cpTitle')}</h1><p className="login-sub">{user ? displayNameOf(user.displayName) : ''}</p>{form}</div></div>
  }
  return (
    <>
      <PageHeader title={t('auth.cpTitle')} />
      <Panel title={t('auth.cpPanel')}><div className="pad">{form}</div></Panel>
    </>
  )
}
