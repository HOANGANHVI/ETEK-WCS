# Chuyển đổi BP → PH2: mapping, thứ tự, điều kiện, rollback

| | |
|---|---|
| **Ngày** | 2026-11-26 |
| **Trạng thái** | `CHỜ XÁC NHẬN TỪNG ĐỢT`. Không trạm nào được chuyển. Process và mapping giữ nguyên |

## 1. Hiện trạng cấu hình

- `ProcessFlows` (engine Task): `Bp01`, `Bp02`, `Bp03`, `Bp05`, `Bp06`.
- `Plc:Integration:StationEngines`: rỗng. Mẫu cấu hình trong repo không khai báo `StationFlows`; **cấu hình thực tế tại từng máy phải được kiểm tra trước mỗi đợt** (chưa kiểm tra máy vận hành).
- Engine lệnh **không có đường nhận tín hiệu PLC thật**: `PhysicalPlcInputAdapter` có trong code nhưng không nơi nào gọi (OQ-41-02).

## 2. Mapping

| BP | Trạm kích hoạt | Tuyến BP | PH2 | Tuyến PH2 | Khác biệt phải chấp nhận |
|---|---|---|---|---|---|
| BP-01 (đầy) | A1, A2 | → B1/B2 → F1/F2 → F5 | PH2-01 | → B1/B2 → P1/P2 → M5 | Buffer F→P, F5→M5; BP không bind tại B, PH2 chưa khai báo (OQ-39-09) |
| BP-02 (cấp rỗng) | A1, A2 | B1/B2, F3/F4 → A | PH2-02 | B1/B2, P1/P2 → A | Buffer F3/F4 → P1/P2 |
| BP-03 (rỗng) | B1, B2 | → A1/A2 → F3/F4 | PH2-03 | → A1/A2 → P1/P2 → M5 | Thêm tràn M5; không bind (OQ-39-09) |
| BP-04 (REVIEW) | – | không chạy | PH2-04 | A1/A2, P1/P2 → B | Chức năng mới; cần duyệt riêng |
| BP-05 (F5 gọi đầy) | F5 | F1/F2 → F5 | PH2-06 | P1/P2 → M5 | Xác nhận F5 tương đương M5 |
| BP-06 (F5 trả rỗng) | F5 | → A1/A2 → F3/F4 | PH2-05 (**chưa duyệt publish**) | M5 → A1/A2, P1/P2 | Không có thay thế |
| – | X1, X2 | – | PH2-07/08 | X ↔ Y, P3/P4, M6 | Mới |
| – | Y1, Y2 | – | PH2-09/10 | Y ↔ X, P3/P4 | Mới |
| – | X3, X4 / Z1, Z2 | – | PH2-11/12 | X ↔ Z | Mới |

**Điểm lệch dữ liệu trạm:** cấu hình PLC hiện tại có `X1, X2, X3, X6` (PLC-03); `PH2-11/12` dùng `X4`, `Z1`, `Z2`; `Y1`, `Y2` không thuộc 16 trạm PLC. Vị trí không có cổng PLC được engine coi là không cần handshake. Phải xác nhận trước đợt X/Y và X/Z.

## 3. Rủi ro mất tiếp nhận tín hiệu PLC

| # | Tình huống | Điều xảy ra | Phòng ngừa |
|---|---|---|---|
| L1 | Đặt trạm `TransportOrder` khi dùng PLC thật | Engine Task bỏ qua trạm, engine lệnh không nhận PLC thật. **Tín hiệu bị bỏ, không log, không Ack**; PLC chờ | **Không đặt `TransportOrder` cho trạm PLC thật trước khi OQ-41-02 được duyệt và hiện thực** |
| L2 | Còn bản ghi `station_requests` đang `Pending` lúc đổi | Bị từ chối `STATION_ENGINE_IS_ORDER`, đánh dấu Rejected và Ack: **yêu cầu mất** | Điều kiện: số Pending của trạm = 0 |
| L3 | Rollback khi bit yêu cầu còn ON | Sau restart WCS coi bit ON là cạnh lên mới; Request_ID chưa từng nhận sẽ tạo task, có thể là yêu cầu đã cũ | Kỹ sư PLC xác nhận bit OFF hoặc yêu cầu còn hiệu lực trước khi khởi động |
| L4 | PLC tắt bit trong lúc không ai nhận | Mất vĩnh viễn | Khóa phát yêu cầu tại trạm trong cửa sổ chuyển đổi |
| L5 | Request_ID đã nhận bởi engine kia | Bị từ chối `REQUEST_TAKEN_BY_OTHER_ENGINE` (đúng chủ đích) | PLC phát ID mới cho yêu cầu mới |
| L6 | `StationEngines` sai chính tả hoặc khai báo hai lần | Trạm `Invalid`: không engine nào nhận | Kiểm tra trang mô phỏng hiển thị engine của trạm sau restart |
| L7 | Máy có nhiều instance, cấu hình khác nhau | Một tín hiệu hai engine | Cùng một cấu hình cho mọi instance |

