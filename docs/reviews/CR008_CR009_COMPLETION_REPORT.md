# CR-008 / CR-009 – Báo cáo hoàn tất

**Ngày:** 2026-11-17 · **Branch:** `develop` · **Kết luận ngắn:** CR-008 đã được đóng trong phạm vi code có thể xác minh. **CR-009 KHÔNG được triển khai: repository không có đặc tả CR-009 đã duyệt** (xem mục 4).

## 1. Executive summary
- **CR-008:** B-1…B-5 đã có; vòng review xuyên suốt tìm thấy 2 lỗ hổng thật và đã sửa (mục 3). 514/514 test backend đạt.
- **CR-009:** BLOCKED – thiếu đặc tả (mục 4). Không tự tạo scope.
- **Commit mới trong nhiệm vụ này:** 1 (`fix(cr-008): ...`, nền `83cd2cd`). Không amend commit cũ.
- Không merge, không push, `main` = `d5d692f`.

## 2. Truy vết yêu cầu CR-008 (quyết định đã chốt)
| Yêu cầu | Nguồn | Code | Test | Kết quả |
|---|---|---|---|---|
| D-1 hết 30 lần: Failed một lần, giữ reservation, không Cancel tiếp | CR-008B-3 | `ReconcileCancelPendingAsync` | `Cr008b3*` | PASS (code/DB tạm) |
| D-9 NotFound ≠ Cancelled | CR-008B-3 | nhánh `NotFound` | `Cr008b3*` | PASS |
| D-5/D-6 đánh dấu rack Unknown + location | CR-008B-4/5 | `MarkPhysicalUnprovenAsync`, `FinishAsync` | `Cr008b4*`, `Cr008b5*` | PASS |
| D-7 DispatchHold P2, PER_AGV, không đổi AGV | CR-008B-5 | `Agv`, `TaskOrchestrator` | `Cr008b5*`, `Cr008Closure*` | PASS |
| D-8 Admin gỡ giữ, hai xác nhận, audit | CR-008B-5 | `AgvHoldService`, endpoint | `Cr008b5*` | PASS |
| Timeout CancelPending không bỏ qua D-6/D-7 | yêu cầu C4 | `EnforceTimeoutAsync` | `A_cancel_pending_timeout_*` | PASS (sửa mới) |
| Không dispatch AGV đang giữ (race) | yêu cầu C5 | `IsOnDispatchHoldAsync` | `Cr008Closure*` | PASS (sửa mới; xem giới hạn) |

## 3. Kết quả review đóng CR-008
1. **Race dispatch vs hold (lỗi thật, đã sửa).** `ProcessTaskAsync` đọc entity AGV trước, rồi mới kiểm tra task active. Dưới READ COMMITTED, nếu task cũ kết thúc (và đặt hold, cùng một commit) giữa hai lần đọc, giá trị `DispatchHold` của entity đã đọc bị cũ (EF không làm mới entity đang theo dõi) trong khi kiểm tra active thấy "trống" ⇒ task mới có thể vào `Dispatching` trên AGV bị giữ. Sửa: `IAgvRepository.IsOnDispatchHoldAsync` (truy vấn mới, AsNoTracking) được gọi **sau** kiểm tra active, ở cả `ProcessTaskAsync` và `TryDispatchNextAsync`; kiểm tra cuối ở `DispatchToRcsAsync` giữ nguyên. Test: `The_hold_flag_used_by_dispatch_is_a_fresh_read_*` (chứng minh bản sao theo dõi bị cũ còn truy vấn mới đúng) và `New_requests_racing_*` (6 vòng song song, bất biến: không task nào bắt đầu trên AGV bị giữ). *Giới hạn trung thực:* không có test tái hiện xác định lịch trình xấu; test song song không bắt buộc phát hiện được lỗi cũ.
2. **Timeout CancelPending (lỗi thật, đã sửa).** Khi bật `StateTimeoutsSeconds["CancelPending"]`, task thành `Failed` chỉ với `forceUncertain` (không đánh dấu rack nếu chưa Pick, không hold) ⇒ AGV được trả dù hủy chưa được xác nhận. Sửa: dùng `physicalUnproven` như hết giới hạn hủy. **Không bật timeout**; mặc định vẫn trống (có test). Các timeout khác không đổi: trạng thái chiếm AGV đi qua `CancelPending` (đường hủy chuẩn), trạng thái không chiếm AGV không có RCS nên giải phóng được.
3. **Vòng đời RcsEnded (xác minh, không cần mã mới).** Sau RcsEnded task vẫn active; hold đặt; release bị từ chối. Đường kết thúc có sẵn: (a) Drop PLC đến muộn ⇒ `Completed`; (b) Admin hủy ⇒ RCS báo Completed/Interrupted/NotFound ⇒ `Failed` + đối soát; (c) RCS không phản hồi ⇒ hết 30 lần ⇒ `Failed`. Sau đó Admin đối soát (xóa cờ vị trí) rồi gỡ giữ. Không thể bị giữ vĩnh viễn nếu Admin còn hủy được task. Test: hai test vòng đời trong `Cr008ClosureTests`. RcsEnded **không** tự kết thúc task hay giải phóng gì.
4. **Late Drop (xác minh).** Chỉ nhận khi task chưa kết thúc, đã Pick, rack thuộc đúng task (`CurrentTaskId`) và ở `InTransit`/`Unknown`; đặt rack đúng một lần tại đích của task, tiêu thụ reservation, không gỡ hold; tín hiệu lặp là Duplicate; Drop cho task đã Failed hoặc chưa Pick bị từ chối, rack không đổi. Cờ vị trí đặt lúc RcsEnded chỉ xóa bằng đối soát của Admin (đã test).

