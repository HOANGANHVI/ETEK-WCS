import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { checkPassword } from '../auth/passwordPolicy'
import { Button, PageHeader, Panel } from '../components/ui'
import { describeError } from '../i18n/vi'

export function ChangePasswordPage({ forced = false }: { forced?: boolean }) {
  const { user, setUser, logout } = useAuth()
  const navigate = useNavigate()
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [again, setAgain] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState(false)

  const problem = next ? checkPassword(next, user?.username ?? '') : null
  const mismatch = again && again !== next ? 'Mật khẩu nhập lại không khớp.' : null

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
      {forced && <div className="notice notice-warn">Tài khoản của bạn được cấp hoặc đặt lại mật khẩu bởi quản trị viên. Hãy đặt mật khẩu mới trước khi sử dụng hệ thống.</div>}
      {done && !forced && <div className="notice notice-ok" role="status">Đã đổi mật khẩu.</div>}
      {error && <div className="notice notice-bad" role="alert">{error}</div>}
      <label className="field"><span className="field-label">Mật khẩu hiện tại</span>
        <input type="password" autoComplete="current-password" value={current} onChange={(e) => setCurrent(e.target.value)} /></label>
      <label className={`field ${problem ? 'has-error' : ''}`}><span className="field-label">Mật khẩu mới</span>
        <input type="password" autoComplete="new-password" value={next} onChange={(e) => setNext(e.target.value)} />
        {problem ? <span className="field-error">{problem}</span> : <span className="field-hint">Tối thiểu 10 ký tự, gồm chữ và số.</span>}</label>
      <label className={`field ${mismatch ? 'has-error' : ''}`}><span className="field-label">Nhập lại mật khẩu mới</span>
        <input type="password" autoComplete="new-password" value={again} onChange={(e) => setAgain(e.target.value)} />
        {mismatch && <span className="field-error">{mismatch}</span>}</label>
      <div className="btn-row">
        <Button type="submit" variant="primary" disabled={busy || !current || !next || !!problem || again !== next}>{busy ? 'Đang lưu…' : 'Đổi mật khẩu'}</Button>
        {forced && <Button disabled={busy} onClick={() => void logout()}>Đăng xuất</Button>}
      </div>
    </form>
  )

  if (forced) {
    return <div className="login-page"><div className="login-card"><h1>Đổi mật khẩu</h1><p className="login-sub">{user?.displayName}</p>{form}</div></div>
  }
  return (
    <>
      <PageHeader title="Đổi mật khẩu" />
      <Panel title="Đổi mật khẩu của tôi"><div className="pad">{form}</div></Panel>
    </>
  )
}
