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

/** CR-020: communication status of the monitoring screen. */
export const MONITOR_STATUS_VI = localizedMap({
  Good: 'Good', Stale: 'Stale', Bad: 'Bad', Offline: 'Offline', Unknown: 'Unknown', NotConfigured: 'Chưa thiết lập', Simulated: 'Mô phỏng',
}, {
  Good: 'Good', Stale: 'Stale', Bad: 'Bad', Offline: 'Offline', Unknown: 'Unknown', NotConfigured: 'Not set', Simulated: 'Simulated',
})
export const MONITOR_STATUS_TONE: Record<string, Tone> = { Good: 'ok', Stale: 'warn', Bad: 'bad', Offline: 'bad', Unknown: 'muted', NotConfigured: 'muted', Simulated: 'warn' }

/** CR-021: system diagnostic export - job state, source status, module names. */
export const DIAG_STATE_VI = localizedMap({
  Queued: 'Đang chờ', Running: 'Đang chạy', Completed: 'Hoàn tất', Failed: 'Thất bại', Expired: 'Đã hết hạn',
}, { Queued: 'Queued', Running: 'Running', Completed: 'Completed', Failed: 'Failed', Expired: 'Expired' })
export const DIAG_STATE_TONE: Record<string, Tone> = { Queued: 'muted', Running: 'warn', Completed: 'ok', Failed: 'bad', Expired: 'muted' }
export const DIAG_SOURCE_VI = localizedMap({
  OK: 'Đầy đủ', Partial: 'Một phần', Failed: 'Không đọc được', NotRequested: 'Ngoài phạm vi',
}, { OK: 'Complete', Partial: 'Partial', Failed: 'Could not be read', NotRequested: 'Out of scope' })
export const DIAG_SOURCE_TONE: Record<string, Tone> = { OK: 'ok', Partial: 'warn', Failed: 'bad', NotRequested: 'muted' }
export const DIAG_MODULE_VI = localizedMap({
  System: 'Hệ thống', Database: 'Cơ sở dữ liệu', Rcs: 'RCS', Agv: 'AGV', Plc: 'PLC', Task: 'Task / điều phối', Routing: 'Routing', Station: 'Station / IO',
  Inventory: 'Tồn kho / vị trí', Auth: 'Đăng nhập / phân quyền', Audit: 'Audit', Api: 'API / SignalR', Deployment: 'Triển khai', Config: 'Cấu hình',
}, {
  System: 'System', Database: 'Database', Rcs: 'RCS', Agv: 'AGV', Plc: 'PLC', Task: 'Task / dispatching', Routing: 'Routing', Station: 'Station / IO',
  Inventory: 'Inventory / location', Auth: 'Authentication / authorization', Audit: 'Audit', Api: 'API / SignalR', Deployment: 'Deployment', Config: 'Configuration',
})

export const CONFIG_STATUS_VI = localizedMap({
  NEEDS_CONFIGURATION: 'Cần thiết lập', CONFIGURED: 'Đã nhập địa chỉ', APPROVED: 'Đã xác nhận (kỹ sư PLC)',
}, {
  NEEDS_CONFIGURATION: 'Setup required', CONFIGURED: 'Address entered', APPROVED: 'Confirmed (PLC engineer)',
})
export const CONFIG_STATUS_TONE: Record<string, Tone> = { NEEDS_CONFIGURATION: 'warn', CONFIGURED: 'info', APPROVED: 'ok' }

