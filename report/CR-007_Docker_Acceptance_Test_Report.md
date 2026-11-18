# CR-007 – Docker Acceptance Test Report (H1–H6)

| Mục | Giá trị |
|---|---|
| Ngày thực hiện | 2026-11-16 |
| Branch / HEAD | `develop` @ `939b453` (đúng HEAD yêu cầu); working tree sạch trước khi thử |
| Checklist | `docs/DEPLOYMENT_TEST_CHECKLIST.md` mục H1–H6 |
| **Kết luận** | **H1–H6: BLOCKED (6/6). Không có hạng mục nào được đánh dấu PASS.** Máy này không có Docker và chưa cài WSL2; việc cài đặt cần quyền Administrator, khởi động lại và (có thể) bật ảo hóa trong BIOS, nên tôi không tự thực hiện |
| Đề xuất nghiệm thu | **CHƯA nghiệm thu phần Docker của CR-007.** Phần chạy ngoài Docker đã có bằng chứng (mục 5); phần Docker cần chạy lại trên máy có Docker theo quy trình ở mục 7 |

> Không có kết quả nào trong báo cáo này được suy ra từ "mã trông đúng". Mọi dòng PASS ở mục 5 là **bằng chứng ngoài Docker**, ghi rõ là không thay thế H1–H6.

---

## 1. Môi trường (đọc trực tiếp, chỉ đọc)

