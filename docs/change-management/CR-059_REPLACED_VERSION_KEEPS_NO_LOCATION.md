# CR-059 – Phiên bản quy trình đã thay thế không còn giữ vị trí

| | |
|---|---|
| **Trạng thái** | `APPROVED, IMPLEMENTED – đã MERGE vào main (cb9cb0d)` |
| **Nguồn phê duyệt** | Chủ dự án, 2026-11-28: đã bỏ Z2 khỏi quy trình nhưng không xóa được, yêu cầu "sửa các lỗi này" |
| **Migration** | Không |

## Vấn đề

CR-057 tính **mọi** phiên bản, gồm cả phiên bản đã tắt. Phiên bản đã công bố hoặc đã tắt không sửa và không xóa được, nên bỏ vị trí ở phiên bản mới vẫn không xóa được vị trí; chỉ còn cách xóa cả quy trình.

## Quy tắc mới

| Phiên bản | Giữ vị trí |
|---|---|
| Nháp, đã kiểm tra, đã công bố | Có |
| Đã tắt, còn lệnh chưa kết thúc đi theo | Có |
| Đã tắt, không còn lệnh chưa kết thúc | **Không** |

Trạm kích hoạt của quy trình vẫn giữ vị trí. Hộp thoại nêu cả số phiên bản (ví dụ `PH2-11 v2`) và cách gỡ. Các điều kiện chặn khác không đổi.

## Rủi ro

Phiên bản đã tắt vẫn xem được và vẫn nêu mã vị trí đã xóa. Tạo phiên bản mới từ nó thì phải sửa mã đó trước khi kiểm tra đạt. Gói xuất cấu hình có phiên bản đã tắt có thể nêu vị trí không còn trong gói.

## Rollback

Quay lại bản trước (không có migration).

## Kiểm thử

`Cr057LocationRemovalTests` (thêm trường hợp CR-059), `ops-smoke`, toàn bộ test backend (1458/1458).
