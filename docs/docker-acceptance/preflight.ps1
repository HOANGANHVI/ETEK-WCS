<#
.SYNOPSIS
  Kiem tra moi truong truoc khi chay nghiem thu Docker H1-H6 cua CR-007. CHI KIEM TRA: khong cai dat, khong khoi dong/dung container, khong sua cau hinh.
.DESCRIPTION
  Moi muc co ket qua DAT / KHONG DAT / CHUA THE KIEM TRA va cot "Bat buoc". San sang = moi muc bat buoc deu DAT.
  Exit code: 0 = san sang, 1 = chua san sang (co muc bat buoc KHONG DAT), 2 = chua du thong tin (co muc bat buoc CHUA THE KIEM TRA).
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File docs\docker-acceptance\preflight.ps1
#>
param(
    [string]$RepoDir,
    [string]$ConfigDir,
    [string]$LogDir,
    [string]$EvidenceRoot,
    [string]$Project = 'etek-wcs',
    [int]$MinFreeGB = 20,
    [string]$OutFile
)
. (Join-Path $PSScriptRoot '_accept-common.ps1')
$ctx = New-AcceptContext -RepoDir $RepoDir -ConfigDir $ConfigDir -LogDir $LogDir -EvidenceRoot $EvidenceRoot -Project $Project -Tag 'preflight'
$ctx.NoEvidence = $true      # preflight chi kiem tra: khong tao thu muc bang chung

$checks = New-Object System.Collections.Generic.List[object]
function Add-Check([string]$Name, [string]$Status, [string]$Detail, [bool]$Required = $true) {
    $checks.Add([pscustomobject]@{ Name = $Name; Status = $Status; Detail = (Hide-Secrets $ctx $Detail); Required = $Required })
}
function Run([string]$exe, [string[]]$a) {
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { $o = @(& $exe @a 2>&1 | ForEach-Object { "$_" }); $c = $LASTEXITCODE; if ($null -eq $c) { $c = 0 } } catch { $o = @($_.Exception.Message); $c = -1 } finally { $ErrorActionPreference = $prev }
    return [pscustomobject]@{ Code = $c; Text = (($o -join "`n").Replace("`0", '')).Trim() }
}
$hasDocker = [bool](Get-Command docker -ErrorAction SilentlyContinue)

# 1. Windows
try {
    $os = Get-CimInstance Win32_OperatingSystem
    $build = [int]$os.BuildNumber
    Add-Check 'Windows 11' $(if ($build -ge 22000) { 'PASS' } else { 'FAIL' }) ("{0} (build {1}; Windows 11 can build >= 22000)" -f $os.Caption, $build)
} catch { Add-Check 'Windows 11' 'BLOCKED' "Không đọc được phiên bản Windows: $($_.Exception.Message)" }

# 2. Administrator (thong tin; khong bat buoc de chay kiem thu)
try {
    $admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    Add-Check 'Quyền Administrator' $(if ($admin) { 'PASS' } else { 'FAIL' }) $(if ($admin) { 'Đang chạy với quyền Administrator.' } else { 'Không phải Administrator. Không bắt buộc để chạy H1–H6 nếu tài khoản thuộc nhóm docker-users; chỉ cần khi cài/sửa WSL, Docker Desktop.' }) $false
} catch { Add-Check 'Quyền Administrator' 'BLOCKED' $_.Exception.Message $false }

