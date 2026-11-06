import { useCallback } from 'react'
import { NavLink, Outlet } from 'react-router-dom'
import { api } from '../api/client'
import type { Agv, SystemHealth } from '../api/types'
import { DEMO_MODE } from '../config'
import { DemoDock } from '../demo/DemoDock'
import { useApi } from '../hooks/useApi'
import { useClock } from '../hooks/useClock'
import { formatDate, formatTime } from '../i18n/format'
import { CONNECTION_VI, type Tone } from '../i18n/vi'
import { useRealtime } from '../realtime/RealtimeProvider'
import { IconAgv, IconOverview, IconTasks, IconWarehouse } from './icons'

const SITE_NAME = (import.meta.env.VITE_SITE_NAME as string | undefined)?.trim()

const NAV = [
  { to: '/', label: 'Tổng quan', end: true, icon: <IconOverview /> },
  { to: '/agvs', label: 'Giám sát AGV', icon: <IconAgv /> },
  { to: '/tasks', label: 'Quản lý nhiệm vụ', icon: <IconTasks /> },
  { to: '/inventory', label: 'Vị trí và tồn kho', icon: <IconWarehouse /> },
]

interface LinkView { tone: Tone; text: string }

/** PLC link, from the API health. In simulator mode we say so instead of pretending to see a real controller. */
function plcView(health: SystemHealth | null): LinkView {
  if (!health) return { tone: 'bad', text: 'Không có dữ liệu' }
  if (health.integration.mode === 'Simulator') return { tone: 'info', text: 'Mô phỏng' }
  switch (health.integration.plc) {
    case 'Connected': return { tone: 'ok', text: 'Đã kết nối' }
    case 'Connecting': return { tone: 'warn', text: 'Đang kết nối' }
    case 'Faulted': return { tone: 'bad', text: 'Lỗi' }
    default: return { tone: 'bad', text: 'Mất kết nối' }
  }
}

/** RCS link: there is no dedicated RCS health yet, so it is derived from AGV statuses reported through RCS. */
function rcsView(health: SystemHealth | null, agvs: Agv[] | null): LinkView {
  if (!health) return { tone: 'bad', text: 'Không có dữ liệu' }
  if (health.integration.mode === 'Simulator') return { tone: 'info', text: 'Mô phỏng' }
  const withRobot = (agvs ?? []).filter((a) => a.rcsRobotCode)
  if (withRobot.length === 0) return { tone: 'warn', text: 'Chưa cấu hình' }
  return withRobot.some((a) => a.connectionStatus === 'Online') ? { tone: 'ok', text: 'Đã kết nối' } : { tone: 'bad', text: 'Mất kết nối' }
}

function TopBar() {
  const now = useClock()
  const health = useApi(useCallback(() => api.health(), []), ['TaskStateChanged'])
  const agvs = useApi(useCallback(() => api.agvs(), []), ['AgvStatusChanged'])
  const plc = plcView(health.error && !health.data ? null : health.data)
  const rcs = rcsView(health.error && !health.data ? null : health.data, agvs.data)

  return (
    <header className="topbar">
      <div className="brand">
        <span className="brand-mark" aria-hidden="true">E</span>
        <span className="brand-text">ETEK <b>WCS</b></span>
      </div>
      {SITE_NAME && (
        <div className="site"><small>Khu vực</small><span>{SITE_NAME}</span></div>
      )}
      <div className="topbar-spacer" />
      {DEMO_MODE && <span className="sim-chip" title="Dữ liệu hiển thị là dữ liệu giả, chạy trong trình duyệt"><i />Chế độ mô phỏng</span>}
      <div className="conn" title="Kết nối tới PLC"><span className="label">PLC</span><span className={`led ${plc.tone}`} /><strong>{plc.text}</strong></div>
      <div className="conn" title="Kết nối tới RCS"><span className="label">RCS</span><span className={`led ${rcs.tone}`} /><strong>{rcs.text}</strong></div>
      <div className="clock"><strong>{formatTime(now.toISOString())}</strong><span>{formatDate(now)}</span></div>
    </header>
  )
}

function SideBar() {
  const { status } = useRealtime()
  const tone = status === 'connected' || status === 'demo' ? 'ok' : status === 'disconnected' ? 'bad' : 'warn'
  return (
    <aside className="sidebar">
      <nav aria-label="Điều hướng chính">
        <div className="nav-title">Vận hành</div>
        {NAV.map((n) => (
          <NavLink key={n.to} to={n.to} end={n.end} className={({ isActive }) => (isActive ? 'nav-link active' : 'nav-link')} title={n.label}>
            {n.icon}<span>{n.label}</span>
          </NavLink>
        ))}
      </nav>
      <div className="side-foot">
        <div className="row"><i className={`led ${tone}`} /><span>Thời gian thực: {CONNECTION_VI[status]}</span></div>
        <span>ETEK WCS · Giai đoạn 1</span>
      </div>
    </aside>
  )
}

export function Layout() {
  return (
    <div className="app">
      <TopBar />
      <SideBar />
      <main className="main"><Outlet /></main>
      {DEMO_MODE && <DemoDock />}
    </div>
  )
}
