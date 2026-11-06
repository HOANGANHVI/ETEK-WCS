/**
 * Demo engine: an in-memory, browser-only imitation of the WCS backend used when VITE_DEMO_MODE=true.
 * It mirrors the documented rules (PER_AGV, WCS-owned inventory, reservations, WAITING_FOR_DESTINATION, PLC handshake,
 * no-bind points, reconciliation) so people can try the UI. It is NOT the business implementation: the real rules live in
 * the backend. Nothing here touches the network, PostgreSQL or any device.
 */
import type {
  Agv, InventoryTransaction, Location, Rack, SystemHealth, TaskHistory, TaskState, TransportTask,
} from '../api/types'

type Kind = 'Full' | 'Empty'
type Mode = 'Request' | 'Search'

interface FlowRule {
  kind: Kind
  stations: string[]
  srcMode: Mode
  srcGroups: string[][]
  dstMode: Mode
  dstGroups: string[][]
  noBind: string[]
}

// Same flow configuration as the backend appsettings.json (order = order shown in process-flows.md, NOT a confirmed priority).
export const DEMO_FLOWS: Record<string, FlowRule> = {
  BP01: { kind: 'Full', stations: ['A1', 'A2'], srcMode: 'Request', srcGroups: [], dstMode: 'Search', dstGroups: [['B1', 'B2'], ['F1', 'F2'], ['F5']], noBind: ['B1', 'B2'] },
  BP02: { kind: 'Empty', stations: ['A1', 'A2'], srcMode: 'Search', srcGroups: [['B1', 'B2'], ['F3', 'F4']], dstMode: 'Request', dstGroups: [], noBind: [] },
  BP03: { kind: 'Empty', stations: ['B1', 'B2'], srcMode: 'Request', srcGroups: [], dstMode: 'Search', dstGroups: [['A1', 'A2'], ['F3', 'F4']], noBind: ['A1', 'A2'] },
  BP05: { kind: 'Full', stations: ['F5'], srcMode: 'Search', srcGroups: [['F1', 'F2']], dstMode: 'Request', dstGroups: [], noBind: [] },
  BP06: { kind: 'Empty', stations: ['F5'], srcMode: 'Request', srcGroups: [], dstMode: 'Search', dstGroups: [['A1', 'A2'], ['F3', 'F4']], noBind: ['A1', 'A2'] },
}

const OCCUPYING: TaskState[] = ['Dispatching', 'Accepted', 'Executing', 'WaitingForPlc', 'WaitingForDestination', 'Continuing', 'CancelPending']
const TERMINAL: TaskState[] = ['Completed', 'Failed', 'Cancelled']

// State-machine edges (same derivation as the backend TaskStateMachine) - used as a guard so the demo cannot show an impossible move.
const EDGES: Record<TaskState, TaskState[]> = {
  Pending: ['Validating', 'Failed', 'Cancelled'],
  Validating: ['Queued', 'Dispatching', 'Failed', 'Cancelled'],
  Queued: ['Dispatching', 'Failed', 'Cancelled'],
  Dispatching: ['Accepted', 'Failed', 'CancelPending'],
  Accepted: ['Executing', 'Failed', 'CancelPending'],
  Executing: ['WaitingForPlc', 'WaitingForDestination', 'Continuing', 'Completed', 'Failed', 'CancelPending'],
  WaitingForPlc: ['Executing', 'Failed', 'CancelPending'],
  WaitingForDestination: ['Continuing', 'Failed', 'CancelPending'],
  Continuing: ['Executing', 'Failed', 'CancelPending'],
  CancelPending: ['Cancelled', 'Failed', 'Completed'],
  Completed: [], Failed: [], Cancelled: [],
}

interface DLocation { id: string; code: string; name: string; type: string; cap: number; allowed: 'Any' | Kind; requiresPlc: boolean; recon: boolean; enabled: boolean }
interface DRack { id: string; ext: string | null; status: Rack['status']; locId: string | null; taskId: string | null; at: string }
interface DReservation { id: string; taskId: string; kind: 'rack' | 'dest'; rackId: string | null; locId: string; active: boolean }
interface DTask { pub: TransportTask; history: TaskHistory[]; kind: Kind; flow: string }
interface DAgv { id: string; code: string; robotCode: string | null; enabled: boolean; role: Agv['role']; kind: Kind; status: Agv['connectionStatus']; detail: string | null; at: string }

export type DemoEventName = 'TaskCreated' | 'TaskStateChanged' | 'InventoryChanged' | 'AgvStatusChanged'
type Listener = (name: DemoEventName, payload: unknown) => void

