# Bộ công cụ nghiệm thu Docker – CR-007 (H1–H6)

Bộ công cụ này giúp người vận hành thực hiện **đúng các bài H1–H6** trong [`docs/DEPLOYMENT_TEST_CHECKLIST.md`](../DEPLOYMENT_TEST_CHECKLIST.md) trên một máy **Windows 11 có Docker Desktop + WSL2**, tự thu thập bằng chứng và xuất báo cáo.

> **Quan trọng:** chỉ kết quả chạy trên máy có Docker mới có giá trị nghiệm thu. Bộ công cụ này **không** tự tuyên bố CR-007 đã nghiệm thu; nó chỉ ghi trạng thái từng bài kèm bằng chứng. Bài nào thiếu điều kiện sẽ là **CHƯA THỂ KIỂM TRA** (kèm nguyên nhân), không bao giờ là ĐẠT.

## 1. Các file

| File | Chức năng |
|---|---|
| `preflight.ps1` | Kiểm tra môi trường (Windows, quyền Administrator, WSL2, Docker CLI/Desktop/daemon/Compose, container và volume hiện có, ổ đĩa, `.env`, PostgreSQL của stack). **Chỉ đọc.** |
| `run-acceptance.ps1` | Điều phối H1–H6 (và X1, X2 bổ sung), ghi lệnh/exit code/stdout/stderr/trạng thái container, tạo thư mục bằng chứng và báo cáo |
| `collect-evidence.ps1` | Thu thập bằng chứng (phiên bản, container, RestartCount, OOMKilled, mount chỉ-đọc, log ứng dụng, log triển khai). **Chỉ đọc.** Có thể chạy riêng |
| `report-template.md` | Mẫu báo cáo nghiệm thu tiếng Việt (điền tự động hoặc điền tay) |
| `_accept-common.ps1` | Hàm dùng chung (che bí mật, chạy lệnh có ghi vết, đăng nhập API…) |

Tự kiểm tra bộ công cụ không cần Docker: `tests\deployment\Test-DockerAcceptancePackage.ps1` (dùng "docker" giả lập `tests\deployment\fake-docker`; đây **không phải** nghiệm thu Docker).

## 2. Điều kiện máy

| Hạng mục | Yêu cầu |
|---|---|
| Hệ điều hành | Windows 11 (build ≥ 22000) |
| WSL2 | Đã cài (`wsl --install`), có distro chạy phiên bản 2 (Docker Desktop dùng distro `docker-desktop`) |
| Docker Desktop | Bản dùng WSL2 backend, **Linux containers**; engine đang chạy ("Engine running"); Docker Compose v2 (`docker compose version`) |
| Quyền | Tài khoản thuộc nhóm `docker-users`. Quyền Administrator chỉ cần để **cài** WSL/Docker, không cần để chạy bộ công cụ |
| Dung lượng | ≥ 20 GB trống ở ổ hệ thống (image + dữ liệu thử) |
| Mã nguồn | Clone repository; branch `develop` @ `939b453` (hoặc commit mới hơn của `develop`) để thử CR-007 |
| Cấu hình | `C:\ETEK-WCS\config\.env` tạo từ `deployment\.env.example` (có `POSTGRES_PASSWORD`; `PLC_ALLOW_LIVE_CONNECTION` **phải** là `false`). Nếu truy cập bằng HTTP: `WCS_COOKIE_SECURE=SameAsRequest` |
| Tài khoản Admin | Một tài khoản **Admin** đã đổi mật khẩu lần đầu (để đọc `/api/logs`). Mật khẩu chỉ nhập khi được hỏi, chỉ ở trong bộ nhớ |
| PLC/RCS/AGV | **Không** kết nối thiết bị thật. Bộ công cụ không bao giờ gửi lệnh tới PLC/RCS/AGV |

> Lưu ý: `deploy.ps1` chỉ chạy từ nhánh `main`. Khi thử trên `develop`, H1 dùng `start.ps1` (bộ công cụ tự ghi lại sự lệch này trong báo cáo).

## 3. Cách kiểm tra môi trường

```powershell
cd <thư mục repository>
powershell -NoProfile -ExecutionPolicy Bypass -File docs\docker-acceptance\preflight.ps1
```

