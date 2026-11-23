import { useCallback, useEffect, useState, type ReactNode, type SyntheticEvent } from 'react'
import { NavLink, Outlet, useLocation } from 'react-router-dom'
import { api } from '../api/client'
import { hasRole, useAuth } from '../auth/AuthContext'
import { DEMO_MODE } from '../config'
import { DemoDock } from '../demo/DemoDock'
import { useApi } from '../hooks/useApi'
import { useClock } from '../hooks/useClock'
import { formatDate, formatTime } from '../i18n/format'
import { useT, type MsgKey } from '../i18n'
import { LangSwitch } from './LangSwitch'
import { UserMenu } from './UserMenu'
import { VersionBadge } from './VersionBadge'
import { plcView, rcsView, type LinkView } from './linkStatus'
import { useRealtime } from '../realtime/RealtimeProvider'
import { usePreferences } from '../preferences/PreferencesContext'
import { IconAgv, IconPanelLeftClose, IconPanelLeftOpen, IconUser, IconLog, IconOverview, IconPlc, IconSettings, IconTasks, IconUsers, IconWarehouse } from './icons'

const SITE_NAME = (import.meta.env.VITE_SITE_NAME as string | undefined)?.trim()

const NAV: NavItem[] = [
  { to: '/', label: 'nav.dashboard', end: true, icon: <IconOverview /> },
  { to: '/agvs', label: 'nav.agv', icon: <IconAgv /> },
  { to: '/tasks', label: 'nav.tasks', icon: <IconTasks /> },
  { to: '/inventory', label: 'nav.inventory', icon: <IconWarehouse /> },
  ...(DEMO_MODE ? [] : [{ to: '/routing', label: 'routing.navRouting' as MsgKey, icon: <IconTasks /> }]),   // CR-010 (not part of the browser-only simulation)
  { to: '/plc', label: 'nav.plcMonitor', end: true, icon: <IconPlc /> },
]

// PLC configuration (CR-004). Admin only; the API enforces the same rule.
const NAV_PLC: NavItem[] = [
  ...(DEMO_MODE ? [] : [{ to: '/processes', label: 'routing.navProcesses' as MsgKey, icon: <IconSettings /> }]),   // CR-010 route configuration
  { to: '/plc/connections', label: 'nav.plcConnections', icon: <IconSettings /> },
  { to: '/plc/io-mapping', label: 'nav.plcIoMapping', icon: <IconPlc /> },
]

// Shown to Admin only. Hiding them is a convenience: the API and the routes enforce the same rule independently.
// Personal group (every role): the signed-in user's own page, kept apart from the WCS operation / configuration groups.
const NAV_PERSONAL: NavItem[] = [
  { to: '/account', label: 'nav.userConfig', icon: <IconUser /> },
]

const NAV_SYSTEM: NavItem[] = [
  { to: '/config', label: 'nav.config', icon: <IconSettings /> },
  { to: '/config-transfer', label: 'nav.configTransfer', icon: <IconSettings /> },   // CR-026
  { to: '/users', label: 'nav.users', icon: <IconUsers /> },
  { to: '/audit', label: 'nav.audit', icon: <IconLog /> },
  { to: '/logs', label: 'nav.logs', icon: <IconLog /> },
  ...(DEMO_MODE ? [] : [{ to: '/rcs-diagnostics', label: 'nav.rcsDiagnostics' as MsgKey, icon: <IconPlc /> }]),   // CR-032 RCS diagnostics (Admin)
  ...(DEMO_MODE ? [] : [{ to: '/diagnostics', label: 'nav.diagnostics' as MsgKey, icon: <IconPlc /> }]),   // PLC diagnostic export (Admin)
]

interface NavItem { to: string; label: MsgKey; end?: boolean; icon: ReactNode }
function TopBar() {
  const t = useT()
  const now = useClock()
  const linkText = (m: LinkView['text']) => (typeof m === 'string' ? t(m) : m.raw)
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
      <LangSwitch />
      <UserMenu />
      <div className="clock"><strong>{formatTime(now.toISOString())}</strong><span>{formatDate(now)}</span></div>
    </header>
  )
}

const RT_KEY: Record<string, MsgKey> = { connecting: 'nav.rtConnecting', connected: 'nav.rtConnected', reconnecting: 'nav.rtReconnecting', disconnected: 'nav.rtDisconnected', demo: 'nav.rtDemo' }

interface Tip { label: string; top: number; left: number }

/** CR-023: on a phone the menu is always the icon-only rail (expanded it would take half of the screen and leave the page about 200 px). The stored preference is untouched. */
function useNarrowScreen(maxWidth = 640): boolean {
  const query = `(max-width: ${maxWidth}px)`
  const [narrow, setNarrow] = useState(() => typeof window !== 'undefined' && window.matchMedia(query).matches)
  useEffect(() => {
    const m = window.matchMedia(query)
    const on = () => setNarrow(m.matches)
    m.addEventListener('change', on)
    return () => m.removeEventListener('change', on)
  }, [query])
  return narrow
}

