using Com.LuisPedroFonseca.ProCamera2D;
using CreativeSpore.SmartColliders;
using Framework.FrameworkCore;
using Framework.Managers;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Gameplay.GameControllers.AnimationBehaviours.Player.Attack;
using Gameplay.GameControllers.AnimationBehaviours.Player.ClimbClifLede;
using Gameplay.GameControllers.AnimationBehaviours.Player.ClimbLadder;
using Gameplay.GameControllers.AnimationBehaviours.Player.Crouch;
using Gameplay.GameControllers.AnimationBehaviours.Player.Dash;
using Gameplay.GameControllers.AnimationBehaviours.Player.Hurt;
using Gameplay.GameControllers.AnimationBehaviours.Player.Jump;
using Gameplay.GameControllers.AnimationBehaviours.Player.Dead;
using Gameplay.GameControllers.AnimationBehaviours.Player.Prayer;
using Gameplay.GameControllers.AnimationBehaviours.Player.RangeAttack;
using Gameplay.GameControllers.AnimationBehaviours.Player.Run;
using Gameplay.GameControllers.AnimationBehaviours.Player.SubStatesBehaviours;
using Gameplay.GameControllers.Camera;
using Gameplay.GameControllers.Effects.Player.Recolor;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Enemies.Framework.Attack;
using Gameplay.GameControllers.Environment.AreaEffects;
using Gameplay.GameControllers.Penitent;
using Gameplay.GameControllers.Penitent.Abilities;
using Gameplay.GameControllers.Penitent.Attack;
using Gameplay.GameControllers.Penitent.Damage;
using Gameplay.GameControllers.Penitent.Gizmos;
using Gameplay.GameControllers.Penitent.InputSystem;
using Gameplay.GameControllers.Penitent.Sensor;
using Gameplay.UI.Others.UIGameLogic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Blasphemous.CoopLocal;

// Round 62 - VerticalAttack (bug reportado #3, "Vertical Attack no funciona en absoluto para P2").
// VerticalAttack.OnUpdate() (Gameplay.GameControllers.Penitent.Abilities.VerticalAttack) ya resuelve
// su owner correctamente por instancia (_penitent = base.EntityOwner.GetComponent<Penitent>() en
// OnStart) salvo por UNA linea dentro del gate principal:
//   _rewired.GetButtonTimedPress("Attack", AttackButtonHoldTime)
// donde _rewired = ReInput.players.GetPlayer(0) (Rewired Player 0 compartido, asignado en OnStart
// tambien de forma per-instance pero apuntando siempre al mismo objeto fisico). Familia 2 -
// "input leido directo de Rewired compartido" - ya documentado como hallazgo colateral en la Ronda
// 60 de NOTES.md. Con este gate roto, el Vertical Attack de P2 solo entra en carga cuando P1
// sostiene fisicamente el boton de ataque real en el aire, sin relacion con el input propio de P2 -
// esto explica "no funciona en absoluto" independientemente del estado del Skill Tree (bug #3 es
// ortogonal al fix de Player2SkillManager.cs de esta misma ronda).
//
// El resto de OnUpdate() (~80 lineas, muchas ramas de animator state + varios campos privados) es
// correcto per-instancia y no vale la pena reimplementar entero solo para esta unica lectura -
// mismo patron ya usado en Movement/LadderMechanics.cs
// (PlatformCharacterController_DoClimbing_P2_AirGrab_Patch): un Transpiler retarga unicamente la
// llamada a Player.GetButtonTimedPress(string,float), un Prefix companero en el mismo metodo
// captura que instancia de VerticalAttack esta corriendo (seguro: Unity's Update() de un solo hilo
// nunca intercala dos VerticalAttack.OnUpdate() a la vez).
[HarmonyPatch(typeof(VerticalAttack), "OnUpdate")]
internal static class VerticalAttack_OnUpdate_P2_TimedPress_Patch
{
    private static readonly MethodInfo RewiredGetButtonTimedPressMethod =
        AccessTools.Method(typeof(Rewired.Player), "GetButtonTimedPress", new[] { typeof(string), typeof(float) });

