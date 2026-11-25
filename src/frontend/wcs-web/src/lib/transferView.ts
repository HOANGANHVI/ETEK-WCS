// CR-036 (A.2) – display rules of the configuration transfer page that need no DOM (and are tested without one): `npm run transfer:test`.
import type { FieldChange, PlanItem, TransferGroup, TransferGroupId } from '../api/configTransferTypes'

/** The optional fields a package may carry for product models. A package without them leaves the model data of this system untouched (the server decides; the page never fills them in). */
export const MODEL_FIELDS = ['productModelCode', 'stationRole'] as const

/** Display order = the order the server applies them: models first, then locations, then the groups that use locations. Unknown groups (a newer server) come last, in the order received. */
const ORDER: string[] = ['ProductModel', 'Location', 'Agv', 'PlcStation', 'BusinessProcess']
const rank = (id: string) => { const i = ORDER.indexOf(id); return i < 0 ? ORDER.length : i }
export const sortGroups = <T extends { id: string }>(groups: readonly T[]): T[] => groups.map((g, i) => ({ g, i })).sort((a, b) => rank(a.g.id) - rank(b.g.id) || a.i - b.i).map((x) => x.g)

/** Identity of one record in the selection (group + natural key). */
export const refKey = (g: string, k: string) => `${g}/${k}`

export const isModelField = (field: string): boolean => (MODEL_FIELDS as readonly string[]).includes(field)

/** The part of a plan item that concerns the product model / station role (empty for a record that comes from an old package). */
export const modelFieldChanges = (item: PlanItem): FieldChange[] => item.changes.filter((c) => isModelField(c.field))

/**
 * True when the selected groups can refer to product models (locations, business processes) but the product model group does not travel with them although this system has models:
 * the package would name models the target may not have. The server does not force the group (it is not a dependency of the older groups), so the page only advises.
 */
export function needsModelHint(selected: readonly TransferGroupId[], groups: readonly TransferGroup[]): boolean {
  const models = groups.find((g) => g.id === 'ProductModel')
  if (!models || models.targetCount === 0 || selected.includes('ProductModel')) return false
  return selected.includes('Location') || selected.includes('BusinessProcess')
}
