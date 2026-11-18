# Thu vien dung chung cua bo kiem thu nghiem thu Docker CR-007 (dot-source). Windows PowerShell 5.1 hoac PowerShell 7.
# File nay duoc luu UTF-8 co BOM de PowerShell 5.1 doc dung tieng Viet. KHONG cai dat, KHONG xoa du lieu, KHONG sua cau hinh he thong.

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

# ------------------------------------------------------------------------------------------------------------------ trang thai
$script:StatusVi = @{ PASS = 'ĐẠT'; FAIL = 'KHÔNG ĐẠT'; BLOCKED = 'CHƯA THỂ KIỂM TRA' }
function Get-StatusVi([string]$s) { if ($script:StatusVi.ContainsKey($s)) { return $script:StatusVi[$s] } else { return $s } }

# ------------------------------------------------------------------------------------------------------------------ duong dan / ngu canh
function New-AcceptContext {
    param([string]$RepoDir, [string]$ConfigDir, [string]$LogDir, [string]$EvidenceRoot, [string]$Project = 'etek-wcs', [string]$Tag = 'run')
    $ctx = [ordered]@{}
    $here = Split-Path -Parent $PSCommandPath
    $ctx.PackageDir = $here
    $ctx.RepoDir    = if ($RepoDir) { $RepoDir } else { Split-Path -Parent (Split-Path -Parent $here) }
    $root           = if ($env:WCS_ROOT_DIR) { $env:WCS_ROOT_DIR } else { 'C:\ETEK-WCS' }
    $ctx.ConfigDir  = if ($ConfigDir) { $ConfigDir } elseif ($env:WCS_CONFIG_DIR) { $env:WCS_CONFIG_DIR } else { Join-Path $root 'config' }
    $ctx.LogDir     = if ($LogDir) { $LogDir } elseif ($env:WCS_LOG_DIR) { $env:WCS_LOG_DIR } else { Join-Path $root 'logs' }
    $ctx.EnvFile    = Join-Path $ctx.ConfigDir '.env'
    $ctx.DeployDir  = Join-Path $ctx.RepoDir 'deployment'
    $ctx.ComposeFile = Join-Path $ctx.DeployDir 'docker-compose.yml'
    $ctx.Project    = $Project
    $evRoot         = if ($EvidenceRoot) { $EvidenceRoot } else { Join-Path $root 'acceptance-evidence' }
    $ctx.EvidenceDir = Join-Path $evRoot ((Get-Date).ToString('yyyyMMdd-HHmmss') + '-' + $Tag)
    $ctx.SecretCache = $null
    $ctx.NoEvidence = $false
    $ctx.Commands = New-Object System.Collections.Generic.List[string]
    return $ctx
}

function Initialize-EvidenceDir($ctx) {
    New-Item -ItemType Directory -Force -Path $ctx.EvidenceDir -ErrorAction Stop | Out-Null
    $probe = Join-Path $ctx.EvidenceDir ('.w-' + [guid]::NewGuid().ToString('N'))
    [System.IO.File]::WriteAllText($probe, '')
    Remove-Item -LiteralPath $probe -Force -ErrorAction SilentlyContinue      # chi xoa file thu ghi do chinh bo cong cu vua tao
}

# ------------------------------------------------------------------------------------------------------------------ che bi mat
function Get-EnvMap($ctx) {
    $map = @{}
    if (-not (Test-Path -LiteralPath $ctx.EnvFile)) { return $map }
    foreach ($line in Get-Content -LiteralPath $ctx.EnvFile -ErrorAction SilentlyContinue) {
        $t = $line.Trim()
        if ($t -eq '' -or $t.StartsWith('#')) { continue }
        $i = $t.IndexOf('=')
        if ($i -lt 1) { continue }
        $map[$t.Substring(0, $i).Trim()] = $t.Substring($i + 1).Trim()
    }
    return $map
}

