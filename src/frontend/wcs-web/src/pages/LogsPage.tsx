import { useCallback, useEffect, useMemo, useState } from 'react'
import { api } from '../api/client'
import { LOG_CATEGORIES, LOG_LEVELS, type LogCategory, type LogDetail, type LogEntry, type LogLevel } from '../api/logTypes'
import { Modal } from '../components/Modal'
import { Async, Button, EmptyState, Kpi, PageHeader, Panel, Pill } from '../components/ui'
import { DEMO_MODE } from '../config'
import { useApi } from '../hooks/useApi'
import { formatDateTime } from '../i18n/format'
import { LOG_CATEGORY_HINT, LOG_CATEGORY_VI, LOG_LEVEL_TONE, LOG_LEVEL_VI } from '../i18n/logs'
import { describeError } from '../i18n/vi'

const PAGE_SIZES = [25, 50, 100, 200]
const RANGES = [
  { key: '1h', label: '1 giờ qua', hours: 1 }, { key: '24h', label: '24 giờ qua', hours: 24 },
  { key: '7d', label: '7 ngày qua', hours: 24 * 7 }, { key: '30d', label: '30 ngày qua', hours: 24 * 30 }, { key: 'custom', label: 'Tùy chọn…', hours: 0 },
] as const

const LevelPill = ({ level }: { level: LogLevel }) => <Pill tone={LOG_LEVEL_TONE[level] ?? 'muted'}>{LOG_LEVEL_VI[level] ?? level}</Pill>
const toIso = (local: string) => (local ? new Date(local).toISOString() : undefined)

/**
 * CR-007: Logging & Diagnostics - one screen for SYSTEM, PLC, DATABASE, DEPLOYMENT, RCS/AGV and SECURITY/AUDIT events (Admin only; the API enforces it as well).
 * Everything shown comes from the backend (no sample data): an empty database shows an empty list, an unreachable backend shows an error.
 */
export function LogsPage() {
  if (DEMO_MODE) {
    return (
      <>
        <PageHeader title="Nhật ký hệ thống" />
        <EmptyState title="Không có dữ liệu" hint="Màn hình này đọc nhật ký từ Backend; chưa có kết nối Backend nên không có bản ghi nào để hiển thị." />
      </>
    )
  }
  return <LogsScreen />
}

