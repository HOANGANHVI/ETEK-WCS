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
