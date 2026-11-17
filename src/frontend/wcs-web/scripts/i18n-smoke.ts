// Runtime test of the bilingual layer (language switch, interpolation, fallbacks, log and task-history wording). Run: npm run i18n:test
const store = new Map<string, string>()
;(globalThis as Record<string, unknown>).window = { localStorage: { getItem: (k: string) => store.get(k) ?? null, setItem: (k: string, v: string) => { store.set(k, v) } } }
;(globalThis as Record<string, unknown>).document = { documentElement: { lang: '' } }
const results: string[] = []
const check = (name: string, ok: boolean) => { results.push(`${ok ? 'PASS' : 'FAIL'} ${name}`); if (!ok) process.exitCode = 1 }

const mod = await import('../src/i18n/index.ts')
const vi = await import('../src/i18n/vi.ts')
const plc = await import('../src/i18n/plc.ts')
const fmt = await import('../src/i18n/format.ts')

check('default language is Vietnamese', mod.getLang() === 'vi')
check('vi label', mod.t('nav.dashboard') === 'Tổng quan')
check('vi enum map', vi.TASK_STATE_VI.Failed === 'Thất bại')
check('vi interpolation', mod.t('agv.holdTitle', { code: 'AGV1' }).includes('AGV1'))
mod.setLang('en')
check('switch to en', mod.getLang() === 'en')
check('persisted', store.get('wcs.lang') === 'en')
check('document lang', ((globalThis as Record<string, unknown>).document as { documentElement: { lang: string } }).documentElement.lang === 'en')
check('en label', mod.t('nav.dashboard') === 'Dashboard')
check('en enum map follows language', vi.TASK_STATE_VI.Failed === 'Failed' && vi.AGV_CONNECTION_VI.Offline === 'Disconnected')
check('en Object.entries on map', Object.entries(vi.AUDIT_ACTION_VI).length > 10 && Object.entries(vi.AUDIT_ACTION_VI).some(([, v]) => v === 'Sign in'))
check('en interpolation', mod.t('dashboard.kpiFailureRateHint', { failed: 3, total: 10 }) === '3 failed / 10 finished tasks')
check('en reason code', vi.reasonVi('RCS_ENDED_WITHOUT_DROP').startsWith('RCS reported'))
check('en backend free text kept', vi.reasonVi('AGV busy; request queued') === 'AGV busy; request queued')
check('vi reason after switch back', (mod.setLang('vi'), vi.reasonVi('AGV busy; request queued')) === 'AGV đang bận, yêu cầu được xếp hàng chờ')
check('error codes keep identifiers (unknown code shown)', vi.describeError({} as unknown) === 'Đã xảy ra lỗi không xác định.')
mod.setLang('en')
check('device message en', plc.localizeDeviceMessage('simulated PLC is offline') === 'Simulated PLC: Not connected')
check('timeAgo en', fmt.timeAgo(new Date(Date.now() - 30_000).toISOString()) === '30s ago')
mod.setLang('vi')
check('timeAgo vi', fmt.timeAgo(new Date(Date.now() - 30_000).toISOString()) === '30 giây trước')
check('flow label follows language', (mod.setLang('en'), vi.flowInfo('Bp01').label.startsWith('Pick up full rack')))
store.set('wcs.lang', 'xx')
check('invalid value falls back to vi', mod.parseLang(store.get('wcs.lang')) === 'vi' && mod.parseLang(undefined) === 'vi' && mod.parseLang('EN') === 'vi')
check('all keys have both languages', mod.allMessageKeys().length > 700 && mod.allMessageKeys().every((k) => { mod.setLang('vi'); const a = mod.translate('vi', k as never); const b = mod.translate('en', k as never); return a !== k && b !== k && a.length > 0 && b.length > 0 }))
const cat = await import('../src/i18n/logCatalog.ts')
const fmt2 = await import('../src/i18n/format.ts')
const sysTpl = 'AGV {Agv} placed on dispatch hold because of task {TaskId}: {Reason}'
const sysEntry = { category: 'RCS_AGV', source: 'WCS.Application.Services.TaskOrchestrator', device: 'AGV1', event: null, message: 'AGV AGV1 placed on dispatch hold because of task T1: X', messageTemplate: sysTpl, detailsJson: '{"Agv":"AGV1","TaskId":"T1","Reason":"X"}' }
mod.setLang('vi')
check('system log vi', cat.localizeLog(sysEntry).text === 'AGV AGV1 bị giữ điều phối do nhiệm vụ T1: X')
mod.setLang('en')
check('system log en (same historic row)', cat.localizeLog(sysEntry).text === 'AGV AGV1 placed on dispatch hold because of task T1: X' && cat.localizeLog(sysEntry).translated)
check('system log without template shows raw', cat.localizeLog({ ...sysEntry, messageTemplate: null, message: 'raw text' }).text === 'raw text')
const plcNew = { category: 'PLC', source: 'PLC.Runtime', device: 'PLC-01', event: 'CONNECT_FAILED', message: 'Connection failed: boom.', messageTemplate: null, detailsJson: '{"args":{"error":"boom","backoffMs":500}}' }
check('plc event en with args', cat.localizeLog(plcNew).text === 'Connection failed: boom. Retrying in 500 ms.')
mod.setLang('vi')
check('plc event vi with args', cat.localizeLog(plcNew).text === 'Kết nối thất bại: boom. Thử lại sau 500 ms.')
const plcOld = { ...plcNew, event: 'READ_BAD_A1', message: '2 IO point đọc lỗi: x', detailsJson: null }
check('plc historic event: sentence rebuilt from the stored format; Vietnamese detail is dropped in English', (mod.setLang('vi'), cat.localizeLog(plcOld).text) === 'Một số tín hiệu đọc lỗi' || cat.localizeLog(plcOld).text.startsWith('2 tín hiệu đọc lỗi'))
const plcState = { ...plcNew, event: 'STATE', message: 'PLC-01: Faulted - [PLC_LINK_LOST] Mất kết nối PLC.', detailsJson: '{"args":{"plc":"PLC-01","state":"Faulted","reason":"[PLC_LINK_LOST] Mất kết nối PLC."}}' }
mod.setLang('en')
check('plc STATE coded reason en', cat.localizeLog(plcState).text === 'PLC-01: state Faulted – The PLC connection was lost.')
const aud = { category: 'SECURITY_AUDIT', source: 'Audit', device: 'admin', event: 'AUTH_LOGIN', message: 'wrong password', messageTemplate: null, detailsJson: null }
check('audit en', cat.localizeLog(aud).text === 'Sign in — Wrong password')
mod.setLang('vi')
check('audit vi', cat.localizeLog(aud).text === 'Đăng nhập — Sai mật khẩu')
check('audit no detail', cat.localizeLog({ ...aud, event: 'AUTH_LOGOUT', message: 'AUTH_LOGOUT Success' }).text === 'Đăng xuất')
check('audit user text untouched', cat.localizeAuditDetail('please cancel: operator typed this') === 'please cancel: operator typed this')
check('audit failed code', cat.localizeAuditDetail('TASK_NOT_FOUND: Không tìm thấy nhiệm vụ.') === 'Không tìm thấy nhiệm vụ.')
mod.setLang('en')
check('audit failed code en', cat.localizeAuditDetail('TASK_NOT_FOUND: Không tìm thấy nhiệm vụ.') === 'Task not found.')
check('audit deleted (historic Vietnamese constant) en', cat.localizeAuditDetail('Đã xóa cứng nhiệm vụ NV-ABCD1234') === 'Task NV-ABCD1234 permanently deleted')
check('field error en', cat.localizeFieldError({ field: 'port', message: 'x', code: 'RANGE', args: { min: 1, max: 5 } }) === 'TCP port must be between 1 and 5.')
check('field error radix kind', cat.localizeFieldError({ field: 'address', message: 'x', code: 'RADIX_UNCONFIRMED', args: { kind: 'xy', address: 'X10' } }).includes('X10'))
check('field error unknown code keeps server text', cat.localizeFieldError({ field: 'a', message: 'server text', code: 'NEW_CODE' }) === 'server text')
check('coded text en', cat.describeCoded('[SIMULATOR_MODE] Chế độ mô phỏng: …') === 'Simulation mode: there is no connection to a physical PLC.')
check('coded text unknown keeps rest', cat.describeCoded('[SOMETHING_NEW] raw detail') === 'raw detail')
check('default admin name follows language', fmt2.displayNameOf('Quản trị viên') === 'Administrator' && fmt2.displayNameOf('Nguyễn Văn A') === 'Nguyễn Văn A')
mod.setLang('vi')
check('default admin name vi', fmt2.displayNameOf('Quản trị viên') === 'Quản trị viên')

