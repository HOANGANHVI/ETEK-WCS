# CR-060 – Cho xóa BP-04; hướng dẫn gỡ REVIEW của PH2-04

| | |
|---|---|
| **Trạng thái** | `APPROVED, IMPLEMENTED – nhánh feature/cr-060-bp04-removal-ph204-hint, CHƯA MERGE` |
| **Nguồn phê duyệt** | Chủ dự án, 2026-11-28: "Sửa tiếp 2 lỗi này" (thay quyết định "trừ BP-04" của CR-056) |
| **Migration** | Không |

## BP-04

Bỏ cờ bảo vệ trong danh mục tham chiếu: BP-04 xóa được theo điều kiện chung (tắt nếu đang công bố, không còn lệnh mở, không giữ cổng; gõ mã và lý do).

**Không đổi:** BP-04 vẫn `REVIEW`, không triển khai, không công bố, luồng nhiệm vụ đơn vẫn từ chối BP-04. Nhập lại quy trình tham chiếu tạo BP-04 ở dạng nháp bị khóa REVIEW.

## PH2-04

Danh mục đã duyệt PH2-04 (D-09). Database còn cờ REVIEW từ lần nhập cũ. Dùng chức năng có sẵn (CR-041) **Tạo bản nháp theo danh mục**, rồi Kiểm tra, Công bố, Đặt Ready. Chỉ bổ sung hướng dẫn vào thông báo lỗi; không tự sửa dữ liệu, không nới kiểm tra công bố.

## Rủi ro và rollback

Xóa BP-04 không hoàn tác được trong ứng dụng. Rollback: quay lại bản trước.

## Kiểm thử

Chỉ test liên quan theo yêu cầu của chủ dự án (xóa quy trình, danh mục, i18n, ops-smoke, build); **không chạy toàn bộ test backend**.
