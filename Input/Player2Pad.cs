using Blasphemous.CoopLocal.MultiPlayer;
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
using Gameplay.UI.Others.MenuLogic;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace Blasphemous.CoopLocal;

// P2's fixed gamepad scheme, active whenever Player2Input.Mode == Gamepad. Reads a physical
// gamepad directly through Rewired's own Controller/Joystick API - the same backend
// "Xinput1_4.dll" in the BepInEx log confirms this game already relies on for gamepad input -
// rather than Unity's legacy Input class, whose virtual "Horizontal"/"Vertical" axes can
// silently also read the keyboard depending on this project's Input Manager config (unverified,
// and not worth the risk of reintroducing keyboard/gamepad cross-talk).
//
// Round 34 confirmed raw button INDICES (0=Jump, 1=Dash, 2=Attack, 3=Parry) worked. Round 36:
// the user then reported Dash/Parry firing off different physical buttons (Y/B) than before
// (Right Trigger/Left Bumper) with no code change to those indices in between - meaning the raw
// index-to-physical-button order isn't stable across sessions for this pad (likely Steam Input's
// virtual layer, or plain OS/driver re-enumeration on reconnect). Buttons are now resolved by
// their Rewired-assigned NAME instead (e.g. "Right Trigger") via
// GetButtonById/GetButtonDownById/GetButtonUpById - a stable id tied to the named element rather
// than raw positional order. LogKnownButtonsOnce logs every button name this pad's Rewired
// hardware map actually exposes (grep BepInEx/LogOutput.log for "[Player2Pad] known gamepad
// buttons") - which turned out to be exactly: A, B, X, Y, Left/Right Shoulder, Back, Start,
// Guide, Left/Right Stick Button, D-Pad Up/Right/Down/Left. No "Trigger" entries at all - this
// pad's Rewired map exposes the analog triggers as AXES only, not as synthetic digital buttons,
// which is why Dash ("Right Trigger") and PrayerActivate ("Left Trigger") kept failing to
// resolve as buttons no matter what candidate names were tried. They're read as axes below
// instead (ResolveAxisId, same by-name lookup, thresholded past AxisThreshold), with manual
// edge-detection (TrackAxisEdge) since Rewired's axis API has no built-in GetAxisDownById the
// way buttons do. Movement (left stick + d-pad, axes 0/1) is untouched - confirmed working and
// not reported as unstable, so left on its raw indices rather than switched to name lookup too.
internal static class Player2Pad
{
    internal const int AxisLeftStickX = 0;
    internal const int AxisLeftStickY = 1;
    private const float AxisThreshold = 0.5f;

    internal static Rewired.Joystick GetPadForSlot(int slotIndex)
    {
        if (!Rewired.ReInput.isReady || Rewired.ReInput.controllers == null) return null;
        var joysticks = Rewired.ReInput.controllers.Joysticks;
        if (joysticks == null || joysticks.Count == 0) return null;

        int joyIndex = 0;
        if (slotIndex >= 0 && slotIndex < CoopGameConfig.Slots.Length)
        {
            switch (CoopGameConfig.Slots[slotIndex].Device)
            {
                case CoopDeviceType.Gamepad1: joyIndex = 0; break;
                case CoopDeviceType.Gamepad2: joyIndex = 1; break;
                case CoopDeviceType.Gamepad3: joyIndex = 2; break;
                case CoopDeviceType.Gamepad4: joyIndex = 3; break;
                default:
                    return null;
            }
        }
        else
        {
            joyIndex = slotIndex > 0 ? (slotIndex - 1) : 0;
        }

        if (joyIndex >= 0 && joyIndex < joysticks.Count)
        {
            return joysticks[joyIndex];
        }
        return null;
    }

