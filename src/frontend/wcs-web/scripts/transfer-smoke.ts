// Configuration transfer page, product model support (CR-036 A.2): wording in both languages, group order, the advice at export, and what the preview shows for old and new packages
// and for the conflicts the server reports. Rendered with react-dom/server on hand-made data (no backend, no DOM). Run: npm run transfer:test
const store = new Map<string, string>()
;(globalThis as Record<string, unknown>).window = { localStorage: { getItem: (k: string) => store.get(k) ?? null, setItem: (k: string, v: string) => { store.set(k, v) } } }
;(globalThis as Record<string, unknown>).document = { documentElement: { lang: '' } }
const results: string[] = []
const check = (name: string, ok: boolean, info = '') => { results.push(`${ok ? 'PASS' : 'FAIL'} ${name}${ok || !info ? '' : ' - ' + info}`); if (!ok) process.exitCode = 1 }

import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import type { ImportPlan, PlanItem, ProductModelInfo, TransferGroup } from '../src/api/configTransferTypes.ts'
import { isModelField, modelFieldChanges, needsModelHint, refKey, sortGroups } from '../src/lib/transferView.ts'
import { GroupLabel, ModelExportHint } from '../src/pages/transfer/Groups.tsx'
import { ModelsManager } from '../src/pages/transfer/ModelsManager.tsx'
import { PlanView } from '../src/pages/transfer/PlanView.tsx'
const i18n = await import('../src/i18n/index.ts')
const T = (k: string, p?: Record<string, string | number>) => (i18n.t as (k: string, p?: Record<string, string | number>) => string)(k, p)
const has = (k: string) => (i18n.hasMessage as (k: string) => boolean)(k)

const item = (o: Partial<PlanItem> & Pick<PlanItem, 'group' | 'key'>): PlanItem => ({ action: 'Update', changes: [], conflictCode: null, conflictMessage: null, headerChange: false, ...o })
const plan = (items: PlanItem[]): ImportPlan => ({
  importId: 'i', groupOrder: ['ProductModel', 'Location', 'BusinessProcess'], items,
  summary: { create: items.filter((i) => i.action === 'Create').length, update: items.filter((i) => i.action === 'Update').length, unchanged: 0, skip: 0, conflict: items.filter((i) => i.action === 'Conflict').length },
  warnings: [], readiness: { maintenanceOn: true, backupReady: true, backupProblem: null, blockers: [], canApply: true }, planHash: 'h', previewToken: 'p', previewExpiresAt: null,
})
const view = (p: ImportPlan) => renderToStaticMarkup(createElement(PlanView, { plan: p, stale: false, excluded: [], onToggle: () => {}, known: [] }))
const group = (id: TransferGroup['id'], targetCount = 1, dependsOn: TransferGroup['dependsOn'] = []): TransferGroup => ({ id, file: id + '.json', dependsOn, updateOnly: false, targetCount })

// ---------------------------------------------------------------- wording (both languages)
const KEYS = ['group_ProductModel', 'groupDesc_ProductModel', 'groupDesc_Location', 'groupDesc_BusinessProcess', 'field_productModelCode', 'field_stationRole', 'modelChangeBadge', 'modelsTitle', 'modelsHint',
  'modelsEmpty', 'modelsColCode', 'modelsColName', 'modelsColState', 'modelsOn', 'modelsOff', 'exportModelHint',
  'conflict_MISSING_DEPENDENCY', 'conflict_LOCATION_HAS_RACKS', 'conflict_LOCATION_REFERENCED_BY_PUBLISHED', 'conflict_PROCESS_MODEL_LOCKED'].map((k) => 'transfer.' + k)
