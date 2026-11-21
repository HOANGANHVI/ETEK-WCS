import { useMemo } from 'react'
import type { Agv, Location, TransportTask } from '../api/types'
import { OCCUPYING_STATES } from '../i18n/vi'

/** id -> display code helpers shared by the pages. */
export function useCodeMaps(locations: Location[] | null | undefined, agvs: Agv[] | null | undefined) {
  return useMemo(() => {
    const loc = new Map((locations ?? []).map((l) => [l.id, l.code]))
    const agv = new Map((agvs ?? []).map((a) => [a.id, a.code]))
    return {
      loc: (id: string | null) => (id ? loc.get(id) ?? id.slice(0, 8) : '—'),
      agv: (id: string | null | undefined) => (id ? agv.get(id) ?? '—' : '—'),
    }
  }, [locations, agvs])
}

export const isActive = (t: TransportTask) => OCCUPYING_STATES.includes(t.state)
