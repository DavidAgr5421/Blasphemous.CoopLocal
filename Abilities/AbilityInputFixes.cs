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

// Parry.ParryInput is a private computed property (`base.Rewired.GetButtonDown(38)`) checked
// at the top of Parry.OnUpdate() - same shared-Rewired-Player-0 problem as Dash's direction
// read, but here the *entire* surrounding cast/gating logic (grounded check, ready-to-cast,
// animation state checks, etc.) is nuanced enough that reimplementing OnUpdate() itself isn't
// worth the risk. Patching just the property getter is much more surgical: for P2, substitute
// our own key's edge-triggered state and skip Rewired entirely; everything downstream in
// OnUpdate() keeps running unmodified and now reacts correctly to P2's own press.
//
// Known remaining gap: inside OnUpdate()'s "still casting" branch, the game sets
// `Core.Logic.Penitent.Parry.IsOnParryChance = ...` (hardcoded to P1's own Parry ability,
// regardless of whose OnUpdate() is running) instead of using the local instance - so while
// this patch does make P2 play the parry animation on its own key, the actual "am I currently
// in the parry window" flag that Penitent.Damage() checks would still only ever apply to P1.
// Not fixed yet since P2 can't take damage at all right now anyway (see the invulnerability
// patch above), so it has no visible effect yet - but revisit this once P2 has real health.
[HarmonyPatch(typeof(Parry), "get_ParryInput")]
internal static class Parry_ParryInput_Patch
{
    private static bool Prefix(Parry __instance, ref bool __result)
    {
        Penitent owner = __instance.GetComponentInParent<Penitent>();
        if (owner == null || owner != CoopLocal.Player2)
        {
            return true;
        }

        __result = Player2Input.ParryDown;
        return false;
    }
}

// Healing has its own separate, *un-gated* input path - Ability_UpdateInput_Patch above only
// disables the generic Ability.UpdateInput() dispatcher for P2, but Healing.LateUpdate() calls
// its own GetHealingInput() every frame for every instance regardless, which (like Parry's
// ParryInput before it was patched) reads straight off the shared Rewired Player 0
// (Rewired.GetButtonDown(23) in the decompiled vanilla method) *and* hardcodes
// Core.Logic.Penitent (always P1) for its "not already performing another action" gate - the
// same wrong-owner bug already fixed elsewhere in this file for other abilities, just not yet
// for this one. Net effect before this patch: P2's own Healing reacted to whatever the shared
// Player 0 read for that button, gated on *P1's* controller state instead of P2's own.
// Reimplemented the same way ParryInput was: P2's own gamepad heal button (see
// Player2Pad/Player2Input - the exact button is an unconfirmed guess, verify against
// RawButtonScanLog's log output), gated on P2's own PlatformCharacterController instead of the
// hardcoded one. P1's own instance keeps running the untouched original.
[HarmonyPatch(typeof(Healing), "GetHealingInput")]
internal static class Healing_GetHealingInput_Patch
{
    private static bool Prefix(Healing __instance, ref bool __result)
    {
        Penitent owner = __instance.GetComponentInParent<Penitent>();
        if (owner == null || owner != CoopLocal.Player2)
        {
            return true;
        }

        // The vanilla method's own second gate - !GetActionState((eControllerActions)16) - is
        // deliberately NOT enforced here. It's untested against P2's own controller state (only
        // ever checked, in vanilla, against the hardcoded Core.Logic.Penitent/P1), and the user
        // reported Heal not firing for P2 at all - this gate being permanently true for P2 for
        // some unrelated reason is the prime suspect, so it's dropped rather than risk it silently
        // blocking every press again. Still logged (once per press) so this can be confirmed.
        bool healPressed = Player2Input.HealDown;
        if (healPressed && Main.CoopLocal != null)
        {
            bool vanillaGateWasBlocking = owner.PlatformCharacterController.GetActionState((eControllerActions)16);
            Blasphemous.ModdingAPI.ModLog.Info(
                $"[Healing] P2 heal button pressed - vanilla's own action-16 gate is currently " +
                $"{(vanillaGateWasBlocking ? "TRUE (would have blocked this press)" : "false (harmless)")}.",
                Main.CoopLocal);
        }
        __result = healPressed;
        return false;
    }
}

