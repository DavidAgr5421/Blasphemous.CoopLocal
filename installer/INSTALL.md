# Instalar Blasphemous CoopLocal

## Requisito previo (una sola vez)
Necesitas el **Modding de Blasphemous** (BepInEx + Blasphemous.ModdingAPI) ya instalado. Debe existir
`<carpeta del juego>/Modding/plugins/ModdingAPI.dll`. Todos los jugadores de la partida deben tener la misma versión del mod.

## Opción A — Setup de Windows (`BlasphemousCoopLocal_Setup.exe`)
Ejecútalo, comprueba la carpeta del juego (se detecta sola desde Steam) y pulsa Instalar. Se desinstala desde "Aplicaciones".

## Opción B — Script sin setup
- **Windows:** doble clic en `install.bat` (o `uninstall.bat` para quitarlo). Si no encuentra el juego te pedirá la carpeta.
  También: `powershell -ExecutionPolicy Bypass -File install.ps1 -GamePath "D:\Juegos\Blasphemous"`
- **Linux / Steam Deck:** `chmod +x install.sh && ./install.sh` (opciones: `--game-path /ruta`, `--uninstall`).
  Lee las bibliotecas de Steam (incluido Flatpak). El juego bajo Proton usa los mismos archivos del Modding.

## Opción C — Manual (sin ningún instalador)
1. Abre la carpeta del juego (Steam → Blasphemous → Administrar → Examinar archivos locales).
2. Copia **todo el contenido** de la carpeta `payload` dentro de la carpeta `Modding` del juego, aceptando mezclar/reemplazar.
   El resultado debe incluir `Modding/plugins/CoopLocal.dll`, `Modding/skulls/…`, `Modding/skull_base.png` y `Modding/lightning_frames.png`.
3. Inicia el juego.

## Configuración
Al primer arranque se crea `Modding/config/CoopLocal.cfg`. Para jugar normalmente deja `DebugMode = false`
(las herramientas de depuración —editores de interfaz, cambiador de teclado, cámara y administración— quedan apagadas
y no muestran textos). Ponlo en `true` para activarlas.

## Desinstalar
`uninstall.bat` / `./install.sh --uninstall`, o borra `Modding/plugins/CoopLocal.dll` y las carpetas/archivos del mod.
Tus partidas y ajustes no se tocan.
