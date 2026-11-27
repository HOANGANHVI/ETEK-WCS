# Hoàn thiện điều hướng User Configuration và Avatar trên Header

Ngày: 2026-11-17 · Branch `develop` · nền `205a187` (không amend). Chỉ thay đổi giao diện; không đổi API, Database, migration, nghiệp vụ WCS, PLC, RCS, Scheduler, TaskStateMachine, JWT/cookie hay phân quyền.

> Yêu cầu nhắc tới "ảnh mẫu người dùng gửi" nhưng ảnh không đến được phiên làm việc này. Bố cục Header làm theo mô tả bằng chữ (tên tài khoản + Avatar tròn nhỏ ở bên phải, bấm vào mở menu). Nếu ảnh mẫu khác mô tả, cần gửi lại để chỉnh.

## 1. Header sau thay đổi

- Đã xóa liên kết "Cấu hình người dùng", khối tên cũ và nút "Đăng xuất" rời. Thay bằng **một menu tài khoản** nằm bên phải, giữa bộ chuyển ngôn ngữ VI/EN và đồng hồ.
- Nút gồm: tên hiển thị + vai trò (2 dòng, căn phải) · Avatar tròn **32 px** · mũi tên.
- Avatar: ảnh từ `/api/auth/avatar`; chưa có ảnh thì chữ cái đầu của tên; không có tên thì icon người dùng. Không có ảnh mặc định hay ảnh giả.
- Menu xổ xuống: thẻ thông tin (tên, tên đăng nhập · vai trò), **Thông tin tài khoản** → `/account`, **Đăng xuất** (dùng đúng luồng đăng xuất sẵn có).
- Đóng khi: bấm ra ngoài, Escape (trả focus về nút), Tab, hoặc sau khi chọn. Bàn phím: ArrowDown mở/di chuyển và quay vòng, ArrowUp, Home, End. Có `aria-haspopup`, `aria-expanded`, `aria-controls`, `role="menu"/"menuitem"`.
- Menu là phần tử định vị tuyệt đối với `z-index` cao, không bị Header hay Sidebar cắt; rộng tối đa `100vw − 16px`.
- Màn hình hẹp (≤ 1000 px): nút thu thành Avatar + mũi tên (tên có trong tooltip, `aria-label` và thẻ trong menu) để Header không tràn. Chiều cao Header giữ nguyên; logo, thương hiệu và tiêu đề không đổi.

## 2. Sidebar sau thay đổi

- Thêm nhóm **Cá nhân** ở **cuối** Sidebar, tách khỏi nhóm vận hành/cấu hình/hệ thống, chứa mục **Cấu hình người dùng** (icon người dùng).
- Mở đúng trang `/account` hiện có (không tạo trang thứ hai); mục sáng (active) khi đang ở `/account`, các mục khác không sáng.
- Admin và Operator đều thấy. Thứ tự: Operator "Vận hành → Cá nhân"; Admin "Vận hành → Cấu hình PLC → Hệ thống → Cá nhân".
- Sidebar thu gọn: chỉ icon, có tooltip như các mục khác.
- Do nhãn "Cấu hình người dùng" bị cắt "…" ở 224 px, bề rộng Sidebar mở rộng tăng lên **236 px** (thu gọn 68 px và ≤ 800 px 184 px giữ nguyên).

## 3. Đồng bộ Avatar và tên người dùng

