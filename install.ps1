$ErrorActionPreference = 'Stop'

$installDir = Join-Path $HOME ".local\share\create-sbx"
$binDir = Join-Path $HOME ".local\bin"

$arch = $env:PROCESSOR_ARCHITECTURE
switch ($arch) {
    "AMD64" { $artifactName = "create-sbx-windows-amd64.exe" }
    "ARM64" { $artifactName = "create-sbx-windows-arm64.exe" }
    default {
        Write-Error "Unsupported architecture: $arch"
        exit 1
    }
}

New-Item -ItemType Directory -Path $installDir -Force | Out-Null
New-Item -ItemType Directory -Path $binDir -Force | Out-Null

$appPath = Join-Path $installDir "create-sbx.exe"
Invoke-WebRequest -Uri "https://github.com/geofflamrock/create-sbx/releases/latest/download/$artifactName" -OutFile $appPath

$binPath = Join-Path $binDir "create-sbx.exe"
Copy-Item -Path $appPath -Destination $binPath -Force

$userPath = [Environment]::GetEnvironmentVariable("PATH", "User")
if ($userPath -notlike "*$binDir*") {
    Write-Host ""
    Write-Host "Warning: $binDir is not in your PATH."
    Write-Host "Add it permanently by running:"
    Write-Host "  [Environment]::SetEnvironmentVariable('PATH', `$env:PATH + ';$binDir', 'User')"
}

Write-Host ""
Write-Host "create-sbx installed successfully. Run 'create-sbx' to get started."
