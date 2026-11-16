# ETEK WCS – Current State Audit & Implementation Status Report

| Mục | Giá trị |
|---|---|
| Thời điểm audit | 2026-11-16, 17:13 (UTC+07:00) |
| Branch được audit | `main` |
| Commit được audit | `d5d692fd4c6a4879b2872ce38049833984d01036` – "feat(deploy): CR-006 completion - trust X-Forwarded-* only from the designated proxy, key-ring and HTTPS guidance" |
| Baseline Deployment đã xác nhận | `d5d692f…` → **HEAD trùng baseline** |
| Working tree trước audit | **Sạch** (0 dòng `git status --short`) |
| Phạm vi | AUDIT chỉ đọc. Không sửa source/tài liệu, không migration, không thay đổi DB, không commit/push/merge/checkout, không fetch/pull, không dừng/khởi động service |
| Người thực hiện | Claude Code (Senior Architect / DevOps / Tech Lead) |

## Quy ước

**Trạng thái:** `DONE` · `PARTIAL` · `NOT IMPLEMENTED` · `NOT VERIFIED` · `BLOCKED`.

**Mức bằng chứng** (mỗi kết luận phải thuộc một mức):

| Mức | Ý nghĩa |
|---|---|
| L1 – Đã lập trình | Có mã nguồn / cấu hình trong repo (đã đọc trong audit này) |
| L2 – Test tự động | Có test tự động pass ở lần chạy gần nhất trong phiên làm việc (xem mục Test Status) |
| L3 – Kiểm chứng ngoài container | Chạy thật ứng dụng/migration trên máy dev (không có Docker, không có thiết bị) |
| L4 – Kiểm chứng môi trường thực | Chạy trên Docker/PLC/RCS/AGV thật. **Hiện chưa có hạng mục nào đạt L4** |

**Giới hạn của audit này (trung thực):**
- Bộ test backend/frontend **không được chạy lại** trong audit vì sẽ build lại binary và tạo/xóa database tạm trên PostgreSQL đang chạy. Số liệu test là kết quả của lần chạy gần nhất trong phiên (410/410, trên commit này); mã nguồn không đổi từ đó (lần merge cuối là fast-forward).
- Kết luận về hành vi Production của RCS Simulator được **suy ra từ đọc mã**, chưa chạy end-to-end ở Production.
- Docker không có trên máy → mọi hạng mục runtime Docker là NOT VERIFIED.

---

## 1. Executive Summary

| Câu hỏi | Trả lời |
|---|---|
| Phiên bản hiện tại | `main` @ `d5d692f` (CHANGELOG v2.0 – CR-006) |
| Giai đoạn dự án | Phần mềm hoàn thiện ở mức **Simulator/Mock** + gói triển khai Docker đã viết. **Giai đoạn pre-commissioning**: chưa có PLC thật, RCS/AGV thật, Docker chưa chạy |
| Mức hoàn thành tổng thể (ước lượng của người audit, không đo được) | Khoảng 60–65% đường tới vận hành thực. Lõi phần mềm đầy đủ; phần còn lại bị chặn bởi thông số PLC, RCS thật, nghiệm thu Docker, HTTPS |
| Có thể triển khai chưa | **Chưa cho vận hành.** Có thể thử nghiệm có giám sát sau khi chạy checklist Docker, với cấu hình mặc định (`RealReadOnly`, không nối PLC thật) |
| REAL PLC TESTED | **NO** |
| Docker runtime tested | **NO** (NOT VERIFIED) |
| RCS/AGV thật | **NOT IMPLEMENTED** (chỉ có `RcsSimulator`) |

