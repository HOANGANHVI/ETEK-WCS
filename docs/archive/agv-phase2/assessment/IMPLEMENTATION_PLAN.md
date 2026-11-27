# AGV Phase 2 – Kế hoạch triển khai đề xuất

> **Toàn bộ nội dung dưới đây là [ĐX] đề xuất kỹ thuật, chưa được duyệt, chưa phải quyết định nghiệp vụ.** Không có dòng code, migration hay cấu hình nào được thay đổi trong Phase 0.
> Theo CLAUDE.md §3: mỗi hạng mục cần Change Request được duyệt trước khi sửa code/spec; §5: trước khi code phải có Change ID, phạm vi, file, migration, API, test hồi quy, rủi ro/rollback. Bảng dưới là **khung** để người dùng duyệt từng CR.
> Tham chiếu câu hỏi: OPEN_QUESTIONS.md (Q-xx). Trạng thái hiện tại: các tài liệu cùng thư mục.

## 1. Nguyên tắc

1. **Không đụng vào bất biến được bảo vệ:** PER_AGV, BP-04 REVIEW, WCS sở hữu tồn kho, A3/A4 & D1/D2 UNCHANGED, không bind rack tại "Không bind rack", không tạo cụm Station, không gắn trạng thái "đã đến đích" khi mới tạo/dispatch task (CLAUDE.md §2, §4).
2. **Tận dụng mô hình CR-010 sẵn có** (Order/Leg/Step/Candidate/Reservation/StationTransaction) – không viết lại module (CLAUDE.md §4 "không rewrite whole modules").
3. **Cổng PLC:** không triển khai giao diện 20/11 trước khi CR-029 được duyệt, bên PLC trả lời câu hỏi chặn, và kế hoạch migration được duyệt (§7.8). Mọi ghi PLC chỉ sau khi PLC engineer duyệt; commissioning read-only trước (§7.5).
4. **Adapter cô lập:** PLC/RCS vendor adapter tách khỏi logic nghiệp vụ; không hard-code IP/cổng/địa chỉ/mã robot/mã trạm.
5. **Idempotency & đồng thời** cho mọi tạo task/callback/cập nhật tồn kho/retry.
6. Dùng **feature flag/cấu hình** cho hành vi chưa hoàn tất; migration **cộng thêm**, không xóa/đổi tên dữ liệu.

## 2. Chặn và điều kiện tiên quyết (gate)

| Gate | Điều kiện | Phụ thuộc câu hỏi |
|---|---|---|
| **G0 – Phạm vi** | Người dùng cung cấp/duyệt tài liệu yêu cầu Phase 2; xác nhận phạm vi các tính năng | Q-01, Q-02, Q-03, Q-04, Q-05 |
| **G1 – Kiểm soát tài liệu PLC** | Xác định chủ sở hữu bản workbook 20/11 đã sửa; cập nhật CR-029 bằng bản đúng; PLC engineer trả lời các câu hỏi chặn | Q-09…Q-13, Q-17…Q-19 |
| **G2 – Mapping trạm** | Mapping PLC cho P1/P2/M5/F5 (hoặc xác nhận là vị trí không có PLC) | Q-06, Q-07 |
| **G3 – Xác nhận bàn giao (D-4)** | Quy tắc nguồn xác nhận pick/drop; nguồn rack presence/identity | Q-13, Q-14 |
| **G4 – Đối soát** | Thiết kế lối thoát đối soát cho Location/Order/Leg | Q-15 |

Hạng mục nào không phụ thuộc gate nào có thể làm sớm (phần H-1, H-2 bên dưới).

## 3. Các hạng mục đề xuất (mỗi hạng mục = một CR riêng)

Ký hiệu rủi ro: **T** thấp / **V** vừa / **C** cao. "Migration" = DB; "API" = hợp đồng ngoài; "Reg." = nhóm test hồi quy chính.

### H-1. (Không cần code) Cấu hình dữ liệu Phase 2 – sau G0/G2
- **Phạm vi:** khai báo Location `P1,P2,M5` (+ vị trí chờ nếu có) bằng chức năng Admin hiện có; import `PH2-01…06` thành Draft; chạy validate; **không publish** `PH2-04` (REVIEW).
- **File/module:** không (dữ liệu). **Migration:** không. **API:** không (dùng `processes/import-reference`, `process-versions/*`).
- **Reg.:** `Cr010ProcessConfigTests`. **Rủi ro:** T. **Rollback:** disable version.
- **Lưu ý:** Phase 2 đặt `noBind` chỉ sau Q-08; thứ tự ưu tiên trong nhóm chỉ sau Q-04.