## 4. Điều kiện chung cho mọi đợt

**Vào đợt:** phê duyệt bằng văn bản cho đợt đó; kết quả đợt trước đạt; scope đã quyết (OQ-54) và lỗi F1 đã xử lý; version PH2 Published và Ready; liên kết cổng và mapping IO được duyệt; sao lưu database và cấu hình; không còn Task, Order, `station_requests` đang mở tại trạm; rack tại trạm khớp thực địa.

**Dừng và rollback khi:** yêu cầu PLC không tạo việc hoặc tạo hai việc; Order vào `ReconciliationRequired` không giải thích được; vòng chờ không tự thoát; vi phạm `PER_AGV`.

**Rollback chung:** khóa phát yêu cầu tại trạm → hoàn tất hoặc hủy Order của trạm (lệnh lỗi còn giữ rack: giải phóng sau khi kiểm tra thực địa) → xóa mục `StationEngines` → xác nhận bit (L3) → restart → chạy một yêu cầu kiểm chứng qua engine Task → mở lại.

## 5. Từng đợt

| Đợt | Phạm vi | Điều kiện riêng | Việc khi chuyển | Rollback riêng | Trạng thái |
|---|---|---|---|---|---|
| **0. X/Y, X/Z (chạy thử)** | PH2-07…12, Simulator | Xác nhận trạm X4/Y/Z; `OQ-39-06`; scope | Publish, Ready, chạy mô phỏng và form. **Không đổi engine trạm vì không có BP tương ứng** | Đặt NotReady; hủy yêu cầu thử | Chờ xác nhận |
| **1. B (B1, B2)** | PH2-03, PH2-04 thay BP-03 | Đợt 0 đạt; PH2-04 được duyệt; OQ-39-09; **OQ-41-02 nếu PLC thật** | Đặt B1, B2 = `TransportOrder`; ngừng BP-03 | Rollback chung; BP-03 nhận lại | PLC thật: **BLOCKED** |
| **2. A (A1, A2)** | PH2-01, PH2-02 thay BP-01/02 | Đợt 1 đạt; OQ-39-09; mapping buffer F→P và tồn kho đã chuyển | Đặt A1, A2 = `TransportOrder`; ngừng BP-01/02 | Rollback chung; rack ở P1/P2 phải khai lại hoặc chuyển về F | PLC thật: **BLOCKED** |
| **3. F5/M5** | PH2-05, PH2-06 thay BP-05/06 | Quyết F5↔M5; PH2-05 được duyệt; OQ-41-02 | Đặt trạm = `TransportOrder` | Rollback chung; đối soát rack F5/M5 | **BLOCKED** |

**Giữa đợt 1 và 2:** BP-02 (gọi tại A) vẫn lấy rack từ B1/B2, nên hai engine cùng dùng vị trí A và B. Giữ chỗ và quyền sở hữu rack ngăn lấy trùng, nhưng không có FIFO giữa hai engine và còn lỗi F1.

## 6. Cần chủ dự án xác nhận

1. Mapping §2, đặc biệt F5↔M5 và F↔P.
2. Thứ tự X/Y → B → A → F5.
3. Chuyển tồn kho từ buffer F sang P thực hiện thế nào ngoài hiện trường.
