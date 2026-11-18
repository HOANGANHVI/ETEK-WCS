# CR-017: shows whether the PLC WRITE settings really reach the backend. Read-only: it changes nothing, sends nothing to a PLC.
# For each setting it compares  (1) the .env file the deployment scripts use  (2) what docker-compose.yml resolves from it  (3) what the RUNNING backend container was created with.
# CR-019: only three settings remain (the approval reference and handshake approval keys were removed; old lines in .env are ignored). ASCII only (Windows PowerShell 5.1).
#   deployment\scripts\plc-write-config.ps1            report (needs Docker for the container column)
#   deployment\scripts\plc-write-config.ps1 -NoContainer   only .env + compose defaults (no Docker needed)
param([switch]$NoContainer)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')

# EnvName = name in .env; Option = name the backend container receives (ASP.NET: "__" = ":"); Default = docker-compose.yml default; Need = what a write requires.
$script:WriteKeys = @(
    @{ EnvName = 'PLC_MODE';                      Option = 'Plc__Mode';                                  Default = 'RealReadWrite'; Need = 'RealReadWrite'; Secret = $false },
    @{ EnvName = 'PLC_ALLOW_LIVE_CONNECTION';     Option = 'Plc__AllowLiveConnection';                   Default = 'false';         Need = 'true';          Secret = $false },
    @{ EnvName = 'PLC_WRITE_ENABLED';             Option = 'Plc__Write__Enabled';                        Default = 'false';         Need = 'true';          Secret = $false }
)

# What docker-compose resolves for one key: a value that is set AND non-empty wins over the default (${NAME:-default}).
function Get-ResolvedValue($key, [hashtable]$dotEnv) {
    if ($dotEnv.ContainsKey($key.EnvName) -and "$($dotEnv[$key.EnvName])".Trim('"', "'").Length -gt 0) { return "$($dotEnv[$key.EnvName])".Trim('"', "'") }
    return $key.Default
}

function Show-Value($key, [string]$value) {
    if ($key.Secret) { if ($value) { return '<set>' } else { return '<unset>' } }
    if ($value -eq '') { return '<empty>' }
    return $value
}

# Pure: builds the report rows. $containerEnv = hashtable Option -> value of the running container, or $null when it could not be read.
function Get-PlcWriteConfigReport([hashtable]$dotEnv, $containerEnv) {
    $rows = @()
    foreach ($k in $script:WriteKeys) {
        $resolved = Get-ResolvedValue $k $dotEnv
        $inContainer = $null
        if ($null -ne $containerEnv) { $inContainer = if ($containerEnv.ContainsKey($k.Option)) { "$($containerEnv[$k.Option])" } else { '' } }
        $effective = if ($null -ne $inContainer) { $inContainer } else { $resolved }          # what the backend has (or will have)
        $met = if ($k.Secret) { $effective.Length -gt 0 } else { $effective.Trim().ToLowerInvariant() -eq $k.Need.ToLowerInvariant() }
        $note = ''
        if ($null -ne $inContainer -and $inContainer -ne $resolved) { $note = 'container differs from .env: the container was NOT recreated after the .env change (run deploy.ps1 or start.ps1)' }
        elseif (-not $met -and $k.EnvName -eq 'PLC_MODE' -and $effective -eq 'RealReadOnly') { $note = 'PLC_MODE=RealReadOnly forces read-only: set RealReadWrite (or remove the line)' }
        elseif (-not $met) { $note = 'not set for writing' }
        $rows += [pscustomobject]@{
            Setting = $k.EnvName; DotEnv = (Show-Value $k $(if ($dotEnv.ContainsKey($k.EnvName)) { "$($dotEnv[$k.EnvName])".Trim('"', "'") } else { '' }))
            Compose = (Show-Value $k $resolved); Container = $(if ($null -eq $inContainer) { '(not read)' } else { Show-Value $k $inContainer })
            Met = $met; Note = $note
        }
    }
    return $rows
}

function Read-ContainerPlcEnvironment {
    $id = Get-ContainerId 'backend'
    if (-not $id) { return $null }
    $json = & docker inspect --format '{{json .Config.Env}}' $id
    $map = @{}
    foreach ($e in ($json | ConvertFrom-Json)) { $i = "$e".IndexOf('='); if ($i -gt 0) { $map["$e".Substring(0, $i)] = "$e".Substring($i + 1) } }
    return $map
}

if ($MyInvocation.InvocationName -eq '.') { return }          # dot-sourced by the self-test: functions only

Start-WcsRun 'plc-write-config.ps1'
Write-Host ("Environment file used by the deployment scripts: {0}" -f $script:EnvFile)
$dotEnv = Read-EnvFile
$containerEnv = $null
if (-not $NoContainer) {
    Assert-Docker
    $containerEnv = Read-ContainerPlcEnvironment
    if ($null -eq $containerEnv) { Write-Warn2 'The backend container does not exist yet (run deploy.ps1): only .env and the compose defaults are compared.' }
}
$rows = Get-PlcWriteConfigReport $dotEnv $containerEnv
$rows | Format-Table Setting, DotEnv, Compose, Container, @{ n = 'Met'; e = { if ($_.Met) { 'yes' } else { 'NO' } } }, Note -AutoSize -Wrap | Out-String -Width 200 | Write-Host
$missing = @($rows | Where-Object { -not $_.Met } | ForEach-Object { $_.Setting })
if ($missing.Count -eq 0) { Write-Ok 'Every write setting is in effect: the runtime is REAL_READ_WRITE (each IO still needs an Admin write approval).' }
else { Write-Warn2 ('Writing is NOT possible yet. Missing: ' + ($missing -join ', ') + '. The IO mapping page shows the same list from the running backend.') }
Write-Host "Nothing was changed."
