import { useCallback, useMemo, useState } from 'react'
import { api } from '../api/client'
import type { AddressIssue, IoPointConfig } from '../api/plcTypes'
import { Async, Button, PageHeader, Panel, Pill } from '../components/ui'
import { useApi } from '../hooks/useApi'
import { useT } from '../i18n'
import { localizeFieldError } from '../i18n/logCatalog'
import { CONFIG_STATUS_VI, DATATYPE_VI, DIRECTION_VI } from '../i18n/plc'
import { describeError } from '../i18n/vi'
import { AddressDialog, type DialogPoint } from './plc/plcDialogs'
import { WriteApprovalDialog } from './plc/writeDialogs'
import { BLOCKED_SIGNALS } from './plc/plcFormat'
import { ConfigPill } from './plc/plcUi'
import { signalDescription } from '../lib/plcSignals'
import { plcOptions, readState, stationOptions, writeState } from '../lib/plcIoMap'

const STATUSES = ['NEEDS_CONFIGURATION', 'CONFIGURED', 'APPROVED'] as const

type Dialog = { kind: 'address' | 'writeApproval'; point: IoPointConfig } | null

const asDialogPoint = (p: IoPointConfig): DialogPoint => ({
  id: p.id, plcId: p.plcId, stationId: p.stationId, signalName: p.signalName, dataType: p.dataType, address: p.actualAddress, configStatus: p.configStatus,
})

/**
 * CR-020: configuration ONLY. Reading, watching values and writing I/O are done on the PLC Monitoring screen ("I/O monitoring" tab); the write approval of a point is still decided here.
 * CR-004: IO mapping - the actual PLC address of each IO point (key PLC_ID + Station_ID + Signal_Name). CR-046: PLCs, stations and signals come from the backend (IO map in use); nothing is hard-coded.
 * Admin only (also enforced by the API). Addresses are NEVER generated or derived; a valid address makes the point pollable at once (CR-012);
 * the approval step now only concerns write permission and the port mapping of a process. Supported devices: D, W (word) and M (bit). X/Y are OUT_OF_SCOPE / NEEDS_CONFIRMATION.
 */
