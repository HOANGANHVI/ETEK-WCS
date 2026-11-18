<#
.SYNOPSIS
  Dieu phoi cac bai nghiem thu Docker H1-H6 cua CR-007 (dung noi dung trong docs\DEPLOYMENT_TEST_CHECKLIST.md) va xuat bao cao.
.DESCRIPTION
  - Khong tu bo qua bai nao: moi bai deu co ket qua DAT / KHONG DAT / CHUA THE KIEM TRA kem ly do.
  - DAT chi duoc ghi khi co bang chung thuc te trong thu muc bang chung (lenh, exit code, stdout/stderr, trang thai container, log Docker).
  - Khong xoa volume/database, khong "docker system prune", khong "compose down", khong tu dong restart/dung container.
    Cac thao tac anh huong (H1 che do chay, H3, H6, X2) can co/flag xac nhan hoac nguoi van hanh go dung cum xac nhan.
  - Khong ket noi toi PLC/RCS/AGV that. Khong dung vao PostgreSQL cua may phat trien (chi lam viec voi container trong stack).
  Exit code: 0 = tat ca bai da chon DAT; 1 = co bai KHONG DAT; 2 = khong co bai KHONG DAT nhung con bai CHUA THE KIEM TRA.
.EXAMPLE
  # kiem tra moi truong truoc
  powershell -NoProfile -ExecutionPolicy Bypass -File docs\docker-acceptance\preflight.ps1
  # H2, H4, H5 (chi doc) + doc log lan chay deploy/start gan nhat cho H1
  powershell -NoProfile -ExecutionPolicy Bypass -File docs\docker-acceptance\run-acceptance.ps1 -Tests H1,H2,H4,H5
  # toan bo (se hoi xac nhan truoc H3 va H6)
  powershell -NoProfile -ExecutionPolicy Bypass -File docs\docker-acceptance\run-acceptance.ps1 -AllowStackStart
#>
param(
    [string[]]$Tests = @('H1', 'H2', 'H3', 'H4', 'H5', 'H6'),
    [string]$RepoDir,
    [string]$ConfigDir,
    [string]$LogDir,
    [string]$EvidenceRoot,
    [string]$Project = 'etek-wcs',
    [string]$BaseUrl,
    [string]$AdminUser = 'admin',
    [securestring]$AdminPassword,
    [switch]$NonInteractive,
    [switch]$AllowStackStart,
    [ValidateSet('auto', 'deploy', 'start')][string]$H1Script = 'auto',
    [switch]$ConfirmH3,
    [switch]$ConfirmH6,
    [switch]$ConfirmX2,
    [string]$ConfirmPostgresContainer,
    [int]$OutageSeconds = 120,
    [int]$ImportWaitSeconds = 90,
    [switch]$ManualUiCheck,
    [switch]$ManualPlcToggle
)
. (Join-Path $PSScriptRoot '_accept-common.ps1')
$ctx = New-AcceptContext -RepoDir $RepoDir -ConfigDir $ConfigDir -LogDir $LogDir -EvidenceRoot $EvidenceRoot -Project $Project -Tag 'acceptance'
# powershell -File truyen "H1,H2" thanh mot chuoi: tach theo dau phay/cham phay/khoang trang
$Tests = @($Tests | ForEach-Object { $_ -split '[,;\s]+' } | Where-Object { $_ } | ForEach-Object { $_.ToUpperInvariant() } | Select-Object -Unique)
$allowed = @('H1', 'H2', 'H3', 'H4', 'H5', 'H6', 'X1', 'X2')
foreach ($t in $Tests) { if ($allowed -notcontains $t) { Write-Host "Bài kiểm thử không hợp lệ: $t (hợp lệ: $($allowed -join ', '))" -ForegroundColor Red; exit 3 } }

try { Initialize-EvidenceDir $ctx } catch { Write-Host "Không tạo được thư mục bằng chứng $($ctx.EvidenceDir): $($_.Exception.Message)" -ForegroundColor Red; exit 3 }
Write-Host "Thư mục bằng chứng: $($ctx.EvidenceDir)" -ForegroundColor Cyan

# ------------------------------------------------------------------------------------------------------------------ mo ta bai (theo checklist goc, khong doi noi dung)
$Meta = [ordered]@{
    H1 = @{ Title = 'Log script'; Objective = 'Chạy script triển khai và kiểm tra log được ghi ngoài repository.'; Pre = 'Docker chạy; .env hợp lệ; (chế độ thực thi) có -AllowStackStart.'
            Cmd = 'chạy `deploy.ps1`; xem `C:\ETEK-WCS\logs`'; Expected = 'có deploy-*.log và deployment-events-*.jsonl, có bước/kết quả/exit code, không có mật khẩu' }
    H2 = @{ Title = 'Nhập sự kiện triển khai'; Objective = 'Sự kiện của lần chạy H1 xuất hiện trong Logging & Diagnostics (nhóm Triển khai).'; Pre = 'H1 đã có lần chạy; backend khỏe; đăng nhập Admin được.'
            Cmd = 'sau H1 mở `/logs` → nhóm *Triển khai* (chờ ≤ 1 phút)'; Expected = 'thấy các sự kiện của lần chạy' }
    H3 = @{ Title = 'Lưu log khi lỗi'; Objective = 'Khi script thất bại, log migrator/backend được lưu và sự kiện FAIL hiện trên màn hình.'; Pre = 'Stack đã có database (volume etek_wcs_pgdata); xác nhận gián đoạn; Admin đăng nhập được để kiểm màn hình.'
            Cmd = 'làm hỏng cố ý một bước (vd sai POSTGRES_PASSWORD rồi `start.ps1`)'; Expected = 'có failure-<run>-*.log (migrator/backend) và sự kiện FAIL trên màn hình' }
    H4 = @{ Title = 'Thư mục log chỉ-đọc'; Objective = 'Backend chỉ đọc được thư mục log được mount, không ghi được.'; Pre = 'Container backend đang chạy.'
            Cmd = '`docker exec etek-wcs-backend-1 sh -c "touch /deploy-logs/x"`'; Expected = 'bị từ chối (read-only)' }
    H5 = @{ Title = 'Log PLC trong Docker'; Objective = 'Sự kiện PLC xuất hiện trong log container với ngữ cảnh trạng thái và IP/cổng.'; Pre = 'Backend chạy; KHÔNG cho phép kết nối PLC thật; có sự kiện PLC (nếu không, bài chưa thể kiểm tra).'
            Cmd = '`docker compose -p etek-wcs logs backend` khi bật/tắt kết nối PLC thử'; Expected = 'thấy dòng `PLC PLC-0x STATE` kèm IP/cổng và trạng thái trước→sau' }
    H6 = @{ Title = 'Database mất kết nối'; Objective = 'Mất và khôi phục PostgreSQL của stack thử được ghi đúng một lần và hiện trên màn hình.'; Pre = 'Xác nhận dừng PostgreSQL; container được nhận diện chính xác; Admin đăng nhập được.'
            Cmd = '`docker stop etek-wcs-postgres-1` 2 phút rồi `start`'; Expected = '1 dòng DB_LOST, throttle, DB_RESTORED trên màn hình sau khi khôi phục' }
    X1 = @{ Title = 'status.ps1: RestartCount / OOMKilled (bổ sung)'; Objective = 'status.ps1 hiển thị số lần restart, OOMKilled và health probe cuối.'; Pre = 'Stack đang chạy.'
            Cmd = '`deployment\scripts\status.ps1`'; Expected = 'mỗi container có restarts=, oom=; dịch vụ có health có dòng last health probe' }
    X2 = @{ Title = 'Log còn sau khi khởi động lại backend (bổ sung)'; Objective = 'Sau khi restart backend, file log và sự kiện cũ còn, import không nhân đôi.'; Pre = 'Xác nhận restart backend; Admin đăng nhập được.'
            Cmd = '`docker restart etek-wcs-backend-1`'; Expected = 'file log và sự kiện cũ còn; số sự kiện DEPLOYMENT không nhân đôi' }
}

$records = New-Object System.Collections.Generic.List[object]
$base = $null
$cred = $null
$api = $null
$script:LastRun = $null            # ket qua H1 (runId, thoi diem, script)
$script:FailRun = $null            # run that bai cua H3

