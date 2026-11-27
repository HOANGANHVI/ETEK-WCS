# ETEK WCS – Báo cáo hiện trạng hệ thống (rà soát source code)

| | |
|---|---|
| **Ngày rà soát** | 2026-11-23 |
| **Phạm vi** | Chỉ đọc và báo cáo. Không sửa code, không commit / push / merge / checkout / reset, không chạy migration, không gửi lệnh tới PLC / RCS / AMR. |
| **Cách xác minh** | Đọc source thực tế (class, method, migration), Git (`log`, `branch`, `status`), tài liệu CR / ADR, và **một truy vấn chỉ-đọc** vào DB local `wcs_db` (bảng `__EFMigrationsHistory` và số dòng một vài bảng). Không chạy lại toàn bộ test; kết quả test lấy từ các lần chạy trong các phiên làm việc trước (ghi rõ ở từng chỗ). |
| **Quy ước** | Completed = có code và có kiểm thử phù hợp. Partial = một phần. Mock/Simulator = chỉ chạy với mô phỏng/mock. Not Implemented. Not Verified = chưa có bằng chứng xác minh. **Không** coi API/UI/test mock là "Completed" cho nghiệp vụ cần thiết bị thật. |

---

## 1. Executive Summary

1. **Hệ thống chạy khép kín được ở chế độ mô phỏng.** Tạo nhiệm vụ → điều phối → AGV/RCS **simulator** → tồn kho/rack (WCS sở hữu) → lịch sử, kèm route engine cấu hình được (Business Process, multi-leg), giám sát PLC, phân quyền Admin/Operator, nhật ký, xuất/nhập cấu hình, đa ngôn ngữ VI/EN. Các phần này có kiểm thử tự động rộng (772 test method, 1.058 ca ở lần chạy toàn bộ gần nhất, đều đạt).
2. **Phần "thật" duy nhất đã ở trên `main`:** (a) giám sát đội AMR thật bằng `queryAgvStatus` mỗi 5 giây (CR-031), (b) driver MC Protocol 3E Binary TCP cho PLC thật (đọc; ghi sau nhiều lớp khóa), (c) công cụ **RCS Diagnostics** gọi tay 23 API outbound (CR-032). **Cả ba chưa được xác minh với thiết bị/hệ thống thật**: nguồn tài liệu đều ghi "RCS unreachable from the dev machine" / "REAL PLC TESTED = NO".
3. **Điều phối thật sang RCS chưa có trên `main`.** `IRcsClient` chỉ có `RcsSimulator` (+ wrapper `LoggingRcsClient`). Ở Production, `RcsDispatchGuard` từ chối tạo task (`RCS_NOT_CONNECTED`) trừ khi bật cờ cho phép simulator. Adapter thật (`HikRcsClient`, callback `agvCallback`) nằm ở nhánh **chưa merge** `feature/cr-030-real-rcs-dispatch` (`0c3a0b5`).
4. **PLC theo tài liệu 20/11/2026 (CR-029) mới là đặc tả, chưa có dòng code nào.** Code vận hành theo baseline cũ: template **23 tín hiệu × 16 trạm = 368 điểm IO** (`plc-io-catalog.json`). Handshake chỉ là **khung quan sát**: không có quy tắc nào được duyệt nên không ghi gì. **Không** thể coi handshake 6 tín hiệu đã hoàn thành.
5. **Chưa có vòng đời nào được kiểm chứng với thiết bị thật** (PLC, RCS, AMR). Hệ thống phù hợp để **triển khai thử giám sát chỉ-đọc + chẩn đoán RCS**, chưa phù hợp để **vận hành điều phối AMR thật**.
6. **Không phát hiện lỗi P0** (mất/hỏng dữ liệu, lệnh nguy hiểm tự chạy) trong phần code đã đọc; xem mục 10 cho các rủi ro P1 – P3 và các giới hạn của lần rà soát này.

---

## 2. Git và phiên bản source hiện tại

| Mục | Kết quả (xác minh trực tiếp bằng Git) |
|---|---|
| Branch hiện tại | `main` |
| HEAD | `4ad8122` – *Merge branch 'feature/cr-032-rcs-diagnostics' into main: CR-032 …, MAP ID TT* |
| `main` so với `origin/main` | 0 ahead / 0 behind (`4ad8122` cả hai) |
| Stash | trống |
| Working tree | **Một thay đổi duy nhất**: `D docs/02_Interface_Specification/PLC_IO_Map/ETEK_WCS_IO_Map_4PLC_16Stations_v2_Separate_IO_Sets.xlsx` (file **đang được Git theo dõi**, bị xóa khỏi ổ đĩa; chưa stage). Không có file untracked. Báo cáo này không chạm vào file đó. |
| Xác nhận bàn giao CR-032 | **Đúng**: merge `4ad8122`; commit bổ sung `de8001f` (MAP ID TT + tham số) là parent thứ hai; `feature/cr-032-rcs-diagnostics` = `de8001f`, đã nằm trong `main`. |

**Branch còn tồn tại (21 local, đều có bản trên origin):**
- Đã merge hoàn toàn vào `main` (0 commit dẫn trước): cr-016, 017, 019, 020, 021, 023, 025, 026, 027, 028, 029, `cr-031-real-fleet-monitoring`, `cr-031-fix-rcs-nonsuccess-logging`, `cr-032-rcs-diagnostics`, `io-description-i18n`, `fix/cr-021-lifecycle-di-cycle`, `fix/cr-025-login-sidebar-alignment`, `fix/diagnostics-ui-single-export`, `fix/startup-logging-cookie-secure-policy`.
- **Chưa merge:** `feature/cr-030-real-rcs-dispatch` (+1 commit `0c3a0b5`, 22 file, +1309/−7: `HikRcsClient`, `RcsCallbackService`, endpoint `agvCallback`, MockRcsServer, test, ADR-0012; mặc định tắt, fail-closed).
- `develop`: dẫn trước `main` 1 commit (`f5f09b5`, merge `main` vào `develop`): chỉ là đồng bộ, không có nội dung riêng; `develop` đang **tụt sau `main`** từ CR-031 trở đi.

