# Cá nhân hóa giao diện: Sidebar thu gọn, Light/Dark, Cấu hình người dùng

**Ngày:** 2026-11-17 · **Branch:** `develop` · nền `2104404`. Có thay đổi backend/DB (chỉ phần tùy chọn người dùng); không đụng TaskStateMachine, Scheduler, InventoryService, AGV, PLC, RCS, số liệu dashboard, menu và phân quyền.

## 1. Kiến trúc Sidebar
- `PreferencesProvider` (`src/preferences/`) giữ `sidebarCollapsed` của tài khoản; `Layout` gắn `data-sidebar="expanded|collapsed"` lên `.app`.
- Chỉ **một** biến CSS quyết định bề rộng: `--sidebar-w` (224px → 68px khi collapsed). Lưới `.app` đổi cột có transition 200ms; header (`.brand`) dùng cùng biến. Không còn margin âm ở `.brand`, không có lớp CSS chồng nhau: khối `@media (max-width:1200px)` cũ (thu gọn bằng CSS) được thay bằng quy tắc theo thuộc tính `data-sidebar`.
- Màn hình ≤ 1200px: Provider tự ép thu gọn (`sidebarForcedCollapsed`, theo `matchMedia`), nút bật/tắt bị vô hiệu và có ghi chú trong User Configuration; lựa chọn của người dùng vẫn được lưu. Hành vi này giữ nguyên ý của quy tắc responsive cũ, không thêm drawer.
- Nút đổi ở đầu sidebar, luôn hiển thị, icon `panel-left-close` / `panel-left-open`, `aria-label` + `aria-expanded`, thao tác được bằng bàn phím (Enter/Space), không đổi route.
- Tooltip menu khi thu gọn: một phần tử `position: fixed` tính từ `getBoundingClientRect` (hiện khi hover và focus), nên vùng cuộn của sidebar không cắt được. Khi mở rộng dùng `title` gốc. Tooltip theo ngôn ngữ đang chọn.

## 2. Kiến trúc Theme
- `styles/tokens.css`: Light là mặc định; `:root[data-theme='dark']` chỉ đổi **giá trị** token (`--bg`, `--surface`, `--surface-2`, `--line*`, `--text*`, `--ok/--warn/--bad/--info/--muted` và nền/viền tương ứng, `--navy-50/100/500/600`, `--shadow`, `color-scheme: dark` cho điều khiển gốc).
- Tên ngữ nghĩa theo yêu cầu: `--app-bg`, `--surface`, `--surface-secondary`, `--text-primary`, `--text-secondary`, `--border-color`, `--accent`, `--success`, `--warning`, `--danger` (là alias của token hiện có nên đi theo theme).
- Token bổ sung cho màu từng cố định: `--heading`, `--accent-text(-strong)`, `--link`, `--amber`, `--muted-solid`, `--stripe-*`, `--on-status`, `--scrim`, `--field-bg`. Các giá trị cố định trong CSS (`#e0a21a`, `#9aa6b6`, `#fff` của ô trống, `#7a4a00`, nền sọc rack giữ chỗ, màu chữ tiêu đề `navy-800`…) đã được chuyển sang token; không dùng override rải rác theo trang.
- Header, sidebar, dock mô phỏng giữ nền navy đậm ở cả hai theme (đã là giao diện tối); viền phải của sidebar tách khỏi nền Dark.
- `PreferencesProvider` đặt `document.documentElement.dataset.theme`.

## 3. Thành phần/trang hỗ trợ Dark Mode (đã kiểm tra bằng trình duyệt)
Quét tự động trong Edge ở Dark (độ tương phản chữ < 3:1, nền sáng diện tích lớn) trên **12 trang**: Dashboard (KPI, biểu đồ cột/donut/đường, tooltip, trục, lưới, chú giải), Giám sát AGV, Quản lý nhiệm vụ (+ chi tiết, stepper, lịch sử), Vị trí & tồn kho, Giám sát PLC, Kết nối PLC, Cấu hình IO, Cấu hình hệ thống, Quản lý tài khoản (+ hộp thoại Tạo tài khoản), Nhật ký thao tác, Nhật ký hệ thống, User Configuration. Kết quả: 0 chữ thiếu tương phản, 0 "đảo trắng". Bảng, input, select, textarea, badge, alert, trạng thái trống/lỗi/loading, modal đều dùng token. Quét Light cũng đạt (Dashboard, Tasks, Inventory, Config, Logs).
Lỗi bắt được và đã sửa trong lúc kiểm tra: nút ngôn ngữ đang chọn (`EN`) bị chữ sáng trên nền sáng ở Dark.
Chưa kiểm tra Dark ở màn hình Reconciliation / dialog đối soát và dock mô phỏng (Demo Mode) bằng quét tự động; các thành phần này chỉ dùng cùng token.

