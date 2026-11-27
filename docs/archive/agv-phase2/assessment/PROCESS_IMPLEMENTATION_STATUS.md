# AGV Phase 2 – Trạng thái triển khai từng quy trình

> Nguồn đối chiếu: `docs/01_Transport_Process_description/WCS_BP_Port_PLC_Transaction_Matrix.xlsx` (sheet `BP_Port_Matrix` 13 dòng, `Port_PLC_Mapping`, `Transaction_Model`, `Open_Points`), `WCS.Application/Routing/reference-processes.json`, `Flows/FlowOptions.cs`, `appsettings.json`.
> Nhãn: [NV] đã xác nhận · [NGUỒN] tài liệu nguồn · [CODE] hành vi code · [ĐX] đề xuất · [HỎI] cần xác nhận.
> Quan trọng: `reference-processes.json` chỉ là **dữ liệu để Admin import thành bản Draft** ("the engine never reads this file", `ReferenceProcessCatalog.cs:5-8`). Việc process có ở trạng thái **Published** trong DB triển khai là **Not Verified** (không có quyền truy cập DB thật trong audit).

## 1. Quy trình Phase 1 (BP-01…BP-06, "Process in detail")

| Process | Kịch bản [NGUỒN] (matrix) | Cấu hình mô hình CR-010 | Mô hình legacy (`ProcessFlowOptions`) | Trạng thái |
|---|---|---|---|---|
| BP-01 | A1/A2 có rack đầy → B1/B2 (không bind) → F1/F2 → F5 (R2) | `reference-processes.json` BP-01: Pickup, nguồn = Request, đích Candidates `AfterPick`; B1,B2 `noBind=true`, F1,F2, F5 | Có (`Flows/FlowOptions.cs`, `NoBindDropLocations`) | **Implemented** (dữ liệu tham chiếu + engine). Published trong DB: **Not Verified** |
| BP-02 | A1/A2 cần rack rỗng ← B1/B2, nếu không F3/F4 (R3) | BP-02: Delivery, nguồn Candidates, đích = Request | Có | **Implemented** (như trên) |
| BP-03 | B1/B2 có rack rỗng → A1/A2 → F3/F4 "theo điều kiện bind rack" (R4) | BP-03 | Có | **Implemented** cho thả không bind tại A1/A2 (`noBind=true`, khớp `process-flows.md:74`). Ma trận ghi F3/F4 "theo điều kiện bind rack": điều kiện này **không** được mô hình hóa (có loại điều kiện `RackBinding` ở `RoutingEnums.cs:52` nhưng process tham chiếu không dùng) → **Partially** cho chi tiết điều kiện F3/F4 [HỎI Q-08] |
| BP-04 | B1/B2 cần rack đầy ← A1/A2, xét F1/F2 (R5) | BP-04 `reviewBlocked=true` | Không (key `Bp04` bị từ chối khi khởi động) | **Blocked – REVIEW [NV]** (`BusinessProcess.cs:49`, `RouteEngine.cs:81-90`). Không triển khai |
| BP-05 | F5 manual: đưa rack đầy vào F5 từ F1/F2 (R6) | BP-05 `ManualRequest`, trigger `F5` | Có | **Implemented** ở mức engine; nguồn request OP/PDA, PLC của F5 "chưa xác định" → tích hợp thao tác thủ công **Not Verified/Not Implemented** |
| BP-06 | F5 manual: trả rack rỗng, đến A1/A2 → F3/F4 (R7) | BP-06 | Có | như BP-05 |
| A3/A4, D1/D2 | "Giữ nguyên process cũ" (`process-flows.md:125-131`, OP-07) | Không có process CR-010 | Luồng legacy | **UNCHANGED [NV]** – không đụng. Validator chặn process cấu hình dùng các vị trí này (`LOCATION_PROTECTED`, `ProcessValidator.cs:47,52`, danh sách `ProtectedLocationCodes`). Không rà từng dòng luồng legacy (Not Verified chi tiết) |

## 2. Quy trình Phase 2 (`PH2-01…PH2-06`, slide 4–9 "ETEK Process AGV Phase 2")

| Process | Kịch bản [NGUỒN] (matrix R8–R13) | Cấu hình tham chiếu | Điều kiện để chạy thật | Trạng thái |
|---|---|---|---|---|
| PH2-01 | A1/A2 rack đầy → ưu tiên B1/B2 → P1/P2 → M5 (P1/P2 buffer hàng; M5 QC thủ công) | Pickup / đích Candidates `AfterPick` / B1,B2 → P1,P2 → M5 | Cần Location `P1,P2,M5` (mô tả process ghi "not locations of the Phase 1 baseline: define them before validating"); validator sẽ từ chối process dùng Location chưa có | **Partially Implemented** (dữ liệu có, chạy được sau khi khai báo Location); **không có `noBind`** cho B1/B2 (khác BP-01) [HỎI Q-08] |
| PH2-02 | A1/A2 cần rack rỗng ← B1/B2, nếu không P1/P2 | Delivery / nguồn B1,B2 → P1,P2 / đích = Request | Cần P1/P2 | **Partially Implemented** |
| PH2-03 | B1/B2 có rack rỗng → A1/A2, nếu không P1/P2; "có vị trí chờ trung gian" | Pickup / đích A1,A2 → P1,P2 | Cần P1/P2 + vị trí chờ chưa khai báo ("NEEDS_CONFIRMATION") | **Partially Implemented** |
| PH2-04 | B1/B2 cần rack đầy ← A1/A2, P1/P2 | `reviewBlocked=true` | — | **Blocked – REVIEW** (cùng BP-04) |
| PH2-05 | Trả rack rỗng từ M5 → A1/A2, P1/P2; nếu chưa có đích AMR chờ ở vị trí trung gian | Pickup tại M5 / `AfterPick` / `WaitBehavior=Wait` | Cần M5, P1/P2; `HoldWithRack` giữ rack trên AMR (`RouteEngine.cs:341-348`) nhưng **không** điều AMR tới vị trí chờ | **Partially Implemented** |
| PH2-06 | Đưa rack đầy P1/P2 → M5; không có rack đầy ở P1/P2 thì báo lỗi | Delivery, nguồn P1,P2, `WaitBehavior=Fail` | Cần P1/P2, M5 | **Partially Implemented** |