- Thêm `ProfileProvider` (một bản hồ sơ dùng chung, chỉ ở bộ nhớ, **không lưu localStorage**). Header và trang Cấu hình người dùng đọc cùng một nguồn nên đổi họ tên / tải / thay / xóa ảnh trên trang là Header đổi ngay, không cần đăng xuất hay tải lại.
- Chống ảnh cũ: URL ảnh mang `?v=<avatarVersion>` (đổi sau mỗi lần thay/xóa) và máy chủ trả `Cache-Control: private, no-store`. Đã kiểm chứng: thay PNG bằng JPEG thì phiên bản URL đổi và nội dung trả về là JPEG mới.
- Đổi tài khoản trên cùng trình duyệt: giá trị hồ sơ gắn với **một tên đăng nhập**; khi tài khoản đổi hoặc đăng xuất, hồ sơ cũ biến mất ngay lập tức (được dẫn xuất theo tên đăng nhập hiện tại, không phụ thuộc thứ tự effect). Đã lấy mẫu Header mỗi 80 ms trong 3,2 s sau khi đăng nhập Operator ngay sau Admin có ảnh: 0 lần thấy tên hay ảnh của Admin.
- Tên hiển thị lấy từ tài khoản đang đăng nhập (hồ sơ máy chủ; dự phòng là tên trong phiên), không hardcode. Đăng xuất: màn hình đăng nhập, không còn ảnh/tên trong trang; `localStorage` chỉ còn ngôn ngữ và cache preferences (không có tên, email hay ảnh).
- JWT/cookie, refresh và luồng xác thực không đổi; không có API upload hay lưu ảnh mới.

## 4. Demo Dock VI/EN

- Toàn bộ chữ của dock (thanh điều khiển, tình huống và gợi ý, sự cố, đối soát, kết nối AGV, ghi chú, tooltip, thông báo trạng thái) chuyển sang từ điển mới `demo` (VI/EN, 52 khóa). Dòng trạng thái lưu dưới dạng khóa nên **đổi ngôn ngữ là dịch lại ngay**. Thông báo lỗi dùng bản dịch theo mã lỗi dùng chung; mã lạ hiển thị câu chung.
- Không đổi logic Demo Mode (engine, kịch bản, tự chạy, đặt lại).
- Đã kiểm 4 tổ hợp Sáng/Tối × VI/EN: bản EN của dock **không còn ký tự tiếng Việt** (kể cả tooltip/aria), bản VI không có câu tiếng Anh; 0 chữ thiếu tương phản.
- Còn lại (ngoài dock): dữ liệu do engine Demo sinh ra (ví dụ lý do/ghi chú nhiệm vụ mô phỏng) vẫn có thể bằng tiếng Việt vì nằm trong engine; không sửa theo yêu cầu "không thay đổi logic Demo Mode".

## 5. Chính sách mật khẩu hiện tại (không đặt chính sách mới)

Đang triển khai (`PasswordPolicy`, trình duyệt chỉ phản chiếu để tiết kiệm một lượt gọi; Backend quyết định):
- Độ dài 10–128 ký tự; phải có ít nhất một chữ và một chữ số; không được chứa tên đăng nhập (không phân biệt hoa/thường).
- Mật khẩu mới phải khác mật khẩu hiện tại; Admin đặt lại thì người dùng bị buộc đổi ở lần đăng nhập sau; mật khẩu chỉ lưu dạng băm có muối.
- Khóa tạm sau 5 lần sai trong 5 phút; phiên hết hạn sau 30 phút không hoạt động hoặc 12 giờ.

Trạng thái: **`NEEDS_CONFIRMATION`** (khách hàng chưa quy định, đây là mặc định kỹ thuật). Cần người quản trị xác nhận: độ dài tối thiểu; có yêu cầu chữ hoa/ký tự đặc biệt không; có hạn dùng/đổi định kỳ và lịch sử mật khẩu không; số lần sai và thời gian khóa; thời gian phiên.

## 6. Email

- Giữ nguyên là trường **tùy chọn**; không xác minh, không gửi thư (chưa có hạ tầng).
- Kiểm tra định dạng (một dấu `@`, miền có dấu chấm, không khoảng trắng/ký tự điều khiển, phần cục bộ ≤ 64, nhãn miền ≤ 63) và độ dài ≤ 254, cả ở giao diện và Backend (đã có test).
- **Không duy nhất (unique)**: hiện không gây ảnh hưởng chức năng vì email không dùng để đăng nhập, định danh, đặt lại mật khẩu hay gửi thông báo; định danh duy nhất vẫn là tên đăng nhập. Rủi ro xuất hiện khi sau này dùng email cho quên mật khẩu/thông báo/tìm kiếm: hai tài khoản trùng email sẽ gây nhập nhằng. Khi đó cần chốt quy tắc duy nhất (không phân biệt hoa/thường) kèm làm sạch dữ liệu trùng trước khi thêm ràng buộc; nên quyết định trước khi triển khai các tính năng đó.

