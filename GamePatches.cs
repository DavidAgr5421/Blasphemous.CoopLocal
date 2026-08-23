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
using Gameplay.GameControllers.AnimationBehaviours.Player.Run;
using Gameplay.GameControllers.AnimationBehaviours.Player.SubStatesBehaviours;
using Gameplay.GameControllers.Camera;
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

// P2's key scheme - centralized here so every patch below reads Player2Keys.Xxx instead of a
// hardcoded KeyCode, making it a one-place change if it ever needs remapping again.
//
// History: briefly moved entirely onto the numpad (4/6/2/8 + corners + 0) on the theory that
// P1's real Rewired bindings overlapped with P2's original arrow-keys assignment - reverted back
// to arrow keys at the time because (a) the numpad turned out to be bound to manual CAMERA PAN
// instead (a *different* real conflict), and (b) the freeze bug under investigation at the time
// persisted unchanged either way, proving key overlap wasn't its cause (that freeze had a
// completely separate, since-fixed root cause - see BlockerOverrideHelper).
//
// Round 28 dug up a *different*, confirmed-real instance of the same key-overlap family: with P1
// dashing on repeat (holding the dash button), pressing P2's own move/crouch/jump keys (all on
// arrows + Right Control) reliably cancels P1's dash mid-repeat. The fix at the time was to move
// P2 entirely onto the numpad (movement on 4/6/2/8, jump on 0, Dash/Attack/Parry on Right
// Shift/Period/Minus) plus CameraPan_Disable_Patch below (the numpad's directional keys collide
// with the game's built-in manual camera pan otherwise).
//
// That numpad-only scheme turned out to have its own, worse cross-talk: with P1 dashing and the
// dash button held, P2 couldn't attack or parry at all, P2's own dash key made it crouch instead,
// and P2 moving/jumping made P1 stop being able to dash even while its button stayed held. Reverted
// back to arrows for movement (accepting the round-28 dash-cancel overlap as the lesser bug) with
// jump/attack/parry moved to Keypad0/1/2 (Dash stayed on Right Control) - which turned out to
// still be broken, just differently: raw [DashParryDebug] logging (round 29) showed the *bare
// keypress itself* misbehaving on Keypad0/1 while P1 held its own dash button (Left Shift) -
// Keypad0 (Jump)/Keypad1 (Attack) simply stopped registering, while Keypad2 (Parry) kept
// registering but got read as P2's *Down/crouch* key instead of Parry. That specific pairing -
// numpad digit silently aliasing to the same signal as an arrow key - is the classic symptom of
// NumLock being off: with NumLock off, the physical numpad sends the same virtual keys as the
// navigation cluster (Keypad2->Down, Keypad4->Left, Keypad6->Right, Keypad8->Up, Keypad0->Insert),
// so Keypad2 became indistinguishable from Player2Keys.Down (also an arrow key) while Keypad0/1
// (Insert/End) matched nothing this mod reads at all. Whether the root cause is really NumLock, a
// leftover CameraPan interaction despite CameraPan_Disable_Patch, or something else, the practical
// fix (per the user's own suggestion) is the same either way: get Attack/Parry/Jump off the
// numpad entirely so the ambiguity can't happen, regardless of NumLock state. Dash stays on Right
// Control (confirmed working, never implicated in any round so far).
internal static class Player2Keys
{
    internal const KeyCode Left = KeyCode.LeftArrow;
    internal const KeyCode Right = KeyCode.RightArrow;
    internal const KeyCode Down = KeyCode.DownArrow;
    internal const KeyCode Up = KeyCode.UpArrow;
    internal const KeyCode Jump = KeyCode.RightShift;
    internal const KeyCode Dash = KeyCode.RightControl;
    internal const KeyCode Attack = KeyCode.LeftBracket;
    internal const KeyCode Parry = KeyCode.RightBracket;
}

// Debug-only logging for tracking down the remaining dash/parry cross-talk (P1 still freezing
// while P2 dashes/parries; simultaneous dash only letting one player through). Every call site
// below only fires on an actual state TRANSITION (lock on/off, Blocked value flipping) rather
// than every frame, so a single dash/parry produces a handful of lines, not hundreds - grep
// BepInEx/LogOutput.log for "[DashParryDebug]" after reproducing either symptom. Remove once the
// remaining cause is found; this is not meant to ship long-term.
internal static class DashParryDebugLog
{
    internal static string Label(Penitent p)
    {
        if (p == null)
        {
            return "null";
        }
        return p == CoopLocal.Player2 ? "P2" : "P1";
    }

    internal static void Log(string message)
    {
        Main.CoopLocal?.Log("[DashParryDebug] " + message);
    }
}

// Runtime evidence (the [DashParryDebug] Blocked/lock logs above, reproduced and checked against
// BepInEx/LogOutput.log) ruled out PlatformCharacterInput.Blocked/Core.Input.SetBlocker entirely
// as the cause of P1 freezing while P2 dashes/parries - P1 never once became Blocked during any
// of those windows. Disabling physical collision between the two characters (see
// CoopLocal.OnPlayerSpawn) didn't fully fix it either. Since the cause isn't in the input-lock
// system, this traces the other half of the picture: what each player's Animator is actually
// playing, moment to moment. Piggybacks on PlatformCharacterInput.Update() (already runs every
// frame for both P1 and P2) with its own unconditional Postfix - separate from
// PlatformCharacterInput_Update_Patch above, which only fires for P2 - and logs the *clip name*
// (not the state hash, which isn't human-readable) every time it changes for either player.
[HarmonyPatch(typeof(PlatformCharacterInput), "Update")]
internal static class AnimatorClipChangeLogger_Patch
{
    private static readonly Dictionary<Penitent, string> lastClipName = new Dictionary<Penitent, string>();

    private static void Postfix(Penitent ____penitent)
    {
        if (____penitent == null || ____penitent.Animator == null)
        {
            return;
        }

        AnimatorClipInfo[] clips = ____penitent.Animator.GetCurrentAnimatorClipInfo(0);
        string clipName = clips.Length > 0 ? clips[0].clip.name : "(none)";

        if (!lastClipName.TryGetValue(____penitent, out string last) || last != clipName)
        {
            lastClipName[____penitent] = clipName;
            DashParryDebugLog.Log($"{DashParryDebugLog.Label(____penitent)} anim -> \"{clipName}\" (frame {Time.frameCount})");
        }
    }
}

// Ground truth for whether a player is *actually* moving, independent of Blocked/locks/animation
// state entirely - all of which have already been checked and never showed an anomaly for P1
// while P2 dashes/parries. Samples both players' X position on a fixed cadence (not edge-
// triggered, since position drifts continuously while moving - logging only on change would spam
// every frame) along with whichever of P2's raw action buttons is currently held, so a genuine
// freeze shows up as several consecutive identical X values for the frozen player while the
// other one's X keeps changing.
[HarmonyPatch(typeof(PlatformCharacterInput), "Update")]
internal static class PositionSamplerLogger_Patch
{
    private const int SampleEveryNFrames = 15;

    private static void Postfix(Penitent ____penitent)
    {
        if (____penitent == null || ____penitent != CoopLocal.Player2 || Time.frameCount % SampleEveryNFrames != 0)
        {
            return;
        }

        Penitent p1 = Core.Logic.Penitent;
        float p1X = p1 != null ? p1.transform.position.x : float.NaN;
        float p2X = CoopLocal.Player2 != null ? CoopLocal.Player2.transform.position.x : float.NaN;
        // The last three rounds of logging proved Blocked/locks/animation-state never show an
        // anomaly for P1 while P2 dashes or parries, and no other engine-level blocker fires at
        // that moment either (checked the raw, non-mod lines in LogOutput.log around several
        // occurrences) - yet P1's X reliably goes flat within 1-2 frames of P2's lock starting
        // and resumes within 1-2 frames of it ending, every single time. One explanation nothing
        // so far has ruled out: this is being tested solo, one person on one keyboard driving
        // both characters - P1's movement key (an arrow key, held with one hand) and P2's dash/
        // parry key (numpad/Right Ctrl, the other hand) are far enough apart that reaching for
        // one quite plausibly means physically releasing the other, which would produce exactly
        // this pattern with no bug involved at all. Logging P1's own raw arrow-key state here
        // (the actual physical keys, regardless of Blocked) settles it directly: if P1.x goes
        // flat while this still reads true, the input is being rejected somewhere (a real bug);
        // if it reads false, P1's key was let go (not a code issue - would need a second person,
        // or one hand fully dedicated to each character, to test this apart from that).
        bool p1MovementKeyHeld = Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow);
        DashParryDebugLog.Log($"pos P1.x={p1X:F2} P2.x={p2X:F2} p1MovementKeyHeld={p1MovementKeyHeld} (frame {Time.frameCount})");
    }
}

// The user confirmed (two people, P1's movement key never released) that P1 genuinely stops
// while P2 dashes/parries even with no collision involved (SmartColliders layer fix applied and
// still happening), and specifically that it's *one-directional* - P1 dashing/parrying never
// does this to P2. That asymmetry is the key clue: Core.Logic.Penitent always resolves to P1
// specifically, never P2 - so any code that reads Core.Logic.Penitent directly (instead of the
// correct per-instance owner) happens to work by coincidence whenever P1 is the one acting (P1
// IS Core.Logic.Penitent), but reaches into P1 by mistake whenever P2 acts. Several such
// hardcoded Core.Logic.Penitent reads already exist in Parry (StartParry's IsRunningCombo/
// CancelEffect check, StopParry's IsOnParryChance/StopParryFx) but none of them were confirmed
// to touch movement directly - there may be another one, in a class not yet read in full, that
// does.
//
// Rather than keep reading classes hoping to spot it, this catches the actual mechanism red-
// handed: PlatformCharacterController.SetActionState(Left/Right, false) is the one call that
// actually zeroes horizontal movement (see PlatformCharacterInput.Update()'s own use of it).
// Postfixing it and logging a full stack trace *specifically when it's called on P1's own
// controller* (regardless of who's Update() call reached it from) will name the exact calling
// method the next time this happens - conclusive, no more guessing. Edge-triggered (only logs on
// the true->false transition) to avoid spamming every normal "not currently holding a direction"
// frame.
// Every previously-tracked condition (Blocked, ladder/crouch/front-blocked, IsHurt/Dead/
// JumpingOff/ChargingAttack/IsAttacking, simulatingMove) has come back False across 20+ logged
// occurrences, and the raw Rewired axis itself reads a valid +-1 (held direction) at the exact
// moment the false call lands on P1's controller. That rules out every branch inside
// PlatformCharacterInput.Update() that could legitimately produce false given those inputs - so
// either something *else* calls SetActionState(Left/Right, false) on P1's controller directly
// (a stray call, likely another _penitent-style wrong-owner bug not yet found), or the vanilla
// call and a second, later call both land in the same frame and only the second one's edge is
// visible here (the dedup below only ever kept the *last* value per action, hiding an earlier
// same-frame call). To tell these apart, WatchWindow below opens a short unconditional logging
// window (every call, true and false, no dedup) for a few frames right after P2's own DASH/PARRY
// lock turns on - if two calls for the same action show up in one frame, that's the smoking gun.
internal static class SetActionStateWatchWindow
{
    // ~0.25s at 60fps - long enough to catch the first few frames of P2's dash/parry lock without
    // spamming the log for the whole duration of the action.
    private const int WindowFrames = 15;

    internal static int EndFrame { get; private set; } = -1;

    internal static void OpenIfPlayer2(Penitent owner)
    {
        if (owner != null && owner == CoopLocal.Player2)
        {
            EndFrame = Time.frameCount + WindowFrames;
        }
    }

    internal static bool IsOpen => Time.frameCount <= EndFrame;
}

[HarmonyPatch(typeof(PlatformCharacterController), nameof(PlatformCharacterController.SetActionState))]
internal static class SetActionState_DebugLogger_Patch
{
    private static readonly Dictionary<eControllerActions, bool> lastP1Value = new Dictionary<eControllerActions, bool>();

