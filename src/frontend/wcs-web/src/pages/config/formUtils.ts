import { ApiRequestError } from '../../api/errors'
import type { FieldError } from '../../api/configTypes'
import { t } from '../../i18n'
import { describeError } from '../../i18n/vi'

export const toInt = (v: string): number | null => (v.trim() === '' ? null : Number(v.trim()))
export const toStr = (v: string): string | null => (v.trim() === '' ? null : v.trim())
export const fromNum = (v: number | null | undefined): string => (v === null || v === undefined ? '' : String(v))

export type Errors = Record<string, string>

export function toErrors(list: FieldError[]): Errors {
  const e: Errors = {}
  for (const x of list) e[x.field] ??= x.message
  return e
}

/** Field errors returned by the API (HTTP 400, error.details = [{field,message}]) or a general message. */
export function fromApiError(e: unknown): { fields: Errors; general: string | null } {
  if (e instanceof ApiRequestError && e.code === 'VALIDATION_FAILED' && Array.isArray(e.details)) {
    return { fields: toErrors(e.details as FieldError[]), general: t('validation.invalidForm') }
  }
  return { fields: {}, general: describeError(e) }
}
