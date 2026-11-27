# CR-010 – Báo cáo triển khai: Business Process cấu hình được, Route Engine, Transport Task đa chặng, Station Transaction theo cổng

**Ngày:** 2026-11-18 · **Nhánh:** `develop` · **Trạng thái Git:** chưa commit, chưa push (theo yêu cầu).
**Kết luận ngắn:** phần phần mềm (domain, database, Route Engine, API, giao diện, kiểm thử tự động) đã được triển khai và kiểm chứng bằng PostgreSQL thật, mô phỏng và trình duyệt thật. Phần **ghi tín hiệu PLC, đọc thanh ghi PLC trực tiếp và adapter RCS thật cho Leg chưa triển khai** vì thiếu xác nhận kỹ thuật; chưa có kiểm thử trên thiết bị thật.

Căn cứ: `ADR-0008`, `CHANGE_REQUESTS.md` (CR-010), `REQUIREMENT_TRACEABILITY.md` (RTE-01…15).

---

## 0. Kiểm tra trước khi lập trình

| Hạng mục | Kết quả |
|---|---|
| `Process in detail.pptx`, `ETEK Process AGV Phase 2 12.08.26.pptx` | Nằm ở thư mục cha của dự án (`7. FFC AMR/`). Tên tệp Phase 2 trên đĩa **không có "(1)"**. Chỉ đọc được văn bản slide, không đọc được mũi tên. |
| `AGV_PLC_IO_Interface_List_10_11_2026.xlsx` | Có, chưa được Git theo dõi. |
| `ETEK_WCS_IO_Map_4PLC_16Stations_v2_Separate_IO_Sets.xlsx` | **Đang ở trạng thái "đã xóa" trong cây làm việc** (không do tôi). Không khôi phục, không ghi đè. |
| `WCS_BP_Port_PLC_Transaction_Matrix.xlsx` | Có tại `docs/01_Transport_Process_description/`. Dùng làm tham chiếu làm việc, đối chiếu với slide. |

**Tái sử dụng:** `PlcStation` (làm "Port"), `PlcConnection`, `StationIoPoint` và catalog 23 tín hiệu (làm Default Template), `Location`, `Rack`, `Reservation`, `InventoryTransaction`, `IUnitOfWork` (retry khi xung đột), `IRcsDispatchGuard`, `Audited`/`IAuditLog`, `PlcRuntime`.
**Mở rộng (cộng thêm):** `LocationType` (+3 giá trị), `AuditQuery` (+tiền tố action), `PlcConfigService.ToView` (public).
**Không đụng:** `TaskStateMachine`, `TransportTask`, `TaskOrchestrator`, `InventoryService`, `FlowOptions`, `Scheduler`, RCS client, `PlcWorker`/driver MC. (Đã kiểm tra bằng `git diff`: chỉ `Enums.cs` thay đổi trong Domain, thuần cộng thêm.)

---

## A. Tổng quan triển khai

**Đã triển khai**
- Transport Task nhiều chặng (Leg) và Station Transaction tách biệt.
- Cấu hình Business Process theo phiên bản (Nháp → Đã kiểm tra → Published → Vô hiệu hóa), kiểm tra trước khi Publish, snapshot trên mỗi Transport Task.
- Route Engine: chọn quy trình, chọn nguồn/đích theo ưu tiên và điều kiện có kiểm soát, buffer, chờ, nhánh thay thế, hoàn thành.
- Một Station Transaction hoạt động cho mỗi cổng, ràng buộc ở database; các cổng độc lập (cùng PLC hay khác PLC).
- `PER_AGV` ở mức Leg (index duy nhất ở database + kiểm tra task cũ + giữ điều phối).
- Tồn kho theo sự kiện vật lý đã xác nhận (lấy, đang vận chuyển, thả vào buffer/đích), đối soát khi PLC lệch tồn kho.
- Phục hồi: mất kết nối PLC (giữ nguyên trạng thái, không hủy), WCS restart (không phụ thuộc bộ nhớ), `HandshakeRecovery` thuần hàm.
- PLC động: thêm PLC/cổng, bản sao độc lập của Default Template cho từng cổng.
- API, giao diện (Điều phối đa chặng, Quy trình và tuyến, thêm PLC/liên kết cổng), 50 kiểm thử tự động mới, migration `AddRoutingEngine`.