    internal static Rewired.Joystick Pad
    {
        get
        {
            var joysticks = (Rewired.ReInput.isReady && Rewired.ReInput.controllers != null) ? Rewired.ReInput.controllers.Joysticks : null;
            if (joysticks == null || joysticks.Count == 0) return null;
            Rewired.Joystick slotPad = GetPadForSlot(1);
            if (slotPad != null) return slotPad;

            int joyIndex = CoopGameConfig.Slots[0].Device == CoopDeviceType.Gamepad1 ? 1 : 0;
            if (joyIndex >= 0 && joyIndex < joysticks.Count) return joysticks[joyIndex];
            return joysticks[0];
        }
    }

    internal static Rewired.Joystick P1Pad
    {
        get
        {
            var joysticks = (Rewired.ReInput.isReady && Rewired.ReInput.controllers != null) ? Rewired.ReInput.controllers.Joysticks : null;
            if (joysticks == null || joysticks.Count == 0) return null;
            Rewired.Joystick slotPad = GetPadForSlot(0);
            if (slotPad != null) return slotPad;
            return joysticks[0];
        }
    }

    internal static bool GetLeft(Rewired.Joystick pad) =>
        pad != null && ((pad.GetAxis(AxisLeftStickX) <= -AxisThreshold) || ButtonHeld(pad, "D-Pad Left", "DPad Left", "Dpad Left"));

    internal static bool GetRight(Rewired.Joystick pad) =>
        pad != null && ((pad.GetAxis(AxisLeftStickX) >= AxisThreshold) || ButtonHeld(pad, "D-Pad Right", "DPad Right", "Dpad Right"));

    internal static bool GetUp(Rewired.Joystick pad) =>
        pad != null && ((pad.GetAxis(AxisLeftStickY) >= AxisThreshold) || ButtonHeld(pad, "D-Pad Up", "DPad Up", "Dpad Up"));

    internal static bool GetDown(Rewired.Joystick pad) =>
        pad != null && ((pad.GetAxis(AxisLeftStickY) <= -AxisThreshold) || ButtonHeld(pad, "D-Pad Down", "DPad Down", "Dpad Down"));

    internal static bool GetJumpHeld(Rewired.Joystick pad, int slotIndex = -1)
    {
        if (slotIndex >= 0)
        {
            string mapped = CoopControlsConfig.GetBindings(slotIndex).PadJump;
            if (!string.IsNullOrEmpty(mapped))
            {
                if (ButtonHeld(pad, mapped)) return true;
                if (AxisHeld(pad, "JumpTrigger", mapped)) return true;
            }
        }
        return ButtonHeld(pad, "A Button", "Button A", "Action Button 1", "Cross", "Button 0", "A");
    }

    internal static bool GetSelectDown(Rewired.Joystick pad) =>
        ButtonDown(pad, "A Button", "Button A", "Action Button 1", "Cross", "Button 0", "A");

    // Botón de Ataque a Distancia (acción propia del jugador; por defecto B / Círculo). Ya NO se dispara con mantener/soltar Ataque.
    private static string RangeName(int slotIndex) => slotIndex >= 0 ? CoopControlsConfig.GetBindings(slotIndex).PadRange : null;

    internal static bool GetRangeDown(Rewired.Joystick pad, int slotIndex = -1)
    {
        string mapped = RangeName(slotIndex);
        if (!string.IsNullOrEmpty(mapped) && GetButtonDownMapped(pad, mapped)) return true;
        return string.IsNullOrEmpty(mapped) && ButtonDown(pad, "B Button", "Button B", "Action Button 2", "Circle", "Button 1", "B");
    }

    internal static bool GetRangeUp(Rewired.Joystick pad, int slotIndex = -1)
    {
        string mapped = RangeName(slotIndex);
        if (!string.IsNullOrEmpty(mapped))
        {
            if (ButtonUp(pad, mapped)) return true;
            if (AxisUp(pad, "MappedTrigger_" + mapped, mapped)) return true;
            return false;
        }
        return ButtonUp(pad, "B Button", "Button B", "Action Button 2", "Circle", "Button 1", "B");
    }

