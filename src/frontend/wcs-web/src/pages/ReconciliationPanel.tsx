import { useCallback, useState } from 'react'
import { api } from '../api/client'
import type { Location, TransportTask } from '../api/types'
import { Async, Button, Pill } from '../components/ui'
import { useAuth } from '../auth/AuthContext'
import { useApi } from '../hooks/useApi'
import { formatDateTime, formatTime } from '../i18n/format'
import { describeError, HISTORY_EVENT_VI, reasonVi } from '../i18n/vi'

/**
 * CR-008B-3: Admin reconciliation of a task whose physical outcome is not proven (for example a cancellation that RCS never confirmed).
 * It shows what WCS knows (RCS request / task code, retry count, last RCS events, reservations, rack and location flags) and offers exactly two actions:
 *  - "verified": the Admin states the REAL location of the rack -> the existing audited reconciliation releases the reservations;
 *  - "unresolved": the check could not establish it -> a note is recorded and EVERYTHING stays held.
 * There is deliberately no "force complete" / "force cancel".
 */
export function ReconciliationPanel({ task, onChanged }: { task: TransportTask; onChanged: () => void }) {
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
        setMessage('Đã đối soát: vị trí thực tế được ghi nhận và giữ chỗ được giải phóng.')
      } else {
        await api.reconciliationCheck(task.id, note.trim())
        setMessage('Đã ghi nhận kết quả kiểm tra. Mọi giữ chỗ và trạng thái rack/vị trí được giữ nguyên.')
      }
      setNote(''); setConfirmed(false); detail.reload(); onChanged()
    } catch (e) { setError(describeError(e)) }
    finally { setBusy(false) }
  }
  const valid = note.trim().length >= 3 && (mode === 'unresolved' || (location !== '' && confirmed))

  return (
    <section className="detail-sec">
      <h3>Đối soát vật lý (Admin)</h3>
      <div className="notice notice-warn">
        Trạng thái <strong>Thất bại</strong> của nhiệm vụ này <strong>không phải</strong> bằng chứng RCS đã hủy nhiệm vụ. Rack và giữ chỗ đang được giữ nguyên cho đến khi Admin xác nhận vị trí thực tế.
      </div>
      <Async state={detail} empty={{ title: 'Không có dữ liệu đối soát' }}>
        {(d) => (
          <>
            <dl className="kv2">
              <dt>Mã yêu cầu RCS</dt><dd className="mono">{d.task.rcsRequestCode}</dd>
              <dt>Mã nhiệm vụ RCS</dt><dd className="mono">{d.task.rcsTaskCode ?? 'RCS chưa tiếp nhận'}</dd>
              <dt>Lý do hiện tại</dt><dd>{reasonVi(d.task.stateReason)}</dd>
              <dt>Số lần thử RCS</dt><dd>{d.rcsRetryCount}{d.rcsLastAttemptAt ? ` · lần cuối ${formatDateTime(d.rcsLastAttemptAt)}` : ''}</dd>
              <dt>Rack</dt><dd>{d.rack ? `${d.rack.status} · WCS ghi nhận tại ${d.rack.currentLocationCode ?? '—'} (xác nhận lần cuối ${formatDateTime(d.rack.lastConfirmedAt)})` : '—'}</dd>
              <dt>Giữ chỗ đang giữ</dt>
              <dd>{d.reservations.length === 0 ? 'Không' : d.reservations.map((r, i) => <Pill key={i} tone="info" dot={false}>{r.kind === 'SourceRack' ? 'Rack nguồn' : 'Vị trí đích'} {r.locationCode ?? ''}</Pill>)}</dd>
              <dt>Vị trí cần đối soát</dt>
              <dd>{d.locations.length === 0 ? '—' : d.locations.map((l) => <Pill key={l.code} tone={l.reconciliationRequired ? 'bad' : 'muted'} dot={false}>{l.code}{l.reconciliationRequired ? ' · cần đối soát' : ''}</Pill>)}</dd>
            </dl>
            <h4>Sự kiện RCS / đối soát gần nhất</h4>
            {d.rcsEvents.length === 0 ? <p className="muted">Chưa có sự kiện.</p> : (
              <ul className="event-list">
                {d.rcsEvents.map((h) => (
                  <li key={h.id}><span className="num muted">{formatTime(h.occurredAt)}</span> <span>{HISTORY_EVENT_VI[h.event] ?? h.event}{h.detail ? ` · ${reasonVi(h.detail)}` : ''}</span></li>
                ))}
              </ul>
            )}

            {d.canReconcile ? (
              <div className="form-grid single">
                <fieldset>
                  <legend>Kết quả kiểm tra thực tế</legend>
                  <label><input type="radio" name="recon-mode" checked={mode === 'unresolved'} onChange={() => setMode('unresolved')} /> Chưa xác định được vị trí rack — <em>tiếp tục giữ mọi tài nguyên</em></label><br />
                  <label><input type="radio" name="recon-mode" checked={mode === 'verified'} onChange={() => setMode('verified')} /> Đã xác minh vị trí thực tế của rack — giải phóng giữ chỗ</label>
                </fieldset>
                {mode === 'verified' && (
                  <>
                    <label className="field"><span className="field-label">Vị trí thực tế của rack</span>
                      <select value={location} onChange={(e) => setLocation(e.target.value)}>
                        <option value="">— Chọn vị trí —</option>
                        {(locations.data ?? [] as Location[]).map((l) => <option key={l.code} value={l.code}>{l.code}</option>)}
                      </select>
                    </label>
                    <label><input type="checkbox" checked={confirmed} onChange={(e) => setConfirmed(e.target.checked)} /> Tôi đã kiểm tra trực tiếp vị trí rack ngoài hiện trường</label>
                  </>
                )}
                <label className="field"><span className="field-label">Ghi chú kiểm tra (bắt buộc)</span>
                  <textarea rows={3} maxLength={1000} value={note} onChange={(e) => setNote(e.target.value)} placeholder="Ai kiểm tra, kiểm tra gì, thấy gì (kể cả trạng thái trên RCS)" />
                </label>
                <div className="btn-row">
                  <Button variant={mode === 'verified' ? 'danger' : 'primary'} disabled={busy || !valid} onClick={() => void submit()}>
                    {mode === 'verified' ? 'Xác nhận vị trí và đối soát' : 'Ghi nhận: chưa xác định'}
                  </Button>
                </div>
              </div>
            ) : <p className="muted">Nhiệm vụ chưa kết thúc hoặc không còn cần đối soát.</p>}
            {message && <div className="notice notice-info">{message}</div>}
            {error && <div className="notice notice-bad" role="alert">{error}</div>}
          </>
        )}
      </Async>
    </section>
  )
}
