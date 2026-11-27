# ETEK WCS – QA và kiểm thử Simulator (2026-11-27)

Nền: `main` `9f57903`, phiên bản `2026.11.26.4`, cộng hai bản sửa của ([CR-049](../../change-management/CR-049_QA_WAITING_REASON_IO_COUNTS.md)).

**Môi trường:** database mới riêng, backend và web riêng (cổng 5111/5110), chỉ Simulator; đã xóa sau khi kiểm thử. Database và tiến trình đang dùng không bị thay đổi; dữ liệu của chúng chỉ được đọc. Không PLC, AGV, RCS thật; Real Write và Real Dispatch tắt. Dữ liệu và cấu hình trong môi trường QA (liên kết cổng, bật PLC giả lập, duyệt đọc IO, quy trình Ready, queue scope) do kiểm thử tạo.

## A. Kiến trúc Simulator

### PLC

Có **hai cơ chế tách rời**:

| Cơ chế | Vị trí | Luồng |
|---|---|---|
| PLC giả lập tầng IO | `SimulatedPlcNetwork`, `SimulatedPlcEndpoint` (`Infrastructure/Plc/PlcRuntimeInfrastructure.cs`) | PlcWorker đọc/ghi giá trị trong bộ nhớ theo PLC + trạm + tín hiệu. Admin đặt giá trị, ngắt PLC, gây lỗi trạm qua `/api/plc/simulator/*` (không có trong Production) |
| Trang Mô phỏng PLC | `PlcSimulationService`, `PlcInputHandler`, `PlcRequestService` | Không đi qua PLC: gửi yêu cầu theo quy trình + vị trí vào cùng cổng nhận yêu cầu của engine |

Không thành phần nào của WCS tiêu thụ tín hiệu của IO Map 22/11. `PlcRequestAssembler` đọc tên tín hiệu của IO Map cũ (`Pickup_Request`, `Request_ID`…); `HandshakeCoordinator` báo `NotImplemented`; không có HeartBit, không có reset hay timeout handshake.

### RCS

`RcsSimulator` (`Infrastructure/Simulation/Simulators.cs`) trong bộ nhớ, đi qua `IRcsClient` / `ILegDispatcher` và được ghi vào `rcs_messages`. Có: tạo, tiếp tục, hủy, truy vấn nhiệm vụ; đội AMR tĩnh (`Rcs:Fleet:SimulatorSize`); tiêm lỗi chỉ từ mã test. Không có: AMR di chuyển, tự Pick/Drop/Completed, callback, Parent Task và trigger. Tiến trình do `SimulationDriver` (engine nhiệm vụ, `/api/simulator/tasks/{id}/advance`) hoặc xác nhận thủ công Pick/Drop/"AMR rời cổng" (engine lệnh vận chuyển).

## B. Ma trận khả năng

| Hạng mục | Đã triển khai | Chạy thực tế | Kiểm thử E2E | Kết quả |
|---|---|---|---|---|
| PLC Simulator (kết nối, đọc, ghi, ngắt, lỗi trạm) | Có | Có | Có | PASS |
| PLC Handshake (22/11) | Không | Không | Không | NOT IMPLEMENTED |
| PLC IO Simulation (đặt giá trị, ghi thủ công, đọc lại) | Có | Có | Giá trị không kích hoạt nghiệp vụ | PARTIAL |
| RCS Simulator (nhận lệnh, bản tin) | Một phần | Có | Có | PARTIAL |
| AMR Simulation | Chỉ dữ liệu hiển thị | AMR đứng yên, luôn rảnh | Không | MOCK |
| Pick/Drop Simulation | Tự động: engine nhiệm vụ qua lệnh "advance". Engine lệnh: thủ công | Có | Có | PARTIAL |
| WCS Orchestration (hai engine) | Có | Có | Có | PASS |
| PLC + WCS + RCS E2E theo tín hiệu IO | Không | Không | Không | **FAIL** |
| Luồng nghiệp vụ A1→B1 với yêu cầu mô phỏng và xác nhận thủ công | Có | Có | Có | PASS |

