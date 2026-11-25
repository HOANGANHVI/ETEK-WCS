import type { StartableProcess } from '../api/routingTypes'

/**
 * CR-039: the "what to request" list of both request forms = the Phase 1 flows of the server configuration (POST /api/tasks) followed by EVERY published process
 * (POST /api/routing/orders). Nothing here knows a process code or a station name; both lists come from the server.
 */
const PREFIX = 'process:'
export const processValue = (code: string): string => PREFIX + code
export const processOf = (value: string, processes: StartableProcess[]): StartableProcess | undefined =>
  value.startsWith(PREFIX) ? processes.find((p) => p.code === value.slice(PREFIX.length)) : undefined
