import { PLC_ERROR_VI } from './plc'
/**
 * Vietnamese UI vocabulary. Technical codes (AGV1, BP-01, PLC, RCS, API, Rack ID, backend state names) are never renamed in the
 * data layer: only the text shown to the user is translated here.
 */
import { ApiRequestError } from '../api/errors'
import type { TaskState } from '../api/types'

export type Tone = 'ok' | 'info' | 'warn' | 'bad' | 'muted'

export const TASK_STATE_VI: Record<TaskState, string> = {
  Pending: 'Chờ xử lý',
  Validating: 'Đang kiểm tra',
  Queued: 'Đang chờ',
  Dispatching: 'Đang gửi RCS',
  Accepted: 'Đã tiếp nhận',
  Executing: 'Đang thực hiện',
  WaitingForPlc: 'Chờ PLC cho phép',
  WaitingForDestination: 'Chờ vị trí đích',
  Continuing: 'Đang tiếp tục',
  Completed: 'Hoàn thành',
  Failed: 'Thất bại',
  CancelPending: 'Đang hủy',
  Cancelled: 'Đã hủy',
}

export const TASK_STATE_TONE: Record<TaskState, Tone> = {
  Pending: 'muted', Validating: 'muted', Queued: 'warn', Dispatching: 'info', Accepted: 'info', Executing: 'ok',
  WaitingForPlc: 'warn', WaitingForDestination: 'warn', Continuing: 'ok', Completed: 'ok', Failed: 'bad',
  CancelPending: 'warn', Cancelled: 'muted',
}

/** Groups used by the task chart. Every backend state belongs to exactly one group. */
export const TASK_GROUPS: { key: string; label: string; tone: Tone; states: TaskState[] }[] = [
  { key: 'waiting', label: 'Đang chờ', tone: 'warn', states: ['Pending', 'Validating', 'Queued', 'WaitingForPlc', 'WaitingForDestination'] },
  { key: 'accepted', label: 'Đã tiếp nhận', tone: 'info', states: ['Dispatching', 'Accepted'] },
  { key: 'moving', label: 'Đang vận chuyển', tone: 'ok', states: ['Executing', 'Continuing'] },
  { key: 'done', label: 'Hoàn thành', tone: 'ok', states: ['Completed'] },
  { key: 'failed', label: 'Lỗi', tone: 'bad', states: ['Failed'] },
  { key: 'cancelled', label: 'Đã hủy / đang hủy', tone: 'muted', states: ['CancelPending', 'Cancelled'] },
]

export const OCCUPYING_STATES: TaskState[] = ['Dispatching', 'Accepted', 'Executing', 'WaitingForPlc', 'WaitingForDestination', 'Continuing', 'CancelPending']

export const AGV_CONNECTION_VI: Record<string, string> = { Unknown: 'Chưa rõ', Online: 'Trực tuyến', Offline: 'Mất kết nối', Error: 'Lỗi' }
export const AGV_CONNECTION_TONE: Record<string, Tone> = { Unknown: 'muted', Online: 'ok', Offline: 'bad', Error: 'bad' }
export const AGV_ROLE_VI: Record<string, string> = { FullRack: 'Vận chuyển rack đầy', EmptyRack: 'Vận chuyển rack rỗng' }

export const OCCUPANCY_VI: Record<string, string> = { Empty: 'Trống', Occupied: 'Đang có rack', Reserved: 'Đang được giữ chỗ', Unknown: 'Chưa xác định' }
export const OCCUPANCY_TONE: Record<string, Tone> = { Empty: 'muted', Occupied: 'info', Reserved: 'warn', Unknown: 'bad' }

export const RACK_STATUS_VI: Record<string, string> = { Full: 'Rack đầy', Empty: 'Rack rỗng', InTransit: 'Đang vận chuyển', Unknown: 'Chưa xác định' }
export const RACK_STATUS_TONE: Record<string, Tone> = { Full: 'info', Empty: 'muted', InTransit: 'warn', Unknown: 'bad' }
export const RACK_KIND_VI: Record<string, string> = { Full: 'rack đầy', Empty: 'rack rỗng' }

export const LOCATION_TYPE_VI: Record<string, string> = { Station: 'Trạm máy', Buffer: 'Vị trí đệm', ManualBuffer: 'Vị trí đệm thủ công', Waypoint: 'Điểm trung gian' }
export const ALLOWED_KIND_VI: Record<string, string> = { Any: 'Mọi loại rack', Full: 'Chỉ rack đầy', Empty: 'Chỉ rack rỗng' }
export const LEG_VI: Record<string, string> = { Pick: 'Lấy rack', Drop: 'Thả rack' }