**Rủi ro mất thay đổi khi đổi branch/cập nhật code:** chỉ có file xlsx đang bị xóa trong working tree. `git checkout -- <file>` / `git restore` / `git stash` / `git checkout <branch>` có thể khôi phục hoặc cuốn theo thay đổi này; không có thay đổi chưa commit nào khác để mất. Không thao tác nào được thực hiện trong lần rà soát này.

---

## 3. Kiến trúc hệ thống

### 3.1 Cấu trúc solution

| Project | Vai trò | Quy mô (không tính Migrations) |
|---|---|---|
| `WCS.Domain` | Entity và quy tắc miền (Task, Rack, Location, Routing, Plc, Fleet, Users, RcsDiagnostics…) | 31 file, 2.499 dòng |
| `WCS.Application` | Dịch vụ nghiệp vụ, hợp đồng (interface), policy, RCS Diagnostics, Fleet, Routing, Plc | 79 file, 11.151 dòng |
| `WCS.Infrastructure` | EF Core/PostgreSQL, MC Protocol client, HikFleetQueryClient, simulator, logging, DI | 38 file, 5.092 dòng |
| `WCS.Api` | Minimal API (139 route), Auth, SignalR hub, ProductionSafety | 18 file, 2.031 dòng |
| `WCS.Runtime` | Hosted service / worker | 6 file, 398 dòng |
| `tests/WCS.Tests` | xUnit, tích hợp trên PostgreSQL thật | 18.806 dòng, **772 test method** |
| `src/frontend/wcs-web` | React + TypeScript + Vite, i18n VI/EN | 16.161 dòng |

Công nghệ: .NET 10, ASP.NET Core minimal API + SignalR (`/hubs/wcs`), EF Core + PostgreSQL 16, React 19, Docker Compose (`postgres`, `migrator`, `backend`, `frontend`; `deployment/docker-compose.yml`).

### 3.2 Phân lớp và định hướng WMS/WCS/RCS
- Phân lớp Domain → Application → Infrastructure → Api/Runtime đúng quy ước; adapter PLC/RCS nằm ở Infrastructure, tách khỏi nghiệp vụ (`IPlcAdapter`, `IRcsClient`, `IRcsFleetQuery`) – **phù hợp** CLAUDE.md §4 và ADR-0004.
- **WCS sở hữu tồn kho/vị trí rack** (`InventoryService`, `ReconciliationService`, bảng `racks`, `locations`, `inventory_transactions`) – đúng quy tắc bảo vệ. RCS chỉ là nguồn điều phối AMR; fleet đọc qua read-model riêng (`rcs_amr_snapshots`), **không thay** bảng `agvs`.
- Tách WMS: WCS chưa có tích hợp WMS nào (không thấy adapter/endpoint WMS) – **Not Implemented** (không phải mục tiêu của các CR đã đọc).

### 3.3 Background service (đăng ký ở `WCS.Runtime/DependencyInjection.cs`)
`ApplicationLifecycleHostedService`, `DispatchModeStartupCheck` (từ chối khởi động nếu đổi mode khi còn việc dở), `DatabaseSeedHostedService`; khi `Runtime:EnableWorkers`: `PlcRuntimeHostedService` + `HandshakeHostedService` (khi `Plc:RuntimeEnabled`), `SchedulerHostedService`, `RoutingHostedService`, `AgvStatusHostedService` (vòng poll fleet), `LogMaintenanceHostedService`, `DiagnosticExportCleanupHostedService`, `DbHealthMonitorHostedService`; ngoài ra `ResourceMonitor` (Infrastructure).

### 3.4 Xác thực, phân quyền, cấu hình
- Cookie auth + chính sách `AdminPolicy` (`AuthSetup.cs`); hai vai trò **Admin / Operator** (không có Viewer). Mọi call ghi yêu cầu header `X-WCS-Client` (request guard). `AdminBootstrapper` tạo admin mặc định chỉ khi **không** ở Production.
- `ProductionSafety.Validate / ValidateFleet`: Production từ chối khởi động với cấu hình không an toàn (ví dụ fleet simulator).
- Cấu hình: biến môi trường / `appsettings` (`Plc__*`, `Rcs__*`, `Integration__Mode`), kết nối PLC/RCS lưu DB qua `ConnectionConfigService` (secret mã hóa bằng DataProtection). `Integration:Mode` chỉ chấp nhận `Simulator` (mã khác ném `NotSupportedException` – `WCS.Infrastructure/DependencyInjection.cs`).
- Triển khai: `deployment/` (compose, scripts, `.env.example`), `docs/docker-acceptance/`. Mặc định compose: `PLC_MODE=RealReadWrite` nhưng `PLC_ALLOW_LIVE_CONNECTION=false`, `PLC_WRITE_ENABLED=false`; `RCS_FLEET_SOURCE=Real`, `RCS_DISPATCH_MODE=Legacy`. Không có "Cloud" riêng: chỉ Docker triển khai tại chỗ (Not Implemented / ngoài phạm vi code đã đọc).

---

## 4. Hiện trạng từng module

