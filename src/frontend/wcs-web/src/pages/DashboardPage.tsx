import { useCallback, useMemo } from 'react'
import { api } from '../api/client'
import type { InventoryTransaction, TransportTask } from '../api/types'
import { AgvCard } from '../components/AgvCard'
import { IconAgv, IconClock, IconTasks, IconWarehouse } from '../components/icons'
import { useAuth } from '../auth/AuthContext'
import { Async, EmptyState, Kpi, PageHeader, Panel } from '../components/ui'
import { useApi } from '../hooks/useApi'
import { useClock } from '../hooks/useClock'
import { isActive, useCodeMaps } from '../hooks/useLookups'
import { formatTime } from '../i18n/format'
import { flowInfo, INVENTORY_EVENT_VI, reasonVi, TASK_GROUPS, TASK_STATE_VI, taskCode, type Tone } from '../i18n/vi'

const EVENTS_TASK = ['TaskCreated', 'TaskStateChanged'] as const
const EVENTS_INV = ['InventoryChanged', 'TaskStateChanged'] as const

interface Activity { id: string; at: string; tone: Tone; text: string; detail?: string }

export function DashboardPage() {
  const { user } = useAuth()
  const isAdmin = user?.role === 'Admin'
  const now = useClock(5000).getTime()
  const agvs = useApi(useCallback(() => api.agvs(), []), ['TaskStateChanged', 'AgvStatusChanged'])
  const locations = useApi(useCallback(() => api.locations(), []), [...EVENTS_INV])
  const racks = useApi(useCallback(() => api.racks(), []), [...EVENTS_INV])
  const tasks = useApi(useCallback(() => api.tasks(undefined, 0, 200), []), [...EVENTS_TASK])
  const completed = useApi(useCallback(() => api.tasks('Completed', 0, 1), []), [...EVENTS_TASK])
  const failed = useApi(useCallback(() => api.tasks('Failed', 0, 1), []), [...EVENTS_TASK])
  const tx = useApi(useCallback(() => api.transactions(), []), [...EVENTS_INV])
  const codes = useCodeMaps(locations.data, agvs.data)

  const taskItems = tasks.data?.items
  const agvList = agvs.data
  const locList = locations.data
  const rackList = racks.data

  const kpi = useMemo(() => {
    const online = agvList?.filter((a) => a.connectionStatus === 'Online') ?? []
    return {
      totalAgv: agvList?.length ?? 0,
      working: agvList?.filter((a) => a.busy).length ?? 0,
      waiting: online.filter((a) => !a.busy).length,
      running: taskItems?.filter(isActive).length ?? 0,
      racks: rackList?.length ?? 0,
    }
  }, [agvList, taskItems, rackList])

  const chart = useMemo(() => {
    const counts = TASK_GROUPS.map((g) => ({ ...g, count: taskItems?.filter((t) => g.states.includes(t.state)).length ?? 0 }))
    return { counts, max: Math.max(1, ...counts.map((c) => c.count)) }
  }, [taskItems])

  const stock = useMemo(() => {
    const l = locList ?? []
    const r = rackList ?? []
    return {
      locations: l.length,
      full: r.filter((x) => x.status === 'Full').length,
      empty: r.filter((x) => x.status === 'Empty').length,
      transit: r.filter((x) => x.status === 'InTransit').length,
      freeLocations: l.filter((x) => x.occupancy === 'Empty').length,
      reservedLocations: l.filter((x) => x.reserved > 0).length,
    }
  }, [locList, rackList])

  const activities = useMemo<Activity[]>(() => {
    const list: Activity[] = []
    for (const t of taskItems ?? []) {
      list.push({
        id: `t-${t.id}-${t.updatedAt}`, at: t.updatedAt,
        tone: t.state === 'Failed' ? 'bad' : t.state === 'Completed' || t.state === 'Executing' ? 'ok' : t.state === 'Cancelled' ? 'muted' : 'warn',
        text: `${taskCode(t.id)} · ${flowInfo(t.flow).code} · ${codes.agv(t.agvId)}: ${TASK_STATE_VI[t.state]}`,
        detail: reasonVi(t.stateReason),
      })
    }
    for (const x of (tx.data ?? []) as InventoryTransaction[]) {
      list.push({
        id: `x-${x.id}`, at: x.occurredAt, tone: 'info',
        text: `Tồn kho · ${INVENTORY_EVENT_VI[x.eventType] ?? x.eventType}: ${codes.loc(x.fromLocationId)} → ${codes.loc(x.toLocationId)}`,
        detail: reasonVi(x.note),
      })
    }
    return list.sort((a, b) => b.at.localeCompare(a.at)).slice(0, 14)
  }, [taskItems, tx.data, codes])

  const tasksByAgv = (agvId: string) => {
    const mine = (taskItems ?? []).filter((t: TransportTask) => t.agvId === agvId)
    return { current: mine.find(isActive) ?? null, last: mine[0] ?? null } // list is newest-first
  }

  return (
    <>
      <PageHeader title={isAdmin ? "Tổng quan quản trị" : "Tổng quan vận hành"} />

      <div className="kpi-row">
        <Kpi label="Tổng số AGV" value={agvList ? kpi.totalAgv : '—'} hint="AGV1 · AGV2" icon={<IconAgv />} />
        <Kpi label="AGV đang hoạt động" value={agvList ? kpi.working : '—'} hint="đang có nhiệm vụ" tone="ok" icon={<IconAgv />} />
        <Kpi label="AGV đang chờ" value={agvList ? kpi.waiting : '—'} hint="sẵn sàng nhận việc" tone="warn" icon={<IconClock />} />
        <Kpi label="Nhiệm vụ đang thực hiện" value={taskItems ? kpi.running : '—'} hint="chiếm AGV" tone="info" icon={<IconTasks />} />
        <Kpi label="Nhiệm vụ hoàn thành" value={completed.data?.total ?? '—'} hint={failed.data ? `${failed.data.total} nhiệm vụ lỗi` : undefined} tone="ok" icon={<IconTasks />} />
        <Kpi label="Số rack trong kho" value={rackList ? kpi.racks : '—'} hint={rackList ? `${stock.full} đầy · ${stock.empty} rỗng · ${stock.transit} đang chuyển` : undefined} tone="info" icon={<IconWarehouse />} />
      </div>

      <div className="dash-grid">
        <Panel title="Giám sát AGV" className="p-agv">
          <Async state={agvs} isEmpty={(d) => d.length === 0} empty={{ title: 'Chưa có AGV nào được cấu hình', hint: 'AGV1 và AGV2 được tạo khi khởi động Backend.' }}>
            {(list) => (
              <div className="agv-grid">
                {list.map((a) => {
                  const t = tasksByAgv(a.id)
                  return <AgvCard key={a.id} agv={a} task={t.current} lastTask={t.last} code={codes.loc} now={now} compact />
                })}
              </div>
            )}
          </Async>
        </Panel>

        <Panel title="Biểu đồ nhiệm vụ" subtitle={tasks.data && tasks.data.total > tasks.data.items.length ? `${tasks.data.items.length} nhiệm vụ gần nhất` : 'Tất cả nhiệm vụ'} className="p-chart">
          <Async state={tasks} isEmpty={(d) => d.items.length === 0} empty={{ title: 'Chưa có nhiệm vụ nào', hint: 'Nhiệm vụ xuất hiện khi trạm yêu cầu vận chuyển.' }}>
            {() => (
              <ul className="bars">
                {chart.counts.map((c) => (
                  <li key={c.key}>
                    <span className="bar-label">{c.label}</span>
                    <span className="bar-track"><span className={`bar-fill tone-${c.tone}`} style={{ width: `${(c.count / chart.max) * 100}%` }} /></span>
                    <strong className="num">{c.count}</strong>
                  </li>
                ))}
              </ul>
            )}
          </Async>
        </Panel>

        <Panel title="Tổng quan tồn kho" className="p-stock">
          <Async state={locations} isEmpty={(d) => d.length === 0} empty={{ title: 'Chưa cấu hình vị trí nào', hint: 'Vị trí và sức chứa là dữ liệu cấu hình.' }}>
            {(list) => (
              <>
                <dl className="stat-grid">
                  <div><dt>Số vị trí</dt><dd>{stock.locations}</dd></div>
                  <div><dt>Rack đầy</dt><dd>{stock.full}</dd></div>
                  <div><dt>Rack rỗng</dt><dd>{stock.empty}</dd></div>
                  <div><dt>Vị trí trống</dt><dd>{stock.freeLocations}</dd></div>
                  <div><dt>Vị trí đang giữ chỗ</dt><dd>{stock.reservedLocations}</dd></div>
                  <div><dt>Rack đang vận chuyển</dt><dd>{stock.transit}</dd></div>
                </dl>
                <ul className="fill-list">
                  {list.map((l) => (
                    <li key={l.id}>
                      <span className="mono">{l.code}</span>
                      <span className="fill-track" title={`${l.occupied} rack, ${l.reserved} giữ chỗ / sức chứa ${l.rackCapacity}`}>
                        <span className="fill-occ" style={{ width: `${Math.min(100, (l.occupied / Math.max(1, l.rackCapacity)) * 100)}%` }} />
                        <span className="fill-res" style={{ width: `${Math.min(100, (l.reserved / Math.max(1, l.rackCapacity)) * 100)}%` }} />
                      </span>
                      <span className="num muted">{l.occupied}/{l.rackCapacity}</span>
                    </li>
                  ))}
                </ul>
              </>
            )}
          </Async>
        </Panel>

        <Panel title="Nhật ký hoạt động gần nhất" className="p-activity" flush>
          {tasks.data === null ? <Async state={tasks} empty={{ title: '' }}>{() => null}</Async> : activities.length === 0 ? (
            <EmptyState title="Chưa có hoạt động nào" hint="Nhật ký sẽ hiển thị khi có nhiệm vụ hoặc thay đổi tồn kho." />
          ) : (
            <ul className="activity">
              {activities.map((a) => (
                <li key={a.id}>
                  <span className="act-time num">{formatTime(a.at)}</span>
                  <i className={`act-dot tone-${a.tone}`} aria-hidden="true" />
                  <span className="act-text"><strong>{a.text}</strong>{a.detail && <small>{a.detail}</small>}</span>
                </li>
              ))}
            </ul>
          )}
        </Panel>
      </div>
    </>
  )
}