    private static readonly MethodInfo ReplacementMethod =
        AccessTools.Method(typeof(VerticalAttack_OnUpdate_P2_TimedPress_Patch), nameof(GetButtonTimedPressForVerticalAttack));

    // Set by our own Prefix immediately before the original (transpiled) method body runs, in the
    // same call - safe, same rationale as PlatformCharacterController_DoClimbing_P2_AirGrab_Patch
    // in Movement/LadderMechanics.cs (Unity's single-threaded Update() loop can never interleave
    // two VerticalAttack.OnUpdate() calls).
    private static VerticalAttack currentInstance;

    // Per-instance-equivalent timer state for P2's own VerticalAttack hold gate - there is only
    // ever one live P2 VerticalAttack component at a time (same assumption Round 51's
    // player2AttackHoldTimer in Movement/Movement.cs already relies on for IsAttackButtonHold),
    // so plain static fields are safe here instead of a per-instance dictionary.
    private static float player2HoldTimer;
    private static bool player2Held;

    private static void Prefix(VerticalAttack __instance)
    {
        currentInstance = __instance;
    }

    private static bool GetButtonTimedPressForVerticalAttack(Rewired.Player player, string actionName, float time)
    {
        VerticalAttack self = currentInstance;
        Penitent owner = self != null ? self.EntityOwner as Penitent : null;
        if (owner == null || owner != CoopLocal.Player2)
        {
            // P1 (or anything else driving this component) - untouched vanilla behavior.
            return player.GetButtonTimedPress(actionName, time);
        }

        bool heldNow = Player2Input.AttackHeld;
        if (!heldNow)
        {
            player2HoldTimer = 0f;
            player2Held = false;
        }
        else
        {
            player2HoldTimer += Time.deltaTime;
            if (player2HoldTimer >= time)
            {
                player2Held = true;
            }
        }
        return player2Held;
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        bool patched = false;
        foreach (CodeInstruction instruction in instructions)
        {
            if (!patched && instruction.opcode == OpCodes.Callvirt &&
                instruction.operand is MethodInfo method && method == RewiredGetButtonTimedPressMethod)
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = ReplacementMethod;
                patched = true;
            }
            yield return instruction;
        }
        if (!patched)
        {
            DashParryDebugLog.Log("[DashParryDebug] VerticalAttack.OnUpdate transpiler did NOT find Player.GetButtonTimedPress(string,float) - P2 VerticalAttack input fix NOT applied!");
        }
    }
}

