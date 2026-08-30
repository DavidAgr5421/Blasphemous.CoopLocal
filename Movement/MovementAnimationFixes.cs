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
using System.Reflection.Emit;
using UnityEngine;
using UnityEngine.UI;

namespace Blasphemous.CoopLocal;

// Manually finding and fixing the _penitent-falls-back-to-P1 bug one class at a time (every
// patch above targeting an OnStateEnter with a Prefix that does
// `animator.GetComponentInParent<Penitent>()` is this exact fix) kept turning up new instances
// every time a new symptom got reported - most notably AttackBehaviour (the state entered on a
// standing attack), which turned out to be the actual cause of "P2 can't attack while P1 is
// crouched, the attack button crouches instead": AttackBehaviour.OnStateUpdate does
// `if (_penitent.Status.IsGrounded && _penitent.PlatformCharacterInput.isJoystickDown && ...)
// animator.Play(_crouchDownAnim);` - on P2's own unfixed instance, `_penitent` resolves to P1,
// so it reads *P1's* isJoystickDown (true while P1 holds down/crouch) and forces *P2's own*
// Animator into "Crouch Down" mid-attack.
//
// A generic scanner (patch every StateMachineBehaviour with a `_penitent` field, Prefixing
// OnStateEnter to set it to the real owner) was tried here and reverted - it actively broke
// things instead of just being redundant. Several of these classes bundle a SECOND one-time
// initialization inside the exact same `if (_penitent == null) { ... }` guard - e.g.
// AttackBehaviour also does `_penitentAttackArea = _penitent.PenitentAttack.CurrentPenitentWeapon
// .AttackAreas[0];` right there, and HurtSubStateBehaviour does
// `_throwBack = _penitent.GetComponentInChildren<ThrowBack>();`. A blanket Prefix that always
// (re)sets `_penitent` before the original runs makes the original's OWN null-check permanently
// see "already set" - so that second field NEVER gets initialized at all, not even wrong -
// producing a NullReferenceException the very first time that state is entered (confirmed live
// in BepInEx/LogOutput.log for both AttackBehaviour.OnStateUpdate and
// HurtSubStateBehaviour.OnStateEnter after enabling the generic scanner). An uncaught exception
// thrown out of a StateMachineBehaviour callback is a plausible explanation for several of the
// harder-to-pin-down symptoms reported afterwards (P2's dash occasionally leaving P1 or P2 stuck)
// - if the exception happens between a lock being pushed and popped, whatever
// PlayerLogicBlocker.SetBlocked(...)/Core.Input.SetBlocker(...) call was supposed to run right
// after never does.
//
// So: back to manual, one class at a time, but each one now checked first for a bundled
// second field before writing the patch. IdleAnimatonBehaviour, MoveAnimationBehaviour and
// RunStartBehaviour (below) don't have this hazard - any extra state they cache
// (_startChargingAttackBehaviour, _stepDustSpawner) has its own separate, independent null-check,
// so presetting _penitent first is safe for them. AttackBehaviour and HurtSubStateBehaviour do
// have the hazard and get a different-shaped fix (further down) that replicates the bundled
// initialization itself instead of just presetting the field.
[HarmonyPatch(typeof(IdleAnimatonBehaviour), "OnStateEnter")]
internal static class IdleAnimatonBehaviour_OnStateEnter_Patch
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

[HarmonyPatch(typeof(MoveAnimationBehaviour), "OnStateEnter")]
internal static class MoveAnimationBehaviour_OnStateEnter_Patch
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

[HarmonyPatch(typeof(RunStartBehaviour), "OnStateEnter")]
internal static class RunStartBehaviour_OnStateEnter_Patch
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

