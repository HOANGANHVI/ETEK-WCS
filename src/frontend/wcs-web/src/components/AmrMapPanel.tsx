import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import { hasRole, useAuth } from '../auth/AuthContext'
import { useFleet } from '../fleet/fleetState'
import { useApi } from '../hooks/useApi'
import { useT } from '../i18n'
import { isOpenRun, rackSpots } from '../lib/simMap'
import { AmrMap } from './AmrMap'
import { SimPanel } from './SimPanel'
import { Loading, Panel } from './ui'
import '../styles/amrmap.css'

const FAST = 400, SLOW = 3000

/** CR-065: "Monitor AMR": the imported map with the AMRs of the shared fleet data (the same polling as the rest of the page; nothing else is queried from the RCS).
 * CR-069: while the AMR simulation runs, its AMRs, racks and routes are drawn from the simulator and the WCS inventory. */
export function AmrMapPanel() {
  const t = useT()
  const { user } = useAuth()
  const isAdmin = hasRole(user, 'Admin')
  const fleet = useFleet()
  const map = useApi(useCallback(() => api.rcsMap(), []))
  // CR-068: another import (by this or another user) shows without reopening the page
  const { reload } = map
  useEffect(() => {
    const timer = setInterval(reload, 20_000)
    window.addEventListener('focus', reload)
    return () => { clearInterval(timer); window.removeEventListener('focus', reload) }
  }, [reload])
  const live = useApi(useCallback(() => api.amrLive(), []))
  const racks = useApi(useCallback(() => api.racks(), []), ['InventoryChanged', 'TaskStateChanged'])
  const locations = useApi(useCallback(() => api.locations(), []), ['InventoryChanged'])
  const active = !!live.data?.active
  const { reload: reloadLive } = live
  const { reload: reloadRacks } = racks
  useEffect(() => { const timer = setInterval(reloadLive, active ? FAST : SLOW); return () => clearInterval(timer) }, [active, reloadLive])
  // a pick or a drop changes the inventory: read it again as soon as the simulator reports it
  const handled = (live.data?.runs ?? []).map((r) => `${r.requestCode}:${r.carrying}:${r.phase === 'Completed'}`).join()
  useEffect(() => { reloadRacks() }, [handled, reloadRacks])
  const [chosen, setChosen] = useState<string | null>(null)
  const selected = chosen ?? live.data?.runs.find(isOpenRun)?.requestCode ?? null
  const subtitle = map.data ? `${map.data.info.mapName} · ${map.data.info.mapId}` : undefined
  const spots = map.data ? rackSpots(map.data, locations.data ?? [], racks.data ?? []) : []
  return (
    <Panel title={t('agv.mapPanel')} subtitle={subtitle} className="mb">
      {map.error && !map.data ? <div className="notice notice-bad" role="alert">{map.error}</div>
        : map.loading && !map.data ? <Loading />
        : !map.data ? (
          <div className="state state-empty"><div><strong>{t('agv.mapNone')}</strong><span>{t('agv.mapNoneHint')}</span>
            {isAdmin && <Link to="/config?tab=map">{t('agv.mapGoConfig')}</Link>}</div></div>
        ) : (
          <>
            {!map.data.info.confirmedAt && <div className="notice notice-warn" role="status">{t('agv.mapUnconfirmed')}</div>}
            {fleet.data?.simulatedData && <div className="notice notice-warn" role="status">{t('agv.mapSimulated')}</div>}
            {fleet.data && (fleet.data.connection !== 'Connected' || !!fleet.error) && <div className="notice notice-bad" role="alert">{t('agv.mapNoLink')}</div>}
            {fleet.data ? <AmrMap map={map.data} fleet={fleet.data} loadFailed={!!fleet.error} canEdit={isAdmin} onChanged={map.reload} live={live.data} spots={spots}
              onSimSelect={setChosen} selectedRun={selected} /> : <div className="notice notice-bad" role="alert">{fleet.error ?? t('agv.fleetNoData')}</div>}
          </>
        )}
      {live.data && fleet.data?.source !== 'Real' && <SimPanel live={live.data} isAdmin={isAdmin} selected={selected} onSelect={(r) => setChosen(r.requestCode)} onChanged={live.reload} />}
    </Panel>
  )
}
