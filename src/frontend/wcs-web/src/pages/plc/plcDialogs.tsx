import { useState } from 'react'
import { api } from '../../api/client'
import type { IoPointState } from '../../api/plcTypes'
import { Button } from '../../components/ui'
import { Modal } from '../../components/Modal'
import { DATATYPE_VI } from '../../i18n/plc'
import { useT } from '../../i18n'
import { describeError } from '../../i18n/vi'
import { Field } from '../config/FormKit'
import { fromApiError, type Errors } from '../config/formUtils'
import { parseTyped } from './plcFormat'

/** The part of an IO point the Admin dialogs need (both the live IO state and the IO-mapping row satisfy it). */
export type DialogPoint = Pick<IoPointState, 'id' | 'plcId' | 'stationId' | 'signalName' | 'dataType' | 'address' | 'configStatus'>

export function AddressDialog({ point, onClose, onDone }: { point: DialogPoint; onClose: () => void; onDone: (m: string) => void }) {
  const t = useT()
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
    <Modal title={t('plc.addrTitle', { station: point.stationId, signal: point.signalName })} onClose={onClose} footer={<>
      <Button onClick={onClose}>{t('plc.close')}</Button>
      {hasAddress && <Button variant="danger" disabled={busy} onClick={() => void run(() => api.plcClearAddress(point.id), t('plc.addressCleared'))}>{t('plc.clearAddress')}</Button>}
      {hasAddress && point.configStatus !== 'APPROVED' && <Button disabled={busy} onClick={() => void run(() => api.plcApprovePoint(point.id), t('plc.approvalRecorded'))}>{t('plc.recordApproval')}</Button>}
      <Button variant="primary" disabled={busy || !address.trim() || (isString && !words.trim())}
        onClick={() => void run(() => api.plcSetAddress(point.id, { address: address.trim(), wordLength: isString ? Number(words) : null, encoding: isString ? encoding : null }), t('plc.addressSaved'))}>{t('plc.saveAddress')}</Button>
    </>}>
      <p className="dialog-hint">{t('plc.addrHint')}</p>
      {general && <div className="notice notice-bad" role="alert">{general}</div>}
      <div className="form-grid single">
        <Field label={t('plc.deviceAddress')} error={errors.address} hint={t('plc.deviceAddressHint')}><input value={address} onChange={(e) => setAddress(e.target.value)} placeholder={point.dataType === 'Bool' ? 'M…' : 'D… / W…'} /></Field>
        {isString && <>
          <Field label={t('plc.wordLength')} error={errors.wordLength} hint={t('plc.wordLengthHint')}><input value={words} onChange={(e) => setWords(e.target.value)} inputMode="numeric" /></Field>
          <Field label="Encoding" error={errors.encoding} hint={t('plc.encodingHint')}><input value={encoding} onChange={(e) => setEncoding(e.target.value)} /></Field>
        </>}
      </div>
    </Modal>
  )
}

export function WriteDialog({ point, simulator, onClose, onDone }: { point: DialogPoint; simulator: boolean; onClose: () => void; onDone: (m: string) => void }) {
  const t = useT()
  const [text, setText] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const parsed = text.trim() === '' ? null : parseTyped(point.dataType, text)
  const submit = async () => {
    if (!parsed || !parsed.ok) return
    setBusy(true); setError(null)
    try { await api.plcTestWrite(point.stationId, point.signalName, parsed.value); onDone(t('plc.testWritten', { station: point.stationId, signal: point.signalName })) }
    catch (e) { setError(describeError(e)) } finally { setBusy(false) }
  }
  return (
    <Modal title={t('plc.writeTitle', { station: point.stationId, signal: point.signalName })} onClose={onClose} footer={<>
      <Button onClick={onClose}>{t('plc.skip')}</Button>
      <Button variant="danger" disabled={busy || !parsed || !parsed.ok} onClick={() => void submit()}>{t('plc.confirmWrite')}</Button>
    </>}>
      {!simulator && (
        <div className="notice notice-warn">
          {t('plc.realWriteWarn')}
        </div>
      )}
      {error && <div className="notice notice-bad" role="alert">{error}</div>}
      <div className="form-grid single">
        <Field label={t('plc.valueLabel', { type: DATATYPE_VI[point.dataType] })} error={parsed && !parsed.ok ? parsed.message : undefined}><input value={text} onChange={(e) => setText(e.target.value)} /></Field>
      </div>
    </Modal>
  )
}

export function SimDialog({ point, onClose, onDone }: { point: DialogPoint; onClose: () => void; onDone: (m: string) => void }) {
  const t = useT()
  const [text, setText] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const parsed = text.trim() === '' && point.dataType !== 'String' ? null : parseTyped(point.dataType, text)
  const submit = async () => {
    if (!parsed || !parsed.ok) return
    setBusy(true); setError(null)
    try { await api.plcSimValue(point.plcId, point.stationId, point.signalName, parsed.value); onDone(t('plc.simValueSet', { station: point.stationId, signal: point.signalName })) }
    catch (e) { setError(describeError(e)) } finally { setBusy(false) }
  }
  return (
    <Modal title={t('plc.simTitle', { station: point.stationId, signal: point.signalName })} onClose={onClose} footer={<>
      <Button onClick={onClose}>{t('plc.skip')}</Button>
      <Button variant="primary" disabled={busy || !parsed || !parsed.ok} onClick={() => void submit()}>{t('plc.setSimValue')}</Button>
    </>}>
      {error && <div className="notice notice-bad" role="alert">{error}</div>}
      <div className="form-grid single">
        <Field label={t('plc.valueLabel', { type: DATATYPE_VI[point.dataType] })} error={parsed && !parsed.ok ? parsed.message : undefined}><input value={text} onChange={(e) => setText(e.target.value)} /></Field>
      </div>
    </Modal>
  )
}
