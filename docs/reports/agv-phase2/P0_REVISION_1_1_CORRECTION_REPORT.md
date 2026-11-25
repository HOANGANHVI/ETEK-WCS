# P0 Revision 1.1 – Correction, Approval & Consistency – Báo cáo

| | |
|---|---|
| **Ngày** | 2026-11-24 |
| **Nhánh / HEAD** | `main` · `34c45b2` |
| **Phạm vi** | Chỉ sửa 6 file Markdown trong `docs/specification/agv-phase2/` và tạo báo cáo này. Không code, config, DB, migration, workbook, PPTX; không commit/push/deploy |
| **Trạng thái** | **Chờ chủ dự án review.** Không bắt đầu P1 |

## A. Executive Summary

- **Hai quyết định mới đã áp dụng:** **D-09** (PH2-04 gỡ REVIEW, chấp thuận toàn bộ làm baseline, thuộc gói mặc định) và **D-10** (ghim Process Version tại Queue intake).
- **Sáu vấn đề:**

| Issue | Nội dung | Mức xử lý |
|---|---|---|
| 1 | PH2-04 publication & Operational Readiness | **Đã xử lý trong tài liệu.** Tách 5 mức L1–L5; ghi **mâu thuẫn thiết kế DC-01** (publish gate ↔ gói mặc định Published) – **chưa giải quyết, cần quyết định** (OQ-44) |
| 2 | `PLC_ReaddyTranfer` | **Đã xử lý.** = permission/ready (1 = Pick, 2 = Drop, 10 = lỗi); không phải bằng chứng cửa/kẹp đã mở; cần tín hiệu riêng (OQ-48) |
| 3 | Append-only & Retention | **Đã xử lý.** Không bất biến tuyệt đối; Retention Service có audit tối thiểu; hold sự cố/đối soát/điều tra |
| 4 | Bằng chứng bền vững & đường điều khiển không chặn | **Đã xử lý ở mức đặc tả** (7 lỗi, điểm dừng, `Unknown outcome`, không auto-retry PLC write). **Không tuyên bố zero-loss**; mức bảo đảm = OPEN (OQ-47) |
| 5 | Chuẩn hóa E0–E6 | **Đã xử lý** ở cả 6 file; đổi mã sự kiện vòng đời E1–E13 → EV-1…EV-13 để không trùng |
| 6 | Ghim Version tại Queue intake | **Đã xử lý.** Queue Request, `NeedsDecision`, Mermaid, test A–D (+5) |

- **Phát hiện bổ sung quan trọng (từ kiểm tra sơ đồ PPTX):** Revision 1 **hiểu sai** nút "AGV hết nhiệm vụ và về điểm chờ" là kết quả của nhánh NG. Theo connector XML, đó là **điểm kết thúc sau nhánh thành công**; **NG nối ngược về quyết định trước = vòng chờ** (slide 4–8). Điều này **loại bỏ** xung đột giả giữa slide và D-02/D-04 ở PH2-01/02/03/05 và khớp bước 6 của D-09 cho PH2-04. Chỉ slide 9 (PH2-06) có nút "thông báo lỗi" thật.
- **Còn OPEN (khác với PH2-04 có được triển khai hay không):** xem mục I.

## B. Decision Implementation

### B.1 PH2-04 – D-09

