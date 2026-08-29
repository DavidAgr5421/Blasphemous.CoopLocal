# Blasphemous.CoopLocal - Notas de desarrollo

Este archivo documenta el progreso del mod: qué se arregló, por qué fallaba de verdad
(no solo el síntoma), y qué queda pendiente. Los comentarios en el código ya usan la
convención "Round N" (ronda N) para referenciar cambios puntuales - este archivo es el
índice legible de esas rondas, y donde se explica el panorama completo cuando una ronda
por sí sola no alcanza a contarlo.

No existía en este entorno hasta 2026-08-29 (se creó recién en esta sesión, recopilando
las rondas ya presentes en el código desde antes). Si en algún momento aparece otra copia
de este archivo en otra máquina/ubicación, hay que fusionar el historial a mano - este es
ahora el autoritativo para este repo.

## Cómo se compila y despliega

```
dotnet build -p:SolutionDir="C:\Users\USUARIO\OneDrive\Documentos\BlasphemousCoop\Blasphemous.CoopLocal"
```

El `.csproj` copia automáticamente el DLL resultante a
`C:\Program Files (x86)\Steam\steamapps\common\Blasphemous\Modding\plugins\CoopLocal.dll`
(target `Development`) - no hace falta ningún paso manual de instalación tras compilar.

## Patrones de bug recurrentes (leer antes de tocar cualquier P2-fix)

Esta sesión encontró el mismo puñado de causas raíz una y otra vez, disfrazadas de
síntomas distintos. Vale la pena chequear estas primero ante cualquier "P1 hace X bien,
P2 no":

1. **Familia 1 - lazy-init de `_penitent` cae a `Core.Logic.Penitent` (P1).** Muchos
   `StateMachineBehaviour` de animación tienen `if (_penitent == null) { _penitent =
   Core.Logic.Penitent; }`. Si el animator de P2 entra a ese estado, el campo queda
   fijado a P1 para siempre. Fix estándar: `Prefix` sobre `OnStateEnter` que reasigna
   `_penitent` al owner real vía `animator.GetComponentInParent<Penitent>()` (o
   equivalente) ANTES de que corra el chequeo null. Ojo con el "bundled-init trap": si el
   mismo `if` inicializa además un *campo de tipo referencia* (no solo `_penitent`), hay
   que reasignar ambos o el segundo queda apuntando a datos de P1.
2. **Familia 2 - input leído directo de Rewired Player 0 compartido**, sin pasar por
   `Player2Input`. No es un problema de "a quién se aplica" sino de "qué valor se lee" -
   el mismo objeto/instancia de P2 termina leyendo el botón físico de P1. Fix estándar:
   Postfix o Transpiler que sobreescribe el campo/backing-field correspondiente con el
   valor real de `Player2Input` después de que corra el código vanilla.
3. **El "disparo" funciona pero el "efecto" sigue atado a P1.** Combinación de las dos
   anteriores: el evento/acción se ejecuta correctamente por-instancia para P2, pero algo
   más abajo en la cadena (otro `StateMachineBehaviour`, otro método) que debería ajustar
   el estado/hitbox/collider de "quien disparó" sigue resolviendo a P1 porque nadie lo
   había parcheado todavía. Un log confirmando "el trigger sí se disparó para P2" no
   alcanza para cerrar un bug - hay que verificar el efecto físico/visual real.
4. **Timing entre el Postfix del mod y el propio método vanilla.** Vanilla a veces toma
   una decisión de "un solo disparo" (ej. arrancar una corrutina) usando datos que el
   Postfix del mod corrige *después* de que la decisión ya se tomó ese frame. En esos
   casos no alcanza con corregir el input - hay que reimplementar el gate completo del
   lado del mod, usando datos ya corregidos, en vez de confiar en que el Postfix llegue a
   tiempo.
5. **Un Postfix corriendo incondicionalmente cuando debía ser condicional al toucher.**
   Visto en la primera versión (rota) de los patches de mejoras permanentes: el Postfix
   aplicaba el efecto a P2 incluso cuando quien disparó la acción había sido P1, porque no
   había guarda que confirmara "esta instancia de Fsm específica fue marcada como P2" antes
   de actuar.

