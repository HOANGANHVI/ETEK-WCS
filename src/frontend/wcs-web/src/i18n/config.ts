import { localizedMap } from './core'
import type { Tone } from './vi'

export const TEST_OUTCOME_VI = localizedMap(
  { Success: 'Thành công', Failed: 'Thất bại', Simulated: 'Chưa kiểm tra với thiết bị', NotExecuted: 'Không thực hiện' },
  { Success: 'Success', Failed: 'Failed', Simulated: 'Not tested against the device', NotExecuted: 'Not performed' })
export const TEST_OUTCOME_TONE: Record<string, Tone> = { Success: 'ok', Failed: 'bad', Simulated: 'warn', NotExecuted: 'muted' }

export const TEST_MODE_VI = localizedMap({
  Simulated: 'Chế độ mô phỏng – chưa kiểm tra với thiết bị thật',
  TcpReachability: 'Kiểm tra cổng TCP (không gửi dữ liệu)',
  None: 'Không thực hiện',
}, {
  Simulated: 'Simulation mode – not tested against the real device',
  TcpReachability: 'TCP port check (no data sent)',
  None: 'Not performed',
})

export const PLC_PROTOCOL_VI = localizedMap(
  { McProtocol3EBinaryTcp: 'MC Protocol – khung 3E, nhị phân, TCP/IP (dự kiến, cần xác nhận)' },
  { McProtocol3EBinaryTcp: 'MC Protocol – 3E frame, binary, TCP/IP (proposed, to be confirmed)' })

export const AUTH_MODE_VI = localizedMap(
  { None: 'Không xác thực', Token: 'Token', UsernamePassword: 'Tên đăng nhập và mật khẩu' },
  { None: 'No authentication', Token: 'Token', UsernamePassword: 'Username and password' })

export const NO_SOURCE_VI = localizedMap(
  { Fail: 'Báo lỗi ngay (Fail)', Wait: 'Chờ có rack nguồn (Wait)' },
  { Fail: 'Fail immediately (Fail)', Wait: 'Wait for a source rack (Wait)' })
export const MODE_VI = localizedMap(
  { Request: 'Trạm yêu cầu', Search: 'Tìm theo thứ tự nhóm' },
  { Request: 'Requesting station', Search: 'Search in group order' })
