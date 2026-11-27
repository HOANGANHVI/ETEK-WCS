# Báo cáo dọn dẹp tài liệu (2026-11-27)

Nền: `main` `5916d05`. Phạm vi: `docs/`, `report/` và các tham chiếu tới chúng. Không sửa mã nguồn, cấu hình, database hay IO Map.

## 1. Số liệu (369 file trong `docs/` và `report/` trước khi dọn)

| Phân loại | Số file | Trong đó Markdown | Trong đó ảnh |
|---|---|---|---|
| KEEP – giữ nguyên vị trí | 125 | 92 | 21 |
| ARCHIVE – chuyển vào `docs/archive/` | 140 | 45 | 95 |
| DELETE | 104 | 0 | 104 |
| CONSOLIDATE – hợp nhất | 0 | 0 | 0 |

File mới: báo cáo này và [archive/README.md](archive/README.md). [README.md](README.md) được viết lại phần cấu trúc thành mục lục; phần quy tắc baseline giữ nguyên.

## 2. Vì sao không hợp nhất

Không có hai tài liệu Markdown nào giống hệt nhau (so mã băm; cặp file giống hệt duy nhất là một ảnh, nằm trong hai thư mục bị xóa). Các nhóm chồng chéo là hồ sơ theo thời điểm đã được duyệt hoặc được tham chiếu (ví dụ `CR-027_BASELINE` và `CR-028_BASELINE` ghi rõ "never edited"; chuỗi báo cáo CR-008B được ADR-0007 dẫn). Gộp lại phải viết lại hồ sơ, nên chúng được giữ hoặc lưu trữ nguyên vẹn.

## 3. Đã xóa

| Thư mục | Số ảnh | Lý do | Khôi phục |
|---|---|---|---|
| `docs/reports/ui-audit/` | 10 | Không tài liệu hay mã nào tham chiếu; không có báo cáo kèm theo | Git, commit `5916d05` |
| `docs/reports/ui-redesign/` | 94 | Như trên (ảnh trước/sau; ảnh nghiệm thu của CR-025 được ghi là không commit) | Git, commit `5916d05` |

Danh sách từng file:

```
docs/reports/ui-audit/screenshots/00-login.png
docs/reports/ui-audit/screenshots/01-after-login.png
docs/reports/ui-audit/screenshots/02-dashboard.png
docs/reports/ui-audit/screenshots/02b-sidebar-header.png
docs/reports/ui-audit/screenshots/03-amr-monitoring-cards.png
docs/reports/ui-audit/screenshots/04-amr-monitoring-table.png
docs/reports/ui-audit/screenshots/05-task-management.png
docs/reports/ui-audit/screenshots/06-rcs-diagnostics.png
docs/reports/ui-audit/screenshots/07-sidebar.png
docs/reports/ui-audit/screenshots/08-header.png
docs/reports/ui-redesign/screenshots/after-1366x768/account.png
docs/reports/ui-redesign/screenshots/after-1366x768/amr.png
docs/reports/ui-redesign/screenshots/after-1366x768/config-transfer.png
docs/reports/ui-redesign/screenshots/after-1366x768/config.png
docs/reports/ui-redesign/screenshots/after-1366x768/dashboard.png
docs/reports/ui-redesign/screenshots/after-1366x768/inventory.png
docs/reports/ui-redesign/screenshots/after-1366x768/login.png
docs/reports/ui-redesign/screenshots/after-1366x768/logs-audit.png
docs/reports/ui-redesign/screenshots/after-1366x768/logs-plc.png
docs/reports/ui-redesign/screenshots/after-1366x768/logs-system.png
docs/reports/ui-redesign/screenshots/after-1366x768/plc-connections.png
docs/reports/ui-redesign/screenshots/after-1366x768/plc-io-mapping.png
docs/reports/ui-redesign/screenshots/after-1366x768/plc-monitor.png
docs/reports/ui-redesign/screenshots/after-1366x768/processes.png
docs/reports/ui-redesign/screenshots/after-1366x768/rcs-diagnostics.png
docs/reports/ui-redesign/screenshots/after-1366x768/routing.png
docs/reports/ui-redesign/screenshots/after-1366x768/tasks.png
docs/reports/ui-redesign/screenshots/after-1366x768/users.png
docs/reports/ui-redesign/screenshots/after-1600x900/account.png
docs/reports/ui-redesign/screenshots/after-1600x900/amr.png
docs/reports/ui-redesign/screenshots/after-1600x900/config-transfer.png
docs/reports/ui-redesign/screenshots/after-1600x900/config.png
docs/reports/ui-redesign/screenshots/after-1600x900/dashboard.png
docs/reports/ui-redesign/screenshots/after-1600x900/inventory.png
docs/reports/ui-redesign/screenshots/after-1600x900/login.png
docs/reports/ui-redesign/screenshots/after-1600x900/logs-audit.png
docs/reports/ui-redesign/screenshots/after-1600x900/logs-plc.png
docs/reports/ui-redesign/screenshots/after-1600x900/logs-system.png
docs/reports/ui-redesign/screenshots/after-1600x900/plc-connections.png
docs/reports/ui-redesign/screenshots/after-1600x900/plc-io-mapping.png
docs/reports/ui-redesign/screenshots/after-1600x900/plc-monitor.png
docs/reports/ui-redesign/screenshots/after-1600x900/processes.png
docs/reports/ui-redesign/screenshots/after-1600x900/rcs-diagnostics.png
docs/reports/ui-redesign/screenshots/after-1600x900/routing.png
docs/reports/ui-redesign/screenshots/after-1600x900/tasks.png
docs/reports/ui-redesign/screenshots/after-1600x900/users.png
docs/reports/ui-redesign/screenshots/after-tablet-820x1180/account.png
docs/reports/ui-redesign/screenshots/after-tablet-820x1180/amr.png
docs/reports/ui-redesign/screenshots/after-tablet-820x1180/config-transfer.png
docs/reports/ui-redesign/screenshots/after-tablet-820x1180/config.png
docs/reports/ui-redesign/screenshots/after-tablet-820x1180/dashboard.png
docs/reports/ui-redesign/screenshots/after-tablet-820x1180/inventory.png
docs/reports/ui-redesign/screenshots/after-tablet-820x1180/login.png
docs/reports/ui-redesign/screenshots/after-tablet-820x1180/logs-audit.png
docs/reports/ui-redesign/screenshots/after-tablet-820x1180/logs-plc.png
docs/reports/ui-redesign/screenshots/after-tablet-820x1180/logs-system.png
docs/reports/ui-redesign/screenshots/after-tablet-820x1180/plc-connections.png
docs/reports/ui-redesign/screenshots/after-tablet-820x1180/plc-io-mapping.png
docs/reports/ui-redesign/screenshots/after-tablet-820x1180/plc-monitor.png
docs/reports/ui-redesign/screenshots/after-tablet-820x1180/processes.png
docs/reports/ui-redesign/screenshots/after-tablet-820x1180/rcs-diagnostics.png
docs/reports/ui-redesign/screenshots/after-tablet-820x1180/routing.png
docs/reports/ui-redesign/screenshots/after-tablet-820x1180/tasks.png
docs/reports/ui-redesign/screenshots/after-tablet-820x1180/users.png
docs/reports/ui-redesign/screenshots/after/account.png
docs/reports/ui-redesign/screenshots/after/amr.png
docs/reports/ui-redesign/screenshots/after/config-transfer.png
docs/reports/ui-redesign/screenshots/after/config.png
docs/reports/ui-redesign/screenshots/after/dashboard-en.png
docs/reports/ui-redesign/screenshots/after/dashboard.png
docs/reports/ui-redesign/screenshots/after/inventory.png
docs/reports/ui-redesign/screenshots/after/login.png
docs/reports/ui-redesign/screenshots/after/logs-audit.png
docs/reports/ui-redesign/screenshots/after/logs-plc.png
docs/reports/ui-redesign/screenshots/after/logs-system.png
docs/reports/ui-redesign/screenshots/after/plc-connections.png
docs/reports/ui-redesign/screenshots/after/plc-io-mapping.png
docs/reports/ui-redesign/screenshots/after/plc-monitor.png
docs/reports/ui-redesign/screenshots/after/processes.png
docs/reports/ui-redesign/screenshots/after/rcs-diagnostics.png
docs/reports/ui-redesign/screenshots/after/routing.png
docs/reports/ui-redesign/screenshots/after/sidebar-collapsed.png
docs/reports/ui-redesign/screenshots/after/tasks.png
docs/reports/ui-redesign/screenshots/after/users.png
docs/reports/ui-redesign/screenshots/before/account.png
docs/reports/ui-redesign/screenshots/before/amr.png
docs/reports/ui-redesign/screenshots/before/config-transfer.png
docs/reports/ui-redesign/screenshots/before/config.png
docs/reports/ui-redesign/screenshots/before/dashboard-en.png
docs/reports/ui-redesign/screenshots/before/dashboard.png
docs/reports/ui-redesign/screenshots/before/inventory.png
docs/reports/ui-redesign/screenshots/before/login.png
docs/reports/ui-redesign/screenshots/before/logs-audit.png
docs/reports/ui-redesign/screenshots/before/logs-plc.png
docs/reports/ui-redesign/screenshots/before/logs-system.png
docs/reports/ui-redesign/screenshots/before/plc-connections.png
docs/reports/ui-redesign/screenshots/before/plc-io-mapping.png
docs/reports/ui-redesign/screenshots/before/plc-monitor.png
docs/reports/ui-redesign/screenshots/before/processes.png
docs/reports/ui-redesign/screenshots/before/rcs-diagnostics.png
docs/reports/ui-redesign/screenshots/before/routing.png
docs/reports/ui-redesign/screenshots/before/sidebar-collapsed.png
docs/reports/ui-redesign/screenshots/before/tasks.png
docs/reports/ui-redesign/screenshots/before/users.png
```