### H-2. Lối thoát đối soát cho mô hình CR-010 – sau G4 (rủi ro vận hành cao nếu để trống)
- **Phạm vi:** dịch vụ + endpoint (Admin) để: (a) xóa `ReconciliationRequired` của Location sau đối soát có ghi `InventoryTransaction` điều chỉnh; (b) đưa Order/Leg ở `ReconciliationRequired` ra theo quyết định có kiểm soát (tiếp tục/thất bại), giải phóng reservation sau đối soát (đúng `inventory-management.md` 4.8).
- **File dự kiến:** `Application/Services/InventoryService.cs` (mở rộng, **không** đổi đường legacy), `Application/Routing/RouteEngine.cs` hoặc dịch vụ mới `RoutingReconciliationService`, `Api/Api/RoutingEndpoints.cs`, frontend trang Orders.
- **Migration:** thường không (dùng bảng sẵn có); có thể cần cột lý do/người duyệt – **cộng thêm**. **API:** thêm endpoint, không đổi endpoint cũ. **Reg.:** `Cr008b3CancelReconciliationTests`, `Cr008b4InventoryUncertaintyTests`, `Cr010MultiLegTests`.
- **Rủi ro:** V (đụng tồn kho). **Rollback:** feature flag tắt endpoint; dữ liệu cộng thêm không phá.

### H-3. Nguồn xác nhận Pick/Drop tự động – sau G1 + G3
- **Phạm vi:** thêm nguồn xác nhận (PLC/RCS/kết hợp) gọi `ConfirmPick/ConfirmDrop` có điều kiện D-4 (presence [+identity] + đúng Location + cập nhật WCS + lịch sử); `ConfirmationSource` mới nếu cần.
- **File:** `Application/Routing/RouteEngine.cs` (không đổi logic xác nhận, chỉ thêm nguồn), adapter mới trong Infrastructure, `Domain/Common/Enums.cs` (thêm giá trị enum cộng thêm).
- **Migration:** enum lưu dạng text → không cần; có thể cần bảng sự kiện nhận. **API:** có thể thêm callback. **Reg.:** `Cr010MultiLegTests`, `Cr028LegRobotTests`.
- **Rủi ro:** C (sai xác nhận làm lệch tồn kho). **Rollback:** feature flag về xác nhận toán tử.

### H-4. Driver PLC giao diện 20/11 (đọc trước, ghi sau) – sau G1 và duyệt CR-029
- **Phạm vi:** adapter đọc 6 tín hiệu/trạm theo cấu hình được duyệt (địa chỉ nằm trong cấu hình/DB, **không hard-code**), bộ chuyển trạng thái handshake theo định nghĩa chính thức; tích hợp `PlcRequestService`; heartbeat sau duyệt riêng.
- **File:** `Application/Plc/*` (driver/handler/handshake – thêm lớp mới cho 20/11, giữ lớp 10/11), `Infrastructure/Plc/*`, `Application/Routing/PlcRequestService.cs` (snapshot mới), catalog mẫu mới (không ghi đè catalog cũ).
- **Migration:** có (mẫu tín hiệu/ánh xạ mới, địa chỉ thực) – **cộng thêm**, kế hoạch migration phải được duyệt (§7.8). **API:** nội bộ; endpoint `plc-requests` có thể giữ.
- **Reg.:** `Plc/HandshakeTests`, `PlcRuntimeIntegrationTests`, `PlcWriteApprovalTests`, `Cr008b2PlcIntakeTests`, `Cr010MultiLegTests`.
- **Rủi ro:** C. **Rollback:** cờ `Integration:Mode`/tắt ghi; đọc-only; tắt worker.

### H-5. D-1: một giao dịch Handshake/PLC + FIFO/PLC – sau G1 (Q-11)
- **Phạm vi:** bộ điều phối giao dịch cấp PLC: hàng đợi FIFO **bền** theo PLC, tuần tự hóa handshake giữa các Station cùng PLC, các PLC song song.
- **File:** dịch vụ mới `PlcTransactionCoordinator` (Application), `Application/Routing/PlcRequestService.cs` (xếp hàng thay vì `PORT_BUSY` khi cổng/PLC bận – **thay đổi hành vi**), `Application/Routing/StationTransactionService.cs`, repository mới.
- **Migration:** có (bảng hàng đợi/vị trí hàng đợi, chỉ mục) – cộng thêm; **không** xóa index per-port ngay (giữ như lớp bảo vệ thứ hai).
- **Test phải đổi (đã nhận diện):** `Cr010MultiLegTests.cs:145` (Test07) mô tả mô hình cũ; cần CR cập nhật `ADR-0008` decision 4 (đã được D-1 thay một phần). **Reg.:** `Cr010MultiLegTests`, `ConcurrencyTests`, test restart.
- **Rủi ro:** C (đồng thời, deadlock, mốc "active"). **Rollback:** cờ cấu hình quay về per-port.

