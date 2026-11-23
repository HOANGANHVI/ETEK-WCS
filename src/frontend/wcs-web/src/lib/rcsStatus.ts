/**
 * The ONE place that classifies the AMR `status` of the RCS queryAgvStatus answer (data[].status, RCS-2000 API Developer Guide V3.3, Appendix B, pp. 77-78 - checked in CR-027).
 * It is NOT the API `code` of the response (Appendix A: 0 succeeded, 1 failed, 6 no need to resend, 99 unknown error): that one only says whether the query worked.
 *
 * A code that the document does not list (for example 10, 19, 22, 32 or 246) is kept as the raw text and classified "Unknown": it is never treated as normal, idle or a fault.
 * When Hikrobot supplies an official table for the installed RCS version, add the codes HERE (and the label in i18n agv.amrStatus_<code>) and nothing else has to change.
 */
export type StatusCategory = 'Idle' | 'Working' | 'Done' | 'Charging' | 'Stopped' | 'Fault' | 'Unknown'

/** Documented codes only. The label of each is the i18n key agv.amrStatus_<code>. */
export const DOCUMENTED_STATUS: Readonly<Record<string, Exclude<StatusCategory, 'Unknown'>>> = {
  1: 'Done',        // task completed
  2: 'Working',     // executing task
  3: 'Fault',       // abnormal task
  4: 'Idle',        // idle, no task
  5: 'Stopped',     // robot stopped
  6: 'Working',     // lifting the rack
  7: 'Charging',    // charging
  8: 'Working',     // curve movement
  9: 'Charging',    // full charge maintenance
  11: 'Fault', 12: 'Fault', 13: 'Fault', 14: 'Fault', 15: 'Fault', 16: 'Fault', 17: 'Fault', 18: 'Fault', 20: 'Fault', 21: 'Fault',
  23: 'Fault', 24: 'Fault', 25: 'Fault', 26: 'Fault', 27: 'Fault', 28: 'Fault', 29: 'Fault', 30: 'Fault', 31: 'Fault', 33: 'Fault', 34: 'Fault',
}

/** `raw` is the value exactly as the RCS sent it (never altered); an empty / missing value has no category (null). */
export function classifyStatus(raw: string | null | undefined): { raw: string | null; category: StatusCategory | null; documented: boolean } {
  if (raw === null || raw === undefined || raw.trim() === '') return { raw: null, category: null, documented: false }
  const key = raw.trim()
  const c = Object.prototype.hasOwnProperty.call(DOCUMENTED_STATUS, key) ? DOCUMENTED_STATUS[key] : undefined
  return c ? { raw, category: c, documented: true } : { raw, category: 'Unknown', documented: false }
}
