# PLC_IO_Map – trạng thái các workbook

| Tệp | Trạng thái | Ghi chú |
|---|---|---|
| `AGV_PLC Interface_List_20_11_2026.xlsx` | **Giao diện PLC mới – cơ sở thiết kế** (CR-029, `PENDING_APPROVAL` / chờ bên PLC xác nhận) | 6 tín hiệu/trạm + `HeartBit` + 5 `Spair`; địa chỉ trống. sha256 `3c5da74b310fec46aed7d5a939ec60e8743d80c1b61c098c0b00ae48dd9aeddf`. Không được sửa tại chỗ; thay đổi qua Change Request. |
| `AGV_PLC_IO_Interface_List_10_11_2026.xlsx` | **SUPERSEDED** bởi bản 20/11 (giữ làm lịch sử) | 22 tín hiệu M9000–M9123/D1500–D1610, 9 bước. Nền của CR-010. sha256 `936271db2d19a9ed45b260e22b289fba14f875261d9604599d6303a4bdabca53` |
| `ETEK_WCS_IO_Map_4PLC_16Stations_v2_Separate_IO_Sets.xlsx` | **Bị ảnh hưởng (CR-029)** – mẫu 23 tín hiệu/trạm × 16 trạm dựa trên giao diện 10/11; CLAUDE.md §7.1 vẫn gọi là tham chiếu chính thức cho đến khi chủ dự án duyệt sửa | sha256 `89ff65b5c2e16504986138edad0bbcf12037916943548841fb0c6e9536cc518d` (trong Git). Catalog `plc-io-catalog.json` được sinh từ tệp này. Lưu ý: tệp đang bị xóa trong working tree của một số nhánh – việc xóa đó **không** thuộc CR-029 và không được commit. |

Đặc tả mới: [`../PLC_20_11_2026/`](../PLC_20_11_2026/README.md).
