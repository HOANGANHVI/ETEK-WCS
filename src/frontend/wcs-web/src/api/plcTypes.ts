// DTOs of the PLC monitoring / configuration API (CR-002).

export type EndpointState = 'Disconnected' | 'Connecting' | 'Connected' | 'Faulted' | 'NeedsConfiguration' | 'Disabled'

export interface PlcDiagnostics {
  requests: number; failures: number; timeouts: number; reconnects: number
  lastEndCode: number | null; lastError: string | null; lastLatencyMs: number | null
  lastSuccessAt: string | null; lastErrorAt: string | null; bytesSent: number; bytesReceived: number
  lastExchange: PlcExchange | null; lastFailedExchange: PlcExchange | null
}

/** Admin-only technical record of one request/response pair (protocol data, never secrets). */
export interface PlcExchange {
  at: string; plcId: string; operation: 'READ' | 'WRITE'; command: string; device: string; startAddress: number; points: number; durationMs: number
  endCode: number | null; outcome: 'OK' | 'END_CODE' | 'TIMEOUT' | 'FAILED' | 'AMBIGUOUS' | 'CANCELLED'; error: string | null
  requestHex: string; responseHex: string | null; isAmbiguous: boolean
}

export interface PlcWorkerStatus {
  plcId: string; name: string; state: EndpointState; mode: string; reason: string | null; missingParameters: string[]
  lastPollAt: string | null; lastSuccessAt: string | null; cycles: number; reconnects: number; consecutiveFailures: number
  stations: number; totalPoints: number; configuredPoints: number; goodPoints: number
  ipAddress: string | null; lastError: string | null; diagnostics: PlcDiagnostics | null
}

export interface PlcRuntimeInfo {
  mode: string; effectiveMode: string; allowLiveConnection: boolean; writeEnabled: boolean; writeKeysComplete: boolean; running: boolean; notes: string[]
}

export interface PlcStationSummary {
  stationId: string; plcId: string; order: number; role: string; locationCode: string | null
  total: number; configured: number; good: number; approved: number | null
}

export interface PlcOverview { runtime: PlcRuntimeInfo; plcs: PlcWorkerStatus[]; stations: PlcStationSummary[] }

export interface IoPointState {
  id: string; plcId: string; stationId: string; seq: number; group: string; signalName: string
  direction: 'PlcToSoftware' | 'SoftwareToPlc'; dataType: 'Bool' | 'Int' | 'String'; configStatus: string
  value: boolean | number | string | null; quality: string; lastUpdated: string | null; address: string | null; error: string | null
}

export interface StationSnapshot { plcId: string; stationId: string; total: number; configured: number; good: number; points: IoPointState[] }

export interface HandshakeState {
  plcId: string; stationId: string; approval: string; phase: string; phaseSince: string; reason: string | null
  approvedRules: number; signalsAwaitingConfirmation: number; active: { signal: string; since: string; timedOut: boolean }[]
  lastAction: string | null; lastError: string | null
}

export interface PlcConnection {
  plcId: string; name: string; ipAddress: string; port: number | null; protocol: string | null; networkNumber: number | null; pcNumber: number | null
  moduleIoNumber: number | null; plcStationNumber: number | null; monitoringTimer: number | null; timeoutMs: number | null; pollingIntervalMs: number | null
  connectTimeoutMs: number | null; readTimeoutMs: number | null; writeTimeoutMs: number | null; xyAddressBase: number | null; wAddressBase: number | null
  enabled: boolean; configStatus: string; missingParameters: string[]; stations: string[]; updatedAt: string; updatedBy: string | null
}

export interface SavePlcConnectionBody {
  name?: string | null; ipAddress: string; port: number | null; protocol: string | null; networkNumber: number | null; pcNumber: number | null
  moduleIoNumber: number | null; plcStationNumber: number | null; monitoringTimer: number | null; timeoutMs: number | null; pollingIntervalMs: number | null; enabled: boolean
  connectTimeoutMs?: number | null; readTimeoutMs?: number | null; writeTimeoutMs?: number | null; xyAddressBase?: number | null; wAddressBase?: number | null
}

export interface PlcLogRow {
  id: string; occurredAt: string; plcId: string | null; stationId: string | null; level: 'Info' | 'Warning' | 'Error'; event: string; message: string; detailsJson?: string | null
}

export interface AddressIssue { pointId: string; stationId: string; signal: string; code: string; message: string; args?: Record<string, unknown> | null }

export interface ProbeResultView { outcome: string; mode: string; message: string; error: string | null; latencyMs: number | null }

/** CR-004: one row of the IO-mapping table (Admin-only API). `pollable` = approved AND address present - the only points ever read from a real PLC. */
export interface IoPointConfig {
  id: string; plcId: string; stationId: string; seq: number; group: string; signalName: string; direction: 'PlcToSoftware' | 'SoftwareToPlc'
  dataType: 'Bool' | 'Int' | 'String'; description: string; configStatus: string; templateAddressReference: string | null; actualAddress: string | null
  wordLength: number | null; encoding: string | null; writeAllowed: boolean | null; approvedBy: string | null; approvedAt: string | null; pollable: boolean | null
  /** CR-014: the Admin decision on WRITING (independent of reading): NOT_APPROVED (awaiting) | APPROVED | REJECTED. */
  writeApproval: 'NOT_APPROVED' | 'APPROVED' | 'REJECTED'; writeDecidedBy: string | null; writeDecidedAt: string | null; writeDecisionNote: string | null
  /** false when the driver cannot write this data type / encoding. */
  writeSupported: boolean | null
}

/** Bulk READ approval of a commissioning PLC (Admin). Outcome: ELIGIBLE (preview only) | APPROVED | ALREADY_APPROVED | NOT_ELIGIBLE | FAILED. */
export interface BulkApprovalItem { pointId: string; stationId: string; signalName: string; dataType: string; address: string | null; outcome: string; code: string | null; reason: string | null }
export interface BulkApprovalPreview { plcId: string; total: number; withAddress: number; eligible: number; notEligible: number; alreadyApproved: number; items: BulkApprovalItem[] }
export interface BulkApprovalResult { plcId: string; total: number; approved: number; alreadyApproved: number; notEligible: number; failed: number; verifiedPollable: number; items: BulkApprovalItem[] }
