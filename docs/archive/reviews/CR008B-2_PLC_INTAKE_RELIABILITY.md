# CR-008B-2 – PLC Station Intake Reliability

**Ngày:** 2026-11-17 · **Branch:** `develop` · **Phạm vi:** `StationRequestProcessor`, bảng `station_requests`, test. **Không đổi:** handshake PLC, địa chỉ PLC, `Request_Ack`, MC Protocol, polling PLC, `PlcStationSignalService`, `TaskOrchestrator`.

## 1. Luồng hiện tại (trước B-2) – FACT

```
PLC bit Pickup_Request ──(poll PLC)──> PlcStationSignalService.PollRequestsAsync
        │ rising edge only; _high[key]=high ghi NGAY, kể cả khi xử lý sau đó thất bại
        ▼
StationRequestProcessor.RunOnceAsync (mỗi tick scheduler)
        ├─ CreateTaskAsync ── OK ──────────► AcknowledgeRequestAsync (chỉ ghi log; KHÔNG ghi Request_Ack)
        ├─ AppException (kể cả RCS_NOT_CONNECTED) ► ack ► yêu cầu coi như đã xử lý, KHÔNG có task
        └─ Exception khác ─► log, không ack ► yêu cầu KHÔNG được trình lại (bit vẫn cao ⇒ không còn cạnh lên)
```
Hệ quả: một yêu cầu bị từ chối vì `RCS_NOT_CONNECTED` hoặc lỗi kỹ thuật **bị mất**, dù có ack hay không. Hàng đợi `StationSignalSimulator` cũng bị rút cạn khi poll. Sau restart `_high` rỗng nên bit còn cao sẽ tạo lại cạnh lên (cùng `Request_ID` ⇒ cùng khoá ⇒ idempotent), nhưng không có gì giữ yêu cầu **trong khi** WCS đang chạy.

## 2. Luồng đề xuất / đã triển khai

```
PollRequestsAsync ──► [1] GHI BỀN: INSERT station_requests (RequestKey UNIQUE) status=Pending
                         (trùng khoá ⇒ bỏ qua; đã Accepted/Rejected ⇒ ack lại, idempotent)
                      [2] nếu IRcsDispatchGuard.Check() = chặn  ──► để nguyên Pending (không tốn lần thử, không ghi DB)
                          ngược lại, với từng Pending đến hạn (backoff RcsRetry*):
                              CreateTaskAsync (idempotent theo RequestKey)
                                 ├─ OK / trả task cũ ──► [commit task] ──► UPDATE record=Accepted(TaskId) ──► ack
                                 ├─ AppException RCS_NOT_CONNECTED ──► giữ Pending (không tốn lần thử)
                                 ├─ AppException khác (từ chối nghiệp vụ) ──► Rejected + ack (như trước)
                                 └─ lỗi kỹ thuật ──► Attempts++ , LastError , backoff; đủ RcsMaxAttempts ⇒ Failed (chờ người vận hành)
```
- **Ranh giới transaction:** (a) ghi yêu cầu = 1 transaction; (b) `CreateTaskAsync` = transaction riêng của orchestrator; (c) đánh dấu Accepted = transaction riêng **sau** khi task đã commit. Sập giữa (b) và (c) chỉ khiến lần sau gọi lại `CreateTaskAsync` – idempotent, trả task cũ – rồi mới đánh dấu Accepted (có test).
- **Bền qua restart:** trạng thái nằm trong PostgreSQL; sau restart không cần cạnh lên mới.
- **Giữ nguyên:** `PollRequestsAsync` của `PlcStationSignalService` (đã đúng: phát hiện cạnh); `AcknowledgeRequestAsync` vẫn chỉ ghi log (chưa có quy tắc handshake được duyệt).

## 3. Thiết kế dữ liệu và migration
Bảng mới `station_requests` (`Id`, `RequestKey` UNIQUE ≤300, `StationCode`, `Flow`, `Status` Pending/Accepted/Rejected/Failed, `Attempts`, `FirstSeenAt`, `LastAttemptAt`, `DecidedAt`, `TaskId`, `LastError`, `xmin`) + index `(Status, FirstSeenAt)`.
Migration **`AddStationRequests`**: chỉ `CREATE TABLE` và hai `CREATE INDEX`; `Down` chỉ `DROP TABLE station_requests`. Additive, không mất dữ liệu; **chưa áp dụng lên `wcs_db`**. Không có chính sách dọn bản ghi cũ (ghi nhận là việc sau).

