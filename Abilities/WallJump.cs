using Com.LuisPedroFonseca.ProCamera2D;
using CreativeSpore.SmartColliders;
using DG.Tweening;
using Framework.FrameworkCore;
using Framework.Managers;
using System;
using System.Collections;
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

// WallJump (Gameplay.GameControllers.Penitent.Abilities.WallJump) - jump toward a wall and hold
// Attack to stick to it - was never audited before. Two separate bugs found reading the decompiled
// source directly:
//
//   Family 2 (Rewired compartido): OnStart() does `Rewired = ReInput.players.GetPlayer(0)` - the
//   one shared Rewired "Player 0" for the whole process, same as everywhere else in this family.
//   Every trigger in OnUpdate() (GetButton(5) = Attack held, GetButtonDown(6) = Jump pressed,
//   GetButton(65) = an early "let go" cancel gesture) reads it directly, so P2's own copy of this
//   component only ever reacts to P1's real keyboard/controller.
//
//   Family 1-style hardcode (not the lazy-null-fallback shape, the flat "always P1" shape already
//   catalogued in NOTES.md for Parry.StartParry/Penitent.Damage): inside the stick-on branch,
//   `Core.Logic.Penitent.Audio.SetParametersValuesByWall(...)` and
//   `playerStickedOrientation = Core.Logic.Penitent.Status.Orientation` both read the P1 singleton
//   instead of `base.EntityOwner` - which the rest of the method already uses correctly. The same
//   hardcode also shows up inside the private Stick() method (`Core.Logic.Penitent.SetOrientation
//   (playerStickedOrientation)`).
//
// Everything else in OnUpdate()/Stick()/Detach()/JumpOff()/ResetWallJumpStatus() already reads
// CharacterController/CharacterInput/EntityOwner, all correctly wired per-instance already (same
// prefab, same pattern as DamageArea/Stats elsewhere) - so unlike Parry/Dash, most of this class
// didn't need touching at all. Fixed the same way as those two: Prefix on OnUpdate that, for P2
// only, skips the original entirely and reimplements it with Player2Input reads and the real
// owner substituted in. Stick()/Detach()'s bodies are inlined here rather than called via
// reflection, both because Stick() needed its own fix anyway and because it keeps every private
// field access in one place.
//
// Known, deliberate gap: the early "let go of the wall" cancel gesture (Rewired button 65 in
// vanilla, CheckCancelHook()/UnHang()) has no equivalent P2 mapping yet - nothing else in this mod
// uses that button index, so rather than guess a keybind, P2 simply doesn't get that shortcut for
// now. Detach() (triggered by Jump) is still P2's normal way off the wall. Revisit if reported.
[HarmonyPatch(typeof(WallJump), "OnUpdate")]
internal static class WallJump_OnUpdate_P2_Patch
{
    private static readonly int WallClimbContactAnim = Animator.StringToHash("WallClimbContact");
    private static readonly int JumpForwardAnim = Animator.StringToHash("Jump Forward");

    private static readonly FieldInfo StickToWallField = AccessTools.Field(typeof(WallJump), "_stickToWall");
    private static readonly FieldInfo WallHitField = AccessTools.Field(typeof(WallJump), "_wallHit");
    private static readonly FieldInfo PlayerStickedOrientationField = AccessTools.Field(typeof(WallJump), "playerStickedOrientation");
    private static readonly FieldInfo JumpOffWallField = AccessTools.Field(typeof(WallJump), "_jumpOffWall");
    private static readonly FieldInfo StickCoolDownTimerField = AccessTools.Field(typeof(WallJump), "_stickCoolDownTimer");
    private static readonly FieldInfo JumpOffCoolDownTimerField = AccessTools.Field(typeof(WallJump), "_jumpOffCoolDownTimer");
    private static readonly FieldInfo IsJumpOffStackedField = AccessTools.Field(typeof(WallJump), "_isJumpOffStacked");
    private static readonly FieldInfo WallJumpTimerField = AccessTools.Field(typeof(WallJump), "_wallJumpTimer");
    private static readonly FieldInfo DefaultRayCastDistanceField = AccessTools.Field(typeof(WallJump), "_defaultRayCastDistance");
    private static readonly FieldInfo DisabledAbilityWhenUseField = AccessTools.Field(typeof(WallJump), "DisabledAbilityWhenUse");
    private static readonly MethodInfo DisableAbilityMethod = AccessTools.Method(typeof(WallJump), "DisableAbility");