function LogsScreen() {
  const [range, setRange] = useState<(typeof RANGES)[number]['key']>('24h')
  const [customFrom, setCustomFrom] = useState('')
  const [customTo, setCustomTo] = useState('')
  const [category, setCategory] = useState<LogCategory | ''>('')
  const [level, setLevel] = useState<LogLevel | ''>('')
  const [device, setDevice] = useState('')
  const [source, setSource] = useState('')
  const [eventCode, setEventCode] = useState('')
  const [keyword, setKeyword] = useState('')
  const [applied, setApplied] = useState({ device: '', source: '', eventCode: '', keyword: '' })   // text filters apply on Enter / "Tìm"
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(50)
  const [now, setNow] = useState(() => Date.now())
  const [openId, setOpenId] = useState<string | null>(null)

  // The window is computed when the user changes the range (or presses refresh), not on every render, so the request key stays stable.
  const window = useMemo(() => {
    if (range === 'custom') return { from: toIso(customFrom), to: toIso(customTo) }
    const hours = RANGES.find((r) => r.key === range)?.hours ?? 24
    return { from: new Date(now - hours * 3600_000).toISOString(), to: new Date(now).toISOString() }
  }, [range, customFrom, customTo, now])

  const stats = useApi(useCallback(() => api.logsStats(window.from, window.to), [window]))
  const list = useApi(useCallback(
    () => api.logsSearch({ from: window.from, to: window.to, category: category || undefined, level: level || undefined, device: applied.device.trim() || undefined,
      source: applied.source.trim() || undefined, eventCode: applied.eventCode.trim() || undefined, keyword: applied.keyword.trim() || undefined, page, pageSize }),
    [window, category, level, applied, page, pageSize]))

  const refresh = () => { setNow(Date.now()); stats.reload(); list.reload() }
  const apply = () => { setApplied({ device, source, eventCode, keyword }); setPage(1) }
  const clear = () => { setCategory(''); setLevel(''); setDevice(''); setSource(''); setEventCode(''); setKeyword(''); setApplied({ device: '', source: '', eventCode: '', keyword: '' }); setPage(1) }

  const s = stats.data
  const totalPages = list.data ? Math.max(1, Math.ceil(list.data.total / list.data.pageSize)) : 1

  return (
    <>
      <PageHeader title="Nhật ký hệ thống" actions={<Button onClick={refresh}>Làm mới</Button>} />

      {/* ------------------------------------------------------------------ dashboard */}
      <div className="kpi-row logs-kpis" aria-label="Thống kê nhật ký">
        <Kpi label="Tổng số log" value={s ? s.total.toLocaleString('vi-VN') : '—'} hint={RANGES.find((r) => r.key === range)?.label} tone="info" />
        <Kpi label="Thông tin" value={s ? s.byLevel.Information : '—'} tone="info" />
        <Kpi label="Cảnh báo" value={s ? s.byLevel.Warning : '—'} tone="warn" />
        <Kpi label="Lỗi" value={s ? s.byLevel.Error : '—'} tone="bad" />
        <Kpi label="Nghiêm trọng" value={s ? s.byLevel.Critical : '—'} tone="bad" />
      </div>
      {stats.error && !s && <div className="notice notice-bad" role="alert">Không tải được thống kê: {stats.error}</div>}

      <div className="logs-dash">
        <Panel title="Theo nhóm">
          <div className="log-groups">
            {LOG_CATEGORIES.map((c) => (
              <button key={c} type="button" className={`log-group ${category === c ? 'active' : ''}`} aria-pressed={category === c} onClick={() => { setCategory(category === c ? '' : c); setPage(1) }}>
                <strong>{LOG_CATEGORY_VI[c]}</strong><span className="log-group-count num">{s ? s.byCategory[c] : '—'}</span>
                <small className="muted">{LOG_CATEGORY_HINT[c]}</small>
              </button>
            ))}
          </div>
        </Panel>
        <Panel title="Lỗi gần nhất">
          {!s ? <p className="muted pad">Đang tải…</p> : s.latestErrors.length === 0 ? <p className="muted pad">Không có lỗi trong khoảng thời gian này.</p> : (
            <ul className="log-latest">
              {s.latestErrors.map((e) => (
                <li key={e.id}><button type="button" onClick={() => setOpenId(e.id)}>
                  <LevelPill level={e.level} /> <span className="nowrap num muted">{formatDateTime(e.occurredAt)}</span> <small className="muted">{LOG_CATEGORY_VI[e.category]}{e.device ? ` · ${e.device}` : ''}</small>
                  <span className="log-latest-msg">{e.message}</span>
                </button></li>
              ))}
            </ul>
          )}
        </Panel>
        <Panel title="Thiết bị đang có sự cố">
          {!s ? <p className="muted pad">Đang tải…</p> : s.problemDevices.length === 0 ? <p className="muted pad">Không có thiết bị nào ghi nhận cảnh báo hoặc lỗi.</p> : (
            <div className="table-wrap"><table className="grid clickable">
              <thead><tr><th>Thiết bị</th><th>Nhóm</th><th>Lỗi</th><th>Cảnh báo</th><th>Gần nhất</th></tr></thead>
              <tbody>
                {s.problemDevices.map((d) => (
                  <tr key={d.device + d.category} onClick={() => { setDevice(d.device); setApplied((a) => ({ ...a, device: d.device })); setCategory(d.category); setPage(1) }}>
                    <td><strong>{d.device}</strong></td><td>{LOG_CATEGORY_VI[d.category]}</td>
                    <td className={d.errors ? 'text-bad num' : 'num'}>{d.errors}</td><td className="num">{d.warnings}</td><td className="nowrap num">{formatDateTime(d.lastAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table></div>
          )}
        </Panel>
      </div>

      {/* ------------------------------------------------------------------ filters */}
      <Panel title="Bộ lọc">
        <form className="form-grid logs-filters" onSubmit={(e) => { e.preventDefault(); apply() }}>
          <label className="field"><span className="field-label">Khoảng thời gian</span>
            <select value={range} onChange={(e) => { setNow(Date.now()); setRange(e.target.value as typeof range); setPage(1) }}>{RANGES.map((r) => <option key={r.key} value={r.key}>{r.label}</option>)}</select></label>
          {range === 'custom' && <>
            <label className="field"><span className="field-label">Từ ngày</span><input type="datetime-local" value={customFrom} onChange={(e) => { setCustomFrom(e.target.value); setPage(1) }} /></label>
            <label className="field"><span className="field-label">Đến ngày</span><input type="datetime-local" value={customTo} onChange={(e) => { setCustomTo(e.target.value); setPage(1) }} /></label>
          </>}
          <label className="field"><span className="field-label">Nhóm</span>
            <select value={category} onChange={(e) => { setCategory(e.target.value as LogCategory | ''); setPage(1) }}><option value="">Tất cả</option>{LOG_CATEGORIES.map((c) => <option key={c} value={c}>{LOG_CATEGORY_VI[c]}</option>)}</select></label>
          <label className="field"><span className="field-label">Mức độ</span>
            <select value={level} onChange={(e) => { setLevel(e.target.value as LogLevel | ''); setPage(1) }}><option value="">Tất cả</option>{LOG_LEVELS.map((l) => <option key={l} value={l}>{LOG_LEVEL_VI[l]}</option>)}</select></label>
          <label className="field"><span className="field-label">Thiết bị</span>
            <input list="log-devices" value={device} onChange={(e) => setDevice(e.target.value)} placeholder="ví dụ PLC-01" />
            <datalist id="log-devices">{(s?.devices ?? []).map((d) => <option key={d} value={d} />)}</datalist></label>
          <label className="field"><span className="field-label">Nguồn / Module</span><input value={source} onChange={(e) => setSource(e.target.value)} placeholder="ví dụ Scheduler" /></label>
          <label className="field"><span className="field-label">Mã sự kiện</span><input value={eventCode} onChange={(e) => setEventCode(e.target.value)} placeholder="ví dụ WRITE_AMBIGUOUS" /></label>
          <label className="field"><span className="field-label">Từ khóa</span><input value={keyword} onChange={(e) => setKeyword(e.target.value)} placeholder="tìm trong nội dung" /></label>
        </form>
        <div className="btn-row">
          <Button variant="primary" onClick={apply}>Tìm</Button>
          <Button onClick={clear}>Xóa bộ lọc</Button>
        </div>
      </Panel>

      {/* ------------------------------------------------------------------ table */}
      <Panel title="Nhật ký" subtitle={list.data ? `${list.data.total.toLocaleString('vi-VN')} bản ghi · mới nhất trước` : undefined} flush
        actions={<label className="inline-field">Số dòng / trang <select aria-label="Số dòng mỗi trang" value={pageSize} onChange={(e) => { setPageSize(Number(e.target.value)); setPage(1) }}>{PAGE_SIZES.map((n) => <option key={n} value={n}>{n}</option>)}</select></label>}>
        <Async state={list} isEmpty={(d) => d.items.length === 0} empty={{ title: 'Không có bản ghi nào khớp bộ lọc', hint: 'Thử mở rộng khoảng thời gian hoặc xóa bộ lọc.' }}>
          {(data) => (
            <>
              <div className="table-wrap">
                <table className="grid clickable log-table" aria-label="Nhật ký hệ thống">
                  <thead><tr><th>Thời gian</th><th>Mức độ</th><th>Nhóm</th><th>Nguồn</th><th>Thiết bị</th><th>Sự kiện</th><th>Nội dung</th></tr></thead>
                  <tbody>
                    {data.items.map((e: LogEntry) => (
                      <tr key={e.id} className={`log-row log-${e.level.toLowerCase()} ${openId === e.id ? 'selected' : ''}`} tabIndex={0} onClick={() => setOpenId(e.id)}
                        onKeyDown={(k) => { if (k.key === 'Enter') setOpenId(e.id) }}>
                        <td className="nowrap num">{formatDateTime(e.occurredAt)}</td>
                        <td><LevelPill level={e.level} /></td>
                        <td className="nowrap">{LOG_CATEGORY_VI[e.category]}</td>
                        <td className="small" title={e.source}>{e.source.split('.').slice(-1)[0]}</td>
                        <td className="nowrap">{e.device ?? <span className="muted">—</span>}</td>
                        <td className="mono nowrap">{e.event ?? <span className="muted">—</span>}</td>
                        <td className="log-msg">{e.message}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <div className="pad pager">
                <Button size="sm" disabled={page <= 1} onClick={() => setPage(page - 1)}>‹ Trước</Button>
                <span className="muted">Trang {data.page} / {totalPages}</span>
                <Button size="sm" disabled={page >= totalPages} onClick={() => setPage(page + 1)}>Sau ›</Button>
              </div>
            </>
          )}
        </Async>
      </Panel>

      {openId && <LogDetailDialog id={openId} onClose={() => setOpenId(null)} />}
    </>
  )
}

function pretty(json: string | null) {
  if (!json) return null
  try { return JSON.stringify(JSON.parse(json), null, 2) } catch { return json }
}

function LogDetailDialog({ id, onClose }: { id: string; onClose: () => void }) {
  const [detail, setDetail] = useState<LogDetail | null>(null)
  const [error, setError] = useState<string | null>(null)
  useEffect(() => {
    let cancelled = false
    api.logDetail(id).then((d) => { if (!cancelled) setDetail(d) }).catch((e: unknown) => { if (!cancelled) setError(describeError(e)) })
    return () => { cancelled = true }
  }, [id])

  const rows: [string, string | number | null | undefined][] = detail ? [
    ['Thời gian', formatDateTime(detail.occurredAt)], ['Nhóm', LOG_CATEGORY_VI[detail.category]], ['Mức độ', LOG_LEVEL_VI[detail.level]],
    ['Module', detail.source], ['Mã thiết bị', detail.device], ['Trạm', detail.station], ['Mã sự kiện', detail.event],
    ['Correlation ID', detail.correlationId], ['Máy chủ', detail.host],
    ['IP', detail.ip], ['Cổng', detail.port],
    ['Trạng thái trước', detail.previousState], ['Trạng thái sau', detail.newState], ['Loại lỗi', detail.failureKind], ['MC End Code', detail.endCode], ['Lý do', detail.reason],
    ['Người thực hiện', detail.actor ? `${detail.actor}${detail.actorRole ? ` (${detail.actorRole})` : ''}` : null], ['Kết quả', detail.result],
    ['Đối tượng', [detail.targetType, detail.targetId].filter(Boolean).join(' ') || null],
  ] : []

  return (
    <Modal title="Chi tiết bản ghi log" onClose={onClose} footer={<Button onClick={onClose}>Đóng</Button>}>
      {error && <div className="notice notice-bad" role="alert">{error}</div>}
      {!detail && !error && <p className="muted">Đang tải…</p>}
      {detail && (
        <div className="log-detail">
          <div className="log-detail-head"><LevelPill level={detail.level} /> <strong>{LOG_CATEGORY_VI[detail.category]}</strong></div>
          <p className="log-detail-msg">{detail.message}</p>
          <dl className="kv2">
            {rows.filter(([, v]) => v !== null && v !== undefined && v !== '').map(([k, v]) => <><dt key={k + 'k'}>{k}</dt><dd key={k + 'v'}>{v}</dd></>)}
          </dl>
          {detail.exception && <><strong>Exception / Stack trace</strong><pre className="log-pre">{detail.exception}</pre></>}
          {detail.beforeJson && <><strong>Trước</strong><pre className="log-pre">{pretty(detail.beforeJson)}</pre></>}
          {detail.afterJson && <><strong>Sau</strong><pre className="log-pre">{pretty(detail.afterJson)}</pre></>}
          {detail.detailsJson && <><strong>Thuộc tính</strong><pre className="log-pre">{pretty(detail.detailsJson)}</pre></>}
          <p className="muted small">Thông tin bảo mật được ẩn khi hiển thị.</p>
        </div>
      )}
    </Modal>
  )
}
