# CR-048 – Địa chỉ IO mặc định theo workbook; nâng cấp catalog trong database

| | |
|---|---|
| **Trạng thái** | `ĐÃ MERGE vào main 2026-11-27 (merge 29ad2e6) – CHƯA DEPLOY` |
| **Nguồn phê duyệt** | Chỉ thị bổ sung của chủ dự án, 2026-11-26 |
| **Nền** | CR-047 (chưa commit) trên `main` `b07a6d9` |
| **Migration** | `AddIoDefaultAddress` (2026-11-27: đã chạy trên database phát triển cục bộ theo quyết định của chủ dự án, có sao lưu; chưa chạy trên database vận hành) |
| **Version** | `2026.11.26.4` |

## 1. Quyết định được ghi nhận

Địa chỉ ghi trong `AGV_PLC Interface_List_22_11_2026.xlsx` được **tự gán làm địa chỉ mặc định** của từng IO point.

Quyết định này thay thế, riêng cho việc gán địa chỉ, quy tắc "`Actual_PLC_Address` chỉ gán sau khi kỹ sư PLC xác nhận" (CLAUDE.md 7.3) và "địa chỉ chỉ để tham chiếu" (CR-046/047). **Không đổi:** hợp đồng IO chưa được kỹ sư PLC xác nhận; Write Guard, AllowList, chế độ chỉ đọc, cấm ghi thật mọi tín hiệu chưa xác nhận.

**Hệ quả cần biết:** ở chế độ Real, khi kết nối của một PLC được bật, hệ thống sẽ **đọc** các địa chỉ mặc định này.

## 2. Hành vi

| Trường hợp | Kết quả |
|---|---|
| Point chưa từng cấu hình | Nhận địa chỉ của đúng trạm và tín hiệu trong workbook; nguồn `WorkbookDefault`; chưa duyệt, không ghi được |
| Workbook không có địa chỉ | Để trống, hiển thị "IO Map không có địa chỉ" |
| Admin nhập hoặc xóa địa chỉ | Nguồn `Custom`; không bao giờ bị ghi đè khi khởi động |
| Khôi phục mặc định | `POST /api/plc/points/{id}/address/restore-default` (Admin, có audit); từ chối nếu điểm khác đang dùng địa chỉ |
| Địa chỉ mặc định đang bị địa chỉ khác chiếm | Không gán, ghi log cảnh báo, hiển thị "Chưa gán: địa chỉ mặc định đang bị dùng" |
| Catalog đổi địa chỉ | Chỉ các point còn là mặc định đổi theo |
| `AGV_RequestTranfer`, `AGV_TranferCompleted` | Dùng chung một thanh ghi đúng như ô gộp của workbook; mọi trùng địa chỉ khác vẫn bị từ chối |
| `HeartBit` (cấp PLC) | Địa chỉ nằm trong catalog, hiển thị ở màn hình Giám sát I/O; không có IO point, chưa đọc/ghi |
| 5 dòng dự phòng mỗi PLC | Không phải tín hiệu |

## 3. Nâng cấp dữ liệu cũ (chạy khi backend khởi động, trong một transaction)

1. Dòng của tín hiệu không có trong catalog được **chuyển** sang bảng `retired_io_points` (giữ địa chỉ, trạng thái, phê duyệt, liên kết trạm).
2. Trạm chỉ còn các dòng đó (X6, G3, G7) được đánh dấu `RetiredAt`: ẩn khỏi mọi danh sách, không còn là cổng, **gỡ liên kết Location**. Không tự gán Location cho trạm mới.
3. Tạo trạm và point còn thiếu, rồi gán địa chỉ mặc định.

Địa chỉ đã có trên point trước migration được đánh dấu `Custom` và giữ nguyên.

## 4. Thay đổi

| Phần | Nội dung |
|---|---|
| Database | `station_io_points.AddressOrigin`, `.SharedWith`; `plc_stations.RetiredAt`; bảng `retired_io_points`; chỉ mục địa chỉ cho phép cặp dùng chung thanh ghi |
| Backend | `PlcCatalogSeeder`, `AddressValidator`, `PlcConfigService`, repository, endpoint khôi phục |
| API | `points`: thêm `defaultAddress`, `addressOrigin`; bỏ `includeRetired`; `io-map`: bỏ `leftoverAddresses`; `monitor/points`: thêm `addressOrigin` |
| Giao diện | Nhãn nguồn địa chỉ, nút khôi phục, bảng tín hiệu cấp PLC; bỏ bảng "địa chỉ cũ" của CR-047 và danh sách mô tả 23 tín hiệu cũ viết cứng |