    // Round 72 - safety-net release: same private method vanilla itself uses for its own
    // "let go early" cancel gesture and the damage/camera-shake knockoff paths (EntityOwnerOnDamaged/
    // OnCameraShakeOverthrow) - reusing it via reflection instead of reimplementing its body means
    // the retry-timeout release restores gravity/blocker/Animator state exactly like every other
    // vanilla unhang path does, and the Postfix below (WallJump_UnhangByEvent_BlockerTracking_Patch)
    // still fires because Harmony patches the method itself, not a particular call site.
    private static readonly MethodInfo UnhangByEventMethod = AccessTools.Method(typeof(WallJump), "UnhangByEvent");

    // Logging edge-triggered para hipótesis familia 4 (Ronda 64) - solo para diagnóstico, no fix.
    private static bool _lastStickOnWallBool;
    private static int _lastWallStateHash;

    // Round 72 - Play(WallClimbContact) retry state. _stickStartFrame records the exact frame the
    // stick-start branch below called Play() for the first time - the retry check must not fire on
    // that same frame, since Round 67 confirmed Animator.Play() never takes effect synchronously
    // within the same Update() that calls it (Unity evaluates Animator transitions once per frame,
    // after every script's Update() has run) - checking STICK_ON_WALL on the start frame itself
    // would always read the pre-Play() value and look like a "failure" even in the 90%+ of cases
    // that succeed one frame later.
    private static int _stickStartFrame = -1;
    private static int _stuckRetryFrames;
    private const int MaxStickRetryFrames = 60; // ~1s of stuck frames before the safety release fires.

    // Ronda 67 - resolución de hash->nombre en runtime (evita reimplementar a ciegas el algoritmo
    // de hash de Unity fuera del proceso: se calcula con el propio Animator.StringToHash real,
    // corriendo dentro del juego, así que no puede estar "mal" por versión/algoritmo distinto).
    // Candidatos elegidos a partir de los nombres de estado que ya cita el propio
    // AnimatorInyector.IsJumping()/UpdateActions() decompilado (Ronda 67): "Jump"/"Falling"/
    // "Jump Forward"/"Falling Forward", más los dos ya conocidos de WallJump.
    private static readonly KeyValuePair<string, int>[] CandidateStateNames = new KeyValuePair<string, int>[]
    {
        new KeyValuePair<string, int>("WallClimbContact", Animator.StringToHash("WallClimbContact")),
        new KeyValuePair<string, int>("WallClimbIdle", Animator.StringToHash("WallClimbIdle")),
        new KeyValuePair<string, int>("Falling", Animator.StringToHash("Falling")),
        new KeyValuePair<string, int>("Falling Forward", Animator.StringToHash("Falling Forward")),
        new KeyValuePair<string, int>("Jump", Animator.StringToHash("Jump")),
        new KeyValuePair<string, int>("Jump Forward", Animator.StringToHash("Jump Forward")),
        new KeyValuePair<string, int>("Idle", Animator.StringToHash("Idle")),
    };

    private static bool _loggedCandidateHashTable;

    private static string ResolveStateName(int shortNameHash, float normalizedTime)
    {
        foreach (KeyValuePair<string, int> candidate in CandidateStateNames)
        {
            if (candidate.Value == shortNameHash)
            {
                return $"{candidate.Key} (norm:{normalizedTime:F2})";
            }
        }
        return $"hash:{shortNameHash} norm:{normalizedTime:F2}";
    }

