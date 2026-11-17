import { useCallback } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api } from '../api/client'
import type { PlcExchange, PlcOverview, PlcStationSummary, PlcWorkerStatus } from '../api/plcTypes'
import { useAuth } from '../auth/AuthContext'
import { Async, EmptyState, PageHeader, Panel, Pill } from '../components/ui'
import { DEMO_MODE } from '../config'
import { useApi } from '../hooks/useApi'
import { useT, type MsgKey } from '../i18n'
import { APPROVAL_VI, EXCHANGE_OUTCOME_VI, localizeDeviceMessage, MISSING_PARAM_VI, MODE_TONE, MODE_VI, PHASE_TONE, PHASE_VI } from '../i18n/plc'
import { describeCoded } from '../i18n/logCatalog'
import { PlcLogsTab } from './plc/PlcLogsTab'
import { fmtTime, qualitySummary } from './plc/plcFormat'
import { QualityBar, StatePill } from './plc/plcUi'
import { StationPanel } from './plc/StationPanel'

const TABS: { key: string; label: MsgKey; admin: boolean }[] = [
  { key: 'overview', label: 'plc.tabOverview', admin: false },
  { key: 'logs', label: 'plc.tabLogs', admin: true },
]

const EVENTS = ['PlcStatusChanged', 'StationIoChanged', 'HandshakePhaseChanged'] as const

/**
 * PLC monitoring (4 PLC, 16 Station, 23 IO per Station) - strictly READ-ONLY (CR-004). Operators see status, values and quality; Admins additionally see
 * network details, diagnostics and logs. Connections and IO addresses are edited in the "Cấu hình PLC" screens. The real-mode safety state is always shown.
 */
export function PlcPage() {
  const t = useT()
  const { user } = useAuth()
  const isAdmin = user?.role === 'Admin'
  const [params, setParams] = useSearchParams()
  const tab = TABS.find((x) => x.key === params.get('tab') && (!x.admin || isAdmin))?.key ?? 'overview'

  if (DEMO_MODE) {
    return (
      <>
        <PageHeader title={t('plc.monitorTitle')} />
        <EmptyState title={t('plc.noPlcData')} hint={t('plc.noPlcDataHint')} />
      </>
    )
  }

  return (
    <>
      <PageHeader title={t('plc.monitorTitle')} />
      <div className="tabs" role="tablist" aria-label={t('plc.tabsLabel')}>
        {TABS.filter((x) => !x.admin || isAdmin).map((x) => (
          <button key={x.key} type="button" role="tab" aria-selected={tab === x.key} className={tab === x.key ? 'tab active' : 'tab'} onClick={() => setParams({ tab: x.key })}>{t(x.label)}</button>
        ))}
      </div>
      {tab === 'overview' && <Overview isAdmin={isAdmin} />}
      {tab === 'logs' && isAdmin && <PlcLogsTab />}
    </>
  )
}

function RuntimeBanner({ rt }: { rt: PlcOverview['runtime'] }) {
  const t = useT()
  return (
    <div className="plc-banner">
      <Pill tone={MODE_TONE[rt.effectiveMode] ?? 'muted'}>{MODE_VI[rt.effectiveMode] ?? rt.effectiveMode}</Pill>
      {rt.mode !== rt.effectiveMode && <span className="muted">{t('plc.requested', { mode: MODE_VI[rt.mode] ?? rt.mode })}</span>}
      {!rt.running && <Pill tone="warn">{t('plc.runtimeStopped')}</Pill>}
      <span className="plc-banner-notes">{rt.effectiveMode === 'Simulator' ? null : rt.notes.map((n) => <span key={n} title={n}>{describeCoded(n)}</span>)}</span>
    </div>
  )
}

function Overview({ isAdmin }: { isAdmin: boolean }) {
  const t = useT()
  const [params, setParams] = useSearchParams()
  const overview = useApi(useCallback(() => api.plcOverview(), []), [...EVENTS])
  const hs = useApi(useCallback(() => api.plcHandshake(), []), ['HandshakePhaseChanged', 'StationIoChanged'])
  const selected = params.get('station')
  const select = (id: string | null) => { const n = new URLSearchParams(params); if (id) n.set('station', id); else n.delete('station'); setParams(n) }

  return (
    <Async state={overview} empty={{ title: t('plc.noPlcDataShort') }}>
      {(o) => { const real = o.runtime.effectiveMode !== 'Simulator'; return (
        <>
          <RuntimeBanner rt={o.runtime} />
          <div className="plc-cards">
            {o.plcs.map((w) => <PlcCard key={w.plcId} w={w} isAdmin={isAdmin} real={real} />)}
          </div>
          <Panel title={t('plc.stationsPanel')}>
            <div className="station-grid">
              {o.stations.map((s) => {
                const state = hs.data?.find((h) => h.stationId === s.stationId)
                return <StationTile key={s.stationId} s={s} real={real} phase={real ? state?.phase : undefined} active={selected === s.stationId} onClick={() => select(selected === s.stationId ? null : s.stationId)} />
              })}
            </div>
          </Panel>
          {selected && <StationPanel key={selected} stationId={selected} isAdmin={isAdmin} handshake={hs.data?.find((h) => h.stationId === selected)} onClose={() => select(null)} />}
        </>
      ) }}
    </Async>
  )
}

