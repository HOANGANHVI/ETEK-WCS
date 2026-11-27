# CR-047 – Hotfix: IO Map hiển thị đúng workbook; xóa Rack

| | |
|---|---|
| **Trạng thái** | `IMPLEMENTED – CHƯA COMMIT, CHƯA DEPLOY` |
| **Nguồn phê duyệt** | Chủ dự án, 2026-11-26 (hotfix) |
| **Nền** | `main` `b07a6d9` |
| **Migration** | Không (database chưa nâng cấp cần các migration đã có từ trước) |
| **Version** | `2026.11.26.3` |

## A. IO Map

### Nguyên nhân

1. Backend đang chạy trên máy phát triển là bản `2026.11.23.1` (commit `581fbad`), khởi động trước khi catalog được thay: API trả 368 tín hiệu v2, trạm X6, G3, G7.
2. Giao diện của CR-046 không dựng theo cấu trúc workbook: thiếu Nguồn, Đích, Nội dung, Ý nghĩa thành cột riêng; tách HeartBit và dự phòng; viết lại mô tả; thiếu sheet trình tự; mô phỏng còn thêm 7 tên tín hiệu cũ.

### Thay đổi

| Phần | Nội dung |
|---|---|
| Bộ sinh catalog | Mang nguyên hai sheet: 11 cột, 120 dòng, ô gộp; sheet trình tự và chiều mũi tên |
| API `GET /api/plc/io-map` | Trả `sheet`, `sequence`, số địa chỉ còn ở dữ liệu cũ; không còn liệt kê trạm cũ |
| Trang cấu hình địa chỉ IO | Bảng đúng cột, thứ tự, chữ và ô gộp của workbook; cột hệ thống tách riêng bên phải; bảng trình tự; báo rõ khi backend không cung cấp IO Map |
| Mô tả tín hiệu | Chữ của workbook, không dịch lại |
| Mô phỏng PLC | Bỏ 7 tín hiệu thử của catalog cũ. Mô phỏng yêu cầu bằng chọn quy trình và vị trí |
| Liên kết trạm cũ – Location | Trạm chỉ còn dữ liệu cũ (ví dụ X6 liên kết Location X4) không còn là cổng: không phục vụ Location, không hiện ở mô phỏng; gán Location đó cho trạm của IO Map sẽ gỡ liên kết cũ. Không tự gán |
| Dữ liệu cũ trong database | Không xóa. Chỉ hiện (đóng sẵn) khi còn địa chỉ đã nhập, để xóa địa chỉ |

Không thay đổi: tín hiệu vẫn **chưa xác nhận**, cấm ghi thật; không tạo địa chỉ ngoài workbook; địa chỉ workbook chỉ để tham chiếu; PLC-OQ vẫn `OPEN`; Location nghiệp vụ không đổi.

## B. Xóa Rack

### Nguyên nhân

Rack rảnh xóa được. Rack không xóa được khi bị một lệnh vận chuyển giữ: các lệnh đang chờ rack (`SourceUnavailable`) giữ rack mới thêm trong vài giây. Khi đó:

- thông báo là câu tiếng Anh của máy chủ, không nói đối tượng nào đang giữ;
- **giao diện không có nút hủy lệnh vận chuyển**, dù API đã có từ trước, nên không có cách gỡ.

### Thay đổi

| Phần | Nội dung |
|---|---|
| API `…/racks/{id}/editability` | Thêm `blockers`: lệnh hoặc nhiệm vụ đang giữ rack, trạng thái, hủy được hay không |
| Hộp thoại xóa/sửa | Lý do bằng ngôn ngữ giao diện, tên lệnh/nhiệm vụ, bước cần làm, liên kết mở; giải thích khi nút bị khóa; thông báo sau khi xóa |
| Trang Điều phối đa chặng | Nút **Hủy lệnh** (Admin, bắt buộc lý do) gọi API sẵn có; khóa khi có chặng đang chạy hoặc rack trên AGV |

Quy tắc từ chối của CR-040/042 **không đổi**: đang vận chuyển, giữ chỗ, thuộc lệnh chưa xong, xử lý thủ công, trạng thái chưa xác nhận, vị trí cần đối soát. Không xóa lệnh, chặng hay lịch sử.

### Còn lại (cần quyết định)

- Rack ở **xử lý thủ công**: chưa có quy trình được phê duyệt để xóa (P2-OQ-16).
- Rack **trạng thái chưa xác định** không gắn nhiệm vụ cần đối soát, và lệnh `ReconciliationRequired` còn rack trên AGV: giao diện chưa có thao tác đối soát cho lệnh.

## C. Kiểm thử (2026-11-26)

Không chạy Full Backend Regression.

| Kiểm tra | Kết quả |
|---|---|
| Test backend liên quan (IO Map, rack, hàng đợi, giám sát PLC, tồn kho) | 200/200 đạt |
| Sau sửa liên kết trạm: IO Map, rack, quy trình, route, cấu hình PLC | 268/269, một lỗi do test mới thiếu Location; sửa test, chạy lại CR-046/047: 15/15 |
| `Cr047WorkbookSheetTests` – đọc workbook độc lập, so từng ô hai sheet với catalog | Đạt |
| `Cr047RackRemovalTests` | 3/3 đạt |
| Frontend: tsc, lint, build, i18n:test 76, ops 69+65, plcmonitor 42, plcwrite 20, mode 46, monitor 23 | Đạt; `i18n:check` còn 7 lỗi mẫu log RCS có từ trước |
| Trình duyệt (Edge) trên **bản sao** database dev: IO Map | 1320/1320 ô trùng workbook; không còn tên cũ; lọc X4; nhập địa chỉ; sheet trình tự |
| Trình duyệt: xóa rack | Xóa 4 rack đang có; rack bị lệnh giữ → mở lệnh → hoàn tất chặng → xóa; hủy lệnh đang chờ; từ chối rack chưa xác định và xử lý thủ công; lệnh, chặng, lịch sử còn nguyên |

Database gốc chỉ được đọc; bản sao đã xóa. Chưa kiểm tra: PLC/RCS thật, Docker (chưa chạy lại cho thay đổi này).

Ảnh: [IO Map](../reports/cr-047/io-map.png) · [trạm X4 và trình tự](../reports/cr-047/io-map-X4-and-sequence.png) · [dòng cấp PLC](../reports/cr-047/io-map-plc-rows.png) · [rack bị giữ](../reports/cr-047/rack-held-by-order.png) · [hủy lệnh](../reports/cr-047/order-cancel.png) · [đã xóa](../reports/cr-047/rack-deleted.png) · [trước khi sửa](../reports/cr-047/before-rack-locked.png).

## D. Rollback

Hoàn tác thay đổi; không đổi schema, không đổi dữ liệu.
