# AGV Phase 2 – Danh mục Station và IO mapping

| | |
|---|---|
| **Trạng thái** | **DRAFT – chờ chủ dự án duyệt (Revision 1.2).** Tài liệu này **không tạo địa chỉ PLC, không tạo mapping mới**. Mọi địa chỉ xuất hiện dưới đây là **trích dẫn nguồn có chỉ rõ nguồn**, không phải mapping được duyệt. |
| **Không chọn baseline** | Không bản workbook nào được coi là chuẩn (OQ-03). Không sửa/restore/commit workbook. |
| **Nhãn** | [NGUỒN] · [CODE] · [QĐ] · [ĐX] · [OPEN OQ-xx] |
| **Revision 1** | 2026-11-24 – thêm §10 truy vết giao tiếp PLC và bằng chứng message (không tạo địa chỉ, không chọn baseline workbook). Nhãn **[QĐ R1-A…E]** = yêu cầu bổ sung đã duyệt (xem `PHASE2_OPERATIONAL_SPEC.md` §15–§19). Không đổi quyết định D-01…D-08. |
| **Revision 1.1** | 2026-11-24 – **Issue 2:** `PLC_ReaddyTranfer` = **permission/ready** (1 = cho phép Pick, 2 = cho phép Drop, 10 = lỗi), **không** phải bằng chứng cửa/kẹp đã mở (§10.2, §10.4, §10.8); **Issue 5:** chuẩn hóa thang E0–E6 (§10.2–§10.4); **Issue 4:** liên hệ bằng chứng bền vững/lệnh `Unknown outcome` cho ghi PLC (§10.9); PH2-04 được liệt kê (D-09). **Không** thêm địa chỉ/tín hiệu; **không** chọn baseline workbook. |
| **Revision 1.2** | 2026-11-24 – **D-11 (Two-Gate):** ánh xạ IO PLC được duyệt và các điều kiện PLC/handshake thuộc **Operational Readiness Gate**, **không** thuộc Publication Gate (§14); **D-13:** Queue Ordering Scope **không** suy ra từ PLC/Station (§14.2). **Không** thêm địa chỉ/tín hiệu; **không** chọn baseline workbook; workbook PLC chưa duyệt **không** là căn cứ Ready. |

## 1. Bốn lớp mapping – phải phân biệt

| Lớp | Định nghĩa | Hiện có |
|---|---|---|
| **Source mapping** | Điều tài liệu nguồn ghi | Workbook 20/11 (HEAD), workbook 20/11 (working tree), workbook 10/11, matrix `Port_PLC_Mapping`, handoff, PPTX |
| **Approved mapping** | Đã được chủ dự án + PLC engineer duyệt | **Không có cho giao diện 20/11** (CR-029 `PENDING_APPROVAL`). Đã duyệt trước đó: *topology* 4 PLC/16 trạm theo IO Map v2 (CLAUDE.md §7.2, IP là "mapping dự án hiện cung cấp"; TCP port/MC parameters vẫn `NEEDS_CONFIGURATION`); `Actual_PLC_Address` **chưa gán** |
| **Current code mapping** | Cấu hình đang nằm trong code/DB | `plc-io-catalog.json` (4 PLC, 16 trạm, **mẫu 23 tín hiệu cũ**, **không có địa chỉ thực**); `appsettings.json:28` `PlcLocationCodes` 1–4 = A1–A4 |
| **Proposed mapping** | Đề xuất cho Phase 2 | **Không tạo trong P0.** Chỉ có đề xuất *phân loại* (§7), cần xác nhận |

## 2. Các nguồn PLC/trạm – so sánh theo khối PLC

| Khối | Matrix `Port_PLC_Mapping` | Catalog code (`plc-io-catalog.json`) | Workbook 20/11 **HEAD** (`3c5da74b…`) | Workbook 20/11 **working tree** (`a291d28f…`) | Handoff §02 | Ghi chú |
|---|---|---|---|---|---|---|
| IP .101 | PLC1: A1,A2,A3,A4 | PLC-01: A1–A4 | **Không có** (chỉ khối .131) | Nhãn khối "A1", IP 192.168.10.101: A1,A2,A3,A4 | A1–A4 | Khớp giữa matrix/catalog/WT |
| IP .111 | PLC2: B1,B2,D1,D2 | PLC-02: B1,B2,D1,D2 | **Không có** | Nhãn "BCD": B1,B2,D1,D2 | B1,B2,D1,D2 | Khớp |
| IP .121 | PLC3: X1,X2,X3,**X6** | PLC-03: X1,X2,X3,**X6** | **Không có** | Nhãn khối **"A2"** (nghi lỗi gõ), IP .121: X1,X2,X3,**X4** | X1–X4 | **Mâu thuẫn X6 ↔ X4** (OQ-29) |
| IP .131 | PLC4: E1,E2,**G3,G7** | PLC-04: E1,E2,**G3,G7** | Nhãn "EF-G": E1,E2,**G1,G2** | Nhãn "EF-G": E1,E2,**G1,G2** | E1,E2,G1,G2 | **Mâu thuẫn G3/G7 ↔ G1/G2** (OQ-29, PQ-20) |
| Không có PLC | F1–F5, KV1–KV4, P1–P4, M5–M6: "chưa xác định" (OP-01/02) | Không có | Không có | Không có | P1–P4, M5–M6 = WCS-managed | **Không gán PLC** (D-07) |

