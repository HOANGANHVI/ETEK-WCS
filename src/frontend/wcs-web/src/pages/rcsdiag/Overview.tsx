import { useCallback, useState } from 'react'
import { api } from '../../api/client'
import type { RcsCheckState, RcsExecution } from '../../api/rcsDiagTypes'
import { Async, Button, Panel, Pill } from '../../components/ui'
import { useApi } from '../../hooks/useApi'
import { useT } from '../../i18n'
import { formatDateTime } from '../../i18n/format'
import { describeError } from '../../i18n/vi'
import { OutcomePill } from './Inspector'

function CheckLine({ label, check }: { label: string; check: RcsCheckState | null }) {
  const t = useT()
  return (
    <>
      <dt>{label}</dt>
      <dd>
        {check ? (
          <>
            <Pill tone={check.success ? 'ok' : 'bad'}>{check.success ? t('rcsDiag.checkOk') : t('rcsDiag.checkFailed')}</Pill>{' '}
            <span className="muted small">{formatDateTime(check.at)} · {check.durationMs} ms · {check.by}{check.detail ? ` · ${check.detail}` : ''}</span>
          </>
        ) : <span className="muted">{t('rcsDiag.notChecked')}</span>}
      </dd>
    </>
  )
}

/**
 * CR-032 – RCS Connection Overview. It shows the SAVED RCS connection (no separate connection is kept here) and the result of the last two checks.
 * A TCP check only proves a port accepts a connection; the API check is ONE read-only queryAgvStatus. TCP success never counts as API success. Nothing is called until a button is pressed.
 */
export function Overview({ onChecked }: { onChecked: () => void }) {
  const t = useT()
  const ov = useApi(useCallback(() => api.rcsDiagOverview(), []))
  const [busy, setBusy] = useState<'tcp' | 'api' | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [apiResult, setApiResult] = useState<RcsExecution | null>(null)

  const run = async (kind: 'tcp' | 'api') => {
    setBusy(kind); setError(null)
    try {
      if (kind === 'tcp') await api.rcsDiagTcpCheck()
      else setApiResult(await api.rcsDiagApiCheck())
    } catch (e) {
      setError(describeError(e))
    } finally {
      setBusy(null); ov.reload(); onChecked()
    }
  }

  return (
    <Panel title={t('rcsDiag.overviewTitle')} subtitle={t('rcsDiag.overviewSub')}
      actions={<><Button size="sm" disabled={busy !== null} onClick={() => void run('tcp')}>{busy === 'tcp' ? t('rcsDiag.checking') : t('rcsDiag.checkTcp')}</Button>
        <Button size="sm" variant="primary" disabled={busy !== null} onClick={() => void run('api')}>{busy === 'api' ? t('rcsDiag.checking') : t('rcsDiag.checkApi')}</Button></>}>
      <Async state={ov} empty={{ title: t('rcsDiag.overviewEmpty') }}>
        {(o) => (
          <>
            {!o.configured && <div className="notice notice-warn" role="alert">{t('rcsDiag.notConfigured')}</div>}
            {o.configured && o.problem && <div className="notice notice-warn" role="alert">{o.problem}</div>}
            <dl className="kv2 rd-facts">
              <dt>{t('rcsDiag.ovSource')}</dt><dd><Pill tone="info">{t('rcsDiag.sourceReal')}</Pill> <span className="muted small">{t('rcsDiag.sourceNote', { fleet: o.fleetSource })}</span></dd>
              <dt>{t('rcsDiag.ovHost')}</dt><dd className="mono">{o.host ?? '—'}</dd>
              <dt>{t('rcsDiag.ovPort')}</dt><dd className="mono">{o.port ?? '—'}</dd>
              <dt>{t('rcsDiag.ovBase')}</dt><dd className="mono">{o.host && o.port ? `${o.scheme ?? 'http'}://${o.host}:${o.port}` : '—'}</dd>
              <dt>{t('rcsDiag.ovState')}</dt><dd><Pill tone={o.configured && o.enabled ? 'ok' : 'warn'}>{o.configured ? (o.enabled ? t('rcsDiag.enabled') : t('rcsDiag.disabled')) : t('rcsDiag.notSaved')}</Pill> <span className="muted small">{t('rcsDiag.ovTimeout', { ms: o.timeoutMs })}</span></dd>
              <CheckLine label={t('rcsDiag.ovTcp')} check={o.lastTcpCheck} />
              <CheckLine label={t('rcsDiag.ovApi')} check={o.lastApiCheck} />
              <dt>{t('rcsDiag.ovAvail')}</dt><dd>{o.apiAvailable === null ? <span className="muted">{t('rcsDiag.notChecked')}</span> : <Pill tone={o.apiAvailable ? 'ok' : 'bad'}>{o.apiAvailable ? t('rcsDiag.apiAvailable') : t('rcsDiag.apiUnavailable')}</Pill>}</dd>
              <dt>{t('rcsDiag.ovLatency')}</dt><dd className="mono">{o.lastApiCheck ? `${o.lastApiCheck.durationMs} ms` : '—'}</dd>
              <dt>{t('rcsDiag.ovConc')}</dt><dd className="mono">{o.inUse} / {o.maxConcurrent}</dd>
            </dl>
            <p className="muted small">{t('rcsDiag.tcpNotApi')}</p>
          </>
        )}
      </Async>
      {error && <div className="notice notice-bad" role="alert">{error}</div>}
      {apiResult && (
        <p className="small">{t('rcsDiag.lastApiCheckResult')} <OutcomePill outcome={apiResult.outcome} /> <span className="muted">HTTP {apiResult.httpStatus ?? '—'} · {apiResult.durationMs} ms</span></p>
      )}
    </Panel>
  )
}
