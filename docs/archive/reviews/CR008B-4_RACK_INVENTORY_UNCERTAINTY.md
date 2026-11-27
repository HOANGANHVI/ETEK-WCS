# CR-008B-4 – Rack & Inventory Uncertainty

**Ngày:** 2026-11-17 · **Branch:** `develop` · **Module bảo vệ được chạm (có phê duyệt trong CR-008B §7):** `InventoryService` (thêm 1 phương thức), `TaskOrchestrator.FinishAsync` (thêm 1 tham số). Không đổi quy tắc đặt chỗ, không đổi `Rack`/`Location`, không thêm trạng thái mới, không migration.

## 1. Mô hình (tận dụng trường có sẵn)

| Khái niệm | Biểu diễn | Thay đổi |
|---|---|---|
| Vị trí rack WCS ghi nhận | `Rack.CurrentLocationId` (+ `Status`) | Giữ nguyên khi rack thành `Unknown` (vẫn tính vào sức chứa) |
| Vị trí đã xác minh ngoài thực tế | Giao dịch tồn kho `Reconciliation` / `LastConfirmedAt` sau khi Admin đối soát | Không đổi |
| Reservation | `Reservation` | **Không bao giờ bị giải phóng** bởi thay đổi này |
| Rack không chắc chắn | `RackStatus.Unknown` | Được đặt cho các kết cục RCS chưa chứng minh được |
| Vị trí cần đối soát | `Location.ReconciliationRequired` | Được đặt cho vị trí nguồn và đích của task; `ReserveSource/Destination` vốn đã bỏ qua vị trí này |

## 2. Quy tắc mới – `IInventoryService.MarkPhysicalUnprovenAsync`
Khi RCS báo một kết cục mà WCS **không chứng minh được** về mặt vật lý (có hay không có Pick): nếu chưa xác nhận Drop → rack (nếu có) thành `Unknown` (giữ `CurrentLocationId`), vị trí nguồn và đích (nếu có) thành `ReconciliationRequired`; **không** khẳng định rack còn ở nguồn hay đã đến đích; không giải phóng reservation. Sau Drop đã xác nhận thì không làm gì.
Được gọi từ `FinishAsync(physicalUnproven: true)` cho: **RCS `Completed`** khi đang hủy (`CANCEL_RACE_RCS_COMPLETED`), **RCS `Interrupted`** khi đang hủy và tín hiệu `RcsInterrupted` (`CANCEL_RACE_RCS_INTERRUPTED` / interrupted), **RCS không còn bản ghi sau khi đã nhận** (`RCS_NOTFOUND_AFTER_ACCEPT`).

## 3. Các tình huống (yêu cầu §7.2)

| # | Tình huống | Kết quả sau thay đổi | Test |
|---|---|---|---|
| 1 | `Interrupted` trước Pick | Task `Failed` + đối soát; rack `Unknown`; nguồn flagged; reservation giữ | `Rcs_interrupted_before_the_pick_*` |
| 2 | `Interrupted` sau Pick | Rack `Unknown`; nguồn **và đích** flagged (đích: `HasFreeSlot=false`); reservation giữ | `Rcs_interrupted_after_the_pick_*` |
| 3 | `Completed` nhưng thiếu xác nhận Drop (đã Pick) | như (2) | `Rcs_completed_after_a_confirmed_pick_*` |
| 4 | RCS `Completed` nhưng WCS chưa ghi nhận Pick | Rack `Unknown` (không khẳng định còn ở nguồn), nguồn flagged, reservation giữ | `Rcs_completed_during_cancel_without_a_wcs_pick_*` |
| 5 | Restart giữa Pick và Drop | Không đổi: rack `InTransit` và task nằm trong PostgreSQL; không có hành động tự động nào ở đây | (CR-008B-3: restart giữ `Failed`/cờ) |
| 6 | PLC/RCS mất kết nối khi đang chuyển rack | Không đổi: task ở trạng thái hiện tại, retry có giới hạn; không timeout nghiệp vụ (chưa duyệt). Kết cục chỉ được đánh dấu khi RCS báo (hoặc hết giới hạn hủy ⇒ D-1) | – |
| – | RCS quên task đã nhận | `Failed` `RCS_NOTFOUND_AFTER_ACCEPT`; rack `Unknown`; nguồn flagged | `Rcs_forgetting_an_accepted_task_*` |

