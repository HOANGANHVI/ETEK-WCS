# Shared helpers for the ETEK WCS deployment scripts (dot-sourced). ASCII only: Windows PowerShell 5.1 safe.
# Layout on the deployment machine (outside the repository, never in Git):
#   C:\ETEK-WCS\config\.env        production settings (copy of deployment\.env.example)
#   C:\ETEK-WCS\backups\           database / key-ring backups
# Override with the environment variables WCS_CONFIG_DIR and WCS_BACKUP_DIR.

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

function Write-Step([string]$m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Write-Ok([string]$m)   { Write-Host "OK  $m" -ForegroundColor Green }
function Write-Warn2([string]$m){ Write-Host "WARN $m" -ForegroundColor Yellow }
function Fail([string]$m)       { Write-Host "ERROR $m" -ForegroundColor Red; exit 1 }

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
    & docker compose --project-directory $script:DeployDir -f (Join-Path $script:DeployDir 'docker-compose.yml') --env-file $script:EnvFile -p $script:Project @ComposeArgs | Out-Host
    return $LASTEXITCODE
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