| Hạng mục | Thực hiện | Ở đâu |
|---|---|---|
| Gỡ REVIEW | Loại kết luận PROTECTED/REVIEW; viết lại toàn bộ §5 thành baseline đã chấp thuận (14 mục + phân loại mâu thuẫn + đối chiếu code/test) | Process Flows §5, §8, §9, §11.4 |
| `reviewBlocked=true` | **Ghi nhận cần gỡ bằng CR P1-G** (dữ liệu `reference-processes.json`, test `Cr010ProcessConfigTests.cs:183-184`). **Không sửa** file (ngoài phạm vi: cấm sửa code/config/dữ liệu) | Process Flows §5.4; Spec §13 (P1-G) |
| Không dùng `PROCESS_REVIEW_BLOCKED` làm expected | Thay T-FL-05; T-LG-03 giới hạn BP-04 Phase 1; T-DP-01 gồm PH2-04; thêm T-P4-01…12; cơ chế REVIEW chung vẫn kiểm bằng process khác (T-P4-02) | Acceptance Tests §21 |
| D-08/BR-02 | D-09 **thay thế hạn chế REVIEW chỉ đối với PH2-04**; mọi phần bảo vệ khác của D-08 giữ nguyên (A3/A4, D1/D2, PER_AGV, dữ liệu, đối soát, không đánh dấu Rack đến đích khi mới dispatch). INV-11 viết lại. Cần CR cập nhật BR-02, CLAUDE.md §2, ADR-0008 §5 (P1-G); **P0 không sửa các file đó** | Spec §2 mục 5, §9 INV-11, §10 F-13 |
| Vào gói mặc định | PH2-04 thuộc PH2-01…06; tham gia vòng đời Draft → Pending Approval → Approved → Published | Spec §15.2, Flows §11.4 |
| Baseline nghiệp vụ | A1/A2 trước → P1/P2 thay thế → không phù hợp thì nhánh chờ; chỉ Full Rack; không chọn Empty/Rack sai Origin/nguồn chưa duyệt | Flows §5; Rack §5, V9a–V9d |
| Không đặt lại câu hỏi PH2-04 | OQ-31 **đóng**; không còn OQ/ghi chú "chờ xác nhận lại BP-04" cho PH2-04 | Spec §12 |
| BP-04 (Phase 1) | Nằm **ngoài** D-09; ghi OQ-46 (không dùng làm căn cứ chặn PH2-04) | Spec §12 OQ-46 |

**Phân biệt bốn mức (không gộp):** L1 Business Approved · L2 Published · L3 đủ điều kiện nhận yêu cầu · L4 Operational Readiness · L5 Technical commissioning readiness (Spec §15.2b, Flows §11.4). Hiện: **L1 đạt cho PH2-04; L2 Not Verified; L4, L5 chưa đạt** – không mức nào được ghi đạt khi chưa có evidence.

**Phụ thuộc kỹ thuật vẫn phải xác minh (không đảo ngược D-09):** Location P1/P2 (OQ-11); ánh xạ IO PLC A1/A2, B1/B2 chưa duyệt (OQ-03/04/06); trigger conflict với BP-0x (OQ-28); vị trí chờ (OQ-14/18); Origin/FIFO (OQ-16/26/30); khởi tạo handshake do WCS tại A1/A2 (OQ-27).

**Mâu thuẫn thiết kế DC-01 (không tự bỏ qua gate, không tạo code):** publish gate hiện gắn Published với điều kiện kỹ thuật → gói mặc định (gồm PH2-04) không thể "khởi tạo Published" khi chưa đạt. Ba phương án (a) giữ gate, (b) tách gate: Published kiểm cấu trúc/nghiệp vụ, mức sẵn sàng chặn thực thi, (c) cờ sẵn sàng độc lập – **chưa chọn** (OQ-44).

### B.2 Queue Version pin – D-10

- Luồng thống nhất `Request Intake → Process Selection → Process Version Pinning → Queue Waiting → Queue Dispatch → Transport Order Creation` có **sơ đồ Mermaid** ở Spec §15.4b và Flows §11.2b.
- Thay mọi chỗ "ghim lúc tạo Order": Spec §15.4 mục 5, §9 INV-26, Flows §11.2 bước 4 và §11.3, Lifecycle §2.5/EV-1/EV-2/EV-3/§10.2, Rack §13.1/§13.3.
- **Queue Request** (thay "Queue Item"): `Received → NeedsDecision/Queued → Active → OrderCreated → Done`; dữ liệu ghim (`ProcessId`, `ProcessVersionId`, số version, content hash, `PinnedAt`, `PinSource`); truy vết Queue Request → Order → Leg.
- **`NeedsDecision`:** không ghim, không gán latest, không tạo Order, cảnh báo operator; sau giải quyết ghi audit (người, thời điểm, process, Version, lý do). Rack §13.1 thêm bản ghi `ProcessVersionPinned`, `QueueNeedsDecision`/`QueueDecisionResolved`.
- **Biên chưa quyết (OQ-45):** Version đã ghim bị Disabled trước dispatch; Version dùng khi giải quyết `NeedsDecision` (đề xuất: Version Published của process được chọn tại thời điểm giải quyết, ghi rõ trong audit).

