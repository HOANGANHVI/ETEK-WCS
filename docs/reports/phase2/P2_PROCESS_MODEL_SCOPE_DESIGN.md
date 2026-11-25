# P2 – Process Model & Scope Design (AGV Phase 2 – ETEK WCS)

| | |
|---|---|
| **Ngày / Branch** | 2026-11-24 · `feature/ph2-first-runnable` (đang giữa merge CR-030, **chưa hoàn tất**) |
| **Loại tài liệu** | **Phân tích + thiết kế, chưa có code.** Không sửa file code, DB, cấu hình, workbook, PPTX, tài liệu P0. Không commit/push/deploy |
| **Trạng thái** | `DRAFT – chờ chủ dự án review`. Mọi mục "đề xuất" **chưa được phê duyệt**; không OQ nào được đóng |
| **Nhãn** | **[ĐÃ XÁC MINH CODE]** đọc trực tiếp code/schema (kèm file:dòng) · **[BASELINE]** baseline nghiệp vụ do chủ dự án cung cấp trong task này · **[ĐX]** đề xuất thiết kế · **[OPEN]** chờ quyết định · **[CHƯA CÓ DỮ LIỆU]** |
| **Tài liệu đi kèm** | `P2_ROUTE_TRACEABILITY_MATRIX.md` |

---

## 1. Executive Summary

1. Code hiện tại **chưa có khái niệm Product Model** ở bất kỳ tầng nào: không trên Rack, Location, Process, Station Transaction hay DB schema (grep `ProductModel`/`Model` trong `WCS.Domain` = 0 kết quả liên quan). Việc "không điều phối Rack sang sai Model" hiện **chỉ dựa vào dữ liệu**: danh sách candidate khai báo trong Process Version. Không có ràng buộc kỹ thuật nào ngăn một process khai báo P1 (F-Bal) trong tuyến F-Pis.
2. **Tìm được** (đã xác minh) nhiều phần đã đủ để dùng lại: Location có `AllowedRackKind` (Any/Full/Empty) nên một vị trí Buffer **có thể chứa cả Full lẫn Empty**; sức chứa tính theo **số vị trí vật lý** (`CountAtLocation + reservation đích Active < RackCapacity`); Reservation Rack nguồn là **duy nhất** bằng unique index; Reservation nguồn được tạo **trước dispatch**; FIFO Rack hiện chỉ trong phạm vi một Location.
3. **Rủi ro lớn nhất:** (a) Manual QC (`LocationType.ManualBuffer`) đang được **đối xử như Buffer** (validator, `WaitKind.AtBuffer`) và Rack ở đó vẫn mang `RackStatus.Full/Empty` nên **đủ điều kiện làm nguồn tự động** nếu bất kỳ process nào khai báo M5/M6 là Source; dữ liệu tham chiếu hiện có PH2-05/PH2-06 **kích hoạt từ M5** (Manual → tự động) — mâu thuẫn tiềm tàng với quy tắc "Manual QC không phải Buffer thứ hai"; (b) không có Product Model → nguy cơ sai Model; (c) luồng tự CNC (`AfterPick`) **không có đích khi dispatch** nên `RealLegDispatcher` hiện chặn chúng (xem §9, phát hiện F1).
4. **Scope** phải tách khỏi FIFO. Tài liệu đánh giá 4 phương án khóa Scope (§6) và **khuyến nghị có điều kiện** `Product Model + hướng luồng (Full-out / Empty-back)`, nhưng **chưa chọn**: 8 quyết định riêng (SD-1…SD-8) cần chủ dự án phê duyệt.
5. **Xung đột nguồn cần quyết định, không tự giải quyết:** baseline mới nói "áp dụng tương tự A3/A4→D1/D2" trong khi `CLAUDE.md` và P0 xác định A3/A4/D1/D2 là **PROTECTED – UNCHANGED**; baseline mới nói Empty Rack tràn Buffer thì ra Manual QC, trong khi P0/slide 6 (PH2-03) chỉ có `A1/A2 → P1/P2` (không có M5) — xem §12 (P2-OQ-01, P2-OQ-02).
6. Chưa kiểm chứng với thiết bị thật: toàn bộ kết luận về PLC/RCS ở đây là đọc code + mock.

---

## 2. Baseline nghiệp vụ [BASELINE]

Nguồn: mục 2 của task P2 (chủ dự án). **Không đổi tên, không ghép nhóm, không mở rộng.** Đối chiếu với P0 ở `P2_ROUTE_TRACEABILITY_MATRIX.md`.

| Model | CNC | QC tự động | Buffer | Manual QC |
|---|---|---|---|---|
| F-Bal 10R80 | A1, A2 | B1, B2 | P1, P2 | M5 |
| F-Bal 10R60 | A3, A4 | D1, D2 | Chưa xác định | Chưa xác định |
| F-Pis 10R80 | X1, X2 | Y1, Y2 | P3, P4 | M6 |
| F-Pis 10R60 | X3, X4 | Z1, Z2 | Chưa xác định | Chưa xác định |

Quy tắc: Full `CNC → QC` cùng Model; Empty `QC → CNC` cùng Model; không nhận được → Buffer của Model (còn vị trí phù hợp) → Manual QC của Model (nếu nhóm có cấu hình). Buffer: 2 vị trí vật lý/cặp, mỗi vị trí chứa Full hoặc Empty, không cố định vai trò; WCS quản lý Rack ID, Product Model, Rack Type, Origin Station, Current Location, Reservation; không lấy Rack Buffer của Model khác; FIFO chỉ áp dụng sau khi lọc. Manual QC: không phải Buffer thứ hai; Rack vào M5/M6 chuyển xử lý thủ công, rời danh sách điều phối tự động; WCS không tự lấy ra; quay lại tự động cần quy trình tiếp nhận lại **riêng được phê duyệt**.

