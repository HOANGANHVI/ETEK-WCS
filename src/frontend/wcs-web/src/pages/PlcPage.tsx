import { useCallback, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api } from '../api/client'
import type { PlcExchange, PlcOverview, PlcStationSummary, PlcWorkerStatus } from '../api/plcTypes'
import { useAuth } from '../auth/AuthContext'
import { Async, Button, EmptyState, PageHeader, Panel, Pill } from '../components/ui'
import { DEMO_MODE } from '../config'
import { useApi } from '../hooks/useApi'
import { APPROVAL_VI, EXCHANGE_OUTCOME_VI, MISSING_PARAM_VI, MODE_TONE, MODE_VI, PHASE_TONE, PHASE_VI } from '../i18n/plc'
import { describeError } from '../i18n/vi'
import { PlcConfigTab } from './plc/PlcConfigTab'
import { PlcLogsTab } from './plc/PlcLogsTab'
import { fmtTime, qualitySummary } from './plc/plcFormat'
import { QualityBar, StatePill } from './plc/plcUi'
import { StationPanel } from './plc/StationPanel'

const TABS = [
  { key: 'overview', label: 'Giám sát', admin: false },
  { key: 'config', label: 'Cấu hình kết nối và địa chỉ', admin: true },
  { key: 'logs', label: 'Nhật ký giao tiếp', admin: true },
] as const

const EVENTS = ['PlcStatusChanged', 'StationIoChanged', 'HandshakePhaseChanged'] as const

/**
 * PLC monitoring (4 PLC, 16 Station, 23 IO per Station). Operators see status, values and quality; Admins additionally see addresses,
 * network details, diagnostics, logs and the configuration. The real-mode safety state is always shown at the top.
 */
export function PlcPage() {
  const { user } = useAuth()
  const isAdmin = user?.role === 'Admin'
  const [params, setParams] = useSearchParams()
  const tab = TABS.find((t) => t.key === params.get('tab') && (!t.admin || isAdmin))?.key ?? 'overview'

  if (DEMO_MODE) {
    return (
      <>
        <PageHeader title="Giám sát PLC" description="4 PLC · 16 trạm · 23 tín hiệu IO mỗi trạm." />
        <EmptyState title="Chế độ Demo không mô phỏng PLC" hint="Trang này đọc dữ liệu từ Backend (chế độ SIMULATOR hoặc PLC thật). Hãy chạy giao diện ở chế độ bình thường để xem." />
      </>
    )
  }

  return (
    <>
      <PageHeader title="Giám sát PLC" description="4 PLC vật lý · 16 trạm · 23 tín hiệu IO mỗi trạm. Mỗi PLC có một kết nối TCP riêng phục vụ 4 trạm của nó." />
      <div className="tabs" role="tablist" aria-label="Nhóm chức năng PLC">
        {TABS.filter((t) => !t.admin || isAdmin).map((t) => (
          <button key={t.key} type="button" role="tab" aria-selected={tab === t.key} className={tab === t.key ? 'tab active' : 'tab'} onClick={() => setParams({ tab: t.key })}>{t.label}</button>
        ))}
      </div>
      {tab === 'overview' && <Overview isAdmin={isAdmin} />}
      {tab === 'config' && isAdmin && <ConfigWrapper />}
      {tab === 'logs' && isAdmin && <PlcLogsTab />}
    </>
  )
}

function ConfigWrapper() {
  const o = useApi(useCallback(() => api.plcOverview(), []))
  return <PlcConfigTab realMode={(o.data?.runtime.effectiveMode ?? 'Simulator') !== 'Simulator'} />
}

function RuntimeBanner({ rt }: { rt: PlcOverview['runtime'] }) {
  return (
    <div className="plc-banner">
      <Pill tone={MODE_TONE[rt.effectiveMode] ?? 'muted'}>{MODE_VI[rt.effectiveMode] ?? rt.effectiveMode}</Pill>
      {rt.mode !== rt.effectiveMode && <span className="muted">(yêu cầu: {MODE_VI[rt.mode] ?? rt.mode})</span>}
      {!rt.running && <Pill tone="warn">Runtime PLC chưa chạy</Pill>}
      <span className="plc-banner-notes">{rt.notes.map((n) => <span key={n}>{n}</span>)}</span>
    </div>
  )
}

function Overview({ isAdmin }: { isAdmin: boolean }) {
  const [params, setParams] = useSearchParams()
  const overview = useApi(useCallback(() => api.plcOverview(), []), [...EVENTS])
  const hs = useApi(useCallback(() => api.plcHandshake(), []), ['HandshakePhaseChanged', 'StationIoChanged'])
  const selected = params.get('station')
  const select = (id: string | null) => { const n = new URLSearchParams(params); if (id) n.set('station', id); else n.delete('station'); setParams(n) }

  return (
    <Async state={overview} empty={{ title: 'Chưa có dữ liệu PLC' }}>
      {(o) => (
        <>
          <RuntimeBanner rt={o.runtime} />
          <div className="plc-cards">
            {o.plcs.map((w) => <PlcCard key={w.plcId} w={w} isAdmin={isAdmin} simulator={o.runtime.effectiveMode === 'Simulator'} onChanged={overview.reload} />)}
          </div>
          <Panel title="16 trạm" subtitle="Chọn một trạm để xem 23 tín hiệu IO. Trạm chưa có quy trình WCS được duyệt chỉ để giám sát, không tạo nhiệm vụ.">
            <div className="station-grid">
              {o.stations.map((s) => {
                const state = hs.data?.find((h) => h.stationId === s.stationId)
                return <StationTile key={s.stationId} s={s} phase={state?.phase} active={selected === s.stationId} onClick={() => select(selected === s.stationId ? null : s.stationId)} />
              })}
            </div>
          </Panel>
          {selected && <StationPanel key={selected} stationId={selected} isAdmin={isAdmin} runtime={o.runtime} handshake={hs.data?.find((h) => h.stationId === selected)} onClose={() => select(null)} />}
        </>
      )}
    </Async>
  )
}

