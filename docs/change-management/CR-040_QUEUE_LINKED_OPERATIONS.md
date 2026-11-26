# CR-040 – Hàng đợi FIFO, liên kết ba trang vận hành, mô phỏng PLC, bản ghi Rack

| | |
|---|---|
| **Trạng thái** | `IMPLEMENTED – TESTED (Simulator / mock) – NOT DEPLOYED – NOT READY FOR REAL OPERATION` |
| **Nguồn phê duyệt** | Chỉ thị của chủ dự án ngày 2026-11-25 (mục A, B, C, E của task tiếp nối CR-039); D-12, D-13 trong `PHASE2_OPERATIONAL_SPEC.md` |
| **Migration** | `AddTransportQueue` (sequence `transport_queue_seq`; thêm cột `transport_orders.QueueSequence/QueueScope/QueueHeld`, `business_processes.QueueScope`). Cùng `AddProcessRequiresReadiness` (CR-039). **Chưa chạy trên database nào ngoài database test** |

## 1. Hiện trạng trước CR-040

Đã có từ CR-039: danh mục PH2, Published + NotReady cho quy trình mặc định, dropdown liệt kê quy trình Published, PH2-05 Draft.
Chưa có: Queue/FIFO/Held/Scope; PLC Simulation còn đòi `PlcSimulation:Allowed` và RCS thật; chưa mô phỏng theo từng IO; chưa sửa/xóa từng Rack; ba trang không hiển thị chung một danh sách yêu cầu; chưa có màn hình đặt Ready.

## 2. Hàng đợi (D-12, D-13)

- **Thành viên:** mọi yêu cầu của quy trình có cờ `RequiresReadiness`. Mọi version Publish **từ nay** đều đặt cờ (`RouteEngine:NewPublicationsRequireReadiness`, mặc định `true`). Quy trình đã Publish trước đó và không có cờ giữ nguyên hành vi, **không** vào hàng đợi.
- **Nhận vào:** Published + Ready (và điều kiện thiết bị) → trong một transaction: ghim Process Version, cấp `QueueSequence` (một lần, không đổi). Cùng RequestKey trả lại cùng yêu cầu.
- **Scope:** Admin khai báo `QueueScope` cho quy trình (`PUT /api/processes/{id}/queue-scope`, có lý do, audit; trả về các quy trình khác scope đang dùng chung vị trí). **Không scope nào được khai báo mặc định.** Chưa khai báo → yêu cầu chờ `SCOPE_UNDETERMINED`, không giữ chỗ, không điều phối (OQ-54).
- **FIFO nghiêm ngặt:** trước khi cấp Rack nguồn, đích / chỗ Buffer, chặng tiếp theo, và trước khi gửi chặng cho hệ AGV: nếu còn yêu cầu cũ hơn cùng scope đang cần tài nguyên (chưa điều phối, đang giữa hai chặng, hoặc chặng chưa có đích) thì yêu cầu mới chờ. Quyết định được khóa theo scope trong transaction cấp tài nguyên. Scope khác chạy song song.
- **Held:** version ghim chuyển Suspended (hoặc quy trình bị tắt, version bị Disable không có bản thay) trước khi điều phối → Held, giữ `QueueSequence`; Ready lại → trở về chờ đúng vị trí, **không** điều phối ngay trong lượt đó. Yêu cầu đã điều phối không bị giữ hay hủy. Version mới Publish không đổi version đã ghim (D-10).

## 3. PLC Simulation

- Điều kiện bật: **chỉ Admin + xác nhận** (`confirmed`). Bỏ `PlcSimulation:Allowed` và yêu cầu `Rcs:RealDispatch:Enabled` (khóa cấu hình cũ bị bỏ qua).
- Không đổi điều kiện gửi lệnh RCS: lệnh đi theo Mode (CR-038). Ở chế độ AGV thật, yêu cầu mô phỏng được nhận **có thể** điều AMR thật: giao diện cảnh báo và đòi xác nhận khu vực an toàn.
- Mô phỏng từng tín hiệu `PLC → WCS` của trạm (đọc từ cấu hình IO, không sửa mapping), giữ trong bộ nhớ, xóa khi tắt mô phỏng. "Gửi tín hiệu" dựng yêu cầu bằng `PlcRequestAssembler` và đưa vào **`IPlcRequestService`** – cùng bộ tiếp nhận của PLC.
- Chỉ `Pickup_Request, Delivery_Request, Request_ID, Rack_Full, Rack_Empty, Destination_Available, Rack_Type` được đọc; tín hiệu khác chỉ hiển thị. Trạng thái handshake xem ở Station Transaction.
- Không ghi xuống PLC. Từ chối port có PLC đang online.
- **Chưa nối:** runtime PLC thật chưa tự gọi bộ tiếp nhận **của engine lệnh** (ngữ nghĩa cạnh/mức/reset chưa được duyệt, CR-029). *Đính chính (CR-042): đường PLC thật sang engine task đã tồn tại, tắt mặc định.*

