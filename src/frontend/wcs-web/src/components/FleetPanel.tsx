import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import type { FleetAmr, FleetView } from '../api/fleetTypes'
import { EmptyState, Panel, Pill } from './ui'
import { hasMessage, useT, type MsgKey } from '../i18n'
import { formatDateTime } from '../i18n/format'
import { taskCode } from '../i18n/vi'
import type { Tone } from '../i18n/vi'
import { batteryPercent, computeKpis, distinctMaps, filterAmrs, isMoving, NO_FILTER, type FleetFilter } from '../lib/fleetModel'
import { classifyStatus, type StatusCategory } from '../lib/rcsStatus'
import '../styles/fleet.css'

const CATEGORY_TONE: Record<StatusCategory, Tone> = { Idle: 'ok', Done: 'ok', Working: 'info', Charging: 'info', Stopped: 'warn', Fault: 'bad', Unknown: 'muted' }
const CATEGORIES: StatusCategory[] = ['Idle', 'Working', 'Done', 'Charging', 'Stopped', 'Fault', 'Unknown']
const DATA_TONE: Record<FleetAmr['state'], Tone> = { Fresh: 'ok', Stale: 'warn', Missing: 'muted' }
const VIEW_KEY = 'wcs.fleet.view'

const has = (v: string | null | undefined): v is string => v !== null && v !== undefined && v !== ''

function readView(): 'cards' | 'list' {
  try { return localStorage.getItem(VIEW_KEY) === 'list' ? 'list' : 'cards' } catch { return 'cards' }
}

/** The RCS status of one AMR: the label of a documented code, or "Unclassified (code)" with the raw code kept; never a guessed meaning. */
function StatusPill({ a }: { a: FleetAmr }) {
  const t = useT()
  const c = classifyStatus(a.status)
  if (c.category === null) return <span className="muted">{t('agv.noData')}</span>
  if (!c.documented) return <Pill tone="muted">{t('agv.amrStatusUnknown', { code: c.raw ?? '' })}</Pill>
  const key = `agv.amrStatus_${c.raw}` as MsgKey
  return <Pill tone={CATEGORY_TONE[c.category]}>{c.raw} · {hasMessage(key) ? t(key) : t(`agv.statusCat_${c.category}` as MsgKey)}</Pill>
}

function Battery({ value }: { value: string | null }) {
  const t = useT()
  const p = batteryPercent(value)
  if (p === null) return <span className="muted">{has(value) ? value : t('agv.noData')}</span>
  return (
    <span className="amr-battery" title={`${p} %`}>
      <span className="amr-battery-bar" role="img" aria-label={`${p} %`}><i style={{ width: `${p}%` }} /></span>
      <span className="amr-battery-num">{p} %</span>
    </span>
  )
}

function Kpi({ label, value, note, tone }: { label: string; value: ReactNode; note?: string; tone?: Tone }) {
  return (
    <div className={`fleet-kpi${tone ? ` fleet-kpi-${tone}` : ''}`}>
      <div className="fleet-kpi-label">{label}</div>
      <div className="fleet-kpi-value">{value}</div>
      {note && <div className="fleet-kpi-note">{note}</div>}
    </div>
  )
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return <><dt>{label}</dt><dd>{children}</dd></>
}

function AmrCard({ a, fleet, onOpen }: { a: FleetAmr; fleet: FleetView; onOpen: () => void }) {
  const t = useT()
  const nd = <span className="muted">{t('agv.noData')}</span>
  return (
    <button type="button" className={`amr-card is-${a.state.toLowerCase()}`} onClick={onOpen} aria-label={t('agv.cardOpen', { code: a.robotCode })}>
      <span className="amr-card-head">
        <strong className="mono amr-code">{a.robotCode}</strong>
        <Pill tone={DATA_TONE[a.state]}>{t(`agv.fleetState_${a.state}` as MsgKey)}</Pill>
      </span>
      <span className="amr-card-status"><StatusPill a={a} /></span>
      <Battery value={a.battery} />
      <dl className="amr-card-grid">
        <Field label={t('agv.cardMap')}>{has(a.mapCode) ? <span className="mono">{a.mapCode}</span> : nd}</Field>
        <Field label={t('agv.cardSource')}><Pill tone={fleet.simulatedData ? 'warn' : 'info'} dot={false}>{fleet.simulatedData ? t('agv.fleetSimulated') : t('agv.fleetReal')}</Pill></Field>
        <Field label={t('agv.fleetColPos')}>{a.posX === null && a.posY === null ? nd : <span className="mono">{has(a.posX) ? a.posX : '–'}, {has(a.posY) ? a.posY : '–'}</span>}</Field>
        <Field label={t('agv.fleetColDir')}>{has(a.robotDir) ? `${a.robotDir}°` : nd}</Field>
        <Field label={t('agv.fleetColSpeed')}>{has(a.speed) ? <>{a.speed} mm/s{isMoving(a) && <span className="amr-moving" aria-hidden="true"> ▶</span>}</> : nd}</Field>
        <Field label={t('agv.fleetColRack')}>{has(a.podCode) ? <span className="mono">{a.podCode}</span> : nd}</Field>
        <Field label={t('agv.cardBusinessAgv')}>{has(a.linkedAgvCode) ? a.linkedAgvCode : nd}</Field>
        <Field label={t('agv.fleetColTask')}>{a.activeTaskId ? <span className="mono">{taskCode(a.activeTaskId)}</span> : nd}</Field>
      </dl>
      <span className="amr-card-foot muted small">{t('agv.fleetColSeen')}: {formatDateTime(a.lastSeenAt)}</span>
    </button>
  )
}

