# PLC Communication Architecture – cập nhật theo giao diện 20/11/2026

| | |
|---|---|
| **Trạng thái** | `SPECIFICATION – PENDING PLC CLARIFICATION`. Không có thay đổi mã. Các phần "đề xuất" là **[ĐX]**, chưa được duyệt. |
| **Cập nhật cho** | ADR-0004 (kiến trúc giao tiếp PLC), `PLC_COMMUNICATION_SPECIFICATION_DRAFT.md` §1, §9, §11–12, ADR-0008 (Station Transaction theo cổng) |
| **Nhãn** | [N] nguồn 20/11 · [SL] suy luận · [CX] chưa xác định · [HT] hiện trạng đã kiểm tra trong mã · [KT] nguyên tắc đã thống nhất |

## 1. Các nguyên tắc kiến trúc – đối chiếu với giao diện mới

| # | Nguyên tắc **[KT]** | Mã hiện tại **[HT]** | Giao diện 20/11 | Kết luận |
|---|---|---|---|---|
| 1 | 4 PLC hoạt động độc lập, xử lý song song | Một `PlcWorker` mỗi PLC, đọc từng trạm độc lập; lỗi một trạm/PLC không chặn nơi khác | Không mâu thuẫn (khối `EF-G` là một PLC) | **Giữ nguyên** |
| 2 | Một kết nối TCP mỗi PLC, không 16 kết nối | `IPlcEndpoint` + worker là người dùng duy nhất của socket; ghi qua hàng đợi, không pipeline | Không ảnh hưởng | **Giữ nguyên** |
| 3 | Không hardcode IP; không đổi cấu hình kết nối hiện có | IP/port trong `plc_connections` (dữ liệu) | Workbook ghi IP 192.168.10.131 cho khối `EF-G` (ô A4) – **chỉ tham chiếu** | **Giữ nguyên**; không đổi cấu hình |
| 4 | FIFO trong phạm vi từng PLC | **Chưa có hàng đợi FIFO theo PLC.** `PlcRequestService` từ chối (`PORT_BUSY`) yêu cầu mới tại cổng đang có Station Transaction; yêu cầu PLC được đọc lại ở chu kỳ sau. FIFO có trong hàng đợi **AGV** (Legacy), không phải hàng đợi PLC | Không nêu hàng đợi/ưu tiên | **Chưa thực hiện – ghi nhận** (PQ-13, PQ-14, PQ-22) |
| 5 | Mỗi PLC tối đa **một** giao dịch tại một thời điểm | ADR-0008: **một giao dịch hoạt động mỗi cổng/Trạm**, ràng buộc bởi chỉ mục duy nhất trong DB; hai trạm cùng PLC chạy song song (test `Test07`) | Mỗi **trạm** có bộ tín hiệu riêng (hỗ trợ song song vật lý theo trạm) | **MÂU THUẪN GIỮA HAI NGUYÊN TẮC ĐÃ THỐNG NHẤT (C-1).** Không tự chọn; chờ chủ dự án + PLC |
| 6 | RCS chọn AMR (đội AMR động); WCS quản lý nghiệp vụ/giao dịch | CR-028 (`RcsManaged`, chưa merge): `RobotCode` theo Task/Leg | `Mission_Feedback` biểu diễn AGV1–AGV3 | **MÂU THUẪN KHẢ DĨ (C-2)**, PQ-06 |
| 7 | Cô lập adapter PLC/RCS khỏi logic nghiệp vụ | `McTcpClient`/`McFrame`/`McPlcEndpoint` ở Infrastructure; `IStationSignalService` ở Application | Không ảnh hưởng | **Giữ nguyên** |
| 8 | Chỉ-đọc trước, ghi sau khi kỹ sư PLC duyệt; không ghi tín hiệu an toàn/cho phép chuyển động để thử kết nối | Chính sách ghi, duyệt từng điểm, allow-list (CR-004/CR-019) | `AGV_RequestTranfer` là lệnh mở cửa/kẹp; `Mission_Feedback`, `AGV_TranferCompleted` là phản hồi | Cơ chế **giữ**; **danh sách tên bị chặn phải làm lại** (PQ-18) |
| 9 | WCS sở hữu tồn kho; dữ liệu PLC không ghi đè tồn kho | Bất biến trong `InventoryService`/`PlcRequestService` (mâu thuẫn ⇒ đối soát) | Giao diện mới **không còn dữ liệu rack** | **Giữ nguyên nguyên tắc**; mất khả năng đối chiếu PLC↔WCS (xem 3.4) |

