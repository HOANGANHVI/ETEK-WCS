import { useCallback, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import { ProfilePanel } from '../account/ProfilePanel'
import { SecurityPanel } from '../account/SecurityPanel'
import { hasRole, useAuth } from '../auth/AuthContext'
import { Async, Button, PageHeader, Panel } from '../components/ui'
import { useApi } from '../hooks/useApi'
import { IconMoon, IconPanelLeftClose, IconPanelLeftOpen, IconSun } from '../components/icons'
import { useT, type MsgKey } from '../i18n'
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

/** The signed-in user's own page: profile (own data), account security, role and organization (administrator-managed), and personal interface preferences. */
export function AccountPage() {
  const t = useT()
  const { user } = useAuth()
  const prefs = usePreferences()
  const profile = useApi(useCallback(() => api.profile(), []))
  if (!user) return null
  const refresh = () => profile.reload()

  return (
    <>
      <PageHeader title={t('account.title')} description={t('account.subtitle')} />
      <div className="account-grid">
        <Async state={profile} empty={{ title: t('account.notProvided') }}>
          {(p) => (
            <>
              <ProfilePanel profile={p} onChanged={refresh} />
              <SecurityPanel />
              <Panel title={t('account.secRole')}>
                <div className="pad">
                  <Row label={t('account.role')} hint={t('account.roleHint')}>{ROLE_VI[p.role]}</Row>
                  <Row label={t('account.position')} hint={t('account.orgHint')}>{p.position ?? <span className="muted">{t('account.notProvided')}</span>}</Row>
                  <Row label={t('account.department')} hint={t('account.orgHint')}>{p.department ?? <span className="muted">{t('account.notProvided')}</span>}</Row>
                  {hasRole(user, 'Admin') && <div className="btn-row"><Link to="/users"><Button size="sm">{t('account.manageUsers')}</Button></Link></div>}
                </div>
              </Panel>
            </>
          )}
        </Async>

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
