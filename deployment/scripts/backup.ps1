# Backs up the PostgreSQL database (custom-format dump) and the Data Protection key ring to a folder OUTSIDE the repository.
# Usage: .\backup.ps1 [-Label pre-update] [-Keep 14]
param([string]$Label = 'manual', [int]$Keep = 14)
. (Join-Path $PSScriptRoot '_common.ps1')
Start-WcsRun 'backup.ps1'

Assert-Docker
$e = Assert-EnvFile
$db = if ($e['POSTGRES_DB']) { $e['POSTGRES_DB'] } else { 'wcs_db' }
$user = if ($e['POSTGRES_USER']) { $e['POSTGRES_USER'] } else { 'wcs' }

if (-not (Test-VolumeExists $script:PgVolume)) { Fail "Database volume $($script:PgVolume) does not exist: nothing to back up." }
if ((Get-ContainerHealth 'postgres') -ne 'healthy') {
    Write-Step 'Starting PostgreSQL for the backup'
    if ((Invoke-Compose @('up','-d','postgres')) -ne 0) { Fail 'Could not start PostgreSQL.' }
    if (-not (Wait-Healthy -Services @('postgres') -TimeoutSec 90)) { Fail 'PostgreSQL did not become healthy.' }
}

New-Item -ItemType Directory -Force -Path $script:BackupDir | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$dump = Join-Path $script:BackupDir "wcs-db-$stamp-$Label.dump"
$keys = Join-Path $script:BackupDir "wcs-keys-$stamp-$Label.tgz"
$pg = Get-ContainerId 'postgres'

Write-Step "Dumping database '$db'"
& docker exec $pg sh -c "pg_dump -U $user -d $db -Fc -f /tmp/wcs.dump"
if ($LASTEXITCODE -ne 0) { Fail 'pg_dump failed.' }
& docker cp "${pg}:/tmp/wcs.dump" $dump
if ($LASTEXITCODE -ne 0) { Fail 'Could not copy the dump out of the container.' }
& docker exec $pg sh -c 'pg_restore --list /tmp/wcs.dump > /dev/null'
$verify = $LASTEXITCODE
& docker exec $pg rm -f /tmp/wcs.dump
if ($verify -ne 0) { Remove-Item -LiteralPath $dump -Force; Fail 'The dump failed verification (pg_restore --list) and was discarded.' }
if (-not (Test-Path $dump) -or (Get-Item $dump).Length -lt 1024) { Fail 'The backup file is missing or implausibly small.' }
Write-Ok ("Database backup: {0} ({1:N0} bytes)" -f $dump, (Get-Item $dump).Length)

if (Test-VolumeExists $script:KeyVolume) {
    Write-Step 'Archiving the Data Protection key ring'
    & docker run --rm -v "$($script:KeyVolume):/keys:ro" -v "$($script:BackupDir):/out" alpine:3.20 tar czf "/out/wcs-keys-$stamp-$Label.tgz" -C /keys .
    if ($LASTEXITCODE -ne 0) { Fail 'Key ring archive failed.' }
    Write-Ok "Key ring backup: $keys"
} else { Write-Warn2 'No key-ring volume yet (first start not done); skipping.' }

# retention: keep the newest $Keep database dumps (and their key archives); manual cleanup of older ones is never automatic beyond this
Get-ChildItem -LiteralPath $script:BackupDir -Filter 'wcs-db-*.dump' | Sort-Object LastWriteTime -Descending | Select-Object -Skip $Keep | ForEach-Object {
    $stampOld = ($_.Name -replace '^wcs-db-', '' -replace '\.dump$', '')
    Remove-Item -LiteralPath $_.FullName -Force
    Get-ChildItem -LiteralPath $script:BackupDir -Filter "wcs-keys-$stampOld.tgz" -ErrorAction SilentlyContinue | Remove-Item -Force
    Write-Host "   removed old backup $($_.Name)"
}
Write-Warn2 "The .env file ($($script:EnvFile)) holds secrets and is NOT in the backup: store a copy securely."
Write-Output $dump
Complete-WcsRun 0
exit 0
