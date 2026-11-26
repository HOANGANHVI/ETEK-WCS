# OQ-54 – Phân tích lại Queue Ordering Scope và phương án A2

| | |
|---|---|
| **Ngày** | 2026-11-26 |
| **Trạng thái** | `ĐỀ XUẤT – CHỜ PHÊ DUYỆT`. Không sửa luồng điều phối. Chưa scope nào được khai báo |
| **Bằng chứng** | Đọc source (`RouteEngine`, `RoutingRepository`), danh mục quy trình, test trên PostgreSQL với Simulator. Không kiểm chứng ngoài hiện trường |

## 1. Scope hiện là gì

Scope là **miền thứ tự FIFO** do Admin gán cho quy trình. Trong một scope, yêu cầu mới không được:

1. nhận nguồn/đích khi yêu cầu cũ hơn còn chờ tài nguyên;
2. nhận đích sau khi lấy rack, trong cùng điều kiện;
3. gửi lệnh sang hệ AGV trước chặng cũ hơn chưa gửi.

"Còn chờ tài nguyên" gồm cả chặng đã tạo nhưng **chưa có đích**. Với quy trình chọn đích sau khi lấy (mọi quy trình cất `PH2-01/03/07/09/11/12`), một scope vì thế chỉ cho một AGV đang đi lấy tại một thời điểm.

| Scope **không phải** | Lý do |
|---|---|
| Quy trình | Nhiều quy trình dùng chung được một scope |
| Chiều vận chuyển | Không có trường nào mô tả chiều |
| Vùng tài nguyên vật lý | Khi chặng đã có đích, yêu cầu kế tiếp được đi ngay, kể cả cùng lối đi |
| Khóa chống va chạm | Không khóa vị trí, lối đi hay giao lộ |

## 2. Tài nguyên vật lý đang được bảo vệ bằng gì

| Tài nguyên | Cơ chế | Mức bảo vệ |
|---|---|---|
| Chỗ chứa tại vị trí | Giữ chỗ + row version | Cả hai engine |
| Rack | Giữ chỗ + quyền sở hữu của lệnh (OQ-42-08) | Cả hai engine |
| Cổng PLC | Một Station Transaction đang hoạt động mỗi cổng | Chỉ engine lệnh; engine Task không mở giao dịch cổng |
| AGV (`PER_AGV`, dispatch Legacy) | Kiểm tra trước khi giao | **Một chiều, xem F1** |
| AGV (RcsManaged) | RCS chọn | Ngoài WCS |
| **Lối đi, giao lộ, đường hai chiều** | **WCS không có dữ liệu và không kiểm soát** | Phụ thuộc điều khiển giao thông của RCS: **CHƯA XÁC MINH** |

## 3. Đánh giá A2 (mỗi chiều cất một scope)

| Vấn đề | A (chung scope hai chiều) | A2 |
|---|---|---|
| Chờ vòng giữa hai chiều cất do FIFO (B và buffer đầy) | **Có** – test `Opposite_directions_block_each_other_in_one_scope` | Hết |
| Chờ vòng do giữ chỗ chéo scope (F2) | Có | **Vẫn còn** |
| AGV đứng giữ rack khi mọi đích đầy | Có | Vẫn có |
| Hai AGV đi ngược chiều cùng lúc | Có thể | Có thể |
| FIFO giữa hai chiều tranh buffer chung | Có | Mất |

Scope chung cũng không ngăn đi ngược chiều: chặng cũ đã có đích thì chặng mới xuất phát khi chặng cũ còn đang chạy.

### F2 – Giữ chỗ trước khi có AGV (đã tái hiện)

Chặng được tạo và **giữ chỗ đích ngay**, dù AGV của nó đang bận. Chuỗi sự kiện (test `Current_behaviour_a_reservation_of_a_leg_without_a_free_AGV_blocks_the_rack_that_AGV_carries`, ba scope khác nhau):

1. Lệnh 1 lấy rack đầy ở A. A trống.
2. Lệnh 3 (cấp rack rỗng cho A) giữ chỗ A ngay; AGV rack rỗng đang bận với lệnh 2.
3. Lệnh 2 lấy rack rỗng ở C, cần đặt vào A: A đã bị giữ.
4. Lệnh 2 giữ rack chờ A; lệnh 3 giữ A chờ AGV của lệnh 2. **Không bên nào thoát**, chỉ gỡ được bằng thao tác tay.

