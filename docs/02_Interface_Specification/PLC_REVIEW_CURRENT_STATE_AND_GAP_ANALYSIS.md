# PLC Communication – Khảo sát hiện trạng và đối chiếu yêu cầu mới

**Trạng thái:** `DRAFT – CR-010 PENDING_APPROVAL`. Đây là tài liệu phân tích. Không có mã nguồn, database schema, migration hay cấu hình runtime nào bị thay đổi. Tài liệu baseline đã được duyệt (`docs/specification/*`, `README.md`, ADR, workbook v2) **không bị sửa**.
**Ngày:** 2026-11-17 · **Nguồn đầu vào:** `AGV_PLC_IO_Interface_List_10_11_2026.xlsx` (sheet `IO Matrix`, `Trang_tính1`), yêu cầu của chủ dự án (task ngày 2026-11-17), mã nguồn tại commit `970b1e7`, workbook baseline v2 lấy từ `git HEAD`.
**Tài liệu liên quan:** [Đặc tả đề xuất](PLC_COMMUNICATION_SPECIFICATION_DRAFT.md) · [Danh sách cần xác nhận](PLC_SPEC_OPEN_QUESTIONS.md) · CR-010 trong `docs/change-management/CHANGE_REQUESTS.md`.

---

## 0. Cách đọc: ba mức độ chắc chắn

Mọi nội dung trong bộ tài liệu này được gắn một trong ba nhãn, để không có điều gì bị "âm thầm" trở thành quy tắc:

| Nhãn | Nghĩa |
|---|---|
| **[XLS]** | Có trong file Excel `AGV_PLC_IO_Interface_List_10_11_2026.xlsx`. |
| **[YC]** | Do chủ dự án xác nhận trong yêu cầu ngày 2026-11-17 (không có trong Excel). |
| **[ĐX]** | **Đề xuất** của người phân tích, chưa được duyệt, dùng để mô hình hóa. Phải được chủ dự án/kỹ sư PLC xác nhận. |
| **TBD** | Chưa có căn cứ. Không được tự điền. |

## 1. Các tệp đã đọc

**Tài liệu và đầu vào**
- `docs/02_Interface_Specification/PLC_IO_Map/AGV_PLC_IO_Interface_List_10_11_2026.xlsx` – đọc đầy đủ cả hai sheet (từng ô), gồm cả 9 mũi tên trong hình của `Trang_tính1` (xác nhận chiều PLC→AGV / AGV→PLC).
- `ETEK_WCS_IO_Map_4PLC_16Stations_v2_Separate_IO_Sets.xlsx` (đọc từ `git HEAD`, commit `b831b7f`): 9 sheet – Read Me, PLC Configuration, Station Mapping, PLC1_IO…PLC4_IO, Handshake Definition, Address Allocation.
- `CLAUDE.md`, `docs/README.md`, `docs/specification/*` (business-rules §1.8, integration-requirements §5.2/5.3/5.9/5.10, process-flows, task-state-machine), `docs/decisions/ADR/ADR-0004-plc-communication-architecture.md`, `docs/change-management/CHANGE_REQUESTS.md` (CR-002, CR-003, CR-004, CR-008A/B), `CHANGELOG.md`, `docs/02_Interface_Specification/MC_PROTOCOL_REFERENCE_ANALYSIS.md` (tham chiếu).

**Mã nguồn (chỉ đọc)**
- Domain: `WCS.Domain/Plc/PlcEntities.cs`, `WCS.Domain/Intake/StationRequestRecord.cs`, `WCS.Domain/Tasks/TransportTask.cs` (trường `RequestKey`).
- Application: `WCS.Application/Plc/{PlcModels, PlcRuntime, PlcWorker, StationIoHandler, Handshake, PlcStationSignalService, PlcConfigService, AddressValidator}.cs`, `WCS.Application/Services/{StationRequestProcessor, TaskOrchestrator}.cs`, `WCS.Application/Abstractions/{Integration, Services, Persistence}.cs`.
- Infrastructure/Runtime/Api: `WCS.Infrastructure/Plc/{Catalog/plc-io-catalog.json, PlcPersistence.cs}`, `WCS.Infrastructure/Persistence/Repositories/Repositories.cs`, danh sách migration (`AddPlcTopology`, `AddStationRequests`), `WCS.Runtime/{DependencyInjection, HostedServices}.cs`, `WCS.Api/Api/PlcEndpoints.cs`.