export const FLOW_VI: Record<string, { code: string; label: string }> = {
  Bp01: { code: 'BP-01', label: 'Lấy rack đầy tại máy A' },
  Bp02: { code: 'BP-02', label: 'Cấp rack rỗng cho máy A' },
  Bp03: { code: 'BP-03', label: 'Lấy rack rỗng tại máy B' },
  Bp05: { code: 'BP-05', label: 'F5 gọi rack đầy' },
  Bp06: { code: 'BP-06', label: 'F5 trả rack rỗng' },
}
/** Accepts backend (Bp01) or demo (BP01) spellings. */
export function flowInfo(flow: string) {
  const key = flow.charAt(0).toUpperCase() + flow.slice(1).toLowerCase()
  return FLOW_VI[key] ?? { code: flow, label: '' }
}

export const INVENTORY_EVENT_VI: Record<string, string> = {
  PickConfirmed: 'Xác nhận lấy rack',
  DropConfirmed: 'Xác nhận thả rack',
  ManualAdjustment: 'Điều chỉnh thủ công',
  InitialStockDeclared: 'Khai báo tồn ban đầu',
  Reconciliation: 'Đối soát',
}
export const BIND_VI: Record<string, string> = { NotRequested: '—', Bound: 'Đã bind rack', NotBoundByProcess: 'Không bind rack (theo quy trình)' }
export const SOURCE_VI: Record<string, string> = {
  WCS: 'WCS', RCS: 'RCS', Rcs: 'RCS', PLC: 'PLC', Plc: 'PLC', Operator: 'Người vận hành', WcsReconciliation: 'Đối soát WCS', Simulator: 'Mô phỏng',
}

export const HISTORY_EVENT_VI: Record<string, string> = {
  STATE_CHANGED: 'Chuyển trạng thái',
  TASK_CREATED: 'Tạo nhiệm vụ',
  SOURCE_RESERVED: 'Giữ chỗ rack nguồn',
  DESTINATION_RESERVED: 'Giữ chỗ vị trí đích',
  AGV_DISPATCH_HOLD: 'AGV bị giữ điều phối',
  AGV_DISPATCH_HOLD_RELEASED: 'Admin gỡ giữ AGV',
  PICK_CONFIRMED: 'Xác nhận đã lấy rack',
  RECONCILIATION_REQUIRED: 'Cần đối soát vị trí rack',
  RECONCILED: 'Đã đối soát',
  TASK_EDITED: 'Người dùng sửa nhiệm vụ',
  TASK_DELETED: 'Xóa nhiệm vụ',
  WAITING_FOR_SOURCE: 'Chờ rack nguồn',
  LATE_EVENT_IGNORED: 'Bỏ qua sự kiện đến muộn',
  OUT_OF_ORDER_EVENT_IGNORED: 'Bỏ qua sự kiện sai thứ tự',
  RCS_DISPATCH_UNKNOWN: 'Chưa rõ kết quả gửi nhiệm vụ tới RCS',
  RCS_QUERY_UNKNOWN: 'Chưa rõ kết quả truy vấn RCS',
  RCS_CANCEL_REQUESTED: 'Đã gửi yêu cầu hủy tới RCS',
  RCS_CANCEL_UNKNOWN: 'Chưa rõ kết quả hủy tại RCS',
  RCS_CONTINUE_UNKNOWN: 'Chưa rõ kết quả tiếp tục nhiệm vụ tại RCS',
  CANCEL_RETRY: 'Gửi lại yêu cầu hủy',
  RCS_CANCEL_RETRY_EXHAUSTED: 'Hết số lần thử hủy tự động – chuyển Thất bại, cần đối soát',
  RCS_CANCEL_NOTFOUND_NOT_CONFIRMED: 'RCS báo không có bản ghi (không coi là đã hủy)',
  RCS_CANCEL_IN_PROGRESS: 'RCS đang hủy nhiệm vụ',
  RECONCILIATION_CHECK_UNRESOLVED: 'Admin kiểm tra: chưa xác định được vị trí thực tế',
}
/** Events that come from or concern the PLC / RCS interfaces (shown in a dedicated list of the task detail). */
export const DEVICE_EVENT_RE = /^(RCS_|PICK_CONFIRMED|LATE_EVENT|OUT_OF_ORDER)/

