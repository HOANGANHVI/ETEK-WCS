import { useCallback, useState } from 'react'
import { useApi } from '../hooks/useApi'
import { api } from '../api/client'
import type { Location, Rack } from '../api/types'
import { Modal } from '../components/Modal'
import { Button } from '../components/ui'
import { useT } from '../i18n'
import { describeError, RACK_STATUS_VI } from '../i18n/vi'
import { Field } from './config/FormKit'

export type RackDialogState = { kind: 'create' } | { kind: 'edit'; rack: Rack } | { kind: 'delete'; rack: Rack }

/** CR-040: add, correct or remove one rack record. The server refuses reserved or moving racks and says why; nothing is decided here. */
export function RackDialog({ state, locations, defaultLocation, codeOf, onClose, onDone }: {
  state: RackDialogState; locations: Location[]; defaultLocation: string; codeOf: (id: string | null) => string; onClose: () => void; onDone: () => void
}) {
  const t = useT()
  const rack = state.kind === 'create' ? null : state.rack
  const [location, setLocation] = useState(rack?.currentLocationId ? codeOf(rack.currentLocationId) : defaultLocation)
  const [kind, setKind] = useState<'Full' | 'Empty'>(rack?.status === 'Empty' ? 'Empty' : 'Full')
  const [externalId, setExternalId] = useState(rack?.externalRackId ?? '')
  const [reason, setReason] = useState('')
  const [rackType, setRackType] = useState(rack?.rackType ?? '')
  const editability = useApi(useCallback(() => (rack ? api.rackEditability(rack.id) : Promise.resolve({ editable: true, code: null, message: null })), [rack]))
  const locked = editability.data && !editability.data.editable ? editability.data : null
  const models = useApi(useCallback(() => api.productModels(), []))
  const [model, setModel] = useState(rack?.productModelId ?? '')
  const modelChoice = (models.data ?? []).filter((m) => m.enabled || m.id === rack?.productModelId)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const save = async () => {
    setBusy(true); setError(null)
    try {
      if (state.kind === 'create') await api.rackCreate({ locationCode: location, rackKind: kind, externalRackId: externalId.trim() || null, reason, rackType: rackType.trim(), productModelId: model || undefined })
      else if (state.kind === 'edit') await api.rackUpdate(state.rack.id, { locationCode: location, rackKind: kind, externalRackId: externalId.trim(), reason, rackType: rackType.trim(), productModelId: !state.rack.productModelId && model ? model : undefined })
      else await api.rackDelete(state.rack.id, reason)
      onDone()
    } catch (e) { setError(describeError(e)) } finally { setBusy(false) }
  }
  const title = t(state.kind === 'create' ? 'linked.rackAdd' : state.kind === 'edit' ? 'linked.rackEdit' : 'linked.rackDelete')
  return (
    <Modal title={title} onClose={onClose} footer={<>
      <Button onClick={onClose}>{t('linked.cancel')}</Button>
      <Button variant={state.kind === 'delete' ? 'danger' : 'primary'} disabled={busy || !!locked || reason.trim() === '' || (state.kind !== 'delete' && location === '')} onClick={() => void save()}>{title}</Button>
    </>}>
      <div className="notice notice-warn" role="note">{t('linked.rackWarning')}</div>
      {error && <div className="notice notice-bad" role="alert">{error}</div>}
      {editability.error && rack && <div className="notice notice-warn" role="status">{t('linked.rackCheckFailed')} {editability.error}</div>}
      {locked && <div className="notice notice-bad" role="status" data-rack-locked={locked.code}>{t('linked.rackLocked')} {locked.message} <span className="mono">({locked.code})</span></div>}
      {rack && <p className="mono">{rack.externalRackId ?? rack.id.slice(0, 8)} · {RACK_STATUS_VI[rack.status]} · {codeOf(rack.currentLocationId) || '—'}</p>}
      <div className="form-grid single">
        {state.kind !== 'delete' && (
          <>
            <Field label={t('linked.rackLocation')}>
              <select value={location} onChange={(e) => setLocation(e.target.value)}>
                <option value="">—</option>
                {locations.map((l) => <option key={l.id} value={l.code}>{l.code}</option>)}
              </select>
            </Field>
            <Field label={t('linked.rackKind')}>
              <select value={kind} onChange={(e) => setKind(e.target.value as 'Full' | 'Empty')}>
                <option value="Full">{RACK_STATUS_VI.Full}</option>
                <option value="Empty">{RACK_STATUS_VI.Empty}</option>
              </select>
            </Field>
            <Field label={t('linked.rackType')}><input value={rackType} maxLength={100} onChange={(e) => setRackType(e.target.value)} /></Field>
            <Field label={t('linked.rackModel')} hint={rack?.productModelId ? t('linked.rackModelFixed') : undefined}>
              <select value={model} disabled={!!rack?.productModelId} onChange={(e) => setModel(e.target.value)}>
                <option value="">{t('linked.rackModelNone')}</option>
                {modelChoice.map((m) => <option key={m.id} value={m.id}>{m.code} · {m.name}</option>)}
              </select>
            </Field>
            <Field label={t('linked.rackId')}><input value={externalId} maxLength={100} onChange={(e) => setExternalId(e.target.value)} /></Field>
          </>
        )}
        <Field label={t('linked.reason')}><input value={reason} maxLength={500} onChange={(e) => setReason(e.target.value)} /></Field>
      </div>
    </Modal>
  )
}
