# Phân quyền và tài khoản (Access Control)

**Nguồn:** CR-001, được chủ dự án phê duyệt chính thức ngày 2026-11-06. Đây là quy tắc đã chốt, không còn là đề xuất.
**Phạm vi:** đăng nhập, hai vai trò, quyền đối với nhiệm vụ / tồn kho / cấu hình / tài khoản, nhật ký thao tác.
**Không thay đổi:** state machine, BP-01/02/03/05/06, BP-04 (REVIEW), A3/A4, D1/D2, chính sách PER_AGV.

## 1. Vai trò

Hai vai trò: **Admin** và **Operator** (mỗi tài khoản đúng một vai trò). Xác thực thật bằng PostgreSQL; mật khẩu chỉ lưu dạng băm. Không có đăng nhập mô phỏng, kể cả Demo Mode.

## 2. Ma trận quyền (Backend là nơi quyết định; giao diện chỉ phản ánh lại)

| Chức năng | Admin | Operator |
|---|---|---|
| Xem Dashboard, AGV, nhiệm vụ, vị trí và tồn kho | Có | Có |
| Tạo nhiệm vụ | Có | Có |
| Sửa **toàn bộ** thông tin yêu cầu của nhiệm vụ (mục 3) | Khi nhiệm vụ còn chờ | Khi nhiệm vụ còn chờ |
| **Xóa cứng** nhiệm vụ (mục 4) | Khi nhiệm vụ còn chờ | Khi nhiệm vụ còn chờ |
| Hủy nhiệm vụ đã gửi RCS / AGV đã nhận, đối soát | Có | Không |
| Xem tồn kho | Có | Có |
| Khai báo / điều chỉnh tồn kho bằng các API hiện có (mục 5) | Có | **Có** |
| Cấu hình PLC/RCS, tham số vận hành, vị trí, AGV | Có | Không |
| Giả lập, kiểm thử, điều khiển Demo | Có | Không |
| Quản lý tài khoản, xem nhật ký thao tác | Có | Không |

## 3. Sửa nhiệm vụ (thay thế quy tắc cũ "chỉ sửa trạm", xem mục 8)

- Điều kiện (kiểm tra lại từ Database trong transaction): trạng thái chờ (Pending, Validating, Queued), AGV chưa nhận, chưa gửi/chấp nhận bởi RCS (không có mã RCS, chưa có lần gửi, chưa lấy/thả rack).
- Sửa được **tất cả thông tin do người dùng nhập khi tạo nhiệm vụ**: quy trình và trạm yêu cầu. AGV, loại rack, rack nguồn, vị trí đích là kết quả của quy tắc nghiệp vụ và được xác định lại theo quy trình mới (không cho nhập tay, để không vượt quy tắc đã duyệt).
- Mã nhiệm vụ, khóa yêu cầu, người tạo, mã RCS không đổi.
- Yêu cầu mới được kiểm tra như tạo mới (quy trình đã duyệt, BP-04 bị từ chối, trạm hợp lệ, AGV bật). Nhiệm vụ đang xếp hàng phải tìm lại được rack nguồn; nếu không, từ chối và giữ nguyên.
- Nhiệm vụ đã được AGV tiếp nhận hoặc đang thực hiện: Operator bị từ chối (403); Admin nhận lỗi nghiệp vụ (409) và dùng chức năng Hủy/Đối soát.

## 4. Xóa cứng nhiệm vụ (thay thế quy tắc cũ "xóa mềm sang Đã hủy")

