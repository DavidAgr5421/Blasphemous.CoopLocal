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

// Round 45: the real "rest at a shrine" heal - PrieDieu.ShallowActivationLogic (private, called
// from both first-time and repeat-use activation coroutines) hardcodes Core.Logic.Penitent for
// Life/Flask/Fervour healing, same as everywhere else this session, but this one matters for a
// different reason than "wrong owner": P2 doesn't have its OWN PrieDieu component at all (P1's is
// the only one, tied to the single shared shrine), so there's nothing to "fix the owner of" - P2
// simply never got healed here. Postfix (not Prefix, since vanilla's own P1 heal should still run
// normally) adds the same treatment for P2, gating Fervour on the identical
// Core.Alms.GetPrieDieuLevel() > 1 condition P1's own heal checks.
[HarmonyPatch(typeof(Tools.Level.Interactables.PrieDieu), "ShallowActivationLogic")]
internal static class PrieDieu_ShallowActivationLogic_HealPlayer2_Patch
{
    private static void Postfix()
    {
        if (Player2DeathState.IsPendingRevive())
        {
            Player2DeathState.ClearPendingRevive();
            if (CoopLocal.Player2 == null)
            {
                CoopLocal.SpawnPlayer2(Core.Logic.Penitent, Core.Logic.Penitent.transform.position);
            }
        }
        Penitent p2 = CoopLocal.Player2;
        if (p2 == null)
        {
            return;
        }
        bool healFervour = Core.Alms.GetPrieDieuLevel() > 1;
        Player2StatsSync.HealAtPrieDieu(p2, healFervour);
    }
}

// Round 44: user reported P2 getting "stuck" to walls (cliff-ledge grab) whenever *P1* presses
// attack, and jumping off ladders whenever *P1* presses jump - two separate bugs in two separate
// ability classes, both previously untouched since neither is an AnimationBehaviour (the ~50-class
// batch scan from earlier this session only covered StateMachineBehaviour subclasses).
//
// GrabCliffLede.Start() does `_penitent = Core.Logic.Penitent;` - the exact same "wrong owner"
// hardcode found dozens of times already, just in a per-Penitent MonoBehaviour component instead
// of an AnimationBehaviour. Every method in the class (Update/OnTriggerStay2D/grabCliffLede/etc)
// reads P1's IsFalling/IsGrounded/animator state through this one field, so P2's own wall-cling
// eligibility was being decided by P1's movement state instead of P2's own.
//
// Round 47 correction: originally "fixed" here with a Prefix (removed - it never actually did
// anything). Turns out this exact bug, on this exact method, was *already* fixed much earlier
// this session by GrabCliffLede_Start_Patch (search this file - a Postfix using
// GetComponentInParent<Penitent>()), whose own comment explicitly explains why a Prefix can't
// work here: Start()'s real assignment has no null-guard at all (`_penitent = Core.Logic.Penitent;`
// unconditionally, every single call, not "only if null" like the AnimationBehaviour family), so
// any Prefix pre-setting the field just gets silently overwritten by vanilla's own body a moment
// later - only a Postfix (running *after* vanilla overwrites it) can actually stick. The Prefix
// added here was therefore dead code the whole time - confirmed harmless (the pre-existing Postfix
// still corrected the field correctly afterward either way) but misleading, so removed. The
// diagnostic Postfix below (added the same round as the dead Prefix) is unaffected by any of this
// and remains accurate - its own log lines already prove the owner resolves to P2 correctly.
//
// Round 45: no log data existed yet to confirm what was/wasn't working here (unlike GrabLadder,
// which already had its own debug logger from earlier in the session), so this diagnostic was
// added rather than guessing blind. Mirrors GrabLadder_OnUpdate_DebugLogger_Patch's own approach -
// logs P2's own grab-eligibility state (the exact fields OnTriggerStay2D's condition checks)
// every time it changes. This is what actually found the real cause (see CoopLocal.cs's
// SetLayerRecursively / LevelManager.OnLevelLoaded re-sync, round 46/47) - _grabbedCliffLede
// stayed null across thousands of airborne frames, which OnTriggerEnter2D only ever sets from
// pure Unity physics-layer filtering, no ownership logic involved.
[HarmonyPatch(typeof(GrabCliffLede), "Update")]
internal static class GrabCliffLede_Update_DebugLogger_Patch
{
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(GrabCliffLede), "_penitent");
    private static readonly FieldInfo GrabbedCliffLedeField = AccessTools.Field(typeof(GrabCliffLede), "_grabbedCliffLede");
    private static readonly FieldInfo IsGrabbedCliffLedeField = AccessTools.Field(typeof(GrabCliffLede), "_isGrabbedCliffLede");
    private static readonly FieldInfo IsAirAttackingField = AccessTools.Field(typeof(GrabCliffLede), "_isAirAttacking");
    private static readonly FieldInfo RemainCooldownField = AccessTools.Field(typeof(GrabCliffLede), "remainCooldown");
    private static string lastLoggedState;

    private static void Postfix(GrabCliffLede __instance)
    {
        Penitent owner = (Penitent)PenitentField.GetValue(__instance);
        if (owner == null || owner != CoopLocal.Player2)
        {
            return;
        }
        Collider2D grabbedCliffLede = (Collider2D)GrabbedCliffLedeField.GetValue(__instance);
        bool isGrabbed = (bool)IsGrabbedCliffLedeField.GetValue(__instance);
        bool isAirAttacking = (bool)IsAirAttackingField.GetValue(__instance);
        float remainCooldown = (float)RemainCooldownField.GetValue(__instance);
        string state = $"grabbedCliffLede={(grabbedCliffLede != null ? grabbedCliffLede.name : "null")} isGrabbed={isGrabbed} " +
            $"isAirAttacking={isAirAttacking} remainCooldown={remainCooldown:F2} IsGrabbingCliffLede={owner.IsGrabbingCliffLede} " +
            $"IsJumpingOff={owner.IsJumpingOff} IsDashing={owner.IsDashing} IsFalling={owner.AnimatorInyector.IsFalling} " +
            $"IsGrounded={owner.Status.IsGrounded} canClimbCliffLede={owner.canClimbCliffLede}";
        if (state != lastLoggedState)
        {
            lastLoggedState = state;
            DashParryDebugLog.Log($"P2 GrabCliffLede.Update: {state} (frame {Time.frameCount})");
        }
    }
}
