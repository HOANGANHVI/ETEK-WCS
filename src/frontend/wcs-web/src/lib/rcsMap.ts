import type { FleetAmr, FleetView } from '../api/fleetTypes'
import type { RcsMapView } from '../api/rcsMapTypes'

/** CR-065: pure geometry of the AMR map. "Drawing coordinates" are the units of the file, with y turned downwards for SVG when the file's y points up. */
export interface Bounds { minX: number; minY: number; maxX: number; maxY: number }

export function boundsOf(map: RcsMapView): Bounds {
  const xs: number[] = []; const ys: number[] = []
  for (const p of map.geometry.points) { xs.push(p.x); ys.push(p.y) }
  for (const e of map.geometry.edges) for (const c of e.ctrl) { xs.push(c[0]); ys.push(c[1]) }
  return xs.reduce((b, x, i) => ({ minX: Math.min(b.minX, x), maxX: Math.max(b.maxX, x), minY: Math.min(b.minY, ys[i]), maxY: Math.max(b.maxY, ys[i]) }),
    { minX: Infinity, minY: Infinity, maxX: -Infinity, maxY: -Infinity })
}

export const drawY = (map: RcsMapView, b: Bounds, y: number): number => (map.info.yAxisUp ? b.minY + b.maxY - y : y)

const num = (v: string | null): number | null => { if (v === null || v.trim() === '') return null; const n = Number(v); return Number.isFinite(n) ? n : null }

/** CR-066: for every point the distance to its nearest neighbour, so that the click areas of two points never overlap. Sweep over x. */
export function nearestDistances(points: { id: number; x: number; y: number }[]): Map<number, number> {
  const s = [...points].sort((a, b) => a.x - b.x)
  const out = new Map<number, number>()
  s.forEach((p, i) => {
    let best = Infinity
    for (let j = i + 1; j < s.length && s[j].x - p.x < best; j++) best = Math.min(best, Math.hypot(s[j].x - p.x, s[j].y - p.y))
    for (let j = i - 1; j >= 0 && p.x - s[j].x < best; j--) best = Math.min(best, Math.hypot(s[j].x - p.x, s[j].y - p.y))
    out.set(p.id, best)
  })
  return out
}

/** CR-066: the short text of a named or linked point: the location code, then the name. */
export const pointLabel = (n: { name: string | null; locationCode: string | null }, full: boolean): string =>
  full ? [n.locationCode, n.name].filter(Boolean).join(' · ') : n.locationCode ?? n.name ?? ''

/** Green only when the RCS link answers and this AMR was in the latest answer; everything else is grey. */
export const isLive = (a: FleetAmr, fleet: FleetView, loadFailed: boolean): boolean => !loadFailed && fleet.connection === 'Connected' && a.state === 'Fresh'

export type Placement =
  | { kind: 'drawn'; amr: FleetAmr; x: number; y: number; rotate: number | null }
  | { kind: 'otherMap' | 'noPosition' | 'outside'; amr: FleetAmr }

/**
 * Where an AMR goes on the map. Nothing is assumed: another mapCode, a missing coordinate or a position far outside the map is listed instead of drawn.
 * rotate = degrees clockwise on the screen for a marker that points to the right; null = the RCS reports no direction.
 */
export function placeAmr(map: RcsMapView, b: Bounds, amr: FleetAmr): Placement {
  const codes = [map.info.mapId, ...map.info.rcsMapCodes].map((c) => c.toUpperCase())
  if (!amr.mapCode || !codes.includes(amr.mapCode.trim().toUpperCase())) return { kind: 'otherMap', amr }
  const px = num(amr.posX); const py = num(amr.posY)
  if (px === null || py === null) return { kind: 'noPosition', amr }
  const x = px / map.info.mmPerUnit; const y = py / map.info.mmPerUnit
  const marginX = (b.maxX - b.minX) * 0.1 + 1; const marginY = (b.maxY - b.minY) * 0.1 + 1
  if (x < b.minX - marginX || x > b.maxX + marginX || y < b.minY - marginY || y > b.maxY + marginY) return { kind: 'outside', amr }
  const dir = num(amr.robotDir)
  const rotate = dir === null ? null : -((map.info.headingClockwise ? -dir : dir) + map.info.headingOffsetDeg)
  return { kind: 'drawn', amr, x, y: drawY(map, b, y), rotate }
}
