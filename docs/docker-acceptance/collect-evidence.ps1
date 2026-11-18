<#
.SYNOPSIS
  Thu thap bang chung Docker de doi chieu H1-H6 (CR-007). CHI DOC: khong khoi dong/dung container, khong xoa gi.
.DESCRIPTION
  Ghi vao -OutDir: phien ban Docker/Compose, trang thai container, restart count, OOMKilled, cau hinh bind mount (RW hay chi-doc), cong publish,
  volume/network, log ung dung (tail), log trien khai (deploy-*.log, deployment-events-*.jsonl). TAT CA van ban deu duoc che bi mat.
  KHONG ghi Config.Env cua container (co the chua mat khau), KHONG ghi gia tri .env (chi liet ke TEN bien).
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File docs\docker-acceptance\collect-evidence.ps1 -OutDir C:\ETEK-WCS\acceptance-evidence\manual
#>
param(
    [Parameter(Mandatory = $true)][string]$OutDir,
    [string]$Project = 'etek-wcs',
    [string]$RepoDir,
    [string]$ConfigDir,
    [string]$LogDir,
    [int]$TailLines = 500,
    [switch]$Quiet
)
. (Join-Path $PSScriptRoot '_accept-common.ps1')
$ctx = New-AcceptContext -RepoDir $RepoDir -ConfigDir $ConfigDir -LogDir $LogDir -Project $Project -Tag 'evidence'
$ctx.EvidenceDir = $OutDir
try { Initialize-EvidenceDir $ctx } catch { Write-Host "Không tạo được $OutDir : $($_.Exception.Message)" -ForegroundColor Red; exit 3 }
function Say([string]$t) { if (-not $Quiet) { Write-Host $t } }

$index = New-Object System.Collections.Generic.List[string]
function Note([string]$line) { $index.Add((Hide-Secrets $ctx $line)) }
$stamp = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
Note "# Chỉ mục bằng chứng – $stamp"
Note ''

# ------------------------------------------------------------------------------------------------------------------ git
try {
    $b = (& git -C $ctx.RepoDir branch --show-current 2>$null); $h = (& git -C $ctx.RepoDir rev-parse --short HEAD 2>$null); $dirty = @(& git -C $ctx.RepoDir status --short 2>$null).Count
    Write-Evidence $ctx 'git.txt' ("branch: $b`r`nHEAD: $h`r`nthay đổi chưa commit: $dirty") | Out-Null
    Note "- git: branch ``$b``, HEAD ``$h``, $dirty thay đổi chưa commit"
} catch { Note '- git: không đọc được' }

# ------------------------------------------------------------------------------------------------------------------ docker
$hasDocker = [bool](Get-Command docker -ErrorAction SilentlyContinue)
if (-not $hasDocker) {
    Write-Evidence $ctx 'docker-version.txt' 'docker: KHÔNG CÓ trong PATH (chưa cài Docker Desktop?)' | Out-Null
    Note '- Docker: **không có** docker trong PATH; không thu thập được dữ liệu container.'
} else {
    $v = Invoke-Docker $ctx @('version') 'docker version' 'evidence'
    $cv = Invoke-Docker $ctx @('compose', 'version') 'compose version' 'evidence'
    $inf = Invoke-Docker $ctx @('info', '--format', 'ServerVersion={{.ServerVersion}}; OSType={{.OSType}}; Containers={{.Containers}}; Running={{.ContainersRunning}}; MemTotal={{.MemTotal}}; NCPU={{.NCPU}}') 'docker info (tóm tắt)' 'evidence'
    Write-Evidence $ctx 'docker-version.txt' ("== docker version`r`n{0}`r`n`r`n== docker compose version`r`n{1}`r`n`r`n== docker info (tóm tắt)`r`n{2}" -f $v.Output, $cv.Output, $inf.Output) | Out-Null
    Note ("- Docker: {0}; {1}" -f (($v.Lines | Where-Object { $_ -match 'Version' } | Select-Object -First 2) -join ' '), ($cv.Output -replace '\s+', ' '))
    if ($inf.ExitCode -ne 0) { Note '- **Docker daemon không phản hồi**: chỉ thu được phiên bản CLI.' }
    else {
        $ps = Invoke-Docker $ctx @('ps', '-a', '--filter', "label=com.docker.compose.project=$Project", '--format', '{{.ID}}|{{.Names}}|{{.Image}}|{{.Status}}|{{.Ports}}') 'container của project' 'evidence'
        Write-Evidence $ctx 'containers.txt' $ps.Output | Out-Null
        $vol = Invoke-Docker $ctx @('volume', 'ls', '--format', '{{.Name}}|{{.Driver}}', '--filter', 'name=etek_wcs') 'volume etek_wcs_*' 'evidence'
        $net = Invoke-Docker $ctx @('network', 'ls', '--format', '{{.Name}}|{{.Driver}}|{{.Scope}}', '--filter', "label=com.docker.compose.project=$Project") 'network của project' 'evidence'
        Write-Evidence $ctx 'volumes-networks.txt' ("== volumes`r`n{0}`r`n`r`n== networks`r`n{1}" -f $vol.Output, $net.Output) | Out-Null
        Note ("- Container ({0}): xem ``containers.txt``; volume/network: ``volumes-networks.txt``" -f @($ps.Lines | Where-Object { $_ }).Count)

        $rows = New-Object System.Collections.Generic.List[object]
        foreach ($svc in 'postgres', 'migrator', 'backend', 'frontend') {
            $r = Invoke-Compose $ctx @('ps', '-a', '-q', $svc) "id $svc" 'evidence'
            $id = ($r.Lines | Where-Object { $_ -match '^[0-9a-f]{12,64}$' } | Select-Object -First 1)
            if (-not $id) { $rows.Add([pscustomobject]@{ Service = $svc; Container = '(chưa tạo)'; Status = ''; Running = ''; Health = ''; Restarts = ''; OOMKilled = ''; ExitCode = ''; StartedAt = ''; Ports = '' }); continue }
            $i = Get-Inspect $ctx $id 'evidence'
            if (-not $i) { continue }
            $rows.Add([pscustomobject]@{ Service = $svc; Container = $i.Name; Status = $i.Status; Running = $i.Running; Health = $i.Health; Restarts = $i.RestartCount; OOMKilled = $i.OOMKilled; ExitCode = $i.ExitCode; StartedAt = $i.StartedAt; Ports = $(if ($i.PublishedPorts.Count -gt 0) { $i.PublishedPorts -join ';' } else { 'không publish' }) })
            Write-Evidence $ctx "container-$svc.json" ($i | ConvertTo-Json -Depth 6) | Out-Null
            Write-Evidence $ctx "mounts-$svc.txt" (($i.Mounts | ForEach-Object { "{0,-6} {1}  ->  {2}   RW={3}  Mode={4}" -f $_.Type, $(if ($_.Name) { $_.Name } else { $_.Source }), $_.Destination, $_.RW, $_.Mode }) -join "`r`n") | Out-Null
            $tail = Invoke-Compose $ctx @('logs', '--no-color', '--tail', "$TailLines", $svc) "log $svc" 'evidence'
            Write-Evidence $ctx "logs-$svc.txt" $tail.Output | Out-Null
        }
        Write-Evidence $ctx 'container-status.md' ((@('| Service | Container | Trạng thái | Chạy | Health | RestartCount | OOMKilled | ExitCode | Bắt đầu | Cổng publish |', '|---|---|---|---|---|---|---|---|---|---|') + ($rows | ForEach-Object { "| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} |" -f $_.Service, $_.Container, $_.Status, $_.Running, $_.Health, $_.Restarts, $_.OOMKilled, $_.ExitCode, $_.StartedAt, $_.Ports })) -join "`r`n") | Out-Null
        Note '- Trạng thái container, RestartCount, OOMKilled, health: `container-status.md` (chi tiết `container-<service>.json`)'
        Note '- Cấu hình bind mount và cờ chỉ-đọc (RW=false): `mounts-<service>.txt` (backend phải có `/deploy-logs` với RW=False)'
        Note ("- Log ứng dụng (tail {0} dòng, đã che bí mật): ``logs-<service>.txt``" -f $TailLines)
    }
}

