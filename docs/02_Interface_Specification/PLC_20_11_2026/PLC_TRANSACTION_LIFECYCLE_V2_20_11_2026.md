# PLC Transaction Lifecycle – cập nhật theo giao diện 20/11/2026

| | |
|---|---|
| **Trạng thái** | `SPECIFICATION – PENDING PLC CLARIFICATION`. Vòng đời dưới đây là **bản đề xuất để rà soát**, không phải yêu cầu đã duyệt. |
| **Cập nhật cho** | `PLC_BUSINESS_PROCESS_AND_TRANSACTION_MODEL_DRAFT.md`, ADR-0008, `PLC_COMMUNICATION_SPECIFICATION_DRAFT.md` §9–10 |
| **Nhãn** | [N] [SL] [CX] [HT] [KT] [ĐX = đề xuất] – xem [Interface Specification](PLC_INTERFACE_SPECIFICATION_V2_20_11_2026.md) |

## 1. Các thực thể và ranh giới (giữ nguyên) **[KT][HT]**

| Thực thể | Ý nghĩa | Ghi chú |
|---|---|---|
| **Transport Task / Order** (`TransportOrder`) | Nhiệm vụ vận chuyển tổng thể theo Business Process; có thể nhiều Leg | Không bị hoàn thành bởi một giao dịch trạm |
| **Leg** (`TransportLeg`) | Một chặng; hoàn thành **chỉ** khi xác nhận thả rack (`ConfirmDropAsync`) [CR-028] | `RobotCode` do RCS chọn (CR-028, chưa merge) |
| **Station Transaction** (`StationTransaction`) | Một lần AMR vào **một cổng/trạm** để lấy/cấp rack; hoàn thành khi bàn giao vật lý xong **và** AMR đã rời cổng | ADR-0008: tối đa **một** giao dịch hoạt động mỗi cổng |
| **Rack/Inventory** | WCS sở hữu; cập nhật theo sự kiện pick/drop được xác nhận | Dữ liệu PLC không ghi đè tồn kho |

Giao diện 20/11 chỉ mô tả **Station Transaction ở mức tín hiệu** (6 tín hiệu/trạm). Nó không thay đổi định nghĩa Task/Leg/Inventory.

## 2. Trạng thái Station Transaction hiện tại **[HT]**

`Created(0) → Requested(1) → Accepted(2) → AgvAssigned(3) → AgvApproaching(4) → AtStation(5) → Handling(6) → HandoverCompleted(7) → AgvExited(8) → Completed(9)`; ngoài chuỗi: `Failed`, `ReconciliationRequired` (xem `StationTransaction.cs`). `HandshakeStep` (1–9) ghi bước 9-bước cao nhất đã quan sát; `Advance` chỉ tiến, `Completed` chỉ sau `AgvExited` ← `HandoverCompleted`. Giao dịch bị treo (`Suspended`) khi PLC mất liên lạc; không bị hủy/reset.

## 3. Ánh xạ sự kiện 20/11 → trạng thái giao dịch [ĐX – dựa trên suy luận, cần xác nhận]

| Sự kiện/tín hiệu (20/11) | Hướng | Trạng thái giao dịch gợi ý | Căn cứ | Chưa xác định |
|---|---|---|---|---|
| `PLC_MissionRequest` = 1/2 (đã đọc, hợp lệ) | PLC→SW | `Requested` → `Accepted` (WCS ghi nhận bền) | [N] yêu cầu; [SL] "ghi nhận" | Điều kiện hợp lệ khi không có `Request_ID`/dữ liệu (PQ-10, PQ-17) |
| `Mission_Feedback` = 1–3 | SW→PLC | `AgvAssigned` (RCS đã gán AMR) | [N] "AGV1…AGV3 = trạng thái nhiệm vụ"; [SL] CR-028 "sent ≠ assigned" | Giá trị khi mới tiếp nhận; ánh xạ RobotCode (PQ-06) |
| `Mission_Feedback` > 10 | SW→PLC | `Failed`/lỗi hay chờ (không chọn) | [N] "Lỗi" | Mã lỗi, hành vi PLC/WCS (PQ-05, PQ-15) |
| `AGV_RequestTranfer` = 1 | SW→PLC | `AtStation` (AGV trước cửa; yêu cầu mở) | [N] "AGV ở vị trí trước cửa" | Nguồn sự kiện "đã đến" từ RCS (PQ-23) |
| `PLC_ReaddyTranfer` = 1 | PLC→SW | `Handling` (cửa/kẹp mở, thao tác diễn ra) | [N] "Xác nhận đã mở cửa Rack, mở Kẹp" | Mức/xung, đóng lại (PQ-07) |
| `AGV_TranferCompleted` = 1 | SW→PLC | `HandoverCompleted` → `AgvExited` | [N] "AGV đã lấy Rack và ra ngoài"; [SL] gộp hai mốc | Hai mốc (bàn giao xong / đã ra) không được tách trong 20/11; với **Drop** chưa mô tả (PQ-16) |
| `PLC_MissionCompleted` = 1 | PLC→SW | `Completed` | [N] "Xác nhận Giao/Nhận thành công" | Điều kiện, reset, giải phóng (PQ-09, PQ-10) |
| `PLC_MissionCompleted` = 0 | PLC→SW | `Failed` hoặc `ReconciliationRequired` (không chọn) | [N] "0: Thất bại" | PQ-09, PQ-15; phân biệt "chưa có" với "thất bại" |

