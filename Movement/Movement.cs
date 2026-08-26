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

// FallingBehaviour (a StateMachineBehaviour on the Animator's "Falling" state) caches its
// target Penitent as Core.Logic.Penitent (P1) the first time it enters that state, instead
// of resolving the Penitent that actually owns the Animator it's attached to. Every Animator
// clone (including P2's) gets its own FallingBehaviour instance, so P2's copy ends up acting
// on P1 every frame while P2 is airborne - which throws (P1's own PlatformCharacterInput
// isn't always in a state CancelPlatformDropDown() expects) and spams the log.
// This patch forces _penitent to the Animator's actual owner before the original method runs.
[HarmonyPatch(typeof(FallingBehaviour), "OnStateEnter")]
internal static class FallingBehaviour_OnStateEnter_Patch
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

// CrouchDownBehaviour (StateMachineBehaviour on the "Crouch" state) has the exact same bug as
// FallingBehaviour, with much worse fallout. For P2's own instance, the wrongly-resolved
// _penitent means:
//   - OnStateEnter/OnStateExit set `_penitent.BeginCrouch = true/false` on *P1's* Penitent
//     every time P2 enters/exits Crouch - which is what blocked P1's own movement (the
//     original PlatformCharacterInput.Update() checks its own instance's BeginCrouch) whenever
//     P2 crouched, with no relation to P1's actual state.
//   - OnStateUpdate checks `_penitent.PlatformCharacterInput.Attack` (P1's Attack field, not
//     P2's own) before playing the "Crouch Attack" animation - on the correctly-passed
//     `animator` parameter (P2's own Animator). So P1 attacking made P2 play its crouch-attack,
//     while P2's own Attack press (from PlatformCharacterInput_Update_Patch) was never even
//     looked at here. This is the actual root cause of "P2 can't crouch-attack" from earlier
//     sessions - nothing to do with FVerAxis or animator-transition timing after all.
// Same fix as FallingBehaviour: resolve the real owner before the original method runs.
[HarmonyPatch(typeof(CrouchDownBehaviour), "OnStateEnter")]
internal static class CrouchDownBehaviour_OnStateEnter_Patch
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

// PlatformCharacterInput.Update() reads all input (movement, jump, attack, dash, crouch...)
// from Rewired Player 0, same as P1 - that's what makes P2 mirror P1's buttons instead of
// having its own. Rather than replace the original method (its ladder/cliff/attack-gating
// logic is too nuanced to safely reimplement), this patch lets it run as-is - using the
// shared input, so anything not explicitly overridden below still mirrors P1 for now (parry)
// - and overwrites, for P2 only, everything driven by P2's own gamepad (Player2Pad) instead:
//  - the movement/jump flags (position/physics), via PlatformCharacterController.SetActionState
//  - ReachAxisThreshold, a public field AnimatorInyector reads to decide the walk/run
//    animation and whether a jump plays "JUMP" or "FORWARD_JUMP"
//  - the Jump property (private-set auto-property; HarmonyX's "___" injection only resolves
//    plain fields, not auto-property backing fields, so this uses AccessTools.Field with the
//    compiler-generated "<Jump>k__BackingField" name instead) which AnimatorInyector also
//    checks before firing jump/attack animations
//  - sprite facing, via Penitent.SetOrientation (the same public method the original input
//    flow itself calls)
//  - isJoystickDown, a plain public field AnimatorInyector checks to set Penitent.IsCrouched
//  - Attack/Dash, plain public fields that fire the attack/dash animation pipelines
//  - FVerAxis (another private-set auto-property, same AccessTools.Field trick as Jump): the
//    animator's JOYSTICK_UP/JOYSTICK_DOWN bools (which is what actually selects the "attack
//    upward"/"crouch attack" animator states, not Penitent.IsCrouched) are computed from this,
//    and it was never being set for P2 at all - so those states could never trigger for P2
//    even though isJoystickDown (a separate, stricter threshold used only for the crouch pose
//    itself) was already correct.
[HarmonyPatch(typeof(PlatformCharacterInput), "Update")]
internal static class PlatformCharacterInput_Update_Patch
{
    private static readonly FieldInfo JumpBackingField =
        AccessTools.Field(typeof(PlatformCharacterInput), "<Jump>k__BackingField");

