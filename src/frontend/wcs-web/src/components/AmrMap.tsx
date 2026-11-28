import { useCallback, useEffect, useMemo, useRef, useState, type PointerEvent as ReactPointerEvent } from 'react'
import type { FleetView } from '../api/fleetTypes'
import type { RcsMapView } from '../api/rcsMapTypes'
import { hasMessage, useT, type MsgKey } from '../i18n'
import { formatDateTime } from '../i18n/format'
import { boundsOf, drawY, isLive, placeAmr, nearestDistances, pointLabel, type Bounds, type Placement } from '../lib/rcsMap'
import { api } from '../api/client'
import { describeError } from '../i18n/vi'
import { MapPointPanel } from './MapPointPanel'
import { callName, kindOf, rowsByPoint } from '../lib/mapData'
import { Button, Pill } from './ui'

interface Box { x: number; y: number; w: number; h: number }
const fitBox = (b: Bounds): Box => {
  const w = Math.max(b.maxX - b.minX, 1); const h = Math.max(b.maxY - b.minY, 1); const pad = Math.max(w, h) * 0.03
  return { x: b.minX - pad, y: b.minY - pad, w: w + pad * 2, h: h + pad * 2 }
}

/** CR-065: the connections and points of the imported map. Drawn once per map and zoom level. */
function Roads({ map, bounds, upp, showNames, selected, onSelect }: { map: RcsMapView; bounds: Bounds; upp: number; showNames: boolean; selected: number | null; onSelect: (id: number) => void }) {
  const notes = useMemo(() => new Map((map.points ?? []).filter((n) => n.current).map((n) => [n.pointId, n])), [map])
  const data = useMemo(() => rowsByPoint(map.geometry.mapData), [map])   // CR-067: identification of the Excel file
  const byId = useMemo(() => new Map(map.geometry.points.map((p) => [p.id, p])), [map])
  const path = useMemo(() => map.geometry.edges.map((e) => {
    const a = byId.get(e.from); const z = byId.get(e.to)
    if (!a || !z) return ''
    return [[a.x, a.y], ...e.ctrl, [z.x, z.y]].map((c, i) => `${i === 0 ? 'M' : 'L'}${c[0]} ${drawY(map, bounds, c[1])}`).join(' ')
  }).join(' '), [map, bounds, byId])
  const r = Math.min(Math.max(0.09, 2 * upp), 6 * upp)
  const near = useMemo(() => nearestDistances(map.geometry.points), [map])
  // a named point is drawn above its neighbours; each click area ends half way to the nearest point
  const ordered = useMemo(() => {
    const rank = (id: number) => (notes.has(id) ? 2 : callName(data.get(id)) ? 1 : 0)
    return [...map.geometry.points].sort((a, b) => rank(a.id) - rank(b.id))
  }, [map, notes, data])
  // what is written under a point: the call code of the RCS and, when WCS adds something different, the WCS link or name
  const labels = useMemo(() => map.geometry.points.flatMap((p) => {
    const n = notes.get(p.id); const call = callName(data.get(p.id))
    return n || call ? [{ p, n, call }] : []
  }), [map, notes, data])
  return (
    <>
      <path d={path} className="amr-map-road" />
      {ordered.map((p) => {
        const n = notes.get(p.id); const y = drawY(map, bounds, p.y)
        const half = (near.get(p.id) ?? Infinity) / 2
        const row = data.get(p.id); const kind = kindOf(row?.dataType); const call = callName(row); const big = !!n || !!call
        const text = [call, row?.dataType, n ? pointLabel(n, true) : null].filter(Boolean).join(' · ')
        return (
          <g key={p.id} className={`amr-map-hit${n ? ' named' : ''}${kind ? ` kind-${kind}` : ''}${p.id === selected ? ' selected' : ''}`} data-point={p.id} role="button" tabIndex={0} aria-label={`#${p.id}${text ? ` ${text}` : ''}`}
            onClick={() => onSelect(p.id)} onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); onSelect(p.id) } }}>
            <circle cx={p.x} cy={y} r={Math.min(Math.max(r, 9 * upp), half * 0.95)} className="amr-map-target" />
            {p.id === selected && <circle cx={p.x} cy={y} r={Math.max(r * 2, 11 * upp)} className="amr-map-ring" />}
            <circle cx={p.x} cy={y} r={big ? Math.max(r * 1.5, 5 * upp) : Math.min(r, half * 0.95)} className="amr-map-point" />
            <title>{`#${p.id} (${p.x}, ${p.y})${text ? ` · ${text}` : ''}`}</title>
          </g>
        )
      })}
      {showNames && labels.map(({ p, n, call }) => {
        const wcs = n ? pointLabel(n, upp < 0.02) : ''
        const label = !call ? wcs : wcs && wcs !== call ? `${call} · ${wcs}` : call
        // zoomed out, the rotating points (the "_1" places beside a working point) are left out so that the main codes stay readable
        if (!n && upp >= 0.02 && kindOf(data.get(p.id)?.dataType) === 'rotating') return null
        return <text key={`n${p.id}`} x={p.x} y={drawY(map, bounds, p.y) + 18 * upp} fontSize={11 * upp} className="amr-map-name" data-label={p.id}>{label}</text>
      })}
      {upp < 0.012 && map.geometry.points.map((p) => (
        <text key={`t${p.id}`} x={p.x + r * 1.4} y={drawY(map, bounds, p.y) - r * 1.4} fontSize={11 * upp} className="amr-map-label">{p.id}</text>
      ))}
    </>
  )
}

