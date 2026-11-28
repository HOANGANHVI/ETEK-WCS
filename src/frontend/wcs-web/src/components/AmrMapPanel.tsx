import { useCallback } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import { hasRole, useAuth } from '../auth/AuthContext'
import { useFleet } from '../fleet/fleetState'
import { useApi } from '../hooks/useApi'
import { useT } from '../i18n'
import { AmrMap } from './AmrMap'
import { Loading, Panel } from './ui'
import '../styles/amrmap.css'

/** CR-065: "Monitor AMR": the imported map with the AMRs of the shared fleet data (the same polling as the rest of the page; nothing else is queried from the RCS). */
export function AmrMapPanel() {
  const t = useT()
  const { user } = useAuth()
  const fleet = useFleet()
  const map = useApi(useCallback(() => api.rcsMap(), []))
  const subtitle = map.data ? `${map.data.info.mapName} · ${map.data.info.mapId}` : undefined
  return (
    <Panel title={t('agv.mapPanel')} subtitle={subtitle} className="mb">
      {map.error && !map.data ? <div className="notice notice-bad" role="alert">{map.error}</div>
        : map.loading && !map.data ? <Loading />
        : !map.data ? (
          <div className="state state-empty"><div><strong>{t('agv.mapNone')}</strong><span>{t('agv.mapNoneHint')}</span>
            {hasRole(user, 'Admin') && <Link to="/config?tab=map">{t('agv.mapGoConfig')}</Link>}</div></div>
        ) : (
          <>
            {!map.data.info.confirmedAt && <div className="notice notice-warn" role="status">{t('agv.mapUnconfirmed')}</div>}
            {fleet.data?.simulatedData && <div className="notice notice-warn" role="status">{t('agv.mapSimulated')}</div>}
            {fleet.data && (fleet.data.connection !== 'Connected' || !!fleet.error) && <div className="notice notice-bad" role="alert">{t('agv.mapNoLink')}</div>}
            {fleet.data ? <AmrMap map={map.data} fleet={fleet.data} loadFailed={!!fleet.error} canEdit={hasRole(user, 'Admin')} onChanged={map.reload} /> : <div className="notice notice-bad" role="alert">{fleet.error ?? t('agv.fleetNoData')}</div>}
          </>
        )}
    </Panel>
  )
}
