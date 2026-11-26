# CR-046 – Catalog PLC theo IO Map 22/11, sửa giao diện IO, F1/F2, kiểm chứng Docker

| | |
|---|---|
| **Trạng thái** | `IMPLEMENTED – NOT DEPLOYED`. PLC thật, RCS thật: chưa kiểm chứng |
| **Nguồn phê duyệt** | Chủ dự án, 2026-11-26 (Master Task: deployment readiness, IO alignment, UI correction, Phase 2 closeout) |
| **Nền** | `main` `f503f0c`; nhánh `feature/cr-046-io-map-catalog-ui-deploy` |
| **Migration** | Không |
| **Version** | `2026.11.26.2` |

## 1. Catalog PLC

| | Trước | Sau |
|---|---|---|
| Nguồn | IO Map v2 (đã ngừng dùng) | `AGV_PLC Interface_List_22_11_2026.xlsx` (SHA-256 `405586c9…34da3`) |
| Trạm | …X1, X2, X3, **X6**; E1, E2, **G3, G7** | …X1, X2, X3, **X4**; E1, E2, **G1, G2** |
| Tín hiệu mỗi trạm | 23 | 6 (`PLC_MissionRequest`, `PLC_ReaddyTranfer`, `PLC_MissionCompleted`, `AGV_RequestTranfer`, `AGV_TranferCompleted`, `Mission_Feedback`) |
| Điểm IO | 368 | 96 |
| Địa chỉ | Mẫu chung `M90xx`… | Địa chỉ tham chiếu theo từng trạm như workbook (`D15n0–2`, `D16n0–1`). **Không** phải địa chỉ đã cấu hình |
| HeartBit, dự phòng | – | 24 dòng cấp PLC, chỉ hiển thị |

Sinh bằng `node scripts/generate-plc-io-catalog.mjs` (kiểm tra SHA-256, cấu trúc; `--check`).

**Database đang có dữ liệu:** seeder chỉ thêm. Dòng của catalog cũ và trạm X6, G3, G7 **được giữ nguyên** nhưng không đọc, không ghi, không tính; giao diện hiện riêng và chỉ cho xóa địa chỉ cũ. Không tự ghép trạm cũ với trạm mới, không chuyển địa chỉ, không chuyển liên kết Location. Trạm mới nhận vị trí thứ tự còn trống.

**Hệ quả cần biết khi nâng cấp:**

- Địa chỉ đã nhập cho 23 tín hiệu cũ hết tác dụng. Muốn dùng lại địa chỉ đó cho tín hiệu mới phải xóa ở dòng cũ.
- Cổng của quy trình chỉ "đã xác nhận" khi các điểm của IO Map mới được xác nhận.
- Trạm X6, G3, G7 đã liên kết Location (nếu có) không còn điểm IO.
- Handshake theo tín hiệu cũ và `Plc:Integration:StationFlows` không còn tín hiệu để đọc; handshake mới **chưa hiện thực** (hiển thị `Chưa hiện thực handshake`).
- Mô phỏng PLC theo yêu cầu: bảy tín hiệu logic cũ nay là **tín hiệu thử** trong bộ nhớ, ghi rõ không thuộc IO Map.
- Catalog v2 chỉ còn là dữ liệu thử trong `tests/WCS.Tests/Fixtures/` để kiểm thử cơ chế PLC.

## 2. An toàn ghi

Thêm vào CR-044: mọi tín hiệu của IO Map mới ở trạng thái **chưa xác nhận** → không duyệt ghi được (`WRITE_SIGNAL_NOT_CONFIRMED`) và không ghi ra PLC thật dù có địa chỉ, duyệt cũ hay khai báo allow-list. Chỉ gỡ bằng thay đổi được phê duyệt. Thanh ghi dành riêng, mặc định từ chối, audit fail-closed không đổi. Không phần nào tự ghi các tín hiệu này, nên giá trị xong Drop (`5`/`6`) không thể bị gửi đi.