const REASON_RULES: [RegExp, (m: RegExpMatchArray) => string][] = [
  [/^CANCEL_RETRY_LIMIT_REACHED$/, () => 'Hết giới hạn thử hủy tự động: chưa có xác nhận hủy từ RCS, cần đối soát'],
  [/^CANCEL_RACE_RCS_COMPLETED$/, () => 'RCS báo hoàn thành trước khi hủy kịp: cần đối soát vị trí rack'],
  [/^CANCEL_RACE_RCS_INTERRUPTED$/, () => 'RCS báo gián đoạn trước khi hủy kịp: cần đối soát vị trí rack'],
  [/^RCS_ENDED_WITHOUT_DROP$/, () => 'RCS báo kết thúc nhưng WCS chưa nhận xác nhận thả rack'],
  [/^RCS_NOTFOUND_AFTER_ACCEPT$/, () => 'RCS không còn bản ghi của nhiệm vụ đã tiếp nhận: cần đối soát'],
  [/^Validating business conditions$/, () => 'Đang kiểm tra điều kiện nghiệp vụ'],
  [/^AGV free; claimed from queue$/, () => 'AGV rảnh, nhận nhiệm vụ từ hàng đợi'],
  [/^AGV free; claimed$/, () => 'AGV rảnh, đã nhận nhiệm vụ'],
  [/^AGV busy; request queued$/, () => 'AGV đang bận, yêu cầu được xếp hàng chờ'],
  [/^RCS accepted the task( \(simulated\))?$/, (m) => 'RCS đã tiếp nhận nhiệm vụ' + (m[1] ? ' (mô phỏng)' : '')],
  [/^AGV executing$/, () => 'AGV bắt đầu thực hiện'],
  [/^Waiting for PLC permission \((Pick|Drop)\)$/, (m) => `Chờ PLC cho phép ${m[1] === 'Pick' ? 'lấy' : 'thả'} rack`],
  [/^PLC permission granted$/, () => 'PLC đã cho phép'],
  [/^Destination assigned: (.+)$/, (m) => `Đã chỉ định vị trí đích ${m[1]}`],
  [/^No destination available$/, () => 'Chưa có vị trí đích phù hợp'],
  [/^RCS continued the task$/, () => 'RCS đã tiếp tục nhiệm vụ'],
  [/^Drop confirmed; inventory updated$/, () => 'Đã xác nhận thả rack, tồn kho được cập nhật'],
  [/^Cancel requested by (.+?): (.*)$/, (m) => `${m[1]} yêu cầu hủy: ${m[2]}`],
  [/^Cancel requested: (.*?)( \(waiting for RCS confirmation\))?$/, (m) => `Yêu cầu hủy: ${m[1]}${m[2] ? ' (chờ RCS xác nhận)' : ''}`],
  [/^Cancel confirmed by RCS( status query)?$/, () => 'RCS đã xác nhận hủy'],
  [/^Cancelled before reaching RCS$/, () => 'Đã hủy trước khi gửi tới RCS'],
  [/^RCS reported the task interrupted$/, () => 'RCS báo nhiệm vụ bị gián đoạn'],
  [/^RCS reported cancellation$/, () => 'RCS báo nhiệm vụ đã bị hủy'],
  [/^NO_SOURCE_AVAILABLE$/, () => 'Không có rack nguồn phù hợp'],
  [/^AGV_UNAVAILABLE$/, () => 'AGV không khả dụng'],
  [/^RCS_REJECTED: ?(.*)$/, (m) => `RCS từ chối nhiệm vụ${m[1] ? `: ${m[1]}` : ''}`],
  [/^RCS_CONTINUE_REJECTED: ?(.*)$/, (m) => `RCS từ chối tiếp tục nhiệm vụ${m[1] ? `: ${m[1]}` : ''}`],
  [/^RCS_DISPATCH_UNCERTAIN$/, () => 'Không rõ RCS đã nhận nhiệm vụ hay chưa'],
  [/^TIMEOUT_(\w+)$/, (m) => `Quá thời gian ở trạng thái ${TASK_STATE_VI[m[1] as TaskState] ?? m[1]}`],
  [/^flow=(\w+)$/, (m) => `Quy trình ${flowInfo(m[1]).code}`],
  [/^location=(.+)$/, (m) => `Vị trí ${m[1]}`],
  [/^actual=(.+?)(; .*)?$/, (m) => `Rack thực tế ở ${m[1]}`],
  [/^Pick confirmed while cancelling.*$/, () => 'Đã lấy rack trong lúc hủy; rack đang vận chuyển, cần đối soát'],
  [/^Initial stock declared( \(demo\))?$/, () => 'Khai báo tồn ban đầu'],
  [/^Manual adjustment \(([+-])\)$/, (m) => `Điều chỉnh thủ công (${m[1]})`],
  [/^Operator cleared the position( \(demo\))?$/, () => 'Người vận hành dọn vị trí'],
  [/^Confirmed by operator( \(demo\))?$/, () => 'Người vận hành xác nhận vị trí thực tế'],
  [/^NO_BIND_POINT: external rack id ignored$/, () => 'Điểm không bind rack: bỏ qua Rack ID đọc được'],
]

