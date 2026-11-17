import { useCallback, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import type { Location, Rack } from '../api/types'
import { Async, EmptyState, OccupancyPill, PageHeader, Panel, RackPill } from '../components/ui'
import { useApi } from '../hooks/useApi'
import { useCodeMaps } from '../hooks/useLookups'
import { t, useT } from '../i18n'
import { formatDateTime } from '../i18n/format'
import {
  ALLOWED_KIND_VI, BIND_VI, INVENTORY_EVENT_VI, LOCATION_TYPE_VI, OCCUPANCY_TONE, RACK_STATUS_VI, reasonVi, SOURCE_VI, taskCode,
} from '../i18n/vi'

const EVENTS = ['InventoryChanged', 'TaskStateChanged'] as const

/** Groups the schematic by what the data says (type / accepted rack kind), not by hard-coded location codes. */
function groupOf(l: Location, t: ReturnType<typeof useT>): { key: string; title: string; order: number } {
  if (l.type === 'Station') {
    const prefix = l.code.replace(/\d+$/, '')
    return { key: `st-${prefix}`, title: t('inventory.stationGroup', { prefix }), order: 10 + prefix.charCodeAt(0) / 1000 }
  }
  if (l.type === 'ManualBuffer') return { key: 'manual', title: t('inventory.manualBuffer'), order: 40 }
  if (l.allowedRackKind === 'Full') return { key: 'buf-full', title: t('inventory.fullBuffer'), order: 20 }
  if (l.allowedRackKind === 'Empty') return { key: 'buf-empty', title: t('inventory.emptyBuffer'), order: 30 }
  return { key: 'other', title: t('inventory.otherLocations'), order: 50 }
}

function SlotStrip({ loc, racks }: { loc: Location; racks: Rack[] }) {
  const cells: { cls: string; title: string; text: string }[] = []
  for (const r of racks.slice(0, loc.rackCapacity)) {
    const text = r.status === 'Full' ? t('inventory.slotFullLetter') : r.status === 'Empty' ? t('inventory.slotEmptyLetter') : r.status === 'InTransit' ? '↔' : '?'
    cells.push({ cls: `slot slot-${r.status.toLowerCase()}`, title: `${r.externalRackId ?? t('inventory.rackNoId')} – ${RACK_STATUS_VI[r.status]}`, text })
  }
  for (let i = 0; i < loc.reserved && cells.length < loc.rackCapacity; i++) cells.push({ cls: 'slot slot-reserved', title: t('inventory.slotReservedTitle'), text: '⏳' })
  while (cells.length < loc.rackCapacity) cells.push({ cls: 'slot slot-free', title: t('inventory.slotFreeTitle'), text: '' })
  return <div className="slots">{cells.map((c, i) => <span key={i} className={c.cls} title={c.title}>{c.text}</span>)}</div>
}

export function InventoryPage() {
  const t = useT()
  const [selected, setSelected] = useState<string | null>(null)
  const locations = useApi(useCallback(() => api.locations(), []), [...EVENTS])
  const racks = useApi(useCallback(() => api.racks(), []), [...EVENTS])
  const tx = useApi(useCallback(() => api.transactions(selected ?? undefined), [selected]), [...EVENTS])
  const codes = useCodeMaps(locations.data, null)

  const racksByLoc = useMemo(() => {
    const m = new Map<string, Rack[]>()
    for (const r of racks.data ?? []) if (r.currentLocationId) m.set(r.currentLocationId, [...(m.get(r.currentLocationId) ?? []), r])
    return m
  }, [racks.data])

  const groups = useMemo(() => {
    const m = new Map<string, { title: string; order: number; items: Location[] }>()
    for (const l of locations.data ?? []) {
      const g = groupOf(l, t)
      const entry = m.get(g.key) ?? { title: g.title, order: g.order, items: [] }
      entry.items.push(l)
      m.set(g.key, entry)
    }
    return [...m.values()].sort((a, b) => a.order - b.order).map((g) => ({ ...g, items: g.items.sort((a, b) => a.code.localeCompare(b.code)) }))
  }, [locations.data, t])

  const current = (locations.data ?? []).find((l) => l.code === selected) ?? null
  const tableRacks = useMemo(
    () => (racks.data ?? []).filter((r) => !current || r.currentLocationId === current.id),
    [racks.data, current],
  )

  return (
    <>
      <PageHeader title={t('inventory.title')} />

      <div className="inv-top">
        <Panel title={t('inventory.mapPanel')} className="inv-map">
          <div className="legend" aria-label={t('inventory.legend')}>
            <span><i className="slot slot-full">{t('inventory.slotFullLetter')}</i>{t('inventory.legendFull')}</span>
            <span><i className="slot slot-empty">{t('inventory.slotEmptyLetter')}</i>{t('inventory.legendEmpty')}</span>
            <span><i className="slot slot-reserved">⏳</i>{t('inventory.legendReserved')}</span>
            <span><i className="slot slot-free" />{t('inventory.legendFree')}</span>
            <span><i className="slot slot-unknown">?</i>{t('inventory.legendUnknown')}</span>
          </div>
          <Async state={locations} isEmpty={(d) => d.length === 0} empty={{ title: t('inventory.noLocations'), hint: t('inventory.noLocationsHint') }}>
            {() => (
              <div className="map-groups">
                {groups.map((g) => (
                  <section key={g.title} className="map-group">
                    <h3>{g.title}</h3>
                    <div className="map-tiles">
                      {g.items.map((l) => (
                        <button type="button" key={l.id} className={`loc-tile tone-${OCCUPANCY_TONE[l.occupancy]} ${l.code === selected ? 'selected' : ''} ${l.enabled ? '' : 'disabled'}`}
                          aria-pressed={l.code === selected} onClick={() => setSelected(l.code === selected ? null : l.code)}>
                          <span className="tile-top"><strong>{l.code}</strong><OccupancyPill value={l.occupancy} /></span>
                          <SlotStrip loc={l} racks={racksByLoc.get(l.id) ?? []} />
                          <span className="tile-foot">{t('inventory.tileFoot', { occupied: l.occupied, capacity: l.rackCapacity })}{l.reserved > 0 ? t('inventory.tileReserved', { n: l.reserved }) : ''}</span>
                        </button>
                      ))}
                    </div>
                  </section>
                ))}
              </div>
            )}
          </Async>
        </Panel>

        <Panel title={current ? t('inventory.locationTitle', { code: current.code }) : t('inventory.locationDetail')} className="inv-detail">
          {current ? (
            <>
              <dl className="kv2">
                <dt>{t('inventory.rowName')}</dt><dd>{current.name}</dd>
                <dt>{t('inventory.rowType')}</dt><dd>{LOCATION_TYPE_VI[current.type] ?? current.type}</dd>
                <dt>{t('inventory.rowAccepts')}</dt><dd>{ALLOWED_KIND_VI[current.allowedRackKind] ?? current.allowedRackKind}</dd>
                <dt>{t('inventory.rowCapacity')}</dt><dd className="num">{t('inventory.rackUnit', { n: current.rackCapacity })}</dd>
                <dt>{t('inventory.rowOccupied')}</dt><dd className="num">{current.occupied}</dd>
                <dt>{t('inventory.rowReserved')}</dt><dd className="num">{current.reserved}</dd>
                <dt>{t('inventory.rowStatus')}</dt><dd><OccupancyPill value={current.occupancy} /></dd>
                <dt>{t('inventory.rowPlc')}</dt><dd>{current.requiresPlcPermission ? t('inventory.plcRequired') : t('inventory.plcNotRequired')}</dd>
                <dt>{t('inventory.rowEnabled')}</dt><dd>{current.enabled ? t('inventory.enabledOn') : t('inventory.enabledOff')}</dd>
              </dl>
              {current.occupancy === 'Unknown' && <div className="notice notice-bad">{t('inventory.unknownNotice')}</div>}
              <h3 className="mini-title">{t('inventory.racksHere')}</h3>
              {(racksByLoc.get(current.id) ?? []).length === 0 ? <p className="muted">{t('inventory.noRacksHere')}</p> : (
                <ul className="event-list">
                  {(racksByLoc.get(current.id) ?? []).map((r) => (
                    <li key={r.id}><span className="mono">{r.externalRackId ?? r.id.slice(0, 8)}</span><RackPill value={r.status} /></li>
                  ))}
                </ul>
              )}
            </>
          ) : (
            <EmptyState title={t('inventory.noneSelected')} hint={t('inventory.noneSelectedHint')} />
          )}
        </Panel>
      </div>

      <Panel title={t('inventory.stockTable')} subtitle={current ? t('inventory.filteredBy', { code: current.code }) : t('inventory.allRacks')} flush
        actions={current ? <button type="button" className="btn btn-sm btn-secondary" onClick={() => setSelected(null)}>{t('inventory.clearFilter')}</button> : undefined}>
        <Async state={racks} isEmpty={() => tableRacks.length === 0} empty={{ title: t('inventory.noRacks'), hint: selected ? t('inventory.noRacksHintSelected') : t('inventory.noRacksHintAll') }}>
          {() => (
            <div className="table-wrap capped">
              <table className="grid">
                <thead><tr><th>{t('inventory.colRackId')}</th><th>{t('inventory.colRackKind')}</th><th>{t('inventory.colCurrentLocation')}</th><th>{t('inventory.colStatus')}</th><th>{t('inventory.colRelatedTask')}</th><th>{t('inventory.colConfirmedAt')}</th></tr></thead>
                <tbody>
                  {tableRacks.map((r) => (
                    <tr key={r.id}>
                      <td>{r.externalRackId ? <strong className="mono">{r.externalRackId}</strong> : <span className="muted">{t('inventory.rackNoIdAssigned')} <span className="mono">({r.id.slice(0, 6)})</span></span>}</td>
                      <td>{r.rackType ?? <span className="muted">{t('inventory.unclassified')}</span>}</td>
                      <td>{r.currentLocationId ? codes.loc(r.currentLocationId) : <span className="muted">{t('inventory.inTransitOrUnknown')}</span>}</td>
                      <td><RackPill value={r.status} /></td>
                      <td>{r.currentTaskId ? <Link to={`/tasks?id=${r.currentTaskId}`} className="mono">{taskCode(r.currentTaskId)}</Link> : '—'}</td>
                      <td className="nowrap">{formatDateTime(r.lastConfirmedAt)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Async>
      </Panel>

      <Panel title={t('inventory.txPanel')} subtitle={current ? t('inventory.locationTitle', { code: current.code }) : t('inventory.txLatest')} flush className="mt">
        <Async state={tx} isEmpty={(d) => d.length === 0} empty={{ title: t('inventory.noTx') }}>
          {(list) => (
            <div className="table-wrap capped">
              <table className="grid">
                <thead><tr><th>{t('inventory.colTime')}</th><th>{t('inventory.colEvent')}</th><th>{t('inventory.colFromTo')}</th><th>{t('inventory.colRackStatus')}</th><th>{t('inventory.colSource')}</th><th>{t('inventory.colBind')}</th><th>{t('inventory.colNote')}</th></tr></thead>
                <tbody>
                  {list.map((x) => (
                    <tr key={x.id}>
                      <td className="nowrap">{formatDateTime(x.occurredAt)}</td>
                      <td>{INVENTORY_EVENT_VI[x.eventType] ?? x.eventType}</td>
                      <td className="nowrap">{codes.loc(x.fromLocationId)} → {codes.loc(x.toLocationId)}</td>
                      <td>{x.statusBefore ? RACK_STATUS_VI[x.statusBefore] : '—'} → {x.statusAfter ? RACK_STATUS_VI[x.statusAfter] : '—'}</td>
                      <td>{SOURCE_VI[x.source] ?? x.source}{x.userName ? ` (${x.userName})` : ''}</td>
                      <td>{BIND_VI[x.bind] ?? x.bind}</td>
                      <td className="muted">{reasonVi(x.note)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Async>
      </Panel>
    </>
  )
}
