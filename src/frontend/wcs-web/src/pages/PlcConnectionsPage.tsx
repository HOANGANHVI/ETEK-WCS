import { useCallback, useState } from 'react'
import { api } from '../api/client'
import type { PlcConnection, ProbeResultView } from '../api/plcTypes'
import { Async, Button, PageHeader, Panel, Pill } from '../components/ui'
import { ENGINEERING_TOOLS } from '../config'
import { useApi } from '../hooks/useApi'
import { MISSING_PARAM_VI } from '../i18n/plc'
import { describeError } from '../i18n/vi'
import { Field, Switch } from './config/FormKit'
import { fromApiError, fromNum, toInt, toStr, type Errors } from './config/formUtils'
import { fmtTime } from './plc/plcFormat'
import { ConfigPill } from './plc/plcUi'

const PROTOCOLS = [{ value: 'McProtocol3EBinaryTcp', label: 'MC Protocol – 3E, Binary, TCP (dự kiến, cần xác nhận)' }]

/**
 * CR-004: the ONLY place where PLC connection parameters are edited (4 PLCs, one form each). Admin-only (also enforced by the API).
 * Nothing is pre-filled except what the catalog knows (name and IP); every other parameter is entered by the PLC engineer. IO addresses are NOT edited here.
 */
export function PlcConnectionsPage() {
  const overview = useApi(useCallback(() => api.plcOverview(), []))
  const mode = overview.data?.runtime.effectiveMode ?? 'Simulator'
  return (
    <>
      <PageHeader title="Kết nối PLC" />
      <PlcConnectionsBody realMode={mode !== 'Simulator'} simulator={mode === 'Simulator'} />
    </>
  )
}

function PlcConnectionsBody({ realMode, simulator }: { realMode: boolean; simulator: boolean }) {
  const list = useApi(useCallback(() => api.plcConnections(), []))
  const [editing, setEditing] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  return (
    <>
      {message && <div className="notice notice-ok" role="status">{message}</div>}
      <Async state={list} isEmpty={(d) => d.length === 0} empty={{ title: 'Chưa có PLC nào', hint: 'Danh mục PLC chưa sẵn sàng. Vui lòng liên hệ quản trị hệ thống.' }}>
        {(rows) => (
          <div className="plc-config-grid">
            {rows.map((c) => (
              <Panel key={c.plcId} title={`${c.plcId} · ${c.stations.join(', ')}`} subtitle={`Cập nhật: ${fmtTime(c.updatedAt)}${c.updatedBy ? ` bởi ${c.updatedBy}` : ''}`}
                actions={<ConfigPill status={c.configStatus} />}>
                {editing === c.plcId
                  ? <ConnectionForm c={c} realMode={realMode} onCancel={() => setEditing(null)} onSaved={() => { setEditing(null); setMessage(`Đã lưu cấu hình ${c.plcId}.`); list.reload() }} />
                  : <ConnectionSummary c={c} realMode={realMode} simulator={simulator} onEdit={() => setEditing(c.plcId)} />}
              </Panel>
            ))}
          </div>
        )}
      </Async>
    </>
  )
}

