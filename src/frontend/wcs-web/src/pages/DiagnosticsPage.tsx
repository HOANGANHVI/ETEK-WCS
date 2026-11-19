import { useCallback } from 'react'
import { api } from '../api/client'
import { PageHeader } from '../components/ui'
import { useApi } from '../hooks/useApi'
import { useT } from '../i18n'
import { SystemExportPanel } from './diagnostics/SystemExportPanel'

/**
 * System → Diagnostics (Admin): ONE place to export diagnostics. The whole-system package already contains the PLC folder (the PLC diagnostic export, unchanged) and can be narrowed to
 * the PLC module, to one PLC, to a task or to a correlation id. Read-only; the API enforces the Admin role. (The separate PLC-only export of CR-013 was removed from this screen by CR-022;
 * its backend endpoint is kept.)
 */
export function DiagnosticsPage() {
  const t = useT()
  const plcs = useApi(useCallback(() => api.plcConnections(), []))
  return (
    <>
      <PageHeader title={t('diagnostics.title')} description={t('diagnostics.description')} />
      <SystemExportPanel plcs={plcs.data ?? []} />
    </>
  )
}
