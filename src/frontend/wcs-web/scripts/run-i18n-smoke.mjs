// Bundles scripts/i18n-smoke.ts with rolldown (already a dependency of Vite) and runs it under Node: no DOM, no test framework needed.
import { build } from 'rolldown'
import { spawnSync } from 'node:child_process'
import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const here = path.dirname(fileURLToPath(import.meta.url))
const out = fs.mkdtempSync(path.join(os.tmpdir(), 'wcs-i18n-'))
try {
  await build({ input: path.join(here, process.argv[2] ?? 'i18n-smoke.ts'), platform: 'node', output: { dir: out, format: 'esm' }, logLevel: 'silent' })
  const r = spawnSync(process.execPath, [path.join(out, (process.argv[2] ?? 'i18n-smoke.ts').replace(/.ts$/, '.js'))], { encoding: 'utf8' })
  process.stdout.write(r.stdout); process.stderr.write(r.stderr)
  const lines = r.stdout.split('\n')
  const failed = lines.filter((l) => l.startsWith('FAIL'))
  console.log(`tests ${lines.filter((l) => l.startsWith('PASS')).length} passed, ${failed.length} failed`)
  process.exitCode = r.status || failed.length ? 1 : 0
} finally { fs.rmSync(out, { recursive: true, force: true }) }