### H-6. Mô hình nhiều điểm dừng (nếu Q-02 chọn (b)/(c)) – chỉ khi được yêu cầu
- **Phạm vi:** mô hình `Mission → Operations[]` hoặc đa-rack; thay đổi hợp đồng `ILegDispatcher`/RCS.
- **Rủi ro:** C; ảnh hưởng schema, RCS contract, tồn kho (một AMR mang >1 rack). **Không khuyến nghị** làm trước khi có tài liệu RCS xác nhận hỗ trợ.

### H-7. Điều AMR tới vị trí chờ, hàng đợi buffer, timeout giữ chỗ – sau Q-04/Q-16/Q-20
- **Phạm vi:** kích hoạt `WaitingPositionCode` qua RCS (nếu RCS hỗ trợ), thứ tự chờ buffer, timeout reservation + cảnh báo.
- **Migration:** có thể (cột hạn giữ chỗ); **Rủi ro:** V.

### H-8. Merge bộ gửi RCS thật (CR-030) – quyết định của người dùng (Q-22)
- Đã có ở `feature/cr-030-real-rcs-dispatch` (chưa merge); cần review riêng, test `Cr028*`. Phase 2 thật phụ thuộc hạng mục này.

## 4. Thứ tự đề xuất

```
G0 ──> H-1 (dữ liệu)                                   [có thể bắt đầu sớm, không code]
G4 ──> H-2 (đối soát CR-010)                           [đóng lỗ hổng vận hành]
G1+G3 ─> H-3 (xác nhận tự động) ─┐
G1 ───> H-4 (driver 20/11, read-only trước) ─┬─> H-5 (D-1 FIFO/PLC)
Q-22 ──> H-8 (RCS thật)                       └─> nghiệm thu commissioning read-only → ghi có kiểm soát
(Q-02) ─> H-6 (chỉ khi cần)      H-7 (sau cùng)
```

## 5. Rủi ro tổng thể (đã nhận diện từ code)

| ID | Rủi ro | Chứng cứ | Giảm thiểu đề xuất |
|---|---|---|---|
| R-01 | Hiểu sai giá trị tín hiệu (`PLC_MissionCompleted` 1=thất bại) | PLC_HANDSHAKE §6 | Bảng định nghĩa giá trị chính thức, test từng giá trị, enum kiểu mạnh |
| R-02 | Khóa PLC quá rộng/hẹp tùy cách hiểu "Handshake hoạt động" | Q-11; `StationTransaction.cs:71-89` | Chốt định nghĩa trước H-5; mô phỏng hiệu năng trước khi chọn |
| R-03 | Location bị khóa đối soát vĩnh viễn | RACK_INVENTORY §3 | Làm H-2 trước khi bật PLC→Order thật |
| R-04 | Xác nhận drop chỉ do toán tử → Leg không tự hoàn tất | MISSION §1.3 | H-3 |
| R-05 | Order `ReconciliationRequired` không có lối ra | MISSION §6 | H-2 |
| R-06 | Hai mô hình điều phối song song gây hiểu nhầm/hai nguồn sự thật | ARCHITECTURE §2; Q-21 | Quyết định cutover, tài liệu rõ phạm vi |
| R-07 | Test hiện hành mâu thuẫn D-1 (Test07) | `Cr010MultiLegTests.cs:145` | Cập nhật có CR, không sửa lặng lẽ |
| R-08 | Workbook điều khiển bị sửa ngoài CR | Q-10 | Khóa quy trình thay đổi; so sánh hash trong CI/tài liệu |
| R-09 | Tải: quét 200 Order/50 Leg mỗi tick | `RoutingRepository.cs:62-70` | Đo trước khi mở rộng |
| R-10 | Giữ chỗ buffer không hết hạn | `Reservation.cs` | H-7 |

## 6. Xác nhận trước khi viết CR đầu tiên (đề xuất cho người dùng duyệt)

1. Duyệt danh sách gate G0–G4 và thứ tự H-1…H-8 (hoặc chỉnh).
2. Trả lời các câu hỏi **B** trong OPEN_QUESTIONS (Q-01, Q-02, Q-06, Q-09…Q-15) – chặn thiết kế.
3. Chỉ định bản workbook PLC chuẩn (Q-10) và có mở CR cập nhật CR-029 hay không.
4. Cho biết hạng mục nào nên mở CR đầu tiên (đề xuất: **H-2 đối soát** vì không phụ thuộc PLC và đóng một lỗ hổng vận hành hiện hữu; song song **H-1** khi G0/G2 xong).
