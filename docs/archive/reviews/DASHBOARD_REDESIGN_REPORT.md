# Dashboard Overview – thiết kế lại

**Ngày:** 2026-11-17 · **Branch:** `develop` · nền `191518b`. Chỉ frontend; không đổi API, DB, migration hay nghiệp vụ.

## 1. Bảng đối chiếu metric ↔ dữ liệu

| Metric | Nguồn hiện có | Cách tính | Hiển thị được? | Dữ liệu còn thiếu |
|---|---|---|---|---|
| Tổng AGV / đang hoạt động / đang chờ | `GET /api/agvs` (`busy`, `connectionStatus`) | như dashboard cũ (đang chờ = Online và không busy) | Có | — |
| Nhiệm vụ đang thực hiện | `GET /api/tasks` (200 mới nhất), trạng thái chiếm AGV | như cũ | Có | — |
| Số rack | `GET /api/inventory/racks` | như cũ | Có | — |
| Hoàn thành trong kỳ | `GET /api/tasks?state=Completed` phân trang (200/trang, mới tạo trước) | đếm task `Completed` có `finishedAt` ≥ đầu kỳ | Có | — |
| Thất bại trong kỳ | như trên, `state=Failed` | đếm theo `finishedAt` | Có | — |
| Tỷ lệ lỗi | hai số trên | Failed / (Completed + Failed) × 100; chỉ task đã kết thúc trong kỳ; Cancelled, chờ, đang chạy không tính. Không có task nào → "Chưa có dữ liệu", không hiện 0% | Có | — |
| Xu hướng hoàn thành | như trên | nhóm theo giờ (Hôm nay) hoặc theo ngày (7/30 ngày), giờ địa phương trình duyệt, theo `finishedAt` | Có | — |
| Thời gian chu kỳ TB | `createdAt`, `finishedAt` của task `Completed` | trung bình `finishedAt − createdAt` (từ lúc tạo đến hoàn thành, gồm thời gian chờ); bỏ task thiếu/ngược timestamp; không có ngưỡng tốt/xấu | Có | — |
| Nhiệm vụ hoàn thành theo AGV | như trên, `agvId` | đếm | Có (số lượng, không phải %) | — |
| **Mức sử dụng AGV (hoạt động / chờ / không khả dụng)** | — | — | **Chưa khả dụng** | Bảng lịch sử trạng thái AGV (AGV, trạng thái, thời điểm bắt đầu/kết thúc). Trạng thái hiện tại không được dùng để suy diễn lịch sử |
| Trạng thái PLC / RCS / thời gian thực | `GET /api/system/health`, `agvs`, kênh realtime | dùng lại đúng logic thanh header (`components/linkStatus.ts`); Simulator hiện "Chưa kết nối" + nhãn "Chế độ mô phỏng" | Có | RCS vẫn suy ra từ trạng thái AGV (chưa có health RCS riêng) |

Giới hạn đọc dữ liệu: tối đa 2000 task mỗi loại trong kỳ (10 trang); chạm giới hạn thì hiện cảnh báo "số liệu có thể thiếu". Phân trang dừng khi một trang không đầy hoặc task cũ nhất được tạo trước đầu kỳ hơn 1 ngày. API không có bộ lọc theo ngày; nếu cần số liệu chính xác cho dữ liệu rất lớn, nên bổ sung endpoint thống kê phía backend (đề xuất, chưa làm).

## 2. Đã triển khai
- 8 thẻ KPI (Total/Active/Waiting AGV, Tasks in progress, Completed, Failure rate, Avg cycle time, Racks); chọn kỳ **Hôm nay / 7 ngày / 30 ngày** dùng chung (nhớ trong `localStorage`).
- Biểu đồ cột Task Completion Trend (hoàn thành + thất bại), donut Success/Failure, đường Average Cycle Time (khoảng trống khi không có task, không nội suy), thanh "hoàn thành theo AGV", ô "chưa khả dụng" cho AGV utilization, panel Live Operation Status (PLC/RCS/realtime, thẻ AGV gồm DispatchHold, danh sách task đang chạy), Recent Activity, Inventory overview.
- Biểu đồ SVG tự viết (`components/charts.tsx`), không thêm thư viện: dự án chưa có thư viện biểu đồ, cần 3 loại biểu đồ đơn giản, tránh thêm dependency. Tooltip (`<title>`), chú giải, mô tả cho trình đọc màn hình; màu theo token hiện có.
- Công thức nằm ở `lib/dashboardStats.ts` (hàm thuần) có kiểm thử `npm run dashboard:test` (18 phép).

## 3. Kiểm tra
- ESLint, `tsc -b`, `npm run build`, `i18n:check`, `i18n:test` (76), `dashboard:test` (18): đạt.
- Trình duyệt (Edge headless, stack riêng với database thử đã xoá): 27 phép đạt ở EN/VI × 1440/760px — 8 KPI, giá trị khớp dữ liệu (8 hoàn thành, tỷ lệ lỗi 27,3% = 3/11, chu kỳ TB 15,5 phút đúng trung bình dữ liệu thử), biểu đồ hiển thị, không tràn ngang, chuyển Hôm nay/7/30 ngày.
- Ảnh chụp: `docs/archive/reviews/dashboard/`. Dữ liệu trong ảnh là dữ liệu thử trong database tạm (không phải số liệu vận hành thật; mốc thời gian được dời để có nhiều ngày).

## 4. Chưa làm / lưu ý
- AGV utilization chưa có dữ liệu (xem bảng).
- Trình duyệt khác Edge chưa kiểm tra; chưa đo hiệu năng với hàng nghìn task.
