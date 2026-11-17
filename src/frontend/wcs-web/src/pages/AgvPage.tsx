import { useCallback } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import { AgvCard } from '../components/AgvCard'
import { Async, PageHeader, Panel, TaskStatePill } from '../components/ui'
import { useApi } from '../hooks/useApi'
import { useClock } from '../hooks/useClock'
import { isActive, useCodeMaps } from '../hooks/useLookups'
import { formatDateTime } from '../i18n/format'
import { flowInfo, taskCode } from '../i18n/vi'

const EVENTS = ['TaskCreated', 'TaskStateChanged', 'AgvStatusChanged'] as const

export function AgvPage() {
  const now = useClock(5000).getTime()
  const agvs = useApi(useCallback(() => api.agvs(), []), [...EVENTS])
  const tasks = useApi(useCallback(() => api.tasks(undefined, 0, 200), []), [...EVENTS])
  const locations = useApi(useCallback(() => api.locations(), []))
  const codes = useCodeMaps(locations.data, agvs.data)

  return (
    <>
      <PageHeader
        title="Giám sát AGV"
        description="Trạng thái kết nối RCS, hoạt động và nhiệm vụ hiện tại của AGV1 (rack đầy) và AGV2 (rack rỗng). Hai AGV có thể chạy song song."
      />
      <Async state={agvs} isEmpty={(d) => d.length === 0} empty={{ title: 'Chưa có AGV nào được cấu hình', hint: 'AGV1 và AGV2 được tạo khi khởi động Backend.' }}>
        {(list) => (
          <div className="agv-grid wide">
            {list.map((a) => {
              const mine = (tasks.data?.items ?? []).filter((t) => t.agvId === a.id) // newest first
              return <AgvCard key={a.id} agv={a} task={mine.find(isActive) ?? null} lastTask={mine[0] ?? null} code={codes.loc} now={now} onChanged={() => { agvs.reload(); tasks.reload() }} />
            })}
          </div>
        )}
      </Async>

      <div className="agv-history">
        {(agvs.data ?? []).map((a) => {
          const mine = (tasks.data?.items ?? []).filter((t) => t.agvId === a.id).slice(0, 8)
          return (
            <Panel key={a.id} title={`Nhiệm vụ gần đây của ${a.code}`} flush>
              {mine.length === 0 ? (
                <div className="state state-empty"><div><strong>Chưa có nhiệm vụ nào</strong></div></div>
              ) : (
                <div className="table-wrap">
                  <table className="grid">
                    <thead><tr><th>Mã nhiệm vụ</th><th>Quy trình</th><th>Xuất phát → Đích</th><th>Trạng thái</th><th>Cập nhật</th></tr></thead>
                    <tbody>
                      {mine.map((t) => (
                        <tr key={t.id}>
                          <td><Link to={`/tasks?id=${t.id}`} className="mono">{taskCode(t.id)}</Link></td>
                          <td>{flowInfo(t.flow).code}</td>
                          <td>{codes.loc(t.sourceLocationId ?? t.requestLocationId)} → {codes.loc(t.destinationLocationId)}</td>
                          <td><TaskStatePill state={t.state} /></td>
                          <td className="nowrap">{formatDateTime(t.updatedAt)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </Panel>
          )
        })}
      </div>
    </>
  )
}
