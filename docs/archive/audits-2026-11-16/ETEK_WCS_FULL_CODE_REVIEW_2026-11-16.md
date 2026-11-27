# ETEK WCS – Full Code Review (Architecture, Code Quality, Security, Operational Risk)

**Ngày:** 2026-11-16 · **Loại:** AUDIT ONLY · **Ngôn ngữ:** Tiếng Việt · **Mục đích:** tài liệu đầu vào cho các Change Request tiếp theo

## Quy ước nhãn

| Nhãn | Ý nghĩa |
|---|---|
| **FACT** | Đọc trực tiếp từ mã nguồn hiện tại (có đường dẫn : dòng) |
| **TEST** | Kết quả test tự động đã chạy ở lần review này |
| **INFERENCE** | Suy luận từ mã, chưa chạy thực tế để chứng minh |
| **NOT VERIFIED** | Không thể kiểm chứng (thiếu Docker/PLC/RCS/AGV hoặc không an toàn để chạy) |

Mức độ: **CRITICAL** (mất dữ liệu, sai điều phối, mất an toàn, lộ quyền nghiêm trọng) · **HIGH** (dừng hệ thống, sai trạng thái, mất đồng bộ khi vận hành thực) · **MEDIUM** (độ tin cậy, mở rộng, bảo trì, hiệu năng) · **LOW** (chất lượng mã, UX, tài liệu).

