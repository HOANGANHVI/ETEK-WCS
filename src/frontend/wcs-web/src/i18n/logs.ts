import type { LogCategory, LogLevel } from '../api/logTypes'
import type { Tone } from './vi'

export const LOG_CATEGORY_VI: Record<LogCategory, string> = {
  SYSTEM: 'Hệ thống', PLC: 'PLC', DATABASE: 'Cơ sở dữ liệu', DEPLOYMENT: 'Triển khai', RCS_AGV: 'RCS / AGV', SECURITY_AUDIT: 'Bảo mật / Thao tác',
}

export const LOG_CATEGORY_HINT: Record<LogCategory, string> = {
  SYSTEM: 'Khởi động, tắt, cấu hình, dịch vụ nền',
  PLC: 'Kết nối, timeout, lỗi đọc/ghi, MC End Code',
  DATABASE: 'Mất/khôi phục kết nối, migration',
  DEPLOYMENT: 'Cài đặt, cập nhật, backup, restore',
  RCS_AGV: 'Giao tiếp RCS, trạng thái AGV',
  SECURITY_AUDIT: 'Đăng nhập, phân quyền, thao tác quản trị',
}

export const LOG_LEVEL_VI: Record<LogLevel, string> = { Information: 'Thông tin', Warning: 'Cảnh báo', Error: 'Lỗi', Critical: 'Nghiêm trọng' }
export const LOG_LEVEL_TONE: Record<LogLevel, Tone> = { Information: 'info', Warning: 'warn', Error: 'bad', Critical: 'bad' }
