# 4. Inventory Management – Quản lý Rack và vị trí

## 4.1. Nguyên tắc sở hữu dữ liệu

WCS quản lý tồn kho rack và trạng thái chiếm dụng vị trí. RCS chịu trách nhiệm điều phối robot và thực thi task; không thay thế WCS làm nguồn dữ liệu tồn kho nghiệp vụ.

## 4.2. Thực thể dữ liệu đề xuất

### Rack

| Field | Ý nghĩa |
|---|---|
| `RackId` | Mã rack; có thể chưa có nếu hiện trường không nhận dạng từng rack |
| `RackType` | Loại rack |
| `RackStatus` | `FULL`, `EMPTY`, `UNKNOWN`, `IN_TRANSIT` |
| `CurrentLocationId` | Vị trí hiện tại |
| `CurrentTaskId` | Task đang di chuyển rack, nếu có |
| `LastConfirmedAt` | Thời điểm xác nhận gần nhất |
| `Version` | Phục vụ concurrency/optimistic locking |

### Location

| Field | Ý nghĩa |
|---|---|
| `LocationId` | Mã vị trí |
| `LocationType` | Station, buffer, manual buffer, waypoint hoặc loại phù hợp |
| `RackCapacity` | Sức chứa, cần cấu hình theo thực tế |
| `OccupancyStatus` | `EMPTY`, `OCCUPIED`, `UNKNOWN`, `RESERVED` |
| `AllowedRackStatus` | Loại rack được phép chứa |
| `Enabled` | Vị trí có được sử dụng hay không |

### Inventory Transaction

Lưu mọi thay đổi tồn kho:
- Rack.
- Vị trí nguồn.
- Vị trí đích.
- Task ID.
- Loại sự kiện.
- Trạng thái trước/sau.
- Thời gian.
- Nguồn xác nhận (PLC, operator, WCS reconciliation).
- Người dùng thực hiện nếu là thao tác thủ công.

## 4.3. Sự kiện cập nhật inventory

WCS chỉ cập nhật tồn kho dựa trên sự kiện được xác nhận:

1. `PICK_CONFIRMED`: rack được xác nhận đã lấy khỏi nguồn.
2. `DROP_CONFIRMED`: rack được xác nhận đã thả tại đích.
3. `MANUAL_ADJUSTMENT`: người có quyền điều chỉnh tồn kho.
4. `INITIAL_STOCK_DECLARED`: khai báo tồn ban đầu, bao gồm số lượng rack F3/F4 qua PDA.
5. `RECONCILIATION`: đối soát trạng thái thực tế sau lỗi/mất kết nối.

## 4.4. Quy tắc nhất quán

- Một rack có tối đa một vị trí hiện tại.
- Một vị trí chỉ được phân bổ theo capacity đã cấu hình.
- Trước khi cấp task, reserve rack nguồn và vị trí đích phù hợp.
- Nếu task không thực hiện được, không tự giải phóng reservation trước khi đối soát trạng thái vật lý.
- Không đánh dấu vị trí đích `OCCUPIED` khi mới tạo task.
- Sau `PICK_CONFIRMED`, rack chuyển sang `IN_TRANSIT` hoặc trạng thái tương đương.
- Sau `DROP_CONFIRMED`, rack được gán vị trí đích và trạng thái `FULL`/`EMPTY` phù hợp.

## 4.5. Reservation và khóa tài nguyên

Để hỗ trợ hai AGV chạy song song:
- Khóa theo `AGV_ID`: một task hoạt động trên mỗi AGV.
- Khóa theo `Rack_ID`: không cho hai task cùng lấy một rack.
- Khóa theo `Location_ID`: không cho hai task cùng reserve một vị trí không đủ sức chứa.
- Các khóa phải được tạo và giải phóng theo transaction có kiểm soát.
- Tránh deadlock bằng thứ tự khóa nhất quán.

## 4.6. Bind rack và các vị trí không bind

WCS quản lý dữ liệu tồn kho, nhưng cách xác nhận tại từng vị trí phải theo process:
- F1/F2: quy trình dùng kiểm tra bind rack; thay bằng truy vấn WCS inventory sau khi WCS đã có dữ liệu xác nhận tương đương.
- F3/F4: quy trình dùng kiểm tra bind rack; WCS inventory là nguồn quản lý nghiệp vụ.
- F5: quy trình dùng kiểm tra bind rack; WCS inventory là nguồn quản lý nghiệp vụ.
- B1/B2 trong BP-01: tài liệu ghi rõ thả rack không bind rack.
- A1/A2 trong BP-03/BP-06: tài liệu ghi rõ thả rack không bind rack.

Không áp dụng một quy tắc bind chung cho mọi location.

## 4.7. PDA và tồn kho ban đầu

- Hỗ trợ khai báo số lượng rack tại F3/F4 bằng PDA.
- Ghi nhận người thao tác, thời gian, số lượng trước/sau.
- Kiểm tra quyền và giới hạn capacity nếu đã được cấu hình.
- Nếu không có Rack ID riêng, cho phép quản lý theo số lượng và loại rack; cần xác nhận mô hình dữ liệu cuối cùng.

## 4.8. Đối soát sau sự cố

Khi mất kết nối, task interrupted, AGV dừng giữa đường hoặc không rõ kết quả giao nhận:
1. Đánh dấu rack/location liên quan `UNKNOWN` hoặc `RECONCILIATION_REQUIRED`.
2. Không cho task khác sử dụng tài nguyên chưa rõ trạng thái.
3. Đối chiếu trạng thái RCS, PLC và xác nhận hiện trường.
4. Người có quyền xác nhận vị trí thực tế.
5. Ghi inventory transaction điều chỉnh.
6. Giải phóng reservation sau khi đối soát hoàn tất.

## Quyền thao tác tồn kho (CR-001, đã phê duyệt 2026-11-06)

Admin và Operator đều được xem và dùng các API tồn kho hiện có (khai báo ban đầu, điều chỉnh số lượng); mọi thay đổi được ghi nhật ký. Các kiểm tra sức chứa, loại rack và rack đang giữ chỗ không thay đổi. Chi tiết và phần còn thiếu: access-control.md mục 5.
