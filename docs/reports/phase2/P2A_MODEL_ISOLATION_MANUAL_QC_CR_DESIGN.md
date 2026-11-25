# P2-A – Thiết kế CR: Product Model Isolation & Manual QC Protection

| | |
|---|---|
| **Ngày / Branch** | 2026-11-25 · `feature/ph2-first-runnable` (merge CR-030 vẫn đang dở, **không hoàn tất**) |
| **Loại** | **Chuẩn bị implementation (thiết kế CR).** Chưa sửa code, DB, migration, cấu hình, dữ liệu tham chiếu, workbook, PPTX, tài liệu P0. Không commit/push/deploy |
| **Trạng thái** | `DRAFT – chờ chủ dự án phê duyệt CR`. Không OQ nào được đóng; mọi phương án là **đề xuất để quyết định** |
| **Nhãn** | **[ĐÃ XÁC MINH CODE]** đọc trực tiếp (kèm file:dòng) · **[ĐX]** đề xuất · **[OPEN]** chờ quyết định |
| **Đầu vào** | `P2_PROCESS_MODEL_SCOPE_DESIGN.md`, `P2_ROUTE_TRACEABILITY_MATRIX.md`; `CLAUDE.md` §2–§4 |
| **Đi kèm** | `P2A_IMPLEMENTATION_CHECKLIST.md` |

---

## 1. Executive Summary

- Hai bất biến cần bảo vệ: **(1)** Rack không đi sai Product Model; **(2)** Rack ở Manual QC không quay lại điều phối tự động ngoài quy trình tiếp nhận lại được phê duyệt.
- Code hiện tại **chưa có** Model, Station Role, Disposition; Rack được tạo chỉ qua hai API tồn kho thủ công (`InventoryService.cs:169` initial-stock, `:201` adjustments) và không mang Model. Bộ lọc nguồn Rack nằm ở **một query dùng chung** (`Repositories.cs:59-62`) mà **cả đường legacy lẫn đường Route Engine** cùng dùng ⇒ bộ lọc mới phải là **tham số tùy chọn**, không đổi hành vi legacy.
- **Phát hiện quan trọng cho thiết kế:** `LocationType.ManualBuffer` đang được **luồng Phase 1 dùng thật** (F5 – BP-05/BP-06 là nguồn/đích hợp lệ, `reference-processes.json`). Vì vậy **không được đổi nghĩa `ManualBuffer` toàn cục**; việc bảo vệ Manual QC phải gắn vào thuộc tính **mới, additive** (`StationRole=ManualQcStation` và `Rack.Disposition`), chỉ áp dụng cho M5/M6, để F5 và mọi luồng Phase 1/legacy **không đổi**.
- **Rủi ro rollback:** nếu bỏ cột `Disposition` thì mọi Rack `ManualHandling` bỗng thành tự động điều phối được ⇒ migration `Down` phải **từ chối** khi còn Rack `ManualHandling` (theo tiền lệ `ROLLBACK_REFUSED` của migration RcsManagedDispatch).
- Phạm vi đầu tiên được xem xét: **chỉ F-Bal 10R80** (A1,A2,B1,B2,P1,P2,M5). F-Bal 10R60 là PROTECTED; F-Pis chưa đủ căn cứ kỹ thuật (Y/Z, X4/X6, P3/P4/M6 chưa tồn tại).
- Implementation **chưa nên bắt đầu** cho tới khi quyết định tối thiểu P2-OQ-03, 04, 07, 08 (và dữ liệu Location P1/P2/M5 – P0 OQ-11); xem §11.

---

## 2. Phạm vi CR

| Trong phạm vi | Ngoài phạm vi (không đụng) |
|---|---|
| Entity `ProductModel` (nếu P2-OQ-03 chọn bảng) | Queue Request/Held, Scope/FIFO |
| `Location.ProductModelId`, `Location.StationRole` | PLC runtime, PLC IO mapping, giao thức |
| `Rack.ProductModelId`, `Rack.Disposition` | `HikRcsClient`, `RealLegDispatcher`, Q1PICK/Trigger |
| `BusinessProcess.ProductModelId` (+ snapshot tùy chọn) | Dữ liệu `reference-processes.json` (sửa dữ liệu là CR riêng) |
| Validator Publication (INV-M1, INV-M4) | Hành vi A3/A4/D1/D2 (PROTECTED) |
| Ràng buộc runtime (INV-M2, INV-M3, INV-M5) tại Route Engine + repository | F-Pis, F-Bal 10R60 (cấu hình thật) |
| Migration additive + rollback an toàn | Giao diện nhập liệu đầy đủ (chỉ hiển thị/quản trị tối thiểu) |
| Audit/Inventory transaction cho gán Model và đổi Disposition | |
| Test acceptance | |

---

## 3. Architecture hiện tại [ĐÃ XÁC MINH CODE]