    private static string DumpFloatParameters(Animator anim)
    {
        List<string> floats = new List<string>();
        foreach (AnimatorControllerParameter p in anim.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Float)
            {
                float v = anim.GetFloat(p.nameHash);
                if (Mathf.Abs(v) > 0.001f)
                    floats.Add($"{p.name}={v:F2}");
            }
        }
        return floats.Count > 0 ? string.Join(", ", floats.ToArray()) : "(none non-zero)";
    }

    private static bool Prefix(WallJump __instance)
    {
        Penitent owner = __instance.EntityOwner as Penitent;
        if (owner == null || owner != CoopLocal.Player2)
        {
            return true;
        }

        PlatformCharacterController controller = __instance.CharacterController;
        bool stickToWall = (bool)StickToWallField.GetValue(__instance);
        float stickCoolDownTimer = (float)StickCoolDownTimerField.GetValue(__instance);
        float jumpOffCoolDownTimer = (float)JumpOffCoolDownTimerField.GetValue(__instance);
        bool jumpOffWall = (bool)JumpOffWallField.GetValue(__instance);

        if (owner.Status.IsGrounded)
        {
            // ResetWallJumpStatus(), inlined - no owner-hardcode issue in the original, just moved
            // here so every field write for P2 lives in one place.
            JumpOffWallField.SetValue(__instance, false);
            IsJumpOffStackedField.SetValue(__instance, false);
            StickToWallField.SetValue(__instance, false);
            StickCoolDownTimerField.SetValue(__instance, -1f);
            controller.PlatformCharacterPhysics.Gravity = new Vector3(0f, -9.8f, 0f);
            stickToWall = false;
            stickCoolDownTimer = -1f;
            jumpOffWall = false;
            if (__instance.Distance <= 0f)
            {
                __instance.Distance = (float)DefaultRayCastDistanceField.GetValue(__instance);
            }
        }

        Vector3 rayOrigin = new Vector3(__instance.transform.position.x, __instance.transform.position.y + __instance.HookHeightFromPivotPoint, __instance.transform.position.z);
        float dir = owner.Status.Orientation != EntityOrientation.Right ? -1f : 1f;
        RaycastHit2D wallHit = Physics2D.Raycast(rayOrigin, Vector2.right * dir, __instance.Distance, __instance.WallLayerMask);
        WallHitField.SetValue(__instance, wallHit);

        bool endStickCoolDown = stickCoolDownTimer < 0f;
        if (Player2Input.AttackHeld && !controller.IsGrounded && wallHit.collider != null && !stickToWall && endStickCoolDown)
        {
            if (!_loggedCandidateHashTable)
            {
                _loggedCandidateHashTable = true;
                string table = string.Join(", ", Array.ConvertAll(CandidateStateNames, c => $"{c.Key}={c.Value}"));
                DashParryDebugLog.Log($"P2 WallJump hash cheat-sheet (runtime Animator.StringToHash, this process): {table}");
            }

            // Estado + parametros del Animator ANTES de tocar nada esta rama (antes de ResetTrigger/
            // Play) - Ronda 67: confirmar si viene de un estado en loop (normalizedTime>1) y si
            // AIR_ATTACK/FALLING ya estaban armados por AnimatorInyector este mismo frame, antes de
            // que este método haga nada.
            AnimatorStateInfo preState = owner.Animator.GetCurrentAnimatorStateInfo(0);
            bool preAirAttack = owner.Animator.GetBool("AIR_ATTACK");
            bool preFalling = owner.Animator.GetBool("FALLING");
            // Round 69 - FallingBehaviour (the plain "Falling" state's own StateMachineBehaviour,
            // sibling of FallingForwardBehaviour) was checked as a candidate family-1 bug for this
            // PRE-state-specific failure - already fixed (Movement/Movement.cs,
            // FallingBehaviour_OnStateEnter_Patch, predates the Round-numbering in this file), so
            // it is NOT a new lead. Logging IsJumpingOff/IsClimbingCliffLede/collider-enabled here
            // anyway in case some OTHER still-unaudited path leaves one of them in an unexpected
            // state specifically coming from "Falling" (none of WallJump's own vanilla code reads
            // any of these three, so they can only matter if the Animator Controller graph itself
            // conditions a transition on them, which ilspycmd cannot show).
            bool preIsJumpingOff = owner.IsJumpingOff;
            bool preIsClimbingCliffLede = owner.IsClimbingCliffLede;
            bool preColliderEnabled = owner.PlatformCharacterController.SmartPlatformCollider.enabled;
            Vector3 preVel = controller.PlatformCharacterPhysics.Velocity;
            float preVSpeed = controller.PlatformCharacterPhysics.VSpeed;
            DashParryDebugLog.Log($"P2 WallJump stick START frame {Time.frameCount} AttackHeld={Player2Input.AttackHeld} AttackDown={Player2Input.AttackDown} AttackUp={Player2Input.AttackUp} stickToWall false->true wall={wallHit.collider.name} PRE-state={ResolveStateName(preState.shortNameHash, preState.normalizedTime)} PRE-AIR_ATTACK={preAirAttack} PRE-FALLING={preFalling} PRE-IsJumpingOff={preIsJumpingOff} PRE-IsClimbingCliffLede={preIsClimbingCliffLede} PRE-ColliderEnabled={preColliderEnabled} PRE-vel={preVel} PRE-vSpeed={preVSpeed:F2} PRE-floats: {DumpFloatParameters(owner.Animator)}");
            owner.Audio.SetParametersValuesByWall(wallHit.collider);
            stickToWall = true;
            StickToWallField.SetValue(__instance, true);
            // Round 72 - fresh retry state for this new stick attempt (belt-and-suspenders: the
            // (!stickToWall) branch below already resets these on every exit, including the
            // safety-release path itself, but resetting again here guards against any future
            // exit path that doesn't).
            _stickStartFrame = Time.frameCount;
            _stuckRetryFrames = 0;
            PlayerStickedOrientationField.SetValue(__instance, owner.Status.Orientation);
            owner.Animator.ResetTrigger("AIR_ATTACK");
            owner.Animator.Play(WallClimbContactAnim);
            owner.Animator.SetBool("FALLING", false);
            Vector3 postVel = controller.PlatformCharacterPhysics.Velocity;
            DashParryDebugLog.Log($"P2 WallJump stick START frame {Time.frameCount} POST-ResetTrigger+Play AIR_ATTACK={owner.Animator.GetBool("AIR_ATTACK")} FALLING={owner.Animator.GetBool("FALLING")} vel={postVel} vSpeed={controller.PlatformCharacterPhysics.VSpeed:F2} floats: {DumpFloatParameters(owner.Animator)}");
            // Round 69 - dump every bool Animator parameter currently true, not just the two
            // already-suspected ones (AIR_ATTACK/FALLING) - Round 67/68 only checked those two by
            // name; this catches any OTHER bool the (invisible, binary) Animator Controller graph
            // might condition an "Any State" transition on, without having to guess its name in
            // advance. Compare this line's list between a successful (PRE-state Jump Forward/
            // Falling Forward) and a failing (PRE-state Falling) log to spot the actual differentiator.
            DashParryDebugLog.Log($"P2 WallJump stick START frame {Time.frameCount} POST-Play all-true-bools: {DumpTrueBoolParameters(owner.Animator)}");
            owner.transform.position = GetClimbPosition(__instance, owner, wallHit.collider);
            PlayerLogicBlocker.SetBlocked(owner, true);
            Core.Input.SetBlocker("PLAYER_LOGIC", blocking: true);
            owner.transform.DOMoveY(owner.transform.position.y - __instance.GravityDragDistance, __instance.GravityDragLapse).SetEase(Ease.OutSine).OnUpdate(() =>
            {
                RaycastHit2D currentHit = (RaycastHit2D)WallHitField.GetValue(__instance);
                if (currentHit.collider == null)
                {
                    DOTween.Kill(owner.transform);
                }
            });
        }

        if (stickToWall)
        {
            // Edge-triggered log del bool de animación + estado real mientras está pegado.
            bool stickOnWallBool = owner.Animator.GetBool("STICK_ON_WALL");
            AnimatorStateInfo st = owner.Animator.GetCurrentAnimatorStateInfo(0);
            string stateName = ResolveStateName(st.shortNameHash, st.normalizedTime);
            int curHash = st.shortNameHash;
            if (stickOnWallBool != _lastStickOnWallBool || curHash != _lastWallStateHash)
            {
                bool airAttackBool = owner.Animator.GetBool("AIR_ATTACK");
                bool fallingBool = owner.Animator.GetBool("FALLING");
                Vector3 vel = controller.PlatformCharacterPhysics.Velocity;
                DashParryDebugLog.Log($"P2 WallJump while-stuck frame {Time.frameCount} STICK_ON_WALL={stickOnWallBool} state={stateName} stickToWall={stickToWall} AttackHeld={Player2Input.AttackHeld} AttackDown={Player2Input.AttackDown} AIR_ATTACK={airAttackBool} FALLING={fallingBool} all-true-bools: {DumpTrueBoolParameters(owner.Animator)} floats: {DumpFloatParameters(owner.Animator)} vel={vel} vSpeed={controller.PlatformCharacterPhysics.VSpeed:F2}");
                _lastStickOnWallBool = stickOnWallBool;
                _lastWallStateHash = curHash;
            }

            // Round 72 - retry Play(WallClimbContact) on every subsequent frame while STICK_ON_WALL
            // hasn't caught on yet. Rounds 67-71 confirmed the Animator sometimes leaves the forced
            // Play() toward a competing state (observed landing back on "Jump Forward" at
            // normalizedTime=0.00) via a transition in the binary Animator Controller graph that
            // isn't readable from C# (no Bool/Float parameter differs between success and failure
            // cases) - rather than try to out-race that competing transition on a single frame
            // (not controllable from this side), keep re-issuing Play() every frame until it takes,
            // bounded by MaxStickRetryFrames as a safety net against a genuinely stuck case this
            // session's logs haven't captured. Skip the very first frame of this stick attempt
            // (Time.frameCount == _stickStartFrame): Play() never takes effect synchronously within
            // the same Update() that calls it (Round 67), so checking STICK_ON_WALL that same frame
            // would always look like a failure even in the normal, working case.
            if (Time.frameCount != _stickStartFrame)
            {
                if (!stickOnWallBool)
                {
                    _stuckRetryFrames++;
                    if (_stuckRetryFrames == 1)
                    {
                        DashParryDebugLog.Log($"P2 WallJump STICK_ON_WALL still False frame {Time.frameCount} state={stateName} - retrying Play(WallClimbContact)");
                    }
                    owner.Animator.Play(WallClimbContactAnim);

                    if (_stuckRetryFrames > MaxStickRetryFrames)
                    {
                        DashParryDebugLog.Log($"P2 WallJump retry limit ({MaxStickRetryFrames} frames) exceeded at frame {Time.frameCount}, state={stateName} - forcing UnhangByEvent safety release");
                        UnhangByEventMethod.Invoke(__instance, null);
                        stickToWall = false;
                        _stuckRetryFrames = 0;
                    }
                }
                else if (_stuckRetryFrames > 0)
                {
                    DashParryDebugLog.Log($"P2 WallJump STICK_ON_WALL recovered to True at frame {Time.frameCount} after {_stuckRetryFrames} Play() retries, state={stateName}");
                    _stuckRetryFrames = 0;
                }
            }

            if (stickToWall)
            {
                // Stick(), inlined and fixed - the one line that mattered:
                // Core.Logic.Penitent.SetOrientation(...) -> owner.SetOrientation(...).
                JumpOffWallField.SetValue(__instance, false);
                __instance.ToogleAbilities(false);
                jumpOffCoolDownTimer -= Time.deltaTime;
                JumpOffCoolDownTimerField.SetValue(__instance, jumpOffCoolDownTimer);
                stickCoolDownTimer = __instance.StickCoolDown;
                StickCoolDownTimerField.SetValue(__instance, stickCoolDownTimer);
                IsJumpOffStackedField.SetValue(__instance, false);
                controller.PlatformCharacterPhysics.Velocity = Vector3.zero;
                controller.PlatformCharacterPhysics.VSpeed = 0f;
                controller.PlatformCharacterPhysics.Gravity = Vector3.zero;
                controller.PlatformCharacterPhysics.Acceleration = Vector3.zero;
                Core.Input.SetBlocker("PLAYER_LOGIC", blocking: true);
                EntityOrientation stickedOrientation = (EntityOrientation)PlayerStickedOrientationField.GetValue(__instance);
                owner.SetOrientation(stickedOrientation);
            }
        }
        else
        {
            // Reset edge cache cuando sale del wall-stick para que el próximo enganche loguee fresh.
            _lastStickOnWallBool = false;
            _lastWallStateHash = 0;
            _stuckRetryFrames = 0;
        }

        if (Player2Input.JumpDown && stickToWall && jumpOffCoolDownTimer < 0f && owner.Animator.GetBool("STICK_ON_WALL") && !Gameplay.UI.UIController.instance.IsShowingMenu)
        {
            DOTween.Kill(owner.transform);
            __instance.ToogleAbilities(true);

            // Detach(), inlined - already owner-safe in the original, no bug here.
            StickToWallField.SetValue(__instance, false);
            JumpOffWallField.SetValue(__instance, true);
            jumpOffWall = true;
            controller.PlatformCharacterPhysics.Gravity = new Vector3(0f, -9.8f, 0f);
            PlayerLogicBlocker.SetBlocked(owner, false);
            Core.Input.SetBlocker("PLAYER_LOGIC", blocking: false);
            controller.PlatformCharacterPhysics.Velocity = new Vector2(__instance.WallJumpSpeed * __instance.CharacterInput.FHorAxis, __instance.WallJumpSpeed);
            owner.Animator.SetBool("STICK_ON_WALL", false);
            GrabCliffLede disabledAbility = (GrabCliffLede)DisabledAbilityWhenUseField.GetValue(__instance);
            if (disabledAbility != null)
            {
                IEnumerator routine = (IEnumerator)DisableAbilityMethod.Invoke(__instance, null);
                __instance.StartCoroutine(routine);
            }
        }

        if (jumpOffWall)
        {
            // JumpOff(), inlined - already owner-safe in the original, no bug here.
            JumpOffCoolDownTimerField.SetValue(__instance, __instance.JumpOffCoolDown);
            stickCoolDownTimer -= Time.deltaTime;
            StickCoolDownTimerField.SetValue(__instance, stickCoolDownTimer);
            bool isJumpOffStacked = (bool)IsJumpOffStackedField.GetValue(__instance);
            if (!isJumpOffStacked)
            {
                IsJumpOffStackedField.SetValue(__instance, true);
                EntityOrientation stickedOrientation = (EntityOrientation)PlayerStickedOrientationField.GetValue(__instance);
                float x = stickedOrientation != EntityOrientation.Right ? 1f : -1f;
                __instance.CharacterInput.Move(x, 0.1f);
                owner.Animator.Play(JumpForwardAnim);
                owner.SetOrientation(stickedOrientation != EntityOrientation.Left ? EntityOrientation.Left : EntityOrientation.Right);
            }
            float wallJumpTimer = (float)WallJumpTimerField.GetValue(__instance);
            if (wallJumpTimer > 0f)
            {
                wallJumpTimer -= Time.deltaTime;
                WallJumpTimerField.SetValue(__instance, wallJumpTimer);
                controller.PlatformCharacterPhysics.Acceleration += __instance.transform.up * __instance.WallJumpAcc;
            }
        }
        else
        {
            WallJumpTimerField.SetValue(__instance, controller.JumpingAccTime);
        }

        return false;
    }

    // Round 69 - lists every Bool-type Animator parameter currently true, by name, regardless of
    // whether this class already knows about it. Cheap (parameters array is small, only called at
    // edge-triggered log points, not every frame) and avoids having to guess in advance which
    // parameter the (binary, unreadable-via-ilspycmd) Animator Controller graph might condition an
    // "Any State" transition on.
    private static string DumpTrueBoolParameters(Animator animator)
    {
        List<string> trueBools = new List<string>();
        foreach (AnimatorControllerParameter param in animator.parameters)
        {
            if (param.type == AnimatorControllerParameterType.Bool && animator.GetBool(param.name))
            {
                trueBools.Add(param.name);
            }
        }
        return trueBools.Count > 0 ? string.Join(",", trueBools.ToArray()) : "(none)";
    }

    private static Vector3 GetClimbPosition(WallJump instance, Penitent owner, Collider2D climbCollider)
    {
        float x = owner.Status.Orientation != EntityOrientation.Right
            ? climbCollider.bounds.max.x + instance.StickDistanceToWall
            : climbCollider.bounds.min.x - instance.StickDistanceToWall;
        return new Vector2(x, owner.transform.position.y);
    }
}