for (const lang of ['vi', 'en'] as const) {
  i18n.setLang(lang)
  check(`${lang}: every new key exists and reads as text, not as its key`, KEYS.every((k) => has(k) && T(k).length >= 2 && T(k) !== k), KEYS.filter((k) => !has(k) || T(k) === k || T(k).length < 2).join())
}
i18n.setLang('vi'); const viTexts = KEYS.map((k) => T(k)); i18n.setLang('en'); const enTexts = KEYS.map((k) => T(k)); i18n.setLang('vi')
check('the two languages differ for every new key', KEYS.every((_, i) => viTexts[i] !== enTexts[i]))
check('product model group is named', T('transfer.group_ProductModel').includes('Product Model') && T('transfer.group_Location') !== '')

// ---------------------------------------------------------------- group order and the advice at export
const server = [group('BusinessProcess', 2, ['Location', 'PlcStation']), group('Agv'), group('PlcStation', 1, ['Location']), group('Location'), group('ProductModel', 2)]
check('groups show in the order the server applies them (models first)', sortGroups(server).map((g) => g.id).join() === 'ProductModel,Location,Agv,PlcStation,BusinessProcess')
check('an unknown (newer) group goes last and the order of equals is kept', sortGroups([{ id: 'Zeta' }, { id: 'Location' }, { id: 'Alpha' }]).map((g) => g.id).join() === 'Location,Zeta,Alpha')
check('an old server without the model group is still ordered', sortGroups(server.filter((g) => g.id !== 'ProductModel')).map((g) => g.id).join() === 'Location,Agv,PlcStation,BusinessProcess')
check('advice: locations selected without models, and this system has models', needsModelHint(['Location'], server) && needsModelHint(['BusinessProcess', 'Agv'], server))
check('no advice when the model group travels along', !needsModelHint(['Location', 'ProductModel'], server))
check('no advice for groups that never carry a model', !needsModelHint(['Agv', 'PlcStation'], server))
check('no advice when this system has no model, or the server has no model group', !needsModelHint(['Location'], [group('ProductModel', 0), group('Location')]) && !needsModelHint(['Location'], server.filter((g) => g.id !== 'ProductModel')))
const hint = renderToStaticMarkup(createElement(ModelExportHint))
check('the advice is shown as a note (it blocks nothing)', hint.includes('role="note"') && hint.includes(T('transfer.exportModelHint')))

// ---------------------------------------------------------------- the model list and the group card
const models: ProductModelInfo[] = [{ id: '1', code: 'F-BAL-10R80', name: 'F-Bal 10R80', enabled: true }, { id: '2', code: 'OLD', name: 'Retired', enabled: false }]
const noApi = { create: async () => models[0], update: async () => models[0] }
const list = renderToStaticMarkup(createElement(ModelsManager, { models, api: noApi, onChanged: () => {} }))
check('model list shows code, name and state', list.includes('F-BAL-10R80') && list.includes('F-Bal 10R80') && list.includes('pill-ok') && list.includes('pill-muted') && list.includes(T('transfer.modelsOn')) && list.includes(T('transfer.modelsOff')))
check('empty model list says so', renderToStaticMarkup(createElement(ModelsManager, { models: [], api: noApi, onChanged: () => {} })).includes(T('transfer.modelsEmpty')))
const card = renderToStaticMarkup(createElement(GroupLabel, { g: group('ProductModel', 3) }))
check('the product model group is listed with its description and count', card.includes(T('transfer.group_ProductModel')) && card.includes(T('transfer.groupDesc_ProductModel')) && card.includes(T('transfer.existing', { n: 3 })))
check('old groups render exactly as before (name, description, dependency)', ['Location', 'Agv', 'PlcStation', 'BusinessProcess'].every((id) => {
  const html = renderToStaticMarkup(createElement(GroupLabel, { g: group(id as TransferGroup['id'], 2, id === 'BusinessProcess' ? ['Location'] : []) }))
  return html.includes(T('transfer.group_' + id)) && html.includes(T('transfer.groupDesc_' + id)) && (id !== 'BusinessProcess' || html.includes(T('transfer.dependsOn', { groups: T('transfer.group_Location') })))
}))

