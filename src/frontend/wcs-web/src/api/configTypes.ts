// Configuration DTOs. Secrets are write-only: they exist in the SAVE bodies only and are never present in any response.
import type { Agv, Location } from './types'

export type TestOutcome = 'Success' | 'Failed' | 'Simulated' | 'NotExecuted'

export interface LastTest {
  outcome: TestOutcome
  mode: string
  message: string
  error: string | null
  latencyMs: number | null
  testedAt: string
}

export interface PlcSettings {
  deviceName: string | null
  ipAddress: string | null
  port: number | null
  protocol: string | null
  networkNumber: number | null
  pcNumber: number | null
  stationNumber: number | null
  timeoutMs: number | null
  pollingIntervalMs: number | null
}

export interface PlcConfig {
  configured: boolean
  enabled: boolean
  settings: PlcSettings
  lastTest: LastTest | null
  updatedAt: string | null
  updatedBy: string | null
}

export interface RcsSettings {
  systemName: string | null
  host: string | null
  port: number | null
  baseUrl: string | null
  authMode: 'None' | 'Token' | 'UsernamePassword'
  username: string | null
  timeoutMs: number | null
}

export interface RcsConfig {
  configured: boolean
  enabled: boolean
  settings: RcsSettings
  secrets: { hasPassword: boolean; hasToken: boolean }
  lastTest: LastTest | null
  updatedAt: string | null
  updatedBy: string | null
}

export type SavePlcBody = PlcSettings & { enabled: boolean; updatedBy?: string }
export type SaveRcsBody = RcsSettings & {
  enabled: boolean
  password?: string | null
  clearPassword: boolean
  token?: string | null
  clearToken: boolean
  updatedBy?: string
}

export interface FlowView {
  flow: string
  rackKind: string
  agvRole: string
  stations: string[]
  sourceMode: string
  sourceGroups: string[][]
  destinationMode: string
  destinationGroups: string[][]
  noBindDropLocations: string[]
}

export interface OperationsView {
  policy: {
    concurrency: string
    concurrencyApproved: boolean
    queueOrder: string
    queueOrderApproved: boolean
    noSourceBehavior: string
    noSourceBehaviorApproved: boolean
  }
  scheduler: { tickIntervalMs: number; maxDispatchAttempts: number; stateTimeoutsSeconds: Record<string, number> }
  flows: FlowView[]
  agvs: Agv[]
  locations: Location[]
}

export interface SaveLocationBody {
  code: string
  name: string
  type: string
  rackCapacity: number
  allowedRackKind: string
  enabled: boolean
  requiresPlcPermission: boolean
}

export interface FieldError { field: string; message: string; code?: string | null; args?: Record<string, unknown> | null }
