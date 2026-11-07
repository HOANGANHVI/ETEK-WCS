# Phân tích thư viện MC Protocol tham chiếu và đối chiếu với driver ETEK WCS (CR-003)

| Mục | Giá trị |
|---|---|
| Change Request | CR-003 |
| Nguồn tham chiếu | `reference/Kết nối MCprotocol.zip` → thư viện **MitsubishiMc.Client v1.1.0** (.NET Framework 4.8, C# 7.3, không có NuGet) |
| Bản giải nén | `reference/extracted/Kết nối MCprotocol/` (ZIP gốc được giữ nguyên) |
| Phạm vi đọc | Toàn bộ 9 file: `McClient.cs`, `McProtocolSettings.cs`, `McDeviceType.cs`, `McReadResult.cs`, `McWriteResult.cs`, `Internal/McFrameBuilder.cs`, `Internal/McResponseParser.cs`, `Properties/AssemblyInfo.cs`, `MitsubishiMc.Client.csproj` và `HUONG_DAN_SU_DUNG.md` |
| Trạng thái | SOURCE CODE IMPLEMENTED · MOCK/SIMULATOR TESTED · **REAL PLC TESTED = NO** |
| Quy ước | Mọi điều dưới đây về cách thư viện hoạt động đều lấy từ **source code**; chỗ nào chỉ có trong tài liệu/comment mà không có trong code thì được nói rõ. Điều chưa xác nhận với kỹ sư PLC được đánh dấu `NEEDS_CONFIRMATION`. |

---

## 1. Cấu trúc thư viện tham chiếu

```
MitsubishiMc.Client (public)
├── McClient                  ← client TCP + vòng đời kết nối + thực thi đọc/ghi (IDisposable)
│     ├── dùng McProtocolSettings   (IP, Port, TimeoutMs, NetworkNo, PcNo, CpuMonitoringTimerUnits)
│     ├── dùng McDeviceType         (D, M, X, Y, W)
│     ├── trả McReadResult / McWriteResult
│     └── gọi Internal.McFrameBuilder  (tạo frame)  và  Internal.McResponseParser (kiểm tra + giải mã)
└── Internal (internal, InternalsVisibleTo "SubApp.ProtocolTests")
      ├── McFrameBuilder      ← tạo frame 3E Binary (đọc word/bit, ghi word/bit, đóng gói nibble)
      └── McResponseParser    ← kiểm tra header, End Code, độ dài; giải mã word/bit
```

### 1.1 Bảng phân tích từng class

| Class | Vai trò | Điểm chính (từ source) | Port sang WCS? |
|---|---|---|---|
| `McClient` | Kết nối, tuần tự hóa, gửi/nhận, timeout, ngắt kết nối an toàn | `SemaphoreSlim _streamLock(1,1)` tuần tự hóa mọi I/O; `_connectionLock` cho connect/disconnect; `CancellationTokenSource` liên kết với `CancelAfter(TimeoutMs)` cho từng thao tác; lỗi sau khi đã gửi → đóng socket; ghi bị timeout sau khi gửi → `IsAmbiguous` | **Ý tưởng được giữ** trong `McTcpClient` (gate, đóng socket khi lỗi, ambiguous). Không sao chép: WCS đã có `PlcWorker` làm chủ socket + backoff |
| `McProtocolSettings` | Cấu hình | `Port=5000`, `TimeoutMs=3000`, `NetworkNo=0x00`, `PcNo=0xFF`, `CpuMonitoringTimerUnits=0` (0 = tự tính `max(1, TimeoutMs/250)`). **Không có** Module I/O No và Station No | WCS cấu hình **riêng từng PLC**, không có giá trị mặc định (xem §8) |
| `McDeviceType` | enum D, M, X, Y, W | Chỉ là nhãn. **Không có phép đổi cơ số**: comment nói X/Y là "bát phân trong tài liệu PLC, truyền thập phân" nhưng code không đổi gì | Port enum thiết bị; **tự cài đặt** phép đổi cơ số có kiểm tra (xem §5) |
| `McReadResult` | Kết quả đọc | `Success`, `ErrorMessage`, `EndCode`, `WordValues`/`BitValues`, `RequestHex`, `ResponseHex` | Khái niệm hex được port thành `PlcExchange` |
| `McWriteResult` | Kết quả ghi | Như trên + `IsAmbiguous` | Port thành `PlcWriteAmbiguousException` + `WriteResult.IsAmbiguous` |
| `McFrameBuilder` | Tạo frame | Mã thiết bị D=A8, W=B4, M=90, X=9C, Y=9D; frame đọc 21 byte; đóng gói nibble | **Port + kiểm chứng từng byte** (`McFrame`) |
| `McResponseParser` | Kiểm tra/giải mã phản hồi | Kiểm tra Subheader, Network, PC, Module I/O (cứng 0x03FF), Station (cứng 0x00), End Code, độ dài khai báo, độ dài thực | **Port + làm chặt hơn** (`McFrame.ParseAndValidateResponse`) |

---

## 2. Cấu trúc Request Frame (3E Binary)

Offset tính từ byte 0 của frame, mọi số nhiều byte là **Little Endian**.

| Offset | Độ dài | Trường | Giá trị / nguồn trong thư viện tham chiếu | Trong WCS |
|---|---|---|---|---|
| 0–1 | 2 | Subheader | `50 00` | `50 00` |
| 2 | 1 | Network No | `settings.NetworkNo` (mặc định 0x00) | cấu hình từng PLC |
| 3 | 1 | PC No | `settings.PcNo` (mặc định 0xFF) | cấu hình từng PLC |
| 4–5 | 2 | Module I/O No | **cố định** `FF 03` (0x03FF) | **cấu hình** từng PLC (`ModuleIoNumber`) |
| 6 | 1 | Station No | **cố định** `00` | **cấu hình** từng PLC (`PlcStationNumber`) |
| 7–8 | 2 | Request Data Length | `12 + số byte dữ liệu` (từ byte 9 đến hết frame) | giống hệt |
| 9–10 | 2 | CPU Monitoring Timer | đơn vị **250 ms**; mặc định `max(1, TimeoutMs/250)` hoặc `CpuMonitoringTimerUnits` | cấu hình từng PLC (`MonitoringTimer`, đơn vị 250 ms), **không tự tính** |
| 11–12 | 2 | Command | Đọc `01 04` (0x0401), Ghi `01 14` (0x1401) | giống hệt |
| 13–14 | 2 | Subcommand | Word `00 00`, Bit `01 00` | giống hệt |
| 15–17 | 3 | Head device number | số thập phân của thiết bị, 3 byte LE | giống hệt |
| 18 | 1 | Device code | D `A8`, W `B4`, M `90`, X `9C`, Y `9D` | giống hệt |
| 19–20 | 2 | Số điểm | LE | giống hệt |
| 21… | n | Dữ liệu ghi | Word: 2 byte LE/điểm. Bit: nibble (xem §4) | giống hệt |

Frame đọc luôn dài **21 byte**. Ví dụ (từ `HUONG_DAN_SU_DUNG.md` §7.3, đọc D100×5, timer 12):

```
50 00 | 00 | FF | FF 03 | 00 | 0C 00 | 0C 00 | 01 04 | 00 00 | 64 00 00 | A8 | 05 00
```

Test `The_reference_documentation_sample_request_is_reproduced_exactly` tái tạo đúng chuỗi này.

## 3. Cấu trúc Response Frame

| Offset | Độ dài | Trường | Kiểm tra trong thư viện tham chiếu |
|---|---|---|---|
| 0–1 | 2 | Subheader `D0 00` | phải đúng, nếu không → lỗi (kiểm tra ở cả `Receive3EResponseAsync` lẫn parser) |
| 2 | 1 | Network No | phải bằng giá trị đã gửi |
| 3 | 1 | PC No | phải bằng giá trị đã gửi |
| 4–5 | 2 | Module I/O No | phải bằng `0x03FF` (hằng số) |
| 6 | 1 | Station No | phải bằng `0x00` (hằng số) |
| 7–8 | 2 | Response Data Length = 2 (End Code) + dữ liệu | đọc từ header để biết còn bao nhiêu byte phải nhận |
| 9–10 | 2 | End Code | `0x0000` = thành công; khác 0 → `Fail(EndCode)` (kiểm tra **trước** độ dài) |
| 11… | n | Dữ liệu | đọc: đúng `2×điểm` byte (word) hoặc `(điểm+1)/2` byte (bit); ghi: **không có** dữ liệu |

Header cố định = 11 byte (0–10). Phản hồi lỗi của PLC có thể kèm khối thông tin lỗi 9 byte → độ dài khai báo 11; thư viện đọc theo độ dài khai báo nên vẫn nhận đủ.

**Cách thư viện xác định hết frame:** đọc đúng 11 byte (`ReadExactAsync`), lấy `len = byte[7]|byte[8]<<8`, đọc tiếp `len − 2` byte. Không dùng timeout riêng cho từng phần: toàn bộ gửi+nhận nằm trong **một** `CancelAfter(TimeoutMs)`.

## 4. Quy tắc đọc/ghi, Nibble và Endian

| Thao tác | Command / Subcommand | Thiết bị | Tối đa/request (tham chiếu) | Đóng gói dữ liệu |
|---|---|---|---|---|
| `ReadWordsAsync` | 0401 / 0000 | D, W | 960 word | 2 byte LE / word |
| `ReadBitsAsync` | 0401 / 0001 | M, X, Y | **3584** bit | nibble |
| `WriteWordsAsync` | 1401 / 0000 | D, W | 960 word | 2 byte LE / word |
| `WriteBitsAsync` | 1401 / 0001 | M, Y (**X bị chặn** trước khi gửi) | **1440** bit | nibble |

**Quy tắc Bit Nibble (từ `PackBitsToNibbles` và `ParseBitResponse`):** mỗi điểm = **1 nibble**, hai điểm / byte. Điểm chẵn (0,2,4…) ở **nibble cao** (bit 4–7), điểm lẻ (1,3,5…) ở **nibble thấp** (bit 0–3). Giá trị ON = `1`. Khi đọc, nibble khác 0 được coi là ON. Số điểm lẻ → nibble thấp của byte cuối là đệm (0). Tuyệt đối **không phải** "1 bit = 1 byte".

| Điểm | Byte |
|---|---|
| [0,0] | `00` |
| [0,1] | `01` |
| [1,0] | `10` |
| [1,1] | `11` |
| [1,1,1] | `11 10` |

**Little Endian:** áp dụng cho Request Data Length, Monitoring Timer, Command, Subcommand, Head device (3 byte), số điểm, End Code, mọi word dữ liệu. Ví dụ word `0x1234` → `34 12`.

## 5. Quy tắc địa chỉ thiết bị (kết quả đọc source)

| Thiết bị | Mã | Kiểu | Cơ số theo tham chiếu | Chuyển đổi **trong code**? |
|---|---|---|---|---|
| D | A8 | word | thập phân | không cần |
| M | 90 | bit | thập phân | không cần |
| W | B4 | word | thập phân (theo comment) | **không** |
| X | 9C | bit | "bát phân trong tài liệu PLC, truyền thập phân" | **không** |
| Y | 9D | bit | như X | **không** |

**Kết luận bắt buộc ghi nhận (đã xác minh từ source):**

1. `McClient.ReadBitsAsync/WriteBitsAsync` đưa `startAddress` **thẳng** vào `McFrameBuilder` (`f[15..17] = startAddress`). **Không có hàm đổi bát phân → thập phân nào trong source.** Các ví dụ `X10 → 8`, `X20 → 16`, `Y10 → 8`, `Y20 → 16` chỉ là hướng dẫn cho **người gọi** tự đổi trước khi truyền vào. Nhận định "thư viện đổi X/Y từ bát phân" là không đúng; WCS phải tự cài đặt phép đổi và có test (xem `McAddressTests`).
2. **Mâu thuẫn cần quyết định (không chọn lặng lẽ):** quy tắc "X/Y bát phân" là quy ước của dòng FX/iQ-F. Theo hiểu biết chung về dòng **MELSEC-Q** (PLC mục tiêu có module QJ71E71-100), số hiệu **X, Y và W được viết ở hệ thập lục phân** trong GX Works (X10 = 16, X20 = 32). Tài liệu tham chiếu ghi đã thử trên Q13UDV nhưng ví dụ chỉ nêu X/Y thấp; nếu chỉ dùng X0–X7 thì sai lệch cơ số không lộ ra. **Chưa đối chiếu với SH-080008 / GX Works của dự án → `NEEDS_CONFIRMATION`.**
3. Quyết định kỹ thuật trong WCS: cơ số X/Y (8 hoặc 16) và W (10 hoặc 16) là **tham số cấu hình riêng của từng PLC**, mặc định **không có**. Khi chưa cấu hình, địa chỉ X/Y/W bị từ chối với thông báo `NEEDS_CONFIRMATION` (không đoán). D và M luôn thập phân. Địa chỉ được lưu **đúng ký hiệu kỹ sư nhập** (`x010` → `X10`), phép đổi chỉ xảy ra khi tạo frame.

| Văn bản | Cơ số 8 | Cơ số 16 |
|---|---|---|
| X7 | 7 | 7 |
| X10 | 8 | 16 |
| X17 | 15 | 23 |
| X20 | 16 | 32 |
| Y10 | 8 | 16 |
| Y20 | 16 | 32 |

## 6. TCP Client, timeout và vòng đời kết nối (từ `McClient.cs`)

| Chủ đề | Cách thư viện tham chiếu làm | Cách WCS làm (`McTcpClient`) |
|---|---|---|
| Tạo socket | `Socket(AddressFamily, Stream, Tcp)`, `NoDelay=true`, `Send/ReceiveTimeout=TimeoutMs` | `TcpClient{NoDelay=true}` |
| Phân giải IP | `IPAddress.TryParse`, nếu không được thì DNS | IP đã được validate chặt khi cấu hình; không dùng DNS |
| ConnectAsync | `BeginConnect` + `TaskCompletionSource` + CTS `CancelAfter(TimeoutMs)`; timeout → `TimeoutException` | `ConnectAsync(token)` + CTS `CancelAfter(ConnectTimeout)`; timeout → `TimeoutException` |
| Lấy stream | `NetworkStream(socket, ownsSocket:true)` | `tcp.GetStream()` |
| Gửi | `stream.WriteAsync` | `WriteAsync` + `FlushAsync` |
| Xác định độ dài response | đọc 11 byte rồi `len-2` byte | đọc 9 byte header rồi `len` byte; **từ chối `len > 1922`** |
| Response thiếu byte | chờ tới timeout → đóng socket | như vậy; byte đã nhận được lưu vào chẩn đoán |
| Timeout | một CTS cho cả (chờ lock + gửi + nhận) | CTS riêng từng thao tác: **kết nối / đọc / ghi** |
| SocketException/IOException | trả `Fail` và `DisconnectInternal()` | ném lỗi truyền thông, đóng socket, `State=Faulted` |
| Tránh dùng socket lỗi | `DisconnectInternal` đặt `_socket=_stream=null`; lần sau `IsConnected=false` | `Close()`; lần gửi kế tiếp mở lại kết nối |
| CancellationToken | liên kết với CTS kết nối | liên kết với CTS thao tác |
| Tuần tự hóa | `SemaphoreSlim(1,1)` | `SemaphoreSlim(1,1)` (gate) **và** chỉ `PlcWorker` của PLC đó gọi vào |
| Ghi bị gián đoạn sau khi gửi | `IsAmbiguous=true`, "tuyệt đối không retry" | `PlcWriteAmbiguousException`, không bao giờ retry, ghi log `WRITE_AMBIGUOUS` |
| Retry đọc | không có | tối đa 1 lần trên kết nối mới (đọc là idempotent) |

## 7. Giá trị mặc định của thư viện (chỉ để tham khảo)

| Tham số | Giá trị thư viện | Trong WCS |
|---|---|---|
| Port | 5000 (Q/FX5), 5007 (iQ-R) | **không có mặc định** – `NEEDS_CONFIGURATION` |
| Network No | 0x00 | không có mặc định |
| PC No | 0xFF | không có mặc định |
| Module I/O No | 0x03FF (hằng) | không có mặc định |
| Station No | 0x00 (hằng) | không có mặc định |
| TimeoutMs | 3000 | không có mặc định |
| CpuMonitoringTimerUnits | tự tính `TimeoutMs/250` | không có mặc định (kỹ sư xác nhận) |

## 8. Các điểm trong thư viện tham chiếu KHÔNG được sao chép (và vì sao)

| # | Điểm | Lý do / cách WCS xử lý | Test chứng minh |
|---|---|---|---|
| R1 | Chuyển đổi X/Y chỉ có trong tài liệu, không có trong code | WCS tự cài đặt có kiểm tra chữ số theo cơ số | `McAddressTests` |
| R2 | Giả định X/Y bát phân, W thập phân (khác thông lệ MELSEC-Q) | Cơ số cấu hình từng PLC, không mặc định | `The_same_text_means_different_devices…` |
| R3 | Module I/O No và Station No cố định trong builder và parser | Cấu hình được; phản hồi phải echo đúng giá trị **đã gửi** | `…wrong_header_field…`, `Where_the_reference_is_weaker…` |
| R4 | Không giới hạn trên của độ dài khai báo (có thể chờ ~65 533 byte) | Từ chối ngay ở header nếu > 1922 | `An_oversized_announced_length…` |
| R5 | Builder nhận số điểm bất kỳ | Codec của WCS áp giới hạn 960 / 3584 / 1440 | `Where_the_reference_is_weaker…` |
| R6 | `PingAsync` đọc `D0` | Đọc một địa chỉ chưa được duyệt – vi phạm quy tắc "không tự đặt địa chỉ" → **không port** | – |
| R7 | `IsConnected` dựa vào `Socket.Connected` (không phát hiện PLC đã đóng) | WCS dựa vào lỗi giao dịch và vòng lặp `PlcWorker` | `Disconnect_in_the_middle…` |
| R8 | `IsHealthy` theo thời gian im lặng | WCS có `State`, `Quality`, `lastSuccessAt` | – |
| R9 | `SendAndReceive3EFrameAsync` không được dùng (mã chết) | không port | – |
| R10 | Mô tả End Code trong `FormatEndCode` | **Chưa được kiểm chứng với SH-080008** (một số mô tả có vẻ khác bảng mã lỗi của dòng Q theo hiểu biết chung). WCS chỉ ghi/hiển thị **mã số** và coi mọi mã ≠ 0 là lỗi PLC, không diễn giải | – |
| R11 | Giới hạn bit đọc 3584 / ghi 1440 thấp hơn mức tối đa mà tài liệu Q nêu (7168) | WCS **lấy đúng giá trị đã chạy thực tế** làm trần; mức cao hơn là `NEEDS_CONFIRMATION` | `Where_the_reference_is_weaker…` |

## 9. Đối chiếu driver WCS với thư viện tham chiếu

Trạng thái: **MATCH** (tương đương, có bằng chứng test) · **NEEDS_IMPROVEMENT** · **MISSING** · **NEEDS_CONFIRMATION**.
Cột "Trước CR-003" là kết quả audit driver CR-002; cột "Sau CR-003" là trạng thái hiện tại. MATCH chỉ được ghi khi có test.

| Chức năng | Thư viện tham chiếu | Driver WCS (trước CR-003) | Sau CR-003 | Bằng chứng |
|---|---|---|---|---|
| TCP connection | Socket + BeginConnect + CTS timeout | TcpClient + CTS timeout | **MATCH** | `McProtocolTests`, `McCr003SocketTests` (kết nối mock TCP thật) |
| Timeout | một `TimeoutMs` cho mọi thao tác | một `TimeoutMs` | **MATCH** (WCS tách connect/read/write, có fallback) | `Read_and_write_use_their_own_timeouts…` |
| 3E Binary Frame | như §2 | đúng cho D/M | **MATCH** | `McReferenceParityTests` (so byte với oracle) |
| Request Builder | `McFrameBuilder` | `McFrame.Build*` (D, M) | **MATCH** cho D, W, M, X, Y | 1 056 frame đọc-word + 2 772 frame đọc-bit + 10 frame ghi-word + 150 frame ghi-bit (mọi độ dài, mẫu bit, route, timer) đều trùng từng byte |
| Response Parser | kiểm tra 6 trường + độ dài | chỉ kiểm tra Network/PC | **MATCH** (WCS chặt hơn) | `Word_read_responses_are_accepted_or_rejected_exactly_like_the_reference`, `Bit_read_and_write_responses…` |
| Read Word | D, W | chỉ D | **MATCH** | `W_X_and_Y_devices_are_read…` |
| Read Bit | M, X, Y | chỉ M | **MATCH** | như trên |
| Write Word | D, W | chỉ D | **MATCH** | `Word_write_requests_are_identical…`, `Writes_go_to_Y_and_W…` |
| Write Bit | M, Y; chặn X | chỉ M | **MATCH** | `Bit_write_requests_are_identical…`, `Writing_to_X_is_refused…` |
| Nibble encoding | even=high, odd=low | đúng | **MATCH** | `Bit_units_use_one_nibble_per_point…`, `Bit_decoding_matches_the_reference_parser…` |
| Little Endian | mọi trường nhiều byte | đúng | **MATCH** | `…little_endian_data` |
| Device Address D/M | thập phân | M, D | **MATCH** | `D_and_M_are_always_decimal…` |
| Device Address W | thập phân (theo comment) | MISSING | **NEEDS_CONFIRMATION** (cơ số W) | `McAddressTests` |
| X/Y conversion | chỉ trong tài liệu; bát phân | MISSING | **NEEDS_CONFIRMATION** (bát phân hay thập lục phân) | `X_and_Y_in_octal…`, `X_and_Y_in_hexadecimal…` |
| Reconnect | do người gọi (vòng lặp) | `PlcWorker` backoff | **MATCH** (tốt hơn) | `PlcWorkerTests`, `Reconnect_works_after_the_PLC_was_powered_off_and_on` |
| Concurrent request | `SemaphoreSlim` | gate | **MATCH** | `Mixed_concurrent_requests_on_one_socket…` (`PipelinedRequests = 0`) |
| Diagnostic Hex | `RequestHex/ResponseHex` | MISSING | **MATCH** (Admin-only) | `The_last_exchange_records…`, `…integration` |
| Ambiguous write | `IsAmbiguous` | NEEDS_IMPROVEMENT (không có khái niệm) | **MATCH** | `A_write_that_gets_no_answer_is_ambiguous…`, `A_write_that_was_executed_but…` |
| Error handling | `Fail(EndCode, message)` | exception | **MATCH** | `A_PLC_end_code_is_not_a_transport_fault…` |
| Giới hạn điểm/request | 960 / 3584 / 960 / 1440 | 960 / 7168 | **MATCH** (dùng giá trị đã chạy thực tế); mức cao hơn `NEEDS_CONFIRMATION` | `Where_the_reference_is_weaker…` |
| Monitoring timer | tự tính từ timeout | cấu hình | khác có chủ ý – WCS **không tự tính**, kỹ sư xác nhận | `NEEDS_CONFIRMATION` |
| Module I/O / Station No | cố định 03FF / 00 | cấu hình | khác có chủ ý – cấu hình từng PLC | `NEEDS_CONFIRMATION` |
| Chỉ polling điểm đã duyệt | (không có) | polling cả điểm chưa duyệt | **MATCH** (sửa trong CR-003) | `A_point_is_polled_only_when_it_has_an_address_and_is_approved`, `Only_approved_addresses_are_polled…` |

## 10. Những điểm cần kỹ sư PLC xác nhận (`NEEDS_CONFIRMATION`)

1. Cơ số X/Y: bát phân hay thập lục phân (MELSEC-Q thường là thập lục phân). Cơ số W.
2. Cổng TCP, Network No, PC No, Module I/O No, Station No, Monitoring Timer (đơn vị 250 ms) cho **từng** PLC. Giá trị của thư viện (5000 / 0x00 / 0xFF / 0x03FF / 0x00) chỉ để tham khảo.
3. Giới hạn số điểm/request của module QJ71E71-100 (WCS dùng 960 word, 3584 bit đọc, 1440 bit ghi vì đã chạy thực tế).
4. Ý nghĩa các End Code gặp thực tế (đối chiếu SH-080008).
5. INT có dấu/không dấu, mã hóa và độ dài STRING, cách dùng `Request_ID`.
6. Địa chỉ thật của 368 điểm IO, phạm vi thiết bị cho phép đọc/ghi, quy trình handshake.
7. Giá trị Monitoring Timer = 0 (theo hiểu biết chung là chờ vô hạn phía PLC) có được phép hay không.

## 11. Tương thích QJ71E71-100 (Giai đoạn 9)

**Không có PLC vật lý → không khẳng định đã tương thích.** Bảng dưới là đối chiếu *kỹ thuật* giữa implementation và cấu hình module Ethernet cần có. Nội dung về cấu hình module là hiểu biết chung về MELSEC-Q, **chưa kiểm chứng với tài liệu QJ71E71-100 của dự án**.

| Hạng mục | Implementation WCS | Cần cấu hình/xác nhận phía module | Trạng thái |
|---|---|---|---|
| Giao thức | MC 3E, Binary, TCP | "Communication data code" = Binary; Open Settings: TCP, kiểu mở dành cho MC protocol | `NEEDS_CONFIRMATION` |
| Port | cấu hình, không mặc định | cổng trong Open Settings (GX Works thường hiển thị HEX) | `NEEDS_CONFIGURATION` |
| Network / PC / Module I/O / Station | cấu hình; phản hồi phải echo đúng | khớp cấu hình mạng PLC | `NEEDS_CONFIRMATION` |
| Monitoring Timer | cấu hình, đơn vị 250 ms | giá trị phù hợp chu kỳ scan | `NEEDS_CONFIRMATION` |
| Device code | D A8, W B4, M 90, X 9C, Y 9D | thiết bị tồn tại và nằm trong phạm vi cho phép | `NEEDS_CONFIRMATION` |
| Lệnh | 0401 / 1401, subcommand 0000 / 0001 (kiểu Q/L) | module Q dùng kiểu subcommand này | `NEEDS_CONFIRMATION` |
| Giới hạn điểm | 960 / 3584 / 1440 | không vượt giới hạn module | `NEEDS_CONFIRMATION` |
| Kết thúc/nhận phản hồi | đọc đúng độ dài khai báo, đóng socket nếu lệch | module trả khung đúng chuẩn | chỉ kiểm chứng với mock |
| Số kết nối | tối đa 4 (1 / PLC) | số kết nối đồng thời module cho phép | `NEEDS_CONFIRMATION` |

Không có thay đổi nào tự bật `REAL_READ_ONLY` / `REAL_READ_WRITE` hay tự kết nối tới địa chỉ PLC. Phiên bản hiện tại chạy `SIMULATOR`.
