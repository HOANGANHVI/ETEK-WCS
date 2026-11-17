import { useCallback, useState } from 'react'
import { api } from '../../api/client'
import type { Agv, Location } from '../../api/types'
import type { OperationsView, SaveLocationBody } from '../../api/configTypes'
import { Async, Button, Panel, Pill } from '../../components/ui'
import { useApi } from '../../hooks/useApi'
import { MODE_VI, NO_SOURCE_VI } from '../../i18n/config'
import { ALLOWED_KIND_VI, flowInfo, LOCATION_TYPE_VI, RACK_KIND_VI, TASK_STATE_VI, AGV_ROLE_VI } from '../../i18n/vi'
import type { TaskState } from '../../api/types'
import { Field, Switch } from './FormKit'
import { fromApiError, type Errors } from './formUtils'

const EVENTS = ['InventoryChanged', 'AgvStatusChanged', 'TaskStateChanged'] as const

export function OperationsTab() {
  const state = useApi(useCallback(() => api.operations(), []), [...EVENTS])
  return (
    <Async state={state} empty={{ title: 'Không có dữ liệu' }}>
      {(ops) => <OperationsView ops={ops} onChanged={state.reload} />}
    </Async>
  )
}

const approval = (ok: boolean) => ok ? <Pill tone="ok">Đã được phê duyệt</Pill> : <Pill tone="warn">Chưa được xác nhận</Pill>
const groups = (g: string[][]) => g.length === 0 ? '—' : g.map((x) => x.join(' / ')).join('  ›  ')