const LOCATION_DEFS: Omit<DLocation, 'id' | 'recon' | 'enabled'>[] = [
  { code: 'A1', name: 'Máy A1', type: 'Station', cap: 1, allowed: 'Any', requiresPlc: true },
  { code: 'A2', name: 'Máy A2', type: 'Station', cap: 1, allowed: 'Any', requiresPlc: true },
  { code: 'B1', name: 'Máy B1', type: 'Station', cap: 1, allowed: 'Any', requiresPlc: true },
  { code: 'B2', name: 'Máy B2', type: 'Station', cap: 1, allowed: 'Any', requiresPlc: true },
  { code: 'F1', name: 'Vị trí đệm rack đầy F1', type: 'Buffer', cap: 3, allowed: 'Full', requiresPlc: false },
  { code: 'F2', name: 'Vị trí đệm rack đầy F2', type: 'Buffer', cap: 3, allowed: 'Full', requiresPlc: false },
  { code: 'F3', name: 'Vị trí đệm rack rỗng F3', type: 'Buffer', cap: 3, allowed: 'Empty', requiresPlc: false },
  { code: 'F4', name: 'Vị trí đệm rack rỗng F4', type: 'Buffer', cap: 3, allowed: 'Empty', requiresPlc: false },
  { code: 'F5', name: 'Vị trí đệm thủ công F5', type: 'ManualBuffer', cap: 3, allowed: 'Any', requiresPlc: false },
]

export class DemoError extends Error {
  readonly code: string
  readonly status: number
  readonly details?: unknown
  constructor(status: number, code: string, message: string, details?: unknown) {
    super(message)
    this.details = details
    this.status = status
    this.code = code
  }
}

const nowIso = () => new Date().toISOString()
const uid = () => crypto.randomUUID()

export class DemoEngine {
  private locations: DLocation[] = []
  private racks: DRack[] = []
  private reservations: DReservation[] = []
  private tasks: DTask[] = []
  private transactions: InventoryTransaction[] = []
  private agvs: DAgv[] = []
  private listeners = new Set<Listener>()
  private storeListeners = new Set<() => void>()
  private version = 0
  private rcsCounter = 0
  private rackCounter = 0
  private keyCounter = 0
  private timer: ReturnType<typeof setInterval> | null = null
  private autoplayMs = 1500

  constructor() { this.reset() }

  // ------------------------------------------------------------------ store plumbing (React useSyncExternalStore)
  subscribeStore = (l: () => void) => { this.storeListeners.add(l); return () => { this.storeListeners.delete(l) } }
  getVersion = () => this.version
  get autoplay() { return this.timer !== null }
  get autoplaySpeedMs() { return this.autoplayMs }

  on(l: Listener) { this.listeners.add(l); return () => { this.listeners.delete(l) } }

  private changed(created?: DTask) {
    this.version++
    if (created) this.listeners.forEach((l) => l('TaskCreated', structuredClone(created.pub)))
    for (const n of ['TaskStateChanged', 'InventoryChanged', 'AgvStatusChanged'] as const) this.listeners.forEach((l) => l(n, null))
    this.storeListeners.forEach((l) => l())
  }

  // ------------------------------------------------------------------ lifecycle
  reset() {
    this.stopAutoplay()
    this.locations = LOCATION_DEFS.map((d) => ({ ...d, id: uid(), recon: false, enabled: true }))
    this.racks = []; this.reservations = []; this.tasks = []; this.transactions = []
    this.agvs = [
      { id: uid(), code: 'AGV1', robotCode: 'DEMO-AGV1', enabled: true, role: 'FullRack', kind: 'Full', status: 'Online', detail: 'mô phỏng', at: nowIso() },
      { id: uid(), code: 'AGV2', robotCode: 'DEMO-AGV2', enabled: true, role: 'EmptyRack', kind: 'Empty', status: 'Online', detail: 'mô phỏng', at: nowIso() },
    ]
    this.rcsCounter = 0; this.rackCounter = 0; this.keyCounter = 0

    // Starting scenario so every page has content.
    this.addStock('A1', 'Full', 1)
    this.addStock('B1', 'Empty', 1)
    this.addStock('F1', 'Full', 2); this.addStock('F2', 'Full', 1)
    this.addStock('F3', 'Empty', 2); this.addStock('F5', 'Empty', 1)
    const done = this.startTask('BP05', 'F5')
    for (let i = 0; i < 6 && done && !TERMINAL.includes(done.pub.state); i++) this.step(done.pub.id)
    this.startTask('BP01', 'A1')               // AGV1 working (Accepted)
    this.startTask('BP02', 'A2')               // AGV2 working (Accepted)
    this.startTask('BP05', 'F5')               // queued behind AGV1 (PER_AGV)
    this.changed()
  }

  startAutoplay(ms = this.autoplayMs) {
    this.stopAutoplay()
    this.autoplayMs = ms
    this.timer = setInterval(() => this.stepAll(), ms)
    this.changed()
  }

  stopAutoplay() {
    if (this.timer) { clearInterval(this.timer); this.timer = null; this.version++; this.storeListeners.forEach((l) => l()) }
  }

  // ------------------------------------------------------------------ read API (same shapes as the real API)
  health(): SystemHealth {
    return {
      status: 'Healthy', time: nowIso(), database: { connected: true },
      scheduler: { lastRunAt: nowIso(), lastError: null, stale: false },
      integration: { mode: 'Simulator', plc: 'Simulated (demo)' },
    }
  }

