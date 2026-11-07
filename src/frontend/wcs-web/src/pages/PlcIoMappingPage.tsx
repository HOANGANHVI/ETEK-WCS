import { useCallback, useMemo, useState } from 'react'
import { api } from '../api/client'
import type { AddressIssue, IoPointConfig } from '../api/plcTypes'
import { Async, Button, PageHeader, Panel, Pill } from '../components/ui'
import { useApi } from '../hooks/useApi'
import { CONFIG_STATUS_VI, DATATYPE_VI } from '../i18n/plc'
import { describeError } from '../i18n/vi'
import { AddressDialog, SimDialog, WriteDialog, type DialogPoint } from './plc/plcDialogs'
import { BLOCKED_SIGNALS, deviceOf } from './plc/plcFormat'
import { ConfigPill } from './plc/plcUi'

const PLCS = ['PLC-01', 'PLC-02', 'PLC-03', 'PLC-04'] as const
const STATIONS: Record<string, string[]> = {
  'PLC-01': ['A1', 'A2', 'A3', 'A4'], 'PLC-02': ['B1', 'B2', 'D1', 'D2'], 'PLC-03': ['X1', 'X2', 'X3', 'X6'], 'PLC-04': ['E1', 'E2', 'G3', 'G7'],
}
const STATUSES = ['NEEDS_CONFIGURATION', 'CONFIGURED', 'APPROVED'] as const

type Dialog = { kind: 'address' | 'write' | 'sim'; point: IoPointConfig } | null

const asDialogPoint = (p: IoPointConfig): DialogPoint => ({
  id: p.id, plcId: p.plcId, stationId: p.stationId, signalName: p.signalName, dataType: p.dataType, address: p.actualAddress, configStatus: p.configStatus,
})

/**
 * CR-004: IO mapping - the actual PLC address of the 368 IO points (4 PLC x 4 Stations x 23 signals; key PLC_ID + Station_ID + Signal_Name).
 * Admin only (also enforced by the API). Addresses are NEVER generated or derived; entering one does not make the point pollable -
 * only the existing approval step does (CR-003). Supported devices: D, W (word) and M (bit). X/Y are OUT_OF_SCOPE / NEEDS_CONFIRMATION.
 */
