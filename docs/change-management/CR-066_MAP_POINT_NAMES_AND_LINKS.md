# CR-066 – Đặt tên điểm bản đồ và liên kết với vị trí WCS

| | |
|---|---|
| **Trạng thái** | `APPROVED, IMPLEMENTED – nhánh feature/cr-066-map-points, CHƯA MERGE` |
| **Nguồn phê duyệt** | Chủ dự án, 2026-11-28 ("Map point naming & port linking") |
| **Migration** | `AddRcsMapPoints`: thêm bảng `rcs_map_points`; không đổi bảng hay dữ liệu có sẵn |

## Quy tắc

- Mọi người đăng nhập xem được; **Admin** đặt tên, liên kết, sửa, hủy (ghi Audit `RCS_MAP_POINT_SET`, `RCS_MAP_POINT_REMOVE`).
- Vị trí lấy từ danh sách vị trí WCS. Một vị trí gắn **tối đa một điểm** của bản đồ.
- Lưu theo Map ID + Point ID, kèm tọa độ lúc lưu. Tên và liên kết chỉ hiện khi điểm còn ở đúng tọa độ đó (sai số 0,001 đơn vị). Nạp bản đồ mới làm điểm mất hoặc đổi chỗ thì bản ghi được liệt kê để kiểm tra, không tự gắn sang điểm khác. Bản đồ khác Map ID không dùng bản ghi của Map ID cũ (nạp lại Map ID cũ thì chúng hiện lại).
- Lưu kèm mã băm file đang xem: file đã đổi thì từ chối `RCS_MAP_CHANGED`.
- Vị trí bị xóa khỏi WCS: liên kết giữ nguyên và được cảnh báo.

## Không đổi

XML gốc, tọa độ, hiển thị AMR, truy vấn RCS, điều phối, vị trí và tồn kho. Liên kết **chỉ dùng để hiển thị**: không dùng cho điều phối hay mã vị trí RCS.

## Kiểm thử và rollback

`Cr066MapPointTests`, `RcsMapParserTests`, build, trình duyệt trên bản sao dữ liệu. Không chạy toàn bộ test. Rollback: quay lại bản trước; `Down` xóa bảng `rcs_map_points` (mất tên và liên kết đã nhập).
