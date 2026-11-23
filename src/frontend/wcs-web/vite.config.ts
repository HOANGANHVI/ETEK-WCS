import { execSync } from 'node:child_process'
import { existsSync, readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import react from '@vitejs/plugin-react'
import { defineConfig, loadEnv } from 'vite'

/** Release metadata embedded in the bundle at build time. Version: build argument WCS_APP_VERSION (Docker), else the VERSION file at the repository root (single source). Commit / time: passed by deploy.ps1, else git / now (local builds). */
function releaseInfo() {
  const fromFile = [resolve(process.cwd(), '../../../VERSION'), '/VERSION'].find((p) => existsSync(p))
  const version = process.env.WCS_APP_VERSION?.trim() || (fromFile ? readFileSync(fromFile, 'utf8').trim() : '') || 'unknown'
  let commit = process.env.WCS_GIT_COMMIT?.trim() || ''
  if (!commit) { try { commit = execSync('git rev-parse --short HEAD', { stdio: ['ignore', 'pipe', 'ignore'] }).toString().trim() } catch { /* no git: unknown */ } }
  const buildTime = process.env.WCS_BUILD_TIME?.trim() || new Date().toISOString().replace(/\.\d{3}Z$/, 'Z')
  return { version, commit: commit || 'unknown', buildTime }
}

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  const target = env.VITE_DEV_API_TARGET || 'http://localhost:5007'
  const rel = releaseInfo()
  return {
    plugins: [react()],
    define: {
      __APP_VERSION__: JSON.stringify(rel.version),
      __GIT_COMMIT__: JSON.stringify(rel.commit),
      __BUILD_TIME__: JSON.stringify(rel.buildTime),
    },
    server: {
      proxy: {
        '/api': { target, changeOrigin: true },
        '/hubs': { target, changeOrigin: true, ws: true },
      },
    },
  }
})