# ------------------------------------------------------------------------------------------------------------------ tien ich
function New-Rec([string]$id) { $r = New-TestRecord $id $Meta[$id].Title; $r.Expected = $Meta[$id].Expected; $r.Commands.Add($Meta[$id].Cmd); return $r }
function Fin($rec, [string]$status, [string]$reason) { $null = Complete-Test $ctx $rec $status $reason; $rec.Actual = $reason; return $rec }

function Get-DeployEvents([datetime]$Since) {
    $ev = @()
    if (-not (Test-Path -LiteralPath $ctx.LogDir)) { return $ev }
    foreach ($f in Get-ChildItem -LiteralPath $ctx.LogDir -Filter 'deployment-events-*.jsonl' -File -ErrorAction SilentlyContinue) {
        foreach ($line in [System.IO.File]::ReadAllLines($f.FullName)) {
            if (-not $line.Trim()) { continue }
            try { $o = $line | ConvertFrom-Json } catch { $ev += [pscustomobject]@{ _invalid = $true; _raw = $line; time = $null; _file = $f.Name }; continue }
            $t = $null; try { $t = [datetimeoffset]::Parse($o.time, [System.Globalization.CultureInfo]::InvariantCulture).LocalDateTime } catch { }
            if ($t -and $t -ge $Since) { $o | Add-Member -NotePropertyName _time -NotePropertyValue $t -Force; $o | Add-Member -NotePropertyName _file -NotePropertyValue $f.Name -Force; $ev += $o }
        }
    }
    return $ev
}

function Test-DockerReady([string]$TestId) {
    # (ready, reason)
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { return @($false, 'Không có docker trong PATH (chưa cài Docker Desktop/WSL2?). Chạy preflight.ps1.') }
    $r = Invoke-Docker $ctx @('info', '--format', '{{.ServerVersion}}|{{.OSType}}') 'daemon' $TestId
    if ($r.ExitCode -ne 0) { return @($false, "Docker daemon không phản hồi (docker info exit $($r.ExitCode)). Mở Docker Desktop và chờ Engine running.") }
    if ($r.Output -notmatch '\|linux') { return @($false, "Docker không ở chế độ Linux containers ($($r.Output)).") }
    if (-not (Test-Path -LiteralPath $ctx.ComposeFile)) { return @($false, "Không thấy $($ctx.ComposeFile).") }
    if (-not (Test-Path -LiteralPath $ctx.EnvFile)) { return @($false, "Không thấy $($ctx.EnvFile) (sao chép deployment\.env.example và điền POSTGRES_PASSWORD).") }
    $m = Get-EnvMap $ctx
    if ($m['PLC_ALLOW_LIVE_CONNECTION'] -eq 'true') { return @($false, 'PLC_ALLOW_LIVE_CONNECTION=true: không chạy kiểm thử khi cho phép kết nối PLC thật.') }
    return @($true, '')
}

function Ensure-Api([string]$TestId) {
    # (ok, reason). Dang nhap mot lan, dung lai.
    if ($script:api -and $script:api.Ok) { return $script:api }
    if (-not $script:base) { $script:base = Get-BaseUrl $ctx $BaseUrl }
    $h = Get-HealthzStatus $script:base
    if ($h -ne 200) { return @{ Ok = $false; Reason = "GET $($script:base)/healthz trả $h (backend/Nginx chưa sẵn sàng)." } }
    if (-not $script:cred) { $script:cred = Read-AdminCredential $ctx $AdminUser $AdminPassword $NonInteractive.IsPresent }
    $script:api = Connect-Api $ctx $script:base $script:cred
    return $script:api
}

function Wait-ServiceHealthy([string]$service, [int]$TimeoutSec, [string]$TestId) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $id = Get-ServiceContainer $ctx $service $TestId
        if ($id) { $i = Get-Inspect $ctx $id $TestId; if ($i -and $i.Running -and ($i.Health -eq 'healthy' -or -not $i.Health)) { return $i } }
        Start-Sleep -Seconds 5
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Get-StackSnapshot([string]$TestId) {
    $o = [ordered]@{}
    foreach ($s in 'postgres', 'backend', 'frontend') {
        $id = Get-ServiceContainer $ctx $s $TestId
        $o[$s] = if ($id) { Get-Inspect $ctx $id $TestId } else { $null }
    }
    return $o
}

function Save-Json($rec, [string]$name, $obj) {
    $p = Write-Evidence $ctx "$($rec.Id)\$name" ($obj | ConvertTo-Json -Depth 6)
    Add-EvidenceRef $rec "$($rec.Id)\$name"
}

# ------------------------------------------------------------------------------------------------------------------ H1
function Test-H1 {
    $rec = New-Rec 'H1'
    $branch = ''; try { $branch = (& git -C $ctx.RepoDir branch --show-current 2>$null) } catch { }
    $mode = if ($AllowStackStart) { 'thực thi' } else { 'quan sát' }
    Add-Step $ctx $rec "Chế độ H1: $mode (branch hiện tại: $branch)"
    $runStart = $null; $scriptName = $null

    if ($AllowStackStart) {
        $ready = Test-DockerReady 'H1'
        if (-not $ready[0]) { return Fin $rec 'BLOCKED' $ready[1] }
        $useDeploy = ($H1Script -eq 'deploy') -or ($H1Script -eq 'auto' -and $branch -eq 'main')
        if ($useDeploy -and $branch -ne 'main') { return Fin $rec 'BLOCKED' "deploy.ps1 chỉ chạy từ nhánh main (hiện là '$branch'). Dùng -H1Script start hoặc checkout main." }
        $scriptName = if ($useDeploy) { 'deploy.ps1' } else { 'start.ps1' }
        if (-not $useDeploy) { Add-Note $ctx $rec "Lệch có chủ ý so với checklist: dùng start.ps1 thay deploy.ps1 vì branch '$branch' không phải main (deploy.ps1 từ chối nhánh khác main)." }
        $marker = (Get-Date).AddSeconds(-1)
        $env:WCS_CONFIG_DIR = $ctx.ConfigDir; $env:WCS_LOG_DIR = $ctx.LogDir
        try { $r = Invoke-Logged $ctx 'powershell' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $ctx.DeployDir "scripts\$scriptName")) "chạy $scriptName" 'H1' }
        finally { Remove-Item Env:WCS_CONFIG_DIR, Env:WCS_LOG_DIR -ErrorAction SilentlyContinue }
        Add-Step $ctx $rec "$scriptName kết thúc với exit code $($r.ExitCode)"
        $runStart = $marker
    } else {
        $all = Get-DeployEvents ([datetime]::MinValue)
        $starts = @($all | Where-Object { $_.result -eq 'START' -and $_.script -in @('deploy.ps1', 'start.ps1') } | Sort-Object _time)
        if ($starts.Count -eq 0) { return Fin $rec 'BLOCKED' "Chưa có lần chạy deploy.ps1/start.ps1 nào trong $($ctx.LogDir). Chạy script rồi chạy lại, hoặc dùng -AllowStackStart để bộ công cụ chạy script." }
        $last = $starts[-1]; $scriptName = $last.script; $runStart = $last._time.AddSeconds(-1)
        Add-Step $ctx $rec "Quan sát lần chạy gần nhất: $scriptName lúc $($last._time.ToString('o')) (runId $($last.runId))"
        Add-Note $ctx $rec 'Chế độ quan sát: bộ công cụ không chạy script; bằng chứng là log của lần chạy thật gần nhất.'
    }

    $events = @(Get-DeployEvents $runStart | Where-Object { $_.script -eq $scriptName })
    $startEvt = @($events | Where-Object { $_.result -eq 'START' } | Sort-Object _time)
    if ($startEvt.Count -eq 0) { return Fin $rec 'FAIL' "Không thấy sự kiện START của $scriptName trong deployment-events-*.jsonl sau $($runStart.ToString('o'))." }
    $runId = $startEvt[-1].runId
    $run = @($events | Where-Object { $_.runId -eq $runId } | Sort-Object _time)
    $script:LastRun = [pscustomobject]@{ RunId = $runId; Script = $scriptName; Start = $startEvt[-1]._time; Events = $run }

    $textLogs = @(Get-ChildItem -LiteralPath $ctx.LogDir -Filter 'deploy-*.log' -File -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $runStart })
    $jsonLogs = @(Get-ChildItem -LiteralPath $ctx.LogDir -Filter 'deployment-events-*.jsonl' -File -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $runStart })
    Write-Evidence $ctx 'H1\log-files.txt' ((Get-ChildItem -LiteralPath $ctx.LogDir -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | ForEach-Object { "{0}  {1,10:N0} bytes  {2}" -f $_.LastWriteTime.ToString('o'), $_.Length, $_.Name }) -join "`r`n") | Out-Null
    Add-EvidenceRef $rec 'H1\log-files.txt'
    Write-Evidence $ctx 'H1\run-events.jsonl' (($run | ForEach-Object { ($_ | Select-Object id, runId, time, script, step, result, level, exitCode, message, host, commit | ConvertTo-Json -Compress) }) -join "`r`n") | Out-Null
    Add-EvidenceRef $rec 'H1\run-events.jsonl'

    $problems = @()
    if ($textLogs.Count -eq 0) { $problems += 'không có deploy-*.log mới' }
    if ($jsonLogs.Count -eq 0) { $problems += 'không có deployment-events-*.jsonl mới' }
    $bad = @($run | Where-Object { -not ($_.id -and $_.time -and $_.script -and $_.result -and $_.level -and ($null -ne $_.message) -and $_.host) })
    if ($bad.Count -gt 0) { $problems += "$($bad.Count) sự kiện thiếu trường bắt buộc" }
    if (@($run | Where-Object { $_.result -eq 'STEP' }).Count -lt 1) { $problems += 'không có sự kiện STEP (bước)' }
    $end = @($run | Where-Object { $_.result -eq 'END' })
    if ($end.Count -eq 0) { $problems += 'không có sự kiện END (exit code)' } elseif ($null -eq $end[-1].exitCode) { $problems += 'sự kiện END không có exitCode' }
    $touched = @(Get-ChildItem -LiteralPath $ctx.LogDir -File -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $runStart } | ForEach-Object { $_.FullName })
    $leaks = @(Find-SecretLeaks $ctx $touched)
    if ($leaks.Count -gt 0) { $problems += ("lộ bí mật trong: " + ($leaks -join ', ')) }
    Add-Step $ctx $rec ("Kiểm tra: {0} sự kiện của run {1}; {2} file text, {3} file jsonl mới; {4} file được quét bí mật; phát hiện lộ: {5}" -f $run.Count, $runId, $textLogs.Count, $jsonLogs.Count, $touched.Count, $leaks.Count)
    if ($end.Count -gt 0) { Add-Note $ctx $rec ("Exit code ghi trong log của script: {0}" -f $end[-1].exitCode) }
    if ($problems.Count -gt 0) { return Fin $rec 'FAIL' ('Không đạt: ' + ($problems -join '; ')) }
    return Fin $rec 'PASS' ("Log script đầy đủ: {0} sự kiện (START/STEP/OK/END), exit code có, không phát hiện bí mật." -f $run.Count)
}