## C. Change Log

| File | Section | Previous issue | Change made | Status |
|---|---|---|---|---|
| OPERATIONAL_SPEC | Đầu file | — | Revision 1.1, nhãn D-09/D-10 | Done |
| OPERATIONAL_SPEC | §2 mục 5–7 | D-08 còn REVIEW cho PH2-04 | D-09 điều chỉnh D-08/BR-02 chỉ cho PH2-04; D-10; 5 mức L1–L5 | Done |
| OPERATIONAL_SPEC | §3 | Thiếu thuật ngữ | Queue Request, Process Version Pin, E0–E6 | Done |
| OPERATIONAL_SPEC | §5, §6 | Luồng thiếu Queue Request | Luồng intake D-10; Queue Request trước Order | Done |
| OPERATIONAL_SPEC | §9 | INV-11/18/23; thiếu INV mới | Sửa INV-11/18/23; thêm INV-26…29 | Done |
| OPERATIONAL_SPEC | §10 | F-13 PROTECTED | F-13 viết lại (REPLACE dữ liệu/test; KEEP cơ chế chung); F-22/23/24 | Done |
| OPERATIONAL_SPEC | §12 | OQ-31, OQ-17, OQ-24, OQ-35 lỗi thời | OQ-31 đóng; OQ-17/24/35 sửa; OQ-39 mở rộng; thêm OQ-44…48 | Done |
| OPERATIONAL_SPEC | §13 | Thiếu CR | P1-E sửa; P1-G, P1-H mới; P2-B, P2-D mở rộng | Done |
| OPERATIONAL_SPEC | §15.2, §15.2b | PH2-04 loại khỏi gói; chưa tách mức | Gói gồm PH2-04; L1–L5; DC-01 | Done |
| OPERATIONAL_SPEC | §15.4, §15.4b | Ghim tại Order | Ghim tại Queue intake; Mermaid; Queue Request | Done |
| OPERATIONAL_SPEC | §16.4, §16.6–16.8 | Append-only tuyệt đối; "không rơi mất" | Append-only có retention; Retention Service; ghi bền, 7 lỗi, điểm dừng; không zero-loss | Done |
| OPERATIONAL_SPEC | §17 | E0–E6 chưa chuẩn | Bảng E0–E6 chuẩn + 6 quy tắc | Done |
| OPERATIONAL_SPEC | §19, §21 | Xung đột 1–2 | Cập nhật; log thay đổi | Done |
| PROCESS_FLOWS | Đầu file, §0 | — | Revision 1.1; ghi chú nguồn sơ đồ | Done |
| PROCESS_FLOWS | §2.1–§2.2, §3, §4 | Hiểu sai "AGV hết nhiệm vụ" là NG | Sửa theo connector XML; OQ-17 thu hẹp | Done |
| PROCESS_FLOWS | §5 | PH2-04 PROTECTED/REVIEW | **Viết lại toàn bộ** baseline D-09 | Done |
| PROCESS_FLOWS | §8, §9 | BP-04/PH2-04 PROTECTED | PH2-04 đủ nguồn; BP-04 Phase 1 ngoài D-09 | Done |
| PROCESS_FLOWS | §11.2, §11.2b, §11.3 | Ghim lúc tạo Order | Ghim tại intake + Mermaid | Done |
| PROCESS_FLOWS | §11.4, §11.6 | Bảng "không Published" | L1–L5; DC-01; PH2-04 trong gói; thêm hàng PH2-04 | Done |
| STATION_IO_MAPPING | §4, §10.2, §10.4 | `PLC_ReaddyTranfer` = "cửa/kẹp mở" | Permission/ready; mức E đúng thang; OQ-48 | Done |
| STATION_IO_MAPPING | §10.8, §10.9 | Chưa có | E0–E6 cho PLC; ghi PLC `Unknown outcome` | Done |
| TRANSPORT_JOB_LIFECYCLE | §1, §2.5, §3 | Queue Item; E1–E13 trùng E0–E6 | Queue Request; EV-1…EV-13; EV-1/2/3 viết lại | Done |
| TRANSPORT_JOB_LIFECYCLE | §10.2, §10.4 | Version ghim tại Order | Version ghim tại intake; `QueueRequestId` | Done |
| TRANSPORT_JOB_LIFECYCLE | §11.2 | E0–E6 chưa chuẩn | Thang chuẩn + 6 quy tắc | Done |
| TRANSPORT_JOB_LIFECYCLE | §16, §17 | Thiếu recovery; `PLC_ReaddyTranfer` | Command lifecycle, điểm dừng, `Unknown outcome`; diễn giải permission | Done |
| RACK_BUFFER_QUEUE | §5, §10 (V9) | V9 "không đặc tả (REVIEW)" | Ghi chú PH2-04; V9a–V9d | Done |
| RACK_BUFFER_QUEUE | §13.1, §13.3 | Thiếu bản ghi ghim | `ProcessVersionPinned`, `QueueNeedsDecision`/`QueueDecisionResolved` | Done |
| ACCEPTANCE_TESTS | T-FL-05 | Kỳ vọng `PROCESS_REVIEW_BLOCKED` | Thay: A1/A2 là nguồn ưu tiên | Done |
| ACCEPTANCE_TESTS | T-LG-03, T-DP-01, T-DP-08 | REVIEW/OQ-31/ambiguity cũ | Sửa theo D-09/D-10 | Done |
| ACCEPTANCE_TESTS | T-CT-03, T-CT-12, T-OB-05 | E-level/append-only cũ | Sửa theo thang chuẩn/retention | Done |
| ACCEPTANCE_TESTS | §21–§28 | Thiếu test | +55 test mới | Done |

