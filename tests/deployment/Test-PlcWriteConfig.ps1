# CR-017: checks of deployment\scripts\plc-write-config.ps1 and of deployment\.env.example WITHOUT Docker, a database or a PLC. Run from the repository root:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tests\deployment\Test-PlcWriteConfig.ps1
# Uses a throw-away folder under %TEMP%; the real C:\ETEK-WCS folders are never touched. ASCII only (Windows PowerShell 5.1).
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$scripts = Join-Path $repo 'deployment\scripts'
$tmp = Join-Path $env:TEMP ('wcs-plcwrite-' + [guid]::NewGuid().ToString('N'))
$ref = 'REF-SECRET-123456'
$results = New-Object System.Collections.Generic.List[string]
function Check([string]$name, [bool]$ok, [string]$extra = '') { $results.Add($(if ($ok) { 'PASS ' } else { 'FAIL ' }) + $name + $(if (-not $ok -and $extra) { ' :: ' + $extra } else { '' })) }
function Row($rows, [string]$name) { return @($rows | Where-Object { $_.Setting -eq $name })[0] }

try {
    New-Item -ItemType Directory -Force -Path (Join-Path $tmp 'config') | Out-Null
    $env:WCS_ROOT_DIR = $tmp; $env:WCS_CONFIG_DIR = Join-Path $tmp 'config'; $env:WCS_LOG_DIR = Join-Path $tmp 'logs'; $env:WCS_BACKUP_DIR = Join-Path $tmp 'backups'
    . (Join-Path $scripts 'plc-write-config.ps1') -NoContainer      # functions only (dot-sourced)

    function EnvMap([string[]]$lines) { Set-Content -LiteralPath (Join-Path $tmp 'config\.env') -Encoding ascii -Value $lines; return (Read-EnvFile) }
    $all = @('POSTGRES_PASSWORD=x', 'PLC_ALLOW_LIVE_CONNECTION=true', 'PLC_WRITE_ENABLED=true', "PLC_WRITE_APPROVAL_REFERENCE=$ref", 'PLC_WRITE_HANDSHAKE_APPROVED=true')

    # 1) the file operators copy: it must not force read-only and must not enable anything
    $example = Get-Content -LiteralPath (Join-Path $repo 'deployment\.env.example')
    Check '.env.example does not force PLC_MODE=RealReadOnly' (-not ($example | Where-Object { $_ -match '^\s*PLC_MODE\s*=\s*RealReadOnly' })) 'a copy of the example would keep the runtime read-only'
    Check '.env.example sets PLC_MODE=RealReadWrite' ([bool]($example | Where-Object { $_ -match '^\s*PLC_MODE\s*=\s*RealReadWrite\s*$' }))
    $m = EnvMap $example
    $rows = Get-PlcWriteConfigReport $m $null
    Check 'a copy of .env.example: the mode is fine' ((Row $rows 'PLC_MODE').Met)
    Check 'a copy of .env.example enables NOTHING (live, write, handshake all off, no reference)' (-not (Row $rows 'PLC_ALLOW_LIVE_CONNECTION').Met -and -not (Row $rows 'PLC_WRITE_ENABLED').Met -and -not (Row $rows 'PLC_WRITE_HANDSHAKE_APPROVED').Met -and -not (Row $rows 'PLC_WRITE_APPROVAL_REFERENCE').Met)
    Check '.env.example lists the three write keys (so the operator sees them)' ((@('PLC_WRITE_ENABLED', 'PLC_WRITE_APPROVAL_REFERENCE', 'PLC_WRITE_HANDSHAKE_APPROVED') | Where-Object { $m.ContainsKey($_) }).Count -eq 3)

    # 2) every key set by the operator
    $rows = Get-PlcWriteConfigReport (EnvMap $all) $null
    Check 'all keys set: every setting is met' (@($rows | Where-Object { -not $_.Met }).Count -eq 0)

    # 3) each key missing alone
    foreach ($drop in 'PLC_ALLOW_LIVE_CONNECTION', 'PLC_WRITE_ENABLED', 'PLC_WRITE_APPROVAL_REFERENCE', 'PLC_WRITE_HANDSHAKE_APPROVED') {
        $r = Get-PlcWriteConfigReport (EnvMap ($all | Where-Object { $_ -notlike "$drop=*" })) $null
        $missing = @($r | Where-Object { -not $_.Met } | ForEach-Object { $_.Setting })
        Check "missing only $drop" ($missing.Count -eq 1 -and $missing[0] -eq $drop) ($missing -join ',')
    }

    # 4) the old .env: PLC_MODE=RealReadOnly forces read-only whatever the keys say, and the report says so
    $r = Get-PlcWriteConfigReport (EnvMap ($all + 'PLC_MODE=RealReadOnly')) $null
    Check 'PLC_MODE=RealReadOnly is reported as the blocker' ((-not (Row $r 'PLC_MODE').Met) -and (Row $r 'PLC_MODE').Note -like '*forces read-only*')

    # 5) .env is right but the running container was created before the change
    $stale = @{ 'Plc__Mode' = 'RealReadOnly'; 'Plc__AllowLiveConnection' = 'false'; 'Plc__Write__Enabled' = 'false'; 'Plc__Write__EngineerApprovalReference' = ''; 'Plc__Write__HandshakeApproved' = 'false' }
    $r = Get-PlcWriteConfigReport (EnvMap $all) $stale
    Check 'container not recreated: every setting is judged by the CONTAINER and the report says so' ((@($r | Where-Object { -not $_.Met }).Count -ge 4) -and (Row $r 'PLC_WRITE_ENABLED').Note -like '*NOT recreated*')
    $fresh = @{ 'Plc__Mode' = 'RealReadWrite'; 'Plc__AllowLiveConnection' = 'true'; 'Plc__Write__Enabled' = 'True'; 'Plc__Write__EngineerApprovalReference' = $ref; 'Plc__Write__HandshakeApproved' = 'true' }
    $r = Get-PlcWriteConfigReport (EnvMap $all) $fresh
    Check 'container recreated with every key: all met, no difference note (True == true)' (@($r | Where-Object { -not $_.Met -or $_.Note }).Count -eq 0)

    # 6) the approval reference is never printed
    $text = (($r | Out-String) + (Get-PlcWriteConfigReport (EnvMap $all) $null | Out-String) + (Get-PlcWriteConfigReport (EnvMap $all) $stale | Out-String))
    Check 'the approval reference value appears nowhere in the report' ($text -notmatch [regex]::Escape($ref)) 'secret leaked'
    Check 'the reference is shown only as <set> / <unset>' (($text -match '<set>') -and ($text -match '<unset>'))

    # 7) the .env check of the deployment scripts warns about a forced read-only mode
    Set-Content -LiteralPath (Join-Path $tmp 'config\.env') -Encoding ascii -Value @('POSTGRES_PASSWORD=x', 'PLC_MODE=RealReadOnly')
    $out = & powershell -NoProfile -ExecutionPolicy Bypass -Command ". '$scripts\_common.ps1'; Start-WcsRun 'probe.ps1'; Assert-EnvFile | Out-Null" 2>&1 | Out-String
    Check 'Assert-EnvFile warns when .env forces PLC_MODE=RealReadOnly' ($out -match 'PLC_MODE=RealReadOnly in .env') $out
}
finally {
    Remove-Item Env:WCS_ROOT_DIR, Env:WCS_CONFIG_DIR, Env:WCS_BACKUP_DIR -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force -LiteralPath $tmp -ErrorAction SilentlyContinue
}
$results | ForEach-Object { Write-Host $_ }
$failed = @($results | Where-Object { $_ -like 'FAIL*' }).Count
Write-Host ("{0} checks, {1} failed" -f $results.Count, $failed)
exit $(if ($failed -gt 0) { 1 } else { 0 })