# ------------------------------------------------------------------------------------------------------------------ H2
function Wait-DeploymentEventsOnScreen($rec, $run, [string]$stage) {
    # tra ve @{ Ok; Seconds; Reason }
    $a = Ensure-Api 'H2'
    if (-not $a.Ok) { return @{ Ok = $false; Blocked = $true; Reason = $a.Reason } }
    $need = @{}
    foreach ($e in $run.Events) { $code = 'DEPLOY_' + $e.result; if ($need.ContainsKey($code)) { $need[$code]++ } else { $need[$code] = 1 } }
    $from = [uri]::EscapeDataString($run.Start.AddMinutes(-1).ToUniversalTime().ToString('o'))
    $t0 = Get-Date; $deadline = $t0.AddSeconds($ImportWaitSeconds); $last = $null
    do {
        $r = Invoke-LogApi $ctx $script:base $script:api.Session "/api/logs?category=DEPLOYMENT&from=$from&pageSize=200"
        if ($r.Ok) {
            $last = $r
            $have = @{}
            foreach ($it in $r.Data.items) { if ($it.device -eq $run.Script) { if ($have.ContainsKey($it.event)) { $have[$it.event]++ } else { $have[$it.event] = 1 } } }
            $missing = @($need.Keys | Where-Object { -not $have.ContainsKey($_) -or $have[$_] -lt $need[$_] })
            if ($missing.Count -eq 0) { Save-Json $rec "api-deployment-events-$stage.json" $r.Data; return @{ Ok = $true; Seconds = [math]::Round(((Get-Date) - $t0).TotalSeconds, 1) } }
        }
        Start-Sleep -Seconds 5
    } while ((Get-Date) -lt $deadline)
    if ($last) { Save-Json $rec "api-deployment-events-$stage.json" $last.Data }
    return @{ Ok = $false; Blocked = $false; Reason = "Sau $ImportWaitSeconds s chưa thấy đủ sự kiện của $($run.Script) trên màn hình/API (thiếu: $(($need.Keys | Where-Object { -not $have -or -not $have.ContainsKey($_) }) -join ', '))." }
}

function Test-H2 {
    $rec = New-Rec 'H2'
    $ready = Test-DockerReady 'H2'
    if (-not $ready[0]) { return Fin $rec 'BLOCKED' $ready[1] }
    if (-not $script:LastRun) {
        $all = Get-DeployEvents ([datetime]::MinValue)
        $starts = @($all | Where-Object { $_.result -eq 'START' -and $_.script -in @('deploy.ps1', 'start.ps1') } | Sort-Object _time)
        if ($starts.Count -eq 0) { return Fin $rec 'BLOCKED' 'Chưa có lần chạy deploy/start để đối chiếu (H1 chưa có dữ liệu).' }
        $s = $starts[-1]
        $script:LastRun = [pscustomobject]@{ RunId = $s.runId; Script = $s.script; Start = $s._time; Events = @($all | Where-Object { $_.runId -eq $s.runId }) }
        Add-Note $ctx $rec 'H2 dùng lần chạy deploy/start gần nhất trong thư mục log (H1 không chạy trong phiên này).'
    }
    $run = $script:LastRun
    Add-Step $ctx $rec ("Đối chiếu run {0} ({1}, {2} sự kiện)" -f $run.RunId, $run.Script, $run.Events.Count)
    $be = Get-ServiceContainer $ctx 'backend' 'H2'
    if (-not $be) { return Fin $rec 'BLOCKED' 'Không có container backend (stack chưa chạy).' }
    $ls = Invoke-Docker $ctx @('exec', $be, 'sh', '-c', 'ls -la /deploy-logs') 'liệt kê /deploy-logs trong container' 'H2'
    Write-Evidence $ctx 'H2\container-deploy-logs-listing.txt' $ls.Output | Out-Null; Add-EvidenceRef $rec 'H2\container-deploy-logs-listing.txt'
    if ($ls.ExitCode -ne 0) { Add-Note $ctx $rec "Không liệt kê được /deploy-logs trong container (exit $($ls.ExitCode)): mount có thể chưa đúng." }
    $w = Wait-DeploymentEventsOnScreen $rec $run 'h2'
    if (-not $w.Ok) {
        if ($w.Blocked) { return Fin $rec 'BLOCKED' $w.Reason }
        return Fin $rec 'FAIL' $w.Reason
    }
    Add-Step $ctx $rec ("API /api/logs trả đủ sự kiện sau {0} s" -f $w.Seconds)
    if ($ManualUiCheck) {
        $ans = if ($NonInteractive) { '' } else { Read-Host "Mở $script:base/logs → nhóm 'Triển khai'. Có thấy các sự kiện của lần chạy $($run.Script) không? (y/n)" }
        Add-Note $ctx $rec ("Xác nhận bằng mắt trên giao diện: {0}" -f $(if ($ans -match '^(y|yes|c)') { 'CÓ' } elseif ($ans) { 'KHÔNG' } else { 'chưa trả lời' }))
        if ($ans -match '^(n|no|k)') { return Fin $rec 'FAIL' 'API có sự kiện nhưng người vận hành báo không thấy trên giao diện.' }
    } else { Add-Note $ctx $rec 'Giao diện chưa được xác nhận bằng mắt (không dùng -ManualUiCheck); xác nhận bằng API /api/logs, là nguồn dữ liệu của giao diện.' }
    if ($w.Seconds -gt 65) { return Fin $rec 'FAIL' ("Sự kiện chỉ xuất hiện sau {0} s, vượt yêu cầu ≤ 1 phút." -f $w.Seconds) }
    return Fin $rec 'PASS' ("Thấy đủ sự kiện của run {0} trong nhóm Triển khai sau {1} s (≤ 60 s)." -f $run.RunId, $w.Seconds)
}

