# CR-065 – Nạp bản đồ RCS và theo dõi AMR trên bản đồ

| | |
|---|---|
| **Trạng thái** | `APPROVED, IMPLEMENTED – nhánh feature/cr-065-rcs-map-monitor, CHƯA MERGE; migration đã áp lên wcs_db ngày 2026-11-28` |
| **Nguồn phê duyệt** | Chủ dự án, 2026-11-28 ("Import RCS map & live AMR map monitor") |
| **Migration** | `AddRcsMap`: thêm bảng `rcs_maps`; không đổi bảng hay dữ liệu có sẵn |

## Phạm vi

- **Cấu hình hệ thống → Bản đồ RCS** (Admin): nạp / thay file XML `MapCfg`, xem thông tin, cấu hình quy đổi tọa độ.
- **Giám sát AGV → Monitor AMR**: bản đồ 2D (điểm, đường nối theo `NeighbInfo`, đường cong theo `CtrlPoint`), phóng to, kéo, vừa khung; AMR từ dữ liệu đội xe hiện có.
- API: `GET /api/rcs-map`; `POST /api/config/rcs-map`, `PUT /api/config/rcs-map/settings` (Admin, ghi Audit).

Chỉ đọc: không gọi thêm API RCS, không gửi lệnh, không đổi điều phối, FleetProvider và bảng đội xe.

## Quy đổi tọa độ

| Nội dung | Nguồn |
|---|---|
| `posX`, `posY` là mm; `robotDir` trong [-180, 360] độ | RCS-2000 API Developer Guide V3.3, 5.2.14 |
| Đơn vị `xpos`/`ypos`, chiều trục Y, gốc và chiều của `robotDir` | **Không có trong tài liệu hay XML** |

Mặc định (1000 mm mỗi đơn vị, Y hướng lên, 0° sang phải, ngược chiều kim đồng hồ) là **giả định**; Admin chỉnh được. Giao diện cảnh báo cho đến khi Admin xác nhận đã kiểm tra với AMR thật. Nạp file mới thì phải xác nhận lại.

## Khớp bản đồ

AMR được vẽ khi `mapCode` bằng Map ID hoặc một mã Admin khai báo. Khác mã, thiếu tọa độ hoặc ngoài phạm vi: liệt kê, không vẽ. Không tự suy ra điểm hiện tại (RCS không trả).

## Rủi ro và rollback

Một bản đồ tại một thời điểm. Rollback: quay lại bản trước; migration `Down` xóa bảng `rcs_maps` (chỉ mất bản đồ đã nạp).

## Kiểm thử

`RcsMapParserTests`, build, trình duyệt trên bản sao dữ liệu với file mẫu và dữ liệu đội xe giả lập trong trình duyệt. Không chạy toàn bộ test.
