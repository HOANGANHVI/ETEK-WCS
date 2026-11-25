import { useCallback, useState } from 'react'
import { api } from '../api/client'
import type { SimFlow, SimStartResult } from '../api/simTypes'
import { Async, Button, EmptyState, PageHeader } from '../components/ui'
import { DEMO_MODE } from '../config'
import { useApi } from '../hooks/useApi'

/**
 * CR-035 – Admin manual PLC simulation. Only the PLC is simulated: requests go through the same WCS path a PLC would use, the RCS is the REAL one.
 * Every state shown here is read from the backend; pressing a button never changes a state by itself (the server decides and may refuse).
 * Hiding this page for other roles is a convenience: the server enforces Admin on every call.
 */
export function PlcSimulationPage() {
  if (DEMO_MODE) {
    return (
      <>
        <PageHeader title="Mô phỏng PLC (thủ công)" />
        <EmptyState title="Không khả dụng ở chế độ demo" hint="Chức năng này chỉ chạy với backend thật và RCS thật." />
      </>
    )
  }
  return <Screen />
}

function Screen() {
  const status = useApi(useCallback(() => api.simStatus(), []))
  const flows = useApi(useCallback(() => api.simFlows(), []))
  const orders = useApi(useCallback(() => api.routingOrders(undefined, 20), []), ['OrderChanged'])
  const [msg, setMsg] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [flowCode, setFlowCode] = useState('')
  const [location, setLocation] = useState('')
  const [ack, setAck] = useState(false)
  const [last, setLast] = useState<SimStartResult | null>(null)

  const run = async (fn: () => Promise<unknown>, ok: string) => {
    setBusy(true); setMsg(null)
    try { await fn(); setMsg(ok) } catch (e) { setMsg(e instanceof Error ? e.message : String(e)) } finally { setBusy(false); status.reload(); flows.reload(); orders.reload() }
  }

  return (
    <>
      <PageHeader title="Mô phỏng PLC (thủ công) – chỉ Admin" description="PLC được mô phỏng bởi Admin; RCS và AMR là thật." />
      <div className="stack">
        <Async state={status} empty={{ title: 'Không có dữ liệu' }}>
          {(s) => (
            <div className="card pad stack">
              <strong style={{ color: s.active ? 'var(--danger, #b00020)' : undefined }}>{s.banner}{s.active ? ' – ĐANG BẬT' : ' – đang tắt'}</strong>
              <span className="muted">{s.mappingSource}</span>
              {s.blockers.length > 0 && <ul>{s.blockers.map((b) => <li key={b}>{b}</li>)}</ul>}
              <div>
                {!s.active
                  ? <Button variant="primary" disabled={busy || !s.allowed || !s.realRcsDispatchEnabled} onClick={() => run(() => api.simActivate(), 'Đã bật mô phỏng PLC')}>Bật mô phỏng PLC</Button>
                  : <Button disabled={busy} onClick={() => run(() => api.simDeactivate(), 'Đã tắt mô phỏng PLC')}>Tắt mô phỏng PLC</Button>}
              </div>
            </div>
          )}
        </Async>

        {msg && <div className="card pad" role="status">{msg}</div>}

        <Async state={flows} empty={{ title: 'Chưa có Process/Flow' }} isEmpty={(f) => f.length === 0}>
          {(list: SimFlow[]) => {
            const sel = list.find((f) => f.code === flowCode)
            return (
              <div className="card pad stack">
                <strong>1. Chọn Flow đã khai báo (không nhập đích tùy ý)</strong>
                <select value={flowCode} onChange={(e) => { setFlowCode(e.target.value); setLocation('') }}>
                  <option value="">— chọn Flow —</option>
                  {list.map((f) => <option key={f.code} value={f.code}>{f.code} – {f.name}{f.startableNow ? '' : ' (không chạy được)'}</option>)}
                </select>
                {sel && (
                  <>
                    <div className="muted">
                      Loại yêu cầu: {sel.requestType} · Rack: {sel.rackKind} · Publication: {sel.publicationState}{sel.publishedVersion ? ` v${sel.publishedVersion}` : ''} · Readiness: {sel.readiness ?? '—'}
                      {sel.notStartableReason ? ` · KHÔNG CHẠY ĐƯỢC: ${sel.notStartableReason}` : ''}
                    </div>
                    <select value={location} onChange={(e) => setLocation(e.target.value)}>
                      <option value="">— vị trí phát yêu cầu —</option>
                      {sel.triggers.map((t) => (
                        <option key={t.locationCode} value={t.locationCode}>{t.locationCode} · {t.plcId ?? 'chưa gắn PLC'} · {t.portBusy ? 'ĐANG BẬN' : 'rảnh'}{t.plcOnline ? ' · PLC ONLINE (bị chặn)' : ''}</option>
                      ))}
                    </select>
                    <label><input type="checkbox" checked={ack} onChange={(e) => setAck(e.target.checked)} /> Tôi xác nhận khu vực thử nghiệm an toàn: <strong>AMR thật có thể di chuyển</strong> (PLC SIMULATION / REAL RCS).</label>
                    <div>
                      <Button variant="primary" disabled={busy || !ack || !location || !sel.startableNow}
                        onClick={() => run(async () => setLast(await api.simStart(sel.code, location, ack)), 'Đã gửi yêu cầu PLC mô phỏng tới WCS')}>2. Tạo yêu cầu PLC</Button>
                    </div>
                  </>
                )}
                {last && <div className="muted">Kết quả: {last.outcome} / {last.code} – {last.message} (RequestId {last.requestId})</div>}
              </div>
            )
          }}
        </Async>

        <Async state={orders} empty={{ title: 'Chưa có Transport Job' }} isEmpty={(o) => o.items.length === 0}>
          {(o) => (
            <div className="card pad stack">
              <strong>3. Theo dõi Job/Leg và xác nhận bàn giao (chỉ nhận khi RCS thật báo Executing)</strong>
              {o.items.map((order) => (
                <div key={order.id} className="stack">
                  <div><strong>{order.processCode} v{order.processVersion}</strong> · {order.state}{order.stateReason ? ` (${order.stateReason})` : ''} · {order.requestKey}</div>
                  {order.legs.map((l) => (
                    <div key={l.id} className="muted">
                      Leg {l.sequence}: {l.sourceLocationCode ?? '—'} → {l.destinationLocationCode ?? '—'} · {l.state}{l.stateReason ? ` (${l.stateReason})` : ''} · RCS task: {l.agvTaskCode ?? 'chưa có'} · AMR: {l.robotCode ?? '—'}
                      {' '}
                      {l.state !== 'Completed' && l.state !== 'Failed' && l.state !== 'Cancelled' && l.agvTaskCode && !l.pickConfirmedAt &&
                        <Button size="sm" disabled={busy} onClick={() => run(() => api.simConfirm(l.id, false), 'Đã xác nhận Pick')}>Xác nhận Pick</Button>}
                      {l.pickConfirmedAt && !l.dropConfirmedAt && l.state !== 'Failed' && l.state !== 'Cancelled' &&
                        <Button size="sm" disabled={busy} onClick={() => run(() => api.simConfirm(l.id, true), 'Đã xác nhận Drop')}>Xác nhận Drop</Button>}
                    </div>
                  ))}
                </div>
              ))}
            </div>
          )}
        </Async>
      </div>
    </>
  )
}
