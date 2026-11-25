# AGV Phase 2 – Đánh giá điều phối Mission / Transport Job

> Nhãn: [NV] · [NGUỒN] · [CODE] · [ĐX] · [HỎI]. Trạng thái: Implemented / Partially / Not Implemented / Not Verified.
> Đường dẫn từ `src/backend/`. Thuật ngữ trong code: **Transport Task nghiệp vụ = `TransportOrder`**; `TransportTask` (legacy, state machine được bảo vệ) là thứ khác (`Domain/Routing/TransportOrder.cs:5-9`).

## 1. Vòng đời Transport Job (`TransportOrder`)

### 1.1 Trạng thái và chuyển trạng thái [CODE]

`OrderState`: Created, Accepted, InProgress, Waiting, Completed, Failed, Cancelled, ReconciliationRequired (`Domain/Routing/RoutingEnums.cs:56`). Các cạnh hợp lệ ở `TransportOrder.cs:26-34`; chuyển ngoài cạnh ném `INVALID_ORDER_TRANSITION` (`:71`). Trạng thái cuối: Completed/Failed/Cancelled (`IsTerminal :65`).

`LegState`: Pending, Ready, InProgress, Waiting, Completed, Failed, Cancelled (`RoutingEnums.cs:58`); cạnh ở `TransportOrder.cs:97-104`; một Leg đã pick **không** quay về Ready (`:144-145`).

`WaitKind` giải thích vì sao chờ (rack ở buffer, AMR đang giữ rack, nguồn thiếu, AGV bận, điều kiện chưa đạt, dispatch bị chặn…) (`RoutingEnums.cs:61-78`).

### 1.2 Chuỗi xử lý [CODE]

| Bước | Hàm | Dòng | Ghi chú |
|---|---|---|---|
| Khởi tạo, idempotent theo `RequestKey` | `RouteEngine.StartAsync` | 50-79 | Chọn process Published (`SelectProcessAsync :81-104`); snapshot version vào Order (`ProcessSnapshot`) → thay đổi process sau đó không ảnh hưởng Order đang chạy |
| Vòng trạng thái | `AdvanceCoreAsync` | 137-203 | `MaxStepsPerAdvance = 50` (`:40`), loại bước: ProcessCompletion (`:151`), Condition (`:156`), Waiting (`:165`), di chuyển (`:182`) |
| Tạo Leg, chọn nguồn/đích, giữ chỗ | `CreateLegAsync` | 222-283 | |
| Gửi RCS | `DispatchLegAsync` | 453-556 | Hai nhánh: RcsManaged (`:473-491`), PER_AGV (`:493-520`) |
| Đồng bộ AMR do RCS chọn | `SyncLegRobotsAsync` | 365-445 | Ghi `RobotCode` lên Leg |
| Xác nhận pick | `ConfirmPickAsync` | 566-610 | Rack → `InTransit` (`Rack.BeginTransit`), tiêu thụ reservation nguồn |
| Xác nhận drop | `ConfirmDropAsync` | 612-650 | `Rack.PlaceAt`, Leg Completed, `AdvanceCoreAsync` ngay trong cùng unit of work (bước kế) |
| Hủy | `CancelAsync` | 654-677 | Từ chối nếu Leg đang chạy hoặc rack trên AMR (`ORDER_NOT_CANCELLABLE`) |

**Trạng thái:** **Implemented** cho vòng đời Order/Leg theo mô hình CR-010. Kiểm bằng test: `Cr010MultiLegTests.cs:33-108,228,318,340`.

### 1.3 Nguồn xác nhận Pick/Drop – khoảng trống [CODE]

- `ConfirmPickAsync`/`ConfirmDropAsync` chỉ được gọi bởi API Admin với `ConfirmationSource.Operator` (`WCS.Api/Api/RoutingEndpoints.cs:72,75`). Không có tác nhân tự động (PLC `PLC_MissionCompleted`, RCS callback tiến độ, quét) nào gọi chúng (grep toàn `src/`).
- RCS báo `Completed` mà chưa có drop xác nhận → engine chỉ ghi sự kiện `LEG_RCS_COMPLETED_WITHOUT_DROP`, **không** tiến (`RouteEngine.cs:398-402`) – đúng [NV] "không đánh dấu rack đến đích chỉ vì task đã dispatch/hoàn thành".
- Hệ quả: với main hiện tại, một Leg dispatch qua RCS (khi có bộ gửi thật) **không thể tự hoàn tất** mà không có người nhấn xác nhận. **Not Implemented**: tự động hóa xác nhận (phụ thuộc D-4 + PLC). [HỎI Q-14]

