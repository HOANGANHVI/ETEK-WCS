# CR-054 – Tên quy trình tham chiếu hiển thị theo ngôn ngữ đang chọn

| | |
|---|---|
| **Trạng thái** | `ĐÃ TRIỂN KHAI – nhánh fix/cr-053-dashboard-counts, CHƯA MERGE` |
| **Nguồn phê duyệt** | Chủ dự án, 2026-11-27 |
| **Migration** | Không; chỉ đổi giao diện |

## Nguyên nhân

Tên quy trình được lưu trong database bằng một ngôn ngữ (trên máy này BP-01…06 và PH2-01…06 bằng tiếng Anh) và giao diện hiển thị nguyên văn.

## Thay đổi

`i18n/processNames.ts` dịch tên của các quy trình tham chiếu ở danh sách chọn quy trình (tạo nhiệm vụ, vận hành RCS, điều phối đa chặng), trang Mô phỏng PLC và trang Quy trình. Chỉ dịch khi tên còn đúng là tên được giao kèm; quy trình do người vận hành tạo hoặc đổi tên hiển thị nguyên văn. Không ghi gì vào database; ô sửa tên vẫn hiện giá trị đã lưu.

## Giới hạn

Mô tả quy trình và một số lý do chưa sẵn sàng do backend trả về vẫn bằng một ngôn ngữ.

## Kiểm thử

`ops-smoke` (ca CR-054), kiểm tra trình duyệt trên bản sao dữ liệu.
