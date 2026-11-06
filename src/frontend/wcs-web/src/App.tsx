import { Route, Routes } from 'react-router-dom'
import { Layout } from './components/Layout'
import { EmptyState } from './components/ui'
import { AgvPage } from './pages/AgvPage'
import { DashboardPage } from './pages/DashboardPage'
import { InventoryPage } from './pages/InventoryPage'
import { TasksPage } from './pages/TasksPage'

function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<DashboardPage />} />
        <Route path="agvs" element={<AgvPage />} />
        <Route path="tasks" element={<TasksPage />} />
        <Route path="inventory" element={<InventoryPage />} />
        <Route path="*" element={<EmptyState title="Không tìm thấy trang" />} />
      </Route>
    </Routes>
  )
}

export default App
