/** CR-026 – configuration export / import (Admin). Shapes mirror the API; records are identified by their natural key (never a database id). */

export type TransferGroupId = 'ProductModel' | 'Location' | 'Agv' | 'PlcStation' | 'BusinessProcess'

/** CR-036 (A.2): GET /api/product-models (Admin). */
export interface ProductModelInfo { id: string; code: string; name: string; enabled: boolean }

export interface MaintenanceView { enabled: boolean; reason: string | null; changedBy: string | null; updatedAt: string | null }

export interface TransferStatus { enabled: boolean; backupReady: boolean; backupProblem: string | null; maxPackageMb: number }

export interface TransferGroup { id: TransferGroupId; file: string; dependsOn: TransferGroupId[]; updateOnly: boolean; targetCount: number }

export interface ExportDependencyReport { selected: TransferGroupId[]; missing: TransferGroupId[]; reasons: Record<string, string[]> }

export interface ManifestGroup { group: TransferGroupId; file: string; count: number; sha256: string }
export interface PackageManifest {
  formatVersion: string; exportedAt: string; exportedBy: string; appVersion: string; schemaMigration: string; sourceSite: string
  groups: ManifestGroup[]; excluded: { name: string; reason: string }[]; warnings: string[]; unresolvedReferences: string[]
}
export interface UploadResult { importId: string; packageSha256: string; manifest: PackageManifest; warnings: string[] }

export type PlanAction = 'Create' | 'Update' | 'Unchanged' | 'Skip' | 'Conflict'
export interface FieldChange { field: string; from: string | null; to: string | null }
export interface PlanItem {
  group: TransferGroupId; key: string; action: PlanAction; changes: FieldChange[]; conflictCode: string | null; conflictMessage: string | null; headerChange: boolean
}
export interface Blocker { code: string; message: string; count: number; samples: string[] }
export interface ApplyReadiness { maintenanceOn: boolean; backupReady: boolean; backupProblem: string | null; blockers: Blocker[]; canApply: boolean }
export interface PlanSummary { create: number; update: number; unchanged: number; skip: number; conflict: number }
export interface ImportPlan {
  importId: string; groupOrder: TransferGroupId[]; items: PlanItem[]; summary: PlanSummary; warnings: string[]; readiness: ApplyReadiness
  planHash: string; previewToken: string | null; previewExpiresAt: string | null
}

export interface RecordRef { group: TransferGroupId; key: string }
export interface Selection { groups?: TransferGroupId[] | null; exclude?: RecordRef[] | null }
export interface ApplyBody { previewToken: string; confirmHeaderChanges: boolean; skipConflicts: boolean }
export interface ApplyResult {
  importId: string; alreadyApplied: boolean; created: number; updated: number; unchanged: number; skipped: number; conflictsSkipped: number
  backupFile: string; applied: PlanItem[]; maintenanceStillOn: boolean
}
