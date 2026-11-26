# CR-042 – Triển khai các quyết định sau INITIAL REVIEW

| | |
|---|---|
| **Trạng thái** | `MERGED – NOT DEPLOYED` (chủ dự án cho phép đưa vào `main` ngày 2026-11-26). Đã kiểm thử trên Simulator / mock. Chưa đóng: còn F1, F2 và các OQ ở §8 |
| **Commit** | `3811180` (nhánh `feature/cr-042-approved-decisions`) |
| **Nguồn phê duyệt** | Chủ dự án, 2026-11-26, trả lời [INITIAL REVIEW](../reviews/INITIAL_REVIEW_2026-11-26.md) |
| **Migration** | `AddVersionWithdrawnByAdmin`, `AddOrderRacksHeld` ([up](CR-042_migration_up.sql), [down](CR-042_migration_down.sql)). Chỉ mới chạy trên database test |
| **Review** | Chủ dự án, 2026-11-26: HOLD trong lúc xác minh, sau đó cho phép commit, merge và push |

## 1. Quyết định và cách hiện thực

| Quyết định | Hành vi | Trạng thái |
|---|---|---|
| OQ-42-01 (b) | Khi có version Published: không đổi loại yêu cầu, loại rack, vị trí kích hoạt, cờ REVIEW (`PROCESS_HEADER_LOCKED`). Tên, mô tả, Ngừng sử dụng vẫn sửa được. Tạo nháp theo danh mục: chỉ khi header không đổi, và không sửa gì trên quy trình; ngược lại bị từ chối. Import cấu hình đổi trigger của quy trình Published: Preview báo xung đột `PROCESS_HEADER_LOCKED` (loại trừ hoặc bỏ qua được). **Thay đổi hành vi của CR-026** (trước đó cho phép sau khi xác nhận) – hai test CR-026 đã cập nhật | Xong – cần chủ dự án xác nhận phần import |
| OQ-42-02 (a) | Quy trình danh mục `defaultPublish=false` (PH2-05) không Publish và không đặt Ready (`PROCESS_NOT_APPROVED_FOR_PUBLICATION`). Version đã Publish từ trước giữ nguyên | Xong |
| OQ-45 (a) | Admin Disable → yêu cầu trong Queue chưa điều phối bị Held, kể cả khi có version khác Published. Bị thay bởi Publish mới → vẫn chạy (D-10). Có thể Disable lại version đã bị thay để rút nó | Xong |
| OQ-58 (a), OQ-57, OQ-49 (a) | Giữ hành vi hiện tại; đặc tả cập nhật cho OQ-49 | Đã đạt từ CR-040 |
| OQ-42-04 (b), N = 10 | Yêu cầu chờ ≥ 10 phút ở đầu Scope (và nhóm chưa có Scope) được cảnh báo trên ba trang, ghi một sự kiện `QUEUE_STALLED` mỗi đợt chờ. Không tự vượt, không hủy. `RouteEngine:QueueStallWarningMinutes` (0 = tắt) | Xong |
| OQ-42-07 (a) | Giảm tồn không xóa rack của lệnh vận chuyển chưa kết thúc (`RACK_IN_USE_BY_ORDER`); rack tự do vẫn giảm được. Quyền không đổi (CR-001) | Xong |
| OQ-42-03 (b) | Mỗi trạm một engine, xem §2 | Xong phần định tuyến; đường PLC thật sang engine lệnh **BLOCKED** (OQ-41-02) |
| OQ-54 (a) | **Chưa đưa vào hệ thống**, xem §3 | **BLOCKED – cần xác nhận lại** |
| PE-3 | `LEG_DISPATCH_PENDING` chỉ ghi khi lý do mới hoặc đổi | Xong |
| RK-1 | Hộp thoại rack: chọn product model (khi chưa có), báo khi không kiểm tra được khóa | Xong |
| RK-3 / OQ-42-08 | Quyền sở hữu rack của lệnh chưa hoàn thành, xem §5 | **FIXED** (yêu cầu của chủ dự án, 2026-11-26) |

## 2. Một tín hiệu – một engine

Cấu hình `Plc:Integration:StationEngines` (mặc định rỗng):

```json
"Plc": { "Integration": { "StationEngines": [ { "Station": "B1", "Engine": "TransportOrder" } ] } }
```

