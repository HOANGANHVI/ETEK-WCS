import type { Agv, SystemHealth } from '../api/types'
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

/** RCS link: there is no dedicated RCS health yet, so it is derived from AGV statuses reported through RCS. */
export function rcsView(health: SystemHealth | null, agvs: Agv[] | null): LinkView {
  if (!health) return { tone: 'bad', text: 'nav.noData' }
  if (health.integration.mode === 'Simulator') return { tone: 'warn', text: 'nav.notConnected' }
  const withRobot = (agvs ?? []).filter((a) => a.rcsRobotCode)
  if (withRobot.length === 0) return { tone: 'warn', text: 'nav.notSet' }
  return withRobot.some((a) => a.connectionStatus === 'Online') ? { tone: 'ok', text: 'nav.connected' } : { tone: 'bad', text: 'nav.disconnected' }
}

/** The text of a link view in the current language (`t` from useT). */
export const linkText = (t: (key: MsgKey) => string, m: LinkView['text']): string => (typeof m === 'string' ? t(m) : m.raw)