# ------------------------------------------------------------------------------------------------------------------ log trien khai (tren host)
try {
    if (Test-Path -LiteralPath $ctx.LogDir) {
        $files = @(Get-ChildItem -LiteralPath $ctx.LogDir -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime)
        Write-Evidence $ctx 'deployment-logs-listing.txt' (($files | ForEach-Object { "{0}  {1,10:N0} bytes  {2}" -f $_.LastWriteTime.ToString('o'), $_.Length, $_.Name }) -join "`r`n") | Out-Null
        foreach ($f in ($files | Where-Object { $_.Name -like 'deploy-*.log' -or $_.Name -like 'deployment-events-*.jsonl' } | Select-Object -Last 4)) {
            $lines = @([System.IO.File]::ReadAllLines($f.FullName) | Select-Object -Last 200)
            Write-Evidence $ctx ("deployment-logs\" + $f.Name + '.tail.txt') ($lines -join "`r`n") | Out-Null
        }
        $leaks = @(Find-SecretLeaks $ctx @($files | ForEach-Object { $_.FullName }))
        Note ("- Log triển khai ({0} file) tại ``{1}``: ``deployment-logs-listing.txt``; quét bí mật: {2}" -f $files.Count, $ctx.LogDir, $(if ($leaks.Count -eq 0) { 'không phát hiện' } else { 'PHÁT HIỆN trong ' + ($leaks -join ', ') }))
    } else { Note ("- Thư mục log triển khai ``{0}`` chưa tồn tại." -f $ctx.LogDir) }
} catch { Note ("- Không đọc được log triển khai: {0}" -f $_.Exception.Message) }

# ------------------------------------------------------------------------------------------------------------------ cau hinh (chi TEN bien)
try {
    if (Test-Path -LiteralPath $ctx.EnvFile) {
        $m = Get-EnvMap $ctx
        $safe = @('PLC_MODE', 'PLC_ALLOW_LIVE_CONNECTION', 'PLC_ALLOW_SIMULATOR_IN_PRODUCTION', 'WCS_COOKIE_SECURE', 'WCS_HTTP_PORT', 'POSTGRES_DB', 'POSTGRES_USER', 'WCS_LOG_LEVEL', 'WCS_VERSION')
        $txt = ($m.Keys | Sort-Object | ForEach-Object { if ($safe -contains $_) { "{0}={1}" -f $_, $m[$_] } else { "{0} -> (có giá trị, ẩn)" -f $_ } }) -join "`r`n"
        Write-Evidence $ctx 'env-keys.txt' $txt | Out-Null
        Note '- Cấu hình .env: chỉ liệt kê TÊN biến và vài giá trị không nhạy cảm (`env-keys.txt`); mật khẩu/token không được ghi.'
    } else { Note ("- Chưa có {0}" -f $ctx.EnvFile) }
} catch { }

Note ''
Note '> Mọi file trong thư mục này đã được che mật khẩu/token/cookie. Không có Config.Env của container, không có connection string.'
Write-Evidence $ctx 'evidence-index.md' ($index -join "`r`n") | Out-Null
Say ("Đã thu thập bằng chứng vào: {0}" -f $OutDir)
exit 0
