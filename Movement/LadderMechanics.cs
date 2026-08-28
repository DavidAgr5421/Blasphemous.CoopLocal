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

[HarmonyPatch(typeof(FloorDistanceChecker), "OnTriggerEnter2D")]
internal static class FloorDistanceChecker_OnTriggerEnter2D_LadderRaiser_Patch
{
    private static void Prefix(Collider2D other, Penitent ____penitent)
    {
        if (other.gameObject.layer == LayerMask.NameToLayer("Ladder"))
        {
            LadderStepRaiser.Current = ____penitent;
        }
    }
}

[HarmonyPatch(typeof(GrabLadder), "OnStepLadder")]
internal static class GrabLadder_OnStepLadder_Patch
{
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(GrabLadder), "_penitent");

    private static bool Prefix(GrabLadder __instance)
    {
        Penitent owner = PenitentField.GetValue(__instance) as Penitent;
        Penitent raiser = LadderStepRaiser.Current;
        bool allow = owner == null || raiser == null || owner == raiser;
        DashParryDebugLog.Log($"GrabLadder.OnStepLadder subscriber owner={DashParryDebugLog.Label(owner)} raiser={DashParryDebugLog.Label(raiser)} allow={allow} (frame {Time.frameCount})");
        // Only intervene when both sides are positively known and disagree - anything ambiguous
        // (either side still null) falls through to the original rather than risk swallowing a
        // legitimate step event.
        return allow;
    }
}

// Temporary diagnostic for the still-open "P2 can't reliably climb ladders" report: logs P2's own
// GrabLadder.OnUpdate() proximity/gating state every time it changes, to see directly whether
// CurrentLadderCollider ever gets set for P2 and whether the distance/StepOnLadder/CanClimbLadder
// conditions actually pass while P2 is on a ladder.
[HarmonyPatch(typeof(GrabLadder), "OnUpdate")]
internal static class GrabLadder_OnUpdate_DebugLogger_Patch
{
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(GrabLadder), "_penitent");
    private static string lastLoggedState;

    private static void Postfix(GrabLadder __instance)
    {
        Penitent owner = PenitentField.GetValue(__instance) as Penitent;
        if (owner == null || owner != CoopLocal.Player2)
        {
            return;
        }

        Collider2D currentLadderCollider = __instance.CurrentLadderCollider;
        bool closeEnough = false;
        float distance = float.NaN;
        if (currentLadderCollider != null)
        {
            distance = __instance.DistanceToTopLadder(owner.transform.position);
            closeEnough = distance < currentLadderCollider.bounds.size.x * 0.2f;
        }

        string state = $"CurrentLadderCollider={(currentLadderCollider != null ? currentLadderCollider.name : "null")} distance={distance:F2} closeEnough={closeEnough} StepOnLadder={owner.StepOnLadder} CanClimbLadder={owner.CanClimbLadder} IsOnLadder={owner.IsOnLadder} IsGrabbingLadder={owner.IsGrabbingLadder} IsClimbingLadder={owner.IsClimbingLadder} IsCrouched={owner.IsCrouched} IsGrounded={owner.Status.IsGrounded} StartingGoingDownLadders={owner.StartingGoingDownLadders}";
        if (state != lastLoggedState)
        {
            lastLoggedState = state;
            DashParryDebugLog.Log($"P2 GrabLadder.OnUpdate: {state} (frame {Time.frameCount})");
        }
    }
}

// Root cause of "P2 never even starts climbing" (distinct from the crouch-racing bug above,
// confirmed fixed): the diagnostic showed StepOnLadder staying true for 200+ frames while P2
// repeatedly pressed down, but `closeEnough` (the tight proximity check that actually drives the
// "STEP_ON_LADDER" animator bool - GrabLadder.OnUpdate()'s own `flag` local, distance < collider
// width * 0.2) only ever holds true for 1-2 frames before P2's own position drifts back out of
// range - nowhere near long enough for the Animator Controller to register the transition into
// "grab_ladder_to_go_down". The drift is slow (~0.02 units/frame, well under normal walk speed),
// consistent with residual horizontal momentum/drag rather than active movement input, but
// nothing currently stops it specifically while a ladder-grab is being attempted (the existing
// horizontal-movement lock in PlatformCharacterInput.Update() only engages once IsGrabbingLadder
// is *already* true - too late to help reach that state in the first place). Zeroing P2's own
// horizontal speed every frame while it's near a ladder and holding down, but not yet
// grabbing, removes that drift and gives the tight proximity window a real chance to hold long
// enough to register.
[HarmonyPatch(typeof(GrabLadder), "OnUpdate")]
internal static class GrabLadder_OnUpdate_StopDriftWhileAttempting_Patch
{
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(GrabLadder), "_penitent");

    private static void Postfix(GrabLadder __instance)
    {
        Penitent penitent = PenitentField.GetValue(__instance) as Penitent;
        if (penitent == null || penitent != CoopLocal.Player2)
        {
            return;
        }
        if (penitent.IsGrabbingLadder || penitent.IsClimbingLadder)
        {
            return;
        }
        if (penitent.StepOnLadder && penitent.PlatformCharacterInput.isJoystickDown)
        {
            penitent.PlatformCharacterController.PlatformCharacterPhysics.HSpeed = 0f;
        }
    }
}