| Giá trị của trạm | PLC thật | Mô phỏng | `plc-requests` / engine lệnh |
|---|---|---|---|
| Không khai báo, không có ánh xạ cũ | Không tạo gì | Engine lệnh | Nhận |
| Không khai báo, tích hợp bật và có ánh xạ cũ (= `Task`) | Task (như trước) | Task | Từ chối |
| `Task` | Task nếu tích hợp bật và có ánh xạ | Task, cùng ánh xạ và cùng khóa `PLC:Trạm:Request_ID` | Từ chối |
| `TransportOrder` | **Không tạo gì** (chờ OQ-41-02) | Engine lệnh | Nhận |
| Mâu thuẫn hoặc giá trị lạ | Không | Từ chối | Từ chối |

Chống trùng khi chuyển đổi: Request_ID đã được engine này nhận thì engine kia từ chối (`REQUEST_TAKEN_BY_OTHER_ENGINE`).
Đường mô phỏng vào engine task tạo task trực tiếp (không qua bảng `station_requests`), cùng `TaskOrchestrator`; ở Real không tạo gì.

**Giới hạn:** quy tắc này áp dụng cho **tín hiệu PLC**. Form tạo yêu cầu thủ công vẫn cho chọn cả luồng Phase 1 lẫn quy trình. Phân tích và đề xuất: [Engine theo trạm và form thủ công](../reviews/STATION_ENGINE_MANUAL_REQUEST_2026-11-26.md).

## 3. OQ-54 – phương án A có thể gây bế tắc

Phương án đã duyệt đặt `PH2-01` (A → B) và `PH2-03` (B → A) cùng scope `AB-CAT`. Khi B và buffer đầy: `PH2-01` cũ chờ chỗ ở B, `PH2-03` giải phóng B lại phải chờ sau nó. `XY-CAT` (07, 09) và `XZ-CAT` (11, 12) tương tự. Test `Opposite_directions_block_each_other_in_one_scope` tái hiện bằng quy trình thử và cho thấy tách scope thì chạy. Lập luận trong INITIAL REVIEW chỉ xét cặp cất/cấp, bỏ sót hai chiều cất.

**Đề xuất A2:** mỗi chiều cất một scope.

| Scope | Quy trình |
|---|---|
| `AB-CAT-A` | PH2-01 |
| `AB-CAT-B` | PH2-03 |
| `AB-CAP` | PH2-02, 04, 06 |
| `XY-CAT-X` | PH2-07 |
| `XY-CAT-Y` | PH2-09 |
| `XY-CAP` | PH2-08, 10 |
| `XZ-CAT-X` | PH2-11 |
| `XZ-CAT-Z` | PH2-12 |

Phân tích lại ngày 2026-11-26 ([OQ-54](../reviews/OQ-54_QUEUE_SCOPE_ANALYSIS_2026-11-26.md)): A2 **chưa đủ** để tránh bế tắc (F2, §7) và scope không ngăn xung đột lối đi.

Hệ quả: các chiều tranh chỗ buffer chung không có thứ tự giữa nhau. Nhóm cấp (`*-CAP`) cũng gồm hai chiều; chưa thấy vòng chờ tương tự nhưng **chưa chứng minh**. Hiện Admin có thể khai báo scope bằng tay; hệ thống không tự gán.

## 4. BP → PH2: mapping và thứ tự chuyển đổi (chờ xác nhận, **chưa thực hiện**)

Điều kiện, rủi ro mất tín hiệu PLC và rollback từng đợt: [kế hoạch chuyển đổi](../reviews/BP_PH2_CUTOVER_PLAN_2026-11-26.md).

| BP (engine task) | Tuyến BP | PH2 | Tuyến PH2 | Khác biệt cần chấp nhận |
|---|---|---|---|---|
| BP-01: A có rack đầy | → B1/B2 (không bind) → F1/F2 → F5 | PH2-01 | → B1/B2 → P1/P2 → M5 | Buffer F → P, F5 → M5; PH2 chưa khai báo không bind tại B (OQ-39-09) |
| BP-02: A cần rack rỗng | B1/B2 → F3/F4 | PH2-02 | B1/B2 → P1/P2 | Buffer F3/F4 → P1/P2 |
| BP-03: B có rack rỗng | → A1/A2 (không bind) → F3/F4 | PH2-03 | → A1/A2 → P1/P2 → M5 | Thêm tràn M5; không bind (OQ-39-09) |
| BP-04 (REVIEW, không chạy) | – | PH2-04 | A1/A2, P1/P2 → B | Chức năng mới |
| BP-05: F5 gọi rack đầy | F1/F2 → F5 | PH2-06 | P1/P2 → M5 | F5 ≠ M5: cần xác nhận tương đương |
| BP-06: F5 trả rack rỗng | F5 → A1/A2 → F3/F4 | **PH2-05 (chưa duyệt)** | M5 → A, P | Không có thay thế |