## 4. Phân loại lỗi (quyết định D-3 chưa được chốt đầy đủ)
Chỉ hai nhóm được tách: **tạm thời** = `RCS_NOT_CONNECTED` (giữ chờ, không tốn lần thử) và lỗi kỹ thuật không phải `AppException` (thử lại có backoff, có trần); **mọi `AppException` còn lại giữ hành vi cũ** (từ chối + ack), kể cả `AGV_DISABLED`, `NO_SOURCE_*`, `STATION_NOT_ALLOWED`… Danh sách chi tiết vẫn chờ chủ dự án.

## 5. Kết quả test thực tế

| Bộ | Kết quả |
|---|---|
| `Cr008b2PlcIntakeTests` (mới, 12 test) + `Cr008aReliabilityTests` (đã điều chỉnh) | **36/36 đạt** |
| Toàn bộ backend (DB tạm `wcs_test_<guid>`, không dùng `wcs_db`) | **474/474 đạt** |

Test B-2: yêu cầu mới được lưu và chỉ Accepted sau khi task tồn tại (TaskId khớp); RCS chưa kết nối ⇒ giữ yêu cầu, `Attempts=0`, không task, không ack, 0 cuộc gọi RCS, 5 pass cách nhau 5 phút không đổi; RCS kết nối ⇒ đúng một task; Production + Simulator ⇒ yêu cầu giữ Pending, 0 `CreateCalls`, không reservation; **restart** khi đang Pending ⇒ vẫn xử lý được; restart sau Accepted + cạnh lên lại cùng `Request_ID` ⇒ không task/record thứ hai; cùng `Request_ID` nhiều lần ⇒ 1 record + 1 task; hai trạm đồng thời ⇒ 2 task (PER_AGV: 1 active); sập giữa tạo task và đánh dấu Accepted ⇒ không trùng; lỗi kỹ thuật một yêu cầu ⇒ các yêu cầu khác vẫn xử lý, yêu cầu lỗi Pending + backoff rồi Accepted khi hết lỗi; vượt hạn mức ⇒ `Failed`, không task; từ chối nghiệp vụ (BP-04) ⇒ Rejected, ack một lần, không thử lại; `RcsRetryPolicy` cho đúng 2,4,8,16,32,60 / 62 s / 1 502 s (**xác nhận số liệu của B-1 bằng mã thật**).
Test hosted service: scheduler vẫn chạy khi bộ xử lý intake ném lỗi (kho yêu cầu lỗi mọi tick).

Test cũ thay đổi: `One_failing_intake_request...` (CR-008A) được thay bằng các test B-2 ở trên (hành vi đã chuyển sang kho bền vững); các test còn lại giữ nguyên. Hành vi trùng khoá trong cùng một lần poll (`handled=2`, 2 ack) được giữ.

## 6. Không đổi / ngoài phạm vi
Không đổi địa chỉ/bit/handshake/MC; không bật ghi PLC; không đổi `StateTimeoutsSeconds`, thông số retry, PER_AGV, `TaskStateMachine`, `InventoryService`, `TaskScheduler`.

## 7. Chưa xác minh / rủi ro còn lại
- **D-11 (NEEDS_CONFIRMATION):** `Request_ID` có duy nhất theo thời gian không? Nếu PLC tái sử dụng ID, yêu cầu mới bị coi là trùng và không tạo task (im lặng). Hành vi PLC khi không nhận `Request_Ack` cũng chưa rõ.
- Khoảng hở rất ngắn giữa lúc dịch vụ tín hiệu ghi `_high` và lúc yêu cầu được lưu (sập đúng lúc đó): sau restart bit cao tạo lại cạnh ⇒ không mất (lập luận; chưa có test sập thật).
- Không có UI xem `station_requests` (Pending/Failed) và không có dọn dẹp bản ghi cũ.
- PLC thật: NOT VERIFIED.
