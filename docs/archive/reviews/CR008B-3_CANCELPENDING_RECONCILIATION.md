# CR-008B-3 – CancelPending Reconciliation

**Ngày:** 2026-11-17 · **Branch:** `develop` · **Quyết định áp dụng:** D-1 (đã chốt), D-9 (chưa duyệt ⇒ không coi NotFound là Cancelled).

## 1. Hành vi sau thay đổi (FACT, có test)

| Tình huống | Hành vi |
|---|---|
| `CancelPending` đạt `RcsMaxAttempts` (30) lần thử tự động | Dừng retry; task → **`Failed`** (một lần, cạnh `CancelPending → Failed` đã có sẵn); `RequiresReconciliation = true`; lý do `CANCEL_RETRY_LIMIT_REACHED`; ghi `RCS_CANCEL_RETRY_EXHAUSTED` kèm số lần thử, thời điểm lần thử cuối, thời điểm đạt giới hạn và câu "không phải xác nhận RCS đã hủy"; reservation, trạng thái rack và vị trí **giữ nguyên**; không gửi Cancel/query nữa; **không** reset hay tăng bộ đếm |
| RCS báo `NotFound`, task chưa từng có mã RCS | **Không** chuyển `Cancelled` (kể cả nhiều lần). Ghi một dòng `RCS_CANCEL_NOTFOUND_NOT_CONFIRMED`, tiếp tục theo dõi; chỉ giới hạn retry mới kết thúc task (là `Failed`) |
| RCS báo `NotFound`, task đã có mã RCS | (không đổi từ CR-008A) `Failed` `RCS_NOTFOUND_AFTER_ACCEPT` + đối soát, giữ tài nguyên |
| Bộ đếm / `RcsLastAttemptAt` | Task kết thúc **giữ** giá trị cuối (để Admin xem); mọi trạng thái không kết thúc vẫn bắt đầu chuỗi mới |
| Restart | Trạng thái `Failed`, cờ đối soát, lịch sử, bộ đếm nằm trong PostgreSQL; process mới không gửi gì thêm (test) |

Thay đổi so với CR-008A: (1) nhánh hết giới hạn cancel trước đây để task ở `CancelPending` (không còn đường thoát – REV-001) ⇒ nay `Failed` theo D-1; (2) nhánh "NotFound hai lần ⇒ Cancelled" bị **gỡ** (D-9; spec §3.9).

Phạm vi KHÔNG đổi: nhánh Continue (hết giới hạn vẫn giữ `Continuing` + cờ đối soát – D-1 chỉ nói về CancelPending; Admin vẫn hủy được), `TaskStateMachine`, `TaskScheduler`, `InventoryService`, PER_AGV, `StateTimeoutsSeconds`.

## 2. Chức năng Admin đối soát

### API (tất cả chỉ Admin; Operator 403, chưa đăng nhập 401 – có test)
| Endpoint | Chức năng |
|---|---|
| `GET /api/tasks/reconciliation` | Danh sách task `RequiresReconciliation` (mới nhất trước) |
| `GET /api/tasks/{id}/reconciliation` | Chi tiết: task, `RcsRequestCode`/`RcsTaskCode`, số lần thử + lần cuối, sự kiện RCS/hủy/đối soát (30 gần nhất), reservation đang giữ (loại, vị trí, rack), rack (trạng thái, vị trí WCS ghi nhận, lần xác nhận cuối), cờ `ReconciliationRequired` của vị trí, `canReconcile` |
| `POST /api/tasks/{id}/reconciliation-check` `{note}` | **Ghi nhận "chưa xác định được"**: chỉ thêm `RECONCILIATION_CHECK_UNRESOLVED` (người + ghi chú) và audit `TASK_RECONCILE_CHECK_UNRESOLVED`; **không giải phóng gì**. Ghi chú bắt buộc (≥ 3 ký tự). Chỉ khi task đã kết thúc và còn cần đối soát |
| `POST /api/tasks/{id}/reconcile` (đã có) | Admin nêu **vị trí thực tế**: logic `InventoryService.ReconcileTaskAsync` (không đổi) đặt rack, kiểm sức chứa/loại rack, giải phóng giữ chỗ, xoá cờ, ghi giao dịch tồn kho; audit `TASK_RECONCILE` |

