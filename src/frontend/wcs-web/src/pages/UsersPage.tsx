import { useCallback, useState } from 'react'
import { api } from '../api/client'
import type { Role, UserRow } from '../api/authTypes'
import { useAuth } from '../auth/AuthContext'
import { Async, Button, PageHeader, Panel, Pill } from '../components/ui'
import { Modal } from '../components/Modal'
import { useApi } from '../hooks/useApi'
import { useT } from '../i18n'
import { Rich } from '../components/Rich'
import { displayNameOf, formatDateTime } from '../i18n/format'
import { ROLE_SHORT_VI } from '../i18n/vi'
import { checkPassword } from '../auth/passwordPolicy'
import { Field } from './config/FormKit'
import { fromApiError, type Errors } from './config/formUtils'

type Dialog = { kind: 'create' } | { kind: 'edit'; user: UserRow } | { kind: 'reset'; user: UserRow } | { kind: 'delete'; user: UserRow } | null

export function UsersPage() {
  const t = useT()
  const { user: me } = useAuth()
  const [search, setSearch] = useState('')
  const [dialog, setDialog] = useState<Dialog>(null)
  const [message, setMessage] = useState<string | null>(null)
  const list = useApi(useCallback(() => api.users(search.trim() || undefined), [search]))
  const done = (msg: string) => { setDialog(null); setMessage(msg); list.reload() }

  return (
    <>
      <PageHeader
        title={t('users.title')}
        actions={<Button variant="primary" onClick={() => setDialog({ kind: 'create' })}>{t('users.create')}</Button>}
      />
      {message && <div className="notice notice-ok" role="status">{message}</div>}
      <Panel title={t('users.listPanel')} flush actions={
        <input type="search" placeholder={t('users.searchPh')} aria-label={t('users.searchAria')} value={search} onChange={(e) => setSearch(e.target.value)} />
      }>
        <Async state={list} isEmpty={(d) => d.length === 0} empty={{ title: t('users.none'), hint: search ? t('users.noneHint') : undefined }}>
          {(rows) => (
            <div className="table-wrap">
              <table className="grid">
                <thead><tr><th>{t('users.colUsername')}</th><th>{t('users.colName')}</th><th>{t('users.colOrg')}</th><th>{t('users.colRole')}</th><th>{t('users.colStatus')}</th><th>{t('users.colCreated')}</th><th>{t('users.colLastLogin')}</th><th>{t('users.colActions')}</th></tr></thead>
                <tbody>
                  {rows.map((u) => (
                    <tr key={u.id}>
                      <td className="mono"><strong>{u.username}</strong>{u.username === me?.username && <span className="muted">{t('users.you')}</span>}</td>
                      <td>{displayNameOf(u.displayName)}{u.email && <small className="muted block">{u.email}</small>}</td>
                      <td>{u.position || u.department ? <>{u.position ?? <span className="muted">{t('users.notProvided')}</span>}<small className="muted block">{u.department ?? t('users.notProvided')}</small></> : <span className="muted">{t('users.notProvided')}</span>}</td>
                      <td><Pill tone={u.role === 'Admin' ? 'info' : 'muted'} dot={false}>{ROLE_SHORT_VI[u.role]}</Pill></td>
                      <td>
                        {u.isActive ? <Pill tone="ok">{t('users.active')}</Pill> : <Pill tone="bad">{t('users.disabled')}</Pill>}
                        {u.lockedUntil && <span className="flag" title={t('users.lockedUntil', { time: formatDateTime(u.lockedUntil) })}> 🔒</span>}
                        {u.mustChangePassword && <small className="muted">{t('users.mustChange')}</small>}
                      </td>
                      <td className="nowrap">{formatDateTime(u.createdAt)}</td>
                      <td className="nowrap">{u.lastLoginAt ? formatDateTime(u.lastLoginAt) : t('users.neverLoggedIn')}</td>
                      <td className="nowrap">
                        <Button size="sm" onClick={() => setDialog({ kind: 'edit', user: u })}>{t('users.edit')}</Button>{' '}
                        <Button size="sm" onClick={() => setDialog({ kind: 'reset', user: u })}>{t('users.resetPassword')}</Button>{' '}
                        <Button size="sm" variant="danger" disabled={u.username === me?.username} title={u.username === me?.username ? t('users.cannotDeleteSelf') : undefined} onClick={() => setDialog({ kind: 'delete', user: u })}>{t('users.delete')}</Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Async>
      </Panel>

      {dialog?.kind === 'create' && <CreateDialog onClose={() => setDialog(null)} onDone={() => done(t('users.createdMsg'))} />}
      {dialog?.kind === 'edit' && <EditDialog user={dialog.user} isSelf={dialog.user.username === me?.username} onClose={() => setDialog(null)} onDone={() => done(t('users.updatedMsg'))} />}
      {dialog?.kind === 'delete' && <DeleteDialog user={dialog.user} onClose={() => setDialog(null)} onDone={() => done(t('users.deletedMsg', { user: dialog.user.username }))} />}
      {dialog?.kind === 'reset' && <ResetDialog user={dialog.user} onClose={() => setDialog(null)} onDone={() => done(t('users.resetMsg', { user: dialog.user.username }))} />}
    </>
  )
}

function RoleSelect({ value, onChange, disabled }: { value: Role; onChange: (r: Role) => void; disabled?: boolean }) {
  const t = useT()
  return (
    <select value={value} disabled={disabled} onChange={(e) => onChange(e.target.value as Role)}>
      <option value="Operator">{t('users.roleOperator')}</option>
      <option value="Admin">{t('users.roleAdmin')}</option>
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
  const t = useT()
  const [username, setUsername] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [role, setRole] = useState<Role>('Operator')
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [active, setActive] = useState(true)
  const pwProblem = password ? checkPassword(password, username) : null
  const mismatch = confirm && confirm !== password ? t('users.fMismatch') : null
  const s = useSubmit(() => api.createUser({ username: username.trim(), displayName: displayName.trim(), role, password, isActive: active }), onDone)
  return (
    <Modal title={t('users.create')} onClose={onClose} footer={<>
      <Button onClick={onClose}>{t('users.skip')}</Button>
      <Button variant="primary" disabled={s.busy || !username.trim() || !displayName.trim() || !password || !!pwProblem || confirm !== password} onClick={() => void s.run()}>{t('users.create')}</Button>
    </>}>
      {s.general && <div className="notice notice-bad" role="alert">{s.general}</div>}
      <div className="form-grid single">
        <Field label={t('users.fUsername')} error={s.errors.username} hint={t('users.fUsernameHint')}>
          <input value={username} onChange={(e) => setUsername(e.target.value)} autoComplete="off" /></Field>
        <Field label={t('users.fDisplayName')} error={s.errors.displayName}><input value={displayName} onChange={(e) => setDisplayName(e.target.value)} /></Field>
        <Field label={t('users.fRole')} error={s.errors.role}><RoleSelect value={role} onChange={setRole} /></Field>
        <Field label={t('users.fInitialPassword')} error={s.errors.password ?? pwProblem ?? undefined} hint={t('users.fInitialPasswordHint')}>
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="new-password" /></Field>
        <Field label={t('users.fConfirm')} error={mismatch ?? undefined}>
          <input type="password" value={confirm} onChange={(e) => setConfirm(e.target.value)} autoComplete="new-password" /></Field>
        <label className="check"><input type="checkbox" checked={active} onChange={(e) => setActive(e.target.checked)} /> {t('users.fActivate')}</label>
      </div>
    </Modal>
  )
}

function EditDialog({ user, isSelf, onClose, onDone }: { user: UserRow; isSelf: boolean; onClose: () => void; onDone: () => void }) {
  const t = useT()
  const [displayName, setDisplayName] = useState(user.displayName)
  const [role, setRole] = useState<Role>(user.role)
  const [active, setActive] = useState(user.isActive)
  const [position, setPosition] = useState(user.position ?? '')
  const [department, setDepartment] = useState(user.department ?? '')
  const s = useSubmit(async () => {
    await api.updateUser(user.id, { displayName: displayName.trim(), role, isActive: active })
    // position / department are separate administrator-managed data (blank = not provided); the preferences, e-mail and avatar are never part of this dialog
    if (position.trim() !== (user.position ?? '') || department.trim() !== (user.department ?? ''))
      await api.setUserOrganization(user.id, { position: position.trim() || null, department: department.trim() || null })
  }, onDone)
  return (
    <Modal title={t('users.editTitle', { user: user.username })} onClose={onClose} footer={<>
      <Button onClick={onClose}>{t('users.skip')}</Button>
      <Button variant="primary" disabled={s.busy || !displayName.trim()} onClick={() => void s.run()}>{t('users.save')}</Button>
    </>}>
      {s.general && <div className="notice notice-bad" role="alert">{s.general}</div>}
      {isSelf && <div className="notice notice-info">{t('users.selfNotice')}</div>}
      <div className="form-grid single">
        <Field label={t('users.fDisplayName')} error={s.errors.displayName}><input value={displayName} onChange={(e) => setDisplayName(e.target.value)} /></Field>
        <Field label={t('users.fPosition')} error={s.errors.position}><input value={position} maxLength={100} onChange={(e) => setPosition(e.target.value)} /></Field>
        <Field label={t('users.fDepartment')} error={s.errors.department} hint={t('users.fOrgHint')}><input value={department} maxLength={100} onChange={(e) => setDepartment(e.target.value)} /></Field>
        <Field label={t('users.fRole')} error={s.errors.role}><RoleSelect value={role} onChange={setRole} disabled={isSelf} /></Field>
        <label className="check"><input type="checkbox" checked={active} disabled={isSelf} onChange={(e) => setActive(e.target.checked)} /> {t('users.fActiveCheck')}</label>
      </div>
    </Modal>
  )
}

function ResetDialog({ user, onClose, onDone }: { user: UserRow; onClose: () => void; onDone: () => void }) {
  const t = useT()
  const [password, setPassword] = useState('')
  const problem = password ? checkPassword(password, user.username) : null
  const s = useSubmit(() => api.resetPassword(user.id, password), onDone)
  return (
    <Modal title={t('users.resetTitle', { user: user.username })} onClose={onClose} footer={<>
      <Button onClick={onClose}>{t('users.skip')}</Button>
      <Button variant="primary" disabled={s.busy || !password || !!problem} onClick={() => void s.run()}>{t('users.resetPassword')}</Button>
    </>}>
      {s.general && <div className="notice notice-bad" role="alert">{s.general}</div>}
      <div className="form-grid single">
        <Field label={t('users.fTempPassword')} error={s.errors.newPassword ?? problem ?? undefined} hint={t('users.fTempPasswordHint')}>
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="new-password" /></Field>
      </div>
    </Modal>
  )
}

function DeleteDialog({ user, onClose, onDone }: { user: UserRow; onClose: () => void; onDone: () => void }) {
  const t = useT()
  const s = useSubmit(() => api.deleteUser(user.id), onDone)
  return (
    <Modal title={t('users.deleteTitle', { user: user.username })} onClose={onClose} footer={<>
      <Button onClick={onClose}>{t('users.skip')}</Button>
      <Button variant="danger" disabled={s.busy} onClick={() => void s.run()}>{t('users.confirmDelete')}</Button>
    </>}>
      {s.general && <div className="notice notice-bad" role="alert">{s.general}</div>}
      <p className="dialog-hint">
        <Rich text={t('users.deleteBody', { name: displayNameOf(user.displayName) })} parts={{ user: <strong>{user.username}</strong> }} />
      </p>
    </Modal>
  )
}
