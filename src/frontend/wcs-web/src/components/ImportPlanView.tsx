import type { ReferenceImportPlan } from '../api/routingTypes'
import { useT } from '../i18n'
import { processName } from '../i18n/processNames'

/** CR-062: what loading the reference processes will add and what it leaves untouched. Nothing is ever overwritten. */
export function ImportPlanView({ plan }: { plan: ReferenceImportPlan }) {
  const t = useT()
  const missing = plan.add.some((p) => p.missingLocations.length > 0)
  return (
    <>
      <p><strong>{t('routing.impNoOverwrite')}</strong></p>
      {plan.add.length === 0 ? <div className="notice notice-ok" role="status">{t('routing.impNothing')}</div> : (
        <div className="notice notice-warn" role="status">
          <p>{t('routing.impAdd', { n: plan.add.length })}</p>
          <ul>
            {plan.add.map((p) => (
              <li key={p.code}>
                <strong>{p.code}</strong> · {processName(p.code, p.name)}
                {p.reviewBlocked && <> — {t('routing.impReview')}</>}
                {p.missingLocations.length > 0 && <> — {t('routing.impMissing', { codes: p.missingLocations.join(', ') })}</>}
              </li>
            ))}
          </ul>
          <p>{t('routing.impEffect')}{missing ? ` ${t('routing.impMissingEffect')}` : ''}</p>
        </div>
      )}
      {plan.keep.length > 0 && <p className="muted">{t('routing.impKeep', { n: plan.keep.length, codes: plan.keep.join(', ') })}</p>}
    </>
  )
}