    private static void Postfix(PlatformCharacterController __instance, eControllerActions action, bool value)
    {
        bool isTrackedAction = action == eControllerActions.Left || action == eControllerActions.Right;
        // Jump/Up/Down are only interesting during the watch window, to tell apart "the normal
        // else-branch computed false because num was ~0" (Left/Right only) from
        // "ResetActions() nuked all five at once" (Jump/Up/Down/Left/Right together) - see
        // PlatformCharacterInput.ResetActions(), called externally by JumpOffBehaviour/
        // VerticalAttackLandingBehaviour/Driven, none of which use the ref-Penitent Harmony
        // injection pattern used elsewhere in this file, so none have been audited yet for the
        // usual wrong-owner bug.
        bool isWatchOnlyAction = action == eControllerActions.Jump || action == eControllerActions.Up || action == eControllerActions.Down;
        if (!isTrackedAction && !isWatchOnlyAction)
        {
            return;
        }

        Penitent p1 = Core.Logic.Penitent;
        if (p1 == null || __instance != p1.PlatformCharacterController)
        {
            return;
        }

        bool windowOpen = SetActionStateWatchWindow.IsOpen;

        if (isWatchOnlyAction)
        {
            if (windowOpen)
            {
                DashParryDebugLog.Log($"P1 SetActionState({action}, {value}) (frame {Time.frameCount}) [watch window]");
            }
            return;
        }

        if (!windowOpen)
        {
            if (lastP1Value.TryGetValue(action, out bool last) && last == value)
            {
                return;
            }
        }
        lastP1Value[action] = value;

        if (windowOpen)
        {
            DashParryDebugLog.Log($"P1 SetActionState({action}, {value}) (frame {Time.frameCount}) [watch window]");
        }

        if (!value)
        {
            // The stack trace approach above didn't pan out - Harmony's patched method shows up
            // as its own opaque DMD trampoline with nothing useful above it in this Mono runtime,
            // so it can't name the caller directly. Dumping every condition that PlatformCharacter
            // Input.Update()'s own vanilla logic actually checks before calling
            // SetActionState(Left/Right, false) does the same job more directly: whichever one is
            // true here *is* the reason, read right at the moment it took effect on P1's own
            // controller, regardless of which Update() call (P1's real one, since this is P1's
            // controller) triggered it.
            // 20+ occurrences across several test sessions all showed every one of the fields
            // below as False, yet the call still happened - meaning none of PlatformCharacterInput
            // .Update()'s own gating conditions explain it, and it must come down to the *raw*
            // Rewired axis read itself (Rewired.GetAxisRaw(0)) reading as not-pressed for that one
            // frame, despite the physical key being held (confirmed with two people). Logging that
            // raw value directly here removes the last bit of inference - if it prints anything
            // other than the expected -1/1 for a held direction, Rewired itself is being disrupted
            // by something, not this mod's own gating logic.
            PlatformCharacterInput p1Input = p1.PlatformCharacterInput;
            float rawRewiredAxis = p1Input.Rewired != null ? p1Input.Rewired.GetAxisRaw(0) : float.NaN;
            // FHorAxis is the *actual* value Update() used to compute num (set via
            // `float num = (FHorAxis = horizontalAxis);`) - unlike RewiredAxisRaw0 above (an
            // independent fresh read of the controller/keys), this reflects whatever
            // horizontalAxis held at that exact moment, including any override from
            // forceHorizontalMovement (see Penitent.ForceMove/ForceMovementAction - hardcoded to
            // Core.Logic.Penitent, so if anything on P2's side ever triggers it, it would corrupt
            // P1's own horizontalAxis read every frame while active, independently of the real
            // Rewired axis). If RewiredAxisRaw0 and FHorAxis disagree, the mismatch happens
            // between those two lines - point squarely at forceHorizontalMovement.
            // Blocked above goes through this mod's own Harmony Postfix on the property getter,
            // which has consistently read False here even when FHorAxis contradicts a valid raw
            // axis - suggesting Update()'s *own internal* call to `Blocked` might not be going
            // through that patched getter at all (a trivial one-line `=>` property is a prime
            // candidate for the JIT inlining its body directly into callers compiled before or
            // without seeing the patch, in which case internal callers would see the raw,
            // *unpatched* value while only external callers like this diagnostic get the override).
            // RawInputBlocked reads Core.Input.InputBlocked directly - the same underlying value,
            // but with zero Harmony involvement anywhere in the call - to check whether the real
            // global blocker (P2's own dash/parry lock, which legitimately sets it) was actually
            // active this whole time and only Update()'s *effective* per-player override was ever
            // failing to apply, not the raw signal itself.
            bool rawInputBlocked = Core.Input.InputBlocked;
            DashParryDebugLog.Log(
                $"P1 SetActionState({action}, false) (frame {Time.frameCount}) - " +
                $"RewiredAxisRaw0={rawRewiredAxis:F3} FHorAxis={p1Input.FHorAxis:F3} ForceHorizontalMovement={p1Input.forceHorizontalMovement:F3} " +
                $"Blocked={p1Input.Blocked} RawInputBlocked={rawInputBlocked} IsGrabbingLadder={p1.IsGrabbingLadder} IsCrouched={p1.IsCrouched} " +
                $"BeginCrouch={p1.BeginCrouch} IsCrouchAttacking={p1.IsCrouchAttacking} " +
                $"FRONT_BLOCKED={p1.HasFlag("FRONT_BLOCKED")} simulatingMove={p1Input.simulatingMove} " +
                $"IsDashing={p1.IsDashing} IsHurt={p1.Status.IsHurt} Dead={p1.Status.Dead} IsJumpingOff={p1.IsJumpingOff} " +
                $"IsChargingAttack={p1.IsChargingAttack} IsAttacking={p1Input.IsAttacking}");
        }
    }
}

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
// - and overwrites, for P2 only, everything driven by P2's own keys (Player2Keys) instead:
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
            $"GetAxisRaw(4) [vertical]={p1Rewired.GetAxisRaw(4):F3} (frame {Time.frameCount})");
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

        bool rawDown = Input.GetKey(Player2Keys.Down);
        bool left = !blocked && Input.GetKey(Player2Keys.Left);
        bool right = !blocked && Input.GetKey(Player2Keys.Right);
        bool rawJumpKey = Input.GetKey(Player2Keys.Jump);
        bool jump = !blocked && rawJumpKey;
        bool crouch = !blocked && rawDown;
        bool attackUp = !blocked && Input.GetKey(Player2Keys.Up);
        bool rawAttackKeyDown = Input.GetKeyDown(Player2Keys.Attack);
        bool attack = !blocked && rawAttackKeyDown;
        bool rawDashKeyDown = Input.GetKeyDown(Player2Keys.Dash);
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
        // comment above the patch). Player2Keys.Down doubles as both crouch and this axis;
        // Player2Keys.Up (jump lives on its own key) drives the upward-attack state.
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

// Dash has its own copy of the "_penitent points at P1" bug: Dash.OnStart() does
// `if (!_penitent) _penitent = Core.Logic.Penitent;`, and _penitent starts out null on every
// fresh instance (nothing ever assigns it from EntityOwner) - so P1's AND P2's own Dash
// component both end up with _penitent pointing at P1. This isn't just cosmetic: further down,
// AddDashForce() calls `_penitent.SetOrientation(...)` to face the dash direction - so every
// time P2 dashes, it was actually flipping *P1's* sprite/facing, not P2's, which is why P1's
// direction visibly changed and P2's next dash reused a stale direction (its own facing was
// never actually being updated). Fixing _penitent at its source, the same way the
// FallingBehaviour patch does, corrects every method in this class at once instead of having
// to work around the bug at each individual call site.
[HarmonyPatch(typeof(Dash), "OnStart")]
internal static class Dash_OnStart_Patch
{
    private static void Prefix(Dash __instance, ref Penitent ____penitent)
    {
        Penitent owner = __instance.GetComponentInParent<Penitent>();
        if (owner != null)
        {
            ____penitent = owner;
        }
    }
}

// AddDashForce() still needs its own fix on top of the above: even with _penitent corrected,
// it computes the dash direction from _penitent.PlatformCharacterInput.Rewired.GetAxisRaw(0) -
// Rewired is still the same shared Player 0 for both P1 and P2, so the direction itself would
// still follow whichever way P1's stick/keys are pointing (or P2's own current facing, as a
// fallback, if P1 isn't pressing anything). This patch runs before the direction gets computed
// (guarded by "_isDashDirectionSet") and, for P2 only, fills it in from P2's own movement keys
// instead, matching the same -1/0/+1 semantics the original produces from the raw axis.
[HarmonyPatch(typeof(Dash), "AddDashForce")]
internal static class Dash_AddDashForce_Patch
{
    private static void Prefix(Dash __instance, ref bool ____isDashDirectionSet, ref float ____dashDirection)
    {
        Penitent owner = __instance.GetComponentInParent<Penitent>();
        if (owner == null || owner != CoopLocal.Player2 || ____isDashDirectionSet)
        {
            return;
        }

        bool left = Input.GetKey(Player2Keys.Left);
        bool right = Input.GetKey(Player2Keys.Right);

        ____dashDirection = left ? -1f : (right ? 1f : 0f);
        ____isDashDirectionSet = true;
    }
}

// DashBehaviour (the Animator StateMachineBehaviour attached to the "Dash" state itself - not
// to be confused with the Dash Ability component, already fixed above) has the exact same
// "_penitent falls back to Core.Logic.Penitent" bug as Falling/CrouchDown, but with much worse
// fallout: on P2's *own* Animator clone, the first time P2 ever enters the Dash state, its own
// separate DashBehaviour instance's _penitent resolves to P1 and stays that way forever. From
// then on, every time P2 dashes, this behaviour keeps calling _penitent.PenitentMoveAnimations
// .PlayDash(), toggling _penitent.Dash.CrouchAfterDash/StopCast(), and - worst of all -
// _penitent.Animator.Play(...) (Attack_Running / GroundUpwardAttack / Start_Run_After_Dash /
// "Crouch Down" / ParryChance) directly on *P1's own Animator*, forcibly yanking P1 into
// unrelated animation states while P2 dashes. That's the real cause of "the other player loses
// the ability to move whenever someone dashes" - P2 dashing doesn't just fail to work right, it
// actively hijacks P1's character. Same root cause explains "if both dash at once, only one of
// them actually works": whichever DashBehaviour instance's _penitent didn't yet get cached
// correctly ends up fighting over the other player's Dash ability/Animator instead of its own.
// Fixed the same way as every other case of this bug: resolve the real owner in OnStateEnter,
// before anything else in the class ever reads _penitent.
[HarmonyPatch(typeof(DashBehaviour), "OnStateEnter")]
internal static class DashBehaviour_OnStateEnter_Patch
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

// AirDashBehaviour, DashStopBehaviour and RunAfterDashBehaviour are the other three
// StateMachineBehaviours involved in the dash's animation state graph (airborne dash, the
// recovery/stop state, and the "keep running after a dash" state) - each with its own separate
// _penitent field subject to the exact same bug, and each capable of the same kind of cross-talk
// (AirDashBehaviour toggles Physics.EnablePhysics on whichever Penitent it resolved to;
// RunAfterDashBehaviour calls _penitent.Dash.StopCast() and reads _penitent.Dash
// .StandUpAfterDash). Same fix, applied to each.
[HarmonyPatch(typeof(AirDashBehaviour), "OnStateEnter")]
internal static class AirDashBehaviour_OnStateEnter_Patch
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

[HarmonyPatch(typeof(DashStopBehaviour), "OnStateEnter")]
internal static class DashStopBehaviour_OnStateEnter_Patch
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

[HarmonyPatch(typeof(RunAfterDashBehaviour), "OnStateEnter")]
internal static class RunAfterDashBehaviour_OnStateEnter_Patch
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

// Even with _penitent correctly resolved above, DashBehaviour.OnStateEnter/OnStateExit still
// call Core.Input.SetBlocker("PLAYER_LOGIC", ...) - a single *global* list shared by the whole
// game (see InputManager.inputBlockers): PlatformCharacterInput.Blocked just returns
// Core.Input.InputBlocked (true if ANY blocker is active, of ANY kind), and the original
// PlatformCharacterInput.Update() zeroes out that instance's own Left/Right/Jump action states
// whenever Blocked is true. So P1 dashing also freezes P2's *own* Update() call and vice versa -
// P2 happens to be protected for plain movement/jump because PlatformCharacterInput_Update_Patch
// (above) unconditionally reasserts P2's own action states every frame regardless of Blocked,
// but P1 has no such protection, so P2 dashing genuinely freezes P1 solid for the dash's
// duration. Parry (see Parry_StartParry_Patch/Parry_StopParry_Patch further down) pushes/pops
// this exact same blocker, so it produces the identical freeze. This is one of dozens of places
// the game pushes "PLAYER_LOGIC" to mean "block MY OWN input for a moment" (WallJump, GuardSlide,
// ladders, hurt states, jump-off, combo finishers...) assuming there is only ever one character
// listening - auditing every remaining one is out of scope for now (nothing else has been
// reported broken), so only Dash's and Parry's own uses are redirected into a per-Penitent
// tracker below; every other "PLAYER_LOGIC" user keeps behaving exactly like solo play
// (globally), which is still correct for genuinely global blockers (dialog/menus/cutscenes/
// initial load) and just a latent, unaudited version of the same bug for the other
// per-character ones.
internal static class PlayerLogicBlocker
{
    private static readonly HashSet<Penitent> blocked = new HashSet<Penitent>();

    internal static void SetBlocked(Penitent owner, bool value)
    {
        if (owner == null)
        {
            return;
        }
        if (value)
        {
            blocked.Add(owner);
        }
        else
        {
            blocked.Remove(owner);
        }
    }

    // Self-healing against a stuck-true entry: if a lock's matching "unblock" call ever gets
    // skipped (an uncaught exception between the two, a level transition wiping the game's own
    // blocker list without going through SetBlocker - see InputManager_RemoveBlockers_Patch
    // below - or any other gap this mod hasn't found yet), that player would otherwise stay
    // permanently frozen out of movement/crouch with no way to recover. Cross-checking against
    // the real global blocker means a stale entry here stops mattering the moment ANYTHING
    // clears "PLAYER_LOGIC" for real, instead of requiring this exact HashSet to be cleared too.
    internal static bool IsBlocked(Penitent owner) => owner != null && blocked.Contains(owner) && Core.Input.HasBlocker("PLAYER_LOGIC");

    internal static void ClearAll() => blocked.Clear();
}

// InputManager.RemoveBlockers() (called from ResetManager(), itself called on level transitions)
// clears the whole shared blocker list directly (inputBlockers.Clear()) without going through
// SetBlocker(name, false) for each entry - so InputManager_SetBlocker_Patch's mirror
// (GlobalBlockerTracker) and PlayerLogicBlocker never hear about it and could keep believing a
// lock is still active across a level change. Clearing both here keeps them honest; combined
// with the self-healing check above this is mostly a belt-and-suspenders since a real level
// transition also blocks on other reasons (fade, etc.) while it's happening anyway.
[HarmonyPatch(typeof(InputManager), "RemoveBlockers")]
internal static class InputManager_RemoveBlockers_Patch
{
    private static void Postfix()
    {
        GlobalBlockerTracker.Clear();
        PlayerLogicBlocker.ClearAll();
    }
}

