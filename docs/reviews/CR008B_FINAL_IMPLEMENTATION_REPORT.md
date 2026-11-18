# CR-008B – Final Implementation Report

**Ngày:** 2026-11-17 · **Branch:** `develop` · **Kết luận ngắn:** B-1…B-4 đã triển khai và kiểm thử; **B-5 (giữ AGV) cố ý chưa triển khai vì D-7 chưa được chốt**; **chưa đủ điều kiện kết thúc CR-008B / chuyển sang CR-009** (xem mục 10).

## 1. Git state và danh sách commit
| Mục | Giá trị |
|---|---|
| Branch / `main` | `develop` / `main` = `d5d692f` (không đổi; không merge, không push) |
| Baseline | `3626812` (CR-008A) |
| Working tree | Chỉ còn các file untracked có sẵn từ trước (`docs/docker-acceptance/`, 4 báo cáo trong `report/`, `tests/deployment/Test-DockerAcceptancePackage.ps1`, `tests/deployment/fake-docker/`) – giữ nguyên |

| Commit | Nội dung |
|---|---|
| `efbb133` | **B-1** docs: đính chính thời gian retry, câu "PLC trình lại", rút quy tắc NotFound×2, ghi D-1; kèm hai báo cáo review/thiết kế |
| `ee2b477` | **B-2** feat(intake): kho yêu cầu trạm bền vững, migration `AddStationRequests` |
| `a3dd93f` | **B-3** feat(reconciliation): D-1 + gỡ NotFound×2 + API/UI đối soát Admin |
| `ab59659` | **B-4** feat(inventory): `MarkPhysicalUnprovenAsync` |
| `9e70e21` | **B-5 (phân tích)** + REV-005: không đổi mã |

Sự cố quy trình đã tự sửa: lần commit B-2 đầu tiên lỡ `git add -A tests` kéo theo hai file untracked không liên quan (công cụ nghiệm thu Docker); đã gỡ khỏi commit cục bộ (chưa push) bằng `git rm --cached` + `--amend` (hash `ee2b477`), hai file vẫn untracked. Các commit sau chỉ dùng đường dẫn tường minh.

## 2. Các CR nhỏ và báo cáo riêng
| CR | Phạm vi | Báo cáo |
|---|---|---|
| B-1 | Tài liệu (REV-005/006) | `docs/reviews/CR008B-1_DOCUMENTATION_CORRECTION.md` |
| B-2 | REV-002 | `docs/reviews/CR008B-2_PLC_INTAKE_RELIABILITY.md` |
| B-3 | REV-001, D-1, D-9 | `docs/reviews/CR008B-3_CANCELPENDING_RECONCILIATION.md` |
| B-4 | REV-003 | `docs/reviews/CR008B-4_RACK_INVENTORY_UNCERTAINTY.md` |
| B-5 | REV-004 (phân tích, chờ D-7) | `docs/reviews/CR008B-5_AGV_HOLD_ANALYSIS.md` |
| REV-005 | Câu hỏi nhà cung cấp RCS | `docs/reviews/CR008B-REV005_RCS_VENDOR_QUESTIONS.md` |

## 3. File thay đổi (từ `3626812`): 43 file, +3 404/−83
Mã nguồn (23 file): `StationRequestRecord` (mới), `IStationRequestRepository` + `StationRequestRepository` + cấu hình EF + `WcsDbContext`, `StationRequestProcessor` (viết lại), `ReconciliationService` (mới), `ITransportTaskRepository.ListRequiringReconciliationAsync`, `TaskOrchestrator`, `InventoryService` (+8 dòng), `IInventoryService`, `TransportTask`, `Endpoints.cs`, `ApiModels.cs`, DI, 2 migration + Designer + snapshot; frontend: `ReconciliationPanel.tsx` (mới), `TasksPage.tsx`, `client.ts`, `types.ts`, `vi.ts`. Test: `Cr008b2PlcIntakeTests`, `Cr008b3CancelReconciliationTests`, `Cr008b4InventoryUncertaintyTests` (mới), `Cr008aReliabilityTests` (điều chỉnh), `Harness.cs`. Tài liệu: CR, CHANGELOG (v2.3–v2.6), IMPACT_MATRIX, REQUIREMENT_TRACEABILITY (REL-06…REL-11), ADR-0007 (mục Đính chính + 5, 6), `task-state-machine.md` (đoạn làm rõ, không đổi quy tắc), các báo cáo trên.