// The user reported P2 can't reliably go up/down ladders, and "only near P1" it seems to work at
// all - "pero se maneja entonces los dos con teclas de P1" is the exact signature of the usual
// _penitent-falls-back-to-P1 bug: every StateMachineBehaviour in the ladder state graph
// (GrabLadder/GrabLadderDown/LadderGoingUp/LadderGoingDown/LadderSliding/ReleaseTopLadder/
// ReleaseBottomLadder/LadderClimbingSubState) has "if (_penitent == null) _penitent =
// Core.Logic.Penitent;" in OnStateEnter, same as every other case already fixed in this file.
// P2's own clone of each of these hits that null check once on first ladder use and locks onto
// P1 forever after - and since LadderGoingUp/DownBehaviour's OnStateUpdate reads
// _penitent.PlatformCharacterInput.FVerAxis *every frame* to decide the climb animation, P2's own
// ladder climb ends up literally being driven by P1's up/down input from then on, which matches
// "se maneja con teclas de P1" precisely. These were flagged as unaudited back when the generic
// scanner regression was fixed (round 2) - this is that audit.
[HarmonyPatch(typeof(GrabLadderBehaviour), "OnStateEnter")]
internal static class GrabLadderBehaviour_OnStateEnter_Patch
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

[HarmonyPatch(typeof(LadderSlidingBehaviour), "OnStateEnter")]
internal static class LadderSlidingBehaviour_OnStateEnter_Patch
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

[HarmonyPatch(typeof(ReleaseBottomLadderBehaviour), "OnStateEnter")]
internal static class ReleaseBottomLadderBehaviour_OnStateEnter_Patch
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

[HarmonyPatch(typeof(ReleaseTopLadderBehaviour), "OnStateEnter")]
internal static class ReleaseTopLadderBehaviour_OnStateEnter_Patch
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

[HarmonyPatch(typeof(LadderClimbingSubStateBehaviour), "OnStateEnter")]
internal static class LadderClimbingSubStateBehaviour_OnStateEnter_Patch
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

// GrabLadderDownBehaviour bundles `_rootMotionDriver = _penitent.GetComponentInChildren
// <RootMotionDriver>();` inside the same _penitent guard - same hazard as AttackBehaviour further
// down, so this needs the reflection-based "only assign once, replicate both fields" fix instead
// of a plain ref-Penitent Prefix.
[HarmonyPatch(typeof(GrabLadderDownBehaviour), "OnStateEnter")]
internal static class GrabLadderDownBehaviour_OnStateEnter_Patch
{
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(GrabLadderDownBehaviour), "_penitent");
    private static readonly FieldInfo RootMotionDriverField = AccessTools.Field(typeof(GrabLadderDownBehaviour), "_rootMotionDriver");

    private static void Prefix(GrabLadderDownBehaviour __instance, Animator animator)
    {
        if (PenitentField.GetValue(__instance) != null)
        {
            return;
        }

        Penitent owner = animator.GetComponentInParent<Penitent>();
        if (owner == null)
        {
            return;
        }

        PenitentField.SetValue(__instance, owner);
        RootMotionDriverField.SetValue(__instance, owner.GetComponentInChildren<RootMotionDriver>());
    }
}

// GrabLadderDownBehaviour.OnStateEnter/OnStateExit call the same global
// Core.Input.SetBlocker("PLAYER_LOGIC", ...) as Dash/Parry (see PlayerLogicBlocker above) to freeze
// movement during the ladder-grab animation - but unlike Dash/Parry, this lock was never
// registered with PlayerLogicBlocker. That was harmless as long as nothing actually consulted
// PlayerLogicBlocker for real gating (the getter patch turned out to never affect
// PlatformCharacterInput.Update()'s own internal read - see PlatformCharacterInput_Update_BlockerOverride_Patch
// above), but that new patch *does* directly mutate the real underlying blocker for the duration
// of each Update() call - and it can only tell "this instance's own lock" from "the other
// player's lock" via PlayerLogicBlocker's registry. Without this, P2 grabbing a ladder would have
// its own genuine PLAYER_LOGIC lock misread as "belongs to the other player" and incorrectly
// cleared, letting P2 keep sliding sideways off the ladder's center during what should be a
// locked grab animation - very plausibly the actual cause of the repeated
// grab-ladder-to-go-down/ladder-going-down cycling reported after that fix. Registering this
// lock the same way Dash/Parry already are closes that gap for this specific class; any other
// still-unaudited PLAYER_LOGIC user (WallJump, GuardSlide, hurt states, jump-off, combo
// finishers - see the comment on PlayerLogicBlocker itself) remains a latent instance of the same
// risk until reported and fixed the same way.
[HarmonyPatch(typeof(GrabLadderDownBehaviour), "OnStateEnter")]
internal static class GrabLadderDownBehaviour_BlockerTracking_OnStateEnter_Patch
{
    private static void Postfix(Animator animator)
    {
        Penitent owner = animator.GetComponentInParent<Penitent>();
        PlayerLogicBlocker.SetBlocked(owner, true);
    }
}

