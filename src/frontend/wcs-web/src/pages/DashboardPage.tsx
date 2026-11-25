import { useCallback, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import type { InventoryTransaction, TransportTask } from '../api/types'
import { AgvCard } from '../components/AgvCard'
import { ServerResources } from '../components/ServerResources'
import { DEMO_MODE } from '../config'
import { BarChart, ChartEmpty, Donut, LineChart } from '../components/charts'
import { IconAgv, IconClock, IconTasks, IconWarehouse } from '../components/icons'
import { linkText, plcView, rcsView } from '../components/linkStatus'
import { useFleet } from '../fleet/fleetState'
import { useAuth } from '../auth/AuthContext'
import { Async, EmptyState, Kpi, PageHeader, Panel, SectionHeader, TaskStatePill } from '../components/ui'
import { useApi } from '../hooks/useApi'
import { useClock } from '../hooks/useClock'
import { useFinishedTasks } from '../hooks/useFinishedTasks'
import { isActive, useCodeMaps } from '../hooks/useLookups'
import { useRealtime } from '../realtime/RealtimeProvider'
import { formatNumber, formatTime } from '../i18n/format'
import { useLang, useT } from '../i18n'
import { describeHistory } from '../i18n/logCatalog'
import { flowInfo, INVENTORY_EVENT_VI, reasonVi, TASK_STATE_VI, taskCode, type Tone } from '../i18n/vi'
import { formatUptime } from '../lib/resourceMonitor'
import { buildBuckets, completedPerAgv, completedPerRobot, countByBucket, failureRate, meanCycle, meanCycleByBucket, RANGES, type RangeKey } from '../lib/dashboardStats'

const EVENTS_TASK = ['TaskCreated', 'TaskStateChanged'] as const
const EVENTS_INV = ['InventoryChanged', 'TaskStateChanged'] as const
const RANGE_KEY = 'wcs.dashboard.range'

interface Activity { id: string; at: string; tone: Tone; text: string; detail?: string }

function loadRange(): RangeKey {
  try { const v = window.localStorage.getItem(RANGE_KEY); return RANGES.includes(v as RangeKey) ? (v as RangeKey) : '7d' } catch { return '7d' }
}

export function DashboardPage() {
  const t = useT()
  const { lang } = useLang()
  const { user } = useAuth()
  const isAdmin = user?.role === 'Admin'
  const now = useClock(5000).getTime()
  const minute = Math.floor(now / 60_000) * 60_000   // the period boundaries only need minute resolution
  const { status: realtime } = useRealtime()
  const [range, setRangeState] = useState<RangeKey>(loadRange)
  const setRange = (r: RangeKey) => { setRangeState(r); try { window.localStorage.setItem(RANGE_KEY, r) } catch { /* the choice is only a convenience */ } }

  const agvs = useApi(useCallback(() => api.agvs(), []), ['TaskStateChanged', 'AgvStatusChanged'])
  const fleet = useFleet()   // CR-028: the one shared fleet view (also read by the header)
  const managed = fleet.data?.dispatchMode === 'RcsManaged' || fleet.data?.source === 'Real'
  const locations = useApi(useCallback(() => api.locations(), []), [...EVENTS_INV])
  const racks = useApi(useCallback(() => api.racks(), []), [...EVENTS_INV])
  const tasks = useApi(useCallback(() => api.tasks(undefined, 0, 200), []), [...EVENTS_TASK])
  const health = useApi(useCallback(() => api.health(), []), ['TaskStateChanged'])
  const tx = useApi(useCallback(() => api.transactions(), []), [...EVENTS_INV])
  const finished = useFinishedTasks(range)
  const codes = useCodeMaps(locations.data, agvs.data)

  const taskItems = tasks.data?.items
  const agvList = agvs.data
  const locList = locations.data
  const rackList = racks.data

  // ---------------------------------------------------------------- figures of the selected period (real task timestamps only)
  const stats = useMemo(() => {
    const data = finished.data
    if (!data) return null
    const buckets = buildBuckets(range, new Date(minute))
    const completed = data.completed, failed = data.failed
    return {
      buckets,
      completed: completed.length,
      failed: failed.length,
      completedByBucket: countByBucket(completed, buckets),
      failedByBucket: countByBucket(failed, buckets),
      rate: failureRate(completed.length, failed.length),
      cycle: meanCycle(completed),
      cycleByBucket: meanCycleByBucket(completed, buckets),
      cycleCount: buckets.map((b) => completed.filter((x) => x.finishedAt && Date.parse(x.finishedAt) >= b.start && Date.parse(x.finishedAt) < b.end).length),
      perAgv: completedPerAgv(completed),
      perRobot: completedPerRobot(completed),
      truncated: data.truncated,
    }
  }, [finished.data, range, minute])

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
        text: `${taskCode(tk.id)} · ${flowInfo(tk.flow).code} · ${tk.robotCode ?? codes.agv(tk.agvId)}: ${TASK_STATE_VI[tk.state]}`,
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

  // ---------------------------------------------------------------- display helpers
  const periodText = t(range === 'today' ? 'dashboard.periodToday' : range === '7d' ? 'dashboard.period7d' : 'dashboard.period30d')
  const locale = lang === 'en' ? 'en-GB' : 'vi-VN'
  const bucketLabel = (start: number) => range === 'today'
    ? new Date(start).toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit', hour12: false })
    : new Date(start).toLocaleDateString(locale, { day: '2-digit', month: '2-digit' })
  const bucketTitle = (start: number) => range === 'today' ? bucketLabel(start) : new Date(start).toLocaleDateString(locale, { day: '2-digit', month: '2-digit', year: 'numeric' })
  const duration = (ms: number) => ms < 90_000 ? t('dashboard.durSec', { n: Math.round(ms / 1000) })
    : ms < 5_400_000 ? t('dashboard.durMin', { n: Math.round((ms / 60_000) * 10) / 10 }) : t('dashboard.durHour', { n: Math.round((ms / 3_600_000) * 10) / 10 })
  const uptimeText = (secs: number) => formatUptime(secs, t)
  const labels = stats?.buckets.map((b) => bucketLabel(b.start)) ?? []
  const rate = stats?.rate ?? null
  const cycleMax = Math.max(0, ...(stats?.cycleByBucket.filter((v): v is number => v !== null) ?? []))
  const cycleUnit = cycleMax < 120_000 ? { div: 1000, key: 'dashboard.unitSec' as const } : cycleMax < 7_200_000 ? { div: 60_000, key: 'dashboard.unitMin' as const } : { div: 3_600_000, key: 'dashboard.unitHour' as const }
  const hasFinished = !!stats && stats.completed + stats.failed > 0
  const plc = plcView(health.error && !health.data ? null : health.data)
  const rcs = rcsView(fleet.data, !!fleet.error && !fleet.data)
  const rtTone = realtime === 'connected' ? 'ok' : realtime === 'disconnected' ? 'bad' : 'warn'
  // Application health (backend process + PostgreSQL) is shown apart from the devices: a running backend says nothing about PLC, RCS or AGV.
  const hd = health.error && !health.data ? null : health.data
  const backendTone = hd ? 'ok' : 'bad'
  const backendText = hd?.backend ? t('monitor.backendUp', { uptime: uptimeText(hd.backend.uptimeSeconds) }) : hd ? t('nav.connected') : t('nav.disconnected')
  const dbTone = hd ? (hd.database.connected ? 'ok' : 'bad') : 'bad'
  const dbText = !hd ? t('nav.disconnected') : hd.database.connected ? (hd.database.latencyMs != null ? t('monitor.dbUp', { ms: formatNumber(Math.round(hd.database.latencyMs)) }) : t('nav.connected')) : t('nav.disconnected')
  const simulator = health.data?.integration.mode === 'Simulator' || health.data?.integration.plcRuntime?.mode === 'Simulator'
  const running = (taskItems ?? []).filter(isActive)
  const maxPerAgv = Math.max(1, ...(agvList ?? []).map((a) => stats?.perAgv.get(a.id) ?? 0))

  return (
    <>
      <PageHeader
        title={isAdmin ? t('dashboard.titleAdmin') : t('dashboard.titleOperator')}
        actions={(
          <div className="range-switch" role="group" aria-label={t('dashboard.rangeLabel')}>
            {RANGES.map((r) => (
              <button key={r} type="button" className={range === r ? 'active' : ''} aria-pressed={range === r} onClick={() => setRange(r)}>
                {t(r === 'today' ? 'dashboard.rangeToday' : r === '7d' ? 'dashboard.range7d' : 'dashboard.range30d')}
              </button>
            ))}
          </div>
        )}
      />

      <SectionHeader title={t('dashboard.secFleet')} />
      <div className="kpi-row dash-kpis">
        <Kpi label={t('dashboard.kpiTotalAgv')} value={agvList ? kpi.totalAgv : '—'} hint="AGV1 · AGV2" icon={<IconAgv />} />
        <Kpi label={t('dashboard.kpiWorking')} value={agvList ? kpi.working : '—'} hint={t('dashboard.kpiWorkingHint')} tone="ok" icon={<IconAgv />} />
        <Kpi label={t('dashboard.kpiWaiting')} value={agvList ? kpi.waiting : '—'} hint={t('dashboard.kpiWaitingHint')} tone="warn" icon={<IconClock />} />
        <Kpi label={t('dashboard.kpiRunning')} value={taskItems ? kpi.running : '—'} hint={t('dashboard.kpiRunningHint')} tone="info" icon={<IconTasks />} />
      </div>
      <SectionHeader title={t('dashboard.secPerformance')} hint={periodText} />
      <div className="kpi-row dash-kpis">
        <Kpi label={t('dashboard.kpiCompleted')} value={stats ? formatNumber(stats.completed) : '—'} hint={periodText} tone="ok" icon={<IconTasks />} />
        <Kpi label={t('dashboard.kpiFailureRate')} value={stats ? (rate === null ? '—' : `${formatNumber(Math.round(rate * 10) / 10)}%`) : '—'}
          hint={stats ? (rate === null ? t('dashboard.noData') : t('dashboard.kpiFailureRateHint', { failed: stats.failed, total: stats.completed + stats.failed })) : undefined} tone="info" icon={<IconTasks />} />
        <Kpi label={t('dashboard.kpiCycle')} value={stats ? (stats.cycle === null ? '—' : duration(stats.cycle)) : '—'}
          hint={stats && stats.cycle === null ? t('dashboard.noData') : t('dashboard.kpiCycleHint')} tone="info" icon={<IconClock />} />
        <Kpi label={t('dashboard.kpiRacks')} value={rackList ? kpi.racks : '—'}
          hint={rackList ? t('dashboard.kpiRacksHint', { full: stock.full, empty: stock.empty, transit: stock.transit }) : undefined} tone="info" icon={<IconWarehouse />} />
      </div>
      {stats?.truncated && <div className="notice notice-warn">{t('dashboard.truncated', { n: 2000 })}</div>}

      <SectionHeader title={t('dashboard.secAnalytics')} />
      <div className="dc-grid">
        <Panel title={t('dashboard.trendTitle')} className="dc-8" subtitle={range === 'today' ? t('dashboard.trendUnitHour') : t('dashboard.trendUnitDay')}>
          <Async state={finished} empty={{ title: t('dashboard.noData') }}>
            {() => !stats || !hasFinished ? <ChartEmpty title={t('dashboard.noData')} /> : (
              <>
                <div className="chart-meta">
                  <div className="legend"><span><i className="ch-ok" />{t('dashboard.legendCompleted')}</span><span><i className="ch-bad" />{t('dashboard.legendFailed')}</span></div>
                  <span>{periodText}</span>
                </div>
                <BarChart labels={labels} ariaLabel={t('dashboard.trendAria')}
                  tooltipLabels={stats.buckets.map((b, i) => t('dashboard.trendTip', { label: bucketTitle(b.start), completed: stats.completedByBucket[i], failed: stats.failedByBucket[i] }))}
                  series={[{ label: 'completed', tone: 'ok', values: stats.completedByBucket }, { label: 'failed', tone: 'bad', values: stats.failedByBucket }]} />
              </>
            )}
          </Async>
        </Panel>

        <Panel title={t('dashboard.rateTitle')} className="dc-4">
          <Async state={finished} empty={{ title: t('dashboard.noData') }}>
            {() => !stats || !hasFinished ? <ChartEmpty title={t('dashboard.noData')} /> : (
              <>
                <div className="donut-wrap">
                  <Donut ariaLabel={t('dashboard.rateAria')}
                    slices={[{ label: t('dashboard.legendCompleted'), value: stats.completed, tone: 'ok' }, { label: t('dashboard.legendFailed'), value: stats.failed, tone: 'bad' }]}
                    center={<><strong>{rate === null ? '—' : `${formatNumber(Math.round(rate * 10) / 10)}%`}</strong><span>{t('dashboard.rateCenter')}</span></>} />
                  <ul className="donut-legend">
                    <li><i className="ch-ok" />{t('dashboard.legendCompleted')}<strong>{formatNumber(stats.completed)}</strong></li>
                    <li><i className="ch-bad" />{t('dashboard.legendFailed')}<strong>{formatNumber(stats.failed)}</strong></li>
                  </ul>
                </div>
                <p className="chart-note">{t('dashboard.rateNote')}</p>
              </>
            )}
          </Async>
        </Panel>

        <Panel title={t('dashboard.cycleTitle')} className="dc-7" subtitle={t('dashboard.cycleUnit', { unit: t(cycleUnit.key) })}>
          <Async state={finished} empty={{ title: t('dashboard.noData') }}>
            {() => !stats || stats.cycle === null ? <ChartEmpty title={t('dashboard.noData')} /> : (
              <>
                <LineChart labels={labels} ariaLabel={t('dashboard.cycleAria')} formatY={(n) => String(Math.round(n * 10) / 10)}
                  values={stats.cycleByBucket.map((v) => (v === null ? null : v / cycleUnit.div))}
                  tooltipLabels={stats.buckets.map((b, i) => (stats.cycleByBucket[i] === null ? bucketTitle(b.start) : t('dashboard.cycleTip', { label: bucketTitle(b.start), value: duration(stats.cycleByBucket[i] as number), n: stats.cycleCount[i] })))} />
                <p className="chart-note">{t('dashboard.cycleNote')}</p>
              </>
            )}
          </Async>
        </Panel>

        <Panel title={t('dashboard.agvUtilTitle')} className="dc-5">
          <div className="unavailable"><strong>{t('dashboard.agvUtilUnavailable')}</strong></div>
          <h3 className="mini-title" style={{ marginTop: 16 }}>{t('dashboard.agvDoneTitle')}</h3>
          {managed && stats ? (
            <ul className="hbars">
              {[...stats.perRobot.byRobot.entries()].sort((a, b) => a[0].localeCompare(b[0])).map(([code, n]) => (
                <li key={code}><span>{code}</span><span className="track"><span className="fill" style={{ width: `${(n / Math.max(1, ...stats.perRobot.byRobot.values())) * 100}%` }} /></span><strong>{n}</strong></li>
              ))}
              <li key="__none"><span className="muted">{t('dashboard.perRobotNone')}</span><span className="track" /><strong>{stats.perRobot.withoutRobot}</strong></li>
            </ul>
          ) : !stats || !agvList ? <div className="chart-empty">{t('dashboard.loadingStats')}</div> : (
            <ul className="hbars">
              {agvList.map((a) => { const n = stats.perAgv.get(a.id) ?? 0; return (
                <li key={a.id}><span>{a.code}</span><span className="track"><span className="fill" style={{ width: `${(n / maxPerAgv) * 100}%` }} /></span><strong>{n}</strong></li>
              ) })}
            </ul>
          )}
        </Panel>

        <SectionHeader className="dc-12" title={t('dashboard.secHealth')} />
        <Panel title={t('dashboard.liveTitle')} className="dc-7">
          <div className="live-links">
            <div className="live-link"><i className={`led ${backendTone}`} /><strong>{t('monitor.liveBackend')}</strong><span>{backendText}</span></div>
            <div className="live-link"><i className={`led ${dbTone}`} /><strong>{t('monitor.livePostgres')}</strong><span>{dbText}</span></div>
            <div className="live-link"><i className={`led ${plc.tone}`} /><strong>PLC</strong><span>{linkText(t, plc.text)}</span></div>
            <div className="live-link"><i className={`led ${rcs.tone}`} /><strong>RCS</strong><span>{linkText(t, rcs.text)}</span></div>
            <div className="live-link"><i className={`led ${rtTone}`} /><strong>{t('dashboard.liveRealtime')}</strong><span>{t(`nav.rt${realtime[0].toUpperCase()}${realtime.slice(1)}` as 'nav.rtConnected')}</span></div>
            {simulator && <div className="live-link"><i className="led warn" /><span>{t('dashboard.liveSimulator')}</span></div>}
          </div>
          {managed ? <p className="muted">{t('dashboard.liveFleet', { n: fleet.data?.count ?? 0, w: fleet.data?.tasksAwaitingAssignment ?? 0 })}</p> : <Async state={agvs} isEmpty={(d) => d.length === 0} empty={{ title: t('agv.emptyTitle'), hint: t('agv.emptyHint') }}>
            {(list) => (
              <div className="live-agvs">
                {list.map((a) => { const cur = tasksByAgv(a.id); return <AgvCard key={a.id} agv={a} task={cur.current} lastTask={cur.last} code={codes.loc} now={now} compact /> })}
              </div>
            )}
          </Async>}
          <h3 className="mini-title" style={{ marginTop: 16 }}>{t('dashboard.liveInProgress')}</h3>
          {running.length === 0 ? <p className="muted">{t('dashboard.liveNone')}</p> : (
            <ul className="live-tasks">
              {running.map((tk) => (
                <li key={tk.id}><Link to={`/tasks?id=${tk.id}`} className="mono">{taskCode(tk.id)}</Link><span>{flowInfo(tk.flow).code}</span><span>{t('dashboard.liveAgv', { agv: codes.agv(tk.agvId) })}</span><TaskStatePill state={tk.state} /></li>
              ))}
            </ul>
          )}
        </Panel>

        <Panel title={t('dashboard.panelActivity')} className="dc-5" flush>
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

        <Panel title={t('dashboard.panelStock')} className="dc-12">
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

        {/* Server resources: secondary information, so it comes last. Admin only, real mode only (Demo Mode has no server to measure). */}
        {isAdmin && !DEMO_MODE && <ServerResources />}
      </div>
    </>
  )
}
