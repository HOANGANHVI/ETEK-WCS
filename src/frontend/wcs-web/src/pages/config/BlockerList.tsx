import type { LocationRemoval } from '../../api/types'
import { useT, type MsgKey } from '../../i18n'

/** CR-057: the reasons the server gives for keeping a location, in the language of the user. */
export function BlockerList({ blockers }: { blockers: LocationRemoval['blockers'] }) {
  const t = useT()
  return <ul>{blockers.map((b) => <li key={b.code}>{t(`config.blk_${b.code}` as MsgKey, { detail: b.detail })}</li>)}</ul>
}
