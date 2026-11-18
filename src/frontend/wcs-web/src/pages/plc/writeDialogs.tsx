import { useCallback, useState, type ReactNode } from 'react'
import { api } from '../../api/client'
import { ApiRequestError } from '../../api/errors'
import type { IoPointConfig } from '../../api/plcTypes'
import { useAuth } from '../../auth/AuthContext'
import { Button, Pill } from '../../components/ui'
import { Modal } from '../../components/Modal'
import { useApi } from '../../hooks/useApi'
import { useT } from '../../i18n'
import { formatDateTime } from '../../i18n/format'
import { DATATYPE_VI } from '../../i18n/plc'
import { WRITE_TONE } from './plcFormat'
import { describeError } from '../../i18n/vi'

const MAX_ASCII = 0x7f
const INT_MIN = -32768, INT_MAX = 32767

type Details = Pick<IoPointConfig, 'plcId' | 'stationId' | 'signalName' | 'actualAddress' | 'dataType'>

function PointDetails({ p, extra }: { p: Details; extra?: ReactNode }) {
  const t = useT()
  return (
    <dl className="stat-grid">
      <div><dt>PLC</dt><dd>{p.plcId}</dd></div>
      <div><dt>{t('plc.station')}</dt><dd>{p.stationId}</dd></div>
      <div><dt>{t('plc.colSignalName')}</dt><dd>{p.signalName}</dd></div>
      <div><dt>{t('plc.colActualAddress')}</dt><dd className="mono">{p.actualAddress ?? '—'}</dd></div>
      <div><dt>{t('plc.colDataType')}</dt><dd>{DATATYPE_VI[p.dataType]}</dd></div>
      {extra}
    </dl>
  )
}

/** The Admin decision on WRITING one point. Independent of reading (Pollable / read confirmation) and of the handshake. It sends nothing to a PLC. */
export function WriteApprovalDialog({ point, onClose, onDone }: { point: IoPointConfig; onClose: () => void; onDone: (m: string) => void }) {
  const t = useT()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const approved = point.writeApproval === 'APPROVED'
  const decide = async (decision: 'Approve' | 'Reject') => {
    setBusy(true); setError(null)
    try {
      await api.plcWriteApproval(point.id, decision)
      onDone(t(decision === 'Approve' ? 'plc.writeApproved' : 'plc.writeRejected', { station: point.stationId, signal: point.signalName }))
    } catch (e) { setError(describeError(e)) } finally { setBusy(false) }
  }
  return (
    <Modal title={t('plc.writeApprovalTitle', { station: point.stationId, signal: point.signalName })} onClose={onClose} footer={<>
      <Button onClick={onClose}>{t('plc.close')}</Button>
      {approved
        ? <Button variant="danger" disabled={busy} onClick={() => void decide('Reject')}>{t('plc.writeWithdraw')}</Button>
        : <>
          {point.writeApproval !== 'REJECTED' && <Button disabled={busy} onClick={() => void decide('Reject')}>{t('plc.writeReject')}</Button>}
          <Button variant="primary" disabled={busy || point.writeSupported === false} onClick={() => void decide('Approve')}>{t('plc.writeConfirmApprove')}</Button>
        </>}
    </>}>
      {error && <div className="notice notice-bad" role="alert">{error}</div>}
      <PointDetails p={point} extra={<div><dt>{t('plc.writeStatus')}</dt><dd><Pill tone={WRITE_TONE[point.writeApproval] ?? 'warn'} dot={false}>{t(`plc.writeState_${point.writeApproval}` as 'plc.writeState_APPROVED')}</Pill></dd></div>} />
      {point.writeDecidedBy && <p className="muted">{t('plc.writeDecidedBy', { user: point.writeDecidedBy, at: point.writeDecidedAt ? formatDateTime(point.writeDecidedAt) : '—' })}</p>}
      {point.writeSupported === false && <div className="notice notice-warn">{t('plc.writeUnsupported')}</div>}
      <div className="notice notice-info">{t('plc.writeScope')}</div>
    </Modal>
  )
}

