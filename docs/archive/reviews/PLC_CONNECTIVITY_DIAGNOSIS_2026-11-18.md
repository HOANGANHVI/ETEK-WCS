# Chẩn đoán kết nối PLC thật – 2026-11-18

**Kết luận phân loại:** **A (khóa cấu hình có chủ đích, không phải lỗi code)**, kèm **C/D ở các tầng sau**: TCP, đọc và ghi register **đã được triển khai** nhưng bị chặn bởi các khóa cấu hình/phê duyệt; handshake 9 bước **chưa được triển khai**. Không tìm thấy exception bị nuốt, worker không đăng ký hay mock bật nhầm trong cấu hình Production.

## 1. Các khóa chủ động (đều nằm ở cấu hình, mặc định ĐÓNG)

| # | Khóa | Vị trí | Điều kiện | Hệ quả |
|---|---|---|---|---|
| 1 | `Plc:AllowLiveConnection` (`PLC_ALLOW_LIVE_CONNECTION`) mặc định **false** | `deployment/docker-compose.yml:75`, `.env.example:31`; `PlcRuntime.BuildAsync` (`WCS.Application/Plc/PlcRuntime.cs`) `else if (!o.AllowLiveConnection) { enabled = false; why = "[LIVE_CONNECTION_DISABLED] …" }` | mode khác Simulator | Worker được tạo nhưng ở trạng thái Disabled, **không tạo endpoint, không mở socket** |
| 2 | Công tắc Admin `plc_connections.Enabled` | `PlcRuntime.BuildAsync` `else if (!c.Enabled)` → `[CONNECTION_NOT_ENABLED]` | từng PLC | Không mở socket cho PLC đó |
| 3 | Tham số kết nối thiếu | `McPlcEndpoint` (constructor) ném `NeedsConfigurationException`; `PlcWorker.RunAsync` → trạng thái `NeedsConfiguration` | port, protocol, network, PC, module I/O, station, monitoring timer, timeout | Không mở socket |
| 4 | Điểm IO chưa `Approved` | `StationIoPoint.IsPollable`; `McPlcEndpoint.ReadStationAsync` → `PointQuality.NotConfigured` | trạng thái Approved + có địa chỉ | **Có TCP nhưng không có lưu lượng đọc nào** |
| 5 | Chế độ `RealReadOnly` và khóa ghi | `WritePolicy.Evaluate/EffectiveMode`; `WriteKeysComplete` | `Write.Enabled`, `EngineerApprovalReference`, `HandshakeApproved`, điểm `WriteAllowed` | Không ghi được; tín hiệu Safety/Permit luôn bị chặn theo tên |
| 6 | Production không cho Simulator | `ProductionSafety.Validate` | `Plc:Mode=Simulator` trong Production | Từ chối khởi động (không phải khóa kết nối thật) |

## 2. Phân tầng giao tiếp (bằng chứng)

| Tầng | Trạng thái | Bằng chứng |
|---|---|---|
| 1. Backend có khởi tạo worker | **Có** | `PlcRuntimeHostedService` đăng ký trong `WCS.Runtime/DependencyInjection.cs` (điều kiện `Plc:RuntimeEnabled`, mặc định true). Log thật: `PLC runtime started: 4 workers, mode RealReadOnly` |
| 2. Tạo TCP tới IP/Port | **Đã triển khai, chỉ chạy khi qua khóa 1–3** | `McTcpClient.OpenAsync` (`TcpClient.ConnectAsync`). Thử thật: `AllowLiveConnection=false` → log `PLC-01 DISABLED: [LIVE_CONNECTION_DISABLED]`, không có thử kết nối; `=true` + PLC-01 cấu hình tới cổng đóng cục bộ → `Connecting → Faulted [PLC_CONNECT_FAILED] … actively refused … Retrying in 1000 ms` (có thử TCP thật, thất bại đúng như mong đợi; PLC-02/03/04 `CONNECTION_NOT_ENABLED`) |
| 3. Protocol handshake | **Không có bước handshake riêng**: MC 3E Binary qua TCP không có phiên; khung đầu tiên là lệnh đọc | `McFrame`, `McTcpClient.SendAsync` |
| 4. Đọc register/bit | **Đã triển khai**, chỉ cho điểm Approved | `McPlcEndpoint.ReadStationAsync` (gộp đọc liên tục, BOOL/INT/STRING ASCII); kiểm thử với `MelsecMcMockServer` (không phải PLC thật) |
| 5. Ghi register/bit | **Đã triển khai**, bị chặn mặc định | `McPlcEndpoint.WritePointAsync`; `WritePolicy` |
| 6. Handshake 9 bước | **Chưa triển khai** | `HandshakeCoordinator` chỉ quan sát, không có luật được phê duyệt; `Request_Ack`, `Heartbeat_SW`, `Transfer_Permit` không bao giờ được ghi; CR-010 không ghi tín hiệu PLC nào |

