# AGV Phase 2 – Đánh giá kiểm thử

> **Audit này không chạy bất kỳ test nào** (không build, không DB, không PLC/RCS). Mọi nhận định dưới đây là từ **đọc mã test**; kết quả pass/fail hiện tại **Not Verified**. Số lượng là số thuộc tính `[Fact]/[Theory]` đếm được (`grep`), không phải số test đã chạy.
> Đường dẫn test từ `tests/WCS.Tests/`.

## 1. Quy mô và cấu trúc

- Tổng số thuộc tính `[Fact]/[Theory]` trong dự án test: **787**.
- Hạ tầng: `Support/Harness.cs`, `Support/RoutingKit.cs`, `Support/Fakes.cs`, DatabaseFixture (PostgreSQL thật cho test tích hợp), simulator RCS/PLC (`MelsecMcMockServer`, `RcsSimulator`).
- Test tích hợp phụ thuộc DB: cần PostgreSQL cục bộ (xem `docs/DEPLOYMENT_TEST_CHECKLIST.md`, `docs/TESTING_REPORT_CR-002.md` – không đọc trong audit này).

## 2. Các test hiện có liên quan trực tiếp đến Phase 2

| Chủ đề | Test (file:dòng) | Bao phủ | Nhận xét |
|---|---|---|---|
| Order/Leg hợp lệ chuyển trạng thái, snapshot process | `Domain/Cr010DomainTests.cs:129` (13 test miền) | Bảng cạnh Order, snapshot | Tốt |
| Leg đã pick không về Ready; không drop trước pick | `Domain/Cr010DomainTests.cs:144` | Bất biến Leg | Tốt |
| Station Transaction: hoàn tất chỉ sau handover + AMR rời; không đi lùi; step 1..9; Request_ID ≤254; reconciliation giữ cổng; suspend không hủy | `Domain/Cr010DomainTests.cs:17,33,44,55,62,74` | Bất biến giao dịch | Dùng mô hình **9 bước 10/11** |
| Khôi phục khi PLC nối lại (bảng quyết định) | `Domain/Cr010DomainTests.cs:92,99,108,117` | `HandshakeRecovery` thuần | Chỉ test hàm thuần; nguồn quan sát thật là `UnavailablePlcObservationSource` |
| Luồng chính: A1 → B1 trực tiếp; buffer; tiếp tục từ buffer | `Integration/Cr010MultiLegTests.cs:33,57,81` | Order/Leg/Buffer | Dùng simulator dispatcher |
| Giao dịch trạm xong khi Task còn Waiting; cổng nhận yêu cầu mới sau khi AMR rời | `Cr010MultiLegTests.cs:108,125` | Tách vòng đời Transaction ↔ Order | |
| **Hai cổng cùng PLC giữ hai giao dịch đồng thời** | `Cr010MultiLegTests.cs:145` (Test07) | Mô hình per-port | **Mâu thuẫn D-1** – sẽ phải đổi bằng CR |
| Hai PLC khác nhau song song | `Cr010MultiLegTests.cs:162` (Test08) | Độc lập PLC | Phù hợp D-1 |
| Hai yêu cầu cùng cổng không tạo hai giao dịch active | `Cr010MultiLegTests.cs:179` (Test09) | Race/PORT_BUSY | Xác nhận hành vi "từ chối", không phải FIFO |
| Trùng Request_ID | `Cr010MultiLegTests.cs:193` (Test14) | Idempotency | Phụ thuộc `Request_ID` (không có trong 20/11) |
| PLC và tồn kho lệch → đối soát, không dispatch | `Cr010MultiLegTests.cs:210` (Test12) | Reconciliation | |
| Không có đích + không buffer → AMR giữ rack, tồn kho không đổi | `Cr010MultiLegTests.cs:228` (Test13) | WaitKind/HoldWithRack | |
| Không dùng vị trí ngoài ứng viên; PER_AGV 2 Leg không chạy cùng lúc; DB từ chối 2 Leg giữ AGV | `Cr010MultiLegTests.cs:257,273,295` | Ràng buộc | |
| Khởi động lại giữa quy trình nhiều Leg | `Cr010MultiLegTests.cs:318` | Resume | |
| Hủy bị từ chối khi rack trên AMR | `Cr010MultiLegTests.cs:340` | Hủy an toàn | |
| Cấu hình process, version, validator, REVIEW, protected locations | `Integration/Cr010ProcessConfigTests.cs` (18) — các dòng 25,68,92,111,123,133,142,158,175,189,202,214,226,236,249,285,294,337 | Process-as-data | Có test nói rõ **"phase 2 needs its own locations"** (dòng 202) |
| Vị trí PLC ngoài 1–4 bị từ chối, không thay thế | `Cr010ProcessConfigTests.cs:226` | Không suy diễn mã vị trí | |
| PLC ngắt kết nối giữ giao dịch, nối lại đối soát | `Cr010ProcessConfigTests.cs:294` (Test10) | Recovery (có giả lập nguồn quan sát) | |
| Hạn mức per-AGV & đồng thời DB | `Integration/ConcurrencyTests.cs:18,43,68,90,109,126` | PER_AGV legacy, slot đích cuối chỉ cấp cho một | |
| Intake yêu cầu trạm (legacy) bền vững/idempotent/retry | `Integration/Cr008b2PlcIntakeTests.cs:74-273` (12) | Legacy `StationRequestProcessor` | Không phải đường CR-010 |
| Bất định tồn kho / đối soát (legacy) | `Cr008b4InventoryUncertaintyTests.cs` (9), `Cr008b3CancelReconciliationTests.cs` (8) | `InventoryService` legacy | **Không phủ Order/Leg CR-010** |
| Handshake framework (observation, rule được duyệt, không ghi khi chưa duyệt) | `Plc/HandshakeTests.cs` (12; ví dụ `:97,125,160,193,221,233,265`) | Khung 10/11 | Dùng tên tín hiệu 23-mẫu cũ |
| Dispatch RcsManaged/Leg robot | `Integration/Cr028DynamicDispatchTests.cs` (18), `Cr028LegRobotTests.cs` (13), `Cr028ModeAndLegTests.cs` (11) | Mô hình CR-028 | |

