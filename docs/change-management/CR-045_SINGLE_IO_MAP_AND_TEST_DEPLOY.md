# CR-045 – IO Map duy nhất, đóng hồ sơ PLC Phase 2, bản triển khai máy kiểm thử

| | |
|---|---|
| **Trạng thái** | `IMPLEMENTED – NOT DEPLOYED`. Chưa kiểm chứng trên Docker thật và PLC thật |
| **Nguồn phê duyệt** | Chủ dự án, 2026-11-26 |
| **Nền** | `main` `fa0caae`, nhánh `feature/plc-io-map-22-11-deploy-ready` |
| **Migration** | Không (bản triển khai vẫn mang các migration có từ trước, gồm CR-041 và CR-042) |
| **Version** | `2026.11.26.1` |

## 1. IO Map

- Chỉ dùng `AGV_PLC Interface_List_22_11_2026.xlsx` (SHA-256 `405586c9e4d2893f1d3a33922259ecfe1c400ca877710877b3b482e049b34da3`). Không sửa nội dung.
- Gỡ khỏi repository bản 20/11 và 10/11 (v2 đã bị gỡ từ trước, không khôi phục). Lịch sử và SHA: [README](../02_Interface_Specification/PLC_IO_Map/README.md). Đóng OW-5, OW-6.
- **Không đổi** catalog IO, database, logic giao tiếp. Script sinh catalog cũ nay dừng với thông báo thay vì lỗi đọc file.
- Tài liệu đang dùng được gắn dòng "IO Map hiện hành"; hồ sơ lịch sử giữ nguyên.
- Hợp đồng IO **chưa** được kỹ sư PLC xác nhận.

## 2. Triển khai

| Thay đổi | Chi tiết |
|---|---|
| `Plc:Write:AllowListText` | Dạng một chuỗi của allow-list CR-044; một mục sai → **không khai báo gì** (kể cả danh sách dạng cấu trúc) và có cảnh báo `WRITE_ALLOWLIST_INVALID` |
| `docker-compose.yml` | Truyền `PLC_WRITE_ALLOWLIST` (mặc định rỗng) |
| `.env.example`, `plc-write-config.ps1`, `deploy.ps1` | Thêm biến; script báo cáo hiển thị danh sách; sửa dòng tóm tắt từng ghi sai chế độ PLC mặc định |
| `deployment/README.md` | Mục "Máy kiểm thử PLC" (mô phỏng → chỉ đọc) |

Mặc định: không mở kết nối PLC thật, không bật ghi, danh sách rỗng, thanh ghi dành riêng bị chặn.

## 3. Rollback

Hoàn tác commit. Không có thay đổi schema. Triển khai cũ không đọc biến mới (tức không khai báo điểm nào).

## 4. Kết quả kiểm thử

<!-- RESULTS -->