**Chưa triển khai (và lý do)**
- Ghi bất kỳ tín hiệu 9 bước nào vào PLC (M9101, M9120…): thời điểm, quyền reset, ánh xạ M9003/M9004/M9005 với pick/drop, handshake cổng đích **chưa được xác nhận**.
- Đọc trực tiếp D1500/D1510/D1512/D1514/D1520 từ PLC: đã có mô hình + xác thực (`PlcRequestSnapshot`, `PlcRequestService`), nhưng **chưa nối** vào `PlcRuntime` (chưa có địa chỉ được phê duyệt; D1500 STRING 254 chồng lấn thanh ghi khác; mã hóa D1514 và Rack_Type chưa có).
- Adapter RCS thật cho Leg: mẫu task RCS cho tuyến cấu hình chưa xác nhận; chỉ có bộ gửi mô phỏng (chặn theo `IRcsDispatchGuard`).
- Route Designer dạng sơ đồ (tài liệu cho phép để sau); giao diện hiện là bảng cấu hình.
- Nguồn yêu cầu F5/M5 (PDA/beeper) chưa nối vào Route Engine: hiện khởi tạo thủ công qua API/UI.

---

## B. Danh sách tệp thay đổi

**Domain:** `WCS.Domain/Routing/{RoutingEnums, BusinessProcess, ProcessSnapshot, TransportOrder, StationTransaction}.cs` (mới); `Common/Enums.cs` (+`WaitingPosition`, `PickupLocation`, `DeliveryLocation`).
**Application:** `Routing/{RoutingAbstractions, RoutingDtos, ProcessDefinitions, RouteConditionEvaluator, ProcessValidator, ProcessAdminService, RouteEngine, StationTransactionService, PlcRequestService, ReferenceProcessCatalog}.cs` + `reference-processes.json` (mới); `Plc/PlcProvisioningService.cs` (mới); `DependencyInjection.cs`, `WCS.Application.csproj`, `Auth/AuthAbstractions.cs` (+`ActionPrefix`), `Plc/PlcConfigService.cs` (`ToView` public).
**Infrastructure:** `Persistence/Configurations/RoutingConfigurations.cs`, `Persistence/Repositories/RoutingRepository.cs`, `Plc/PlcTemplateProvider.cs`, `Simulation/RoutingAdapters.cs` (mới); `WcsDbContext.cs`, `DependencyInjection.cs`, `Auth/AuthInfrastructure.cs`.
**Runtime:** `RoutingHostedService.cs` (mới), `DependencyInjection.cs`.
**API:** `Api/RoutingEndpoints.cs` (mới), `Api/Endpoints.cs`, `appsettings.json` (mục `RouteEngine`).
**Frontend:** `pages/{RoutingPage, ProcessesPage}.tsx`, `pages/plc/PlcProvisioning.tsx`, `api/routingTypes.ts`, `i18n/routing.ts`, `i18n/messages/routing.ts` (mới); `App.tsx`, `Layout.tsx`, `PlcConnectionsPage.tsx`, `api/client.ts`, `i18n/{index,vi,plc,systemLogTemplates}.ts`, `realtime/signalr.ts`.
**Database/Migration:** `Migrations/20261118022206_AddRoutingEngine{,.Designer}.cs`, `WcsDbContextModelSnapshot.cs`.
**Tests:** `Domain/Cr010DomainTests.cs`, `Integration/{Cr010MultiLegTests, Cr010ProcessConfigTests, Cr010ApiTests}.cs`, `Support/RoutingKit.cs` (mới); `Support/Harness.cs` (thêm bảng vào lệnh reset).
**Documentation:** `ADR-0008` (mới); `CHANGE_REQUESTS.md`, `CHANGELOG.md`, `IMPACT_MATRIX.md`, `REQUIREMENT_TRACEABILITY.md`; phụ lục (chỉ thêm) cho `business-rules.md` §1.9, `process-flows.md` §2.4, `integration-requirements.md`; tài liệu PLC nháp/khoảng cách/câu hỏi; báo cáo này.