  listAgvs(): Agv[] {
    return this.agvs.map((a) => {
      const active = this.tasks.find((t) => t.pub.agvId === a.id && OCCUPYING.includes(t.pub.state))
      return {
        id: a.id, code: a.code, role: a.role, rcsRobotCode: a.robotCode, enabled: a.enabled, connectionStatus: a.status,
        statusDetail: a.detail, lastStatusAt: a.at, activeTaskId: active?.pub.id ?? null, busy: !!active,
      }
    })
  }

  listLocations(): Location[] {
    return this.locations.map((l) => {
      const occupied = this.racks.filter((r) => r.locId === l.id).length
      const reserved = this.reservations.filter((r) => r.active && r.kind === 'dest' && r.locId === l.id).length
      return {
        id: l.id, code: l.code, name: l.name, type: l.type, rackCapacity: l.cap, allowedRackKind: l.allowed, enabled: l.enabled,
        requiresPlcPermission: l.requiresPlc, occupied, reserved,
        occupancy: l.recon ? 'Unknown' : occupied > 0 ? 'Occupied' : reserved > 0 ? 'Reserved' : 'Empty',
      }
    })
  }

  listRacks(locationCode?: string): Rack[] {
    const loc = locationCode ? this.locations.find((l) => l.code === locationCode) : undefined
    if (locationCode && !loc) throw new DemoError(404, 'LOCATION_NOT_FOUND', `Location '${locationCode}' not found.`)
    return this.racks
      .filter((r) => !loc || r.locId === loc.id)
      .map((r) => ({ id: r.id, externalRackId: r.ext, rackType: null, status: r.status, currentLocationId: r.locId, currentTaskId: r.taskId, lastConfirmedAt: r.at }))
  }

  listTransactions(locationCode?: string): InventoryTransaction[] {
    const loc = locationCode ? this.locations.find((l) => l.code === locationCode) : undefined
    return [...this.transactions]
      .filter((t) => !loc || t.fromLocationId === loc.id || t.toLocationId === loc.id)
      .sort((a, b) => b.occurredAt.localeCompare(a.occurredAt)).slice(0, 50)
  }

  listTasks(state: TaskState | undefined, skip: number, take: number) {
    const all = this.tasks.filter((t) => !state || t.pub.state === state).sort((a, b) => b.pub.createdAt.localeCompare(a.pub.createdAt))
    return { items: all.slice(skip, skip + take).map((t) => t.pub), total: all.length, skip, take }
  }

  getTask(id: string): TransportTask { return this.require(id).pub }
  getHistory(id: string): TaskHistory[] { return this.require(id).history }
  tasksNeedingReconciliation() { return this.tasks.filter((t) => t.pub.requiresReconciliation && TERMINAL.includes(t.pub.state)).map((t) => t.pub) }
  locationCode(id: string | null) { return this.locations.find((l) => l.id === id)?.code ?? '—' }

  // ------------------------------------------------------------------ operator actions
  /** Creates a task like a station request would. Returns the task or throws a DemoError for rejected requests. */
  createTask(flow: string, stationCode: string): TransportTask {
    const t = this.startTask(flow, stationCode)
    if (!t) throw new DemoError(422, 'FLOW_NOT_SUPPORTED', 'Quy trình không được hỗ trợ trong chế độ mô phỏng.')
    this.changed(t)
    return t.pub
  }

  /** Same rule as the backend: a task can be edited/deleted by a user only before an AGV has been claimed for it. */
  private ensureUserEditable(t: DTask, expectedState: string | undefined, isAdmin: boolean) {
    const s = t.pub.state
    if (expectedState && expectedState !== s) throw new DemoError(409, 'TASK_STATE_CHANGED', `Nhiệm vụ đã chuyển sang trạng thái ${s}. Vui lòng tải lại dữ liệu.`)
    if (s === 'Pending' || s === 'Validating' || s === 'Queued') return
    throw isAdmin
      ? new DemoError(409, 'TASK_NOT_EDITABLE', `Nhiệm vụ đang ở trạng thái ${s} nên không thể sửa/xóa trực tiếp.`)
      : new DemoError(403, 'TASK_LOCKED', 'Nhiệm vụ đã được AGV tiếp nhận hoặc đã kết thúc; tài khoản Operator không có quyền sửa/xóa.')
  }

