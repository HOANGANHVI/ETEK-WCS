# CR-068 – Hiển thị điểm, trạng thái ghép và liên kết trên Monitor AMR

| | |
|---|---|
| **Trạng thái** | `APPROVED, IMPLEMENTED – đã MERGE vào main (f3db996)` |
| **Nguồn phê duyệt** | Chủ dự án, 2026-11-28 (bổ sung CR-067: hiển thị bản đồ và thông tin điểm) |
| **Migration** | Không |

## Thay đổi

- Hiển thị mọi Call code, gồm điểm xoay (`A1_1`...); nhãn của điểm làm việc và điểm xoay nằm hai phía.
- Điểm không có dữ liệu Excel hiển thị rỗng viền đứt, ghi `#Point ID`; Duplicate viền đỏ.
- Bấm điểm: XML Point ID, trạng thái ghép (Matched / Unmatched / Duplicate), dữ liệu Excel, liên kết WCS và đường dẫn tới vị trí trong "Vị trí và tồn kho".
- Cảnh báo khi hai file không khớp hoàn toàn (trên bản đồ và khi nạp); khi nạp phải đánh dấu xác nhận mới lưu được.
- Bản đồ đọc lại mỗi 20 giây và khi cửa sổ được chọn lại.
- AMR được vẽ khi `mapCode` là Map ID, **MapQRCode** hoặc mã Admin khai báo. MapQRCode được thêm vì tài liệu RCS (5.2.11) cho `mapCode` là mã ngắn nằm trong Map Data ID (`AA` trong `011724AA012414`). Thay quy tắc CR-065. Việc quy đổi tọa độ vẫn cần Admin xác nhận.

## Không đổi

Truy vấn RCS (`mapCode` gửi đi theo cấu hình hiện có), Fleet Polling, điều phối, dữ liệu vận hành, PLC/RCS/AMR.

## Kiểm thử và rollback

`RcsMapDataMatcherTests`, build, trình duyệt trên bản sao dữ liệu với bộ file thật và cặp file lệch phiên bản; tọa độ AMR thử chỉ đưa vào trình duyệt. **Chưa kiểm tra với AMR thật.** Rollback: quay lại bản trước.
