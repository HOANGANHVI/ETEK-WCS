import { useCallback, useEffect, useMemo, useState } from 'react'
import { api } from '../../api/client'
import type { MonitorPoint, PlcOverview } from '../../api/plcTypes'
import { Async, Button, Panel, Pill } from '../../components/ui'
import { ENGINEERING_TOOLS } from '../../config'
import { useApi } from '../../hooks/useApi'
import { useT } from '../../i18n'
import { DATATYPE_VI, MONITOR_STATUS_TONE, MONITOR_STATUS_VI } from '../../i18n/plc'
import { describeError } from '../../i18n/vi'
import { filterPoints, MONITOR_STATUSES, readReadiness, rowWrite, sortPoints, type MonitorFilter, type SortKey } from '../../lib/plcMonitor'
import { runtimeAllowsWrite, writeKeyChecklist, type WriteBlock } from '../../lib/plcWrite'
import { fmtTime, WRITE_TONE } from './plcFormat'
import { SimDialog } from './plcDialogs'
import { TypedWriteDialog } from './writeDialogs'

const REFRESH_SECONDS = 5
const EVENTS = ['StationIoChanged', 'PlcStatusChanged'] as const

type Dialog = { kind: 'write' | 'sim'; point: MonitorPoint } | null

const without = <V,>(m: Record<string, V>, id: string): Record<string, V> => { const next = { ...m }; delete next[id]; return next }

function ValueView({ p }: { p: MonitorPoint }) {
  const t = useT()
  if (p.value === null || p.value === undefined) return <span className="muted">—</span>
  if (typeof p.value === 'boolean') return <Pill tone={p.value ? 'ok' : 'muted'} dot={false}>{p.value ? t('plc.on') : t('plc.off')}</Pill>
  if (typeof p.value === 'string') return <span className="mono">“{p.value}”</span>
  return <span className="mono num">{p.value}</span>
}

/**
 * CR-020: the single place to watch and operate I/O. Every row is a real IO point of one of the PLCs with the value its worker polled (or an immediate read just made), the communication status and
 * the time of the last good read. Read goes through the PLC's own worker (no extra connection); Write (Admin only) is the CR-019 dialog and API unchanged - this screen adds no guard and removes none.
 * The list is re-read from the backend (data already held; no PLC traffic) on every IO/PLC event and every few seconds, so a value that stops refreshing turns Stale by itself.
 */