function PlcCard({ w, isAdmin, simulator, onChanged }: { w: PlcWorkerStatus; isAdmin: boolean; simulator: boolean; onChanged: () => void }) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const toggle = async (online: boolean) => {
    setBusy(true); setError(null)
    try { await api.plcSimOnline(w.plcId, online); onChanged() } catch (e) { setError(describeError(e)) } finally { setBusy(false) }
  }
  const d = w.diagnostics
  return (
    <section className={`plc-card state-${w.state}`} aria-label={w.plcId}>
      <header><strong>{w.plcId}</strong><StatePill state={w.state} /></header>
      {isAdmin && <div className="muted small mono">{w.ipAddress}</div>}
      <dl className="kv2">
        <dt>Trạm</dt><dd>{w.stations}</dd>
        <dt>IO</dt><dd>{qualitySummary(w)}</dd>
        <dt>Lần đọc gần nhất</dt><dd>{fmtTime(w.lastPollAt)}</dd>
        <dt>Dữ liệu tốt gần nhất</dt><dd>{fmtTime(w.lastSuccessAt)}</dd>
        <dt>Chu kỳ / Kết nối lại</dt><dd>{w.cycles} / {w.reconnects}</dd>
        {w.consecutiveFailures > 0 && <><dt>Lỗi liên tiếp</dt><dd className="text-bad">{w.consecutiveFailures}</dd></>}
        {isAdmin && d && <><dt>Yêu cầu / lỗi / timeout</dt><dd>{d.requests} / {d.failures} / {d.timeouts}</dd><dt>Độ trễ · End code</dt><dd>{d.lastLatencyMs ?? '—'} ms · {d.lastEndCode === null ? '—' : '0x' + d.lastEndCode.toString(16).toUpperCase().padStart(4, '0')}</dd></>}
      </dl>
      {isAdmin && d && (d.lastFailedExchange || d.lastExchange) && <ExchangeBox last={d.lastExchange} failed={d.lastFailedExchange} />}
      {w.reason && <div className="notice notice-warn">{w.reason}</div>}
      {w.missingParameters.length > 0 && <div className="notice notice-warn">NEEDS_CONFIGURATION: {w.missingParameters.map((m) => MISSING_PARAM_VI[m] ?? m).join(', ')}</div>}
      {isAdmin && w.lastError && <div className="notice notice-bad" role="status">Lỗi gần nhất: {w.lastError}</div>}
      {error && <div className="notice notice-bad" role="alert">{error}</div>}
      {isAdmin && simulator && (
        <div className="btn-row">
          <Button size="sm" disabled={busy} onClick={() => void toggle(false)}>Mô phỏng mất kết nối</Button>
          <Button size="sm" disabled={busy} onClick={() => void toggle(true)}>Khôi phục</Button>
        </div>
      )}
      <QualityBar good={w.goodPoints} configured={w.configuredPoints} total={w.totalPoints} />
    </section>
  )
}

/** Admin-only: the last request/response of a PLC connection as hex, for field diagnosis. Never shown to Operators. */
function ExchangeBox({ last, failed }: { last: PlcExchange | null; failed: PlcExchange | null }) {
  const one = (title: string, x: PlcExchange) => (
    <div className="exchange">
      <strong>{title}</strong>
      <dl className="kv2">
        <dt>Thời điểm</dt><dd>{fmtTime(x.at)} · {x.durationMs} ms</dd>
        <dt>Lệnh</dt><dd>{x.operation === 'READ' ? 'Đọc' : 'Ghi'} {x.command} · {x.device}{x.startAddress} × {x.points}</dd>
        <dt>Kết quả</dt><dd className={x.outcome === 'OK' ? '' : 'text-bad'}>{EXCHANGE_OUTCOME_VI[x.outcome] ?? x.outcome}{x.endCode ? ` · End code 0x${x.endCode.toString(16).toUpperCase().padStart(4, '0')}` : ''}{x.isAmbiguous ? ' · không tự ghi lại' : ''}</dd>
        {x.error && <><dt>Lỗi</dt><dd>{x.error}</dd></>}
      </dl>
      <div className="muted small">Request</div><code className="hex">{x.requestHex}</code>
      <div className="muted small">Response</div><code className="hex">{x.responseHex ?? '(không có phản hồi)'}</code>
    </div>
  )
  return (
    <details className="exchange-box">
      <summary>Giao tiếp gần nhất (chẩn đoán, chỉ Admin)</summary>
      {failed && one('Lần lỗi gần nhất', failed)}
      {last && (!failed || last.at !== failed.at) && one('Lần gần nhất', last)}
    </details>
  )
}

function StationTile({ s, phase, active, onClick }: { s: PlcStationSummary; phase: string | undefined; active: boolean; onClick: () => void }) {
  return (
    <button type="button" className={`station-tile ${active ? 'active' : ''}`} onClick={onClick} aria-pressed={active}>
      <span className="st-head"><strong>{s.stationId}</strong><small className="muted">{s.plcId}</small></span>
      <QualityBar good={s.good} configured={s.configured} total={s.total} />
      <span className="st-meta">{s.good}/{s.total} IO tốt</span>
      <Pill tone={PHASE_TONE[phase ?? 'Unknown'] ?? 'muted'} dot={false}>{PHASE_VI[phase ?? 'Unknown'] ?? phase}</Pill>
      <small className="muted">{APPROVAL_VI.NEEDS_CONFIRMATION}</small>
    </button>
  )
}
