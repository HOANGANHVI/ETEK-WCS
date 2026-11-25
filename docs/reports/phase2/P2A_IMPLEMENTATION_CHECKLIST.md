# P2-A – Implementation Checklist (Product Model Isolation & Manual QC Protection)

| | |
|---|---|
| **Ngày** | 2026-11-25 · branch `feature/ph2-first-runnable` |
| **Trạng thái** | **Chưa được phép thực hiện.** Checklist chỉ có hiệu lực sau khi chủ dự án phê duyệt CR (`P2A_MODEL_ISOLATION_MANUAL_QC_CR_DESIGN.md`) và các quyết định ở Bước 0 |
| **Quy tắc chung** | Mỗi bước: đọc CLAUDE.md §4–§6; không đụng PLC runtime/`HikRcsClient`/`RealLegDispatcher`/IO mapping; additive migration; cờ `ModelIsolation:Enforce` mặc định **tắt**; chạy regression đầy đủ trước khi sang bước sau; **không commit/push/deploy** nếu chưa có yêu cầu riêng |
| **Phạm vi đầu tiên** | **Chỉ F-Bal 10R80** (A1, A2, B1, B2, P1, P2, M5). F-Bal 10R60 = PROTECTED (không cấu hình); F-Pis = chưa đủ căn cứ |

Ký hiệu cột: **Vào** = điều kiện đầu vào; **Nghiệm thu** = tiêu chí hoàn thành (ID test ở §10 của tài liệu CR).

---

## Bước 0 – Quyết định và phê duyệt (không code)

| # | Việc | Vào | Nghiệm thu |
|---|---|---|---|
| 0.1 | Chủ dự án phê duyệt CR (số CR mới, đăng ký trong `docs/change-management`) | Tài liệu P2-A đã review | CR trạng thái `APPROVED`, ghi người duyệt |
| 0.2 | Quyết **P2-OQ-03** (bảng `product_models` hay mã chuỗi) | — | Ghi vào CR; không tự chọn |
| 0.3 | Quyết **P2-OQ-04** (nguồn Model của Rack: PA A/B/C) | — | Ghi vào CR; PA D bị loại |
| 0.4 | Quyết **P2-OQ-08** (`Disposition` D1 / `RackStatus` D2 / D3) | — | Ghi vào CR |
| 0.5 | Quyết **P2-OQ-07** (PH2-05/06; re-intake R1/R2/R3; xử lý dữ liệu X1…X4) | — | Ghi vào CR; nếu chưa quyết ⇒ **hoãn Bước 5**, vẫn làm được Bước 1–4 |
| 0.6 | Xác nhận dữ liệu Location P1, P2, M5 (P0 OQ-11; P2-OQ-05/06: cấu trúc P1/P2, capacity M5) | — | Bảng cấu hình do Admin cung cấp (không seed tự động) |
| 0.7 | Xác nhận phạm vi = F-Bal 10R80; ghi rõ ngoài phạm vi: 10R60 (P2-OQ-01), F-Pis (P2-OQ-11), PLC Pickup mapping (P2-OQ-12) | — | Mục "ngoài phạm vi" trong CR |
| 0.8 | Kế hoạch sao lưu DB + kế hoạch rollback được duyệt | — | Có tài liệu; **không** migration trên DB triển khai trước khi duyệt |

## Bước 1 – Schema và Domain (cờ tắt, không đổi hành vi)