    private static readonly FieldInfo FVerAxisBackingField =
        AccessTools.Field(typeof(PlatformCharacterInput), "<FVerAxis>k__BackingField");

    // The original Update() also calls SetOrientation(horizontalAxis) using P1's shared
    // axis - harmless while P2 is actively pressing its own left/right (we override right
    // after), but when P2 is idle and P1 moves, that call still goes through unopposed and
    // flips P2's sprite. So P2's facing is tracked here and reasserted every frame, not just
    // while a direction key is held.
    private static EntityOrientation player2Facing = EntityOrientation.Right;

    // Debug only (see DashParryDebugLog): edge-triggered raw-hardware-key logger for exactly the
    // key P2's crouch reads. Animation-clip logging showed P2 dropping into "Player_crouch_down"
    // repeatedly, correlated with P1 dashing, with nothing else in this file able to explain it -
    // meaning either UnityEngine.Input.GetKey(KeyCode.DownArrow) really was true at that moment
    // (which would mean P1's own keyboard bindings also use the arrow keys - raw Input.GetKey
    // has no concept of "whose" key this is, so both P1's Rewired reads and this P2-only check
    // would react to the exact same physical key at once), or `blocked` is wrong somehow. Logs
    // both raw and blocked every time the resulting `crouch` flag flips, to tell those two apart
    // directly instead of guessing further.
    private static bool lastLoggedCrouch;
    private static bool lastLoggedJump;
    private static bool lastLoggedRawJumpKey;
    private static bool lastLoggedLeft;
    private static bool lastLoggedRight;

    // Diagnostic for the user's own finding: pressing P2's real crouch or jump button makes P1
    // stop dashing even while P1's own dash button stays physically held down. DashBehaviour
    // .OnStateUpdate's *vanilla* copy (the one that still runs unmodified for P1's own instance -
    // this mod only reimplements it for P2) cancels P1's own dash by reading
    // _penitent.PlatformCharacterInput.Rewired directly for jump/crouch/attack/axes, where
    // Rewired is always the shared "Player 0" - it reflects whatever physical keys/buttons are
    // actually held on the keyboard, with no concept of "whose" press it is. If any of P2's own
    // raw keys happen to *also* be keys Rewired has mapped for player 0 (P1's arrow-key movement
    // overlap was already confirmed the same way back in round 7-8, for a different symptom),
    // P2 pressing its own button would look, from Rewired's perspective, exactly like P1 pressing
    // it too - independently of any blocker/animation-sharing bug. This logs P1's own raw Rewired
    // jump button and vertical axis at the exact instant P2's own crouch/jump edge fires, to
    // confirm or rule this out directly instead of guessing at a shared-logic explanation.
    private static void LogP1RewiredCrossTalkCheck(string label, bool p2ActionNowTrue)
    {
        if (!p2ActionNowTrue)
        {
            return;
        }
        Penitent p1 = Core.Logic.Penitent;
        if (p1 == null || p1.PlatformCharacterInput.Rewired == null)
        {
            return;
        }
        Rewired.Player p1Rewired = p1.PlatformCharacterInput.Rewired;
        DashParryDebugLog.Log(
            $"P2 pressed its own {label} - P1's Rewired at that instant: GetButton(6) [jump]={p1Rewired.GetButton(6)}, " +
            $"GetAxisRaw(0) [horizontal]={p1Rewired.GetAxisRaw(0):F3}, GetAxisRaw(4) [vertical]={p1Rewired.GetAxisRaw(4):F3}, " +
            $"P2 mode={Player2Input.Mode}, P1's assigned joystick count={p1Rewired.controllers.joystickCount} " +
            $"(frame {Time.frameCount})");
    }

