import type { Tone } from './vi'

export const ENDPOINT_STATE_VI: Record<string, string> = {
  Connected: 'Đã kết nối', Connecting: 'Đang kết nối', Faulted: 'Lỗi / mất kết nối', Disconnected: 'Chưa kết nối',
  NeedsConfiguration: 'Cần cấu hình', Disabled: 'Đã tắt',
}
export const ENDPOINT_STATE_TONE: Record<string, Tone> = {
  Connected: 'ok', Connecting: 'warn', Faulted: 'bad', Disconnected: 'muted', NeedsConfiguration: 'warn', Disabled: 'muted',
}

export const MODE_VI: Record<string, string> = {
  Simulator: 'Chế độ mô phỏng', RealReadOnly: 'PLC THẬT – CHỈ ĐỌC (REAL_READ_ONLY)', RealReadWrite: 'PLC THẬT – ĐỌC/GHI (REAL_READ_WRITE)',
}
export const MODE_TONE: Record<string, Tone> = { Simulator: 'warn', RealReadOnly: 'warn', RealReadWrite: 'bad' }

export const QUALITY_VI: Record<string, string> = {
  Unknown: 'Chưa đọc', NotConfigured: 'Chưa cấu hình', Good: 'Tốt', Bad: 'Lỗi đọc', Disconnected: 'Mất kết nối', Simulated: 'Không từ thiết bị thật',
}
export const QUALITY_TONE: Record<string, Tone> = {
  Unknown: 'muted', NotConfigured: 'muted', Good: 'ok', Bad: 'bad', Disconnected: 'warn', Simulated: 'warn',
}

export const DIRECTION_VI: Record<string, string> = { PlcToSoftware: 'PLC → Phần mềm', SoftwareToPlc: 'Phần mềm → PLC' }
export const DATATYPE_VI: Record<string, string> = { Bool: 'BOOL', Int: 'INT', String: 'STRING' }

export const CONFIG_STATUS_VI: Record<string, string> = {
  NEEDS_CONFIGURATION: 'Cần cấu hình', CONFIGURED: 'Đã nhập địa chỉ', APPROVED: 'Đã xác nhận (kỹ sư PLC)',
}
export const CONFIG_STATUS_TONE: Record<string, Tone> = { NEEDS_CONFIGURATION: 'warn', CONFIGURED: 'info', APPROVED: 'ok' }

export const PHASE_VI: Record<string, string> = {
  Unknown: 'Chưa có dữ liệu', Offline: 'Trạm offline', Fault: 'Trạm lỗi', Idle: 'Rảnh', PickupRequested: 'Yêu cầu lấy rack',
  DeliveryRequested: 'Yêu cầu cấp rack', ConflictingRequests: 'Yêu cầu xung đột', TransferComplete: 'Báo chuyển giao xong',
}
export const PHASE_TONE: Record<string, Tone> = {
  Unknown: 'muted', Offline: 'warn', Fault: 'bad', Idle: 'ok', PickupRequested: 'info', DeliveryRequested: 'info', ConflictingRequests: 'bad', TransferComplete: 'info',
}

export const APPROVAL_VI: Record<string, string> = {
  NEEDS_CONFIRMATION: 'Chờ xác nhận quy trình handshake', PARTIALLY_APPROVED: 'Đã duyệt một phần',
}

export const EXCHANGE_OUTCOME_VI: Record<string, string> = {
  OK: 'Thành công', END_CODE: 'PLC báo lỗi (End code)', TIMEOUT: 'Hết thời gian chờ', FAILED: 'Lỗi truyền thông / phản hồi sai khung', AMBIGUOUS: 'Kết quả ghi KHÔNG XÁC ĐỊNH', CANCELLED: 'Đã hủy',
}

export const LOG_LEVEL_VI: Record<string, string> = { Info: 'Thông tin', Warning: 'Cảnh báo', Error: 'Lỗi' }
export const LOG_LEVEL_TONE: Record<string, Tone> = { Info: 'info', Warning: 'warn', Error: 'bad' }

export const MISSING_PARAM_VI: Record<string, string> = {
  port: 'Cổng TCP', protocol: 'Giao thức', networkNumber: 'Network number', pcNumber: 'PC number', moduleIoNumber: 'Module I/O number',
  plcStationNumber: 'PLC station number', monitoringTimer: 'Monitoring timer', timeoutMs: 'Timeout', pollingIntervalMs: 'Chu kỳ đọc',
}

export const PLC_ERROR_VI: Record<string, string> = {
  PLC_NOT_FOUND: 'Không tìm thấy PLC.',
  IO_POINT_NOT_FOUND: 'Không tìm thấy IO point.',
  IO_NO_ADDRESS: 'Cần nhập địa chỉ trước khi xác nhận.',
  IO_NOT_APPROVED: 'Chỉ tín hiệu đã được xác nhận mới được cho phép ghi.',
  IO_NOT_WRITABLE: 'Chỉ tín hiệu chiều Phần mềm → PLC mới ghi được.',
  WRITE_BLOCKED_SAFETY: 'Tín hiệu Safety/Interlock và Transfer/Departure Permit bị chặn ghi thử.',
  WRITE_DIRECTION: 'Chỉ ghi được tín hiệu chiều Phần mềm → PLC.',
  READ_ONLY: 'Hệ thống đang ở chế độ chỉ đọc: không được ghi.',
  WRITE_NOT_ALLOWED: 'Tín hiệu chưa nằm trong danh sách được phép ghi.',
  NO_ADDRESS: 'Tín hiệu chưa có địa chỉ được xác nhận.',
  NOT_CONNECTED: 'PLC chưa kết nối.',
  NOT_SIMULATOR_MODE: 'Chức năng này không khả dụng.',
  NOT_AVAILABLE_IN_PRODUCTION: 'Chức năng kỹ thuật này không khả dụng trong môi trường vận hành.',
  VALUE_INVALID: 'Giá trị không hợp lệ cho kiểu dữ liệu của tín hiệu.',
  TYPE_MISMATCH: 'Giá trị không đúng kiểu dữ liệu của tín hiệu.',
  WRITE_FAILED: 'Ghi xuống PLC thất bại.',
  WRITE_AMBIGUOUS: 'Lệnh ghi đã được gửi nhưng không nhận được xác nhận: KHÔNG được ghi lại tự động. Hãy đọc lại giá trị trên PLC để kiểm tra.',
  RADIX_UNCONFIRMED: 'Cơ số địa chỉ X/Y/W chưa được kỹ sư PLC xác nhận (NEEDS_CONFIRMATION).',
  X_READ_ONLY: 'Thiết bị X là ngõ vào phần cứng, không được ghi.',
  NEEDS_CONFIGURATION: 'Thiếu thông số PLC cần kỹ sư PLC xác nhận.',
}
