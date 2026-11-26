// RETIRED INPUT (CR-045, 2026-11-26): the project's only IO map is "AGV_PLC Interface_List_22_11_2026.xlsx". The workbook this script reads (IO Map v2) is no longer in the repository
// (Git history: commit 34c45b2). The catalog it produced stays in use unchanged until the new IO map is implemented under an approved change; do not run this script to create a new one.
// Generates src/backend/WCS.Infrastructure/Plc/Catalog/plc-io-catalog.json from the controlled IO workbook (CR-002).
// The workbook stays the source of truth; this file is a derived, versioned copy that the seeder loads. No value is invented:
// Actual_PLC_Address stays empty, port / MC parameters stay empty, handshake rows stay PENDING.
//
//   node scripts/generate-plc-io-catalog.mjs            (writes the JSON)
//   node scripts/generate-plc-io-catalog.mjs --check    (fails if the committed JSON is out of date)
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import zlib from 'node:zlib'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const xlsx = path.join(root, 'docs/02_Interface_Specification/PLC_IO_Map/ETEK_WCS_IO_Map_4PLC_16Stations_v2_Separate_IO_Sets.xlsx')
const outFile = path.join(root, 'src/backend/WCS.Infrastructure/Plc/Catalog/plc-io-catalog.json')
if (!fs.existsSync(xlsx)) {
  console.error('The input workbook (IO Map v2) is retired and not in the repository; the committed catalog is left unchanged. See docs/02_Interface_Specification/PLC_IO_Map/README.md.')
  process.exit(2)
}

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
const text = (files, n) => files.get(n)?.toString('utf8') ?? ''

function readWorkbook(file) {
  const buf = fs.readFileSync(file)
  const files = readZip(buf)
  const shared = [...text(files, 'xl/sharedStrings.xml').matchAll(/<si>([\s\S]*?)<\/si>/g)].map((m) => dec([...m[1].matchAll(/<t[^>]*>([\s\S]*?)<\/t>/g)].map((x) => x[1]).join('')))
  const wb = text(files, 'xl/workbook.xml'); const rels = text(files, 'xl/_rels/workbook.xml.rels')
  const sheets = {}
  for (const m of wb.matchAll(/<sheet\b[^>]*?name="([^"]*)"[^>]*?r:id="([^"]*)"[^>]*?\/>/g)) {
    const target = rels.match(new RegExp(`<Relationship\\b[^>]*?Id="${m[2]}"[^>]*?Target="([^"]*)"`))?.[1] ?? rels.match(new RegExp(`<Relationship\\b[^>]*?Target="([^"]*)"[^>]*?Id="${m[2]}"`))?.[1]
    const xml = text(files, target.startsWith('/') ? target.slice(1) : 'xl/' + target)
    const rows = []
    for (const r of xml.matchAll(/<row\b[^>]*>([\s\S]*?)<\/row>/g)) {
      const cells = {}
      for (const c of r[1].matchAll(/<c\b([^>]*?)(?:\/>|>([\s\S]*?)<\/c>)/g)) {
        const ref = c[1].match(/r="([A-Z]+)\d+"/)?.[1]; if (!ref) continue
        const type = c[1].match(/t="([^"]*)"/)?.[1]; const body = c[2] ?? ''
        let v = body.match(/<v>([\s\S]*?)<\/v>/)?.[1]
        if (type === 's' && v !== undefined) v = shared[+v]
        else if (type === 'inlineStr') v = dec([...body.matchAll(/<t[^>]*>([\s\S]*?)<\/t>/g)].map((x) => x[1]).join(''))
        else if (v !== undefined) v = dec(v)
        if (v !== undefined && v !== '') cells[ref] = String(v)
      }
      if (Object.keys(cells).length) rows.push(cells)
    }
    sheets[dec(m[1])] = rows
  }
  return { sheets, sha256: crypto.createHash('sha256').update(buf).digest('hex') }
}

const { sheets, sha256 } = readWorkbook(xlsx)
const need = (n) => sheets[n] ?? (() => { throw new Error('missing sheet ' + n) })()
const directionOf = (t) => t === 'PLC → Software' ? 'PlcToSoftware' : t === 'Phần mềm → PLC' ? 'SoftwareToPlc' : (() => { throw new Error('unknown direction ' + t) })()
const typeOf = (t) => ({ BOOL: 'Bool', INT: 'Int', STRING: 'String' })[t] ?? (() => { throw new Error('unknown data type ' + t) })()

