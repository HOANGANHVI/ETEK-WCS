# CR-067 – Nạp đồng bộ bản đồ XML và Mapdata Excel của RCS

| | |
|---|---|
| **Trạng thái** | `APPROVED, IMPLEMENTED – đã MERGE vào main (920ee28)` |
| **Nguồn phê duyệt** | Chủ dự án, 2026-11-28 ("Import & integrate RCS map XML + Mapdata Excel") |
| **Migration** | **Không.** Dữ liệu Excel lưu cùng cấu trúc bản đồ trong `rcs_maps.GeometryJson` |
| **Thư viện mới** | `ExcelDataReader` 3.7.0 (MIT), chỉ đọc `.xls` / `.xlsx` |

## Quy tắc ghép

`Map Data ID` = X (mm) + `MapQRCode` + Y (mm). Đối chiếu trên bộ file ngày 2026-11-28: 73/73 điểm XML khớp dòng mã CC; 47 dòng mã TT thuộc bản đồ khác. Không dùng thứ tự dòng hay tên gần giống. Mỗi dòng nhận một trạng thái: Matched, OtherMap, NoPoint, Duplicate, Ambiguous, Invalid; không bỏ dòng nào. Không có dòng nào khớp thì từ chối cả bộ (`RCS_MAPDATA_MISMATCH`). Excel không có Map ID; tương thích được kiểm tra bằng mã QR và tọa độ.

## Sử dụng

- Cấu hình: chọn cả hai file → **Đối chiếu** (`POST /api/config/rcs-map/preview`, không lưu) → lưu.
- Monitor AMR: Call code, màu theo Data type; bấm điểm xem dữ liệu Excel (chỉ đọc) tách riêng cấu hình WCS.
- Liên kết WCS vẫn do Admin lưu (CR-066). Chỉ **gợi ý** khi có vị trí WCS trùng đúng Call code. `Linked Location` của Excel là Map Data ID của điểm đi kèm, chỉ để tham chiếu.
- Tên / liên kết CR-066 được giữ khi cùng Map ID và điểm ở đúng tọa độ.

## Giới hạn

Không lưu bản nhị phân của file Excel (lưu toàn bộ ô của các cột nhận biết; cột khác không lưu). API vẫn nhận riêng XML (tương thích CR-065); giao diện yêu cầu cả hai. Dữ liệu chỉ để hiển thị: không đổi vị trí, tồn kho, điều phối, PLC, RCS.

## Kiểm thử và rollback

`RcsMapDataMatcherTests`, `RcsMapParserTests`, `Cr066MapPointTests`, build, trình duyệt trên bản sao dữ liệu với hai file thật. Không chạy toàn bộ test. Rollback: quay lại bản trước (bản đồ đã nạp vẫn đọc được).