# ------------------------------------------------------------------------------------------------------------------ H3
function Test-H3 {
    $rec = New-Rec 'H3'
    $ready = Test-DockerReady 'H3'
    if (-not $ready[0]) { return Fin $rec 'BLOCKED' $ready[1] }
    $snap = Get-StackSnapshot 'H3'
    if (-not $snap.postgres -or -not $snap.backend) { return Fin $rec 'BLOCKED' 'Cần stack đã chạy và đã có database (container postgres + backend). Trên stack mới, đổi POSTGRES_PASSWORD không gây lỗi (database được khởi tạo bằng mật khẩu đó).' }
    $vol = Invoke-Docker $ctx @('volume', 'inspect', 'etek_wcs_pgdata') 'volume dữ liệu có sẵn' 'H3'
    if ($vol.ExitCode -ne 0) { return Fin $rec 'BLOCKED' 'Không có volume etek_wcs_pgdata (chưa có database để làm hỏng bước một cách an toàn).' }
    Save-Json $rec 'stack-before.json' $snap
    $q = 'H3 sẽ chạy start.ps1 với một bản sao .env có POSTGRES_PASSWORD SAI (file .env thật không bị sửa). Compose sẽ TẠO LẠI container postgres (volume và dữ liệu giữ nguyên) và migrator sẽ lỗi xác thực. Sau đó bộ công cụ tự chạy start.ps1 với .env thật để KHÔI PHỤC. WCS có thể gián đoạn tạm thời. Chỉ làm trên stack thử.'
    if (-not (Confirm-Action $ConfirmH3.IsPresent 'H3-LAM-HONG-VA-KHOI-PHUC' $q $NonInteractive.IsPresent)) { return Fin $rec 'BLOCKED' 'Chưa có xác nhận thao tác gây gián đoạn (-ConfirmH3 hoặc nhập đúng cụm xác nhận). Không thực hiện.' }
    $a = Ensure-Api 'H3'
    if (-not $a.Ok) { Add-Note $ctx $rec "Phần 'sự kiện FAIL trên màn hình' sẽ không kiểm được: $($a.Reason)" }

    $tmp = Join-Path $ctx.EvidenceDir 'H3\tmp-config'
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    $wrong = 'wrong-' + [guid]::NewGuid().ToString('N').Substring(0, 12)
    Add-ExtraSecret $ctx $wrong
    $lines = @(Get-Content -LiteralPath $ctx.EnvFile) | ForEach-Object { if ($_ -match '^\s*POSTGRES_PASSWORD\s*=') { "POSTGRES_PASSWORD=$wrong" } else { $_ } }
    [System.IO.File]::WriteAllLines((Join-Path $tmp '.env'), $lines)
    Add-Step $ctx $rec 'Đã tạo bản sao .env tạm với POSTGRES_PASSWORD sai (không ghi giá trị vào báo cáo)'
    $marker = (Get-Date).AddSeconds(-1)
    $failExit = $null; $restored = $false
    try {
        $env:WCS_CONFIG_DIR = $tmp; $env:WCS_LOG_DIR = $ctx.LogDir
        $r = Invoke-Logged $ctx 'powershell' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $ctx.DeployDir 'scripts\start.ps1')) 'start.ps1 với .env sai mật khẩu (kỳ vọng thất bại)' 'H3'
        $failExit = $r.ExitCode
        Add-Step $ctx $rec "start.ps1 (cấu hình sai) kết thúc với exit code $failExit"
    } finally {
        Remove-Item Env:WCS_CONFIG_DIR -ErrorAction SilentlyContinue
        Add-Step $ctx $rec 'KHÔI PHỤC: chạy start.ps1 với .env thật'
        $env:WCS_CONFIG_DIR = $ctx.ConfigDir
        $rs = Invoke-Logged $ctx 'powershell' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $ctx.DeployDir 'scripts\start.ps1')) 'khôi phục stack bằng .env thật' 'H3'
        Remove-Item Env:WCS_CONFIG_DIR, Env:WCS_LOG_DIR -ErrorAction SilentlyContinue
        $be = Wait-ServiceHealthy 'backend' 240 'H3'
        $restored = ($rs.ExitCode -eq 0 -and $be -ne $null -and (Get-HealthzStatus (Get-BaseUrl $ctx $BaseUrl)) -eq 200)
        Add-Step $ctx $rec ("Khôi phục: start.ps1 exit {0}; backend khỏe: {1}" -f $rs.ExitCode, [bool]$be)
        try { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue } catch { }      # chi xoa thu muc tam do bo cong cu tao
    }
    Save-Json $rec 'stack-after.json' (Get-StackSnapshot 'H3')
    if (-not $restored) { Add-Note $ctx $rec 'CẢNH BÁO: stack CHƯA được khôi phục hoàn toàn. Chạy lại deployment\scripts\start.ps1 với .env thật và chạy status.ps1.'; return Fin $rec 'FAIL' 'Không khôi phục được stack sau bài thử H3 (xem ghi chú).' }

    $events = @(Get-DeployEvents $marker | Where-Object { $_.script -eq 'start.ps1' })
    $fail = @($events | Where-Object { $_.result -eq 'FAIL' })
    $failRunId = if ($fail.Count -gt 0) { ($fail | Sort-Object _time | Select-Object -First 1).runId } else { $null }
    $failFiles = @(Get-ChildItem -LiteralPath $ctx.LogDir -Filter 'failure-*' -File -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $marker -and ($failRunId -and $_.Name -like "failure-$failRunId-*") })
    Write-Evidence $ctx 'H3\failure-files.txt' (($failFiles | ForEach-Object { "{0}  {1:N0} bytes" -f $_.Name, $_.Length }) -join "`r`n") | Out-Null; Add-EvidenceRef $rec 'H3\failure-files.txt'
    foreach ($f in $failFiles | Select-Object -First 6) { Write-Evidence $ctx ("H3\" + $f.Name) ([System.IO.File]::ReadAllText($f.FullName)) | Out-Null; Add-EvidenceRef $rec ("H3\" + $f.Name) }
    $touched = @(Get-ChildItem -LiteralPath $ctx.LogDir -File -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $marker } | ForEach-Object { $_.FullName })
    $leaks = @(Find-SecretLeaks $ctx $touched)
    $problems = @()
    if ($failExit -eq 0) { $problems += 'start.ps1 với cấu hình sai KHÔNG thất bại (kỳ vọng exit ≠ 0)' }
    if ($fail.Count -eq 0) { $problems += 'không có sự kiện FAIL trong deployment-events-*.jsonl' }
    if ($failFiles.Count -eq 0) { $problems += 'không có failure-<run>-*.log' }
    elseif (@($failFiles | Where-Object { $_.Name -match 'migrator|backend|last-command' }).Count -eq 0) { $problems += 'failure-*.log không chứa migrator/backend/last-command' }
    if ($leaks.Count -gt 0) { $problems += ('lộ bí mật trong: ' + ($leaks -join ', ')) }
    Add-Step $ctx $rec ("Kiểm tra: FAIL event={0}; failure files={1}; lộ bí mật={2}" -f $fail.Count, $failFiles.Count, $leaks.Count)
    if ($problems.Count -gt 0) { return Fin $rec 'FAIL' ('Không đạt: ' + ($problems -join '; ')) }

    # man hinh: sau khi stack duoc khoi phuc, su kien FAIL cua run that bai phai hien tren /logs
    $a = Ensure-Api 'H3'
    if (-not $a.Ok) { return Fin $rec 'BLOCKED' ("Phần log file đạt (FAIL event + failure-*.log, không lộ bí mật) và stack đã khôi phục, nhưng chưa kiểm được sự kiện FAIL trên màn hình: " + $a.Reason) }
    $script:FailRun = [pscustomobject]@{ RunId = $failRunId; Script = 'start.ps1'; Start = $marker; Events = @($events | Where-Object { $_.runId -eq $failRunId }) }
    $w = Wait-DeploymentEventsOnScreen $rec $script:FailRun 'h3'
    if (-not $w.Ok) { return Fin $rec ($(if ($w.Blocked) { 'BLOCKED' } else { 'FAIL' })) ("Phần log file đạt nhưng " + $w.Reason) }
    return Fin $rec 'PASS' ("start.ps1 thất bại có kiểm soát (exit {0}); có {1} file failure-*.log và sự kiện FAIL (hiện trên màn hình sau {2} s); stack đã khôi phục, không lộ bí mật." -f $failExit, $failFiles.Count, $w.Seconds)
}

