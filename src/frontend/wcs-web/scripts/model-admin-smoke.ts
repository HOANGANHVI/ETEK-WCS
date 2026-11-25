// Product model administration page (CR-036 A.3): validation before sending, create / edit / enable-disable against a FAKE api (what is sent, what is not), the server's refusals in words,
// the effect of every outcome (only a success reloads the list), and the first render of every dialog. No backend, no DOM. Run: npm run modeladmin:test
const store = new Map<string, string>()
;(globalThis as Record<string, unknown>).window = { localStorage: { getItem: (k: string) => store.get(k) ?? null, setItem: (k: string, v: string) => { store.set(k, v) } } }
;(globalThis as Record<string, unknown>).document = { documentElement: { lang: '' } }
const results: string[] = []
const check = (name: string, ok: boolean, info = '') => { results.push(`${ok ? 'PASS' : 'FAIL'} ${name}${ok || !info ? '' : ' - ' + info}`); if (!ok) process.exitCode = 1 }

import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { ApiRequestError } from '../src/api/errors.ts'
import type { ProductModelInfo } from '../src/api/configTransferTypes.ts'
import { effectOf, normalizeCode, submitCreate, submitEdit, submitToggle, validateModelForm, type ModelApi } from '../src/lib/modelAdmin.ts'
import { ModelsManager } from '../src/pages/transfer/ModelsManager.tsx'
const i18n = await import('../src/i18n/index.ts')
const vi = await import('../src/i18n/vi.ts')
const T = (k: string, p?: Record<string, string | number>) => (i18n.t as (k: string, p?: Record<string, string | number>) => string)(k, p)

const A: ProductModelInfo = { id: 'id-a', code: 'F-BAL-10R80', name: 'F-Bal 10R80', enabled: true }
const B: ProductModelInfo = { id: 'id-b', code: 'OLD', name: 'Retired model', enabled: false }

/** A fake server: records every call; can be told to refuse. */
function fakeApi(refuse?: { code: string; status: number }) {
  const calls: { op: string; args: unknown[] }[] = []
  const fail = () => { if (refuse) throw new ApiRequestError(refuse.status, { code: refuse.code, message: 'server text' }) }
  const api: ModelApi = {
    create: async (code, name) => { calls.push({ op: 'create', args: [code, name] }); fail(); return { id: 'new', code, name, enabled: true } },
    update: async (id, name, enabled) => { calls.push({ op: 'update', args: [id, name, enabled] }); fail(); return { id, code: id === A.id ? A.code : B.code, name, enabled } },
  }
  return { api, calls }
}

// ---------------------------------------------------------------- validation before sending (limits of the server DTOs)
check('code is normalised the way the server does (trim, upper case)', normalizeCode('  f-bal.10r80 ') === 'F-BAL.10R80')
check('create: an empty code and an empty name are refused', validateModelForm({ code: '  ', name: '' }, [], true).code === 'required' && validateModelForm({ code: '  ', name: '' }, [], true).name === 'required')
check('create: a code over 40 characters is refused, 40 is allowed', validateModelForm({ code: 'A'.repeat(41), name: 'n' }, [], true).code === 'tooLong' && validateModelForm({ code: 'A'.repeat(40), name: 'n' }, [], true).code === undefined)
check('create: characters a configuration package would reject are refused', ['A B', 'A/B', 'Ä', 'A,B', 'A;B'].every((c) => validateModelForm({ code: c, name: 'n' }, [], true).code === 'format') && ['A.B', 'A-B', 'A_B', 'a1'].every((c) => validateModelForm({ code: c, name: 'n' }, [], true).code === undefined))
check('create: a code that is already in the list is refused (any case); the server still has the last word', validateModelForm({ code: 'f-bal-10r80', name: 'n' }, ['F-BAL-10R80'], true).code === 'exists')
check('a name over 200 characters is refused, 200 is allowed', validateModelForm({ code: 'X', name: 'n'.repeat(201) }, [], true).name === 'tooLong' && validateModelForm({ code: 'X', name: 'n'.repeat(200) }, [], true).name === undefined)
check('edit: the code is not validated at all (it cannot change), the name is', validateModelForm({ code: '', name: 'ok' }, [], false).code === undefined && validateModelForm({ code: 'x', name: ' ' }, [], false).name === 'required')

