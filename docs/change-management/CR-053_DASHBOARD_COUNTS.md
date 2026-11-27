# CR-053 – Tổng quan đếm cả lệnh điều phối đa chặng

| | |
|---|---|
| **Trạng thái** | `ĐÃ TRIỂN KHAI – nhánh fix/cr-053-dashboard-counts, CHƯA MERGE` |
| **Nguồn phê duyệt** | Chủ dự án, 2026-11-27 ("số nhiệm vụ hoàn thành không đúng, hãy kiểm tra và sửa") |
| **Nền** | `main` `925245a` |
| **Migration** | Không; chỉ đổi giao diện |

## Nguyên nhân

Trang Tổng quan chỉ đọc nhiệm vụ đơn (`transport_tasks`). Lệnh đa chặng (`transport_orders`) không được đếm.

## Thay đổi

- Số hoàn thành, thất bại, tỷ lệ lỗi, xu hướng, thời gian chu kỳ và số theo AGV gồm cả nhiệm vụ đơn và lệnh đa chặng. Mỗi lệnh đếm một lần theo thời điểm kết thúc; tính cho AGV / robot của chặng cuối.
- "Đang thực hiện": nhiệm vụ đang chiếm AGV, lệnh `Accepted` / `InProgress`, và lệnh chờ đích khi AMR đang giữ rack.
- AGV đang làm việc, thẻ AGV, danh sách đang thực hiện và nhật ký hoạt động gồm cả lệnh đa chặng.
- Không đổi quy tắc có sẵn: lệnh hủy, đang chờ hoặc cần đối soát không tính vào số đã kết thúc.

## Giới hạn

Đọc tối đa 2000 bản ghi mỗi loại và trạng thái; lệnh tạo trước kỳ xem hơn 30 ngày có thể không được đọc.

## Kiểm thử

`dashboard-smoke` (ca CR-053), đối chiếu số trên trang với database trên bản sao dữ liệu.

## Rollback

Hoàn tác thay đổi giao diện.