Điều kiện: dispatch Legacy (một AGV mỗi loại rack) và các đích thay thế đều đầy. Với RcsManaged, nguy cơ xuất hiện khi không còn AMR rảnh (suy luận, chưa tái hiện).

### F1 – `PER_AGV` không được kiểm tra theo chiều ngược lại (đã tái hiện, **nghiêm trọng**)

Engine lệnh xem Task đang chạy trước khi giao AGV cho chặng. Engine Task **không** xem chặng đang chạy. Tạo Task Phase 1 khi AGV đang chạy chặng → Task vẫn được điều phối (test `Current_behaviour_a_Phase_1_task_is_dispatched_to_an_AGV_that_already_carries_a_leg`). Vi phạm quy tắc được bảo vệ "tối đa một việc đang hoạt động mỗi AGV". Chỉ xảy ra khi dùng cả hai engine; Real cho Transport Order đang bị chặn.

### Nhóm cấp (`*-CAP`)

Chặng cấp chọn đích trước khi lấy nên không đứng giữ rack. Nhưng một scope cấp chứa hai chiều: yêu cầu đầu chờ đích trống có thể chặn yêu cầu sau là yêu cầu duy nhất lấy rack khỏi đích đó. **Suy luận từ code, chưa tái hiện**; điều kiện hẹp (không còn nguồn khác).

## 4. Đề xuất

Tách ba mối quan tâm mà scope hiện không giải quyết cùng lúc được.

| Mối quan tâm | Đề xuất |
|---|---|
| **Thứ tự (FIFO)** | Scope giữ đúng nghĩa "miền thứ tự". Áp dụng A2 cho nhóm cất |
| **Bế tắc** | R1: sửa F1. R2: chặng chỉ giữ chỗ đích khi **có AGV để thực hiện** (cấp phát AGV + rack + chỗ trong một giao dịch). R3: chỗ vừa trống ưu tiên cho AGV **đang giữ rack** trước chặng chưa bắt đầu |
| **Xung đột vật lý** | Giao cho điều khiển giao thông của RCS, **chỉ sau khi nhà cung cấp xác nhận bằng văn bản**. Nếu RCS không bảo đảm, WCS cần mô hình "vùng giao thông" có sức chứa; cần sơ đồ mặt bằng từ chủ dự án, không tự suy ra |

R2 và R3 đổi cách cấp phát, cần phê duyệt. R3 đổi thứ tự được duyệt ở D-13 trong một trường hợp. Chưa đề xuất nào được chứng minh là loại bỏ mọi bế tắc; khi được duyệt phải có test cho từng chuỗi ở trên.

| Phương án | Nội dung | Ưu | Nhược |
|---|---|---|---|
| **P-1 (đề xuất)** | A2 + R1 + R2 + R3 + xác nhận RCS | Xử lý hai chuỗi đã tái hiện | Phải sửa cấp phát |
| P-2 | Chỉ A2 | Không sửa code | Còn F1, F2 |
| P-3 | Mỗi nhóm một việc tại một thời điểm (khóa vùng) | Tránh đi ngược chiều | Thông lượng thấp; AGV giữ rack chờ đích trong vùng đang khóa vẫn bế tắc |
| P-4 | Chưa khai báo scope | An toàn: yêu cầu hàng đợi không được điều phối | Không vận hành được PH2 |

**Trạng thái đề nghị cho đến khi quyết:** P-4 (hiện trạng).

## 5. Cần quyết định

| Mã | Câu hỏi | Đề xuất |
|---|---|---|
| OQ-54a | Scope là miền thứ tự, không phải khóa vật lý? | Đồng ý |
| OQ-54b | Chọn P-1…P-4 | P-1 |
| OQ-54c | Duyệt sửa F1 (`PER_AGV`) ngay, tách khỏi OQ-54? | Duyệt |
| OQ-54d | Ai chịu trách nhiệm chống xung đột lối đi hai chiều? | RCS, chờ vendor xác nhận ([câu hỏi §3](ENGINEER_QUESTIONS_PLC_RCS_2026-11-26.md)) |