// P1's own vanilla Stick()/Detach() calls (unaffected by the Prefix above, which only intercepts
// P2's OnUpdate) never register with PlayerLogicBlocker - only the global Core.Input.SetBlocker.
// Same gap already flagged in NOTES.md for every other still-unaudited PLAYER_LOGIC user; closing
// it here the same way Dash/Parry/GrabLadderDown already are, so BlockerOverrideHelper can
// correctly un-freeze the other player while either one is wall-clinging.
[HarmonyPatch(typeof(WallJump), "Stick")]
internal static class WallJump_Stick_BlockerTracking_Patch
{
    private static void Postfix(WallJump __instance)
    {
        PlayerLogicBlocker.SetBlocked(__instance.EntityOwner as Penitent, true);
    }
}

[HarmonyPatch(typeof(WallJump), "Detach")]
internal static class WallJump_Detach_BlockerTracking_Patch
{
    private static void Postfix(WallJump __instance)
    {
        PlayerLogicBlocker.SetBlocked(__instance.EntityOwner as Penitent, false);
    }
}

// Round 72 - same gap as Stick()/Detach() above, closed the same way: UnhangByEvent() is vanilla's
// own release path (used by EntityOwnerOnDamaged/OnCameraShakeOverthrow/CheckCancelHook's UnHang()
// coroutine, and now also by this mod's P2 retry-timeout safety net via reflection above) but never
// registered with PlayerLogicBlocker on its own - only the global Core.Input.SetBlocker, which
// BlockerOverrideHelper doesn't consult. Harmony patches the method itself, so this Postfix fires
// regardless of which call site (vanilla P1's own, or the reflection Invoke in the Prefix above)
// triggered it.
[HarmonyPatch(typeof(WallJump), "UnhangByEvent")]
internal static class WallJump_UnhangByEvent_BlockerTracking_Patch
{
    private static void Postfix(WallJump __instance)
    {
        PlayerLogicBlocker.SetBlocked(__instance.EntityOwner as Penitent, false);
    }
}

