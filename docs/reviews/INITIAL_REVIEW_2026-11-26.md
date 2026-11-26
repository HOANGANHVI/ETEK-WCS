# INITIAL REVIEW – ETEK WCS (2026-11-26)

| | |
|---|---|
| **Phạm vi** | CR-029, CR-038…CR-041, source `main` = `844c415` |
| **Cách rà soát** | Đọc source các luồng liên quan; đối chiếu test hiện có và database local (chỉ đọc). Không chạy lại test trong lượt rà soát này (lần chạy gần nhất trên cùng source: backend 1293/1293). Chưa đọc hết mọi file; mục chưa đủ bằng chứng ghi **CHƯA KẾT LUẬN** |
| **Thời điểm** | Sau khi CR-041 đã merge. Với CR-041, đây là rà soát **sau** triển khai, không phải trước |
| **Thay đổi trong lượt này** | Chỉ thêm file này. Không sửa source, schema, cấu hình |

---

## 0. Cảnh báo cần biết ngay

1. **Không bấm "Tạo bản nháp theo danh mục" trên quy trình đang Published** cho tới khi sửa GAP-01: nút này đổi ngay tên, loại yêu cầu, loại rack, vị trí kích hoạt và cờ REVIEW của quy trình đang chạy, dù bản nháp chưa được công bố. (Backend đang chạy là bản cũ nên hiện nút này báo lỗi.)
2. **`PH2-05` đang Published** trên database local (`admin`, 2026-11-26 08:42) dù danh mục ghi chưa được duyệt. Đang NotReady. Nên Disable version.
3. **Đính chính CR-041:** câu "PLC thật chưa tạo yêu cầu" và "dùng chung logic với PLC thật" **không đúng** (GAP-02).

---

## 1. Kiến trúc hiện tại và luồng xử lý

```mermaid
flowchart TB
  subgraph Nguon["Nguồn yêu cầu"]
    UI[3 trang vận hành]
    PLCL[PLC thật: PlcStationSignalService<br/>cạnh lên, Plc:Integration:Enabled mặc định tắt]
    SIMOLD[/api/simulator/station-requests<br/>chỉ Development, RealDispatch tắt]
    SIMNEW[Trang mô phỏng PLC]
    INJ[POST /api/routing/plc-requests]
  end
  UI --> TO[TaskOrchestrator - luồng BP từ appsettings]
  UI --> RE[RouteEngine - quy trình trong DB]
  PLCL --> SRP[StationRequestProcessor] --> TO
  SIMOLD --> SRP
  SIMNEW --> H[PlcInputHandler] --> PRS[PlcRequestService] --> RE
  INJ --> PRS
  PHY[PhysicalPlcInputAdapter<br/>không ai gọi] -.-> H
  TO --> RCS[RoutedRcsClient: Simulator / Real theo mode đã ghim]
  RE --> LD[RoutedLegDispatcher: Simulator / Real theo mode đã ghim]
  CB[RCS callback] --> TO
```

| Điểm | Hiện trạng xác minh được |
|---|---|
| Hai engine | Task Phase 1 và Transport Order tách rời; chung bảng rack, reservation; chung khóa RequestKey (CR-041) |
| Queue | Chỉ Transport Order của quy trình có `RequiresReadiness`. Task Phase 1 không xếp hàng |
| Mode | Ghim theo task/order khi tạo |
| Callback RCS | Chỉ chuyển tới engine task. Không có đường callback cho Leg |
| Gửi lại lệnh Real cho Leg | Hỏi RCS trước khi gửi; trạng thái không rõ thì **không gửi lại** |

---

## 2. Bảng đối chiếu yêu cầu

Trạng thái: **ĐÚNG** · **CHƯA ĐỦ** · **SAI** · **CHƯA CÓ** · **CHƯA KẾT LUẬN**.

