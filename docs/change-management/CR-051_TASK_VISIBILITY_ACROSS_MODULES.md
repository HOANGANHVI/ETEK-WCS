# CR-051 – Công việc vận chuyển hiển thị liên thông giữa Điều phối đa chặng, Quản lý nhiệm vụ và Giám sát AGV

| | |
|---|---|
| **Trạng thái** | `ĐÃ TRIỂN KHAI, ĐÃ KIỂM THỬ TRÊN SIMULATOR – NHÁNH feature/cr-050-simulator, CHƯA MERGE, CHƯA DEPLOY` |
| **Nguồn phê duyệt** | Chỉ thị của chủ dự án, 2026-11-27 |
| **Nền** | `feature/cr-050-simulator` (`321f884`) |
| **Migration** | Không |

## Nguyên nhân

Hai engine lưu ở hai nơi: nhiệm vụ đơn (`transport_tasks`) và lệnh đa chặng (`transport_orders`, `transport_legs`). Danh sách của **Quản lý nhiệm vụ** và **Nhiệm vụ gần đây của AGV** chỉ đọc `transport_tasks`.

## Thay đổi

- **Read model chỉ đọc** `GET /api/routing/rcs-work` (`IRcsWorkQuery`): nhiệm vụ và chặng, kèm RCS Task ID, mã yêu cầu (tương quan WCS → RCS → AMR), AMR, trạng thái WCS, thời gian. Không sao chép dữ liệu, không tạo Task.
- **Trạng thái với RCS** (suy ra, không lưu): `NotSent`, `NoAnswer`, `Rejected`, `Accepted`, `Executing`, `Interrupted`, `Completed`, `Failed`, `Cancelled`, từ trạng thái task/chặng, mã nhiệm vụ RCS, xác nhận Pick và nhật ký bản tin RCS.
- AMR Simulator ghi thêm sự kiện `Started` và `Recovered`.
- `TaskDto.requestedBy` (bổ sung).
- **Quản lý nhiệm vụ:** một danh sách gồm nhiệm vụ đơn và lệnh đa chặng; lọc theo loại, trạng thái, AGV, mã; chi tiết lệnh dùng chung khung của Điều phối đa chặng, thêm mục gửi và thực thi qua RCS.
- **Giám sát AGV:** "Nhiệm vụ gần đây" gồm cả chặng đã gửi RCS, ở cả chế độ Legacy và RcsManaged/Real; thẻ AGV hiện chặng đang chạy; công việc chưa gửi được đếm riêng.

Không đổi: vòng đời lệnh, chặng, task; tồn kho; giữ chỗ; PER_AGV; quyền ghi PLC; Real Dispatch.

## Giới hạn

- Chế độ Real: "AMR/RCS báo lỗi" chỉ có từ sự kiện của Simulator; RCS thật chưa có callback lỗi chặng được xác nhận. "Đang thực thi" của chặng thật được thấy từ khi có xác nhận Pick.
- Danh sách lấy tối đa 200 bản ghi mới nhất mỗi loại ở Quản lý nhiệm vụ, 500 ở read model.
- Tổng quan (dashboard) và Vận hành RCS chưa đổi.

## Kiểm thử

`Cr051RcsWorkTests`, các kiểm tra CR-051 trong `ops-smoke`, kiểm thử đầu cuối 25/25 trên môi trường QA cách ly. Ảnh: [AGV đang thực thi](../reports/cr-051/1-agv-executing.png) · [chi tiết lệnh](../reports/cr-051/2-task-management-order.png) · [AMR báo lỗi](../reports/cr-051/3-agv-interrupted.png) · [danh sách nhiệm vụ](../reports/cr-051/4-task-management-list.png).

## Rollback

Hoàn tác thay đổi; không đổi schema hay dữ liệu.
