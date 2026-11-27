import { LocationRemovalDialog } from './LocationRemovalDialog'
import { DEMO_MODE } from '../../config'
import { useCallback, useState } from 'react'
import { api } from '../../api/client'
import type { Agv, Location } from '../../api/types'
import type { OperationsView, SaveLocationBody } from '../../api/configTypes'
import { Async, Button, Panel, Pill } from '../../components/ui'
import { useApi } from '../../hooks/useApi'
import { t, useT } from '../../i18n'
import { MODE_VI, NO_SOURCE_VI } from '../../i18n/config'
import { ALLOWED_KIND_VI, flowInfo, LOCATION_TYPE_VI, RACK_KIND_VI, TASK_STATE_VI, AGV_ROLE_VI } from '../../i18n/vi'
import type { TaskState } from '../../api/types'
import { Field, Switch } from './FormKit'
import { fromApiError, type Errors } from './formUtils'

const EVENTS = ['InventoryChanged', 'AgvStatusChanged', 'TaskStateChanged'] as const

export function OperationsTab() {
  const tr = useT()
  const state = useApi(useCallback(() => api.operations(), []), [...EVENTS])
  return (
    <Async state={state} empty={{ title: tr('config.noData') }}>
      {(ops) => <OperationsView ops={ops} onChanged={state.reload} />}
    </Async>
  )
}

const approval = (ok: boolean) => ok ? <Pill tone="ok">{t('config.approved')}</Pill> : <Pill tone="warn">{t('config.notConfirmed')}</Pill>
const groups = (g: string[][]) => g.length === 0 ? '—' : g.map((x) => x.join(' / ')).join('  ›  ')

