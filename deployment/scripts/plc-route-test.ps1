# Checks that the BACKEND CONTAINER can open a TCP connection to the PLCs over the LAN. It only opens and closes a TCP socket:
# it sends NO bytes, so no PLC command, no read, no write. Run it only after the PLC engineer has confirmed the TCP port.
# The port is deliberately mandatory: no PLC port is assumed anywhere in this project.
# Usage: .\plc-route-test.ps1 -Port <confirmed TCP port> [-Hosts 192.168.10.101,192.168.10.111,192.168.10.121,192.168.10.131]
param(
    [Parameter(Mandatory = $true)][ValidateRange(1, 65535)][int]$Port,
    [string[]]$Hosts = @('192.168.10.101', '192.168.10.111', '192.168.10.121', '192.168.10.131'),
    [int]$TimeoutSec = 3
)
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-Docker
$id = Get-ContainerId 'backend'
if (-not $id) { Fail 'The backend container is not running. Start the system first (start.ps1).' }

$failed = 0
foreach ($h in $Hosts) {
    if ($h -notmatch '^\d{1,3}(\.\d{1,3}){3}$') { Fail "Not an IPv4 address: $h" }
    & docker exec $id timeout $TimeoutSec bash -c "exec 3<>/dev/tcp/$h/$Port" *> $null
    if ($LASTEXITCODE -eq 0) { Write-Ok "$h`:$Port reachable from the backend container (TCP connect only)" }
    else { Write-Warn2 "$h`:$Port NOT reachable from the backend container"; $failed++ }
}
if ($failed -gt 0) {
    Write-Host 'Check: PLC powered and on 192.168.10.x, the port is the one confirmed by the PLC engineer, Windows firewall, and whether the PLC accepts connections from this machine''s IP (the PLC sees the Windows host address, not the container).'
    exit 2
}
Write-Host 'TCP reachability only. This is NOT a PLC communication test and does not make the connection "tested": that needs the PLC engineer''s read-only commissioning.'
exit 0
