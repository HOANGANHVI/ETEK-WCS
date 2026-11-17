import type { Tone } from './vi'

export const TEST_OUTCOME_VI: Record<string, string> = {
  Success: 'Thành công', Failed: 'Thất bại', Simulated: 'Chưa kiểm tra với thiết bị', NotExecuted: 'Không thực hiện',
}
export const TEST_OUTCOME_TONE: Record<string, Tone> = { Success: 'ok', Failed: 'bad', Simulated: 'warn', NotExecuted: 'muted' }

export const TEST_MODE_VI: Record<string, string> = {
  Simulated: 'Chế độ mô phỏng – chưa kiểm tra với thiết bị thật',
  TcpReachability: 'Kiểm tra cổng TCP (không gửi dữ liệu)',
  None: 'Không thực hiện',
}

export const PLC_PROTOCOL_VI: Record<string, string> = {
  McProtocol3EBinaryTcp: 'MC Protocol – khung 3E, nhị phân, TCP/IP (dự kiến, cần xác nhận)',
}

export const AUTH_MODE_VI: Record<string, string> = {
  None: 'Không xác thực',
  Token: 'Token',
  UsernamePassword: 'Tên đăng nhập và mật khẩu',
}

export const NO_SOURCE_VI: Record<string, string> = { Fail: 'Báo lỗi ngay (Fail)', Wait: 'Chờ có rack nguồn (Wait)' }
export const MODE_VI: Record<string, string> = { Request: 'Trạm yêu cầu', Search: 'Tìm theo thứ tự nhóm' }