// Round 62 - RangeAttack (bug reportado #5, "solo reacciona/aplica efecto cuando P1 lo dispara").
// Confirmado contra el decompilado real (Gameplay.GameControllers.Penitent.Abilities.RangeAttack):
// a diferencia de VerticalAttack, esta clase NO resuelve su owner correctamente en casi nada -
// hardcodea Core.Logic.Penitent (el singleton de P1) literalmente en OnStart, OnUpdate,
// CastRangeAttack, OnCastStart e InstanceProjectile, ademas de leer el boton 57 directo del
// Rewired compartido:
//   OnStart:      _rootMotion = Core.Logic.Penitent.GetComponentInChildren<RootMotionDriver>();
//   OnUpdate:     _rewired = Core.Logic.Penitent.PlatformCharacterInput.Rewired; (mismo objeto
//                 compartido igual, pero el _rewired.GetButtonDown/Up(57) que sigue SI es family-2)
//                 Penitent penitent = Core.Logic.Penitent; usado para RangeAttackCancelledByAbility
//                 y para decidir si cancelar el ataque - lee el estado de P1, no el de quien
//                 realmente posee este RangeAttack.
//   CastRangeAttack: la rama aerea usa Core.Logic.Penitent.PlatformCharacterController.GroundDist
//                 en vez de la del propio owner.
//   OnCastStart:  Core.Logic.Penitent.Dash.StopCast() - para el dash de P1 sin importar quien
//                 disparo el RangeAttack.
//   InstanceProjectile: la altura Y del proyectil usa Core.Logic.Penitent.DamageArea.Center().y
//                 en vez de la del propio owner.
// Con esto, el RangeAttack de P2 en la practica reacciona al estado fisico/input de P1 (si P1 esta
// en el suelo/aire, si P1 suelta el boton 57) y no al propio de P2 - coincide exactamente con el
// bug reportado. Reimplementado method-by-method (Prefix devolviendo false + logica reescrita
// sustituyendo cada Core.Logic.Penitent por el owner real), dejando P1 intacto (untouched vanilla)
// vía el mismo guard `owner != CoopLocal.Player2 => return true` usado en todo el resto del mod.
//
// Incertidumbre real, no resuelta por lectura de codigo: el boton "57" no tiene nombre visible en
// el decompilado (es un id numerico de accion de Rewired, no la misma "Attack" que triggerCode/
// button 5 usan en otros lados de este mismo archivo - confirmado distinto en Movement/Movement.cs,
// que documenta boton 5=Attack, 7=Dash). Se sustituyo por Player2Input.AttackDown/AttackUp por ser
// la hipotesis mas plausible (mismo gesto de "mantener y soltar" que Attack/VerticalAttack ya usan),
// pero esto NO esta confirmado contra el mapeo real de Rewired - ver el log
// "[RangeAttack] boton Rewired id=57 real ->" (una sola vez, en el primer P1 GetButtonDown(57) que
// ocurra) para confirmar/corregir si hiciera falta.
// Shared reflection handles + helpers used by every RangeAttack patch below. Not a Harmony patch
// class itself - kept separate (rather than nesting patch classes inside it) to match this
// codebase's existing convention of one top-level [HarmonyPatch] class per patched method.
internal static class RangeAttackP2Shared
{
    internal static readonly FieldInfo RootMotionField = AccessTools.Field(typeof(RangeAttack), "_rootMotion");
    internal static readonly FieldInfo RewiredField = AccessTools.Field(typeof(RangeAttack), "_rewired");
    internal static readonly FieldInfo PressedKeyDownField = AccessTools.Field(typeof(RangeAttack), "_pressedKeyDown");
    internal static readonly FieldInfo CurrentTimeThresholdField = AccessTools.Field(typeof(RangeAttack), "currentTimeThreshold");
    internal static readonly FieldInfo AbilityTimeThresholdField = AccessTools.Field(typeof(RangeAttack), "abilityTimeThreshold");

    internal static readonly MethodInfo RangeAttackCancelledByAbilityMethod =
        AccessTools.Method(typeof(RangeAttack), "RangeAttackCancelledByAbility");
    internal static readonly MethodInfo CastRangeAttackMethod =
        AccessTools.Method(typeof(RangeAttack), "CastRangeAttack");
    internal static readonly MethodInfo GetLastUnlockedSkillMethod =
        AccessTools.Method(typeof(Ability), "GetLastUnlockedSkill");
    internal static readonly PropertyInfo HasEnoughFervourProperty =
        AccessTools.Property(typeof(Ability), "HasEnoughFervour");
    internal static readonly FieldInfo LastUnlockedSkillIdBackingField =
        AccessTools.Field(typeof(Ability), "<LastUnlockedSkillId>k__BackingField");

    internal static bool loggedButton57Name;

    internal static Penitent OwnerOf(RangeAttack instance)
    {
        return instance.EntityOwner as Penitent;
    }

    // One-shot diagnostic: logs the real Rewired action name behind the numeric id "57" this
    // ability reads directly (`_rewired.GetButtonDown(57)`), since that action id has no name
    // visible anywhere in the decompiled source and might not actually be the same physical
    // button as Player2Input's "Attack" (see class comment above for why this substitution is
    // a plausible-but-unconfirmed hypothesis). Fires once, off P1's own real press (vanilla,
    // untouched), so it costs nothing and needs no P2 input to trigger.
    internal static void LogButton57NameOnce(RangeAttack instance)
    {
        if (loggedButton57Name)
        {
            return;
        }
        Rewired.Player rewired = (Rewired.Player)RewiredField.GetValue(instance);
        if (rewired == null || !rewired.GetButtonDown(57))
        {
            return;
        }
        loggedButton57Name = true;
        try
        {
            Rewired.InputAction action = Rewired.ReInput.mapping.GetAction(57);
            DashParryDebugLog.Log($"[RangeAttack] boton Rewired id=57 real -> \"{(action != null ? action.name : "desconocido")}\"");
        }
        catch (Exception ex)
        {
            DashParryDebugLog.Log($"[RangeAttack] no se pudo resolver el nombre de la accion id=57: {ex.Message}");
        }
    }
}

