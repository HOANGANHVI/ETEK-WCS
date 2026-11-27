# CR-055 – Xóa và ngừng sử dụng quy trình nghiệp vụ

| | |
|---|---|
| **Trạng thái** | `ĐÃ TRIỂN KHAI – nhánh fix/cr-053-dashboard-counts, CHƯA MERGE` |
| **Nguồn phê duyệt** | Chủ dự án, 2026-11-27 ("đồng ý theo khuyến nghị") |
| **Migration** | Không |

## Quy tắc được duyệt

| Tình trạng quy trình | Kết quả khi xóa |
|---|---|
| Quy trình tham chiếu BP-01…BP-06 | Không xóa (`PROCESS_PROTECTED`); vẫn tắt được bằng công tắc |
| Còn lệnh vận chuyển chưa kết thúc | Từ chối (`PROCESS_HAS_OPEN_ORDERS`) |
| Đã có lệnh, mọi lệnh đã kết thúc, đang bật | Không xóa: tắt và ẩn khỏi danh sách mặc định; lịch sử giữ nguyên |
| Đã có lệnh, đã tắt | Không làm gì (`PROCESS_ALREADY_DISABLED`) |
| Chưa từng có lệnh, đã công bố và đang bật | Phải tắt trước (`PROCESS_DISABLE_FIRST`) |
| Chưa từng có lệnh, còn lại | Xóa hẳn quy trình, phiên bản, bước, tuyến |

Phiên bản: chỉ xóa bản `Draft` / `Validated`, không lệnh nào dùng, và không phải phiên bản duy nhất. Bước, nguồn, đích, điều kiện trong bản nháp xóa được như trước.

Mọi thao tác: chỉ Admin, hộp xác nhận, bắt buộc lý do (tối đa 500 ký tự), ghi Audit Log (`PROCESS_REMOVE`, `PROCESS_VERSION_DELETE`).

## Thay đổi

- API: `GET /api/processes/{id}/removal`, `DELETE /api/processes/{id}?reason=`, `DELETE /api/process-versions/{id}?reason=`.
- Dữ liệu: bộ quy trình tham chiếu đánh dấu `protected` cho BP-01…06 (cấu hình, không viết mã quy trình trong code).
- Giao diện trang Quy trình: mục "Xóa quy trình", nút xóa phiên bản, bộ lọc "Hiện quy trình đã tắt" (mặc định ẩn).

Không đổi: lịch sử lệnh, tồn kho, quy tắc chạy quy trình, PLC, RCS.

## Rủi ro và rollback

Xóa hẳn không hoàn tác được trong ứng dụng; khôi phục từ bản sao lưu database. Quy trình giao kèm đã xóa (PH2) được tạo lại ở dạng bản nháp khi bấm "Nhập quy trình tham chiếu". Rollback mã: hoàn tác thay đổi; không đổi schema.

## Kiểm thử

`Cr055ProcessRemovalTests`, `ops-smoke` (CR-055), kiểm tra trình duyệt trên bản sao dữ liệu: [mục xóa](../reports/cr-055/1-delete-draft-process.png) · [xóa phiên bản](../reports/cr-055/2-delete-version.png).