---

## C. Kiến trúc sau triển khai

- **Transport Task** (mã `TransportOrder`; `TransportTask` cũ giữ nguyên nghĩa "nhiệm vụ AGV" và state machine được bảo vệ): sở hữu các Leg, lưu mã + số phiên bản + snapshot JSON của quy trình. Hoàn thành **chỉ** khi tới bước `ProcessCompletion`.
- **Leg:** một lần chuyển một rack từ nguồn tới đích (bước Pickup/Delivery/BufferStorage/BufferRetrieval). Đích có thể chọn trước khi lấy hoặc sau khi lấy (`AfterPick`, giống `continueTask`).
- **Station Transaction:** một lần AMR vào một cổng. Kết thúc khi **đã giao nhận xong và AMR đã rời cổng**; việc này **không** làm Transport Task hoàn thành.
- **Năm họ trạng thái tách biệt:** Transport Task, Leg, Station Transaction, thực thi AGV (cũ), Tồn kho (cũ), cộng trạng thái cấu hình phiên bản.
- **Route Engine:** không có tên vị trí trong mã. Mỗi lần `Advance` đọc snapshot, đánh giá bước (điều kiện/chờ/di chuyển), chọn nguồn/đích **chỉ trong ứng viên đã khai báo**, đặt chỗ, tạo Leg, gửi AGV (ngoài transaction DB), rồi chờ sự kiện vật lý.
- **Concurrency theo cổng:** index duy nhất một phần trên `station_transactions(PortId)` với trạng thái chưa kết thúc; thêm kiểm tra ứng dụng; không khóa PLC, không khóa toàn hệ thống.
- **Buffer / chờ:** thả vào buffer là lưu thật (tồn kho cập nhật), task chuyển `Waiting (AtBuffer)`; AMR chờ khi còn giữ rack là `Waiting (DestinationUnavailable_AgvHoldingRack)`, rack ở `IN_TRANSIT`, **không** cập nhật tồn kho. Vị trí chờ trung gian không phải nơi lưu.
- **PLC:** mỗi cổng là một `PlcStation` với bộ IO riêng (baseline CR-002); số PLC là dữ liệu. Engine **không ghi** tín hiệu PLC.
- **Phục hồi:** toàn bộ trạng thái ở database; `RoutingHostedService` tính lại mọi Transport Task mở mỗi chu kỳ, khởi động lại yêu cầu PLC đã ghi nhận nhưng chưa tạo task, đánh dấu tạm dừng giao dịch của PLC mất kết nối và đối chiếu khi kết nối lại.

---

## D. Database

**Bảng mới (8), không đổi/xóa bảng cũ:** `business_processes`, `business_process_versions`, `route_steps`, `route_candidates`, `transport_orders`, `transport_legs`, `station_transactions`, `routing_events`.
**Quan hệ chính:** version→process; step→version (cascade); candidate→step (cascade); leg→order (cascade); leg→agv; station_transaction→`plc_stations`/`plc_connections`; order→process/version; event→order.
**Ràng buộc/chỉ mục quan trọng**
- `ux_station_transactions_one_active_per_port`: một giao dịch hoạt động mỗi cổng (`State NOT IN ('Completed','Failed')`).
- `ux_station_transactions_request_id`: `(PlcId, RequestId)` duy nhất (chống trùng Request_ID, độ dài 254).
- `ux_transport_legs_one_active_per_agv`: một Leg giữ AGV (`InProgress`/`Waiting`) mỗi AGV.
- `ux_process_versions_one_published`: một phiên bản Published mỗi quy trình.
- Duy nhất: `business_processes.Code`, `(ProcessId, Number)`, `(VersionId, StepKey)`, `transport_orders.RequestKey`, `(OrderId, Sequence)`, `AgvRequestCode`; check `Priority > 0`, `Number > 0`, `HandshakeStep` 0..9.
**Migration:** `AddRoutingEngine` (chỉ `CreateTable`; `DropTable` nằm ở `Down`, **chưa chạy Down**).
**Áp dụng lên `wcs_db`:** đã sao lưu `C:\Users\Admin\wcs-backups\wcs_db_before_AddRoutingEngine_20261118-094032.dump` (xác minh `pg_restore -l`: 125 mục) rồi chạy `dotnet ef database update`. Dữ liệu cũ còn nguyên (4 PLC, 368 điểm IO). `dotnet ef migrations has-pending-model-changes`: không có thay đổi chưa migrate.

