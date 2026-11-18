# 3. Task State Machine – Vòng đời và điều phối Task

## 3.1. Mục tiêu

Quản lý vòng đời task nghiệp vụ của WCS độc lập với trạng thái task do HIK RCS-2000 cung cấp. WCS phải lưu mapping giữa `WcsTaskId` và `RcsTaskCode`.

## 3.2. Trạng thái nghiệp vụ

| State | Ý nghĩa |
|---|---|
| `PENDING` | Đã tiếp nhận yêu cầu, chờ xử lý |
| `VALIDATING` | Đang kiểm tra điều kiện nghiệp vụ |
| `QUEUED` | Đủ điều kiện nhưng AGV đang bận |
| `DISPATCHING` | Đang gửi task tới RCS |
| `ACCEPTED` | RCS đã tiếp nhận task |
| `EXECUTING` | AGV đang thực hiện |
| `WAITING_FOR_PLC` | Chờ PLC cấp quyền hoặc xác nhận giao nhận |
| `WAITING_FOR_DESTINATION` | Chờ có vị trí đích phù hợp |
| `CONTINUING` | WCS đang yêu cầu RCS tiếp tục task |
| `COMPLETED` | Hoàn thành toàn bộ nghiệp vụ |
| `FAILED` | Thất bại |
| `CANCEL_PENDING` | Đang yêu cầu hủy |
| `CANCELLED` | Đã hủy và được xác nhận |

## 3.3. Quy tắc concurrency PER_AGV

- Mỗi AGV có tối đa một task ở trạng thái hoạt động.
- Các trạng thái được xem là chiếm AGV: `DISPATCHING`, `ACCEPTED`, `EXECUTING`, `WAITING_FOR_PLC`, `WAITING_FOR_DESTINATION`, `CONTINUING`, `CANCEL_PENDING`.
- `PENDING` và `QUEUED` chưa chiếm AGV.
- `COMPLETED`, `FAILED`, `CANCELLED` là trạng thái cuối, không chiếm AGV.
- AGV1 và AGV2 có thể cùng có một task hoạt động.
- Cần khóa/transaction ở tầng dữ liệu để tránh hai worker cùng phân công task cho một AGV.

## 3.4. Quy tắc hàng đợi

1. Yêu cầu mới được tạo thành WCS task.
2. Kiểm tra loại AGV theo nghiệp vụ.
3. Nếu AGV tương ứng đang bận, task chuyển `QUEUED`.
4. Khi AGV rảnh, scheduler chọn task đủ điều kiện theo chính sách ưu tiên đã cấu hình.
5. Không tự đặt thứ tự ưu tiên nếu chưa có quy tắc được xác nhận.

## 3.5. Quy tắc tiếp tục task

- `continueTask` được sử dụng tại các điểm tạm dừng theo task flow.
- Chỉ gọi sau khi WCS nhận được điều kiện tiếp tục hợp lệ, ví dụ xác nhận lấy rack xong.
- Trước khi gọi, kiểm tra task còn tồn tại và chưa ở trạng thái cuối.
- Lưu request/response và kết quả gọi API.
- Nếu timeout hoặc phản hồi không rõ, truy vấn trạng thái task RCS trước khi gửi lại để tránh lặp hành động.

## 3.6. Waiting for destination

Khi không có vị trí đích:
- Giữ task hiện tại ở `WAITING_FOR_DESTINATION`.
- Giữ quan hệ AGV-task; AGV chưa được nhận task mới nếu task RCS hiện tại vẫn đang chiếm AGV.
- Theo dõi thay đổi inventory để đánh giá lại.
- Khi có đích phù hợp, khóa vị trí đích, cập nhật task và yêu cầu RCS tiếp tục theo cơ chế task template đã cấu hình.
- Không tạo task thay thế trùng với task hiện hữu.

**Cần xác nhận kỹ thuật:** RCS task template có hỗ trợ chờ tại waypoint và tiếp tục bằng API trong đúng trạng thái đang chờ hay không. Nếu không, phải thiết kế cơ chế nhiều chặng task và được duyệt trước khi triển khai.

## 3.7. Retry và idempotency

- Mỗi yêu cầu PLC/OP cần có khóa chống lặp (request identity hoặc sequence).
- Mỗi lần tạo task RCS phải có request code duy nhất theo quy tắc API.
- Khi nhận timeout, không mặc định rằng RCS chưa tạo task.
- Truy vấn RCS trước khi gửi lại lệnh có khả năng tạo task trùng.
- Ghi log request/response và số lần retry.

## 3.8. Ánh xạ trạng thái RCS

Các mã trạng thái RCS từng được ghi nhận trong tài liệu API:
- `0`: sending exception.
- `1`: created.
- `2`: executing.
- `3`: sending.
- `4`: canceling.
- `5`: canceled.
- `6`: resending.
- `9`: completed.
- `10`: interrupted.

Ánh xạ sang trạng thái WCS phải được triển khai trong adapter riêng và xác nhận theo đúng phiên bản API thực tế. Không dùng trực tiếp mã RCS làm trạng thái nghiệp vụ WCS.

## 3.9. Hủy và lỗi

- Chỉ đánh dấu `CANCELLED` khi RCS xác nhận hủy hoặc có cơ chế đối soát tương đương.
- Khi task bị interrupt, chuyển trạng thái lỗi/chờ xử lý theo chính sách; không tự đánh dấu hoàn thành.
- Nếu rack đã được lấy khỏi nguồn nhưng chưa thả tại đích, trạng thái rack phải là đang vận chuyển/đang xử lý, không được ghi nhận ở nguồn hoặc đích như bình thường.
- Cần đối soát vị trí thực tế trước khi giải phóng khóa rack/vị trí.

## Sửa / xóa nhiệm vụ bởi người dùng (CR-001, đã phê duyệt 2026-11-06)

Bảng chuyển trạng thái **không đổi**. Nhiệm vụ còn chờ (Pending, Validating, Queued) và chưa được AGV/RCS tiếp nhận có thể được người dùng sửa thông tin yêu cầu hoặc **xóa cứng** (bản ghi bị xóa, không chuyển sang Cancelled); nhật ký thao tác được giữ. Chi tiết: access-control.md mục 3–4.

## Làm rõ không đổi quy tắc (CR-008B-3, 2026-11-17)

Bảng chuyển trạng thái và các quy tắc ở 3.1–3.9 **không thay đổi**. Chỉ ghi nhận cách triển khai tuân thủ 3.9 và quyết định D-1 của chủ dự án:
- Khi hủy nhiệm vụ (`CANCEL_PENDING`) mà RCS không xác nhận sau 30 lần thử tự động: dừng thử, chuyển `FAILED`, đánh dấu cần đối soát, giữ nguyên reservation và trạng thái rack/vị trí. `FAILED` không có nghĩa RCS đã hủy.
- RCS báo "không tìm thấy" (NotFound) **không** được xem là xác nhận hủy; `CANCELLED` chỉ khi RCS xác nhận hoặc có cơ chế đối soát tương đương **đã được chủ dự án phê duyệt** (chưa có).
- Giải phóng reservation chỉ xảy ra khi Admin nêu vị trí thực tế đã xác minh (đối soát theo `inventory-management.md` 4.8).