| Module | Chức năng | Trạng thái | Bằng chứng trong code | Phần còn thiếu |
|---|---|---|---|---|
| Dashboard | Tổng quan task, AGV, PLC, RCS, tài nguyên server | **Completed (UI + API)**; dữ liệu AMR ở Real = một danh sách từ RCS | `DashboardPage.tsx`, `/api/system/health`, `ResourceMonitor` | Số liệu RCS thật chưa quan sát; thiết kế lại ở CR-025 chỉ trình bày |
| AGV/AMR Monitoring | Danh sách AMR động từ RCS, trạng thái fresh/stale/missing, thẻ AGV nghiệp vụ (Legacy) | **Partial (Real chưa xác minh)** | `FleetService.cs`, `HikFleetQueryClient` (`FleetInfrastructure.cs`), `AgvStatusHostedService`, `AgvPage.tsx`/`FleetPanel.tsx`, bảng `rcs_amr_snapshots` | Chưa nhận câu trả lời RCS thật; Phase 2 (CR-027) chưa duyệt |
| RCS Integration – Fleet | `queryAgvStatus` mỗi 5 s | **Not Verified với RCS thật** (mock đầy đủ) | `Cr031RealFleetMonitoringTests`, `Cr031PollOutcomeTests` | Acceptance thật chưa làm (RCS không tới được từ máy dev) |
| RCS Integration – Dispatch | Legacy / RcsManaged | **Mock/Simulator** | `IRcsClient` chỉ có `RcsSimulator`; `RcsDispatchGuard`; `DispatchOptions`; ADR-0010 | Adapter thật ở nhánh chưa merge (CR-030); callback chưa có trên `main` |
| RCS Diagnostics | 23 API outbound gọi tay, lịch sử, callback monitor, tham số | **Completed cho công cụ; Not Verified với RCS thật** | `RcsApiCatalog`, `RcsDiagnosticsService`, `RcsDiagnosticsEndpoints`, `Cr032RcsDiagnosticsTests` (83) | Chưa có lần gọi thật; callback receiver chưa có |
| Transport Task / Dispatch | Vòng đời task, hàng đợi, PER_AGV, retry, idempotent, hold | **Completed trong Simulator; Mock** | `TaskOrchestrator`, `TaskScheduler`, `RcsRetryPolicy`, `AgvHoldService`, `Cr008*`, `Cr028*` tests | Chạy với RCS/AGV thật: chưa |
| Routing / Business Process | Process cấu hình, multi-leg, Station Transaction, publish version | **Completed (cấu hình & mô phỏng); BP-04 REVIEW** | `RouteEngine`, `ProcessValidator`, `StationTransactionService`, `RoutingHostedService`, `Cr010*` tests | Luồng PLC live chưa nối; BP-04 chưa được duyệt (đúng quy tắc) |
| PLC Integration | Driver MC 3E, polling, đọc/ghi, phê duyệt ghi theo điểm, chẩn đoán | **Partial; Mock** (MC mock server) | `Plc/Mc/McTcpClient.cs`, `McFrame.cs`, `PlcWorker.cs`, `PlcRuntime.cs`, `Plc*Tests` | **Chưa test với PLC thật**; địa chỉ thực chưa có |
| PLC Handshake / Station signals | Khung handshake quan sát; `PlcStationSignalService` | **Partial (khung, không có quy tắc duyệt)** | `Handshake.cs` (HandshakeCoordinator); `plc-io-catalog.json` `handshake` toàn `PENDING` | Handshake 6 tín hiệu (CR-029) **Not Implemented** |
| Rack / Location / Inventory | Tồn kho, đặt chỗ, rack, đối soát, bất định tồn kho | **Completed (WCS-owned, DB test)**; đối soát vật lý **Partial** | `InventoryService`, `ReconciliationService`, `racks`/`locations`/`reservations`, `Cr008b4*` tests, `InventoryPage` | Xác nhận rack thực tế (PLC/thiết bị nhận diện) chưa có (D-4) |
| Station / Door / Handoff | Trạm PLC (16), yêu cầu trạm, Station Transaction (hand-off) | **Partial** | `plc_stations`, `station_requests`, `station_transactions`, `StationRequestProcessor`, `PlcRequestService` | **Không có khái niệm "Door" trong code/đặc tả**; hand-off thật phụ thuộc handshake (chưa có) |
| Maintenance | Cờ bảo trì hệ thống, chặn việc mới | **Completed (test DB)** | `MaintenanceService`, `MaintenanceEndpoints`, `Cr026MaintenanceTests`, bảng `system_maintenance` | — |
| Cấu hình & chuyển cấu hình | Export/Import (Location, AGV, PLC Station, Business Process) | **Completed (test); chưa triển khai** | `ConfigTransfer/*`, `Cr026ConfigTransfer*Tests` | Chưa chạy ở môi trường deploy |
| Logging & chẩn đoán | System log, audit, xuất chẩn đoán toàn hệ thống | **Completed (mock)** | `LogEndpoints`, `DiagnosticEndpoints`, `SystemLogSink`, `SystemDiagnostic*Tests` | — |
| Tài khoản & quyền | Admin/Operator, hồ sơ, audit | **Completed** | `AuthSetup`, `UsersPage`, `AuthIntegrationTests` | Không có Viewer (không yêu cầu) |

---

## 5. Hiện trạng PLC

### 5.1 Kết nối và giao thức
- **MC Protocol 3E Binary qua TCP:** `Plc/Mc/McFrame.cs` (mã hóa/giải mã khung), `McTcpClient.cs` (kết nối, timeout, reconnect có backoff, chẩn đoán yêu cầu/lỗi/độ trễ), `McPlcEndpoint.cs`. Thiết bị M/X/Y/D/W; xác thực phản hồi chặt; ghi mơ hồ (ambiguous write) được xử lý (CR-003). Code mức frame đối chiếu với thư viện tham chiếu (parity test).
- **Giao dịch / hàng đợi:** mỗi `McTcpClient` có `SemaphoreSlim(1,1)` – tuần tự hóa truy cập trên **một kết nối**; mỗi PLC một kết nối (4 PLC, `plc-io-catalog.json`: PLC-01 A1–A4, PLC-02 B1/B2/D1/D2, PLC-03 X1/X2/X3/X6, PLC-04 E1/E2/G3/G7; IP 192.168.10.101/111/121/131). Timeout và address base theo từng PLC (migration `AddMcAddressBasesAndTimeouts`).
- **Chế độ chạy** (`PlcRuntimeOptions`): `Simulator` (mặc định) / `RealReadOnly` / `RealReadWrite`; khóa thứ hai `AllowLiveConnection` (không có thì **không mở socket** tới PLC thật); ghi cần thêm `Write.Enabled` + phê duyệt ghi **theo từng điểm IO** (CR-014/019).
- **Chẩn đoán:** `plc_comm_log`, `plc_exchange_log` (cặp MC request/response), xuất chẩn đoán.
- **Heartbeat / đồng bộ trạng thái:** `PlcWorker` poll, chất lượng `Good/Stale/Simulated…`; heartbeat `Heartbeat_SW` là tín hiệu của template cũ; phạm vi heartbeat (PLC hay Station) **chưa được xác định** (NEEDS_CONFIRMATION trong `docs/archive/status/WORK_LOG.md`).

### 5.2 Mapping: cái nào theo cũ, cái nào theo 20/11, cái nào chưa làm

