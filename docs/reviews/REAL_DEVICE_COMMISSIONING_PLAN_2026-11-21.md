# Kế hoạch triển khai và commissioning với thiết bị thật – 2026-11-21

| | |
|---|---|
| **Trạng thái** | Đã merge CR-027, CR-028, CR-029 vào `main`. **Chưa deploy, chưa kết nối thiết bị thật** (xem mục 2). Tài liệu này là kế hoạch và trạng thái trung thực, không phải kết quả kiểm thử. |
| **Nguyên tắc** | Chỉ báo "chạy thật" khi có bằng chứng từ thiết bị thật. Không gửi lệnh vận chuyển hay ghi PLC bằng dữ liệu giả/mapping suy đoán. Không bỏ qua interlock/an toàn. |

## 1. Những gì code hiện tại thực sự hỗ trợ ở chế độ Real

| Chức năng | Hỗ trợ trong code | Điều kiện bật | Đã kiểm chứng với thiết bị thật? |
|---|---|---|---|
| **PLC – kết nối TCP + MC Protocol 3E binary** | **Có** (`McTcpClient`, `McFrame`, `McPlcEndpoint`; một worker mỗi PLC; kết nối lại có back-off; log chẩn đoán) | `PLC_ALLOW_LIVE_CONNECTION=true`, `PLC_MODE=RealReadWrite` hoặc `RealReadOnly`; PLC được Admin bật; đủ tham số (port, network, PC, module I/O, station, timer, timeout) do kỹ sư PLC xác nhận | **Chưa** (chỉ với `MelsecMcMockServer`) |
| **PLC – đọc điểm IO** | **Có** | Điểm IO có địa chỉ hợp lệ (CR-012: có địa chỉ ⇒ được đọc) | **Chưa** |
| **PLC – ghi điểm IO** | **Có, bị khóa mặc định** | `PLC_WRITE_ENABLED=true` + `PLC_ALLOW_LIVE_CONNECTION=true` + Admin duyệt ghi **từng điểm** | **Chưa** |
| **PLC – Heartbeat** | **Không** (không có bộ ghi heartbeat chu kỳ; `Heartbeat_SW` chỉ là một tín hiệu trong mẫu) | – | – |
| **PLC – Handshake** | **Không**: khung `HandshakeCoordinator` chỉ quan sát, không có luật ghi được duyệt; **không** có handshake theo giao diện 10/11 lẫn 20/11 (CR-029 chỉ là tài liệu) | – | – |
| **RCS – giám sát đội AMR (`queryAgvStatus`)** | **Có** (CR-027: `HikFleetQueryClient`, POST `/rcms-dps/rest/queryAgvStatus` cổng 8182; polling; trạng thái Fresh/Stale/Missing; `GET /api/agvs/fleet`; màn hình AGV Monitoring) | `RCS_FLEET_SOURCE=Real` (đã thêm vào compose/.env.example, mặc định Simulator) + RCS được Admin lưu và bật trong *Kết nối RCS* (host, token) | **Chưa** (chỉ với máy chủ thử cục bộ trả lời theo tài liệu) |
| **RCS – tạo task (`genAgvSchedulingTask`)** | **Không**. `Integration:Mode` khác `Simulator` làm backend **từ chối khởi động**; chỉ có `RcsSimulator` | – | – |
| **RCS – `queryTaskStatus`, `continueTask`, `cancelTask` thật** | **Không** (chỉ trong simulator) | – | – |
| **RCS – `agvCallback` (RCS → WCS)** | **Không** (không có endpoint; cách xác thực chưa biết) | – | – |
| **Ánh xạ Location → `positionCode`; BP01–BP06 → `taskTyp`** | **Không có** trong tài liệu hay cấu hình | – | – |
| **Đồng bộ `RobotCode` vào Task/Leg** | Có (CR-028) **nhưng chỉ với simulator**, vì phụ thuộc `queryTaskStatus` | – | – |
| **Rack/Inventory ↔ PLC** | WCS sở hữu tồn kho; xác nhận pick/drop hiện đến từ simulator/Admin; **không có nguồn quan sát PLC thật** (`IPlcObservationSource` không có dữ liệu) | – | – |

**Kết luận:** có thể bật thật **(a)** kết nối PLC chỉ-đọc và **(b)** giám sát đội AMR từ RCS. **Không thể** thực hiện tạo/thực hiện Transport Task với AMR thật, handshake PLC, hay đối chiếu Rack/Inventory thật cho tới khi có phần tích hợp bổ sung (mục 6).

## 2. Giới hạn môi trường của phiên làm việc này (đã kiểm tra)

