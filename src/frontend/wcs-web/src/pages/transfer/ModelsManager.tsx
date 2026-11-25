import { useState } from 'react'
import type { ProductModelInfo } from '../../api/configTransferTypes'
import { Modal } from '../../components/Modal'
import { Button, EmptyState, Pill } from '../../components/ui'
import { useT } from '../../i18n'
import { describeError } from '../../i18n/vi'
import { effectOf, MODEL_CODE_MAX, MODEL_NAME_MAX, submitCreate, submitEdit, submitToggle, type ModelApi, type ModelForm, type ModelFormErrors, type Outcome } from '../../lib/modelAdmin'

type Dialog = { kind: 'create' } | { kind: 'edit'; model: ProductModelInfo } | { kind: 'toggle'; model: ProductModelInfo }

/**
 * Product model administration (Admin): list, create, rename, enable / disable. The server decides and validates; the page checks the form first (same limits) and shows the server's answer as it is.
 * `initial` only exists so the first render of a dialog can be tested without a browser.
 */
export function ModelsManager({ models, api, onChanged, initial }: {
  models: ProductModelInfo[]; api: ModelApi; onChanged: () => void; initial?: { dialog: Dialog; form?: ModelForm; errors?: ModelFormErrors; serverError?: string }
}) {
  const t = useT()
  const [dialog, setDialog] = useState<Dialog | null>(initial?.dialog ?? null)
  const [form, setForm] = useState<ModelForm>(initial?.form ?? { code: '', name: '' })
  const [errors, setErrors] = useState<ModelFormErrors>(initial?.errors ?? {})
  const [serverError, setServerError] = useState<string | null>(initial?.serverError ?? null)
  const [busy, setBusy] = useState(false)
  const [saved, setSaved] = useState(false)

  const open = (d: Dialog) => {
    setDialog(d); setErrors({}); setServerError(null); setSaved(false)
    setForm(d.kind === 'edit' ? { code: d.model.code, name: d.model.name } : { code: '', name: '' })
  }
  const close = () => { setDialog(null); setErrors({}); setServerError(null) }
  const finish = (o: Outcome) => {
    const e = effectOf(o, describeError)
    if (e.close) { setDialog(null); setSaved(true) }
    if (!e.close && e.errors) setErrors(e.errors)
    if (!e.close && e.serverError) setServerError(e.serverError)   // the server's refusal, in words (nothing is hidden or retried)
    if (e.reload) onChanged()
  }
  const run = async (f: () => Promise<Outcome>) => {
    setBusy(true); setServerError(null)
    try { finish(await f()) } finally { setBusy(false) }
  }
  const save = () => {
    if (!dialog) return
    if (dialog.kind === 'create') void run(() => submitCreate(api, form, models.map((m) => m.code)))
    else if (dialog.kind === 'edit') void run(() => submitEdit(api, dialog.model, form.name))
    else void run(() => submitToggle(api, dialog.model))
  }
  const codeProblem = errors.code === undefined ? null
    : errors.code === 'required' ? t('transfer.mmCodeRequired')
    : errors.code === 'tooLong' ? t('transfer.mmCodeTooLong', { max: MODEL_CODE_MAX })
    : errors.code === 'format' ? t('transfer.mmCodeFormat')
    : t('transfer.mmCodeExists')
  const nameProblem = errors.name === undefined ? null
    : errors.name === 'required' ? t('transfer.mmNameRequired')
    : t('transfer.mmNameTooLong', { max: MODEL_NAME_MAX })

  return (
    <div className="stack">
      {saved && <div className="notice notice-ok" role="status">{t('transfer.mmSaved')}</div>}
      <div className="btn-row">
        <Button variant="primary" size="sm" onClick={() => open({ kind: 'create' })}>{t('transfer.mmAdd')}</Button>
        <span className="muted small">{t('transfer.mmNoDelete')}</span>
      </div>
      {models.length === 0 ? <EmptyState title={t('transfer.modelsEmpty')} /> : (
        <div className="table-wrap">
          <table className="grid">
            <thead><tr><th>{t('transfer.modelsColCode')}</th><th>{t('transfer.modelsColName')}</th><th>{t('transfer.modelsColState')}</th><th>{t('transfer.mmColActions')}</th></tr></thead>
            <tbody>
              {models.map((m) => (
                <tr key={m.id}>
                  <td><code>{m.code}</code></td><td>{m.name}</td>
                  <td><Pill tone={m.enabled ? 'ok' : 'muted'}>{m.enabled ? t('transfer.modelsOn') : t('transfer.modelsOff')}</Pill></td>
                  <td><div className="btn-row">
                    <Button size="sm" onClick={() => open({ kind: 'edit', model: m })}>{t('transfer.mmEdit')}</Button>
                    <Button size="sm" onClick={() => open({ kind: 'toggle', model: m })}>{m.enabled ? t('transfer.mmDisable') : t('transfer.mmEnable')}</Button>
                  </div></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {dialog && dialog.kind !== 'toggle' && (
        <Modal title={dialog.kind === 'create' ? t('transfer.mmFormCreate') : t('transfer.mmFormEdit', { code: dialog.model.code })} onClose={close}
          footer={<><Button onClick={close}>{t('common.close')}</Button><Button variant="primary" disabled={busy} onClick={save}>{t('transfer.mmSave')}</Button></>}>
          {serverError && <div className="notice notice-bad" role="alert">{serverError}</div>}
          <label className="field"><span>{t('transfer.mmCode')}</span>
            <input value={form.code} maxLength={MODEL_CODE_MAX + 10} readOnly={dialog.kind === 'edit'} disabled={dialog.kind === 'edit'}
              onChange={(e) => setForm({ ...form, code: e.target.value })} />
            {codeProblem ? <span className="field-error" role="alert">{codeProblem}</span> : <span className="field-hint">{dialog.kind === 'edit' ? t('transfer.mmCodeFixed') : t('transfer.mmCodeHint')}</span>}
          </label>
          <label className="field"><span>{t('transfer.mmName')}</span>
            <input value={form.name} maxLength={MODEL_NAME_MAX + 20} onChange={(e) => setForm({ ...form, name: e.target.value })} />
            {nameProblem && <span className="field-error" role="alert">{nameProblem}</span>}
          </label>
        </Modal>
      )}

      {dialog?.kind === 'toggle' && (
        <Modal title={dialog.model.enabled ? t('transfer.mmDisable') : t('transfer.mmEnable')} onClose={close}
          footer={<><Button onClick={close}>{t('common.close')}</Button><Button variant="primary" disabled={busy} onClick={save}>{dialog.model.enabled ? t('transfer.mmDisable') : t('transfer.mmEnable')}</Button></>}>
          {serverError && <div className="notice notice-bad" role="alert">{serverError}</div>}
          <p>{dialog.model.enabled ? t('transfer.mmConfirmDisable', { code: dialog.model.code }) : t('transfer.mmConfirmEnable', { code: dialog.model.code })}</p>
        </Modal>
      )}
    </div>
  )
}