function OperationsView({ ops, onChanged }: { ops: OperationsView; onChanged: () => void }) {
  const [adding, setAdding] = useState(false)
  const timeouts = Object.entries(ops.scheduler.stateTimeoutsSeconds)
  return (
    <div className="ops-stack">
      <div className="ops-two">
        <Panel title="Chính sách điều phối AGV">
          <table className="grid">
            <tbody>
              <tr><td>Số nhiệm vụ hoạt động trên mỗi AGV</td><td><strong>Tối đa 1 (PER_AGV)</strong></td><td>{approval(ops.policy.concurrencyApproved)}</td></tr>
              <tr><td>Thứ tự hàng đợi</td><td>Theo thời gian tạo (FIFO)</td><td>{approval(ops.policy.queueOrderApproved)}</td></tr>
              <tr><td>Khi không có rack nguồn</td><td>{NO_SOURCE_VI[ops.policy.noSourceBehavior] ?? ops.policy.noSourceBehavior}</td><td>{approval(ops.policy.noSourceBehaviorApproved)}</td></tr>
            </tbody>
          </table>
        </Panel>

        <Panel title="Tham số bộ lập lịch và timeout">
          <dl className="kv2">
            <dt>Chu kỳ quét</dt><dd>{ops.scheduler.tickIntervalMs} ms</dd>
            <dt>Số lần gửi RCS tối đa</dt><dd>{ops.scheduler.maxDispatchAttempts}</dd>
            <dt>Timeout theo trạng thái</dt>
            <dd>{timeouts.length === 0 ? <span className="muted">Không giới hạn</span>
              : timeouts.map(([k, v]) => <div key={k}>{TASK_STATE_VI[k as TaskState] ?? k}: {v} giây</div>)}</dd>
          </dl>
        </Panel>
      </div>

      <Panel title="Quy trình đã triển khai" flush>
        <div className="table-wrap">
          <table className="grid">
            <thead><tr><th>Quy trình</th><th>Rack</th><th>AGV</th><th>Trạm được yêu cầu</th><th>Nguồn</th><th>Đích</th><th>Không bind rack tại</th></tr></thead>
            <tbody>
              {ops.flows.map((f) => (
                <tr key={f.flow}>
                  <td><strong>{flowInfo(f.flow).code}</strong> <span className="muted">{flowInfo(f.flow).label}</span></td>
                  <td>{(RACK_KIND_VI[f.rackKind] ?? f.rackKind).replace(/^./, (c) => c.toUpperCase())}</td>
                  <td>{AGV_ROLE_VI[f.agvRole] ?? f.agvRole}</td>
                  <td>{f.stations.join(', ')}</td>
                  <td>{f.sourceMode === 'Request' ? MODE_VI.Request : groups(f.sourceGroups)}</td>
                  <td>{f.destinationMode === 'Request' ? MODE_VI.Request : groups(f.destinationGroups)}</td>
                  <td>{f.noBindDropLocations.length ? f.noBindDropLocations.join(', ') : '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Panel>

      <Panel title="AGV và RCS Robot Code" flush>
        <div className="table-wrap">
          <table className="grid">
            <thead><tr><th>AGV</th><th>Vai trò</th><th>RCS Robot Code</th><th>Sử dụng</th><th>Thao tác</th></tr></thead>
            <tbody>{ops.agvs.map((a) => <AgvRow key={`${a.id}-${a.rcsRobotCode}-${a.enabled}`} agv={a} onSaved={onChanged} />)}</tbody>
          </table>
        </div>
      </Panel>

      <Panel title="Cấu hình vị trí" flush
        actions={<Button size="sm" variant="primary" onClick={() => setAdding((v) => !v)}>{adding ? 'Đóng' : 'Thêm vị trí'}</Button>}>
        {adding && <div className="pad"><LocationForm onDone={() => { setAdding(false); onChanged() }} onCancel={() => setAdding(false)} /></div>}
        {ops.locations.length === 0 && !adding ? (
          <div className="state state-empty"><div><strong>Chưa có vị trí nào</strong><span>Quản lý danh sách vị trí lưu trữ theo sơ đồ bố trí nhà máy.</span></div></div>
        ) : (
          <div className="table-wrap">
            <table className="grid">
              <thead><tr><th>Mã</th><th>Tên</th><th>Loại</th><th>Chấp nhận</th><th className="right">Sức chứa</th><th>PLC cho phép</th><th>Sử dụng</th><th>Thao tác</th></tr></thead>
              <tbody>{ops.locations.map((l) => <LocationRow key={`${l.id}-${l.rackCapacity}-${l.name}-${l.enabled}`} loc={l} onSaved={onChanged} />)}</tbody>
            </table>
          </div>
        )}
      </Panel>
    </div>
  )
}

function AgvRow({ agv, onSaved }: { agv: Agv; onSaved: () => void }) {
  const [code, setCode] = useState(agv.rcsRobotCode ?? '')
  const [enabled, setEnabled] = useState(agv.enabled)
  const [err, setErr] = useState<{ fields: Errors; general: string | null } | null>(null)
  const [busy, setBusy] = useState(false)
  const dirty = code.trim() !== (agv.rcsRobotCode ?? '') || enabled !== agv.enabled
  const save = async () => {
    setBusy(true); setErr(null)
    try { await api.saveAgv(agv.id, { rcsRobotCode: code.trim() || null, enabled }); onSaved() }
    catch (e) { setErr(fromApiError(e)) }
    finally { setBusy(false) }
  }
  return (
    <tr>
      <td><strong>{agv.code}</strong></td>
      <td>{AGV_ROLE_VI[agv.role]}</td>
      <td>
        <input className="inline-input" aria-label={`RCS Robot Code của ${agv.code}`} value={code} onChange={(e) => setCode(e.target.value)} placeholder="Chưa gán" />
        {(err?.fields.rcsRobotCode) && <div className="field-error">{err.fields.rcsRobotCode}</div>}
        {err && !err.fields.rcsRobotCode && err.general && <div className="field-error">{err.general}</div>}
      </td>
      <td><Switch checked={enabled} onChange={setEnabled} label={enabled ? 'Đang bật' : 'Đã tắt'} /></td>
      <td><Button size="sm" variant="primary" disabled={!dirty || busy} onClick={() => void save()}>Lưu</Button></td>
    </tr>
  )
}

function LocationRow({ loc, onSaved }: { loc: Location; onSaved: () => void }) {
  const [editing, setEditing] = useState(false)
  if (editing) return (
    <tr><td colSpan={8}><LocationForm initial={loc} onDone={() => { setEditing(false); onSaved() }} onCancel={() => setEditing(false)} /></td></tr>
  )
  return (
    <tr>
      <td><strong>{loc.code}</strong></td>
      <td>{loc.name}</td>
      <td>{LOCATION_TYPE_VI[loc.type] ?? loc.type}</td>
      <td>{ALLOWED_KIND_VI[loc.allowedRackKind] ?? loc.allowedRackKind}</td>
      <td className="right num">{loc.rackCapacity}</td>
      <td>{loc.requiresPlcPermission ? 'Cần' : '—'}</td>
      <td>{loc.enabled ? <Pill tone="ok">Đang bật</Pill> : <Pill tone="muted">Đã tắt</Pill>}</td>
      <td><Button size="sm" onClick={() => setEditing(true)}>Sửa</Button></td>
    </tr>
  )
}

function LocationForm({ initial, onDone, onCancel }: { initial?: Location; onDone: () => void; onCancel: () => void }) {
  const [f, setF] = useState({
    code: initial?.code ?? '', name: initial?.name ?? '', type: initial?.type ?? 'Buffer', capacity: String(initial?.rackCapacity ?? ''),
    allowed: initial?.allowedRackKind ?? 'Any', enabled: initial?.enabled ?? true, plc: initial?.requiresPlcPermission ?? false,
  })
  const [err, setErr] = useState<{ fields: Errors; general: string | null } | null>(null)
  const [busy, setBusy] = useState(false)
  const set = <K extends keyof typeof f>(k: K, v: (typeof f)[K]) => setF((x) => ({ ...x, [k]: v }))

  const save = async () => {
    const capacity = f.capacity.trim() === '' ? NaN : Number(f.capacity)
    if (Number.isNaN(capacity)) { setErr({ fields: { rackCapacity: 'Cần nhập sức chứa (số nguyên).' }, general: null }); return }
    const body: SaveLocationBody = { code: f.code.trim(), name: f.name.trim(), type: f.type, rackCapacity: capacity, allowedRackKind: f.allowed, enabled: f.enabled, requiresPlcPermission: f.plc }
    setBusy(true); setErr(null)
    try { await (initial ? api.saveLocation(body) : api.createLocation(body)); onDone() }
    catch (e) { setErr(fromApiError(e)) }
    finally { setBusy(false) }
  }
  const e = err?.fields ?? {}
  return (
    <div className="loc-form">
      <div className="form-grid">
        <Field label="Mã vị trí" error={e.code}><input value={f.code} onChange={(x) => set('code', x.target.value)} disabled={!!initial} maxLength={50} /></Field>
        <Field label="Tên vị trí" error={e.name}><input value={f.name} onChange={(x) => set('name', x.target.value)} maxLength={200} /></Field>
        <Field label="Loại vị trí" error={e.type}>
          <select value={f.type} onChange={(x) => set('type', x.target.value)} disabled={!!initial}>
            {Object.entries(LOCATION_TYPE_VI).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
          </select>
        </Field>
        <Field label="Sức chứa (số rack)" error={e.rackCapacity} hint="0–1000, theo thực tế."><input value={f.capacity} onChange={(x) => set('capacity', x.target.value)} inputMode="numeric" /></Field>
        <Field label="Loại rack chấp nhận" error={e.allowedRackKind}>
          <select value={f.allowed} onChange={(x) => set('allowed', x.target.value)}>
            {Object.entries(ALLOWED_KIND_VI).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
          </select>
        </Field>
        <div className="field switches">
          <Switch checked={f.plc} onChange={(v) => set('plc', v)} label="Cần PLC cho phép trước khi lấy/thả rack" />
          <Switch checked={f.enabled} onChange={(v) => set('enabled', v)} label="Đang sử dụng" />
        </div>
      </div>
      {err?.general && <div className="notice notice-bad" role="alert">{err.general}</div>}
      <div className="btn-row">
        <Button variant="primary" disabled={busy} onClick={() => void save()}>{initial ? 'Lưu vị trí' : 'Thêm vị trí'}</Button>
        <Button disabled={busy} onClick={onCancel}>Hủy bỏ</Button>
      </div>
    </div>
  )
}
