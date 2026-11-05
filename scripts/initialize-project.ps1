$ErrorActionPreference = "Stop"

function Require-Command([string]$Name, [string]$InstallHint) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name was not found. $InstallHint"
    }
}

Write-Host "=== ETEK WCS project initialization ===" -ForegroundColor Cyan
Require-Command "dotnet" "Install the .NET 10 SDK, then reopen PowerShell."
Require-Command "node" "Install a supported Node.js LTS version (22.12+ recommended)."
Require-Command "npm" "Install Node.js/npm, then reopen PowerShell."

$sdkList = dotnet --list-sdks
if (-not ($sdkList | Where-Object { $_ -match '^10\.' })) {
    throw "The .NET 10 SDK is required by this setup script. Detected SDKs: $($sdkList -join ', '). Install .NET 10 SDK first."
}

$nodeVersion = (node -p "process.versions.node").Trim()
$nodeParts = $nodeVersion.Split('.')
$nodeMajor = [int]$nodeParts[0]
$nodeMinor = [int]$nodeParts[1]
if (-not (($nodeMajor -ge 22 -and $nodeMinor -ge 12) -or ($nodeMajor -ge 24))) {
    throw "Node.js 22.12+ is recommended/required for current Vite. Detected: $nodeVersion"
}

$backend = Join-Path $PSScriptRoot "..\src\backend"
$frontend = Join-Path $PSScriptRoot "..\src\frontend"
$backend = [System.IO.Path]::GetFullPath($backend)
$frontend = [System.IO.Path]::GetFullPath($frontend)

if (-not (Test-Path (Join-Path $backend "WCS.sln"))) {
    Push-Location $backend
    try {
        dotnet new sln -n WCS

        dotnet new webapi -n WCS.Api -f net10.0
        dotnet new classlib -n WCS.Domain -f net10.0
        dotnet new classlib -n WCS.Application -f net10.0
        dotnet new classlib -n WCS.Infrastructure -f net10.0
        dotnet new classlib -n WCS.Runtime -f net10.0

        dotnet sln WCS.sln add `
            WCS.Api/WCS.Api.csproj `
            WCS.Domain/WCS.Domain.csproj `
            WCS.Application/WCS.Application.csproj `
            WCS.Infrastructure/WCS.Infrastructure.csproj `
            WCS.Runtime/WCS.Runtime.csproj

        dotnet add WCS.Application/WCS.Application.csproj reference WCS.Domain/WCS.Domain.csproj
        dotnet add WCS.Infrastructure/WCS.Infrastructure.csproj reference WCS.Domain/WCS.Domain.csproj
        dotnet add WCS.Infrastructure/WCS.Infrastructure.csproj reference WCS.Application/WCS.Application.csproj
        dotnet add WCS.Runtime/WCS.Runtime.csproj reference WCS.Application/WCS.Application.csproj
        dotnet add WCS.Runtime/WCS.Runtime.csproj reference WCS.Infrastructure/WCS.Infrastructure.csproj
        dotnet add WCS.Api/WCS.Api.csproj reference WCS.Application/WCS.Application.csproj
        dotnet add WCS.Api/WCS.Api.csproj reference WCS.Infrastructure/WCS.Infrastructure.csproj
        dotnet add WCS.Api/WCS.Api.csproj reference WCS.Runtime/WCS.Runtime.csproj

        dotnet build WCS.sln
    }
    finally {
        Pop-Location
    }
}
else {
    Write-Host "Backend solution already exists; skipping creation." -ForegroundColor Yellow
}

if (-not (Test-Path (Join-Path $frontend "wcs-web/package.json"))) {
    Push-Location $frontend
    try {
        npm create vite@latest wcs-web -- --template react-ts
        Push-Location (Join-Path $frontend "wcs-web")
        try {
            npm install
            npm run build
        }
        finally {
            Pop-Location
        }
    }
    finally {
        Pop-Location
    }
}
else {
    Write-Host "Frontend app already exists; skipping creation." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Initialization completed. No WCS business workflows or live device connections were implemented." -ForegroundColor Green
Write-Host "Next: open the repository root in VS Code and ask Claude Code to inspect the scaffold only."
