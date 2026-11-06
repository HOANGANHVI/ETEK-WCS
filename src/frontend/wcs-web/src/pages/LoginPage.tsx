import { useState, type FormEvent } from 'react'
import { useAuth } from '../auth/AuthContext'
import { DEMO_MODE } from '../config'
import { Button } from '../components/ui'
import { describeError } from '../i18n/vi'

const SITE_NAME = (import.meta.env.VITE_SITE_NAME as string | undefined)?.trim()

export function LoginPage() {
  const { login, notice } = useAuth()
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [show, setShow] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const submit = async (e: FormEvent, as?: string) => {
    e.preventDefault()
    setBusy(true); setError(null)
    try { await login(as ?? username.trim(), as ? '' : password) }
    catch (err) { setError(describeError(err)); setPassword('') }
    finally { setBusy(false) }
  }

  return (
    <div className="login-page">
      <div className="login-card">
        <div className="login-brand"><span className="brand-mark" aria-hidden="true">E</span><span>ETEK <b>WCS</b></span></div>
        <h1>Đăng nhập hệ thống</h1>
        <p className="login-sub">Hệ thống điều khiển kho (WCS){SITE_NAME ? ` · ${SITE_NAME}` : ''}</p>

        {notice && <div className="notice notice-warn" role="status">{notice}</div>}
        {error && <div className="notice notice-bad" role="alert">{error}</div>}

        {DEMO_MODE ? (
          <div className="login-demo">
            <div className="notice notice-info">Chế độ mô phỏng: dữ liệu và tài khoản chỉ tồn tại trong trình duyệt, không có mật khẩu thật. Chọn vai trò để xem giao diện.</div>
            <div className="btn-row">
              <Button variant="primary" disabled={busy} onClick={(e) => void submit(e as unknown as FormEvent, 'demo-admin')}>Vào với vai trò Admin (mô phỏng)</Button>
              <Button disabled={busy} onClick={(e) => void submit(e as unknown as FormEvent, 'demo-operator')}>Vào với vai trò Operator (mô phỏng)</Button>
            </div>
          </div>
        ) : (
          <form onSubmit={(e) => void submit(e)} noValidate>
            <label className="field">
              <span className="field-label">Tên đăng nhập</span>
              <input autoFocus autoComplete="username" value={username} onChange={(e) => setUsername(e.target.value)} />
            </label>
            <label className="field">
              <span className="field-label">Mật khẩu</span>
              <span className="pw-row">
                <input type={show ? 'text' : 'password'} autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} />
                <button type="button" className="pw-toggle" onClick={() => setShow((v) => !v)} aria-pressed={show}>{show ? 'Ẩn' : 'Hiện'}</button>
              </span>
            </label>
            <Button type="submit" variant="primary" className="login-submit" disabled={busy || !username.trim() || !password}>
              {busy ? 'Đang đăng nhập…' : 'Đăng nhập'}
            </Button>
          </form>
        )}
        <p className="login-foot">Tài khoản do quản trị viên cấp. Bạn không thể tự đăng ký.</p>
      </div>
    </div>
  )
}
