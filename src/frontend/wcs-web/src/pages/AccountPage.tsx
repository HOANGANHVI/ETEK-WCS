import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { hasRole, useAuth } from '../auth/AuthContext'
import { Button, PageHeader, Panel } from '../components/ui'
import { IconMoon, IconPanelLeftClose, IconPanelLeftOpen, IconSun } from '../components/icons'
import { useT, type MsgKey } from '../i18n'
import { displayNameOf } from '../i18n/format'
import { ROLE_VI } from '../i18n/vi'
import { usePreferences } from '../preferences/PreferencesContext'
import type { Preferences } from '../preferences/prefsStore'

interface Choice<V extends string> { value: V; icon: ReactNode; title: MsgKey; desc: MsgKey }

function ChoiceGroup<V extends string>({ id, label, value, choices, onPick }: {
  id: string; label: string; value: V; choices: Choice<V>[]; onPick: (v: V) => void
}) {
  const t = useT()
  return (
    <div className="pref-group">
      <div className="field-label" id={id}>{label}</div>
      <div className="pref-cards" role="radiogroup" aria-labelledby={id}>
        {choices.map((c) => (
          <button key={c.value} type="button" role="radio" aria-checked={value === c.value}
            className={`pref-card${value === c.value ? ' selected' : ''}`} onClick={() => onPick(c.value)}>
            <span className="pref-icon">{c.icon}</span>
            <span className="pref-text"><strong>{t(c.title)}</strong><small>{t(c.desc)}</small></span>
          </button>
        ))}
      </div>
    </div>
  )
}

const THEMES: Choice<Preferences['theme']>[] = [
  { value: 'light', icon: <IconSun />, title: 'account.themeLight', desc: 'account.themeLightDesc' },
  { value: 'dark', icon: <IconMoon />, title: 'account.themeDark', desc: 'account.themeDarkDesc' },
]
const SIDEBARS: Choice<'expanded' | 'collapsed'>[] = [
  { value: 'expanded', icon: <IconPanelLeftClose />, title: 'account.sidebarExpanded', desc: 'account.sidebarExpandedDesc' },
  { value: 'collapsed', icon: <IconPanelLeftOpen />, title: 'account.sidebarCollapsed', desc: 'account.sidebarCollapsedDesc' },
]

function Row({ label, hint, children }: { label: string; hint?: string; children: ReactNode }) {
  return <div className="kv-row"><div className="kv-label">{label}{hint && <small>{hint}</small>}</div><div className="kv-value">{children}</div></div>
}

/** The signed-in user's own page: profile (read-only, managed by an administrator), security, role, and personal interface preferences. */
export function AccountPage() {
  const t = useT()
  const { user } = useAuth()
  const prefs = usePreferences()
  if (!user) return null
  const name = displayNameOf(user.displayName)
  const initial = (name.trim()[0] ?? user.username[0] ?? '?').toUpperCase()

  return (
    <>
      <PageHeader title={t('account.title')} description={t('account.subtitle')} />
      <div className="account-grid">
        <Panel title={t('account.secProfile')}>
          <div className="pad">
            <div className="avatar-row">
              <span className="avatar" aria-label={t('account.avatar')} role="img">{initial}</span>
              <small className="muted">{t('account.avatarHint')}</small>
            </div>
            <Row label={t('account.displayName')} hint={t('account.displayNameHint')}>{name}</Row>
          </div>
        </Panel>

        <Panel title={t('account.secSecurity')}>
          <div className="pad">
            <Row label={t('account.username')}><span className="mono">{user.username}</span></Row>
            <Row label={t('account.password')} hint={t('account.passwordHint')}>
              <Link to="/account/password"><Button size="sm">{t('account.changePassword')}</Button></Link>
            </Row>
          </div>
        </Panel>

        <Panel title={t('account.secRole')}>
          <div className="pad">
            <Row label={t('account.role')} hint={t('account.roleHint')}>{ROLE_VI[user.role]}</Row>
            <p className="muted small">{t('account.orgNote')}</p>
            {hasRole(user, 'Admin') && <div className="btn-row"><Link to="/users"><Button size="sm">{t('account.manageUsers')}</Button></Link></div>}
          </div>
        </Panel>

        <Panel title={t('account.secPrefs')} className="account-prefs">
          <div className="pad">
            <p className="muted small">{t('account.prefsOwn')}</p>
            <ChoiceGroup id="pref-theme" label={t('account.themeLabel')} value={prefs.theme} choices={THEMES} onPick={(v) => void prefs.update({ theme: v })} />
            <ChoiceGroup id="pref-sidebar" label={t('account.sidebarLabel')} value={prefs.sidebarCollapsed ? 'collapsed' : 'expanded'} choices={SIDEBARS}
              onPick={(v) => void prefs.update({ sidebarCollapsed: v === 'collapsed' })} />
            <div className="pref-status" role="status" aria-live="polite">
              {prefs.saving && <span className="muted">{t('account.saving')}</span>}
              {!prefs.saving && prefs.saveState === 'saved' && <span className="text-ok">{t('account.saved')}</span>}
              {!prefs.saving && prefs.saveState === 'failed' && <span className="text-bad">{t('account.saveFailed')}</span>}
            </div>
          </div>
        </Panel>
      </div>
    </>
  )
}
