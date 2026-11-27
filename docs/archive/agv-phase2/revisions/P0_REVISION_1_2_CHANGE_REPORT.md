# P0 Revision 1.2 – Báo cáo thay đổi: Two-Gate Lifecycle & Queue Ordering

| | |
|---|---|
| **Ngày** | 2026-11-24 |
| **Phạm vi** | **Chỉ tài liệu.** Cập nhật 6 file Markdown trong `docs/specification/agv-phase2/` từ Revision 1.1 lên **Revision 1.2** và báo cáo này |
| **Quyết định áp dụng** | **D-11** (Two-Gate: Publication ⟂ Readiness; đóng OQ-44), **D-12** (Queue Intake = Published + Ready; Intake Record; Suspended → Held), **D-13** (Strict FIFO theo Queue Ordering Scope) |
| **Giữ nguyên** | D-09 (PH2-04 baseline, không mở khóa BP-04 Phase 1), D-10 (ghim Version tại intake, mơ hồ → `NeedsDecision`, không tự chuyển Version), 20 bất biến nghiệp vụ |
| **Trạng thái** | DRAFT – chờ chủ dự án duyệt. **Không** triển khai code, **không** commit/push/deploy |

---

## 1. File đã thay đổi

| File | Thay đổi chính |
|---|---|
| `PHASE2_OPERATIONAL_SPEC.md` | Hàng Revision 1.2; D-11/12/13 trong §2; thuật ngữ; INV-26/29 viết lại, **INV-30…34** mới; OQ-01, OQ-44 → đóng; OQ-32/35 sửa; **OQ-49…58** mới; CR P1-E/P1-H/P2-B sửa, **P1-I, P2-E** mới; §15.2b thay bằng **Mô hình hai Gate**; §15.3, §15.4b (Intake), §15.7, §19; **§22** mới (trạng thái Queue, Held, Scope, ADR-0011 D-1, Readiness theo giai đoạn, bảng khác biệt Rev 1.1 → 1.2); §23 change log |
| `PHASE2_TRANSPORT_JOB_LIFECYCLE.md` | Thực thể Queue Request và Intake Record; §2.5 viết lại (trạng thái Intake Record, sơ đồ Queue Request, bảng sở hữu, điều kiện Queue → Dispatch, chuyển trạng thái cho phép/cấm, sự kiện audit, tương tác Order/Leg, ánh xạ 1.1 → 1.2); EV-1/2/3 viết lại; **EV-14, EV-15** mới; §6, §7, §12; §19 change log |
| `PHASE2_PROCESS_FLOWS.md` | §11.2 (bước 1, 4–6), §11.2b (luồng + Mermaid Published + Ready), §11.3 (Held, strict FIFO, Disable), **§11.4 viết lại** (Publication Gate ⟂ Readiness Gate theo từng process; bỏ L1–L5/DC-01), §11.5, §5 (đoạn phân biệt trạng thái, hàng Location/IO/trigger), §12; §15 change log |
| `PHASE2_STATION_IO_MAPPING.md` | **§14 mới:** điều kiện PLC/Station thuộc Readiness Gate; Scope không suy ra từ PLC; §9 thêm OQ-53/54/55; §15 change log. **Không** thêm địa chỉ/tín hiệu |
| `PHASE2_RACK_BUFFER_QUEUE_RULES.md` | §6 (FIFO theo Scope), **§6.1 viết lại** (strict FIFO theo Scope), **§6.2** (Queue Ordering Scope), §7.2 (`Waiting`, thứ tự theo Scope, sự kiện Readiness), **§7.3** (Held và hồi phục), §8, §10, §13.1 (thêm `QueueHeld`/`QueueResumed`, `ReadinessCheck`, `ScopeEvaluation`; bỏ `QueueBypass`), §12/§14; §17 change log |
| `PHASE2_ACCEPTANCE_TESTS.md` | Sửa 14 test (giữ ID); **§29–§36 mới: 59 test**; §12/§13 cập nhật |
| `docs/archive/agv-phase2/revisions/P0_REVISION_1_2_CHANGE_REPORT.md` | Báo cáo này (mới) |

## 2. Các mục đã thêm/sửa theo quyết định

