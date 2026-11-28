import { useCallback, useEffect, useMemo, useRef, useState, type PointerEvent as ReactPointerEvent } from 'react'
import type { FleetView } from '../api/fleetTypes'
import type { RcsMapView } from '../api/rcsMapTypes'
import { hasMessage, useT, type MsgKey } from '../i18n'
import { formatDateTime } from '../i18n/format'
import { boundsOf, drawY, isLive, placeAmr, type Bounds, type Placement } from '../lib/rcsMap'
import { Button, Pill } from './ui'

interface Box { x: number; y: number; w: number; h: number }
const fitBox = (b: Bounds): Box => {
  const w = Math.max(b.maxX - b.minX, 1); const h = Math.max(b.maxY - b.minY, 1); const pad = Math.max(w, h) * 0.03
  return { x: b.minX - pad, y: b.minY - pad, w: w + pad * 2, h: h + pad * 2 }
}

/** CR-065: the connections and points of the imported map. Drawn once per map and zoom level. */
function Roads({ map, bounds, upp }: { map: RcsMapView; bounds: Bounds; upp: number }) {
  const byId = useMemo(() => new Map(map.geometry.points.map((p) => [p.id, p])), [map])
  const path = useMemo(() => map.geometry.edges.map((e) => {
    const a = byId.get(e.from); const z = byId.get(e.to)
    if (!a || !z) return ''
    return [[a.x, a.y], ...e.ctrl, [z.x, z.y]].map((c, i) => `${i === 0 ? 'M' : 'L'}${c[0]} ${drawY(map, bounds, c[1])}`).join(' ')
  }).join(' '), [map, bounds, byId])
  const r = Math.min(Math.max(0.09, 2 * upp), 6 * upp)
  return (
    <>
      <path d={path} className="amr-map-road" />
      {map.geometry.points.map((p) => (
        <circle key={p.id} cx={p.x} cy={drawY(map, bounds, p.y)} r={r} className="amr-map-point">
          <title>{`#${p.id} (${p.x}, ${p.y})${p.value ? ` · value ${p.value}` : ''}`}</title>
        </circle>
      ))}
      {upp < 0.012 && map.geometry.points.map((p) => (
        <text key={`t${p.id}`} x={p.x + r * 1.4} y={drawY(map, bounds, p.y) - r * 1.4} fontSize={11 * upp} className="amr-map-label">{p.id}</text>
      ))}
    </>
  )
}

