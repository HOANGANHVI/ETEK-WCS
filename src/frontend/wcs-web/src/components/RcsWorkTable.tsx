import { Link } from 'react-router-dom'
import type { RcsWork } from '../api/workTypes'
import type { TaskState } from '../api/types'
import { useT, type MsgKey } from '../i18n'
import { formatDateTime } from '../i18n/format'
import { flowInfo, taskCode } from '../i18n/vi'
import { LEG_STATE_LABEL } from '../i18n/routing'
import { orderCode, RCS_STATE_TONE, workLink } from '../lib/work'
import { Pill, TaskStatePill } from './ui'

/** CR-051: RCS work of both engines: which WCS record, which RCS task, which AMR, where it stands towards the RCS and in WCS. */
export function RcsWorkTable({ rows }: { rows: RcsWork[] }) {
  const t = useT()
  return (
    <div className="table-wrap">
      <table className="grid" aria-label={t('linked.rcsWorkTitle')}>
        <thead><tr><th>{t('linked.rcsColWork')}</th><th>{t('linked.colProcess')}</th><th>{t('linked.rcsColRoute')}</th><th>{t('linked.rcsColAmr')}</th><th>{t('linked.rcsColTask')}</th><th>{t('linked.rcsColRcsState')}</th><th>{t('linked.rcsColWcsState')}</th><th>{t('linked.rcsColSent')}</th><th>{t('linked.rcsColUpdated')}</th></tr></thead>
        <tbody>
          {rows.map((w) => (
            <tr key={w.id} data-work={w.rcsRequestCode}>
              <td className="nowrap">
                <Link to={workLink(w)} className="mono">{w.kind === 'Task' ? taskCode(w.id) : orderCode(w.orderId ?? w.id)}</Link>
                <div className="muted small">{w.kind === 'Task' ? t('linked.kindTask') : t('linked.rcsLeg', { n: w.legSequence ?? 0 })}</div>
              </td>
              <td className="nowrap">{w.kind === 'Task' ? flowInfo(w.processCode).code : w.processCode}</td>
              <td className="mono nowrap">{w.source ?? '—'} → {w.destination ?? '?'}</td>
              <td className="mono">{w.robotCode ?? '—'}{w.agvCode ? <div className="muted small">{w.agvCode}</div> : null}</td>
              <td className="mono">{w.rcsTaskCode ?? '—'}<div className="muted small">{w.rcsRequestCode}</div></td>
              <td><Pill tone={RCS_STATE_TONE[w.rcsState]}>{t(`linked.rcsState_${w.rcsState}` as MsgKey)}</Pill>{w.detail && w.rcsState !== 'Completed' ? <div className="muted small">{w.detail}</div> : null}</td>
              <td>{w.kind === 'Task' ? <TaskStatePill state={w.workState as TaskState} /> : LEG_STATE_LABEL[w.workState] ?? w.workState}</td>
              <td className="nowrap num">{w.sentAt ? formatDateTime(w.sentAt) : '—'}</td>
              <td className="nowrap num">{formatDateTime(w.updatedAt)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
