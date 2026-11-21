import { Link } from 'react-router-dom'
import type { FleetAmr, FleetView } from '../api/fleetTypes'
import { EmptyState, Panel, Pill } from './ui'
import { hasMessage, useT, type MsgKey } from '../i18n'
import { formatDateTime } from '../i18n/format'
import { taskCode } from '../i18n/vi'
import type { Tone } from '../i18n/vi'

/** Tone of an AMR status code of the RCS API (Appendix B): 1 done, 2 executing, 4 idle, 5 stopped, 6-9 normal work, everything else an exception. Unknown codes are muted. */
const STATUS_TONE: Record<string, Tone> = { 1: 'ok', 2: 'info', 3: 'bad', 4: 'ok', 5: 'warn', 6: 'info', 7: 'info', 8: 'info', 9: 'info' }
const toneOf = (status: string | null): Tone => (status === null ? 'muted' : STATUS_TONE[status] ?? (hasStatus(status) ? 'bad' : 'muted'))
const hasStatus = (s: string) => hasMessage(`agv.amrStatus_${s}` as MsgKey)
const DATA_TONE: Record<FleetAmr['state'], Tone> = { Fresh: 'ok', Stale: 'warn', Missing: 'muted' }
const val = (v: string | null | undefined) => (v === null || v === undefined || v === '' ? '–' : v)

/**
 * CR-027 – the AMR fleet exactly as the RCS reports it (queryAgvStatus): any number of AMRs, identified by robotCode. Read-only. Data that is not being refreshed is
 * shown as stale / missing, never as current; a simulated list is labelled as simulated. The business AGV1/AGV2 cards below stay as they were.
 */
export function FleetPanel({ fleet }: { fleet: FleetView }) {
  const t = useT()
  const connTone: Tone = fleet.connection === 'Connected' ? 'ok' : fleet.connection === 'Disconnected' ? 'bad' : 'muted'
  const errKey = fleet.lastErrorCode ? (`agv.fleetErr_${fleet.lastErrorCode.split(':')[0]}` as MsgKey) : null
  const err = fleet.lastErrorCode ? `${errKey && hasMessage(errKey) ? t(errKey) : fleet.lastErrorCode} (${fleet.lastErrorCode})` : null
  const status = (a: FleetAmr) => {
    if (a.status === null || a.status === '') return <span className="muted">–</span>
    return hasStatus(a.status)
      ? <Pill tone={toneOf(a.status)}>{a.status} · {t(`agv.amrStatus_${a.status}` as MsgKey)}</Pill>
      : <Pill tone="muted">{t('agv.amrStatusUnknown', { code: a.status })}</Pill>
  }
  return (
    <Panel title={t('agv.fleetTitle')} subtitle={t('agv.fleetSub', { n: fleet.count, fresh: fleet.freshCount })} className="mb"
      actions={<><Pill tone={fleet.simulatedData ? 'warn' : 'info'} dot={false}>{fleet.simulatedData ? t('agv.fleetSimulated') : t('agv.fleetReal')}</Pill><Pill tone={connTone}>{t(`agv.fleetConn_${fleet.connection}` as MsgKey)}</Pill></>}>
      {fleet.simulatedData && <div className="notice notice-warn" role="note">{t('agv.fleetSimulatedNote')}</div>}
      {fleet.connection === 'Disconnected' && <div className="notice notice-bad" role="alert">{t('agv.fleetDisconnected', { time: fleet.lastSuccessAt ? formatDateTime(fleet.lastSuccessAt) : '–' })}{err ? ` ${err}` : ''}</div>}
      <p className="muted small">
        {fleet.lastSuccessAt ? t('agv.fleetLastOk', { time: formatDateTime(fleet.lastSuccessAt), n: fleet.intervalSeconds }) : t('agv.fleetNever')}
      </p>
      {fleet.amrs.length === 0
        ? <EmptyState title={fleet.connection === 'NoData' ? t('agv.fleetNoData') : fleet.connection === 'Connected' ? t('agv.fleetNone') : t('agv.fleetNoDataFailed')} hint={t('agv.fleetEmptyHint')} />
        : (
          <div className="table-wrap">
            <table className="grid">
              <thead><tr>
                <th>{t('agv.fleetColAmr')}</th><th>{t('agv.fleetColData')}</th><th>{t('agv.fleetColStatus')}</th><th>{t('agv.fleetColBattery')}</th><th>{t('agv.fleetColPos')}</th>
                <th>{t('agv.fleetColDir')}</th><th>{t('agv.fleetColSpeed')}</th><th>{t('agv.fleetColExcl')}</th><th>{t('agv.fleetColStop')}</th><th>{t('agv.fleetColRack')}</th>
                <th>{t('agv.fleetColTask')}</th><th>{t('agv.fleetColSeen')}</th>
              </tr></thead>
              <tbody>
                {fleet.amrs.map((a) => (
                  <tr key={a.robotCode}>
                    <td className="mono nowrap"><strong>{a.robotCode}</strong>{a.mapCode ? <span className="muted small"> · {a.mapCode}</span> : null}</td>
                    <td><Pill tone={DATA_TONE[a.state]}>{t(`agv.fleetState_${a.state}` as MsgKey)}</Pill></td>
                    <td>{status(a)}</td>
                    <td className="nowrap">{a.battery !== null && a.battery !== '' ? `${a.battery} %` : '–'}</td>
                    <td className="nowrap">{a.posX === null && a.posY === null ? '–' : `${val(a.posX)}, ${val(a.posY)} mm`}</td>
                    <td className="nowrap">{a.robotDir !== null && a.robotDir !== '' ? `${a.robotDir}°` : '–'}</td>
                    <td className="nowrap">{a.speed !== null && a.speed !== '' ? `${a.speed} mm/s` : '–'}</td>
                    <td>{a.exclType === '1' ? <Pill tone="warn" dot={false}>{t('agv.fleetExcluded')}</Pill> : a.exclType === '0' ? t('agv.fleetNo') : '–'}</td>
                    <td>{a.stop === '1' ? <Pill tone="warn" dot={false}>{t('agv.fleetStopped')}</Pill> : a.stop === '0' ? t('agv.fleetNo') : '–'}</td>
                    <td className="mono">{val(a.podCode)}{a.podDir ? <span className="muted small"> {a.podDir}°</span> : null}</td>
                    <td>
                      {a.linkedAgvCode ? <span className="muted small">{a.linkedAgvCode}</span> : null}
                      {a.activeTaskId ? <> <Link to={`/tasks?id=${a.activeTaskId}`} className="mono">{taskCode(a.activeTaskId)}</Link></> : a.linkedAgvCode ? null : '–'}
                    </td>
                    <td className="nowrap" title={formatDateTime(a.firstSeenAt)}>{formatDateTime(a.lastSeenAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      {fleet.amrs.some((a) => a.state === 'Missing') && <p className="muted small">{t('agv.fleetMissingNote')}</p>}
    </Panel>
  )
}
