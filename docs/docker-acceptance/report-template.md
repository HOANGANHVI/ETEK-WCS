# BÁO CÁO NGHIỆM THU DOCKER – CR-007 (H1–H6)

> Mẫu báo cáo. `run-acceptance.ps1` điền tự động các trường `{{...}}`; có thể điền tay khi chạy thủ công.
> Trạng thái chỉ gồm: **ĐẠT** / **KHÔNG ĐẠT** / **CHƯA THỂ KIỂM TRA**. Không ghi ĐẠT nếu chưa có bằng chứng thực tế trong thư mục bằng chứng.

| Mục | Giá trị |
|---|---|
| Ngày thực hiện | {{DATE}} |
| Người thực hiện | {{OPERATOR}} |
| Máy / Hệ điều hành | {{HOSTNAME}} / {{OS}} |
| Docker | {{DOCKER_VERSION}} |
| Docker Compose | {{COMPOSE_VERSION}} |
| Git | branch `{{BRANCH}}`, HEAD `{{HEAD}}` |
| Thư mục bằng chứng | `{{EVIDENCE_DIR}}` |
| **Kết luận chung** | **{{OVERALL}}** |

## 1. Tổng hợp

{{SUMMARY_TABLE}}

## 2. Chi tiết từng bài

<!-- TEST-BLOCK-BEGIN -->
### {{ID}} – {{TITLE}}

| Mục | Nội dung |
|---|---|
| Mã kiểm thử | {{ID}} |
| Mục tiêu | {{OBJECTIVE}} |
| Điều kiện tiên quyết | {{PRECONDITIONS}} |
| **Trạng thái** | **{{STATUS}}** |

**Lệnh thực hiện (theo checklist gốc):**

{{COMMANDS}}

**Kết quả mong đợi:** {{EXPECTED}}

**Kết quả thực tế:** {{ACTUAL}}

**Các bước đã thực hiện (có thời gian):**

{{STEPS}}

**Bằng chứng:**

{{EVIDENCE}}

**Ghi chú và vấn đề phát sinh:**

{{NOTES}}

---

<!-- TEST-BLOCK-END -->

## 3. Vấn đề phát sinh, nguyên nhân gốc, phân loại

| # | Vấn đề | Nguyên nhân gốc | Phân loại (lỗi mã / cấu hình Docker / môi trường / giới hạn kiểm thử) | Đề xuất |
|---|---|---|---|---|
| 1 | | | | |

## 4. Rủi ro tồn đọng

- 

## 5. Khôi phục sau kiểm thử

- [ ] `status.ps1`: tất cả container khỏe; `postgres`, `backend`, `frontend` đang chạy
- [ ] Volume `etek_wcs_pgdata` và `etek_wcs_keys` còn nguyên
- [ ] `.env` thật không bị sửa (so sánh với bản đã sao lưu)
- [ ] Không còn thư mục tạm của bộ kiểm thử (`...\H3\tmp-config`)

## 6. Xác nhận

| Vai trò | Họ tên | Ngày | Chữ ký |
|---|---|---|---|
| Người thực hiện | | | |
| Người xem xét / nghiệm thu | | | |

> Báo cáo này không tự động tuyên bố CR-007 đã nghiệm thu: quyết định thuộc về người xem xét sau khi đọc bằng chứng.
