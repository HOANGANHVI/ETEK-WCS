import type { ReactNode } from 'react'
import type { IoPointState } from '../../api/plcTypes'
import { Pill } from '../../components/ui'
import { t } from '../../i18n'
import {
  CONFIG_STATUS_TONE, CONFIG_STATUS_VI, ENDPOINT_STATE_TONE, ENDPOINT_STATE_VI, QUALITY_TONE, QUALITY_VI,
} from '../../i18n/plc'

export const StatePill = ({ state }: { state: string }) => <Pill tone={ENDPOINT_STATE_TONE[state] ?? 'muted'}>{ENDPOINT_STATE_VI[state] ?? state}</Pill>
export const QualityPill = ({ quality }: { quality: string }) => <Pill tone={QUALITY_TONE[quality] ?? 'muted'} dot={false}>{QUALITY_VI[quality] ?? quality}</Pill>
export const ConfigPill = ({ status }: { status: string }) => <Pill tone={CONFIG_STATUS_TONE[status] ?? 'muted'} dot={false}>{CONFIG_STATUS_VI[status] ?? status}</Pill>

/** Current value as the operator expects to read it: BOOL as BẬT/TẮT, INT as a number, STRING quoted, nothing as a dash. */
export function ValueCell({ p }: { p: IoPointState }): ReactNode {
  if (p.value === null || p.value === undefined) return <span className="muted">—</span>
  if (typeof p.value === 'boolean') return <Pill tone={p.value ? 'ok' : 'muted'} dot={false}>{p.value ? t('plc.on') : t('plc.off')}</Pill>
  if (typeof p.value === 'string') return <span className="mono">“{p.value}”</span>
  return <span className="mono num">{p.value}</span>
}

/** Tiny stacked bar: good (green) vs configured-but-not-good (amber) vs not configured (grey). */
export function QualityBar({ good, configured, total }: { good: number; configured: number; total: number }) {
  const pct = (n: number) => (total === 0 ? 0 : Math.round((n / total) * 100))
  return (
    <span className="qbar" role="img" aria-label={t('plc.qualityLabel', { good, configured, total })}>
      <i className="q-good" style={{ width: `${pct(good)}%` }} />
      <i className="q-cfg" style={{ width: `${pct(Math.max(0, configured - good))}%` }} />
    </span>
  )
}