| Chủ đề | Hiện trạng | Hệ quả cho CR |
|---|---|---|
| Tạo Rack | Chỉ `InventoryService.cs:169` (initial-stock) và `:201` (adjustments) + test/seeder. Không có Model/Origin | Điểm tự nhiên để **bắt buộc/gán Model** (P2-OQ-04) |
| Chuyển vị trí Rack | `Rack.BeginTransit` (`RouteEngine.cs:593`, `InventoryService.cs:113`), `Rack.PlaceAt` (`RouteEngine.cs:640`, `InventoryService.cs:142`, `:244` đối soát), `MarkUnknown` (`:86`) | Bốn nơi `PlaceAt` ⇒ cần **một chỗ tập trung** áp quy tắc Disposition khi đặt Rack vào Manual QC |
| Chọn Rack nguồn | `FindUnreservedAsync`/`ListUnreservedAsync` (`Repositories.cs:59-68`) dùng ở: `PlcRequestService.cs:96`, `RouteConditionEvaluator.cs:40,43,66`, `RouteEngine.cs:269`, `InventoryService.cs:38` (**legacy**), `:209` (điều chỉnh tồn kho) | Thêm tham số lọc **tùy chọn**; legacy giữ nguyên |
| Kiểm tra process | `ProcessValidator.cs:36-157` (`UseLocation`, `LOCATION_PROTECTED`, `RACK_TYPE_MISMATCH`, `BUFFER_NOT_ALLOWED`: coi `ManualBuffer` như Buffer ở `:149,156`) | Thêm mã lỗi Publication mới |
| Chạy | `RouteEngine.CreateLegAsync :230-290` (`SourceMode.Request`/`Candidates` → `FindUnreserved`), `ConfirmDropAsync :620-660` (**nơi duy nhất** commit Drop của Route Engine) | Hook Model check + Disposition |
| Snapshot | `ProcessSnapshot` ghi cấu hình vào Order (record, đọc bằng JSON) | Thêm trường nullable tương thích ngược |
| Cấu hình chuyển | CR-026 `ConfigGroups.cs`: Location (`:97-137`) và Business Process có danh sách trường tường minh | Trường mới phải **tùy chọn** và import gói cũ **không xóa** giá trị mới |
| API/DTO | `LocationDto`, `RackDto` (`Dtos.cs:13-18`), `LocationConfigRequest` (`ApiModels.cs:59`), `InitialStockRequest/StockAdjustmentRequest` (`:32,38`), `GET inventory/racks` (`Endpoints.cs:150`), `cfg.MapPost/MapPut locations` (`:273,277`) | Thêm trường **cuối, tùy chọn** (không phá hợp đồng) |
| Legacy dùng ManualBuffer | F5 (Phase 1 BP-05/06) | **Không được** đổi nghĩa `ManualBuffer` |
| Tests hiện có | `new Rack(…)` 5 tham số ở `InventoryDomainTests.cs`, `ConcurrencyTests` dùng F5 `ManualBuffer` | Giữ chữ ký constructor; giá trị mặc định tương thích |

---

## 4. Product Model Design

### 4.1 Mô hình dữ liệu đề xuất [ĐX]

| Thành phần | Đề xuất | Tương thích |
|---|---|---|
| `product_models` | `Id`, `Code` (unique, ví dụ `F-BAL-10R80`), `Name`, `Enabled`, `CreatedAt` — **nếu** P2-OQ-03 chọn bảng; nếu chọn mã chuỗi thì thay bằng cột `varchar` + danh mục cấu hình | Bảng mới, không ảnh hưởng bảng cũ |
| `locations.ProductModelId` | nullable FK; NULL = **không thuộc Model nào** (legacy, F1–F5, Waypoint) | Dữ liệu cũ nguyên vẹn |
| `locations.StationRole` | nullable enum-text: `CncStation`, `AutoQcStation`, `BufferPosition`, `ManualQcStation`, `Other` | Không đổi `LocationType`; NULL = hành vi cũ |
| `racks.ProductModelId` | nullable FK; NULL = **chưa xác định Model** | Rack cũ không được gán tự động |
| `racks.Disposition` | `Auto` (mặc định) \| `ManualHandling` | Cột NOT NULL default `'Auto'` — **không** đổi hành vi dữ liệu cũ |
| `business_processes.ProductModelId` | nullable FK; NULL = process legacy/không ràng buộc Model | Process Version cũ không bị gán |
| `ProcessSnapshot.ProductModelId` | trường nullable thêm cuối | Order cũ deserialize OK |
| Lịch sử | thêm `InventoryEventType` mới (text, additive): `ModelAssigned`, `DispositionChanged` vào `inventory_transactions` | Giữ lịch sử; không bảng audit mới |

### 4.2 Quy tắc đề xuất
1. Rack **tham gia Phase 2** phải có `ProductModelId`. Rack NULL **không** được chọn làm nguồn cho process có Model (INV-M2), và không bị tự động gán.
2. Process có `ProductModelId` chỉ được khai báo Location cùng Model (INV-M1). Location trung lập (Waypoint/WaitingPosition, Model NULL): cho phép hay không là **P2-OQ-03b [OPEN]** (đề xuất mặc định: cho phép chỉ khi không lưu Rack).
3. Process có `ProductModelId = NULL` (legacy) **không đổi hành vi** và **không được** dùng Location/Rack có Model (tránh legacy lấy Rack của Phase 2): quy tắc phụ INV-M6 [ĐX].
4. Model không suy ra từ vị trí hiện tại của Rack (không dùng "Rack ở P1 ⇒ F-Bal").

### 4.3 Nguồn dữ liệu Model của Rack – các phương án (P2-OQ-04, **không chọn**)

| PA | Cách | Ưu | Nhược / hệ quả |
|---|---|---|---|
| **A** | **Khai báo khi nhập tồn kho:** `initial-stock`/`adjustments` bắt buộc chỉ định Model cho Rack mới; Rack cũ do Admin gán Model **từng Rack** qua thao tác có audit (`ModelAssigned`) | Đơn giản, dùng điểm tạo Rack sẵn có (`InventoryService.cs:169,201`); rõ trách nhiệm | Phụ thuộc người nhập đúng; Rack số lượng lớn cần gán hàng loạt (có preview); không tự sửa được lỗi nhập |
| **B** | **Kế thừa từ Origin CNC:** Model = Model của CNC nơi Rack xuất phát lần đầu | Tự động, nhất quán với Origin (P0 D-02) | Cần Origin (chưa có); Rack đặt nhầm CNC sẽ **lan truyền** lỗi; Rack cũ không có Origin |
| **C** | **Danh mục ngoài:** tra `ExternalRackId`/mã nhận dạng → Model (master data / hệ thống khác / thiết bị đọc) | An toàn nhất nếu có mã Rack tin cậy | Cần dữ liệu và nguồn đọc mã (ADR-0011 D-4); `ExternalRackId` nay tùy chọn (`Rack.cs:9-11`); phạm vi lớn |
| **D** | ~~Suy từ vị trí hiện tại~~ | Không cần dữ liệu | **Không khuyến nghị/không được dùng**: P2 §4.2.4 và yêu cầu của chủ dự án cấm suy tùy tiện; một Rack đặt nhầm vị trí sẽ bị gán sai Model và "hợp thức hóa" lỗi |

