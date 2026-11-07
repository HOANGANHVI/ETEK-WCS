# Stops ETEK WCS safely: containers are stopped (PostgreSQL last, so it can shut down cleanly). Volumes and the database are NEVER removed.
# -Remove additionally removes the stopped containers (still keeps volumes). There is no option that deletes data.
param([switch]$Remove)
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-Docker
Assert-EnvFile | Out-Null
Write-Step 'Stopping frontend and backend'
Invoke-Compose @('stop','frontend','backend') | Out-Null
Write-Step 'Stopping PostgreSQL'
if ((Invoke-Compose @('stop','postgres')) -ne 0) { Fail 'Could not stop PostgreSQL cleanly.' }
if ($Remove) {
    Write-Step 'Removing stopped containers (volumes are kept)'
    if ((Invoke-Compose @('down')) -ne 0) { Fail 'docker compose down failed.' }
}
Write-Ok 'Stopped. Database and key-ring volumes are untouched.'
exit 0
