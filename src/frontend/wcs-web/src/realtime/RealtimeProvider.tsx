import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { useAuth } from '../auth/AuthContext'
import { DEMO_MODE } from '../config'
import { demo } from '../demo/engine'
import { createWcsConnection, toStatus, WCS_EVENTS, type ConnectionStatus, type WcsEventName } from './signalr'

type Listener = (name: WcsEventName, payload: unknown) => void

interface RealtimeContextValue {
  status: ConnectionStatus
  subscribe: (listener: Listener) => () => void
}

const RealtimeContext = createContext<RealtimeContextValue>({ status: 'disconnected', subscribe: () => () => {} })

// eslint-disable-next-line react-refresh/only-export-components
export const useRealtime = () => useContext(RealtimeContext)

export function RealtimeProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<ConnectionStatus>(DEMO_MODE ? 'demo' : 'connecting')
  const listeners = useRef(new Set<Listener>())
  const { status: authStatus, user } = useAuth()
  // The hub requires the same session cookie as the REST API, so only connect once signed in (and not while a password change is pending).
  const signedIn = authStatus === 'authenticated' && !user?.mustChangePassword

  useEffect(() => {
    // Demo Mode: events come from the in-browser engine and no SignalR connection is ever opened.
    if (!signedIn) return
    if (DEMO_MODE) return demo.on((name, payload) => listeners.current.forEach((l) => l(name, payload)))

    const connection = createWcsConnection()
    let disposed = false
    for (const name of WCS_EVENTS) {
      connection.on(name, (payload: unknown) => listeners.current.forEach((l) => l(name, payload)))
    }
    connection.onreconnecting(() => setStatus('reconnecting'))
    connection.onreconnected(() => setStatus('connected'))
    connection.onclose(() => !disposed && setStatus('disconnected'))

    connection
      .start()
      .then(() => !disposed && setStatus(toStatus(connection.state)))
      .catch(() => !disposed && setStatus('disconnected'))

    return () => {
      disposed = true
      void connection.stop()
    }
  }, [signedIn])

  const subscribe = useCallback((listener: Listener) => {
    listeners.current.add(listener)
    return () => { listeners.current.delete(listener) }
  }, [])

  const value = useMemo(() => ({ status, subscribe }), [status, subscribe])
  return <RealtimeContext.Provider value={value}>{children}</RealtimeContext.Provider>
}
