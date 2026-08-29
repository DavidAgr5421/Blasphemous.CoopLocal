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

// Round 57 - "P2 visually enters the falling/drop pose but stays physically stuck on the
// platform". JumpOffBehaviour (StateMachineBehaviour on the "JumpOff" animator state - the
// actual platform-drop-through pose, entered via AnimatorInyector.OnUpdate's own
// `SpriteAnimator.SetTrigger(JumpOff)` call when _playerInput.IsJumpOff goes true - see
// AnimatorInyector.cs ~line 492) has the exact same _penitent lazy-fallback bug as
// FallingBehaviour/CrouchDownBehaviour above (decompiled JumpOffBehaviour.cs line 24-27:
// `if (_penitent == null) { _penitent = Core.Logic.Penitent; }`), never patched until now.
// Unlike those two, everything OnStateEnter does while wrongly bound lands on P1 instead of P2
// every single time P2 tries to drop through a one-way platform:
//   - _penitent.Status.Invulnerable = true
//   - _penitent.Dash.enabled = false
//   - _penitent.Dash.SetDashSkinCollision() - shrinks the *collision skin* (Size/Center on the
//     SmartPlatformCollider, see Dash.cs line 293-297) to the smaller dash-sized hitbox. P2's own
//     collider size never changes at all for its own drop attempt, while P1's does, at a moment
//     P1 never asked for it.
//   - _penitent.PlatformCharacterInput.ResetActions()/ResetInputs() - wipes P1's own action
//     states/raw inputs.
//   - _penitent.PlatformCharacterController.PlatformCharacterPhysics.Velocity = Vector3.zero -
//     zeroes P1's velocity, not P2's.
//   - Core.Input.SetBlocker("PLAYER_LOGIC", true) - the global blocker. "jump-off" was already
//     named as one of the known-but-unaudited PLAYER_LOGIC users in PlayerLogicBlocker's own
//     comment (Dash/DashAndInputBlockers.cs) - registered below the same way DashBehaviour's own
//     lock is (JumpOffBehaviour_BlockerTracking_OnState{Enter,Exit}_Patch further down), so
//     BlockerOverrideHelper can un-freeze the *other* player for the duration instead of both
//     players losing input every time either one drops through a platform.
// OnStateUpdate (startedJumpOff/jumpOffRoot/IsJumpingOff) and OnStateExit (Invulnerable=false,
// DamageArea.EnableEnemyAttack(), Dash re-enable, Core.Input.SetBlocker(false), and the delayed
// Enable2DPhysics() coroutine restoring the collision skin/2D collision) have the exact same
// problem - _penitent is never reassigned once bound, so it stays wrong for the rest of the
// session (every future drop attempt by P2 keeps toggling P1's own Status/Dash/collider instead).
// Same fix as FallingBehaviour/CrouchDownBehaviour: force _penitent to the Animator's actual
// owner before OnStateEnter runs. _rootMotion's own lazy-init a few lines later in the same
// method (`if (_rootMotion == null) { _rootMotion = _penitent.GetComponentInChildren<...>(); }`)
// is its own separate null-check, not nested inside _penitent's - no bundled-init trap here -
// and reads off _penitent too, so fixing _penitent first here also fixes _rootMotion for free.
[HarmonyPatch(typeof(JumpOffBehaviour), "OnStateEnter")]
internal static class JumpOffBehaviour_OnStateEnter_Patch
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

// Mirrors DashBehaviour_BlockerTracking_OnState{Enter,Exit}_Patch (Dash/DashAndInputBlockers.cs) -
// registers/clears the correctly-resolved owner's PLAYER_LOGIC lock with PlayerLogicBlocker so
// BlockerOverrideHelper (already wrapping PlatformCharacterInput.Update() and
// AnimatorInyector.Update() for every instance) can tell this lock apart from one genuinely
// belonging to the other player and un-freeze that other player's own Update() call for its
// duration - same treatment Dash already gets.
[HarmonyPatch(typeof(JumpOffBehaviour), "OnStateEnter")]
internal static class JumpOffBehaviour_BlockerTracking_OnStateEnter_Patch
{
    private static void Postfix(Animator animator)
    {
        Penitent owner = animator.GetComponentInParent<Penitent>();
        PlayerLogicBlocker.SetBlocked(owner, true);
        DashParryDebugLog.Log($"{DashParryDebugLog.Label(owner)} JUMP_OFF lock ON (frame {Time.frameCount})");
    }
}