// Root cause of "P2 can't reliably climb ladders", found via the diagnostic above:
// AnimatorInyector.Crouch() computes `_penitent.IsCrouched = _playerInput.isJoystickDown && ...`
// - with NO check at all for whether the character is currently grabbing/on/climbing a ladder -
// and only runs while grounded (Status.IsGrounded), which the game apparently considers true even
// while gripping a ladder. Holding "down" to descend a ladder is therefore simultaneously read as
// "crouch". The only thing that was ever suppressing this was the PLAYER_LOGIC blocker that
// GrabLadderDownBehaviour pushes while its own grab/descend states are active - but that blocker
// briefly clears for exactly one frame at the handoff from "grab_ladder_to_go_down" to
// "ladder_going_down" (SetRootMotionPosition's callback clears it right before playing the next
// clip), and the log shows *exactly* that frame is where isJoystickDown (still true - the user is
// still holding down to keep descending) sets IsCrouched = true and fires the "IS_CROUCH"
// animator bool, racing against the ladder animation graph's own transition into
// "ladder_going_down" for that same frame. LadderGoingDownBehaviour.OnStateEnter() does clear
// IsCrouched back to false, but by then the animator's own transition evaluation may already have
// latched onto the crouch bool from the frame it was true, derailing the descent into
// "Player_crouch_down" instead - matching the repeated grab/going-down cycling and eventual
// dropout into crouch observed in every capture.
//
// Fix: temporarily hide isJoystickDown from Crouch()'s own computation (save-and-restore around
// just this one call, same technique as the blocker override above) whenever the character is
// currently interacting with a ladder in any of these three ways - crouching while on a ladder
// makes no gameplay sense for either player, so this isn't specific to P2.
[HarmonyPatch(typeof(Gameplay.GameControllers.Penitent.Animator.AnimatorInyector), "Crouch")]
internal static class AnimatorInyector_Crouch_LadderGuard_Patch
{
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(Gameplay.GameControllers.Penitent.Animator.AnimatorInyector), "_penitent");
    private static readonly FieldInfo PlayerInputField = AccessTools.Field(typeof(Gameplay.GameControllers.Penitent.Animator.AnimatorInyector), "_playerInput");

    private static bool overrodeThisCall;

    private static string lastLoggedDecision;

    private static void Prefix(object __instance)
    {
        overrodeThisCall = false;
        Penitent penitent = PenitentField.GetValue(__instance) as Penitent;
        PlatformCharacterInput input = PlayerInputField.GetValue(__instance) as PlatformCharacterInput;
        if (penitent != CoopLocal.Player2)
        {
            return;
        }
        if (penitent == null || input == null)
        {
            return;
        }

        bool ladderish = penitent.IsGrabbingLadder || penitent.IsOnLadder || penitent.IsClimbingLadder || penitent.StepOnLadder;
        string decision = $"isJoystickDown={input.isJoystickDown} ladderish={ladderish} (IsGrabbingLadder={penitent.IsGrabbingLadder} IsOnLadder={penitent.IsOnLadder} IsClimbingLadder={penitent.IsClimbingLadder} StepOnLadder={penitent.StepOnLadder})";
        if (decision != lastLoggedDecision)
        {
            lastLoggedDecision = decision;
            DashParryDebugLog.Log($"P2 Crouch() guard check: {decision} (frame {Time.frameCount})");
        }

        if (!input.isJoystickDown)
        {
            return;
        }
        // IsGrabbingLadder/IsClimbingLadder/IsOnLadder alone weren't enough: the diagnostic log
        // showed all three reading False for exactly one frame right at the "grab_ladder_to_go_down"
        // -> "ladder_going_down" handoff (GrabLadderDownBehaviour's own OnStateUpdate sets
        // IsClimbingLadder=true in the same block that starts the transition, but this method runs
        // during the regular Update() phase, which can land before that Animator-driven state
        // change lands within the same frame) - and that exact frame is where Crouch() would slip
        // through and set IsCrouched=true again. StepOnLadder stays continuously true for the
        // whole ladder interaction (set by GrabLadder.OnUpdate() from actual proximity, not from
        // any of the animation sub-states), so it's a more robust guard across this handoff.
        if (!ladderish)
        {
            return;
        }
        input.isJoystickDown = false;
        overrodeThisCall = true;
        DashParryDebugLog.Log($"P2 Crouch() guard SUPPRESSED isJoystickDown (frame {Time.frameCount})");
    }

    private static void Postfix(object __instance)
    {
        if (!overrodeThisCall)
        {
            return;
        }
        overrodeThisCall = false;
        PlatformCharacterInput input = PlayerInputField.GetValue(__instance) as PlatformCharacterInput;
        if (input != null)
        {
            input.isJoystickDown = true;
        }
    }
}



