# Server Resource Monitoring trên Overview

Ngày: 2026-11-17 · Branch `develop` · nền `adfdd08`. Không đổi nghiệp vụ WCS, PLC, RCS, Scheduler, TaskStateMachine, không có migration, không đổi Docker Compose.

> **Tuyên bố phạm vi kiểm chứng (đọc trước):** toàn bộ kiểm thử thực tế được chạy trên **Windows 11 Pro 10.0.22631, backend chạy trực tiếp (không trong Docker)**. Máy này **không có Docker và không có WSL**. Vì vậy:
> - Số đo **máy chủ Windows** (CPU, RAM, ổ đĩa, thời gian chạy, số bộ xử lý) đã được lấy thật và đối chiếu với chính Windows.
> - Số đo **container** (cgroup v1/v2) và **Linux (/proc, /sys)** đã được cài đặt và kiểm thử bằng cây tệp mô phỏng đúng định dạng kernel, **chưa được kiểm chứng trong một container hay máy Linux thật**. Không được hiểu là "đã hỗ trợ thực tế Docker/Linux".
> - **Nhiệt độ CPU không đọc được** trên máy này (hiển thị N/A kèm lý do).
> - Với cấu hình triển khai chính (Windows 11 + Docker Desktop, container Linux) số đo **máy chủ Windows thật chưa có** (cần Host Agent, chưa cài). Level 2 **chưa hoàn thành** cho cấu hình đó.

## 1. Giao diện

Khu vực **"Giám sát tài nguyên máy chủ"** nằm ngay trong trang Overview (Dashboard) hiện có, dưới hai hàng KPI, chỉ dành cho **Admin** và không hiển thị trong Demo Mode. Không có gì ở Header, Footer hay Sidebar, không có trang riêng.

- Hai tab: **Container WCS** và **Máy chủ** (đổi thành **VM Docker (Linux)** khi chạy trên Docker Desktop). Mặc định là tab Container; nếu WCS không chạy trong container thì tự chọn tab Máy chủ vì tab Container không có gì để hiển thị.
- 4 thẻ KPI: CPU, RAM, Ổ đĩa, và (Container) Thời gian chạy / (Máy chủ) Nhiệt độ CPU. Mỗi thẻ có giá trị, thanh tiến độ, chú thích (đã dùng / tổng / còn lại), nhãn nguồn (Container / VM Docker / Máy chủ) và nhãn trạng thái.
- Hai biểu đồ lịch sử CPU và RAM (thang 0–100%, ~10 phút, lấy từ lịch sử ở máy chủ). Chỗ thiếu số đo là **khoảng trống**, không nối đường và không vẽ thành 0.
- Khối Thông tin hệ thống: tên máy (hoặc mã container), hệ điều hành, kiến trúc, môi trường container, nguồn số liệu máy chủ, thời gian chạy, thời điểm cập nhật gần nhất, chu kỳ lấy mẫu.
- Trạng thái: **Đang tải, Bình thường, Cảnh báo, Nghiêm trọng, Không khả dụng** (kèm lý do), cộng trạng thái kết nối **Trực tiếp / Mất kết nối / Đã tắt**; banner cảnh báo khi vượt ngưỡng.
- Trong khung "Trực tiếp" có thêm **Backend WCS** (đang chạy + thời gian chạy) và **PostgreSQL** (đã kết nối + độ trễ ms), tách biệt với PLC, RCS và AGV (mọi vai trò đều thấy). Trạng thái "Chưa kết nối" / "Chế độ mô phỏng" của PLC và RCS giữ nguyên: backend chạy không có nghĩa thiết bị online. MQTT: mã nguồn không dùng nên không hiển thị.
- VI/EN, Light/Dark dùng chung token; responsive 4 → 2 → 1 cột.

## 2. Kiến trúc