## 2. Kiến trúc đích [ĐX – tham khảo thiết kế, chưa duyệt]

```
RCS ──(trạng thái task/leg, RobotCode, đã đến)──▶  WCS nghiệp vụ (Task / Leg / Station Transaction)
                                                       │  (chỉ qua IStationSignalService / dịch vụ giao dịch)
                                         PLC Handshake Coordinator   ← tách khỏi mapping và khỏi driver
                                                       │
                              StationIoHandler × mỗi trạm (6 tín hiệu mẫu mới)  + tín hiệu PLC-scoped (HeartBit) ← cần quyết định phạm vi
                                                       │
                                    PlcWorker × mỗi PLC (một kết nối, một vòng lặp)
                                                       │
                                  IPlcEndpoint (MC Protocol 3E – đề xuất, chờ xác nhận)
```

Giữ phân tầng hiện có: giao tiếp PLC · mapping IO trạm · điều phối handshake · logic nghiệp vụ WCS **[KT]**. Mỗi trạm có trạng thái polling, chất lượng dữ liệu, chẩn đoán, và trạng thái handshake riêng; truy cập chung trên một kết nối PLC được tuần tự hóa bởi worker **[KT]**.

## 3. Phân tích ảnh hưởng theo thành phần

Phân loại: **Giữ nguyên · Điều chỉnh · Thiết kế lại · Chưa đủ thông tin**. Chi tiết và tên tệp: [Impact Analysis](PLC_INTERFACE_CHANGE_IMPACT_ANALYSIS_20_11_2026.md) mục 4.

### 3.1. Phần giữ nguyên
Codec MC Protocol (`McFrame`, `McTcpClient`, `McPlcEndpoint`), `PlcWorker` (một worker mỗi PLC, hàng đợi ghi, kết nối lại, ghi mơ hồ không gửi lại), `PlcRuntime`, chẩn đoán/`PlcExchange`, chính sách "chỉ điểm đã duyệt mới polling".

### 3.2. Heartbeat – chức năng **chưa tồn tại** trong mã **[HT]**
Mã hiện tại **không có** bộ ghi heartbeat chu kỳ (`Heartbeat_SW` chỉ là một tín hiệu trong mẫu; có thể bị ràng buộc bởi quy tắc handshake tùy chọn, mặc định không có quy tắc nào). Giao diện mới đòi `HeartBit` bật/tắt chu kỳ 1 s, timeout 10 s **[N]**. Cần một bộ ghi heartbeat theo PLC (hoặc theo trạm, PQ-19), độc lập với chu kỳ polling, tuân thủ chính sách ghi (chỉ chế độ ghi thật được duyệt), và quyết định hành vi của WCS khi **PLC** ngừng phản hồi (chưa có tín hiệu phản hồi trong workbook). Phạm vi: **Điều chỉnh/Bổ sung** (không nằm trong phạm vi tài liệu này để thiết kế chi tiết).

### 3.3. Handshake và tiếp nhận yêu cầu
`HandshakeCoordinator.Observe`, `PlcStationSignalService`, `PlcRequestService`, `StationRequestProcessor` phụ thuộc trực tiếp tên tín hiệu cũ (`Pickup_Request`, `Delivery_Request`, `Request_ID`, `Rack_Full`…): **Thiết kế lại**. Quy tắc nháp ghi tín hiệu (`HandshakeDefinition`, bộ điều kiện "set when/reset when") có thể tái sử dụng làm khung nhưng 6 bước mới phụ thuộc **sự kiện nghiệp vụ** (AGV đã đến, đã ra ngoài), không chỉ giá trị của tín hiệu PLC khác: **Chưa đủ thông tin**.

