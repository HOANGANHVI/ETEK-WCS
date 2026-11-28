import { AmrMapPanel } from '../components/AmrMapPanel'
import { useCallback, useMemo } from 'react'
import { api } from '../api/client'
import { AgvCard } from '../components/AgvCard'
import { FleetPanel } from '../components/FleetPanel'
import { Async, PageHeader, Panel } from '../components/ui'
import { RcsWorkTable } from '../components/RcsWorkTable'
import type { RcsWork } from '../api/workTypes'
import { isWaitingToBeSent, isWithRcs, workOfAgv } from '../lib/work'
import { StackedSummary, type SummaryItem } from '../components/charts'
import { useApi } from '../hooks/useApi'
import { useClock } from '../hooks/useClock'
import { isActive, useCodeMaps } from '../hooks/useLookups'
import { useT } from '../i18n'
import { useFleet } from '../fleet/fleetState'

const EVENTS = ['TaskCreated', 'TaskStateChanged', 'AgvStatusChanged'] as const
const WORK_EVENTS = ['TaskCreated', 'TaskStateChanged', 'OrderChanged', 'RcsMessageRecorded'] as const

export function AgvPage() {
  const t = useT()
  const now = useClock(5000).getTime()
  const agvs = useApi(useCallback(() => api.agvs(), []), [...EVENTS])
  const fleet = useFleet()
  // CR-028 / CR-031: when the RCS chooses the AMR, or the list comes from the REAL RCS, there is ONE fleet list from the RCS: no AGV1 / AGV2 cards and no per-AGV history
  const managed = fleet.data?.dispatchMode === 'RcsManaged' || fleet.data?.source === 'Real'
  const tasks = useApi(useCallback(() => api.tasks(undefined, 0, 200), []), [...EVENTS])
  // CR-051: what both engines sent to the RCS; shown in the Legacy and in the RcsManaged / Real view
  const work = useApi(useCallback(() => api.rcsWork(false, undefined, 500), []), [...WORK_EVENTS])
  const sent = useMemo(() => (work.data ?? []).filter((w) => w.rcsState !== 'NotSent'), [work.data])
  const waiting = (work.data ?? []).filter(isWaitingToBeSent).length
  const other = sent.filter((w) => !(agvs.data ?? []).some((a) => workOfAgv([w], a).length > 0))
  const locations = useApi(useCallback(() => api.locations(), []))
  const codes = useCodeMaps(locations.data, agvs.data)
  const summary = useMemo<SummaryItem[]>(() => {
    const l = agvs.data ?? []
    const down = (a: (typeof l)[number]) => a.connectionStatus === 'Offline' || a.connectionStatus === 'Error'
    const busy = (a: (typeof l)[number]) => a.busy || workOfAgv(sent, a).some(isWithRcs)
    return [
      { key: 'work', label: t('agv.sumWorking'), tone: 'ok', value: l.filter((a) => !a.dispatchHold && !down(a) && a.connectionStatus === 'Online' && busy(a)).length },
      { key: 'ready', label: t('agv.sumReady'), tone: 'info', value: l.filter((a) => !a.dispatchHold && a.connectionStatus === 'Online' && !busy(a)).length },
      { key: 'hold', label: t('agv.sumHold'), tone: 'warn', value: l.filter((a) => a.dispatchHold).length },
      { key: 'down', label: t('agv.sumDown'), tone: 'bad', value: l.filter((a) => !a.dispatchHold && down(a)).length },
      { key: 'unk', label: t('agv.sumUnknown'), tone: 'muted', value: l.filter((a) => !a.dispatchHold && a.connectionStatus === 'Unknown').length },
    ]
  }, [agvs.data, sent, t])

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
      <AmrMapPanel />
      <Async state={fleet} empty={{ title: t('agv.fleetNoData') }}>{(f) => <FleetPanel fleet={f} />}</Async>
      {!managed && <Async state={agvs} isEmpty={(d) => d.length === 0} empty={{ title: t('agv.emptyTitle'), hint: t('agv.emptyHint') }}>
        {(list) => (
          <div className="agv-grid wide">
            {list.map((a) => {
              const mine = (tasks.data?.items ?? []).filter((t) => t.agvId === a.id) // newest first
              const own = workOfAgv(sent, a)
              return <AgvCard key={a.id} agv={a} task={mine.find(isActive) ?? null} lastTask={mine[0] ?? null} code={codes.loc} now={now} onChanged={() => { agvs.reload(); tasks.reload() }}
                leg={own.find((w) => w.kind === 'OrderLeg' && isWithRcs(w)) ?? null} lastWorkAt={own[0]?.updatedAt ?? null} />
            })}
          </div>
        )}
      </Async>}

      {waiting > 0 && <div className="notice notice-info">{t('linked.rcsWaitingSend', { n: waiting })}</div>}
      {work.error ? <div className="notice notice-bad" role="alert">{t('linked.rcsUnavailable')}</div> : !managed ? (
        <div className="agv-history">
          {(agvs.data ?? []).map((a) => <WorkPanel key={a.id} title={t('agv.recentTasks', { code: a.code })} rows={workOfAgv(sent, a).slice(0, 8)} />)}
          {other.length > 0 && <WorkPanel title={t('linked.rcsOtherTitle')} rows={other.slice(0, 8)} />}
        </div>
      ) : <div className="agv-history"><WorkPanel title={t('linked.rcsAllTitle')} rows={sent.slice(0, 30)} /></div>}
    </>
  )
}

function WorkPanel({ title, rows }: { title: string; rows: RcsWork[] }) {
  const t = useT()
  return (
    <Panel title={title} flush>
      {rows.length === 0 ? <div className="state state-empty"><div><strong>{t('linked.rcsNone')}</strong></div></div> : <RcsWorkTable rows={rows} />}
    </Panel>
  )
}