| ID | Yêu cầu | Hiện tại (source) | Mong muốn | Gap / ảnh hưởng | Đề xuất | Duyệt? | Bằng chứng sau sửa | Trạng thái |
|---|---|---|---|---|---|---|---|---|
| PM-1 | Version đã Publish không bị đổi | Bước/tuyến bất biến; header (loại yêu cầu, rack, trigger, REVIEW) nằm trên Process, `BusinessProcess.Update` không chặn; `ApplyCatalogAsync` ghi đè ngay | Thay đổi chỉ có hiệu lực khi Publish | GAP-01: yêu cầu mới chạy theo header chưa duyệt | Khóa header khi đang có version Published | OQ-42-01 | Test: sửa header/apply-catalog khi Published bị từ chối | **SAI** |
| PM-2 | Quy trình chưa duyệt không công bố được | BP-04 chặn bằng REVIEW. PH2-05 không có chặn | Không công bố quy trình chưa duyệt | PH2-05 đã bị publish | Chặn theo `defaultPublish=false` | OQ-42-02 | Test publish PH2-05 bị từ chối | **CHƯA ĐỦ** |
| PM-3 | Publish / Ready / Suspend / tắt quy trình | Có, audit; chuyển Readiness giới hạn (OQ-51) | Như hiện tại | – | – | Không | Cr035, Cr039, Cr040, Cr041 | ĐÚNG |
| PM-4 | Disable version khi có Order | Order đã điều phối chạy tiếp theo snapshot. Order trong Queue: nếu có version Published khác thì **vẫn chạy version vừa Disable** | Theo quyết định | Admin tắt version lỗi nhưng yêu cầu đang chờ vẫn chạy nó | Phân biệt "bị thay bởi Publish" và "Admin tắt" | OQ-45 | Test hai trường hợp | CHƯA ĐỦ |
| PE-1 | Tạo Order idempotent, ghim version | Đúng | – | – | – | Không | Cr010, Cr040 | ĐÚNG |
| PE-2 | Real: Leg hoàn tất theo tín hiệu thật | Không callback cho Leg; template Leg chưa có trigger Pick/Drop; chỉ Admin xác nhận | Theo vendor | Order Real không tự hoàn tất | Giữ xác nhận tay, ghi rõ giới hạn | OQ-42-06 | Cần RCS thật | **CHƯA CÓ** |
| PE-3 | Nhật ký chờ gửi lệnh | `LEG_DISPATCH_PENDING` ghi ở mỗi lượt thử khi Blocked/Unknown | Ghi khi lý do đổi | GAP-03: phình bảng sự kiện | Chỉ ghi khi đổi lý do | Không | Test số sự kiện sau N lượt | CHƯA ĐỦ |
| TM-1 | Một danh sách/form chung, chống trùng | CR-041 | – | – | – | Không | Cr041, ops smoke | ĐÚNG (chưa xem trên trình duyệt) |
| TM-2 | Dropdown một nguồn từ DB | BP từ appsettings, PH2 từ DB; BP có thể xuất hiện hai lần | DB quản lý | Hai định nghĩa cho cùng mã | Quyết cắt chuyển | OQ-40-01 | Test catalog | CHƯA ĐỦ |
| RCS-1 | Dual mode, không gửi trùng | Ghim mode; dispatcher hỏi trước khi gửi; task cũ không gửi tới khi Admin nhận | – | – | – | Không | Cr038 | ĐÚNG (mock) |
| MD-1 | Hủy lệnh an toàn | Từ chối khi AMR đang chạy / đang giữ rack | – | Cách xử lý sau đó là thủ công | – | Không | Cr010 | ĐÚNG |
| Q-1 | FIFO theo Scope, Held giữ số | Có | – | – | – | Không | Cr040 | ĐÚNG |
| Q-2 | Giá trị Scope | Không khai báo sẵn → không điều phối | Có giá trị được duyệt | Quy trình PH2 không chạy | Đề xuất ở OQ-54 | OQ-54 | – | CHƯA CÓ (chờ duyệt) |
| Q-3 | Chống/phát hiện deadlock | Chỉ hiển thị "chờ sau" | Có cách phát hiện | Scope có thể tắc vô hạn | Cảnh báo theo ngưỡng | OQ-42-04 | Test cảnh báo | CHƯA CÓ |
| Q-4 | FIFO với yêu cầu ngoài Queue | Task BP có thể lấy chỗ A/B trước | Theo quyết định | Mất thứ tự giữa hai engine | Gắn với cắt chuyển BP | OQ-40-01 | – | CHƯA ĐỦ |
| Q-5 | Một giao dịch mỗi PLC (D-1) | Một mỗi **port** | Theo D-1 | Đã duyệt, chưa làm; ranh giới Handshake chưa rõ (PQ-26) | Chờ PLC | PQ-26 | – | CHƯA CÓ |
| PLC-1 | Một logic cho PLC thật và mô phỏng (CR-041 mục 6.2) | PLC thật (khi bật) → `StationRequestProcessor` → Task. Mô phỏng → `PlcInputHandler` → Order. `PhysicalPlcInputAdapter` không được gọi | Hội tụ một bộ xử lý | GAP-02: thử bằng mô phỏng không chứng minh được hành vi PLC thật | Định tuyến theo trạm tại bộ xử lý chung | OQ-42-03 | Test cùng tín hiệu, cùng kết quả qua hai đường | **SAI** |
| PLC-2 | Trigger, Request_ID, ack | Đường cũ: cạnh lên, thiếu Request_ID thì đọc lại; không ghi Request_Ack | Theo PLC xác nhận | Reset, timeout chưa định nghĩa | Giữ cạnh lên + Request_ID | OQ-41-02 | – | CHƯA ĐỦ |
| PLC-3 | Reconnect / restart | Trạng thái cạnh giữ trong bộ nhớ: sau restart bit đang ON được coi là cạnh mới; chống trùng nhờ khóa Request_ID. Đổi Request_ID khi bit vẫn ON không tạo cạnh mới (bị bỏ qua cho tới khi restart) | Theo PLC | Phụ thuộc hành vi PLC | Hỏi kỹ sư PLC | OQ-41-02 | – | CHƯA KẾT LUẬN |
| PLC-4 | IO mapping 20/11 | Chưa triển khai (đúng cổng CR-029) | – | – | – | PQ | – | CHƯA CÓ (đúng chủ ý) |
| SIM-1 | Đặt/reset/lịch sử từng IO, Admin | Có; 13 tín hiệu, 7 được xử lý | – | – | – | Không | Cr040, Cr041 | ĐÚNG |
| SIM-2 | Mô phỏng phản hồi (Transfer_Complete…) | Chỉ hiển thị | Theo quyết định | Không chạy trọn vòng đời từ mô phỏng IO | Giữ tới khi PLC định nghĩa | OQ-42-05 | – | CHƯA CÓ |
| SIM-3 | Không lọt vào RCS thật | Đường mới chặn ở Real. Đường cũ chỉ mở khi RealDispatch tắt | – | An toàn nhờ cấu hình khởi động | Gộp/khóa đường cũ | OQ-42-03 | Test cấu hình | ĐÚNG |
| RK-1 | Thêm/sửa/chuyển/xóa rack có bảo vệ (Admin) | Có | – | Hộp thoại thêm chưa chọn product model; lỗi gọi kiểm tra khóa không hiển thị | Bổ sung | Không | Cr040, Cr041 | CHƯA ĐỦ (nhỏ) |
| RK-2 | Chỉ Admin xóa rack | `inventory/adjustments` (cả hai vai trò, CR-001) giảm tồn = xóa rack; chỉ tránh rack đang giữ chỗ, **không kiểm tra rack thuộc lệnh chưa xong**, không bắt buộc lý do | Một quy tắc | GAP-04: vòng qua bảo vệ; mâu thuẫn CR-001 ↔ yêu cầu mới | Quyết định | OQ-42-07 | Test điều chỉnh bị từ chối | **CHƯA ĐỦ (mâu thuẫn)** |
| RK-3 | Rack gửi tạm ở Buffer giữa hai chặng không bị lệnh khác lấy | Reservation được tiêu thụ khi Drop; bộ chọn nguồn chỉ loại rack đang giữ chỗ | Rack thuộc lệnh được bảo vệ | Có thể đưa lệnh cũ sang đối soát. Chưa kiểm chứng bằng test | Viết test trước, sửa nếu đúng | Không (test) / có (nếu đổi quy tắc) | Test hai lệnh cùng buffer | CHƯA KẾT LUẬN |
| RC-1 | Restart, đối soát, audit | RECOVERY_REQUIRED, resume yêu cầu PLC, lịch sử giữ; giá trị mô phỏng mất khi restart (chủ ý) | – | – | – | Không | Cr038, Cr041 | ĐÚNG (Simulator/mock) |
| MG-1 | Migration và rollback | 3 migration CR-039/040 đã chạy trên DB local; `AddSimulatedStationTransaction` **chưa** | – | Backend mới cần migrate trước | Sao lưu → migrate | Khi triển khai | Test Up/Down | CHƯA TRIỂN KHAI |