```
IContainerMetricsProvider ── CgroupContainerMetricsProvider (Linux: cgroup v2/v1 + /proc)   | không phải container → Unavailable
IHostMetricsProvider      ── LinuxHostMetricsProvider (/proc, /sys hwmon/thermal)
                          ── WindowsHostMetricsProvider (kernel32: GetSystemTimes, GlobalMemoryStatusEx, GetTickCount64)
                          ── Unavailable (nền tảng khác / Windows container: cần Host Agent)
MetricsProviderFactory    chọn theo hệ điều hành (không có logic OS ở nơi khác)
ResourceMonitor           MỘT bộ lấy mẫu nền (5 s), áp ngưỡng, giữ lịch sử trong bộ nhớ; API chỉ trả mẫu mới nhất
GET /api/system/resources Admin; /api/system/health mở rộng (chỉ sức khỏe ứng dụng)
```

- **Tách nguồn**: mỗi số đo mang `source` = `Container` / `DockerVm` / `Host`. Trên Docker Desktop (kernel `microsoft…WSL2`/`linuxkit`), số đo /proc là của **VM Linux**, được gắn nhãn `DockerVm` và bật cờ `hostAgentRequired`; máy Windows thật không bị thay thế bằng VM, container không được dùng thay máy chủ. Trong container, tên máy là mã container và được hiển thị đúng như vậy.
- **Mỗi metric** có `value`, `unit`, `source`, `status`, `lastUpdatedAt`, `reason` (mã ổn định, đã dịch VI/EN khi Không khả dụng).
- **Ngưỡng do server quyết định** (cấu hình `ResourceMonitoring`, mặc định CPU/RAM ≥ 80 Cảnh báo, ≥ 95 Nghiêm trọng; Ổ đĩa ≥ 80 / ≥ 90). **Nhiệt độ không có ngưỡng chung** trừ khi cấu hình `TemperatureWarningC/CriticalC`.
- **Số đo cgroup**: CPU = chênh lệch `usage_usec` / thời gian thực / số lõi được cấp (`cpu.max`, không giới hạn thì số CPU container thấy); RAM = `memory.current − inactive_file` (như `docker stats`) so với `memory.max`; không đặt giới hạn thì tổng và % là Không khả dụng (`NO_MEMORY_LIMIT`) nhưng vẫn hiện RAM đang dùng; uptime = uptime hệ thống − thời điểm khởi động PID 1; ổ đĩa = volume chứa dữ liệu (nhìn từ container). Bộ đếm CPU lùi (reset) không được báo thành số.
- **Không đặc quyền**: không `privileged`, không mount Docker socket, không mount hệ thống tệp host; chỉ đọc tệp container đã thấy. Compose **không phải đổi**.
- **Dữ liệu cũ không giả làm dữ liệu mới**: (1) server: mẫu quá 3 chu kỳ + 2 s → mọi metric `Unavailable / SAMPLER_STALLED`, `value = null`; (2) trình duyệt: dữ liệu quá hạn, yêu cầu lỗi/treo, hoặc `sampleAgeSeconds` quá cũ → trạng thái **Mất kết nối**, thẻ hiện "—" và banner ghi lần cập nhật cuối. Yêu cầu bị hủy sau 8 s để vòng polling không bao giờ kẹt.
- **API**: `/api/system/health` được **mở rộng** (thêm `backend.uptimeSeconds`, `database.latencyMs`; các trường cũ giữ nguyên, không có CPU/RAM trong đó); `/api/system/resources` là endpoint mới vì cần lịch sử và quyền Admin riêng, không trùng chức năng. Một vòng polling duy nhất ở trang Overview (theo chu kỳ máy chủ, 3–30 s, tạm dừng khi tab ẩn); không polling độc lập khác.

## 3. Thiết kế Host Agent cho Docker Desktop trên Windows (chưa cài đặt)

Mục tiêu: lấy số đo của **máy Windows thật** khi WCS chạy trong container Linux của Docker Desktop.

