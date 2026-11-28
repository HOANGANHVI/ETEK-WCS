import type { Location, Rack } from '../api/types'
import type { RcsMapView } from '../api/rcsMapTypes'
import type { AmrLiveRun } from '../api/simTypes'

/** CR-069: pure helpers of the simulation drawing on the map. */

/** Location code → point id, the rule the simulator uses: a current WCS link, else the one Excel row whose call code is exactly the code. */
export function locationPoints(map: RcsMapView): Map<string, number> {
  const out = new Map<string, number>()
  const calls = new Map<string, number[]>()
  for (const r of map.geometry.mapData?.rows ?? []) if (r.pointId !== null && r.callCode) calls.set(r.callCode.toUpperCase(), [...(calls.get(r.callCode.toUpperCase()) ?? []), r.pointId])
  for (const [code, ids] of calls) if (ids.length === 1) out.set(code, ids[0])
  for (const n of map.points ?? []) if (n.current && n.locationCode && !n.locationRemoved) out.set(n.locationCode.toUpperCase(), n.pointId)
  return out
}

export interface RackSpot { pointId: number; location: string; racks: { id: string; full: boolean | null }[] }

/** The racks WCS has at a location that has a point on the map. A rack in transport has no location and is drawn on its AMR instead. */
export function rackSpots(map: RcsMapView, locations: Location[], racks: Rack[]): RackSpot[] {
  const points = locationPoints(map)
  const codes = new Map(locations.map((l) => [l.id, l.code]))
  const spots = new Map<number, RackSpot>()
  for (const r of racks) {
    const code = r.currentLocationId ? codes.get(r.currentLocationId) : undefined
    const point = code ? points.get(code.toUpperCase()) : undefined
    if (!code || point === undefined || r.status === 'InTransit') continue
    const spot = spots.get(point) ?? { pointId: point, location: code, racks: [] }
    spot.racks.push({ id: r.externalRackId ?? r.id.slice(0, 8), full: r.status === 'Full' ? true : r.status === 'Empty' ? false : null })
    spots.set(point, spot)
  }
  return [...spots.values()]
}

/** A route cut at the distance already driven: [driven, ahead], positions as the file's units. */
export function splitRoute(path: number[][], travelled: number): [number[][], number[][]] {
  if (path.length < 2) return [[], path]
  const done: number[][] = [path[0]]
  let left = travelled
  for (let i = 1; i < path.length; i++) {
    const a = path[i - 1]; const b = path[i]; const d = Math.hypot(b[0] - a[0], b[1] - a[1])
    if (left < d) {
      const cut = [a[0] + (b[0] - a[0]) * (left / d), a[1] + (b[1] - a[1]) * (left / d)]
      return [left > 0 ? [...done, cut] : [], [cut, ...path.slice(i)]]
    }
    left -= d; done.push(b)
  }
  return [done, []]
}

export const isOpenRun = (r: AmrLiveRun): boolean => r.phase !== 'Completed' && r.phase !== 'Cancelled'
