import { ApiRequestError } from '../api/errors'
import type { SimulationStep, TaskState } from '../api/types'
import { demo, DemoError } from './engine'
import { getPlc, getRcs, savePlc, saveRcs, testPlc, testRcs } from './demoConfig'

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

/** Same surface as the real API client, answered from memory. Contains no fetch / XHR / WebSocket. */
export const demoApi = {
  health: () => call(() => demo.health()),
  agvs: () => call(() => demo.listAgvs()),
  locations: () => call(() => demo.listLocations()),
  racks: (location?: string) => call(() => demo.listRacks(location)),
  transactions: (location?: string) => call(() => demo.listTransactions(location)),
  tasks: (state?: TaskState, skip = 0, take = 50) => call(() => demo.listTasks(state, skip, take)),
  task: (id: string) => call(() => demo.getTask(id)),
  taskHistory: (id: string) => call(() => demo.getHistory(id)),
  cancelTask: (id: string, reason: string) => call(() => demo.cancelTask(id, reason)),
  simulateNext: (id: string) =>
    call((): SimulationStep => {
      const message = demo.advance(id)
      return { signal: 'demo', message, task: demo.getTask(id) }
    }),

  // configuration: in-memory only (no network, nothing persisted)
  plcConfig: () => call(() => getPlc()),
  savePlc: (b: Parameters<typeof savePlc>[0]) => call(() => savePlc(b)),
  testPlc: () => call(() => testPlc()),
  rcsConfig: () => call(() => getRcs()),
  saveRcs: (b: Parameters<typeof saveRcs>[0]) => call(() => saveRcs(b)),
  testRcs: () => call(() => testRcs()),
  operations: () => call(() => demo.operations()),
  saveAgv: (id: string, b: { rcsRobotCode: string | null; enabled: boolean }) =>
    call(() => demo.configureAgv(id, b.rcsRobotCode && b.rcsRobotCode.trim() ? b.rcsRobotCode.trim() : null, b.enabled)),
  createLocation: (b: Parameters<typeof demo.saveLocation>[0]) => call(() => demo.saveLocation(b, true)),
  saveLocation: (b: Parameters<typeof demo.saveLocation>[0]) => call(() => demo.saveLocation(b, false)),
}
