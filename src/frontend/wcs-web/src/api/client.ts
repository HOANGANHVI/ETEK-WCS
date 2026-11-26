import { DEMO_MODE } from '../config'
import { demoDataApi } from '../demo/demoApi'
import type { Preferences } from '../preferences/prefsStore'
import { setDemoSessionRole } from '../demo/session'
import { ApiRequestError } from './errors'
import type { AuditFilter, AuditRow, Me, Profile, Role, UserRow } from './authTypes'
import type { LogDetail, LogFilter, LogPage, LogStats } from './logTypes'
import type { SimFlow, SimStartResult, SimStation, SimStatus } from './simTypes'
import type { ExecMode, ExecutionModeView, LegacyAdoptionResult, ModeReadiness, ModeTransitionBody, ModeTransitionResult, ModeTransitionView, RcsMessageDetail, RcsMessageSummary } from './modeTypes'
import type { DiagExportBody, DiagExportJob, DiagExportOptions } from './diagTypes'
import type { AddressIssue, BulkApprovalPreview, BulkApprovalResult, HandshakeState, IoPointConfig, IoPointState, MonitorList, PlcConnection, PlcLogRow, PlcOverview, ProbeResultView, ReadNowResult, SavePlcConnectionBody, StationSnapshot, TestWriteResult } from './plcTypes'
import type { AddPlcBody, QueueAlert, StartableProcess, QueueScopeResult, ImportResult, Order, OrderDetail, OrderPage, PortView, Process, ProcessHeaderBody, ProcessVersion, StationTransaction, VersionDefinition } from './routingTypes'
import type {
  ApplyBody, ApplyResult, ExportDependencyReport, ImportPlan, MaintenanceView, ProductModelInfo, Selection, TransferGroup, TransferGroupId, TransferStatus, UploadResult,
} from './configTransferTypes'
import type { FleetView } from './fleetTypes'
import type { RcsParameter, RcsParameters, RcsCallbacks, RcsCatalog, RcsCheckState, RcsExecution, RcsHistoryFilter, RcsHistoryItem, RcsHistoryPage, RcsOverview, RcsPreview } from './rcsDiagTypes'
import type { LastTest, OperationsView, PlcConfig, RcsConfig, SaveLocationBody, SavePlcBody, SaveRcsBody } from './configTypes'
import type {
  Agv, ApiResponse, FlowOption, InventoryTransaction, Location, Paged, Rack, ReconciliationDetail, SimulationStep, SystemHealth,
  ResourceSnapshot, TaskHistory, TaskState, TransportTask,
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

/** CR-021: the ZIP of a finished system export (Admin-only, audited by the server). */
const downloadDiagExport = (id: string): Promise<void> => downloadZip(`/api/diagnostics/exports/${encodeURIComponent(id)}/download`, 'WCS_SystemDiagnostic.zip')

async function downloadZip(path: string, fallbackName: string): Promise<void> {
  let res: Response
  try {
    res = await fetch(`${BASE_URL}${path}`, { credentials: 'include', headers: { 'X-WCS-Client': 'web' } })
  } catch {
    throw new ApiRequestError(0, { code: 'NETWORK_ERROR', message: 'Cannot reach the WCS API.' })
  }
  if (!res.ok) {
    let body: ApiResponse<unknown> | null = null
    try { body = (await res.json()) as ApiResponse<unknown> } catch { /* non-JSON */ }
    if (res.status === 401) window.dispatchEvent(new CustomEvent(UNAUTHORIZED_EVENT))
    throw new ApiRequestError(res.status, body?.error ?? { code: 'HTTP_' + res.status, message: res.statusText || 'Request failed' })
  }
  const name = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(res.headers.get('Content-Disposition') ?? '')?.[1]
  const url = URL.createObjectURL(await res.blob())
  const a = document.createElement('a')
  a.href = url; a.download = name ? decodeURIComponent(name) : fallbackName
  document.body.appendChild(a); a.click(); a.remove()
  setTimeout(() => URL.revokeObjectURL(url), 10_000)
}

/** CR-026: POST that answers with a ZIP (the configuration export). The server decides the file name. */
async function postForZip(path: string, body: unknown, fallbackName: string): Promise<void> {
  let res: Response
  try {
    res = await fetch(`${BASE_URL}${path}`, { method: 'POST', credentials: 'include', headers: { 'Content-Type': 'application/json', 'X-WCS-Client': 'web' }, body: JSON.stringify(body) })
  } catch {
    throw new ApiRequestError(0, { code: 'NETWORK_ERROR', message: 'Cannot reach the WCS API.' })
  }
  if (!res.ok) {
    let err: ApiResponse<unknown> | null = null
    try { err = (await res.json()) as ApiResponse<unknown> } catch { /* non-JSON */ }
    if (res.status === 401) window.dispatchEvent(new CustomEvent(UNAUTHORIZED_EVENT))
    throw new ApiRequestError(res.status, err?.error ?? { code: 'HTTP_' + res.status, message: res.statusText || 'Request failed' })
  }
  const name = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(res.headers.get('Content-Disposition') ?? '')?.[1]
  const url = URL.createObjectURL(await res.blob())
  const a = document.createElement('a')
  a.href = url; a.download = name ? decodeURIComponent(name) : fallbackName
  document.body.appendChild(a); a.click(); a.remove()
  setTimeout(() => URL.revokeObjectURL(url), 10_000)
}

/** CR-026: multipart upload of a configuration package (the browser sets the boundary, so no JSON content type). */
async function uploadPackage(file: File): Promise<UploadResult> {
  const form = new FormData(); form.append('file', file)
  let res: Response
  try {
    res = await fetch(`${BASE_URL}/api/config-transfer/imports`, { method: 'POST', credentials: 'include', headers: { 'X-WCS-Client': 'web' }, body: form })
  } catch {
    throw new ApiRequestError(0, { code: 'NETWORK_ERROR', message: 'Cannot reach the WCS API.' })
  }
  let body: ApiResponse<UploadResult> | null = null
  try { body = (await res.json()) as ApiResponse<UploadResult> } catch { /* non-JSON */ }
  if (!res.ok || !body?.success) {
    if (res.status === 401) window.dispatchEvent(new CustomEvent(UNAUTHORIZED_EVENT))
    throw new ApiRequestError(res.status, body?.error ?? { code: 'HTTP_' + res.status, message: res.statusText || 'Request failed' })
  }
  return body.data as UploadResult
}

const qs = (params: Record<string, string | number | undefined>) => {
  const s = new URLSearchParams()
  for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== '') s.set(k, String(v))
  const text = s.toString()
  return text ? `?${text}` : ''
}

