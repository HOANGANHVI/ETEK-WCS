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
  PICK_CONFIRMED: 'Xác nhận đã lấy rack',
  RECONCILIATION_REQUIRED: 'Cần đối soát vị trí rack',
  RECONCILED: 'Đã đối soát',
  WAITING_FOR_SOURCE: 'Chờ rack nguồn',
  LATE_EVENT_IGNORED: 'Bỏ qua sự kiện đến muộn',
  OUT_OF_ORDER_EVENT_IGNORED: 'Bỏ qua sự kiện sai thứ tự',
  RCS_DISPATCH_UNKNOWN: 'Chưa rõ kết quả gửi nhiệm vụ tới RCS',
  RCS_QUERY_UNKNOWN: 'Chưa rõ kết quả truy vấn RCS',
  RCS_CANCEL_REQUESTED: 'Đã gửi yêu cầu hủy tới RCS',
  RCS_CANCEL_UNKNOWN: 'Chưa rõ kết quả hủy tại RCS',
  RCS_CONTINUE_UNKNOWN: 'Chưa rõ kết quả tiếp tục nhiệm vụ tại RCS',
  CANCEL_RETRY: 'Gửi lại yêu cầu hủy',
}
/** Events that come from or concern the PLC / RCS interfaces (shown in a dedicated list of the task detail). */
export const DEVICE_EVENT_RE = /^(RCS_|PICK_CONFIRMED|LATE_EVENT|OUT_OF_ORDER)/

const REASON_RULES: [RegExp, (m: RegExpMatchArray) => string][] = [
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
  CONCURRENT_MODIFICATION: 'Dữ liệu vừa được thay đổi bởi thao tác khác, vui lòng thử lại.',
  INTERNAL_ERROR: 'Máy chủ gặp lỗi nội bộ. Kiểm tra nhật ký Backend.',
}

export function describeError(e: unknown): string {
  if (e instanceof ApiRequestError) {
    if (e.status === 0 || e.code === 'NETWORK_ERROR' || [502, 503, 504].includes(e.status))
      return 'Không kết nối được tới máy chủ WCS (API). Hãy kiểm tra Backend đang chạy rồi thử lại.'
    return ERROR_VI[e.code] ?? `Yêu cầu không thành công (mã ${e.code}).`
  }
  return 'Đã xảy ra lỗi không xác định.'
}

export const CONNECTION_VI: Record<string, string> = {
  connecting: 'Đang kết nối', connected: 'Đã kết nối', reconnecting: 'Đang kết nối lại', disconnected: 'Mất kết nối', demo: 'Cục bộ (mô phỏng)',
}

/** Display code of a task, derived from its id (not a stored business field). */
export const taskCode = (id: string) => `NV-${id.replaceAll('-', '').slice(0, 8).toUpperCase()}`
