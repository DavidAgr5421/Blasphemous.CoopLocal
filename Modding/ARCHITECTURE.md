# Blasphemous.CoopLocal — arquitectura

Mod BepInEx/ModdingAPI (net35, Harmony) que añade cooperativo local y online (Steam P2P) de 2-4 jugadores. `NOTES.md` es el diario
de rondas; **este archivo es la referencia**: dónde vive cada sistema.

## Reglas del código (léelas antes de añadir algo)
1. **Un jugador = un `CoopPlayerContext`** (`CoopPlayers.Get(i)` / `CoopPlayers.Of(penitent)`). No ramifiques por "si es P1 ... si no Player2";
   pregunta al contexto (equipo, stats, datos, HUD).
2. **Vanilla solo conoce a P1** (`Core.Logic.Penitent`, `InventoryManager`). Para ejecutar código vanilla como otro jugador usa
   `PenitentSwap.Begin/End` (restaurar en `Finalizer`, nunca en `Postfix`).
3. **Consultas de objetos equipados** (`IsXEquipped`): se resuelven por contexto de llamada (`EquipContext`): dueño / cualquiera / P1.
   Un consumidor nuevo debe elegir contexto explícitamente.
4. **Reflexión sobre el juego**: siempre `Reflect.Field/Method/Property` (no `AccessTools` directo). Registra una vez lo que falte.
5. **No más `catch { }` mudos**: `catch (Exception e) { SwallowedExceptions.Report(e, "Archivo:línea"); }`.
6. **Verifica contra el decompilado** (`ilspycmd Assembly-CSharp.dll`) antes de dar por buena una hipótesis sobre código vanilla.

## Mapa de sistemas
| Carpeta / archivo | Qué es |
|---|---|
| `CoopLocal.cs` | Punto de entrada del mod (`BlasMod`): ciclo de vida, spawn/limpieza de P2-P4, reconstrucción del HUD. `CoopLocalHelpers.cs`: componentes auxiliares. |
| `MultiPlayer/CoopPlayerContext.cs` | Vista unificada de un jugador (penitente, stats, datos, equipo, HUD). `CoopPlayerRegistry.cs`: slots, nombres, colores. |
| `MultiPlayer/PenitentSwap.cs` | Cambio temporal de `Core.Logic.Penitent`. |
| `MultiPlayer/CoopGameConfig.cs` | Config global (`coop_game_settings.cfg`): nº jugadores, árbol elemental, defaults horneados. |
| `MultiPlayer/CoopSaveManager.cs` | Persistencia por ranura (`PlayerCoopData`: skills, inventario propio, stats de P2-P4). |
| `MultiPlayer/CoopGameSetupUI.cs`, `CoopMatchConfigUI.cs`, `CoopJoinLobbyUI.cs`, `CoopUiStyle.cs` | Menús pre-partida / config de partida / unirse (uGUI). `CoopUiFontScale.cs`: escala de texto (F4). |
| `MultiPlayer/CoopNetwork*.cs`, `CoopLobbyManager.cs`, `EnemySync*.cs`, `CoopClientWorldSync.cs` | Online Steam P2P y sincronización de enemigos/mundo. Spec: `ENEMY_SYNC_SPEC.md`. |
| `Input/` | `Player2Input` (entrada P2-P4), `Player2Pad`/`Player2Keys` (mandos/teclado), `CoopControlsConfig` (bindings). |
| `Abilities/`, `Movement/`, `Dash/`, `Parry/`, `Camera/` | Parches que corrigen habilidades/movimiento/parry/cámara para guests (vanilla hardcodea P1). |
| `Combat/` | Daño, muerte, escalado de enemigos (`CoopEnemyScaling`), jefes, y el sistema elemental (`SwordElementalUpgrades` + `SwordElemental*`, `EnemyElementalStatusController`, VFX). `SwordHeartPerPlayer.cs`: corazones de Mea Culpa por jugador. |
| `Stats/` | Inventario y habilidades de guests (`Player2InventoryManager`, `Player2SkillManager`), `InventoryGameplayPatches` (`EquipContext`), menús de P2-P4, debug F8. |
| `Prayer/` | Rezos de guests. |
| `HUD/` | Barras de vida/fervor/monedas de P2-P4 (clones del HUD vanilla), layout (`HudLayoutConfig` → `coop_hud_layout.json`), editor F7, `HudCloneCleaner`. |
| `Diagnostics/` | `DashParryDebugLog` (log), `Reflect`, `SwallowedExceptions`, `PatchSelfTest`. |
| `Tests/` | Pruebas xUnit (net8) de lógica pura enlazada sin el juego: `cd Tests && dotnet test`. |

## Archivos de datos del jugador (LocalLow/TheGameKitchen/Blasphemous/CoopLocalMod)
`coop_game_settings.cfg` (clave=valor) · `coop_hud_layout.json` · `coop_ui_fonts.cfg` · `coop_savedata_slot{N}.txt`.
Los valores calibrados se "hornean" copiándolos a los defaults del código (`CoopGameConfig.Default*`, `HudLayoutConfig`, `CoopUiFontSettings.Defaults`).

## Teclas de depuración
F2 jefes · F4 tamaño de texto de menús · F7 editor de HUD/árbol · F8 menú debug (Shift+F8 panel antiguo).

## Deuda conocida (prioridad)
- Archivos grandes: `SwordElementalUpgrades.cs` (clase estática de ~2.400 líneas), `CoopGameSetupUI.cs`, `CoopMatchConfigUI.cs` — una sola clase; dividir exige `partial`.
- El HUD de P2-P4 clona el contenedor vanilla (frágil); reemplazar por partes explícitas.
- Muchos `Update()` con sondeo; mover a temporizadores/eventos.
- Config repartida en tres formatos y editores de depuración sin estructura común.
- ~150 usos de `Core.Logic.Penitent` por migrar al contexto de jugador.
