# CR-007: tu kiem tra bo cong cu nghiem thu Docker (docs\docker-acceptance) KHONG can Docker.
#   Phan A - tinh: cu phap PowerShell, ma hoa, duong dan, lenh nguy hiem, nhat quan README <-> script.
#   Phan B - dong: chay run-acceptance.ps1 voi "docker" GIA LAP (tests\deployment\fake-docker) nhung backend va PostgreSQL that (DB tam + role tam),
#            kiem tra logic DAT / KHONG DAT / CHUA THE KIEM TRA, xac nhan truoc thao tac anh huong, che bi mat, khoi phuc sau H6.
#            Phan B can: dotnet, dich vu PostgreSQL cua may phat trien (chi tao DB tam wcs_acc va role tam wcs_acc_role roi xoa), WCS.Api da build.
# Day KHONG phai nghiem thu Docker: chi chung minh logic cua bo cong cu. Chay: powershell -NoProfile -ExecutionPolicy Bypass -File tests\deployment\Test-DockerAcceptancePackage.ps1
param([switch]$StaticOnly)
$ErrorActionPreference = 'Continue'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$pkg = Join-Path $repo 'docs\docker-acceptance'
$results = New-Object System.Collections.Generic.List[string]
function Check([string]$n, [bool]$ok, [string]$x = '') { $script:results.Add($(if ($ok) { 'PASS ' } else { 'FAIL ' }) + $n + $(if (-not $ok -and $x) { ' :: ' + $x } else { '' })) }

# =========================================================================================================== A. TINH
$files = @('README.md', 'preflight.ps1', 'run-acceptance.ps1', 'collect-evidence.ps1', 'report-template.md', '_accept-common.ps1')
$missing = @($files | Where-Object { -not (Test-Path -LiteralPath (Join-Path $pkg $_)) })
Check 'đủ file bắt buộc trong docs\docker-acceptance' ($missing.Count -eq 0) ($missing -join ',')
$ps1 = @(Get-ChildItem -LiteralPath $pkg -Filter *.ps1) + @(Get-ChildItem -LiteralPath (Join-Path $repo 'tests\deployment') -Recurse -Filter *.ps1)
$perr = @()
foreach ($f in $ps1) { $e = $null; $t = $null; [void][System.Management.Automation.Language.Parser]::ParseFile($f.FullName, [ref]$t, [ref]$e); if ($e.Count -gt 0) { $perr += ("{0}: {1} @{2}" -f $f.Name, $e[0].Message, $e[0].Extent.StartLineNumber) } }
Check ("cú pháp PowerShell hợp lệ ({0} file)" -f $ps1.Count) ($perr.Count -eq 0) ($perr -join '; ')
$bom = @(Get-ChildItem -LiteralPath $pkg -Filter *.ps1 | Where-Object { $b = [System.IO.File]::ReadAllBytes($_.FullName); -not ($b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF) })
Check 'script của bộ công cụ có UTF-8 BOM (PowerShell 5.1 đọc đúng tiếng Việt)' ($bom.Count -eq 0) (($bom | ForEach-Object Name) -join ',')