## 4. Migration
| Migration | Nội dung | Kiểm chứng |
|---|---|---|
| `AddRcsRetryTracking` (CR-008A) | `ALTER TABLE transport_tasks ADD RcsLastAttemptAt timestamptz NULL, RcsRetryCount int NOT NULL DEFAULT 0` | SQL sinh ra (`dotnet ef migrations script`) chỉ có `ADD COLUMN` |
| `AddStationRequests` (B-2) | `CREATE TABLE station_requests` + 2 index (RequestKey UNIQUE; Status,FirstSeenAt) | SQL chỉ có `CREATE`; `Down` chỉ `DROP TABLE station_requests` |
`dotnet ef migrations has-pending-model-changes`: "No changes have been made to the model" (model và snapshot đồng bộ). **Không áp dụng lên `wcs_db`**; chỉ áp dụng lên DB test tạm do harness tạo/xoá. Không có DROP/TRUNCATE ngoài phạm vi.

## 5. Kết quả test thực tế (lần chạy cuối, sau commit `9e70e21`)
| Bộ | Kết quả |
|---|---|
| Backend toàn bộ (DB tạm `wcs_test_<guid>`, không dùng `wcs_db`) | **491/491 đạt**, 0 lỗi |
| Trong đó mới/điều chỉnh: `Cr008b2PlcIntakeTests` 12, `Cr008b3…` 7, `Cr008b4…` 9, `Cr008aReliabilityTests` 24 (điều chỉnh) | đạt |
| Frontend `eslint .` | 0 lỗi / 0 cảnh báo |
| Frontend `tsc -b` | 0 lỗi |
| Frontend `npm run build` | thành công (chỉ cảnh báo kích thước chunk có sẵn) |
| Kiểm tra migration | model đồng bộ; SQL chỉ cộng thêm |
Test phủ: PLC request persistence (RCS chưa kết nối, kết nối lại, restart, trùng Request_ID, hai trạm, một request lỗi không chặn request khác, Production+Simulator); CancelPending hết retry (Failed một lần, không trùng lịch sử, không gửi Cancel tiếp, giữ reservation, restart); NotFound không thành Cancelled; Admin/Operator; inventory uncertainty (Unknown rack, vị trí cần đối soát không được cấp, không hai vị trí hiện tại, reservation không giải phóng nhầm); PER_AGV và AGV1/AGV2 song song (CR-008A + ProcessFlow/Concurrency còn nguyên). Số liệu thời gian (62 s / 1 502 s) được **khẳng định bằng unit test** trên mã thật.
**Không phải PASS:** giao diện chưa được kiểm tra bằng trình duyệt thật (chỉ eslint/tsc/build); Docker/PLC/RCS/AGV thật: NOT VERIFIED; không kiểm tra lại danh sách database PostgreSQL sau khi chạy test.

## 6. Quy tắc nghiệp vụ được giữ nguyên (đối chiếu diff từ `3626812`)
`TaskStateMachine`, `TaskScheduler`, `FlowOptions`, `Application/Plc/**`, `Infrastructure/Plc/**` (MC, handshake, worker), `HostedServices.cs`, `appsettings.json` (`StateTimeoutsSeconds` vẫn rỗng): **diff rỗng**. `InventoryService`: +8 dòng (một phương thức mới; quy tắc đặt chỗ/pick/drop/đối soát không đổi). `TaskOrchestrator`: đổi có chủ đích (D-1, D-9, `physicalUnproven`). PER_AGV, AGV1/AGV2 song song, A3/A4, D1/D2, F5, thông số retry, handshake/địa chỉ PLC, `Request_Ack` (vẫn không ghi), priority: không đổi.

## 7. Những nội dung chưa được xác minh
RCS HIK RCS-2000 / PLC / AGV / Docker thật; **REV-005 – `NOT VERIFIED – RCS VENDOR CONFIRMATION REQUIRED`** (không giả định idempotent theo `RcsRequestCode`; câu hỏi và điều kiện chặn ở tài liệu riêng); UI đối soát chưa thử trên trình duyệt; hành vi PLC khi không nhận `Request_Ack`; `Request_ID` có duy nhất theo thời gian không (D-11); ngữ nghĩa trạng thái RCS và NotFound thật.

