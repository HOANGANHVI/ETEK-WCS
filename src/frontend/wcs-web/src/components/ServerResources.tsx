import { useState, type ReactNode } from 'react'
import type { MetricSource, MetricStatus, ResourceMetric, ResourceSnapshot } from '../api/types'
import { useResourceMonitor } from '../hooks/useResourceMonitor'
import { hasMessage, useT, type MsgKey } from '../i18n'
import { formatDateTime, formatNumber, formatTime } from '../i18n/format'
import { alertsOf, type ConnectionState, formatUptime, scaleBytes, segmentsOf, seriesOf, type HistoryKey } from '../lib/resourceMonitor'
import { Panel, Pill } from './ui'

const STATUS_TONE: Record<MetricStatus, 'ok' | 'warn' | 'bad' | 'muted'> = { Normal: 'ok', Warning: 'warn', Critical: 'bad', Unavailable: 'muted' }
const STATUS_KEY: Record<MetricStatus, MsgKey> = { Normal: 'monitor.statusNormal', Warning: 'monitor.statusWarning', Critical: 'monitor.statusCritical', Unavailable: 'monitor.statusUnavailable' }
const SOURCE_KEY: Record<MetricSource, MsgKey> = { Container: 'monitor.srcContainer', DockerVm: 'monitor.srcDockerVm', Host: 'monitor.srcHost' }
const KEY_LABEL: Record<string, MsgKey> = { cpuPercent: 'monitor.kpiCpu', memoryPercent: 'monitor.kpiRam', diskPercent: 'monitor.kpiDisk', temperatureC: 'monitor.kpiTemp' }
type Scope = 'container' | 'host'

const W = 420, H = 120, ML = 34, MR = 8, MT = 8, MB = 18

/** Line chart with a fixed 0-100 % scale. A missing measurement breaks the line (nothing is interpolated or drawn as zero). */
function PercentChart({ points, labels, tone, aria, tip, gapTip }: { points: (number | null)[]; labels: string[]; tone: 'info' | 'ok' | 'warn' | 'bad'; aria: string; tip: (time: string, v: number) => string; gapTip: (time: string) => string }) {
  const slot = (W - ML - MR) / Math.max(1, points.length - 1)
  const x = (i: number) => ML + slot * i
  const y = (v: number) => MT + (1 - Math.min(100, Math.max(0, v)) / 100) * (H - MT - MB)
  const segs = segmentsOf(points)
  return (
    <svg viewBox={`0 0 ${W} ${H}`} className="chart mon-chart" role="img" aria-label={aria}>
      {[0, 50, 100].map((v) => (
        <g key={v}><line x1={ML} x2={W - MR} y1={y(v)} y2={y(v)} className="ch-grid" /><text x={ML - 6} y={y(v) + 3.5} className="ch-axis" textAnchor="end">{v}%</text></g>
      ))}
      {segs.map((s, k) => (
        <g key={k}>
          {s.length > 1 && <polygon points={`${x(s[0].i)},${y(0)} ${s.map((p) => `${x(p.i)},${y(p.v)}`).join(' ')} ${x(s[s.length - 1].i)},${y(0)}`} className={`mon-area ch-fill-${tone}`} />}
          {s.length > 1 && <polyline points={s.map((p) => `${x(p.i)},${y(p.v)}`).join(' ')} className={`ch-line ch-stroke-${tone}`} />}
          {s.length === 1 && <circle cx={x(s[0].i)} cy={y(s[0].v)} r={3} className={`ch-dot ch-${tone}`} />}
        </g>
      ))}
      {points.map((v, i) => <rect key={i} x={x(i) - slot / 2} y={MT} width={Math.max(2, slot)} height={H - MT - MB} className="mon-hit"><title>{v === null ? gapTip(labels[i]) : tip(labels[i], Math.round(v * 10) / 10)}</title></rect>)}
    </svg>
  )
}