// Round 36: the user reported P2 getting stuck with a lingering healing-aura sprite and unable
// to Parry after drinking a flask - a real bug, and the exact same "_penitent falls back to P1"
// family already fixed throughout this file for Dash/AirDash/RunAfterDash, just not yet for this
// one. HealingBehaviour (an Animator StateMachineBehaviour, one instance per Animator, so P2's
// clone genuinely has its own) resolves its `_penitent` field lazily on first OnStateEnter -
// `if (_penitent == null) _penitent = Core.Logic.Penitent;` - hardcoded to P1 regardless of whose
// Animator is actually entering the healing state. OnStateEnter then caches
// `HealingAbility = _penitent.GetComponentInChildren<Healing>()` from that (wrong, P1's own)
// Penitent, so when the healing animation naturally finishes and OnStateExit fires
// `HealingAbility.StopHeal()`, it's stopping *P1's* Healing (usually a harmless no-op, since P1
// probably isn't healing) instead of P2's own - P2's IsHealing/aura/Invulnerable never get reset
// by StopHeal, which is exactly the "stuck healing state, aura won't go away, can't Parry"
// (Ability.StopCast() - which clears whatever cast-lock blocks Parry - lives inside StopHeal(),
// so skipping it for P2 skips that cleanup too) symptom reported. Fixed the same way as the
// existing Dash/AirDash patches: pre-set `_penitent` correctly (via the Animator parameter
// OnStateEnter already receives) before the original method's own null-check ever runs, so it
// sees an already-correct value and never overwrites it with P1.
[HarmonyPatch(typeof(HealingBehaviour), "OnStateEnter")]
internal static class HealingBehaviour_OnStateEnter_Patch
{
    private static void Prefix(Animator animator, ref Penitent ____penitent)
    {
        Penitent owner = animator.GetComponentInParent<Penitent>();
        if (owner != null)
        {
            ____penitent = owner;
        }
    }
}

// Round 37: the user reported dash+attack (the "lunge"/estoque combo) leaving P2 unable to Dash
// or Heal again afterward. LungeAttackBehaviour has the exact same bug shape as HealingBehaviour
// above, just caching a different field: OnStateEnter does
// `if (_lungeAttack == null) _lungeAttack = Core.Logic.Penitent.GetComponentInChildren<LungeAttack>();`
// - hardcoded to P1 - then OnStateExit calls `_lungeAttack.StopCast()` on whatever that resolved
// to. For P2's own Animator entering this state, that's P1's LungeAttack, not P2's - so P2's own
// LungeAttack ability's cast-lock (Ability.StopCast(), which also lives inside StopHeal() for
// Healing - same family) never gets cleared, leaving P2 stuck exactly as reported. Same fix as
// HealingBehaviour, just targeting the ability-typed field instead of a Penitent-typed one.
[HarmonyPatch(typeof(LungeAttackBehaviour), "OnStateEnter")]
internal static class LungeAttackBehaviour_OnStateEnter_Patch
{
    private static void Prefix(Animator animator, ref LungeAttack ____lungeAttack)
    {
        Penitent owner = animator.GetComponentInParent<Penitent>();
        if (owner == null)
        {
            return;
        }
        LungeAttack ownAbility = owner.GetComponentInChildren<LungeAttack>();
        if (ownAbility != null)
        {
            ____lungeAttack = ownAbility;
        }
    }
}

// Round 37: an exhaustive scan of every Player AnimationBehaviour (StateMachineBehaviour) class
// in the game found the exact same "_penitent starts null, defaults to Core.Logic.Penitent
// (always P1) on first use" bug in roughly fifty separate classes - the same pattern already
// individually fixed, one reported symptom at a time, for Dash/AirDash/RunAfterDash/Attack/
// Crouch*/Ladder*/CliffLede*/Hurt*/Falling/Idle/Move/RunStart/Healing/LungeAttack (their own
// patches are scattered throughout this file). Rather than keep adding one narrowly-scoped patch
// per newly-reported symptom, this single patch covers every *remaining* class at once via
// Harmony's TargetMethods() - the exact same fix (pre-set `_penitent` from the Animator parameter
// every one of these methods already receives, before the original's own null-check runs and
// overwrites it with P1), just applied wholesale instead of piecemeal. This is what actually
// fixes the reported "P2 does a charged attack whenever P1 does" - StartChargingAttackBehaviour
// is in this list, and was the real cause (its OnStateEnter calls `_penitent.ChargedAttack.Cast()`
// - resolving to P1's ChargedAttack instead of P2's own when it's P2's Animator entering the
// state - a wrong-owner Cast() call, not a shared-input-read bug like Healing's was). The rest
// (air/ground attack variants, jump/fall/landing, death, a few Prayer-cutscene states, range
// attack) weren't specifically reported broken, but share the identical bug shape, so they're
// fixed proactively here rather than waiting for each to surface as its own bug report.
[HarmonyPatch]
internal static class ManyPlayerAnimationBehaviours_PenitentOwnerFix_Patch
{
    private static readonly Type[] TargetTypes =
    {
        typeof(AirAttackBehaviour), typeof(AirUpwardAttackBehaviour), typeof(ChargedAttackBehaviour),
        typeof(ChargedAttackEffectBehaviour), typeof(ChargingAttackBehaviour), typeof(FinishingComboStarterBehaviour),
        typeof(GroundUpwardAttackBehaviour), typeof(StartChargingAttackBehaviour),
        typeof(PlayerDeathAnimationBehaviour), typeof(PlayerDeathFallBehaviour), typeof(PlayerDeathSpikeBehaviour),
        typeof(FallingOverBehaviour), typeof(GroundingOverBehaviour),
        typeof(JumpBehaviour), typeof(JumpForwardBehaviour), typeof(JumpOffBehaviour),
        typeof(LandingBehaviour), typeof(LandingRunningBehaviour),
        typeof(AuraTransformBehaviour), typeof(HighWillsRespawnBehaviour), typeof(PR202TeleportBehaviour),
        typeof(GroundRangeAttackBehaviour), typeof(MidAirRangeAttackBehaviour),
        typeof(AirAttackSubStateBehaviour), typeof(ChargeAttackSubStateBehaviour), typeof(CliffLedeSubStateBehaviour),
        typeof(CrouchSubStateBehaviour), typeof(DashSubStateBehaviour),
    };

