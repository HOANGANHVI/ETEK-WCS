# Personalization – báo cáo hoàn thiện sau nghiệm thu

Ngày: 2026-11-17 · Nền: commit `dddd628` (branch `develop`) · Không thay đổi nghiệp vụ WCS, backend hay migration.

## 1. Sidebar responsive

**Trước:** từ 1200px trở xuống sidebar bị ép thu gọn, nút bật/tắt bị khóa.
**Sau:**
- Gỡ hoàn toàn cơ chế ép thu gọn (`matchMedia`, `sidebarForcedCollapsed`, `sidebarIsCollapsed`, nút `disabled`, thông báo "màn hình hẹp" và khóa i18n `account.sidebarForced`). Trạng thái sidebar chỉ còn một nguồn: `sidebarCollapsed` trong tùy chọn người dùng.
- **Đổi kích thước cửa sổ không ghi gì lên Server**: đã kiểm bằng cách đếm `PUT /api/auth/preferences` qua chuỗi 1440→1024→760→1200→900… = 0 lần; giá trị lưu không đổi.
- Nguyên nhân thật của lỗi tràn ở 1024/760 khi mở rộng không nằm ở nội dung chính mà ở **header** (ô logo rộng bằng sidebar 224px). Cách xử lý (chỉ CSS, `layout.css`):

| Độ rộng | Sidebar mở rộng | Sidebar thu gọn |
|---|---|---|
| ≥ 1201px | 224px | 68px |
| 1001–1200px (gồm 1024px) | 224px; header chuyển sang dạng gọn (ẩn nhãn phụ "PLC/RCS", vai trò, thứ trong tuần; biểu tượng-only cho "Cấu hình người dùng") | 68px |
| ≤ 800px (gồm 760px) | **184px**, vẫn nằm trong luồng bố cục (không che nội dung, không overlay); header ẩn thêm khối tên người dùng (tên vẫn xem ở trang Cấu hình người dùng) | 68px |

- Nhãn menu dài (ví dụ "Task Management") bị cắt bằng dấu "…" ở 184px; tên đầy đủ có trong `title`/`aria-label`. Hành vi cắt nhãn đã có từ trước.
- Hạn chế cần biết: ở ≤ 1000px nhãn "PLC"/"RCS" trong header bị ẩn (quy tắc đã có từ trước); trạng thái vẫn có màu LED và `title` mô tả.

## 2. Dark Mode – các phần còn thiếu

Kiểm tra 4 tổ hợp Sáng/Tối × VI/EN ở 1440px, kèm quét tự động (tương phản chữ < 3:1; vùng nền sáng ngoài ý muốn ở Dark):

| Khu vực | Dark | Light |
|---|---|---|
| Đối soát (panel trong chi tiết nhiệm vụ, chế độ mặc định và biểu mẫu "đã xác minh") | 0 chữ thiếu tương phản, 0 vùng nền sáng | 0 chữ thiếu tương phản |
| Dock mô phỏng (đóng, mở, sau khi chạy tình huống và gây gián đoạn) và trang Nhiệm vụ có dock | 0 / 0 | 0 chữ thiếu tương phản (sau sửa) |

(Ở Light, tiêu chí "vùng nền sáng" không áp dụng vì bề mặt vốn sáng.)

Chỉ sửa 2 chỗ thực sự không theo theme:
1. **Dock mô phỏng – nhãn trạng thái** (`.dock-list .pill-*`): ở Light, chữ đỏ đậm trên nền navy của dock chỉ đạt tương phản 2,21:1 (nhãn "Thất bại/Failed" trong mục "Cần đối soát vị trí rack"). Dock luôn navy ở cả hai theme nên dùng tông sáng cố định.
2. **Khung `fieldset`** của biểu mẫu đối soát: dùng màu xám mặc định của trình duyệt, nay theo token `--line-strong`.

Ngoài phạm vi, chỉ ghi nhận: dock Demo viết cứng tiếng Việt, không đổi theo EN (xem `USER_PROFILE_MISSING_FIELDS.md` mục 4).

## 3. Trang Cấu hình người dùng – không tạo dữ liệu giả

