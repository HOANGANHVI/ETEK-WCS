# User Configuration hoàn chỉnh: hồ sơ, bảo mật tài khoản, tùy chọn cá nhân

Ngày: 2026-11-17 · Branch `develop` · nền `cf3538c` (trên `dddd628`, không amend).
Phạm vi: chỉ phần tài khoản/giao diện. Không đụng `TaskStateMachine`, FlowOptions, `InventoryService`, Scheduler, RCS Client, PLC, PER_AGV, số liệu dashboard, đăng nhập/JWT/cookie và phân quyền hiện có.

## 1. Chức năng đã hoàn thành

| Mục | Kết quả |
|---|---|
| Họ và tên | Người dùng tự sửa (1–100 ký tự). Header cập nhật ngay vì tên nằm trong cookie phiên, phiên được cấp lại nhưng **không đổi security stamp** (các phiên khác không bị đăng xuất). |
| Email | Người dùng tự sửa, tùy chọn, kiểm tra định dạng ở cả giao diện và Backend, tối đa 254 ký tự. Để trống = "chưa cập nhật". Không gửi thư, không xác minh, không bắt buộc duy nhất. |
| Ảnh đại diện | Tải lên / đổi / xóa. Trình duyệt tự thu nhỏ ảnh lớn (≤ 512 px) trước khi gửi; Backend kiểm tra lại toàn bộ. Chưa có ảnh thì hiện chữ cái đầu. |
| Chức danh, Phòng ban | Admin quản lý (trong hộp thoại Sửa ở Quản lý tài khoản). Người dùng chỉ xem. Chưa có dữ liệu hiển thị "Chưa cập nhật", không gán mặc định. |
| Tên đăng nhập, Vai trò | Chỉ hiển thị. Chưa có cơ chế quản trị đổi tên đăng nhập nên không cho đổi. Vai trò do Admin cấp (cơ chế sẵn có). |
| Đổi mật khẩu | Form ngay trong trang (Mật khẩu hiện tại / Mới / Nhập lại). **Tái sử dụng** API `POST /api/auth/change-password` có sẵn, không tạo API trùng. Chính sách mật khẩu hiện hành (≥ 10 ký tự, có chữ và số, không chứa tên đăng nhập). Đường dẫn cũ `/account/password` chuyển về `/account`; luồng đổi mật khẩu bắt buộc lần đầu không đổi. |
| Tùy chọn cá nhân | Giữ Theme và Sidebar như trước (lưu theo tài khoản ở Backend, cache cục bộ theo từng tài khoản). |
| Sidebar responsive | Bật/tắt ở mọi độ rộng; không bao giờ tự ghi đè giá trị đã lưu (làm ở `cf3538c`, được kiểm tra lại trong đợt này). |
| Quản lý tài khoản (Admin) | Thêm cột Chức danh/Phòng ban, hiện email (chỉ xem), sửa chức danh/phòng ban. |

## 2. Dữ liệu mới và thay đổi Database

Migration **`20261117121953_AddUserProfile`** (cộng thêm, có Up/Down):
- `app_users`: thêm `Email varchar(254) NULL`, `Position varchar(100) NULL`, `Department varchar(100) NULL`. Không giá trị mặc định.
- Bảng mới `user_avatars(UserId PK + FK → app_users ON DELETE CASCADE, ContentType varchar(30), Data bytea, UpdatedAt)`. Ảnh nằm trong DB nên **không có đường dẫn tệp** nào để client chỉ định, được sao lưu cùng DB và bị xóa cùng tài khoản; tải tài khoản không bao giờ kéo theo bytes ảnh.
- `Down` xóa 3 cột và bảng ảnh (dữ liệu hồ sơ/ảnh sẽ mất, tài khoản và preferences giữ nguyên).

Áp dụng:
- DB tạm: Up → Down → Up đạt 12/12 kiểm tra (2 tài khoản có Theme/Sidebar riêng được giữ nguyên ở mọi bước, 3 cột mới là NULL, ảnh mồ côi bị khóa ngoại từ chối).
- `wcs_db`: đã **backup** `C:\Users\Admin\wcs-backups\wcs_db_before_AddUserProfile_20261117-192858.dump` (81.671 byte, `pg_restore -l` đọc được, 121 mục) rồi mới áp dụng. Sau áp dụng: 2 tài khoản nguyên vẹn, preferences giữ nguyên (Light + Expanded), 0 email/chức danh/phòng ban, 0 ảnh.

