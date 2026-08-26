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

        bool left = Player2Input.Left;
        bool right = Player2Input.Right;

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

        bool left = Player2Input.Left;
        bool right = Player2Input.Right;
        bool crouchAxis = Player2Input.Down;
        bool attackUpAxis = Player2Input.Up;
        bool jumpHeld = Player2Input.JumpHeld;
        bool attackPressed = Player2Input.AttackDown;
        bool attackReleased = Player2Input.AttackUp;
        bool parryPressed = Player2Input.ParryDown;

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


