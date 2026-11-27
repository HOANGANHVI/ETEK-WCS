# CR-056 – Xóa hẳn quy trình đã có lệnh, giữ nguyên lịch sử

| | |
|---|---|
| **Trạng thái** | `APPROVED, IMPLEMENTED – đã MERGE vào main (f27fa1b)`; migration đã áp lên wcs_db ngày 2026-11-27 |
| **Nguồn phê duyệt** | Chủ dự án, 2026-11-27: "A, cho xóa BP trừ BP-04, đồng ý migration" |
| **Migration** | `DropOrderProcessForeignKeys` |

## Quy tắc được duyệt (thay một phần CR-055)

| Tình trạng quy trình | Kết quả khi xóa |
|---|---|
| BP-04 (`REVIEW`) | Không xóa; BP-01, 02, 03, 05, 06 **không còn được bảo vệ** |
| Còn lệnh chưa kết thúc | Từ chối |
| Lệnh của quy trình còn giữ cổng trạm | Từ chối, nêu tên cổng |
| Đã công bố và đang bật | Phải tắt trước |
| Còn lại, **kể cả đã có lệnh** | Xóa quy trình, phiên bản, bước, tuyến. Lệnh, chặng, sự kiện, giao dịch cổng, giao dịch tồn kho, bản tin RCS, Audit **giữ nguyên** |

Bỏ trạng thái "tắt và ẩn thay cho xóa" của CR-055. Xóa cần: Admin, gõ lại mã quy trình, lý do; ghi Audit `PROCESS_REMOVE`. Mỗi lệnh đã lưu sẵn mã và định nghĩa quy trình nên vẫn đọc được.

## Migration và tác động

- Chỉ bỏ hai khóa ngoại `transport_orders` → `business_processes`, `business_process_versions`. Không đổi dữ liệu, cột hay index.
- Bản backend cũ vẫn chạy được trên database đã migrate.
- **Rollback:** được khi chưa xóa quy trình nào có lệnh. Sau đó migration `Down` từ chối (`ROLLBACK_REFUSED`); khôi phục từ bản sao lưu.
- Hệ thống không còn chặn ở mức database việc lệnh trỏ tới quy trình không tồn tại; ứng dụng chỉ cho xóa khi mọi lệnh đã kết thúc và quy trình không còn nhận yêu cầu.

## Rủi ro

Xóa không hoàn tác được trong ứng dụng; sao lưu trước khi xóa. "Nhập quy trình tham chiếu" tạo lại BP đã xóa ở dạng bản nháp mới; lệnh cũ không gắn với bản mới.

## Kiểm thử

`Cr055ProcessRemovalTests`, `ops-smoke`, toàn bộ test backend (1453/1453), kiểm tra migration và trình duyệt trên bản sao dữ liệu: [hộp xác nhận](../reports/cr-056/1-delete-with-history.png).