| # | Việc | File/module | Vào | Nghiệm thu |
|---|---|---|---|---|
| 1.1 | Enum mới `StationRole`, `RackDisposition`; thêm `InventoryEventType` (`ModelAssigned`, `DispositionChanged`) | `WCS.Domain/Common/Enums.cs` | 0.1–0.4 | Build OK; enum text additive |
| 1.2 | Entity `ProductModel` (nếu 0.2 = bảng) | `WCS.Domain/…/ProductModel.cs` (mới) | 0.2 | Unit test domain |
| 1.3 | `Location.ProductModelId`, `Location.StationRole` | `WCS.Domain/Locations/Location.cs` | 1.1 | Giữ constructor cũ; mặc định NULL |
| 1.4 | `Rack.ProductModelId`, `Rack.Disposition` (default `Auto`) + phương thức domain `AssignModel`, `SetDisposition` | `WCS.Domain/Inventory/Rack.cs` | 1.1, 0.4 | `new Rack(5 tham số)` vẫn dùng được; `InventoryDomainTests` không đổi |
| 1.5 | `BusinessProcess.ProductModelId`; `ProcessSnapshot.ProductModelId?` | `BusinessProcess.cs`, `ProcessSnapshot.cs` | 1.2/1.3 | Snapshot cũ deserialize OK |
| 1.6 | Cấu hình EF + migration additive `AddProductModelIsolation`; **`Down` từ chối** khi còn Rack `ManualHandling`/dữ liệu Model | `EntityConfigurations.cs`, `RoutingConfigurations.cs`, `Migrations/` | 0.8 | **MG-01, MG-02, MG-03** PASS trên scratch DB; không UPDATE dữ liệu cũ |
| 1.7 | Cờ cấu hình `ModelIsolation:Enforce`/`Mode` (`Off|Warn|Enforce`, mặc định `Off`) | `RouteEngineOptions` hoặc options mới + `appsettings.example` | — | Bật/tắt qua cấu hình, không hard-code |

**Cổng kiểm tra Bước 1:** toàn bộ regression backend PASS (**MG-05**), không đổi hành vi khi `Off`.

## Bước 2 – Repository và Inventory (lọc Model/Disposition)

| # | Việc | File/module | Vào | Nghiệm thu |
|---|---|---|---|---|
| 2.1 | Overload `FindUnreservedAsync/ListUnreservedAsync` nhận bộ lọc tùy chọn (`Disposition=Auto`, `ProductModelId`); **overload cũ giữ nguyên** | `Persistence.cs:59-70`, `Repositories.cs:59-68` | 1.4 | **MQ-03**, **RI-01/02**; legacy `InventoryService.cs:38` không đổi |
| 2.2 | Điểm tập trung đặt Rack vào Location (`RackPlacement`): áp INV-M5 (`ManualQcStation` ⇒ `ManualHandling`) cho `RouteEngine.cs:640`, `InventoryService.cs:142,244`, tạo Rack `:169,:201` | Application mới + các nơi gọi | 1.3, 0.4 | **MQ-08, MQ-10, MQ-13, RI-06** |
| 2.3 | `InventoryService`: nhận `productModelId` ở initial-stock/adjustments theo quyết định 0.3 | `InventoryService.cs`, `ApiModels.cs` | 0.3 | **MI-11**; Rack mới có Model theo PA |
| 2.4 | API/Service gán Model cho Rack (đơn lẻ + hàng loạt có preview), audit `ModelAssigned` | Application + `Endpoints.cs` (Admin) | 0.3 | Audit đầy đủ; không auto-gán; **MI-03** |
| 2.5 | Đối soát (`InventoryService.cs:244`): Model/Location không khớp ⇒ `ReconciliationRequired` + event | `InventoryService.cs` | 1.4 | **MI-09** |

## Bước 3 – Validator (Publication)

| # | Việc | File/module | Vào | Nghiệm thu |
|---|---|---|---|---|
| 3.1 | INV-M1: `PROCESS_MODEL_MISMATCH` trong `UseLocation` | `ProcessValidator.cs:50-62` | 1.3, 1.5 | **MI-02** |
| 3.2 | INV-M4: `MANUAL_QC_NOT_BUFFER`, `MANUAL_QC_NOT_SOURCE` (chỉ cho `StationRole=ManualQcStation`; **không** chạm `LocationType.ManualBuffer`) | `ProcessValidator.cs:147-157` | 1.3 | **MQ-06, MQ-07, MQ-14 (F5 không đổi)** |
| 3.3 | INV-M6: process legacy không chạm Location/Rack có Model | `ProcessValidator.cs` | 1.5 | **MI-10** |
| 3.4 | Phân loại mã mới là **Publication** (D-11); đồng bộ API/UI hiển thị lỗi | `ProcessAdminService`, `RoutingEndpoints` | — | Mã lỗi hiển thị đúng; không gộp vào Readiness |
| 3.5 | Chặn đổi Model của Location đang chứa Rack/đang được Version Published tham chiếu | `OperationalConfigService`, `ConfigGroups.cs` | 1.3 | **MI-12, MI-13** |

