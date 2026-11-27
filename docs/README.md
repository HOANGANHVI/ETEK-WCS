# ETEK WCS – Specification Package

**Version:** 1.3  
**Status:** Change-management-ready baseline  
**Baseline inherited from:** v1.2

## Purpose

This package defines the approved business baseline and the controlled process for evolving the ETEK WCS specification without unintentionally changing unrelated behavior.

## Mục lục (cập nhật 2026-11-27)

Quy tắc làm việc bắt buộc: [CLAUDE.md](../CLAUDE.md). Triển khai: [deployment/README.md](../deployment/README.md).

### Baseline và quyết định

| Nhóm | Nội dung |
|---|---|
| [specification/](specification/) | Đặc tả nghiệp vụ và tích hợp đã duyệt |
| [specification/agv-phase2/](specification/agv-phase2/) | Đặc tả AGV Phase 2 (xem trạng thái trong từng file) |
| [decisions/ADR/](decisions/ADR/) | Quyết định kiến trúc |

### Quản lý thay đổi

| Tài liệu | Nội dung |
|---|---|
| [CHANGE_REQUESTS.md](change-management/CHANGE_REQUESTS.md) | Sổ Change Request và trạng thái |
| [CHANGELOG.md](change-management/CHANGELOG.md) | Lịch sử phiên bản |
| [IMPACT_MATRIX.md](change-management/IMPACT_MATRIX.md) | Ma trận ảnh hưởng |
| [REQUIREMENT_TRACEABILITY.md](change-management/REQUIREMENT_TRACEABILITY.md) | Truy vết yêu cầu |
| [change-management/](change-management/) | Hồ sơ từng CR (CR-026 …), gồm [CR-047](change-management/CR-047_HOTFIX_IO_MAP_AND_RACK_REMOVAL.md) và [CR-048](change-management/CR-048_IO_DEFAULT_ADDRESSES.md) |

### Giao diện (interface contract)

| Tài liệu | Nội dung |
|---|---|
| [IO Map 22/11/2026](02_Interface_Specification/PLC_IO_Map/AGV_PLC%20Interface_List_22_11_2026.xlsx) và [trạng thái](02_Interface_Specification/PLC_IO_Map/README.md) | IO Map duy nhất của dự án; chưa được kỹ sư PLC xác nhận |
| [02_Interface_Specification/](02_Interface_Specification/) | Đặc tả PLC, định dạng gói cấu hình; bộ 20/11 và các bản nháp 10/11 giữ để đối chiếu lịch sử (xem ghi chú đầu file) |
| [03_RCS_Interface/](03_RCS_Interface/) | API RCS-2000 của nhà cung cấp và câu hỏi |
| [01_Transport_Process_description/](01_Transport_Process_description/) | Tài liệu quy trình nguồn |

### Đang mở: câu hỏi, đề xuất, kế hoạch

| Tài liệu | Nội dung |
|---|---|
| [BP_PH2_CUTOVER_PLAN_2026-11-26.md](reviews/BP_PH2_CUTOVER_PLAN_2026-11-26.md) | Chuyển đổi BP → PH2: mapping, thứ tự, điều kiện, rollback |
| [CR-043_SIMULATION_MODULE_INITIAL_REVIEW.md](reviews/CR-043_SIMULATION_MODULE_INITIAL_REVIEW.md) | CR-043 – Module mô phỏng PLC & RCS dùng lại được: INITIAL REVIEW |
| [CR008B-REV005_RCS_VENDOR_QUESTIONS.md](reviews/CR008B-REV005_RCS_VENDOR_QUESTIONS.md) | REV-005 – Câu hỏi cần nhà cung cấp RCS-2000 xác nhận |
| [ENGINEER_QUESTIONS_PLC_RCS_2026-11-26.md](reviews/ENGINEER_QUESTIONS_PLC_RCS_2026-11-26.md) | Câu hỏi cho kỹ sư PLC và nhà cung cấp RCS |
| [OQ-54_QUEUE_SCOPE_ANALYSIS_2026-11-26.md](reviews/OQ-54_QUEUE_SCOPE_ANALYSIS_2026-11-26.md) | OQ-54 – Phân tích lại Queue Ordering Scope và phương án A2 |
| [REAL_DEVICE_COMMISSIONING_PLAN_2026-11-21.md](reviews/REAL_DEVICE_COMMISSIONING_PLAN_2026-11-21.md) | Kế hoạch triển khai và commissioning với thiết bị thật – 2026-11-21 |
| [STATION_ENGINE_MANUAL_REQUEST_2026-11-26.md](reviews/STATION_ENGINE_MANUAL_REQUEST_2026-11-26.md) | Engine theo trạm và form tạo yêu cầu thủ công |
| [OPEN_QUESTIONS.md](reports/agv-phase2-assessment/OPEN_QUESTIONS.md) | AGV Phase 2 – Câu hỏi cần người dùng xác nhận |

