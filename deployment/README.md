# ETEK WCS – Triển khai bằng Docker trên Windows 11

Tài liệu này hướng dẫn cài ETEK WCS trên **một máy Windows 11** bằng Docker Desktop + WSL2 + Docker Compose, lấy mã nguồn từ GitHub (nhánh `main`).

> **Trạng thái kiểm thử (CR-006):** Dockerfile, Compose và script đã được kiểm tra tĩnh và logic ứng dụng đã được kiểm thử, nhưng **chưa được chạy trên Docker thật** (máy lập trình không có Docker). Hãy chạy checklist `docs/DEPLOYMENT_TEST_CHECKLIST.md` trên máy triển khai trước khi dùng. **Chưa kiểm thử với PLC thật. RCS chỉ có adapter mô phỏng.**

## 1. Kiến trúc

```
Trình duyệt ──:80──► frontend (Nginx: giao diện + proxy /api, /hubs WebSocket)
                              │  edge_net
                              ▼
                         backend (ASP.NET Core .NET 10, 8080 – không publish) ──TCP──► 4 PLC trong LAN
                              │  db_net (internal, không có đường ra LAN)
                              ▼
   migrator (chạy 1 lần, áp migration)      postgres (PostgreSQL 16 – không publish)
```

| Container | Vai trò | Cổng ra ngoài |
|---|---|---|
| `frontend` | Nginx: phục vụ bản build React, reverse proxy API và SignalR | `WCS_HTTP_PORT` (mặc định 80) – **cổng duy nhất** |
| `backend` | API, SignalR, scheduler, PLC runtime | không |
| `postgres` | PostgreSQL 16, volume `etek_wcs_pgdata` | không |
| `migrator` | EF Core migration bundle, chạy một lần trước backend | không |

Volume (không bao giờ bị script xóa): `etek_wcs_pgdata` (database), `etek_wcs_keys` (khóa mã hóa mật khẩu/token PLC-RCS).
**Không có MQTT** (source không dùng). **Không có Redis/khác.** Chỉ chạy **một** backend (scheduler và PLC worker nằm trong process).

## 2. Chuẩn bị Windows 11

