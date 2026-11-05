import { useCallback, useMemo, useState } from 'react'
import { api } from '../api/client'
import { formatDateTime } from '../components/format'
import { Async, Badge, Card } from '../components/ui'
import { useApi } from '../hooks/useApi'

export function InventoryPage() {
  const [selected, setSelected] = useState<string | null>(null)
  const locations = useApi(useCallback(() => api.locations(), []), ['InventoryChanged', 'TaskStateChanged'])
  const racks = useApi(useCallback(() => api.racks(selected ?? undefined), [selected]), ['InventoryChanged', 'TaskStateChanged'])
  const tx = useApi(useCallback(() => api.transactions(selected ?? undefined), [selected]), ['InventoryChanged', 'TaskStateChanged'])
  const codeById = useMemo(() => new Map(locations.data?.map((l) => [l.id, l.code]) ?? []), [locations.data])

  return (
    <>
      <h1>Location / Inventory</h1>
      <p className="muted">
        Inventory is owned by WCS. A destination is only <em>reserved</em> while a task is running; it becomes occupied after the drop is confirmed.
        The rack data model (individual Rack ID vs. quantity) is <strong>NEEDS_CONFIRMATION</strong>.
      </p>
      <Card title="Locations">
        <Async state={locations} isEmpty={(d) => d.length === 0} empty={{ title: 'No locations configured', hint: 'Locations are configuration data (capacities are not part of the specification).' }}>
          {(list) => (
            <table className="selectable">
              <thead><tr><th>Code</th><th>Type</th><th>Accepts</th><th>PLC permission</th><th>Occupied</th><th>Reserved</th><th>Capacity</th><th>Status</th></tr></thead>
              <tbody>
                {list.map((l) => (
                  <tr key={l.id} className={l.code === selected ? 'selected' : ''} onClick={() => setSelected(l.code === selected ? null : l.code)}>
                    <td><strong>{l.code}</strong> {!l.enabled && <span className="muted">(disabled)</span>}</td>
                    <td>{l.type}</td>
                    <td>{l.allowedRackKind}</td>
                    <td>{l.requiresPlcPermission ? 'required' : '—'}</td>
                    <td>{l.occupied}</td>
                    <td>{l.reserved}</td>
                    <td>{l.rackCapacity}</td>
                    <td><Badge value={l.occupancy} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </Async>
      </Card>

      <div className="split">
        <Card title={selected ? `Racks at ${selected}` : 'Racks (all)'}>
          <Async state={racks} isEmpty={(d) => d.length === 0} empty={{ title: 'No racks', hint: selected ? 'No rack is stored here.' : 'No rack has been declared yet.' }}>
            {(list) => (
              <table>
                <thead><tr><th>Rack</th><th>Status</th><th>Location</th><th>Confirmed</th></tr></thead>
                <tbody>
                  {list.map((r) => (
                    <tr key={r.id}>
                      <td className="mono">{r.externalRackId ?? r.id.slice(0, 8)}</td>
                      <td><Badge value={r.status} /></td>
                      <td>{r.currentLocationId ? codeById.get(r.currentLocationId) ?? '?' : <span className="muted">in transit / unknown</span>}</td>
                      <td>{formatDateTime(r.lastConfirmedAt)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </Async>
        </Card>

        <Card title="Inventory transactions">
          <Async state={tx} isEmpty={(d) => d.length === 0} empty={{ title: 'No transactions yet' }}>
            {(list) => (
              <table>
                <thead><tr><th>Time</th><th>Event</th><th>From → To</th><th>Source</th><th>Bind</th></tr></thead>
                <tbody>
                  {list.map((t) => (
                    <tr key={t.id}>
                      <td>{formatDateTime(t.occurredAt)}</td>
                      <td>{t.eventType}</td>
                      <td>{t.fromLocationId ? codeById.get(t.fromLocationId) : '—'} → {t.toLocationId ? codeById.get(t.toLocationId) : '—'}</td>
                      <td>{t.source}{t.userName ? ` (${t.userName})` : ''}</td>
                      <td>{t.bind}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </Async>
        </Card>
      </div>
    </>
  )
}
