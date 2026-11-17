import { useCallback } from 'react'
import { api } from '../../api/client'
import type { HandshakeState } from '../../api/plcTypes'
import { Async, Button, Panel, Pill } from '../../components/ui'
import { useApi } from '../../hooks/useApi'
import { useT } from '../../i18n'
import { APPROVAL_VI, DATATYPE_VI, DIRECTION_VI, localizeDeviceMessage, PHASE_TONE, PHASE_VI } from '../../i18n/plc'
import { describeCoded } from '../../i18n/logCatalog'
import { fmtTime } from './plcFormat'
import { QualityPill, ValueCell } from './plcUi'

interface Props { stationId: string; isAdmin: boolean; handshake: HandshakeState | undefined; onClose: () => void }

/** CR-004: the 23 IO of one station, strictly READ-ONLY. Addresses are edited in the IO-mapping screen, connections in the PLC-connection screen. */
export function StationPanel({ stationId, isAdmin, handshake, onClose }: Props) {
  const t = useT()
  const io = useApi(useCallback(() => api.plcStationIo(stationId), [stationId]), ['StationIoChanged', 'PlcStatusChanged'])
  return (
    <Panel title={t('plc.stationTitle', { id: stationId })} subtitle={io.data ? t('plc.qualitySummary', { good: io.data.good, configured: io.data.configured, total: io.data.total }) : undefined}
      actions={<Button size="sm" onClick={onClose}>{t('common.close')}</Button>} flush>
      {handshake && (
        <div className="hs-strip">
          <span><small>{t('plc.phaseLabel')}</small> <Pill tone={PHASE_TONE[handshake.phase] ?? 'muted'}>{PHASE_VI[handshake.phase] ?? handshake.phase}</Pill></span>
          <span><small>{t('plc.handshake')}</small> <Pill tone={handshake.approval === 'NEEDS_CONFIRMATION' ? 'warn' : 'ok'} dot={false}>{APPROVAL_VI[handshake.approval] ?? handshake.approval}</Pill></span>
          {handshake.lastError && isAdmin && <span className="text-bad">{t('plc.lastWriteRefused', { error: localizeDeviceMessage(describeCoded(handshake.lastError)) })}</span>}
        </div>
      )}
      <Async state={io} isEmpty={(d) => d.points.length === 0} empty={{ title: t('plc.noIoPoints') }}>
        {(snap) => (
          <div className="table-wrap">
            <table className="grid">
              <thead>
                <tr>
                  <th>#</th><th>{t('plc.colSignal')}</th><th>{t('plc.colDirection')}</th><th>{t('plc.colType')}</th>
                  <th>{t('plc.colValue')}</th><th>{t('plc.colQuality')}</th><th>{t('plc.colUpdated')}</th>
                </tr>
              </thead>
              <tbody>
                {snap.points.map((p) => (
                  <tr key={p.id}>
                      <td className="num muted">{p.seq}</td>
                      <td><strong>{p.signalName}</strong><br /><small className="muted">{p.group}</small></td>
                      <td className="nowrap">{DIRECTION_VI[p.direction]}</td>
                      <td>{DATATYPE_VI[p.dataType]}</td>
                      <td><ValueCell p={p} /></td>
                      <td><QualityPill quality={p.quality} />{isAdmin && p.error && <small className="text-bad" title={p.error}> ⚠</small>}</td>
                      <td className="nowrap num">{fmtTime(p.lastUpdated)}</td>
                    </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Async>
    </Panel>
  )
}