| Nội dung | Baseline cũ (IO Map v2, 23 tín hiệu) | Tài liệu 20/11/2026 (CR-029) |
|---|---|---|
| Template tín hiệu | **Đang chạy**: 23 tín hiệu × 16 trạm = **368 điểm** (`plc-io-catalog.json`; DB local `station_io_points` = 368) | Chưa dùng: 6 tín hiệu/Station (`Mission_*`, `PLC_MissionCompleted`, `HeartBit`, `Spair`…) **không có trong code** (grep không thấy) |
| Địa chỉ thực | `Actual_PLC_Address` **để trống**, nhập tay trên màn hình *Cấu hình địa chỉ IO* | Cột địa chỉ của file 20/11 cũng trống |
| Handshake | Khung `HandshakeCoordinator`; sheet *Handshake Definition* toàn `PENDING`; **không có quy tắc nào được duyệt ⇒ không ghi gì** | Handshake 6 bước: **Not Implemented** |
| Đồng thời giao dịch | Hiện tại: một điểm vào trên mỗi kết nối (semaphore) và từ chối khi cổng bận (ADR-0008) | **D-1** (1 handshake active/PLC, FIFO/PLC): **Not Implemented**; CLAUDE.md §7.8 yêu cầu không được nói là đã hỗ trợ |
| Trạm PLC-04 | E1, E2, G3, G7 (đang cấu hình) | File 20/11 ghi G1, G2: **PQ-20 chưa trả lời**, không đổi cấu hình |
| Định danh RobotCode / `Mission_Feedback` | — | D-2: chưa triển khai |
| Đối soát rack vật lý | — | D-4: chưa triển khai (xem 6.2) |

Trạng thái CR-029: **PENDING_APPROVAL**, tài liệu đã merge vào `main` (`ae54ba0`), **không** Approved, **không** Ready for Implementation (`CR-029_PLC_INTERFACE_20_11_2026.md`, CLAUDE.md §7.8 "Implementation gate").

### 5.3 Chức năng bị khóa hoặc chỉ Simulator
- `Plc:Integration:Enabled=false` mặc định và `StationFlows` rỗng ⇒ **không trạm nào tự tạo task** từ PLC (monitor-only).
- Ghi PLC: tắt mặc định (`PLC_WRITE_ENABLED=false`); không có tín hiệu an toàn/interlock nào được ghi để thử kết nối.
- **Chưa có PLC thật nào được kiểm chứng** (CR-002/CR-003: "Simulator/Mock; real PLC untested"; CR-020: "verified against the MC mock only").

---

## 6. Hiện trạng RCS

### 6.1 Fleet Monitoring (CR-027, CR-031, bản sửa log)
- **Gọi `queryAgvStatus`:** `HikFleetQueryClient` (`WCS.Infrastructure/Fleet/FleetInfrastructure.cs`) gửi **một** `POST /rcms-dps/rest/queryAgvStatus` chỉ với `reqCode`/`reqTime` – **không** username/password/`tokenCode` (quyết định dự án 2026-11-21: RCS không có xác thực cho API này). Host/cổng lấy từ **kết nối RCS đã lưu** (không hardcode; không có cổng mặc định → lỗi cấu hình).
- **Chu kỳ 5 s:** `AgvStatusHostedService` (+ `Rcs:Fleet:IntervalSeconds`=5), `FleetService.RefreshAsync` với cổng **single-flight** (`SemaphoreSlim`) và dung sai tick sớm 500 ms; timeout `Rcs:Fleet:TimeoutSeconds`=5 (client timeout ngắn hơn 250 ms để log có endpoint).
- **Danh sách AMR động:** số AMR = số phần tử `data[]` (`HikQueryAgvStatusParser`); không cố định AGV1/AGV2.
- **Snapshot / trạng thái:** bảng `rcs_amr_snapshots`, `rcs_fleet_state`; dữ liệu cũ chuyển `Stale`/`Missing`, **không** bao giờ hiển thị `Live` khi mất kết nối; AMR biến mất → `Missing`, dòng được giữ.
- **Lỗi được phân loại:** `RCS_TIMEOUT`, `RCS_CONNECTION_FAILED`, `RCS_HTTP_ERROR:<n>`, `RCS_CODE_NOT_SUCCESS:<code>`, `RCS_RESPONSE_INVALID`, `RCS_NOT_CONFIGURED` (`FleetErrorCodes`). Mỗi lần poll có log `RCS_FLEET_POLL_STARTED/SUCCESS/FAILED/SNAPSHOT_UPDATED` (endpoint, HTTP, mã/lời RCS, DurationMs, request id, số lần lỗi liên tiếp), bỏ qua throttle 60 s; bản sao DB giới hạn tần suất.
- **Không dùng dữ liệu Simulator thay Real:** `Rcs:Fleet:Source=Real` chọn `HikFleetQueryClient` **lúc khởi động**; không có fallback; `ProductionSafety.ValidateFleet` từ chối Production với fleet simulator; `AgvStatusService` không hỏi simulator ở Real.
- **Xác minh:** 16 + 16 + 32 test mock. **Chưa có câu trả lời RCS thật**; trên máy deploy từng thấy `RCS_CODE_NOT_SUCCESS:1` nhưng lý do chưa quan sát được (`CR-031_REAL_FLEET_MONITORING.md` mục 7).