function Get-SecretValues($ctx) {
    if ($null -ne $ctx.SecretCache) { return $ctx.SecretCache }
    $vals = New-Object System.Collections.Generic.List[string]
    $m = Get-EnvMap $ctx
    foreach ($k in $m.Keys) { if ($k -match '(?i)PASSWORD|SECRET|TOKEN|KEY' -and $m[$k].Length -ge 4) { $vals.Add($m[$k]) } }
    foreach ($extra in $script:ExtraSecrets) { if ($extra -and $extra.Length -ge 4) { $vals.Add($extra) } }
    $ctx.SecretCache = @($vals | Sort-Object { $_.Length } -Descending)
    return $ctx.SecretCache
}
$script:ExtraSecrets = @()
function Add-ExtraSecret($ctx, [string]$value) { if ($value) { $script:ExtraSecrets += $value; $ctx.SecretCache = $null } }

function Hide-Secrets($ctx, [string]$text) {
    if ([string]::IsNullOrEmpty($text)) { return $text }
    try {
        $t = $text
        foreach ($s in (Get-SecretValues $ctx)) { $t = $t.Replace($s, '***') }
        $t = [regex]::Replace($t, '(?i)\b(bearer|basic)\s+[A-Za-z0-9\-\._~\+/]{6,}=*', '$1 ***')
        $t = [regex]::Replace($t, '(?i)\b(password|passwd|pwd|secret|token|api[_-]?key|authorization|cookie|set-cookie|connection[_-]?string|private[_-]?key|postgres_password|bootstrap_admin_password)\b(["'']?\s*[=:]\s*)("[^"]*"|''[^'']*''|[^\s;,&"'']+)', '$1$2***')
        return $t
    } catch { return '***' }
}

function Find-SecretLeaks($ctx, [string[]]$files) {
    # tra ve so tep chua gia tri bi mat that (khong bao gio tra ve chinh gia tri)
    $leaks = @()
    $secrets = Get-SecretValues $ctx
    foreach ($f in $files) {
        try {
            $raw = [System.IO.File]::ReadAllText($f)
            foreach ($s in $secrets) { if ($raw.Contains($s)) { $leaks += (Split-Path -Leaf $f); break } }
            if ([regex]::IsMatch($raw, '(?i)\b(password|passwd|pwd|token|secret)\b["'']?\s*[=:]\s*(?!\*\*\*)(?!["'']?\s*$)[^\s;,&"'']{4,}')) { if ($leaks -notcontains (Split-Path -Leaf $f)) { $leaks += (Split-Path -Leaf $f) } }
        } catch { }
    }
    return $leaks
}

# ------------------------------------------------------------------------------------------------------------------ ghi file
$script:Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
function Write-Evidence($ctx, [string]$relPath, [string]$content) {
    if ($ctx.NoEvidence) { return $null }
    $p = Join-Path $ctx.EvidenceDir $relPath
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $p) | Out-Null
    [System.IO.File]::WriteAllText($p, (Hide-Secrets $ctx $content), $script:Utf8NoBom)
    return $p
}
function Append-Evidence($ctx, [string]$relPath, [string]$content) {
    if ($ctx.NoEvidence) { return }
    $p = Join-Path $ctx.EvidenceDir $relPath
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $p) | Out-Null
    [System.IO.File]::AppendAllText($p, (Hide-Secrets $ctx $content) + "`r`n", $script:Utf8NoBom)
}

