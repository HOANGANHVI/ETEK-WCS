// Bilingual (vi/en) completeness check. Run: `npm run i18n:check` (exit code 1 on any problem).
//
// 1. Dictionaries  : every key exists in both languages, placeholders match, no unused keys.
// 2. Hardcoded text: no Vietnamese or English user-visible literals in components/pages outside the dictionaries (demo/ is excluded).
// 3. Backend codes : every code the backend can emit (error codes, audit actions, task-history events, PLC events, field-error codes, coded texts,
//                    persisted log templates) has a translation here. A new backend code without a translation fails this check.
//
// The checks read source text only (no build needed), so they also run in CI without a database.
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const here = path.dirname(fileURLToPath(import.meta.url))
const web = path.resolve(here, '..')
const src = path.join(web, 'src')
const backend = path.resolve(web, '..', '..', 'backend')
const problems = []
const notes = []
const fail = (area, msg) => problems.push(`[${area}] ${msg}`)

const walk = (d, skip = []) => fs.readdirSync(d, { withFileTypes: true }).flatMap((e) =>
  skip.includes(e.name) ? [] : e.isDirectory() ? walk(path.join(d, e.name), skip) : [path.join(d, e.name)])
const read = (f) => fs.readFileSync(f, 'utf8').replace(/\r\n/g, '\n')
const rel = (f) => path.relative(web, f).replaceAll('\\', '/')