/** CR-065: read-only 2D view of the AMRs on the imported map: zoom, pan, fit; one click shows what the RCS reports about an AMR. */
export function AmrMap({ map, fleet, loadFailed, canEdit = false, onChanged = () => {} }: { map: RcsMapView; fleet: FleetView; loadFailed: boolean; canEdit?: boolean; onChanged?: () => void }) {
  const t = useT()
  const bounds = useMemo(() => boundsOf(map), [map])
  const [view, setView] = useState<Box>(() => fitBox(bounds))
  const [fitted, setFitted] = useState(map.info.sha256 + map.info.importedAt + map.info.yAxisUp)
  const identity = map.info.sha256 + map.info.importedAt + map.info.yAxisUp
  if (fitted !== identity) { setFitted(identity); setView(fitBox(bounds)) }   // a new map is shown whole; saving a point keeps the zoom
  const [point, setPoint] = useState<number | null>(null)   // CR-066
  const [showNames, setShowNames] = useState(true)
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
    if ((e.target as Element).closest('.amr-map-robot, .amr-map-hit')) return
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
  const chosen = map.geometry.points.find((p) => p.id === point) ?? null
  const dataRows = useMemo(() => rowsByPoint(map.geometry.mapData), [map])
  const kinds = useMemo(() => {
    const m = new Map<string, number>()
    for (const r of dataRows.values()) if (r.dataType) m.set(r.dataType, (m.get(r.dataType) ?? 0) + 1)
    return [...m].map(([type, n]) => ({ type, n, kind: kindOf(type) ?? 'other' })).sort((a, b) => a.type.localeCompare(b.type))
  }, [dataRows])
  const stale = (map.points ?? []).filter((n) => !n.current)
  const pickPoint = (id: number) => { setPoint(id); setSelected(null) }
  const pickAmr = (code: string) => { setSelected(code); setPoint(null) }
  const [removeError, setRemoveError] = useState<string | null>(null)
  const removeStale = async (id: number) => {
    setRemoveError(null)
    try { await api.rcsMapPointRemove(id); onChanged() } catch (e) { setRemoveError(describeError(e)) }
  }
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
        <span><i className="amr-map-dot named" />{t('agv.pointNamed')}</span>
        {kinds.map((k) => <span key={k.type} data-kind={k.kind}><i className={`amr-map-dot kind-${k.kind}`} />{k.type} ({k.n})</span>)}
        <label className="inline-check"><input type="checkbox" checked={showNames} onChange={(e) => setShowNames(e.target.checked)} /> {t('agv.pointShowNames')}</label>
        <span className="amr-map-tools">
          <Button size="sm" onClick={() => zoom(1 / 1.4)} title={t('agv.mapZoomIn')}>+</Button>
          <Button size="sm" onClick={() => zoom(1.4)} title={t('agv.mapZoomOut')}>−</Button>
          <Button size="sm" onClick={() => setView(fitBox(bounds))}>{t('agv.mapFit')}</Button>
        </span>
      </div>
      <div className="amr-map-body">
        <svg ref={svg} className="amr-map-svg" viewBox={`${view.x} ${view.y} ${view.w} ${view.h}`} role="img" aria-label={t('agv.mapTitle', { name: map.info.mapName })}
          onPointerDown={down} onPointerMove={move} onPointerUp={up} onPointerCancel={up}>
          <Roads map={map} bounds={bounds} upp={upp} showNames={showNames} selected={point} onSelect={pickPoint} />
          {drawn.map((p) => (
            <g key={p.amr.robotCode} className={`amr-map-robot ${isLive(p.amr, fleet, loadFailed) ? 'live' : 'lost'}${p.amr.robotCode === selected ? ' selected' : ''}`}
              transform={`translate(${p.x} ${p.y}) scale(${upp})`} role="button" tabIndex={0} aria-label={p.amr.robotCode} data-amr={p.amr.robotCode}
              onClick={() => pickAmr(p.amr.robotCode)} onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); pickAmr(p.amr.robotCode) } }}>
              <circle r={13} />
              {p.rotate !== null && <path d="M10 0 L-6 7 L-2 0 L-6 -7 Z" className="amr-map-arrow" transform={`rotate(${p.rotate})`} />}
              <text y={-18} fontSize={12}>{p.amr.robotCode}</text>
              <title>{p.amr.robotCode}</title>
            </g>
          ))}
        </svg>
        {chosen && <MapPointPanel key={`${chosen.id}-${map.info.sha256}-${map.info.importedAt}`} point={chosen} note={(map.points ?? []).find((n) => n.pointId === chosen.id && n.current) ?? null} notes={map.points ?? []}
          mapSha256={map.info.sha256} row={dataRows.get(chosen.id) ?? null} dataRows={map.geometry.mapData?.rows ?? []} canEdit={canEdit} onSaved={onChanged} onClose={() => setPoint(null)} />}
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
      {stale.length > 0 && (
        <div className="notice notice-warn" role="status">
          <p>{t('agv.pointStale', { n: stale.length })}</p>
          {removeError && <p role="alert">{removeError}</p>}
          <ul>
            {stale.map((n) => (
              <li key={n.pointId}>#{n.pointId} ({n.x}, {n.y}): {pointLabel(n, true)}{' '}
                {canEdit && <Button size="sm" onClick={() => void removeStale(n.pointId)}>{t('agv.pointRemove')}</Button>}</li>
            ))}
          </ul>
        </div>
      )}
      {missed.length > 0 && (
        <div className="notice notice-warn" role="status">
          <p>{t('agv.mapNotDrawn', { n: missed.length })}</p>
          <ul>
            {missed.map((p) => (
              <li key={p.amr.robotCode}><button type="button" className="link-button mono" onClick={() => pickAmr(p.amr.robotCode)}>{p.amr.robotCode}</button>
                {' — '}{t(`agv.mapWhy_${p.kind}` as MsgKey, { code: p.amr.mapCode ?? '–', map: map.info.mapId })}</li>
            ))}
          </ul>
        </div>
      )}
    </div>
  )
}