---

## E. Cấu hình tuyến (Route Configuration)

- **Tạo quy trình:** màn hình *Quy trình và tuyến* → "Tạo quy trình" (mã, tên, loại yêu cầu, loại rack, vị trí khởi tạo).
- **Bước:** mỗi bước có loại, chế độ nguồn (vị trí yêu cầu / ứng viên / đích của chặng trước), chế độ đích, thời điểm chọn đích (trước/sau khi lấy), bước kế tiếp, bước thay thế, hành vi khi không có vị trí (Chờ / Lỗi / Bước thay thế), vị trí chờ trung gian.
- **Ưu tiên đích:** danh sách ứng viên (priority 1 thử trước; cùng mức thử theo thứ tự — thứ tự trong cùng mức tài liệu chưa xác nhận), cờ "không bind rack".
- **Điều kiện:** chọn từ danh sách cố định (`LocationEnabled`, `LocationAvailable`, `RackPresent`, `RackTypeMatch`, `CapacityAvailable`, `StationReady`, `PlcOnline`, `InventoryState`, `RackBinding`). Không script/SQL; loại lạ bị từ chối ngay khi lưu.
- **Validate/Publish/Version:** Validate tạo danh sách lỗi theo bước; Publish chạy lại kiểm tra ở thời điểm đó; Publish bản mới vô hiệu hóa bản cũ cho task mới; bản đã Publish không sửa được, muốn đổi thì tạo phiên bản mới (sao chép). Nhật ký thay đổi lấy từ `audit_log` (tiền tố `PROCESS_`).
- **Kiểm thử đổi A→B thành A→C (Test 21/22):** quy trình Publish v1 (đích B1); Admin tạo v2 (đích C1) bằng dữ liệu, Publish. Task mới đi C1; **task đã chạy dưới v1 vẫn đi B1** và hoàn thành ở B1 (snapshot không chứa C1). Không sửa mã, không build lại.
- **Quy trình tham chiếu:** 12 bản nháp (BP-01/02/03/04/05/06 và PH2-01…06) nạp bằng nút "Nạp quy trình tham chiếu", **không tự Publish**. BP-04 và PH2-04 bị chặn Publish (REVIEW). Cấu hình khớp với `ProcessFlows` hiện có.

---

## F. API

Quy ước: `Admin` = chỉ Admin; `Ops` = Admin và Operator. Tất cả thay đổi đều ghi `audit_log`.

| Endpoint | Quyền | Chức năng |
|---|---|---|
| `GET /api/routing/orders`, `/orders/{id}` | Ops | Danh sách / chi tiết Transport Task (Leg, giao dịch, sự kiện) |
| `GET /api/routing/startable-processes` | Ops | Quy trình có thể khởi tạo (đã Publish, bật, không REVIEW) |
| `POST /api/routing/orders` | Ops | Khởi tạo Transport Task (idempotent theo `requestKey`) |
| `POST /api/routing/orders/{id}/cancel` | Admin | Hủy (từ chối khi rack đang trên AMR hoặc chặng đang chạy) |
| `POST /api/routing/legs/{id}/pick`, `/drop` | Admin | Nhập sự kiện vật lý đã xác nhận (tồn kho) |
| `GET /api/routing/transactions` | Ops | Danh sách Station Transaction |
| `POST /api/routing/transactions/{id}/advance`, `/exit`, `/observe`, `/resolve` | Admin | Cập nhật trạng thái, AMR rời cổng, ghi nhận bước handshake, đối soát |
| `POST /api/routing/plc-requests` | Admin | Nhập một yêu cầu PLC (mô phỏng; nơi PLC runtime sẽ gọi sau này) |
| `GET/POST /api/processes`, `GET/PUT /api/processes/{id}`, `POST /api/processes/{id}/versions`, `POST /api/processes/import-reference`, `GET /api/processes/audit` | Admin | Quản lý quy trình |
| `GET /api/process-versions/{id}`, `PUT …/definition`, `POST …/validate`, `…/publish`, `…/disable` | Admin | Cấu hình, kiểm tra, publish, vô hiệu hóa phiên bản (publish bị từ chối → 422 kèm danh sách lỗi) |
| `GET /api/plc/stations`, `POST /api/plc/connections`, `POST /api/plc/connections/{id}/stations`, `PUT /api/plc/stations/{id}/link` | Admin | Cổng, thêm PLC/cổng, liên kết cổng–vị trí |