## 3. API mới / thay đổi

Mới (đều `RequireAuthorization`; tài khoản luôn lấy từ phiên, không có id từ client):

| API | Mô tả | Quyền |
|---|---|---|
| `GET /api/auth/profile` | Hồ sơ của chính mình (không có mật khẩu/hash/stamp) | mọi người dùng đã đăng nhập |
| `PUT /api/auth/profile` | `{displayName, email}` — chỉ hai trường này; trường khác trong body bị bỏ qua | chính mình |
| `GET /api/auth/avatar` | Ảnh của chính mình (404 nếu chưa có) | chính mình |
| `PUT /api/auth/avatar` | Thân yêu cầu là bytes ảnh (không tên tệp/đường dẫn/multipart) | chính mình |
| `DELETE /api/auth/avatar` | Xóa ảnh của mình (không có ảnh vẫn trả 200) | chính mình |
| `PUT /api/users/{id}/organization` | `{position, department}` | **chỉ Admin** |

Thay đổi tương thích ngược: `GET /api/users` trả thêm `email`, `position`, `department`. `PUT /api/users/{id}` và các API preferences/đổi mật khẩu giữ nguyên hành vi.

Ảnh đại diện:
- Chỉ PNG/JPEG. Loại tệp xác định **từ nội dung** (magic bytes + cấu trúc đầy đủ: PNG có IHDR và IEND, JPEG có khung SOF và EOI), bỏ qua `Content-Type` do client khai.
- Tối đa 512 KB, kích thước 32–2048 px mỗi cạnh. Từ chối SVG/HTML/GIF/WebP và tệp cắt dở. Đọc tối đa giới hạn + 1 byte nên ảnh quá lớn bị từ chối mà không đệm toàn bộ.
- Khi phục vụ: `Content-Type` chỉ lấy từ loại đã kiểm tra, `X-Content-Type-Options: nosniff`, `Content-Security-Policy: default-src 'none'; sandbox`, `Cache-Control: private, no-store`.
- Mã lỗi mới (đã dịch VI/EN): `AVATAR_EMPTY/TOO_LARGE/UNSUPPORTED_TYPE/INVALID_IMAGE/DIMENSIONS/NOT_FOUND`, `EMAIL_INVALID`, `ORGANIZATION_TOO_LONG`, `DISPLAY_NAME_LENGTH`; mã trường `EMAIL_INVALID/POSITION_MAX/DEPARTMENT_MAX`.
- Nhật ký kiểm toán (đã dịch): `USER_PROFILE_UPDATE`, `USER_AVATAR_UPDATE`, `USER_AVATAR_DELETE`, `USER_ORG_UPDATE` (không ghi mật khẩu).

## 4. Phân quyền

| Hành động | Operator | Admin |
|---|---|---|
| Xem/sửa họ tên, email, ảnh của chính mình | Có | Có |
| Sửa họ tên/email/ảnh của người khác | Không có route nào | Không có route nào |
| Xem ảnh của người khác | Không (`/api/auth/avatar` chỉ trả ảnh của người gọi) | Không |
| Sửa chức danh/phòng ban | 403 (kể cả của chính mình) | Có, cho mọi tài khoản |
| Đổi vai trò / kích hoạt | 403 | Như hiện hành |
| Đổi mật khẩu của người khác / xem mật khẩu | 403 / không có API trả mật khẩu | Đặt lại (như hiện hành), không xem được |
| Sửa Theme/Sidebar của người khác | Không có route nào | Không có route nào; sửa hồ sơ/tổ chức của Operator **không** làm đổi hai thiết lập này |

## 5. Kết quả kiểm thử

