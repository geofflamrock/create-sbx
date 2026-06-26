$ErrorActionPreference = 'Stop'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet is required but not installed. Install it from https://dot.net"
    exit 1
}

$installDir = Join-Path $HOME ".local\share\create-sbx"
$binDir = Join-Path $HOME ".local\bin"

New-Item -ItemType Directory -Path $installDir -Force | Out-Null
New-Item -ItemType Directory -Path $binDir -Force | Out-Null

$appPath = Join-Path $installDir "create-sbx"
Invoke-WebRequest -Uri "https://raw.githubusercontent.com/geofflamrock/create-sbx/main/create-sbx.cs" -OutFile $appPath

$wrapperPath = Join-Path $binDir "create-sbx.ps1"
Set-Content -Path $wrapperPath -Value "dotnet run `"$appPath`" @args"

$userPath = [Environment]::GetEnvironmentVariable("PATH", "User")
if ($userPath -notlike "*$binDir*") {
    Write-Host ""
    Write-Host "Warning: $binDir is not in your PATH."
    Write-Host "Add it permanently by running:"
    Write-Host "  [Environment]::SetEnvironmentVariable('PATH', `$env:PATH + ';$binDir', 'User')"
}

Write-Host ""
Write-Host "create-sbx installed successfully. Run 'create-sbx' to get started."