Lưu ý quan trọng:
1. Ánh xạ trên có **gộp bước**; `StationTransaction.Advance` hiện bắt buộc đi qua `HandoverCompleted` rồi `AgvExited` trước `Completed`. Cần xem xét có thay đổi máy trạng thái hay vẫn giữ và đi qua hai trạng thái liền nhau (**Chưa đủ thông tin**).
2. Giá trị `HandshakeStep` 1–9 (domain kiểm tra 1..9) không còn khớp 6 bước; nếu giữ, cần định nghĩa ánh xạ; nếu đổi, ảnh hưởng domain/DB (**Điều chỉnh**).
3. **Rack/Inventory không được cập nhật bởi tín hiệu PLC.** Hoàn thành chặng và cập nhật tồn kho vẫn đến từ `ConfirmPick`/`ConfirmDrop` của WCS (nguồn sự kiện thật chưa nối – `IPlcObservationSource` hiện không có dữ liệu). `PLC_MissionCompleted` = 1 **không** đồng nghĩa rack đã được cập nhật vị trí trong WCS. Điều kiện xác nhận bàn giao theo nguyên tắc CR-028: rack đặt đúng Location/buffer, có sự kiện xác nhận đặt rack xuống, WCS cập nhật thành công, có lịch sử.

## 4. Bắt đầu và kết thúc giao dịch

| | Trạng thái |
|---|---|
| **Bắt đầu** | PLC đặt `PLC_MissionRequest` ∈ {1,2} **[N]**. Điều kiện WCS coi một yêu cầu là *mới* (so với yêu cầu đang xử lý/đã xong): **[CX]** (PQ-02, PQ-03, PQ-10) |
| **Kết thúc thành công** | `PLC_MissionCompleted` = 1 **[N]**; xác nhận bên WCS (bàn giao, tồn kho) là điều kiện riêng **[KT]** |
| **Kết thúc thất bại** | `PLC_MissionCompleted` = 0 hoặc `Mission_Feedback` > 10 **[N]**; hành vi **[CX]** |
| **Giải phóng cổng** | Hiện: sau `AgvExited`/`Completed`. 20/11: **không có điều kiện giải phóng/reset** **[CX]** (PQ-03, PQ-04, PQ-07, PQ-10). Q-REL-1 (giải phóng ở bước nào) cũ vẫn mở |
| **Hai giao dịch liên tiếp** | Cơ chế phân biệt/chống trùng: **[CX]** (PQ-10) |

## 5. Quan hệ Station Transaction ↔ Task/Leg/RCS

- Một Leg có thể mở giao dịch ở cổng nguồn và cổng đích (ADR-0008); điểm không có cổng PLC (buffer F1–F5) không cần bắt tay **[KT][HT]**.
- `Mission_Feedback`/`AGV_RequestTranfer` cần dữ liệu từ RCS (AMR đã gán, đã đến). CR-028 (chưa merge): `RobotCode` theo Leg khi RCS báo; "đã gửi" ≠ "đã gán". Dependency: giao diện PLC mới **phụ thuộc CR-028 được duyệt/merge** nếu muốn ghi `Mission_Feedback` dựa trên AMR thực tế.
- Không có chuyển trạng thái nào của `TaskStateMachine` được thay đổi bởi tài liệu này.

## 6. Điều kiện trước khi triển khai
Chốt PQ-02…PQ-10, PQ-16, C-1; xem [Impact Analysis](PLC_INTERFACE_CHANGE_IMPACT_ANALYSIS_20_11_2026.md).