function Card({ label, source, metric, conn, value, hint, progress, whenUnavailable }: {
  label: string; source: MetricSource; metric: ResourceMetric | undefined; conn: ConnectionState; value: ReactNode; hint?: ReactNode; progress?: number | null
  /** Shown instead of N/A when only this metric is unavailable but a related real figure exists (e.g. memory in use when the container has no limit). */
  whenUnavailable?: { value: ReactNode; hint: ReactNode }
}) {
  const t = useT()
  // Until there is current data nothing is shown as a number: "…" while loading, "—" when the connection is lost.
  if (!metric) {
    const lost = conn === 'disconnected'
    return (
      <div className={`mon-card ${lost ? 'mon-lost' : 'mon-unavailable'}`}>
        <div className="mon-card-head"><span className="mon-label">{label}</span><span className={`mon-src mon-src-${source.toLowerCase()}`}>{t(SOURCE_KEY[source])}</span></div>
        <div className="mon-value"><span className="mon-na">{lost ? '—' : '…'}</span></div>
        <div className="mon-bar mon-bar-off" aria-hidden="true" />
        <div className="mon-hint" />
        <div className="mon-foot"><Pill tone="muted">{lost ? t('monitor.connLost') : t('monitor.connLoading')}</Pill></div>
      </div>
    )
  }
  const status: MetricStatus = metric.status
  const reason = metric.reason
  const reasonKey = `monitor.reason_${reason}` as MsgKey
  return (
    <div className={`mon-card mon-${status.toLowerCase()}`}>
      <div className="mon-card-head">
        <span className="mon-label">{label}</span>
        <span className={`mon-src mon-src-${source.toLowerCase()}`} title={t(SOURCE_KEY[source])}>{t(SOURCE_KEY[source])}</span>
      </div>
      <div className="mon-value">{status === 'Unavailable' ? (whenUnavailable ? whenUnavailable.value : <span className="mon-na">{t('monitor.na')}</span>) : value}</div>
      {progress !== undefined && progress !== null && status !== 'Unavailable'
        ? <div className="mon-bar" role="progressbar" aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(progress)} aria-label={label}><span style={{ width: `${Math.min(100, Math.max(0, progress))}%` }} /></div>
        : <div className="mon-bar mon-bar-off" aria-hidden="true" />}
      <div className="mon-hint">
        {status === 'Unavailable'
          ? (whenUnavailable ? whenUnavailable.hint : reason ? (hasMessage(reasonKey) ? t(reasonKey) : t('monitor.reasonUnknown', { code: reason })) : t('monitor.na'))
          : hint}
      </div>
      <div className="mon-foot"><Pill tone={STATUS_TONE[status]}>{t(STATUS_KEY[status])}</Pill></div>
    </div>
  )
}

function Dl({ rows }: { rows: [string, ReactNode][] }) {
  return <dl className="mon-sys">{rows.map(([k, v]) => <div key={k}><dt>{k}</dt><dd>{v}</dd></div>)}</dl>
}

/**
 * Server Resource Monitoring section of the Overview (Admin). Container metrics (the WCS container itself) and host metrics are separate tabs and carry their
 * own source; a Docker Desktop VM is labelled as a VM, never as the Windows host. All figures and statuses come from the server; this component only presents them.
 */
