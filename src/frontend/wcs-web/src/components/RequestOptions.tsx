import type { StartableProcess } from '../api/routingTypes'
import type { FlowOption } from '../api/types'
import { useT } from '../i18n'
import { flowInfo } from '../i18n/vi'
import { processValue } from '../lib/requestOptions'

/**
 * CR-041: THE option list of every request form (task management, RCS operations, multi-leg dispatch). Both groups come from the server: the Phase 1 flows (they create a task) and
 * every published process of the database (it creates a transport order; one that accepts no request now is listed and marked). No process code or station is written here.
 */
export function RequestOptions({ flows, processes }: { flows: FlowOption[]; processes: StartableProcess[] }) {
  const t = useT()
  return (
    <>
      <optgroup label={t('ops.createGroupFlows')}>
        {flows.map((f) => <option key={f.flow} value={f.flow}>{flowInfo(f.flow).code} · {flowInfo(f.flow).label}</option>)}
      </optgroup>
      {processes.length > 0 && (
        <optgroup label={t('ops.createGroupProcesses')}>
          {processes.map((p) => <option key={p.code} value={processValue(p.code)}>{p.code} · {p.name}{p.acceptsRequests ? '' : ` (${t('ops.createNotReadyTag')})`}</option>)}
        </optgroup>
      )}
    </>
  )
}
