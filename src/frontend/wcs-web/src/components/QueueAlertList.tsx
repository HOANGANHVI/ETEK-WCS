import { Link } from 'react-router-dom'
import type { QueueAlert } from '../api/routingTypes'
import { useT } from '../i18n'

/** CR-042 (OQ-42-04): the server says which queue scopes have a head that waited too long; the page only shows it. */
export function QueueAlertList({ alerts }: { alerts: QueueAlert[] }) {
  const t = useT()
  if (alerts.length === 0) return null
  return (
    <div className="notice notice-warn" role="alert" data-queue-alerts={alerts.length}>
      <strong>{t('linked.alertTitle')}</strong>
      <ul>
        {alerts.map((a) => (
          <li key={a.orderId}>
            <Link to={`/routing?order=${a.orderId}`}>{t('linked.alertRow', { key: a.requestKey, process: a.processCode, minutes: a.minutes, scope: a.scope ?? t('linked.scopeNone'), behind: a.behind })}</Link>
            {a.reason && <div className="muted small">{a.reason}</div>}
          </li>
        ))}
      </ul>
      <p className="muted small">{t('linked.alertNote')}</p>
    </div>
  )
}