# ------------------------------------------------------------------------------------------------------------------ H4
function Test-H4 {
    $rec = New-Rec 'H4'
    $ready = Test-DockerReady 'H4'
    if (-not $ready[0]) { return Fin $rec 'BLOCKED' $ready[1] }
    $id = Get-ServiceContainer $ctx 'backend' 'H4'
    if (-not $id) { return Fin $rec 'BLOCKED' 'Không có container backend.' }
    $i = Get-Inspect $ctx $id 'H4'
    if (-not $i -or -not $i.Running) { return Fin $rec 'BLOCKED' 'Container backend không chạy.' }
    Add-Step $ctx $rec ("Container {0} ({1}) đang chạy" -f $i.Name, $i.Image)
    Save-Json $rec 'mounts.json' $i.Mounts
    $m = @($i.Mounts | Where-Object { $_.Destination -eq '/deploy-logs' })
    if ($m.Count -eq 0) { return Fin $rec 'FAIL' 'Container không có mount nào tại /deploy-logs (compose chưa gắn thư mục log).' }
    Add-Step $ctx $rec ("Mount /deploy-logs: type={0}, nguồn={1}, RW={2}" -f $m[0].Type, $m[0].Source, $m[0].RW)
    $name = '.acceptance-write-test-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
    $t = Invoke-Docker $ctx @('exec', $id, 'sh', '-c', "touch /deploy-logs/$name") "thử ghi vào /deploy-logs (kỳ vọng bị từ chối)" 'H4'
    $created = Test-Path -LiteralPath (Join-Path $ctx.LogDir $name)
    if ($created) { try { Remove-Item -LiteralPath (Join-Path $ctx.LogDir $name) -Force } catch { } }      # chi xoa dung file thu do bo cong cu vua tao
    $rd = Invoke-Docker $ctx @('exec', $id, 'sh', '-c', 'ls /deploy-logs | head -5') 'đọc /deploy-logs (kỳ vọng đọc được)' 'H4'
    Write-Evidence $ctx 'H4\touch-result.txt' ("exit={0}`r`n{1}`r`nfile xuất hiện trên host: {2}`r`nđọc được thư mục: exit {3}" -f $t.ExitCode, $t.Output, $created, $rd.ExitCode) | Out-Null; Add-EvidenceRef $rec 'H4\touch-result.txt'
    if ($created -or $t.ExitCode -eq 0) { return Fin $rec 'FAIL' 'Ghi được vào /deploy-logs: mount KHÔNG chỉ-đọc (file thử đã được xóa).' }
    if ($m[0].RW -ne $false) { return Fin $rec 'FAIL' ("touch bị từ chối nhưng Mounts.RW = {0} (không phải false): không xác nhận được mount chỉ-đọc." -f $m[0].RW) }
    if ($t.Output -match '(?i)read-only file system') { $why = 'lỗi "Read-only file system"' } else { $why = 'lỗi khác: ' + (($t.Output -replace '\s+', ' ').Trim()); Add-Note $ctx $rec "Thông báo lỗi không phải 'Read-only file system' ($why); kết luận dựa trên Mounts.RW=false và việc ghi thất bại, không có file nào xuất hiện trên host." }
    if ($rd.ExitCode -ne 0) { Add-Note $ctx $rec 'Lưu ý: backend không liệt kê được /deploy-logs (quyền đọc?).' }
    return Fin $rec 'PASS' ("touch bị từ chối (exit {0}, {1}); Mounts.RW=false; không có file nào xuất hiện trên host; thư mục vẫn đọc được: {2}." -f $t.ExitCode, $why, ($rd.ExitCode -eq 0))
}

# ------------------------------------------------------------------------------------------------------------------ H5
function Test-H5 {
    $rec = New-Rec 'H5'
    $ready = Test-DockerReady 'H5'
    if (-not $ready[0]) { return Fin $rec 'BLOCKED' $ready[1] }
    $id = Get-ServiceContainer $ctx 'backend' 'H5'
    if (-not $id) { return Fin $rec 'BLOCKED' 'Không có container backend.' }
    if ($ManualPlcToggle) {
        if ($NonInteractive) { Add-Note $ctx $rec '-ManualPlcToggle bị bỏ qua ở chế độ không tương tác.' }
        else { Read-Host 'Bật/tắt kết nối PLC THỬ (PLC giả lập hoặc thiết bị thử đã được kỹ sư PLC phê duyệt; KHÔNG dùng PLC vận hành) rồi nhấn Enter để thu log' | Out-Null }
    }
    $r = Invoke-Compose $ctx @('logs', '--no-color', '--tail', '5000', 'backend') 'log backend' 'H5'
    Write-Evidence $ctx 'H5\backend-logs.txt' $r.Output | Out-Null; Add-EvidenceRef $rec 'H5\backend-logs.txt'
    $plc = @($r.Lines | Where-Object { $_ -match '\bPLC PLC-0[1-4]\b' })
    $state = @($plc | Where-Object { $_ -match 'STATE:' -and $_ -match '\(\s*\w+\s*->\s*\w+' })
    $withIp = @($state | Where-Object { $_ -match '\b\d{1,3}(\.\d{1,3}){3}(:\d+)?\b' })
    Write-Evidence $ctx 'H5\plc-lines.txt' (($plc | Select-Object -First 200) -join "`r`n") | Out-Null; Add-EvidenceRef $rec 'H5\plc-lines.txt'
    Add-Step $ctx $rec ("Log backend: {0} dòng PLC; {1} dòng STATE có chuyển trạng thái trước->sau; {2} dòng có IP" -f $plc.Count, $state.Count, $withIp.Count)
    if ($withIp.Count -gt 0) {
        $withPort = @($withIp | Where-Object { $_ -match ':\d{2,5}\b' })
        if ($withPort.Count -eq 0) { Add-Note $ctx $rec 'Có IP nhưng chưa có cổng trong ngữ cảnh (cổng TCP của PLC chưa được cấu hình: NEEDS_CONFIGURATION).' }
        return Fin $rec 'PASS' ("Thấy {0} dòng PLC STATE có trạng thái trước→sau và IP (ví dụ: {1})." -f $withIp.Count, ($withIp[0] -replace '^\[?\d{4}-\d{2}-\d{2} ', '').Trim())
    }
    $sample = ($plc | Select-Object -First 3 | ForEach-Object { ($_ -replace '\s+', ' ').Trim() }) -join ' | '
    if ($plc.Count -eq 0) { return Fin $rec 'BLOCKED' 'Không có dòng "PLC PLC-0x" nào trong log backend (chưa có sự kiện PLC; log container có thể đã xoay vòng). Cần sự kiện bật/tắt kết nối PLC thử hợp lệ (bộ công cụ KHÔNG tự bật kết nối PLC).' }
    return Fin $rec 'BLOCKED' ("Có {0} dòng PLC nhưng chưa có dòng STATE chuyển trạng thái kèm IP (stack không có PLC/giả lập để bật/tắt). Mẫu: {1}" -f $plc.Count, $sample)
}

