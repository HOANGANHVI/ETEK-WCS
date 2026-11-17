import { createContext, useContext } from 'react'
import type { Preferences } from './prefsStore'

export interface PreferencesValue extends Preferences {
  saving: boolean
  /** null: nothing to report; 'saved' / 'failed' describe the last save attempt. */
  saveState: 'saved' | 'failed' | null
  /** Applies the change at once and stores it for the account; on failure the previous values are restored. Resolves true when stored. */
  update: (change: Partial<Preferences>) => Promise<boolean>
}

export const PreferencesContext = createContext<PreferencesValue | null>(null)

export function usePreferences(): PreferencesValue {
  const v = useContext(PreferencesContext)
  if (!v) throw new Error('usePreferences must be used inside <PreferencesProvider>')
  return v
}