### 6.2 Dynamic Dispatch (CR-028, ADR-0010)
- **Hai mode:** `Legacy` (mặc định; WCS chọn AGV nghiệp vụ theo vai trò, PER_AGV, hàng đợi từng AGV, DispatchHold) và `RcsManaged` (WCS gửi task **không** mã AMR, RCS chọn; `AgvId` nullable; chỉ giới hạn `MaxInFlightTasks`). Đổi mode bị chặn khi còn việc dở (`DispatchModeGuard`, `DispatchModeStartupCheck`).
- **Tạo task / dispatch sang RCS:** chỉ qua `IRcsClient` ⇒ hiện **chỉ simulator**. Production: `RcsDispatchGuard` trả `RCS_NOT_CONNECTED`. `.env.example`: "RcsManaged is for the RCS simulator only… never set it in operation".
- **RobotCode thật theo từng leg:** `TransportLeg.RobotCode` (`WCS.Domain/Routing/TransportOrder.cs`; kết quả `Assigned/Changed/NoChange`), `RcsRobotMapper` ánh xạ mã sang AGV nghiệp vụ (Legacy link) và fleet; kiểm tra "AMR nào?" mỗi 5 s, kiểm tra lại mỗi 60 s; đổi robot cần `RobotChangeConfirmChecks`=2 lần liên tiếp rồi Order sang `ReconciliationRequired` (không tự chuyển rack).
- **Retry / trùng lặp / đồng bộ:** backoff mũ có trần (`RcsRetryPolicy`, CR-008A), khóa yêu cầu idempotent, truy vấn theo mã yêu cầu trước khi gửi lại khi kết quả không rõ (`QueryTaskStatusAsync`), trạng thái hold/đối soát (CR-008B). Tất cả chạy trên simulator.
- **Task hoàn thành nhưng chưa xác nhận giao/nhận rack:** WCS không đánh dấu rack "đã tới" chỉ vì tạo/dispatch (quy tắc CLAUDE.md §4), có đối soát và bất định tồn kho; nhưng **xác nhận bằng chứng vật lý (hiện diện/nhận dạng rack qua PLC hoặc thiết bị khác – D-4)** **chưa có** vì phụ thuộc interface PLC chưa duyệt.
- **Chưa có tích hợp thật:** adapter tạo/tiếp tục/hủy task thật và callback `agvCallback` (CR-030) **chưa merge**; trạng thái task hiện chỉ có thể lấy bằng truy vấn (không có receiver).

### 6.3 RCS Diagnostics (CR-032, đã merge `4ad8122`)
- **Trang `/rcs-diagnostics`** (Admin; menu ẩn với Operator và Demo): Tổng quan kết nối (kiểm tra TCP tách khỏi kiểm tra API), Danh mục, Request Builder (form từ schema + JSON editor + xem trước), Inspector, Lịch sử (lọc, export JSON), tab **Tham số cấu hình**, tab **Theo dõi callback**.
- **Danh mục 23 API** (`RcsApiCatalog.cs`, đối chiếu PDF V3.3 mục 5.2.1–5.2.23). Phân loại rủi ro (2026-11-23): **Read-only 3** (`queryAgvStatus`, `queryTaskStatus`, `queryPodBerthAndMat`); **Unverified/có thể ghi 1** (`syncMapDatas`); **Mutating 7** (`setTaskPriority`, `lockPosition`, `blockStgBin`, `bindPodAndBerth`, `bindCtnrAndBin`, `bindPodAndMat`, `bindMaterialTypCodeAndBerth`); **Critical 12** (`genAgvSchedulingTask`, `continueTask`, `cancelTask`, `genPreScheduleTask`, `genCtuGroupTaskBatch`, `stopRobot`, `resumeRobot`, `blockArea`, `clearRoadWay`, `getOutPod`, `returnPod`, `boxApplyPass`).
- **Phân quyền:** policy `AdminOnly` trên cả nhóm `/api/rcs-diagnostics` **và** `RcsDiagnosticsService.RequireAdmin` (Operator 403, chưa đăng nhập 401). Không có bước duyệt: Execute gửi ngay (quyết định chủ dự án). Chỉ catalog mới là đích gọi (host/port/url trong body bị từ chối – chống SSRF); hạn mức đồng thời `Rcs:Diagnostics:MaxConcurrent`=2; HTTP client riêng, không đụng polling 5 s.
- **Validator:** `RcsRequestValidator` (bắt buộc, kiểu, độ dài, enum, khoảng, luật "một trong/đúng một/bắt buộc khi"); yêu cầu sai **không** được gửi.
- **Kết quả:** 8 loại (`API_SUCCESS`, `TCP_CONNECTION_FAILED`, `TIMEOUT`, `HTTP_FAILURE`, `RCS_CODE_NOT_SUCCESS`, `JSON_PARSE_FAILED`, `RESPONSE_INVALID`, `NOT_CONFIGURED`); **không** coi HTTP 200 là thành công.
- **Masking:** khóa chứa token/password/secret/authorization/credential thành `***` trong request, response, lịch sử, export, audit (`RcsDiagnosticsService.Mask*`).
- **MAP ID `TT`:** `Rcs:Diagnostics:MapId` (mặc định `TT`, `RcsDiagnosticsOptions`), Admin ghi đè được (bảng `rcs_diag_params`); chỉ điền vào trường yêu cầu `mapShortName` (5 API); **không** đụng `mapCode`/`mapDataCode`; polling CR-031 không đổi.
- **Tham số / nhóm trường** (`RcsParameterCatalog.cs`): *Đã xác nhận*: `mapId`. *Người dùng cần nhập* (chưa có giá trị, không điền giả): `clientCode`, `agvTyp`, `wbCode`, `ctnrTyp`, `roadWayCode`, `areaCode` (→ `matterArea`/`targetArea`/`areaCode`). *Chưa xác định*: 13 trường (ví dụ `genAgvSchedulingTask.data/podTyp/materialType`, `characterValue`, `useableLayers`, `cacheCount`).
- **Migration/bảng:** `AddRcsDiagnostics` (`rcs_api_tests`, `rcs_callback_log`), `AddRcsDiagParams` (`rcs_diag_params`).
- **Callback Monitor:** liệt kê 5 callback (`agvCallback`, `warnCallback`, `bindNotify`, `applyReturnForValid`, `applyReturnForBin`) với trạng thái "chưa cung cấp trong bản này"; `rcs_callback_log` **trống**, không có receiver trên `main`.

**Ma trận 23 API (đối chiếu bằng chứng):**

