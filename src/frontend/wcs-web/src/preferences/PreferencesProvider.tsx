import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { api } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { PreferencesContext, type PreferencesValue } from './PreferencesContext'
import { DEFAULT_PREFERENCES, readCache, sanitize, writeCache, type Preferences } from './prefsStore'

/**
 * Loads the signed-in account's preferences, applies the theme to the document and exposes the sidebar state. A change is applied immediately and then stored
 * on the server; if storing fails the previous (server-confirmed) values come back, so the screen never silently differs from what is saved.
 */
export function PreferencesProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth()
  const username = user?.username ?? null
  const [prefs, setPrefs] = useState<Preferences>(DEFAULT_PREFERENCES)
  const [shownFor, setShownFor] = useState<string | null>(null)       // account the values on screen belong to
  const confirmed = useRef<Preferences>(DEFAULT_PREFERENCES)      // last values known to be stored on the server
  const owner = useRef<string | null>(null)                        // current account (guards late answers after a sign-out / account switch)
  const [saving, setSaving] = useState(false)
  const [saveState, setSaveState] = useState<'saved' | 'failed' | null>(null)

  // Another account (or none) is now signed in: start from that account's own cache, never from the previous account's values.
  if (shownFor !== username) {
    const start = (username && readCache(username)) || DEFAULT_PREFERENCES
    setShownFor(username); setPrefs(start); setSaveState(null)
  }

  useEffect(() => {
    owner.current = username
    confirmed.current = (username && readCache(username)) || DEFAULT_PREFERENCES
    if (!username) return
    let cancelled = false
    api.preferences().then((p) => {
      if (cancelled || owner.current !== username) return
      const fresh = sanitize(p); confirmed.current = fresh; setPrefs(fresh); writeCache(username, fresh)
    }).catch(() => { /* the cached / default look stays; the next successful call refreshes it */ })
    return () => { cancelled = true }
  }, [username])

  useEffect(() => { document.documentElement.dataset.theme = prefs.theme }, [prefs.theme])

  const update = useCallback(async (change: Partial<Preferences>): Promise<boolean> => {
    if (!username) return false
    const next = sanitize({ ...prefs, ...change })
    setPrefs(next); setSaving(true); setSaveState(null)
    try {
      const stored = sanitize(await api.savePreferences(next))
      if (owner.current !== username) return false
      confirmed.current = stored; setPrefs(stored); writeCache(username, stored); setSaveState('saved')
      return true
    } catch {
      if (owner.current === username) { setPrefs(confirmed.current); setSaveState('failed') }
      return false
    } finally { setSaving(false) }
  }, [prefs, username])

  const value = useMemo<PreferencesValue>(() => ({
    ...prefs, saving, saveState, update,
  }), [prefs, saving, saveState, update])

  return <PreferencesContext.Provider value={value}>{children}</PreferencesContext.Provider>
}
