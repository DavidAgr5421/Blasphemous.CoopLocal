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

- **Ronda 63** - Dos persistencias + muerte pendiente hasta Prie Dieu (sin playtest, solo
  verificación contra decompilado real vía `ilspycmd` + build incremental 0 errores).

  **Parte A - Skill Tree auto-save.** Estado previo `Stats/Player2SkillManager.cs:56` ya
  cargaba en cada `OnPlayerSpawn` (`EnsureLoadedForCurrentSlot` desde `CoopLocal.cs:201`) pero
  solo guardaba vía botón F8 `PermanentStatsDebugPanel.cs:148` (`Persist()`). Si el usuario
  toggleaba en F8 y cruzaba de sala sin apretar Guardar, `LoadForSlot` pisaba el shadow dict.
  Fix: `CoopLocal.cs:109` `Player2SkillManager.Persist()` justo antes de
  `Player2StatsSync.SaveCurrentVitals(Player2)` (mismo punto donde el `Player2 != null` guarda
  vitals antes del `Object.Destroy`, ya existente desde Ronda 55). Botón F8 se deja intacto como
  acción explícita adicional. Comentario añadido en `Player2SkillManager.cs:70` sobre partida
  nueva (`!File.Exists` => todo `false`, no clona de P1): decisión consciente documentada
  (Ronda 60 pendiente sin flujo de compra real; clonar daría progresión gratis), dejando
  snippet comentado de cómo cambiar a `shadow[id]=Core.SkillManager.IsSkillUnlocked(id)` si se
  pide. Verificado `dotnet build` 0 errores.

  **Parte B - P2 muerto no reaparece hasta Prie Dieu.** Hallazgos re-verificados contra
  `Blasphemous_Data/Managed/Assembly-CSharp.dll` (ilspycmd, no NuGet stub): `Entity:
  KillInstanteneously() {Status.Dead=true; Entity.Death(this); ...}`; `Penitent:Awake
  Entity.Death+=OnEntityDead` (suscripción estática compartida P1+P2); `Penitent:OnEntityDead:
  282-297` hace `Penitent penitent=entity as Penitent; if(!(penitent==null)){EnableAbilities
  (false); EnableTraits(false); DamageArea.IncludeEnemyLayer(false);}` sin `penitent==this`
  -> familia 3, P2 muerto deshabilita permanentemente traits/abilities de P1; `Penitent:
  OnUpdate:478-499` hace `Core.Logic.SetState(PlayerDead)` y `OnDead()` si cualquier
  `Penitent.Status.Dead` -> familia 3, estado global congelado + Pantallas `BellGhost/
  PontiffHusk/WaxCrawler` (ya citados en Ronda 60) pausadas; `DeadScreenWidget:OnPenitentReady
  99-107` solo suscribe `Core.Logic.Penitent.OnDead`; `PrieDieu:ShallowActivationLogic` (llamada
  desde `ActivationLogic/ReActivationLogic/ShallowUse`) resetea `Life/Flask(/Fervour si Alms>1)`
  y `Core.Persistence.SaveGame()` - ya parcheado en Ronda 45 para P2 vivo; `PlayerDeathFall:
  19` hace `_penitent.Kill()` con lazy-init `if(_penitent==null) _penitent=Core.Logic.Penitent`
  (familia 1, sin bundled-init, 1 campo solo; igual `Animation/Spike`). Vanilla no tiene "muerto
  pero el juego sigue" (P1 siempre `Respawn()` vía `DeadScreenWidget:OnDead`).

  Implementación:
  1. `Combat/PlayerDeathFixes.cs` nuevo: `Penitent_OnEntityDead_P2Fix_Patch` Prefix sobre
     `Penitent.OnEntityDead(Entity)` reimplementa completo vía `AccessTools.Method("GetPurge")`
     para rama `Enemy` (mismo patrón `Abilities/RangedAndVerticalAttackFixes.cs`), cambiando
     `if(!(penitent==null))` a `if(penitent!=null && penitent==__instance)` -> `return false`;
     deja anotado hallazgo colateral no arreglado **doble-farming Purge** (cuando `entity is
     Enemy`, `OnEntityDead` de **ambos** Penitents corre y cada uno hace `GetPurge(enemy)` ->
     ~2x Tears por kill con dos jugadores vivos) - fuera de scope de esta ronda como otros
     hallazgos colaterales previos. `Penitent_OnUpdate_P2Death_Patch` Prefix sobre
     `Penitent.OnUpdate` solo si `__instance==CoopLocal.Player2` -> `IsVisibleOnCamera=
     IsVisible()` + `if(Dead && !DeathEventLaunched){DeathEventLaunched=true;
     Player2DeathState.MarkDeadPendingRevive();} return false`; P1 (`return true`) intacto.
     3 Prefixes `PlayerDeathAnimation/Fall/SpikeBehaviour.OnStateEnter` reasignan `_penitent`
     via `AccessTools.Field(...,"_penitent")` + `animator.GetComponentInParent<Penitent>()`
     antes de vanilla (sin bloquear), verificado que ninguno tiene segundo campo ref en mismo
     `if` (trampa Ronda 59).
  2. `Stats/Player2DeathState.cs` nuevo - patrón idéntico `Player2StatsSync/Player2SkillManager`:
     `MarkerDirectory CooplocalMod/`, `GetAutomaticSlot()`, `try/catch ModLog "[P2DeathState]"`,
     archivo `p2_deathstate_slot{slot}.txt` línea `PendingRevive=true|false`,
     `IsPendingRevive()` (false si no existe), `MarkDeadPendingRevive()` sync inmediato,
     `ClearPendingRevive()` sync.
  3. `CoopLocal.cs:135-200` refactorizado: extraído `internal static Penitent SpawnPlayer2(Penitent
     p1, Vector3 spawnPosition)` con todo el cuerpo desde `Resources.Load<Penitent>` hasta log
     final (layers, `DontDestroyOnLoad`, `GravityScale=3f`, `EnsureSynced`+`EnsureLoaded`, colisiones,
     labels, baselines, cámara, HUDs, `HudFadeSync`), asignando `Player2` dentro y retornándolo
     (fix `this` -> `Main.CoopLocal` para `ModLog.Info` en método estático). `OnPlayerSpawn`
     queda con `if(Player2DeathState.IsPendingRevive()) return;` tras el chequeo `MENU` (mismo
     code-path que menú) y `SpawnPlayer2(p1,p1.transform.position+P2SpawnOffset)` en caso
     contrario.
  4. `Abilities/PrieDieuAndCliffLedgeFixes.cs:47` extendido `PrieDieu_ShallowActivationLogic_
     HealPlayer2_Patch.Postfix`: si `IsPendingRevive()` -> `ClearPendingRevive()` + si
     `Player2==null` -> `SpawnPlayer2(Core.Logic.Penitent, Core.Logic.Penitent.transform
     .position)` + sigue con `HealAtPrieDieu` normal (vida/flask/fervour a máximo).
  5. Builds incrementales `dotnet build` 0 errores tras cada paso (1: `Player2DeathState.cs`,
     2: `PlayerDeathFixes.cs`, 3: `CoopLocal` refactor, 4: `PrieDieu` patch).
  6. `grep CoopLocal.Player2` (58 hits) revisado: todos dereferencian con `!=null/==null` previo
     en mismo método o guard `owner!=Player2 => return true`; ningún `Player2.Algo` desnudo sin
     chequeo cercano (cubierto por fixes de familia 3 ya existentes). Sitios dudosos no-P2
     relacionados a esta ronda no requieren fix.
  **Pendiente de playtest real (todo Ronda 63, no verificable solo leyendo):** `P2 muere ->
  P1 sigue con Abilities/Traits intactos -> cruzar varias salas sin que P2 reaparezca ->
  llegar a Prie Dieu -> P2 revive con vida completa` y `P2 togglea skill en F8 -> cruza sala
  sin apretar Guardar -> skill sigue activa` (auto-save de Parte A). Doble-farming Purge
  queda anotado como hallazgo colateral no arreglado.

  **Auditoría posterior (misma ronda, otra sesión)** - se releyó todo el código entregado
  línea por línea contra un decompilado fresco (`ilspycmd`, no confiado a la Ronda 60/62
  previas) antes de aceptar nada como correcto, dado el historial de esta sesión con
  entregas que no compilaban o tenían bugs semánticos (Rondas 57/59). Resultado: la entrega
  es fiel al spec y correcta contra el decompilado real, sin bugs de compilación ni
  semánticos nuevos. Puntos verificados explícitamente:
  - `Penitent.OnEntityDead:282-297` confirmado carácter por carácter contra el Prefix: mismo
    orden Enemy/Penitent, mismo signature `GetPurge(Enemy)` privado, `DamageArea` es
    property pública (no necesita reflection, correctamente no reflejada), `EnableAbilities`/
    `EnableTraits` privados confirmados `(bool)` un solo parámetro.
  - `Penitent.OnUpdate:478-499`: confirmado que `Entity.OnUpdate()` base (la clase de la que
    Penitent hereda directo, `class Penitent : Entity, IDamageable`) es
    `protected virtual void OnUpdate() { }` **vacío** - el Prefix nuevo devuelve `false`
    incondicionalmente para P2 (no solo cuando está muerto), lo que en un primer vistazo
    parecía saltarse `base.OnUpdate()` todos los frames para P2 vivo, pero como ese base es
    un no-op confirmado, no hay regresión real: el método entero de Penitent (478-499) queda
    fielmente reimplementado para P2, vivo o muerto, sin perder nada.
  - Confirmado el mecanismo de fondo completo: `Entity.Death` es
    `public static event Core.EntityEvent Death;` (Entity.cs:143), cada `Penitent.Awake`
    suscribe su propio `OnEntityDead` (`Entity.Death += OnEntityDead`), y
    `Entity.KillInstanteneously()` (Entity.cs:317-324) hace `Status.Dead=true` **antes** de
    `Entity.Death(this)` - confirma que cuando P2 muere, tanto el `OnEntityDead` de P1 como
    el de P2 se disparan con `entity=P2`, y sin el `penitent==__instance` del fix, la rama
    vanilla (`if(!(penitent==null))`, sin chequeo de identidad) desactivaba Abilities/Traits
    de **ambos** jugadores con la muerte de cualquiera de los dos - familia 3 en su forma más
    clásica, confirmada con nombres/línea reales, no solo por semejanza de patrón.
  - Confirmado que el freeze global evitado es real y no cosmético: varias IA de enemigo
    (`BellGhostBehaviour:125`, `PontiffHuskMeleeBehaviour:128`, `WaxCrawlerBehaviour:65`)
    consultan `Core.Logic.CurrentState==LogicStates.PlayerDead` para pausar su propio
    comportamiento - con el `SetState` vanilla intacto, la muerte de P2 pausaría el combate
    de estas IA para todo el nivel, afectando a P1 en pleno combate. También confirmado que
    `DeadScreenWidget.OnPenitentReady` (la pantalla de "Continuar") se suscribe únicamente a
    `Core.Logic.Penitent.OnDead` (el singleton P1) - el campo `OnDead` es *per-instancia*
    (`public Core.SimpleEvent OnDead;` en Penitent, no en Entity), así que la pantalla de
    muerte nunca se habría mostrado igual con la muerte de P2 aunque el Prefix no existiera;
    el problema real y confirmado es exclusivamente el `Core.Logic.SetState` global, no el
    evento `OnDead`.
  - Los 3 `StateMachineBehaviour` de muerte (`PlayerDeathAnimationBehaviour`,
    `PlayerDeathFallBehaviour`, `PlayerDeathSpikeBehaviour`) confirmados con un solo campo
    `_penitent` cada uno, sin segunda inicialización de tipo referencia en el mismo `if`
    (trampa Ronda 59) - el Prefix usa `AccessTools.Field` + `SetValue` por reflection directa
    en vez de argumento inyectado por Harmony (`____penitent`), así que el bug de conteo de
    guiones bajos de la Ronda 62 no aplica aquí (no hay reversed-field involucrado).
  - `CoopLocal.OnPlayerSpawn`/`SpawnPlayer2`: orden confirmado idéntico al spec (destruir P2
    saliente + persistir skills/vitals -> chequeo MENU -> chequeo `IsPendingRevive` ->
    `SpawnPlayer2`), y el cuerpo de `SpawnPlayer2` conserva integro todo lo que tenía el
    método original (layers, `DontDestroyOnLoad`, `GravityScale`, sync de stats/skills,
    colisiones, labels, baseline de mud, cámara, los 3 HUD + `BringToFront` +
    `HudFadeSync.ApplyCurrentFadeState`, log final) - no se perdió ni se duplicó nada en el
    refactor.
  - `PrieDieu_ShallowActivationLogic_HealPlayer2_Patch.Postfix` maneja correctamente los 3
    casos: (a) P1 nunca tuvo P2 en la partida (`IsPendingRevive()` default `false` sin
    archivo) -> salta directo a `p2==null -> return`, sin curar nada, sin crash; (b) revive
    real (`PendingRevive=true`, `Player2==null` porque una transición de sala ya lo destruyó)
    -> limpia flag, `SpawnPlayer2`, cura; (c) caso defensivo del spec (`PendingRevive=true`
    pero `Player2` seguía sin ser null) -> solo limpia el flag, no duplica instanciación.
  - `grep CoopLocal.Player2` repetido de forma independiente (58 hits, mismo conteo que
    reportó la ronda) - muestreo de los sitios de HUD (`HealthHUD.cs:283-284`, ternario
    `p2!=null`) y del propio Prie Dieu confirma el patrón de guarda consistente en todo el
    repo, sin dereferencias desnudas nuevas introducidas por esta ronda.
  - Único hallazgo, de calidad no de corrección: `Penitent_OnEntityDead_P2Fix_Patch` resolvía
    `EnableAbilities`/`EnableTraits` vía `AccessTools.Method` en cada invocación (cada muerte
    de cualquier entidad del nivel) en vez de cachear una vez como ya hacía
    `GetPurgeMethod` al lado en el mismo archivo - inconsistente y repite reflection sin
    necesidad. Corregido: ambos métodos ahora cacheados en `PlayerDeathFixShared` junto a
    `GetPurgeMethod`. `dotnet build` 0 errores después del cambio.

  Conclusión de la auditoría: la Parte A y la Parte B de la Ronda 63, tal como fueron
  entregadas, son correctas contra el decompilado real y fieles al spec - a diferencia de
  las Rondas 57/59, esta entrega no tenía bugs de compilación ni semánticos que arreglar más
  allá del detalle de estilo/perf de arriba. Sigue pendiente el mismo playtest real ya
  anotado arriba (nada de esto reemplaza esa verificación en juego).

- **Ronda 64** - Investigación (sin fix, solo diagnóstico + logging propuesto) del reporte "P2 cayendo,
  aprieta Attack cerca de una pared escalable, no se pega bien - queda trabado en el aire, no cae pero
  tampoco entra al estado real de wall-stick, no puede saltar ni soltarse". Verificado contra
  `Blasphemous_Data/Managed/Assembly-CSharp.dll` real (con cuerpos, no el stub publicizado de
  `bin/Development`) vía `ilspycmd`.

  **Mecanismo vanilla confirmado, con cita exacta:**
  - `Gameplay.GameControllers.Penitent.Abilities.WallJump.OnUpdate` (decompilado, ~línea 142-152):
    con `Rewired.GetButton(5)` [Attack HELD] + no-grounded + raycast de pared + `!_stickToWall` +
    cooldown terminado, pone `_stickToWall=true`, hace `Animator.ResetTrigger("AIR_ATTACK")` y
    `Animator.Play(_wallClimbContactAnim)` (hash de "WallClimbContact"). Esto **nunca** pone
    `Animator.SetBool("STICK_ON_WALL", true)` en ningún lado de la clase - todas las apariciones de
    `SetBool("STICK_ON_WALL", ...)` dentro de `WallJump.cs` son a `false` (Detach/UnhangByEvent/
    ShakeOverthrowPenalty/UnHang).
  - `Gameplay.GameControllers.AnimationBehaviours.Player.Jump.WallJumpContactBehaviour.OnStateEnter`
    (`StateMachineBehaviour` de la propia animación "WallClimbContact") es quien realmente pone
    `animator.SetBool("STICK_ON_WALL", true)` (línea 16) - **ya resuelve el owner correctamente por
    instancia** (`_penitent = animator.GetComponentInParent<Penitent>()`, no el patrón lazy-fallback
    a `Core.Logic.Penitent` de la familia 1 - confirmado, no necesita fix de owner).
  - `WallJump.OnUpdate` línea 157: `Detach()` (la única salida normal del wall-stick, atada a
    `Player2Input.JumpDown` en el patch de P2) exige `_stickToWall && jumpOffCoolDownTimer<0 &&
    base.EntityOwner.Animator.GetBool("STICK_ON_WALL") && !IsShowingMenu`. Sin ese bool en `true`,
    `Detach()` **nunca** corre - coincide exacto con "no puede saltar ni soltarse".
  - `Physics/gravedad`: `Stick()` (llamado todo frame mientras `_stickToWall`) pone
    `Velocity=Vector3.zero`, `VSpeed=0`, `Gravity=Vector3.zero` incondicionalmente - esto por sí solo
    ya explica el "no sigue cayendo" **aunque el bool de animación nunca se ponga en true** (son dos
    mecanismos independientes: el freeze físico depende solo del campo privado `_stickToWall`, el
    gate de salida depende solo del bool del Animator).
  - Único escape alternativo en vanilla (`CheckCancelHook`/`UnHang`, gatillado por `Rewired.GetButton(65)`)
    está deliberadamente sin portar a P2 (comentario propio en `Abilities/WallJump.cs:68-71`, "Known,
    deliberate gap") - así que si `STICK_ON_WALL` nunca llega a `true` para P2, no queda ningún otro
    camino de salida.

  **Estado actual del mod**: `Abilities/WallJump.cs` (`WallJump_OnUpdate_P2_Patch`) ya reimplementa
  `OnUpdate()` completo para P2 (familia 2 - `Rewired.GetButton/GetButtonDown` compartido - y el
  hardcode plano `Core.Logic.Penitent` en `Stick()`/la rama de enganche, ya prolijamente documentado
  en el propio header del archivo) - el `Animator.Play(WallClimbContactAnim)` y el
  `ResetTrigger("AIR_ATTACK")` de esa reimplementación son fieles línea por línea a vanilla. No hay
  ningún patch tocando `WallJumpContactBehaviour` (no lo necesita, ya es correcto por instancia) ni
  gatillando manualmente `STICK_ON_WALL`.

  **Hipótesis principal de causa raíz (familia 4 - carrera de un solo frame, NO probada 100% solo con
  código, requiere log real)**: el mismo apretón de Attack que dispara el wall-stick (lectura HELD)
  **también** dispara, ese mismo frame, la animación de ataque aéreo normal (lectura EDGE) - ambos
  sistemas leen el mismo botón físico de P2 vía dos campos distintos:
  - `Gameplay.GameControllers.Penitent.Animator.AnimatorInyector.AirAttack()` (línea ~301-307,
    llamado desde `UpdateActions()` cuando `!_isGrounded`):
    `if ((!IsDemakeMode || !_playerInput.isJoystickUp) && _playerInput.Attack) { SpriteAnimator.
    SetTrigger(AirAttackParam); }` - `_playerInput.Attack` es el campo público que
    `Movement/Movement.cs` (`PlatformCharacterInput_Update_Patch.Postfix`) ya llena correctamente
    para P2 con el flanco (`Player2Input.AttackDown`, disparo de un solo frame al presionar).
  - `WallJump.OnUpdate`'s propia rama de enganche (la que hace `ResetTrigger("AIR_ATTACK")` +
    `Play(WallClimbContact)`) usa `Player2Input.AttackHeld` (mantenido) - **true en el mismo frame
    exacto** que `AttackDown` (ambos van a true el primer frame que se presiona el botón, igual que
    vanilla con `Rewired.GetButtonDown(5)`/`GetButton(5)`).
  - `AnimatorInyector` (MonoBehaviour propio, `Update()` de Unity) y `WallJump` (`Trait`, también su
    propio `Update()` de Unity vía `Trait.Update()->OnUpdate()`, confirmado en
    `Framework.FrameworkCore.Trait` decompilado) son **dos scripts hermanos independientes en el
    mismo GameObject** - el orden relativo de sus `Update()` lo decide Unity (orden de componentes
    del prefab / Script Execution Order), **el mismo para el clon de P2 que para P1** (mismo prefab,
    mismo orden de componentes) - así que esta carrera, si es real, existe también en teoría para P1,
    solo que un jugador real rara vez llega "recién apretando Attack" exactamente en el mismo frame
    en que el raycast de pared empieza a pegar (normalmente ya venía sosteniendo Attack desde antes,
    con el flanco ya consumido frames atrás) - el reporte de P2 describe justo ese caso límite
    ("presiona el botón... cerca de la pared" = apretón fresco), que en la práctica de testeo
    estructurado de este mod puede ser mucho más frecuente que en juego solo.
  - Si `AnimatorInyector.SetTrigger("AIR_ATTACK")` gana esa carrera (se consume en la próxima
    evaluación interna del Animator después de que `Play()` ya forzó "WallClimbContact"), P2 saldría
    del estado de wall-stick hacia un estado de ataque aéreo con `_stickToWall` (campo privado de
    `WallJump`) **todavía en `true`** (nada lo resetea salvo `Detach()/UnhangByEvent()/
    ResetWallJumpStatus()`, ninguno de los cuales corrió) - físicamente congelado
    (`Stick()` sigue corriendo, sigue zorreando velocidad/gravedad) pero animado/lógicamente fuera de
    "WallClimbContact", con el bool `STICK_ON_WALL` en un estado indefinido según si algo en el grafo
    del Animator Controller (dato binario del asset, no visible en el C# decompilado) lo resetea al
    salir. Esto calza exactamente con el síntoma reportado.
  - **Corroborado como bug real, pero probablemente secundario**: `PlatformCharacterInput.Update()`
    vanilla (nunca reemplazado para P2, solo Postfixed) tiene, dentro de su rama
    `InputMode==Gamepad` (línea ~217-219): `if (Jump) { _penitent.IsStickedOnWall = false; }` - con
    `Jump` computado unas líneas antes desde `Rewired.GetButton(6)` **compartido** (familia 2) - o sea,
    cuando P1 sostiene su propio botón de salto real, esto pone en `false` el campo
    `Penitent.IsStickedOnWall` de **P2** (el `_penitent` de esa instancia de componente sí resuelve
    correctamente a P2 - el bug es el botón leído, no el owner). Confirmado que este campo (distinto
    del bool de Animator `STICK_ON_WALL`) lo leen `FloorDistanceChecker.cs:160`,
    `AnimatorInyector.cs:204` (solo en rama grounded), `WindAreaEffect.cs:164` y
    `GuardianPrayerFollowState.cs:78` (IA enemiga) - ninguno de ellos es el gate de `Detach()`, así
    que esto no parece ser la causa del "trabado" en sí, pero sigue siendo un cross-talk real
    (P1 saltando podría, por ejemplo, hacer que la IA de un Guardian dejara de tratar a P2 como
    "trepando/pegado a pared" a mitad de un wall-stick real de P2) - documentado para una ronda
    aparte, no mezclado con el fix principal.

  **No se tocó ningún archivo de código esta ronda** (tarea explícitamente solo investigación). Plan
   de fix y prompt para el agente de implementación entregados aparte al usuario en esta misma sesión;
   quedan pendientes: (1) agregar logging de diagnóstico edge-triggered para confirmar/descartar la
   hipótesis de la carrera de trigger antes de tocar código (metodología ya establecida en este
   archivo - "un log confirmando que algo se disparó no alcanza, hay que confirmar el mecanismo real"),
   (2) aplicar el fix real según lo que el log confirme, (3) considerar además el fix del campo
   `IsStickedOnWall` compartido como hallazgo colateral separado.

- **Ronda 65** - PASO 1 del bug wall-stick P2 (logging de confirmación, sin fix aún). Pedido
  explícito: no asumir la hipótesis de la Ronda 64 (familia 4, `AIR_ATTACK` trigger compitiendo
  con `WallClimbContact` en el mismo frame que `AttackHeld`+`AttackDown` van a `true` juntos),
  confirmarla primero con log real filtrado por `[DashParryDebug]` (metodología del inicio del
  archivo).

  Implementado solo lo pedido en el Paso 1, sin tocar `AnimatorInyector.AirAttack()` ni
  `PlatformCharacterInput.Update()` IsStickedOnWall:
  `Abilities/WallJump.cs:WallJump_OnUpdate_P2_Patch` (único `Prefix` que ya reimplementa
  `WallJump.OnUpdate` completo para `CoopLocal.Player2`): 1) al entrar por primera vez en
  `stickToWall` (`if(Player2Input.AttackHeld && !IsGrounded && wallHit!=null && !stickToWall
  && cooldown<0)`) loguea `frame + AttackHeld/AttackDown/AttackUp + collider.name` (detecta si
  el flanco y el hold coinciden en el mismo frame que dispara el stick); 2) mientras
  `stickToWall==true` cada frame chequea `Animator.GetBool("STICK_ON_WALL")` y
  `GetCurrentAnimatorStateInfo(0).IsName("WallClimbContact")/IsName("WallClimbIdle")` - si no
  es ninguno loguea `hash:norm` crudo - y solo loguea cuando cambia el bool o el `shortNameHash`
  (edge-triggered, mismo patrón que el resto de `Diagnostics/Diagnostics.cs`:
  `DashParryDebugLog.Log("[DashParryDebug] ...")`, tag ya prefijado por la propia clase).
  Campos estáticos `_lastStickOnWallBool/_lastWallStateHash` reiniciados en `else (!stickToWall)`
  para fresh en el próximo enganche. Build `dotnet build` 0 errores.

  **Instrucción al usuario (PARAR aquí, Paso 2 no toca código hasta ver log):** reproducir con P2
  cayendo cerca de pared escalable y **apretar Attack fresco** (no venir ya sosteniendo) - caso que
  maximiza la ventana de carrera. Pasar `BepInEx/LogOutput.log` filtrado por `[DashParryDebug]`.
  Si el log muestra que el animator abandona `WallClimbContact/Idle` hacia otro estado mientras
  `STICK_ON_WALL` queda en valor equivocado y `_stickToWall` sigue `true`: Paso 2 será
  `Harmony Prefix` sobre `AnimatorInyector.AirAttack()` (privado, resolver con
  `AccessTools.Method(typeof(AnimatorInyector),"AirAttack")`) -> `if(owner==Player2 &&
  owner.IsStickedOnWall) return false` para bloquear `AIR_ATTACK` mientras está pegado.
  Si `STICK_ON_WALL` nunca llega a `true`: no aplicar ese fix, re-diagnosticar con el log.