function OperationsView({ ops, onChanged }: { ops: OperationsView; onChanged: () => void }) {
  const tr = useT()
  const [adding, setAdding] = useState(false)
  const timeouts = Object.entries(ops.scheduler.stateTimeoutsSeconds)
  return (
    <div className="ops-stack">
      <div className="ops-two">
        <Panel title={tr('config.policyPanel')}>
          <table className="grid">
            <tbody>
              <tr><td>{tr('config.policyConcurrency')}</td><td><strong>{tr('config.policyConcurrencyValue')}</strong></td><td>{approval(ops.policy.concurrencyApproved)}</td></tr>
              <tr><td>{tr('config.policyQueue')}</td><td>{tr('config.policyQueueValue')}</td><td>{approval(ops.policy.queueOrderApproved)}</td></tr>
              <tr><td>{tr('config.policyNoSource')}</td><td>{NO_SOURCE_VI[ops.policy.noSourceBehavior] ?? ops.policy.noSourceBehavior}</td><td>{approval(ops.policy.noSourceBehaviorApproved)}</td></tr>
            </tbody>
          </table>
        </Panel>

        <Panel title={tr('config.schedulerPanel')}>
          <dl className="kv2">
            <dt>{tr('config.tick')}</dt><dd>{ops.scheduler.tickIntervalMs} ms</dd>
            <dt>{tr('config.maxDispatch')}</dt><dd>{ops.scheduler.maxDispatchAttempts}</dd>
            <dt>{tr('config.stateTimeouts')}</dt>
            <dd>{timeouts.length === 0 ? <span className="muted">{tr('config.unlimited')}</span>
              : timeouts.map(([k, v]) => <div key={k}>{tr('config.seconds', { state: TASK_STATE_VI[k as TaskState] ?? k, n: v })}</div>)}</dd>
          </dl>
        </Panel>
      </div>

      <Panel title={tr('config.flowsPanel')} flush>
        <div className="table-wrap">
          <table className="grid">
            <thead><tr><th>{tr('config.flowCol')}</th><th>{tr('config.flowRack')}</th><th>{tr('config.flowAgv')}</th><th>{tr('config.flowStations')}</th><th>{tr('config.flowSource')}</th><th>{tr('config.flowDestination')}</th><th>{tr('config.flowNoBind')}</th></tr></thead>
            <tbody>
              {ops.flows.map((f) => (
                <tr key={f.flow}>
                  <td><strong>{flowInfo(f.flow).code}</strong> <span className="muted">{flowInfo(f.flow).label}</span></td>
                  <td>{(RACK_KIND_VI[f.rackKind] ?? f.rackKind).replace(/^./, (c) => c.toUpperCase())}</td>
                  <td>{AGV_ROLE_VI[f.agvRole] ?? f.agvRole}</td>
                  <td>{f.stations.join(', ')}</td>
                  <td>{f.sourceMode === 'Request' ? MODE_VI.Request : groups(f.sourceGroups)}</td>
                  <td>{f.destinationMode === 'Request' ? MODE_VI.Request : groups(f.destinationGroups)}</td>
                  <td>{f.noBindDropLocations.length ? f.noBindDropLocations.join(', ') : '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Panel>

      <Panel title={tr('config.agvPanel')} flush>
        <div className="table-wrap">
          <table className="grid">
            <thead><tr><th>AGV</th><th>{tr('config.agvRole')}</th><th>{tr('config.agvRobotCode')}</th><th>{tr('config.agvEnabled')}</th><th>{tr('config.actions')}</th></tr></thead>
            <tbody>{ops.agvs.map((a) => <AgvRow key={`${a.id}-${a.rcsRobotCode}-${a.enabled}`} agv={a} onSaved={onChanged} />)}</tbody>
          </table>
        </div>
      </Panel>

      <Panel title={tr('config.locPanel')} flush
        actions={<Button size="sm" variant="primary" onClick={() => setAdding((v) => !v)}>{adding ? tr('config.closeBtn') : tr('config.addLocation')}</Button>}>
        {adding && <div className="pad"><LocationForm onDone={() => { setAdding(false); onChanged() }} onCancel={() => setAdding(false)} /></div>}
        {ops.locations.length === 0 && !adding ? (
          <div className="state state-empty"><div><strong>{tr('config.noLocations')}</strong><span>{tr('config.noLocationsHint')}</span></div></div>
        ) : (
          <div className="table-wrap">
            <table className="grid">
              <thead><tr><th>{tr('config.locCode')}</th><th>{tr('config.locName')}</th><th>{tr('config.locType')}</th><th>{tr('config.locAccepts')}</th><th className="right">{tr('config.locCapacity')}</th><th>{tr('config.locPlc')}</th><th>{tr('config.locInUse')}</th><th>{tr('config.actions')}</th></tr></thead>
              <tbody>{ops.locations.map((l) => <LocationRow key={`${l.id}-${l.rackCapacity}-${l.name}-${l.enabled}`} loc={l} onSaved={onChanged} />)}</tbody>
            </table>
          </div>
        )}
      </Panel>
    </div>
  )
}

function AgvRow({ agv, onSaved }: { agv: Agv; onSaved: () => void }) {
  const tr = useT()
  const [code, setCode] = useState(agv.rcsRobotCode ?? '')
  const [enabled, setEnabled] = useState(agv.enabled)
  const [err, setErr] = useState<{ fields: Errors; general: string | null } | null>(null)
  const [busy, setBusy] = useState(false)
  const dirty = code.trim() !== (agv.rcsRobotCode ?? '') || enabled !== agv.enabled
  const save = async () => {
    setBusy(true); setErr(null)
    try { await api.saveAgv(agv.id, { rcsRobotCode: code.trim() || null, enabled }); onSaved() }
    catch (e) { setErr(fromApiError(e)) }
    finally { setBusy(false) }
  }
  return (
    <tr>
      <td><strong>{agv.code}</strong></td>
      <td>{AGV_ROLE_VI[agv.role]}</td>
      <td>
        <input className="inline-input" aria-label={tr('config.robotCodeOf', { code: agv.code })} value={code} onChange={(e) => setCode(e.target.value)} placeholder={tr('config.unassigned')} />
        {(err?.fields.rcsRobotCode) && <div className="field-error">{err.fields.rcsRobotCode}</div>}
        {err && !err.fields.rcsRobotCode && err.general && <div className="field-error">{err.general}</div>}
      </td>
      <td><Switch checked={enabled} onChange={setEnabled} label={enabled ? tr('config.on') : tr('config.off')} /></td>
      <td><Button size="sm" variant="primary" disabled={!dirty || busy} onClick={() => void save()}>{tr('config.saveShort')}</Button></td>
    </tr>
  )
}

function LocationRow({ loc, onSaved }: { loc: Location; onSaved: () => void }) {
  const tr = useT()
  const [editing, setEditing] = useState(false)
  const [removing, setRemoving] = useState(false)   // CR-057
  if (editing) return (
    <tr><td colSpan={8}><LocationForm initial={loc} onDone={() => { setEditing(false); onSaved() }} onCancel={() => setEditing(false)} /></td></tr>
  )
  return (
    <tr>
      <td><strong>{loc.code}</strong></td>
      <td>{loc.name}</td>
      <td>{LOCATION_TYPE_VI[loc.type] ?? loc.type}</td>
      <td>{ALLOWED_KIND_VI[loc.allowedRackKind] ?? loc.allowedRackKind}</td>
      <td className="right num">{loc.rackCapacity}</td>
      <td>{loc.requiresPlcPermission ? tr('config.required') : '—'}</td>
      <td>{loc.enabled ? <Pill tone="ok">{tr('config.on')}</Pill> : <Pill tone="muted">{tr('config.off')}</Pill>}</td>
      <td><span className="nowrap"><Button size="sm" onClick={() => setEditing(true)}>{tr('config.edit')}</Button>{' '}
        {!DEMO_MODE && <Button size="sm" variant="danger" onClick={() => setRemoving(true)}>{tr('config.locDelete')}</Button>}</span>
        {removing && <LocationRemovalDialog code={loc.code} onClose={() => setRemoving(false)} onDeleted={() => { setRemoving(false); onSaved() }} />}</td>
    </tr>
  )
}

function LocationForm({ initial, onDone, onCancel }: { initial?: Location; onDone: () => void; onCancel: () => void }) {
  const tr = useT()
  const [f, setF] = useState({
    code: initial?.code ?? '', name: initial?.name ?? '', type: initial?.type ?? 'Buffer', capacity: String(initial?.rackCapacity ?? ''),
    allowed: initial?.allowedRackKind ?? 'Any', enabled: initial?.enabled ?? true, plc: initial?.requiresPlcPermission ?? false,
  })
  const [err, setErr] = useState<{ fields: Errors; general: string | null } | null>(null)
  const [busy, setBusy] = useState(false)
  const set = <K extends keyof typeof f>(k: K, v: (typeof f)[K]) => setF((x) => ({ ...x, [k]: v }))

  const save = async () => {
    const capacity = f.capacity.trim() === '' ? NaN : Number(f.capacity)
    if (Number.isNaN(capacity)) { setErr({ fields: { rackCapacity: tr('config.fCapacityRequired') }, general: null }); return }
    const body: SaveLocationBody = { code: f.code.trim(), name: f.name.trim(), type: f.type, rackCapacity: capacity, allowedRackKind: f.allowed, enabled: f.enabled, requiresPlcPermission: f.plc }
    setBusy(true); setErr(null)
    try { await (initial ? api.saveLocation(body) : api.createLocation(body)); onDone() }
    catch (e) { setErr(fromApiError(e)) }
    finally { setBusy(false) }
  }
  const e = err?.fields ?? {}
  return (
    <div className="loc-form">
      <div className="form-grid">
        <Field label={tr('config.fCode')} error={e.code}><input value={f.code} onChange={(x) => set('code', x.target.value)} disabled={!!initial} maxLength={50} /></Field>
        <Field label={tr('config.fName')} error={e.name}><input value={f.name} onChange={(x) => set('name', x.target.value)} maxLength={200} /></Field>
        <Field label={tr('config.fType')} error={e.type}>
          <select value={f.type} onChange={(x) => set('type', x.target.value)} disabled={!!initial}>
            {Object.entries(LOCATION_TYPE_VI).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
          </select>
        </Field>
        <Field label={tr('config.fCapacity')} error={e.rackCapacity} hint={tr('config.fCapacityHint')}><input value={f.capacity} onChange={(x) => set('capacity', x.target.value)} inputMode="numeric" /></Field>
        <Field label={tr('config.fAccepts')} error={e.allowedRackKind}>
          <select value={f.allowed} onChange={(x) => set('allowed', x.target.value)}>
            {Object.entries(ALLOWED_KIND_VI).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
          </select>
        </Field>
        <div className="field switches">
          <Switch checked={f.plc} onChange={(v) => set('plc', v)} label={tr('config.fPlcPermission')} />
          <Switch checked={f.enabled} onChange={(v) => set('enabled', v)} label={tr('config.fInUse')} />
        </div>
      </div>
      {err?.general && <div className="notice notice-bad" role="alert">{err.general}</div>}
      <div className="btn-row">
        <Button variant="primary" disabled={busy} onClick={() => void save()}>{initial ? tr('config.saveLocation') : tr('config.addLocation')}</Button>
        <Button disabled={busy} onClick={onCancel}>{tr('config.cancel')}</Button>
      </div>
    </div>
  )
}
