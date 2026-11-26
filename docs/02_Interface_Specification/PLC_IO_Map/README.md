# PLC IO Map – nguồn duy nhất

| | |
|---|---|
| **IO Map chuẩn duy nhất** | [`AGV_PLC Interface_List_22_11_2026.xlsx`](AGV_PLC%20Interface_List_22_11_2026.xlsx) (sheet `IO Matrix`, `Trang_tính1`) |
| **SHA-256** | `405586c9e4d2893f1d3a33922259ecfe1c400ca877710877b3b482e049b34da3` |
| **Quyết định** | Chủ dự án, 2026-11-26 (CR-045) |
| **Hợp đồng IO** | **Chưa** được kỹ sư PLC xác nhận. Không `APPROVED`, không `READY FOR IMPLEMENTATION` |
| **Trong hệ thống** | **Chưa hiện thực.** Catalog đang chạy vẫn sinh từ IO Map v2 cũ (23 tín hiệu) và không được đổi chỉ vì đổi tài liệu nguồn |

Không sửa workbook tại chỗ. Mọi thay đổi tín hiệu theo Change Request và cập nhật SHA-256 ở đây.

## Trạng thái từng tín hiệu

Ba trạng thái được giữ riêng: **Đã thống nhất** (chủ dự án quyết định) · **Chưa xác nhận** (chờ kỹ sư PLC) · **Kiểm thử thực tế** (chưa tín hiệu nào được kiểm thử với PLC thật).

Địa chỉ giống nhau trên 4 PLC; `n` = 0…3 theo thứ tự trạm.

| Tín hiệu | Chiều, địa chỉ, giá trị theo workbook | Đã thống nhất | Chưa xác nhận (mã câu hỏi) | Kiểm thử thực tế |
|---|---|---|---|---|
| `PLC_MissionRequest` | PLC→SW, `D15n0`: 0, 1 Pick, 2 Drop, 10 lỗi | Giữ trạng thái, không mặc định là xung (PD-2) | Điều kiện OFF, chống trùng (03, 04) | Chờ |
| `PLC_ReaddyTranfer` | PLC→SW, `D15n1`: 1 Pick, 2 Drop, 10 lỗi | Là phản hồi của PLC cho yêu cầu (PD-1) | Thời hạn, lỗi, reset (03, 07) | Chờ |
| `PLC_MissionCompleted` | PLC→SW, `D15n2`: 1, 2 | `1` Failure, `2` Success; chỉ tính khi đã xác minh đúng giao dịch (OW-4) | Xử lý thất bại, reset, định danh giao dịch (04, 09) | Chờ |
| `AGV_RequestTranfer` | SW→PLC, `D16n0` (chung thanh ghi): 1 Pick, 4 Drop | Là **yêu cầu**, không điều khiển trực tiếp cửa/kẹp (PD-1); chung một thanh ghi INT (PD-3) | Thời điểm ghi/huỷ, interlock (05, 16, 21) | Chờ |
| `AGV_TranferCompleted` | SW→PLC, `D16n0`: 3 Pick, **6** Drop | Chung thanh ghi (PD-3); xong Pick = 3 | **Giá trị xong Drop:** workbook ghi `6`, quyết định PD-3 trước đó ghi `5` – chờ chủ dự án và kỹ sư PLC (01, 02) | Chờ |
| `Mission_Feedback` | SW→PLC, `D16n1`: 0, 1–3, 10 | Không coi 1–3 là định danh cố định của AGV khi chưa xác nhận (OW-3) | Ý nghĩa, thời điểm, mã xe (06) | Chờ |
| `HeartBit` | SW→PLC, `M9200`: on/off chu kỳ 1 s, timeout 10 s | Một tín hiệu cho mỗi PLC (workbook ghi "dùng chung cho PLC n") | Cách tính chu kỳ, hành vi khi quá hạn (11) | Chờ |
| `Spair` | `M9201–M9205` | Dự phòng, không dùng | – | – |

| Trạm theo workbook | Ghi chú |
|---|---|
| PLC `192.168.10.101`: A1–A4 · `.111`: B1, B2, D1, D2 | A3, A4, D1, D2 chỉ giám sát (OW-7) |
| `.121`: X1, X2, X3, **X4** · `.131`: E1, E2, **G1, G2** | Hệ thống đang cấu hình X6, G3, G7; chưa đổi (14) |

Mã câu hỏi `PLC-OQ-nn`: [bộ câu hỏi](../PLC_20_11_2026/PLC_ENGINEER_QUESTIONS_2026-11-26.md). Phân tích: [báo cáo Phase 2](../PLC_20_11_2026/PLC_PHASE2_REVIEW_2026-11-26.md).

## Workbook đã ngừng sử dụng

Đã gỡ khỏi thư mục này; chỉ còn trong lịch sử Git. **Không khôi phục** vào thư mục chuẩn.

| Workbook | SHA-256 | Còn ở commit | Vì sao ngừng |
|---|---|---|---|
| `AGV_PLC Interface_List_20_11_2026.xlsx` (bản 2) | `a291d28f…babb3` | `a8c8787` | Thay bằng bản 22/11 (khác: xong Drop `4`; không ghi chú heartbeat) |
| `AGV_PLC Interface_List_20_11_2026.xlsx` (bản 1) | `3c5da74b…aedddf` | `d4c5030` | Bản phân tích của CR-029 |
| `AGV_PLC_IO_Interface_List_10_11_2026.xlsx` | `936271db…bca53` | `cce0d1e` | Nền của CR-010; thay từ 20/11 (đóng OW-6) |
| `ETEK_WCS_IO_Map_4PLC_16Stations_v2_Separate_IO_Sets.xlsx` | `89ff65b5…518d` | `34c45b2` | Mẫu 23 tín hiệu; nguồn của `plc-io-catalog.json` (OW-5 thay thế: không khôi phục) |

Tài liệu lịch sử (Change Request, báo cáo, nhật ký) vẫn nhắc tên các workbook này. Đó là ghi chép tại thời điểm viết, không phải nguồn hiện hành.
