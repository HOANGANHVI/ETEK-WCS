# Shared helpers for the ETEK WCS deployment scripts (dot-sourced). ASCII only: Windows PowerShell 5.1 safe.
# Layout on the deployment machine (outside the repository, never in Git):
#   C:\ETEK-WCS\config\.env        production settings (copy of deployment\.env.example)
#   C:\ETEK-WCS\backups\           database / key-ring backups
#   C:\ETEK-WCS\logs\              deployment logs (see "deployment logging" below)
# Override with the environment variables WCS_CONFIG_DIR, WCS_BACKUP_DIR and WCS_LOG_DIR.

$ErrorActionPreference = 'Stop'

$script:DeployDir = Split-Path -Parent $PSScriptRoot                 # <repo>\deployment
$script:RepoDir   = Split-Path -Parent $script:DeployDir             # <repo>
$script:Root      = if ($env:WCS_ROOT_DIR) { $env:WCS_ROOT_DIR } else { 'C:\ETEK-WCS' }
$script:ConfigDir = if ($env:WCS_CONFIG_DIR) { $env:WCS_CONFIG_DIR } else { Join-Path $script:Root 'config' }
$script:BackupDir = if ($env:WCS_BACKUP_DIR) { $env:WCS_BACKUP_DIR } else { Join-Path $script:Root 'backups' }
$script:EnvFile   = Join-Path $script:ConfigDir '.env'
$script:Project   = 'etek-wcs'
$script:PgVolume  = 'etek_wcs_pgdata'
$script:KeyVolume = 'etek_wcs_keys'

