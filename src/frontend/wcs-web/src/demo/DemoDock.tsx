import { useState, useSyncExternalStore } from 'react'
import { Button, Pill } from '../components/ui'
import { IconChevronDown, IconChevronUp, IconFlask, IconPause, IconPlay, IconReset, IconStep } from '../components/icons'
import { flowInfo, TASK_STATE_VI } from '../i18n/vi'
import type { TaskState } from '../api/types'
import { demo, DemoError } from './engine'

interface Scenario { label: string; hint: string; run: () => unknown }

const SCENARIOS: Scenario[] = [
  { label: 'BP-01 · Máy A1 có rack đầy', hint: 'AGV1 lấy rack đầy tại A1, thả ở B1/B2 → F1/F2 → F5 (thả ở B1/B2: không bind rack)', run: () => demo.runScenario('BP01', 'A1') },
  { label: 'BP-01 · Không có vị trí đích', hint: 'Lấp đầy B1, B2, F1, F2, F5 rồi chạy từ A2 → nhiệm vụ chuyển sang "Chờ vị trí đích"', run: () => demo.runScenario('BP01', 'A2', { blockDestinations: true }) },
  { label: 'BP-02 · Máy A2 cần rack rỗng', hint: 'AGV2 lấy rack rỗng từ B1/B2 hoặc F3/F4 mang tới A2', run: () => demo.runScenario('BP02', 'A2') },
  { label: 'BP-03 · Máy B1 có rack rỗng', hint: 'AGV2 lấy rack rỗng tại B1, thả ở A1/A2 (không bind rack) hoặc F3/F4', run: () => demo.runScenario('BP03', 'B1') },
  { label: 'BP-05 · F5 gọi rack đầy', hint: 'AGV1 lấy rack đầy từ F1/F2 mang tới F5', run: () => demo.runScenario('BP05', 'F5') },
  { label: 'BP-06 · F5 trả rack rỗng', hint: 'AGV2 lấy rack rỗng tại F5, thả ở A1/A2 (không bind rack) hoặc F3/F4', run: () => demo.runScenario('BP06', 'F5') },
]

const FINISHED: TaskState[] = ['Completed', 'Failed', 'Cancelled', 'Queued', 'Pending']

/**
 * Simulation dock: a bottom toolbar that is part of the page layout (it never floats over content).
 * Everything here acts on the in-browser demo engine only.
 */
