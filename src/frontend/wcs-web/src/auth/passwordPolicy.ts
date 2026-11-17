import { t } from '../i18n'

/** Mirrors the backend password policy (the server decides; this only saves a round trip). */
export function checkPassword(pw: string, username: string): string | null {
  if (pw.length < 10) return t('auth.pwTooShort')
  if (!/[A-Za-z\u00C0-\u1EF9]/.test(pw) || !/\d/.test(pw)) return t('auth.pwNeedLetterDigit')
  if (username && pw.toLowerCase().includes(username.toLowerCase())) return t('auth.pwContainsUser')
  return null
}