// Mirrors InputManager's private blocker list (Postfix on the only method that ever mutates it),
// split out so PlatformCharacterInput_Blocked_Patch can tell "something OTHER than a per-
// character PLAYER_LOGIC lock is blocking input" (dialog, cutscenes, menus, initial load -
// things that should still freeze both players, exactly like solo play) apart from the
// PLAYER_LOGIC entry itself, which shouldn't.
internal static class GlobalBlockerTracker
{
    private static readonly HashSet<string> active = new HashSet<string>();

    internal static void Track(string name, bool blocking)
    {
        if (blocking)
        {
            active.Add(name);
        }
        else
        {
            active.Remove(name);
        }
    }

    internal static bool AnyBlockerOtherThanPlayerLogic()
    {
        foreach (string name in active)
        {
            if (name != "PLAYER_LOGIC")
            {
                return true;
            }
        }
        return false;
    }

    internal static void Clear() => active.Clear();
}

[HarmonyPatch(typeof(InputManager), nameof(InputManager.SetBlocker))]
internal static class InputManager_SetBlocker_Patch
{
    private static void Postfix(string name, bool blocking) => GlobalBlockerTracker.Track(name, blocking);
}

[HarmonyPatch(typeof(DashBehaviour), "OnStateEnter")]
internal static class DashBehaviour_BlockerTracking_OnStateEnter_Patch
{
    private static void Postfix(Animator animator)
    {
        Penitent owner = animator.GetComponentInParent<Penitent>();
        PlayerLogicBlocker.SetBlocked(owner, true);
        SetActionStateWatchWindow.OpenIfPlayer2(owner);
        DashParryDebugLog.Log($"{DashParryDebugLog.Label(owner)} DASH lock ON (frame {Time.frameCount})");
    }
}

[HarmonyPatch(typeof(DashBehaviour), "OnStateExit")]
internal static class DashBehaviour_BlockerTracking_OnStateExit_Patch
{
    private static void Postfix(Animator animator)
    {
        Penitent owner = animator.GetComponentInParent<Penitent>();
        PlayerLogicBlocker.SetBlocked(owner, false);
        DashParryDebugLog.Log($"{DashParryDebugLog.Label(owner)} DASH lock OFF (frame {Time.frameCount})");
    }
}

// The actual consumer: PlatformCharacterInput.Blocked (read by that instance's own Update() to
// decide whether it may move/jump this frame) is patched to ignore a PLAYER_LOGIC-only global
// block unless *this* instance's own Penitent is the one currently locked (dashing or parrying).
// Any other concurrent blocker (dialog, menu, cutscene...) still blocks everyone, same as solo
// play.
[HarmonyPatch(typeof(PlatformCharacterInput), nameof(PlatformCharacterInput.Blocked), MethodType.Getter)]
internal static class PlatformCharacterInput_Blocked_Patch
{
    // Edge-triggered per-owner so this doesn't spam every frame - only logs when the effective
    // (post-override) value actually changes for that Penitent, along with the raw pre-override
    // value and why. If P1 ever logs "own PLAYER_LOGIC lock" that's the smoking gun (means
    // PlayerLogicBlocker wrongly contains P1); if P1 logs "other global blocker active" while
    // P2 dashes/parries alone, that's a different, real global blocker sneaking in; if P1 never
    // logs true at all here but still visibly freezes, the freeze isn't coming through this
    // property at all and the cause is somewhere else entirely (worth knowing either way).
    private static readonly Dictionary<Penitent, bool> lastLogged = new Dictionary<Penitent, bool>();

    private static void Postfix(Penitent ____penitent, ref bool __result)
    {
        bool raw = __result;
        string reason;
        if (PlayerLogicBlocker.IsBlocked(____penitent))
        {
            __result = true;
            reason = "own PLAYER_LOGIC lock";
        }
        else if (!__result)
        {
            reason = "not blocked";
        }
        else if (GlobalBlockerTracker.AnyBlockerOtherThanPlayerLogic())
        {
            reason = "other global blocker active";
        }
        else
        {
            // The only reason Blocked is currently true is the shared PLAYER_LOGIC entry, and
            // it's not this instance's own lock - so it belongs to the other player's dash/parry.
            __result = false;
            reason = "PLAYER_LOGIC belongs to the other player - ignored";
        }

        if (____penitent != null && (!lastLogged.TryGetValue(____penitent, out bool last) || last != __result))
        {
            lastLogged[____penitent] = __result;
            DashParryDebugLog.Log($"{DashParryDebugLog.Label(____penitent)}.Blocked -> {__result} (raw={raw}, reason={reason}, frame {Time.frameCount})");
        }
    }
}

// Round 17's diagnostic proved the patch above never actually fixed the freeze: SetActionState's
// own log showed Blocked=False (read externally, through the patched getter above) at the exact
// same instant RawInputBlocked (Core.Input.InputBlocked, read with zero Harmony involvement) was
// True. Both readings happen a few lines apart inside the very same synchronous call, with
// nothing able to mutate the underlying blocker state in between - the only way for them to
// legitimately disagree is if PlatformCharacterInput.Update()'s own internal use of `Blocked`
// (`bool flag = !Blocked;`) never goes through the patched get_Blocked() at all. `Blocked` is a
// trivial one-line `=>` property - exactly the shape the Mono JIT is most likely to inline
// directly into a caller's compiled code, especially a caller in the same assembly compiled
// well before this mod's Harmony patch existed. An inlined call reads the field
// (Core.Input.InputBlocked) directly, bypassing the getter method - and therefore this patch -
// entirely, while any *external* caller (this mod's own diagnostic, compiled into a separate
// assembly, always a real non-inlined call) correctly sees the patched result. That would explain
// every single symptom collected so far without contradiction.
//
// Rather than fight the JIT over whether a property gets inlined, this patches Update() itself:
// right before the original body runs, if the *only* reason Core.Input.InputBlocked is currently
// true is a PLAYER_LOGIC lock that belongs to the *other* player (exactly the condition the getter
// patch above already computes correctly), the actual backing field behind InputManager's
// InputBlocked auto-property is flipped to false for the duration of this one Update() call - so
// whatever Update() reads internally, inlined or not, sees the corrected value - and flipped back
// immediately after in a Postfix. Since MonoBehaviour.Update() calls never overlap/re-enter
// (single-threaded, one full call finishes before the next character's Update() begins), a plain
// save-and-restore around each individual call is safe even though P1's and P2's Update() both
// run within the same frame.
// Shared by every Update()-shaped method found so far that bare-checks Blocked/
// Core.Input.InputBlocked internally instead of going through PlatformCharacterInput_Blocked_Patch
// (which only ever affects *external* callers, per the inlining theory above). Temporarily hides
// a PLAYER_LOGIC lock that's positively confirmed (via PlayerLogicBlocker) to belong to the
// *other* Penitent, for the duration of one wrapped call, and restores the true value immediately
// after. Safe because none of the MonoBehaviour.Update() calls this gets attached to ever
// overlap/re-enter (single-threaded, one full call finishes before the next character's Update()
// begins) - a plain save-and-restore around each individual call is correct even though P1's and
// P2's own calls both happen within the same frame.
internal static class BlockerOverrideHelper
{
    private static readonly FieldInfo InputBlockedBackingField = AccessTools.Field(typeof(InputManager), "<InputBlocked>k__BackingField");

    // InputManager.HasBlocker(name) checks this List<string> *directly* - it's a completely
    // separate data source from the InputBlocked bool above (which is just a cached
    // `inputBlockers.Count > 0`, refreshed by SetBlocker() whenever it mutates the list).
    // Flipping InputBlocked alone therefore does nothing for any bare `Core.Input
    // .HasBlocker("PLAYER_LOGIC")` check - and there are several: PlatformCharacterInput
    // .AttackButtonHold() (`if (HasBlocker("DIALOG") || HasBlocker("PLAYER_LOGIC")) return;`,
    // called from inside PlatformCharacterInput.Update() itself) is the one that explains "P2
    // can't attack, parry, or dash while P1 holds its own dash button, but can still move and
    // jump" - confirmed by the user testing all three side by side. Movement/jump never route
    // through AttackButtonHold(), so they were never affected by this specific gap; anything
    // that reads Blocked (a real property, backed by InputBlocked) was already fixed, but this
    // bare-list check was invisible to that fix entirely.
    private static readonly FieldInfo InputBlockersListField = AccessTools.Field(typeof(InputManager), "inputBlockers");

    private static bool removedFromList;

    internal static bool TryOverride(Penitent instancePenitent)
    {
        removedFromList = false;
        if (instancePenitent == null || Core.Input == null)
        {
            return false;
        }
        if (PlayerLogicBlocker.IsBlocked(instancePenitent))
        {
            // This instance's own dash/parry/ladder-grab lock - it really should be blocked,
            // same as solo play.
            return false;
        }
        bool raw = (bool)InputBlockedBackingField.GetValue(Core.Input);
        if (!raw || GlobalBlockerTracker.AnyBlockerOtherThanPlayerLogic())
        {
            // Either nothing is blocking right now, or something genuinely global is (dialog/
            // menu/cutscene) - leave it alone, that should still freeze both players.
            return false;
        }

        // PlayerLogicBlocker only knows about the handful of abilities explicitly wired into it
        // (Dash, Parry, ladder-grab-down so far) - dozens of other places in the game's own code
        // push this same "PLAYER_LOGIC" blocker too (WallJump, GuardSlide, hurt states, jump-off,
        // combo finishers...) and aren't registered with it yet. Only override when the *other*
        // Penitent is positively confirmed to hold this lock through a tracked source - if neither
        // side is tracked (an unaudited ability locked *this* instance's own input, or the
        // tracker simply doesn't know), do nothing and leave the real block in effect. This is
        // the safe default: it never incorrectly un-freezes anyone, it just doesn't yet fix
        // cross-talk from abilities nobody has wired in - same "audit as reported" posture as the
        // rest of this file, instead of assuming un-tracked always means "the other player".
        Penitent other = (instancePenitent == CoopLocal.Player2) ? Core.Logic.Penitent : CoopLocal.Player2;
        if (!PlayerLogicBlocker.IsBlocked(other))
        {
            return false;
        }

        List<string> blockerList = (List<string>)InputBlockersListField.GetValue(Core.Input);
        if (blockerList.Contains("PLAYER_LOGIC"))
        {
            blockerList.Remove("PLAYER_LOGIC");
            removedFromList = true;
        }
        InputBlockedBackingField.SetValue(Core.Input, false);
        return true;
    }

    internal static void Restore()
    {
        InputBlockedBackingField.SetValue(Core.Input, true);
        if (removedFromList)
        {
            List<string> blockerList = (List<string>)InputBlockersListField.GetValue(Core.Input);
            if (!blockerList.Contains("PLAYER_LOGIC"))
            {
                blockerList.Add("PLAYER_LOGIC");
            }
            removedFromList = false;
        }
    }
}

[HarmonyPatch(typeof(PlatformCharacterInput), "Update")]
internal static class PlatformCharacterInput_Update_BlockerOverride_Patch
{
    private static bool overrodeThisCall;

    private static void Prefix(Penitent ____penitent)
    {
        overrodeThisCall = BlockerOverrideHelper.TryOverride(____penitent);
    }

    private static void Postfix()
    {
        if (overrodeThisCall)
        {
            BlockerOverrideHelper.Restore();
            overrodeThisCall = false;
        }
    }
}

// The dash-not-registering-when-simultaneous report traced back to a *second* instance of the
// exact same inlining gap, in a completely different class: AnimatorInyector.Dashing() (called
// from this class's own Update() -> UpdateActions() while grounded) gates starting a new dash on
// a bare `!_penitent.PlatformCharacterInput.Blocked` check - and ChargedAttack() (called right
// after it) has the same bare `!_playerInput.Blocked` check for attack-charge bookkeeping. Neither
// goes through PlatformCharacterInput.Update() at all, so the Prefix/Postfix pair above never
// touches them - when P1 and P2 press dash in the same frame, P2's own Dashing() reads the real,
// still-true global PLAYER_LOGIC lock P1's dash just pushed and refuses to call _playerDash.Cast()
// for P2 at all, leaving P2 sitting in whatever grounded-branch state Crouch() (which has no such
// gate) put it in instead - matching "P2 never even registers the dash, just crouches instead".
// Wrapping this whole Update() the same way covers Dashing(), ChargedAttack(), and any other
// currently-unaudited bare Blocked check inside this class in one place.
[HarmonyPatch(typeof(Gameplay.GameControllers.Penitent.Animator.AnimatorInyector), "Update")]
internal static class AnimatorInyector_Update_BlockerOverride_Patch
{
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(Gameplay.GameControllers.Penitent.Animator.AnimatorInyector), "_penitent");

    private static bool overrodeThisCall;

    private static void Prefix(object __instance)
    {
        Penitent penitent = PenitentField.GetValue(__instance) as Penitent;
        overrodeThisCall = BlockerOverrideHelper.TryOverride(penitent);
    }

    private static void Postfix()
    {
        if (overrodeThisCall)
        {
            BlockerOverrideHelper.Restore();
            overrodeThisCall = false;
        }
    }
}

