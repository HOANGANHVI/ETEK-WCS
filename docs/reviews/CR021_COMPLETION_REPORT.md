# CR-021 – Báo cáo hoàn thành: logging và xuất chẩn đoán toàn hệ thống

Ngày: 2026-11-18. Nhánh: `feature/cr-021-system-diagnostic-export`. Khảo sát và đề xuất: `CR021_PHASE1_LOGGING_SURVEY_AND_PROPOSAL.md`.
**Phạm vi kiểm chứng: RCS Simulator, MC Mock Server và PostgreSQL thử nghiệm. Chưa kiểm chứng với RCS, AGV, PLC thật và chưa deploy.**

## 1. Cấu trúc gói ZIP

```
WCS_SystemDiagnostic_<phạm vi>_<yyyyMMdd_HHmmss>.zip
├─ Summary.txt                  tổng quan, VẤN ĐỀ CỦA GÓI (đọc trước), nguồn, lỗi thường gặp, task có lỗi, hướng dẫn đọc, giới hạn
├─ Manifest.json                yêu cầu, múi giờ, khoảng thời gian thực, từng nguồn (OK/Partial/Failed/NotRequested, số dòng, bản ghi cũ nhất, retention, lỗi), danh sách file, giới hạn
├─ System_Info.txt              phiên bản (WCS_BUILD), môi trường, múi giờ máy chủ, chế độ PLC/RCS
├─ Health_Snapshot.json         runtime/worker PLC, DB, task theo trạng thái, AGV, log 1 giờ gần nhất
├─ Configuration/config.json    các mục cấu hình WCS đã che bí mật (không có connection string, không có biến môi trường)
├─ Events/events_<ngày>.jsonl   sự kiện chuẩn hóa của mọi module, theo thời gian
├─ Errors/errors.jsonl          lỗi kèm exceptionType/Message/stackTrace;  Errors/error_summary.csv
├─ Timeline/timeline.csv        cảnh báo, lỗi và mọi sự kiện task/routing/inventory/audit
├─ Modules/<Module>/<Module>_<ngày>.log   log đọc được theo module
├─ Audit/audit.jsonl            ai làm gì
└─ PLC/…                        gói PLC Diagnostic hiện có, GIỮ NGUYÊN định dạng (PLC_Connections.csv, PLC_Runtime.log, PLC_Communication.log, …)
```
Trường mỗi sự kiện: timestampUtc, level, eventName, category, module, component, source (bảng gốc), message, correlationId, taskId, agvId, plcId, stationId, rackId, actor, operation, result, durationMs, errorCode, exceptionType/Message, stackTrace, context, related. Trường không có dữ liệu để trống, không suy diễn.

## 2. Cơ chế CorrelationId
- `DiagnosticContext` (AsyncLocal) + enricher Serilog đặt CorrelationId/TaskId/AgvId/PlcId/StationId/RackId lên mọi log.
- HTTP giữ `X-Correlation-Id` hợp lệ (8–64 ký tự an toàn), không thì dùng id của request.
- Task và Order lưu id lúc tạo (cột nullable, có index). Mọi bước của Task (process, dispatch, continue, cancel, reconcile, timeout, signal) và Order/Leg tự vào lại id của chính nó, nên một Task giữ MỘT id qua nhiều request và worker.
- Yêu cầu từ PLC có id riêng, Task tạo ra kế thừa. Sự kiện PLC thường (poll, lỗi MC) **không** gắn id vì không có căn cứ.
- Bài học kỹ thuật: giá trị AsyncLocal đặt trong một hàm `async` phụ bị mất khi hàm đó trả về; mọi nơi gọi `Begin` trực tiếp trong phương thức đang chạy (phát hiện nhờ test).

Chuỗi mẫu từ kịch bản kiểm thử (CorrelationId `http-scenario-0001`, RCS Simulator từ chối lệnh continue):
`TASK_CREATED → TASK_DISPATCH_DECISION → RCS_CALL(CREATE) → RCS_CALL_FAILED(CONTINUE) → TASK_FINISHED(Failed) → INVENTORY_UNCERTAIN → POST_COMMIT_FAILED(exception + stack trace)`, kèm các dòng `task_history` và `inventory_transactions` của cùng Task (module Task/Inventory). Lỗi PLC end code 0x4031 từ MC Mock nằm trong gói (module Plc, có code và stage) nhưng correlationId rỗng: không có căn cứ để gắn vào Task.

