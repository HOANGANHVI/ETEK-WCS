import { ApiRequestError } from '../api/errors'
import type { SimulationStep, TaskState } from '../api/types'
import { DEMO_FLOWS, demo, DemoError } from './engine'
import { getPlc, getRcs, savePlc, saveRcs, testPlc, testRcs } from './demoConfig'
import { requireAdmin, requireSession } from './session'

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

/**
 * Demo Mode simulates DATA only (tasks, AGVs, inventory, configuration). Authentication, accounts and the audit trail are NOT
 * simulated: the client routes them to the real backend (see api/client.ts). The role below comes from that real session and is
 * used only so the in-browser data follows the same permission rules as the API.
 */
const read = <T>(fn: () => T) => call(() => { requireSession(); return fn() })
const adminWrite = <T>(fn: () => T) => call(() => { requireAdmin(); return fn() })

/** Same data surface as the real API client, answered from memory. Contains no fetch / XHR / WebSocket. */
export const demoDataApi = {
  health: () => read(() => demo.health()),
  agvs: () => read(() => demo.listAgvs()),
  flows: () => read(() => Object.entries(DEMO_FLOWS).map(([flow, r]) => ({ flow, rackKind: r.kind, stations: [...r.stations] }))),
  routingStartable: () => read(() => []),   // CR-039: Demo Mode has no configured processes
  locations: () => read(() => demo.listLocations()),
  racks: (location?: string) => read(() => demo.listRacks(location)),
  transactions: (location?: string) => read(() => demo.listTransactions(location)),
  tasks: (state?: TaskState, skip = 0, take = 50) => read(() => demo.listTasks(state, skip, take)),
  task: (id: string) => read(() => demo.getTask(id)),
  taskHistory: (id: string) => read(() => demo.getHistory(id)),

  createTask: (flow: string, stationCode: string, requestKey: string) =>
    call(() => { requireSession(); void requestKey; return demo.createTask(flow, stationCode) }),
  updateTask: (id: string, flow: string, stationCode: string, expectedState?: string) =>
    call(() => demo.updateTask(id, flow, stationCode, expectedState, requireSession() === 'Admin')),
  deleteTask: (id: string, expectedState?: string) =>
    call(() => demo.deleteTask(id, expectedState, requireSession() === 'Admin')),
  cancelTask: (id: string, reason: string) => adminWrite(() => demo.cancelTask(id, reason)),
  simulateNext: (id: string) =>
    adminWrite((): SimulationStep => {
      const message = demo.advance(id)
      return { signal: 'demo', message, task: demo.getTask(id) }
    }),

  // configuration: in-memory only (no network, nothing persisted). Admin only, like the backend.
  plcConfig: () => adminWrite(() => getPlc()),
  savePlc: (b: Parameters<typeof savePlc>[0]) => adminWrite(() => savePlc(b)),
  testPlc: () => adminWrite(() => testPlc()),
  rcsConfig: () => adminWrite(() => getRcs()),
  saveRcs: (b: Parameters<typeof saveRcs>[0]) => adminWrite(() => saveRcs(b)),
  testRcs: () => adminWrite(() => testRcs()),
  operations: () => adminWrite(() => demo.operations()),
  saveAgv: (id: string, b: { rcsRobotCode: string | null; enabled: boolean }) =>
    adminWrite(() => demo.configureAgv(id, b.rcsRobotCode && b.rcsRobotCode.trim() ? b.rcsRobotCode.trim() : null, b.enabled)),
  createLocation: (b: Parameters<typeof demo.saveLocation>[0]) => adminWrite(() => demo.saveLocation(b, true)),
  saveLocation: (b: Parameters<typeof demo.saveLocation>[0]) => adminWrite(() => demo.saveLocation(b, false)),
}
