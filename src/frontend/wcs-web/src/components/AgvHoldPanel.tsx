import { useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import type { Agv } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { formatDateTime } from '../i18n/format'
import { useT } from '../i18n'
import { describeReasonCode } from '../i18n/logCatalog'
import { describeError, taskCode } from '../i18n/vi'
import { Button } from './ui'

/**
 * CR-008B-5: an AGV on dispatch hold takes no new task because the physical result of its last task is not proven (D-7).
 * Everyone sees the banner and the reason; ONLY an Admin can lift the hold and only after BOTH confirmations (D-8). The server enforces both rules again.
 */
export function AgvHoldPanel({ agv, onChanged, compact }: { agv: Agv; onChanged?: () => void; compact?: boolean }) {
  const t = useT()
  const { user } = useAuth()
  const isAdmin = user?.role === 'Admin'
  const [inventory, setInventory] = useState(false)
  const [physical, setPhysical] = useState(false)
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  if (!agv.dispatchHold) return null

  const release = async () => {
    setBusy(true); setError(null)
    try {
      await api.releaseAgvHold(agv.id, inventory, physical, note.trim())
      setInventory(false); setPhysical(false); setNote('')
      onChanged?.()
    } catch (e) { setError(describeError(e)) }
    finally { setBusy(false) }
  }
  const valid = inventory && physical && note.trim().length >= 3

  return (
    <div className="notice notice-bad agv-hold" role="alert">
      <strong>{t('agv.holdTitle', { code: agv.code })}</strong>
      <div>{t('agv.holdReason', { reason: describeReasonCode(agv.dispatchHoldReason) || '—', since: agv.dispatchHoldAt ? t('agv.holdSince', { time: formatDateTime(agv.dispatchHoldAt) }) : '' })}</div>
      {agv.dispatchHoldTaskId && <div>{t('agv.holdTask')} <Link to={`/tasks?id=${agv.dispatchHoldTaskId}`} className="mono">{taskCode(agv.dispatchHoldTaskId)}</Link></div>}
      {!compact && <small>{t('agv.holdQueued', { code: agv.code })}</small>}
      {!compact && isAdmin && (
        <div className="form-grid single">
          <label><input type="checkbox" checked={inventory} onChange={(e) => setInventory(e.target.checked)} /> {t('agv.holdChkInventory')}</label>
          <label><input type="checkbox" checked={physical} onChange={(e) => setPhysical(e.target.checked)} /> {t('agv.holdChkPhysical')}</label>
          <label className="field"><span className="field-label">{t('agv.holdNote')}</span>
            <textarea rows={2} maxLength={1000} value={note} onChange={(e) => setNote(e.target.value)} placeholder={t('agv.holdNotePh')} />
          </label>
          <div className="btn-row"><Button variant="danger" disabled={busy || !valid} onClick={() => void release()}>{t('agv.holdRelease')}</Button></div>
          {error && <div className="notice notice-bad" role="alert">{error}</div>}
        </div>
      )}
      {!compact && !isAdmin && <small>{t('agv.holdViewOnly')}</small>}
    </div>
  )
}
