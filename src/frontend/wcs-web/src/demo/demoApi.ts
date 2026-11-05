import { ApiRequestError } from '../api/errors'
import type { SimulationStep, TaskState } from '../api/types'
import { demo, DemoError } from './engine'

/** Small latency so loading states are visible and the UI behaves like it does against a real API. */
const LATENCY_MS = 120

async function call<T>(fn: () => T): Promise<T> {
  await new Promise((r) => setTimeout(r, LATENCY_MS))
  try {
    return structuredClone(fn())
  } catch (e) {
    if (e instanceof DemoError) throw new ApiRequestError(e.status, { code: e.code, message: e.message })
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
}