Kết hợp khả dĩ (A cho dữ liệu tồn kho hiện có + C/B cho dài hạn) là **lựa chọn của chủ dự án**; CR này **chỉ hỗ trợ điểm gán và audit**, không chọn nguồn.

### 4.4 Các tình huống cần xử lý

| Tình huống | Hành vi đề xuất |
|---|---|
| Rack cũ chưa có Model | `ProductModelId=NULL`; bị loại khỏi nguồn process có Model; **không** auto-gán; hiển thị "Chưa xác định Model" trong báo cáo/API |
| Rack nhập mới | PA A: Model bắt buộc **khi nhập vào Location có Model** (nếu Location NULL-Model thì Model tùy chọn) |
| Rack được đối soát (`InventoryService.cs:244`) | Không đổi Model; nếu vị trí thực tế là Location khác Model ⇒ **không tự sửa**: đánh dấu `ReconciliationRequired` + event `MODEL_LOCATION_MISMATCH` (không bịa) |
| Rack đi qua Buffer | Model giữ nguyên (thuộc tính Rack, không thuộc Location); Buffer chỉ chứa Rack cùng Model (INV-M2 khi đặt đích) |
| Rack có CurrentLocation nhưng Model không rõ | Rack NULL ở Location có Model ⇒ cờ cảnh báo vận hành; không điều phối |
| Process Version cũ không có Model | Giữ nguyên, không gán, hành vi legacy (INV-M6 chặn việc legacy chạm Rack/Location có Model) |
| Location đã có Model đổi Model | Bị từ chối khi Location đang chứa Rack hoặc đang được Published Version tham chiếu (tránh sai lệch âm thầm) [ĐX] |

---

## 5. Manual QC Design

### 5.1 Tách `BufferPosition` ≠ `ManualQcStation`
- Thêm `StationRole` (không đụng `LocationType`). `ManualQcStation` chỉ gán cho M5/M6 (và Manual QC tương lai). **F5 `ManualBuffer` của Phase 1 giữ `StationRole=NULL`** ⇒ mọi hành vi hiện tại của F5, BP-05/06 và test hiện có **không đổi**.
- Validator mới: Location `ManualQcStation` **không** được là candidate của step `BufferStorage/BufferRetrieval` (INV-M4a) và **không** được là `Source` của process tự động (INV-M4b); nó **được** là `Destination` Priority cuối của step chuyển Rack vào xử lý thủ công.

### 5.2 `Rack.Disposition` [ĐX; P2-OQ-08]

| Phương án | Mô tả | Nhận xét |
|---|---|---|
| **D1 (đề xuất đánh giá)** | Cột riêng `Disposition (Auto \| ManualHandling)` | Không đổi ngữ nghĩa `RackStatus`; tránh phá switch hiện có (`RackStatus` dùng ở `Rack.cs:36,46`, `StatusOf` trong Route Engine/evaluator/PLC); dễ thêm giá trị sau |
| D2 | Thêm `ManualHandling` vào `RackStatus` | Một trạng thái duy nhất nhưng **trộn "loại Rack Full/Empty" với "ai xử lý"**: Rack ManualHandling mất thông tin Full/Empty, phá `StatusOf(RackKind)`/`Unreserved(status)`; ảnh hưởng PLC `RackFull/RackEmpty` |
| D3 | Chỉ dựa `Location.StationRole` (không cột Rack) | Không đủ: Rack có thể bị đặt lại/đối soát đi nơi khác, mất cờ; không audit chuyển giao |

Quyết định D1/D2/D3 thuộc chủ dự án; thiết kế dưới đây dùng **D1** làm cơ sở để mô tả chi tiết, và chỉ ra rằng D2 buộc thay đổi lớn hơn.

### 5.3 Vòng đời
1. Rack Full/Empty ở Buffer/QC `Disposition=Auto`. Process đặt M5/M6 là đích Priority cuối.
2. **Chỉ khi `ConfirmDropAsync` được xác nhận** (bằng chứng vật lý – `RouteEngine.cs:620-660`) và đích có `StationRole=ManualQcStation`: trong **cùng giao dịch** đặt `Disposition=ManualHandling` + `InventoryTransaction(DispositionChanged)`; Rack **vẫn** `Status=Full|Empty`, `CurrentLocationId=M5`, giữ Rack ID/Model/RackType/Origin; lịch sử không bị xóa.
3. Dispatch/RCS nhận task **không** đổi Disposition (đúng quy tắc "không đổi CurrentLocation khi mới dispatch").
4. **Không có đường tự động** `ManualHandling → Auto`.
5. **Tiếp nhận lại** (re-intake): chỉ qua thao tác riêng được phê duyệt — phương án (P2-OQ-07, **không chọn**):

| PA | Mô tả | Hệ quả |
|---|---|---|
| R1 | Thao tác Admin "Re-intake Rack" (endpoint riêng, lý do bắt buộc, xác nhận vị trí/Model, audit `DispositionChanged`), không phải process | Đơn giản, nhưng cần định nghĩa người duyệt, điều kiện (Model hợp lệ, vị trí vật lý đối soát) |
| R2 | Process "re-intake" có cờ `IsReintake` được phê duyệt riêng; chỉ loại process này được phép chọn Rack `ManualHandling` làm nguồn | Phù hợp mô hình process, nhưng thêm ngoại lệ vào INV-M3 (phải có kiểm tra cờ ở runtime lẫn Publication) |
| R3 | **Chưa cho phép** trong giai đoạn này; Rack ManualHandling chỉ rời bằng đối soát/điều chỉnh thủ công ngoài WCS | An toàn nhất, nhưng không có đường quay lại |

