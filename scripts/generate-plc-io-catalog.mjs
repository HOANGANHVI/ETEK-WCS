// Generates src/backend/WCS.Infrastructure/Plc/Catalog/plc-io-catalog.json from the ONLY IO map of the project (CR-045 / CR-046).
// The workbook stays the source of truth; this file is a derived copy that the seeder loads. Nothing is invented:
//  - every address is the address written in the workbook and is a REFERENCE (not a configured address of the running system);
//  - port / MC parameters, handshake and reset rules are not in the workbook and stay absent;
//  - nothing is marked confirmed: the PLC engineer has not confirmed the IO contract.
//
//   node scripts/generate-plc-io-catalog.mjs            (writes the JSON)
//   node scripts/generate-plc-io-catalog.mjs --check    (fails if the committed JSON is out of date)
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import zlib from 'node:zlib'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const SOURCE = 'docs/02_Interface_Specification/PLC_IO_Map/AGV_PLC Interface_List_22_11_2026.xlsx'
const SHA256 = '405586c9e4d2893f1d3a33922259ecfe1c400ca877710877b3b482e049b34da3'
const outFile = path.join(root, 'src/backend/WCS.Infrastructure/Plc/Catalog/plc-io-catalog.json')
const fail = (m) => { console.error(m); process.exit(1) }

// ---- minimal .xlsx reader (zip central directory + inflate) -------------------------------------------------
function readZip(buf) {
  let eocd = -1
  for (let i = buf.length - 22; i >= 0; i--) if (buf.readUInt32LE(i) === 0x06054b50) { eocd = i; break }
  if (eocd < 0) throw new Error('not a zip file')
  const count = buf.readUInt16LE(eocd + 10); let p = buf.readUInt32LE(eocd + 16)
  const files = new Map()
  for (let n = 0; n < count; n++) {
    const method = buf.readUInt16LE(p + 10), csize = buf.readUInt32LE(p + 20)
    const nameLen = buf.readUInt16LE(p + 28), extraLen = buf.readUInt16LE(p + 30), commentLen = buf.readUInt16LE(p + 32)
    const lho = buf.readUInt32LE(p + 42); const name = buf.toString('utf8', p + 46, p + 46 + nameLen)
    const lhNameLen = buf.readUInt16LE(lho + 26), lhExtraLen = buf.readUInt16LE(lho + 28)
    const start = lho + 30 + lhNameLen + lhExtraLen; const raw = buf.subarray(start, start + csize)
    files.set(name, method === 0 ? raw : zlib.inflateRawSync(raw))
    p += 46 + nameLen + extraLen + commentLen
  }
  return files
}
const dec = (s) => s.replace(/&#x([0-9a-fA-F]+);/g, (_, h) => String.fromCodePoint(parseInt(h, 16))).replace(/&#(\d+);/g, (_, d) => String.fromCodePoint(+d)).replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&apos;/g, "'").replace(/&amp;/g, '&')

const bytes = fs.readFileSync(path.join(root, SOURCE))
const sha256 = crypto.createHash('sha256').update(bytes).digest('hex')
if (sha256 !== SHA256) fail(`The IO map changed (sha256 ${sha256}). A change of the workbook goes through a Change Request; update the hash here and in PLC_IO_Map/README.md only then.`)
const files = readZip(bytes)
const text = (n) => files.get(n)?.toString('utf8') ?? ''
const shared = [...text('xl/sharedStrings.xml').matchAll(/<si>([\s\S]*?)<\/si>/g)].map((m) => dec([...m[1].matchAll(/<t[^>]*>([\s\S]*?)<\/t>/g)].map((x) => x[1]).join('')))
const rels = text('xl/_rels/workbook.xml.rels')
const sheet = [...text('xl/workbook.xml').matchAll(/<sheet\b[^>]*?name="([^"]*)"[^>]*?r:id="([^"]*)"[^>]*?\/>/g)].find((m) => dec(m[1]) === 'IO Matrix') ?? fail('sheet "IO Matrix" is missing')
const target = rels.match(new RegExp(`<Relationship\\b[^>]*?Id="${sheet[2]}"[^>]*?Target="([^"]*)"`))?.[1] ?? rels.match(new RegExp(`<Relationship\\b[^>]*?Target="([^"]*)"[^>]*?Id="${sheet[2]}"`))[1]
const xml = text(target.startsWith('/') ? target.slice(1) : 'xl/' + target)

const cells = new Map()   // "A3" -> text
for (const c of xml.matchAll(/<c\b([^>]*?)(?:\/>|>([\s\S]*?)<\/c>)/g)) {
  const ref = c[1].match(/r="([A-Z]+\d+)"/)?.[1]; if (!ref) continue
  const type = c[1].match(/t="([^"]*)"/)?.[1]; const body = c[2] ?? ''
  let v = body.match(/<v>([\s\S]*?)<\/v>/)?.[1]
  if (type === 's' && v !== undefined) v = shared[+v]
  else if (type === 'inlineStr') v = dec([...body.matchAll(/<t[^>]*>([\s\S]*?)<\/t>/g)].map((x) => x[1]).join(''))
  else if (v !== undefined) v = dec(v)
  if (v !== undefined && String(v).trim() !== '') cells.set(ref, String(v).trim().normalize("NFC"))
}
// merged cells: every cell of a range reads the value of its first cell; the range is remembered (a merged address = one register shared by those rows)
const merged = new Map()
for (const m of xml.matchAll(/<mergeCell ref="([A-Z]+)(\d+):([A-Z]+)(\d+)"/g)) {
  const [, c1, r1, c2, r2] = m
  if (c1 !== c2) continue
  for (let r = +r1; r <= +r2; r++) merged.set(`${c1}${r}`, { first: `${c1}${r1}`, from: +r1, to: +r2 })
}
const at = (col, row) => cells.get(merged.get(`${col}${row}`)?.first ?? `${col}${row}`)

