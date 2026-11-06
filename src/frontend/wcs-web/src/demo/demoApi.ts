import { ApiRequestError } from '../api/errors'
import type { SimulationStep, TaskState } from '../api/types'
import type { AuditFilter, Role } from '../api/authTypes'
import { DEMO_FLOWS, demo, DemoError } from './engine'
import { getPlc, getRcs, savePlc, saveRcs, testPlc, testRcs } from './demoConfig'
import { demoAuth, record, requireAdmin, requireSession } from './demoAuth'

/** Small latency so loading states are visible and the UI behaves like it does against a real API. */
const LATENCY_MS = 120

async function call<T>(fn: () => T): Promise<T> {
  await new Promise((r) => setTimeout(r, LATENCY_MS))
  try {
    return structuredClone(fn())
  } catch (e) {
    if (e instanceof DemoError) throw new ApiRequestError(e.status, { code: e.code, message: e.message, details: e.details })
    throw e
  }
}

/** Reads need a session; every write below needs Admin unless it is a task create/edit/delete (both roles). Mirrors the backend policies. */
const read = <T>(fn: () => T) => call(() => { requireSession(); return fn() })
const adminWrite = <T>(fn: () => T) => call(() => { requireAdmin(); return fn() })

/** Same surface as the real API client, answered from memory. Contains no fetch / XHR / WebSocket. */
export const demoApi = {
  health: () => read(() => demo.health()),
  agvs: () => read(() => demo.listAgvs()),
  flows: () => read(() => Object.entries(DEMO_FLOWS).map(([flow, r]) => ({ flow, rackKind: r.kind, stations: [...r.stations] }))),
  locations: () => read(() => demo.listLocations()),
  racks: (location?: string) => read(() => demo.listRacks(location)),
  transactions: (location?: string) => read(() => demo.listTransactions(location)),
  tasks: (state?: TaskState, skip = 0, take = 50) => read(() => demo.listTasks(state, skip, take)),
  task: (id: string) => read(() => demo.getTask(id)),
  taskHistory: (id: string) => read(() => demo.getHistory(id)),

  createTask: (flow: string, stationCode: string, requestKey: string) =>
    call(() => {
      requireSession()
      void requestKey
      const t = demo.createTask(flow, stationCode)
      record('TASK_CREATE', 'Task', t.id, 'Success', `flow=${flow}, station=${stationCode}`)
      return t
    }),
  updateTask: (id: string, stationCode: string, expectedState?: string) =>
    call(() => {
      const me = requireSession()
      try {
        const t = demo.updateTask(id, stationCode, expectedState, me.role === 'Admin')
        record('TASK_UPDATE', 'Task', id, 'Success', `station=${stationCode}`)
        return t
      } catch (e) {
        if (e instanceof DemoError) record('TASK_UPDATE', 'Task', id, e.status === 403 ? 'Denied' : 'Failed', e.code)
        throw e
      }
    }),
  deleteTask: (id: string, expectedState?: string) =>
    call(() => {
      const me = requireSession()
      try {
        const t = demo.deleteTask(id, expectedState, me.role === 'Admin')
        record('TASK_DELETE', 'Task', id)
        return t
      } catch (e) {
        if (e instanceof DemoError) record('TASK_DELETE', 'Task', id, e.status === 403 ? 'Denied' : 'Failed', e.code)
        throw e
      }
    }),
  cancelTask: (id: string, reason: string) => adminWrite(() => { const t = demo.cancelTask(id, reason); record('TASK_CANCEL', 'Task', id, 'Success', reason); return t }),
  simulateNext: (id: string) =>
    adminWrite((): SimulationStep => {
      const message = demo.advance(id)
      record('SIMULATOR_ADVANCE', 'Task', id)
      return { signal: 'demo', message, task: demo.getTask(id) }
    }),

  // configuration: in-memory only (no network, nothing persisted). Admin only, like the backend.
  plcConfig: () => adminWrite(() => getPlc()),
  savePlc: (b: Parameters<typeof savePlc>[0]) => adminWrite(() => { const r = savePlc(b); record('CONFIG_PLC_UPDATE', 'ConnectionSetting', 'Plc'); return r }),
  testPlc: () => adminWrite(() => testPlc()),
  rcsConfig: () => adminWrite(() => getRcs()),
  saveRcs: (b: Parameters<typeof saveRcs>[0]) => adminWrite(() => { const r = saveRcs(b); record('CONFIG_RCS_UPDATE', 'ConnectionSetting', 'Rcs'); return r }),
  testRcs: () => adminWrite(() => testRcs()),
  operations: () => adminWrite(() => demo.operations()),
  saveAgv: (id: string, b: { rcsRobotCode: string | null; enabled: boolean }) =>
    adminWrite(() => demo.configureAgv(id, b.rcsRobotCode && b.rcsRobotCode.trim() ? b.rcsRobotCode.trim() : null, b.enabled)),
  createLocation: (b: Parameters<typeof demo.saveLocation>[0]) => adminWrite(() => demo.saveLocation(b, true)),
  saveLocation: (b: Parameters<typeof demo.saveLocation>[0]) => adminWrite(() => demo.saveLocation(b, false)),

  // session + accounts (Demo Mode only; see demoAuth.ts)
  login: (username: string, _password: string) => { void _password; return call(() => demoAuth.login(username)) },
  logout: () => call(() => demoAuth.logout()),
  me: () => call(() => demoAuth.me()),
  changePassword: (_current: string, _next: string) => { void _current; void _next; return call(() => demoAuth.changePassword()) },
  users: (search?: string) => call(() => demoAuth.listUsers(search)),
  createUser: (b: { username: string; displayName: string; role: Role; password: string }) => call(() => demoAuth.createUser(b)),
  updateUser: (id: string, b: { displayName: string; role: Role; isActive: boolean }) => call(() => demoAuth.updateUser(id, b)),
  resetPassword: (id: string, _newPassword: string) => { void _newPassword; return call(() => demoAuth.resetPassword(id)) },
  audit: (f: AuditFilter) => call(() => demoAuth.listAudit(f)),
}
