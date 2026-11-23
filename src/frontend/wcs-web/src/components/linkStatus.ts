import type { FleetView } from '../api/fleetTypes'
import type { SystemHealth } from '../api/types'
import type { MsgKey } from '../i18n'
import type { Tone } from '../i18n/vi'

export interface LinkView { tone: Tone; text: MsgKey | { raw: string } }

/** PLC link, from the API health. With no real controller attached the label is "Not connected" - never a healthy-looking state. */
export function plcView(health: SystemHealth | null): LinkView {
  if (!health) return { tone: 'bad', text: 'nav.noData' }
  const rt = health.integration.plcRuntime
  if (rt) {
    if (rt.mode === 'Simulator') return { tone: 'warn', text: 'nav.notConnected' }
    if (!rt.running) return { tone: 'warn', text: 'nav.notRunning' }
    if (rt.connected === 0) return { tone: 'bad', text: { raw: `0/${rt.total} PLC` } }
    return { tone: rt.connected === rt.total ? 'ok' : 'warn', text: { raw: `${rt.connected}/${rt.total} PLC` } }
  }
  if (health.integration.mode === 'Simulator') return { tone: 'warn', text: 'nav.notConnected' }
  switch (health.integration.plc) {
    case 'Connected': return { tone: 'ok', text: 'nav.connected' }
    case 'Connecting': return { tone: 'warn', text: 'nav.connecting' }
    case 'Faulted': return { tone: 'bad', text: 'nav.fault' }
    default: return { tone: 'bad', text: 'nav.disconnected' }
  }
}

/**
 * RCS link, from the fleet view that the AMR monitoring shows (ONE rule, evaluated by the server): Connected = the last poll succeeded and is fresh; Degraded = the last poll failed but the
 * last success is still fresh; Disconnected = no fresh success; NoData = no poll yet. A simulated fleet is never shown as a connection. No separate RCS check is made for the header.
 */
export function rcsView(fleet: FleetView | null, loadFailed = false): LinkView {
  if (!fleet) return loadFailed ? { tone: 'bad', text: 'nav.noData' } : { tone: 'muted', text: 'nav.notChecked' }
  if (fleet.simulatedData) return { tone: 'warn', text: 'nav.notConnected' }
  switch (fleet.connection) {
    case 'Connected': return { tone: 'ok', text: 'nav.connected' }
    case 'Degraded': return { tone: 'warn', text: 'nav.interrupted' }
    case 'Disconnected': return { tone: 'bad', text: 'nav.disconnected' }
    default: return { tone: 'muted', text: 'nav.notChecked' }
  }
}

/** The text of a link view in the current language (`t` from useT). */
export const linkText = (t: (key: MsgKey) => string, m: LinkView['text']): string => (typeof m === 'string' ? t(m) : m.raw)
