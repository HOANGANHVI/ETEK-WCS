// CR-021: whole-system diagnostic export (background job). Mirrors the DiagnosticEndpoints views; the server path of the file is never part of it.
export type DiagJobState = 'Queued' | 'Running' | 'Completed' | 'Failed' | 'Expired'
export type DiagSourceStatus = 'OK' | 'Partial' | 'Failed' | 'NotRequested'

export interface DiagSourceReport {
  name: string; modules: string[]; status: DiagSourceStatus; rowsInRange: number; rowsExported: number; rowsOmittedOldest: number
  oldestStored: string | null; newestInRange: string | null; retention: string; error: string | null; note: string | null
}

export interface DiagExportRequestView { mode: string; from: string | null; to: string | null; modules: string[]; minLevel: string | null; plcId: string | null; device: string | null; correlationId: string | null; taskId: string | null }

export interface DiagExportJob {
  id: string; state: DiagJobState; progress: number; stage: string; requestedBy: string; createdAt: string; startedAt: string | null; finishedAt: string | null; expiresAt: string | null
  fileName: string; bytes: number; completeness: 'Complete' | 'Partial' | null; events: number; errors: number; warnings: number
  request: DiagExportRequestView; sources: DiagSourceReport[]; notes: string[]; errorCode: string | null
}

export interface DiagExportOptions { modules: string[]; levels: string[]; modes: string[] }

export interface DiagExportBody { mode: string; from?: string; to?: string; modules?: string[]; minLevel?: string; plcId?: string; device?: string; correlationId?: string; taskId?: string }
