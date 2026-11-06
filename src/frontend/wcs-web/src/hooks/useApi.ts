import { useEffect, useState } from 'react'
import { describeError } from '../i18n/vi'
import { useRealtime } from '../realtime/RealtimeProvider'
import type { WcsEventName } from '../realtime/signalr'

export interface ApiState<T> {
  data: T | null
  error: string | null
  loading: boolean
  reload: () => void
}

interface Result<T> {
  source: unknown
  data: T | null
  error: string | null
}

/**
 * Loads data and, optionally, reloads (debounced) when one of the given SignalR events arrives.
 * `fetcher` must be stable (wrap it in useCallback) - a changed fetcher starts a new load and shows the loading state.
 * Refreshes (manual or realtime) keep the previous data visible.
 */
export function useApi<T>(fetcher: () => Promise<T>, refreshOn: WcsEventName[] = []): ApiState<T> {
  const [result, setResult] = useState<Result<T>>({ source: null, data: null, error: null })
  const [nonce, setNonce] = useState(0)
  const { subscribe } = useRealtime()

  useEffect(() => {
    let cancelled = false
    fetcher()
      .then((data) => { if (!cancelled) setResult({ source: fetcher, data, error: null }) })
      .catch((e: unknown) => {
        if (!cancelled) setResult((prev) => ({ source: fetcher, data: prev.data, error: describeError(e) }))
      })
    return () => { cancelled = true }
  }, [fetcher, nonce])

  const key = refreshOn.join(',')
  useEffect(() => {
    if (!key) return
    let timer: ReturnType<typeof setTimeout> | undefined
    const names = new Set(key.split(','))
    const off = subscribe((name) => {
      if (!names.has(name)) return
      clearTimeout(timer)
      timer = setTimeout(() => setNonce((n) => n + 1), 250)
    })
    return () => { off(); clearTimeout(timer) }
  }, [key, subscribe])

  return {
    data: result.data,
    error: result.error,
    loading: result.source !== fetcher,
    reload: () => setNonce((n) => n + 1),
  }
}
