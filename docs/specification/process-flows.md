# 2. Process Flows – Luồng nghiệp vụ

## 2.1. Quy ước

- `PLC`: Mitsubishi PLC.
- `WCS`: hệ thống điều phối nghiệp vụ.
- `RCS`: HIK RCS-2000.
- `AGV1`: robot vận chuyển rack hàng đầy.
- `AGV2`: robot vận chuyển rack trống.
- `continueTask`: thao tác yêu cầu RCS tiếp tục task đang tạm dừng/chờ theo thiết kế task template.
- `WCS inventory`: dữ liệu tồn kho rack/vị trí do WCS quản lý.
- `REVIEW`: không triển khai trước khi được xác nhận.

Các bước giao nhận tại máy có PLC điều khiển cho phép. Quy trình nguồn có một số vị trí không sử dụng bind rack; phải giữ đúng chỉ dẫn của từng luồng.

## BP-01 – A1/A2 có rack đầy

**Mục tiêu:** Lấy rack đầy tại A1/A2 và chuyển đến vị trí đích phù hợp.

1. PLC phát yêu cầu lấy hàng/rack tại station.
2. WCS tiếp nhận, xác thực request và kiểm tra station.
3. WCS tạo task cho AGV1.
4. AGV1 di chuyển tới trước vị trí được yêu cầu.
5. AGV1 yêu cầu quyền lấy rack.
6. PLC cho phép cơ cấu Load/Unload và AGV vào lấy rack.
7. AGV1 lấy rack và rời điểm giao nhận.
8. WCS nhận xác nhận hoàn tất bước lấy và gọi `continueTask`.
9. WCS kiểm tra vị trí đích theo trình tự thể hiện trong tài liệu:
   - B1/B2 có vị trí trống hay không.
   - Nếu không, kiểm tra F1/F2 có vị trí trống theo WCS inventory.
   - Nếu không, kiểm tra F5 có vị trí trống theo WCS inventory.
10. Nếu chưa có vị trí đích, chuyển task sang `WAITING_FOR_DESTINATION`; không hủy task hoặc tạo task mới.
11. Khi có vị trí đích, WCS chỉ định điểm thả cho AGV1.
12. AGV1 thực hiện giao rack tại điểm đích.
13. Với điểm cần PLC cho phép, AGV chỉ tiến hành sau khi PLC cấp quyền.
14. Tại B1/B2, tài liệu ghi rõ thả rack “Không bind rack”; giữ nguyên hành vi này.
15. Tại F1/F2 và F5, trạng thái vị trí được kiểm tra bằng WCS inventory.
16. WCS xác nhận kết quả, cập nhật tồn kho và hoàn tất task.
17. AGV1 trở về điểm chờ theo task template/quy trình RCS.

**Điều kiện lỗi/chờ:** Không có vị trí đích thì `WAITING_FOR_DESTINATION`.

## BP-02 – A1/A2 có vị trí trống, cần rack trống

1. PLC gửi yêu cầu cấp rack trống.
2. WCS tiếp nhận và tạo task cho AGV2.
3. WCS tìm rack trống tại B1/B2.
4. Nếu không có, WCS kiểm tra F3/F4 theo WCS inventory.
5. Nếu tìm thấy rack trống, WCS chỉ định vị trí nguồn cho AGV2.
6. AGV2 đi tới vị trí nguồn và yêu cầu PLC cho phép lấy nếu điểm giao nhận yêu cầu.
7. PLC cho phép; AGV2 lấy rack trống và rời vị trí.
8. WCS nhận xác nhận lấy rack và gọi `continueTask`.
9. AGV2 di chuyển tới A1/A2 theo yêu cầu ban đầu.
10. AGV2 yêu cầu PLC cho phép thả rack.
11. PLC cho phép giao nhận; AGV2 thả rack và rời vị trí.
12. WCS xác nhận giao nhận và cập nhật tồn kho.
13. AGV2 trở về điểm chờ.

**Lưu ý:** Nếu không có rack trống ở cả B1/B2 và F3/F4, xử lý theo trạng thái chờ/lỗi nghiệp vụ được cấu hình; không tự tạo nguồn thay thế.

## BP-03 – B1/B2 có rack trống