Mô hình request/response nằm trong `RoutingEndpoints.cs` và `routingTypes.ts`.

---

## G. Giao diện

- **Điều phối đa chặng** (Admin + Operator): tab *Transport Task* (danh sách, chi tiết, Leg, giao dịch, diễn biến, lọc theo trạng thái, cập nhật thời gian thực) và tab *Giao dịch tại cổng*. Phân biệt rõ: giao dịch đã hoàn thành / task còn đang thực hiện / task chờ tại buffer / task chờ khi AMR vẫn giữ rack. Nút khởi tạo Transport Task. Admin có nút xác nhận lấy/thả rack, AMR rời cổng, đối soát; Operator chỉ xem.
- **Quy trình và tuyến** (Admin): danh sách, tạo, sửa thông tin, phiên bản, trình soạn bước/ứng viên/điều kiện, Validate/Publish/Vô hiệu hóa, danh sách lỗi theo bước (song ngữ), nhật ký thay đổi.
- **Kết nối PLC** (Admin): thêm PLC, liên kết cổng với vị trí, hiển thị số điểm IO đã duyệt.
- **Cấu hình hệ thống → Vị trí:** có thêm các loại vị trí mới.
- Tiếng Việt/English đầy đủ (`npm run i18n:check`). Ẩn trong chế độ demo trình duyệt (không có dữ liệu thật).

---

## H. Kết quả kiểm thử (chạy thật)

| Hạng mục | Kết quả |
|---|---|
| Backend toàn bộ (PostgreSQL thật, simulator) | 3 lần chạy toàn bộ 700 test (650 cũ + 50 mới): **700/700 đạt** ở lần 1 và lần 3; lần 2 có **1 test cũ thất bại** (`RobustnessTests.Parallel_duplicate_callbacks_apply_the_pick_exactly_once`, thuộc `InventoryService`/`TaskOrchestrator` không bị sửa; lỗi "rack không ở vị trí nguồn" trong cuộc đua 4 callback song song). Chạy riêng 12/12 đạt. Nghi ngờ test cũ nhạy theo thời gian; **chưa chứng minh** nguyên nhân gốc và chưa kiểm tra lại trên mã trước CR-010 |
| Riêng CR-010 | 50 đạt, chạy lặp 3 lần ổn định |
| — Unit (domain/recovery/điều kiện) | 13 |
| — Tích hợp đa chặng, concurrency, restart, PER_AGV, DB constraint | 16 |
| — Cấu hình, validation, version, bảo vệ, cung cấp PLC, recovery | 18 |
| — API/phân quyền/audit | 3 |
| Frontend | `tsc` sạch, `eslint` sạch, `npm run build` thành công, `i18n:check` OK, `i18n:test` 76/76, `dashboard:test` 18/18, `preferences:test` 10/10, `monitor:test` 23/23 |
| Trình duyệt thật (Edge headless, API + PostgreSQL thật, DB tạm đã xóa) | **48/48** kiểm tra đạt: nạp/validate/publish, BP-04 bị chặn, soạn phiên bản mới, khởi tạo → lấy → thả → hoàn thành, tồn kho, làm mới realtime, thêm PLC, English, Operator bị chặn (menu, URL, API), tràn ngang ở 1440/1024/760 px |
| Migration | Áp dụng lên bản sao DB tạm và lên `wcs_db` sau khi sao lưu; `has-pending-model-changes`: không có |