// ---------------------------------------------------------------- preview: old and new packages
const oldPkg = item({ group: 'Location', key: 'A1', changes: [{ field: 'rackCapacity', from: '1', to: '4' }] })
const newPkg = item({ group: 'Location', key: 'A1', changes: [{ field: 'productModelCode', from: null, to: 'F-BAL-10R80' }, { field: 'stationRole', from: 'CncStation', to: 'AutoQcStation' }] })
check('a field change of a model is recognised, other fields are not', isModelField('productModelCode') && isModelField('stationRole') && !isModelField('rackCapacity') && !isModelField('name'))
check('an old package has no model change; a new one has both', modelFieldChanges(oldPkg).length === 0 && modelFieldChanges(newPkg).length === 2)
const oldHtml = view(plan([oldPkg]))
check('old package: only its own change is shown, no model / role line, no model badge', oldHtml.includes(T('transfer.field_rackCapacity')) && !oldHtml.includes(T('transfer.field_productModelCode')) && !oldHtml.includes(T('transfer.field_stationRole')) && !oldHtml.includes(T('transfer.modelChangeBadge')))
const newHtml = view(plan([newPkg]))
check('new package: model and role are shown with from and to, and marked', newHtml.includes(T('transfer.field_productModelCode')) && newHtml.includes('F-BAL-10R80') && newHtml.includes(T('transfer.field_stationRole')) && newHtml.includes('CncStation') && newHtml.includes('AutoQcStation') && newHtml.includes(T('transfer.modelChangeBadge')))
check('a missing model value shows as empty (∅), never as a made-up value', newHtml.includes('∅'))
const created = view(plan([item({ group: 'ProductModel', key: 'F-BAL-10R80', action: 'Create' }), item({ group: 'BusinessProcess', key: 'P', changes: [{ field: 'productModelCode', from: 'A', to: 'B' }] })]))
check('a new product model record is listed under its group; a process model change is shown', created.includes(T('transfer.group_ProductModel')) && created.includes('F-BAL-10R80') && created.includes(T('transfer.newRecord')) && created.includes(T('transfer.field_productModelCode')))
check('the selection key of a record is group/key', refKey('Location', 'A1') === 'Location/A1')

// ---------------------------------------------------------------- preview: what the server reports is shown, never hidden or fixed
const CONFLICTS: [string, TransferGroup['id']][] = [['MISSING_DEPENDENCY', 'Location'], ['LOCATION_HAS_RACKS', 'Location'], ['LOCATION_REFERENCED_BY_PUBLISHED', 'Location'], ['PROCESS_MODEL_LOCKED', 'BusinessProcess']]
for (const [code, g] of CONFLICTS) {
  const html = view(plan([item({ group: g, key: 'X1', action: 'Conflict', conflictCode: code, conflictMessage: `server says: ${code} for X1` })]))
  check(`conflict ${code}: explained, server message kept, counted as a conflict`, html.includes(T('transfer.conflict_' + code)) && html.includes(`server says: ${code} for X1`) && html.includes(T('transfer.action_Conflict')) && html.includes(`${T('transfer.sumConflict')}: 1`))
}
const unknown = view(plan([item({ group: 'Location', key: 'X1', action: 'Conflict', conflictCode: 'SOMETHING_NEW', conflictMessage: 'new rule' })]))
check('a conflict code this page does not know is shown as the code with the server text (nothing is swallowed)', unknown.includes('SOMETHING_NEW') && unknown.includes('new rule'))
const four = view(plan(CONFLICTS.map(([code, g], i) => item({ group: g, key: 'K' + i, action: 'Conflict', conflictCode: code, conflictMessage: 'm' }))))
check('every conflict row has its own inclusion checkbox (the user decides; the page skips nothing by itself)', (four.match(/type="checkbox"/g) ?? []).length === 4)
check('conflicts are never pre-excluded: the box is checked unless the user clears it', (four.match(/checked=""/g) ?? []).length === 4)

console.log(results.join('\n'))