type Draft = { kind: 'bool'; value: boolean | null } | { kind: 'int'; text: string } | { kind: 'string'; text: string }
type Parsed = { ok: true; value: boolean | number | string } | { ok: false; message: string | null }

/** Strict, per data type; the backend repeats every check. Nothing is converted: an invalid input is an error, never a guess. */
function parseDraft(d: Draft, point: IoPointConfig, t: ReturnType<typeof useT>): Parsed {
  if (d.kind === 'bool') return d.value === null ? { ok: false, message: null } : { ok: true, value: d.value }
  if (d.kind === 'int') {
    const s = d.text.trim()
    if (s === '') return { ok: false, message: null }
    if (!/^-?\d+$/.test(s)) return { ok: false, message: t('plc.enterInt') }
    const n = Number(s)
    return n < INT_MIN || n > INT_MAX ? { ok: false, message: t('plc.intRange') } : { ok: true, value: n }
  }
  const text = d.text
  if (text === '') return { ok: false, message: null }
  if ([...text].some((c) => c.charCodeAt(0) > MAX_ASCII)) return { ok: false, message: t('plc.stringNotAscii') }
  const max = (point.wordLength ?? 0) * 2
  if (max === 0) return { ok: false, message: t('plc.stringNoLength') }
  return text.length > max ? { ok: false, message: t('plc.stringTooLong', { max, words: point.wordLength ?? 0 }) } : { ok: true, value: text }
}

const show = (v: boolean | number | string | null | undefined, t: ReturnType<typeof useT>) =>
  v === null || v === undefined ? '—' : typeof v === 'boolean' ? t(v ? 'plc.on' : 'plc.off') : typeof v === 'string' ? `"${v}"` : String(v)