## 2. Phát hiện về tình trạng làm việc của Git (cần chủ dự án xử lý)

Trong cây làm việc hiện tại: workbook được kiểm soát theo `CLAUDE.md` §7.1 `ETEK_WCS_IO_Map_4PLC_16Stations_v2_Separate_IO_Sets.xlsx` **đang ở trạng thái đã xóa** (` D`, chưa staged), và file mới `AGV_PLC_IO_Interface_List_10_11_2026.xlsx` **chưa được theo dõi** (`??`). Người phân tích **không** thực hiện các thay đổi này. Theo `CLAUDE.md` §7.6 (workbook vắng mặt thì dừng và báo cáo; không tạo IO map thay thế), việc phân tích này:

- dùng bản v2 từ `git HEAD` **chỉ để đối chiếu lịch sử**;
- coi file Excel mới là **đầu vào ứng viên**, chưa phải baseline được duyệt;
- đề xuất giữ nguyên v2 để truy vết, và nếu file mới được chấp nhận thì đăng ký làm **phiên bản mới** của workbook qua CR-010 (không ghi đè v2).

## 3. Nội dung thực tế của file Excel mới

### 3.1. Sheet `IO Matrix` – 22 dòng tín hiệu

Khớp **từng tên, địa chỉ, kiểu dữ liệu** với bảng trong yêu cầu của chủ dự án. Các chi tiết có trong file nhưng dễ bị bỏ sót:

- Cột J chứa ghi chú **"làm rõ"** ở `Request_ID` (D1500), `Rack_Type` (D1520), `Destination_Available` (D1514): chính tác giả file đánh dấu ba tín hiệu này chưa rõ.
- `Rack_Full` (D1510) và `Rack_Empty` (D1512): cột J ghi `=1: A1 đầy, =2: A2 đầy, =3: A3 đầy, =4: A4 đầy` và tương tự cho "trống". **Chỉ có A1–A4.**
- Cột STT bị **ngắt quãng**: PLC→Software có STT 1,2,3,5,6,7,4,13,8,9,10 (thiếu 11 và 12); Software→PLC có STT 14–23 và dòng cuối **không có STT**. Hai STT thiếu 11, 12 chính là `Station_Fault` và `Transfer_Complete` của bản v2 (xem 3.3).
- M9123: dòng cuối **không có Nhóm, không có Tên tín hiệu, không có STT**; chỉ có chiều, địa chỉ, kiểu BOOL và ý nghĩa "Xác nhận ra khỏi trạm".
- Dòng 14 để trống; `STRING` không ghi độ dài trong file (độ dài **254** là thông tin **[YC]**).
- Tiêu đề file (metadata): "Proposed I/O and handshake matrix including bidirectional heartbeat" – tức bản **đề xuất**.

### 3.2. Sheet `Trang_tính1` – 9 bước

Văn bản và hình mũi tên khớp với bảng 9 bước trong yêu cầu. Cột trái là phía AGV/phần mềm, cột phải là phía PLC; mũi tên đảo chiều (`flipH`) ở bước 1, 4, 6, 9 (PLC→phần mềm) và không đảo ở bước 2, 3, 5, 7, 8 (phần mềm→PLC), **khớp** với cột "Bên gửi". Sheet **không** có: điều kiện chuyển bước, timeout, reset, xử lý lỗi, thời điểm đọc thanh ghi dữ liệu, hay vai trò của `Safety_Ready`, `AGV_Assigned`, `AGV_ID`, `AGV_Arrived`, `Transfer_Permit`, `Error_Code`, `Heartbeat_SW`.