# ------------------------------------------------------------------------------------------------------------------ H6
function Test-H6 {
    $rec = New-Rec 'H6'
    $ready = Test-DockerReady 'H6'
    if (-not $ready[0]) { return Fin $rec 'BLOCKED' $ready[1] }
    $pg = Get-ServiceContainer $ctx 'postgres' 'H6'
    $be = Get-ServiceContainer $ctx 'backend' 'H6'
    if (-not $pg -or -not $be) { return Fin $rec 'BLOCKED' 'Cần stack đang chạy (container postgres và backend).' }
    $pi = Get-Inspect $ctx $pg 'H6'
    # --- nhan dien chinh xac container thu nghiem (khong bao gio dung vao PostgreSQL cua may phat trien)
    $why = @()
    if (-not $pi) { $why += 'không inspect được container' } else {
        if ($pi.Labels.Project -ne $ctx.Project) { $why += "label project='$($pi.Labels.Project)' khác '$($ctx.Project)'" }
        if ($pi.Labels.Service -ne 'postgres') { $why += "label service='$($pi.Labels.Service)' khác 'postgres'" }
        if ($pi.Image -notmatch '^postgres:16') { $why += "image '$($pi.Image)' không phải postgres:16" }
        if (@($pi.Mounts | Where-Object { $_.Destination -eq '/var/lib/postgresql/data' -and $_.Name -eq 'etek_wcs_pgdata' }).Count -eq 0) { $why += 'không gắn volume etek_wcs_pgdata' }
        if ($pi.PublishedPorts.Count -gt 0) { $why += ('có cổng publish ra host: ' + ($pi.PublishedPorts -join ';')) }
        if (-not $pi.Running) { $why += 'container không chạy' }
    }
    if ($why.Count -gt 0) { return Fin $rec 'BLOCKED' ('Không xác định chắc chắn container PostgreSQL thử nghiệm: ' + ($why -join '; ') + '. Không dừng gì cả.') }
    $m = Get-EnvMap $ctx; $dbName = if ($m['POSTGRES_DB']) { $m['POSTGRES_DB'] } else { 'wcs_db' }; $dbUser = if ($m['POSTGRES_USER']) { $m['POSTGRES_USER'] } else { 'wcs' }
    $q = Invoke-Docker $ctx @('exec', $pg, 'psql', '-U', $dbUser, '-d', $dbName, '-tAc', 'select (select count(*) from "__EFMigrationsHistory")||'' migrations; ''||pg_size_pretty(pg_database_size(current_database()))') 'số migration và dung lượng DB trong container' 'H6'
    $baseline = ($q.Output).Trim()
    Add-Step $ctx $rec ("Container thử nghiệm: {0} ({1}); DB '{2}' TRONG container: {3}" -f $pi.Name, $pi.Image, $dbName, $(if ($q.ExitCode -eq 0) { $baseline } else { 'không đọc được' }))
    Add-Note $ctx $rec "Bài này chỉ làm việc với container '$($pi.Name)' (volume etek_wcs_pgdata). Không kết nối PostgreSQL của máy phát triển (dịch vụ trên host, cổng 5432) và không đụng database wcs_db của máy phát triển."
    Save-Json $rec 'postgres-container.json' $pi

    $phrase = "STOP $($pi.Name)"
    $ok = ($ConfirmH6.IsPresent -and $ConfirmPostgresContainer -eq $pi.Name)
    if (-not $ok -and -not $NonInteractive) {
        Write-Host ''
        Write-Host ("SẮP DỪNG container PostgreSQL '{0}' trong {1} giây rồi khởi động lại. WCS sẽ KHÔNG truy cập được database trong thời gian đó (không xóa dữ liệu/volume). PLC/RCS/AGV không bị tác động. Chỉ làm trên stack thử." -f $pi.Name, $OutageSeconds) -ForegroundColor Yellow
        $a = Read-Host "Gõ chính xác: $phrase   (khác = hủy)"
        $ok = ($a -ceq $phrase)
    }
    if (-not $ok) { return Fin $rec 'BLOCKED' ("Chưa có xác nhận dừng PostgreSQL (dùng -ConfirmH6 -ConfirmPostgresContainer '{0}' hoặc nhập đúng cụm xác nhận). Không dừng gì cả." -f $pi.Name) }

    $a = Ensure-Api 'H6'
    if (-not $a.Ok) { Add-Note $ctx $rec "Phần 'trên màn hình' không kiểm được: $($a.Reason)" }
    $rb = Invoke-Compose $ctx @('logs', '--no-color', '--tail', '5000', 'backend') 'log backend trước khi dừng (đường cơ sở)' 'H6'
    $baseLost = @($rb.Lines | Where-Object { $_ -match 'DB_LOST' }).Count; $baseRest = @($rb.Lines | Where-Object { $_ -match 'DB_RESTORED' }).Count
    Add-Step $ctx $rec ("Đường cơ sở trong log backend: DB_LOST={0}, DB_RESTORED={1}" -f $baseLost, $baseRest)
    $marker = (Get-Date)
    $samples = New-Object System.Collections.Generic.List[string]
    $stopped = $false; $startedBack = $false; $stopExit = $null; $startExit = $null
    try {
        $s = Invoke-Docker $ctx @('stop', $pg) "docker stop $($pi.Name)" 'H6'; $stopExit = $s.ExitCode; $stopped = ($s.ExitCode -eq 0)
        Add-Step $ctx $rec ("docker stop exit {0}; chờ {1} s (lấy mẫu /healthz mỗi 10 s)" -f $stopExit, $OutageSeconds)
        if ($stopped) {
            $end = (Get-Date).AddSeconds($OutageSeconds)
            while ((Get-Date) -lt $end) { $h = Get-HealthzStatus (Get-BaseUrl $ctx $BaseUrl); $samples.Add(("{0} healthz={1}" -f (Get-Date).ToString('HH:mm:ss'), $h)); Start-Sleep -Seconds 10 }
        }
    } finally {
        if ($stopped) {
            $s2 = Invoke-Docker $ctx @('start', $pg) "docker start $($pi.Name) (luôn thực hiện)" 'H6'; $startExit = $s2.ExitCode; $startedBack = ($s2.ExitCode -eq 0)
            Add-Step $ctx $rec ("docker start exit {0}" -f $startExit)
        }
    }
    Write-Evidence $ctx 'H6\healthz-samples.txt' ($samples -join "`r`n") | Out-Null; Add-EvidenceRef $rec 'H6\healthz-samples.txt'
    if ($stopped -and -not $startedBack) { Add-Note $ctx $rec "CẢNH BÁO: docker start thất bại. Chạy ngay: docker start $($pi.Name) (không dùng docker compose down -v)."; return Fin $rec 'FAIL' 'Không khởi động lại được PostgreSQL sau khi dừng.' }
    if (-not $stopped) { return Fin $rec 'FAIL' ("docker stop thất bại (exit {0}); không có gián đoạn nào được tạo ra." -f $stopExit) }

    $pgBack = Wait-ServiceHealthy 'postgres' 180 'H6'
    $beBack = Wait-ServiceHealthy 'backend' 180 'H6'
    $h = Get-HealthzStatus (Get-BaseUrl $ctx $BaseUrl)
    Add-Step $ctx $rec ("Sau khi start: postgres khỏe={0}; backend khỏe={1}; /healthz={2}" -f [bool]$pgBack, [bool]$beBack, $h)
    Add-Step $ctx $rec 'Chờ 45 s để monitor (15 s/lần) và sink lưu DB (backoff tối đa 30 s) ghi xong sự kiện'
    Start-Sleep -Seconds 45
    $ra = Invoke-Compose $ctx @('logs', '--no-color', '--since', $marker.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'), 'backend') 'log backend từ lúc dừng' 'H6'
    Write-Evidence $ctx 'H6\backend-logs-since-stop.txt' $ra.Output | Out-Null; Add-EvidenceRef $rec 'H6\backend-logs-since-stop.txt'
    $lost = @($ra.Lines | Where-Object { $_ -match 'DB_LOST' }).Count; $rest = @($ra.Lines | Where-Object { $_ -match 'DB_RESTORED' }).Count
    $ticks = @($ra.Lines | Where-Object { $_ -match 'Scheduler tick failed' }).Count
    $maxTicks = [math]::Ceiling($OutageSeconds / 60.0) + 2
    $down = @($samples | Where-Object { $_ -match 'healthz=503' }).Count
    $vol = Invoke-Docker $ctx @('volume', 'inspect', 'etek_wcs_pgdata') 'volume dữ liệu vẫn còn' 'H6'
    $q2 = Invoke-Docker $ctx @('exec', $pg, 'psql', '-U', $dbUser, '-d', $dbName, '-tAc', 'select (select count(*) from "__EFMigrationsHistory")||'' migrations; ''||pg_size_pretty(pg_database_size(current_database()))') 'số migration sau khôi phục' 'H6'
    $problems = @()
    if (-not $pgBack) { $problems += 'postgres không khỏe lại' }
    if (-not $beBack -or $h -ne 200) { $problems += "backend/healthz không hồi phục (healthz=$h)" }
    if ($down -lt 1) { $problems += 'không mẫu /healthz nào trả 503 trong lúc mất DB' }
    if ($lost -ne 1) { $problems += "DB_LOST trong log = $lost (kỳ vọng 1)" }
    if ($rest -ne 1) { $problems += "DB_RESTORED trong log = $rest (kỳ vọng 1)" }
    if ($ticks -gt $maxTicks) { $problems += "lỗi scheduler lặp $ticks dòng (> $maxTicks): throttle không hoạt động" }
    if ($vol.ExitCode -ne 0) { $problems += 'volume etek_wcs_pgdata không còn' }
    if ($q2.ExitCode -eq 0 -and $baseline -and ($q2.Output).Trim() -ne $baseline) { Add-Note $ctx $rec ("Số migration/dung lượng trước: '{0}'; sau: '{1}' (dung lượng có thể khác một chút)." -f $baseline, ($q2.Output).Trim()) }
    Add-Step $ctx $rec ("Kết quả stdout: DB_LOST={0}, DB_RESTORED={1}, Scheduler tick failed={2} (tối đa {3}), mẫu 503={4}/{5}" -f $lost, $rest, $ticks, $maxTicks, $down, $samples.Count)
    if ($problems.Count -gt 0) { return Fin $rec 'FAIL' ('Không đạt: ' + ($problems -join '; ')) }

    $a = Ensure-Api 'H6'
    if (-not $a.Ok) { return Fin $rec 'BLOCKED' ("Phần stdout đạt (đúng 1 DB_LOST, 1 DB_RESTORED, throttle ≤ $maxTicks, dữ liệu còn) và stack đã khôi phục, nhưng chưa kiểm được màn hình: " + $a.Reason) }
    $from = [uri]::EscapeDataString($marker.AddSeconds(-5).ToUniversalTime().ToString('o'))
    $deadline = (Get-Date).AddSeconds($ImportWaitSeconds); $scr = $null
    do {
        $r = Invoke-LogApi $ctx $script:base $script:api.Session "/api/logs?category=DATABASE&from=$from&pageSize=100"
        if ($r.Ok) { $scr = $r; $L = @($r.Data.items | Where-Object { $_.event -eq 'DB_LOST' }).Count; $R = @($r.Data.items | Where-Object { $_.event -eq 'DB_RESTORED' }).Count; if ($L -ge 1 -and $R -ge 1) { break } }
        Start-Sleep -Seconds 5
    } while ((Get-Date) -lt $deadline)
    if ($scr) { Save-Json $rec 'api-database-events.json' $scr.Data }
    if (-not $scr -or $L -lt 1 -or $R -lt 1) { return Fin $rec 'FAIL' ("Trên màn hình/API chưa thấy đủ DB_LOST và DB_RESTORED sau {0} s (DB_LOST={1}, DB_RESTORED={2})." -f $ImportWaitSeconds, $L, $R) }
    if ($L -ne 1 -or $R -ne 1) { return Fin $rec 'FAIL' ("Màn hình/API có DB_LOST={0}, DB_RESTORED={1} (kỳ vọng mỗi loại 1)." -f $L, $R) }
    return Fin $rec 'PASS' ("PostgreSQL dừng {0} s rồi khởi động lại: /healthz 503→200; đúng 1 DB_LOST và 1 DB_RESTORED (stdout và màn hình); throttle {1} ≤ {2}; volume etek_wcs_pgdata còn nguyên." -f $OutageSeconds, $ticks, $maxTicks)
}

# ------------------------------------------------------------------------------------------------------------------ X1 / X2
function Test-X1 {
    $rec = New-Rec 'X1'
    $ready = Test-DockerReady 'X1'
    if (-not $ready[0]) { return Fin $rec 'BLOCKED' $ready[1] }
    $env:WCS_CONFIG_DIR = $ctx.ConfigDir; $env:WCS_LOG_DIR = $ctx.LogDir
    try { $r = Invoke-Logged $ctx 'powershell' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $ctx.DeployDir 'scripts\status.ps1')) 'status.ps1' 'X1' } finally { Remove-Item Env:WCS_CONFIG_DIR, Env:WCS_LOG_DIR -ErrorAction SilentlyContinue }
    Write-Evidence $ctx 'X1\status-output.txt' $r.Output | Out-Null; Add-EvidenceRef $rec 'X1\status-output.txt'
    $rows = @($r.Lines | Where-Object { $_ -match '^(postgres|backend|frontend)\s+state=' })
    $okRows = @($rows | Where-Object { $_ -match 'restarts=\d+' -and $_ -match 'oom=(true|false)' })
    Add-Step $ctx $rec ("status.ps1 exit {0}; {1} dòng container; {2} dòng có restarts=/oom=" -f $r.ExitCode, $rows.Count, $okRows.Count)
    if ($rows.Count -lt 3) { return Fin $rec 'FAIL' "status.ps1 không in đủ 3 container (postgres, backend, frontend): $($rows.Count)." }
    if ($okRows.Count -ne $rows.Count) { return Fin $rec 'FAIL' 'Có container thiếu restarts= hoặc oom= trong status.ps1.' }
    $hasProbe = @($r.Lines | Where-Object { $_ -match 'last health probe' }).Count
    if ($hasProbe -lt 1) { return Fin $rec 'FAIL' 'Không có dòng "last health probe" cho dịch vụ có healthcheck.' }
    return Fin $rec 'PASS' ("status.ps1 hiển thị restarts=/oom= cho {0} container và {1} dòng health probe cuối." -f $okRows.Count, $hasProbe)
}

