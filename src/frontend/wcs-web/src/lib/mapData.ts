import type { MapDataRow, MapDataSet } from '../api/rcsMapTypes'

/** CR-067: pure helpers of the Excel map data. */
export const MAP_DATA_STATUSES = ['Matched', 'OtherMap', 'NoPoint', 'Duplicate', 'Ambiguous', 'Invalid'] as const

export const countByStatus = (data: MapDataSet) => MAP_DATA_STATUSES.map((status) => ({ status, n: data.rows.filter((r) => r.status === status).length }))

export const rowsByPoint = (data: MapDataSet | null | undefined): Map<number, MapDataRow> =>
  new Map((data?.rows ?? []).filter((r) => r.pointId !== null).map((r) => [r.pointId as number, r]))

/** The call code of a row, only when it names something (the export repeats the Map Data ID for unnamed points). */
export const callName = (r: MapDataRow | undefined | null): string | null => (r?.callCode && r.callCode !== r.mapDataId ? r.callCode : null)

/** CR-068: how one point of the XML stands against the Excel file. */
export type PointMapping = 'Matched' | 'Duplicate' | 'Unmatched'
export function pointMapping(data: MapDataSet, pointId: number): PointMapping {
  if (data.rows.some((r) => r.pointId === pointId)) return 'Matched'
  return data.rows.some((r) => r.conflictPointId === pointId) ? 'Duplicate' : 'Unmatched'
}

/** What shows that the two files may not be of the same version: rows of this map's QR code without a point, and points without a row. */
export function mismatches(data: MapDataSet, points: { id: number }[]) {
  const attached = new Set(data.rows.filter((r) => r.pointId !== null).map((r) => r.pointId))
  const rows = data.rows.filter((r) => r.status === 'NoPoint' || r.status === 'Duplicate' || r.status === 'Ambiguous' || r.status === 'Invalid').length
  const without = points.filter((p) => !attached.has(p.id)).length
  return { rows, points: without, any: rows > 0 || without > 0 }
}

/** The documented data types get their own colour; any other text is kept and drawn as "other". Passing points stay small and plain. */
const KINDS: Record<string, string> = { 'working area': 'working', 'rotating area': 'rotating', 'swerving area': 'swerving', 'parking point': 'parking', 'charging area': 'charging', 'passing point': 'passing' }
export const kindOf = (dataType: string | null | undefined): string | null => (dataType ? KINDS[dataType.trim().toLowerCase()] ?? 'other' : null)

/** Call codes shared by several matched rows (reported, never resolved by guessing). */
export function repeatedCallCodes(data: MapDataSet): string[] {
  const seen = new Map<string, number>()
  for (const r of data.rows) { const c = r.status === 'Matched' ? callName(r) : null; if (c) seen.set(c, (seen.get(c) ?? 0) + 1) }
  return [...seen].filter(([, n]) => n > 1).map(([c]) => c)
}
