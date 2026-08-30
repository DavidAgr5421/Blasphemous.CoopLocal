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

// Debug-only logging, not meant to ship long-term - grep BepInEx/LogOutput.log for
// "[DashParryDebug]" to follow any of it.
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

    // Round 76 audit: Directory.CreateDirectory() is a syscall - cheap, but pointless to repeat
    // on literally every single Log() call (this fires many times per second across 83 call
    // sites in the mod) once the directory demonstrably exists already. Cached after the first
    // successful creation; File.AppendAllText itself still opens/writes/closes every call (no
    // buffering) which is an accepted tradeoff for "never lose a line on a crash/relaunch".
    private static bool directoryEnsured;

    // Unity's own Update()/Harmony-patch call sites are all single-threaded (main thread), so in
    // practice File.AppendAllText's own open-write-close per call can never truly overlap. This
    // lock is cheap insurance in case anything (a coroutine callback, an async asset load
    // continuation) ever calls Log() off the main thread, avoiding a rare "file in use"
    // IOException that would otherwise be silently swallowed by the try/catch anyway - not
    // relied upon as the only safety net, just removes the small remaining risk for near-zero cost.
    private static readonly object fileLock = new object();

    internal static void Log(string message)
    {
        string line = "[DashParryDebug] " + message;
        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info(line, Main.CoopLocal);
        }
        // Persistente append-only para que no se pierda al relanzar el juego (BepInEx/LogOutput.log se trunca)
        try
        {
            string dir = System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, "CoopLocalMod");
            string ts = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            lock (fileLock)
            {
                if (!directoryEnsured)
                {
                    System.IO.Directory.CreateDirectory(dir);
                    directoryEnsured = true;
                }
                string path = System.IO.Path.Combine(dir, "debug_log.txt");
                System.IO.File.AppendAllText(path, $"[{ts}] {line}{System.Environment.NewLine}");
            }
        }
        catch { }
    }

    internal static string PersistentLogPath => System.IO.Path.Combine(System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, "CoopLocalMod"), "debug_log.txt");
}

// Logs the current animation clip name for either player whenever it changes - used to trace
// P1-freezes-while-P2-dashes-style cross-talk by watching what each player's Animator is actually
// playing, moment to moment.
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

// Samples both players' X position on a fixed cadence, independent of Blocked/locks/animation
// state - a "ground truth" check for whether a player is actually moving, plus P1's raw arrow-key
// state (useful for telling a real freeze apart from the tester's own hand just not holding the
// key while operating both characters solo).
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
        bool p1MovementKeyHeld = Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow);
        DashParryDebugLog.Log($"pos P1.x={p1X:F2} P2.x={p2X:F2} p1MovementKeyHeld={p1MovementKeyHeld} (frame {Time.frameCount})");
    }
}

// Opens a short unconditional logging window (every SetActionState call, no dedup) right after
// P2's dash/parry lock turns on, to catch the case where two same-frame calls for the same action
// would otherwise hide each other behind SetActionState_DebugLogger_Patch's own last-value dedup.
internal static class SetActionStateWatchWindow
{
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

// Logs every time P1's own PlatformCharacterController.SetActionState(Left/Right, false) fires,
// dumping the full set of conditions PlatformCharacterInput.Update()'s vanilla logic checks before
// making that call - used to trace P1 losing movement input while P2 acts, by seeing exactly which
// condition (if any) explains a given false.
[HarmonyPatch(typeof(PlatformCharacterController), nameof(PlatformCharacterController.SetActionState))]
internal static class SetActionState_DebugLogger_Patch
{
    private static readonly Dictionary<eControllerActions, bool> lastP1Value = new Dictionary<eControllerActions, bool>();

    private static void Postfix(PlatformCharacterController __instance, eControllerActions action, bool value)
    {
        bool isTrackedAction = action == eControllerActions.Left || action == eControllerActions.Right;
        // Jump/Up/Down are only logged during the watch window - useful for telling apart "the
        // normal else-branch computed false" (Left/Right only) from "ResetActions() nuked all
        // five at once".
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
            PlatformCharacterInput p1Input = p1.PlatformCharacterInput;
            float rawRewiredAxis = p1Input.Rewired != null ? p1Input.Rewired.GetAxisRaw(0) : float.NaN;
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
