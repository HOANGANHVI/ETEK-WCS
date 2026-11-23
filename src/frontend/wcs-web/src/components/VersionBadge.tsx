import { useEffect, useState } from 'react'
import { BUILD } from '../buildInfo'
import { useT } from '../i18n'

/** Application version and git short hash of the running build (footer). The hover text adds the build time and the BACKEND build; if the backend commit differs from this web bundle, a warning mark shows. */
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
      {t('about.version')} <span className="mono">{BUILD.version}</span> · {t('about.commit')} <span className="mono">{BUILD.gitCommit}</span>
      {mismatch && <span className="version-warn" role="status"> · {t('about.mismatchShort')}</span>}
    </div>
  )
}
