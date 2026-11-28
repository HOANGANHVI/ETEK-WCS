import { useCallback, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../../api/client'
import type { RcsMapInfo } from '../../api/rcsMapTypes'
import { Button, Loading, Panel, Pill } from '../../components/ui'
import { useApi } from '../../hooks/useApi'
import { useFleet } from '../../fleet/fleetState'
import { useT } from '../../i18n'
import { formatDateTime } from '../../i18n/format'
import { describeError } from '../../i18n/vi'
import { Field, Switch } from './FormKit'

/** CR-065: import (or replace) the RCS map and set how its numbers relate to the positions the RCS reports. */
export function MapTab() {
  const t = useT()
  const map = useApi(useCallback(() => api.rcsMap(), []))
  const [file, setFile] = useState<File | null>(null)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const upload = async () => {
    if (!file) return
    setBusy(true); setError(null); setMessage(null)
    try {
      const r = await api.rcsMapImport(file.name, await file.text())
      setMessage(t(r.replaced ? 'config.mapReplaced' : 'config.mapImported', { name: r.info.mapName, points: r.info.pointCount, edges: r.info.edgeCount })
        + (r.unknownNeighbours > 0 ? ` ${t('config.mapUnknownNeighbours', { n: r.unknownNeighbours })}` : ''))
      map.reload()
    } catch (e) { setError(describeError(e)) } finally { setBusy(false) }
  }

  const info = map.data?.info
  return (
    <>
      {message && <div className="notice notice-ok" role="status">{message}</div>}
      {error && <div className="notice notice-error" role="alert">{error}</div>}
      <Panel title={t('config.mapImportTitle')} subtitle={t('config.mapImportHint')} className="mb">
        <div className="btn-row">
          <input type="file" accept=".xml,text/xml,application/xml" aria-label={t('config.mapFile')} onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
          <Button variant="primary" disabled={!file || busy} onClick={() => void upload()}>{t(info ? 'config.mapReplace' : 'config.mapImport')}</Button>
        </div>
        {info && <p className="muted small">{t('config.mapReplaceHint')}</p>}
      </Panel>
      {map.loading && !map.data ? <Loading /> : map.error && !map.data ? <div className="notice notice-bad" role="alert">{map.error}</div> : !info ? (
        <div className="state state-empty"><div><strong>{t('config.mapNone')}</strong></div></div>
      ) : (
        <>
          <Panel title={t('config.mapInfoTitle')} className="mb" actions={<Link to="/agvs">{t('config.mapOpenMonitor')}</Link>}>
            <dl className="kv2">
              <dt>{'Map ID'}</dt><dd className="mono">{info.mapId}</dd>
              <dt>{'Map Name'}</dt><dd>{info.mapName}</dd>
              <dt>{'Map QR Code'}</dt><dd className="mono">{info.mapQrCode ?? '–'}</dd>
              <dt>{'Map Type'}</dt><dd className="mono">{info.mapType ?? '–'}</dd>
              <dt>{'Row / Col'}</dt><dd className="mono">{info.row ?? '–'} / {info.col ?? '–'}</dd>
              <dt>{'Width / Height'}</dt><dd className="mono">{info.width ?? '–'} / {info.height ?? '–'}</dd>
              <dt>{t('config.mapCounts')}</dt><dd>{t('config.mapCountsValue', { points: info.pointCount, edges: info.edgeCount })}</dd>
              <dt>{t('config.mapFile')}</dt><dd className="mono">{info.fileName}</dd>
              <dt>{t('config.mapImportedAt')}</dt><dd>{formatDateTime(info.importedAt)}{info.importedBy ? ` · ${info.importedBy}` : ''}</dd>
            </dl>
          </Panel>
          <Calibration key={`${info.sha256}-${info.importedAt}-${info.confirmedAt}`} info={info} onSaved={(m) => { setError(null); setMessage(m); map.reload() }} onError={setError} />
        </>
      )}
    </>
  )
}

function Calibration({ info, onSaved, onError }: { info: RcsMapInfo; onSaved: (m: string) => void; onError: (m: string) => void }) {
  const t = useT()
  const fleet = useFleet()
  const [scale, setScale] = useState(String(info.mmPerUnit))
  const [yUp, setYUp] = useState(info.yAxisUp)
  const [offset, setOffset] = useState(String(info.headingOffsetDeg))
  const [clockwise, setClockwise] = useState(info.headingClockwise)
  const [codes, setCodes] = useState(info.rcsMapCodes.join(', '))
  const [confirmed, setConfirmed] = useState(!!info.confirmedAt)
  const [busy, setBusy] = useState(false)
  const reported = [...new Set((fleet.data?.amrs ?? []).map((a) => a.mapCode).filter((c): c is string => !!c))]
  const save = async () => {
    setBusy(true)
    try {
      await api.rcsMapSettings({ mmPerUnit: Number(scale), yAxisUp: yUp, headingOffsetDeg: Number(offset), headingClockwise: clockwise, rcsMapCodes: codes.split(',').map((c) => c.trim()).filter(Boolean), confirmed })
      onSaved(t('config.mapSettingsSaved'))
    } catch (e) { onError(describeError(e)) } finally { setBusy(false) }
  }
  return (
    <Panel title={t('config.mapCalTitle')} subtitle={t('config.mapCalHint')}
      actions={<Pill tone={info.confirmedAt ? 'ok' : 'warn'}>{info.confirmedAt ? t('config.mapConfirmedBy', { by: info.confirmedBy ?? '–', at: formatDateTime(info.confirmedAt) }) : t('config.mapNotConfirmed')}</Pill>}>
      <div className="form-grid">
        <Field label={t('config.mapScale')} hint={t('config.mapScaleHint')}><input type="number" step="any" min="0" value={scale} onChange={(e) => setScale(e.target.value)} /></Field>
        <Field label={t('config.mapOffset')} hint={t('config.mapOffsetHint')}><input type="number" step="any" value={offset} onChange={(e) => setOffset(e.target.value)} /></Field>
        <Field label={t('config.mapCodes')} hint={reported.length > 0 ? t('config.mapCodesReported', { codes: reported.join(', ') }) : t('config.mapCodesHint', { id: info.mapId })} wide>
          <input value={codes} maxLength={500} onChange={(e) => setCodes(e.target.value)} /></Field>
      </div>
      <p><Switch checked={yUp} onChange={setYUp} label={t('config.mapYUp')} /></p>
      <p><Switch checked={clockwise} onChange={setClockwise} label={t('config.mapClockwise')} /></p>
      <p><Switch checked={confirmed} onChange={setConfirmed} label={t('config.mapConfirm')} /></p>
      <div className="btn-row"><Button variant="primary" disabled={busy || !(Number(scale) > 0) || offset.trim() === ''} onClick={() => void save()}>{t('config.mapSave')}</Button></div>
    </Panel>
  )
}