1. Bật ảo hóa trong BIOS (Intel VT-x / AMD-V).
2. Cài WSL2 (PowerShell **Administrator**): `wsl --install` rồi khởi động lại máy.
3. Cài **Docker Desktop** (https://www.docker.com/products/docker-desktop), chọn *Use WSL 2 based engine*. Bật *Start Docker Desktop when you sign in*.
4. Cài **Git for Windows**.
5. Khởi điểm tài nguyên (Docker Desktop → Settings → Resources, hoặc `%UserProfile%\.wslconfig`):
   ```
   [wsl2]
   processors=6
   memory=8GB
   swap=2GB
   ```
   Disk image của Docker dự kiến 100 GB (Settings → Resources → Disk image size). Có thể chỉnh sau; giới hạn từng container nằm trong `.env`.
6. Mở cổng trên Windows Firewall nếu máy khác trong LAN cần truy cập (ví dụ TCP 80).

## 3. Cài đặt lần đầu

```powershell
# 1) Lấy mã nguồn (nhánh main)
git clone https://github.com/HOANGANHVI/ETEK-WCS.git C:\ETEK-WCS\src
cd C:\ETEK-WCS\src
git checkout main

# 2) Tạo cấu hình riêng của máy (NGOÀI repository, không bị git pull ghi đè)
mkdir C:\ETEK-WCS\config
copy deployment\.env.example C:\ETEK-WCS\config\.env
notepad C:\ETEK-WCS\config\.env
```

Trong `.env` **bắt buộc** đặt: `POSTGRES_PASSWORD` (chuỗi ngẫu nhiên dài) và `WCS_ADMIN_BOOTSTRAP_PASSWORD` (mật khẩu admin đầu tiên, sẽ buộc đổi ở lần đăng nhập đầu). Giải thích các biến ở mục 4.

```powershell
# 3) Cài đặt và khởi động (lần đầu build image mất vài phút)
powershell -ExecutionPolicy Bypass -File deployment\scripts\deploy.ps1

# 4) Kiểm tra
powershell -ExecutionPolicy Bypass -File deployment\scripts\status.ps1
```
Mở `http://localhost` (hoặc `http://<IP máy>`), đăng nhập `admin` + mật khẩu bootstrap, đổi mật khẩu.

> **Khuyến nghị cho vận hành chính thức: HTTPS + `WCS_COOKIE_SECURE=Always`.** Bản này chưa tích hợp chứng chỉ TLS (cần CR riêng hoặc một bộ chấm dứt TLS phía trước Nginx).
> **HTTP + `WCS_COOKIE_SECURE=SameAsRequest` chỉ dành cho LAN thử nghiệm đáng tin cậy**: cookie phiên đi qua mạng không mã hóa, ai nghe được mạng có thể chiếm phiên. Nếu dùng HTTP thì phải đặt `SameAsRequest`, nếu không trình duyệt không gửi cookie đăng nhập. Không dùng `SameAsRequest` cho vận hành hoặc qua Internet. Backend ghi cảnh báo trong log khi Production chạy với `SameAsRequest`.

## 3a. Máy kiểm thử PLC (CR-045)

Làm theo thứ tự. **Chưa bước nào được kiểm chứng trên Docker thật hoặc PLC thật.**

**Bước A – Chạy thử với bộ mô phỏng** (không kết nối thiết bị). Ngoài các giá trị bắt buộc, đặt trong `.env`:

```
PLC_MODE=Simulator
PLC_ALLOW_SIMULATOR_IN_PRODUCTION=true
PLC_ALLOW_LIVE_CONNECTION=false
PLC_WRITE_ENABLED=false
PLC_WRITE_ALLOWLIST=
RCS_ALLOW_SIMULATOR_IN_PRODUCTION=true
RCS_REAL_DISPATCH_ENABLED=false
WCS_COOKIE_SECURE=SameAsRequest
```

(`SameAsRequest` chỉ khi dùng HTTP trong LAN kiểm thử.) Danh sách AMR để trống và báo mất kết nối RCS là bình thường khi chưa khai báo RCS.

Chạy `deploy.ps1`, rồi `status.ps1`; đăng nhập, đổi mật khẩu; chạy `docs/DEPLOYMENT_TEST_CHECKLIST.md`.

**Bước B – Kết nối PLC chỉ đọc** (chỉ khi kỹ sư PLC đồng ý): `PLC_MODE=RealReadOnly`, `PLC_ALLOW_SIMULATOR_IN_PRODUCTION=false`, `PLC_ALLOW_LIVE_CONNECTION=true`; giữ `PLC_WRITE_ENABLED=false` và danh sách rỗng. Xem mục 8.

**Bước C – Ghi thử:** chưa được phép. Cần đủ điều kiện ở báo cáo Phase 2 §8.

Bản này có các migration chỉ-tiến (gồm CR-041 và CR-042); `deploy.ps1` tự sao lưu rồi áp dụng. Không chạy trên database vận hành khi chưa được phê duyệt.

**Nâng cấp từ bản dùng catalog PLC cũ (CR-046):** địa chỉ đã nhập cho 23 tín hiệu cũ và trạm X6, G3, G7 được giữ trong database nhưng không còn được đọc/ghi. Đọc `docs/change-management/CR-046_IO_MAP_CATALOG_UI_F1_F2_DOCKER.md` mục 1 trước khi nâng cấp.

**Ở bước A:** trang *Giám sát PLC* hiện 96 điểm IO ở trạng thái "chưa thiết lập" cho tới khi Admin nhập địa chỉ; giá trị khi đó là dữ liệu mô phỏng. API bơm giá trị giả vào IO (`/api/plc/simulator/*`) bị khóa trong Production; mô phỏng yêu cầu dùng trang *Mô phỏng PLC*.

**Kiểm tra tự động** (máy kiểm thử có Docker và `jq`, không đụng bản cài đặt khác): `bash tests/deployment/compose-smoke.sh`.

## 4. Cấu hình `.env`

| Biến | Ý nghĩa | Mặc định |
|---|---|---|
| `POSTGRES_DB`, `POSTGRES_USER` | Tên database / user | `wcs_db`, `wcs` |
| `POSTGRES_PASSWORD` | **Bắt buộc.** Chỉ có hiệu lực khi tạo database lần đầu | – |
| `WCS_ADMIN_USERNAME`, `WCS_ADMIN_BOOTSTRAP_PASSWORD` | Admin đầu tiên (Production không tạo `admin/admin`) | `admin`, – |
| `WCS_HTTP_PORT` | Cổng Nginx | `80` |
| `WCS_COOKIE_SECURE` | `Always` (HTTPS, **khuyến nghị vận hành**) hoặc `SameAsRequest` (chỉ HTTP LAN thử nghiệm) | `Always` |
| `WCS_EDGE_SUBNET`, `WCS_NGINX_IP` | Mạng nội bộ Docker và địa chỉ cố định của Nginx; backend chỉ tin `X-Forwarded-*` từ đúng địa chỉ này | `172.30.0.0/24`, `172.30.0.10` |
| `PLC_MODE` | `RealReadWrite` (mặc định, chế độ hỗ trợ ghi) hoặc `RealReadOnly`. Thiếu `PLC_ALLOW_LIVE_CONNECTION` hoặc `PLC_WRITE_ENABLED` thì runtime **vẫn chỉ đọc** (hiển thị trên trang PLC) | `RealReadWrite` |
| `PLC_ALLOW_LIVE_CONNECTION` | `false`: **không mở socket tới PLC thật** | `false` |
| `PLC_WRITE_ENABLED` | Khóa ghi (CR-015/CR-019): `true` để cho phép ghi | `false` |
| `PLC_WRITE_ALLOWLIST` (CR-044, CR-045) | **Bắt buộc để ghi thật.** Danh sách điểm được ghi, một dòng, ngăn bằng `;`: `PlcId\|StationId\|Signal\|Address` (tín hiệu STRING thêm `\|Words`). Rỗng = không ghi gì. Một mục sai thì **cả danh sách không có hiệu lực**. Thanh ghi dành riêng của IO Map chưa xác nhận bị chặn dù đã khai báo | rỗng |
| *(đã bỏ, CR-019)* `PLC_WRITE_APPROVAL_REFERENCE`, `PLC_WRITE_HANDSHAKE_APPROVED` | Không còn được đọc. Dòng cũ trong `.env` được bỏ qua, không gây lỗi; có thể xóa. Phê duyệt duy nhất là **Duyệt ghi theo từng IO** của Admin | — |
| `PLC_ALLOW_SIMULATOR_IN_PRODUCTION` | Production từ chối `Plc:Mode=Simulator` trừ khi `true` (chỉ để khởi động có giám sát khi chưa có PLC) | `false` |
| `WCS_ALLOW_NETWORK_PROBE` | Cho phép phép thử TCP của "Kiểm tra kết nối" (chỉ mở/đóng cổng) | `false` |
| `WCS_LOG_LEVEL`, `WCS_TIMEZONE`, `WCS_VERSION` | Log, múi giờ, nhãn image | `Information`, `Asia/Ho_Chi_Minh`, commit |
| `WCS_*_CPUS`, `WCS_*_MEMORY` | Giới hạn tài nguyên container | DB 2 CPU/2g, backend 3 CPU/3g |

Thư mục cấu hình và backup có thể đổi bằng biến môi trường của Windows `WCS_CONFIG_DIR` (mặc định `C:\ETEK-WCS\config`) và `WCS_BACKUP_DIR` (mặc định `C:\ETEK-WCS\backups`).

**Địa chỉ PLC, cổng, tham số MC và địa chỉ IO không nằm trong `.env`.** Chúng được Admin nhập trong giao diện (*Kết nối PLC*, *Cấu hình địa chỉ IO*) sau khi kỹ sư PLC xác nhận, và lưu trong database nên được giữ qua mọi lần cập nhật.

## 5. Vận hành hằng ngày

| Việc | Lệnh (thư mục repo) |
|---|---|
| Khởi động | `deployment\scripts\start.ps1` |
| Dừng an toàn (không mất dữ liệu) | `deployment\scripts\stop.ps1` (`-Remove` để xóa thêm container, vẫn giữ volume) |
| Trạng thái | `deployment\scripts\status.ps1` |
| Backup | `deployment\scripts\backup.ps1` |
| Cập nhật phiên bản | `deployment\scripts\deploy.ps1` |

Container có `restart: unless-stopped`: sau khi Windows/Docker khởi động lại, hệ thống tự chạy lại (Docker Desktop phải tự khởi động cùng Windows).
**Tuyệt đối không chạy `docker compose down -v` hoặc `docker volume rm etek_wcs_pgdata`**: đó là xóa database.

## 6. Cập nhật từ GitHub

Quy trình: phát triển trên `develop` → kiểm thử → merge vào `main` → trên máy triển khai chạy:

```powershell
cd C:\ETEK-WCS\src
powershell -ExecutionPolicy Bypass -File deployment\scripts\deploy.ps1
```
`deploy.ps1` lần lượt: kiểm tra Docker/Git/`.env` → yêu cầu đang ở nhánh `main` → **dừng nếu có thay đổi local hoặc lịch sử lệch** → `git pull --ff-only` → **backup tự động** → build image → áp migration → khởi động → kiểm tra health. Nó không bao giờ triển khai từ `develop`, không xóa volume, và không ghi đè `.env` (nằm ngoài repo). Mỗi lần triển khai được ghi vào `C:\ETEK-WCS\config\deployments.log` (commit trước/sau).

### Rollback
1. Xem commit trước: `type C:\ETEK-WCS\config\deployments.log`.
2. `git checkout <commit-trước>` rồi `deploy.ps1 -NoPull` (tự backup trước khi chạy).
3. Migration chỉ đi **tiến**. Nếu bản mới đã thêm migration và cần quay lại hoàn toàn: sau khi checkout commit cũ, khôi phục database từ backup trước đó (`restore.ps1 -BackupFile ...\wcs-db-...-pre-<commit>.dump`). Backup `pre-<commit>` được tạo tự động ngay trước mỗi lần update.
4. Sau rollback chạy lại `git checkout main` khi sẵn sàng đi tiếp.

## 7. Backup và Restore

`backup.ps1` tạo (ngoài repo, trong `C:\ETEK-WCS\backups`): `wcs-db-<thời gian>-<nhãn>.dump` (pg_dump định dạng custom, đã kiểm tra bằng `pg_restore --list`) và `wcs-keys-<thời gian>-<nhãn>.tgz` (khóa mã hóa). Giữ 14 bản gần nhất. **File `.env` không nằm trong backup** (chứa mật khẩu): hãy cất một bản riêng, an toàn. Nên chép thư mục backup sang ổ/ máy khác định kỳ.

```powershell
deployment\scripts\restore.ps1 -BackupFile C:\ETEK-WCS\backups\wcs-db-20261107-100000-manual.dump
# kèm khóa nếu khôi phục sang máy mới hoặc mật khẩu PLC/RCS không giải mã được:
deployment\scripts\restore.ps1 -BackupFile ...dump -KeysFile ...\wcs-keys-20261107-100000-manual.tgz
```
`restore.ps1` hiển thị cảnh báo, yêu cầu gõ `RESTORE`, tự tạo backup an toàn `pre-restore`, kiểm tra file, thay database, áp migration mới (nếu có) rồi khởi động lại. **Sau restore:** đăng nhập Admin; kiểm tra nhiệm vụ, tồn kho, *Kết nối PLC*, *Cấu hình địa chỉ IO*; chạy `status.ps1`.

## 8. Kết nối PLC (chỉ làm sau khi kỹ sư PLC xác nhận)

Địa chỉ PLC hiện được cấp: PLC-01 `192.168.10.101`, PLC-02 `.111`, PLC-03 `.121`, PLC-04 `.131`; IP đề xuất của máy triển khai `192.168.10.50` (**chưa được xác nhận**). Backend dùng IP LAN thật (không `localhost`); lưu lượng ra LAN đi qua NAT của Docker Desktop nên **PLC thấy địa chỉ IP của máy Windows**.

Trình tự an toàn (mỗi bước cần kỹ sư PLC):
1. Admin nhập tham số kết nối (cổng, giao thức, Network/PC/Module I/O/Station, timeout…) trong giao diện.
2. Kiểm tra đường mạng từ container (chỉ mở/đóng TCP, **không gửi byte nào**):
   `deployment\scripts\plc-route-test.ps1 -Port <cổng đã xác nhận>` — tham số `-Port` bắt buộc, dự án không giả định cổng.
3. Đặt `PLC_ALLOW_LIVE_CONNECTION=true`, chạy lại `deploy.ps1`/`start.ps1`; Admin bật kết nối từng PLC; **bắt đầu chỉ đọc** (`PLC_MODE=RealReadOnly`).
4. Việc đọc chỉ cần địa chỉ IO hợp lệ (CR-012). **Ghi** (CR-015) chỉ xảy ra khi đủ: `PLC_MODE=RealReadWrite`, `PLC_ALLOW_LIVE_CONNECTION=true`, `PLC_WRITE_ENABLED=true`; IO có **duyệt quyền ghi** của Admin (*Cấu hình địa chỉ IO → Duyệt ghi*); điểm được **khai báo trong `PLC_WRITE_ALLOWLIST`** và địa chỉ ngoài thanh ghi dành riêng (CR-044); người ghi là Admin; giá trị đúng kiểu; không phải tín hiệu Safety/Permit. Mỗi lần ghi có hộp thoại xác nhận và Audit Log. Chỉ bật các khóa này khi thực sự có ý định ghi.

Nếu không tới được PLC: kiểm tra PLC bật và cùng mạng 192.168.10.x, đúng cổng, Windows Firewall, và danh sách IP được phép trên PLC (dùng IP máy Windows).

## 9. An toàn Production

- Backend **từ chối khởi động** nếu Production chạy `Plc:Mode=Simulator` (trừ khi `PLC_ALLOW_SIMULATOR_IN_PRODUCTION=true`, có cảnh báo trong log). Khi ngoại lệ bật, giao diện vẫn ghi rõ "Chưa kết nối PLC thật", không có handshake tự động.
- API mô phỏng (`/api/plc/simulator/*`) bị chặn ở Production; giao diện bản build không hiển thị công cụ mô phỏng. **Ghi PLC (`test-write`) không còn bị chặn theo môi trường (CR-015)**: nó được bảo vệ bởi quyền Admin, duyệt quyền ghi từng IO, kiểm tra giá trị, WritePolicy (đủ khóa ghi) và Audit Log.
- **RCS hiện chỉ có adapter mô phỏng**: nhiệm vụ **không** được gửi tới AGV/RCS thật; giao diện hiển thị thông báo này trên mọi màn hình.
- PostgreSQL không publish ra LAN; backend không publish; không dùng privileged hay host network; backend chạy bằng user không phải root.
- **Forwarded headers:** backend chỉ chấp nhận `X-Forwarded-For/Proto` từ địa chỉ Nginx được chỉ định (`Deployment:TrustedProxyNetworks`, mặc định `172.30.0.10/32`); từ nguồn khác header bị bỏ qua. Nginx ghi đè `X-Forwarded-For` bằng địa chỉ thật của client (không nối thêm giá trị client gửi). Bật tin cậy mà không có danh sách proxy, hoặc dùng `0.0.0.0/0`, thì backend từ chối khởi động. Backend không được publish ra LAN: **không bao giờ thêm `ports:` cho `backend`/`postgres` trong Compose.**
- **Khóa mã hóa (Data Protection key ring):** nằm trong volume `etek_wcs_keys` (`/keys`) và dùng để giải mã mật khẩu/token PLC–RCS đã lưu trong database. ASP.NET ghi cảnh báo "No XML encryptor configured": các file khóa **không được mã hóa thêm** khi lưu trên volume. Vì vậy: (1) bảo vệ máy triển khai (tài khoản Windows riêng, khóa màn hình, BitLocker cho ổ đĩa, giới hạn người có quyền Docker/Administrator); (2) coi `wcs-keys-*.tgz` và `.env` là **bí mật**: lưu ở nơi hạn chế quyền, không đưa vào Git/email; (3) luôn giữ backup khóa cùng bản backup database tương ứng, vì mất khóa = không giải mã được mật khẩu/token đã lưu (phải nhập lại). Cơ chế mã hóa hiện tại **chưa được thay đổi**; việc mã hóa key ring (chứng chỉ/DPAPI) cần đánh giá ảnh hưởng tới khả năng giải mã dữ liệu cũ trước khi làm.

## 10. Nhật ký và chẩn đoán (CR-007)

| Nơi | Nội dung | Cách xem |
|---|---|---|
| Màn hình **Logging & Diagnostics** (menu *Hệ thống*, chỉ Admin) | Lỗi khởi động/hệ thống, cơ sở dữ liệu, PLC (mất kết nối, timeout, MC End Code, lỗi đọc/ghi, WRITE_AMBIGUOUS), RCS/AGV, **sự kiện triển khai**, thao tác bảo mật/quản trị | Trình duyệt → `/logs`; lọc theo thời gian, nhóm, mức độ, thiết bị, nguồn, mã sự kiện, từ khóa; bấm một dòng để xem chi tiết (stack trace, IP/cổng PLC, trạng thái trước/sau) |
| stdout của container | Toàn bộ log ứng dụng (kể cả khi database lỗi) | `docker compose -p etek-wcs logs backend` (Docker giữ 5 × 10 MB mỗi container) |
| `C:\ETEK-WCS\logs\deploy-YYYYMMDD.log` | Mỗi lần chạy script: thời điểm, script, bước, kết quả, exit code, lỗi, output của `docker compose` | Mở bằng trình soạn thảo |
| `C:\ETEK-WCS\logs\deployment-events-YYYYMMDD.jsonl` | Cùng sự kiện ở dạng JSON, **backend tự nhập vào màn hình** (nhóm *Triển khai*) | Màn hình `/logs` |
| `C:\ETEK-WCS\logs\failure-<run>-*.log` | Khi một script thất bại: output lệnh cuối (kể cả migrator) và log container (`backend`, `postgres`, `frontend`, `migrator`) | Mở bằng trình soạn thảo |

- Thư mục log nằm **ngoài repository**, được gắn **chỉ-đọc** vào backend. Backend không mở endpoint nào để nhận log từ bên ngoài.
- **Không bao giờ ghi mật khẩu/token/giá trị `.env`**: giá trị bí mật trong `.env` và các mẫu password/token/cookie bị thay bằng `***`. Việc ghi log không bao giờ làm script thất bại.
- Log script **không tự xóa**. Dọn thủ công (mặc định chỉ liệt kê): `deployment\scripts\prune-logs.ps1 -OlderThanDays 90` rồi thêm `-Execute` để xóa (phải gõ `DELETE`).
- Bản ghi `system_log` trong database giữ 90 ngày (cấu hình `Logging:SystemLog:RetentionDays`, tối thiểu 7). Nhật ký PLC 30 ngày; nhật ký thao tác (audit) không tự xóa.
- Lỗi lặp lại (ví dụ database mất kết nối) chỉ được ghi 1 lần/phút kèm số lần bị bỏ qua; trạng thái mất/khôi phục database có sự kiện riêng (`DB_LOST`, `DB_RESTORED`).
- Kiểm tra nhanh các script ghi log mà không cần Docker: `powershell -ExecutionPolicy Bypass -File tests\deployment\Test-DeploymentLogging.ps1`.
- **Chưa kiểm chứng trên Docker thật:** việc gắn thư mục log chỉ-đọc vào container và nhập sự kiện trong container (checklist H1–H4 trong `docs/DEPLOYMENT_TEST_CHECKLIST.md`).

## 11. Xử lý sự cố

| Triệu chứng | Cách xử lý |
|---|---|
| `Docker engine is not running` | Mở Docker Desktop, chờ "Engine running". |
| `POSTGRES_PASSWORD is required` / `.env not found` | Tạo `C:\ETEK-WCS\config\.env` từ `.env.example`. |
| Đăng nhập được nhưng lập tức bị đăng xuất (HTTP) | Đặt `WCS_COOKIE_SECURE=SameAsRequest` (chỉ HTTP LAN thử nghiệm), `start.ps1`. |
| Backend `unhealthy` | `docker compose -p etek-wcs logs backend`; kiểm tra `PLC_MODE`/`PLC_ALLOW_SIMULATOR_IN_PRODUCTION` (xem mục 9) và `/healthz` trong `status.ps1`. |
| `migrator` exit khác 0 | `docker compose -p etek-wcs logs migrator`; backend không chạy cho tới khi sửa xong. Không xóa volume. |
| Cổng 80 bị chiếm | Đổi `WCS_HTTP_PORT` (vd 8080), `start.ps1`. |
| Quá chậm/thiếu RAM | Tăng `memory` trong `.wslconfig`, giảm/tăng `WCS_*_MEMORY`. |
| Mất kết nối giữa chừng sau khi Windows ngủ | `status.ps1`; nếu cần `stop.ps1` rồi `start.ps1`. |

Đã chạy `docker compose -p etek-wcs ...` thủ công thì luôn kèm `--env-file C:\ETEK-WCS\config\.env`.

## Giám sát tài nguyên máy chủ (trang Overview, chỉ Admin)

Backend tự đọc cgroup của chính container (CPU, RAM theo giới hạn `WCS_BACKEND_CPUS` / `WCS_BACKEND_MEMORY`, uptime, ổ chứa volume dữ liệu) và hiển thị trên Overview. **Không cần đổi Compose, không cần `privileged`, Docker socket hay mount ổ đĩa host.** Trên Docker Desktop, số đo "Máy chủ" là của VM Linux chạy container (được gắn nhãn rõ), không phải máy Windows; số đo Windows thật cần Host Agent (chưa cài, xem `docs/reviews/SERVER_RESOURCE_MONITORING_REPORT.md`). Cấu hình (tùy chọn, biến môi trường backend): `ResourceMonitoring__Enabled`, `ResourceMonitoring__SampleIntervalSeconds`, `ResourceMonitoring__CpuWarning/CpuCritical`, `...MemoryWarning/MemoryCritical`, `...DiskWarning/DiskCritical`, `ResourceMonitoring__DiskPath`.

## 10. Kiểm tra cấu hình ghi PLC thực sự vào backend (CR-017)

Trang *Cấu hình địa chỉ IO* hiển thị banner "Trạng thái ghi PLC" và liệt kê khóa còn thiếu theo **giá trị backend thực sự nhận được**. Nếu banner báo thiếu mà bạn tin là đã đặt:

1. Chạy `deployment\scripts\plc-write-config.ps1`. Script chỉ đọc: so sánh `.env` (đường dẫn được in ra) với biến môi trường **thực sự trong container backend**; hiển thị 3 khóa ghi (CR-019) và danh sách `PLC_WRITE_ALLOWLIST` (CR-045).
2. Nguyên nhân thường gặp: (a) `.env` được tạo từ `.env.example` cũ có `PLC_MODE=RealReadOnly` (ép chỉ đọc, khóa nào cũng vô hiệu); (b) thiếu `PLC_ALLOW_LIVE_CONNECTION` hoặc `PLC_WRITE_ENABLED`; (c) sửa `.env` nhưng chưa chạy lại `deploy.ps1`/`start.ps1` nên container backend chưa được tạo lại; (d) `.env` sai thư mục (`WCS_CONFIG_DIR`, mặc định `C:\ETEK-WCS\config\.env`); (e) giá trị boolean không phải `true`/`false`.
3. Log backend khi khởi động có dòng `RUNTIME_START` nêu chế độ yêu cầu, chế độ hiệu lực và tên các khóa còn thiếu (không có giá trị).

## Xuất / nhập cấu hình (CR-026)

Chức năng **bật sẵn** sau khi triển khai, **không cần** sửa `appsettings` hay `.env`: chỉ tài khoản **Admin** thấy trang *Hệ thống → Xuất / nhập cấu hình* và gọi được API (server kiểm tra quyền; Operator nhận 403). `WCS_CONFIG_TRANSFER_ENABLED=false` chỉ là công tắc khẩn cấp để tắt.

- **Thư mục lưu trữ bắt buộc:** compose gắn volume `etek_wcs_config_backups` vào `/config-backups` (`ConfigTransfer__BackupPath`). Gói tải lên và bản sao lưu trước mỗi lần áp dụng nằm ở đây, nên **không mất khi container được tạo lại**. Không đặt thư mục này ở nơi tạm (`/tmp`): hệ thống từ chối và không cho áp dụng.
- Volume này **không** bị các script xóa; hãy đưa nó vào kế hoạch sao lưu máy chủ như `etek_wcs_pgdata` và `etek_wcs_keys`.
- Gói cấu hình **không chứa** mật khẩu, token, secret RCS, trạng thái duyệt IO / quyền ghi PLC, tài khoản, tồn kho, nhiệm vụ hay nhật ký. Mật khẩu/token RCS luôn nhập lại thủ công trên máy nhận.
- Áp dụng cần **chế độ bảo trì bật** và **không còn Task/Order/giao dịch trạm đang chạy**; sau khi áp dụng chế độ bảo trì vẫn bật cho tới khi quản trị viên tắt.
- Khuyến nghị: thử trên môi trường kiểm thử trước; kiểm tra bản xem trước; sau khi áp dụng, kỹ sư PLC kiểm tra lại mapping IO (nếu có thay đổi) trước khi Publish quy trình.