## D. PH2-04 Consistency Review

| Điểm | Kết luận (nhất quán trong 6 file) |
|---|---|
| A1/A2 là nguồn ưu tiên | ✔ Flows §5.2 (mục 5–6), Rack §5, T-FL-05, T-P4-03 |
| P1/P2 là nguồn thay thế | ✔ chỉ khi A1/A2 không có Full Rack phù hợp |
| Rack phải là Full Rack phù hợp | ✔ `RackPresent(Full)`; Origin phù hợp + FIFO ở Buffer (OQ-16/26/30) |
| Không chọn Empty Rack | ✔ Flows §5.2 mục 3; Rack V9b; T-P4-04 |
| Nhánh không có Rack phù hợp | ✔ Chờ theo nhánh chờ (vòng kiểm tra lại A1/A2 → P1/P2); không chọn nguồn khác/không lỗi/không hủy; T-P4-06 |
| Vị trí chờ trung gian | Slide 7 id 33; mã vị trí và ai điều AMR: **OPEN OQ-14/18** |
| Slide 7 ↔ matrix R11 | Nhãn "P1,P2 có vị trí trống?" = **lỗi nhãn nguồn**, đọc là "có Full Rack phù hợp" (Flows §5.3 mục 1); matrix R11 khớp (A1/A2, P1/P2 thay thế, giao tại B1/B2); sơ đồ **không vẽ** bước thả vào B1/B2 (khoảng trống sơ đồ) |
| D-08/BR-02 | D-09 thay thế REVIEW chỉ cho PH2-04; phần bảo vệ khác giữ nguyên; BP-04 Phase 1 ngoài D-09 (OQ-46) |
| Lifecycle & Publish | PH2-04 tham gia Draft → Pending Approval → Approved → Published; Published ≠ sẵn sàng vận hành (L1–L5) |
| Trigger conflict | Kiểm tra theo gate chung (trigger B1/B2, Delivery, Full vs BP-04 Phase 1/process khác – OQ-28/46); **không** dùng để về REVIEW (T-P4-08) |
| PLC mapping | `PORT_MAPPING_NOT_APPROVED` là điều kiện kỹ thuật (L5), không phủ nhận L1 (T-P4-09, OQ-03/44) |

**Phân loại:** *Business baseline đã chấp thuận* – PH2-04 (D-09). *Chi tiết tài liệu đã hiệu chỉnh* – nhãn slide 7, "AGV hết nhiệm vụ", PROTECTED/REVIEW, OQ-24 phần slide 7, OQ-31. *Technical dependencies chưa xác minh* – Location, IO mapping, trigger conflict, vị trí chờ, Origin/FIFO, handshake do WCS khởi tạo, JSON/test/BR-02 cần CR.

