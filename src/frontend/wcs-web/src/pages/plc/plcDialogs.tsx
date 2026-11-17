import { useState } from 'react'
import { api } from '../../api/client'
import type { IoPointState } from '../../api/plcTypes'
import { Button } from '../../components/ui'
import { Modal } from '../../components/Modal'
import { DATATYPE_VI } from '../../i18n/plc'
import { describeError } from '../../i18n/vi'
import { Field } from '../config/FormKit'
import { fromApiError, type Errors } from '../config/formUtils'
import { parseTyped } from './plcFormat'

/** The part of an IO point the Admin dialogs need (both the live IO state and the IO-mapping row satisfy it). */
export type DialogPoint = Pick<IoPointState, 'id' | 'plcId' | 'stationId' | 'signalName' | 'dataType' | 'address' | 'configStatus'>

export function AddressDialog({ point, onClose, onDone }: { point: DialogPoint; onClose: () => void; onDone: (m: string) => void }) {
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
      <p className="dialog-hint">Chỉ nhập địa chỉ do kỹ sư PLC cấp cho riêng trạm này.</p>
      {general && <div className="notice notice-bad" role="alert">{general}</div>}
      <div className="form-grid single">
        <Field label="Địa chỉ thiết bị" error={errors.address} hint="Ví dụ M100, D2000, W1A"><input value={address} onChange={(e) => setAddress(e.target.value)} placeholder={point.dataType === 'Bool' ? 'M…' : 'D… / W…'} /></Field>
        {isString && <>
          <Field label="Độ dài (word)" error={errors.wordLength} hint="Số word chiếm chỗ, 2 ký tự/word"><input value={words} onChange={(e) => setWords(e.target.value)} inputMode="numeric" /></Field>
          <Field label="Encoding" error={errors.encoding} hint="Hiện chỉ hỗ trợ ASCII"><input value={encoding} onChange={(e) => setEncoding(e.target.value)} /></Field>
        </>}
      </div>
    </Modal>
  )
}

export function WriteDialog({ point, simulator, onClose, onDone }: { point: DialogPoint; simulator: boolean; onClose: () => void; onDone: (m: string) => void }) {
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
      {!simulator && (
        <div className="notice notice-warn">
          PLC THẬT: lệnh ghi này sẽ tác động vào thiết bị. Chỉ thực hiện theo quy trình thử đã được kỹ sư PLC cho phép. Mọi lần ghi thử đều được ghi nhật ký. Tín hiệu an toàn và permit không bao giờ được ghi.
        </div>
      )}
      {error && <div className="notice notice-bad" role="alert">{error}</div>}
      <div className="form-grid single">
        <Field label={`Giá trị (${DATATYPE_VI[point.dataType]})`} error={parsed && !parsed.ok ? parsed.message : undefined}><input value={text} onChange={(e) => setText(e.target.value)} /></Field>
      </div>
    </Modal>
  )
}

export function SimDialog({ point, onClose, onDone }: { point: DialogPoint; onClose: () => void; onDone: (m: string) => void }) {
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
    <Modal title={`Mô phỏng phía PLC: ${point.stationId}/${point.signalName}`} onClose={onClose} footer={<>
      <Button onClick={onClose}>Bỏ qua</Button>
      <Button variant="primary" disabled={busy || !parsed || !parsed.ok} onClick={() => void submit()}>Đặt giá trị mô phỏng</Button>
    </>}>
      {error && <div className="notice notice-bad" role="alert">{error}</div>}
      <div className="form-grid single">
        <Field label={`Giá trị (${DATATYPE_VI[point.dataType]})`} error={parsed && !parsed.ok ? parsed.message : undefined}><input value={text} onChange={(e) => setText(e.target.value)} /></Field>
      </div>
    </Modal>
  )
}