Mỗi mục có kết quả **ĐẠT / KHÔNG ĐẠT / CHƯA THỂ KIỂM TRA** và cột "bắt buộc". Exit code: `0` sẵn sàng, `1` có mục bắt buộc KHÔNG ĐẠT, `2` thiếu thông tin. Thêm `-OutFile <đường dẫn.md>` để lưu kết quả. Preflight **không cài đặt, không đổi cấu hình, không tạo thư mục**.

## 4. Trình tự chạy H1–H6

Bài chạy theo thứ tự H1 → H6 (H2 dùng lần chạy của H1; H3 và H6 làm gián đoạn tạm thời nên cần xác nhận). Tham số chính của `run-acceptance.ps1`:

| Tham số | Ý nghĩa |
|---|---|
| `-Tests H1,H2,...` | Chọn bài (mặc định H1–H6; có thêm `X1`, `X2`). Phân tách bằng dấu phẩy |
| `-AllowStackStart` | H1 ở **chế độ thực thi**: tự chạy `deploy.ps1` (nếu đang ở `main`) hoặc `start.ps1`. Không có cờ này, H1 chỉ **quan sát** lần chạy deploy/start gần nhất trong `C:\ETEK-WCS\logs` |
| `-H1Script auto\|deploy\|start` | Chọn script cho H1 (auto: `deploy` nếu branch `main`, ngược lại `start`) |
| `-ConfirmH3` | Xác nhận H3 (tạo lại container postgres bằng `.env` tạm sai mật khẩu, rồi khôi phục) |
| `-ConfirmH6 -ConfirmPostgresContainer <tên>` | Xác nhận H6 không tương tác: phải khớp **chính xác** tên container PostgreSQL (vd `etek-wcs-postgres-1`) |
| `-ConfirmX2` | Xác nhận X2 (`docker restart` backend) |
| `-OutageSeconds <n>` | Thời gian mất DB trong H6 (mặc định 120 s, theo checklist) |
| `-ImportWaitSeconds <n>` | Thời gian chờ sự kiện hiện trên `/api/logs` (mặc định 90 s; yêu cầu ≤ 60 s) |
| `-AdminUser`, `-AdminPassword` | Tài khoản Admin để đọc `/api/logs`. `-AdminPassword` là SecureString (chỉ dùng khi gọi từ phiên PowerShell; **không** truyền được qua `powershell -File`). Cách khuyến nghị: để bộ công cụ **hỏi** mật khẩu; hoặc tự động hóa bằng biến môi trường `WCS_ACCEPT_ADMIN_PASSWORD` (không để mật khẩu trong dòng lệnh; biến bị xóa ngay sau khi đọc) |
| `-BaseUrl http://localhost` | Địa chỉ web của stack (mặc định lấy `WCS_HTTP_PORT` trong `.env`) |
| `-NonInteractive` | Không hỏi gì: thao tác cần xác nhận mà thiếu cờ → CHƯA THỂ KIỂM TRA |
| `-ManualUiCheck` | H2 hỏi người vận hành có thấy sự kiện trên giao diện `/logs` không |
| `-ManualPlcToggle` | H5 dừng chờ để bạn bật/tắt kết nối PLC **thử** (PLC giả lập/thiết bị thử đã được phê duyệt) |
| `-EvidenceRoot`, `-ConfigDir`, `-LogDir`, `-Project` | Đường dẫn/ tên project (mặc định `C:\ETEK-WCS\acceptance-evidence`, `...\config`, `...\logs`, `etek-wcs`) |

Trình tự khuyến nghị:

```powershell
# 1) Kiểm tra môi trường
powershell -NoProfile -ExecutionPolicy Bypass -File docs\docker-acceptance\preflight.ps1

# 2) H1 (chạy script) + H2 + H4 + H5: không gây gián đoạn nếu stack đã chạy
powershell -NoProfile -ExecutionPolicy Bypass -File docs\docker-acceptance\run-acceptance.ps1 -Tests H1,H2,H4,H5 -AllowStackStart

# 3) H3 và H6: gây gián đoạn tạm thời -> chỉ trên stack thử, sẽ hỏi xác nhận (hoặc dùng cờ Confirm)
powershell -NoProfile -ExecutionPolicy Bypass -File docs\docker-acceptance\run-acceptance.ps1 -Tests H3,H6

# 4) Bổ sung (tùy chọn): status.ps1 và log còn sau khi restart backend
powershell -NoProfile -ExecutionPolicy Bypass -File docs\docker-acceptance\run-acceptance.ps1 -Tests X1,X2
```

