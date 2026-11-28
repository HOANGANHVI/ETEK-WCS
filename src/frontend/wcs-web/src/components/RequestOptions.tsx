import type { StartableProcess } from '../api/routingTypes'
import type { FlowOption } from '../api/types'
import { useT } from '../i18n'
import { flowInfo } from '../i18n/vi'
import { processName } from '../i18n/processNames'
import { processValue } from '../lib/requestOptions'

/**
 * CR-063: a flow or process the process settings do not offer is left out.
 * CR-041: THE option list of every request form (task management, RCS operations, multi-leg dispatch). Both groups come from the server: the Phase 1 flows (they create a task) and
 * every published process of the database (it creates a transport order; one that accepts no request now is listed and marked). No process code or station is written here.
 */
export function RequestOptions({ flows, processes }: { flows: FlowOption[]; processes: StartableProcess[] }) {
  const t = useT()
  // CR-063: only what can be requested now, as the process settings say
  const offered = flows.filter((f) => f.available !== false)
  const ready = processes.filter((p) => p.acceptsRequests)
  return (
    <>
      {offered.length === 0 && ready.length === 0 && <option value="" disabled>{t('ops.createNone')}</option>}
      {offered.length > 0 && (
        <optgroup label={t('ops.createGroupFlows')}>
          {offered.map((f) => <option key={f.flow} value={f.flow}>{flowInfo(f.flow).code} · {flowInfo(f.flow).label}</option>)}
        </optgroup>
      )}
      {ready.length > 0 && (
        <optgroup label={t('ops.createGroupProcesses')}>
          {ready.map((p) => <option key={p.code} value={processValue(p.code)}>{p.code} · {processName(p.code, p.name)}</option>)}
        </optgroup>
      )}
    </>
  )
}
