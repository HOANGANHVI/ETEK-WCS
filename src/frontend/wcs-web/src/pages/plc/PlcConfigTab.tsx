import { useCallback, useState } from 'react'
import { api } from '../../api/client'
import type { AddressIssue, PlcConnection, ProbeResultView } from '../../api/plcTypes'
import { Async, Button, Panel, Pill } from '../../components/ui'
import { useApi } from '../../hooks/useApi'
import { MISSING_PARAM_VI } from '../../i18n/plc'
import { describeError } from '../../i18n/vi'
import { Field, Switch } from '../config/FormKit'
import { fromApiError, fromNum, toInt, toStr, type Errors } from '../config/formUtils'
import { fmtTime } from './plcFormat'
import { ConfigPill } from './plcUi'

const PROTOCOLS = [{ value: 'McProtocol3EBinaryTcp', label: 'MC Protocol – 3E, Binary, TCP (dự kiến, cần xác nhận)' }]

/** Admin-only: the four PLC connections. Nothing is pre-filled except what the catalog knows (name and IP); every other parameter is entered by the PLC engineer. */
export function PlcConfigTab({ realMode }: { realMode: boolean }) {
  const list = useApi(useCallback(() => api.plcConnections(), []))
  const [editing, setEditing] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  return (
    <>
      <div className="notice notice-info">
        Thông số kết nối thật (cổng, giao thức, Network/PC/Module I/O/Station number, Monitoring timer, timeout, chu kỳ đọc) <strong>chưa được xác nhận</strong> và không có giá trị mặc định.
        Chỉ nhập khi kỹ sư PLC đã cung cấp. Lần kết nối thật đầu tiên chỉ đọc.
        {!realMode && ' Hệ thống đang ở chế độ mô phỏng nên các thông số này chưa được sử dụng.'}
      </div>
      {message && <div className="notice notice-ok" role="status">{message}</div>}
      <Async state={list} isEmpty={(d) => d.length === 0} empty={{ title: 'Chưa có PLC nào', hint: 'Danh mục IO chưa được nạp (kiểm tra migration và seeder).' }}>
        {(rows) => (
          <div className="plc-config-grid">
            {rows.map((c) => (
              <Panel key={c.plcId} title={`${c.plcId} · ${c.stations.join(', ')}`} subtitle={`Cập nhật: ${fmtTime(c.updatedAt)}${c.updatedBy ? ` bởi ${c.updatedBy}` : ''}`}
                actions={<ConfigPill status={c.configStatus} />}>
                {editing === c.plcId
                  ? <ConnectionForm c={c} realMode={realMode} onCancel={() => setEditing(null)} onSaved={() => { setEditing(null); setMessage(`Đã lưu cấu hình ${c.plcId}.`); list.reload() }} />
                  : <ConnectionSummary c={c} realMode={realMode} onEdit={() => setEditing(c.plcId)} />}
              </Panel>
            ))}
          </div>
        )}
      </Async>
    </>
  )
}

function ConnectionSummary({ c, onEdit, realMode }: { c: PlcConnection; onEdit: () => void; realMode: boolean }) {
  const [busy, setBusy] = useState(false)
  const [probe, setProbe] = useState<ProbeResultView | null>(null)
  const [issues, setIssues] = useState<AddressIssue[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const run = async (fn: () => Promise<void>) => { setBusy(true); setError(null); try { await fn() } catch (e) { setError(describeError(e)) } finally { setBusy(false) } }
  const row = (k: string, v: string | number | null) => <><dt>{k}</dt><dd>{v === null || v === '' ? <span className="muted">chưa cấu hình</span> : v}</dd></>
  return (
    <>
      <dl className="kv2">
        {row('Địa chỉ IP', c.ipAddress)}{row('Cổng TCP', c.port)}{row('Giao thức', c.protocol)}
        {row('Network number', c.networkNumber)}{row('PC number', c.pcNumber)}{row('Module I/O number', c.moduleIoNumber)}{row('PLC station number', c.plcStationNumber)}
        {row('Monitoring timer', c.monitoringTimer)}{row('Timeout (ms)', c.timeoutMs)}{row('Chu kỳ đọc (ms)', c.pollingIntervalMs)}
        <dt>Bật kết nối</dt><dd>{c.enabled ? <Pill tone="ok">Đã bật</Pill> : <Pill tone="muted">Đang tắt</Pill>}</dd>
      </dl>
      {c.missingParameters.length > 0 && (
        <div className="notice notice-warn">NEEDS_CONFIGURATION – còn thiếu: {c.missingParameters.map((m) => MISSING_PARAM_VI[m] ?? m).join(', ')}.</div>
      )}
      {error && <div className="notice notice-bad" role="alert">{error}</div>}
      {probe && <div className={`notice ${probe.outcome === 'Failed' ? 'notice-bad' : 'notice-info'}`}>Kiểm tra ({probe.mode}): {probe.message}{probe.latencyMs !== null ? ` · ${probe.latencyMs} ms` : ''}</div>}
      {issues && (issues.length === 0
        ? <div className="notice notice-ok">Không có xung đột địa chỉ trên {c.plcId}.</div>
        : <div className="notice notice-bad"><strong>Xung đột địa chỉ:</strong><ul>{issues.map((i) => <li key={i.pointId + i.code}>{i.stationId}/{i.signal}: {i.message}</li>)}</ul></div>)}
      <div className="btn-row">
        <Button variant="primary" onClick={onEdit}>Sửa cấu hình</Button>
        <Button disabled={busy} onClick={() => void run(async () => setProbe(await api.plcTestConnection(c.plcId)))}>Kiểm tra kết nối</Button>
        <Button disabled={busy} onClick={() => void run(async () => setIssues(await api.plcAddressCheck(c.plcId)))}>Kiểm tra xung đột địa chỉ</Button>
      </div>
      <p className="muted small">{realMode ? 'Kiểm tra kết nối chỉ mở/đóng cổng TCP, không gửi lệnh PLC.' : 'Chế độ mô phỏng: kiểm tra không dùng mạng.'}</p>
    </>
  )
}

function ConnectionForm({ c, onCancel, onSaved, realMode }: { c: PlcConnection; onCancel: () => void; onSaved: () => void; realMode: boolean }) {
  const [f, setF] = useState({
    name: c.name, ip: c.ipAddress, port: fromNum(c.port), protocol: c.protocol ?? '', net: fromNum(c.networkNumber), pc: fromNum(c.pcNumber),
    module: fromNum(c.moduleIoNumber), station: fromNum(c.plcStationNumber), timer: fromNum(c.monitoringTimer), timeout: fromNum(c.timeoutMs),
    poll: fromNum(c.pollingIntervalMs), enabled: c.enabled,
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
        {num('Timeout (ms)', 'timeout', 'timeoutMs', '100–60000')}
        {num('Chu kỳ đọc (ms)', 'poll', 'pollingIntervalMs', '50–60000')}
        <div className="field switches">
          <Switch checked={f.enabled} onChange={(v) => setF((p) => ({ ...p, enabled: v }))} label="Bật kết nối PLC này" />
          <span className="field-hint">{realMode ? 'Chỉ bật được khi đủ thông số. Kết nối thật còn cần khóa triển khai Plc:AllowLiveConnection.' : 'Chế độ mô phỏng: công tắc này chưa được sử dụng.'}</span>
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