# lenh co the xoa du lieu / thay doi he thong: phan tich AST (chi xet LENH THUC SU, khong xet comment hay thong bao gui nguoi dung)
$all = ($ps1 | Where-Object { $_.FullName -like "$pkg*" } | ForEach-Object { [System.IO.File]::ReadAllText($_.FullName) }) -join "`n"
$badCmd = @('Stop-Service', 'Restart-Service', 'Restart-Computer', 'Stop-Computer', 'Set-ExecutionPolicy', 'Disable-NetFirewallRule', 'Set-NetFirewallRule', 'New-NetFirewallRule', 'Enable-WindowsOptionalFeature', 'Install-Package', 'Remove-Item -Recurse')
$forbiddenVerbs = @('prune', 'down', 'rm', 'rmi', 'kill', 'unregister', '--install', '--shutdown', 'pause', 'rename', 'update')
$dockerWrappers = @('Invoke-Docker', 'Invoke-Compose', 'Invoke-Logged', 'Run', 'docker', 'wsl', 'winget')
$violations = New-Object System.Collections.Generic.List[string]
foreach ($pf in (Get-ChildItem -LiteralPath $pkg -Filter *.ps1)) {
    $e = $null; $t = $null; $ast = [System.Management.Automation.Language.Parser]::ParseFile($pf.FullName, [ref]$t, [ref]$e)
    foreach ($c in $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true)) {
        $name = $c.GetCommandName()
        if (-not $name) { continue }
        if ($name -in @('Stop-Service', 'Restart-Service', 'Restart-Computer', 'Stop-Computer', 'Set-ExecutionPolicy', 'Disable-NetFirewallRule', 'Set-NetFirewallRule', 'New-NetFirewallRule', 'Enable-WindowsOptionalFeature', 'Install-Package', 'Disable-WindowsOptionalFeature', 'Set-Service')) { $violations.Add("$($pf.Name): lệnh '$name'") }
        if ($name -eq 'Remove-Item' -and $c.Extent.Text -match '-Recurse' -and $c.Extent.Text -notmatch '\$tmp') { $violations.Add("$($pf.Name): Remove-Item -Recurse ngoài thư mục tạm") }
        if ($name -in $dockerWrappers) {
            $strings = @($c.FindAll({ param($n) $n -is [System.Management.Automation.Language.StringConstantExpressionAst] }, $true) | ForEach-Object { $_.Value })
            $bad = @($strings | Where-Object { $_ -in $forbiddenVerbs })
            if ($bad.Count -gt 0) { $violations.Add("$($pf.Name): $name với đối số nguy hiểm: $($bad -join ',')") }
            if ($strings -contains 'volume' -and $strings -contains 'rm') { $violations.Add("$($pf.Name): docker volume rm") }
            if ($strings -contains 'system' -and $strings -contains 'prune') { $violations.Add("$($pf.Name): docker system prune") }
        }
    }
}
Check 'không có lệnh phá hủy/cấu hình hệ thống thực thi (prune, volume rm, compose down, rm/kill, Stop-Service, wsl --install, ...) - phân tích AST' ($violations.Count -eq 0) ($violations -join ' | ')
Check 'không có DROP DATABASE/ROLE trong bộ công cụ (chỉ test tự kiểm tra mới tạo/xóa DB tạm của riêng nó)' (-not ($all -match '(?i)DROP\s+(DATABASE|ROLE|TABLE)'))
$stops = [regex]::Matches($all, 'Invoke-Docker \$ctx @\(''(stop|start|restart|exec)''') | ForEach-Object { $_.Groups[1].Value } | Group-Object | ForEach-Object { "$($_.Name)=$($_.Count)" }
Write-Host ("  (lệnh docker có tác động trong bộ công cụ: {0})" -f ($stops -join ', '))
$rm = @([regex]::Matches($all, 'Remove-Item[^\n]*') | ForEach-Object { $_.Value })
$badRm = @($rm | Where-Object { $_ -notmatch '\$probe|\$tmp|\(Join-Path \$ctx\.LogDir \$name\)|Env:|\$tmp -Recurse' })
Check 'mọi Remove-Item chỉ xóa file/thư mục tạm do bộ công cụ tự tạo hoặc biến môi trường' ($badRm.Count -eq 0) ($badRm -join ' | ')
$stopGate = $all -match 'Confirm-Action \$ConfirmH3' -and $all -match 'STOP \$\(\$pi\.Name\)' -and $all -match 'Confirm-Action \$ConfirmX2'
Check 'H3, H6, X2 đều có bước xác nhận trước thao tác gây gián đoạn' $stopGate
Check 'H6 luôn khởi động lại PostgreSQL trong khối finally' ($all -match "(?s)finally \{\s*if \(\`$stopped\) \{\s*\`$s2 = Invoke-Docker \`$ctx @\('start'")
Check 'H6 xác nhận container bằng label project/service, image postgres:16, volume etek_wcs_pgdata, không publish cổng' (($all -match "Labels\.Project -ne") -and ($all -match "postgres:16") -and ($all -match "etek_wcs_pgdata") -and ($all -match 'PublishedPorts\.Count -gt 0'))
Check 'không dump Config.Env của container (có thể chứa mật khẩu)' (-not ($all -match '\.Config\.Env') -and -not ($all -match '\{\{json \.Config\}\}'))
Check 'không có địa chỉ/lệnh tới PLC, RCS, AGV thật' (-not ($all -match '192\.168\.10\.\d+') -and -not ($all -match '(?i)Test-NetConnection|plc-route-test'))

