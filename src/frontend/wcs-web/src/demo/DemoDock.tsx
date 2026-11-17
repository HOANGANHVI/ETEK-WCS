import { useState, useSyncExternalStore } from 'react'
import { Button, Pill } from '../components/ui'
import { IconChevronDown, IconChevronUp, IconFlask, IconPause, IconPlay, IconReset, IconStep } from '../components/icons'
import { errorTextForCode, flowInfo, TASK_STATE_VI } from '../i18n/vi'
import { useT, type MsgKey } from '../i18n'
import type { TaskState } from '../api/types'
import { resetDemoConfig } from './demoConfig'
import { demo, DemoError } from './engine'

interface Scenario { label: MsgKey; hint: MsgKey; run: () => unknown }

const SCENARIOS: Scenario[] = [
  { label: 'demo.sc1', hint: 'demo.sc1Hint', run: () => demo.runScenario('BP01', 'A1') },
  { label: 'demo.sc2', hint: 'demo.sc2Hint', run: () => demo.runScenario('BP01', 'A2', { blockDestinations: true }) },
  { label: 'demo.sc3', hint: 'demo.sc3Hint', run: () => demo.runScenario('BP02', 'A2') },
  { label: 'demo.sc4', hint: 'demo.sc4Hint', run: () => demo.runScenario('BP03', 'B1') },
  { label: 'demo.sc5', hint: 'demo.sc5Hint', run: () => demo.runScenario('BP05', 'F5') },
  { label: 'demo.sc6', hint: 'demo.sc6Hint', run: () => demo.runScenario('BP06', 'F5') },
]

const FINISHED: TaskState[] = ['Completed', 'Failed', 'Cancelled', 'Queued', 'Pending']

/**
 * Simulation dock: a bottom toolbar that is part of the page layout (it never floats over content).
 * Everything here acts on the in-browser demo engine only.
 */