| Hạng mục | Kết quả | Lệnh |
|---|---|---|
| OS | Windows 11 Pro; RAM 31,8 GB; ổ C: trống 174 GB | \`Get-CimInstance Win32_OperatingSystem / Win32_ComputerSystem\`, \`Get-PSDrive C\` |
| Docker CLI | **Không có** (\`docker\` không có trong PATH) | \`Get-Command docker\` → \`False\` |
| Docker Desktop | **Chưa cài**: không có \`C:\Program Files\Docker\Docker\Docker Desktop.exe\`, không có \`resources\bin\docker.exe\`, không có \`C:\ProgramData\DockerDesktop\` | \`Test-Path …\` → \`False\` ×3 |
| Tiến trình Docker/WSL | Không có (\`docker\`, \`vmmem\`, \`com.docker\`) | \`Get-Process\` → 0 |
| WSL | \`wsl.exe\` chỉ là bản stub: \`wsl -l -v\` và \`wsl --version\` in ra màn hình trợ giúp (chưa cài WSL, chưa có distro) | \`wsl -l -v\`, \`wsl --version\` |
| Ảo hóa | \`systeminfo\`: "A hypervisor has been detected"; \`VirtualizationFirmwareEnabled\` = \`False\` (không kết luận được nguyên nhân khi đã có hypervisor) | \`systeminfo\`, \`Win32_Processor\` |
| Quyền | **Không phải Administrator**: không đọc được trạng thái tính năng Windows (\`VirtualMachinePlatform\`, \`Microsoft-Windows-Subsystem-Linux\`) – lỗi "requires elevation" | \`Get-WindowsOptionalFeature -Online\`, \`IsInRole(Administrator)\` → \`False\` |
| winget | Có sẵn (có thể dùng để cài Docker Desktop, nhưng cần quyền admin) | \`Get-Command winget\` |
| dotnet / node / PostgreSQL | 10.0.403 / v24.19.0 / PostgreSQL 16 (service \`postgresql-x64-16\` Running, cổng 5432, dùng cho phát triển) | — |

**Phiên bản Docker / Docker Compose: không có (NOT AVAILABLE).**

### Vì sao tôi không tự cài Docker Desktop
Cài Docker Desktop + WSL2 là thay đổi cấp hệ thống: cần UAC/Administrator, bật tính năng Windows, **khởi động lại máy**, chấp nhận điều khoản cấp phép của Docker và có thể phải bật ảo hóa trong BIOS. Việc này vượt phạm vi đã được cho phép (nhiệm vụ yêu cầu bảo toàn dữ liệu và cấu hình hiện có, không có quyền thay đổi hệ thống), và khởi động lại sẽ làm gián đoạn các dịch vụ đang chạy trên máy (PostgreSQL phát triển, phiên làm việc). Vì vậy kết quả đúng là **BLOCKED**, không phải giả lập.

---

## 2. Kiểm tra an toàn trước khi thử

| Việc | Kết quả |
|---|---|
| \`git branch --show-current\`, \`git rev-parse --short HEAD\` | \`develop\`, \`939b453\` |
| \`git status --short\` trước khi thử | 0 thay đổi |
| Không đụng \`main\`, không commit/push | Đúng (file báo cáo này là file duy nhất mới, chưa commit) |
| Không xóa volume/database hiện có | Không có volume Docker nào; mọi DB/role thử nghiệm ngoài Docker là DB tạm \`wcs_rt2\` + role \`wcs_h\`, đã xóa; DB phát triển \`wcs_db\` không bị chạm |
| Không đổi mã nghiệp vụ / PLC / RCS | Không sửa bất kỳ file mã nguồn nào trong lượt này |

---

## 3. Kết quả H1–H6

Cột "Kết quả thực tế" mô tả đúng điều đã quan sát. Với mọi hạng mục, lệnh đầu tiên cần chạy (\`docker compose …\`) không thể thực hiện vì Docker không tồn tại.

| # | Kiểm tra (theo checklist) | Kỳ vọng | Kết quả thực tế | Trạng thái |
|---|---|---|---|---|
| **H1** | Log script: chạy \`deploy.ps1\`, xem \`C:\ETEK-WCS\logs\` | Có \`deploy-*.log\` và \`deployment-events-*.jsonl\`, có bước/kết quả/exit code, không có mật khẩu | \`deploy.ps1\` dừng ngay ở bước kiểm tra tiền điều kiện: \`Docker is not installed or not in PATH\` (script đúng thiết kế). Không thể đi tới bước build/migrate/up. Bằng chứng ngoài Docker xem 5.1 | **BLOCKED** |
| **H2** | Nhập sự kiện triển khai: \`/logs\` → nhóm *Triển khai* (≤ 1 phút) | Thấy các sự kiện của lần chạy | Backend **trong container** không tồn tại. Bằng chứng ngoài Docker (API chạy trực tiếp trên host) xem 5.2 | **BLOCKED** |
| **H3** | Lưu log khi lỗi: sai \`POSTGRES_PASSWORD\` rồi \`start.ps1\` | Có \`failure-<run>-*.log\` (migrator/backend) và sự kiện FAIL trên màn hình | Không có container để sinh log migrator/backend. Phần đã chạy được: script thất bại do thiếu Docker ghi sự kiện FAIL + END (exit 1) và hiển thị trên màn hình (5.1, 5.2). **Việc lưu log container (\`docker compose logs\`) chưa được chạy** | **BLOCKED** |
| **H4** | Thư mục log chỉ-đọc: \`docker exec etek-wcs-backend-1 sh -c "touch /deploy-logs/x"\` | Bị từ chối (read-only) | Không có container. Cấu hình có trong compose (\`…:/deploy-logs:ro\`, kiểm tĩnh bằng parser YAML – xem 5.3) nhưng **hiệu lực của cờ \`:ro\` trên Docker Desktop/WSL2 chưa được kiểm** | **BLOCKED** |
| **H5** | Log PLC trong Docker: \`docker compose -p etek-wcs logs backend\` khi bật/tắt kết nối PLC thử | Dòng \`PLC PLC-0x STATE\` kèm IP/cổng, trạng thái trước→sau | Không có \`docker compose logs\`. Dòng PLC trên stdout của backend chạy trực tiếp đã kiểm ở các lượt trước (5.4). Không có PLC thật; không gửi lệnh tới PLC/RCS/AGV | **BLOCKED** |
| **H6** | Database mất kết nối: \`docker stop etek-wcs-postgres-1\` 2 phút rồi \`start\` | 1 dòng DB_LOST, throttle, DB_RESTORED sau khi khôi phục | Không có container PostgreSQL. **Bản mô phỏng ngoài Docker đã chạy được** (role không phải superuser bị khóa/mở lại trên DB tạm) và đạt: xem 5.5. Lệnh \`docker stop/start\` thật chưa chạy | **BLOCKED** |

---

## 4. Container status / log Docker

**Không có dữ liệu** (không có Docker engine): không có \`docker ps\`, \`docker inspect\` (RestartCount, OOMKilled, ExitCode), \`docker compose logs\`, \`docker network/volume\` để ghi nhận. \`status.ps1\` (RestartCount/OOMKilled/health) **chưa được chạy** trên container thật.

---

## 5. Bằng chứng bổ sung NGOÀI Docker (không thay thế H1–H6)

### 5.1 Script triển khai ghi log (H1/H3 – phần không cần Docker)
\`\`\`
powershell -NoProfile -ExecutionPolicy Bypass -File tests\deployment\Test-DeploymentLogging.ps1
\`\`\`
Kết quả (lượt chạy cuối trước báo cáo này): **20/20 PASS** – tạo \`deploy-*.log\` + \`deployment-events-*.jsonl\` ngoài repository; JSON hợp lệ, id duy nhất; START/STEP/OK/WARN/FAIL/END (exit 1); che 6 dạng secret (mật khẩu \`.env\`, \`password=…\`, \`token=…\`, \`Bearer …\`, \`Password=\` trong connection string, giá trị trong thông báo lỗi); thư mục log không ghi được → cảnh báo, script vẫn kết thúc bằng mã lỗi riêng; \`prune-logs.ps1\` mặc định chỉ liệt kê.
Chạy thật \`stop.ps1\` trên máy không có Docker: \`FAIL … Docker is not installed or not in PATH\` + \`END exit=1\` được ghi (xem 5.2).

### 5.2 Nhập sự kiện triển khai vào API (H2 – bản host, không container)
Lệnh: chạy \`deployment\scripts\stop.ps1\` với \`WCS_LOG_DIR\` tạm → tạo file log thật; khởi động API (Development) với \`DeploymentLogs__Directory\` trỏ tới thư mục đó, DB tạm \`wcs_rt2\`.
Thực tế: **4 sự kiện DEPLOYMENT** được nhập khi khởi động (\`DEPLOY_START\`, \`DEPLOY_STEP\`, \`DEPLOY_FAIL\`, \`DEPLOY_END\`), thiết bị = \`stop.ps1\`; hiển thị ở nhóm *Triển khai* của trang (đã chụp ảnh ở lượt trước, trình duyệt 29/29). Khởi động lại tiến trình backend: **4 → 4** (không nhân đôi), log cũ còn nguyên (**11 → 12** dòng \`system_log\`, thêm dòng khởi động mới).
**Giới hạn:** đây là tiến trình .NET trên host đọc thư mục cục bộ, **không phải** container đọc bind mount.

### 5.3 Cấu hình mount chỉ-đọc (H4 – kiểm tĩnh)
\`deployment/docker-compose.yml\`: \`- \${WCS_LOG_DIR:-C:/ETEK-WCS/logs}:/deploy-logs:ro\` và \`DeploymentLogs__Directory: /deploy-logs\` (kiểm bằng parser YAML ở CR-006/CR-007). Không có endpoint HTTP ghi log: \`POST /api/logs\` trả **401** (chưa đăng nhập) / **405** (Admin) và mã nguồn không có route POST/PUT/DELETE/PATCH nào cho \`logs\` (kiểm bằng \`Select-String\`). **Cờ \`:ro\` có thực sự chặn ghi trong container chưa được kiểm.**

### 5.4 Sự kiện PLC trên stdout (H5 – không container)
Đã kiểm ở lượt runtime trước (16/16): backend chạy trực tiếp in dòng \`PLC PLC-0x …\` khi chuyển trạng thái (Simulator), lưu vào \`plc_comm_log\` và thấy trên trang. Test đơn vị xác nhận ngữ cảnh \`Connected -> Faulted\`, IP/cổng, TIMEOUT / END_CODE, RECONNECTED. **Không có PLC thật.**

### 5.5 DB_LOST / DB_RESTORED (H6 – mô phỏng ngoài Docker)
DB tạm \`wcs_rt2\`; API kết nối bằng role thường \`wcs_h\` (role superuser không bị giới hạn nên không dùng được). Làm mất kết nối: \`ALTER ROLE wcs_h NOLOGIN\` + \`pg_terminate_backend\`; khôi phục: \`ALTER ROLE wcs_h LOGIN\`.

| Kiểm tra | Kết quả |
|---|---|
| \`/healthz\` khi mất kết nối | **503** (đạt) |
| \`/healthz\` sau khi khôi phục | **200** (đạt) |
| stdout | đúng **1** \`DB_LOST\` và **1** \`DB_RESTORED\`; dòng khôi phục ghi thời gian gián đoạn ("restored after N s") |
| Lỗi scheduler trong ~38 s mất kết nối | **2 dòng** (throttle hoạt động; không có 38 dòng) |
| \`system_log\` sau khôi phục | \`DB_LOST/DATABASE/Warning\` rồi \`DB_RESTORED/DATABASE/Information\` (hàng đợi giữ sự kiện trong lúc mất kết nối, lưu lại khi DB trở lại) |
| Mật khẩu trong stdout | không có |
| Khởi động lại backend | log cũ còn, \`DB_LOST\` vẫn tìm được |

Tổng: **13/13 PASS ngoài Docker** (script \`scratchpad\dbrestore.ps1\`). Các DB/role tạm đã xóa.

---

## 6. Vấn đề, nguyên nhân gốc, phân loại

| # | Vấn đề | Nguyên nhân gốc | Phân loại | Hướng xử lý |
|---|---|---|---|---|
| 1 | Không chạy được H1–H6 | Máy không có Docker Desktop và WSL2; tài khoản không có quyền Administrator | **ENVIRONMENT** | Cài Docker Desktop + WSL2 trên máy triển khai (hoặc cấp quyền/ cho phép cài và khởi động lại), rồi chạy lại mục 7 |
| 2 | Lần chạy mô phỏng mất DB đầu tiên cho kết quả sai ( \`/healthz\` vẫn 200, không có DB_LOST) | Phương pháp thử sai: role \`postgres\` là superuser nên **không chịu** \`CONNECTION LIMIT 0\` | **TEST LIMITATION** (không phải lỗi mã) | Đã sửa phương pháp (role thường + \`NOLOGIN\`), chạy lại đạt 13/13 |
| 3 | Lần chạy đầu một kiểm tra "không có route ghi cho logs" báo sai | Mẫu regex quá rộng (khớp \`login\`/\`logout\`) | **TEST LIMITATION** | Đã thu hẹp mẫu; đạt |
| 4 | Không có lỗi mã nguồn hay cấu hình Docker nào được phát hiện trong lượt này | — | — | — |

Không có thay đổi mã nào được đề xuất hoặc thực hiện.

---

## 7. Quy trình chạy lại trên máy có Docker (đã chuẩn bị, **chưa thực hiện**)

Điều kiện: Docker Desktop (WSL2) đang chạy; \`C:\ETEK-WCS\config\.env\` từ \`.env.example\` (có \`POSTGRES_PASSWORD\`, \`WCS_ADMIN_BOOTSTRAP_PASSWORD\`, \`WCS_COOKIE_SECURE=SameAsRequest\` nếu dùng HTTP); clone \`develop\` (không dùng \`deploy.ps1\` từ \`develop\` – script yêu cầu \`main\`; dùng \`start.ps1\` hoặc \`docker compose\` thủ công cho đợt thử này).

| # | Lệnh | Bằng chứng cần lưu |
|---|---|---|
| Chuẩn bị | \`docker --version\`, \`docker compose version\`, \`docker info\`; \`docker compose --env-file C:\ETEK-WCS\config\.env -f deployment\docker-compose.yml config -q\` | phiên bản, \`config -q\` không lỗi |
| H1 | \`deployment\scripts\start.ps1\`; \`dir C:\ETEK-WCS\logs\`; \`Select-String -Path C:\ETEK-WCS\logs\* -Pattern '<giá trị POSTGRES_PASSWORD>'\` | có \`deploy-*.log\` + \`deployment-events-*.jsonl\`; **0** kết quả khớp mật khẩu |
| H2 | đăng nhập Admin, \`/logs\` → nhóm *Triển khai* (chờ ≤ 60 s); \`docker compose -p etek-wcs logs backend \| Select-String DEPLOY\` nếu cần | ảnh chụp trang, số sự kiện |
| H3 | đổi \`POSTGRES_PASSWORD\` trong \`.env\` **sau khi DB đã tạo** (hoặc sai cố ý), \`start.ps1\`; \`dir C:\ETEK-WCS\logs\failure-*\` | \`failure-<run>-migrator.log\`/\`-backend.log\`/\`-last-command.log\`, sự kiện FAIL trên trang; **khôi phục \`.env\` sau đó** (không xóa volume) |
| H4 | \`docker exec etek-wcs-backend-1 sh -c "touch /deploy-logs/x"\`; \`docker inspect etek-wcs-backend-1 --format "{{json .Mounts}}"\` | lỗi "Read-only file system"; Mounts có \`"RW":false\` |
| H5 | \`docker compose -p etek-wcs logs backend --tail 200 \| Select-String 'PLC PLC-'\` (Plc__Mode mặc định RealReadOnly, PLC \`Disabled\`; nếu muốn có sự kiện: bật mô phỏng chỉ trên stack thử) | dòng \`PLC PLC-0x …\` kèm ngữ cảnh |
| H6 | \`docker stop etek-wcs-postgres-1\` (**chỉ trên stack thử, xin phép trước**, 2 phút) rồi \`docker start etek-wcs-postgres-1\`; \`docker compose logs backend \| Select-String 'DB_LOST\|DB_RESTORED'\` | đúng 1 \`DB_LOST\`, đúng 1 \`DB_RESTORED\`, trang \`/logs\` hiển thị cả hai; \`docker volume ls\` vẫn còn \`etek_wcs_pgdata\` |
| Thêm | \`deployment\scripts\status.ps1\` sau khi \`docker restart etek-wcs-backend-1\` | \`restarts=\` tăng, \`oom=false\`, health probe cuối |
| Thêm | \`docker restart etek-wcs-backend-1\`; \`dir C:\ETEK-WCS\logs\`; \`/logs\` | file log và sự kiện cũ còn; số sự kiện DEPLOYMENT không nhân đôi |

Ghi chú an toàn cho H6: \`docker stop\` PostgreSQL làm WCS ngừng hoạt động trong lúc dừng — chỉ làm trên stack thử và có sự đồng ý của chủ dự án; không dùng \`docker compose down -v\`.

---

## 8. Rủi ro tồn đọng

| Rủi ro | Mức | Ghi chú |
|---|---|---|
| Chưa có bằng chứng Docker cho H1–H6 | **HIGH** (cho nghiệm thu Docker) | Chặn nghiệm thu phần deployment-in-container của CR-007 |
| Cờ \`:ro\` của bind mount thư mục log trên Docker Desktop/WSL2 | MEDIUM | Đường dẫn Windows \`C:/ETEK-WCS/logs\` gắn qua WSL2; cần xác nhận quyền đọc (và chỉ-đọc) thực tế |
| Importer đọc file trên bind mount (độ trễ ghi, khóa file) | MEDIUM | Đã chịu được \`FileShare.ReadWrite\`; chưa thử qua WSL2 file share |
| Thời điểm import ≤ 30 s sau khi script ghi (khoảng thử của importer) | LOW | Checklist ghi "≤ 1 phút" |
| \`docker compose logs\` khi thất bại (Save-FailureLogs) và \`status.ps1\` đọc \`.State.Health\` | MEDIUM | Chưa chạy; cú pháp \`--format\` cần xác nhận với Docker thật |
| \`deploy.ps1\` chỉ chạy từ \`main\` | LOW | Muốn thử trên \`develop\` phải dùng \`start.ps1\`/compose thủ công (ghi ở mục 7) |

---

## 9. Đề xuất nghiệm thu

- **Phần Docker (H1–H6): CHƯA ĐƯỢC NGHIỆM THU – BLOCKED do môi trường.** Không có kết quả FAIL và không có lỗi mã được phát hiện, nhưng cũng **không có PASS** nào để công nhận.
- **Phần ngoài Docker: đạt** (script 20/20; nhập sự kiện từ file, idempotent qua khởi động lại; DB_LOST/DB_RESTORED 13/13; không có endpoint ghi log). Đây là bằng chứng bổ trợ, không thay thế.
- **Khuyến nghị:** không merge \`develop\` → \`main\` cho phần liên quan Docker của CR-007 cho tới khi chạy xong mục 7 trên máy có Docker. Nếu chủ dự án chấp nhận rủi ro (mục 8), có thể merge với ghi chú "Docker acceptance pending"; quyết định thuộc về chủ dự án.
- **Việc cần từ chủ dự án để mở khóa:** (1) máy có Docker Desktop + WSL2 (cài bởi người có quyền Administrator, có thể cần khởi động lại và bật ảo hóa trong BIOS), hoặc cho phép tôi cài và khởi động lại máy này; (2) đồng ý cho dừng/khởi động container PostgreSQL của stack thử (H6).

Tôi dừng tại đây và chờ quyết định.
