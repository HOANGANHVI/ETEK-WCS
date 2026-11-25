# P2 – Route Traceability Matrix

| | |
|---|---|
| **Ngày** | 2026-11-24 |
| **Quy tắc** | Một tuyến **chỉ** được ghi "Đã xác nhận" khi có **cả** (a) baseline chủ dự án (task P2 §2) **và** (b) nguồn tài liệu/code không mâu thuẫn. Còn lại ghi **Chưa xác định / Xung đột**. Không suy diễn từ "cùng Model". Không có dòng nào ở đây là phê duyệt triển khai |
| **Nhãn nguồn** | **B** = baseline chủ dự án trong task P2 §2 (2026-11-24) · **P0** = `docs/specification/agv-phase2` Rev 1.2 (DRAFT) · **PPTX** = `ETEK Process AGV Phase 2 12.08.26.pptx` (qua P0) · **WB20** = workbook 20/11 (CR-029 `PENDING_APPROVAL`) · **CODE** = repo hiện tại · **CLAUDE** = `CLAUDE.md` |
| **Trạng thái** | `BASELINE` = có trong B, chưa có cấu hình kỹ thuật · `XÁC NHẬN MỘT PHẦN` · `XUNG ĐỘT` · `CHƯA XÁC ĐỊNH` · `PROTECTED` |

## 1. Bản đồ Model – Station

| Product Model | Source CNC | Destination QC | Buffer Positions | Manual QC | Nguồn | Trạng thái |
|---|---|---|---|---|---|---|
| **F-Bal 10R80** | A1, A2 | B1, B2 | P1, P2 | M5 | B; P0 `STATION_IO_MAPPING` §4 (A1/A2 CNC 10R80, B1/B2 QC 10R80); PPTX s2/3 | **BASELINE.** A1,A2,B1,B2 có trong code/IO map (PLC-01/02); **P1, P2, M5 chưa có Location trong DB/code/workbook** (P0 OQ-11) |
| **F-Bal 10R60** | A3, A4 | D1, D2 | Chưa xác định | Chưa xác định | B; P0 (PROTECTED); CLAUDE §2; `appsettings` `ProtectedLocationCodes` | **PROTECTED – UNCHANGED**. Baseline B nói "áp dụng tương tự" ⇒ **XUNG ĐỘT** với CLAUDE (P2-OQ-01); chưa có Buffer/Manual |
| **F-Pis 10R80** | X1, X2 | Y1, Y2 | P3, P4 | M6 | B; PPTX s2/3 (nhãn, không có luồng – P0 OQ-09) | **BASELINE.** X1,X2 có trong IO map; **Y1,Y2 không có trong workbook/code** (P0 OQ-07); **P3,P4,M6 chưa có** |
| **F-Pis 10R60** | X3, X4 | Z1, Z2 | Chưa xác định | Chưa xác định | B; PPTX s2/3 | **BASELINE.** **X4 ↔ X6 xung đột tên** (code/matrix: X6; WB20/PPTX: X4 – P0 OQ-29); Z1,Z2 không có trong workbook/code (OQ-07) |

## 2. Tuyến Full Rack (CNC → QC cùng Model)

| Model | Source CNC | Destination QC | Rack Type | Buffer Positions (tràn) | Manual QC (tràn tiếp) | Allowed Route | Nguồn căn cứ | Trạng thái xác nhận | Giả định chưa phê duyệt |
|---|---|---|---|---|---|---|---|---|---|
| F-Bal 10R80 | A1, A2 | B1, B2 | Full | P1, P2 | M5 | `A1/A2 → B1/B2 → P1/P2 → M5` | B §2.2; P0 PH2-01 (slide 4: B1/B2, P1/P2, M5); code BP-01 (Phase 1: B1/B2, F1/F2, F5) | **Xác nhận (B + P0 nhất quán)** về thứ tự; **kỹ thuật chưa sẵn sàng** (P1/P2/M5 chưa là Location; PH2-01 `AfterPick` bị chặn bởi F1) | Thứ tự P1 trước P2; Scope key (SD-1…8); Rack "tiếp theo" ra M5 theo FIFO nào; chọn `noBind` (P0 OQ-22) |
| F-Bal 10R60 | A3, A4 | D1, D2 | Full | Chưa xác định | Chưa xác định | `A3/A4 → D1/D2` (legacy) | B (liệt kê); P0/CLAUDE: PROTECTED | **XUNG ĐỘT / PROTECTED** – **không** được ghi là tuyến đã duyệt cho Phase 2 | Không áp dụng quy tắc Buffer/Manual |
| F-Pis 10R80 | X1, X2 | Y1, Y2 | Full | P3, P4 | M6 | `X1/X2 → Y1/Y2 → P3/P4 → M6` | B §2.2 "áp dụng tương tự"; P0: không có luồng X (OQ-09) | **BASELINE (chỉ B)** – không có nguồn P0/PPTX mô tả luồng; **CHƯA XÁC ĐỊNH về kỹ thuật** | Mọi chi tiết giống F-Bal đều là giả định (priority, noBind, điều kiện); PLC của Y chưa xác định |
| F-Pis 10R60 | X3, X4 | Z1, Z2 | Full | Chưa xác định | Chưa xác định | `X3/X4 → Z1/Z2` | B §2.2 "áp dụng tương tự" | **BASELINE (chỉ B)**; Buffer/Manual **chưa xác định** ⇒ không có đường tràn | Không tự gán P3/P4/M6 cho nhóm này |

