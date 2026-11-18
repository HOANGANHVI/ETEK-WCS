# CR-007: "docker" GIA LAP chi dung de tu kiem tra bo cong cu nghiem thu (tests\deployment\Test-DockerAcceptancePackage.ps1).
# KHONG phai Docker that. Khong dung/khoi dong container that, khong ghi vao he thong: chi doc/ghi trong thu muc tam cua bai tu kiem tra.
# Cau hinh qua bien moi truong FAKE_DOCKER_*:
#   FAKE_DOCKER_LOGDIR      thu muc log trien khai tren host (= /deploy-logs trong "container")
#   FAKE_DOCKER_BACKEND_OUT file stdout cua tien trinh backend that dang chay (dung lam log container backend)
#   FAKE_DOCKER_MOUNT_RW    "true" mo phong mount ghi duoc, mac dinh chi-doc
#   FAKE_DOCKER_EXTRA_LOG   file them cac dong log backend gia (vd dong PLC STATE) vao cuoi log
#   FAKE_DOCKER_HIDE_PLC    "true" an cac dong "PLC PLC-..." khoi log backend (mo phong stack khong co su kien PLC)
#   FAKE_DOCKER_ROLE        role PostgreSQL ma backend that dang dung (stop/start = NOLOGIN/LOGIN role nay tren DB tam)
#   FAKE_DOCKER_PGBIN, FAKE_DOCKER_PGPW  psql.exe va mat khau superuser (chi de dieu khien role tren DB tam)
$a = @($args)
$ids = @{ postgres = ('a1' * 32); backend = ('b2' * 32); frontend = ('c3' * 32); migrator = ('d4' * 32) }
$names = @{ postgres = 'etek-wcs-postgres-1'; backend = 'etek-wcs-backend-1'; frontend = 'etek-wcs-frontend-1'; migrator = 'etek-wcs-migrator-1' }
$stateDir = $env:FAKE_DOCKER_STATE
function Get-Down { Test-Path (Join-Path $stateDir 'postgres.stopped') }
function Read-Shared([string]$path) {
    # tien trinh backend that dang giu file stdout de ghi: doc voi FileShare.ReadWrite (giong cach docker logs doc log dang ghi)
    $fs = New-Object System.IO.FileStream($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    try { $sr = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8); $t = $sr.ReadToEnd(); $sr.Dispose() } finally { $fs.Dispose() }
    return @($t -split "\r?\n" | Where-Object { $_ -ne '' })
}
function Svc-ById([string]$id) { foreach ($k in $ids.Keys) { if ($ids[$k] -eq $id -or $names[$k] -eq $id) { return $k } } return $null }
function Pg([string]$sql) {
    if (-not $env:FAKE_DOCKER_PGBIN) { return }
    $env:PGPASSWORD = $env:FAKE_DOCKER_PGPW; & $env:FAKE_DOCKER_PGBIN -h localhost -U postgres -d postgres -w -At -c $sql 2>&1 | Out-Null; Remove-Item Env:PGPASSWORD
}

