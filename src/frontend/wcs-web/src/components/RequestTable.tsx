import { Link } from 'react-router-dom'
import type { Order } from '../api/routingTypes'
import type { TransportTask } from '../api/types'
import { useT } from '../i18n'
import { formatDateTime } from '../i18n/format'
import { flowInfo, TASK_STATE_VI, taskCode } from '../i18n/vi'
import { ORDER_STATE_LABEL } from '../i18n/routing'
import type { requestRows } from '../lib/requestRows'

export function RequestTable({ rows }: { rows: ReturnType<typeof requestRows> }) {
  const t = useT()
  const state = (r: (typeof rows)[number]) => r.kind === 'task' ? TASK_STATE_VI[(r.source as TransportTask).state] : ORDER_STATE_LABEL[(r.source as Order).state] ?? r.state
  return (
    <div className="table-wrap capped">
      <table className="grid">
        <thead><tr><th>{t('linked.colKey')}</th><th>{t('linked.colKind')}</th><th>{t('linked.colProcess')}</th><th>{t('linked.colState')}</th><th>{t('linked.colQueue')}</th><th>{t('linked.colLegs')}</th><th>{t('linked.colRobot')}</th><th>{t('linked.colCreated')}</th><th>{t('linked.colFollow')}</th></tr></thead>
        <tbody>
          {rows.map((r) => (
            <tr key={r.id} data-request={r.requestKey}>
              <td className="mono"><Link to={r.link}>{r.requestKey}</Link></td>
              <td>{t(r.kind === 'task' ? 'linked.kindTask' : 'linked.kindOrder')}{r.kind === 'task' ? ` ${taskCode(r.id)}` : ''}</td>
              <td>{r.kind === 'task' ? flowInfo(r.process).code : r.process}</td>
              <td>{state(r)}</td>
              <td>{r.queue ? t('linked.queueCell', { status: t(`linked.queue_${r.queue.status}` as never), position: r.queue.position, scope: r.queue.scope ?? t('linked.scopeNone') }) : '—'}</td>
              <td className="num">{r.legs ?? '—'}</td>
              <td className="mono">{r.robots || '—'}</td>
              <td className="nowrap">{formatDateTime(r.createdAt)}</td>
              <td className="nowrap"><Link to={r.kind === 'task' ? `/operations?task=${r.id}` : r.link}>{t(r.kind === 'task' ? 'linked.followTask' : 'linked.followOrder')}</Link></td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