Regla general confirmada repetidas veces: **verificar contra el decompilado real antes de
aplicar un fix, aunque el diagnóstico venga de una sesión/fuente previa que suene
razonable.** Varios diagnósticos "correctos en la familia, incorrectos en el mecanismo
exacto" llegaron a esta sesión y habrían producido fixes que no arreglaban nada (o rompían
algo) si se hubieran aplicado a ciegas.

## Historial por ronda (resumen)

- **Ronda 48** - P2 no podía escalar escaleras a menos que P1 también sostuviera
  arriba/abajo (el Postfix de input solo sobreescribía Left/Right/Jump, no Up/Down).
  También: offset de spawn de P2 tras transición de sala causaba que a veces apareciera
  "hacia atrás" en el cruce.
- **Ronda 49** - Hitbox visible de P2 (`Player2HitboxVisualizer`) desactivado (era una
  herramienta de debug de una ronda anterior, no debía llegar visible al jugador). Un
  solo `EnsureCreated(Player2)` comentado en `CoopLocal.cs` - la clase queda intacta,
  reversible con una línea.
- **Ronda 50** - Varios `StateMachineBehaviour` con el mismo bug de familia 1
  (`AirUpwardAttackBehaviour`, `FallingOverBehaviour`, `GroundingOverBehaviour`), y un
  fix del tracker de contacto/daño que pasaba de Postfix a Prefix para evitar una ventana
  donde "nadie parecía estar tocando" al enemigo.
- **Ronda 51** - Ataque cargado de P2 se disparaba con el botón de ataque de P1
  sostenido. Causa real: `IsAttackButtonHold` se calculaba dentro de
  `PlatformCharacterInput.Update()` vanilla leyendo Rewired Player 0 compartido (familia
  2) - el patch existente `ManyPlayerAnimationBehaviours_PenitentOwnerFix_Patch` (familia
  1) era correcto pero no cubría esto. Fix en `Movement/Movement.cs`.
- **Ronda 52** - P2 no podía bajar de plataformas de un solo sentido (drop-through).
  Causa real: el gate de vanilla (`PlatformCharacterInput.Update()`) toma la decisión de
  arrancar la corrutina `JumpOff()` usando Rewired Player 0 compartido, *antes* de que el
  Postfix del mod corrija el input de P2 ese mismo frame (familia 4) - reimplementado el
  gate + temporizador completo para P2 en `Movement/Movement.cs`. También: agarre de
  escalera desde el aire - la causa real vivía en
  `CreativeSpore.SmartColliders.PlatformCharacterController.DoClimbing()` (plugin de
  terceros), no en `GrabLadder.OnUpdate()` como sugería el diagnóstico original (fix vía
  Transpiler en `Movement/LadderMechanics.cs`). Panel de debug F10 para ciclar a qué
  jugador(es) sigue la cámara (`Camera/Camera.cs`).
- **Ronda 53** - Investigación de hitbox de ataque de P2 (`AttackArea`) apareciendo lejos
  de P2 sin estar atacando, y de un gap ocasional en el tracker de contacto. Fixes de
  cámara: reposicionamiento incorrecto al cambiar de modo de target a mitad de sala.
- **Ronda 54** - `SetLayerRecursively` reemplazado por copia per-nodo de capas (P1→P2):
  un valor de capa único aplicado a toda la jerarquía de P2 pisaba silenciosamente hijos
  que en el prefab original están deliberadamente en una capa distinta a la raíz (ej.
  "Attack Area" en capa "Water").
- **Ronda 55** - Persistencia de stats de P2 entre salas rota de raíz: P2 se instanciaba
  sin `DontDestroyOnLoad`, así que en una transición de sala real Unity lo destruía
  *antes* de que el mod alcanzara a guardar sus vitals - el guardado nunca corría para
  esas transiciones. Fix: `Object.DontDestroyOnLoad(Player2.gameObject)` en
  `CoopLocal.OnPlayerSpawn`. También: reposicionamiento de cámara en modo coop normal
  (no solo en el debug F10).
- **Ronda 56** - Fade del HUD de P2 en transiciones de sala: el HUD de P1 no se
  desvanece realmente, queda oculto detrás del panel negro de carga (mismo Canvas raíz);
  el HUD custom de P2 no tenía esa noción. Fix: `HUD/Player2HudFadeSync.cs`, suscrito a
  los eventos reales de `FadeWidget` (`OnFadeShowStart`/`OnFadeHidedEnd`).