- Cùng điều kiện như mục 3. Dòng nhiệm vụ, lịch sử trạng thái, giữ chỗ và sự kiện đã xử lý của nhiệm vụ bị **xóa hẳn**; đây không phải thao tác hủy.
- Kiểm tra trạng thái mới nhất trong transaction; xung đột với Scheduler/RCS được xử lý bằng khóa phiên bản dòng (xmin) và thử lại: nếu nhiệm vụ đã chuyển trạng thái thì từ chối xóa (403/409, yêu cầu tải lại).
- Nhật ký (audit) ghi **trong cùng transaction** với việc xóa và không có khóa ngoại tới nhiệm vụ nên tồn tại sau khi xóa: mã nhiệm vụ (NV-xxxxxxxx), Id, khóa yêu cầu, quy trình, trạng thái lúc xóa, trạm yêu cầu, người tạo, người xóa, vai trò, thời gian, hành động TASK_DELETE, kết quả. Lần xóa bị từ chối được ghi là Denied/Failed.

## 5. Tồn kho

- Operator được xem và dùng **các API tồn kho đã có**: khai báo tồn kho ban đầu và điều chỉnh số lượng theo vị trí/loại rack. Người thực hiện lấy từ phiên đăng nhập, mọi thay đổi được audit (INVENTORY_INITIAL_STOCK, INVENTORY_ADJUST, kể cả khi bị từ chối).
- Các kiểm tra nghiệp vụ hiện có vẫn áp dụng cho Operator: sức chứa, loại rack được phép ở vị trí, không xóa rack đang được giữ chỗ.
- **Chưa có / không tự suy diễn:** nhập kho, xuất kho, chuyển rack thủ công giữa vị trí, sửa Rack ID, bind rack. Mô hình Rack ID và số lượng rack vẫn NEEDS_CONFIRMATION. Giao diện hiện chỉ hiển thị tồn kho; chưa có màn hình sửa tồn kho (API đã sẵn sàng).
- Hủy và đối soát nhiệm vụ (kể cả việc xác nhận vị trí rack thực tế sau sự cố) vẫn chỉ Admin vì yêu cầu không giao quyền này cho Operator.

## 6. Tài khoản

- Admin: tạo, sửa họ tên, đổi vai trò, khóa/mở khóa, đặt lại mật khẩu, xóa khi đủ điều kiện.
- Không được xóa chính tài khoản đang đăng nhập (kể cả khi còn Admin khác).
- Không được xóa hoặc vô hiệu hóa/hạ quyền Admin hoạt động cuối cùng.
- Tài khoản đã có lịch sử (nhật ký, nhiệm vụ đã yêu cầu, thay đổi tồn kho) không được xóa cứng; phải vô hiệu hóa.
- Không tự đăng ký, không tự nâng quyền. Tài khoản bị khóa mất phiên ngay.

## 7. Giữ nguyên (chưa được phê duyệt thay đổi)

Chính sách mật khẩu (tối thiểu 10 ký tự, có chữ và số, không chứa tên đăng nhập), khóa sau 5 lần sai trong 5 phút, hết hạn phiên 30 phút không hoạt động / 12 giờ tuyệt đối là **mặc định kỹ thuật chưa được chủ dự án phê duyệt**; giữ nguyên cấu hình hiện tại. Tài khoản bootstrap admin/admin chỉ tạo khi chưa có Admin, bắt buộc đổi mật khẩu lần đầu, và không tạo ở Production nếu không cấu hình rõ.

## 8. Quy tắc cũ bị thay thế (SUPERSEDED)

| Quy tắc cũ (CR-001, bản đề xuất 2026-11-06) | Trạng thái | Thay bằng |
|---|---|---|
| "Sửa nhiệm vụ" chỉ đổi trạm yêu cầu | SUPERSEDED | Mục 3 |
| "Xóa" nhiệm vụ = xóa mềm, chuyển sang Đã hủy, giữ bản ghi | SUPERSEDED | Mục 4 |
| Operator chỉ xem tồn kho, mọi thay đổi tồn kho chỉ Admin | SUPERSEDED | Mục 5 |
| "Chưa được AGV tiếp nhận" = Pending/Validating/Queued | CONFIRMED, bổ sung điều kiện chưa gửi RCS | Mục 3 |
