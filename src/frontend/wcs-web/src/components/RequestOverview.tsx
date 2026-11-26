import { useCallback } from 'react'
import { api } from '../api/client'
import { useApi } from '../hooks/useApi'
import { useT } from '../i18n'
import { requestRows } from '../lib/requestRows'
import { RequestTable } from './RequestTable'
import { QueueAlerts } from './QueueAlerts'
import { Async, Panel } from './ui'

const EVENTS = ['TaskCreated', 'TaskStateChanged', 'TaskDeleted', 'OrderChanged'] as const

/**
 * CR-040: the same list of requests on Task management, RCS operations and Multi-leg dispatch: legacy tasks and transport orders together, identified by their request key,
 * each linking to the page that owns its detail. It only reads what the server returns; it creates nothing.
 */
export function RequestOverview() {
  const t = useT()
  const data = useApi(useCallback(async () => {
    const [tasks, orders] = await Promise.all([api.tasks(undefined, 0, 50), api.routingOrders(undefined, 50)])
    return requestRows(tasks.items, orders.items)
  }, []), [...EVENTS])
  return (
    <Panel title={t('linked.overviewTitle')} subtitle={t('linked.overviewSub')} flush className="mt">
      <QueueAlerts />
      <Async state={data} isEmpty={(d) => d.length === 0} empty={{ title: t('linked.overviewNone') }}>
        {(rows) => <RequestTable rows={rows} />}
      </Async>
    </Panel>
  )
}