- **Ronda 57** - Fundido suave (no pop binario) para el show/hide del HUD de P2, tanto en
  transición de sala como en su primera creación (`HUD/HudFade.cs`, vía `DOTween.To` sobre
  `CanvasGroup.alpha`, ya que el juego no trae `DOTweenModuleUI`). Platform drop-through:
  el trigger se disparaba pero el hitbox de P2 nunca se ajustaba durante la pose de salto
  - causa real: `JumpOffBehaviour` (familia 1, nunca parcheado) resolvía `_penitent` a P1,
  así que el encogimiento del `SmartPlatformCollider` se aplicaba sobre P1. Fix en
  `Movement/Movement.cs`. Mejoras permanentes de P2 independientes de P1
  (`Stats/Player2UpgradeCredit.cs`, 7 Harmony Prefix sobre las acciones de PlayMaker
  `LifeUpgrade/StrengthUpgrade/BeadUpgrade/FlaskHealthUpgrade/FervourUpgrade/
  MeaCulpaUpgrade/FlaskAdd`, que hardcodeaban `Core.Logic.Penitent`) + persistencia vía
  `Player2StatsSync.PersistPermanentBonus`. **Nota**: la primera versión de este archivo
  no compilaba (21 errores) y tenía bugs semánticos serios (Postfix incondicional que
  duplicaba mejoras hacia P2 aunque las tocara P1 solo, NRE si P2 no había spawneado,
  reflection rota sobre tipos 3D en un juego 2D) - se reescribió por completo antes de dar
  la ronda por cerrada. Panel de debug F8 (`Stats/PermanentStatsDebugPanel.cs`) para ver y
  ajustar en vivo el `PermanetBonus` (typo real del juego) de P1/P2 por separado, sin
  depender de tocar un altar real.
- **Ronda 58** - P2 a veces saltaba en vez de bajar de la plataforma al soltar Down+Jump
  juntos: el patch de P2 seteaba `Jump` sin excluir `crouch`, a diferencia de vanilla
  (`!IsJoystickDown()` en su propia condición) - competían por el mismo frame. Fix:
  `SetActionState(Jump, jump && !crouch)`. También se arregló un log de diagnóstico que
  nunca lograba loguear `True` porque leía el estado del gate de drop-through *después*
  de que el propio método vanilla ya lo hubiera mutado (Postfix → Prefix).

- **Ronda 59** - la primera entrega de `Stats/PermanentStatsDebugPanel.cs` (panel F8 de la
  ronda 57) no compilaba: llaves desbalanceadas dejaban métodos (`DrawWindow`,
  `DrawWindowContent`) sueltos fuera de cualquier clase, y aunque hubiera compilado nunca
  se conectaba a un `OnGUI()` real (nadie llamaba a esos métodos) - el panel jamás se
  habría dibujado. También usaba una firma inventada de `GUILayout.WindowRect` (no
  existe). Reescrito por completo siguiendo el spec original al pie de la letra. Pendiente
  de que el usuario confirme en juego que F8 abre/cierra el panel sin chocar con F9/F10, y
  que los botones +1/-1/Reset/Guardar baseline realmente mueven y persisten el valor.