/** Side drawer with the details of one AMR; it follows the live data (the AMR is looked up by robot code on every refresh). */
function AmrDrawer({ a, fleet, onClose }: { a: FleetAmr; fleet: FleetView; onClose: () => void }) {
  const t = useT()
  const closeRef = useRef<HTMLButtonElement>(null)
  useEffect(() => {
    closeRef.current?.focus()
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])
  const nd = <span className="muted">{t('agv.noData')}</span>
  const c = classifyStatus(a.status)
  const errKey = fleet.lastErrorCode ? (`agv.fleetErr_${fleet.lastErrorCode.split(':')[0]}` as MsgKey) : null
  const err = fleet.lastErrorCode ? `${errKey && hasMessage(errKey) ? t(errKey) : fleet.lastErrorCode} (${fleet.lastErrorCode}${fleet.lastErrorMessage ? ': ' + fleet.lastErrorMessage : ''})` : null
  return (
    <div className="amr-drawer-overlay" onClick={onClose}>
      <aside className="amr-drawer" role="dialog" aria-modal="true" aria-label={a.robotCode} onClick={(e) => e.stopPropagation()}>
        <header className="amr-drawer-head">
          <div><strong className="mono">{a.robotCode}</strong> <Pill tone={DATA_TONE[a.state]}>{t(`agv.fleetState_${a.state}` as MsgKey)}</Pill></div>
          <button ref={closeRef} type="button" className="btn btn-secondary btn-sm" onClick={onClose}>{t('agv.drawerClose')}</button>
        </header>
        <div className="amr-drawer-body">
          {a.state !== 'Fresh' && <div className="notice notice-warn" role="note">{a.state === 'Missing' ? t('agv.fMissingNoteOne') : t('agv.fOldNote', { time: formatDateTime(a.lastSeenAt) })}</div>}
          <section>
            <h3>{t('agv.secRobot')}</h3>
            <dl className="kv2">
              <Field label={t('agv.fRobotCode')}><span className="mono">{a.robotCode}</span></Field>
              <Field label={t('agv.cardBusinessAgv')}>{has(a.linkedAgvCode) ? a.linkedAgvCode : nd}</Field>
              <Field label={t('agv.cardMap')}>{has(a.mapCode) ? <span className="mono">{a.mapCode}</span> : nd}</Field>
              <Field label={t('agv.cardSource')}>{fleet.simulatedData ? t('agv.fleetSimulated') : t('agv.fleetReal')}</Field>
              {a.robotIp !== null && <Field label={t('agv.fIp')}>{has(a.robotIp) ? <span className="mono">{a.robotIp}</span> : nd}</Field>}
              <Field label={t('agv.fFirstSeen')}>{formatDateTime(a.firstSeenAt)}</Field>
            </dl>
          </section>
          <section>
            <h3>{t('agv.secStatus')}</h3>
            <dl className="kv2">
              <Field label={t('agv.fRcsStatus')}><StatusPill a={a} /></Field>
              <Field label={t('agv.fRcsCode')}>{c.raw === null ? nd : <span className="mono">{c.raw}</span>}</Field>
              <Field label={t('agv.fCategory')}>{c.category === null ? nd : t(`agv.statusCat_${c.category}` as MsgKey)}</Field>
              <Field label={t('agv.fleetColBattery')}><Battery value={a.battery} /></Field>
              <Field label={t('agv.fleetColExcl')}>{a.exclType === '1' ? <Pill tone="warn" dot={false}>{t('agv.fleetExcluded')}</Pill> : a.exclType === '0' ? t('agv.fleetNo') : nd}</Field>
              <Field label={t('agv.fleetColStop')}>{a.stop === '1' ? <Pill tone="warn" dot={false}>{t('agv.fleetStopped')}</Pill> : a.stop === '0' ? t('agv.fleetNo') : nd}</Field>
            </dl>
          </section>
          <section>
            <h3>{t('agv.secMotion')}</h3>
            <dl className="kv2">
              <Field label={t('agv.fleetColPos')}>{a.posX === null && a.posY === null ? nd : <span className="mono">{has(a.posX) ? a.posX : '–'}, {has(a.posY) ? a.posY : '–'} mm</span>}</Field>
              <Field label={t('agv.fleetColDir')}>{has(a.robotDir) ? `${a.robotDir}°` : nd}</Field>
              <Field label={t('agv.fleetColSpeed')}>{has(a.speed) ? `${a.speed} mm/s` : nd}</Field>
              {a.path.length > 0 && <Field label={t('agv.fPath')}><span className="mono">{a.path.join(' → ')}</span></Field>}
            </dl>
          </section>
          <section>
            <h3>{t('agv.secTask')}</h3>
            <dl className="kv2">
              <Field label={t('agv.fleetColRack')}>{has(a.podCode) ? <><span className="mono">{a.podCode}</span>{has(a.podDir) ? <span className="muted small"> {a.podDir}°</span> : null}</> : nd}</Field>
              <Field label={t('agv.fleetColTask')}>{a.activeTaskId ? <Link to={`/tasks?id=${a.activeTaskId}`} className="mono">{taskCode(a.activeTaskId)}</Link> : nd}</Field>
              <Field label={t('agv.fTaskState')}>{has(a.activeTaskState) ? a.activeTaskState : nd}</Field>
            </dl>
          </section>
          <section>
            <h3>{t('agv.secUpdate')}</h3>
            <dl className="kv2">
              <Field label={t('agv.fLastSeen')}>{formatDateTime(a.lastSeenAt)}</Field>
              <Field label={t('agv.fPollLast')}>{fleet.lastAttemptAt ? formatDateTime(fleet.lastAttemptAt) : nd}</Field>
              <Field label={t('agv.fPollOk')}>{fleet.lastSuccessAt ? formatDateTime(fleet.lastSuccessAt) : nd}</Field>
              <Field label={t('agv.fPollEvery')}>{t('agv.fPollEverySec', { n: fleet.intervalSeconds })}</Field>
              <Field label={t('agv.fStaleAfter')}>{t('agv.fPollEverySec', { n: fleet.staleAfterSeconds })}</Field>
              <Field label={t('agv.fPollError')}>{err ?? t('agv.fPollNone')}</Field>
              <Field label={t('agv.fPollFail')}>{fleet.consecutiveFailures}</Field>
            </dl>
          </section>
        </div>
      </aside>
    </div>
  )
}