### 3.3. Khác biệt giữa file Excel mới và baseline v2 (workbook đang được kiểm soát)

| Tín hiệu | Baseline v2 (23 tín hiệu mẫu) | Excel mới | Loại khác biệt |
|---|---|---|---|
| `Station_Online` M9003 | "Trạng thái trạm sẵn sàng giao tiếp/vận hành" | "Xác nhận đã mở cửa Rack" | **Ngữ nghĩa đổi** |
| `Station_Ready` M9004 | "Trạm sẵn sàng thực hiện thao tác giao/nhận rack" | "Xác nhận đã Đóng/Nhả kẹp" | **Ngữ nghĩa đổi** |
| `Rack_Present` M9005 | "Cảm biến xác nhận có rack tại vị trí giao nhận" | "Xác nhận Giao/Nhận thành công" | **Ngữ nghĩa đổi** |
| `Station_Fault` M9009 (STT 11) | có: "Trạm lỗi, không cho phép giao/nhận" | **không có** | Bị loại |
| `Transfer_Complete` M9010 (STT 12) | có: "Rack đã chuyển giao thành công" | **không có** | Bị loại |
| `AGV_Arrived` M9103 | "AGV đã đến đúng điểm giao nhận" | "AGV đang xử lý nhiệm vụ liên quan đến trạm" | **Ngữ nghĩa đổi** |
| `Transfer_Permit` M9104 | "Cho phép bắt đầu chuyển rack…" | "Nhiệm vụ AGV gặp lỗi ảnh hưởng đến thao tác của trạm" | **Ngữ nghĩa đảo** (cho phép → báo lỗi) |
| `AGV_Departure_Permit` | **M9105** "Cho phép AGV rời trạm" | **M9120** "Yêu cầu mở cửa Rack" | Địa chỉ **và** ngữ nghĩa đổi |
| `AGV_Busy` | **M9106** "AGV đang xử lý nhiệm vụ" | **M9121** "Xác nhận ở vị trí Lấy/Trả Rack" | Địa chỉ **và** ngữ nghĩa đổi |
| `AGV_Fault` | **M9107** "Nhiệm vụ AGV gặp lỗi" | **M9122** "Xác nhận hoàn thành Lấy/Trả Rack" | Địa chỉ **và** ngữ nghĩa đổi (tên giữ nguyên, ý nghĩa **không còn là lỗi** [YC]) |
| (không có) | – | **M9123** "Xác nhận ra khỏi trạm" (chưa có tên) | Tín hiệu mới |
| Phạm vi | 16 bộ IO độc lập theo Trạm (368 điểm), khóa `PLC+Station+Signal` | Một bảng tín hiệu; không nêu theo Trạm hay theo PLC. Yêu cầu mới [YC]: mapping theo **PLC**, vị trí xác định bằng giá trị thanh ghi | **Mô hình đổi** |
| Địa chỉ thực | `Actual_PLC_Address` **để trống** chờ kỹ sư PLC; địa chỉ mẫu chỉ là tham chiếu | Excel ghi địa chỉ cụ thể (M9000…, D1500…) | Cần quyết định trạng thái "đã duyệt" (xem câu hỏi Q-IO-2) |
| Handshake | Sheet `Handshake Definition` là biểu mẫu trống, `PENDING` | `Trang_tính1` là trình tự 9 bước cụ thể | Có nội dung mới (nhưng thiếu điều kiện/timeout/reset) |

**Hệ quả quan trọng:** bốn thay đổi ngữ nghĩa đổi nghĩa của tín hiệu cùng tên (M9004, M9005, M9104, M9122). Mã hiện tại và tài liệu hiện tại (UI mô tả, `WritePolicy.BlockedSignals`, `HandshakeCoordinator.Observe`) đang gắn nghĩa **cũ** cho các tên này, nên **không thể** chỉ thay địa chỉ.

## 4. Hiện trạng kiến trúc PLC Communication

Tóm tắt theo tầng (mọi mệnh đề dưới đây đã được kiểm tra trong mã nguồn):

