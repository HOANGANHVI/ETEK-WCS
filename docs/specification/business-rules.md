# 1. Business Rules – Quy tắc nghiệp vụ

## 1.1. Mục tiêu hệ thống

WCS là lớp điều phối nghiệp vụ giữa Mitsubishi PLC, người vận hành và HIK RCS-2000. WCS tiếp nhận yêu cầu vận chuyển, xác định AGV phù hợp, quản lý trạng thái rack/vị trí, gửi task tới RCS và theo dõi quá trình giao nhận.

## 1.2. Phạm vi tích hợp

- Một kết nối PLC vật lý tới Mitsubishi PLC.
- Một kết nối API tới HIK RCS-2000.
- Hai AGV theo phân công nghiệp vụ:
  - `AGV1`: vận chuyển rack hàng đầy.
  - `AGV2`: vận chuyển rack trống.
- Một PLC connection phục vụ nhiều station/transfer point. Không chia station thành cluster trong logic WCS.

## 1.3. Quyết định nghiệp vụ bắt buộc

| ID | Quy tắc | Giá trị |
|---|---|---|
| BR-01 | Task concurrency | `PER_AGV` |
| BR-02 | Luồng slide 8 – B1/B2 cần rack đầy | `REVIEW` |
| BR-03 | Quản lý Bind Rack / tồn kho rack | `WCS` |
| BR-04 | A3/A4 và D1/D2 | `UNCHANGED` |

### BR-01 – PER_AGV

- Mỗi AGV được phép có tối đa một task đang hoạt động.
- AGV1 và AGV2 có thể thực hiện task song song.
- Không áp dụng giới hạn một task cho toàn hệ thống.
- Một AGV đang thực hiện task không được nhận task mới, trừ khi task hiện tại đã kết thúc theo trạng thái cuối cùng được xác nhận.
- Các yêu cầu đến trong lúc AGV bận phải được lưu hàng đợi.

### BR-02 – REVIEW

Luồng slide 8 mô tả B1/B2 có vị trí trống và yêu cầu rack đầy. Tài liệu nguồn có sự không nhất quán về định danh AGV trong các bước. Vì vậy:
- Không tự sửa AGV1/AGV2.
- Không triển khai luồng này trong phiên bản nghiệp vụ đã chốt.
- Chỉ đưa vào triển khai sau khi người phụ trách nghiệp vụ xác nhận lại luồng.

### BR-03 – WCS sở hữu trạng thái rack

WCS là nguồn dữ liệu nghiệp vụ chính cho:
- Rack ID (nếu có).
- Loại rack.
- Trạng thái `FULL` / `EMPTY`.
- Vị trí hiện tại.
- Trạng thái chiếm dụng của vị trí.
- Trạng thái di chuyển và task liên quan.

WCS cập nhật tồn kho dựa trên sự kiện giao nhận đã xác nhận, không dựa đơn thuần vào việc tạo task thành công.

Các bước được tài liệu nguồn ghi rõ `Không bind rack` vẫn phải giữ nguyên. WCS không được tự ý áp đặt thao tác bind rack tại các điểm đó.

### BR-04 – A3/A4 và D1/D2

- Giữ nguyên process cũ.
- Không áp dụng các luồng A1/A2 hoặc B1/B2 thay thế cho các vị trí này.
- Chi tiết triển khai phải tham chiếu process cũ. Nếu process cũ chưa có trong bộ tài liệu đầu vào, đánh dấu phụ thuộc tài liệu và không tự tạo quy trình.

## 1.4. Phân công AGV

### AGV1 – rack hàng đầy

Theo tài liệu quy trình:
- Nguồn: KV1, KV3.
- Đích: KV2, KV4, F5.

### AGV2 – rack trống

Theo tài liệu quy trình:
- Nguồn: KV2, KV4.
- Đích: KV1, KV3.

Các luồng chi tiết có thể sử dụng thêm F1/F2/F3/F4 và A1/A2/B1/B2 theo từng kịch bản được mô tả trong `process-flows.md`. Không suy diễn rằng mọi vị trí đều là nguồn/đích hợp lệ cho mọi loại rack.

## 1.5. Vị trí

| Vị trí | Mô tả từ tài liệu / vai trò |
|---|---|
| A1/A2 | Trạm máy |
| B1/B2 | Trạm máy; có các kịch bản rack trống và vị trí trống |
| F1/F2 | Buffer rack hàng đầy trong các luồng được mô tả |
| F3/F4 | Buffer rack trống trong các luồng được mô tả |
| F5 | Manual Buffer |
| KV1/KV2 | Điểm trong luồng vận chuyển |
| KV3 | Buffer hàng |
| KV4 | Buffer rack trống |

Không tự gán thêm ý nghĩa cho KV1/KV2 ngoài sơ đồ nguồn nếu chưa có xác nhận.