/** Writes one value: a typed input, a confirmation of exactly what will be sent, then the real result. Needs the write approval AND a mode that allows writing (the API checks both). */
export function TypedWriteDialog({ point, simulator, onClose, onDone }: { point: IoPointConfig; simulator: boolean; onClose: () => void; onDone: (m: string) => void }) {
  const t = useT()
  const { user } = useAuth()
  const io = useApi(useCallback(() => api.plcStationIo(point.stationId), [point.stationId]))
  const current = io.data?.points.find((p) => p.signalName === point.signalName)?.value
  const [draft, setDraft] = useState<Draft>(point.dataType === 'Bool' ? { kind: 'bool', value: null } : point.dataType === 'Int' ? { kind: 'int', text: '' } : { kind: 'string', text: '' })
  const [step, setStep] = useState<'input' | 'confirm' | 'done'>('input')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [writtenValue, setWrittenValue] = useState<boolean | number | string | null>(null)
  const supported = point.writeSupported !== false
  const approved = point.writeApproval === 'APPROVED'
  const parsed = parseDraft(draft, point, t)
  const canGo = supported && approved && parsed.ok

  const send = async () => {
    if (!parsed.ok) return
    setBusy(true); setError(null)
    try { await api.plcTestWrite(point.stationId, point.signalName, parsed.value); setWrittenValue(parsed.value); setStep('done'); io.reload() }
    catch (e) {
      // WRITE_FAILED carries the PLC end code / driver reason, WRITE_AMBIGUOUS says the PLC may or may not have executed it: show both, never a bare success
      const detail = e instanceof ApiRequestError && (e.code === 'WRITE_FAILED' || e.code === 'WRITE_AMBIGUOUS') && e.message ? `  (${e.message})` : ''
      setError(describeError(e) + detail); setStep('input')
    } finally { setBusy(false) }
  }
  const finish = () => (step === 'done' ? onDone(t('plc.testWritten', { station: point.stationId, signal: point.signalName })) : onClose())

  return (
    <Modal title={t('plc.writeTitle', { station: point.stationId, signal: point.signalName })} onClose={finish} footer={step === 'input' ? <>
      <Button onClick={onClose}>{t('plc.skip')}</Button>
      <Button variant="primary" disabled={!canGo} onClick={() => { setError(null); setStep('confirm') }}>{t('plc.writeReview')}</Button>
    </> : step === 'confirm' ? <>
      <Button disabled={busy} onClick={() => setStep('input')}>{t('plc.writeBack')}</Button>
      <Button variant="danger" disabled={busy || !parsed.ok} onClick={() => void send()}>{busy ? t('plc.writeSending') : t('plc.confirmWrite')}</Button>
    </> : <Button variant="primary" onClick={finish}>{t('plc.close')}</Button>}>
      {!simulator && step !== 'done' && <div className="notice notice-warn">{t('plc.realWriteWarn')}</div>}
      {error && <div className="notice notice-bad" role="alert">{error}</div>}
      {!supported && <div className="notice notice-bad" role="alert">{t('plc.writeUnsupported')}</div>}
      {supported && !approved && <div className="notice notice-bad" role="alert">{t('plc.writeNotApproved')}</div>}

      {step === 'input' && (
        <>
          <PointDetails p={point} extra={<div><dt>{t('plc.currentValue')}</dt><dd>{io.loading && !io.data ? '…' : show(current, t)}</dd></div>} />
          <div className="form-grid single">
            {draft.kind === 'bool' && (
              <div className="field">
                <span className="field-label">{t('plc.valueLabel', { type: DATATYPE_VI[point.dataType] })}</span>
                <div className="btn-row" role="radiogroup" aria-label={t('plc.valueLabel', { type: DATATYPE_VI[point.dataType] })}>
                  <Button role="radio" aria-checked={draft.value === true} variant={draft.value === true ? 'primary' : 'secondary'} disabled={!supported || !approved} onClick={() => setDraft({ kind: 'bool', value: true })}>{t('plc.on')}</Button>
                  <Button role="radio" aria-checked={draft.value === false} variant={draft.value === false ? 'primary' : 'secondary'} disabled={!supported || !approved} onClick={() => setDraft({ kind: 'bool', value: false })}>{t('plc.off')}</Button>
                </div>
              </div>
            )}
            {draft.kind === 'int' && (
              <label className={`field ${!parsed.ok && parsed.message ? 'has-error' : ''}`}>
                <span className="field-label">{t('plc.valueLabel', { type: DATATYPE_VI[point.dataType] })}</span>
                <input type="number" step={1} min={INT_MIN} max={INT_MAX} inputMode="numeric" value={draft.text} disabled={!supported || !approved}
                  onChange={(e) => setDraft({ kind: 'int', text: e.target.value })} />
                {!parsed.ok && parsed.message ? <span className="field-error" role="alert">{parsed.message}</span> : <span className="field-hint">{t('plc.intRange')}</span>}
              </label>
            )}
            {draft.kind === 'string' && (
              <label className={`field ${!parsed.ok && parsed.message ? 'has-error' : ''}`}>
                <span className="field-label">{t('plc.valueLabel', { type: DATATYPE_VI[point.dataType] })}</span>
                <textarea rows={3} value={draft.text} disabled={!supported || !approved} spellCheck={false}
                  onChange={(e) => setDraft({ kind: 'string', text: e.target.value })} />
                {!parsed.ok && parsed.message ? <span className="field-error" role="alert">{parsed.message}</span>
                  : <span className="field-hint">{t('plc.stringHint', { n: draft.text.length, max: (point.wordLength ?? 0) * 2, enc: point.encoding ?? '—' })}</span>}
              </label>
            )}
          </div>
        </>
      )}

      {step !== 'input' && parsed.ok && (
        <>
          <p className="dialog-hint">{step === 'confirm' ? t('plc.writeConfirmHint') : t('plc.writeDoneHint')}</p>
          <PointDetails p={point} extra={<>
            <div><dt>{t('plc.currentValue')}</dt><dd>{show(step === 'done' ? current : current, t)}</dd></div>
            <div><dt>{step === 'done' ? t('plc.writtenValue') : t('plc.newValue')}</dt><dd><strong>{show(step === 'done' ? writtenValue : parsed.value, t)}</strong></dd></div>
            <div><dt>{t('plc.performedBy')}</dt><dd>{user?.username ?? '—'}</dd></div>
          </>} />
          {step === 'done' && <div className="notice notice-ok" role="status">{t('plc.writeAccepted')}</div>}
        </>
      )}
    </Modal>
  )
}
