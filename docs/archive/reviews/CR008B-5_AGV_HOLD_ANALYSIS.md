# CR-008B-5 – AGV Reconciliation Hold (PHÂN TÍCH – KHÔNG TRIỂN KHAI)

**Ngày:** 2026-11-17 · **Trạng thái:** DỪNG TRƯỚC KHI TRIỂN KHAI – chờ quyết định **D-7**. Không đổi mã, không migration, không cờ giữ AGV, không đổi PER_AGV, không tắt AGV, không đổi `TaskScheduler`/`TaskOrchestrator` trong sub-CR này.

## 1. Hành vi hiện tại (FACT)
- AGV "bận" chỉ khi có task ở trạng thái chiếm AGV (`AgvOccupyingStates`; index DB `ux_transport_tasks_one_active_per_agv`). `FinishAsync` đưa task về trạng thái kết thúc rồi gọi `TryDispatchNextAsync(agvId)`; `TaskScheduler` cũng gọi nó mỗi tick. Không có điều kiện nào kiểm tra "task cũ của AGV còn cần đối soát".
- Vì vậy mọi task kết thúc kiểu "chưa chắc chắn" – hiện gồm `Interrupted`, `Completed` khi đang hủy, `RCS_NOTFOUND_AFTER_ACCEPT`, `RCS_DISPATCH_UNCERTAIN`, **và (theo D-1) hết giới hạn hủy** – đều **trả AGV** cho scheduler ngay; task `Queued` kế tiếp được gán.
- Mẫu này **đã có trước CR-008B** (tín hiệu `RcsInterrupted`, dispatch uncertain). CR-008B-3 (D-1 bắt buộc `Failed`) thêm một đường dẫn tới cùng kết cục; CR-008B không mở rộng quyền scheduler hơn mức đó. **Không** có thay đổi nào ở B-5.

## 2. `RequiresReconciliation` có đủ để scheduler nhận biết AGV không được điều phối không?
- **Dữ liệu thì đủ để suy ra, nhưng điều kiện thì không đủ chính xác.** `TransportTask.RequiresReconciliation && !ReconciliationCompleted` cho biết task cần đối soát. Nhưng cờ này cũng được đặt cho **mọi** hủy có xác nhận RCS (`Cancelled` + đối soát theo `inventory-management.md` 4.4) và mọi `Failed` sau khi RCS đã nhận task. Nếu dùng cờ này để giữ AGV, **mọi lần hủy thường** cũng giữ AGV đến khi Admin đối soát ⇒ ảnh hưởng vận hành lớn.
- Cần một **phân loại kết cục** (xem bảng 3), lưu bằng một trong: (a) quy ước `StateReason` (dễ vỡ, chuỗi tự do); (b) cột/enum "lý do không chắc chắn" trên task (migration); (c) cờ giữ trên AGV (migration).
- Muốn scheduler tôn trọng: phải đổi `TaskOrchestrator.TryDispatchNextAsync` và `ProcessTaskAsync` (module bảo vệ) và/hoặc repository ⇒ **cần phê duyệt trước theo yêu cầu CR-008B §8**.

## 3. Bảng điều kiện theo kết cục (đề xuất để chủ dự án chọn; chưa phải chính sách)

| Kết cục RCS | Hiện nay (FACT) | Rack/vị trí (sau B-4) | Phương án giữ AGV đề xuất | Lý do |
|---|---|---|---|---|
| `Cancelled` (RCS xác nhận) | trả AGV; task `Cancelled` + đối soát | không đánh dấu (giữ bằng reservation) | **Trả AGV** (i) | RCS đã xác nhận ngừng; rack ở nguồn |
| `Completed` (khi đang hủy) | trả AGV; `Failed` + đối soát | rack `Unknown`, vị trí flagged | **Giữ chờ Admin đối soát** (ii) *hoặc* trả (i) | RCS nói robot đã xong; vị trí rack chưa được WCS xác nhận |
| `Interrupted` | trả AGV; `Failed` + đối soát | rack `Unknown`, vị trí flagged | **Giữ chờ xác minh vị trí vật lý** (iii) | AGV có thể dừng giữa đường, có thể đang mang rack |
| `NotFound` sau khi RCS đã nhận | trả AGV; `Failed` + đối soát | rack `Unknown`, vị trí flagged | **Giữ chờ xác minh** (iii) | RCS mất bản ghi: không biết robot đang làm gì |
| `Unknown` / không truy vấn được | task vẫn `CancelPending`, AGV vẫn bị chiếm | không đổi | Giữ nguyên (đang chiếm) | chưa kết thúc |
| Hết giới hạn hủy (D-1) | trả AGV (task `Failed`) | giữ nguyên (D-1) | **Giữ chờ xác minh** (iii) | chưa có xác nhận hủy; robot có thể còn chạy |
| `RCS_DISPATCH_UNCERTAIN` | trả AGV | không đánh dấu (chưa pick) | cần quyết định | RCS có thể đã nhận task |

