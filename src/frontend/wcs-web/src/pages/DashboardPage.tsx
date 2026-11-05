import { useCallback, useMemo } from 'react'
import { api } from '../api/client'
import { TASK_STATES } from '../api/types'
import { formatTime } from '../components/format'
import { Async, Badge, Card } from '../components/ui'
import { useApi } from '../hooks/useApi'

export function DashboardPage() {
  const health = useApi(useCallback(() => api.health(), []), ['TaskStateChanged'])
  const agvs = useApi(useCallback(() => api.agvs(), []), ['TaskStateChanged', 'AgvStatusChanged'])
  const tasks = useApi(useCallback(() => api.tasks(undefined, 0, 200), []), ['TaskCreated', 'TaskStateChanged'])
  const locations = useApi(useCallback(() => api.locations(), []), ['InventoryChanged', 'TaskStateChanged'])

  const counts = useMemo(() => {
    const m = new Map<string, number>()
    tasks.data?.items.forEach((t) => m.set(t.state, (m.get(t.state) ?? 0) + 1))
    return m
  }, [tasks.data])

  return (
    <>
      <h1>Dashboard</h1>
      <div className="grid">
        <Card title="System health">
          <Async state={health} empty={{ title: 'No health data' }}>
            {(h) => (
              <dl className="kv">
                <dt>Status</dt><dd><Badge value={h.status} /></dd>
                <dt>Database</dt><dd><Badge value={h.database.connected ? 'Connected' : 'Disconnected'} /></dd>
                <dt>Scheduler</dt><dd>{h.scheduler.stale ? <Badge value="Degraded" /> : <Badge value="Healthy" />} last run {formatTime(h.scheduler.lastRunAt)}</dd>
                <dt>Integration</dt><dd>{h.integration.mode} (PLC: {h.integration.plc})</dd>
                {h.scheduler.lastError && (<><dt>Last error</dt><dd className="text-bad">{h.scheduler.lastError}</dd></>)}
              </dl>
            )}
          </Async>
        </Card>

        <Card title="AGVs">
          <Async state={agvs} isEmpty={(d) => d.length === 0} empty={{ title: 'No AGV configured', hint: 'AGV1 and AGV2 are created by the seed step.' }}>
            {(list) => (
              <ul className="plain">
                {list.map((a) => (
                  <li key={a.id}>
                    <strong>{a.code}</strong> <span className="muted">{a.role === 'FullRack' ? 'full racks' : 'empty racks'}</span>{' '}
                    <Badge value={a.busy ? 'Busy' : 'Idle'} /> <Badge value={a.connectionStatus} />
                  </li>
                ))}
              </ul>
            )}
          </Async>
        </Card>

        <Card title="Tasks by state (latest 200)">
          <Async state={tasks} isEmpty={(d) => d.items.length === 0} empty={{ title: 'No tasks yet', hint: 'Tasks appear when a station requests a transport.' }}>
            {() => (
              <ul className="chips">
                {TASK_STATES.filter((s) => counts.get(s)).map((s) => (
                  <li key={s}><Badge value={s} /> <strong>{counts.get(s)}</strong></li>
                ))}
              </ul>
            )}
          </Async>
        </Card>

        <Card title="Inventory overview">
          <Async state={locations} isEmpty={(d) => d.length === 0} empty={{ title: 'No locations configured' }}>
            {(list) => (
              <dl className="kv">
                <dt>Locations</dt><dd>{list.length}</dd>
                <dt>Racks stored</dt><dd>{list.reduce((n, l) => n + l.occupied, 0)}</dd>
                <dt>Slots reserved</dt><dd>{list.reduce((n, l) => n + l.reserved, 0)}</dd>
                <dt>Unknown state</dt><dd>{list.filter((l) => l.occupancy === 'Unknown').length}</dd>
              </dl>
            )}
          </Async>
        </Card>
      </div>
    </>
  )
}
