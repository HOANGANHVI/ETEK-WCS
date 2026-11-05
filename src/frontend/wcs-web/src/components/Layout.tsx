import { NavLink, Outlet } from 'react-router-dom'
import { DEMO_MODE } from '../config'
import { DemoPanel } from '../demo/DemoPanel'
import { useRealtime } from '../realtime/RealtimeProvider'

const NAV = [
  { to: '/', label: 'Dashboard', end: true },
  { to: '/agvs', label: 'AGV Monitoring' },
  { to: '/tasks', label: 'Task Monitoring' },
  { to: '/inventory', label: 'Location / Inventory' },
]

export function Layout() {
  const { status } = useRealtime()
  return (
    <>
    {DEMO_MODE && (
      <div className="demo-banner" role="alert">
        DEMO – DỮ LIỆU GIẢ <span>Chạy hoàn toàn trong trình duyệt. Không kết nối Backend, PostgreSQL hoặc thiết bị; không ghi dữ liệu thật.</span>
      </div>
    )}
    <div className="shell">
      <aside className="sidebar">
        <div className="brand">ETEK <span>WCS</span></div>
        <nav aria-label="Main">
          {NAV.map((n) => (
            <NavLink key={n.to} to={n.to} end={n.end} className={({ isActive }) => (isActive ? 'nav-link active' : 'nav-link')}>
              {n.label}
            </NavLink>
          ))}
        </nav>
        <div className={`realtime realtime-${status}`} title="SignalR connection">
          <span className="dot" aria-hidden="true" /> {DEMO_MODE ? "Realtime: demo (cục bộ)" : `Realtime: ${status}`}
        </div>
      </aside>
      <main className="content">
        <Outlet />
      </main>
    </div>
    {DEMO_MODE && <DemoPanel />}
    </>
  )
}