- Không đổi mã hay bố cục trang. Email, Ảnh đại diện, Chức danh, Phòng ban vẫn chưa hiển thị; trang đã ghi chú rõ (`account.orgNote`, `account.avatarHint`).
- Báo cáo riêng cho CR sau này: [USER_PROFILE_MISSING_FIELDS.md](USER_PROFILE_MISSING_FIELDS.md).
- Theme và Sidebar Preferences không bị ảnh hưởng (kiểm bên dưới).

## 4. Kết quả kiểm thử

Trình duyệt: Edge headless (chỉ Edge), stack cô lập API 5099 + Vite 5180 (+ Vite 5181 bản Demo), DB tạm `wcs_ui`.

| Nhóm | Kết quả |
|---|---|
| Nghiệm thu giao diện (`accept.mjs`) | **30/30 đạt** |
| Bật/tắt ở 1440/1024/760: nút không bị khóa, đổi trạng thái, lưu lên Server, bật lại được | đạt |
| Mở rộng chủ động ở 1024 (800px nội dung) và 760 (576px nội dung): không che nội dung, không tràn ngang | đạt |
| Ma trận 3 độ rộng × 2 sidebar × 2 theme × 2 ngôn ngữ × 7 trang = **168 lượt** | 0 lỗi tràn ngang / header chồng / sidebar đè nội dung |
| Dark ở 1024 và 760 mở rộng, 12 trang × VI/EN | 0 chữ thiếu tương phản, 0 vùng nền sáng |
| Tooltip khi thu gọn ở 760px | hiện, nằm trong khung nhìn |
| Operator: tài khoản mặc định Light+Expanded, đổi được ở 1024/760, tải lại giữ nguyên, xóa localStorage vẫn khôi phục từ Server | đạt |
| Chuyển tài khoản Admin→Operator→Admin: Admin giữ Dark+Collapsed, tùy chọn Admin không bị đổi | đạt |
| Reconciliation + Demo dock × Sáng/Tối × VI/EN | 0 chữ thiếu tương phản (xem mục 2) |

Build/test frontend: ESLint sạch · `tsc -b` sạch · `npm run build` thành công · `i18n:check` OK · `i18n:test` 76 đạt · `dashboard:test` 18 đạt · `preferences:test` 10 đạt.
Backend: **không thay đổi** trong đợt này nên không chạy lại bộ 551 test (kết quả ở `dddd628` vẫn là kết quả hiện hành).

## 5. Ảnh chụp (`docs/archive/reviews/personalization-followup/`)

Sidebar responsive: `r1-1024-expanded-light-vi`, `r2-1024-expanded-dark-en`, `r3-1024-collapsed-light-en`, `r4-760-expanded-light-vi`, `r5-760-expanded-dark-en`, `r6-760-collapsed-dark-vi`, `r7-760-expanded-dark-vi-tasks`, `r8-760-account-expanded-light-en`, `r9-1440-expanded-light-vi`, `s-760-collapsed-tooltip-vi`.
Operator: `o1-operator-760-expanded-light-vi`, `o2-operator-760-collapsed-after-reload`.
Đối soát: `recon-{light,dark}-{vi,en}-b`.
Dock Demo: `demo-dark-vi-a`, `demo-dark-vi-b`, `demo-dark-en-b`, `demo-light-vi-b`, `demo-light-en-b`, `demo-dark-vi-tasks`.

## 6. Tệp thay đổi

`Layout.tsx`, `PreferencesProvider.tsx`, `PreferencesContext.ts`, `AccountPage.tsx`, `i18n/messages/account.ts` (bỏ khóa `sidebarForced`), `styles/layout.css`, `styles/base.css`; tài liệu: báo cáo này và `USER_PROFILE_MISSING_FIELDS.md`; ảnh chụp.
Không đụng: backend, migration, `TaskStateMachine`, PER_AGV, Scheduler, InventoryService, AGV/PLC/RCS, số liệu dashboard, menu/phân quyền, xác thực.

## 7. Rủi ro còn lại

- Chỉ kiểm tra trên Edge; chưa thử Chrome/Firefox/Safari hay thiết bị cảm ứng thật.
- Ở 760px mở rộng, header bỏ khối tên người dùng và nhãn menu dài bị cắt "…" (có tooltip).
- Dock Demo chưa được bản địa hóa EN (có từ trước).
- Không có unit test React cho Provider; hành vi được kiểm bằng kịch bản trình duyệt.