### Đối chiếu 17 bước của kịch bản

| Bước | Kết quả |
|---|---|
| 1 PLC_MissionRequest | Đặt được giá trị; WCS **không** phản ứng (0 lệnh sau 8 giây). Thay bằng yêu cầu của trang Mô phỏng PLC |
| 2–4 Nhận yêu cầu, kiểm tra, tạo lệnh | PASS (chỉ khi PLC giả lập đang ngắt – D-01) |
| 5–6 Gửi và nhận nhiệm vụ | PASS một phần: `Create Leg Task` / `Accepted`; không có Parent Task |
| 7, 12 AMR di chuyển | Không mô phỏng |
| 8–10, 13–15 Pick, Drop, hoàn tất | PASS bằng xác nhận thủ công; `Continue Leg Task` sau Pick |
| 11, 16 Phản hồi PLC | Không có; mọi IO của A1 giữ 0 |
| 17 Rack và trạm | Rack `A1:Full` → `InTransit` → `B1:Full`; cổng A1, B1 còn giữ tới khi xác nhận "AMR rời cổng" |

Sự kiện lệnh: `ORDER_CREATED > QUEUE_ADMITTED > ORDER_WAITING > LEG_CREATED > LEG_DISPATCHING > LEG_ACCEPTED > PICK_CONFIRMED > DESTINATION_ASSIGNED > DROP_CONFIRMED > ORDER_COMPLETED`. Khung tin tới PLC thật: 0. Bản tin RCS chế độ thật: 0.

Engine nhiệm vụ (Bp01): `Accepted → Executing → WaitingForPlc → … → Completed`, rack tới B1; tín hiệu "PLC permission" do `SimulationDriver` phát, không đi qua IO.

## Tình huống lỗi

| Tình huống | Kết quả quan sát | Cách kiểm |
|---|---|---|
| Không có rack nguồn | Engine nhiệm vụ `Failed/NO_SOURCE_AVAILABLE`; engine lệnh chờ, tự chạy khi có rack | E2E |
| Không có đích sau Pick | `Waiting/DestinationUnavailable_AgvHoldingRack`, rack `InTransit`; hủy bị từ chối; mở lại đích thì tiếp tục | E2E |
| AGV bị tắt | Engine nhiệm vụ từ chối `AGV_DISABLED`, không giữ chỗ; engine lệnh giữ chặng `Ready`, gửi khi bật lại | E2E |
| AGV đang bận | Lệnh sau chờ đúng thứ tự | E2E |
| Hủy trước Pick | Chặng chưa gửi: hủy được, nhả chỗ. Chặng đã gửi: `ORDER_NOT_CANCELLABLE` | E2E |
| PLC ngắt giữa chặng | Giao dịch cổng bị đánh dấu tạm ngưng; Pick thủ công vẫn được nhận; cờ tạm ngưng còn sau khi nối lại | E2E |
| Lỗi đọc trạm | IO của trạm chất lượng `Bad` | E2E |
| Yêu cầu trùng, Pick/Drop lặp, Drop trước Pick | `Duplicate`, `Duplicate`, `Rejected`; một lệnh duy nhất | E2E |
| RCS từ chối, timeout, mất kết nối, Interrupted, đổi AMR | Không tiêm được qua API/giao diện | Chỉ test tự động (đạt) |
| AMR lỗi, Pick/Drop thất bại giữa hành trình | Không mô phỏng được | Chỉ phần có trong test tự động |
| PLC handshake timeout | Không có handshake | Không kiểm được |

## C. QA findings

