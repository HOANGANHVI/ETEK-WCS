import { NavLink, Outlet } from 'react-router-dom'
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
          <span className="dot" aria-hidden="true" /> Realtime: {status}
        </div>
      </aside>
      <main className="content">
        <Outlet />
      </main>
    </div>
  )
}