    internal static bool GetRangeHeld(Rewired.Joystick pad, int slotIndex = -1)
    {
        string mapped = RangeName(slotIndex);
        if (!string.IsNullOrEmpty(mapped))
        {
            if (ButtonHeld(pad, mapped)) return true;
            if (AxisHeld(pad, "MappedTrigger_" + mapped, mapped)) return true;
            return false;
        }
        return ButtonHeld(pad, "B Button", "Button B", "Action Button 2", "Circle", "Button 1", "B");
    }

    internal static bool GetAttackDown(Rewired.Joystick pad, int slotIndex = -1)
    {
        if (slotIndex >= 0)
        {
            string mapped = CoopControlsConfig.GetBindings(slotIndex).PadAttack;
            if (!string.IsNullOrEmpty(mapped))
            {
                if (ButtonDown(pad, mapped)) return true;
                if (AxisDown(pad, "AttackTrigger", mapped)) return true;
            }
        }
        return ButtonDown(pad, "X Button", "Button X", "Action Button 3", "Square", "Button 2", "X");
    }

    internal static bool GetAttackUp(Rewired.Joystick pad, int slotIndex = -1)
    {
        if (slotIndex >= 0)
        {
            string mapped = CoopControlsConfig.GetBindings(slotIndex).PadAttack;
            if (!string.IsNullOrEmpty(mapped))
            {
                if (ButtonUp(pad, mapped)) return true;
                if (AxisUp(pad, "AttackTrigger", mapped)) return true;
            }
        }
        return ButtonUp(pad, "X Button", "Button X", "Action Button 3", "Square", "Button 2", "X");
    }

    internal static bool GetAttackHeld(Rewired.Joystick pad, int slotIndex = -1)
    {
        if (slotIndex >= 0)
        {
            string mapped = CoopControlsConfig.GetBindings(slotIndex).PadAttack;
            if (!string.IsNullOrEmpty(mapped))
            {
                if (ButtonHeld(pad, mapped)) return true;
                if (AxisHeld(pad, "AttackTrigger", mapped)) return true;
            }
        }
        return ButtonHeld(pad, "X Button", "Button X", "Action Button 3", "Square", "Button 2", "X");
    }

    internal static bool GetParryDown(Rewired.Joystick pad, int slotIndex = -1)
    {
        if (slotIndex >= 0)
        {
            string mapped = CoopControlsConfig.GetBindings(slotIndex).PadParry;
            if (!string.IsNullOrEmpty(mapped))
            {
                if (ButtonDown(pad, mapped)) return true;
                if (AxisDown(pad, "ParryTrigger", mapped)) return true;
            }
        }
        return ButtonDown(pad, "Left Bumper", "LB", "L1", "Left Shoulder");
    }

    internal static bool GetHealDown(Rewired.Joystick pad, int slotIndex = -1)
    {
        if (slotIndex >= 0)
        {
            string mapped = CoopControlsConfig.GetBindings(slotIndex).PadHeal;
            if (!string.IsNullOrEmpty(mapped))
            {
                if (ButtonDown(pad, mapped)) return true;
                if (AxisDown(pad, "HealTrigger", mapped)) return true;
            }
        }
        return ButtonDown(pad, "Right Bumper", "RB", "R1", "Right Shoulder");
    }

    internal static bool GetInteractDown(Rewired.Joystick pad, int slotIndex = -1)
    {
        if (slotIndex >= 0)
        {
            string mapped = CoopControlsConfig.GetBindings(slotIndex).PadInteract;
            if (!string.IsNullOrEmpty(mapped))
            {
                if (ButtonDown(pad, mapped)) return true;
                if (AxisDown(pad, "InteractTrigger", mapped)) return true;
            }
        }
        return ButtonDown(pad, "Y Button", "Button Y", "Action Button 4", "Triangle", "Button 3", "Y");
    }

