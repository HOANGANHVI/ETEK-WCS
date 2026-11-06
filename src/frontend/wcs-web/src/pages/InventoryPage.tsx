import { useCallback, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import type { Location, Rack } from '../api/types'
import { Async, EmptyState, OccupancyPill, PageHeader, Panel, RackPill } from '../components/ui'
import { useApi } from '../hooks/useApi'
import { useCodeMaps } from '../hooks/useLookups'
import { formatDateTime } from '../i18n/format'
import {
  ALLOWED_KIND_VI, BIND_VI, INVENTORY_EVENT_VI, LOCATION_TYPE_VI, OCCUPANCY_TONE, RACK_STATUS_VI, reasonVi, SOURCE_VI, taskCode,
} from '../i18n/vi'

const EVENTS = ['InventoryChanged', 'TaskStateChanged'] as const

/** Groups the schematic by what the data says (type / accepted rack kind), not by hard-coded location codes. */
function groupOf(l: Location): { key: string; title: string; order: number } {
  if (l.type === 'Station') {
    const prefix = l.code.replace(/\d+$/, '')
    return { key: `st-${prefix}`, title: `Trạm máy ${prefix}`, order: 10 + prefix.charCodeAt(0) / 1000 }
  }
  if (l.type === 'ManualBuffer') return { key: 'manual', title: 'Vị trí đệm thủ công', order: 40 }
  if (l.allowedRackKind === 'Full') return { key: 'buf-full', title: 'Vị trí đệm rack đầy', order: 20 }
  if (l.allowedRackKind === 'Empty') return { key: 'buf-empty', title: 'Vị trí đệm rack rỗng', order: 30 }
  return { key: 'other', title: 'Vị trí khác', order: 50 }
}

function SlotStrip({ loc, racks }: { loc: Location; racks: Rack[] }) {
  const cells: { cls: string; title: string; text: string }[] = []
  for (const r of racks.slice(0, loc.rackCapacity)) {
    const text = r.status === 'Full' ? 'Đ' : r.status === 'Empty' ? 'R' : r.status === 'InTransit' ? '↔' : '?'
    cells.push({ cls: `slot slot-${r.status.toLowerCase()}`, title: `${r.externalRackId ?? 'Rack chưa có ID'} – ${RACK_STATUS_VI[r.status]}`, text })
  }
  for (let i = 0; i < loc.reserved && cells.length < loc.rackCapacity; i++) cells.push({ cls: 'slot slot-reserved', title: 'Chỗ đang được giữ cho nhiệm vụ', text: '⏳' })
  while (cells.length < loc.rackCapacity) cells.push({ cls: 'slot slot-free', title: 'Chỗ trống', text: '' })
  return <div className="slots">{cells.map((c, i) => <span key={i} className={c.cls} title={c.title}>{c.text}</span>)}</div>
}

export function InventoryPage() {
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
      const g = groupOf(l)
      const entry = m.get(g.key) ?? { title: g.title, order: g.order, items: [] }
      entry.items.push(l)
      m.set(g.key, entry)
    }
    return [...m.values()].sort((a, b) => a.order - b.order).map((g) => ({ ...g, items: g.items.sort((a, b) => a.code.localeCompare(b.code)) }))
  }, [locations.data])

  const current = (locations.data ?? []).find((l) => l.code === selected) ?? null
  const tableRacks = useMemo(
    () => (racks.data ?? []).filter((r) => !current || r.currentLocationId === current.id),
    [racks.data, current],
  )

  return (
    <>
      <PageHeader
        title="Vị trí và tồn kho"
        description="Tồn kho rack do WCS quản lý. Vị trí đích chỉ được giữ chỗ khi nhiệm vụ đang chạy và chỉ có rack sau khi xác nhận đã thả."
      />

      <div className="inv-top">
        <Panel title="Sơ đồ vị trí" subtitle="Bấm vào một vị trí để xem chi tiết" className="inv-map">
          <div className="legend" aria-label="Chú giải">
            <span><i className="slot slot-full">Đ</i>Rack đầy</span>
            <span><i className="slot slot-empty">R</i>Rack rỗng</span>
            <span><i className="slot slot-reserved">⏳</i>Đang được giữ chỗ</span>
            <span><i className="slot slot-free" />Vị trí trống</span>
            <span><i className="slot slot-unknown">?</i>Chưa xác định</span>
          </div>
          <Async state={locations} isEmpty={(d) => d.length === 0} empty={{ title: 'Chưa cấu hình vị trí nào', hint: 'Vị trí và sức chứa là dữ liệu cấu hình, chưa có trong đặc tả.' }}>
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
                          <span className="tile-foot">{l.occupied}/{l.rackCapacity} rack{l.reserved > 0 ? ` · ${l.reserved} giữ chỗ` : ''}</span>
                        </button>
                      ))}
                    </div>
                  </section>
                ))}
              </div>
            )}
          </Async>
        </Panel>

        <Panel title={current ? `Vị trí ${current.code}` : 'Chi tiết vị trí'} className="inv-detail">
          {current ? (
            <>
              <dl className="kv2">
                <dt>Tên</dt><dd>{current.name}</dd>
                <dt>Loại vị trí</dt><dd>{LOCATION_TYPE_VI[current.type] ?? current.type}</dd>
                <dt>Chấp nhận</dt><dd>{ALLOWED_KIND_VI[current.allowedRackKind] ?? current.allowedRackKind}</dd>
                <dt>Sức chứa</dt><dd className="num">{current.rackCapacity} rack</dd>
                <dt>Đang có rack</dt><dd className="num">{current.occupied}</dd>
                <dt>Đang giữ chỗ</dt><dd className="num">{current.reserved}</dd>
                <dt>Trạng thái</dt><dd><OccupancyPill value={current.occupancy} /></dd>
                <dt>PLC cho phép</dt><dd>{current.requiresPlcPermission ? 'Cần PLC cho phép trước khi lấy/thả' : 'Không yêu cầu'}</dd>
                <dt>Sử dụng</dt><dd>{current.enabled ? 'Đang bật' : 'Đã tắt'}</dd>
              </dl>
              {current.occupancy === 'Unknown' && <div className="notice notice-bad">Trạng thái vật lý chưa xác định. Cần đối soát trước khi dùng vị trí này cho nhiệm vụ khác.</div>}
              <h3 className="mini-title">Rack tại vị trí</h3>
              {(racksByLoc.get(current.id) ?? []).length === 0 ? <p className="muted">Không có rack nào.</p> : (
                <ul className="event-list">
                  {(racksByLoc.get(current.id) ?? []).map((r) => (
                    <li key={r.id}><span className="mono">{r.externalRackId ?? r.id.slice(0, 8)}</span><RackPill value={r.status} /></li>
                  ))}
                </ul>
              )}
            </>
          ) : (
            <EmptyState title="Chưa chọn vị trí" hint="Chọn một ô trên sơ đồ để xem sức chứa, quy tắc và rack tại vị trí đó." />
          )}
        </Panel>
      </div>

      <Panel title="Bảng tồn kho" subtitle={current ? `Đang lọc theo vị trí ${current.code}` : 'Tất cả rack'} flush
        actions={current ? <button type="button" className="btn btn-sm btn-secondary" onClick={() => setSelected(null)}>Bỏ lọc vị trí</button> : undefined}>
        <Async state={racks} isEmpty={() => tableRacks.length === 0} empty={{ title: 'Không có rack nào', hint: selected ? 'Vị trí này đang trống.' : 'Chưa có rack nào được khai báo.' }}>
          {() => (
            <div className="table-wrap capped">
              <table className="grid">
                <thead><tr><th>Rack ID</th><th>Loại rack</th><th>Vị trí hiện tại</th><th>Trạng thái</th><th>Nhiệm vụ liên quan</th><th>Thời điểm cập nhật</th></tr></thead>
                <tbody>
                  {tableRacks.map((r) => (
                    <tr key={r.id}>
                      <td>{r.externalRackId ? <strong className="mono">{r.externalRackId}</strong> : <span className="muted">Chưa gán Rack ID <span className="mono">({r.id.slice(0, 6)})</span></span>}</td>
                      <td>{r.rackType ?? <span className="muted">Chưa phân loại</span>}</td>
                      <td>{r.currentLocationId ? codes.loc(r.currentLocationId) : <span className="muted">Đang vận chuyển / chưa xác định</span>}</td>
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

      <Panel title="Lịch sử giao dịch tồn kho" subtitle={current ? `Vị trí ${current.code}` : '50 giao dịch gần nhất'} flush className="mt">
        <Async state={tx} isEmpty={(d) => d.length === 0} empty={{ title: 'Chưa có giao dịch nào' }}>
          {(list) => (
            <div className="table-wrap capped">
              <table className="grid">
                <thead><tr><th>Thời gian</th><th>Sự kiện</th><th>Từ → Đến</th><th>Trạng thái rack</th><th>Nguồn xác nhận</th><th>Bind rack</th><th>Ghi chú</th></tr></thead>
                <tbody>
                  {list.map((t) => (
                    <tr key={t.id}>
                      <td className="nowrap">{formatDateTime(t.occurredAt)}</td>
                      <td>{INVENTORY_EVENT_VI[t.eventType] ?? t.eventType}</td>
                      <td className="nowrap">{codes.loc(t.fromLocationId)} → {codes.loc(t.toLocationId)}</td>
                      <td>{t.statusBefore ? RACK_STATUS_VI[t.statusBefore] : '—'} → {t.statusAfter ? RACK_STATUS_VI[t.statusAfter] : '—'}</td>
                      <td>{SOURCE_VI[t.source] ?? t.source}{t.userName ? ` (${t.userName})` : ''}</td>
                      <td>{BIND_VI[t.bind] ?? t.bind}</td>
                      <td className="muted">{reasonVi(t.note)}</td>
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