/** Translates the free-text reasons produced by the backend / simulator. Unknown text (technical codes) is kept as is. */
export function reasonVi(text: string | null | undefined): string {
  if (!text) return ''
  for (const [re, fn] of REASON_RULES) {
    const m = text.match(re)
    if (m) return fn(m)
  }
  return text
}

const ERROR_VI: Record<string, string> = {
  VALIDATION_FAILED: 'Dữ liệu gửi lên không hợp lệ.',
  HOLD_RELEASE_CONFIRMATION_REQUIRED: 'Cần xác nhận cả hai: đã đối soát rack/vị trí VÀ đã kiểm tra thực tế AGV.',
  AGV_NOT_ON_HOLD: 'AGV này không ở trạng thái giữ điều phối.',
  HOLD_TASK_NOT_FINISHED: 'Nhiệm vụ gây ra việc giữ AGV vẫn đang hoạt động; hãy kết thúc hoặc hủy nhiệm vụ trước.',
  RCS_NOT_CONNECTED: 'RCS chưa được kết nối (chưa có RCS adapter thật): hệ thống không nhận nhiệm vụ mới.',
  TASK_NOT_FINISHED: 'Chỉ đối soát được nhiệm vụ đã kết thúc.',
  TASK_NOT_FOUND: 'Không tìm thấy nhiệm vụ.',
  AGV_NOT_FOUND: 'Không tìm thấy AGV.',
  LOCATION_NOT_FOUND: 'Không tìm thấy vị trí.',
  STATION_NOT_FOUND: 'Không tìm thấy trạm yêu cầu.',
  STATION_NOT_ALLOWED: 'Trạm này không được phép yêu cầu quy trình đã chọn.',
  FLOW_IN_REVIEW: 'Quy trình đang ở trạng thái xem xét, chưa được phê duyệt triển khai.',
  FLOW_NOT_SUPPORTED: 'Quy trình này không được hỗ trợ.',
  TASK_ALREADY_FINISHED: 'Nhiệm vụ đã kết thúc, không thể thao tác.',
  INVALID_TASK_TRANSITION: 'Không thể chuyển nhiệm vụ sang trạng thái đó.',
  CAPACITY_EXCEEDED: 'Vị trí không còn chỗ trống.',
  NO_RECONCILIATION_NEEDED: 'Nhiệm vụ không cần đối soát.',
  UNAUTHENTICATED: 'Bạn chưa đăng nhập hoặc phiên đã hết hạn.',
  INVALID_CREDENTIALS: 'Tên đăng nhập hoặc mật khẩu không đúng.',
  ACCOUNT_LOCKED: 'Tài khoản tạm thời bị khóa do đăng nhập sai nhiều lần. Vui lòng thử lại sau ít phút.',
  ACCOUNT_DISABLED: 'Tài khoản đã bị khóa. Vui lòng liên hệ quản trị viên.',
  FORBIDDEN: 'Bạn không có quyền thực hiện thao tác này.',
  PASSWORD_CHANGE_REQUIRED: 'Bạn cần đổi mật khẩu trước khi tiếp tục.',
  CURRENT_PASSWORD_INCORRECT: 'Mật khẩu hiện tại không đúng.',
  PASSWORD_UNCHANGED: 'Mật khẩu mới phải khác mật khẩu hiện tại.',
  CLIENT_HEADER_MISSING: 'Yêu cầu không hợp lệ (thiếu thông tin định danh ứng dụng).',
  USER_NOT_FOUND: 'Không tìm thấy tài khoản.',
  CANNOT_CHANGE_OWN_ROLE: 'Không thể tự thay đổi vai trò của chính mình.',
  CANNOT_DELETE_SELF: 'Không thể tự xóa tài khoản của chính mình.',
  USER_HAS_HISTORY: 'Tài khoản đã có lịch sử thao tác nên không thể xóa để giữ khả năng truy vết. Hãy khóa (vô hiệu hóa) tài khoản.',
  CANNOT_DISABLE_SELF: 'Không thể tự vô hiệu hóa tài khoản của chính mình.',
  LAST_ADMIN: 'Không thể vô hiệu hóa, hạ quyền hoặc xóa Admin đang hoạt động cuối cùng.',
  TASK_LOCKED: 'Nhiệm vụ đã được AGV/RCS tiếp nhận hoặc đã kết thúc; tài khoản Operator không có quyền sửa/xóa.',
  TASK_NOT_EDITABLE: 'Nhiệm vụ đang thực hiện hoặc đã kết thúc nên không thể sửa/xóa trực tiếp. Hãy dùng chức năng hủy hoặc đối soát.',
  TASK_STATE_CHANGED: 'Nhiệm vụ đã chuyển trạng thái trong lúc bạn thao tác. Dữ liệu đã được tải lại, vui lòng kiểm tra rồi thử lại.',
  NO_SOURCE_AT_STATION: 'Không có rack nguồn phù hợp cho yêu cầu mới. Nhiệm vụ không bị thay đổi.',
  REQUEST_KEY_REQUIRED: 'Thiếu mã định danh yêu cầu.',
  CONCURRENT_MODIFICATION: 'Dữ liệu vừa được thay đổi bởi thao tác khác, vui lòng thử lại.',
  INTERNAL_ERROR: 'Máy chủ gặp lỗi nội bộ. Kiểm tra nhật ký Backend.',
}