/* ------------------------------------------------------------------ 1. dictionaries */
const messagesDir = path.join(src, 'i18n', 'messages')
const feFiles = walk(src).filter((f) => /\.(ts|tsx)$/.test(f) && !f.includes(`${path.sep}messages${path.sep}`))
const feText = feFiles.map(read).join('\n')
const dynamicPrefixes = ['taskHistory.d_', 'codes.field_', 'codes.fieldLabel_', 'codes.text_', 'codes.plcEvt_', 'codes.plcEvtTitle_', 'codes.auditDetail_', 'monitor.reason_', 'plc.writeState_', 'plc.sigDesc_', 'transfer.group_', 'transfer.groupDesc_', 'transfer.action_', 'transfer.blocker_', 'transfer.conflict_', 'transfer.field_', 'agv.amrStatus_', 'agv.fleetErr_', 'agv.fleetState_', 'agv.fleetConn_']
const dictKeys = new Set()
let keyCount = 0
for (const f of fs.readdirSync(messagesDir)) {
  const domain = f.replace(/\.ts$/, '')
  const s = read(path.join(messagesDir, f))
  const split = s.indexOf('\n}, {\n')
  const parse = (part) => Object.fromEntries([...part.matchAll(/^ {2}(\w+): '((?:[^'\\]|\\.)*)',?$/gm)].map((m) => [m[1], m[2]]))
  const vi = parse(s.slice(0, split)), en = parse(s.slice(split))
  const ph = (x) => [...x.matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort().join(',')
  for (const k of Object.keys(vi)) {
    keyCount++; dictKeys.add(`${domain}.${k}`)
    if (!(k in en)) fail('dict', `${domain}.${k} missing in EN`)
    else if (ph(vi[k]) !== ph(en[k])) fail('dict', `${domain}.${k} placeholders differ (vi: ${ph(vi[k])} | en: ${ph(en[k])})`)
    if (!vi[k].trim() || (en[k] !== undefined && !en[k].trim())) fail('dict', `${domain}.${k} is empty`)
    const full = `${domain}.${k}`
    const used = feText.includes(`'${full}'`) || dynamicPrefixes.some((p) => full.startsWith(p) && feText.includes(p))
    if (!used) fail('dict', `${full} is not used`)
  }
  for (const k of Object.keys(en)) if (!(k in vi)) fail('dict', `${domain}.${k} missing in VI`)
}
for (const m of feText.matchAll(/\bt\(\s*'((?:common|nav|auth|dashboard|agv|tasks|inventory|plc|config|users|logs|validation|codes|taskHistory|monitor|demo|account)\.\w+)'/g))
  if (!dictKeys.has(m[1])) fail('dict', `t('${m[1]}') has no dictionary entry`)

/* ------------------------------------------------------------------ 2. hardcoded text */
const VI_CHARS = /[àáạảãâầấậẩẫăằắặẳẵèéẹẻẽêềếệểễìíịỉĩòóọỏõôồốộổỗơờớợởỡùúụủũưừứựửữỳýỵỷỹđ]/i
const uiFiles = feFiles.filter((f) => {
  const r = rel(f)
  return /\.(tsx|ts)$/.test(r) && !r.startsWith('src/i18n/') && !r.startsWith('src/demo/') && !r.startsWith('src/api/')
})
const stripComments = (s) => s.replace(/\/\*[\s\S]*?\*\//g, (m) => m.replace(/[^\n]/g, ' ')).replace(/(^|[^:'"`])\/\/.*$/gm, '$1')
// Vietnamese typed on purpose as an input alias (the value box accepts "bật"/"tắt"): not display text.
const VI_ALLOWED = [/'bật'/, /'tắt'/]
for (const f of uiFiles) {
  const lines = stripComments(read(f)).split('\n')
  lines.forEach((line, i) => {
    if (VI_CHARS.test(line) && !VI_ALLOWED.some((a) => a.test(line))) fail('hardcoded-vi', `${rel(f)}:${i + 1}: ${line.trim().slice(0, 110)}`)
  })
}
// English text typed directly in JSX: text nodes with at least two lowercase words, and common text attributes.
const EN_ALLOWED = new Set(['Admin', 'Operator', 'PLC', 'RCS', 'AGV', 'IO', 'MQTT', 'WCS', 'ETEK', 'Token', 'Base URL', 'Port', 'Rack ID'])
for (const f of uiFiles.filter((x) => x.endsWith('.tsx'))) {
  const text = stripComments(read(f))
  for (const m of text.matchAll(/>\s*([A-Za-z][A-Za-z ,.'’()/&-]{3,}?)\s*</g)) {
    const t = m[1].trim()
    if (EN_ALLOWED.has(t) || /^[A-Z0-9_ /.-]+$/.test(t) || !/[a-z]{2,} [a-z]{2,}/i.test(t) || /^(return|const|if|else|new|typeof|void|await)\b/.test(t) || /[=;{}]/.test(t)) continue
    fail('hardcoded-en', `${rel(f)}: JSX text "${t.slice(0, 80)}"`)
  }
  for (const m of text.matchAll(/\b(placeholder|title|aria-label|label|hint|alt)="([^"{]*[A-Za-z]{3,}[^"{]*)"/g))
    if (!EN_ALLOWED.has(m[2]) && !/^[A-Z0-9_ /.-]+$/.test(m[2]) && /[a-z]{2,}/.test(m[2]) && !/^(Any|Full|Empty)$/.test(m[2])) fail('hardcoded-en', `${rel(f)}: attribute ${m[1]}="${m[2].slice(0, 60)}"`)
}

/* ------------------------------------------------------------------ 2b. no Vietnamese in English text, no raw text glued to translated text */
const VI_ANY = /[àáạảãâầấậẩẫăằắặẳẵèéẹẻẽêềếệểễìíịỉĩòóọỏõôồốộổỗơờớợởỡùúụủũưừứựửữỳýỵỷỹđ]/i
for (const f of fs.readdirSync(messagesDir)) {
  const s = read(path.join(messagesDir, f)); const i = s.indexOf('\n}, {\n')
  s.slice(i).split('\n').forEach((line, n) => { if (VI_ANY.test(line)) fail('vi-in-en', `messages/${f}: English entry contains Vietnamese: ${line.trim().slice(0, 90)}`) })
}
for (const m of read(path.join(src, 'i18n', 'systemLogTemplates.ts')).matchAll(/en: ('(?:[^'\\]|\\.)*'|"(?:[^"\\]|\\.)*")/g))
  if (VI_ANY.test(m[1])) fail('vi-in-en', `systemLogTemplates: English wording contains Vietnamese: ${m[1].slice(0, 90)}`)
// translated text must never be assembled with the raw stored message (e.g. "Title (raw message)")
for (const f of ['logCatalog.ts']) {
  const text = stripComments(read(path.join(src, 'i18n', f)))
  for (const m of text.matchAll(/`[^`]*\$\{[^}]*\bmessage\b[^}]*\}[^`]*`/g)) fail('raw-glued', `i18n/${f}: a template literal inserts the raw message: ${m[0].slice(0, 90)}`)
}

/* ------------------------------------------------------------------ 3. backend codes -> translations */
if (!fs.existsSync(backend)) { notes.push('backend source not found: backend code checks skipped') } else {
  const cs = walk(backend, ['bin', 'obj', 'Migrations']).filter((f) => f.endsWith('.cs'))
  const csText = (f) => read(f)
  const all = cs.map((f) => ({ f, s: csText(f) }))
  const uniq = (arr) => [...new Set(arr)]
  const grep = (re, group = 1, filter = () => true) => uniq(all.flatMap(({ f, s }) => [...s.matchAll(re)].filter((m) => filter(f, m)).map((m) => m[group])))
  const feFile = (n) => read(path.join(src, 'i18n', n))
  const feKeys = (text, re = /^\s*['"]?([A-Z][A-Z0-9_]+)['"]?:/gm) => new Set([...text.matchAll(re)].map((m) => m[1]))

  // 3a. error codes shown through describeError (vi.ts ERROR_VI + plc.ts PLC_ERROR_VI)
  const errorCodes = uniq([
    ...grep(/AppException\.\w+\(\s*"([A-Z][A-Z0-9_]+)"/g),
    ...grep(/DomainException\(\s*"([A-Z][A-Z0-9_]+)"/g),
    ...grep(/ApiResults\.Fail\([^"\n]*"([A-Z][A-Z0-9_]+)"/g),
    ...grep(/\bWrite\(ctx,\s*[^,]+,\s*"([A-Z][A-Z0-9_]+)"/g),
    ...grep(/new WriteResult\(false,\s*"([A-Z][A-Z0-9_]+)"/g),
    ...grep(/\(false,\s*"([A-Z][A-Z0-9_]+)",/g),
  ])
  const feErrors = new Set([...feKeys(feFile('vi.ts')), ...feKeys(feFile('plc.ts'))])
  const IGNORED_ERRORS = new Set(['ERROR', 'NETWORK_ERROR'])
  for (const c of errorCodes) if (!feErrors.has(c) && !IGNORED_ERRORS.has(c)) fail('error-code', `backend error code ${c} has no translation (i18n/vi.ts ERROR_VI or i18n/plc.ts PLC_ERROR_VI)`)

  // 3b. audit actions (AUDIT_ACTION_VI)
  const actions = uniq([
    ...grep(/Audited\.(?:Run|Write)Async(?:<[^>]*>)?\(\s*c,\s*"([A-Z][A-Z0-9_]+)"/g),
    ...grep(/new AuditRecord\(\s*"([A-Z][A-Z0-9_]+)"/g),
    ...grep(/\bAudit\(\s*\w+,\s*"([A-Z][A-Z0-9_]+)"/g),
    ...grep(/AuditAction\s*=\s*"([A-Z][A-Z0-9_]+)"/g),
    ...grep(/\(action:\s*"([A-Z][A-Z0-9_]+)"/g),
  ])
  const feActions = feKeys(feFile('vi.ts').split('AUDIT_ACTION_VI')[1] ?? '')
  for (const a of actions) if (!feActions.has(a)) fail('audit-action', `audit action ${a} has no translation (AUDIT_ACTION_VI)`)

  // 3c. task history events (HISTORY_EVENT_VI)
  const events = uniq([
    ...grep(/\.Note\(\s*"([A-Z][A-Z0-9_]+)"/g),
    ...grep(/new TaskHistoryEntry\([^\n]*?"([A-Z][A-Z0-9_]+)"/g),
    ...grep(/const string \w*Event\w*\s*=\s*"([A-Z][A-Z0-9_]+)"/g),
    ...grep(/new TaskEvent\(\s*"([A-Z][A-Z0-9_]+)"/g),
  ])
  const feEvents = feKeys((feFile('vi.ts').split('HISTORY_EVENT_VI')[1] ?? '').split('DEVICE_EVENT_RE')[0])
  for (const e of events) if (!feEvents.has(e) && !feEvents.has(e.replace(/^TASK_/, ''))) fail('history-event', `task history event ${e} has no translation (HISTORY_EVENT_VI)`)

  // 3d. PLC communication events
  const plcEvents = uniq([
    ...grep(/\.Log\(\s*PlcLogLevel\.\w+\s*,[^,\n]+,[^,\n]+,\s*"([A-Z][A-Z0-9_]+)"/g),
    ...grep(/\bLogLimited\(\s*"([A-Z][A-Z0-9_]+?)_?"/g),
    ...grep(/\bLogLimited\(\s*"([A-Z][A-Z0-9_]+)"\s*\+/g).map((x) => x.replace(/_$/, '')),   // "READ_BAD_" + station -> READ_BAD
  ])
  const codesText = feFile('messages/codes.ts')
  for (const e of plcEvents) if (!codesText.includes(`plcEvt_${e}:`)) fail('plc-event', `PLC event ${e} has no translation (messages/codes.ts plcEvt_${e})`)

  // 3e. field-error codes and coded texts
  const fieldCodes = uniq([
    ...grep(/new(?: FieldError)?\(\s*\w+\s*,\s*(?:\$?"(?:[^"\\]|\\.)*"),\s*"([A-Z][A-Z0-9_]+)"/g),
    ...grep(/new(?: FieldError)?\(\s*"\w+"\s*,\s*(?:\$?"(?:[^"\\]|\\.)*"),\s*"([A-Z][A-Z0-9_]+)"/g),
    ...grep(/\bnew\(p\.Id[^;]*?,\s*"([A-Z][A-Z0-9_]+)",/g),
  ])
  for (const c of fieldCodes) if (!codesText.includes(`field_${c}`)) fail('field-error', `field error code ${c} has no translation (messages/codes.ts field_${c})`)
  const codedTexts = grep(/"\[([A-Z][A-Z0-9_]+)\]/g)
  for (const c of codedTexts) if (!codesText.includes(`text_${c}:`)) fail('coded-text', `coded text [${c}] has no translation (messages/codes.ts text_${c})`)

  // 3e2. task history detail codes (written by the backend as HistoryDetail codes; read-side formats in Domain/Tasks/HistoryDetail.cs)
  const historyCodes = uniq([
    ...grep(/HistoryDetail\.Of\(\s*"([A-Z][A-Z0-9_]+)"/g),
    ...all.filter((x) => x.f.endsWith('HistoryDetail.cs')).flatMap(({ s }) => [...s.matchAll(/"([A-Z][A-Z0-9_]{3,})"/g)].map((m) => m[1])),
    'RCS_REJECTED', 'RCS_CONTINUE_REJECTED', 'QUEUED_AGV_HOLD', 'QUEUED_AGV_BUSY',
  ])
  const historyText = read(path.join(src, 'i18n', 'messages', 'taskHistory.ts'))
  for (const c of historyCodes) if (!historyText.includes(`d_${c}:`)) fail('history-detail', `task history detail code ${c} has no translation (messages/taskHistory.ts d_${c})`)
  notes.push(`backend: ${historyCodes.length} task history detail codes`)

  // 3f. persisted system-log templates
  const tplFile = read(path.join(src, 'i18n', 'systemLogTemplates.ts'))
  const templates = []
  for (const { f, s } of all) {
    const re = /\.Log(Information|Warning|Error|Critical)\(\s*(?:ex,\s*)?((?:\$?"(?:[^"\\]|\\.)*"\s*\+?\s*)+)/g
    let m
    while ((m = re.exec(s))) {
      const lit = [...m[2].matchAll(/"((?:[^"\\]|\\.)*)"/g)].map((x) => x[1]).join('')
      if (m[1] === 'Information' && !lit.includes('{EventCode}')) continue
      templates.push(lit.replace(/\\"/g, '"'))
    }
  }
  for (const tpl of uniq(templates)) {
    const needle = tpl.includes("'") ? `"${tpl.replace(/"/g, '\\"')}":` : `'${tpl}':`
    if (!tplFile.includes(needle)) fail('log-template', `persisted log template has no translation: ${tpl.slice(0, 100)}`)
  }
  // 3g. Vietnamese text in the backend may only exist where the UI never shows it as it is: fallback messages of coded errors, coded texts ([CODE] ...),
  //     the default administrator name (mapped in the UI) and seed data. Anything else (log messages, reasons, exception texts) must be English + code/args.
  const VI_ALLOWED_LINE = [
    /AppException\.|DomainException\(|: AppException\(|new FieldError|errors\.Add\(new|e\.Add\(new|\[new\(|\bRange\(/,   // message of a coded error (the UI uses the code)
    /new\(null, AddressProblem|new\(p\.Id|parsed\.Message|new\(false, "|new WriteResult\(false,|FailPendingWrites\(/,   // coded address / write results
    /"\[[A-Z][A-Z0-9_]+\] /, /^\s*"[^"]*"\)\);\s*$/,                                                                        // coded texts and their continuation lines
    /ApiResults\.Fail\(|\bWrite\(ctx,|const string Generic|FieldArgs\.Of\(/,                                                                // coded API errors
    /"Quản trị viên"/,                                                                                                       // default administrator name (UI: displayNameOf)
  ]
  const VI = /[àáạảãâầấậẩẫăằắặẳẵèéẹẻẽêềếệểễìíịỉĩòóọỏõôồốộổỗơờớợởỡùúụủũưừứựửữỳýỵỷỹđ]/i
  for (const { f, s } of all) {
    if (/Seed|appsettings/i.test(f)) continue
    s.split('\n').forEach((line, i) => {
      if (!VI.test(line) || /^\s*(\/\/|\/\*|\*)/.test(line) || VI_ALLOWED_LINE.some((r) => r.test(line))) return
      fail('backend-vi', `${path.relative(backend, f).replaceAll('\\', '/')}:${i + 1}: Vietnamese text outside the allowed categories: ${line.trim().slice(0, 100)}`)
    })
  }
  notes.push(`backend: ${errorCodes.length} error codes, ${actions.length} audit actions, ${events.length} history events, ${plcEvents.length} PLC events, ${fieldCodes.length} field codes, ${codedTexts.length} coded texts, ${uniq(templates).length} log templates`)
}

console.log(`i18n: ${keyCount} keys checked`)
for (const n of notes) console.log(`i18n: ${n}`)
if (problems.length) { console.error(`\n${problems.length} problem(s):`); for (const p of problems) console.error(' - ' + p); process.exit(1) }
console.log('i18n: OK')