function SideBar() {
  const t = useT()
  const { user } = useAuth()
  const { status } = useRealtime()
  const { sidebarCollapsed: preferCollapsed, update } = usePreferences()
  const narrow = useNarrowScreen()
  const collapsed = preferCollapsed || narrow
  const [tip, setTip] = useState<Tip | null>(null)
  const tone = status === 'connected' || status === 'demo' ? 'ok' : status === 'disconnected' ? 'bad' : 'warn'

  // Collapsed menu items show a tooltip placed with fixed coordinates, so the scrolling sidebar can never clip it.
  const showTip = (label: string) => (e: SyntheticEvent<HTMLElement>) => {
    if (!collapsed) return
    const r = e.currentTarget.getBoundingClientRect()
    setTip({ label, top: r.top + r.height / 2, left: r.right + 10 })
  }
  const hideTip = () => setTip(null)
  const toggleLabel = t(collapsed ? 'account.expandSidebar' : 'account.collapseSidebar')

  const item = (n: NavItem) => (
    <NavLink key={n.to} to={n.to} end={n.end} className={({ isActive }) => (isActive ? 'nav-link active' : 'nav-link')}
      aria-label={t(n.label)} title={collapsed ? undefined : t(n.label)}
      onMouseEnter={showTip(t(n.label))} onMouseLeave={hideTip} onFocus={showTip(t(n.label))} onBlur={hideTip}>
      {n.icon}<span>{t(n.label)}</span>
    </NavLink>
  )
  return (
    <aside className="sidebar">
      <button type="button" className="side-toggle" aria-label={toggleLabel} aria-expanded={!collapsed}
        title={collapsed ? undefined : toggleLabel}
        onMouseEnter={showTip(toggleLabel)} onMouseLeave={hideTip} onFocus={showTip(toggleLabel)} onBlur={hideTip}
        onClick={() => { hideTip(); void update({ sidebarCollapsed: !collapsed }) }}>
        {collapsed ? <IconPanelLeftOpen /> : <IconPanelLeftClose />}
      </button>
      <nav aria-label={t('nav.mainNav')}>
        <div className="nav-title">{t('nav.groupOps')}</div>
        {NAV.map(item)}
        {hasRole(user, 'Admin') && <div className="nav-title">{t('nav.groupPlc')}</div>}
        {hasRole(user, 'Admin') && NAV_PLC.map(item)}
        {hasRole(user, 'Admin') && <div className="nav-title">{t('nav.groupSystem')}</div>}
        {hasRole(user, 'Admin') && NAV_SYSTEM.map(item)}
        <div className="nav-title">{t('nav.groupPersonal')}</div>
        {NAV_PERSONAL.map(item)}
      </nav>
      <div className="side-foot" title={collapsed ? t('nav.realtime', { status: t(RT_KEY[status]) }) : undefined}>
        <div className="row"><i className={`led ${tone}`} /><span>{t('nav.realtime', { status: t(RT_KEY[status]) })}</span></div>
        <span>ETEK WCS</span>
        <VersionBadge />
      </div>
      {tip && <div className="side-tip" role="tooltip" style={{ top: tip.top, left: tip.left }}>{tip.label}</div>}
    </aside>
  )
}

/** CR-026: shown to everyone while the system is in Maintenance, so a refused "create task" has an obvious reason. Read-only; only an Admin switches the flag (on the transfer page). */
function MaintenanceBanner() {
  const t = useT()
  const { user } = useAuth()
  const m = useApi(useCallback(() => api.maintenance(), []), ['MaintenanceChanged'])
  if (m.data?.enabled !== true) return null
  return (
    <div className="notice notice-warn maint-banner" role="status">
      <strong>{t('transfer.banner')}</strong>
      {m.data.reason && <span> {t('transfer.bannerReason', { reason: m.data.reason })}</span>}
      {hasRole(user, 'Admin') && <> <NavLink to="/config-transfer">{t('transfer.bannerLink')}</NavLink></>}
    </div>
  )
}

export function Layout() {
  const t = useT()
  const { user } = useAuth()
  const location = useLocation()
  const { sidebarCollapsed: preferCollapsed } = usePreferences()
  const narrow = useNarrowScreen()
  const sidebarIsCollapsed = preferCollapsed || narrow
  const deniedKey = (location.state as { denied?: MsgKey } | null)?.denied
  const denied = deniedKey ? t(deniedKey) : null
  return (
    <div className="app" data-sidebar={sidebarIsCollapsed ? 'collapsed' : 'expanded'}>
      <TopBar />
      <SideBar />
      <main className="main">
        <MaintenanceBanner />
        {denied && <div className="notice notice-warn" role="alert">{denied}</div>}
        <Outlet />
      </main>
      {DEMO_MODE && hasRole(user, 'Admin') && <DemoDock />}
    </div>
  )
}
