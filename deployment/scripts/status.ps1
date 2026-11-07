# Shows the state of every ETEK WCS container, health, uptime, volumes and the anonymous health probe. Read-only.
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-Docker
$e = Read-EnvFile

Write-Host '--- Containers' -ForegroundColor Cyan
foreach ($s in 'postgres', 'migrator', 'backend', 'frontend') {
    $id = Get-ContainerId $s
    if (-not $id) {
        $id = (& docker ps -a -q --filter "label=com.docker.compose.project=$($script:Project)" --filter "label=com.docker.compose.service=$s" | Select-Object -First 1)
    }
    if (-not $id) { Write-Host ("{0,-10} not created" -f $s); continue }
    $info = & docker inspect --format '{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{else}}-{{end}}|{{.State.StartedAt}}|{{.State.ExitCode}}|{{.Config.Image}}' $id
    $p = $info -split '\|'
    $uptime = ''
    if ($p[0] -eq 'running') { try { $uptime = ((Get-Date).ToUniversalTime() - ([datetime]::Parse($p[2]).ToUniversalTime())).ToString('d\.hh\:mm\:ss') } catch { $uptime = '?' } }
    Write-Host ("{0,-10} state={1,-8} health={2,-9} uptime={3,-12} exit={4} image={5}" -f $s, $p[0], $p[1], $uptime, $p[3], $p[4])
}
Write-Host '   (migrator is a one-shot job: state=exited, exit=0 is the normal result)'

Write-Host '--- Volumes' -ForegroundColor Cyan
foreach ($v in $script:PgVolume, $script:KeyVolume) { Write-Host ("{0,-20} {1}" -f $v, $(if (Test-VolumeExists $v) { 'present' } else { 'MISSING' })) }

Write-Host '--- Health probe (anonymous /healthz through Nginx)' -ForegroundColor Cyan
Write-Host (Get-HealthzReport)

Write-Host '--- Configuration (from the machine .env)' -ForegroundColor Cyan
Write-Host ("PLC_MODE={0}  PLC_ALLOW_LIVE_CONNECTION={1}  PLC_ALLOW_SIMULATOR_IN_PRODUCTION={2}  WCS_COOKIE_SECURE={3}  port={4}" -f `
    $(if ($e['PLC_MODE']) { $e['PLC_MODE'] } else { 'RealReadOnly' }), $(if ($e['PLC_ALLOW_LIVE_CONNECTION']) { $e['PLC_ALLOW_LIVE_CONNECTION'] } else { 'false' }),
    $(if ($e['PLC_ALLOW_SIMULATOR_IN_PRODUCTION']) { $e['PLC_ALLOW_SIMULATOR_IN_PRODUCTION'] } else { 'false' }), $(if ($e['WCS_COOKIE_SECURE']) { $e['WCS_COOKIE_SECURE'] } else { 'Always' }), (Get-HttpPort))
Write-Host 'RCS: simulator adapter only (no real RCS in this release). MQTT: not used.'
if (Get-Command git -ErrorAction SilentlyContinue) { Push-Location $script:RepoDir; Write-Host ("Source: branch {0}, commit {1}" -f (& git rev-parse --abbrev-ref HEAD), (& git rev-parse --short HEAD)); Pop-Location }