## Mục lục
1. [Executive Summary](#1-executive-summary)
2. [Git baseline và phạm vi audit](#2-git-baseline-và-phạm-vi-audit)
3. [Kiến trúc tổng thể](#3-kiến-trúc-tổng-thể)
4. [Đánh giá từng module](#4-đánh-giá-từng-module)
5. [Danh sách phát hiện chi tiết](#5-danh-sách-phát-hiện-chi-tiết)
6. [Bảng tổng hợp theo mức độ](#6-bảng-tổng-hợp-theo-mức-độ)
7. [Top 10 rủi ro cần xử lý](#7-top-10-rủi-ro-cần-xử-lý)
8. [Chức năng đã hoàn thành](#8-chức-năng-đã-hoàn-thành)
9. [Chức năng chưa hoàn thành](#9-chức-năng-chưa-hoàn-thành)
10. [Chỉ test bằng Simulator/Mock](#10-chỉ-test-bằng-simulatormock)
11. [Chưa test với thiết bị thực](#11-chưa-test-với-thiết-bị-thực)
12. [Technical Debt](#12-technical-debt)
13. [Cần xác nhận từ kỹ sư PLC/RCS](#13-cần-xác-nhận-từ-kỹ-sư-plcrcs)
14. [Đề xuất thứ tự khắc phục](#14-đề-xuất-thứ-tự-khắc-phục)
15. [Roadmap theo Change Request](#15-roadmap-theo-change-request)
16. [Kết luận mức độ sẵn sàng](#16-kết-luận-mức-độ-sẵn-sàng)
- [Phụ lục A – Đính chính báo cáo audit trước](#phụ-lục-a--đính-chính-báo-cáo-audit-trước)
- [Phụ lục B – Phạm vi đã đọc và test đã chạy](#phụ-lục-b--phạm-vi-đã-đọc-và-test-đã-chạy)

---

## 1. Executive Summary

- **Hệ thống đã hoàn chỉnh ở mức Simulator**, kiến trúc phân lớp sạch, nghiệp vụ lõi (PER_AGV, đặt chỗ rack, idempotency, an toàn PLC) được thiết kế chắc và có test. **Không phát hiện sai lệch với các quy tắc bảo vệ** (PER_AGV, BP-04 REVIEW, A3/A4–D1/D2 UNCHANGED, WCS sở hữu tồn kho, không bind rack ở điểm "Không bind rack"; không ghi PLC khi chưa duyệt).
- **Khoảng trống lớn nhất không phải lỗi mã mà là thiếu tích hợp thật:** không có RCS adapter, không có đường đưa trạng thái từ RCS/PLC về WCS ngoài API simulator (AUDIT-001). Với cấu hình Production hiện tại, một task sẽ treo vĩnh viễn ở `Accepted`/`Executing`.
- **Các điểm yếu độ tin cậy khi vận hành thật** (đều INFERENCE từ mã, chưa chứng minh bằng chạy): scheduler tick bị chặn bởi lỗi intake trạm (002), ngân sách retry dispatch rất ngắn (5 lần × 1 s, 003), vòng retry RCS không giới hạn làm phình lịch sử (004), yêu cầu quyền PLC chỉ gửi một lần (005), nhánh đối soát hủy bỏ sót trạng thái RCS (006), timeout mặc định rỗng (007).
- **Triển khai:** gói Docker chưa từng chạy; không có HTTPS (cookie mặc định yêu cầu `Secure` nên không đăng nhập được qua HTTP).
- **Kiểm thử:** 308 test không-DB đạt; `tsc` và `eslint` sạch. Test tích hợp DB **không chạy** (tạo DB tạm trên PostgreSQL phát triển). Không có test frontend, E2E, tải, CI.
- **Kết luận:** *chưa sẵn sàng vận hành thật*; phù hợp thử nghiệm có giám sát ở chế độ Simulator/không-PLC. **31 phát hiện: 1 CRITICAL, 8 HIGH, 13 MEDIUM, 9 LOW.**

---

## 2. Git baseline và phạm vi audit

| Mục | Giá trị |
|---|---|
| Branch | `develop` |
| HEAD | `939b453` (đúng dự kiến); `main` = `d5d692f` |
| Working tree lúc bắt đầu | 5 mục chưa theo dõi (công cụ nghiệm thu Docker + 2 báo cáo + audit trước); **không có thay đổi lên file được theo dõi** |
| Hành động Git trong review | Không checkout, commit, push, merge, reset |
| File tạo mới | Chỉ báo cáo này |
| Không làm | Không sửa mã, không format, không migration, không đụng DB/cấu hình/dịch vụ đang chạy, không kết nối PLC/RCS/AGV |
| Lịch sử Git | 40 commit; `git log --all -- .env` không thấy file `.env` nào từng được commit; quét `git grep` không thấy mật khẩu/token cứng (chỉ tên trường/kiểu dữ liệu) – **FACT** (lưu ý: quét theo mẫu, không phải quét bí mật chuyên dụng → không thay thế công cụ như gitleaks) |

Phạm vi đọc thực tế: xem Phụ lục B.

---

## 3. Kiến trúc tổng thể

| Câu hỏi | Kết luận | Bằng chứng |
|---|---|---|
| Tuân thủ Clean Architecture? | **Có, phần lớn.** Domain không tham chiếu gì; Application chỉ tham chiếu Domain + abstractions; Infrastructure → Application/Domain; Runtime → Application/Infrastructure; Api → tất cả (composition root) | **FACT** – các `*.csproj` (`WCS.Application.csproj` chỉ có `ProjectReference` tới Domain; không có EF/Npgsql trong Application/Domain – `grep` rỗng) |
| Hướng phụ thuộc | Đúng | **FACT** + `SolutionStructureTests` đạt (TEST) |
| Phụ thuộc vòng | Không có | **FACT** (đồ thị tham chiếu ở trên, không chu trình) |
| Business logic sai lớp | Hầu như không. Ngoại lệ nhỏ: ràng buộc "chỉ hoàn thành khi đã xác nhận drop" nằm ở Orchestrator chứ không ở Domain (AUDIT-022); API lớp trên trực tiếp dùng `WcsDbContext` và kiểu Infrastructure (AUDIT-027) | `TaskOrchestrator.cs:313,321`; `Endpoints.cs:236` |
| Coupling | `TaskOrchestrator` nhận 15 dependency (constructor), 665 dòng, gom create/dispatch/signal/cancel/edit/delete/timeout (AUDIT-028) | `TaskOrchestrator.cs:14-31` |
| Code trùng | Chuỗi phân tích flow BP-04 lặp ở `CreateTaskAsync` và `UpdateTaskAsync` | `TaskOrchestrator.cs:~46-52` và `485-491` |
| Mã chết / chưa dùng | `PlcSimulator` (`IPlcAdapter`) và `IStationSignalService` legacy chỉ dùng trong Simulator; thư mục `reference/` (10 file, ~151 KB, có mã bên thứ ba) được track trong Git | `Simulators.cs`; `git ls-files reference` |
| Cấu hình hard-code | Chu kỳ 10 s (AGV status), 250 ms (handshake), 3 s (đợi catalog PLC) cứng trong mã; một số văn bản UI cứng | `HostedServices.cs` (các `PeriodicTimer`) |
| Khả năng mở rộng AGV/PLC/Station | **PLC/Station:** tốt (1 worker/PLC, cấu hình từ DB, 4 PLC × 4 trạm đang chạy trong test). **AGV:** vai trò AGV = `Role` unique (`AgvRepository`/index `Role` unique, `EntityConfigurations.cs:25`) → hệ thống mã hoá cứng "một AGV cho mỗi vai trò"; thêm AGV thứ 3 cùng vai trò **không được hỗ trợ** nếu không đổi schema/logic | **FACT** `EntityConfigurations.cs:25`; `agvs.GetByRoleAsync(rule.AgvRole)` `TaskOrchestrator.cs:~58` (INFERENCE về hệ quả) |

---

## 4. Đánh giá từng module

| Module | Đánh giá | Ghi chú chính |
|---|---|---|
| **WCS.Domain** | Tốt | Thực thể có bất biến (`TransportTask.ConfirmPick/ConfirmDrop`, `AttachRcsTask` chặn đổi mã RCS); `PlcPermissionGranted` được reset sau pick/drop (`TransportTask.cs:149,156`) – xác minh không có lỗ hổng "bỏ qua permission ở leg thứ hai". State machine là bảng kỹ thuật (ADR-0002), không phải đặc tả (AUDIT-022) |
| **WCS.Application** | Tốt, có điểm yếu ở vòng lặp retry | Orchestrator gọi RCS **ngoài** transaction (3 pha: chuẩn bị – gọi – áp dụng), `Tx` chạy lại khi xung đột; không gửi lại mù sau timeout (query trước). Điểm yếu: 002–006 |
| **WCS.Infrastructure** | Tốt | `EfUnitOfWork` (xmin + retry 12 lần + jitter); index unique bảo vệ PER_AGV/rack/idempotency; driver MC chặt chẽ (đã kiểm tra frame/length/route echo/ambiguous write); thiếu FK ở `reservations`/`inventory_transactions` (013) |
| **WCS.Runtime** | Trung bình | Các hosted service bắt lỗi theo tick; nhưng `SchedulerHostedService` gộp 2 bước trong 1 `try` (002); seeding nuốt lỗi (024) |
| **WCS.Api** | Tốt | Fallback policy bắt buộc đăng nhập; Admin/Operator tách rõ; CSRF header + SameSite=Strict; forwarded headers chỉ từ proxy; `ProductionSafety`; API log/simulator/test-write bị chặn ở Production |
| **Frontend** | Khá | Build sạch; role-UI chỉ là UX, backend vẫn xác thực lại (đúng); thiếu refetch sau reconnect và 0 test (015, 021) |
| **PostgreSQL/EF** | Khá | Ràng buộc quan trọng có; thiếu FK/retention (013, 018) |
| **PLC/MC** | Tốt (về mã) / NOT VERIFIED (thiết bị) | Xem mục 5.C và AUDIT-010 |
| **RCS/AGV** | Chỉ Simulator | AUDIT-001, 016, 017 |
| **Docker/Nginx/scripts** | Thiết kế tốt / NOT VERIFIED | AUDIT-008, 009, 020, 025, 031 |
| **Logging (CR-007)** | Tốt | Xem mục 5.D |
| **Tests** | Tốt cho backend Simulator; thiếu phần còn lại | AUDIT-021 |

---

## 5. Danh sách phát hiện chi tiết

> Định dạng mỗi phát hiện: ID · Severity · Category · File:Location · Evidence · Problem · Impact · Recommendation · Verification. Mọi khuyến nghị **chưa được phê duyệt**; các file thuộc danh sách bảo vệ (TaskStateMachine, TaskOrchestrator, InventoryService, TaskScheduler, FlowOptions, RCS Client, PlcWorker, McPlcEndpoint, McTcpClient, MC frame builder/parser, StationIoHandler, HandshakeCoordinator, HostedServices.cs) **chỉ được đề xuất, chờ phê duyệt trước khi sửa**.

### CRITICAL

#### AUDIT-001 · CRITICAL · RCS/Business · Task không thể tiến triển quá `Accepted` trong Production
- **File/Location:** `src/backend/WCS.Infrastructure/DependencyInjection.cs:85-88` (throw nếu `Integration:Mode` ≠ Simulator); `WCS.Api/Program.cs:75-76` (`MapSimulatorApi` chỉ khi `Simulator:ExposeApi`, mặc định = Development); `WCS.Application/Services/TaskOrchestrator.cs:211` (`HandleSignalAsync`)
- **Evidence:** `grep HandleSignalAsync` chỉ có hai nơi gọi ngoài chính nó: `SimulationDriver.cs:26` và `TaskOrchestrator.cs:579` (đối soát hủy). `IRcsClient` luôn là `RcsSimulator` (`DependencyInjection.cs`). Không có endpoint callback RCS, không có job poll `QueryTaskStatusAsync` cho task đang `Accepted/Executing`.
- **Problem:** tín hiệu `AgvStarted`, `PickConfirmed`, `DropConfirmed`, `PlcPermission*` không có nguồn nào trong Production.
- **Impact:** task tạo ở Production được `RcsSimulator` chấp nhận rồi treo; AGV bị chiếm (PER_AGV) và rack bị khoá cho đến khi Admin hủy; vì timeout rỗng (007) không tự phục hồi.
- **Recommendation:** CR "Tích hợp RCS HIK RCS-2000": adapter + ingestion trạng thái (callback có xác thực & idempotent, hoặc poller) → `HandleSignalAsync`; xác nhận pick/drop từ nguồn được duyệt (RCS/PLC). Không thực hiện khi chưa có tài liệu API RCS.
- **Verification:** FACT (đọc mã + grep). Hành vi chạy Production: NOT VERIFIED (không có Docker).
- **Giảm nhẹ hiện có:** banner "Hệ thống chưa kết nối RCS và PLC thật" (`Layout.tsx:138-141`) và chỉ báo RCS "Chưa kết nối" khi mode Simulator (`Layout.tsx:63`).

### HIGH

#### AUDIT-002 · HIGH · Backend · Lỗi intake trạm làm hỏng cả tick của scheduler
- **File/Location:** `WCS.Runtime/HostedServices.cs:25-28` ; `WCS.Application/Services/StationRequestProcessor.cs:25`
- **Evidence:** hai bước `StationRequestProcessor.RunOnceAsync` rồi `ITaskScheduler.RunOnceAsync` nằm trong **cùng một `try`**; `StationRequestProcessor` chỉ bắt `AppException`.
- **Problem:** một ngoại lệ khác (`DataConflictException` sau 12 lần thử, `DomainException`, lỗi `PollRequestsAsync` của `PlcStationSignalService`) thoát ra → bước scheduler **không chạy** ở tick đó.
- **Impact:** nếu lỗi dai dẳng, **không có dispatch/timeout/đối soát cho cả hai AGV** (scheduler "stale" trên `/api/system/health` nhưng không tự phục hồi). Với simulator hàng đợi, yêu cầu đã bị lấy khỏi hàng (mất); với PLC thật yêu cầu lặp lại mỗi tick.
- **Recommendation (chờ duyệt – file bảo vệ):** tách hai bước thành hai khối try độc lập; bắt mọi ngoại lệ ở mức từng yêu cầu trong intake và ghi nhận/ack có chủ đích.
- **Verification:** FACT (cấu trúc mã); hành vi INFERENCE (chưa có test gây lỗi).

#### AUDIT-003 · HIGH · RCS/Business · Ngân sách retry dispatch quá ngắn, không backoff
- **File/Location:** `TaskOrchestrator.cs:146-151` (`DispatchAttempts >= MaxDispatchAttempts` → `Failed` `RCS_DISPATCH_UNCERTAIN`, `forceUncertain`), `SchedulerOptions.MaxDispatchAttempts=5` (`Services.cs:102`), `TaskScheduler.cs:~40` (mỗi tick gọi lại `DispatchToRcsAsync`)
- **Evidence:** mỗi lần thử tăng `DispatchAttempts` (`RegisterDispatchAttempt`); tick 1 s; không có thời gian chờ giữa các lần.
- **Problem/Impact:** RCS không trả lời ~5 s ⇒ task chuyển `Failed` ở trạng thái "không chắc chắn", reservation bị giữ, yêu cầu Admin đối soát thủ công (`/reconcile`). Mỗi lần RCS khởi động lại/mạng chập chờn có thể sinh task `Failed` hàng loạt.
- **Recommendation:** backoff theo thời gian + giới hạn theo thời gian (không theo số tick), cấu hình được; quyết định với chủ dự án.
- **Verification:** FACT (mã); INFERENCE (thời gian ~5 s).

#### AUDIT-004 · HIGH · Business/Database · Vòng retry RCS không giới hạn, mỗi tick ghi lịch sử
- **File/Location:** `TaskOrchestrator.cs:416` (`RCS_CONTINUE_UNKNOWN` mỗi lần), `:554,559` (`RCS_CANCEL_REQUESTED/UNKNOWN`), `:582-584` (gửi lại cancel mỗi tick khi RCS còn `Executing/Created`); `TaskScheduler.cs` (Continuing/CancelPending quét mỗi tick)
- **Evidence:** `SendContinueAsync` không có trần số lần thử (khác với dispatch); mỗi vòng `Note(...)` thêm một `TaskHistoryEntry` (cùng transaction với tick).
- **Problem/Impact:** khi RCS chậm/lỗi kéo dài: ~86 400 dòng `task_history`/ngày/task + gọi RCS 1 Hz không dừng; có thể làm ngập RCS và phình bảng.
- **Recommendation (chờ duyệt):** backoff + trần, chỉ ghi Note khi đổi kết quả.
- **Verification:** FACT (mã); INFERENCE (tốc độ).

#### AUDIT-005 · HIGH · Business/PLC · Yêu cầu quyền PLC chỉ gửi một lần, không retry
- **File/Location:** `TaskOrchestrator.cs:268-275` (`after.Add(() => stations.RequestTransferPermissionAsync(...))`) và `Tx` `:658-662` (bắt và nuốt lỗi sau commit)
- **Problem:** task đã sang `WaitingForPlc`; nếu lệnh thực thi sau commit thất bại, chỉ log lỗi; không có vòng quét `WaitingForPlc` để gửi lại.
- **Impact:** task treo `WaitingForPlc` (chiếm AGV) cho đến timeout (mặc định tắt – 007). Chỉ xảy ra khi `Plc:Integration:Enabled=true` (hiện tắt, chưa có quy tắc handshake được duyệt).
- **Recommendation:** thêm bước phục hồi `WaitingForPlc` trong scheduler (idempotent) sau khi quy tắc handshake được duyệt.
- **Verification:** FACT (mã); hành vi INFERENCE.

#### AUDIT-006 · HIGH · Business · Đối soát `CancelPending` bỏ sót trạng thái RCS
- **File/Location:** `TaskOrchestrator.cs:576-585`
- **Evidence:** `switch (status)` chỉ xử lý `Cancelled|NotFound` và `Executing|Created`; `Completed`, `Interrupted`, `Cancelling`, `Unknown` **không làm gì**.
- **Problem/Impact:** nếu RCS báo task đã `Completed` hoặc `Interrupted` trước khi hủy kịp, task kẹt `CancelPending` (chiếm AGV, giữ rack) cho tới khi timeout (tắt). `NotFound` bị coi là "đã hủy" – nếu RCS mất bản ghi do lỗi khác, WCS sẽ kết luận hủy thành công.
- **Recommendation (chờ duyệt):** xử lý tường minh `Completed/Interrupted` (đi vào đối soát vật lý – `RequireReconciliation`), và phân biệt `NotFound` hợp lệ.
- **Verification:** FACT (mã); không có test cho các nhánh còn thiếu (đã tìm trong `ProcessFlowTests/RobustnessTests` – NOT VERIFIED chi tiết).

#### AUDIT-007 · HIGH · Business/Deployment · Timeout theo trạng thái mặc định rỗng
- **File/Location:** `WCS.Api/appsettings.json:24` (`"StateTimeoutsSeconds": {}`); `TaskOrchestrator.cs:596`; `Services.cs:104`
- **Impact:** kết hợp với 001/005/006: không có cơ chế tự giải phóng AGV bị kẹt. Giá trị phải do PLC/RCS thống nhất (đã ghi `NEEDS_CONFIRMATION`) – không tự đặt.
- **Verification:** FACT.

#### AUDIT-008 · HIGH · Security/Deployment · Không có HTTPS; mặc định không đăng nhập được qua HTTP
- **File/Location:** `deployment/docker/frontend/nginx.conf` (chỉ `listen 80`); `deployment/docker-compose.yml` (`Auth__CookieSecure: ${WCS_COOKIE_SECURE:-Always}`); `AuthSetup.cs` (`ResolveSecurePolicy`)
- **Problem/Impact:** cookie `Secure=Always` không được trình duyệt gửi qua HTTP ⇒ đăng nhập thất bại; phương án thay thế `SameAsRequest` đưa cookie phiên và mật khẩu qua HTTP thuần (backend có cảnh báo `CONFIG_COOKIE_INSECURE`).
- **Recommendation:** CR cho TLS termination (chứng chỉ do chủ dự án cung cấp), HSTS.
- **Verification:** FACT (cấu hình); chạy thực NOT VERIFIED.

#### AUDIT-009 · HIGH · Deployment · Gói Docker chưa từng được chạy
- **File/Location:** toàn bộ `deployment/*`; bằng chứng thiếu: `docs/DEPLOYMENT_TEST_CHECKLIST.md` H1–H6
- **Impact:** không có bằng chứng build image, migrator, healthcheck, proxy SignalR, backup/restore, khôi phục key ring.
- **Verification:** NOT VERIFIED (không có Docker). Công cụ nghiệm thu đã chuẩn bị (49/49 test với docker giả).

### MEDIUM

#### AUDIT-010 · MEDIUM · PLC · Worker PLC kết thúc vĩnh viễn khi gặp lỗi ngoài vòng try trong
- **File/Location:** `WCS.Application/Plc/PlcWorker.cs:165-175` (chỉ bắt `NeedsConfigurationException`), `:244-248` (`catch (Exception)` → `WORKER_CRASH`, vòng lặp kết thúc)
- **Impact:** lỗi không lường (cấu hình IP sai làm `_factory.Create` ném lỗi khác) → worker của PLC đó dừng, trạng thái `Faulted`, không tự khởi động lại cho tới `ReloadPlc`/restart. **Cô lập tốt:** mỗi PLC một worker nên 3 PLC còn lại không bị ảnh hưởng (`PlcRuntime.cs:123` – mỗi PLC một `PlcWorker`; `PlcWorker.cs:60` từ chối trạm của PLC khác).
- **Recommendation (chờ duyệt):** giám sát/khởi động lại worker, hoặc bắt rộng hơn trong vòng.
- **Verification:** FACT (mã); INFERENCE (kịch bản).

#### AUDIT-011 · MEDIUM · Business · Publish realtime sau commit không được bảo vệ ở InventoryService
- **File/Location:** `InventoryService.cs:254-256` (`Run`) và `:243` (`ReconcileTaskAsync`)
- **Evidence:** khác với `TaskOrchestrator.Tx` (bọc `try/catch` quanh `notifier.PublishAsync`, `:655-656`), ở đây ngoại lệ từ SignalR thoát ra **sau khi giao dịch đã commit**.
- **Impact:** API trả 500 dù dữ liệu đã đổi; người dùng thử lại (xem 012) có thể tạo trùng.
- **Verification:** FACT (mã); INFERENCE (tần suất thấp).

#### AUDIT-012 · MEDIUM · Business · `DeclareInitialStock` không idempotent
- **File/Location:** `InventoryService.cs:140-165`, `Endpoints.cs` `inv.MapPost("initial-stock")`
- **Problem:** không có khoá yêu cầu; gọi lặp tạo thêm rack (chỉ bị chặn bởi `CAPACITY_EXCEEDED`). Hành vi được CR-001 cho phép cả Operator.
- **Recommendation:** khoá idempotency hoặc xác nhận thao tác trên UI.
- **Verification:** FACT.

#### AUDIT-013 · MEDIUM · Database · Thiếu ràng buộc khoá ngoại ở bảng đặt chỗ và giao dịch tồn kho
- **File/Location:** `EntityConfigurations.cs:64-93` (không có `HasOne/HasForeignKey` cho `Reservation`/`InventoryTransaction`); `InventoryService.cs:203` (`racks.Remove` xoá cứng rack khi điều chỉnh giảm); `TaskOrchestrator.cs:535-537` (xoá cứng task/reservations)
- **Impact:** toàn vẹn dựa vào mã ứng dụng; `inventory_transactions` giữ `RackId/TaskId` mồ côi sau khi xoá (có chủ ý cho truy vết, nhưng không được ràng buộc). Một rack bị xoá khi `ListUnreservedAsync` đã loại rack đang đặt – nhưng không có FK nào chặn lỗi logic tương lai.
- **Verification:** FACT; không có bằng chứng mồ côi thực tế (NOT VERIFIED trên dữ liệu thật).

#### AUDIT-014 · MEDIUM · Business/Operations · Task kết thúc sau khi đã có `RcsTaskCode` giữ chỗ đến khi Admin đối soát
- **File/Location:** `TaskOrchestrator.cs:616-630` (`FinishAsync`), `InventoryService.cs:82-89` (`MarkUncertainAsync` không làm gì khi chưa pick), `:211-245`
- **Problem:** `Failed`/`Cancelled` mà RCS đã nhận task nhưng **chưa pick** → `RequireReconciliation` nhưng reservation giữ nguyên (không `MarkUnknown`, không set cờ vị trí); AGV được trả, rack nguồn vẫn khoá.
- **Impact:** mọi lần RCS lỗi/hủy sau accept cần thao tác Admin; kết hợp với 003 dễ phát sinh hàng loạt. (Theo `inventory-management.md 4.4/4.8` – là quyết định thiết kế có chủ đích; đây là đánh giá vận hành.)
- **Verification:** FACT; khối lượng INFERENCE.

#### AUDIT-015 · MEDIUM · Frontend · Dữ liệu cũ có thể hiển thị như mới
- **File/Location:** `RealtimeProvider.tsx:37` (`onreconnected` chỉ đổi trạng thái); `hooks/useApi.ts:34` (khi lỗi vẫn giữ `prev.data`)
- **Problem:** sau mất kết nối SignalR, **không refetch** và **không polling** (`setInterval` chỉ dùng cho đồng hồ); sự kiện bị lỡ đến khi có sự kiện kế tiếp. Khi API lỗi, dữ liệu cũ vẫn hiển thị (có `error` nhưng trang có thể chỉ hiện dữ liệu).
- **Impact:** màn hình AGV/Task/PLC có thể sai trạng thái trong vài phút sau gián đoạn mạng.
- **Recommendation:** refetch khi `reconnected`; hiển thị thời điểm cập nhật cuối; polling dự phòng.
- **Verification:** FACT (mã). Hiển thị thực tế: NOT VERIFIED (không có test frontend).

#### AUDIT-016 · MEDIUM · RCS/Frontend · Trạng thái AGV "Online" có thể là của Simulator
- **File/Location:** `AgvStatusService.cs:19` + `Simulators.cs` (`QueryAgvStatusAsync` luôn `Online=true, "simulated"`); `Agv.cs:21` (mặc định `Unknown`); `Layout.tsx:61-67`
- **Evidence:** AGV chỉ được làm mới khi có `RcsRobotCode` (không có → vẫn `Unknown`). Khi Admin đặt mã robot, `AgvStatusHostedService` đặt `Online` từ simulator. Chỉ báo RCS tổng ở header đã chặn (`mode==='Simulator'` → "Chưa kết nối"), nhưng thẻ AGV/ danh sách có thể vẫn hiện `Online`.
- **Impact:** người vận hành có thể hiểu nhầm AGV đang kết nối thật. (Đính chính audit trước: mặc định là `Unknown`, không phải `Online`.)
- **Verification:** FACT (mã); hiển thị thẻ AGV INFERENCE (không đọc `AgvCard`).

#### AUDIT-017 · MEDIUM · RCS · Hợp đồng `IRcsClient` không quy định timeout/hủy; scheduler xử lý tuần tự
- **File/Location:** `Abstractions/Integration.cs` (`IRcsClient`), `TaskScheduler.cs:28-60`
- **Impact:** adapter HIK thật không đặt timeout sẽ làm một cuộc gọi treo chặn **cả tick** (cả hai AGV; `PeriodicTimer` không chồng tick). Hợp đồng nên yêu cầu timeout theo cấu hình (`ConnectionSetting.TimeoutMs` đã có).
- **Verification:** INFERENCE.

#### AUDIT-018 · MEDIUM · Database · Retention không đồng nhất
- **File/Location:** `appsettings.json` (`SystemLog.RetentionDays=90`), `PlcModels.cs:189` + `PlcRuntimeInfrastructure.cs:230` (`LogRetentionDays=30` cho `plc_comm_log`)
- **Evidence:** retention **có** cho `system_log` (90 ngày) và nhật ký PLC (30 ngày) – (đính chính audit trước). **Không có** cho `audit_log`, `task_history`, `inventory_transactions`, `processed_events`.
- **Impact:** tăng trưởng không giới hạn; kết hợp 004 làm `task_history` phình nhanh.
- **Verification:** FACT.

#### AUDIT-019 · MEDIUM · Security · Không giới hạn tốc độ đăng nhập theo IP; khoá tài khoản có thể bị lợi dụng
- **File/Location:** `AuthService.cs:28-45`, `AppUser.cs:60-65` (`RegisterFailedLogin`), `nginx.conf` (không `limit_req`)
- **Impact:** kẻ có truy cập mạng có thể khoá tài khoản Admin (DoS tài khoản) hoặc thử mật khẩu ở tốc độ cao giữa các lần khoá. Mật khẩu băm PBKDF2 là mức hợp lý.
- **Verification:** FACT; khả năng khai thác INFERENCE.

#### AUDIT-020 · MEDIUM · Security/Deployment · Bí mật đi qua biến môi trường container
- **File/Location:** `docker-compose.yml` (`Database__Password`, `Bootstrap__Admin__Password`, `POSTGRES_PASSWORD`)
- **Impact:** người có quyền Docker (`docker inspect`) đọc được. Chấp nhận được cho máy đơn nội bộ nhưng cần ghi nhận; `.env` ngoài repo và `.gitignore` chặn `.env`. Backup **không** chứa `.env` (cảnh báo trong `backup.ps1`).
- **Verification:** FACT.

#### AUDIT-021 · MEDIUM · Tests/Deployment · Không có CI; không có test frontend/E2E/hiệu năng/bảo mật tự động
- **Evidence:** không có `.github/`; `package.json` không có test runner; 0 test frontend.
- **Impact:** hồi quy khó phát hiện; hành vi realtime/role-UI chỉ được kiểm bằng `tsc/eslint`.
- **Verification:** FACT.

#### AUDIT-022 · MEDIUM · Business/Domain · Bảng chuyển trạng thái cho phép `Executing → Completed` không kiểm điều kiện ở Domain
- **File/Location:** `TaskStateMachine.cs` (`Executing` → `Completed`; `CancelPending` → `Completed`); điều kiện thật nằm ở `TaskOrchestrator.cs:313,321` (`DropConfirmed` cần `PickConfirmedAt`, rack `InTransit`)
- **Problem:** `TransportTask.TransitionTo(Completed)` không tự kiểm tra `DropConfirmedAt`. Hiện chỉ Orchestrator gọi, nên không có đường sai đã tìm thấy; nhưng bất biến "hoàn thành ⇒ đã drop" không được Domain bảo vệ. Bảng edge là suy ra kỹ thuật (ADR-0002), không phải đặc tả → cần xác nhận.
- **Verification:** FACT (mã). Test `TaskStateMachineTests` (10) đạt (TEST) nhưng kiểm bảng, không kiểm bất biến này.

### LOW

| ID | Category | File : Location | Evidence / Vấn đề | Khuyến nghị | Verif. |
|---|---|---|---|---|---|
| **AUDIT-023** | Business | `TaskScheduler.cs:33`, `Repositories.cs:145` | Mỗi bước quét `Take(100)` (500 cho timeout) sắp theo `CreatedAt`; >100 task cùng trạng thái → các task sau chưa được xử lý/timeout | Phân trang hoặc quét theo AGV | FACT; chưa có tải |
| **AUDIT-024** | Backend | `HostedServices.cs` (`DatabaseSeedHostedService`) | Lỗi seed/bootstrap admin chỉ log, không retry; DB tạm chưa sẵn lúc khởi động → không tạo admin cho tới khi restart (Docker `depends_on` giảm rủi ro) | Retry có giới hạn | FACT |
| **AUDIT-025** | Security | `nginx.conf` | Có `nosniff`, `X-Frame-Options`, `Referrer-Policy`; thiếu CSP, HSTS (cần TLS) | Thêm khi có HTTPS | FACT |
| **AUDIT-026** | PLC | `Handshake.cs:89,104,120-124` | Bộ nhớ trạng thái khoá theo `stationId` (không phải `PlcId+StationId` như CLAUDE.md §7.3); khi đã duyệt luật, bit "set" có thể bị ghi lại mỗi tick 250 ms cho tới khi vòng đọc phản ánh giá trị | Dùng khoá hợp; chống ghi lặp | FACT/INFERENCE (chưa có luật được duyệt) |
| **AUDIT-027** | Architecture | `Endpoints.cs:11-12,236` | Lớp API dùng trực tiếp `WcsDbContext`, `WCS.Infrastructure.Simulation`, `IntegrationOptions` | Đưa truy vấn health vào Application | FACT |
| **AUDIT-028** | Maintainability | `TaskOrchestrator.cs` (665 dòng, 15 phụ thuộc); `HostedServices.cs` (hằng số 10 s/250 ms/3 s) | Nhiều trách nhiệm trong một lớp; chu kỳ cứng | Tách theo use-case khi có CR; đưa chu kỳ vào cấu hình | FACT |
| **AUDIT-029** | Repo hygiene | `reference/` (10 file ~151 KB) | Mã tham chiếu/bên thứ ba được track (có `McClient.cs`) | Xác nhận giấy phép; cân nhắc tách khỏi repo | FACT |
| **AUDIT-030** | Security/Logging | `AuthInfrastructure.cs:73` | Lỗi ghi `audit_log` được log (`Audit write failed`) nhưng nuốt — sự kiện bảo mật có thể mất khi DB lỗi cục bộ | Cảnh báo/đếm lỗi audit | FACT |
| **AUDIT-031** | Deployment | `deployment/docker/backend/Dockerfile:14-24`; `deployment/scripts/restore.ps1:50` | Build cài `dotnet-ef` bằng `--version 10.0.*` (không ghim đầy đủ); `restore.ps1` `DROP DATABASE … WITH (FORCE)` (có xác nhận `RESTORE`, kiểm `pg_restore --list`, backup an toàn `pre-restore` theo thông điệp) | Ghim phiên bản; diễn tập trên Docker thật | FACT; chạy NOT VERIFIED |

### 5.A Backend – câu trả lời cho các điểm chỉ định (mục 3 của yêu cầu)

| Điểm kiểm tra | Kết quả |
|---|---|
| API thiếu authorization | **Không phát hiện.** `Endpoints.cs`: nhóm `/api` yêu cầu `Operations`; cấu hình/người dùng/audit/log/PLC-config yêu cầu `AdminOnly`; fallback policy bắt đăng nhập; `/healthz` ẩn danh (chỉ Up/Down). Hub SignalR `[Authorize(Operations)]` |
| Thao tác vượt quyền | Operator không gọi được cancel/reconcile/config/users (`RequireAuthorization(admin)`); Operator sửa/xóa task chỉ khi chưa chạm AGV/RCS và được **kiểm lại trong transaction** (`EnsureUserEditable`, `TaskOrchestrator.cs:467-475`) |
| HTTP status | `ExceptionMapping` ánh xạ hợp lý; `InvalidTaskTransitionException` → 409; lỗi chưa xử lý → 500 không lộ chi tiết. Lưu ý `DataConflictException` → 409 |
| Service lifetime | Đúng: repository/Orchestrator `Scoped`; `PlcRuntime`, simulator, `HandshakeCoordinator` `Singleton` dùng `IServiceScopeFactory`; không phát hiện capture Scoped trong Singleton |
| Background service lỗi không phục hồi | Scheduler/AGV/Handshake: bắt lỗi theo tick (nhưng 002); **PlcWorker: không tự phục hồi sau crash (010)**; seeding không retry (024) |
| Async không await / fire-and-forget | Không thấy `async void`/task bỏ rơi; hành động "after-commit" của `Tx` được await tuần tự và nuốt lỗi (có log) – gây 005 |
| Resource disposal / socket | `McTcpClient`: `Close()` trước khi mở lại, `tcp.Dispose()` trên lỗi connect, `_gate` bảo vệ, timeout đóng socket (`McTcpClient.cs:60-78,133-139`); `DisposeAsync` gọi `DisconnectAsync` – **không phát hiện rò rỉ** (INFERENCE từ mã; test mock đạt) |
| Race condition | PER_AGV, rack, đặt chỗ đích dựa vào index unique + xmin (`Touch`) + retry UoW (`EfUnitOfWork.cs`); `ConcurrencyTests` (6) – **chưa chạy lại** ở review này |
| Deadlock | `PlcRuntime._sync` chỉ dùng cho start/stop/reload; `PlcWorker` là consumer duy nhất của socket. Không phát hiện khoá lồng nhau |

### 5.B Nghiệp vụ (mục 4 của yêu cầu)

| Nội dung | Kết quả |
|---|---|
| Duplicate request / idempotency | `RequestKey` unique + `GetByRequestKeyAsync` trong transaction (`TaskOrchestrator.cs:~64-70`); `ProcessedEvent(Source,EventKey)` unique cho tín hiệu (`:220`) – **DONE (TEST ở phiên trước)** |
| Task bị thực hiện hai lần | Một AGV một task hoạt động (index DB + `GetActiveForAgvAsync`); gửi RCS bằng `RcsRequestCode` ổn định và query trước khi gửi lại – không phát hiện đường gửi trùng |
| AGV Offline | `ProcessTaskAsync`/`TryDispatchNextAsync` chỉ kiểm `agv.Enabled`; **không kiểm `ConnectionStatus`** ⇒ task vẫn được gửi cho AGV được RCS báo Offline (RCS sẽ tự xử lý). Cần chủ dự án xác nhận chính sách (NEEDS_CONFIRMATION, không tự suy diễn) |
| Không có destination | `WaitingForDestination` được quét lại mỗi tick (`TaskScheduler.cs`, `TryResolveDestinationAsync`); đúng |
| Starvation | FIFO theo `CreatedAt`; chính sách ưu tiên **không được xác nhận** nên không đổi. Giới hạn 100 (AUDIT-023) |
| Task treo | Xem 001, 005, 006, 007 |
| Rack ở hai vị trí / mất rack | Rack có một `CurrentLocationId`; chuyển `InTransit` bằng `BeginTransit`, `PlaceAt` khi drop; check constraint `ck_racks_transit_no_location`; `ConfirmPick/ConfirmDrop` kiểm `RACK_NOT_AT_SOURCE`/`RACK_NOT_IN_TRANSIT` (`InventoryService.cs:97,115`). Không tìm thấy đường tạo rack hai vị trí. Rack "mất" chỉ xảy ra có kiểm soát qua `MarkUnknown` + đối soát |
| Hai task cùng một rack | Index unique `reservations(RackId)` khi `Active ∧ SourceRack` (`EntityConfigurations.cs:74-75`) |
| Hai task cùng slot cuối của đích | `loc.Touch()` đổi xmin → một transaction thua và chạy lại (`InventoryService.cs:68`) |
| Điều chỉnh tồn kho | Kiểm sức chứa gồm đặt chỗ; không xoá rack đang đặt (`ListUnreservedAsync`) |
| Station/Location | `AllowedRequestStations` theo flow, `Accepts(RackKind)`, `HasFreeSlot(occupied, reserved)` |

### 5.C PLC và MC Protocol (mục 5 của yêu cầu)

| # | Câu hỏi | Kết luận |
|---|---|---|
| 1 | Frame 3E Binary đúng? | **Đúng về cấu trúc** (FACT, `McFrame.cs:84-102`): sub-header `50 00`, route, độ dài, monitoring timer, command `0401/1401`, sub-command `0000` (word)/`0001` (bit), đầu thiết bị 3 byte, mã thiết bị (M `90`, D `A8`, W `B4`, X `9C`, Y `9D`). So sánh byte-by-byte với thư viện tham chiếu: 13 test `McReferenceParityTests` (TEST – nằm trong 308) |
| 2 | Tính độ dài | `dataLength = 12 + data.Length` (`:87`) = timer 2 + cmd 2 + sub 2 + head 3 + code 1 + points 2 ✔. Phản hồi: `len ≥ 2`, `≤ 2+2×960` (`:122-125`), kiểm tra tổng chiều dài (`:146`) |
| 3 | End code | `McEndCodeException`, giữ kết nối (`McTcpClient.cs:114-119`); dữ liệu không được giải mã khi end code ≠ 0 |
| 4 | Timeout làm treo worker? | Không: `CancelAfter` quanh gửi+nhận (`:156-157`) → `TimeoutException`, đóng socket (`:133-139`); đọc thử lại 1 lần |
| 5 | Reconnect tạo nhiều socket? | Không: `OpenAsync` gọi `Close()` trước; `_gate` tuần tự hoá; mở lại khi `_stream is null` (`:111`) |
| 6 | Nhiễm phản hồi giữa các request? | Được chặn: mọi timeout/lỗi khung đóng socket (`:137`); phản hồi kiểm route echo và độ dài dữ liệu mong đợi (`McFrame.cs:156-166`) |
| 7 | Race nhiều Station cùng PLC? | Không: một `PlcWorker` là người dùng duy nhất của socket; các trạm đọc **tuần tự** (`PlcWorker.cs:203-224`); ghi qua kênh hàng đợi |
| 8 | Ghi nhầm địa chỉ? | `WritePolicy` + `IoPointRef.Address` chỉ từ `ActualAddress` đã xác nhận (`PlcRuntime.cs:123-126`); X không ghi được (`McFrame.cs:66`). Địa chỉ **không được suy ra** |
| 9 | Ghi khi chưa đủ điều kiện? | Không (FACT): ghi thật cần `AllowLiveConnection` ∧ `Write.Enabled` ∧ `EngineerApprovalReference` ∧ `HandshakeApproved` + điểm `WriteAllowed` + địa chỉ duyệt (`PlcConfigService.cs:195-233`); Safety/Transfer/Departure luôn bị chặn ở chế độ thật; Production mặc định `RealReadOnly` |
| 10 | Poll địa chỉ chưa duyệt? | Không: điểm không có địa chỉ xác nhận ⇒ `NotConfigured` (`PlcWorker.cs:219`); `HasConfirmedAddress` quyết định (`PlcRuntime.cs:125`) |
| 11 | PLC mất kết nối ảnh hưởng scheduler? | Không trực tiếp: `PlcWorker` chạy `Task.Run` riêng; scheduler chỉ dùng `IStationSignalService` (mặc định simulator). Khi bật tích hợp PLC, mất PLC làm dữ liệu trạm `Bad/Disconnected` (an toàn) |
| 12 | Một PLC lỗi ảnh hưởng 3 PLC còn lại? | Không (FACT): worker/socket/`_gate` riêng cho từng PLC; lỗi một PLC chỉ ảnh hưởng 4 trạm của nó. Tồn tại: lỗi chậm tuần tự trong cùng một PLC (đọc 4 trạm tuần tự, timeout × retry = chu kỳ dài) |
| – | Điểm yếu | AUDIT-010 (crash worker vĩnh viễn), AUDIT-026 (handshake ghi lặp/khoá), `State` của `McTcpClient` không có khoá khi đọc từ luồng khác (an toàn thực tế vì ghi/đọc đơn giản – LOW, INFERENCE) |
| – | Test thực tế | **Chỉ Simulator/mock** (`MelsecMcMockServer`, oracle tham chiếu); **chưa có PLC thật** – NOT VERIFIED với thiết bị (QJ71E71-100, base X/Y/W, giới hạn điểm) |

### 5.D Logging & Diagnostics (mục 11 của yêu cầu; đánh giá thiết kế CR-007)

| Nội dung | Đánh giá |
|---|---|
| Phân loại/tra cứu | `system_log` + view hợp nhất (API Admin `/api/logs`, lọc theo thời gian/loại/mức/thiết bị/mã sự kiện/từ khoá, chi tiết kèm stack trace); `LogEndpoints.cs` chỉ GET, Admin-only |
| Theo PLC/Station/AGV | Thuộc tính `Device`/`PlcId`/`Agv`/`RobotCode` được map (`SystemLogSink.cs: ToEntry`); log PLC nằm ở `plc_comm_log` (retention 30 ngày) |
| Không mất log khi DB lỗi | Sink có hàng đợi **giới hạn** (`_queue`, `RemoveFirst` khi vượt dung lượng, đếm `_dropped` – `SystemLogSink.cs:140-141`) ⇒ khi DB down log **bị rơi có đếm** (không phình RAM); log console/Docker vẫn còn. Điều tra lúc DB down dựa vào log Docker + deploy log |
| Bảo mật log | `LogSanitizer.Redact` che mẫu bí mật trước khi lưu; sink bỏ qua log của chính nó (`SkipSources`) |
| Thiếu sót thiết kế còn lại | (a) Console log không có giữ-xoay ngoài `json-file 10m×5` của Docker → mất log cũ sớm khi chạy lâu; (b) không có cảnh báo chủ động (alert) khi có `Critical`; (c) UI Logging chưa kiểm chứng trên Docker (NOT VERIFIED); (d) `audit_log` không có retention (018) |

### 5.E Frontend (mục 8 của yêu cầu)

| Câu hỏi | Kết quả |
|---|---|
| Operator thấy/gọi chức năng Admin? | UI: `RequireRole` chặn route Admin; **backend vẫn chặn độc lập** (`RequireAuthorization(admin)`) → FACT: không phụ thuộc việc ẩn nút |
| Mất kết nối API | `request()` bắt `fetch` lỗi → `NETWORK_ERROR` (`client.ts`); `401` → sự kiện đăng xuất |
| SignalR reconnect | Có `withAutomaticReconnect` (`signalr.ts`) nhưng **không refetch sau reconnect** (015) |
| Dữ liệu cũ như mới | Xem 015 |
| Trạng thái AGV/PLC sai? | Xem 016; PLC dữ liệu `Simulated` được gắn nhãn chất lượng (`PointQuality.Simulated`) |
| XSS | Không thấy `dangerouslySetInnerHTML`/`innerHTML`/`localStorage` (grep rỗng) |
| Demo Mode lọt vào production? | Build Docker dùng `npm run build` (không `VITE_DEMO_MODE`); `.env.example` đặt `false`; `ENGINEERING_TOOLS` tắt trong build production (`config.ts`) – FACT |
| Error boundary | Không thấy (LOW, không tách riêng) |

### 5.F Database (mục 7)

6 migration tuần tự (InitialCreate, AddConnectionSettings, AddAuthentication, AddPlcTopology, AddMcAddressBasesAndTimeouts, AddSystemLog). Không phát hiện migration phá huỷ dữ liệu trong phần đọc (FACT cho phạm vi đã đọc; nội dung `Up()` của từng migration **không đọc dòng-dòng** – NOT VERIFIED chi tiết). Bundle chạy **forward only** (Dockerfile). Index/unique đã nêu ở 5.B. Phân trang: `Skip/Take` ở danh sách task/log; `racks` `Take(500)`; giao dịch tồn kho `Take(50/100)`. N+1: `AgvStatusService` truy vấn từng AGV trong vòng lặp (2 AGV – không đáng kể); `TaskScheduler` nạp `History` (`WithHistory`) khi lấy task để đổi trạng thái (tăng kích thước khi lịch sử phình – liên hệ 004).

### 5.G Security (mục 9)

| Nội dung | Kết quả |
|---|---|
| Hard-code credentials | Không phát hiện (quét tracked files + lịch sử `.env`); `admin/admin` chỉ tạo ngoài Production và bắt buộc đổi (`AuthInfrastructure.cs:153-154`); Production không tạo `admin/admin` |
| Password hashing/policy | PBKDF2 (ASP.NET Identity `PasswordHasher`); chính sách tối thiểu 10 ký tự, chữ + số, không chứa tên đăng nhập (`PasswordPolicy.cs`), ghi `NEEDS_CONFIRMATION` |
| Session/CSRF/CORS | Cookie HttpOnly, SameSite=Strict, `Secure` mặc định; header `X-WCS-Client` cho mọi thao tác thay đổi; CORS chỉ whitelist, mặc định rỗng |
| SQL injection | EF Core tham số hoá; `FromSql`/SQL thô: không thấy trong phần đọc |
| Forwarded headers | Chỉ từ proxy cấu hình, cấm `0.0.0.0/0` (`ForwardedHeadersSetup.cs`) |
| Data Protection | Key ring ở volume `etek_wcs_keys`; có sao lưu key ring (`backup.ps1`); mất volume ⇒ mất bí mật RCS đã lưu |
| Lộ cổng | PostgreSQL ở mạng `internal`, không publish; backend không publish; chỉ Nginx publish |
| Nhiều replica backend ngoài ý muốn | Compose không khai báo `deploy.replicas`; `PlcRuntime`, scheduler chạy trong tiến trình → **chạy 2 backend sẽ có 2 scheduler/2 worker PLC** (xung đột). Index DB bảo vệ PER_AGV nhưng 2 worker cùng PLC không được bảo vệ – INFERENCE; nên ghi rõ "single instance" |
| Còn lại | 008, 019, 020, 025 |

### 5.H Docker/Deployment (mục 10)

| Câu hỏi | Kết quả |
|---|---|
| Deploy từ máy sạch | Thiết kế có (`deploy.ps1`, `.env` ngoài repo, migrator trước backend) – **NOT VERIFIED** |
| Khôi phục DB | `restore.ps1` (xác nhận `RESTORE`, `pg_restore --list`, backup an toàn) – NOT VERIFIED |
| Mất Data Protection keys | Có sao lưu key ring; chưa diễn tập |
| Proxy SignalR | `nginx.conf`: Upgrade/Connection, `proxy_read_timeout 1h`, buffering off – đúng về cấu hình; NOT VERIFIED chạy thật |
| Migration thất bại | `depends_on: service_completed_successfully` ⇒ backend không khởi động; bundle forward-only |
| Healthcheck/restart/giới hạn tài nguyên/log rotation | Có (xem compose); NOT VERIFIED |
| `deploy.ps1` chỉ cho `main` | Trên `develop` dùng `start.ps1` |

---

## 6. Bảng tổng hợp theo mức độ

| Mức độ | Số lượng | ID |
|---|---|---|
| **CRITICAL** | **1** | 001 |
| **HIGH** | **8** | 002, 003, 004, 005, 006, 007, 008, 009 |
| **MEDIUM** | **13** | 010–022 |
| **LOW** | **9** | 023–031 |
| **Tổng** | **31** | |

Theo hạng mục: Business/RCS 9 · PLC 3 · Backend 4 · Database 3 · Frontend 2 · Security 5 · Deployment 5.

---

## 7. Top 10 rủi ro cần xử lý

| # | Rủi ro | ID | Lý do ưu tiên |
|---|---|---|---|
| 1 | Task không tiến triển quá `Accepted` ở Production | 001 | Chặn vận hành thật |
| 2 | Scheduler tick bị chặn bởi lỗi intake | 002 | Dừng điều phối cả 2 AGV |
| 3 | Dispatch fail "uncertain" sau ~5 s RCS chập chờn | 003 | Tạo task Failed hàng loạt, cần đối soát tay |
| 4 | Retry RCS không giới hạn + phình `task_history` | 004 | Quá tải RCS/DB |
| 5 | `CancelPending` kẹt khi RCS `Completed/Interrupted` | 006 | AGV/rack kẹt |
| 6 | Timeout mặc định rỗng | 007 | Không tự giải phóng AGV kẹt |
| 7 | `WaitingForPlc` không retry | 005 | AGV kẹt (khi bật tích hợp PLC) |
| 8 | Không HTTPS, cookie `Secure` không đăng nhập qua HTTP | 008 | Triển khai LAN không an toàn/không dùng được |
| 9 | Docker chưa kiểm chứng | 009 | Mọi thứ triển khai là chưa chứng minh |
| 10 | Không CI + không test frontend + test DB chưa chạy lại | 021 | Hồi quy |

---

## 8. Chức năng đã hoàn thành

(Mức Simulator/Mock; có bằng chứng L1 + test, trừ ghi chú)
- Xác thực cookie, vai trò Admin/Operator, quản lý tài khoản, audit, khoá tài khoản, đổi mật khẩu bắt buộc.
- Tạo/sửa/xoá/huỷ/đối soát task; PER_AGV (logic + ràng buộc DB); idempotency; đặt chỗ rack nguồn/ô đích; tồn kho WCS (khai báo/điều chỉnh/giao dịch).
- Connection settings (PLC/RCS) với bí mật mã hoá, không trả về.
- Mô hình 4 PLC × 4 trạm × 23 tín hiệu; chính sách an toàn ghi PLC; PLC worker; driver MC 3E (với mock); handshake framework (chỉ quan sát khi chưa có luật duyệt).
- Giao diện: Dashboard, AGV, Task, Inventory, PLC, cấu hình, Users, Audit, Logging & Diagnostics, Demo Mode.
- Logging & Diagnostics (CR-007) + bộ nghiệm thu Docker (đã tự kiểm bằng docker giả).

## 9. Chức năng chưa hoàn thành

RCS adapter thật + ingestion trạng thái (001) · địa chỉ PLC thật, thông số MC/QJ71E71-100, quy tắc handshake được duyệt, heartbeat chưa phân loại · HTTPS · sao lưu tự động · retention đầy đủ · timeout/ưu tiên/chính sách AGV Offline được duyệt · CI/test frontend/E2E/tải · dữ liệu thật (vị trí, sức chứa, mã robot) · BP-04 (REVIEW, cố ý).

## 10. Chỉ test bằng Simulator/Mock

Toàn bộ luồng task/tồn kho (RcsSimulator + StationSignalSimulator); driver MC (mock server + oracle); PLC runtime (PlcSimulator); kiểm tra kết nối (`SimulatedConnectionProbe`); bộ nghiệm thu Docker (docker giả); frontend chạy Demo/engine trong trình duyệt.

## 11. Chưa test với thiết bị thực

PLC (4 thiết bị), RCS HIK RCS-2000, AGV, Docker Desktop/WSL2 runtime, Nginx/SignalR qua Docker, backup/restore thực, nghiệm thu H1–H6.

## 12. Technical Debt

Orchestrator lớn; thiếu FK ở hai bảng; hằng số chu kỳ cứng; không CI; 0 test frontend; `reference/` trong repo; phiên bản công cụ rất mới (TS 6, Vite 8, ESLint 10) cần khoá; chính sách retention rời rạc; lặp mã phân tích flow; API lớp trên dùng kiểu Infrastructure.

## 13. Cần xác nhận từ kỹ sư PLC/RCS

**PLC:** cổng TCP, loại khung/MC (3E binary), network/PC/module I/O/station, cơ sở đánh số X/Y/W (hex/dec), phạm vi thiết bị cho phép, kích thước chuỗi/encoding/scale, địa chỉ thực từng tín hiệu (368 điểm), đặc tả handshake (chuyển trạng thái, ai reset, timeout), phân loại heartbeat (PLC hay Station), chu kỳ polling, quy trình thử ghi an toàn.
**RCS:** API/phiên bản, xác thực, callback (URL, bảo mật, retry), ánh xạ mã trạng thái 0..10, mã robot, timeout/giới hạn tần suất, ngữ nghĩa `NotFound` sau hủy, hành vi khi AGV Offline.
**Chủ dự án:** timeout theo trạng thái, ưu tiên task, chính sách AGV Offline, retention, xác nhận bảng chuyển trạng thái (ADR-0002).

## 14. Đề xuất thứ tự khắc phục

1. Quyết định chủ dự án (mục 13) – mở khoá mọi thứ.
2. Nghiệm thu Docker H1–H6 (009) – không đổi mã.
3. CI + test frontend (021), chạy lại test DB trong môi trường cô lập.
4. Sửa độ tin cậy vòng điều phối **sau khi được duyệt** (002, 003, 004, 006, 005): thay đổi nhỏ, tập trung, có test hồi quy.
5. HTTPS/hardening (008, 019, 025), sao lưu tự động.
6. Tích hợp RCS (001, 016, 017).
7. Tích hợp PLC thật (010, 026, handshake).
8. Dữ liệu thật, retention (018), vận hành.

Phụ thuộc: 4 độc lập với 6 nhưng nên làm trước 6 để adapter thật không thừa hưởng lỗi vòng lặp; 6 phụ thuộc tài liệu RCS; 7 phụ thuộc kỹ sư PLC; 2 và 3 có thể song song.

## 15. Roadmap theo Change Request

| CR đề xuất | Nội dung | Phát hiện liên quan | Phụ thuộc |
|---|---|---|---|
| CR-008 | Kiểm tra độ tin cậy vòng điều phối: tách khối try tick, backoff/trần retry RCS, xử lý `Completed/Interrupted` ở đối soát hủy, retry `WaitingForPlc` | 002–006, 010 | Phê duyệt sửa module bảo vệ |
| CR-009 | CI + test frontend + E2E cơ bản | 021 | – |
| CR-010 | HTTPS/TLS, rate-limit đăng nhập, CSP/HSTS, quản lý bí mật | 008, 019, 020, 025 | Chứng chỉ/hạ tầng |
| CR-011 | Tích hợp RCS HIK RCS-2000 | 001, 016, 017 | Tài liệu RCS, môi trường RCS, CR-008 |
| CR-012 | Tích hợp PLC thật (commissioning chỉ-đọc → ghi được duyệt) | 010, 026 | Kỹ sư PLC |
| CR-013 | Timeout/ưu tiên/AGV Offline/retention | 007, 018, 023 | Chủ dự án |
| CR-014 | Toàn vẹn dữ liệu (FK, idempotency tồn kho, publish an toàn) | 011, 012, 013 | – |
| Nghiệm thu | Docker H1–H6, sao lưu/khôi phục | 009, 031 | Máy Docker |

## 16. Kết luận mức độ sẵn sàng

Không dùng tỷ lệ phần trăm (không có tiêu chí đo). Thang: **Sẵn sàng / Có điều kiện / Chưa sẵn sàng / Không thể đánh giá**.

| Khía cạnh | Đánh giá | Cơ sở |
|---|---|---|
| Code Architecture | **Sẵn sàng** | Phân lớp đúng, không vòng phụ thuộc, test cấu trúc đạt |
| Business Logic | **Có điều kiện** | Lõi đúng và chặt; thiếu ngân sách/backoff retry, đối soát hủy chưa đủ trạng thái, timeout rỗng (002–007) |
| PLC Integration | **Có điều kiện (mã) / Không thể đánh giá (thiết bị)** | Driver chặt, an toàn ghi tốt; chưa có PLC thật, địa chỉ, handshake |
| RCS/AGV Integration | **Chưa sẵn sàng** | Chỉ simulator; không có ingestion trạng thái (001) |
| Database | **Có điều kiện** | Ràng buộc chính có; thiếu FK, retention, chưa diễn tập backup/restore |
| Frontend | **Có điều kiện** | Build sạch; 0 test; thiếu refetch/hiển thị độ mới dữ liệu |
| Security | **Có điều kiện** | Xác thực/ủy quyền tốt; thiếu HTTPS, rate-limit |
| Deployment | **Chưa sẵn sàng (chưa kiểm chứng)** | Docker chưa chạy; HTTPS chưa có |
| Operational Readiness | **Chưa sẵn sàng** | Không vận hành được với RCS/PLC thật; chưa nghiệm thu, không CI |

**Tóm lại:** hệ thống đủ tốt để **thử nghiệm có giám sát ở chế độ Simulator, không PLC/RCS thật**; **chưa** đủ điều kiện vận hành thật. Việc cần làm trước tiên là nghiệm thu Docker và quyết định các thông số nghiệp vụ, sau đó sửa độ tin cậy vòng điều phối trước khi viết adapter RCS.

---

## Phụ lục A – Đính chính báo cáo audit trước

Hai nhận định trong `ETEK_WCS_Full_Product_Audit_2026-11-16.md` đã được đối chiếu lại với mã và **cần sửa**:
1. *"plc_comm_log không có chính sách giữ"* – **sai**: có `Plc:LogRetentionDays` (30 ngày) dùng ở `PlcRuntimeInfrastructure.cs:230`.
2. *"Trạng thái AGV hiển thị trong Production là giả (luôn Online)"* – **chỉ đúng khi có `RcsRobotCode`**; mặc định AGV `Unknown` và không được làm mới (`AgvStatusService.cs:~17`, `Agv.cs:21`); chỉ báo RCS tổng ở header đã hiển thị "Chưa kết nối" ở chế độ Simulator (`Layout.tsx:63`).

Các kết luận còn lại của báo cáo trước (P0-1 ≙ AUDIT-001, HTTPS, CI…) được xác nhận lại bằng đọc mã.

## Phụ lục B – Phạm vi đã đọc và test đã chạy

**Đã đọc trực tiếp (có số dòng):** `Program.cs`, `ProductionSafety.cs`, `AuthSetup.cs`, `ForwardedHeadersSetup.cs`, `Endpoints.cs`, `ApiPlumbing.cs`, `LogEndpoints.cs`, `PlcEndpoints.cs` (route/authorization), `HostedServices.cs`, `TaskScheduler.cs`, `TaskStateMachine.cs`, `TransportTask.cs`, `TaskOrchestrator.cs` (toàn bộ), `InventoryService.cs` (toàn bộ), `StationRequestProcessor.cs`, `AgvStatusService.cs`, `AuthService.cs`, `PasswordPolicy.cs`, phần chính `AuthInfrastructure.cs`, `EfUnitOfWork.cs`, `Simulators.cs`, `WcsDataSeeder.cs`, `Infrastructure/DependencyInjection.cs`, `Integration.cs`, `EntityConfigurations.cs` (index/FK), `McFrame.cs` (toàn bộ), `McTcpClient.cs` (toàn bộ), `PlcWorker.cs` (vòng chính), `PlcRuntime.cs`, `WritePolicy` (`PlcConfigService.cs:195-233`), `Handshake.cs` (toàn bộ), `SystemLogSink.cs` (đầu + hàng đợi), `docker-compose.yml`, `nginx.conf`, Dockerfile, phần của các script triển khai, `App.tsx`, `RealtimeProvider.tsx`, `useApi.ts`, `client.ts`, `Layout.tsx` (phần liên quan), `config.ts`, `package.json`.
**Chưa đọc dòng-dòng (không kết luận chi tiết):** `McPlcEndpoint.cs`, `StationIoHandler.cs`, `PlcConfigService` (ngoài `WritePolicy`), `PlcPersistence.cs`, `LogQueryService.cs`, `DeploymentLogImporter.cs`, nội dung `Up()` các migration, `UserAdminService`, `ConnectionConfigService`, phần lớn trang frontend, `engine.ts` (demo), toàn bộ script PowerShell (chỉ đọc đoạn nguy hiểm: `Remove-Item`, backup retention, restore).

**Test đã chạy (cùng phiên, HEAD `939b453`, working tree không đổi file được theo dõi):**

| Lệnh | Kết quả |
|---|---|
| `dotnet test tests/WCS.Tests --filter` (Domain, Plc, Auth, Config validator/service/probe, LoggingCore, SolutionStructure) | **308 đạt / 0 lỗi / 0 bỏ qua** (24 s) |
| `tsc -p tsconfig.app.json --noEmit` | 0 lỗi |
| `eslint .` | 0 lỗi/cảnh báo |

**Không chạy (lý do):** 13 file test tích hợp dùng `Harness` (tạo/xoá DB tạm `wcs_test_<guid>` trên PostgreSQL phát triển) – vi phạm nguyên tắc không tác động hệ thống đang chạy; `npm run build` (ghi `dist/`); test triển khai PowerShell động; Docker. Con số 410/410 của phiên trước được trích dẫn, **không** được xác nhận lại ở đây. Số lượng test không được dùng để kết luận an toàn: các nhánh nêu ở 002, 003, 004, 005, 006, 022 **chưa có test gây lỗi/biên** (INFERENCE – chưa tìm thấy test tương ứng; không đọc toàn bộ test).