## 3. Khoảng trống kiểm thử so với Phase 2

| # | Khoảng trống | Hệ quả | Liên quan |
|---|---|---|---|
| T-01 | Không có test nào cho tín hiệu/handshake giao diện 20/11 (grep `MissionRequest|PLC_Mission`: 0 kết quả) | Đúng với cổng triển khai; cần bộ test mới khi CR-029 duyệt | PLC_HANDSHAKE |
| T-02 | Không có test cho FIFO cấp PLC / một giao dịch mỗi PLC (D-1) | Chưa có hành vi để test; Test07 phải được cập nhật có kiểm soát | H-5 |
| T-03 | Không có test cho lối thoát đối soát của Location/Order trong mô hình CR-010 (do chưa có chức năng) | Rủi ro R-03/R-05 không có lưới an toàn | H-2 |
| T-04 | Không có test cho `PH2-01…06` chạy end-to-end (chỉ test import Draft và xác nhận "cần Location riêng", `Cr010ProcessConfigTests.cs:202`) | Phase 2 chưa được kiểm chứng như luồng | H-1 |
| T-05 | Chuỗi BufferStorage → Waiting → BufferRetrieval **đã** được test chung (`Cr010MultiLegTests.cs:57,81`, bộ dựng `Support/RoutingKit.cs:170-172`) nhưng **không** theo quy trình tham chiếu Phase 2 (A→B/P1/P2→M5) | Phase 2 chưa được kiểm chứng đúng kịch bản | H-1 |
| T-06 | Xác nhận pick/drop tự động (PLC/RCS) chưa có | `ConfirmPick/Drop` chỉ test với nguồn Operator | H-3 |
| T-07 | Không có test hiệu năng/tải cho quét 200 Order/50 Leg mỗi tick | R-09 | |
| T-08 | Không có test tuần tự hóa nhiều PLC dưới tải (4 PLC × 4 trạm) thật; `PlcWorkerTests` chỉ 11 test đơn vị | | H-5 |
| T-09 | Không có test cho giữ chỗ buffer treo (timeout) | R-10 | H-7 |
| T-10 | Không có test bảo toàn bất biến được bảo vệ cho Phase 2 (A3/A4, D1/D2 không bị process cấu hình dùng) ngoài `Cr010ProcessConfigTests.cs:214` | Đã phủ cơ bản | |
| T-11 | Test frontend: các `scripts/*-smoke.ts` kiểm giao diện, không phủ Order/Phase 2 | | |

## 4. Đề xuất bộ test hồi quy cho từng hạng mục (xem IMPLEMENTATION_PLAN)

| Hạng mục | Test hồi quy bắt buộc chạy | Test mới cần |
|---|---|---|
| H-1 | `Cr010ProcessConfigTests` | Import + validate + publish PH2 trên Location mẫu; PH2-04 không publish được |
| H-2 | `Cr008b3*`, `Cr008b4*`, `Cr010MultiLegTests` | Giải quyết Location/Order bị đối soát: giải phóng reservation đúng một lần, ghi `InventoryTransaction`, phân quyền Admin |
| H-3 | `Cr010MultiLegTests`, `Cr028LegRobotTests` | Mỗi nguồn xác nhận; trùng lặp (Duplicate); xác nhận khi vị trí sai bị từ chối |
| H-4 | `Plc/HandshakeTests`, `PlcRuntimeIntegrationTests`, `PlcWriteApprovalTests`, `Cr008b2PlcIntakeTests` | Từng giá trị tín hiệu (đặc biệt `PLC_MissionCompleted` 1/2), reset, timeout, ghi bị chặn khi chưa duyệt |
| H-5 | `Cr010MultiLegTests`, `ConcurrencyTests` | FIFO bền qua khởi động lại; 4 PLC song song; không hai Station cùng PLC handshake cùng lúc; cập nhật Test07 |
| H-6/H-7 | Toàn bộ nhóm Cr010, Cr028 | Theo thiết kế được duyệt |

## 5. Điều không thể xác minh

- Trạng thái xanh/đỏ hiện tại của 787 test (không chạy).
- Độ tin cậy của test phụ thuộc DB/timing (một số dùng đồng hồ cố định – `Harness`).
- Khả năng test chạy trên môi trường triển khai thật.
