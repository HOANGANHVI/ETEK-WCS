# AGV Phase 2 – Phase 0: Đánh giá kiến trúc hiện có

> Phạm vi: chỉ đọc code và tài liệu. Không sửa code, không migration, không đổi cấu hình. Ngày: 2026-11-24. Nhánh `main`, HEAD `34c45b2`.
> Đường dẫn code tính từ `src/backend/`; số dòng là của file tại thời điểm audit (working tree).

## 0. Quy ước phân loại (dùng trong cả 9 tài liệu)

| Nhãn | Ý nghĩa |
|---|---|
| **[NV]** | Nghiệp vụ đã xác nhận (CLAUDE.md §2, `docs/specification/*`, ADR đã duyệt, yêu cầu chủ dự án đã ghi) |
| **[NGUỒN]** | Nội dung từ tài liệu nguồn (workbook, slide) chưa phải quy tắc đã duyệt |
| **[CODE]** | Hành vi code hiện tại (đã đọc thực tế) |
| **[ĐX]** | Đề xuất kỹ thuật (chưa được duyệt) |
| **[HỎI]** | Câu hỏi cần người dùng xác nhận |
| **Implemented / Partially / Not Implemented / Not Verified** | Trạng thái triển khai (xem từng tài liệu) |

"Not Verified" = không thể xác minh bằng việc đọc code/tài liệu trong repo (ví dụ hành vi PLC thật, RCS thật, DB triển khai).

## 1. "Phase 2" trong repo nghĩa là gì – điều cần nói trước

- **[NV]** `docs/specification/process-flows.md:133`: "Không suy ra quy trình Phase 2 nếu chưa có tài liệu." Không có tài liệu đặc tả Phase 2 đã duyệt trong `docs/specification/`.
- **[NGUỒN]** Nguồn Phase 2 duy nhất trong repo: sheet `BP_Port_Matrix` dòng 8–13 của `docs/01_Transport_Process_description/WCS_BP_Port_PLC_Transaction_Matrix.xlsx` (nguồn ghi "ETEK Process AGV Phase 2", slide 4–9) và sheet `Open_Points` (OP-01…OP-12).
- **[CODE]** 6 quy trình tham chiếu `PH2-01…PH2-06` đã có trong `WCS.Application/Routing/reference-processes.json` (mỗi quy trình chỉ là dữ liệu Draft để Admin import; `ProcessAdminService.ImportReferenceAsync` – xem `WCS.Api/Api/RoutingEndpoints.cs`, endpoint `processes/import-reference`). `PH2-04` bị `reviewBlocked=true` (cùng kịch bản BP-04 đang REVIEW).
- **[HỎI]** Không có tài liệu yêu cầu Phase 2 tổng thể (vd. danh sách tính năng "Transport Job nhiều Pick/Drop", "Origin-based Rack selection"). Bản audit này lấy các chủ đề trong yêu cầu của người dùng làm checklist và đối chiếu với code + 2 workbook. **Cần người dùng cung cấp/chỉ định tài liệu yêu cầu Phase 2 chính thức** (xem OPEN_QUESTIONS Q-01).

## 2. Cấu trúc giải pháp

| Lớp | Dự án | Vai trò (đã đọc) |
|---|---|---|
| Domain | `WCS.Domain` (~2.5k dòng) | Entity/state machine: `Tasks/TransportTask.cs` (AGV task "legacy", state machine được bảo vệ), `Routing/*` (CR-010: Order/Leg/StationTransaction/BusinessProcess), `Plc/PlcEntities.cs`, `Inventory/*`, `Locations/Location.cs` |
| Application | `WCS.Application` (~11k dòng) | Dịch vụ: `Services/TaskOrchestrator.cs` (legacy), `Routing/RouteEngine.cs` (CR-010 engine), `Routing/StationTransactionService.cs`, `Routing/PlcRequestService.cs`, `Plc/*` (runtime, worker, handshake) |
| Infrastructure | `WCS.Infrastructure` (~5k dòng) | EF Core/PostgreSQL, MC Protocol (`Plc/Mc/*`), mô phỏng (`Simulation/*`), RCS fleet |
| Runtime | `WCS.Runtime` | Hosted services: `RoutingHostedService.cs` (vòng lặp engine), `HostedServices.cs` |
| Api | `WCS.Api` | Minimal API + SignalR |