## Bước 4 – Runtime enforcement (Route Engine)

| # | Việc | File/module | Vào | Nghiệm thu |
|---|---|---|---|---|
| 4.1 | INV-M2 trong `CreateLegAsync` (Rack/Source/Destination cùng Model; Rack NULL-Model ⇒ `RACK_MODEL_UNKNOWN`) – fail-closed, event, giữ chờ | `RouteEngine.cs:230-290` | 2.1, 0.3 | **MI-01, MI-03, MI-04, MI-16** |
| 4.2 | INV-M1 runtime (candidate khác Model bị loại + event) | `RouteConditionEvaluator.cs` | 1.3 | **MI-12** |
| 4.3 | INV-M3 trong evaluator và `PlcRequestService.cs:96` (chỉ lọc Disposition); `PreviousDestination` + Rack `ManualHandling` ⇒ `ReconciliationRequired` | `RouteConditionEvaluator.cs:40,43,66`, `RouteEngine.cs` (~`:250`), `PlcRequestService.cs:96` | 2.1 | **MQ-01…MQ-05** |
| 4.4 | `ConfirmDropAsync`: đích `ManualQcStation` ⇒ `ManualHandling` cùng giao dịch với Drop; mismatch Model khi bàn giao **không** từ chối, ghi `MODEL_MISMATCH_AT_HANDOVER` + reconcile | `RouteEngine.cs:620-660` | 2.2 | **MQ-09, MQ-10, MI-15, RI-03…RI-06** |
| 4.5 | Chế độ `Warn`/`Enforce` theo cờ 1.7 (Warn chỉ ghi event) | `RouteEngine.cs`, evaluator | 1.7 | Test cho từng mode |
| 4.6 | `MQ-11`: rà toàn bộ scheduler/wake-up không có đường `ManualHandling → Auto` | `RoutingHostedService`, `RouteEngine.RunOnceAsync` | 4.3 | **MQ-11** |

**Cổng kiểm tra Bước 4:** MI/MQ/RI P0 PASS; full regression PASS; Cr030/Cr035 (RCS/Readiness) không đổi.

## Bước 5 – Re-intake và dữ liệu PH2-05/06 (**chỉ khi P2-OQ-07 đã quyết**)

| # | Việc | File/module | Vào | Nghiệm thu |
|---|---|---|---|---|
| 5.1 | Thao tác/process re-intake theo PA R1 hoặc R2 (audit, quyền, lý do, kiểm Model/vị trí) | Application + endpoint Admin | 0.5 | **MQ-16** |
| 5.2 | CR dữ liệu riêng cho `reference-processes.json` (PH2-04 `reviewBlocked`; PH2-05/06 theo PA X1…X4) | `reference-processes.json` | CR dữ liệu riêng | **MQ-12**; không đổi dữ liệu nếu chưa có CR |
| 5.3 | Nếu R3 (chưa cho phép): ghi tài liệu "không có đường quay lại" + cảnh báo UI | UI/Docs | 0.5 | Rõ ràng cho vận hành |

## Bước 6 – API, ConfigTransfer, UI

| # | Việc | File/module | Vào | Nghiệm thu |
|---|---|---|---|---|
| 6.1 | Trường mới tùy chọn ở DTO/Request (`LocationDto`, `RackDto`, `LocationConfigRequest`, Stock*) | `Dtos.cs`, `ApiModels.cs`, `WcsQueries` | 1.x | Client cũ vẫn chạy (test hợp đồng) |
| 6.2 | `ConfigGroups.cs`: Location/Process trường tùy chọn + group `ProductModels`; import gói cũ không xóa Model | `WCS.Application/ConfigTransfer/ConfigGroups.cs` | 1.x | **MG-04, MI-13** |
| 6.3 | UI: Inventory (Model/Disposition + cảnh báo thiếu Model), Config Locations, Processes; bỏ danh sách trạm cứng ở `PlcIoMappingPage.tsx:18` (nếu được yêu cầu) | `frontend/wcs-web/src/...` | 6.1 | `tsc`, eslint, `vite build` sạch |