- **Ronda 60** - Investigación (sin patches todavía, solo verificación + diseño) del Skill Tree
  (árbol de habilidades) para hacerlo independiente P1/P2. Verificado contra el decompilado real:
  `Framework.Managers.SkillManager` (no `Gameplay.GameControllers.Penitent.Abilities` como se había
  asumido) es un singleton `Core.SkillManager` con un solo `Dictionary<string, UnlockableSkill>
  allSkills` global - `IsSkillUnlocked/CanUnlockSkill/UnlockSkill/LockSkill/GetCurrentMeaCulpa/
  GetPurgePoints` todos hardcodean `Core.Logic.Penitent.Stats.*` (confirmado línea por línea,
  `SkillManager.cs:130,141,164,182,187,192`) - correcto tal cual lo describió el usuario.
  `Framework.FrameworkCore.Ability` (no en el namespace de Abilities - esa clase base vive en
  FrameworkCore) sí resuelve `EntityOwner` correctamente por instancia (`ReloadOwner()` en `Awake()`
  vía `GetComponentInParent<Entity>()`, confirmado, sin el patrón lazy-init de familia 1). El único
  punto de lectura real para el gating de skill es `Ability.GetLastUnlockedSkill()` (protected,
  recorre el campo privado `unlocableSkill` llamando a `Core.SkillManager.IsSkillUnlocked` por
  cada id) - `CanExecuteSkilledAbility()` se construye enteramente sobre ese método, así que un
  único patch ahí (no en `SkillManager.IsSkillUnlocked`, que no recibe owner) alcanza para las 5
  subclases (`Combo/ChargedAttack/LungeAttack/RangeAttack/VerticalAttack`, todas en
  `Gameplay.GameControllers.Penitent.Abilities`, confirmado). **Corrección importante al análisis
  del usuario**: `UnlockSkill` NO se dispara vía `Fsm`/altar físico como las mejoras permanentes de
  la Ronda 57 - se dispara desde `Gameplay.UI.Others.MenuLogic.NewInventory_LayoutSkill.Update()`,
  una pantalla de menú (Confesor) única y compartida, sin ningún parámetro/contexto de jugador en
  toda la cadena (confirmado también en `NewInventory_Skill.SetFocus`/`NewInventory_Description`) -
  reusar `Fsm.TriggerCollider2D` (como propuso el usuario) no aplica, no hay colisión física
  involucrada. Ya existe una forma de que P2 abra este mismo menú compartido
  (`Player2Input.MenuDown` -> `UIController.instance.ToggleInventoryMenu()`, ver
  `Input/Player2Input.cs:450-453`, con su propio comentario explícito: "no hay noción per-player").
  **Hallazgo colateral no pedido, no arreglado, marcado para más adelante**: `RangeAttack.OnUpdate/
  OnStart/OnCastStart` (`Gameplay.GameControllers.Penitent.Abilities.RangeAttack`) hardcodea
  `Core.Logic.Penitent` en casi todo el método (grounded/climbing state, botón 57 vía
  `Core.Logic.Penitent.PlatformCharacterInput.Rewired`, `Core.Logic.Penitent.Dash.StopCast()`) -
  como `Ability.OnUpdate()` SÍ corre para P2 (`Ability_UpdateInput_Patch` en
  `Combat/ContactDamage.cs` solo bloquea `UpdateInput()`, no `OnUpdate()`), el RangeAttack de P2
  hoy reacciona al estado/input de P1, no al propio - esto es independiente del Skill Tree pero
  bloquea que "RANGED_1/2/3" tenga sentido para P2 hasta que se arregle. `VerticalAttack.OnUpdate`
  tiene un gap menor similar: un solo `_rewired.GetButtonTimedPress("Attack", ...)` leído del
  Rewired compartido (el resto de VerticalAttack sí resuelve owner correctamente por instancia).
  Ningún patch de este hallazgo se escribió esta ronda - solo diagnóstico.

- **Ronda 61** - Skill Tree independiente P1/P2 (implementado, sin playtest del flujo real de
  compra). Verificado en Ronda 60 que el único gating es `Ability.GetLastUnlockedSkill()` sobre
  `SkillManager` global. Implementado: (1) `Stats/Player2SkillManager.cs` shadow
  `Dictionary<string,bool>` para los 15 ids de `UnlockableSkillId` con persistencia
  `p2_skills_slot{slot}.txt` bajo `Application.persistentDataPath/CoopLocalMod/` (SnapshotPath/
  SaveForSlot/LoadForSlot idéntico a `Player2StatsSync`, cargado desde `CoopLocal.OnPlayerSpawn`);
  (2) único `HarmonyPrefix` sobre `Framework.FrameworkCore.Ability.GetLastUnlockedSkill()` (protected,
  campo privado `unlocableSkill` inyectado como `____unlocableSkill`): si
  `EntityOwner != CoopLocal.Player2` deja pasar vanilla, si es P2 recorre la lista consultando
  `Player2SkillManager.IsUnlocked` y resuelve la definición vía `Core.SkillManager.GetSkill(id)`
  - cubre automáticamente `CanExecuteSkilledAbility()` y las 5 subclases
  (`Combo/ChargedAttack/LungeAttack/RangeAttack/VerticalAttack`) sin tocarlas una por una;
  (3) `Stats/PermanentStatsDebugPanel.cs` extendido con sección Skill Tree 15 filas x P1|P2:
  P1 llama `Core.SkillManager.UnlockSkill(id, ignoreChecks:true)/LockSkill(id)`, P2 llama
  `Player2SkillManager.SetUnlocked(id,bool)` + botón explícito `Guardar skills P2 ahora` (`Persist()`);
  verificado por inspección y compilación (dotnet build 0 errores) que el toggle de P2 sombrea el
  bool y que `ChargedAttack/Combo/LungeAttack` de P2 leen el tier sombreado; `RangeAttack` sigue
  roto por hardcodeo `Core.Logic.Penitent` (ver punto 5 de tarea, no tocado aquí) así que su tier
  no tendrá efecto aunque el skill esté marcado ON para P2 hasta que se reimplemente
  `RangeAttack.OnUpdate/OnStart/OnCastStart`; `VerticalAttack` solo necesita fix menor de
  `_rewired.GetButtonTimedPress` compartido. **Pendiente de playtest en juego**: confirmar que
  abrir/cerrar F8 no choca con F9/F10, que togglear P2 cambia el comportamiento de
  `ChargedAttack` (1.5s vs 0.75s y proyectil `CHARGED_3`/Fervour) y `Combo` (ventana +3/+6 y
  `COMBO_3` finisher) de P2 de forma independiente de P1, y que el archivo `p2_skills` persiste
  entre respawns/cambios de sala. **No tocado** (a propósito): flujo de compra/costo real en
  `NewInventory_LayoutSkill` (Confesor) - necesita decidir "para quién está abierto el árbol"
  sin toucher físico; recomendado toggle explícito P1/P2 en esa pantalla como paso aparte.