### 5.4 PH2-05 / PH2-06 [ĐÃ XÁC MINH CODE]
`reference-processes.json`: PH2-05 (`ManualRequest`, Empty, trigger **M5**) và PH2-06 (`ManualRequest`, Full, trigger **M5**), `SourceMode.Request` ⇒ nguồn = Rack tại M5 (`RouteEngine.cs:240-243`). Với INV-M3/M4 các process này sẽ **không thể Publish** (Source là Manual QC) và/hoặc **không có Rack hợp lệ** lúc chạy (Rack ManualHandling bị lọc). **Không** coi chúng là re-intake. Phương án xử lý dữ liệu (CR dữ liệu riêng, không làm ở đây; **P2-OQ-07 giữ mở**):

| PA | Nội dung |
|---|---|
| X1 | Giữ trong dữ liệu nhưng chuyển `reviewBlocked`/Disabled cho tới khi được phê duyệt là re-intake (R2) |
| X2 | Biến thành quy trình re-intake được phê duyệt (R2) với cờ riêng |
| X3 | Loại khỏi gói mặc định; thay bằng thao tác R1 |
| X4 | Giữ nguyên (không chọn) và chấp nhận rủi ro G6 — **không khuyến nghị** |

---

## 6. INV-M1…M4 (+M5, M6) – enforcement

Nguyên tắc: kiểm tra **ở cả Publication lẫn runtime**; UI/API không là tầng bảo vệ duy nhất; dữ liệu có thể đổi sau Publish hoặc tồn tại từ trước.

| ID | Bất biến | Tầng kiểm tra | Service / repository | Cách bypass có thể xảy ra | Mã lỗi | Hành vi khi vi phạm | Test |
|---|---|---|---|---|---|---|---|
| **INV-M1** | Process và mọi Location candidate/trigger cùng Model | (1) **Publication**: `ProcessValidator.UseLocation` (`:50-62`); (2) **Runtime**: khi chọn candidate trong `CreateLegAsync`/evaluator | `ProcessValidator`, `RouteConditionEvaluator` | Sửa Model của Location sau Publish; import cấu hình (CR-026) bỏ qua UI; Process Version cũ | `PROCESS_MODEL_MISMATCH` (Publication, Error); `MODEL_MISMATCH` (runtime) | Publication: **chặn Publish**. Runtime: candidate bị **loại** + event `MODEL_MISMATCH` (fail-closed); hết candidate ⇒ chờ theo `WaitBehavior` (không chọn Location ngoài danh sách) | MI-02, MI-12, MI-13 |
| **INV-M2** | Rack, Source, Destination cùng Model (với process có Model) | **Runtime** `CreateLegAsync :230-290` + repository; **không** chỉ Publication | `IRackRepository` (overload lọc), `RouteEngine` | Rack NULL-Model; Rack bị đối soát sang Location khác Model; Rack gán Model sau khi Reservation; ghi trực tiếp DB | `MODEL_MISMATCH`, `RACK_MODEL_UNKNOWN` | Không tạo Leg; không Reservation; event + giữ Order chờ (không chọn Rack khác Model); **không chặn một Pick/Drop vật lý đã xảy ra** (xem ghi chú) | MI-01, MI-03, MI-04, MI-14 |
| **INV-M3** | Rack `ManualHandling` không là nguồn tự động | **Repository query** (`Unreserved`) + `RouteConditionEvaluator.RackPresent/RackTypeMatch/RackBinding` + `PlcRequestService.cs:96` | `Repositories.cs:59-62` overload `Disposition==Auto` | Gọi trực tiếp `GetAsync(rackId)` rồi dùng (như `PreviousDestination`, `RouteEngine.cs:~250`); đổi Disposition ngoài WCS | `RACK_MANUAL_HANDLING` | Rack không xuất hiện trong ứng viên; `PreviousDestination` + Rack ManualHandling ⇒ **ReconciliationRequired** (không tự dùng) | MQ-01…MQ-05 |
| **INV-M4** | Manual QC không là Buffer; không là Source tự động | (1) **Publication** (`ProcessValidator.cs:149,156`+ rule mới); (2) Runtime: Location `ManualQcStation` bị loại khỏi `Sources` | `ProcessValidator`, `RouteConditionEvaluator.SourceUsableAsync` | Đổi `StationRole` sau Publish; PH2-05/06 hiện có; process tạo qua import | `MANUAL_QC_NOT_BUFFER`, `MANUAL_QC_NOT_SOURCE` | Publication: chặn. Runtime: loại + event | MQ-06, MQ-07, MQ-12 |
| **INV-M5** (bổ sung) | Rack đặt tại `ManualQcStation` ⇒ `Disposition=ManualHandling` (nhất quán) | Một **điểm tập trung** áp dụng khi `PlaceAt`/tạo Rack: `RouteEngine.cs:640`, `InventoryService.cs:142,244,169,201` | helper `RackPlacement` (Application) | Điều chỉnh tồn kho tạo Rack mới tại M5 mặc định Auto; đối soát đặt Rack vào M5 | `MANUAL_QC_DISPOSITION_REQUIRED` (tự áp dụng, không lỗi) | Tự đặt `ManualHandling` + audit | MQ-08, MQ-13 |
| **INV-M6** (bổ sung) | Process không có Model (legacy) không dùng Location/Rack có Model | Publication + Runtime | như M1/M2 | Process legacy khai báo P1/M5 | `LEGACY_PROCESS_MODEL_LOCATION` | Chặn Publish; runtime loại | MI-10 |

