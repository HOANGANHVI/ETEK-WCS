import { useCallback } from 'react'
import { api } from '../api/client'
import { useApi } from '../hooks/useApi'
import { QueueAlertList } from './QueueAlertList'

export function QueueAlerts() {
  const data = useApi(useCallback(() => api.queueAlerts(), []), ['OrderChanged'])
  return <QueueAlertList alerts={data.data ?? []} />
}
