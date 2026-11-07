# Hướng Dẫn Sử Dụng Thư Viện `MitsubishiMc.Client`

> **Thư viện giao tiếp PLC Mitsubishi qua MC Protocol 3E Binary**  
> Dùng cho: Q-Series, iQ-R, iQ-F (FX5U, FX5UC), iQ-L  
> Nền tảng: **.NET Framework 4.8**  
> Phụ thuộc bên ngoài: **Không có** (zero NuGet)

---

## 1. Bàn Giao — File / Folder Cần Nhận

Người nhận chỉ cần **1 trong 2 lựa chọn** dưới đây:

### Lựa chọn A — Nhận file DLL đã biên dịch (Khuyến nghị)

```
distribute/
├── MitsubishiMc.Client.dll      ← Reference vào project của bạn
├── MitsubishiMc.Client.pdb      ← (Tuỳ chọn) Debug symbols, hỗ trợ stack trace
└── HUONG_DAN_SU_DUNG.md         ← File hướng dẫn này
```

**Cách thêm vào project:**  
1. Copy `MitsubishiMc.Client.dll` vào thư mục `lib\` trong project của bạn.  
2. Trong Visual Studio: Chuột phải vào **References** → **Add Reference** → **Browse** → chọn file `.dll`.  
3. Thêm `using MitsubishiMc.Client;` vào đầu file C# cần dùng.

### Lựa chọn B — Nhận mã nguồn (thêm vào cùng Solution)

```
MitsubishiMc.Client/
├── MitsubishiMc.Client.csproj
├── McClient.cs                  ← Class client chính
├── McDeviceType.cs              ← Enum loại thiết bị (D, W, M, X, Y)
├── McProtocolSettings.cs        ← Cấu hình kết nối
├── McReadResult.cs              ← Kết quả đọc dữ liệu
├── McWriteResult.cs             ← Kết quả ghi dữ liệu
└── Internal/
    ├── McFrameBuilder.cs        ← Nội bộ: đóng gói 3E frame
    └── McResponseParser.cs      ← Nội bộ: giải mã phản hồi PLC
```

**Cách thêm vào Solution:**  
1. Trong Visual Studio: **File** → **Add** → **Existing Project** → chọn `MitsubishiMc.Client.csproj`.  
2. Chuột phải vào project của bạn → **Add** → **Reference** → tích chọn `MitsubishiMc.Client`.

---

## 2. Yêu Cầu Phần Cứng & Mạng

| Mục                | Yêu cầu |
|--------------------|---------|
| PLC Mitsubishi     | Q-Series, iQ-R, iQ-F (FX5U/FX5UC), iQ-L có cổng Ethernet |
| Cấu hình PLC       | GX Works2/3: Mở 1 Connection với Protocol = **MC Protocol (Binary)**, Mode = **TCP** |
| Cổng mặc định      | `5000` (Q/FX5) hoặc `5007` (iQ-R) — xem trong GX Works (hiển thị Hex: `0x1388` = 5000) |
| Mạng               | Máy tính và PLC phải cùng Subnet (ví dụ: PC `192.168.3.100`, PLC `192.168.3.39`, Mask `255.255.255.0`) |
| Firewall           | Mở cổng TCP tương ứng trên máy tính |

---

## 3. Khởi Tạo Client

```csharp
using MitsubishiMc.Client;

// Cấu hình tối thiểu (đơn giản nhất)
var settings = new McProtocolSettings
{
    IpAddress = "192.168.3.39",   // IP của Ethernet module PLC
    Port      = 5000,             // Cổng TCP (5000 cho Q/FX5, 5007 cho iQ-R)
    TimeoutMs = 3000              // Timeout đọc/ghi (ms)
};

// CpuMonitoringTimerUnits = 0 (mặc định): tự động đồng bộ với TimeoutMs
// Nếu cần ghi đè thủ công: settings.CpuMonitoringTimerUnits = 12; // 12 × 250ms = 3000ms

