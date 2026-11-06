import { useCallback, useState } from 'react'
import { api } from '../../api/client'
import type { PlcConfig, SavePlcBody } from '../../api/configTypes'
import { Async, Button, Panel } from '../../components/ui'
import { useApi } from '../../hooks/useApi'
import { formatDateTime } from '../../i18n/format'
import { PLC_PROTOCOL_VI } from '../../i18n/config'
import { PLC_PROTOCOLS, validatePlc } from '../../settings/validate'
import { Field, Switch, TestResultPanel } from './FormKit'
import { fromApiError, fromNum, toErrors, toInt, toStr, type Errors } from './formUtils'

interface Form {
  enabled: boolean; deviceName: string; ipAddress: string; port: string; protocol: string
  networkNumber: string; pcNumber: string; stationNumber: string; timeoutMs: string; pollingIntervalMs: string
}

const toForm = (c: PlcConfig): Form => ({
  enabled: c.enabled, deviceName: c.settings.deviceName ?? '', ipAddress: c.settings.ipAddress ?? '', port: fromNum(c.settings.port),
  protocol: c.settings.protocol ?? '', networkNumber: fromNum(c.settings.networkNumber), pcNumber: fromNum(c.settings.pcNumber),
  stationNumber: fromNum(c.settings.stationNumber), timeoutMs: fromNum(c.settings.timeoutMs), pollingIntervalMs: fromNum(c.settings.pollingIntervalMs),
})

const toBody = (f: Form): SavePlcBody => ({
  enabled: f.enabled, deviceName: toStr(f.deviceName), ipAddress: toStr(f.ipAddress), port: toInt(f.port), protocol: toStr(f.protocol),
  networkNumber: toInt(f.networkNumber), pcNumber: toInt(f.pcNumber), stationNumber: toInt(f.stationNumber), timeoutMs: toInt(f.timeoutMs),
  pollingIntervalMs: toInt(f.pollingIntervalMs), updatedBy: 'operator',
})

export function PlcTab() {
  const state = useApi(useCallback(() => api.plcConfig(), []))
  return (
    <Async state={state} empty={{ title: 'Không có dữ liệu cấu hình' }}>
      {(cfg) => <PlcForm key={cfg.updatedAt ?? 'new'} initial={cfg} onChanged={state.reload} />}
    </Async>
  )
}

