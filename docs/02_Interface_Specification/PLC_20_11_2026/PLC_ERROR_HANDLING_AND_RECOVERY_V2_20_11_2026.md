# PLC Error Handling & Recovery – cập nhật theo giao diện 20/11/2026

| | |
|---|---|
| **Trạng thái** | `SPECIFICATION – PENDING PLC CLARIFICATION` |
| **Cập nhật cho** | `PLC_COMMUNICATION_SPECIFICATION_DRAFT.md` §11–13, `PLC_SPEC_OPEN_QUESTIONS.md` §1, ADR-0004 (ghi mơ hồ), CR-008B-2 |
| **Nhãn** | [N] [SL] [CX] [HT] [KT] – xem [Interface Specification](PLC_INTERFACE_SPECIFICATION_V2_20_11_2026.md) |
| **Nguyên tắc** | Không tạo giá trị timeout/retry/mã lỗi khi chưa được duyệt: ghi `NEEDS_CONFIRMATION` **[KT]** |

## 1. Nguyên tắc đã thống nhất, **giữ nguyên** **[KT][HT]**

1. Mất kết nối PLC: giao dịch **được giữ, không hủy, không tự reset**; khi kết nối lại đọc IO thực và đối chiếu DB, tiếp tục đúng bước; không đủ dữ liệu ⇒ chờ đối soát (`ReconciliationRequired`).
2. Trạng thái giao dịch lưu bền trong DB; khôi phục sau khi WCS khởi động lại; mọi vòng lặp là idempotent.
3. Một ghi lên PLC có thể đã đến PLC mà không có xác nhận (timeout/ngắt/hủy) ⇒ **không gửi lại**; báo `WRITE_AMBIGUOUS`; kiểm tra giá trị trước khi hành động (ADR-0004 #15).
4. Không bao giờ coi dữ liệu không còn "live" là dữ liệu hiện tại (chất lượng `Disconnected/Stale`); dữ liệu hết hạn không tạo cạnh yêu cầu.
5. PLC offline không ảnh hưởng 3 PLC còn lại; WCS vẫn chạy.
6. Không ghi tín hiệu an toàn/cho phép chuyển động để thử kết nối; đọc-trước, ghi-sau-khi-duyệt.
7. Dữ liệu PLC không ghi đè tồn kho WCS; mâu thuẫn ⇒ đối soát, không "sửa tạm".

## 2. Tình huống lỗi trong giao diện 20/11

| # | Tình huống | [N] Nguồn nêu | [SL] Suy luận | [CX] Chưa xác định (PQ) | Hành vi tạm thời (an toàn, không cần bên PLC) |
|---|---|---|---|---|---|
| E-1 | `Mission_Feedback` > 10 (phần mềm báo lỗi cho PLC) | "Lỗi" (J9) | PLC dừng/hủy yêu cầu? | Danh sách mã lỗi, ai reset, PLC làm gì (PQ-05, PQ-15) | WCS chỉ ghi khi có quy tắc được duyệt; ghi nhật ký; không tự hủy giao dịch |
| E-2 | `PLC_MissionRequest` > 10 (PLC báo lỗi) | ">10 lỗi" **chỉ dòng E1** (J4) | PLC tự báo lỗi nội bộ | Có áp dụng cho mọi trạm; mã lỗi; có cần WCS phản hồi (PQ-05) | Đọc và ghi nhật ký; không tạo task; không coi là Pick/Drop |
| E-3 | `PLC_MissionCompleted` = 0 (thất bại) | "0: Thất Bại" (J6) | Thất bại giao/nhận | Phân biệt "chưa có kết quả" với "thất bại" (cùng 0); hậu quả: rack ở đâu (PQ-09, PQ-15) | Không cập nhật tồn kho từ tín hiệu này; đưa giao dịch sang `ReconciliationRequired` **chỉ khi có quy tắc được duyệt**; ghi nhật ký |
| E-4 | Mất `HeartBit` / timeout 10 s | "on/off chu kì 1s … timeout 10s" (J28/K28) | PLC coi phần mềm mất kết nối sau 10 s không đổi | Ai đo timeout; PLC làm gì; có tín hiệu PLC→SW báo heartbeat PLC không (PQ-19) | WCS phải tự phát hiện mất liên lạc bằng chất lượng polling (cơ chế hiện có), không bằng heartbeat PLC |
| E-5 | Mất kết nối WCS↔PLC | – | – | Timeout/retry cho từng bước (PQ-11) | Giao dịch `Suspended`, không hủy, không reset; kết nối lại → đọc lại 6 tín hiệu và đối chiếu |
| E-6 | PLC khởi động lại giữa giao dịch | – | Các tín hiệu PLC→SW có thể về 0 | Giá trị khởi tạo, WCS phải ghi lại gì (PQ-12) | Coi dữ liệu thay đổi đột ngột là bất thường → đối soát; **không** ghi lại tín hiệu điều khiển cho tới khi PLC xác nhận |
| E-7 | WCS khởi động lại giữa giao dịch | – | – | Tín hiệu SW→PLC đã ghi có còn giá trị (PQ-04, PQ-08) | Khôi phục từ DB + nhật ký ý định ghi; đọc lại giá trị tín hiệu SW→PLC trước khi quyết định; mơ hồ ⇒ đối soát |
| E-8 | Hai yêu cầu liên tiếp / yêu cầu lặp | – | – | Không còn `Request_ID` (PQ-10) | Cổng đang có giao dịch hoạt động ⇒ không tạo giao dịch thứ hai (cơ chế hiện có: `PORT_BUSY`) |
| E-9 | `PLC_ReaddyTranfer` không đến sau `AGV_RequestTranfer` | – | – | Timeout, retry (PQ-11) | Không tự hủy; cảnh báo quá thời gian **chỉ khi** có giá trị được duyệt; giữ nguyên trạng thái |
| E-10 | Tín hiệu mâu thuẫn (ví dụ `PLC_MissionCompleted`=1 khi `AGV_TranferCompleted`=0; `PLC_ReaddyTranfer`=1 khi chưa có yêu cầu mở) | K22: `PLC_MissionCompleted` "thay đổi theo `Mission_Feedback` và `AGV_TranferCompleted`" | Chuỗi bất thường | Hành vi chuẩn khi vi phạm thứ tự (PQ-03, PQ-09) | Không dựa trên tín hiệu bất thường; ghi nhật ký; `ReconciliationRequired` khi quy tắc được duyệt |
| E-11 | Nhiều trạm cùng PLC lỗi/đồng thời | – | – | PQ-13, PQ-14 | Lỗi một trạm không dừng trạm khác (đã có ở worker) |
| E-12 | Dữ liệu vị trí/rack không còn từ PLC nên không phát hiện lệch PLC↔tồn kho | – | – | PQ-17 | Phát hiện lệch dựa trên chính sách đối soát WCS (không còn kiểm tra qua `Rack_Full/Empty`) |

## 3. Khôi phục sau mất kết nối / khởi động lại – quy tắc chung **[ĐX]**

1. Sau kết nối lại: đọc toàn bộ 6 tín hiệu của từng trạm đang có giao dịch hoạt động; so sánh với **trạng thái cuối cùng đã lưu**.
2. Nếu giá trị đọc được **phù hợp** với một trạng thái hợp lệ kế tiếp ⇒ tiến tới trạng thái đó (PLC sở hữu tín hiệu PLC→SW; WCS sở hữu tín hiệu SW→PLC).
3. Nếu **không phù hợp** hoặc không đủ dữ liệu ⇒ `ReconciliationRequired`, giữ cổng bận; một Admin quyết định.
4. Không ghi lại tín hiệu SW→PLC "cho chắc" khi chưa biết giá trị hiện tại (rủi ro lặp lệnh mở cửa/kẹp).
5. Mọi quy tắc timeout/retry cụ thể: `NEEDS_CONFIRMATION` (PQ-11).

## 4. Lỗi của quá trình RCS liên quan giao dịch PLC (nội bộ)

- AMR chưa được gán (CR-028 `AwaitingAssignment`): `Mission_Feedback` giữ 0 (chưa có AGV nhận) **[SL theo J9]**; không ghi AGV tùy ý.
- RCS báo đổi AMR khi rack đang trên xe (CR-028): Order `ReconciliationRequired`; không ghi giá trị `Mission_Feedback` mới cho đến khi đối soát. (Phụ thuộc CR-028; PQ-06.)
- Task/Leg bị lỗi sau khi PLC đã mở cửa/kẹp: ảnh hưởng an toàn – quy tắc đóng/nhả kẹp không có trong 20/11 (PQ-07, PQ-18).

## 5. Việc chưa thực hiện

Không có thay đổi mã. Chức năng hiện có xử lý các lỗi giao tiếp (kết nối lại có back-off, ghi mơ hồ, chất lượng dữ liệu, treo/tiếp tục giao dịch, đối soát) là **nền tảng giữ nguyên**; phần phụ thuộc tín hiệu cũ cần làm lại sau khi có câu trả lời của bên PLC.

## 6. Cập nhật theo D-1, D-2, D-4 (2026-11-21, [ADR-0011](../../decisions/ADR/ADR-0011-plc-interface-20-11-2026-architectural-decisions.md))

- **E-3 (`PLC_MissionCompleted`=1/0):** theo D-4, giá trị 1 **không** là bằng chứng đủ rằng đúng rack đã được giao/nhận đúng vị trí; thiếu bằng chứng rack (có mặt/danh tính) ⇒ `ReconciliationRequired`, không tự hoàn thành.
- **Hàng đợi sau sự cố (D-1):** hàng đợi cấp PLC phải **bền**; sau khởi động lại WCS hoặc PLC, thứ tự FIFO và lượt `Active` được khôi phục từ DB, không được cấp hai lượt cho một PLC; mất liên lạc PLC ⇒ lượt `Active` giữ nguyên (không hủy, không reset), các yêu cầu chờ giữ nguyên.
- **Feedback lỗi (D-2):** WCS không ghi định danh AMR vào `Mission_Feedback`; mã lỗi và trạng thái do giao diện phản hồi mới quy định (IF-P3, PQ-05, PQ-15).