    internal static bool GetDashDown(Rewired.Joystick pad, int slotIndex = -1)
    {
        if (slotIndex >= 0)
        {
            string mapped = CoopControlsConfig.GetBindings(slotIndex).PadDash;
            if (!string.IsNullOrEmpty(mapped))
            {
                if (AxisDown(pad, "DashTrigger", mapped)) return true;
                if (ButtonDown(pad, mapped)) return true;
            }
        }
        return AxisDown(pad, "DashTrigger", "Right Trigger", "RT", "R2") || ButtonDown(pad, "Right Trigger", "RT", "R2");
    }

    internal static bool GetDashHeld(Rewired.Joystick pad, int slotIndex = -1)
    {
        if (slotIndex >= 0)
        {
            string mapped = CoopControlsConfig.GetBindings(slotIndex).PadDash;
            if (!string.IsNullOrEmpty(mapped))
            {
                if (AxisHeld(pad, "DashTrigger", mapped)) return true;
                if (ButtonHeld(pad, mapped)) return true;
            }
        }
        return AxisHeld(pad, "DashTrigger", "Right Trigger", "RT", "R2") || ButtonHeld(pad, "Right Trigger", "RT", "R2");
    }

    internal static bool GetPrayerActivateDown(Rewired.Joystick pad, int slotIndex = -1)
    {
        if (slotIndex >= 0)
        {
            string mapped = CoopControlsConfig.GetBindings(slotIndex).PadPrayer;
            if (!string.IsNullOrEmpty(mapped))
            {
                if (AxisDown(pad, "PrayerTrigger", mapped)) return true;
                if (ButtonDown(pad, mapped)) return true;
            }
        }
        return AxisDown(pad, "PrayerTrigger", "Left Trigger", "LT", "L2") || ButtonDown(pad, "Left Trigger", "LT", "L2");
    }

    internal static bool GetPrayerActivateUp(Rewired.Joystick pad, int slotIndex = -1)
    {
        if (slotIndex >= 0)
        {
            string mapped = CoopControlsConfig.GetBindings(slotIndex).PadPrayer;
            if (!string.IsNullOrEmpty(mapped))
            {
                if (AxisUp(pad, "PrayerTrigger", mapped)) return true;
                if (ButtonUp(pad, mapped)) return true;
            }
        }
        return AxisUp(pad, "PrayerTrigger", "Left Trigger", "LT", "L2") || ButtonUp(pad, "Left Trigger", "LT", "L2");
    }

    internal static bool Left => GetLeft(Pad);
    internal static bool Right => GetRight(Pad);
    internal static bool Up => GetUp(Pad);
    internal static bool Down => GetDown(Pad);

    internal static bool JumpHeld => GetJumpHeld(Pad);
    internal static bool AttackDown => GetAttackDown(Pad);
    internal static bool AttackUp => GetAttackUp(Pad);
    internal static bool AttackHeld => GetAttackHeld(Pad);
    internal static bool ParryDown => GetParryDown(Pad);
    internal static bool HealDown => GetHealDown(Pad);
    internal static bool InteractDown => GetInteractDown(Pad);
    /// <summary>Menú de sala del anfitrión (P1) con mando: Start + Back a la vez (no choca con la pausa vanilla de Start).</summary>
    internal static bool GetHostMenuDown(Rewired.Joystick pad)
    {
        if (pad == null) return false;
        return (ButtonDown(pad, "Start", "Options", "Menu Button") && ButtonHeld(pad, "Back", "Select", "Share", "View"))
            || (ButtonDown(pad, "Back", "Select", "Share", "View") && ButtonHeld(pad, "Start", "Options", "Menu Button"));
    }
    internal static bool GetStartDown(Rewired.Joystick pad) => ButtonDown(pad, "Start", "Options", "Menu Button");
    internal static bool GetMenuDown(Rewired.Joystick pad, int slotIndex = -1)
    {
        if (pad == null) return false;
        if (slotIndex >= 0)
        {
            string mapped = CoopControlsConfig.GetBindings(slotIndex).PadMenu;
            if (!string.IsNullOrEmpty(mapped))
            {
                if (GetButtonDownMapped(pad, mapped)) return true;
            }
        }
        return ButtonDown(pad, "Back", "Select", "View", "Share", "Touchpad Button", "TouchpadButton", "Touchpad", "Menu", "Menu Button", "Start", "Options");
    }
    internal static bool GetButtonDownMapped(Rewired.Joystick pad, string mapped)
    {
        if (pad == null || string.IsNullOrEmpty(mapped)) return false;
        if (ButtonDown(pad, mapped)) return true;
        if (AxisDown(pad, "MappedTrigger_" + mapped, mapped)) return true;
        return false;
    }
    internal static bool StartDown => GetStartDown(Pad);
    internal static bool MenuDown => GetMenuDown(Pad);