// OnStart: _rootMotion es la unica pieza rota aca (todo lo demas - creacion de pools - es
// global/idempotente). Postfix simple: dejar correr vanilla y despues corregir el campo.
[HarmonyPatch(typeof(RangeAttack), "OnStart")]
internal static class RangeAttack_OnStart_P2_Patch
{
    private static void Postfix(RangeAttack __instance)
    {
        Penitent owner = RangeAttackP2Shared.OwnerOf(__instance);
        if (owner == null || owner != CoopLocal.Player2)
        {
            return;
        }
        RootMotionDriver rootMotion = owner.GetComponentInChildren<RootMotionDriver>();
        RangeAttackP2Shared.RootMotionField.SetValue(__instance, rootMotion);
    }
}

// OnUpdate: family 2 (boton 57 compartido) + hardcodeo de Core.Logic.Penitent para el gate de
// cancelacion. Full reimplementation para P2 (Prefix -> false), P1 sigue vanilla intacto.
[HarmonyPatch(typeof(RangeAttack), "OnUpdate")]
internal static class RangeAttack_OnUpdate_P2_Patch
{
    private static readonly Dictionary<RangeAttack, string> lastRangeSkill = new Dictionary<RangeAttack, string>();

    private static bool Prefix(RangeAttack __instance)
    {
        Penitent owner = RangeAttackP2Shared.OwnerOf(__instance);
        if (owner == null || owner != CoopLocal.Player2)
        {
            RangeAttackP2Shared.LogButton57NameOnce(__instance);
            return true;
        }

        float currentTimeThreshold = (float)RangeAttackP2Shared.CurrentTimeThresholdField.GetValue(__instance);
        currentTimeThreshold += Time.deltaTime;
        RangeAttackP2Shared.CurrentTimeThresholdField.SetValue(__instance, currentTimeThreshold);

        bool cancelled = (bool)RangeAttackP2Shared.RangeAttackCancelledByAbilityMethod.Invoke(__instance, new object[] { owner });
        if (cancelled)
        {
            return false;
        }

        bool pressedKeyDown = (bool)RangeAttackP2Shared.PressedKeyDownField.GetValue(__instance);
        if (Player2Input.AttackDown && !pressedKeyDown)
        {
            pressedKeyDown = true;
            RangeAttackP2Shared.PressedKeyDownField.SetValue(__instance, true);
        }
        bool buttonUp = Player2Input.AttackUp;

        if (Core.Input.InputBlocked)
        {
            RangeAttackP2Shared.PressedKeyDownField.SetValue(__instance, false);
            return false;
        }

        if (!buttonUp || !pressedKeyDown)
        {
            return false;
        }

        UnlockableSkill lastUnlockedSkill = (UnlockableSkill)RangeAttackP2Shared.GetLastUnlockedSkillMethod.Invoke(__instance, null);
        string curId = lastUnlockedSkill != null ? lastUnlockedSkill.id : "null";
        if (!lastRangeSkill.TryGetValue(__instance, out string last) || last != curId)
        {
            lastRangeSkill[__instance] = curId;
            DashParryDebugLog.Log($"[Ability] RangeAttack P2 GetLastUnlockedSkill -> {curId} (owner={DashParryDebugLog.Label(owner)}:{owner.GetInstanceID()} viewP2={Player2MenuView.IsInventoryP2View})");
        }
        if (lastUnlockedSkill == null || owner.Status.Dead)
        {
            return false;
        }
        RangeAttackP2Shared.LastUnlockedSkillIdBackingField.SetValue(__instance, lastUnlockedSkill.id);

        bool hasEnoughFervour = (bool)RangeAttackP2Shared.HasEnoughFervourProperty.GetValue(__instance, null);
        currentTimeThreshold = (float)RangeAttackP2Shared.CurrentTimeThresholdField.GetValue(__instance);
        float abilityTimeThreshold = (float)RangeAttackP2Shared.AbilityTimeThresholdField.GetValue(__instance);
        if (!__instance.Casting && !(currentTimeThreshold < abilityTimeThreshold) && hasEnoughFervour)
        {
            RangeAttackP2Shared.CastRangeAttackMethod.Invoke(__instance, null);
        }
        return false;
    }
}