// Diagnostic for the still-open "holding P1's dash button, then P2's own dash just crouches"
// report: logs every one of Dashing()'s gating conditions whenever P2's own Dash input pulses
// true (once per press, since it's a GetKeyDown edge), to see directly which condition (if any)
// is false and blocking _playerDash.Cast() from ever running.
[HarmonyPatch(typeof(Gameplay.GameControllers.Penitent.Animator.AnimatorInyector), "Dashing")]
internal static class AnimatorInyector_Dashing_DebugLogger_Patch
{
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(Gameplay.GameControllers.Penitent.Animator.AnimatorInyector), "_penitent");
    private static readonly FieldInfo PlayerInputField = AccessTools.Field(typeof(Gameplay.GameControllers.Penitent.Animator.AnimatorInyector), "_playerInput");
    private static readonly FieldInfo PlayerDashField = AccessTools.Field(typeof(Gameplay.GameControllers.Penitent.Animator.AnimatorInyector), "_playerDash");

    private static void Prefix(object __instance)
    {
        Penitent penitent = PenitentField.GetValue(__instance) as Penitent;
        if (penitent != CoopLocal.Player2)
        {
            return;
        }
        PlatformCharacterInput input = PlayerInputField.GetValue(__instance) as PlatformCharacterInput;
        if (input == null || !input.Dash)
        {
            return;
        }
        Dash playerDash = PlayerDashField.GetValue(__instance) as Dash;
        DashParryDebugLog.Log(
            $"P2 Dashing() attempt: Dash={input.Dash} Jump={input.Jump} DashEnabled={penitent.Dash.enabled} " +
            $"ReadyToUse={(playerDash != null ? playerDash.ReadyToUse.ToString() : "null")} IsGrabbingCliffLede={penitent.IsGrabbingCliffLede} " +
            $"IsHurt={penitent.Status.IsHurt} Dead={penitent.Status.Dead} StandUpAfterDash={penitent.Dash.StandUpAfterDash} " +
            $"IsChargingAttack={penitent.IsChargingAttack} Blocked={input.Blocked} IsFallingStunt={penitent.IsFallingStunt} " +
            $"(frame {Time.frameCount})");
    }
}

// DashBehaviour.OnStateUpdate (the per-frame logic while the "Dash" animation is playing) reads
// _penitent.PlatformCharacterInput.Rewired directly in five different places - Attack (button 5),
// Jump (button 6), Parry-cancel (button 38), and the vertical/horizontal axes - to decide whether
// to cancel the dash into a lunge attack, a parry, a jump, a crouch, or a run. Rewired is *always*
// the shared Player 0 (see "Rewired compartido" above) regardless of whose _penitent this is, so
// even with _penitent correctly resolved to the real owner, P2's own dash reacts to *P1's* real
// buttons instead of P2's: P1 pressing jump forces P2.AnimatorInyector.IsJumpWhileDashing and
// cuts P2's dash short ("recorrido reducido" when P1 jumps); P1's real parry button
// (mapped in Rewired) cancels P2's dash straight into *P2's own* Parry.Cast() even though P2
// never pressed Keypad3; and so on. Reimplemented for P2 only, substituting each Rewired read
// with P2's own keys (matching the scheme in PlatformCharacterInput_Update_Patch) - P1's own
// instance keeps running the untouched original, since Rewired correctly describes P1.
[HarmonyPatch(typeof(DashBehaviour), "OnStateUpdate")]
internal static class DashBehaviour_OnStateUpdate_Patch
{
    private static readonly int AttackRunningAnimHash = Animator.StringToHash("Attack_Running");
    private static readonly int UpwardAttackAnimHash = Animator.StringToHash("GroundUpwardAttack");
    private static readonly int RunningAfterDashAnimHash = Animator.StringToHash("Start_Run_After_Dash");
    private static readonly int ParryAnimHash = Animator.StringToHash("ParryChance");

    private static readonly MethodInfo AddExtraDashMethod = AccessTools.Method(typeof(DashBehaviour), "AddExtraDash");
    private static readonly MethodInfo CastLungeAttackMethod = AccessTools.Method(typeof(DashBehaviour), "CastLungeAttack");
    private static readonly MethodInfo CrouchMethod = AccessTools.Method(typeof(DashBehaviour), "Crouch");
    private static readonly FieldInfo AddExtraDashField = AccessTools.Field(typeof(DashBehaviour), "_addExtraDash");
    private static readonly FieldInfo CancelToParryField = AccessTools.Field(typeof(DashBehaviour), "_cancelToParry");

    private static bool Prefix(DashBehaviour __instance, Animator animator, AnimatorStateInfo stateInfo)
    {
        Penitent owner = animator.GetComponentInParent<Penitent>();
        if (owner == null || owner != CoopLocal.Player2)
        {
            return true;
        }

        bool left = Input.GetKey(Player2Keys.Left);
        bool right = Input.GetKey(Player2Keys.Right);
        bool crouchAxis = Input.GetKey(Player2Keys.Down);
        bool attackUpAxis = Input.GetKey(Player2Keys.Up);
        bool jumpHeld = Input.GetKey(Player2Keys.Jump);
        bool attackPressed = Input.GetKeyDown(Player2Keys.Attack);
        bool attackReleased = Input.GetKeyUp(Player2Keys.Attack);
        bool parryPressed = Input.GetKeyDown(Player2Keys.Parry);

        if (stateInfo.normalizedTime > 0.9f && owner.Dash.IsUpperBlocked && !(bool)AddExtraDashField.GetValue(__instance))
        {
            // AddExtraDash's own DOTween callback pushes/pops the global PLAYER_LOGIC blocker
            // directly (see comment further up) without going through PlayerLogicBlocker - a
            // known, not-yet-closed gap. Logged so it's obvious if this is what's actually
            // happening during a reported freeze.
            DashParryDebugLog.Log($"{DashParryDebugLog.Label(owner)} DASH hit upper-blocked wall, extending (frame {Time.frameCount})");
            AddExtraDashMethod.Invoke(__instance, null);
        }

        if (owner.Dash.IsUpperBlocked)
        {
            return false;
        }
        if (attackPressed && stateInfo.normalizedTime < 1f && (bool)CastLungeAttackMethod.Invoke(__instance, null))
        {
            return false;
        }

        if (parryPressed)
        {
            DashParryDebugLog.Log($"{DashParryDebugLog.Label(owner)} DASH cancelled into PARRY (frame {Time.frameCount})");
            CancelToParryField.SetValue(__instance, true);
            owner.Dash.StopCast();
            owner.CancelEffect.PlayCancelEffect();
            owner.DashDustGenerator.GetStopDashDust(0.1f);
            owner.Parry.Cast();
            owner.Dash.CrouchAfterDash = false;
            owner.Animator.Play(ParryAnimHash);
        }

        if (attackReleased && !jumpHeld && stateInfo.normalizedTime >= 0.1f)
        {
            owner.Dash.StopCast();
            owner.DashDustGenerator.GetStopDashDust(0.2f);
            owner.Dash.CrouchAfterDash = false;
            animator.Play(attackUpAxis ? UpwardAttackAnimHash : AttackRunningAnimHash);
        }

        if (jumpHeld && stateInfo.normalizedTime > 0.1f)
        {
            owner.AnimatorInyector.IsJumpWhileDashing = true;
            owner.Dash.StopCast();
            owner.Dash.CrouchAfterDash = false;
            if (PlayerLogicBlocker.IsBlocked(owner))
            {
                DashParryDebugLog.Log($"{DashParryDebugLog.Label(owner)} DASH cancelled by jump (frame {Time.frameCount})");
            }
            PlayerLogicBlocker.SetBlocked(owner, false);
            Core.Input.SetBlocker("PLAYER_LOGIC", blocking: false);
        }

        if (stateInfo.normalizedTime > 0.5f && stateInfo.normalizedTime < 1f && crouchAxis)
        {
            if (PlayerLogicBlocker.IsBlocked(owner))
            {
                DashParryDebugLog.Log($"{DashParryDebugLog.Label(owner)} DASH cancelled by crouch (frame {Time.frameCount})");
            }
            PlayerLogicBlocker.SetBlocked(owner, false);
            CrouchMethod.Invoke(__instance, null);
        }
        else if (stateInfo.normalizedTime > 0.5f && stateInfo.normalizedTime < 1f && (left || right))
        {
            if (!owner.Dash.StandUpAfterDash)
            {
                owner.Dash.StandUpAfterDash = true;
            }
            if (owner.Status.IsGrounded)
            {
                owner.DashDustGenerator.GetStopDashDust(0.1f);
            }
            owner.Dash.CrouchAfterDash = false;
            animator.Play(RunningAfterDashAnimHash);
        }

        return false;
    }
}

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
        bool rawParryKeyDown = Input.GetKeyDown(Player2Keys.Parry);
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

// The camera (ProCamera2D, the Com.LuisPedroFonseca.ProCamera2D asset the game ships in
// Assembly-CSharp-firstpass.dll) only ever tracks Core.Logic.Penitent -
// CameraManager.UpdateNewCameraParams() (called on every level load) wipes the whole target
// list and re-adds P1 alone. ProCamera2D itself already supports multiple simultaneous targets
// natively (it tracks their combined midpoint) and ships its own purpose-built extension for
// exactly the requested "shared, beat-em-up style" behavior - ProCamera2DZoomToFitTargets
// automatically zooms the camera out to keep every current target on screen, and back in as
// they get closer together - it's just never attached to the game's camera by default, since
// vanilla never has more than one target. This adds it once and keeps P2 registered as a second
// target through every level transition (Postfixing UpdateNewCameraParams, since that method
// rebuilds the target list from scratch each time) and every P2 respawn (see
// CoopLocal.OnPlayerSpawn, for the case where a respawn doesn't also trigger a full camera
// reset). GetCameraTarget(...) guards against double-adding P2 in either path -
// AddCameraTarget itself has no such guard and would otherwise create a second, competing
// target entry for the exact same Transform.
// Player2Keys puts P2's jump/attack/parry on Keypad0/1/2 (see that class's comment for why) -
// which still touches CameraPan's own numpad-driven manual camera panning (Rewired axes 20/21,
// read directly off the shared "Player 0" the same way everything else in this family does).
// EnableCameraPan is a plain public field, never reassigned anywhere in the game's own
// code after its initial Inspector-set value (confirmed - nothing else writes to it), so forcing
// it false once per CameraPan instance is permanent for that instance's lifetime; Postfixing
// Start() (rather than a one-time find-and-set from CoopLocal) means this keeps applying correctly
// across level transitions, whenever the game creates a fresh CameraPan for the new scene.
[HarmonyPatch(typeof(CameraPan), "Start")]
internal static class CameraPan_Disable_Patch
{
    private static void Postfix(CameraPan __instance)
    {
        __instance.EnableCameraPan = false;
    }
}

[HarmonyPatch(typeof(CameraManager), nameof(CameraManager.UpdateNewCameraParams))]
internal static class CameraManager_UpdateNewCameraParams_Patch
{
    private static void Postfix(CameraManager __instance) => AddPlayer2AsCameraTarget(__instance.ProCamera2D);

    internal static void AddPlayer2AsCameraTarget(ProCamera2D proCamera2D)
    {
        if (proCamera2D == null || CoopLocal.Player2 == null)
        {
            return;
        }

        if (proCamera2D.GetComponent<ProCamera2DZoomToFitTargets>() == null)
        {
            proCamera2D.gameObject.AddComponent<ProCamera2DZoomToFitTargets>();
        }

        if (proCamera2D.GetCameraTarget(CoopLocal.Player2.transform) == null)
        {
            // Same weight/offset the game itself uses for P1 in
            // CameraManager.UpdateNewCameraParams - keeps both players framed with identical
            // priority.
            proCamera2D.AddCameraTarget(CoopLocal.Player2.transform, 1f, 1f, 0f, new Vector2(0f, 6f));
        }
    }
}

// CrouchAttackBehaviour and CrouchUpBehaviour are two more Animator states in the same crouch
// state graph as CrouchDownBehaviour (already fixed above) - both with their own separate
// _penitent field, both subject to the identical bug. CrouchAttackBehaviour is the one that
// actually matters most: it's the state CrouchDownBehaviour transitions into when the crouch-
// attack key is pressed, and its OnStateEnter/OnStateUpdate is what raises the attack event
// (_penitent.AnimatorInyector.RaiseAttackEvent()), sets the damage amount
// (_penitent.CurrentOutputDamage), and toggles _penitent.IsCrouchAttacking /
// _penitent.PlatformCharacterInput.IsAttacking. On P2's first ever crouch-attack, an unfixed
// _penitent here resolves to P1 - so P2's crouch-attack animation plays, but the actual attack
// (damage, hitbox event) fires as if P1 had done it, while P1's own IsCrouchAttacking/IsAttacking
// get set to true out of nowhere - which, since PlatformCharacterInput.IsHorizontalClamped()
// includes IsAttacking in its clamp check, would also zero out P1's own movement for the
// duration. This is almost certainly why "P2 couldn't attack while P1 was crouched" persisted
// even after the CrouchDownBehaviour fix: that fix only handles entering/staying in the Crouch
// state itself, not the separate Crouch Attack state it hands off to.
[HarmonyPatch(typeof(CrouchAttackBehaviour), "OnStateEnter")]
internal static class CrouchAttackBehaviour_OnStateEnter_Patch
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