**Ghi chú về vi phạm khi vật lý đã xảy ra:** nếu `ConfirmPick/Drop` xác nhận một Rack mà Model **không khớp** (do dữ liệu sai), **không** từ chối sự kiện vật lý (Rack đã ở đó); thay vào đó: ghi inventory đúng thực tế, đặt `ReconciliationRequired` cho Location/Order và event `MODEL_MISMATCH_AT_HANDOVER`. Tương tự quy tắc "không bịa vị trí" hiện có (`RouteEngine.cs:~250`).

**Vị trí hook cụ thể:** `RouteEngine.CreateLegAsync` (sau chọn nguồn/đích), `RouteConditionEvaluator` (Source/DestinationUsable), `ConfirmDropAsync` (Disposition + kiểm tra), `InventoryService` (stock, reconcile), `ProcessValidator`, `Repositories.Unreserved`.

---

## 7. Migration Design (chỉ đề xuất – chưa tạo)

Tên đề xuất: `AddProductModelIsolation` (một migration additive; cân nhắc tách `AddRackDisposition` để rollback độc lập).

| Bước | Thao tác | Dữ liệu hiện tại |
|---|---|---|
| 1 | `CREATE TABLE product_models` (nếu chọn bảng) | trống |
| 2 | `ALTER TABLE locations ADD ProductModelId uuid NULL, ADD StationRole varchar(40) NULL` + FK, index | NULL |
| 3 | `ALTER TABLE racks ADD ProductModelId uuid NULL` + FK; `ADD Disposition varchar(40) NOT NULL DEFAULT 'Auto'` + index `(CurrentLocationId, Disposition)` | `Disposition='Auto'` cho **mọi** Rack hiện có; Model NULL |
| 4 | `ALTER TABLE business_processes ADD ProductModelId uuid NULL` + FK | NULL |
| 5 | Enum text mới `InventoryEventType` (additive) — không cần DDL | — |
| 6 | **Không** UPDATE dữ liệu; **không** tự gán Model; **không** tự đổi StationRole; **không** tự chuyển Rack ManualHandling | — |

**Down (an toàn):** theo tiền lệ `AddRcsManagedDispatch` (`ROLLBACK_REFUSED`): **từ chối** nếu tồn tại Rack `Disposition='ManualHandling'` **hoặc** Rack/Location/Process có Model (mất thông tin ⇒ Rack Manual QC trở lại tự động điều phối). Chỉ khi không còn dữ liệu mới nên mới `DROP COLUMN`. Có test Up/Down trên dữ liệu có sẵn (mẫu `Cr035ReadinessAndSimulationTests` đã dùng scratch DB).

**Ảnh hưởng:**
- *Entity configurations:* thêm cấu hình cột/FK trong `EntityConfigurations.cs` (Location/Rack) và `RoutingConfigurations.cs` (process); `Rack` constructor 5 tham số giữ nguyên, thuộc tính mới có setter nội bộ/ phương thức domain.
- *Unique indexes:* không đổi; `ux_reservations_rack_active` giữ nguyên. `ux_process_versions_one_published` không đổi.
- *Repository queries:* overload mới `FindUnreservedAsync(locationId, status, RackSelection filter)`; **overload cũ không đổi** (legacy `InventoryService.cs:38` giữ nguyên).
- *API contracts:* chỉ thêm trường **cuối, tùy chọn** (`LocationDto`, `RackDto`, `LocationConfigRequest`, `InitialStockRequest`, `StockAdjustmentRequest`); client cũ vẫn chạy.
- *ConfigTransfer (CR-026):* Location/Process group thêm trường tùy chọn; **import gói cũ không được ghi đè (NULL hóa) Model/Role đã có**; thêm group `ProductModels` mới. Test nhập/xuất cần cập nhật.
- *Tests hiện có:* dùng giá trị mặc định ⇒ không phải sửa; `Cr028…Rollback` đi qua `Down` mới ⇒ `Down` phải chạy được khi dữ liệu trống (đã quy định).
- *EF rollback:* xem Down ở trên; migration snapshot cập nhật bằng `dotnet ef` (lưu ý môi trường khóa file khi API đang chạy – dùng Release/ output riêng).

---

## 8. Legacy Compatibility

| Đối tượng | Bảo đảm |
|---|---|
| Rack cũ | `Model=NULL`, `Disposition=Auto`; hành vi legacy giữ nguyên; **không** vào luồng Model |
| Location cũ (A*, B*, D*, F1–F5, Waypoint) | `Model=NULL`, `StationRole=NULL`; F5 `ManualBuffer` giữ nghĩa cũ |
| Process/Version cũ (BP-01…06, PH2-xx hiện có) | `Model=NULL`; không đổi; INV-M6 chỉ chặn việc legacy chạm Location/Rack **có Model** |
| A3/A4/D1/D2 | PROTECTED, `ProtectedLocationCodes` không đổi; **không** gán Model/Role mặc định (P2-OQ-01) |
| Order đang chạy | Snapshot cũ deserialize (trường mới nullable); không đánh giá lại theo Model |
| Hành vi khi cờ tắt | Tính năng đứng sau **feature flag** `ModelIsolation:Enforce` (đề xuất, mặc định tắt) + chế độ `Warn` (ghi event, không chặn) để quan sát trước khi `Enforce` |
| Cấu hình thực tế | Dữ liệu Location/Model **do Admin cấu hình** qua API/UI; không có seed tự động của P1/P2/M5 |

---

## 9. API/UI impact

| Khu vực | Thay đổi dự kiến (đều additive) |
|---|---|
| `GET /api/locations`, `inventory/racks` | thêm `productModel`, `stationRole`, `disposition` |
| `POST/PUT /api/config/locations` | nhận `productModelId`, `stationRole` (tùy chọn; kiểm tra ràng buộc đổi Model khi có Rack/Version tham chiếu) |
| `POST inventory/initial-stock`, `adjustments` | nhận `productModelId` (bắt buộc hay không theo P2-OQ-04 / vị trí) |
| API mới (Admin, audit) | quản lý `product_models`; gán Model cho Rack (đơn lẻ/hàng loạt có preview); (tùy P2-OQ-07) re-intake |
| Process admin (`ProcessesPage`, `RoutingEndpoints`) | hiển thị/nhập Model của process; lỗi Publication mới |
| UI | Inventory: cột Model/Disposition + cảnh báo "Chưa có Model"; Config Locations: Model + StationRole; không còn danh sách trạm cứng (`PlcIoMappingPage.tsx:18`) khi triển khai thật (task riêng) |
| Không thay đổi | `PlcRequestService` (G11 – task riêng), PLC/RCS |