---

## 3. Lỗi và thiếu sót (theo mức độ)

| Mã | Mức | Nội dung | Vị trí |
|---|---|---|---|
| GAP-01 | Cao | Header quy trình đổi được khi đang Published; apply-catalog (CR-041, do tôi thêm) ghi đè ngay | `ProcessAdminService.UpdateAsync`, `ApplyCatalogAsync`; `BusinessProcess.Update` |
| GAP-02 | Cao | Hai đường PLC khác engine; báo cáo CR-041 nói sai | `PlcStationSignalService`, `StationRequestProcessor`, `PlcInputHandler` |
| GAP-04 | Trung bình–cao | Điều chỉnh tồn kho xóa được rack của lệnh chưa xong | `InventoryService.AdjustStockAsync` |
| PM-2 | Trung bình | PH2-05 publish được | `ProcessAdminService.PublishAsync` |
| PE-2 | Trung bình (chặn Real) | Leg Real không có nguồn tự hoàn tất | `RcsCallbackService`, `RealLegDispatcher` |
| GAP-03 | Thấp–trung bình | Sự kiện chờ gửi lệnh ghi lặp | `RouteEngine.DispatchLegAsync` |
| RK-1 | Thấp | Giao diện rack còn thiếu hai điểm | `RackDialog.tsx` |