- **Ronda 62** - 5 bugs reportados tras el playtest del shadow de Skill Tree (Ronda 61). Diagnostico
  externo aportado por el usuario tratado como hipotesis, verificado uno por uno contra el
  decompilado real y contra `BepInEx/LogOutput.log` antes de tocar nada (metodologia del inicio de
  este archivo) - resultado: 3 de 5 causas raiz coincidian con el diagnostico externo (con detalles
  distintos en 1 de ellas), 2 eran mecanismos nuevos no cubiertos por ese diagnostico.

  1-2. **P1 filtra hacia P2 / P2 no puede usar su propia skill** - el diagnostico externo acerto la
     familia y el archivo (`Ability_GetLastUnlockedSkill_P2_Patch`,
     `Stats/Player2SkillManager.cs`) pero erro el detalle exacto: dijo "Harmony espera 3 guiones,
     aca hay 4" sin confirmar el nombre real del campo. Confirmado via decompilado
     (`Framework.FrameworkCore.Ability`): el campo real es `private List<string> unlocableSkill;`
     - **sin guion bajo propio**. La convencion de reversed-field de Harmony es siempre
     "3 guiones + nombre exacto del campo tal cual esta declarado" - los `____penitent` (4
     guiones) usados en el resto del repo son 3 + el propio guion inicial de `_penitent`
     (confirmado contra `Dash.cs:39`: `private Penitent _penitent;`), no una regla especial de 4.
     Con `____unlocableSkill` (4 guiones), Harmony buscaba un campo `_unlocableSkill` inexistente.
     Confirmado en runtime real via `BepInEx/LogOutput.log` de una sesion previa del usuario:
     `[Error: HarmonyX] Failed to patch ... GetLastUnlockedSkill(): ArgumentException: No such
     field defined in class Framework.FrameworkCore.Ability / Parameter name: _unlocableSkill`.
     Dato nuevo no cubierto por el diagnostico externo: este proyecto usa **HarmonyX** (parcheo via
     ILHook/Mono.Cecil, no el `Lib.Harmony` clasico basado en DynamicMethod) - `HarmonyX.
     PatchClassProcessor.ProcessPatchJob` atrapa la excepcion de patch fallido *por patch
     individual* y sigue con el resto (confirmado en el mismo log: el resto de patches de la
     sesion seguian aplicandose despues de esta falla) - por eso el mod no crasheaba entero, solo
     este patch en particular nunca se aplicaba: `Ability.GetLastUnlockedSkill()` corria 100%
     vanilla para P1 y P2 por igual, ambos leyendo el mismo `Core.SkillManager` global - explica
     exactamente los dos sintomas (P2 heredaba lo que P1 tuviera desbloqueado globalmente, y
     togglear P2 en el panel F8 no tenia ningun efecto porque el shadow dict nunca se consultaba).
     Fix: `List<string> ___unlocableSkill` (3 guiones) en `Stats/Player2SkillManager.cs`.

  3. **Vertical Attack no funciona en absoluto para P2** - confirmado family 2 (input compartido),
     tal como preveia el diagnostico externo, pero el detalle era mas grave de lo que sugeria
     ("gap menor"): `VerticalAttack.OnUpdate()` (`Gameplay.GameControllers.Penitent.Abilities.
     VerticalAttack`) resuelve su owner correctamente en todo el metodo salvo por
     `_rewired.GetButtonTimedPress("Attack", AttackButtonHoldTime)`, donde `_rewired =
     ReInput.players.GetPlayer(0)` (Rewired Player 0 compartido) - sin este gate en true, el
     Vertical Attack de P2 **nunca** entra en carga sin importar el input propio de P2, solo
     reacciona a que P1 sostenga fisicamente el boton de ataque real en el aire. Se sumaba ademas
     al bug 1-2 (aun si el gate de input hubiese andado, el gating de skill tampoco funcionaba).
     Fix: `Abilities/RangedAndVerticalAttackFixes.cs`,
     `VerticalAttack_OnUpdate_P2_TimedPress_Patch` - mismo patron de Transpiler puntual ya usado en
     `Movement/LadderMechanics.cs` (`PlatformCharacterController_DoClimbing_P2_AirGrab_Patch`):
     retarga solo la llamada a `Player.GetButtonTimedPress(string,float)` a un wrapper que, para
     P2, reimplementa el mismo timer de "sostener N segundos" que ya usa Round 51
     (`player2AttackHoldTimer` en `Movement/Movement.cs`) pero con el umbral propio de
     `VerticalAttack.AttackButtonHoldTime` en vez de `PlatformCharacterInput.
     timeInputAttackHold`. El resto del metodo (~80 lineas, varias ramas de animator state) queda
     intacto para P1 y P2 - no valia la pena reimplementarlo entero por una sola lectura rota.

  5. **RangeAttack solo reacciona/aplica efecto cuando P1 lo dispara** - confirmado, y bastante mas
     extenso que lo que documentaba el hallazgo colateral de la Ronda 60: la clase entera
     (`Gameplay.GameControllers.Penitent.Abilities.RangeAttack`) hardcodea `Core.Logic.Penitent`
     en `OnStart` (`_rootMotion`), `OnUpdate` (el `Penitent penitent = Core.Logic.Penitent` usado
     para decidir si cancelar el ataque, ademas del boton 57 compartido), `CastRangeAttack` (rama
     aerea via `GroundDist`), `OnCastStart` (`Dash.StopCast()`) e `InstanceProjectile` (altura Y
     del proyectil via `DamageArea`) - el RangeAttack de P2 en la practica reaccionaba al estado
     fisico de P1, no al propio. Reimplementado metodo por metodo en
     `Abilities/RangedAndVerticalAttackFixes.cs` (`RangeAttack_OnStart_P2_Patch` Postfix,
     `RangeAttack_OnUpdate_P2_Patch` y `RangeAttack_CastRangeAttack_P2_Patch` Prefix + reflection
     hacia los metodos/campos privados reales via `AccessTools`, `RangeAttack_OnCastStart_P2_Patch`
     Postfix, `RangeAttack_InstanceProjectile_P2_Patch` Prefix), gate `EntityOwner !=
     CoopLocal.Player2 => return true` en cada uno, P1 sin tocar. **Incertidumbre real, no
     resuelta sin playtest**: el boton "57" que lee `_rewired.GetButtonDown/Up(57)` no tiene
     nombre visible en el decompilado (Rewired action id numerico, distinto del boton 5=Attack/
     7=Dash ya documentados en Round 51) - se sustituyo por `Player2Input.AttackDown/AttackUp`
     como hipotesis mas plausible (mismo gesto sostener-y-soltar que ya usa Vertical Attack), pero
     queda logging de diagnostico (`[RangeAttack] boton Rewired id=57 real -> "..."`) que se
     dispara una sola vez con la propia pulsacion real de P1 - pedirle al usuario que revise ese
     log tras probar para confirmar o corregir si el boton real no es Attack.

  4. **Rezo (Prayer) hardcodeado a "Q"** - NO estaba en el diagnostico externo aportado (el usuario
     pidio investigarlo desde cero). Resultaron ser DOS bugs de familia distinta reportados juntos:
     - *Animacion*: `PrayerUse.OnUpdate()` (`Gameplay.GameControllers.Penitent.Abilities.
       PrayerUse`) - `if (base.Rewired.GetButtonTimedPressDown(25, 0f) && !Core.Input.
       InputBlocked) { ... if (CanUsePrayer) { base.EntityOwner.Animator.Play(_animAuraTransform);
       } }` - familia 2 clasica, `base.Rewired` es el Player 0 compartido. La animacion
       "AuraTransform" de P2 solo se disparaba cuando P1 apretaba su propio rezo, nunca con el
       propio boton de P2 - completamente independiente del CAST real (que ya funcionaba desde la
       Ronda 39 via `PrayerUse_P2Input_Patch`, un Postfix separado que nunca tocaba esta linea).
       Fix: mismo patron de Transpiler puntual (`PrayerUse_OnUpdate_P2_AuraTransform_Patch` en
       `Prayer/PrayerSystem.cs`), retargeteando solo `Player.GetButtonTimedPressDown(int,float)`
       a `Player2Input.PrayerActivateDown` para P2 (time=0 en el call site real = press-edge puro,
       coincide exacto con la semantica de PrayerActivateDown).
     - *Efecto aplicado a P1*: la Ronda 43 ya habia arreglado 3 de 8 tipos de Prayer
       (`PrayerAlliedCherubEffect`/`PrayerShieldEffect`/`PenitentLightBeamEffect`) via
       `PrayerCasterTracker.LastCaster`, dejando explicitamente sin auditar el resto
       ("multishotPrayer/etc... no auditadas todavia"). Confirmado que los 5 restantes
       (`Tools.Items.PenitentCrawlerOrbsEffect/PenitentDivineLightEffect/
       PenitentFlamePillarsEffect/PenitentMultishotEffect/StuntPrayerEffect`) tienen exactamente
       el mismo bug (`_owner = Core.Logic.Penitent;` hardcodeado en su propio `OnApplyEffect()`
       self-contenido) - mismo fix aplicado (`PrayerCasterTracker.LastCaster`, sin cambios al
       tracker en si) en `Prayer/PrayerSystem.cs`. Detalle verificado, no asumido por semejanza:
       estas 5 clases terminan con `return base.OnApplyEffect();`, y
       `Framework.Inventory.ObjectEffect.OnApplyEffect()` base es trivial (`return false;`) - el
       `__result` sombreado en cada Prefix nuevo es por lo tanto `false`, a diferencia de
       `PenitentLightBeamEffect` (esa clase no llama a `base`, retorna `true` ella misma).

  **Compilacion**: `dotnet build` 0 errores despues de cada cambio (verificado incrementalmente:
  Player2SkillManager.cs solo, luego RangedAndVerticalAttackFixes.cs, luego PrayerSystem.cs).
  **Pendiente de playtest real, no verificable solo por lectura de codigo**: los 5 fixes de esta
  ronda (skill tree independiente, Vertical Attack por input propio, Prayer animacion+efecto
  independientes, y especialmente RangeAttack por la incertidumbre del boton 57 senalada arriba).

