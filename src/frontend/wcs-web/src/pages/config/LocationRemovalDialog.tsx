import { useCallback } from 'react'
import { api } from '../../api/client'
import { Modal } from '../../components/Modal'
import { RemovalDialog } from '../../components/RemovalDialog'
import { Async, Button } from '../../components/ui'
import { useApi } from '../../hooks/useApi'
import { useT } from '../../i18n'
import { BlockerList, WarningList } from './BlockerList'

/** CR-057: asks what stops the deletion; only a location nothing depends on gets the confirmation (CR-058: with the configuration warnings). */
export function LocationRemovalDialog({ code, onClose, onDeleted }: { code: string; onClose: () => void; onDeleted: () => void }) {
  const t = useT()
  const plan = useApi(useCallback(() => api.locationRemoval(code), [code]))
  const title = t('config.locDeleteTitle', { code })
  if (plan.data?.canDelete)
    return <RemovalDialog title={title} body={t('config.locDeleteBody', { code })} confirm={t('config.locDeleteConfirm')} typeToConfirm={code} onClose={onClose}
      details={<WarningList warnings={plan.data.warnings ?? []} />}
      onConfirm={async (reason, typed) => { await api.deleteLocation(code, reason, typed); onDeleted() }} />
  return (
    <Modal title={title} onClose={onClose} footer={<Button onClick={onClose}>{t('config.closeBtn')}</Button>}>
      <Async state={plan} empty={{ title }}>
        {(d) => <><p>{t('config.locDeleteBlocked', { code })}</p><BlockerList blockers={d.blockers} /></>}
      </Async>
    </Modal>
  )
}