| Mức | Số API | Bằng chứng |
|---|---|---|
| Có schema + adapter (gọi được từ công cụ) | 23 / 23 | `RcsApiCatalog.All` (`Executable = true`) |
| Đã kiểm tra với Mock (đúng method/đường dẫn/body, kết quả thành công) | 23 / 23 | `Every_API_reaches_the_mock_RCS…` (theory 23) |
| Có giao diện (form + JSON + xem trước) | 23 / 23 | Duyệt trình duyệt 23/23 (xem `docs/archive/reviews/CR-032_UI_WALKTHROUGH_2026-11-23.md`) |
| **Đã gọi thành công với RCS thật** | **0 xác minh được** | Không có phản hồi thật nào được ghi nhận; máy dev không tới được RCS |
| Chỉ có schema (chưa rõ ràng/đủ thông tin) | các điểm "gaps" từng API (ví dụ `queryAgvStatus` mẫu dùng `mapCode` so với bảng `mapShortName`; `blockStgBin` khóa `stgBunCode`; `blockArea` thiếu `noticeThird`; `returnPod` suy cờ Req/Opt từ thứ tự cột; `getOutPod`/`returnPod` dùng `hikTpsService`) | `gaps` trong catalog; `CR-032_RCS_DIAGNOSTICS.md` §8 |

---

## 7. Hiện trạng Database

### 7.1 Migration (24, `WCS.Infrastructure/Persistence/Migrations`)
`InitialCreate` (11/05) → `AddConnectionSettings` → `AddAuthentication` → `AddPlcTopology` → `AddMcAddressBasesAndTimeouts` → `AddSystemLog` → `AddRcsRetryTracking` → `AddStationRequests` → `AddAgvDispatchHold` → `AddLogLocalization` → `AddTaskHistoryDetailCodes` → `AddUserPreferences` → `AddUserProfile` → `AddRoutingEngine` → `AddPlcDiagnostics` → `AddPlcWriteApproval` → `AddCorrelationIds` → `AddSystemMaintenance` → `AddConfigTransferSessions` → `AddRcsFleetSnapshots` (11/19) → `AddRcsManagedDispatch` (11/21) → `AddLegRobotCode` (11/21) → **`AddRcsDiagnostics` (11/23)** → **`AddRcsDiagParams` (11/23)**.

### 7.2 Bảng chính (33 DbSet, `WcsDbContext`)
- Nghiệp vụ: `agvs`, `locations`, `racks`, `reservations`, `inventory_transactions`, `transport_tasks`, `task_history`, `processed_events`, `station_requests`.
- Routing: `business_processes`, `business_process_versions`, `route_steps`, `route_candidates`, `transport_orders`, `transport_legs`, `station_transactions`, `routing_events`.
- PLC: `plc_connections`, `plc_stations`, `station_io_points`, `plc_comm_log`, `plc_exchange_log`.
- RCS: `rcs_amr_snapshots`, `rcs_fleet_state` (monitoring); `rcs_api_tests`, `rcs_callback_log`, `rcs_diag_params` (diagnostics).
- Hệ thống: `connection_settings`, `system_maintenance`, `config_transfer_sessions`, `app_users`, `user_avatars`, `audit_log`, `system_log`.

### 7.3 DB local `wcs_db` (đã truy vấn chỉ-đọc)
- Migration cuối đã áp dụng: **`20261121033125_AddLegRobotCode`**. **Chưa áp dụng:** `AddRcsDiagnostics`, `AddRcsDiagParams` (kiểm tra: 0/3 bảng `rcs_*` của CR-032 tồn tại) ⇒ trang RCS Diagnostics trên backend trỏ DB này sẽ lỗi cho tới khi migrate. Dữ liệu: `locations`=2, `agvs`=2, `app_users`=2, `station_io_points`=368, `transport_tasks`=0, `racks`=0, `rcs_amr_snapshots`=0.
- **DB triển khai:** **Không truy cập, chưa xác minh** trạng thái migration. Theo thiết kế, service `migrator` trong compose áp dụng migration khi triển khai.

### 7.4 Rủi ro khi nâng cấp database
- `AddRcsDiagnostics`, `AddRcsDiagParams`, `AddRcsFleetSnapshots`: chỉ **tạo bảng** (Up không có Drop/Alter); `AddRcsDiagParams` tạo `rcs_diag_params(Key PK≤40, Value≤64, UpdatedBy, UpdatedAt)`; Down xóa bảng mới. **Rủi ro thấp.**
- **`AddRcsManagedDispatch` (đã có trong `wcs_db`, cần lưu ý cho deploy):** `DropIndex ux_transport_tasks_one_active_per_agv`, `AlterColumn AgvId` thành nullable trên `transport_tasks`, rồi **tạo lại** chỉ mục duy nhất có điều kiện (`AgvId IS NOT NULL AND RcsDispatchState IS NULL AND State IN (...)`). Trong khoảng giữa không có chỉ mục bảo vệ "một task active mỗi AGV" – chạy trong một migration/transaction; nên áp dụng khi không có task đang chạy.
- **`AddLegRobotCode`:** chuyển dữ liệu bằng SQL và tạo bảng sao lưu `transport_leg_robot_backup` (không xóa dữ liệu); `AddPlcWriteApproval` có `UPDATE station_io_points` (đổi trạng thái duyệt); `AddLogLocalization`/`AddSystemLog` có SQL. Cần sao lưu DB trước khi nâng cấp.
- Dữ liệu ghi nhật ký diagnostics `rcs_api_tests` giữ tối đa `Rcs:Diagnostics:HistoryKeep`=5000 dòng (xóa dần dòng cũ).

---

## 8. Hiện trạng Frontend

- **Trang và quyền** (`App.tsx`): mọi người dùng đăng nhập: Dashboard, `agvs`, `tasks`, `inventory`, `routing`, `plc`, `account`. **Chỉ Admin (`RequireRole` + API kiểm lại):** `processes`, `plc/connections`, `plc/io-mapping`, `diagnostics`, `config`, `config-transfer`, `users`, `audit`, `logs`, **`rcs-diagnostics`**. Menu hiện theo vai trò; `/routing` và các mục chẩn đoán ẩn ở Demo mode.
- **Demo/dữ liệu giả:** chỉ khi build với `VITE_DEMO_MODE=true` (`demo/demoApi.ts`, `engine.ts`: engine trong trình duyệt cho các call dữ liệu; đăng nhập/tài khoản/audit vẫn thật). Chạy bình thường **không** có dữ liệu giả; màn log và RCS Diagnostics hiển thị trạng thái trống/không có ở Demo.
- **Kết nối Backend:** `api/client.ts` (`realApi`, nhóm `rcsDiag*`, `transfer*`, `plc*`, `fleet`…); realtime qua SignalR.
- **Kết quả kiểm tra gần nhất (lần chạy trong phiên, sau CR-032 và phần tham số):** `tsc -b`, ESLint, `npm run i18n:check` (2.042 khóa VI/EN, đủ mã lỗi/audit từ backend) và `npm run build` đều sạch (chỉ cảnh báo chunk > 500 kB của Vite). **Không chạy lại trong lần rà soát này.**
- **RCS Diagnostics sau CR-032:** đã duyệt thực tế trên Chrome với RCS mock: 43/43 (23 API, validation, JSON editor, execute không modal, inspector, lịch sử/export, Operator bị chặn) + 15/15 (MAP ID TT, tham số). Chưa xem bố cục hẹp/di động và tiếng Anh bằng mắt.
- **Khu vực chưa hoàn thiện:** chưa có test E2E tự động trong repo (script duyệt trình duyệt chỉ nằm ở thư mục tạm của phiên); bundle lớn; các màn PLC/routing chưa dùng với thiết bị thật.

