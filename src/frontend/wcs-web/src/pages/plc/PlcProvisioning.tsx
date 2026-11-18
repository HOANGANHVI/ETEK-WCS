import { useCallback, useState } from 'react'
import { api } from '../../api/client'
import type { PortView } from '../../api/routingTypes'
import { Modal } from '../../components/Modal'
import { Async, Button, Panel, Pill } from '../../components/ui'
import { useApi } from '../../hooks/useApi'
import { useT } from '../../i18n'
import { describeError } from '../../i18n/vi'
import { Field } from '../config/FormKit'

/**
 * CR-010: add a PLC (any number of PLCs) and link its ports to WCS locations. A new PLC is disabled and unapproved; every port gets its own copy
 * of the default IO mapping template. Admin only (also enforced by the API).
 */
export function PlcProvisioning({ onChanged }: { onChanged: () => void }) {
  const t = useT()
  const ports = useApi(useCallback(() => api.plcStations(), []))
  const locations = useApi(useCallback(() => api.locations(), []))
  const [adding, setAdding] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  return (
    <Panel title={t('routing.plcPortsTitle')} subtitle={t('routing.plcPortsHint')} actions={<Button variant="primary" onClick={() => setAdding(true)}>{t('routing.plcAddBtn')}</Button>}>
      {message && <div className="notice notice-ok" role="status">{message}</div>}
      <Async state={ports} isEmpty={(d) => d.length === 0} empty={{ title: t('plc.noPlc'), hint: t('plc.noPlcHint') }}>
        {(rows) => (
          <div className="table-wrap">
            <table className="grid">
              <thead><tr><th>{t('routing.plcPortColPort')}</th><th>{t('routing.plcPortColPlc')}</th><th>{t('routing.plcPortColLocation')}</th><th>{t('routing.plcPortColApproval')}</th><th /></tr></thead>
              <tbody>
                {rows.map((p) => <PortRow key={p.stationId} port={p} locationCodes={(locations.data ?? []).map((l) => l.code)} onSaved={(id) => { setMessage(t('routing.plcPortSaved', { id })); ports.reload() }} />)}
              </tbody>
            </table>
          </div>
        )}
      </Async>
      {adding && <AddPlcDialog onClose={() => setAdding(false)} onCreated={(id, n) => { setAdding(false); setMessage(t('routing.plcAddDone', { id, n })); ports.reload(); onChanged() }} />}
    </Panel>
  )
}

function PortRow({ port, locationCodes, onSaved }: { port: PortView; locationCodes: string[]; onSaved: (id: string) => void }) {
  const t = useT()
  const [code, setCode] = useState(port.locationCode ?? '')
  const [enabled, setEnabled] = useState(port.enabled)
  const [error, setError] = useState<string | null>(null)
  const dirty = code !== (port.locationCode ?? '') || enabled !== port.enabled
  const save = async () => {
    setError(null)
    try { await api.plcLinkPort(port.stationId, code || null, enabled); onSaved(port.stationId) } catch (e) { setError(describeError(e)) }
  }
  return (
    <tr>
      <td className="mono"><strong>{port.stationId}</strong></td>
      <td className="mono">{port.plcId}</td>
      <td>
        <select value={code} aria-label={t('routing.plcPortColLocation')} onChange={(e) => setCode(e.target.value)}>
          <option value="">{t('routing.plcPortUnlinked')}</option>
          {locationCodes.map((c) => <option key={c} value={c}>{c}</option>)}
        </select>{' '}
        <label><input type="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} /> {t('plc.enabledOn')}</label>
        {error && <div className="field-error" role="alert">{error}</div>}
      </td>
      <td>{port.approvedPoints === port.pointCount && port.pointCount > 0 ? <Pill tone="ok">{port.approvedPoints}/{port.pointCount}</Pill> : <Pill tone="muted">{port.approvedPoints}/{port.pointCount}</Pill>}</td>
      <td><Button size="sm" disabled={!dirty} onClick={save}>{t('routing.plcPortSave')}</Button></td>
    </tr>
  )
}

function AddPlcDialog({ onClose, onCreated }: { onClose: () => void; onCreated: (id: string, ports: number) => void }) {
  const t = useT()
  const [plcId, setPlcId] = useState('')
  const [name, setName] = useState('')
  const [ip, setIp] = useState('')
  const [ports, setPorts] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const submit = async () => {
    setBusy(true); setError(null)
    const stations = ports.split(',').map((x) => x.trim()).filter(Boolean).map((stationId) => ({ stationId, locationCode: null }))
    try { const c = await api.plcAdd({ plcId: plcId.trim(), name: name.trim(), ipAddress: ip.trim(), stations }); onCreated(c.plcId, stations.length) }
    catch (e) { setError(describeError(e)) } finally { setBusy(false) }
  }
  return (
    <Modal title={t('routing.plcAddTitle')} onClose={onClose}
      footer={<><Button onClick={onClose}>{t('routing.closeDialog')}</Button><Button variant="primary" disabled={busy || !plcId.trim() || !name.trim() || !ip.trim()} onClick={submit}>{t('routing.plcAddBtn')}</Button></>}>
      <p className="muted">{t('routing.plcAddHint')}</p>
      {error && <div className="notice notice-error" role="alert">{error}</div>}
      <div className="form-grid">
        <Field label={t('routing.plcAddId')}><input value={plcId} maxLength={20} onChange={(e) => setPlcId(e.target.value)} /></Field>
        <Field label={t('routing.plcAddName')}><input value={name} maxLength={100} onChange={(e) => setName(e.target.value)} /></Field>
        <Field label={t('routing.plcAddIp')}><input value={ip} maxLength={64} onChange={(e) => setIp(e.target.value)} /></Field>
        <Field label={t('routing.plcAddPorts')} wide><input value={ports} onChange={(e) => setPorts(e.target.value)} placeholder="N1, N2" /></Field>
      </div>
    </Modal>
  )
}
