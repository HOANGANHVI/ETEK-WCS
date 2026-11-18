# 5. Integration Requirements – PLC và HIK RCS-2000

## 5.1. Kiến trúc tích hợp

```text
Mitsubishi PLC
     |
     | MC Protocol (đề xuất, cần xác nhận cấu hình thực tế)
     |
PLC Adapter
     |
     v
WCS Application
  |-- Request Handler
  |-- Process Orchestrator
  |-- Task Scheduler (PER_AGV)
  |-- Inventory Manager
  |-- PLC Handshake Manager
  |-- RCS Adapter
  |
  +--------------------> HIK RCS-2000 API
                             |
                             v
                           AGV1 / AGV2
```

Một PLC connection duy nhất phục vụ các tín hiệu của nhiều station. Không thiết kế cluster station.

## 5.2. Mitsubishi PLC

Thiết bị được cung cấp trong bối cảnh dự án:
- PLC: Mitsubishi MELSEC-Q.
- Ethernet module: QJ71E71-100.

Giao thức dự kiến:
- MC Protocol.
- 3E Frame.
- Binary code.
- TCP/IP.

Các thông số trên là cấu hình dự kiến và phải được xác nhận với kỹ sư PLC trước khi kết nối thực tế.

### Cấu hình cần có

- PLC model.
- Ethernet module.
- Protocol.
- Frame type.
- Data code.
- Transport.
- IP address.
- Port.
- Polling interval.
- Connection timeout.
- Retry interval.
- Device/address mapping.

Không hard-code các thông số này.

### PLC Adapter

Tạo interface độc lập với nghiệp vụ, ví dụ:
- `ConnectAsync`
- `ReadSignalsAsync`
- `WriteSignalsAsync`
- `GetConnectionStatus`
- `ReconnectAsync`

Không để process flow phụ thuộc trực tiếp vào thư viện/giao thức Mitsubishi.

### Signal mapping

- Tín hiệu phải được cấu hình theo station/transfer point.
- Request, ACK, status, permission và error phải có định nghĩa direction/type rõ ràng.
- Không dùng một request bit chung cho nhiều station nếu có thể gây nhầm lẫn.
- Bảng địa chỉ PLC thực tế và handshake cuối cùng cần được đội PLC xác nhận.

## 5.3. Handshake giao nhận

Mô hình logic cần hỗ trợ:
1. PLC phát request.
2. WCS ghi nhận request và ACK.
3. WCS tạo task và theo dõi AGV.
4. AGV tới transfer point.
5. WCS/PLC phối hợp điều kiện cho phép giao nhận.
6. PLC xác nhận bắt đầu và hoàn tất giao nhận.
7. AGV rời vị trí khi điều kiện cho phép.
8. WCS cập nhật inventory và task.

Các tên tín hiệu cụ thể phải khớp IO list đã thống nhất với PLC. Tên tín hiệu trong đặc tả chỉ là ý nghĩa logic, không thay thế bảng mapping địa chỉ.

Cần có:
- Request identity/sequence để chống xử lý lặp.
- Timeout cho từng bước handshake.
- Reset handshake sau khi hoàn tất theo thỏa thuận PLC.
- Error code và diagnostic log.

## 5.4. HIK RCS-2000

WCS tích hợp RCS qua REST API/JSON theo tài liệu API được cung cấp.

Các nhóm chức năng cần adapter:
- Tạo task (`genAgvSchedulingTask`).
- Tiếp tục task (`continueTask`).
- Hủy task (`cancelTask`).
- Đặt độ ưu tiên (`setTaskPriority`) nếu được sử dụng.
- Truy vấn trạng thái task (`queryTaskStatus`).
- Truy vấn trạng thái AGV (`queryAgvStatus`).

RCS có thể lựa chọn AGV nếu không truyền mã AGV, nhưng nghiệp vụ dự án đang phân công AGV1/AGV2 theo loại rack. Vì vậy cần xác nhận cách ràng buộc robot cụ thể trong API/task template thực tế.

### Callback

Tài liệu API mô tả callback từ RCS về WCS, gồm các sự kiện như:
- `start`
- `outbin`
- `end`
- `cancel`
- `ctu`

WCS cần:
- Endpoint callback có xác thực và log.
- Idempotency cho callback gửi lặp.
- Kiểm tra taskCode/robotCode trước khi cập nhật task.
- Xử lý callback đến sai thứ tự hoặc đến sau trạng thái cuối.

## 5.5. Xử lý mã phản hồi

Các mã phản hồi từng được ghi nhận trong tài liệu API:
- `0`: thành công.
- `1`: thất bại.
- `6`: không cần gửi lại (task cùng reqCode chưa hoàn tất).
- `99`: lỗi không xác định, có thể cần retry theo quy tắc API.

Phải xác minh theo đúng phiên bản RCS triển khai. Với timeout hoặc kết quả không rõ, truy vấn trạng thái trước khi gửi lại lệnh tạo task.

## 5.6. Cấu hình RCS

