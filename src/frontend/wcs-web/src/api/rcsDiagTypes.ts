/** CR-032 – RCS Diagnostics & Manual API Tester (Admin only). Shapes of the /api/rcs-diagnostics endpoints. */

export type RcsEffect = 'ReadOnly' | 'ChangesRcsData' | 'ControlsAmr'
export const RCS_GROUPS = ['Query', 'Task', 'AmrControl', 'LocationMapArea', 'BindingMaterialRack', 'Ctu'] as const
export type RcsGroup = (typeof RCS_GROUPS)[number]

export interface RcsField {
  name: string; type: 'string' | 'number' | 'string[]' | 'object' | 'object[]'; required: boolean; maxLength: number | null; description: string
  enum: string[] | null; children: RcsField[] | null; maxItems: number | null; min: number | null; max: number | null
}
export interface RcsRule { kind: 'AtLeastOne' | 'ExactlyOne' | 'RequiredWhen'; fields: string[]; message: string; whenField: string | null; whenValue: string | null }

export interface RcsApiDef {
  name: string; group: RcsGroup; section: string; method: 'POST'; path: string; function: string; effect: RcsEffect; executable: boolean
  fields: RcsField[]; rules: RcsRule[]; response: RcsField[]; sample: string; gaps: string[]; permission: 'Admin'
}
export interface RcsCallbackDef { name: string; section: string; function: string; provided: 'NotProvided' | 'Provided' }
export interface RcsCatalog { groups: RcsGroup[]; apis: RcsApiDef[]; callbacks: RcsCallbackDef[] }

export interface RcsCheckState { at: string; success: boolean; durationMs: number; detail: string | null; by: string }
export interface RcsOverview {
  source: 'Real' | 'Simulator'; configured: boolean; enabled: boolean; scheme: string | null; host: string | null; port: number | null; baseUrl: string | null; authMode: string; timeoutMs: number
  problem: string | null; fleetSource: string; lastTcpCheck: RcsCheckState | null; lastApiCheck: RcsCheckState | null; apiAvailable: boolean | null; maxConcurrent: number; inUse: number
}

export interface RcsIssue { path: string; code: string; message: string }
export interface RcsPreview {
  api: string; method: string; endpoint: string | null; headers: Record<string, string>; body: string; valid: boolean; errors: RcsIssue[]; warnings: RcsIssue[]; configProblem: string | null
}

export type RcsOutcome = 'API_SUCCESS' | 'TCP_CONNECTION_FAILED' | 'TIMEOUT' | 'HTTP_FAILURE' | 'RCS_CODE_NOT_SUCCESS' | 'JSON_PARSE_FAILED' | 'RESPONSE_INVALID' | 'NOT_CONFIGURED'
export const RCS_OUTCOMES: RcsOutcome[] = ['API_SUCCESS', 'TCP_CONNECTION_FAILED', 'TIMEOUT', 'HTTP_FAILURE', 'RCS_CODE_NOT_SUCCESS', 'JSON_PARSE_FAILED', 'RESPONSE_INVALID', 'NOT_CONFIGURED']

/** What the Inspector shows: the live result of an Execute or a row of the history (the history keeps no response headers). */
export interface RcsExecution {
  id: string; executedAt: string; api: string; group: string; user: string; requestId: string; method: string; endpoint: string | null
  requestHeaders: Record<string, string>; requestBody: string | null
  httpStatus: number | null; responseHeaders: Record<string, string>; responseBody: string | null; responseTruncated: boolean
  rcsCode: string | null; rcsMessage: string | null; durationMs: number; outcome: RcsOutcome; success: boolean; errorMessage: string | null; warnings: RcsIssue[]
}

export interface RcsHistoryItem {
  id: string; executedAt: string; user: string; role: string; api: string; group: string; requestId: string; endpoint: string | null
  requestBody: string | null; responseBody: string | null; responseTruncated: boolean
  httpStatus: number | null; rcsCode: string | null; rcsMessage: string | null; durationMs: number; outcome: RcsOutcome; success: boolean; errorMessage: string | null
}
export interface RcsHistoryPage { total: number; items: RcsHistoryItem[] }
export interface RcsHistoryFilter { from?: string; to?: string; api?: string; user?: string; status?: string; skip?: number; take?: number }

export interface RcsCallbackEntry { id: string; receivedAt: string; callback: string; path: string | null; sourceAddress: string | null; requestJson: string | null; responseJson: string | null; httpStatus: number | null; processed: boolean; errorMessage: string | null }
export interface RcsCallbacks { endpoints: RcsCallbackDef[]; received: RcsCallbackEntry[] }
