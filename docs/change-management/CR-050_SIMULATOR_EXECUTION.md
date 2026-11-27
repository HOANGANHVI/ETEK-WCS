# CR-050 – Simulator: PLC mô phỏng dùng được; RCS/AMR tự thực thi; tự rời cổng

| | |
|---|---|
| **Trạng thái** | `THIẾT KẾ ĐƯỢC DUYỆT QUA QUYẾT ĐỊNH D-01/D-03/D-04 (2026-11-27) – ĐÃ TRIỂN KHAI, ĐÃ KIỂM THỬ; NHÁNH `feature/cr-050-simulator`, CHƯA MERGE` |
| **Nguồn phê duyệt** | Chủ dự án, 2026-11-27 |
| **Nền** | `feature/cr-049-qa-fixes` (`63e0883`) |
| **Migration** | Không |

## 1. Phạm vi được duyệt

- **D-01:** trang Mô phỏng PLC hoạt động khi runtime PLC ở chế độ Simulator. Write Guard, AllowList, chế độ Real giữ nguyên.
- **D-04:** AMR mô phỏng rời vùng cổng thì Simulator xác nhận rời cổng. Không tự nhả theo thời gian chờ. Real mode không đổi.
- **D-03:** RCS/AMR Simulator: đội AMR, vị trí, di chuyển, nhiệm vụ cha và trigger, tự Pick/Drop và phản hồi, tạo lỗi từ giao diện, ghi bản tin.

## 2. Kiến trúc

```
RouteEngine ── ILegDispatcher ── RoutedLegDispatcher ─┬─ (order Real) RealLegDispatcher (không đổi)
                                                      └─ (order Simulator) SimulatedLegDispatcher ── AmrExecutionSimulator
TaskOrchestrator ── IRcsClient ── RoutedRcsClient ─── (task Simulator) RcsSimulator ─────────────── AmrExecutionSimulator
AmrExecutionSimulator ──> IRouteEngine.ConfirmPick/ConfirmDrop, StationTransactionService.ConfirmAgvExitedForLeg,
                          SimulationDriver / ITaskOrchestrator.HandleSignal, IRouteEngine.ReportLegProblem, IRcsMessageRecorder
```

Không có luồng nghiệp vụ thứ hai: tồn kho, giữ chỗ, trạng thái lệnh và cổng chỉ do các service hiện có thay đổi. Quy trình vận chuyển lấy từ cấu hình hiện có.

### Quy tắc ranh giới

- Mặc định **tắt** (`Rcs:Simulator:Execution:Enabled=false`); bật/tắt bằng Admin; trạng thái trong bộ nhớ.
- Chỉ nhận chặng gửi qua `SimulatedLegDispatcher` **khi đang bật** của **lệnh tạo sau khi bật**, và nhiệm vụ Simulator tạo sau khi bật. Lệnh, nhiệm vụ, chặng và cổng có trước không bị Simulator thực thi, kể cả khi chặng được gửi sau đó (ví dụ sau khi Admin nhả cổng): chúng được nhận như hành vi cũ và chờ thao tác tay. Tắt rồi bật lại thì mốc tính theo lần bật mới.
- **Lưu ý:** lỗi "RCS từ chối", "RCS timeout" đã gài và trạng thái mất kết nối mô phỏng tác động lên mọi lần **tạo nhiệm vụ** của engine nhiệm vụ đơn trong lúc bật, kể cả nhiệm vụ cũ đang chờ gửi.
- API chỉ tồn tại trong nhóm API mô phỏng hiện có (không Real Dispatch, `Integration:Mode=Simulator`, được phép expose); từ chối bật khi Real Dispatch bật.
- Trước mỗi sự kiện, kiểm lại lệnh/nhiệm vụ còn mở và ở chế độ Simulator.

### Trạng thái một lượt chạy (một chặng = một nhiệm vụ RCS)

`Queued → ToSource → Picking → [WaitingForTrigger] → ToDestination → Dropping → Leaving → Completed`; lỗi → `Failed`; lệnh bị hủy → `Cancelled`.

- **Nhiệm vụ cha:** các lượt chạy được nhóm theo mã yêu cầu của lệnh. Chưa có hợp đồng "Parent Task" của RCS; đây là cách nhóm của Simulator.
- **Trigger:** đến nguồn → Pick; `Continue` (đích) → đi tiếp; đến đích → Drop; ra khỏi bán kính cổng → rời cổng.
- Vị trí và quãng đường là **dữ liệu tổng hợp** của Simulator (lưới theo mã Location), không phải bản đồ thật.

### Lỗi tạo được từ giao diện

RCS từ chối, RCS timeout khi nhận lệnh, mất kết nối (sự kiện bị giữ), lỗi di chuyển, lỗi Pick, lỗi Drop, AMR treo, phản hồi trễ, phản hồi lặp; thao tác "AMR đã phục hồi".

Lỗi thực thi của **chặng** lệnh vận chuyển: WCS chưa có quy tắc được duyệt, nên chỉ ghi sự kiện `LEG_RCS_INTERRUPTED`; không đổi trạng thái, tồn kho hay giữ chỗ. Lỗi của **nhiệm vụ** đi qua tín hiệu `RcsInterrupted` đã được duyệt (CR-008B-4).

## 3. File dự kiến