---

## 10. Acceptance Test Matrix

Ưu tiên: **P0** = an toàn bắt buộc; **P1** = quan trọng; **P2** = bổ sung. Tầng: U = unit, I = integration (DB), M = migration, A = API. Tất cả chưa chạy.

### 10.1 Model isolation

| ID | Given | When | Then | Tầng | Ưu tiên |
|---|---|---|---|---|---|
| MI-01 | Rack F-Bal tại Buffer F-Pis (dữ liệu giả) và process F-Pis cần Rack | Process chọn nguồn | Rack F-Bal **không** được chọn (`MODEL_MISMATCH`), không Reservation | I | P0 |
| MI-02 | Process F-Bal khai báo candidate Location F-Pis | Publish | Bị chặn `PROCESS_MODEL_MISMATCH` | I | P0 |
| MI-03 | Rack `Model=NULL` tại A1; process có Model | Request tại A1 | Từ chối `RACK_MODEL_UNKNOWN`; không tự gán Model | I | P0 |
| MI-04 | Rack F-Bal, Destination (Buffer) F-Pis | Tạo Leg | Không tạo Leg / candidate bị loại; event `MODEL_MISMATCH` | I | P0 |
| MI-05 | DB có Process Version legacy (Model NULL) | Chạy migration | Model vẫn NULL; Version vẫn Published; chạy như cũ | M | P0 |
| MI-06 | Thêm Model mới `T-MODEL` + Location/Rack/Process của nó bằng dữ liệu | Chạy luồng đầy đủ | Hoạt động **không đổi code thuật toán** | I | P1 |
| MI-07 | Rack đi A1→B1 | Kết thúc | `Rack.ProductModelId` không đổi; Model Source/Dest/Rack cùng | I | P1 |
| MI-08 | Rack qua Buffer P1→B1 | Retrieval | Model giữ; chỉ Rack cùng Model khả dụng | I | P1 |
| MI-09 | Rack được đối soát sang Location Model khác | Reconcile | Không đổi Model; `MODEL_LOCATION_MISMATCH` + `ReconciliationRequired` | I | P0 |
| MI-10 | Process legacy (Model NULL) khai báo P1 (có Model) | Publish | Chặn `LEGACY_PROCESS_MODEL_LOCATION` (INV-M6) | I | P1 |
| MI-11 | `initial-stock` vào Location có Model, không chỉ Model (theo P2-OQ-04 PA A) | Gọi API | Từ chối hoặc theo quyết định; không Rack NULL-Model âm thầm | A | P1 |
| MI-12 | Process Published; sau đó Location đổi Model (nếu API cho phép) | Chạy | Đổi Model bị từ chối khi đang được tham chiếu; nếu bằng DB → runtime loại candidate | I | P0 |
| MI-13 | Import gói cấu hình (CR-026) đổi Model Location | Import | Cùng ràng buộc như API; gói cũ không xóa Model | A | P1 |
| MI-14 | Hai Order đồng thời cùng Rack Model đúng | Chạy | Reservation unique; không race lẫn Model | I | P1 |
| MI-15 | Pick/Drop vật lý xảy ra với Rack Model sai (dữ liệu sai) | ConfirmDrop | Sự kiện **không bị từ chối**; inventory đúng thực tế; `ReconciliationRequired` + `MODEL_MISMATCH_AT_HANDOVER` | I | P0 |
| MI-16 | Process hợp lệ Publication nhưng Model Rack NULL ở Buffer | Retrieval | Không chọn; Order chờ; không lỗi nghiệp vụ | I | P1 |

### 10.2 Manual QC

| ID | Given | When | Then | Tầng | Ưu tiên |
|---|---|---|---|---|---|
| MQ-01 | Rack Full tại M5 (`ManualHandling`) | Process tự động tìm nguồn | Không xuất hiện trong ứng viên | I | P0 |
| MQ-02 | Rack tại M6 | như trên | Không được chọn | I | P0 |
| MQ-03 | `Disposition=ManualHandling` ở bất kỳ vị trí | Mọi process tự động | Không chọn (INV-M3 ở repository) | U/I | P0 |
| MQ-04 | Rack ManualHandling, `SourceMode.PreviousDestination` | Leg kế | `ReconciliationRequired`, không dùng Rack | I | P0 |
| MQ-05 | `PlcRequestService` Pickup tại vị trí có Rack ManualHandling | PLC request | Không tính là Rack hợp lệ (`mismatch`/reconcile) — không tự động điều phối | I | P1 |
| MQ-06 | Process khai báo M5 là `BufferStorage` | Publish | Chặn `MANUAL_QC_NOT_BUFFER` | I | P0 |
| MQ-07 | Process khai báo M5 là Source/`BufferRetrieval` | Publish | Chặn `MANUAL_QC_NOT_SOURCE` | I | P0 |
| MQ-08 | Rack mới tạo qua `adjustments` tại M5 | Tạo | Tự `ManualHandling` + audit (INV-M5) | I | P1 |
| MQ-09 | Order đưa Rack tới M5, RCS đã nhận task | Chưa xác nhận Drop | Rack **vẫn** `Auto`, `CurrentLocation` cũ/InTransit; chưa đổi Disposition | I | P0 |
| MQ-10 | Drop tại M5 được xác nhận hợp lệ | ConfirmDrop | `ManualHandling` + `DispositionChanged` cùng giao dịch; Rack ID/Model/RackType/Origin/lịch sử nguyên vẹn | I | P0 |
| MQ-11 | Rack ManualHandling | Mọi tick scheduler/Wake | Không có thao tác tự động nào đổi về `Auto` | I | P0 |
| MQ-12 | PH2-05/06 (nguồn M5) | Publish / chạy | Không tạo đường Manual→Auto; kết quả theo quyết định P2-OQ-07 (chặn/không có Rack) | I | P0 |
| MQ-13 | Reconcile đặt Rack vào M5 | Reconcile | `ManualHandling` tự áp dụng + audit | I | P1 |
| MQ-14 | F5 (`ManualBuffer` Phase 1, StationRole NULL) | BP-05/06 chạy | **Hành vi không đổi** (regression Phase 1) | I | P0 |
| MQ-15 | Nhóm không có Manual QC, Buffer đầy | Request | Order chờ + cảnh báo; không tự gán Manual QC của nhóm khác | I | P1 |
| MQ-16 | Re-intake (nếu P2-OQ-07 chọn R1/R2) | Thực hiện | Cần quyền + lý do + audit; chỉ đổi khi Model hợp lệ; ngoài đó bị từ chối | A | P0 (khi có quyết định) |

