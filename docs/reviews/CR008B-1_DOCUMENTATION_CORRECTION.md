# CR-008B-1 – Documentation Correction

**Ngày:** 2026-11-17 · **Phạm vi:** chỉ tài liệu (không đổi mã nguồn, schema, cấu hình, thông số retry) · **Branch:** `develop`

## 1. Nội dung đã kiểm tra và sửa

| # | Nhận định cũ | Sự thật | Bằng chứng | Tài liệu đã sửa |
|---|---|---|---|---|
| 1 | Yêu cầu PLC không được ack sẽ được PLC "trình lại" | **Sai.** `PlcStationSignalService.PollRequestsAsync` chỉ nhận **cạnh lên** (`if (!high \|\| wasHigh) continue;`) và ghi `_high[key]` ngay khi poll; `AcknowledgeRequestAsync` chỉ gọi `events.Log(...)` (không ghi `Request_Ack`). Yêu cầu có thể mất dù ack hay không | `WCS.Application/Plc/PlcStationSignalService.cs` | ADR-0007 (mục Đính chính), báo cáo CR-008A, CHANGE_REQUESTS (CR-008A), CHANGELOG |
| 2 | Dispatch hết hạn mức sau ≈ 30 s | **≈ 62 s**: các lần thử tại 0/2/6/14/30 s, rồi chờ thêm `DelayAfter(5)=32 s` vì cổng backoff đặt trước kiểm tra hạn mức | `TaskOrchestrator.DispatchToRcsAsync` (cổng `IsDue` trước `DispatchAttempts >= Max`); `RcsRetryPolicy.DelayAfter`; tính lại bằng công thức: độ trễ 2,4,8,16,32,60,60… | ADR-0007, báo cáo CR-008A, CHANGE_REQUESTS, CHANGELOG, review CR-008A |
| 3 | Continue/Cancel hết hạn mức ≈ 30 phút | **≈ 25 phút** (lần thứ 30 tại 1 502 s; "exhausted" được khai báo ở tick kế tiếp vì kiểm tra hạn mức đặt trước `IsDue`) | như trên | ADR-0007, review CR-008A (REV-001) |
| 4 | NotFound hai lần liên tiếp ⇒ Cancelled | Chưa được chủ dự án phê duyệt (D-9); trái `task-state-machine.md` §3.9 | spec §3.9 | ADR-0007 (rút lại), CHANGE_REQUESTS; mã sẽ gỡ ở B-3 |
| 5 | Hết hạn mức cancel: task ở lại `CancelPending` | Chủ dự án chốt D-1: `Failed` + `RequiresReconciliation` | chỉ dẫn CR-008B | CHANGE_REQUESTS (CR-008B), ADR-0007; mã ở B-3 |

Kiểm tra số liệu: công thức của `RcsRetryPolicy` (khởi đầu 2 000 ms, nhân đôi, trần 60 000 ms) được tính lại độc lập (`2,4,8,16,32,60…`; dispatch 0/2/6/14/30 → 62 s; 1 502 s ≈ 25,0 phút). Việc gọi trực tiếp lớp đã biên dịch bằng PowerShell không thực hiện được (assembly net10 không nạp được trong Windows PowerShell 5.1); số liệu sẽ được **khẳng định bằng unit test thật** `RcsRetryPolicyTests` ở CR-008B-2. Nếu test lệch, tài liệu sẽ được sửa lại.

## 2. Nguyên tắc áp dụng
- Không xoá lịch sử: câu gốc được giữ, đánh dấu "đính chính" kèm ngày và lý do.
- Phân biệt trong tài liệu bốn khái niệm: **phát hiện cạnh tín hiệu**, **WCS chấp nhận yêu cầu**, **PLC acknowledgement**, **theo dõi yêu cầu bền vững** (ADR-0007, mục Đính chính 1).
- Không đổi thông số retry; không đổi `task-state-machine.md` (không cần: quy tắc đã phê duyệt không thay đổi).

## 3. Chưa làm trong B-1 (theo thiết kế)
`IMPACT_MATRIX.md` và `REQUIREMENT_TRACEABILITY.md` được cập nhật khi từng sub-CR có thay đổi hành vi (B-2…B-4), không phải ở B-1.

## 4. Kiểm thử
Không có mã nguồn thay đổi ⇒ không cần chạy test. Đã kiểm tra: working tree chỉ có file tài liệu thay đổi; `main` vẫn `d5d692f`.
