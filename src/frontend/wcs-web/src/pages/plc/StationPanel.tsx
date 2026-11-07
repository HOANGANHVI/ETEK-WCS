import { useCallback, useState } from 'react'
import { api } from '../../api/client'
import type { HandshakeState, IoPointState, PlcOverview } from '../../api/plcTypes'
import { Async, Button, Panel, Pill } from '../../components/ui'
import { Modal } from '../../components/Modal'
import { useApi } from '../../hooks/useApi'
import { APPROVAL_VI, DATATYPE_VI, DIRECTION_VI, PHASE_TONE, PHASE_VI } from '../../i18n/plc'
import { describeError } from '../../i18n/vi'
import { Field } from '../config/FormKit'
import { fromApiError, type Errors } from '../config/formUtils'
import { BLOCKED_SIGNALS, fmtTime, parseTyped } from './plcFormat'
import { ConfigPill, QualityPill, ValueCell } from './plcUi'

interface Props { stationId: string; isAdmin: boolean; runtime: PlcOverview['runtime']; handshake: HandshakeState | undefined; onClose: () => void }

type Dialog = { kind: 'address'; point: IoPointState } | { kind: 'write'; point: IoPointState } | { kind: 'sim'; point: IoPointState } | null

/** The 23 IO of one station. Everything here is read-only except the Admin actions, which are explicit and guarded by the backend. */
export function StationPanel({ stationId, isAdmin, runtime, handshake, onClose }: Props) {
  const io = useApi(useCallback(() => api.plcStationIo(stationId), [stationId]), ['StationIoChanged', 'PlcStatusChanged'])
  const [dialog, setDialog] = useState<Dialog>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const simulator = runtime.effectiveMode === 'Simulator'
  const canWrite = simulator || runtime.effectiveMode === 'RealReadWrite'
  const done = (msg: string) => { setDialog(null); setNotice(msg); io.reload() }

  return (
    <Panel title={`Trạm ${stationId} – 23 tín hiệu IO`} subtitle={io.data ? `${io.data.good} tốt / ${io.data.configured} đã cấu hình / ${io.data.total} IO` : undefined}
      actions={<Button size="sm" onClick={onClose}>Đóng</Button>} flush>
      {handshake && (
        <div className="hs-strip">
          <span><small>Pha quan sát (kỹ thuật)</small> <Pill tone={PHASE_TONE[handshake.phase] ?? 'muted'}>{PHASE_VI[handshake.phase] ?? handshake.phase}</Pill></span>
          <span><small>Handshake</small> <Pill tone={handshake.approval === 'NEEDS_CONFIRMATION' ? 'warn' : 'ok'} dot={false}>{APPROVAL_VI[handshake.approval] ?? handshake.approval}</Pill></span>
          <span className="muted">{handshake.signalsAwaitingConfirmation} tín hiệu chưa có quy tắc set/reset được duyệt (NEEDS_CONFIRMATION)</span>
          {handshake.lastError && isAdmin && <span className="text-bad">Lần ghi gần nhất bị từ chối: {handshake.lastError}</span>}
        </div>
      )}
      {notice && <div className="notice notice-ok" role="status">{notice}</div>}
      <Async state={io} isEmpty={(d) => d.points.length === 0} empty={{ title: 'Trạm chưa có IO point' }}>
        {(snap) => (
          <div className="table-wrap">
            <table className="grid">
              <thead>
                <tr>
                  <th>#</th><th>Tín hiệu</th><th>Chiều</th><th>Kiểu</th>
                  {isAdmin && <th>Địa chỉ PLC</th>}
                  {isAdmin && <th>Cấu hình</th>}
                  <th>Giá trị</th><th>Chất lượng</th><th>Cập nhật</th>
                  {isAdmin && <th>Thao tác</th>}
                </tr>
              </thead>
              <tbody>
                {snap.points.map((p) => {
                  const inbound = p.direction === 'PlcToSoftware'
                  const blocked = BLOCKED_SIGNALS.includes(p.signalName)
                  return (
                    <tr key={p.id}>
                      <td className="num muted">{p.seq}</td>
                      <td><strong>{p.signalName}</strong><br /><small className="muted">{p.group}</small></td>
                      <td className="nowrap">{DIRECTION_VI[p.direction]}</td>
                      <td>{DATATYPE_VI[p.dataType]}</td>
                      {isAdmin && <td className="mono">{p.address ?? <span className="muted">chưa gán</span>}</td>}
                      {isAdmin && <td><ConfigPill status={p.configStatus} /></td>}
                      <td><ValueCell p={p} /></td>
                      <td><QualityPill quality={p.quality} />{isAdmin && p.error && <small className="text-bad" title={p.error}> ⚠</small>}</td>
                      <td className="nowrap num">{fmtTime(p.lastUpdated)}</td>
                      {isAdmin && (
                        <td className="nowrap">
                          <Button size="sm" onClick={() => setDialog({ kind: 'address', point: p })}>Địa chỉ</Button>{' '}
                          {simulator && <><Button size="sm" onClick={() => setDialog({ kind: 'sim', point: p })}>Giả lập</Button>{' '}</>}
                          {!inbound && !blocked && canWrite && <Button size="sm" variant="danger" onClick={() => setDialog({ kind: 'write', point: p })}>Ghi thử</Button>}
                          {!inbound && blocked && <small className="muted" title="Safety/Interlock và Transfer/Departure Permit không bao giờ được ghi thử">🔒 chặn ghi</small>}
                        </td>
                      )}
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
      </Async>
      {dialog?.kind === 'address' && <AddressDialog point={dialog.point} onClose={() => setDialog(null)} onDone={done} />}
      {dialog?.kind === 'write' && <WriteDialog point={dialog.point} simulator={simulator} onClose={() => setDialog(null)} onDone={done} />}
      {dialog?.kind === 'sim' && <SimDialog point={dialog.point} onClose={() => setDialog(null)} onDone={done} />}
    </Panel>
  )
}

function AddressDialog({ point, onClose, onDone }: { point: IoPointState; onClose: () => void; onDone: (m: string) => void }) {
  const [address, setAddress] = useState(point.address ?? '')
  const [words, setWords] = useState('')
  const [encoding, setEncoding] = useState('ASCII')
  const [busy, setBusy] = useState(false)
  const [errors, setErrors] = useState<Errors>({})
  const [general, setGeneral] = useState<string | null>(null)
  const isString = point.dataType === 'String'

  const run = async (action: () => Promise<unknown>, msg: string) => {
    setBusy(true); setErrors({}); setGeneral(null)
    try { await action(); onDone(msg) } catch (e) { const r = fromApiError(e); setErrors(r.fields); setGeneral(r.general) } finally { setBusy(false) }
  }
  const hasAddress = point.configStatus !== 'NEEDS_CONFIGURATION'
  return (
    <Modal title={`Địa chỉ PLC: ${point.stationId}/${point.signalName}`} onClose={onClose} footer={<>
      <Button onClick={onClose}>Đóng</Button>
      {hasAddress && <Button variant="danger" disabled={busy} onClick={() => void run(() => api.plcClearAddress(point.id), 'Đã xóa địa chỉ.')}>Xóa địa chỉ</Button>}
      {hasAddress && point.configStatus !== 'APPROVED' && <Button disabled={busy} onClick={() => void run(() => api.plcApprovePoint(point.id), 'Đã ghi nhận xác nhận của kỹ sư PLC.')}>Ghi nhận đã xác nhận</Button>}
      <Button variant="primary" disabled={busy || !address.trim() || (isString && !words.trim())}
        onClick={() => void run(() => api.plcSetAddress(point.id, { address: address.trim(), wordLength: isString ? Number(words) : null, encoding: isString ? encoding : null }), 'Đã lưu địa chỉ (chưa được xác nhận).')}>Lưu địa chỉ</Button>
    </>}>
      <p className="dialog-hint">
        Chỉ nhập địa chỉ <strong>do kỹ sư PLC cấp</strong> cho riêng trạm này. Không dùng địa chỉ tham chiếu của file mẫu (ví dụ M9000, D1500: giống nhau ở mọi trạm).
        Địa chỉ không được trùng với tín hiệu khác trên cùng PLC; có thể trùng trên PLC khác. Thiết bị hỗ trợ: M, X, Y (bit) cho BOOL; D, W (word) cho INT/STRING. X là ngõ vào, chỉ dùng cho tín hiệu chiều PLC → Phần mềm. Địa chỉ X/Y/W được giữ đúng ký hiệu kỹ sư nhập; cơ số (bát phân/thập lục phân) phải được xác nhận ở tab cấu hình PLC trước.
      </p>
      {general && <div className="notice notice-bad" role="alert">{general}</div>}
      <div className="form-grid single">
        <Field label="Địa chỉ thiết bị" error={errors.address} hint="Ví dụ M100, Y10, D2000, W1A"><input value={address} onChange={(e) => setAddress(e.target.value)} placeholder={point.dataType === 'Bool' ? 'M… / X… / Y…' : 'D… / W…'} /></Field>
        {isString && <>
          <Field label="Độ dài (word)" error={errors.wordLength} hint="Số word chiếm chỗ, 2 ký tự/word"><input value={words} onChange={(e) => setWords(e.target.value)} inputMode="numeric" /></Field>
          <Field label="Encoding" error={errors.encoding} hint="Hiện chỉ hỗ trợ ASCII"><input value={encoding} onChange={(e) => setEncoding(e.target.value)} /></Field>
        </>}
      </div>
    </Modal>
  )
}

function WriteDialog({ point, simulator, onClose, onDone }: { point: IoPointState; simulator: boolean; onClose: () => void; onDone: (m: string) => void }) {
  const [text, setText] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const parsed = text.trim() === '' ? null : parseTyped(point.dataType, text)
  const submit = async () => {
    if (!parsed || !parsed.ok) return
    setBusy(true); setError(null)
    try { await api.plcTestWrite(point.stationId, point.signalName, parsed.value); onDone(`Đã ghi thử ${point.stationId}/${point.signalName}.`) }
    catch (e) { setError(describeError(e)) } finally { setBusy(false) }
  }
  return (
    <Modal title={`Ghi thử: ${point.stationId}/${point.signalName}`} onClose={onClose} footer={<>
      <Button onClick={onClose}>Bỏ qua</Button>
      <Button variant="danger" disabled={busy || !parsed || !parsed.ok} onClick={() => void submit()}>Xác nhận ghi</Button>
    </>}>
      <div className={simulator ? 'notice notice-info' : 'notice notice-warn'}>
        {simulator ? 'Chế độ mô phỏng: lệnh ghi chỉ tác động vào bộ nhớ mô phỏng.' : 'PLC THẬT: lệnh ghi này sẽ tác động vào thiết bị. Chỉ thực hiện theo quy trình thử đã được kỹ sư PLC cho phép.'}
        {' '}Mọi lần ghi thử đều được ghi nhật ký. Tín hiệu an toàn và permit không bao giờ được ghi.
      </div>
      {error && <div className="notice notice-bad" role="alert">{error}</div>}
      <div className="form-grid single">
        <Field label={`Giá trị (${DATATYPE_VI[point.dataType]})`} error={parsed && !parsed.ok ? parsed.message : undefined}><input value={text} onChange={(e) => setText(e.target.value)} /></Field>
      </div>
    </Modal>
  )
}

function SimDialog({ point, onClose, onDone }: { point: IoPointState; onClose: () => void; onDone: (m: string) => void }) {
  const [text, setText] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const parsed = text.trim() === '' && point.dataType !== 'String' ? null : parseTyped(point.dataType, text)
  const submit = async () => {
    if (!parsed || !parsed.ok) return
    setBusy(true); setError(null)
    try { await api.plcSimValue(point.plcId, point.stationId, point.signalName, parsed.value); onDone(`Đã đặt giá trị mô phỏng cho ${point.stationId}/${point.signalName}.`) }
    catch (e) { setError(describeError(e)) } finally { setBusy(false) }
  }
  return (
    <Modal title={`Giả lập phía PLC: ${point.stationId}/${point.signalName}`} onClose={onClose} footer={<>
      <Button onClick={onClose}>Bỏ qua</Button>
      <Button variant="primary" disabled={busy || !parsed || !parsed.ok} onClick={() => void submit()}>Đặt giá trị mô phỏng</Button>
    </>}>
      <p className="dialog-hint">Đặt giá trị như thể PLC mô phỏng đưa ra. Không có thiết bị thật nào bị tác động.</p>
      {error && <div className="notice notice-bad" role="alert">{error}</div>}
      <div className="form-grid single">
        <Field label={`Giá trị (${DATATYPE_VI[point.dataType]})`} error={parsed && !parsed.ok ? parsed.message : undefined}><input value={text} onChange={(e) => setText(e.target.value)} /></Field>
      </div>
    </Modal>
  )
}