Không có kết luận "PLC đã kết nối" từ cấu hình IP/Port: trạng thái `Connected` chỉ có sau `TcpClient.ConnectAsync` thành công.

## 3. Cấu hình thực tế đọc được (cơ sở dữ liệu phát triển, chỉ SELECT)

- PLC-01 `192.168.10.101`: port 5000, `McProtocol3EBinaryTcp`, network 1, PC 1, module I/O 1, station 1, monitoring 300, timeout 5000 ms, polling 2000 ms, **Enabled**.
- PLC-02 `.111`: chưa có tham số, **tắt**. PLC-03 `.121`, PLC-04 `.131`: chưa có tham số, bật (sẽ báo `NeedsConfiguration`).
- Điểm IO: 2 điểm `Configured` (PLC-01), 0 điểm `Approved`, 366 điểm `NeedsConfiguration` ⇒ ngay cả khi TCP thông, **không có lệnh đọc nào được gửi**.
- Máy lập trình có IP 192.168.30.7 và 172.16.0.2, **không nằm trong mạng 192.168.10.x**; thử TCP tới `192.168.10.101:5000` (3 giây, không gửi byte) **thất bại/hết thời gian**. Đây là hạn chế môi trường, không kết luận được về PLC thật.
- Các giá trị Network/PC/Module = 1/1/1 là giá trị đã được nhập trong cấu hình, **không do tôi thay đổi và chưa được xác minh**; phải do kỹ sư PLC xác nhận trước khi đọc.

## 4. Nguyên nhân chính khiến WCS chưa kết nối PLC thật

1. **Khóa 1:** `PLC_ALLOW_LIVE_CONNECTION` mặc định `false` trong `.env.example`/compose. Nếu `.env` trên máy Deploy chưa đặt `true` thì mọi worker ở trạng thái Disabled và không có thử TCP nào.
2. Khóa 2–4: từng PLC phải được Admin bật, đủ tham số, và các điểm IO phải được duyệt thì mới có lưu lượng đọc.
3. Khác (cần kiểm tra trên máy Deploy): máy Deploy có tới được `192.168.10.x`, cổng TCP đã được kỹ sư PLC xác nhận, danh sách IP được phép trên module Ethernet của PLC (PLC thấy IP của máy Windows, không phải container), tường lửa.

## 5. Phạm vi thay đổi

Không thay đổi code kết nối PLC, không thay đổi IP/Port hay thông số, không mở chức năng ghi, không đụng `InventoryService`/`TaskStateMachine`/`TaskOrchestrator`.

## 6. Kiểm tra trên máy Deploy (chỉ đọc, cần kỹ sư PLC)

1. Chạy `deployment\scripts\plc-route-test.ps1 -Port <cổng đã xác nhận>` (chỉ mở/đóng TCP, không gửi byte).
2. Đặt `PLC_ALLOW_LIVE_CONNECTION=true`, giữ `PLC_MODE=RealReadOnly`, **không** đặt khóa ghi; khởi động lại backend.
3. Admin: *Kết nối PLC* → nhập/xác nhận tham số → bật từng PLC. Xem log `docker compose logs backend | findstr PLC-` tìm `STATE: Connected` hoặc `CONNECT_FAILED`.
4. Đọc: duyệt từng điểm IO cần đọc ở *Cấu hình địa chỉ IO*; quan sát chất lượng `Good`/`Bad` và mã lỗi PLC (end code).
5. Chỉ sau khi kỹ sư PLC phê duyệt mới bàn tới ghi và handshake.
