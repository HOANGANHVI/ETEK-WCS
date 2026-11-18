# Controlled deployment/update of ETEK WCS from the main branch.
#   Stops (without changing anything) on: wrong branch, uncommitted local changes, diverged history, missing/invalid .env, Docker problems.
#   Never deletes volumes, never uses "down -v", never deploys from develop or from unmerged code.
# Usage: .\deploy.ps1            (first install or update)
#        .\deploy.ps1 -NoPull    (rebuild/redeploy the commit that is already checked out; still requires branch main)
param([switch]$NoPull, [switch]$SkipBackup)
. (Join-Path $PSScriptRoot '_common.ps1')
Start-WcsRun 'deploy.ps1'

Write-Step 'Checking prerequisites'
Assert-Docker
Assert-Git
$e = Assert-EnvFile
Set-Location $script:RepoDir

$branch = (& git rev-parse --abbrev-ref HEAD).Trim()
if ($branch -ne 'main') { Fail "Current branch is '$branch'. Deployments are made from 'main' only (git checkout main)." }

$dirty = & git status --porcelain
if ($dirty) { Write-Host ($dirty -join "`n"); Fail 'There are local changes in the repository. Commit/stash or discard them deliberately, then run again. Nothing was changed.' }

$previous = (& git rev-parse HEAD).Trim()
if (-not $NoPull) {
    Write-Step 'Fetching origin'
    & git fetch origin
    if ($LASTEXITCODE -ne 0) { Fail 'git fetch failed (network / credentials).' }
    $counts = (& git rev-list --left-right --count 'HEAD...origin/main').Trim() -split '\s+'
    $ahead = [int]$counts[0]; $behind = [int]$counts[1]
    if ($ahead -gt 0) { Fail "Local main has $ahead commit(s) that are not on origin/main (ahead=$ahead, behind=$behind). Resolve this manually; nothing was changed." }
    if ($behind -gt 0) {
        Write-Step "Fast-forwarding main by $behind commit(s)"
        & git pull --ff-only origin main
        if ($LASTEXITCODE -ne 0) { Fail 'git pull --ff-only failed. Resolve manually; nothing was deployed.' }
    } else { Write-Ok 'main is already up to date.' }
}
$commit = (& git rev-parse --short HEAD).Trim()
Write-Ok "Deploying commit $commit (previous: $($previous.Substring(0, 7)))"

# backup before touching an existing installation
if (Test-VolumeExists $script:PgVolume) {
    if ($SkipBackup) { Write-Warn2 'Backup skipped by request (-SkipBackup).' }
    else {
        Write-Step 'Backing up the existing database before the update'
        & (Join-Path $PSScriptRoot 'backup.ps1') -Label "pre-$commit"
        if ($LASTEXITCODE -ne 0) { Fail 'Backup failed: the update was NOT started.' }
    }
} else { Write-Host '   First installation: no existing database, backup not needed.' }

# remember the commit for rollback
New-Item -ItemType Directory -Force -Path $script:ConfigDir | Out-Null
$logFile = Join-Path $script:ConfigDir 'deployments.log'
Add-Content -LiteralPath $logFile -Value ("{0}  from={1} to={2}" -f (Get-Date -Format 's'), $previous, (& git rev-parse HEAD).Trim())

$env:WCS_VERSION = $commit
Write-Step 'Building images (this can take several minutes the first time)'
if ((Invoke-Compose @('build')) -ne 0) { Fail 'Image build failed. The running system (if any) was not changed.' }

Write-Step 'Starting PostgreSQL'
if ((Invoke-Compose @('up','-d','postgres')) -ne 0) { Fail 'Could not start PostgreSQL.' }
if (-not (Wait-Healthy -Services @('postgres') -TimeoutSec 120)) { Fail 'PostgreSQL did not become healthy.' }

Write-Step 'Applying database migrations (forward only, single runner)'
if ((Invoke-Compose @('run','--rm','migrator')) -ne 0) { Fail 'Migration failed. The application was not restarted. See: docker compose logs migrator. To go back use the rollback steps in deployment\README.md.' }

Write-Step 'Starting / updating containers'
if ((Invoke-Compose @('up','-d')) -ne 0) { Fail 'docker compose up failed.' }
if (-not (Wait-Healthy)) { Fail 'The stack did not become healthy in time. Run status.ps1 and read the logs; rollback steps are in deployment\README.md.' }

Write-Ok "ETEK WCS $commit is running."
Write-Host ("   healthz: " + (Get-HealthzReport))
Write-Host ("   open:    http://localhost:" + (Get-HttpPort))
$mode = if ($e['PLC_MODE']) { $e['PLC_MODE'] } else { 'RealReadOnly' }
Write-Host "   PLC mode: $mode, live connection allowed: $(if ($e['PLC_ALLOW_LIVE_CONNECTION']) { $e['PLC_ALLOW_LIVE_CONNECTION'] } else { 'false' }). RCS: simulator adapter only (no real RCS)."
Complete-WcsRun 0
exit 0
