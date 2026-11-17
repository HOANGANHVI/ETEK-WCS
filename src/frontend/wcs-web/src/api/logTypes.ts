/** CR-007 Logging & Diagnostics (Admin only). Mirrors the backend DTOs. */
export const LOG_CATEGORIES = ['SYSTEM', 'PLC', 'DATABASE', 'DEPLOYMENT', 'RCS_AGV', 'SECURITY_AUDIT'] as const
export type LogCategory = (typeof LOG_CATEGORIES)[number]

export const LOG_LEVELS = ['Information', 'Warning', 'Error', 'Critical'] as const
export type LogLevel = (typeof LOG_LEVELS)[number]

export interface LogEntry {
  id: string
  occurredAt: string
  category: LogCategory
  level: LogLevel
  source: string
  device: string | null
  event: string | null
  message: string
  /** Stable log template (system events) and the event parameters: the UI phrases the entry in the selected language from these. */
  messageTemplate?: string | null
  detailsJson?: string | null
}

export interface LogPage { items: LogEntry[]; total: number; page: number; pageSize: number }

export interface ProblemDevice { device: string; category: LogCategory; errors: number; warnings: number; lastAt: string }

export interface LogStats {
  from: string
  to: string
  total: number
  byLevel: Record<LogLevel, number>
  byCategory: Record<LogCategory, number>
  latestErrors: LogEntry[]
  problemDevices: ProblemDevice[]
  devices: string[]
}

export interface LogDetail {
  id: string
  entryKind: 'system' | 'plc' | 'audit'
  occurredAt: string
  category: LogCategory
  level: LogLevel
  source: string
  device: string | null
  station: string | null
  event: string | null
  message: string
  exception: string | null
  correlationId: string | null
  ip: string | null
  port: number | null
  previousState: string | null
  newState: string | null
  failureKind: string | null
  endCode: string | null
  reason: string | null
  host: string | null
  actor: string | null
  actorRole: string | null
  result: string | null
  targetType: string | null
  targetId: string | null
  beforeJson: string | null
  afterJson: string | null
  detailsJson: string | null
  messageTemplate?: string | null
}

export interface LogFilter {
  from?: string
  to?: string
  category?: string
  level?: string
  device?: string
  source?: string
  eventCode?: string
  keyword?: string
  page?: number
  pageSize?: number
}