- **Kết nối:** một `PlcWorker` cho mỗi PLC, là người dùng duy nhất của socket (đọc, ghi, reconnect không chồng nhau); `IPlcEndpoint` tách vendor (MC 3E binary, đề xuất; có endpoint giả lập). Chế độ SIMULATOR (mặc định) / REAL_READ_ONLY / REAL_READ_WRITE; kết nối thật cần cả `Plc:AllowLiveConnection` và công tắc Admin.
- **Cấu hình kết nối:** bảng `plc_connections` (IP, Port, Protocol, NetworkNumber, PcNumber, ModuleIoNumber, PlcStationNumber, MonitoringTimer, Timeout, PollingInterval, timeouts theo thao tác, X/Y/W base). Mọi tham số mạng có thể `null` (NEEDS_CONFIGURATION), không có giá trị mặc định tự đặt. Admin sửa qua màn "Kết nối PLC" (`PUT /api/plc/connections/{plcId}`).
- **IO mapping:** bảng `station_io_points` (khóa `PlcId + StationId + SignalName`), nạp một lần từ catalog nhúng `plc-io-catalog.json` (4 PLC, 16 Trạm, 23 tín hiệu mẫu = 368 điểm; sinh từ workbook v2, có sha256). `ActualAddress` rỗng cho đến khi Admin nhập (`Configured`) và kỹ sư PLC duyệt (`Approved`); chỉ điểm `Approved` mới được đọc/ghi trên PLC thật (`IsPollable`). Kiểm tra trùng/giao nhau địa chỉ **trong cùng PLC** (`AddressValidator`); cùng địa chỉ trên PLC khác được phép.
- **Đọc dữ liệu:** `StationIoHandler` (một cho mỗi Trạm) giữ giá trị, chất lượng (`Good/Bad/Disconnected/Simulated/NotConfigured`), thời điểm; khi mất kết nối vẫn giữ giá trị cuối nhưng đánh dấu `Disconnected`.
- **Ghi:** hàng đợi ghi tuần tự trong worker, không pipeline; ghi mơ hồ (`WRITE_AMBIGUOUS`) **không bao giờ gửi lại**; `WritePolicy` chặn theo tên `Safety_Ready`, `Transfer_Permit`, `AGV_Departure_Permit` và nhóm Safety/Interlock.
- **Handshake:** `HandshakeCoordinator` chỉ **quan sát** (suy ra pha từ các bit PLC→phần mềm) và có động cơ luật đã duyệt; **không có luật nào được cấp phát** (`Handshake.Definitions` rỗng), nên **không bit nào được ghi**. Bộ nhớ của nó là trong tiến trình (mất khi khởi động lại).
- **Tiếp nhận yêu cầu:** `PlcStationSignalService` phát hiện **cạnh lên** của `Pickup_Request`/`Delivery_Request` theo từng Trạm đã có ánh xạ trong `Plc:Integration:StationFlows` (mặc định **rỗng**: không Trạm nào tạo được task), đọc `Request_ID` như chuỗi, tạo `StationRequest` với khóa `PlcId:Station:RequestID`. `StationRequestProcessor` lưu bền vào `station_requests` (khóa duy nhất) rồi mới tạo task (`CreateTaskAsync`); trạng thái `Pending/Accepted/Rejected/Failed`. `Request_Ack` **không được ghi** (chỉ log).
- **Quyền chuyển giao:** task vào `WaitingForPlc` khi tín hiệu AGV `PlcPermissionRequested`; `TaskOrchestrator` gọi `RequestTransferPermissionAsync`, hiện chỉ **ghi log** (`TRANSFER_PERMIT_NOT_APPROVED`). `PlcPermissionGranted`, `PickConfirmed`, `DropConfirmed` đến từ RCS/AGV (và người dùng), chưa từ M9003–M9005.
- **Vị trí và tồn kho:** WCS sở hữu tồn kho (quy tắc được bảo vệ); nguồn/đích do WCS chọn theo `FlowOptions`/inventory. PLC **không** cung cấp vị trí rack cho WCS; thanh ghi `Rack_Full`, `Rack_Empty`, `Destination_Available`, `Rack_Type` có trong catalog nhưng **không đọc ở đâu trong luồng nghiệp vụ**.
- **Persistence/recovery:** `station_requests` sống sót qua restart; pha handshake, trạng thái bit và bất kỳ "transaction" nào **không** được lưu.