## 5. Kiểm thử (2026-11-26 và kiểm tra trước commit 2026-11-27)

Không chạy Full Backend Regression.

| Kiểm tra | Kết quả |
|---|---|
| Test backend liên quan trên cây cuối: PLC, CR-010, CR-026, CR-035, CR-036, CR-039, CR-04x, tồn kho, rack | 659/659 đạt |
| `Cr048RealModeReadOnlyTests`: RealReadOnly và RealReadWrite (có khai báo ghi) với PLC giả lập MC trên loopback | Chỉ nhận lệnh đọc; không lệnh ghi |
| Frontend: tsc, lint, build, 11 bộ smoke test | Đạt; `i18n:check` còn 7 lỗi mẫu log RCS có từ trước |
| Bản sao database phát triển, 4 migration Up | Không dòng nào của bảng cũ bị thêm, đổi hay xóa |
| Down cả 4 trước lần khởi động đầu | Schema và dữ liệu giống nguồn |
| Khởi động bản mới (hai lần) | Không xóa dòng nào của 14 bảng nghiệp vụ/lịch sử; lần hai không đổi bảng PLC |
| 368 dòng IO cũ trong `retired_io_points` | Đủ 368 dòng, 23/23 giá trị mỗi dòng khớp nguồn |
| Down sau khi đã nâng cấp | Bị từ chối `ROLLBACK_REFUSED`, dữ liệu không đổi |
| Khôi phục từ bản sao lưu | Schema và dữ liệu giống nguồn |
| Database từng chạy CR-046 | Địa chỉ đã nhập được giữ qua nâng cấp và hai lần khởi động |
| Trình duyệt (Edge, 2026-11-26, trước hai sửa đổi ở bảng lưu và Down) | 96/96 địa chỉ khớp; sửa/xóa → restart → giữ; khôi phục → restart |

Database gốc chỉ được đọc. Chưa kiểm tra: PLC thật, Docker, bản mới chạy khi chưa migration, bản cũ chạy trên database đã nâng cấp.

Bằng chứng: [bảng đối chiếu](../reports/cr-048/IO_ADDRESS_COMPARISON.md) · [trạm A1](../reports/cr-048/monitor-station-A1.png) · [giám sát I/O A1](../reports/cr-048/monitor-list-A1.png) · [cấu hình sau sửa và restart](../reports/cr-048/config-A1-edited-after-restart.png) · [sau khôi phục](../reports/cr-048/config-A1-restored.png).

## 6. Còn lại

- Database từng chạy bản CR-046: địa chỉ Admin đã **xóa** trước CR-048 không phân biệt được với "chưa cấu hình", nên nhận địa chỉ mặc định ở lần nâng cấp đầu.
- Chưa xử lý, chưa kiểm thử: workbook đời sau **hoán đổi** địa chỉ giữa hai point đang là mặc định có thể vi phạm chỉ mục duy nhất khi khởi động (transaction hủy, dữ liệu giữ nguyên, log "Seeding failed").
- Mã nguồn còn logic viết theo tên tín hiệu cũ (bộ quan sát handshake, tiếp nhận yêu cầu theo `Pickup_Request`): không chạy với catalog mới; thuộc phần handshake chưa hiện thực.
- Hai tín hiệu dùng chung thanh ghi được mô phỏng bằng hai giá trị riêng.
- Câu hỏi PLC vẫn `OPEN`.

## 7. Rollback

- Sao lưu database trước khi nâng cấp. Rollback sau nâng cấp: **khôi phục bản sao lưu** và hoàn tác commit CR-048 cùng nhau.
- `Down` của `AddIoDefaultAddress` chỉ chạy được khi chưa có dòng lưu, trạm đã nghỉ hay địa chỉ dùng chung; ngược lại bị từ chối để không mất dữ liệu.
- Commit CR-048 hoàn tác được riêng (về đúng trạng thái CR-047). Phần IO Map của CR-047 chỉ hoàn tác được sau CR-048. Phần xóa rack của CR-047 hoàn tác được riêng, không đụng database.