## 4. Bản ghi Rack (Admin)

`POST/PUT/DELETE /api/inventory/racks`. Bắt buộc lý do. Từ chối: đang giữ chỗ, đang vận chuyển, thuộc lệnh chưa kết thúc, xử lý thủ công, trạng thái chưa xác nhận, vị trí tắt/cần đối soát, sai loại, hết chỗ (tính cả chỗ đã giữ), trùng mã. Mỗi thay đổi ghi `InventoryTransaction`; xóa bản ghi rack không xóa lịch sử.

## 5. Liên kết ba trang

- Danh sách "Tất cả yêu cầu vận chuyển" dùng chung ở Quản lý nhiệm vụ, Vận hành RCS và Điều phối đa chặng: task Phase 1 và Transport Order theo **mã yêu cầu**, trạng thái, hàng đợi, số chặng, AGV, liên kết sang trang chi tiết (`/tasks?id=`, `/routing?order=`).
- Chi tiết lệnh: mã yêu cầu, người yêu cầu, hàng đợi, lý do, chặng, AGV, **lệnh RCS và kết quả** theo mã yêu cầu của từng chặng, giao dịch trạm, lịch sử.
- Không tạo luồng Task mới: Vận hành RCS gọi cùng API.
- Trang Quy trình: đặt Ready / tạm đình chỉ và khai báo scope.

## 6. Nội dung chưa quyết – `DEFERRED - PENDING USER APPROVAL`

| ID | Nội dung | Cách làm tạm (an toàn) |
|---|---|---|
| OQ-54 | Cách xác định Queue Ordering Scope và giá trị cho các quy trình | Admin khai báo tay; mặc định không khai báo → không điều phối |
| OQ-58 | Held ở đầu scope có chặn yêu cầu sau không | Chặn (không vượt) |
| OQ-45 | Version đã ghim bị Disable | Held nếu không có version thay |
| OQ-49 | Ranh giới Prepared / Dispatched | Bản ghi Transport Order được tạo khi nhận vào hàng đợi (là yêu cầu gốc); chặng chỉ tạo khi tới lượt. Khác đặc tả §2.5 (tạo Order khi dispatch) |
| OQ-55 | Phân xử khe giao dịch PLC giữa các scope; một giao dịch/PLC | Chưa làm; vẫn một giao dịch/port |
| OQ-40-01 | Dropdown chỉ đọc database: BP-01…06 vẫn lấy từ `ProcessFlows` (appsettings) vì chúng chạy bằng engine task cũ, chưa là quy trình Published | Giữ hai nhóm trong dropdown; không chuyển BP khi chưa có quyết định (liên quan OQ-39-08) |
| OQ-39-01…10 | Giữ nguyên (A3/A4↔D1/D2, thứ tự lấy rack nhóm X/Y, BP↔PH2 tại A1/A2, …) | Không tự giải quyết |

## 7. Rủi ro và giới hạn

1. **Mọi quy trình PH2 chưa chạy được cho tới khi Admin: liên kết port, duyệt mapping, khai báo scope, đặt Ready.**
2. FIFO chỉ giữa các yêu cầu **trong hàng đợi**. Task BP Phase 1 và quy trình cũ không có cờ có thể lấy chung vị trí (A, B) trước.
3. Đưa hai chiều ngược nhau (ví dụ PH2-01 và PH2-03) vào **cùng một scope** có thể **bế tắc**: yêu cầu cũ chờ chỗ mà chỉ yêu cầu mới giải phóng được. Hệ thống hiển thị "chờ sau yêu cầu …"; người vận hành hủy yêu cầu hoặc đổi cách chia scope. Cần xét khi quyết OQ-54.
4. Lệnh ở `ReconciliationRequired` không chặn hàng đợi.
5. Rack theo Origin (D-02) chưa hiện thực.
6. Quy trình Publish mới nay cần Ready (thay đổi hành vi so với trước).
7. Chưa kiểm chứng giao diện trên trình duyệt, PLC, RCS/AMR thật.

## 8. Triển khai và rollback

Sao lưu → chạy hai migration → khởi động backend mới. Rollback `AddTransportQueue` bị từ chối khi còn yêu cầu hàng đợi chưa kết thúc; `AddProcessRequiresReadiness` bị từ chối khi còn quy trình Published có cờ.
