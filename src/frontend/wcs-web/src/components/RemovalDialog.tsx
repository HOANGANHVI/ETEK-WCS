import { useState, type ReactNode } from 'react'
import { useT } from '../i18n'
import { describeError } from '../i18n/vi'
import { Field } from '../pages/config/FormKit'
import { Modal } from './Modal'
import { Button } from './ui'

/** CR-055: the confirmation of a removal: what will happen, a mandatory reason, one call. CR-056: with `typeToConfirm` the code has to be typed as well. */
export function RemovalDialog({ title, body, details, confirm, typeToConfirm, onClose, onConfirm }: { title: string; body: string; details?: ReactNode; confirm: string; typeToConfirm?: string; onClose: () => void; onConfirm: (reason: string, typed: string) => Promise<void> }) {
  const t = useT()
  const [reason, setReason] = useState('')
  const [typed, setTyped] = useState('')
  const matches = !typeToConfirm || typed.trim().toUpperCase() === typeToConfirm.toUpperCase()
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const run = async () => {
    setBusy(true); setError(null)
    try { await onConfirm(reason.trim(), typed.trim()) } catch (e) { setError(describeError(e)); setBusy(false) }
  }
  return (
    <Modal title={title} onClose={onClose} footer={<><Button onClick={onClose}>{t('routing.closeDialog')}</Button><Button variant="danger" disabled={busy || !reason.trim() || !matches} onClick={() => void run()}>{confirm}</Button></>}>
      <p>{body}</p>
      {details}
      {error && <div className="notice notice-error" role="alert">{error}</div>}
      {typeToConfirm && <Field label={t('routing.removeTypeCode', { code: typeToConfirm })}><input value={typed} maxLength={60} autoComplete="off" onChange={(e) => setTyped(e.target.value)} /></Field>}
      <Field label={t('routing.removeReason')}><input value={reason} maxLength={500} onChange={(e) => setReason(e.target.value)} /></Field>
    </Modal>
  )
}