# ------------------------------------------------------------------------------------------------------------------ deployment logging (CR-007)
# Every script run is recorded OUTSIDE the repository, in a folder that the backend container mounts read-only:
#   C:\ETEK-WCS\logs\deploy-YYYYMMDD.log             human readable text log (one line per event + the output of docker compose commands)
#   C:\ETEK-WCS\logs\deployment-events-YYYYMMDD.jsonl one JSON object per line; the backend imports these into "Logging & Diagnostics" (category DEPLOYMENT)
#   C:\ETEK-WCS\logs\failure-<run>-*.log              container logs / last command output saved automatically when a script fails
# Rules: secrets never reach a log (known .env secret values and password/token/cookie patterns are masked); logging never stops or fails a deployment;
# nothing is deleted automatically (see prune-logs.ps1, manual and dry-run by default).
$script:LogDir     = if ($env:WCS_LOG_DIR) { $env:WCS_LOG_DIR } else { Join-Path $script:Root 'logs' }
$env:WCS_LOG_DIR   = $script:LogDir.Replace('\', '/')              # docker-compose.yml bind-mounts this folder read-only into the backend
$script:LogReady   = $false
$script:RunId      = ''
$script:RunName    = ''
$script:Commit     = ''
$script:Seq        = 0
$script:CurrentStep = ''
$script:LastOutput = @()
$script:SecretCache = $null
$script:Utf8NoBom  = New-Object System.Text.UTF8Encoding($false)

function Get-SecretValues {
    # literal secret values of the machine .env (never logged, only used to mask them wherever they might appear)
    if ($null -ne $script:SecretCache) { return $script:SecretCache }
    $vals = @()
    try {
        if (Test-Path -LiteralPath $script:EnvFile) {
            foreach ($line in Get-Content -LiteralPath $script:EnvFile -ErrorAction Stop) {
                $t = $line.Trim()
                if ($t -eq '' -or $t.StartsWith('#')) { continue }
                $i = $t.IndexOf('=')
                if ($i -lt 1) { continue }
                $k = $t.Substring(0, $i).Trim(); $v = $t.Substring($i + 1).Trim()
                if ($k -match '(?i)PASSWORD|SECRET|TOKEN|KEY' -and $v.Length -ge 4) { $vals += $v }
            }
        }
    } catch { }
    $script:SecretCache = $vals
    return $vals
}

function Hide-Secrets([string]$text) {
    if ([string]::IsNullOrEmpty($text)) { return $text }
    try {
        $t = $text
        foreach ($s in (Get-SecretValues)) { $t = $t.Replace($s, '***') }
        $t = [regex]::Replace($t, '(?i)\b(bearer|basic)\s+[A-Za-z0-9\-\._~\+/]{6,}=*', '$1 ***')
        $t = [regex]::Replace($t, '(?i)\b(password|passwd|pwd|secret|token|api[_-]?key|authorization|cookie|connection[_-]?string|private[_-]?key|postgres_password|bootstrap_admin_password)\b(["'']?\s*[=:]\s*)("[^"]*"|''[^'']*''|[^\s;,&"'']+)', '$1$2***')
        return $t
    } catch { return '***' }
}

function Initialize-WcsLog {
    if ($script:LogReady) { return }
    try {
        New-Item -ItemType Directory -Force -Path $script:LogDir -ErrorAction Stop | Out-Null
        if (-not (Test-Path -LiteralPath $script:LogDir -PathType Container)) { throw 'the log path is not a folder' }
        $probe = Join-Path $script:LogDir ('.write-test-' + [guid]::NewGuid().ToString('N'))      # prove the folder is writable before relying on it
        [System.IO.File]::WriteAllText($probe, '')
        Remove-Item -LiteralPath $probe -Force -ErrorAction SilentlyContinue
        $script:LogReady = $true
    } catch {
        $script:LogReady = $false
        Write-Host "WARN Log folder $($script:LogDir) cannot be created; logging to file is disabled for this run ($($_.Exception.Message))." -ForegroundColor Yellow
    }
}

function Write-WcsLog {
    # never throws, never changes the exit code of the calling script
    param([string]$Step = '', [string]$Result = 'INFO', [string]$Message = '', $ExitCode = $null, [string]$Level = '')
    try {
        Initialize-WcsLog
        if (-not $script:LogReady) { return }
        $script:Seq++
        $now = Get-Date
        $stamp = $now.ToString('yyyyMMdd')
        $msg = Hide-Secrets $Message
        $stepText = Hide-Secrets $Step
        if (-not $Level) { $Level = switch ($Result) { 'FAIL' { 'Error' } 'WARN' { 'Warning' } default { 'Information' } } }
        $name = if ($script:RunName) { $script:RunName } else { 'script' }
        $line = '{0} | {1} | run={2} | {3} | {4} | exit={5} | {6}' -f $now.ToString('o'), $name, $script:RunId, $Result, $stepText, $(if ($null -eq $ExitCode) { '-' } else { $ExitCode }), $msg
        [System.IO.File]::AppendAllText((Join-Path $script:LogDir "deploy-$stamp.log"), $line + "`r`n", $script:Utf8NoBom)

        $evt = [ordered]@{
            id = ('{0}-{1}' -f $script:RunId, $script:Seq); runId = $script:RunId; time = $now.ToString('o'); script = $name; step = $stepText; result = $Result; level = $Level
            exitCode = $ExitCode; message = $msg; host = $env:COMPUTERNAME; commit = $script:Commit
        }
        [System.IO.File]::AppendAllText((Join-Path $script:LogDir "deployment-events-$stamp.jsonl"), ($evt | ConvertTo-Json -Compress) + "`n", $script:Utf8NoBom)
    } catch { }
}

function Write-WcsRaw([string[]]$lines) {
    try {
        Initialize-WcsLog
        if (-not $script:LogReady -or -not $lines) { return }
        $path = Join-Path $script:LogDir ("deploy-{0}.log" -f (Get-Date).ToString('yyyyMMdd'))
        [System.IO.File]::AppendAllText($path, (($lines | ForEach-Object { '    ' + (Hide-Secrets $_) }) -join "`r`n") + "`r`n", $script:Utf8NoBom)
    } catch { }
}

function Start-WcsRun([string]$Name) {
    $script:RunName = $Name
    $script:RunId = (Get-Date).ToString('yyyyMMddHHmmss') + '-' + ([guid]::NewGuid().ToString('N').Substring(0, 4))
    $script:Seq = 0
    $script:Commit = ''
    try { if (Get-Command git -ErrorAction SilentlyContinue) { $script:Commit = ((& git -C $script:RepoDir rev-parse --short HEAD) 2>$null | Select-Object -First 1) } } catch { }
    Write-WcsLog -Step 'run' -Result 'START' -Message ("{0} started by {1}" -f $Name, $env:USERNAME)
}

function Complete-WcsRun([int]$ExitCode = 0) {
    Write-WcsLog -Step 'run' -Result 'END' -Message ("{0} finished" -f $script:RunName) -ExitCode $ExitCode -Level $(if ($ExitCode -eq 0) { 'Information' } else { 'Error' })
}

function Save-FailureLogs {
    # keeps what is needed to diagnose a failed run: the last command output and the container logs (masked)
    try {
        Initialize-WcsLog
        if (-not $script:LogReady) { return }
        $prefix = Join-Path $script:LogDir ("failure-{0}" -f $script:RunId)
        if ($script:LastOutput -and $script:LastOutput.Count -gt 0) {
            [System.IO.File]::WriteAllText("$prefix-last-command.log", ((($script:LastOutput | Select-Object -Last 300) | ForEach-Object { Hide-Secrets $_ }) -join "`r`n"), $script:Utf8NoBom)
        }
        if ((Get-Command docker -ErrorAction SilentlyContinue) -and (Test-Path -LiteralPath $script:EnvFile)) {
            foreach ($svc in 'migrator', 'backend', 'postgres', 'frontend') {
                $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
                try {
                    $out = & docker compose --project-directory $script:DeployDir -f (Join-Path $script:DeployDir 'docker-compose.yml') --env-file $script:EnvFile -p $script:Project logs --no-color --tail 300 $svc 2>&1 | ForEach-Object { "$_" }
                    if ($out) { [System.IO.File]::WriteAllText("$prefix-$svc.log", (($out | ForEach-Object { Hide-Secrets $_ }) -join "`r`n"), $script:Utf8NoBom) }
                } finally { $ErrorActionPreference = $prev }
            }
        }
        Write-WcsLog -Step $script:CurrentStep -Result 'INFO' -Message ("diagnostic logs saved: {0}-*.log" -f $prefix)
    } catch { }
}

function Write-Step([string]$m) { $script:CurrentStep = $m; Write-Host "==> $m" -ForegroundColor Cyan; Write-WcsLog -Step $m -Result 'STEP' -Message $m }
function Write-Ok([string]$m)   { Write-Host "OK  $m" -ForegroundColor Green; Write-WcsLog -Step $script:CurrentStep -Result 'OK' -Message $m }
function Write-Warn2([string]$m){ Write-Host "WARN $m" -ForegroundColor Yellow; Write-WcsLog -Step $script:CurrentStep -Result 'WARN' -Message $m }
function Fail([string]$m) {
    Write-Host "ERROR $m" -ForegroundColor Red
    Write-WcsLog -Step $script:CurrentStep -Result 'FAIL' -Message $m -ExitCode 1
    Save-FailureLogs
    Complete-WcsRun 1
    exit 1
}

function Assert-Docker {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { Fail 'Docker is not installed or not in PATH. Install Docker Desktop (WSL2 backend).' }
    & docker info *> $null
    if ($LASTEXITCODE -ne 0) { Fail 'Docker engine is not running. Start Docker Desktop and wait until it says "Engine running".' }
    & docker compose version *> $null
    if ($LASTEXITCODE -ne 0) { Fail 'Docker Compose v2 is not available ("docker compose"). Update Docker Desktop.' }
}

function Assert-Git {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) { Fail 'Git is not installed or not in PATH.' }
}

