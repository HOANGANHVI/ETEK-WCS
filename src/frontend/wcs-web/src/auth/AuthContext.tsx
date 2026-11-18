import type { MsgKey } from '../i18n'
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { api, UNAUTHORIZED_EVENT } from '../api/client'
import type { Me, Role } from '../api/authTypes'

export type AuthStatus = 'loading' | 'anonymous' | 'authenticated'

interface AuthValue {
  status: AuthStatus
  user: Me | null
  /** Message shown on the sign-in screen (e.g. the session expired). */
  notice: MsgKey | null
  login: (username: string, password: string) => Promise<void>
  logout: () => Promise<void>
  refresh: () => Promise<void>
  /** Called after a successful password change. */
  setUser: (u: Me) => void
}

const AuthContext = createContext<AuthValue | null>(null)

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth(): AuthValue {
  const v = useContext(AuthContext)
  if (!v) throw new Error('useAuth must be used inside <AuthProvider>')
  return v
}

/** UI convenience only. The backend enforces every permission again on each request. */
// eslint-disable-next-line react-refresh/only-export-components
export const hasRole = (user: Me | null, ...roles: Role[]) => !!user && roles.includes(user.role)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<AuthStatus>('loading')
  const [user, setUserState] = useState<Me | null>(null)
  const [notice, setNotice] = useState<MsgKey | null>(null)

  const apply = useCallback((u: Me | null, message: MsgKey | null = null) => {
    setUserState(u)
    setStatus(u ? 'authenticated' : 'anonymous')
    setNotice(u ? null : message)
  }, [])

  const refresh = useCallback(async () => {
    try { apply(await api.me()) } catch { apply(null) }
  }, [apply])

  useEffect(() => {
    let cancelled = false
    api.me().then((u) => { if (!cancelled) apply(u) }).catch(() => { if (!cancelled) apply(null) })
    return () => { cancelled = true }
  }, [apply])

  // Any request answered with 401 means the session is gone (expired, signed out elsewhere, account disabled).
  useEffect(() => {
    const onUnauthorized = (e: Event) => {
      if ((e as CustomEvent).detail === 'password') { void refresh(); return }
      setUserState((prev) => {
        if (prev) { setStatus('anonymous'); setNotice('auth.sessionExpired') }
        return null
      })
    }
    window.addEventListener(UNAUTHORIZED_EVENT, onUnauthorized)
    return () => window.removeEventListener(UNAUTHORIZED_EVENT, onUnauthorized)
  }, [refresh])

  // Coming back to the tab after a long absence: find out right away whether the session is still valid.
  useEffect(() => {
    const onVisible = () => { if (document.visibilityState === 'visible' && status === 'authenticated') void refresh() }
    document.addEventListener('visibilitychange', onVisible)
    return () => document.removeEventListener('visibilitychange', onVisible)
  }, [refresh, status])

  const value = useMemo<AuthValue>(() => ({
    status, user, notice,
    login: async (username, password) => { apply(await api.login(username, password)) },
    logout: async () => {
      try { await api.logout() } catch { /* already signed out */ }
      apply(null)
    },
    refresh,
    setUser: (u) => apply(u),
  }), [status, user, notice, apply, refresh])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