[HarmonyPatch(typeof(JumpOffBehaviour), "OnStateExit")]
internal static class JumpOffBehaviour_BlockerTracking_OnStateExit_Patch
{
    private static void Postfix(Animator animator)
    {
        Penitent owner = animator.GetComponentInParent<Penitent>();
        PlayerLogicBlocker.SetBlocked(owner, false);
        DashParryDebugLog.Log($"{DashParryDebugLog.Label(owner)} JUMP_OFF lock OFF (frame {Time.frameCount})");
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

    // Round 52: root cause of "P2 no puede bajar de plataformas de un solo sentido (drop-through)".
    // NOT a SetActionState(Down/Jump) gap - those were already being overridden correctly below.
    // The actual trigger for a platform drop lives entirely inside vanilla's own
    // PlatformCharacterInput.Update() body (decompiled, same class, ~line 273): a one-shot edge
    // check `if (Jump && !IsJumpOff && controller.IsGrounded && isJumpOffReady && !pressedJumpButton
    // && !FloorChecker.IsOnFloorPlatform && !StepOnLadder && isJoystickDown && level != "D24Z01S01")`
    // that calls `StartCoroutine(JumpOff())` - a private coroutine that waits `timeToJumpOff`
    // seconds and then does `m_platformCtrl.SetActionState(eControllerActions.PlatformDropDown,
    // true)`. CreativeSpore.SmartColliders.PlatformCharacterController.OnUpdate (the third-party
    // physics asset, same family already found for ladders) only clears the one-way collision layer
    // mask - the actual thing that lets the character fall through - when THAT specific action state
    // (PlatformDropDown, distinct from Down/Jump) is true. This mod's Postfix here never touched
    // PlatformDropDown at all - there was no override for it, explicit or otherwise.
    // Worse: the `Jump` and `isJoystickDown` values that edge check reads are computed earlier in
    // that SAME vanilla Update() call (`Jump = aKey` where aKey = Rewired.GetButton(6); isJoystickDown
    // = IsJoystickDown() which reads Rewired.GetAxis(4) directly) - i.e. shared Player 0 - BEFORE this
    // Postfix ever runs. Overriding isJoystickDown/Jump afterward (as already done below, correctly,
    // for continuous state like SetActionState/AnimatorInyector reads) is too late for this one-shot
    // decision: by the time the Postfix corrects those fields, vanilla's own edge check already fired
    // (or, in practice, almost never fired, since P1's real jump+down buttons essentially never
    // coincide with P2 pressing its own) using the wrong data for that frame. So P2's own StartCoroutine
    // never ran, PlatformDropDown was never set, and P2 stayed physically stuck on the platform.
    // Fix: reimplement the same edge-triggered gate + timer here, entirely from P2's own already-gated
    // `jump`/`crouch` values and P2's own instance state (isJumpOffReady, FloorChecker, StepOnLadder,
    // controller.IsGrounded - all public, all correctly per-instance already), driving the same public
    // IsJumpOff property (backing field, same AccessTools trick as Jump/FVerAxis above) and firing
    // onJumpOff so anything hooked to that event (audio/vfx) still plays for P2 too.
    private static readonly FieldInfo IsJumpOffBackingField =
        AccessTools.Field(typeof(PlatformCharacterInput), "<IsJumpOff>k__BackingField");

    private static bool player2PressedJumpButton;
    private static bool player2JumpOffPending;
    private static float player2JumpOffTimer;

    // Round 51: root cause of "P2 entra en carga de ataque cuando P1 mantiene su botón de
    // ataque". IsAttackButtonHold (private-set auto-property) is computed inside vanilla's own
    // AttackButtonHold()/ResetAttackButtonHold() private methods, called unconditionally at the
    // top of Update() - both read `Rewired.GetButton(5)`/`GetButtonUp(5)` directly, the shared
    // Player 0. Every PlatformCharacterInput instance in the game computes this from the SAME
    // physical button, including P2's own - so P2's own IsAttackButtonHold goes true whenever P1
    // holds the real attack button for timeInputAttackHold seconds, completely independent of P2's
    // own input. AnimatorInyector.ChargeAttackTriggered() (correctly per-instance, reads P2's own
    // _playerInput) then legitimately fires SetTrigger(ChargeAttack) on P2's real Animator - P2
    // really does enter the charging state, it's not an owner-resolution bug at all (the existing
    // ManyPlayerAnimationBehaviours_PenitentOwnerFix_Patch in Abilities/AbilityInputFixes.cs, which
    // fixes StartChargingAttackBehaviour's `_penitent` field, was necessary but insufficient - it
    // only fixes which ChargedAttack.Cast() gets called *after* P2 has already wrongly entered the
    // state). Fixed the same way Attack/Dash are overridden below: reimplement the timed-hold gate
    // here using Player2Input.AttackHeld (already mode/device-aware) instead of shared Rewired, and
    // overwrite the backing field every frame after vanilla's own (wrong) computation has run.
    private static readonly FieldInfo IsAttackButtonHoldBackingField =
        AccessTools.Field(typeof(PlatformCharacterInput), "<IsAttackButtonHold>k__BackingField");

    private static float player2AttackHoldTimer;
    private static bool player2AttackButtonHold;

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
    private static string lastLoggedVerticalActionState;

    // Round 58: edge-trigger flag for the jump-vs-drop-through race fix below (Jump action state
    // suppression while crouch is held).
    private static bool lastLoggedJumpSuppressedByCrouch;

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

        // Round 48 - "P2 can't climb ladders unless P1 also holds up/down": this Postfix has only
        // ever overridden Left/Right/Jump. The vanilla PlatformCharacterInput.Update() body (which
        // always runs first, unconditionally, before this Postfix - Harmony can't skip it for a
        // Postfix-only patch) ALSO calls SetActionState(Up/Down, ...) itself, computed from the
        // shared Rewired vertical axis - and since nothing here ever overwrote those two afterward,
        // P2's own controller kept whatever Up/Down state P1's real input just set. That wouldn't
        // matter for the animator-driven crouch/attack-up reads (isJoystickDown/isJoystickUp,
        // already fixed below) - but CreativeSpore.SmartColliders.PlatformCharacterController.
        // DoClimbing() (the third-party asset that actually moves the character along a ladder
        // while climbing) reads these exact GetActionState(Up/Down) flags directly, completely
        // independent of anything in Gameplay.* this mod had audited before. Logged here (only
        // when either the vanilla-set value or our own value changes) so the cross-talk is visible
        // directly in BepInEx/LogOutput.log instead of just inferred from reading decompiled code.
        bool vanillaUp = controller.GetActionState(eControllerActions.Up);
        bool vanillaDown = controller.GetActionState(eControllerActions.Down);
        string verticalActionState = $"vanillaUp={vanillaUp} vanillaDown={vanillaDown} -> P2 own up={attackUp} down={crouch}";
        if (verticalActionState != lastLoggedVerticalActionState)
        {
            lastLoggedVerticalActionState = verticalActionState;
            DashParryDebugLog.Log($"P2 ladder Up/Down action state: {verticalActionState} (frame {Time.frameCount})");
        }

        controller.SetActionState(eControllerActions.Left, canMove && left);
        controller.SetActionState(eControllerActions.Right, canMove && right);

        // Round 58 - "P2 a veces salta en vez de bajar de la plataforma al soltar Down+Jump juntos".
        // Vanilla's own Jump action-state assignment (decompiled PlatformCharacterInput.Update,
        // ~line 296: `bool value = aKey && !BlockJump && !Blocked && (...) && !IsJoystickDown() &&
        // ...`) NEVER sets the Jump action state true while isJoystickDown (Down held) is true -
        // specifically so PlatformCharacterController.Update()'s own immediate jump-trigger block
        // (decompiled PCC.cs ~line 421: `if (GetActionState(Jump) && m_jumpingTimer < 0f &&
        // (IsGrounded || CanGhostJump)) { ...VSpeed = JumpingSpeed... }`, which is unconditional on
        // crouch state) can never fire a normal jump while Down+Jump are held together - only the
        // separate JumpOff gate below (which also requires crouch, same as vanilla's own copy) is
        // allowed to act on that combo. This mod's own override used to do
        // `SetActionState(Jump, jump)` unconditionally, with no crouch exclusion - so the instant P2
        // pressed Jump while grounded, PCC.Update() could fire a normal jump the very same frame
        // regardless of whether Down was also held, racing the JumpOff gate below (which only flips
        // PlatformDropDown after a `timeToJumpOff` delay - by which point the normal jump had often
        // already lifted P2 off the ground, so PCC.Update()'s own gate, which requires m_isGrounded,
        // would no longer match). Which side won on any given attempt depended on incidental
        // per-frame timing (leftover m_jumpingTimer from a previous jump, exact frame Down registered
        // as held vs Jump) - hence the inconsistency. Fix: mirror vanilla's !IsJoystickDown()
        // exclusion exactly - never set Jump true for P2 while crouch is also true.
        bool jumpActionState = jump && !crouch;
        if ((jump && crouch) != lastLoggedJumpSuppressedByCrouch)
        {
            lastLoggedJumpSuppressedByCrouch = jump && crouch;
            DashParryDebugLog.Log(
                $"P2 jump+crouch held together -> Jump action state suppressed (jumpActionState={jumpActionState}, " +
                $"jump={jump}, crouch={crouch}, grounded={controller.IsGrounded}, frame {Time.frameCount})");
        }
        controller.SetActionState(eControllerActions.Jump, jumpActionState);
        controller.SetActionState(eControllerActions.Up, attackUp);
        controller.SetActionState(eControllerActions.Down, crouch);

        PlatformCharacterInput input = ____penitent.PlatformCharacterInput;

        // Round 52 - platform drop-through. Reimplements vanilla's JumpOff edge-trigger/timer for
        // P2 only (see comment on IsJumpOffBackingField above). Mirrors vanilla's own gate exactly,
        // substituting P2's own gated `jump`/`crouch` for the shared-Rewired-derived Jump/isJoystickDown
        // vanilla reads, and its own static timer/latch fields for vanilla's private
        // pressedJumpButton/coroutine (which live on the same PlatformCharacterInput instance, but
        // get fed wrong data for P2's instance by vanilla's own Update() body before this Postfix runs).
        if (player2JumpOffPending)
        {
            player2JumpOffTimer -= Time.deltaTime;
            if (player2JumpOffTimer <= 0f)
            {
                controller.SetActionState(eControllerActions.PlatformDropDown, true);
                player2JumpOffPending = false;
                IsJumpOffBackingField.SetValue(input, false);
            }
        }
        if (____penitent.Status.IsHurt || ____penitent.Status.IsIdle)
        {
            IsJumpOffBackingField.SetValue(input, false);
        }
        bool player2IsJumpOff = (bool)IsJumpOffBackingField.GetValue(input);
        if (jump && !player2IsJumpOff && controller.IsGrounded && ____penitent.isJumpOffReady &&
            !player2PressedJumpButton && !____penitent.FloorChecker.IsOnFloorPlatform &&
            !____penitent.StepOnLadder && crouch &&
            !Core.LevelManager.currentLevel.LevelName.Equals("D24Z01S01"))
        {
            player2PressedJumpButton = true;
            IsJumpOffBackingField.SetValue(input, true);
            input.onJumpOff?.Invoke(____penitent.transform.position);
            controller.SetActionState(eControllerActions.PlatformDropDown, false);
            player2JumpOffPending = true;
            player2JumpOffTimer = input.timeToJumpOff;
            DashParryDebugLog.Log($"P2 platform drop-through triggered (timeToJumpOff={input.timeToJumpOff:F2}, frame {Time.frameCount})");
        }
        else if (!jump)
        {
            player2PressedJumpButton = false;
        }
        else
        {
            IsJumpOffBackingField.SetValue(input, false);
        }
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

        // Round 51 - see comment on IsAttackButtonHoldBackingField above. Mirrors vanilla's own
        // AttackButtonHold()/ResetAttackButtonHold() gate exactly (same timeInputAttackHold
        // threshold, same dead/not-grounded reset), just fed from Player2Input.AttackHeld instead
        // of the shared Rewired button. Also gated on `blocked` (PlayerLogicBlocker/dead), same
        // rationale as every other raw read in this Postfix - P2 shouldn't charge-attack while its
        // own dash/parry/ladder lock is active any more than P1 can while blocked.
        bool attackHeldNow = !blocked && Player2Input.AttackHeld;
        if (____penitent.Status.Dead || !____penitent.Status.IsGrounded)
        {
            player2AttackHoldTimer = 0f;
            player2AttackButtonHold = false;
        }
        if (attackHeldNow)
        {
            player2AttackHoldTimer += Time.deltaTime;
            if (player2AttackHoldTimer >= input.timeInputAttackHold && !player2AttackButtonHold)
            {
                player2AttackHoldTimer = 0f;
                player2AttackButtonHold = true;
            }
        }
        else
        {
            player2AttackHoldTimer = 0f;
            player2AttackButtonHold = false;
        }
        IsAttackButtonHoldBackingField.SetValue(input, player2AttackButtonHold);

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

// Round 57 diagnostic: directly observes CreativeSpore.SmartColliders.PlatformCharacterController
// .Update()'s own drop-through gate (decompiled PCC.cs ~line 440:
// `if (m_isGrounded && m_platformDropTimer <= 0f && GetActionState(PlatformDropDown)) { ... }`,
// the only place that actually clears the SmartPlatformCollider's OneWayCollisionDown mask -
// everything upstream of this, including the JumpOffBehaviour owner fix above, only gets P2's own
// PlatformDropDown action-state flag set correctly; this is the one spot that turns that flag into
// an actual physical pass-through). Edge-triggered on the gate's own boolean result, for P2's
// PlatformCharacterController instance only, logging every field the gate reads plus the
// SmartPlatformCollider's own LayerCollision/OneWayCollisionDown/EnableCollision2D state right at
// that instant - so a re-test can confirm directly whether the gate ever evaluates true for P2 at
// all (and if so, whether the collider mask actually changes), instead of just inferring it from
// the trigger log further up this file.
//
// Round 58 fix: this was a Postfix originally, which is wrong. The gate's own body (PCC.cs line
// 440-445), when it fires, immediately sets `m_platformDropTimer = PlatformDropTime` (0.1s, i.e.
// > 0) as part of the SAME call that just used `m_platformDropTimer <= 0f` to decide to fire.
// A Postfix reads the fields AFTER Update() already mutated them, so `platformDropTimer <= 0f` -
// the very condition the gate just consumed - always reads back false immediately after a real
// trigger, and the recomputed gateResult can never be observed as true. This is exactly what the
// real log showed: "-> False" logged once near session start and never again, despite 10 confirmed
// P2 drop-throughs afterward in the same log (the actual in-game trigger log
// "P2 platform drop-through triggered" above fired correctly all 10 times - only this diagnostic's
// own recomputation was blind). Fixed by moving this to a Prefix, so it reads m_isGrounded/
// m_platformDropTimer/PlatformDropDown exactly as Update() itself is about to see them.
[HarmonyPatch(typeof(PlatformCharacterController), "Update")]
internal static class PlatformCharacterController_Update_DropThroughDebug_Patch
{
    private static readonly FieldInfo IsGroundedField = AccessTools.Field(typeof(PlatformCharacterController), "m_isGrounded");
    private static readonly FieldInfo PlatformDropTimerField = AccessTools.Field(typeof(PlatformCharacterController), "m_platformDropTimer");

    private static bool lastLoggedGateResult;
    private static bool hasLoggedOnce;

    private static void Prefix(PlatformCharacterController __instance)
    {
        Penitent owner = __instance.GetComponent<Penitent>();
        if (owner == null || owner != CoopLocal.Player2)
        {
            return;
        }

        bool isGrounded = (bool)IsGroundedField.GetValue(__instance);
        float platformDropTimer = (float)PlatformDropTimerField.GetValue(__instance);
        bool dropFlag = __instance.GetActionState(eControllerActions.PlatformDropDown);
        bool gateResult = isGrounded && platformDropTimer <= 0f && dropFlag;

        if (!hasLoggedOnce || gateResult != lastLoggedGateResult)
        {
            hasLoggedOnce = true;
            lastLoggedGateResult = gateResult;
            SmartPlatformCollider collider = __instance.SmartPlatformCollider;
            DashParryDebugLog.Log(
                $"P2 PCC.Update() drop-through gate -> {gateResult} (m_isGrounded={isGrounded}, " +
                $"m_platformDropTimer={platformDropTimer:F3}, PlatformDropDown flag={dropFlag}, " +
                $"LayerCollision={collider.LayerCollision.value}, OneWayCollisionDown={collider.OneWayCollisionDown.value}, " +
                $"EnableCollision2D={collider.EnableCollision2D}, colliderComponentEnabled={collider.enabled}, " +
                $"frame {Time.frameCount})");
        }
    }
}