function Read-EnvFile {
    if (-not (Test-Path $script:EnvFile)) {
        Fail "Environment file not found: $($script:EnvFile). Copy deployment\.env.example to that path and fill it in (see deployment\README.md)."
    }
    $map = @{}
    foreach ($line in Get-Content -LiteralPath $script:EnvFile) {
        $t = $line.Trim()
        if ($t -eq '' -or $t.StartsWith('#')) { continue }
        $i = $t.IndexOf('=')
        if ($i -lt 1) { continue }
        $map[$t.Substring(0, $i).Trim()] = $t.Substring($i + 1).Trim()
    }
    return $map
}

function Assert-EnvFile {
    $e = Read-EnvFile
    if (-not $e['POSTGRES_PASSWORD']) { Fail "POSTGRES_PASSWORD is empty in $($script:EnvFile)." }
    if ($e['WCS_COOKIE_SECURE'] -and $e['WCS_COOKIE_SECURE'] -notin @('Always', 'SameAsRequest')) { Fail 'WCS_COOKIE_SECURE must be Always or SameAsRequest.' }
    if ($e['PLC_MODE'] -eq 'Simulator' -and $e['PLC_ALLOW_SIMULATOR_IN_PRODUCTION'] -ne 'true') {
        Fail 'PLC_MODE=Simulator is refused in Production. Use PLC_MODE=RealReadOnly, or (deliberate, supervised, no PLC) PLC_ALLOW_SIMULATOR_IN_PRODUCTION=true.'
    }
    if ($e['PLC_MODE'] -eq 'RealReadWrite') { Write-Warn2 'PLC_MODE=RealReadWrite requested: writes still need the separate engineer-approval keys. Make sure this was approved.' }
    return $e
}