Nguồn khối PLC: workbook `IO Matrix` cột A/B (ô nhãn khối chứa IP xuống dòng). IP ở matrix/CLAUDE §7.2: .101/.111/.121/.131.

## 3. Khác biệt giữa hai bản workbook 20/11 (trường nào khác)

| Trường | HEAD | Working tree |
|---|---|---|
| Số trạm | 4 (E1,E2,G1,G2) | 16 |
| Địa chỉ thanh ghi | cột **trống** | có (`D1500…`, `D1600…`, `M9200…`) trừ `AGV_TranferCompleted` |
| `PLC_MissionRequest` | INT 0/1 Pick/2 Drop/`>10` lỗi | INT 0/1/2/`10` lỗi |
| `PLC_ReaddyTranfer` | BOOL 0 đóng/1 mở | INT 1 cho phép Pick/2 cho phép Drop/10 lỗi |
| `PLC_MissionCompleted` | BOOL 0 thất bại/1 thành công | INT **1 thất bại/2 thành công** |
| `AGV_RequestTranfer` | BOOL 0 rời đi/1 đã đến | INT 1 mở cửa-kẹp để Pick/4 để Drop |
| `AGV_TranferCompleted` | BOOL 0 rời đi/1 đã đến | INT 3 hoàn thành Pick/4 hoàn thành Drop; **không có địa chỉ** |
| `Mission_Feedback` | INT 0/1–3/`>10` | INT 0/1–3/`10` |
| `HeartBit` | BOOL (khối .131) | BOOL `M9200` ở **mỗi** khối PLC, chu kỳ 1 s/timeout 10 s; `Spair M9201–M9205` mỗi khối |
| Sheet `Trang_tính1` (6 bước) | giống nhau ở hai bản | giống nhau ở hai bản |

Handoff §02 mô tả **khớp bản working tree**. Điều này *không* biến bản working tree thành baseline được duyệt (OQ-03).

## 4. Danh mục Station

Cột: **Station Code** · **Tên (theo PPTX slide 2/3, nếu có)** · **Station Type** · **Quản lý** · **PLC ID (code / matrix)** · **Port ID (code)** · **IO signals** · **Rack type** · **Full/Empty** · **Vai trò** · **Process liên quan** · **Bằng chứng** · **Approval**.

Quy ước: *Port ID (code)* = `PlcStation.Id` trong catalog; trạm không có trong catalog ghi "— (không có)". *IO signals*: "6 tín hiệu 20/11" = `PLC_MissionRequest`, `PLC_ReaddyTranfer`, `PLC_MissionCompleted`, `AGV_RequestTranfer`, `AGV_TranferCompleted`, `Mission_Feedback`; "23 mẫu cũ" = mẫu IO Map v2 trong code. *Approval*: trạng thái duyệt của **mapping giao diện 20/11**; "Topology IO v2" = đã có trong baseline cũ.

### 4.1 PLC-managed (trong workbook 20/11)