## 3. Chuỗi Transport Task nhiều Leg trong dữ liệu tham chiếu

Mọi quy trình tham chiếu (BP-xx và PH2-xx) chỉ có **1 bước di chuyển + ProcessCompletion** (`reference-processes.json`, steps `s1`, `done`). Chuỗi nhiều bước (BufferStorage → Waiting → BufferRetrieval) được engine hỗ trợ (`RouteEngine.cs:156-199`, test `Cr010MultiLegTests.cs:57,81`) nhưng **chưa có quy trình tham chiếu nào dùng** → luồng "lưu buffer rồi lấy lại" là **Implemented ở engine, chưa có cấu hình nghiệp vụ**.

## 4. Mapping Port/PLC của quy trình ↔ PLC IO workbook

| Cổng | `Port_PLC_Mapping` (matrix) | Catalog trong code `plc-io-catalog.json` | Workbook 20/11 (working tree, **chưa duyệt**) | Khớp? |
|---|---|---|---|---|
| A1,A2,A3,A4 | PLC1 192.168.10.101 | PLC-01: A1–A4 | PLC block "A1" 192.168.10.101: A1–A4 | Khớp |
| B1,B2,D1,D2 | PLC2 …111 | PLC-02: B1,B2,D1,D2 | block "BCD" …111: B1,B2,D1,D2 | Khớp |
| X1,X2,X3,**X6** | PLC3 …121 | X1,X2,X3,**X6** | **X1,X2,X3,X4** (ô nhãn PLC ghi "A2" – nghi lỗi gõ) | **Chênh** (OP-04, PQ chưa trả lời) |
| E1,E2,**G3,G7** | PLC4 …131 | E1,E2,**G3,G7** | E1,E2,**G1,G2** (block "EF-G") | **Chênh** (OP-03, PQ-20) |
| F1–F5, KV1–KV4, P1–P4, M5–M6 | "chưa xác định" (OP-01, OP-02) | Không có | Không có | **Chưa có mapping – không được tự gán [NV]** |

→ Mọi điểm P1/P2/M5/F5 là **cổng chưa có PLC**: engine coi Location không có Port là "luôn sẵn sàng" (`RouteConditionEvaluator.cs:106,115`: không Port ⇒ `PortReady=true`, `PlcOnline=true`). Đây là hành vi an toàn cho việc cấu hình sớm **nhưng** có nghĩa là Phase 2 sẽ không có bất kỳ kiểm soát handshake nào tại P1/P2/M5 cho tới khi có mapping. [HỎI Q-06].

## 5. Open Points của matrix (OP-01…OP-12) ↔ tình trạng trong repo

| OP | Nội dung | Tình trạng trong code/tài liệu |
|---|---|---|
| OP-01/02 | PLC mapping F1–F5, KV1–4, P1–P4, M5–M6 | Không có mapping; không được tự tạo |
| OP-03/04 | E/G, X khác tên giữa các tài liệu | Mở (PQ-20) |
| OP-05 | Hai bộ tên (F/KV và P/M) | Giữ riêng theo source (`reference-processes.json` có cả BP và PH2) |
| OP-06 | "Một task tại một thời điểm" toàn cục (slide 4) so với quy tắc per-port mới | Đã giải bằng ADR-0008 (per-port) rồi D-1 (per-PLC) – D-1 **chưa triển khai** |
| OP-07 | A3/A4, D1/D2 giữ nguyên | Giữ UNCHANGED |
| OP-08 | BP-04 REVIEW | Bị chặn trong code |
| OP-09 | Buffer ≠ vị trí chờ | Phân biệt ở mô hình (`LocationType`), nhưng vị trí chờ không được điều khiển |
| OP-10 | Request từ PDA tại F5/M5 | Không có giao diện PDA/PLC cho F5/M5 |
| OP-11 | Task qua nhiều PLC | Engine hỗ trợ; mapping toàn bộ port chưa xác minh bằng workbook được duyệt |
| OP-12 | Không dùng fallback chung | Mỗi process có thứ tự riêng ✔ |

## 5b. Tie-break trong cùng nhóm ưu tiên

[NGUỒN] workbook R2/R8 chỉ cho "thứ tự ưu tiên trong tài liệu". [CODE] candidates cùng `Priority` thử theo `Order` (`BusinessProcess.cs:205-206`, "NEEDS_CONFIRMATION"). Ví dụ B1 trước B2 chỉ là thứ tự tài liệu; chưa có quy tắc cân bằng tải/gần nhất. [HỎI Q-04].