## 7. Migration

- Không chạy `Down` trên `wcs_db`, không sửa migration đã áp dụng, không rollback để thử nghiệm. Đợt này không có migration mới.
- Rủi ro rollback `AddUserProfile`: `Down` xóa cột `Email/Position/Department` và bảng `user_avatars` nên **mất toàn bộ dữ liệu hồ sơ và ảnh đã nhập** (tài khoản và preferences giữ nguyên). Nếu phải rollback, hãy sao lưu trước (bản sao lưu đã có: `C:\Users\Admin\wcs-backups\wcs_db_before_AddUserProfile_20261117-192858.dump` là **trước** khi thêm dữ liệu hồ sơ, không chứa dữ liệu hồ sơ mới).

## 8. Chức danh / Phòng ban

Giữ nguyên: Admin quản lý, Operator chỉ xem; văn bản tự do, chưa chuyển sang danh mục chuẩn hóa.

## 9. Kết quả kiểm thử

| Nhóm | Kết quả |
|---|---|
| Backend (không đổi mã) | **609 đạt, 0 lỗi** (chạy lại) |
| ESLint, `tsc -b`, `npm run build` | sạch |
| `i18n:check` | OK (1135 khóa) |
| `i18n:test` / `dashboard:test` / `preferences:test` | 76 / 18 / 10 đạt |
| Trình duyệt Edge (Admin + Operator) | **50/50 đạt** |

Trình duyệt (Edge headless, chỉ Edge; DB tạm + API + Vite, đã dừng và xóa sau khi chạy): Header không còn link/nút cũ; Sidebar nhóm Cá nhân đúng thứ tự cho từng vai trò, click mở `/account`, mục active; Avatar: chưa có ảnh → chữ cái đầu, tải ảnh → hiện ngay, thay ảnh → URL phiên bản đổi và nội dung mới, đổi họ tên → Header đổi ngay, xóa ảnh → về chữ cái đầu; menu: mở/đóng bằng chuột, ArrowDown/Up/Home/End, Escape, Tab, bấm ra ngoài, "Thông tin tài khoản" và "Đăng xuất"; đổi tài khoản Admin→Operator trên cùng trình duyệt (lấy mẫu 40 lần) không lộ dữ liệu tài khoản trước; ma trận 3 độ rộng (1440/1024/760) × Expanded/Collapsed × Sáng/Tối × VI/EN × 3 trang = 72 lượt + kiểm menu ở 24 tổ hợp: không tràn ngang, không chồng lấn, Header ≤ 60 px, Avatar 32×32, menu không bị cắt hay che; Dark Mode với menu mở (VI/EN): 0 chữ thiếu tương phản, 0 vùng nền sáng; Demo Mode 4 tổ hợp.

Ảnh chụp: `docs/archive/reviews/ui-navigation/` (Header có/không có Avatar, menu mở Sáng/Tối VI/EN và 760 px, Sidebar Operator, trang Cấu hình người dùng từ Sidebar, 1440/1024/760 Expanded/Collapsed, Demo Dock Sáng/Tối VI/EN).

## 10. Vấn đề còn tồn tại

- Chỉ kiểm tra trên Edge (chưa Chrome/Firefox/Safari hay cảm ứng thật).
- Ở ≤ 1000 px tên người dùng chỉ còn trong tooltip/menu (Header chỉ hiện Avatar).
- Nhãn menu dài vẫn bị cắt "…" ở Sidebar 184 px (≤ 800 px); có tooltip.
- Dữ liệu do engine Demo sinh ra có thể còn tiếng Việt (xem mục 4).
- Mật khẩu: chính sách vẫn `NEEDS_CONFIRMATION`; email chưa unique/xác minh.