- **Kiến trúc**: một dịch vụ nhỏ chạy **trực tiếp trên Windows** (tái sử dụng `WindowsHostMetricsProvider`, hoặc `windows_exporter`), trả JSON chỉ-đọc cùng định dạng số đo. Backend trong container gọi `http://host.docker.internal:<cổng>/metrics` bằng Bearer token; thêm `HostMetrics:Mode=Agent`, `AgentUrl`, `AgentToken` (đưa qua `.env`/secret, không đưa vào image).
- **Quyền & bảo mật**: dịch vụ chạy bằng tài khoản dịch vụ ít đặc quyền (không cần Administrator cho CPU/RAM/ổ đĩa/uptime); chỉ lắng nghe trên giao diện mà Docker truy cập được và quy tắc tường lửa chỉ cho dải mạng Docker; có xác thực token, chỉ GET, không có thao tác ghi; không mở ra LAN. Không `privileged`, không Docker socket, không mount ổ đĩa Windows vào container.
- **Nhiệt độ CPU** trên Windows cần cảm biến của nhà sản xuất/driver (thường cần quyền Administrator): để Agent tùy chọn và mặc định Không khả dụng, không ước lượng.
- **Compose**: chỉ thêm biến môi trường cho backend (và `extra_hosts` nếu cần trên Linux); không đổi mạng `db_net`.
- **Chi phí**: một tiến trình nhẹ (đọc vài lời gọi hệ thống mỗi 5 s, cỡ vài chục MB RAM); phía backend thêm một lời gọi HTTP cục bộ mỗi chu kỳ.
- Cho đến khi có Agent, tab Máy chủ trên Docker Desktop hiển thị số đo của **VM Docker** (gắn nhãn rõ) kèm thông báo "số liệu máy chủ thật cần Host Agent (chưa cài đặt)".

## 4. Kiểm thử và môi trường

| Hạng mục | Kết quả | Môi trường |
|---|---|---|
| Backend toàn bộ | **650 đạt, 0 lỗi** (609 trước + 41 mới) | Windows 11, .NET 10, PostgreSQL |
| Provider container (cgroup v2, v1, không giới hạn, không trong container, thiếu cgroup, bộ đếm lùi, uptime với tên lệnh có ngoặc) | đạt | **cây tệp mô phỏng**, không phải container thật |
| Provider Linux host (/proc, MemAvailable, iowait, hwmon: gói/ lõi, cảm biến không phải CPU, giá trị vô lý, không có cảm biến, nhãn VM Docker/Host) | đạt | **cây tệp mô phỏng** |
| Provider Windows host | đạt, đối chiếu số liệu thật | **Windows 11 thật** |
| Ngưỡng, nhiệt độ không có ngưỡng chung, lịch sử giới hạn + khoảng trống, mẫu cũ, chưa lấy mẫu, tắt giám sát, provider ném lỗi, số vô hạn | đạt | đơn vị |
| API: chỉ Admin (Operator 403, ẩn danh 401), cấu trúc metric, container ≠ host, health mở rộng | đạt | tích hợp có PostgreSQL |
| Logic giao diện (`monitor:test`) | 23 đạt | Node |
| Trình duyệt Edge, chế độ thường | **39/39 đạt** | stack cô lập |
| Trình duyệt Edge, chế độ cảnh báo (API ngưỡng thấp) | 6/6 đạt | stack cô lập |
| ESLint, `tsc -b`, `npm run build`, `i18n:check` (1215 khóa), `i18n:test` 76, `dashboard:test` 18, `preferences:test` 10 | sạch / đạt | — |

**Đối chiếu số đo thật (Windows, cùng thời điểm):** RAM 49,08% so với 49,1% của hệ điều hành; tổng RAM 34.138.931.200 B = 32.557 MB của Windows; CPU 1,2% so với bộ đếm hiệu năng 0,9%; ổ C: 311,8 GB dùng / 188,4 GB trống tương ứng 290 / 175 GiB; uptime 11,7 giờ khớp thời điểm khởi động Windows; 16 bộ xử lý logic. Trên màn hình: RAM 49,8% so với API 49,9%, ổ đĩa 62,3% = 62,3%.