    internal static bool GetCancelDown(Rewired.Joystick pad) => ButtonDown(pad, "B Button", "Button B", "Action Button 2", "Circle", "Button 1", "B");
    internal static bool CancelDown => GetCancelDown(Pad);
    internal static bool LoreDown => ButtonDown(Pad, "Y Button", "Button Y", "Action Button 4", "Triangle", "Button 3", "Y");
    internal static bool SelectDown => GetSelectDown(Pad);

    internal static bool DashDown => GetDashDown(Pad);
    internal static bool DashHeld => GetDashHeld(Pad);
    internal static bool PrayerActivateDown => GetPrayerActivateDown(Pad);
    internal static bool PrayerActivateUp => GetPrayerActivateUp(Pad);

    private static readonly Dictionary<string, int> resolvedButtonIds = new Dictionary<string, int>();
    private static readonly Dictionary<string, int> resolvedAxisIds = new Dictionary<string, int>();
    private static readonly Dictionary<string, bool> lastAxisState = new Dictionary<string, bool>();
    private static bool loggedKnownButtons;
    private static bool loggedKnownAxes;

    internal static bool ButtonHeld(Rewired.Joystick pad, params string[] candidateNames)
    {
        if (pad == null) return false;
        int id = ResolveButtonId(pad, candidateNames);
        return id >= 0 && pad.GetButtonById(id);
    }

    private static bool ButtonHeld(params string[] candidateNames) => ButtonHeld(Pad, candidateNames);

    internal static bool ButtonDown(Rewired.Joystick pad, params string[] candidateNames)
    {
        if (pad == null) return false;
        int id = ResolveButtonId(pad, candidateNames);
        return id >= 0 && pad.GetButtonDownById(id);
    }

    private static bool ButtonDown(params string[] candidateNames) => ButtonDown(Pad, candidateNames);

    private static bool ButtonUp(Rewired.Joystick pad, params string[] candidateNames)
    {
        if (pad == null) return false;
        int id = ResolveButtonId(pad, candidateNames);
        return id >= 0 && pad.GetButtonUpById(id);
    }

    private static bool ButtonUp(params string[] candidateNames) => ButtonUp(Pad, candidateNames);

    private static bool AxisHeld(Rewired.Joystick pad, string cacheKey, params string[] candidateNames)
    {
        if (pad == null) return false;
        int id = ResolveAxisId(pad, cacheKey, candidateNames);
        return id >= 0 && Mathf.Abs(pad.GetAxisById(id)) >= AxisThreshold;
    }

    private static bool AxisHeld(string cacheKey, params string[] candidateNames) => AxisHeld(Pad, cacheKey, candidateNames);

    private static bool AxisDown(Rewired.Joystick pad, string cacheKey, params string[] candidateNames)
    {
        return TrackAxisEdge(pad, cacheKey, candidateNames) == 1;
    }

    private static bool AxisDown(string cacheKey, params string[] candidateNames) => AxisDown(Pad, cacheKey, candidateNames);

    private static bool AxisUp(Rewired.Joystick pad, string cacheKey, params string[] candidateNames)
    {
        return TrackAxisEdge(pad, cacheKey, candidateNames) == -1;
    }

    private static bool AxisUp(string cacheKey, params string[] candidateNames) => AxisUp(Pad, cacheKey, candidateNames);

