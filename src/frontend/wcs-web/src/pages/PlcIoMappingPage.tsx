import { useCallback, useMemo, useState } from 'react'
import { api } from '../api/client'
import type { AddressIssue, IoPointConfig } from '../api/plcTypes'
import { Button, PageHeader, Panel, Pill } from '../components/ui'
import { useApi } from '../hooks/useApi'
import { useT } from '../i18n'
import { localizeFieldError } from '../i18n/logCatalog'
import { CONFIG_STATUS_VI } from '../i18n/plc'
import { describeError } from '../i18n/vi'
import { AddressDialog, type DialogPoint } from './plc/plcDialogs'
import { WriteApprovalDialog } from './plc/writeDialogs'
import { BLOCKED_SIGNALS } from './plc/plcFormat'
import { ConfigPill } from './plc/plcUi'
import { IoMapSequence, IoMapSheetTable } from './plc/IoMapSheet'
import { filterSheetRows, plcOptions, readState, sheetRows, stationOptions, writeState } from '../lib/plcIoMap'

const STATUSES = ['NEEDS_CONFIGURATION', 'CONFIGURED', 'APPROVED'] as const

type Dialog = { kind: 'address' | 'writeApproval'; point: IoPointConfig } | null

const asDialogPoint = (p: IoPointConfig): DialogPoint => ({
  id: p.id, plcId: p.plcId, stationId: p.stationId, signalName: p.signalName, dataType: p.dataType, address: p.actualAddress, configStatus: p.configStatus,
})

/** Addresses still entered on rows of an earlier map: closed by default; their only action is to clear them (they would otherwise keep the address occupied). */
function LeftoverAddresses({ count, onCleared }: { count: number; onCleared: (message: string) => void }) {
  const t = useT()
  const [open, setOpen] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const rows = useApi(useCallback(() => (open ? api.plcPoints({ includeRetired: true }) : Promise.resolve([] as IoPointConfig[])), [open]))
  const left = (rows.data ?? []).filter((p) => p.inIoMap === false && p.actualAddress)
  const clear = async (p: IoPointConfig) => {
    setError(null)
    try { await api.plcClearAddress(p.id); rows.reload(); onCleared(t('plc.clearOldDone', { signal: `${p.stationId}/${p.signalName}` })) } catch (e) { setError(describeError(e)) }
  }
  return (
    <details className="card pad" onToggle={(e) => setOpen(e.currentTarget.open)}>
      <summary>{t('plc.leftoverTitle', { n: count })}</summary>
      <p className="muted small">{t('plc.leftoverHint')}</p>
      {error && <div className="notice notice-bad" role="alert">{error}</div>}
      <div className="table-wrap capped">
        <table className="grid">
          <thead><tr><th>{t('plc.colPlc')}</th><th>{t('plc.station')}</th><th>{t('plc.colSignalName')}</th><th>{t('plc.colActualAddress')}</th><th /></tr></thead>
          <tbody>{left.map((p) => <tr key={p.id}><td>{p.plcId}</td><td>{p.stationId}</td><td>{p.signalName}</td><td className="mono">{p.actualAddress}</td><td><Button size="sm" onClick={() => void clear(p)}>{t('plc.btnClearOld')}</Button></td></tr>)}</tbody>
        </table>
      </div>
    </details>
  )
}

/**
 * CR-020: configuration ONLY. Reading, watching values and writing I/O are done on the PLC Monitoring screen ("I/O monitoring" tab); the write approval of a point is still decided here.
 * CR-004: IO mapping - the actual PLC address of each IO point (key PLC_ID + Station_ID + Signal_Name). CR-046: PLCs, stations and signals come from the backend (IO map in use); nothing is hard-coded.
 * Admin only (also enforced by the API). Addresses are NEVER generated or derived; a valid address makes the point pollable at once (CR-012);
 * the approval step now only concerns write permission and the port mapping of a process. Supported devices: D, W (word) and M (bit). X/Y are OUT_OF_SCOPE / NEEDS_CONFIRMATION.
 */