## 5. Bảng đối chiếu: hiện trạng – yêu cầu mới – khoảng cách – đề xuất

| # | Hạng mục | Hiện trạng | Yêu cầu mới | Khoảng cách | Đề xuất (chi tiết trong Đặc tả) |
|---|---|---|---|---|---|
| 1 | Số lượng PLC | 4 PLC nạp từ catalog; **không có API thêm/vô hiệu PLC** (chỉ sửa kết nối); UI "4 PLC, mỗi PLC một form"; runtime duyệt tập PLC trong DB | Không hardcode tối đa 4; Admin thêm/sửa/quản lý PLC | Thiếu vòng đời PLC (create/disable); catalog và UI giả định 4 | Thêm quản lý PLC động; catalog chỉ còn là nguồn **template**, không là nguồn topology (§3, §9 Đặc tả) |
| 2 | Tham số kết nối | Đủ trường, nullable, Admin sửa | Bảo toàn, không hardcode | Không có (giữ) | Giữ nguyên; thêm PLC mới bắt đầu với tham số trống, không mặc định |
| 3 | Mô hình IO | Theo **Trạm** (16 bộ × 23); khóa `PLC+Station+Signal` | Theo **PLC**: bản sao độc lập của Default IO Mapping Template cho từng PLC; vị trí lấy từ giá trị thanh ghi | Mô hình khác căn bản; CR-002 §"16 bộ IO độc lập theo Trạm" bị thay thế một phần | CR-010 phải ghi rõ phần CR-002/ADR-0004 bị thay; migration dữ liệu (TBD) |
| 4 | Danh mục tín hiệu | 23 tín hiệu v2 (có `Station_Fault`, `Transfer_Complete`, M9105–M9107) | 22 tín hiệu + M9123 chưa có tên; M9120–M9122 thay M9105–M9107 | Catalog, UI, simulator, handshake observe đang dựa vào tín hiệu cũ | Tạo phiên bản template mới, giữ v2 để truy vết; không tự đặt tên M9123 |
| 5 | Địa chỉ mặc định | Template chỉ là *tham chiếu*; `ActualAddress` rỗng cho đến khi kỹ sư duyệt (CR-002) | Template mặc định mang địa chỉ theo Excel; sao chép khi thêm PLC | Mâu thuẫn nguyên tắc "không tự gán địa chỉ" của CR-002 | Quyết định Q-IO-2: bản sao ở trạng thái `Configured` (chưa `Approved`, chưa pollable) cho tới khi kỹ sư xác nhận |
| 6 | Heartbeat | `Heartbeat_SW` **không được ghi** ở đâu | M9100 đổi trạng thái định kỳ | Chưa có | Vòng heartbeat theo PLC; chu kỳ TBD (Q-HB) |
| 7 | Nhận request | Cạnh lên theo Trạm, cần `StationFlows`; khóa `PlcId:Station:RequestID`; mất cạnh nếu WCS tắt khi bit đã lên | PLC-level; đọc D1500…D1520; chống trùng theo Request_ID | Không đọc thanh ghi vị trí/loại rack; phát hiện theo cạnh; khóa chứa Station | Phát hiện theo **mức + Request_ID chưa gặp**; khóa `PlcId:RequestID`; chụp nguyên tử thanh ghi |
| 8 | Ack M9101 | Không ghi | Bước 2 | Chưa có | Xem §6.4 Đặc tả (thời điểm ack so với đọc dữ liệu) + Q-ORD-1 |
| 9 | 9 bước handshake | Chưa có; tín hiệu nghiệp vụ `PlcPermissionRequested/Granted` đến từ nơi khác | 9 bước M9001/2 → M9005 | Chưa có; chưa rõ M9003–M9005 ứng với `PlcPermissionGranted/PickConfirmed/DropConfirmed` nào | Ánh xạ ĐX trong Đặc tả §7; cần chủ dự án duyệt (Q-MAP-1) |
| 10 | `Transfer_Permit` | Chưa ghi; ngữ nghĩa cũ "cho phép"; bị chặn theo tên | Chỉ báo lỗi; WCS tự reset khi hết lỗi trong lúc AMR chạy | Ngữ nghĩa đảo; điều kiện lỗi chưa định nghĩa | Xem §7.3; điều kiện lỗi = TBD (Q-ERR-1); rà lại danh sách chặn theo tên |
| 11 | Queue | Không có queue theo PLC. `ListPendingAsync` sắp theo `FirstSeenAt` trên **toàn bộ** request (một processor chung) | Queue FIFO **riêng cho mỗi PLC**, không queue toàn cục, FIFO theo thời điểm tiếp nhận, không ưu tiên Pickup/Delivery | Thiếu queue theo PLC; thiếu thứ tự xác định khi trùng thời điểm | Số thứ tự đơn điệu theo PLC được cấp lúc ghi bền (Đặc tả §9) |
| 12 | Transaction | Không có; chỉ `station_requests` | Mỗi PLC tối đa 1 transaction; PLC khác song song | Thiếu hoàn toàn | Thực thể transaction có `LastCompletedStep` (Đặc tả §9–10) |
| 13 | Liên kết AGV task | `station_requests.TaskId`, `TransportTask.RequestKey` | Lưu quan hệ PLC–request–transaction–task; không tạo task thứ hai cho cùng request | Có phần; thiếu PLC/transaction | Mở rộng liên kết; ràng buộc duy nhất giữ nguyên |
| 14 | Mất kết nối PLC | Worker đánh dấu `Disconnected`, giữ giá trị cuối, backoff; ghi bị từ chối `NOT_CONNECTED`; ghi mơ hồ không gửi lại | Giữ transaction; không hủy/không reset; kết nối lại → đọc IO thực, đối chiếu DB, tiếp tục đúng bước | Chưa có đối chiếu và tiếp tục; không có transaction để giữ | Quy trình reconcile (Đặc tả §11) |
| 15 | Khởi động lại WCS | `station_requests` Pending còn; pha/bit không lưu | Khôi phục transaction từ DB | Chưa có | Đặc tả §12; không phát lệnh vật lý trước khi đối chiếu |
| 16 | Location mapping | `plc_stations.LocationCode` (Trạm→Location, tùy chọn, Admin) | Mapping giá trị thanh ghi PLC → Location, riêng từng PLC | Không có mapping giá trị→Trạm/Location | Bảng Location Mapping theo PLC (Đặc tả §5, §8) |
| 17 | Xác thực dữ liệu | Chỉ kiểm tra `Request_ID` đọc được | Kiểm tra hợp lệ trước khi tạo/dispatch; không tự chọn vị trí thay thế | Thiếu | Danh sách luật V1–V12 (Đặc tả §8) |
| 18 | `Error_Code` D1610 | Không ghi | Mã lỗi giao tiếp/vận chuyển | Bảng mã **chưa có** | TBD (Q-ERR-1) |
| 19 | Xung đột với quy tắc được bảo vệ | WCS sở hữu tồn kho; nguồn/đích do WCS chọn; A3/A4, D1/D2 UNCHANGED; BP-04 REVIEW | PLC cho biết vị trí rack/đích | Xung đột tiềm ẩn nếu PLC và tồn kho WCS khác nhau; A3/A4 nằm trong dải giá trị 1–4; Delivery tại B1/B2 = BP-04 | Nguyên tắc: dữ liệu PLC là **bằng chứng nhận diện**, không ghi đè tồn kho; khác nhau → reconciliation (Q-INV-1); A3/A4, BP-04 → giữ monitor-only/REVIEW |
| 20 | UI/API/quyền | 3 màn (Kết nối, IO, Giám sát); Admin cấu hình, Operator chỉ giám sát | Thêm PLC, chỉnh mapping, Location Mapping | Chưa có màn/API cho template, thêm PLC, location mapping | Phạm vi UI/API liệt kê ở §6 CR-010; Operator vẫn chỉ giám sát |
| 21 | STRING Request_ID | `AddressValidator`: STRING tính theo **word** (1–960) | "Chiều dài 254" (đơn vị chưa nêu) | **Chồng lấn thanh ghi** (xem 6) | Q-STR-1 |