export const PHASE_VI = localizedMap({
  Unknown: 'Chưa có dữ liệu', Offline: 'Trạm mất kết nối', Fault: 'Trạm lỗi', Idle: 'Rảnh', PickupRequested: 'Yêu cầu lấy rack',
  DeliveryRequested: 'Yêu cầu cấp rack', ConflictingRequests: 'Yêu cầu xung đột', TransferComplete: 'Báo chuyển giao xong', NotImplemented: 'Chưa hiện thực handshake',
}, {
  Unknown: 'No data', Offline: 'Station disconnected', Fault: 'Station fault', Idle: 'Idle', PickupRequested: 'Rack pick-up requested',
  DeliveryRequested: 'Rack delivery requested', ConflictingRequests: 'Conflicting requests', TransferComplete: 'Transfer complete reported', NotImplemented: 'Handshake not implemented',
})
export const PHASE_TONE: Record<string, Tone> = {
  Unknown: 'muted', Offline: 'warn', Fault: 'bad', Idle: 'ok', PickupRequested: 'info', DeliveryRequested: 'info', ConflictingRequests: 'bad', TransferComplete: 'info', NotImplemented: 'muted',
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
  BULK_APPROVAL_NOT_ALLOWED: 'Duyệt đọc hàng loạt không được bật cho PLC này.',
  BULK_APPROVAL_NOT_CONFIRMED: 'Cần xác nhận và số điểm đủ điều kiện đã xem ở bản xem trước.',
  BULK_APPROVAL_PREVIEW_STALE: 'Số tín hiệu đủ điều kiện đã thay đổi từ lúc xem trước. Hãy mở lại hộp thoại và xác nhận lại.',
  DIAGNOSTIC_RANGE_INVALID: 'Khoảng thời gian không hợp lệ (thời điểm kết thúc phải sau thời điểm bắt đầu; chỉ chọn 1 giờ hoặc 24 giờ gần nhất).',
  DIAGNOSTIC_RANGE_TOO_LARGE: 'Khoảng thời gian vượt quá giới hạn cho phép.',
  DIAGNOSTIC_EXPORT_FAILED: 'Không đọc được dữ liệu chẩn đoán nên chưa xuất được gói. Hãy xem nhật ký backend.',
  IO_POINT_NOT_FOUND: 'Không tìm thấy IO point.',
  IO_NO_ADDRESS: 'Cần nhập địa chỉ trước khi xác nhận.',
  IO_NOT_APPROVED: 'Chỉ tín hiệu đã được xác nhận mới được cho phép ghi.',
  IO_NOT_WRITABLE: 'Chỉ tín hiệu chiều Phần mềm → PLC mới ghi được.',
  WRITE_BLOCKED_SAFETY: 'Tín hiệu Safety/Interlock và Transfer/Departure Permit bị chặn ghi thử.',
  WRITE_DIRECTION: 'Chỉ ghi được tín hiệu chiều Phần mềm → PLC.',
  READ_ONLY: 'Hệ thống đang ở chế độ chỉ đọc: không được ghi.',
  WRITE_NOT_ALLOWED: 'Tín hiệu không nằm trong danh sách được phép ghi.',
  WRITE_SIGNAL_NOT_CONFIRMED: 'Tín hiệu của IO Map chưa được kỹ sư PLC xác nhận: không duyệt ghi và không ghi ra PLC thật.',
  IO_POINT_RETIRED: 'Tín hiệu thuộc IO Map cũ, không còn dùng: chỉ được xóa địa chỉ.',
  IO_NO_DEFAULT_ADDRESS: 'IO Map không có địa chỉ cho tín hiệu này.',
  WRITE_NOT_DECLARED: 'Điểm ghi chưa được khai báo trong cấu hình triển khai (Plc:Write:AllowList): không ghi tới PLC thật.',
  WRITE_ADDRESS_RESERVED: 'Địa chỉ chạm thanh ghi dành riêng của giao diện PLC chưa được xác nhận: bị chặn ghi.',
  WRITE_RESERVED_CONFIG_INVALID: 'Danh sách thanh ghi dành riêng không đọc được: mọi lệnh ghi tới PLC thật bị chặn.',
  WRITE_AUDIT_UNAVAILABLE: 'Không lưu được nhật ký kiểm toán nên lệnh ghi không được gửi.',
  WRITE_POINT_UNKNOWN: 'Điểm ghi không khớp cấu hình của kết nối PLC.',
  OUTCOME_UNKNOWN: 'Chưa rõ kết quả lệnh ghi: hãy đọc lại giá trị trước khi thao tác tiếp.',
  NO_ADDRESS: 'Tín hiệu chưa có địa chỉ được xác nhận.',
  NOT_CONNECTED: 'PLC chưa kết nối.',
  NOT_SIMULATOR_MODE: 'Chức năng này không khả dụng.',
  NOT_AVAILABLE_IN_PRODUCTION: 'Chức năng này không khả dụng trong môi trường vận hành.',
  VALUE_INVALID: 'Giá trị không hợp lệ cho kiểu dữ liệu của tín hiệu.',
  VALUE_OUT_OF_RANGE: 'Giá trị nằm ngoài miền của kiểu dữ liệu (INT: −32768…32767).',
  STRING_NOT_ASCII: 'STRING chỉ chấp nhận ký tự ASCII.',
  STRING_TOO_LONG: 'STRING dài hơn số word đã cấu hình.',
  STRING_LENGTH_UNKNOWN: 'STRING chưa có độ dài (word) được cấu hình.',
  WRITE_TYPE_UNSUPPORTED: 'Chưa hỗ trợ ghi cho kiểu dữ liệu hoặc encoding này.',
  WRITE_DECISION_INVALID: 'Quyết định phải là Approve hoặc Reject.',
  TYPE_MISMATCH: 'Giá trị không đúng kiểu dữ liệu của tín hiệu.',
  WRITE_FAILED: 'Ghi xuống PLC thất bại.',
  READ_FAILED: 'Đọc PLC thất bại.',
  DIAGNOSTIC_MODULE_INVALID: 'Tên module không hợp lệ.',
  DIAGNOSTIC_LEVEL_INVALID: 'Mức lỗi phải là Information, Warning, Error hoặc Critical.',
  DIAGNOSTIC_EXPORT_BUSY: 'Đang có quá nhiều lần xuất chẩn đoán. Hãy thử lại khi một lần xuất đã xong.',
  DIAGNOSTIC_JOB_NOT_FOUND: 'Không tìm thấy lần xuất (có thể đã hết hạn 24 giờ).',
  DIAGNOSTIC_JOB_NOT_READY: 'Lần xuất chưa xong, đã thất bại hoặc file đã hết hạn.',
  DIAGNOSTIC_EXPORT_TIMEOUT: 'Lần xuất vượt quá thời gian cho phép.',
  NOT_CONFIGURED: 'IO chưa có địa chỉ hợp lệ nên không đọc được.',
  WRITE_AMBIGUOUS: 'Lệnh ghi đã được gửi nhưng không nhận được xác nhận: KHÔNG được ghi lại tự động. Hãy đọc lại giá trị trên PLC để kiểm tra.',
  RADIX_UNCONFIRMED: 'Cơ số địa chỉ X/Y/W đang chờ kỹ sư PLC xác nhận.',
  X_READ_ONLY: 'Thiết bị X là ngõ vào phần cứng, không được ghi.',
  NEEDS_CONFIGURATION: 'Thiếu thông số PLC, cần bổ sung và xác nhận bởi kỹ sư PLC.',
  ORDER_NOT_FOUND: 'Không tìm thấy Transport Task.',
  STATE_INVALID: 'Trạng thái không hợp lệ.',
  STATE_NOT_ALLOWED: 'Không thể đặt trực tiếp trạng thái này; hãy dùng thao tác riêng.',
  PLC_ID_INVALID: 'Mã PLC chỉ gồm chữ, số, gạch ngang, gạch dưới (tối đa 20 ký tự).',
  PLC_NAME_REQUIRED: 'Cần nhập tên PLC.',
  PLC_IP_REQUIRED: 'Cần nhập địa chỉ IP.',
  PLC_EXISTS: 'Mã PLC đã tồn tại.',
  STATION_ID_INVALID: 'Mã cổng chỉ gồm chữ, số, gạch ngang, gạch dưới (tối đa 20 ký tự).',
  STATION_DUPLICATE: 'Mã cổng bị trùng.',
  STATION_EXISTS: 'Mã cổng đã tồn tại.',
  LOCATION_ALREADY_LINKED: 'Vị trí này đã liên kết với một cổng khác.',
  PORT_BUSY: 'Cổng đang có một Station Transaction hoạt động.',
  PROCESS_NOT_FOUND: 'Không tìm thấy quy trình.',
  VERSION_NOT_FOUND: 'Không tìm thấy phiên bản.',
  FLOW_NOT_AVAILABLE: 'Quy trình chưa được công bố hoặc chưa sẵn sàng ở “Quy trình và tuyến”: không tạo được nhiệm vụ.',
  LOCATION_CODE_CONFIRMATION: 'Hãy gõ đúng mã vị trí để xác nhận xóa.',
  LOCATION_DELETE_PROTECTED: 'Không xóa được vị trí: Là trạm được bảo vệ (giữ nguyên quy trình cũ).',
  LOCATION_DELETE_RACKS: 'Không xóa được vị trí: Đang chứa rack.',
  LOCATION_DELETE_PORT: 'Không xóa được vị trí: Đang liên kết với cổng PLC (bỏ liên kết trước).',
  LOCATION_DELETE_PROCESS: 'Không xóa được vị trí: Được quy trình sử dụng.',
  LOCATION_DELETE_OPEN_WORK: 'Không xóa được vị trí: còn việc chưa kết thúc tại vị trí này.',
  PROCESS_PROTECTED: 'Quy trình được bảo vệ (BP-04 đang chờ duyệt) không xóa được; chỉ có thể tắt.',
  PROCESS_HOLDS_PORT: 'Còn cổng trạm bị lệnh của quy trình này giữ: hãy xác nhận AMR đã rời cổng (hoặc đối soát) trước.',
  PROCESS_CODE_CONFIRMATION: 'Hãy gõ đúng mã quy trình để xác nhận xóa.',
  PROCESS_HAS_OPEN_ORDERS: 'Quy trình còn lệnh vận chuyển chưa kết thúc: hãy hoàn tất hoặc hủy các lệnh đó trước.',
  PROCESS_DISABLE_FIRST: 'Quy trình đã công bố và đang bật: hãy tắt (bỏ chọn "bật" rồi lưu) trước khi xóa.',
  VERSION_NOT_DELETABLE: 'Chỉ xóa được phiên bản nháp hoặc đã kiểm tra; phiên bản đã công bố hoặc vô hiệu được giữ lại.',
  VERSION_IN_USE: 'Phiên bản đang được lệnh vận chuyển sử dụng nên được giữ lại.',
  VERSION_ONLY_ONE: 'Đây là phiên bản duy nhất: hãy xóa cả quy trình.',
  REASON_TOO_LONG: 'Lý do dài tối đa 500 ký tự.',
  PROCESS_CODE_INVALID: 'Mã quy trình không hợp lệ (bắt buộc, tối đa 60 ký tự).',
  PROCESS_CODE_EXISTS: 'Mã quy trình đã tồn tại.',
  VERSION_NOT_EDITABLE: 'Phiên bản đã Publish hoặc vô hiệu hóa không được sửa; hãy tạo phiên bản mới.',
  VERSION_NOT_VALIDATED: 'Hãy kiểm tra (Validate) phiên bản trước khi Publish.',
  PUBLISH_REFUSED: 'Không thể Publish: cấu hình không còn hợp lệ.',
  DEFINITION_INVALID: 'Cấu hình chứa giá trị không được hỗ trợ.',
  PROCESS_DISABLED: 'Quy trình đang bị tắt.',
  PROCESS_REVIEW_BLOCKED: 'Quy trình đang REVIEW, chưa được duyệt triển khai.',
  PROCESS_NOT_PUBLISHED: 'Quy trình chưa có phiên bản được Publish.',
  LOCATION_NOT_ALLOWED: 'Vị trí này không được phép khởi tạo quy trình.',
  PROCESS_AMBIGUOUS: 'Có nhiều quy trình cùng phù hợp; hãy chỉ rõ mã quy trình.',
  ORDER_NOT_CANCELLABLE: 'Có chặng đang chạy hoặc rack đang trên AMR: hãy hủy ở hệ thống AGV rồi đối soát.',
  TRANSACTION_NOT_FOUND: 'Không tìm thấy Station Transaction.',
  TRANSACTION_NEEDS_RECONCILIATION: 'Hãy đối soát giao dịch trước.',
  HANDOVER_NOT_COMPLETED: 'Chưa xác nhận giao nhận xong nên chưa thể ghi nhận AMR đã rời cổng.',
  CONDITIONS_INVALID: 'Điều kiện không hợp lệ: chỉ chấp nhận các loại điều kiện được hỗ trợ.',
  PROCESS_CODE_REQUIRED: 'Cần nhập mã quy trình.',
  PROCESS_NAME_REQUIRED: 'Cần nhập tên quy trình.',
  STEP_KEY_REQUIRED: 'Cần nhập mã bước.',
  CANDIDATE_LOCATION_REQUIRED: 'Cần nhập mã vị trí cho ứng viên.',
  SNAPSHOT_INVALID: 'Không đọc được bản sao cấu hình của Transport Task.',
  PORT_REQUIRED: 'Cần chỉ rõ cổng.',
  REQUEST_ID_TOO_LONG: 'Request_ID dài quá 254 ký tự.',
  TRANSACTION_FINAL: 'Station Transaction đã kết thúc.',
  USE_DEDICATED_METHOD: 'Hãy dùng thao tác riêng cho trạng thái này.',
  INVALID_TRANSACTION_TRANSITION: 'Không được chuyển Station Transaction theo hướng này.',
  EXIT_REQUIRED: 'Station Transaction chỉ hoàn thành sau khi AMR đã rời cổng.',
  HANDOVER_REQUIRED: 'AMR chỉ rời cổng sau khi đã giao nhận xong.',
  HANDSHAKE_STEP_INVALID: 'Bước handshake phải từ 1 đến 9.',
  NOT_IN_RECONCILIATION: 'Giao dịch không ở trạng thái cần đối soát.',
  INVALID_RESUME_STATE: 'Trạng thái tiếp tục phải là một trạng thái đang xử lý.',
  INVALID_ORDER_TRANSITION: 'Không được chuyển trạng thái Transport Task theo hướng này.',
  LEG_ALREADY_PICKED: 'Chặng đã lấy rack không thể quay về trạng thái sẵn sàng.',
  INVALID_LEG_TRANSITION: 'Không được chuyển trạng thái chặng theo hướng này.',
  AGV_TASK_CODE_MISMATCH: 'Chặng đã gắn với một mã nhiệm vụ AGV khác.',
}, {
  PLC_NOT_FOUND: 'PLC not found.',
  BULK_APPROVAL_NOT_ALLOWED: 'Bulk read approval is not enabled for this PLC.',
  BULK_APPROVAL_NOT_CONFIRMED: 'A confirmation and the number of eligible points seen in the preview are required.',
  BULK_APPROVAL_PREVIEW_STALE: 'The number of eligible signals changed since the preview. Reopen the dialog and confirm again.',
  DIAGNOSTIC_RANGE_INVALID: 'The time range is not valid (the end must be after the start; only the last 1 hour or 24 hours can be chosen).',
  DIAGNOSTIC_RANGE_TOO_LARGE: 'The time range exceeds the allowed limit.',
  DIAGNOSTIC_EXPORT_FAILED: 'The diagnostic data could not be read, so no package was exported. See the backend log.',
  IO_POINT_NOT_FOUND: 'IO point not found.',
  IO_NO_ADDRESS: 'Enter an address before confirming.',
  IO_NOT_APPROVED: 'Only confirmed signals may be written.',
  IO_NOT_WRITABLE: 'Only Software → PLC signals can be written.',
  WRITE_BLOCKED_SAFETY: 'Safety/Interlock and Transfer/Departure Permit signals cannot be test-written.',
  WRITE_DIRECTION: 'Only Software → PLC signals can be written.',
  READ_ONLY: 'The system is in read-only mode: writing is not allowed.',
  WRITE_NOT_ALLOWED: 'The signal is not on the list of signals allowed for writing.',
  WRITE_SIGNAL_NOT_CONFIRMED: 'The signal of the IO map is not confirmed by the PLC engineer: writing cannot be approved and nothing is written to a real PLC.',
  IO_POINT_RETIRED: 'The signal belongs to an earlier IO map and is no longer used: only its address can be cleared.',
  IO_NO_DEFAULT_ADDRESS: 'The IO map has no address for this signal.',
  WRITE_NOT_DECLARED: 'The point is not declared in the deployment configuration (Plc:Write:AllowList): nothing is written to a real PLC.',
  WRITE_ADDRESS_RESERVED: 'The address reaches a register reserved for the unconfirmed PLC interface: writing is blocked.',
  WRITE_RESERVED_CONFIG_INVALID: 'The list of reserved registers cannot be read: every write to a real PLC is blocked.',
  WRITE_AUDIT_UNAVAILABLE: 'The audit record could not be stored, so the write was not sent.',
  WRITE_POINT_UNKNOWN: 'The point does not match the configuration of the PLC connection.',
  OUTCOME_UNKNOWN: 'The outcome of the write is unknown: read the value back before doing anything else.',
  NO_ADDRESS: 'The signal has no confirmed address.',
  NOT_CONNECTED: 'The PLC is not connected.',
  NOT_SIMULATOR_MODE: 'This function is not available.',
  NOT_AVAILABLE_IN_PRODUCTION: 'This function is not available in the production environment.',
  VALUE_INVALID: 'The value is not valid for the signal data type.',
  VALUE_OUT_OF_RANGE: 'The value is outside the data type range (INT: −32768…32767).',
  STRING_NOT_ASCII: 'STRING accepts ASCII characters only.',
  STRING_TOO_LONG: 'The STRING is longer than the configured number of words.',
  STRING_LENGTH_UNKNOWN: 'The STRING has no configured length (words).',
  WRITE_TYPE_UNSUPPORTED: 'Writing is not supported for this data type or encoding yet.',
  WRITE_DECISION_INVALID: 'The decision must be Approve or Reject.',
  TYPE_MISMATCH: 'The value does not match the signal data type.',
  WRITE_FAILED: 'Writing to the PLC failed.',
  READ_FAILED: 'Reading the PLC failed.',
  DIAGNOSTIC_MODULE_INVALID: 'The module name is not valid.',
  DIAGNOSTIC_LEVEL_INVALID: 'The level must be Information, Warning, Error or Critical.',
  DIAGNOSTIC_EXPORT_BUSY: 'Too many diagnostic exports are running or waiting. Try again when one has finished.',
  DIAGNOSTIC_JOB_NOT_FOUND: 'Diagnostic export not found (it may have expired after 24 hours).',
  DIAGNOSTIC_JOB_NOT_READY: 'The export is not finished, failed, or its file has expired.',
  DIAGNOSTIC_EXPORT_TIMEOUT: 'The export exceeded the allowed time.',
  NOT_CONFIGURED: 'The IO has no valid address, so it cannot be read.',
  WRITE_AMBIGUOUS: 'The write command was sent but no confirmation was received: do NOT retry automatically. Read the value back from the PLC to verify.',
  RADIX_UNCONFIRMED: 'The X/Y/W address radix is awaiting confirmation by the PLC engineer.',
  X_READ_ONLY: 'Device X is a hardware input and cannot be written.',
  NEEDS_CONFIGURATION: 'PLC parameters are missing; they must be supplied and confirmed by the PLC engineer.',
  ORDER_NOT_FOUND: 'Transport Task not found.',
  STATE_INVALID: 'The state is not valid.',
  STATE_NOT_ALLOWED: 'This state cannot be set directly; use the dedicated operation.',
  PLC_ID_INVALID: 'The PLC code may contain letters, digits, - and _ only (max 20).',
  PLC_NAME_REQUIRED: 'The PLC name is required.',
  PLC_IP_REQUIRED: 'The IP address is required.',
  PLC_EXISTS: 'The PLC code already exists.',
  STATION_ID_INVALID: 'The port code may contain letters, digits, - and _ only (max 20).',
  STATION_DUPLICATE: 'A port code is used twice.',
  STATION_EXISTS: 'The port code already exists.',
  LOCATION_ALREADY_LINKED: 'This location is already linked to another port.',
  PORT_BUSY: 'The port already has an active Station Transaction.',
  PROCESS_NOT_FOUND: 'Process not found.',
  VERSION_NOT_FOUND: 'Version not found.',
  FLOW_NOT_AVAILABLE: 'The process is not published or not ready in the process settings: no task can be created.',
  LOCATION_CODE_CONFIRMATION: 'Type the location code to confirm the removal.',
  LOCATION_DELETE_PROTECTED: 'The location cannot be deleted: A protected station (the legacy process is kept).',
  LOCATION_DELETE_RACKS: 'The location cannot be deleted: Holds rack(s).',
  LOCATION_DELETE_PORT: 'The location cannot be deleted: Linked to PLC port (unlink first).',
  LOCATION_DELETE_PROCESS: 'The location cannot be deleted: Used by process.',
  LOCATION_DELETE_OPEN_WORK: 'The location cannot be deleted: work at this location is not finished.',
  PROCESS_PROTECTED: 'A protected process (BP-04, under review) is not removed; it can only be switched off.',
  PROCESS_HOLDS_PORT: 'An order of this process still holds a station port: confirm that the AMR has left (or resolve the port) first.',
  PROCESS_CODE_CONFIRMATION: 'Type the process code to confirm the removal.',
  PROCESS_HAS_OPEN_ORDERS: 'The process has unfinished transport orders: finish or cancel them first.',
  PROCESS_DISABLE_FIRST: 'The process is published and switched on: switch it off and save before removing it.',
  VERSION_NOT_DELETABLE: 'Only a draft or validated version can be deleted; a published or disabled version is kept.',
  VERSION_IN_USE: 'A transport order uses this version; it is kept.',
  VERSION_ONLY_ONE: 'This is the only version: remove the process instead.',
  REASON_TOO_LONG: 'The reason has at most 500 characters.',
  PROCESS_CODE_INVALID: 'The process code is not valid (required, max 60 characters).',
  PROCESS_CODE_EXISTS: 'The process code already exists.',
  VERSION_NOT_EDITABLE: 'A published or disabled version cannot be edited; create a new version.',
  VERSION_NOT_VALIDATED: 'Validate the version before publishing it.',
  PUBLISH_REFUSED: 'Cannot publish: the configuration is no longer valid.',
  DEFINITION_INVALID: 'The configuration contains an unsupported value.',
  PROCESS_DISABLED: 'The process is disabled.',
  PROCESS_REVIEW_BLOCKED: 'The process is REVIEW and not approved for implementation.',
  PROCESS_NOT_PUBLISHED: 'The process has no published version.',
  LOCATION_NOT_ALLOWED: 'This location cannot start the process.',
  PROCESS_AMBIGUOUS: 'More than one process fits; name the process code.',
  ORDER_NOT_CANCELLABLE: 'A leg is in motion or the rack is on the AMR: cancel in the AGV system, then reconcile.',
  TRANSACTION_NOT_FOUND: 'Station Transaction not found.',
  TRANSACTION_NEEDS_RECONCILIATION: 'Reconcile the transaction first.',
  HANDOVER_NOT_COMPLETED: 'The handover is not confirmed yet, so the AMR cannot be recorded as having left the port.',
  CONDITIONS_INVALID: 'The conditions are not valid: only the supported condition types are accepted.',
  PROCESS_CODE_REQUIRED: 'The process code is required.',
  PROCESS_NAME_REQUIRED: 'The process name is required.',
  STEP_KEY_REQUIRED: 'The step key is required.',
  CANDIDATE_LOCATION_REQUIRED: 'A candidate needs a location code.',
  SNAPSHOT_INVALID: 'The configuration snapshot of the Transport Task cannot be read.',
  PORT_REQUIRED: 'A port is required.',
  REQUEST_ID_TOO_LONG: 'Request_ID is longer than 254 characters.',
  TRANSACTION_FINAL: 'The Station Transaction is already finished.',
  USE_DEDICATED_METHOD: 'Use the dedicated operation for this state.',
  INVALID_TRANSACTION_TRANSITION: 'This Station Transaction transition is not allowed.',
  EXIT_REQUIRED: 'A Station Transaction completes only after the AMR has left the port.',
  HANDOVER_REQUIRED: 'The AMR can only leave after the handover is completed.',
  HANDSHAKE_STEP_INVALID: 'The handshake step must be 1 to 9.',
  NOT_IN_RECONCILIATION: 'The transaction does not require reconciliation.',
  INVALID_RESUME_STATE: 'The resume state must be an in-progress state.',
  INVALID_ORDER_TRANSITION: 'This Transport Task transition is not allowed.',
  LEG_ALREADY_PICKED: 'A leg whose rack was picked cannot go back to Ready.',
  INVALID_LEG_TRANSITION: 'This leg transition is not allowed.',
  AGV_TASK_CODE_MISMATCH: 'The leg is already mapped to a different AGV task code.',
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
