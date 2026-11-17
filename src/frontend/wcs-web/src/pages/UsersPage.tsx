import { useCallback, useState } from 'react'
import { api } from '../api/client'
import type { Role, UserRow } from '../api/authTypes'
import { useAuth } from '../auth/AuthContext'
import { Async, Button, PageHeader, Panel, Pill } from '../components/ui'
import { Modal } from '../components/Modal'
import { useApi } from '../hooks/useApi'
import { formatDateTime } from '../i18n/format'
import { ROLE_SHORT_VI } from '../i18n/vi'
import { checkPassword } from '../auth/passwordPolicy'
import { Field } from './config/FormKit'
import { fromApiError, type Errors } from './config/formUtils'

type Dialog = { kind: 'create' } | { kind: 'edit'; user: UserRow } | { kind: 'reset'; user: UserRow } | { kind: 'delete'; user: UserRow } | null

export function UsersPage() {
  const { user: me } = useAuth()
  const [search, setSearch] = useState('')
  const [dialog, setDialog] = useState<Dialog>(null)
  const [message, setMessage] = useState<string | null>(null)
  const list = useApi(useCallback(() => api.users(search.trim() || undefined), [search]))
  const done = (msg: string) => { setDialog(null); setMessage(msg); list.reload() }

  return (
    <>
      <PageHeader
        title="Quản lý tài khoản"
        actions={<Button variant="primary" onClick={() => setDialog({ kind: 'create' })}>Tạo tài khoản</Button>}
      />
      {message && <div className="notice notice-ok" role="status">{message}</div>}
      <Panel title="Danh sách tài khoản" flush actions={
        <input type="search" placeholder="Tìm tên đăng nhập hoặc họ tên" aria-label="Tìm tài khoản" value={search} onChange={(e) => setSearch(e.target.value)} />
      }>
        <Async state={list} isEmpty={(d) => d.length === 0} empty={{ title: 'Không có tài khoản nào', hint: search ? 'Thử từ khóa khác.' : undefined }}>
          {(rows) => (
            <div className="table-wrap">
              <table className="grid">
                <thead><tr><th>Tên đăng nhập</th><th>Họ tên</th><th>Vai trò</th><th>Trạng thái</th><th>Thời điểm tạo</th><th>Đăng nhập gần nhất</th><th>Thao tác</th></tr></thead>
                <tbody>
                  {rows.map((u) => (
                    <tr key={u.id}>
                      <td className="mono"><strong>{u.username}</strong>{u.username === me?.username && <span className="muted"> (bạn)</span>}</td>
                      <td>{u.displayName}</td>
                      <td><Pill tone={u.role === 'Admin' ? 'info' : 'muted'} dot={false}>{ROLE_SHORT_VI[u.role]}</Pill></td>
                      <td>
                        {u.isActive ? <Pill tone="ok">Đang hoạt động</Pill> : <Pill tone="bad">Đã vô hiệu hóa</Pill>}
                        {u.lockedUntil && <span className="flag" title={`Khóa tạm đến ${formatDateTime(u.lockedUntil)}`}> 🔒</span>}
                        {u.mustChangePassword && <small className="muted"> · chờ đổi mật khẩu</small>}
                      </td>
                      <td className="nowrap">{formatDateTime(u.createdAt)}</td>
                      <td className="nowrap">{u.lastLoginAt ? formatDateTime(u.lastLoginAt) : 'Chưa đăng nhập'}</td>
                      <td className="nowrap">
                        <Button size="sm" onClick={() => setDialog({ kind: 'edit', user: u })}>Sửa</Button>{' '}
                        <Button size="sm" onClick={() => setDialog({ kind: 'reset', user: u })}>Đặt lại mật khẩu</Button>{' '}
                        <Button size="sm" variant="danger" disabled={u.username === me?.username} title={u.username === me?.username ? 'Không thể tự xóa tài khoản của mình' : undefined} onClick={() => setDialog({ kind: 'delete', user: u })}>Xóa</Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Async>
      </Panel>

      {dialog?.kind === 'create' && <CreateDialog onClose={() => setDialog(null)} onDone={() => done('Đã tạo tài khoản. Người dùng phải đổi mật khẩu ở lần đăng nhập đầu tiên.')} />}
      {dialog?.kind === 'edit' && <EditDialog user={dialog.user} isSelf={dialog.user.username === me?.username} onClose={() => setDialog(null)} onDone={() => done('Đã cập nhật tài khoản.')} />}
      {dialog?.kind === 'delete' && <DeleteDialog user={dialog.user} onClose={() => setDialog(null)} onDone={() => done(`Đã xóa tài khoản ${dialog.user.username}.`)} />}
      {dialog?.kind === 'reset' && <ResetDialog user={dialog.user} onClose={() => setDialog(null)} onDone={() => done(`Đã đặt lại mật khẩu của ${dialog.user.username}. Mọi phiên đăng nhập hiện tại của tài khoản này đã bị đăng xuất.`)} />}
    </>
  )
}

function RoleSelect({ value, onChange, disabled }: { value: Role; onChange: (r: Role) => void; disabled?: boolean }) {
  return (
    <select value={value} disabled={disabled} onChange={(e) => onChange(e.target.value as Role)}>
      <option value="Operator">Operator – vận hành</option>
      <option value="Admin">Admin – quản trị</option>
    </select>
  )
}

function useSubmit(action: () => Promise<unknown>, onDone: () => void) {
  const [busy, setBusy] = useState(false)
  const [errors, setErrors] = useState<Errors>({})
  const [general, setGeneral] = useState<string | null>(null)
  const run = async () => {
    setBusy(true); setErrors({}); setGeneral(null)
    try { await action(); onDone() }
    catch (e) { const r = fromApiError(e); setErrors(r.fields); setGeneral(r.general) }
    finally { setBusy(false) }
  }
  return { busy, errors, general, run }
}

function CreateDialog({ onClose, onDone }: { onClose: () => void; onDone: () => void }) {
  const [username, setUsername] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [role, setRole] = useState<Role>('Operator')
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [active, setActive] = useState(true)
  const pwProblem = password ? checkPassword(password, username) : null
  const mismatch = confirm && confirm !== password ? 'Mật khẩu xác nhận không khớp.' : null
  const s = useSubmit(() => api.createUser({ username: username.trim(), displayName: displayName.trim(), role, password, isActive: active }), onDone)
  return (
    <Modal title="Tạo tài khoản" onClose={onClose} footer={<>
      <Button onClick={onClose}>Bỏ qua</Button>
      <Button variant="primary" disabled={s.busy || !username.trim() || !displayName.trim() || !password || !!pwProblem || confirm !== password} onClick={() => void s.run()}>Tạo tài khoản</Button>
    </>}>
      {s.general && <div className="notice notice-bad" role="alert">{s.general}</div>}
      <div className="form-grid single">
        <Field label="Tên đăng nhập" error={s.errors.username} hint="Chữ thường, số, dấu chấm, gạch dưới hoặc gạch ngang (3–50 ký tự).">
          <input value={username} onChange={(e) => setUsername(e.target.value)} autoComplete="off" /></Field>
        <Field label="Họ tên hiển thị" error={s.errors.displayName}><input value={displayName} onChange={(e) => setDisplayName(e.target.value)} /></Field>
        <Field label="Vai trò" error={s.errors.role}><RoleSelect value={role} onChange={setRole} /></Field>
        <Field label="Mật khẩu ban đầu" error={s.errors.password ?? pwProblem ?? undefined} hint="Người dùng bắt buộc đổi mật khẩu khi đăng nhập lần đầu.">
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="new-password" /></Field>
        <Field label="Xác nhận mật khẩu" error={mismatch ?? undefined}>
          <input type="password" value={confirm} onChange={(e) => setConfirm(e.target.value)} autoComplete="new-password" /></Field>
        <label className="check"><input type="checkbox" checked={active} onChange={(e) => setActive(e.target.checked)} /> Kích hoạt tài khoản (được phép đăng nhập)</label>
      </div>
    </Modal>
  )
}

function EditDialog({ user, isSelf, onClose, onDone }: { user: UserRow; isSelf: boolean; onClose: () => void; onDone: () => void }) {
  const [displayName, setDisplayName] = useState(user.displayName)
  const [role, setRole] = useState<Role>(user.role)
  const [active, setActive] = useState(user.isActive)
  const s = useSubmit(() => api.updateUser(user.id, { displayName: displayName.trim(), role, isActive: active }), onDone)
  return (
    <Modal title={`Sửa tài khoản ${user.username}`} onClose={onClose} footer={<>
      <Button onClick={onClose}>Bỏ qua</Button>
      <Button variant="primary" disabled={s.busy || !displayName.trim()} onClick={() => void s.run()}>Lưu thay đổi</Button>
    </>}>
      {s.general && <div className="notice notice-bad" role="alert">{s.general}</div>}
      {isSelf && <div className="notice notice-info">Đây là tài khoản của bạn: không thể tự đổi vai trò hoặc tự vô hiệu hóa.</div>}
      <div className="form-grid single">
        <Field label="Họ tên hiển thị" error={s.errors.displayName}><input value={displayName} onChange={(e) => setDisplayName(e.target.value)} /></Field>
        <Field label="Vai trò" error={s.errors.role}><RoleSelect value={role} onChange={setRole} disabled={isSelf} /></Field>
        <label className="check"><input type="checkbox" checked={active} disabled={isSelf} onChange={(e) => setActive(e.target.checked)} /> Tài khoản đang hoạt động. Bỏ chọn để khóa tài khoản (không đăng nhập được, phiên hiện tại bị ngắt).</label>
      </div>
    </Modal>
  )
}

function ResetDialog({ user, onClose, onDone }: { user: UserRow; onClose: () => void; onDone: () => void }) {
  const [password, setPassword] = useState('')
  const problem = password ? checkPassword(password, user.username) : null
  const s = useSubmit(() => api.resetPassword(user.id, password), onDone)
  return (
    <Modal title={`Đặt lại mật khẩu: ${user.username}`} onClose={onClose} footer={<>
      <Button onClick={onClose}>Bỏ qua</Button>
      <Button variant="primary" disabled={s.busy || !password || !!problem} onClick={() => void s.run()}>Đặt lại mật khẩu</Button>
    </>}>
      {s.general && <div className="notice notice-bad" role="alert">{s.general}</div>}
      <div className="form-grid single">
        <Field label="Mật khẩu tạm thời mới" error={s.errors.newPassword ?? problem ?? undefined} hint="Người dùng phải đổi mật khẩu ở lần đăng nhập kế tiếp; các phiên hiện tại bị đăng xuất.">
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="new-password" /></Field>
      </div>
    </Modal>
  )
}

function DeleteDialog({ user, onClose, onDone }: { user: UserRow; onClose: () => void; onDone: () => void }) {
  const s = useSubmit(() => api.deleteUser(user.id), onDone)
  return (
    <Modal title={`Xóa tài khoản ${user.username}`} onClose={onClose} footer={<>
      <Button onClick={onClose}>Bỏ qua</Button>
      <Button variant="danger" disabled={s.busy} onClick={() => void s.run()}>Xác nhận xóa</Button>
    </>}>
      {s.general && <div className="notice notice-bad" role="alert">{s.general}</div>}
      <p className="dialog-hint">
        Bạn sắp xóa vĩnh viễn tài khoản <strong>{user.username}</strong> ({user.displayName}). Thao tác không thể hoàn tác.
        Tài khoản đã có lịch sử thao tác, hoặc là Admin đang hoạt động cuối cùng, sẽ không xóa được; khi đó hãy khóa (vô hiệu hóa) tài khoản.
      </p>
    </Modal>
  )
}