| QA | Nguyên nhân | Đã xử lý | Kết quả kiểm | Trạng thái |
|---|---|---|---|---|
| QA-001: 5/5 nhiệm vụ thất bại (`NV-02AEB9EE`) | **Dữ liệu:** ngày 25/11 chưa có rack nào; sự kiện tồn kho đầu tiên là 26/11 18:45. Engine nhiệm vụ cấu hình `NoSourceBehavior=Fail` nên thất bại ngay | Không cần sửa code; E2E đạt khi có dữ liệu hợp lệ | Tái hiện và PASS | Đã giải thích |
| QA-001b (phát hiện thêm): 13 lệnh chờ "không có rack" dù A1 có rack | Cổng A1, A2 còn giao dịch trạm `HandoverCompleted` từ 26/11; chỉ nhả khi xác nhận "AMR rời cổng". Lý do hiển thị sai | Sửa lý do chờ và cột "Lý do chờ" (CR-049) | Test mới và trình duyệt: PASS | Hiển thị: đã sửa. Cổng tự nhả trong Simulator: chờ quyết định (D-04) |
| QA-003: 96 và 120 | Hai tập khác nhau, số liệu đúng: 96 tín hiệu trạm (IO point); 120 dòng sheet = 96 + 4 HeartBit + 20 dự phòng. 95 = một địa chỉ đã bị Admin xóa | Ghi nhãn rõ (CR-049); không đổi workbook, địa chỉ, quyền ghi | Đối chiếu API và trình duyệt: PASS | Đã sửa |
| QA-004: dữ liệu thử trong tồn kho | Rack `xx`, `123`, `123123`, `1234123412341234`: tạo thủ công bằng tài khoản admin 26/11 18:50–18:54, không tham gia chặng hay giữ chỗ nào. Location `123123`, `A123`: tạo qua cấu hình 17/11. Process `123`, `QT001`: chưa phát hành | Không xóa, không sửa dữ liệu | Truy vấn chỉ đọc | Chờ quyết định |

**QA-004, đề xuất:** schema không có cờ "dữ liệu thử". Cho UAT nên dùng database mới và nạp tồn kho đã kiểm đếm. Nếu giữ database hiện tại: sao lưu; hủy các lệnh chờ không còn cần; xác nhận cổng A1, A2; xóa 4 rack qua giao diện (lịch sử được giữ); tắt Location `123123`, `A123` và process `123`, `QT001`. Gắn nhãn lâu dài cần Change Request và migration.

## D. CR-047 và CR-048 (môi trường QA)

| Kiểm tra | Kết quả |
|---|---|
| Xóa rack rảnh | PASS; mất dòng rack, lịch sử còn |
| Rack bị lệnh giữ | Từ chối `RACK_RESERVED`, nêu lệnh giữ và khả năng hủy |
| Hủy lệnh giữ rồi xóa | PASS; lệnh và chặng còn ở trạng thái `Cancelled` |
| Xóa không có lý do | `REASON_REQUIRED` |
| Rack bị chặng đã gửi giữ | Không xóa được, không hủy được (đúng quy tắc) |
| Audit | Ghi cả thành công và từ chối |
| Sửa, xóa, khôi phục địa chỉ trên giao diện; tải lại | PASS; database khớp màn hình |
| Trùng địa chỉ | Từ chối, nêu điểm đang dùng; audit `Failed` |
| Khởi động lại backend | Địa chỉ đã sửa và đã xóa giữ nguyên |
| Quyền ghi | Đổi địa chỉ làm cổng mất duyệt đọc: quy trình dùng cổng đó từ chối yêu cầu cho tới khi duyệt lại. Không khung tin tới PLC |

## E. Danh sách lỗi và thiếu sót

