import { useCallback, useState } from 'react'
import { api } from '../api/client'
import type { PlcConnection, ProbeResultView } from '../api/plcTypes'
import { Async, Button, PageHeader, Panel, Pill } from '../components/ui'
import { ENGINEERING_TOOLS } from '../config'
import { useApi } from '../hooks/useApi'
import { useT, type MsgKey } from '../i18n'
import { MISSING_PARAM_VI } from '../i18n/plc'
import { describeError } from '../i18n/vi'
import { Field, Switch } from './config/FormKit'
import { fromApiError, fromNum, toInt, toStr, type Errors } from './config/formUtils'
import { fmtTime } from './plc/plcFormat'
import { ConfigPill } from './plc/plcUi'

const PROTOCOLS: { value: string; label: MsgKey }[] = [{ value: 'McProtocol3EBinaryTcp', label: 'plc.protocolMc' }]

/**
 * CR-004: the ONLY place where PLC connection parameters are edited (4 PLCs, one form each). Admin-only (also enforced by the API).
 * Nothing is pre-filled except what the catalog knows (name and IP); every other parameter is entered by the PLC engineer. IO addresses are NOT edited here.
 */
export function PlcConnectionsPage() {
  const t = useT()
  const overview = useApi(useCallback(() => api.plcOverview(), []))
  const mode = overview.data?.runtime.effectiveMode ?? 'Simulator'
  return (
    <>
      <PageHeader title={t('plc.connTitle')} />
      <PlcConnectionsBody realMode={mode !== 'Simulator'} simulator={mode === 'Simulator'} />
    </>
  )
}

function PlcConnectionsBody({ realMode, simulator }: { realMode: boolean; simulator: boolean }) {
  const t = useT()
  const list = useApi(useCallback(() => api.plcConnections(), []))
  const [editing, setEditing] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  return (
    <>
      {message && <div className="notice notice-ok" role="status">{message}</div>}
      <Async state={list} isEmpty={(d) => d.length === 0} empty={{ title: t('plc.noPlc'), hint: t('plc.noPlcHint') }}>
        {(rows) => (
          <div className="plc-config-grid">
            {rows.map((c) => (
              <Panel key={c.plcId} title={`${c.plcId} · ${c.stations.join(', ')}`} subtitle={t('plc.updatedAt', { time: fmtTime(c.updatedAt) }) + (c.updatedBy ? t('plc.updatedBy', { user: c.updatedBy }) : '')}
                actions={<ConfigPill status={c.configStatus} />}>
                {editing === c.plcId
                  ? <ConnectionForm c={c} realMode={realMode} onCancel={() => setEditing(null)} onSaved={() => { setEditing(null); setMessage(t('plc.saved', { id: c.plcId })); list.reload() }} />
                  : <ConnectionSummary c={c} realMode={realMode} simulator={simulator} onEdit={() => setEditing(c.plcId)} />}
              </Panel>
            ))}
          </div>
        )}
      </Async>
    </>
  )
}