$readme = [System.IO.File]::ReadAllText((Join-Path $pkg 'README.md'))
$scriptNames = @('preflight.ps1', 'run-acceptance.ps1', 'collect-evidence.ps1', 'report-template.md')
Check 'README nhắc đủ preflight, run-acceptance, collect-evidence, report-template' (@($scriptNames | Where-Object { $readme -notmatch [regex]::Escape($_) }).Count -eq 0)
$runText = [System.IO.File]::ReadAllText((Join-Path $pkg 'run-acceptance.ps1'))
$paramNames = [regex]::Matches($runText, '(?m)^\s+\[[^\]]+\]\$(\w+)') | ForEach-Object { $_.Groups[1].Value } | Where-Object { $_ -in @('AllowStackStart', 'ConfirmH3', 'ConfirmH6', 'ConfirmX2', 'ConfirmPostgresContainer', 'OutageSeconds', 'ImportWaitSeconds', 'NonInteractive', 'BaseUrl', 'AdminUser', 'AdminPassword', 'ManualUiCheck', 'ManualPlcToggle', 'H1Script', 'Tests', 'EvidenceRoot') } | Select-Object -Unique
$undocumented = @($paramNames | Where-Object { $readme -notmatch ('-' + $_ + '\b') })
Check ("README mô tả mọi tham số chính của run-acceptance.ps1 ({0})" -f $paramNames.Count) ($undocumented.Count -eq 0) ($undocumented -join ',')
$checklist = [System.IO.File]::ReadAllText((Join-Path $repo 'docs\DEPLOYMENT_TEST_CHECKLIST.md'))
$h = [regex]::Matches($checklist, '(?m)^\| (H[1-6]) \|') | ForEach-Object { $_.Groups[1].Value }
Check 'checklist gốc vẫn có H1–H6 và bộ công cụ có Test-H1..Test-H6' ((($h -join ',') -eq 'H1,H2,H3,H4,H5,H6') -and (@(1..6 | Where-Object { $runText -notmatch "function Test-H$_ " }).Count -eq 0))
$tpl = [System.IO.File]::ReadAllText((Join-Path $pkg 'report-template.md'))
$ph = [regex]::Matches($runText, "'(\{\{[A-Z_]+\}\})'") | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique
Check 'mọi placeholder mà run-acceptance điền đều có trong report-template.md' (@($ph | Where-Object { $tpl -notmatch [regex]::Escape($_) }).Count -eq 0) ((@($ph | Where-Object { $tpl -notmatch [regex]::Escape($_) })) -join ',')

if ($StaticOnly) { $results | ForEach-Object { Write-Host $_ }; $f = @($results | Where-Object { $_ -like 'FAIL*' }).Count; Write-Host ("{0} kiểm tra, {1} lỗi" -f $results.Count, $f); exit $(if ($f -eq 0) { 0 } else { 1 }) }

