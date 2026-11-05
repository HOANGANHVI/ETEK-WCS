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