## 6. Phát hiện kỹ thuật cần lưu ý đặc biệt

1. **Request_ID dài 254 chồng lấn các thanh ghi kế tiếp.** Nếu 254 là số ký tự (≈127 word) hay số word, vùng `D1500…` chạm `D1510` (`Rack_Full`), `D1512`, `D1514`, `D1520`, `D1600` (`AGV_ID`), `D1610` (`Error_Code`). `AddressValidator` hiện tại sẽ báo giao nhau trong cùng PLC. Chỉ khi vùng *thực sự dành cho chuỗi* ≤ 10 word thì không chồng. Đây là điểm mâu thuẫn trong bảng IO, **không tự sửa** → Q-STR-1.
2. **PLC chỉ có một khối dữ liệu yêu cầu** (một `D1500`, một `D1510`…): tại một thời điểm PLC chỉ trình bày được **một** request. Nếu bốn Trạm của một PLC cùng cần xe, PLC phải tự phân xử; WCS chưa biết cách (Q-PLC-1).
3. **Chín bước chỉ mô tả Trạm tự phát request.** Các chặng do WCS khởi tạo ở Trạm khác (ví dụ BP-02 lấy rack trống tại B1/B2 cho yêu cầu của A1/A2; BP-01 thả tại B1/B2) **không có trong sheet** (Q-LEG-1).
4. **Thời điểm bật M9120** (mở cửa) trong sheet nằm ngay sau M9101 và *trước* khi AGV đến (M9121); mối quan hệ với `Safety_Ready` và tiến độ AGV chưa được nêu (Q-ORD-2).
5. **`M9xxx`:** cần kỹ sư PLC xác nhận các thiết bị M9000–M9123 tồn tại trong cấu hình điểm thiết bị `M` và không trùng vùng rơ-le đặc biệt của họ PLC được dùng (Q-DEV-1).

