# CR-008B-5 – Triển khai AGV Dispatch Hold

**Ngày:** 2026-11-17 · **Branch:** `develop` · **Commit nền:** `108534d` · **Commit mới:** xem `git log` (một commit riêng `feat(wcs): implement AGV dispatch hold for uncertain outcomes`) · **Chưa merge, chưa push, `main` = `d5d692f`.**

Quyết định chủ dự án áp dụng: **D-5 (M2)**, **D-6**, **D-7 (P2)**, **D-8**. D-9 và REV-005 vẫn như cũ (xem mục 11).

## 1. Mapping outcome thực tế (D-5 / D-6)

| Outcome | Vị trí trong code | Rack Unknown + Location cần đối soát | DispatchHold | Test |
|---|---|---|---|---|
| RCS xác nhận `Cancelled` | `FinishAsync(Cancelled)` | Không (đường cũ) | **Không** | `A_completion_with_a_confirmed_drop_and_an_rcs_confirmed_cancel_*` |
| `Completed` + Drop đã xác nhận | `HandleSignal DropConfirmed` | Không | **Không** | cùng test |
| `Completed` khi CancelPending, chưa có Drop | `EndUncertainAsync` | Có | Có | `Completed_while_cancel_pending_*` |
| `Interrupted` (tín hiệu và khi hủy) | `RcsInterrupted`, `EndUncertainAsync` | Có | Có | `Interrupted_marks_rack_unknown_*`, `Failed_task_frees_the_agv_*` (đã cập nhật) |
| `NotFound` sau khi RCS đã nhận | `ReconcileCancelPendingAsync` | Có | Có | `Rcs_forgetting_an_accepted_task_holds_the_agv` |
| CancelPending hết 30 lần (D-1) | `ReconcileCancelPendingAsync` | **Có (mới)** | Có | `The_cancel_retry_limit_keeps_the_reservation_*` |
| `RCS_DISPATCH_UNCERTAIN` | `DispatchToRcsAsync` | **Có (mới)** | Có | `A_dispatch_whose_outcome_stays_unknown_*` |
| `RcsEnded` khi chưa có Drop | `HandleSignal RcsEnded` | **Có (mới)** | Có | `Rcs_ended_without_a_drop_*` |

Điều kiện phân loại **không** dựa vào `RequiresReconciliation`: hold chỉ được đặt khi `FinishAsync(physicalUnproven: true)` **và** `DropConfirmedAt == null` (hoặc nhánh `RcsEnded` khi `DropConfirmedAt == null`). Reservation không bị giải phóng ở bất kỳ nhánh nào; `Rack.CurrentLocationId` không bị đổi bởi việc đánh dấu.

**Khoảng trống còn lại (không tự suy luận):** `EnforceTimeoutAsync` khi task ở `CancelPending` (chỉ chạy nếu cấu hình `StateTimeoutsSeconds`, mặc định tắt) vẫn dùng đường cũ: không đánh dấu và không hold, vì không nằm trong danh sách D-6.

## 2. Thiết kế DispatchHold (P2)
- Bảng `agvs` thêm: `DispatchHold` (bool, default false), `DispatchHoldReason` (≤200), `DispatchHoldAt`, `DispatchHoldTaskId` (con trỏ audit, không FK). Độc lập với `Agv.Enabled`.
- `Agv.PlaceDispatchHold` idempotent (giữ lý do/task đầu tiên); `ReleaseDispatchHold` xóa cả bốn trường.
- Hold được đặt **trong cùng giao dịch** với chuyển trạng thái kết thúc (`FinishAsync`), và task ghi history `AGV_DISPATCH_HOLD`.

## 3. Ngăn dispatch AGV đang bị giữ và race condition
- `ProcessTaskAsync`: AGV bị giữ ⇒ task vào `Queued` (như AGV bận); không chuyển sang AGV khác (`AgvId` không đổi).
- `TryDispatchNextAsync`: trả về ngay nếu `DispatchHold`.
- `DispatchToRcsAsync` (phase 1): kiểm tra lần cuối trước khi gọi RCS (phòng thủ).
- **Race:** hold và trạng thái kết thúc cùng commit. Mọi giao dịch dispatch chỉ chọn task kế tiếp sau khi task hiện tại kết thúc, nên thấy hoặc (task còn active) hoặc (task kết thúc **và** hold). Chỉ mục duy nhất một task active/AGV vẫn là lớp bảo vệ ở DB. Test song song: `The_hold_is_set_in_the_same_transaction_*` (signal Interrupted chạy đồng thời với 8 `TryDispatchNext` + 8 tick scheduler) – đạt. Đây là test dưới tải mô phỏng, **không** chứng minh toàn bộ mọi lịch trình thực thi.
- `RcsEnded`: task vẫn active (PER_AGV nguyên vẹn); hold đặt ngay. Một PLC `DropConfirmed` đến muộn vẫn được nhận: `ConfirmDropAsync` nay chấp nhận rack `Unknown` nếu rack còn thuộc task đó (**thay đổi nhỏ trong `InventoryService`, cần reviewer xác nhận**; nếu không, drop hợp lệ sau `RcsEnded` sẽ bị từ chối `RACK_NOT_IN_TRANSIT`).

