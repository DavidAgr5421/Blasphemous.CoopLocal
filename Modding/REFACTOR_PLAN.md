# Plan de refactor: contexto de jugador, rendimiento y calidad

Datos medidos (no estimados) sobre el código actual:
- Usos reales (sin comentarios) de `Core.Logic.Penitent`: ~110 líneas. Concentrados en: `Interactables.cs` 20, `BossDebugTeleportManager` 11, `CoopLocal` 8, `CinematicMovementSync` 7, `PlayerDeathFixes` 7, `BossTargetController` 6, `Parry` 5, `TestDummyManager` 5, `CoopClientWorldSync` 4. Uso directo de `CoopLocal.Player2/3/4`: ~60 (28 en `CoopLocal.cs`).
- Logger (`DashParryDebugLog.WriteLine`): en **cada** llamada hace `File.Exists` + `FileInfo` + `AppendAllText` (abrir/escribir/cerrar) en el hilo principal, y además duplica en `ModLog`. El log llegó a ~5 MB en una sesión corta. Es el mayor coste de rendimiento evidente.
- 52 parches Harmony sobre métodos vanilla de fotograma (`Update`/`OnUpdate`/`OnStateUpdate`...), 418 `GetComponent*`, 18 `FindObjectOf*`/`FindObjectsOfTypeAll`, ~42 usos de LINQ.

## Reglas de ejecución (todas las fases)
- Un lote = un archivo o un grupo pequeño; tras cada lote: `dotnet build -c Release` con **0 errores** y `cd Tests && dotnet test` en verde. Revisión del diff por el agente principal antes de seguir.
- Cambios mecánicos y sin cambio de comportamiento salvo que la fase diga lo contrario. Nada de refactor "ya que estamos".
- Todo lo que toque vanilla se verifica contra el decompilado (`ilspycmd`), no contra suposiciones.
- Los subagentes NO hacen commit, no tocan `Modding/NOTES.md` y no cambian la API de `CoopPlayerContext`; solo editan los archivos de su lote.

## Reparto por coste (agentes baratos para lo mecánico; el principal diseña y revisa)
| Nivel | Modelo | Para qué |
|---|---|---|
| Barato | `haiku` | Búsquedas/inventarios, sustituciones mecánicas con patrón exacto, comentarios XML, tests triviales. |
| Medio | `sonnet` | Migrar un archivo a `CoopPlayerContext`, cachear componentes, mover polling a temporizadores, escribir tests. |
| Principal | sesión actual | Diseño de API, parches con riesgo (Parry, muerte, red), revisión de diffs, decisiones. |
Los agentes de decompilación (`decompiler-modder`) solo cuando haya que confirmar un mecanismo vanilla.

## Fase 0 — Preparación (principal, ~1 sesión corta)
1. Commit por temas de lo acumulado (hoy ~70 archivos sin commit) para poder revertir por fase.
2. Ampliar `CoopPlayerContext` con lo que las fases siguientes necesitan: `Penitent`/`PlayerOf(Entity)`, `Others` (resto de jugadores vivos), `Position`, `IsInRoom`, helpers `ForEachActive`. **Una sola vez, aquí**, para que los lotes no la modifiquen.
3. Prueba en juego (tú): HUD P2-P4, corazones, F7/F4. Sin esto no se migra nada más.

## Fase 1 — Migrar el resto al contexto de jugador (≈110 + ≈60 usos)
Cada lote lo hace un agente `sonnet` con instrucciones fijas: sustituir `Core.Logic.Penitent` por el penitente del contexto correcto SOLO cuando el código actúa "en nombre de" un jugador concreto; dejar tal cual (con comentario) lo que de verdad es "el jugador local/P1" y los parches `PenitentSwap`.
| Lote | Archivos | Notas |
|---|---|---|
| 1A | `CoopPlayerRegistry`, `CoopLocal.cs` (28 usos de `Player2/3/4`) | Sustituir `CoopLocal.Player2..4` por `CoopPlayers.Get(i).Penitent`; mantener `SetPlayer`. Principal revisa. |
| 1B | `Abilities/Interactables.cs` (20), `PrieDieu*` | Interacciones: dueño = quien interactúa. |
| 1C | `Combat/PlayerDeathFixes`, `Damage`, `CombatOwnerFixes` | Alto riesgo (muerte/reaparición): **principal**, no delegar. |
| 1D | `Combat/BossTargetController`, `EnemyTargetingSystem`, `TestDummyManager`, `BossDebugTeleportManager` | Objetivos de enemigos: usar `CoopPlayers.Active`. |
| 1E | `Movement/*`, `Parry/Parry.cs`, `LadderMechanics` | Parry/Movement ya usan `PenitentSwap`; solo uniformar. Revisión del principal. |
| 1F | `Stats/*`, `Prayer/*`, `Cosmetics`, `MultiPlayer/CoopClientWorldSync`, `OnlinePlayerRewardSync` | Medio. |
Criterio de cierre: `grep Core.Logic.Penitent` solo queda en `PenitentSwap`, el contexto y casos documentados "P1/local".