export function PlcIoMonitor({ isAdmin, overview }: { isAdmin: boolean; overview: PlcOverview }) {
  const t = useT()
  const list = useApi(useCallback(() => api.plcMonitorPoints({}), []), [...EVENTS])
  const [filter, setFilter] = useState<MonitorFilter>({ plc: '', station: '', status: '', text: '' })
  const [sort, setSort] = useState<{ key: SortKey | null; dir: 'asc' | 'desc' }>({ key: null, dir: 'asc' })
  const [pending, setPending] = useState<Record<string, true>>({})
  const [readError, setReadError] = useState<Record<string, string>>({})
  const [dialog, setDialog] = useState<Dialog>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const { reload } = list

  useEffect(() => { const id = setInterval(reload, REFRESH_SECONDS * 1000); return () => clearInterval(id) }, [reload])

  const runtime = overview.runtime
  const simulator = runtime.effectiveMode === 'Simulator'
  const writeOk = runtimeAllowsWrite(runtime)
  const plcState = (id: string) => overview.plcs.find((w) => w.plcId === id)?.state

  const all = useMemo(() => list.data?.points ?? [], [list.data])
  const plcs = useMemo(() => [...new Set(all.map((p) => p.plcId))].sort(), [all])
  const stations = useMemo(() => [...new Set(all.filter((p) => !filter.plc || p.plcId === filter.plc).map((p) => p.stationId))].sort(), [all, filter.plc])
  const rows = useMemo(() => sortPoints(filterPoints(all, filter), sort.key, sort.dir), [all, filter, sort])
  const counts = useMemo(() => Object.fromEntries(MONITOR_STATUSES.map((s) => [s, all.filter((p) => p.status === s).length])) as Record<string, number>, [all])

  const reasonText = (b: WriteBlock, plcId: string): string => {
    if (b.ok) return ''
    switch (b.reason) {
      case 'RUNTIME_UNKNOWN': return t('plc.wrLoading')
      case 'READ_ONLY': return t('plc.wrReadOnly', { keys: b.missing && b.missing.length ? t('plc.wrReadOnlyKeys', { list: b.missing.join(', ') }) : '' })
      case 'NO_ADDRESS': return t('plc.wrNoAddress')
      case 'UNSUPPORTED': return t('plc.wrUnsupported')
      case 'NOT_APPROVED': return t('plc.wrNotApproved')
      case 'REJECTED': return t('plc.wrRejected')
      default: return t('plc.wrNotConnected', { plc: plcId })
    }
  }

  // One immediate read per point at a time (the button is disabled while it runs; the backend also shares concurrent reads). A failed read is shown with its cause; the list then reflects the new status.
  const read = async (p: MonitorPoint) => {
    setPending((x) => ({ ...x, [p.id]: true })); setReadError((x) => without(x, p.id))
    try {
      const r = await api.plcReadNow(p.stationId, p.signalName)
      if (!r.success) setReadError((x) => ({ ...x, [p.id]: t('plc.monReadFailed', { code: r.code ?? '', message: r.message ?? '' }) }))
    } catch (e) { setReadError((x) => ({ ...x, [p.id]: describeError(e) })) }
    finally { setPending((x) => without(x, p.id)); reload() }
  }
  const sortBy = (key: SortKey) => setSort((s) => (s.key === key ? { key, dir: s.dir === 'asc' ? 'desc' : 'asc' } : { key, dir: 'asc' }))
  const th = (key: SortKey, label: string) => (
    <th aria-sort={sort.key === key ? (sort.dir === 'asc' ? 'ascending' : 'descending') : 'none'}>
      <button type="button" className="th-sort" title={t('plc.monSortBy', { col: label })} onClick={() => sortBy(key)}>{label}{sort.key === key ? (sort.dir === 'asc' ? ' ▲' : ' ▼') : ''}</button>
    </th>
  )
  const done = (msg: string) => { setDialog(null); setNotice(msg); reload() }
  const asWritePoint = (p: MonitorPoint) => ({ plcId: p.plcId, stationId: p.stationId, signalName: p.signalName, actualAddress: p.address, dataType: p.dataType, wordLength: p.wordLength, encoding: p.encoding, writeApproval: p.writeApproval ?? 'NOT_APPROVED' as const, writeSupported: p.writeSupported })

  return (
    <>
      {notice && <div className="notice notice-ok" role="status">{notice}</div>}
      {isAdmin ? (
        <div className={`notice ${writeOk ? 'notice-ok' : 'notice-warn'}`} role="status" aria-label={t('plc.writeRtTitle')}>
          <strong>{t('plc.writeRtTitle')}: </strong>
          {writeOk ? t('plc.writeRtOk', { mode: runtime.effectiveMode }) : (runtime.missingWriteKeys?.length ? t('plc.writeRtReadOnly', { list: runtime.missingWriteKeys.join(', ') }) : t('plc.writeRtReadOnlyNoList'))}
          {!simulator && (
            <div className="btn-row" style={{ marginTop: 8 }} aria-label={t('plc.writeKeysTitle')}>
              {writeKeyChecklist(runtime).map((k) => <Pill key={k.key} tone={k.ok ? 'ok' : 'bad'} dot={false}>{k.ok ? '✓' : '✗'} {k.key}{k.current ? ' = ' + k.current : ''}</Pill>)}
            </div>
          )}
        </div>
      ) : <div className="notice notice-info">{t('plc.monViewOnly')}</div>}

      <Panel title={t('plc.monTitle')} subtitle={t('plc.monHint', { s: list.data?.staleAfterSeconds ?? '—' })}>
        <div className="form-grid">
          <label className="field"><span className="field-label">PLC</span>
            <select value={filter.plc} onChange={(e) => setFilter({ ...filter, plc: e.target.value, station: '' })}>
              <option value="">{t('plc.all')}</option>{plcs.map((p) => <option key={p} value={p}>{p}</option>)}
            </select></label>
          <label className="field"><span className="field-label">{t('plc.station')}</span>
            <select value={filter.station} onChange={(e) => setFilter({ ...filter, station: e.target.value })}>
              <option value="">{t('plc.all')}</option>{stations.map((s) => <option key={s} value={s}>{s}</option>)}
            </select></label>
          <label className="field"><span className="field-label">{t('plc.monFilterStatus')}</span>
            <select value={filter.status} onChange={(e) => setFilter({ ...filter, status: e.target.value })}>
              <option value="">{t('plc.all')}</option>{MONITOR_STATUSES.map((s) => <option key={s} value={s}>{MONITOR_STATUS_VI[s]} ({counts[s] ?? 0})</option>)}
            </select></label>
          <label className="field"><span className="field-label">{t('plc.searchSignal')}</span>
            <input value={filter.text} onChange={(e) => setFilter({ ...filter, text: e.target.value })} placeholder={t('plc.searchPh')} /></label>
        </div>
        <div className="btn-row">
          <Button onClick={() => { setFilter({ plc: '', station: '', status: '', text: '' }); setSort({ key: null, dir: 'asc' }) }}>{t('plc.clearFilters')}</Button>
          <span className="muted">{t('plc.showing', { shown: rows.length, total: all.length })}</span>
          <span className="muted small">{t('plc.monAutoRefresh', { s: REFRESH_SECONDS })}</span>
        </div>
      </Panel>

      <Panel title={t('plc.tabPoints')} flush>
        <Async state={list} isEmpty={(d) => d.points.length === 0} empty={{ title: t('plc.monNoData') }}>
          {() => (
            <div className="table-wrap">
              <table className="grid" aria-label={t('plc.tabPoints')}>
                <thead>
                  <tr>
                    {th('plc', t('plc.colPlc'))}{th('station', t('plc.station'))}{th('signal', t('plc.colSignalName'))}<th>{t('plc.colDescription')}</th>
                    {isAdmin && <th>{t('plc.colActualAddress')}</th>}<th>{t('plc.colDataType')}</th><th>{t('plc.monColValue')}</th>{th('status', t('plc.monColStatus'))}{th('updated', t('plc.colUpdated'))}
                    {isAdmin && <th>{t('plc.colWrite')}</th>}<th>{t('plc.colActions')}</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((p) => {
                    const busy = pending[p.id] === true
                    const rb = readReadiness(p, busy)
                    const w = rowWrite(isAdmin, runtime, plcState(p.plcId), p)
                    return (
                      <tr key={p.id}>
                        <td className="nowrap">{p.plcId}</td>
                        <td className="nowrap"><strong>{p.stationId}</strong></td>
                        <td><strong>{p.signalName}</strong><br /><small className="muted">{p.group}</small></td>
                        <td className="small">{p.description}</td>
                        {isAdmin && <td className="mono">{p.address ?? <span className="muted">—</span>}</td>}
                        <td>{DATATYPE_VI[p.dataType]}</td>
                        <td><ValueView p={p} /></td>
                        <td><Pill tone={MONITOR_STATUS_TONE[p.status] ?? 'muted'} dot={false}>{MONITOR_STATUS_VI[p.status] ?? p.status}</Pill>{isAdmin && p.error && <small className="text-bad" title={p.error}> ⚠</small>}</td>
                        <td className="nowrap num">{fmtTime(p.lastUpdated)}</td>
                        {isAdmin && <td className="nowrap">{w.kind === 'inbound' || w.kind === 'blocked'
                          ? <span className="muted" title={t('plc.writeNotApplicableTitle')}>{t('plc.writeNotApplicable')}</span>
                          : <Pill tone={WRITE_TONE[p.writeApproval ?? 'NOT_APPROVED'] ?? 'warn'} dot={false}>{t(`plc.writeState_${p.writeApproval ?? 'NOT_APPROVED'}` as 'plc.writeState_APPROVED')}</Pill>}</td>}
                        <td className="nowrap">
                          <Button size="sm" disabled={!rb.ok} aria-busy={busy} title={rb.ok ? undefined : rb.reason === 'NO_ADDRESS' ? t('plc.wrNoAddress') : t('plc.monReading')} onClick={() => void read(p)}>{busy ? t('plc.monReading') : t('plc.monRead')}</Button>{' '}
                          {w.kind === 'action' && <Button size="sm" variant="danger" disabled={!w.gate.ok} title={w.gate.ok ? undefined : reasonText(w.gate, p.plcId)} onClick={() => setDialog({ kind: 'write', point: p })}>{t('plc.btnWrite')}</Button>}
                          {isAdmin && ENGINEERING_TOOLS && simulator && <>{' '}<Button size="sm" onClick={() => setDialog({ kind: 'sim', point: p })}>{t('plc.btnSim')}</Button></>}
                          {w.kind === 'action' && !w.gate.ok && <><br /><small className="muted">{reasonText(w.gate, p.plcId)}</small></>}
                          {w.kind === 'blocked' && <small className="muted" title={t('plc.writeBlockedTitle')}>{t('plc.writeBlocked')}</small>}
                          {readError[p.id] && <><br /><small className="text-bad" role="alert">{readError[p.id]}</small></>}
                        </td>
                      </tr>
                    )
                  })}
                  {rows.length === 0 && <tr><td colSpan={isAdmin ? 11 : 9} className="muted">{t('plc.noMatch')}</td></tr>}
                </tbody>
              </table>
            </div>
          )}
        </Async>
      </Panel>
      {isAdmin && <p className="muted small">{t('plc.monWriteApprovalHint')}</p>}
      {dialog?.kind === 'write' && <TypedWriteDialog point={asWritePoint(dialog.point)} simulator={simulator} onClose={() => setDialog(null)} onDone={done} />}
      {dialog?.kind === 'sim' && <SimDialog point={{ id: dialog.point.id, plcId: dialog.point.plcId, stationId: dialog.point.stationId, signalName: dialog.point.signalName, dataType: dialog.point.dataType, address: dialog.point.address, configStatus: '' }} onClose={() => setDialog(null)} onDone={done} />}
    </>
  )
}
