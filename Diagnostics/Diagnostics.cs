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

// Log del mod (debug_log.txt en LocalLow/.../CoopLocalMod). Las líneas se encolan en memoria y un hilo de fondo las vuelca por
// lotes (ver BufferedLogWriter): llamar a Log() cuesta una concatenación y un lock, no una escritura a disco. Hasta ~250 ms
// de líneas pueden perderse si el proceso muere de golpe; Flush() las fuerza (se llama al descargar el mod y al salir).
// Solo avisos/errores se copian al log de BepInEx (LogOutput.log); lo demás vive únicamente en debug_log.txt.
internal static class DashParryDebugLog
{
    internal static string Label(Penitent p)
    {
        if (p == null)
        {
            return "null";
        }
        int idx = CoopPlayerRegistry.GetPlayerIndex(p);
        return idx >= 0 ? "P" + (idx + 1) : "P?";
    }

    private const long MaxLogBytes = 5L * 1024L * 1024L;
    private const int FlushIntervalMs = 250;
    private static readonly object stateLock = new object();
    private static readonly HashSet<string> loggedOnceKeys = new HashSet<string>(StringComparer.Ordinal);
    private static string logPath;       // se resuelve en el hilo principal (Application.persistentDataPath no es seguro fuera de él)
    private static bool sessionInitialized;
    private static BufferedLogWriter writer;

    internal static bool VerboseEnabled = false;

    /// <summary>true = además de a debug_log.txt, las líneas informativas se copian al log de BepInEx (más lento; solo para depurar).</summary>
    internal static bool MirrorInfoToModLog = false;

    private static BufferedLogWriter Writer
    {
        get
        {
            if (writer == null)
            {
                lock (stateLock)
                {
                    if (writer == null)
                    {
                        var w = new BufferedLogWriter(ResolvePath, MaxLogBytes);
                        w.Start(FlushIntervalMs);
                        writer = w;
                        try { AppDomain.CurrentDomain.ProcessExit += (sender, e) => Flush(); } catch (Exception) { /* sin ProcessExit: se vuelca por el hilo */ }
                    }
                }
            }
            return writer;
        }
    }

    private static string ResolvePath()
    {
        if (logPath != null) return logPath;
        try
        {
            string p = UnityEngine.Application.persistentDataPath;
            if (string.IsNullOrEmpty(p)) return null;
            logPath = System.IO.Path.Combine(System.IO.Path.Combine(p, "CoopLocalMod"), "debug_log.txt");
        }
        catch (Exception) { return null; } // fuera del hilo principal / Unity aún no listo: reintentar luego
        return logPath;
    }

    /// <summary>Rota la sesión anterior (debug_log.txt -> debug_log_prev.txt) y arranca el escritor. Llamar una vez al iniciar el mod.</summary>
    internal static void InitializeSession()
    {
        try
        {
            lock (stateLock)
            {
                if (sessionInitialized) return;
                sessionInitialized = true;
            }
            string path = ResolvePath();
            if (path != null)
            {
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                string prevPath = BufferedLogWriter.PrevPath(path);
                if (System.IO.File.Exists(path))
                {
                    if (System.IO.File.Exists(prevPath)) System.IO.File.Delete(prevPath);
                    System.IO.File.Move(path, prevPath);
                }
                Writer.ResetSizeCache();
            }
        }
        catch (Exception) { /* fallo al rotar el log de sesión: no debe impedir el arranque */ }
    }

    internal static void Log(string message)
    {
        WriteLine("[DashParryDebug] " + message, isWarning: false);
    }

    internal static void LogVerbose(string message)
    {
        if (!VerboseEnabled) return;
        WriteLine("[DashParryDebug] " + message, isWarning: false);
    }

    internal static void LogOnce(string key, string message, bool isWarning = false)
    {
        if (string.IsNullOrEmpty(key)) return;
        lock (stateLock)
        {
            if (!loggedOnceKeys.Add(key)) return;
        }
        WriteLine(message, isWarning);
    }

    /// <summary>Fuerza el volcado de las líneas pendientes a disco (síncrono).</summary>
    internal static void Flush()
    {
        try { if (writer != null) writer.Flush(); } catch (Exception) { /* el log nunca debe tumbar el juego */ }
    }

    private static void WriteLine(string line, bool isWarning)
    {
        if (Main.CoopLocal != null && (isWarning || MirrorInfoToModLog))
        {
            if (isWarning)
                Blasphemous.ModdingAPI.ModLog.Warn(line, Main.CoopLocal);
            else
                Blasphemous.ModdingAPI.ModLog.Info(line, Main.CoopLocal);
        }
        Writer.Enqueue("[" + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "] " + line);
    }

    internal static string PersistentLogPath => ResolvePath() ?? "debug_log.txt";
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
        if (!DashParryDebugLog.VerboseEnabled) return; // solo registra: sin log detallado no hay nada que calcular
        if (____penitent == null || ____penitent.Animator == null)
        {
            return;
        }

        AnimatorClipInfo[] clips = ____penitent.Animator.GetCurrentAnimatorClipInfo(0);
        string clipName = clips.Length > 0 ? clips[0].clip.name : "(none)";

        if (!lastClipName.TryGetValue(____penitent, out string last) || last != clipName)
        {
            lastClipName[____penitent] = clipName;
            DashParryDebugLog.LogVerbose($"{DashParryDebugLog.Label(____penitent)} anim -> \"{clipName}\" (frame {Time.frameCount})");
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
        DashParryDebugLog.LogVerbose($"pos P1.x={p1X:F2} P2.x={p2X:F2} p1MovementKeyHeld={p1MovementKeyHeld} (frame {Time.frameCount})");
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
                DashParryDebugLog.LogVerbose($"P1 SetActionState({action}, {value}) (frame {Time.frameCount}) [watch window]");
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
            DashParryDebugLog.LogVerbose($"P1 SetActionState({action}, {value}) (frame {Time.frameCount}) [watch window]");
        }

        if (!value)
        {
            PlatformCharacterInput p1Input = p1.PlatformCharacterInput;
            float rawRewiredAxis = p1Input.Rewired != null ? p1Input.Rewired.GetAxisRaw(0) : float.NaN;
            bool rawInputBlocked = Core.Input.InputBlocked;
            DashParryDebugLog.LogVerbose(
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
