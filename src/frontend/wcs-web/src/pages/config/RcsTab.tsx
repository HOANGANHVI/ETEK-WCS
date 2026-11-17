import { useCallback, useState } from 'react'
import { api } from '../../api/client'
import type { RcsConfig, SaveRcsBody } from '../../api/configTypes'
import { Async, Button, Panel, Pill } from '../../components/ui'
import { useApi } from '../../hooks/useApi'
import { formatDateTime } from '../../i18n/format'
import { AUTH_MODE_VI } from '../../i18n/config'
import { RCS_AUTH_MODES, validateRcs } from '../../settings/validate'
import { Field, Switch, TestResultPanel } from './FormKit'
import { fromApiError, fromNum, toErrors, toInt, toStr, type Errors } from './formUtils'

interface Form {
  enabled: boolean; systemName: string; host: string; port: string; baseUrl: string; authMode: RcsConfig['settings']['authMode']
  username: string; timeoutMs: string
  password: string; clearPassword: boolean; token: string; clearToken: boolean   // write-only: never pre-filled
}

const toForm = (c: RcsConfig): Form => ({
  enabled: c.enabled, systemName: c.settings.systemName ?? '', host: c.settings.host ?? '', port: fromNum(c.settings.port), baseUrl: c.settings.baseUrl ?? '',
  authMode: c.settings.authMode, username: c.settings.username ?? '', timeoutMs: fromNum(c.settings.timeoutMs),
  password: '', clearPassword: false, token: '', clearToken: false,
})

const toBody = (f: Form): SaveRcsBody => ({
  enabled: f.enabled, systemName: toStr(f.systemName), host: toStr(f.host), port: toInt(f.port), baseUrl: toStr(f.baseUrl), authMode: f.authMode,
  username: toStr(f.username), timeoutMs: toInt(f.timeoutMs), password: toStr(f.password), clearPassword: f.clearPassword,
  token: toStr(f.token), clearToken: f.clearToken, updatedBy: 'operator',
})

export function RcsTab() {
  const state = useApi(useCallback(() => api.rcsConfig(), []))
  return (
    <Async state={state} empty={{ title: 'Không có dữ liệu cấu hình' }}>
      {(cfg) => <RcsForm key={cfg.updatedAt ?? 'new'} initial={cfg} onChanged={state.reload} />}
    </Async>
  )
}

