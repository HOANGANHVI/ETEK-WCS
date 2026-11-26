# Engine theo trạm và form tạo yêu cầu thủ công

| | |
|---|---|
| **Ngày** | 2026-11-26 |
| **Trạng thái** | `PHÂN TÍCH VÀ ĐỀ XUẤT – CHƯA SỬA` |

## 1. Vì sao form vẫn chọn được cả Phase 1 và PH2

| Lối vào | Hỏi engine của trạm? |
|---|---|
| PLC thật → engine Task (`PlcStationSignalService`, `StationRequestProcessor`) | Có |
| `plc-requests`, mô phỏng PLC (`PlcRequestService`, `PlcInputHandler`) | Có |
| Form thủ công → `POST /api/tasks` | **Không** |
| Form thủ công → `POST /api/routing/orders` | **Không** |

- OQ-42-03 được hiện thực theo nghĩa "**một tín hiệu** một engine"; thao tác tay không phải tín hiệu.
- CR-041 chủ đích dùng một danh sách chung (`RequestOptions`): nhóm luồng Phase 1 và nhóm quy trình đã Publish, lấy từ server, không lọc theo trạm.
- Chặn riêng ở giao diện là không đủ vì API vẫn nhận.

## 2. Nguy cơ

| # | Nguy cơ | Cơ chế hiện có | Mức |
|---|---|---|---|
| M1 | Chọn sai quy trình: `Bp01` đưa tràn sang F1/F2/F5, `PH2-01` sang P1/P2/M5 | Chỉ kiểm tra trạm được phép của từng quy trình | Cao: rack đi sai khu |
| M2 | Hai yêu cầu cùng trạm, cùng loại rack, hai engine | `RequestKey` khác nhau nên đều được nhận | Cao |
| M3 | Hệ quả của M2: yêu cầu thứ hai đang chờ sẽ lấy **rack kế tiếp** đặt vào trạm | Giữ chỗ chỉ chống lấy cùng một rack | Cao: chuyển rack ngoài ý muốn |
| M4 | `PER_AGV`: Task được giao cho AGV đang chạy chặng | Không kiểm tra (F1, đã tái hiện) | **Nghiêm trọng** |
| M5 | Hai engine cùng phục vụ một cổng PLC | Engine Task không mở Station Transaction | Cao khi có handshake thật |
| M6 | Không có FIFO giữa hai engine | – | Trung bình |
| M7 | Người vận hành tin rằng "đã chuyển trạm" nên chỉ còn một luồng | Hiển thị engine chỉ ở trang mô phỏng | Trung bình |

Đã được bảo vệ: một `RequestKey` chỉ thuộc một engine; không lấy cùng một rack; không vượt sức chứa; Real cho Transport Order bị chặn.

## 3. Đề xuất: một nơi quyết định cho mọi lối vào

Backend hỏi `IStationEngineRouter` **trong `CreateTaskAsync` và `StartAsync`**, áp dụng cho PLC, mô phỏng và thao tác tay. Giao diện chỉ phản ánh: API danh sách trả engine của từng trạm; form chọn **trạm trước**, chỉ liệt kê lựa chọn hợp lệ.

| Engine của trạm | Task Phase 1 | Transport Order |
|---|---|---|
| `Task` | Nhận | Từ chối `STATION_ENGINE_IS_TASK` |
| `TransportOrder` | Từ chối `STATION_ENGINE_IS_ORDER` | Nhận |
| `Invalid` | Từ chối | Từ chối |
| `Unassigned` | Xem MR-1 | Xem MR-1 |

Điểm cần lưu ý khi hiện thực: với `Unassigned`, router hiện trả "không ai nhận tín hiệu thật" nhưng mô phỏng vào engine lệnh; thao tác tay cần quy tắc riêng. "Trạm" là vị trí yêu cầu; trạm không có cổng PLC (M5, Y, Z) cũng phải khai báo được.

**Không đề xuất** ngoại lệ cho Admin: ngoại lệ tái tạo M1–M3.

## 4. Cần quyết định

| Mã | Câu hỏi | Phương án | Đề xuất |
|---|---|---|---|
| MR-1 | Trạm chưa gán | (a) Giữ cả hai như hiện nay, cảnh báo trên form. (b) Chỉ Phase 1 cho tới khi gán. (c) Từ chối cả hai | **(a)** cho đến khi bắt đầu đợt chuyển đổi đầu tiên, sau đó (b) |
| MR-2 | Áp quy tắc cho thao tác tay | Có / Không | **Có** |
| MR-3 | Chặn yêu cầu thứ hai cùng trạm và loại rack khi yêu cầu trước chưa xong (kể cả cùng engine) | (a) Chặn. (b) Chỉ cảnh báo. (c) Giữ nguyên | **(b)**: chặn có thể trái FIFO nhiều yêu cầu đã duyệt (D-12, D-13) |
| MR-4 | Nơi lưu gán engine | (a) File cấu hình. (b) Database, có trang Admin và audit | **(a)** bây giờ; (b) khi chuyển nhiều đợt |

Trong khi chưa quyết: không dùng đồng thời Task Phase 1 và Transport Order cho cùng trạm.
