# Starts ETEK WCS (existing images). Runs the migrator first through the compose dependency, so the database is always current.
. (Join-Path $PSScriptRoot '_common.ps1')
Start-WcsRun 'start.ps1'
Assert-Docker
Assert-EnvFile | Out-Null
Write-Step 'Starting containers'
if ((Invoke-Compose @('up','-d')) -ne 0) { Fail 'docker compose up failed. If the images do not exist yet, run deploy.ps1 first.' }
if (-not (Wait-Healthy)) { Fail 'The stack did not become healthy in time. Run status.ps1.' }
Write-Ok 'ETEK WCS is running.'
Write-Host ("   healthz: " + (Get-HealthzReport))
Write-Host ("   open:    http://localhost:" + (Get-HttpPort))
Complete-WcsRun 0
exit 0
