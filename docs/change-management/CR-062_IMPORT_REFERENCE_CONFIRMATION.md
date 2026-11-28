# CR-062 – Xác nhận trước khi nạp quy trình tham chiếu

| | |
|---|---|
| **Trạng thái** | `APPROVED, IMPLEMENTED – nhánh feature/cr-062-import-confirmation, CHƯA MERGE` |
| **Nguồn phê duyệt** | Chủ dự án, 2026-11-28: "tôi đồng ý triển khai" |
| **Migration** | Không |

Nút **Nạp quy trình tham chiếu** trước đây chạy ngay. Nay mở hộp xác nhận gồm: quy trình sẽ được thêm (kể cả đã xóa trước đây), cờ REVIEW, vị trí không còn tồn tại; quy trình đã có được giữ nguyên. Không có gì để thêm thì nút Nạp bị khóa.

Cách nạp không đổi: chỉ thêm mã chưa có, tạo bản nháp, không ghi đè, không công bố. API bổ sung chỉ đọc: `GET /api/processes/import-reference`.

## Kiểm thử và rollback

`Cr055ProcessRemovalTests`, `ops-smoke`, build. Rollback: quay lại bản trước.
