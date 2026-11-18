import { useSyncExternalStore } from 'react'

/**
 * Runtime localization core (no external framework). The language lives in a tiny external store so that switching
 * re-renders every component that reads it, without reloading the page. Vietnamese is the default.
 */
export type Lang = 'vi' | 'en'
export const LANGS: readonly Lang[] = ['vi', 'en']
export const DEFAULT_LANG: Lang = 'vi'
export const LANG_STORAGE_KEY = 'wcs.lang'

export function parseLang(value: unknown): Lang {
  return value === 'vi' || value === 'en' ? value : DEFAULT_LANG
}

function readStored(): Lang {
  try { return parseLang(window.localStorage.getItem(LANG_STORAGE_KEY)) } catch { return DEFAULT_LANG }
}

let current: Lang = readStored()
const listeners = new Set<() => void>()

function applyDocumentLang(l: Lang) {
  try { document.documentElement.lang = l } catch { /* non-DOM environment */ }
}
applyDocumentLang(current)

export const getLang = (): Lang => current

export function setLang(next: Lang) {
  const l = parseLang(next)
  if (l === current) return
  current = l
  try { window.localStorage.setItem(LANG_STORAGE_KEY, l) } catch { /* storage unavailable: language still switches for this session */ }
  applyDocumentLang(l)
  listeners.forEach((fn) => fn())
}

const subscribe = (fn: () => void) => { listeners.add(fn); return () => { listeners.delete(fn) } }

/** Subscribes the component to language changes. */
export function useLang(): { lang: Lang; setLang: (l: Lang) => void } {
  const lang = useSyncExternalStore(subscribe, getLang, () => DEFAULT_LANG)
  return { lang, setLang }
}

/* ------------------------------------------------------------------ messages */
export type Params = Record<string, string | number>

/** Pair of dictionaries of one domain. `en` must contain exactly the keys of `vi` (compile-time missing-key check). */
export function defineMessages<const V extends Record<string, string>>(vi: V, en: Record<keyof V, string>) {
  return { vi, en } as { vi: V; en: Record<keyof V, string> }
}

export function interpolate(template: string, params?: Params): string {
  if (!params) return template
  return template.replace(/\{(\w+)\}/g, (m, k: string) => (k in params ? String(params[k]) : m))
}

/**
 * Locale-aware lookup table for enum labels. Reads the current language at access time, so existing `MAP[value] ?? value`
 * call sites keep working and follow the language selected. Keys are never translated, only the labels.
 */
export function localizedMap<const V extends Record<string, string>>(vi: V, en: Record<keyof V, string>): Record<string, string> {
  const pick = (): Record<string, string> => (getLang() === 'en' ? en : vi)
  return new Proxy({} as Record<string, string>, {
    get: (_t, p) => (typeof p === 'string' ? pick()[p] : undefined),
    has: (_t, p) => typeof p === 'string' && p in pick(),
    ownKeys: () => Reflect.ownKeys(pick()),
    getOwnPropertyDescriptor: (_t, p) => (typeof p === 'string' && p in pick() ? { enumerable: true, configurable: true, value: pick()[p] } : undefined),
  })
}

/** Pick by language, for values that are not plain strings (objects, lists). */
export function pickLang<T>(vi: T, en: T): T { return getLang() === 'en' ? en : vi }