# ------------------------------------------------------------------------------------------------------------------ chay lenh co ghi vet
function Invoke-Logged {
    # Chay mot lenh ngoai, ghi: thoi diem, lenh, exit code, stdout+stderr (da che bi mat). Khong nem loi khi lenh that bai.
    param($ctx, [string]$Exe, [string[]]$Arguments = @(), [string]$Label = '', [string]$TestId = 'common')
    $t0 = Get-Date
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    $lines = @(); $code = -1
    try {
        $lines = @(& $Exe @Arguments 2>&1 | ForEach-Object { "$_" })
        $code = $LASTEXITCODE
        if ($null -eq $code) { $code = 0 }
    } catch { $lines = @("EXCEPTION: $($_.Exception.Message)"); $code = -1 }
    finally { $ErrorActionPreference = $prev }
    $cmdline = ($Exe + ' ' + ($Arguments -join ' ')).Trim()
    $entry = ("[{0}] ({1:N1}s) exit={2} :: {3}{4}" -f $t0.ToString('o'), ((Get-Date) - $t0).TotalSeconds, $code, (Hide-Secrets $ctx $cmdline), $(if ($Label) { "  # $Label" } else { '' }))
    $ctx.Commands.Add($entry)
    Append-Evidence $ctx "$TestId\commands.log" ($entry + "`r`n" + (($lines | ForEach-Object { '    ' + $_ }) -join "`r`n"))
    return [pscustomobject]@{ ExitCode = $code; Lines = $lines; Output = ($lines -join "`n"); Command = $cmdline }
}

function Invoke-Docker($ctx, [string[]]$Arguments, [string]$Label = '', [string]$TestId = 'common') {
    return Invoke-Logged $ctx 'docker' $Arguments $Label $TestId
}

function Invoke-Compose($ctx, [string[]]$Arguments, [string]$Label = '', [string]$TestId = 'common') {
    $base = @('compose', '--project-directory', $ctx.DeployDir, '-f', $ctx.ComposeFile, '--env-file', $ctx.EnvFile, '-p', $ctx.Project)
    return Invoke-Logged $ctx 'docker' ($base + $Arguments) $Label $TestId
}

function Get-ServiceContainer($ctx, [string]$service, [string]$TestId = 'common') {
    $r = Invoke-Compose $ctx @('ps', '-q', $service) "id cua service $service" $TestId
    if ($r.ExitCode -ne 0) { return $null }
    $id = ($r.Lines | Where-Object { $_ -match '^[0-9a-f]{12,64}$' } | Select-Object -First 1)
    return $id
}

function Get-Inspect($ctx, [string]$id, [string]$TestId = 'common') {
    # Chi doc cac truong can thiet. KHONG luu Config.Env (chua mat khau).
    $r = Invoke-Docker $ctx @('inspect', $id) "inspect $id" $TestId
    if ($r.ExitCode -ne 0) { return $null }
    try { $j = $r.Output | ConvertFrom-Json } catch { return $null }
    $c = @($j)[0]
    if (-not $c) { return $null }
    $health = $null; $lastProbe = $null
    if ($c.State.PSObject.Properties.Name -contains 'Health' -and $c.State.Health) {
        $health = $c.State.Health.Status
        $lp = @($c.State.Health.Log) | Select-Object -Last 1
        if ($lp) { $lastProbe = [pscustomobject]@{ ExitCode = $lp.ExitCode; End = $lp.End; Output = (Hide-Secrets $ctx ("$($lp.Output)".Trim())) } }
    }
    $mounts = @($c.Mounts | ForEach-Object { [pscustomobject]@{ Type = $_.Type; Name = $_.Name; Source = $_.Source; Destination = $_.Destination; RW = $_.RW; Mode = $_.Mode } })
    $ports = @()
    if ($c.NetworkSettings -and $c.NetworkSettings.Ports) {
        foreach ($p in $c.NetworkSettings.Ports.PSObject.Properties) { if ($p.Value) { $ports += ("{0} -> {1}" -f $p.Name, ((@($p.Value) | ForEach-Object { "$($_.HostIp):$($_.HostPort)" }) -join ',')) } }
    }
    return [pscustomobject]@{
        Id = $c.Id; Name = ("$($c.Name)").TrimStart('/'); Image = $c.Config.Image; Status = $c.State.Status; Running = [bool]$c.State.Running; StartedAt = $c.State.StartedAt
        ExitCode = $c.State.ExitCode; OOMKilled = [bool]$c.State.OOMKilled; RestartCount = [int]$c.RestartCount; Health = $health; LastProbe = $lastProbe
        Mounts = $mounts; PublishedPorts = $ports
        Labels = @{ Project = $c.Config.Labels.'com.docker.compose.project'; Service = $c.Config.Labels.'com.docker.compose.service' }
    }
}