Không có "Force Complete" / "Force Cancel". Nếu Admin chưa xác định được, hệ thống tiếp tục giữ mọi tài nguyên.

### UI (`ReconciliationPanel`, trong chi tiết nhiệm vụ của Admin; ẩn ở Demo Mode)
Hiển thị toàn bộ dữ liệu trên, cảnh báo "Thất bại ≠ RCS đã hủy", hai lựa chọn (mặc định **chưa xác định – giữ tài nguyên**; hoặc **đã xác minh** – phải chọn vị trí thực tế + tích xác nhận đã kiểm tra hiện trường), ghi chú bắt buộc. Bộ lọc "Cần đối soát" trên danh sách nhiệm vụ. Thông điệp tiếng Việt cho các mã mới.

## 3. Phân tích: retry Cancel sau đối soát (không triển khai – chờ quyết định)

Không có thao tác nào reset bộ đếm hay tái sử dụng `RcsRequestCode`. Task `Failed` là terminal; không có cạnh quay lại. Nếu chủ dự án muốn "thử hủy lại", phải là thao tác tường minh có idempotency + audit, và theo từng kết quả Admin kiểm tra:

| Kết quả kiểm tra | Hành động an toàn hiện có | Nếu muốn thêm (cần phê duyệt) |
|---|---|---|
| RCS đã hủy (Admin thấy trên RCS) | `reconcile` với vị trí thực tế (rack thường vẫn ở nguồn); giải phóng giữ chỗ | Ghi nhận nguồn xác nhận = RCS trong ghi chú; không cần Cancel |
| RCS vẫn đang thực thi | **Không** reconcile (rack/AGV đang bị RCS điều khiển); dùng "chưa xác định" | Một thao tác "gửi lại Cancel" mới cho `RcsRequestCode` đó, idempotent, audit, tối đa N lần mỗi lần Admin bấm; task terminal nên cần thiết kế riêng (không đổi `TaskStateMachine` trừ khi được duyệt) |
| RCS không còn task | `reconcile` sau khi xác minh vị trí vật lý | Ngữ nghĩa NotFound phải được nhà cung cấp xác nhận (REV-005) |
| Chưa xác định được | `reconciliation-check` (giữ nguyên) | – |
| Không truy vấn được RCS | như "chưa xác định" | – |

## 4. Test thực tế
`Cr008b3CancelReconciliationTests` (7 test) + `Cr008aReliabilityTests` điều chỉnh: **32/32 đạt**. Phủ: đạt giới hạn ⇒ `Failed` đúng một lần (một dòng STATE_CHANGED→Failed, một `RCS_CANCEL_RETRY_EXHAUSTED`, không `Cancelled`); reservation/rack giữ; không gọi Cancel/query sau giới hạn dù RCS hoạt động lại; bộ đếm và lịch sử không đổi; **restart** giữ `Failed` + cờ + không gửi gì; Admin thấy task trong danh sách và chi tiết (số lần thử, reservation, rack, sự kiện); Operator 403 trên cả bốn endpoint, anonymous 401, dữ liệu không đổi; "chưa xác định" được ghi lịch sử + audit và **không** giải phóng; ghi chú rỗng ⇒ 400; `reconcile` đã xác minh giải phóng đúng reservation, tạo giao dịch tồn kho + audit; task không cần đối soát ⇒ 422; NotFound không bao giờ thành `Cancelled`.
Frontend: `eslint` 0 lỗi, `tsc -b` 0 lỗi (xem báo cáo cuối cho `npm run build`).

## 5. Chưa xác minh / còn mở
- **AGV sau `Failed` (D-7):** vì `Failed` là terminal, AGV không còn bị chiếm và scheduler có thể giao task Queued tiếp theo – giống mọi kết cục không chắc chắn hiện có. D-1 đòi `Failed`, nên phần này **không thể** tránh nếu không có chính sách giữ AGV; xem `CR008B-5_AGV_HOLD_ANALYSIS.md`. Không mở rộng hơn mức hiện có.
- Giao diện chưa kiểm tra bằng trình duyệt thật (chỉ eslint/tsc/build); RCS/PLC thật: NOT VERIFIED.
- Ngữ nghĩa các trạng thái RCS thật và NotFound: cần nhà cung cấp (REV-005).
