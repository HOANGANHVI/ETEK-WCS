// CR-024: the description of an IO signal in the language of the screen.
// The 23 template signals are the same for every station and PLC and their description is fixed by the controlled IO workbook (it is stored once and cannot be edited in the application), so it is
// translated centrally BY SIGNAL NAME through the normal i18n dictionary (keys plc.sigDesc_<Signal_Name>); the stored text stays unchanged in the database and API. A signal that has no entry
// (a future catalog change) shows the stored text as it is, never a missing-key marker.
import type { MsgKey } from '../i18n'

type Translate = (key: MsgKey) => string

export const TEMPLATE_SIGNALS = [
  // IO map of 22/11/2026 (CR-046)
  'PLC_MissionRequest', 'PLC_ReaddyTranfer', 'PLC_MissionCompleted', 'AGV_RequestTranfer', 'AGV_TranferCompleted', 'Mission_Feedback',
  // signals of earlier maps: rows an existing database or a test configuration may still show
  'Safety_Ready',
  'Pickup_Request',
  'Delivery_Request',
  'Request_ID',
  'Station_Online',
  'Station_Ready',
  'Rack_Present',
  'Rack_Full',
  'Rack_Empty',
  'Destination_Available',
  'Station_Fault',
  'Transfer_Complete',
  'Rack_Type',
  'Heartbeat_SW',
  'Request_Ack',
  'AGV_Assigned',
  'AGV_ID',
  'AGV_Arrived',
  'Transfer_Permit',
  'AGV_Departure_Permit',
  'AGV_Busy',
  'AGV_Fault',
  'Error_Code',
] as const

const KNOWN: ReadonlySet<string> = new Set(TEMPLATE_SIGNALS)

export function signalDescription(t: Translate, signalName: string, storedDescription: string): string {
  return KNOWN.has(signalName) ? t(`plc.sigDesc_${signalName}` as MsgKey) : storedDescription
}
