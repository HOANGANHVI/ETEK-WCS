import { useCallback, useState } from 'react'
import { api } from '../api/client'
import type { MapPoint, MapPointNote } from '../api/rcsMapTypes'
import { useApi } from '../hooks/useApi'
import { useT } from '../i18n'
import { formatDateTime } from '../i18n/format'
import { describeError } from '../i18n/vi'
import { Field } from '../pages/config/FormKit'
import { Button } from './ui'

/** CR-066: one point of the map: what the file says, and the name and WCS location WCS adds. An Admin edits; everyone reads. */
export function MapPointPanel({ point, note, notes, mapSha256, canEdit, onSaved, onClose }: {
  point: MapPoint; note: MapPointNote | null; notes: MapPointNote[]; mapSha256: string; canEdit: boolean; onSaved: () => void; onClose: () => void
}) {
  const t = useT()
  const locations = useApi(useCallback(() => api.locations(), []), ['InventoryChanged'])
  const [name, setName] = useState(note?.name ?? '')
  const [code, setCode] = useState(note?.locationCode ?? '')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState(false)
  const save = async (location: string) => {
    setBusy(true); setError(null); setDone(false)
    try {
      await api.rcsMapPointSet(point.id, { mapSha256, name: name.trim() || null, locationCode: location || null })
      setCode(location); setDone(true); onSaved()
    } catch (e) { setError(describeError(e)) } finally { setBusy(false) }
  }
  const takenBy = new Map(notes.filter((n) => n.current && n.locationCode && n.pointId !== point.id).map((n) => [n.locationCode as string, n.pointId]))
  const codes = (locations.data ?? []).map((l) => l.code).sort((a, b) => a.localeCompare(b))
  const removed = note?.locationRemoved && code === note.locationCode
  return (
    <aside className="amr-map-detail">
      <div className="amr-map-detail-head"><strong>{t('agv.pointTitle', { id: point.id })}</strong>
        <button type="button" className="modal-x" aria-label={t('common.close')} onClick={onClose}>×</button></div>
      <dl className="kv2">
        <dt>{'Point ID'}</dt><dd className="mono">{point.id}</dd>
        <dt>{t('agv.pointXY')}</dt><dd className="mono">{point.x}, {point.y}</dd>
        {point.value && <><dt>{'value'}</dt><dd className="mono">{point.value}</dd></>}
        {point.storageType && <><dt>{'StorageType'}</dt><dd className="mono">{point.storageType}</dd></>}
        {!canEdit && <><dt>{t('agv.pointName')}</dt><dd>{note?.name ?? '–'}</dd><dt>{t('agv.pointLocation')}</dt><dd>{note?.locationCode ?? '–'}</dd></>}
      </dl>
      {removed && <div className="notice notice-warn">{t('agv.pointLocationRemoved', { code })}</div>}
      {canEdit && (
        <>
          {error && <div className="notice notice-bad" role="alert">{error}</div>}
          {done && !error && <div className="notice notice-ok" role="status">{t('agv.pointSaved')}</div>}
          <Field label={t('agv.pointName')}><input value={name} maxLength={100} placeholder={t('agv.pointNameHint')} onChange={(e) => { setName(e.target.value); setDone(false) }} /></Field>
          <Field label={t('agv.pointLocation')} hint={locations.error ?? undefined}>
            <select value={code} onChange={(e) => { setCode(e.target.value); setDone(false) }}>
              <option value="">{t('agv.pointNoLocation')}</option>
              {removed && <option value={code}>{code}</option>}
              {codes.map((c) => <option key={c} value={c} disabled={takenBy.has(c)}>{takenBy.has(c) ? t('agv.pointTaken', { code: c, id: takenBy.get(c) as number }) : c}</option>)}
            </select>
          </Field>
          <div className="btn-row">
            <Button variant="primary" disabled={busy || !!removed} onClick={() => void save(code)}>{t('agv.pointSave')}</Button>
            <Button disabled={busy || !note?.locationCode} onClick={() => void save('')}>{t('agv.pointUnlink')}</Button>
          </div>
        </>
      )}
      {note && <p className="muted small">{t('agv.pointUpdated', { by: note.updatedBy ?? '–', at: formatDateTime(note.updatedAt) })}</p>}
    </aside>
  )
}