| Nhóm | Kết quả |
|---|---|
| Backend toàn bộ | **609 đạt, 0 lỗi** (551 trước đó + 58 mới) |
| Test mới: quy tắc ảnh/email/tổ chức (không DB) | gồm trong 58, đạt |
| Test mới: tích hợp có PostgreSQL (hồ sơ, validation, phân quyền Admin/Operator, ảnh, cô lập, đổi mật khẩu, cascade xóa) | gồm trong 58, đạt |
| Migration Up/Down/Up trên DB tạm | 12/12 đạt |
| ESLint, `tsc -b`, `npm run build` | sạch |
| `i18n:check` | OK (1080 khóa; mã lỗi/audit/trường mới đều có VI/EN) |
| `i18n:test` / `dashboard:test` / `preferences:test` | 76 / 18 / 10 đạt |
| Trình duyệt: kịch bản hồ sơ/ảnh/mật khẩu/tổ chức/Admin–Operator | **38/38 đạt** |
| Trình duyệt: ma trận bố cục + Dark Mode + hộp thoại + trạng thái lỗi | **30/30 đạt** |

## 6. Kiểm tra trình duyệt (Edge headless, chỉ Edge; DB tạm, API 5099 + Vite 5180/5181)

- Operator và Admin: sửa họ tên/email (email sai bị chặn), tải ảnh 1600×1200 (được thu về ≤ 512 px), thay bằng JPEG, GIF và tệp giả `.png` bị từ chối với thông báo rõ, SVG/HTML gửi thẳng lên Server bị từ chối, xóa ảnh trở về chữ cái đầu; đổi mật khẩu sai mật khẩu hiện tại / quá ngắn / không khớp / thành công (ô nhập được xóa, mật khẩu không còn trong trang) rồi đổi lại.
- Admin đặt chức danh/phòng ban cho Operator; Operator đăng nhập lại thấy dữ liệu chỉ-đọc; Theme/Sidebar, email, tên và ảnh của Operator **không đổi**; Dark + Collapsed của Operator được khôi phục từ Server trên cùng trình duyệt.
- Bố cục: 3 độ rộng (1440/1024/760) × Expanded/Collapsed × Light/Dark × VI/EN × 2 trang = 48 lượt, không tràn ngang, không chồng lấn, panel không tràn.
- Dark Mode: 12 trang × VI/EN, hộp thoại Tạo/Sửa/Đặt lại mật khẩu/Xóa tài khoản, hộp thoại Tạo nhiệm vụ, trang tài khoản có đồng thời lỗi trường + lỗi ảnh + lỗi mật khẩu và khi có thông báo đã lưu, và trang tài khoản trong Demo Mode (có dock mô phỏng): 0 chữ thiếu tương phản, 0 vùng nền sáng. Màu semantic (Running/Completed/Failed/Warning…) giữ nguyên.
- Ảnh chụp: `docs/archive/reviews/user-configuration/` (24 ảnh: Operator, Admin, đổi mật khẩu, trang Quản lý tài khoản trước/sau, hộp thoại, trạng thái lỗi, Light/Dark, VI/EN, 1440/1024/760, Demo).

## 7. Hạn chế / chưa làm

- Email không bắt buộc duy nhất, không xác minh, hệ thống không gửi thư (đã nói rõ trên giao diện).
- Ảnh đại diện chỉ hiển thị ở trang Cấu hình người dùng; chưa hiện ở header hay danh sách tài khoản của Admin (cũng không có API cho Admin xem ảnh người khác, theo yêu cầu riêng tư).
- Chỉ nhận PNG/JPEG (không GIF/WebP/SVG). Ảnh lưu trong DB (tối đa 512 KB/tài khoản).
- Chức danh/Phòng ban là văn bản tự do (chưa có danh mục). Admin không sửa được email của người dùng.
- Tên đăng nhập chưa đổi được (chưa có cơ chế quản trị tương ứng).
- Chính sách mật khẩu vẫn là mặc định kỹ thuật `NEEDS_CONFIRMATION` như trước.
- `Down` của migration làm mất dữ liệu hồ sơ/ảnh đã nhập.
- Chỉ kiểm tra trên Edge; chưa thử Chrome/Firefox/Safari hay thiết bị cảm ứng thật.
- Dock mô phỏng (Demo Mode) vẫn viết cứng tiếng Việt (có từ trước, ngoài phạm vi).
- Ngôn ngữ VI/EN vẫn lưu ở `localStorage`, chưa theo tài khoản trên Server (xem `USER_PROFILE_MISSING_FIELDS.md`).
- Thay đổi này được thực hiện theo yêu cầu trực tiếp của chủ dự án và không thuộc nghiệp vụ WCS nên không tạo Change Request riêng.
