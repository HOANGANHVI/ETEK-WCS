import type { LocationRemoval } from '../../api/types'
import { useT, type MsgKey } from '../../i18n'

/** CR-057: the reasons the server gives for keeping a location, in the language of the user. */
export function BlockerList({ blockers }: { blockers: LocationRemoval['blockers'] }) {
  const t = useT()
  return <ul>{blockers.map((b) => <li key={b.code}>{t(`config.blk_${b.code}` as MsgKey, { detail: b.detail })}</li>)}</ul>
}

/** CR-058: the uses by the deployment configuration, shown before the confirmation. */
export function WarningList({ warnings }: { warnings: NonNullable<LocationRemoval['warnings']> }) {
  const t = useT()
  if (warnings.length === 0) return null
  return (
    <div className="notice notice-warn" role="status">
      <p>{t('config.locWarnTitle')}</p>
      <ul>{warnings.map((w) => <li key={w.code}>{t(`config.warn_${w.code}` as MsgKey, { detail: w.detail })}</li>)}</ul>
      <p>{t('config.locWarnAgain')}</p>
    </div>
  )
}
