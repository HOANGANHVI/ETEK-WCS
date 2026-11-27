// QA-001: the waiting kind alone said "no suitable source rack" also when the rack is there and the station port is held; the backend marks that cause in the reason.
export type WaitCause = 'portHeld' | 'kind'
export function waitCause(o: { waitKind: string; stateReason?: string | null }): WaitCause {
  return o.waitKind === 'SourceUnavailable' && o.stateReason?.startsWith('PORT_HELD:') ? 'portHeld' : 'kind'
}
