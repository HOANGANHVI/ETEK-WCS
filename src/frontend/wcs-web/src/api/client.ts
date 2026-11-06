import { DEMO_MODE } from '../config'
import { demoApi } from '../demo/demoApi'
import { ApiRequestError } from './errors'
import type { AuditFilter, AuditRow, Me, Role, UserRow } from './authTypes'
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
  updateTask: (id: string, stationCode: string, expectedState?: string) =>
    request<TransportTask>(`/api/tasks/${id}`, { method: 'PUT', body: JSON.stringify({ stationCode, expectedState }) }),
  deleteTask: (id: string, expectedState?: string) =>
    request<TransportTask>(`/api/tasks/${id}${qs({ expectedState })}`, { method: 'DELETE' }),

  // session and accounts
  login: (username: string, password: string) => request<Me>('/api/auth/login', { method: 'POST', body: JSON.stringify({ username, password }) }),
  logout: () => request<{ signedOut: boolean }>('/api/auth/logout', { method: 'POST' }),
  me: () => request<Me>('/api/auth/me'),
  changePassword: (currentPassword: string, newPassword: string) =>
    request<Me>('/api/auth/change-password', { method: 'POST', body: JSON.stringify({ currentPassword, newPassword }) }),
  users: (search?: string) => request<UserRow[]>(`/api/users${qs({ search })}`),
  createUser: (b: { username: string; displayName: string; role: Role; password: string }) =>
    request<UserRow>('/api/users', { method: 'POST', body: JSON.stringify(b) }),
  updateUser: (id: string, b: { displayName: string; role: Role; isActive: boolean }) =>
    request<UserRow>(`/api/users/${id}`, { method: 'PUT', body: JSON.stringify(b) }),
  resetPassword: (id: string, newPassword: string) =>
    request<{ reset: boolean }>(`/api/users/${id}/reset-password`, { method: 'POST', body: JSON.stringify({ newPassword }) }),
  audit: (f: AuditFilter) =>
    request<AuditRow[]>(`/api/audit${qs({ actor: f.actor, action: f.action, result: f.result, skip: f.skip ?? 0, take: f.take ?? 100 })}`),

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

/** In Demo Mode every call is answered by the in-browser engine; no request ever leaves the page. */
export const api: typeof realApi = DEMO_MODE ? demoApi : realApi
