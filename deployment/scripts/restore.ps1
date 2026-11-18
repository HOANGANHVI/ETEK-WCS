# Restores the database from a backup made by backup.ps1. DESTRUCTIVE for the current database: it is replaced.
# Usage: .\restore.ps1 -BackupFile C:\ETEK-WCS\backups\wcs-db-...dump [-KeysFile ...tgz] [-NoSafetyBackup]
param(
    [Parameter(Mandatory = $true)][string]$BackupFile,
    [string]$KeysFile,
    [switch]$NoSafetyBackup
)
. (Join-Path $PSScriptRoot '_common.ps1')
Start-WcsRun 'restore.ps1'

Assert-Docker
$e = Assert-EnvFile
$db = if ($e['POSTGRES_DB']) { $e['POSTGRES_DB'] } else { 'wcs_db' }
$user = if ($e['POSTGRES_USER']) { $e['POSTGRES_USER'] } else { 'wcs' }

if (-not (Test-Path -LiteralPath $BackupFile)) { Fail "Backup file not found: $BackupFile" }
$BackupFile = (Resolve-Path -LiteralPath $BackupFile).Path
if ((Get-Item -LiteralPath $BackupFile).Length -lt 1024) { Fail 'The backup file is implausibly small.' }
if ($KeysFile -and -not (Test-Path -LiteralPath $KeysFile)) { Fail "Keys file not found: $KeysFile" }

Write-Host ''
Write-Host "This will REPLACE the current database '$db' with:" -ForegroundColor Yellow
Write-Host "   $BackupFile"
if ($KeysFile) { Write-Host "and REPLACE the key ring with: $KeysFile" -ForegroundColor Yellow }
Write-Host 'Everything written after that backup is lost (unless the safety backup below is kept).'
$answer = Read-Host 'Type RESTORE (capitals) to continue'
if ($answer -ne 'RESTORE') { Fail 'Cancelled. Nothing was changed.' }
Write-WcsLog -Step 'confirmation' -Result 'INFO' -Message "RESTORE confirmed by the operator for backup file $(Split-Path -Leaf $BackupFile)"

if (-not (Test-VolumeExists $script:PgVolume)) { Write-Warn2 'No existing database volume: this is a fresh restore.' }
elseif (-not $NoSafetyBackup) {
    Write-Step 'Safety backup of the current database'
    & (Join-Path $PSScriptRoot 'backup.ps1') -Label 'pre-restore'
    if ($LASTEXITCODE -ne 0) { Fail 'The safety backup failed: restore aborted, nothing was changed.' }
}

Write-Step 'Stopping the application (database stays up)'
Invoke-Compose @('stop','frontend','backend') | Out-Null
if ((Invoke-Compose @('up','-d','postgres')) -ne 0) { Fail 'Could not start PostgreSQL.' }
if (-not (Wait-Healthy -Services @('postgres') -TimeoutSec 90)) { Fail 'PostgreSQL did not become healthy.' }
$pg = Get-ContainerId 'postgres'

Write-Step 'Copying the dump into the container and validating it'
& docker cp $BackupFile "${pg}:/tmp/restore.dump"
if ($LASTEXITCODE -ne 0) { Fail 'docker cp failed.' }
& docker exec $pg sh -c 'pg_restore --list /tmp/restore.dump > /dev/null'
if ($LASTEXITCODE -ne 0) { & docker exec $pg rm -f /tmp/restore.dump; Fail 'The file is not a valid pg_dump custom-format backup. Nothing was changed.' }

Write-Step "Recreating database '$db' and restoring"
& docker exec $pg psql -U $user -d postgres -v ON_ERROR_STOP=1 -c "DROP DATABASE IF EXISTS $db WITH (FORCE)" -c "CREATE DATABASE $db OWNER $user"
if ($LASTEXITCODE -ne 0) { Fail 'Could not recreate the database.' }
& docker exec $pg pg_restore -U $user -d $db --no-owner --exit-on-error /tmp/restore.dump
$rc = $LASTEXITCODE
& docker exec $pg rm -f /tmp/restore.dump
if ($rc -ne 0) { Fail 'pg_restore failed. The database may be incomplete: restore again, or restore the safety backup (label pre-restore).' }

if ($KeysFile) {
    Write-Step 'Restoring the key ring'
    $KeysFile = (Resolve-Path -LiteralPath $KeysFile).Path
    $dir = Split-Path -Parent $KeysFile; $name = Split-Path -Leaf $KeysFile
    & docker run --rm -v "$($script:KeyVolume):/keys" -v "${dir}:/in:ro" alpine:3.20 sh -c "rm -rf /keys/* && tar xzf /in/$name -C /keys && chown -R 1654:1654 /keys"
    if ($LASTEXITCODE -ne 0) { Fail 'Key ring restore failed.' }
}

Write-Step 'Applying any newer migrations and starting the application'
if ((Invoke-Compose @('run','--rm','migrator')) -ne 0) { Fail 'Migration step failed after restore.' }
if ((Invoke-Compose @('up','-d')) -ne 0) { Fail 'Could not start the stack.' }
if (-not (Wait-Healthy)) { Fail 'The stack did not become healthy after restore. Run status.ps1 and check the logs.' }
Write-Ok 'Restore finished.'
Write-Host 'Verify now: sign in as Admin; check tasks, locations/inventory, PLC connections (Ket noi PLC) and IO mapping; run status.ps1.'
Write-Host 'If PLC/RCS passwords show as unreadable, the key ring does not match this database: restore the matching wcs-keys-*.tgz with -KeysFile.'
Complete-WcsRun 0