function RcsForm({ initial, onChanged }: { initial: RcsConfig; onChanged: () => void }) {
  const [form, setForm] = useState<Form>(() => toForm(initial))
  const [errors, setErrors] = useState<Errors>({})
  const [general, setGeneral] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)
  const [busy, setBusy] = useState<'save' | 'test' | null>(null)

  // secrets that will exist after saving (for instant validation); the plaintext is never read back from the server
  const willHavePassword = !form.clearPassword && (form.password.trim() !== '' || initial.secrets.hasPassword)
  const willHaveToken = !form.clearToken && (form.token.trim() !== '' || initial.secrets.hasToken)
  const dirty = JSON.stringify(form) !== JSON.stringify(toForm(initial))
  const set = <K extends keyof Form>(k: K, v: Form[K]) => { setForm((f) => ({ ...f, [k]: v })); setSaved(false) }

  const save = async () => {
    setGeneral(null)
    const local = validateRcs(toBody(form), willHavePassword, willHaveToken)
    if (local.length) { setErrors(toErrors(local)); setGeneral('Dữ liệu chưa hợp lệ. Hãy sửa các trường được đánh dấu.'); return }
    setErrors({}); setBusy('save')
    try { await api.saveRcs(toBody(form)); setSaved(true); onChanged() }
    catch (e) { const r = fromApiError(e); setErrors(r.fields); setGeneral(r.general) }
    finally { setBusy(null) }
  }

  const test = async () => {
    setBusy('test'); setGeneral(null)
    try { await api.testRcs(); onChanged() }
    catch (e) { setGeneral(fromApiError(e).general) }
    finally { setBusy(null) }
  }

  return (
    <div className="cfg-grid">
      <Panel title="Kết nối HIK RCS-2000" subtitle={initial.updatedAt ? `Lưu lần cuối ${formatDateTime(initial.updatedAt)} · ${initial.updatedBy ?? ''}` : 'Chưa có cấu hình nào được lưu'}>
        <Switch checked={form.enabled} onChange={(v) => set('enabled', v)} label="Bật kết nối RCS" />
        <div className="form-grid">
          <Field label="Tên hệ thống" error={errors.systemName}><input value={form.systemName} onChange={(e) => set('systemName', e.target.value)} maxLength={100} /></Field>
          <Field label="Địa chỉ IP hoặc hostname" error={errors.host}><input value={form.host} onChange={(e) => set('host', e.target.value)} autoComplete="off" /></Field>
          <Field label="Port" error={errors.port} hint="1–65535."><input value={form.port} onChange={(e) => set('port', e.target.value)} inputMode="numeric" /></Field>
          <Field label="Base URL" error={errors.baseUrl} hint="Tùy chọn, ví dụ https://tên-máy:cổng/đường-dẫn-gốc. Host phải trùng với địa chỉ ở trên. Không nhập tài khoản vào URL."><input value={form.baseUrl} onChange={(e) => set('baseUrl', e.target.value)} autoComplete="off" /></Field>
          <Field label="Phương thức xác thực" error={errors.authMode}>
            <select value={form.authMode} onChange={(e) => set('authMode', e.target.value as Form['authMode'])}>
              {RCS_AUTH_MODES.map((m) => <option key={m} value={m}>{AUTH_MODE_VI[m] ?? m}</option>)}
            </select>
          </Field>
          <Field label="Timeout (ms)" error={errors.timeoutMs} hint="100–120000."><input value={form.timeoutMs} onChange={(e) => set('timeoutMs', e.target.value)} inputMode="numeric" /></Field>

          {form.authMode === 'UsernamePassword' && (
            <>
              <Field label="Tên đăng nhập" error={errors.username}><input value={form.username} onChange={(e) => set('username', e.target.value)} autoComplete="off" /></Field>
              <Field label="Mật khẩu" error={errors.password}
                hint={initial.secrets.hasPassword ? 'Đã lưu mật khẩu (được mã hóa, không hiển thị). Để trống nếu không đổi.' : 'Chỉ ghi, không bao giờ hiển thị lại.'}>
                <input type="password" value={form.password} onChange={(e) => set('password', e.target.value)} autoComplete="new-password" placeholder={initial.secrets.hasPassword ? '••••••••' : ''} />
                {initial.secrets.hasPassword && <span className="check"><input type="checkbox" checked={form.clearPassword} onChange={(e) => set('clearPassword', e.target.checked)} /> Xóa mật khẩu đã lưu</span>}
              </Field>
            </>
          )}
          {form.authMode === 'Token' && (
            <Field label="Token" error={errors.token} wide
              hint={initial.secrets.hasToken ? 'Đã lưu token (được mã hóa, không hiển thị). Để trống nếu không đổi.' : 'Chỉ ghi, không bao giờ hiển thị lại.'}>
              <input type="password" value={form.token} onChange={(e) => set('token', e.target.value)} autoComplete="new-password" placeholder={initial.secrets.hasToken ? '••••••••' : ''} />
              {initial.secrets.hasToken && <span className="check"><input type="checkbox" checked={form.clearToken} onChange={(e) => set('clearToken', e.target.checked)} /> Xóa token đã lưu</span>}
            </Field>
          )}
        </div>
        <p className="muted">
          Thông tin xác thực: {initial.secrets.hasPassword ? <Pill tone="ok">Đã lưu mật khẩu</Pill> : <Pill tone="muted">Chưa có mật khẩu</Pill>}{' '}
          {initial.secrets.hasToken ? <Pill tone="ok">Đã lưu token</Pill> : <Pill tone="muted">Chưa có token</Pill>}
        </p>
        {general && <div className="notice notice-bad" role="alert">{general}</div>}
        {saved && !dirty && <div className="notice notice-info">Đã lưu cấu hình RCS.</div>}
        <div className="btn-row">
          <Button variant="primary" disabled={!dirty || busy !== null} onClick={() => void save()}>{busy === 'save' ? 'Đang lưu…' : 'Lưu cấu hình'}</Button>
          <Button disabled={!dirty || busy !== null} onClick={() => { setForm(toForm(initial)); setErrors({}); setGeneral(null) }}>Hoàn tác thay đổi</Button>
          {dirty && <span className="muted">Có thay đổi chưa lưu.</span>}
        </div>
      </Panel>

      <Panel title="Kiểm tra kết nối">
        <div className="btn-row">
          <Button variant="primary" disabled={dirty || !initial.configured || busy !== null} onClick={() => void test()}>Kiểm tra kết nối</Button>
        </div>
        {(dirty || !initial.configured) && <p className="muted">{initial.configured ? 'Hãy lưu thay đổi trước khi kiểm tra.' : 'Hãy lưu cấu hình trước khi kiểm tra.'}</p>}
        <TestResultPanel last={initial.lastTest} running={busy === 'test'} />
      </Panel>
    </div>
  )
}