| Code | Tên (PPTX) | Type | Quản lý | PLC ID | Port ID (code) | IO signals | Rack | F/E | Vai trò | Process | Bằng chứng | Approval |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| A1 | CNC F-Bal 10R80 (6.3 p/rack) | Station (CNC) | PLC | PLC-01 / PLC1 | A1 | 6 tín hiệu 20/11 (WT) / 23 mẫu cũ (code) | Rack thường | Cả hai (Full ra, Empty vào) | Máy cấp CNC | PH2-01, PH2-02, **PH2-04 (nguồn Full Rack ưu tiên, D-09)**, PH2-05; BP-01/02 (Phase 1) | PPTX s2/3/4/5; matrix R8–R9; WT IO Matrix | Topology IO v2 ✔; 20/11 ✘ chưa duyệt |
| A2 | CNC F-Bal 10R80 | Station | PLC | PLC-01 | A2 | như A1 | như A1 | như A1 | CNC | như A1 | như A1 | như A1 |
| A3 | CNC F-Bal 10R60 (12.8 p/rack) | Station | PLC (**PROTECTED – legacy**) | PLC-01 | A3 | như A1 | — | — | CNC | Luồng legacy (UNCHANGED) | PPTX s2/3; BR-04 | PROTECTED |
| A4 | CNC F-Bal 10R60 | Station | PLC (**PROTECTED**) | PLC-01 | A4 | như A1 | — | — | CNC | Legacy | như A3 | PROTECTED |
| B1 | QC F-Bal 10R80 (7.5 p/rack) | Station (QC) | PLC | PLC-02 / PLC2 | B1 | như A1 | Rack thường | Cả hai | QC | PH2-03, **PH2-04 (trạm yêu cầu Full Rack – baseline đã chấp thuận D-09)**, PH2-01/02 (đích/nguồn) | PPTX s2/3/6/7; matrix R10–R11 | Topology IO v2 ✔; 20/11 ✘ |
| B2 | QC F-Bal 10R80 | Station | PLC | PLC-02 | B2 | như A1 | như B1 | như B1 | QC | như B1 | như B1 | như B1 |
| D1 | QC F-Bal 10R60 (12.8 p/rack) | Station | PLC (**PROTECTED**) | PLC-02 | D1 | như A1 | — | — | QC | Legacy | PPTX s2/3; BR-04 | PROTECTED |
| D2 | QC F-Bal 10R60 | Station | PLC (**PROTECTED**) | PLC-02 | D2 | như A1 | — | — | QC | Legacy | như D1 | PROTECTED |
| X1 | CNC F-Pis 10R80 (6.3 p/rack) | Station | PLC | PLC-03 / PLC3 | X1 | như A1 | — | — | CNC | **OPEN OQ-09** (không có luồng trong nguồn) | PPTX s2/3; WT | 20/11 ✘ |
| X2 | CNC F-Pis 10R80 | Station | PLC | PLC-03 | X2 | như A1 | — | — | CNC | OPEN OQ-09 | như X1 | 20/11 ✘ |
| X3 | CNC F-Pis 10R60 (12.8 p/rack) | Station | PLC | PLC-03 | X3 | như A1 | — | — | CNC | OPEN OQ-09 | như X1 | 20/11 ✘ |
| **X4** (WT/handoff/PPTX) ↔ **X6** (code/matrix) | CNC F-Pis 10R60 (X4 theo PPTX) | Station | PLC | PLC-03 | X6 (code) / — (không có X4 trong code) | như A1 (X4 trong WT) | — | — | CNC | OPEN OQ-09/29 | PPTX s2/3; WT; catalog; matrix | **OPEN OQ-29** |
| E1 | (không có tên; 7.5 p/rack) | Station | PLC | PLC-04 / PLC4 | E1 | như A1 | — | — | **OPEN OQ-08** | OPEN OQ-08/09 | PPTX s2/3; workbook HEAD+WT; catalog | 20/11 ✘ |
| E2 | (7.5 p/rack) | Station | PLC | PLC-04 | E2 | như A1 | — | — | OPEN OQ-08 | OPEN | như E1 | 20/11 ✘ |
| **G1** (workbook/handoff/PPTX) ↔ **G3** (code/matrix) | (12.8 p/rack) | Station | PLC | PLC-04 | G3 (code) / — (G1 không có trong code) | như A1 | — | — | OPEN OQ-08 | OPEN | như E1 | **OPEN OQ-29** |
| **G2** ↔ **G7** | (12.8 p/rack) | Station | PLC | PLC-04 | G7 (code) / — (G2 không có trong code) | như A1 | — | — | OPEN OQ-08 | OPEN | như E1 | **OPEN OQ-29** |

### 4.2 WCS-managed (D-07)

| Code | Tên (PPTX) | Type | Quản lý | PLC ID | Port ID | IO signals | Rack | F/E | Vai trò | Process | Bằng chứng | Approval |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| P1 | "Buffer hàng cho A1, A2 (Thêm mới)" | Buffer | **WCS** | Không có | — (không có) | **Không có** – không gán handshake | Cần OQ-11 | Cả hai (có Full và có Empty theo slide 4–9) | Buffer | PH2-01…06 (PH2-04: nguồn thay thế) | PPTX s2/4–9; handoff 00-§2.2; matrix R8–R13 | Phân loại [QĐ D-07] ✔; Location/capacity **chưa cấu hình** (OQ-11) |
| P2 | như P1 | Buffer | WCS | — | — | — | OQ-11 | như P1 | Buffer | như P1 | như P1 | như P1 |
| P3 | "Buffer hàng cho X1, X2 (Thêm mới)" | Buffer | WCS | — | — | — | OQ-11 | — | Buffer | **OPEN OQ-09** | PPTX s2/3; handoff | Phân loại ✔; luồng OPEN |
| P4 | như P3 | Buffer | WCS | — | — | — | OQ-11 | — | Buffer | OPEN OQ-09 | như P3 | như P3 |
| M5 | "QC thủ công cho A1, A2 (Thêm mới)"; "Buffer manual" (slide 8) | Manual QC (`LocationType.ManualBuffer` gần nhất – [CODE]) | **WCS** | Không có | — | **Không có** | OQ-11 | Nhận Full, trả Empty | QC thủ công | PH2-01 (đích nhóm 3), PH2-05, PH2-06 | PPTX s2/4/8/9; matrix R8,R12,R13 | Phân loại ✔; capacity OQ-11; **không** đồng nhất F5 (OQ-10) |
| M6 | "QC thủ công cho X1, X2 (Thêm mới)" | Manual QC | WCS | — | — | — | OQ-11 | — | QC thủ công | **OPEN OQ-09** | PPTX s2/3 | Phân loại ✔; luồng OPEN |