| Đợt | Nhóm | Việc | Điều kiện | Trạng thái |
|---|---|---|---|---|
| 0 | X/Y, X/Z | Chạy thử Simulator (không có BP để thay) | Scope (§3), liên kết port, duyệt mapping; OQ-39-06 | Chờ xác nhận |
| 1 | B1, B2 | Chạy PH2-03/04 qua form và mô phỏng; sau đó đặt `TransportOrder`, ngừng BP-03 | Như trên; **PLC thật cần OQ-41-02** | Chờ xác nhận |
| 2 | A1, A2 | PH2-01/02 thay BP-01/02 | Đợt 1 đạt | Chờ xác nhận |
| 3 | F5 / M5 | BP-05/06 | Quyết F5↔M5 và PH2-05 | BLOCKED |

**Lưu ý:** đặt trạm thành `TransportOrder` lúc này sẽ **ngừng tiếp nhận PLC thật** của trạm đó mà chưa có đường thay thế. Giữa đợt 1 và 2 hai engine cùng dùng vị trí A/B (reservation vẫn bảo vệ chỗ, nhưng không có FIFO giữa hai engine).
**Quay lui:** xóa mục `StationEngines`, khởi động lại; hoàn tất hoặc hủy yêu cầu đang mở trước khi đổi.

## 5. OQ-42-08 – Quyền sở hữu rack của Transport Order

**Lỗi:** giữa hai chặng, rack ở Buffer không được giữ chỗ; lệnh khác lấy được, lệnh đầu chuyển `ReconciliationRequired`.

**Quy tắc:** quyền sở hữu suy ra từ lệnh và chặng trong database (`RackOwnership`), nên còn nguyên sau khi khởi động lại.

| Trạng thái lệnh | Rack thuộc lệnh | Giải phóng khi |
|---|---|---|
| Created, Accepted, InProgress, Waiting (gồm chờ ở Buffer, chờ nguồn/đích, AGV giữ rack), ReconciliationRequired | Rack đã lấy và rack của chặng chưa kết thúc | Completed hoặc Cancelled |
| Held | Chưa có chặng nên chưa sở hữu rack | – |
| Failed, **đã** lấy rack | Vẫn giữ (`RacksHeld`) | Admin giải phóng, có lý do và audit (`POST /api/routing/orders/{id}/release-racks`); bị từ chối khi rack chưa có vị trí xác nhận |
| Failed, **chưa** lấy rack (ví dụ lệnh bị từ chối khi gửi) | Không giữ | Ngay khi lỗi (hành vi cũ) |
| Completed, Cancelled | Không | – |

| Đường lấy hoặc đổi rack | Hành vi với rack đã có chủ |
|---|---|
| Chọn nguồn và điều kiện `RackPresent`, `RackTypeMatch`, `RackBinding` của lệnh khác | Không nhìn thấy; chủ sở hữu vẫn thấy rack của mình |
| Chọn nguồn của engine Task Phase 1 | Không nhìn thấy; không còn rack khác thì `NO_SOURCE_AVAILABLE` như cấu hình hiện hành |
| Sửa, xóa rack; giảm tồn | Từ chối `RACK_IN_USE_BY_ORDER` |
| Đối chiếu tín hiệu PLC với tồn kho | Rack có chủ vẫn được tính là có mặt, không đánh dấu đối soát |

**Ảnh hưởng cần biết:** rack đã được lệnh nhiều chặng đặt xuống bị khóa tới khi cả lệnh kết thúc, kể cả khi đứng tại trạm. Các quy trình trong danh mục chỉ có một chặng nên rack được trả ngay khi đặt xong. Lệnh lỗi tồn tại từ trước migration không giữ rack.

**Quyết định cần xác nhận:** dòng "Failed, chưa lấy rack" (không giữ).

## 6. Triển khai và rollback