## Fase 2 — Rendimiento (ordenado por ganancia/esfuerzo)
1. **Logger** (principal; el cambio más rentable): cola en memoria + escritura por lotes en un hilo (o `Update` cada 0,25 s), comprobar tamaño/rotación cada N líneas en vez de por llamada, no duplicar en `ModLog` salvo `Warn/Error`, y que `LogVerbose` evalúe el mensaje solo si está activo (sobrecarga con `Func<string>` o comprobar `VerboseEnabled` antes de interpolar). Test unitario de rotación/cola (el logger se aísla como `SwallowedExceptions`).
2. **Auditar logs en caminos de fotograma** (`haiku` inventaria, `sonnet` corrige): `AnimatorClipChangeLogger_Patch`, `EnemyTargetingSystem`, etc. Pasar a `LogVerbose` o rate-limit por clave (`LogOnce`/`SwallowedExceptions.ShouldLog`).
3. **Caché de componentes en parches de fotograma** (`sonnet`, por archivo): los 52 parches sobre `Update`/`OnUpdate`/`OnStateUpdate` → medir cuáles hacen `GetComponent*`/`Find*`/LINQ/`new List` por llamada (inventario con `haiku`); cachear en campo o `Dictionary<Penitent, X>` con limpieza al cambiar de sala.
4. **`FindObjectOfType`/`FindObjectsOfTypeAll`** (18): ya cacheados en HUD; el resto (`CoopEnemyScaling:374`, `EnemySyncWorld` ×2, fuentes TMP ×4, `CoopUiFontScale`) → cachear o resolver una vez por carga de sala (`OnLevelLoaded`).
5. **Sondeo → eventos**: `CoopGuestHeartEffects` y `CoopUiFontScaler` (propios) y `PermanentStatsDebugPanel`/`HudLayoutDebugEditor`: poner a dormir cuando su ventana no está abierta / no hay cambios; engancharse a "equipo cambiado" y "sala cargada".
6. **LINQ y asignaciones** (42 usos LINQ): solo en caminos de fotograma/eventos de combate; el resto se deja.
7. Medición: contador simple de ms por sistema (`Stopwatch` en modo verbose) para comprobar antes/después en lugar de adivinar.

## Fase 3 — Calidad
1. Dividir con `partial` los tres archivos de una sola clase (`SwordElementalUpgrades` 2.400, `CoopGameSetupUI` 4.200, `CoopMatchConfigUI` 1.400): por región ya existente (datos / sprites / UI / lógica). Mecánico → `sonnet`, revisión de compilación.
2. Config común y menú de depuración único (items 8 del plan anterior) — diseño del principal, implementación `sonnet`.
3. HUD de P2-P4 por partes explícitas: solo si el clon con lista blanca da problemas en pruebas (datos en `Modding/NOTES.md`).
4. Tests: extraer lógica pura (escalado de enemigos, mapeo de slots, parseo de config, cola del logger) y cubrirla. `sonnet`.
5. Documentación: actualizar `ARCHITECTURE.md` al cerrar cada fase (`haiku`).

## Orden recomendado
Fase 0 → Fase 2.1/2.2 (logger: mayor ganancia, bajo riesgo) → Fase 1 (lotes 1A, 1B, 1D, 1F; el principal hace 1C y 1E) → Fase 2.3-2.6 → Fase 3.
Paralelismo: los lotes de Fase 1 tocan archivos distintos, así que pueden ir en paralelo siempre que ningún lote comparta archivo; el build final lo corre el principal.
