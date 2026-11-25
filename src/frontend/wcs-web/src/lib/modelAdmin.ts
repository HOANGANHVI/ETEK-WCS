// CR-036 (A.3) – product model administration: validation before sending and the three actions (create, edit, enable / disable), without any DOM or HTTP of its own.
// The rules mirror the server contract in the source: `ProductModelRequest` (Code required, max 40; Name required, max 200), `ProductModelUpdateRequest` (Name required, max 200; Enabled),
// the code is trimmed and UPPER-CASED by the server, and `ProductModelConfigGroup` accepts only [A-Z0-9._-] when a model travels in a configuration package. There is no description field and no delete.
import type { ProductModelInfo } from '../api/configTransferTypes'

export const MODEL_CODE_MAX = 40
export const MODEL_NAME_MAX = 200
/** The server upper-cases the code; the configuration transfer only accepts these characters, so a code outside them would later break an export / import. */
export const MODEL_CODE_PATTERN = /^[A-Z0-9._-]{1,40}$/

export interface ModelForm { code: string; name: string }
export type CodeProblem = 'required' | 'tooLong' | 'format' | 'exists'
export type NameProblem = 'required' | 'tooLong'
export interface ModelFormErrors { code?: CodeProblem; name?: NameProblem }

export const normalizeCode = (code: string): string => code.trim().toUpperCase()
export const hasErrors = (e: ModelFormErrors): boolean => e.code !== undefined || e.name !== undefined

/** `existing` = the codes already in the list (only an early hint: the server still decides and answers MODEL_CODE_EXISTS). `creating = false` leaves the code alone (it cannot be changed). */
export function validateModelForm(form: ModelForm, existing: readonly string[], creating: boolean): ModelFormErrors {
  const errors: ModelFormErrors = {}
  if (creating) {
    const code = normalizeCode(form.code)
    if (code.length === 0) errors.code = 'required'
    else if (code.length > MODEL_CODE_MAX) errors.code = 'tooLong'
    else if (!MODEL_CODE_PATTERN.test(code)) errors.code = 'format'
    else if (existing.some((c) => c.toUpperCase() === code)) errors.code = 'exists'
  }
  const name = form.name.trim()
  if (name.length === 0) errors.name = 'required'
  else if (name.length > MODEL_NAME_MAX) errors.name = 'tooLong'
  return errors
}

/** The server calls the page can make. The page passes the real client; the tests pass a fake. */
export interface ModelApi {
  create: (code: string, name: string) => Promise<ProductModelInfo>
  update: (id: string, name: string, enabled: boolean) => Promise<ProductModelInfo>
}

/** `invalid` = nothing was sent (the form is wrong). `failed` = the server answered with an error, which is passed on untouched. */
export type Outcome = { ok: true; model: ProductModelInfo } | { ok: false; kind: 'invalid'; errors: ModelFormErrors } | { ok: false; kind: 'failed'; error: unknown }

async function call(run: () => Promise<ProductModelInfo>): Promise<Outcome> {
  try { return { ok: true, model: await run() } } catch (error) { return { ok: false, kind: 'failed', error } }
}

export async function submitCreate(api: ModelApi, form: ModelForm, existing: readonly string[]): Promise<Outcome> {
  const errors = validateModelForm(form, existing, true)
  if (hasErrors(errors)) return { ok: false, kind: 'invalid', errors }
  return call(() => api.create(normalizeCode(form.code), form.name.trim()))
}

/** Edit: only the name is sent, together with the model's current state. The code is never sent (it cannot change). */
export async function submitEdit(api: ModelApi, model: ProductModelInfo, name: string): Promise<Outcome> {
  const errors = validateModelForm({ code: model.code, name }, [], false)
  if (hasErrors(errors)) return { ok: false, kind: 'invalid', errors }
  return call(() => api.update(model.id, name.trim(), model.enabled))
}

/** Enable / disable: the same update call with the stored name and the opposite flag. Nothing is ever deleted. */
export const submitToggle = (api: ModelApi, model: ProductModelInfo): Promise<Outcome> => call(() => api.update(model.id, model.name, !model.enabled))

/** What the page does with an outcome: only a success closes the dialog and tells the list to reload; an invalid form shows its problems; a server refusal is shown in words and nothing else changes. */
export type OutcomeEffect = { close: true; saved: true; reload: true } | { close: false; saved: false; reload: false; errors?: ModelFormErrors; serverError?: string }
export function effectOf(o: Outcome, describe: (error: unknown) => string): OutcomeEffect {
  if (o.ok) return { close: true, saved: true, reload: true }
  if (o.kind === 'invalid') return { close: false, saved: false, reload: false, errors: o.errors }
  return { close: false, saved: false, reload: false, serverError: describe(o.error) }
}
