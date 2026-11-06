import { DEMO_MODE } from '../config'
import { demoDataApi } from '../demo/demoApi'
import { setDemoSessionRole } from '../demo/session'
import { ApiRequestError } from './errors'
import type { AuditFilter, AuditRow, Me, Role, UserRow } from './authTypes'
import type { AddressIssue, HandshakeState, IoPointState, PlcConnection, PlcLogRow, PlcOverview, ProbeResultView, SavePlcConnectionBody, StationSnapshot } from './plcTypes'
import type { LastTest, OperationsView, PlcConfig, RcsConfig, SaveLocationBody, SavePlcBody, SaveRcsBody } from './configTypes'
import type {
  Agv, ApiResponse, FlowOption, InventoryTransaction, Location, Paged, Rack, SimulationStep, SystemHealth,
  TaskHistory, TaskState, TransportTask,
} from './types'

export const UNAUTHORIZED_EVENT = 'wcs:unauthorized'
const BASE_URL = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? ''

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let res: Response
  try {
    res = await fetch(`${BASE_URL}${path}`, {
      ...init,
      // Same-origin cookie session. The custom header is required by the API for every state-changing call.
      credentials: 'include',
      headers: { 'Content-Type': 'application/json', 'X-WCS-Client': 'web', ...(init?.headers ?? {}) },
    })
  } catch {
    throw new ApiRequestError(0, { code: 'NETWORK_ERROR', message: 'Cannot reach the WCS API.' })
  }

  let body: ApiResponse<T> | null = null
  try {
    body = (await res.json()) as ApiResponse<T>
  } catch {
    /* non-JSON response */
  }
  if (!res.ok || !body?.success) {
    // A rejected session (not a wrong password at the login form) sends the user back to the sign-in screen.
    if (res.status === 401 && !path.startsWith('/api/auth/login')) window.dispatchEvent(new CustomEvent(UNAUTHORIZED_EVENT))
    if (res.status === 403 && body?.error?.code === 'PASSWORD_CHANGE_REQUIRED') window.dispatchEvent(new CustomEvent(UNAUTHORIZED_EVENT, { detail: 'password' }))
    throw new ApiRequestError(res.status, body?.error ?? { code: 'HTTP_' + res.status, message: res.statusText || 'Request failed' })
  }
  return body.data as T
}

const qs = (params: Record<string, string | number | undefined>) => {
  const s = new URLSearchParams()
  for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== '') s.set(k, String(v))
  const text = s.toString()
  return text ? `?${text}` : ''
}

