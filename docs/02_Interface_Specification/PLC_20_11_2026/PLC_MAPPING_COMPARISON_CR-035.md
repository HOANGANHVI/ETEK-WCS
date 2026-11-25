# CR-035 – Đối chiếu PLC IO Map v2 ↔ Workbook 20/11/2026 ↔ Code hiện tại

| | |
|---|---|
| **Ngày** | 2026-11-24 |
| **Mục đích** | Làm căn cứ cho PLC Simulation (thử nghiệm) và cho việc chuyển sang mapping 20/11 sau khi CR-029 được duyệt |
| **Nguồn** | (A) IO Map v2 `ETEK_WCS_IO_Map_4PLC_16Stations_v2_Separate_IO_Sets.xlsx` (đọc từ git HEAD; file đang bị xóa trong working tree – **không restore**); (B) `AGV_PLC Interface_List_20_11_2026.xlsx` (bản working tree của người dùng, **chỉ đọc**); (C) code: `plc-io-catalog.json` (dẫn xuất từ A), `PlcRequestSnapshot`, `Handshake.cs`, `PlcStationSignalService.cs`, `RouteEngineOptions.PlcLocationCodes` |
| **Trạng thái phê duyệt** | **A: không có địa chỉ thực được duyệt** (`Actual_PLC_Address` trống, `Configuration_Status = NEEDS_CONFIGURATION`, handshake `PENDING`). **B: CR-029 `PENDING_APPROVAL`** – có cột địa chỉ nhưng chưa được duyệt. Không nguồn nào được phép điều khiển PLC thật |

## 1. Khác biệt cấu trúc

| Hạng mục | v2 (A) | 20/11 (B) |
|---|---|---|
| Mẫu tín hiệu / Station | 23 tín hiệu, tên kiểu `Pickup_Request`, `Request_ID`… | 6 tín hiệu lệnh + heartbeat, tên `PLC_MissionRequest`… |
| Cột địa chỉ | `Template_Address_Reference` (tham chiếu mẫu) + `Actual_PLC_Address` **trống** | Cột `Địa chỉ` có giá trị cho 5/6 tín hiệu lệnh; **`AGV_TranferCompleted` để trống** |
| Mô hình request | 2 bit `Pickup_Request`/`Delivery_Request` (BOOL) + `Request_ID` (STRING) | Một INT `PLC_MissionRequest` (0/1 Pick/2 Drop/10 lỗi); **không có Request_ID** (liên quan duplicate protection – ADR-0011 D-4) |
| Địa chỉ trùng giữa Station | Cùng mẫu cho mọi Station (chưa cấp phát thật) | Cấp theo Station trong PLC: A1 D1500…, A2 D1510…, A3 D1520…, A4 D1530… (PLC2: B1 D1500, B2 D1510, D1 D1520, D2 D1530) – địa chỉ lặp lại giữa PLC khác nhau (hợp lệ) |
| Trạm PLC3 | X1, X2, X3, X6 | X1, X2, X3, **X4** |
| Trạm PLC4 | E1, E2, G3, G7 | E1, E2, **G1, G2** (PQ-20 / OQ-29 còn mở) |
| AMR | `AGV_ID` (INT) | `Mission_Feedback` 0/1/2/3/10 (ADR-0011 D-2: **không** là RobotCode) |
| Heartbeat | `Heartbeat_SW` M9100 (BOOL, mỗi Station) | `HeartBit` M9200 (BOOL, mỗi PLC, chu kỳ 1 s, timeout 10 s); M9201–M9205 dự phòng |

## 2. Bảng từng tín hiệu

Ký hiệu: **✔ code** = đã được code đọc/ghi/ngụ ý; **—** = chưa dùng; **TRỐNG** = không có địa chỉ trong nguồn.