## 2. Nhiều Pick/Drop

| Câu hỏi | Kết luận [CODE] | Trạng thái |
|---|---|---|
| Một Order có thể có nhiều Leg? | Có, tuần tự theo `NextStepKey`; mỗi bước di chuyển → một Leg (`RouteEngine.cs:182-199`, `TransportOrder.cs:78`) | Implemented |
| Một Leg có nhiều điểm pick/drop? | **Không**. `TransportLeg` có đúng một `SourceLocationCode`, một `DestinationLocationCode`, một `PickConfirmedAt`, một `DropConfirmedAt` (`TransportOrder.cs:111-113,128-129`) | **Not Implemented** |
| Mỗi Leg gửi RCS như thế nào? | Một Leg = một nhiệm vụ RCS độc lập, kèm `continueTask` cho đích `AfterPick` (`RouteEngine.cs:475, 558-563`, `dispatcher.ContinueAsync`) | Implemented (mô phỏng) |
| AMR có được giữ cho các Leg kế tiếp của cùng Order? | **Không** – các Leg của một Order là nhiệm vụ riêng, AMR có thể khác nhau; code chỉ ghi sự kiện thông tin `LEG_ROBOT_DIFFERS_FROM_PREVIOUS_LEG` (`RouteEngine.cs:415-418`) | Not Implemented (có thể là đúng ý định D-2) |
| Hai thao tác pick liên tiếp trước khi drop (AMR mang >1 rack)? | Không có mô hình. `Rack.BeginTransit` đặt `CurrentTaskId` đơn (`Rack.cs:114-122`); Leg chỉ giữ một `RackId` (`TransportOrder.cs:116`) | Not Implemented |

[HỎI Q-02] "Nhiều Pick/Drop operation" trong yêu cầu nghĩa là: (a) chuỗi Leg tuần tự (đã có), (b) một nhiệm vụ RCS với nhiều điểm dừng, hay (c) AMR mang nhiều rack? Không có tài liệu nguồn trả lời.

## 3. Dispatch và phân bổ AMR

- **RcsManaged** (CR-028): RCS chọn AMR; WCS không áp PER_AGV, không dispatch hold, không dùng AGV1/AGV2; chỉ giữ kiểm tra cổng bận (`RouteEngine.cs:473-491`). `AgvId` luôn null (`TransportOrder.cs:117-120`).
- **PER_AGV** (WCS chọn AGV): bận nếu có task legacy active, Leg khác active, hoặc dispatch hold (`RouteEngine.cs:496-506`); DB ép buộc (`RoutingConfigurations.cs:136-137`).
- **Tình trạng bộ gửi:** trên `main` chỉ có `SimulatedLegDispatcher` (`Infrastructure/DependencyInjection.cs:90`); bộ gửi thật ở `feature/cr-030-real-rcs-dispatch` chưa merge → **Not Implemented trên main** cho dispatch thật.
- **Không có `ILegDispatcher` cho nhiều điểm dừng** (interface `RoutingAbstractions.cs:86`).
- [HỎI Q-05] "PER_AGV" là quy tắc được bảo vệ (CLAUDE.md §2) nhưng ở chế độ RcsManaged nó không được áp. Cần xác nhận chế độ nào là chế độ vận hành Phase 2 (xem `DispatchModeGuard`, `WCS.Runtime/DispatchModeStartupCheck.cs`).

## 4. Hàng đợi, thứ tự và FIFO

