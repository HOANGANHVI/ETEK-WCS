import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
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
  const [status, setStatus] = useState<ConnectionStatus>('connecting')
  const listeners = useRef(new Set<Listener>())

  useEffect(() => {
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
  }, [])

  const subscribe = useCallback((listener: Listener) => {
    listeners.current.add(listener)
    return () => { listeners.current.delete(listener) }
  }, [])

  const value = useMemo(() => ({ status, subscribe }), [status, subscribe])
  return <RealtimeContext.Provider value={value}>{children}</RealtimeContext.Provider>
}