export function PlcIoMappingPage() {
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

  const mode = overview.data?.runtime.effectiveMode ?? 'Simulator'
  const simulator = mode === 'Simulator'
  const canWrite = simulator || mode === 'RealReadWrite'

  const rows = useMemo(() => {
    const q = search.trim().toLowerCase()
    return (points.data ?? []).filter((p) =>
      (!plc || p.plcId === plc) && (!station || p.stationId === station) && (!status || p.configStatus === status) && (!q || p.signalName.toLowerCase().includes(q)))
  }, [points.data, plc, station, status, search])

  const all = points.data ?? []
  const counts = { total: all.length, configured: all.filter((p) => p.configStatus !== 'NEEDS_CONFIGURATION').length, pollable: all.filter((p) => p.pollable).length }
  const stationOptions = plc ? STATIONS[plc] ?? [] : Object.values(STATIONS).flat()
  const done = (msg: string) => { setDialog(null); setNotice(msg); setIssues(null); points.reload() }
  const check = async () => {
    setCheckError(null)
    try { setIssues(await api.plcAddressCheck(plc)) } catch (e) { setCheckError(describeError(e)) }
  }

  return (
    <>
      <PageHeader title="Cấu hình địa chỉ IO" description="Địa chỉ PLC thực tế của 368 tín hiệu (4 PLC · 16 trạm · 23 tín hiệu). Khóa định danh: PLC + Trạm + Tín hiệu." />
      <div className="notice notice-info">
        Chỉ nhập địa chỉ <strong>do kỹ sư PLC cấp</strong>; hệ thống không tự sinh, không tự cộng offset, không gán theo thứ tự. Thiết bị hỗ trợ: <strong>D, W</strong> (word) và <strong>M</strong> (bit).
        <strong> X/Y chưa được hỗ trợ</strong> (OUT_OF_SCOPE / NEEDS_CONFIRMATION: cơ số bát phân hay thập lục phân phải được kỹ sư PLC xác nhận).
        Nhập địa chỉ <strong>không</strong> làm tín hiệu được polling: chỉ tín hiệu đã được ghi nhận xác nhận mới được đọc từ PLC thật.
      </div>
      {notice && <div className="notice notice-ok" role="status">{notice}</div>}
      <Panel title="Bộ lọc" subtitle={`${counts.total} tín hiệu · ${counts.configured} đã nhập địa chỉ · ${counts.pollable} được polling (Pollable)`}>
        <div className="form-grid">
          <label className="field"><span className="field-label">PLC</span>
            <select value={plc} onChange={(e) => { setPlc(e.target.value); setStation(''); setIssues(null) }}>
              <option value="">Tất cả</option>{PLCS.map((p) => <option key={p} value={p}>{p}</option>)}
            </select></label>
          <label className="field"><span className="field-label">Trạm</span>
            <select value={station} onChange={(e) => setStation(e.target.value)}>
              <option value="">Tất cả</option>{stationOptions.map((s) => <option key={s} value={s}>{s}</option>)}
            </select></label>
          <label className="field"><span className="field-label">Trạng thái cấu hình</span>
            <select value={status} onChange={(e) => setStatus(e.target.value)}>
              <option value="">Tất cả</option>{STATUSES.map((s) => <option key={s} value={s}>{CONFIG_STATUS_VI[s] ?? s}</option>)}
            </select></label>
          <label className="field"><span className="field-label">Tìm tên tín hiệu</span>
            <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="ví dụ Rack_Full" /></label>
        </div>
        <div className="btn-row">
          <Button disabled={!plc} onClick={() => void check()}>Kiểm tra xung đột địa chỉ {plc || '(chọn PLC)'}</Button>
          <Button onClick={() => { setPlc(''); setStation(''); setStatus(''); setSearch('') }}>Xóa bộ lọc</Button>
          <span className="muted">Hiển thị {rows.length} / {counts.total}</span>
        </div>
        {checkError && <div className="notice notice-bad" role="alert">{checkError}</div>}
        {issues && (issues.length === 0
          ? <div className="notice notice-ok">Không có xung đột địa chỉ trên {plc}.</div>
          : <div className="notice notice-bad"><strong>Xung đột địa chỉ:</strong><ul>{issues.map((i) => <li key={i.pointId + i.code}>{i.stationId}/{i.signal}: {i.message}</li>)}</ul></div>)}
      </Panel>

      <Panel title="Bảng IO Mapping" flush>
        <Async state={points} isEmpty={(d) => d.length === 0} empty={{ title: 'Chưa có IO point', hint: 'Danh mục IO chưa được nạp (kiểm tra migration và seeder).' }}>
          {() => (
            <div className="table-wrap">
              <table className="grid" aria-label="IO Mapping">
                <thead>
                  <tr><th>PLC</th><th>Trạm</th><th>Tín hiệu</th><th>Mô tả</th><th>Kiểu dữ liệu</th><th>Thiết bị</th><th>Địa chỉ PLC thực tế</th><th>Pollable</th><th>Xác nhận</th><th>Thao tác</th></tr>
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
                        <td className="small">{p.description}</td>
                        <td>{DATATYPE_VI[p.dataType]}</td>
                        <td className="mono">{deviceOf(p.actualAddress) ?? <span className="muted">—</span>}</td>
                        <td className="mono">{p.actualAddress ?? <span className="muted">chưa gán</span>}</td>
                        <td>{p.pollable ? <Pill tone="ok" dot={false}>Có</Pill> : <Pill tone="muted" dot={false}>Không</Pill>}</td>
                        <td><ConfigPill status={p.configStatus} />{p.approvedBy && <small className="muted"> {p.approvedBy}</small>}</td>
                        <td className="nowrap">
                          <Button size="sm" onClick={() => setDialog({ kind: 'address', point: p })}>Địa chỉ</Button>{' '}
                          {simulator && <><Button size="sm" onClick={() => setDialog({ kind: 'sim', point: p })}>Giả lập</Button>{' '}</>}
                          {!inbound && !blocked && canWrite && <Button size="sm" variant="danger" onClick={() => setDialog({ kind: 'write', point: p })}>Ghi thử</Button>}
                          {!inbound && blocked && <small className="muted" title="Safety/Interlock và Transfer/Departure Permit không bao giờ được ghi thử">🔒 chặn ghi</small>}
                        </td>
                      </tr>
                    )
                  })}
                  {rows.length === 0 && <tr><td colSpan={10} className="muted">Không có tín hiệu nào khớp bộ lọc.</td></tr>}
                </tbody>
              </table>
            </div>
          )}
        </Async>
      </Panel>
      {dialog?.kind === 'address' && <AddressDialog point={asDialogPoint(dialog.point)} onClose={() => setDialog(null)} onDone={done} />}
      {dialog?.kind === 'write' && <WriteDialog point={asDialogPoint(dialog.point)} simulator={simulator} onClose={() => setDialog(null)} onDone={done} />}
      {dialog?.kind === 'sim' && <SimDialog point={asDialogPoint(dialog.point)} onClose={() => setDialog(null)} onDone={done} />}
    </>
  )
}
