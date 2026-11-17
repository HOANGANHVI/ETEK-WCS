import { useState } from 'react'
import { api } from '../api/client'
import type { Profile } from '../api/authTypes'
import { useT } from '../i18n'

/** The account's own avatar, or the first letter of the name when there is none (or it cannot be loaded). Only the signed-in account's image is ever requested. */
export function Avatar({ profile, initial }: { profile: Pick<Profile, 'hasAvatar' | 'avatarVersion'>; initial: string }) {
  const t = useT()
  const [failedVersion, setFailedVersion] = useState<number | null>(null)
  const show = profile.hasAvatar && failedVersion !== profile.avatarVersion
  return (
    <span className="avatar" role="img" aria-label={t('account.avatar')}>
      {show
        ? <img src={api.avatarUrl(profile.avatarVersion)} alt="" onError={() => setFailedVersion(profile.avatarVersion)} />
        : initial}
    </span>
  )
}