function ConnectionSummary({ c, onEdit, realMode, simulator }: { c: PlcConnection; onEdit: () => void; realMode: boolean; simulator: boolean }) {
  const t = useT()
  const [busy, setBusy] = useState(false)
  const [probe, setProbe] = useState<ProbeResultView | null>(null)
  const [error, setError] = useState<string | null>(null)
  const run = async (fn: () => Promise<void>) => { setBusy(true); setError(null); try { await fn() } catch (e) { setError(describeError(e)) } finally { setBusy(false) } }
  const row = (k: string, v: string | number | null) => <><dt>{k}</dt><dd>{v === null || v === '' ? <span className="muted">{t('common.notSet')}</span> : v}</dd></>
  return (
    <>
      <dl className="kv2">
        {row(t('plc.ipAddress'), c.ipAddress)}{row(t('plc.tcpPort'), c.port)}{row(t('plc.protocol'), c.protocol)}
        {row(t('plc.networkNumber'), c.networkNumber)}{row(t('plc.pcNumber'), c.pcNumber)}{row(t('plc.moduleIo'), c.moduleIoNumber)}{row(t('plc.plcStation'), c.plcStationNumber)}
        {row(t('plc.readMonitor'), c.monitoringTimer)}{row(t('plc.commonTimeout'), c.timeoutMs)}{row(t('plc.connectTimeout'), c.connectTimeoutMs ?? (c.timeoutMs === null ? null : t('plc.useCommonTimeout')))}
        {row(t('plc.readTimeout'), c.readTimeoutMs ?? (c.timeoutMs === null ? null : t('plc.useCommonTimeout')))}{row(t('plc.writeTimeout'), c.writeTimeoutMs ?? (c.timeoutMs === null ? null : t('plc.useCommonTimeout')))}{row(t('plc.pollInterval'), c.pollingIntervalMs)}
        {row(t('plc.xyBase'), c.xyAddressBase === 8 ? t('plc.octal8') : c.xyAddressBase === 16 ? t('plc.hex16') : null)}{row(t('plc.wBase'), c.wAddressBase === 10 ? t('plc.dec10') : c.wAddressBase === 16 ? t('plc.hex16') : null)}
        <dt>{t('plc.enabled')}</dt><dd>{c.enabled ? <Pill tone="ok">{t('plc.enabledOn')}</Pill> : <Pill tone="muted">{t('plc.enabledOff')}</Pill>}</dd>
      </dl>
      {c.missingParameters.length > 0 && (
        <div className="notice notice-warn">{t('plc.missingParams', { list: c.missingParameters.map((m) => MISSING_PARAM_VI[m] ?? m).join(', ') })}</div>
      )}
      {error && <div className="notice notice-bad" role="alert">{error}</div>}
      {probe && <div className={`notice ${probe.outcome === 'Failed' ? 'notice-bad' : 'notice-info'}`}>{t('plc.probeResult', { mode: probe.mode, message: probe.message, latency: probe.latencyMs !== null ? ` · ${probe.latencyMs} ms` : '' })}</div>}
      <div className="btn-row">
        <Button variant="primary" onClick={onEdit}>{t('plc.edit')}</Button>
        <Button disabled={busy} onClick={() => void run(async () => setProbe(await api.plcTestConnection(c.plcId)))}>{t('plc.testConn')}</Button>
        {simulator && ENGINEERING_TOOLS && <Button disabled={busy} onClick={() => void run(async () => { await api.plcSimOnline(c.plcId, false) })}>{t('plc.simDrop')}</Button>}
        {simulator && ENGINEERING_TOOLS && <Button disabled={busy} onClick={() => void run(async () => { await api.plcSimOnline(c.plcId, true) })}>{t('plc.simRestore')}</Button>}
      </div>
      {realMode && <p className="muted small">{t('plc.probeNote')}</p>}
    </>
  )
}

