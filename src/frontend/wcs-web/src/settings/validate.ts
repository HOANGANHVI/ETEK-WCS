import type { FieldError, SavePlcBody, SaveRcsBody } from '../api/configTypes'
import { t } from '../i18n'

// Client-side mirror of the backend validator (the backend remains the authority). The forms use it for instant feedback and Demo Mode
// uses it so that it rejects exactly what the real API would reject.

export const PLC_PROTOCOLS = ['McProtocol3EBinaryTcp']
export const RCS_AUTH_MODES = ['None', 'Token', 'UsernamePassword']

/** Strict IP: rejects legacy shorthand such as "192.168.1". */
export function isStrictIp(v: string): boolean {
  if (v.includes(':')) return /^[0-9a-fA-F:.]+$/.test(v) && v.split(':').length >= 3
  const p = v.split('.')
  return p.length === 4 && p.every((x) => /^\d{1,3}$/.test(x) && Number(x) <= 255)
}

const HOST_RE = /^(?=.{1,253}$)([A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)(\.[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*$/

function port(e: FieldError[], v: number | null, required: boolean) {
  if (v === null) { if (required) e.push({ field: 'port', message: t('validation.portRequired') }) }
  else if (!Number.isInteger(v) || v < 1 || v > 65535) e.push({ field: 'port', message: t('validation.portRange') })
}
function range(e: FieldError[], field: string, v: number | null, min: number, max: number, label: string) {
  if (v !== null && (!Number.isInteger(v) || v < min || v > max)) e.push({ field, message: t('validation.rangeMsg', { label, min, max }) })
}
function timeout(e: FieldError[], v: number | null, required: boolean, min: number, max: number) {
  if (v === null) { if (required) e.push({ field: 'timeoutMs', message: t('validation.timeoutRequired') }) }
  else if (!Number.isInteger(v) || v < min || v > max) e.push({ field: 'timeoutMs', message: t('validation.timeoutRange', { min, max }) })
}

export function validatePlc(s: SavePlcBody): FieldError[] {
  const e: FieldError[] = []
  if (s.deviceName && s.deviceName.length > 100) e.push({ field: 'deviceName', message: t('validation.deviceNameMax') })
  if (s.ipAddress) { if (!isStrictIp(s.ipAddress)) e.push({ field: 'ipAddress', message: t('validation.ipInvalid') }) }
  else if (s.enabled) e.push({ field: 'ipAddress', message: t('validation.ipRequired') })
  port(e, s.port, s.enabled)
  range(e, 'networkNumber', s.networkNumber, 0, 255, 'Network number')
  range(e, 'pcNumber', s.pcNumber, 0, 255, 'PC number')
  range(e, 'stationNumber', s.stationNumber, 0, 255, 'PLC station number')
  range(e, 'pollingIntervalMs', s.pollingIntervalMs, 50, 3_600_000, t('validation.pollingLabel'))
  if (s.protocol) { if (!PLC_PROTOCOLS.includes(s.protocol)) e.push({ field: 'protocol', message: t('validation.protocolUnsupported') }) }
  else if (s.enabled) e.push({ field: 'protocol', message: t('validation.protocolRequired') })
  timeout(e, s.timeoutMs, s.enabled, 100, 60_000)
  return e
}

export function validateRcs(s: SaveRcsBody, hasPassword: boolean, hasToken: boolean): FieldError[] {
  const e: FieldError[] = []
  if (s.systemName && s.systemName.length > 100) e.push({ field: 'systemName', message: t('validation.systemNameMax') })
  if (s.host) {
    const numeric = /^[0-9.]+$/.test(s.host)
    if (numeric ? !isStrictIp(s.host) : !HOST_RE.test(s.host)) e.push({ field: 'host', message: t('validation.hostInvalid') })
  } else if (s.enabled) e.push({ field: 'host', message: t('validation.hostRequired') })
  port(e, s.port, s.enabled)
  if (s.baseUrl) {
    try {
      const u = new URL(s.baseUrl)
      if (u.protocol !== 'http:' && u.protocol !== 'https:') e.push({ field: 'baseUrl', message: t('validation.baseUrlScheme') })
      else if (u.username || u.password) e.push({ field: 'baseUrl', message: t('validation.baseUrlCredentials') })
      else if (s.host && u.hostname.toLowerCase() !== s.host.toLowerCase()) e.push({ field: 'baseUrl', message: t('validation.baseUrlHost') })
      else if (s.port !== null && u.port !== '' && Number(u.port) !== s.port) e.push({ field: 'baseUrl', message: t('validation.baseUrlPort') })
    } catch {
      e.push({ field: 'baseUrl', message: t('validation.baseUrlAbsolute') })
    }
  }
  if (!RCS_AUTH_MODES.includes(s.authMode)) e.push({ field: 'authMode', message: t('validation.authModeInvalid') })
  if (s.authMode === 'UsernamePassword') {
    if (!s.username) e.push({ field: 'username', message: t('validation.usernameRequired') })
    if (s.enabled && !hasPassword) e.push({ field: 'password', message: t('validation.passwordRequired') })
  }
  if (s.authMode === 'Token' && s.enabled && !hasToken) e.push({ field: 'token', message: t('validation.tokenRequired') })
  timeout(e, s.timeoutMs, s.enabled, 100, 120_000)
  return e
}
