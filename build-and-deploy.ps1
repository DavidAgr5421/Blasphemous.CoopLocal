# Compiles the CoopLocal mod and copies the resulting DLL into the game's Modding\plugins folder.
#
# Usage:  powershell -ExecutionPolicy Bypass -File .\build-and-deploy.ps1
#         (or right-click -> "Run with PowerShell")

$ErrorActionPreference = 'Stop'

$repoRoot   = $PSScriptRoot
$config     = 'Debug'
$gameModDir = 'C:\Program Files (x86)\Steam\steamapps\common\Blasphemous\Modding'
$pluginDir  = Join-Path $gameModDir 'plugins'
$project    = Join-Path $repoRoot 'Blasphemous.CoopLocal.csproj'
$outDll     = Join-Path $repoRoot "bin\$config\CoopLocal.dll"

if (-not (Test-Path -LiteralPath $project)) {
    Write-Error "Project not found: $project"
    exit 1
}
if (-not (Test-Path -LiteralPath $pluginDir)) {
    Write-Error "Game Modding folder not found: $pluginDir"
    exit 1
}

Write-Host "== Build: $project" -ForegroundColor Cyan
dotnet build $project -c $config -p:SolutionDir="$repoRoot"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build FAILED (exit code $LASTEXITCODE)"
}
Write-Host "== Build OK" -ForegroundColor Green

if (-not (Test-Path -LiteralPath $outDll)) {
    Write-Error "Expected output DLL not found: $outDll"
}

Copy-Item -LiteralPath $outDll -Destination (Join-Path $pluginDir 'CoopLocal.dll') -Force

$src    = Get-Item -LiteralPath $outDll
$dest   = Get-Item -LiteralPath (Join-Path $pluginDir 'CoopLocal.dll')
$stamp  = '{0:HH:mm:ss}' -f $dest.LastWriteTime
Write-Host "== Deployed CoopLocal.dll ($($src.Length) bytes), $stamp" -ForegroundColor Green
Write-Host "   -> $($dest.FullName)" -ForegroundColor Green