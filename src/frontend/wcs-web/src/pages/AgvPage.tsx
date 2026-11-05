import { useCallback } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import { formatDateTime } from '../components/format'
import { Async, Badge, Card } from '../components/ui'
import { useApi } from '../hooks/useApi'

export function AgvPage() {
  const agvs = useApi(useCallback(() => api.agvs(), []), ['TaskStateChanged', 'AgvStatusChanged'])
  return (
    <>
      <h1>AGV Monitoring</h1>
      <Card title="AGVs (PER_AGV: at most one active task each)">
        <Async state={agvs} isEmpty={(d) => d.length === 0} empty={{ title: 'No AGV configured' }}>
          {(list) => (
            <table>
              <thead>
                <tr><th>AGV</th><th>Role</th><th>RCS robot code</th><th>Enabled</th><th>Connection</th><th>Work</th><th>Active task</th><th>Last status</th></tr>
              </thead>
              <tbody>
                {list.map((a) => (
                  <tr key={a.id}>
                    <td><strong>{a.code}</strong></td>
                    <td>{a.role === 'FullRack' ? 'Full racks' : 'Empty racks'}</td>
                    <td>{a.rcsRobotCode ?? <span className="muted">not configured</span>}</td>
                    <td>{a.enabled ? 'Yes' : 'No'}</td>
                    <td><Badge value={a.connectionStatus} />{a.statusDetail && <span className="muted"> {a.statusDetail}</span>}</td>
                    <td><Badge value={a.busy ? 'Busy' : 'Idle'} /></td>
                    <td>{a.activeTaskId ? <Link to={`/tasks?id=${a.activeTaskId}`}>{a.activeTaskId.slice(0, 8)}</Link> : '—'}</td>
                    <td>{formatDateTime(a.lastStatusAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </Async>
      </Card>
    </>
  )
}