    private static void Postfix(Penitent ____penitent)
    {
        if (____penitent == null || ____penitent != CoopLocal.Player2)
        {
            return;
        }

        // The original method zeroes every raw input flag via ResetInputs() whenever Blocked is
        // true for that instance's own Update() call (dialog/menu/cutscene, or - per
        // PlatformCharacterInput_Blocked_Patch further down - this instance's own dash/parry
        // lock). This patch used to ignore that entirely and always read P2's raw keys, which is
        // exactly why P2 could still crouch and walk around freely while its own Parry was
        // active - something P1 can never do (parrying zeroes P1's own inputs the same way a
        // dialog box would). Gating the raw reads here reproduces ResetInputs()'s effect for
        // every signal this method sets: movement, jump, crouch/attack-up axis, and Attack/Dash.
        //
        // Also gates on ____penitent.Status.Dead (round 30 report: "P2 dies but keeps moving and
        // attacking, just stops taking damage"). In solo play, dying blocks input globally
        // (LogicStates.PlayerDead), which is exactly what this method's own Blocked/PlayerLogicBlocker
        // gate above was designed to bypass for P2 - so once P2 dies, nothing was left stopping this
        // Postfix from continuing to read P2's raw keys and drive its action states every frame.
        // Damage correctly stops on its own (PenitentDamageArea.OnUpdate disables the collider once
        // Status.Dead is true - untouched vanilla logic, per-instance already), which is why only
        // the "still moving/attacking" half was reported.
        bool blocked = PlayerLogicBlocker.IsBlocked(____penitent) || ____penitent.Status.Dead;

        Player2Input.Tick();

        bool rawDown = Player2Input.Down;
        bool left = !blocked && Player2Input.Left;
        bool right = !blocked && Player2Input.Right;
        bool rawJumpKey = Player2Input.JumpHeld;
        bool jump = !blocked && rawJumpKey;
        bool crouch = !blocked && rawDown;
        bool attackUp = !blocked && Player2Input.Up;
        bool rawAttackKeyDown = Player2Input.AttackDown;
        bool attack = !blocked && rawAttackKeyDown;
        bool rawDashKeyDown = Player2Input.DashDown;
        bool dash = !blocked && rawDashKeyDown;
        if (rawDashKeyDown)
        {
            // Raw, unfiltered check for the still-open "holding P1's dash button makes P2's own
            // dash key just crouch" report - logs the instant UnityEngine.Input.GetKeyDown itself
            // reports the physical Keypad2 press, before `blocked`/PlayerLogicBlocker/anything
            // else in this mod gets a chance to touch it. If this never fires while the user is
            // holding Left Shift and pressing Keypad2, the keypress itself isn't reaching Unity's
            // input system at all in that combination (a real hardware/OS-level interaction, e.g.
            // key ghosting or an OS accessibility feature intercepting Shift+Numpad) rather than
            // anything this mod's own logic could be responsible for.
            DashParryDebugLog.Log($"P2 raw Input.GetKeyDown(Dash) = True (blocked={blocked}, frame {Time.frameCount})");
        }

        // Same raw-vs-gated split as the Dash check above, now for Attack and Jump: the user
        // reports P2 can't attack, parry, or jump at all while P1 is dashing/holding its own dash
        // button (Left Shift), and suspects the numpad itself stops being read in that combination
        // rather than a code-level gate. `blocked` here is PlayerLogicBlocker.IsBlocked(P2) - P1
        // dashing alone should never make this true for P2 (only P2's own dash/parry/ladder-grab
        // lock does) - so if `attack`/`jump` end up false while `rawAttackKeyDown`/`rawJumpKey` are
        // true, the gate is the cause; if the raw reads themselves never go true while Left Shift
        // is held, UnityEngine.Input isn't seeing the physical keypress at all in that combination
        // (hardware/OS-level, same family as the Dash check above) and no amount of patching this
        // mod's gating logic would fix it.
        if (rawAttackKeyDown)
        {
            DashParryDebugLog.Log($"P2 raw Input.GetKeyDown(Attack) = True (blocked={blocked}, frame {Time.frameCount})");
        }

        if (crouch != lastLoggedCrouch)
        {
            lastLoggedCrouch = crouch;
            DashParryDebugLog.Log($"P2 crouch input -> {crouch} (rawDown={rawDown}, blocked={blocked}, frame {Time.frameCount})");
            LogP1RewiredCrossTalkCheck("crouch/down", crouch);
        }
        if (rawJumpKey != lastLoggedRawJumpKey)
        {
            lastLoggedRawJumpKey = rawJumpKey;
            DashParryDebugLog.Log($"P2 raw Input.GetKey(Jump) -> {rawJumpKey} (gated jump={jump}, blocked={blocked}, frame {Time.frameCount})");
        }
        if (jump != lastLoggedJump)
        {
            lastLoggedJump = jump;
            LogP1RewiredCrossTalkCheck("jump", jump);
        }
        if (left != lastLoggedLeft)
        {
            lastLoggedLeft = left;
            LogP1RewiredCrossTalkCheck("left", left);
        }
        if (right != lastLoggedRight)
        {
            lastLoggedRight = right;
            LogP1RewiredCrossTalkCheck("right", right);
        }

        // The original method itself already blocks Left/Right while crouched (same rule
        // P1 follows) - our own crouch key is the source of truth for that here instead of
        // waiting a frame for Penitent.IsCrouched to catch up.
        bool canMove = !crouch;

        PlatformCharacterController controller = ____penitent.PlatformCharacterController;
        controller.SetActionState(eControllerActions.Left, canMove && left);
        controller.SetActionState(eControllerActions.Right, canMove && right);
        controller.SetActionState(eControllerActions.Jump, jump);

        PlatformCharacterInput input = ____penitent.PlatformCharacterInput;
        input.ReachAxisThreshold = left || right;
        JumpBackingField.SetValue(input, jump);

        // isJoystickDown is what AnimatorInyector actually checks to set Penitent.IsCrouched -
        // a plain public field, no backing-field trickery needed here.
        input.isJoystickDown = crouch;

        // isJoystickUp was never being overridden here, so it kept the value the *original*
        // method just computed from the shared Rewired vertical axis (P1's) a few lines earlier
        // in this same Update() call. AnimatorInyector.OnUpdate checks exactly this field
        // (_playerInput.isJoystickUp) to fire the "CLIMB_CLIFF_LEDGE" animator trigger while
        // hanging off a ledge - so P2 climbing a cliff lede only actually worked while *P1* was
        // also holding up. Same fix as isJoystickDown: plain public field, just needs setting.
        input.isJoystickUp = attackUp;

        // FVerAxis > AxisMovingThreshold => JOYSTICK_UP, < -threshold => JOYSTICK_DOWN (see
        // comment above the patch). Player2Input.Down doubles as both crouch and this axis;
        // Player2Input.Up (jump lives on its own button) drives the upward-attack state.
        FVerAxisBackingField.SetValue(input, crouch ? -1f : (attackUp ? 1f : 0f));

        // Attack/Dash are also plain public fields; GetKeyDown (not GetKey) matches the
        // original bKey/xKey = Rewired.GetButtonDown(5/7) - one pulse per press. Must be
        // assigned unconditionally (both true AND false) every frame - the original method
        // still runs first using P1's shared input and may have just set Attack/Dash = true
        // from P1's own button, so only ever writing `true` here let that leak through and
        // never got cleared, which is why P2 could attack off P1's button and never stopped
        // being "stuck" attacking.
        input.Attack = attack;
        input.Dash = dash;

        if (left)
        {
            player2Facing = EntityOrientation.Left;
        }
        else if (right)
        {
            player2Facing = EntityOrientation.Right;
        }
        ____penitent.SetOrientation(player2Facing);
    }
}