// ---------------------------------------------------------------- create
{
  const { api, calls } = fakeApi()
  const o = await submitCreate(api, { code: ' f-pis-10r80 ', name: '  F-Pis 10R80 ' }, ['F-BAL-10R80'])
  check('create succeeds: ONE call with the normalised code and trimmed name', o.ok && calls.length === 1 && calls[0].op === 'create' && calls[0].args.join('|') === 'F-PIS-10R80|F-Pis 10R80')
}
{
  const { api, calls } = fakeApi()
  const o = await submitCreate(api, { code: 'bad code', name: '' }, [])
  check('an invalid form sends nothing and reports both problems', !o.ok && o.kind === 'invalid' && o.errors.code === 'format' && o.errors.name === 'required' && calls.length === 0)
}
{
  const { api, calls } = fakeApi({ code: 'MODEL_CODE_EXISTS', status: 409 })
  const o = await submitCreate(api, { code: 'NEW', name: 'New' }, [])
  check('a server refusal comes back as a failure with the original error, sent exactly once (no retry)', !o.ok && o.kind === 'failed' && o.error instanceof ApiRequestError && (o.error as ApiRequestError).code === 'MODEL_CODE_EXISTS' && calls.length === 1)
  const eff = effectOf(o, vi.describeError)
  check('the refusal is shown in words, the dialog stays open and the list is NOT reloaded', !eff.close && !eff.reload && eff.serverError === 'Mã mô hình sản phẩm đã tồn tại.')
  i18n.setLang('en'); const en = effectOf(o, vi.describeError); i18n.setLang('vi')
  check('the refusal is also translated into English', !en.close && en.serverError === 'The product model code already exists.')
}
for (const [code, status] of [['MODEL_CODE_INVALID', 400], ['MODEL_NAME_REQUIRED', 400], ['MODEL_NOT_FOUND', 404], ['MODEL_DISABLED', 422], ['VALIDATION_FAILED', 400]] as const) {
  const { api } = fakeApi({ code, status })
  const o = await submitCreate(api, { code: 'NEW', name: 'New' }, [])
  const eff = effectOf(o, vi.describeError)
  check(`server error ${code} is shown as text, not as a code`, !eff.close && typeof eff.serverError === 'string' && eff.serverError.length > 5 && !eff.serverError.includes(code))
}
{
  const { api } = fakeApi({ code: 'NETWORK_ERROR', status: 0 })
  const eff = effectOf(await submitCreate(api, { code: 'NEW', name: 'New' }, []), vi.describeError)
  check('a network failure is reported as such and changes nothing', !eff.close && !eff.reload && eff.serverError === i18n.t('common.networkError'))
}

// ---------------------------------------------------------------- edit
{
  const { api, calls } = fakeApi()
  const o = await submitEdit(api, A, '  F-Bal 10R80 (rev.)  ')
  check('edit sends the id, the trimmed name and the CURRENT enabled flag; never the code', o.ok && calls.length === 1 && calls[0].op === 'update' && calls[0].args.join('|') === 'id-a|F-Bal 10R80 (rev.)|true')
  check('a successful edit closes the dialog, shows the notice and reloads the list', (() => { const e = effectOf(o, vi.describeError); return e.close && e.saved && e.reload })())
  const disabled = await submitEdit(api, B, 'Renamed')
  check('editing a disabled model keeps it disabled (the name change never enables it)', disabled.ok && calls[1].args.join('|') === 'id-b|Renamed|false')
}
{
  const { api, calls } = fakeApi()
  const o = await submitEdit(api, A, '   ')
  check('edit with an empty name sends nothing', !o.ok && o.kind === 'invalid' && o.errors.name === 'required' && calls.length === 0)
  const f = fakeApi({ code: 'MODEL_NOT_FOUND', status: 404 })
  const failed = await submitEdit(f.api, A, 'x')
  check('edit of a model that no longer exists is refused by the server and shown (one call, nothing reloaded)', !failed.ok && failed.kind === 'failed' && f.calls.length === 1 && !effectOf(failed, vi.describeError).reload)
}

// ---------------------------------------------------------------- enable / disable (never delete)
{
  const { api, calls } = fakeApi()
  const off = await submitToggle(api, A)
  const on = await submitToggle(api, B)
  check('disable = update with the stored name and enabled=false; enable = enabled=true', off.ok && on.ok && calls[0].args.join('|') === 'id-a|F-Bal 10R80|false' && calls[1].args.join('|') === 'id-b|Retired model|true')
  check('the api has only create and update: there is no way to delete a model', Object.keys(api).sort().join() === 'create,update' && calls.every((c) => c.op === 'create' || c.op === 'update'))
  const f = fakeApi({ code: 'MODEL_NOT_FOUND', status: 404 })
  const failed = await submitToggle(f.api, A)
  check('a refused toggle is a failure; the list is not reloaded', !failed.ok && failed.kind === 'failed' && !effectOf(failed, vi.describeError).reload)
}

// ---------------------------------------------------------------- first render of the page parts
const noop = { create: async () => A, update: async () => A }
const render = (props: Partial<Parameters<typeof ModelsManager>[0]> = {}) => renderToStaticMarkup(createElement(ModelsManager, { models: [A, B], api: noop, onChanged: () => {}, ...props }))
const buttons = (html: string) => [...html.matchAll(/<button[^>]*>([^<]*)<\/button>/g)].map((m) => m[1])

