using Com.LuisPedroFonseca.ProCamera2D;
using CreativeSpore.SmartColliders;
using Framework.FrameworkCore;
using Framework.Managers;
using System;
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
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace Blasphemous.CoopLocal;

// Parry has the exact same "reads shared Rewired directly, unrelated to who's actually casting"
// problem Dash does. ParryInput (patched below via Parry_ParryInput_Patch) already fixed the
// trigger key itself, but Parry.OnUpdate()'s own gating still bare-checks
// Core.Input.InputBlocked directly - not through PlatformCharacterInput.Blocked, so
// PlatformCharacterInput_Blocked_Patch above doesn't reach it - meaning P2's Parry.Cast() flatly
// refuses to fire whenever *any* PLAYER_LOGIC lock is active anywhere (including P2's own dash
// mid-cancel, or P1 parrying/dashing), and StartParry()/StopParry() push/pop that same global
// blocker exactly like Dash does, freezing the other player's movement while parrying. Reimplemented
// for P2 only: identical logic, but the bare Core.Input.InputBlocked check is replaced with
// PlayerLogicBlocker, and GuardSlide.Casting is read off P2's own ability instead of the base
// game's hardcoded Core.Logic.Penitent.GuardSlide. P1's own instance keeps running the untouched
// original.
[HarmonyPatch(typeof(Parry), "OnUpdate")]
internal static class Parry_OnUpdate_Patch
{
    private static readonly MethodInfo IsGroundedMethod = AccessTools.Method(typeof(Parry), "IsGrounded");
    private static readonly MethodInfo ReadyToCastMethod = AccessTools.Method(typeof(Parry), "ReadyToCast");
    private static readonly MethodInfo RaiseParryEventMethod = AccessTools.Method(typeof(Parry), "RaiseParryEvent");
    private static readonly MethodInfo CheckParryWindowMethod = AccessTools.Method(typeof(Parry), "CheckParryWindow");

    private static bool Prefix(Parry __instance)
    {
        Penitent owner = __instance.GetComponentInParent<Penitent>();
        if (owner == null || owner != CoopLocal.Player2)
        {
            return true;
        }

        bool grounded = (bool)IsGroundedMethod.Invoke(__instance, null);
        bool rawParryKeyDown = Player2Input.ParryDown;
        if (rawParryKeyDown)
        {
            // Same raw-vs-gated split as the Attack/Jump/Dash checks in
            // PlatformCharacterInput_Update_Patch - logs the instant UnityEngine.Input sees the
            // physical Keypad2 press, before any of this method's own gates (grounded/anim-state/
            // PlayerLogicBlocker) get a chance to touch it, to tell "P1 dashing blocks P2's parry
            // logic" apart from "the keypress itself never reaches Unity while Left Shift is held".
            DashParryDebugLog.Log($"P2 raw Input.GetKeyDown(Parry) = True (blocked={PlayerLogicBlocker.IsBlocked(owner)}, frame {Time.frameCount})");
        }
        if (rawParryKeyDown)
        {
            if (!grounded || __instance.IsRunningParryAnim || !(bool)ReadyToCastMethod.Invoke(__instance, null) || __instance.SuccessParry || PlayerLogicBlocker.IsBlocked(owner))
            {
                return false;
            }
            RaiseParryEventMethod.Invoke(__instance, null);
            __instance.Cast();
        }
        else
        {
            if (!__instance.Casting || owner.GuardSlide.Casting)
            {
                return false;
            }
            CheckParryWindowMethod.Invoke(__instance, null);
            bool inParryChance = __instance.EntityOwner.Animator.GetCurrentAnimatorStateInfo(0).IsName("ParryStart")
                || __instance.EntityOwner.Animator.GetCurrentAnimatorStateInfo(0).IsName("ParryChance");
            owner.Parry.IsOnParryChance = inParryChance;
            if (__instance.EntityOwner.Animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"))
            {
                __instance.StopCast();
            }
        }

        if (!__instance.EntityOwner.Status.IsGrounded || __instance.EntityOwner.Status.Dead || __instance.EntityOwner.Status.IsHurt)
        {
            __instance.StopCast();
        }

        return false;
    }
}

[HarmonyPatch(typeof(Parry), "StartParry")]
internal static class Parry_StartParry_Patch
{
    private static void Postfix(Parry __instance)
    {
        Penitent owner = __instance.GetComponentInParent<Penitent>();
        PlayerLogicBlocker.SetBlocked(owner, true);
        SetActionStateWatchWindow.OpenIfPlayer2(owner);
        DashParryDebugLog.Log($"{DashParryDebugLog.Label(owner)} PARRY lock ON (frame {Time.frameCount})");
    }
}

[HarmonyPatch(typeof(Parry), "StopParry")]
internal static class Parry_StopParry_Patch
{
    private static void Postfix(Parry __instance)
    {
        Penitent owner = __instance.GetComponentInParent<Penitent>();
        PlayerLogicBlocker.SetBlocked(owner, false);
        DashParryDebugLog.Log($"{DashParryDebugLog.Label(owner)} PARRY lock OFF (frame {Time.frameCount})");
    }
}

// ParryRepostBehaviour and ParrySuccessBehaviour (the two Animator states reached only on a
// *successful* parry - blocking a real hit) have the same _penitent-falls-back-to-P1 bug as
// everything above, just spelled as an auto-property (`Penitent { get; set; }`) instead of a
// plain field - so the usual "ref Penitent ____penitent" Harmony injection doesn't apply
// directly; this goes through the compiler-generated backing field instead, same trick already
// used for PlatformCharacterInput's Jump/FVerAxis auto-properties above. Both only toggle
// Status.Invulnerable, so on their own they can't explain a movement freeze - but if P2's
// successful parry ends up flagging *P1* invulnerable instead of P2, that's still a real,
// separate bug worth closing now that it's been found.
[HarmonyPatch(typeof(ParryRepostBehaviour), "OnStateEnter")]
internal static class ParryRepostBehaviour_OnStateEnter_Patch
{
    private static readonly FieldInfo PenitentBackingField = AccessTools.Field(typeof(ParryRepostBehaviour), "<Penitent>k__BackingField");

    private static void Prefix(Animator animator, ParryRepostBehaviour __instance)
    {
        Penitent owner = animator.GetComponentInParent<Penitent>();
        if (owner != null)
        {
            PenitentBackingField.SetValue(__instance, owner);
        }
    }
}

[HarmonyPatch(typeof(ParrySuccessBehaviour), "OnStateEnter")]
internal static class ParrySuccessBehaviour_OnStateEnter_Patch
{
    private static readonly FieldInfo PenitentBackingField = AccessTools.Field(typeof(ParrySuccessBehaviour), "<Penitent>k__BackingField");

    private static void Prefix(Animator animator, ParrySuccessBehaviour __instance)
    {
        Penitent owner = animator.GetComponentInParent<Penitent>();
        if (owner != null)
        {
            PenitentBackingField.SetValue(__instance, owner);
        }
    }
}


