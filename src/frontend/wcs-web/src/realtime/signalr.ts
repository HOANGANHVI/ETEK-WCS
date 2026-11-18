import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'

const BASE_URL = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? ''

/** Server -> client events published by the WCS API (see the SignalR event list in the Phase 1 report). */
export const WCS_EVENTS = ['TaskCreated', 'TaskStateChanged', 'TaskDeleted', 'PlcStatusChanged', 'StationIoChanged', 'PlcLog', 'HandshakePhaseChanged', 'InventoryChanged', 'AgvStatusChanged', 'OrderChanged'] as const
export type WcsEventName = (typeof WCS_EVENTS)[number]

export type ConnectionStatus = 'connecting' | 'connected' | 'reconnecting' | 'disconnected' | 'demo'

export function createWcsConnection(): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(`${BASE_URL}/hubs/wcs`)
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(LogLevel.Warning)
    .build()
}

export function toStatus(state: HubConnectionState): ConnectionStatus {
  switch (state) {
    case HubConnectionState.Connected: return 'connected'
    case HubConnectionState.Connecting: return 'connecting'
    case HubConnectionState.Reconnecting: return 'reconnecting'
    default: return 'disconnected'
  }
}
