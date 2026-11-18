// Personal preference helpers: validation and the per-account browser cache (no backend, no DOM). Run: npm run preferences:test
import { DEFAULT_PREFERENCES, readCache, sanitize, writeCache } from '../src/preferences/prefsStore.ts'

const results: string[] = []
const check = (name: string, ok: boolean, info = '') => { results.push(`${ok ? 'PASS' : 'FAIL'} ${name}${ok || !info ? '' : ' — ' + info}`); if (!ok) process.exitCode = 1 }

const store = new Map<string, string>()
;(globalThis as unknown as { window: unknown }).window = { localStorage: { getItem: (k: string) => store.get(k) ?? null, setItem: (k: string, v: string) => { store.set(k, v) } } }

check('defaults are Light + Expanded', DEFAULT_PREFERENCES.theme === 'light' && DEFAULT_PREFERENCES.sidebarCollapsed === false)
check('valid values pass through', JSON.stringify(sanitize({ theme: 'dark', sidebarCollapsed: true })) === '{"theme":"dark","sidebarCollapsed":true}')
check('unknown theme falls back to light', sanitize({ theme: 'blue', sidebarCollapsed: true }).theme === 'light')
check('non-boolean sidebar falls back to expanded', sanitize({ theme: 'dark', sidebarCollapsed: 'yes' }).sidebarCollapsed === false)
check('garbage input gives the defaults', JSON.stringify(sanitize(null)) === JSON.stringify(DEFAULT_PREFERENCES) && JSON.stringify(sanitize('x')) === JSON.stringify(DEFAULT_PREFERENCES))

check('no cache for an unknown account', readCache('alice') === null)
writeCache('alice', { theme: 'dark', sidebarCollapsed: true })
writeCache('bob', { theme: 'light', sidebarCollapsed: false })
check('each account reads back its own cache', readCache('alice')?.theme === 'dark' && readCache('bob')?.theme === 'light')
check('cache key is case-insensitive per username', readCache('ALICE')?.sidebarCollapsed === true)
check('there is no shared key between accounts', [...store.keys()].every((k) => /\.(alice|bob)$/.test(k)))
store.set('wcs.prefs.v1.carol', '{not json')
check('a corrupt cache entry is ignored', readCache('carol') === null)

console.log(results.join('\n'))
console.log(`summary: ${results.filter((r) => r.startsWith('PASS')).length} pass, ${results.filter((r) => r.startsWith('FAIL')).length} fail`)
