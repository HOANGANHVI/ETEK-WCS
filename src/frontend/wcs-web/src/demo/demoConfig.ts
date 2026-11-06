/**
 * Demo Mode configuration store: keeps PLC/RCS "saved" settings in browser memory only. It never opens a socket, never calls fetch and
 * stores nothing on disk. Secrets are write-only exactly as in the real API (only "hasPassword/hasToken" flags are returned).
 */
import type { LastTest, PlcConfig, PlcSettings, RcsConfig, RcsSettings, SavePlcBody, SaveRcsBody } from '../api/configTypes'
import { validatePlc, validateRcs } from '../settings/validate'
import { DemoError } from './engine'

const emptyPlc = (): PlcSettings => ({ deviceName: null, ipAddress: null, port: null, protocol: null, networkNumber: null, pcNumber: null, stationNumber: null, timeoutMs: null, pollingIntervalMs: null })
const emptyRcs = (): RcsSettings => ({ systemName: null, host: null, port: null, baseUrl: null, authMode: 'None', username: null, timeoutMs: null })

interface Row<S> { configured: boolean; enabled: boolean; settings: S; lastTest: LastTest | null; updatedAt: string | null; updatedBy: string | null }

let plc: Row<PlcSettings> = { configured: false, enabled: false, settings: emptyPlc(), lastTest: null, updatedAt: null, updatedBy: null }
let rcs: Row<RcsSettings> = { configured: false, enabled: false, settings: emptyRcs(), lastTest: null, updatedAt: null, updatedBy: null }
let secrets: { password: string | null; token: string | null } = { password: null, token: null }

const clean = (v: string | null | undefined) => (v && v.trim() ? v.trim() : null)
const fail = (errors: { field: string; message: string }[]) => new DemoError(400, 'VALIDATION_FAILED', 'Dữ liệu cấu hình không hợp lệ.', errors)

export function resetDemoConfig() {
  plc = { configured: false, enabled: false, settings: emptyPlc(), lastTest: null, updatedAt: null, updatedBy: null }
  rcs = { configured: false, enabled: false, settings: emptyRcs(), lastTest: null, updatedAt: null, updatedBy: null }
  secrets = { password: null, token: null }
}

export const getPlc = (): PlcConfig => structuredClone(plc)

export function savePlc(b: SavePlcBody): PlcConfig {
  const settings: PlcSettings = {
    deviceName: clean(b.deviceName), ipAddress: clean(b.ipAddress), port: b.port, protocol: clean(b.protocol), networkNumber: b.networkNumber,
    pcNumber: b.pcNumber, stationNumber: b.stationNumber, timeoutMs: b.timeoutMs, pollingIntervalMs: b.pollingIntervalMs,
  }
  const errors = validatePlc({ ...settings, enabled: b.enabled })
  if (errors.length) throw fail(errors)
  plc = { configured: true, enabled: b.enabled, settings, lastTest: null, updatedAt: new Date().toISOString(), updatedBy: clean(b.updatedBy) ?? 'Demo' }
  return getPlc()
}

export const getRcs = (): RcsConfig => ({ ...structuredClone(rcs), secrets: { hasPassword: !!secrets.password, hasToken: !!secrets.token } })

export function saveRcs(b: SaveRcsBody): RcsConfig {
  const settings: RcsSettings = {
    systemName: clean(b.systemName), host: clean(b.host), port: b.port, baseUrl: clean(b.baseUrl), authMode: b.authMode, username: clean(b.username), timeoutMs: b.timeoutMs,
  }
  let password = b.clearPassword ? null : clean(b.password) ?? secrets.password
  let token = b.clearToken ? null : clean(b.token) ?? secrets.token
  const errors = validateRcs({ ...b, ...settings }, !!password, !!token)
  if (errors.length) throw fail(errors)
  if (settings.authMode !== 'UsernamePassword') password = null
  if (settings.authMode !== 'Token') token = null
  secrets = { password, token }
  rcs = { configured: true, enabled: b.enabled, settings, lastTest: null, updatedAt: new Date().toISOString(), updatedBy: clean(b.updatedBy) ?? 'Demo' }
  return getRcs()
}

function test(row: Row<unknown>, errors: { message: string }[]): LastTest {
  const at = new Date().toISOString()
  const done = (outcome: LastTest['outcome'], mode: string, message: string, error: string | null = null): LastTest => ({ outcome, mode, message, error, latencyMs: null, testedAt: at })
  if (!row.configured) return done('NotExecuted', 'None', 'Chưa có cấu hình đã lưu. Hãy nhập và lưu cấu hình trước khi kiểm tra.')
  if (!row.enabled) return done('NotExecuted', 'None', 'Kết nối đang ở trạng thái tắt nên không thực hiện kiểm tra.')
  if (errors.length) return done('NotExecuted', 'None', 'Cấu hình chưa đầy đủ hoặc không hợp lệ nên không thực hiện kiểm tra.', errors.map((e) => e.message).join('; '))
  return done('Simulated', 'Simulated', 'Kiểm tra MÔ PHỎNG (chế độ Demo): không có kết nối mạng nào tới thiết bị được thực hiện.')
}

export function testPlc(): LastTest {
  const r = test(plc, validatePlc({ ...plc.settings, enabled: true }))
  plc = { ...plc, lastTest: r }
  return structuredClone(r)
}

export function testRcs(): LastTest {
  const r = test(rcs, validateRcs({ ...rcs.settings, enabled: true, clearPassword: false, clearToken: false }, !!secrets.password, !!secrets.token))
  rcs = { ...rcs, lastTest: r }
  return structuredClone(r)
}
