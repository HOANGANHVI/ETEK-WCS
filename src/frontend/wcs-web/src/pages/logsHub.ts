import type { Role } from '../api/authTypes'
import { hasRole, useAuth } from '../auth/AuthContext'
import { DEMO_MODE } from '../config'
import type { MsgKey } from '../i18n'

/**
 * Logs & Diagnostics: ONE sidebar entry with four tabs. Each tab is the existing page, unchanged (same route component, same API, same permission).
 * The roles here only decide which tabs are shown; every route and every API still enforces its role on its own.
 */
export interface HubTab { path: 'audit' | 'system' | 'plc' | 'rcs'; label: MsgKey; roles: Role[]; hideInDemo?: boolean }

export const HUB_BASE = '/logs-diagnostics'
export const HUB_TABS: HubTab[] = [
  { path: 'audit', label: 'nav.audit', roles: ['Admin'] },
  { path: 'system', label: 'nav.logs', roles: ['Admin'] },
  { path: 'plc', label: 'nav.diagnostics', roles: ['Admin'], hideInDemo: true },
  { path: 'rcs', label: 'nav.rcsDiagnostics', roles: ['Admin'], hideInDemo: true },
]

/** The tabs this account may use (in the demo build the two diagnostics tabs do not exist, as their old menu entries did not). */
export function useAllowedTabs(): HubTab[] {
  const { user } = useAuth()
  return HUB_TABS.filter((x) => hasRole(user, ...x.roles) && !(DEMO_MODE && x.hideInDemo))
}
