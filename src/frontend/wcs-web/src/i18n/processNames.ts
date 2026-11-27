import { pickLang } from './core'

/**
 * CR-054: the names of the reference processes are stored in the database in ONE language. They are shown in the chosen language here. Only a name that is still exactly a delivered one is
 * translated: a process the operator created or renamed is shown as stored. Nothing is written back.
 */
type Pair = readonly [vi: string, en: string]
const A_FULL: Pair = ['A1/A2 có rack đầy', 'A1/A2 has a full rack']
const A_NEEDS_EMPTY: Pair = ['A1/A2 cần rack rỗng', 'A1/A2 needs an empty rack']
const B_EMPTY: Pair = ['B1/B2 có rack rỗng', 'B1/B2 has an empty rack']
const B_NEEDS_FULL: Pair = ['B1/B2 cần rack đầy', 'B1/B2 needs a full rack']
const phase2 = ([vi, en]: Pair): Pair => [`Giai đoạn 2: ${vi}`, `Phase 2: ${en}`]

/** Per code: every delivered name. PH2-01..06 were delivered under two sets of names (earlier installations, and the current reference file). */
const REFERENCE: Record<string, readonly Pair[]> = {
  'BP-01': [A_FULL],
  'BP-02': [A_NEEDS_EMPTY],
  'BP-03': [B_EMPTY],
  'BP-04': [B_NEEDS_FULL],
  'BP-05': [['F5 yêu cầu rack đầy', 'F5 requests a full rack']],
  'BP-06': [['F5 trả rack rỗng', 'F5 returns an empty rack']],
  'PH2-01': [phase2(A_FULL), ['A1/A2 có rack đầy → B1/B2, P1/P2, M5', 'A1/A2 has a full rack → B1/B2, P1/P2, M5']],
  'PH2-02': [phase2(A_NEEDS_EMPTY), A_NEEDS_EMPTY],
  'PH2-03': [phase2(B_EMPTY), ['B1/B2 có rack rỗng → A1/A2, P1/P2, M5', 'B1/B2 has an empty rack → A1/A2, P1/P2, M5']],
  'PH2-04': [phase2(B_NEEDS_FULL), B_NEEDS_FULL],
  'PH2-05': [phase2(['trả rack rỗng từ M5', 'return an empty rack from M5']), ['Trả rack rỗng từ M5 (chưa công bố)', 'Return an empty rack from M5 (not published)']],
  'PH2-06': [phase2(['đưa rack đầy đến M5', 'bring a full rack to M5']), ['Yêu cầu rack đầy tới M5', 'Request a full rack to M5']],
  'PH2-07': [['X1/X2 có rack đầy → Y1/Y2, P3/P4, M6', 'X1/X2 has a full rack → Y1/Y2, P3/P4, M6']],
  'PH2-08': [['X1/X2 cần rack rỗng từ buffer', 'X1/X2 needs an empty rack from the buffer']],
  'PH2-09': [['Y1/Y2 có rack rỗng → X1/X2, P3/P4, M6', 'Y1/Y2 has an empty rack → X1/X2, P3/P4, M6']],
  'PH2-10': [['Y1/Y2 cần rack đầy từ buffer', 'Y1/Y2 needs a full rack from the buffer']],
  'PH2-11': [['X3/X4 có rack đầy → Z1/Z2', 'X3/X4 has a full rack → Z1/Z2']],
  'PH2-12': [['Z1/Z2 có rack rỗng → X3/X4', 'Z1/Z2 has an empty rack → X3/X4']],
}

export function processName(code: string, name: string): string {
  const known = REFERENCE[code]?.find((pair) => pair.includes(name))
  return known ? pickLang(known[0], known[1]) : name
}
