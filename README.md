# Blasphemous CoopLocal

Mod cooperativo para **Blasphemous** (el primero) que permite jugar de **2 a 4 jugadores**, en local (varios mandos / teclado) o **en línea por Steam**. Inspirado en el mod multijugador de BrandenEK.

> Versión actual: **v1.0.1** · Plataforma: Windows y Linux / Steam Deck · BepInEx y el Modding API se instalan automáticamente.

---

## Instalación rápida

No necesitas instalar BepInEx ni nada más antes: **el instalador descarga e instala BepInEx y el Modding API automáticamente** (última versión oficial) si no están.

1. Descarga desde la [página de Releases](https://github.com/DavidAgr5421/Blasphemous.CoopLocal/releases):
   - **`BlasphemousCoopLocal_Setup.exe`** (Windows, recomendado): doble clic, elige la carpeta del juego (se detecta sola) e instala.
   - o **`BlasphemousCoopLocal-1.0.1.zip`** (sin setup): descomprime y ejecuta `install.bat` (Windows) o `./install.sh` (Linux / Steam Deck).
2. Inicia el juego.

Todos los jugadores de una partida en línea deben tener **la misma versión** del mod. Instalación manual y detalles: [installer/INSTALL.md](installer/INSTALL.md).

---

## Qué trae el mod

**Cooperativo de 2 a 4 jugadores**
- Jugadores adicionales (P2, P3, P4) con su propio Penitente, vida, fervor, inventario, habilidades y cámara compartida.
- Cada jugador puede usar **teclado o mando**; los controles se reasignan por jugador desde su menú de pausa.
- Menú de pausa individual por jugador, con ajustes de controles y de partida.
- Botón dedicado de **ataque a distancia** para los jugadores invitados.
- HUD de vida y fervor para cada jugador, skins seleccionables y nombre por jugador.

**Partidas en línea (Steam)**
- Un jugador crea la partida (lobby) y los demás se unen desde el menú **Unirse**.
- Sincronización de enemigos, jefes, daño, recompensas y prie-dieus.

**Mea Culpa elemental**
- Corazones Mea Culpa nuevos con efectos elementales: **sangrado, veneno, rayo y maldición**, con tres niveles de mejora cada uno.
- Árbol de habilidades propio con iconos nuevos, calaveras "umbral" por elemento y efectos visuales (aturdimiento eléctrico, chispas por golpe, explosiones de sangre y veneno).
- Mejoras permanentes de estadísticas por jugador.

**Calidad de vida**
- Menús del mod con estilo visual propio, escalado de texto y posiciones ajustables.
- Archivo de configuración global y registro de diagnóstico.

---

## Cómo se juega

1. Desde el menú principal (selección de ranura) abre la **configuración de partida** y elige el número de jugadores y el dispositivo (teclado / mando) de cada uno.
2. Para jugar en línea: el anfitrión marca puestos como **Online** (o abre la sala en plena partida, ver abajo); los demás pulsan **U** (o el botón de la pantalla) para **Unirse a partida online**. En ese menú: **Enter** sobre "Código de sala" para escribirlo (Enter otra vez para terminar), y luego **Conectar**. También puedes pegar con Ctrl+V.
3. Los puestos online deciden su **nombre y skin** (el anfitrión solo elige el color de su texto).
4. Pausa individual: cada jugador (P2-P4) abre su propio menú de pausa con su botón de pausa para cambiar controles.
5. **Menú del anfitrión (P1):** `Tab` en teclado, o `Start + Back` en mando. Permite **abrir/cerrar la sala online** en plena partida, **copiar el código de sala** y reasignar controles.
6. **F9 — cambiar teclado/mando:** intercambia el teclado y el mando entre los jugadores **locales** de esa PC (los puestos online no cuentan; con un solo jugador local no hace nada). Muestra un aviso arriba a la derecha que desaparece a los 5 s.

### Controles por defecto de los jugadores invitados (teclado)

| Acción | Tecla | | Acción | Tecla |
|---|---|---|---|---|
| Mover | W A S D | | Ataque | K |
| Salto | Espacio | | Dash | Shift izq. |
| Parry | J | | Curar | R |
| Interactuar | E | | Oración | Q |
| Ataque a distancia | U | | Menú / Pausa | I / Esc |

En mando: Ataque X, Salto A, Dash RT, Parry LB, Curar RB, Interactuar Y, Oración LT, A distancia B, Pausa Start. Todo es reasignable desde el menú de pausa del jugador.

---

## Configuración

Al primer arranque se crea **`Modding/config/CoopLocal.cfg`** (texto plano, `Clave = valor`):

| Clave | Por defecto | Qué hace |
|---|---|---|
| `DebugMode` | `false` | Interruptor maestro de las herramientas de depuración. Apagado = no hay teclas ni textos de depuración. |
| `UiEditors` | `true` | Editores de interfaz: F4 (tamaño de texto), F7 (HUD), F11 (posición/escala de menús). |
| `CameraChanger` | `true` | F10: cambia el objetivo de la cámara (y su texto en pantalla). |
| `AdminTools` | `true` | F2 jefes, F5 dummy, F6 fervor máximo, F8 menú debug, F10+2/3/4 conectar/desconectar jugadores. |

Para jugar normalmente deja `DebugMode = false`. Para desarrollar o calibrar, ponlo en `true`. Reinicia el juego tras editar el archivo.

---

## Desinstalar

`uninstall.bat` (Windows) · `./install.sh --uninstall` (Linux) · o borra `Modding/plugins/CoopLocal.dll`, `Modding/skulls/`, `Modding/skull_base.png` y `Modding/lightning_frames.png`. Tus partidas y ajustes no se modifican.

---

## Problemas frecuentes

- **El mod no aparece / no hay menú de coop:** comprueba que existe `Modding/plugins/CoopLocal.dll` y que el Modding de Blasphemous funciona sin este mod.
- **No puedo unirme en línea:** ambos jugadores necesitan Steam abierto y la misma versión del mod.
- **Algo falla:** revisa el log del Modding (`Modding/logs`) y abre un [issue](https://github.com/DavidAgr5421/Blasphemous.CoopLocal/issues) adjuntándolo.

---

## Compilar desde el código

Requisitos: .NET SDK, Blasphemous con el Modding instalado (el `.csproj` referencia `ModdingAPI.dll` de la carpeta del juego; ajusta la ruta si tu instalación es distinta).

```bash
dotnet build Blasphemous.CoopLocal.csproj -c Release
powershell -ExecutionPolicy Bypass -File installer/make-release.ps1 -Version 1.0.1   # genera dist/*.zip
```

Notas de arquitectura y decisiones: carpeta [Modding/](Modding/).

---

## Créditos

Basado en la idea del mod multijugador de **BrandenEK**. Blasphemous es propiedad de The Game Kitchen; este es un proyecto de fans sin afiliación oficial.
