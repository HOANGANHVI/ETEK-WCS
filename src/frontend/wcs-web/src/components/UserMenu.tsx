import { useEffect, useId, useRef, useState, type KeyboardEvent } from 'react'
import { Link } from 'react-router-dom'
import { Avatar } from '../account/Avatar'
import { useProfile } from '../account/ProfileContext'
import { useAuth } from '../auth/AuthContext'
import { useT } from '../i18n'
import { displayNameOf } from '../i18n/format'
import { ROLE_SHORT_VI } from '../i18n/vi'
import { IconChevronDown } from './icons'

/** Header account menu: name + avatar; opens "Account information" (/account) and "Sign out". Closes on outside click, Escape, Tab and after a choice. */
export function UserMenu() {
  const t = useT()
  const { user, logout } = useAuth()
  const { profile } = useProfile()
  const [open, setOpen] = useState(false)
  const wrap = useRef<HTMLDivElement>(null)
  const button = useRef<HTMLButtonElement>(null)
  const menuId = useId()

  useEffect(() => {
    if (!open) return
    const onDown = (e: MouseEvent) => { if (wrap.current && !wrap.current.contains(e.target as Node)) setOpen(false) }
    document.addEventListener('mousedown', onDown)
    return () => document.removeEventListener('mousedown', onDown)
  }, [open])

  useEffect(() => { if (open) wrap.current?.querySelector<HTMLElement>('[role=menuitem]')?.focus() }, [open])

  if (!user) return null
  const name = displayNameOf(profile?.displayName ?? user.displayName)
  const initial = (name.trim()[0] ?? user.username[0] ?? '').toUpperCase()
  const close = (refocus: boolean) => { setOpen(false); if (refocus) button.current?.focus() }

  const onMenuKey = (e: KeyboardEvent<HTMLDivElement>) => {
    const items = [...(wrap.current?.querySelectorAll<HTMLElement>('[role=menuitem]') ?? [])]
    const i = items.indexOf(document.activeElement as HTMLElement)
    if (e.key === 'Escape') { e.preventDefault(); close(true) }
    else if (e.key === 'Tab') setOpen(false)
    else if (e.key === 'ArrowDown') { e.preventDefault(); items[(i + 1) % items.length]?.focus() }
    else if (e.key === 'ArrowUp') { e.preventDefault(); items[(i - 1 + items.length) % items.length]?.focus() }
    else if (e.key === 'Home') { e.preventDefault(); items[0]?.focus() }
    else if (e.key === 'End') { e.preventDefault(); items.at(-1)?.focus() }
  }

  return (
    <div className="user-menu" ref={wrap}>
      <button ref={button} type="button" className="user-btn" aria-haspopup="menu" aria-expanded={open} aria-controls={open ? menuId : undefined}
        aria-label={`${t('nav.accountMenu')}: ${name}`} title={t('nav.loggedInAs', { user: user.username })}
        onClick={() => setOpen((o) => !o)}
        onKeyDown={(e) => { if (e.key === 'ArrowDown' && !open) { e.preventDefault(); setOpen(true) } }}>
        <span className="user-text"><span className="who-name">{name}</span><span className="who-role">{ROLE_SHORT_VI[user.role]}</span></span>
        <Avatar key={user.username} profile={profile} initial={initial} small />
        <IconChevronDown className="user-caret" aria-hidden="true" />
      </button>
      {open && (
        <div id={menuId} className="user-dropdown" role="menu" aria-label={t('nav.accountMenu')} onKeyDown={onMenuKey}>
          <div className="user-dropdown-head"><strong>{name}</strong><small>{user.username} · {ROLE_SHORT_VI[user.role]}</small></div>
          <Link role="menuitem" className="user-item" to="/account" onClick={() => close(false)}>{t('nav.accountInfo')}</Link>
          <button type="button" role="menuitem" className="user-item" onClick={() => { close(false); void logout() }}>{t('nav.logout')}</button>
        </div>
      )}
    </div>
  )
}