function Test-X2 {
    $rec = New-Rec 'X2'
    $ready = Test-DockerReady 'X2'
    if (-not $ready[0]) { return Fin $rec 'BLOCKED' $ready[1] }
    $be = Get-ServiceContainer $ctx 'backend' 'X2'
    if (-not $be) { return Fin $rec 'BLOCKED' 'Không có container backend.' }
    $q = 'X2 sẽ chạy "docker restart" trên container backend (gián đoạn WCS vài chục giây; dữ liệu và volume giữ nguyên). Chỉ làm trên stack thử.'
    if (-not (Confirm-Action $ConfirmX2.IsPresent 'X2-RESTART-BACKEND' $q $NonInteractive.IsPresent)) { return Fin $rec 'BLOCKED' 'Chưa có xác nhận restart backend (-ConfirmX2 hoặc nhập đúng cụm xác nhận). Không thực hiện.' }
    $a = Ensure-Api 'X2'
    $before = @(Get-ChildItem -LiteralPath $ctx.LogDir -File -ErrorAction SilentlyContinue | ForEach-Object { "{0}|{1}" -f $_.Name, $_.Length })
    $evBefore = $null
    if ($a.Ok) { $evBefore = (Invoke-LogApi $ctx $script:base $script:api.Session '/api/logs?category=DEPLOYMENT&pageSize=1').Data.total }
    $i0 = Get-Inspect $ctx $be 'X2'
    $r = Invoke-Docker $ctx @('restart', $be) 'docker restart backend' 'X2'
    if ($r.ExitCode -ne 0) { return Fin $rec 'FAIL' "docker restart thất bại (exit $($r.ExitCode))." }
    $healthy = Wait-ServiceHealthy 'backend' 240 'X2'
    if (-not $healthy) { return Fin $rec 'FAIL' 'Backend không khỏe lại sau restart.' }
    Add-Step $ctx $rec 'Chờ 60 s cho importer chạy lại sau khi khởi động'
    Start-Sleep -Seconds 60
    $after = @(Get-ChildItem -LiteralPath $ctx.LogDir -File -ErrorAction SilentlyContinue | ForEach-Object { "{0}|{1}" -f $_.Name, $_.Length })
    $lost = @($before | Where-Object { ($_.Split('|')[0]) -notin @($after | ForEach-Object { $_.Split('|')[0] }) })
    $shrunk = @($before | Where-Object { $n = $_.Split('|'); $m = @($after | Where-Object { $_.StartsWith($n[0] + '|') }); $m.Count -eq 1 -and [long]($m[0].Split('|')[1]) -lt [long]$n[1] })
    $problems = @()
    if ($lost.Count -gt 0) { $problems += "mất file log: $($lost -join ', ')" }
    if ($shrunk.Count -gt 0) { $problems += 'có file log bị rút ngắn' }
    if ($a.Ok -and $null -ne $evBefore) {
        $evAfter = (Invoke-LogApi $ctx $script:base $script:api.Session '/api/logs?category=DEPLOYMENT&pageSize=1').Data.total
        Add-Step $ctx $rec ("Số sự kiện DEPLOYMENT trên API: trước {0}, sau {1}" -f $evBefore, $evAfter)
        if ($evAfter -lt $evBefore) { $problems += 'mất sự kiện cũ' }
        # co the tang vi script chay them; nhan doi (import lap) la loi
        $newFiles = @($after | Where-Object { $_ -notin $before }).Count
        if ($evAfter -gt $evBefore * 2 -and $evBefore -gt 0 -and $newFiles -eq 0) { $problems += 'số sự kiện tăng gấp đôi mà không có log mới (import lặp)' }
    } else { Add-Note $ctx $rec 'Không kiểm được số sự kiện trên API (không đăng nhập được): chỉ kiểm file log.' }
    if ($problems.Count -gt 0) { return Fin $rec 'FAIL' ('Không đạt: ' + ($problems -join '; ')) }
    if (-not $a.Ok) { return Fin $rec 'BLOCKED' ('File log còn nguyên sau restart, nhưng chưa kiểm được sự kiện trên màn hình: ' + $a.Reason) }
    return Fin $rec 'PASS' 'Backend restart và khỏe lại; mọi file log còn, không rút ngắn; số sự kiện DEPLOYMENT không mất/không nhân đôi.'
}

