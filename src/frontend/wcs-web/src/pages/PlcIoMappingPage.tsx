import { useCallback, useMemo, useState } from 'react'
import { api } from '../api/client'
import type { AddressIssue, IoPointConfig } from '../api/plcTypes'
import { Async, Button, PageHeader, Panel, Pill } from '../components/ui'
import { useApi } from '../hooks/useApi'
import { useT } from '../i18n'
import { localizeFieldError } from '../i18n/logCatalog'
import { CONFIG_STATUS_VI, DATATYPE_VI } from '../i18n/plc'
import { describeError } from '../i18n/vi'
import { AddressDialog, type DialogPoint } from './plc/plcDialogs'
import { WriteApprovalDialog } from './plc/writeDialogs'
import { BLOCKED_SIGNALS, deviceOf, WRITE_TONE } from './plc/plcFormat'
import { ConfigPill } from './plc/plcUi'
import { signalDescription } from '../lib/plcSignals'

const PLCS = ['PLC-01', 'PLC-02', 'PLC-03', 'PLC-04'] as const
const STATIONS: Record<string, string[]> = {
  'PLC-01': ['A1', 'A2', 'A3', 'A4'], 'PLC-02': ['B1', 'B2', 'D1', 'D2'], 'PLC-03': ['X1', 'X2', 'X3', 'X6'], 'PLC-04': ['E1', 'E2', 'G3', 'G7'],
}
const STATUSES = ['NEEDS_CONFIGURATION', 'CONFIGURED', 'APPROVED'] as const

type Dialog = { kind: 'address' | 'writeApproval'; point: IoPointConfig } | null

const asDialogPoint = (p: IoPointConfig): DialogPoint => ({
  id: p.id, plcId: p.plcId, stationId: p.stationId, signalName: p.signalName, dataType: p.dataType, address: p.actualAddress, configStatus: p.configStatus,
})

/**
 * CR-020: configuration ONLY. Reading, watching values and writing I/O are done on the PLC Monitoring screen ("I/O monitoring" tab); the write approval of a point is still decided here.
 * CR-004: IO mapping - the actual PLC address of the 368 IO points (4 PLC x 4 Stations x 23 signals; key PLC_ID + Station_ID + Signal_Name).
 * Admin only (also enforced by the API). Addresses are NEVER generated or derived; a valid address makes the point pollable at once (CR-012);
 * the approval step now only concerns write permission and the port mapping of a process. Supported devices: D, W (word) and M (bit). X/Y are OUT_OF_SCOPE / NEEDS_CONFIRMATION.
 */
