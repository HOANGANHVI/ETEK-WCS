import { DEMO_MODE } from '../config'
import { demoApi } from '../demo/demoApi'
import { ApiRequestError } from './errors'
import type { LastTest, OperationsView, PlcConfig, RcsConfig, SaveLocationBody, SavePlcBody, SaveRcsBody } from './configTypes'
import type {
  Agv, ApiResponse, InventoryTransaction, Location, Paged, Rack, SimulationStep, SystemHealth,
  TaskHistory, TaskState, TransportTask,
} from './types'

const BASE_URL = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? ''

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let res: Response
  try {
    res = await fetch(`${BASE_URL}${path}`, {
      ...init,
      headers: { 'Content-Type': 'application/json', ...(init?.headers ?? {}) },
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
  locations: () => request<Location[]>('/api/locations'),
  racks: (location?: string) => request<Rack[]>(`/api/inventory/racks${qs({ location, take: 500 })}`),
  transactions: (location?: string) => request<InventoryTransaction[]>(`/api/inventory/transactions${qs({ location, take: 50 })}`),
  tasks: (state?: TaskState, skip = 0, take = 50) => request<Paged<TransportTask>>(`/api/tasks${qs({ state, skip, take })}`),
  task: (id: string) => request<TransportTask>(`/api/tasks/${id}`),
  taskHistory: (id: string) => request<TaskHistory[]>(`/api/tasks/${id}/history`),
  cancelTask: (id: string, reason: string, user?: string) =>
    request<TransportTask>(`/api/tasks/${id}/cancel`, { method: 'POST', body: JSON.stringify({ reason, user }) }),
  simulateNext: (id: string) => request<SimulationStep>(`/api/simulator/tasks/${id}/advance`, { method: 'POST' }),

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
