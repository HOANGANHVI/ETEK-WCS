# CR-058 – Cấu hình triển khai chỉ cảnh báo, không chặn xóa vị trí

| | |
|---|---|
| **Trạng thái** | `APPROVED (phương án A), IMPLEMENTED – đã MERGE vào main (1ee4fe9)` |
| **Nguồn phê duyệt** | Chủ dự án, 2026-11-27: chọn "A" sau khi báo F5 không xóa được |
| **Migration** | Không |

## Thay đổi so với CR-057

Bỏ điều kiện chặn `LOCATION_DELETE_CONFIGURED`. Vị trí được nêu trong cấu hình triển khai (`ProcessFlows`, `RouteEngine:PlcLocationCodes`) xóa được; hộp thoại cảnh báo trước khi xác nhận:

| Mã cảnh báo | Sau khi xóa |
|---|---|
| `LOCATION_WARN_FLOW_STATION` | Không gọi được luồng đó từ vị trí này (`STATION_NOT_FOUND`) |
| `LOCATION_WARN_FLOW_SOURCE` | Luồng bỏ qua vị trí khi tìm rack nguồn |
| `LOCATION_WARN_FLOW_DESTINATION` | Luồng bỏ qua vị trí khi tìm đích |
| `LOCATION_WARN_PLC_CODE` | Yêu cầu PLC gửi mã vị trí đó bị từ chối |

Các điều kiện chặn còn lại giữ nguyên: trạm được bảo vệ (A3, A4, D1, D2), còn rack, còn liên kết cổng PLC, quy trình đang dùng, việc chưa kết thúc.

`GET /api/flows` không còn đưa vị trí đã xóa vào danh sách trạm yêu cầu. `GET /api/config/locations/{code}/removal` trả thêm `warnings`.

## Không đổi

File cấu hình triển khai và đặc tả không bị sửa: tạo lại vị trí cùng mã thì luồng dùng lại. Database, PLC, RCS, quy tắc tồn kho không đổi.

## Ảnh hưởng và rủi ro

- Áp dụng cho mọi vị trí được cấu hình, gồm A1, A2, B1, B2, F1–F5: khi đã gỡ hết các điều kiện chặn, Admin xóa được cả vị trí mà luồng đang cần. Chủ dự án chấp nhận khi chọn phương án A.
- Xóa F5: không còn gọi được BP-05, BP-06; BP-01 mất đích cuối. Đặc tả `process-flows.md` vẫn mô tả F5.
- Kiểm tra sẵn sàng chế độ Real vẫn yêu cầu `positionCode` cho mọi mã trong cấu hình, kể cả vị trí đã xóa (giữ nguyên, không nới điều kiện an toàn).
- Bảng "Quy trình đã triển khai" vẫn hiển thị cấu hình như đã khai báo.
- Không khôi phục được vị trí đã xóa; tạo lại là bản ghi mới.

## Rollback

Quay lại bản trước (không có migration). Vị trí đã xóa vẫn ở trạng thái đã xóa.

## Kiểm thử

`Cr057LocationRemovalTests` (thêm trường hợp CR-058), `ops-smoke`, toàn bộ test backend (1457/1457), trình duyệt trên bản sao dữ liệu: [hộp xác nhận](../reports/cr-058/1-warning.png).