export function ServerResources() {
  const t = useT()
  const { snapshot: s, connection, lastOkAt } = useResourceMonitor(true)
  const [choice, setChoice] = useState<Scope | null>(null)

  const env = s?.environment
  // Until the user picks a tab: the container tab, unless WCS is not in a container (then there is nothing to show there and the host tab is the useful one).
  const scope: Scope = choice ?? (s?.container.cpuPercent?.reason === 'NOT_IN_CONTAINER' ? 'host' : 'container')
  const hostIsVm = env?.hostScope === 'DockerVm'
  const group = s ? (scope === 'container' ? s.container : s.host) : null
  const live = connection === 'live'
  const showValues = (live || connection === 'disabled') && !!group
  const source: MetricSource = scope === 'container' ? 'Container' : (env?.hostScope ?? 'Host')
  const m = (k: string): ResourceMetric | undefined => (showValues ? group?.[k] : undefined)

  const connLabel = connection === 'live' ? t('monitor.connLive') : connection === 'loading' ? t('monitor.connLoading') : connection === 'disabled' ? t('monitor.connDisabled') : t('monitor.connLost')
  const connTone = connection === 'live' ? 'ok' : connection === 'loading' ? 'muted' : connection === 'disabled' ? 'muted' : 'bad'

  const bytes = (n: number) => { const b = scaleBytes(n); return `${formatNumber(Math.round(b.value * 10) / 10)} ${b.unit}` }
  const pct = (v: number) => `${formatNumber(Math.round(v * 10) / 10)}%`
  const uptime = (secs: number) => formatUptime(secs, t)

  const cpu = m('cpuPercent'), ram = m('memoryPercent'), disk = m('diskPercent'), temp = m('temperatureC'), up = m('uptimeSeconds')
  const cores = m('cpuCores')?.value
  const ramUsed = m('memoryUsedBytes')?.value, ramTotal = m('memoryTotalBytes')?.value, ramFree = m('memoryAvailableBytes')?.value
  const diskUsed = m('diskUsedBytes')?.value, diskTotal = m('diskTotalBytes')?.value, diskFree = m('diskFreeBytes')?.value

  const alerts = showValues && group ? alertsOf(group, ['cpuPercent', 'memoryPercent', 'diskPercent']) : []
  const alertText = (status: 'Warning' | 'Critical') => alerts.filter((a) => a.status === status).map((a) => t(KEY_LABEL[a.key])).join(', ')

  const points = s?.history ?? []
  const labels = points.map((p) => formatTime(p.t))
  const windowMin = s ? Math.max(1, Math.round(points.length * s.intervalSeconds / 60)) : 0
  const scopeLabel = scope === 'container' ? t('monitor.scopeContainer') : hostIsVm ? t('monitor.scopeVm') : t('monitor.scopeHost')
  const chart = (key: HistoryKey, title: MsgKey) => {
    const values = seriesOf(points, scope, key)
    const has = values.some((v) => v !== null)
    return (
      <div className="mon-chart-box">
        <div className="mon-chart-title"><strong>{t(title)}</strong><span>{points.length > 0 ? t('monitor.chartWindow', { min: windowMin }) : ''}</span></div>
        {!has ? <div className="chart-empty">{t('monitor.chartNone')}</div> : (
          <div className={live ? '' : 'mon-dim'}>
            <PercentChart points={values} labels={labels} tone="info" aria={t('monitor.chartAria', { what: t(title), scope: scopeLabel })}
              tip={(time, v) => t('monitor.chartTip', { time, value: v })} gapTip={(time) => t('monitor.chartTipGap', { time })} />
          </div>
        )}
      </div>
    )
  }

  const sysRows: [string, ReactNode][] = env ? [
    ...(scope === 'host' && env.hostname ? [[t('monitor.sysHostname'), env.hostname] as [string, ReactNode]] : []),
    ...(env.containerHostname ? [[t('monitor.sysContainerId'), <span className="mono" key="cid">{env.containerHostname}</span>] as [string, ReactNode]] : []),
    [t('monitor.sysOs'), env.operatingSystem || t('monitor.sysUnknown')],
    [t('monitor.sysArch'), env.architecture],
    [t('monitor.sysRuntime'), env.inContainer ? (env.containerRuntime ?? t('monitor.sysUnknown')) : t('monitor.sysRuntimeNone')],
    [t('monitor.sysScope'), hostIsVm ? t('monitor.sysScopeVm') : t('monitor.sysScopeHost', { platform: env.platform })],
    [t('monitor.sysUptime'), up?.value != null ? uptime(up.value) : t('monitor.na')],
    [t('monitor.sysUpdated'), s?.sampledAt ? formatDateTime(s.sampledAt) : '—'],
    [t('monitor.sysInterval'), s ? t('monitor.sysIntervalValue', { s: s.intervalSeconds }) : '—'],
  ] : []

  const tabs: { key: Scope; label: string }[] = [{ key: 'container', label: t('monitor.scopeContainer') }, { key: 'host', label: hostIsVm ? t('monitor.scopeVm') : t('monitor.scopeHost') }]

  return (
    <Panel title={t('monitor.title')} subtitle={t('monitor.subtitle')} className="dc-12 mon-panel" actions={(
      <div className="mon-actions">
        <Pill tone={connTone}>{connLabel}</Pill>
        <div className="range-switch" role="group" aria-label={t('monitor.scopeAria')}>
          {tabs.map((tab) => <button key={tab.key} type="button" className={scope === tab.key ? 'active' : ''} aria-pressed={scope === tab.key} onClick={() => setChoice(tab.key)}>{tab.label}</button>)}
        </div>
      </div>
    )}>
      <div className="pad mon-body">
        {connection === 'disconnected' && (
          <div className="notice notice-bad" role="alert">{lastOkAt ? t('monitor.bannerLost', { time: formatTime(new Date(lastOkAt).toISOString()) }) : t('monitor.bannerNever')}</div>
        )}
        {connection === 'disabled' && <div className="notice notice-info">{t('monitor.bannerDisabled')}</div>}
        {alertText('Critical') && <div className="notice notice-bad" role="alert">{t('monitor.alertCrit', { items: alertText('Critical') })}</div>}
        {alertText('Warning') && <div className="notice notice-warn" role="status">{t('monitor.alertWarn', { items: alertText('Warning') })}</div>}
        {scope === 'host' && s && hostIsVm && <div className="notice notice-warn">{t('monitor.noticeVm')}</div>}
        {scope === 'host' && s && !hostIsVm && env?.hostAgentRequired && <div className="notice notice-warn">{t('monitor.noticeAgent')}</div>}

        <div className="mon-cards">
          <Card label={t('monitor.kpiCpu')} source={source} metric={m('cpuPercent')} conn={connection} progress={cpu?.value}
            value={cpu?.value != null ? pct(cpu.value) : null} hint={cores != null ? t(scope === 'container' ? 'monitor.cpuCoresContainer' : 'monitor.cpuCoresHost', { cores: formatNumber(Math.round(cores * 10) / 10) }) : undefined} />
          <Card label={t('monitor.kpiRam')} source={source} metric={m('memoryPercent')} conn={connection} progress={ram?.value}
            whenUnavailable={ram?.reason === 'NO_MEMORY_LIMIT' && ramUsed != null ? { value: bytes(ramUsed), hint: t('monitor.ramUsedOnly', { used: bytes(ramUsed) }) } : undefined}
            value={ram?.value != null ? pct(ram.value) : null}
            hint={ramUsed != null && ramTotal != null && ramFree != null ? t('monitor.ramUsage', { used: bytes(ramUsed), total: bytes(ramTotal), free: bytes(ramFree) }) : undefined} />
          <Card label={t('monitor.kpiDisk')} source={source} metric={m('diskPercent')} conn={connection} progress={disk?.value}
            value={disk?.value != null ? pct(disk.value) : null}
            hint={diskUsed != null && diskTotal != null && diskFree != null ? <>{t('monitor.diskUsage', { used: bytes(diskUsed), total: bytes(diskTotal), free: bytes(diskFree) })}<small className="mon-note">{t(scope === 'container' ? 'monitor.diskContainerNote' : 'monitor.diskHostNote')}</small></> : undefined} />
          {scope === 'host'
            ? <Card label={t('monitor.kpiTemp')} source={source} metric={m('temperatureC')} conn={connection} value={temp?.value != null ? `${formatNumber(Math.round(temp.value * 10) / 10)} °C` : null} hint={t('monitor.tempHint')} />
            : <Card label={t('monitor.kpiUptime')} source={source} metric={m('uptimeSeconds')} conn={connection} value={up?.value != null ? uptime(up.value) : null} />}
        </div>

        <div className="mon-lower">
          <div className="mon-charts">{chart('cpu', 'monitor.chartCpu')}{chart('memory', 'monitor.chartRam')}</div>
          <div className="mon-sysbox"><h3 className="mini-title">{t('monitor.sysTitle')}</h3><Dl rows={sysRows} /></div>
        </div>
      </div>
    </Panel>
  )
}

export type { ResourceSnapshot }
