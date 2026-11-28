import { localizedMap } from './core'
import type { Tone } from './vi'

/** CR-010 enum labels, issue texts and API error texts. Keys are the stable codes of the API; only the labels are translated. */

export const ORDER_STATE_LABEL = localizedMap(
  { Created: 'Mới tạo', Accepted: 'Đã tiếp nhận', InProgress: 'Đang thực hiện', Waiting: 'Đang chờ', Completed: 'Hoàn thành', Failed: 'Thất bại', Cancelled: 'Đã hủy', ReconciliationRequired: 'Cần đối soát' },
  { Created: 'Created', Accepted: 'Accepted', InProgress: 'In progress', Waiting: 'Waiting', Completed: 'Completed', Failed: 'Failed', Cancelled: 'Cancelled', ReconciliationRequired: 'Reconciliation required' })
export const ORDER_STATE_TONE: Record<string, Tone> = { Created: 'muted', Accepted: 'info', InProgress: 'info', Waiting: 'warn', Completed: 'ok', Failed: 'bad', Cancelled: 'muted', ReconciliationRequired: 'bad' }

export const LEG_STATE_LABEL = localizedMap(
  { Pending: 'Chờ', Ready: 'Sẵn sàng gửi AGV', InProgress: 'Đang thực hiện', Waiting: 'Đang chờ', Completed: 'Hoàn thành', Failed: 'Thất bại', Cancelled: 'Đã hủy' },
  { Pending: 'Pending', Ready: 'Ready to dispatch', InProgress: 'In progress', Waiting: 'Waiting', Completed: 'Completed', Failed: 'Failed', Cancelled: 'Cancelled' })
export const LEG_STATE_TONE: Record<string, Tone> = { Pending: 'muted', Ready: 'info', InProgress: 'info', Waiting: 'warn', Completed: 'ok', Failed: 'bad', Cancelled: 'muted' }

export const TX_STATE_LABEL = localizedMap(
  {
    Created: 'Mới tạo', Requested: 'Đã nhận yêu cầu', Accepted: 'Đã ghi nhận', AgvAssigned: 'Đã gán AGV', AgvApproaching: 'AMR đang tới', AtStation: 'AMR tại cổng',
    Handling: 'Đang giao nhận', HandoverCompleted: 'Đã giao nhận xong', AgvExited: 'AMR đã rời cổng', Completed: 'Hoàn thành', Failed: 'Thất bại', ReconciliationRequired: 'Cần đối soát',
  },
  {
    Created: 'Created', Requested: 'Requested', Accepted: 'Recorded', AgvAssigned: 'AGV assigned', AgvApproaching: 'AMR approaching', AtStation: 'AMR at the port',
    Handling: 'Handling', HandoverCompleted: 'Handover completed', AgvExited: 'AMR left the port', Completed: 'Completed', Failed: 'Failed', ReconciliationRequired: 'Reconciliation required',
  })
export const TX_STATE_TONE: Record<string, Tone> = {
  Created: 'muted', Requested: 'info', Accepted: 'info', AgvAssigned: 'info', AgvApproaching: 'info', AtStation: 'warn', Handling: 'warn',
  HandoverCompleted: 'ok', AgvExited: 'ok', Completed: 'ok', Failed: 'bad', ReconciliationRequired: 'bad',
}
/** States an Admin may record by hand (AMR approaching / at the port / handling) and states usable when resuming after reconciliation. */
export const TX_RESUME_STATES = ['AgvAssigned', 'AgvApproaching', 'AtStation', 'Handling', 'HandoverCompleted'] as const

export const STEP_TYPE_LABEL = localizedMap(
  { Pickup: 'Lấy rack', Delivery: 'Giao rack', BufferStorage: 'Lưu vào buffer', BufferRetrieval: 'Lấy từ buffer', Waiting: 'Chờ điều kiện', Condition: 'Rẽ nhánh theo điều kiện', ProcessCompletion: 'Kết thúc quy trình' },
  { Pickup: 'Pickup', Delivery: 'Delivery', BufferStorage: 'Buffer storage', BufferRetrieval: 'Buffer retrieval', Waiting: 'Waiting', Condition: 'Condition branch', ProcessCompletion: 'Process completion' })
export const STEP_TYPES = ['Pickup', 'Delivery', 'BufferStorage', 'BufferRetrieval', 'Waiting', 'Condition', 'ProcessCompletion'] as const

