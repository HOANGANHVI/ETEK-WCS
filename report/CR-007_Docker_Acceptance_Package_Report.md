# CR-007 – Báo cáo chuẩn bị bộ kiểm thử nghiệm thu Docker (H1–H6)

| Mục | Giá trị |
|---|---|
| Ngày | 2026-11-16 |
| Branch / HEAD | `develop` @ `939b453` (đúng yêu cầu) |
| `main` | `d5d692f` (không đổi; `origin/main` cũng `d5d692f`) |
| Git status khi bắt đầu | 1 file chưa theo dõi (`report/CR-007_Docker_Acceptance_Test_Report.md`) – đã bảo toàn |
| Phạm vi | Chuẩn bị công cụ. **Không** thay đổi checklist H1–H6, mã nguồn hay trạng thái H1–H6. **Không** commit/push/merge |
| **Trạng thái H1–H6** | **Không đổi: chưa nghiệm thu (BLOCKED do không có Docker trên máy này).** Báo cáo này không tuyên bố CR-007 đã nghiệm thu Docker |

---

## 1. Các file đã tạo (chưa commit)

| File | Dòng | Chức năng |
|---|---|---|
| `docs/docker-acceptance/README.md` | 140 | Hướng dẫn tiếng Việt: điều kiện máy, phiên bản Docker/WSL2, kiểm tra môi trường, trình tự H1–H6, cách đọc kết quả, thu thập bằng chứng, an toàn, khôi phục, xử lý sự cố |
| `docs/docker-acceptance/preflight.ps1` | 193 | Kiểm tra môi trường, **chỉ đọc** |
| `docs/docker-acceptance/run-acceptance.ps1` | 603 | Điều phối H1–H6 (+ X1, X2 bổ sung), ghi bằng chứng, xuất báo cáo |
| `docs/docker-acceptance/collect-evidence.ps1` | 108 | Thu thập bằng chứng, **chỉ đọc**, chạy được độc lập |
| `docs/docker-acceptance/report-template.md` | 81 | Mẫu báo cáo nghiệm thu tiếng Việt |
| `docs/docker-acceptance/_accept-common.ps1` | 255 | Hàm dùng chung (che bí mật, chạy lệnh có ghi vết, đăng nhập API Admin, xác nhận thao tác) – file phụ ngoài danh sách yêu cầu, để các script dùng chung một cách che bí mật |
| `tests/deployment/Test-DockerAcceptancePackage.ps1` | 239 | Tự kiểm tra bộ công cụ **không cần Docker** (tĩnh + động) |
| `tests/deployment/fake-docker/docker.ps1` | 100 | "docker" **giả lập** chỉ dùng cho tự kiểm tra; không phải Docker thật |

Tất cả script có UTF-8 BOM (Windows PowerShell 5.1 đọc đúng tiếng Việt), không sửa file hiện có nào.

## 2. Chức năng từng script

### `preflight.ps1` (chỉ đọc)
Mỗi mục có **ĐẠT / KHÔNG ĐẠT / CHƯA THỂ KIỂM TRA** và cột *bắt buộc*: Windows 11 (build ≥ 22000); quyền Administrator (không bắt buộc); WSL2 (phát hiện cả `wsl.exe` bản stub); Docker CLI; Docker Desktop; Docker daemon ở chế độ Linux containers; Compose v2; container/volume hiện có (cảnh báo gián đoạn nếu stack đang chạy); dung lượng ổ đĩa; `compose` và `.env` (chỉ ghi **tên** biến); từ chối nếu `PLC_ALLOW_LIVE_CONNECTION=true`; thư mục log; Git; PostgreSQL **của stack** (label, image, volume, không publish cổng, `pg_isready`); cổng web. Exit code `0` sẵn sàng / `1` có mục bắt buộc KHÔNG ĐẠT / `2` thiếu thông tin. Không cài, không đổi cấu hình, không tạo thư mục.

### `run-acceptance.ps1`
- Thực hiện đúng nội dung checklist gốc (lệnh và kết quả mong đợi được lấy nguyên văn từ checklist trong báo cáo):