### 4.3 Điểm khác / không thuộc Phase 2 nhưng có trong nguồn

| Code | Mô tả [NGUỒN] | Quản lý | Ghi chú |
|---|---|---|---|
| F1, F2 | Buffer rack đầy (Phase 1, BP-01/04) | Chưa xác định PLC (OP-01) | Phase 1; quan hệ với Phase 2: OQ-28 |
| F3, F4 | Buffer rack rỗng (Phase 1) | Chưa xác định PLC | Matrix R3: "Không tự gán PLC cho F3/F4"; "Không đồng nhất P1/P2 với F3/F4" |
| F5 | Manual buffer (Phase 1: BP-05/06); **hình layout slide 3 của PPTX Phase 2 có nhãn "F5"** cạnh cụm Rack | Matrix R6: PLC "chưa xác định" | **Không đồng nhất với M5** (D-07); OQ-10 |
| KV1–KV4 | Điểm luồng/buffer (Phase 1, `business-rules.md §1.5`) | Chưa xác định | Không tự gán ý nghĩa |
| Y1, Y2, Z1, Z2 | QC của F-Pis (PPTX slide 2) | Chưa xác định | **Không đồng nhất với D/E/G** (OQ-07) |

## 5. Tín hiệu IO theo nguồn (SOURCE ONLY – workbook working tree, chưa duyệt)

Đọc trực tiếp sheet `IO Matrix` bản working tree. Quy luật quan sát được: mỗi PLC block có 4 trạm; địa chỉ của trạm thứ k (k = 1…4) lệch +10 so với trạm trước **trong cùng block**; cùng bộ địa chỉ lặp ở cả 4 PLC (mỗi PLC có không gian riêng – CLAUDE §7.3). **Đây là mô tả quan sát nguồn, không phải suy diễn để tạo địa chỉ mới.** Không dùng bảng này làm cấu hình.

| Trạm thứ (trong block) | `PLC_MissionRequest` | `PLC_ReaddyTranfer` | `PLC_MissionCompleted` | `AGV_RequestTranfer` | `AGV_TranferCompleted` | `Mission_Feedback` |
|---|---|---|---|---|---|---|
| 1 (A1 / B1 / X1 / E1) | D1500 | D1501 | D1502 | D1600 | **(trống)** | D1601 |
| 2 (A2 / B2 / X2 / E2) | D1510 | D1511 | D1512 | D1610 | **(trống)** | D1611 |
| 3 (A3 / D1 / X3 / G1) | D1520 | D1521 | D1522 | D1620 | **(trống)** | D1621 |
| 4 (A4 / D2 / X4 / G2) | D1530 | D1531 | D1532 | D1630 | **(trống)** | D1631 |
| Mỗi PLC block | `HeartBit` M9200 (BOOL); `Spair` M9201–M9205 | | | | | |

Kiểm tra trực tiếp một số ô: A1 R3–R8; B1 R33–R38; X1 R63–R68; E1 R93–R98; `HeartBit` R27/R57/R87/R117. **`AGV_TranferCompleted` không có địa chỉ ở cả 16 trạm → tiếp tục đánh dấu THIẾU (OQ-06); không suy đoán.**

Chưa rõ: heartbeat **PLC-scoped hay Station-scoped** (workbook đặt trong mỗi block; CLAUDE §7.3 yêu cầu phân loại tường minh); kiểu tín hiệu (xung/mức), thứ tự reset, timeout từng bước (handoff §6; PQ-07…).

## 6. Current code mapping (đã đọc)

