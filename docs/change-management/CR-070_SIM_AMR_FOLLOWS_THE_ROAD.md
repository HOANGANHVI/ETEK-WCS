# CR-070 – AMR mô phỏng bám đường ở góc cua

| | |
|---|---|
| **Trạng thái** | `APPROVED, IMPLEMENTED – nhánh feature/cr-070-amr-follows-road, CHƯA COMMIT` |
| **Nguồn phê duyệt** | Chủ dự án, 2026-11-28 (báo lỗi: AMR qua góc cua không bám đường khi phóng to) |
| **Migration** | Không |

Nguyên nhân: giữa hai lần đọc (0,4 s) giao diện trượt thẳng từ vị trí cũ đến vị trí mới nên cắt góc; hướng đầu xe đổi theo bậc.

Sửa (chỉ frontend): AMR được vẽ theo từng khung hình dọc các vị trí của tuyến đã đi qua; đầu xe xoay dần theo đoạn đường; phần tuyến đã đi kết thúc ở chỗ AMR được vẽ. Hình vẽ chậm hơn simulator khoảng một lần đọc; Pause dừng sau khoảng đó. Dữ liệu, API và simulator không đổi.

Kiểm thử: `fleet-smoke`, build, trình duyệt trên bản sao dữ liệu. Rollback: quay lại bản trước.