Chi tiết từng bài (đúng theo checklist gốc; bộ công cụ không sửa nội dung checklist):

| Bài | Bộ công cụ làm gì | Điều kiện ĐẠT |
|---|---|---|
| **H1** | Chạy (hoặc quan sát) `deploy.ps1`/`start.ps1`; đọc `deploy-*.log` và `deployment-events-*.jsonl` | Có cả hai loại file mới; mỗi sự kiện đủ id/time/script/step/result/level/message/host; có STEP và END với exit code; **không** lộ giá trị bí mật của `.env` |
| **H2** | Đăng nhập Admin, gọi `/api/logs?category=DEPLOYMENT` sau H1; liệt kê `/deploy-logs` trong container | Đủ sự kiện của lần chạy, xuất hiện trong ≤ 60 s |
| **H3** | Tạo bản sao `.env` **tạm** với `POSTGRES_PASSWORD` sai (file thật không bị sửa) → `start.ps1` (kỳ vọng thất bại) → **tự khôi phục** bằng `.env` thật | Script thất bại; có `failure-<run>-*.log` (migrator/backend/last-command) và sự kiện FAIL (cả trên `/api/logs` sau khi khôi phục); không lộ bí mật; stack được khôi phục khỏe |
| **H4** | `docker exec ... touch /deploy-logs/<file thử>`; `docker inspect` đọc Mounts | Ghi bị từ chối, `Mounts.RW = false`, không có file nào xuất hiện trên host |
| **H5** | `docker compose logs backend`; tìm `PLC PLC-0x STATE (A -> B; IP:cổng)` | Có dòng STATE chuyển trạng thái kèm IP. Nếu stack không có PLC/giả lập để bật/tắt → **CHƯA THỂ KIỂM TRA** (bộ công cụ không tự bật PLC) |
| **H6** | Xác định container PostgreSQL, xin xác nhận, `docker stop` → chờ → `docker start` (luôn khởi động lại, kể cả khi lỗi/Ctrl-C) | `/healthz` 503 → 200; đúng 1 DB_LOST và 1 DB_RESTORED (log + `/api/logs`); lỗi scheduler lặp được throttle; volume và dữ liệu còn |
| X1 | `status.ps1` | Mỗi container có `restarts=` và `oom=`; có health probe cuối |
| X2 | `docker restart` backend (cần xác nhận) | File log còn nguyên, sự kiện DEPLOYMENT không mất/không nhân đôi |

## 5. Cách đọc kết quả

- Màn hình in từng bước kèm giờ và bảng tổng hợp cuối. **Exit code:** `0` tất cả bài đã chọn ĐẠT; `1` có bài KHÔNG ĐẠT; `2` không có bài KHÔNG ĐẠT nhưng còn bài CHƯA THỂ KIỂM TRA; `3` lỗi tham số/ghi bằng chứng.
- Trạng thái chỉ gồm: **ĐẠT**, **KHÔNG ĐẠT**, **CHƯA THỂ KIỂM TRA**. ĐẠT chỉ khi có bằng chứng thực tế; thiếu điều kiện (không có Docker, không đăng nhập được Admin, thiếu xác nhận, chưa có sự kiện PLC…) → CHƯA THỂ KIỂM TRA kèm nguyên nhân.
- Báo cáo: `acceptance-report.md` trong thư mục bằng chứng (điền từ `report-template.md`); dữ liệu máy đọc: `results.json`.

## 6. Thu thập bằng chứng

