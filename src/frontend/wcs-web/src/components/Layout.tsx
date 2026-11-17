import { useCallback, type ReactNode } from 'react'
import { Link, NavLink, Outlet, useLocation } from 'react-router-dom'
import { api } from '../api/client'
import type { Agv, SystemHealth } from '../api/types'
import { hasRole, useAuth } from '../auth/AuthContext'
import { DEMO_MODE } from '../config'
import { DemoDock } from '../demo/DemoDock'
import { useApi } from '../hooks/useApi'
import { useClock } from '../hooks/useClock'
import { displayNameOf, formatDate, formatTime } from '../i18n/format'
import { useT, type MsgKey } from '../i18n'
import { ROLE_SHORT_VI, type Tone } from '../i18n/vi'
import { LangSwitch } from './LangSwitch'
import { useRealtime } from '../realtime/RealtimeProvider'
import { IconAgv, IconLog, IconOverview, IconPlc, IconSettings, IconTasks, IconUsers, IconWarehouse } from './icons'

const SITE_NAME = (import.meta.env.VITE_SITE_NAME as string | undefined)?.trim()

const NAV: NavItem[] = [
  { to: '/', label: 'nav.dashboard', end: true, icon: <IconOverview /> },
  { to: '/agvs', label: 'nav.agv', icon: <IconAgv /> },
  { to: '/tasks', label: 'nav.tasks', icon: <IconTasks /> },
  { to: '/inventory', label: 'nav.inventory', icon: <IconWarehouse /> },
  { to: '/plc', label: 'nav.plcMonitor', end: true, icon: <IconPlc /> },
]

// PLC configuration (CR-004). Admin only; the API enforces the same rule.
const NAV_PLC: NavItem[] = [
  { to: '/plc/connections', label: 'nav.plcConnections', icon: <IconSettings /> },
  { to: '/plc/io-mapping', label: 'nav.plcIoMapping', icon: <IconPlc /> },
]

// Shown to Admin only. Hiding them is a convenience: the API and the routes enforce the same rule independently.
const NAV_SYSTEM: NavItem[] = [
  { to: '/config', label: 'nav.config', icon: <IconSettings /> },
  { to: '/users', label: 'nav.users', icon: <IconUsers /> },
  { to: '/audit', label: 'nav.audit', icon: <IconLog /> },
  { to: '/logs', label: 'nav.logs', icon: <IconLog /> },
]

interface NavItem { to: string; label: MsgKey; end?: boolean; icon: ReactNode }
interface LinkView { tone: Tone; text: MsgKey | { raw: string } }

/** PLC link, from the API health. With no real controller attached the label is "Chưa kết nối" - never a healthy-looking state. */
function plcView(health: SystemHealth | null): LinkView {
  if (!health) return { tone: 'bad', text: 'nav.noData' }
  const rt = health.integration.plcRuntime
  if (rt) {
    if (rt.mode === 'Simulator') return { tone: 'warn', text: 'nav.notConnected' }
    if (!rt.running) return { tone: 'warn', text: 'nav.notRunning' }
    if (rt.connected === 0) return { tone: 'bad', text: { raw: `0/${rt.total} PLC` } }
    return { tone: rt.connected === rt.total ? 'ok' : 'warn', text: { raw: `${rt.connected}/${rt.total} PLC` } }
  }
  if (health.integration.mode === 'Simulator') return { tone: 'warn', text: 'nav.notConnected' }
  switch (health.integration.plc) {
    case 'Connected': return { tone: 'ok', text: 'nav.connected' }
    case 'Connecting': return { tone: 'warn', text: 'nav.connecting' }
    case 'Faulted': return { tone: 'bad', text: 'nav.fault' }
    default: return { tone: 'bad', text: 'nav.disconnected' }
  }
}

/** RCS link: there is no dedicated RCS health yet, so it is derived from AGV statuses reported through RCS. */
function rcsView(health: SystemHealth | null, agvs: Agv[] | null): LinkView {
  if (!health) return { tone: 'bad', text: 'nav.noData' }
  if (health.integration.mode === 'Simulator') return { tone: 'warn', text: 'nav.notConnected' }
  const withRobot = (agvs ?? []).filter((a) => a.rcsRobotCode)
  if (withRobot.length === 0) return { tone: 'warn', text: 'nav.notSet' }
  return withRobot.some((a) => a.connectionStatus === 'Online') ? { tone: 'ok', text: 'nav.connected' } : { tone: 'bad', text: 'nav.disconnected' }
}

