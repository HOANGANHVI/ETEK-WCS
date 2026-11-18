// DTOs of the authentication / account / audit API.

export type Role = 'Admin' | 'Operator'

export interface Me {
  username: string
  displayName: string
  role: Role
  mustChangePassword: boolean
}

export interface UserRow {
  id: string
  username: string
  displayName: string
  role: Role
  isActive: boolean
  mustChangePassword: boolean
  lastLoginAt: string | null
  lockedUntil: string | null
  createdAt: string
  /** null = nobody has provided it yet (never defaulted). */
  email: string | null
  position: string | null
  department: string | null
}

/** The signed-in user's own profile. Email / position / department are null until somebody provides them. */
export interface Profile {
  username: string
  displayName: string
  role: Role
  email: string | null
  position: string | null
  department: string | null
  hasAvatar: boolean
  /** Changes whenever the avatar changes (cache-busting query value); 0 when there is none. */
  avatarVersion: number
  lastLoginAt: string | null
}

export type AuditResult = 'Success' | 'Denied' | 'Failed'

export interface AuditRow {
  id: string
  occurredAt: string
  actor: string | null
  actorRole: string | null
  action: string
  targetType: string | null
  targetId: string | null
  result: AuditResult
  detail: string | null
  beforeJson: string | null
  afterJson: string | null
  ipAddress: string | null
}

export interface AuditFilter {
  actor?: string
  action?: string
  result?: AuditResult | ''
  skip?: number
  take?: number
}