## E. Cross-document Consistency

Đã đối chiếu giữa 6 file: PH2-04 lifecycle/publication · baseline A1/A2 → P1/P2 → B1/B2 · nguồn Rack & Origin/FIFO · nhánh chờ/vị trí chờ · trigger conflict · D-08/BR-02 · Operational Readiness L1–L5 · pin Process Version · Queue Request lifecycle · Job/Order/Leg · Station Transaction · PLC handshake · xác nhận Pick/Drop vật lý · E0–E6 · append-only/Retention · outbox/spool · reconciliation · ID test.

Kiểm tra tự động (bằng script, **không** dùng để tuyên bố test chạy): 0 bảng lệch cột; 48 OQ (mọi OQ được tham chiếu đều có định nghĩa); 160 test, **0 ID trùng, 0 tham chiếu test thiếu**; 2 khối Mermaid (cú pháp kiểm thủ công; **không** render bằng công cụ vì không có trong môi trường); INV-01…29 liên tục. Còn nhắc "PH2-04 REVIEW" chỉ ở **log thay đổi, mục lịch sử, và phần giải thích thay thế**.

## F. Acceptance Tests

| | Trước (Revision 1) | Sau (Revision 1.1) |
|---|---|---|
| Số test | 105 (49 + 56) | **160** (+55) |
| Đã chạy / Passed | 0 / 0 | **0 / 0** (mọi test `NOT RUN`) |
| Sửa (giữ ID) | — | T-FL-05, T-LG-03, T-DP-01, T-DP-08, T-CT-03, T-CT-12, T-OB-05 |
| Mới | — | T-P4-01…12 (12), T-PIN-A…I (9), T-RD-01…06 (6), T-RT-01…08 (8), T-DU-01…13 (13), T-EV-01…07 (7) |
| Bao phủ Test A–D (pin) | — | T-PIN-A (Version không đổi), T-PIN-B (Publish V2), T-PIN-C (`NeedsDecision`), T-PIN-D (truy vết) + T-PIN-E…I |
| BLOCKED theo tiêu đề | (không thống kê trước đó) | 45 (kỳ vọng chưa xác định vì OQ) |

Không xóa test. Test hiện có trong code (`Cr010ProcessConfigTests.cs:183-184`, `Cr010MultiLegTests.cs:145`) **không** được sửa trong P0; việc cập nhật dành cho CR.

## G. Visual Verification

**Phương pháp:** PPTX (`ETEK Process AGV Phase 2 12.08.26.pptx`) được giải nén; slide 4–9 là **shape + connector** PowerPoint, không có hình nhúng. Đã đọc cấu trúc XML (vị trí, loại hình, `stCxn/endCxn` của từng connector) để xác định mũi tên và nhánh. Hình layout nhúng của slide 3 đã xem trực tiếp (không có nhãn trạm). Trình render PPTX **không có** trong môi trường nên **chưa xem trực quan** slide 4–9.

| Slide | Phát hiện chính (cấu trúc) | Độ chắc chắn |
|---|---|---|
| 4 | NG của "M5…?" nối ngược về "B1,B2…?" (vòng chờ); "AGV hết nhiệm vụ" là kết thúc sau thả OK | Cao |
| 5 | Nhánh OK → kết thúc; NG của P: connector có đầu/cuối cùng gắn quyết định B (vòng quay về) | **Trung bình** (connector bất thường) |
| 6 | NG của P nối ngược về A; thả vào **"P1"** (chỉ P1) | Cao |
| 7 | A NG → P; P NG nối ngược A (vòng chờ); nhãn P "có vị trí trống?" lệch với "lấy rack đầy"; sơ đồ không vẽ thả vào B1/B2; bước "ra vị trí chờ trung gian" đặt **trước** quyết định nguồn | Cao |
| 8 | NG của P nối ngược về A (AMR đang giữ Rack tại vị trí chờ trung gian) | Cao |
| 9 | NG của P → "Thông báo gửi lỗi" (nút lỗi thật, không vòng chờ) | Cao |