| Phần | File |
|---|---|
| Simulator | `Infrastructure/Simulation/AmrExecutionSimulator.cs` (mới), `RoutingAdapters.cs`, `Simulators.cs`, `DependencyInjection.cs` |
| Engine | `RouteEngine.cs` (thêm `ReportLegProblemAsync`, chỉ ghi sự kiện) |
| D-01 | `PlcSimulationService.cs`, `PlcSignalSimulation.cs` |
| API | `Endpoints.cs` (`/api/simulator/amr/*`) |
| Giao diện | trang Mô phỏng PLC: khối RCS/AMR; kiểu, client, chữ |
| Test | `Cr050SimulatorTests.cs` (mới), cập nhật `Cr040QueueSimulationRackTests` |

## 4. Tiêu chí PASS

1. Runtime Simulator, PLC giả lập kết nối: gửi yêu cầu được. Runtime Real, PLC kết nối: vẫn từ chối.
2. Lệnh BP: tự Pick, Drop, hoàn tất, nhả cả hai cổng; lệnh chờ tiếp theo tự chạy; tồn kho đúng.
3. Nhiệm vụ đơn: tự hoàn tất.
4. Tắt Simulator: hành vi như trước; dữ liệu tạo trước khi bật không đổi.
5. Mỗi lỗi: trạng thái lệnh, chặng, cổng, rack, giữ chỗ nhất quán; phục hồi được hoặc báo rõ không phục hồi được.
6. Không khung tin tới PLC thật; không bản tin RCS chế độ Real; không lệnh ghi PLC do Simulator.

## 5. Ngoài phạm vi – cần kỹ sư PLC / nhà cung cấp RCS

- Kích hoạt yêu cầu từ tín hiệu IO Map 22/11 (sườn/mức, mã giá trị, reset) và **phản hồi WCS → PLC**: chưa hiện thực; Simulator không ghi tín hiệu nào.
- Mã Drop hoàn tất (5/6), thanh ghi dùng chung, ý nghĩa `Mission_Feedback`, HeartBit, timeout.
- Hợp đồng RCS cho chặng cấu hình được, Parent Task, callback lỗi của chặng (Q-MLEG-1).

## 7. Kết quả triển khai (2026-11-27)

| Phát hiện khi kiểm thử | Xử lý |
|---|---|
| **Lỗi có sẵn trong engine:** RCS từ chối một chặng thì lệnh lỗi, giữ chỗ được nhả nhưng giao dịch cổng vẫn mở, không thao tác nào nhả được | Sửa (mục 7.1). Có test |
| WCS không gửi lại trigger đích bị mất do mất kết nối | Không đổi WCS. Simulator giữ AMR chờ; nút phục hồi gửi lại. Cần quy tắc cho RCS thật |
| Chặng bị AMR báo lỗi: WCS chưa có quy tắc | Chỉ ghi sự kiện LEG_RCS_INTERRUPTED; cần quyết định |

### 7.1 Thay đổi nghiệp vụ: đóng giao dịch cổng khi RCS từ chối chặng

Áp dụng cho **cả chế độ Real** (nhánh chung của engine). `Rejected` chỉ phát sinh khi RCS trả mã 1, hoặc WCS từ chối trước khi gửi (thiếu cấu hình hay ánh xạ). Timeout, lỗi kết nối, HTTP lỗi, mã 99 **không** phải từ chối: chặng và cổng giữ nguyên, thử lại.

| Tình trạng giao dịch cổng của chặng bị từ chối | Xử lý |
|---|---|
| Chưa xác nhận Pick và giao dịch chưa quá `AgvAssigned` | `Failed`, cổng được nhả |
| Đã xác nhận Pick, hoặc giao dịch đã quá `AgvAssigned` | `ReconciliationRequired` (`LEG_REJECTED_AFTER_PHYSICAL_PROGRESS`), cổng **vẫn bị giữ**, người xử lý |
| Đang `ReconciliationRequired` | Không đổi |
| Giao dịch của chặng khác | Không đổi |

Tồn kho không đổi ở nhánh này. Việc lệnh và chặng chuyển Failed, giữ chỗ được nhả là hành vi có sẵn, không đổi, kể cả khi Pick đã được xác nhận bằng tay (cần quy tắc riêng). Giao dịch đã mở từ trước bản sửa không tự được đóng.

Giới hạn của Simulator: một chặng là một nhiệm vụ RCS; nhiệm vụ cha là cách nhóm theo mã lệnh; vị trí tổng hợp, không tránh va chạm hay giữ làn; "Phản hồi lặp" chỉ áp dụng cho chặng; tín hiệu "PLC permission" của engine nhiệm vụ do SimulationDriver phát như trước, không đi qua IO; trạng thái mất khi khởi động lại hoặc tắt (chặng đang dở phải xử lý bằng tay); phản hồi trễ tính theo giây mô phỏng.

Bằng chứng: [lỗi Pick](../reports/cr-050/1-pick-failure.png) · [hoàn tất](../reports/cr-050/2-completed.png) · [AMR đang chạy](../reports/cr-050/3-amr-monitoring-moving.png) · [lệnh](../reports/cr-050/4-orders.png).

## 6. Rollback

Hoàn tác thay đổi; không đổi schema hay dữ liệu. Tắt cấu hình là trở về hành vi cũ.