## 4. Thiết kế User Configuration
Route `/account` (mọi vai trò), mở từ liên kết "Cấu hình người dùng" ở header (thay "Đổi mật khẩu"; trang đổi mật khẩu `/account/password` giữ nguyên). Admin còn có nút từ mục C sang Quản lý tài khoản. Bốn mục:
- **A Thông tin cá nhân:** ảnh đại diện (chữ cái đầu của họ tên), họ tên hiển thị (chỉ đọc; Admin sửa ở Quản lý tài khoản).
- **B Tài khoản và bảo mật:** tên đăng nhập, nút Đổi mật khẩu (cơ chế hiện có).
- **C Vai trò và tổ chức:** vai trò (chỉ đọc). **Email, chức danh, phòng ban và ảnh đại diện chưa tồn tại trong dữ liệu tài khoản** (`AppUser` chỉ có username, displayName, role…); theo yêu cầu "không tạo trùng/không bịa" nên không thêm cột, giao diện nêu rõ điều này. Nếu cần các trường đó thì phải có Change Request riêng.
- **D Tùy chọn giao diện cá nhân:** thẻ chọn Theme (Sáng/Tối, icon + mô tả) và Thanh bên (Mở rộng/Thu gọn, icon + mô tả), áp dụng ngay không tải lại, tự lưu, hiển thị "Đang lưu… / Đã lưu / Không lưu được".
Dữ liệu do Admin quản lý (vai trò, họ tên) tách khỏi tùy chọn do người dùng sở hữu (theme, sidebar). Không có nút trùng trên header.

## 5. Lưu trữ tùy chọn
- Server là nguồn đúng: cột theo tài khoản; danh tính lấy từ phiên đăng nhập, không nhận `userId` từ client.
- Trình duyệt chỉ giữ **cache theo tài khoản** (`wcs.prefs.v1.<username>`) để hiện đúng giao diện ngay sau khi tải lại; không có khóa chung giữa các tài khoản. Cache hỏng hoặc giá trị lạ → dùng mặc định.
- Luồng: đăng nhập → hiển thị cache của tài khoản đó (hoặc Light + Expanded) → gọi API → áp giá trị server. Đổi tùy chọn → áp ngay → `PUT`; lỗi thì **quay về giá trị server đã xác nhận** và báo lỗi, UI không lệch với dữ liệu đã lưu. Đổi tài khoản/đăng xuất không để lại giao diện của tài khoản trước.

## 6. Thay đổi API và DB
- `GET /api/auth/preferences` → `{theme, sidebarCollapsed}`; `PUT /api/auth/preferences` (body `{theme, sidebarCollapsed}`), cả hai `RequireAuthorization`. Theme chỉ nhận đúng `light|dark` (phân biệt hoa thường), sai → 400 `PREFERENCE_INVALID` (đã có bản dịch VI/EN). Không có route theo user id.
- Lưu tùy chọn không đổi `UpdatedAt`, `SecurityStamp`, không đăng xuất phiên và không ghi audit (không phải hành động quản trị/bảo mật).
- Migration `20261117093555_AddUserPreferences`: thêm `Theme varchar(10) NOT NULL DEFAULT 'light'` và `SidebarCollapsed boolean NOT NULL DEFAULT false` vào `app_users`; `Down` xóa hai cột. Cộng thêm, không xóa/đổi tên dữ liệu.
- Kiểm thử migration trên DB tạm `wcs_mig`: trước migration 2 tài khoản; sau `Up` cả hai còn và mặc định Light + Expanded; `Down` bỏ cột, tài khoản còn nguyên; `Up` lại ổn. Đã **sao lưu `wcs_db`** (`~/wcs-backups/wcs_db_before_AddUserPreferences_*.dump`, kiểm tra đọc được bằng `pg_restore -l`) rồi áp migration lên `wcs_db`: 2 tài khoản, cả hai đúng mặc định.