export function DemoDock() {
  useSyncExternalStore(demo.subscribeStore, demo.getVersion)
  const t = useT()
  const [open, setOpen] = useState(false)
  // The message is kept as a key + params (not as text) so it follows the language when the user switches it.
  const [message, setMessage] = useState<{ key: MsgKey; params?: Record<string, string>; raw?: string }>({ key: 'demo.intro' })
  const [speed, setSpeed] = useState(demo.autoplaySpeedMs)

  const act = (fn: () => unknown, ok: { key: MsgKey; params?: Record<string, string> } = { key: 'demo.done' }) => {
    try {
      fn()
      setMessage(ok)
    } catch (e) {
      // known codes use the shared translated texts; anything else gets a generic line (the engine's own wording is Vietnamese only)
      const reason = e instanceof DemoError ? (errorTextForCode(e.code) ?? t('demo.rejectedGeneric')) : null
      setMessage(reason !== null ? { key: 'demo.rejected', params: { reason } } : { key: 'demo.unexpected' })
    }
  }

  const agvs = demo.listAgvs()
  const active = demo.listTasks(undefined, 0, 100).items.filter((t) => !FINISHED.includes(t.state))
  const recon = demo.tasksNeedingReconciliation()
  const running = demo.autoplay

  return (
    <section className="dock" aria-label={t('demo.panelAria')}>
      <div className="dock-bar">
        <span className="dock-title"><IconFlask />{t('demo.title')}</span>
        {running ? (
          <Button size="sm" onClick={() => demo.stopAutoplay()}><IconPause />{t('demo.pause')}</Button>
        ) : (
          <Button size="sm" variant="primary" onClick={() => demo.startAutoplay(speed)}><IconPlay />{t('demo.start')}</Button>
        )}
        <Button size="sm" onClick={() => act(() => demo.stepAll(), { key: 'demo.stepDone' })}><IconStep />{t('demo.step')}</Button>
        <label className="dock-speed">
          <select value={speed} aria-label={t('demo.speedAria')} onChange={(e) => { const v = Number(e.target.value); setSpeed(v); if (demo.autoplay) demo.startAutoplay(v) }}>
            <option value={600}>{t('demo.speedFast')}</option><option value={1500}>{t('demo.speedMid')}</option><option value={3000}>{t('demo.speedSlow')}</option>
          </select>
        </label>
        <Button size="sm" variant="danger" onClick={() => act(() => { demo.reset(); resetDemoConfig() }, { key: 'demo.resetDone' })}><IconReset />{t('demo.reset')}</Button>
        <span className="dock-sep" />
        <span className="dock-msg" role="status">{t(message.key, message.params)}</span>
        <Button size="sm" onClick={() => setOpen((o) => !o)} aria-expanded={open}>
          {t('demo.scenariosToggle')} {open ? <IconChevronDown /> : <IconChevronUp />}
        </Button>
      </div>

      {open && (
        <div className="dock-body">
          <div className="dock-col">
            <h4>{t('demo.secScenarios')}</h4>
            <div className="dock-scenarios">
              {SCENARIOS.map((sc) => (
                <Button key={sc.label} title={t(sc.hint)} onClick={() => act(() => { sc.run() }, { key: 'demo.scenarioCreated', params: { label: t(sc.label), hint: t(sc.hint) } })}>{t(sc.label)}</Button>
              ))}
            </div>
            <h4>{t('demo.secStock')}</h4>
            <div className="dock-row">
              <Button size="sm" onClick={() => act(() => demo.removeStock('F1', 'Full', 1), { key: 'demo.freeF1Done' })}>{t('demo.freeF1')}</Button>
            </div>
          </div>

          <div className="dock-col">
            <h4>{t('demo.secIncidents')}</h4>
            {active.length === 0 ? <p className="empty">{t('demo.noActive')}</p> : (
              <ul className="dock-list">
                {active.map((tk) => (
                  <li key={tk.id}>
                    <span>{flowInfo(tk.flow).code} · {TASK_STATE_VI[tk.state]}</span>
                    <span className="dock-row">
                      <Button size="sm" variant="danger" onClick={() => act(() => demo.interruptTask(tk.id), { key: 'demo.interruptDone', params: { flow: tk.flow } })}>{t('demo.interrupt')}</Button>
                      <Button size="sm" onClick={() => act(() => demo.cancelTask(tk.id, t('demo.cancelReason')), { key: 'demo.cancelDone' })}>{t('demo.cancel')}</Button>
                    </span>
                  </li>
                ))}
              </ul>
            )}
            {recon.length > 0 && (
              <>
                <h4>{t('demo.secRecon')}</h4>
                <ul className="dock-list">
                  {recon.map((tk) => (
                    <li key={tk.id}>
                      <span>{flowInfo(tk.flow).code} · <Pill tone="bad" dot={false}>{TASK_STATE_VI[tk.state]}</Pill></span>
                      <Button size="sm" onClick={() => act(() => demo.reconcile(tk.id, demo.locationCode(tk.sourceLocationId)), { key: 'demo.reconDone', params: { loc: demo.locationCode(tk.sourceLocationId) } })}>
                        {t('demo.rackAt', { loc: demo.locationCode(tk.sourceLocationId) })}
                      </Button>
                    </li>
                  ))}
                </ul>
              </>
            )}
          </div>

          <div className="dock-col">
            <h4>{t('demo.secAgv')}</h4>
            <div className="dock-row">
              {agvs.map((a) => {
                const online = a.connectionStatus === 'Online'
                return (
                  <Button key={a.id} size="sm" onClick={() => act(() => demo.setAgvOnline(a.code, !online), { key: online ? 'demo.agvNowOffline' : 'demo.agvNowOnline', params: { code: a.code } })}>
                    {t(online ? 'demo.agvDisconnect' : 'demo.agvReconnect', { code: a.code })}
                  </Button>
                )
              })}
            </div>
            <h4>{t('demo.secNote')}</h4>
            <p className="empty">{t('demo.note')}</p>
          </div>
        </div>
      )}
    </section>
  )
}
