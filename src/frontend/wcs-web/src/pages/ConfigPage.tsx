import { useSearchParams } from 'react-router-dom'
import { PageHeader } from '../components/ui'
import { OperationsTab } from './config/OperationsTab'
import { RcsTab } from './config/RcsTab'

const TABS = [
  { key: 'rcs', label: 'Kết nối RCS' },
  { key: 'ops', label: 'Tham số vận hành' },
] as const

export function ConfigPage() {
  const [params, setParams] = useSearchParams()
  const tab = TABS.find((t) => t.key === params.get('tab'))?.key ?? 'rcs'
  return (
    <>
      <PageHeader
        title="Cấu hình hệ thống"
        description="Khai báo kết nối HIK RCS-2000 và các tham số vận hành. Kết nối và địa chỉ PLC được quản lý ở nhóm menu 'Cấu hình PLC'. Cấu hình được lưu bền vững; thông tin xác thực được mã hóa và không bao giờ hiển thị lại."
      />
      <div className="tabs" role="tablist" aria-label="Nhóm cấu hình">
        {TABS.map((t) => (
          <button key={t.key} type="button" role="tab" id={`tab-${t.key}`} aria-selected={tab === t.key} aria-controls={`panel-${t.key}`}
            className={tab === t.key ? 'tab active' : 'tab'} onClick={() => setParams({ tab: t.key })}>{t.label}</button>
        ))}
      </div>
      <div role="tabpanel" id={`panel-${tab}`} aria-labelledby={`tab-${tab}`}>
        {tab === 'rcs' && <RcsTab />}
        {tab === 'ops' && <OperationsTab />}
      </div>
    </>
  )
}
