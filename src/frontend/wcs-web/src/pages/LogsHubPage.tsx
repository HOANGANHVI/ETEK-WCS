import { Navigate, NavLink, Outlet, useLocation } from 'react-router-dom'
import { useT } from '../i18n'
import { HUB_BASE, useAllowedTabs, type HubTab } from './logsHub'
import '../styles/hub.css'

/** The tab is part of the URL (/logs-diagnostics/<tab>): a link opens that tab directly, a refresh keeps it and Back / Forward move between tabs. */
export function LogsHubPage() {
  const t = useT()
  const tabs = useAllowedTabs()
  if (tabs.length === 0) return <Navigate to="/" replace state={{ denied: 'nav.denied' }} />
  return (
    <>
      <nav className="hub-tabs" aria-label={t('nav.logsDiagnostics')}>
        {tabs.map((x) => <NavLink key={x.path} to={`${HUB_BASE}/${x.path}`} className={({ isActive }) => (isActive ? 'hub-tab active' : 'hub-tab')}>{t(x.label)}</NavLink>)}
      </nav>
      <Outlet />
    </>
  )
}

/** /logs-diagnostics (no tab): the first tab the account may use. */
export function LogsHubIndex() {
  const first = useAllowedTabs()[0]
  return <Navigate to={first ? `${HUB_BASE}/${first.path}` : '/'} replace />
}

/** An old route (/audit, /logs, /diagnostics, /rcs-diagnostics) goes to its tab; the query string and hash are kept, so old bookmarks and links still work. */
export function LegacyHubRedirect({ tab }: { tab: HubTab['path'] }) {
  const { search, hash } = useLocation()
  return <Navigate to={{ pathname: `${HUB_BASE}/${tab}`, search, hash }} replace />
}
