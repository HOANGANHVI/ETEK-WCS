# CR-061 – Sơ đồ vị trí dạng mặt bằng

| | |
|---|---|
| **Trạng thái** | `APPROVED, IMPLEMENTED – nhánh feature/cr-061-inventory-floor-plan, CHƯA MERGE` |
| **Nguồn phê duyệt** | Chủ dự án, 2026-11-28 (yêu cầu thiết kế lại sơ đồ vị trí, cho phép commit, push, merge, restart) |
| **Migration** | Không |

## Bố trí

| Hàng | Trái | Giữa | Phải |
|---|---|---|---|
| Trên | A1–A4 (Máy cấp CNC) | B1, B2, D1, D2 (Máy QC) | E1, E2, G1, G2 (Máy QC) |
| Giữa | Đường đi minh họa (không phải vị trí) | | |
| Dưới | X1–X4 (Máy cấp CNC) | P1–P4 (Buffer) | M5, M6 (QC thủ công) |

- Mỗi vị trí có ô cố định; vị trí không tồn tại thì ô để trống.
- Vị trí ngoài sơ đồ (hiện có F1, F2) vẫn hiển thị bên dưới theo nhóm như trước.
- Màn hình hẹp hơn 1600 px: bảng chi tiết xuống dưới sơ đồ; hẹp hơn nữa thì sơ đồ cuộn ngang.

## Không đổi

Thẻ vị trí, biểu tượng và màu trạng thái, số lượng, chọn vị trí, cập nhật realtime, API, database, nghiệp vụ. Không đổi màn hình khác. Chỉ frontend; bố trí theo mã vị trí chỉ phục vụ hiển thị (`lib/floorPlan.ts`).

## Lưu ý

Hình tham chiếu không đính kèm trong yêu cầu; bố trí làm theo mô tả bằng chữ.

## Kiểm thử và rollback

TypeScript, build, kiểm tra trình duyệt trên bản sao dữ liệu. Rollback: quay lại bản trước.
