import { useState } from 'react'
import { useT } from '../i18n'
import { describeError } from '../i18n/vi'
import { Field } from '../pages/config/FormKit'
import { Modal } from './Modal'
import { Button } from './ui'

/** CR-055: the confirmation of a removal: what will happen, a mandatory reason, one call. */
export function RemovalDialog({ title, body, confirm, onClose, onConfirm }: { title: string; body: string; confirm: string; onClose: () => void; onConfirm: (reason: string) => Promise<void> }) {
  const t = useT()
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const run = async () => {
    setBusy(true); setError(null)
    try { await onConfirm(reason.trim()) } catch (e) { setError(describeError(e)); setBusy(false) }
  }
  return (
    <Modal title={title} onClose={onClose} footer={<><Button onClick={onClose}>{t('routing.closeDialog')}</Button><Button variant="danger" disabled={busy || !reason.trim()} onClick={() => void run()}>{confirm}</Button></>}>
      <p>{body}</p>
      {error && <div className="notice notice-error" role="alert">{error}</div>}
      <Field label={t('routing.removeReason')}><input value={reason} maxLength={500} onChange={(e) => setReason(e.target.value)} /></Field>
    </Modal>
  )
}