| Hạng mục | Hành vi thực tế [CODE] | Trạng thái so với yêu cầu FIFO |
|---|---|---|
| Quét Order | `ListOpenOrderIdsAsync` sắp `CreatedAt` tăng, lấy 200 (`Infrastructure/Persistence/Repositories/RoutingRepository.cs:62-65`) → Order cũ được đánh giá trước mỗi tick | Gần FIFO theo thời điểm tạo **nhưng không là hợp đồng**: không có vị trí hàng đợi lưu trữ, không đảm bảo Order cũ chiếm chỗ trước khi Order mới có điều kiện khác |
| Quét Leg Ready | `ListReadyLegIdsAsync` sắp `CreatedAt`, lấy 50 (`RoutingRepository.cs:67-70`); Leg cổng bận bị trả về Ready với lý do `PORT_BUSY` và đánh giá lại lần sau (`RouteEngine.cs:478-483`) | Không bảo đảm thứ tự nhận cổng; Leg khác có thể nhận cổng khi vừa rảnh nếu được đánh giá trước |
| Chờ buffer đích | Order ở `Waiting`/`WaitKind.DestinationUnavailable…` đánh giá lại từng tick (`RouteEngine.cs:302-318, 323-338`) | Không có hàng đợi riêng theo buffer; ai đánh giá trước thì lấy |
| FIFO theo AGV (legacy) | `NextQueuedForAgvAsync`, ghi chú "NEEDS_CONFIRMATION (no approved priority policy)" (`Services/TaskOrchestrator.cs:204`) | Có cho legacy |
| FIFO theo PLC (D-1) | Không có | **Not Implemented** |
| Giới hạn quét | 200 order/50 leg mỗi tick; số lượng lớn hơn bị trì hoãn (không mất) | Not Verified dưới tải |

[HỎI Q-04] Yêu cầu "FIFO" áp dụng cho: giao dịch handshake mỗi PLC (D-1, đã chốt), hay còn cho chờ buffer/chờ rack giữa các Order (chưa có quyết định)?

## 5. Điều kiện Waiting / buffer

- Bước `Waiting` kiểm điều kiện kiểm soát (`RouteEngine.cs:165-180`, `EvaluateStepConditionsAsync :205`). Khi Leg trước đã thả ở Buffer/ManualBuffer, Order chờ với `WaitKind.AtBuffer` và AGV được giải phóng (`:174-178`).
- Khi không có đích và Leg đang giữ rack: `HoldWithRack` → `WaitKind.DestinationUnavailable_AgvHoldingRack`, tồn kho giữ `InTransit`, AGV vẫn bị gắn (`:341-348`). `WaitingPositionCode` chỉ là chuỗi mô tả; engine **không** điều AMR tới đó (grep: chỉ dùng trong chi tiết sự kiện và validator `ProcessValidator.cs:108-110`).
- Nếu nguồn không dùng được và `WaitBehavior.Fail` → Order Failed (`NO_CANDIDATE_AVAILABLE`, `:307-309`); riêng khi rack đã trên AMR và không có đích mà hành vi Fail → `ReconciliationRequired` (không bỏ rack) (`:598-603`).

## 6. Khôi phục và nhất quán

- Khởi động lại giữa chừng không lặp Leg đã xong (test `Cr010MultiLegTests.cs:318`); `ResumeOrphansAsync` khởi động lại Order cho giao dịch đã ghi (`PlcRequestService.cs:159-170`); giữ lại giao dịch khi mất PLC (`StationTransactionService.cs:133-138`).
- Bảo trì: không tạo Order/leg đầu mới trong Maintenance (`RouteEngine.cs:59,191,464`).
- Reconciliation: Order/Transaction/Location có `ReconciliationRequired`. Admin giải quyết **Station Transaction** qua `POST transactions/{id}/resolve` (`RoutingEndpoints.cs:89`, `StationTransactionService.cs:172-184`). Với **Order** ở `ReconciliationRequired`: engine bỏ qua (`RouteEngine.cs:141`, `:377`, `:397`); `Edges` cho phép ra InProgress/Waiting/Failed/Cancelled (`TransportOrder.cs:32`) nhưng **không thấy** endpoint/dịch vụ nào đưa Order ra khỏi trạng thái này ngoài `POST orders/{id}/cancel` (bị từ chối nếu rack trên AMR – `RouteEngine.cs:662-664`). → **Partially**: lối thoát cho Order bị đối soát khi rack đang trên AMR chưa có (rủi ro vận hành, xem IMPLEMENTATION_PLAN rủi ro R-05). [HỎI Q-15]

## 7. Kết luận

| Hạng mục | Trạng thái |
|---|---|
| Transport Job lifecycle (Order/Leg/Step/Snapshot) | Implemented |
| Nhiều Leg tuần tự | Implemented |
| Nhiều Pick/Drop trong một nhiệm vụ AMR / nhiều rack/AMR | Not Implemented (và chưa được định nghĩa) |
| Xác nhận pick/drop tự động | Not Implemented |
| Dispatch thật qua RCS | Not Implemented trên main (có ở nhánh CR-030) |
| FIFO chờ tài nguyên có bảo đảm | Not Implemented |
| Điều AMR tới vị trí chờ trung gian | Not Implemented |
