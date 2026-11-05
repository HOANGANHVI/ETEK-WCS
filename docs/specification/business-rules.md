# 1. Business Rules – Quy tắc nghiệp vụ

## 1.1. Mục tiêu hệ thống

WCS là lớp điều phối nghiệp vụ giữa Mitsubishi PLC, người vận hành và HIK RCS-2000. WCS tiếp nhận yêu cầu vận chuyển, xác định AGV phù hợp, quản lý trạng thái rack/vị trí, gửi task tới RCS và theo dõi quá trình giao nhận.

## 1.2. Phạm vi tích hợp

- Một kết nối PLC vật lý tới Mitsubishi PLC.
- Một kết nối API tới HIK RCS-2000.
- Hai AGV theo phân công nghiệp vụ:
  - `AGV1`: vận chuyển rack hàng đầy.
  - `AGV2`: vận chuyển rack trống.
- Một PLC connection phục vụ nhiều station/transfer point. Không chia station thành cluster trong logic WCS.

## 1.3. Quyết định nghiệp vụ bắt buộc

| ID | Quy tắc | Giá trị |
|---|---|---|
| BR-01 | Task concurrency | `PER_AGV` |
| BR-02 | Luồng slide 8 – B1/B2 cần rack đầy | `REVIEW` |
| BR-03 | Quản lý Bind Rack / tồn kho rack | `WCS` |
| BR-04 | A3/A4 và D1/D2 | `UNCHANGED` |

### BR-01 – PER_AGV

- Mỗi AGV được phép có tối đa một task đang hoạt động.
- AGV1 và AGV2 có thể thực hiện task song song.
- Không áp dụng giới hạn một task cho toàn hệ thống.
- Một AGV đang thực hiện task không được nhận task mới, trừ khi task hiện tại đã kết thúc theo trạng thái cuối cùng được xác nhận.
- Các yêu cầu đến trong lúc AGV bận phải được lưu hàng đợi.

### BR-02 – REVIEW

Luồng slide 8 mô tả B1/B2 có vị trí trống và yêu cầu rack đầy. Tài liệu nguồn có sự không nhất quán về định danh AGV trong các bước. Vì vậy:
- Không tự sửa AGV1/AGV2.
- Không triển khai luồng này trong phiên bản nghiệp vụ đã chốt.
- Chỉ đưa vào triển khai sau khi người phụ trách nghiệp vụ xác nhận lại luồng.

### BR-03 – WCS sở hữu trạng thái rack

WCS là nguồn dữ liệu nghiệp vụ chính cho:
- Rack ID (nếu có).
- Loại rack.
- Trạng thái `FULL` / `EMPTY`.
- Vị trí hiện tại.
- Trạng thái chiếm dụng của vị trí.
- Trạng thái di chuyển và task liên quan.

WCS cập nhật tồn kho dựa trên sự kiện giao nhận đã xác nhận, không dựa đơn thuần vào việc tạo task thành công.

Các bước được tài liệu nguồn ghi rõ `Không bind rack` vẫn phải giữ nguyên. WCS không được tự ý áp đặt thao tác bind rack tại các điểm đó.

### BR-04 – A3/A4 và D1/D2

- Giữ nguyên process cũ.
- Không áp dụng các luồng A1/A2 hoặc B1/B2 thay thế cho các vị trí này.
- Chi tiết triển khai phải tham chiếu process cũ. Nếu process cũ chưa có trong bộ tài liệu đầu vào, đánh dấu phụ thuộc tài liệu và không tự tạo quy trình.

## 1.4. Phân công AGV

### AGV1 – rack hàng đầy

Theo tài liệu quy trình:
- Nguồn: KV1, KV3.
- Đích: KV2, KV4, F5.

### AGV2 – rack trống

Theo tài liệu quy trình:
- Nguồn: KV2, KV4.
- Đích: KV1, KV3.

Các luồng chi tiết có thể sử dụng thêm F1/F2/F3/F4 và A1/A2/B1/B2 theo từng kịch bản được mô tả trong `process-flows.md`. Không suy diễn rằng mọi vị trí đều là nguồn/đích hợp lệ cho mọi loại rack.

## 1.5. Vị trí

| Vị trí | Mô tả từ tài liệu / vai trò |
|---|---|
| A1/A2 | Trạm máy |
| B1/B2 | Trạm máy; có các kịch bản rack trống và vị trí trống |
| F1/F2 | Buffer rack hàng đầy trong các luồng được mô tả |
| F3/F4 | Buffer rack trống trong các luồng được mô tả |
| F5 | Manual Buffer |
| KV1/KV2 | Điểm trong luồng vận chuyển |
| KV3 | Buffer hàng |
| KV4 | Buffer rack trống |

Không tự gán thêm ý nghĩa cho KV1/KV2 ngoài sơ đồ nguồn nếu chưa có xác nhận.

## 1.6. Nguyên tắc an toàn và giao nhận

- “PLC mở cửa” trong quy trình được hiểu là PLC cho phép cơ cấu/điểm Load-Unload của máy thực hiện giao nhận; không diễn giải thành cửa ra vào nhà xưởng.
- AGV chỉ thực hiện bước vào lấy/thả khi nhận được điều kiện cho phép tương ứng.
- Nếu chưa có xác nhận giao nhận vật lý, không đánh dấu task hoàn tất và không cập nhật tồn kho đích như đã có rack.
- Khi lỗi giữa hành trình, phải xác minh vị trí rack thực tế trước khi điều chỉnh tồn kho.

## 1.7. Quy tắc khi thiếu điều kiện

- Không có vị trí đích: giữ task ở `WAITING_FOR_DESTINATION`, đánh giá lại khi trạng thái vị trí thay đổi.
- Không tạo task trùng cho cùng AGV.
- Không cho phép hai task chiếm cùng rack hoặc cùng vị trí đích nếu gây xung đột.
- Các ưu tiên nguồn/đích chưa được xác nhận phải được cấu hình hoặc đánh dấu `NEEDS_CONFIRMATION`, không tự hard-code.
