import { useCallback, useMemo } from 'react'
import { api } from '../api/client'
import { useApi } from './useApi'
import type { Agv, Location, TransportTask } from '../api/types'
import { OCCUPYING_STATES } from '../i18n/vi'

/** id -> display code helpers shared by the pages. */
export function useCodeMaps(locations: Location[] | null | undefined, agvs: Agv[] | null | undefined) {
  // CR-057: past records may point at a location that was removed from the system
  const removed = useApi(useCallback(() => api.removedLocations(), [])).data
  return useMemo(() => {
    const loc = new Map([...(removed ?? []), ...(locations ?? [])].map((l) => [l.id, l.code]))
    const agv = new Map((agvs ?? []).map((a) => [a.id, a.code]))
    return {
      loc: (id: string | null) => (id ? loc.get(id) ?? id.slice(0, 8) : '—'),
      agv: (id: string | null | undefined) => (id ? agv.get(id) ?? '—' : '—'),
    }
  }, [locations, agvs, removed])
}

export const isActive = (t: TransportTask) => OCCUPYING_STATES.includes(t.state)