export function describeError(e: unknown): string {
  if (e instanceof ApiRequestError) {
    if (e.status === 0 || e.code === 'NETWORK_ERROR' || [502, 503, 504].includes(e.status))
      return 'Không kết nối được tới máy chủ WCS (API). Hãy kiểm tra Backend đang chạy rồi thử lại.'
    return ERROR_VI[e.code] ?? PLC_ERROR_VI[e.code] ?? `Yêu cầu không thành công (mã ${e.code}).`
  }
  return 'Đã xảy ra lỗi không xác định.'
}

export const CONNECTION_VI: Record<string, string> = {
  connecting: 'Đang kết nối', connected: 'Đã kết nối', reconnecting: 'Đang kết nối lại', disconnected: 'Mất kết nối', demo: 'Cục bộ (mô phỏng)',
}

/** Display code of a task, derived from its id (not a stored business field). */
export const taskCode = (id: string) => `NV-${id.replaceAll('-', '').slice(0, 8).toUpperCase()}`

export const ROLE_VI: Record<string, string> = { Admin: 'Quản trị viên (Admin)', Operator: 'Vận hành viên (Operator)' }
export const ROLE_SHORT_VI: Record<string, string> = { Admin: 'Admin', Operator: 'Operator' }

export const AUDIT_RESULT_VI: Record<string, string> = { Success: 'Thành công', Denied: 'Bị từ chối', Failed: 'Thất bại' }
export const AUDIT_RESULT_TONE: Record<string, Tone> = { Success: 'ok', Denied: 'warn', Failed: 'bad' }

export const AUDIT_ACTION_VI: Record<string, string> = {
  AUTH_LOGIN: 'Đăng nhập',
  AUTH_LOGOUT: 'Đăng xuất',
  AUTH_PASSWORD_CHANGE: 'Đổi mật khẩu',
  ACCESS_DENIED: 'Truy cập bị từ chối',
  TASK_CREATE: 'Tạo nhiệm vụ',
  TASK_UPDATE: 'Sửa nhiệm vụ',
  TASK_DELETE: 'Xóa nhiệm vụ',
  TASK_CANCEL: 'Hủy nhiệm vụ',
  TASK_RECONCILE: 'Đối soát nhiệm vụ',
  CONFIG_PLC_UPDATE: 'Sửa cấu hình PLC',
  CONFIG_PLC_TEST: 'Kiểm tra kết nối PLC',
  CONFIG_RCS_UPDATE: 'Sửa cấu hình RCS',
  CONFIG_RCS_TEST: 'Kiểm tra kết nối RCS',
  CONFIG_AGV_UPDATE: 'Sửa cấu hình AGV',
  CONFIG_LOCATION_CREATE: 'Thêm vị trí',
  CONFIG_LOCATION_UPDATE: 'Sửa vị trí',
  USER_CREATE: 'Tạo tài khoản',
  USER_UPDATE: 'Sửa tài khoản',
  USER_DELETE: 'Xóa tài khoản',
  USER_PASSWORD_RESET: 'Đặt lại mật khẩu',
  USER_BOOTSTRAP_ADMIN: 'Khởi tạo Admin đầu tiên',
  INVENTORY_INITIAL_STOCK: 'Khai báo tồn kho ban đầu',
  INVENTORY_ADJUST: 'Điều chỉnh tồn kho',
  SIMULATOR_ADVANCE: 'Giả lập: chạy bước tiếp theo',
  SIMULATOR_RUN: 'Giả lập: chạy toàn bộ',
  SIMULATOR_STATION_REQUEST: 'Giả lập: yêu cầu từ trạm',
}