function ConnectionSummary({ c, onEdit, realMode, simulator }: { c: PlcConnection; onEdit: () => void; realMode: boolean; simulator: boolean }) {
  const [busy, setBusy] = useState(false)
  const [probe, setProbe] = useState<ProbeResultView | null>(null)
  const [error, setError] = useState<string | null>(null)
  const run = async (fn: () => Promise<void>) => { setBusy(true); setError(null); try { await fn() } catch (e) { setError(describeError(e)) } finally { setBusy(false) } }
  const row = (k: string, v: string | number | null) => <><dt>{k}</dt><dd>{v === null || v === '' ? <span className="muted">Chưa thiết lập</span> : v}</dd></>
  return (
    <>
      <dl className="kv2">
        {row('Địa chỉ IP', c.ipAddress)}{row('Cổng TCP', c.port)}{row('Giao thức', c.protocol)}
        {row('Network number', c.networkNumber)}{row('PC number', c.pcNumber)}{row('Module I/O number', c.moduleIoNumber)}{row('PLC station number', c.plcStationNumber)}
        {row('Monitoring timer (×250 ms)', c.monitoringTimer)}{row('Timeout chung (ms)', c.timeoutMs)}{row('Timeout kết nối (ms)', c.connectTimeoutMs ?? (c.timeoutMs === null ? null : 'theo timeout chung'))}
        {row('Timeout đọc (ms)', c.readTimeoutMs ?? (c.timeoutMs === null ? null : 'theo timeout chung'))}{row('Timeout ghi (ms)', c.writeTimeoutMs ?? (c.timeoutMs === null ? null : 'theo timeout chung'))}{row('Chu kỳ đọc (ms)', c.pollingIntervalMs)}
        {row('Cơ số địa chỉ X/Y', c.xyAddressBase === 8 ? 'Bát phân (8)' : c.xyAddressBase === 16 ? 'Thập lục phân (16)' : null)}{row('Cơ số địa chỉ W', c.wAddressBase === 10 ? 'Thập phân (10)' : c.wAddressBase === 16 ? 'Thập lục phân (16)' : null)}
        <dt>Bật kết nối</dt><dd>{c.enabled ? <Pill tone="ok">Đã bật</Pill> : <Pill tone="muted">Đang tắt</Pill>}</dd>
      </dl>
      {c.missingParameters.length > 0 && (
        <div className="notice notice-warn">Cần bổ sung thông số: {c.missingParameters.map((m) => MISSING_PARAM_VI[m] ?? m).join(', ')}.</div>
      )}
      {error && <div className="notice notice-bad" role="alert">{error}</div>}
      {probe && <div className={`notice ${probe.outcome === 'Failed' ? 'notice-bad' : 'notice-info'}`}>Kiểm tra ({probe.mode}): {probe.message}{probe.latencyMs !== null ? ` · ${probe.latencyMs} ms` : ''}</div>}
      <div className="btn-row">
        <Button variant="primary" onClick={onEdit}>Sửa cấu hình</Button>
        <Button disabled={busy} onClick={() => void run(async () => setProbe(await api.plcTestConnection(c.plcId)))}>Kiểm tra kết nối</Button>
        {simulator && ENGINEERING_TOOLS && <Button disabled={busy} onClick={() => void run(async () => { await api.plcSimOnline(c.plcId, false) })}>Mô phỏng mất kết nối</Button>}
        {simulator && ENGINEERING_TOOLS && <Button disabled={busy} onClick={() => void run(async () => { await api.plcSimOnline(c.plcId, true) })}>Khôi phục</Button>}
      </div>
      {realMode && <p className="muted small">Kiểm tra kết nối chỉ mở/đóng cổng TCP, không gửi lệnh PLC.</p>}
    </>
  )
}