### 10.3 Reservation và Inventory

| ID | Given | When | Then | Tầng | Ưu tiên |
|---|---|---|---|---|---|
| RI-01 | Rack đang Reserved bởi Job A | Job B tìm nguồn | Không chọn (đã có `ux_reservations…`) | I | P0 |
| RI-02 | Rack `InTransit` | Tìm nguồn | Không chọn (Status lọc) | I | P0 |
| RI-03 | Leg vừa dispatch (RCS nhận) | Trước Pick | `CurrentLocation`/`Status` không đổi | I | P0 |
| RI-04 | Pick xác nhận | ConfirmPick | `InTransit`, `CurrentLocation=null`, `InventoryTransaction` | I | P0 |
| RI-05 | Drop xác nhận | ConfirmDrop | `PlaceAt` đích; Reservation Consumed | I | P0 |
| RI-06 | Chuyển vào Manual QC | Drop M5 | Rack không bị mất khỏi Inventory; CurrentLocation = M5 | I | P0 |
| RI-07 | Stock adjustment giảm tại M5 | Xóa Rack | Hành vi theo quyết định (P2-OQ-16 mới: có cho phép xóa Rack ManualHandling không; mặc định cần audit) | A | P1 |
| RI-08 | Hai Request tranh slot cuối Buffer | Đồng thời | Một bên chờ; không overbooking (đã có `ConcurrencyTests.cs:126`) | I | P1 |

### 10.4 Migration / Compatibility

| ID | Given | When | Then | Tầng | Ưu tiên |
|---|---|---|---|---|---|
| MG-01 | DB hiện có (Rack, Location, Process) | Up | Cột mới NULL / `Auto`; không đổi dữ liệu | M | P0 |
| MG-02 | DB có Rack `ManualHandling` | Down | **ROLLBACK_REFUSED**, không mất dữ liệu | M | P0 |
| MG-03 | DB không có dữ liệu mới | Down → Up | Thành công, lặp lại được | M | P1 |
| MG-04 | Gói ConfigTransfer cũ | Import | Không ghi đè Model/StationRole | A | P1 |
| MG-05 | Suite regression đầy đủ | Chạy | Không test cũ nào đổi kết quả (cờ tắt) | I | P0 |

---

## 11. OQ dependency

Không đóng OQ nào. Bảng chỉ ra **OQ chặn gì**.

| OQ | Nội dung | Chặn implementation F-Bal 10R80? | Chặn Model khác? | Ghi chú |
|---|---|---|---|---|
| **P2-OQ-03** | Model lưu bảng hay mã chuỗi | **Chặn** (schema) | — | Chưa chọn; thiết kế hỗ trợ cả hai |
| **P2-OQ-04** | Nguồn Model của Rack | **Chặn** (INV-M2 cần Model) | — | PA A/B/C; D không dùng |
| **P2-OQ-07** | PH2-05/06 có phải re-intake | **Chặn** phần Manual QC/PH2-05/06 (có thể làm trước phần Model) | — | R1/R2/R3 + X1…X4 |
| **P2-OQ-08** | `Disposition` hay `RackStatus` | **Chặn** (schema Manual QC) | — | D1/D2/D3 |
| P2-OQ-05 | P1/P2 hai Location capacity 1 hay một capacity 2 | Chặn **cấu hình dữ liệu** (không chặn code) | — | |
| P2-OQ-06 | Manual QC capacity/đầy | Chặn dữ liệu M5 | — | |
| P2-OQ-02 | Empty tràn → Manual QC? | Chặn **PH2-03** (dữ liệu process), không chặn bất biến | — | Xung đột P0 ↔ baseline |
| P2-OQ-12 | PLC Pickup location mapping | **Không** chặn bất biến Model/Manual QC (thực thi qua API/Engine); chặn **PH2-03/B1,B2 qua PLC thật** | Có (X, Y…) | Task PLC riêng |
| P2-OQ-01 | A3/A4→D1/D2 PROTECTED | Không chặn F-Bal 10R80 | **Chặn** F-Bal 10R60 | Không cấu hình mặc định |
| P2-OQ-11 | X4/X6; mapping Y/Z | Không chặn | **Chặn** F-Pis (cả hai) | |
| P0 OQ-11 | Tạo Location P1/P2/M5 (dữ liệu) | **Chặn** dữ liệu vận hành | — | Admin cấu hình |
| P0 OQ-51/53/54… | Readiness/Scope/Queue | Không chặn | — | Task sau |

**Mới phát sinh từ thiết kế này (đề xuất ghi nhận, chưa là OQ chính thức):** P2-OQ-03b (Location trung lập có Model không), P2-OQ-16 (xóa Rack `ManualHandling` bằng điều chỉnh tồn kho), P2-OQ-17 (đổi Model Location đã có Rack/Version), P2-OQ-18 (`ModelIsolation:Enforce` mức `Warn` → `Enforce` do ai quyết).