if ($a.Count -eq 0) { exit 0 }
switch ($a[0]) {
    '--version' { 'Docker version 27.0.0-FAKE, build fake'; exit 0 }
    'version' { 'Client: FAKE docker (self-test)'; 'Server: FAKE'; exit 0 }
    'info' { 'fake-27.0.0|linux'; exit 0 }
    'volume' {
        if ($a[1] -eq 'inspect') { '[{"Name":"etek_wcs_pgdata"}]'; exit 0 }
        if ($a[1] -eq 'ls') { 'etek_wcs_pgdata|local'; 'etek_wcs_keys|local'; exit 0 }
    }
    'network' { 'etek-wcs_edge_net|bridge|local'; exit 0 }
    'ps' { foreach ($k in 'postgres', 'backend', 'frontend') { '{0}|{1}|{2}|Up 5 minutes|' -f $ids[$k].Substring(0, 12), $names[$k], 'img' }; exit 0 }
    'stop' {
        $s = Svc-ById $a[1]
        if ($s -eq 'postgres') { New-Item -ItemType File -Force -Path (Join-Path $stateDir 'postgres.stopped') | Out-Null; if ($env:FAKE_DOCKER_ROLE) { Pg "ALTER ROLE $($env:FAKE_DOCKER_ROLE) NOLOGIN"; Pg "select pg_terminate_backend(pid) from pg_stat_activity where usename='$($env:FAKE_DOCKER_ROLE)'" }; $a[1]; exit 0 }
        Write-Error 'fake docker: stop supported only for postgres'; exit 1
    }
    'start' {
        $s = Svc-ById $a[1]
        if ($s -eq 'postgres') { Remove-Item -LiteralPath (Join-Path $stateDir 'postgres.stopped') -Force -ErrorAction SilentlyContinue; if ($env:FAKE_DOCKER_ROLE) { Pg "ALTER ROLE $($env:FAKE_DOCKER_ROLE) LOGIN" }; $a[1]; exit 0 }
        exit 1
    }
    'inspect' {
        $s = Svc-ById $a[1]
        if (-not $s) { Write-Error "Error: No such object: $($a[1])"; exit 1 }
        $mounts = @()
        $running = $true; $health = 'healthy'
        if ($s -eq 'postgres') { $mounts = @(@{ Type = 'volume'; Name = 'etek_wcs_pgdata'; Source = '/var/lib/docker/volumes/etek_wcs_pgdata/_data'; Destination = '/var/lib/postgresql/data'; Driver = 'local'; Mode = 'z'; RW = $true; Propagation = '' }); if (Get-Down) { $running = $false; $health = 'unhealthy' } }
        if ($s -eq 'backend') { $mounts = @(@{ Type = 'volume'; Name = 'etek_wcs_keys'; Source = '/var/lib/docker/volumes/etek_wcs_keys/_data'; Destination = '/keys'; Driver = 'local'; Mode = 'z'; RW = $true; Propagation = '' }, @{ Type = 'bind'; Source = $env:FAKE_DOCKER_LOGDIR; Destination = '/deploy-logs'; Mode = 'ro'; RW = ($env:FAKE_DOCKER_MOUNT_RW -eq 'true'); Propagation = 'rprivate' }) }
        $o = @{ Id = $ids[$s]; Name = '/' + $names[$s]; RestartCount = 0
            State = @{ Status = $(if ($running) { 'running' } else { 'exited' }); Running = $running; ExitCode = 0; OOMKilled = $false; StartedAt = '2026-11-16T19:00:00Z'; Health = @{ Status = $health; Log = @(@{ ExitCode = 0; End = '2026-11-16T19:10:00Z'; Output = '' }) } }
            Config = @{ Image = $(if ($s -eq 'postgres') { 'postgres:16-alpine' } else { "etek-wcs-${s}:local" }); Labels = @{ 'com.docker.compose.project' = 'etek-wcs'; 'com.docker.compose.service' = $s }; Env = @('POSTGRES_PASSWORD=THIS-MUST-NEVER-BE-COPIED') }
            Mounts = $mounts; NetworkSettings = @{ Ports = $(if ($s -eq 'frontend') { @{ '80/tcp' = @(@{ HostIp = '0.0.0.0'; HostPort = '80' }) } } else { @{} }) } }
        ConvertTo-Json -InputObject @($o) -Depth 8; exit 0
    }
    'exec' {
        $s = Svc-ById $a[1]
        $cmd = ($a | Select-Object -Skip 2) -join ' '
        if ($s -eq 'backend' -and $cmd -match 'touch /deploy-logs/(\S+)') {
            if ($env:FAKE_DOCKER_MOUNT_RW -eq 'true') { Set-Content -LiteralPath (Join-Path $env:FAKE_DOCKER_LOGDIR $Matches[1]) -Value '' ; exit 0 }
            "touch: /deploy-logs/$($Matches[1]): Read-only file system"; exit 1
        }
        if ($s -eq 'backend' -and $cmd -match 'ls') { Get-ChildItem -LiteralPath $env:FAKE_DOCKER_LOGDIR -File | ForEach-Object { '-rw-r--r-- 1 root root {0} {1}' -f $_.Length, $_.Name }; exit 0 }
        if ($s -eq 'postgres' -and $cmd -match 'pg_isready') { if (Get-Down) { exit 2 } else { exit 0 } }
        if ($s -eq 'postgres' -and $cmd -match 'psql') { if (Get-Down) { 'psql: error: connection refused'; exit 2 } else { '5 migrations; 12 MB'; exit 0 } }
        exit 0
    }
    'compose' {
        $i = 1; $opts = @('--project-directory', '-f', '--env-file', '-p')
        while ($i -lt $a.Count -and ($a[$i] -in $opts -or $a[$i].StartsWith('-'))) { if ($a[$i] -in $opts) { $i += 2 } else { $i++ } }
        $verb = $a[$i]; $rest = @($a | Select-Object -Skip ($i + 1))
        switch ($verb) {
            'version' { if ($rest -contains '--short') { '2.29.0' } else { 'Docker Compose version v2.29.0-FAKE' }; exit 0 }
            'ps' { $svc = $rest | Where-Object { $ids.ContainsKey($_) } | Select-Object -First 1; if ($rest -contains '-q' -and $svc) { $ids[$svc]; exit 0 }; exit 0 }
            'logs' {
                $svc = $rest | Where-Object { $ids.ContainsKey($_) } | Select-Object -First 1
                if ($svc -ne 'backend' -or -not (Test-Path -LiteralPath $env:FAKE_DOCKER_BACKEND_OUT)) { exit 0 }
                $lines = @(Read-Shared $env:FAKE_DOCKER_BACKEND_OUT)
                if ($env:FAKE_DOCKER_EXTRA_LOG -and (Test-Path -LiteralPath $env:FAKE_DOCKER_EXTRA_LOG)) { $lines += @(Read-Shared $env:FAKE_DOCKER_EXTRA_LOG) }
                if ($env:FAKE_DOCKER_HIDE_PLC -eq 'true') { $lines = @($lines | Where-Object { $_ -notmatch 'PLC PLC-' }) }
                $si = [array]::IndexOf($rest, '--since')
                if ($si -ge 0) {
                    $since = [datetimeoffset]::Parse($rest[$si + 1], [System.Globalization.CultureInfo]::InvariantCulture).LocalDateTime
                    $lines = @($lines | Where-Object { if ($_ -match '^\[?(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})') { [datetime]::Parse($Matches[1]) -ge $since } else { $false } })
                }
                $ti = [array]::IndexOf($rest, '--tail'); if ($ti -ge 0) { $n = [int]$rest[$ti + 1]; $lines = @($lines | Select-Object -Last $n) }
                $lines; exit 0
            }
            default { exit 0 }
        }
    }
    default { exit 0 }
}
exit 0
