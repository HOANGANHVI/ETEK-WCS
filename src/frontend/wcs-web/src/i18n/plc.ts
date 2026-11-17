import { getLang, localizedMap } from './core'
import type { Tone } from './vi'

export const ENDPOINT_STATE_VI = localizedMap({
  Connected: 'Đã kết nối', Connecting: 'Đang kết nối', Faulted: 'Lỗi / mất kết nối', Disconnected: 'Chưa kết nối',
  NeedsConfiguration: 'Cần thiết lập', Disabled: 'Đã tắt',
}, {
  Connected: 'Connected', Connecting: 'Connecting', Faulted: 'Fault / disconnected', Disconnected: 'Not connected',
  NeedsConfiguration: 'Setup required', Disabled: 'Disabled',
})
export const ENDPOINT_STATE_TONE: Record<string, Tone> = {
  Connected: 'ok', Connecting: 'warn', Faulted: 'bad', Disconnected: 'muted', NeedsConfiguration: 'warn', Disabled: 'muted',
}

export const MODE_VI = localizedMap({
  Simulator: 'Chế độ mô phỏng', RealReadOnly: 'PLC THẬT – CHỈ ĐỌC (REAL_READ_ONLY)', RealReadWrite: 'PLC THẬT – ĐỌC/GHI (REAL_READ_WRITE)',
}, {
  Simulator: 'Simulation mode', RealReadOnly: 'REAL PLC – READ ONLY (REAL_READ_ONLY)', RealReadWrite: 'REAL PLC – READ/WRITE (REAL_READ_WRITE)',
})
export const MODE_TONE: Record<string, Tone> = { Simulator: 'warn', RealReadOnly: 'warn', RealReadWrite: 'bad' }

export const QUALITY_VI = localizedMap({
  Unknown: 'Chưa đọc', NotConfigured: 'Chưa thiết lập', Good: 'Tốt', Bad: 'Lỗi đọc', Disconnected: 'Mất kết nối', Simulated: 'Dữ liệu mô phỏng',
}, {
  Unknown: 'Not read', NotConfigured: 'Not set', Good: 'Good', Bad: 'Read error', Disconnected: 'Disconnected', Simulated: 'Simulated data',
})
export const QUALITY_TONE: Record<string, Tone> = {
  Unknown: 'muted', NotConfigured: 'muted', Good: 'ok', Bad: 'bad', Disconnected: 'warn', Simulated: 'warn',
}

export const CONFIG_STATUS_VI = localizedMap({
  NEEDS_CONFIGURATION: 'Cần thiết lập', CONFIGURED: 'Đã nhập địa chỉ', APPROVED: 'Đã xác nhận (kỹ sư PLC)',
}, {
  NEEDS_CONFIGURATION: 'Setup required', CONFIGURED: 'Address entered', APPROVED: 'Confirmed (PLC engineer)',
})
export const CONFIG_STATUS_TONE: Record<string, Tone> = { NEEDS_CONFIGURATION: 'warn', CONFIGURED: 'info', APPROVED: 'ok' }

export const PHASE_VI = localizedMap({
  Unknown: 'Chưa có dữ liệu', Offline: 'Trạm mất kết nối', Fault: 'Trạm lỗi', Idle: 'Rảnh', PickupRequested: 'Yêu cầu lấy rack',
  DeliveryRequested: 'Yêu cầu cấp rack', ConflictingRequests: 'Yêu cầu xung đột', TransferComplete: 'Báo chuyển giao xong',
}, {
  Unknown: 'No data', Offline: 'Station disconnected', Fault: 'Station fault', Idle: 'Idle', PickupRequested: 'Rack pick-up requested',
  DeliveryRequested: 'Rack delivery requested', ConflictingRequests: 'Conflicting requests', TransferComplete: 'Transfer complete reported',
})
export const PHASE_TONE: Record<string, Tone> = {
  Unknown: 'muted', Offline: 'warn', Fault: 'bad', Idle: 'ok', PickupRequested: 'info', DeliveryRequested: 'info', ConflictingRequests: 'bad', TransferComplete: 'info',
}

export const APPROVAL_VI = localizedMap(
  { NEEDS_CONFIRMATION: 'Chờ xác nhận quy trình handshake', PARTIALLY_APPROVED: 'Đã duyệt một phần' },
  { NEEDS_CONFIRMATION: 'Handshake procedure awaiting confirmation', PARTIALLY_APPROVED: 'Partially approved' })

export const EXCHANGE_OUTCOME_VI = localizedMap({
  OK: 'Thành công', END_CODE: 'PLC báo lỗi (End code)', TIMEOUT: 'Hết thời gian chờ', FAILED: 'Lỗi truyền thông / phản hồi sai khung', AMBIGUOUS: 'Kết quả ghi KHÔNG XÁC ĐỊNH', CANCELLED: 'Đã hủy',
}, {
  OK: 'Success', END_CODE: 'PLC reported an error (End code)', TIMEOUT: 'Timed out', FAILED: 'Communication error / malformed response', AMBIGUOUS: 'Write result is UNDETERMINED', CANCELLED: 'Cancelled',
})

export const LOG_LEVEL_VI = localizedMap(
  { Info: 'Thông tin', Warning: 'Cảnh báo', Error: 'Lỗi' },
  { Info: 'Information', Warning: 'Warning', Error: 'Error' })
export const LOG_LEVEL_TONE: Record<string, Tone> = { Info: 'info', Warning: 'warn', Error: 'bad' }

