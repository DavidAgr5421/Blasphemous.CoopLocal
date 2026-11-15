<#
 Instalador de Blasphemous CoopLocal (Windows).
 Uso:  doble clic en install.bat   |   powershell -ExecutionPolicy Bypass -File install.ps1 [-GamePath "D:\Juegos\Blasphemous"] [-Uninstall] [-UpdateDeps]
 Si falta BepInEx / el Modding API, los descarga automaticamente de sus repositorios oficiales (BrandenEK) y los instala.
 Parametros internos (los usa el setup .exe): -DepsOnly (solo dependencias, el setup ya copio el mod), -NoPause.
#>
param([string]$GamePath = "", [switch]$Uninstall, [switch]$UpdateDeps, [switch]$DepsOnly, [switch]$NoPause)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$payload = Join-Path $PSScriptRoot 'payload'

# Fuentes oficiales (las mismas que usa el Blasphemous Modding Installer).
$ToolsUrl = 'https://github.com/BrandenEK/Blasphemous.ModdingTools/raw/main/win64.zip'                     # BepInEx + Doorstop + MultiFolderLoader ya configurados
$ApiUrl   = 'https://github.com/BrandenEK/Blasphemous.ModdingAPI/releases/latest/download/ModdingAPI.zip'  # Modding API (ultima version)

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
                        $cands.Add((Join-Path ($m.Groups[1].Value -replace '\\\\', '\') 'steamapps\common\Blasphemous'))
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

function Get-Zip($url, $name) {
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("coop_" + $name)
    Write-Host "  Descargando $name ..."
    Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $tmp
    return $tmp
}

function Install-Dependencies($game) {
    $modding = Join-Path $game 'Modding'
    $hasBep = (Test-Path -LiteralPath (Join-Path $game 'BepInEx\core\BepInEx.dll')) -and (Test-Path -LiteralPath (Join-Path $game 'winhttp.dll')) -and (Test-Path -LiteralPath (Join-Path $game 'BepInEx\patchers\BepInEx.MultiFolderLoader.dll'))
    $hasApi = Test-Path -LiteralPath (Join-Path $modding 'plugins\ModdingAPI.dll')

    if (-not $hasBep -or $UpdateDeps) {
        Write-Host 'Instalando BepInEx (Modding Tools oficiales)...' -ForegroundColor Cyan
        $zip = Get-Zip $ToolsUrl 'modding_tools.zip'
        Expand-Archive -LiteralPath $zip -DestinationPath $game -Force
        Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
    } else { Write-Host 'BepInEx ya instalado.' -ForegroundColor DarkGray }

    if (-not $hasApi -or $UpdateDeps) {
        Write-Host 'Instalando Modding API (ultima version)...' -ForegroundColor Cyan
        $zip = Get-Zip $ApiUrl 'modding_api.zip'
        New-Item -ItemType Directory -Force -Path $modding | Out-Null
        Expand-Archive -LiteralPath $zip -DestinationPath $modding -Force
        Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
    } else { Write-Host 'Modding API ya instalado.' -ForegroundColor DarkGray }

    if (-not (Test-Path -LiteralPath (Join-Path $modding 'plugins\ModdingAPI.dll'))) { throw 'No se pudo instalar el Modding API.' }
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
        Write-Host 'CoopLocal desinstalado (se conserva config\CoopLocal.cfg, BepInEx y tus partidas).' -ForegroundColor Green
        return
    }

    Install-Dependencies $GamePath

    if (-not $DepsOnly) {
        if (-not (Test-Path -LiteralPath $payload)) { throw "No encuentro la carpeta 'payload' junto al instalador." }
        Copy-Item -Path (Join-Path $payload '*') -Destination $modding -Recurse -Force
    }
    Write-Host 'Instalado correctamente. Ya puedes iniciar el juego.' -ForegroundColor Green
    Write-Host "Configuracion (se crea al primer arranque): $(Join-Path $modding 'config\CoopLocal.cfg')"
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    if (-not $NoPause) { Read-Host 'Pulsa Enter para salir' | Out-Null }
    exit 1
}
if (-not $NoPause -and $Host.Name -eq 'ConsoleHost') { Read-Host 'Pulsa Enter para salir' | Out-Null }
