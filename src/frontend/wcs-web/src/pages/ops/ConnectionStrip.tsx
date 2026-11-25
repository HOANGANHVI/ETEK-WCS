import type { FleetView } from '../../api/fleetTypes'
import type { SystemHealth } from '../../api/types'
import { Pill } from '../../components/ui'
import { linkText, rcsView } from '../../components/linkStatus'
import { useT, type MsgKey } from '../../i18n'
import { formatDateTime } from '../../i18n/format'
import type { Gate } from '../../lib/opsConsole'

interface Props { health: SystemHealth | null; fleet: FleetView | null; loadFailed: boolean; gate: Gate }

/**
 * Overview of the RCS link and the fleet. It shows exactly what the shared fleet / health data say (the same data and the same `rcsView` rule as the header and the AMR page):
 * the link is never "Connected" because of the integration mode, simulated data is labelled, and old / missing data is said to be old / missing.
 */
export function ConnectionStrip({ health, fleet, loadFailed, gate }: Props) {
  const t = useT()
  const link = rcsView(fleet, loadFailed)
  const guard = health?.integration.rcs
  const never = t('ops.connNever')
  const amrs = fleet?.amrs ?? []
  const active = amrs.filter((a) => a.activeTaskId !== null).length
  return (
    <section className="panel ops-conn" aria-label={t('ops.connTitle')}>
      <h2>{t('ops.connTitle')}</h2>
      {loadFailed && !fleet && !health && <div className="notice notice-bad" role="alert">{t('ops.connLoadFailed')}</div>}
      <dl className="kv2">
        <dt>{t('ops.connIntegration')}</dt>
        <dd>{health ? health.integration.mode : t('ops.noValue')}</dd>
        <dt>{t('ops.connFleetSource')}</dt>
        <dd>
          {fleet ? <Pill tone={fleet.source === 'Real' && !fleet.simulatedData ? 'ok' : 'warn'}>{fleet.source === 'Real' ? t('ops.sourceReal') : t('ops.sourceSimulator')}</Pill> : t('ops.noValue')}
          {fleet?.simulatedData && <span className="notice notice-warn" role="note"> {t('ops.simulatedData')}</span>}
        </dd>
        <dt>{t('ops.connRcsLink')}</dt>
        <dd><Pill tone={link.tone}>{linkText(t, link.text)}</Pill></dd>
        <dt>{t('ops.connGuard')}</dt>
        <dd>{guard === undefined ? t('ops.guardUnknown') : guard.dispatchAllowed ? <Pill tone="ok">{t('ops.guardAllowed')}</Pill> : <Pill tone="bad">{t('ops.guardRefused', { code: guard.code })}</Pill>}</dd>
        <dt>{t('ops.connDispatchMode')}</dt>
        <dd>{fleet?.dispatchMode ? t(`ops.amrChoice_${fleet.dispatchMode}` as MsgKey) : t('ops.noValue')}</dd>
        <dt>{t('ops.connLastSuccess')}</dt>
        <dd>{fleet?.lastSuccessAt ? formatDateTime(fleet.lastSuccessAt) : never}</dd>
        <dt>{t('ops.connLastAttempt')}</dt>
        <dd>{fleet?.lastAttemptAt ? formatDateTime(fleet.lastAttemptAt) : never}</dd>
        {fleet && fleet.consecutiveFailures > 0 && (<><dt>{t('ops.connFailures')}</dt><dd>{fleet.consecutiveFailures}</dd></>)}
        {fleet?.lastErrorCode && (<><dt>{t('ops.connLastError')}</dt><dd>{fleet.lastErrorCode}{fleet.lastErrorMessage ? ` · ${fleet.lastErrorMessage}` : ''}</dd></>)}
        <dt>AMR</dt>
        <dd>{fleet ? `${fleet.count} · ${fleet.freshCount} / ${fleet.count} ${t('ops.connFresh')} · ${active} ${t('ops.connActive')}` : t('ops.noValue')}</dd>
      </dl>
      {fleet && <p className="muted small">{t('ops.connFreshRule', { n: fleet.staleAfterSeconds })}</p>}
      {amrs.length > 0 && (
        <ul className="ops-amrs">
          {amrs.map((a) => (
            <li key={a.robotCode}>
              <strong>{a.robotCode}</strong> · {a.status ?? t('ops.noValue')} · <Pill tone={a.state === 'Fresh' ? 'ok' : 'warn'}>{t(`ops.amr_${a.state}` as MsgKey)}</Pill>
              {a.activeTaskId && <span className="muted"> · {a.activeTaskState ?? ''}</span>}
            </li>
          ))}
        </ul>
      )}
      <GateNotice gate={gate} />
    </section>
  )
}

export function GateNotice({ gate }: { gate: Gate }) {
  const t = useT()
  return (
    <div className={`notice ${gate.allowed ? 'notice-ok' : 'notice-bad'}`} role="status" data-gate={gate.allowed ? 'open' : 'blocked'}>
      {gate.allowed ? t('ops.gateOk') : t('ops.gateBlocked')}
      {!gate.allowed && <ul>{gate.blockers.map((b) => <li key={b}>{t(`ops.block_${b}` as MsgKey)}</li>)}</ul>}
      {gate.warnings.length > 0 && <ul>{gate.warnings.map((w) => <li key={w} className="muted">{t(`ops.warn_${w}` as MsgKey)}</li>)}</ul>}
    </div>
  )
}
