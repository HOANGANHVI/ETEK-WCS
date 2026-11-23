import { useCallback, useState } from 'react'
import { api } from '../api/client'
import { Async, EmptyState, PageHeader } from '../components/ui'
import { DEMO_MODE } from '../config'
import { useApi } from '../hooks/useApi'
import { useT } from '../i18n'
import { Callbacks } from './rcsdiag/Callbacks'
import { History } from './rcsdiag/History'
import { Overview } from './rcsdiag/Overview'
import { Params } from './rcsdiag/Params'
import { Tester } from './rcsdiag/Tester'

type Tab = 'tester' | 'params' | 'history' | 'callbacks'

/**
 * CR-032 – RCS Diagnostics & Manual API Tester (Admin only; the server enforces it on every call, hiding the menu is only a convenience).
 * Opening this page calls nothing on the RCS. Diagnostics are not operations: results here never change inventory, racks, transport tasks or the AMR monitor.
 */
export function RcsDiagnosticsPage() {
  const t = useT()
  if (DEMO_MODE) {
    return (
      <>
        <PageHeader title={t('rcsDiag.title')} />
        <EmptyState title={t('rcsDiag.demoTitle')} hint={t('rcsDiag.demoHint')} />
      </>
    )
  }
  return <Screen />
}

function Screen() {
  const t = useT()
  const [tab, setTab] = useState<Tab>('tester')
  const [refreshKey, setRefreshKey] = useState(0)
  const catalog = useApi(useCallback(() => api.rcsDiagCatalog(), []))
  const params = useApi(useCallback(() => api.rcsDiagParameters(), []))
  const bump = useCallback(() => setRefreshKey((k) => k + 1), [])

  return (
    <>
      <PageHeader title={t('rcsDiag.title')} description={t('rcsDiag.description')} />
      <div className="stack">
        <Overview onChecked={bump} />
        <div className="tabs" role="tablist">
          {(['tester', 'params', 'history', 'callbacks'] as const).map((k) => (
            <button key={k} type="button" role="tab" aria-selected={tab === k} className={`tab ${tab === k ? 'active' : ''}`} onClick={() => setTab(k)}>{t(`rcsDiag.tab_${k}` as const)}</button>
          ))}
        </div>
        <Async state={catalog} empty={{ title: t('rcsDiag.catalogEmpty') }} isEmpty={(c) => c.apis.length === 0}>
          {(c) => (
            <>
              {tab === 'tester' && <Tester catalog={c} params={params.data} onExecuted={bump} />}
              {tab === 'params' && (params.data ? <Params params={params.data} onSaved={params.reload} /> : <p className="muted pad">{t('rcsDiag.paramsLoading')}</p>)}
              {tab === 'history' && <History apis={c.apis} refreshKey={refreshKey} />}
              {tab === 'callbacks' && <Callbacks />}
            </>
          )}
        </Async>
      </div>
    </>
  )
}
