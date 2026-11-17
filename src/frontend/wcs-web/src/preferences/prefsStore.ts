/**
 * Personal interface preferences. The server is the source of truth (stored per account); the browser keeps only a per-account cache, so the right
 * look appears at once after a reload. There is no cache shared between accounts.
 */
export type Theme = 'light' | 'dark'
export interface Preferences { theme: Theme; sidebarCollapsed: boolean }

export const DEFAULT_PREFERENCES: Preferences = { theme: 'light', sidebarCollapsed: false }
const cacheKey = (username: string) => `wcs.prefs.v1.${username.toLowerCase()}`

/** Accepts only valid values; anything else (corrupt cache, unknown theme) falls back to the default for that field. */
export function sanitize(value: unknown): Preferences {
  const v = (value && typeof value === 'object' ? value : {}) as Record<string, unknown>
  return {
    theme: v.theme === 'dark' ? 'dark' : 'light',
    sidebarCollapsed: v.sidebarCollapsed === true,
  }
}

export function readCache(username: string): Preferences | null {
  try {
    const raw = window.localStorage.getItem(cacheKey(username))
    return raw ? sanitize(JSON.parse(raw)) : null
  } catch { return null }
}

export function writeCache(username: string, prefs: Preferences) {
  try { window.localStorage.setItem(cacheKey(username), JSON.stringify(prefs)) } catch { /* cache only: the server keeps the value */ }
}