var client = new McClient(settings);
```

> **Lưu ý:** `McClient` implement `IDisposable`. Hãy luôn dùng trong khối `using` hoặc gọi `Dispose()` khi kết thúc.

---

## 4. Kết Nối Tới PLC

```csharp
try
{
    await client.ConnectAsync();
    Console.WriteLine("Kết nối thành công!");
}
catch (TimeoutException ex)
{
    // Không thể mở TCP trong TimeoutMs — sai IP/Port, PLC off, cáp đứt
    Console.WriteLine($"Timeout kết nối: {ex.Message}");
}
catch (SocketException ex)
{
    // Lỗi mạng cụ thể (Connection Refused, Host Unreachable, v.v.)
    Console.WriteLine($"Lỗi mạng: {ex.SocketErrorCode} — {ex.Message}");
}
catch (OperationCanceledException)
{
    // Người dùng hủy qua CancellationToken
    Console.WriteLine("Kết nối bị hủy.");
}
```

**Kiểm tra trạng thái sau kết nối:**

```csharp
bool isConnected = client.IsConnected;   // true = socket TCP đang mở
bool isHealthy   = client.IsHealthy;     // true = kết nối + giao tiếp gần đây còn sống
DateTime? last   = client.LastSuccessfulCommunicationUtc; // Thời điểm giao tiếp gần nhất
```

**Health Check (ping thực tế tới PLC):**

```csharp
bool plcResponding = await client.PingAsync();
if (!plcResponding)
{
    Console.WriteLine("PLC không phản hồi, cần kiểm tra kết nối!");
}
```

---

## 5. Đọc Dữ Liệu

### 5.1. Đọc Word — Thanh ghi D hoặc W

```csharp
// Đọc D100 đến D104 (5 word)
McReadResult result = await client.ReadWordsAsync(McDeviceType.D, startAddress: 100, count: 5);

if (result.Success)
{
    ushort d100 = result.WordValues[0];  // D100
    ushort d101 = result.WordValues[1];  // D101
    ushort d104 = result.WordValues[4];  // D104

    Console.WriteLine($"D100 = {d100}");
    Console.WriteLine($"D101 = {d101}");
}
else
{
    Console.WriteLine($"Đọc thất bại: {result.ErrorMessage}");
    Console.WriteLine($"End Code PLC: 0x{result.EndCode:X4}");
    // Xem Phần 7 để tra cứu EndCode
}
```

```csharp
// Đọc W10 đến W14 (5 word Link Register)
McReadResult resultW = await client.ReadWordsAsync(McDeviceType.W, startAddress: 10, count: 5);
```

**Giới hạn:** Tối đa **960 word/lần** cho mỗi request.

---

### 5.2. Đọc Bit — Cờ M, X, Y

```csharp
// Đọc M0 đến M15 (16 bit)
McReadResult result = await client.ReadBitsAsync(McDeviceType.M, startAddress: 0, count: 16);

if (result.Success)
{
    bool m0  = result.BitValues[0];   // M0
    bool m1  = result.BitValues[1];   // M1
    bool m15 = result.BitValues[15];  // M15

    Console.WriteLine($"M0 = {(m0 ? "ON" : "OFF")}");
}
```

```csharp
// Đọc X — Lưu ý: Địa chỉ X/Y truyền theo thập phân (không phải bát phân trong tài liệu PLC)
// X10 (octal trong PLC manual) = 8 (decimal)
McReadResult xResult = await client.ReadBitsAsync(McDeviceType.X, startAddress: 8, count: 8);
//                                                   X10 (octal) ↑

if (xResult.Success)
{
    bool x10 = xResult.BitValues[0];  // X10 (octal) = index 0
}
```

**Giới hạn:** Tối đa **3584 bit/lần** cho mỗi request.

---

## 6. Ghi Dữ Liệu

> ⚠️ **Quy tắc bắt buộc:** Luôn kiểm tra `result.IsAmbiguous` sau mỗi lệnh ghi.  
> Khi `IsAmbiguous = true`, **TUYỆT ĐỐI KHÔNG tự động retry** — PLC có thể đã thực thi lệnh.

### 6.1. Ghi Word — Thanh ghi D hoặc W

```csharp
// Ghi D200 = 1500, D201 = 2500
ushort[] dataToWrite = new ushort[] { 1500, 2500 };

McWriteResult result = await client.WriteWordsAsync(McDeviceType.D, startAddress: 200, values: dataToWrite);