**Không thể xác minh:** hình ảnh render thực tế (màu, đường nét, vị trí hiển thị); ý đồ của tác giả cho connector bất thường (slide 5); ngày PPTX (tên file `12.08.26` ↔ slide 1 `20/1/2026`, OQ-23).

## H. Scope Verification

- **File thay đổi:** `docs/specification/agv-phase2/PHASE2_{OPERATIONAL_SPEC,PROCESS_FLOWS,STATION_IO_MAPPING,TRANSPORT_JOB_LIFECYCLE,RACK_BUFFER_QUEUE_RULES,ACCEPTANCE_TESTS}.md` (6) và **tạo mới** `docs/reports/agv-phase2/P0_REVISION_1_1_CORRECTION_REPORT.md`.
- `git status` trước/sau: chỉ khác **thư mục báo cáo mới** (`docs/reports/` đã untracked từ trước). Các thay đổi chưa commit của người dùng (UI redesign, workbook 20/11 `M`, IO Map v2 `D`, handoff + PPTX untracked, `docs/reports/`) **nguyên vẹn**.
- **Không** sửa: source code, frontend/backend, DB schema/migration, config, workbook/XLSX, PPTX, tài liệu Phase 0 audit (`docs/reports/agv-phase2-assessment/`), `reference-processes.json`, BR-02/CLAUDE.md/ADR. **Không** commit, push, deploy; không chạy lệnh thay đổi dữ liệu runtime.
- Các tệp tạm (script/ảnh/XML) đặt trong thư mục scratchpad ngoài repo.

## I. Remaining Open Questions

Chỉ nội dung **chưa có cơ sở kết luận** (không có câu hỏi "PH2-04 có được triển khai hay không"). Bảng đầy đủ (lý do, tác động, phương án, bên xác nhận): `PHASE2_OPERATIONAL_SPEC.md` §12.

| Nhóm | OQ |
|---|---|
| **Chặn thiết kế (B)** | OQ-01 (strict/controlled FIFO), OQ-02 (mốc kết thúc giao dịch PLC), OQ-03/04 (workbook baseline, CR-029), OQ-05 (chống duplicate không có `Request_ID`), OQ-06 (địa chỉ thiếu), OQ-12 (nguồn Rack presence/identity), OQ-15 (reconciliation CR-010), OQ-16 (định nghĩa Origin), OQ-27 (handshake do WCS khởi tạo), OQ-29 (X4/X6, G1,G2/G3,G7), OQ-32 (điều kiện tiên quyết gói mặc định), **OQ-44 (DC-01 gate)**, **OQ-47 (mức bảo đảm bằng chứng bền vững)** |
| Ảnh hưởng lớn (H) | OQ-07/08/09/10/11 (trạm), OQ-13 (chế độ RCS), OQ-14/18 (vị trí chờ, thời điểm giữ chỗ), OQ-17 (chỉ còn PH2-06/slide 9), OQ-22 (`noBind`), OQ-28 (cutover BP ↔ PH2), OQ-30 (phạm vi FIFO), OQ-33…36 (phê duyệt/ambiguity/ranh giới cấu hình), OQ-37 (danh mục Step), OQ-39/40 (retention/raw nhạy cảm), **OQ-45 (biên ghim Version)**, **OQ-48 (tín hiệu xác nhận cửa/kẹp)** |
| Cần biết sớm (M) | OQ-19/20/21/23/24 (còn slide 6)/25/26/38/41/42/43, **OQ-46 (BP-04 Phase 1 & cập nhật tài liệu)** |
| Đã đóng | **OQ-31** (D-09) |

**Quyết định cần chủ dự án để tiếp tục:** (1) OQ-44 – chọn cách xử lý DC-01; (2) OQ-47 – mức bảo đảm/ngân sách độ trễ bằng chứng bền vững; (3) OQ-45/OQ-35 – biên của ghim Version và chủ thể giải quyết `NeedsDecision`; (4) OQ-46 – CR cập nhật tài liệu/dữ liệu cho D-09 và quyết định cho BP-04 Phase 1; (5) các OQ chặn nhóm B ở trên.

**Dừng tại đây – chờ review P0 Revision 1.1. Không tự chuyển sang P1 hoặc implementation.**