## Pendiente (roadmap de "credit P2 independently", fases 2-4 sin terminar)

- Corazones de Espada (Sword) para P2: aún no investigado a fondo, probablemente vive en
  `Framework.Inventory`, no en `EntityStats.PermanentBonus`.
- Cuentas de Rosario / slots de Prayer para P2: `BeadSlots` (cantidad de slots) ya queda
  cubierto por la ronda 57, pero *qué* rosarios tiene equipados P2 y su propio prayer
  equipado son sistemas de inventario aparte, sin tocar.
- HUD de P2 para sword hearts, rosary beads, prayer slots: no existen todavía (sí existen
  Flask/Purge/Fervour/Health).
- Bug "Q" de P2 (rezo/prayer): el ítem de Prayer equipado es un objeto de inventario único
  compartido a nivel de juego, no per-jugador - causa raíz distinta a todo lo de arriba,
  revisar `Prayer/PrayerSystem.cs`. No tocado esta sesión.
- Guardado/carga del inventario completo de P2 entre sesiones (no solo dentro de la
  misma run) - fuera de alcance de todo lo hecho hasta ahora.
- `GameModeManager`/NG+/Demake: hallazgo relacionado sin confirmar impacto real - también
  hardcodean `Core.Logic.Penitent.Stats.X.SetPermanentBonus(0f)` al cambiar de modo,
  dejarían a P2 con progresión "vieja" si el coop se usa junto con esos modos.