# 3. WSL2
if (-not (Get-Command wsl -ErrorAction SilentlyContinue)) {
    Add-Check 'WSL2' 'FAIL' 'Không có wsl.exe.'
} else {
    $w = Run 'wsl' @('-l', '-v')
    if ($w.Text -match '(?i)Usage:|wsl\.exe \[Argument\]|--install') { Add-Check 'WSL2' 'FAIL' 'wsl.exe chỉ là bản stub (in trợ giúp): WSL chưa được cài. Cài bằng "wsl --install" (cần Administrator và khởi động lại).' }
    elseif ($w.Code -ne 0) { Add-Check 'WSL2' 'BLOCKED' ("wsl -l -v trả về exit code {0}: {1}" -f $w.Code, ($w.Text -replace '\s+', ' ')) }
    else {
        $v2 = @($w.Text -split "`n" | Where-Object { $_ -match '\s2\s*$' })
        if ($v2.Count -ge 1) { Add-Check 'WSL2' 'PASS' ("Có {0} distro chạy WSL phiên bản 2: {1}" -f $v2.Count, (($v2 | ForEach-Object { ($_ -replace '^\s*\*?\s*', '').Split(' ')[0] }) -join ', ')) }
        else { Add-Check 'WSL2' 'FAIL' ("Không thấy distro nào ở phiên bản 2. Kết quả: " + ($w.Text -replace '\s+', ' ')) }
    }
}

# 4. Docker CLI
if (-not $hasDocker) { Add-Check 'Docker CLI' 'FAIL' 'Không có "docker" trong PATH.' }
else { $r = Run 'docker' @('--version'); Add-Check 'Docker CLI' $(if ($r.Code -eq 0) { 'PASS' } else { 'FAIL' }) $r.Text }

# 5. Docker Desktop
$dd = Get-Process -Name 'Docker Desktop' -ErrorAction SilentlyContinue
$ddPath = Test-Path 'C:\Program Files\Docker\Docker\Docker Desktop.exe'
if ($dd) { Add-Check 'Docker Desktop' 'PASS' 'Tiến trình Docker Desktop đang chạy.' }
elseif ($ddPath) { Add-Check 'Docker Desktop' 'FAIL' 'Đã cài nhưng chưa chạy: mở Docker Desktop và chờ "Engine running".' }
elseif ($hasDocker) { Add-Check 'Docker Desktop' 'BLOCKED' 'Có docker CLI nhưng không thấy Docker Desktop (có thể dùng engine khác). Không kết luận được.' $false }
else { Add-Check 'Docker Desktop' 'FAIL' 'Chưa cài Docker Desktop.' }

# 6. Docker daemon (Linux containers)
$daemonOk = $false
if ($hasDocker) {
    $r = Run 'docker' @('info', '--format', '{{.ServerVersion}}|{{.OSType}}')
    if ($r.Code -eq 0 -and $r.Text -match '^\S+\|linux') { $daemonOk = $true; Add-Check 'Docker daemon (Linux containers)' 'PASS' ("Engine {0}" -f $r.Text.Replace('|', ', ')) }
    elseif ($r.Code -eq 0) { Add-Check 'Docker daemon (Linux containers)' 'FAIL' ("Engine không ở chế độ Linux containers: $($r.Text)") }
    else { Add-Check 'Docker daemon (Linux containers)' 'FAIL' ("docker info lỗi (exit {0}): {1}" -f $r.Code, ($r.Text -replace '\s+', ' ')) }
} else { Add-Check 'Docker daemon (Linux containers)' 'BLOCKED' 'Không có Docker CLI nên không kiểm tra được daemon.' }

# 7. Docker Compose v2
if ($hasDocker) {
    $r = Run 'docker' @('compose', 'version', '--short')
    if ($r.Code -eq 0 -and $r.Text -match '^v?(\d+)\.') { Add-Check 'Docker Compose v2' $(if ([int]$Matches[1] -ge 2) { 'PASS' } else { 'FAIL' }) ("Compose {0}" -f $r.Text) }
    else { Add-Check 'Docker Compose v2' 'FAIL' ("docker compose version lỗi: " + ($r.Text -replace '\s+', ' ')) }
} else { Add-Check 'Docker Compose v2' 'BLOCKED' 'Không có Docker CLI.' }