export const SOURCE_MODE_LABEL = localizedMap(
  { Request: 'Vị trí phát yêu cầu', Candidates: 'Một trong các nguồn ứng viên', PreviousDestination: 'Đích của chặng trước' },
  { Request: 'The requesting location', Candidates: 'One of the source candidates', PreviousDestination: 'Destination of the previous leg' })
export const SOURCE_MODES = ['Request', 'Candidates', 'PreviousDestination'] as const

export const DEST_MODE_LABEL = localizedMap(
  { Request: 'Vị trí phát yêu cầu', Candidates: 'Một trong các đích ứng viên' },
  { Request: 'The requesting location', Candidates: 'One of the destination candidates' })
export const DEST_MODES = ['Request', 'Candidates'] as const

export const RESOLUTION_LABEL = localizedMap(
  { BeforePick: 'Khi tạo chặng (trước khi lấy rack)', AfterPick: 'Sau khi lấy rack (continue task)' },
  { BeforePick: 'When the leg is created (before the pick)', AfterPick: 'After the pick (continue task)' })
export const RESOLUTIONS = ['BeforePick', 'AfterPick'] as const

export const WAIT_BEHAVIOR_LABEL = localizedMap(
  { Wait: 'Chờ', Fail: 'Báo lỗi / thất bại', Alternative: 'Chuyển sang bước thay thế' },
  { Wait: 'Wait', Fail: 'Fail', Alternative: 'Go to the alternative step' })
export const WAIT_BEHAVIORS = ['Wait', 'Fail', 'Alternative'] as const

export const REQUEST_TYPE_LABEL = localizedMap(
  { PickupRequest: 'PLC yêu cầu lấy rack', DeliveryRequest: 'PLC yêu cầu cấp rack', ManualRequest: 'Người vận hành / PDA' },
  { PickupRequest: 'PLC asks to pick a rack', DeliveryRequest: 'PLC asks for a rack', ManualRequest: 'Operator / PDA' })
export const REQUEST_TYPES = ['PickupRequest', 'DeliveryRequest', 'ManualRequest'] as const

export const RACK_KIND_LABEL = localizedMap({ Full: 'Rack đầy', Empty: 'Rack rỗng' }, { Full: 'Full rack', Empty: 'Empty rack' })

export const CONDITION_LABEL = localizedMap(
  {
    LocationEnabled: 'Vị trí đang bật', LocationAvailable: 'Vị trí còn chỗ và nhận loại rack này', RackPresent: 'Có rack phù hợp chưa giữ chỗ',
    RackTypeMatch: 'Loại rack khớp', CapacityAvailable: 'Còn sức chứa', StationReady: 'Cổng sẵn sàng (không có giao dịch đang chạy)', PlcOnline: 'PLC đang kết nối',
    InventoryState: 'Trạng thái tồn kho của vị trí', RackBinding: 'Rack đã/chưa bind',
  },
  {
    LocationEnabled: 'Location is enabled', LocationAvailable: 'Location has room and accepts this rack type', RackPresent: 'A suitable unreserved rack is present',
    RackTypeMatch: 'Rack type matches', CapacityAvailable: 'Capacity is available', StationReady: 'Port is ready (no active transaction)', PlcOnline: 'PLC is connected',
    InventoryState: 'Inventory state of the location', RackBinding: 'Rack is / is not bound',
  })
export const CONDITION_TYPES = ['LocationEnabled', 'LocationAvailable', 'RackPresent', 'RackTypeMatch', 'CapacityAvailable', 'StationReady', 'PlcOnline', 'InventoryState', 'RackBinding'] as const
/** Conditions that need a parameter and the values they accept (free text where the list is empty). */
export const CONDITION_PARAMS: Record<string, readonly string[]> = {
  RackTypeMatch: ['Full', 'Empty'],
  InventoryState: ['Empty', 'Occupied', 'HasFreeSlot', 'NotUnknown'],
  RackBinding: ['Bound', 'Unbound'],
}

export const VERSION_STATE_LABEL = localizedMap(
  { Draft: 'Nháp', Validated: 'Đã kiểm tra', Published: 'Đang chạy (Published)', Disabled: 'Đã vô hiệu hóa' },
  { Draft: 'Draft', Validated: 'Validated', Published: 'Running (Published)', Disabled: 'Disabled' })
export const VERSION_STATE_TONE: Record<string, Tone> = { Draft: 'muted', Validated: 'info', Published: 'ok', Disabled: 'warn' }