| Thành phần | Nội dung | Ghi chú |
|---|---|---|
| `Infrastructure/Plc/Catalog/plc-io-catalog.json` | 4 PLC (PLC-01…04), IP .101/.111/.121/.131; 16 trạm: A1–A4, B1,B2,D1,D2, X1,X2,X3,**X6**, E1,E2,**G3,G7**; mẫu **23 tín hiệu** cũ với `templateAddressReference` (M9000…); nguồn ghi sha256 `89ff65b5…` của IO Map v2; "Actual_PLC_Address … intentionally absent" | **Không chứa** 6 tín hiệu 20/11; khóa `PLC_ID+Station_ID+Signal_Name` |
| `WCS.Domain/Plc/PlcEntities.cs:97-115` | `PlcStation` (`Id`, `PlcId`, `Order`, `LocationCode?`, `Enabled`, `Role="MonitorOnly"`) | Không có trường PLC-managed/WCS-managed |
| `appsettings.json:28` | `PlcLocationCodes` {"1":"A1","2":"A2","3":"A3","4":"A4"} (mã vị trí trong `Rack_Full`/`Rack_Empty` của giao diện **10/11**) | Không áp dụng cho 20/11 |
| Location không có Port | Coi là luôn sẵn sàng/online (`RouteConditionEvaluator.cs:106,115`) | Hành vi mặc định cho P/M – an toàn nhưng chưa có phân loại tường minh |
| Seeder | `Seed.Enabled=false` (dev); danh sách ví dụ A1,A2,B1,B2,F1–F5 (`appsettings.Development.json`) | P1–P4, M5, M6 **chưa có** |

## 7. Đề xuất phân loại [ĐX – PROPOSAL, NEED USER CONFIRMATION]

1. Thêm thuộc tính **quản lý** tường minh cho Location/Station: `PlcManaged | WcsManaged` (hiện chỉ suy ra từ việc có `PlcStation` liên kết). Cho phép validator Phase 2 từ chối gán Port PLC cho Location `WcsManaged` (D-07).
2. P1–P4 → `LocationType.Buffer`, `WcsManaged`; M5–M6 → `LocationType.ManualBuffer` hoặc loại mới "ManualQc" (cần xác nhận), `WcsManaged`. Capacity/Rack kind: **chờ OQ-11**.
3. Giữ nguyên khóa IO; không đổi tên trạm cấu hình cho tới khi OQ-29 được trả lời.
4. Khi giao diện 20/11 được duyệt: **mẫu tín hiệu mới** (6 + heartbeat) là một template riêng, **không ghi đè** mẫu 23 tín hiệu cũ; địa chỉ thực chỉ do PLC engineer xác nhận.

## 8. Trạng thái duyệt – tổng hợp

| Hạng mục | Trạng thái |
|---|---|
| Topology 4 PLC/16 trạm (IO Map v2, tên X6/G3/G7) | Đã duyệt trước đó [HOẠT ĐỘNG] (CR-002) |
| Giao diện 20/11 (cả hai bản workbook) | **Chưa duyệt** (CR-029 `PENDING_APPROVAL`) |
| Địa chỉ thực (`Actual_PLC_Address`) | **Chưa gán** |
| `AGV_TranferCompleted` địa chỉ | **THIẾU** |
| P1–P4, M5–M6 phân loại WCS-managed | **Đã chốt [QĐ D-07]**; cấu hình Location **chưa làm** |
| Tên trạm X4/X6, G1/G2 ↔ G3/G7 | **OPEN OQ-29** |
| Vai trò E/G, ánh xạ Y/Z | **OPEN OQ-07/08** |

## 9. Open Questions liên quan

OQ-03, OQ-04, OQ-05, OQ-06, OQ-07, OQ-08, OQ-09, OQ-10, OQ-11, OQ-25, OQ-27, OQ-29, **OQ-53, OQ-54, OQ-55** (nội dung đầy đủ: `PHASE2_OPERATIONAL_SPEC.md` §12). Thêm: *heartbeat PLC-scoped hay Station-scoped* (phụ thuộc OQ-03; phát sinh từ CLAUDE §7.3) và *kiểu tín hiệu/reset* (PQ-07…PQ-10 trong `PLC_VENDOR_CLARIFICATION_QUESTIONS_20_11_2026.md`).

---

## 10. Truy vết giao tiếp PLC và bằng chứng message [QĐ R1-C, R1-D]

> Tài liệu này **không tạo địa chỉ**: địa chỉ nêu ở đây chỉ là **trích dẫn nguồn** (§5) hoặc "chưa có". Giao diện 20/11 **chưa được duyệt** (OQ-03); giá trị/ý nghĩa tín hiệu khác nhau giữa hai bản workbook (§3) nên **mọi diễn giải phải ghi kèm phiên bản giao diện đã dùng**.

### 10.1 Phạm vi

- **Trạm PLC-managed:** mọi message đọc/ghi giữa WCS và PLC trong khuôn khổ một Execution Step (LIFECYCLE §10–§11) được lưu bằng chứng thô, gắn Step.
- **Trạm WCS-managed (P1–P4, M5–M6):** **không** có message PLC (D-07). Bằng chứng là **sự kiện xác nhận thủ công/PDA** (ai, khi nào, thiết bị, Rack, Location) – mức **E6** nếu đủ điều kiện D-05; ghi như sự kiện Inbound loại `OperatorConfirmation` với `StepId`.
- **Heartbeat** (`HeartBit`, M9200 theo workbook working tree): thuộc phạm vi PLC, **chưa phân loại PLC-scoped/Station-scoped** (CLAUDE §7.3); đề xuất ghi như message PLC-scoped (`StepId = null`, tầng 2) cho tới khi được phân loại.