# 8. Container / volume hien co (thong tin)
if ($daemonOk) {
    $c = Run 'docker' @('ps', '-a', '--filter', "label=com.docker.compose.project=$Project", '--format', '{{.Names}}|{{.Status}}')
    $v = Run 'docker' @('volume', 'ls', '--format', '{{.Name}}', '--filter', 'name=etek_wcs')
    $cl = @($c.Text -split "`n" | Where-Object { $_ })
    $vl = @($v.Text -split "`n" | Where-Object { $_ })
    $detail = if ($cl.Count -eq 0) { "Chưa có container của project '$Project'." } else { ("Container ({0}): {1}" -f $cl.Count, ($cl -join '; ')) }
    $detail += if ($vl.Count -eq 0) { ' Chưa có volume etek_wcs_*.' } else { ' Volume: ' + ($vl -join ', ') + ' (chứa dữ liệu: bộ công cụ KHÔNG xóa chúng).' }
    if ($cl.Count -gt 0) { $detail += ' LƯU Ý: stack đang tồn tại; H3 và H6 sẽ gây gián đoạn tạm thời nếu được xác nhận.' }
    Add-Check 'Container và volume hiện có' 'PASS' $detail $false
} else { Add-Check 'Container và volume hiện có' 'BLOCKED' 'Cần Docker daemon.' $false }

# 9. Dung luong o dia
try {
    $sys = (Get-CimInstance Win32_OperatingSystem).SystemDrive.TrimEnd(':')
    $free = [math]::Round((Get-PSDrive $sys.Substring(0, 1)).Free / 1GB, 1)
    Add-Check "Dung lượng ổ đĩa (ổ $sys)" $(if ($free -ge $MinFreeGB) { 'PASS' } else { 'FAIL' }) ("Trống {0} GB (yêu cầu tối thiểu {1} GB cho image và dữ liệu thử)" -f $free, $MinFreeGB)
} catch { Add-Check 'Dung lượng ổ đĩa' 'BLOCKED' $_.Exception.Message }

# 10. Tep cau hinh va thu muc log
Add-Check 'Mã nguồn và compose' $(if (Test-Path -LiteralPath $ctx.ComposeFile) { 'PASS' } else { 'FAIL' }) ("Compose: " + $ctx.ComposeFile)
if (Test-Path -LiteralPath $ctx.EnvFile) {
    $m = Get-EnvMap $ctx
    $missing = @('POSTGRES_PASSWORD') | Where-Object { -not $m[$_] }
    $adm = if ($m['WCS_ADMIN_BOOTSTRAP_PASSWORD']) { 'có' } else { 'không có (Admin phải đã tồn tại)' }
    $plc = if ($m['PLC_MODE']) { $m['PLC_MODE'] } else { 'RealReadOnly (mặc định)' }
    $live = if ($m['PLC_ALLOW_LIVE_CONNECTION']) { $m['PLC_ALLOW_LIVE_CONNECTION'] } else { 'false (mặc định)' }
    if ($missing.Count -gt 0) { Add-Check 'Cấu hình .env' 'FAIL' ("Thiếu biến bắt buộc: {0}. File: {1} (chỉ ghi TÊN biến, không ghi giá trị)" -f ($missing -join ', '), $ctx.EnvFile) }
    else { Add-Check 'Cấu hình .env' 'PASS' ("{0}: POSTGRES_PASSWORD có; bootstrap admin: {1}; PLC_MODE={2}; PLC_ALLOW_LIVE_CONNECTION={3}; WCS_COOKIE_SECURE={4}; WCS_HTTP_PORT={5}" -f $ctx.EnvFile, $adm, $plc, $live, $(if ($m['WCS_COOKIE_SECURE']) { $m['WCS_COOKIE_SECURE'] } else { 'Always (mặc định)' }), $(if ($m['WCS_HTTP_PORT']) { $m['WCS_HTTP_PORT'] } else { '80' })) }
    if ($live -eq 'true') { Add-Check 'An toàn PLC' 'FAIL' 'PLC_ALLOW_LIVE_CONNECTION=true: bộ kiểm thử KHÔNG được chạy khi cho phép kết nối PLC thật. Đặt false cho đợt nghiệm thu.' }
    else { Add-Check 'An toàn PLC' 'PASS' 'Không cho phép kết nối PLC thật (PLC_ALLOW_LIVE_CONNECTION không phải true).' }
} else {
    Add-Check 'Cấu hình .env' 'FAIL' ("Chưa có {0}. Sao chép deployment\.env.example sang đó và điền POSTGRES_PASSWORD." -f $ctx.EnvFile)
    Add-Check 'An toàn PLC' 'BLOCKED' 'Không đọc được .env.'
}
try {
    if (-not (Test-Path -LiteralPath $ctx.LogDir)) { Add-Check 'Thư mục log triển khai' 'PASS' ("{0} chưa tồn tại: script triển khai sẽ tự tạo (preflight không tạo)." -f $ctx.LogDir) $false }
    else { Add-Check 'Thư mục log triển khai' 'PASS' ("{0} tồn tại ({1} file)." -f $ctx.LogDir, @(Get-ChildItem -LiteralPath $ctx.LogDir -File -ErrorAction SilentlyContinue).Count) $false }
} catch { Add-Check 'Thư mục log triển khai' 'BLOCKED' $_.Exception.Message $false }

