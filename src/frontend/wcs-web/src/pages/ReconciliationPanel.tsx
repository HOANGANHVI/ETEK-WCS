import { useCallback, useState } from 'react'
import { api } from '../api/client'
import type { Location, TransportTask } from '../api/types'
import { Async, Button, Pill } from '../components/ui'
import { useAuth } from '../auth/AuthContext'
import { useApi } from '../hooks/useApi'
import { useT } from '../i18n'
import { Rich } from '../components/Rich'
import { formatDateTime, formatTime } from '../i18n/format'
import { describeError, HISTORY_EVENT_VI, RACK_STATUS_VI, reasonVi } from '../i18n/vi'

/**
 * CR-008B-3: Admin reconciliation of a task whose physical outcome is not proven (for example a cancellation that RCS never confirmed).
 * It shows what WCS knows (RCS request / task code, retry count, last RCS events, reservations, rack and location flags) and offers exactly two actions:
 *  - "verified": the Admin states the REAL location of the rack -> the existing audited reconciliation releases the reservations;
 *  - "unresolved": the check could not establish it -> a note is recorded and EVERYTHING stays held.
 * There is deliberately no "force complete" / "force cancel".
 */
export function ReconciliationPanel({ task, onChanged }: { task: TransportTask; onChanged: () => void }) {
  const t = useT()
  const { user } = useAuth()
  const detail = useApi(useCallback(() => api.reconciliationDetail(task.id), [task.id]))
  const locations = useApi(useCallback(() => api.locations(), []))
  const [mode, setMode] = useState<'unresolved' | 'verified'>('unresolved')   // the safe choice is the default
  const [location, setLocation] = useState('')
  const [note, setNote] = useState('')
  const [confirmed, setConfirmed] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)

  const submit = async () => {
    setBusy(true); setError(null); setMessage(null)
    try {
      if (mode === 'verified') {
        await api.reconcileTask(task.id, location, user?.username ?? 'admin', note.trim())
        setMessage(t('tasks.rcDone'))
      } else {
        await api.reconciliationCheck(task.id, note.trim())
        setMessage(t('tasks.rcRecorded'))
      }
      setNote(''); setConfirmed(false); detail.reload(); onChanged()
    } catch (e) { setError(describeError(e)) }
    finally { setBusy(false) }
  }
  const valid = note.trim().length >= 3 && (mode === 'unresolved' || (location !== '' && confirmed))

  return (
    <section className="detail-sec">
      <h3>{t('tasks.rcSection')}</h3>
      <div className="notice notice-warn">
        <Rich text={t('tasks.rcWarn1')} parts={{ bold1: <strong>{t('tasks.rcFailed')}</strong>, bold2: <strong>{t('tasks.rcNotProof')}</strong> }} />
      </div>
      <Async state={detail} empty={{ title: t('tasks.rcNoData') }}>
        {(d) => (
          <>
            <dl className="kv2">
              <dt>{t('tasks.rowRcsRequest')}</dt><dd className="mono">{d.task.rcsRequestCode}</dd>
              <dt>{t('tasks.rowRcsTask')}</dt><dd className="mono">{d.task.rcsTaskCode ?? t('tasks.rcsNotAccepted')}</dd>
              <dt>{t('tasks.rcReason')}</dt><dd>{reasonVi(d.task.stateReason)}</dd>
              <dt>{t('tasks.rcRetries')}</dt><dd>{d.rcsRetryCount}{d.rcsLastAttemptAt ? t('tasks.rcLastAttempt', { time: formatDateTime(d.rcsLastAttemptAt) }) : ''}</dd>
              <dt>{t('tasks.rcRack')}</dt><dd>{d.rack ? t('tasks.rcRackInfo', { status: RACK_STATUS_VI[d.rack.status] ?? d.rack.status, location: d.rack.currentLocationCode ?? '—', time: formatDateTime(d.rack.lastConfirmedAt) }) : '—'}</dd>
              <dt>{t('tasks.rcReservations')}</dt>
              <dd>{d.reservations.length === 0 ? t('tasks.rcNoReservations') : d.reservations.map((r, i) => <Pill key={i} tone="info" dot={false}>{r.kind === 'SourceRack' ? t('tasks.rcSourceRack') : t('tasks.rcDestination')} {r.locationCode ?? ''}</Pill>)}</dd>
              <dt>{t('tasks.rcLocations')}</dt>
              <dd>{d.locations.length === 0 ? '—' : d.locations.map((l) => <Pill key={l.code} tone={l.reconciliationRequired ? 'bad' : 'muted'} dot={false}>{l.code}{l.reconciliationRequired ? t('tasks.rcNeedsRecon') : ''}</Pill>)}</dd>
            </dl>
            <h4>{t('tasks.rcEvents')}</h4>
            {d.rcsEvents.length === 0 ? <p className="muted">{t('tasks.rcNoEvents')}</p> : (
              <ul className="event-list">
                {d.rcsEvents.map((h) => (
                  <li key={h.id}><span className="num muted">{formatTime(h.occurredAt)}</span> <span>{HISTORY_EVENT_VI[h.event] ?? h.event}{h.detail ? ` · ${reasonVi(h.detail)}` : ''}</span></li>
                ))}
              </ul>
            )}

            {d.canReconcile ? (
              <div className="form-grid single">
                <fieldset>
                  <legend>{t('tasks.rcResult')}</legend>
                  <label><input type="radio" name="recon-mode" checked={mode === 'unresolved'} onChange={() => setMode('unresolved')} /> {t('tasks.rcUnresolved')}<em>{t('tasks.rcKeepHolding')}</em></label><br />
                  <label><input type="radio" name="recon-mode" checked={mode === 'verified'} onChange={() => setMode('verified')} /> {t('tasks.rcVerified')}</label>
                </fieldset>
                {mode === 'verified' && (
                  <>
                    <label className="field"><span className="field-label">{t('tasks.rcActualLocation')}</span>
                      <select value={location} onChange={(e) => setLocation(e.target.value)}>
                        <option value="">{t('tasks.rcChooseLocation')}</option>
                        {(locations.data ?? [] as Location[]).map((l) => <option key={l.code} value={l.code}>{l.code}</option>)}
                      </select>
                    </label>
                    <label><input type="checkbox" checked={confirmed} onChange={(e) => setConfirmed(e.target.checked)} /> {t('tasks.rcCheckedOnSite')}</label>
                  </>
                )}
                <label className="field"><span className="field-label">{t('tasks.rcNote')}</span>
                  <textarea rows={3} maxLength={1000} value={note} onChange={(e) => setNote(e.target.value)} placeholder={t('tasks.rcNotePh')} />
                </label>
                <div className="btn-row">
                  <Button variant={mode === 'verified' ? 'danger' : 'primary'} disabled={busy || !valid} onClick={() => void submit()}>
                    {mode === 'verified' ? t('tasks.rcConfirmRecon') : t('tasks.rcRecordUnresolved')}
                  </Button>
                </div>
              </div>
            ) : <p className="muted">{t('tasks.rcNotNeeded')}</p>}
            {message && <div className="notice notice-info">{message}</div>}
            {error && <div className="notice notice-bad" role="alert">{error}</div>}
          </>
        )}
      </Async>
    </section>
  )
}