Ánh xạ 25 kịch bản yêu cầu → test:
1–5: `Cr010MultiLegTests` Test01–06 · 6: Test06 · 7–9: Test07–09 · 10: `Cr010ProcessConfigTests.Test10` · 11: `Restart_in_the_middle…` · 12: Test12 · 13: Test13 · 14: Test14 · 15–16: Test15/16 · 17: Test17 · 18: Test18 · 19: `Per_AGV_*` và `Database_refuses_*` · 20: Test20 · 21–22: Test21/22 · 23–24: Test23/24 · 25: `Engine_never_uses_a_location…`.

**Điều chưa được kiểm chứng:** không dùng PLC, RCS hay AGV thật; không chạy trên Linux/Docker; không chạy môi trường Production. Khi đăng nhập ở màn hình đổi mật khẩu bắt buộc, trình duyệt ghi 9–11 thông báo SignalR (kết nối hub khi chưa đăng nhập hoàn tất) trước khi mở bất kỳ màn hình CR-010 nào; từ sau đăng nhập không có lỗi console nào. Tôi không điều tra riêng hành vi này.

---

## I. Vấn đề còn tồn tại

**PLC/giao thức chưa xác nhận (không ghi PLC):** thời điểm M9101 và M9120, vai trò `Safety_Ready`, ánh xạ M9003/M9004/M9005 với pick/drop, handshake cổng đích, chủ reset, timeout/retry, chu kỳ heartbeat, độ dài và chồng lấn của D1500, mã hóa D1514, ánh xạ Rack_Type, chỉ biết mã 1–4 = A1–A4.
**Mâu thuẫn/điểm cần chủ dự án quyết định**
1. Tệp Phase 2 dùng vị trí P1–P4, M5, M6 và "vị trí chờ trung gian"; chưa có bảng ánh xạ với F1–F5/KV: các quy trình PH2-xx chỉ là bản nháp, **không validate được** cho đến khi có vị trí tương ứng.
2. Ma trận ghi E1/E2/G1/G2 và X4 trong khi danh sách PLC ghi E1/E2/G3/G7 và X6 (OP-03/OP-04).
3. BP-01 khi không có đích: baseline ghi chờ (`WAITING_FOR_DESTINATION`), ma trận ghi "NG": áp dụng baseline.
4. BP-05: nhánh "xóa task AGV1" trên slide chưa rõ điều kiện, **không** mô hình hóa; BP-04 và PH2-04 giữ REVIEW.
5. **Giả định cần xác nhận:** AGV được giải phóng giữa hai chặng (PER_AGV áp dụng theo Leg); giao dịch cổng đích mở từ lúc gửi Leg (bảo thủ hơn lúc AMR thật sự vào); thứ tự trong cùng mức ưu tiên theo thứ tự tài liệu.
6. Tên tệp Phase 2 không có "(1)"; workbook v2 bị xóa/Excel mới chưa theo dõi (tình trạng Git, chưa xử lý).

**Chức năng bị chặn/chưa làm:** đọc thanh ghi PLC trực tiếp và quan sát handshake thật (`IPlcObservationSource` mới có bộ "không có dữ liệu"); adapter RCS thật cho Leg; nguồn yêu cầu F5/M5; Route Designer sơ đồ.
**Chưa kiểm thử trên thiết bị thật:** toàn bộ.

---

## J. Truy vết yêu cầu

