import type { RcsIssue } from '../../api/rcsDiagTypes'

export type JsonObject = Record<string, unknown>

/** Error / warning messages of one field path (the server validates; the form only places its answer under the field). */
export const issuesFor = (issues: RcsIssue[], path: string): RcsIssue[] => issues.filter((i) => i.path === path)

export const isObject = (v: unknown): v is JsonObject => typeof v === 'object' && v !== null && !Array.isArray(v)

/** Text typed in a field -> the value kept in the body. An empty text removes the key; a number field keeps a JSON number when the text is numeric. */
export function fromText(type: string, text: string): unknown {
  if (text === '') return undefined
  if (type === 'number' && /^-?\d+(\.\d+)?$/.test(text.trim())) return Number(text)
  return text
}

export const toText = (v: unknown): string => (v === undefined || v === null ? '' : typeof v === 'string' ? v : String(v))

/** Lines of a textarea -> string array (an empty textarea removes the key). */
export const fromLines = (text: string): string[] | undefined => {
  const lines = text.split('\n').map((l) => l.trim()).filter(Boolean)
  return lines.length ? lines : undefined
}

export function parseJsonObject(text: string): { value: JsonObject | null; error: string | null } {
  try {
    const v: unknown = JSON.parse(text)
    return isObject(v) ? { value: v, error: null } : { value: null, error: 'NOT_OBJECT' }
  } catch (e) {
    return { value: null, error: e instanceof Error ? e.message : 'PARSE' }
  }
}

/** The body as text for a person to copy: the Inspector's request / response blocks, plus the facts an RCS engineer asks for. */
export interface CopyFacts {
  api: string; endpoint: string | null; at: string; user: string; requestId: string; method: string
  requestBody: string | null; httpStatus: number | null; rcsCode: string | null; rcsMessage: string | null; durationMs: number; outcome: string
  responseBody: string | null; errorMessage: string | null
}
export const supportText = (f: CopyFacts): string => [
  `API: ${f.api}`, `${f.method} ${f.endpoint ?? '-'}`, `Time: ${f.at}`, `User: ${f.user}`, `Request ID (reqCode): ${f.requestId}`,
  `Outcome: ${f.outcome}`, `HTTP status: ${f.httpStatus ?? '-'}`, `RCS code: ${f.rcsCode ?? '-'}`, `RCS message: ${f.rcsMessage ?? '-'}`, `Duration: ${f.durationMs} ms`,
  ...(f.errorMessage ? [`Error: ${f.errorMessage}`] : []),
  '', 'Request (masked):', f.requestBody ?? '-', '', 'Response (masked):', f.responseBody ?? '-',
].join('\n')

export async function copyText(text: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(text)
    return true
  } catch {
    try {
      const ta = document.createElement('textarea')
      ta.value = text; ta.style.position = 'fixed'; ta.style.opacity = '0'
      document.body.appendChild(ta); ta.select()
      const ok = document.execCommand('copy')
      ta.remove()
      return ok
    } catch {
      return false
    }
  }
}