// CastRangeAttack: unica linea rota es la rama aerea (Core.Logic.Penitent.PlatformCharacterController
// .GroundDist en vez de la del owner real). Full reimplementation, misma logica exacta.
[HarmonyPatch(typeof(RangeAttack), "CastRangeAttack")]
internal static class RangeAttack_CastRangeAttack_P2_Patch
{
    private static bool Prefix(RangeAttack __instance)
    {
        Penitent owner = RangeAttackP2Shared.OwnerOf(__instance);
        if (owner == null || owner != CoopLocal.Player2)
        {
            return true;
        }
        if (owner.Status.IsGrounded)
        {
            __instance.Cast();
            RangeAttackP2Shared.PressedKeyDownField.SetValue(__instance, false);
            owner.Animator.Play(RangeAttack.GroundRangeAttackAnim);
        }
        else if (owner.PlatformCharacterController.GroundDist >= 1f)
        {
            __instance.Cast();
            RangeAttackP2Shared.PressedKeyDownField.SetValue(__instance, false);
            owner.Animator.Play(RangeAttack.MidAirRangeAttackAnim);
        }
        return false;
    }
}

// OnCastStart: vanilla incondicionalmente para Core.Logic.Penitent.Dash.StopCast() sin
// importar quien disparo el RangeAttack - dejado tal cual para P1 (comportamiento pre-existente,
// no reportado como bug), pero ademas se corta el propio dash del owner real cuando es P2 -
// el resto de OnCastStart (Fervour/audio) ya corre correctamente per-instancia via
// base.EntityOwner, asi que un Postfix alcanza aca (no hace falta bloquear vanilla).
[HarmonyPatch(typeof(RangeAttack), "OnCastStart")]
internal static class RangeAttack_OnCastStart_P2_Patch
{
    private static void Postfix(RangeAttack __instance)
    {
        Penitent owner = RangeAttackP2Shared.OwnerOf(__instance);
        if (owner == null || owner != CoopLocal.Player2)
        {
            return;
        }
        owner.Dash.StopCast();
    }
}

// InstanceProjectile: la altura Y del punto de disparo usa Core.Logic.Penitent.DamageArea en
// vez de la del owner real - esto corre ANTES de instanciar el proyectil, asi que un Postfix
// llegaria tarde. Full reimplementation (metodo corto, sin efectos colaterales previos).
[HarmonyPatch(typeof(RangeAttack), "InstanceProjectile")]
internal static class RangeAttack_InstanceProjectile_P2_Patch
{
    private static bool Prefix(RangeAttack __instance)
    {
        Penitent owner = RangeAttackP2Shared.OwnerOf(__instance);
        if (owner == null || owner != CoopLocal.Player2)
        {
            return true;
        }
        if (__instance.RangeAttackProjectile == null)
        {
            return false;
        }
        RootMotionDriver rootMotion = (RootMotionDriver)RangeAttackP2Shared.RootMotionField.GetValue(__instance);
        Vector3 position = (owner.Status.Orientation != EntityOrientation.Right)
            ? __instance.GetReverseFirePosition()
            : rootMotion.transform.position;
        position.y = owner.DamageArea.Center().y + 0.2f;
        PoolManager.Instance.ReuseObject(__instance.RangeAttackProjectile, position, Quaternion.identity);
        return false;
    }
}

// --- Logging adicional Ronda 74 para Skill Tree compartido ---

[HarmonyPatch(typeof(VerticalAttack), "OnUpdate")]
internal static class VerticalAttack_OnUpdate_SkillLog_Patch
{
    private static readonly Dictionary<VerticalAttack, string> lastVerticalSkill = new Dictionary<VerticalAttack, string>();
    private static readonly MethodInfo GetLastSkillMethod = AccessTools.Method(typeof(Ability), "GetLastUnlockedSkill");

