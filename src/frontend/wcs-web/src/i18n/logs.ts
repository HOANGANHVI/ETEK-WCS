import type { LogCategory, LogLevel } from '../api/logTypes'
import { localizedMap } from './core'
import type { Tone } from './vi'

export const LOG_CATEGORY_VI = localizedMap({
  SYSTEM: 'Hệ thống', PLC: 'PLC', DATABASE: 'Cơ sở dữ liệu', DEPLOYMENT: 'Triển khai', RCS_AGV: 'RCS / AGV', SECURITY_AUDIT: 'Bảo mật / Thao tác',
}, {
  SYSTEM: 'System', PLC: 'PLC', DATABASE: 'Database', DEPLOYMENT: 'Deployment', RCS_AGV: 'RCS / AGV', SECURITY_AUDIT: 'Security / Operations',
}) as Record<LogCategory, string>

export const LOG_CATEGORY_HINT = localizedMap({
  SYSTEM: 'Khởi động, tắt, cấu hình, dịch vụ nền',
  PLC: 'Kết nối, timeout, lỗi đọc/ghi, MC End Code',
  DATABASE: 'Mất/khôi phục kết nối, migration',
  DEPLOYMENT: 'Cài đặt, cập nhật, backup, restore',
  RCS_AGV: 'Giao tiếp RCS, trạng thái AGV',
  SECURITY_AUDIT: 'Đăng nhập, phân quyền, thao tác quản trị',
}, {
  SYSTEM: 'Startup, shutdown, configuration, background services',
  PLC: 'Connection, timeout, read/write errors, MC End Code',
  DATABASE: 'Connection loss/recovery, migrations',
  DEPLOYMENT: 'Install, update, backup, restore',
  RCS_AGV: 'RCS communication, AGV status',
  SECURITY_AUDIT: 'Sign-in, authorization, administrative actions',
}) as Record<LogCategory, string>

export const LOG_LEVEL_VI = localizedMap(
  { Information: 'Thông tin', Warning: 'Cảnh báo', Error: 'Lỗi', Critical: 'Nghiêm trọng' },
  { Information: 'Information', Warning: 'Warning', Error: 'Error', Critical: 'Critical' }) as Record<LogLevel, string>
export const LOG_LEVEL_TONE: Record<LogLevel, Tone> = { Information: 'info', Warning: 'warn', Error: 'bad', Critical: 'bad' }