### 10.2 Message theo tín hiệu (nguồn 20/11 – chưa duyệt)

| Tín hiệu | Chiều | Thao tác của WCS | Message cần lưu | Mức bằng chứng đạt được | Điều KHÔNG được kết luận |
|---|---|---|---|---|---|
| `PLC_MissionRequest` | PLC → SW | READ/OBSERVE | Frame đọc phát hiện giá trị 1/2; giá trị trước/sau | E2 → E3 → E4 (WCS diễn giải "yêu cầu Pick/Drop") | Không chứng minh Rack hiện diện/danh tính (D-05) |
| `Mission_Feedback` | SW → PLC | WRITE | Lệnh ghi + phản hồi MC | E1 → E2 (MC EndCode=0) | **Không** ghi 1–3 làm `RobotCode` (D-2, ADR-0011); không chứng minh PLC "đã hiểu" |
| `AGV_RequestTranfer` | SW → PLC | WRITE | Lệnh ghi + phản hồi | E1 → E2 | Không chứng minh PLC đã mở cửa/kẹp; không chứng minh AMR thực sự đã đến (nguồn "đã đến" là RCS – PQ-23) |
| `PLC_ReaddyTranfer` | PLC → SW | READ/OBSERVE | Frame đọc quan sát 1/2/10 | E2 → E3 → E4 (WCS ghi nhận **permission đã được cấp**: 1 = Pick, 2 = Drop) | **Không** phải bằng chứng cửa/kẹp đã mở (cần tín hiệu riêng – OQ-48); không suy trạng thái vật lý; không phải hoàn tất bàn giao |
| `AGV_TranferCompleted` | SW → PLC | WRITE | **Chưa thể tạo message**: workbook **không có địa chỉ** (16/16 trạm) | — | Ghi nhận bản ghi `Blocked: ADDRESS_NOT_CONFIGURED` (không có frame); **không** suy đoán địa chỉ |
| `PLC_MissionCompleted` | PLC → SW | READ/OBSERVE | Frame đọc quan sát 1/2 (nghĩa của 1/2 theo **bản workbook đã ghi trong tham chiếu phiên bản**) | E3 → E5 (xác nhận nghiệp vụ của PLC) | **Không** đủ để kết luận Rack đã bàn giao (D-05, ADR-0011 D-4); cần E6 |
| `HeartBit` | SW → PLC | WRITE định kỳ | Lấy mẫu/đệm vòng; sự kiện timeout 10 s | E1 → E2 | Không chứng minh PLC logic đang chạy ngoài việc phản hồi giao thức |

### 10.3 Nội dung bắt buộc của một message PLC

`MessageId`, `CommTransactionId`, `StepId`, `AttemptNo`, **`PlcSeq`** (số thứ tự đơn điệu theo PLC – tái dựng xen kẽ giữa các Station của cùng PLC vì mọi I/O của một PLC đi qua một hàng đợi tuần tự trong `PlcWorker`), `PlcId`, `StationId`, `SignalName` (khóa `PLC_ID+Station_ID+Signal_Name`), hướng, thao tác, `Device`/`StartAddress`/`Points` **đúng như đã cấu hình khi gửi** (và tham chiếu mẫu `Template_Address_Reference` nếu có), giao thức (MC 3E binary – *đề xuất, chờ PLC xác nhận*), **`RequestHex`/`ResponseHex` thô**, `EndCode`, giá trị parsed + kiểu, **diễn giải của WCS** + **`InterfaceVersionRef`** (phiên bản/hash workbook hoặc cấu hình được dùng để giải nghĩa – bắt buộc vì ý nghĩa giá trị khác nhau theo bản), `SentAt`/`ReceivedAt`, `DurationMs`, `TimeoutMs`, `IsAmbiguous`, mã lỗi/exception, tương quan (`OrderId`, `LegId`, `StationTransactionId`, `CorrelationId`).

### 10.4 Sáu bước handshake ↔ bằng chứng (không gộp mức)