# 11. Git (thong tin)
try {
    if (Get-Command git -ErrorAction SilentlyContinue) {
        $b = (Run 'git' @('-C', $ctx.RepoDir, 'branch', '--show-current')).Text; $h = (Run 'git' @('-C', $ctx.RepoDir, 'rev-parse', '--short', 'HEAD')).Text
        $dirty = @((Run 'git' @('-C', $ctx.RepoDir, 'status', '--short')).Text -split "`n" | Where-Object { $_ }).Count
        Add-Check 'Git' 'PASS' ("branch {0}, HEAD {1}, {2} thay đổi chưa commit. (Kỳ vọng nghiệm thu: develop @ 939b453 hoặc commit mới hơn của develop.)" -f $b, $h, $dirty) $false
    } else { Add-Check 'Git' 'BLOCKED' 'Không có git.' $false }
} catch { Add-Check 'Git' 'BLOCKED' $_.Exception.Message $false }

# 12. PostgreSQL thu nghiem (container trong stack; KHONG ket noi toi PostgreSQL cua may phat trien)
if ($daemonOk -and (Test-Path -LiteralPath $ctx.EnvFile)) {
    $id = Get-ServiceContainer $ctx 'postgres' 'preflight'
    if (-not $id) { Add-Check 'PostgreSQL thử nghiệm (container)' 'BLOCKED' 'Stack chưa chạy: chưa có container postgres. Chạy start.ps1/deploy.ps1 rồi chạy lại preflight.' }
    else {
        $i = Get-Inspect $ctx $id 'preflight'
        if (-not $i) { Add-Check 'PostgreSQL thử nghiệm (container)' 'BLOCKED' 'Không đọc được thông tin container postgres.' }
        else {
            $pgReady = Run 'docker' @('exec', $id, 'pg_isready', '-q')
            $okLabel = ($i.Labels.Project -eq $Project -and $i.Labels.Service -eq 'postgres')
            $hostPub = ($i.PublishedPorts.Count -gt 0)
            $vol = @($i.Mounts | Where-Object { $_.Destination -eq '/var/lib/postgresql/data' -and $_.Name -eq 'etek_wcs_pgdata' }).Count -gt 0
            $detail = "Container {0} ({1}), trạng thái {2}, health {3}; pg_isready exit {4}; label project/service đúng: {5}; volume etek_wcs_pgdata gắn đúng: {6}; cổng publish ra host: {7}." -f $i.Name, $i.Image, $i.Status, $i.Health, $pgReady.Code, $okLabel, $vol, $(if ($hostPub) { ($i.PublishedPorts -join ';') } else { 'không (đúng thiết kế)' })
            Add-Check 'PostgreSQL thử nghiệm (container)' $(if ($i.Running -and $pgReady.Code -eq 0 -and $okLabel -and $vol -and -not $hostPub) { 'PASS' } else { 'FAIL' }) ($detail + ' Đây là PostgreSQL TRONG Docker, khác với dịch vụ PostgreSQL của máy phát triển (cổng 5432 trên host).')
        }
    }
} else { Add-Check 'PostgreSQL thử nghiệm (container)' 'BLOCKED' 'Cần Docker daemon và file .env.' }

