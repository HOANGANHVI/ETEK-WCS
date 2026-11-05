import { useState, useSyncExternalStore } from 'react'
import { Badge } from '../components/ui'
import { demo, DemoError } from './engine'

interface Scenario { label: string; hint: string; run: () => unknown }

const SCENARIOS: Scenario[] = [
  { label: 'BP-01 · A1 có rack đầy', hint: 'AGV1 lấy rack đầy ở A1 → B1/B2 → F1/F2 → F5 (thả ở B1/B2: không bind rack)', run: () => demo.runScenario('BP01', 'A1') },
  { label: 'BP-01 · không có vị trí đích', hint: 'Lấp đầy B1,B2,F1,F2,F5 rồi chạy A2 → WAITING_FOR_DESTINATION', run: () => demo.runScenario('BP01', 'A2', { blockDestinations: true }) },
  { label: 'BP-02 · A2 cần rack trống', hint: 'AGV2 lấy rack trống từ B1/B2 hoặc F3/F4 mang tới A2', run: () => demo.runScenario('BP02', 'A2') },
  { label: 'BP-03 · B1 có rack trống', hint: 'AGV2 lấy rack trống ở B1 → A1/A2 (không bind) hoặc F3/F4', run: () => demo.runScenario('BP03', 'B1') },
  { label: 'BP-05 · F5 gọi rack đầy', hint: 'AGV1 lấy rack đầy từ F1/F2 mang tới F5', run: () => demo.runScenario('BP05', 'F5') },
  { label: 'BP-06 · F5 trả rack trống', hint: 'AGV2 lấy rack trống ở F5 → A1/A2 (không bind) hoặc F3/F4', run: () => demo.runScenario('BP06', 'F5') },
]

export function DemoPanel() {
  useSyncExternalStore(demo.subscribeStore, demo.getVersion)
  const [open, setOpen] = useState(true)
  const [message, setMessage] = useState<string>('Chọn một tình huống để tạo task mô phỏng.')
  const [speed, setSpeed] = useState(demo.autoplaySpeedMs)

  const act = (fn: () => unknown, ok?: string) => {
    try {
      const r = fn()
      setMessage(ok ?? (typeof r === 'string' ? r : 'Đã thực hiện.'))
    } catch (e) {
      setMessage(e instanceof DemoError ? `Bị từ chối: ${e.message}` : 'Lỗi không mong đợi trong demo.')
    }
  }

  const agvs = demo.listAgvs()
  const active = demo.listTasks(undefined, 0, 100).items.filter((t) => !['Completed', 'Failed', 'Cancelled', 'Queued', 'Pending'].includes(t.state))
  const recon = demo.tasksNeedingReconciliation()

  if (!open) {
    return <button type="button" className="demo-fab" onClick={() => setOpen(true)}>🎮 Bảng điều khiển DEMO</button>
  }

  return (
    <aside className="demo-panel" aria-label="Demo controls">
      <header>
        <strong>Bảng điều khiển DEMO</strong>
        <button type="button" className="secondary" onClick={() => setOpen(false)} aria-label="Thu gọn">–</button>
      </header>
      <p className="demo-msg" role="status">{message}</p>

      <h4>Tình huống</h4>
      <div className="demo-list">
        {SCENARIOS.map((s) => (
          <button key={s.label} type="button" title={s.hint} onClick={() => act(() => { s.run() }, `${s.label}: đã tạo task. ${s.hint}`)}>{s.label}</button>
        ))}
      </div>
      <button type="button" className="secondary" onClick={() => act(() => demo.removeStock('F1', 'Full', 1), 'Đã lấy 1 rack đầy khỏi F1 (nếu không bị giữ chỗ) → task đang chờ đích sẽ tiếp tục.')}>
        Giải phóng 1 vị trí ở F1
      </button>

      <h4>Chạy mô phỏng</h4>
      <div className="demo-row">
        <button type="button" onClick={() => act(() => demo.stepAll(), 'Đã chạy 1 bước cho mọi task đang hoạt động.')}>Bước tiếp (tất cả)</button>
        <button type="button" className="secondary" onClick={() => (demo.autoplay ? demo.stopAutoplay() : demo.startAutoplay(speed))}>
          {demo.autoplay ? '⏸ Dừng tự chạy' : '▶ Tự chạy'}
        </button>
        <select value={speed} aria-label="Tốc độ" onChange={(e) => { const v = Number(e.target.value); setSpeed(v); if (demo.autoplay) demo.startAutoplay(v) }}>
          <option value={600}>Nhanh</option><option value={1500}>Vừa</option><option value={3000}>Chậm</option>
        </select>
      </div>

      <h4>Sự cố</h4>
      {active.length === 0 ? <p className="muted">Không có task đang chạy.</p> : (
        <ul className="demo-tasks">
          {active.map((t) => (
            <li key={t.id}>
              <span>{t.flow} · <Badge value={t.state} /></span>
              <span>
                <button type="button" className="secondary" onClick={() => act(() => demo.interruptTask(t.id), `Task ${t.flow} bị RCS báo gián đoạn → FAILED, cần đối soát.`)}>Gián đoạn</button>{' '}
                <button type="button" className="secondary" onClick={() => act(() => demo.cancelTask(t.id, 'huỷ từ demo'), 'Đã yêu cầu hủy → CANCEL_PENDING, chờ RCS xác nhận (bấm Bước tiếp).')}>Hủy</button>
              </span>
            </li>
          ))}
        </ul>
      )}
      {recon.length > 0 && (
        <>
          <h4>Cần đối soát</h4>
          <ul className="demo-tasks">
            {recon.map((t) => (
              <li key={t.id}>
                <span>{t.flow} · {t.state}</span>
                <button type="button" onClick={() => act(() => demo.reconcile(t.id, demo.locationCode(t.sourceLocationId)), `Đã xác nhận rack ở ${demo.locationCode(t.sourceLocationId)}; giải phóng giữ chỗ.`)}>
                  Rack ở {demo.locationCode(t.sourceLocationId)}
                </button>
              </li>
            ))}
          </ul>
        </>
      )}

      <h4>AGV</h4>
      <div className="demo-row">
        {agvs.map((a) => (
          <button key={a.id} type="button" className="secondary" onClick={() => act(() => demo.setAgvOnline(a.code, a.connectionStatus !== 'Online'), `${a.code} → ${a.connectionStatus === 'Online' ? 'Offline' : 'Online'}`)}>
            {a.code}: {a.connectionStatus === 'Online' ? 'ngắt kết nối' : 'kết nối lại'}
          </button>
        ))}
      </div>

      <h4>Dữ liệu</h4>
      <button type="button" className="secondary" onClick={() => act(() => demo.reset(), 'Đã khôi phục dữ liệu demo ban đầu.')}>Khôi phục dữ liệu demo</button>
    </aside>
  )
}