[HarmonyPatch(typeof(CrouchUpBehaviour), "OnStateEnter")]
internal static class CrouchUpBehaviour_OnStateEnter_Patch
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

// HangOnCliffLedeBehaviour and ClimbCliffLedeBehaviour are the two Animator states that make up
// cliff-ledge climbing ("cornisas") - both with the same unfixed _penitent-falls-back-to-P1 bug.
// On P2's first attempt to climb a ledge, HangOnCliffLedeBehaviour.OnStateEnter resolves
// _penitent to P1 and then does everything (IsClimbingCliffLede = true, canClimbCliffLede,
// disabling P1's 2D collision/physics, snapping P1's position to the ledge's root target...) to
// *P1* instead of P2 - meaning P2's own climb never actually starts (P2.IsClimbingCliffLede stays
// false) while P1 gets silently frozen/teleported. Same fix as every other case above.
[HarmonyPatch(typeof(HangOnCliffLedeBehaviour), "OnStateEnter")]
internal static class HangOnCliffLedeBehaviour_OnStateEnter_Patch
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

[HarmonyPatch(typeof(ClimbCliffLedeBehaviour), "OnStateEnter")]
internal static class ClimbCliffLedeBehaviour_OnStateEnter_Patch
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

// GrabCliffLede (the MonoBehaviour, one per Penitent, whose trigger collider actually *detects*
// a climbable ledge and starts the grab) has a variant of the same bug that's even more direct:
// its Start() does an *unconditional* `_penitent = Core.Logic.Penitent;` (no null-check guard),
// so P2's own copy of this component always points at P1, no matter what. Every ledge P2 walks
// into is evaluated against and applied to P1's state (_penitent.IsGrabbingCliffLede,
// .CliffLedeOrientation, .RootTargetPosition, .IsJumpingOff, .IsDashing, .Status.IsGrounded...) -
// P2 can never climb a ledge at all, since nothing ever sets P2's own IsGrabbingCliffLede.
// Because the assignment is unconditional, a Prefix can't just pre-set the field (the original
// method would immediately overwrite it back to Core.Logic.Penitent) - this corrects it
// afterwards instead. The one known gap: Start() also subscribes this component's damage
// handler to _penitent.DamageArea.OnDamaged *before* this Postfix runs, so on P2's instance that
// subscribes to P1's DamageArea instead of P2's - harmless for now since P2 takes no real damage
// at all (see PenitentDamageArea_TakeDamage_Patch below), but worth revisiting if P2 ever gets
// real health.
[HarmonyPatch(typeof(GrabCliffLede), "Start")]
internal static class GrabCliffLede_Start_Patch
{
    private static void Postfix(GrabCliffLede __instance, ref Penitent ____penitent)
    {
        Penitent owner = __instance.GetComponentInParent<Penitent>();
        if (owner != null)
        {
            ____penitent = owner;
        }
    }
}

// MudAreaEffect (the slow-down trigger zone for mud/swamp terrain) caches the *last entity
// that entered* in single shared fields (Controller/Dash/Animator, plus the "default" values
// it read from them) instead of tracking each entity in the zone separately. With two players
// able to be in the mud at once, whichever one entered last "wins" that cache; ApplyMudEffects
// (every frame anyone stays in the zone) only ever touches that one cached controller, and
// OnExitAreaEffect's reset-to-default step does the same regardless of which entity (`other`)
// is actually the one leaving - so leaving the mud can silently reset the wrong player, or
// reset the right one using stale/wrong "default" values, leaving them stuck with the mud's
// reduced jump/walk speed permanently.
//
// There's a second, independent bug on top of that one, in the *base* AreaEffect class: its
// OnTriggerExit2D sets the whole zone's IsPopulated = false the instant ANY ONE occupant leaves,
// and OnUpdate() only calls OnStayAreaEffect (the periodic mud re-apply) while IsPopulated is
// true - so the moment either player leaves a mud patch the other player is still standing in,
// the periodic re-application stops firing for them entirely. Combined with the single-entity
// cache above, this is what produces the reported "sometimes the horizontal slowdown just
// disappears, sometimes you can suddenly jump normally again" - it happens whenever the OTHER
// player crosses the mud's edge (leaving, or re-entering and re-winning the cache), not from
// dashing specifically; dashing just makes reaching that edge far more likely; a normal walk
// across the same boundary would trigger it too.
//
// Population (the list of GameObjects currently inside, on the base AreaEffect class) - unlike
// Controller/Dash/Animator/IsPopulated - IS tracked correctly per-entity by AddEntityToArea
// Population/RemoveEntityToAreaPopulation, so all three fixes below key off that list directly
// instead of trusting the single-entity cache or the zone-wide IsPopulated flag.
[HarmonyPatch(typeof(AreaEffect), "OnTriggerExit2D")]
internal static class AreaEffect_OnTriggerExit2D_Patch
{
    // Scoped to MudAreaEffect only - other AreaEffect subclasses (poison, wind, etc.) haven't
    // been reported broken and haven't been audited for the same two-occupant issue.
    private static void Postfix(AreaEffect __instance, List<GameObject> ___Population)
    {
        if (__instance is MudAreaEffect && ___Population.Count > 0)
        {
            __instance.IsPopulated = true;
        }
    }
}

// Replaces the single-cache periodic mud application with one that walks every entity actually
// in Population and applies this zone's mud values directly to each of them, every tick -
// completely independent of whichever entity OnEnterAreaEffect's shared cache last happened to
// point at.
[HarmonyPatch(typeof(MudAreaEffect), "OnStayAreaEffect")]
internal static class MudAreaEffect_OnStayAreaEffect_Patch
{
    private static bool Prefix(MudAreaEffect __instance, List<GameObject> ___Population)
    {
        foreach (GameObject populant in ___Population)
        {
            Entity entity = populant.GetComponentInParent<Entity>();
            if (entity == null)
            {
                continue;
            }

            PlatformCharacterController controller = entity.GetComponentInChildren<PlatformCharacterController>();
            if (controller == null)
            {
                continue;
            }

            controller.JumpingSpeed = __instance.JumpingSpeed;
            controller.WalkingDrag = __instance.WalkingDrag;
            controller.WalkingAcc = __instance.WalkingAcceleration;
            controller.MaxWalkingSpeed = __instance.MaxWalkingSpeed;

            Dash dash = entity.GetComponentInChildren<Dash>();
            if (dash != null)
            {
                dash.DashMoveSetting.Speed = __instance.DashSettings.Speed;
                dash.DashMoveSetting.Drag = __instance.DashSettings.Drag;
                if (entity.Animator != null)
                {
                    entity.Animator.speed = entity.Animator.GetCurrentAnimatorStateInfo(0).IsName("Run") ? 0.7f : 1f;
                }
            }
        }

        return false;
    }
}

// Rather than rewrite MudAreaEffect's whole caching scheme, this keeps its own reliable
// per-controller baseline (captured once, right at spawn, before either player could ever
// have touched mud) and reapplies it directly to whichever Penitent actually triggered
// OnExitAreaEffect, overriding whatever the buggy shared-cache logic just did. It also
// re-applies this zone's mud values to every player still left in Population right afterwards,
// undoing any collateral damage the original method's reset-to-default step may have just done
// to whichever player its stale single-entity cache happened to be pointing at.
[HarmonyPatch(typeof(MudAreaEffect), "OnExitAreaEffect")]
internal static class MudAreaEffect_OnExitAreaEffect_Patch
{
    private readonly struct Baseline(float jumpingSpeed, float walkingDrag, float walkingAcc, float maxWalkingSpeed, float dashSpeed, float dashDrag)
    {
        public readonly float JumpingSpeed = jumpingSpeed;
        public readonly float WalkingDrag = walkingDrag;
        public readonly float WalkingAcc = walkingAcc;
        public readonly float MaxWalkingSpeed = maxWalkingSpeed;
        public readonly float DashSpeed = dashSpeed;
        public readonly float DashDrag = dashDrag;
    }

    private static readonly Dictionary<Penitent, Baseline> Baselines = new Dictionary<Penitent, Baseline>();

    // Called from CoopLocal right after a Penitent spawns, before it could possibly have
    // touched any mud yet, so these values are guaranteed clean.
    internal static void RememberBaseline(Penitent penitent)
    {
        PlatformCharacterController controller = penitent.PlatformCharacterController;
        Dash dash = penitent.GetComponentInChildren<Dash>();
        Baselines[penitent] = new Baseline(
            controller.JumpingSpeed,
            controller.WalkingDrag,
            controller.WalkingAcc,
            controller.MaxWalkingSpeed,
            dash != null ? dash.DashMoveSetting.Speed : 0f,
            dash != null ? dash.DashMoveSetting.Drag : 0f);
    }

    private static void Postfix(MudAreaEffect __instance, Collider2D other, List<GameObject> ___Population)
    {
        Penitent owner = other.GetComponentInParent<Penitent>();
        if (owner != null && Baselines.TryGetValue(owner, out Baseline baseline))
        {
            PlatformCharacterController controller = owner.PlatformCharacterController;
            controller.JumpingSpeed = baseline.JumpingSpeed;
            controller.WalkingDrag = baseline.WalkingDrag;
            controller.WalkingAcc = baseline.WalkingAcc;
            controller.MaxWalkingSpeed = baseline.MaxWalkingSpeed;

            Dash dash = owner.GetComponentInChildren<Dash>();
            if (dash != null)
            {
                dash.DashMoveSetting.Speed = baseline.DashSpeed;
                dash.DashMoveSetting.Drag = baseline.DashDrag;
            }
        }

        // Population no longer contains the exiting entity by this point (AreaEffect.
        // OnTriggerExit2D removes it before calling OnExitAreaEffect) - whoever's left here is
        // still physically standing in the mud and must keep their debuff, regardless of what
        // the original method's single-entity cache just reset.
        foreach (GameObject populant in ___Population)
        {
            Entity remaining = populant.GetComponentInParent<Entity>();
            if (remaining == null)
            {
                continue;
            }

            PlatformCharacterController remainingController = remaining.GetComponentInChildren<PlatformCharacterController>();
            if (remainingController == null)
            {
                continue;
            }

            remainingController.JumpingSpeed = __instance.JumpingSpeed;
            remainingController.WalkingDrag = __instance.WalkingDrag;
            remainingController.WalkingAcc = __instance.WalkingAcceleration;
            remainingController.MaxWalkingSpeed = __instance.MaxWalkingSpeed;

            Dash remainingDash = remaining.GetComponentInChildren<Dash>();
            if (remainingDash != null)
            {
                remainingDash.DashMoveSetting.Speed = __instance.DashSettings.Speed;
                remainingDash.DashMoveSetting.Drag = __instance.DashSettings.Drag;
            }
        }
    }
}

// Round 31 - confirmed root cause of "P2 gets hit by an enemy, and P1 takes the exact same damage
// too, even standing far away from that enemy": Gameplay.GameControllers.Entities.ContactDamage
// (the framework component behind "touch this enemy and take periodic contact damage", used by
// Fool and presumably others) exposes only a single bool IsTargetOverlapped - true while *any*
// entity on DamageableLayers is touching, with no record of *which* one. Enemy-specific attack
// scripts (confirmed for FoolAttack.OnUpdate) then read that bool and, when true, call
// EnemyAttack.ContactAttack(Core.Logic.Penitent) - hardcoded to the P1 singleton, regardless of
// who's actually in contact. So while P2 stands on Fool, P1 takes repeated contact damage every
// ~0.1s no matter how far away P1 physically is - confirmed in [DashParryDebug] logs showing P1
// hit from as far as 24 units away, at the exact frame cadence and damage amounts as P2's own hits,
// with the attacker's own position tracking P2's, never P1's.
//
// Fixed at the shared choke point instead of per-enemy: EnemyAttack.ContactAttack(IDamageable) is
// the base-class method every such enemy attack script ultimately calls into, so patching there
// once covers Fool and any other enemy with the same "IsTargetOverlapped + hardcoded
// Core.Logic.Penitent" shape, without needing to find and reimplement each one's own OnUpdate.
// ContactDamageOverlapTracker independently tracks *which* Penitent(s) are really touching each
// ContactDamage component (via that component's own OnTriggerEnter2D/OnTriggerExit2D - a real,
// per-instance, position-based signal, not the single shared bool). The redirect only ever fires
// when it's positively confirmed P1 is *not* among the real touchers and someone else is - same
// "never redirect by elimination" discipline as BlockerOverrideHelper elsewhere in this file - so
// an untracked/ambiguous case just leaves the original hardcoded call alone rather than guessing.
// Round 34: the redirect above was confirmed working most of the time (log showed the vast
// majority of hardcoded-to-P1 calls correctly redirected to P2), but occasionally still let a
// hardcoded hit through with "nobody tracked as touching", and P1 took the damage anyway - while
// the user's own report (moving P2 away while it's still in its post-hit invulnerability window,
// right after touching a second enemy) points at a timing gap in the tracking itself. Tracking by
// Penitent directly (the original approach) breaks if a Penitent has more than one collider that
// can independently enter/exit this same trigger (a plausible setup - a body collider plus the
// DamageArea's own separate collider, for instance): if one of the two exits while the other is
// still inside, removing "the Penitent" from a HashSet<Penitent> keyed by player wipes out the
// correct "still touching" state contributed by the other, still-overlapping collider. Tracking by
// the actual Collider2D instead (mirroring exactly what ContactDamage's own IsTargetOverlapped
// bool is built from) and deriving "which Penitent(s) are touching" from that set on demand avoids
// this - a Penitent only ever drops out once *all* of its own colliders have actually exited.
internal static class ContactDamageOverlapTracker
{
    private static readonly Dictionary<ContactDamage, HashSet<Collider2D>> overlapping = new Dictionary<ContactDamage, HashSet<Collider2D>>();