// PLC configuration (IP, station list). Port / protocol parameters are deliberately NOT carried over (NEEDS_CONFIRMATION).
const plcs = need('PLC Configuration').slice(1).map((r) => ({
  plcId: r.A, ipAddress: r.B, stations: r.D.split(',').map((s) => s.trim()), connectionCount: +r.J,
}))
const mapping = need('Station Mapping').slice(1).map((r) => ({ stationId: r.A, plcId: r.B, plcIp: r.C, ioSet: r.D }))
const sheetByPlc = { 'PLC-01': 'PLC1_IO', 'PLC-02': 'PLC2_IO', 'PLC-03': 'PLC3_IO', 'PLC-04': 'PLC4_IO' }

const pointsByStation = new Map()
for (const [plcId, sheet] of Object.entries(sheetByPlc)) {
  for (const r of need(sheet)) {
    if (!r.A || !r.D || r.A === 'Station_ID' || !/^\d+$/.test(r.B ?? '')) continue
    const key = `${plcId}/${r.A}`
    if (!pointsByStation.has(key)) pointsByStation.set(key, [])
    pointsByStation.get(key).push({
      seq: +r.B, group: r.C, signalName: r.D, direction: directionOf(r.E), source: r.F, destination: r.G,
      templateAddressReference: r.H, dataType: typeOf(r.I), description: (r.K ?? '').trim(),
      actualAddress: r.J ?? null,
    })
  }
}

// ---- consistency checks against the approved architecture (fail loudly instead of guessing) ----
if (plcs.length !== 4) throw new Error('expected 4 PLCs, got ' + plcs.length)
if (mapping.length !== 16) throw new Error('expected 16 stations, got ' + mapping.length)
for (const p of plcs) {
  const listed = mapping.filter((m) => m.plcId === p.plcId).map((m) => m.stationId)
  if (JSON.stringify(listed) !== JSON.stringify(p.stations)) throw new Error(`station list mismatch for ${p.plcId}`)
  for (const m of mapping.filter((x) => x.plcId === p.plcId)) if (m.plcIp !== p.ipAddress) throw new Error(`IP mismatch ${m.stationId}`)
}
const first = pointsByStation.get('PLC-01/A1')
if (first.length !== 23) throw new Error('template must have 23 signals, got ' + first.length)
for (const m of mapping) {
  const pts = pointsByStation.get(`${m.plcId}/${m.stationId}`)
  if (!pts || pts.length !== 23) throw new Error(`station ${m.stationId} must have 23 signals`)
  pts.forEach((p, i) => {
    const t = first[i]
    if (p.signalName !== t.signalName || p.dataType !== t.dataType || p.direction !== t.direction || p.templateAddressReference !== t.templateAddressReference || p.group !== t.group)
      throw new Error(`station ${m.stationId} differs from the common template at signal ${i + 1}`)
    if (p.actualAddress) throw new Error(`workbook already contains an actual address for ${m.stationId}/${p.signalName}: review before importing`)
  })
}

const handshake = need('Handshake Definition').slice(1).map((r) => ({
  stationId: r.A, signalName: r.B, owner: r.J ?? null, approvalStatus: r.K ?? null,
  trigger: r.C ?? null, wcsAction: r.D ?? null, plcAction: r.E ?? null, setCondition: r.F ?? null, resetCondition: r.G ?? null, timeout: r.H ?? null, retryPolicy: r.I ?? null,
}))

const catalog = {
  schemaVersion: 1,
  source: { file: 'docs/02_Interface_Specification/PLC_IO_Map/ETEK_WCS_IO_Map_4PLC_16Stations_v2_Separate_IO_Sets.xlsx', sha256 },
  note: 'Derived from the controlled workbook. Actual_PLC_Address, TCP port and MC Protocol parameters are intentionally absent (NEEDS_CONFIGURATION).',
  plcs: plcs.map((p) => ({ plcId: p.plcId, ipAddress: p.ipAddress, stations: p.stations })),
  stations: mapping.map((m, i) => ({ stationId: m.stationId, plcId: m.plcId, order: i + 1 })),
  template: first.map(({ seq, group, signalName, direction, source, destination, templateAddressReference, dataType, description }) =>
    ({ seq, group, signalName, direction, source, destination, templateAddressReference, dataType, description })),
  handshake,
}
const json = JSON.stringify(catalog, null, 2) + '\n'
if (process.argv.includes('--check')) {
  const current = fs.existsSync(outFile) ? fs.readFileSync(outFile, 'utf8') : ''
  if (current.replace(/\r\n/g, '\n') !== json) { console.error('plc-io-catalog.json is out of date; run the generator'); process.exit(1) }
  console.log('catalog is up to date'); process.exit(0)
}
fs.mkdirSync(path.dirname(outFile), { recursive: true })
fs.writeFileSync(outFile, json)
console.log(`written ${path.relative(root, outFile)}: ${catalog.plcs.length} PLC, ${catalog.stations.length} stations, ${catalog.template.length} signals/station = ${catalog.stations.length * catalog.template.length} IO points, ${handshake.length} handshake rows, sha256 ${sha256.slice(0, 12)}…`)
