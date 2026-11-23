import type { RcsEffect, RcsOutcome } from '../../api/rcsDiagTypes'
import type { Tone } from '../../i18n/vi'

export const OUTCOME_TONE: Record<RcsOutcome, Tone> = {
  API_SUCCESS: 'ok', TCP_CONNECTION_FAILED: 'bad', TIMEOUT: 'bad', HTTP_FAILURE: 'bad', RCS_CODE_NOT_SUCCESS: 'warn', JSON_PARSE_FAILED: 'bad', RESPONSE_INVALID: 'bad', NOT_CONFIGURED: 'warn',
}
export const EFFECT_TONE: Record<RcsEffect, Tone> = { ReadOnly: 'ok', ChangesRcsData: 'warn', ControlsAmr: 'bad' }