## 7. Báo cáo ảnh hưởng (theo `CLAUDE.md` §3 bước 3–4)

| Khu vực | Ảnh hưởng nếu CR-010 được duyệt |
|---|---|
| Đặc tả cần cập nhật (sau duyệt) | `specification/business-rules.md` §1.8; `integration-requirements.md` §5.2, 5.3, 5.10; `README.md`; ADR-0004 (bổ sung/thay thế mục 3, 4, 7, 8); workbook phiên bản mới; `IMPACT_MATRIX`, `REQUIREMENT_TRACEABILITY`, `CHANGELOG` |
| Luồng nghiệp vụ | BP-01/02/03/05/06 không đổi hành vi; BP-04 giữ REVIEW; A3/A4 và D1/D2 giữ UNCHANGED; F5 không có PLC |
| Dữ liệu | Template IO theo PLC, mapping theo PLC, location mapping, request/transaction/queue; chuyển đổi dữ liệu từ `station_io_points` (TBD) |
| API | Thêm/vô hiệu PLC, template, mapping theo PLC, location mapping, xem transaction/queue; API cũ theo Trạm cần quyết định giữ/ngừng |
| Tín hiệu PLC | Tập tín hiệu và ngữ nghĩa mới; chặn ghi theo tên phải rà lại |
| RCS | Không đổi giao thức; thứ tự tạo task thay đổi (sau xác thực); không đổi `FlowOptions` |
| UI | Màn Kết nối PLC, Cấu hình địa chỉ IO, Giám sát PLC; mới: quản lý PLC, template, location mapping, transaction/queue |
| Backend | PLC runtime, intake, handshake, persistence; **không** đụng `TaskStateMachine`, `InventoryService`, Scheduler, PER_AGV (cần xác nhận riêng nếu tương tác với `WaitingForPlc` và `StateTimeoutsSeconds`) |
| Kiểm thử | Simulator cần sinh theo tập tín hiệu mới; test intake/handshake/recovery mới; hồi quy các test PLC hiện có (CR-002/003/004/008B) |
| Quy tắc được bảo vệ | PER_AGV, BP-04, WCS sở hữu tồn kho, A3/A4 & D1/D2, không bind rack — **được giữ**; điểm tiếp xúc ghi ở Q-INV-1, Q-LEG-1 |