## 4. Đã chuyển

| Từ | Đến | Số file | Nội dung |
|---|---|---|---|
| `docs/reviews/` | `docs/archive/reviews/` | 120 | Báo cáo, rà soát theo CR đã hoàn tất, kèm ảnh |
| `report/` | `docs/archive/audits-2026-11-16/` | 7 | Audit và báo cáo ngày 16/11 |
| `docs/reports/agv-phase2-assessment/` | `docs/archive/agv-phase2/assessment/` | 8 | Đánh giá Phase 0 (câu hỏi mở giữ tại chỗ) |
| `docs/reports/agv-phase2/` | `docs/archive/agv-phase2/revisions/` | 2 | Báo cáo sửa đặc tả Phase 2 |
| `docs/` | `docs/archive/status/` | 3 | Hiện trạng, bàn giao, báo cáo kiểm thử cũ |

Tài liệu Markdown đã chuyển:

| Vị trí cũ | Vị trí mới |
|---|---|
| `docs/TESTING_REPORT_CR-002.md` | [archive/status/TESTING_REPORT_CR-002.md](archive/status/TESTING_REPORT_CR-002.md) |
| `docs/WORK_LOG.md` | [archive/status/WORK_LOG.md](archive/status/WORK_LOG.md) |
| `docs/reports/CURRENT_SYSTEM_STATUS.md` | [archive/status/CURRENT_SYSTEM_STATUS.md](archive/status/CURRENT_SYSTEM_STATUS.md) |
| `docs/reports/agv-phase2-assessment/ARCHITECTURE_ASSESSMENT.md` | [archive/agv-phase2/assessment/ARCHITECTURE_ASSESSMENT.md](archive/agv-phase2/assessment/ARCHITECTURE_ASSESSMENT.md) |
| `docs/reports/agv-phase2-assessment/IMPLEMENTATION_PLAN.md` | [archive/agv-phase2/assessment/IMPLEMENTATION_PLAN.md](archive/agv-phase2/assessment/IMPLEMENTATION_PLAN.md) |
| `docs/reports/agv-phase2-assessment/MISSION_ORCHESTRATION_ASSESSMENT.md` | [archive/agv-phase2/assessment/MISSION_ORCHESTRATION_ASSESSMENT.md](archive/agv-phase2/assessment/MISSION_ORCHESTRATION_ASSESSMENT.md) |
| `docs/reports/agv-phase2-assessment/PLC_HANDSHAKE_ASSESSMENT.md` | [archive/agv-phase2/assessment/PLC_HANDSHAKE_ASSESSMENT.md](archive/agv-phase2/assessment/PLC_HANDSHAKE_ASSESSMENT.md) |
| `docs/reports/agv-phase2-assessment/PROCESS_IMPLEMENTATION_STATUS.md` | [archive/agv-phase2/assessment/PROCESS_IMPLEMENTATION_STATUS.md](archive/agv-phase2/assessment/PROCESS_IMPLEMENTATION_STATUS.md) |
| `docs/reports/agv-phase2-assessment/RACK_INVENTORY_ASSESSMENT.md` | [archive/agv-phase2/assessment/RACK_INVENTORY_ASSESSMENT.md](archive/agv-phase2/assessment/RACK_INVENTORY_ASSESSMENT.md) |
| `docs/reports/agv-phase2-assessment/REQUIREMENT_CODE_TRACEABILITY.md` | [archive/agv-phase2/assessment/REQUIREMENT_CODE_TRACEABILITY.md](archive/agv-phase2/assessment/REQUIREMENT_CODE_TRACEABILITY.md) |
| `docs/reports/agv-phase2-assessment/TEST_ASSESSMENT.md` | [archive/agv-phase2/assessment/TEST_ASSESSMENT.md](archive/agv-phase2/assessment/TEST_ASSESSMENT.md) |
| `docs/reports/agv-phase2/P0_REVISION_1_1_CORRECTION_REPORT.md` | [archive/agv-phase2/revisions/P0_REVISION_1_1_CORRECTION_REPORT.md](archive/agv-phase2/revisions/P0_REVISION_1_1_CORRECTION_REPORT.md) |
| `docs/reports/agv-phase2/P0_REVISION_1_2_CHANGE_REPORT.md` | [archive/agv-phase2/revisions/P0_REVISION_1_2_CHANGE_REPORT.md](archive/agv-phase2/revisions/P0_REVISION_1_2_CHANGE_REPORT.md) |
| `docs/reviews/BILINGUAL_I18N_COMPLETION_REPORT.md` | [archive/reviews/BILINGUAL_I18N_COMPLETION_REPORT.md](archive/reviews/BILINGUAL_I18N_COMPLETION_REPORT.md) |
| `docs/reviews/CR-032_UI_WALKTHROUGH_2026-11-23.md` | [archive/reviews/CR-032_UI_WALKTHROUGH_2026-11-23.md](archive/reviews/CR-032_UI_WALKTHROUGH_2026-11-23.md) |
| `docs/reviews/CR008A_POST_IMPLEMENTATION_REVIEW.md` | [archive/reviews/CR008A_POST_IMPLEMENTATION_REVIEW.md](archive/reviews/CR008A_POST_IMPLEMENTATION_REVIEW.md) |
| `docs/reviews/CR008B-1_DOCUMENTATION_CORRECTION.md` | [archive/reviews/CR008B-1_DOCUMENTATION_CORRECTION.md](archive/reviews/CR008B-1_DOCUMENTATION_CORRECTION.md) |
| `docs/reviews/CR008B-2_PLC_INTAKE_RELIABILITY.md` | [archive/reviews/CR008B-2_PLC_INTAKE_RELIABILITY.md](archive/reviews/CR008B-2_PLC_INTAKE_RELIABILITY.md) |
| `docs/reviews/CR008B-3_CANCELPENDING_RECONCILIATION.md` | [archive/reviews/CR008B-3_CANCELPENDING_RECONCILIATION.md](archive/reviews/CR008B-3_CANCELPENDING_RECONCILIATION.md) |
| `docs/reviews/CR008B-4_RACK_INVENTORY_UNCERTAINTY.md` | [archive/reviews/CR008B-4_RACK_INVENTORY_UNCERTAINTY.md](archive/reviews/CR008B-4_RACK_INVENTORY_UNCERTAINTY.md) |
| `docs/reviews/CR008B-5_AGV_HOLD_ANALYSIS.md` | [archive/reviews/CR008B-5_AGV_HOLD_ANALYSIS.md](archive/reviews/CR008B-5_AGV_HOLD_ANALYSIS.md) |
| `docs/reviews/CR008B-5_IMPLEMENTATION_REPORT.md` | [archive/reviews/CR008B-5_IMPLEMENTATION_REPORT.md](archive/reviews/CR008B-5_IMPLEMENTATION_REPORT.md) |
| `docs/reviews/CR008B_DESIGN_IMPACT_ASSESSMENT.md` | [archive/reviews/CR008B_DESIGN_IMPACT_ASSESSMENT.md](archive/reviews/CR008B_DESIGN_IMPACT_ASSESSMENT.md) |
| `docs/reviews/CR008B_FINAL_IMPLEMENTATION_REPORT.md` | [archive/reviews/CR008B_FINAL_IMPLEMENTATION_REPORT.md](archive/reviews/CR008B_FINAL_IMPLEMENTATION_REPORT.md) |
| `docs/reviews/CR008_CR009_COMPLETION_REPORT.md` | [archive/reviews/CR008_CR009_COMPLETION_REPORT.md](archive/reviews/CR008_CR009_COMPLETION_REPORT.md) |
| `docs/reviews/CR010_ROUTE_ENGINE_IMPLEMENTATION_REPORT.md` | [archive/reviews/CR010_ROUTE_ENGINE_IMPLEMENTATION_REPORT.md](archive/reviews/CR010_ROUTE_ENGINE_IMPLEMENTATION_REPORT.md) |
| `docs/reviews/CR021_COMPLETION_REPORT.md` | [archive/reviews/CR021_COMPLETION_REPORT.md](archive/reviews/CR021_COMPLETION_REPORT.md) |
| `docs/reviews/CR021_PHASE1_LOGGING_SURVEY_AND_PROPOSAL.md` | [archive/reviews/CR021_PHASE1_LOGGING_SURVEY_AND_PROPOSAL.md](archive/reviews/CR021_PHASE1_LOGGING_SURVEY_AND_PROPOSAL.md) |
| `docs/reviews/DASHBOARD_REDESIGN_REPORT.md` | [archive/reviews/DASHBOARD_REDESIGN_REPORT.md](archive/reviews/DASHBOARD_REDESIGN_REPORT.md) |
| `docs/reviews/INITIAL_REVIEW_2026-11-26.md` | [archive/reviews/INITIAL_REVIEW_2026-11-26.md](archive/reviews/INITIAL_REVIEW_2026-11-26.md) |
| `docs/reviews/PERSONALIZATION_FOLLOWUP_REPORT.md` | [archive/reviews/PERSONALIZATION_FOLLOWUP_REPORT.md](archive/reviews/PERSONALIZATION_FOLLOWUP_REPORT.md) |
| `docs/reviews/PERSONALIZATION_UI_REPORT.md` | [archive/reviews/PERSONALIZATION_UI_REPORT.md](archive/reviews/PERSONALIZATION_UI_REPORT.md) |
| `docs/reviews/PLC_CONNECTIVITY_DIAGNOSIS_2026-11-18.md` | [archive/reviews/PLC_CONNECTIVITY_DIAGNOSIS_2026-11-18.md](archive/reviews/PLC_CONNECTIVITY_DIAGNOSIS_2026-11-18.md) |
| `docs/reviews/SERVER_RESOURCE_MONITORING_REPORT.md` | [archive/reviews/SERVER_RESOURCE_MONITORING_REPORT.md](archive/reviews/SERVER_RESOURCE_MONITORING_REPORT.md) |
| `docs/reviews/TASK_HISTORY_I18N_REPORT.md` | [archive/reviews/TASK_HISTORY_I18N_REPORT.md](archive/reviews/TASK_HISTORY_I18N_REPORT.md) |
| `docs/reviews/UI_NAVIGATION_REFINEMENT_REPORT.md` | [archive/reviews/UI_NAVIGATION_REFINEMENT_REPORT.md](archive/reviews/UI_NAVIGATION_REFINEMENT_REPORT.md) |
| `docs/reviews/USER_CONFIGURATION_REPORT.md` | [archive/reviews/USER_CONFIGURATION_REPORT.md](archive/reviews/USER_CONFIGURATION_REPORT.md) |
| `docs/reviews/USER_PROFILE_MISSING_FIELDS.md` | [archive/reviews/USER_PROFILE_MISSING_FIELDS.md](archive/reviews/USER_PROFILE_MISSING_FIELDS.md) |
| `report/CR-007_Docker_Acceptance_Package_Report.md` | [archive/audits-2026-11-16/CR-007_Docker_Acceptance_Package_Report.md](archive/audits-2026-11-16/CR-007_Docker_Acceptance_Package_Report.md) |
| `report/CR-007_Docker_Acceptance_Test_Report.md` | [archive/audits-2026-11-16/CR-007_Docker_Acceptance_Test_Report.md](archive/audits-2026-11-16/CR-007_Docker_Acceptance_Test_Report.md) |
| `report/CR-007_Logging_Diagnostics_Gap_Analysis.md` | [archive/audits-2026-11-16/CR-007_Logging_Diagnostics_Gap_Analysis.md](archive/audits-2026-11-16/CR-007_Logging_Diagnostics_Gap_Analysis.md) |
| `report/CR-008A_Dispatch_Scheduler_Reliability_Report.md` | [archive/audits-2026-11-16/CR-008A_Dispatch_Scheduler_Reliability_Report.md](archive/audits-2026-11-16/CR-008A_Dispatch_Scheduler_Reliability_Report.md) |
| `report/ETEK_WCS_Current_State_Audit_2026-11-16.md` | [archive/audits-2026-11-16/ETEK_WCS_Current_State_Audit_2026-11-16.md](archive/audits-2026-11-16/ETEK_WCS_Current_State_Audit_2026-11-16.md) |
| `report/ETEK_WCS_FULL_CODE_REVIEW_2026-11-16.md` | [archive/audits-2026-11-16/ETEK_WCS_FULL_CODE_REVIEW_2026-11-16.md](archive/audits-2026-11-16/ETEK_WCS_FULL_CODE_REVIEW_2026-11-16.md) |
| `report/ETEK_WCS_Full_Product_Audit_2026-11-16.md` | [archive/audits-2026-11-16/ETEK_WCS_Full_Product_Audit_2026-11-16.md](archive/audits-2026-11-16/ETEK_WCS_Full_Product_Audit_2026-11-16.md) |

