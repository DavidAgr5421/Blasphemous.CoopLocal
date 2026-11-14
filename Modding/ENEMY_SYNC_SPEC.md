# Spec: sincronizacion de enemigos host-autoritativa (online coop)

Estado: especificacion final de Fase 1 (hechos vanilla verificados por decompilado de Assembly-CSharp.dll).
Decisiones del usuario: jefes tambien neutralizados en el cliente; las muertes del host y los flags de jefe se
reflejan en el guardado del cliente; clones/invocaciones del host como puppets `Dynamic` en Fase 2.

## Hechos vanilla que condicionan el diseno
1. `Entity.Update/FixedUpdate` son privados y solo llaman a hooks virtuales vacios (`OnUpdate/OnFixedUpdated`).
   `Enemy` no declara Update. La logica por enemigo esta en `OnUpdate` de la subclase y en `EnemyBehaviour`
   (+ `BehaviourTreeOwner` de NodeCanvas, + `StateMachine<T>` propio en jefes).
   => NO sirve parchear `Entity.Update`. Neutralizar = `enabled=false` en el `Enemy` concreto (detiene su
   OnUpdate, que si no reactiva la IA via `EnablePhysics()`), en `EnemyBehaviour`, `BehaviourTreeOwner`,
   `NPCInputs`, `PlatformCharacterController`, y en los componentes de ataque. Animator y SpriteRenderer se dejan.
2. Los enemigos NO estan precolocados: nacen en `EnemySpawnPoint.CreateEnemy()` (llamado por
   `EnemySpawner.SpawnEnemiesOnLoad()` en `LevelManager.InitializeLevel`, ANTES de `OnLevelLoaded`).
3. IDs estables: `Entity.Id`, `SpawningId` y nombres de instancia usan `GetHashCode()` => NO estables.
   Estable: `EnemySpawnPoint.transform.position` (vanilla ya lo usa: `EnemySpawner.consumedSpawns` es
   `List<Vector3>`). NetId = hash32(sceneName + posicion del spawn point redondeada a 0.01 + ordinal de desempate).
4. Muerte de enemigos normales: NO se guarda en disco. Es `EnemySpawner.consumedSpawns` en memoria
   (`AddConsumedSpawner(spawnPoint)`, `IsSpawnerConsumed`), reseteado en descanso Prie Dieu / carga / respawn.
   Solo `PersistentEnemy` (pocos, ej. Piety) usa `PersistentObject` con `UniqueId` estable.
5. `Entity.KillInstanteneously` dispara eventos estaticos con efectos (purga al Penitent local via
   `Entity.Death`, logros, FSM Playmaker, `EnemySpawnPoint.OnEntityDie`). El cliente NO debe matar enemigos localmente.
6. Los clips del Animator del puppet disparan animation events (`AnimationEvent_*Attack`, proyectiles, shake):
   el puppet intentara danar al Penitent local y crear proyectiles. Los SMB de muerte hacen `Destroy(gameObject)`.
7. Danio a jugadores: toda ruta termina en `Penitent.Damage(Hit)` / `PenitentDamageArea.TakeDamage`.
   El Penitent real del cliente es solo un espejo (sprite oculto, input bloqueado) => se bloquea el danio ahi.
8. `Core.Input.SetBlocker` es global (un bool para todos): NO usarlo para bloquear a un jugador concreto.
9. `Enemy.Target` es siempre `Core.Logic.Penitent` salvo el parche de EnemyTargetingSystem (solo host).
10. Jefes: inicio de combate, puertas de arena, cutscenes y flags post-muerte son datos de escena
    (UnityEvents/Playmaker), NO estan en codigo. `BossFightManager.StartBossFight` solo se invoca desde la escena.

## Arquitectura Fase 1
- Cliente = renderer: los enemigos se crean localmente por el propio juego (visuales y prefabs correctos),
  se neutralizan justo tras `CreateEnemy` (Postfix en `EnemySpawnPoint.CreateEnemy`, solo `IsOnlineClient`) y se les
  pega un `CoopEnemyPuppet { uint NetId; ... }`. El host registra sus enemigos igual (sin neutralizar).
