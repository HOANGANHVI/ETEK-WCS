# ETEK WCS – Full Product Completion Audit

| Mục | Giá trị |
|---|---|
| Ngày | 2026-11-16 |
| Loại | **AUDIT ONLY** – không sửa source, không migration, không đổi DB, không đổi cấu hình triển khai |
| Phạm vi | Toàn dự án (backend 5 project, frontend, DB, deployment, test, tài liệu), không chỉ CR-007 |
| Báo cáo nền | `docs/archive/audits-2026-11-16/ETEK_WCS_Current_State_Audit_2026-11-16.md` (audit `main`@d5d692f); báo cáo này **kiểm tra lại trên `develop`@939b453** và mở rộng |

## Nhãn trạng thái
**DONE** = đã triển khai và có bằng chứng kiểm thử · **PARTIAL** = đã triển khai nhưng chưa đầy đủ · **NOT IMPLEMENTED** · **UNVERIFIED** = có code, thiếu bằng chứng kiểm chứng · **BLOCKED** = không kiểm tra được do thiếu môi trường/điều kiện.

**Mức bằng chứng:** L1 = đã đọc code · L2 = test tự động đã chạy ở audit này · L3 = chạy thật ngoài container (máy dev) ở phiên trước · L4 = môi trường thực (Docker/PLC/RCS/AGV). **Hiện không hạng mục nào đạt L4.**

