import { useSearchParams } from 'react-router-dom'
import { PageHeader } from '../components/ui'
import { useT, type MsgKey } from '../i18n'
import { OperationsTab } from './config/OperationsTab'
import { RcsTab } from './config/RcsTab'

const TABS: { key: 'rcs' | 'ops'; label: MsgKey }[] = [
  { key: 'rcs', label: 'config.tabRcs' },
  { key: 'ops', label: 'config.tabOps' },
]

export function ConfigPage() {
  const t = useT()
  const [params, setParams] = useSearchParams()
  const tab = TABS.find((x) => x.key === params.get('tab'))?.key ?? 'rcs'
  return (
    <>
      <PageHeader title={t('config.title')} />
      <div className="tabs" role="tablist" aria-label={t('config.tabsLabel')}>
        {TABS.map((x) => (
          <button key={x.key} type="button" role="tab" id={`tab-${x.key}`} aria-selected={tab === x.key} aria-controls={`panel-${x.key}`}
            className={tab === x.key ? 'tab active' : 'tab'} onClick={() => setParams({ tab: x.key })}>{t(x.label)}</button>
        ))}
      </div>
      <div role="tabpanel" id={`panel-${tab}`} aria-labelledby={`tab-${tab}`}>
        {tab === 'rcs' && <RcsTab />}
        {tab === 'ops' && <OperationsTab />}
      </div>
    </>
  )
}
