import { createContext, useContext } from 'react'
import type { Profile } from '../api/authTypes'

export interface ProfileValue {
  /** The signed-in account's own profile; null while loading, when nobody is signed in, and ALWAYS for an account other than the one the data was loaded for. */
  profile: Profile | null
  /** Loads the profile again (after a change); resolves when the new value is in place. */
  reload: () => Promise<void>
}

export const ProfileContext = createContext<ProfileValue | null>(null)

export function useProfile(): ProfileValue {
  const v = useContext(ProfileContext)
  if (!v) throw new Error('useProfile must be used inside <ProfileProvider>')
  return v
}
