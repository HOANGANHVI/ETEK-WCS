# CR-039 – Danh mục quy trình vận chuyển mặc định

| | |
|---|---|
| **Trạng thái** | `IMPLEMENTED – TESTED (Simulator) – NOT DEPLOYED – NOT READY FOR REAL OPERATION` |
| **Nguồn phê duyệt** | Yêu cầu và 4 quyết định của chủ dự án ngày 2026-11-25 (mục 2) |
| **Căn cứ** | Slide 4–9 PPTX Phase 2, `PHASE2_PROCESS_FLOWS.md`, `PHASE2_OPERATIONAL_SPEC.md` (D-09, D-11, D-12), CR-035, CR-036, CR-037 |
| **Migration** | `AddProcessRequiresReadiness` (thêm cột `business_processes.RequiresReadiness`, mặc định `false`). **Chưa chạy trên database nào ngoài database test** |

## 1. Nguyên nhân danh sách tạo nhiệm vụ thiếu quy trình

1. Dropdown ở **Quản lý nhiệm vụ** và **Vận hành RCS** chỉ đọc `GET /api/flows`, tức cấu hình cố định `ProcessFlows` trong appsettings: đúng 5 luồng Phase 1 (BP-01, 02, 03, 05, 06). Quy trình Published trong database không bao giờ xuất hiện ở đó.
2. Không quy trình Phase 2 nào được seed. Dữ liệu tham chiếu chỉ được nạp dạng Draft khi Admin bấm import.
3. Location P1–P4, M5, M6, X, Y, Z chưa tồn tại (CR-037 chủ ý không seed), nên các quy trình đó không qua được kiểm tra Publish.
4. Dữ liệu tham chiếu thiếu toàn bộ nhóm X, và PH2-04 còn cờ REVIEW dù D-09 đã chấp thuận.

## 2. Quyết định của chủ dự án (2026-11-25)

| # | Câu hỏi | Quyết định |
|---|---|---|
| Q1 | Sức chứa khởi đầu | Vị trí buffer P1–P4 = 1; QC thủ công M5/M6 = 2; trạm = 1 (Admin sửa được) |
| Q2 | Điều kiện Publish | Published + NotReady (D-11): kiểm tra port / mapping PLC chuyển sang Readiness |
| Q3 | Nhóm 2 và nhóm 4 | Thêm **X3/X4 ↔ Z1/Z2 tuyến thẳng** (đích bận thì chờ). A3/A4 ↔ D1/D2 **không** thêm, vẫn được bảo vệ |
| Q4 | QC thủ công | Theo yêu cầu mới: rack đầy **và rỗng** đều ra M5/M6 khi buffer đầy; không công bố quy trình lấy rack ra khỏi M5/M6 (PH2-05 để Draft); PH2-06 vẫn công bố |

**Đính chính khi trình bày Q2:** PLC catalog seed **chưa liên kết** port với Location, nên điều kiện mapping chỉ chặn Publish ở hệ thống mà Admin đã liên kết port. Trên bản mới, rào chắn thực tế là Readiness: trạm cần quyền PLC mà chưa có port thì không đặt Ready được.

## 3. Danh mục mặc định

Mọi quy trình dưới đây là **dữ liệu** (`reference-processes.json`, `defaultSinceVersion`), không có mã quy trình hay tên trạm trong logic.

| Mã | Yêu cầu / rack | Trạm gọi | Nguồn → đích (theo ưu tiên) | Sau triển khai |
|---|---|---|---|---|
| PH2-01 | Pickup / đầy | A1, A2 | trạm gọi → B1,B2 → P1,P2 → M5 | Published, NotReady |
| PH2-02 | Delivery / rỗng | A1, A2 | B1,B2 → P1,P2 → trạm gọi | Published, NotReady |
| PH2-03 | Pickup / rỗng | B1, B2 | trạm gọi → A1,A2 → P1,P2 → M5 | Published, NotReady |
| PH2-04 | Delivery / đầy | B1, B2 | A1,A2 → P1,P2 → trạm gọi | Published, NotReady |
| PH2-05 | Manual / rỗng | M5 | M5 → A, P | **Draft** (OQ-39-04) |
| PH2-06 | Manual / đầy | M5 | P1,P2 → M5 (không có rack: lỗi) | Published, NotReady |
| PH2-07 | Pickup / đầy | X1, X2 | trạm gọi → Y1,Y2 → P3,P4 → M6 | Published, NotReady |
| PH2-08 | Delivery / rỗng | X1, X2 | P3,P4 → trạm gọi | Published, NotReady |
| PH2-09 | Pickup / rỗng | Y1, Y2 | trạm gọi → X1,X2 → P3,P4 → M6 | Published, NotReady |
| PH2-10 | Delivery / đầy | Y1, Y2 | P3,P4 → trạm gọi | Published, NotReady |
| PH2-11 | Pickup / đầy | X3, X4 | trạm gọi → Z1,Z2 (chờ) | Published, NotReady |
| PH2-12 | Pickup / rỗng | Z1, Z2 | trạm gọi → X3,X4 (chờ) | Published, NotReady |

Phase 1 (BP-01, 02, 03, 05, 06) **không đổi**: vẫn chạy qua `POST /api/tasks` và cấu hình `ProcessFlows`. BP-04 vẫn REVIEW.

PH2-08 và PH2-10 chỉ lấy từ buffer P3/P4 (phần được nêu rõ). Việc có tìm ở trạm đối diện trước như PH2-02/04 hay không là OQ-39-02.

