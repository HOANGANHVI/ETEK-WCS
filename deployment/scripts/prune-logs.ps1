# Removes OLD deployment log files from the log folder. MANUAL and DRY-RUN by default: nothing is deleted unless -Execute is given and confirmed.
# Only files created by the deployment scripts are considered (deploy-*.log, failure-*.log, deployment-events-*.jsonl). The database copy of the events
# (Logging & Diagnostics, category DEPLOYMENT) is not touched by this script. Backups and .env are never touched.
# Usage: .\prune-logs.ps1 -OlderThanDays 90            (lists what would be removed)
#        .\prune-logs.ps1 -OlderThanDays 90 -Execute   (asks for confirmation, then removes)
param([Parameter(Mandatory = $true)][ValidateRange(7, 3650)][int]$OlderThanDays, [switch]$Execute)
. (Join-Path $PSScriptRoot '_common.ps1')
Start-WcsRun 'prune-logs.ps1'

if (-not (Test-Path -LiteralPath $script:LogDir)) { Write-Host "No log folder: $($script:LogDir)"; Complete-WcsRun 0; exit 0 }
$cutoff = (Get-Date).AddDays(-$OlderThanDays)
$old = Get-ChildItem -LiteralPath $script:LogDir -File | Where-Object {
    $_.LastWriteTime -lt $cutoff -and ($_.Name -like 'deploy-*.log' -or $_.Name -like 'failure-*.log' -or $_.Name -like 'deployment-events-*.jsonl')
}
if (-not $old) { Write-Ok "No deployment log older than $OlderThanDays days."; Complete-WcsRun 0; exit 0 }

$old | ForEach-Object { Write-Host ("  {0}  {1:N0} bytes  {2}" -f $_.Name, $_.Length, $_.LastWriteTime.ToString('yyyy-MM-dd')) }
Write-Host ("{0} file(s) older than {1} days." -f @($old).Count, $OlderThanDays)
if (-not $Execute) { Write-Host 'Dry run: nothing was deleted. Add -Execute to remove them.'; Complete-WcsRun 0; exit 0 }

$answer = Read-Host 'Type DELETE to remove these log files'
if ($answer -ne 'DELETE') { Fail 'Cancelled. Nothing was deleted.' }
foreach ($f in $old) { Remove-Item -LiteralPath $f.FullName -Force }
Write-Ok ("Removed {0} deployment log file(s) older than {1} days." -f @($old).Count, $OlderThanDays)
Complete-WcsRun 0
exit 0
