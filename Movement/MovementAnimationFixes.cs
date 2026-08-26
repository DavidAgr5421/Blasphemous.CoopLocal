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


