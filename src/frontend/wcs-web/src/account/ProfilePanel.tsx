import { useRef, useState } from 'react'
import { api } from '../api/client'
import type { Profile } from '../api/authTypes'
import { useAuth } from '../auth/AuthContext'
import { Button, Panel } from '../components/ui'
import { Field } from '../pages/config/FormKit'
import { fromApiError, type Errors } from '../pages/config/formUtils'
import { useT, type MsgKey } from '../i18n'
import { displayNameOf } from '../i18n/format'
import { describeError } from '../i18n/vi'
import { AvatarPrepError, prepareAvatar } from './avatarImage'
import { Avatar } from './Avatar'

const EMAIL_MAX = 254, NAME_MAX = 100
// Same shape rule as the server (one '@', a dotted domain, no spaces). The server decides; this only saves a round trip.
const looksLikeEmail = (v: string) => v.length <= EMAIL_MAX && /^[^\s@]+@[^\s@.]+(\.[^\s@.]+)+$/.test(v)

type Notice = { tone: 'ok' | 'bad'; text: string } | null

/** Section A: the signed-in user's own profile. Full name, e-mail and avatar are self-service; the username is shown but cannot be changed. */
export function ProfilePanel({ profile, onChanged }: { profile: Profile; onChanged: () => Promise<void> | void }) {
  const t = useT()
  const { setUser } = useAuth()
  const fileInput = useRef<HTMLInputElement>(null)
  const [name, setName] = useState(displayNameOf(profile.displayName))
  const [email, setEmail] = useState(profile.email ?? '')
  const [busy, setBusy] = useState(false)
  const [errors, setErrors] = useState<Errors>({})
  const [notice, setNotice] = useState<Notice>(null)
  const [avatarBusy, setAvatarBusy] = useState(false)
  const [avatarNotice, setAvatarNotice] = useState<Notice>(null)

  const nameTrim = name.trim(), emailTrim = email.trim()
  const emailBad = emailTrim !== '' && !looksLikeEmail(emailTrim)
  const nameBad = nameTrim === '' || nameTrim.length > NAME_MAX
  const dirty = nameTrim !== displayNameOf(profile.displayName) || emailTrim !== (profile.email ?? '')
  const initial = (nameTrim[0] ?? profile.username[0] ?? '?').toUpperCase()

  const save = async () => {
    setBusy(true); setErrors({}); setNotice(null)
    try {
      await api.updateProfile({ displayName: nameTrim, email: emailTrim === '' ? null : emailTrim })
      setUser(await api.me())            // the header shows the (session) name: refresh it
      await onChanged()
      setNotice({ tone: 'ok', text: t('account.profileSaved') })
    } catch (e) {
      const r = fromApiError(e); setErrors(r.fields); setNotice({ tone: 'bad', text: r.general ?? '' })
    } finally { setBusy(false) }
  }

  const pick = async (file: File | undefined) => {
    if (!file) return
    setAvatarBusy(true); setAvatarNotice(null)
    try {
      const image = await prepareAvatar(file)
      await api.uploadAvatar(image)
      await onChanged()
      setAvatarNotice({ tone: 'ok', text: t('account.avatarSaved') })
    } catch (e) {
      const key: Record<string, MsgKey> = { type: 'account.avatarErrType', size: 'account.avatarErrSize', image: 'account.avatarErrImage' }
      setAvatarNotice({ tone: 'bad', text: e instanceof AvatarPrepError ? t(key[e.problem]) : describeError(e) })
    } finally { setAvatarBusy(false); if (fileInput.current) fileInput.current.value = '' }
  }

  const remove = async () => {
    setAvatarBusy(true); setAvatarNotice(null)
    try { await api.deleteAvatar(); await onChanged(); setAvatarNotice({ tone: 'ok', text: t('account.avatarRemoved') }) }
    catch (e) { setAvatarNotice({ tone: 'bad', text: describeError(e) }) }
    finally { setAvatarBusy(false) }
  }

  return (
    <Panel title={t('account.secProfile')}>
      <div className="pad">
        <div className="avatar-row">
          <Avatar profile={profile} initial={initial} />
          <div className="avatar-side">
            <div className="btn-row">
              <input ref={fileInput} type="file" accept="image/png,image/jpeg" hidden aria-label={t('account.avatar')} onChange={(e) => void pick(e.target.files?.[0])} />
              <Button size="sm" disabled={avatarBusy} onClick={() => fileInput.current?.click()}>{profile.hasAvatar ? t('account.avatarChange') : t('account.avatarUpload')}</Button>
              {profile.hasAvatar && <Button size="sm" variant="danger" disabled={avatarBusy} onClick={() => void remove()}>{t('account.avatarRemove')}</Button>}
            </div>
            <small className="muted">{avatarBusy ? t('account.avatarBusy') : t('account.avatarHint')}</small>
          </div>
        </div>
        <div className="pref-status" role="status" aria-live="polite">
          {avatarNotice && <span className={avatarNotice.tone === 'ok' ? 'text-ok' : 'text-bad'}>{avatarNotice.text}</span>}
        </div>

        <div className="form-grid single profile-form">
          <Field label={t('account.username')} hint={t('account.usernameHint')}><div className="readonly-value mono">{profile.username}</div></Field>
          <Field label={t('account.displayName')} hint={t('account.displayNameHint')} error={errors.displayName ?? (nameTrim === '' ? t('codes.field_DISPLAY_NAME_REQUIRED') : nameTrim.length > NAME_MAX ? t('codes.field_DISPLAY_NAME_MAX') : undefined)}>
            <input value={name} maxLength={NAME_MAX + 20} onChange={(e) => setName(e.target.value)} autoComplete="name" />
          </Field>
          <Field label={t('account.email')} hint={t('account.emailHint')} error={errors.email ?? (emailBad ? t('codes.field_EMAIL_INVALID') : undefined)}>
            <input type="email" inputMode="email" value={email} placeholder={t('account.emailPh')} onChange={(e) => setEmail(e.target.value)} autoComplete="email" />
          </Field>
        </div>
        <div className="btn-row">
          <Button variant="primary" disabled={busy || !dirty || nameBad || emailBad} onClick={() => void save()}>{busy ? t('account.saving') : t('account.saveProfile')}</Button>
        </div>
        <div className="pref-status" role="status" aria-live="polite">
          {notice && <span className={notice.tone === 'ok' ? 'text-ok' : 'text-bad'}>{notice.text}</span>}
        </div>
      </div>
    </Panel>
  )
}