**Hai mô hình điều phối song song (quan trọng):**

1. **Mô hình legacy** (Phase 1): `TransportTask` (`WCS.Domain/Tasks/TransportTask.cs`) + `TaskOrchestrator` (`WCS.Application/Services/TaskOrchestrator.cs`, 992 dòng) + `StationRequestProcessor` + `PlcStationSignalService`. Hàng đợi FIFO theo AGV (`TaskOrchestrator.cs:204` `NextQueuedForAgvAsync`, ghi chú "FIFO: NEEDS_CONFIRMATION"). Luồng A1/A2…, A3/A4, D1/D2 "UNCHANGED" **[NV]**.
2. **Mô hình CR-010 Business Process** (cấu hình được): `TransportOrder` → `TransportLeg` → `StationTransaction` (`WCS.Domain/Routing/*`), chạy bởi `RouteEngine`. Đây là nền tự nhiên cho Phase 2.

`TransportOrder` được đặt tên như vậy vì `TransportTask` đã tồn tại với nghĩa khác (`WCS.Domain/Routing/TransportOrder.cs:5-9`). Trong tài liệu nghiệp vụ "Transport Task" = `TransportOrder` **[CODE]**.

## 3. Luồng điều khiển hiện có (đã theo dõi trong code)

```
HTTP POST /orders  (RoutingEndpoints.cs:57)  ─┐
PLC request (HTTP only) POST /plc-requests    ├─> RouteEngine.StartAsync (RouteEngine.cs:50)
  (RoutingEndpoints.cs:99-103 -> PlcRequestService.HandleRequestAsync :34)   │      idempotent theo RequestKey
                                                                              v
  RoutingHostedService (tick) -> RouteEngine.RunOnceAsync (:352) -> AdvanceAsync -> AdvanceCoreAsync (:137)
     Step Pickup/Delivery/BufferStorage/BufferRetrieval -> CreateLegAsync (:222)  [chọn nguồn/đích, Reservation]
     Leg Ready -> DispatchLegAsync (:453) -> ILegDispatcher.DispatchAsync (RCS, hoặc SimulatedLegDispatcher)
     Xác nhận pick/drop -> ConfirmPickAsync (:566) / ConfirmDropAsync (:612) -> cập nhật Rack/Inventory -> bước kế
```

Điểm vào PLC → WCS **chỉ là HTTP** (`POST /api/.../plc-requests`, quyền Admin) – không có vòng lặp nào đọc thanh ghi PLC rồi gọi `HandleRequestAsync` (grep: chỉ `RoutingEndpoints.cs:101` và chính service gọi). Chú thích trong code: "what the PLC runtime will call once the PLC engineer approves the mapping" (`RoutingEndpoints.cs:99`). → **Not Implemented** cho tích hợp tự động PLC → Order.

## 4. Các ràng buộc/bảo vệ đã có trong DB (đã đọc `Infrastructure/Persistence/Configurations/RoutingConfigurations.cs`)

| Ràng buộc | Dòng | Ý nghĩa |
|---|---|---|
| `ux_station_transactions_one_active_per_port` (unique `PortId` khi State ∉ Completed/Failed) | 164-165 | **Một giao dịch hoạt động mỗi CỔNG (Station)**, không phải mỗi PLC |
| `ux_station_transactions_request_id` (unique `PlcId`+`RequestId`) | 167 | Chống trùng Request_ID theo PLC |
| `ux_transport_legs_one_active_per_agv` (unique `AgvId` khi InProgress/Waiting) | 136-137 | PER_AGV ở mức DB (chỉ cho chế độ WCS chọn AGV) |
| `ux_process_versions_one_published` | 46 | Mỗi process 1 version Published |
| `RequestKey` unique (Order) | 107 | Idempotency tạo Order |
| `Location.Version` (xmin) + `Touch()` | `Location.cs:63` | Xung đột khi hai giao dịch cùng chiếm chỗ cuối cùng |

## 5. Hai chế độ dispatch (CR-028/CR-030)