## 7. Kiểm thử quyền và độc lập (Admin / Operator)
Backend (`UserPreferencesTests`, 9 ca): mặc định Light + Expanded; mỗi tài khoản giữ riêng giá trị qua các lần đăng nhập; theme sai (`blue`, rỗng, `DARK`) → 400 và không đổi; chưa đăng nhập → 401; `userId`/`username` giả trong body bị bỏ qua và không có route theo id; Admin sửa hồ sơ + đặt lại mật khẩu của Operator không động tới tùy chọn; lưu tùy chọn không đổi SecurityStamp/phiên.
Trình duyệt (kịch bản trên cùng trình duyệt): Admin Dark + Collapsed → đăng xuất → Operator đăng nhập bắt đầu Light + Expanded (không thừa hưởng giao diện Admin) → Operator đặt Light + Expanded → Admin quay lại thì Dark + Collapsed được khôi phục từ server; xóa `localStorage` (giả lập máy khác) vẫn khôi phục từ server; gọi `PUT /api/auth/preferences/<id>` → không phải 200; Admin sửa hồ sơ Operator → tùy chọn Operator không đổi. Operator thấy 5 mục menu, không có mục Admin, vào được User Configuration, không có nút sang Quản lý tài khoản. Giả lập lỗi lưu (chặn `PUT`): giao diện quay về Dark đã lưu và hiện thông báo lỗi.

## 8. VI/EN và responsive
- Chuỗi mới ở domain `account` (+ `nav.userConfig`, lỗi `PREFERENCE_INVALID`), có đủ VI/EN; `i18n:check` đạt (1056 khóa, backend code coverage). Tooltip và nhãn nút đổi sidebar theo ngôn ngữ (đã kiểm tra tooltip "Giám sát AGV", nhãn "Mở rộng thanh bên").
- Ma trận 2 theme × 2 ngôn ngữ × 2 chế độ sidebar × 3 độ rộng (1440, 1024, 760) × 2 trang (Dashboard, User Configuration): không tràn ngang, nút bật/tắt nằm trong khung, các nút header không chồng nhau. Ở ≤ 1000px liên kết "Cấu hình người dùng" ở header chỉ còn icon (có `title`/`aria-label`) để không đè nút Đăng xuất/đồng hồ.
- Dark Mode, tooltip không bị cắt: tooltip nằm ngoài sidebar và trong viewport (đã đo).

## 9. Ảnh chụp 4 trạng thái (Edge headless, dữ liệu thử trong DB tạm đã xóa)
| Trạng thái | Ảnh |
|---|---|
| Expanded + Light | `docs/reviews/personalization/state1-expanded-light.png` |
| Collapsed + Light | `state2-collapsed-light.png` (và `state2-collapsed-light-tooltip.png` có tooltip) |
| Expanded + Dark | `state3-expanded-dark.png` |
| Collapsed + Dark | `state4-collapsed-dark.png` |

Ảnh bổ sung: `dark-tasks.png`, `dark-inventory.png`, `dark-plc.png`, `dark-logs.png`, `dark-dialog.png`, `vi-collapsed-tooltip.png`, `responsive-1024-light-en.png`, `responsive-760-dark-vi.png`.

## 10. Ảnh User Configuration
`user-configuration-dark.png` (Admin, Dark + Collapsed) và `user-configuration-operator-light.png` (Operator, Light + Expanded).

## 11. Kết quả kiểm thử và build
- Backend `dotnet test`: **551 đạt**, 0 lỗi (542 cũ + 9 mới).
- `npm run i18n:check` OK; `i18n:test` 76 đạt; `dashboard:test` 18 đạt; `preferences:test` (mới) 10 đạt; ESLint sạch; `tsc -b` sạch; `npm run build` thành công (chỉ cảnh báo kích thước chunk có từ trước).
- Kiểm tra trình duyệt bằng Edge headless (CDP) trên stack riêng (API 5099 + Vite 5180 + DB tạm): **118/118 đạt**. Chỉ kiểm tra bằng Edge; các trình duyệt khác chưa thử.

## 12. Git
Một commit duy nhất trên `develop`, xem hash ở báo cáo cuối; `main` (`d5d692f`) không đụng; không merge/push/amend; 7 mục untracked cũ không bị stage.

## Tồn đọng / lưu ý
- Email, chức danh, phòng ban, ảnh đại diện chưa có trong dữ liệu tài khoản: cần CR nếu muốn bổ sung.
- Màn hình ≤ 1200px luôn thu gọn sidebar (hành vi cũ được giữ), nên người dùng không mở rộng được trên màn hình hẹp.
- Chưa có kiểm thử tự động (unit) cho Provider React; phần này được kiểm bằng kịch bản trình duyệt ở mục 7.
