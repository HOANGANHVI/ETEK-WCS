# CR-050 – Báo cáo nghiệm thu Simulation PLC + RCS/AMR (2026-11-27)

Nhánh `feature/cr-050-simulator`, nền `8676511` cộng các thay đổi ở mục 4 (commit kế tiếp trên nhánh).

## 1. Kết luận

- **Simulation vận hành** (tạo yêu cầu bằng chức năng hiện có, WCS điều phối, AMR Simulator hoàn tất): chạy tự động đầu cuối; còn 2 ca phục hồi không đạt.
- **Simulation qua IO** (tín hiệu PLC tự kích hoạt, handshake, phản hồi): **BLOCKED**, thiếu contract PLC.
- **Không đạt FULL SIMULATION PASS.**

Tổng 51 ca: **44 PASS, 2 FAIL, 5 BLOCKED**.

| Hạng mục | Kết quả | Số ca |
|---|---|---|
| PLC Simulator standalone | **PASS** | 7 PASS, 0 FAIL, 0 BLOCKED |
| PLC IO kích hoạt nghiệp vụ | **BLOCKED** | 0 PASS, 0 FAIL, 1 BLOCKED |
| WCS → PLC Feedback | **BLOCKED** | 0 PASS, 0 FAIL, 1 BLOCKED |
| RCS Simulator standalone (Parent Task/Trigger: BLOCKED) | **PASS** | 3 PASS, 0 FAIL, 1 BLOCKED |
| AMR Movement | **PASS** | 2 PASS, 0 FAIL, 0 BLOCKED |
| AMR Pick/Drop (E2E-01, E2E-02 và NEG-07…13) | **PASS** | 2 PASS, 0 FAIL, 0 BLOCKED |
| E2E qua chức năng tạo yêu cầu | **PASS** | 2 PASS, 0 FAIL, 0 BLOCKED |
| E2E qua PLC IO | **BLOCKED** | 0 PASS, 0 FAIL, 1 BLOCKED |
| Negative Scenarios (NEG-21, NEG-23 không đạt; NEG-03 bị chặn) | **FAIL** | 20 PASS, 2 FAIL, 1 BLOCKED |
| Simulation/Real Isolation | **PASS** | 9 PASS, 0 FAIL, 0 BLOCKED |

"PASS" của một nhóm có ca BLOCKED chỉ nói về phần đã đủ contract.

## 2. Môi trường và bằng chứng

- **QA cách ly:** DB mới `wcs_qa_e2e` (đã xóa sau khi chạy), backend 5111, PLC và RCS giả lập, đội 2 AMR (`9001`, `9002`), ghi PLC thật và Real Dispatch tắt. Mọi kết quả đọc qua API có đăng nhập và truy vấn DB.
- **Môi trường 5007 (chỉ đọc):** sau restart 13:37, Admin tạo 6 lệnh (13:44–13:51, BP-01 và BP-03). Cả 6 hoàn tất, Pick/Drop nguồn `Simulator`, AMR `2048`/`1357`, có sự kiện rời cổng, Audit không có thao tác Pick/Drop/Exit tay. Chỉ A1, A2 có cổng PLC liên kết.
- **Hồi quy mục tiêu:** 458/458 (gồm Cr030, Cr044, PlcWrite*: bảo vệ ghi thật và Real Dispatch; Cr050: chế độ Real từ chối mô phỏng).
- **Frontend:** tsc, lint, 11 bộ smoke, build đạt; `check-i18n` còn 7 lỗi mẫu log RCS có từ trước.
- **Trình duyệt:** [đội 2 AMR và AMR giữ cổng](5-exit-stall-two-amrs.png) · [giám sát đội AMR](6-fleet-two-amrs.png).

## 3. Contract PLC