# ------------------------------------------------------------------------------------------------------------------ ghi nhan buoc / ket qua cua tung bai
function New-TestRecord([string]$Id, [string]$Title) {
    return [pscustomobject]@{ Id = $Id; Title = $Title; Status = 'BLOCKED'; Reason = 'Chưa chạy'; Steps = New-Object System.Collections.Generic.List[string]; Evidence = New-Object System.Collections.Generic.List[string]
        Commands = New-Object System.Collections.Generic.List[string]; Expected = ''; Actual = ''; Notes = New-Object System.Collections.Generic.List[string]; StartedAt = (Get-Date); EndedAt = $null }
}
function Add-Step($ctx, $rec, [string]$text) {
    $line = ("[{0}] {1}" -f (Get-Date).ToString('HH:mm:ss'), (Hide-Secrets $ctx $text))
    $rec.Steps.Add($line)
    Write-Host ("  {0}" -f $line)
    Append-Evidence $ctx "$($rec.Id)\steps.log" $line
}
function Add-Note($ctx, $rec, [string]$text) { $rec.Notes.Add((Hide-Secrets $ctx $text)) }
function Add-EvidenceRef($rec, [string]$relPath) { if ($relPath -and -not $rec.Evidence.Contains($relPath)) { $rec.Evidence.Add($relPath) } }
function Complete-Test($ctx, $rec, [string]$Status, [string]$Reason) {
    $rec.Status = $Status; $rec.Reason = (Hide-Secrets $ctx $Reason); $rec.EndedAt = Get-Date
    Write-Host ("=> {0}: {1}  ({2})" -f $rec.Id, (Get-StatusVi $Status), $rec.Reason) -ForegroundColor $(if ($Status -eq 'PASS') { 'Green' } elseif ($Status -eq 'FAIL') { 'Red' } else { 'Yellow' })
    Append-Evidence $ctx "$($rec.Id)\steps.log" ("KET QUA: {0} - {1}" -f (Get-StatusVi $Status), $rec.Reason)
    return $rec
}

# ------------------------------------------------------------------------------------------------------------------ xac nhan thao tac co anh huong
function Confirm-Action {
    # Dung neu co co (flag) tuong ung, hoac nguoi van hanh go dung cum xac nhan. Khong tuong tac duoc -> khong lam.
    param([bool]$FlagGiven, [string]$Phrase, [string]$Question, [bool]$NonInteractive)
    if ($FlagGiven) { return $true }
    if ($NonInteractive) { return $false }
    Write-Host ''
    Write-Host $Question -ForegroundColor Yellow
    $a = Read-Host ("Go chinh xac: {0}   (bat ky noi dung khac = huy)" -f $Phrase)
    return ($a -ceq $Phrase)
}

# ------------------------------------------------------------------------------------------------------------------ API (dang nhap Admin)
function Get-BaseUrl($ctx, [string]$Override) {
    if ($Override) { return $Override.TrimEnd('/') }
    $m = Get-EnvMap $ctx
    $port = if ($m['WCS_HTTP_PORT']) { $m['WCS_HTTP_PORT'] } else { '80' }
    if ($port -eq '80') { return 'http://localhost' } else { return "http://localhost:$port" }
}