function ConnectionForm({ c, onCancel, onSaved, realMode }: { c: PlcConnection; onCancel: () => void; onSaved: () => void; realMode: boolean }) {
  const [f, setF] = useState({
    name: c.name, ip: c.ipAddress, port: fromNum(c.port), protocol: c.protocol ?? '', net: fromNum(c.networkNumber), pc: fromNum(c.pcNumber),
    module: fromNum(c.moduleIoNumber), station: fromNum(c.plcStationNumber), timer: fromNum(c.monitoringTimer), timeout: fromNum(c.timeoutMs),
    poll: fromNum(c.pollingIntervalMs), enabled: c.enabled,
    connectTimeout: fromNum(c.connectTimeoutMs), readTimeout: fromNum(c.readTimeoutMs), writeTimeout: fromNum(c.writeTimeoutMs),
    xyBase: fromNum(c.xyAddressBase), wBase: fromNum(c.wAddressBase),
  })
  const [busy, setBusy] = useState(false)
  const [errors, setErrors] = useState<Errors>({})
  const [general, setGeneral] = useState<string | null>(null)
  const set = (k: keyof typeof f) => (e: { target: { value: string } }) => setF((p) => ({ ...p, [k]: e.target.value }))
  const save = async () => {
    setBusy(true); setErrors({}); setGeneral(null)
    try {
      await api.plcSaveConnection(c.plcId, {
        name: toStr(f.name), ipAddress: f.ip.trim(), port: toInt(f.port), protocol: toStr(f.protocol), networkNumber: toInt(f.net), pcNumber: toInt(f.pc),
        moduleIoNumber: toInt(f.module), plcStationNumber: toInt(f.station), monitoringTimer: toInt(f.timer), timeoutMs: toInt(f.timeout), pollingIntervalMs: toInt(f.poll), enabled: f.enabled,
        connectTimeoutMs: toInt(f.connectTimeout), readTimeoutMs: toInt(f.readTimeout), writeTimeoutMs: toInt(f.writeTimeout),
        xyAddressBase: toInt(f.xyBase), wAddressBase: toInt(f.wBase),
      })
      onSaved()
    } catch (e) { const r = fromApiError(e); setErrors(r.fields); setGeneral(r.general) } finally { setBusy(false) }
  }
  const num = (label: string, k: keyof typeof f, name: string, hint?: string) => (
    <Field label={label} error={errors[name]} hint={hint}><input inputMode="numeric" value={String(f[k])} onChange={set(k)} /></Field>
  )
  return (
    <>
      {general && <div className="notice notice-bad" role="alert">{general}</div>}
      <div className="form-grid">
        <Field label="Tên hiển thị" error={errors.name}><input value={f.name} onChange={set('name')} /></Field>
        <Field label="Địa chỉ IP" error={errors.ipAddress}><input value={f.ip} onChange={set('ip')} /></Field>
        {num('Cổng TCP', 'port', 'port', 'Do kỹ sư PLC cung cấp (QJ71E71-100)')}
        <Field label="Giao thức" error={errors.protocol}>
          <select value={f.protocol} onChange={set('protocol')}><option value="">— Chưa xác nhận —</option>{PROTOCOLS.map((p) => <option key={p.value} value={p.value}>{p.label}</option>)}</select>
        </Field>
        {num('Network number', 'net', 'networkNumber')}
        {num('PC number', 'pc', 'pcNumber')}
        {num('Module I/O number', 'module', 'moduleIoNumber', 'Request destination module I/O')}
        {num('PLC station number', 'station', 'plcStationNumber', 'Request destination module station')}
        {num('Monitoring timer', 'timer', 'monitoringTimer', 'Đơn vị 250 ms')}
        {num('Timeout chung (ms)', 'timeout', 'timeoutMs', '100–60000. Dùng cho kết nối/đọc/ghi nếu không đặt riêng.')}
        {num('Timeout kết nối (ms)', 'connectTimeout', 'connectTimeoutMs', 'Tùy chọn, 100–60000. Để trống = timeout chung.')}
        {num('Timeout đọc (ms)', 'readTimeout', 'readTimeoutMs', 'Tùy chọn, 100–60000. Để trống = timeout chung.')}
        {num('Timeout ghi (ms)', 'writeTimeout', 'writeTimeoutMs', 'Tùy chọn, 100–60000. Lệnh ghi không bao giờ tự gửi lại.')}
        <Field label="Cơ số địa chỉ X/Y" error={errors.xyAddressBase} hint="Chờ kỹ sư PLC xác nhận. MELSEC-Q thường dùng thập lục phân; FX/iQ-F dùng bát phân.">
          <select value={f.xyBase} onChange={set('xyBase')}><option value="">— Chưa xác nhận —</option><option value="8">Bát phân (X10 = 8)</option><option value="16">Thập lục phân (X10 = 16)</option></select>
        </Field>
        <Field label="Cơ số địa chỉ W" error={errors.wAddressBase} hint="Chờ kỹ sư PLC xác nhận. Áp dụng cho địa chỉ thanh ghi liên kết W.">
          <select value={f.wBase} onChange={set('wBase')}><option value="">— Chưa xác nhận —</option><option value="10">Thập phân (W10 = 10)</option><option value="16">Thập lục phân (W10 = 16)</option></select>
        </Field>
        {num('Chu kỳ đọc (ms)', 'poll', 'pollingIntervalMs', '50–60000')}
        <div className="field switches">
          <Switch checked={f.enabled} onChange={(v) => setF((p) => ({ ...p, enabled: v }))} label="Bật kết nối PLC này" />
          {realMode && <span className="field-hint">Chỉ bật được khi đủ thông số. Kết nối thật còn cần khóa triển khai Plc:AllowLiveConnection.</span>}
          {errors.enabled && <span className="field-error">{errors.enabled}</span>}
        </div>
      </div>
      <div className="btn-row">
        <Button variant="primary" disabled={busy} onClick={() => void save()}>{busy ? 'Đang lưu…' : 'Lưu cấu hình'}</Button>
        <Button disabled={busy} onClick={onCancel}>Hủy</Button>
      </div>
    </>
  )
}