function TopBar() {
  const t = useT()
  const { user, logout } = useAuth()
  const now = useClock()
  const linkText = (m: MsgKey | { raw: string }) => (typeof m === 'string' ? t(m) : m.raw)
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
        <div className="site"><small>{t('nav.area')}</small><span>{SITE_NAME}</span></div>
      )}
      <div className="topbar-spacer" />
      {DEMO_MODE && <span className="sim-chip" title={t('nav.simChipTitle')}><i />{t('nav.simChip')}</span>}
      <div className="conn" title={t('nav.plcLink')}><span className="label">PLC</span><span className={`led ${plc.tone}`} /><strong>{linkText(plc.text)}</strong></div>
      <div className="conn" title={t('nav.rcsLink')}><span className="label">RCS</span><span className={`led ${rcs.tone}`} /><strong>{linkText(rcs.text)}</strong></div>
      {user && (
        <div className="whoami" title={t('nav.loggedInAs', { user: user.username })}>
          <span className="who-name">{displayNameOf(user.displayName)}</span>
          <span className="who-role">{ROLE_SHORT_VI[user.role]}</span>
        </div>
      )}
      <Link className="top-link" to="/account/password">{t('nav.changePassword')}</Link>
      <LangSwitch />
      <button type="button" className="top-btn" onClick={() => void logout()}>{t('nav.logout')}</button>
      <div className="clock"><strong>{formatTime(now.toISOString())}</strong><span>{formatDate(now)}</span></div>
    </header>
  )
}

const RT_KEY: Record<string, MsgKey> = { connecting: 'nav.rtConnecting', connected: 'nav.rtConnected', reconnecting: 'nav.rtReconnecting', disconnected: 'nav.rtDisconnected', demo: 'nav.rtDemo' }

function SideBar() {
  const t = useT()
  const { user } = useAuth()
  const { status } = useRealtime()
  const tone = status === 'connected' || status === 'demo' ? 'ok' : status === 'disconnected' ? 'bad' : 'warn'
  return (
    <aside className="sidebar">
      <nav aria-label={t('nav.mainNav')}>
        <div className="nav-title">{t('nav.groupOps')}</div>
        {NAV.map((n) => (
          <NavLink key={n.to} to={n.to} end={n.end} className={({ isActive }) => (isActive ? 'nav-link active' : 'nav-link')} title={t(n.label)}>
            {n.icon}<span>{t(n.label)}</span>
          </NavLink>
        ))}
        {hasRole(user, 'Admin') && <div className="nav-title">{t('nav.groupPlc')}</div>}
        {hasRole(user, 'Admin') && NAV_PLC.map((n) => (
          <NavLink key={n.to} to={n.to} className={({ isActive }) => (isActive ? 'nav-link active' : 'nav-link')} title={t(n.label)}>
            {n.icon}<span>{t(n.label)}</span>
          </NavLink>
        ))}
        {hasRole(user, 'Admin') && <div className="nav-title">{t('nav.groupSystem')}</div>}
        {hasRole(user, 'Admin') && NAV_SYSTEM.map((n) => (
          <NavLink key={n.to} to={n.to} className={({ isActive }) => (isActive ? 'nav-link active' : 'nav-link')} title={t(n.label)}>
            {n.icon}<span>{t(n.label)}</span>
          </NavLink>
        ))}
      </nav>
      <div className="side-foot">
        <div className="row"><i className={`led ${tone}`} /><span>{t('nav.realtime', { status: t(RT_KEY[status]) })}</span></div>
        <span>ETEK WCS</span>
      </div>
    </aside>
  )
}

export function Layout() {
  const t = useT()
  const { user } = useAuth()
  const location = useLocation()
  const deniedKey = (location.state as { denied?: MsgKey } | null)?.denied
  const denied = deniedKey ? t(deniedKey) : null
  return (
    <div className="app">
      <TopBar />
      <SideBar />
      <main className="main">
        {denied && <div className="notice notice-warn" role="alert">{denied}</div>}
        <Outlet />
      </main>
      {DEMO_MODE && hasRole(user, 'Admin') && <DemoDock />}
    </div>
  )
}