const realApi = {
  health: () => request<SystemHealth>('/api/system/health'),
  agvs: () => request<Agv[]>('/api/agvs'),
  flows: () => request<FlowOption[]>('/api/flows'),
  locations: () => request<Location[]>('/api/locations'),
  racks: (location?: string) => request<Rack[]>(`/api/inventory/racks${qs({ location, take: 500 })}`),
  transactions: (location?: string) => request<InventoryTransaction[]>(`/api/inventory/transactions${qs({ location, take: 50 })}`),
  tasks: (state?: TaskState, skip = 0, take = 50) => request<Paged<TransportTask>>(`/api/tasks${qs({ state, skip, take })}`),
  task: (id: string) => request<TransportTask>(`/api/tasks/${id}`),
  taskHistory: (id: string) => request<TaskHistory[]>(`/api/tasks/${id}/history`),
  cancelTask: (id: string, reason: string) =>
    request<TransportTask>(`/api/tasks/${id}/cancel`, { method: 'POST', body: JSON.stringify({ reason }) }),
  simulateNext: (id: string) => request<SimulationStep>(`/api/simulator/tasks/${id}/advance`, { method: 'POST' }),
  createTask: (flow: string, stationCode: string, requestKey: string) =>
    request<TransportTask>('/api/tasks', { method: 'POST', body: JSON.stringify({ requestKey, flow, stationCode }) }),
  updateTask: (id: string, flow: string, stationCode: string, expectedState?: string) =>
    request<TransportTask>(`/api/tasks/${id}`, { method: 'PUT', body: JSON.stringify({ flow, stationCode, expectedState }) }),
  deleteTask: (id: string, expectedState?: string) =>
    request<TransportTask>(`/api/tasks/${id}${qs({ expectedState })}`, { method: 'DELETE' }),

  // session and accounts
  login: (username: string, password: string) => request<Me>('/api/auth/login', { method: 'POST', body: JSON.stringify({ username, password }) }),
  logout: () => request<{ signedOut: boolean }>('/api/auth/logout', { method: 'POST' }),
  me: () => request<Me>('/api/auth/me'),
  changePassword: (currentPassword: string, newPassword: string) =>
    request<Me>('/api/auth/change-password', { method: 'POST', body: JSON.stringify({ currentPassword, newPassword }) }),
  users: (search?: string) => request<UserRow[]>(`/api/users${qs({ search })}`),
  createUser: (b: { username: string; displayName: string; role: Role; password: string; isActive: boolean }) =>
    request<UserRow>('/api/users', { method: 'POST', body: JSON.stringify(b) }),
  updateUser: (id: string, b: { displayName: string; role: Role; isActive: boolean }) =>
    request<UserRow>(`/api/users/${id}`, { method: 'PUT', body: JSON.stringify(b) }),
  deleteUser: (id: string) => request<{ deleted: boolean }>(`/api/users/${id}`, { method: 'DELETE' }),
  resetPassword: (id: string, newPassword: string) =>
    request<{ reset: boolean }>(`/api/users/${id}/reset-password`, { method: 'POST', body: JSON.stringify({ newPassword }) }),
  audit: (f: AuditFilter) =>
    request<AuditRow[]>(`/api/audit${qs({ actor: f.actor, action: f.action, result: f.result, skip: f.skip ?? 0, take: f.take ?? 100 })}`),

  // PLC monitoring and configuration (CR-002)
  plcOverview: () => request<PlcOverview>('/api/plc/overview'),
  plcStationIo: (stationId: string) => request<StationSnapshot>(`/api/plc/stations/${encodeURIComponent(stationId)}/io`),
  plcHandshake: () => request<HandshakeState[]>('/api/plc/handshake'),
  plcConnections: () => request<PlcConnection[]>('/api/plc/connections'),
  plcSaveConnection: (plcId: string, b: SavePlcConnectionBody) => request<PlcConnection>(`/api/plc/connections/${plcId}`, { method: 'PUT', body: JSON.stringify(b) }),
  plcTestConnection: (plcId: string) => request<ProbeResultView>(`/api/plc/connections/${plcId}/test`, { method: 'POST' }),
  plcAddressCheck: (plcId: string) => request<AddressIssue[]>(`/api/plc/connections/${plcId}/address-check`),
  plcSetAddress: (id: string, b: { address: string; wordLength?: number | null; encoding?: string | null }) =>
    request<IoPointState>(`/api/plc/points/${id}/address`, { method: 'PUT', body: JSON.stringify(b) }),
  plcClearAddress: (id: string) => request<IoPointState>(`/api/plc/points/${id}/address`, { method: 'DELETE' }),
  plcApprovePoint: (id: string) => request<IoPointState>(`/api/plc/points/${id}/approve`, { method: 'POST' }),
  plcWriteAllowed: (id: string, allowed: boolean) => request<IoPointState>(`/api/plc/points/${id}/write-allowed`, { method: 'PUT', body: JSON.stringify({ allowed }) }),
  plcLogs: (f: { plcId?: string; stationId?: string; minLevel?: string; take?: number }) =>
    request<PlcLogRow[]>(`/api/plc/logs${qs({ plcId: f.plcId, stationId: f.stationId, minLevel: f.minLevel, take: f.take ?? 100 })}`),
  plcTestWrite: (stationId: string, signal: string, value: boolean | number | string) =>
    request<{ success: boolean }>(`/api/plc/stations/${encodeURIComponent(stationId)}/signals/${encodeURIComponent(signal)}/test-write`, { method: 'POST', body: JSON.stringify({ value }) }),
  plcSimValue: (plcId: string, stationId: string, signal: string, value: boolean | number | string) =>
    request<unknown>('/api/plc/simulator/value', { method: 'POST', body: JSON.stringify({ plcId, stationId, signal, value }) }),
  plcSimOnline: (plcId: string, online: boolean) => request<unknown>('/api/plc/simulator/plc-online', { method: 'POST', body: JSON.stringify({ plcId, online }) }),
  plcSimStationFault: (plcId: string, stationId: string, failing: boolean) =>
    request<unknown>('/api/plc/simulator/station-fault', { method: 'POST', body: JSON.stringify({ plcId, stationId, failing }) }),

  // configuration (secrets are write-only)
  plcConfig: () => request<PlcConfig>('/api/config/plc'),
  savePlc: (b: SavePlcBody) => request<PlcConfig>('/api/config/plc', { method: 'PUT', body: JSON.stringify(b) }),
  testPlc: () => request<LastTest>('/api/config/plc/test', { method: 'POST' }),
  rcsConfig: () => request<RcsConfig>('/api/config/rcs'),
  saveRcs: (b: SaveRcsBody) => request<RcsConfig>('/api/config/rcs', { method: 'PUT', body: JSON.stringify(b) }),
  testRcs: () => request<LastTest>('/api/config/rcs/test', { method: 'POST' }),
  operations: () => request<OperationsView>('/api/config/operations'),
  saveAgv: (id: string, b: { rcsRobotCode: string | null; enabled: boolean }) =>
    request<Agv>(`/api/config/agvs/${id}`, { method: 'PUT', body: JSON.stringify(b) }),
  createLocation: (b: SaveLocationBody) => request<Location>('/api/config/locations', { method: 'POST', body: JSON.stringify(b) }),
  saveLocation: (b: SaveLocationBody) => request<Location>(`/api/config/locations/${encodeURIComponent(b.code)}`, { method: 'PUT', body: JSON.stringify(b) }),
}

export { ApiRequestError }

/**
 * Demo Mode: DATA calls (tasks, AGVs, inventory, configuration) are answered by the in-browser engine and never leave the page.
 * Authentication, accounts and the audit trail are always real (same backend, same PostgreSQL users); the signed-in role is only
 * mirrored so the demo data obeys the same permission rules.
 */
const demoApi: typeof realApi = {
  ...realApi,
  ...demoDataApi,
  login: async (u, p) => { const me = await realApi.login(u, p); setDemoSessionRole(me.role); return me },
  me: async () => { try { const me = await realApi.me(); setDemoSessionRole(me.role); return me } catch (e) { setDemoSessionRole(null); throw e } },
  logout: async () => { try { return await realApi.logout() } finally { setDemoSessionRole(null) } },
}

export const api: typeof realApi = DEMO_MODE ? demoApi : realApi
