import { useEffect, useState } from 'react'
import { BUILD } from '../buildInfo'
import { useT } from '../i18n'

/**
 * Application version and git short hash of the running build, shown once at the bottom of the sidebar (it belongs to the application, not to a page).
 * The tooltip adds the build time and the BACKEND build; a warning mark shows when the backend commit differs from this web bundle. In a collapsed sidebar only a small mark remains (the tooltip is the same).
 */
export function VersionBadge() {
  const t = useT()
  const [server, setServer] = useState<{ version: string; gitCommit: string; buildTime: string } | null>(null)
  useEffect(() => {
    let live = true
    fetch('/api/version').then((r) => (r.ok ? r.json() : null)).then((j) => { if (live && j) setServer(j) }).catch(() => { /* the footer still shows the web build */ })
    return () => { live = false }
  }, [])
  const mismatch = !!server && server.gitCommit !== 'unknown' && BUILD.gitCommit !== 'unknown' && server.gitCommit !== BUILD.gitCommit
  const title = [
    t('about.web', { version: BUILD.version, commit: BUILD.gitCommit, time: BUILD.buildTime }),
    server ? t('about.api', { version: server.version, commit: server.gitCommit, time: server.buildTime }) : '',
    mismatch ? t('about.mismatch') : '',
  ].filter(Boolean).join('\n')
  return (
    <div className="version-badge" title={title}>
      <span className="vb-full mono">v{BUILD.version} · {BUILD.gitCommit}</span>
      <span className="vb-mini" aria-hidden="true">v</span>
      {mismatch && <span className="version-warn" role="status" aria-label={t('about.mismatchShort')}> ⚠</span>}
    </div>
  )
}