| Quyết định | Nội dung đặc tả | Nơi |
|---|---|---|
| **D-11 Two-Gate** | Hai trục độc lập: *Publication Status* (Draft, Pending Approval, Approved, Published, Disabled) và *Readiness* (NotReady, Ready, Suspended); không có trạng thái gộp; Readiness thay đổi **không** đổi Publication Status. Bảng kết hợp (Published + Ready → đủ điều kiện, vẫn qua Process Selection/business checks; Published + NotReady/Suspended → không; Disabled → không). Publication Gate: định nghĩa hợp lệ, phê duyệt nghiệp vụ, Version định danh, không xung đột nghiệp vụ chưa giải quyết, tham chiếu tồn tại/hợp lệ, không lỗi cấu trúc – **không** đòi PLC/RCS/vật lý. Readiness Gate: Location/Station xác minh, ánh xạ IO PLC duyệt (nếu dùng PLC), kết nối + handshake đã thử, RCS, cơ chế xác nhận Pick/Drop, commissioning, không bị đình chỉ. Workbook PLC chưa duyệt **không** là căn cứ Ready | Spec §15.2b; Flows §11.4 |
| **D-12 Queue Intake** | Chỉ Published + Ready vào Queue chính thức; không Version thay thế, không bỏ qua Readiness; Intake Record (Received/NeedsDecision) ≠ Queue Request; sau `NeedsDecision` kiểm tra lại Published + Ready trước khi ghim; audit quyết định/người/Version; kiểm tra + ghim là một thao tác nhất quán. Luồng: Request Intake → Process Selection → Check Publication + Readiness → Pinning → Queue Waiting | Spec §15.4b; Lifecycle §2.5; Flows §11.2b |
| **D-12 Held** | Suspended ⇒ `Waiting → Held`: không hủy, giữ `ProcessVersionId` + `QueueSequence`, không đổi Rack/tồn kho, không tự Dispatch khi hồi phục, revalidate (Rack, đích, PLC, điều kiện vận hành), trở lại **vị trí FIFO ban đầu**; Dispatched/InProgress không tự hủy, không giả định dừng an toàn | Spec §22.2; Rack §7.3; Lifecycle EV-14/15 |
| **D-13 Strict FIFO** | Không bypass trong một Scope; đầu Scope Blocked ⇒ Request sau chờ; Scope độc lập chạy song song; ≤ 1 giao dịch/PLC; **không** mặc định PLC = Scope, **không** mặc định một Scope toàn cục; dữ liệu Scope thiếu ⇒ ghi OQ/phụ thuộc kỹ thuật, không bịa thuật toán | Spec §22.3–§22.4; Rack §6.1–§6.2 |

## 3. Khác biệt ngữ nghĩa Revision 1.1 → 1.2 (không đổi hành vi âm thầm)

Bảng đầy đủ: `PHASE2_OPERATIONAL_SPEC.md` §22.6 (9 hàng) và `PHASE2_TRANSPORT_JOB_LIFECYCLE.md` §2.5.3 (ánh xạ trạng thái). Tóm tắt:

| # | Revision 1.1 | Revision 1.2 |
|---|---|---|
| 1 | Điều kiện nhận Request: Published đủ (L3); sẵn sàng kỹ thuật chặn **thực thi** (DC-01 phương án b) | **Published + Ready** mới vào Queue chính thức; NotReady/Suspended không vào |
| 2 | Mô hình 5 mức L1–L5 + DC-01 | Hai trục Publication ⟂ Readiness (OQ-44 đóng) |
| 3 | Bộ trạng thái Queue: Received, NeedsDecision, Queued, Active, OrderCreated, Done, Failed/Cancelled | Waiting, Held, Prepared, Dispatched, InProgress, Completed, Failed, Cancelled + Intake Record (Received, NeedsDecision) |
| 4 | FIFO: OQ-01 mở; FIFO riêng mỗi PLC (ADR-0011 D-1) | **Strict FIFO theo Scope**; PLC không mặc định là Scope; OQ-01 đóng; OQ-54/55/56/58 mở |
| 5 | `NeedsDecision` là trạng thái Queue Request | `NeedsDecision` là trạng thái **Intake Record**; sau giải quyết kiểm tra lại Published + Ready |
| 6 | Controlled FIFO/`QueueBypass` là phương án | **Không bypass** trong Scope; `QueueBypass` bị loại khỏi bản ghi quyết định |
| 7 | Suspended chưa tồn tại | Suspended ⇒ Held (EV-14/15) |
| 8 | `Queued` / `Active` / `OrderCreated` | `Waiting` / `Prepared`+`Dispatched` / `Dispatched`+`InProgress` (**ranh giới [ĐX] – OQ-49**) |
| 9 | `PORT_MAPPING_NOT_APPROVED`… chặn Publish | Chuyển sang **Readiness Gate**; chỉ lỗi tham chiếu/cấu trúc/nghiệp vụ chặn Publish |

