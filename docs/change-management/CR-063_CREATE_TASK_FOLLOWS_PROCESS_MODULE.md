# CR-063 – Danh sách "Tạo nhiệm vụ" theo module Quy trình và tuyến

| | |
|---|---|
| **Trạng thái** | `APPROVED, IMPLEMENTED – nhánh feature/cr-063-create-task-processes, CHƯA MERGE` |
| **Nguồn phê duyệt** | Chủ dự án, 2026-11-28 (yêu cầu sửa ngay, không chạy test, commit, push, restart, merge) |
| **Migration** | Không |

## Quy tắc

| Mục | Được đưa vào danh sách / được tạo |
|---|---|
| Quy trình cấu hình (transport order) | Đã công bố, đang bật, không REVIEW, đang nhận được yêu cầu |
| Luồng Phase 1 BP-0x (nhiệm vụ đơn) | Module có quy trình cùng mã đạt điều kiện trên; trạm = trạm cấu hình trùng trạm kích hoạt của quy trình |

- Form Quản lý nhiệm vụ và Vận hành RCS đọc lại danh sách mỗi 5 giây.
- `POST /api/tasks` và đổi luồng ở `PUT /api/tasks/{id}` từ chối `FLOW_NOT_AVAILABLE`. Gửi lại cùng RequestKey vẫn trả nhiệm vụ đã tạo.
- `GET /api/flows` thêm `available`.
- Cấu hình `ProcessFlows:RequireModuleProcess` (mặc định `true`); `false` = hành vi cũ.

## Không đổi

Yêu cầu do PLC gửi vào engine nhiệm vụ đơn; nhiệm vụ, lệnh và dữ liệu hiện có (sửa trạm của nhiệm vụ cũ vẫn được qua API); luật nghiệp vụ; database.

## Ảnh hưởng và rủi ro

- Dữ liệu ngày 2026-11-28 không có quy trình BP nào trong module: BP-0x không còn tạo tay được cho đến khi nạp, công bố (và Ready nếu cần) quy trình đó.
- Máy triển khai khác đang tạo tay BP-0x mà không có quy trình trong module sẽ bị từ chối: công bố quy trình, hoặc đặt cờ cấu hình `false`.
- **Chưa chạy test** (theo yêu cầu). Test API nhiệm vụ đơn đặt cờ `false`; một kiểm tra trong `ops-smoke` được sửa theo quy tắc mới. Chưa có test mới cho quy tắc này.

## Rollback

Đặt cờ `false` (backend) hoặc quay lại bản trước.