    private static int TrackAxisEdge(Rewired.Joystick pad, string cacheKey, string[] candidateNames)
    {
        if (pad == null) return 0;
        int id = ResolveAxisId(pad, cacheKey, candidateNames);
        bool wasPressed;
        string key = $"{pad.id}_{cacheKey}";
        lastAxisState.TryGetValue(key, out wasPressed);
        bool isPressed = id >= 0 && Mathf.Abs(pad.GetAxisById(id)) >= AxisThreshold;
        lastAxisState[key] = isPressed;
        if (isPressed && !wasPressed) return 1;
        if (!isPressed && wasPressed) return -1;
        return 0;
    }

    private static int ResolveButtonId(Rewired.Joystick pad, string[] candidateNames)
    {
        if (pad == null) return -1;
        string cacheKey = $"{pad.id}_{candidateNames[0]}";
        int cached;
        if (resolvedButtonIds.TryGetValue(cacheKey, out cached)) return cached;

        LogKnownButtonsOnce(pad);
        int resolved = FindElementId(pad.ButtonElementIdentifiers, candidateNames);
        resolvedButtonIds[cacheKey] = resolved;
        return resolved;
    }

    private static int ResolveButtonId(string[] candidateNames) => ResolveButtonId(Pad, candidateNames);

    private static int ResolveAxisId(Rewired.Joystick pad, string cacheKey, string[] candidateNames)
    {
        if (pad == null) return -1;
        string key = $"{pad.id}_{cacheKey}";
        int cached;
        if (resolvedAxisIds.TryGetValue(key, out cached)) return cached;

        LogKnownAxesOnce(pad);
        int resolved = FindElementId(pad.AxisElementIdentifiers, candidateNames);
        resolvedAxisIds[key] = resolved;
        return resolved;
    }

    private static int ResolveAxisId(string cacheKey, string[] candidateNames) => ResolveAxisId(Pad, cacheKey, candidateNames);

    private static int FindElementId(System.Collections.Generic.IList<Rewired.ControllerElementIdentifier> identifiers, string[] candidateNames)
    {
        if (identifiers == null) return -1;

        // 1. Coincidencia exacta primero (para evitar que "A" o "X" coincidan con "D-Pad Down" o "Start")
        foreach (Rewired.ControllerElementIdentifier identifier in identifiers)
        {
            foreach (string candidate in candidateNames)
            {
                if (string.Equals(identifier.name, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return identifier.id;
                }
            }
        }

        // 2. Coincidencia por subcadena solo para nombres con longitud >= 3 (ej. "D-Pad", "Bumper", "Trigger")
        foreach (Rewired.ControllerElementIdentifier identifier in identifiers)
        {
            foreach (string candidate in candidateNames)
            {
                if (candidate.Length >= 3 && identifier.name.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return identifier.id;
                }
            }
        }
        return -1;
    }

    private static void LogKnownButtonsOnce(Rewired.Joystick pad)
    {
        if (loggedKnownButtons || Main.CoopLocal == null)
        {
            return;
        }
        loggedKnownButtons = true;

        List<string> entries = new List<string>();
        foreach (Rewired.ControllerElementIdentifier identifier in pad.ButtonElementIdentifiers)
        {
            entries.Add($"{identifier.id}:{identifier.name}");
        }
        Blasphemous.ModdingAPI.ModLog.Info(
            $"[Player2Pad] known gamepad buttons: {string.Join(", ", entries.ToArray())}", Main.CoopLocal);
    }

    private static void LogKnownAxesOnce(Rewired.Joystick pad)
    {
        if (loggedKnownAxes || Main.CoopLocal == null)
        {
            return;
        }
        loggedKnownAxes = true;

        List<string> entries = new List<string>();
        foreach (Rewired.ControllerElementIdentifier identifier in pad.AxisElementIdentifiers)
        {
            entries.Add($"{identifier.id}:{identifier.name}");
        }
        Blasphemous.ModdingAPI.ModLog.Info(
            $"[Player2Pad] known gamepad axes: {string.Join(", ", entries.ToArray())}", Main.CoopLocal);
    }
}
