import { useMemo, useState } from 'react'
import type { Location, TransportTask } from '../../api/types'
import { TASK_STATES } from '../../api/types'
import { TaskStatePill } from '../../components/ui'
import { useT, type MsgKey } from '../../i18n'
import { formatDateTime } from '../../i18n/format'
import { flowInfo, TASK_STATE_VI, taskCode } from '../../i18n/vi'
import { filterTasks, NO_TASK_FILTER, type TaskFilter } from '../../lib/opsConsole'

interface Props {
  tasks: TransportTask[]
  locations: Location[]
  selectedId: string | null
  onSelect: (id: string) => void
}

/** The task list with its filters. Columns come only from fields the server returns; a value the server does not give is shown as "—", never guessed. */
export function TaskMonitor({ tasks, locations, selectedId, onSelect }: Props) {
  const t = useT()
  const [f, setF] = useState<TaskFilter>(NO_TASK_FILTER)
  const code = useMemo(() => {
    const m = new Map(locations.map((l) => [l.id, l.code]))
    return (id: string | null) => (id ? m.get(id) ?? id.slice(0, 8) : '')
  }, [locations])
  const shown = useMemo(() => filterTasks(tasks, f, (x) => taskCode(x.id), code), [tasks, f, code])
  const set = (p: Partial<TaskFilter>) => setF((prev) => ({ ...prev, ...p }))
  const codes = useMemo(() => [...locations].map((l) => l.code).sort(), [locations])
  const dash = t('ops.noValue')

  return (
    <section className="panel ops-monitor" aria-label={t('ops.monTitle')}>
      <h2>{t('ops.monTitle')}</h2>
      <div className="filters">
        <input type="search" value={f.text} placeholder={t('ops.monSearch')} aria-label={t('ops.monSearch')} onChange={(e) => set({ text: e.target.value })} />
        <select value={f.state} aria-label={t('ops.monState')} onChange={(e) => set({ state: e.target.value })}>
          <option value="">{t('ops.monAllStates')}</option>
          {TASK_STATES.map((s) => <option key={s} value={s}>{TASK_STATE_VI[s]}</option>)}
        </select>
        <label>{t('ops.monFrom')} <input type="date" value={f.from} onChange={(e) => set({ from: e.target.value })} /></label>
        <label>{t('ops.monTo')} <input type="date" value={f.to} onChange={(e) => set({ to: e.target.value })} /></label>
        <select value={f.source} aria-label={t('ops.monSource')} onChange={(e) => set({ source: e.target.value })}>
          <option value="">{t('ops.monSource')}: {t('ops.monAnyLocation')}</option>
          {codes.map((c) => <option key={c} value={c}>{c}</option>)}
        </select>
        <select value={f.destination} aria-label={t('ops.monDest')} onChange={(e) => set({ destination: e.target.value })}>
          <option value="">{t('ops.monDest')}: {t('ops.monAnyLocation')}</option>
          {codes.map((c) => <option key={c} value={c}>{c}</option>)}
        </select>
        <button type="button" onClick={() => setF(NO_TASK_FILTER)}>{t('ops.monClear')}</button>
        <span className="muted">{t('ops.monCount', { shown: shown.length, total: tasks.length })}</span>
      </div>
      {tasks.length === 0 ? <p className="muted">{t('ops.monNone')}</p> : shown.length === 0 ? <p className="muted">{t('ops.monNoMatch')}</p> : (
        <div className="table-wrap">
          <table className="data-table">
            <thead>
              <tr>
                <th>{t('ops.colTask')}</th><th>{t('ops.colSource')}</th><th>{t('ops.colDest')}</th><th>{t('ops.colRack')}</th><th>{t('ops.colAgv')}</th>
                <th>{t('ops.colState')}</th><th>{t('ops.colDispatch')}</th><th>{t('ops.colCreated')}</th><th>{t('ops.colUpdated')}</th>
              </tr>
            </thead>
            <tbody>
              {shown.map((x) => (
                <tr key={x.id} className={x.id === selectedId ? 'selected' : ''} onClick={() => onSelect(x.id)} data-task={x.id}>
                  <td><button type="button" className="link" onClick={() => onSelect(x.id)}>{taskCode(x.id)}</button><div className="muted small">{flowInfo(x.flow).code}</div></td>
                  <td>{code(x.sourceLocationId ?? x.requestLocationId) || dash}</td>
                  <td>{code(x.destinationLocationId) || dash}</td>
                  <td className="mono">{x.rackId ? x.rackId.slice(0, 8) : dash}</td>
                  <td>{x.robotCode ?? (x.agvId ? x.agvId.slice(0, 8) : dash)}</td>
                  <td><TaskStatePill state={x.state} /></td>
                  <td>{x.rcsDispatchState ? t(`tasks.dispatchState_${x.rcsDispatchState}` as MsgKey) : t('ops.dispatchNone')}</td>
                  <td>{formatDateTime(x.createdAt)}</td>
                  <td>{formatDateTime(x.updatedAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}