| Bước (Trang_tính1) | Message tối thiểu | Mức tối thiểu để chuyển Step | Ghi chú |
|---|---|---|---|
| 1 PLC gửi yêu cầu | `PLC_MissionRequest` quan sát | E4 | Định danh yêu cầu duy nhất: OQ-05 |
| 2 WCS phản hồi tiếp nhận | `Mission_Feedback` ghi (trạng thái tiếp nhận – D-2) | E2 (nếu cần xác nhận PLC đã nhận lệnh ghi) | Ý nghĩa giá trị: OQ-25 |
| 3 AMR tới cửa | `AGV_RequestTranfer` ghi | E2 | Nguồn sự kiện "đã tới": RCS (không phải PLC) |
| 4 PLC **cho phép** thao tác (permission) | `PLC_ReaddyTranfer` quan sát (1 = Pick, 2 = Drop) | E4 (permission đã ghi nhận) | **Không** phải cửa/kẹp đã mở; ghi chú nguồn "xác nhận đã mở cửa/kẹp" không dùng làm bằng chứng vật lý (OQ-48) |
| 5 AMR đã lấy/đã ra | `AGV_TranferCompleted` ghi | E2 | **Chưa thể** (thiếu địa chỉ) |
| 6 PLC xác nhận | `PLC_MissionCompleted` quan sát | E5, và **E6** cho bàn giao vật lý | Một mình bước 6 không đủ (D-05) |

### 10.5 Bắt giữ quan sát (polling)

Giữ hai tầng theo Spec §16.4: (1) **lần đọc làm đổi trạng thái** của tín hiệu mà một Step đang chờ được lưu **đầy đủ, bền**, gắn Step; (2) polling nền lấy mẫu/đệm vòng, **mất mát được đếm**. Giá trị đổi không có Step chờ → `Unmatched`. Chiến lược chi tiết/dung lượng: OQ-39. Hiện `plc_exchange_log`: giới hạn số dòng theo PLC, lấy mẫu READ OK, **luôn giữ lỗi và mọi WRITE** (`PlcExchangeRecorder.cs:10-18`).

### 10.6 Ghi bị chặn bởi chính sách

Mọi lệnh ghi bị từ chối bởi chính sách (chế độ, danh sách cho phép, tín hiệu bị chặn, chưa được PLC engineer duyệt, thiếu địa chỉ) phải để lại **bản ghi** `Refused` kèm lý do (đã có log `HANDSHAKE_WRITE_REFUSED` – `Handshake.cs:130`); không tạo frame; không tính là đã gửi.

### 10.7 Đối chiếu code

| Nội dung | Code hiện tại | Kết luận |
|---|---|---|
| Raw hex request/response, EndCode, DurationMs, IsAmbiguous | `PlcExchange` (`PlcModels.cs:137-138`), `plc_exchange_log` | **KEEP** (mở rộng) |
| Gắn Order/Leg/Step/MessageId/`PlcSeq`/parsed/diễn giải/`InterfaceVersionRef` | **Không có** | **MODIFY** |
| Ghi lỗi và mọi WRITE luôn giữ | Có | **KEEP** |
| Kênh `DropOldest`, mất lô khi DB lỗi | `PlcExchangeRecorder.cs:14-18` | **REPLACE** cho tầng 1 (Spec §16.4) |
| Heartbeat/ghi 20/11 | Chưa có (Phase 0) | **OPEN** theo OQ-03/06 |

### 10.8 Thang bằng chứng E0–E6 áp dụng cho PLC (chuẩn hóa Revision 1.1)

Thang chuẩn: **E0** chuẩn bị · **E1** đã gửi · **E2** nhận phản hồi giao thức · **E3** đã phân tích · **E4** WCS đã áp dụng xử lý nghiệp vụ · **E5** hệ thống ngoài xác nhận nghiệp vụ · **E6** WCS xác nhận hoàn tất vật lý (định nghĩa đầy đủ và 6 quy tắc: `PHASE2_TRANSPORT_JOB_LIFECYCLE.md` §11.2). Với PLC:

- Ghi (WRITE): E0 → E1 → E2 (phản hồi MC; `EndCode` = 0 chỉ là PLC chấp nhận lệnh ghi **ở tầng giao thức**, không chứng minh PLC logic đã thực hiện).
- Đọc/quan sát (READ/OBSERVE): E2 (phản hồi đọc) → E3 (giải mã) → E4 (WCS áp dụng diễn giải).
- **PLC permission** (`PLC_ReaddyTranfer`) và **PLC xác nhận nghiệp vụ** (`PLC_MissionCompleted`, E5) **không** phải E6; **E6** chỉ do WCS xác nhận khi có bằng chứng vật lý (D-05; nguồn: OQ-12).
- Mức cao **không** suy ra từ mức thấp. Không khẳng định WCS có cảm biến/cơ chế xác nhận vật lý chưa được xác nhận.

### 10.9 Ghi PLC khi kết quả chưa xác định (liên hệ Revision 1.1 – Issue 4)