### Triển khai và nghiệm thu

| Tài liệu | Nội dung |
|---|---|
| [DEPLOYMENT_TEST_CHECKLIST.md](DEPLOYMENT_TEST_CHECKLIST.md) | Checklist nghiệm thu triển khai |
| [docker-acceptance/](docker-acceptance/) | Bộ script và mẫu báo cáo nghiệm thu Docker |
| [reports/cr-046-ui/](reports/cr-046-ui/), [reports/cr-047/](reports/cr-047/), [reports/cr-048/](reports/cr-048/) | Bằng chứng nghiệm thu của CR-046 đến CR-048 |
| [reports/phase2/](reports/phase2/) | Thiết kế và truy vết của CR-036 |
| [Báo cáo QA 27/11](reports/qa-2026-11-27/QA_REPORT.md) | Kiểm thử Simulator, QA findings, danh sách lỗi |

### Lịch sử

[archive/](archive/README.md): báo cáo triển khai, rà soát, audit và hiện trạng đã cũ. Không dùng làm cơ sở thiết kế mới.

## Approved baseline rules carried forward

1. Task concurrency: `PER_AGV`.
2. Slide 8 / B1-B2 full-rack delivery flow: `REVIEW`, not approved for implementation.
3. Rack inventory / bind-rack business ownership: `WCS`.
4. A3/A4 and D1/D2: `UNCHANGED`, preserve the legacy process.

## Change-control rule

Never overwrite the approved baseline just because a new source document is received. First create a Change Request, analyze conflicts and impacts, obtain approval, then update only affected specification files and tests.

## Versioning

- **Major**: incompatible business or architecture baseline change.
- **Minor**: approved additive capability or process change.
- **Patch**: clarification or correction that does not change approved behavior.

The project owner approves changes to business behavior. Technical implementation proposals do not by themselves authorize a business rule change.

## Important source limitations

This package does not invent missing details. In particular, BP-04 remains REVIEW; legacy A3/A4 and D1/D2 process details require the original process document; PLC addresses, RCS production configuration, workstation codes and task templates require confirmation.

## PLC interface documents – updated 2026-11-06

The Phase 1 PLC topology and controlled working IO interface are documented in:
- `specification/business-rules.md` section 1.8.
- `specification/integration-requirements.md` section 5.10.
- **Hiện hành (2026-11-26, CR-045):** `02_Interface_Specification/PLC_IO_Map/AGV_PLC Interface_List_22_11_2026.xlsx` là IO Map duy nhất ([trạng thái](02_Interface_Specification/PLC_IO_Map/README.md)); workbook IO Map v2 từng nêu ở đây đã ngừng sử dụng.

The approved direction is four physical PLCs, four WCS PLC connections, and four independent Station IO sets per PLC (16 Station IO sets total). Read the addenda and workbook together. The workbook does not yet constitute a commissioned actual-address map; actual per-Station addresses and handshake details require PLC-engineer confirmation.