---

## 9. Lịch sử CR

| CR | Nội dung | Trạng thái trong code (Git) | Ghi chú |
|---|---|---|---|
| CR-001/002/003 | Auth/phân quyền; 4 PLC × 16 trạm; thư viện MC | Đã trong `main` | "real PLC untested" |
| CR-004 → CR-025 | PLC monitoring/ghi, routing, logging, UI | Đã trong `main` (qua `develop`) | Nhiều CR ghi "not deployed" |
| CR-026 | Xuất/nhập cấu hình + Maintenance | **Đã merge** (`a0c4c70`) | Sổ CR (`CHANGE_REQUESTS.md`) vẫn ghi "NOT merged" |
| **CR-027** | Giám sát AMR động Phase 1 | **Đã merge** `94017e3` | Phase 2 chưa duyệt |
| **CR-028** | Điều phối RcsManaged (simulator) | **Đã merge** `0481ca1` | Không dùng trong vận hành |
| **CR-029** | Đặc tả PLC 20/11 + D-1…D-4 | **Chỉ tài liệu**, merge `ae54ba0` | `PENDING_APPROVAL`; **không** có code |
| **CR-030** | Adapter RCS dispatch thật + callback | **CHƯA merge** (`0c3a0b5`), tắt mặc định | Không có trong sổ CR trên `main` |
| **CR-031** | Fleet thật + sửa log mỗi poll | **Đã merge** `009b5e8`, `047ef05` | Sổ CR ghi "not merged" (lỗi thời) |
| **CR-032** | RCS Diagnostics + MAP ID/tham số | **Đã merge** `4ad8122` (+`de8001f`) | Sổ CR ghi "not merged" (lỗi thời) |

---

## 10. Các vấn đề còn tồn tại

**Không có P0 xác nhận được** trong phần code đã đọc (không thấy đường nào tự ghi PLC, tự gửi lệnh AMR hoặc làm mất dữ liệu). Lưu ý giới hạn ở cuối mục.

### P1 – quan trọng, cần xử lý trước khi chạy thực tế
| # | Vấn đề | Liên quan / bằng chứng | Ảnh hưởng | Hướng xử lý |
|---|---|---|---|---|
| 1 | **Chưa có adapter điều phối RCS thật trên `main`** | `IRcsClient` chỉ `RcsSimulator`+`LoggingRcsClient`; `RcsDispatchGuard`; nhánh CR-030 chưa merge | Production **không** gửi được task cho AMR (`RCS_NOT_CONNECTED`) | Quyết định merge/hoàn thiện CR-030 sau khi có xác nhận API thật; test dispatch có giám sát |
| 2 | **Chưa gọi thành công RCS thật** (fleet lẫn diagnostics) | `CR-031_…` §6; `CR-032_…` §8 | Mọi định dạng/giả định mới chỉ là của tài liệu; `RCS_CODE_NOT_SUCCESS:1` ở máy deploy chưa rõ nguyên nhân | Chạy trên máy deploy: API chỉ-đọc trước (`queryAgvStatus`, `queryTaskStatus`), thu log `RCS_FLEET_POLL_FAILED` |
| 3 | **Chưa test PLC thật; interface 20/11 chưa duyệt/triển khai** | CR-002/003/020; CR-029 `PENDING_APPROVAL`; catalog 23 tín hiệu | Không thể vận hành handshake; mapping hiện tại là baseline cũ | Chốt PQ-20 và địa chỉ với kỹ sư PLC; chỉ sau đó duyệt CR-029 và lập kế hoạch migration |
| 4 | **Không có đối soát rack vật lý (D-4)** | CLAUDE.md §7.8; `ReconciliationService` chỉ dựa dữ liệu WCS | "Task xong" không đủ chứng cứ đã giao đúng rack/vị trí | Xác định thiết bị nhận diện rack (PLC hoặc hệ thống khác) trước khi cho dispatch thật |
| 5 | **Lệnh Critical/Mutating chạy ngay, không duyệt** (theo quyết định chủ dự án) | `RcsDiagnosticsService.ExecuteAsync`; 12 API Critical | Một thao tác sai (ví dụ `stopRobot -1`, `clearRoadWay`) tác động ngay thiết bị thật | Giữ nguyên quyết định nhưng cân nhắc cờ cấu hình triển khai để tắt nhóm Critical ở môi trường vận hành |
| 6 | **Migration CR-032 chưa áp dụng trên DB local; DB triển khai chưa xác minh** | `__EFMigrationsHistory` local dừng ở `AddLegRobotCode` | Trang Diagnostics lỗi trên DB chưa migrate; nâng cấp `AddRcsManagedDispatch` có bước drop/recreate index | Sao lưu; chạy `migrator` ở deploy; áp dụng khi không có task đang chạy |
| 7 | **D-1 (1 handshake/PLC, FIFO) chưa có** | `McTcpClient` semaphore; ADR-0008 | Hai trạm cùng PLC có thể bị từ chối thay vì xếp hàng | Thiết kế sau khi CR-029 được duyệt |