Các trường cấu hình:
- Base URL.
- Port/protocol.
- Authentication nếu có.
- Callback URL.
- Request timeout.
- Retry policy.
- Robot code mapping.
- Workstation code mapping.
- Task template code mapping.
- Map code nếu API yêu cầu.

Không hard-code các giá trị triển khai.

## 5.7. Tách lớp và khả năng thay thế

- `IPlcAdapter`: có thể thay Mitsubishi bằng PLC khác.
- `IRcsClient`: có thể thay HIK RCS bằng fleet manager khác.
- `ITaskOrchestrator`: điều phối nghiệp vụ độc lập vendor.
- `IInventoryService`: quản lý rack/location độc lập RCS.
- `IStationSignalService`: ánh xạ tín hiệu station.

## 5.8. Logging và monitoring

Cần ghi:
- PLC connect/disconnect.
- PLC read/write và lỗi.
- Request/ACK theo station.
- RCS request/response.
- Callback nhận được.
- Task state transition.
- Rack/location reservation.
- Inventory transaction.
- Timeout/retry/cancel.
- Người dùng thao tác PDA và điều chỉnh tồn kho.

Không ghi thông tin xác thực hoặc secret ở dạng plain text.

## 5.9. Các thông tin cần xác nhận trước nghiệm thu tích hợp

- PLC IP/port và cấu hình MC Protocol thực tế.
- Device address và data type của từng tín hiệu.
- Handshake chi tiết và timeout do PLC thống nhất.
- RCS API base URL, authentication và phiên bản.
- Robot code của AGV1/AGV2.
- Workstation code và task template cho từng chặng.
- Cách RCS giữ task tại điểm chờ và hỗ trợ `continueTask`.
- Callback network reachability và retry behavior.
- Cơ chế xác nhận rack vật lý tại từng location.

---

## 5.10. Approved PLC topology and per-Station IO sets (CR-002, 2026-11-06)

This section supersedes the single-PLC/single-connection assumption in section 5.1 and the corresponding single-connection statement in section 5.3. The remainder of this integration specification is preserved.

### Physical topology

WCS connects to four physical Mitsubishi PLCs independently:

| PLC | IP address | Stations |
|---|---|---|
| PLC-01 | `192.168.10.101` | A1, A2, A3, A4 |
| PLC-02 | `192.168.10.111` | B1, B2, D1, D2 |
| PLC-03 | `192.168.10.121` | X1, X2, X3, X6 |
| PLC-04 | `192.168.10.131` | E1, E2, G3, G7 |

Each PLC has one WCS connection. Do not create a separate TCP connection per Station.

### Station IO architecture

- Each PLC has four independent Station IO sets.
- Each Station IO set uses the common 23-signal template from the official workbook.
- Use one reusable driver implementation per PLC connection and one reusable Station IO mapping/handler instantiated independently for each Station.
- Each Station's actual device allocation must be isolated from the other Stations on the same PLC.
- Identical device addresses are allowed on different physical PLCs.
- The WCS identity key for a signal is `PLC_ID + Station_ID + Signal_Name`.
- PLC-wide signals must be explicitly classified as PLC-scoped or Station-scoped; do not silently duplicate or consolidate them.

### Official interface workbook

`docs/02_Interface_Specification/PLC_IO_Map/ETEK_WCS_IO_Map_4PLC_16Stations_v2_Separate_IO_Sets.xlsx`

The workbook is the controlled working interface reference. Preserve signal names, direction, data type and descriptions. The workbook deliberately leaves actual per-Station addresses unassigned pending PLC-engineer allocation.

### Configuration and commissioning

The following remain `NEEDS_CONFIGURATION` / `NEEDS_CONFIRMATION` until confirmed:
- Actual M/D device addresses for each Station.
- TCP port and QJ71E71-100 Ethernet settings.
- MC Protocol frame, data code, network/PC/PLC station numbers.
- STRING length and encoding, word/bit allocation and scaling.
- PLC-wide versus Station-scoped heartbeat.
- Handshake set/reset ownership, transition conditions, timeout and retry policy.

Do not derive actual addresses by applying offsets to the template reference addresses. Commission with read-only communication first. Enable PLC writes only after the PLC engineer approves the signal list and safe test procedure. Do not write safety/interlock or transfer-permit signals merely to test connectivity.

---

## Addendum – port-level Station Transaction and PLC provisioning (CR-010, 2026-11-18)

- The unit of PLC-side independence is the **port** (PLC Station). One Station Transaction is active per port; two ports of one PLC may be active together. The CR-002 baseline (4 PLCs, 16 Station IO sets) stays; the number of PLCs is data and a new PLC receives an independent copy of the default IO template, disabled and unapproved.
- WCS does **not** write any PLC signal for the 9-step exchange yet. M9101 / M9120 timing, M9003 / M9004 / M9005 mapping to pick/drop, destination-port handshake, reset ownership, D1500 layout, D1514 and Rack_Type encodings, timeouts, retries and heartbeat remain open.
- WCS reads (model and validation implemented, live reading not wired): D1500 Request_ID, D1510 Rack_Full, D1512 Rack_Empty, D1514, D1520. Only the codes 1..4 = A1..A4 are known; no other code is accepted and no substitute position is ever chosen.
