# CR-057 – Xóa vị trí WCS, giữ mã cho lịch sử

| | |
|---|---|
| **Trạng thái** | `APPROVED (cách A), IMPLEMENTED – nhánh feature/cr-056-delete-process-keep-history, CHƯA MERGE`; migration đã áp lên wcs_db ngày 2026-11-27 |
| **Nguồn phê duyệt** | Chủ dự án, 2026-11-27: "hãy bổ xung" (chức năng xóa), "xóa cả vị trí đã có lịch sử", chọn "A" |
| **Migration** | `AddLocationRemovedAt` |

## Hành vi

Cấu hình → Vận hành → Vị trí: nút **Xóa** (Admin; ẩn ở Demo Mode). Cần gõ lại mã vị trí và lý do; ghi Audit `CONFIG_LOCATION_DELETE`.

Xóa = **đánh dấu** (`RemovedAt`, đồng thời tắt "Đang sử dụng"), không xóa dòng dữ liệu:

- Không còn trong danh sách vị trí, tồn kho, trình soạn quy trình, xuất cấu hình; không nhận yêu cầu mới.
- Lịch sử (giao dịch tồn kho, giữ chỗ, nhiệm vụ, lệnh, chặng, giao dịch cổng) giữ nguyên và vẫn hiển thị mã.
- Mã được dùng lại cho vị trí mới. Bản ghi cũ lưu theo id vẫn trỏ vị trí cũ; bản ghi lưu theo mã (lệnh, chặng, giao dịch cổng) chỉ phân biệt bằng thời gian.
- Danh mục vị trí mặc định không tạo lại vị trí đã xóa khi khởi động lại.
- Không có chức năng khôi phục.

## Từ chối khi còn phụ thuộc

| Mã | Điều kiện |
|---|---|
| `LOCATION_DELETE_PROTECTED` | Trạm được bảo vệ (A3, A4, D1, D2 theo cấu hình) |
| `LOCATION_DELETE_CONFIGURED` | Cấu hình triển khai (luồng nhiệm vụ đơn, mã vị trí PLC) đang nêu mã |
| `LOCATION_DELETE_RACKS` | Còn rack tại vị trí |
| `LOCATION_DELETE_PORT` | Còn liên kết với cổng PLC |
| `LOCATION_DELETE_PROCESS` | Một phiên bản quy trình bất kỳ (kể cả nháp) đang dùng |
| `LOCATION_DELETE_OPEN_WORK` | Còn giữ chỗ, nhiệm vụ, lệnh, chặng, giao dịch cổng chưa kết thúc, hoặc đang chờ đối soát |

Hộp thoại liệt kê mọi lý do trước khi cho xác nhận.

## Tác động

- **Database:** thêm cột `locations.RemovedAt` (cho phép rỗng; mọi dòng hiện có để rỗng); chỉ mục duy nhất trên `Code` chỉ áp cho vị trí chưa xóa.
- **API (bổ sung):** `GET /api/config/locations/{code}/removal`, `DELETE /api/config/locations/{code}?reason=&confirmCode=`, `GET /api/locations/removed`.
- Không đổi PLC, RCS, luồng nghiệp vụ, quy tắc tồn kho.

## Rollback

Khi chưa xóa vị trí nào: chạy migration `Down`. Sau khi đã xóa: `Down` từ chối (`ROLLBACK_REFUSED`), vì bỏ cột sẽ làm vị trí đã xóa hiện lại; khôi phục từ bản sao lưu. Backend cũ trên database đã migrate vẫn chạy, nhưng hiển thị lại các vị trí đã xóa (ở trạng thái tắt).

## Kiểm thử

`Cr057LocationRemovalTests`, `ops-smoke`, toàn bộ test backend (1455/1456; `A_large_export_streams_in_flat_memory…` lỗi khi máy tải nặng, chạy lại riêng đạt), kiểm tra migration/rollback và trình duyệt trên bản sao dữ liệu: [từ chối](../reports/cr-057/1-refused.png), [xác nhận](../reports/cr-057/2-confirm.png), [lịch sử](../reports/cr-057/3-history.png).