---

## 4. Câu hỏi cần phê duyệt

Đã chốt, **không hỏi lại**: D-01…D-13, D-1…D-4 (PLC), Published + NotReady cho quy trình mặc định, mô phỏng PLC chỉ cần Admin + xác nhận.

Mỗi câu: **thực tế → ảnh hưởng nếu không quyết → phương án (hệ quả) → đề xuất → phạm vi**.

### Nhóm A – kiến trúc hoặc dữ liệu

**OQ-42-03 · Đường vào của PLC thật**
- Thực tế: GAP-02. · Không quyết: mô phỏng và PLC thật cho kết quả khác nhau.
- (a) Chuyển mọi trạm sang RouteEngine: một logic; BP phải chuyển sang DB trước. (b) Một bộ xử lý chung, chọn engine **theo trạm** bằng cấu hình đã duyệt; mô phỏng đi theo đúng lựa chọn đó: giữ Legacy, mô phỏng phản ánh thật. (c) Giữ nguyên, chỉ ghi rõ trên giao diện: không rủi ro mã, không đạt yêu cầu 6.2.
- **Đề xuất (b).** Phạm vi: `PlcInputHandler`, `PlcStationSignalService`, `StationRequestProcessor`, mô phỏng, cấu hình, test.

**OQ-40-01 + OQ-39-08 · BP và PH2**
- Thực tế: BP chạy từ appsettings; DB cũng có BP Published; BP và PH2 trùng trigger A1/A2, B1/B2. · Không quyết: khi PH2 Ready, yêu cầu PLC vào engine Order không kèm mã bị `PROCESS_AMBIGUOUS`; dropdown hai mục; FIFO bị task BP chen.
- (a) Cắt chuyển theo từng trạm: tắt BP trong DB, PH2 thay thế; rollback được. (b) Đưa BP vào DB và bỏ engine task: một engine, thay đổi lớn, cần migration cấu hình. (c) Giữ song song, quy định ưu tiên: phải định nghĩa ưu tiên mới.
- **Đề xuất (a).** Cần cho biết: trạm nào chuyển trước.