### 3.4. Mất dữ liệu xác định vị trí và tồn kho
Giao diện cũ cho WCS biết **vị trí rack đầy/rỗng (`Rack_Full`/`Rack_Empty`) và loại rack**, và `PlcRequestService` dùng chúng để (a) xác định Location (`PlcLocationCodes` 1–4 → A1–A4) và (b) đối chiếu PLC ↔ tồn kho WCS, đánh dấu cần đối soát khi lệch (`RECON_PLC_INVENTORY_MISMATCH`). Giao diện mới chỉ cho biết **trạm + Pick/Drop**. Hệ quả **[SL]**: vị trí lấy/cấp suy ra từ trạm (cấu hình trạm ↔ Location) và quy trình nghiệp vụ; kiểu rack (đầy/rỗng) suy ra từ BP; việc đối chiếu vật lý với PLC **không còn khả năng** ở mức tín hiệu. Đây là thay đổi về **mức độ chắc chắn dữ liệu**, phải được chủ dự án chấp nhận (PQ-17, PQ-18) – WCS vẫn là chủ sở hữu tồn kho **[KT]**.

### 3.5. Chống trùng
Khóa chống trùng hiện tại dựa trên `Request_ID` (`PlcId:Station:RequestID`, tối đa 254 ký tự). Giao diện mới **không có định danh yêu cầu** ⇒ cần cơ chế khác (ví dụ phát hiện cạnh/chu trình yêu cầu + mã giao dịch do WCS sinh) – **[CX]** PQ-10; không thiết kế khi chưa biết giữ mức/xung và reset.

### 3.5.1. Mission_Feedback và đội AMR động
Ghi `Mission_Feedback` ∈ {1,2,3} đòi ánh xạ `RobotCode` (do RCS báo sau khi gán – CR-028: "đã gửi ≠ đã gán") sang AGV1–AGV3; đội AMR động có thể >3 xe hoặc xe không thuộc bảng `agvs`. Không có ánh xạ nào được duyệt. **Chưa đủ thông tin** (PQ-06); không tự đặt ánh xạ, không mặc định AGV1.

### 3.6. Giao dịch, vòng đời, lỗi, phục hồi
Xem [Vòng đời](PLC_TRANSACTION_LIFECYCLE_V2_20_11_2026.md) và [Lỗi & phục hồi](PLC_ERROR_HANDLING_AND_RECOVERY_V2_20_11_2026.md).

## 4. Hàng đợi và tuần tự hóa (ghi nhận, chưa quyết)

| Phương án | Mô tả | Phù hợp nguyên tắc | Cần |
|---|---|---|---|
| **A – tuần tự theo PLC** | Mỗi PLC một giao dịch hoạt động; FIFO theo thời điểm WCS tiếp nhận | Nguyên tắc 4–5 trong yêu cầu của tác vụ này | Thay đổi so với ADR-0008 (một giao dịch hoạt động mỗi cổng) và mã/DB; PLC xác nhận chỉ một trạm hoạt động mỗi PLC |
| **B – độc lập theo trạm (hiện trạng)** | Mỗi trạm một giao dịch hoạt động; trạm cùng PLC song song; FIFO trong phạm vi trạm/PLC cho yêu cầu chờ | ADR-0008; bộ tín hiệu riêng từng trạm (20/11) | PLC xác nhận các trạm cùng PLC có thể phát yêu cầu đồng thời (PQ-13) và quy tắc ưu tiên (PQ-14) |

Chưa chọn. Một thay đổi sang A **không thể** hạn chế ở tài liệu: ảnh hưởng ràng buộc DB (`ux` giao dịch hoạt động theo cổng), `StationTransactionService`, `PlcRequestService`, test `Cr010MultiLegTests.Test07`.

## 5. Trạng thái và điều kiện tiếp theo

`NEEDS_CONFIRMATION` cho: địa chỉ, ngữ nghĩa tín hiệu, hàng đợi PLC, heartbeat, ánh xạ AGV. Thứ tự triển khai đề xuất ở [Impact Analysis](PLC_INTERFACE_CHANGE_IMPACT_ANALYSIS_20_11_2026.md) mục 7. Không triển khai trước khi CR-029 được duyệt và có kế hoạch chuyển đổi.
