# CR-069 – Mô phỏng vận chuyển AMR trực quan trên bản đồ

| | |
|---|---|
| **Trạng thái** | `APPROVED, IMPLEMENTED – đã MERGE vào main (bea0a35)` |
| **Nguồn phê duyệt** | Chủ dự án, 2026-11-28 ("Trực quan hóa mô phỏng vận chuyển AMR") |
| **Migration** | Không |

## Phạm vi

Nâng cấp chính AMR Simulator của CR-050; Pick, Drop, rời cổng vẫn báo qua các lối vào hiện có của WCS. Không thêm luồng nghiệp vụ.

- Có bản đồ: AMR chạy trên các kết nối `NeighbInfo` (đúng chiều, theo `CtrlPoint`), đường ngắn nhất. Vị trí → điểm: liên kết WCS (CR-066), nếu không có thì dòng Excel duy nhất có Call code đúng bằng mã vị trí. Không có điểm hoặc không có đường: lượt chạy lỗi `NO_MAP_POINT` / `NO_MAP_PATH`, không di chuyển.
- AMR mô phỏng không đứng trên điểm nào được đặt tại điểm đỗ (Parking, rồi Charging, rồi điểm khác).
- Rời trạm khi cách quá 0,7 m (trên bản đồ); không bản đồ giữ 1,5 m như cũ.
- Không có bản đồ: giữ hành vi cũ (tọa độ giả lập).
- Mới: Pause / Resume (`POST /api/simulator/amr/pause`); `GET /api/simulator/amr/live` (mọi người đăng nhập; rỗng khi chế độ Real).
- Giao diện: biểu tượng AMR và rack, hiệu ứng Pick / Drop, tuyến đã đi / chưa đi / dự kiến, bảng điều khiển và thông tin nhiệm vụ. Vị trí AMR mô phỏng đọc mỗi 0,4 giây.

## Không đổi

FIFO, chọn rack, hàng đợi, giữ chỗ, phân AMR, trạng thái lệnh, tồn kho; Fleet Polling và dữ liệu AMR thật; Real RCS (mô phỏng không bật được khi Real Dispatch bật); PLC.

## Giới hạn

- Trạng thái mô phỏng ở bộ nhớ: khởi động lại thì mất. Dừng mô phỏng không đổi lệnh đang dở.
- Hủy lệnh theo quy tắc hiện có: máy chủ từ chối khi không được phép.
- Không mô phỏng va chạm, tránh nhau, gia tốc, thời gian quay.

## Kiểm thử và rollback

`Cr069SimMapGraphTests`, `Cr050SimulatorTests`, build, nghiệm thu trình duyệt trên bản sao dữ liệu. Rollback: quay lại bản trước.