[HarmonyPatch(typeof(GrabLadderDownBehaviour), "OnStateExit")]
internal static class GrabLadderDownBehaviour_BlockerTracking_OnStateExit_Patch
{
    private static void Postfix(Animator animator)
    {
        Penitent owner = animator.GetComponentInParent<Penitent>();
        PlayerLogicBlocker.SetBlocked(owner, false);
    }
}

// LadderGoingDownBehaviour and LadderGoingUpBehaviour both bundle
// `_animatorInyector = _penitent.GetComponentInChildren<AnimatorInyector>();` inside the same
// guard - same treatment.
[HarmonyPatch(typeof(LadderGoingDownBehaviour), "OnStateEnter")]
internal static class LadderGoingDownBehaviour_OnStateEnter_Patch
{
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(LadderGoingDownBehaviour), "_penitent");
    private static readonly FieldInfo AnimatorInyectorField = AccessTools.Field(typeof(LadderGoingDownBehaviour), "_animatorInyector");

    private static void Prefix(LadderGoingDownBehaviour __instance, Animator animator)
    {
        if (PenitentField.GetValue(__instance) != null)
        {
            return;
        }

        Penitent owner = animator.GetComponentInParent<Penitent>();
        if (owner == null)
        {
            return;
        }

        PenitentField.SetValue(__instance, owner);
        AnimatorInyectorField.SetValue(__instance, owner.GetComponentInChildren<Gameplay.GameControllers.Penitent.Animator.AnimatorInyector>());
    }
}

[HarmonyPatch(typeof(LadderGoingUpBehaviour), "OnStateEnter")]
internal static class LadderGoingUpBehaviour_OnStateEnter_Patch
{
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(LadderGoingUpBehaviour), "_penitent");
    private static readonly FieldInfo AnimatorInyectorField = AccessTools.Field(typeof(LadderGoingUpBehaviour), "_animatorInyector");

    private static void Prefix(LadderGoingUpBehaviour __instance, Animator animator)
    {
        if (PenitentField.GetValue(__instance) != null)
        {
            return;
        }

        Penitent owner = animator.GetComponentInParent<Penitent>();
        if (owner == null)
        {
            return;
        }

        PenitentField.SetValue(__instance, owner);
        AnimatorInyectorField.SetValue(__instance, owner.GetComponentInChildren<Gameplay.GameControllers.Penitent.Animator.AnimatorInyector>());
    }
}

// GrabLadder.OnStart() subscribes this *instance's* OnStepLadder method to
// FloorDistanceChecker.OnStepLadder - a *static* event shared by the whole game, not per-Penitent.
// Both P1's and P2's GrabLadder instances subscribe to the same static event, so whenever either
// player's own FloorDistanceChecker fires it (from its own, correctly self-resolved OnTriggerEnter2D
// - see FloorDistanceChecker._penitent, already confirmed fine), *both* instances' OnStepLadder
// runs and both end up with CurrentLadderCollider pointing at whichever ladder was actually
// stepped on - even the one who never went near it. CurrentLadderCollider then feeds directly into
// TopLadderReposition() (snaps the player's X position to the ladder's center) and the "close
// enough to climb" distance check in GrabLadder.OnUpdate(), so this cross-talk can silently
// reposition/gate the wrong player's ladder interaction based on the other one's movements.
// The event's own payload (the ladder's Collider2D) doesn't say who stepped on it, so the actual
// raiser has to be captured at the source: Prefixing FloorDistanceChecker.OnTriggerEnter2D stashes
// which Penitent is *about* to raise OnStepLadder (read from that instance's own already-correct
// _penitent) into LadderStepRaiser.Current right before the original body runs and fires the
// static event - then each GrabLadder subscriber can compare that against its own _penitent and
// ignore the callback if it wasn't really meant for it.
internal static class LadderStepRaiser
{
    internal static Penitent Current;
}