function Read-AdminCredential($ctx, [string]$User, $SecurePassword, [bool]$NonInteractive) {
    # Mat khau Admin chi o trong bo nho; khong ghi ra dia, khong dua vao lenh, duoc them vao danh sach che bi mat.
    # Tu dong hoa khong tuong tac: bien moi truong WCS_ACCEPT_ADMIN_PASSWORD (khong dua mat khau vao dong lenh). Bien nay duoc xoa khoi tien trinh ngay sau khi doc.
    if (-not $SecurePassword -and $env:WCS_ACCEPT_ADMIN_PASSWORD) { $SecurePassword = ConvertTo-SecureString $env:WCS_ACCEPT_ADMIN_PASSWORD -AsPlainText -Force; Remove-Item Env:WCS_ACCEPT_ADMIN_PASSWORD -ErrorAction SilentlyContinue }
    if (-not $SecurePassword -and -not $NonInteractive) { $SecurePassword = Read-Host "Mat khau Admin '$User' (chi dung de doc /api/logs; khong luu)" -AsSecureString }
    if (-not $SecurePassword) { return $null }
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecurePassword)
    try { $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
    Add-ExtraSecret $ctx $plain
    return [pscustomobject]@{ User = $User; Password = $plain }
}

function Connect-Api($ctx, [string]$BaseUrl, $Cred) {
    # tra ve @{ Ok; Session; Reason }
    if (-not $Cred) { return @{ Ok = $false; Reason = 'Không có thông tin đăng nhập Admin (dùng -AdminUser và nhập mật khẩu, hoặc -AdminPassword dạng SecureString).' } }
    $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    try {
        $body = (@{ username = $Cred.User; password = $Cred.Password } | ConvertTo-Json)
        $r = Invoke-WebRequest -Uri "$BaseUrl/api/auth/login" -Method Post -Headers @{ 'X-WCS-Client' = 'web' } -ContentType 'application/json' -Body $body -WebSession $session -UseBasicParsing -TimeoutSec 20
        $me = ($r.Content | ConvertFrom-Json).data
        if ($me.mustChangePassword) { return @{ Ok = $false; Reason = 'Tài khoản Admin phải đổi mật khẩu trước (mustChangePassword). Hãy đăng nhập giao diện và đổi mật khẩu.' } }
        if ($me.role -ne 'Admin') { return @{ Ok = $false; Reason = "Tài khoản '$($Cred.User)' không phải Admin (role=$($me.role)); API nhật ký chỉ dành cho Admin." } }
        # kiem tra cookie phien thuc su dung duoc (cookie Secure + HTTP se khong duoc gui)
        $t = Invoke-WebRequest -Uri "$BaseUrl/api/logs/stats" -Headers @{ 'X-WCS-Client' = 'web' } -WebSession $session -UseBasicParsing -TimeoutSec 20
        return @{ Ok = $true; Session = $session; Reason = 'OK' }
    } catch {
        $resp = $_.Exception.Response
        $code = if ($resp) { [int]$resp.StatusCode } else { 0 }
        $hint = if ($code -eq 401) { ' (sai thông tin đăng nhập, hoặc cookie Secure không được gửi qua HTTP: đặt WCS_COOKIE_SECURE=SameAsRequest cho LAN thử nghiệm hoặc dùng -BaseUrl https://…)' } elseif ($code -eq 0) { ' (không kết nối được tới ' + $BaseUrl + ')' } else { '' }
        return @{ Ok = $false; Reason = "Đăng nhập/gọi API thất bại: HTTP $code$hint" }
    }
}

function Invoke-LogApi($ctx, [string]$BaseUrl, $Session, [string]$PathAndQuery) {
    try {
        $r = Invoke-WebRequest -Uri "$BaseUrl$PathAndQuery" -Headers @{ 'X-WCS-Client' = 'web' } -WebSession $Session -UseBasicParsing -TimeoutSec 30
        return @{ Ok = $true; Status = [int]$r.StatusCode; Data = ($r.Content | ConvertFrom-Json).data; Raw = $r.Content }
    } catch {
        $resp = $_.Exception.Response
        return @{ Ok = $false; Status = $(if ($resp) { [int]$resp.StatusCode } else { 0 }); Data = $null; Raw = '' }
    }
}

function Get-HealthzStatus([string]$BaseUrl) {
    try { return [int](Invoke-WebRequest -Uri "$BaseUrl/healthz" -UseBasicParsing -TimeoutSec 8).StatusCode }
    catch { $r = $_.Exception.Response; if ($r) { return [int]$r.StatusCode } else { return 0 } }
}