# ------------------------------------------------------------------------------------------------------------------ chay
$null = & (Join-Path $PSScriptRoot 'collect-evidence.ps1') -OutDir (Join-Path $ctx.EvidenceDir '_pre') -Project $Project -RepoDir $ctx.RepoDir -ConfigDir $ctx.ConfigDir -LogDir $ctx.LogDir -Quiet 2>&1
foreach ($id in $Tests) {
    Write-Host ''
    Write-Host ("=== {0} – {1} ===" -f $id, $Meta[$id].Title) -ForegroundColor Cyan
    $rec = $null
    try { $rec = & ("Test-" + $id) }
    catch {
        $rec = New-Rec $id
        $null = Fin $rec 'BLOCKED' ("Lỗi không mong đợi khi chạy bài: " + (Hide-Secrets $ctx $_.Exception.Message))
        Add-Note $ctx $rec ("Chi tiết: " + (Hide-Secrets $ctx ($_ | Out-String)))
    }
    $records.Add($rec)
}
$null = & (Join-Path $PSScriptRoot 'collect-evidence.ps1') -OutDir (Join-Path $ctx.EvidenceDir '_post') -Project $Project -RepoDir $ctx.RepoDir -ConfigDir $ctx.ConfigDir -LogDir $ctx.LogDir -Quiet 2>&1

# ------------------------------------------------------------------------------------------------------------------ bao cao
$pass = @($records | Where-Object { $_.Status -eq 'PASS' }).Count; $failN = @($records | Where-Object { $_.Status -eq 'FAIL' }).Count; $blk = @($records | Where-Object { $_.Status -eq 'BLOCKED' }).Count
$overall = if ($failN -gt 0) { "KHÔNG ĐẠT ($failN bài không đạt, $pass đạt, $blk chưa thể kiểm tra)" } elseif ($blk -gt 0) { "CHƯA ĐỦ BẰNG CHỨNG ($pass đạt, $blk chưa thể kiểm tra, 0 không đạt)" } else { "ĐẠT (tất cả $pass bài đã chọn)" }
$json = $records | ForEach-Object { [ordered]@{ id = $_.Id; title = $_.Title; status = $_.Status; statusVi = (Get-StatusVi $_.Status); reason = $_.Reason; expected = $_.Expected; steps = @($_.Steps); notes = @($_.Notes); evidence = @($_.Evidence); startedAt = $_.StartedAt.ToString('o'); endedAt = $(if ($_.EndedAt) { $_.EndedAt.ToString('o') } else { $null }) } }
Write-Evidence $ctx 'results.json' ($json | ConvertTo-Json -Depth 6) | Out-Null
Write-Evidence $ctx 'commands-all.log' ($ctx.Commands -join "`r`n") | Out-Null

function Read-Tool([string]$exe, [string[]]$a) { try { $o = (& $exe @a 2>$null | Select-Object -First 1); return "$o".Trim() } catch { return 'không có' } }
$dockerV = if (Get-Command docker -ErrorAction SilentlyContinue) { Read-Tool 'docker' @('--version') } else { 'không có' }
$composeV = if (Get-Command docker -ErrorAction SilentlyContinue) { Read-Tool 'docker' @('compose', 'version', '--short') } else { 'không có' }
$br = ''; $hd = ''; try { $br = (& git -C $ctx.RepoDir branch --show-current 2>$null); $hd = (& git -C $ctx.RepoDir rev-parse --short HEAD 2>$null) } catch { }
$tpl = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'report-template.md'))
$b0 = $tpl.IndexOf('<!-- TEST-BLOCK-BEGIN -->'); $b1 = $tpl.IndexOf('<!-- TEST-BLOCK-END -->')
$head = $tpl.Substring(0, $b0); $block = $tpl.Substring($b0 + '<!-- TEST-BLOCK-BEGIN -->'.Length, $b1 - $b0 - '<!-- TEST-BLOCK-BEGIN -->'.Length); $tail = $tpl.Substring($b1 + '<!-- TEST-BLOCK-END -->'.Length)
$sumRows = ($records | ForEach-Object { "| {0} | {1} | **{2}** | {3} |" -f $_.Id, $_.Title, (Get-StatusVi $_.Status), (($_.Reason -replace '\|', '/') -replace "`r?`n", ' ') }) -join "`r`n"
$summary = "| Mã | Tên | Trạng thái | Lý do / kết quả |`r`n|---|---|---|---|`r`n$sumRows"
$blocks = New-Object System.Text.StringBuilder
foreach ($r in $records) {
    $b = $block
    $map = @{
        '{{ID}}' = $r.Id; '{{TITLE}}' = $r.Title; '{{OBJECTIVE}}' = $Meta[$r.Id].Objective; '{{PRECONDITIONS}}' = $Meta[$r.Id].Pre
        '{{COMMANDS}}' = $Meta[$r.Id].Cmd + "`r`n`r`nLệnh thực tế đã chạy: xem ``$($r.Id)\commands.log``"; '{{EXPECTED}}' = $r.Expected; '{{ACTUAL}}' = $r.Reason
        '{{EVIDENCE}}' = $(if ($r.Evidence.Count -gt 0) { ($r.Evidence | ForEach-Object { "- ``$_``" }) -join "`r`n" } else { '- (không có file bằng chứng riêng; xem steps.log và commands.log của bài)' })
        '{{STATUS}}' = (Get-StatusVi $r.Status); '{{NOTES}}' = $(if ($r.Notes.Count -gt 0) { ($r.Notes | ForEach-Object { "- $_" }) -join "`r`n" } else { '–' })
        '{{STEPS}}' = ($r.Steps | ForEach-Object { "- $_" }) -join "`r`n"
    }
    foreach ($k in $map.Keys) { $b = $b.Replace($k, [string]$map[$k]) }
    [void]$blocks.Append($b)
}
$hdr = @{ '{{DATE}}' = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss'); '{{OPERATOR}}' = $env:USERNAME; '{{HOSTNAME}}' = $env:COMPUTERNAME; '{{OS}}' = (Get-CimInstance Win32_OperatingSystem).Caption
    '{{DOCKER_VERSION}}' = $dockerV; '{{COMPOSE_VERSION}}' = $composeV; '{{BRANCH}}' = $br; '{{HEAD}}' = $hd; '{{EVIDENCE_DIR}}' = $ctx.EvidenceDir; '{{OVERALL}}' = $overall; '{{SUMMARY_TABLE}}' = $summary }
$report = $head + $blocks.ToString() + $tail
foreach ($k in $hdr.Keys) { $report = $report.Replace($k, [string]$hdr[$k]) }
$reportPath = Write-Evidence $ctx 'acceptance-report.md' $report

Write-Host ''
Write-Host '================ TỔNG HỢP ================' -ForegroundColor Cyan
foreach ($r in $records) { Write-Host ("{0}  {1,-18}  {2}" -f $r.Id, (Get-StatusVi $r.Status), $r.Reason) }
Write-Host ("KẾT LUẬN: {0}" -f $overall) -ForegroundColor $(if ($failN -gt 0) { 'Red' } elseif ($blk -gt 0) { 'Yellow' } else { 'Green' })
Write-Host ("Báo cáo: {0}" -f $reportPath)
Write-Host ("Bằng chứng: {0}" -f $ctx.EvidenceDir)
if ($failN -gt 0) { exit 1 } elseif ($blk -gt 0) { exit 2 } else { exit 0 }
