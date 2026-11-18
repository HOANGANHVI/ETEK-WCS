import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { api } from '../api/client'
import type { Profile } from '../api/authTypes'
import { useAuth } from '../auth/AuthContext'
import { ProfileContext, type ProfileValue } from './ProfileContext'

interface Loaded { owner: string; profile: Profile }

/**
 * One shared copy of the signed-in account's profile (name, avatar version, organization) for the header and the User Configuration page, so a change made
 * on the page shows in the header at once. Nothing is persisted in the browser: it lives in memory, belongs to ONE username, and is never exposed to another
 * account (the value is derived against the current username, so the previous account's data disappears the moment the account changes or the user signs out).
 */
export function ProfileProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth()
  const username = user?.username ?? null
  const [loaded, setLoaded] = useState<Loaded | null>(null)
  const current = useRef<string | null>(null)

  useEffect(() => {
    current.current = username
    if (!username) return
    let cancelled = false
    api.profile().then((p) => { if (!cancelled && current.current === username) setLoaded({ owner: username, profile: p }) }).catch(() => { /* the header falls back to the session name */ })
    return () => { cancelled = true }
  }, [username])

  const reload = useCallback(async () => {
    if (!username) return
    const p = await api.profile()
    if (current.current === username) setLoaded({ owner: username, profile: p })
  }, [username])

  const profile = loaded && username !== null && loaded.owner === username ? loaded.profile : null
  const value = useMemo<ProfileValue>(() => ({ profile, reload }), [profile, reload])
  return <ProfileContext.Provider value={value}>{children}</ProfileContext.Provider>
}
