import { useState } from 'react'
import { api } from '../api/client'
import type { Profile } from '../api/authTypes'
import { IconUser } from '../components/icons'
import { useT } from '../i18n'

/**
 * The signed-in account's own avatar: the image from /api/auth/avatar (the version in the URL changes with every upload/removal, and the server sends no-store,
 * so an old picture is never reused), or the first letter of the name, or a plain user icon. Only the signed-in account's image is ever requested.
 */
export function Avatar({ profile, initial, small = false }: { profile: Pick<Profile, 'hasAvatar' | 'avatarVersion'> | null; initial?: string; small?: boolean }) {
  const t = useT()
  const [failedVersion, setFailedVersion] = useState<number | null>(null)
  const show = !!profile && profile.hasAvatar && failedVersion !== profile.avatarVersion
  return (
    <span className={`avatar${small ? ' avatar-sm' : ''}`} role="img" aria-label={t('account.avatar')}>
      {show && profile
        ? <img src={api.avatarUrl(profile.avatarVersion)} alt="" onError={() => setFailedVersion(profile.avatarVersion)} />
        : initial ? initial : <IconUser />}
    </span>
  )
}
