# Song ngữ Việt – Anh: kiến trúc, độ phủ và giới hạn

**Ngày:** 2026-11-17 · **Branch:** `develop` · Nối tiếp commit `d01f591` (cơ chế i18n + chuyển VI/EN).
Không đổi nghiệp vụ, TaskStateMachine, scheduler, giao tiếp RCS/PLC, quyền, điều kiện hiển thị trạng thái.

## 1. Nguyên tắc
Backend phát **mã ổn định + tham số**; Frontend dựng câu theo ngôn ngữ đang chọn, kể cả bản ghi lịch sử. Văn bản gốc luôn được giữ (tooltip / "Nội dung gốc") để chẩn đoán. Không dịch bằng cách dò từ trong câu tự do: nội dung không có mã được hiển thị đúng như đã lưu.

## 2. Nguồn dữ liệu log và cách dịch
| Nguồn | Trước đây | Bây giờ | Bản ghi lịch sử |
|---|---|---|---|
| **Audit** (`audit_log`) | `action` là mã ổn định; `detail` là hằng số / `CODE: message` / văn bản người dùng | Frontend dịch `action` (đủ 37 mã), hằng số cố định (`wrong password`…), `CODE: …` (theo mã lỗi); văn bản người dùng giữ nguyên | Dịch được toàn bộ phần có mã/hằng số |
| **System** (`system_log`) | Chỉ câu đã dựng (tiếng Anh), tham số chỉ nằm trong `DetailsJson` | **Thêm cột `MessageTemplate`** (khuôn câu Serilog, ổn định) + `DetailsJson` đã có; Frontend dịch theo khuôn (`systemLogTemplates.ts`, 49 khuôn) | Hàng cũ **không có khuôn** → hiển thị nguyên văn tiếng Anh |
| **PLC** (`plc_comm_log`) | `Event` là mã; `Message` tiếng Việt cố định | `Message` giữ làm văn bản gốc (nay tiếng Anh) + **tham số `args`** trong `DetailsJson`; Frontend dịch theo mã sự kiện (`codes.plcEvt_*`) | Hàng cũ không có `args` → tiêu đề đã dịch + văn bản gốc trong ngoặc |
| **Lỗi trường biểu mẫu** | Câu tiếng Việt từ server | `FieldError` có thêm `code` + `args` (52 mã); Frontend dịch theo mã | Không lưu |
| **Ghi chú runtime / lý do / kết quả kiểm tra kết nối** | Câu tiếng Việt | Tiền tố `[MÃ]` trước văn bản gốc (18 mã); Frontend dịch theo mã | Kết quả kiểm tra cũ (không có `[MÃ]`) giữ nguyên văn |
| **Mã lỗi API** | Có `code`, Frontend thiếu 33 mã | Bổ sung đủ 82 mã | — |
| **Tên mặc định "Quản trị viên"** | Tên hiển thị lưu trong DB (dữ liệu) | Đúng giá trị mặc định do hệ thống tạo → hiển thị "Administrator" ở EN; tên người dùng tự đặt giữ nguyên | — |

Thay đổi backend (additive, không đổi ý nghĩa audit): migration `20261117064702_AddLogLocalization` (cột `system_log.MessageTemplate` + tạo lại view `v_log_entries` thêm `MessageTemplate`, `DetailsJson`); DTO log; `FieldError`/`AddressIssue` thêm `Code`/`Args`; `IPlcEvents.Log(..., args)`; thông điệp PLC chuyển sang tiếng Anh + tham số.

## 3. Kiểm tra tự động
- `npm run i18n:check` (`scripts/check-i18n.mjs`, đọc mã nguồn, không cần DB): khóa VI/EN đủ và cùng tham số, không khóa thừa; không còn chuỗi Việt/Anh hardcode trong JSX; mọi mã backend (mã lỗi, hành động audit, sự kiện lịch sử nhiệm vụ, sự kiện PLC, mã lỗi trường, mã `[CODE]`, khuôn log được lưu) phải có bản dịch — thêm mã mới ở backend mà quên dịch sẽ làm script thất bại. Lần chạy đầu phát hiện 48 thiếu sót thật (33 mã lỗi, 11 hành động audit…), đã xử lý.
- Test backend `BilingualLogTests` (5): mọi lỗi validation có mã; chính sách mật khẩu và địa chỉ IO có mã + tham số; khuôn log được lưu; tham số PLC và khuôn đi qua API log.

## 4. Chưa thể / chưa dịch (trung thực)
1. **Task history `detail`** (`task.Note(event, detail)`): câu tiếng Anh dựng sẵn ở backend. VI dịch bằng các quy tắc khớp câu tiếng Anh cố định (có từ trước), EN hiển thị nguyên văn. Chưa chuẩn hóa thành mã + tham số (khoảng 40 điểm gọi) → đề xuất CR riêng.
2. **Log lịch sử trước migration**: system_log không có khuôn → nguyên văn tiếng Anh ở cả VI; plc_comm_log có tiêu đề dịch + văn bản gốc tiếng Việt; kết quả kiểm tra kết nối đã lưu giữ nguyên văn tiếng Việt. Không tự suy diễn.
3. **Cảnh báo/ngoại lệ của framework** (ASP.NET, EF…) trong system_log: không có khuôn của WCS → nguyên văn.
4. **Chế độ DEMO** (`src/demo/*`): chưa dịch (công cụ kỹ thuật).
5. `RANGE` dùng nhãn trường từ bảng trong Frontend; trường mới cần thêm `fieldLabel_*`.
6. Thông báo "đã lưu/đã tạo…" và lỗi đang hiện giữ ngôn ngữ lúc phát sinh cho đến thao tác kế tiếp.
7. **Chưa kiểm tra bằng trình duyệt** (không có công cụ): bố cục/tràn chữ ở EN, đổi ngôn ngữ trên từng trang, hộp thoại, log lịch sử trên giao diện thật.

## 5. Vận hành
Cần áp migration mới trước khi chạy backend: `dotnet ef database update --project WCS.Infrastructure --startup-project WCS.Api` (sao lưu DB trước). Migration chỉ thêm cột và tạo lại view; có `Down`.
