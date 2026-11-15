<#
 Instalador de Blasphemous CoopLocal (Windows).
 Uso:  doble clic en install.bat   |   powershell -ExecutionPolicy Bypass -File install.ps1 [-GamePath "D:\Juegos\Blasphemous"] [-Uninstall]
 Requisito: tener instalado el Modding de Blasphemous (BepInEx + Blasphemous.ModdingAPI) en la carpeta del juego.
#>
param([string]$GamePath = "", [switch]$Uninstall)
$ErrorActionPreference = 'Stop'
$payload = Join-Path $PSScriptRoot 'payload'

function Test-Game($p) { return ($p -and (Test-Path -LiteralPath (Join-Path $p 'Blasphemous.exe'))) }

function Find-Game {
    $cands = New-Object System.Collections.Generic.List[string]
    foreach ($key in 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam', 'HKCU:\SOFTWARE\Valve\Steam') {
        try {
            $v = Get-ItemProperty -Path $key -ErrorAction Stop
            $steam = if ($v.InstallPath) { $v.InstallPath } else { $v.SteamPath }
            if ($steam) {
                $cands.Add((Join-Path $steam 'steamapps\common\Blasphemous'))
                $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
                if (Test-Path -LiteralPath $vdf) {
                    foreach ($m in [regex]::Matches((Get-Content -LiteralPath $vdf -Raw), '"path"\s+"([^"]+)"')) {
                        $cands.Add((Join-Path ($m.Groups[1].Value -replace '\\', '\') 'steamapps\common\Blasphemous'))
                    }
                }
            }
        } catch {}
    }
    foreach ($d in 'C:\Program Files (x86)\Steam', 'C:\Program Files\Steam', 'D:\Steam', 'D:\SteamLibrary', 'E:\SteamLibrary', 'C:\GOG Games', 'C:\Program Files (x86)\GOG Galaxy\Games') {
        $cands.Add((Join-Path $d 'steamapps\common\Blasphemous')); $cands.Add((Join-Path $d 'Blasphemous'))
    }
    foreach ($c in $cands) { if (Test-Game $c) { return $c } }
    return $null
}

try {
    if (-not $GamePath) { $GamePath = Find-Game }
    if (-not (Test-Game $GamePath)) {
        $GamePath = Read-Host 'No encontre Blasphemous. Escribe la carpeta del juego (donde esta Blasphemous.exe)'
    }
    if (-not (Test-Game $GamePath)) { throw "Carpeta invalida: no hay Blasphemous.exe en '$GamePath'." }
    Write-Host "Juego: $GamePath" -ForegroundColor Cyan

    $modding = Join-Path $GamePath 'Modding'
    if ($Uninstall) {
        Remove-Item -LiteralPath (Join-Path $modding 'plugins\CoopLocal.dll') -Force -ErrorAction SilentlyContinue
        foreach ($f in 'lightning_frames.png', 'skull_base.png') { Remove-Item -LiteralPath (Join-Path $modding $f) -Force -ErrorAction SilentlyContinue }
        Remove-Item -LiteralPath (Join-Path $modding 'skulls') -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host 'CoopLocal desinstalado (se conserva config\CoopLocal.cfg y tus partidas).' -ForegroundColor Green
        return
    }

    if (-not (Test-Path -LiteralPath (Join-Path $modding 'plugins\ModdingAPI.dll'))) {
        throw "Falta el Modding de Blasphemous (no existe '$modding\plugins\ModdingAPI.dll'). Instala primero el Blasphemous Modding Installer y vuelve a ejecutar esto."
    }
    if (-not (Test-Path -LiteralPath $payload)) { throw "No encuentro la carpeta 'payload' junto al instalador." }

    Copy-Item -Path (Join-Path $payload '*') -Destination $modding -Recurse -Force
    $cfg = Join-Path $modding 'config\CoopLocal.cfg'
    Write-Host 'Instalado correctamente.' -ForegroundColor Green
    Write-Host "Configuracion (opcional, se crea al primer arranque): $cfg"
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    if ($Host.Name -eq 'ConsoleHost' -and -not $env:COOP_NO_PAUSE) { Read-Host 'Pulsa Enter para salir' | Out-Null }
}
