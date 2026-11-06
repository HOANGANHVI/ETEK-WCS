import type { FieldError, SavePlcBody, SaveRcsBody } from '../api/configTypes'

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
  if (v === null) { if (required) e.push({ field: 'port', message: 'Cần nhập cổng trước khi bật kết nối.' }) }
  else if (!Number.isInteger(v) || v < 1 || v > 65535) e.push({ field: 'port', message: 'Cổng phải nằm trong khoảng 1–65535.' })
}
function range(e: FieldError[], field: string, v: number | null, min: number, max: number, label: string) {
  if (v !== null && (!Number.isInteger(v) || v < min || v > max)) e.push({ field, message: `${label} phải nằm trong khoảng ${min}–${max}.` })
}
function timeout(e: FieldError[], v: number | null, required: boolean, min: number, max: number) {
  if (v === null) { if (required) e.push({ field: 'timeoutMs', message: 'Cần nhập timeout trước khi bật kết nối.' }) }
  else if (!Number.isInteger(v) || v < min || v > max) e.push({ field: 'timeoutMs', message: `Timeout phải nằm trong khoảng ${min}–${max} ms.` })
}

export function validatePlc(s: SavePlcBody): FieldError[] {
  const e: FieldError[] = []
  if (s.deviceName && s.deviceName.length > 100) e.push({ field: 'deviceName', message: 'Tên thiết bị tối đa 100 ký tự.' })
  if (s.ipAddress) { if (!isStrictIp(s.ipAddress)) e.push({ field: 'ipAddress', message: 'Địa chỉ IP không hợp lệ.' }) }
  else if (s.enabled) e.push({ field: 'ipAddress', message: 'Cần nhập địa chỉ IP trước khi bật kết nối.' })
  port(e, s.port, s.enabled)
  range(e, 'networkNumber', s.networkNumber, 0, 255, 'Network number')
  range(e, 'pcNumber', s.pcNumber, 0, 255, 'PC number')
  range(e, 'stationNumber', s.stationNumber, 0, 255, 'PLC station number')
  range(e, 'pollingIntervalMs', s.pollingIntervalMs, 50, 3_600_000, 'Chu kỳ đọc dữ liệu (ms)')
  if (s.protocol) { if (!PLC_PROTOCOLS.includes(s.protocol)) e.push({ field: 'protocol', message: 'Giao thức không nằm trong danh sách được hỗ trợ.' }) }
  else if (s.enabled) e.push({ field: 'protocol', message: 'Cần chọn giao thức trước khi bật kết nối.' })
  timeout(e, s.timeoutMs, s.enabled, 100, 60_000)
  return e
}

export function validateRcs(s: SaveRcsBody, hasPassword: boolean, hasToken: boolean): FieldError[] {
  const e: FieldError[] = []
  if (s.systemName && s.systemName.length > 100) e.push({ field: 'systemName', message: 'Tên hệ thống tối đa 100 ký tự.' })
  if (s.host) {
    const numeric = /^[0-9.]+$/.test(s.host)
    if (numeric ? !isStrictIp(s.host) : !HOST_RE.test(s.host)) e.push({ field: 'host', message: 'Địa chỉ IP hoặc hostname không hợp lệ.' })
  } else if (s.enabled) e.push({ field: 'host', message: 'Cần nhập địa chỉ IP hoặc hostname trước khi bật kết nối.' })
  port(e, s.port, s.enabled)
  if (s.baseUrl) {
    try {
      const u = new URL(s.baseUrl)
      if (u.protocol !== 'http:' && u.protocol !== 'https:') e.push({ field: 'baseUrl', message: 'Base URL phải bắt đầu bằng http:// hoặc https://.' })
      else if (u.username || u.password) e.push({ field: 'baseUrl', message: 'Không nhập tài khoản/mật khẩu trong Base URL.' })
      else if (s.host && u.hostname.toLowerCase() !== s.host.toLowerCase()) e.push({ field: 'baseUrl', message: 'Host trong Base URL phải trùng với địa chỉ IP/hostname đã nhập.' })
      else if (s.port !== null && u.port !== '' && Number(u.port) !== s.port) e.push({ field: 'baseUrl', message: 'Port trong Base URL phải trùng với Port đã nhập.' })
    } catch {
      e.push({ field: 'baseUrl', message: 'Base URL phải là địa chỉ tuyệt đối bắt đầu bằng http:// hoặc https://.' })
    }
  }
  if (!RCS_AUTH_MODES.includes(s.authMode)) e.push({ field: 'authMode', message: 'Phương thức xác thực không hợp lệ.' })
  if (s.authMode === 'UsernamePassword') {
    if (!s.username) e.push({ field: 'username', message: 'Cần nhập tên đăng nhập.' })
    if (s.enabled && !hasPassword) e.push({ field: 'password', message: 'Cần nhập mật khẩu trước khi bật kết nối.' })
  }
  if (s.authMode === 'Token' && s.enabled && !hasToken) e.push({ field: 'token', message: 'Cần nhập token trước khi bật kết nối.' })
  timeout(e, s.timeoutMs, s.enabled, 100, 120_000)
  return e
}