- Cliente NO ejecuta: `CoopEnemyScaling.ProcessSceneEnemies` (ni clones), `EnemyTargetingSystem`, ni efectos
  elementales (ya host-only).
- Seguridad: Prefix `return false` con `IsOnlineClient` en `PenitentDamageArea.TakeDamage` y `Penitent.Damage`
  (el jugador local del cliente no recibe danio; su vida viene espejada del host).
- Puppet NO llama a `Entity.Damage/Kill`. La muerte llega por evento y el puppet se oculta/desactiva (no reproducir
  SMB de muerte salvo que se pruebe que es seguro).

## Protocolo v2 (CURRENT_PROTOCOL_VERSION = 2; host rechaza versiones distintas, ya existe el chequeo)
- `EnemyState = 32` (unreliable, canal STATE, 20 Hz): u8 type, u16 seq, u16 sceneHash16, u8 count,
  por enemigo: u32 netId, i16 x*100... (usar float32 x,y en Fase 1 por simplicidad), u8 flags (bit0 alive, bit1 facingLeft,
  bit2 visible), i32 animShortHash, u8 animNormTime(0-255), u16 hp (clamp). Tope ~60 enemigos por paquete
  (<= ~1200 B); dividir en varios paquetes. Solo enemigos a < 40 unidades de algun jugador activo.
  Refresco completo cada 1 s (los demas: solo si cambio pos>0.02 / anim / hp).
- `EnemyEvent = 33` (reliable, canal RELIABLE): u8 kind (1=Death), u32 netId, u16 sceneHash16, i8 killerSlot.
- `SceneReady = 35` (cliente->host, reliable): u16 sceneHash16, u16 count, u32[] netIds.
- `EnemyFullSync = 34` (host->cliente, reliable, respuesta a SceneReady): lista completa (netId, alive, hp, pos).
- El cliente descarta paquetes con sceneHash distinto del actual.
- Host loguea `[EnemySync]` mismatch de netIds entre su lista y la del cliente (dato clave para validar el hash).

## Aplicacion en el cliente (por frame, sobre CoopEnemyPuppet)
- Posicion: lerp hacia el objetivo (snap si > 5u). Orientacion: `Status.Orientation` (EntityOrientation.Left=1) Y
  `SpriteRenderer.flipX` coherentes (llamar `SetOrientation`).
- Animacion: si cambia animShortHash -> `Animator.Play(hash, 0, normTime)` (con guarda por si el hash no existe).
- HP: `Stats.Life.Current` solo para barras de vida; nunca disparar muerte.
- Muerte (EnemyEvent): desactivar GameObject del puppet, llamar `EnemySpawner.AddConsumedSpawner(spawnPoint)` en el
  cliente (espeja el guardado en memoria) y NO emitir `Entity.Death`.
- Sin entrada en el snapshot tras EnemyFullSync => desactivar ese enemigo local.

## Persistencia / flags (espejo en el guardado del cliente)
- Enemigos normales: `AddConsumedSpawner` al recibir la muerte (arriba). Al cargar/entrar a sala, tras SceneReady
  el host manda EnemyFullSync con los muertos => el cliente marca consumidos y desactiva.
- Flags / eventos de jefe: el host parchea `Core.Events.SetFlag` y `Core.Events.LaunchEvent` (VERIFICAR firmas con
  decompiler-modder) y envia un paquete reliable `FlagChange` (nombre + valor); el cliente lo reaplica.
  => Esto es Fase 1b (tras validar enemigos normales).

## Jefes (Fase 1b, el usuario pidio neutralizarlos)
- Mismo mecanismo de puppet que enemigos normales, pero ademas:
  * Eventos reliables del host: `BossFightStart/End`, `FlagChange`, `GateState` (puertas/arena abiertas o cerradas).
  * El cliente aplica el estado de arena/puerta desde el host en vez de correr la cutscene; barra de vida del jefe
    via `UIController.ShowBossHealth(entity)` con HP del snapshot.
  * Bloqueos de cutscene: el cliente nunca debe quedarse con `Core.Input` blockers; red de seguridad = timeout
    que llama a `Core.Input.ResetManager()` si el cliente lleva > N s bloqueado sin evento del host.
  * Triggers sin filtro de capa (ej. `BossScripting.OnTriggerEnter2D`) pueden dispararse con colliders locales:
    guardarlos con IsOnlineClient.