export function PlcIoMappingPage() {
  const t = useT()
  const points = useApi(useCallback(() => api.plcPoints({}), []))
  const ioMap = useApi(useCallback(() => api.plcIoMap(), []))
  const [plc, setPlc] = useState('')
  const [station, setStation] = useState('')
  const [status, setStatus] = useState('')
  const [search, setSearch] = useState('')
  const [dialog, setDialog] = useState<Dialog>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [issues, setIssues] = useState<AddressIssue[] | null>(null)
  const [checkError, setCheckError] = useState<string | null>(null)

  const all = points.data ?? []
  const counts = { total: all.length, configured: all.filter((p) => p.configStatus !== 'NEEDS_CONFIGURATION').length, pollable: all.filter((p) => p.pollable).length }
  const pointOf = useMemo(() => new Map((points.data ?? []).map((p) => [`${p.plcId}|${p.stationId}|${p.signalName}`, p])), [points.data])
  const mapRows = useMemo(() => (ioMap.data ? sheetRows(ioMap.data) : []), [ioMap.data])
  const rows = useMemo(() => filterSheetRows(mapRows, { plc, station, text: search },
    (r) => !status || pointOf.get(`${r.plcId}|${r.stationId}|${r.signalName}`)?.configStatus === status), [mapRows, plc, station, search, status, pointOf])
  const plcs = plcOptions(all)
  const stations = stationOptions(all, plc)
  const done = (msg: string) => { setDialog(null); setNotice(msg); setIssues(null); points.reload(); ioMap.reload() }   // the approval is re-read at once: no page reload needed
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
          <span className="muted">{t('plc.showing', { shown: rows.length, total: mapRows.length })}</span>
        </div>
        {checkError && <div className="notice notice-bad" role="alert">{checkError}</div>}
        {issues && (issues.length === 0
          ? <div className="notice notice-ok">{t('plc.noConflicts', { plc })}</div>
          : <div className="notice notice-bad"><strong>{t('plc.conflicts')}</strong><ul>{issues.map((i) => <li key={i.pointId + i.code}>{i.stationId}/{i.signal}: {localizeFieldError({ field: 'address', message: i.message, code: i.code, args: i.args })}</li>)}</ul></div>)}
      </Panel>

      {ioMap.error && <div className="notice notice-bad" role="alert">{t('plc.mapUnavailable')} {ioMap.error}</div>}
      {points.error && <div className="notice notice-bad" role="alert">{points.error}</div>}
      {points.data?.length === 0 && <div className="notice notice-warn" role="status">{t('plc.noIo')}. {t('plc.noIoHint')}</div>}
      {ioMap.data?.sheet && (
        <Panel title={t('plc.mapTitle')} subtitle={t('plc.mapSource', { file: ioMap.data.sourceFile, sheet: ioMap.data.sheet.name, sha: ioMap.data.sourceSha256.slice(0, 12) + '…' })} flush>
          <div className="notice notice-warn" role="note">{t('plc.mapWarning')}</div>
          <IoMapSheetTable sheet={ioMap.data.sheet} rows={rows}
            systemHeaders={[t('plc.colActualAddress'), t('plc.colRead'), t('plc.colApproval'), t('plc.colWriteState'), t('plc.colActions')]}
            system={(r) => {
              const p = pointOf.get(`${r.plcId}|${r.stationId}|${r.signalName}`)
              if (!p) return <td colSpan={5} className="iomap-sys muted">{t('plc.mapNotAPoint')}</td>
              const inbound = p.direction === 'PlcToSoftware'
              const blocked = BLOCKED_SIGNALS.includes(p.signalName)
              const rs = readState(p)
              const ws = writeState(p, BLOCKED_SIGNALS)
              return (
                <>
                  <td className="iomap-sys mono">{p.actualAddress ?? <span className="muted">{t('plc.unassigned')}</span>}</td>
                  <td className="iomap-sys"><Pill tone={rs === 'enabled' ? 'ok' : rs === 'blocked' ? 'bad' : 'muted'} dot={false}>{t(rs === 'enabled' ? 'plc.rsEnabled' : rs === 'blocked' ? 'plc.rsBlocked' : 'plc.rsNoAddress')}</Pill></td>
                  <td className="iomap-sys"><ConfigPill status={p.configStatus} />{p.approvedBy && <small className="muted"> {p.approvedBy}</small>}</td>
                  <td className="iomap-sys">
                    {ws === 'input' ? <span className="muted">{t('plc.wsInput')}</span>
                      : <Pill tone={ws === 'approved' ? 'ok' : 'warn'} dot={false}>{t(ws === 'approved' ? 'plc.wsApproved' : ws === 'notConfirmed' ? 'plc.wsNotConfirmed' : 'plc.wsProtected')}</Pill>}
                  </td>
                  <td className="iomap-sys iomap-actions">
                    <Button size="sm" onClick={() => setDialog({ kind: 'address', point: p })}>{t('plc.btnAddress')}</Button>{' '}
                    {(ws === 'protected' || ws === 'approved') && !blocked && <Button size="sm" disabled={!p.actualAddress} title={p.actualAddress ? undefined : t('plc.writeNoAddressTitle')} onClick={() => setDialog({ kind: 'writeApproval', point: p })}>{t('plc.btnWriteApproval')}</Button>}
                    {!inbound && blocked && <small className="muted" title={t('plc.writeBlockedTitle')}>{t('plc.writeBlocked')}</small>}
                  </td>
                </>
              )
            }} />
        </Panel>
      )}
      {ioMap.data?.sequence && (
        <Panel title={t('plc.mapSequence', { sheet: ioMap.data.sequence.name })} flush><IoMapSequence sequence={ioMap.data.sequence} /></Panel>
      )}
      {(ioMap.data?.leftoverAddresses ?? 0) > 0 && <LeftoverAddresses count={ioMap.data!.leftoverAddresses} onCleared={done} />}
      {dialog?.kind === 'address' && <AddressDialog point={asDialogPoint(dialog.point)} onClose={() => setDialog(null)} onDone={done} />}
      {dialog?.kind === 'writeApproval' && <WriteApprovalDialog point={dialog.point} onClose={() => setDialog(null)} onDone={done} />}
    </>
  )
}
