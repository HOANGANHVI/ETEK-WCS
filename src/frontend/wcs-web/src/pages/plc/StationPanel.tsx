import { useCallback } from 'react'
import { api } from '../../api/client'
import type { HandshakeState } from '../../api/plcTypes'
import { Async, Button, Panel, Pill } from '../../components/ui'
import { useApi } from '../../hooks/useApi'
import { APPROVAL_VI, DATATYPE_VI, DIRECTION_VI, PHASE_TONE, PHASE_VI } from '../../i18n/plc'
import { fmtTime } from './plcFormat'
import { QualityPill, ValueCell } from './plcUi'

interface Props { stationId: string; isAdmin: boolean; handshake: HandshakeState | undefined; onClose: () => void }

/** CR-004: the 23 IO of one station, strictly READ-ONLY. Addresses are edited in the IO-mapping screen, connections in the PLC-connection screen. */
export function StationPanel({ stationId, isAdmin, handshake, onClose }: Props) {
  const io = useApi(useCallback(() => api.plcStationIo(stationId), [stationId]), ['StationIoChanged', 'PlcStatusChanged'])
  return (
    <Panel title={`Trạm ${stationId} – 23 tín hiệu IO`} subtitle={io.data ? `${io.data.good} tốt / ${io.data.configured} đã cấu hình / ${io.data.total} IO` : undefined}
      actions={<Button size="sm" onClick={onClose}>Đóng</Button>} flush>
      {handshake && (
        <div className="hs-strip">
          <span><small>Pha quan sát (kỹ thuật)</small> <Pill tone={PHASE_TONE[handshake.phase] ?? 'muted'}>{PHASE_VI[handshake.phase] ?? handshake.phase}</Pill></span>
          <span><small>Handshake</small> <Pill tone={handshake.approval === 'NEEDS_CONFIRMATION' ? 'warn' : 'ok'} dot={false}>{APPROVAL_VI[handshake.approval] ?? handshake.approval}</Pill></span>
          {handshake.lastError && isAdmin && <span className="text-bad">Lần ghi gần nhất bị từ chối: {handshake.lastError}</span>}
        </div>
      )}
      <Async state={io} isEmpty={(d) => d.points.length === 0} empty={{ title: 'Trạm chưa có IO point' }}>
        {(snap) => (
          <div className="table-wrap">
            <table className="grid">
              <thead>
                <tr>
                  <th>#</th><th>Tín hiệu</th><th>Chiều</th><th>Kiểu</th>
                  <th>Giá trị</th><th>Chất lượng</th><th>Cập nhật</th>
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