**5 điểm quan trọng nhất:**
1. Lõi WCS (task/scheduler PER_AGV/tồn kho/auth/UI) hoàn chỉnh ở Simulator và có test tự động (410/410 lần chạy cuối).
2. Không có RCS adapter thật; Production vẫn chạy `RcsSimulator` → rủi ro HIGH về dữ liệu giả (mục 11, #3).
3. PLC: driver MC 3E Binary TCP đã viết và test với mock; **chưa có thiết bị thật**, thiếu cổng TCP, tham số MC, địa chỉ 368 IO và đặc tả handshake.
4. Gói Docker (compose, Dockerfile, Nginx, 8 script) đã có nhưng **chưa từng được build/chạy trên Docker**.
5. Không có E2E trong repo, không có CI.

---

## 2. Git & Source Code Status

| Hạng mục | Kết quả (đã xác minh) |
|---|---|
| Current branch | `main` |
| HEAD | `d5d692fd4c6a4879b2872ce38049833984d01036` |
| Local / tracking | `main` = `origin/main` = `d5d692f`; `develop` = `origin/develop` = `d5d692f` (ref remote là bản đã có sẵn trong repo local; **không fetch** trong audit) |
| Working tree | Sạch; không modified/untracked; không stash; không tag |
| `LoginPage.tsx` | Đã commit (`79957d5`, thay đổi riêng của người dùng) |
| `reference/` | Đã commit (`fdd3d7e`, 10 file nguồn thư viện MC); `*.zip` và `obj/` bị `.gitignore` loại (không theo dõi) |
| Số commit | 34 |
| Quy mô backend (C#, không gồm migration/test) | Domain 997 dòng/16 file · Application 3.732/30 · Infrastructure 2.285/16 · Runtime 140/2 · Api 966/8 |
| Test | 30 file, `[Fact]`=265, `[Theory]`=27 (số ca thực tế khi chạy: 410) |
| Migration | 5: `InitialCreate`, `AddConnectionSettings`, `AddAuthentication`, `AddPlcTopology`, `AddMcAddressBasesAndTimeouts` |
| Endpoint | 54 khai báo `Map*` (chưa tính SignalR hub) |
| Hosted services | 5: `DatabaseSeedHostedService`, `PlcRuntimeHostedService`, `HandshakeHostedService`, `SchedulerHostedService`, `AgvStatusHostedService` |
| Lịch sử gần nhất | `d5d692f` CR-006 hoàn tất · `ab56975` .gitattributes · `e7f5a55` docs CR-006 · `df30a74` gói deployment · `03c5886` backend CR-006 · `fdd3d7e` reference · `79957d5` LoginPage · `c1c2350` CR-004 acceptance · `d90b2c8` CR-004 · `3db0694` CR-003 |

Source code **đúng Deployment Baseline** đã xác nhận (HEAD = `d5d692f`).

---

## 3. Implementation Status (tổng hợp)

| Module | Trạng thái | Mức bằng chứng cao nhất | Ghi chú ngắn |
|---|---|---|---|
| Backend lõi | DONE (simulator) | L2 | Xem mục 4 |
| AGV / RCS | PARTIAL | L2 (simulator) | RCS thật NOT IMPLEMENTED |
| PLC / MC Protocol | PARTIAL / BLOCKED | L2 (mock), L3 | Chưa có thiết bị; thiếu thông số |
| Frontend | DONE | L2/L3 (E2E ngoài repo) | Không có test frontend trong repo |
| Docker / Deployment | PARTIAL | L1 + kiểm tra tĩnh + L3 một phần | Runtime NOT VERIFIED |
| Tài liệu / CR | PARTIAL | – | Có lệch (mục 10) |

---

## 4. Backend Status

| Module | Chức năng | Trạng thái | Bằng chứng (file / class / test) | Việc còn thiếu |
|---|---|---|---|---|
| WCS.Domain | Entity, TaskStateMachine, Inventory domain | DONE | `src/backend/WCS.Domain/Tasks/TaskStateMachine.cs`, `TaskState.cs`; test `Domain/TaskStateMachineTests`, `Domain/InventoryDomainTests` (L2) | – |
| WCS.Application | Services (TaskOrchestrator, TaskScheduler, InventoryService, AgvStatusService, PlcConfigService…) | DONE | `WCS.Application/Services/*`, `Plc/*`; 3.732 dòng | – |
| WCS.Infrastructure | EF Core, repositories, Auth, Config, Simulators, MC driver | DONE (simulator) | `Persistence/`, `Auth/`, `Plc/Mc/*`, `Simulation/Simulators.cs` | – |
| WCS.Runtime | Hosted services | DONE | `WCS.Runtime/HostedServices.cs`, `DependencyInjection.cs` | – |
| WCS.Api | Endpoints, SignalR hub, Program | DONE | `Api/Endpoints.cs`, `Api/PlcEndpoints.cs`, `Api/AuthSetup.cs`, `Program.cs`; 54 endpoint | – |
| Authentication / Authorization | Cookie session, Admin/Operator policy, lockout, idle/absolute timeout, header `X-WCS-Client` | DONE | `AuthSetup.cs`; `Integration/AuthIntegrationTests`, `Auth/AuthApiTests` (L2) | HTTPS/TLS NOT IMPLEMENTED |
| Admin / Operator permissions | Admin-only: cấu hình PLC/RCS, accounts, audit; Operator: giám sát và thao tác task theo CR-001 | DONE | `PlcCr004Tests` (Operator 403) (L2) | – |
| Database / EF / Migrations | 5 migration, PostgreSQL 16 | DONE | DB dev `wcs_db`: `__EFMigrationsHistory` có đủ 5 bản ghi (SELECT, đọc trong audit – L3) | Migration trong container NOT VERIFIED |
| SignalR | Hub `/hubs/wcs`, `SignalRWcsNotifier` | PARTIAL | `Program.cs` (`MapHub<WcsHub>`) (L1); E2E qua Vite ở phiên trước (L3, script ngoài repo) | **Qua Nginx: NOT VERIFIED** |
| Background services | Seed, PLC runtime, Handshake, Scheduler, AgvStatus | DONE | `HostedServices.cs` | Chạy một replica duy nhất (thiết kế) |
| Logging / Audit | Serilog console; bảng `audit_log`; `Audited.RunAsync` | DONE | `AuditLog` (Infrastructure/Auth), bảng `audit_log` trong DB | Đăng nhập sai log mức ERROR kèm stack trace (LOW) |
| Health Check | `GET /healthz` (ẩn danh, app/DB tách riêng); `GET /api/system/health` (cần đăng nhập) | DONE | `Program.cs`; `Integration/Cr006DeploymentTests` (L2); chạy ngoài container: 200 (L3) | Trong Docker NOT VERIFIED |
| Production safety | Từ chối `Plc:Mode=Simulator` ở Production (trừ `Plc:AllowSimulatorInProduction=true`); API mô phỏng/test-write 409 ở Production; `Auth:CookieSecure`; forwarded headers chỉ từ proxy chỉ định | DONE (test) | `Api/ProductionSafety.cs`, `Api/ForwardedHeadersSetup.cs`, `Api/PlcEndpoints.cs` (`RequireNotProduction`); `Cr006DeploymentTests`, `PlcCr004Tests` (L2); smoke ngoài container (L3) | Trong Docker NOT VERIFIED |
| Seed | AGV1/AGV2 luôn được tạo; Location chỉ seed khi `Seed:Enabled=true` (mặc định false) | PARTIAL | `Seeding/WcsDataSeeder.cs`; DB dev: agvs=2, locations=0 | Capacity thật chưa xác nhận; chưa có hướng dẫn khởi tạo vị trí (MEDIUM) |

---

## 5. AGV / RCS Status

| Chức năng | Trạng thái | Bằng chứng | Việc còn thiếu |
|---|---|---|---|
| RCS Client thật (HIK RCS-2000) | **NOT IMPLEMENTED** | Chỉ có `RcsSimulator : IRcsClient` (`Infrastructure/Simulation/Simulators.cs`). `Infrastructure/DependencyInjection.cs` ném `NotSupportedException` nếu `Integration:Mode` khác `Simulator` | Thông số/API RCS; CR mới |
| RCS Simulator | DONE | `RcsSimulator` (idempotent theo request code, fault injection); test `Integration/RobustnessTests` (L2) | – |
| AGV monitoring / status | PARTIAL | `Application/Services/AgvStatusService.cs`: chỉ truy vấn AGV có `RcsRobotCode`; `RcsSimulator.QueryAgvStatusAsync` luôn trả Online "simulated" | Trạng thái AGV thật cần RCS thật |
| Task creation / dispatch | DONE (simulator) | `TaskOrchestrator`, `TaskScheduler`; `Integration/ProcessFlowTests` (L2) | Dispatch tới AGV thật NOT VERIFIED |
| Task State Machine | DONE | `Domain/Tasks/TaskStateMachine.cs`; `Domain/TaskStateMachineTests` (L2) | – |
| Scheduler | DONE (simulator) | `SchedulerHostedService`, `TaskScheduler` (FIFO – ghi rõ NEEDS_CONFIRMATION trong mã) | Chính sách thứ tự chưa được duyệt |
| Task concurrency PER_AGV | DONE | `Application/Services/TaskOrchestrator.cs:125`; partial unique index `Infrastructure/Persistence/Configurations/EntityConfigurations.cs:138`; `Integration/ConcurrencyTests` (L2) | – |
| AGV1 / AGV2 | DONE | `WcsDataSeeder` (AGV1 = FullRack, AGV2 = EmptyRack); DB dev agvs=2 | Robot code thật NEEDS_CONFIRMATION |
| Rack assignment, Inventory | DONE (simulator) | `Application/Services/InventoryService.cs`; `Domain/InventoryDomainTests` (L2) | – |
| Station / Location management | DONE | API và UI quản lý vị trí; DB dev: locations=0 | Capacity thật |
| `WAITING_FOR_DESTINATION` | DONE | `TaskState.WaitingForDestination`; cạnh trong `TaskStateMachine.cs`; `ProcessFlowTests` (L2) | – |
| BP-04 (slide 8) | **Không triển khai theo chủ ý** (REVIEW) | `Application/Flows/FlowOptions.cs` từ chối khóa `Bp04` | Chờ phê duyệt nghiệp vụ |
| A3/A4, D1/D2 | UNCHANGED theo CR (không thiết kế lại) | `FlowOptions.cs` | – |
| Kiểm thử AGV thật | **NOT VERIFIED** | Không có thiết bị | – |

---

## 6. PLC / MC Protocol Status

| Chức năng | Trạng thái | Bằng chứng | Việc còn thiếu |
|---|---|---|---|
| Topology 4 PLC / 16 Station | DONE | DB dev: `plc_connections`=4, `plc_stations`=16 (SELECT) | – |
| 368 IO points | DONE (danh mục) | DB dev: `station_io_points`=368; `Infrastructure/Plc/Catalog/plc-io-catalog.json` (không chứa địa chỉ thật) | Địa chỉ thật **BLOCKED** |
| Cấu hình PLC (PlcConnection) | PARTIAL | `Application/Plc/PlcConfigService.cs`; UI *Kết nối PLC* | Cổng TCP, tham số MC, QJ71E71-100: **BLOCKED** (chờ kỹ sư PLC) |
| PlcWorker | DONE (simulator/mock) | `Application/Plc/PlcWorker.cs`; `Plc/PlcWorkerTests` (L2) | Thiết bị thật NOT VERIFIED |
| StationIoHandler | DONE | `Application/Plc/*`; test `Plc/PlcConfigTests`, `Plc/HandshakeTests` (L2) | – |
| IPlcEndpoint / `SimulatedPlcEndpoint` | DONE | `Infrastructure/Plc/PlcRuntimeInfrastructure.cs` | – |
| `McPlcEndpoint` (MC 3E Binary TCP) | PARTIAL | `Infrastructure/Plc/Mc/*`; test `Plc/McProtocolTests`, `McEndpointTests`, `McReferenceParityTests`, `McCr003Tests` (parity byte-by-byte với thư viện tham chiếu, mock TCP server) (L2) | **REAL PLC TESTED = NO** |
| Read / Write | PARTIAL | Đọc theo lô; ghi cần khóa phê duyệt; ghi mơ hồ không retry | NOT VERIFIED với thiết bị |
| Chế độ SIMULATOR / REAL_READ_ONLY / REAL_READ_WRITE | DONE (logic) | `WritePolicy.EffectiveMode` (`PlcConfigService.cs`); REAL_READ_WRITE cần `Plc:Write.Enabled`, `EngineerApprovalReference`, `HandshakeApproved`; compose **không** truyền các khóa này | Chưa thử thật |
| Address validation | DONE | `PlcConfigService.SetAddressAsync`; `Plc/McAddressTests`, `Integration/PlcMcIntegrationTests` (L2) | – |
| Pollable / approval | DONE | `StationIoPoint.IsPollable` = Approved và có địa chỉ; `PlcCr004Tests` (L2) | Địa chỉ thật chưa có |
| Thiết bị X/Y | Ngoài phạm vi | API từ chối `X*`/`Y*` (OUT_OF_SCOPE / NEEDS_CONFIRMATION) | Xác nhận cơ số (CR riêng) |
| Thiết bị W | PARTIAL | Hỗ trợ trong driver nhưng bị từ chối tới khi xác nhận cơ số W | Xác nhận cơ số |
| Handshake | **BLOCKED** | `HandshakeCoordinator` có mã; `Plc:Integration:Enabled` mặc định tắt; không có đặc tả handshake duyệt (`docs/specification/business-rules.md:122`) | Sheet handshake được duyệt |
| Production safety (PLC) | DONE (test) | Mặc định compose `PLC_MODE=RealReadOnly`, `PLC_ALLOW_LIVE_CONNECTION=false` → 4 PLC `Disabled`, không socket (smoke ngoài container, L3) | – |
| Admin / Operator | DONE | Operator 403 trên API cấu hình/IO (`PlcCr004Tests`) | – |
| PLC communication log | DONE | Bảng `plc_comm_log`; tab Nhật ký (Admin) | – |
| Dữ liệu trong DB dev | Ghi nhận | `station_io_points`: **2 địa chỉ đã nhập, 0 đã phê duyệt** (SELECT) | – |

---

## 7. Frontend Status

| Chức năng | Trạng thái | Bằng chứng | Việc còn thiếu |
|---|---|---|---|
| Dashboard | DONE | `src/frontend/wcs-web/src/pages/DashboardPage.tsx` | – |
| AGV Monitoring | DONE | `AgvPage.tsx` | Dữ liệu thật cần RCS thật |
| Task Monitoring | DONE | `TasksPage.tsx`, `TaskDialogs.tsx` | – |
| Location / Inventory | DONE | `InventoryPage.tsx` | – |
| PLC Monitoring (chỉ đọc) | DONE | `PlcPage.tsx`, `plc/StationPanel.tsx`, `plc/PlcLogsTab.tsx` | – |
| PLC Connections | DONE | `PlcConnectionsPage.tsx` | – |
| PLC IO Mapping (368 IO) | DONE | `PlcIoMappingPage.tsx` | – |
| System Configuration | DONE | `ConfigPage.tsx`, `config/RcsTab.tsx`, `config/OperationsTab.tsx` | – |
| Authentication, Users, Audit, đổi mật khẩu | DONE | `LoginPage.tsx`, `UsersPage.tsx`, `AuditPage.tsx`, `ChangePasswordPage.tsx` | – |
| Admin / Operator UI | DONE | Route guard `RequireRole`, menu theo quyền | – |
| Engineering tools | DONE | `src/config.ts` `ENGINEERING_TOOLS` (chỉ `npm run dev` hoặc `VITE_ENABLE_ENGINEERING_TOOLS=true`); bản `npm run build` không hiện | – |
| Production UI behavior | DONE | Header PLC/RCS "Chưa kết nối" khi chỉ có simulator; banner trên mọi màn hình; thẻ PLC "Chưa kết nối" | Không chặn nghiệp vụ ở backend (xem Open Issue #3) |
| Test frontend tự động | **NOT IMPLEMENTED** | `package.json` không có `test`; không có file e2e/vitest trong repo | Đưa E2E vào repo |
| E2E (đã chạy trong phiên) | PARTIAL | CR-004: 44 PASS/0 FAIL; dọn UI: 10/10 (L3, script **nằm ngoài repo**) | Không tái lập được từ repo |

---

## 8. Docker & Deployment Status

**Môi trường thực tế đã kiểm tra (chỉ đọc):**

| Mục | Kết quả |
|---|---|
| `docker` trong PATH | **Không có** |
| Docker Desktop | Không có file cài `C:\Program Files\Docker\Docker\Docker Desktop.exe` |
| Tiến trình docker/vmmem/wsl | Không có |
| `wsl --status` | Không trả nội dung (NOT VERIFIED) |
| Container / image / volume / network / port mapping | **NOT VERIFIED** (không có Docker engine) |
| PostgreSQL 16 trên máy | Service `postgresql-x64-16` Running (cổng 5432 – dùng cho dev, **không phải** container) |
| DB trên máy | `postgres`, `wcs_db`, `Gateway_DAT`, `Gateway_station_dat` (hai DB cuối không thuộc dự án này) |
| API dev 5007 / Vite 5173 | **Không chạy** tại thời điểm audit (không khởi động lại theo yêu cầu) |
| dotnet / node | 10.0.403 / v24.19.0 |

**Thành phần Docker:**

| Thành phần | File | Đã kiểm chứng | Trạng thái |
|---|---|---|---|
| Compose architecture (postgres, migrator, backend, frontend; `db_net` internal, `edge_net`) | `deployment/docker-compose.yml` (116 dòng) | YAML parse; chỉ `frontend` có `ports`; `db_net.internal=true`; không privileged/host network; mọi `${VAR}` có trong `.env.example`; log rotation 10m×5 | PARTIAL (tĩnh) |
| PostgreSQL persistence | volume `etek_wcs_pgdata` | Chưa chạy | NOT VERIFIED |
| Migrator / migration bundle | `deployment/docker/backend/Dockerfile` (stage `bundle`, `migrator`) | Bundle build và áp 5 migration vào DB trống ngoài Docker, môi trường Production (L3) | PARTIAL |
| Backend container | Dockerfile (stage `runtime`, user `app`, healthcheck bash `/dev/tcp`, `/keys`) | Chưa build; image base có `bash` chưa xác minh | NOT VERIFIED |
| Frontend / Nginx | `deployment/docker/frontend/Dockerfile`, `nginx.conf` | Chưa build | NOT VERIFIED |
| API reverse proxy | `nginx.conf` `/api/` | Chưa thử | NOT VERIFIED |
| SignalR WebSocket proxy | `nginx.conf` `/hubs/` (Upgrade, timeout 1h) | Chưa thử | NOT VERIFIED |
| Docker network isolation | compose | Chỉ kiểm tra tĩnh; test từ máy khác chưa làm | NOT VERIFIED |
| Data Protection Keys persistence | volume `etek_wcs_keys`, `/keys` owner `app` | Chưa chạy | NOT VERIFIED |
| Trusted Forwarded Headers | `ForwardedHeadersSetup.cs`; compose cố định IP Nginx `172.30.0.10/32` | Ngoài container: nguồn không tin → bỏ qua; nguồn tin → dùng; thiếu danh sách → từ chối khởi động; unit test (L2/L3). Nginx ghi đè header: NOT VERIFIED | PARTIAL |
| Production safety | `ProductionSafety.cs` | Test backend (L2); smoke (L3) | PARTIAL |
| Environment configuration | `deployment/.env.example` | Có; `.env` thật ngoài repo (`C:\ETEK-WCS\config\.env`) | DONE (tĩnh) |
| Deploy / Rollback | `deployment/scripts/deploy.ps1`, README mục 6 | Script parse không lỗi, ASCII; logic `.env` thử 6 ca; **chưa chạy Docker** | NOT VERIFIED |
| Backup / Restore | `backup.ps1`, `restore.ps1` | Parse không lỗi; **chưa chạy** | NOT VERIFIED |
| Start / Stop / Status | `start.ps1`, `stop.ps1`, `status.ps1` | Parse không lỗi; chưa chạy | NOT VERIFIED |
| PLC route test | `plc-route-test.ps1` (bắt buộc `-Port`, chỉ mở/đóng TCP) | Chưa chạy; chưa có PLC/cổng | NOT VERIFIED |
| Checklist nghiệm thu | `docs/DEPLOYMENT_TEST_CHECKLIST.md` (A1–G4) | **Toàn bộ cột kết quả trống** | NOT VERIFIED |
| ADR | `docs/decisions/ADR/ADR-0005-docker-deployment.md` | Có (12 quyết định) | DONE (tài liệu) |
| HTTPS/TLS | – | – | **NOT IMPLEMENTED** |
| Windows 11 deployment readiness | – | – | **Chưa nghiệm thu** |

---

## 9. Test Status

| Hạng mục | Kết quả | Mức bằng chứng | Ghi chú |
|---|---|---|---|
| Backend build | OK (lần build cuối) | L2 | Không build lại trong audit |
| Backend unit + integration | **410 pass / 0 fail** (lần chạy gần nhất, commit này) | L2 | Không chạy lại trong audit (xem Giới hạn) |
| Frontend ESLint / `tsc -b` / `build` / `build:demo` | Sạch ở lần chạy gần nhất | L2 | Không chạy lại trong audit |
| E2E trình duyệt | CR-004 regression 44 PASS/0 FAIL; làm sạch UI 10/10 | L3 | Script **ngoài repo**; không tái lập từ repo |
| CI | Không có (`.github` không tồn tại) | – | – |
| Migration | 5 migration áp ở DB dev (SELECT) và bằng bundle vào DB trống | L3 | Trong container NOT VERIFIED |
| Docker build / runtime | **NOT VERIFIED** | – | Không có Docker |
| Persistence | **NOT VERIFIED** | – | – |
| Backup / Restore | **NOT VERIFIED** | – | – |
| SignalR qua Nginx | **NOT VERIFIED** | – | – |
| Network isolation | **NOT VERIFIED** | – | – |
| Production security (ngoài container) | PASS: `/healthz` 200 ẩn danh, `/api/system/health` 401, `admin/admin` bị từ chối, cookie theo `SameAsRequest`, PLC `Disabled`, API mô phỏng 409, forwarded header đúng | L3 | Smoke test trong phiên, DB tạm đã xóa |
| PLC thật | **NOT VERIFIED** (REAL PLC TESTED = NO) | – | – |
| AGV/RCS thật | **NOT VERIFIED** | – | – |

Phân bố test (số file): Auth 1, Config 3, Domain 2, Integration 10, Plc 7 (+ReferenceOracle), SolutionStructure 1, Support 3.

---

## 10. Change Request Status (CR-002 đến CR-006)

| CR | Nội dung | Trạng thái trong tài liệu | Đối chiếu thực tế |
|---|---|---|---|
| CR-001 (tham chiếu) | Auth, phân quyền, audit | APPROVED | Đã triển khai + test; nhãn chưa cập nhật thành IMPLEMENTED |
| CR-002 | 4 PLC / 16 Station / 368 IO | APPROVED (architecture; tham số commissioning còn mở) | Đã triển khai ở Simulator/mock (L1, L2); nhãn chưa cập nhật |
| CR-003 | Tích hợp thư viện MC Protocol | IMPLEMENTED for SIMULATOR/MOCK; REAL PLC TESTED = NO | Khớp |
| CR-004 | Tách Kết nối PLC / Giám sát (chỉ đọc) / IO Mapping; dọn UI Production; hạ tầng Production safety | IMPLEMENTED; REAL PLC TESTED = NO | Khớp; phần "dọn UI" được ghi như addendum CR-004 |
| CR-005 | (nhắc trong yêu cầu) | **Không có mục nào trong `CHANGE_REQUESTS.md` / `CHANGELOG.md`** (grep = 0) | Lệch: cần quyết định đánh số hoặc ghi chú gộp |
| CR-006 | Docker hóa và triển khai Windows 11 | IMPLEMENTED (source); chưa chạy Docker | Khớp; nghiệm thu runtime chưa làm |

Lệch tài liệu khác (đã đối chiếu):

| Tài liệu | Vấn đề |
|---|---|
| `docs/README.md` | Version 1.3 (cũ so với CHANGELOG v2.0) |
| `docs/WORK_LOG.md` | Dừng ở CR-003; không có CR-004/005/006 |
| `CHANGELOG.md` | Có hai tiêu đề "v1.6" (dòng 41 và 99) |
| `TESTING_REPORT_CR-002.md` | Chứa các mục CR-002, CR-003, CR-004, CR-006 chung một file |

---

## 11. Open Issues & Risks

| # | Mức | Vấn đề | Bằng chứng | Tác động | Điều kiện xử lý |
|---|---|---|---|---|---|
| 1 | **BLOCKER** (vận hành thực) | Chưa có RCS adapter thật | `Simulators.cs`; `Infrastructure/DependencyInjection.cs` chỉ cho `Simulator` | Không có AGV thật | Thông số/API RCS-2000; CR mới |
| 2 | **BLOCKER** | Chưa xác nhận cổng TCP, tham số MC, cài đặt QJ71E71-100, 368 địa chỉ IO | `docs/specification/*`, CR-002/003 | Không nối PLC thật | Kỹ sư PLC xác nhận |
| 3 | **HIGH** | Production chạy RCS Simulator: Scheduler vẫn dispatch `RcsSimulator` ("Accepted"); AGV có `RcsRobotCode` hiện "Online – simulated". Banner giao diện nói chưa kết nối RCS nhưng backend **không chặn** | `TaskScheduler`/`TaskOrchestrator`; `RcsSimulator.CreateTaskAsync`, `QueryAgvStatusAsync`; `AgvStatusService.cs:15`. **Suy ra từ đọc mã, chưa chạy ở Production** | Task trông như đã được tiếp nhận nhưng không có AGV; dữ liệu vận hành giả | CR riêng (chặn dispatch hoặc ép trạng thái "Chưa kết nối RCS" khi chỉ có simulator) |
| 4 | **HIGH** | Docker runtime, persistence, backup/restore, SignalR qua Nginx, network isolation chưa nghiệm thu | Mục 8, 9; checklist trống | Có thể lỗi khi triển khai thật | Chạy checklist A–G trên Windows 11 |
| 5 | **HIGH** | Không có đặc tả handshake được duyệt | `business-rules.md:122`, `integration-requirements.md:76,94` | Không bật handshake / ghi PLC | Đội PLC hoàn thiện và duyệt |
| 6 | **HIGH** | Chưa có HTTPS/TLS | – | Cookie phiên đi không mã hóa khi dùng HTTP + `SameAsRequest` | CR TLS (chứng chỉ hoặc bộ chấm dứt TLS) |
| 7 | MEDIUM | Mật khẩu bootstrap `admin` nằm trong mã/lịch sử Git | `Infrastructure/Auth/AuthInfrastructure.cs:154`; Production không tạo tài khoản này trừ khi cấu hình `AllowDefaultCredentials` | Rủi ro nếu repo public hoặc môi trường không phải Production | CR riêng (xoay/loại khỏi lịch sử) |
| 8 | MEDIUM | Cài mới không có vị trí/capacity; README chưa hướng dẫn khởi tạo | `WcsDataSeeder.cs`; DB dev locations=0 | Hệ thống trống sau cài đặt | Xác nhận capacity, bổ sung hướng dẫn |
| 9 | MEDIUM | E2E ngoài repo, không CI | mục 7, 9 | Hồi quy giao diện không tự kiểm tra | Đưa E2E vào repo, thêm CI |
| 10 | MEDIUM | Key ring Data Protection lưu không mã hóa thêm trên volume | ASP.NET cảnh báo "No XML encryptor"; ADR-0005 mục 11 | Lộ khóa nếu lộ máy/backup | Đã có cảnh báo; đánh giá tác động trước khi đổi cơ chế |
| 11 | MEDIUM | X/Y ngoài phạm vi; W chưa dùng được khi chưa xác nhận cơ số | `PlcConfigService.SetAddressAsync` | IO dùng X/Y/W chưa cấu hình được | Xác nhận cơ số; CR riêng |
| 12 | MEDIUM | Tài liệu lệch (không có CR-005, version cũ, WORK_LOG dừng ở CR-003, nhãn CR-001/002) | mục 10 | Truy vết sai | CR tài liệu |
| 13 | LOW | Đăng nhập sai log ERROR kèm stack trace | Log smoke test | Log nhiễu | Tối ưu sau |
| 14 | LOW | PLC thấy IP máy Windows (NAT Docker), không phải IP container | Thiết kế Docker Desktop | Có thể cần whitelist trên PLC | Ghi nhận khi commissioning |
| 15 | LOW | Hai DB `Gateway_DAT`, `Gateway_station_dat` trên cùng PostgreSQL dev không thuộc WCS | Danh sách DB | Dễ nhầm khi backup/restore thủ công | Ghi nhận |

---

## 12. Next Action Plan

### Bước 1 – Việc cần làm ngay

| # | Công việc | Mục tiêu | Nội dung | Điều kiện đầu vào | Kết quả đầu ra | Tiêu chí nghiệm thu |
|---|---|---|---|---|---|---|
| 1 | Nghiệm thu Docker trên Windows 11 | Chứng minh gói CR-006 chạy được | Cài WSL2, Docker Desktop, Git; clone `main`; `.env` từ `.env.example`; `deploy.ps1`; chạy checklist A–D | Máy Windows 11; mật khẩu DB/admin | Checklist điền kết quả thật | A1–D7 PASS có bằng chứng; mục không chạy giữ trống |
| 2 | CR xử lý RCS Simulator ở Production (Issue #3) | Không còn dữ liệu AGV/task giả ở Production | Đề xuất: chặn dispatch hoặc ép "Chưa kết nối RCS" khi chỉ có simulator; đánh giá ảnh hưởng | Phê duyệt chủ dự án | CR được duyệt | Không có task nào hiện "đã tiếp nhận" khi chỉ có simulator; có test |

### Bước 2 – Việc tiếp theo

| # | Công việc | Mục tiêu | Nội dung | Đầu vào | Đầu ra | Tiêu chí |
|---|---|---|---|---|---|---|
| 3 | Đồng bộ tài liệu | Truy vết đúng | Ghi nhận CR-005, cập nhật trạng thái CR-001/002, `docs/README.md`, `WORK_LOG.md`, sửa trùng "v1.6" | Quyết định về CR-005 | Tài liệu khớp mã | Không còn lệch ở mục 10 |
| 4 | E2E và CI | Hồi quy tự động | Đưa E2E vào repo, thêm CI (build, test, lint) | – | Pipeline | Pipeline xanh trên `develop` |
| 5 | Hướng dẫn khởi tạo vị trí/capacity | Cài mới dùng được | Bổ sung README, xác nhận capacity thật | Capacity từ vận hành | README cập nhật | Cài mới có thể tạo vị trí theo hướng dẫn |

### Bước 3 – Điều kiện trước khi nghiệm thu

| # | Công việc | Mục tiêu | Nội dung | Đầu vào | Đầu ra | Tiêu chí |
|---|---|---|---|---|---|---|
| 6 | Hoàn tất checklist E–G | Nghiệm thu Docker/Production | Production safety, phân quyền, forwarded headers, key ring, HTTPS (khi có) | Kết quả bước 1 | Checklist đầy đủ | Tất cả mục PASS hoặc có ngoại lệ được duyệt |
| 7 | Xử lý mật khẩu bootstrap trong lịch sử Git | Giảm rủi ro lộ thông tin | CR riêng | Phê duyệt | CR + biện pháp | Production không dùng mật khẩu mặc định |
| 8 | Thu thập thông số PLC | Mở khóa BLOCKER #2, #5 | Cổng TCP, tham số MC, cơ số W, 368 địa chỉ, handshake | Kỹ sư PLC | Tài liệu xác nhận | Có chữ ký/phê duyệt, cập nhật workbook IO qua CR |

### Bước 4 – Điều kiện trước khi vận hành thực tế

| # | Công việc | Mục tiêu | Nội dung | Đầu vào | Đầu ra | Tiêu chí |
|---|---|---|---|---|---|---|
| 9 | Commissioning PLC chỉ đọc | Kiểm chứng PLC thật | `plc-route-test.ps1 -Port <cổng xác nhận>`; `PLC_ALLOW_LIVE_CONNECTION=true`; chỉ đọc IO đã duyệt | Bước 8 | Biên bản commissioning | Đọc đúng, không ghi |
| 10 | RCS adapter thật + AGV thật | Vận hành điều phối thật | CR triển khai adapter; thử với AGV | Tài liệu RCS-2000 | Adapter + test | Task chạy hết vòng đời với AGV thật |
| 11 | REAL_READ_WRITE (nếu cần) | Ghi PLC có kiểm soát | Cần phê duyệt văn bản kỹ sư + handshake duyệt | Bước 8, 9 | Cấu hình khóa ghi | Chỉ khi có phê duyệt |
| 12 | HTTPS + `Always`, backup định kỳ | Vận hành an toàn | TLS, lịch backup, thử restore | – | Cấu hình + biên bản | Restore thử thành công |

---

## 13. Final Assessment

1. **Hiện tại WCS đã hoàn thành được những gì?** Lõi WCS ở Simulator: Auth/phân quyền, task state machine, scheduler PER_AGV, tồn kho, giao diện đầy đủ; mô hình 4 PLC / 16 Station / 368 IO; driver MC Protocol 3E Binary đã test với mock; cấu hình PLC và IO Mapping có kiểm soát; chốt chặn Production; gói Docker và tài liệu triển khai. 410/410 test backend ở lần chạy cuối.
2. **Chưa hoàn thành:** RCS adapter thật; kết nối PLC thật; địa chỉ IO, cổng, tham số MC và handshake được duyệt; HTTPS; BP-04 (REVIEW); hỗ trợ X/Y; W (cần xác nhận cơ số); E2E trong repo và CI.
3. **Có code nhưng chưa kiểm chứng thực tế:** `McPlcEndpoint` với PLC thật; toàn bộ Docker (build, runtime, persistence, backup/restore, SignalR qua Nginx, cách ly mạng, Nginx ghi đè forwarded headers); script deploy trên Windows; AGV/RCS thật. Hành vi Production với RCS Simulator mới chỉ suy ra từ mã.
4. **Có thể triển khai Docker trên Windows 11 ngay chưa?** Chưa được nghiệm thu. Về kỹ thuật có thể thử vì đủ file, nhưng không có bằng chứng nó chạy; nên thử với cấu hình mặc định (`RealReadOnly`, không nối PLC thật), chạy checklist, và chưa dùng cho vận hành.
5. **Công việc tiếp theo chính xác:** (a) nghiệm thu Docker trên máy Windows 11 theo checklist A–G; (b) song song soạn CR xử lý RCS Simulator ở Production. Hai việc này không phụ thuộc thiết bị và mở đường cho commissioning PLC.

---

## 14. Danh sách NOT VERIFIED (tổng hợp)

- Toàn bộ Docker: build image, compose runtime, healthcheck container, persistence (restart container/Docker), backup/restore, deploy/update/rollback, SignalR qua Nginx, network isolation, Nginx ghi đè `X-Forwarded-For`, `bash` trong image runtime.
- WSL2 (`wsl --status` không trả nội dung).
- PLC thật (đọc/ghi, MC parameters, QJ71E71-100, cơ số X/Y/W, handshake).
- RCS/AGV thật; hành vi Production của `RcsSimulator` (suy ra từ mã).
- Kết quả test hiện tại: không chạy lại trong audit (chỉ dựa vào lần chạy gần nhất trong phiên, 410/410).
- Frontend lint/tsc/build hiện tại: không chạy lại trong audit.

*Hết báo cáo.*
