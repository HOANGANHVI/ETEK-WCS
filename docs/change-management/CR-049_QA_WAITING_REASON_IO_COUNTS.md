# CR-049 – QA: lý do chờ khi cổng trạm bị giữ; nhãn số liệu IO Map

| | |
|---|---|
| **Trạng thái** | `IMPLEMENTED – ĐÃ MERGE vào main (0db229a), CHƯA DEPLOY` |
| **Nguồn phê duyệt** | Chỉ thị QA của chủ dự án, 2026-11-27 ("sửa code nếu phát hiện lỗi và phạm vi đã rõ") |
| **Nền** | `main` `9f57903` |
| **Migration** | Không |

## Thay đổi

| Phát hiện | Thay đổi |
|---|---|
| QA-001b | Lý do chờ nguồn của lệnh nêu đúng nguyên nhân (Location, cổng bị giữ với trạng thái giao dịch, PLC ngắt, không có rack). Cổng bị giữ mang tiền tố `PORT_HELD:`; danh sách và chi tiết lệnh hiển thị "Cổng trạm đang bị giữ…". Sự kiện `ORDER_WAITING` được ghi lại khi nguyên nhân đổi |
| QA-003 | Trang địa chỉ IO ghi rõ 96 tín hiệu trạm và các loại dòng của IO Map |

Không đổi: điều kiện chọn nguồn/đích, loại chờ, trạng thái, giữ chỗ, quy tắc nhả cổng, quyền ghi PLC, IO Map.

## Kiểm thử

`QaWaitingReasonTests`; kết quả đầy đủ trong [báo cáo QA](../reports/qa-2026-11-27/QA_REPORT.md).

## Rollback

Hoàn tác thay đổi; không đổi schema hay dữ liệu.
