# Instalar Blasphemous CoopLocal

No necesitas instalar nada antes: **los instaladores descargan e instalan BepInEx y el Modding API solos** (desde los repositorios oficiales de BrandenEK, siempre la última versión) si no los encuentran. Solo hace falta conexión a internet la primera vez. Todos los jugadores de una partida deben tener la misma versión del mod.

## Opción A — Setup de Windows: `BlasphemousCoopLocal_Setup.exe`
Doble clic → elige la carpeta del juego (se detecta sola desde Steam) → Instalar. **Instala solo los archivos del mod**: BepInEx y el Modding API deben estar ya instalados (usa la opción B o C si no los tienes). Se desinstala desde "Aplicaciones".

## Opción B — Script sin setup
- **Windows:** doble clic en `install.bat` (`uninstall.bat` para quitarlo). Opciones: `powershell -ExecutionPolicy Bypass -File install.ps1 -GamePath "D:\Juegos\Blasphemous"`; `-UpdateDeps` fuerza actualizar BepInEx y el Modding API.
- **Linux / Steam Deck:** `chmod +x install.sh && ./install.sh` (opciones: `--game-path /ruta`, `--uninstall`, `--update-deps`; requiere `unzip` y `curl` o `wget`).
  Después, en Steam → Blasphemous → Propiedades → Opciones de lanzamiento: `./start_blas1_modded.sh %command%`

## Opción C — Manual
1. Instala BepInEx con las *Modding Tools* oficiales: descarga `win64.zip` (o `linux64.zip`) de <https://github.com/BrandenEK/Blasphemous.ModdingTools> y descomprímelo dentro de la carpeta del juego.
2. Descarga `ModdingAPI.zip` de <https://github.com/BrandenEK/Blasphemous.ModdingAPI/releases/latest> y descomprímelo dentro de `Modding/`.
3. Copia **todo el contenido** de la carpeta `payload` de este paquete dentro de `Modding/` (debe quedar `Modding/plugins/CoopLocal.dll`).
4. Inicia el juego.

## Configuración
Al primer arranque se crea `Modding/config/CoopLocal.cfg`. Para jugar normalmente deja `DebugMode = false`
(las herramientas de depuración quedan apagadas y sin textos en pantalla). Ponlo en `true` para activarlas.

## Desinstalar
`uninstall.bat` / `./install.sh --uninstall`, o borra `Modding/plugins/CoopLocal.dll` y las carpetas/archivos del mod. Se conservan BepInEx, tus partidas y ajustes.
