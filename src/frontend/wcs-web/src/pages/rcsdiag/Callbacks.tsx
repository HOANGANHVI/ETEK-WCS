import { useCallback } from 'react'
import { api } from '../../api/client'
import { Async, Button, Panel, Pill } from '../../components/ui'
import { useApi } from '../../hooks/useApi'
import { useT } from '../../i18n'
import { formatDateTime } from '../../i18n/format'

/**
 * CR-032 – Callback Monitor. The five calls the RCS makes TO a third-party platform (agvCallback, warnCallback, bindNotify, applyReturnForValid, applyReturnForBin) are NOT outbound APIs:
 * WCS cannot send them, it can only receive them. This build provides no receiving endpoint, and the screen says so; the list shows only callbacks that were really received (nothing is invented or simulated).
 */
export function Callbacks() {
  const t = useT()
  const cb = useApi(useCallback(() => api.rcsDiagCallbacks(), []))
  return (
    <Async state={cb} empty={{ title: t('rcsDiag.cbEmpty') }}>
      {(d) => (
        <div className="stack">
          <div className="notice notice-info" role="note">{t('rcsDiag.cbExplain')}</div>
          <Panel title={t('rcsDiag.cbEndpoints')} subtitle={t('rcsDiag.cbEndpointsSub')} actions={<Button size="sm" onClick={() => cb.reload()}>{t('rcsDiag.hRefresh')}</Button>} flush>
            <div className="table-wrap">
              <table className="grid">
                <thead><tr><th>{t('rcsDiag.cbName')}</th><th>{t('rcsDiag.cbSection')}</th><th>{t('rcsDiag.cbPurpose')}</th><th>{t('rcsDiag.cbStatus')}</th></tr></thead>
                <tbody>
                  {d.endpoints.map((e) => (
                    <tr key={e.name}>
                      <td><strong className="mono">{e.name}</strong></td><td>{e.section}</td><td>{e.function}</td>
                      <td><Pill tone={e.provided === 'Provided' ? 'ok' : 'warn'}>{e.provided === 'Provided' ? t('rcsDiag.cbProvided') : t('rcsDiag.cbNotProvided')}</Pill></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </Panel>
          <Panel title={t('rcsDiag.cbReceived')} subtitle={t('rcsDiag.cbReceivedSub')} flush>
            {d.received.length === 0 ? <p className="muted pad">{t('rcsDiag.cbNone')}</p> : (
              <div className="table-wrap">
                <table className="grid">
                  <thead><tr><th>{t('rcsDiag.hcTime')}</th><th>{t('rcsDiag.cbName')}</th><th>{t('rcsDiag.cbFrom')}</th><th>{t('rcsDiag.hcHttp')}</th><th>{t('rcsDiag.cbProcessed')}</th><th>{t('rcsDiag.cbBody')}</th></tr></thead>
                  <tbody>
                    {d.received.map((r) => (
                      <tr key={r.id}>
                        <td className="nowrap num">{formatDateTime(r.receivedAt)}</td><td className="mono">{r.callback}</td><td className="mono">{r.sourceAddress ?? '—'}</td><td className="num">{r.httpStatus ?? '—'}</td>
                        <td><Pill tone={r.processed ? 'ok' : 'bad'}>{r.processed ? t('rcsDiag.cbYes') : t('rcsDiag.cbNo')}</Pill>{r.errorMessage ? <span className="muted small"> {r.errorMessage}</span> : null}</td>
                        <td><pre className="rd-code rd-code-sm" tabIndex={0}>{r.requestJson ?? '—'}</pre></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Panel>
        </div>
      )}
    </Async>
  )
}
