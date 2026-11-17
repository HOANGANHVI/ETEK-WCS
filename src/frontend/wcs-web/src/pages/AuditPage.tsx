import { Fragment, useCallback, useState } from 'react'
import { api } from '../api/client'
import type { AuditResult, AuditRow } from '../api/authTypes'
import { Async, PageHeader, Panel, Pill } from '../components/ui'
import { useApi } from '../hooks/useApi'
import { formatDateTime } from '../i18n/format'
import { useT } from '../i18n'
import { AUDIT_ACTION_VI, AUDIT_RESULT_TONE, AUDIT_RESULT_VI } from '../i18n/vi'

const PAGE = 100

function pretty(json: string | null) {
  if (!json) return null
  try { return JSON.stringify(JSON.parse(json), null, 2) } catch { return json }
}

export function AuditPage() {
  const t = useT()
  const [actor, setActor] = useState('')
  const [action, setAction] = useState('')
  const [result, setResult] = useState<AuditResult | ''>('')
  const [take, setTake] = useState(PAGE)
  const [open, setOpen] = useState<string | null>(null)
  const list = useApi(useCallback(() => api.audit({ actor: actor.trim() || undefined, action: action || undefined, result, take }), [actor, action, result, take]))

  return (
    <>
      <PageHeader title={t('logs.auditTitle')} />
      <Panel title={t('logs.auditPanel')} flush actions={
        <>
          <input type="search" placeholder={t('logs.actorPh')} aria-label={t('logs.actorAria')} value={actor} onChange={(e) => { setActor(e.target.value); setTake(PAGE) }} />
          <select aria-label={t('logs.actionAria')} value={action} onChange={(e) => { setAction(e.target.value); setTake(PAGE) }}>
            <option value="">{t('logs.allActions')}</option>
            {Object.entries(AUDIT_ACTION_VI).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
          </select>
          <select aria-label={t('logs.resultAria')} value={result} onChange={(e) => { setResult(e.target.value as AuditResult | ''); setTake(PAGE) }}>
            <option value="">{t('logs.allResults')}</option>
            {Object.entries(AUDIT_RESULT_VI).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
          </select>
        </>
      }>
        <Async state={list} isEmpty={(d) => d.length === 0} empty={{ title: t('logs.auditNone') }}>
          {(rows: AuditRow[]) => (
            <>
              <div className="table-wrap">
                <table className="grid clickable">
                  <thead><tr><th>{t('logs.colTime')}</th><th>{t('logs.colActor')}</th><th>{t('logs.colAction')}</th><th>{t('logs.colTarget')}</th><th>{t('logs.colResult')}</th><th>{t('logs.colNote')}</th></tr></thead>
                  <tbody>
                    {rows.map((a) => (
                      <Fragment key={a.id}>
                        <tr onClick={() => setOpen(open === a.id ? null : a.id)} className={open === a.id ? 'selected' : ''}>
                          <td className="nowrap num">{formatDateTime(a.occurredAt)}</td>
                          <td><strong>{a.actor ?? '—'}</strong>{a.actorRole && <small className="muted"> · {a.actorRole}</small>}</td>
                          <td>{AUDIT_ACTION_VI[a.action] ?? a.action}</td>
                          <td className="mono">{[a.targetType, a.targetId].filter(Boolean).join(' ') || '—'}</td>
                          <td><Pill tone={AUDIT_RESULT_TONE[a.result]}>{AUDIT_RESULT_VI[a.result]}</Pill></td>
                          <td>{a.detail ?? ''}</td>
                        </tr>
                        {open === a.id && (
                          <tr className="audit-detail">
                            <td colSpan={6}>
                              <div className="audit-diff">
                                <div><strong>{t('logs.before')}</strong><pre>{pretty(a.beforeJson) ?? '—'}</pre></div>
                                <div><strong>{t('logs.after')}</strong><pre>{pretty(a.afterJson) ?? '—'}</pre></div>
                                <div><strong>{t('logs.address')}</strong><pre>{a.ipAddress ?? '—'}</pre></div>
                              </div>
                            </td>
                          </tr>
                        )}
                      </Fragment>
                    ))}
                  </tbody>
                </table>
              </div>
              {rows.length >= take && (
                <div className="pad"><button type="button" className="btn btn-secondary btn-sm" onClick={() => setTake(take + PAGE)}>{t('common.loadMore')}</button></div>
              )}
            </>
          )}
        </Async>
      </Panel>
    </>
  )
}
