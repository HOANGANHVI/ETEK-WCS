# CR-071 – AMR mô phỏng chạy đều

| | |
|---|---|
| **Trạng thái** | `APPROVED, IMPLEMENTED – nhánh feature/cr-071-even-movement, CHƯA MERGE` |
| **Nguồn phê duyệt** | Chủ dự án, 2026-11-28 (báo lỗi: AMR mô phỏng chạy giật, nhanh chậm thất thường) |
| **Migration** | Không |

Nguyên nhân (đo được 0,61–1,36 m/s ở 1x): simulator bước mỗi 0,25 s, giao diện đọc mỗi 0,4 s nên mỗi lần đọc được một hoặc hai bước, và giao diện lấy thời điểm nhận làm nhịp.

Ngoài ra: khi WCS chưa nhả cổng trạm nguồn, lượt chạy chờ 2 s và AMR đứng giữa đường; nay chỉ việc nhả cổng được thử lại, AMR vẫn chạy.

Sửa: API `live` trả đồng hồ mô phỏng (`time`, và `at` của từng lượt); giao diện phát lại theo đồng hồ đó, trễ 0,8 s. Simulator tính theo thời gian thực đã trôi (tối đa 1 s mỗi bước). Nghiệp vụ không đổi.

Kiểm thử: `Cr050SimulatorTests`, `Cr069SimMapGraphTests`, `fleet-smoke`, đo trên trình duyệt với bản sao dữ liệu. Rollback: quay lại bản trước.