## 7A. Bổ sung 2026-11-18 – hợp nhất với quy trình nghiệp vụ (Station Transaction / Transport Task)

Chi tiết ở [PLC_BUSINESS_PROCESS_AND_TRANSACTION_MODEL_DRAFT.md](PLC_BUSINESS_PROCESS_AND_TRANSACTION_MODEL_DRAFT.md). Hiện trạng đối chiếu thêm:

| Hạng mục | Hiện trạng (đã kiểm tra trong baseline/mã) | Yêu cầu mới | Khoảng cách | Đề xuất |
|---|---|---|---|---|
| Hai cấp Task/Station Transaction | Một `TransportTask` + `station_requests`; không có khái niệm Leg hay Station Transaction | Hai cấp độc lập, Task nhiều chặng | Chưa có Leg; liên kết Task↔request chỉ 1-1 | Lớp điều phối Leg bổ sung, **không** sửa `TaskStateMachine` (Q-MLEG-1) |
| Nhiều chặng | `WAITING_FOR_DESTINATION` giữ AGV; baseline §3.6 nói nhiều chặng "phải được duyệt" | A→buffer→B với tồn kho cập nhật tại buffer | Chưa có luồng buffer-rồi-tiếp-tục trong baseline | Chờ Q-MLEG-1, Q-AGV-1, Q-BUF-1 |
| Cổng độc lập | Excel: một bộ tín hiệu/PLC; [YC] cũ: 1 transaction/PLC | Cổng độc lập, cùng PLC lẫn khác PLC | Mâu thuẫn trực tiếp nếu "cổng" ≠ PLC | Q-GATE-1 (chặn); mô tả theo "Gate" |
| Giải phóng cổng | Chưa có | Khi AMR rời cổng | Bước 8 hay 9? | Q-REL-1 |
| "Một task một thời điểm" | BR-01 = `PER_AGV`; câu nằm ở `Process in detail` slide 4, không chép vào baseline | Không tự bỏ giới hạn | BR-01 (đã duyệt) cao hơn slide nguồn | Q-SLD-1; áp dụng ở cấp task theo AGV |
| Nguồn process | Hai .pptx ở `7. FFC AMR/` (ngoài thư mục dự án); đã đọc văn bản | Đọc hai file .pptx | Hình vẽ/mũi tên không đọc được từ văn bản; Phase 2 chưa thuộc baseline | Bảng BP đối chiếu slide 5–10; Phase 2 chỉ làm bối cảnh (Q-PH2-1, Q-LOC-1) |
| Buffer→cổng máy tự động | Slide 5–10: thả vào buffer F kết thúc task; chặng buffer→B1/B2 chỉ ở BP-04 (`REVIEW`) | A→buffer→B tự động | Không có trong luồng đã duyệt | Q-MLEG-2 (chặn) |

## 8. Xác nhận phạm vi thay đổi của đợt phân tích này

Chỉ **thêm** tài liệu mới trong `docs/02_Interface_Specification/` và **thêm** mục CR-010 vào `docs/change-management/CHANGE_REQUESTS.md`. Không có tệp mã nguồn, migration, cấu hình runtime, workbook, hoặc tài liệu baseline nào bị sửa.