export function DemoDock() {
  useSyncExternalStore(demo.subscribeStore, demo.getVersion)
  const [open, setOpen] = useState(false)
  const [message, setMessage] = useState('Chế độ mô phỏng: chọn tình huống hoặc bấm “Bắt đầu” để chạy tự động.')
  const [speed, setSpeed] = useState(demo.autoplaySpeedMs)

  const act = (fn: () => unknown, ok?: string) => {
    try {
      const r = fn()
      setMessage(ok ?? (typeof r === 'string' ? r : 'Đã thực hiện.'))
    } catch (e) {
      setMessage(e instanceof DemoError ? `Bị từ chối: ${e.message}` : 'Lỗi không mong đợi trong chế độ mô phỏng.')
    }
  }

  const agvs = demo.listAgvs()
  const active = demo.listTasks(undefined, 0, 100).items.filter((t) => !FINISHED.includes(t.state))
  const recon = demo.tasksNeedingReconciliation()
  const running = demo.autoplay

  return (
    <section className="dock" aria-label="Bảng điều khiển mô phỏng">
      <div className="dock-bar">
        <span className="dock-title"><IconFlask />Điều khiển mô phỏng</span>
        {running ? (
          <Button size="sm" onClick={() => demo.stopAutoplay()}><IconPause />Tạm dừng</Button>
        ) : (
          <Button size="sm" variant="primary" onClick={() => demo.startAutoplay(speed)}><IconPlay />Bắt đầu</Button>
        )}
        <Button size="sm" onClick={() => act(() => demo.stepAll(), 'Đã chạy 1 bước cho mọi nhiệm vụ đang hoạt động.')}><IconStep />Chạy từng bước</Button>
        <label className="dock-speed">
          <select value={speed} aria-label="Tốc độ tự chạy" onChange={(e) => { const v = Number(e.target.value); setSpeed(v); if (demo.autoplay) demo.startAutoplay(v) }}>
            <option value={600}>Tốc độ: nhanh</option><option value={1500}>Tốc độ: vừa</option><option value={3000}>Tốc độ: chậm</option>
          </select>
        </label>
        <Button size="sm" variant="danger" onClick={() => act(() => demo.reset(), 'Đã khởi tạo lại dữ liệu mô phỏng ban đầu.')}><IconReset />Khởi tạo lại</Button>
        <span className="dock-sep" />
        <span className="dock-msg" role="status">{message}</span>
        <Button size="sm" onClick={() => setOpen((o) => !o)} aria-expanded={open}>
          Tình huống và sự cố {open ? <IconChevronDown /> : <IconChevronUp />}
        </Button>
      </div>

      {open && (
        <div className="dock-body">
          <div className="dock-col">
            <h4>Tình huống vận hành</h4>
            <div className="dock-scenarios">
              {SCENARIOS.map((s) => (
                <Button key={s.label} title={s.hint} onClick={() => act(() => { s.run() }, `${s.label}: đã tạo nhiệm vụ. ${s.hint}`)}>{s.label}</Button>
              ))}
            </div>
            <h4>Tồn kho</h4>
            <div className="dock-row">
              <Button size="sm" onClick={() => act(() => demo.removeStock('F1', 'Full', 1), 'Đã lấy 1 rack đầy khỏi F1 (nếu không bị giữ chỗ). Nhiệm vụ đang chờ vị trí đích sẽ tiếp tục.')}>Giải phóng 1 vị trí ở F1</Button>
            </div>
          </div>

          <div className="dock-col">
            <h4>Sự cố trên nhiệm vụ đang hoạt động</h4>
            {active.length === 0 ? <p className="empty">Không có nhiệm vụ đang hoạt động.</p> : (
              <ul className="dock-list">
                {active.map((t) => (
                  <li key={t.id}>
                    <span>{flowInfo(t.flow).code} · {TASK_STATE_VI[t.state]}</span>
                    <span className="dock-row">
                      <Button size="sm" variant="danger" onClick={() => act(() => demo.interruptTask(t.id), `Nhiệm vụ ${t.flow}: RCS báo gián đoạn → Thất bại, cần đối soát.`)}>Gián đoạn</Button>
                      <Button size="sm" onClick={() => act(() => demo.cancelTask(t.id, 'hủy từ bảng mô phỏng'), 'Đã yêu cầu hủy → “Đang hủy”, chờ RCS xác nhận (bấm Chạy từng bước).')}>Hủy</Button>
                    </span>
                  </li>
                ))}
              </ul>
            )}
            {recon.length > 0 && (
              <>
                <h4>Cần đối soát vị trí rack</h4>
                <ul className="dock-list">
                  {recon.map((t) => (
                    <li key={t.id}>
                      <span>{flowInfo(t.flow).code} · <Pill tone="bad" dot={false}>{TASK_STATE_VI[t.state]}</Pill></span>
                      <Button size="sm" onClick={() => act(() => demo.reconcile(t.id, demo.locationCode(t.sourceLocationId)), `Đã xác nhận rack đang ở ${demo.locationCode(t.sourceLocationId)}; giải phóng giữ chỗ.`)}>
                        Rack đang ở {demo.locationCode(t.sourceLocationId)}
                      </Button>
                    </li>
                  ))}
                </ul>
              </>
            )}
          </div>

          <div className="dock-col">
            <h4>Kết nối AGV (RCS)</h4>
            <div className="dock-row">
              {agvs.map((a) => (
                <Button key={a.id} size="sm" onClick={() => act(() => demo.setAgvOnline(a.code, a.connectionStatus !== 'Online'), `${a.code} → ${a.connectionStatus === 'Online' ? 'mất kết nối' : 'trực tuyến'}`)}>
                  {a.code}: {a.connectionStatus === 'Online' ? 'ngắt kết nối' : 'kết nối lại'}
                </Button>
              ))}
            </div>
            <h4>Lưu ý</h4>
            <p className="empty">Toàn bộ dữ liệu chỉ nằm trong trình duyệt và mất khi tải lại trang. Không có lệnh nào được gửi tới Backend, PostgreSQL hoặc thiết bị. BP-04, A3/A4 và D1/D2 không được mô phỏng.</p>
          </div>
        </div>
      )}
    </section>
  )
}
