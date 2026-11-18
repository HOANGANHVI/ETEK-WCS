import { useCallback, useState } from 'react'
import { api } from '../../api/client'
import type { BulkApprovalItem, BulkApprovalResult } from '../../api/plcTypes'
import { Async, Button, Pill } from '../../components/ui'
import { Modal } from '../../components/Modal'
import { useApi } from '../../hooks/useApi'
import { useT } from '../../i18n'
import { describeError } from '../../i18n/vi'

/**
 * Bulk READ approval of one commissioning PLC. Step 1 shows what would happen (nothing is changed); step 2, after the Admin confirms, runs the existing
 * approval for every eligible point and shows what really happened, checked against the database. It never writes to the PLC and never changes an address.
 */
export function BulkApprovalDialog({ plcId, onClose, onDone }: { plcId: string; onClose: () => void; onDone: (message: string) => void }) {
  const t = useT()
  const preview = useApi(useCallback(() => api.plcBulkApprovalPreview(plcId), [plcId]))
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<BulkApprovalResult | null>(null)

  const run = async (expectedEligible: number) => {
    setBusy(true); setError(null)
    try { setResult(await api.plcBulkApprove(plcId, expectedEligible)) } catch (e) { setError(describeError(e)) } finally { setBusy(false) }
  }
  const finish = () => (result ? onDone(t('plc.bulkDone', { approved: result.approved, pollable: result.verifiedPollable })) : onClose())
  const reasons = (items: BulkApprovalItem[], outcome: string) => items.filter((i) => i.outcome === outcome)

  return (
    <Modal title={t('plc.bulkTitle', { plc: plcId })} onClose={finish} footer={<Button onClick={finish}>{t('plc.close')}</Button>}>
      <p className="dialog-hint">{t('plc.bulkHint')}</p>
      {error && <div className="notice notice-bad" role="alert">{error}</div>}
      {!result && (
        <Async state={preview} isEmpty={() => false} empty={{ title: '' }}>
          {(p) => (
            <>
              <ul>
                <li>{t('plc.bulkTotal', { n: p.total })}</li>
                <li>{t('plc.bulkWithAddress', { n: p.withAddress })}</li>
                <li><strong>{t('plc.bulkEligible', { n: p.eligible })}</strong></li>
                <li>{t('plc.bulkNotEligible', { n: p.notEligible })}</li>
                <li>{t('plc.bulkAlready', { n: p.alreadyApproved })}</li>
              </ul>
              <p className="muted">{t('plc.bulkScope')}</p>
              {p.notEligible > 0 && p.withAddress > 0 && (
                <details><summary>{t('plc.bulkWhyNot')}</summary>
                  <ul>{reasons(p.items, 'NOT_ELIGIBLE').filter((i) => i.address).map((i) => <li key={i.pointId}>{i.stationId}/{i.signalName} ({i.address}): {i.code}</li>)}</ul>
                </details>
              )}
              <Button variant="primary" disabled={busy || p.eligible === 0} onClick={() => void run(p.eligible)}>{busy ? t('plc.bulkRunning') : t('plc.bulkConfirm', { n: p.eligible })}</Button>
            </>
          )}
        </Async>
      )}
      {result && (
        <>
          <div className={`notice ${result.failed === 0 ? 'notice-ok' : 'notice-warn'}`} role="status">
            {t('plc.bulkResult', { approved: result.approved, already: result.alreadyApproved, notEligible: result.notEligible, failed: result.failed, pollable: result.verifiedPollable })}
          </div>
          {result.failed > 0 && (
            <ul>{reasons(result.items, 'FAILED').map((i) => <li key={i.pointId}>{i.stationId}/{i.signalName}: {i.code} <Pill tone="bad" dot={false}>{t('plc.bulkFailed')}</Pill></li>)}</ul>
          )}
        </>
      )}
    </Modal>
  )
}