- **Ronda 66** - Se pidió reproducir el wall-stick de la Ronda 65 (Paso 1, logging
  `[DashParryDebug]` en `Abilities/WallJump.cs`) y pasar el log real. El log entregado
  (`BepInEx/LogOutput.log`, 573 líneas) **no contiene ni una sola línea `[DashParryDebug]`** -
  confirmado que el escenario de wall-stick (P2 cayendo cerca de una pared, apretando Attack) no
  llegó a reproducirse en esta sesión de juego; el DLL desplegado sí tenía el logging del Paso 1
  (mismo tamaño/timestamp en `bin/Development` y en `Modding/plugins`), así que no es un problema
  de build viejo - simplemente no se dio la situación. **La hipótesis de la Ronda 64/65 (carrera
  AIR_ATTACK vs WallClimbContact) sigue sin confirmar ni descartar** - pendiente de un playtest que
  sí dispare ese escenario.

  En cambio, el log mostraba un hallazgo real y no relacionado: cientos de
  `NullReferenceException` consecutivas (`FallingForwardBehaviour.GetRayCastOrigin ->
  IsSideBlocked -> OnStateUpdate`) durante dos transiciones de sala completas, cesando justo en
  "Spawning enemies on level" en ambos casos. El usuario los señaló como "el bug del wall-stick" -
  **verificado que es un bug real y distinto, coincidente en la misma sesión de juego, no la misma
  causa** (instrucción explícita de no asumir una sola causa para todo el reporte, respetada aquí:
  cero líneas `[DashParryDebug]` en el log = el mecanismo de wall-stick nunca se ejecutó, así que
  no puede ser la explicación de nada de este log en particular).

  **Causa raíz confirmada de `FallingForwardBehaviour`** (`Gameplay.GameControllers
  .AnimationBehaviours.Player.Jump.FallingForwardBehaviour`, nunca auditada antes, decompilada
  completa vía `ilspycmd` contra el DLL real):
  - `GetRayCastOrigin(float heightOffset)`: `Vector3 position = Core.Logic.Penitent.transform
    .position;` - hardcode plano a P1 (la forma "flat", no el `if (_penitent == null)` de la
    familia 1 clásica - misma forma ya catalogada para `Parry.StartParry`/`Penitent.Damage`),
    ignorando el propio campo `_penitent` de la clase por completo. `OnStateUpdate` tiene el mismo
    hardcode una línea más abajo, en el raycast de detección de pendiente
    (`Physics2D.Raycast(Core.Logic.Penitent.transform.position, Vector2.down, 1.5f,
    RayCastLayerDetection)`).
  - Por qué explota solo para el clon de P2 y nunca para P1: `Gameplay.GameControllers.Penitent
    .Gizmos.PenitentSpawnPoint.Instance()` hace `Object.Instantiate(PenitentPrefab, ...)` sin
    `DontDestroyOnLoad` - el GameObject completo de P1 (Animator y cada instancia de
    `StateMachineBehaviour` incluida) se destruye en cada descarga de escena y se re-instancia de
    cero en la nueva, así que nada de P1 sigue "vivo" tickeando durante la pantalla de carga.
    Confirmado además el mecanismo exacto de reasignación: `Framework.Managers.SpawnManager
    .CreatePlayer(Vector3, EntityOrientation, bool)` línea 489: `if (createNewInstance) { Core
    .Logic.Penitent = UnityEngine.Object.Instantiate(penitentPrefab, position, Quaternion
    .identity); }` - confirma que `Core.Logic.Penitent` (propiedad autoimplementada normal,
    `Framework.Managers.LogicManager.Penitent { get; set; }`, sin ningún blindaje) queda
    genuinamente `null` en la ventana entre la destrucción del P1 viejo y la creación del nuevo.
    P2, en cambio, tiene `Object.DontDestroyOnLoad(Player2.gameObject)` desde la Ronda 55
    específicamente para sobrevivir transiciones de sala - así que si P2 está en el estado
    "FallingForward" en el instante exacto en que se dispara el trigger de puerta, su propia
    instancia de `FallingForwardBehaviour` sigue recibiendo `OnStateUpdate` cada frame durante
    *toda* la pantalla de carga, y cada uno de esos frames lee `Core.Logic.Penitent` (null en ese
    momento) -> NRE, repetido hasta que la nueva escena instancia el nuevo P1 y
    `Core.Logic.Penitent` vuelve a ser válido. **Bug exclusivo de coop**: literalmente no puede
    pasar en single-player vanilla, porque nada sobrevive el límite de escena para seguir
    llamando a este código.
  - `OnStateEnter` tiene además el bug clásico de familia 1 con trampa de bundled-init: `if
    (!_penitent) { _penitent = Core.Logic.Penitent; Dash dash = _penitent.Dash; dash.OnStartDash =
    Delegate.Combine(dash.OnStartDash, new Core.SimpleEvent(OnStartDash)); }` - la suscripción a
    `Dash.OnStartDash` vive dentro del mismo `if` que `_penitent`, así que un Prefix simple que solo
    reasignara `_penitent` habría dejado esa suscripción sin correr nunca (misma trampa ya
    confirmada dos veces antes). `_penitent` está declarado como propiedad autoimplementada
    (`private Penitent _penitent { get; set; }`), igual que `ParryRepostBehaviour`/
    `ParrySuccessBehaviour` en `Parry/Parry.cs` - el campo real de reflection es el backing field
    generado por el compilador.

  **Fix aplicado** (`Movement/MovementAnimationFixes.cs`, 3 patches nuevos, siguiendo el patrón ya
  usado para `GrabLadderDownBehaviour`/`LadderGoingUpBehaviour`/`LadderGoingDownBehaviour` en el
  mismo archivo para el caso de bundled-init):
  1. `FallingForwardBehaviour_OnStateEnter_Patch`: Prefix que, si el backing field de `_penitent`
     aún no está seteado, hace ÉL MISMO la inicialización completa (asigna `_penitent` al owner
     real vía `animator.GetComponentInParent<Penitent>()` + suscribe `OnStartDash` al `Dash` del
     owner real) antes de que corra el `if` original - vanilla ve el campo ya no-null y se salta su
     propio bloque, sin duplicar la suscripción ni perderla.
  2. `FallingForwardBehaviour_GetRayCastOrigin_Patch`: Prefix que reemplaza el método completo (no
     recibe `Animator` como parámetro, así que lee el backing field ya corregido de `_penitent` en
     vez de re-derivar el owner) - soluciona la NRE y de paso corrige el raycast para que P2 mida
     distancias desde su propia posición, no la de P1.
  3. `FallingForwardBehaviour_OnStateUpdate_Patch`: mismo patrón de Transpiler puntual de un solo
     call-site ya usado en `VerticalAttack_OnUpdate_P2_TimedPress_Patch`
     (`Abilities/RangedAndVerticalAttackFixes.cs`) - un Prefix acompañante guarda qué instancia está
     corriendo (`Update()` de Unity nunca intercala dos llamadas a la misma StateMachineBehaviour),
     y el Transpiler retarga la única llamada a `LogicManager.get_Penitent()` dentro de
     `OnStateUpdate` a un método que devuelve el `_penitent` ya corregido de esa instancia.
  `dotnet build` 0 errores; confirmado que el DLL desplegado (`Modding/plugins/CoopLocal.dll`)
  cambió de tamaño/timestamp tras el build.

  **Hallazgo colateral pedido explícitamente, arreglado por ser trivial**: los 7 patches de mejoras
  permanentes de la Ronda 57 (`Stats/Player2UpgradeCredit.cs`) declaraban
  `Prefix(Fsm fsm)`/`Postfix(Fsm fsm, ...)` - pero las 7 `OnEnter()` de
  `Tools.Playmaker2.Action.*Upgrade` no reciben ningún parámetro (confirmado decompilando
  `LifeUpgrade`/`StrengthUpgrade` reales); `fsm` solo existe como campo **privado** `fsm` (minúscula,
  sin guion) en la clase base `HutongGames.PlayMaker.FsmStateAction`. Para que Harmony lo inyecte
  por reflection hace falta el prefijo de 3 guiones bajos (`___fsm`), no un nombre de parámetro
  plano - error de convención, no de "un guion de más/de menos" como la Ronda 62. El log
  (`LogOutput.log:28-56`) solo mostraba la falla de `LifeUpgrade` explícitamente
  (`ArgumentException`/`Exception: Parameter "fsm" not found in method ... OnEnter()`), pero las
  otras 6 clases tienen exactamente el mismo `Prefix(Fsm fsm)` con idéntico problema estructural -
  se corrigieron las 7 por consistencia (`Fsm fsm` -> `Fsm ___fsm` en Prefix y Postfix de las 7
  clases). **No verificado con log si las otras 6 efectivamente fallaban silenciosamente antes de
  este fix o si de alguna manera solo `LifeUpgrade` estaba realmente rota** - el mecanismo interno
  exacto de por qué solo una de las siete tiró el error visible en este log puntual (¿seguía
  procesando el resto tras la excepción, o abortó ahí y ninguna otra llegó siquiera a intentarse
  esa carga?) no se investigó a fondo, dado que el fix aplica igual y es correcto para las 7 sin
  necesidad de resolver esa duda. Pendiente de playtest: confirmar en el próximo log que ya no
  aparece el error de `HarmonyX` al arrancar, y que las 7 mejoras permanentes efectivamente aplican
  a P2 cuando P2 es quien toca el altar.

  **Pendiente real de esta ronda**: el wall-stick original (Rondas 64/65) sigue sin confirmar -
  hace falta un nuevo playtest que dispare el escenario específico (P2 cayendo cerca de pared,
  Attack fresco) y that revise `[DashParryDebug]` en el log resultante. El fix de
  `FallingForwardBehaviour` de esta ronda es independiente y no sustituye ese playtest.