# 13. Cong web
try {
    $m = Get-EnvMap $ctx; $port = if ($m['WCS_HTTP_PORT']) { [int]$m['WCS_HTTP_PORT'] } else { 80 }
    $listen = @(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue)
    if ($listen.Count -eq 0) { Add-Check "Cổng web $port" 'PASS' 'Cổng đang trống (stack chưa chạy hoặc chưa publish).' $false }
    else { Add-Check "Cổng web $port" 'PASS' ("Cổng đang được dùng bởi PID {0} (nếu là Docker Desktop/com.docker.backend thì đúng là frontend của stack)." -f (($listen | Select-Object -First 1).OwningProcess)) $false }
} catch { Add-Check 'Cổng web' 'BLOCKED' $_.Exception.Message $false }

# ------------------------------------------------------------------------------------------------------------------ ket qua
$req = @($checks | Where-Object { $_.Required })
$notReady = @($req | Where-Object { $_.Status -eq 'FAIL' }).Count
$unknown = @($req | Where-Object { $_.Status -eq 'BLOCKED' }).Count
$verdict = if ($notReady -gt 0) { 'CHƯA SẴN SÀNG (có mục bắt buộc KHÔNG ĐẠT)' } elseif ($unknown -gt 0) { 'CHƯA ĐỦ THÔNG TIN (có mục bắt buộc CHƯA THỂ KIỂM TRA)' } else { 'SẴN SÀNG chạy run-acceptance.ps1' }
$exit = if ($notReady -gt 0) { 1 } elseif ($unknown -gt 0) { 2 } else { 0 }

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("# Kết quả preflight – $((Get-Date).ToString('yyyy-MM-dd HH:mm:ss'))")
[void]$sb.AppendLine('')
[void]$sb.AppendLine('| Mục | Bắt buộc | Kết quả | Chi tiết |')
[void]$sb.AppendLine('|---|---|---|---|')
foreach ($c in $checks) { [void]$sb.AppendLine(("| {0} | {1} | **{2}** | {3} |" -f $c.Name, $(if ($c.Required) { 'có' } else { 'không' }), (Get-StatusVi $c.Status), ($c.Detail -replace '\|', '/' -replace "`n", ' '))) }
[void]$sb.AppendLine('')
[void]$sb.AppendLine("**Kết luận:** $verdict")
$text = $sb.ToString()

Write-Host ''
foreach ($c in $checks) {
    $color = switch ($c.Status) { 'PASS' { 'Green' } 'FAIL' { 'Red' } default { 'Yellow' } }
    Write-Host ("[{0,-18}] {1}{2}" -f (Get-StatusVi $c.Status), $c.Name, $(if (-not $c.Required) { ' (không bắt buộc)' } else { '' })) -ForegroundColor $color
    Write-Host ("                     {0}" -f $c.Detail)
}
Write-Host ''
Write-Host "KẾT LUẬN: $verdict" -ForegroundColor $(if ($exit -eq 0) { 'Green' } else { 'Yellow' })

if ($OutFile) {
    try { New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutFile) | Out-Null; [System.IO.File]::WriteAllText($OutFile, $text, $script:Utf8NoBom); Write-Host "Đã ghi: $OutFile" } catch { Write-Host "Không ghi được $OutFile : $($_.Exception.Message)" -ForegroundColor Yellow }
}
exit $exit