if (result.Success)
{
    Console.WriteLine("Ghi thành công.");
}
else if (result.IsAmbiguous)
{
    // Timeout SAU KHI đã gửi lệnh xuống PLC
    // Không biết PLC có thực thi hay không — KHÔNG retry
    Console.WriteLine("CẢNH BÁO: Kết quả ghi không xác định do mất kết nối!");
    Console.WriteLine("Cần đọc lại thanh ghi D200-D201 để xác nhận trạng thái thực tế.");
}
else
{
    Console.WriteLine($"Ghi thất bại: {result.ErrorMessage}");
    Console.WriteLine($"End Code PLC: 0x{result.EndCode:X4}");
}
```

**Giới hạn:** Tối đa **960 word/lần** cho mỗi request.

---

### 6.2. Ghi Bit — Cờ M hoặc Y

```csharp
// Ghi M50 = ON (true), M51 = OFF (false), M52 = ON (true)
bool[] bitsToWrite = new bool[] { true, false, true };

McWriteResult result = await client.WriteBitsAsync(McDeviceType.M, startAddress: 50, values: bitsToWrite);

if (result.Success)
{
    Console.WriteLine("Ghi M50-M52 thành công.");
}
```

```csharp
// Ghi Y — Địa chỉ theo thập phân (Y20 octal = 16 decimal)
McWriteResult yResult = await client.WriteBitsAsync(McDeviceType.Y, startAddress: 16, values: new bool[] { true });
//                                                      Y20 (octal) ↑
```

> 🔒 **Khóa an toàn phần cứng:** Thư viện từ chối tuyệt đối lệnh ghi vào thiết bị `X`  
> (X = Input Relay = cảm biến vật lý đầu vào, không được phép ghi theo thiết kế phần cứng).  
> Lệnh `WriteBitsAsync(McDeviceType.X, ...)` sẽ trả về `Fail` ngay lập tức, không gửi frame xuống PLC.

**Giới hạn:** Tối đa **1440 bit/lần** cho mỗi request.

---

## 7. Xử Lý Lỗi

### 7.1. Phân loại lỗi theo `ErrorMessage`

| Tình huống | Dấu hiệu trong `ErrorMessage` | Xử lý |
|------------|-------------------------------|--------|
| Chưa gọi `ConnectAsync` | "Chưa kết nối tới PLC" | Gọi `ConnectAsync()` trước |
| PLC tắt nguồn / rút cáp | "Lỗi Socket", "Lỗi I/O" | Reconnect, giám sát `IsHealthy` |
| Timeout đọc | "Timeout quá X ms khi đọc" | Tăng `TimeoutMs`, kiểm tra tải PLC |
| Timeout ghi | `IsAmbiguous = true` | ⚠️ Không retry, đọc lại để xác nhận |
| Địa chỉ ngoài phạm vi PLC | EndCode `0xC05B` | Kiểm tra lại địa chỉ device |
| Số điểm không hợp lệ | EndCode `0xC05C` | Kiểm tra `count` hoặc số phần tử `values` |
| PLC từ chối truy cập | EndCode `0x4031` | Kiểm tra quyền truy cập trong GX Works |
| Command sai | EndCode `0x0055` | Lỗi thư viện, liên hệ người phát triển |

### 7.2. Tra cứu EndCode đầy đủ

```
0x0000 — Thành công
0x0055 — Command/Subcommand không hợp lệ
0xC050 — Lỗi thông số Ethernet
0xC051 — Số điểm vượt giới hạn frame
0xC056 — Device code không tồn tại (sai loại thiết bị)
0xC059 — Thiết bị chỉ đọc (Read-Only)
0xC05B — Địa chỉ thiết bị vượt phạm vi PLC
0xC05C — Số lượng điểm không hợp lệ
0x4031 — Không có quyền truy cập bộ nhớ
```

Tra cứu đầy đủ trong: **MELSEC Communication Protocol Reference Manual (SH-080008)**.

### 7.3. Thông tin chẩn đoán chi tiết (Hex dump)

Mọi kết quả đọc/ghi đều kèm theo hex dump của frame truyền thông:

```csharp
var result = await client.ReadWordsAsync(McDeviceType.D, 100, 5);

Console.WriteLine($"Request gửi đi : {result.RequestHex}");
// Ví dụ: 50 00 00 FF FF 03 00 0C 00 0C 00 01 04 00 00 64 00 00 A8 05 00

