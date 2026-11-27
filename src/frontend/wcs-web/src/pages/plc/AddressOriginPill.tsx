import type { IoPointConfig } from '../../api/plcTypes'
import { Pill } from '../../components/ui'
import { useT } from '../../i18n'
import { addressState } from '../../lib/plcIoMap'

/** CR-048: says whether an address is the IO map's default, an Admin's own, removed, or missing. */
export function AddressOriginPill({ point }: { point: Pick<IoPointConfig, 'actualAddress' | 'addressOrigin' | 'defaultAddress'> }) {
  const t = useT()
  const state = addressState(point)
  const key = ({ default: 'plc.originDefault', custom: 'plc.originCustom', removed: 'plc.originRemoved', blocked: 'plc.originBlocked', none: 'plc.originNone' } as const)[state]
  return <Pill tone={state === 'default' ? 'ok' : state === 'custom' ? 'warn' : state === 'blocked' ? 'bad' : 'muted'} dot={false}>{t(key)}</Pill>
}