| Điểm | Tình trạng |
|---|---|
| Catalog IO và địa chỉ mặc định theo IO Map 22/11; đọc/ghi trong PLC giả lập | Đã triển khai, **chưa được kỹ sư PLC xác nhận** |
| Tiếp nhận yêu cầu theo `Pickup_Request`, `Request_ID`… (IO Map v2) | Đã triển khai theo bản đã ngừng dùng |
| Một handshake mỗi PLC, FIFO (D-1) | Có quyết định; thiếu điểm bắt đầu/kết thúc handshake → chưa triển khai |
| `Mission_Feedback` (D-2), xác nhận và định danh rack (D-4) | Có yêu cầu; thiếu định dạng và tín hiệu → chưa triển khai |
| `PLC_MissionCompleted` 1 lỗi / 2 thành công | Có ý nghĩa giá trị; thiếu cách gắn với đúng giao dịch → chưa triển khai |
| Trigger cạnh lên hay mức; `PLC_MissionRequest` 1/2/10; `PLC_ReaddyTranfer`; `AGV_RequestTranfer` 1/4; `AGV_TranferCompleted` 3 và 5/6; thanh ghi dùng chung `D16n0`; reset; HeartBeat; timeout; định danh và chống trùng yêu cầu | **Chưa xác nhận → chưa triển khai** |

Cần nhà cung cấp RCS xác nhận: Parent Task, trigger Pick/Drop của chặng cấu hình được, callback lỗi chặng, gửi lại trigger, mã trạng thái cuối.

## 4. Thay đổi trong đợt này

- `Rcs:Fleet:SimulatorRobotCodes` (tùy chọn): đặt tên đội AMR giả lập theo mã AGV. Nếu chỉ đặt `SimulatorSize=2` ở chế độ `Legacy`, sẽ có `SIM-001`, `SIM-002` đứng yên cùng hai robot sinh thêm theo mã AGV.
- Lỗi giả lập mới `ExitStall` ("AMR không rời cổng").
- Test: 3 ca mới trong `Cr050SimulatorTests`.

## 5. Điểm còn mở

| Mã | Nội dung | Ảnh hưởng | Cần quyết định |
|---|---|---|---|
| GAP-1 | Lệnh vận chuyển `Failed` sau Pick: rack ở `InTransit`; sửa, xóa, drop, release-racks đều bị từ chối | Rack kẹt, không thao tác nào của WCS gỡ được | Thao tác đối soát: Admin xác nhận vị trí thật của rack |
| GAP-2 | Location bị gắn cờ đối soát (PLC báo rack mà WCS không ghi): đóng giao dịch cổng không gỡ cờ | Location không dùng lại được | Thao tác đối soát Location của engine lệnh vận chuyển |
| OPEN-1 | Chặng báo lỗi hoặc AMR treo: không có quy tắc hủy; chỉ phục hồi hoặc xác nhận tay | Lệnh chờ người xử lý | Quy tắc hủy/đối soát chặng đang chạy |
| OPEN-2 | Chưa có timeout cho chặng tại Source/Destination | Không tự cảnh báo | Giá trị và hành động timeout |
| OPEN-3 | Sau restart hoặc tắt Simulator, chặng đang dở không được nhận lại | Hoàn tất bằng tay (đã kiểm tra) | Có cần nhận lại không |
| OPEN-4 | Từ chối sau Pick (xác nhận tay khi RCS chưa trả lời) vẫn chuyển lệnh `Failed` và nhả giữ chỗ | Dẫn tới GAP-1 | Có nên chuyển lệnh sang đối soát |

## 6. Kết quả từng ca