## Bước 7 – Dữ liệu cấu hình F-Bal 10R80 và rollout

| # | Việc | Vào | Nghiệm thu |
|---|---|---|---|
| 7.1 | Backup DB; áp migration trên môi trường thử (không phải DB triển khai) | 0.8, Bước 1 PASS | Up/Down kiểm chứng |
| 7.2 | Admin tạo `ProductModel`, gán Model/StationRole cho A1,A2,B1,B2,P1,P2,M5 | 0.6 | Báo cáo cấu hình |
| 7.3 | Gán Model cho Rack hiện có theo PA đã chọn (0.3); báo cáo "Rack chưa có Model" | 7.2 | Rack Phase 2 chưa Model = 0 |
| 7.4 | Chế độ `Warn` (quan sát log `MODEL_*`) | 7.3 | Không vi phạm bất ngờ |
| 7.5 | `Enforce=true` cho F-Bal 10R80 — **cần phê duyệt riêng** | 7.4 | Người duyệt ghi tên |
| 7.6 | Bật Manual QC protection (`StationRole=ManualQcStation` M5) sau 0.4/0.5 | Bước 4, 5 | Test MQ trên môi trường thử |

## Bước 8 – Hoàn tất (CLAUDE.md §6)

| # | Việc |
|---|---|
| 8.1 | Cập nhật CHANGELOG, CHANGE_REQUESTS, IMPACT_MATRIX, REQUIREMENT_TRACEABILITY |
| 8.2 | Báo cáo: file đổi, hành vi, test (PASS/FAIL/BLOCKED), migration, rủi ro, xác nhận **không đổi** PLC/RCS/IO mapping/A3-A4-D1-D2/F5 |
| 8.3 | Không commit/push/deploy nếu chưa có yêu cầu riêng |

---

## Tiêu chí nghiệm thu tổng

1. Không Rack sai Model được chọn làm nguồn hoặc đặt vào đích sai (MI-01, MI-04, MI-15).
2. Rack `ManualHandling`/tại M5, M6 không bao giờ là nguồn tự động; không có thao tác tự động đưa về `Auto` (MQ-01…MQ-05, MQ-11).
3. `Disposition` chỉ đổi sau xác nhận Drop hợp lệ, cùng giao dịch, giữ Rack ID/Model/loại/lịch sử (MQ-09, MQ-10).
4. Manual QC không bị coi là Buffer; F5 Phase 1 không đổi (MQ-06, MQ-07, MQ-14).
5. Dữ liệu cũ không bị tự gán Model/Role/Disposition khác mặc định; Down từ chối khi mất an toàn (MI-05, MG-01…MG-03).
6. Regression đầy đủ PASS; PLC/RCS/IO mapping, Readiness (Cr035), Real RCS (Cr030) không đổi hành vi.
7. Không OQ nào bị đóng nếu không có quyết định văn bản của chủ dự án.

## Chặn / không chặn

| Hạng mục | Chặn bởi |
|---|---|
| Bước 1 (schema/domain), 2.1, 2.2, 3, 4.1–4.3, test MI/MQ/RI | 0.1–0.4 (CR + P2-OQ-03/04/08) |
| Bước 4.4, 5, 7.6 (Manual QC đầy đủ) | P2-OQ-07 |
| Dữ liệu thật P1/P2/M5 | P0 OQ-11, P2-OQ-05/06 |
| PH2-03 Empty → Manual QC | P2-OQ-02 |
| F-Bal 10R60 | P2-OQ-01 |
| F-Pis | P2-OQ-11 (và nguồn luồng) |
| PH2-03/B1,B2 qua PLC thật | P2-OQ-12 (task PLC riêng) |