  updateTask(id: string, stationCode: string, expectedState: string | undefined, isAdmin: boolean): TransportTask {
    const t = this.require(id)
    this.ensureUserEditable(t, expectedState, isAdmin)
    const rule = DEMO_FLOWS[t.flow]
    const station = this.locations.find((l) => l.code === stationCode)
    if (!station) throw new DemoError(404, 'STATION_NOT_FOUND', `Không tìm thấy trạm '${stationCode}'.`)
    if (!rule.stations.includes(station.code)) throw new DemoError(422, 'STATION_NOT_ALLOWED', `Trạm ${station.code} không được phép yêu cầu quy trình ${t.flow}.`)
    if (station.id === t.pub.requestLocationId) return t.pub
    const oldCode = this.locationCode(t.pub.requestLocationId)
    const oldReservations = this.reservations.filter((r) => r.active && r.taskId === t.pub.id)
    const saved = { src: t.pub.sourceLocationId, rack: t.pub.rackId }
    if (rule.srcMode === 'Request') {
      oldReservations.forEach((r) => { r.active = false })
      t.pub.sourceLocationId = null; t.pub.rackId = null
    }
    t.pub.requestLocationId = station.id; t.pub.destinationLocationId = null
    if (rule.srcMode === 'Request' && t.pub.state === 'Queued' && !this.reserveSource(t, rule)) {
      // Not possible: leave everything exactly as it was.
      t.pub.requestLocationId = this.locations.find((l) => l.code === oldCode)!.id
      t.pub.sourceLocationId = saved.src; t.pub.rackId = saved.rack
      oldReservations.forEach((r) => { r.active = true })
      throw new DemoError(422, 'NO_SOURCE_AT_STATION', `Trạm ${station.code} hiện không có rack phù hợp để lấy. Nhiệm vụ không bị thay đổi.`)
    }
    t.pub.updatedAt = nowIso()
    this.log(t, t.pub.state, t.pub.state, 'TASK_EDITED', `station ${oldCode} -> ${station.code}`, 'operator')
    this.schedule()
    this.changed()
    return t.pub
  }

  /** "Delete" keeps the record: the task becomes Cancelled and its reservations are released. */
  deleteTask(id: string, expectedState: string | undefined, isAdmin: boolean): TransportTask {
    const t = this.require(id)
    this.ensureUserEditable(t, expectedState, isAdmin)
    this.log(t, t.pub.state, t.pub.state, 'TASK_DELETED', 'deleted by user', 'operator')
    this.move(t, 'Cancelled', 'Deleted by user before dispatch', 'operator')
    this.releaseReservations(t)
    this.schedule()
    this.changed()
    return t.pub
  }

  cancelTask(id: string, reason: string): TransportTask {
    const t = this.require(id)
    const s = t.pub.state
    if (s === 'Cancelled') return t.pub
    if (TERMINAL.includes(s)) throw new DemoError(422, 'TASK_ALREADY_FINISHED', `Nhiệm vụ đã kết thúc (${s}), không thể thao tác.`)
    if (s === 'Pending' || s === 'Validating' || s === 'Queued') {
      this.move(t, 'Cancelled', `Cancel requested: ${reason}`)
      this.releaseReservations(t)
    } else if (s !== 'CancelPending') this.move(t, 'CancelPending', `Cancel requested: ${reason} (waiting for RCS confirmation)`)
    this.schedule()
    this.changed()
    return t.pub
  }

  /** One simulated device step (what the SimulationDriver does in the backend). */
  advance(id: string): string {
    const msg = this.step(id)
    this.schedule()
    this.changed()
    return msg
  }

  stepAll() {
    for (const t of this.tasks.filter((x) => !TERMINAL.includes(x.pub.state) && x.pub.state !== 'Queued')) this.step(t.pub.id)
    this.schedule()
    this.changed()
  }

  /** RCS reports the task interrupted -> FAILED, rack uncertain, reconciliation required. */
  interruptTask(id: string) {
    const t = this.require(id)
    if (TERMINAL.includes(t.pub.state)) throw new DemoError(422, 'TASK_ALREADY_FINISHED', `Nhiệm vụ đã kết thúc (${t.pub.state}), không thể thao tác.`)
    this.finish(t, 'Failed', 'RCS reported the task interrupted')
    this.schedule(); this.changed()
  }

  reconcile(id: string, locationCode: string) {
    const t = this.require(id)
    const loc = this.locations.find((l) => l.code === locationCode)
    if (!loc || !t.pub.requiresReconciliation) throw new DemoError(422, 'NO_RECONCILIATION_NEEDED', 'Không có gì cần đối soát.')
    const rack = this.racks.find((r) => r.id === t.pub.rackId)
    if (rack) {
      const before = rack.status
      rack.status = t.kind; rack.locId = loc.id; rack.taskId = null; rack.at = nowIso()
      this.tx('Reconciliation', rack.id, t.pub.sourceLocationId, loc.id, t.pub.id, before, rack.status, 'WcsReconciliation', 'NotRequested', 'Confirmed by operator (demo)')
    }
    this.releaseReservations(t)
    for (const l of this.locations) l.recon = false
    t.pub.requiresReconciliation = false
    this.log(t, null, null, 'RECONCILED', `actual=${loc.code}`, 'Operator')
    this.schedule(); this.changed()
  }

  setAgvOnline(code: string, online: boolean) {
    const a = this.agvs.find((x) => x.code === code)
    if (!a) return
    a.status = online ? 'Online' : 'Offline'; a.detail = online ? 'mô phỏng' : 'ngắt kết nối mô phỏng'; a.at = nowIso()
    this.changed()
  }