## 4. Open Questions

**Đóng:**
- **OQ-44** (Two-Gate) – theo D-11.
- **OQ-01** (strict hay bypass) – **đóng thêm ngoài yêu cầu liệt kê**, vì D-13 chọn strict FIFO theo Scope và trực tiếp trả lời câu hỏi gốc. *Cần chủ dự án xác nhận việc đóng này.* Phần còn lại của OQ-01 (phạm vi Scope) chuyển thành OQ-54.

**Giữ mở:** OQ-35, **OQ-45** (không đóng), **OQ-46** (không đóng), các OQ kỹ thuật PLC/Location/RCS/commissioning (OQ-03, 04, 06, 11, 21, 27, 28, 29…).

**Mới (OQ-49…58):**

| OQ | Nội dung |
|---|---|
| OQ-49 | Ranh giới `Prepared` / `Dispatched`, thời điểm tạo Order |
| OQ-50 | Xử lý Intake Record bị từ chối (ghi nhận / thông báo / nhận lại khi Ready) |
| OQ-51 | Chuyển trạng thái Readiness chưa nêu (Ready → NotReady; Suspended → NotReady) |
| OQ-52 | Định nghĩa/thẩm quyền Emergency Revocation, khác Disabled thế nào |
| OQ-53 | Độ mịn, phụ thuộc và người xác nhận Readiness; `LOCATION_DISABLED` thuộc Gate nào |
| OQ-54 | Cấu hình Queue Ordering Scope (nguồn dữ liệu, tài nguyên tạo ràng buộc, hành vi khi thiếu dữ liệu) |
| OQ-55 | Phân xử khe giao dịch PLC giữa các Scope; quan hệ với ADR-0011 D-1 |
| OQ-56 | Khóa FIFO và thời điểm cấp `QueueSequence` |
| OQ-57 | Reservation khi Held |
| OQ-58 | Request Held ở đầu Scope có chặn các Request sau không |

## 5. Kiểm thử chấp nhận

| | Trước (Rev 1.1) | Sau (Rev 1.2) |
|---|---|---|
| Test được đặc tả | 160 | **219** (+59) |
| Trạng thái | Tất cả NOT RUN | Tất cả **NOT RUN** (0 chạy, 0 Passed) |
| Test có BLOCKED trong tiêu đề | 45 | 63 (44 cũ + 19 mới; T-P4-07 hết BLOCKED) |
| Test bị xóa | — | **0** |

**Test mới (59):** T-PG-01…10 (Publication Gate), T-RG-01…08 (Readiness Gate), T-QI-01…11 (Queue Intake/Pinning), T-HD-01…10 (Held/Recovery), T-SF-01…12 (Strict FIFO/Scope), T-DR-01…08 (Disabled/Emergency Revocation).

**Test sửa (giữ ID, 14):** T-FI-01 (bỏ BLOCKED(OQ-01)), T-FI-02, T-FI-04, T-QU-01 (`Queued` → `Waiting`), T-DP-01, T-P4-01, T-P4-07, T-P4-09 (bỏ OQ-44; theo Two-Gate), T-P4-10 (OQ-01 → OQ-54), T-DP-08, T-PIN-A, T-PIN-B, T-PIN-C, T-PIN-G (Intake Record, Published + Ready, `Waiting`). Test thông tin còn thiếu được ghi BLOCKED/[ĐX] thay vì tự đặt kỳ vọng (ví dụ T-HD-04, T-HD-09, T-SF-04…09, T-DR-03/05/06, T-QI-03).

## 6. Kiểm tra nhất quán đã chạy

Script kiểm tra tự viết (ngoài repo), kết quả:

