/**
 * CR-061: where the cards of the location map are drawn (the shop floor drawing). Display only: which locations exist, their racks, capacity and state come from the API.
 * A planned code that does not exist leaves its cell empty; a location that is not in the plan is listed below the drawing, so nothing is ever hidden.
 */
export const FLOOR_ZONES = [
  { area: 'cnc-a', title: 'inventory.zoneCnc', codes: ['A1', 'A2', 'A3', 'A4'] },
  { area: 'qc-bd', title: 'inventory.zoneQc', codes: ['B1', 'B2', 'D1', 'D2'] },
  { area: 'qc-eg', title: 'inventory.zoneQc', codes: ['E1', 'E2', 'G1', 'G2'] },
  { area: 'cnc-x', title: 'inventory.zoneCnc', codes: ['X1', 'X2', 'X3', 'X4'] },
  { area: 'buffer', title: 'inventory.zoneBuffer', codes: ['P1', 'P2', 'P3', 'P4'] },
  { area: 'manual-qc', title: 'inventory.zoneManualQc', codes: ['M5', 'M6'] },
] as const

/** Cards per row inside a zone. */
const COLUMNS = 2

export function floorPlan<T extends { code: string }>(locations: T[]) {
  const byCode = new Map(locations.map((l) => [l.code.toUpperCase(), l]))
  const placed = new Set<T>()
  const zones = FLOOR_ZONES.map((z) => ({
    area: z.area, title: z.title,
    cells: z.codes.flatMap((code, i) => {
      const location = byCode.get(code)
      if (!location) return []
      placed.add(location)
      return [{ location, column: (i % COLUMNS) + 1, row: Math.floor(i / COLUMNS) + 1 }]
    }),
  }))
  return { zones: zones.filter((z) => z.cells.length > 0), others: locations.filter((l) => !placed.has(l)) }
}