// Round 66 - FallingForwardBehaviour (Gameplay.GameControllers.AnimationBehaviours.Player.Jump),
// the animator state entered while falling forward off a ledge. Never audited before. Found from a
// real playtest log, not a bug report: BepInEx/LogOutput.log showed hundreds of consecutive
// NullReferenceExceptions ("GetRayCastOrigin -> IsSideBlocked -> OnStateUpdate") spamming every
// frame for the entire duration of TWO separate room transitions, stopping exactly at "Spawning
// enemies on level" each time.
//
// Root cause, confirmed against the decompiled source (not the same shape as the usual family-1
// lazy-fallback, though OnStateEnter has that bug too - see below):
//   private Vector2 GetRayCastOrigin(float heightOffset = 0f)
//   {
//       Vector3 position = Core.Logic.Penitent.transform.position;   // <-- flat P1 hardcode
//       return new Vector2(position.x, position.y + heightOffset);
//   }
// This ignores the class's own (per-instance) _penitent field entirely and reads the global P1
// singleton directly - the flat-hardcode shape already catalogued for Parry.StartParry/
// Penitent.Damage, not the "if (_penitent == null)" shape. OnStateUpdate's slope-check raycast has
// the identical hardcode one line further down (`Physics2D.Raycast(Core.Logic.Penitent.transform
// .position, Vector2.down, ...)`).
//
// Confirmed why this only ever throws for P2's clone, never P1's: PenitentSpawnPoint.Instance()
// (Gameplay.GameControllers.Penitent.Gizmos.PenitentSpawnPoint) does a plain
// `Object.Instantiate(PenitentPrefab, ...)` with no DontDestroyOnLoad - P1's whole Penitent
// GameObject (Animator, every StateMachineBehaviour instance included) gets destroyed on every
// scene unload and freshly re-instantiated in the new scene, so nothing of P1's ever ticks during
// the load window itself. P2, on the other hand, has had `Object.DontDestroyOnLoad(Player2
// .gameObject)` since Round 55 specifically so its stats/position survive room transitions - which
// also means P2's own Animator and this exact StateMachineBehaviour instance keep receiving
// OnStateUpdate every frame through the *entire* load screen if P2 happened to be in
// "FallingForward" when the transition trigger fired. `Framework.Managers.LogicManager.Penitent`
// is a plain nullable auto-property (`public Penitent Penitent { get; set; }`) that only points at
// whichever Penitent GameObject currently exists in the active scene - during the gap between the
// old one being destroyed and the new one's Awake() re-registering it, `Core.Logic.Penitent` is
// genuinely null, and P2's still-running instance of this class hits it every frame until the new
// scene's own Penitent spawns. Pure coop-only bug: literally cannot happen in vanilla single-player
// since nothing survives the scene boundary to keep calling it.
//
// OnStateEnter also has the ordinary family-1 lazy-fallback AND a bundled second init in the exact
// same guard (`if (!_penitent) { _penitent = Core.Logic.Penitent; Dash dash = _penitent.Dash;
// dash.OnStartDash = Delegate.Combine(...); }`) - handled with the same "preset both fields
// ourselves, once, before vanilla's own check ever runs" pattern already used above for
// GrabLadderDownBehaviour/LadderGoingUpBehaviour/LadderGoingDownBehaviour, not a blanket
// ref-Penitent Prefix (would silently skip the Dash.OnStartDash subscription - the exact bundled-
// init trap documented at the top of NOTES.md). `_penitent` is spelled as an auto-property here
// (`private Penitent _penitent { get; set; }`), so the reflection target is the compiler-generated
// backing field, same trick as ParryRepostBehaviour/ParrySuccessBehaviour in Parry/Parry.cs.
[HarmonyPatch(typeof(FallingForwardBehaviour), "OnStateEnter")]
internal static class FallingForwardBehaviour_OnStateEnter_Patch
{
    private static readonly FieldInfo PenitentBackingField =
        AccessTools.Field(typeof(FallingForwardBehaviour), "<_penitent>k__BackingField");
    private static readonly MethodInfo OnStartDashMethod =
        AccessTools.Method(typeof(FallingForwardBehaviour), "OnStartDash");