| ID | Nhóm | Kỳ vọng | Thực tế | Kết quả | Nguyên nhân nếu không đạt |
|---|---|---|---|---|---|
| ISO-01 | Isolation | AMR simulation OFF after start | enabled=false runs=0 | **PASS** |  |
| ISO-02 | Isolation | PLC simulation page OFF after start | active=false | **PASS** |  |
| ISO-03 | Isolation | PLC runtime Simulator, live and write disabled, real dispatch off | mode=Simulator live=false write=false realDispatch=false exec=Simulator | **PASS** |  |
| AMR-01 | RCS Simulator | Fleet monitoring shows exactly the two configured AMRs with position, battery and link | 9001(AGV1) bat=99 @1000,2000 Fresh status=4; 9002(AGV2) bat=98 @2000,2000 Fresh status=4 | **PASS** |  |
| PLC-S-01 | PLC Simulator standalone | A signal cannot be set while the PLC simulation is off | SIMULATION_NOT_ACTIVE | **PASS** |  |
| PLC-S-02 | PLC Simulator standalone | Disconnect is visible with non-good quality, reconnect restores it | down=Faulted quality=Disconnected up=Connected reconnects=1 | **PASS** |  |
| PLC-S-03 | PLC Simulator standalone | A simulated IO value is read back by polling and by an immediate read | set=200 polled=10 Simulated @D1500; read=10 | **PASS** |  |
| PLC-S-04 | PLC Simulator standalone | A station fault affects only that station and recovers | A1=Bad A2=Simulated recovered=true | **PASS** |  |
| PLC-S-05 | PLC Simulator standalone | In Simulator mode an approved output can be written and read back; it stays inside the simulator (real writes disabled) | 200 readBack=VERIFIED value=1; frames to a PLC=0 | **PASS** |  |
| PLC-S-06 | PLC Simulator standalone | Signal changes and resets are in the history (who, what, value) | PLC_SIM_SIGNAL,PLC_SIM_RESET,PLC_SIM_ACTIVATE | **PASS** |  |
| PLC-S-07 | PLC Simulator standalone | Deactivate clears the simulated values | -,-,- | **PASS** |  |
| IO-01 | PLC IO triggers business | A 22/11 IO signal creates a Transport Order through the backend | values 1/2/10 -> 1:Rejected/REQUEST_ID_MISSING, 2:Rejected/REQUEST_ID_MISSING, 10:Rejected/REQUEST_ID_MISSING; orders created=0; the intake reads the retired signal names and no trigger contract is approved | **BLOCKED** | Bộ tiếp nhận đọc tên tín hiệu cũ (IO Map v2); chưa có contract kích hoạt cho IO Map 22/11 |
| IO-02 | WCS -> PLC feedback | WCS answers the handshake on PLC outputs | successful writes (the manual test write of PLC-S-05 only)=1, PLC frames=0; no feedback logic exists and its contract is not approved | **BLOCKED** | Chưa có logic phản hồi và chưa có contract được duyệt |
| NEG-02 | Negative | A repeated request id creates one order only (request intake, old signal contract, injected by Admin - not IO) | Accepted/ACCEPTED, Duplicate/DUPLICATE_REQUEST_ID | **PASS** |  |
| NEG-03 | Negative | PLC does not answer during a handshake (timeout) | no handshake, timeout or reset ownership is approved | **BLOCKED** | Chưa có handshake, timeout, quyền reset |
| E2E-01 | E2E operational | BP-01: request -> order -> RCS -> AMR 9001 -> pick -> drop -> completed, ports released, no manual step | order=Completed leg=Completed+pick+drop A1->B1 robot=- ports=[] res=0; inventory=PickConfirmed,DropConfirmed; racks QA-A1-3@B1:Full | **PASS** |  |
| E2E-01-AMR | AMR Movement | AMR 9001 moves, lifts, returns to idle, battery drops; two AMRs only | robots=9001,9002 positions=12 battery 99->98.8 fleet statuses=4,2 phases=ToSource,Picking,ToDestination,Dropping,Leaving,Completed | **PASS** |  |
| E2E-01-LOG | RCS Simulator | Message log and event log of the run are recorded | messages: Create Leg Task[Sent] / Create Leg Task Response[Accepted] / Continue Leg Task[Sent] / Continue Leg Task Response[Accepted] / Simulated AGV Event: PickConfirmed[Applied] / Simulated AGV Event: AMR left port[Applied] / Simulated AGV Event: DropConfirmed[Applied] / Simulated AGV Event: AMR left port[Applied]; events: ORDER_CREATED > QUEUE_ADMITTED > LEG_CREATED > LEG_DISPATCHING > LEG_ACCEPTED > PICK_CONFIRMED > DESTINATION_ASSIGNED > DROP_CONFIRMED > ORDER_COMPLETED | **PASS** |  |
| E2E-02 | E2E operational | BP-02: request -> order -> RCS -> AMR 9002 -> pick -> drop -> completed, ports released, no manual step | order=Completed leg=Completed+pick+drop B1->A1 robot=- ports=[] res=0; inventory=PickConfirmed,DropConfirmed; racks QA-B1-4@A1:Empty | **PASS** |  |
| E2E-02-AMR | AMR Movement | AMR 9002 moves, lifts, returns to idle, battery drops; two AMRs only | robots=9001,9002 positions=16 battery 98->97.7 fleet statuses=4,2 phases=Queued,ToSource,Picking,ToDestination,Dropping,Leaving,Completed | **PASS** |  |
| E2E-02-LOG | RCS Simulator | Message log and event log of the run are recorded | messages: Create Leg Task[Sent] / Create Leg Task Response[Accepted] / Simulated AGV Event: PickConfirmed[Applied] / Simulated AGV Event: AMR left port[Applied] / Simulated AGV Event: DropConfirmed[Applied] / Simulated AGV Event: AMR left port[Applied]; events: ORDER_CREATED > QUEUE_ADMITTED > LEG_CREATED > LEG_DISPATCHING > LEG_ACCEPTED > PICK_CONFIRMED > DROP_CONFIRMED > ORDER_COMPLETED | **PASS** |  |
| E2E-IO | E2E via PLC IO | PLC IO -> WCS handshake -> ... -> PLC receives the result | blocked at the first step (IO-01) and the last (IO-02) | **BLOCKED** | Phụ thuộc IO-01 và IO-02 |
| RCS-01 | RCS Simulator | Parent task and pick/drop trigger of the RCS contract | runs are grouped by the order request key; no RCS Parent Task / trigger contract is confirmed for legs | **BLOCKED** | Chưa có contract Parent Task / trigger của RCS cho chặng |
| NEG-04 | Negative | RCS rejects: order Failed, reservation and untouched ports released, rack stays | order=Failed leg=Failed A1->? robot=- ports=[] res=0; racks QA-A1-5@A1:Full | **PASS** |  |
| NEG-05 | Negative | RCS timeout on dispatch is not a rejection: retried, completes once | order=Completed leg=Completed+pick+drop A1->B1 robot=- ports=[] res=0 | **PASS** |  |
| NEG-06 | Negative | RCS disconnected while the AMR moves: nothing reaches WCS, ports kept; completes after reconnect | during: order=InProgress leg=InProgress A1->? robot=- ports=[A1:AgvAssigned] res=1; fleet=Degraded/RCS_UNREACHABLE; after: order=Completed leg=Completed+pick+drop A1->B1 robot=- ports=[] res=0 | **PASS** |  |
| NEG-07 | Negative | Movement failure: state kept (no false completion, no release, no second request on the port, no cancel in motion); recovery completes once | during: order=InProgress leg=InProgress A1->? robot=- ports=[A1:AgvAssigned] res=1; racks QA-A1-8@A1:Full; cancel=ORDER_NOT_CANCELLABLE; 2nd request=PORT_BUSY; events=LEG_RCS_INTERRUPTED; after: order=Completed leg=Completed+pick+drop A1->B1 robot=- ports=[] res=0 | **PASS** |  |
| NEG-08 | Negative | Pick failure: state kept (no false completion, no release, no second request on the port, no cancel in motion); recovery completes once | during: order=InProgress leg=InProgress A1->? robot=- ports=[A1:AgvAssigned] res=1; racks QA-A1-9@A1:Full; cancel=ORDER_NOT_CANCELLABLE; 2nd request=PORT_BUSY; events=LEG_RCS_INTERRUPTED; after: order=Completed leg=Completed+pick+drop A1->B1 robot=- ports=[] res=0 | **PASS** |  |
| NEG-09 | Negative | Drop failure: state kept (no false completion, no release, no second request on the port, no cancel in motion); recovery completes once | during: order=InProgress leg=InProgress+pick A1->B1 robot=- ports=[B1:AgvAssigned] res=1; racks QA-A1-10@?:InTransit; cancel=ORDER_NOT_CANCELLABLE; 2nd request=not tried (the source is empty after the pick: see NEG-22); events=LEG_RCS_INTERRUPTED; after: order=Completed leg=Completed+pick+drop A1->B1 robot=- ports=[] res=0 | **PASS** |  |
| NEG-10 | Negative | AMR stuck on the way to the source (no answer): state kept (no false completion, no release, no second request on the port, no cancel in motion); recovery completes once | during: order=InProgress leg=InProgress A1->? robot=- ports=[A1:AgvAssigned] res=1; racks QA-A1-11@A1:Full; cancel=ORDER_NOT_CANCELLABLE; 2nd request=PORT_BUSY; events=none; after: order=Completed leg=Completed+pick+drop A1->B1 robot=- ports=[] res=0 | **PASS** |  |
| NEG-11 | Negative | AMR stuck after the pick (towards the destination): rack on the AMR, recovery completes. WCS has no leg timeout rule | during: order=InProgress leg=InProgress+pick A1->B1 robot=- ports=[B1:AgvAssigned,A1:HandoverCompleted] res=1; after: order=Completed leg=Completed+pick+drop A1->B1 robot=- ports=[] res=0 | **PASS** |  |
| NEG-12 | Negative | Delayed RCS answer: nothing assumed meanwhile, one pick and one drop | during: order=InProgress leg=InProgress A1->? robot=- ports=[A1:AgvAssigned] res=1; inventory=PickConfirmed,DropConfirmed | **PASS** |  |
| NEG-13 | Negative | Repeated RCS answer is a duplicate, inventory moves once | 06:55:23.519 PickConfirmed sent again: Duplicate; inventory=PickConfirmed,DropConfirmed | **PASS** |  |
| NEG-14 | Negative | AMR does not leave the destination port: port stays held by time, released only when it leaves; queued work follows | held: order=Completed leg=Completed+pick+drop A1->B1 robot=- ports=[B1:HandoverCompleted] res=0; next: order=InProgress leg=InProgress A1->? robot=- ports=[A1:AgvAssigned,B1:HandoverCompleted] res=1; released=true; next ends=Completed | **PASS** |  |
| NEG-15 | Negative | No AMR available: the work waits with the reason, nothing sent; runs when the AMR is back | during: order=InProgress leg=Ready A1->? robot=- ports=[A1:Accepted] res=1 reason=AGV_UNAVAILABLE; after: order=Completed leg=Completed+pick+drop A1->B1 robot=- ports=[] res=0 | **PASS** |  |
| NEG-16 | Negative | All destinations full: waits with the reason; continues when one is free | filled=8; during: order=Waiting/DestinationUnavailable_AgvHoldingRack leg=Waiting+pick A1->? robot=- ports=[] res=0 reason=No destination; AMR holds the rack; after: order=Completed leg=Completed+pick+drop A1->B2 robot=- ports=[] res=0 | **PASS** |  |
| NEG-01 | Negative | PLC disconnects during the transaction (no IO handshake exists): AMR work continues, state consistent after reconnect | during: order=InProgress leg=InProgress A1->? robot=- ports=[A1:AgvAssigned:suspended] res=1; after: order=Completed leg=Completed+pick+drop A1->B1 robot=- ports=[] res=0 | **PASS** |  |
| NEG-17 | Negative | Simulator switched off during the transaction: nothing changes by itself, not taken over again, finished by hand (drop + exit) | AMR sim after interrupt=false, PLC page=true; order=InProgress leg=InProgress+pick A1->B1 robot=- ports=[B1:AgvAssigned,A1:HandoverCompleted] res=1; after re-enable: runs=0; cancel=ORDER_NOT_CANCELLABLE; drop=Applied; end: order=Completed leg=Completed+pick+drop A1->B1 robot=- ports=[] res=0 | **PASS** |  |
| NEG-18 | Negative | Backend restart during the transaction: nothing changes by itself, not taken over again, finished by hand (drop + exit) | AMR sim after interrupt=false, PLC page=false; order=InProgress leg=InProgress+pick A1->B1 robot=- ports=[B1:AgvAssigned,A1:HandoverCompleted] res=1; after re-enable: runs=0; cancel=ORDER_NOT_CANCELLABLE; drop=Applied; end: order=Completed leg=Completed+pick+drop A1->B1 robot=- ports=[] res=0 | **PASS** |  |
| ISO-04 | Isolation | Both simulations OFF after a restart in the middle of work | amr=false plcPage=false | **PASS** |  |
| ISO-05 | Isolation | Work created before the AMR simulation was switched on is not executed | order=InProgress leg=InProgress A1->? robot=- ports=[A1:AgvAssigned] res=1 | **PASS** |  |
| ISO-06 | Isolation | Switched off: new work is not executed by the AMR simulation | order=InProgress leg=InProgress A1->? robot=- ports=[A1:AgvAssigned] res=1 | **PASS** |  |
| NEG-19 | Negative | Rejected after a pick: rack kept in transit and held, ports kept for reconciliation | order=Failed leg=Failed+pick A1->B1 robot=- ports=[B1:ReconciliationRequired,A1:ReconciliationRequired] res=0; racks QA-A1-37@?:InTransit; racksHeld=true | **PASS** |  |
| NEG-20 | Negative | Ports in reconciliation can be resolved and released by an Admin | B1 resolve=Failed; A1 resolve=HandoverCompleted; exit=Completed | **PASS** |  |
| NEG-21 | Negative | The rack of a failed order can be given its real position (reconciliation) | release-racks=RACK_POSITION_NOT_CONFIRMED; edit=RACK_IN_TRANSPORT; delete=RACK_IN_TRANSPORT; drop=Rejected; rack status=InTransit: no WCS operation places it | **FAIL** | Khoảng trống có sẵn của engine lệnh vận chuyển (mục 5, GAP-1); cần quy tắc được duyệt |
| NEG-22 | Negative | The PLC reports a rack WCS does not know: nothing dispatched, location and port go to reconciliation | ReconciliationRequired/RECON_PLC_INVENTORY_MISMATCH; locationFlag=t | **PASS** |  |
| NEG-23 | Negative | The reconciled location can be put back into use | port closed=true; locationFlag=t; add rack=LOCATION_RECONCILIATION_REQUIRED: no operation of the transport order engine clears the flag | **FAIL** | Khoảng trống có sẵn của engine lệnh vận chuyển (mục 5, GAP-2); cần quy tắc được duyệt |
| ISO-07 | Isolation | No frame to a physical PLC; the only successful write is the one to the simulator | frames=0 simulatorWrites=1 runtime=Simulator | **PASS** |  |
| ISO-08 | Isolation | Every RCS message and order is Simulator | protocols=SIMULATOR (in-process, no network) messages=163 | **PASS** |  |
| ISO-09 | Isolation | The backend has no connection outside this machine | pid=18076 nonLoopback=0 plcSubnet=0 | **PASS** |  |
| AUD-01 | Audit | Admin actions of the run are audited | AMR_SIM_CONFIG=51 AMR_SIM_FAULT=32 AMR_SIM_RECOVER=6 PLC_CONNECTION_UPDATE=4 PLC_IO_ADDRESS_APPROVE=48 PLC_PORT_LINK=8 PLC_SIM_ACTIVATE=6 PLC_SIM_DEACTIVATE=1 PLC_SIM_EVALUATE=3 PLC_SIM_REQUEST=26 PLC_SIM_RESET=1 PLC_SIM_SIGNAL=6 PLC_SIMULATOR_ONLINE=4 PLC_SIMULATOR_SET_VALUE=4 PLC_SIMULATOR_STATION_FAULT=2 PLC_TEST_WRITE=1 ROUTING_LEG_DROP=7 ROUTING_LEG_PICK=5 ROUTING_ORDER_CANCEL=6 ROUTING_ORDER_RELEASE_RACKS=1 ROUTING_PLC_REQUEST=2 ROUTING_TX_EXIT=11 ROUTING_TX_RECONCILE=4 | **PASS** |  |
