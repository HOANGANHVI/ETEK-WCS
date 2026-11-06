import type { AuditFilter, AuditResult, AuditRow, Me, Role, UserRow } from '../api/authTypes'
import { DemoError } from './engine'

/**
 * Demo Mode ONLY: an in-browser stand-in for the account/session API so both roles can be shown without a backend.
 * It is reachable exclusively through `demoApi`, which the client uses only when VITE_DEMO_MODE=true. It has no passwords,
 * protects nothing, and must never be used as authentication of a real deployment.
 */
const SESSION_KEY = 'wcs-demo-session'

const now = () => new Date().toISOString()
const uid = () => crypto.randomUUID()

const users: UserRow[] = [
  { id: uid(), username: 'demo-admin', displayName: 'Quản trị viên (mô phỏng)', role: 'Admin', isActive: true, mustChangePassword: false, lastLoginAt: null, lockedUntil: null, createdAt: now() },
  { id: uid(), username: 'demo-operator', displayName: 'Vận hành viên (mô phỏng)', role: 'Operator', isActive: true, mustChangePassword: false, lastLoginAt: null, lockedUntil: null, createdAt: now() },
]
const audit: AuditRow[] = []

const readSession = (): string | null => { try { return sessionStorage.getItem(SESSION_KEY) } catch { return null } }
const writeSession = (v: string | null) => { try { if (v) sessionStorage.setItem(SESSION_KEY, v); else sessionStorage.removeItem(SESSION_KEY) } catch { /* private mode */ } }

function current(): UserRow | null {
  const name = readSession()
  const u = users.find((x) => x.username === name && x.isActive)
  return u ?? null
}

export function record(action: string, targetType: string | null = null, targetId: string | null = null, result: AuditResult = 'Success', detail: string | null = null, before: unknown = null, after: unknown = null) {
  const u = current()
  audit.unshift({
    id: uid(), occurredAt: now(), actor: u?.username ?? null, actorRole: u?.role ?? null, action, targetType, targetId, result, detail,
    beforeJson: before ? JSON.stringify(before) : null, afterJson: after ? JSON.stringify(after) : null, ipAddress: null,
  })
}

export const demoRole = (): Role | null => current()?.role ?? null

export function requireSession(): UserRow {
  const u = current()
  if (!u) throw new DemoError(401, 'UNAUTHENTICATED', 'Chưa đăng nhập.')
  return u
}
export function requireAdmin(): UserRow {
  const u = requireSession()
  if (u.role !== 'Admin') { record('ACCESS_DENIED', null, null, 'Denied', 'Operator tried an Admin-only action'); throw new DemoError(403, 'FORBIDDEN', 'Bạn không có quyền thực hiện thao tác này.') }
  return u
}

const toMe = (u: UserRow): Me => ({ username: u.username, displayName: u.displayName, role: u.role, mustChangePassword: false })

export const demoAuth = {
  login(username: string): Me {
    const u = users.find((x) => x.username === username.trim().toLowerCase())
    if (!u || !u.isActive) {
      record('AUTH_LOGIN', 'User', null, 'Failed', 'demo: unknown or disabled account')
      throw new DemoError(401, 'INVALID_CREDENTIALS', 'Tên đăng nhập hoặc mật khẩu không đúng.')
    }
    writeSession(u.username)
    u.lastLoginAt = now()
    record('AUTH_LOGIN')
    return toMe(u)
  },
  me(): Me { return toMe(requireSession()) },
  logout() { requireSession(); record('AUTH_LOGOUT'); writeSession(null); return { signedOut: true } },
  changePassword(): Me { const u = requireSession(); record('AUTH_PASSWORD_CHANGE'); return toMe(u) },

  listUsers(search?: string): UserRow[] {
    requireAdmin()
    const q = (search ?? '').trim().toLowerCase()
    return users.filter((u) => !q || u.username.includes(q) || u.displayName.toLowerCase().includes(q)).map((u) => ({ ...u }))
  },
  createUser(b: { username: string; displayName: string; role: Role }): UserRow {
    requireAdmin()
    const name = b.username.trim().toLowerCase()
    if (!/^[a-z0-9._-]{3,50}$/.test(name)) throw new DemoError(400, 'VALIDATION_FAILED', 'Tên đăng nhập không hợp lệ.', [{ field: 'username', message: 'Tên đăng nhập gồm 3-50 ký tự: chữ thường, số, dấu chấm, gạch dưới hoặc gạch ngang.' }])
    if (users.some((u) => u.username === name)) throw new DemoError(400, 'VALIDATION_FAILED', 'Tên đăng nhập đã tồn tại.', [{ field: 'username', message: 'Tên đăng nhập đã tồn tại.' }])
    const u: UserRow = { id: uid(), username: name, displayName: b.displayName.trim(), role: b.role, isActive: true, mustChangePassword: true, lastLoginAt: null, lockedUntil: null, createdAt: now() }
    users.push(u)
    record('USER_CREATE', 'User', u.id, 'Success', null, null, { username: u.username, role: u.role })
    return { ...u }
  },
  updateUser(id: string, b: { displayName: string; role: Role; isActive: boolean }): UserRow {
    const me = requireAdmin()
    const u = users.find((x) => x.id === id)
    if (!u) throw new DemoError(404, 'USER_NOT_FOUND', 'Không tìm thấy tài khoản.')
    if (u.id === me.id && b.role !== u.role) throw new DemoError(422, 'CANNOT_CHANGE_OWN_ROLE', 'Không thể tự thay đổi vai trò.')
    if (u.id === me.id && !b.isActive) throw new DemoError(422, 'CANNOT_DISABLE_SELF', 'Không thể tự vô hiệu hóa tài khoản.')
    const activeAdmins = users.filter((x) => x.role === 'Admin' && x.isActive)
    if (u.role === 'Admin' && u.isActive && (b.role !== 'Admin' || !b.isActive) && activeAdmins.length <= 1)
      throw new DemoError(422, 'LAST_ADMIN', 'Không thể vô hiệu hóa hoặc hạ quyền Admin đang hoạt động cuối cùng.')
    const before = { username: u.username, role: u.role, isActive: u.isActive }
    u.displayName = b.displayName.trim(); u.role = b.role; u.isActive = b.isActive
    record('USER_UPDATE', 'User', u.id, 'Success', null, before, { username: u.username, role: u.role, isActive: u.isActive })
    return { ...u }
  },
  resetPassword(id: string) {
    requireAdmin()
    const u = users.find((x) => x.id === id)
    if (!u) throw new DemoError(404, 'USER_NOT_FOUND', 'Không tìm thấy tài khoản.')
    record('USER_PASSWORD_RESET', 'User', id)
    return { reset: true }
  },
  listAudit(f: AuditFilter): AuditRow[] {
    requireAdmin()
    return audit
      .filter((a) => (!f.actor || a.actor === f.actor.trim().toLowerCase()) && (!f.action || a.action === f.action) && (!f.result || a.result === f.result))
      .slice(f.skip ?? 0, (f.skip ?? 0) + (f.take ?? 100)).map((a) => ({ ...a }))
  },
}