## 8. Quyết định còn chờ chủ dự án
| ID | Nội dung | Ảnh hưởng nếu chưa chốt |
|---|---|---|
| **D-7** | Điều kiện trả/giữ AGV theo từng kết cục | **B-5 chưa làm**: AGV vẫn được trả sau kết cục không chắc chắn (hành vi có từ trước, nay thêm kết cục hết giới hạn hủy theo D-1) |
| D-5/D-6 | Mô hình/phạm vi đánh dấu tồn kho | B-4 áp dụng cách đọc hẹp nhất của §7 (M2, chỉ kết cục RCS báo chưa chứng minh) – cần xác nhận |
| D-3 | Danh sách lỗi coi là vĩnh viễn/tạm thời ở intake | Hiện chỉ `RCS_NOT_CONNECTED` và lỗi kỹ thuật là tạm thời; mọi `AppException` khác như cũ |
| D-9 | Cơ chế đối soát tương đương để xác nhận `Cancelled` | Hiện NotFound không bao giờ thành Cancelled |
| D-10 | Giá trị retry (2 s/60 s/5/30) | Chỉ là giá trị kỹ thuật chưa xác nhận |
| D-11 | `Request_ID` duy nhất theo thời gian? | Rủi ro bỏ qua im lặng yêu cầu mới trùng ID cũ |
| D-14 | Retry Cancel sau đối soát | Chưa có thao tác; phân tích ở báo cáo B-3 |

## 9. Rủi ro còn lại
1. **REV-004 (MEDIUM, chờ D-7):** sau `Interrupted`, `Completed` khi hủy, `NotFound-sau-accept`, `RCS_DISPATCH_UNCERTAIN` và **hết giới hạn hủy (D-1)** task thành `Failed` ⇒ AGV được trả và task `Queued` kế tiếp được gán trong khi vị trí vật lý chưa rõ. Chưa có chính sách được duyệt nên không thay đổi.
2. Khoảng trống inventory: `RcsEnded` (task chưa terminal) và `RCS_DISPATCH_UNCERTAIN` không đánh dấu rack/vị trí; hết giới hạn hủy giữ nguyên trạng thái rack theo đúng chữ D-1.
3. Khối lượng đối soát có thể tăng: kết cục chưa chứng minh chặn **cả vị trí** (kể cả trạm yêu cầu) đến khi Admin đối soát.
4. Gửi lại `CreateTask` sau `NotFound` phụ thuộc idempotency của RCS thật (REV-005).
5. `station_requests` chưa có UI xem/dọn dẹp; yêu cầu `Failed` cần người vận hành xử lý bằng DB.
6. Task `Continuing` hết hạn mức vẫn chỉ gắn cờ (D-1 chỉ nêu CancelPending).

## 10. Kết luận: có đủ điều kiện chuyển sang CR-009 không?
**Chưa.** Đối chiếu điều kiện kết thúc (§13 của CR-008B):

| Điều kiện | Trạng thái |
|---|---|
| REV-001 đường thoát an toàn + Admin reconciliation | **Đạt** (backend đã test; UI chưa thử trên trình duyệt) |
| REV-002 không mất request PLC khi RCS unavailable | **Đạt** (mô phỏng; PLC thật NOT VERIFIED) |
| REV-003 không sai lệch inventory khi vị trí vật lý không rõ | **Đạt một phần**: kết cục RCS-báo đã xử lý; `RcsEnded`/`DISPATCH_UNCERTAIN` còn khoảng trống; D-5/D-6 chờ xác nhận |
| REV-004 không điều phối AGV trái đối soát đã duyệt | **Chưa xử lý – chờ D-7** (không có chính sách được duyệt để vi phạm, nhưng rủi ro còn mở) |
| REV-005 ghi nhận chưa xác minh | **Đạt** (đã ghi `NOT VERIFIED – RCS VENDOR CONFIRMATION REQUIRED`) |
| REV-006 tài liệu thống nhất với mã | **Đạt** (khẳng định bằng unit test) |
| Test bắt buộc đạt | **Đạt** (491/491, frontend sạch) |
| Không còn CRITICAL/HIGH chưa xử lý | Không có CRITICAL/HIGH mới; REV-004 là MEDIUM đang mở |

Đề nghị: chủ dự án quyết định **D-7** (và xác nhận D-5/D-6) → thực hiện **CR-008B-5**; sau đó đánh giá lại điều kiện chuyển CR-009. Việc kiểm tra giao diện đối soát trên trình duyệt nên làm trước khi dùng thật.