    private static void Prefix(FallingForwardBehaviour __instance, Animator animator)
    {
        if (PenitentBackingField.GetValue(__instance) != null)
        {
            return;
        }

        Penitent owner = animator.GetComponentInParent<Penitent>();
        if (owner == null)
        {
            return;
        }

        PenitentBackingField.SetValue(__instance, owner);
        Dash dash = owner.Dash;
        Core.SimpleEvent handler = (Core.SimpleEvent)Delegate.CreateDelegate(typeof(Core.SimpleEvent), __instance, OnStartDashMethod);
        dash.OnStartDash = (Core.SimpleEvent)Delegate.Combine(dash.OnStartDash, handler);
    }
}

// GetRayCastOrigin has no Animator parameter to resolve an owner from directly - reads the
// (now-correct, thanks to the Prefix above) _penitent backing field instead of re-deriving it, and
// fully replaces the method body (private, no reason to leave the Core.Logic.Penitent hardcode
// reachable at all - fixes both the NRE and the wrong-raycast-origin-for-P2 bug in one patch).
[HarmonyPatch(typeof(FallingForwardBehaviour), "GetRayCastOrigin")]
internal static class FallingForwardBehaviour_GetRayCastOrigin_Patch
{
    private static readonly FieldInfo PenitentBackingField =
        AccessTools.Field(typeof(FallingForwardBehaviour), "<_penitent>k__BackingField");

    private static bool Prefix(FallingForwardBehaviour __instance, float heightOffset, ref Vector2 __result)
    {
        Penitent owner = (Penitent)PenitentBackingField.GetValue(__instance);
        if (owner == null)
        {
            return true; // nothing resolved yet - fall back to original (matches pre-fix behavior)
        }

        Vector3 position = owner.transform.position;
        __result = new Vector2(position.x, position.y + heightOffset);
        return false;
    }
}

// OnStateUpdate's slope-check raycast (`Physics2D.Raycast(Core.Logic.Penitent.transform.position,
// Vector2.down, 1.5f, RayCastLayerDetection)`) is the second and last direct Core.Logic.Penitent
// read in this class - everything else in OnStateUpdate already goes through _penitent correctly.
// Same single-call-site Transpiler pattern as VerticalAttack_OnUpdate_P2_TimedPress_Patch in
// Abilities/RangedAndVerticalAttackFixes.cs: a companion Prefix stashes which instance is currently
// running (safe - Unity's single-threaded per-frame Animator callbacks never interleave two
// FallingForwardBehaviour.OnStateUpdate calls at once), and the retargeted call substitutes that
// instance's own (correct) _penitent for the LogicManager.Penitent property read.
[HarmonyPatch(typeof(FallingForwardBehaviour), "OnStateUpdate")]
internal static class FallingForwardBehaviour_OnStateUpdate_Patch
{
    private static readonly FieldInfo PenitentBackingField =
        AccessTools.Field(typeof(FallingForwardBehaviour), "<_penitent>k__BackingField");
    private static readonly MethodInfo LogicManagerGetPenitentMethod =
        AccessTools.PropertyGetter(typeof(LogicManager), "Penitent");
    private static readonly MethodInfo ReplacementMethod =
        AccessTools.Method(typeof(FallingForwardBehaviour_OnStateUpdate_Patch), nameof(GetOwnerForSlopeRaycast));

    private static FallingForwardBehaviour currentInstance;

    private static void Prefix(FallingForwardBehaviour __instance)
    {
        currentInstance = __instance;
    }

    private static Penitent GetOwnerForSlopeRaycast(LogicManager logic)
    {
        FallingForwardBehaviour self = currentInstance;
        Penitent owner = self != null ? (Penitent)PenitentBackingField.GetValue(self) : null;
        return owner != null ? owner : logic.Penitent;
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        bool patched = false;
        foreach (CodeInstruction instruction in instructions)
        {
            if (!patched && (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                instruction.operand is MethodInfo method && method == LogicManagerGetPenitentMethod)
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = ReplacementMethod;
                patched = true;
            }
            yield return instruction;
        }
        if (!patched)
        {
            DashParryDebugLog.Log("[DashParryDebug] FallingForwardBehaviour.OnStateUpdate transpiler did NOT find LogicManager.get_Penitent() - P2 slope-raycast fix NOT applied!");
        }
    }
}


