import { useEffect, useRef, useState } from 'react'
import { api } from '../api/client'
import type { ResourceSnapshot } from '../api/types'
import { connectionState, pollSeconds, type ConnectionState } from '../lib/resourceMonitor'

const REQUEST_TIMEOUT_MS = 8000

export interface ResourceMonitorState {
  snapshot: ResourceSnapshot | null
  connection: ConnectionState
  /** Local time (ms) of the last successful answer; null before the first one. */
  lastOkAt: number | null
}

/**
 * THE polling loop of the server-resource panel (one per page, nothing else polls this API). It asks every few seconds (the server's sampling interval),
 * pauses while the tab is hidden and asks again at once when it comes back. A failed request keeps the previous snapshot only as "last known" for the
 * timestamp: the connection state turns to 'disconnected' and the panel stops showing its numbers as current values.
 */
export function useResourceMonitor(enabled: boolean): ResourceMonitorState {
  const [snapshot, setSnapshot] = useState<ResourceSnapshot | null>(null)
  const [lastOkAt, setLastOkAt] = useState<number | null>(null)
  const [lastFailed, setLastFailed] = useState(false)
  const [now, setNow] = useState(() => Date.now())
  const intervalRef = useRef(5)

  useEffect(() => {
    if (!enabled) return
    let cancelled = false
    let timer: ReturnType<typeof setTimeout> | undefined
    let inFlight = false                      // a second trigger (tab visible again) never starts a second loop
    const poll = async () => {
      if (cancelled || inFlight) return
      if (document.visibilityState === 'hidden') { timer = setTimeout(poll, 1000); return }
      // A request that never answers must not stop the loop: it is cancelled after REQUEST_TIMEOUT_MS and counted as a failure.
      inFlight = true
      const controller = new AbortController()
      let timeout: ReturnType<typeof setTimeout> | undefined
      try {
        const s = await Promise.race([
          api.resources(controller.signal),
          new Promise<never>((_, reject) => { timeout = setTimeout(() => { controller.abort(); reject(new Error('timeout')) }, REQUEST_TIMEOUT_MS) }),
        ])
        if (cancelled) return
        intervalRef.current = pollSeconds(s)
        setSnapshot(s); setLastOkAt(Date.now()); setLastFailed(false)
      } catch {
        if (cancelled) return
        setLastFailed(true)
      } finally { inFlight = false; if (timeout) clearTimeout(timeout) }
      if (!cancelled) timer = setTimeout(poll, intervalRef.current * 1000)
    }
    void poll()
    const onVisible = () => { if (document.visibilityState === 'visible') { if (timer) clearTimeout(timer); void poll() } }
    document.addEventListener('visibilitychange', onVisible)
    const tick = setInterval(() => setNow(Date.now()), 1000)     // so that data that stops arriving is noticed without any new response
    return () => { cancelled = true; if (timer) clearTimeout(timer); clearInterval(tick); document.removeEventListener('visibilitychange', onVisible) }
  }, [enabled])

  return { snapshot, lastOkAt, connection: connectionState({ snapshot, lastOkAt, now, lastFailed }) }
}
