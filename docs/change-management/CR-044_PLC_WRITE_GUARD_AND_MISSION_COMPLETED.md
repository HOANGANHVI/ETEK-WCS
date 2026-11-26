# CR-044 – Chặn ghi PLC mặc định (OW-2) và đính chính `PLC_MissionCompleted` (OW-4)

| | |
|---|---|
| **Trạng thái** | `MERGED – NOT DEPLOYED` (chủ dự án cho phép đưa vào `main` ngày 2026-11-26). Đã kiểm thử trên PLC giả lập / Simulator |
| **Nguồn phê duyệt** | Chủ dự án, 2026-11-26 (OW-2, OW-4) |
| **Nền / commit** | Nền `844c415`; commit `b25b4b6`, sau các commit CR-042, CR-043 (tài liệu) và Phase 2 |
| **Migration** | Không |
| **Không đổi** | Workbook, catalog IO, mapping, CR-029, đọc PLC, Simulator, hợp đồng IO đang chờ kỹ sư PLC, câu "cột địa chỉ trống" trong `CLAUDE.md` (C-8) |

## 1. Luật ghi ở chế độ Real

Một lệnh ghi chỉ được gửi khi **tất cả** đúng:

| # | Điều kiện | Mã từ chối |
|---|---|---|
| 1 | Chiều Phần mềm → PLC; không phải tín hiệu Safety/Permit | `WRITE_DIRECTION`, `WRITE_BLOCKED_SAFETY` |
| 2 | `Mode=RealReadWrite`, `AllowLiveConnection`, `Write.Enabled` | `READ_ONLY` |
| 3 | Admin đã duyệt ghi điểm; có địa chỉ hợp lệ | `WRITE_NOT_ALLOWED`, `NO_ADDRESS`, `ADDRESS_NOT_APPROVED` |
| 4 | **Mới:** không chạm thanh ghi dành riêng (kể cả khi đã khai báo; tính cả độ dài STRING) | `WRITE_ADDRESS_RESERVED`, `WRITE_RESERVED_CONFIG_INVALID` |
| 5 | **Mới:** khai báo trong `Plc:Write:AllowList`, khớp PLC + trạm + tín hiệu + địa chỉ (STRING thêm độ dài); mặc định rỗng, không ký tự đại diện | `WRITE_NOT_DECLARED` |
| 6 | **Mới:** bản ghi cho phép đã lưu được vào audit **trước khi** gửi | `WRITE_AUDIT_UNAVAILABLE` |

- Chốt thứ hai ở worker của kết nối: chỉ ghi điểm của chính kết nối, đúng như cấu hình (`WRITE_POINT_UNKNOWN`), và áp dụng lại luật trên.
- API không nhận địa chỉ; người gọi chỉ chọn PLC, trạm, tín hiệu, giá trị.
- Handshake tự động ở Real đi qua cùng luật.
- Simulator: như trước.

### Khai báo

```json
"Plc": { "Write": { "AllowList": [ { "PlcId": "PLC-01", "StationId": "A1", "Signal": "Request_Ack", "Address": "M101" } ] } }
```

`Plc:Write:ReservedAddresses` nằm trong `appsettings.json`: các thanh ghi của workbook `a291d28f` (`D15n0–D15n2`, `D16n0–D16n1`, `M9200–M9205`). Gỡ hoặc đổi danh sách này cần phê duyệt (khi hợp đồng IO được xác nhận).

## 2. Audit (chế độ Real)

| Hành động | Khi nào |
|---|---|
| `PLC_WRITE_DENIED` | Bị từ chối bởi luật, kiểm tra giá trị, không tìm thấy điểm; yêu cầu bị 401/403 (mọi chế độ) |
| `PLC_WRITE_AUTHORIZED` | Được phép, trước khi gửi |
| `PLC_WRITE_RESULT` | Kết quả (kể cả không rõ) |
| `PLC_TEST_WRITE` | Bản ghi của API như trước; nay gồm cả yêu cầu bị từ chối khi tra điểm hoặc đọc giá trị |

## 3. Tương thích

| Đối tượng | Ảnh hưởng |
|---|---|
| Triển khai đang ghi PLC thật | **Mọi lệnh ghi bị chặn** cho tới khi khai báo |
| Docker | `docker-compose.yml` **chưa truyền** `Plc__Write__AllowList__*`; muốn ghi phải bổ sung biến hoặc file cấu hình. Không sửa trong CR này |
| Điểm đã duyệt ghi trên database máy phát triển (9) | Không chạm thanh ghi dành riêng; cần khai báo nếu chạy Real |
| Điểm đặt ở thanh ghi dành riêng | Vẫn đọc; bị chặn ghi ở Real; sự kiện `WRITE_GUARD_CHECK` liệt kê khi khởi động / nạp lại |
| CR-019 | "Phê duyệt duy nhất là duyệt ghi của Admin" **được thay một phần**: thêm khai báo của bản triển khai |
| Test cũ ghi Real | Bổ sung khai báo; thanh ghi thử `D1600` đổi thành `D1700` |

## 4. OW-4

`PLC_MissionCompleted`: `1` = Thất bại, `2` = Thành công; một giá trị INT không được coi là hoàn thành khi chưa xác minh đúng giao dịch. Đã sửa `CLAUDE.md` §7.8 (D-4), ADR-0011 (đính chính), `REQUIREMENT_TRACEABILITY.md`; thêm đính chính đầu 8 tài liệu trong `PLC_20_11_2026/`. Không có mã nào đọc tín hiệu này. **Còn câu cũ:** `CR-029_PLC_INTERFACE_20_11_2026.md` (ngoài phạm vi), CHANGELOG lịch sử.

## 5. Triển khai và rollback

Không migration. Trước khi cập nhật máy đang ghi thật: lập danh sách điểm cần ghi, được phê duyệt, khai báo. Rollback: hoàn tác mã (bản cũ bỏ qua cấu hình mới) – đồng thời mất lớp chặn.

## 6. Kết quả kiểm thử

| Kiểm tra | Kết quả |
|---|---|
| Full backend regression | **1371/1371 đạt** (16 phút 45 giây), sau mọi thay đổi mã |
| Nhóm PLC, handshake, MC, chuyển cấu hình | 394/394 đạt |
| Frontend: `tsc`, `eslint`, build | Đạt |
| `i18n:test` 76, `plcwrite:test` 20, `plcmonitor:test` 29, `monitor:test` 23, `ops:test` 65 | Đạt |
| `i18n:check` | 7 lỗi mẫu log RCS có từ trước; không lỗi mới |
| Test mới `Cr044PlcWriteGuard*` | Cho phép / từ chối; Real chỉ đọc, đọc-ghi, Simulator; chặn ở API và ở worker; audit; audit hỏng thì không gửi; thanh ghi dành riêng |
| PLC thật, giao diện trên trình duyệt | **NOT VERIFIED** (chỉ PLC giả lập MC) |

Lần chạy nhóm PLC đầu tiên có 8 test cũ thất bại đúng theo luật mới (ghi Real chưa khai báo, hoặc ghi `D1600`); đã bổ sung khai báo và đổi sang `D1700`, giữ nguyên các khẳng định còn lại.