/** `real` = a real PLC runtime. Without it nothing here may look connected or healthy: no real device is being read. */
function PlcCard({ w, isAdmin, real }: { w: PlcWorkerStatus; isAdmin: boolean; real: boolean }) {
  const t = useT()
  const d = w.diagnostics
  return (
    <section className={`plc-card ${real ? `state-${w.state}` : 'state-Disconnected'}`} aria-label={w.plcId}>
      <header><strong>{w.plcId}</strong>{real ? <StatePill state={w.state} /> : <Pill tone="muted">{t('plc.notConnected')}</Pill>}</header>
      {isAdmin && <div className="muted small mono">{w.ipAddress}</div>}
      <dl className="kv2">
        <dt>{t('plc.rowStations')}</dt><dd>{w.stations}</dd>
        <dt>{t('plc.rowIo')}</dt><dd>{real ? qualitySummary(w) : t('plc.noDeviceData')}</dd>
        <dt>{t('plc.rowLastPoll')}</dt><dd>{fmtTime(w.lastPollAt)}</dd>
        <dt>{t('plc.rowLastGood')}</dt><dd>{fmtTime(w.lastSuccessAt)}</dd>
        <dt>{t('plc.rowCycles')}</dt><dd>{w.cycles} / {w.reconnects}</dd>
        {w.consecutiveFailures > 0 && <><dt>{t('plc.rowFailures')}</dt><dd className="text-bad">{w.consecutiveFailures}</dd></>}
        {isAdmin && d && <><dt>{t('plc.rowReqFail')}</dt><dd>{d.requests} / {d.failures} / {d.timeouts}</dd><dt>{t('plc.rowLatency')}</dt><dd>{d.lastLatencyMs ?? '—'} ms · {d.lastEndCode === null ? '—' : '0x' + d.lastEndCode.toString(16).toUpperCase().padStart(4, '0')}</dd></>}
      </dl>
      {isAdmin && d && (d.lastFailedExchange || d.lastExchange) && <ExchangeBox last={d.lastExchange} failed={d.lastFailedExchange} />}
      {w.reason && <div className="notice notice-warn">{localizeDeviceMessage(describeCoded(w.reason))}</div>}
      {w.missingParameters.length > 0 && <div className="notice notice-warn">{t('plc.missingParams', { list: w.missingParameters.map((m) => MISSING_PARAM_VI[m] ?? m).join(', ') })}</div>}
      {isAdmin && w.lastError && <div className="notice notice-bad" role="status">{t('plc.lastError', { error: localizeDeviceMessage(describeCoded(w.lastError)) })}</div>}
      {real && <QualityBar good={w.goodPoints} configured={w.configuredPoints} total={w.totalPoints} />}
    </section>
  )
}

/** Admin-only: the last request/response of a PLC connection as hex, for field diagnosis. Never shown to Operators. */
function ExchangeBox({ last, failed }: { last: PlcExchange | null; failed: PlcExchange | null }) {
  const t = useT()
  const one = (title: string, x: PlcExchange) => (
    <div className="exchange">
      <strong>{title}</strong>
      <dl className="kv2">
        <dt>{t('plc.exchTime')}</dt><dd>{fmtTime(x.at)} · {x.durationMs} ms</dd>
        <dt>{t('plc.exchCommand')}</dt><dd>{x.operation === 'READ' ? t('plc.exchRead') : t('plc.exchWrite')} {x.command} · {x.device}{x.startAddress} × {x.points}</dd>
        <dt>{t('plc.exchResult')}</dt><dd className={x.outcome === 'OK' ? '' : 'text-bad'}>{EXCHANGE_OUTCOME_VI[x.outcome] ?? x.outcome}{x.endCode ? ` · End code 0x${x.endCode.toString(16).toUpperCase().padStart(4, '0')}` : ''}{x.isAmbiguous ? t('plc.exchNoRetry') : ''}</dd>
        {x.error && <><dt>{t('plc.exchError')}</dt><dd>{x.error}</dd></>}
      </dl>
      <div className="muted small">Request</div><code className="hex">{x.requestHex}</code>
      <div className="muted small">Response</div><code className="hex">{x.responseHex ?? t('plc.exchNoResponse')}</code>
    </div>
  )
  return (
    <details className="exchange-box">
      <summary>{t('plc.exchSummary')}</summary>
      {failed && one(t('plc.exchLastFailed'), failed)}
      {last && (!failed || last.at !== failed.at) && one(t('plc.exchLast'), last)}
    </details>
  )
}

function StationTile({ s, phase, active, onClick, real }: { s: PlcStationSummary; phase: string | undefined; active: boolean; onClick: () => void; real: boolean }) {
  const t = useT()
  return (
    <button type="button" className={`station-tile ${active ? 'active' : ''}`} onClick={onClick} aria-pressed={active}>
      <span className="st-head"><strong>{s.stationId}</strong><small className="muted">{s.plcId}</small></span>
      {real && <QualityBar good={s.good} configured={s.configured} total={s.total} />}
      <span className="st-meta">{real ? t('plc.tileGood', { good: s.good, total: s.total }) : t('plc.tileNoData', { total: s.total })}</span>
      <Pill tone={PHASE_TONE[phase ?? 'Unknown'] ?? 'muted'} dot={false}>{PHASE_VI[phase ?? 'Unknown'] ?? phase}</Pill>
      <small className="muted">{APPROVAL_VI.NEEDS_CONFIRMATION}</small>
    </button>
  )
}