## 3. Tuyến Empty Rack (QC → CNC cùng Model)

| Model | Source QC | Destination CNC | Rack Type | Buffer Positions (tràn) | Manual QC (tràn tiếp) | Allowed Route | Nguồn căn cứ | Trạng thái xác nhận | Giả định chưa phê duyệt |
|---|---|---|---|---|---|---|---|---|---|
| F-Bal 10R80 | B1, B2 | A1, A2 | Empty | P1, P2 | M5 (theo B) | `B1/B2 → A1/A2 → P1/P2 → M5` | B §2.3; P0 PH2-03 (slide 6: **A1/A2, else P1/P2 – không có M5**; OQ-24: slide ghi P1); code BP-03 | **XUNG ĐỘT MỘT PHẦN** – bước M5 cho Empty có trong B nhưng **không** có trong P0/PPTX (P2-OQ-02) | M5 làm lựa chọn cuối cho Empty; "chờ" khi không còn Buffer |
| F-Bal 10R60 | D1, D2 | A3, A4 | Empty | Chưa xác định | Chưa xác định | `D1/D2 → A3/A4` (legacy) | B; PROTECTED | **XUNG ĐỘT / PROTECTED** | — |
| F-Pis 10R80 | Y1, Y2 | X1, X2 | Empty | P3, P4 | M6 | `Y1/Y2 → X1/X2 → P3/P4 → M6` | B §2.3 "tương tự" | **BASELINE (chỉ B)**; **CHƯA XÁC ĐỊNH về kỹ thuật** | như trên |
| F-Pis 10R60 | Z1, Z2 | X3, X4 | Empty | Chưa xác định | Chưa xác định | `Z1/Z2 → X3/X4` | B §2.3 "tương tự" | **BASELINE (chỉ B)** | Không có đường tràn |

## 4. Tuyến phụ thuộc Buffer / Manual (ngoài hai luồng chính)

| Model | Tuyến | Rack Type | Nguồn căn cứ | Trạng thái | Ghi chú / giả định chưa duyệt |
|---|---|---|---|---|---|
| F-Bal 10R80 | `P1/P2 → B1/B2` giao Rack Full khi QC cần (PH2-04 Option B, D-09) | Full | P0 D-09 (PH2-04: A1/A2 ưu tiên, P1/P2 dự phòng; chờ nếu không có Full Rack phù hợp); B §2.4 | **Xác nhận (D-09)** – **dữ liệu tham chiếu PH2-04 vẫn `reviewBlocked=true` (P1-G chưa làm)**; D-09 **không** mở khóa BP-04 Phase 1 (P0 OQ-46) | Lấy Rack từ Buffer chỉ trong cùng Model (INV-M2 đề xuất) |
| F-Bal 10R80 | `P1/P2 → A1/A2` giao Rack Empty | Empty | P0 PH2-02 (B/P → A); B §2.4 | **Xác nhận (P0)**; kỹ thuật chưa sẵn sàng | Origin + FIFO (P0 D-02 chưa hiện thực) |
| F-Bal 10R80 | `M5 → A1/A2 / P1/P2` (P0 PH2-05 Empty), `M5` (PH2-06 Full) | Empty/Full | P0 PH2-05/06 (slide 8/9); CODE `reference-processes.json` (ManualRequest, trigger M5) | **XUNG ĐỘT với B §2.5** (WCS không tự lấy Rack từ M5/M6 trở lại tự động; cần quy trình tiếp nhận riêng được phê duyệt) – chưa rõ PH2-05/06 có phải quy trình đó (P2-OQ-07) | **Không** được coi là tuyến Manual→Auto đã duyệt |
| F-Pis 10R80 | `P3/P4 → Y1/Y2`, `P3/P4 → X1/X2` | Full / Empty | B §2.4 "lấy Rack từ Buffer đến trạm phù hợp cùng Model" | **BASELINE (chỉ B)**; P0 không có | — |
| F-Bal/F-Pis 10R60 | Mọi tuyến Buffer/Manual | — | B: "Chưa xác định" | **CHƯA XÁC ĐỊNH** – không tự gán | Không có Buffer/Manual ⇒ khi không nhận được: chờ + cảnh báo (đề xuất, chưa duyệt) |

