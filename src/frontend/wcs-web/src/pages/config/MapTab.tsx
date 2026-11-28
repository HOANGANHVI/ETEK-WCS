import { useCallback, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../../api/client'
import type { RcsMapFiles, RcsMapInfo, RcsMapPreview } from '../../api/rcsMapTypes'
import { MapDataReport } from '../../components/MapDataReport'
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
  const [excel, setExcel] = useState<File | null>(null)   // CR-067: the two files are one set
  const [checked, setChecked] = useState<{ files: RcsMapFiles; result: RcsMapPreview } | null>(null)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  // the files are read once: what is saved is exactly what the preview showed
  const preview = async () => {
    if (!file || !excel) return
    setBusy(true); setError(null); setMessage(null); setChecked(null)
    try {
      const files: RcsMapFiles = { fileName: file.name, content: await file.text(), mapDataFileName: excel.name, mapDataBase64: toBase64(new Uint8Array(await excel.arrayBuffer())) }
      setChecked({ files, result: await api.rcsMapPreview(files) })
    } catch (e) { setError(describeError(e)) } finally { setBusy(false) }
  }
  const save = async () => {
    if (!checked) return
    setBusy(true); setError(null); setMessage(null)
    try {
      const r = await api.rcsMapImport(checked.files)
      setMessage(t(r.replaced ? 'config.mapReplaced' : 'config.mapImported', { name: r.info.mapName, points: r.info.pointCount, edges: r.info.edgeCount })
        + (r.unknownNeighbours > 0 ? ` ${t('config.mapUnknownNeighbours', { n: r.unknownNeighbours })}` : ''))
      setChecked(null); map.reload()
    } catch (e) { setError(describeError(e)) } finally { setBusy(false) }
  }

  const info = map.data?.info
  return (
    <>
      {message && <div className="notice notice-ok" role="status">{message}</div>}
      {error && <div className="notice notice-error" role="alert">{error}</div>}
      <Panel title={t('config.mapImportTitle')} subtitle={t('config.mapImportHint')} className="mb">
        <div className="form-grid">
          <Field label={t('config.mapFile')}><input type="file" accept=".xml,text/xml,application/xml" data-file="xml" onChange={(e) => { setFile(e.target.files?.[0] ?? null); setChecked(null) }} /></Field>
          <Field label={t('config.mdFile')}><input type="file" accept=".xls,.xlsx" data-file="excel" onChange={(e) => { setExcel(e.target.files?.[0] ?? null); setChecked(null) }} /></Field>
        </div>
        <div className="btn-row"><Button variant="primary" disabled={!file || !excel || busy} onClick={() => void preview()}>{t('config.mdCheck')}</Button></div>
        {info && <p className="muted small">{t('config.mapReplaceHint')}</p>}
        {checked && (
          <div className="map-preview">
            <h3>{t('config.mdPreviewTitle', { name: checked.result.mapName, id: checked.result.mapId, qr: checked.result.mapQrCode ?? '–' })}</h3>
            <p>{t('config.mapCountsValue', { points: checked.result.pointCount, edges: checked.result.edgeCount })}{checked.result.unknownNeighbours > 0 ? ` · ${t('config.mapUnknownNeighbours', { n: checked.result.unknownNeighbours })}` : ''}</p>
            {checked.result.mapData && <MapDataReport data={checked.result.mapData} pointsWithoutRow={checked.result.pointsWithoutRow} />}
            {checked.result.currentMapId && checked.result.currentMapId !== checked.result.mapId && <div className="notice notice-warn">{t('config.mdOtherId', { now: checked.result.currentMapId, next: checked.result.mapId })}</div>}
            {(checked.result.namedPointsKept > 0 || checked.result.namedPointsForReview > 0) && <p>{t('config.mdNamed', { kept: checked.result.namedPointsKept, review: checked.result.namedPointsForReview })}</p>}
            <div className="btn-row">
              <Button variant="primary" disabled={busy} onClick={() => void save()}>{t(info ? 'config.mapReplace' : 'config.mapImport')}</Button>
              <Button disabled={busy} onClick={() => setChecked(null)}>{t('config.mdCancel')}</Button>
            </div>
          </div>
        )}
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
          {map.data?.geometry.mapData && (
            <Panel title={t('config.mdCurrentTitle')} className="mb">
              <MapDataReport data={map.data.geometry.mapData} pointsWithoutRow={map.data.geometry.points.filter((x) => !map.data!.geometry.mapData!.rows.some((r) => r.pointId === x.id)).map((x) => x.id)} />
            </Panel>
          )}
          {map.data && !map.data.geometry.mapData && <div className="notice notice-warn">{t('config.mdMissing')}</div>}
          <Calibration key={`${info.sha256}-${info.importedAt}-${info.confirmedAt}`} info={info} onSaved={(m) => { setError(null); setMessage(m); map.reload() }} onError={setError} />
        </>
      )}
    </>
  )
}

function toBase64(bytes: Uint8Array): string {
  let s = ''
  for (let i = 0; i < bytes.length; i += 0x8000) s += String.fromCharCode(...bytes.subarray(i, i + 0x8000))
  return btoa(s)
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
