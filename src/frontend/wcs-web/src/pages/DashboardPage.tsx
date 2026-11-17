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
import { useT } from '../i18n'
import { describeHistory } from '../i18n/logCatalog'
import { flowInfo, INVENTORY_EVENT_VI, reasonVi, TASK_GROUPS, TASK_STATE_VI, taskCode, type Tone } from '../i18n/vi'

const EVENTS_TASK = ['TaskCreated', 'TaskStateChanged'] as const
const EVENTS_INV = ['InventoryChanged', 'TaskStateChanged'] as const

interface Activity { id: string; at: string; tone: Tone; text: string; detail?: string }

export function DashboardPage() {
  const t = useT()
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
    const counts = TASK_GROUPS.map((g) => ({ ...g, count: taskItems?.filter((tk) => g.states.includes(tk.state)).length ?? 0, label: t(g.labelKey) }))
    return { counts, max: Math.max(1, ...counts.map((c) => c.count)) }
  }, [taskItems, t])

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
    for (const tk of taskItems ?? []) {
      list.push({
        id: `t-${tk.id}-${tk.updatedAt}`, at: tk.updatedAt,
        tone: tk.state === 'Failed' ? 'bad' : tk.state === 'Completed' || tk.state === 'Executing' ? 'ok' : tk.state === 'Cancelled' ? 'muted' : 'warn',
        text: `${taskCode(tk.id)} · ${flowInfo(tk.flow).code} · ${codes.agv(tk.agvId)}: ${TASK_STATE_VI[tk.state]}`,
        detail: describeHistory(tk.stateReasonCode, tk.stateReasonArgs, tk.stateReason),
      })
    }
    for (const x of (tx.data ?? []) as InventoryTransaction[]) {
      list.push({
        id: `x-${x.id}`, at: x.occurredAt, tone: 'info',
        text: t('dashboard.activityInventory', { event: INVENTORY_EVENT_VI[x.eventType] ?? x.eventType, from: codes.loc(x.fromLocationId), to: codes.loc(x.toLocationId) }),
        detail: reasonVi(x.note),
      })
    }
    return list.sort((a, b) => b.at.localeCompare(a.at)).slice(0, 14)
  }, [taskItems, tx.data, codes, t])

  const tasksByAgv = (agvId: string) => {
    const mine = (taskItems ?? []).filter((tk: TransportTask) => tk.agvId === agvId)
    return { current: mine.find(isActive) ?? null, last: mine[0] ?? null } // list is newest-first
  }

  return (
    <>
      <PageHeader title={isAdmin ? t('dashboard.titleAdmin') : t('dashboard.titleOperator')} />

      <div className="kpi-row">
        <Kpi label={t('dashboard.kpiTotalAgv')} value={agvList ? kpi.totalAgv : '—'} hint="AGV1 · AGV2" icon={<IconAgv />} />
        <Kpi label={t('dashboard.kpiWorking')} value={agvList ? kpi.working : '—'} hint={t('dashboard.kpiWorkingHint')} tone="ok" icon={<IconAgv />} />
        <Kpi label={t('dashboard.kpiWaiting')} value={agvList ? kpi.waiting : '—'} hint={t('dashboard.kpiWaitingHint')} tone="warn" icon={<IconClock />} />
        <Kpi label={t('dashboard.kpiRunning')} value={taskItems ? kpi.running : '—'} hint={t('dashboard.kpiRunningHint')} tone="info" icon={<IconTasks />} />
        <Kpi label={t('dashboard.kpiCompleted')} value={completed.data?.total ?? '—'} hint={failed.data ? t('dashboard.kpiFailedHint', { n: failed.data.total }) : undefined} tone="ok" icon={<IconTasks />} />
        <Kpi label={t('dashboard.kpiRacks')} value={rackList ? kpi.racks : '—'} hint={rackList ? t('dashboard.kpiRacksHint', { full: stock.full, empty: stock.empty, transit: stock.transit }) : undefined} tone="info" icon={<IconWarehouse />} />
      </div>

      <div className="dash-grid">
        <Panel title={t('dashboard.panelAgv')} className="p-agv">
          <Async state={agvs} isEmpty={(d) => d.length === 0} empty={{ title: t('agv.emptyTitle'), hint: t('agv.emptyHint') }}>
            {(list) => (
              <div className="agv-grid">
                {list.map((a) => {
                  const cur = tasksByAgv(a.id)
                  return <AgvCard key={a.id} agv={a} task={cur.current} lastTask={cur.last} code={codes.loc} now={now} compact />
                })}
              </div>
            )}
          </Async>
        </Panel>

        <Panel title={t('dashboard.panelChart')} subtitle={tasks.data && tasks.data.total > tasks.data.items.length ? t('dashboard.chartRecent', { n: tasks.data.items.length }) : t('dashboard.chartAll')} className="p-chart">
          <Async state={tasks} isEmpty={(d) => d.items.length === 0} empty={{ title: t('dashboard.noTasks'), hint: t('dashboard.noTasksHint') }}>
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

        <Panel title={t('dashboard.panelStock')} className="p-stock">
          <Async state={locations} isEmpty={(d) => d.length === 0} empty={{ title: t('dashboard.noLocations'), hint: t('dashboard.noLocationsHint') }}>
            {(list) => (
              <>
                <dl className="stat-grid">
                  <div><dt>{t('dashboard.statLocations')}</dt><dd>{stock.locations}</dd></div>
                  <div><dt>{t('dashboard.statFull')}</dt><dd>{stock.full}</dd></div>
                  <div><dt>{t('dashboard.statEmpty')}</dt><dd>{stock.empty}</dd></div>
                  <div><dt>{t('dashboard.statFree')}</dt><dd>{stock.freeLocations}</dd></div>
                  <div><dt>{t('dashboard.statReserved')}</dt><dd>{stock.reservedLocations}</dd></div>
                  <div><dt>{t('dashboard.statTransit')}</dt><dd>{stock.transit}</dd></div>
                </dl>
                <ul className="fill-list">
                  {list.map((l) => (
                    <li key={l.id}>
                      <span className="mono">{l.code}</span>
                      <span className="fill-track" title={t('dashboard.fillTitle', { occupied: l.occupied, reserved: l.reserved, capacity: l.rackCapacity })}>
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

        <Panel title={t('dashboard.panelActivity')} className="p-activity" flush>
          {tasks.data === null ? <Async state={tasks} empty={{ title: '' }}>{() => null}</Async> : activities.length === 0 ? (
            <EmptyState title={t('dashboard.noActivity')} hint={t('dashboard.noActivityHint')} />
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