function ConnectionForm({ c, onCancel, onSaved, realMode }: { c: PlcConnection; onCancel: () => void; onSaved: () => void; realMode: boolean }) {
  const t = useT()
  const [f, setF] = useState({
    name: c.name, ip: c.ipAddress, port: fromNum(c.port), protocol: c.protocol ?? '', net: fromNum(c.networkNumber), pc: fromNum(c.pcNumber),
    module: fromNum(c.moduleIoNumber), station: fromNum(c.plcStationNumber), timer: fromNum(c.monitoringTimer), timeout: fromNum(c.timeoutMs),
    poll: fromNum(c.pollingIntervalMs), enabled: c.enabled,
    connectTimeout: fromNum(c.connectTimeoutMs), readTimeout: fromNum(c.readTimeoutMs), writeTimeout: fromNum(c.writeTimeoutMs),
    xyBase: fromNum(c.xyAddressBase), wBase: fromNum(c.wAddressBase),
  })
  const [busy, setBusy] = useState(false)
  const [errors, setErrors] = useState<Errors>({})
  const [general, setGeneral] = useState<string | null>(null)
  const set = (k: keyof typeof f) => (e: { target: { value: string } }) => setF((p) => ({ ...p, [k]: e.target.value }))
  const save = async () => {
    setBusy(true); setErrors({}); setGeneral(null)
    try {
      await api.plcSaveConnection(c.plcId, {
        name: toStr(f.name), ipAddress: f.ip.trim(), port: toInt(f.port), protocol: toStr(f.protocol), networkNumber: toInt(f.net), pcNumber: toInt(f.pc),
        moduleIoNumber: toInt(f.module), plcStationNumber: toInt(f.station), monitoringTimer: toInt(f.timer), timeoutMs: toInt(f.timeout), pollingIntervalMs: toInt(f.poll), enabled: f.enabled,
        connectTimeoutMs: toInt(f.connectTimeout), readTimeoutMs: toInt(f.readTimeout), writeTimeoutMs: toInt(f.writeTimeout),
        xyAddressBase: toInt(f.xyBase), wAddressBase: toInt(f.wBase),
      })
      onSaved()
    } catch (e) { const r = fromApiError(e); setErrors(r.fields); setGeneral(r.general) } finally { setBusy(false) }
  }
  const num = (label: string, k: keyof typeof f, name: string, hint?: string) => (
    <Field label={label} error={errors[name]} hint={hint}><input inputMode="numeric" value={String(f[k])} onChange={set(k)} /></Field>
  )
  return (
    <>
      {general && <div className="notice notice-bad" role="alert">{general}</div>}
      <div className="form-grid">
        <Field label={t('plc.displayName')} error={errors.name}><input value={f.name} onChange={set('name')} /></Field>
        <Field label={t('plc.ipAddress')} error={errors.ipAddress}><input value={f.ip} onChange={set('ip')} /></Field>
        {num(t('plc.tcpPort'), 'port', 'port', t('plc.portHint'))}
        <Field label={t('plc.protocol')} error={errors.protocol}>
          <select value={f.protocol} onChange={set('protocol')}><option value="">{t('plc.notConfirmed')}</option>{PROTOCOLS.map((p) => <option key={p.value} value={p.value}>{t(p.label)}</option>)}</select>
        </Field>
        {num(t('plc.networkNumber'), 'net', 'networkNumber')}
        {num(t('plc.pcNumber'), 'pc', 'pcNumber')}
        {num(t('plc.moduleIo'), 'module', 'moduleIoNumber', t('plc.moduleIoHint'))}
        {num(t('plc.plcStation'), 'station', 'plcStationNumber', t('plc.plcStationHint'))}
        {num(t('plc.monitorTimer'), 'timer', 'monitoringTimer', t('plc.monitorTimerHint'))}
        {num(t('plc.commonTimeout'), 'timeout', 'timeoutMs', t('plc.commonTimeoutHint'))}
        {num(t('plc.connectTimeout'), 'connectTimeout', 'connectTimeoutMs', t('plc.connectTimeoutHint'))}
        {num(t('plc.readTimeout'), 'readTimeout', 'readTimeoutMs', t('plc.readTimeoutHint'))}
        {num(t('plc.writeTimeout'), 'writeTimeout', 'writeTimeoutMs', t('plc.writeTimeoutHint'))}
        <Field label={t('plc.xyBase')} error={errors.xyAddressBase} hint={t('plc.xyHint')}>
          <select value={f.xyBase} onChange={set('xyBase')}><option value="">{t('plc.notConfirmed')}</option><option value="8">{t('plc.octalOpt')}</option><option value="16">{t('plc.hexOptX')}</option></select>
        </Field>
        <Field label={t('plc.wBase')} error={errors.wAddressBase} hint={t('plc.wHint')}>
          <select value={f.wBase} onChange={set('wBase')}><option value="">{t('plc.notConfirmed')}</option><option value="10">{t('plc.decOptW')}</option><option value="16">{t('plc.hexOptW')}</option></select>
        </Field>
        {num(t('plc.pollInterval'), 'poll', 'pollingIntervalMs', t('plc.pollHint'))}
        <div className="field switches">
          <Switch checked={f.enabled} onChange={(v) => setF((p) => ({ ...p, enabled: v }))} label={t('plc.enableLabel')} />
          {realMode && <span className="field-hint">{t('plc.enableHint')}</span>}
          {errors.enabled && <span className="field-error">{errors.enabled}</span>}
        </div>
      </div>
      <div className="btn-row">
        <Button variant="primary" disabled={busy} onClick={() => void save()}>{busy ? t('common.saving') : t('plc.saveConfig')}</Button>
        <Button disabled={busy} onClick={onCancel}>{t('plc.cancel')}</Button>
      </div>
    </>
  )
}
