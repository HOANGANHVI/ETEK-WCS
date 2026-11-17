import { useCallback } from 'react'
import { getLang, interpolate, useLang, type Lang, type Params } from './core'
import account from './messages/account'
import agv from './messages/agv'
import auth from './messages/auth'
import common from './messages/common'
import codes from './messages/codes'
import config from './messages/config'
import dashboard from './messages/dashboard'
import inventory from './messages/inventory'
import logs from './messages/logs'
import nav from './messages/nav'
import plc from './messages/plc'
import taskHistory from './messages/taskHistory'
import tasks from './messages/tasks'
import users from './messages/users'
import validation from './messages/validation'

export * from './core'

/** One dictionary pair per domain. A key is `<domain>.<name>`; the English dictionary must contain exactly the Vietnamese keys. */
const domains = { common, account, nav, auth, dashboard, agv, tasks, inventory, plc, config, users, logs, validation, codes, taskHistory }

type DomainKeys<D extends keyof typeof domains> = `${D}.${Extract<keyof (typeof domains)[D]['vi'], string>}`
export type MsgKey = { [D in keyof typeof domains]: DomainKeys<D> }[keyof typeof domains]

const table: Record<Lang, Record<string, string>> = { vi: {}, en: {} }
for (const [domain, pair] of Object.entries(domains)) {
  for (const lang of ['vi', 'en'] as const) {
    for (const [k, v] of Object.entries(pair[lang] as Record<string, string>)) table[lang][`${domain}.${k}`] = v
  }
}

/** True when a (dynamically composed) key exists in the dictionaries. */
export const hasMessage = (k: MsgKey): boolean => table.vi[k] !== undefined && table.en[k] !== undefined

export function translate(lang: Lang, key: MsgKey, params?: Params): string {
  const text = table[lang][key]
  if (text === undefined) {
    if (import.meta.env?.DEV) console.warn(`[i18n] missing ${lang} message: ${key}`)
    return key // a visible key is better than silently showing text in the wrong language
  }
  return interpolate(text, params)
}

/** Non-hook lookup using the current language (for code that runs outside React rendering). */
export const t = (key: MsgKey, params?: Params): string => translate(getLang(), key, params)

/** Hook form: re-renders the component when the language changes. */
export function useT() {
  const { lang } = useLang()
  return useCallback((key: MsgKey, params?: Params) => translate(lang, key, params), [lang])
}

/** All keys, for tests and tooling. */
export const allMessageKeys = (): string[] => Object.keys(table.vi)
