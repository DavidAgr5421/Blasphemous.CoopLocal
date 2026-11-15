#!/usr/bin/env bash
# Instalador de Blasphemous CoopLocal para Linux / Steam Deck.
# Uso: ./install.sh [--game-path /ruta/a/Blasphemous] [--uninstall]
# Requisito: Modding de Blasphemous (BepInEx + ModdingAPI) ya instalado en la carpeta del juego.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PAYLOAD="$HERE/payload"
GAME=""; UNINSTALL=0
while [ $# -gt 0 ]; do
  case "$1" in
    --game-path) GAME="$2"; shift 2;;
    --uninstall) UNINSTALL=1; shift;;
    -h|--help) sed -n 2,5p "$0"; exit 0;;
    *) echo "Opcion desconocida: $1"; exit 1;;
  esac
done

is_game() { [ -n "${1:-}" ] && { [ -f "$1/Blasphemous.exe" ] || [ -f "$1/Blasphemous.x86_64" ] || [ -d "$1/Blasphemous_Data" ]; }; }

find_game() {
  local libs=("$HOME/.local/share/Steam" "$HOME/.steam/steam" "$HOME/.steam/root" \
              "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam" "/run/media/mmcblk0p1")
  local vdf p
  for root in "${libs[@]}"; do
    vdf="$root/steamapps/libraryfolders.vdf"
    [ -f "$vdf" ] && while IFS= read -r p; do libs+=("$p"); done < <(grep -oP '"path"\s+"\K[^"]+' "$vdf" || true)
  done
  for root in "${libs[@]}"; do
    if is_game "$root/steamapps/common/Blasphemous"; then echo "$root/steamapps/common/Blasphemous"; return 0; fi
  done
  return 1
}

if ! is_game "$GAME"; then GAME="$(find_game || true)"; fi
if ! is_game "$GAME"; then
  read -r -p "No encontre Blasphemous. Escribe la carpeta del juego: " GAME
fi
is_game "$GAME" || { echo "ERROR: carpeta invalida: $GAME"; exit 1; }
echo "Juego: $GAME"
MODDING="$GAME/Modding"

if [ "$UNINSTALL" = 1 ]; then
  rm -f "$MODDING/plugins/CoopLocal.dll" "$MODDING/lightning_frames.png" "$MODDING/skull_base.png"
  rm -rf "$MODDING/skulls"
  echo "CoopLocal desinstalado (se conserva config/CoopLocal.cfg y tus partidas)."
  exit 0
fi

[ -f "$MODDING/plugins/ModdingAPI.dll" ] || { echo "ERROR: falta el Modding de Blasphemous ($MODDING/plugins/ModdingAPI.dll). Instalalo primero."; exit 1; }
[ -d "$PAYLOAD" ] || { echo "ERROR: no encuentro la carpeta 'payload' junto al instalador."; exit 1; }
cp -r "$PAYLOAD"/. "$MODDING"/
echo "Instalado correctamente."
echo "Configuracion (se crea al primer arranque): $MODDING/config/CoopLocal.cfg"
echo "Si usas Proton, en Steam pon como opcion de lanzamiento (solo si tu Modding lo requiere):"
echo "  WINEDLLOVERRIDES=\"winhttp=n,b\" %command%"