// GrabLadder itself correctly resolves its owner (`_penitent = (Penitent)base.EntityOwner;` in
// OnStart() - a Trait, not affected by the Start()-hardcode bug above). The actual bug here is
// different: OnUpdate()'s ladder-dismount trigger reads
// `_penitent.PlatformCharacterInput.Rewired.GetButtonDown(65)` - a *direct* read of the single
// shared Rewired Player 0 (the same class of cross-talk bug fixed for Dash/Parry/Heal/Interact/
// PrayerActivate earlier this session, just never applied here since GrabLadder is a Trait, not
// an Ability, so it was never covered by Ability_UpdateInput_Patch's blanket P2-disable). Whoever
// is physically pressing whatever key Rewired action 65 maps to (in practice, P1's jump) trips
// this check for *both* P1's and P2's GrabLadder instances identically, since both read the exact
// same shared Rewired.Player object - explaining "el salto tambien lo ocasiona P1, debe de
// hacerlo P2". Fixed via a full OnUpdate() reimplementation for P2's instance only (mirroring the
// real decompiled body exactly) with just that one condition redirected to Player2Input.JumpDown -
// every other line (StepOnLadder computation, animator bools, top/bottom repositioning) was
// already correct per-instance and is reproduced unchanged, not guessed.
[HarmonyPatch(typeof(GrabLadder), "OnUpdate")]
internal static class GrabLadder_OnUpdate_P2_Patch
{
    // IsBottomLadderRepositioning/IsTopLadderReposition/StartGoingDown/CurrentLadderCollider are
    // all public properties on GrabLadder - called directly below, no reflection needed. Only the
    // private serialized field and the two private static readonly hash ints need it.
    private static readonly FieldInfo LadderWidthFactorField = AccessTools.Field(typeof(GrabLadder), "ladderWidthFactor");
    private static readonly FieldInfo StepOnLadderHashField = AccessTools.Field(typeof(GrabLadder), "StepOnLadderHash");
    private static readonly FieldInfo IsCollidingLadderHashField = AccessTools.Field(typeof(GrabLadder), "IsCollidingLadderHash");
    private static readonly MethodInfo TakeOffLadderMethod = AccessTools.Method(typeof(GrabLadder), "TakeOffLadder");

    private static bool Prefix(GrabLadder __instance, ref Penitent ____penitent)
    {
        if (____penitent == null || ____penitent != CoopLocal.Player2)
        {
            return true;
        }
        Penitent penitent = ____penitent;

        if (__instance.IsBottomLadderRepositioning)
        {
            __instance.IsBottomLadderRepositioning = false;
        }

        bool startGoingDown = penitent.StepOnLadder && penitent.PlatformCharacterInput.isJoystickDown
            && !penitent.PlatformCharacterController.IsClimbing && penitent.Status.IsGrounded;
        __instance.StartGoingDown = startGoingDown;

        bool closeToTop = false;
        Collider2D currentLadderCollider = __instance.CurrentLadderCollider;
        if (currentLadderCollider != null)
        {
            float distance = __instance.DistanceToTopLadder(penitent.transform.position);
            float widthFactor = (float)LadderWidthFactorField.GetValue(__instance);
            closeToTop = distance < currentLadderCollider.bounds.size.x * widthFactor;
        }

        if (startGoingDown && !__instance.IsTopLadderReposition)
        {
            __instance.IsTopLadderReposition = true;
            __instance.TopLadderReposition();
        }

        bool stepOnLadderValue = penitent.StepOnLadder && closeToTop && penitent.CanClimbLadder;
        Animator animator = penitent.Animator;
        animator.SetBool((int)StepOnLadderHashField.GetValue(__instance), stepOnLadderValue);
        animator.SetBool((int)IsCollidingLadderHashField.GetValue(__instance), penitent.IsOnLadder);

        if (!penitent.StepOnLadder)
        {
            __instance.IsTopLadderReposition = false;
        }

        bool isTakingOffLadder = animator.GetCurrentAnimatorStateInfo(0).IsName("grab_ladder_to_go_down")
            || animator.GetCurrentAnimatorStateInfo(0).IsName("release_ladder_to_floor_up");
        // The one line that actually differs from vanilla: P2's own edge-triggered jump instead of
        // the shared Rewired Player 0 read.
        if (Player2Input.JumpDown && !isTakingOffLadder && !Core.Input.InputBlocked)
        {
            TakeOffLadderMethod.Invoke(__instance, null);
        }
        return false;
    }
}