/** CR-065: read-only 2D view of the AMRs on the imported map: zoom, pan, fit; one click shows what the RCS reports about an AMR. */
export function AmrMap({ map, fleet, loadFailed }: { map: RcsMapView; fleet: FleetView; loadFailed: boolean }) {
  const t = useT()
  const bounds = useMemo(() => boundsOf(map), [map])
  const [view, setView] = useState<Box>(() => fitBox(bounds))
  const [fitted, setFitted] = useState(bounds)
  if (fitted !== bounds) { setFitted(bounds); setView(fitBox(bounds)) }      // a new map is shown whole
  const [size, setSize] = useState({ w: 1000, h: 480 })
  const [selected, setSelected] = useState<string | null>(null)
  const svg = useRef<SVGSVGElement>(null)
  const drag = useRef<{ x: number; y: number } | null>(null)
  const upp = Math.max(view.w / size.w, view.h / size.h)      // map units per screen pixel

  useEffect(() => {
    const el = svg.current
    if (!el || typeof ResizeObserver === 'undefined') return
    const watch = new ResizeObserver(() => setSize({ w: el.clientWidth || 1, h: el.clientHeight || 1 }))
    watch.observe(el)
    return () => watch.disconnect()
  }, [])

  const zoom = useCallback((factor: number, at?: { x: number; y: number }) => setView((v) => {
    const full = fitBox(bounds)
    const w = Math.min(Math.max(v.w * factor, 0.5), full.w * 4); const k = w / v.w
    const c = at ?? { x: v.x + v.w / 2, y: v.y + v.h / 2 }
    return { x: c.x - (c.x - v.x) * k, y: c.y - (c.y - v.y) * k, w, h: v.h * k }
  }), [bounds])

  // the wheel listener must not be passive: zooming the map must not scroll the page
  useEffect(() => {
    const el = svg.current
    if (!el) return
    const onWheel = (e: WheelEvent) => {
      e.preventDefault()
      const m = el.getScreenCTM()
      const at = m ? new DOMPoint(e.clientX, e.clientY).matrixTransform(m.inverse()) : undefined
      zoom(e.deltaY > 0 ? 1.2 : 1 / 1.2, at)
    }
    el.addEventListener('wheel', onWheel, { passive: false })
    return () => el.removeEventListener('wheel', onWheel)
  }, [zoom])

  const down = (e: ReactPointerEvent<SVGSVGElement>) => {
    if ((e.target as Element).closest('.amr-map-robot')) return
    drag.current = { x: e.clientX, y: e.clientY }; e.currentTarget.setPointerCapture(e.pointerId)
  }
  const move = (e: ReactPointerEvent<SVGSVGElement>) => {
    const from = drag.current
    if (!from) return
    drag.current = { x: e.clientX, y: e.clientY }
    setView((v) => ({ ...v, x: v.x - (e.clientX - from.x) * upp, y: v.y - (e.clientY - from.y) * upp }))
  }
  const up = () => { drag.current = null }

  const placed = fleet.amrs.map((a) => placeAmr(map, bounds, a))
  const drawn = placed.filter((p): p is Extract<Placement, { kind: 'drawn' }> => p.kind === 'drawn')
  const missed = placed.filter((p) => p.kind !== 'drawn')
  const sel = fleet.amrs.find((a) => a.robotCode === selected) ?? null
  const status = (raw: string | null) => {
    if (!raw) return t('agv.noData')
    const key = `agv.amrStatus_${raw}` as MsgKey
    return hasMessage(key) ? `${raw} · ${t(key)}` : t('agv.amrStatusUnknown', { code: raw })
  }

  return (
    <div className="amr-map">
      <div className="amr-map-bar">
        <span><i className="amr-map-dot live" />{t('agv.mapLive')}</span>
        <span><i className="amr-map-dot lost" />{t('agv.mapLost')}</span>
        <span className="muted">{t('agv.mapCount', { shown: drawn.length, total: fleet.amrs.length })}</span>
        <span className="amr-map-tools">
          <Button size="sm" onClick={() => zoom(1 / 1.4)} title={t('agv.mapZoomIn')}>+</Button>
          <Button size="sm" onClick={() => zoom(1.4)} title={t('agv.mapZoomOut')}>−</Button>
          <Button size="sm" onClick={() => setView(fitBox(bounds))}>{t('agv.mapFit')}</Button>
        </span>
      </div>
      <div className="amr-map-body">
        <svg ref={svg} className="amr-map-svg" viewBox={`${view.x} ${view.y} ${view.w} ${view.h}`} role="img" aria-label={t('agv.mapTitle', { name: map.info.mapName })}
          onPointerDown={down} onPointerMove={move} onPointerUp={up} onPointerCancel={up}>
          <Roads map={map} bounds={bounds} upp={upp} />
          {drawn.map((p) => (
            <g key={p.amr.robotCode} className={`amr-map-robot ${isLive(p.amr, fleet, loadFailed) ? 'live' : 'lost'}${p.amr.robotCode === selected ? ' selected' : ''}`}
              transform={`translate(${p.x} ${p.y}) scale(${upp})`} role="button" tabIndex={0} aria-label={p.amr.robotCode} data-amr={p.amr.robotCode}
              onClick={() => setSelected(p.amr.robotCode)} onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); setSelected(p.amr.robotCode) } }}>
              <circle r={13} />
              {p.rotate !== null && <path d="M10 0 L-6 7 L-2 0 L-6 -7 Z" className="amr-map-arrow" transform={`rotate(${p.rotate})`} />}
              <text y={-18} fontSize={12}>{p.amr.robotCode}</text>
              <title>{p.amr.robotCode}</title>
            </g>
          ))}
        </svg>
        {sel && (
          <aside className="amr-map-detail">
            <div className="amr-map-detail-head"><strong className="mono">{sel.robotCode}</strong>
              <Pill tone={isLive(sel, fleet, loadFailed) ? 'ok' : 'muted'}>{isLive(sel, fleet, loadFailed) ? t('agv.mapLive') : t('agv.mapLost')}</Pill>
              <button type="button" className="modal-x" aria-label={t('common.close')} onClick={() => setSelected(null)}>×</button></div>
            <dl className="kv2">
              <dt>{t('agv.fleetColPos')}</dt><dd className="mono">{sel.posX ?? '–'}, {sel.posY ?? '–'} mm</dd>
              <dt>{t('agv.fleetColDir')}</dt><dd>{sel.robotDir ? `${sel.robotDir}°` : t('agv.noData')}</dd>
              <dt>{t('agv.mapBattery')}</dt><dd>{sel.battery ? `${sel.battery} %` : t('agv.noData')}</dd>
              <dt>{t('agv.mapActivity')}</dt><dd>{status(sel.status)}</dd>
              <dt>{t('agv.cardMap')}</dt><dd className="mono">{sel.mapCode ?? t('agv.noData')}</dd>
              <dt>{t('agv.mapPoint')}</dt><dd className="muted">{t('agv.mapPointNone')}</dd>
              <dt>{t('agv.mapLastSeen')}</dt><dd>{formatDateTime(sel.lastSeenAt)}</dd>
            </dl>
          </aside>
        )}
      </div>
      {missed.length > 0 && (
        <div className="notice notice-warn" role="status">
          <p>{t('agv.mapNotDrawn', { n: missed.length })}</p>
          <ul>
            {missed.map((p) => (
              <li key={p.amr.robotCode}><button type="button" className="link-button mono" onClick={() => setSelected(p.amr.robotCode)}>{p.amr.robotCode}</button>
                {' — '}{t(`agv.mapWhy_${p.kind}` as MsgKey, { code: p.amr.mapCode ?? '–', map: map.info.mapId })}</li>
            ))}
          </ul>
        </div>
      )}
    </div>
  )
}