Mỗi lần chạy tạo thư mục `C:\ETEK-WCS\acceptance-evidence\<yyyyMMdd-HHmmss>-acceptance\` (**ngoài repository, không commit**):

```
acceptance-report.md     báo cáo
results.json             kết quả từng bài
commands-all.log         mọi lệnh đã chạy: thời điểm, lệnh, exit code (stdout/stderr nằm trong commands.log của từng bài)
H1\ ... H6\ X1\ X2\      steps.log (bước có giờ), commands.log (lệnh + exit code + stdout/stderr), file bằng chứng riêng
_pre\  _post\            trạng thái trước/sau (container, RestartCount, OOMKilled, mount, log ứng dụng, log triển khai)
```

`collect-evidence.ps1 -OutDir <thư mục>` có thể chạy riêng bất cứ lúc nào (chỉ đọc). **Không bao giờ** ghi mật khẩu/token/cookie/connection string: giá trị bí mật trong `.env` và mật khẩu Admin được che trong mọi file; không lưu `Config.Env` của container; `.env` chỉ liệt kê tên biến.

## 7. An toàn

- Không xóa Docker volume, không xóa database, **không** `docker system prune`, **không** `docker compose down`, không `docker rm/kill`.
- Không tự dừng/khởi động lại container đang chạy. Chỉ H3, H6, X2 làm gián đoạn và cần xác nhận rõ ràng (cờ `-Confirm...` hoặc gõ đúng cụm xác nhận). H6 chỉ dừng container được **xác định chính xác** (label project/service, image `postgres:16`, volume `etek_wcs_pgdata`, không publish cổng) và gõ đúng `STOP <tên container>`.
- **Database `wcs_db` của máy phát triển** (dịch vụ PostgreSQL trên host, cổng 5432) **không bị tác động**: bộ công cụ chỉ làm việc với container trong stack Docker.
- Không kết nối/gửi lệnh tới PLC/RCS/AGV thật; từ chối chạy nếu `.env` có `PLC_ALLOW_LIVE_CONNECTION=true`.
- Không sửa mã nguồn hay các module nghiệp vụ được bảo vệ.

## 8. Khôi phục sau kiểm thử

1. `deployment\scripts\status.ps1`: `postgres`, `backend`, `frontend` đều chạy và khỏe.
2. Nếu H3/H6/X2 bị gián đoạn giữa chừng (mất điện, đóng cửa sổ): chạy `docker start etek-wcs-postgres-1` (nếu đang dừng) rồi `deployment\scripts\start.ps1`. **Không** dùng `docker compose down -v`.
3. Kiểm tra `docker volume ls`: `etek_wcs_pgdata`, `etek_wcs_keys` còn nguyên; `.env` thật không bị sửa (H3 chỉ dùng bản sao tạm).
4. Thư mục `...\H3\tmp-config` (bản sao `.env` tạm) được tự xóa; nếu còn thì xóa thủ công.
5. Lưu thư mục bằng chứng ở nơi hạn chế quyền; đính kèm `acceptance-report.md` vào hồ sơ nghiệm thu (không đưa `.env` hay mật khẩu vào hồ sơ).

## 9. Xử lý sự cố

| Triệu chứng | Cách xử lý |
|---|---|
| Hầu hết bài là CHƯA THỂ KIỂM TRA | Chạy `preflight.ps1`; thường do Docker daemon chưa chạy, thiếu `.env`, hoặc chưa đăng nhập Admin được |
| Không đăng nhập được API (HTTP 401) | Cookie `Secure` không được gửi qua HTTP: đặt `WCS_COOKIE_SECURE=SameAsRequest` (LAN thử) hoặc dùng `-BaseUrl https://...`; hoặc Admin chưa đổi mật khẩu lần đầu |
| H2 quá 60 s | Importer đọc thư mục log mỗi 30 s; kiểm tra mount `/deploy-logs` (H4) và `DeploymentLogs__Directory` |
| H5 CHƯA THỂ KIỂM TRA | Stack không có PLC/giả lập hợp lệ để bật/tắt: cần PLC thử đã được kỹ sư PLC phê duyệt (dùng `-ManualPlcToggle`) |
| H6 báo không xác định chắc chắn container | Container không khớp tiêu chí an toàn (label/image/volume/cổng): kiểm tra stack có đúng là `etek-wcs` |

Sau khi có kết quả, người xem xét đọc `acceptance-report.md` và bằng chứng rồi quyết định nghiệm thu; không có bước nào trong bộ công cụ tự merge hoặc thay đổi trạng thái checklist.