## 1.6. Nguyên tắc an toàn và giao nhận

- “PLC mở cửa” trong quy trình được hiểu là PLC cho phép cơ cấu/điểm Load-Unload của máy thực hiện giao nhận; không diễn giải thành cửa ra vào nhà xưởng.
- AGV chỉ thực hiện bước vào lấy/thả khi nhận được điều kiện cho phép tương ứng.
- Nếu chưa có xác nhận giao nhận vật lý, không đánh dấu task hoàn tất và không cập nhật tồn kho đích như đã có rack.
- Khi lỗi giữa hành trình, phải xác minh vị trí rack thực tế trước khi điều chỉnh tồn kho.

## 1.7. Quy tắc khi thiếu điều kiện

- Không có vị trí đích: giữ task ở `WAITING_FOR_DESTINATION`, đánh giá lại khi trạng thái vị trí thay đổi.
- Không tạo task trùng cho cùng AGV.
- Không cho phép hai task chiếm cùng rack hoặc cùng vị trí đích nếu gây xung đột.
- Các ưu tiên nguồn/đích chưa được xác nhận phải được cấu hình hoặc đánh dấu `NEEDS_CONFIRMATION`, không tự hard-code.

---

## 1.8. Addendum – PLC topology and Station IO architecture (approved 2026-11-06, CR-002)

This addendum supersedes only the earlier PLC topology statements in sections 1.2 and 1.2's description of a single PLC connection. All other approved business rules remain unchanged.

- Phase 1 uses **4 physical Mitsubishi PLCs** and **16 Stations**.
- WCS maintains **4 independent PLC connections**, one connection to each physical PLC.
- Each PLC manages exactly four Stations:
  - PLC-01 – `192.168.10.101`: A1, A2, A3, A4.
  - PLC-02 – `192.168.10.111`: B1, B2, D1, D2.
  - PLC-03 – `192.168.10.121`: X1, X2, X3, X6.
  - PLC-04 – `192.168.10.131`: E1, E2, G3, G7.
- Each Station has an independent instance of the approved common 23-signal IO template. The same reusable signal definition and processing contract is used for all Stations.
- Each Station must have its own register allocation within its owning PLC. Device addresses may repeat across different physical PLCs, but allocations for independent Stations on the same PLC must not overlap where that would cause data collision.
- A signal is uniquely identified by `PLC_ID + Station_ID + Signal_Name`.
- The official working interface workbook is `docs/02_Interface_Specification/PLC_IO_Map/ETEK_WCS_IO_Map_4PLC_16Stations_v2_Separate_IO_Sets.xlsx`.
- **Update 2026-11-26 (CR-045, owner decision):** the workbook above is retired and removed. The only IO map is `docs/02_Interface_Specification/PLC_IO_Map/AGV_PLC Interface_List_22_11_2026.xlsx`, not yet confirmed by the PLC engineer. No other business rule changes.
- Actual per-Station PLC device addresses, protocol settings and handshake/reset details remain unconfirmed until approved by the PLC engineer. The workbook's `Template_Address_Reference` is not an actual per-Station address allocation.
- Do not infer register offsets or change the 23 source signal definitions without an approved change.

The four-PLC topology does not alter the protected business rules: `PER_AGV`, BP-04 `REVIEW`, WCS inventory ownership, A3/A4 and D1/D2 `UNCHANGED`, and the “Không bind rack” constraints remain in force.

---

## 1.9. Addendum – Transport Task, Leg and Station Transaction (CR-010, 2026-11-18)

This addendum adds concepts; it changes none of BR-01 .. BR-04.

- A **Transport Task** is the whole transport job of one Business Process. It may contain several **Legs** and several **Station Transactions** (also across PLCs). It completes only when its process reaches its completion step.
- A **Station Transaction** is one entry of an AMR into one port to pick or deliver a rack. A port has at most one active Station Transaction; ports are independent (also ports of one PLC). It ends after the handover and after the AMR left the port. Ending it does not complete the Transport Task.
- A **Buffer** is a physical storage location: a confirmed drop there updates inventory. An **intermediate waiting position** is where an AMR waits while it still holds the rack; it is not a storage location and inventory is not updated for it.
- BR-01 (`PER_AGV`) is applied to the AGV-holding leg: at most one leg per AGV holds the AGV. There is no system-wide task limit and no PLC-wide lock. The statement "only one task at a time" of the source slide 4 of `Process in detail` is not a system-wide limit (BR-01 prevails).
- Routes, priorities, buffers, waiting and branches are configuration (`ADR-0008`); BP-04 stays `REVIEW`; A3/A4 and D1/D2 stay `UNCHANGED`; WCS keeps owning inventory and never overwrites it from PLC data.
