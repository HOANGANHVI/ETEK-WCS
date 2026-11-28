# CR-064 – Bỏ bước "Đặt Ready": công bố là dùng được

| | |
|---|---|
| **Trạng thái** | `APPROVED, IMPLEMENTED trên main` |
| **Nguồn phê duyệt** | Chủ dự án, 2026-11-28 ("Simplify workflow lifecycle – remove Ready step"; không chạy test) |
| **Thay thế** | Phần "Admin đặt Ready" của CR-035 (D-11), CR-039, CR-040 (D-12) |
| **Migration** | Không; không sửa dữ liệu |

## Quy tắc

- Công bố thành công → phiên bản ở trạng thái Ready.
- Phiên bản đã công bố trước đây đang NotReady được coi là Ready; không cần công bố lại; dữ liệu không bị sửa.
- Phiên bản đã bị **tạm đình chỉ** trước đây vẫn bị giữ (đó là lệnh dừng có chủ ý). Không còn thao tác đình chỉ hay đặt Ready; dùng lại bằng cách công bố phiên bản mới.
- Bỏ nút "Đặt Ready", "Tạm đình chỉ" và API `GET/PUT /api/process-versions/{id}/readiness`.

## Không đổi

Điều kiện thiết bị vẫn được kiểm tra khi nhận yêu cầu và trong hàng đợi (PLC đang bật, cổng đang bật, mapping IO đã duyệt, trạm cần PLC có cổng). Công bố vẫn phải qua Kiểm tra; REVIEW và quy trình không được duyệt vẫn bị chặn. Phiên bản đã công bố không sửa được. FIFO, phạm vi hàng đợi, chọn rack, bước thực thi, lệnh đang chạy không đổi.

## Rủi ro

- Công bố (kể cả khi nạp cấu hình hoặc danh mục mặc định) cho phép nhận yêu cầu ngay; không còn bước xác nhận riêng của Admin về commissioning, an toàn, RCS. Muốn dừng: tắt phiên bản hoặc quy trình.
- Các phương thức dịch vụ đặt readiness còn trong mã (không có lối gọi) để không phải sửa lại bộ test.
- **Chưa chạy test.** Test cũ mong đợi NotReady sau công bố hoặc gọi API readiness sẽ không còn đúng và cần cập nhật.

## Rollback

Quay lại bản trước. Phiên bản công bố sau thay đổi giữ trạng thái Ready.