| Bài | Làm gì | ĐẠT khi |
|---|---|---|
| H1 | Chạy (có `-AllowStackStart`) hoặc **quan sát** lần chạy `deploy.ps1`/`start.ps1` gần nhất; đọc `deploy-*.log`, `deployment-events-*.jsonl` | Có cả hai file, đủ trường, có STEP và END+exit code, không lộ bí mật |
| H2 | Đăng nhập Admin, `/api/logs?category=DEPLOYMENT`; liệt kê `/deploy-logs` trong container | Đủ sự kiện của lần chạy trong ≤ 60 s |
| H3 | `.env` **tạm** sai mật khẩu → `start.ps1` (kỳ vọng lỗi) → **tự khôi phục** bằng `.env` thật (trong `finally`) | Có `failure-<run>-*.log`, sự kiện FAIL (cả trên API), không lộ bí mật, stack khỏe lại |
| H4 | `docker exec … touch /deploy-logs/<file thử>`; `docker inspect` Mounts | Ghi bị từ chối, `RW=false`, không có file lạ trên host (file thử nếu lọt qua sẽ bị xóa) |
| H5 | `docker compose logs backend`; tìm `PLC PLC-0x STATE (A -> B; IP)` | Có dòng chuyển trạng thái kèm IP; nếu không có sự kiện → CHƯA THỂ KIỂM TRA (không tự bật PLC) |
| H6 | Xác định container PostgreSQL → xin xác nhận → `docker stop` → chờ (mặc định 120 s) → `docker start` (luôn chạy trong `finally`) | `/healthz` 503→200, đúng 1 `DB_LOST` + 1 `DB_RESTORED` (log và API), throttle, volume còn |
| X1 (bổ sung) | `status.ps1` | `restarts=`, `oom=`, health probe cuối |
| X2 (bổ sung) | `docker restart` backend (cần xác nhận) | File log còn, sự kiện không nhân đôi |