Nếu một lệnh ghi PLC đã Prepared/Sent nhưng kết quả chưa xác định (không có phản hồi, crash, restart): trạng thái **`Unknown outcome`**; **không retry tự động**; chỉ kiểm tra **đọc-lại** read-only và quyết định có kiểm soát (INV-28; `PHASE2_TRANSPORT_JOB_LIFECYCLE.md` §16). Lệnh ghi bị chặn do thiếu địa chỉ (`AGV_TranferCompleted`) là `Blocked`, **không** phải `Unknown outcome`. Bằng chứng giao tiếp PLC phải thuộc **tầng 1 (ghi bền)** khi gắn Step (Spec §16.8); không tuyên bố zero-loss.

## 11. Open Questions liên quan Revision 1

OQ-03, OQ-05, OQ-06, OQ-25, OQ-39, OQ-40, OQ-41 (đầy đủ: `PHASE2_OPERATIONAL_SPEC.md` §12).

## 12. Revision 1 – thay đổi trong file này

Thêm: dòng Revision (đầu file); §10 truy vết giao tiếp PLC (phạm vi, bảng message theo tín hiệu, nội dung bắt buộc, 6 bước ↔ mức bằng chứng, bắt giữ quan sát, ghi bị chặn, đối chiếu code). **Không đổi** §1–§9 (không tạo địa chỉ, không chọn baseline workbook, `AGV_TranferCompleted` vẫn THIẾU địa chỉ).
## 13. Revision 1.1 – thay đổi trong file này

| Mục | Thay đổi | Issue/Decision |
|---|---|---|
| Đầu file | Hàng Revision 1.1 | — |
| §4 (A1, B1, P1) | Liệt kê PH2-04 (D-09) | Decision 1 |
| §10.2, §10.4 | `PLC_ReaddyTranfer` = permission/ready; sửa mức E của các tín hiệu đọc theo thang chuẩn; bước 4 không còn "PLC mở cửa/kẹp" | Issue 2, 5 |
| §10.8 | Thang E0–E6 áp dụng cho PLC | Issue 5 |
| §10.9 | Ghi PLC `Unknown outcome`, không retry tự động | Issue 4 |
## 14. Readiness Gate và Scope: tác động lên ánh xạ PLC/Station [QĐ D-11, D-13]

### 14.1 Điều kiện PLC/Station thuộc Operational Readiness (không thuộc Publication)

| Điều kiện | Gate | Trạng thái hiện tại | Ghi chú |
|---|---|---|---|
| Ánh xạ IO PLC cho Station của process được **duyệt** (`PORT_MAPPING_NOT_APPROVED`) | **Readiness** | **Chưa duyệt** (CR-029 `PENDING_APPROVAL`) | Workbook 20/11 chưa duyệt **không** là căn cứ Ready |
| Port/PLC bật (`PORT_DISABLED`, `PLC_DISABLED`) | **Readiness** | Không xác minh trong P0 | Suspended khi PLC mất kết nối/không an toàn (Spec §15.2b) |
| Kết nối PLC và handshake đã **kiểm thử** (read-only trước, ghi có kiểm soát sau) | **Readiness** | Chưa | Theo CLAUDE §7.5; không ghi tín hiệu an toàn chỉ để thử |
| Cơ chế xác nhận Pick/Drop tại Station (PLC handshake hoặc thủ công cho P/M) | **Readiness** | P/M thủ công (OQ-21) | E6 bắt buộc (INV, Lifecycle §11) |
| Station/Location tồn tại và hợp lệ | **Publication** (tồn tại) / **Readiness** (đã xác minh) | Location P1–P4/M5 **chưa có bản ghi** (OQ-11) | Phân loại `LOCATION_DISABLED`: OQ-53 |

Hệ quả: process (kể cả PH2-04) có thể **Published** khi qua Publication Gate nhưng **không nhận Request** cho tới khi Ready (Spec §15.2b, §11.4 Flows).

### 14.2 Scope không suy ra từ PLC/Station

- Một PLC phục vụ nhiều Station; **không** coi mỗi PLC là một Queue Ordering Scope và **không** coi cả hệ thống là một Scope (Spec §22.3).
- Giao dịch hoạt động: tối đa **một/PLC** (D-03, ADR-0011 D-1 – mục tiêu, chưa hiện thực); ràng buộc này là **khe PLC** riêng, không xác định thành viên Scope (OQ-55).
- Cấu hình Scope cần dữ liệu được duyệt (OQ-54); danh mục Station/PLC ở §4 **không** đủ để suy Scope.

## 15. Revision 1.2 – thay đổi trong file này

| Mục | Thay đổi | Decision |
|---|---|---|
| Đầu file | Trạng thái, hàng Revision 1.2 | — |
| §9 | Thêm OQ-53, OQ-54, OQ-55 | — |
| §14 | **Mới:** điều kiện PLC/Station thuộc Readiness Gate; Scope không suy từ PLC | D-11, D-13 |
| §1–§8, §10–§13 | **Không đổi** (không địa chỉ mới, không chọn baseline workbook, `AGV_TranferCompleted` vẫn THIẾU địa chỉ) | — |
