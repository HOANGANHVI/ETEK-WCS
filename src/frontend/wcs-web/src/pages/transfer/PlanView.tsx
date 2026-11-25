import { useState } from 'react'
import type { ImportPlan, PlanAction, PlanItem } from '../../api/configTransferTypes'
import { EmptyState, Pill } from '../../components/ui'
import { hasMessage, useT, type MsgKey } from '../../i18n'
import type { Tone } from '../../i18n/vi'
import { isModelField, refKey } from '../../lib/transferView'

const ACTION_TONE: Record<PlanAction, Tone> = { Create: 'ok', Update: 'info', Unchanged: 'muted', Skip: 'muted', Conflict: 'bad' }

function fieldLabel(t: ReturnType<typeof useT>, f: string): string {
  const k = `transfer.field_${f}` as MsgKey
  return hasMessage(k) ? t(k) : f
}

/** The field changes of one record. A record of an old package simply has no model / role line: nothing is shown that the package does not say. */
export function Changes({ item }: { item: PlanItem }) {
  const t = useT()
  if (item.action === 'Create') return <span className="muted small">{t('transfer.newRecord')}</span>
  if (item.changes.length === 0) return null
  return (
    <ul className="transfer-changes">
      {item.changes.map((c) => (
        <li key={c.field}>
          <span className="muted">{fieldLabel(t, c.field)}:</span> <code>{c.from ?? '∅'}</code> → <code>{c.to ?? '∅'}</code>
          {isModelField(c.field) && <> <Pill tone="info" dot={false}>{t('transfer.modelChangeBadge')}</Pill></>}
        </li>
      ))}
    </ul>
  )
}

/** The preview of an import: every record with its action, its changes and, for a conflict, the code explained and the server's own message (never hidden, never worked around). */
export function PlanView({ plan, stale, excluded, onToggle, known }: { plan: ImportPlan; stale: boolean; excluded: string[]; onToggle: (g: string, k: string) => void; known: string[] }) {
  const t = useT()
  const [showSame, setShowSame] = useState(false)
  const s = plan.summary
  const warnings = plan.warnings.filter((w) => !known.includes(w))
  if (plan.items.length === 0) return <EmptyState title={t('transfer.noRecords')} />
  return (
    <div className={stale ? 'transfer-plan stale' : 'transfer-plan'}>
      <div className="btn-row transfer-summary">
        <Pill tone="ok">{t('transfer.sumCreate')}: {s.create}</Pill>
        <Pill tone="info">{t('transfer.sumUpdate')}: {s.update}</Pill>
        <Pill tone="muted">{t('transfer.sumUnchanged')}: {s.unchanged}</Pill>
        <Pill tone="muted">{t('transfer.sumSkip')}: {s.skip}</Pill>
        <Pill tone={s.conflict > 0 ? 'bad' : 'muted'}>{t('transfer.sumConflict')}: {s.conflict}</Pill>
      </div>
      {warnings.length > 0 && <div className="notice notice-warn"><ul>{warnings.map((w) => <li key={w}>{w}</li>)}</ul></div>}
      {s.unchanged > 0 && <label className="check"><input type="checkbox" checked={showSame} onChange={(e) => setShowSame(e.target.checked)} /><span>{t('transfer.showUnchanged', { n: s.unchanged })}</span></label>}
      <div className="table-wrap">
        <table className="grid">
          <thead><tr><th>{t('transfer.colGroup')}</th><th>{t('transfer.colKey')}</th><th>{t('transfer.colAction')}</th><th>{t('transfer.colChanges')}</th><th>{t('transfer.colInclude')}</th></tr></thead>
          <tbody>
            {plan.items.filter((i) => showSame || i.action !== 'Unchanged').map((i) => {
              const off = excluded.includes(refKey(i.group, i.key))
              const conflictKey = `transfer.conflict_${i.conflictCode}` as MsgKey
              return (
                <tr key={refKey(i.group, i.key)}>
                  <td>{t(`transfer.group_${i.group}` as MsgKey)}</td>
                  <td><code>{i.key}</code></td>
                  <td><Pill tone={ACTION_TONE[i.action]}>{t(`transfer.action_${i.action}` as MsgKey)}</Pill>{i.headerChange && <> <Pill tone="warn" dot={false}>{t('transfer.headerBadge')}</Pill></>}</td>
                  <td>
                    <Changes item={i} />
                    {i.conflictCode && <p className="small transfer-conflict">{hasMessage(conflictKey) ? t(conflictKey) : i.conflictCode}<br /><span className="muted">{i.conflictMessage}</span></p>}
                  </td>
                  <td>{i.action !== 'Unchanged' && (
                    <input type="checkbox" aria-label={t('transfer.includeAria', { key: i.key })} checked={!off} onChange={() => onToggle(i.group, i.key)} />
                  )}</td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
    </div>
  )
}
