import { useCallback, useEffect, useRef, type ReactNode } from 'react'
import { api } from '../api/client'
import { useApi } from '../hooks/useApi'
import { FleetContext } from './fleetState'

/**
 * The ONE copy of the fleet view the screens share: the header link status, the AMR monitoring and the dashboard all read it, so they can never disagree.
 * It reloads when the server says the fleet changed AND on a timer of one polling interval: the connection state (Connected / Degraded / Disconnected) depends on how old the last
 * successful poll is, and nothing is published when time merely passes.
 */
export function FleetProvider({ children }: { children: ReactNode }) {
  const state = useApi(useCallback(() => api.agvFleet(), []), ['FleetChanged', 'TaskStateChanged'])
  const reload = useRef(state.reload)
  useEffect(() => { reload.current = state.reload })
  const seconds = state.data?.intervalSeconds ?? 5
  useEffect(() => {
    const timer = setInterval(() => reload.current(), Math.max(2, seconds) * 1000)
    return () => clearInterval(timer)
  }, [seconds])
  return <FleetContext.Provider value={state}>{children}</FleetContext.Provider>
}