// ---- task history: code + parameters, both languages, historical rows and fallbacks
mod.setLang('vi')
const holdArgs = { agv: 'AGV1', reason: 'RCS_ENDED_WITHOUT_DROP' }
check('history vi', cat.describeHistory('AGV_DISPATCH_HOLD', holdArgs, 'AGV1 placed on dispatch hold: RCS_ENDED_WITHOUT_DROP') === 'AGV1 bị giữ điều phối: RCS báo kết thúc nhưng WCS chưa nhận xác nhận thả rack')
mod.setLang('en')
check('history en (same row after switching language)', cat.describeHistory('AGV_DISPATCH_HOLD', holdArgs, 'x') === 'AGV1 placed on dispatch hold: RCS reported the end of the task but WCS has not received the drop confirmation')
check('history ids are not translated', cat.describeHistory('DESTINATION_ASSIGNED', { location: 'B2' }, 'raw') === 'Destination B2 assigned')
check('history leg label', cat.describeHistory('WAITING_PLC_PERMISSION', { leg: 'Drop' }, 'raw') === 'Waiting for PLC permission: Drop rack')
check('history timeout state label', cat.describeHistory('TIMEOUT', { state: 'Executing' }, 'raw') === 'Timed out in state In progress')
check('history user text kept verbatim', cat.describeHistory('CANCEL_REQUESTED', { user: 'tester', reason: 'Dừng gấp {x}' }, 'raw') === 'tester requested cancellation: Dừng gấp {x}')
check('history unknown code falls back to the original text', cat.describeHistory('SOME_FUTURE_CODE', { a: '1' }, 'Original English text') === 'Original English text')
check('history without code shows the original text', cat.describeHistory(null, null, 'Raw RCS message') === 'Raw RCS message' && cat.describeHistory(undefined, undefined, null) === '')
check('hold reason: known code translated, unknown kept', cat.describeReasonCode('NO_SOURCE_AVAILABLE') === 'No suitable source rack available' && cat.describeReasonCode('SOMETHING_ELSE') === 'SOMETHING_ELSE' && cat.describeReasonCode('free text') === 'free text')
mod.setLang('vi')
check('history missing parameter stays visible', cat.describeHistory('DESTINATION_ASSIGNED', {}, 'raw') === 'Đã chỉ định vị trí đích {location}')
check('every history code has both languages and the same parameters', mod.allMessageKeys().filter((k) => k.startsWith('taskHistory.')).every((k) => {
  const a = mod.translate('vi', k as never), b = mod.translate('en', k as never)
  const ph = (x: string) => [...x.matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort().join()
  return a !== k && b !== k && ph(a) === ph(b)
}))

// ---- regression found in runtime acceptance: a PLC STATE event without a reason must not leave a raw placeholder
mod.setLang('en')
const stateNoReason = { category: 'PLC', source: 'PLC.Runtime', device: 'PLC-03', event: 'STATE', message: 'PLC-03: Connected', messageTemplate: null, detailsJson: '{"newState":"Connected","args":{"plc":"PLC-03","state":"Connected"}}' }
console.log(cat.localizeLog(stateNoReason).text === 'PLC-03: state Connected' ? 'PASS state event without reason has no placeholder' : 'FAIL state event without reason: ' + cat.localizeLog(stateNoReason).text)

// ---- no mixed languages: a translated entry is never followed by the raw stored text, and English output has no Vietnamese
const VI_CHARS = /[àáạảãâầấậẩẫăằắặẳẵèéẹẻẽêềếệểễìíịỉĩòóọỏõôồốộổỗơờớợởỡùúụủũưừứựửữỳýỵỷỹđ]/i
const reported = { category: 'PLC', source: 'PLC.Runtime', device: 'PLC-01', event: 'STATION_ERROR_A1', message: 'Lỗi xử lý trạm: simulated PLC is offline', messageTemplate: null, detailsJson: null }
mod.setLang('en')
check('reported case: English sentence with the technical detail, no Vietnamese', cat.localizeLog(reported).text === 'Station processing error: simulated PLC is offline')
mod.setLang('vi')
check('reported case: Vietnamese sentence with the same technical detail', cat.localizeLog(reported).text === 'Lỗi xử lý trạm: simulated PLC is offline')
const corpus = [
  reported,
  { ...reported, event: 'CONNECT_FAILED', message: 'Kết nối thất bại: x. Thử lại sau 2000 ms.' },
  { ...reported, event: 'LINK_LOST', message: 'Mất kết nối: x.' },
  { ...reported, event: 'RELOAD', message: 'PLC-01: cấu hình được nạp lại.' },
  { ...reported, event: 'WRITE_AMBIGUOUS', message: 'A1/Rack_Full: Không nhận được xác nhận cho lệnh ghi. KHÔNG tự động ghi lại.' },
  { ...reported, event: 'HANDSHAKE_TIMEOUT', message: 'Pickup_Request vẫn bật sau 5000 ms (chính sách retry chưa được phê duyệt).' },
  { ...reported, event: 'REQUEST_ID_MISSING', message: 'Pickup_Request bật nhưng Request_ID chưa đọc được.' },
  { ...reported, detailsJson: '{"args":{"error":"simulated PLC is offline"}}' },
  { category: 'SYSTEM', source: 'WCS', device: null, event: null, message: 'x', messageTemplate: 'Station request {Key} from {Station} rejected: {Code} {Message}', detailsJson: '{"Key":"k","Station":"A1","Code":"FLOW_IN_REVIEW","Message":"Quy trình đang xem xét"}' },
  { category: 'SECURITY_AUDIT', source: 'Audit', device: 'u', event: 'TASK_DELETE', message: 'Đã xóa cứng nhiệm vụ NV-ABCD1234', messageTemplate: null, detailsJson: null },
  { category: 'SECURITY_AUDIT', source: 'Audit', device: 'u', event: 'TASK_CANCEL', message: 'TASK_ALREADY_FINISHED: Nhiệm vụ đã kết thúc', messageTemplate: null, detailsJson: null },
  { category: 'SECURITY_AUDIT', source: 'Audit', device: 'u', event: 'TASK_CANCEL', message: 'FUTURE_CODE_NOT_IN_DICTIONARY: Câu tiếng Việt', messageTemplate: null, detailsJson: null },
]
mod.setLang('en')
for (const row of corpus) { const out = cat.localizeLog(row); check(`English output has no Vietnamese (${row.event ?? row.messageTemplate?.slice(0, 20)})`, !VI_CHARS.test(out.text)) }
check('coded probe error list is localized', !VI_CHARS.test(cat.describeTestError('[CONFIG_INVALID] [{"field":"port","code":"PORT_RANGE","args":null},{"field":"ipAddress","code":"IP_INVALID","args":null}]')))
check('plc params list uses labels', cat.describeCoded('[PLC_PARAMS_MISSING] port, protocol') === 'PLC configuration parameters are missing: TCP port, Protocol')
// every English dictionary text and every English label table is free of Vietnamese
const bad: string[] = []
for (const k of mod.allMessageKeys()) if (VI_CHARS.test(mod.translate('en', k as never))) bad.push(k)
check(`English dictionary has no Vietnamese (${bad.slice(0, 3).join(',')})`, bad.length === 0)
const tables = [await import('../src/i18n/vi.ts'), await import('../src/i18n/plc.ts'), await import('../src/i18n/config.ts'), await import('../src/i18n/logs.ts')]
const badTables: string[] = []
for (const ns of tables) for (const [name, table] of Object.entries(ns)) {
  if (!name.endsWith('_VI') && name !== 'LOG_CATEGORY_HINT' && name !== 'ERROR_VI') continue
  for (const [k, v] of Object.entries(table as Record<string, unknown>)) if (typeof v === 'string' && VI_CHARS.test(v) && k !== 'Unknown') badTables.push(`${name}.${k}`)
}
check(`English label tables have no Vietnamese (${badTables.slice(0, 3).join(',')})`, badTables.length === 0)

// ---- historic PLC events: parameters read back from the exact stored format; a Vietnamese stored detail never reaches English output
mod.setLang('en')
check('historic CONNECT_FAILED rebuilt in English', cat.localizeLog({ ...reported, event: 'CONNECT_FAILED', message: 'Kết nối thất bại: simulated PLC is offline. Thử lại sau 2000 ms.' }).text === 'Connection failed: simulated PLC is offline. Retrying in 2000 ms.')
check('historic RELOAD rebuilt in English', cat.localizeLog({ ...reported, event: 'RELOAD', message: 'PLC-01: cấu hình được nạp lại.' }).text === 'PLC-01: configuration reloaded')
const viDetail = cat.localizeLog({ ...reported, event: 'STATION_ERROR_A1', message: 'Lỗi xử lý trạm: Không kết nối được' })
check('a Vietnamese stored detail falls back to the English headline only', viDetail.text === 'Station processing error' && viDetail.translated)
const unknownEvent = cat.localizeLog({ ...reported, event: 'NEW_FUTURE_EVENT', message: 'Một sự kiện lạ' })
check('unknown event is shown as stored and flagged as not translated', unknownEvent.text === 'Một sự kiện lạ' && !unknownEvent.translated)
mod.setLang('vi')
console.log(results.join(String.fromCharCode(10)))