/** Validation issue codes (ProcessValidator) -> text. The server text is the fallback for any unknown code. */
export const ISSUE_LABEL = localizedMap(
  {
    PROCESS_REVIEW_BLOCKED: 'Quy trình ở trạng thái REVIEW (chưa được duyệt triển khai) nên không thể Publish. Nếu danh mục chuẩn đã duyệt quy trình (ví dụ PH2-04): ở mục "Vận hành: sẵn sàng và hàng đợi", nhập lý do rồi bấm "Tạo bản nháp theo danh mục", sau đó Kiểm tra và Công bố bản nháp mới.',
    NO_TRIGGER_LOCATION: 'Quy trình chưa có vị trí nào được phép khởi tạo.',
    NO_STEPS: 'Phiên bản chưa có bước nào.',
    FIRST_STEP_MISSING: 'Bước đầu tiên không tồn tại.',
    DUPLICATE_STEP_KEY: 'Mã bước bị trùng.',
    LOCATION_PROTECTED: 'Vị trí thuộc quy trình giữ nguyên (UNCHANGED), không được dùng trong tuyến cấu hình.',
    LOCATION_NOT_FOUND: 'Vị trí không tồn tại.',
    LOCATION_DISABLED: 'Vị trí đang bị tắt.',
    PORT_DISABLED: 'Cổng của vị trí đang bị tắt.',
    PLC_DISABLED: 'PLC phục vụ vị trí này đang bị tắt.',
    PORT_MAPPING_NOT_APPROVED: 'IO mapping của cổng chưa được kỹ sư PLC phê duyệt: không được dùng cho thao tác vật lý.',
    STEP_LINK_INVALID: 'Bước kế tiếp / thay thế không tồn tại.',
    COMPLETION_HAS_NEXT: 'Bước kết thúc không được có bước kế tiếp.',
    CONDITION_NO_NEXT: 'Bước điều kiện thiếu nhánh "đúng".',
    CONDITION_NO_ALTERNATIVE: 'Bước điều kiện thiếu nhánh "sai".',
    WAITING_NO_NEXT: 'Bước chờ thiếu bước kế tiếp.',
    STEP_NO_NEXT: 'Bước di chuyển thiếu bước kế tiếp.',
    WAIT_ALTERNATIVE_MISSING: 'Chọn "bước thay thế" nhưng chưa khai báo bước thay thế.',
    CONDITION_LOCATION_REQUIRED: 'Điều kiện của bước điều kiện / bước chờ phải chỉ rõ vị trí.',
    CONDITIONS_REQUIRED: 'Bước điều kiện / bước chờ cần ít nhất một điều kiện.',
    CONDITION_UNSUPPORTED: 'Loại điều kiện không được hỗ trợ.',
    CONDITION_PARAMETER: 'Tham số của điều kiện không hợp lệ.',
    WAITING_POSITION_TYPE: 'Vị trí chờ trung gian phải có loại "vị trí chờ"; nó không phải nơi lưu rack.',
    CANDIDATES_IGNORED: 'Ứng viên của bước không di chuyển sẽ bị bỏ qua.',
    SOURCE_MODE_INVALID: 'Chế độ nguồn không hợp lệ với loại bước.',
    PREVIOUS_DESTINATION_ON_FIRST: 'Bước đầu tiên không có chặng trước.',
    NO_SOURCE_CANDIDATE: 'Cần ít nhất một nguồn ứng viên.',
    NO_DESTINATION_CANDIDATE: 'Cần ít nhất một đích ứng viên.',
    SOURCE_CANDIDATES_IGNORED: 'Nguồn ứng viên bị bỏ qua vì chế độ nguồn không phải "ứng viên".',
    DEST_CANDIDATES_IGNORED: 'Đích ứng viên bị bỏ qua vì chế độ đích không phải "ứng viên".',
    SAME_ENDS: 'Nguồn và đích không được cùng là vị trí phát yêu cầu.',
    ALTERNATIVE_AFTER_PICK: 'Không thể chuyển sang bước thay thế khi rack đã nằm trên AMR; hãy chọn Chờ hoặc Báo lỗi.',
    DUPLICATE_CANDIDATE_ORDER: 'Hai ứng viên trùng cả mức ưu tiên và thứ tự.',
    DESTINATION_NOT_STORAGE: 'Vị trí này không lưu được rack nên không thể là đích.',
    SOURCE_NOT_STORAGE: 'Vị trí này không chứa rack nên không thể là nguồn.',
    RACK_TYPE_MISMATCH: 'Loại rack của vị trí không phù hợp với quy trình.',
    BUFFER_NOT_ALLOWED: 'Bước lưu/lấy buffer chỉ được dùng vị trí buffer.',
    STEP_ORPHAN: 'Bước không thể đi tới từ bước đầu tiên.',
    ROUTE_CYCLE: 'Tuyến bị vòng lặp vô hạn.',
    NO_PATH_TO_COMPLETION: 'Nhánh này không dẫn tới bước kết thúc quy trình.',
    NO_COMPLETION_STEP: 'Quy trình chưa có bước kết thúc.',
    AMBIGUOUS_TRIGGER: 'Đã có quy trình khác được Publish cho cùng yêu cầu: yêu cầu không chỉ rõ mã quy trình sẽ bị từ chối.',
  },
  {
    PROCESS_REVIEW_BLOCKED: 'The process is REVIEW (not approved for implementation) and cannot be published. If the default catalog approves it (for example PH2-04): under "Operation: readiness and queue" give a reason, press "Create a draft from the catalog", then validate and publish the new draft.',
    NO_TRIGGER_LOCATION: 'The process has no location allowed to start it.',
    NO_STEPS: 'The version has no step.',
    FIRST_STEP_MISSING: 'The first step does not exist.',
    DUPLICATE_STEP_KEY: 'A step key is used twice.',
    LOCATION_PROTECTED: 'The location keeps its legacy process (UNCHANGED) and cannot be used by a configurable route.',
    LOCATION_NOT_FOUND: 'The location does not exist.',
    LOCATION_DISABLED: 'The location is disabled.',
    PORT_DISABLED: 'The port of the location is disabled.',
    PLC_DISABLED: 'The PLC serving this location is disabled.',
    PORT_MAPPING_NOT_APPROVED: 'The IO mapping of the port is not approved by the PLC engineer: it cannot be used for a physical operation.',
    STEP_LINK_INVALID: 'The next / alternative step does not exist.',
    COMPLETION_HAS_NEXT: 'A completion step cannot have a next step.',
    CONDITION_NO_NEXT: 'The condition step has no "true" branch.',
    CONDITION_NO_ALTERNATIVE: 'The condition step has no "false" branch.',
    WAITING_NO_NEXT: 'The waiting step has no next step.',
    STEP_NO_NEXT: 'The movement step has no next step.',
    WAIT_ALTERNATIVE_MISSING: '"Alternative step" is chosen but no alternative step is set.',
    CONDITION_LOCATION_REQUIRED: 'A condition of a condition/waiting step must name its location.',
    CONDITIONS_REQUIRED: 'A condition/waiting step needs at least one condition.',
    CONDITION_UNSUPPORTED: 'The condition type is not supported.',
    CONDITION_PARAMETER: 'The condition parameter is not valid.',
    WAITING_POSITION_TYPE: 'An intermediate waiting position must have the "waiting position" type; it is not a storage location.',
    CANDIDATES_IGNORED: 'Candidates of a non-movement step are ignored.',
    SOURCE_MODE_INVALID: 'The source mode is not valid for this step type.',
    PREVIOUS_DESTINATION_ON_FIRST: 'The first step has no previous leg.',
    NO_SOURCE_CANDIDATE: 'At least one source candidate is needed.',
    NO_DESTINATION_CANDIDATE: 'At least one destination candidate is needed.',
    SOURCE_CANDIDATES_IGNORED: 'Source candidates are ignored because the source mode is not "candidates".',
    DEST_CANDIDATES_IGNORED: 'Destination candidates are ignored because the destination mode is not "candidates".',
    SAME_ENDS: 'Source and destination cannot both be the requesting location.',
    ALTERNATIVE_AFTER_PICK: 'The alternative step cannot replace the destination once the rack is on the AMR; choose Wait or Fail.',
    DUPLICATE_CANDIDATE_ORDER: 'Two candidates share the same priority and order.',
    DESTINATION_NOT_STORAGE: 'This location cannot store a rack and cannot be a destination.',
    SOURCE_NOT_STORAGE: 'This location cannot hold a rack and cannot be a source.',
    RACK_TYPE_MISMATCH: 'The rack type of the location does not fit the process.',
    BUFFER_NOT_ALLOWED: 'A buffer storage/retrieval step may only use buffer locations.',
    STEP_ORPHAN: 'The step cannot be reached from the first step.',
    ROUTE_CYCLE: 'The route loops endlessly.',
    NO_PATH_TO_COMPLETION: 'This branch does not lead to a process completion.',
    NO_COMPLETION_STEP: 'The process has no completion step.',
    AMBIGUOUS_TRIGGER: 'Another process is already published for the same request: a request that does not name the process will be refused.',
  })