  // ------------------------------------------------------------------ configuration data (AGV robot codes, locations)
  configureAgv(id: string, robotCode: string | null, enabled: boolean) {
    const a = this.agvs.find((x) => x.id === id)
    if (!a) throw new DemoError(404, 'AGV_NOT_FOUND', 'Không tìm thấy AGV.')
    if (robotCode && this.agvs.some((x) => x.id !== id && x.robotCode?.toLowerCase() === robotCode.toLowerCase()))
      throw new DemoError(400, 'VALIDATION_FAILED', 'Mã robot này đã được gán cho AGV khác.', [{ field: 'rcsRobotCode', message: 'Mã robot này đã được gán cho AGV khác.' }])
    if (!enabled && a.enabled && this.tasks.some((t) => t.pub.agvId === id && OCCUPYING.includes(t.pub.state)))
      throw new DemoError(409, 'AGV_HAS_ACTIVE_TASK', 'AGV đang có nhiệm vụ hoạt động nên chưa thể tắt.')
    a.robotCode = robotCode; a.enabled = enabled
    this.changed()
    return this.listAgvs().find((x) => x.id === id)!
  }

  saveLocation(b: { code: string; name: string; type: string; rackCapacity: number; allowedRackKind: string; enabled: boolean; requiresPlcPermission: boolean }, create: boolean) {
    const bad = (field: string, message: string) => new DemoError(400, 'VALIDATION_FAILED', message, [{ field, message }])
    if (!/^[A-Za-z0-9_-]{1,50}$/.test(b.code)) throw bad('code', 'Mã vị trí chỉ gồm chữ, số, gạch ngang, gạch dưới (tối đa 50 ký tự).')
    if (!b.name.trim()) throw bad('name', 'Cần nhập tên vị trí.')
    if (!Number.isInteger(b.rackCapacity) || b.rackCapacity < 0 || b.rackCapacity > 1000) throw bad('rackCapacity', 'Sức chứa phải nằm trong khoảng 0–1000.')
    const existing = this.locations.find((l) => l.code === b.code)
    if (create) {
      if (existing) throw bad('code', 'Mã vị trí đã tồn tại.')
      this.locations.push({ id: uid(), code: b.code, name: b.name.trim(), type: b.type, cap: b.rackCapacity, allowed: b.allowedRackKind as DLocation['allowed'], requiresPlc: b.requiresPlcPermission, recon: false, enabled: b.enabled })
    } else {
      if (!existing) throw new DemoError(404, 'LOCATION_NOT_FOUND', `Không tìm thấy vị trí '${b.code}'.`)
      const used = this.racks.filter((r) => r.locId === existing.id).length + this.reservations.filter((r) => r.active && r.kind === 'dest' && r.locId === existing.id).length
      if (b.rackCapacity < used) throw bad('rackCapacity', `Sức chứa không được nhỏ hơn số rack đang có và đang giữ chỗ (${used}).`)
      Object.assign(existing, { name: b.name.trim(), cap: b.rackCapacity, allowed: b.allowedRackKind, enabled: b.enabled, requiresPlc: b.requiresPlcPermission })
    }
    this.changed()
    return this.listLocations().find((l) => l.code === b.code)!
  }

  /** Same shape as the backend "operations" view; policy/scheduler/flows are read-only. */
  operations() {
    return {
      policy: { concurrency: 'PER_AGV', concurrencyApproved: true, queueOrder: 'FIFO', queueOrderApproved: false, noSourceBehavior: 'Fail', noSourceBehaviorApproved: false },
      scheduler: { tickIntervalMs: 1000, maxDispatchAttempts: 5, stateTimeoutsSeconds: {} as Record<string, number> },
      flows: Object.entries(DEMO_FLOWS).map(([k, f]) => ({
        flow: k.replace('BP', 'Bp'), rackKind: f.kind, agvRole: f.kind === 'Full' ? 'FullRack' : 'EmptyRack', stations: [...f.stations],
        sourceMode: f.srcMode, sourceGroups: f.srcGroups, destinationMode: f.dstMode, destinationGroups: f.dstGroups, noBindDropLocations: [...f.noBind],
      })),
      agvs: this.listAgvs(),
      locations: this.listLocations(),
    }
  }

  /** Declares stock (PDA-style). Rack ids are anonymous: the rack model is NEEDS_CONFIRMATION. */
  declareStock(code: string, kind: Kind, qty: number) {
    this.addStock(code, kind, qty)
    this.changed()
  }

  /** Removes unreserved racks (manual adjustment) to make room. */
  removeStock(code: string, kind: Kind, qty: number) {
    const loc = this.loc(code)
    let n = 0
    for (const r of [...this.racks]) {
      if (n >= qty) break
      if (r.locId === loc.id && r.status === kind && !this.reservations.some((v) => v.active && v.rackId === r.id)) {
        this.tx('ManualAdjustment', r.id, loc.id, null, null, r.status, null, 'Operator', 'NotRequested', 'Manual adjustment (-)')
        this.racks = this.racks.filter((x) => x !== r); n++
      }
    }
    this.schedule(); this.changed()
    return n
  }

  private removeRack(r: DRack, note: string) {
    this.tx('ManualAdjustment', r.id, r.locId, null, null, r.status, null, 'Operator', 'NotRequested', note)
    this.racks = this.racks.filter((x) => x !== r)
  }