- Máy phát triển này **không có Docker** (`docker` không tồn tại), **không có** `C:\ETEK-WCS`, **không nằm trong mạng 192.168.10.x** (IP 192.168.30.7/24 và 172.16.0.2).
- `ping` tới 192.168.10.101/111/121/131: **100 % mất gói** (chẩn đoán 2026-11-18 cũng ghi TCP không tới được). PostgreSQL cục bộ (service `postgresql-x64-16`) là CSDL **phát triển**, không phải CSDL triển khai.
- Địa chỉ/host RCS, token RCS, cổng TCP PLC và tham số MC **không** có trong repository (đúng thiết kế: nhập trong UI sau khi kỹ sư xác nhận).
- Hệ quả: phiên này **không thể** deploy lên máy vận hành, không thể kết nối PLC/RCS thật; mọi kết quả "đã chạy thật" **chưa có**. Triển khai/commissioning phải thực hiện **trên máy Deploy** trong mạng thiết bị, bởi người có quyền và có kỹ sư PLC/RCS bên cạnh.

## 3. Mapping hiện code đang dùng (không đổi)

- Mẫu tín hiệu: **23 tín hiệu/trạm** (IO Map v2, catalog `plc-io-catalog.json`); 4 PLC/16 trạm: PLC-01 A1–A4, PLC-02 B1/B2/D1/D2, PLC-03 X1/X2/X3/X6, PLC-04 E1/E2/G3/G7; `Actual_PLC_Address` do Admin nhập sau xác nhận của kỹ sư PLC (cơ sở dữ liệu triển khai quyết định địa chỉ nào đang có — **cần đọc trên máy Deploy**, chỉ SELECT).
- **Workbook 20/11/2026 chưa được triển khai** và **không** phải mapping đã duyệt (CLAUDE.md §7.8). Không tự tạo địa chỉ, không thay mapping cũ, không ghi tín hiệu theo 20/11.
- Chính sách ghi theo tên (`Safety_Ready`, `Transfer_Permit`, `AGV_Departure_Permit`, nhóm Safety/Interlock) vẫn chặn các tín hiệu đó; không được vô hiệu hóa để thử.

## 4. Triển khai lên máy Deploy (do người vận hành chạy; script có sẵn)

**Điều kiện:** Docker Desktop chạy; repo `C:\ETEK-WCS\src` ở nhánh `main`, sạch; `C:\ETEK-WCS\config\.env` tồn tại (ngoài repo).

1. Cập nhật `.env`: thêm (tùy chọn) `RCS_FLEET_SOURCE`, `RCS_FLEET_INTERVAL_SECONDS`, `RCS_DISPATCH_MODE=Legacy`; giữ `PLC_ALLOW_LIVE_CONNECTION=false` và `PLC_WRITE_ENABLED=false` cho lần deploy đầu. **Không** đặt `RCS_DISPATCH_MODE=RcsManaged`.
2. Xác nhận **đúng database mục tiêu** (volume `etek_wcs_pgdata`); `deploy.ps1` tự **backup** (`backup.ps1`) trước khi cập nhật nếu volume đã tồn tại; không dùng `-SkipBackup`.
3. Chạy `deployment\scripts\deploy.ps1` (không xóa volume, không `down -v`). Migrator áp **3 migration cộng thêm** theo thứ tự: `20261119120653_AddRcsFleetSnapshots` (CR-027), `20261121014633_AddRcsManagedDispatch` (CR-028; `transport_tasks.AgvId` thành nullable), `20261121033125_AddLegRobotCode` (CR-028; `transport_legs`). Không xóa dữ liệu nghiệp vụ.
4. `status.ps1`: backend healthy; đăng nhập; kiểm tra `GET /api/agvs/fleet` (nguồn `Simulator`, danh sách rỗng khi chưa bật Real).
5. **Rollback:** chỉ khi có kế hoạch phục hồi: ưu tiên khôi phục bản backup trước deploy (`restore.ps1`) hoặc quay về commit trước; **không** chạy rollback migration trên dữ liệu thật nếu chưa có kế hoạch (Down của `AddRcsManagedDispatch` dừng nếu có task chưa gán xe hoặc task RcsManaged đang chạy; `AddLegRobotCode` sao lưu robot code sang `transport_leg_robot_backup`).
6. Ở Production, tạo task bị từ chối `RCS_NOT_CONNECTED` khi chỉ có RCS simulator (CR-008A) – đúng thiết kế; không đặt `RCS_ALLOW_SIMULATOR_IN_PRODUCTION=true` trừ khi chạy có giám sát với RCS giả.

## 5. Commissioning theo cấp (mỗi cấp cần người có thẩm quyền xác nhận trước khi sang cấp sau)

