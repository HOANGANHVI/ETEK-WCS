import { useCallback, useState } from 'react'
import { api } from '../api/client'
import { ApiRequestError } from '../api/errors'
import type { TransportTask } from '../api/types'
import { Button, Loading } from '../components/ui'
import { Modal } from '../components/Modal'
import { useApi } from '../hooks/useApi'
import { describeError, flowInfo, taskCode, TASK_STATE_VI } from '../i18n/vi'
import { Field } from './config/FormKit'

function useAction(onDone: () => void, onStale: () => void) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const run = async (action: () => Promise<unknown>) => {
    setBusy(true); setError(null)
    try { await action(); onDone() }
    catch (e) {
      setError(describeError(e))
      // The task moved on (or the account lost the right) while the dialog was open: refresh what the screen shows.
      if (e instanceof ApiRequestError && [403, 404, 409].includes(e.status)) onStale()
    } finally { setBusy(false) }
  }
  return { busy, error, run }
}

export function CreateTaskDialog({ onClose, onCreated }: { onClose: () => void; onCreated: (t: TransportTask) => void }) {
  const flows = useApi(useCallback(() => api.flows(), []))
  const [flow, setFlow] = useState('')
  const [station, setStation] = useState('')
  // One key per opened dialog: double-clicking "Tạo" cannot create two tasks (the API is idempotent on this key).
  const [requestKey] = useState(() => crypto.randomUUID())
  const stations = flows.data?.find((f) => f.flow === flow)?.stations ?? []
  const a = useAction(() => undefined, () => undefined)

  return (
    <Modal title="Tạo nhiệm vụ mới" onClose={onClose} footer={<>
      <Button onClick={onClose}>Bỏ qua</Button>
      <Button variant="primary" disabled={a.busy || !flow || !station}
        onClick={() => void a.run(async () => { const t = await api.createTask(flow, station, requestKey); onCreated(t) })}>Tạo nhiệm vụ</Button>
    </>}>
      {a.error && <div className="notice notice-bad" role="alert">{a.error}</div>}
      {flows.loading && !flows.data ? <Loading /> : flows.error && !flows.data ? <div className="notice notice-bad">{flows.error}</div> : (
        <div className="form-grid single">
          <Field label="Quy trình" hint="Chỉ các quy trình đã được phê duyệt triển khai.">
            <select value={flow} onChange={(e) => { setFlow(e.target.value); setStation('') }}>
              <option value="">— Chọn quy trình —</option>
              {(flows.data ?? []).map((f) => <option key={f.flow} value={f.flow}>{flowInfo(f.flow).code} · {flowInfo(f.flow).label}</option>)}
            </select>
          </Field>
          <Field label="Trạm yêu cầu" hint="Hệ thống tự chọn AGV, rack nguồn và vị trí đích theo quy trình.">
            <select value={station} disabled={!flow} onChange={(e) => setStation(e.target.value)}>
              <option value="">— Chọn trạm —</option>
              {stations.map((s) => <option key={s} value={s}>{s}</option>)}
            </select>
          </Field>
        </div>
      )}
    </Modal>
  )
}

export function EditTaskDialog({ task, stationCode, onClose, onChanged }: {
  task: TransportTask; stationCode: string; onClose: () => void; onChanged: () => void
}) {
  const flows = useApi(useCallback(() => api.flows(), []))
  const currentFlow = flows.data?.find((f) => f.flow.toLowerCase() === task.flow.toLowerCase())?.flow ?? task.flow
  const [flow, setFlow] = useState(currentFlow)
  const [station, setStation] = useState(stationCode)
  const stations = flows.data?.find((f) => f.flow === flow)?.stations ?? []
  const a = useAction(() => { onChanged(); onClose() }, onChanged)
  const unchanged = flow === currentFlow && station === stationCode
  return (
    <Modal title={`Sửa nhiệm vụ ${taskCode(task.id)}`} onClose={onClose} footer={<>
      <Button onClick={onClose}>Bỏ qua</Button>
      <Button variant="primary" disabled={a.busy || !flow || !station || unchanged}
        onClick={() => void a.run(() => api.updateTask(task.id, flow, station, task.state))}>Lưu thay đổi</Button>
    </>}>
      <p className="dialog-hint">Sửa toàn bộ thông tin yêu cầu khi nhiệm vụ còn chờ và chưa được AGV/RCS tiếp nhận (hiện: {TASK_STATE_VI[task.state]}).
        AGV, loại rack và rack nguồn sẽ được xác định lại theo quy trình mới; nếu không có rack phù hợp, nhiệm vụ giữ nguyên.</p>
      {a.error && <div className="notice notice-bad" role="alert">{a.error}</div>}
      <div className="form-grid single">
        <Field label="Quy trình">
          <select value={flow} disabled={flows.loading && !flows.data} onChange={(e) => {
            const next = e.target.value; setFlow(next)
            const allowed = flows.data?.find((f) => f.flow === next)?.stations ?? []
            if (!allowed.includes(station)) setStation(allowed[0] ?? '')
          }}>
            {(flows.data ?? [{ flow: currentFlow, rackKind: '', stations: [] }]).map((f) => <option key={f.flow} value={f.flow}>{flowInfo(f.flow).code} · {flowInfo(f.flow).label}</option>)}
          </select>
        </Field>
        <Field label="Trạm yêu cầu">
          <select value={station} onChange={(e) => setStation(e.target.value)} disabled={flows.loading && !flows.data}>
            {(stations.length ? stations : [stationCode]).map((s) => <option key={s} value={s}>{s}</option>)}
          </select>
        </Field>
      </div>
    </Modal>
  )
}

export function DeleteTaskDialog({ task, onClose, onChanged }: { task: TransportTask; onClose: () => void; onChanged: () => void }) {
  const a = useAction(() => { onChanged(); onClose() }, onChanged)
  return (
    <Modal title={`Xóa nhiệm vụ ${taskCode(task.id)}`} onClose={onClose} footer={<>
      <Button onClick={onClose}>Bỏ qua</Button>
      <Button variant="danger" disabled={a.busy} onClick={() => void a.run(() => api.deleteTask(task.id, task.state))}>Xác nhận xóa</Button>
    </>}>
      {a.error && <div className="notice notice-bad" role="alert">{a.error}</div>}
      <p className="dialog-hint">
        Nhiệm vụ {flowInfo(task.flow).code} đang ở trạng thái “{TASK_STATE_VI[task.state]}” và chưa được AGV/RCS tiếp nhận.
        Nhiệm vụ sẽ bị <strong>xóa hẳn</strong> khỏi hệ thống (không phải hủy) và rack đã giữ chỗ được giải phóng. Việc xóa được ghi vào nhật ký thao tác
        (mã nhiệm vụ, người xóa, thời gian, thông tin nhiệm vụ) và nhật ký này vẫn còn sau khi xóa.
      </p>
    </Modal>
  )
}
