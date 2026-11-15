#!/usr/bin/env bash
# Instalador de Blasphemous CoopLocal para Linux / Steam Deck.
# Uso: ./install.sh [--game-path /ruta/a/Blasphemous] [--uninstall] [--update-deps]
# Si falta BepInEx / Modding API los descarga solos de sus repositorios oficiales. Requiere curl o wget, y unzip.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PAYLOAD="$HERE/payload"
GAME=""; UNINSTALL=0; UPDATE=0
while [ $# -gt 0 ]; do
  case "$1" in
    --game-path) GAME="$2"; shift 2;;
    --uninstall) UNINSTALL=1; shift;;
    --update-deps) UPDATE=1; shift;;
    -h|--help) sed -n 2,4p "$0"; exit 0;;
    *) echo "Opcion desconocida: $1"; exit 1;;
  esac
done

TOOLS_URL="https://github.com/BrandenEK/Blasphemous.ModdingTools/raw/main/linux64.zip"
API_URL="https://github.com/BrandenEK/Blasphemous.ModdingAPI/releases/latest/download/ModdingAPI.zip"

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

fetch() { if command -v curl >/dev/null; then curl -fsSL "$1" -o "$2"; else wget -q "$1" -O "$2"; fi; }

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
  echo "CoopLocal desinstalado (se conserva config/CoopLocal.cfg, BepInEx y tus partidas)."
  exit 0
fi

command -v unzip >/dev/null || { echo "ERROR: instala 'unzip' (sudo apt install unzip / sudo pacman -S unzip)."; exit 1; }
[ -d "$PAYLOAD" ] || { echo "ERROR: no encuentro la carpeta 'payload' junto al instalador."; exit 1; }
TMP="$(mktemp -d)"; trap 'rm -rf "$TMP"' EXIT

if [ ! -f "$GAME/BepInEx/core/BepInEx.dll" ] || [ "$UPDATE" = 1 ]; then
  echo "Instalando BepInEx (Modding Tools oficiales)..."
  fetch "$TOOLS_URL" "$TMP/tools.zip"; unzip -qo "$TMP/tools.zip" -d "$GAME"
  chmod +x "$GAME/start_blas1_modded.sh" 2>/dev/null || true
else
  echo "BepInEx ya instalado."
fi
if [ ! -f "$MODDING/plugins/ModdingAPI.dll" ] || [ "$UPDATE" = 1 ]; then
  echo "Instalando Modding API (ultima version)..."
  mkdir -p "$MODDING"; fetch "$API_URL" "$TMP/api.zip"; unzip -qo "$TMP/api.zip" -d "$MODDING"
else
  echo "Modding API ya instalado."
fi
[ -f "$MODDING/plugins/ModdingAPI.dll" ] || { echo "ERROR: no se pudo instalar el Modding API."; exit 1; }

cp -r "$PAYLOAD"/. "$MODDING"/
echo "Instalado correctamente."
echo "Configuracion (se crea al primer arranque): $MODDING/config/CoopLocal.cfg"
echo
echo "IMPORTANTE (Linux): en Steam -> Blasphemous -> Propiedades -> Opciones de lanzamiento, pon:"
echo "  ./start_blas1_modded.sh %command%"
echo "(Si juegas la version Windows con Proton usa en su lugar: WINEDLLOVERRIDES=\"winhttp=n,b\" %command%)"