function PlcForm({ initial, onChanged }: { initial: PlcConfig; onChanged: () => void }) {
  const [form, setForm] = useState<Form>(() => toForm(initial))
  const [errors, setErrors] = useState<Errors>({})
  const [general, setGeneral] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)
  const [busy, setBusy] = useState<'save' | 'test' | null>(null)

  const dirty = JSON.stringify(form) !== JSON.stringify(toForm(initial))
  const set = <K extends keyof Form>(k: K, v: Form[K]) => { setForm((f) => ({ ...f, [k]: v })); setSaved(false) }

  const save = async () => {
    setGeneral(null)
    const local = validatePlc(toBody(form))
    if (local.length) { setErrors(toErrors(local)); setGeneral('Dữ liệu chưa hợp lệ. Hãy sửa các trường được đánh dấu.'); return }
    setErrors({}); setBusy('save')
    try { await api.savePlc(toBody(form)); setSaved(true); onChanged() }
    catch (e) { const r = fromApiError(e); setErrors(r.fields); setGeneral(r.general) }
    finally { setBusy(null) }
  }

  const test = async () => {
    setBusy('test'); setGeneral(null)
    try { await api.testPlc(); onChanged() }
    catch (e) { setGeneral(fromApiError(e).general) }
    finally { setBusy(null) }
  }

  return (
    <div className="cfg-grid">
      <Panel title="Kết nối PLC Mitsubishi" subtitle={initial.updatedAt ? `Lưu lần cuối ${formatDateTime(initial.updatedAt)} · ${initial.updatedBy ?? ''}` : 'Chưa có cấu hình nào được lưu'}>
        <div className="notice notice-warn">
          Các trường dưới đây là <strong>đề xuất cần đối chiếu với kỹ sư PLC</strong> và adapter thực tế; không có giá trị nào được điền sẵn.
          Giai đoạn 1 chưa có adapter PLC thật: bật kết nối chỉ lưu cấu hình, WCS chưa tự kết nối thiết bị.
        </div>
        <Switch checked={form.enabled} onChange={(v) => set('enabled', v)} label="Bật kết nối PLC" />
        <div className="form-grid">
          <Field label="Tên thiết bị" error={errors.deviceName}><input value={form.deviceName} onChange={(e) => set('deviceName', e.target.value)} maxLength={100} /></Field>
          <Field label="Giao thức kết nối" error={errors.protocol} hint="Chỉ liệt kê giao thức đã nêu trong tài liệu tích hợp (cần xác nhận với kỹ sư PLC).">
            <select value={form.protocol} onChange={(e) => set('protocol', e.target.value)}>
              <option value="">— Chưa chọn —</option>
              {PLC_PROTOCOLS.map((p) => <option key={p} value={p}>{PLC_PROTOCOL_VI[p] ?? p}</option>)}
            </select>
          </Field>
          <Field label="Địa chỉ IP" error={errors.ipAddress} hint="Địa chỉ IPv4/IPv6 của module Ethernet PLC."><input value={form.ipAddress} onChange={(e) => set('ipAddress', e.target.value)} inputMode="decimal" autoComplete="off" /></Field>
          <Field label="Cổng TCP" error={errors.port} hint="1–65535. Lấy từ cấu hình module Ethernet của PLC."><input value={form.port} onChange={(e) => set('port', e.target.value)} inputMode="numeric" /></Field>
          <Field label="Network number" error={errors.networkNumber} hint="0–255. Chỉ cần nếu giao thức/adapter yêu cầu."><input value={form.networkNumber} onChange={(e) => set('networkNumber', e.target.value)} inputMode="numeric" /></Field>
          <Field label="PC number" error={errors.pcNumber} hint="0–255. Chỉ cần nếu giao thức/adapter yêu cầu."><input value={form.pcNumber} onChange={(e) => set('pcNumber', e.target.value)} inputMode="numeric" /></Field>
          <Field label="PLC station number" error={errors.stationNumber} hint="0–255. Chỉ cần nếu giao thức/adapter yêu cầu."><input value={form.stationNumber} onChange={(e) => set('stationNumber', e.target.value)} inputMode="numeric" /></Field>
          <Field label="Timeout (ms)" error={errors.timeoutMs} hint="100–60000."><input value={form.timeoutMs} onChange={(e) => set('timeoutMs', e.target.value)} inputMode="numeric" /></Field>
          <Field label="Chu kỳ đọc dữ liệu (ms)" error={errors.pollingIntervalMs} hint="Tùy chọn. 50–3600000."><input value={form.pollingIntervalMs} onChange={(e) => set('pollingIntervalMs', e.target.value)} inputMode="numeric" /></Field>
        </div>
        {general && <div className="notice notice-bad" role="alert">{general}</div>}
        {saved && !dirty && <div className="notice notice-info">Đã lưu cấu hình PLC.</div>}
        <div className="btn-row">
          <Button variant="primary" disabled={!dirty || busy !== null} onClick={() => void save()}>{busy === 'save' ? 'Đang lưu…' : 'Lưu cấu hình'}</Button>
          <Button disabled={!dirty || busy !== null} onClick={() => { setForm(toForm(initial)); setErrors({}); setGeneral(null) }}>Hoàn tác thay đổi</Button>
          {dirty && <span className="muted">Có thay đổi chưa lưu.</span>}
        </div>
      </Panel>

      <Panel title="Kiểm tra kết nối" subtitle="Dùng cấu hình đã lưu">
        <p className="muted">
          Phép kiểm tra an toàn: không gửi lệnh PLC, không tạo hay điều khiển nhiệm vụ vận chuyển. Mặc định là kiểm tra mô phỏng và không dùng mạng;
          kiểm tra cổng TCP thật chỉ chạy khi Backend được bật tùy chọn riêng.
        </p>
        <div className="btn-row">
          <Button variant="primary" disabled={dirty || !initial.configured || busy !== null} onClick={() => void test()}>Kiểm tra kết nối</Button>
        </div>
        {(dirty || !initial.configured) && <p className="muted">{initial.configured ? 'Hãy lưu thay đổi trước khi kiểm tra.' : 'Hãy lưu cấu hình trước khi kiểm tra.'}</p>}
        <TestResultPanel last={initial.lastTest} running={busy === 'test'} />
      </Panel>
    </div>
  )
}
