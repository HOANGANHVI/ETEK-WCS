import type { RcsMapView } from '../api/rcsMapTypes'
import type { AmrLiveRun } from '../api/simTypes'
import { useT } from '../i18n'
import { drawY, type Bounds } from '../lib/rcsMap'
import { splitRoute, type RackSpot } from '../lib/simMap'

/** CR-069: a rack seen from above: filled = full, outline = empty. */
export function RackIcon({ full }: { full: boolean | null }) {
  return (
    <g className={`rack-icon ${full === true ? 'full' : full === false ? 'empty' : 'unknown'}`}>
      <rect x={-8} y={-8} width={16} height={16} rx={2} />
      <path d="M-8 -2.5 H8 M-8 2.5 H8" />
    </g>
  )
}

/** An AMR seen from above, drawn pointing to the right; <paramref name="rotate"/> turns it, the rack it carries stays upright on top. */
export function AmrIcon({ rotate }: { rotate: number | null }) {
  return (
    <g className="amr-icon" transform={`rotate(${rotate ?? 0})`}>
      <rect className="amr-wheel" x={-9} y={-13} width={18} height={4} rx={2} />
      <rect className="amr-wheel" x={-9} y={9} width={18} height={4} rx={2} />
      <rect className="amr-body" x={-14} y={-11} width={28} height={22} rx={7} />
      {rotate !== null && <path className="amr-front" d="M4 -6 L12 0 L4 6 Z" />}
      <circle className="amr-lamp" cx={-7} cy={0} r={3} />
    </g>
  )
}

/** Routes of the open runs: the driven part, the part ahead, and (before the pick) the planned way to the drop. */
export function Routes({ map, bounds, runs }: { map: RcsMapView; bounds: Bounds; runs: AmrLiveRun[] }) {
  const line = (p: number[][]) => p.map((c, i) => `${i === 0 ? 'M' : 'L'}${c[0] / map.info.mmPerUnit} ${drawY(map, bounds, c[1] / map.info.mmPerUnit)}`).join(' ')
  return (
    <g className="sim-routes">
      {runs.map((r) => {
        const [done, ahead] = splitRoute(r.path, r.travelled)
        return (
          <g key={r.requestCode} data-route={r.requestCode}>
            {r.nextPath.length > 1 && <path className="sim-route next" d={line(r.nextPath)} />}
            {ahead.length > 1 && <path className="sim-route ahead" d={line(ahead)} />}
            {done.length > 1 && <path className="sim-route done" d={line(done)} />}
          </g>
        )
      })}
    </g>
  )
}

/** The racks standing at locations. */
export function Racks({ map, bounds, spots, upp }: { map: RcsMapView; bounds: Bounds; spots: RackSpot[]; upp: number }) {
  const t = useT()
  return (
    <g className="sim-racks">
      {spots.map((s) => {
        const p = map.geometry.points.find((x) => x.id === s.pointId)
        if (!p) return null
        return (
          <g key={s.pointId} data-rack-at={s.location} transform={`translate(${p.x} ${drawY(map, bounds, p.y)}) scale(${upp})`}>
            <RackIcon full={s.racks[0].full} />
            {s.racks.length > 1 && <text className="rack-count" y={4} x={13}>×{s.racks.length}</text>}
            <title>{`${s.location}: ${s.racks.map((r) => `${r.id} · ${r.full === true ? t('agv.simFull') : r.full === false ? t('agv.simEmpty') : '?'}`).join(', ')}`}</title>
          </g>
        )
      })}
    </g>
  )
}