## 5. Quy tắc Buffer và Manual QC theo baseline (đối chiếu nguồn)

| Quy tắc | Nguồn B | Có trong P0/code? | Trạng thái |
|---|---|---|---|
| P1/P2 chỉ F-Bal 10R80; P3/P4 chỉ F-Pis 10R80 | §2.4 | Không (code không có Model) | **BASELINE; chưa có cơ chế thực thi** |
| Mỗi vị trí chứa Full hoặc Empty, không cố định vai trò | §2.4 | `AllowedRackKind.Any` (CODE, `Location.cs:41`) hỗ trợ | **Khả thi bằng cấu hình** (cần Location) |
| WCS quản lý Rack ID, Product Model, Rack Type, Origin Station, Current Location, Reservation | §2.4 | Có Rack ID/Type/Location/Reservation; **không có Model, không có Origin** (CODE) | **THIẾU** (P2-OQ-04, OQ-15) |
| FIFO sau khi lọc | §2.4 | P0 D-02/D-13 | Nhất quán; chưa hiện thực |
| Manual QC không phải Buffer thứ hai; Rack vào M5/M6 rời danh sách tự động; không tự lấy ra | §2.5 | CODE coi `ManualBuffer` ≈ Buffer (`ProcessValidator.cs:149,156`) | **XUNG ĐỘT với code hiện tại** (G6) |
| Tiếp nhận lại cần quy trình riêng được phê duyệt | §2.5 | Chưa có | **CHƯA CÓ** |

## 6. Trạng thái cấu hình Station trong code/IO (để so sánh)

| Station | Trong code/IO Map v2 | Trong WB20 (20/11) | PLC | Ghi chú |
|---|---|---|---|---|
| A1, A2, A3, A4 | ✔ (PLC-01) | ✔ | PLC-01 | A3/A4 PROTECTED |
| B1, B2, D1, D2 | ✔ (PLC-02) | ✔ | PLC-02 | D1/D2 PROTECTED |
| X1, X2, X3 | ✔ (PLC-03) | ✔ | PLC-03 | |
| X4 / X6 | X6 | X4 | PLC-03 | **OPEN** (OQ-29) |
| Y1, Y2, Z1, Z2 | ✘ | ✘ | chưa xác định | **OPEN** (OQ-07) |
| E1, E2, G3, G7 / G1, G2 | ✔ / WB20: E1,E2,G1,G2 | ✔ | PLC-04 | vai trò E/G chưa xác định (OQ-08) |
| P1, P2, M5 | ✘ (không là Location/port) | ✘ | WCS-managed (D-07) | Location chưa tạo (OQ-11) |
| P3, P4, M6 | ✘ | ✘ | WCS-managed | chưa có |

## 7. Giả định chưa được phê duyệt (tổng hợp)

1. Product Model là thuộc tính của Location/Rack/Process (P2-OQ-03/04).
2. Scope key = `Model + hướng luồng` (S2) — chỉ là khuyến nghị có điều kiện (P2-OQ-09).
3. P1/P2 là hai Location capacity 1 (P2-OQ-05).
4. `Rack.Disposition=ManualHandling` làm cơ chế loại khỏi điều phối (P2-OQ-08).
5. Luồng F-Pis giống F-Bal ("áp dụng tương tự") — **chỉ B**, không có nguồn P0/PPTX.
6. Không có đường Buffer/Manual cho nhóm 10R60 (đúng với "chưa xác định" – là hệ quả, không phải quyết định).
7. PH2-05/PH2-06 không phải quy trình tiếp nhận lại (hoặc ngược lại) — **chưa quyết** (P2-OQ-07).