## 4. API, quyền, audit (D-8)
- `GET /api/agvs`, `GET /api/agvs/{id}`: thêm `dispatchHold`, `dispatchHoldReason`, `dispatchHoldAt`, `dispatchHoldTaskId` (Admin và Operator đều xem).
- `POST /api/agvs/{id}/release-hold` `{inventoryReconciled, agvPhysicallyVerified, note}` – **Admin only** (backend `RequireAuthorization(admin)`; Operator ⇒ 403). Thiếu một trong hai xác nhận ⇒ từ chối `HOLD_RELEASE_CONFIRMATION_REQUIRED`. Từ chối nếu AGV không bị giữ, hoặc task gây hold vẫn còn active.
- Audit `AGV_DISPATCH_HOLD_RELEASE`: người thực hiện, thời điểm, AGV, task liên quan, lý do hold cũ, thời điểm bắt đầu giữ, hai xác nhận, ghi chú. Ngoài ra task có history `AGV_DISPATCH_HOLD_RELEASED`.
- Hoàn tất đối soát tồn kho **không** gỡ hold (`Finishing_the_inventory_reconciliation_alone_*`). Sau khi gỡ, `TryDispatchNextAsync` được gọi best-effort; thất bại không biến release thành lỗi, scheduler sẽ làm ở tick sau.

## 5. Frontend
`AgvHoldPanel` (banner "Dispatch Hold", lý do, thời điểm, link task) hiện trên thẻ AGV (cả dạng compact); form gỡ giữ (2 checkbox + ghi chú bắt buộc) chỉ hiển thị cho Admin; Operator thấy thông báo "chỉ có quyền xem". Pill trạng thái AGV hiển thị "Dispatch Hold". Chuỗi tiếng Việt cho lỗi/sự kiện/lý do mới. Chưa mở trình duyệt thật.

## 6. Migration
`20261117035102_AddAgvDispatchHold` (additive; không sửa/xóa migration cũ; **không chạy trên `wcs_db`**). SQL sinh ra:
```sql
ALTER TABLE agvs ADD "DispatchHold" boolean NOT NULL DEFAULT FALSE;
ALTER TABLE agvs ADD "DispatchHoldAt" timestamp with time zone;
ALTER TABLE agvs ADD "DispatchHoldReason" character varying(200);
ALTER TABLE agvs ADD "DispatchHoldTaskId" uuid;
```
`has-pending-model-changes`: không còn thay đổi chưa có migration.

## 7. Kết quả kiểm thử (DB tạm `wcs_test_*`, không dùng `wcs_db`)
| Nhóm | Kết quả |
|---|---|
| `Cr008b5AgvDispatchHoldTests` (mới) | 15/15 đạt (các mục 1–23 của yêu cầu: hold, AGV còn lại, queue, không đổi AGV, race, restart, từng outcome, giữ CurrentLocationId, không cấp phát vị trí cần đối soát, Operator 403, thiếu xác nhận, đủ xác nhận, audit, dispatch sau gỡ) |
| Toàn bộ backend | **506/506 đạt** (trước: 491; +15) |
| Frontend `eslint` | 0 lỗi |
| `tsc -b` | 0 lỗi |
| `npm run build` | thành công |

Test cũ được cập nhật vì quyết định mới (không phải sửa cho khớp): `Failed_task_frees_the_agv_so_the_next_queued_task_starts` (nay: AGV bị giữ, task kế tiếp vẫn `Queued`), hai test B-3 và một test B-4 về hết giới hạn hủy (nay rack `Unknown`, vị trí bị đánh dấu). Hồi quy B-1…B-4, PER_AGV, AGV1/AGV2 song song, PLC: toàn bộ test hiện có còn lại đạt.

## 8. Không thay đổi (đã kiểm chứng bằng diff)
`TaskStateMachine`, `TaskScheduler`, `FlowOptions`, mã PLC/MC/handshake/địa chỉ/`Request_Ack`, A3/A4, D1/D2, F5, retry mặc định, `StateTimeoutsSeconds`, `HostedServices.cs`, `appsettings.json`. Không có lệnh PLC/RCS mới. Quy tắc cấp phát vị trí/rack không đổi (chỉ đánh dấu thêm trường hợp).

## 9. Chưa kiểm chứng trên thiết bị thật
Docker: **chưa**. PLC thật, RCS thật, AGV thật: **chưa**. Giao diện chưa được mở trên trình duyệt. Kết luận ở mục này chỉ là "đã triển khai và kiểm thử bằng code trên DB PostgreSQL tạm với RcsSimulator".

## 10. Rủi ro còn lại
- Hold chặn AGV đến khi Admin xác nhận: nếu không ai gỡ, hàng đợi của AGV đó đứng yên (đúng ý D-7, cần quy trình vận hành/ cảnh báo).
- Vị trí bị đánh dấu chặn cả trạm yêu cầu (A1/A2) tới khi đối soát.
- `RcsEnded` đánh dấu rack `Unknown` khi task còn chạy; drop đến muộn được nhận (xem mục 3) – cần xác nhận của reviewer.
- Test race không bao quát hết mọi lịch trình; dựa vào lập luận cùng-giao-dịch + chỉ mục duy nhất.
- `AgvStatusService` cập nhật cùng hàng `agvs` (xmin): xung đột được `EfUnitOfWork` thử lại, không kiểm thử riêng.
- Timeout `CancelPending` (cấu hình tùy chọn) chưa đánh dấu/hold (mục 1).

## 11. Vẫn mở
D-9 (hai lần NotFound ≠ Cancelled): không đổi. **REV-005 NOT VERIFIED – RCS VENDOR CONFIRMATION REQUIRED**: không coi gửi lại cùng `RcsRequestCode` là an toàn; không tuyên bố sẵn sàng RCS thật. Chưa tự kết luận CR-009 đủ điều kiện: chờ review độc lập.