Ảnh: 95 file trong `docs/reviews/{dashboard, personalization, personalization-followup, server-monitoring, ui-navigation, user-configuration}/` chuyển sang `docs/archive/reviews/` cùng tên thư mục.

## 5. Giữ tại chỗ

- Baseline, ADR, hồ sơ Change Request (gồm CR-047, CR-048) và các file SQL migration đi kèm.
- IO Map 22/11/2026 và README trạng thái. IO Map cũ không được khôi phục. Bộ tài liệu PLC 20/11 và bản nháp 10/11 giữ tại chỗ theo CLAUDE.md 7.8 (không xóa tài liệu PLC cũ).
- Câu hỏi, đề xuất và kế hoạch còn mở trong `docs/reviews/` và `reports/agv-phase2-assessment/OPEN_QUESTIONS.md`.
- Bằng chứng nghiệm thu `reports/cr-046-ui`, `cr-047`, `cr-048`; thiết kế CR-036 trong `reports/phase2`; checklist và script nghiệm thu Docker.
- Ngoài phạm vi chuyển: `reference/` (mã tham chiếu được test dùng), README của dự án và frontend.

## 6. Liên kết và kiểm tra

- Liên kết Markdown và đường dẫn trỏ tới file đã chuyển được viết lại trong 25 file, gồm `deployment/README.md`. Không mã nguồn, script hay CI nào trỏ tới file đã chuyển hoặc xóa.
- Kiểm tra mọi liên kết Markdown tương đối trong repository: không còn liên kết hỏng.
