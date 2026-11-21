import { useCallback, useMemo } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import { AgvCard } from '../components/AgvCard'
import { FleetPanel } from '../components/FleetPanel'
import { Async, PageHeader, Panel, TaskStatePill } from '../components/ui'
import { StackedSummary, type SummaryItem } from '../components/charts'
import { useApi } from '../hooks/useApi'
import { useClock } from '../hooks/useClock'
import { isActive, useCodeMaps } from '../hooks/useLookups'
import { formatDateTime } from '../i18n/format'
import { useT } from '../i18n'
import { flowInfo, taskCode } from '../i18n/vi'

const EVENTS = ['TaskCreated', 'TaskStateChanged', 'AgvStatusChanged'] as const

export function AgvPage() {
  const t = useT()
  const now = useClock(5000).getTime()
  const agvs = useApi(useCallback(() => api.agvs(), []), [...EVENTS])
  const fleet = useApi(useCallback(() => api.agvFleet(), []), ['FleetChanged', 'TaskStateChanged'])
  const managed = fleet.data?.dispatchMode === 'RcsManaged'   // CR-028: the RCS chooses the AMR: one fleet list, no AGV1 / AGV2 cards
  const tasks = useApi(useCallback(() => api.tasks(undefined, 0, 200), []), [...EVENTS])
  const locations = useApi(useCallback(() => api.locations(), []))
  const codes = useCodeMaps(locations.data, agvs.data)
  const summary = useMemo<SummaryItem[]>(() => {
    const l = agvs.data ?? []
    const down = (a: (typeof l)[number]) => a.connectionStatus === 'Offline' || a.connectionStatus === 'Error'
    return [
      { key: 'work', label: t('agv.sumWorking'), tone: 'ok', value: l.filter((a) => !a.dispatchHold && !down(a) && a.connectionStatus === 'Online' && a.busy).length },
      { key: 'ready', label: t('agv.sumReady'), tone: 'info', value: l.filter((a) => !a.dispatchHold && a.connectionStatus === 'Online' && !a.busy).length },
      { key: 'hold', label: t('agv.sumHold'), tone: 'warn', value: l.filter((a) => a.dispatchHold).length },
      { key: 'down', label: t('agv.sumDown'), tone: 'bad', value: l.filter((a) => !a.dispatchHold && down(a)).length },
      { key: 'unk', label: t('agv.sumUnknown'), tone: 'muted', value: l.filter((a) => !a.dispatchHold && a.connectionStatus === 'Unknown').length },
    ]
  }, [agvs.data, t])

  return (
    <>
      <PageHeader
        title={t('agv.pageTitle')}
      />
      {!managed && (
        <Panel title={t('agv.sumTitle')} subtitle={t('agv.sumSub', { n: agvs.data?.length ?? 0 })} className="mb">
          <StackedSummary items={summary} ariaLabel={t('agv.sumTitle')} emptyTitle={t('agv.sumEmpty')} />
        </Panel>
      )}
      <Async state={fleet} empty={{ title: t('agv.fleetNoData') }}>{(f) => <FleetPanel fleet={f} />}</Async>
      {!managed && <Async state={agvs} isEmpty={(d) => d.length === 0} empty={{ title: t('agv.emptyTitle'), hint: t('agv.emptyHint') }}>
        {(list) => (
          <div className="agv-grid wide">
            {list.map((a) => {
              const mine = (tasks.data?.items ?? []).filter((t) => t.agvId === a.id) // newest first
              return <AgvCard key={a.id} agv={a} task={mine.find(isActive) ?? null} lastTask={mine[0] ?? null} code={codes.loc} now={now} onChanged={() => { agvs.reload(); tasks.reload() }} />
            })}
          </div>
        )}
      </Async>}

      {!managed && <div className="agv-history">
        {(agvs.data ?? []).map((a) => {
          const mine = (tasks.data?.items ?? []).filter((t) => t.agvId === a.id).slice(0, 8)
          return (
            <Panel key={a.id} title={t('agv.recentTasks', { code: a.code })} flush>
              {mine.length === 0 ? (
                <div className="state state-empty"><div><strong>{t('agv.noTasks')}</strong></div></div>
              ) : (
                <div className="table-wrap">
                  <table className="grid">
                    <thead><tr><th>{t('agv.colTask')}</th><th>{t('agv.colFlow')}</th><th>{t('agv.colRoute')}</th><th>{t('agv.colState')}</th><th>{t('agv.colUpdated')}</th></tr></thead>
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
      </div>}
    </>
  )
}