**OQ-42-01 · Header quy trình đã Publish**
- Thực tế: GAP-01.
- (a) Đưa header vào Version: đúng nhất, cần migration và đổi API. (b) Khóa sửa header và apply-catalog khi có version Published (phải Disable trước, hoặc tạo quy trình mới): nhỏ, không migration; trong lúc Disable quy trình không nhận yêu cầu. (c) Chỉ cảnh báo: rủi ro còn nguyên.
- **Đề xuất (b) ngay, (a) sau.**

**OQ-42-07 · Quyền xóa rack qua điều chỉnh tồn kho**
- Thực tế: GAP-04. · Không quyết: Operator vẫn xóa được rack.
- (a) Chặn xóa rack của lệnh chưa xong (không đổi quyền). (b) (a) + giảm tồn chỉ Admin, bắt buộc lý do: **thay CR-001**. (c) Giữ nguyên.
- **Đề xuất (a) ngay** (sửa lỗi bảo vệ dữ liệu); (b) chỉ khi bạn muốn thay CR-001.

**OQ-54 · Scope hàng đợi**
- Thực tế: chưa scope nào được khai báo; Buffer P1/P2, M5 dùng chung cho hai chiều.
- (a) Theo nhóm trạm **và chiều cất / cấp**: `AB-CAT` (trạm gửi rack đi) {PH2-01, 03}, `AB-CAP` (trạm gọi rack về) {02, 04, 06}, `XY-CAT` {07, 09}, `XY-CAP` {08, 10}, `XZ-CAT` {11, 12}: yêu cầu cất đang chờ chỗ không chặn yêu cầu cấp là thứ giải phóng buffer; các scope tranh buffer chung không có thứ tự giữa nhau. (b) Theo nhóm trạm và loại rack (đầy / rỗng): yêu cầu cất và cấp cùng loại nằm chung scope, có thể bế tắc. (c) Mỗi nhóm trạm một scope: thứ tự chặt nhất, nguy cơ bế tắc cao nhất. (d) Mỗi quy trình một scope: ít chờ nhất, FIFO yếu nhất.
- **Đề xuất (a)**, kèm OQ-42-04. Đây là suy luận từ mô tả tuyến, cần test trước khi vận hành. PH2-01…04 phải được đồng bộ danh mục trước.

**OQ-45 · Version đã ghim bị Disable**
- (a) Admin tắt → yêu cầu đang chờ Held; bị thay bởi Publish mới → vẫn chạy (D-10): cần ghi lý do Disable (migration nhỏ). (b) Giữ như hiện tại. (c) Mọi Disable đều Held: trái D-10 khi Publish.
- **Đề xuất (a).**

**OQ-42-02 · Quy trình danh mục đánh dấu "chưa công bố"**
- (a) Chặn Publish cho tới khi danh mục cho phép. (b) Cảnh báo + lý do. (c) Không chặn.
- **Đề xuất (a).** Version PH2-05 hiện tại do bạn Disable.

### Nhóm B – vận hành

**OQ-58 · Held ở đầu Scope** — (a) chặn yêu cầu sau (hiện tại, nhất quán D-13); (b) cho vượt: không tắc nhưng mất thứ tự. **Đề xuất (a).**

**OQ-42-04 · Deadlock / chờ quá lâu** — (a) chỉ hiển thị; (b) cảnh báo khi đầu scope chờ quá N phút, người vận hành quyết định; (c) tự vượt/hủy: trái D-13. **Đề xuất (b)**; cần giá trị N.

**OQ-57 · Giữ chỗ khi Held** — Hiện: yêu cầu Held chưa có chặng nên không giữ gì (hành vi hiện tại, chưa được duyệt). **Đề xuất giữ.**

**OQ-49 · Thời điểm tạo Order** — (a) giữ: Order tạo khi vào Queue (đã triển khai, khác đặc tả §2.5: sửa đặc tả); (b) tách thực thể Request: migration lớn. **Đề xuất (a).**

### Nhóm C – phụ thuộc bên ngoài (cần kỹ sư PLC / nhà cung cấp RCS; im lặng = DEFERRED)

