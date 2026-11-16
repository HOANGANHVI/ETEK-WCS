import { useCallback } from 'react'
import { Link, NavLink, Outlet, useLocation } from 'react-router-dom'
import { api } from '../api/client'
import type { Agv, SystemHealth } from '../api/types'
import { hasRole, useAuth } from '../auth/AuthContext'
import { DEMO_MODE } from '../config'
import { DemoDock } from '../demo/DemoDock'
import { useApi } from '../hooks/useApi'
import { useClock } from '../hooks/useClock'
import { formatDate, formatTime } from '../i18n/format'
import { CONNECTION_VI, ROLE_SHORT_VI, type Tone } from '../i18n/vi'
import { useRealtime } from '../realtime/RealtimeProvider'
import { IconAgv, IconLog, IconOverview, IconPlc, IconSettings, IconTasks, IconUsers, IconWarehouse } from './icons'

const SITE_NAME = (import.meta.env.VITE_SITE_NAME as string | undefined)?.trim()

const NAV = [
  { to: '/', label: 'Tổng quan', end: true, icon: <IconOverview /> },
  { to: '/agvs', label: 'Giám sát AGV', icon: <IconAgv /> },
  { to: '/tasks', label: 'Quản lý nhiệm vụ', icon: <IconTasks /> },
  { to: '/inventory', label: 'Vị trí và tồn kho', icon: <IconWarehouse /> },
  { to: '/plc', label: 'Giám sát PLC', end: true, icon: <IconPlc /> },
]

// PLC configuration (CR-004). Admin only; the API enforces the same rule.
const NAV_PLC = [
  { to: '/plc/connections', label: 'Kết nối PLC', icon: <IconSettings /> },
  { to: '/plc/io-mapping', label: 'Cấu hình địa chỉ IO', icon: <IconPlc /> },
]

// Shown to Admin only. Hiding them is a convenience: the API and the routes enforce the same rule independently.
const NAV_SYSTEM = [
  { to: '/config', label: 'Cấu hình hệ thống', icon: <IconSettings /> },
  { to: '/users', label: 'Quản lý tài khoản', icon: <IconUsers /> },
  { to: '/audit', label: 'Nhật ký thao tác', icon: <IconLog /> },
  { to: '/logs', label: 'Logging & Diagnostics', icon: <IconLog /> },
]

interface LinkView { tone: Tone; text: string }

/** PLC link, from the API health. With no real controller attached the label is "Chưa kết nối" - never a healthy-looking state. */
function plcView(health: SystemHealth | null): LinkView {
  if (!health) return { tone: 'bad', text: 'Không có dữ liệu' }
  const rt = health.integration.plcRuntime
  if (rt) {
    if (rt.mode === 'Simulator') return { tone: 'warn', text: 'Chưa kết nối' }
    if (!rt.running) return { tone: 'warn', text: 'Chưa chạy' }
    if (rt.connected === 0) return { tone: 'bad', text: `0/${rt.total} PLC` }
    return { tone: rt.connected === rt.total ? 'ok' : 'warn', text: `${rt.connected}/${rt.total} PLC` }
  }
  if (health.integration.mode === 'Simulator') return { tone: 'warn', text: 'Chưa kết nối' }
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
  if (health.integration.mode === 'Simulator') return { tone: 'warn', text: 'Chưa kết nối' }
  const withRobot = (agvs ?? []).filter((a) => a.rcsRobotCode)
  if (withRobot.length === 0) return { tone: 'warn', text: 'Chưa cấu hình' }
  return withRobot.some((a) => a.connectionStatus === 'Online') ? { tone: 'ok', text: 'Đã kết nối' } : { tone: 'bad', text: 'Mất kết nối' }
}

function TopBar() {
  const { user, logout } = useAuth()
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
      {user && (
        <div className="whoami" title={`Đăng nhập: ${user.username}`}>
          <span className="who-name">{user.displayName}</span>
          <span className="who-role">{ROLE_SHORT_VI[user.role]}</span>
        </div>
      )}
      <Link className="top-link" to="/account/password">Đổi mật khẩu</Link>
      <button type="button" className="top-btn" onClick={() => void logout()}>Đăng xuất</button>
      <div className="clock"><strong>{formatTime(now.toISOString())}</strong><span>{formatDate(now)}</span></div>
    </header>
  )
}

function SideBar() {
  const { user } = useAuth()
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
        {hasRole(user, 'Admin') && <div className="nav-title">Cấu hình PLC</div>}
        {hasRole(user, 'Admin') && NAV_PLC.map((n) => (
          <NavLink key={n.to} to={n.to} className={({ isActive }) => (isActive ? 'nav-link active' : 'nav-link')} title={n.label}>
            {n.icon}<span>{n.label}</span>
          </NavLink>
        ))}
        {hasRole(user, 'Admin') && <div className="nav-title">Hệ thống</div>}
        {hasRole(user, 'Admin') && NAV_SYSTEM.map((n) => (
          <NavLink key={n.to} to={n.to} className={({ isActive }) => (isActive ? 'nav-link active' : 'nav-link')} title={n.label}>
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

/** RCS has only a simulator adapter (project baseline): say so on every screen, so nothing looks like a real AGV/RCS link. */
function IntegrationNotice() {
  const health = useApi(useCallback(() => api.health(), []), ['TaskStateChanged'])
  if (health.data?.integration.mode !== 'Simulator') return null
  return <div className="notice notice-warn" role="status">Hệ thống chưa kết nối RCS và PLC thật: nhiệm vụ không được gửi tới AGV thật và dữ liệu PLC không lấy từ thiết bị.</div>
}

export function Layout() {
  const { user } = useAuth()
  const location = useLocation()
  const denied = (location.state as { denied?: string } | null)?.denied
  return (
    <div className="app">
      <TopBar />
      <SideBar />
      <main className="main">
        {denied && <div className="notice notice-warn" role="alert">{denied}</div>}
        {!DEMO_MODE && <IntegrationNotice />}
        <Outlet />
      </main>
      {DEMO_MODE && hasRole(user, 'Admin') && <DemoDock />}
    </div>
  )
}