export const MISSING_PARAM_VI = localizedMap({
  port: 'Cổng TCP', protocol: 'Giao thức', networkNumber: 'Network number', pcNumber: 'PC number', moduleIoNumber: 'Module I/O number',
  plcStationNumber: 'PLC station number', monitoringTimer: 'Monitoring timer', timeoutMs: 'Timeout', pollingIntervalMs: 'Chu kỳ đọc',
}, {
  port: 'TCP port', protocol: 'Protocol', networkNumber: 'Network number', pcNumber: 'PC number', moduleIoNumber: 'Module I/O number',
  plcStationNumber: 'PLC station number', monitoringTimer: 'Monitoring timer', timeoutMs: 'Timeout', pollingIntervalMs: 'Polling interval',
})

export const PLC_ERROR_VI = localizedMap({
  PLC_NOT_FOUND: 'Không tìm thấy PLC.',
  IO_POINT_NOT_FOUND: 'Không tìm thấy IO point.',
  IO_NO_ADDRESS: 'Cần nhập địa chỉ trước khi xác nhận.',
  IO_NOT_APPROVED: 'Chỉ tín hiệu đã được xác nhận mới được cho phép ghi.',
  IO_NOT_WRITABLE: 'Chỉ tín hiệu chiều Phần mềm → PLC mới ghi được.',
  WRITE_BLOCKED_SAFETY: 'Tín hiệu Safety/Interlock và Transfer/Departure Permit bị chặn ghi thử.',
  WRITE_DIRECTION: 'Chỉ ghi được tín hiệu chiều Phần mềm → PLC.',
  READ_ONLY: 'Hệ thống đang ở chế độ chỉ đọc: không được ghi.',
  WRITE_NOT_ALLOWED: 'Tín hiệu không nằm trong danh sách được phép ghi.',
  NO_ADDRESS: 'Tín hiệu chưa có địa chỉ được xác nhận.',
  NOT_CONNECTED: 'PLC chưa kết nối.',
  NOT_SIMULATOR_MODE: 'Chức năng này không khả dụng.',
  NOT_AVAILABLE_IN_PRODUCTION: 'Chức năng này không khả dụng trong môi trường vận hành.',
  VALUE_INVALID: 'Giá trị không hợp lệ cho kiểu dữ liệu của tín hiệu.',
  TYPE_MISMATCH: 'Giá trị không đúng kiểu dữ liệu của tín hiệu.',
  WRITE_FAILED: 'Ghi xuống PLC thất bại.',
  WRITE_AMBIGUOUS: 'Lệnh ghi đã được gửi nhưng không nhận được xác nhận: KHÔNG được ghi lại tự động. Hãy đọc lại giá trị trên PLC để kiểm tra.',
  RADIX_UNCONFIRMED: 'Cơ số địa chỉ X/Y/W đang chờ kỹ sư PLC xác nhận.',
  X_READ_ONLY: 'Thiết bị X là ngõ vào phần cứng, không được ghi.',
  NEEDS_CONFIGURATION: 'Thiếu thông số PLC, cần bổ sung và xác nhận bởi kỹ sư PLC.',
}, {
  PLC_NOT_FOUND: 'PLC not found.',
  IO_POINT_NOT_FOUND: 'IO point not found.',
  IO_NO_ADDRESS: 'Enter an address before confirming.',
  IO_NOT_APPROVED: 'Only confirmed signals may be written.',
  IO_NOT_WRITABLE: 'Only Software → PLC signals can be written.',
  WRITE_BLOCKED_SAFETY: 'Safety/Interlock and Transfer/Departure Permit signals cannot be test-written.',
  WRITE_DIRECTION: 'Only Software → PLC signals can be written.',
  READ_ONLY: 'The system is in read-only mode: writing is not allowed.',
  WRITE_NOT_ALLOWED: 'The signal is not on the list of signals allowed for writing.',
  NO_ADDRESS: 'The signal has no confirmed address.',
  NOT_CONNECTED: 'The PLC is not connected.',
  NOT_SIMULATOR_MODE: 'This function is not available.',
  NOT_AVAILABLE_IN_PRODUCTION: 'This function is not available in the production environment.',
  VALUE_INVALID: 'The value is not valid for the signal data type.',
  TYPE_MISMATCH: 'The value does not match the signal data type.',
  WRITE_FAILED: 'Writing to the PLC failed.',
  WRITE_AMBIGUOUS: 'The write command was sent but no confirmation was received: do NOT retry automatically. Read the value back from the PLC to verify.',
  RADIX_UNCONFIRMED: 'The X/Y/W address radix is awaiting confirmation by the PLC engineer.',
  X_READ_ONLY: 'Device X is a hardware input and cannot be written.',
  NEEDS_CONFIGURATION: 'PLC parameters are missing; they must be supplied and confirmed by the PLC engineer.',
})

export const DIRECTION_VI = localizedMap(
  { PlcToSoftware: 'PLC → Phần mềm', SoftwareToPlc: 'Phần mềm → PLC' },
  { PlcToSoftware: 'PLC → Software', SoftwareToPlc: 'Software → PLC' })
export const DATATYPE_VI: Record<string, string> = { Bool: 'BOOL', Int: 'INT', String: 'STRING' }

/** Maps raw backend device messages to operator-facing text; unknown text is returned unchanged (never hidden). */
export function localizeDeviceMessage(msg: string | null | undefined): string {
  if (!msg) return ''
  if (/simulated PLC is offline/i.test(msg)) return getLang() === 'en' ? 'Simulated PLC: Not connected' : 'PLC mô phỏng: Chưa kết nối'
  return msg
}