export function PlcIoMappingPage() {
  const t = useT()
  const points = useApi(useCallback(() => api.plcPoints({}), []))
  const [plc, setPlc] = useState('')
  const [station, setStation] = useState('')
  const [status, setStatus] = useState('')
  const [search, setSearch] = useState('')
  const [dialog, setDialog] = useState<Dialog>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [issues, setIssues] = useState<AddressIssue[] | null>(null)
  const [checkError, setCheckError] = useState<string | null>(null)

  const rows = useMemo(() => {
    const q = search.trim().toLowerCase()
    return (points.data ?? []).filter((p) =>
      (!plc || p.plcId === plc) && (!station || p.stationId === station) && (!status || p.configStatus === status) && (!q || p.signalName.toLowerCase().includes(q)))
  }, [points.data, plc, station, status, search])

  const all = points.data ?? []
  const counts = { total: all.length, configured: all.filter((p) => p.configStatus !== 'NEEDS_CONFIGURATION').length, pollable: all.filter((p) => p.pollable).length }
  const stationOptions = plc ? STATIONS[plc] ?? [] : Object.values(STATIONS).flat()
  const done = (msg: string) => { setDialog(null); setNotice(msg); setIssues(null); points.reload() }   // the approval is re-read at once: no page reload needed
  const check = async () => {
    setCheckError(null)
    try { setIssues(await api.plcAddressCheck(plc)) } catch (e) { setCheckError(describeError(e)) }
  }

  return (
    <>
      <PageHeader title={t('plc.ioTitle')} />
      {notice && <div className="notice notice-ok" role="status">{notice}</div>}
      <Panel title={t('plc.filters')} subtitle={t('plc.filterSummary', { total: counts.total, configured: counts.configured, pollable: counts.pollable })}>
        <div className="form-grid">
          <label className="field"><span className="field-label">PLC</span>
            <select value={plc} onChange={(e) => { setPlc(e.target.value); setStation(''); setIssues(null) }}>
              <option value="">{t('plc.all')}</option>{PLCS.map((p) => <option key={p} value={p}>{p}</option>)}
            </select></label>
          <label className="field"><span className="field-label">{t('plc.station')}</span>
            <select value={station} onChange={(e) => setStation(e.target.value)}>
              <option value="">{t('plc.all')}</option>{stationOptions.map((s) => <option key={s} value={s}>{s}</option>)}
            </select></label>
          <label className="field"><span className="field-label">{t('plc.configStatus')}</span>
            <select value={status} onChange={(e) => setStatus(e.target.value)}>
              <option value="">{t('plc.all')}</option>{STATUSES.map((s) => <option key={s} value={s}>{CONFIG_STATUS_VI[s] ?? s}</option>)}
            </select></label>
          <label className="field"><span className="field-label">{t('plc.searchSignal')}</span>
            <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t('plc.searchPh')} /></label>
        </div>
        <div className="btn-row">
          <Button disabled={!plc} onClick={() => void check()}>{t('plc.checkConflicts', { plc: plc || t('plc.choosePlc') })}</Button>
          <Button onClick={() => { setPlc(''); setStation(''); setStatus(''); setSearch('') }}>{t('plc.clearFilters')}</Button>
          <span className="muted">{t('plc.showing', { shown: rows.length, total: counts.total })}</span>
        </div>
        {checkError && <div className="notice notice-bad" role="alert">{checkError}</div>}
        {issues && (issues.length === 0
          ? <div className="notice notice-ok">{t('plc.noConflicts', { plc })}</div>
          : <div className="notice notice-bad"><strong>{t('plc.conflicts')}</strong><ul>{issues.map((i) => <li key={i.pointId + i.code}>{i.stationId}/{i.signal}: {localizeFieldError({ field: 'address', message: i.message, code: i.code, args: i.args })}</li>)}</ul></div>)}
      </Panel>

      <Panel title={t('plc.mappingTable')} flush>
        <Async state={points} isEmpty={(d) => d.length === 0} empty={{ title: t('plc.noIo'), hint: t('plc.noIoHint') }}>
          {() => (
            <div className="table-wrap">
              <table className="grid" aria-label={t('plc.mappingTable')}>
                <thead>
                  <tr><th>{t('plc.colPlc')}</th><th>{t('plc.station')}</th><th>{t('plc.colSignalName')}</th><th>{t('plc.colDescription')}</th><th>{t('plc.colDataType')}</th><th>{t('plc.colDevice')}</th><th>{t('plc.colActualAddress')}</th><th>{t('plc.colPollable')}</th><th>{t('plc.colApproval')}</th><th>{t('plc.colWrite')}</th><th>{t('plc.colActions')}</th></tr>
                </thead>
                <tbody>
                  {rows.map((p) => {
                    const inbound = p.direction === 'PlcToSoftware'
                    const blocked = BLOCKED_SIGNALS.includes(p.signalName)
                    return (
                      <tr key={p.id}>
                        <td className="nowrap">{p.plcId}</td>
                        <td className="nowrap"><strong>{p.stationId}</strong></td>
                        <td><strong>{p.signalName}</strong><br /><small className="muted">{p.group}</small></td>
                        <td className="small">{signalDescription(t, p.signalName, p.description)}</td>
                        <td>{DATATYPE_VI[p.dataType]}</td>
                        <td className="mono">{deviceOf(p.actualAddress) ?? <span className="muted">—</span>}</td>
                        <td className="mono">{p.actualAddress ?? <span className="muted">{t('plc.unassigned')}</span>}</td>
                        <td>{p.pollable ? <Pill tone="ok" dot={false}>{t('plc.yes')}</Pill> : <Pill tone="muted" dot={false}>{t('plc.no')}</Pill>}</td>
                        <td><ConfigPill status={p.configStatus} />{p.approvedBy && <small className="muted"> {p.approvedBy}</small>}</td>
                        <td className="nowrap">{inbound || blocked
                          ? <span className="muted" title={t('plc.writeNotApplicableTitle')}>{t('plc.writeNotApplicable')}</span>
                          : <><Pill tone={WRITE_TONE[p.writeApproval] ?? 'warn'} dot={false}>{t(`plc.writeState_${p.writeApproval}` as 'plc.writeState_APPROVED')}</Pill>{p.writeDecidedBy && <small className="muted"> {p.writeDecidedBy}</small>}</>}</td>
                        <td className="nowrap">
                          <Button size="sm" onClick={() => setDialog({ kind: 'address', point: p })}>{t('plc.btnAddress')}</Button>{' '}
                          {!inbound && !blocked && <><Button size="sm" disabled={!p.actualAddress} title={p.actualAddress ? undefined : t('plc.writeNoAddressTitle')} onClick={() => setDialog({ kind: 'writeApproval', point: p })}>{t('plc.btnWriteApproval')}</Button>{' '}</>}
                          {!inbound && blocked && <small className="muted" title={t('plc.writeBlockedTitle')}>{t('plc.writeBlocked')}</small>}
                        </td>
                      </tr>
                    )
                  })}
                  {rows.length === 0 && <tr><td colSpan={11} className="muted">{t('plc.noMatch')}</td></tr>}
                </tbody>
              </table>
            </div>
          )}
        </Async>
      </Panel>
      {dialog?.kind === 'address' && <AddressDialog point={asDialogPoint(dialog.point)} onClose={() => setDialog(null)} onDone={done} />}
      {dialog?.kind === 'writeApproval' && <WriteApprovalDialog point={dialog.point} onClose={() => setDialog(null)} onDone={done} />}
    </>
  )
}