| Cấp | Nội dung | Người có mặt | Điều kiện trước | Bằng chứng cần lưu | Dừng khi |
|---|---|---|---|---|---|
| **1** | **Kết nối và đọc PLC thật** (PLC-04 trước, theo CR-011) | Kỹ sư PLC | Máy Deploy ping/TCP được tới PLC (`plc-route-test.ps1 -Port <cổng xác nhận>`, chỉ mở/đóng TCP); tham số MC do kỹ sư xác nhận (port, network, PC, module I/O, station, timer, timeout; whitelist IP trên QJ71E71-100 là IP máy Windows); điểm IO cần đọc có địa chỉ xác nhận | Log `STATE: Connected`, bảng `plc_exchange_log` (MC request/response hex), chất lượng điểm `Good`, ảnh màn hình *Giám sát PLC*; so sánh giá trị với quan sát hiện trường | Lỗi end code, ghi mơ hồ, giá trị không khớp thực tế; **không** bật ghi |
| **2** | **Đọc danh sách AMR thật** | Kỹ sư RCS | Admin lưu RCS (host, token) trong *Kết nối RCS*; `RCS_FLEET_SOURCE=Real`; máy Deploy tới được `host:8182` | `GET /api/agvs/fleet` (`source=Real`, `simulatedData=false`), số lượng AMR so với RCS, RobotCode/vị trí/pin/trạng thái; kiểm tra AMR mất/biến mất (Stale/Missing); polling | Số lượng/dữ liệu lệch RCS; lỗi HTTP/timeout kéo dài |
| **3** | **Trạng thái các Station** (chỉ đọc) | Kỹ sư PLC | Cấp 1 đạt; điểm trạm được duyệt đọc | Ảnh *Giám sát PLC* theo trạm, log | Trạm báo bất thường |
| **4** | **Đồng bộ RobotCode** | Kỹ sư RCS | **Chưa thể** – cần `queryTaskStatus` thật (mục 6) | – | – |
| **5** | **Một giao dịch thực tế có giám sát** | Kỹ sư PLC + RCS + vận hành | **Chưa thể** – cần: adapter RCS thật, mapping Location/`taskTyp`, handshake PLC được xác nhận, interlock, xác nhận rack (CLAUDE.md §7.8 D-4) | – | – |
| **6** | **Vòng đời Pick/Drop đầy đủ** | như trên | Mapping, interlock, xác nhận rack đã **nghiệm thu** | – | – |
| **7** | **Lỗi kết nối, timeout, retry, recovery** | Kỹ sư PLC + RCS | Sau cấp 1–2: rút/khôi phục kết nối có kiểm soát ở **chế độ chỉ-đọc** | Log reconnect, chất lượng `Disconnected/Stale`, trạng thái khôi phục | Hành vi lạ |

Không tự phát lệnh di chuyển AMR hoặc ghi PLC ngoài kịch bản đã được kỹ sư vận hành xác nhận bằng văn bản.

## 6. Phần tích hợp còn thiếu để chạy dispatch/handshake thật (cần CR riêng, **chưa làm**)

1. **Adapter RCS thật** theo RCS-2000 API V3.3: `genAgvSchedulingTask` (5.2.1), `queryTaskStatus` (5.2.15), `continueTask` (5.2.2), `cancelTask`, và endpoint `agvCallback` (5.3.1). **Chặn bởi:** (a) ánh xạ **Location WCS → `positionCode`/`type`** và **BP01–BP06 → `taskTyp`/`podTyp`/`podDir`/`priority`** – không có trong tài liệu dự án (câu hỏi nhà cung cấp số 8); (b) **cách xác thực callback** (câu 3); (c) hành vi lặp `reqCode`, `data` response, đổi robot (câu 1, 2, 5, 9–15). Mapping phải là **cấu hình** (không hardcode) và để trống ⇒ dispatch từ chối; không suy đoán.
2. **Handshake PLC theo giao diện 20/11:** CR-029 mới là tài liệu; còn chờ PLC (địa chỉ, ngữ nghĩa, reset) và các quyết định D-1…D-4 → thiết kế/triển khai ở CR sau (hàng đợi FIFO cấp PLC chưa có).
3. **Heartbeat** (`HeartBit` 1 s, timeout 10 s): bộ ghi chưa tồn tại.
4. **Nguồn dữ liệu rack** (có mặt, danh tính) để đối chiếu Rack/Inventory (D-4): chưa xác định.
5. **Quan sát PLC thật cho pick/drop** (`IPlcObservationSource`).

Đề xuất: mở CR riêng "Real RCS dispatch adapter" chỉ gồm phần **được tài liệu RCS V3.3 xác nhận**, mapping là cấu hình, tắt mặc định, kiểm thử bằng máy chủ RCS giả theo tài liệu, rồi thử với RCS thật ở chế độ chỉ-đọc/giám sát trước khi gửi bất kỳ lệnh nào.

## 7. Thông tin cần từ chủ dự án để tiếp tục
1. Truy cập/người chạy `deploy.ps1` trên máy Deploy (phiên này không có đường tới máy đó).
2. RCS: host, token (nhập qua UI, không đưa vào chat/repo), và xác nhận `queryAgvStatus` cổng 8182.
3. PLC: cổng TCP và tham số MC đã xác nhận; địa chỉ IO đã duyệt; lịch có mặt của kỹ sư PLC; danh sách tín hiệu được phép ghi.
4. Quyết định mở CR "Real RCS dispatch adapter" và cung cấp mapping Location/`taskTyp` từ nhà cung cấp.

## 8. Mẫu nhật ký bằng chứng (điền khi commissioning)

| Thời gian | Cấp | Hành động | Người xác nhận | Kết quả | Tệp bằng chứng (log/ảnh/xuất chẩn đoán) |
|---|---|---|---|---|---|
| | | | | | |