function ListView({ amrs, onOpen }: { amrs: FleetAmr[]; onOpen: (code: string) => void }) {
  const t = useT()
  const val = (v: string | null | undefined) => (has(v) ? v : '–')
  return (
    <div className="table-wrap">
      <table className="grid clickable">
        <thead><tr>
          <th>{t('agv.fleetColAmr')}</th><th>{t('agv.fleetColData')}</th><th>{t('agv.fleetColStatus')}</th><th>{t('agv.fleetColBattery')}</th><th>{t('agv.fleetColPos')}</th>
          <th>{t('agv.fleetColDir')}</th><th>{t('agv.fleetColSpeed')}</th><th>{t('agv.fleetColRack')}</th><th>{t('agv.fleetColTask')}</th><th>{t('agv.fleetColSeen')}</th>
        </tr></thead>
        <tbody>
          {amrs.map((a) => (
            <tr key={a.robotCode} tabIndex={0} onClick={() => onOpen(a.robotCode)} onKeyDown={(e) => { if (e.key === 'Enter') onOpen(a.robotCode) }}>
              <td className="mono nowrap"><strong>{a.robotCode}</strong>{has(a.mapCode) ? <span className="muted small"> · {a.mapCode}</span> : null}</td>
              <td><Pill tone={DATA_TONE[a.state]}>{t(`agv.fleetState_${a.state}` as MsgKey)}</Pill></td>
              <td><StatusPill a={a} /></td>
              <td className="nowrap">{batteryPercent(a.battery) !== null ? `${batteryPercent(a.battery)} %` : val(a.battery)}</td>
              <td className="nowrap">{a.posX === null && a.posY === null ? '–' : `${val(a.posX)}, ${val(a.posY)} mm`}</td>
              <td className="nowrap">{has(a.robotDir) ? `${a.robotDir}°` : '–'}</td>
              <td className="nowrap">{has(a.speed) ? `${a.speed} mm/s` : '–'}</td>
              <td className="mono">{val(a.podCode)}</td>
              <td>{a.linkedAgvCode ? <span className="muted small">{a.linkedAgvCode} </span> : null}{a.activeTaskId ? <span className="mono">{taskCode(a.activeTaskId)}</span> : a.linkedAgvCode ? null : '–'}</td>
              <td className="nowrap">{formatDateTime(a.lastSeenAt)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

/**
 * CR-027 / CR-031 – the AMR fleet exactly as the RCS reports it (queryAgvStatus): KPI strip, one card (or table row) per robot, a drawer with the details.
 * Read-only. Old data is shown as old / missing, never as current; a simulated list is labelled as simulated and never replaces real data. A field the RCS did not send reads "No data".
 * The business AGV1/AGV2 cards below stay as they were.
 */
export function FleetPanel({ fleet }: { fleet: FleetView }) {
  const t = useT()
  const [view, setView] = useState<'cards' | 'list'>(readView)
  const [filter, setFilter] = useState<FleetFilter>(NO_FILTER)
  const [openCode, setOpenCode] = useState<string | null>(null)
  const setViewSaved = (v: 'cards' | 'list') => { setView(v); try { localStorage.setItem(VIEW_KEY, v) } catch { /* the choice is only a convenience */ } }

  const kpi = useMemo(() => computeKpis(fleet), [fleet])
  const maps = useMemo(() => distinctMaps(fleet.amrs), [fleet.amrs])
  const shown = useMemo(() => filterAmrs(fleet.amrs, filter), [fleet.amrs, filter])
  const open = openCode ? fleet.amrs.find((a) => a.robotCode === openCode) ?? null : null
  const filtered = filter.text !== '' || filter.category !== '' || filter.map !== '' || filter.data !== ''

  const connTone: Tone = fleet.connection === 'Connected' ? 'ok' : fleet.connection === 'Degraded' ? 'warn' : fleet.connection === 'Disconnected' ? 'bad' : 'muted'
  const errKey = fleet.lastErrorCode ? (`agv.fleetErr_${fleet.lastErrorCode.split(':')[0]}` as MsgKey) : null
  const err = fleet.lastErrorCode ? `${errKey && hasMessage(errKey) ? t(errKey) : fleet.lastErrorCode} (${fleet.lastErrorCode}${fleet.lastErrorMessage ? ': ' + fleet.lastErrorMessage : ''})` : null

  return (
    <Panel title={t('agv.fleetTitle')} subtitle={t('agv.fleetSub', { n: fleet.count, fresh: fleet.freshCount })} className="mb"
      actions={<><Pill tone={fleet.simulatedData ? 'warn' : 'info'} dot={false}>{fleet.simulatedData ? t('agv.fleetSimulated') : t('agv.fleetReal')}</Pill><Pill tone={connTone}>{t(`agv.fleetConn_${fleet.connection}` as MsgKey)}</Pill></>}>
      {fleet.simulatedData && <div className="notice notice-warn" role="note">{t('agv.fleetSimulatedNote')}</div>}
      {fleet.dispatchModePending && <div className="notice notice-warn" role="alert">{t('agv.mgdPending', { mode: fleet.dispatchModePending })}</div>}
      {fleet.dispatchMode === 'RcsManaged' && (
        <div className="notice notice-info" role="note">
          <strong>{t('agv.mgdTitle')}</strong>
          <div className="btn-row">
            <Pill tone="info">{t('agv.mgdInFlight', { n: fleet.tasksInFlight ?? 0 })}</Pill>
            <Pill tone={(fleet.tasksAwaitingAssignment ?? 0) > 0 ? 'warn' : 'muted'}>{t('agv.mgdAwaiting', { n: fleet.tasksAwaitingAssignment ?? 0 })}</Pill>
            <Pill tone="muted">{t('agv.mgdQueued', { n: fleet.tasksQueued ?? 0 })}</Pill>
            {(fleet.maxInFlightTasks ?? 0) > 0 && <Pill tone="muted" dot={false}>{t('agv.mgdLimit', { n: fleet.maxInFlightTasks ?? 0 })}</Pill>}
          </div>
        </div>
      )}
      {fleet.connection === 'Degraded' && <div className="notice notice-warn" role="status">{t('agv.fleetDegraded', { time: formatDateTime(fleet.lastSuccessAt ?? '') })}{err ? ` ${err}` : ''}</div>}
      {fleet.connection === 'Disconnected' && <div className="notice notice-bad" role="alert">{fleet.lastSuccessAt ? t('agv.fleetDisconnected', { time: formatDateTime(fleet.lastSuccessAt) }) : t('agv.fleetDisconnectedNever')}{err ? ` ${err}` : ''}</div>}
      <p className="muted small">
        {fleet.lastSuccessAt ? t('agv.fleetLastOk', { time: formatDateTime(fleet.lastSuccessAt), n: fleet.intervalSeconds }) : t('agv.fleetNever')}
      </p>

      {fleet.amrs.length === 0
        ? <EmptyState title={fleet.connection === 'NoData' ? t('agv.fleetNoData') : fleet.connection === 'Connected' ? t('agv.fleetNone') : t('agv.fleetNoDataFailed')} />
        : (
          <>
            <div className="fleet-kpis">
              <Kpi label={t('agv.kpiTotal')} value={kpi.total} />
              <Kpi label={t('agv.kpiUpdating')} value={kpi.updating} tone="ok" />
              <Kpi label={t('agv.kpiOld')} value={kpi.old} tone={kpi.old > 0 ? 'warn' : undefined} />
              <Kpi label={t('agv.kpiMoving')} value={kpi.moving} tone="info" />
              <Kpi label={t('agv.kpiAbnormal')} value={kpi.abnormal} note={kpi.unknownStatus > 0 ? t('agv.kpiAbnormalNote', { n: kpi.unknownStatus }) : undefined} tone={kpi.abnormal > 0 ? 'bad' : undefined} />
              <Kpi label={t('agv.kpiBattery')} value={kpi.avgBattery === null ? '–' : `${kpi.avgBattery} %`} note={kpi.avgBattery === null ? t('agv.noData') : t('agv.kpiBatteryNote', { n: kpi.batterySamples })} />
            </div>

            <div className="fleet-toolbar">
              <input type="search" value={filter.text} placeholder={t('agv.filterSearch')} aria-label={t('agv.filterSearch')} onChange={(e) => setFilter({ ...filter, text: e.target.value })} />
              <select value={filter.category} aria-label={t('agv.filterStatus')} onChange={(e) => setFilter({ ...filter, category: e.target.value as FleetFilter['category'] })}>
                <option value="">{t('agv.filterStatus')}: {t('agv.filterAll')}</option>
                {CATEGORIES.map((c) => <option key={c} value={c}>{t(`agv.statusCat_${c}` as MsgKey)}</option>)}
                <option value="NoStatus">{t('agv.statusCat_NoStatus')}</option>
              </select>
              <select value={filter.map} aria-label={t('agv.filterMap')} onChange={(e) => setFilter({ ...filter, map: e.target.value })}>
                <option value="">{t('agv.filterMap')}: {t('agv.filterAll')}</option>
                {maps.map((m) => <option key={m} value={m}>{m}</option>)}
              </select>
              <select value={filter.data} aria-label={t('agv.filterData')} onChange={(e) => setFilter({ ...filter, data: e.target.value as FleetFilter['data'] })}>
                <option value="">{t('agv.filterData')}: {t('agv.filterAll')}</option>
                {(['Fresh', 'Stale', 'Missing'] as const).map((s) => <option key={s} value={s}>{t(`agv.fleetState_${s}` as MsgKey)}</option>)}
              </select>
              <span className="muted small fleet-count">{t('agv.filterShown', { shown: shown.length, total: fleet.amrs.length })}</span>
              {filtered && <button type="button" className="btn btn-ghost btn-sm" onClick={() => setFilter(NO_FILTER)}>{t('agv.filterClear')}</button>}
              <span className="fleet-view" role="group">
                <button type="button" className={`btn btn-sm ${view === 'cards' ? 'btn-primary' : 'btn-secondary'}`} aria-pressed={view === 'cards'} onClick={() => setViewSaved('cards')}>{t('agv.viewCards')}</button>
                <button type="button" className={`btn btn-sm ${view === 'list' ? 'btn-primary' : 'btn-secondary'}`} aria-pressed={view === 'list'} onClick={() => setViewSaved('list')}>{t('agv.viewList')}</button>
              </span>
            </div>

            {shown.length === 0
              ? <p className="muted">{t('agv.filterNone')}</p>
              : view === 'cards'
                ? <div className="amr-cards">{shown.map((a) => <AmrCard key={a.robotCode} a={a} fleet={fleet} onOpen={() => setOpenCode(a.robotCode)} />)}</div>
                : <ListView amrs={shown} onOpen={setOpenCode} />}
          </>
        )}
      {open && <AmrDrawer a={open} fleet={fleet} onClose={() => setOpenCode(null)} />}
    </Panel>
  )
}