**Trình duyệt đã kiểm tra:** cấu trúc và vị trí (không ở Header/Sidebar/Footer, Overview cũ còn nguyên 8 KPI và các biểu đồ); 4 thẻ với nguồn "Máy chủ"; nhiệt độ N/A + lý do; tab Container hiện N/A + lý do `NOT_IN_CONTAINER` và **không lọt số của máy chủ**; mất kết nối (giả lập từ chối yêu cầu) → banner + "—" + khôi phục; yêu cầu treo vĩnh viễn → Mất kết nối rồi hồi phục (phát hiện và sửa lỗi vòng polling bị kẹt); mẫu cũ được server báo → không hiện như dữ liệu hiện tại; Operator không thấy khu vực và API 403; VI/EN (EN không còn tiếng Việt), Light/Dark (0 chữ thiếu tương phản, 0 vùng nền sáng ở Dark); 24 tổ hợp độ rộng (1440/1024/760) × Expanded/Collapsed × theme × ngôn ngữ không tràn; Demo Mode không có khu vực và không gọi API; polling 6 yêu cầu/30 s.

**Chi phí đo thực (backend chạy 90 giây, không có trình duyệt):** giám sát tắt 281 ms CPU, bật (5 s) 328 ms CPU, tức thêm **≈ 47 ms / 90 s ≈ 0,05% một lõi**; RAM tiến trình không khác biệt đáng kể (163 so với 162 MB; lịch sử 120 điểm rất nhỏ). Một lần lấy mẫu mất vài ms. Mỗi lần polling trả khoảng **22 KB JSON** (chủ yếu là lịch sử), tức ≈ 4,4 KB/s cho mỗi trang Overview đang mở của Admin.

## 5. Trạng thái các Level

| Hạng mục | Trạng thái |
|---|---|
| Level 1 – Backend Health (thời gian chạy), PostgreSQL Health (kết nối + độ trễ) | **Hoàn thành, đã kiểm chứng** |
| Level 1 – CPU, RAM, Uptime, ổ đĩa của **container** | **Đã cài đặt (cgroup v1/v2), đã kiểm thử bằng dữ liệu mô phỏng, CHƯA kiểm chứng trong container thật** |
| Level 2 – CPU, RAM, ổ đĩa, uptime của **máy chủ** | **Đã kiểm chứng trên Windows chạy trực tiếp.** Trên Docker Desktop: chưa có (cần Host Agent). Linux Docker Engine: đã cài đặt, chưa kiểm chứng |
| Level 2 – Nhiệt độ CPU | **Không đọc được** trên máy kiểm thử (N/A + lý do); đọc hwmon/thermal đã kiểm thử bằng dữ liệu mô phỏng, chưa trên Linux thật |

## 6. Cần thực hiện để đóng các điểm còn lại

1. Dựng stack Docker thật (Docker Desktop và, nếu có, Linux Docker Engine) rồi chạy lại: tab Container (CPU/RAM/uptime/ổ đĩa của container, so sánh với `docker stats`), tab VM Docker, và nhãn nguồn.
2. Quyết định có làm Host Agent cho Windows hay không (mục 3).
3. Quyết định ngưỡng nhiệt độ theo phần cứng thực tế sau khi biết cảm biến có đọc được không.

## 7. Hạn chế

- Chỉ kiểm tra giao diện trên Edge. Chưa có ảnh chụp của trạng thái container thật vì không có container.
- Mỗi lần polling gửi cả lịch sử (~22 KB). Chấp nhận được trong LAN; có thể rút gọn bằng nén hoặc chỉ gửi phần mới nếu cần.
- Nếu container không đặt giới hạn bộ nhớ thì chỉ có RAM đang dùng (không có % và ngưỡng). Compose hiện đặt giới hạn mặc định `WCS_BACKEND_MEMORY=3g` và `WCS_BACKEND_CPUS=3`, nên trường hợp này không xảy ra với cấu hình chuẩn.
- Khối "Thông tin hệ thống" hiển thị tên máy và hệ điều hành cho Admin; Operator không thấy.