### P2 – cần thiết
| # | Vấn đề | Bằng chứng | Hướng xử lý |
|---|---|---|---|
| 8 | **Sổ CR/tài liệu lệch trạng thái thực**: CR-026/031/032 ghi "not merged"; CR-030 không có trong sổ; `develop` tụt sau `main` | `CHANGE_REQUESTS.md`, `git log main..develop` | Cập nhật sổ CR, đồng bộ `develop` |
| 9 | **File IO Map v2 `.xlsx` đang bị xóa trong working tree** nhưng là nguồn của catalog nhúng (`plc-io-catalog.json` → `source`) và được CLAUDE.md §7.1/§7.6 coi là tài liệu kiểm soát | `git status`; JSON `source.file` | Quyết định của chủ dự án: khôi phục hay xác nhận bản thay thế; không tự khôi phục (theo yêu cầu) |
| 10 | **Chưa có receiver callback RCS** (`agvCallback`…) | Callback Monitor "NotProvided"; `rcs_callback_log` trống | Trạng thái task chỉ lấy bằng polling; làm cùng CR-030 |
| 11 | Tên trạm PLC-04 (E1,E2,G3,G7 so với E1,E2,G1,G2) | PQ-20, CLAUDE.md §7.8 | Chờ kỹ sư PLC |
| 12 | Lịch sử diagnostics không lưu header phản hồi; chỉ giữ 5.000 dòng | `RcsApiTest` | Chấp nhận hoặc mở rộng nếu cần cho kỹ sư RCS |
| 13 | Không có kiểm thử E2E tự động trong repo; kiểm thử với RCS/PLC thật chỉ có mock | `tests/`, thư mục tạm | Đưa script E2E vào CI/tài liệu (tùy chọn) |
| 14 | 13 trường "chưa xác định" + các điểm `gaps` của V3.3 cần Hikrobot xác nhận (đặc biệt `syncMapDatas` đọc hay ghi; `TT` có đúng là `mapShortName`) | `RcsParameterCatalog`, catalog `gaps` | Gửi câu hỏi cho Hikrobot (`CR-032_RCS_DIAGNOSTICS.md` §8) |

### P3 – tối ưu / sau này
Chunk frontend > 500 kB; Demo mode vẫn nằm trong cùng codebase; tham số chẩn đoán là danh sách tĩnh (thêm tham số cần sửa code); Phase 2 của CR-027; không có tích hợp WMS.

**Giới hạn của lần rà soát:** không đọc từng dòng của mọi file (đọc theo hợp đồng, DI, endpoint, migration, test chính); không chạy lại test; không truy cập DB triển khai; không chạy ứng dụng với thiết bị thật; các khẳng định về hành vi runtime dựa vào code, test mock và tài liệu CR.

---

## 11. Danh sách công việc ưu tiên (theo mức quan trọng và phụ thuộc)

1. **Xác minh RCS thật ở mức chỉ-đọc** trên máy deploy: nhập host/cổng RCS thật, chạy *Kiểm tra TCP/API* và `queryAgvStatus`/`queryTaskStatus` từ RCS Diagnostics; thu log `RCS_FLEET_POLL_*` để hiểu `RCS_CODE_NOT_SUCCESS:1`. *(Mở khóa mọi việc RCS còn lại.)*
2. **Gửi danh sách điểm cần Hikrobot xác nhận** (mục 10 #14) và ghi kết quả vào catalog (đặc biệt `syncMapDatas`, `TT`/`mapShortName`, `returnPod`).
3. **Áp dụng migration** `AddRcsDiagnostics` + `AddRcsDiagParams` (và các migration chưa có) lên DB deploy sau khi sao lưu, qua service `migrator`; xác nhận bảng `rcs_*`.
4. **Quyết định về file IO Map v2 `.xlsx`** (khôi phục hay thay thế được duyệt) và cập nhật sổ CR/IMPACT (mục 10 #8, #9).
5. **Chốt PQ-20 và địa chỉ PLC với kỹ sư PLC**, sau đó đưa CR-029 ra duyệt; chỉ khi duyệt mới lập kế hoạch code/migration (CLAUDE.md §7.8 gate).
6. **Chạy commissioning PLC chỉ-đọc** (theo `docs/reviews/REAL_DEVICE_COMMISSIONING_PLAN_2026-11-21.md`) để xác minh driver MC trên PLC thật; không ghi tín hiệu an toàn.
7. **Quyết định phạm vi CR-030** (adapter dispatch thật + callback): rà soát nhánh `0c3a0b5`, merge khi có xác nhận API; sau đó kiểm thử dispatch có giám sát bằng `Legacy`.
8. **Thiết kế đối soát rack vật lý (D-4) và D-1 (FIFO/PLC)** cùng với kỹ sư PLC trước khi cho vận hành nhiệm vụ thật.
9. **Cân nhắc cờ cấu hình khóa nhóm API Critical** của RCS Diagnostics ở môi trường vận hành (không thêm bước duyệt).
10. Dọn tài liệu/Git: cập nhật `CHANGE_REQUESTS.md`, đồng bộ `develop`, đưa script E2E vào tài liệu.

---

## 12. Kết luận về khả năng triển khai và kiểm tra thực tế

- **Có thể triển khai ngay để kiểm tra thực tế (rủi ro thấp):** giao diện, tài khoản/phân quyền, nhật ký, xuất cấu hình, **giám sát AMR chỉ-đọc** (`Rcs:Fleet:Source=Real`), **RCS Diagnostics** (nên bắt đầu bằng API Read-only), và giám sát PLC ở chế độ đọc sau khi có địa chỉ được xác nhận và bật `PLC_ALLOW_LIVE_CONNECTION` có chủ ý. Mặc định compose giữ ghi PLC tắt, dispatch `Legacy`, fleet `Real`.
- **Chưa thể vận hành thật:** điều phối AMR từ WCS (Production từ chối), handshake PLC (khung rỗng, interface 20/11 chưa duyệt), xác nhận giao/nhận rack vật lý. Phần "thật" hiện có đều **Not Verified** với thiết bị/hệ thống thật.
- **Điều kiện tiên quyết để chuyển từ "chạy được trên mô phỏng" sang "chạy thật":** (1) xác minh RCS thật chỉ-đọc, (2) PLC engineer chốt interface và địa chỉ, (3) merge và kiểm thử adapter dispatch thật, (4) giải pháp đối soát rack vật lý, (5) DB deploy được migrate và sao lưu.