const list = render()
check('list: code, name and state of every model', list.includes('F-BAL-10R80') && list.includes('F-Bal 10R80') && list.includes('Retired model') && list.includes(T('transfer.modelsOn')) && list.includes(T('transfer.modelsOff')))
check('list: Edit on each row, Disable for an enabled model, Enable for a disabled one', buttons(list).filter((b) => b === T('transfer.mmEdit')).length === 2 && buttons(list).includes(T('transfer.mmDisable')) && buttons(list).includes(T('transfer.mmEnable')))
check('list: no delete action exists and the page says models are only enabled or disabled', !buttons(list).some((b) => /xóa|delete|remove/i.test(b)) && list.includes(T('transfer.mmNoDelete')))
check('list: there is no description column (the server has no such field)', !/mô tả|description/i.test(list.match(/<thead>[\s\S]*?<\/thead>/)![0]))
check('empty list: the empty state and the Add button', (() => { const h = render({ models: [] }); return h.includes(T('transfer.modelsEmpty')) && buttons(h).includes(T('transfer.mmAdd')) })())

const create = render({ initial: { dialog: { kind: 'create' } } })
check('create dialog: editable code and name, hint, Save', create.includes('role="dialog"') && create.includes(T('transfer.mmFormCreate')) && create.includes(T('transfer.mmCodeHint')) && !create.includes('disabled=""') && buttons(create).includes(T('transfer.mmSave')))
const bad = render({ initial: { dialog: { kind: 'create' }, form: { code: 'x y', name: '' }, errors: validateModelForm({ code: 'x y', name: '' }, [], true) } })
check('create dialog with problems: each problem is shown next to its field', bad.includes(T('transfer.mmCodeFormat')) && bad.includes(T('transfer.mmNameRequired')))
const dup = render({ initial: { dialog: { kind: 'create' }, errors: { code: 'exists' } } })
check('duplicate code problem is worded', dup.includes(T('transfer.mmCodeExists')))
const refused = render({ initial: { dialog: { kind: 'create' }, serverError: 'The product model code already exists.' } })
check('a server error is shown as an alert inside the dialog', refused.includes('notice-bad') && refused.includes('The product model code already exists.'))
const edit = render({ initial: { dialog: { kind: 'edit', model: A }, form: { code: A.code, name: A.name } } })
check('edit dialog: the code is shown but cannot be changed; the name is editable', edit.includes(T('transfer.mmFormEdit', { code: A.code })) && /<input[^>]*readOnly=""[^>]*disabled=""[^>]*value="F-BAL-10R80"/.test(edit) && edit.includes(T('transfer.mmCodeFixed')) && edit.includes('value="F-Bal 10R80"'))
const disable = render({ initial: { dialog: { kind: 'toggle', model: A } } })
check('disable asks for confirmation naming the model and says nothing is deleted', disable.includes(T('transfer.mmConfirmDisable', { code: A.code })) && buttons(disable).includes(T('transfer.mmDisable')))
const enable = render({ initial: { dialog: { kind: 'toggle', model: B } } })
check('enable asks for confirmation naming the model', enable.includes(T('transfer.mmConfirmEnable', { code: B.code })))

// ---------------------------------------------------------------- wording
const KEYS = ['mmAdd', 'mmEdit', 'mmEnable', 'mmDisable', 'mmColActions', 'mmNoDelete', 'mmFormCreate', 'mmFormEdit', 'mmCode', 'mmCodeHint', 'mmCodeFixed', 'mmName', 'mmSave', 'mmSaved', 'mmCodeRequired',
  'mmCodeTooLong', 'mmCodeFormat', 'mmCodeExists', 'mmNameRequired', 'mmNameTooLong', 'mmConfirmDisable', 'mmConfirmEnable', 'modelsTitle', 'modelsHint'].map((k) => 'transfer.' + k)
const P = { code: 'X', max: 40 }
i18n.setLang('vi'); const viT = KEYS.map((k) => T(k, P)); i18n.setLang('en'); const enT = KEYS.map((k) => T(k, P)); i18n.setLang('vi')
check('every key has a Vietnamese and an English text, different from each other and from the key', KEYS.every((k, i) => viT[i] !== k && enT[i] !== k && viT[i] !== enT[i] && viT[i].length > 1 && enT[i].length > 1))
check('limits shown in the messages are the real ones (40 / 200)', T('transfer.mmCodeTooLong', { max: 40 }).includes('40') && T('transfer.mmNameTooLong', { max: 200 }).includes('200'))

console.log(results.join('\n'))
