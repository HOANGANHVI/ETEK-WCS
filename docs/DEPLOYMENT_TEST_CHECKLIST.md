# CR-006 – Checklist nghiệm thu triển khai trên Windows 11 (Docker Desktop)

Điền kết quả thật khi chạy trên máy triển khai. **Mục chưa chạy phải để trống / "CHƯA CHẠY" – không được ghi PASS.**
Trạng thái tại thời điểm viết CR-006: tất cả mục dưới đây **CHƯA CHẠY** (máy lập trình không có Docker).

| # | Hạng mục | Lệnh / cách kiểm tra | Kỳ vọng | Kết quả |
|---|---|---|---|---|
| A1 | Compose hợp lệ | `docker compose --env-file C:\ETEK-WCS\config\.env -f deployment\docker-compose.yml config -q` | không lỗi | |
| A2 | Build backend | `deploy.ps1` (bước build) | image `etek-wcs-backend`, `etek-wcs-migrator` được tạo | |
| A3 | Build frontend | như trên | image `etek-wcs-frontend` | |
| B1 | PostgreSQL healthy | `status.ps1` | postgres `healthy` | |
| B2 | Migrator | `status.ps1` | `migrator` exited, exit=0 | |
| B3 | Backend healthy | `status.ps1` | backend `healthy`, `/healthz` = `{"application":"Up","database":"Up"}` | |
| B4 | Frontend | trình duyệt `http://localhost` | trang đăng nhập; refresh `/plc` không 404 | |
| B5 | API qua Nginx | đăng nhập Admin; mở Tasks/Inventory | dữ liệu tải được | |
| B6 | SignalR qua Nginx | DevTools → Network → WS `/hubs/wcs` (101); chân trang "Thời gian thực: Đã kết nối" | WebSocket mở | |
| B7 | PostgreSQL không lộ ra LAN | từ máy khác `Test-NetConnection <ip> -Port 5432` | thất bại | |
| B8 | Backend không lộ ra LAN | `Test-NetConnection <ip> -Port 8080` | thất bại | |
| C1 | Restart container | `docker restart etek-wcs-backend-1`; tạo nhiệm vụ trước đó vẫn còn | dữ liệu còn | |
| C2 | Restart Docker/Windows | khởi động lại Docker Desktop (hoặc máy) | hệ thống tự lên; dữ liệu còn | |
| C3 | stop/start | `stop.ps1` rồi `start.ps1` | dữ liệu còn | |
| C4 | Backup | `backup.ps1` | file `.dump` + `.tgz` trong `C:\ETEK-WCS\backups` | |
| C5 | Restore | sửa 1 dữ liệu, `restore.ps1 -BackupFile ...` | dữ liệu về lúc backup; mật khẩu PLC/RCS vẫn giải mã | |
| C6 | Key ring | tạo lại container backend (`docker compose up -d --force-recreate backend`) | cấu hình RCS/PLC đã lưu vẫn đọc được | |
| D1 | Clone main | `git clone` + `git checkout main` trên máy sạch | OK | |
| D2 | Deploy lần đầu | `deploy.ps1` | hoàn tất, health OK | |
| D3 | Update | merge 1 thay đổi nhỏ vào main; `deploy.ps1` | tự backup `pre-<commit>`, build, migrate, healthy | |
| D4 | `.env` không bị ghi đè | so sánh `C:\ETEK-WCS\config\.env` trước/sau | không đổi | |
| D5 | Chặn thay đổi local | sửa 1 file trong repo rồi `deploy.ps1` | dừng với lỗi, không đổi gì | |
| D6 | Chặn nhánh sai | `git checkout develop`; `deploy.ps1` | dừng: chỉ triển khai từ main | |
| D7 | Rollback | theo README mục 6 | về đúng phiên bản trước | |
| E1 | Không dữ liệu Simulator | mở Giám sát PLC (Operator) | "Chưa kết nối", không giá trị giả, banner RCS/PLC | |
| E2 | Production từ chối Simulator | `.env`: `PLC_MODE=Simulator` (không allow) → `start.ps1` | backend không khởi động (log: "Production refuses…") | |
| E3 | Không ghi PLC | `PLC_MODE=RealReadOnly`; thử test-write | 409 `NOT_AVAILABLE_IN_PRODUCTION` | |
| E4 | Không polling IO chưa duyệt | PLC đã kết nối, IO chưa phê duyệt | Pollable=Không; không có đọc | |
| E5 | Phân quyền | Operator: `/plc/connections` bị chặn; API cấu hình 403; Admin đầy đủ | đúng | |
| F1 | Route container → PLC | `plc-route-test.ps1 -Port <cổng đã xác nhận>` (chỉ khi có PLC + cổng) | TCP connect OK | **không giả lập kết quả** |
| F2 | Giao tiếp PLC thật (đọc) | kỹ sư PLC commissioning chỉ-đọc | – | **REAL PLC TESTED = NO** cho tới khi làm |
| G1 | Chỉ tin Nginx cho X-Forwarded-* | từ máy khác gọi API có header `X-Forwarded-For: 1.2.3.4`; xem Nhật ký thao tác | IP ghi là IP máy gọi (do Nginx ghi đè), không phải 1.2.3.4 | |
| G2 | Backend không truy cập trực tiếp | `docker compose ... ps`: backend không có cổng publish; `Test-NetConnection <ip> -Port 8080` thất bại | không có cổng | |
| G3 | Bảo vệ key ring | máy triển khai có BitLocker/khóa màn hình; `wcs-keys-*.tgz` và `.env` được lưu hạn chế quyền | đã làm | |
| G4 | HTTPS vận hành | (khi có TLS) `WCS_COOKIE_SECURE=Always` và đăng nhập qua https | OK | |
| H1 | Log script | chạy `deploy.ps1`; xem `C:\ETEK-WCS\logs` | có deploy-*.log và deployment-events-*.jsonl, có bước/kết quả/exit code, không có mật khẩu | |
| H2 | Nhập sự kiện triển khai | sau H1 mở `/logs` → nhóm *Triển khai* (chờ ≤ 1 phút) | thấy các sự kiện của lần chạy | |
| H3 | Lưu log khi lỗi | làm hỏng cố ý một bước (vd sai POSTGRES_PASSWORD rồi `start.ps1`) | có failure-<run>-*.log (migrator/backend) và sự kiện FAIL trên màn hình | |
| H4 | Thư mục log chỉ-đọc | `docker exec etek-wcs-backend-1 sh -c "touch /deploy-logs/x"` | bị từ chối (read-only) | |
| H5 | Log PLC trong Docker | `docker compose -p etek-wcs logs backend` khi bật/tắt kết nối PLC thử | thấy dòng `PLC PLC-0x STATE` kèm IP/cổng và trạng thái trước→sau | |
| H6 | Database mất kết nối | `docker stop etek-wcs-postgres-1` 2 phút rồi `start` | 1 dòng DB_LOST, throttle, DB_RESTORED trên màn hình sau khi khôi phục | |
