# REV-005 – Câu hỏi cần nhà cung cấp RCS-2000 xác nhận

**Trạng thái: `NOT VERIFIED – RCS VENDOR CONFIRMATION REQUIRED`.** WCS **không giả định** RCS idempotent theo `RcsRequestCode`. Đến khi có xác nhận bằng văn bản, **không bật tích hợp RCS thật** và không dựa vào việc gửi lại `CreateTask` an toàn. (Hiện chỉ có `RcsSimulator`; Production từ chối task – CR-008A.)

## Điều WCS đang làm (FACT) và vì sao cần xác nhận
Khi create bị timeout/không rõ kết quả: WCS **truy vấn theo `RcsRequestCode` trước**; nếu RCS không tìm thấy (`NotFound`) hoặc trả trạng thái khác `NotFound` nhưng không có mã task thì WCS **gọi create lần nữa** với **cùng** `RcsRequestCode` (`TaskOrchestrator.DispatchToRcsAsync`). Spec `task-state-machine.md` 3.7: "khi nhận timeout, không mặc định rằng RCS chưa tạo task". Việc tạo lại chỉ an toàn nếu các điều dưới đây đúng.

## Câu hỏi cho nhà cung cấp
| # | Câu hỏi | Vì sao quan trọng | Ảnh hưởng nếu trả lời "không" |
|---|---|---|---|
| 1 | CreateTask có khóa idempotency không (trường nào, độ dài, phạm vi duy nhất, thời gian giữ)? | Chống tạo task trùng sau timeout | Phải bỏ cơ chế "tạo lại sau NotFound"; chỉ cho Admin quyết định |
| 2 | Gửi lại **cùng** `RequestCode` thì RCS: trả lại task cũ / từ chối (mã lỗi) / tạo task mới? | Quyết định xử lý phản hồi gửi lại | Task trùng ⇒ hai AGV chạy một yêu cầu |
| 3 | Truy vấn theo `RequestCode` ngay sau create: có thể trả "không có" trong khi task đang được tạo (bất đồng bộ)? Độ trễ tối đa? | Ý nghĩa của `NotFound` | Cần chờ/xác nhận nhiều lần trước khi tạo lại |
| 4 | Task đã được nhận nhưng phản hồi bị mất: RCS cung cấp cách tra lại đáng tin (theo `RequestCode`/`TaskCode`)? | Khôi phục sau mất phản hồi | Không thể phân biệt "chưa tạo" với "đã tạo" |
| 5 | CancelTask có idempotent không (hủy lặp, hủy task đã xong/đã hủy trả gì)? | Cancel được gửi lại theo backoff | Cần xử lý lỗi "đã kết thúc" như thành công/thất bại |
| 6 | Sau khi hủy/hoàn thành, task còn truy vấn được bao lâu? `NotFound` nghĩa là gì (chưa từng có / đã bị xoá)? | WCS **không** coi `NotFound` là đã hủy (D-9) | Không dùng được `NotFound` làm bằng chứng |
| 7 | Có API tra lịch sử task đã hoàn thành/hủy/gián đoạn (theo thời gian, theo robot)? | Đối soát bởi Admin | Đối soát chỉ dựa vào kiểm tra hiện trường |
| 8 | Ánh xạ mã trạng thái 0–10 → created/executing/canceling/canceled/completed/interrupted theo đúng phiên bản API; trạng thái nào có thể quay lại? | Adapter riêng (spec 3.8) | Sai ánh xạ ⇒ sai trạng thái WCS |
| 9 | Callback: có không, xác thực, thứ tự, gửi lặp, gửi muộn sau trạng thái cuối? | Idempotency của `HandleSignal` | Cần poll thay callback |
| 10 | Timeout/giới hạn tần suất khuyến nghị; hành vi khi RCS khởi động lại? | Giá trị backoff (2 s/60 s/5/30) hiện chỉ là kỹ thuật chưa xác nhận | Chỉnh thông số theo vendor |
| 11 | Hành vi khi robot Offline: nhận task hay từ chối? | Chính sách AGV Offline chưa được duyệt | Cần quyết định nghiệp vụ |

## Điều kiện chặn (gate) cho CR tích hợp RCS thật
Trả lời 1–6 và 8 bằng văn bản; thử nghiệm trên môi trường RCS thử nghiệm với các kịch bản: gửi trùng `RequestCode`, mất phản hồi sau create, hủy lặp, truy vấn sau hoàn thành. Chưa đạt ⇒ giữ `Integration:Mode=Simulator` và Production từ chối task.