    internal static void Add(ContactDamage source, Collider2D collider)
    {
        if (collider == null)
        {
            return;
        }
        if (!overlapping.TryGetValue(source, out HashSet<Collider2D> set))
        {
            set = new HashSet<Collider2D>();
            overlapping[source] = set;
        }
        set.Add(collider);
    }

    internal static void Remove(ContactDamage source, Collider2D collider)
    {
        if (collider == null || !overlapping.TryGetValue(source, out HashSet<Collider2D> set))
        {
            return;
        }
        set.Remove(collider);
    }

    internal static IEnumerable<Penitent> GetOverlapping(ContactDamage source)
    {
        if (!overlapping.TryGetValue(source, out HashSet<Collider2D> set) || set.Count == 0)
        {
            return new Penitent[0];
        }
        HashSet<Penitent> penitents = new HashSet<Penitent>();
        foreach (Collider2D collider in set)
        {
            // A destroyed/disabled collider can linger in the set if its own OnTriggerExit2D never
            // fired (e.g. the GameObject was deactivated instead of physically leaving the
            // trigger) - Unity's "==" on a destroyed object correctly evaluates true against null,
            // so this skips those instead of throwing or resolving a stale Penitent.
            if (collider == null)
            {
                continue;
            }
            Penitent penitent = collider.GetComponentInParent<Penitent>();
            if (penitent != null)
            {
                penitents.Add(penitent);
            }
        }
        return penitents;
    }
}

[HarmonyPatch(typeof(ContactDamage), "OnTriggerEnter2D")]
internal static class ContactDamage_OnTriggerEnter2D_Track_Patch
{
    private static void Postfix(ContactDamage __instance, Collider2D other)
    {
        ContactDamageOverlapTracker.Add(__instance, other);
    }
}

[HarmonyPatch(typeof(ContactDamage), "OnTriggerExit2D")]
internal static class ContactDamage_OnTriggerExit2D_Track_Patch
{
    private static void Postfix(ContactDamage __instance, Collider2D other)
    {
        ContactDamageOverlapTracker.Remove(__instance, other);
    }
}

[HarmonyPatch(typeof(EnemyAttack), nameof(EnemyAttack.ContactAttack))]
internal static class EnemyAttack_ContactAttack_OwnerRedirect_Patch
{
    private static void Prefix(EnemyAttack __instance, ref IDamageable damageable)
    {
        Penitent p1 = Core.Logic.Penitent;
        if (p1 == null || !(damageable is Penitent target) || target != p1)
        {
            // Only ever intervenes on the exact bug shape - a call hardcoded to P1. Any other
            // target (P2, an enemy, anything else IDamageable) is left completely alone.
            return;
        }

        // NOT __instance.GetComponentInChildren<ContactDamage>() - confirmed by testing to find
        // nothing and silently no-op the whole patch. FoolAttack.OnStart() resolves its own
        // ContactDamage reference via Fool.GetComponentInChildren<ContactDamage>() (Fool being
        // base.EntityOwner, the shared Entity root), not from FoolAttack's own transform - meaning
        // ContactDamage lives as a *sibling* component under the enemy's root, not a descendant of
        // the Attack component's own GameObject. Mirroring that exact resolution path here instead.
        if (__instance.EntityOwner == null)
        {
            DashParryDebugLog.Log($"ContactAttack redirect: no EntityOwner on {__instance.GetType().Name} (frame {Time.frameCount})");
            return;
        }
        ContactDamage contactDamage = __instance.EntityOwner.GetComponentInChildren<ContactDamage>();
        if (contactDamage == null)
        {
            DashParryDebugLog.Log($"ContactAttack redirect: no ContactDamage found under {__instance.EntityOwner.name} (frame {Time.frameCount})");
            return;
        }

        bool p1Touching = false;
        Penitent otherTouching = null;
        foreach (Penitent touching in ContactDamageOverlapTracker.GetOverlapping(contactDamage))
        {
            if (touching == p1)
            {
                p1Touching = true;
            }
            else
            {
                otherTouching = touching;
            }
        }

        if (otherTouching == null)
        {
            // Nobody else tracked as touching (including the untracked/ambiguous case) - leave the
            // original call alone, matching vanilla/solo-play behavior exactly.
            DashParryDebugLog.Log(
                $"ContactAttack redirect: hardcoded-to-P1 call from {__instance.EntityOwner.name}, but nobody tracked as " +
                $"touching {contactDamage.gameObject.name} - leaving as-is (frame {Time.frameCount})");
            return;
        }
        if (p1Touching)
        {
            // Both are genuinely touching at once - let the original call through for P1 as-is,
            // and separately attack the other player for real instead of dropping their hit.
            DashParryDebugLog.Log($"ContactAttack redirect: both P1 and {DashParryDebugLog.Label(otherTouching)} touching {contactDamage.gameObject.name} - hitting both (frame {Time.frameCount})");
            __instance.ContactAttack(otherTouching);
            return;
        }
        DashParryDebugLog.Log($"ContactAttack redirect: P1 NOT touching {contactDamage.gameObject.name}, redirecting hardcoded hit to {DashParryDebugLog.Label(otherTouching)} (frame {Time.frameCount})");
        damageable = otherTouching;
    }
}

// Ability (the base class behind Dash, Parry, Combo, VerticalAttack, and every other
// cast-based skill) has its own *generic* input dispatcher, completely separate from anything
// PlatformCharacterInput does:
//
//   private void UpdateInput()
//   {
//       if ((bool)EntityOwner && Rewired != null && EntityOwner.CompareTag("Penitent"))
//       {
//           if (Rewired.GetButtonDown(triggerCode)) Cast();
//           if (Rewired.GetButtonUp(triggerCode)) StopCast();
//       }
//   }
//
// This runs every frame for every Ability component on every Penitent - including P2's, since
// `Rewired` here is (as always) just ReInput.players.GetPlayer(0), the same shared Player 0.
// So whenever P1 presses their own real Dash/Parry/etc. button, this fires Cast()/StopCast()
// on P2's *own* Dash/Parry ability too, racing against whatever we triggered for P2 through
// PlatformCharacterInput.Dash / the ParryInput patch. That race is what produced the "both
// dash at once -> P2 crouches instead, both get stuck" bug: two independent paths both calling
// Cast()/StopCast() on the same ability in the same window, leaving castTime/animator state
// half-updated.
//
// P2's own casting is already fully covered elsewhere (Dash via AnimatorInyector reading
// PlatformCharacterInput.Dash, Parry via the ParryInput patch above), so this dispatcher adds
// nothing for P2 except cross-talk from P1's buttons - safe to disable outright for any Ability
// living on P2. Abilities we haven't explicitly wired for P2 yet simply won't be castable
// through this path either, which is a gap to close later, not a new bug.
[HarmonyPatch(typeof(Ability), "UpdateInput")]
internal static class Ability_UpdateInput_Patch
{
    private static bool Prefix(Ability __instance)
    {
        return __instance.EntityOwner != CoopLocal.Player2;
    }
}

// P2 used to be made invulnerable here rather than wired into the death/respawn flow (see
// Modding/NOTES.md history) - a Prefix no-op'd TakeDamage for P2 entirely. Per the user's request
// for P2 to have its own real health pool, that skip is removed: P2 now takes damage through the
// exact same code path P1 always has. This is safe to just turn on, for two reasons already
// established earlier in this file: (1) the component itself is never destroyed (the historical
// reason invulnerability was added this way instead of destroying PenitentDamageArea outright -
// ~108 places in the game's own code call methods on Penitent.DamageArea assuming it always
// exists), so nothing here changes; (2) Stats.Life is a genuinely per-instance value
// (VariableAttribute's constructor sets Current = baseValue, i.e. the prefab's own serialized
// starting-life field) - P2 already has its own separate, correctly-initialized life pool the
// moment it spawns, no extra setup needed. If P2's own Status.Dead ever becomes true,
// Penitent.OnUpdate() (completely unmodified, runs per-instance for both P1 and P2 alike) already
// calls Core.Logic.SetState(LogicStates.PlayerDead) exactly like it does when P1 dies - so either
// player dying ends the run the same way solo play always has, entirely for free.
//
// One thing this does need to guard against: PenitentDamageArea.RaiseDamageEvent unconditionally
// writes `_logicManager.PlayerCurrentLife = _penitent.Stats.Life.Current;` - a single global value
// that looked, at first glance, like what the HUD's health bar reads to decide what to display.
// With P2 now able to take real damage, its hits would stomp this with *P2's* life number. See
// PenitentDamageArea_RaiseDamageEvent_HudFix_Patch below for the fix - kept even after confirming
// (decompiling Gameplay.UI.Others.UIGameLogic.PlayerHealth) that P1's actual on-screen bar reads
// Core.Logic.Penitent.Stats.Life directly, never LogicManager.PlayerCurrentLife, so this specific
// write was never the cause of any observed HUD bug. Left in place in case something else in the
// game's own code does read PlayerCurrentLife (unconfirmed either way) - harmless either way,
// since it's just restoring the value to what it already should be.
//
// Known limitation, not fixed here: P2 starts at its own LifeBase (a fresh-save starting value),
// not P1's current (possibly upgraded) max life - the two pools aren't kept in sync with whatever
// life-upgrade items P1 has collected during the playthrough. Revisit if that turns out to matter.
// Diagnostic for the round-30 report "hitting P2 after its invulnerability window ends damages
// *both* players from what looks like one hit". Logs every real TakeDamage call that gets past
// the early-out guards (CanTakeHit/recover-time), tagged with which player's own DamageArea it
// ran on, the hit's source, and a frame number - so a genuine "one enemy swing tagging both
// players' separate, real DamageArea colliders because they're standing in the same spot"
// (expected: P1 and P2 have no collision between them, per CoopLocal.OnPlayerSpawn, so nothing
// stops them occupying the same space) can be told apart from an actual bug (e.g. two calls
// against the same instance, or a call whose owner doesn't match the DamageArea it ran on) just
// by reading the timestamps and owners next to each other in the log.
//
// Round 31: the first log confirmed each hit only reduces the correct player's own Life.Current
// (no shared/duplicated line ever appeared), and P1/P2 hits from the same enemy landed a handful
// of frames apart, not the same frame - consistent with "both standing near the same enemy, two
// separate real hits". The user then said P1 was reportedly *far* from the enemy when this
// happened, which the "standing together" theory doesn't explain - so positions (owner, the other
// player, and the attacker, when available) are now logged alongside the life numbers, to settle
// with actual distances instead of guessing further.
[HarmonyPatch(typeof(PenitentDamageArea), "TakeDamage")]
internal static class PenitentDamageArea_TakeDamage_DebugLog_Patch
{
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(PenitentDamageArea), "_penitent");

    private static float lifeBefore;

    private static void Prefix(PenitentDamageArea __instance)
    {
        Penitent owner = PenitentField.GetValue(__instance) as Penitent;
        lifeBefore = owner != null ? owner.Stats.Life.Current : -1f;
    }

    // Only logs when Life.Current actually changed - TakeDamage has several early-out guards
    // (CanTakeHit, recover-time window) that make it return without applying anything, and a
    // Postfix fires regardless of which path was taken inside. Comparing life before/after is a
    // reliable way to tell "damage genuinely landed" apart from a no-op call, without needing to
    // duplicate TakeDamage's own gating logic here.
    private static void Postfix(PenitentDamageArea __instance, Gameplay.GameControllers.Entities.Hit hit)
    {
        Penitent owner = PenitentField.GetValue(__instance) as Penitent;
        float lifeAfter = owner != null ? owner.Stats.Life.Current : -1f;
        if (Mathf.Approximately(lifeAfter, lifeBefore))
        {
            return;
        }
        string ownerLabel = DashParryDebugLog.Label(owner);
        string attackerName = hit.AttackingEntity != null ? hit.AttackingEntity.name : "null";

        Penitent p1 = Core.Logic.Penitent;
        Penitent p2 = CoopLocal.Player2;
        Penitent other = (owner == p2) ? p1 : p2;
        string ownerPos = owner != null ? owner.transform.position.ToString("F1") : "?";
        string otherLabel = DashParryDebugLog.Label(other);
        string otherPos = other != null ? other.transform.position.ToString("F1") : "?";
        float distanceToOther = (owner != null && other != null) ? Vector3.Distance(owner.transform.position, other.transform.position) : -1f;
        string attackerPos = hit.AttackingEntity != null ? hit.AttackingEntity.transform.position.ToString("F1") : "?";

        DashParryDebugLog.Log(
            $"PenitentDamageArea.TakeDamage APPLIED on {ownerLabel} (instance={__instance.GetInstanceID()}) from attacker='{attackerName}' " +
            $"damageType={hit.DamageType} lifeBefore={lifeBefore:F1} lifeAfter={lifeAfter:F1} | {ownerLabel}Pos={ownerPos} " +
            $"{otherLabel}Pos={otherPos} distanceToOther={distanceToOther:F1} attackerPos={attackerPos} (frame {Time.frameCount})");
    }
}