    private static void Postfix(VerticalAttack __instance)
    {
        Penitent owner = __instance.EntityOwner as Penitent;
        if (owner == null || owner != CoopLocal.Player2) return;
        // Solo loguear cuando intenta vertical attack (cerca de gate) para no spamear
        // Chequeamos si está en aire y con input relevante
        if (!owner.Status.IsGrounded && owner.PlatformCharacterInput != null && owner.PlatformCharacterInput.isJoystickDown)
        {
            UnlockableSkill skill = (UnlockableSkill)GetLastSkillMethod.Invoke(__instance, null);
            string curId = skill != null ? skill.id : "null";
            if (!lastVerticalSkill.TryGetValue(__instance, out string last) || last != curId)
            {
                lastVerticalSkill[__instance] = curId;
                DashParryDebugLog.Log($"[Ability] VerticalAttack P2 GetLastUnlockedSkill -> {curId} (owner={DashParryDebugLog.Label(owner)}:{owner.GetInstanceID()}) isGrounded={owner.Status.IsGrounded} vSpeed={owner.PlatformCharacterController.PlatformCharacterPhysics.VSpeed:F2}");
            }
        }
    }
}

[HarmonyPatch(typeof(Gameplay.GameControllers.Penitent.Animator.AnimatorInyector), "ChargeAttackTriggered")]
internal static class AnimatorInyector_ChargeAttackTriggered_SkillLog_Patch
{
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(Gameplay.GameControllers.Penitent.Animator.AnimatorInyector), "_penitent");
    private static readonly Dictionary<Gameplay.GameControllers.Penitent.Animator.AnimatorInyector, string> lastChargeSkill = new Dictionary<Gameplay.GameControllers.Penitent.Animator.AnimatorInyector, string>();

    // Round 76 audit: ChargeAttackTriggered() runs every single frame while grounded (called from
    // AnimatorInyector.ChargedAttack(), itself called from UpdateActions() every Update() while
    // _isGrounded) - re-resolving these via AccessTools.Property/Method on every call (as the
    // original version of this patch did) repeats a reflection member lookup every frame P2 is
    // grounded, whether or not anything ends up being logged. Cached once, same pattern already
    // used by RangeAttackP2Shared/VerticalAttack_OnUpdate_SkillLog_Patch in this same file.
    private static readonly PropertyInfo HasEnoughFervourProperty = AccessTools.Property(typeof(Ability), "HasEnoughFervour");
    private static readonly MethodInfo GetLastUnlockedSkillMethod = AccessTools.Method(typeof(Ability), "GetLastUnlockedSkill");

    private static void Prefix(Gameplay.GameControllers.Penitent.Animator.AnimatorInyector __instance)
    {
        Penitent penitent = PenitentField.GetValue(__instance) as Penitent;
        if (penitent == null || penitent != CoopLocal.Player2) return;
        var chargedAttack = penitent.ChargedAttack;
        string curId = "null";
        bool available = false;
        bool hasFervour = false;
        if (chargedAttack != null)
        {
            available = chargedAttack.IsAvailableSkilledAbility;
            if (HasEnoughFervourProperty != null) hasFervour = (bool)HasEnoughFervourProperty.GetValue(chargedAttack, null);
            var skill = GetLastUnlockedSkillMethod.Invoke(chargedAttack, null) as UnlockableSkill;
            curId = skill != null ? skill.id : "null";
        }
        string key = $"{curId}:{available}";
        if (!lastChargeSkill.TryGetValue(__instance, out string last) || last != key)
        {
            lastChargeSkill[__instance] = key;
            DashParryDebugLog.Log($"[Ability] ChargedAttack P2 IsAvailableSkilledAbility={available} lastSkill={curId} (owner={DashParryDebugLog.Label(penitent)}:{penitent.GetInstanceID()} IsCharging={penitent.IsChargingAttack} HasFervour={hasFervour})");
        }
    }
}