- **Ronda 67** - Análisis de los datos reales del wall-stick (Rondas 64/65/66) con logging
  `[DashParryDebug]` ya funcionando y reproducido varias veces en una sola sesión. Tarea
  explícita: solo investigación/diseño, sin aplicar el fix final - implementado únicamente
  logging adicional de bajo riesgo (Paso 2, mismo patrón que la Ronda 65).

  **Dato crudo aportado por el usuario, clave para descartar la hipótesis original:**
  - Éxito (frames 902/903, 924/925, 1769/1770): estado previo al stick con `normalizedTime<1`
    (ej. 0.94, un solo clip sin loopear todavía) - un frame después ya está en
    `WallClimbContact` con `STICK_ON_WALL=True`.
  - Fallo (frames 1100/1101/…/1142, 1836/1837/1878): estado previo al stick con
    `normalizedTime>1` (ej. 2.12 - solo posible en un estado en LOOP que ya dio al menos una
    vuelta completa, es decir, una caída larga) - el frame siguiente cambia de hash pero con
    `norm:0.00` (un estado recién arrancado, no `WallClimbContact`) y **se queda ahí
    congelado, sin que `STICK_ON_WALL` llegue nunca a `True`**, durante 40+ frames, hasta que
    se suelta Attack y pasa a un tercer estado (el mismo hash que tenía antes del intento).

  **Hipótesis de la Ronda 64/65 (carrera `AIR_ATTACK` vs `WallClimbContact`, ambas disparadas
  por el mismo flanco de Attack) descartada como explicación completa**: `AttackDown=True` en
  el frame START tanto en los casos exitosos como en los fallidos (confirmado en el log
  crudo del propio usuario) - si la carrera dependiera solo de que el trigger `AIR_ATTACK` se
  arme junto con el intento de stick, debería fallar (o tener éxito) siempre por igual, no
  correlacionar con la duración de la caída previa. La única diferencia real observada entre
  éxito y fallo es el estado/`normalizedTime` del Animator justo antes del intento - apunta a
  algo relacionado con el estado de caída EN LOOP en sí, no (solo) con el trigger de ataque.

  **Verificado contra decompilado real** (`ilspycmd`, `Assembly-CSharp.dll`):
  - `WallJump.OnUpdate()` vanilla (releído completo): el `Prefix` de
    `WallJump_OnUpdate_P2_Patch` ya es fiel línea por línea, incluido el orden exacto
    `_stickToWall=true` -> `Animator.ResetTrigger("AIR_ATTACK")` -> `Animator.Play(_wallClimbContactAnim)`
    -> (recién en la siguiente sentencia del mismo `if`, fuera del cuerpo del stick-start) ->
    `if(_stickToWall) Stick()`, y `Stick()` es quien zeroa `Velocity/VSpeed/Gravity` - todo
    esto ocurre en la MISMA llamada a `OnUpdate()`, antes de que el motor de Unity evalúe
    transiciones de Animator para este frame (esa evaluación es un paso interno de Unity que
    corre una sola vez por frame, después de que TODOS los `Update()` de scripts terminaron -
    confirma que `Animator.Play()` no toma efecto de forma sincrónica dentro de la misma
    llamada a C#, por eso el propio log "mismo frame" de la Ronda 65 en realidad muestra el
    estado ANTERIOR a este frame, no el resultado del `Play()` recién invocado - así se
    reinterpretaron los datos del usuario arriba).
  - `Gameplay.GameControllers.Penitent.Animator.AnimatorInyector` (releído completo,
    `Update()` -> `CheckStuntFall()` + `UpdateActions()` -> `AirAttack()`), decompilado línea
    por línea:
    - `CheckStuntFall()` (líneas ~376-392): corre INCONDICIONALMENTE todos los frames que
      `!_isGrounded` - completamente ajeno a si `WallJump._stickToWall` está activo (esta
      clase no conoce ese campo privado de `WallJump`, son dos componentes hermanos
      independientes en el mismo GameObject). Calcula
      `IsFalling = _platformCharacterController.PlatformCharacterPhysics.VSpeed <= -0.1f` y
      hace `SpriteAnimator.SetBool("FALLING", IsFalling)` cada frame - un bool, no un
      trigger, pero si el Animator Controller (asset binario, no visible en C#) tiene alguna
      transición tipo "Any State -> Falling" u otra condicionada a este bool, queda
      re-armándose cada frame mientras la velocidad vertical siga siendo negativa.
    - `AirAttack()` (líneas ~301-307): `if ((!IsDemakeMode || !_playerInput.isJoystickUp) &&
      _playerInput.Attack) SpriteAnimator.SetTrigger("AIR_ATTACK");` - confirmado que
      `_playerInput.Attack` para P2 es un flanco de un solo frame
      (`Movement/Movement.cs:344-345`, `bool attack = !blocked && Player2Input.AttackDown;`,
      no `AttackHeld`) - descarta la idea de un re-armado CONTINUO de `AIR_ATTACK` mientras
      se sostiene Attack (solo se arma, como mucho, una vez, el mismo frame que
      `AttackDown` pulsa) - consistente con que `AttackDown=True` en el frame START tanto en
      éxito como en fallo, sin diferencia entre ambos casos.
    - Orden real de ejecución entre `AnimatorInyector.Update()` y `WallJump.OnUpdate()`
      dentro del mismo frame **no es determinable leyendo el C# decompilado** (depende del
      orden de componentes del prefab / Script Execution Order de Unity, un dato de proyecto,
      no de código) - dejado como logging a confirmar, no asumido.

  **Hipótesis principal actualizada, aún sin confirmar 100% (requiere el log de esta ronda)**:
  el Animator Controller (asset, no auditable vía `ilspycmd`) probablemente tiene una cadena
  de estados de caída con más de un escalón (ej. "Falling" corto -> "Falling Forward" en loop
  tras cierto tiempo cayendo, inferido de que `AnimatorInyector.IsJumping()`/`UpdateActions()`
  tratan expresamente a "Jump"/"Falling"/"Jump Forward"/"Falling Forward" como un grupo
  relacionado) con transiciones propias (por tiempo de salida / "Exit Time", o por el bool
  `FALLING` re-armado cada frame) que compiten con el `Animator.Play(WallClimbContact)`
  forzado por `WallJump` en el mismo instante de evaluación del motor - y que esa competencia
  solo se vuelve relevante (o solo se "gana" en contra de `WallClimbContact") cuando el
  personaje ya venía en el escalón de caída EN LOOP (caída larga), no en el escalón corto
  inicial. No se pudo confirmar el mecanismo interno exacto sin acceso al grafo del Animator
  Controller (binario) ni sin datos de orden de ejecución real - **no se aplicó ningún fix
  esta ronda**, solo se agregó logging adicional para la próxima sesión de juego.

  **Logging adicional agregado esta ronda** (`Abilities/WallJump.cs`, mismo patrón
  `[DashParryDebug]`, sin cambiar ningún comportamiento real):
  1. Resolución de hash->nombre en runtime: `ResolveStateName()` usa
     `Animator.StringToHash(...)` real (calculado dentro del propio proceso del juego, sin
     necesidad de reimplementar el algoritmo de hash de Unity fuera de proceso - más
     confiable) contra una lista de candidatos (`WallClimbContact/WallClimbIdle/Falling/
     Falling Forward/Jump/Jump Forward/Idle`, elegidos a partir de los nombres de estado que
     el propio `AnimatorInyector.IsJumping()` decompilado ya trata como grupo) - reemplaza
     los hashes crudos (`hash:998495048`, `hash:-1233616792`) de los logs de la Ronda 65 por
     el nombre real si matchea alguno de los candidatos, con fallback al hash crudo si no
     matchea ninguno. Se loguea una vez (primer enganche) la tabla completa
     nombre=hash calculada en runtime, para poder confirmar a mano cualquier hash nuevo que
     aparezca sin tener que adivinar.
  2. Log del estado PRE (antes de `ResetTrigger`/`Play`) y de `GetBool("AIR_ATTACK")`/
     `GetBool("FALLING")` en tres momentos: justo antes de tocar nada en la rama de stick-start,
     justo después de `ResetTrigger("AIR_ATTACK")`+`Play(WallClimbContact)` (mismo frame,
     antes de que el motor evalúe transiciones), y en cada línea "while-stuck" ya existente
     (ahora también muestra `AIR_ATTACK=`/`FALLING=` además de `STICK_ON_WALL=`/`state=`).
  3. Nuevo patch, diagnóstico puro, sin alterar comportamiento:
     `AnimatorInyector_AirAttack_OrderDebugLogger_Patch` (`Abilities/WallJump.cs`), `Postfix`
     sobre `AnimatorInyector.AirAttack()` (privado, resuelto vía `AccessTools.Method`), solo
     para `CoopLocal.Player2` y solo cuando `_playerInput.Attack` es `true` ese frame (evita
     spamear el log en los frames donde no dispara nada) - loguea `Time.frameCount`. Como
     usa el mismo contador de frame que los logs de `WallJump`, comparar el ORDEN DE LAS
     LÍNEAS en `LogOutput.log` para el mismo número de frame revela directamente si
     `AnimatorInyector.AirAttack()` corrió antes o después de `WallJump.OnUpdate()` ese
     frame - dato que no se puede obtener leyendo el C# decompilado (orden de componentes
     del prefab, no de código).
  `dotnet build` 0 errores (2 iteraciones: primera con tuplas con nombre falló por el target
  framework del proyecto sin `System.Runtime.CompilerServices.TupleElementNamesAttribute` -
  CS8137/CS8179 -, corregido usando `KeyValuePair<string,int>[]` en vez de tuplas). DLL
  desplegado confirmado con timestamp nuevo en `Modding/plugins/CoopLocal.dll`.

   **Pendiente real de esta ronda**: reproducir de nuevo el escenario (P2 cayendo un buen
   rato antes de acercarse a la pared y recién ahí apretar Attack, para maximizar la chance
   del caso "loop"), pasar `LogOutput.log` filtrado por `[DashParryDebug]`, y con eso:
   (a) confirmar el nombre real de los hashes -1233616792 y 998495048 (vía la tabla
   nombre=hash logueada, o si no matchean ningún candidato, ampliar la lista con más nombres
   de estado y repetir), (b) confirmar el orden real AnimatorInyector vs WallJump por frame,
   (c) ver si `AIR_ATTACK`/`FALLING` están en `True` en el momento POST-Play del caso
   fallido y no en el exitoso. Recién con esos tres datos confirmados, decidir el fix real (no
   antes) - candidatos ya considerados pero NO aplicados: forzar `Play()` con
   `normalizedTime=0f` explícito en vez de dejar el default (podría no alcanzar si el problema
   es una transición de "Any State" del Animator Controller ganándole a la evaluación, no el
   valor de tiempo del `Play()` en sí), o bloquear explícitamente `AnimatorInyector.AirAttack()`
   para P2 mientras `WallJump._stickToWall` es `true` (razonable solo si el log confirma que el
   trigger es la causa real y no solo un síntoma acompañante).

- **Ronda 68** - Fix puntual wall-stick P2 derivado de la correlación confirmada fuera de sesión
  (no re-derivada aquí): 7 intentos reales con hashes resueltos en runtime
  (`Abilities/WallJump.cs` cheat-sheet): `Jump Forward=-1233616792`, `Falling=998495048`,
  `WallClimbContact=19863364`. Correlación 5 éxitos con `PRE-state=Jump Forward` (normalizedTime
  <1, estado no loopleado) -> al frame siguiente `WallClimbContact` + `STICK_ON_WALL=True`;
  1 fallo reproducido a propósito con caída larga `PRE-state=Falling` (`norm 2.12` loop) ->
  nunca llega a `WallClimbContact`, `FALLING True` persiste tras `Play`, al frame siguiente
  `Jump Forward` con `_stickToWall=true` (congelado, `Detach` bloqueado por
  `GetBool("STICK_ON_WALL")==false`). Verificado en `WallJump.OnUpdate` vanilla que nunca toca
  `SetBool("FALLING")` (solo `ResetTrigger("AIR_ATTACK")`+`Play(WallClimbContact)`). Fix
  aplicado solo en `Abilities/WallJump.cs:WallJump_OnUpdate_P2_Patch` (no se toca vanilla/P1):
  inmediatamente después de `owner.Animator.Play(WallClimbContactAnim)` se añadió
  `owner.Animator.SetBool("FALLING", false)` reusando la variable `owner` ya presente. Logging
  de diagnóstico de Ronda 67 se deja intacto para verificar en próximo playtest que ahora
  `POST-Play FALLING=False` y al frame siguiente `WallClimbContact`+`STICK_ON_WALL=True` incluso
  viniendo de `Falling`. Build `dotnet build` 0 errores. Pendiente playtest final: dejar caer P2
  varios segundos, acercarse a pared y atacar, revisar `BepInEx/LogOutput.log` filtrado por
  `[DashParryDebug]` y confirmar `POST-Play FALLING=False` + `STICK_ON_WALL=True`.

- **Ronda 69** - Auditoría del namespace completo `Gameplay.GameControllers.AnimationBehaviours
  .Player.Jump` (`ilspycmd`) buscando una clase "hermana" de `FallingForwardBehaviour` (Ronda 66)
  atada al estado "Falling" puro (no "Falling Forward"), con la hipótesis de que explicara por qué
  el wall-stick sigue fallando específicamente cuando `PRE-state=Falling` (Ronda 68, ya con el fix
  `SetBool("FALLING", false)` aplicado y confirmado insuficiente para ese caso).

  **Hallazgo principal, negativo**: la clase `FallingBehaviour` (el StateMachineBehaviour real del
  estado "Falling") existe y tiene el mismo bug de familia 1 (`if (_penitent == null) { _penitent =
  Core.Logic.Penitent; }`) que `FallingForwardBehaviour` - pero **ya estaba arreglada antes de esta
  sesión**, en `Movement/Movement.cs:46-57` (`FallingBehaviour_OnStateEnter_Patch`, un Prefix que
  reasigna `____penitent` incondicionalmente en cada `OnStateEnter`, sin trampa de bundled-init -
  confirmado que `OnStateEnter`/`OnStateUpdate` decompilados no tienen segundo campo de referencia
  en el mismo `if`). Este parche predata la numeración "Ronda N" de este archivo (mismo bloque de
  comentarios que `CrouchDownBehaviour`/`JumpOffBehaviour`, de una sesión anterior a que este NOTES
  existiera). Se intentó agregar un segundo patch idéntico (duplicado por no haber grepeado el repo
  completo antes de escribir código) - `dotnet build` lo detectó de inmediato (CS0101/CS0111,
  nombre de clase repetido) y se revirtió sin aplicarlo. **Conclusión**: `FallingBehaviour` no es
  un hallazgo nuevo y, al estar ya arreglada desde antes de las Rondas 64-68 (que reprodujeron el
  fallo con ese mismo fix ya desplegado), **no puede ser la causa del fallo wall-stick-desde-
  Falling** - se descarta explícitamente esta hipótesis, no solo por falta de evidencia sino por
  evidencia directa en contra (el bug ya no existía cuando el fallo se siguió reproduciendo).

  **Resto del namespace auditado, sin hallazgos nuevos aplicables**:
  - `JumpBehaviour`: mismo `if (_penitent == null) _penitent = Core.Logic.Penitent;` en
    `OnStateEnter`, pero `_penitent` no se lee en ningún otro lado de la clase (campo muerto tras la
    asignación) - sin efecto observable, no amerita fix.
  - `JumpForwardBehaviour`: mismo patrón familia 1, con un solo efecto real
    (`_penitent.DamageArea.IsFallingForwardResized = false;` en `OnStateExit`, mal dirigido a P1 si
    no se arregla) - no tocado esta ronda por estar fuera del pedido explícito (el estado "Jump
    Forward" es uno de los que YA funciona para el wall-stick, no el que falla) y para no ampliar
    el alcance sin pedido explícito - **queda anotado como hallazgo colateral no arreglado** para
    una ronda futura si se reporta algún síntoma relacionado a `IsFallingForwardResized`.
  - `HardLandingBehaviour`: **ya resuelve el owner correctamente por instancia**
    (`_penitent = animator.GetComponentInParent<Penitent>()`, no el fallback lazy a P1) - sin bug
    de familia 1. Tiene un bug de familia 2 real (`_penitent.PlatformCharacterInput.Rewired
    .GetButton(7)` compartido, gate del cancel-into-dash a mitad de un hard landing) pero
    irrelevante para el wall-stick: este estado sólo se alcanza aterrizando en el suelo
    (`Grounded`), y `WallJump.OnUpdate` exige `!controller.IsGrounded` para siquiera evaluar el
    enganche - código mutuamente excluyente con el escenario reportado. No tocado (fuera de
    alcance del pedido).
  - `LandingBehaviour`/`LandingRunningBehaviour`/`WallJumpContactBehaviour`: no decompilados en
    detalle esta ronda más allá de lo ya citado en la Ronda 64 para `WallJumpContactBehaviour`
    (confirmado sin bug ahí) - los dos de aterrizaje son, por nombre y por ser hermanos directos de
    `HardLandingBehaviour`, estados de suelo (mismo argumento de exclusión mutua con
    `!IsGrounded`); no se decompilaron completos por no haber ninguna señal de que aplican al
    escenario aéreo reportado.

  **Por qué la hipótesis del prompt original no se sostiene**: el namespace *sí* tenía una clase
  hermana con el bug exacto previsto (`FallingBehaviour`), pero resultó ser un caso ya cerrado, no
  uno nuevo - la búsqueda "clase hermana con hardcodeo a P1" como explicación del wall-stick
  específico de "Falling" queda descartada por evidencia directa, no por falta de búsqueda.

  **No se aplicó ningún fix funcional esta ronda** (ninguna causa nueva y confirmada que arreglar).
  Se agregó únicamente logging de diagnóstico adicional, de bajo riesgo, en
  `Abilities/WallJump.cs` (`WallJump_OnUpdate_P2_Patch`):
  1. Extendido el log `PRE-state` (línea del stick-start) con `PRE-IsJumpingOff`,
     `PRE-IsClimbingCliffLede`, `PRE-ColliderEnabled` (`owner.PlatformCharacterController
     .SmartPlatformCollider.enabled`) - documentado en el propio código que ninguno de estos tres
     es leído por el `WallJump.OnUpdate` vanilla (confirmado contra el decompilado), así que sólo
     importan si el grafo del Animator Controller (asset binario, no legible con `ilspycmd`)
     condiciona alguna transición sobre ellos - dato que sólo un log real puede confirmar o
     descartar.
  2. Nuevo helper `DumpTrueBoolParameters(Animator)`: recorre `Animator.parameters` y devuelve,
     por nombre, todos los parámetros `Bool` actualmente en `true` - reemplaza la estrategia previa
     (Rondas 67/68) de chequear a mano sólo `AIR_ATTACK`/`FALLING` por nombre, que ya demostró ser
     insuficiente (ambos confirmados `False` en el caso de fallo más reciente y aun así el enganche
     falla) - ahora se loguea el conjunto completo de bools verdaderos, sin tener que adivinar de
     antemano cuál es el parámetro relevante. Agregado en dos puntos: inmediatamente después de
     `ResetTrigger("AIR_ATTACK")` + `Play(WallClimbContact)` + `SetBool("FALLING", false)` (mismo
     punto que el log POST ya existente), y en cada línea "while-stuck" ya existente (edge-triggered
     por cambio de bool/estado, sin aumentar el spam).
  `dotnet build` 0 errores (2 iteraciones: primera con `string.Join(string, List<string>)` falló -
  `CS1503`, esa sobrecarga no existe en el target framework del proyecto -, corregido con
  `.ToArray()`). DLL desplegado confirmado con timestamp nuevo en `Modding/plugins/CoopLocal.dll`.

  **Instrucción al usuario (pendiente de esta ronda, no verificable sin playtest)**: reproducir de
  nuevo el caso de fallo (P2 cayendo un buen rato antes de acercarse a la pared, `PRE-state=Falling`
  con `normalizedTime>1`) Y un caso de éxito en la misma sesión (`PRE-state=Jump Forward` o
  `Falling Forward`), pasar `BepInEx/LogOutput.log` filtrado por `[DashParryDebug]`, y comparar la
  lista `all-true-bools` de la línea `POST-Play` entre ambos casos - cualquier parámetro presente en
  uno y ausente en el otro es candidato directo a ser el condicionante de la transición que le gana
  a `Play(WallClimbContact)`. Si las dos listas resultan idénticas, la causa no es un parámetro de
  Animator legible por este método (dato igual de valioso: descartaría también el grafo de
  condiciones simples y apuntaría a algo más estructural del propio Animator Controller, como Exit
  Time puro sin condición de parámetro, que no se puede instrumentar desde C#).

- **Ronda 70** - Menús propios para P2. Decompilado `NewInventoryWidget`, `NewInventory_LayoutSkill` (`Rewired.GetButton(52)` hold 2s `UnlockSkill`), `NewInventory_LayoutGrid` (`EquipObject/UnEquipObject/IsEquipped/GetFirstEmptySlot` con `Core.InventoryManager.*` global) y `InventoryManager` (`Dictionary all*/List own*/ wear*[]`, `Persistent ID_INVENTORY`). `SkillManager` no es `Penitent.Abilities` sino `Framework.Managers` singleton global (ya sombreado en gameplay por `Player2SkillManager`+`Ability.GetLastUnlockedSkill` pero UI seguía leyendo `Core.SkillManager`).

  **Fase 0** `Stats/Player2InventoryManager.cs` nuevo — patrón idéntico a `Player2SkillManager` (`p2_inventory_slot{slot}.txt` en `CoopLocalMod/`, `GetAutomaticSlot()`, `HashSet ownedBeads/Prayers/Swords/Relics` + `string[] wearBeads[8]`, `string wearPrayer`, `string wearSword`, `string[] wearRelics[3]`), `LoadForSlot/SaveForSlot/Persist/EnsureLoadedForCurrentSlot` (hook en `CoopLocal.OnPlayerSpawn` tras `Player2SkillManager` y `Persist` pre-Destroy junto a vitals/skills). Decisión documentada no-clonar de P1 por defecto (owned compartido fallback a global si shadow vacío).

  **Fase 1** `Stats/SkillTreeUI.cs` + `Stats/Player2MenuView.cs` (`static int SkillViewPlayer/InventoryViewPlayer`, toggle `F7`): `NewInventory_LayoutSkill.ShowLayout` Postfix antepone `[P1]/[P2]` a `maxTier` con `MeaCulpa`/`Purge` de P2, `Update` Prefix reimplementa hold de 2s para P2 usando `Player2SkillManager.IsUnlocked/parent+MeaCulpa` check, `SetUnlocked+Persist` y descuenta `p2.Stats.Purge`; `NewInventory_Skill.UpdateStatus/SetFocus` Prefix leen `Player2SkillManager` cuando `view==P2`. P1 `return true` vanilla intacto.

  **Fase 2** `Stats/InventoryUI.cs`: `LayoutGrid.ShowLayout/Postfix`, `Update` toggle `F7` para `InventoryViewPlayer`, `IsEquipped`/`EquipObject`/`UnEquipObject`/`GetFirstEmptySlot` Prefix redirigen a `Player2InventoryManager` cuando `view==P2` (Bead busca `FindFreeBeadSlot` respetando `p2.Stats.BeadSlots.Final`, Prayer/Sword 1 slot). `Core.InventoryManager` queda autoridad para P1.

  **Fase 3** `Stats/InventoryGameplayPatches.cs`: `IsRosaryBeadEquipped/IsPrayerEquipped/IsSwordEquipped/GetEquippedPrayer` Postfix hacen overlay (`if !result && P2IsEquipped => true`) para que pasivos de beads/prayers de P2 cuenten en gameplay (parche global, luego refinable a per-entity con stack context; MVP permite overlay). Prayers activas (Q) quedan para extender `PrayerCasterTracker` ya existente (3/8 en Ronda 62) a 5 restantes en `PrayerSystem.cs` como paso siguiente no incluido aquí.

  Build `dotnet build` 0 errores tras cada fase (Fase 0 `Exists` sobre `ReadOnlyCollection` corregido a loop). Pendiente playtest: `P1 equip PrayerA / P2 equip PrayerB` => Q dispara efecto propio, toggle F7 en skill/inventory muestra `[P2]` y permite unlock/equip sin afectar P1, y fila `STICK_ON_WALL` ya verificada Ronda 68.

- **Ronda 71** - Wall-stick P2 — hipótesis float descartada, extendido logging para velocidad (Tarea 1-8). Decompilado `AnimatorInyector:Update` completo (`C:\Program Files (x86)\Steam\...\Blasphemous_Data\Managed\Assembly-CSharp.dll` vía `ilspycmd`): `CheckStuntFall()` escribe `FALLING` bool desde `PlatformCharacterPhysics.VSpeed <= -0.1f` y `MaxVSpeedFallStunt`; `UpdateActions()` escribe `GROUNDED/CAN_AIR_ATTACK/.../AIR_ATTACK` pero **ningún `SetFloat`** en toda la clase (grep `SetFloat` vacío) — el único float hipotético `VSpeed` no existe como parámetro del Animator, solo el bool derivado `FALLING` (ya cubierto por el fix de Ronda 68). **Orden `AnimatorInyector.Update` vs `WallJump.OnUpdate` no determinable solo por C#**: ambos son `MonoBehaviour.Update()` en el mismo `GameObject` (hermanos), depende de orden de componentes del prefab / `Script Execution Order` (dato de proyecto Unity, no de DLL) — explícitamente documentado como no determinable, no asumido.

  Logging extendido en `Abilities/WallJump.cs:WallJump_OnUpdate_P2_Patch` sobre los 3 puntos ya logueados (`PRE-state` antes de `ResetTrigger/Play`, `POST-Play`, `while-stuck` edge-triggered): mismo patrón `DumpTrueBoolParameters` ahora plus `DumpFloatParameters` (filtra `AnimatorControllerParameterType.Float` y vuelca `name=val` solo si `|v|>0.001`) y `Velocity / VSpeed` reales (`controller.PlatformCharacterPhysics.Velocity` / `VSpeed`). Implementado como `DumpFloatParameters(Animator)` análogo a `DumpTrueBoolParameters`, llamado en las mismas líneas. No se encontró float que escriba incondicionalmente sin chequear `_stickToWall` (no existe), por lo que **no se aplicó fix candidato de forzar float a neutro** — hipótesis del float descartada por evidencia negativa (ausencia total de `SetFloat` en `AnimatorInyector`).

  Siguiente paso propuesto (Tarea 7): si el log con floats tampoco diferencia éxito/fallo (esperable dado que no hay floats no-nulos más allá de transitorios), la causa está en el grafo binario del `Animator Controller` (asset, no auditable por `ilspycmd`), no en parámetro legible. Fix alternativo: hacer que `WallJump` **reintente** `Play(WallClimbContact)` en frames sucesivos mientras `STICK_ON_WALL==false && _stickToWall==true` en vez de intentarlo una sola vez, para ganarle a la transición competidora con `Exit Time` sin condición de parámetro.

  Build `dotnet build` 0 errores. Pendiente playtest con nuevo log: comparar `PRE-floats`/`POST-floats`/`vel` entre éxito (`Jump Forward`) y fallo (`Falling`) y confirmar que son idénticos (descarta float) antes de implementar reintento.

- **Ronda 72** - Wall-stick P2, fix pragmático de reintento (implementado, pendiente playtest -
  no reemplaza la confirmación real, ver instrucción abajo). Dado que las Rondas 67-71 agotaron
  todo lo auditable desde C# (ningún Bool ni Float del Animator diferencia éxito de fallo, orden
  de `Update()` entre `AnimatorInyector`/`WallJump` no determinable sin el proyecto Unity/grafo
  binario del Animator Controller), se abandonó la estrategia de "ganarle en un solo frame" a la
  transición competidora y se implementó la alternativa ya propuesta al cierre de la Ronda 71:
  reintentar `Animator.Play(WallClimbContact)` en cada frame sucesivo mientras `_stickToWall==true`
  pero `STICK_ON_WALL==false`, con límite y red de seguridad.

  **Cambios en `Abilities/WallJump.cs` (`WallJump_OnUpdate_P2_Patch`), todos dentro del `Prefix`
  que ya reimplementa `OnUpdate()` completo para P2 - nada tocado para P1/vanilla:**
  1. Campos estáticos nuevos: `_stickStartFrame` (frame exacto en que se llamó `Play()` por
     primera vez en el intento actual), `_stuckRetryFrames` (contador de frames consecutivos con
     `STICK_ON_WALL==false` tras el frame de arranque), `MaxStickRetryFrames=60` (~1s de frames
     estancados antes de la liberación de seguridad), y `UnhangByEventMethod` (`AccessTools.Method`
     sobre el método privado `WallJump.UnhangByEvent()`, sin parámetros - decompilado y confirmado
     que hace `ResetWallJumpStatus()` + `_stickToWall=false` + `Distance=0f` +
     `SetBool("STICK_ON_WALL",false)` + `ResetTrigger("WALLCLIMB_UNHANG")` +
     `Core.Input.SetBlocker("PLAYER_LOGIC",false)` - el mismo método que ya usa vanilla para sus
     propios releases por daño/camera-shake, reutilizado tal cual vía reflection en vez de
     reimplementado, para no duplicar ni desincronizar su comportamiento real).
  2. En la rama de arranque del stick (`if(Player2Input.AttackHeld && ... && !stickToWall && ...)`)
     se agregó `_stickStartFrame=Time.frameCount; _stuckRetryFrames=0;` justo después de marcar
     `stickToWall=true` - registra el frame exacto en que se llamó `Play()` por primera vez.
  3. Dentro de `if(stickToWall){...}`, después del logging edge-triggered ya existente y antes del
     `Stick()` inlined: si `Time.frameCount != _stickStartFrame` (es decir, no es el mismísimo
     frame en que se llamó `Play()` por primera vez - Ronda 67 confirmó que `Play()` nunca surte
     efecto de forma sincrónica dentro del mismo `Update()` que lo invoca, así que chequear
     `STICK_ON_WALL` ese mismo frame siempre daría `False` incluso en el 90%+ de casos exitosos)
     y `STICK_ON_WALL==false`, incrementa `_stuckRetryFrames`, loguea una sola vez por racha
     (`_stuckRetryFrames==1`) y vuelve a llamar `owner.Animator.Play(WallClimbContactAnim)`. Si
     `_stuckRetryFrames>MaxStickRetryFrames`, loguea y llama `UnhangByEventMethod.Invoke(__instance,
     null)` (red de seguridad) + `stickToWall=false` local + resetea el contador. Si
     `STICK_ON_WALL==true` y había reintentos pendientes, loguea la recuperación y resetea el
     contador a 0.
  4. El bloque `Stick()` inlined (zeroing de velocidad/gravedad/orientación) quedó envuelto en un
     `if(stickToWall)` adicional - necesario porque la liberación de seguridad del punto 3 puede
     poner `stickToWall=false` a mitad del mismo frame, y en ese caso no debe volver a zorrear
     velocidad/gravedad (`UnhangByEvent()` ya restauró `Gravity=(0,-9.8,0)` por su cuenta vía
     `ResetWallJumpStatus()`).
  5. `_stuckRetryFrames=0` agregado también al `else` (`!stickToWall`, cuando sale del wall-stick
     por cualquier vía) para no arrastrar conteo estancado a un enganche futuro.
  6. Nuevo patch `WallJump_UnhangByEvent_BlockerTracking_Patch` (`Postfix` sobre
     `WallJump.UnhangByEvent()`, mismo patrón ya usado para `Stick()`/`Detach()` justo arriba en el
     archivo) - cierra el mismo gap ya documentado para esos dos: `UnhangByEvent()` nunca se
     registraba con `PlayerLogicBlocker` (solo con el `Core.Input.SetBlocker` global), así que sin
     este patch la liberación de seguridad del punto 3 habría dejado a P2 marcado como bloqueado
     para `BlockerOverrideHelper` aunque el blocker global ya estuviera libre. Como Harmony parchea
     el método en sí (no un call site puntual), este Postfix corre tanto si lo dispara el propio
     P2 (nuevo, vía reflection) como si lo dispara P1 en su código 100% vanilla
     (`EntityOwnerOnDamaged`/`OnCameraShakeOverthrow`/`UnHang()` coroutine) - cierra el gap para
     ambos jugadores, no solo para el caso nuevo.

  **Por qué no debería interferir con el 90%+ de casos exitosos**: el chequeo de reintento se
  salta explícitamente el frame de arranque (`_stickStartFrame`), que es el único frame en que
  `STICK_ON_WALL` puede estar en `False` de forma legítima y esperada (latencia de un frame del
  motor). En un enganche exitoso (Ronda 67: éxito = `STICK_ON_WALL=True` ya en el frame
  siguiente), el primer chequeo real de reintento (frame `_stickStartFrame+1`) encuentra
  `STICK_ON_WALL==true` de entrada y toma la rama `else` sin loguear ni reintentar nada -
  `_stuckRetryFrames` nunca pasa de 0 en ese camino.

  **Qué esperar en el próximo log si el fix funciona**: mismo patrón de antes hasta
  `POST-Play` (Rondas 67-71 intactas, sin tocar), pero en vez de quedar congelado en
  `while-stuck STICK_ON_WALL=False` indefinidamente, debería aparecer una línea
  `P2 WallJump STICK_ON_WALL still False frame N ... - retrying Play(WallClimbContact)` seguida,
  en algún frame posterior, de `P2 WallJump STICK_ON_WALL recovered to True at frame M after K
  Play() retries` - y el jugador debería quedar pegado a la pared normalmente desde ahí (sin
  congelamiento). Si en cambio aparece la línea `retry limit (60 frames) exceeded ... forcing
  UnhangByEvent safety release`, la red de seguridad se activó: P2 debería caer/soltarse de la
  pared en vez de quedar trabado para siempre, pero esto NO es el resultado esperado del fix
  principal - indicaría que el reintento de `Play()` no alcanza a ganarle nunca a la transición
  competidora en ese caso particular, y habría que revisar el log completo de esa racha para
  decidir el siguiente paso (posible candidato ya descartado antes por falta de datos: bloquear
  `AnimatorInyector.AirAttack()` para P2 mientras `_stickToWall==true`, ver Ronda 67).

  Build `dotnet build` 0 errores. DLL desplegado confirmado con timestamp nuevo en
  `Modding/plugins/CoopLocal.dll`. **Pendiente de playtest real, no verificable solo por lectura
  de código**: reproducir el caso de fallo ya conocido (P2 cayendo un buen rato antes de acercarse
  a la pared, `PRE-state=Falling` con `normalizedTime>1`) y confirmar en el log cuál de los tres
  desenlaces de arriba ocurre.

- **Ronda 73** - Investigación (sin fix, solo diagnóstico - pedido explícito) del reporte "el Skill
  Tree de P2 no es realmente independiente de P1: si P1 tiene una habilidad desbloqueada, P2
  también puede ejecutarla aunque su propio shadow diga false, para los 5 ataques mejorables
  (`ChargedAttack/Combo/LungeAttack/RangeAttack/VerticalAttack`)". Releído `Stats/Player2SkillManager.cs`
  completo y redecompilado `Framework.FrameworkCore.Ability` + las 5 subclases + `PenitentAttack` +
  `AnimatorInyector` + `DashBehaviour` vía `ilspycmd` (decompile completo del proyecto para grep,
  no solo clases sueltas) contra `Assembly-CSharp.dll` real.

  **El choke point en sí (`Ability.GetLastUnlockedSkill()`) está correcto tal cual quedó en la
  Ronda 62**: campo real confirmado `private List<string> unlocableSkill;` (sin guion propio),
  patch usa `___unlocableSkill` (3 guiones) - coincide. `Ability_GetLastUnlockedSkill_P2_Patch`
  (`Stats/Player2SkillManager.cs:159-184`) compara `__instance.EntityOwner != CoopLocal.Player2`,
  recorre la lista consultando `Player2SkillManager.IsUnlocked` y corta en el primer `false` - fiel
  al vanilla (`Ability.cs:122-135` decompilado: mismo recorrido, corta en el primer skill no
  desbloqueado, se queda con el último que sí lo estaba).

  **Grep de `Core.SkillManager.IsSkillUnlocked` sobre el decompilado COMPLETO del juego (2079
  archivos)**: exactamente 2 call sites en todo `Assembly-CSharp.dll` - `Ability.GetLastUnlockedSkill()`
  (el choke point, ya patcheado) y `Gameplay.UI.Others.MenuLogic.NewInventory_Skill` (UI del menú,
  ya cubierto por `SkillItem_UpdateStatus_P2_Patch`/`SkillItem_SetFocus_P2_Patch` de la Ronda 70).
  **No existe ningún otro punto en todo el juego que lea el estado global de skills para gameplay** -
  descarta con evidencia fuerte (no solo lectura puntual) la hipótesis "alguna subclase llama
  directo a `Core.SkillManager.IsSkillUnlocked` bypaseando `GetLastUnlockedSkill()`".

  **Recorridas las 5 entradas de "cast" de las abilities mejorables, una por una, hasta su gate real**
  (no solo el método `OnUpdate`, sino de dónde viene la llamada a `.Cast()`):
  - `ChargedAttack`: gate real está en `AnimatorInyector.ChargeAttackTriggered()`
    (`Gameplay.GameControllers.Penitent.Animator.AnimatorInyector.cs:319-334`, decompilado):
    `if (_playerInput.IsAttackButtonHold && !_penitent.ReleaseChargedAttack &&
    _penitent.ChargedAttack.IsAvailableSkilledAbility)` antes de armar el trigger `CHARGE_ATTACK`
    del Animator (que a su vez dispara `StartChargingAttackBehaviour.OnStateEnter ->
    _penitent.ChargedAttack.Cast()`, `Gameplay.GameControllers.AnimationBehaviours.Player.Attack
    .StartChargingAttackBehaviour.cs:18`). `AnimatorInyector._penitent` se resuelve en `Awake()`
    vía `GetComponent<Penitent>()` (`AnimatorInyector.cs:145`) - **correcto por instancia, no
    lazy-fallback a P1** - así que para P2 este gate correctamente consulta el `ChargedAttack` de
    P2 y por lo tanto el shadow de P2 vía el choke point ya patcheado.
  - `Combo`: **no gatea "puede ejecutar el combo" en absoluto, ni en vanilla ni con el mod** -
    `Combo.IsAvailable`/`GetMaxSkill` sólo se consultan en `PenitentAttack.IsFinalComboAvailable`
    (afecta el `DamageType` del 3er golpe) y `PenitentAttack.GetExecutionBonus()` (bonus % de
    ejecución en enemigos con poca vida) - la animación del combo 3 golpes + finisher
    (`Combo_1/2/3/4`/`ComboFinisherUp`/`ComboFinisherDown`) se dispara por conteo de golpes
    (`comboCharge`/`IsComboCharged()`) sin ninguna consulta a `SkillManager`
    (`PenitentAttack.cs:229-324` decompilado, releído completo). **Si lo que el usuario comparó fue
    "¿P2 hace la animación de combo completa igual que P1?", eso no es un bug de coop - en vanilla
    single-player el combo de 3 golpes tampoco requiere desbloquear nada del Skill Tree, el árbol
    sólo modula el bonus de daño/ejecución.** Esto puede explicar buena parte del reporte para
    `Combo` específicamente sin que haga falta ningún fix.
  - `LungeAttack`: gate real está en `DashBehaviour.CastLungeAttack()` (privado,
    `Gameplay.GameControllers.AnimationBehaviours.Player.Dash.DashBehaviour.cs:140-152`,
    invocado por reflection desde `Dash/DashAndInputBlockers.cs:607`,
    `DashBehaviour_OnStateUpdate_Patch`, cuando P2 suelta Attack en medio del dash):
    `LungeAttack componentInChildren = _penitent.GetComponentInChildren<LungeAttack>(); if
    (!componentInChildren.IsAvailable) return false;`. Esto usa el campo PRIVADO `_penitent` de la
    propia `DashBehaviour` (bug clásico de familia 1 con lazy-init, confirmado en
    `OnStateEnter:33-36` del decompilado: `if (_penitent == null) _penitent = Core.Logic.Penitent;`)
    - de no estar arreglado, esto haría que `CastLungeAttack()` en la instancia de P2 resolviera
    `_penitent` a P1 y por lo tanto ejecutara `Cast()` sobre el `LungeAttack` **de P1**, leyendo el
    desbloqueo de P1 (encajaría perfecto con el síntoma reportado). **Pero está arreglado desde antes
    de esta sesión**: `DashBehaviour_OnStateEnter_Patch` (`Dash/DashAndInputBlockers.cs:104-115`,
    con comentario propio ya documentando este exact escenario línea por línea) reasigna
    `____penitent = owner` incondicionalmente en cada `OnStateEnter`, sin trampa de bundled-init -
    confirmado que para P2 `CastLungeAttack()` termina resolviendo el `LungeAttack` correcto (el de
    P2), y por lo tanto el shadow de P2 vía el choke point.
  - `RangeAttack`: gate real está en `RangeAttack_OnUpdate_P2_Patch`
    (`Abilities/RangedAndVerticalAttackFixes.cs:246-301`, Ronda 62) - reimplementación completa que
    invoca `GetLastUnlockedSkillMethod.Invoke(__instance, null)` (reflection sobre el choke point ya
    patcheado) antes de permitir `CastRangeAttack()`. Releído completo, consistente con lo
    documentado en la Ronda 62.
  - `VerticalAttack`: gate real está inline en el propio `VerticalAttack.OnUpdate()`
    (`VerticalAttack.cs:179-194` decompilado) - `UnlockableSkill lastUnlockedSkill =
    GetLastUnlockedSkill(); if (lastUnlockedSkill == null) return;` antes de `Cast()`. El único
    fix de P2 aquí (Ronda 62, Transpiler) retarga sólo la lectura de
    `_rewired.GetButtonTimedPress("Attack", ...)` en la misma línea del `if` - no toca la llamada a
    `GetLastUnlockedSkill()`, que corre intacta (y por lo tanto pasa por el choke point patcheado).

  **Dato adicional encontrado, no en el pedido original pero relevante para reproducir el bug con
  precisión la próxima vez**: el archivo real persistido en disco
  (`C:\Users\USUARIO\AppData\LocalLow\TheGameKitchen\Blasphemous\CoopLocalMod\p2_skills_slot0.txt`,
  slot 0, que es el slot activo según `BepInEx/LogOutput.log` de esta misma sesión, "Loading data
  for slot 0") **no está todo en `false` ni todo en `true`** - tiene un patrón parcial real:
  `CHARGED_1=true` (2/3 false), `LUNGE_1=true LUNGE_2=true` (3 false), `RANGED_1=true` (2/3 false),
  `VERTICAL_1=true` (2/3 false), `COMBO_*` los 3 en `false`. Esto es evidencia de que el shadow SÍ
  se está escribiendo con datos reales y diferenciados (no una copia plana de P1 ni un bug que deje
  todo en un solo valor) - útil como caso de prueba concreto y falsificable para el próximo
  playtest: con este estado exacto, P2 debería poder cargar el ataque cargado sólo a nivel 1 (1.5s,
  sin proyectil), Lunge hasta nivel 2, Ranged sólo nivel 1, Vertical sólo nivel 1, y el combo normal
  (sin bonus de daño/ejecución en el 3er golpe, ya que `COMBO_1` está en `false`) - si el próximo
  playtest muestra a P2 ejecutando algo por encima de este techo exacto (ej. `CHARGED_3` con
  proyectil, o `RANGED_2/3`), eso confirma el bug con datos concretos en vez de una impresión
  general "P2 hace lo mismo que P1".

  **No se encontró, con lectura de código exhaustiva (choke point + los 5 gates de entrada +
  grep del decompilado completo), ningún bypass vivo en el código actual** que explique
  "P2 ejecuta lo que P1 tiene desbloqueado sin importar su propio shadow" para
  `ChargedAttack/LungeAttack/RangeAttack/VerticalAttack` - la arquitectura de la Ronda 61/62 está
  correctamente cableada en todos los puntos verificables por lectura estática. Para `Combo`
  específicamente, gran parte de lo reportado probablemente no es un bug (ver arriba - la animación
  del combo nunca estuvo gateada por skill, ni en vanilla).

  **Lo que la lectura de código NO puede confirmar ni descartar por sí sola** (límite explícito de
  este método, no una laguna de esfuerzo): si el `Prefix` de HarmonyX sobre `GetLastUnlockedSkill()`
  realmente intercepta EN RUNTIME cada uno de los caminos de invocación encontrados (`call` directo
  desde las 5 subclases, y los 2 `MethodInfo.Invoke()` por reflection en
  `RangedAndVerticalAttackFixes.cs`/potencialmente otros) de forma consistente - HarmonyX en Mono usa
  un detour a nivel de método (no depende de si el call site es `call`/`callvirt`/reflection), así
  que en teoría debería cubrir todos los caminos por igual, pero esto es una afirmación sobre el
  mecanismo interno del runtime, no algo confirmable leyendo sólo el C# decompilado.
  **Recomendación concreta para la próxima sesión** (no aplicada esta ronda, es diagnóstico): un
  único log edge-triggered dentro del propio `Ability_GetLastUnlockedSkill_P2_Patch.Prefix`
  (`[DashParryDebug] P2 GetLastUnlockedSkill: owner=... idsChecked=[...] result=...`, logueado sólo
  cuando el `id` resuelto cambia respecto a la última vez para esa `___unlocableSkill` en particular,
  para no saturar el log) daría la confirmación definitiva en runtime de qué tier resuelve
  efectivamente el choke point para P2 en cada ability, correlacionado con el estado real del shadow
  en ese instante - más confiable que seguir razonando en abstracto sobre HarmonyX.

  **Hallazgos colaterales menores, no arreglados (fuera de pedido explícito de esta ronda)**:
  - `LungeAttack.OnCastStart()` (`LungeAttack.cs:227`): `base.LastUnlockedSkillId =
    GetLastUnlockedSkill().id;` sin null-check - si algo llamara `.Cast()` sobre un `LungeAttack`
    sin ningún `LUNGE_x` desbloqueado (hoy no ocurre en la práctica porque `CastLungeAttack()` ya
    filtra con `IsAvailable` antes de llegar a `Cast()`), esto tiraría `NullReferenceException` y
    dejaría el cast a mitad de camino (`IsUsingAbility=true` ya seteado por `base.OnCastStart()`,
    pero `fixedCastScheduled`/`ToggleAbilities` de `Ability.Cast()` nunca correrían por la excepción
    interrumpiendo la cadena de llamadas). Frágil pero no explotado por ningún camino real
    confirmado hoy.
  - `Stats/PermanentStatsDebugPanel.cs:151`: el label del panel F8 sigue diciendo "RangeAttack roto
    por hardcodeo Core.Logic.Penitent (ver NOTES Ronda 60) - tier no afecta aun" - desactualizado
    desde que la Ronda 62 arregló `RangeAttack` por completo; sólo cosmético (texto de UI de debug),
    pero engañoso para lectura futura.

  **No se tocó ningún archivo de código esta ronda** (tarea explícitamente solo investigación).

- **Ronda 74** - Solo instrumentación (sin fix) para confirmar en runtime el choke point del Skill Tree de P2. Pedido explícito de no tocar comportamiento hasta tener log.

  **1. `Stats/Player2SkillManager.cs:Ability_GetLastUnlockedSkill_P2_Patch` (líneas ~159-184):** agregado `Dictionary<Ability,string> lastLoggedResult` edge-triggered por instancia de `Ability` (cada una de las 5 subclases attachadas a P2 tiene su propio `unlocableSkill`). Log en cada `Prefix` que entra: `owner= P1/P2 + InstanceID` + `branch=shadow/vanilla` + `list=[...]` + `result=id/null`, solo cuando cambia el `id` devuelto para esa misma instancia (no global, evita spam pero permite ver `CHARGED_1` vs `CHARGED_3` por separado). Verifica que `Player2SkillManager` no queda sombreado solo en memoria sino que el `Prefix` realmente se ejecuta.

  **2. `Dash/DashAndInputBlockers.cs:607` (`DashBehaviour_OnStateUpdate_Patch`):** el `CastLungeAttack()` vía reflection era sospechoso (Ronda 73: `MethodInfo.Invoke` podría bypasear HarmonyX). Agregados dos logs alrededor del `Invoke` (`pre-invoke` y `post-invoke` con `result=bool`), indicando `owner=P1/P2 + DashBehaviour InstanceID + Penitent InstanceID + frame` para confirmar si alguna vez se invoca sobre la instancia equivocada o si el `IsAvailable` ya venía contaminado antes.

  **3. `Abilities/RangedAndVerticalAttackFixes.cs`:** `RangeAttack_OnUpdate_P2_Patch` ya reimplementaba `GetLastUnlockedSkill` por reflection, se extendió con `Dictionary<RangeAttack,string> lastRangeSkill` edge-triggered que loguea `GetLastUnlockedSkill -> id (owner=P2)` solo al cambiar; `VerticalAttack_OnUpdate` (gate inline `GetLastUnlockedSkill()==null`) - no tenía log dedicado, se añadió `VerticalAttack_OnUpdate_SkillLog_Patch` (`Postfix` sobre `VerticalAttack.OnUpdate`) con `Dictionary<VerticalAttack,string>` que loguea solo cuando P2 está en aire con `isJoystickDown` y el `id` cambia; `ChargedAttack` vía `AnimatorInyector.ChargeAttackTriggered()` (owner correcto por instancia `Awake:GetComponent<Penitent>`) - se añadió `AnimatorInyector_ChargeAttackTriggered_SkillLog_Patch` (`Prefix` sobre `ChargeAttackTriggered`) que loguea `_penitent.ChargedAttack.IsAvailableSkilledAbility + lastSkill id` (vía reflection `HasEnoughFervour` + `GetLastUnlockedSkill`) edge-triggered por `AnimatorInyector` instancia.

  **Build:** `dotnet build` 0 errores tras cada cambio (1 error intermedio `CS0246 Penitent` por falta de `using Gameplay.GameControllers.Penitent` corregido).

  **Playtest requerido (slot 0 con patrón parcial conocido `CHARGED_1=true, LUNGE_1/2=true RANGED_1=true VERTICAL_1=true resto false`):** con P1 y P2 activos, hacer que **P2** intente en orden: Lunge (probar tier 3 - debería fallar si shadow correcto), Ranged, Charged (ver si proyectil `CHARGED_3` sale), Vertical (down+attack en aire). Pasar `BepInEx/LogOutput.log` filtrado por `[DashParryDebug]` y `[Ability]`. **Combo excluido** (ya confirmado que no gatea por skill, ni vanilla lo hace).

- **Ronda 75** - Desbloqueo de instrumentación (Parte 1 y 2 del plan) + preparación de playtest limpio (Parte 3).

  **Parte 1 — Harmony roto `GetEquippedPrayer`:** decompilado `InventoryManager` (`ilspycmd`, `Assembly-CSharp.dll`) confirma que `Framework.Managers.InventoryManager` **no tiene** `GetEquippedPrayer()` sin parámetros; el método real es `Gameplay.GameControllers.Penitent.Abilities.PrayerUse.GetEquippedPrayer() : Prayer { return Core.InventoryManager.GetPrayerInSlot(slot); }` (`PrayerUse.cs:140472`). El `[HarmonyPatch(typeof(InventoryManager),"GetEquippedPrayer")]` de `Stats/InventoryGameplayPatches.cs:55` buscaba firma inexistente -> `HarmonyX DeclaredMethod not found -> Undefined target method` al arrancar (log líneas 28-33). Fix en `Stats/InventoryGameplayPatches.cs` (`PrayerUse_GetEquippedPrayer_P2_Patch`): cambiado target a `typeof(PrayerUse)` con `Postfix(PrayerUse __instance, ref Prayer __result)` que si `__instance.EntityOwner==CoopLocal.Player2` devuelve `Player2InventoryManager.GetEquippedPrayerObj()` (null si P2 no tiene nada, sin fallback a P1), para P1 deja vanilla intacto (`return` sin tocar `__result`). Build 0 errores; el warning `HarmonyX` debe desaparecer en el próximo arranque (pendiente de confirmar en vivo).

  **Parte 2 — `DashDustGenerator` NRE en 3ra transición `D02Z01S01`:** decompilado `DashDustGenerator:OnStart` setea `_penitent = (Penitent)base.EntityOwner` una sola vez; `GetDashDustPosition()` hace `_penitent.DamageArea.DamageAreaCollider.bounds` con solo `if (_penitent.DamageArea==null) return Vector3.zero` (no cubre `_penitent==null` ni `DamageAreaCollider==null`). Durante `LevelManager.ChangeLevel` con `DontDestroyOnLoad` (P2) + recreación de P1 (`SpawnManager.CreatePlayer`), el coroutine `DelayStopDash` del `DashDustGenerator` disparado justo antes del cambio sobrevive a la destrucción del GO viejo (`P1` viejo) y re-entra con `_penitent` stale/null -> `NullReferenceException` cada frame hasta nuevo P1. Afecta a P1, P2 o ambos según qué instancia disparó el dash antes del cambio; explica cierre temprano de sesión del log ( `Saving global data` inmediato tras NRE). Fix en `Combat/DashDustFix.cs` nuevo: `DashDustFixShared` con `FieldInfo _penitent`, 4 `Prefix` (`GetDashDustPosition`, `GetStopDashDust(float)`, `GetStopDashDust()`, `GetStartDashDust`) que si `_penitent==null` re-resuelven `GetComponentInParent<Penitent>()` y cachean, y si sigue null o `DamageArea/collider` null hacen early-return (`Vector3.zero` o `return false` sin ejecutar vanilla). Build 0 errores.

  **Parte 3 — Playtest limpio pendiente (lo más importante, sin esto no avanza el diagnóstico real del skill tree):** DLL ya desplegado `C:\Program Files (x86)\Steam\steamapps\common\Blasphemous\Modding\plugins\CoopLocal.dll` con fixes de Parte 1/2 (timestamp fresco). **No se pudo jugar interactivamente en este entorno** (sin input real de usuario), por lo que no se inventan datos: se deja guía explícita para el usuario:
  1. Cargar partida slot 0 (patrón `CHARGED_1, LUNGE_1/2, RANGED_1, VERTICAL_1 true`).
  2. Confirmar `P2 spawned at` en log en vivo.
  3. Con **P2** intentar en orden: **Lunge** (dash+attack, tier 3 debería fallar), **Ranged**, **Charged** (mantener ataque), **Vertical** (down+attack en aire).
  4. Pasar `BepInEx/LogOutput.log` filtrado por `[DashParryDebug]` y `[Ability]`.

  **Build:** `dotnet build` 0 errores tras cada cambio (Parte 1+2). **Siguiente Ronda:** con ese log se decidirá el fix real del skill tree (no implementado aquí a propósito).

- **Ronda 76** - Logging persistente + verificación de los 4 gates reales del Skill Tree (sin fix de gameplay, solo instrumentación).

  **Parte 1 — Fix pérdida de log:** `Diagnostics/Diagnostics.cs:DashParryDebugLog.Log` ahora hace `ModLog.Info` + `File.AppendAllText(Application.persistentDataPath/CoopLocalMod/debug_log.txt, $"[{ts}] {line}")` con `Directory.CreateDirectory` y `try/catch`, append-only (nunca trunca al iniciar, sobrevive relanzos). `PersistentLogPath` expone `Path.Combine(Combine(persistentDataPath,"CoopLocalMod"),"debug_log.txt")` (corregido `CS1501` por `Path.Combine` de 3 args inexistente en target framework). Build 0 errores.

  **Parte 2 — Verificación de los 4 gates (ilspycmd, no asumir Ronda 73):** decompilados completos:
  1. `ChargedAttack.IsAvailableSkilledAbility` (`ChargedAttack.cs`) => `CanExecuteSkilledAbility() && HasEnoughFervour`; `CanExecute...` => `!useUnlocableSkill || GetLastUnlockedSkill()!=null` (choke ya parcheado), no bypass directo a `SkillManager`.
  2. `LungeAttack` vía `DashBehaviour.CastLungeAttack()` (`DashBehaviour.cs:140-152`, invocado por reflection en `DashAndInputBlockers.cs:607`) => `componentInChildren<LungeAttack>().IsAvailable` (`IsAvailable=>CanExecute...&&HasEnoughFervour`) + `_penitent` ya arreglado por `DashBehaviour_OnStateEnter_Patch` (Ronda 62). Sin hardcodeo a P1 en el gate.
  3. `RangeAttack` reimplementado en `RangedAndVerticalAttackFixes.cs:RangeAttack_OnUpdate_P2_Patch` ya invoca `GetLastUnlockedSkillMethod.Invoke(__instance,null)` (reflection sobre choke parcheado) antes de `CastRangeAttack`; extendido con log edge-triggered `lastRangeSkill` del `id` devuelto.
  4. `VerticalAttack.OnUpdate()` gate inline `if((last=GetLastUnlockedSkill())==null) return;` antes de `Cast()`; `Rewired GetButtonTimedPress` ya parcheado por transpiler (Ronda 62) a `Player2Input`, no bypass. Se añadió `VerticalAttack_OnUpdate_SkillLog_Patch` (Postfix) edge-triggered que loguea `GetLastUnlockedSkill->id` solo cuando P2 está en aire con `isJoystickDown`.
  **Conclusión:** las 4 SÍ pasan por el choke `Ability.GetLastUnlockedSkill` ya parcheado; no existe bypass directo a `Core.SkillManager.IsSkillUnlocked` en ninguno de los 4 caminos que gatean el cast. Si el shadow no se consulta, debe ser porque el propio choke no se está ejecutando para las instancias reales de P2 en juego (no por bypass estático).

  **Logging adicional para descartar identidad de referencia (P2 recreado Ronda 55/63):** `Stats/Player2SkillManager.cs:Ability_GetLastUnlockedSkill_P2_Patch` ampliado para loguear `ownerId vs P2Id + ==/!=` además de `branch/list/result` (edge-triggered por `Ability` instancia). `DashAndInputBlockers.cs:CastLungeAttack` ya loguea `owner + DashBehaviour InstanceID + frame pre/post result`; `RangedAndVerticalAttackFixes.cs` y `AnimatorInyector.ChargeAttackTriggered` (`AnimatorInyector_ChargeAttackTriggered_SkillLog_Patch`) loguean `IsAvailableSkilledAbility + lastSkill + HasFervour` por `AnimatorInyector` instancia. Todo vía `DashParryDebugLog` ahora persistido en `debug_log.txt`.

  **Build:** `dotnet build` 0 errores (1 error intermedio `CS0246 Penitent` por falta de `using` y `CS1501 Combine` corregidos). **Pendiente playtest:** repetir los 4 intentos de P2 (slot 0, patrón `CHARGED_1/LUNGE_1-2/RANGED_1/VERTICAL_1 true`) y pasar **ambos** logs: `BepInEx/LogOutput.log` (filtrado `[DashParryDebug]/[Ability]`) y **`%USERPROFILE%\AppData\LocalLow\TheGameKitchen\Blasphemous\CoopLocalMod\debug_log.txt`** (persistente, no se pierde al relanzar). Combo excluido.

  **Auditoría posterior (misma ronda, otra sesión) - antes del playtest, a pedido explícito del
  usuario dado el historial de entregas rotas (Rondas 57/59/62).** `dotnet build` re-verificado 0
  errores de forma independiente (no se confió en el reporte previo). Encontrados y arreglados 3
  problemas, ninguno de compilación:

  1. **Bug real, crítico para el propio objetivo de la ronda** -
     `Stats/Player2SkillManager.cs:Ability_GetLastUnlockedSkill_P2_Patch`: el log de "identidad de
     referencia (P2 recreado Ronda 55/63)" que esta ronda dice haber agregado estaba escrito
     *después* del `if (__instance.EntityOwner != CoopLocal.Player2) return true;` - exactamente el
     branch que descarta el único caso que el log dice estar chequeando. Si `EntityOwner` alguna vez
     resultara **stale** (un componente `Ability` de P2 huérfano cuyo `EntityOwner` quedó apuntando a
     un `Penitent` viejo tras un respawn/transición, en vez del `CoopLocal.Player2` actual), el método
     retorna `true` ahí mismo y corre vanilla en silencio - el `DashParryDebugLog.Log` de esa rama
     nunca se alcanza para ese caso exacto. Con el código tal cual se entregó, `ownerId{eq}p2Id` sólo
     podía imprimir `"=="` (código muerto, `"!="` era literalmente inalcanzable) - el próximo playtest
     no habría podido confirmar ni descartar la hipótesis de identidad stale, que es la pregunta
     central de toda la Ronda 73-76. **Fix**: el chequeo de identidad (`null`/`P1`/`P2-current`/
     `STALE`, comparando contra `Core.Logic.Penitent` y `CoopLocal.Player2` explícitamente) se movió
     antes del `return true`, cubre las 10 instancias de `Ability` en juego (5 skills x P1 y P2), no
     sólo las que ya pasaron el filtro `==Player2`, edge-triggered por instancia (dict separado
     `lastLoggedIdentity`) para no duplicar el log de resultado del shadow que sigue existiendo debajo
     sin cambios de comportamiento.
  2. **Perf, no crítico pero real**: `Abilities/RangedAndVerticalAttackFixes.cs:
     AnimatorInyector_ChargeAttackTriggered_SkillLog_Patch` resolvía `AccessTools.Property`/
     `AccessTools.Method` (reflection lookup sin cachear) en cada invocación - y `ChargeAttackTriggered()`
     corre **todos los frames que P2 está en el suelo** (confirmado vía `ilspycmd`:
     `AnimatorInyector.UpdateActions() -> ChargedAttack() -> ChargeAttackTriggered()` incondicional
     mientras `_isGrounded`), no sólo cuando el log efectivamente dispara. Cacheado como campos
     `static readonly` (mismo patrón ya usado por `RangeAttackP2Shared` en el mismo archivo).
  3. **Rendimiento/robustez del logging persistente**: `Diagnostics/Diagnostics.cs:
     DashParryDebugLog.Log` hacía `Directory.CreateDirectory` en cada llamada (repetido
     innecesariamente una vez confirmado que el directorio ya existe) y no tenía ningún lock alrededor
     del `File.AppendAllText` - en la práctica Unity es single-threaded así que no había carrera real
     hoy, pero es una insurance barata contra un futuro caller fuera del hilo principal. Agregado
     `directoryEnsured` (bool cacheado tras el primer `CreateDirectory` exitoso) + `lock` estático
     alrededor de la sección de archivo. No se agregó buffering/`StreamWriter` persistente a propósito
     (abrir-escribir-cerrar en cada línea es justamente lo que garantiza que no se pierda nada si el
     juego crashea, que es el objetivo explícito de esta ronda - un writer bufferizado lo comprometería).

   Verificado también, sin encontrar problemas: `Dash/DashAndInputBlockers.cs` (log de
   `CastLungeAttack`, owner resuelto fresco vía `GetComponentInParent<Penitent>()` en cada llamada, no
   puede quedar stale de la misma forma) y los otros dos logs de
   `RangedAndVerticalAttackFixes.cs`/`VerticalAttack` - estos tres están correctamente acotados a "qué
   tier resuelve durante un intento real", una pregunta distinta a la de identidad-stale que ya cubre
   el fix del punto 1 de forma centralizada (los 5 abilities pasan por el mismo choke point
   `Ability.GetLastUnlockedSkill()`). `dotnet build` 0 errores tras cada uno de los 3 fixes, DLL
   redesplegado con timestamp fresco confirmado en `Modding/plugins/CoopLocal.dll`.

- **Ronda 76** - Diagnóstico del playtest con patrón claro (sin log sobreviviente, se perdió al
  relanzar) + desbloqueo del próximo playtest.

  **Contexto recibido (no logueado, se perdió al relanzar antes de copiar `debug_log.txt`):**
  1. P1 `false` + P2 Hold(Charged, shadow `true`) → falla
  2. P1 `false` + P2 Lunge → falla
  3. P1 `true` + P2 `true` → funciona
  4. P1 `true` + P2 `false` → P2 **sí puede** (hereda) — patrón `P2 resultado == global P1`, no shadow, 100% consistente con `Ability_GetLastUnlockedSkill_P2_Patch` nunca ejecutado para P2 (aunque compila con `___unlocableSkill` correcto tras Ronda 62). Ronda 73 ya había descartado bypass estático (grep 2079 archivos: solo 2 call sites `IsSkillUnlocked` = choke + `NewInventory_Skill`).

  **Log 11:34 (`BepInEx/LogOutput.log`, 228 líneas, `MainMenu->D02Z01S01`, truncado en `PLAYER_LOGIC` toggle):** **0 líneas `[Ability]` / `P2 spawned` / `[DashParryDebug]` de skill** — sesión corta sin gameplay, sobrescribió la sesión de los 4 tests. Como `Diagnostics.cs` ahora persiste a `CoopLocalMod/debug_log.txt` append-only, el `debug_log.txt` sí conservó 6 líneas viejas `00:21` (`Charged P2 null -> CHARGED_1 true` tras toggle F8) que prueban que el Prefix **sí se ejecutó** en una sesión anterior con P2 `InstanceID -17958/-29918/-41652` (P2 recreado), pero no es la sesión de los 4 tests.

  **Bloqueante colateral aún en 11:34:** `Stats/InventoryUI.cs: Grid_Update_Toggle_Patch` `HarmonyPatch(NewInventory_LayoutGrid,"Update")` → `DeclaredMethod not found` (la clase no tiene `Update` público; `Update` real es de `NewInventoryWidget`). `PatchAll` sigue (HarmonyX atrapa por patch), pero ensucia log. Fix en esta ronda: eliminado ese `HarmonyPatch` completo (el toggle `F7` para inventario se deja para `ShowLayout Postfix` ya existente o futuro driver, no bloqueante para skill tree). `Stats/InventoryGameplayPatches.cs` `PrayerUse_GetEquippedPrayer_P2_Patch` ya corregido a `typeof(PrayerUse)` con `owner==Player2 => Player2InventoryManager` en Ronda 75, y `Combat/DashDustFix.cs` 4 Prefix ya mitiga el `NullReferenceException` de `DashDustGenerator` de la 3ª transición (no reaparece en 11:34, solo el bloqueante de Grid).

  **Fix de instrumentación en esta ronda:** `Stats/Player2SkillManager.cs:Ability_GetLastUnlockedSkill_P2_Patch` ampliado para loguear identidad de referencia `ownerId vs P2Id ==/!=` además de `branch/list/result` (edge-triggered por `Ability` instancia), ya desplegado, pero no hay log nuevo de los 4 tests para evaluar.

   **Instrucción para próximo playtest (persistente, no se pierde):** cargar slot 0 (patrón `CHARGED_1/LUNGE_1-2/RANGED_1/VERTICAL_1 true`), confirmar `P2 spawned at` en vivo, que **P2** haga `Hold(Charged) / Lunge tier3 (debe fallar) / Ranged / Vertical` sin tocar `F7` de inventario, y **copiar `debug_log.txt` antes de relanzar** (`%USERPROFILE%\AppData\LocalLow\TheGameKitchen\Blasphemous\CoopLocalMod\debug_log.txt` + `BepInEx/LogOutput.log` filtrado `[Ability]`). `dotnet build` 0 errores.

- **Ronda 77** - Bug de camara Coop ~6u mas arriba que P1 solo + snap en cada ciclo F10 (implementado, verificado por build, pendiente de playtest real).

  **Causa raiz confirmada contra DLLs reales (ilspycmd, no stub de `bin/Development`):**
  `Framework.Managers.LevelManager.UpdateNewCameraParams()` llama en secuencia:
  1. `Gameplay.GameControllers.Camera.CameraManager.UpdateNewCameraParams()` - hace
     `ProCamera2D.RemoveAllCameraTargets()` + `AddCameraTarget(P1, 1,1,0, new Vector2(0,6))` +
     snap + `Smoothness=0` por 1s.
  2. `Gameplay.GameControllers.Camera.CameraPlayerOffset.UpdateNewParams()->SetCameraTarget()` -
     recorre `ProCamera2D.CameraTargets`, encuentra el primer target taggeado `"Penitent"` (P1,
     agregado primero) y le pone `TargetOffset = Vector2.zero`. El `(0,6)` del paso 1 es
     transitorio y queda anulado en el mismo frame de carga. El offset vertical estable vanilla
     vive en `Com.LuisPedroFonseca.ProCamera2D.ProCamera2D.OverallOffset` (campo global sumado
     en `Move()` y `Reset()`, nunca escrito por codigo - baked en Inspector de escena/prefab).

  El mod (`Camera/Camera.cs`) trataba el `(0,6)` como offset real y se lo copiaba a P2 en dos
  sitios: `CameraManager_UpdateNewCameraParams_Patch.AddPlayer2AsCameraTarget` linea ~115
  (`AddCameraTarget(P2, ..., new Vector2(0,6))`) y `CameraTargetDebugToggle.TargetOffset` linea
  ~152 (`new Vector2(0,6)` usado en `SetTargetPresent` para re-agregar P1/P2 en cada F10). Como
  el Postfix que agrega a P2 corre dentro de `CameraManager.UpdateNewCameraParams` (antes de que
  `LevelManager` llame a `CameraPlayerOffset.SetCameraTarget`), solo P1 era zerado y P2 quedaba
  con `(0,6)` permanente tras cada carga normal - asimetria de punto medio (+3u efectivo al
  promediar). En cada ciclo F10, `SetTargetPresent` re-agregaba el target removido con `(0,6)`
  sin que `SetCameraTarget` volviera a correr (solo corre en carga de nivel real), dejando ambos
  en `(0,6)` simultaneo -> salto de +6u completo reportado.

  **Fix (minimo, 2 lineas de valor, sin tocar `CameraPlayerOffset`/`CentreTargetOnStart` ni
  agregar patch nuevo - el offset global vanilla `OverallOffset` sigue siendo la unica fuente):**
  1. `Camera/Camera.cs:115` `AddCameraTarget(P2, ..., Vector2.zero)` en vez de `new Vector2(0,6)`.
  2. `Camera/Camera.cs:152` `TargetOffset = Vector2.zero` (campo `CameraTargetDebugToggle`).
  3. Comentarios en ambos sitios actualizados (Ronda 77) explicando por que `Vector2.zero` y
     referenciando `CameraPlayerOffset.SetCameraTarget`/`OverallOffset` en vez del razonamiento
     previo "Same weight/offset the game itself uses for P1" (incorrecto - el `(0,6)` de P1 no es
     estable).

   **Build:** `dotnet build -p:SolutionDir="..."` 0 errores. **Pendiente de playtest real (no
  verificable solo por lectura de codigo):** F10 `Coop->P1Only` debe quedar centrado sin salto de
  6u, y Coop con P1/P2 a la misma Y debe quedar a la misma altura de encuadre que P1 solo. No
  marcado como confirmado en juego, solo como implementado + verificado por build.

- **Ronda 78** - Bug de camara horizontal en F10 "P2 solo" (P2 corrido ~1.5u a izquierda/derecha) y sesgo Coop hacia P1 (implementado, verificado por build, pendiente de playtest real). Distinto de Ronda 77 (vertical TargetOffset).

  **Causa raiz confirmada contra DLL real (`Assembly-CSharp.dll`, ilspycmd, no stub):**
  `Gameplay.GameControllers.Camera.CameraPlayerOffset` (`CameraPlayerOffset.cs`) maneja "forward focus" horizontal:
  `private Penitent _penitent; [Range(-5,5)] public float XOffset = 1.5f;`
  `UpdateNewParams() { _penitent = Core.Logic.Penitent; ... _playerCurrentOrientation = _penitent.Status.Orientation; SetCameraXOffset(_playerCurrentOrientation, 0f); ... SetCameraTarget(...); }`
  `LateUpdate()` corre todos los frames: `if (!_penitent) return; _playerCurrentOrientation = _penitent.Status.Orientation; if (_playerLastOrientation != _playerCurrentOrientation) { DOTween.Kill("ForwardFocus"); if (_deltaXOffsetRecoverTime >= XOffsetRecoverTime) SetCameraXOffset(...); } if (IsCameraForwardBlocked()) StopCameraTween();`
  `SetCameraXOffset(EntityOrientation o, float t) { if (PlayerTarget != null) { Vector2 v = new Vector2(XOffset, YOffset); if (o==Left) v.x = -XOffset; DOTween.To(x=>_proCamera2D.OverallOffset.x=x, _proCamera2D.OverallOffset.x, v.x, t).SetEase(OutSine).SetId("ForwardFocus"); } }`
  `_penitent` se asigna UNA sola vez en `UpdateNewParams()` (solo en carga real via `LevelManager.UpdateNewCameraParams()`) y nunca se reasigna segun que target siga realmente la camara. `OverallOffset` es campo publico mutable de `ProCamera2D` (mismo que Ronda 77, sumado global en `Move()/Reset()`). En F10 P2Only la camara sigue a P2 pero `_penitent` sigue siendo P1, el offset se calcula con la orientacion de P1 desplazando a P2 ~XOffset (1.5). Mismo sesgo en Coop (siempre a favor de P1, grep confirma que ningun otro sitio en `Assembly-CSharp.dll` distingue el Penitent seguido).

  **Fix minimo en `Camera/Camera.cs` (no tocar `CentreTargetOnStart`, solo `CameraPlayerOffset`):**
  1. `[HarmonyPatch(typeof(CameraPlayerOffset), "UpdateNewParams")] Postfix` corrige tras el vanilla: si `Mode==P2Only && Player2!=null` setea `_penitent=P2`, `_playerCurrentOrientation/_playerLastOrientation = P2.Status.Orientation`, mata tween "ForwardFocus" y tweunea `OverallOffset.x` inmediato (0f) a `±XOffset` segun P2, actualiza `DefaultTargetOffset`; si `Mode==Coop` lleva `OverallOffset.x->0f` inmediato y pone `_penitent=null` para desactivar forward tracking y deja `DefaultTargetOffset` centrado; si `P1Only` deja vanilla.
  2. `[HarmonyPatch(typeof(CameraPlayerOffset), "LateUpdate")] Prefix` decide por frame: `P2Only` asegura `_penitent=P2` y deja correr vanilla con orientacion correcta; `Coop` asegura `OverallOffset.x` se tweunea a 0 con `ElapsedTime` si hace falta y hace `return false` para saltarse el tracking de P1 y el `IsCameraForwardBlocked()` de P1; `P1Only` re-restaura `_penitent=P1` si venia nulado por Coop y deja vanilla. Usa `AccessTools.Field` para `_penitent/_proCamera2D/_playerCurrentOrientation/_playerLastOrientation` (HarmonyX, 4 guiones para `_penitent`) y `DG.Tweening`.

   **Build:** `dotnet build -p:SolutionDir="..."` 0 errores. **Pendiente de playtest real:** F10 P2Only con P1 mirando Left/Right debe mantener a P2 centrado (±0, no ±1.5 segun P1), y Coop debe quedar centrado sin sesgo lateral (OverallOffset.x≈0) con ambos a la misma X se ve igual que P1 solo. No confirmado en juego, solo implementado + build.

- **Ronda 79** - Rezo de P2 anima pero no castea (implementado, verificado por build, pendiente de playtest real).

  **Causa raiz (lectura directa del repo, no requiere decompilar vanilla):**
  `Stats/Player2InventoryManager.cs:124-132` `IsPrayerOwned(string id)` y `IsOwnedBead` ya hacen fallback a global cuando el shadow de P2 esta vacio (`if (ownedPrayers.Count==0 && Core.InventoryManager!=null) foreach(var p in Core.InventoryManager.GetPrayersOwned()) ...`), misma filosofia `IsOwnedBead`. Pero `GetEquippedPrayerObj():137-141` (el que usa `Stats/InventoryGameplayPatches.cs:57-68` `PrayerUse_GetEquippedPrayer_P2_Patch` `Postfix(PrayerUse __instance, ref Prayer __result)` donde `if(owner==Player2) __result = Player2InventoryManager.GetEquippedPrayerObj()`) **no** tenia fallback: `if(string.IsNullOrEmpty(equippedPrayer) || Core.InventoryManager==null) return null; return GetPrayer(equippedPrayer);`. Antes de Ronda 70 (sistema propio de inventario P2) P2 no tenia override y heredaba implicito el `PrayerUse.GetEquippedPrayer()` global de P1 (`Core.InventoryManager.GetPrayerInSlot(0)`). Desde Ronda 70 el Postfix fuerza `null` permanente si P2 nunca equipo via F7 (`Stats/InventoryUI.cs` `Grid_EquipObject_P2_Patch`), y `PrayerUse_P2Input_Patch`/`PrayerCasterTracker` siguen animando (`AuraTransform` via `CanUsePrayer` solo chequea Fervour) pero `Cast()/StartUsingPrayer()->prayer.Use()` no tiene Prayer real -> sintoma exacto "anima pero no hace nada".

  **Fix minimo en `Stats/Player2InventoryManager.cs:137-149` (restaura paridad con `IsPrayerOwned`):**
  `GetEquippedPrayerObj()` ahora: `if(Core.InventoryManager==null) return null; if(!string.IsNullOrEmpty(equippedPrayer)) return Core.InventoryManager.GetPrayer(equippedPrayer); return Core.InventoryManager.GetPrayerInSlot(0);` — fallback al equipado global (el de P1). Verificado nombre real en DLL (`Framework.Managers.InventoryManager` no tiene `GetEquippedPrayer()`, tiene `GetPrayerInSlot(int slot)`; `Gameplay.GameControllers.Penitent.Abilities.PrayerUse.GetEquippedPrayer()` vanilla es `return Core.InventoryManager.GetPrayerInSlot(slot)` slot 0) y en el repo (`Prayer/PrayerSystem.cs:104` loguea `__instance.GetEquippedPrayer()`). Asi si P2 tiene equip explicito via F7 se prioriza shadow, si no hereda el de P1 como antes de Ronda 70. No tocado `UnequipPrayer()` (vacio sigue cayendo en mismo fallback, indistinguible de "nunca equipo", aceptado como `IsPrayerOwned` vacio = fallback), no tocado `Stats/InventoryGameplayPatches.cs` (Postfix ya bien, ahora devuelve no-null) ni `Prayer/PrayerSystem.cs` (Ronda 39/43/62 siguen correctos).

   **Build:** `dotnet build -p:SolutionDir="..."` 0 errores. **Pendiente de playtest real:** 1) P2 sin equipar nada via F7, con P1 teniendo rezo equipado global, pulsar Q de P2 -> debe castear mismo rezo que P1 (fallback); 2) equipar rezo distinto para P2 via F7 menu inventario → Q de P2 debe castear su propio rezo y Q de P1 el suyo, sin cruzarse; verificar que `UnequipPrayer` (des-equipar) vuelve a fallback y no queda null permanente.

- **Ronda 80** - Lunge attack de P2 no hace daño (implementado, verificado por build, pendiente de playtest real). No es mismo mecanismo que Ronda 79 (rezo `Player2InventoryManager`).

  **Causa raiz confirmada contra DLL real (`Assembly-CSharp.dll`, ilspycmd, no stub):**
  `Gameplay.GameControllers.Penitent.Abilities.LungeAttack:AttackAreaOnStay` solo aplica daño si `if(CanHit && Casting && _newEnemyHit) AttackDamageableEntity(...)`. `CanHit {get;set;}` default false y `OnCastEnd` lo pone false. Nunca se pone true por C# normal — se activa via Animation Event `Gameplay.GameControllers.Penitent.Animator.PenitentAttackAnimations.CanLungeAttack(Activation activation)` (hereda de `Gameplay.GameControllers.Entities.Animations.AttackAnimationsEvents`, enum anidado `Activation {True, False}` public). Vanilla:
  `public void CanLungeAttack(Activation activation) { LungeAttack l = Core.Logic.Penitent.GetComponentInChildren<LungeAttack>(); l.CanHit = activation==Activation.True; }`
  A diferencia de todos los demas metodos de esa misma clase (`DamageByFall`, `OpenAttackWindow`, `PrayerUseAttack`, etc.) que usan correctamente `private Penitent _penitent` resuelto en `Awake() via GetComponentInParent<Penitent>()` per-instance (confirmado sin lazy-init familia 1), este unico metodo usa `Core.Logic.Penitent` global (siempre P1). El evento se invoca por `SendMessage` sobre el Animator de P2 — la instancia que recibe el call SI es la de P2, pero dentro hardcodea P1, asi que `CanHit=true` va al LungeAttack de P1, no al de P2. `AttackArea` de P2 detecta overlap correcto per-instance pero su `CanHit` es false → `if(CanHit&&...)` nunca pasa y `AttackDamageableEntity()` nunca se llama. Daño `_lungeHit.DamageAmount = _penitent.Stats.Strength.Final * damageFactorByLevel` y tracking `_hitEntities` (lista instancia) SI son per-instance correctos — unico gate roto es este.

  **Datos exactos verificados ilspycmd:** `Activation` es `Gameplay.GameControllers.Entities.Animations.AttackAnimationsEvents.Activation`, `PenitentAttackAnimations` es `public class` en `Gameplay.GameControllers.Penitent.Animator` con campo `private Penitent _penitent` asignado en `Awake()`, sin bundled-init (no hay trampa familia 1). `CanLungeAttack` es `public void` no virtual/static.

  **Fix minimo en `Combat/LungeAttackFix.cs` nuevo (carpeta `Combat/` segun convencion "combat animation owner-fixes"):**
  `[HarmonyPatch(typeof(PenitentAttackAnimations), nameof(CanLungeAttack))] Prefix(Activation activation, Penitent ____penitent) { if(____penitent==null) return true; LungeAttack l = ____penitent.GetComponentInChildren<LungeAttack>(); if(l!=null) l.CanHit = activation==Activation.True; return false; }` — reemplaza metodo entero (`return false`) para P1 y P2 por igual, seguro porque para P1 `____penitent` (Awake per-instance) es exactamente `Core.Logic.Penitent` (P1 unico global), mismo resultado para P1 y nuevo correcto para P2, sin branch `owner==Player2`. Usa `____penitent` (4 guiones, HarmonyX para `_penitent`).

  **Build:** `dotnet build -p:SolutionDir="..."` 0 errores. **Pendiente de playtest real:** P2 `dash+atacar` sobre enemigo real debe aplicar daño visible (barra vida baja) y P1 lunge debe seguir igual que antes (equivalencia para P1, ver nota arriba — no debe haber cambio).

- **Ronda 81** - Menús propios para P2 con inventario/skill tree independiente.

  **Contexto:** La Ronda 70 estableció la Fase 2 (`Stats/InventoryUI.cs`) donde `InventoryViewPlayer` ya
  podía fijarse a 1, pero el Grid siempre leía `Core.InventoryManager` global (inventario P1) sin forma
  de override per-player. Adicionalmente, el toggle `ToggleInventoryMenu` en `Player2Input` abría el
  menú compartido siempre mostrando la vista P1. Esta Ronda 81 cierra ese gap:

  1. **`Input/Player2Input.cs:402`** - `MenuDown` ahora lee `Player2Pad.StartDown` en modo gamepad
     (antes solo teclado `KeyCode.I`), confirmado contra la firma real `ButtonDown("Start")` en
     `Player2Pad`.

  2. **`Stats/Player2MenuView.cs`** - Agregado `PendingOpenAsP2` flag + `RequestOpenAsP2()` /
     `ConsumePendingOpenAsP2()` métodos para comunicación bidireccional entre el input patch y el patch
     central de Show().

  3. **`Stats/Player2MenuOpen.cs`** (nuevo) - Patch Harmony `Prefix` sobre
     `NewInventoryWidget.Show(bool p_active)` que fija `SkillViewPlayer`/`InventoryViewPlayer` ANTES de
     que Show() renderice el primer frame, evitando "flash" de datos P1 al abrir como P2. Usa
     `ConsumePendingOpenAsP2()` para consumir la solicitud una sola vez.

  4. **`Input/Player2Input.cs:450-467`** - Lógica completa en `Tick()`: si el menú ya está abierto
     mostrando P1 (`alreadyOpenAsP1`), cambia a vista P2 sin cerrar el menú (fija
     `SkillViewPlayer=1`/`InventoryViewPlayer=1` y invoca `SelectTab` con el tab actual). Si el menú
     está cerrado o ya es P2, solicita apertura como P2 vía `RequestOpenAsP2()` y luego llama
     `ToggleInventoryMenu()` vanilla.

  5. **`Stats/Player2InventoryManager.cs:134-136`** - Agregado `IsSwordOwned(string id)` con
     fallback a `Core.InventoryManager.GetSwordsOwned()` idéntico al patrón ya existente de
     `IsPrayerOwned`/`IsOwnedBead`. Faltaba este equivalente para que el Grid mostrara Swords owneds
     de P2.

  6. **`Stats/InventoryUI.cs:73-111`** - Nuevo `Prefix` `Grid_ShowLayout_P2_ItemSource_Patch` sobre
     `NewInventory_LayoutGrid.ShowLayout` que, cuando `IsInventoryP2View` es true, filtra los ítems
     mostrados en el grid para que solo incluyan los que P2 efectivamente posee (usando
     `Player2InventoryManager.IsXxxOwned()`). Los tipos coverage: Prayers, Rosary Beads, Swords.
     Collectables/Reliquary/Quest permanecen vanilla. Usa `FillGridElements<T>` genérico vía
     reflection (`MakeGenericMethod`). El Postfix existente `Grid_ShowLayout_P2_Patch` continúa
     operando sin conflictos (Harmony permite Prefix+Postfix del mismo método).

  **Hallazgo colateral documentado:** `InventoryViewPlayer` nunca se tocó desde la Ronda 70, por eso
  el Grid de ítems always mostraba la colección global de P1. Ambos gaps (fallback de Sword ownership
  + filtro de ítems en ShowLayout) ya fueron arreglados en esta Ronda 81, no pendientes.

  **Build:** `dotnet build` 0 errores (verificado incrementalmente tras cada archivo modificado).
  **Pendiente de playtest real:** P2 presiona I/Start con menú cerrado -> abre mostrando SU inventario/
  skills; P2 presiona I/Start con el menú de P1 abierto -> cambia a vista P2 sin cerrar; P2 presiona
  I/Start de nuevo ya en su propia vista -> cierra; P1 sigue abriendo su menú sin cambios; equipar
 /desequipar Prayers/Beads/Sword desde la vista P2 afecta solo a P2 con la lista correcta de ítems.
"#

- Corazones de Espada (Sword) para P2: estructura base en `Player2InventoryManager` pero `Sword` hearts no auditados a fondo (probablemente `Framework.Inventory` hearts son Beads con id `HE*`).
- Cuentas de Rosario / slots de Prayer para P2: `BeadSlots` ya cubierto, equipado ahora sombreado pero HUD de P2 para beads/prayer slots aún no existe.
- HUD de P2 para sword hearts, rosary beads, prayer slots: no existen todavía (sí existen
  Flask/Purge/Fervour/Health).
- Guardado/carga del inventario completo de P2 entre sesiones (no solo dentro de la
  misma run) - fuera de alcance de todo lo hecho hasta ahora.
- `GameModeManager`/NG+/Demake: hallazgo relacionado sin confirmar impacto real - también
  hardcodean `Core.Logic.Penitent.Stats.X.SetPermanentBonus(0f)` al cambiar de modo,
  dejarían a P2 con progresión "vieja" si el coop se usa junto con esos modos.


### Ronda 82 - Drop-through inmediato de plataformas unidireccionales para P2 & Menú de inventario / equipamiento propio interactivo para P2

**Causas raíz identificadas y solucionadas:**
1. **Drop-through de plataformas unidireccionales (saltar hacia abajo / atravesar):**
   - *Causa raíz 1 (Delay y timing desincronizado):* En `PlatformCharacterInput_Update_Patch`, el salto hacia abajo estaba sujeto a un timer `player2JumpOffTimer = input.timeToJumpOff` (1.0 segundo). Tras un segundo de retardo, el jugador ya no estaba presionando o la animación se había cancelado. Además, el flag `IsJumpOff` se reiniciaba prematuramente en el frame 2 (`else { IsJumpOff = false; }`), lo que impedía que `AnimatorInyector` ejecutara la transición fluida.
   - *Causa raíz 2 (Familia 1 en StateMachineBehaviours de salto y agacharse):*
     - `JumpOffBehaviour` (`OnStateExit`): No reasignaba `____penitent = owner`, provocando que al salir del salto se restableciera la colisión 2D (`Dash.SetDefaultSkinCollision()` y `Physics.Enable2DCollision()`) sobre P1 o se omitiera en P2.
     - `JumpOffBehaviour` (`OnStateUpdate`): No reasignaba `____penitent` ni `____rootMotion`, afectando la raíz de salto de P1.
     - `CrouchDownBehaviour` (`OnStateUpdate` y `OnStateExit`): Solo `OnStateEnter` estaba parcheado. En `OnStateUpdate`, si `_penitent` apuntaba a P1, `_penitent.isJumpOffReady` se activaba para P1 y no para P2.
   - *Solución Ronda 82:*
     - Activación **inmediata** de `PlatformDropDown`: al detectar Agacharse (`crouch`) + Salto (`jump`) sobre una plataforma unidireccional (`OneWayDownCollision || !IsOnFloorPlatform`), se cancela el salto hacia arriba (`SetActionState(Jump, false)`), se activa `SetActionState(PlatformDropDown, true)`, se envía un impulso de velocidad vertical negativa (`VSpeed = -2.5f`), se dispara el trigger de animación `"JUMP_OFF"` y se limpia `PlatformDropDown` en el frame siguiente (`player2PlatformDropDownPulse = true`).
     - Parches completos sobre `CrouchDownBehaviour` (`OnStateEnter`, `OnStateUpdate`, `OnStateExit`) y `JumpOffBehaviour` (`OnStateEnter`, `OnStateUpdate`, `OnStateExit`) reasignando `____penitent = owner` y `____rootMotion` para P2.

2. **Menú de inventario propio para P2 (visualización, equipamiento y navegación):**
   - *Causa raíz 1 (Control de la UI limitado a P1):* Blasphemous delega la UI (`RewiredStandaloneInputModule` y `UIController.Update()`) a Rewired Player 0 (P1). Dado que el joystick de P2 estaba desasignado de Player 0 para evitar crosstalk en gameplay, al abrir el menú P2 no podía navegar el grid ni cambiar de pestañas ni presionar botones.
   - *Causa raíz 2 (Slots equipados en la parte superior):* En `NewInventory_LayoutGrid`, la visualización de los slots superiores (`UpdateEquipped`) no estaba parcheada para P2, mostrando siempre los rezos, cuentas y corazón de espada de P1.
   - *Causa raíz 3 (Equip/Unequip/EmptySlot):* `EquipObject`, `UnEquipObject` y `GetFirstEmptySlot` manipulaban el inventario global de P1.
   - *Solución Ronda 82:*
     - `Stats/InventoryUI.cs`:
       - `Grid_UpdateEquipped_P2_Patch`: Actualiza los slots superiores (rosario, rezo activo, corazón de espada) para reflejar los ítems equipados de P2 (`Player2InventoryManager.GetBeadInSlot`, `GetEquippedPrayerObj`, `GetSwordInSlot`, `GetRelicInSlot`) y bloquea los slots por encima de `P2.Stats.BeadSlots.Final`.
       - `Grid_IsEquipped_P2_Patch`, `Grid_EquipObject_P2_Patch`, `Grid_UnEquipObject_P2_Patch`, `Grid_GetFirstEmptySlot_P2_Patch`: Gestionan el equipamiento y desequipamiento directo en el inventario propio de P2.
       - `NewInventoryWidget_SetTab_P2_Patch`: Muestra explícitamente en el encabezado `[P2] NOMBRE (F7=P1)` o `[P1] NOMBRE (F7=P2)`.
     - `Input/Player2Input.cs` & `Stats/Player2MenuOpen.cs`:
       - Asignación dinámica de dispositivos en `ExcludedFromPlayer0`: mientras `Player2MenuView.IsInventoryP2View == true`, Player 0 recibe temporalmente el mando/teclado de P2, permitiendo a P2 navegar el grid y presionar botones directamente a través de la UI del juego.
       - `UIController_MenuNavigation_P2_Patch`: Permite alternar la vista en cualquier momento pulsando `F7`, y permite a P2 navegar entre categorías (LB/RB / Q/E), ver lore (Y / U) y cerrar el menú (B / Dash / Escape).


### Ronda 83 - Eliminación de toggle F7 y corrección de mecánicas de escalera (Ladder Jump Cooldown & Let-Go) para P2

1. **Eliminación del intercambio con F7 (Causa del bug de teclado):**
   - El toggle con F7 fuera del menú dejaba `Player2MenuView.IsInventoryP2View == 1` activo en pleno juego, lo que alteraba `ExcludedFromPlayer0` y provocaba que ambos personajes respondieran al teclado simultáneamente.
   - Se eliminó completamente la tecla F7 de `Stats/Player2MenuOpen.cs`, `Stats/SkillTreeUI.cs` y los textos de `Stats/InventoryUI.cs`.
   - `Player2MenuView.IsInventoryP2View` y `IsSkillP2View` ahora exigen explícitamente que el menú esté visible en pantalla (`Gameplay.UI.UIController.instance.IsShowingInventory`), garantizando que fuera del menú sea siempre `false`.

2. **Mecánica de escaleras de mano (Ladder jump & release):**
   - *Problema 1 (Regrab instantáneo al saltar con Arriba presionado):* En `PlatformCharacterInput.Update` vanilla, al saltar desde una escalera se llama a `controller.ResetLadderJumpTimeThreshold()`, lo que establece `m_deltaLadderJumpTimeThreshold = 0.5f`. Esto impide que `PlatformCharacterController.StartClimbing()` vuelva a engancharse a la escalera durante el salto. En P2 nunca se llamaba a este método, por lo que `m_deltaLadderJumpTimeThreshold` era siempre 0 y P2 se reenganchaba en 0 frames.
     - *Solución:* En `Movement/Movement.cs`, al detectar que P2 está escalando (`controller.IsClimbing || penitent.IsClimbingLadder`) y presiona Salto, se invoca `controller.ResetLadderJumpTimeThreshold()`. Esto otorga los 0.5s de gracia donde P2 realiza el salto limpiamente antes de poder reengancharse.
   - *Problema 2 (Soltarse o lanzarse de la escalera):* En vanilla, presionar Dash (Action 65 / Grab_Cancel) ejecuta `GrabLadder.TakeOffLadder()`, que llama a `StopClimbing()` e inicia `DisabledClimbAbilityLapse(0.5f)` (desactiva `CanClimbLadder` y retira la capa `Ladder` de `ClimbingLayers` durante 0.5s). En `Movement/LadderMechanics.cs`, el parche de P2 devolvía `false` con un bloque vacío, impidiendo que `TakeOffLadder` se ejecutara.
     - *Solución:* En `GrabLadder_OnUpdate_P2_Patch`, al presionar Dash o Abajo + Salto mientras se escala, se llama a `controller.ResetLadderJumpTimeThreshold()` y se invoca `TakeOffLadderMethod` para iniciar el cooldown de 0.5 segundos y soltarse en caída libre sin reengancharse inmediatamente.


### Ronda 84 - Sistema de rezos independiente y progresión propia para P2

**Causas raíz diagnosticadas:**
1. **Fallback forzado a P1 en `GetEquippedPrayerObj()`:** En `Player2InventoryManager.cs`, cuando P2 no tenía un rezo equipado o aún no había asignado uno, el método devolvía `Core.InventoryManager.GetPrayerInSlot(0)` (el rezo de P1). En consecuencia, P2 siempre terminaba usando el rezo de P1.
2. **`PrayerUse.GetEquippedPrayer()` y `CanUsePrayer` en `PrayerUse.cs`:** Ambos métodos leían directamente `Core.InventoryManager.GetPrayerInSlot(slot)` (el rezo de P1) tanto para P1 como para P2. El casteo de P2 (`OnCastStart()`) obtenía la instancia de P1 y evaluaba el coste de fervor del rezo de P1.
3. **Bug en `IsPrayerOwned(id)` (desaparición de rezos tras equipar uno):** La condición `if (ownedPrayers.Count == 0 && Core.InventoryManager != null)` hacía que, tras equipar o desbloquear 1 solo rezo en `ownedPrayers`, el contador fuera > 0 y todos los demás rezos descubiertos por la partida desaparecieran de la interfaz de P2.
4. **Segmentos de fervor en el HUD de P2 (`HUD/FervourHUD.cs`):** La línea 487 leía `Core.InventoryManager.GetPrayerInSlot(0)` para calcular los segmentos de coste en la barra de fervor de P2.

**Soluciones implementadas:**
- `Stats/Player2InventoryManager.cs`:
  - `GetEquippedPrayerObj()`: eliminado el fallback a P1; devuelve estrictamente `Core.InventoryManager.GetPrayer(equippedPrayer)` o `null`.
  - `IsPrayerOwned(id)`: consulta siempre si el rezo está en `ownedPrayers` o en los rezos descubiertos de la partida (`Core.InventoryManager.GetPrayersOwned()`).
  - `AddOwnedPrayer(id)` y parches Harmony en `InventoryManager.AddPrayer` (tanto la sobrecarga por string como por objeto `Prayer`): cualquier rezo recogido en el mundo se añade automáticamente a la progresión de P2 y se persiste en `p2_inventory_slot{slot}.txt`.
- `Prayer/PrayerSystem.cs`:
  - `PrayerUse_GetEquippedPrayer_P2_Patch`: prefijo sobre `PrayerUse.GetEquippedPrayer()` que devuelve exclusivamente `Player2InventoryManager.GetEquippedPrayerObj()` para P2.
  - `PrayerUse_CanUsePrayer_P2_Patch`: prefijo sobre el getter `PrayerUse.CanUsePrayer` que valida el coste contra el rezo propio de P2 y su fervor actual.
  - `PrayerUse_P2Input_Patch`: valida si P2 tiene un rezo equipado antes de lanzar; si no tiene fervor suficiente llama a `NotEnoughFervour()`, reproduce la animación `AuraTransform` en P2 y ejecuta `Cast()` con su propio rezo.
  - `PrayerUse_OnUpdate_P2_AuraTransform_Patch`: para P2 devuelve `false` en la lectura de Rewired para que la lógica vanilla (atada a P1) no interfiera con P2.
- `HUD/FervourHUD.cs`:
  - Actualizado para calcular los segmentos de coste según `Player2InventoryManager.GetEquippedPrayerObj()`.


### Ronda 85 - Inmovilización de P2 durante rezos y sincronización de reclinatorio (Prie-Dieu / Altar) en cooperativo

1. **Suspensión / inmovilización de P2 durante el casteo de rezos:**
   - *Causas identificadas:*
     - En `Prayer/PrayerSystem.cs`, `PrayerUse_P2Input_Patch` contenía una escucha en `Player2Input.PrayerActivateUp` que llamaba inmediatamente a `StopCast()`. Al pulsar y soltar el botón de rezo, la habilidad se cancelaba al instante en vez de reproducir el efecto y mantener el estado durante su duración natural (`timeToEnd`).
     - Al entrar en la animación `AuraTransform`, la clase de máquina de estados vanilla `AuraTransformBehaviour.OnStateEnter` ejecutaba `Core.Input.SetBlocker("PLAYER_LOGIC", true)` sobre P1 y no marcaba a P2 en `PlayerLogicBlocker`, lo que dejaba a P2 libre de moverse y patinar por el suelo mientras congelaba a P1.
     - `PlatformCharacterInput_Update_Patch` en `Movement/Movement.cs` no evaluaba el estado de rezo (`PrayerCast.IsUsingAbility`), permitiendo que las velocidades de movimiento y teclas de acción continuaran aplicándose a P2.
   - *Soluciones implementadas:*
     - Se eliminó el `StopCast()` prematuro al soltar el botón de rezo en `PrayerUse_P2Input_Patch`.
     - Se crearon los parches `AuraTransformBehaviour_OnStateEnter_Patch` y `AuraTransformBehaviour_OnStateExit_Patch` para P2: establecen `PlayerLogicBlocker.SetBlocked(owner, true/false)` específicamente para P2 y evitan que el blocker global de P1 sea modificado o que se detenga el audio de P1 al terminar.
     - En `Movement/Movement.cs`, `isPrayerCasting` (`penitent.PrayerCast.IsUsingAbility`) se incluye en la condición de `blocked` y pone `controller.PlatformCharacterPhysics.HSpeed = 0f`, impidiendo cualquier movimiento horizontal, salto, ataque o dash mientras P2 está lanzando el rezo.

2. **Sincronización de reclinatorio (Prie-Dieu y Altar de Confesor) para P1 y P2:**
   - *Mecanismo vanilla de Prie-Dieu:* El juego no usa la animación del propio Penitent para el reclinatorio; desactiva el `SpriteRenderer` y `DamageArea` del Penitent y activa en su lugar el objeto de escena `interactorAnimator`, el cual reproduce las animaciones dedicadas `ACTIVATION`, `KNEE_START` y `KNEE_END`. Al usarlo P2 o P1, P2 era ignorado, quedándose de pie o pudiendo caminar alrededor mientras P1 quedaba de rodillas.
   - *Solución implementada en `Abilities/PrieDieuCoopSync.cs`:*
     - Cuando cualquiera de los dos jugadores (P1 o P2) interactúa con un Prie-Dieu o Altar:
       - P1 se posiciona en el lugar estándar frente al reclinatorio.
       - P2 es reposicionado automáticamente un poco más a la derecha (`X + 1.2f`) a la misma altura, con orientación hacia la izquierda (`EntityOrientation.Left`) mirando hacia P1.
       - Se desactiva el `SpriteRenderer` y `DamageArea` de P2 y se bloquea su input (`PlayerLogicBlocker.SetBlocked(p2, true)`).
       - Se instancia un clon visual de `interactorAnimator` para P2 situado a la derecha (`X + 1.2f`) con `flipX = true` (mirando a P1) y despojado de colliders o scripts de eventos para no alterar la escena.
       - Cada frame mientras la interacción está activa (`PlayerReposition`), el clon de P2 sincroniza su estado de animación, tiempo normalizado y velocidad con `interactorAnimator`, haciendo que ambos personajes se arrodillen y se levanten en perfecta sincronía.
       - Al terminar la interacción (`InteractionEnd`): se reactivan el `SpriteRenderer` y `DamageArea` de P2, se desbloquea su movimiento y se destruye limpiamente el clon visual.
       - Si P2 estaba muerto o pendiente de reanimación, es revivido y curado por completo junto con P1.
       - En `CoopLocal.cs` (`OnLevelLoaded`), se invoca `PrieDieuCoopSync.Reset()` como salvaguarda para limpiar referencias en cualquier cambio de nivel.


### Ronda 86 - Skin oscura de P2 en Prie-Dieu y paridad integral de todos los rezos del juego

1. **Skin oscura de P2 (Verdadera Apostasía) en Prie-Dieu:**
   - *Problema:* El objeto de animación de Prie-Dieu clonado para P2 (`interactorAnimator`) utilizaba la paleta/material estándar de P1, provocando que P2 se arrodillara con la skin por defecto en lugar de su skin oscura.
   - *Solución:* En `Cosmetics/Cosmetics.cs` se expuso `GetPlayer2PaletteTexture()`. En `Abilities/PrieDieuCoopSync.cs`, al instanciar y actualizar el clon visual `p2KneelVisual`, se genera una instancia de material independiente y se le asigna la textura `_PaletteTex` de Verdadera Apostasía (`PENITENT_ENDING_B`), asegurando que P2 vista su skin oscura idéntica a su modelo en juego.

2. **Auditoría e implementación completa de los 18 rezos del juego (`Prayer/PrayerEffectsExpanded.cs`):**
   - Mediante análisis estático con REA y descompilación de `Assembly-CSharp.dll` y `data.unity3d`, se mapearon y estudiaron todos los rezos del juego (`PR01` a `PR203`), identificando hardcodes a `Core.Logic.Penitent` en los efectos de oraciones restantes:
     1. **`PR03` / `PR101` (Seguidilla / Zarabanda - Escudos orbitales):** `PenitentShieldSystem.FollowPlayer` y `ShieldSystemPrayer.InstantiateShield` seguían a P1. Parcheados con `ShieldOwnerTracker` para seguir y orbitar exclusivamente a P2 cuando es quien lanza el rezo.
     2. **`PR04` (Saeta Dolorosa):** `HeavyAttackPrayerEffect` activaba `IsHeavyAttackPrayerEquipped` sobre P1. Ahora activa y desactiva el estado de ataque pesado y nivel 2 en el `PenitentAttack` de P2.
     3. **`PR10` (Tientos a tus Cabellos Ensangrentados):** `PenitentGuardianEffect` otorgaba invulnerabilidad a P1. Ahora `____owner` se reasigna a P2, aplicando `PrayerProtectionEnabled` e `Invulnerable` en P2 y posicionando la dama protectora sobre P2.
     4. **`PR12` (Zambra al Resplandor Coronado):** `ZambraTearsHarvestEffect` y `TearHarvestEffect` solo afectaban y seguían a P1. Ahora disparan el `ZambraTearsHarvestPrayer` de P2 y el efecto visual de cosecha sigue la posición de P2.
     5. **`PR14` (Campanillero a los Hijos de la Aurora - Querubines):** `AlliedCherubSystem` y `AlliedCherub` medían distancias y orbitaban a P1. Con `CherubOwnerTracker`, los querubines se despliegan, orbitan y atacan a los enemigos cercanos a P2.
     6. **`PR15` (Taranto a la Hermana Mía):** `PenitentAreaAttack` invocaba el shockwave y daño en área en la posición de P1 con las estadísticas de P1. Ahora se ejecuta centrado en P2, con el multiplicador de fuerza de rezo de P2.
     7. **`PR16` (Aubade of the Nameless Guardian):** `PrayerGhostGuardian` y `GuardianPrayerBehaviour` fijaban a P1 como `Master`. Ahora el guardián espectral se instancia junto a P2, lo reconoce como `Master`, calcula distancias y ataques según P2 y se coordina con los ataques y bloqueos de P2.
     8. **`PR201` (Cantó de los Muertos / Miriam):** `PrayerMiriam` y `MiriamPortalPrayerBehaviour` vinculaban el portal a P1 y se autodestruía si P1 no tenía PR201 equipado. Ahora el portal sigue a P2, ataca sincronizado con P2 y no se destruye mientras P2 lo tenga equipado.
     9. **`PR202` (Regreso a Puerto):** `PenitentTeleportToPriedieu` solo reproducía la animación en P1. Ahora reproduce la animación `RegresoAPuerto` tanto en P2 como en P1, asegurando que ambos se teletransporten juntos al Prie-Dieu activo.
     10. **`PR203` (Fuego de San Telmo):** `PR203ElmFireLoopEffect` generaba las trampas de relámpagos alrededor de P1. Ahora genera el anillo eléctrico en la posición y orientación de P2 con la fuerza y duración de P2.


### Ronda 87 - Sincronización de eventos de animación en rezos (Verdiales del Pueblo Olvidado y paridad general)

1. **Problema identificado con el timing de rezos en P2:**
   - En el rezo *Verdiales del Pueblo Olvidado* (y demás rezos ofensivos), al presionar la tecla de rezo con P2, el daño y los proyectiles (orbes/ondas rastreadoras) se disparaban instantáneamente en el frame 0, antes de que el Penitente clavara la espada en el suelo.
   - En P1, en cambio, la animación `AuraTransform` comenzaba primero y los proyectiles y el daño solo se liberaban en el momento exacto en que la espada tocaba el suelo.

2. **Causa raíz descubierta en `PrayerUse.OnUpdate` y `PenitentAttackAnimations`:**
   - Análisis comparativo contra el decompilado de `Gameplay.GameControllers.Penitent.Abilities.PrayerUse` y `Gameplay.GameControllers.Penitent.Animator.PenitentAttackAnimations`:
     - En el juego vanilla, al pulsar la tecla de rezo, `PrayerUse.OnUpdate` **NUNCA** llama a `Cast()`. Únicamente ejecuta `base.EntityOwner.Animator.Play(_animAuraTransform)`.
     - La animación `AuraTransform` posee un **Animation Event** en el fotograma del impacto de la espada contra el suelo que invoca el método `PrayerUseAttack()` en `PenitentAttackAnimations`.
     - Dicho método ejecuta `VerticalAttack.TriggerVerticalAttack(instantiateExplosion: false)` y finalmente llama a `PrayerUse.Cast()`, deduciendo el fervor y disparando `prayer.Use()` / `OnApplyEffect()` exactamente en el fotograma del golpe de espada.
     - En nuestro mod, `PrayerUse_P2Input_Patch` estaba ejecutando `__instance.Cast()` inmediatamente en el bloque `if (Player2Input.PrayerActivateDown)`, forzando la deducción de fervor y la creación de los proyectiles en el instante del botón (frame 0). Cuando el Animation Event `PrayerUseAttack` se disparaba después, `Ability.Cast()` ya veía `Casting == true` y se descartaba.

3. **Solución implementada:**
   - En `Prayer/PrayerSystem.cs`:
     - **`PrayerUse_P2Input_Patch`:** Se eliminó la llamada inmediata a `__instance.Cast()` al presionar `Player2Input.PrayerActivateDown`. Ahora únicamente valida el coste de fervor (`NotEnoughFervour()`), verifica `CanUsePrayer` y arranca la animación con `owner.Animator?.Play("AuraTransform")`.
     - **`PenitentAttackAnimations_PrayerUseAttack_Patch`:** Nuevo parche Prefix sobre `PenitentAttackAnimations.PrayerUseAttack` que asegura que `_penitent` apunte al Penitent dueño (`__instance.GetComponentInParent<Penitent>()`), garantizando que cuando el Animation Event de P2 se ejecute en el fotograma de clavado de espada, `PrayerUseAttack` llame a `Cast()` en el componente `PrayerUse` propio de P2.
   - **Resultado:**
     - En *Verdiales del Pueblo Olvidado*, las ondas/proyectiles viajan y aplican daño en el punto exacto de la animación donde la espada penetra la tierra, 100% idéntico a P1.
     - Este mismo ajuste sincroniza automáticamente todos los rezos dependientes del evento de animación (columnas de fuego, rayos divinos, destellos de luz, etc.) con el fotograma exacto del juego original.


### Ronda 88 - Visuales de Menú Propio de P2, Árbol de Habilidades, Banner "[nombre jugador] menu", Ocultación de HUD y Arquitectura Multi-Jugador (P2-P4)

1. **Corrección de visuales del menú de inventario de P2 (`Stats/InventoryUI.cs`, `Stats/Player2InventoryManager.cs`, `Stats/InventoryGameplayPatches.cs`):**
   - *Causa raíz:* En `Stats/InventoryUI.cs`, `Grid_ShowLayout_P2_ItemSource_Patch` era un `Prefix` sobre `NewInventory_LayoutGrid.ShowLayout` que retornaba `false` tras llamar a `FillGridElements`. Al abortar el resto de `ShowLayout`, nunca se ejecutaban `CacheEquipped()` (que activa el contenedor visual superior `rootLayout` de la pestaña de Rezos, Cuentas de Rosario o Corazones de Espada y puebla `cachedEquipped`), `UpdateItemsBySwordHeartEquipability()`, ni `FocusSlotSecure()`. Además, `NewInventoryWidget_SetTab_P2_Patch` parcheaba `SetTab` en lugar de `SelectTab`, y `Player2InventoryManager.IsOwnedBead` / `IsSwordOwned` ocultaban el resto de objetos globales en cuanto `ownedBeads.Count > 0`.
   - *Solución:*
     - `ShowLayout` ahora ejecuta su flujo completo (`CacheEquipped`, activación de paneles superiores `rootLayout`, `FocusSlotSecure`, `UpdateEquipped`) y en `Grid_ShowLayout_P2_Patch` actualiza tanto los recuadros superiores de objetos equipados de P2 (rezo equipado, cuentas de rosario, corazón de Mea Culpa) como el contador de Lágrimas de Enmienda (`purgeControl.text`) de P2.
     - En `Grid_EquipObject_P2_Patch` y `Grid_UnEquipObject_P2_Patch`, al reemplazar un rezo o corazón de espada se desmarca la celda previa (`UpdateItemById(prevId)`) y se reproducen los sonidos originales de `equipSounds` / `unequipSounds`.
     - En `Stats/InventoryGameplayPatches.cs`, se evita superponer los objetos equipados de P2 sobre las consultas de `InventoryManager` mientras el menú está abierto en vista de P1.

2. **Menú de Habilidades y Santuario Mea Culpa para P2 (`Stats/SkillTreeUI.cs`, `Stats/Player2MenuOpen.cs`):**
   - Se añadió `NewInventoryWidget_ShowSkills_P2View_Patch` y `UnlockSkillsMenu_OnEnter_P2_Patch`: si P2 interactúa con un santuario Mea Culpa (o presiona su botón de menú `I` / `Start`), el menú de mejora de habilidades se abre mostrando el árbol propio de P2 (`Player2SkillManager`), el nivel de Mea Culpa de P2 (`[Baby] <nivel>`), las Lágrimas de Enmienda de P2 y el estado real del botón de compra (`SkillDescription_SetKill_P2_Patch` sobre `NewInventory_Description.SetKill`).

3. **Indicador superior derecho `"[nombre jugador] menu"` con la fuente del juego (`Stats/Player2MenuOpen.cs`):**
   - Se implementó `CoopMenuOwnerBanner`, que instancia un texto en la esquina superior derecha del menú clonando la tipografía original del juego (`"Majestic"` desde `NewInventoryWidget.tabLabel` y `"MajesticExtended_FullLatin"` para el menú de pausa), mostrando `"Baby menu"` (en el color verde de P2) cuando P2 abre su menú y `"Pan-chan menu"` (en el color rojo de P1) cuando P1 abre el suyo.

4. **Ocultación de barras de vida, fervor y lágrimas de P2 al abrir menús (`HUD/Player2HudFadeSync.cs`):**
   - *Causa raíz:* Los clones del HUD de P2 estaban emparentados en la raíz del `Canvas` de `UIController` por encima de `UIController.content`, y `Player2HudFadeSync` solo escuchaba a `FadeWidget` (transiciones de sala), no a la apertura de menús.
   - *Solución:* `Player2HudFadeSync.RefreshMenuOcclusion()` (invocado desde `UIController.Update` y `NewInventoryWidget.Show`/`ShowSkills`) oculta instantáneamente `Player2HealthBar`, `Player2FervourBar`, `Player2PurgePoints` y `Player2ModeIndicator` mientras cualquier menú esté abierto, y los restaura al cerrarse.

5. **Corrección de navegación y bloqueo de personaje durante menús (`Stats/Player2MenuOpen.cs`, `Input/Player2Input.cs`, `Movement/Movement.cs`):**
   - Se eliminaron las llamadas duplicadas a `SelectPreviousCategory`, `SelectNextCategory`, `ShowLore` y `GoBack` en `UIController_MenuNavigation_P2_Patch` que provocaban saltos dobles de pestaña y cierres/reaperturas en el mismo frame (`LastMenuToggleFrame`).
   - En `Movement/Movement.cs`, se añadió `isMenuBlocked` a `blocked` para que P2 quede completamente inmóvil mientras cualquier menú esté abierto.

6. **Base arquitectónica y diagnóstico para P2, P3 y P4 (`MultiPlayer/CoopPlayerRegistry.cs`):**
   - Se creó `CoopPlayerSlot` y `CoopPlayerRegistry` con soporte indexado para hasta 4 jugadores (`0` = P1 `"Pan-chan"`, `1` = P2 `"Baby"`, `2` = P3 `"P3"`, `3` = P4 `"P4"`), centralizando nombres, colores, skins, posiciones de HUD e instancias de `Penitent`.

### Ronda 89: Corrección de Rezos de Buff Temporal (*Següiriya*), Bloqueo en *Verdiales*, Persistencia de Fervor y UI Izquierda

1. **Separación entre Animación de Rezo (`AuraTransform`) y Duración del Efecto (`PrayerCast.IsUsingAbility`)** (`Movement/Movement.cs`, `Prayer/PrayerSystem.cs`):
   - **Causa raíz doble**:
     - En `Movement.cs`, `isPrayerCasting` se evaluaba con `____penitent.PrayerCast.IsUsingAbility`. Para rezos de buff temporal como *Següiriya a tus Ojos como Estrellas* (`EffectTime = 15s–20s`), `IsUsingAbility` permanece `true` durante los 15–20 segundos completos del buff, lo que dejaba a P2 congelado hasta que terminaba el efecto.
     - En cambio, para rezos instantáneos de proyectiles como *Verdiales del Pueblo Olvidado* (`EffectTime = 0s`), durante la primera mitad de la animación `AuraTransform` (antes de clavar la espada) `IsUsingAbility` era `false`, y `canMove = !crouch && !isPrayerCasting` no consultaba `blocked` (`PlayerLogicBlocker`), permitiendo a P2 deslizarse o cancelar la animación si se movía al activar el rezo.
   - **Solución**:
     - En `PrayerUse_P2Input_Patch.Postfix` (`Prayer/PrayerSystem.cs`), al iniciar `AuraTransform` se activa inmediatamente `PlayerLogicBlocker.SetBlocked(owner, true)` y se pone `HSpeed = 0f`.
     - En `Movement.cs`, se reemplazó `PrayerCast.IsUsingAbility` por la verificación del estado activo del `Animator` (`AuraTransform` / `RegresoAPuerto`) junto con `PlayerLogicBlocker.IsBlocked(____penitent)`. Además, `canMove = !crouch && !blocked` y `HSpeed = 0f` se aplican siempre que `isPrayerAnimPlaying || PlayerLogicBlocker.IsBlocked(____penitent)` sea verdadero. Así P2 queda 100% inmóvil únicamente durante los ~1.5s de la animación `AuraTransform` y recupera la movilidad en cuanto sale del estado (`AuraTransformBehaviour.OnStateExit`), idéntico a P1.

2. **Redirección de *Següiriya a tus Ojos como Estrellas* y Todos los `ObjectEffect_Stat` / `ItemGhostTrail` / `ItemTemporalEffect` a P2** (`Prayer/PrayerEffectsExpanded.cs`):
   - Decompilado `Framework.Inventory.ObjectEffect_Stat`, `Tools.Items.ItemGhostTrail`, `Tools.Items.ItemTemporalEffect`, `Framework.Inventory.PrayerUseVfx`, `Framework.Inventory.ProtectionDomeEffect` y `Framework.Inventory.ToxicCloudEffect`: todos tenían hardcodeado `Core.Logic.Penitent`.
   - Añadido `ObjectEffectCasterTracker` y parches `Prefix` en `OnApplyEffect`, `OnRemoveEffect` y `OnUpdate` para que cuando P2 lanza *Següiriya* (o cualquier rezo de estadísticas/estela/invulnerabilidad/cúpula/nube tóxica), el `RawBonus` de `AttackSpeed` (y demás stats) y la estela roja `GhostTrailGenerator` se apliquen y remuevan exclusivamente sobre `CoopLocal.Player2`.

3. **Cartel `"{jugador} menu"` Movido Arriba a la Izquierda** (`Stats/Player2MenuOpen.cs`):
   - `CoopMenuOwnerBanner` ahora ancla tanto el `Text` nativo del inventario como el `TextMeshProUGUI` del overlay en la esquina superior izquierda (`anchorMin = (0, 1)`, `anchorMax = (0, 1)`, `pivot = (0, 1)`, `anchoredPosition = (24, -14)` / `(24, -18)`, alineación izquierda).

4. **Atajo `F6` / `F7` para Fervor al Máximo y Persistencia de Fervor entre Salas** (`Input/Player2Input.cs`, `Stats/Player2StatsSync.cs`):
   - Añadido en `Player2Input.Tick()` el atajo en `KeyCode.F6` y `KeyCode.F7` que rellena `Stats.Fervour.SetToCurrentMax()` para P1 y P2 y guarda el estado de P2.
   - Eliminada la línea `p2.Stats.Fervour.SetToCurrentMax()` al final de `Player2StatsSync.ApplySnapshot` que sobrescribía en cada cambio de sala el valor `__FervourCurrent__` restaurado del disco.

### Ronda 90: Sistema Elemental de Mea Culpa (Veneno I-III, Sangrado I-III), Corazones de Espada y Árbol de Habilidades
- **Sendas Elementales**:
  - Implementadas en [`Combat/SwordElementalUpgrades.cs`](file:///d:/Tirando/Blasphemous%20coop/Blasphemous.CoopLocal/Combat/SwordElementalUpgrades.cs):
    - **Veneno (I a III)**:
      - *Veneno I*: 1 DPS durante 5s.
      - *Veneno II*: 2.5 DPS durante 7.5s.
      - *Veneno III*: 4 DPS durante 10s.
    - **Sangrado (I a III)**:
      - *Sangrado I*: Umbral de golpes (6 en enemigos comunes, 10 en jefes). Al alcanzarlo: 5% de vida máxima inmediata + 3 DPS durante 5s.
      - *Sangrado II*: 7.5% de vida máxima inmediata + 4 DPS durante 5s.
      - *Sangrado III*: 10% de vida máxima inmediata + 5 DPS durante 5s.
  - **Progresión Dual no excluyente**:
    - Un jugador puede desbloquear ambos caminos; el efecto elemental activado en combate depende estrictamente del corazón de Mea Culpa equipado:
      - `HE_VENENO`: *Corazón de Ponzoña Viva* (requiere al menos Ponzoña I).
      - `HE_SANGRADO`: *Corazón de la Hemorragia Sagrada* (requiere al menos Filo Sangrante I).
  - **Árbol de Habilidades**:
    - Nodos escalados un 30% más grandes (`scale = 1.10f`).
    - Navegación bidireccional completa por cruceta (D-Pad), WASD y flechas conectada explícitamente entre el árbol original y las columnas elementales.
    - Atajos `Q` (Veneno) y `E` (Sangrado) en el menú de habilidades para ciclar directamente a las columnas.
    - Coste fijado a 0 lágrimas para pruebas y testing.
  - **Debug F8**:
    - Cicla entre modos de prueba (Ninguno -> Veneno I -> Veneno III -> Sangrado I -> Sangrado III -> Híbridos P1/P2), equipa el corazón correspondiente, avanza por la lista de rezos y rellena el fervor al 100%.

### Ronda 91: HUD Elemental sobre Enemigos - Barras de Duración Descendente y Números de Daño Flotantes
- **Barras Descendentes de Duración (`Combat/SwordElementalUpgrades.cs`)**:
  - Sustituido el texto de cuenta regresiva en segundos por barras visuales que descienden con el tiempo sobre los enemigos:
    - **Barra Verde de Veneno**: Representa el tiempo restante de la ponzoña descendiendo de 100% a 0% a lo largo de su duración (5s / 7.5s / 10s).
    - **Barra Roja de Sangrado**:
      - Durante la acumulación de golpes muestra el progreso hacia el umbral de hemorragia.
      - Tras el estallido, la barra desciende mostrando el tiempo restante de la hemorragia progresiva (5s).
    - Soporte multi-jugador: si un enemigo sufre ambos efectos simultáneamente (ej. P1 aplica veneno y P2 sangrado), ambas barras se apilan limpiamente en vertical sobre la cabeza del enemigo.
  - **Números de Daño Emergentes Flotantes (`FloatingDamageNumber`)**:
    - Al aplicar ticks o estallidos de daño directo elemental:
      - **Texto verde** para ticks de daño de veneno (`-1`, `-2.5`, `-4`).
      - **Texto rojo** para ticks de sangrado (`-3`, `-4`, `-5`) y estallidos de hemorragia porcentual (mayor tamaño para impacto crítico).
    - Los números flotan verticalmente hacia arriba y se desvanecen gradualmente en transparencia (`alpha -> 0`) antes de eliminarse.

### Ronda 92: Muñeco de Pruebas Dummy (`Combat/TestDummyManager.cs`) y Atajo F5
- **Atajo `F5`**:
  - Al pulsar `F5`, invoca o reposiciona un enemigo Dummy de pruebas frente al jugador que tenga el foco (o P1).
  - Si ya existe un Dummy en la sala, lo teletransporta frente al jugador, reinicia su vida al 100% y elimina estados alterados.
- **Características del Dummy Enemy**:
  - **Inmóvil e inerte**: Implementa `DummyEnemyBehaviour` (`EnemyBehaviour`), sin IA de persecución ni ataques (`EnableBehaviourOnLoad = false`).
  - **Física cinemática**: `Rigidbody2D` con `isKinematic = true` para que permanezca en su sitio sin ser empujado ni caer al vacío.
  - **Vida masiva (1,000,000 HP)**: Permite probar cadenas de combos, rezos, daño de veneno y estallidos de sangrado. Si la vida llega a 0, se auto-reinicia de inmediato a 1,000,000 HP.
  - **Texto de HP flotante**: Muestra sobre el Dummy el valor actual de vida (`HP: 998,420 / 1,000,000`) en color dorado brillante.
  - **Visual estilizado procedural**: Sprite de muñeco de entrenamiento generado por código (poste de madera, saco con cuerdas y diana roja/blanca en el pecho).
  - **Totalmente compatible con el sistema elemental**: Admite el acoplamiento de `EnemyElementalStatusController`, mostrando sobre él las barras de veneno/sangrado y los números flotantes verdes/rojos.

### Ronda 93: Sistema de Enfoque y Selección Dinámica de Objetivo para Enemigos y Jefes (`Combat/EnemyTargetingSystem.cs`)
1. **Estudio y Diagnóstico con REA / Decompilación**:
   - Se analizaron las 225 clases de la jerarquía de enemigos y jefes en `Assembly-CSharp.dll`:
     - En el juego vanilla, todos los enemigos (`Enemy`, `EnemyBehaviour`, `VisionCone`, `EnemyAttack`) gestionan el objetivo mediante `Enemy.Target` y `Enemy.DistanceToTarget`.
     - En `Enemy.OnStart()`, `Enemy.OnUpdate()` y `EnemyBehaviour.GetTarget()`, el juego asigna de forma rígida: `base.Target = Core.Logic.Penitent.gameObject;` (P1).
     - Las acciones de IA (`LookAtTarget`, `Chase(Target.transform)`, `DistanceToTarget <= attackRange`) y los conos de visión (`VisionCone.CanSeeTarget(Target.transform, "Penitent")`) se evalúan contra `Target`.
     - Por esa razón, aunque P2 estuviera al lado del enemigo o golpeándolo, el enemigo ignoraba a P2 y solo perseguía, miraba y atacaba en dirección a P1.
2. **Solución Implementada**:
   - **Módulo `Combat/EnemyTargetingSystem.cs`**:
     - **Selección por Proximidad**: Evalúa en cada frame a todos los jugadores activos y vivos (`CoopPlayerRegistry` con soporte para P1, P2, P3 y P4) y asigna como `Enemy.Target` al jugador vivo más cercano.
     - **Sistema de Represalia / Aggro**: Cuando un jugador (ej. P2) golpea a un enemigo (`EnemyDamageArea.TakeDamage`), el enemigo registra represalia inmediata hacia ese jugador durante 5 segundos, girándose y enfocándose en él.
     - **Parches de Harmony**:
       - `Enemy_OnUpdate_Targeting_Patch` (Postfix sobre `Enemy.OnUpdate`): Mantiene sincronizados `Target` y `DistanceToTarget` en cada frame con el jugador objetivo.
       - `EnemyBehaviour_GetTarget_MultiPlayer_Patch` (Prefix sobre `EnemyBehaviour.GetTarget`): Retorna el objetivo real a la máquina de estados / BehaviourTree.
       - `VisionCone_CanSeeTarget_MultiPlayer_Patch` (Prefix sobre `VisionCone.CanSeeTarget`): Lanza los rayos de visión hacia el jugador objetivo actual.
       - `EnemyDamageArea_TakeDamage_Aggro_Patch` (Postfix sobre `EnemyDamageArea.TakeDamage`): Activa el cambio de aggro al recibir daño.
       - Parches de orientación para jefes (`Amanecidas`, `Crisanta`, `Esdras`, `PontiffOldman`) en `LookAtPenitent`.

### Ronda 94: Sistema de Configuración Previa de Partida Cooperativa (`MultiPlayer/CoopGameSetupUI.cs`, `CoopGameConfig.cs`, `CoopGameSetupPatches.cs`)
1. **Requerimientos del Usuario**:
   - Al crear una nueva partida en los slots de guardado, debe abrirse una pantalla de configuración previa que permita:
     - Elegir si jugar cooperativo (`¿Jugar en Cooperativo? Sí / No`).
     - Elegir el número de jugadores (1 a 4).
     - Asignar el dispositivo de control a cada jugador:
       - **Regla estricta**: El teclado solo puede ser asignado a un único jugador.
       - **Detección por botón**: Al presionar cualquier botón de un mando físico (Joystick 1..4) o tecla del teclado, el sistema auto-detecta y asigna de inmediato ese mando/teclado a la ranura del jugador correspondiente.
     - Elegir el nombre de cada jugador (con ciclo de nombres sugeridos).
     - Elegir el color del texto de cada jugador (paleta vibrante y contrastada).
2. **Arquitectura e Implementación**:
   - **`MultiPlayer/CoopGameConfig.cs`**:
     - Estructura persistente (`coop_game_settings.cfg`) para guardar el modo coop, número de jugadores, nombres, paletas y dispositivos (`CoopDeviceType`: `Unassigned`, `Keyboard`, `Gamepad1..4`).
     - Lógica `AssignDevice(playerIndex, device)`: garantiza exclusividad total del teclado (si un jugador toma el teclado, se desasigna automáticamente de cualquier otro).
     - Vinculado con `CoopPlayerRegistry` para que los nombres y colores se reflejen de inmediato en las etiquetas flotantes sobre las cabezas de los penitentes, HUD y en los banners del menú (`"[Nombre] Menú"`).
   - **`MultiPlayer/CoopGameSetupUI.cs`**:
     - Canvas uGUI dinámico overlay con estilo gótico del juego (fondo semi-transparente oscuro, marco dorado, tipografía original del juego).
     - Escaneo continuo de botones de mandos (`Joystick1Button0..15` a `Joystick4Button0..15`) y teclado para asignación interactiva al pulsar.
     - Navegación fluida por menú (Arriba/Abajo), cambio de valores (Izquierda/Derecha o A/Enter) y atajos de confirmación y cancelación.
   - **`MultiPlayer/CoopGameSetupPatches.cs`**:
     - Harmony Prefix en `SelectSaveSlots.OnAcceptSlots`: intercepta la pulsación sobre una ranura vacía (`slot.IsEmpty`), detiene la transición y despliega la UI de configuración cooperativa. Al pulsar "¡Comenzar Partida!", reanuda la creación de la partida con toda la configuración aplicada.
     - Atajo `C` / `F4` en `SelectSaveSlots.Update` para abrir la pantalla de configuración en cualquier momento desde la pantalla de partidas guardadas.
   - **Integración con Gameplay (`Input/Player2Input.cs` & `CoopLocal.cs`)**:
     - `Player2Pad.Pad` y `Player2Pad.P1Pad` resuelven dinámicamente los gamepads físicos basándose en `CoopGameConfig.Slots`.
     - Exclusión y reasignación de mandos en Rewired adaptadas para que 2 gamepads puedan jugar simultáneamente sin interferencia mutua.
     - `CoopLocal.SpawnPlayer2` respeta `CoopGameConfig.IsCoopEnabled` y `PlayerCount < 2`, evitando instanciar a P2 ni su HUD si el cooperativo fue desactivado.

### Ronda 95: Desbloqueo Elemental Independiente, Activación por Compra en Árbol, Navegación Fluida y Debug de Posición/Escala (`Combat/SwordElementalUpgrades.cs`, `MultiPlayer/CoopGameConfig.cs`)
1. **Desbloqueo Independiente sin Bloqueos Mutuos**:
   - Eliminada cualquier restricción o reseteo que bloquease una rama al comprar o equipar la otra. Ambas ramas (Veneno I..III y Sangrado I..III) pueden adquirirse y mejorarse en su totalidad al mismo tiempo en el árbol de habilidades.
   - Corregido el campo `parentSkill` de `SkillVeneno1` y `SkillSangrado1`: se cambió de `"NO DEPENDENCY"` a `string.Empty` (`""`), permitiendo que el sistema vanilla y de P2 detecten el Nivel I como inmediatamente desbloqueable con Mea Culpa >= 1.
2. **Regla Estricta de Activación de Habilidades por Compra en el Árbol**:
   - Aunque el jugador tenga equipado el Corazón de Mea Culpa (`HE_VENENO` o `HE_SANGRADO`), **ningún efecto elemental se activa hasta que se adquiera al menos el Nivel I en el árbol de habilidades**.
   - En `EnemyDamageArea_TakeDamage_ElementalStatus_Patch`, se valida que `poisonTier > 0` y `bleedTier > 0` antes de invocar `ctrl.ApplyPoison` o `ctrl.AddBleedHit`.
3. **Navegación Natural en el Menú de Habilidades**:
   - Implementado un despachador de navegación interactiva bidireccional en `SwordElementalUpgrades.HandleSkillTreeShortcuts` compatible con teclado (`WASD`) y mandos (`Left Stick` y `D-Pad`):
     - **Desde Veneno**: Pulsar Derecha navega limpiamente a Sangrado en el mismo nivel; Arriba/Abajo navega entre los tiers (I, II, III).
     - **Desde Sangrado**: Pulsar Izquierda navega a Veneno en el mismo nivel; Derecha navega al nodo correspondiente del árbol vanilla; Arriba/Abajo navega entre los tiers.
     - **Desde el Árbol Vanilla**: Pulsar Izquierda cerca del margen izquierdo entra fluidamente a la columna de Sangrado en el tier correspondiente.
     - Preservadas las conexiones originales del árbol vanilla (`FindSelectableOnRight`, `Up`, `Down`) para evitar atrapar el cursor en los bordes.
4. **Sistema en Vivo de Ajuste de Posición, Escala y Tamaño de Texto**:
   - Empaquetados todos los nodos elementales y sus etiquetas en un contenedor raíz común (`ElementalTreeContainer`).
   - Controles interactivos dentro del menú de habilidades:
     - **Flechas (`Arriba`, `Abajo`, `Izquierda`, `Derecha`)**: Mueven la posición completa del árbol elemental en pantalla (mantener Shift para desplazamiento rápido).
     - **Tecla `.` (Punto) o `+`**: Aumenta el tamaño / escala del árbol en incrementos de +0.05.
     - **Tecla `-` (Menos)**: Disminuye el tamaño / escala del árbol en incrementos de -0.05.
     - **Tecla `{` (Llave que abre o `[` )**: Aumenta el tamaño del texto de descripción e instrucciones de la habilidad en incrementos de +10% para máxima legibilidad.
     - **Tecla `}` (Llave que cierra o `]` )**: Disminuye el tamaño del texto de descripción en incrementos de -10%.
   - **Persistencia**: La posición (`TreeOffsetX`, `TreeOffsetY`), la escala (`TreeScale`) y la escala de texto (`SkillTextScale`) se guardan automáticamente en `coop_game_settings.cfg`.
   - **Banner Visual en Pantalla**: Muestra en tiempo real en la parte inferior del menú de habilidades:
     `[ÁRBOL ELEMENTAL DEBUG] Posición: X=... Y=... | Escala Árbol: ... | Escala Texto: ...%` con instrucciones claras.

### Ronda 96: Corrección de Enfoque y Detección de P2 en Enemigos con Poste / Tronco (`Combat/EnemyTargetingSystem.cs`)
1. **Diagnóstico y Causa Raíz**:
   - El usuario reportó que ciertos enemigos como *"los que llevan una especie de poste"* aún no fijaban a P2 y solo iban a por P1.
   - Mediante análisis con Mono.Cecil sobre las 77 clases de enemigos en `Assembly-CSharp.dll`, se identificaron los enemigos exactos:
     - **`BellCarrier`** (Porteador con poste/tronco y campana colgada que embiste con el poste).
     - **`Processioner`** y **`ShooterProcessioner`** (Porteador con madero/poste sobre los hombros y querubín encadenado).
     - **`WheelCarrier`** (Porteador con poste y rueda de Santa Catalina).
     - **`Flagellant`** y **`NewFlagellant`** (Porteador con poste/cruz y alambre de espino).
     - **`WalkingTomb`** (Porteador con monolito/poste de tumba).
   - **Hallazgo Crítico 1 (Sobrescritura de OnUpdate)**: En Ronda 93 se había aplicado un parche en `[HarmonyPatch(typeof(Enemy), "OnUpdate")]`. Sin embargo, **48 de las 77 clases de enemigos sobrescriben `OnUpdate()` virtualmente llamando a `Entity.OnUpdate()` directamente**, ¡omitiendo por completo a `Enemy.OnUpdate()`! Por lo tanto, `Enemy_OnUpdate_Targeting_Patch` nunca se ejecutaba para ninguno de estos 48 enemigos. En su propio `OnUpdate()`, cada uno comprobaba `if (Target == null) Target = Core.Logic.Penitent.gameObject;` y calculaba `DistanceToTarget` contra P1 exclusivamente.
   - **Hallazgo Crítico 2 (Composición de ChainedAngel)**: `Processioner` tiene como hijo/compañero a `ChainedAngel` (el querubín que dispara). En `Processioner.OnPlayerSpawn`, se asignaba `ChainedAngel.Target = base.Target` (P1). Al no actualizarse el `Target` de `ChainedAngel`, los proyectiles seguían disparándose únicamente a P1.
   - **Hallazgo Crítico 3 (Sobrecarga de LookAtTarget en BellCarrier)**: `EnemyBehaviour.LookAtTarget` recibe `Vector3 targetPos`. Sin embargo, `BellCarrierBehaviour.LookAtTarget` no es un override de `EnemyBehaviour`, sino una **sobrecarga independiente que recibe `Transform targetPosition`**. La llamada polimórfica vanilla en `EnemyBehaviour` caía en el método base vacío y nunca ejecutaba el giro (`TurnAround`) en `BellCarrier`.
   - **Hallazgo Crítico 4 (Suscripciones a OnPlayerDead)**: `WalkingTomb`, `AshCharger`, `Runner` y `NewFlagellant` se suscribían directamente a `Core.Logic.Penitent.OnDead`, desactivando su IA y deteniéndose por completo en cuanto P1 caía, ignorando que P2 seguía vivo.
2. **Solución Implementada**:
   - **Parche Maestro en `Entity.Update`**: Al ser `Entity.Update()` un método no virtual de MonoBehaviour que Unity llama en cada fotograma para todas las entidades (`this.OnUpdate()`), engancharlo garantiza cobertura absoluta del 100% de las 77 clases de enemigos, actualizando `enemy.Target` y `enemy.DistanceToTarget` hacia el jugador vivo óptimo (P1 o P2).
   - **Sincronización de `ChainedAngel`**: Al actualizar el objetivo de `Processioner`, se actualiza de forma simultánea `ChainedAngel.Target` y su `DistanceToTarget`.
   - **Orientación y Giro Dinámico para `BellCarrier`**:
     - Si el jugador objetivo se encuentra detrás del `BellCarrier` mientras está activo/alerta/persiguiendo y no se encuentra ya girando, se invoca `bellCarrier.BellCarrierBehaviour.LookAtTarget(target.transform)`.
     - En `RecordDamageAggro`, si P2 golpea a un `BellCarrier`, este fija aggro de inmediato, reinicia su temporizador de persecución (`ResetTimeChasing()`), se gira hacia P2 y activa el modo persecución (`Chasing()`).
     - En `BellCarrierBehaviour.CheckCollision`, se añadió una comprobación de seguridad para que `TargetInLine` se active si el objetivo se encuentra en la línea frontal (hasta 15 unidades de alcance).
   - **Navegación de `NewFlagellant` en Plataformas**: Parche en `NewFlagellantBehaviour.CanGoDownToReachPlayer` para evaluar la posición del objetivo real en lugar de asumir `Core.Logic.Penitent`.
   - **Protección de Muerte de P1**: Parches en `NewFlagellantBehaviour.OnPlayerDead`, `WalkingTombBehaviour.TargetOnDead`, `AshChargerBehaviour.TargetOnDead` y `RunnerBehaviour.OnPlayerDead` para no desactivarse si todavía queda algún jugador vivo en la sala (`EnemyTargetingSystem.GetActiveLivingPlayers().Count > 0`).

### Ronda 97: Selección de Skin para P2, P3 y P4 en Menú de Partida Nueva y Persistencia (`MultiPlayer/CoopGameSetupUI.cs`, `CoopGameConfig.cs`, `Cosmetics/Cosmetics.cs`)
1. **Requerimiento del Usuario**:
   - Permitir elegir la skin / aspecto cosmético para los jugadores P2, P3 y P4 desde el menú interactivo previo de configuración de partida cooperativa.
2. **Arquitectura e Integración**:
   - **Catálogo Oficial de Aspectos (`CoopGameConfig.AvailableSkins`)**:
     - 18 aspectos oficiales disponibles: `Original`, `Verdadera Apostasía`, `Ruta Inmisericorde`, `Hijo del Milagro`, `El Osario`, `Aleación del Pecado (Mecenas)`, `Carga Dorada (Deluxe)`, `Limosnero de la Iglesia`, `Corazón Sangrante`, `Fe Inquebrantable`, `Verdadera Culpa`, `Canto de Sirena (Boss Rush)`, `Fervor de Acero (Rango S)`, `8-Bits (Demake)`, `Sierpes`, `Isidora`, `Game Boy (Demake 2)` y `Konami / Anónimo`.
   - **Menú de Configuración Interactiva (`CoopGameSetupUI.cs`)**:
     - Para cada jugador cooperativo invitado (P2, P3, P4) se añade una fila `Jugador X - Aspecto / Skin`.
     - Navegación con Izquierda/Derecha o A/D para alternar entre las 18 skins disponibles en tiempo real con indicador ornamental `◈ [Nombre Skin]`.
     - Dimensiones y distribución del panel ajustadas dinámicamente con espaciado responsive para acomodar hasta 19 filas sin desbordamiento.
   - **Persistencia en Disco (`coop_game_settings.cfg`)**:
     - Nuevas claves `P{i}_Skin` guardadas y leídas automáticamente en `CoopGameConfig.Save()` y `Load()`.
     - Sincronización inmediata con `CoopPlayerRegistry.SkinPaletteId`.
   - **Aplicación Dinámica per-Instancia (`Cosmetics/Cosmetics.cs`)**:
     - `ColorPaletteSwapper.SetMaterial`: Parche generalizado para que cualquier jugador invitado (`CoopPlayerRegistry.IsGuestPlayer`) aplique su textura `_PaletteTex` específica en su propio `SpriteRenderer` y `extraMaterial`.
     - Compatible con `PrieDieuCoopSync` para que el sprite de penitente rezando en el reclinatorio de P2 refleje también su skin elegida.

### Ronda 98: Reorganización Horizontal del Menú de Configuración y Cambio de Estado con Botón de Ataque (`MultiPlayer/CoopGameSetupUI.cs`)
1. **Requerimiento del Usuario**:
   - Organizar el menú de modo que para cada jugador y sus configuraciones se navegue moviéndose hacia la izquierda o derecha (`[◄ / ►]`).
   - Para cambiar el estado / valor de la configuración seleccionada, presionar el **Botón de Atacar**.
2. **Diseño de Interfaz por Columnas / Tarjetas Horizontales**:
   - Reemplazada la antigua lista vertical continua por un sistema de 3 zonas con disposición por columnas:
     - **Zona 0 (Barra Superior)**: `[ Modo de Juego: Cooperativo (Sí/No) ]` y `[ Cantidad: 2..4 Jugadores ]`.
     - **Zona 1 (Tarjetas de Jugadores)**: Columnas lado a lado (`PlayerCard_0` a `PlayerCard_3`) cuyo ancho y posición se distribuyen de forma uniforme según la cantidad activa (2, 3 o 4 jugadores). Cada tarjeta muestra:
       - Cabecera con rol y color (`JUGADOR 1 (Host)`, `JUGADOR 2`, etc.).
       - Fila 0: `Control / Dispositivo` (Teclado / Mando 1..4).
       - Fila 1: `Nombre` (Ciclo de nombres sugeridos).
       - Fila 2: `Color de Texto` (Muestra de color y nombre).
       - Fila 3: `Aspecto / Skin` (Catálogo de 18 skins).
     - **Zona 2 (Barra Inferior)**: Botones de `¡Comenzar Partida!` y `Volver / Cancelar`.
3. **Flujo de Navegación Bidireccional (`HandleNavigationInput`)**:
   - **`Izquierda` / `Derecha` (`A` / `D`, Stick / Cruceta)**: Se desplaza horizontalmente entre los jugadores (Columnas P1 ◄──► P2 ◄──► P3 ◄──► P4). La tarjeta activa se resalta con borde dorado brillante y fondo iluminado.
   - **`Arriba` / `Abajo` (`W` / `S`, Stick / Cruceta)**: Se desplaza verticalmente entre las configuraciones de la tarjeta activa (Control ◄──► Nombre ◄──► Color ◄──► Skin), subiendo a la barra superior o bajando a los botones de acción.
4. **Modificación con el Botón de Atacar (`IsAttackButtonPressed` & `HandleAttackButtonPress`)**:
   - Al situarse sobre cualquier ajuste y presionar el botón de ataque (`Botón X / Cuadrado` en mando, tecla `K` o `J` en teclado, o Enter/A), se conmuta o avanza al siguiente valor/estado de la configuración seleccionada.
   - Auto-asignación interactiva preservada: pulsar cualquier botón en un mando físico o tecla en teclado mientras se está en la fila "Control" auto-detecta y asigna el dispositivo a ese jugador.

### Ronda 99: Escritura de Nombres con Teclado, Vista Previa de Skins en Menú, Corrección de Distancia de Tiers y Desacoplamiento Visual de Corazones en Árbol Elemental
1. **Edición Interactiva de Nombre con Teclado (`MultiPlayer/CoopGameSetupUI.cs`)**:
   - Eliminado el ciclo de nombres default predefinidos.
   - Al seleccionar la fila "Nombre" de cualquier jugador y presionar el **Botón de Atacar** (o Enter/Espacio), se activa el modo de escritura interactiva `isEditingName`.
   - Se intercepta `Input.inputString` en tiempo real:
     - Permite escribir caracteres alfanuméricos, espacios, guiones y símbolos habituales (hasta 14 caracteres de longitud).
     - Admite borrar caracteres hacia atrás mediante la tecla `Backspace` (`\b`).
     - Muestra un cursor parpadeante dinámico (`_`) en color dorado brillante al final del texto mientras se escribe.
     - Presionar `Enter`, `KeypadEnter` o el botón de ataque confirma y guarda el nombre personalizado en `coop_game_settings.cfg` y `CoopPlayerRegistry`.
     - Presionar `Escape` cancela la edición y restaura el nombre anterior.
2. **Sprite de Vista Previa de Skin en Menú (`MultiPlayer/CoopGameSetupUI.cs`)**:
   - Añadido un componente `Image` dedicado (`SkinPreview`) a la derecha del texto en la Fila 3 ("Aspecto / Skin") de cada tarjeta de jugador.
   - En cada refresco o ciclado de skin, se obtiene el sprite oficial en miniatura mediante `Core.ColorPaletteManager.GetPaletteSpritePreview(skinId)` y se muestra con `preserveAspect = true`.
3. **Reducción de Distancia entre Nodos Elementales a un 60% (`Combat/SwordElementalUpgrades.cs`)**:
   - En `EnsureSkillTreeNodesInjected`, se recalcularon las alturas verticales de los 3 tiers elementales (`Tier I, II, III`) tanto para Veneno como para Sangrado centrados en el Tier 2 (`elemTier2Y = yTier2`).
   - Los deltas hacia el Tier 1 y Tier 3 se escalan por `0.60f` (`elemTier1Y = yTier2 + (yTier1 - yTier2) * 0.60f`, `elemTier3Y = yTier2 + (yTier3 - yTier2) * 0.60f`).
   - Las etiquetas superiores de columna (`VENENO [Q]` y `SANGRADO [E]`) se reubicaron proporcionalmente a `elemTier3Y + 28f`.
4. **Desacoplamiento Visual Estricto: Corazones vs Desbloqueo en Árbol de Habilidades (`Stats/SkillTreeUI.cs`, `Combat/SwordElementalUpgrades.cs`, `MultiPlayer/CoopGameSetupPatches.cs`)**:
   - **Corrección de Bug**: Equipar un corazón de Mea Culpa (`HE_VENENO` o `HE_SANGRADO`) en el inventario ya no altera ni desbloquea visualmente los nodos del árbol de habilidades.
   - En `SkillItem_UpdateStatus_P2_Patch`, se unificó la lógica visual de los nodos elementales tanto para P1 como para P2:
     - Cada nodo solo se muestra desbloqueado (`backgorundUnLocked` activo y sprite `smallImageBuyed`) si realmente ha sido adquirido y comprado en `SkillManager` (P1) o `Player2SkillManager` (P2).
     - Si no ha sido adquirido, se evalúa estrictamente si es desbloqueable: Tier 1 requiere Mea Culpa $\ge 1$; Tier 2 requiere Mea Culpa $\ge 2$ y Tier 1 comprado; Tier 3 requiere Mea Culpa $\ge 3$ y Tier 2 comprado.
     - Si el tier previo no ha sido comprado, el nodo se muestra bloqueado (`backgorundLocked` activo con número de tier e icono oculto).
   - Al crear una nueva partida en un slot vacío (`SelectSaveSlots_OnAcceptSlots_CoopSetup_Patch`), se reinicia limpiamente el estado elemental de ambos jugadores mediante `ResetElementalPath` para evitar arrastrar residuos de depuración anteriores.