Sao lưu → chạy `AddSimulatedStationTransaction` (CR-041, nếu chưa), `AddVersionWithdrawnByAdmin`, `AddOrderRacksHeld` → khởi động backend mới. Rollback `AddOrderRacksHeld` bị từ chối khi còn lệnh lỗi giữ rack. Rollback bị từ chối khi còn yêu cầu trong Queue ghim version đã bị Admin Disable. Version Disable trước CR-042 được coi là "bị thay thế".

## 7. Phát hiện mới khi review (đã tái hiện bằng test, **chưa sửa**)

| Mã | Phát hiện | Mức | Đề xuất |
|---|---|---|---|
| F1 | Engine Task không kiểm tra chặng đang chạy: AGV đang chạy chặng vẫn được giao Task mới, vi phạm `PER_AGV` | Nghiêm trọng | Duyệt sửa ngay (OQ-54c) |
| F2 | Chặng giữ chỗ đích khi chưa có AGV: bế tắc với rack mà AGV đó đang giữ, kể cả khi tách scope | Cao | OQ-54 phương án P-1 |

Test `Cr042OpenFindingsTests` (2/2 đạt, ba lần chạy liên tiếp) mô tả hành vi hiện tại; khi sửa, đảo lại. Test đạt nghĩa là **lỗi đã được tái hiện**.

| Mã | Test chứng minh | Phạm vi chứng minh |
|---|---|---|
| F1 | Chặng `InProgress` trên một AGV; Task Phase 1 tạo sau đó nhận cùng AGV, không vào hàng đợi, ở trạng thái chiếm AGV; chặng vẫn đang chạy | Dispatch Legacy, RCS giả lập |
| F2 | Ba lệnh ở ba scope: lệnh giữ rack không có đích, lệnh giữ chỗ đích báo `AGV_BUSY`; không tự thoát qua nhiều lượt; hủy lệnh giữ rack bị từ chối; hủy lệnh giữ chỗ thì lệnh kia đi tiếp | Dispatch Legacy, đích thay thế không có |

## 8. Trạng thái từng OQ

| OQ / hạng mục | Trạng thái | Ghi chú |
|---|---|---|
| OQ-42-01 | FIXED | Thay đổi import CR-026 chờ xác nhận |
| OQ-42-02 | PASS | |
| OQ-42-03 | PASS một phần | Thao tác tay chưa theo engine (đề xuất, MR-1…4) |
| OQ-42-04 | PASS | |
| OQ-42-07 | FIXED | |
| OQ-42-08 | FIXED | Chờ review |
| OQ-45, 49, 57, 58 | PASS | |
| OQ-54 | BLOCKED | OQ-54a…d |
| F1, F2 | BLOCKED – chờ phê duyệt sửa | |
| OQ-40-01 / 39-08 (BP → PH2) | BLOCKED – chờ xác nhận đợt | |
| OQ-41-02, 55, 42-05, 42-06 | BLOCKED – chờ kỹ sư PLC / vendor | |
| OQ-43-01…12 | BLOCKED – chờ trả lời | Chưa sửa source |
| Trình duyệt; PLC, RCS, AGV thật | NOT VERIFIED | |

## 9. Kết quả xác minh (2026-11-26)

| Kiểm tra | Kết quả |
|---|---|
| Full backend regression (gồm sửa OQ-42-08; **trước** khi thêm 2 test F1/F2) | **1323/1323 đạt**, 18 phút 37 giây |
| F1, F2 | 2/2 đạt, ba lần chạy |
| Nhóm OQ-42-08 (`Cr042RackOwnershipTests`) | 15/15 đạt |
| Liên quan rack, Buffer, nhiều chặng, hàng đợi (`Cr010`, `Cr036`, `Cr040`, `Cr041`, `Cr042`) | 158/158 đạt |
| Sau lần full regression | Chỉ thêm file test F1/F2; không đổi mã trong `src` |
| Model và migration | Khớp |
| Frontend (`tsc`, `eslint`, build, `i18n:test` 76, `ops:test` 65) | Đạt; `i18n:check` còn 7 lỗi mẫu log RCS có từ trước |
| Nút giải phóng rack trên trình duyệt; PLC, RCS, AGV thật | **NOT VERIFIED** |
| Database vận hành | Chưa chạy migration; backend đang chạy trên máy vẫn là bản cũ |

**Cập nhật 2026-11-26:** HOLD đã được gỡ; mã được đưa vào `main`. Không deploy, không chạy migration trên database vận hành.