Nhóm: (i) trả ngay; (ii) giữ chờ Admin đối soát; (iii) giữ chờ xác minh vị trí vật lý; (iv) trả nhưng hạn chế điều phối (chỉ task không dùng vị trí liên quan).

## 4. Phương án kỹ thuật (nếu D-7 chọn giữ AGV) – KHÔNG triển khai
| PA | Mô tả | Ưu | Nhược / rủi ro |
|---|---|---|---|
| P2 | Cột `DispatchHold` (+ lý do, thời điểm) trên `agvs`; đặt khi kết thúc thuộc nhóm (ii)/(iii); `TryDispatchNextAsync`/`ProcessTaskAsync` bỏ qua AGV bị giữ; Admin gỡ (hoặc tự gỡ khi đối soát xong) | Rõ ràng, độc lập từng AGV | Migration, API + UI AGV, quyền, test; khái niệm mới cần phê duyệt |
| P3 | Suy ra từ task: tồn tại task terminal của AGV có lý do thuộc nhóm "không chắc chắn" và chưa `ReconciliationCompleted` | Không thêm bảng AGV; tự gỡ khi đối soát | Cần cột/enum lý do trên task (migration) hoặc quy ước `StateReason` (dễ vỡ) |
| P4 | Tự đặt `Agv.Enabled=false` | Không migration | Trộn cấu hình với trạng thái vận hành; chặn cả `CreateTask`; khó phân biệt với Admin chủ ý tắt – **không khuyến nghị** |
| P1 | Giữ nguyên | Không rủi ro triển khai | AGV có thể nhận task mới khi vị trí vật lý chưa rõ |

PER_AGV và AGV1/AGV2 song song **không đổi** ở mọi phương án: giữ ≠ có task hoạt động; chỉ AGV bị giữ ngừng nhận task, AGV kia chạy bình thường.

## 5. Test case cần có nếu được duyệt (chưa viết)
Giữ AGV sau từng kết cục thuộc nhóm (ii)/(iii); task `Queued` của AGV bị giữ **không** được gán, task của AGV kia vẫn chạy; gỡ giữ bởi Admin (Operator bị từ chối); tự gỡ khi `reconcile` hoàn tất (nếu chọn); giữ nguyên sau restart; `Cancelled` được RCS xác nhận **không** giữ; PER_AGV (một task hoạt động/AGV) và AGV1+AGV2 song song vẫn đúng; tạo task mới cho AGV bị giữ vào `Queued` (hoặc bị từ chối – cần quyết định); audit mọi thao tác gỡ.

## 6. Quyết định cần chủ dự án (D-7)
1. Kết cục nào thuộc nhóm (i)/(ii)/(iii)/(iv) (bảng 3)?
2. Phương án kỹ thuật: P1 / P2 / P3 (P4 không khuyến nghị)?
3. Ai gỡ giữ AGV: Admin thủ công / tự gỡ khi đối soát xong?
4. Tạo task mới cho AGV đang bị giữ: cho vào hàng đợi hay từ chối?
5. `RCS_DISPATCH_UNCERTAIN` có thuộc nhóm giữ không?

**Trong lúc chưa có D-7:** hành vi hiện tại giữ nguyên (không mở rộng quyền scheduler); rủi ro được ghi nhận ở báo cáo cuối.
