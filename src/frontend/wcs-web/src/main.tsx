import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import './index.css'
import App from './App.tsx'
import { DEMO_MODE } from './config'
import { AuthProvider } from './auth/AuthContext'
import { RealtimeProvider } from './realtime/RealtimeProvider'

if (DEMO_MODE) {
  // Safety net: Demo Mode must never talk to the backend, even if some code path tried to.
  const realFetch = window.fetch.bind(window)
  window.fetch = (input, init) => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.pathname : input.url
    if (/(^|\/\/[^/]+)\/(api|hubs)\//.test(url)) {
      console.warn('[demo] blocked network call to', url)
      return Promise.reject(new TypeError('Network access is disabled in Demo Mode'))
    }
    return realFetch(input, init)
  }
  document.title = 'ETEK WCS'
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <AuthProvider>
        <RealtimeProvider>
          <App />
        </RealtimeProvider>
      </AuthProvider>
    </BrowserRouter>
  </StrictMode>,
)