---

## 12. File/code dự kiến thay đổi (khi được phê duyệt)

| Nhóm | File | Thay đổi |
|---|---|---|
| Domain | `WCS.Domain/Locations/Location.cs`, `WCS.Domain/Inventory/Rack.cs`, `WCS.Domain/Routing/BusinessProcess.cs`, `WCS.Domain/Common/Enums.cs` (+`StationRole`, `RackDisposition`, `InventoryEventType`), mới `ProductModel.cs` | thuộc tính, phương thức `SetDisposition/AssignModel`, bất biến domain |
| Snapshot | `WCS.Domain/Routing/ProcessSnapshot.cs` | `ProductModelId?` |
| Persistence | `EntityConfigurations.cs`, `RoutingConfigurations.cs`, migration mới + snapshot | cột, FK, index, Down có chặn |
| Repository | `Repositories.cs:59-68`, `Persistence.cs:59-70` (IRackRepository) | overload lọc Model/Disposition |
| Application | `RouteEngine.cs` (`CreateLegAsync`, `ConfirmDropAsync`), `RouteConditionEvaluator.cs`, `ProcessValidator.cs`, `InventoryService.cs`, `PlcRequestService.cs` (chỉ lọc Disposition ở `:96`), mới `RackPlacement`/`ModelGuard` | INV-M1…M6 |
| Config transfer | `ConfigGroups.cs` | trường tùy chọn + group `ProductModels` |
| API | `ApiModels.cs`, `Dtos.cs`, `Endpoints.cs`, `RoutingEndpoints.cs`, `OperationalConfigService` | trường + endpoint quản trị |
| UI | `InventoryPage.tsx`, `pages/config/OperationsTab.tsx`, `ProcessesPage.tsx`, `api/types.ts`, `configTypes.ts` | hiển thị/nhập |
| Dữ liệu/Config | `reference-processes.json` (CR dữ liệu riêng, PH2-04/05/06), `appsettings.json` (`ModelIsolation`) | theo quyết định |
| Tests | file test mới (MI/MQ/RI/MG) + `Cr028…Rollback` tương thích | |
| Docs | CR mới, CHANGELOG, Impact Matrix, Traceability | theo CLAUDE §3 |

**Không đụng:** `HikRcsClient`, `RealLegDispatcher`, PLC runtime/`PlcWorker`, IO mapping, `PlcRequestService` (ngoài lọc Disposition), Queue.

---

## 13. Rollout / Rollback

| Giai đoạn | Nội dung | Điều kiện chuyển |
|---|---|---|
| **R-0** | Duyệt CR + quyết định P2-OQ-03/04/07/08 | Chủ dự án |
| **R-1** | Deploy migration additive + code (cờ `ModelIsolation:Enforce=false`); không dữ liệu Model | Regression đầy đủ PASS |
| **R-2** | Admin cấu hình Model/StationRole/Location cho F-Bal 10R80; gán Model cho Rack hiện có (PA đã chọn) | Báo cáo "Rack chưa có Model" = 0 cho Rack Phase 2 |
| **R-3** | Bật chế độ `Warn` (ghi `MODEL_*`, không chặn) | Quan sát log, không vi phạm bất ngờ |
| **R-4** | `Enforce=true` cho F-Bal 10R80 | Phê duyệt chủ dự án |
| **R-5** | Manual QC: bật `StationRole=ManualQcStation` M5 + Disposition | Quyết định P2-OQ-07/08 |
| **Rollback** | Tắt cờ về `Warn/false` ngay (tức thời, không DB); chỉ rollback migration khi không còn dữ liệu mới (Down từ chối nếu còn `ManualHandling`/Model) | — |

Không migration trên DB triển khai nếu chưa được phê duyệt; luôn có bản sao lưu trước (nguyên tắc dự án).

---

## 14. Rủi ro và khuyến nghị

| Rủi ro | Mức | Giảm thiểu |
|---|---|---|
| Triển khai khi chưa quyết P2-OQ-04 ⇒ Rack không Model hoặc gán Model sai "hợp thức hóa" lỗi | **Cao** | Không auto-gán; PA D bị loại; bắt buộc quyết định trước R-2 |
| Đổi nghĩa `ManualBuffer` làm hỏng F5/BP-05/06 | **Cao** | Dùng `StationRole`/`Disposition` mới; test MQ-14 |
| Bỏ cột Disposition khi rollback ⇒ Rack Manual QC quay lại tự động | **Cao** | `Down` từ chối; test MG-02 |
| Bộ lọc dùng chung `Unreserved` ảnh hưởng legacy | Trung | Overload; test regression đầy đủ |
| Kiểm tra chỉ ở Publication, dữ liệu đổi sau | Trung | Runtime enforcement + chặn đổi Model khi đang tham chiếu |
| Từ chối sự kiện vật lý sai Model làm lệch tồn kho | Trung | Không từ chối; ghi thực tế + reconcile (MI-15) |
| PH2-05/06 còn trong dữ liệu mặc định | Trung | Quyết định P2-OQ-07 trước khi bật INV-M4 trên dữ liệu thật |
| ConfigTransfer import gói cũ xóa Model | Trung | Trường tùy chọn + test MG-04 |
| Chưa kiểm chứng DB hiện trường (cấu hình thật có thể khác repo) | Trung | R-2 kiểm kê trước; không giả định |

**Khuyến nghị:** (1) Chốt P2-OQ-03/04/07/08; (2) làm theo checklist, bắt đầu bằng **migration + domain + test** ở chế độ cờ tắt; (3) tách riêng CR dữ liệu cho PH2-04/05/06; (4) F-Bal 10R80 trước, các Model khác sau khi OQ tương ứng được trả lời.