Console.WriteLine($"Response nhận về: {result.ResponseHex}");
// Ví dụ: D0 00 00 FF FF 03 00 0C 00 00 00 E8 03 D0 07 00 00 ...
```

Dán các chuỗi hex này vào ticket kỹ thuật để hỗ trợ chẩn đoán nhanh tại hiện trường.

---

## 8. Mẫu Vòng Lặp Giám Sát (Polling Loop)

Dưới đây là pattern chuẩn cho ứng dụng AGV hoặc SCADA giám sát định kỳ:

```csharp
public async Task RunPlcMonitoringLoop(CancellationToken stoppingToken)
{
    var settings = new McProtocolSettings
    {
        IpAddress = "192.168.3.39",
        Port = 5000,
        TimeoutMs = 2000
    };

    using (var client = new McClient(settings))
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // 1. Đảm bảo kết nối
            if (!client.IsConnected)
            {
                try
                {
                    await client.ConnectAsync(stoppingToken);
                    Console.WriteLine("Đã kết nối lại PLC.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Chưa kết nối được: {ex.Message}. Thử lại sau 5 giây...");
                    await Task.Delay(5000, stoppingToken);
                    continue;
                }
            }

            // 2. Đọc dữ liệu trạng thái
            var statusResult = await client.ReadWordsAsync(McDeviceType.D, startAddress: 0, count: 10, stoppingToken);
            if (statusResult.Success)
            {
                ushort agvSpeed    = statusResult.WordValues[0]; // D0
                ushort agvStatus   = statusResult.WordValues[1]; // D1
                ushort batteryLevel= statusResult.WordValues[2]; // D2

                // Xử lý dữ liệu...
                Console.WriteLine($"Speed={agvSpeed}, Status={agvStatus}, Battery={batteryLevel}%");
            }
            else
            {
                Console.WriteLine($"Lỗi đọc chu kỳ: {statusResult.ErrorMessage}");
                // IsConnected sẽ tự động false, vòng lặp sau sẽ reconnect
            }

            // 3. Chờ chu kỳ tiếp theo (100ms)
            await Task.Delay(100, stoppingToken);
        }
    }
}
```

---

## 9. Lưu Ý Địa Chỉ X/Y (Quan Trọng)

| Địa chỉ trong tài liệu PLC | Địa chỉ truyền vào thư viện |
|----------------------------|-----------------------------|
| X0 | 0 |
| X7 | 7 |
| X10 | 8 |
| X17 | 15 |
| X20 | 16 |
| Y0 | 0 |
| Y10 | 8 |
| Y20 | 16 |

**Công thức chuyển đổi:**  
`startAddress (decimal) = OctalToDecimal(địa_chỉ_trong_GX_Works)`

---

## 10. Giới Hạn Kỹ Thuật

| Thao tác | Giới hạn tối đa / request |
|----------|--------------------------|
| ReadWordsAsync (D, W) | 960 word |
| ReadBitsAsync (M, X, Y) | 3584 bit |
| WriteWordsAsync (D, W) | 960 word |
| WriteBitsAsync (M, Y) | 1440 bit |
| Ghi vào X | ❌ Bị chặn hoàn toàn |

---

## 11. Thread Safety

Thư viện **an toàn đa luồng** (thread-safe). Có thể gọi đồng thời từ nhiều `Task` trên cùng 1 instance `McClient` — các request sẽ được tự động tuần tự hóa qua `SemaphoreSlim(1,1)`, đảm bảo không xảy ra xen kẽ byte trên cùng 1 socket TCP.

```csharp
// Ví dụ: 2 luồng cùng dùng 1 client — an toàn
var t1 = Task.Run(() => client.ReadWordsAsync(McDeviceType.D, 0, 10));
var t2 = Task.Run(() => client.ReadBitsAsync(McDeviceType.M, 0, 16));
var results = await Task.WhenAll(t1, t2);
```

---

## 12. Thông Tin Liên Hệ & Phiên Bản

| Mục | Thông tin |
|-----|-----------|
| Phiên bản | 1.1.0 (Nov 2026) |
| Target Framework | .NET Framework 4.8 |
| Protocol | MELSEC MC Protocol 3E Binary (SH-080008) |
| PLC được test | Q13UDV, FX5U, iQ-R |
| Tác giả | AGV T10-2026 Team |

---

*File này được tạo tự động. Vui lòng giữ nguyên cùng file DLL.*