| ID | Yêu cầu | Vị trí mã | Kiểm thử | Trạng thái |
|---|---|---|---|---|
| RTE-01 | Task đa chặng tách Station Transaction | `TransportOrder`, `TransportLeg`, `StationTransaction`, `RouteEngine` | `Cr010MultiLegTests` (05, 06), `Cr010DomainTests` | Implemented |
| RTE-02 | Một giao dịch hoạt động/cổng; cổng độc lập | `ux_station_transactions_one_active_per_port`, `StationTransactionService` | Test07, 08, 09 | Implemented |
| RTE-03 | Giải phóng cổng sau giao nhận + AMR rời | `StationTransaction.Advance`, `ConfirmAgvExited*` | `Cr010DomainTests`, Test05 | Implemented |
| RTE-04 | Tuyến/ưu tiên/buffer/nhánh là cấu hình | `business_processes`…`route_candidates`, `RouteEngine` | Test21 | Implemented |
| RTE-05 | Phiên bản, Publish, lịch sử, audit, task giữ version | `ProcessAdminService`, `ProcessSnapshot` | Test21/22, `Cr010ApiTests` | Implemented |
| RTE-06 | Validation trước Publish | `ProcessValidator` | Test17/18/20/23/24 | Implemented |
| RTE-07 | Buffer cập nhật tồn kho; phân biệt chờ ở buffer / giữ rack | `RouteEngine.Confirm*`, `WaitKind` | Test02/03, 04, 13 | Implemented |
| RTE-08 | PLC lệch tồn kho → đối soát | `PlcRequestService` | Test12 | Implemented |
| RTE-09 | PER_AGV mức Leg | `ux_transport_legs_one_active_per_agv`, `DispatchLegAsync` | `Per_AGV_*`, `Database_refuses_*` | Implemented |
| RTE-10 | Mất kết nối / restart | `SuspendPlcAsync`, `RecoverPlcAsync`, `HandshakeRecovery`, `RoutingHostedService` | Test10, `Restart_*`, `Cr010DomainTests` | **Partial** (chưa đọc PLC thật) |
| RTE-11 | PLC động, bản sao độc lập của template | `PlcProvisioningService` | Test15/16/17 | Implemented |
| RTE-12 | Đọc D1500/D1510/D1512/D1514/D1520, mã 1–4 | `PlcRequestSnapshot`, `PlcRequestService` | `PLC_position_codes_*`, Test14 | **Partial** (diễn giải có, đọc thật chưa nối) |
| RTE-13 | 9 bước giữ nguyên, không ghi PLC khi chưa duyệt | `StationTransaction.HandshakeStep` (chỉ quan sát) | `Cr010DomainTests` | **Blocked** (các điểm mở §I) |
| RTE-14 | A3/A4, D1/D2 UNCHANGED; BP-04 REVIEW | `RouteEngine:ProtectedLocationCodes`, review-blocked | Test18, 20 | Implemented |
| RTE-15 | API + giao diện quản trị/giám sát, phân quyền | `RoutingEndpoints`, `RoutingPage`, `ProcessesPage`, `PlcProvisioning` | `Cr010ApiTests` + trình duyệt 48/48 | Implemented |

---

## K. Bước tiếp theo trước khi commissioning với PLC thật

1. Kỹ sư PLC xác nhận: độ dài/mã hóa D1500 và vùng nhớ chồng lấn, mã hóa D1514, Rack_Type, địa chỉ M/D thực của từng cổng, mô hình (một bộ IO mỗi cổng hay mỗi PLC).
2. Chủ dự án quyết định các điểm §I (mục 1, 2, 5) và cung cấp bảng ánh xạ P/M ↔ F/KV nếu Phase 2 nằm trong phạm vi.
3. Xác nhận thời điểm M9101 ACK, M9120, vai trò `Safety_Ready`, ánh xạ M9003/M9004/M9005, handshake cổng đích, chủ reset, timeout/retry, heartbeat.
4. Phê duyệt IO mapping từng cổng (trạng thái `Approved`) và cấu hình tham số kết nối còn thiếu; chạy chỉ-đọc trước.
5. Hiện thực `IPlcObservationSource` và việc đọc `PlcRequestSnapshot` từ `PlcRuntime`; sau đó mới thiết kế các thao tác ghi, từng tín hiệu, với quy trình thử an toàn.
6. Xác nhận mẫu task RCS cho Leg (task có waypoint + `continueTask` hay mỗi Leg một task) rồi viết adapter RCS thật; thay `SimulatedLegDispatcher`.
7. Kiểm thử có giám sát trên PLC/AGV thật (không có kiểm thử nào ở phần cứng đã được thực hiện), kiểm thử Linux/Docker, kiểm thử Production.
8. Chủ dự án xử lý trạng thái Git của workbook IO v2/Excel mới và ký duyệt chính thức CR-010.
