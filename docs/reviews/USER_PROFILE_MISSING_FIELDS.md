# Trường hồ sơ người dùng còn thiếu – đầu vào cho một CR bổ sung

> **CẬP NHẬT 2026-11-17: Email, Ảnh đại diện, Chức danh và Phòng ban đã được triển khai theo yêu cầu trực tiếp của chủ dự án** (migration `AddUserProfile`). Xem [USER_CONFIGURATION_REPORT.md](USER_CONFIGURATION_REPORT.md). Phần dưới đây là hiện trạng tại thời điểm trước đó và được giữ để truy vết; các câu hỏi mở còn lại là: email có bắt buộc duy nhất/xác minh không, danh mục chức danh/phòng ban, ngôn ngữ theo tài khoản.

Ngày: 2026-11-17 · Liên quan: trang "Cấu hình người dùng" (`/account`), commit `dddd628` và đợt hoàn thiện kế tiếp.

> Tài liệu này **chỉ ghi nhận hiện trạng và câu hỏi cần chủ dự án quyết định**. Chưa có Change Request nào được duyệt cho các trường dưới đây,
> nên **không có cột dữ liệu, API hay dữ liệu giả nào được thêm**. Trang `/account` giữ nguyên cách hiển thị hiện tại.

## 1. Hiện trạng dữ liệu tài khoản (`AppUser`)

Có: `Username`, `DisplayName`, `Role` (Admin/Operator), `IsActive`, `MustChangePassword`, `LastLoginAt`, `CreatedAt`, `UpdatedAt`, và (từ `dddd628`) `Theme`, `SidebarCollapsed`.

## 2. Trường chưa được hỗ trợ

| Trường | Hiện trạng trên giao diện | Dữ liệu trong hệ thống | Ghi chú cho CR |
|---|---|---|---|
| Email | Không hiển thị; có ghi chú "chưa có trong dữ liệu tài khoản" (`account.orgNote`) | Không có | Cần quyết định: bắt buộc hay tùy chọn; có xác minh không; có dùng để khôi phục mật khẩu/thông báo không (hiện chưa có luồng nào dùng email); duy nhất toàn hệ thống? |
| Ảnh đại diện (Avatar) | Hiển thị chữ cái đầu của họ tên; ghi chú "hệ thống chưa lưu ảnh đại diện" (`account.avatarHint`) | Không có | Cần quyết định: có cần tải ảnh lên không; nơi lưu (DB/đĩa/đối tượng), giới hạn dung lượng, định dạng, kiểm tra nội dung tệp; hay chỉ giữ chữ cái đầu. |
| Chức danh (Position) | Không hiển thị; chung ghi chú `account.orgNote` | Không có | Cần quyết định: văn bản tự do hay danh mục; ai được sửa (Admin hay chính người dùng). |
| Phòng ban / bộ phận (Department) | Không hiển thị; chung ghi chú `account.orgNote` | Không có | Cần quyết định: văn bản tự do hay danh mục (liên quan đến ca/xưởng); có dùng để phân quyền hay chỉ để hiển thị. |

## 3. Câu hỏi chung khi mở CR

1. **Ai chỉnh sửa**: các trường này do Admin quản lý (như `DisplayName` hiện nay) hay người dùng tự sửa? Hiện trang tự phục vụ chỉ cho đổi mật khẩu và tùy chọn giao diện.
2. **Quyền riêng tư**: email/ảnh là dữ liệu cá nhân – cần quy định hiển thị cho ai (Operator có thấy hồ sơ người khác không?) và việc ghi nhật ký kiểm toán khi thay đổi.
3. **Di chuyển dữ liệu**: các cột mới phải thêm bằng migration cộng thêm (nullable), không đổi dữ liệu tài khoản hiện có, có `Down`.
4. **Không ảnh hưởng nghiệp vụ**: các trường này không tác động tới `TaskStateMachine`, PER_AGV, Scheduler, tồn kho, AGV/PLC/RCS.
5. **i18n**: nhãn VI/EN phải đi qua `i18n/messages/account.ts` (đã có cơ chế `MsgKey`).

## 4. Quan sát liên quan (không thuộc yêu cầu, chỉ để chủ dự án cân nhắc)

- **Ngôn ngữ giao diện (VI/EN)** hiện lưu trong `localStorage` của trình duyệt (`wcs.lang`), **chưa** là tùy chọn theo tài khoản trên máy chủ như Theme/Sidebar. Nếu muốn ngôn ngữ đi theo người dùng qua các máy, cần mở rộng cùng cơ chế `UserPreferences` bằng một CR.
- **Dock mô phỏng (Demo Mode)** hiển thị cố định tiếng Việt (chuỗi viết cứng trong `DemoDock.tsx`), kể cả khi giao diện đang ở EN. Đây là khoảng trống i18n có từ trước, không phải lỗi theme; chưa sửa vì ngoài phạm vi đợt này.

## 5. Khuyến nghị

Chỉ mở CR khi có nhu cầu nghiệp vụ rõ ràng cho từng trường. Đề xuất tách thành các CR nhỏ: (a) Email + Chức danh + Phòng ban (cột văn bản nullable, Admin quản lý) và (b) Ảnh đại diện (kèm thiết kế lưu trữ), để khối lượng ảnh/tệp không làm chậm phần còn lại.