| # | Ý nghĩa | v2: tên · tham chiếu mẫu · kiểu | 20/11: tên · địa chỉ (A1) · kiểu · giá trị | Code hiện tại | Trạng thái |
|---|---|---|---|---|---|
| 1 | PLC yêu cầu lấy rack | `Pickup_Request` · M9001 · BOOL | `PLC_MissionRequest` = 1 · D1500 · INT | ✔ `PlcRequestSnapshot.PickupRequest` (logic, không phụ thuộc địa chỉ); `StationFlows` rỗng mặc định | v2: NEEDS_CONFIGURATION; 20/11: PENDING |
| 2 | PLC yêu cầu cấp rack | `Delivery_Request` · M9002 · BOOL | `PLC_MissionRequest` = 2 · D1500 · INT | ✔ `DeliveryRequest` | như trên |
| 3 | Mã yêu cầu duy nhất | `Request_ID` · D1500 · STRING | **không có** | ✔ `RequestId` (chống trùng); SIM sinh `SIM-…` | **20/11 thiếu → chưa đủ căn cứ** (OQ-05) |
| 4 | Rack đầy tại vị trí | `Rack_Full` · D1510 · INT (1–4 = A1–A4) | không có | ✔ `RackFull` + `PlcLocationCodes` (`appsettings.json`: 1→A1…4→A4) | v2: chỉ giá trị 1–4 xác nhận |
| 5 | Rack rỗng tại vị trí | `Rack_Empty` · D1512 · INT | không có | ✔ `RackEmpty` | như trên |
| 6 | Đích có thể nhận | `Destination_Available` · D1514 · INT | không có | ✔ đọc, **mã hóa chưa xác nhận** (không dùng chọn đích) | chưa đủ căn cứ |
| 7 | Loại rack | `Rack_Type` · D1520 · INT | không có | ✔ đọc | chưa đủ căn cứ |
| 8 | PLC cho phép Pick/Drop (permission/ready) | `Transfer_Permit`(SW→PLC) / — | `PLC_ReaddyTranfer` · D1501 · INT (1 Pick, 2 Drop, 10 lỗi) | — (WCS không ghi Transfer_Permit: PLC engineer chưa duyệt) | **không phải bằng chứng cửa/kẹp đã mở** (Rev 1.1 Issue 2) |
| 9 | Bàn giao hoàn tất | `Transfer_Complete` · M9010 · BOOL | `PLC_MissionCompleted` · D1502 · INT (1 thất bại, 2 thành công) | ✔ `Transfer_Complete` đọc ở `Handshake.cs` | cần đủ cả E6 (ADR-0011 D-4); `=1` đơn lẻ chưa đủ |
| 10 | Trạm online | `Station_Online` · M9003 | không có | ✔ `Handshake.cs` | — |
| 11 | Trạm lỗi | `Station_Fault` · M9009 | `PLC_*`=10 (lỗi) gián tiếp | ✔ | ngữ nghĩa khác, không tự quy đổi |
| 12 | An toàn/Interlock | `Safety_Ready` · M9000 | không có | — | **không ghi/thử để kiểm kết nối** |
| 13 | Rack hiện diện | `Rack_Present` · M9005 | không có | — | D-4: cần nguồn khác nếu PLC không cung cấp |
| 14 | Station sẵn sàng | `Station_Ready` · M9004 | không có | — | — |
| 15 | WCS đã nhận request | `Request_Ack` · M9101 · BOOL | không có (20/11: không có ack) | ghi **bị chặn** (không có handshake rule được duyệt) | NEEDS_CONFIRMATION |
| 16 | AGV được gán | `AGV_Assigned` · M9102 | `Mission_Feedback` ≠ 0 · D1601 | không ghi | D-2: không đại diện RobotCode |
| 17 | Mã AGV | `AGV_ID` · D1600 · INT | `Mission_Feedback` 1/2/3 | không ghi | **không** ghi 1–3 làm định danh AMR |
| 18 | AGV đã đến | `AGV_Arrived` · M9103 | `AGV_RequestTranfer` = 1/4 · D1600 (AGV đến trước cửa) | không ghi | điều kiện ghi chưa duyệt |
| 19 | Cho phép chuyển | `Transfer_Permit` · M9104 | `AGV_RequestTranfer` · D1600 | không ghi (motion-related) | **cấm ghi khi chưa duyệt** |
| 20 | Cho phép AGV rời | `AGV_Departure_Permit` · M9105 | `AGV_TranferCompleted` · **TRỐNG** · INT (3 xong Pick, 4 xong Drop) | không ghi | **20/11 thiếu địa chỉ** |
| 21 | AGV bận / lỗi | `AGV_Busy` M9106 / `AGV_Fault` M9107 | — | không ghi | — |
| 22 | Mã lỗi | `Error_Code` · D1610 · INT | — | không ghi | — |
| 23 | Heartbeat | `Heartbeat_SW` · M9100 · BOOL (Station) | `HeartBit` · M9200 · BOOL (**PLC**) | không ghi (ghi cần duyệt) | PLC-scoped vs Station-scoped (CLAUDE §7.3) còn mở |

## 3. Cách PLC Simulation dùng mapping (CR-035)

- Simulation **không** đọc/ghi địa chỉ PLC nào. Nó đưa **tín hiệu logic** (`PlcRequestSnapshot`: PlcId, PortId, RequestId, Pickup/Delivery, RackFull/Empty) vào **cùng** `IPlcRequestService.HandleRequestAsync` mà PLC runtime sẽ dùng. Vì vậy:
  - **Cấu hình thử nghiệm đang dùng:** tín hiệu logic của v2 (`Pickup_Request`, `Delivery_Request`, `Request_ID`, `Rack_Full/Rack_Empty` + `PlcLocationCodes` 1–4 = A1–A4, đã có trong code/appsettings). Không phải mapping 20/11 đã duyệt.
  - **Không được phép dùng:** mọi địa chỉ trong workbook 20/11 và `Template_Address_Reference` của v2 để đọc/ghi PLC thật.
  - **Tín hiệu thiếu địa chỉ/căn cứ:** `AGV_TranferCompleted` (20/11, địa chỉ trống); `Request_ID` (20/11 không có); `Destination_Available` và `Rack_Type` (mã hóa chưa xác nhận); `Rack_Present` (D-4); `Request_Ack`, `Transfer_Permit`, `AGV_Departure_Permit` (điều kiện ghi chưa duyệt).
- Vì tầng logic tách khỏi địa chỉ, chuyển sang mapping 20/11 sau này chỉ cần: (1) CR-029 được duyệt; (2) PLC engineer điền/xác nhận địa chỉ (gồm `AGV_TranferCompleted`) và trả lời PQ; (3) chọn lại tên tín hiệu → tín hiệu logic trong adapter (không đổi PlcRequestService, Queue, Process); (4) nhập mapping qua cấu hình/danh mục có phiên bản (catalog) và duyệt từng IO point (`IsPortMappingApprovedAsync`); (5) cập nhật CHANGELOG, Impact Matrix, Traceability.

## 4. Điểm cần PLC engineer/chủ dự án trả lời trước khi dùng PLC thật

1. Địa chỉ `AGV_TranferCompleted`; duplicate protection khi không có `Request_ID` (OQ-05).
2. Tên trạm PLC3 (X6 hay X4), PLC4 (G3/G7 hay G1/G2) – OQ-29/PQ-20.
3. Heartbeat theo PLC hay theo Station.
4. Rack presence/identity (ADR-0011 D-4) lấy từ đâu.
5. Quy ước reset, timeout, mốc bắt đầu/kết thúc giao dịch active (OQ-02).