# =========================================================================================================== B. DONG (docker gia lap + backend that)
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
$psql = 'C:\Program Files\PostgreSQL\16\bin\psql.exe'
$be = Join-Path $repo 'src\backend'
if (-not $dotnet -or -not (Test-Path $psql) -or -not (Test-Path (Join-Path $be 'WCS.Api\bin\Debug\net10.0\WCS.Api.dll'))) {
    $results.Add('SKIP phần B (cần dotnet, PostgreSQL 16 cục bộ và WCS.Api đã build)')
    $results | ForEach-Object { Write-Host $_ }; Write-Host 'Phần B bị bỏ qua.'; exit 0
}
$tmp = Join-Path $env:TEMP ('wcs-acc-selftest-' + [guid]::NewGuid().ToString('N')); New-Item -ItemType Directory -Force -Path "$tmp\config", "$tmp\logs", "$tmp\state", "$tmp\bin", "$tmp\evidence" | Out-Null
Set-Location $be
$pw = ((dotnet user-secrets list --project WCS.Api) | Where-Object { $_ -like 'Database:Password*' }) -replace '^Database:Password\s*=\s*', ''
function Sql($db, $sql) { $f = Join-Path $env:TEMP ('s-' + [guid]::NewGuid().ToString('N') + '.sql'); Set-Content -LiteralPath $f -Value $sql -Encoding ascii; $env:PGPASSWORD = $pw; & $psql -h localhost -U postgres -d $db -w -At -f $f 2>&1; Remove-Item Env:PGPASSWORD; Remove-Item $f -ErrorAction SilentlyContinue }
$rolePw = 'Acc-role-' + [guid]::NewGuid().ToString('N').Substring(0, 10); $adminPw1 = 'Boot-' + [guid]::NewGuid().ToString('N').Substring(0, 10); $adminPw = 'Adm-' + [guid]::NewGuid().ToString('N').Substring(0, 10) + '!A1'
$secretPg = 'PgSecret-' + [guid]::NewGuid().ToString('N').Substring(0, 12)
$apiProc = $null
try {
    Sql 'postgres' 'DROP DATABASE IF EXISTS wcs_acc WITH (FORCE)' | Out-Null; Sql 'postgres' 'DROP ROLE IF EXISTS wcs_acc_role' | Out-Null
    Sql 'postgres' 'CREATE DATABASE wcs_acc' | Out-Null; Sql 'postgres' "CREATE ROLE wcs_acc_role LOGIN PASSWORD '$rolePw'" | Out-Null
    $env:ASPNETCORE_ENVIRONMENT = 'Development'; $env:ConnectionStrings__WcsDatabase = 'Host=localhost;Database=wcs_acc;Username=postgres'
    dotnet ef database update --project WCS.Infrastructure --startup-project WCS.Api 2>&1 | Select-Object -Last 1 | Out-Null
    Remove-Item Env:ConnectionStrings__WcsDatabase, Env:ASPNETCORE_ENVIRONMENT
    Sql 'wcs_acc' 'GRANT ALL ON SCHEMA public TO wcs_acc_role; GRANT ALL ON ALL TABLES IN SCHEMA public TO wcs_acc_role; GRANT ALL ON ALL SEQUENCES IN SCHEMA public TO wcs_acc_role;' | Out-Null

    # cau hinh gia lap: .env co bi mat, thu muc log, mot lan chay deploy/start "that" (tao bang chinh helper cua script trien khai)
    Set-Content "$tmp\config\.env" -Encoding ascii -Value @("POSTGRES_PASSWORD=$secretPg", 'POSTGRES_DB=wcs_db', 'POSTGRES_USER=wcs', 'WCS_HTTP_PORT=5017', 'PLC_MODE=RealReadOnly', 'PLC_ALLOW_LIVE_CONNECTION=false')
    $gen = @"
`$env:WCS_ROOT_DIR='$tmp'; `$env:WCS_CONFIG_DIR='$tmp\config'; `$env:WCS_LOG_DIR='$tmp\logs'
. '$repo\deployment\scripts\_common.ps1'
Start-WcsRun 'start.ps1'
Write-Step 'Starting containers'
Write-Ok 'postgres healthy; password=$secretPg was not printed'
Write-Step 'Applying migrations'
Write-Ok 'migrations applied'
Complete-WcsRun 0
"@
    Set-Content "$tmp\gen.ps1" -Value $gen -Encoding ascii
    & powershell -NoProfile -ExecutionPolicy Bypass -File "$tmp\gen.ps1" *> $null
    Check 'tạo log triển khai thật bằng helper của script (START/STEP/OK/END)' ((Get-ChildItem "$tmp\logs" -Filter 'deployment-events-*.jsonl' -ErrorAction SilentlyContinue | Measure-Object).Count -eq 1)

    # backend that (Development) bang role thuong, nhap log trien khai
    $env:ASPNETCORE_ENVIRONMENT = 'Development'; $env:ASPNETCORE_URLS = 'http://localhost:5017'; $env:ConnectionStrings__WcsDatabase = 'Host=localhost;Database=wcs_acc;Username=wcs_acc_role;Timeout=3;Command Timeout=3'
    $env:Database__Password = $rolePw; $env:Bootstrap__Admin__Password = $adminPw1; $env:DeploymentLogs__Directory = "$tmp\logs"
    $apiProc = Start-Process dotnet -ArgumentList 'run', '--project', 'WCS.Api', '--no-launch-profile', '--no-build' -WorkingDirectory $be -PassThru -WindowStyle Hidden -RedirectStandardOutput "$tmp\backend.out" -RedirectStandardError "$tmp\backend.err"
    Remove-Item Env:ASPNETCORE_ENVIRONMENT, Env:ASPNETCORE_URLS, Env:ConnectionStrings__WcsDatabase, Env:Database__Password, Env:Bootstrap__Admin__Password, Env:DeploymentLogs__Directory -ErrorAction SilentlyContinue
    $up = $false; 1..30 | ForEach-Object { if (-not $up) { Start-Sleep 2; try { $up = ((Invoke-WebRequest http://localhost:5017/healthz -UseBasicParsing -TimeoutSec 3).StatusCode -eq 200) } catch { } } }
    Check 'backend thật khởi động (healthz 200)' $up
    $h = @{ 'X-WCS-Client' = 'web' }; $ws = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    Invoke-RestMethod http://localhost:5017/api/auth/login -Method Post -Headers $h -ContentType 'application/json' -Body (@{ username = 'admin'; password = $adminPw1 } | ConvertTo-Json) -WebSession $ws | Out-Null
    Invoke-RestMethod http://localhost:5017/api/auth/change-password -Method Post -Headers $h -ContentType 'application/json' -Body (@{ currentPassword = $adminPw1; newPassword = $adminPw } | ConvertTo-Json) -WebSession $ws | Out-Null

    # docker gia lap
    Copy-Item (Join-Path $PSScriptRoot 'fake-docker\docker.ps1') "$tmp\bin\docker.ps1"
    $env:PATH = "$tmp\bin;$env:PATH"
    $env:FAKE_DOCKER_STATE = "$tmp\state"; $env:FAKE_DOCKER_LOGDIR = "$tmp\logs"; $env:FAKE_DOCKER_BACKEND_OUT = "$tmp\backend.out"; $env:FAKE_DOCKER_ROLE = 'wcs_acc_role'
    $env:FAKE_DOCKER_PGBIN = $psql; $env:FAKE_DOCKER_PGPW = $pw; $env:FAKE_DOCKER_MOUNT_RW = 'false'
    $env:WCS_ROOT_DIR = $tmp; $env:WCS_CONFIG_DIR = "$tmp\config"; $env:WCS_LOG_DIR = "$tmp\logs"
    $run = Join-Path $pkg 'run-acceptance.ps1'
    function Latest-Evidence { $d = Get-ChildItem "$tmp\evidence" -Directory -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -Last 1; if (-not $d) { throw 'run-acceptance không tạo thư mục bằng chứng' }; return $d.FullName }
    function Get-Results($dir) { Get-Content (Join-Path $dir 'results.json') -Raw -Encoding UTF8 | ConvertFrom-Json }

    # --- B1: cac bai chi-doc (H1, H2, H4, H5) voi docker gia lap + backend that; stack "khong co su kien PLC"
    $env:FAKE_DOCKER_HIDE_PLC = 'true'
    $env:WCS_ACCEPT_ADMIN_PASSWORD = $adminPw; $out1 = & powershell -NoProfile -ExecutionPolicy Bypass -File $run -Tests 'H1,H2,H4,H5' -EvidenceRoot "$tmp\evidence" -BaseUrl 'http://localhost:5017' -NonInteractive -ImportWaitSeconds 70 2>&1
    $code1 = $LASTEXITCODE; $ev1 = Latest-Evidence; $r1 = Get-Results $ev1
    $st = @{}; foreach ($r in $r1) { $st[$r.id] = $r.status }
    Check 'H1 (quan sát log script thật) = ĐẠT' ($st['H1'] -eq 'PASS') ($r1 | Where-Object id -eq 'H1' | ForEach-Object reason)
    Check 'H2 (sự kiện triển khai hiện trong /api/logs của backend thật, ≤ 60 s) = ĐẠT' ($st['H2'] -eq 'PASS') ($r1 | Where-Object id -eq 'H2' | ForEach-Object reason)
    Check 'H4 (mount chỉ-đọc, touch bị từ chối) = ĐẠT' ($st['H4'] -eq 'PASS') ($r1 | Where-Object id -eq 'H4' | ForEach-Object reason)
    Remove-Item Env:FAKE_DOCKER_HIDE_PLC
    Check 'H5 (stack không có sự kiện PLC) = CHƯA THỂ KIỂM TRA, không bịa kết quả' ($st['H5'] -eq 'BLOCKED') ($r1 | Where-Object id -eq 'H5' | ForEach-Object reason)
    Check 'exit code 2 khi còn bài CHƯA THỂ KIỂM TRA và không có bài KHÔNG ĐẠT' ($code1 -eq 2) "exit=$code1"
    Check 'có results.json, acceptance-report.md, commands-all.log, steps.log từng bài, _pre và _post' ((@('results.json', 'acceptance-report.md', 'commands-all.log', 'H1\steps.log', 'H4\commands.log', '_pre\evidence-index.md', '_post\container-status.md') | Where-Object { -not (Test-Path (Join-Path $ev1 $_)) }).Count -eq 0)
    $rep = Get-Content (Join-Path $ev1 'acceptance-report.md') -Raw -Encoding UTF8
    Check 'báo cáo có tiếng Việt đúng dấu và đủ mục (ĐẠT / CHƯA THỂ KIỂM TRA, Lệnh thực hiện, Bằng chứng)' ($rep -match 'ĐẠT' -and $rep -match 'CHƯA THỂ KIỂM TRA' -and $rep -match 'Lệnh thực hiện' -and $rep -match 'Bằng chứng')
    $allText = (Get-ChildItem $ev1 -Recurse -File | ForEach-Object { [System.IO.File]::ReadAllText($_.FullName) }) -join "`n"
    foreach ($s in @($secretPg, $adminPw, $adminPw1, $rolePw, 'THIS-MUST-NEVER-BE-COPIED')) { Check ("bí mật '{0}…' KHÔNG xuất hiện trong bất kỳ file bằng chứng nào" -f $s.Substring(0, 6)) (-not $allText.Contains($s)) }

    # --- B1b: H5 tren log that cua backend (Simulator) co "Connecting -> Connected" va IP -> DAT (bang chung thuc, khong can dong gia)
    $env:WCS_ACCEPT_ADMIN_PASSWORD = $adminPw; $null = & powershell -NoProfile -ExecutionPolicy Bypass -File $run -Tests 'H5' -EvidenceRoot "$tmp\evidence" -BaseUrl 'http://localhost:5017' -NonInteractive 2>&1
    $r1b = (Get-Results (Latest-Evidence)) | Where-Object id -eq 'H5'
    Check 'H5 = ĐẠT với log thật của backend (dòng PLC STATE "Connecting -> Connected" kèm IP)' ($r1b.status -eq 'PASS') $r1b.reason
    # --- B2: H5 PASS khi co dong STATE kem trang thai truoc->sau va IP; H4 KHONG DAT khi mount ghi duoc
    Set-Content "$tmp\extra.log" -Encoding utf8 -Value ('[{0} WRN] WCS.Infrastructure.Plc.PlcEventDispatcher: PLC PLC-01 STATE: PLC-01: Faulted - Mat ket noi PLC. (Connected -> Faulted; 192.168.10.101:5002; CONNECTION)' -f (Get-Date).ToString('yyyy-MM-dd HH:mm:ss.fff'))
    $env:FAKE_DOCKER_EXTRA_LOG = "$tmp\extra.log"; $env:FAKE_DOCKER_MOUNT_RW = 'true'
    $env:WCS_ACCEPT_ADMIN_PASSWORD = $adminPw; $null = & powershell -NoProfile -ExecutionPolicy Bypass -File $run -Tests 'H4,H5' -EvidenceRoot "$tmp\evidence" -BaseUrl 'http://localhost:5017' -NonInteractive 2>&1
    $code2 = $LASTEXITCODE; $r2 = Get-Results (Latest-Evidence); $s2 = @{}; foreach ($r in $r2) { $s2[$r.id] = $r.status }
    Check 'H4 = KHÔNG ĐẠT khi mount ghi được (và file thử bị bộ công cụ xóa lại)' ($s2['H4'] -eq 'FAIL' -and -not (Get-ChildItem "$tmp\logs" -Filter '.acceptance-write-test-*' -Force -ErrorAction SilentlyContinue)) ($r2 | Where-Object id -eq 'H4' | ForEach-Object reason)
    Check 'H5 = ĐẠT khi log có dòng PLC STATE (Connected -> Faulted) kèm IP:cổng' ($s2['H5'] -eq 'PASS') ($r2 | Where-Object id -eq 'H5' | ForEach-Object reason)
    Check 'exit code 1 khi có bài KHÔNG ĐẠT' ($code2 -eq 1) "exit=$code2"
    Remove-Item Env:FAKE_DOCKER_EXTRA_LOG; $env:FAKE_DOCKER_MOUNT_RW = 'false'

    # --- B3: cong xac nhan (khong tuong tac) -> khong thuc hien thao tac anh huong
    $env:WCS_ACCEPT_ADMIN_PASSWORD = $adminPw; $null = & powershell -NoProfile -ExecutionPolicy Bypass -File $run -Tests 'H3,H6,X2' -EvidenceRoot "$tmp\evidence" -BaseUrl 'http://localhost:5017' -NonInteractive 2>&1
    $r3 = Get-Results (Latest-Evidence); $s3 = @{}; foreach ($r in $r3) { $s3[$r.id] = $r }
    Check 'H3 / H6 / X2 không có xác nhận -> CHƯA THỂ KIỂM TRA và KHÔNG thực hiện gì' ($s3['H3'].status -eq 'BLOCKED' -and $s3['H6'].status -eq 'BLOCKED' -and $s3['X2'].status -eq 'BLOCKED')
    Check 'sau B3 PostgreSQL (role) vẫn đăng nhập được: chưa có gì bị dừng' ((Get-Item "$tmp\state\postgres.stopped" -ErrorAction SilentlyContinue) -eq $null -and (Invoke-WebRequest http://localhost:5017/healthz -UseBasicParsing).StatusCode -eq 200)
    $env:WCS_ACCEPT_ADMIN_PASSWORD = $adminPw; $null = & powershell -NoProfile -ExecutionPolicy Bypass -File $run -Tests 'H6' -ConfirmH6 -ConfirmPostgresContainer 'etek-wcs-WRONG-1' -EvidenceRoot "$tmp\evidence" -BaseUrl 'http://localhost:5017' -NonInteractive 2>&1
    $rw = Get-Results (Latest-Evidence)
    Check 'H6 với tên container sai -> không dừng PostgreSQL' (($rw | Where-Object id -eq 'H6').status -eq 'BLOCKED' -and -not (Test-Path "$tmp\state\postgres.stopped"))

    # --- B3b: H3 day du voi "repo" tam: start.ps1 la STUB dung _common.ps1 THAT (log, che bi mat, Save-FailureLogs) va that bai khi POSTGRES_PASSWORD sai
    New-Item -ItemType Directory -Force -Path "$tmp\repo" | Out-Null
    Copy-Item -Recurse -Force "$repo\deployment" "$tmp\repo\deployment"
    $stub = @'
param()
. (Join-Path $PSScriptRoot '_common.ps1')
Start-WcsRun 'start.ps1'
Write-Step 'Starting containers'
$e = Read-EnvFile
Write-Step 'Applying database migrations'
if ($e['POSTGRES_PASSWORD'] -like 'wrong-*') { Fail 'Migration failed: password authentication failed for user "wcs"' }
Write-Ok 'migrations applied'
Complete-WcsRun 0
exit 0
'@
    Set-Content "$tmp\repo\deployment\scripts\start.ps1" -Value $stub -Encoding ascii
    $envBefore = (Get-FileHash "$tmp\config\.env").Hash
    $env:WCS_ACCEPT_ADMIN_PASSWORD = $adminPw
    $null = & powershell -NoProfile -ExecutionPolicy Bypass -File $run -Tests 'H3' -RepoDir "$tmp\repo" -ConfirmH3 -ImportWaitSeconds 90 -EvidenceRoot "$tmp\evidence" -BaseUrl 'http://localhost:5017' -NonInteractive 2>&1
    $ev3 = Latest-Evidence; $h3 = (Get-Results $ev3) | Where-Object id -eq 'H3'
    Check 'H3 đầy đủ = ĐẠT (start.ps1 thất bại có kiểm soát, failure-*.log, sự kiện FAIL trên màn hình, đã khôi phục)' ($h3.status -eq 'PASS') "$($h3.status): $($h3.reason)"
    Check 'H3: file .env thật không bị sửa (cùng hash trước/sau)' ((Get-FileHash "$tmp\config\.env").Hash -eq $envBefore)
    Check 'H3: thư mục .env tạm đã được xóa và có failure-*.log' (-not (Test-Path (Join-Path $ev3 'H3\tmp-config')) -and (@(Get-ChildItem "$tmp\logs" -Filter 'failure-*' -File).Count -ge 1))
    $all3 = (Get-ChildItem $ev3 -Recurse -File | ForEach-Object { [System.IO.File]::ReadAllText($_.FullName) }) -join "`n"
    $logs3 = (Get-ChildItem "$tmp\logs" -File | ForEach-Object { [System.IO.File]::ReadAllText($_.FullName) }) -join "`n"
    Check 'H3: mật khẩu sai tạm thời và mật khẩu thật không lộ trong bằng chứng lẫn log triển khai' (-not ($all3 -match 'wrong-[0-9a-f]{12}') -and -not ($logs3 -match 'wrong-[0-9a-f]{12}') -and -not $all3.Contains($secretPg) -and -not $logs3.Contains($secretPg))
    Check 'H3: bắt đầu trong H3 có 2 lần chạy start.ps1 (lỗi rồi khôi phục) trong log triển khai' (([regex]::Matches((Get-ChildItem "$tmp\logs" -Filter 'deployment-events-*.jsonl' | ForEach-Object { Get-Content $_.FullName -Raw }) -join '', '"result":"START"').Count) -ge 3)

    # --- B4: H6 day du (dung postgres gia lap = khoa role tren DB tam), mat ket noi 40 s roi khoi phuc
    $t0 = Get-Date
    $env:WCS_ACCEPT_ADMIN_PASSWORD = $adminPw; $null = & powershell -NoProfile -ExecutionPolicy Bypass -File $run -Tests 'H6' -ConfirmH6 -ConfirmPostgresContainer 'etek-wcs-postgres-1' -OutageSeconds 40 -ImportWaitSeconds 90 -EvidenceRoot "$tmp\evidence" -BaseUrl 'http://localhost:5017' -NonInteractive 2>&1
    $code4 = $LASTEXITCODE; $ev4 = Latest-Evidence; $r4 = (Get-Results $ev4) | Where-Object id -eq 'H6'
    Check ("H6 đầy đủ = ĐẠT (1 DB_LOST, 1 DB_RESTORED, throttle, màn hình, khôi phục; {0:N0} s)" -f ((Get-Date) - $t0).TotalSeconds) ($r4.status -eq 'PASS' -and $code4 -eq 0) "$($r4.status): $($r4.reason)"
    Check 'sau H6 PostgreSQL đã được bật lại (không còn cờ dừng) và backend khỏe' (-not (Test-Path "$tmp\state\postgres.stopped") -and (Invoke-WebRequest http://localhost:5017/healthz -UseBasicParsing).StatusCode -eq 200)
    Check 'H6 có bằng chứng: mẫu healthz 503, log từ lúc dừng, JSON API DATABASE, container postgres' ((@('H6\healthz-samples.txt', 'H6\backend-logs-since-stop.txt', 'H6\api-database-events.json', 'H6\postgres-container.json') | Where-Object { -not (Test-Path (Join-Path $ev4 $_)) }).Count -eq 0)
    $s6 = Get-Content (Join-Path $ev4 'H6\healthz-samples.txt') -Raw -Encoding UTF8
    Check 'mẫu /healthz trong lúc mất DB có 503' ($s6 -match 'healthz=503')
    $all4 = (Get-ChildItem $ev4 -Recurse -File | ForEach-Object { [System.IO.File]::ReadAllText($_.FullName) }) -join "`n"
    Check 'H6: bằng chứng không chứa bí mật' (-not $all4.Contains($secretPg) -and -not $all4.Contains($adminPw) -and -not $all4.Contains($rolePw) -and -not $all4.Contains('THIS-MUST-NEVER-BE-COPIED'))

    # --- B5: collect-evidence doc lap
    $ce = Join-Path $tmp 'ce'
    $null = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $pkg 'collect-evidence.ps1') -OutDir $ce -Quiet 2>&1
    $cexit = $LASTEXITCODE
    Check 'collect-evidence.ps1 chạy độc lập: container-status.md (RestartCount, OOMKilled), mounts-backend.txt (RW=False), log, env-keys' ($cexit -eq 0 -and (Test-Path "$ce\container-status.md") -and ((Get-Content "$ce\mounts-backend.txt" -Raw -Encoding UTF8) -match '/deploy-logs\s+RW=False') -and ((Get-Content "$ce\container-status.md" -Raw -Encoding UTF8) -match 'RestartCount') -and (Test-Path "$ce\env-keys.txt") -and (Test-Path "$ce\logs-backend.txt"))
    $envKeys = Get-Content "$ce\env-keys.txt" -Raw -Encoding UTF8
    Check 'env-keys.txt chỉ có TÊN biến, không có mật khẩu' (-not $envKeys.Contains($secretPg) -and $envKeys -match 'POSTGRES_PASSWORD -> \(có giá trị, ẩn\)')
    # preflight voi docker gia lap
    $pf = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $pkg 'preflight.ps1') -OutFile "$tmp\preflight.md" 2>&1 | Out-String
    Check 'preflight.ps1 với docker giả lập: nhận Docker daemon, Compose, PostgreSQL container; không tạo thư mục bằng chứng' ($pf -match 'ĐẠT\s*\] Docker daemon' -and $pf -match 'ĐẠT\s*\] Docker Compose' -and $pf -match 'PostgreSQL thử nghiệm' -and (Test-Path "$tmp\preflight.md"))
}
finally {
    foreach ($v in 'FAKE_DOCKER_STATE', 'FAKE_DOCKER_LOGDIR', 'FAKE_DOCKER_BACKEND_OUT', 'FAKE_DOCKER_ROLE', 'FAKE_DOCKER_PGBIN', 'FAKE_DOCKER_PGPW', 'FAKE_DOCKER_MOUNT_RW', 'FAKE_DOCKER_EXTRA_LOG', 'WCS_ROOT_DIR', 'WCS_CONFIG_DIR', 'WCS_LOG_DIR', 'WCS_ACCEPT_ADMIN_PASSWORD') { Remove-Item "Env:$v" -ErrorAction SilentlyContinue }
    if ($apiProc) { $q = (Get-NetTCPConnection -LocalPort 5017 -State Listen -ErrorAction SilentlyContinue).OwningProcess | Select-Object -First 1; if ($q) { Stop-Process -Id $q -Force -ErrorAction SilentlyContinue }; try { Stop-Process -Id $apiProc.Id -Force -ErrorAction SilentlyContinue } catch { } }
    Start-Sleep 2
    Sql 'postgres' 'DROP DATABASE IF EXISTS wcs_acc WITH (FORCE)' | Out-Null; Sql 'postgres' 'DROP ROLE IF EXISTS wcs_acc_role' | Out-Null
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

$results | ForEach-Object { Write-Host $_ }
$failed = @($results | Where-Object { $_ -like 'FAIL*' }).Count
Write-Host ("{0} kiểm tra, {1} lỗi" -f $results.Count, $failed)
exit $(if ($failed -eq 0) { 0 } else { 1 })
