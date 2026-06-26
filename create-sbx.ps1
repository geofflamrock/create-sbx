$ErrorActionPreference = 'Stop'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet is required but not installed. Install it from https://dot.net"
    exit 1
}

$tmpDir = Join-Path ([System.IO.Path]::GetTempPath()) ([System.IO.Path]::GetRandomFileName())
New-Item -ItemType Directory -Path $tmpDir | Out-Null

try {
    $scriptPath = Join-Path $tmpDir "create-sbx"
    Invoke-WebRequest -Uri "https://raw.githubusercontent.com/geofflamrock/create-sbx/main/create-sbx" -OutFile $scriptPath
    dotnet run $scriptPath
}
finally {
    Remove-Item -Recurse -Force $tmpDir
}