| OQ | Câu hỏi cụ thể | Đề xuất tạm |
|---|---|---|
| OQ-41-02 | Yêu cầu tính theo cạnh lên hay mức? Ai reset bit và Request_ID, khi nào? Timeout? Sau mất kết nối, bit còn ON có phải yêu cầu mới? | Cạnh lên + Request_ID duy nhất; không ghi ack |
| OQ-55 / PQ-26 | Cửa sổ "một Handshake mỗi PLC" bắt đầu và kết thúc ở tín hiệu nào? | Chưa triển khai D-1 |
| OQ-42-05 | Tín hiệu nào chứng minh bàn giao xong, để mô phỏng và PLC thật dùng chung? (D-4: `MissionCompleted` không đủ) | Chỉ hiển thị |
| OQ-42-06 | RCS gửi callback nào cho task của Leg; template Leg và trigger Pick/Drop? | Xác nhận tay; chưa chạy Real cho Order |

OQ-39-01…07, 09, 10 (CR-039) và PQ còn lại của CR-029 không đổi, vẫn mở.

**Cách trả lời:** ghi mã và chữ, ví dụ `OQ-42-03 b; OQ-42-01 b; OQ-54 a; còn lại theo đề xuất`.

**Về thời hạn 60 giây:** công cụ không tự hết hạn được. Câu chưa được trả lời khi bạn yêu cầu triển khai sẽ ghi **DEFERRED**; im lặng không được coi là đồng ý.

---

## 5. Phương án kỹ thuật tổng thể

1. **Sửa lỗi bảo vệ trước** (không đổi quy tắc nghiệp vụ): khóa apply-catalog khi đang Published; chặn điều chỉnh tồn kho đụng rack của lệnh chưa xong; chống ghi sự kiện lặp.
2. **Viết test làm rõ** RK-3 và PLC-3 trước khi kết luận.
3. **Theo quyết định:** định tuyến PLC chung → cắt chuyển BP → scope và cảnh báo chờ lâu.
4. **Real** để sau cùng, khi có câu trả lời PLC/RCS.

## 6. Phạm vi file dự kiến

| Hạng mục | File chính | Migration |
|---|---|---|
| GAP-01 | `ProcessAdminService.cs`, `ProcessesPage.tsx` | Không (b) / Có (a) |
| GAP-04 | `InventoryService.cs` | Không |
| GAP-03 | `RouteEngine.cs` | Không |
| PM-2 | `ProcessAdminService.cs` | Không |
| OQ-42-03 | `PlcSignalSimulation.cs`, `PlcStationSignalService.cs`, `StationRequestProcessor.cs`, `PlcSimulationService.cs`, cấu hình | Có thể |
| OQ-45 (a) | `BusinessProcess.cs`, `RouteEngine.cs` | Có |
| OQ-42-04 | `RouteEngine.cs`, trang Điều phối đa chặng | Không |
| RK-1 | `RackDialog.tsx` | Không |

## 7. Rủi ro và kiểm thử

| Rủi ro | Giảm thiểu |
|---|---|
| Đụng luồng Legacy được bảo vệ | Test hồi quy Legacy; định tuyến theo trạm, mặc định giữ nguyên |
| Database local đang được sử dụng | Không sửa dữ liệu; migrate chỉ khi được yêu cầu, sao lưu trước |
| Khóa header làm quy trình đang chạy phải Disable | Thông báo rõ trên giao diện |
| Chưa có trình duyệt, PLC, RCS thật | Ghi NOT VERIFIED |

Kiểm thử: test mới cho từng GAP trước khi sửa; full backend regression; `tsc`, `eslint`, build, i18n và bộ smoke bị ảnh hưởng; Up/Down migration trên database test.

## 8. Làm ngay được và phải chờ

| Làm được ngay (sau khi bạn cho phép bắt đầu) | Phải chờ quyết định |
|---|---|
| GAP-01 phần apply-catalog (từ chối khi đang Published) | Khóa sửa header nói chung (OQ-42-01) |
| GAP-04 phương án (a) | Đổi quyền điều chỉnh tồn kho (OQ-42-07 b) |
| GAP-03, RK-1, test RK-3 | PM-2, OQ-45, OQ-54, OQ-58, OQ-42-04 |
| Đính chính tài liệu CR-041 | OQ-42-03, OQ-40-01; toàn bộ nhóm C |