| Kiểm tra | Kết quả |
|---|---|
| Số cột mọi bảng Markdown (6 file) | **0 lỗi** |
| Hàng rào ``` khớp cặp (6 file) | Đạt |
| OQ được dùng đều có định nghĩa trong Spec §12 | 58 định nghĩa, 58 được dùng, **0 thiếu** |
| ID test duy nhất; mọi tham chiếu test tồn tại | 219 ID, **0 trùng, 0 tham chiếu hỏng** |
| Mọi test có `NOT RUN` trong tiêu đề | 219/219 |
| Hàng Revision 1.2 trong cả 6 file | Có |
| Tham chiếu cũ (OQ-44, DC-01, L1–L5, Queued, OrderCreated, OQ-01) | Đã rà từng chỗ; **chỉ còn** trong: lịch sử Rev 1.1, hàng "đã đóng/đã giải quyết", bảng so sánh 1.1 → 1.2, change log, tên `WaitKind` (`QueuedForDestination`). Đã sửa các chỗ còn sót (Spec dòng Transport Job/FIFO/P2-A, Lifecycle EV-3 và danh sách OQ, Flows danh sách OQ) |
| PH2-04 | Không còn REVIEW-blocked; được phép Published qua Publication Gate dù chưa Ready (D-09) |
| BP-04 Phase 1 | **Không** mở khóa (T-LG-03, OQ-46 giữ nguyên) |
| D-10 | Giữ: ghim tại intake, mơ hồ → `NeedsDecision`, không tự chuyển Version |
| Sơ đồ Mermaid | Đọc kiểm cú pháp thủ công (flowchart Intake; stateDiagram Queue Request dùng nhãn không dấu) – **chưa render** |

## 7. Trạng thái git (so với trước khi bắt đầu)

`git status --short` **giống hệt** ảnh chụp trước Revision 1.2 (8 mục có sẵn từ đầu phiên). Toàn bộ `docs/specification/agv-phase2/` và `docs/reports/` là **untracked** nên `git diff` không hiển thị diff từng file; thay đổi được xác nhận qua change log trong từng file và kiểm tra ở §6.

Không đụng tới: backend/frontend/source, DB/migration, cấu hình, `reference-processes.json`, workbook PLC, PPTX, tài liệu Phase 0, BR-02/ADR-0008/CLAUDE.md, test code. File IO Map v2 đã xóa và thư mục `docs/reports/` **không** được restore/stage/commit. **Không** commit, push, deploy; **không** bắt đầu P1/CR-010.

## 8. Giới hạn và rủi ro còn lại

1. **Prepared/Dispatched** (ranh giới, thời điểm tạo Order) là **đề xuất [ĐX]** – OQ-49. Test chỉ kiểm các trạng thái đã quyết (Waiting, Held, NeedsDecision).
2. **Chưa có dữ liệu cấu hình Queue Ordering Scope** (OQ-54): chưa xác định được thành viên Scope, giao nhau gián tiếp, hành vi khi thiếu dữ liệu (`SCOPE_UNDETERMINED` là đề xuất). Test Scope dùng fixture giả lập.
3. **Phân xử khe PLC giữa các Scope** (OQ-55) và việc ADR-0011 D-1 (FIFO theo PLC) được thay/bổ sung thế nào **chưa quyết**; code hiện tại **không** có PLC-level FIFO lẫn Scope.
4. **Hành vi sau revalidate thất bại**, **Held ở đầu Scope**, **Reservation khi Held**, **Ready → NotReady**, **Emergency Revocation**, **Version đã ghim bị Disabled** vẫn mở (OQ-51/52/57/58/45).
5. Phân loại mã validator giữa Publication/Readiness là **[ĐX]** (riêng `LOCATION_DISABLED`: OQ-53); khi triển khai cần CR (P1-E, P1-I, P2-E).
6. Việc đóng **OQ-01** là hệ quả suy ra từ D-13 – cần chủ dự án xác nhận.
7. Mô hình mục tiêu **chưa hiện thực**: code hiện tại không có Readiness, Intake Record, Held, Scope hay PLC-level FIFO. Không tuyên bố zero-loss.
8. Sơ đồ Mermaid chưa được render thực tế.

**Dừng tại đây – chờ chủ dự án review Revision 1.2.**