## 4. Bất biến được kiểm chứng
- **Không cấp cho task mới:** vị trí flagged và rack `Unknown` không được cấp; rack thứ hai cùng vị trí (thực tế bình thường) cũng không được cấp vì cả **vị trí** cần đối soát (`An_uncertain_rack_and_location_are_not_allocated_to_a_new_task`); đích flagged ⇒ `HasFreeSlot=false`.
- **Không có hai vị trí hiện tại cho một rack / không mất rack:** số rack không đổi sau đối soát; rack có đúng một `CurrentLocationId` (chuyển sang F1 khi Admin xác nhận), A1 hết rack (`Reconciliation_places_the_rack_once_*`).
- **Reservation không bị giải phóng nhầm:** mọi test kết cục chưa chứng minh vẫn còn reservation `Active`; chỉ `ReconcileTaskAsync` do Admin gọi giải phóng.
- **Không ảnh hưởng:** hủy được RCS xác nhận (`Cancelled`) và hoàn thành bình thường **không** đánh dấu gì (`A_cancel_confirmed_by_rcs_and_a_normal_completion_*`); hết giới hạn hủy giữ nguyên trạng thái rack/vị trí đúng chữ D-1 ("giữ reservation và trạng thái tài nguyên"): rack vẫn `Full`, vị trí không flag, reservation giữ (`The_cancel_retry_limit_keeps_*`); A3/A4, D1/D2, F5 không có luồng liên quan.

## 5. Tác động tới `InventoryService` (báo cáo riêng theo yêu cầu)
- Thêm `MarkPhysicalUnprovenAsync`; `MarkUncertainAsync` và mọi quy tắc đặt chỗ/pick/drop/đối soát **không đổi**.
- Trường hợp khác `Interrupted/Completed/NotFound-sau-accept` (hủy xác nhận, từ chối RCS, dispatch uncertain, hết giới hạn hủy) vẫn dùng đường cũ.
- Hệ quả vận hành: sau các kết cục trên, **cả vị trí** bị chặn đến khi Admin đối soát (bao gồm trạm yêu cầu như A1); vì vậy khối lượng đối soát có thể tăng – xem rủi ro.

## 6. Test
`Cr008b4InventoryUncertaintyTests` (9) đạt; toàn bộ backend **491/491** (DB tạm, không dùng `wcs_db`).

## 7. Chưa xác minh / rủi ro / quyết định còn mở
- **D-5/D-6 chưa được chủ dự án chọn rõ.** Tôi áp dụng đúng chỉ dẫn §7 của CR-008B: đánh dấu `Unknown`/`ReconciliationRequired` bằng trường có sẵn cho các kết cục RCS-báo-không-chắc-chắn (phạm vi (a) hẹp nhất). Nếu chủ dự án muốn M1 (chỉ vị trí), hay mở rộng sang mọi task đã được RCS nhận, cần quyết định thêm.
- **Chưa phủ:** tín hiệu `RcsEnded` (task chưa terminal, đã có `RequiresReconciliation`); `RCS_DISPATCH_UNCERTAIN`; timeout. Hai nhánh đầu giữ hành vi cũ (rack không bị đánh dấu), ghi nhận là khoảng trống.
- Rủi ro vận hành: chặn cả vị trí có thể làm trạm yêu cầu (A1/A2) không nhận task mới cho tới khi Admin đối soát (UI đã có từ B-3).
- RCS/PLC thật: NOT VERIFIED.