  /** Prepares the stock a flow needs and starts it - used by the scenario buttons. */
  runScenario(flow: string, station: string, opts?: { blockDestinations?: boolean }): TransportTask {
    const rule = DEMO_FLOWS[flow]
    if (opts?.blockDestinations) {
      for (const code of rule.dstGroups.flat()) {
        const l = this.loc(code)
        const free = l.cap - this.racks.filter((r) => r.locId === l.id).length - this.reservations.filter((r) => r.active && r.kind === 'dest' && r.locId === l.id).length
        if (free > 0) this.addStock(code, rule.kind, Math.min(free, l.allowed === 'Any' || l.allowed === rule.kind ? free : 0))
      }
    }
    const st = this.loc(station)
    const countAt = (l: DLocation) => this.racks.filter((r) => r.locId === l.id).length
    const free = (r: DRack) => !this.reservations.some((v) => v.active && v.rackId === r.id)
    // The operator physically prepares the station: make room, then place the rack the flow needs.
    const makeRoom = (l: DLocation, keep?: Kind) => {
      for (const r of this.racks.filter((x) => x.locId === l.id && free(x) && x.status !== keep)) {
        if (countAt(l) < l.cap) break
        this.removeRack(r, 'Operator cleared the position (demo)')
      }
    }
    if (rule.srcMode === 'Request') {
      const has = this.racks.some((r) => r.locId === st.id && r.status === rule.kind && free(r))
      if (!has) { makeRoom(st); if (countAt(st) < st.cap) this.addStock(station, rule.kind, 1) }
    } else {
      const any = rule.srcGroups.flat().some((c) => this.racks.some((r) => r.locId === this.loc(c).id && r.status === rule.kind && free(r)))
      if (!any) this.addStock(rule.srcGroups[0][0], rule.kind, 1)
    }
    if (rule.dstMode === 'Request') makeRoom(st)
    return this.createTask(flow, station)
  }

  // ------------------------------------------------------------------ core simulation (mirrors the orchestrator)
  private startTask(flow: string, stationCode: string): DTask | null {
    const rule = DEMO_FLOWS[flow]
    if (!rule) return null
    const station = this.locations.find((l) => l.code === stationCode)
    if (!station) throw new DemoError(404, 'STATION_NOT_FOUND', `Không tìm thấy trạm '${stationCode}'.`)
    if (!rule.stations.includes(station.code)) throw new DemoError(422, 'STATION_NOT_ALLOWED', `Trạm ${station.code} không được yêu cầu quy trình ${flow}.`)
    const agv = this.agvs.find((a) => a.kind === rule.kind)!
    const id = uid()
    const at = nowIso()
    const pub: TransportTask = {
      id, requestKey: `demo-${++this.keyCounter}`, flow, rackKind: rule.kind, agvId: agv.id, requestLocationId: station.id,
      sourceLocationId: null, destinationLocationId: null, rackId: null, state: 'Pending', leg: 'Pick', plcPermissionGranted: false,
      stateReason: null, rcsRequestCode: `WCS-${id.replaceAll('-', '')}`, rcsTaskCode: null, requiresReconciliation: false,
      pickConfirmedAt: null, dropConfirmedAt: null, createdAt: at, updatedAt: at, finishedAt: null,
    }
    const t: DTask = { pub, history: [], kind: rule.kind, flow }
    this.tasks.push(t)
    this.log(t, null, 'Pending', 'TASK_CREATED', `flow=${flow}`, 'WCS')
    this.move(t, 'Validating', 'Validating business conditions')

    if (!this.reserveSource(t, rule)) { this.finish(t, 'Failed', 'NO_SOURCE_AVAILABLE'); return t }
    const queuedBefore = this.tasks.some((x) => x !== t && x.pub.agvId === agv.id && x.pub.state === 'Queued')
    const busy = this.tasks.some((x) => x !== t && x.pub.agvId === agv.id && OCCUPYING.includes(x.pub.state))
    if (busy || queuedBefore) this.move(t, 'Queued', 'AGV busy; request queued')
    else this.dispatch(t)
    return t
  }

  private dispatch(t: DTask) {
    this.move(t, 'Dispatching', 'AGV free; claimed')
    t.pub.rcsTaskCode = `DEMO-${String(++this.rcsCounter).padStart(5, '0')}`
    this.move(t, 'Accepted', 'RCS accepted the task (simulated)', 'RCS')
  }

  /** Scheduler pass: fill free AGVs FIFO and re-evaluate tasks waiting for a destination. */
  private schedule() {
    let progress = true
    while (progress) {
      progress = false
      for (const agv of this.agvs) {
        if (this.tasks.some((t) => t.pub.agvId === agv.id && OCCUPYING.includes(t.pub.state))) continue
        const next = this.tasks.filter((t) => t.pub.agvId === agv.id && t.pub.state === 'Queued')
          .sort((a, b) => a.pub.createdAt.localeCompare(b.pub.createdAt))[0]
        if (next) { this.dispatch(next); progress = true }
      }
      for (const t of this.tasks.filter((x) => x.pub.state === 'WaitingForDestination')) {
        if (this.reserveDestination(t, DEMO_FLOWS[t.flow])) { this.move(t, 'Continuing', `Destination assigned: ${this.locationCode(t.pub.destinationLocationId)}`); progress = true }
      }
    }
  }