Giới hạn: `AGV_RequestTranfer` và `AGV_TranferCompleted` dùng chung thanh ghi; hiện chỉ một trong hai nhận được địa chỉ cấu hình (cái còn lại báo trùng địa chỉ). Dùng chung thực sự thuộc Giai đoạn 3.

## 3. Lỗi giao diện IO

| Lỗi | Nguyên nhân gốc | Sửa |
|---|---|---|
| Tên trạm, tín hiệu, số lượng IO sai | Catalog backend sinh từ workbook cũ | §1 |
| Bộ lọc trạm hiện X6, G3, G7 | Danh sách PLC/trạm **viết cứng** trong `PlcIoMappingPage` | Lấy từ API |
| "16 trạm", "23 tín hiệu" cố định | Chuỗi dịch viết cứng | Theo dữ liệu |
| Không thấy địa chỉ theo IO Map | Chỉ hiện địa chỉ cấu hình; cột mẫu không dùng | Cột "Địa chỉ theo IO Map" tách khỏi địa chỉ thực tế; thanh ghi dùng chung được đánh dấu |
| Không phân biệt chiều tín hiệu ở trang cấu hình và giám sát | Không hiển thị | Hiện chiều cạnh tên |
| Không phân biệt đọc/ghi/chưa xác nhận | Chỉ có cột duyệt | `Read Enabled` / chưa nhập địa chỉ; `Not Confirmed`, `Write Protected`, `Write approved`, đầu vào PLC |
| HeartBit không xuất hiện | Điểm IO chỉ ở cấp trạm | Bảng tín hiệu cấp PLC, ghi "chưa hiện thực" |

Giữ nguyên (đã đúng): chế độ mô phỏng không hiển thị "đã kết nối"; giá trị cũ quá hạn là `Stale`; thời điểm cập nhật cuối.

## 4. F1 và F2

| | Nguyên nhân | Xử lý |
|---|---|---|
| **F1** | Engine Task không nhìn chặng đang chạy | Engine Task coi AGV bận khi có chặng `InProgress`/`Waiting`; hai engine khóa theo AGV trong giao dịch trước khi quyết định |
| **F2** | Lệnh giữ chỗ đích khi AGV của nó đang có chặng **chưa có đích** (có thể đang chở đúng rack sẽ cần chỗ đó) | **Chặng đầu** của lệnh không được tạo (không giữ chỗ) khi AGV cùng loại rack có chặng đang chờ/đang chạy mà chưa có đích; lệnh không giữ gì, giữ vị trí hàng đợi, lý do `AgvBusy`, đi tiếp khi chặng kia đã có đích hoặc kết thúc. Mọi trường hợp khác như cũ. Cờ `RouteEngine:DeferDestinationWhileAgvNeedsOne` (mặc định `true`) |

Không đổi FIFO (D-13), không đổi thứ tự ưu tiên; các test đã duyệt của hàng đợi, PER_AGV và Maintenance giữ nguyên và đạt. Phương án rộng hơn (hoãn mọi giữ chỗ khi AGV bận, R2) đã thử và loại vì đổi hành vi đã duyệt. R3 của phân tích OQ-54 (ưu tiên AGV đang giữ rack) **không** làm.

**Còn lại, chưa xử lý:**

- Chặng thứ hai trở đi vẫn giữ chỗ như cũ. Hoãn chúng sẽ tạo chờ vòng mới trong cùng scope (lệnh cũ chờ AGV đang chở rack của lệnh mới hơn, lệnh mới hơn chờ lệnh cũ). Quy trình một chặng không bị.
- Điều kiện xét theo loại rack (một AGV mỗi loại trong dispatch Legacy).
- Không có thứ tự ưu tiên giữa Task đang chờ và chặng đang chờ cùng AGV: có thể chờ lâu. Cảnh báo hàng đợi đứng (CR-042) vẫn áp dụng cho lệnh.
- RcsManaged: quy tắc F1/F2 theo AGV không áp dụng (RCS chọn xe).
- Chưa chứng minh loại bỏ mọi chờ vòng; OQ-54 (phạm vi hàng đợi) vẫn mở. Chống va chạm vật lý thuộc RCS, chưa xác nhận.

