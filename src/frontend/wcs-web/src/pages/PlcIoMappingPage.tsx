import { useCallback, useMemo, useState } from 'react'
import { api } from '../api/client'
import type { AddressIssue, IoPointConfig } from '../api/plcTypes'
import { Async, Button, PageHeader, Panel, Pill } from '../components/ui'
import { ENGINEERING_TOOLS } from '../config'
import { useApi } from '../hooks/useApi'
import { useT } from '../i18n'
import { localizeFieldError } from '../i18n/logCatalog'
import { CONFIG_STATUS_VI, DATATYPE_VI } from '../i18n/plc'
import { describeError } from '../i18n/vi'
import { AddressDialog, SimDialog, type DialogPoint } from './plc/plcDialogs'
import { TypedWriteDialog, WriteApprovalDialog } from './plc/writeDialogs'
import { BLOCKED_SIGNALS, deviceOf, WRITE_TONE } from './plc/plcFormat'
import { runtimeAllowsWrite, writeKeyChecklist, writeReadiness, type WriteBlock } from '../lib/plcWrite'
import { ConfigPill } from './plc/plcUi'

const PLCS = ['PLC-01', 'PLC-02', 'PLC-03', 'PLC-04'] as const
const STATIONS: Record<string, string[]> = {
  'PLC-01': ['A1', 'A2', 'A3', 'A4'], 'PLC-02': ['B1', 'B2', 'D1', 'D2'], 'PLC-03': ['X1', 'X2', 'X3', 'X6'], 'PLC-04': ['E1', 'E2', 'G3', 'G7'],
}
const STATUSES = ['NEEDS_CONFIGURATION', 'CONFIGURED', 'APPROVED'] as const

type Dialog = { kind: 'address' | 'write' | 'sim' | 'writeApproval'; point: IoPointConfig } | null

const asDialogPoint = (p: IoPointConfig): DialogPoint => ({
  id: p.id, plcId: p.plcId, stationId: p.stationId, signalName: p.signalName, dataType: p.dataType, address: p.actualAddress, configStatus: p.configStatus,
})

/**
 * CR-004: IO mapping - the actual PLC address of the 368 IO points (4 PLC x 4 Stations x 23 signals; key PLC_ID + Station_ID + Signal_Name).
 * Admin only (also enforced by the API). Addresses are NEVER generated or derived; a valid address makes the point pollable at once (CR-012);
 * the approval step now only concerns write permission and the port mapping of a process. Supported devices: D, W (word) and M (bit). X/Y are OUT_OF_SCOPE / NEEDS_CONFIRMATION.
 */
export function PlcIoMappingPage() {
  const t = useT()
  const points = useApi(useCallback(() => api.plcPoints({}), []))
  const overview = useApi(useCallback(() => api.plcOverview(), []))
  const [plc, setPlc] = useState('')
  const [station, setStation] = useState('')
  const [status, setStatus] = useState('')
  const [search, setSearch] = useState('')
  const [dialog, setDialog] = useState<Dialog>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [issues, setIssues] = useState<AddressIssue[] | null>(null)
  const [checkError, setCheckError] = useState<string | null>(null)

  const runtime = overview.data?.runtime ?? null            // CR-016: never assume a write mode while the runtime state is unknown
  const mode = runtime?.effectiveMode ?? 'Simulator'
  const simulator = runtime?.effectiveMode === 'Simulator'
  const plcState = (id: string) => overview.data?.plcs.find((w) => w.plcId === id)?.state
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
  // CR-015/016: the write action is part of every build and is ALWAYS shown for a writable point: enabled when everything the backend will check is met, otherwise disabled with the
  // concrete reason beside it (runtime read-only and which key is missing, no write approval, PLC not connected ...) - never silently hidden.
  const writeOk = runtimeAllowsWrite(runtime)

  const rows = useMemo(() => {
    const q = search.trim().toLowerCase()
    return (points.data ?? []).filter((p) =>
      (!plc || p.plcId === plc) && (!station || p.stationId === station) && (!status || p.configStatus === status) && (!q || p.signalName.toLowerCase().includes(q)))
  }, [points.data, plc, station, status, search])

  const all = points.data ?? []
  const counts = { total: all.length, configured: all.filter((p) => p.configStatus !== 'NEEDS_CONFIGURATION').length, pollable: all.filter((p) => p.pollable).length }
  const stationOptions = plc ? STATIONS[plc] ?? [] : Object.values(STATIONS).flat()
  const done = (msg: string) => { setDialog(null); setNotice(msg); setIssues(null); points.reload(); overview.reload() }   // the approval and the runtime state are re-read at once: no page reload needed
  const check = async () => {
    setCheckError(null)
    try { setIssues(await api.plcAddressCheck(plc)) } catch (e) { setCheckError(describeError(e)) }
  }

  return (
    <>
      <PageHeader title={t('plc.ioTitle')} />
      {notice && <div className="notice notice-ok" role="status">{notice}</div>}
      <div className={`notice ${!runtime ? 'notice-info' : writeOk ? 'notice-ok' : 'notice-warn'}`} role="status" aria-label={t('plc.writeRtTitle')}>
        <strong>{t('plc.writeRtTitle')}: </strong>
        {!runtime ? t('plc.writeRtLoading') : writeOk ? t('plc.writeRtOk', { mode }) : (runtime.missingWriteKeys?.length ? t('plc.writeRtReadOnly', { list: runtime.missingWriteKeys.join(', ') }) : t('plc.writeRtReadOnlyNoList'))}
        {runtime && !simulator && (
          <div className="btn-row" style={{ marginTop: 8 }} aria-label={t('plc.writeKeysTitle')}>
            {writeKeyChecklist(runtime).map((k) => (
              <Pill key={k.key} tone={k.ok ? 'ok' : 'bad'} dot={false}>{k.ok ? '✓' : '✗'} {k.key}{k.current ? ' = ' + k.current : ''}</Pill>
            ))}
          </div>
        )}
      </div>
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
                    const gate = writeReadiness(runtime, plcState(p.plcId), p)
                    const why = reasonText(gate, p.plcId)
                    return (
                      <tr key={p.id}>
                        <td className="nowrap">{p.plcId}</td>
                        <td className="nowrap"><strong>{p.stationId}</strong></td>
                        <td><strong>{p.signalName}</strong><br /><small className="muted">{p.group}</small></td>
                        <td className="small">{p.description}</td>
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
                          {ENGINEERING_TOOLS && simulator && <><Button size="sm" onClick={() => setDialog({ kind: 'sim', point: p })}>{t('plc.btnSim')}</Button>{' '}</>}
                          {!inbound && !blocked && <Button size="sm" variant="danger" disabled={!gate.ok} title={gate.ok ? undefined : why} onClick={() => setDialog({ kind: 'write', point: p })}>{t('plc.btnWrite')}</Button>}
                          {!inbound && !blocked && !gate.ok && <><br /><small className="muted">{why}</small></>}
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
      {dialog?.kind === 'write' && <TypedWriteDialog point={dialog.point} simulator={simulator} onClose={() => setDialog(null)} onDone={done} />}
      {dialog?.kind === 'writeApproval' && <WriteApprovalDialog point={dialog.point} onClose={() => setDialog(null)} onDone={done} />}
      {dialog?.kind === 'sim' && <SimDialog point={asDialogPoint(dialog.point)} onClose={() => setDialog(null)} onDone={done} />}
    </>
  )
}
