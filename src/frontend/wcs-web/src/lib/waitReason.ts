// QA-001: the waiting kind alone said "no suitable source rack" also when the rack is there and the station port is held; the backend marks that cause in the reason.
export type WaitCause = 'portHeld' | 'kind'
export function waitCause(o: { waitKind: string; stateReason?: string | null }): WaitCause {
  // CR-052: the same for a destination whose port is held, before or after the pick
  return ['SourceUnavailable', 'DestinationUnavailable', 'DestinationUnavailable_AgvHoldingRack'].includes(o.waitKind) && o.stateReason?.startsWith('PORT_HELD:') ? 'portHeld' : 'kind'
}
