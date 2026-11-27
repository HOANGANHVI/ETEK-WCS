// CR-047: pure helpers of the rack dialog (no DOM, no i18n: testable without a browser). The server decides whether a rack may be changed; this only decides what to say and where to lead.
export interface RackBlocker { kind: 'Order' | 'Task'; id: string; reference: string; state: string; process: string | null; canCancel: boolean; racksHeld: boolean }

/** What the operator can do about one job that holds the rack. Nothing is cancelled from the rack screen. */
export type RackBlockerAction = 'cancelOrder' | 'finishOrder' | 'releaseRacks' | 'openTask'
export const blockerAction = (b: RackBlocker): RackBlockerAction => (b.kind === 'Task' ? 'openTask' : b.racksHeld ? 'releaseRacks' : b.canCancel ? 'cancelOrder' : 'finishOrder')

/** An order that can still end (a finished one cannot be cancelled). */
export const orderIsOpen = (state: string): boolean => !['Completed', 'Failed', 'Cancelled'].includes(state)

/** The rule of the cancellation (the server enforces it): not while a leg is running in the AGV system or the rack is on the AMR. */
export const orderInMotion = (legs: { state: string; rackOnAgv: boolean; agvTaskCode: string | null }[]): boolean => legs.some((l) => l.rackOnAgv || (l.state === 'InProgress' && l.agvTaskCode !== null))

/** The page of the job. */
export const blockerLink = (b: RackBlocker): string => (b.kind === 'Order' ? `/routing?order=${encodeURIComponent(b.id)}` : `/tasks?id=${encodeURIComponent(b.id)}`)