- Ghi timestamp từng bước (`steps.log`), lệnh + exit code + stdout/stderr (`commands.log`), trạng thái container và log Docker (`_pre`, `_post`), JSON kết quả và báo cáo Markdown. Bằng chứng nằm ngoài repository (`C:\ETEK-WCS\acceptance-evidence\<thời gian>-acceptance\`).
- Không bỏ qua bài: thiếu điều kiện → **CHƯA THỂ KIỂM TRA** + nguyên nhân; không bao giờ ĐẠT khi chưa có bằng chứng. Exit code `0/1/2/3`.

### `collect-evidence.ps1` (chỉ đọc)
Phiên bản Docker/Compose, trạng thái container, RestartCount, OOMKilled, health, cổng publish, **cấu hình bind mount và cờ RW**, volume/network, log ứng dụng (tail), log triển khai, quét bí mật. **Không** ghi `Config.Env`, không ghi giá trị `.env` (chỉ tên biến), mọi văn bản được che bí mật.

### `report-template.md`
Mỗi bài: mã, mục tiêu, điều kiện tiên quyết, lệnh thực hiện, kết quả mong đợi, kết quả thực tế, các bước (có giờ), bằng chứng, trạng thái, ghi chú; thêm tổng hợp, vấn đề/nguyên nhân gốc, rủi ro, danh sách khôi phục, ký xác nhận.

## 3. Kết quả kiểm tra cú pháp PowerShell

| Kiểm tra | Kết quả |
|---|---|
| Parser PowerShell (Windows PowerShell 5.1) trên 8 file `.ps1` mới | **0 lỗi** |
| UTF-8 BOM trên mọi script của bộ công cụ | Có |
| Chạy thực `preflight.ps1` trên máy này (không có Docker) | Chạy đúng, báo "CHƯA SẴN SÀNG" với từng nguyên nhân (WSL chưa cài, không có Docker, không có `.env`), **không tạo thư mục nào** |
| Chạy thực `run-acceptance.ps1 -Tests H1..H6,X1,X2` trên máy này | **8/8 CHƯA THỂ KIỂM TRA** kèm nguyên nhân, exit code 2, tạo đủ báo cáo/bằng chứng – không có ĐẠT giả |
| Lỗi phát hiện và đã sửa trong lúc làm | `-Tests H1,H2…` qua `powershell -File` bị coi là một chuỗi → đã tách theo dấu phẩy; `-AdminPassword` (SecureString) không truyền được qua `-File` → thêm đường `WCS_ACCEPT_ADMIN_PASSWORD` (biến xóa ngay sau khi đọc) và ghi trong README |

## 4. Tự kiểm tra bằng `tests\deployment\Test-DockerAcceptancePackage.ps1` – **49/49 đạt**

Không cần Docker. Phần động dùng "docker" giả lập nhưng **backend WCS thật** (Development) + **PostgreSQL thật** (DB tạm `wcs_acc`, role tạm `wcs_acc_role`, đã xóa sau khi chạy).

**A. Tĩnh (15):** đủ file; cú pháp; BOM; **phân tích AST** không có lệnh phá hủy/cấu hình hệ thống thực thi (`prune`, `volume rm`, `compose down`, `rm/rmi/kill`, `Stop-Service`, `wsl --install`, `Set-ExecutionPolicy`, firewall…) – đã chứng minh bằng *mutation test* (một file cố tình nguy hiểm bị phát hiện); không `DROP`; mọi `Remove-Item` chỉ xóa thư mục/file tạm của chính bộ công cụ; H3/H6/X2 có bước xác nhận; H6 luôn `start` lại trong `finally`; H6 nhận diện container bằng label/image/volume/cổng; không dump `Config.Env`; không địa chỉ PLC/RCS/AGV; README ↔ script nhất quán (14 tham số); placeholder báo cáo khớp mẫu.

**B. Động (34):**
- H1 quan sát log script thật = ĐẠT; H2 sự kiện hiện trong `/api/logs` của backend thật ≤ 60 s = ĐẠT; H4 mount chỉ-đọc = ĐẠT, **KHÔNG ĐẠT** khi mount ghi được (file thử được xóa lại); H5 = CHƯA THỂ KIỂM TRA khi không có sự kiện PLC, = ĐẠT với log thật của backend (`Connecting -> Connected; IP`) và với dòng giả lập; exit code 2 / 1 / 0 đúng.
- Cổng xác nhận: H3/H6/X2 không xác nhận (không tương tác) → CHƯA THỂ KIỂM TRA **và không làm gì**; H6 với tên container sai → không dừng PostgreSQL.
- **H6 đầy đủ = ĐẠT:** mất DB 40 s (khóa role) → `/healthz` 503, đúng 1 `DB_LOST`/1 `DB_RESTORED`, API hiển thị cả hai, khôi phục, bằng chứng đầy đủ.
- **H3 đầy đủ = ĐẠT** (với `start.ps1` *stub* dùng `_common.ps1` thật): thất bại có kiểm soát, `failure-*.log`, sự kiện FAIL trên API, **`.env` thật không đổi (cùng hash)**, thư mục `.env` tạm được xóa, 3 lần START trong log (thử + lỗi + khôi phục).
- **Che bí mật:** mật khẩu PostgreSQL, mật khẩu Admin (2 giá trị), mật khẩu role, mật khẩu sai tạm thời và chuỗi `THIS-MUST-NEVER-BE-COPIED` đặt trong `Config.Env` của container giả đều **không** xuất hiện trong bất kỳ file bằng chứng hay log triển khai nào.
- `collect-evidence.ps1` chạy độc lập: `container-status.md` (RestartCount, OOMKilled), `mounts-backend.txt` (`/deploy-logs RW=False`), log, `env-keys.txt` chỉ có tên biến; `preflight.ps1` với docker giả nhận daemon/Compose/PostgreSQL container.

## 5. Rà soát an toàn

| Yêu cầu | Kết quả |
|---|---|
| Không xóa Docker volume | Không có lệnh nào (AST) |
| Không xóa database | Không có `DROP`/`volume rm`/`down`/`rm` trong bộ công cụ |
| Không `docker system prune` | Không có |
| Không tự động restart container đang chạy | Chỉ `docker restart` trong X2, sau xác nhận |
| Không tự động dừng PostgreSQL; H6 cần xác nhận | `docker stop` duy nhất ở H6, sau khi gõ đúng `STOP <tên container>` hoặc `-ConfirmH6 -ConfirmPostgresContainer <tên>`; `start` luôn chạy lại trong `finally` |
| Xác định chính xác container/DB thử trước H6 | Label `project/service`, image `postgres:16*`, volume `etek_wcs_pgdata`, không publish cổng; từ chối (CHƯA THỂ KIỂM TRA) nếu thiếu một điều kiện |
| Không tác động `wcs_db` phát triển | Bộ công cụ chỉ làm việc với container của stack; không kết nối PostgreSQL của host (cổng 5432); README và báo cáo ghi rõ |
| Không kết nối PLC/RCS/AGV thật | Từ chối chạy nếu `PLC_ALLOW_LIVE_CONNECTION=true`; không có địa chỉ/lệnh PLC trong bộ công cụ; H5 không tự bật PLC |
| Không lộ bí mật trong bằng chứng | Che theo giá trị `.env` + mẫu password/token/cookie/bearer; không lưu `Config.Env`; đã kiểm bằng test |
| Không sửa module nghiệp vụ được bảo vệ | `git diff --name-only HEAD` không có file nào thuộc danh sách (chỉ có file mới trong `docs/docker-acceptance/` và `tests/deployment/`) |
| Thao tác gây gián đoạn của stack | `start.ps1` (H1 chế độ thực thi, H3) có thể tạo/tạo lại container qua `docker compose up`; H1 mặc định ở chế độ **quan sát** (không chạy gì) |

## 6. Git

`develop` @ `939b453`; `main` = `origin/main` = `d5d692f`. Chưa commit/push/merge. File chưa theo dõi: `docs/docker-acceptance/` (6 file), `tests/deployment/Test-DockerAcceptancePackage.ps1`, `tests/deployment/fake-docker/`, và 2 file trong `report/`.

## 7. Vấn đề còn tồn tại / giới hạn (trung thực)

1. **Chưa chạy trên Docker thật.** Mọi kết quả ở mục 4 dùng "docker" giả lập: chúng chứng minh **logic** của bộ công cụ, không chứng minh hành vi Docker Desktop/WSL2. Các điểm cần xác nhận lần đầu trên Docker thật: định dạng `docker inspect` (Mounts/RW, Health), thông báo lỗi `touch` ("Read-only file system"), `docker compose logs --since`, `docker compose ps -q`, tên container `etek-wcs-<service>-1`.
2. **X1 và X2 chưa được chạy động** (chỉ có cổng xác nhận được kiểm); **H3 dùng `start.ps1` stub** (dùng `_common.ps1` thật nhưng không chạy `docker compose up`/migrator thật). Luồng H3 với migrator thật cần kiểm trên Docker thật.
3. **H5** chỉ ĐẠT khi stack thực sự phát sinh sự kiện PLC có trạng thái trước→sau và IP; trên stack Production không có PLC thì sẽ là CHƯA THỂ KIỂM TRA (đúng bản chất; bộ công cụ không tự bật PLC).
4. **H2** cần tài khoản Admin đã đổi mật khẩu; cookie `Secure` qua HTTP cần `WCS_COOKIE_SECURE=SameAsRequest` (đã nêu trong README và thông báo lỗi).
5. **`deploy.ps1` chỉ chạy từ `main`:** trên `develop`, H1 dùng `start.ps1` và ghi rõ sự lệch trong báo cáo (không sửa checklist).
6. **Phần giao diện** của H2/H3/H6 được xác nhận qua `/api/logs` (nguồn dữ liệu của giao diện); xác nhận bằng mắt là tùy chọn (`-ManualUiCheck`).
7. **Lưu ý môi trường phát triển:** còn các database thử nghiệm cũ trên PostgreSQL phát triển (`wcs_e2e`, `wcs_test_*` do các lần chạy test trước để lại) – không thuộc lượt này và **không bị xóa** theo nguyên tắc "không xóa database"; có thể dọn thủ công. Không còn DB/role tạm của lượt này.
8. Bộ công cụ **chưa được commit**; cần quyết định commit (có thể thành một commit riêng trên `develop`).

## 8. Hướng dẫn chạy trên máy Docker thực tế

1. Chuẩn bị máy Windows 11: cài WSL2 và Docker Desktop (WSL2 backend), bật ảo hóa trong BIOS nếu cần; clone repository, `git checkout develop`.
2. Tạo `C:\ETEK-WCS\config\.env` từ `deployment\.env.example`: đặt `POSTGRES_PASSWORD`, `WCS_ADMIN_BOOTSTRAP_PASSWORD`, `PLC_ALLOW_LIVE_CONNECTION=false`; nếu dùng HTTP thêm `WCS_COOKIE_SECURE=SameAsRequest`.
3. Kiểm tra môi trường: `powershell -NoProfile -ExecutionPolicy Bypass -File docs\docker-acceptance\preflight.ps1` → cần "SẴN SÀNG".
4. Khởi động stack lần đầu và đăng nhập giao diện bằng admin để **đổi mật khẩu**.
5. Chạy các bài không gây gián đoạn: `...\run-acceptance.ps1 -Tests H1,H2,H4,H5 -AllowStackStart` (nhập mật khẩu Admin khi được hỏi).
6. Chạy H3 và H6 (gây gián đoạn tạm thời, chỉ trên stack thử): `...\run-acceptance.ps1 -Tests H3,H6` rồi gõ đúng cụm xác nhận được yêu cầu.
7. (Tùy chọn) `...\run-acceptance.ps1 -Tests X1,X2`.
8. Đọc `acceptance-report.md` trong `C:\ETEK-WCS\acceptance-evidence\<thời gian>-acceptance\`; làm theo mục "Khôi phục" của README; chuyển bằng chứng và báo cáo cho người nghiệm thu.

## 9. Điều kiện kết thúc

Bộ công cụ và báo cáo đã sẵn sàng. **Không** tuyên bố CR-007 đã nghiệm thu Docker; **không** thay đổi trạng thái H1–H6. Chờ người dùng xem xét trước bước tiếp theo.