  private step(id: string): string {
    const t = this.require(id)
    const p = t.pub
    const rule = DEMO_FLOWS[t.flow]
    switch (p.state) {
      case 'Accepted': this.move(t, 'Executing', 'AGV executing', 'RCS'); return 'AGV bắt đầu thực hiện nhiệm vụ'
      case 'Continuing': this.move(t, 'Executing', 'RCS continued the task', 'RCS'); return 'RCS chấp nhận tiếp tục nhiệm vụ (continueTask, mô phỏng)'
      case 'WaitingForPlc': p.plcPermissionGranted = true; this.move(t, 'Executing', 'PLC permission granted', 'PLC'); return 'PLC đã cho phép'
      case 'WaitingForDestination': return 'Vẫn đang chờ: chưa có vị trí đích còn chỗ trống (hãy giải phóng một vị trí).'
      case 'Queued': return 'Đang chờ: AGV đang bận nhiệm vụ khác (mỗi AGV chỉ một nhiệm vụ hoạt động).'
      case 'CancelPending': this.finish(t, 'Cancelled', 'Cancel confirmed by RCS'); return 'RCS đã xác nhận hủy nhiệm vụ'
      case 'Executing': {
        const loc = this.locations.find((l) => l.id === (p.leg === 'Pick' ? p.sourceLocationId : p.destinationLocationId))
        if (loc?.requiresPlc && !p.plcPermissionGranted) { this.move(t, 'WaitingForPlc', `Waiting for PLC permission (${p.leg})`); return `Đang chờ PLC cho phép tại ${loc.code}` }
        return p.leg === 'Pick' ? this.confirmPick(t, rule) : this.confirmDrop(t, rule)
      }
      default: return `Không có bước mô phỏng nào ở trạng thái này.`
    }
  }

  private confirmPick(t: DTask, rule: FlowRule): string {
    const p = t.pub
    const rack = this.racks.find((r) => r.id === p.rackId)!
    const before = rack.status
    rack.status = 'InTransit'; rack.locId = null; rack.taskId = p.id; rack.at = nowIso()
    this.tx('PickConfirmed', rack.id, p.sourceLocationId, null, p.id, before, 'InTransit', 'Plc', 'NotRequested', null)
    this.reservations.filter((r) => r.active && r.taskId === p.id && r.kind === 'rack').forEach((r) => { r.active = false })
    p.pickConfirmedAt = nowIso(); p.leg = 'Drop'; p.plcPermissionGranted = false
    this.log(t, p.state, p.state, 'PICK_CONFIRMED', null, 'PLC')
    if (this.reserveDestination(t, rule)) {
      this.move(t, 'Continuing', `Destination assigned: ${this.locationCode(p.destinationLocationId)}`)
      return `Đã lấy rack; đã giữ chỗ vị trí đích ${this.locationCode(p.destinationLocationId)}`
    }
    this.move(t, 'WaitingForDestination', 'No destination available')
    return 'Đã lấy rack; chưa có vị trí đích → chuyển sang chờ vị trí đích'
  }

  private confirmDrop(t: DTask, rule: FlowRule): string {
    const p = t.pub
    const rack = this.racks.find((r) => r.id === p.rackId)!
    const dest = this.locations.find((l) => l.id === p.destinationLocationId)!
    const noBind = rule.noBind.includes(dest.code)
    // The demo simulates a scanned rack id on every drop to show where binding is (not) applied.
    const scanned = `RACK-${String(++this.rackCounter).padStart(3, '0')}`
    if (!noBind) rack.ext = scanned
    rack.status = t.kind; rack.locId = dest.id; rack.taskId = null; rack.at = nowIso()
    this.tx('DropConfirmed', rack.id, p.sourceLocationId, dest.id, p.id, 'InTransit', t.kind, 'Plc', noBind ? 'NotBoundByProcess' : 'Bound',
      noBind ? 'NO_BIND_POINT: external rack id ignored' : null)
    this.reservations.filter((r) => r.active && r.taskId === p.id).forEach((r) => { r.active = false })
    p.dropConfirmedAt = nowIso(); p.plcPermissionGranted = false
    this.move(t, 'Completed', 'Drop confirmed; inventory updated', 'PLC')
    return noBind ? `Đã thả rack tại ${dest.code} – điểm "Không bind rack", không gán Rack ID` : `Đã thả rack tại ${dest.code}; đã bind ${scanned}`
  }

  private reserveSource(t: DTask, rule: FlowRule): boolean {
    const codes = rule.srcMode === 'Request' ? [this.locationCode(t.pub.requestLocationId)] : rule.srcGroups.flat()
    for (const code of codes) {
      const loc = this.locations.find((l) => l.code === code)
      if (!loc || loc.recon || !loc.enabled) continue
      const rack = this.racks.find((r) => r.locId === loc.id && r.status === t.kind && !this.reservations.some((v) => v.active && v.rackId === r.id))
      if (!rack) continue
      this.reservations.push({ id: uid(), taskId: t.pub.id, kind: 'rack', rackId: rack.id, locId: loc.id, active: true })
      t.pub.sourceLocationId = loc.id; t.pub.rackId = rack.id
      this.log(t, t.pub.state, t.pub.state, 'SOURCE_RESERVED', `location=${loc.code}`, 'WCS')
      return true
    }
    return false
  }