- **[CODE]** `RouteEngine.DispatchLegAsync` có hai nhánh: `IsRcsManaged` (`RouteEngine.cs:473-491`: RCS chọn AMR, không PER_AGV) và nhánh PER_AGV (`:493-520`: WCS chọn AGV theo role, `agvs.GetByRoleAsync`).
- **[CODE]** Trên `main`, `ILegDispatcher` chỉ có **`SimulatedLegDispatcher`** (`Infrastructure/DependencyInjection.cs:90`, `Simulation/RoutingAdapters.cs:12`). Bộ gửi thật nằm ở nhánh `feature/cr-030-real-rcs-dispatch` (chưa merge). → Phase 2 chạy thật với RCS = **Not Implemented trên main**.

## 6. Điểm mạnh làm nền cho Phase 2

1. Mô hình Order/Leg/Step cấu hình được; Candidate có priority + order + điều kiện kiểm soát (`BusinessProcess.cs:141-215`, `RoutingEnums.cs:36-54`).
2. Reservation nguồn (khóa rack) và đích (giữ chỗ) tách riêng (`Inventory/Reservation.cs:154-164`).
3. Inventory do WCS sở hữu, có đối soát và trạng thái `ReconciliationRequired` ở Order/Leg/Location/Transaction.
4. Cô lập adapter PLC/RCS khỏi logic nghiệp vụ (interface `IPlcRuntime`, `ILegDispatcher`, `IRcsClient`).
5. Idempotency nhiều lớp (RequestKey, Request_ID, AgvRequestCode, AgvTaskCode).
6. Khởi động lại an toàn: `ResumeOrphansAsync` (`PlcRequestService.cs:159`), `Restart_in_the_middle_of_a_multi_leg_process…` (test `Cr010MultiLegTests.cs:318`).

## 7. Khoảng trống kiến trúc chính so với yêu cầu Phase 2 (chi tiết ở các tài liệu sau)

| # | Khoảng trống | Tài liệu |
|---|---|---|
| G1 | Không có tích hợp PLC → Order tự động; code theo giao diện cũ 10/11 (9 bước, M9000…), không có tín hiệu 20/11 (`PLC_MissionRequest`…) | PLC_HANDSHAKE_ASSESSMENT |
| G2 | Không có FIFO cấp PLC; ràng buộc là mỗi cổng, test hiện tại khẳng định ngược lại D-1 | PLC_HANDSHAKE_ASSESSMENT |
| G3 | Không có "Transport Job gồm nhiều Pick/Drop trong một lượt AMR"; mỗi Leg = một nhiệm vụ RCS độc lập | MISSION_ORCHESTRATION_ASSESSMENT |
| G4 | Chưa có Location P1/P2/M5 trong baseline; chưa có "vị trí chờ trung gian" khai báo | RACK_INVENTORY_ASSESSMENT |
| G5 | Xác nhận pick/drop chỉ do toán tử gọi API; chưa có nguồn xác nhận từ PLC (`PLC_MissionCompleted`) hay RCS | MISSION_ORCHESTRATION_ASSESSMENT |
| G6 | Workbook 20/11 trong working tree là **phiên bản mới hơn** bản CR-029 đã phân tích (xem §8) | PLC_HANDSHAKE_ASSESSMENT |

## 8. Phát hiện về kiểm soát tài liệu (cần chú ý)

- **[CODE/Git]** File `docs/02_Interface_Specification/PLC_IO_Map/AGV_PLC Interface_List_20_11_2026.xlsx` ở working tree **khác** bản đã commit (`M` trong `git status`): sha256 working tree bắt đầu `a291d28f…`, bản HEAD `3c5da74b…` (bản CR-029 ghi nhận). Bản mới điền địa chỉ thanh ghi (D1500…) và đổi giá trị tín hiệu. Tài liệu `CLAUDE.md §7.8` và CR-029 vẫn mô tả "cột địa chỉ trống". Đây là **thay đổi tài liệu được kiểm soát chưa qua Change Request**. Audit này **không** dùng nó làm mapping; chỉ ghi nhận (PLC_HANDSHAKE_ASSESSMENT §2).
- **[Git]** File IO Map v2 (`ETEK_WCS_IO_Map_4PLC_16Stations_v2_Separate_IO_Sets.xlsx`) hiển thị `D` (đã xóa) ở working tree – thuộc nhóm file được bảo vệ, không đụng tới.