## 3. Ma trận độ phủ trước → sau
| Nhóm | Trước | Sau | Bổ sung chính |
|---|---|---|---|
| 3.1 System/Runtime | Một phần | Đủ (phạm vi hiện có) | APP_STARTED/STOPPING/STOPPED + danh sách worker, UNHANDLED_EXCEPTION, UNOBSERVED_TASK_EXCEPTION, cảnh báo cấu hình khởi động |
| 3.2 AGV | Một phần | Một phần | AGV_STATUS_CHANGED. Pin/vị trí/mã lỗi AGV: chưa có dữ liệu (không có adapter thật) |
| 3.3 RCS | Một phần | Một phần | `LoggingRcsClient`: thao tác, request code, outcome, thời lượng, adapter. Payload/mã lỗi RCS thật: chưa có adapter thật |
| 3.4 PLC | Đủ | Đủ | Giữ nguyên; thêm vào gói toàn hệ thống và lọc theo PLC |
| 3.5 Task/Dispatch | Một phần | Đủ | TASK_CREATED/REJECTED, DISPATCH_DECISION/WAIT (lý do, ghi khi đổi), CANCEL_REQUESTED, FINISHED, RCS_CALL* |
| 3.6 Business logic | Một phần | Một phần→Đủ | Lý do từ chối tạo Task, lý do AGV chờ. Quyết định ở module khác vẫn qua mã lỗi API/audit |
| 3.7 Station/IO/Rack | Một phần | Một phần | STATION_REQUEST_ACCEPTED/REJECTED/FAILED/ABANDONED có CorrelationId riêng. Sự kiện PLC của Station chưa nối được với giao dịch |
| 3.8 Location/Inventory | Một phần | Đủ | `inventory_transactions` trong gói, INVENTORY_UNCERTAIN/RECONCILED |
| 3.9 Database | Một phần | Đủ | DbDiagnosticsInterceptor: lệnh chậm ≥ 1000 ms, deadlock, ràng buộc, timeout, mất kết nối (không ghi giá trị tham số) |
| 3.10 API/SignalR | Một phần | Đủ | Request ≥ 1000 ms/5xx/401/403 được lưu có mã; SignalR connect/disconnect |
| 3.11 Auth/Audit | Đủ | Đủ | Audit trong gói, liên kết CorrelationId qua Task |
| 3.12 Config/Deploy | Một phần | Một phần | Snapshot cấu hình đã che bí mật, STARTUP_CONFIG_WARNING; log triển khai có sẵn trong gói |

## 4. Kiểm thử
- Lát A: `SystemDiagnosticExportTests` (8). Lát B: `CorrelationTests` (9). Lát C: `DiagnosticLoggingTests` (12). Lát D: `SystemDiagnosticScenarioTests` (2). Tổng backend sau lát D: xem báo cáo chat.
- Lỗi do test phát hiện và đã sửa: bí mật trong JSON lồng (audit before/after) lọt qua bộ che; AsyncLocal mất khi đặt trong hàm async phụ; Task hoàn thành/hủy khi còn xếp hàng không đi qua đường ghi "kết thúc"; module `TASK_FINISHED` bị xếp vào RCS vì nội dung có chữ "RCS"; chế độ "all" báo Partial dù PLC không mất dữ liệu.
- Hiệu năng (PostgreSQL thử nghiệm, máy phát triển): 500 000 sự kiện (300 000 system_log + 200 000 plc_comm_log), chế độ "all": ~25 s, ZIP 13 MB, heap managed tăng ~22 MB (streaming, không giữ trong bộ nhớ). Giới hạn theo nguồn giữ bản ghi MỚI nhất và Manifest/Summary nêu số dòng bị bỏ.

## 5. Giới hạn chưa kiểm chứng / chưa thu thập được
- RCS/AGV thật: chưa có adapter; request/response thật, mã lỗi RCS, pin, vị trí không tồn tại để ghi.
- Chưa chạy với PLC thật: chỉ có MC Mock Server.
- Chưa đo trên Docker/PostgreSQL triển khai thật và chưa xuất dữ liệu nhiều tháng.
- Dữ liệu trước CR-021 không có CorrelationId (nối qua TaskId/OrderId/TransactionId nếu có).
- Lịch sử khung MC chỉ 300 khung/PLC, đọc OK lấy mẫu.
- Thư mục ZIP tạm nằm trong filesystem của container: mất khi container được tạo lại (job không bền qua khởi động lại).

## 6. Migration
`AddCorrelationIds` (chỉ cộng thêm): cột `CorrelationId varchar(100)` nullable và index trên `transport_tasks` và `transport_orders`. `system_log.CorrelationId` đã có sẵn. Không sửa/xóa dữ liệu. Chỉ được tạo và kiểm thử trên DEV/Test.

## 7. Trước khi Deploy
1. Backup DB (deploy.ps1 làm tự động), sau đó chạy migrator (migration cộng thêm, nhanh).
2. Xác nhận `Diagnostics:System:Directory` (mặc định thư mục tạm của container) hoặc mount một volume nếu muốn giữ file ZIP qua khởi động lại.
3. Cân nhắc `Diagnostics:SlowQueryMs` và `Diagnostics:SlowRequestMs` (mặc định 1000) theo thực tế tải.
4. Sau deploy: chạy một lần xuất 1 giờ từ trang Chẩn đoán, kiểm tra Manifest (completeness, các nguồn) và Summary.
5. Chưa nên coi RCS/AGV là đã được chẩn đoán đầy đủ cho đến khi có adapter thật.