  private reserveDestination(t: DTask, rule: FlowRule): boolean {
    const codes = rule.dstMode === 'Request' ? [this.locationCode(t.pub.requestLocationId)] : rule.dstGroups.flat()
    for (const code of codes) {
      const loc = this.locations.find((l) => l.code === code)
      if (!loc || loc.recon || !loc.enabled || !(loc.allowed === 'Any' || loc.allowed === t.kind)) continue
      const occupied = this.racks.filter((r) => r.locId === loc.id).length
      const reserved = this.reservations.filter((r) => r.active && r.kind === 'dest' && r.locId === loc.id).length
      if (occupied + reserved >= loc.cap) continue
      this.reservations.push({ id: uid(), taskId: t.pub.id, kind: 'dest', rackId: null, locId: loc.id, active: true })
      t.pub.destinationLocationId = loc.id
      this.log(t, t.pub.state, t.pub.state, 'DESTINATION_RESERVED', `location=${loc.code}`, 'WCS')
      return true
    }
    return false
  }

  /** Terminal transition. Physical contact -> keep reservations and require reconciliation (inventory 4.4 / 4.8). */
  private finish(t: DTask, terminal: 'Failed' | 'Cancelled', reason: string) {
    const physical = !!t.pub.rcsTaskCode || !!t.pub.pickConfirmedAt
    this.move(t, terminal, reason)
    if (physical) {
      t.pub.requiresReconciliation = true
      if (t.pub.pickConfirmedAt && !t.pub.dropConfirmedAt) {
        const rack = this.racks.find((r) => r.id === t.pub.rackId)
        if (rack) rack.status = 'Unknown'
        for (const id of [t.pub.sourceLocationId, t.pub.destinationLocationId]) {
          const l = this.locations.find((x) => x.id === id)
          if (l) l.recon = true
        }
      }
      this.log(t, t.pub.state, t.pub.state, 'RECONCILIATION_REQUIRED', reason, 'WCS')
    } else this.releaseReservations(t)
  }

  private releaseReservations(t: DTask) {
    this.reservations.filter((r) => r.active && r.taskId === t.pub.id).forEach((r) => { r.active = false })
  }

  // ------------------------------------------------------------------ helpers
  private move(t: DTask, to: TaskState, reason: string, source = 'WCS') {
    const from = t.pub.state
    if (from !== to && !EDGES[from].includes(to)) throw new DemoError(409, 'INVALID_TASK_TRANSITION', `Không thể chuyển nhiệm vụ từ ${from} sang ${to}.`)
    t.pub.state = to; t.pub.stateReason = reason; t.pub.updatedAt = nowIso()
    if (TERMINAL.includes(to)) t.pub.finishedAt = nowIso()
    this.log(t, from, to, 'STATE_CHANGED', reason, source)
  }

  private log(t: DTask, from: string | null, to: string | null, event: string, detail: string | null, source: string) {
    t.history.push({ id: uid(), fromState: from, toState: to, event, detail, source, occurredAt: nowIso() })
    t.pub.updatedAt = nowIso()
  }

  private tx(eventType: string, rackId: string | null, from: string | null, to: string | null, taskId: string | null,
    before: string | null, after: string | null, source: string, bind: string, note: string | null) {
    this.transactions.push({
      id: uid(), eventType, rackId, fromLocationId: from, toLocationId: to, taskId, statusBefore: before, statusAfter: after,
      source, userName: source === 'Operator' ? 'demo' : null, bind, note, occurredAt: nowIso(),
    })
  }

  private addStock(code: string, kind: Kind, qty: number) {
    const loc = this.loc(code)
    for (let i = 0; i < qty; i++) {
      if (this.racks.filter((r) => r.locId === loc.id).length >= loc.cap) throw new DemoError(409, 'CAPACITY_EXCEEDED', `${code} đã đầy.`)
      const r: DRack = { id: uid(), ext: null, status: kind, locId: loc.id, taskId: null, at: nowIso() }
      this.racks.push(r)
      this.tx('InitialStockDeclared', r.id, null, loc.id, null, null, kind, 'Operator', 'NotRequested', 'Initial stock declared (demo)')
    }
  }

  private loc(code: string) {
    const l = this.locations.find((x) => x.code === code)
    if (!l) throw new DemoError(404, 'LOCATION_NOT_FOUND', `Không tìm thấy vị trí '${code}'.`)
    return l
  }

  private require(id: string) {
    const t = this.tasks.find((x) => x.pub.id === id)
    if (!t) throw new DemoError(404, 'TASK_NOT_FOUND', 'Không tìm thấy nhiệm vụ.')
    return t
  }
}

export const demo = new DemoEngine()
