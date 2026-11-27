# CR-052 – Lý do chờ nêu đúng nguyên nhân khi không có vị trí đích dùng được

| | |
|---|---|
| **Trạng thái** | `IMPLEMENTED – nhánh fix/cr-052-destination-reason, CHƯA MERGE, CHƯA DEPLOY` |
| **Nguồn phê duyệt** | Chỉ thị của chủ dự án, 2026-11-27 (sửa lỗi kỹ thuật nếu có) |
| **Nền** | `main` `20fa494` |
| **Migration** | Không |

## Trường hợp

Lệnh `LVC-D843F4F4` (BP-02, rack rỗng, yêu cầu từ A1) chờ ở bước `s1` với `No usable destination`. Đích của bước là chính điểm yêu cầu (`destinationMode = Request`). A1 trống, còn chỗ, nhận mọi loại rack, không cần đối soát, PLC kết nối; điều kiện duy nhất không đạt: cổng A1 còn giao dịch `HandoverCompleted` của lệnh `a15c4d2a` (chưa xác nhận AMR rời cổng). Hành vi chờ là đúng; thông báo không nêu nguyên nhân.

## Thay đổi

- `RouteConditionEvaluator.DestinationBlockerAsync`: nguyên nhân của từng ứng viên đích, lặp lại đúng thứ tự kiểm tra, không quyết định gì.
- Lý do chờ (trước Pick và khi AMR đang giữ rack) liệt kê nguyên nhân từng đích; tiền tố `PORT_HELD:` khi chỉ do cổng bị giữ, giao diện hiển thị "Cổng trạm đang bị giữ…".

Không đổi: điều kiện chọn đích, loại chờ, trạng thái, giữ chỗ, quy tắc nhả cổng.

## Cách xử lý lệnh đang chờ

Khi AMR thực sự đã rời A1: Điều phối đa chặng → Giao dịch tại cổng → "AMR đã rời cổng" cho A1. Lệnh tự tạo chặng ở lần đánh giá kế tiếp.

## Kiểm thử

`QaWaitingReasonTests` (tái hiện: 3 ca đỏ trước khi sửa, 4/4 đạt sau khi sửa), hồi quy định tuyến 246/246, `ops-smoke`, kiểm tra trên bản sao dữ liệu.

## Rollback

Hoàn tác thay đổi; không đổi schema hay dữ liệu.