// Ronda 67 - diagnóstico puro (no toca comportamiento) para confirmar el orden real de ejecución
// entre AnimatorInyector.Update() (SpriteAnimator.SetTrigger("AIR_ATTACK") vive dentro de su
// AirAttack(), llamada desde UpdateActions() en la rama !grounded) y WallJump.OnUpdate() en el
// mismo frame - la hipótesis de la Ronda 64/65 depende de cuál de los dos corre último, algo que
// no se puede leer del C# decompilado (orden de componentes del prefab / Script Execution Order).
// Con ambos logs usando el mismo Time.frameCount, el ORDEN DE LAS LÍNEAS en LogOutput.log para el
// mismo número de frame revela directamente cuál corrió primero, sin necesitar un contador
// artificial. Postfix (no Prefix) para loguear el estado real de AIR_ATTACK justo después de que
// AnimatorInyector haya terminado de decidir si lo arma o no.
[HarmonyPatch(typeof(Gameplay.GameControllers.Penitent.Animator.AnimatorInyector), "AirAttack")]
internal static class AnimatorInyector_AirAttack_OrderDebugLogger_Patch
{
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(Gameplay.GameControllers.Penitent.Animator.AnimatorInyector), "_penitent");
    private static readonly FieldInfo PlayerInputField = AccessTools.Field(typeof(Gameplay.GameControllers.Penitent.Animator.AnimatorInyector), "_playerInput");

    private static void Postfix(object __instance)
    {
        Penitent penitent = PenitentField.GetValue(__instance) as Penitent;
        if (penitent != CoopLocal.Player2)
        {
            return;
        }
        PlatformCharacterInput input = PlayerInputField.GetValue(__instance) as PlatformCharacterInput;
        if (input == null || !input.Attack)
        {
            // AirAttack() runs every frame while airborne, but only actually calls SetTrigger when
            // input.Attack is true (single-frame edge) - only that call matters for the race, so
            // skip logging the (very frequent) no-op frames.
            return;
        }
        DashParryDebugLog.Log($"P2 AnimatorInyector.AirAttack() SetTrigger(AIR_ATTACK) frame {Time.frameCount} (order marker vs WallJump logs at same frame)");
    }
}