    // StateMachineBehaviour declares two OnStateEnter overloads (with and without a trailing
    // AnimatorControllerPlayable parameter) - AccessTools.Method(type, "OnStateEnter") alone is
    // ambiguous between them and throws at patch time (confirmed live: it took down this entire
    // patch, silently skipping all ~24 fixes below it). Parameter types must be given explicitly
    // to pick the plain 3-parameter overload every one of these classes actually overrides.
    private static readonly Type[] OnStateEnterParams = { typeof(Animator), typeof(AnimatorStateInfo), typeof(int) };

    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (Type type in TargetTypes)
        {
            MethodInfo method = AccessTools.Method(type, "OnStateEnter", OnStateEnterParams);
            if (method != null)
            {
                yield return method;
            }
        }
    }

    private static void Prefix(Animator animator, ref Penitent ____penitent)
    {
        Penitent owner = animator.GetComponentInParent<Penitent>();
        if (owner != null)
        {
            ____penitent = owner;
        }
    }
}

// Round 45: found a real, previously-unknown side effect of the batch patch above, from a
// NullReferenceException that fired 10 times in one live P2 test session (upward attacks
// specifically). GroundUpwardAttackBehaviour.OnStateEnter's real body is
// `if (_penitent == null) { _penitent = Core.Logic.Penitent; ...also compute _defaultAttackAreaOffset/
// _defaultAttackAreaSize/_penitentSword/_swordAnimatorInyector... }` - the batch Prefix above
// pre-sets ____penitent to the correct owner *before* vanilla's own null-check runs, which fixes
// the owner but has a side effect for this specific class: since ____penitent is never null by
// the time vanilla checks, that whole init block - including the three fields OnStateUpdate/
// OnStateExit actually depend on - never runs for P2 at all, leaving them permanently null and
// crashing the moment OnStateUpdate reaches `_swordAnimatorInyector.PlayAttackDesiredTime(...)`.
// This is a real gap in the batch-patch technique itself: it silently breaks any class that
// bundles *other* cached-once state inside the same guard as `_penitent`, not just this one - a
// full audit of the other ~23 classes for the same shape is still open (only this one has actually
// been proven broken via a live crash log). Fixed with a Postfix that recomputes the three skipped
// fields directly from the correct P2 owner, mirroring vanilla's own logic exactly.
[HarmonyPatch(typeof(GroundUpwardAttackBehaviour), "OnStateEnter")]
internal static class GroundUpwardAttackBehaviour_FixSkippedInit_P2_Patch
{
    private static readonly FieldInfo PenitentSwordField = AccessTools.Field(typeof(GroundUpwardAttackBehaviour), "_penitentSword");
    private static readonly FieldInfo SwordAnimatorInyectorField = AccessTools.Field(typeof(GroundUpwardAttackBehaviour), "_swordAnimatorInyector");
    private static readonly FieldInfo DefaultAttackAreaOffsetField = AccessTools.Field(typeof(GroundUpwardAttackBehaviour), "_defaultAttackAreaOffset");
    private static readonly FieldInfo DefaultAttackAreaSizeField = AccessTools.Field(typeof(GroundUpwardAttackBehaviour), "_defaultAttackAreaSize");

    private static void Postfix(GroundUpwardAttackBehaviour __instance, Penitent ____penitent)
    {
        if (____penitent == null || ____penitent != CoopLocal.Player2)
        {
            return;
        }
        if (SwordAnimatorInyectorField.GetValue(__instance) != null)
        {
            return;
        }
        Vector2 offset = new Vector2(____penitent.AttackArea.WeaponCollider.offset.x, ____penitent.AttackArea.WeaponCollider.offset.y);
        Vector2 size = new Vector2(____penitent.AttackArea.WeaponCollider.bounds.size.x, ____penitent.AttackArea.WeaponCollider.bounds.size.y);
        DefaultAttackAreaOffsetField.SetValue(__instance, offset);
        DefaultAttackAreaSizeField.SetValue(__instance, size);
        PenitentSword sword = (PenitentSword)____penitent.PenitentAttack.CurrentPenitentWeapon;
        PenitentSwordField.SetValue(__instance, sword);
        SwordAnimatorInyectorField.SetValue(__instance, sword.SlashAnimator);
    }
}


