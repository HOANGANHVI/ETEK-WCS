import { createContext, useContext } from 'react'
import type { FleetView } from '../api/fleetTypes'
import type { ApiState } from '../hooks/useApi'

export const FleetContext = createContext<ApiState<FleetView> | null>(null)

/** The shared fleet view (see FleetProvider). */
export function useFleet(): ApiState<FleetView> {
  const v = useContext(FleetContext)
  if (!v) throw new Error('useFleet must be used inside FleetProvider')
  return v
}
