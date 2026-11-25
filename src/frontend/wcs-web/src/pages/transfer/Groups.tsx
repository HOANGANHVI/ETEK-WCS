import type { TransferGroup } from '../../api/configTransferTypes'
import { useT, type MsgKey } from '../../i18n'

/** One configuration group in the export list / the package list: name, what it carries and what it depends on. */
export function GroupLabel({ g }: { g: TransferGroup }) {
  const t = useT()
  return (
    <span className="transfer-group">
      <strong>{t(`transfer.group_${g.id}` as MsgKey)}</strong>
      <span className="muted small">{t(`transfer.groupDesc_${g.id}` as MsgKey)}</span>
      <span className="muted small">{t('transfer.existing', { n: g.targetCount })}{g.dependsOn.length > 0 ? ' · ' + t('transfer.dependsOn', { groups: g.dependsOn.map((d) => t(`transfer.group_${d}` as MsgKey)).join(', ') }) : ''}</span>
    </span>
  )
}

/** Advice shown while locations / processes are selected for export without the product model group (the package would name models by code that the target may not have). */
export function ModelExportHint() {
  const t = useT()
  return <div className="notice notice-warn" role="note">{t('transfer.exportModelHint')}</div>
}
