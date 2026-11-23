import { Navigate, Route, Routes } from 'react-router-dom'
import type { ReactNode } from 'react'
import type { Role } from './api/authTypes'
import { hasRole, useAuth } from './auth/AuthContext'
import { Layout } from './components/Layout'
import { EmptyState, Loading } from './components/ui'
import { useLang, useT } from './i18n'
import { AgvPage } from './pages/AgvPage'
import { AuditPage } from './pages/AuditPage'
import { LogsPage } from './pages/LogsPage'
import { ChangePasswordPage } from './pages/ChangePasswordPage'
import { AccountPage } from './pages/AccountPage'
import { ConfigPage } from './pages/ConfigPage'
import { DashboardPage } from './pages/DashboardPage'
import { InventoryPage } from './pages/InventoryPage'
import { LoginPage } from './pages/LoginPage'
import { ConfigTransferPage } from './pages/ConfigTransferPage'
import { DiagnosticsPage } from './pages/DiagnosticsPage'
import { RcsDiagnosticsPage } from './pages/RcsDiagnosticsPage'
import { PlcConnectionsPage } from './pages/PlcConnectionsPage'
import { PlcIoMappingPage } from './pages/PlcIoMappingPage'
import { PlcPage } from './pages/PlcPage'
import { ProcessesPage } from './pages/ProcessesPage'
import { RoutingPage } from './pages/RoutingPage'
import { TasksPage } from './pages/TasksPage'
import { UsersPage } from './pages/UsersPage'

/**
 * Direct URL access to an area the account may not use goes back to the home page with a message.
 * This only improves the experience: every API behind these pages is authorized again on the server.
 */
function RequireRole({ roles, children }: { roles: Role[]; children: ReactNode }) {
  const { user } = useAuth()
  if (!hasRole(user, ...roles)) {
    return <Navigate to="/" replace state={{ denied: 'nav.denied' }} />
  }
  return <>{children}</>
}

function App() {
  useLang() // re-render the whole route tree when the language changes
  const t = useT()
  const { status, user } = useAuth()
  if (status === 'loading') return <div className="boot"><Loading label={t('auth.checkingSession')} /></div>
  if (status === 'anonymous') return <LoginPage />
  if (user?.mustChangePassword) return <ChangePasswordPage forced />

  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<DashboardPage />} />
        <Route path="agvs" element={<AgvPage />} />
        <Route path="tasks" element={<TasksPage />} />
        <Route path="inventory" element={<InventoryPage />} />
        <Route path="routing" element={<RoutingPage />} />
        <Route path="processes" element={<RequireRole roles={['Admin']}><ProcessesPage /></RequireRole>} />
        <Route path="plc" element={<PlcPage />} />
        <Route path="plc/connections" element={<RequireRole roles={['Admin']}><PlcConnectionsPage /></RequireRole>} />
        <Route path="diagnostics" element={<RequireRole roles={['Admin']}><DiagnosticsPage /></RequireRole>} />
        <Route path="plc/io-mapping" element={<RequireRole roles={['Admin']}><PlcIoMappingPage /></RequireRole>} />
        <Route path="account" element={<AccountPage />} />
        <Route path="account/password" element={<Navigate to="/account" replace />} />
        <Route path="config" element={<RequireRole roles={['Admin']}><ConfigPage /></RequireRole>} />
        <Route path="config-transfer" element={<RequireRole roles={['Admin']}><ConfigTransferPage /></RequireRole>} />
        <Route path="users" element={<RequireRole roles={['Admin']}><UsersPage /></RequireRole>} />
        <Route path="audit" element={<RequireRole roles={['Admin']}><AuditPage /></RequireRole>} />
        <Route path="rcs-diagnostics" element={<RequireRole roles={['Admin']}><RcsDiagnosticsPage /></RequireRole>} />
        <Route path="logs" element={<RequireRole roles={['Admin']}><LogsPage /></RequireRole>} />
        <Route path="*" element={<EmptyState title={t('common.notFound')} />} />
      </Route>
    </Routes>
  )
}

export default App