# docker compose with the machine-specific env file; never uses -v / down -v
function Invoke-Compose {
    param([Parameter(Mandatory = $true)][string[]]$ComposeArgs)
    $lines = New-Object System.Collections.Generic.List[string]
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'   # docker writes progress to stderr: that must not become a terminating error
    try {
        & docker compose --project-directory $script:DeployDir -f (Join-Path $script:DeployDir 'docker-compose.yml') --env-file $script:EnvFile -p $script:Project @ComposeArgs 2>&1 |
            ForEach-Object { $l = Hide-Secrets ("$_"); $lines.Add($l); Write-Host $l }
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $prev }
    $script:LastOutput = @($lines | Select-Object -Last 300)
    Write-WcsRaw ($script:LastOutput | Select-Object -Last 40)
    Write-WcsLog -Step $script:CurrentStep -Result $(if ($code -eq 0) { 'OK' } else { 'FAIL' }) -Message ('docker compose ' + ($ComposeArgs -join ' ')) -ExitCode $code
    return $code
}

function Get-HttpPort {
    $e = Read-EnvFile
    if ($e['WCS_HTTP_PORT']) { return [int]$e['WCS_HTTP_PORT'] } else { return 80 }
}

function Test-VolumeExists([string]$name) {
    & docker volume inspect $name *> $null
    return ($LASTEXITCODE -eq 0)
}

function Get-ContainerId([string]$service) {
    $id = & docker compose --project-directory $script:DeployDir -f (Join-Path $script:DeployDir 'docker-compose.yml') --env-file $script:EnvFile -p $script:Project ps -q $service
    return ($id | Select-Object -First 1)
}

function Get-ContainerHealth([string]$service) {
    $id = Get-ContainerId $service
    if (-not $id) { return 'not created' }
    $h = & docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' $id
    return $h
}

function Wait-Healthy {
    param([string[]]$Services = @('postgres', 'backend', 'frontend'), [int]$TimeoutSec = 240)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $states = @{}
        foreach ($s in $Services) { $states[$s] = Get-ContainerHealth $s }
        if (-not ($states.Values | Where-Object { $_ -ne 'healthy' })) { return $true }
        Start-Sleep -Seconds 5
    } while ((Get-Date) -lt $deadline)
    foreach ($k in $states.Keys) { Write-Host ("   {0}: {1}" -f $k, $states[$k]) }
    return $false
}

function Get-HealthzReport {
    $port = Get-HttpPort
    try {
        $r = Invoke-WebRequest -UseBasicParsing -Uri "http://localhost:$port/healthz" -TimeoutSec 10
        return "HTTP $($r.StatusCode) $($r.Content)"
    } catch {
        $resp = $_.Exception.Response
        if ($resp) { return "HTTP $([int]$resp.StatusCode)" }
        return "unreachable: $($_.Exception.Message)"
    }
}
