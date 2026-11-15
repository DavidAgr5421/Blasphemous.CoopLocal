# Prepara el paquete para usuarios: dist\BlasphemousCoopLocal.zip (instaladores + payload) y dist\payload (para Inno Setup).
# Uso: powershell -ExecutionPolicy Bypass -File installer\make-release.ps1 [-Version 1.0.0]
param([string]$Version = "1.0.0")
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    dotnet build (Join-Path $root 'Blasphemous.CoopLocal.csproj') -c Release -p:SolutionDir="$root"
    if ($LASTEXITCODE -ne 0) { throw "Build fallido" }

    $publish = Join-Path $root 'publish\CoopLocal'
    $dist    = Join-Path $root 'dist'
    $pkg     = Join-Path $dist 'BlasphemousCoopLocal'
    if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
    New-Item -ItemType Directory -Path (Join-Path $dist 'payload') | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $pkg 'payload') | Out-Null
    Copy-Item "$publish\*" (Join-Path $dist 'payload') -Recurse
    Copy-Item "$publish\*" (Join-Path $pkg 'payload') -Recurse
    foreach ($f in 'install.ps1', 'install.bat', 'uninstall.bat', 'install.sh', 'INSTALL.md') { Copy-Item (Join-Path $PSScriptRoot $f) $pkg }

    $zip = Join-Path $dist "BlasphemousCoopLocal-$Version.zip"
    Compress-Archive -Path "$pkg\*" -DestinationPath $zip
    Write-Host "Paquete: $zip" -ForegroundColor Green
    if (Get-Command iscc -ErrorAction SilentlyContinue) { iscc (Join-Path $PSScriptRoot 'CoopLocal.iss') } else { Write-Host "Inno Setup (iscc) no encontrado: omito setup.exe (opcional)." }
}
finally { Pop-Location }