## 5. Docker

`tests/deployment/compose-smoke.sh` và `.github/workflows/docker-smoke.yml`: dựng image trên máy Linux sạch của GitHub Actions, chạy Compose với mật khẩu sinh ngẫu nhiên, kiểm tra qua Nginx (health, đăng nhập, IO Map, SignalR/WebSocket, khởi động lại giữ dữ liệu, mặc định an toàn). Không deploy, không phát hành image.

## 6. Rollback

Hoàn tác commit và deploy bản trước. Không đổi schema. Dòng IO Map mới đã thêm vào database sẽ ở lại; bản cũ coi chúng là điểm thường (vị trí trạm mới nằm sau trạm cũ). Cần sạch dữ liệu thì khôi phục bản sao lưu mà `deploy.ps1` tạo trước khi nâng cấp. F2 tắt riêng bằng cờ.

## 7. Kết quả kiểm thử

Ngày 2026-11-26. **Không chạy Full Backend Regression** (theo chỉ đạo).

| Kiểm tra | Kết quả |
|---|---|
| Build backend Release | Đạt |
| Test backend mục tiêu: catalog, PLC, handshake, route engine, task, hàng đợi, dispatch, fleet, log, chẩn đoán | 1064/1064 đạt (sau F1/F2) |
| Chạy lại phần PLC/monitoring/chẩn đoán/Cr046 sau lần sửa giao diện cuối | 80/80 đạt |
| `Cr046IoMapCatalogTests` (workbook → catalog → API, database mới và database đang có dữ liệu, chặn ghi) | Đạt |
| `Cr046AgvAndQueueTests` (F1: chờ, đồng thời; F2: chuỗi bế tắc đã tái hiện, tắt cờ) | Đạt; thay cho `Cr042OpenFindingsTests` |
| Một test thời gian (`PlcWorkerTests`, một PLC mất kết nối) | Lỗi 2 lần khi chạy trong nhóm lớn, đạt khi chạy lại (3 lần riêng và trong nhóm 80); không liên quan catalog |
| Frontend: `tsc`, lint, build, `i18n:test` 76, `plcmonitor:test` 37, `plcwrite:test` 20 | Đạt |
| `i18n:check` | 7 lỗi mẫu log RCS có từ trước |
| Giao diện trên Edge (bản publish, Production + mô phỏng, database tạm đã xóa) | Tổng quan, trạm, giám sát, cấu hình địa chỉ, lọc theo PLC, tiếng Anh: đạt; không log lỗi backend |
| **Docker** trên GitHub Actions ([run](https://github.com/HOANGANHVI/ETEK-WCS/actions/runs/45809588716), Ubuntu 24.04) | 3 image dựng được; Compose healthy; migrator exit 0; Nginx, SignalR/WebSocket, 96 IO/16 trạm, khởi động lại giữ dữ liệu, mặc định chỉ đọc/ghi tắt, allow-list sai: đạt; 0 log Error/Fatal |
| Script triển khai | Cú pháp 0 lỗi; test cấu hình ghi 16/16, logging 20/20 |

Lỗi phát hiện khi kiểm tra trên trình duyệt và đã sửa: chế độ mô phỏng hiện giá trị mặc định và "đã cấu hình" cho điểm **chưa có địa chỉ**.

Ảnh sau sửa: [trạm X4](../reports/cr-046-ui/station-X4-simulator.png) · [giám sát I/O](../reports/cr-046-ui/io-monitoring.png) · [cấu hình địa chỉ](../reports/cr-046-ui/io-address-configuration.png) · [lọc G1](../reports/cr-046-ui/io-address-configuration-G1.png).

**Chưa kiểm chứng:** Docker Desktop trên Windows và `deploy.ps1`/`start.ps1`/`status.ps1` trên máy đích; nâng cấp trên bản sao database vận hành; PLC thật; RCS/AGV thật.