1. PLC gửi yêu cầu lấy rack trống tại B1/B2.
2. WCS tiếp nhận và tạo task cho AGV2.
3. AGV2 đến vị trí được yêu cầu.
4. AGV2 yêu cầu PLC cho phép lấy rack.
5. PLC cho phép; AGV2 lấy rack trống và rời vị trí.
6. WCS xác nhận lấy rack và gọi `continueTask`.
7. WCS kiểm tra A1/A2 có vị trí trống hay không.
8. Nếu A1/A2 không có vị trí phù hợp, WCS kiểm tra F3/F4 theo inventory.
9. Nếu không có đích phù hợp, chuyển `WAITING_FOR_DESTINATION`.
10. Khi có đích, WCS chỉ định điểm thả cho AGV2.
11. AGV2 giao rack tại đích; các điểm giao nhận cần PLC cho phép phải tuân thủ handshake.
12. Tại A1/A2, tài liệu ghi rõ thả rack “Không bind rack”; giữ nguyên hành vi này.
13. WCS cập nhật inventory theo kết quả thực tế.
14. AGV2 trở về điểm chờ.

## BP-04 – B1/B2 có vị trí trống, cần rack đầy

**Trạng thái: REVIEW – OUT OF IMPLEMENTATION SCOPE.**

Tài liệu slide 8 có các bước không nhất quán về AGV1/AGV2. Không được:
- Tự đổi tất cả tham chiếu thành AGV1 hoặc AGV2.
- Sao chép BP-01 để thay thế luồng này.
- Triển khai task template cho luồng này trước khi xác nhận.

Cần xác nhận tối thiểu:
1. AGV nào thực hiện nhiệm vụ lấy rack đầy?
2. AGV nào thực hiện giao rack tại B1/B2?
3. Có một AGV thực hiện toàn bộ luồng hay có bước chuyển giao giữa AGV?
4. Điểm nào cần `continueTask` và điều kiện tiếp tục là gì?

## BP-05 – F5 yêu cầu cấp rack đầy

1. Người vận hành nhấn beeper gọi hàng.
2. WCS tiếp nhận yêu cầu.
3. WCS tạo task cho AGV1.
4. WCS kiểm tra F1/F2 có rack đầy theo WCS inventory.
5. Nếu có, WCS chỉ định nguồn cho AGV1.
6. AGV1 lấy rack đầy và di chuyển đến F5.
7. WCS kiểm tra F5 có vị trí trống theo WCS inventory.
8. Nếu F5 có vị trí trống, AGV1 thả rack tại F5.
9. WCS xác nhận và cập nhật inventory.
10. AGV1 trở về điểm chờ.

**Điểm cần xác nhận khi thiết kế chi tiết:** Sơ đồ nguồn có thể hiện tín hiệu xóa/hủy task AGV1 ở nhánh F5. Cần đối chiếu lại điều kiện nhánh trên sơ đồ gốc trước khi hiện thực hóa. Không tự động gọi cancel task sau khi AGV đã thả rack.

## BP-06 – F5 yêu cầu trả rack trống

1. Người vận hành nhấn beeper yêu cầu trả rack trống.
2. WCS tiếp nhận và tạo task cho AGV2.
3. AGV2 đến F5 lấy rack trống.
4. WCS xác định đích theo điều kiện trong quy trình:
   - A1/A2 có vị trí trống.
   - F3/F4 có vị trí trống theo WCS inventory.
5. WCS chỉ định điểm thả.
6. AGV2 di chuyển đến đích.
7. Nếu điểm đích yêu cầu PLC cho phép giao nhận, AGV2 phải chờ PLC cho phép.
8. AGV2 thả rack và rời vị trí.
9. Tại A1/A2, tài liệu ghi rõ “Không bind rack”; giữ nguyên hành vi này.
10. Tại F3/F4, trạng thái vị trí được quản lý bởi WCS inventory.
11. WCS cập nhật inventory và hoàn tất task.
12. AGV2 trở về điểm chờ.

## 2.2. Luồng ngoài phạm vi thay đổi

### A3/A4 và D1/D2

Tài liệu nguồn nêu rõ process của A3/A4 và D1/D2 giữ nguyên như process cũ. Không đặc tả lại hoặc thay đổi logic. Việc triển khai phải tham chiếu tài liệu/process cũ.

## 2.3. Những điểm không được tự suy diễn

- Không suy ra quy trình Phase 2 nếu chưa có tài liệu.
- Không tự gán ý nghĩa chi tiết cho KV1/KV2 ngoài sơ đồ.
- Không tự thêm bind rack vào điểm được ghi “Không bind rack”.
- Không tự sửa luồng BP-04.
- Không tự đổi thứ tự ưu tiên nguồn/đích nếu tài liệu hoặc quyết định nghiệp vụ chưa xác nhận.