- Requiere observacion en juego: registrar en el host (`[EnemySync][Boss]`) todos los LaunchEvent/SetFlag/Gate durante
  un combate de jefe para saber que replicar. NO se puede deducir del codigo (datos de escena).

## Fuera de Fase 1
Clones de coop y invocaciones (Fase 2: `Dynamic` con prefab id via `Entity.OnCreated`), proyectiles reales,
drops/pickups, puertas fuera de jefes, DoT elemental, hitstop/shake.

## Pasos de implementacion (un coder por paso, en orden, mismos archivos nuevos en MultiPlayer/EnemySync*.cs)
A. Protocolo v2 + registro de ids + hooks (EnemySpawnPoint.CreateEnemy Postfix) + neutralizacion cliente + guardas.
B. Broadcaster host (EnemyState/Event/FullSync) + SceneReady + aplicador del cliente.
C. Muertes y espejo de consumedSpawns.
D (Fase 1b). Flags/eventos y jefes.

## Addendum (hechos verificados, items 16-19) - afectan pasos C y D
- `consumedSpawns` (EnemySpawner.AddConsumedSpawner(EnemySpawnPoint)) solo evita el spawn si se llama ANTES de
  `CreateEnemy()` de esa sala; no elimina enemigos ya nacidos. Para el cliente: al recibir EnemyFullSync de una sala
  ya cargada, desactivar los puppets muertos Y llamar AddConsumedSpawner (para visitas futuras en la sesion).
  Se resetea en Prie Dieu / carga / respawn: el host debe enviar un evento `RespawnEnemies` (reliable) cuando ocurra
  `EnemySpawner.RespawnDeadEnemies/Reset`, y el cliente hace lo mismo localmente.
- Flags: `Core.Events.SetFlag(string id, bool b, bool forcePreserve=false)` es publico y funciona en cualquier momento;
  eventos `Core.Events.OnFlagChanged(string,bool)` y `OnEventLaunched(string,string)` permiten al host capturarlos.
  Los flags son globales por slot y se guardan en cada SaveSnapShot. FSMs Playmaker ya pasados NO se reevaluan al
  fijar un flag tarde: replicar flags en el momento en que cambian, y aplicar el estado completo al cargar sala.
  `PlayMakerFSM.BroadcastEvent("BOSS DEAD")` etc. tambien son replicables (hook en LaunchEvent/BroadcastEvent).
- Puertas/arenas: `Gate.Use()` ALTERNA (no idempotente): el cliente debe comparar `Gate.IsOpenOrActivated()` con el
  estado del host antes de llamar `Use()`. Enviar GateState como estado absoluto (id de gate = path+pos).
- Jefes: `BossFightManager.StartBossFight()` es publica; barra de vida: `BossHealth` lee `target.Stats.Life.Current/Final`
  cada frame => el puppet solo necesita mantener `Stats.Life`. Amanecidas NO nacen de EnemySpawnPoint: `AmanecidasFightSpawner`
  los instancia en `LevelManager.OnLevelLoaded` segun flags locales => el cliente necesita los MISMOS flags que el host
  antes de entrar (de ahi la importancia de FlagChange). Esdras: solo `Esdras.Damage(Hit)` llama `Behaviour.Death()`;
  `Kill()` no. Cutscenes: `Core.Cinematics.EndCutscene(true)`, `SetFreeCamera(false)` y `Core.Input.SetBlocker(name,false)` para
  desbloquear. Blockers conocidos que pueden dejar atascado: CINEMATIC, FREECAMERA, HW_DIALOG_TIME, END_BOSS_DEFEATED,
  TPOReposition. OJO: `InputManager.ResetManager()` no recalcula `InputBlocked` hasta el siguiente `SetBlocker`.
- Los jefes que corren logica local (Snake, TresAngustias) ponen Invulnerable/blockers en el Penitent LOCAL del cliente:
  razon adicional para neutralizar sus behaviours en el cliente (ya previsto).