export function PlcIoMappingPage() {
  const t = useT()
  const [showRetired, setShowRetired] = useState(false)
  const points = useApi(useCallback(() => api.plcPoints({ includeRetired: showRetired }), [showRetired]))
  const ioMap = useApi(useCallback(() => api.plcIoMap(), []))
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
  const plcs = plcOptions(all)
  const stations = stationOptions(all, plc)
  const clearOld = async (p: IoPointConfig) => {
    setCheckError(null)
    try { await api.plcClearAddress(p.id); done(t('plc.clearOldDone', { signal: `${p.stationId}/${p.signalName}` })) } catch (e) { setCheckError(describeError(e)) }
  }
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
              <option value="">{t('plc.all')}</option>{plcs.map((p) => <option key={p} value={p}>{p}</option>)}
            </select></label>
          <label className="field"><span className="field-label">{t('plc.station')}</span>
            <select value={station} onChange={(e) => setStation(e.target.value)}>
              <option value="">{t('plc.all')}</option>{stations.map((s) => <option key={s} value={s}>{s}</option>)}
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
          <label><input type="checkbox" checked={showRetired} onChange={(e) => { setShowRetired(e.target.checked); setPlc(''); setStation('') }} /> {t('plc.showRetired')}</label>
        </div>
        {checkError && <div className="notice notice-bad" role="alert">{checkError}</div>}
        {issues && (issues.length === 0
          ? <div className="notice notice-ok">{t('plc.noConflicts', { plc })}</div>
          : <div className="notice notice-bad"><strong>{t('plc.conflicts')}</strong><ul>{issues.map((i) => <li key={i.pointId + i.code}>{i.stationId}/{i.signal}: {localizeFieldError({ field: 'address', message: i.message, code: i.code, args: i.args })}</li>)}</ul></div>)}
      </Panel>

      {ioMap.data && (
        <Panel title={t('plc.mapTitle')} subtitle={t('plc.mapSource', { file: ioMap.data.sourceFile, sha: ioMap.data.sourceSha256.slice(0, 12) + '…' })}>
          <div className="notice notice-warn" role="note">{t('plc.mapWarning')}</div>
          {ioMap.data.stationsNotInMap.length > 0 && <div className="notice notice-warn" role="note">{t('plc.mapRetiredStations', { list: ioMap.data.stationsNotInMap.map((s) => `${s.stationId} (${s.plcId})`).join(', ') })}</div>}
          <details>
            <summary>{t('plc.mapPlcSignals')}</summary>
            <div className="table-wrap capped">
              <table className="grid" aria-label={t('plc.mapPlcSignals')}>
                <thead><tr><th>{t('plc.colPlc')}</th><th>{t('plc.colSignalName')}</th><th>{t('plc.colDataType')}</th><th>{t('plc.colIoMapAddress')}</th><th>{t('plc.colDescription')}</th></tr></thead>
                <tbody>
                  {ioMap.data.plcSignals.filter((s) => !plc || s.plcId === plc).map((s) => (
                    <tr key={s.plcId + s.address}><td>{s.plcId}</td><td><strong>{s.signalName}</strong></td><td>{DATATYPE_VI[s.dataType] ?? s.dataType}</td><td className="mono">{s.address}</td><td className="small">{[s.meaning, s.values].filter(Boolean).join(' · ')}</td></tr>
                  ))}
                </tbody>
              </table>
            </div>
          </details>
        </Panel>
      )}

      <Panel title={t('plc.mappingTable')} flush>
        <Async state={points} isEmpty={(d) => d.length === 0} empty={{ title: t('plc.noIo'), hint: t('plc.noIoHint') }}>
          {() => (
            <div className="table-wrap">
              <table className="grid" aria-label={t('plc.mappingTable')}>
                <thead>
                  <tr><th>{t('plc.colPlc')}</th><th>{t('plc.station')}</th><th>{t('plc.colSignalName')}</th><th>{t('plc.colDescription')}</th><th>{t('plc.colDataType')}</th><th>{t('plc.colIoMapAddress')}</th><th>{t('plc.colActualAddress')}</th><th>{t('plc.colRead')}</th><th>{t('plc.colApproval')}</th><th>{t('plc.colWriteState')}</th><th>{t('plc.colActions')}</th></tr>
                </thead>
                <tbody>
                  {rows.map((p) => {
                    const inbound = p.direction === 'PlcToSoftware'
                    const blocked = BLOCKED_SIGNALS.includes(p.signalName)
                    const rs = readState(p)
                    const ws = writeState(p, BLOCKED_SIGNALS)
                    const retired = ws === 'retired'
                    return (
                      <tr key={p.id}>
                        <td className="nowrap">{p.plcId}</td>
                        <td className="nowrap"><strong>{p.stationId}</strong></td>
                        <td><strong>{p.signalName}</strong><br /><small className="muted">{p.group} · {DIRECTION_VI[p.direction]}</small></td>
                        <td className="small">{signalDescription(t, p.signalName, p.description)}</td>
                        <td>{DATATYPE_VI[p.dataType]}</td>
                        <td className="mono">{retired ? <span className="muted">—</span> : p.templateAddressReference || <span className="muted">—</span>}{p.sharedRegisterWith && <><br /><small className="muted">{t('plc.sharedRegister', { signals: p.sharedRegisterWith.join(', ') })}</small></>}</td>
                        <td className="mono">{p.actualAddress ?? <span className="muted">{t('plc.unassigned')}</span>}</td>
                        <td>{retired ? <span className="muted">—</span> : <Pill tone={rs === 'enabled' ? 'ok' : rs === 'blocked' ? 'bad' : 'muted'} dot={false}>{t(rs === 'enabled' ? 'plc.rsEnabled' : rs === 'blocked' ? 'plc.rsBlocked' : 'plc.rsNoAddress')}</Pill>}</td>
                        <td><ConfigPill status={p.configStatus} />{p.approvedBy && <small className="muted"> {p.approvedBy}</small>}</td>
                        <td className="nowrap">
                          {ws === 'input' || ws === 'retired' ? <span className="muted">{t(ws === 'input' ? 'plc.wsInput' : 'plc.wsRetired')}</span>
                            : <><Pill tone={ws === 'approved' ? 'ok' : 'warn'} dot={false}>{t(ws === 'approved' ? 'plc.wsApproved' : ws === 'notConfirmed' ? 'plc.wsNotConfirmed' : 'plc.wsProtected')}</Pill>
                              {ws === 'protected' && !blocked && <small className="muted"> {t(`plc.writeState_${p.writeApproval}` as 'plc.writeState_APPROVED')}</small>}{p.writeDecidedBy && <small className="muted"> {p.writeDecidedBy}</small>}</>}
                        </td>
                        <td className="nowrap">
                          {retired ? <Button size="sm" disabled={!p.actualAddress} onClick={() => void clearOld(p)}>{t('plc.btnClearOld')}</Button> : <>
                            <Button size="sm" onClick={() => setDialog({ kind: 'address', point: p })}>{t('plc.btnAddress')}</Button>{' '}
                            {(ws === 'protected' || ws === 'approved') && !blocked && <Button size="sm" disabled={!p.actualAddress} title={p.actualAddress ? undefined : t('plc.writeNoAddressTitle')} onClick={() => setDialog({ kind: 'writeApproval', point: p })}>{t('plc.btnWriteApproval')}</Button>}
                            {!inbound && blocked && <small className="muted" title={t('plc.writeBlockedTitle')}>{t('plc.writeBlocked')}</small>}
                          </>}
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