[HarmonyPatch(typeof(PenitentDamageArea), "RaiseDamageEvent")]
internal static class PenitentDamageArea_RaiseDamageEvent_HudFix_Patch
{
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(PenitentDamageArea), "_penitent");
    private static readonly FieldInfo LogicManagerField = AccessTools.Field(typeof(PenitentDamageArea), "_logicManager");

    private static void Postfix(object __instance)
    {
        Penitent penitent = PenitentField.GetValue(__instance) as Penitent;
        if (penitent == null || penitent != CoopLocal.Player2)
        {
            return;
        }
        Penitent p1 = Core.Logic.Penitent;
        LogicManager logicManager = LogicManagerField.GetValue(__instance) as LogicManager;
        if (p1 != null && logicManager != null)
        {
            logicManager.PlayerCurrentLife = p1.Stats.Life.Current;
        }
    }
}

// Second HUD health bar for P2, per the user's request ("reutilizar el hud de P1 y ponerlo abajo
// con un tamaño reducido"). Gameplay.UI.Others.UIGameLogic.PlayerHealth is a single HUD widget
// hardcoded to read Core.Logic.Penitent - there's no per-Penitent instancing built into it - so
// the second bar has to be a real runtime clone of the same GameObject (Unity's Instantiate()
// correctly remaps a cloned hierarchy's own internal SerializeField references - health/loss
// Image, backgroundMid/backgroundFillTransform RectTransform - to point at the clone's own
// children, not the original's), then redirected via the patches below wherever it reads
// Core.Logic.Penitent. CalculateLossBar()/CalculateHealthBar() aren't patched directly - both
// only depend on BarTarget (redirected below) and this instance's own Image fields (already
// correctly re-pointed by Instantiate), so they work correctly through the clone unmodified.
//
// Positioning: anchored to the top-right corner of whatever Canvas the original bar lives in
// The top-right-corner attempt anchored the clone relative to original.transform.parent directly
// ("Health Bar") - if that's a small sub-container rather than the actual screen-sized Canvas,
// anchoring to its own (1,1) corner lands wherever that container happens to sit, not the screen's
// corner - which is almost certainly why it showed up far to one side instead. Now walks up to the
// outermost Canvas ancestor and parents the clone there, then centers it on screen for now (per
// the user's own suggestion) purely to visually confirm the clone mechanism itself works before
// worrying about a less obtrusive final position.
internal static class Player2HealthBar
{
    private const float Scale = 0.65f;

    private static readonly MethodInfo OnPenitentReadyMethod = AccessTools.Method(typeof(PlayerHealth), "OnPenitentReady");

    internal static PlayerHealth Instance { get; private set; }

    // Cached on first use and never looked up again. Object.Destroy() only *marks* a GameObject
    // for destruction - the real removal happens at the end of the current frame - so calling
    // FindObjectOfType<PlayerHealth>() again right after destroying the previous clone (same
    // synchronous call, same frame) would still find that not-yet-actually-gone clone, since at
    // that instant there are legitimately two PlayerHealth components in the scene and nothing
    // besides object identity tells them apart. Confirmed exactly this way in the field: the
    // second and third respawns each cloned from the *previous* P2 clone instead of P1's real
    // bar, compounding the Offset/Scale adjustment every time (position drifting down another 40
    // units and shrinking another 0.65x per respawn) until it was scaled down and pushed off
    // enough to be effectively invisible. The real original bar is a stable, persistent UI
    // element that's never destroyed - so finding it once, ever, and reusing that same reference
    // for every later respawn is both correct and simpler than trying to filter it out by name.
    private static PlayerHealth originalCache;

    // The clone's root is now "Health Bar" (the whole decorated container - see EnsureCreated),
    // not the "Bar" sub-object PlayerHealth itself lives on, so Instance.gameObject alone is no
    // longer the right thing to destroy on the next respawn - that would only remove the inner
    // "Bar" and leave the outer "Health Bar" wrapper (and any decorative siblings) orphaned in the
    // scene forever. Tracked separately instead of trying to derive it from Instance each time.
    private static GameObject instanceRoot;

    private static void LogChildren(string label, Transform parent)
    {
        System.Text.StringBuilder log = new System.Text.StringBuilder();
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            Graphic graphic = child.GetComponent<Graphic>();
            log.Append($"[{i}] '{child.name}' active={child.gameObject.activeSelf} hasGraphic={graphic != null} ");
        }
        DashParryDebugLog.Log($"Player2HealthBar.EnsureCreated: {label}: {log}");
    }

    internal static void EnsureCreated(Penitent p2)
    {
        if (instanceRoot != null)
        {
            UnityEngine.Object.Destroy(instanceRoot);
            instanceRoot = null;
            Instance = null;
        }

        if (originalCache == null)
        {
            originalCache = UnityEngine.Object.FindObjectOfType<PlayerHealth>();
        }
        PlayerHealth original = originalCache;
        if (original == null || p2 == null)
        {
            DashParryDebugLog.Log($"Player2HealthBar.EnsureCreated: aborted - original PlayerHealth found={original != null}, p2 found={p2 != null}");
            return;
        }

        // Anchoring/insetting only means "screen corner" if the parent itself is the full-screen
        // Canvas. The previous attempt anchored the clone to (1,1) of original.transform.parent
        // directly ("Health Bar") - if that's actually a small sub-container hugging one part of
        // the HUD rather than the screen-sized Canvas itself, (1,1) means "top-right of that small
        // container", which could visually land almost anywhere, including off to one side - which
        // is what the user saw. Walking up to the outermost Canvas ancestor and parenting the
        // clone there instead makes the anchor genuinely relative to the whole screen.
        Canvas canvas = original.GetComponentInParent<Canvas>();
        while (canvas != null && canvas.transform.parent != null)
        {
            Canvas parentCanvas = canvas.transform.parent.GetComponentInParent<Canvas>();
            if (parentCanvas == null)
            {
                break;
            }
            canvas = parentCanvas;
        }
        Transform cloneParent = canvas != null ? canvas.transform : original.transform.parent;

        // Round 32: the user reported the clone looks like "a piece of the real sprite", not a
        // complete bar - cloning only original.gameObject ("Bar", the PlayerHealth component's own
        // GameObject) was the suspect, since a polished HUD bar is often composed of an ornate
        // frame/border as a *sibling* decoration next to the bare fill-mechanism object, not a
        // child of it - "Bar" holds the fill Images (health/loss/background* are all its own
        // children, per PlayerHealth's own fields) but the decorative frame around it could easily
        // live one level up, as another child of "Health Bar" alongside "Bar". Logging every
        // sibling under "Health Bar" (name/active/whether it renders anything) to see what's
        // actually there, and cloning that whole parent container instead of just "Bar" so nothing
        // decorative gets left behind.
        Transform originalParent = original.transform.parent;
        if (originalParent != null)
        {
            LogChildren("'Health Bar' children", originalParent);

            // Round 33: "Health Bar" itself only has 'Health Fills' and 'Bar' as children - no
            // frame/icon in there. The user confirmed the clone shows *some* bar but still lacks
            // the decorative border and the Penitent portrait icon P1's real HUD shows alongside
            // it - meaning those live even further out, as *siblings of "Health Bar" itself* under
            // whatever groups the whole HUD widget (icon + bar + frame) together, not inside it.
            // Logging one level further up to find them before guessing what to clone next.
            Transform grandparent = originalParent.parent;
            if (grandparent != null)
            {
                LogChildren("'Health Bar' siblings (under '" + grandparent.name + "')", grandparent);
            }
        }
        GameObject sourceToClone = originalParent != null ? originalParent.gameObject : original.gameObject;

        GameObject cloneObject = UnityEngine.Object.Instantiate(sourceToClone, cloneParent);
        cloneObject.name = "PlayerHealth_P2";
        instanceRoot = cloneObject;
        Instance = cloneObject.GetComponentInChildren<PlayerHealth>();

        RectTransform originalRect = (originalParent != null ? originalParent : original.transform) as RectTransform;
        RectTransform rect = cloneObject.GetComponent<RectTransform>();
        DashParryDebugLog.Log(
            $"Player2HealthBar.EnsureCreated: cloned from '{sourceToClone.name}' (parent={(original.transform.parent != null ? original.transform.parent.name : "none")}, " +
            $"canvasRoot={(canvas != null ? canvas.gameObject.name : "not found")}, active={sourceToClone.activeInHierarchy}, componentEnabled={original.enabled}, " +
            $"original anchorMin={originalRect?.anchorMin} anchorMax={originalRect?.anchorMax} pivot={originalRect?.pivot} anchoredPosition={originalRect?.anchoredPosition} sizeDelta={originalRect?.sizeDelta}) " +
            $"-> clone active={cloneObject.activeInHierarchy}, hasRectTransform={rect != null}, foundPlayerHealth={Instance != null}" +
            (rect != null ? $", anchoredPosition={rect.anchoredPosition}, localScale={rect.localScale}" : ""));
        if (rect != null)
        {
            // Back to dead center for now (round 33, per the user's own request) - the bottom-right
            // placement ran partly off-screen and is hard to iterate on, while the decoration/icon
            // pieces (see the sibling logging above) are still being tracked down. Once the visual
            // is actually complete, revisit final positioning (bottom-right was the ask) as its own
            // separate step - don't fold that back in until the bar itself looks right.
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.localScale *= Scale;
            DashParryDebugLog.Log($"Player2HealthBar.EnsureCreated: positioned clone at screen center for testing, anchoredPosition={rect.anchoredPosition}, localScale={rect.localScale}");
        }

        // The clone's own Awake() already subscribed its own OnPenitentReady to the shared
        // SpawnManager.OnPlayerSpawn static event (the same one CoopLocal itself hooks) - but
        // that event has already finished firing for this spawn by the time we get here (we're
        // running from inside CoopLocal's own handler for it), so the clone would otherwise sit
        // completely unwired until the *next* time P1 respawns. Call it once, right now,
        // ourselves instead - PlayerHealth_OnPenitentReady_P2_Patch redirects the argument to P2
        // for this specific instance regardless of what gets passed in, including this call.
        OnPenitentReadyMethod.Invoke(Instance, new object[] { p2 });
    }
}

[HarmonyPatch(typeof(PlayerHealth), "OnPenitentReady")]
internal static class PlayerHealth_OnPenitentReady_P2_Patch
{
    private static void Prefix(PlayerHealth __instance, ref Penitent penitent)
    {
        if (__instance == Player2HealthBar.Instance && CoopLocal.Player2 != null)
        {
            penitent = CoopLocal.Player2;
        }
    }
}

// Root cause of "the clone shows up centered but never displays real info" (see Modding/NOTES.md):
// BarTarget is a small *private* property, and CalculateLossBar()/CalculateHealthBar() call it
// internally, from methods in the exact same class, on `this`. That's precisely the shape the
// Mono JIT is most likely to inline directly into the caller's compiled code - the same "trivial
// property inlines past a Harmony Postfix on its getter" gotcha already found once in this file
// for PlatformCharacterInput.Blocked (see BlockerOverrideHelper's comment). Patching the getter
// here still matters for any genuinely external caller, but for the clone's own Update() loop -
// which only ever calls CalculateLossBar()/CalculateHealthBar() on itself - it likely never goes
// through this patched getter at all, so the P2 clone's fill Images kept lerping toward P1's
// BarTarget (Core.Logic.Penitent's own ratio) instead of P2's. Left in place for any external
// caller, but PlayerHealth_CalculateLossBar_P2_Patch/PlayerHealth_CalculateHealthBar_P2_Patch
// below are the actual fix, using the same reimplement-the-caller approach already proven for
// CalculateHealthBarSize() just below this.
[HarmonyPatch(typeof(PlayerHealth), "BarTarget", MethodType.Getter)]
internal static class PlayerHealth_BarTarget_P2_Patch
{
    private static string lastLoggedState;

    private static void Postfix(PlayerHealth __instance, ref float __result)
    {
        if (__instance != Player2HealthBar.Instance)
        {
            return;
        }
        Penitent p2 = CoopLocal.Player2;
        __result = (p2 != null) ? (p2.Stats.Life.Current / p2.Stats.Life.Final) : 0f;

        // Diagnostic for "the clone shows up but doesn't display real info" - if Life.Final is 0
        // or NaN at this point, __result itself becomes 0/NaN/Infinity, which would make the fill
        // Images collapse to nothing even though the bar's background/frame sprite is still
        // visible - looking exactly like "a sprite with no info" instead of a missing bar.
        string state = p2 != null ? $"Life.Current={p2.Stats.Life.Current:F1} Life.Final={p2.Stats.Life.Final:F1} BarTarget={__result:F3}" : "p2 is null";
        if (state != lastLoggedState)
        {
            lastLoggedState = state;
            DashParryDebugLog.Log($"Player2HealthBar.BarTarget: {state}");
        }
    }
}

// CalculateHealthBarSize() reads Core.Logic.Penitent as a bare local variable (not exposed via
// any field), so it can't be redirected with a simple Postfix the way BarTarget's getter is -
// reimplemented instead, substituting P2 for Core.Logic.Penitent, against the clone's own private
// fields via reflection.
[HarmonyPatch(typeof(PlayerHealth), "CalculateHealthBarSize")]
internal static class PlayerHealth_CalculateHealthBarSize_P2_Patch
{
    private static readonly FieldInfo LastBarWidthField = AccessTools.Field(typeof(PlayerHealth), "lastBarWidth");
    private static readonly FieldInfo BackgroundStartSizeField = AccessTools.Field(typeof(PlayerHealth), "backgroundStartSize");
    private static readonly FieldInfo EndFillSizeField = AccessTools.Field(typeof(PlayerHealth), "endFillSize");
    private static readonly FieldInfo BackgroundMidField = AccessTools.Field(typeof(PlayerHealth), "backgroundMid");
    private static readonly FieldInfo HealthTransformField = AccessTools.Field(typeof(PlayerHealth), "healthTransform");
    private static readonly FieldInfo LossTransformField = AccessTools.Field(typeof(PlayerHealth), "lossTransform");
    private static readonly FieldInfo BackgroundFillTransformField = AccessTools.Field(typeof(PlayerHealth), "backgroundFillTransform");

