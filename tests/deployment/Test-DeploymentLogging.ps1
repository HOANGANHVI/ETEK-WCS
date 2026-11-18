# CR-007: checks of the deployment logging helpers and scripts WITHOUT Docker, a database or a PLC. Run from the repository root:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tests\deployment\Test-DeploymentLogging.ps1
# Uses a throw-away folder under %TEMP%; the real C:\ETEK-WCS folders are never touched. ASCII only (Windows PowerShell 5.1).
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$scripts = Join-Path $repo 'deployment\scripts'
$tmp = Join-Path $env:TEMP ('wcs-logtest-' + [guid]::NewGuid().ToString('N'))
$secret = 'Sup3rS3cretValueXYZ'
$results = New-Object System.Collections.Generic.List[string]
function Check([string]$name, [bool]$ok, [string]$extra = '') { $results.Add($(if ($ok) { 'PASS ' } else { 'FAIL ' }) + $name + $(if (-not $ok -and $extra) { ' :: ' + $extra } else { '' })) }

try {
    New-Item -ItemType Directory -Force -Path (Join-Path $tmp 'config') | Out-Null
    Set-Content -LiteralPath (Join-Path $tmp 'config\.env') -Encoding ascii -Value @("POSTGRES_PASSWORD=$secret", 'WCS_ADMIN_BOOTSTRAP_PASSWORD=AnotherSecret9876', 'POSTGRES_DB=wcs_db')
    $env:WCS_ROOT_DIR = $tmp; $env:WCS_CONFIG_DIR = Join-Path $tmp 'config'; $env:WCS_LOG_DIR = Join-Path $tmp 'logs'; $env:WCS_BACKUP_DIR = Join-Path $tmp 'backups'
    $logs = Join-Path $tmp 'logs'

    # ---- 1) helper functions: step/ok/warn/fail events, masking, file formats
    $probe = @"
`$ErrorActionPreference = 'Stop'
. '$scripts\_common.ps1'
Start-WcsRun 'probe.ps1'
Write-Step 'connect with password=abc12345 and token=XYZ-token-value'
Write-Ok 'value $secret was accepted; Authorization: Bearer abcdefghijklmnop'
Write-Warn2 'ConnectionString = Host=db;Password=InlinePw99;Port=5432'
Write-WcsLog -Step 'custom' -Result 'INFO' -Message 'plain message' -ExitCode 0
Fail 'something went wrong with AnotherSecret9876'
"@
    $probeFile = Join-Path $tmp 'probe.ps1'; Set-Content -LiteralPath $probeFile -Value $probe -Encoding ascii
    & powershell -NoProfile -ExecutionPolicy Bypass -File $probeFile *> $null
    Check 'Fail exits with code 1' ($LASTEXITCODE -eq 1) "exit=$LASTEXITCODE"

    $textLogs = @(Get-ChildItem -LiteralPath $logs -Filter 'deploy-*.log' -ErrorAction SilentlyContinue)
    $jsonLogs = @(Get-ChildItem -LiteralPath $logs -Filter 'deployment-events-*.jsonl' -ErrorAction SilentlyContinue)
    Check 'text log and JSONL event file are created outside the repository' ($textLogs.Count -eq 1 -and $jsonLogs.Count -eq 1 -and -not $logs.StartsWith($repo))
    $lines = @(Get-Content -LiteralPath $jsonLogs[0].FullName)
    $events = @($lines | ForEach-Object { $_ | ConvertFrom-Json })
    Check 'every JSONL line is valid JSON with id/time/script/step/result/level/message/host' (@($events | Where-Object { $_.id -and $_.time -and $_.script -and $_.result -and $_.level -and $null -ne $_.message -and $_.host }).Count -eq $events.Count -and $events.Count -ge 7) "events=$($events.Count)"
    Check 'event ids are unique (idempotent import)' (@($events.id | Select-Object -Unique).Count -eq $events.Count)
    Check 'run START, STEP, OK, WARN, FAIL and END (exit code 1) are recorded' (($events.result -contains 'START') -and ($events.result -contains 'STEP') -and ($events.result -contains 'OK') -and ($events.result -contains 'WARN') -and ($events.result -contains 'FAIL') -and (@($events | Where-Object { $_.result -eq 'END' -and $_.exitCode -eq 1 }).Count -eq 1))
    Check 'levels: FAIL=Error, WARN=Warning, OK=Information' ((@($events | Where-Object { $_.result -eq 'FAIL' })[0].level -eq 'Error') -and (@($events | Where-Object { $_.result -eq 'WARN' })[0].level -eq 'Warning') -and (@($events | Where-Object { $_.result -eq 'OK' })[0].level -eq 'Information'))
    $all = ((Get-ChildItem -LiteralPath $logs -File | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n")
    foreach ($s in @($secret, 'AnotherSecret9876', 'abc12345', 'XYZ-token-value', 'abcdefghijklmnop', 'InlinePw99')) { Check "secret '$($s.Substring(0, 4))...' never appears in any log file" (-not $all.Contains($s)) }
    Check 'masked placeholder is present' ($all.Contains('***'))
    Check '.env values are not copied into the logs (only variable-free messages)' (-not $all.Contains('POSTGRES_DB=wcs_db'))

    # ---- 2) real scripts on a machine without Docker: the failure is logged, exit code is preserved
    $logs2 = Join-Path $tmp 'logs2'; $env:WCS_LOG_DIR = $logs2
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $scripts 'stop.ps1') *> $null
    $stopExit = $LASTEXITCODE
    $ev2 = @(Get-ChildItem -LiteralPath $logs2 -Filter 'deployment-events-*.jsonl' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content -LiteralPath $_.FullName | ForEach-Object { $_ | ConvertFrom-Json } })
    $dockerPresent = [bool](Get-Command docker -ErrorAction SilentlyContinue)
    if (-not $dockerPresent) {
        Check 'stop.ps1 without Docker: exit code 1 and the failure is logged' ($stopExit -eq 1 -and @($ev2 | Where-Object { $_.script -eq 'stop.ps1' -and $_.result -eq 'FAIL' -and $_.message -match 'Docker' }).Count -ge 1) "exit=$stopExit"
    } else { $results.Add('SKIP stop.ps1 failure path (Docker is installed on this machine)') }
    $names = @('deploy', 'start', 'stop', 'backup', 'restore', 'status', 'plc-route-test', 'prune-logs')
    $missing = @($names | Where-Object { -not ((Get-Content -LiteralPath (Join-Path $scripts "$_.ps1") -Raw) -match ("Start-WcsRun '" + $_ + "\.ps1'")) })
    Check 'every operational script starts and completes a logged run' ($missing.Count -eq 0) ($missing -join ',')
    $noEnd = @($names | Where-Object { -not ((Get-Content -LiteralPath (Join-Path $scripts "$_.ps1") -Raw) -match 'Complete-WcsRun') })
    Check 'every operational script records its end and exit code' ($noEnd.Count -eq 0) ($noEnd -join ',')

    # ---- 3) a log folder that cannot be created never breaks the script
    $blocker = Join-Path $tmp 'blocker'; Set-Content -LiteralPath $blocker -Value 'file, not a folder'
    $env:WCS_LOG_DIR = Join-Path $blocker 'logs'
    $out = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $scripts 'stop.ps1') 2>&1 | Out-String
    $exit3 = $LASTEXITCODE
    if (-not $dockerPresent) { Check 'unwritable log folder: warning printed, script still ends with its own exit code (1)' ($exit3 -eq 1 -and $out -match 'logging to file is disabled') "exit=$exit3" }

    # ---- 4) prune-logs is a dry run unless -Execute and only lists deployment logs
    $env:WCS_LOG_DIR = $logs
    $old = Join-Path $logs 'deploy-20200101.log'; Set-Content -LiteralPath $old -Value 'old'; (Get-Item $old).LastWriteTime = (Get-Date).AddDays(-400)
    $keep = Join-Path $logs 'notes.txt'; Set-Content -LiteralPath $keep -Value 'not a deployment log'; (Get-Item $keep).LastWriteTime = (Get-Date).AddDays(-400)
    $dry = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $scripts 'prune-logs.ps1') -OlderThanDays 30 2>&1 | Out-String
    Check 'prune-logs dry run lists the old deployment log and deletes nothing' ($dry -match 'deploy-20200101.log' -and $dry -match 'Dry run' -and (Test-Path -LiteralPath $old))
    Check 'prune-logs never lists unrelated files' ($dry -notmatch 'notes.txt' -and (Test-Path -LiteralPath $keep))
}
finally {
    foreach ($v in 'WCS_ROOT_DIR', 'WCS_CONFIG_DIR', 'WCS_LOG_DIR', 'WCS_BACKUP_DIR') { Remove-Item "Env:$v" -ErrorAction SilentlyContinue }
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

$results | ForEach-Object { Write-Host $_ }
$failed = @($results | Where-Object { $_ -like 'FAIL*' }).Count
Write-Host ("{0} checks, {1} failed" -f $results.Count, $failed)
exit $(if ($failed -eq 0) { 0 } else { 1 })