## 4. Hành vi mới

- **Hai cổng (D-11).** `PORT_DISABLED`, `PLC_DISABLED`, `PORT_MAPPING_NOT_APPROVED` thành cảnh báo khi Publish và đánh dấu quy trình `RequiresReadiness` (một chiều). Quy trình có cờ này chỉ nhận yêu cầu khi version **Ready** và điều kiện thiết bị vẫn đạt lúc nhận, không phụ thuộc `RouteEngine:EnforceReadiness`. Đặt Ready bị từ chối (`READINESS_CONDITIONS_UNMET`) nếu điều kiện chưa đạt, gồm cả `STATION_PORT_MISSING`.
- Quy trình không có cờ giữ nguyên hành vi cũ.
- **Seed** (`Seed:DefaultProcesses`, mặc định bật): một lần cho mỗi phiên bản danh mục, một transaction với advisory lock; mã đã tồn tại thì không đụng tới; quy trình Admin đã tắt không được khôi phục; thiếu Location hoặc trùng trigger với process đang Published thì để Draft kèm lý do trong audit.
- **Location catalog v2:** X1–X4, Y1, Y2, Z1, Z2, P1–P4, M5, M6 (M5/M6 mang `StationRole=ManualQcStation`).
- **API** `GET /api/routing/startable-processes`: mọi quy trình Published, kèm `readiness`, `acceptsRequests`, `blockedCode`, `blockedDetail`.
- **UI:** hai form tạo yêu cầu liệt kê luồng Phase 1 và mọi quy trình Published; quy trình chưa sẵn sàng vẫn hiện, có nhãn và không bấm tạo được. Quy trình Published tạo **Transport Order** (theo dõi ở trang Lệnh vận chuyển).
- **ConfigTransfer:** xuất/nhập `requiresReadiness` (chỉ ghi khi bật; import không bao giờ gỡ).

## 5. Triển khai và rollback

1. Sao lưu database, chạy migration, rồi khởi động backend: seed Location v2 và danh mục quy trình chạy lúc start.
2. Database **đã** import PH2-01…06 trước đây: các mã đó được giữ nguyên (Draft, PH2-04 còn cờ REVIEW) – Admin phải xử lý thủ công.
3. Để vận hành: kỹ sư PLC xác nhận địa chỉ; Admin liên kết port với Location, duyệt mapping; Admin đặt Ready bằng `PUT /api/process-versions/{id}/readiness` (**chưa có màn hình**).
4. Rollback migration bị từ chối (`ROLLBACK_REFUSED`) khi còn quy trình Published có cờ: tắt các quy trình đó trước. Tắt seed: `Seed:DefaultProcesses=false`.

## 6. Phần chưa định nghĩa (OQ)

| ID | Câu hỏi | Liên quan |
|---|---|---|
| OQ-39-01 | A3/A4 ↔ D1/D2: có thay quy trình cũ không; buffer / QC thủ công nào | CLAUDE.md §2, P2-OQ-01 |
| OQ-39-02 | Yêu cầu Delivery ở X1/X2 và Y1/Y2: có tìm rack ở trạm đối diện trước buffer không; thứ tự P3/P4 | PH2-08, PH2-10, OQ-26 |
| OQ-39-03 | Nhóm X3/X4 ↔ Z1/Z2: buffer, QC thủ công, yêu cầu Delivery; M6 có dùng cho nhóm này không | PH2-11, PH2-12 |
| OQ-39-04 | Tiếp nhận lại rack từ M5/M6 vào luồng (PH2-05) | P2-OQ-07 |
| OQ-39-05 | Gọi rack đầy thủ công tới M6 (tương đương PH2-06) có cần không | OQ-09 |
| OQ-39-06 | Y1/Y2, Z1/Z2 thuộc PLC nào; X4 hay X6 | OQ-07, OQ-29 |
| OQ-39-07 | Sức chứa thực của từng vị trí (giá trị hiện là khởi đầu) | OQ-11 |
| OQ-39-08 | Cắt chuyển BP-01…06 và PH2-01…06 cùng trigger tại A1/A2, B1/B2 (hai đường tạo yêu cầu song song) | OQ-28 |
| OQ-39-09 | Không bind rack ở Phase 2 | OQ-22 |
| OQ-39-10 | PH2-06 không có rack: báo lỗi hay chờ | OQ-17 |

## 7. Giới hạn đã biết

- **FIFO:** scheduler xử lý lệnh đang mở theo thời điểm tạo (cũ trước) và rack trong một vị trí theo `LastConfirmedAt`. **Chưa có** Queue / Scope / FIFO nghiêm ngặt (D-13); một yêu cầu mới có thể lấy chỗ vừa trống trước lượt scheduler của lệnh đang chờ.
- **Origin** của rack (D-02) chưa hiện thực: rack ở buffer dùng chung được chọn theo loại và thứ tự cũ nhất.
- "Không tự đưa rack quay lại sau QC": danh mục mặc định không lấy từ M5/M6. Chặn cứng với quy trình người dùng tự tạo cần `ModelIsolation:Mode=Enforce` (CR-036).
- Product Model chưa gán cho Location mới.
- Real RCS: quy trình chọn đích sau khi lấy (`AfterPick`) và mapping RCS của các Location mới chưa được cấu hình; chưa kiểm chứng với thiết bị thật.
- Vị trí chờ trung gian chưa được điều AMR tới (OQ-14).