## Những gì audit này thực sự đã làm (trung thực)
- Đọc trực tiếp: `Program.cs`, `ProductionSafety`, `AuthSetup`, `ForwardedHeadersSetup`, `Endpoints`, `ApiPlumbing`, `HostedServices`, `TaskScheduler`, `TaskStateMachine`, `TaskOrchestrator` (phần đầu + danh sách điểm vào), `StationRequestProcessor`, `AgvStatusService`, `AuthService`, `EfUnitOfWork`, `Simulators`, `WcsDataSeeder`, `Infrastructure/DependencyInjection`, `Integration.cs` (hợp đồng RCS/PLC), cấu hình EF (index/FK/constraint), `PlcWorker` (reconnect/ghi), `docker-compose.yml`, `nginx.conf`, các Dockerfile, `App.tsx`, `RealtimeProvider`, `package.json`.
- **Chưa đọc từng dòng** (dựa trên báo cáo trước và test): phần sau của `TaskOrchestrator` (HandleSignal, Cancel, Reconcile, Finish), `InventoryService`, `McFrame/McTcpClient/McPlcEndpoint`, `Handshake`, `PlcConfigService`, các trang frontend (ngoài routing), `LogsPage`, `DeploymentLogImporter`. Nhận định về các phần này mang nhãn L2/báo cáo trước, không phải đọc lại.
- **Test đã chạy:** 308 test backend **không dùng DB** (Domain, Plc/MC, Auth service, Config validator/service, Logging core, Solution structure): **308/308 đạt** (24 s). `tsc --noEmit` (tsconfig.app) = 0 lỗi; `eslint .` = 0 cảnh báo/lỗi.
- **Cố ý không chạy:** các test tích hợp dùng `Harness` (13 file: Integration/*, ConfigApiTests, LogStorageTests…) vì mỗi test tạo/xóa một database PostgreSQL tạm (`wcs_test_<guid>`) trên PostgreSQL phát triển – trái chỉ dẫn "không chạy test có thể tác động hệ thống đang chạy" và sẽ để lại thêm DB rác nếu tiến trình bị ngắt. Kết quả 410/410 của phiên trước (trên `main`) được trích dẫn, **không** tái xác nhận ở đây. Không chạy bộ test triển khai PowerShell động (cần PostgreSQL và cổng mạng). Không build Docker (không có Docker). Không chạy `npm run build` (ghi ra `dist/`).
- Không kết nối PLC/RCS/AGV; không đụng `wcs_e2e`, `wcs_test_*`.

---

## 1. Executive Summary

| Câu hỏi | Trả lời |
|---|---|
| Trạng thái sản phẩm | Phần mềm lõi hoàn chỉnh **ở mức Simulator/Mock**; gói triển khai Docker đã viết nhưng **chưa từng chạy**; **chưa có tích hợp thiết bị thật** |
| Có vận hành thật được chưa? | **Chưa.** Hai chặn cứng: (1) không có RCS adapter thật + không có đường đưa trạng thái RCS về WCS; (2) PLC thật chưa có thông số/địa chỉ/handshake và chưa kiểm thử |
| REAL PLC TESTED | **NO** |
| Docker runtime tested | **NO** (BLOCKED – máy không có Docker) |
| RCS/AGV thật | **NOT IMPLEMENTED** |
| Điểm mạnh | PER_AGV được bảo đảm cả ở tầng DB (unique index một-task-hoạt-động-trên-AGV), khóa rack bằng unique index, idempotency theo `RequestKey`/`RcsRequestCode`/`(Source,EventKey)`, gọi RCS ngoài transaction, không gửi lại mù sau timeout, ghi PLC ambiguous không retry, phòng thủ Production (từ chối Simulator, không bật ghi PLC), xác thực cookie + re-validate mỗi request + lockout + audit |
| Điểm yếu lớn nhất | **Vòng đời task không thể tiến triển trong Production** (xem P0-1); không có CI; không có test frontend/E2E/hiệu năng/bảo mật tự động; HTTPS chưa có; sao lưu thủ công |

**Câu trả lời ngắn cho "đã hoàn thiện chưa":** chưa. Phần còn thiếu chủ yếu là **tích hợp thực** (RCS, PLC), **nghiệm thu môi trường thực** (Docker, thiết bị) và **hạ tầng chất lượng** (CI, E2E, test frontend). Không phát hiện lỗi nghiệp vụ trong các module được bảo vệ.

---

## 2. Git Baseline

| Mục | Giá trị |
|---|---|
| Branch hiện tại | `develop` |
| HEAD | `939b453` – đúng dự kiến |
| `main` | `d5d692f` – đúng dự kiến |
| Chênh lệch `main..develop` | 56 file, +4830/−79 (toàn bộ là CR-007 logging + test/tài liệu triển khai) |
| Working tree lúc bắt đầu | 5 mục chưa theo dõi (toàn là công việc hôm nay, **không** có thay đổi lên file được theo dõi): `docs/docker-acceptance/`, `docs/archive/audits-2026-11-16/CR-007_Docker_Acceptance_Package_Report.md`, `docs/archive/audits-2026-11-16/CR-007_Docker_Acceptance_Test_Report.md`, `tests/deployment/Test-DockerAcceptancePackage.ps1`, `tests/deployment/fake-docker/` |
| Thay đổi do audit này | Chỉ tạo file báo cáo này. Không commit/push/merge/reset |
| Không có CI | Không có `.github/` hay pipeline nào trong repository |

---

## 3. Architecture Assessment

| Hạng mục | Trạng thái | Ghi chú / bằng chứng |
|---|---|---|
| Phân lớp Domain → Application → Infrastructure → Runtime → Api | **DONE** (L1, L2) | `SolutionStructureTests` đạt; Domain không phụ thuộc hạ tầng; Application chỉ biết hợp đồng (`IRcsClient`, `IPlcAdapter`, `IStationSignalService`) |
| Cô lập adapter thiết bị | **DONE** (L1) | Nghiệp vụ chỉ dùng interface; driver MC nằm trong `Infrastructure/Plc/Mc` |
| Quy mô mã | Backend ≈ 16 200 dòng C# (gồm ~7 000 dòng migration/designer sinh tự động), 59 file TS/TSX frontend |
| Dependency Injection | **DONE** (L1) | Đăng ký rõ ràng theo lớp; Application không tự `new` hạ tầng |
| Chọn adapter theo cấu hình | **PARTIAL** | `Integration:Mode` ≠ `Simulator` → `NotSupportedException` khi khởi động (cố ý). Nghĩa là hiện **không có đường cấu hình** để dùng RCS thật |
| Tính nhất quán Domain/Application/Runtime | **DONE** (L1, L2) | State machine dùng chung bởi scheduler/orchestrator; tập trạng thái chiếm AGV (`AgvOccupyingStates`) trùng với filter của unique index DB |
| `StationSignalService` theo cấu hình | **PARTIAL** | Mặc định `StationSignalSimulator`; `PlcStationSignalService` chỉ khi `Plc:Integration:Enabled=true` (cần quy tắc flow được duyệt – chưa có) |

---

## 4. Backend Assessment

| Hạng mục | Trạng thái | Bằng chứng / nhận xét |
|---|---|---|
| API & validation | **DONE** (L1,L2) | `ValidationFilter` (DataAnnotations) cho DTO; `ExceptionMapping` ánh xạ lỗi nghiệp vụ → 400/401/403/404/409/422, 500 không lộ chi tiết |
| Authentication | **DONE** (L1,L2) | Cookie HttpOnly, SameSite=Strict, `Secure` mặc định (Always ngoài Dev); re-validate người dùng + `SecurityStamp` mỗi request; idle + absolute timeout; lockout; hash PBKDF2 (ASP.NET Identity); chống dò tên tài khoản (hash giả) |
| Authorization | **DONE** (L1,L2) | Fallback policy = bắt buộc đăng nhập; `AdminOnly`/`Operations`; hub SignalR cần `Operations`, nhóm admin riêng cho sự kiện cấu hình |
| CSRF | **DONE** (L1,L2) | Header `X-WCS-Client` bắt buộc với POST/PUT/DELETE + SameSite=Strict |
| Đổi mật khẩu bắt buộc lần đầu | **DONE** (L1,L2) | Chặn mọi API ngoài `/api/auth` |
| Xử lý ngoại lệ & correlation | **DONE** (L1,L2) | `X-Correlation-Id`, request logging đặt trước exception handler |
| Giao dịch & đồng thời | **DONE** (L1,L2,L3) | `EfUnitOfWork`: 1 transaction/đơn vị, xmin optimistic concurrency, tự chạy lại tối đa 12 lần khi xung đột (unique/serialization/deadlock/FK), jitter. Có `ConcurrencyTests` (cần DB – **chưa chạy lại** ở audit này) |
| Background services | **DONE** (L1) | Scheduler 1 s, AGV status 10 s, PLC runtime, handshake 250 ms, seeder, log retention/DB monitor; lỗi một tick không dừng vòng lặp |
| CancellationToken | **DONE** (L1) | Truyền xuyên suốt; `OperationCanceledException` luôn được loại khỏi bắt lỗi chung |
| Retry/timeout/reconnect | **PARTIAL** | PLC: backoff cấp số nhân có trần (L1, test mock). RCS: dispatch tối đa `MaxDispatchAttempts=5`, **query trước khi gửi lại** (L2 với simulator). Timeout theo trạng thái **mặc định tắt** (xem P0-1) |
| Recovery khi khởi động lại | **PARTIAL** | Scheduler phục hồi `Dispatching` chưa có `RcsTaskCode`, `Continuing`, `CancelPending`; `DispatchToRcsAsync` chưa thấy bằng chứng chạy khôi phục trên Docker thật |
| `/healthz` ẩn danh | **DONE** (L1,L3) | Chỉ trả Up/Down của app và DB, timeout 3 s |
| Mật khẩu DB | **DONE** | Không nằm trong source; từ user-secrets/env |
| Tính tương đối của "bị bảo vệ": `HostedServices.cs`, `TaskScheduler`, `TaskStateMachine` | Đã đọc | **Không phát hiện lỗi logic** trong các file này. Ghi chú nhỏ ở mục 12 |

---

## 5. Business Logic Assessment

Đối chiếu với `docs/specification` và quy tắc bảo vệ trong CLAUDE.md. Không suy diễn thêm nghiệp vụ.

| Hạng mục | Trạng thái | Ghi chú |
|---|---|---|
| PER_AGV (một task hoạt động/AGV, hai AGV song song) | **DONE** (L1,L2) | Vừa ở logic (`GetActiveForAgvAsync`) vừa ở DB (`ux_transport_tasks_one_active_per_agv`, partial unique) |
| BP-04 = REVIEW | **DONE – được chặn đúng** (L1,L2) | `FLOW_IN_REVIEW`; `FlowOptions` fail-fast nếu cấu hình có Bp04 |
| A3/A4, D1/D2 UNCHANGED | **DONE** (L1,L2) | Bị từ chối `FLOW_NOT_SUPPORTED`, không đi qua orchestrator |
| WCS sở hữu tồn kho/vị trí | **DONE** (L1,L2) | Reservation + rack unique index khi `Active & SourceRack`; giao dịch tồn kho (`InventoryTransaction`); khai báo/điều chỉnh tồn có audit |
| Không đánh dấu rack "đã đến" chỉ vì task được tạo/dispatch | **DONE** (L1) | Kết thúc/đối soát qua tín hiệu hoàn thành, không qua dispatch |
| Tạo task idempotent (`RequestKey`), callback idempotent (`ProcessedEvent`) | **DONE** (L1,L2) | Unique index |
| Dispatch an toàn (gọi RCS ngoài transaction; không gửi mù sau timeout) | **DONE** (L1,L2) | `RCS_DISPATCH_UNCERTAIN` giữ tài nguyên, yêu cầu đối soát |
| Hủy/đối soát (Admin) | **DONE** (L2 simulator) | `CancelPending` → đối soát qua RCS; `reconcile` Admin |
| Ưu tiên task (priority) | **NOT IMPLEMENTED – cố ý** | FIFO, ghi rõ `NEEDS_CONFIRMATION` (không có chính sách ưu tiên được duyệt). **Không tự suy diễn** |
| Xử lý thiếu nguồn | **PARTIAL** | `NoSourceBehavior` Wait/Fail là cấu hình; chính sách chưa được chủ dự án chốt |
| Timeout từng trạng thái | **PARTIAL** | Có cơ chế, **mặc định không có giá trị** (cần thống nhất với đội PLC/RCS) |
| Quy tắc handshake PLC ↔ flow | **NOT IMPLEMENTED** | Chưa có đặc tả handshake được duyệt; `Plc:Integration:Enabled` giữ tắt |
| Connection Settings (PLC/RCS) | **DONE** (L1,L2) | Bí mật mã hóa Data Protection, không trả về qua API, audit chỉ ghi việc thay thế |
| Thông số Location/Capacity thực | **BLOCKED** | Spec không cung cấp; seed chỉ lấy từ cấu hình, không bịa |

---

## 6. Frontend Assessment

| Hạng mục | Trạng thái | Ghi chú |
|---|---|---|
| Build sạch | **DONE** (L2) | `tsc --noEmit` 0 lỗi; ESLint 0 lỗi |
| Login / đổi mật khẩu bắt buộc | **DONE** (L1; L3 phiên trước) | `App.tsx`: loading → login → ép đổi mật khẩu → routes |
| Dashboard, AGV, Task, Inventory | **DONE ở mức Simulator** (L1; L3 phiên trước) | Dùng `useApi` có loading/error; sự kiện realtime qua SignalR |
| PLC (giám sát, kết nối, IO mapping) | **DONE ở mức Simulator** | Trang cấu hình chỉ Admin; Operator không thấy IP/địa chỉ |
| System Configuration | **DONE** | RCS/PLC/Operations (Admin) |
| Logging & Diagnostics (CR-007) | **DONE ở mức L2/L3 backend**; UI **UNVERIFIED trên Docker** | Chỉ Admin; kiểm tra thị giác trên Docker còn mở (H2/H3/H6) |
| Demo/Test | **DONE** | Demo Mode chạy hoàn toàn trong trình duyệt (engine 642 dòng), tách khỏi SignalR |
| Routing + role-based UI | **DONE** (L1) | `RequireRole` chỉ cải thiện UX; **bảo mật nằm ở server** (đúng) |
| Realtime (SignalR) | **PARTIAL** | Có reconnect tự động và trạng thái; **chưa có test tự động nào** |
| Form validation | **PARTIAL** | `settings/validate.ts`, `passwordPolicy.ts`; server vẫn là nguồn chân lý |
| Test frontend | **NOT IMPLEMENTED** | `package.json` không có vitest/jest/playwright; **0 test** |
| E2E | **NOT IMPLEMENTED** | Không có |
| Responsive/accessibility | **UNVERIFIED** | Có CSS riêng, chưa có bằng chứng kiểm tra trên thiết bị/độ phân giải; không có kiểm tra a11y |
| Error boundary React | **NOT IMPLEMENTED** (L1: không thấy) | Lỗi render có thể làm trắng trang |
| Phụ thuộc phiên bản mới | Lưu ý | TypeScript 6, Vite 8, ESLint 10, React 19.3: build hiện đạt, nhưng khóa phiên bản qua `package-lock.json` cần được giữ |

---

## 7. Equipment Integration Assessment

| Hạng mục | Loại adapter | Trạng thái |
|---|---|---|
| PLC – driver Mitsubishi MC 3E Binary TCP (khung, parser, endpoint, client) | Thật (code) + test với **mock server** (`MelsecMcMockServer`) và oracle tham chiếu | **UNVERIFIED trên thiết bị** – chưa có PLC thật; đề xuất MC 3E là **proposal** chờ kỹ sư PLC |
| Mô hình 4 PLC / 16 Station / 23 tín hiệu | Cấu hình + seed theo workbook | **DONE ở mức dữ liệu/Simulator**; `Actual_PLC_Address` **chưa được gán** (đúng quy định, không suy diễn) |
| Chế độ an toàn PLC (Simulator / RealReadOnly / RealReadWrite) | Thật (code) | **DONE** (L2): ghi chỉ khi đủ khóa (`Write.Enabled`, `EngineerApprovalReference`, `HandshakeApproved`); mặc định `RealReadOnly`, `AllowLiveConnection=false` |
| PLC Worker (1 worker/PLC, độc quyền socket) | Thật | **DONE** (L1,L2 mock): ghi tuần tự; ghi mơ hồ không retry |
| Heartbeat PLC | – | **NOT IMPLEMENTED** – chưa phân loại PLC-scoped/Station-scoped (CLAUDE.md §7.3 yêu cầu quyết định) |
| Handshake coordinator | Thật (code) | **UNVERIFIED**; quy tắc ghi chưa được duyệt, mặc định chỉ quan sát |
| RCS Client | **Chỉ `RcsSimulator`** | **NOT IMPLEMENTED (thật)**: không có HTTP client HIK RCS-2000, không có endpoint callback, không có bộ poll trạng thái |
| AGV communication / đồng bộ trạng thái thiết bị | Simulator | `AgvStatusService` hỏi RCS mỗi 10 s – với simulator luôn trả "Online" ⇒ **trạng thái AGV hiển thị trong Production là giả** |
| SignalR | Thật | **DONE** (L2) – chỉ là kênh UI, không phải kênh thiết bị |
| MQTT | – | Không thuộc phạm vi hiện tại, không có code |
| Kiểm tra kết nối (PLC/RCS test) | Mô phỏng mặc định | `SimulatedConnectionProbe` (không đụng mạng) trừ khi `AllowNetworkProbe=true` – TCP reachability, **không** xác nhận giao thức |

---

## 8. Database Assessment

| Hạng mục | Trạng thái | Ghi chú |
|---|---|---|
| Schema & migration | **DONE** (L1,L3) | 6 migration tuần tự, additive (InitialCreate → ConnectionSettings → Authentication → PlcTopology → McAddressBases → SystemLog); migrator Docker chỉ forward |
| Ràng buộc toàn vẹn | **DONE** (L1,L2) | Unique: `RequestKey`, `RcsRequestCode`, `RcsTaskCode` (partial), `Code`, `Role` AGV, `ExternalRackId` (partial), one-active-per-AGV (partial), one-active-source-reservation-per-rack (partial); check `ck_racks_transit_no_location`, `ck_locations_capacity`; FK `Restrict` trên tài nguyên, `Cascade` chỉ cho lịch sử task |
| Optimistic concurrency | **DONE** (L1,L2) | `xmin` rowversion trên các thực thể chính |
| Index | **PARTIAL** | Có index cho truy vấn chính; **chưa có bằng chứng hiệu năng** với dữ liệu lớn (không có test tải) |
| Seed | **DONE** | AGV1/AGV2 luôn bảo đảm; vị trí chỉ từ cấu hình; admin Production không bao giờ `admin/admin` |
| Retention | **PARTIAL** | `system_log`: 90 ngày (tối thiểu 7). **`audit_log`, `plc_comm_log`, `inventory_transactions`, `task_history` không có chính sách giữ/dọn** – sẽ lớn dần |
| Backup/restore | **PARTIAL / UNVERIFIED** | `backup.ps1` (pg_dump -Fc + `pg_restore --list` xác minh + sao lưu key ring), `restore.ps1` có sẵn; **chưa chạy trên Docker thật**; **không có lịch sao lưu tự động**; không có kiểm thử khôi phục định kỳ |
| Mất key ring Data Protection | Rủi ro | Mất volume `etek_wcs_keys` ⇒ mất khả năng giải mã bí mật RCS đã lưu; có sao lưu key ring nhưng chưa được thử |
| DB thử nghiệm còn tồn tại | **Ghi nhận, không dọn** | `wcs_e2e`, `wcs_test_0e4898eb…`, `wcs_test_65783ebf…` trên PostgreSQL phát triển (không đụng tới trong audit) |
| Dữ liệu thật | **BLOCKED** | Capacity, mã vị trí thật, mã robot RCS: chưa được cung cấp |

---

## 9. Deployment Assessment

| Hạng mục | Trạng thái | Bằng chứng |
|---|---|---|
| Docker Compose (postgres, migrator, backend, frontend) | **UNVERIFIED** | Đã viết kỹ (L1) nhưng **chưa từng build/chạy** (không có Docker) |
| Cô lập mạng | **DONE về thiết kế** (L1) | `db_net` internal (Postgres không ra LAN/host); backend không publish cổng; chỉ Nginx publish cổng; IP Nginx cố định để tin `X-Forwarded-*` chỉ từ proxy |
| Volume bền vững | **DONE về thiết kế** | `etek_wcs_pgdata`, `etek_wcs_keys` đặt tên cố định; tài liệu cấm `down -v` |
| Health check | **DONE về thiết kế** | Postgres `pg_isready`; backend `HEALTHCHECK` + `/healthz`; Nginx `/nginx-health` |
| Dockerfile | **DONE về thiết kế** | Backend chạy `USER app` (không root); multi-stage; frontend Nginx 1.27-alpine |
| Giới hạn tài nguyên & log rotation | **DONE về thiết kế** | `deploy.resources.limits`, json-file 10m×5 |
| Script triển khai | **UNVERIFIED** | `deploy/start/stop/status/backup/restore/prune-logs/plc-route-test`; có log triển khai ra ngoài repo (CR-007); chạy thật chưa có |
| `deploy.ps1` chỉ cho `main` | Lưu ý | Trên `develop` phải dùng `start.ps1` – đã ghi nhận trong gói nghiệm thu |
| Rollback | **PARTIAL** | Tag image theo `WCS_VERSION`, migration chỉ tiến; rollback = chạy lại image cũ + khôi phục backup (quy trình có chữ, **chưa diễn tập**) |
| Bộ nghiệm thu Docker H1–H6 | **BLOCKED** | Bộ công cụ đã chuẩn bị, tự kiểm 49/49 bằng docker giả; **chưa có kết quả trên Docker thật** |
| HTTPS | **NOT IMPLEMENTED** | Nginx chỉ lắng nghe cổng 80, không TLS. Với `Auth__CookieSecure=Always` mặc định, **đăng nhập qua HTTP sẽ thất bại**; muốn dùng thử phải đặt `WCS_COOKIE_SECURE=SameAsRequest` (cookie đi qua HTTP thuần) – chỉ chấp nhận trên LAN thử nghiệm |
| Quản lý bí mật | **PARTIAL** | `.env` ngoài repo (`C:\ETEK-WCS\config`), `.gitignore` chặn `.env`; **mật khẩu truyền qua biến môi trường vào container** (xem được bằng `docker inspect`); không dùng Docker secrets |
| Logging triển khai | **DONE về logic** (L2/L3 với docker giả) | Chưa chạy với Docker thật |

---

## 10. Testing Assessment

| Loại | Số liệu (đã xác minh) | Trạng thái |
|---|---|---|
| Unit (Domain, Plc/MC, Config, Auth service, Logging core) | **308 test chạy và đạt ở audit này** | **DONE** (L2) |
| Integration/API (Postgres thật, Harness tạo DB tạm) | 13 file; tổng backend ở phiên trước 410/410 (trên `main`) | **DONE (L3, phiên trước)**; **không chạy lại ở audit này** (lý do ở trên). Số test CR-007 mới sau 410 **chưa được xác nhận lại tổng thể** |
| Số phương thức `[Fact]/[Theory]` | 314 (Theory mở rộng thêm) trong 28 file có test | |
| Runtime/PLC | `PlcWorkerTests`, `PlcRuntimeIntegrationTests`, `McEndpointTests` với mock server | **DONE với mock**; **BLOCKED với thiết bị thật** |
| Triển khai (PowerShell) | `Test-DeploymentLogging.ps1`, `Test-DockerAcceptancePackage.ps1` (49/49 ở phiên trước, docker **giả**) | **DONE với giả lập**; chưa Docker thật |
| Frontend | **0 test**; chỉ có tsc + eslint | **NOT IMPLEMENTED** |
| E2E | 0 | **NOT IMPLEMENTED** |
| Hiệu năng/tải | 0 | **NOT IMPLEMENTED** |
| Bảo mật tự động (SAST, quét phụ thuộc, pentest) | 0 (chỉ có test đăng nhập/phân quyền ứng dụng) | **NOT IMPLEMENTED** |
| CI | Không có | **NOT IMPLEMENTED** |
| Độ phủ (coverage) | Chưa đo (`coverlet.collector` có sẵn) | **UNVERIFIED** |

---

## 11. Security Assessment

| Hạng mục | Trạng thái | Ghi chú |
|---|---|---|
| Xác thực/ủy quyền, lockout, audit | **DONE** (L1,L2) | Xem mục 4 |
| Phiên | **DONE** | Cookie phiên, hết hạn idle+tuyệt đối; **không dùng JWT** (JWT không áp dụng) |
| CORS | **DONE** | Whitelist theo cấu hình; mặc định không mở nguồn nào; `AllowCredentials` chỉ với nguồn được liệt kê |
| CSRF | **DONE** | Header tùy chỉnh + SameSite=Strict |
| Chống brute-force | **PARTIAL** | Khóa tài khoản theo số lần sai; **không có giới hạn tốc độ theo IP** ở Nginx/ứng dụng (kẻ tấn công vẫn có thể khóa tài khoản người khác – DoS tài khoản) |
| Header bảo mật | **PARTIAL** | Nginx có `nosniff`, `X-Frame-Options`, `Referrer-Policy`; **không có CSP, không HSTS** (chưa có TLS) |
| HTTPS/TLS | **NOT IMPLEMENTED** | Xem mục 9 |
| Bí mật | **PARTIAL** | Bí mật RCS mã hóa bằng Data Protection; LogSanitizer che mẫu; biến môi trường container lộ qua `docker inspect` cho ai có quyền Docker |
| Bề mặt Simulator | **DONE** | API simulator chỉ khi `Integration:Mode=Simulator` **và** `Simulator:ExposeApi` (mặc định chỉ Development), Admin-only, có audit |
| Production không chạy Simulator PLC mà không báo | **DONE** (L2) | `ProductionSafety` + cảnh báo; không cho bật handshake cùng simulator |
| Ghi PLC ngoài ý muốn | **DONE** (L2) | Nhiều khóa, mặc định đọc-chỉ, ghi mơ hồ không retry |
| Khả năng quan sát (logging) | **DONE** (L2/L3 ngoài Docker) | `system_log`, throttling, sanitizer, API chỉ Admin |
| Phụ thuộc/CVE | **UNVERIFIED** | Chưa quét `dotnet list package --vulnerable` / `npm audit` trong audit này |

---

## 12. Technical Debt

1. **Không có CI**: mọi bằng chứng test là chạy tay trong phiên làm việc; không có cổng chặn hồi quy.
2. **Test frontend = 0**; các hành vi realtime/validate/role-UI chỉ được kiểm bằng tsc/eslint và duyệt tay.
3. `Endpoints.cs` (286 dòng) gom nhiều nhóm API vào một hàm; `TaskOrchestrator` 665 dòng – chấp nhận được nhưng khó đọc (không đề xuất sửa các module được bảo vệ khi chưa có phê duyệt).
4. `StationRequestProcessor` chỉ bắt `AppException`: nếu `CreateTaskAsync` ném loại lỗi khác (ví dụ `DomainException`), request đã bị lấy khỏi hàng đợi ở `PollRequestsAsync` có thể **mất** (Simulator) hoặc lặp lại (PLC thật). *Chưa chứng minh bằng test; ghi nhận để xem xét – module nằm ngoài danh sách cấm nhưng gắn với intake nghiệp vụ.*
5. `TaskScheduler` truy vấn 7 danh sách trạng thái mỗi giây (giới hạn 100/500 dòng) – ổn với quy mô hiện tại, **chưa đo tải**.
6. Retention không đồng nhất giữa các bảng log/lịch sử (mục 8).
7. Docker/`deploy.ps1` gắn với nhánh `main` – cần quy trình phát hành (release) rõ ràng.
8. Phiên bản công cụ rất mới (TS 6/Vite 8/ESLint 10) – cần khóa và kiểm soát nâng cấp.
9. Dữ liệu thử nghiệm tồn đọng trong PostgreSQL phát triển (3 DB) – dọn thủ công sau khi chủ dự án đồng ý.

---

## 13. Missing Features (so với mục tiêu vận hành thật)

| # | Tính năng | Trạng thái | Phụ thuộc |
|---|---|---|---|
| 1 | RCS adapter thật (HIK RCS-2000: create/continue/cancel/query, xác thực, ánh xạ mã trạng thái 0..10) | NOT IMPLEMENTED | Tài liệu API RCS, môi trường RCS |
| 2 | Đường đưa trạng thái từ RCS về WCS (callback có xác thực + idempotent **hoặc** poll trạng thái) | NOT IMPLEMENTED | #1 |
| 3 | Địa chỉ thiết bị PLC thực (`Actual_PLC_Address`) cho 4×4 trạm, thông số MC/QJ71E71-100 | BLOCKED | Kỹ sư PLC |
| 4 | Đặc tả handshake được duyệt + chủ sở hữu reset + timeout; phân loại heartbeat | BLOCKED | Chủ dự án + kỹ sư PLC (Change Request) |
| 5 | HTTPS (TLS termination, HSTS, cookie `Secure`) | NOT IMPLEMENTED | Chứng chỉ/hạ tầng |
| 6 | Sao lưu tự động + diễn tập khôi phục | NOT IMPLEMENTED | Docker nghiệm thu |
| 7 | CI/CD, test frontend, E2E, test tải | NOT IMPLEMENTED | – |
| 8 | Chính sách ưu tiên task; timeout theo trạng thái | BLOCKED (chưa duyệt) | Chủ dự án |
| 9 | Retention cho audit/PLC log/lịch sử | NOT IMPLEMENTED (cần quyết định) | Chủ dự án |
| 10 | Giới hạn tốc độ đăng nhập theo IP | NOT IMPLEMENTED | – |
| 11 | Cảnh báo/thông báo vận hành (email/Teams/…) | NOT IMPLEMENTED (không có trong baseline) | Xác nhận phạm vi |
| 12 | Dữ liệu thật: vị trí, sức chứa, mã robot | BLOCKED | Chủ dự án |
| 13 | BP-04 | **REVIEW – cố ý chưa làm** | Chủ dự án |

---

## 14. Critical Risks

| ID | Mức | Rủi ro | Bằng chứng |
|---|---|---|---|
| **P0-1** | **P0 – Critical** | **Trong Production task không thể tiến triển quá `ACCEPTED`.** `IRcsClient` luôn là `RcsSimulator`; mọi tín hiệu tiến triển (`HandleSignalAsync`) chỉ được gọi từ `SimulationDriver` (API simulator – mặc định **tắt** ngoài Development) và từ luồng hủy nội bộ. Không có callback/poll RCS. Timeout theo trạng thái mặc định **tắt**. Hậu quả: một task tạo trong Production sẽ treo ở `ACCEPTED`/`EXECUTING`, **chiếm AGV vĩnh viễn** (PER_AGV) và khóa rack; hệ thống hiển thị AGV "Online" giả | `Infrastructure/DependencyInjection.cs` (`Integration:Mode` khác Simulator ⇒ throw), `Program.cs` (simulator API điều kiện), grep `HandleSignalAsync` (chỉ `SimulationDriver` + nội bộ), `SchedulerOptions` (timeout null) |
| P0-2 | P0 | **Không được đưa vào vận hành thật** cho tới khi có P0-1 + PLC thật. Hiện chỉ phù hợp "thử nghiệm có giám sát, không PLC, không RCS thật" | Executive Summary |
| P1-1 | P1 | Docker **chưa được kiểm chứng thật**; toàn bộ H1–H6 và deploy/backup/restore là L1 | `docs/DEPLOYMENT_TEST_CHECKLIST.md`, báo cáo nghiệm thu |
| P1-2 | P1 | Không có HTTPS; cấu hình mặc định (`Cookie Secure=Always`) **không đăng nhập được qua HTTP**; cấu hình thay thế đưa cookie phiên qua HTTP thuần | `nginx.conf`, `docker-compose.yml` |
| P1-3 | P1 | Không có CI và không có test frontend/E2E: hồi quy khó phát hiện | repo |
| P1-4 | P1 | Backup chỉ thủ công, chưa từng chạy thật; mất volume key ring ⇒ mất bí mật RCS đã lưu | `backup.ps1` |
| P1-5 | P1 | PLC thật chưa kiểm thử; thông số/địa chỉ/handshake chưa có (BLOCKED, đúng quy định, nhưng là điều kiện cần cho vận hành) | CLAUDE.md §7, báo cáo CR-002/003 |
| P2-1 | P2 | Dữ liệu thật (vị trí/sức chứa/mã robot) chưa có – hệ thống không có trạm/tồn kho thật để chạy | seed |
| P2-2 | P2 | Không có giới hạn tốc độ đăng nhập theo IP; khóa tài khoản có thể bị lợi dụng | `AuthService` |
| P2-3 | P2 | Retention không có cho `audit_log`, `plc_comm_log`, `task_history`, `inventory_transactions` | EF/Runtime |
| P2-4 | P2 | Bí mật qua biến môi trường container; không CSP/HSTS | compose, nginx |
| P2-5 | P2 | Intake yêu cầu trạm có thể mất yêu cầu khi lỗi không phải `AppException` (chưa chứng minh bằng test) | `StationRequestProcessor.cs` |
| P3-1 | P3 | Không có error boundary React; a11y/responsive chưa kiểm | frontend |
| P3-2 | P3 | Phụ thuộc phiên bản rất mới; chưa quét CVE | package.json |
| P3-3 | P3 | 3 DB thử nghiệm tồn đọng; thư mục tạm `%TEMP%\wcs-e2e-deploylogs` | môi trường dev |
| P3-4 | P3 | Hiệu năng scheduler/DB chưa đo | – |

> **Không phát hiện lỗi** trong `TaskStateMachine`, `TaskOrchestrator`, `InventoryService`, `TaskScheduler`, `FlowOptions`, `PlcWorker`, `McPlcEndpoint`, `McTcpClient`, MC frame builder/parser, `StationIoHandler`, `HandshakeCoordinator`, `HostedServices.cs` trong phạm vi đã đọc; lưu ý P0-1 là **thiếu tính năng tích hợp**, không phải lỗi trong các module đó. `StationIoHandler`, `HandshakeCoordinator`, `McFrame` chỉ được đánh giá qua test (L2), chưa đọc lại dòng-dòng.

---

## 15. Recommended Completion Roadmap

Thứ tự theo phụ thuộc (không bắt đầu nếu chưa có phê duyệt):

| Giai đoạn | Công việc | Phụ thuộc | Ghi chú |
|---|---|---|---|
| **0 – Quyết định (không cần code)** | Chủ dự án xác nhận: thứ tự ưu tiên; tài liệu API RCS; chính sách timeout/priority/no-source; retention; quy mô phát hành (nhánh/tag); chấp nhận HTTP LAN thử nghiệm hay yêu cầu HTTPS | – | Mở khóa mọi thứ phía sau; mỗi thay đổi nghiệp vụ qua Change Request |
| **1 – Nghiệm thu Docker (đã có công cụ)** | Chạy `docs/docker-acceptance` H1–H6 trên máy Docker thật; sửa lỗi phát hiện; ghi nhận bằng chứng | Máy Windows 11 + Docker | Giải phóng P1-1; cũng kiểm backup/restore (P1-4) |
| **2 – Hạ tầng chất lượng** | CI (build + 308 test không DB + tsc/eslint; test DB trong container PostgreSQL tạm); test frontend (Vitest + Testing Library) và vài E2E (Playwright) cho luồng chính; quét phụ thuộc | Giai đoạn 0 | Giải quyết P1-3; có thể song song với 1 |
| **3 – HTTPS & hardening** | TLS ở Nginx, HSTS, CSP, giới hạn tốc độ đăng nhập, quản lý bí mật; lịch sao lưu tự động | Chứng chỉ; 1 | P1-2, P2-2, P2-4 |
| **4 – RCS thật** (CR mới) | Adapter HIK RCS-2000 + callback có xác thực/idempotent + poll đối chiếu + timeout trạng thái được duyệt; kiểm thử với RCS thử nghiệm | **Tài liệu API RCS + môi trường RCS** (BLOCKED hiện tại) | **Giải quyết P0-1**; phải qua Change Request; không làm trước khi được duyệt |
| **5 – PLC thật** (CR) | Kỹ sư PLC cung cấp cổng/thông số MC/địa chỉ thực; commissioning chỉ-đọc → duyệt từng tín hiệu ghi; đặc tả handshake & heartbeat | Kỹ sư PLC; 1 | Giải quyết P1-5; vẫn cấm ghi tín hiệu an toàn để "thử" |
| **6 – Dữ liệu thật & vận hành** | Nạp vị trí/sức chứa/mã robot; retention; giám sát/cảnh báo; tài liệu vận hành; đào tạo | 4, 5 | P2-1, P2-3 |
| **7 – Quyết định BP-04** | Chủ dự án duyệt/ không duyệt | – | Tuỳ chọn, độc lập |

Đường găng tới vận hành thật: **0 → 4 → 5 → 6**; Giai đoạn 1–3 chạy song song và giảm rủi ro nhưng không thay thế 4/5.

---

## Phụ lục – đề nghị phê duyệt tiếp theo
Báo cáo dừng ở AUDIT. Đề nghị chủ dự án chọn: (a) cho phép chạy nghiệm thu Docker H1–H6 (không đổi code); (b) mở Change Request cho CI + test frontend; (c) cung cấp tài liệu API RCS để mở CR tích hợp RCS (giải quyết P0-1); (d) chốt các quyết định ở Giai đoạn 0. Không có sửa đổi nào được thực hiện trong audit này ngoài tạo file báo cáo.