    private static bool Prefix(PlayerHealth __instance)
    {
        if (__instance != Player2HealthBar.Instance)
        {
            return true;
        }
        Penitent p2 = CoopLocal.Player2;
        if (p2 == null)
        {
            return false;
        }

        float final = p2.Stats.Life.Final;
        float lastBarWidth = (float)LastBarWidthField.GetValue(__instance);
        if (final == lastBarWidth)
        {
            return false;
        }
        LastBarWidthField.SetValue(__instance, final);

        float backgroundStartSize = (float)BackgroundStartSizeField.GetValue(__instance);
        float endFillSize = (float)EndFillSizeField.GetValue(__instance);
        float num = Mathf.Max(final - backgroundStartSize - endFillSize, 0f);

        RectTransform backgroundMid = (RectTransform)BackgroundMidField.GetValue(__instance);
        RectTransform healthTransform = (RectTransform)HealthTransformField.GetValue(__instance);
        RectTransform lossTransform = (RectTransform)LossTransformField.GetValue(__instance);
        RectTransform backgroundFillTransform = (RectTransform)BackgroundFillTransformField.GetValue(__instance);

        backgroundMid.sizeDelta = new Vector2(num, backgroundMid.sizeDelta.y);
        lossTransform.sizeDelta = new Vector2(final, lossTransform.sizeDelta.y);
        healthTransform.sizeDelta = new Vector2(final, healthTransform.sizeDelta.y);
        backgroundFillTransform.sizeDelta = new Vector2(final, healthTransform.sizeDelta.y);
        DashParryDebugLog.Log(
            $"Player2HealthBar.CalculateHealthBarSize: final={final:F1} backgroundStartSize={backgroundStartSize:F1} endFillSize={endFillSize:F1} " +
            $"-> backgroundMid.sizeDelta={backgroundMid.sizeDelta} healthTransform.sizeDelta={healthTransform.sizeDelta}");
        return false;
    }
}

// The actual fix for the clone showing a frame but no fill (see the comment on
// PlayerHealth_BarTarget_P2_Patch above for why patching the getter alone doesn't reach these two
// callers): reimplemented against the clone's own private fields via reflection, computing the
// target ratio from P2's own stats directly instead of going through the (likely-inlined) private
// BarTarget property at all - same approach already proven for CalculateHealthBarSize().
[HarmonyPatch(typeof(PlayerHealth), "CalculateLossBar")]
internal static class PlayerHealth_CalculateLossBar_P2_Patch
{
    private static readonly FieldInfo LossField = AccessTools.Field(typeof(PlayerHealth), "loss");
    private static readonly FieldInfo CurveField = AccessTools.Field(typeof(PlayerHealth), "HealthLossAnimationCurve");
    private static readonly FieldInfo DamageTimeElapsedField = AccessTools.Field(typeof(PlayerHealth), "_damageTimeElapsed");

    private static bool Prefix(PlayerHealth __instance)
    {
        if (__instance != Player2HealthBar.Instance)
        {
            return true;
        }
        Penitent p2 = CoopLocal.Player2;
        if (p2 == null)
        {
            return false;
        }

        float target = p2.Stats.Life.Current / p2.Stats.Life.Final;
        Image loss = (Image)LossField.GetValue(__instance);
        if (!Mathf.Approximately(loss.fillAmount, target))
        {
            float elapsed = (float)DamageTimeElapsedField.GetValue(__instance) + Time.deltaTime;
            DamageTimeElapsedField.SetValue(__instance, elapsed);
            AnimationCurve curve = (AnimationCurve)CurveField.GetValue(__instance);
            loss.fillAmount = Mathf.Lerp(loss.fillAmount, target, curve.Evaluate(elapsed));
        }
        return false;
    }
}

[HarmonyPatch(typeof(PlayerHealth), "CalculateHealthBar")]
internal static class PlayerHealth_CalculateHealthBar_P2_Patch
{
    private static readonly FieldInfo HealthField = AccessTools.Field(typeof(PlayerHealth), "health");
    private static readonly FieldInfo SpeedField = AccessTools.Field(typeof(PlayerHealth), "speed");
    private static readonly FieldInfo DamageTimeElapsedField = AccessTools.Field(typeof(PlayerHealth), "_damageTimeElapsed");

    // Diagnostic for the round-30 report "still looks like one shared bar, P1's, drops when P2 is
    // hit" - if this Prefix is genuinely running and reading P2's own numbers (which it should,
    // being a direct Prefix on the real method Update() calls, not a getter that could be
    // JIT-inlined past), the log below should show *this instance*'s (the clone's) target ratio
    // tracking P2's own Stats.Life independently of whatever P1's real bar is doing. If this line
    // never appears at all, the Prefix isn't running (return-true path / __instance mismatch,
    // worth knowing directly instead of guessing further).
    private static float lastLoggedTarget = -1f;

    private static bool Prefix(PlayerHealth __instance)
    {
        if (__instance != Player2HealthBar.Instance)
        {
            return true;
        }
        Penitent p2 = CoopLocal.Player2;
        if (p2 == null)
        {
            return false;
        }

        float target = p2.Stats.Life.Current / p2.Stats.Life.Final;
        if (!Mathf.Approximately(target, lastLoggedTarget))
        {
            lastLoggedTarget = target;
            DashParryDebugLog.Log(
                $"Player2HealthBar.CalculateHealthBar: instance={__instance.GetInstanceID()} P2.Life.Current={p2.Stats.Life.Current:F1} " +
                $"P2.Life.Final={p2.Stats.Life.Final:F1} target={target:F3} (frame {Time.frameCount})");
        }

        Image health = (Image)HealthField.GetValue(__instance);
        if (!Mathf.Approximately(health.fillAmount, target))
        {
            float elapsed = (float)DamageTimeElapsedField.GetValue(__instance) + Time.deltaTime;
            DamageTimeElapsedField.SetValue(__instance, elapsed);
            float speed = (float)SpeedField.GetValue(__instance);
            health.fillAmount = Mathf.Lerp(health.fillAmount, target, elapsed * speed);
        }
        return false;
    }
}

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

        __result = Input.GetKeyDown(Player2Keys.Parry);
        return false;
    }
}

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

// AttackBehaviour bundles `_penitentAttackArea = _penitent.PenitentAttack.CurrentPenitentWeapon
// .AttackAreas[0];` inside the same "if (_penitent == null)" guard as _penitent itself - so the
// fix has to replicate BOTH assignments together, against the real owner, the first time (and
// only the first time, matching the original's once-only intent) this instance's _penitent is
// still unset. A plain "always overwrite _penitent" Prefix would make the original's own guard
// permanently see it as already-set and skip _penitentAttackArea forever, which is exactly what the
// generic scanner did and crashed on (see comment above).
[HarmonyPatch(typeof(AttackBehaviour), "OnStateEnter")]
internal static class AttackBehaviour_OnStateEnter_Patch
{
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(AttackBehaviour), "_penitent");
    private static readonly FieldInfo AttackAreaField = AccessTools.Field(typeof(AttackBehaviour), "_penitentAttackArea");

    private static void Prefix(AttackBehaviour __instance, Animator animator)
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
        AttackAreaField.SetValue(__instance, owner.PenitentAttack.CurrentPenitentWeapon.AttackAreas[0]);
    }
}

// Same shape of fix for HurtSubStateBehaviour, which bundles
// `_throwBack = _penitent.GetComponentInChildren<ThrowBack>();` inside its own _penitent guard -
// confirmed crashing (NullReferenceException on _throwBack.Casting in OnStateEnter) under the
// generic scanner the first time either player got hurt.
[HarmonyPatch(typeof(HurtSubStateBehaviour), "OnStateEnter")]
internal static class HurtSubStateBehaviour_OnStateEnter_Patch
{
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(HurtSubStateBehaviour), "_penitent");
    private static readonly FieldInfo ThrowBackField = AccessTools.Field(typeof(HurtSubStateBehaviour), "_throwBack");

    private static void Prefix(HurtSubStateBehaviour __instance, Animator animator)
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
        ThrowBackField.SetValue(__instance, owner.GetComponentInChildren<ThrowBack>());
    }
}

// GroundHurtBehaviour and AirHurtBehaviour - the two StateMachineBehaviours actually entered when
// a hit lands (children states of the sub-state machine HurtSubStateBehaviour dispatches into,
// grounded vs airborne) - have the exact same simple "_penitent falls back to Core.Logic.Penitent"
// bug as everything else in this family, just never audited/patched until now. Neither bundles a
// second field init inside its own null-check (read in full before writing this - see the trap
// comment above), so the plain preset-in-Prefix fix is safe here. This is very likely the real
// cause of "damage/knockback still happens to P1 when P2 gets hit": the first time P2's own
// GroundHurtBehaviour/AirHurtBehaviour instance ever runs, its _penitent resolves to P1 and stays
// wrong forever - so every later hit P2 takes calls _penitent.DamageArea.HitDisplacement(...),
// sets _penitent.Status.Unattacable = true (a brief invulnerability window), stops
// _penitent.MotionLerper, etc. on *P1*, not on P2 - even though the underlying life-number
// reduction (Entity.Damage, via PenitentDamageArea.RaiseDamageEvent) is correctly per-instance and
// already only affects the player who was actually hit.
[HarmonyPatch(typeof(GroundHurtBehaviour), "OnStateEnter")]
internal static class GroundHurtBehaviour_OnStateEnter_Patch
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

[HarmonyPatch(typeof(AirHurtBehaviour), "OnStateEnter")]
internal static class AirHurtBehaviour_OnStateEnter_Patch
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

// Fervour turned out to have its own, different-shaped bug: PenitentSword.OnEnemyDamaged is
// subscribed to EnemyDamageArea.OnDamagedGlobal - a *static* event, combined via Delegate.Combine
// in OnAwake - the exact same family-3 pattern already found and fixed once for GrabLadder's
// subscription to FloorDistanceChecker.OnStepLadder (see Modding/NOTES.md). Both P1's and P2's own
// PenitentSword instances subscribe their own instance method to this one shared event, so *every*
// enemy hit - dealt by either player - invokes *both* players' OnEnemyDamaged. Each call
// unconditionally grants its own _penitent Fervour (_penitent.IncrementFervour(hit) - Fervour
// itself is genuinely per-instance, see NOTES.md) and pokes the shared
// Core.InventoryManager.OnDamageInflicted(hit) tracker - so landing one hit with P2 also grants
// Fervour to P1 (and vice versa), and the inventory/on-hit-effect tracker fires twice per hit
// instead of once. PenitentSword's own _penitent (GetComponentInParent<Penitent>() in OnAwake) is
// already correctly per-instance - the missing piece is that the callback never checks whether the
// Hit it received was actually dealt by *its own* _penitent. hit.AttackingEntity is set by
// PenitentAttack (PenitentAttack._penitent, resolved from base.EntityOwner - correctly
// per-instance) to the attacker's own gameObject, so comparing against that is enough to tell
// "my hit" from "the other player's hit" without any new tracking state.
[HarmonyPatch(typeof(PenitentSword), "OnEnemyDamaged")]
internal static class PenitentSword_OnEnemyDamaged_OwnerFilter_Patch
{
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(PenitentSword), "_penitent");

    private static bool Prefix(PenitentSword __instance, Gameplay.GameControllers.Entities.Hit hit)
    {
        Penitent owner = PenitentField.GetValue(__instance) as Penitent;
        if (owner == null || hit.AttackingEntity == null)
        {
            return true;
        }
        return hit.AttackingEntity == owner.gameObject;
    }
}

// Found while investigating round 30/31's damage-sharing reports (not confirmed to be their
// cause, but a real bug in its own right): Penitent.OnAwake subscribes each instance's own
// OnEntityDead to the *static* Entity.Death event - same family-3 shape as PenitentSword above,
// just on a different event. Entity.Death fires for *any* entity dying, players included, so when
// P2 dies, *both* P1's and P2's own OnEntityDead handlers run with entity=P2. The enemy-death
// branch (Purge gain) is harmless either way since `entity as Enemy` is null for a dead Penitent -
// but the player-death branch runs unconditionally on `this` (EnableAbilities(false),
// EnableTraits(false), DamageArea.IncludeEnemyLayer(false)), regardless of whether `this` is the
// player who actually died. So P2 dying was also disabling P1's own abilities/traits (and vice
// versa) - a real, separate cross-talk bug, distinct from the damage/Fervour ones already fixed.
// Filtered the same way: skip the whole method when the Entity that died is a Penitent that isn't
// this instance - the enemy-death branch is untouched (still fires for every player on every
// enemy kill, matching solo-play Purge behavior, since that wasn't reported as a problem).
[HarmonyPatch(typeof(Penitent), "OnEntityDead")]
internal static class Penitent_OnEntityDead_OwnerFilter_Patch
{
    private static bool Prefix(Penitent __instance, Entity entity)
    {
        Penitent diedPenitent = entity as Penitent;
        if (diedPenitent != null && diedPenitent != __instance)
        {
            return false;
        }
        return true;
    }
}