---

## 3. Architecture hiện tại [ĐÃ XÁC MINH CODE]

| Thành phần | Hiện trạng (file:dòng) |
|---|---|
| **Process Definition / Version** | `BusinessProcess` (Code, RequestType, RackKind, `TriggerLocationCodesCsv`, `ReviewBlocked`, `Enabled`) và `BusinessProcessVersion` (State Draft→Validated→Published→Disabled, **Readiness**, một Published/process: index `ux_process_versions_one_published`, `RoutingConfigurations.cs:46`) — `WCS.Domain/Routing/BusinessProcess.cs` |
| **Cấu hình tuyến** | `RouteStep` (Pickup/Delivery/BufferStorage/BufferRetrieval/Waiting/Condition/ProcessCompletion; `SourceMode` Request/Candidates/PreviousDestination; `DestinationMode` Request/Candidates; `DestinationResolution` BeforePick/AfterPick; `WaitBehavior` Wait/Fail/Alternative) và `RouteCandidate` (Role, Priority, Order, LocationCode, `Conditions`) — `BusinessProcess.cs`, `RoutingEnums.cs:15-27` |
| **Process data mặc định** | `reference-processes.json`: BP-01…06 (Phase 1) và PH2-01…06; **PH2-04 vẫn `reviewBlocked=true` trong dữ liệu** (D-09 chưa áp dụng vào JSON; CR P1-G). Không có trường Model |
| **Route Engine** | `RouteEngine.cs`: `StartAsync:50`, `SelectProcessAsync:81`, `CreateLegAsync:230`, `ChooseDestinationAsync:293`, `ReserveDestinationAsync:302`, `DispatchLegAsync:461`, `ConfirmPickAsync:574`, `ConfirmDropAsync:620` |
| **Điều kiện** | `RouteConditionEvaluator.cs`: `SourceUsableAsync`/`DestinationUsableAsync` (location enabled, rack khả dụng/slot trống, port ready, PLC online); loại điều kiện: LocationEnabled, LocationAvailable, RackPresent, RackTypeMatch, CapacityAvailable, StationReady, PlcOnline, InventoryState, RackBinding — **không có điều kiện Model/Scope** |
| **Location** | `Location`: Code, `LocationType` (Station, Buffer, ManualBuffer, Waypoint, WaitingPosition, PickupLocation, DeliveryLocation), `RackCapacity`, `AllowedRackKind`, `Enabled`, `ReconciliationRequired` — `WCS.Domain/Locations/Location.cs`, `Enums.cs:11-22`. **Không có Model, không có vai trò CNC/QC** |
| **Rack** | `Rack`: Id, `ExternalRackId?`, `RackType?` (chuỗi tự do), `Status` (Unknown/Full/Empty/InTransit), `CurrentLocationId?`, `CurrentTaskId?`, `LastConfirmedAt` — `WCS.Domain/Inventory/Rack.cs`. **Không có Model, không có Origin, không có Disposition** (P0 D-02 Origin chưa hiện thực) |
| **Chọn Rack nguồn** | `Repositories.cs:59-62`: Rack tại **đúng Location đó**, đúng Status, chưa có Reservation Active, sắp theo `LastConfirmedAt` (FIFO trong location) |
| **Reservation** | `Reservation` Kind SourceRack/DestinationLocation, Active/Consumed/Released; unique index Rack nguồn Active (`EntityConfigurations.cs:76`); sức chứa đích = `CountAtLocationAsync` + `CountActiveDestinationAsync` (`Repositories.cs:52,93`) so với `RackCapacity` (`Location.cs:50`); tranh chấp slot cuối giải quyết bằng `Location.Touch` + row version (`RouteEngine.cs:302-308`; test `ConcurrencyTests.cs:126`) |
| **Transport Order/Job/Leg** | `TransportOrder` (ghim snapshot tại tạo Order), `TransportLeg`, `StationTransaction` (một giao dịch active/**port**: `RoutingConfigurations.cs:164-165`) |
| **PLC → WCS** | `PlcRequestService.HandleRequestAsync` (`PlcRequestService.cs:34`): Pickup xác định vị trí **chỉ qua `PlcLocationCodes`** (`appsettings.json:28`: 1→A1…4→A4; `PlcRequestService.cs:60-69`); Delivery qua PortId |
| **RCS** | `ILegDispatcher`; Real: `RealLegDispatcher` (hỏi RCS trước khi gửi) + `HikRcsClient.CreateLegTaskAsync` (`:68`); `ReqCodeOf` xác định (`:243`) |
| **Readiness/Publication** | Hai trục độc lập (migration `AddProcessVersionReadiness`, mặc định `NotReady`), cổng intake sau cờ `RouteEngine:EnforceReadiness` (`RouteEngine.cs:90,98`) |
| **API/UI Process** | `RoutingEndpoints.cs` (`/api/processes`, `/api/process-versions/{id}/readiness`…), `ProcessesPage.tsx`; UI có danh sách trạm cứng ở `PlcIoMappingPage.tsx:18` (PLC-03 = X1,X2,X3,**X6**) |

---

## 4. Gap Analysis

| # | Yêu cầu / câu hỏi | Kết quả | Chi tiết (đã xác minh) |
|---|---|---|---|
| G1 | Product Model trên Rack/Location/Process | **CHƯA CÓ** | Không có cột/entity. `Rack.RackType` là chuỗi tự do, chỉ dùng cho điều kiện `RackTypeMatch` (`RouteConditionEvaluator.cs:~45`) |
| G2 | Nhiều Model | **CHƯA HỖ TRỢ** | Không có phân tách Model; Process khai báo trigger/candidate theo mã Location. Thêm Model = tạo process mới, không có kiểm tra chéo Model |
| G3 | Nhiều CNC/QC trong một Model | **Hỗ trợ một phần** | `triggerLocationCodes` và candidate có nhiều Location (BP/PH2 dùng A1/A2, B1/B2) ✔. Nhưng nhóm chỉ là danh sách mã trong từng process, không có "CNC Group" dùng chung; mỗi process lặp lại danh sách |
| G4 | Buffer chứa cả Full và Empty | **ĐÃ HỖ TRỢ ở mức Location** | `AllowedRackKind.Any` (`Location.cs:41-47`); đếm sức chứa theo tổng Rack, không theo loại (`Repositories.cs:52`) ✔. Cần dữ liệu P1/P2 = `Buffer`, capacity 1, `Any` (chưa tồn tại trong DB — OQ-11 P0) |
| G5 | Sai Model | **NGUY CƠ CAO** | Không kiểm tra Model ở validator (`ProcessValidator.cs:145-157` chỉ kiểm `Accepts(RackKind)` và loại Location) lẫn lúc chạy. Rack ở P1 có thể bị process nào khai báo P1 lấy |
| G6 | Manual QC → tự động | **NGUY CƠ CAO** | (1) `ManualBuffer` được xử lý như Buffer: `ProcessValidator.cs:149,156`, `RouteEngine.cs:183`. (2) Rack ở M5 vẫn `Status=Full/Empty`, `CurrentLocationId=M5` ⇒ `FindUnreservedAsync` trả về nó nếu M5 là Source candidate. (3) `reference-processes.json` có **PH2-05 (M5 Empty → A/P)** và **PH2-06 (M5 Full)** kích hoạt từ M5 — hiện là yêu cầu *thủ công* (`ManualRequest`), nhưng chưa có "quy trình tiếp nhận lại được phê duyệt" nào để gắn vào |
| G7 | Trạng thái `ManualHandling` | **CHƯA CÓ** | `RackStatus` chỉ Unknown/Full/Empty/InTransit (`Enums.cs:24`) |
| G8 | Origin Station trên Rack | **CHƯA CÓ** | Không có cột Origin; FIFO Rack hiện theo `LastConfirmedAt` (P0 D-02 chưa hiện thực) |
| G9 | Scope Matching | **CHƯA CÓ** | Không có khái niệm Scope/Ordering; không Queue Request; mỗi request tạo Order ngay (`RouteEngine.cs:50-79`) |
| G10 | Hard-code từng tuyến | **Không hard-code trong C#** | grep mã trạm trong `WCS.Domain/Application/Infrastructure/Runtime/Api` (trừ migration): không có. Cấu hình: `ProtectedLocationCodes`, `PlcLocationCodes` ở `appsettings.json:27-28`. **Hard-code UI:** `PlcIoMappingPage.tsx:18` |
| G11 | Pickup request từ trạm ngoài A1–A4 qua PLC | **KHÔNG HỖ TRỢ** | `PlcRequestService.cs:66-67`: Pickup chỉ chấp nhận mã 1–4 ⇒ `UNSUPPORTED_LOCATION_CODE` cho B1/B2 (PH2-03), X, Y… Cần thiết kế nguồn vị trí theo Station (liên quan IO 20/11 – chưa duyệt) |
| G12 | Trạm F-Pis (X, Y, Z, M6, P3/P4) | **CHƯA CÓ trong code/DB/workbook** | Y1/Y2/Z1/Z2 không có trong workbook (P0 OQ-07); PLC3 trong code là X1,X2,X3,**X6**, workbook 20/11 là X1…X4 (OQ-29) |
| G13 | Scope chặn FIFO | **CHƯA CÓ** | — |
| G14 | Reservation đích trước dispatch | **MỘT PHẦN** | Chỉ khi `DestinationResolution=BeforePick` (`RouteEngine.cs:274-278`). `AfterPick` (mọi luồng CNC→… : BP-01, PH2-01, PH2-03) **không có Reservation đích cho đến sau Pick** (`:603`) |
| G15 | Overbooking Buffer | **Được bảo vệ mức ứng dụng** | Không có ràng buộc DB cho tổng; dựa `Location.Touch`/row version (đã test). Không kiểm chứng với tải thật |

Các thành phần **đã có và dùng lại được:** Location + AllowedRackKind + capacity, Reservation (Rack unique), candidate theo priority, `WaitBehavior`, Readiness/Publication tách trục, ghim snapshot, audit/event, `PlcOnline`/`StationReady` conditions.

---

## 5. Process Model đề xuất [ĐX – chưa duyệt]

Nguyên tắc: **thêm thuộc tính vào entity hiện có**, không tạo tầng mới. Station Group không là bảng: nhóm = truy vấn `Model + Role`.

| Khái niệm | Biểu diễn đề xuất | Ghi chú |
|---|---|---|
| Product Model | Bảng nhỏ `product_models(Id, Code, Name)` **hoặc** chuỗi mã trên Location/Rack/Process; khuyến nghị bảng + FK để tránh sai chính tả | Quyết định P2-OQ-03 |
| Vai trò trạm | `Location.StationRole` (enum nullable: `CncStation`, `AutoQcStation`, `BufferPosition`, `ManualQcStation`, `Other`) — bổ sung song song với `LocationType` hiện có (không đổi enum cũ) | `ManualQcStation` **tách** khỏi `BufferPosition` |
| Model của trạm | `Location.ProductModelId` (nullable; `Waypoint/WaitingPosition` có thể trung lập — OQ) | CNC/QC/Buffer/Manual của F-Bal 10R80 cùng Model |
| Model của Rack | `Rack.ProductModelId` (nullable cho dữ liệu cũ; **bắt buộc** với Rack tham gia luồng Phase 2) | Gán khi Rack được WCS ghi nhận (nhập kho/đối soát) — nguồn dữ liệu Model của Rack **chưa xác định** (P2-OQ-04) |
| Rack Type | Giữ `RackKind` (Full/Empty) làm loại vận hành; `Rack.RackType` chuỗi tự do **không** dùng làm Model | |
| Disposition | `Rack.Disposition` (`Auto` mặc định, `ManualHandling`) — **không** thêm vào `RackStatus` để không đổi ngữ nghĩa các switch hiện có | Xem §8 |
| Process → Model | `BusinessProcess.ProductModelId` (nullable với BP legacy) | Process Version kế thừa; không đổi snapshot cũ |
| Allowed Route / Scope | Rút ra từ **Process Version + Model**: Source Group, Destination Group, Buffer eligibility, Manual eligibility là các **vai trò + candidate** của step (đã có) cộng ràng buộc Model | Không bảng "route" riêng |
| Buffer eligibility | Candidate Priority 2 có `StationRole=BufferPosition` cùng Model; thứ tự P1→P2 theo priority/order hiện có | |
| Manual handling eligibility | Candidate Priority 3 có `StationRole=ManualQcStation` cùng Model; **step đích riêng, kết thúc process** (không `BufferRetrieval` từ M5) | |
| Thêm Model sau này | Thêm `product_models` + Location/Rack/Process data, **không sửa thuật toán** (lọc theo Model là điều kiện chung) | Mục tiêu thiết kế |

**Bất biến đề xuất (kiểm ở Publication và lúc chạy):**
- INV-M1: mọi Location trong một Process Version cùng `ProductModelId` với process (ngoại lệ trung lập: cần quyết định).
- INV-M2: lúc tạo Leg, `Rack.ProductModelId == Source.ProductModelId == Destination.ProductModelId`; sai → không tạo Leg, lỗi `MODEL_MISMATCH` (không chọn Rack/đích khác ngoài danh sách).
- INV-M3: Rack `Disposition=ManualHandling` không bao giờ là ứng viên nguồn tự động (bộ lọc ở truy vấn `Unreserved`, không chỉ ở process).
- INV-M4: Location `ManualQcStation` không được là candidate của step `BufferStorage/BufferRetrieval`, và không là Source của process tự động.

---

## 6. Scope Matching đề xuất [ĐX / OPEN]

**Phân biệt bắt buộc:** *Scope* = phạm vi **được phép ghép** nguồn–đích–request (tập tài nguyên/yêu cầu chịu cùng ràng buộc thứ tự). *FIFO* = thứ tự xử lý **bên trong** một Scope. FIFO **không** mở rộng Scope; hai Request cùng Scope mới chịu strict FIFO (D-13, đã quyết); khai báo Scope là dữ liệu cấu hình chưa có (P0 OQ-54).

### 6.1 So sánh khóa Scope

| Phương án | Khóa | Ưu | Nhược / rủi ro |
|---|---|---|---|
| S1 | Product Model | Đơn giản; bảo đảm không lẫn Model | Full-out và Empty-back của cùng Model chung một hàng ⇒ Request Empty bị chặn có thể **chặn cả luồng Full** (tắc chéo) dù cần tài nguyên khác |
| S2 | Product Model + hướng luồng (Full-out CNC→QC / Empty-back QC→CNC) | Hai hàng độc lập mỗi Model; khớp baseline 2.2/2.3; Request cùng hướng cùng nguồn/đích cạnh tranh trong một FIFO | Buffer dùng chung giữa hai hướng ⇒ tranh slot Buffer được phân xử bởi **Reservation nguyên tử**, không bởi FIFO ⇒ có thể bỏ đói một hướng (cần OQ) |
| S3 | Process Version | Chi tiết, dễ cấu hình | Đổi Version làm đổi Scope ⇒ phá FIFO khi publish version mới trong lúc có Request chờ (xung đột D-10); nhiều process cùng hướng bị tách hàng |
| S4 | Model + hướng + Station Group đích cụ thể | Tối đa tách biệt | Quá mịn; phải cấu hình từng cặp; dễ sót |

**Khuyến nghị có điều kiện:** S2 làm khóa Scope mặc định **nếu** SD-1…SD-8 được phê duyệt như đề xuất bên dưới; S1 chỉ khi chủ dự án muốn một hàng chung toàn Model. **Chưa chọn.** Scope key cần được **lưu lúc Queue Intake cùng ProcessVersionId** (không tính lại lúc Dispatch).

### 6.2 Tám quyết định riêng (không suy diễn từ "cùng Model")

| ID | Câu hỏi | Cái đã chắc chắn (nguồn) | Đề xuất [ĐX] | Cần quyết định |
|---|---|---|---|---|
| **SD-1** | A1 và A2 cùng Scope? | Baseline: cùng Model, cùng đích QC B1/B2 | Cùng Scope Full-out (hai CNC cạnh tranh cùng QC/Buffer ⇒ chung thứ tự đến) | **Có** – phê duyệt |
| **SD-2** | B1 và B2 cùng Scope? | Baseline: cùng Model, cùng đích CNC A1/A2 | Cùng Scope Empty-back; riêng hướng "B cần Full" (PH2-04, D-09) là Scope Delivery-Full – xem SD-3 | **Có** |
| **SD-3** | Request tại A1 dùng Rack của A2? | Code hiện tại: Source=Request ⇒ Rack của **chính trạm yêu cầu** (`RouteEngine.cs:240-243`). Với yêu cầu *Delivery* (cần Rack) nguồn là candidate (B/P hoặc A/P theo process) | Pickup request: **không** (chỉ Rack của trạm gọi). Delivery request: nguồn theo **candidate khai báo trong Process Version**, trong cùng Model | **Có** – xác nhận phân biệt Pickup/Delivery |
| **SD-4** | Request tại B1 dùng Rack của B2? | Không có trong baseline | **Không** mặc định; chỉ qua candidate khai báo tường minh | **Có** |
| **SD-5** | Rack từ P1/P2 giao đến cả B1 và B2? | Baseline: Buffer→trạm phù hợp cùng Model; D-09 PH2-04 (B cần Full: A1/A2 trước, P1/P2 sau) | Có, trạm đích = trạm **gửi request** (Delivery) hoặc theo priority (Pickup) | **Có** |
| **SD-6** | FIFO tính trên phạm vi nào? | D-13: strict FIFO theo Scope; không mặc định PLC/toàn cục (P0 §22.3) | Theo Scope key (S2). `QueueSequence` do Intake cấp; thời điểm cấp: P0 OQ-56 | **Có** (+OQ-56) |
| **SD-7** | Request đầu Scope bị Blocked: bypass? | D-13: **không bypass** trong Scope; Scope độc lập chạy song song | Không bypass; Request sau **chờ** | Đã quyết ở D-13; xác nhận áp dụng cho Held (P0 OQ-58) |
| **SD-8** | Reservation trước dispatch? | Code: Rack nguồn ✔ trước dispatch; đích **chỉ BeforePick** | Muốn cả Rack nguồn **và** vị trí đích (Buffer/Manual) được giữ **trước** khi gửi RCS; riêng luồng `AfterPick` cần quyết định: giữ nguyên "chọn sau Pick" hay đặt trước một đích dự kiến | **Có – quyết định chính** (liên quan Q1PICK/continueTask) |

**Lưu ý (khe PLC):** "một giao dịch active/PLC" (ADR-0011 D-1) là ràng buộc **khe** riêng, không là Scope (P0 §22.4, OQ-55). Code hiện **chưa** có (đang một giao dịch/port).

---

## 7. Rack Inventory State Model [ĐÃ XÁC MINH CODE + ĐX]

Ánh xạ chuỗi trạng thái baseline vào mô hình hiện tại (không đổi state hiện có):

| Bước baseline | Biểu diễn trong code hiện tại | Hàm |
|---|---|---|
| **At CNC** (Rack Full tại A1) | `Rack.Status=Full`, `CurrentLocationId=A1` | tồn kho |
| **Reserved** | `Reservation(SourceRack, Active)` (**không** là `RackStatus`; unique theo Rack) | `CreateLegAsync` `RouteEngine.cs:284` |
| **InTransit** | `Rack.BeginTransit`: `Status=InTransit`, `CurrentLocationId=null`, `CurrentTaskId=leg` | `ConfirmPickAsync` (`RouteEngine.cs:574`; `Rack.cs:36-44`), chỉ khi **xác nhận Pick** |
| **Buffer / At QC / At CNC** | `Rack.PlaceAt(location, Full/Empty)` | `ConfirmDropAsync` (`Rack.cs:46-53`) |
| **Reserved (đích)** | `Reservation(DestinationLocation, Active)` — giữ **slot** của Location | `ReserveDestinationAsync :302` |
| **ManualHandling** | **CHƯA CÓ** — đề xuất `Rack.Disposition=ManualHandling` đặt khi Drop tại `ManualQcStation` được xác nhận | §8 |

Sơ đồ đề xuất (Full): `AtCNC → [Reservation nguồn] → InTransit → AtBuffer/AtQC` ; Empty: `AtQC → [Reservation] → InTransit → AtBuffer/AtCNC` ; tràn: `AtBuffer(Full|Empty) → [Reservation] → InTransit → ManualHandling(terminal tự động)`. Quy tắc giữ nguyên: `CurrentLocation` **chỉ đổi khi có xác nhận Pick/Drop**, không vì tạo task hay RCS nhận (đã đúng trong code; `ConfirmPick/Drop` là nơi duy nhất `Rack` đổi). Rack `Unknown` không bao giờ là nguồn (`FindUnreserved` lọc theo Status).

---

## 8. Buffer và Manual QC Lifecycle

### 8.1 Buffer
- **Đánh giá đầy:** theo **số vị trí vật lý**: `occupied (mọi Rack tại Location) + reservation đích Active ≥ RackCapacity` [ĐÃ XÁC MINH `Location.cs:50`, `Repositories.cs:52,93`]. Không phân biệt Full/Empty ⇒ đúng baseline "mỗi vị trí chứa Full hoặc Empty". Dữ liệu cần: P1, P2 là **hai Location capacity 1, `AllowedRackKind=Any`** (hoặc một Location capacity 2 — P2-OQ-05; hai Location cho phép thứ tự ưu tiên P1→P2 và gắn Rack với vị trí cụ thể; khuyến nghị hai Location).
- **Overbooking:** Reservation đích được tính vào đầy nên không đặt quá sức chứa trong một giao dịch; hai giao dịch đồng thời giành slot cuối bị `Location.Touch`/row version buộc một bên đánh giá lại [ĐÃ XÁC MINH, có test]. **Rủi ro còn lại:** (a) luồng `AfterPick` chọn đích sau Pick ⇒ Rack đã trên AMR mà **không còn đích** (code xử lý bằng `DestinationUnavailable_AgvHoldingRack`, `RouteEngine.cs:603-610`) — là kịch bản vận hành xấu, nên Reservation đích sớm (SD-8); (b) Reservation không có hết hạn (P0 OQ-19).
- **Lấy Rack từ Buffer:** `BufferRetrieval`/Source candidate; chọn Rack theo FIFO **sau khi lọc** Model + RackKind + Disposition=Auto + chưa Reservation (hiện FIFO theo `LastConfirmedAt` trong từng Location; Origin chưa có).

### 8.2 Manual QC (M5/M6)
- **Không phải Buffer thứ hai:** đề xuất `StationRole=ManualQcStation` tách khỏi `BufferPosition`; validator cấm dùng làm candidate của `BufferStorage/BufferRetrieval` hoặc Source tự động (INV-M4). Hiện code coi `ManualBuffer` ≈ Buffer (G6) ⇒ cần thay đổi ở giai đoạn implementation.
- **Chọn Manual QC:** là candidate Priority 3 (cuối) của step đích; chỉ dùng khi QC và Buffer (cùng Model) không còn chỗ; "Rack tiếp theo" theo đúng thứ tự FIFO của Scope.
- **Sau khi Drop tại Manual QC được xác nhận:** `Rack.Disposition=ManualHandling`; bộ lọc `Unreserved` tự loại; inventory vẫn ghi `CurrentLocation=M5` (WCS sở hữu tồn kho). Order kết thúc `Completed` (bàn giao xong), **không** `Waiting`.
- **Không có Manual QC cấu hình (nhóm 10R60, hoặc chưa tạo):** Request ở trạng thái chờ có lý do `NO_DESTINATION_NO_MANUAL`; trong kiến trúc hiện tại là `Order Waiting + DestinationUnavailable` (`NoCandidateAsync`, `RouteEngine.cs:~277-300`); trong kiến trúc Queue là **Held/Blocked + cảnh báo operator** (cơ chế cảnh báo: **chưa xác minh trong code** – chỉ có event `ORDER_WAITING`; P0 OQ-19). Không tự gán Buffer/Manual của nhóm khác.
- **Quay lại tự động:** chỉ qua **quy trình tiếp nhận lại riêng** (Admin operation có audit đổi `Disposition` về `Auto` + đối soát vị trí/Model). **PH2-05/PH2-06 hiện có trong dữ liệu tham chiếu không được coi là quy trình đó** cho tới khi chủ dự án phê duyệt (P2-OQ-07).

---

## 9. Reservation và Dispatch (review lượt trước) [ĐÃ XÁC MINH CODE]

| Mục kiểm tra | Kết quả | Vị trí |
|---|---|---|
| Reservation Rack nguồn trước dispatch | ✔ Tạo cùng giao dịch tạo Leg | `RouteEngine.cs:284` |
| Reservation đích trước dispatch | ✔ chỉ `BeforePick`; ✘ `AfterPick` | `:274-278`, `:603` |
| Pick/Drop Location lấy từ Leg | ✔ `LegDispatchRequest(... leg.SourceLocationCode, leg.DestinationLocationCode)` | `:498,528` |
| `LegTemplate` không ghi đè nguồn/đích | ✔ chỉ `TaskTyp`, thứ tự vai trò path, tuỳ chọn pod/priority; vị trí lấy từ tham số | `HikRcsClient.cs:68-` |
| Không dispatch khi nguồn/đích chưa xác nhận | ✔ `RealLegDispatcher`: thiếu nguồn/đích ⇒ `LEG_LOCATIONS_INCOMPLETE` (Blocked); thiếu mapping ⇒ Rejected | `RealLegDispatcher.cs` |
| Không tạo task RCS trùng khi timeout | ✔ hỏi RCS trước khi gửi; `ReqCodeOf` xác định; chưa kiểm chứng RCS thật | `RealLegDispatcher.cs`, `HikRcsClient.cs:243` |
| Không tự gửi lại khi kết quả chưa rõ | ✔ cho `RealLegDispatcher` (Blocked nếu không hỏi được). **Lưu ý:** Engine vẫn gọi lại ở mỗi pass (`RouteEngine.cs:559`) — an toàn nhờ query-before-send | |

**Phát hiện (chưa sửa, theo yêu cầu):**
- **F1 – AfterPick × RealLegDispatcher (nghiêm trọng):** mọi luồng từ CNC (BP-01, PH2-01, PH2-03…) dùng `AfterPick`, nên `DestinationLocationCode == null` lúc dispatch (`RouteEngine.cs:498`). `RealLegDispatcher` chặn vĩnh viễn (`LEG_LOCATIONS_INCOMPLETE`) ⇒ **không tạo task RCS** ⇒ Pick không bao giờ xảy ra. Giải pháp phụ thuộc Q1PICK/Trigger (task tạo với nguồn, đích bổ sung bằng `continueTask` — `ConfirmPickAsync` đã gọi `ContinueAsync` `:615`). *Đề xuất thay đổi riêng:* khi có Q1PICK XML, cho phép template "chỉ nguồn" cho leg `AfterPick`. Không sửa trong task này.
- **F2:** `PlcRequestService.cs:66-67` giới hạn Pickup ở mã 1–4 (G11).
- **F3:** `reference-processes.json` PH2-04 còn `reviewBlocked=true` (đã nêu P1-G).
- **F4:** `ProcessValidator.cs:149,156` coi `ManualBuffer` như Buffer (G6).

---

## 10. Readiness / Publication [ĐÃ XÁC MINH CODE]

- Publication (`Draft/Validated/Published/Disabled`) và Readiness (`NotReady/Ready/Suspended`) là **hai cột độc lập**; Publish **không** đặt Ready (mặc định `NotReady`, migration dữ liệu cũ cũng `NotReady`); chỉ NotReady→Ready, Ready→Suspended, Suspended→Ready được phép, còn lại `READINESS_TRANSITION_NOT_DECIDED` (OQ-51); lý do bắt buộc + audit; `EnforceReadiness=true` ⇒ chỉ Published+Ready nhận request. Test đã có (Cr035).
- **Thiết kế liên quan Model:** Readiness theo **Process Version**; điều kiện Readiness (PLC/Station/Location) có thể phụ thuộc nhiều Model ⇒ độ mịn: P0 OQ-53 vẫn mở. Thiết kế này không thay đổi.
- Validator Publication bổ sung (ở implementation): INV-M1, INV-M4, Station tồn tại đúng `StationRole` (lỗi cấu trúc ⇒ chặn Publish), **không** đưa PLC/RCS/commissioning vào Publication (D-11).

---

## 11. Queue Integration Design (chưa triển khai) [ĐX]

`Request Intake → Process Selection → Version Pinning → Queue/Held → Scope Matching → FIFO → Dispatch`

| Bước | Dữ liệu thiết kế cần có (từ P2) |
|---|---|
| Intake Record | `RequestLocation` → `Location.ProductModelId`, `StationRole`; `RackKind` (từ yêu cầu) |
| Process Selection | Lọc process theo trigger + Model + RackKind; mơ hồ ⇒ `NeedsDecision` (P0) |
| Version Pinning (D-10) | `ProcessVersionId` + hash **và Scope key** ghi tại Intake; **không** chọn Latest lúc Dispatch |
| Published + Ready | kiểm tra trong cùng giao dịch ghim (P0 D-12) |
| Queue/Held | `QueueSequence`, `ScopeKey`; Held khi Readiness Suspended (P0) |
| Scope Matching | tập Request cùng `ScopeKey`; đầu Scope chặn các Request sau (strict FIFO); Scope khác song song |
| FIFO Rack | sau lọc Model + RackKind + Disposition=Auto + chưa Reservation; Origin (P0 D-02) |
| Dispatch | tạo Order từ Version đã ghim; Reservation nguồn (và đích nếu SD-8) trước khi gửi RCS; khe PLC (D-1) |

Không thiết kế lại PLC runtime/HikRcsClient; Queue chỉ chạm tầng Application (Process/Route Engine) và bảng mới.

---

## 12. Open Questions cần quyết định (tiền tố P2-OQ để không trùng P0)

| ID | Nội dung | Ảnh hưởng | Mức |
|---|---|---|---|
| P2-OQ-01 | **A3/A4→D1/D2 (F-Bal 10R60):** baseline nói "áp dụng tương tự" nhưng `CLAUDE.md`/P0: A3/A4/D1/D2 **PROTECTED – UNCHANGED**. Thiết kế có phủ nhóm này hay chỉ ghi nhận Model? | Phạm vi, validator `ProtectedLocationCodes` | **Cao** |
| P2-OQ-02 | **Empty tràn → Manual QC**: baseline 2.3 có Manual QC làm lựa chọn cuối cho Empty, còn P0/slide 6 (PH2-03) chỉ `A1/A2 → P1/P2` (OQ-24: slide ghi P1). Quy tắc nào là chuẩn? | PH2-03, Scope | **Cao** |
| P2-OQ-03 | Product Model là bảng riêng hay mã chuỗi? Location trung lập (Waypoint) có Model không? | Schema | Trung |
| P2-OQ-04 | **Nguồn dữ liệu Model của Rack** (ai/khi nào gán; Rack legacy chưa có Model xử lý ra sao; có cần Rack ID/`ExternalRackId`?) | INV-M2, an toàn Model | **Cao** |
| P2-OQ-05 | P1/P2: hai Location capacity 1 hay một Location capacity 2? Có thứ tự ưu tiên P1→P2? | Cấu hình, FIFO Rack | Trung |
| P2-OQ-06 | Manual QC capacity/ý nghĩa đầy (M5/M6 hết chỗ thì sao)? | Held/cảnh báo | Trung |
| P2-OQ-07 | PH2-05/PH2-06 (từ M5) có thuộc "quy trình tiếp nhận lại" không? Hay phải bỏ/đổi thành quy trình tiếp nhận riêng? | G6, Manual→Auto | **Cao** |
| P2-OQ-08 | `Rack.Disposition` (cờ riêng) hay trạng thái mới `ManualHandling` trong `RackStatus`? | Schema, UI | Trung |
| P2-OQ-09 | SD-1…SD-8 (Scope) phê duyệt như đề xuất ở §6.2? Khóa Scope S1/S2/S3/S4? | Queue | **Cao** |
| P2-OQ-10 | SD-8: Reservation đích sớm cho luồng `AfterPick`? (liên quan Q1PICK/continueTask, F1) | RCS, Queue | **Cao** |
| P2-OQ-11 | Tên trạm: PLC3 X4 hay X6; Y1/Y2/Z1/Z2 ↔ PLC nào (P0 OQ-07/29) | Cấu hình F-Pis | **Cao** |
| P2-OQ-12 | Request Pickup từ trạm ngoài A1–A4 qua PLC (G11): nguồn xác định vị trí là gì khi giao diện 20/11 không có `Rack_Full/Rack_Empty`? | PLC adapter (không đổi trong task này) | **Cao** |
| P2-OQ-13 | Tiêu chí "Buffer không còn vị trí phù hợp": phụ thuộc loại Rack không (vd. vị trí P1 đang giữ Empty có chấp nhận Full không)? Theo baseline mọi vị trí chứa cả hai ⇒ mặc định "có"; xác nhận | Candidate Buffer | Trung |
| P2-OQ-14 | Cơ chế cảnh báo operator khi Held/không có Manual QC (P0 OQ-19) | UX | Trung |
| P2-OQ-15 | Origin Station trên Rack (P0 D-02): mức ưu tiên hiện thực cùng Model | FIFO Rack | Trung |

P0 OQ liên quan còn mở và **không bị đóng** ở đây: OQ-01 (chưa được xác nhận), 03, 05, 07, 11, 14, 16, 19, 24, 28, 29, 45, 46, 49–58.

---

## 13. File/code cần thay đổi ở giai đoạn implementation (ước lượng)

| Nhóm | File/Module | Nội dung |
|---|---|---|
| Domain | `WCS.Domain/Locations/Location.cs`, `WCS.Domain/Inventory/Rack.cs`, `WCS.Domain/Routing/BusinessProcess.cs`, `Enums.cs` (+ mới `ProductModel`) | `ProductModelId`, `StationRole`, `Disposition` |
| Persistence | `EntityConfigurations.cs`, `RoutingConfigurations.cs`, migration mới (cộng thêm, dữ liệu cũ NULL) | Cột + FK + index |
| Validation | `ProcessValidator.cs:145-157` | INV-M1/M4, vai trò trạm; mã lỗi mới (Publication) |
| Engine | `RouteEngine.cs` `CreateLegAsync` (:230-290) | INV-M2 `MODEL_MISMATCH`, bộ lọc Disposition |
| Repository | `Repositories.cs:59-62` | `Unreserved` lọc `Disposition=Auto` + Model |
| Evaluator | `RouteConditionEvaluator.cs` | điều kiện Model/Role |
| Queue (task sau) | `IRoutingRepository`, `RouteEngine.StartAsync`, bảng Queue/Intake mới | theo §11 |
| RCS | `RealLegDispatcher.cs`, `HikRcsClient.cs` | F1 sau khi có Q1PICK (task riêng) |
| PLC | `PlcRequestService.cs:60-69` | G11 sau khi giao diện được duyệt (task riêng) |
| Data/Config | `reference-processes.json`, `appsettings` | PH2-04 (P1-G), dữ liệu Model/Location |
| UI | `ProcessesPage.tsx`, `PlcIoMappingPage.tsx:18` | hiển thị Model/Role; bỏ danh sách trạm cứng |

Migration: chỉ cộng cột nullable; **không** đổi/xóa dữ liệu; rollback = bỏ cột; thực thi sau khi CR được duyệt (CLAUDE.md §3).

---

## 14. Test cases dự kiến (chưa viết)

| Nhóm | Case |
|---|---|
| Model | MM-01 Rack F-Bal không lấy được từ Buffer F-Pis; MM-02 Process khai báo Location khác Model bị chặn Publication; MM-03 Rack không có Model bị từ chối (OQ-04); MM-04 thêm Model mới chỉ bằng dữ liệu |
| Station group | SG-01 A1 và A2 cùng Scope (SD-1); SG-02 Pickup tại A1 không dùng Rack của A2 (SD-3); SG-03 B1 không dùng Rack B2 (SD-4); SG-04 P1/P2 giao được cho B1 và B2 (SD-5) |
| Buffer | BF-01 Buffer chứa Full và Empty cùng lúc; BF-02 đầy theo vị trí vật lý (không theo loại); BF-03 hai giao dịch giành slot cuối → một bên chờ (đã có tương tự `ConcurrencyTests.cs:126`); BF-04 không overbooking với Reservation |
| Manual QC | MQ-01 Buffer đầy → M5; MQ-02 Rack tại M5 `Disposition=ManualHandling` không là nguồn; MQ-03 process không thể khai báo M5 là Source/BufferRetrieval; MQ-04 nhóm không có Manual QC → chờ + cảnh báo; MQ-05 PH2-05/06 theo quyết định P2-OQ-07 |
| Scope/FIFO | SF-01 cùng Scope không bypass; SF-02 Scope khác song song; SF-03 FIFO không mở rộng Scope (Request đủ điều kiện ở Scope khác không bị chặn); SF-04 Full không bị Empty chặn (nếu S2) |
| Reservation/RCS | RS-01 Reservation nguồn trước dispatch; RS-02 đích trước dispatch theo SD-8; RS-03 AfterPick + template (F1); RS-04 timeout không tạo task trùng (đã có mock); RS-05 LegTemplate không ghi đè |
| Readiness | RD-01 Published không tự Ready (đã có); RD-02 Suspended không nhận Request (đã có) |
| Legacy | LG-01 A3/A4/D1/D2 không đổi (P2-OQ-01) |

---

## 15. Thứ tự triển khai đề xuất

1. **Chốt quyết định:** P2-OQ-01, 02, 04, 07, 09, 10, 11, 12 (chặn thiết kế còn lại).
2. **CR thay đổi dữ liệu** (CLAUDE §3): Product Model + `StationRole` + `Rack.ProductModelId` + `Rack.Disposition` (cộng thêm, nullable) + validator INV-M1/M4 + dữ liệu F-Bal 10R80 (A1,A2,B1,B2,P1,P2,M5) — **chưa** F-Pis đến khi OQ-11 có đáp án.
3. **Ràng buộc chạy:** INV-M2 và lọc `Disposition` trong chọn Rack; test MM/BF/MQ.
4. **Q1PICK + Trigger + F1** (cần XML) — task RCS riêng.
5. **Queue** (Intake, Pin, Held, Scope key, FIFO) theo §11.
6. **PLC intake cho trạm mới** (G11) sau khi giao diện 20/11 được duyệt.
7. F-Pis và F-Bal 10R60 khi có quyết định OQ-01/11.

---

## 16. Giới hạn của tài liệu

- Đã đọc code/schema; **chưa truy vấn DB thật** (cấu hình Location/Process đang chạy tại hiện trường có thể khác dữ liệu tham chiếu trong repo).
- Mọi kết luận về PLC/RCS là từ code + mock; **chưa kiểm chứng thiết bị thật**.
- Các bảng P1/P2/M5/Y/Z/X4 **chưa tồn tại** trong code/DB/workbook; không giả định chúng đã được cấu hình.