| Mã | Mức | Tái hiện | Thực tế / Mong đợi | Nguyên nhân | Hướng xử lý |
|---|---|---|---|---|---|
| D-01 | High | Simulator, PLC giả lập kết nối; trang Mô phỏng PLC → gửi yêu cầu | `SIMULATION_PLC_ONLINE` / chấp nhận. Mà ngắt PLC thì lệnh chờ PLC | PLC giả lập được coi như PLC thật. Hành vi đúng theo chữ của CR-040 ("Từ chối port có PLC đang online") | **Chờ quyết định:** chỉ từ chối khi runtime ở chế độ thật. Đi vòng: ngắt, gửi, nối lại |
| D-02 | High | Đặt `PLC_MissionRequest=1` | Không có gì xảy ra; không phản hồi PLC | Chưa hiện thực intake và handshake của IO Map 22/11 (CR-029, câu hỏi PLC mở) | Cần xác nhận của kỹ sư PLC và Change Request |
| D-03 | High | Gửi chặng, chờ | AMR đứng yên, không Pick/Drop; trang Mô phỏng PLC xác nhận Pick bị từ chối (`NotFound`) | Simulator không mô phỏng thực thi; ở chế độ AGV do WCS chọn, chặng không được đăng ký trong `RcsSimulator` | Đề xuất Change Request: mô phỏng thực thi (trạng thái, vị trí, Pick/Drop, lỗi) |
| D-04 | Medium | Hoàn tất một lệnh, gửi lệnh mới cùng cổng | Lệnh mới chờ vô hạn tới khi Admin xác nhận "AMR rời cổng" | Đúng ADR-0008; Simulator không phát sự kiện rời cổng | Quyết định: tự xác nhận trong Simulator, hoặc cảnh báo cổng bị giữ lâu |
| D-05 | Medium | Như D-04 | "Chưa có rack nguồn phù hợp" dù rack có / nêu cổng bị giữ | Một lý do chung | **Đã sửa** |
| D-06 | Low | Trang địa chỉ IO | 96 và 120 không rõ nghĩa | Thiếu nhãn | **Đã sửa** |
| D-07 | Medium | – | Không tiêm được lỗi RCS từ API/giao diện | Chỉ có trong mã test | Gộp vào Change Request của D-03 |
| D-08 | Low | Ngắt rồi nối lại PLC giữa chặng | Cờ tạm ngưng còn; Pick thủ công được nhận khi PLC đang ngắt | Chưa có nguồn quan sát PLC (`UnavailablePlcObservationSource`) | Xác nhận quy tắc khi làm handshake |
| D-09 | Low | Hộp thoại sửa địa chỉ IO | Gợi ý "Chỉ nhập địa chỉ do kỹ sư PLC cấp" không còn đúng sau CR-048 | Chữ chưa cập nhật | Sửa chữ |

## F. Kết luận

**READY FOR INTERNAL TEST** – chỉ cho luồng nghiệp vụ WCS với yêu cầu mô phỏng và xác nhận thủ công. Chưa đủ cho UAT.

| Phạm vi | Trạng thái |
|---|---|
| Simulator hoạt động | Một phần: IO PLC và nhận lệnh RCS chạy; không handshake, không AMR thực thi |
| Nghiệp vụ WCS | Đã chứng minh ở các luồng và lỗi nêu trên |
| Tích hợp PLC/RCS thật | **Chưa xác minh** |

Điều kiện để lên UAT: quyết định D-01 và D-04; mô phỏng thực thi và lỗi (D-03, D-07); hợp đồng và handshake PLC được xác nhận (D-02); dữ liệu UAT sạch (QA-004).

## Test tự động

Không chạy Full Backend Regression. Backend: 427/427 (điều phối, lỗi RCS/PLC, CR-008/010/028/030/035/036/038/040–042/046/047, đồng thời, test mới); sau khi đổi mã lý do chờ: 239/239 (CR-010/028/036/039–042/046, test mới). Frontend: tsc, lint, build, 11 bộ smoke đạt; `i18n:check` còn 7 lỗi mẫu log RCS có từ trước.

## Ảnh

[Lý do chờ sau sửa](qa001-after-reason.png) · [lệnh hoàn tất](e2e-order-completed.png) · [danh sách lệnh](e2e-orders.png) · [AMR mô phỏng](e2e-amr.png) · [IO PLC](e2e-plc-io.png) · [số liệu IO trước](qa003-before.png) · [sau](qa003-after.png) · [CR-048 sau sửa](cr048-edit.png) · [CR-048 sau restart](cr048-check.png)
