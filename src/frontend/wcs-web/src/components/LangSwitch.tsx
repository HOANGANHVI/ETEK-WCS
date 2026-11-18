import { useLang, useT, type Lang } from '../i18n'

const OPTIONS: { value: Lang; label: string }[] = [{ value: 'vi', label: 'VI' }, { value: 'en', label: 'EN' }]

/** Compact VI | EN switch. The language changes immediately and is remembered in localStorage. */
export function LangSwitch({ onLight = false }: { onLight?: boolean }) {
  const { lang, setLang } = useLang()
  const t = useT()
  return (
    <div className={onLight ? 'lang-switch on-light' : 'lang-switch'} role="group" aria-label={t('nav.langSwitch')}>
      {OPTIONS.map((o) => (
        <button key={o.value} type="button" className={lang === o.value ? 'active' : ''} aria-pressed={lang === o.value}
          lang={o.value} onClick={() => setLang(o.value)}>{o.label}</button>
      ))}
    </div>
  )
}