const header = ['PLC', 'Station', 'Nhóm', 'Tên tín hiệu', 'Chiều tín hiệu']
header.forEach((h, i) => { if ((at('ABCDE'[i], 2) ?? '') !== h) fail(`unexpected header in column ${'ABCDE'[i]}: ${at('ABCDE'[i], 2)}`) })
const directionOf = (t) => t === 'PLC → Software' ? 'PlcToSoftware' : t === 'Phần mềm → PLC' ? 'SoftwareToPlc' : fail('unknown direction ' + t)
const typeOf = (t) => ({ BOOL: 'Bool', INT: 'Int' })[t] ?? fail('unknown data type ' + t)

const plcs = [], rows = [], plcSignals = []
for (let r = 3; r <= 200; r++) {
  const group = at('C', r); if (!group) continue
  const plcCell = at('A', r) ?? fail(`row ${r}: no PLC`)
  const [label, ip] = plcCell.split(/\r?\n/).map((s) => s.trim())
  let plc = plcs.find((p) => p.ipAddress === ip)
  if (!plc) { plc = { plcId: `PLC-0${plcs.length + 1}`, documentLabel: label, ipAddress: ip, stations: [] }; plcs.push(plc) }
  const address = at('H', r) ?? fail(`row ${r}: no address`)
  if (!/^[DM]\d+$/.test(address)) fail(`row ${r}: unexpected address ${address}`)
  if (group === 'Command') {
    const stationId = at('B', r)
    if (!plc.stations.includes(stationId)) plc.stations.push(stationId)
    const share = merged.get(`H${r}`)
    rows.push({ row: r, plcId: plc.plcId, stationId, signalName: at('D', r), direction: directionOf(at('E', r)), dataType: typeOf(at('I', r)), values: at('J', r) ?? '', meaning: at('K', r) ?? '', address,
      shareKey: share && share.to > share.from ? `${plc.plcId}:${share.first}` : null })
  } else if (group === 'Status') {
    plcSignals.push({ plcId: plc.plcId, signalName: at('D', r), direction: directionOf(at('E', r)), dataType: typeOf(at('I', r)), address, values: at('J', r) ?? '', meaning: at('K', r) ?? '', spare: false })
  } else if (group === 'Spair') {
    plcSignals.push({ plcId: plc.plcId, signalName: 'Spare', direction: null, dataType: typeOf(at('I', r)), address, values: '', meaning: 'dự phòng', spare: true })
  } else fail(`row ${r}: unknown group ${group}`)
}

// ---- consistency checks (fail loudly instead of guessing) ----
if (plcs.length !== 4) fail('expected 4 PLCs, got ' + plcs.length)
for (const p of plcs) if (p.stations.length !== 4) fail(`${p.plcId} must have 4 stations`)
const stations = plcs.flatMap((p) => p.stations.map((s) => ({ stationId: s, plcId: p.plcId }))).map((s, i) => ({ ...s, order: i + 1 }))
if (new Set(stations.map((s) => s.stationId)).size !== 16) fail('station ids must be 16 unique names')
const byStation = (id) => rows.filter((x) => x.stationId === id)
const first = byStation(stations[0].stationId)
if (first.length !== 6) fail('expected 6 signals per station, got ' + first.length)
for (const s of stations) {
  const own = byStation(s.stationId)
  if (own.length !== 6) fail(`station ${s.stationId} must have 6 signals`)
  own.forEach((x, i) => { const t = first[i]; for (const k of ['signalName', 'direction', 'dataType', 'values', 'meaning']) if (x[k] !== t[k]) fail(`station ${s.stationId} differs from the common signal set at ${x.signalName} (${k})`) })
}
for (const p of plcs) if (plcSignals.filter((x) => x.plcId === p.plcId).length !== 6) fail(`${p.plcId} must have the heartbeat and 5 spare rows`)

const sharedWith = (x) => x.shareKey ? first.map((t) => t.signalName).filter((n) => n !== x.signalName && byStation(x.stationId).some((y) => y.signalName === n && y.shareKey === x.shareKey)) : []
const catalog = {
  schemaVersion: 2,
  source: { file: SOURCE, sha256 },
  note: 'Derived from the only IO map of the project (22/11/2026). Addresses are references written in the workbook, not configured addresses. The IO contract is NOT confirmed by the PLC engineer.',
  confirmation: 'NotConfirmed',
  plcs,
  stations: stations.map((s) => ({ ...s, addresses: Object.fromEntries(byStation(s.stationId).map((x) => [x.signalName, x.address])) })),
  template: first.map((x, i) => ({ seq: i + 1, group: 'Command', signalName: x.signalName, direction: x.direction, dataType: x.dataType, templateAddressReference: '',
    description: `${x.meaning} (${x.values})`, values: x.values, sharedRegisterWith: sharedWith(x) })),
  plcSignals,
  handshake: [],
}
const json = JSON.stringify(catalog, null, 2) + '\n'
if (process.argv.includes('--check')) {
  const current = fs.existsSync(outFile) ? fs.readFileSync(outFile, 'utf8') : ''
  if (current.replace(/\r\n/g, '\n') !== json) fail('plc-io-catalog.json is out of date; run the generator')
  console.log('catalog is up to date'); process.exit(0)
}
fs.writeFileSync(outFile, json)
console.log(`written ${path.relative(root, outFile)}: ${plcs.length} PLC, ${stations.length} stations, ${first.length} signals/station = ${stations.length * first.length} station points, ${plcSignals.length} PLC-level rows`)