const realApi = {
  health: () => request<SystemHealth>('/api/system/health'),
  // CR-038: operating mode (Simulator / Real AGV) and the RCS message log. The server decides; the page only asks.
  executionMode: () => request<ExecutionModeView>('/api/execution-mode'),
  executionModeReadiness: (target: ExecMode) => request<ModeReadiness>(`/api/execution-mode/readiness${qs({ target })}`),
  executionModeHistory: (take = 20) => request<ModeTransitionView[]>(`/api/execution-mode/history${qs({ take })}`),
  executionModeTransition: (body: ModeTransitionBody) => request<ModeTransitionResult>('/api/execution-mode/transition', { method: 'POST', body: JSON.stringify(body) }),
  executionModeAcknowledge: (requestId: string, reason: string) => request<ModeTransitionResult>('/api/execution-mode/recovery/acknowledge', { method: 'POST', body: JSON.stringify({ requestId, reason }) }),
  executionModeAdoptLegacy: (reason: string) => request<LegacyAdoptionResult>('/api/execution-mode/legacy/adopt-simulator', { method: 'POST', body: JSON.stringify({ reason }) }),
  unlinkedRcsMessages: () => request<RcsMessageSummary[]>('/api/execution-mode/unlinked-messages?take=50'),
  taskRcsMessages: (taskId: string) => request<RcsMessageSummary[]>(`/api/tasks/${taskId}/rcs-messages`),
  rcsMessage: (id: string) => request<RcsMessageDetail>(`/api/rcs-messages/${id}`),
  /** Server resources, container and host kept apart (Admin only). */
  resources: (signal?: AbortSignal) => request<ResourceSnapshot>('/api/system/resources', { signal }),
  agvs: () => request<Agv[]>('/api/agvs'),
  /** CR-027: the AMR fleet reported by the RCS (read-only). */
  agvFleet: () => request<FleetView>('/api/agvs/fleet'),
  flows: () => request<FlowOption[]>('/api/flows'),
  locations: () => request<Location[]>('/api/locations'),
  racks: (location?: string) => request<Rack[]>(`/api/inventory/racks${qs({ location, take: 500 })}`),
  transactions: (location?: string) => request<InventoryTransaction[]>(`/api/inventory/transactions${qs({ location, take: 50 })}`),
  tasks: (state?: TaskState, skip = 0, take = 50) => request<Paged<TransportTask>>(`/api/tasks${qs({ state, skip, take })}`),
  task: (id: string) => request<TransportTask>(`/api/tasks/${id}`),
  taskHistory: (id: string) => request<TaskHistory[]>(`/api/tasks/${id}/history`),
  cancelTask: (id: string, reason: string) =>
    request<TransportTask>(`/api/tasks/${id}/cancel`, { method: 'POST', body: JSON.stringify({ reason }) }),
  // CR-008B-3 physical reconciliation (Admin only; the API enforces it)
  reconciliationList: () => request<TransportTask[]>('/api/tasks/reconciliation'),
  reconciliationDetail: (id: string) => request<ReconciliationDetail>(`/api/tasks/${id}/reconciliation`),
  reconcileTask: (id: string, actualLocationCode: string, user: string, note: string) =>
    request<TransportTask>(`/api/tasks/${id}/reconcile`, { method: 'POST', body: JSON.stringify({ actualLocationCode, user, note }) }),
  releaseAgvHold: (id: string, inventoryReconciled: boolean, agvPhysicallyVerified: boolean, note: string) =>
    request<Agv>(`/api/agvs/${id}/release-hold`, { method: 'POST', body: JSON.stringify({ inventoryReconciled, agvPhysicallyVerified, note }) }),
  reconciliationCheck: (id: string, note: string) =>
    request<TransportTask>(`/api/tasks/${id}/reconciliation-check`, { method: 'POST', body: JSON.stringify({ note }) }),
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
  // own profile (full name, e-mail, avatar). The account is always the signed-in one; there is no user id in any of these calls.
  profile: () => request<Profile>('/api/auth/profile'),
  updateProfile: (b: { displayName: string; email: string | null }) => request<Profile>('/api/auth/profile', { method: 'PUT', body: JSON.stringify(b) }),
  /** Raw image bytes in the body (no file name or path is sent). */
  uploadAvatar: (image: Blob) => request<Profile>('/api/auth/avatar', { method: 'PUT', body: image, headers: { 'Content-Type': image.type || 'application/octet-stream' } }),
  deleteAvatar: () => request<Profile>('/api/auth/avatar', { method: 'DELETE' }),
  avatarUrl: (version: number) => `${BASE_URL}/api/auth/avatar?v=${version}`,
  preferences: () => request<Preferences>('/api/auth/preferences'),
  savePreferences: (p: Preferences) => request<Preferences>('/api/auth/preferences', { method: 'PUT', body: JSON.stringify(p) }),
  users: (search?: string) => request<UserRow[]>(`/api/users${qs({ search })}`),
  createUser: (b: { username: string; displayName: string; role: Role; password: string; isActive: boolean }) =>
    request<UserRow>('/api/users', { method: 'POST', body: JSON.stringify(b) }),
  updateUser: (id: string, b: { displayName: string; role: Role; isActive: boolean }) =>
    request<UserRow>(`/api/users/${id}`, { method: 'PUT', body: JSON.stringify(b) }),
  /** Administrator-managed organization data (blank = not provided). */
  setUserOrganization: (id: string, b: { position: string | null; department: string | null }) =>
    request<UserRow>(`/api/users/${id}/organization`, { method: 'PUT', body: JSON.stringify(b) }),
  deleteUser: (id: string) => request<{ deleted: boolean }>(`/api/users/${id}`, { method: 'DELETE' }),
  resetPassword: (id: string, newPassword: string) =>
    request<{ reset: boolean }>(`/api/users/${id}/reset-password`, { method: 'POST', body: JSON.stringify({ newPassword }) }),
  audit: (f: AuditFilter) =>
    request<AuditRow[]>(`/api/audit${qs({ actor: f.actor, action: f.action, result: f.result, skip: f.skip ?? 0, take: f.take ?? 100 })}`),

  // PLC monitoring and configuration (CR-002)
  downloadDiagExport,
  diagExportOptions: () => request<DiagExportOptions>('/api/diagnostics/export-options'),
  diagStartExport: (b: DiagExportBody) => request<DiagExportJob>('/api/diagnostics/exports', { method: 'POST', body: JSON.stringify(b) }),
  diagGetExport: (id: string) => request<DiagExportJob>(`/api/diagnostics/exports/${encodeURIComponent(id)}`),
  diagListExports: () => request<DiagExportJob[]>('/api/diagnostics/exports'),
  plcOverview: () => request<PlcOverview>('/api/plc/overview'),
  plcMonitorPoints: (f: { plcId?: string; stationId?: string }) => request<MonitorList>(`/api/plc/monitor/points${qs({ plcId: f.plcId, stationId: f.stationId })}`),
  plcReadNow: (stationId: string, signal: string) =>
    request<ReadNowResult>(`/api/plc/stations/${encodeURIComponent(stationId)}/signals/${encodeURIComponent(signal)}/read`, { method: 'POST', body: JSON.stringify({}) }),
  plcStationIo: (stationId: string) => request<StationSnapshot>(`/api/plc/stations/${encodeURIComponent(stationId)}/io`),
  plcHandshake: () => request<HandshakeState[]>('/api/plc/handshake'),
  plcPoints: (f: { plcId?: string; stationId?: string }) => request<IoPointConfig[]>(`/api/plc/points${qs({ plcId: f.plcId, stationId: f.stationId })}`),
  plcConnections: () => request<PlcConnection[]>('/api/plc/connections'),
  plcSaveConnection: (plcId: string, b: SavePlcConnectionBody) => request<PlcConnection>(`/api/plc/connections/${plcId}`, { method: 'PUT', body: JSON.stringify(b) }),
  plcTestConnection: (plcId: string) => request<ProbeResultView>(`/api/plc/connections/${plcId}/test`, { method: 'POST' }),
  plcBulkApprovalPreview: (plcId: string) => request<BulkApprovalPreview>(`/api/plc/connections/${plcId}/bulk-approval`),
  plcBulkApprove: (plcId: string, expectedEligible: number) =>
    request<BulkApprovalResult>(`/api/plc/connections/${plcId}/bulk-approval`, { method: 'POST', body: JSON.stringify({ confirm: true, expectedEligible }) }),
  plcAddressCheck: (plcId: string) => request<AddressIssue[]>(`/api/plc/connections/${plcId}/address-check`),
  plcSetAddress: (id: string, b: { address: string; wordLength?: number | null; encoding?: string | null }) =>
    request<IoPointState>(`/api/plc/points/${id}/address`, { method: 'PUT', body: JSON.stringify(b) }),
  plcClearAddress: (id: string) => request<IoPointState>(`/api/plc/points/${id}/address`, { method: 'DELETE' }),
  plcApprovePoint: (id: string) => request<IoPointState>(`/api/plc/points/${id}/approve`, { method: 'POST' }),
  plcWriteApproval: (id: string, decision: 'Approve' | 'Reject', note?: string) =>
    request<IoPointConfig>(`/api/plc/points/${id}/write-approval`, { method: 'POST', body: JSON.stringify({ decision, note }) }),
  plcWriteAllowed: (id: string, allowed: boolean) => request<IoPointState>(`/api/plc/points/${id}/write-allowed`, { method: 'PUT', body: JSON.stringify({ allowed }) }),
  // CR-010 multi-leg Transport Tasks, Station Transactions and configurable Business Processes
  routingOrders: (state?: string, take = 100) => request<OrderPage>(`/api/routing/orders${qs({ state, take })}`),
  routingOrder: (id: string) => request<OrderDetail>(`/api/routing/orders/${id}`),
  routingStartable: () => request<StartableProcess[]>('/api/routing/startable-processes'),
  routingStart: (b: { requestKey: string; locationCode: string; requestType: string; rackKind?: string; processCode: string }) =>
    request<{ order: Order; created: boolean }>('/api/routing/orders', { method: 'POST', body: JSON.stringify(b) }),
  routingCancel: (id: string, reason?: string) => request<Order>(`/api/routing/orders/${id}/cancel`, { method: 'POST', body: JSON.stringify({ reason }) }),
  routingReleaseRacks: (id: string, reason: string) => request<Order>(`/api/routing/orders/${id}/release-racks`, { method: 'POST', body: JSON.stringify({ reason }) }),
  routingPick: (legId: string) => request<unknown>(`/api/routing/legs/${legId}/pick`, { method: 'POST' }),
  routingDrop: (legId: string) => request<unknown>(`/api/routing/legs/${legId}/drop`, { method: 'POST', body: JSON.stringify({}) }),
  routingTransactions: (activeOnly: boolean) => request<StationTransaction[]>(`/api/routing/transactions${qs({ activeOnly: activeOnly ? 'true' : undefined, take: 200 })}`),
  routingTxExit: (id: string) => request<StationTransaction>(`/api/routing/transactions/${id}/exit`, { method: 'POST' }),
  routingTxResolve: (id: string, resumeState: string | null) => request<StationTransaction>(`/api/routing/transactions/${id}/resolve`, { method: 'POST', body: JSON.stringify({ resumeState }) }),
  processes: () => request<Process[]>('/api/processes'),
  process: (id: string) => request<Process>(`/api/processes/${id}`),
  processCreate: (b: ProcessHeaderBody) => request<Process>('/api/processes', { method: 'POST', body: JSON.stringify(b) }),
  processUpdate: (id: string, b: ProcessHeaderBody) => request<Process>(`/api/processes/${id}`, { method: 'PUT', body: JSON.stringify(b) }),
  processNewVersion: (id: string, fromVersionId?: string) => request<ProcessVersion>(`/api/processes/${id}/versions`, { method: 'POST', body: JSON.stringify({ fromVersionId }) }),
  processImportReference: () => request<ImportResult>('/api/processes/import-reference', { method: 'POST' }),
  processAudit: () => request<AuditRow[]>('/api/processes/audit?take=200'),
  processVersion: (id: string) => request<ProcessVersion>(`/api/process-versions/${id}`),
  processSaveDefinition: (id: string, d: VersionDefinition) => request<ProcessVersion>(`/api/process-versions/${id}/definition`, { method: 'PUT', body: JSON.stringify(d) }),
  processValidate: (id: string) => request<ProcessVersion>(`/api/process-versions/${id}/validate`, { method: 'POST' }),
  processPublish: (id: string) => request<ProcessVersion>(`/api/process-versions/${id}/publish`, { method: 'POST' }),
  processDisable: (id: string) => request<ProcessVersion>(`/api/process-versions/${id}/disable`, { method: 'POST' }),
  // CR-040: Operational Readiness of a version and the queue ordering scope of a process (Admin)
  processReadiness: (versionId: string, readiness: 'Ready' | 'Suspended' | 'NotReady', reason: string) =>
    request<unknown>(`/api/process-versions/${versionId}/readiness`, { method: 'PUT', body: JSON.stringify({ readiness, reason }) }),
  processQueueScope: (processId: string, scope: string | null, reason: string) =>
    request<QueueScopeResult>(`/api/processes/${processId}/queue-scope`, { method: 'PUT', body: JSON.stringify({ scope, reason }) }),
  // CR-040: one rack record (Admin)
  rackEditability: (id: string) => request<{ editable: boolean; code: string | null; message: string | null }>(`/api/inventory/racks/${id}/editability`),
  processApplyCatalog: (processId: string, reason: string) => request<ProcessVersion>(`/api/processes/${processId}/apply-catalog`, { method: 'POST', body: JSON.stringify({ reason }) }),
  rackCreate: (b: { locationCode: string; rackKind: 'Full' | 'Empty'; externalRackId: string | null; reason: string; rackType?: string; productModelId?: string }) =>
    request<Rack>('/api/inventory/racks', { method: 'POST', body: JSON.stringify(b) }),
  rackUpdate: (id: string, b: { locationCode?: string; rackKind?: 'Full' | 'Empty'; externalRackId?: string; reason: string; rackType?: string; productModelId?: string }) =>
    request<Rack>(`/api/inventory/racks/${id}`, { method: 'PUT', body: JSON.stringify(b) }),
  rackDelete: (id: string, reason: string) => request<{ id: string }>(`/api/inventory/racks/${id}${qs({ reason })}`, { method: 'DELETE' }),
  plcAdd: (b: AddPlcBody) => request<PlcConnection>('/api/plc/connections', { method: 'POST', body: JSON.stringify(b) }),
  plcStations: () => request<PortView[]>('/api/plc/stations'),
  plcLinkPort: (stationId: string, locationCode: string | null, enabled: boolean) =>
    request<PortView>(`/api/plc/stations/${encodeURIComponent(stationId)}/link`, { method: 'PUT', body: JSON.stringify({ locationCode, enabled }) }),
  // CR-007 Logging & Diagnostics (Admin only; the API enforces it)
  logsStats: (from?: string, to?: string) => request<LogStats>(`/api/logs/stats${qs({ from, to })}`),
  logsSearch: (f: LogFilter) =>
    request<LogPage>(`/api/logs${qs({ from: f.from, to: f.to, category: f.category, level: f.level, device: f.device, source: f.source, eventCode: f.eventCode, keyword: f.keyword, page: f.page ?? 1, pageSize: f.pageSize ?? 50 })}`),
  logDetail: (id: string) => request<LogDetail>(`/api/logs/${encodeURIComponent(id)}`),
  plcLogs: (f: { plcId?: string; stationId?: string; minLevel?: string; take?: number }) =>
    request<PlcLogRow[]>(`/api/plc/logs${qs({ plcId: f.plcId, stationId: f.stationId, minLevel: f.minLevel, take: f.take ?? 100 })}`),
  plcTestWrite: (stationId: string, signal: string, value: boolean | number | string) =>
    request<TestWriteResult>(`/api/plc/stations/${encodeURIComponent(stationId)}/signals/${encodeURIComponent(signal)}/test-write`, { method: 'POST', body: JSON.stringify({ value }) }),
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

  // CR-026: Maintenance flag (everyone reads, Admin switches) and configuration export / import (Admin)
  maintenance: () => request<MaintenanceView>('/api/system/maintenance'),
  setMaintenance: (enabled: boolean, reason: string | null) => request<MaintenanceView>('/api/system/maintenance', { method: 'PUT', body: JSON.stringify({ enabled, reason }) }),
  productModels: () => request<ProductModelInfo[]>('/api/product-models'),   // CR-036 (A.2), Admin
  productModelCreate: (code: string, name: string) => request<ProductModelInfo>('/api/product-models', { method: 'POST', body: JSON.stringify({ code, name }) }),   // A.3
  productModelUpdate: (id: string, name: string, enabled: boolean) => request<ProductModelInfo>(`/api/product-models/${id}`, { method: 'PUT', body: JSON.stringify({ name, enabled }) }),   // A.3: code cannot change
  transferStatus: () => request<TransferStatus>('/api/config-transfer/status'),
  transferGroups: () => request<TransferGroup[]>('/api/config-transfer/groups'),
  transferExportCheck: (groups: TransferGroupId[]) => request<ExportDependencyReport>('/api/config-transfer/exports/check', { method: 'POST', body: JSON.stringify({ groups }) }),
  transferExport: (groups: TransferGroupId[]) => postForZip('/api/config-transfer/exports', { groups }, 'wcs-config.zip'),
  transferUpload: (file: File) => uploadPackage(file),
  transferPreview: (id: string, selection: Selection) => request<ImportPlan>(`/api/config-transfer/imports/${id}/preview`, { method: 'POST', body: JSON.stringify(selection) }),
  // CR-035 Admin manual PLC simulation (PLC only; RCS is real). The server enforces Admin and the safety acknowledgement.
  queueAlerts: () => request<QueueAlert[]>('/api/routing/queue-alerts'),
  simStatus: () => request<SimStatus>('/api/plc-simulation/status'),
  simActivate: (confirmed: boolean) => request<SimStatus>('/api/plc-simulation/activate', { method: 'POST', body: JSON.stringify({ confirmed }) }),
  simStations: () => request<SimStation[]>('/api/plc-simulation/stations'),
  simSetSignal: (stationId: string, signal: string, value: string) =>
    request<SimStation>(`/api/plc-simulation/stations/${encodeURIComponent(stationId)}/signals/${encodeURIComponent(signal)}`, { method: 'PUT', body: JSON.stringify({ value }) }),
  simEvaluate: (stationId: string, safetyAcknowledged: boolean) =>
    request<SimStartResult>(`/api/plc-simulation/stations/${encodeURIComponent(stationId)}/evaluate`, { method: 'POST', body: JSON.stringify({ safetyAcknowledged }) }),
  simReset: (stationId: string) => request<SimStation>(`/api/plc-simulation/stations/${encodeURIComponent(stationId)}/reset`, { method: 'POST' }),
  simHistory: () => request<AuditRow[]>('/api/plc-simulation/history?take=100'),
  simDeactivate: () => request<SimStatus>('/api/plc-simulation/deactivate', { method: 'POST' }),
  simFlows: () => request<SimFlow[]>('/api/plc-simulation/flows'),
  simStart: (flowCode: string, locationCode: string, safetyAcknowledged: boolean) =>
    request<SimStartResult>('/api/plc-simulation/requests', { method: 'POST', body: JSON.stringify({ flowCode, locationCode, safetyAcknowledged }) }),
  simConfirm: (legId: string, drop: boolean) =>
    request<unknown>(`/api/plc-simulation/legs/${legId}/confirm-${drop ? 'drop' : 'pick'}`, { method: 'POST', body: JSON.stringify({ externalRackId: null }) }),
  // CR-032 RCS Diagnostics (Admin only): reading never calls the RCS; only execute / tcp-check / api-check do, and only when the Admin presses the button
  rcsDiagOverview: () => request<RcsOverview>('/api/rcs-diagnostics/overview'),
  rcsDiagCatalog: () => request<RcsCatalog>('/api/rcs-diagnostics/catalog'),
  rcsDiagPreview: (api: string, body: unknown) => request<RcsPreview>('/api/rcs-diagnostics/preview', { method: 'POST', body: JSON.stringify({ api, body }) }),
  rcsDiagExecute: (api: string, body: unknown) => request<RcsExecution>('/api/rcs-diagnostics/execute', { method: 'POST', body: JSON.stringify({ api, body }) }),
  rcsDiagTcpCheck: () => request<RcsCheckState>('/api/rcs-diagnostics/tcp-check', { method: 'POST' }),
  rcsDiagApiCheck: () => request<RcsExecution>('/api/rcs-diagnostics/api-check', { method: 'POST' }),
  rcsDiagHistory: (f: RcsHistoryFilter) => request<RcsHistoryPage>('/api/rcs-diagnostics/history' + qs({ ...f })),
  rcsDiagHistoryItem: (id: string) => request<RcsHistoryItem>(`/api/rcs-diagnostics/history/${encodeURIComponent(id)}`),
  rcsDiagExport: (f: RcsHistoryFilter) => downloadZip('/api/rcs-diagnostics/history/export' + qs({ ...f }), 'rcs-diagnostics-history.json'),
  rcsDiagParameters: () => request<RcsParameters>('/api/rcs-diagnostics/parameters'),
  rcsDiagSaveParameter: (key: string, value: string) => request<RcsParameter>(`/api/rcs-diagnostics/parameters/${encodeURIComponent(key)}`, { method: 'PUT', body: JSON.stringify({ value }) }),
  rcsDiagCallbacks: (callback?: string) => request<RcsCallbacks>('/api/rcs-diagnostics/callbacks' + qs({ callback })),
  transferApply: (id: string, body: ApplyBody) => request<ApplyResult>(`/api/config-transfer/imports/${id}/apply`, { method: 'POST', body: JSON.stringify(body) }),
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
