import type { Role } from '../api/authTypes'
import { DemoError } from './engine'

/**
 * Role of the REAL signed-in account, copied here by api/client.ts after every login / me call so the in-browser demo data can apply the
 * same permission rules as the API. It is not an identity: nothing here authenticates anybody.
 */
let role: Role | null = null

export const setDemoSessionRole = (r: Role | null) => { role = r }

export function requireSession(): Role {
  if (!role) throw new DemoError(401, 'UNAUTHENTICATED', 'Chưa đăng nhập.')
  return role
}

export function requireAdmin(): Role {
  const r = requireSession()
  if (r !== 'Admin') throw new DemoError(403, 'FORBIDDEN', 'Bạn không có quyền thực hiện thao tác này.')
  return r
}