## 4. CR-009
Tìm toàn repository: không có requirement/design/acceptance criteria/decision register cho CR-009. Chỉ có dòng "CR-009 – CI + test frontend + E2E cơ bản" trong roadmap *đề xuất* của báo cáo review nội bộ `report/ETEK_WCS_FULL_CODE_REVIEW_2026-11-16.md` (mục 15), do chính trợ lý viết, **chưa được chủ dự án phê duyệt**, và không có tiêu chí nghiệm thu. Theo `CLAUDE.md` mục 3 (không triển khai khi chưa có Change Request được duyệt) và chỉ dẫn "không giả định nội dung CR-009", **tôi không triển khai gì cho CR-009**. Trạng thái: **NEEDS DECISION** – chủ dự án cần cung cấp/duyệt đặc tả CR-009 (hoặc xác nhận roadmap đề xuất trên là CR-009 và đưa tiêu chí nghiệm thu).

## 5. Tóm tắt kiểm thử
| Hạng mục | Kết quả |
|---|---|
| Backend (DB PostgreSQL tạm `wcs_test_*`, RcsSimulator) | **514/514 đạt** (506 trước + 8 `Cr008ClosureTests`) |
| Frontend | Không đổi trong nhiệm vụ này (kết quả lần trước: eslint/tsc/build sạch) |
| Migration | Không có migration mới; `AddAgvDispatchHold` đã kiểm tra ở lần trước (additive) |
| Docker | **KHÔNG thực hiện**: `docker` không có trên máy này |
| Trình duyệt thật | **KHÔNG thực hiện** (UI đối soát/hold chưa được duyệt bằng mắt) |
| PLC / RCS / AGV thật | **KHÔNG thực hiện** |

## 6. Vấn đề còn mở
| Mức | Mục |
|---|---|
| NEEDS DECISION | Đặc tả CR-009 |
| Vendor confirmation | REV-005 RCS idempotency (NOT VERIFIED); ngữ nghĩa trạng thái RCS 0–10 |
| Hardware validation | PLC/MC/handshake, AGV thật, RCS thật |
| High | Chưa chạy Docker acceptance và duyệt UI trên trình duyệt |
| Medium | Race chỉ được kiểm bằng lập luận + test song song, không có bộ tái hiện xác định; vận hành: AGV bị giữ cần Admin theo dõi (chưa có cảnh báo) |
| Low | `RcsEnded` đánh dấu rack Unknown khi task còn chạy (drop muộn vẫn được nhận) – cần reviewer xác nhận |

## 7. Git
Branch `develop`; commit mới nằm sau `83cd2cd`; file untracked cũ (docker-acceptance, report/…) giữ nguyên, không đưa vào commit; `main` = `d5d692f`; không merge, không push, không tag.

## 8. Khuyến nghị cuối
**NOT READY – BLOCKERS REMAIN** cho "hoàn thiện CR-008 + CR-009": CR-009 thiếu đặc tả. Riêng CR-008: **READY FOR INTEGRATION TEST** (trên môi trường có Docker/RCS thử nghiệm), chưa đủ bằng chứng cho site acceptance. Bước tiếp: (1) chủ dự án cung cấp đặc tả CR-009; (2) chạy Docker acceptance trên máy có Docker; (3) duyệt UI bằng trình duyệt; (4) nhận xác nhận của nhà cung cấp RCS (REV-005).
