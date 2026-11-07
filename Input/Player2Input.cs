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

// P2's input mode - Keyboard or Gamepad, toggled at runtime with Player2Input.ToggleKey (F9)
// instead of being fixed at compile time, since not everyone testing this has a second
// controller plugged in at all times. Round 32 tried auto-detecting this from which device P1
// was actively using; the user asked for that removed in favour of an explicit, user-driven
// choice instead - this is the "button to pick a mapping" version of that, just a keybind
// rather than a clickable menu for now (this mod has no Canvas/uGUI infrastructure at all yet -
// only world-space TextMesh labels - and no way to visually iterate on a real settings screen in
// the environment this was built in; a proper clickable UI is still on the table if the hotkey
// version doesn't cut it). Player2ModeIndicator shows the active mode as on-screen text so it
// doesn't have to be guessed from feel alone.
internal enum Player2InputMode
{
    Keyboard,
    Gamepad,
}

// Round 34: confirmed by the user that in Keyboard mode, P2 owns the *entire* keyboard now (not
// just arrows) - P1 no longer reads any keyboard input at all in this mode (see Player2Input's
// exclusivity section), so there's nothing left to share keys with. WASD for movement, matching
// the layout the user asked for; Attack/Parry/Jump/Dash on K/J/Space/LeftShift. Round 35 added
// Heal/Interact/Menu/PrayerActivate on R/E/I/U per the user's own explicit list.
internal static class Player2Keys
{
    internal const KeyCode Left = KeyCode.A;
    internal const KeyCode Right = KeyCode.D;
    internal const KeyCode Down = KeyCode.S;
    internal const KeyCode Up = KeyCode.W;
    internal const KeyCode Jump = KeyCode.Space;
    internal const KeyCode Dash = KeyCode.LeftShift;
    internal const KeyCode Attack = KeyCode.K;
    internal const KeyCode Parry = KeyCode.J;
    internal const KeyCode Heal = KeyCode.R;
    internal const KeyCode Interact = KeyCode.E;
    internal const KeyCode Menu = KeyCode.I;
    internal const KeyCode PrayerActivate = KeyCode.Q;
}

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
    private const int AxisLeftStickX = 0;
    private const int AxisLeftStickY = 1;
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

    internal static bool GetJumpHeld(Rewired.Joystick pad) =>
        ButtonHeld(pad, "A Button", "Button A", "Action Button 1", "Cross", "Button 0", "A");

    internal static bool GetSelectDown(Rewired.Joystick pad) =>
        ButtonDown(pad, "A Button", "Button A", "Action Button 1", "Cross", "Button 0", "A");

    internal static bool GetAttackDown(Rewired.Joystick pad) =>
        ButtonDown(pad, "X Button", "Button X", "Action Button 3", "Square", "Button 2", "X");

    internal static bool GetAttackUp(Rewired.Joystick pad) =>
        ButtonUp(pad, "X Button", "Button X", "Action Button 3", "Square", "Button 2", "X");

    internal static bool GetAttackHeld(Rewired.Joystick pad) =>
        ButtonHeld(pad, "X Button", "Button X", "Action Button 3", "Square", "Button 2", "X");

    internal static bool GetParryDown(Rewired.Joystick pad) =>
        ButtonDown(pad, "Left Bumper", "LB", "L1", "Left Shoulder");

    internal static bool GetHealDown(Rewired.Joystick pad) =>
        ButtonDown(pad, "Right Bumper", "RB", "R1", "Right Shoulder");

    internal static bool GetInteractDown(Rewired.Joystick pad) =>
        ButtonDown(pad, "Y Button", "Button Y", "Action Button 4", "Triangle", "Button 3", "Y");

    internal static bool GetDashDown(Rewired.Joystick pad) =>
        AxisDown(pad, "DashTrigger", "Right Trigger", "RT", "R2");

    internal static bool GetDashHeld(Rewired.Joystick pad) =>
        AxisHeld(pad, "DashTrigger", "Right Trigger", "RT", "R2");

    internal static bool GetPrayerActivateDown(Rewired.Joystick pad) =>
        AxisDown(pad, "PrayerTrigger", "Left Trigger", "LT", "L2");

    internal static bool GetPrayerActivateUp(Rewired.Joystick pad) =>
        AxisUp(pad, "PrayerTrigger", "Left Trigger", "LT", "L2");

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
    internal static bool GetStartDown(Rewired.Joystick pad) => ButtonDown(pad, "Start", "Options", "Menu", "Back", "Select", "View", "Share");
    internal static bool StartDown => GetStartDown(Pad);

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

    private static bool ButtonHeld(Rewired.Joystick pad, params string[] candidateNames)
    {
        if (pad == null) return false;
        int id = ResolveButtonId(pad, candidateNames);
        return id >= 0 && pad.GetButtonById(id);
    }

    private static bool ButtonHeld(params string[] candidateNames) => ButtonHeld(Pad, candidateNames);

    private static bool ButtonDown(Rewired.Joystick pad, params string[] candidateNames)
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

// Logs the raw index of every gamepad button the instant it's pressed or released (edge-
// triggered per index, so holding a button doesn't spam) - purely so a button's real index can
// be read directly off BepInEx/LogOutput.log instead of guessed, the same way every other
// cross-talk question in this file has been settled. Grep "[Player2Pad] raw gamepad button".
internal static class RawButtonScanLog
{
    private static readonly bool[] lastState = new bool[20];

    internal static void Tick()
    {
        Rewired.Joystick pad = Player2Pad.Pad;
        if (pad == null)
        {
            return;
        }
        for (int i = 0; i < lastState.Length; i++)
        {
            bool now = pad.GetButton(i);
            if (now == lastState[i])
            {
                continue;
            }
            lastState[i] = now;
            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info($"[Player2Pad] raw gamepad button {i} -> {now}", Main.CoopLocal);
            }
        }
    }
}

// Single dispatcher every patch below reads instead of Player2Keys/Player2Pad directly, so
// switching P2's mode at runtime (ToggleKey, F9) takes effect everywhere at once. Also owns
// device exclusivity for BOTH players, in both directions:
//
//   Gamepad mode: P2 = gamepad, P1 = keyboard only (any joystick removed from P1's Rewired
//   player). This is what round 33 already had - the user confirmed it works correctly, P1
//   doesn't react to the gamepad at all.
//
//   Keyboard mode (new, round 34): P2 = keyboard (Player2Keys, the full WASD scheme above),
//   P1 = gamepad only (the keyboard controller removed from P1's Rewired player instead). P1's
//   own vanilla, unpatched Update()/Rewired-driven logic keeps running exactly as it always has
//   (same reasoning as Gamepad mode: reimplementing P1's own nuanced ladder/cliff-grab/etc logic
//   raw, the way P2's has to be, is exactly the "too nuanced to safely reimplement" trap already
//   documented elsewhere in this file) - it just no longer has a keyboard in its device list to
//   read from, only the gamepad.
//
// Whichever direction is active, the *other* device type is deliberately left untouched on P1's
// Rewired player rather than force-added back - Rewired's own auto-assignment (re-enabled below
// whenever a device is released) picks it back up on its own once nothing else claims it
// exclusively.
//
// Known open question, unverified: this removes the ENTIRE keyboard controller from P1's Rewired
// player in Keyboard mode, which - if the game's "U"/"I" menu shortcuts are themselves read
// through Rewired on Player 0 rather than through a separate menu/UI input path - could
// theoretically also block them for P1. Every mode switch below logs P1's Rewired player's
// exact remaining controller list so this is directly checkable in BepInEx/LogOutput.log rather
// than assumed; if U/I stop opening menus for P1 specifically in Keyboard mode, report back and
// this needs a more surgical fix (leave the keyboard attached, patch P1's own gameplay reads
// individually instead - more code, but doesn't touch device assignment at all).
//
// The user also reported that a single one-time device removal (round 33's first attempt) didn't
// hold - both players kept responding to the same device. Most likely cause: Rewired's own
// automatic controller-to-player assignment (IControllerAssigner, runs continuously by default)
// silently re-attaching a freed device to Player 0 sometime after the one-time removal. Fixed two
// ways at once, in whichever direction is currently active: (1)
// p1Rewired.controllers.excludeFromControllerAutoAssignment = true stops Rewired's auto-assigner
// from re-offering *any* controller to Player 0, and (2) as a belt-and-braces safety net,
// EnsureExclusiveDevices() re-clears the excluded type every frame, not just once -
// ClearControllersOfType on an already-empty list is a cheap no-op either way.
internal static class Player2Input
{
    private const KeyCode ToggleKey = KeyCode.F9;

    internal static int ActiveKeyboardPlayerIndex { get; private set; } = 0; // 0=P1, 1=P2, 2=P3, 3=P4

    internal static Player2InputMode Mode { get; set; } = Player2InputMode.Gamepad;

    internal static void CycleKeyboardPlayer()
    {
        int activeCount = CoopGameConfig.IsCoopEnabled ? Mathf.Clamp(CoopGameConfig.PlayerCount, 2, 4) : 1;
        if (activeCount <= 1)
        {
            ApplyKeyboardPlayer(0);
            return;
        }

        ActiveKeyboardPlayerIndex = (ActiveKeyboardPlayerIndex + 1) % activeCount;
        ApplyKeyboardPlayer(ActiveKeyboardPlayerIndex);
    }

    internal static void ApplyKeyboardPlayer(int playerIndex)
    {
        int activeCount = CoopGameConfig.IsCoopEnabled ? Mathf.Clamp(CoopGameConfig.PlayerCount, 2, 4) : 1;
        if (playerIndex >= activeCount)
        {
            playerIndex = 0;
        }
        ActiveKeyboardPlayerIndex = Mathf.Clamp(playerIndex, 0, activeCount - 1);

        int gamepadNum = 1;
        for (int i = 0; i < 4; i++)
        {
            if (i == ActiveKeyboardPlayerIndex)
            {
                CoopGameConfig.Slots[i].Device = CoopDeviceType.Keyboard;
            }
            else
            {
                CoopGameConfig.Slots[i].Device = (CoopDeviceType)(gamepadNum + 1); // Gamepad1..4
                gamepadNum++;
            }
        }

        // P2 lee teclado si ActiveKeyboardPlayerIndex >= 1
        Mode = (ActiveKeyboardPlayerIndex >= 1) ? Player2InputMode.Keyboard : Player2InputMode.Gamepad;

        ApplyExclusiveDevices();

        string pName = CoopGameConfig.Slots[ActiveKeyboardPlayerIndex].Name;
        if (string.IsNullOrEmpty(pName))
        {
            pName = CoopPlayerRegistry.GetDisplayName(ActiveKeyboardPlayerIndex);
        }

        Player2ModeIndicator.ShowPlayer(ActiveKeyboardPlayerIndex, pName);

        Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
        LogModeAndControllers($"F9 -> Teclado asignado a P{ActiveKeyboardPlayerIndex + 1} ({pName})");
    }

    // Métodos para consultar input por ranura de jugador (P2=1, P3=2, P4=3)
    internal static bool GetLeft(int slotIndex)
    {
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKey(Player2Keys.Left);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetLeft(pad);
    }

    internal static bool GetRight(int slotIndex)
    {
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKey(Player2Keys.Right);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetRight(pad);
    }

    internal static bool GetUp(int slotIndex)
    {
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKey(Player2Keys.Up);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetUp(pad);
    }

    internal static bool GetDown(int slotIndex)
    {
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKey(Player2Keys.Down);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetDown(pad);
    }

    internal static bool GetJumpHeld(int slotIndex)
    {
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKey(Player2Keys.Jump);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetJumpHeld(pad);
    }

    internal static bool GetAttackDown(int slotIndex)
    {
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKeyDown(Player2Keys.Attack);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetAttackDown(pad);
    }

    internal static bool GetAttackUp(int slotIndex)
    {
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKeyUp(Player2Keys.Attack);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetAttackUp(pad);
    }

    internal static bool GetAttackHeld(int slotIndex)
    {
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKey(Player2Keys.Attack);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetAttackHeld(pad);
    }

    internal static bool GetDashDown(int slotIndex)
    {
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKeyDown(Player2Keys.Dash);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetDashDown(pad);
    }

    internal static bool GetDashHeld(int slotIndex)
    {
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKey(Player2Keys.Dash);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetDashHeld(pad);
    }

    internal static bool GetParryDown(int slotIndex)
    {
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKeyDown(Player2Keys.Parry);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetParryDown(pad);
    }

    internal static bool GetHealDown(int slotIndex)
    {
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKeyDown(Player2Keys.Heal);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetHealDown(pad);
    }

    internal static bool GetInteractDown(int slotIndex)
    {
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKeyDown(Player2Keys.Interact);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetInteractDown(pad);
    }

    internal static bool GetPrayerActivateDown(int slotIndex)
    {
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKeyDown(Player2Keys.PrayerActivate);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetPrayerActivateDown(pad);
    }

    internal static bool GetPrayerActivateUp(int slotIndex)
    {
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKeyUp(Player2Keys.PrayerActivate);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetPrayerActivateUp(pad);
    }

    internal static bool Left => Mode == Player2InputMode.Gamepad ? Player2Pad.Left : Input.GetKey(Player2Keys.Left);
    internal static bool Right => Mode == Player2InputMode.Gamepad ? Player2Pad.Right : Input.GetKey(Player2Keys.Right);
    internal static bool Up => Mode == Player2InputMode.Gamepad ? Player2Pad.Up : Input.GetKey(Player2Keys.Up);
    internal static bool Down => Mode == Player2InputMode.Gamepad ? Player2Pad.Down : Input.GetKey(Player2Keys.Down);
    internal static bool JumpHeld => Mode == Player2InputMode.Gamepad ? Player2Pad.JumpHeld : Input.GetKey(Player2Keys.Jump);

    // Round 44: edge-triggered jump press, tracked once per Tick() - needed for GrabLadder's
    // ladder-dismount trigger (see GrabLadder_OnUpdate_P2_Patch), which needs a GetButtonDown-style
    // edge rather than JumpHeld's continuous state.
    internal static bool JumpDown { get; private set; }
    private static bool previousJumpHeld;
    internal static bool AttackDown => Mode == Player2InputMode.Gamepad ? Player2Pad.AttackDown : Input.GetKeyDown(Player2Keys.Attack);
    internal static bool AttackUp => Mode == Player2InputMode.Gamepad ? Player2Pad.AttackUp : Input.GetKeyUp(Player2Keys.Attack);
    // Continuous "is the attack button currently held" - added for WallJump (see
    // Abilities/WallJump.cs), which checks this every frame rather than watching for a fresh
    // press, unlike every other Attack read in this mod so far.
    internal static bool AttackHeld => Mode == Player2InputMode.Gamepad ? Player2Pad.AttackHeld : Input.GetKey(Player2Keys.Attack);
    internal static bool DashDown => Mode == Player2InputMode.Gamepad ? Player2Pad.DashDown : Input.GetKeyDown(Player2Keys.Dash);
    internal static bool ParryDown => Mode == Player2InputMode.Gamepad ? Player2Pad.ParryDown : Input.GetKeyDown(Player2Keys.Parry);
    internal static bool HealDown => Mode == Player2InputMode.Gamepad ? Player2Pad.HealDown : Input.GetKeyDown(Player2Keys.Heal);
    internal static bool InteractDown => Mode == Player2InputMode.Gamepad ? Player2Pad.InteractDown : Input.GetKeyDown(Player2Keys.Interact);
    internal static bool PrayerActivateDown => Mode == Player2InputMode.Gamepad ? Player2Pad.PrayerActivateDown : Input.GetKeyDown(Player2Keys.PrayerActivate);
    internal static bool PrayerActivateUp => Mode == Player2InputMode.Gamepad ? Player2Pad.PrayerActivateUp : Input.GetKeyUp(Player2Keys.PrayerActivate);

    internal static bool GetMenuDown(int slotIndex)
    {
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKeyDown(Player2Keys.Menu);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return pad != null && Player2Pad.GetStartDown(pad);
    }

    internal static bool MenuDown => GetMenuDown(1);

    private static bool everTicked;

    // Checked every frame from PlatformCharacterInput_Update_Patch (already runs every frame for
    // P2, so this rides along for free instead of needing its own separate Update hook).
    internal static void Tick()
    {
        bool jumpHeldNow = JumpHeld;
        JumpDown = jumpHeldNow && !previousJumpHeld;
        previousJumpHeld = jumpHeldNow;

        if (!everTicked)
        {
            everTicked = true;
            int initialKb = 0;
            for (int i = 0; i < 4; i++)
            {
                if (CoopGameConfig.Slots[i].Device == CoopDeviceType.Keyboard)
                {
                    initialKb = i;
                    break;
                }
            }
            ApplyKeyboardPlayer(initialKb);
        }

        if (Input.GetKeyDown(ToggleKey))
        {
            CycleKeyboardPlayer();
        }
        else
        {
            EnsureExclusiveDevices();
        }

        if (Mode == Player2InputMode.Gamepad)
        {
            RawButtonScanLog.Tick();

            // Ronda 89: Atajo en F6 y F7 (en Gamepad) — además se evalúa globalmente abajo.
        }

        // Ronda 92: Atajo F8 reservado para depuración de estadísticas / guardado de layout en árbol

        // Ronda 93: Atajo F5 para invocar o reiniciar un enemigo Dummy de pruebas frente al jugador
        if (Input.GetKeyDown(KeyCode.F5))
        {
            Combat.TestDummyManager.SpawnOrResetDummy();
        }

        // Atajo F2: Selector de jefes para práctica / depuración (solo cuando CoopGameSetupUI no esté abierto)
        if (Input.GetKeyDown(KeyCode.F2) && !CoopGameSetupUI.IsOpen)
        {
            Combat.BossDebugTeleportManager.ToggleMenu();
        }

        // Dentro de Tick(), lógica P2 para abrir menú de inventario y atajo F6/F7 de fervor máximo:
        if (Input.GetKeyDown(KeyCode.F6) || Input.GetKeyDown(KeyCode.F7))
        {
            var p1 = Framework.Managers.Core.Logic.Penitent;
            if (p1 != null && p1.Stats != null && p1.Stats.Fervour != null)
            {
                p1.Stats.Fervour.SetToCurrentMax();
            }
            var p2 = CoopLocal.Player2;
            if (p2 != null && p2.Stats != null && p2.Stats.Fervour != null)
            {
                p2.Stats.Fervour.SetToCurrentMax();
                Player2StatsSync.SaveCurrentVitals(p2);
            }
            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info("[FervourShortcut] F6/F7 pressed -> P1 & P2 Fervour set to CurrentMax", Main.CoopLocal);
            }
        }
        int activeGuestCount = Mathf.Clamp(CoopGameConfig.PlayerCount, 2, 4);
        for (int p = 1; p < activeGuestCount; p++)
        {
            if (GetMenuDown(p) && Gameplay.UI.UIController.instance != null && Time.frameCount != Player2MenuView.LastMenuToggleFrame)
            {
                NewInventoryWidget widget = NewInventoryMenuField.GetValue(Gameplay.UI.UIController.instance) as NewInventoryWidget;
                bool isMenuOpen = widget != null && widget.currentlyActive;
                if (!isMenuOpen)
                {
                    // Menú cerrado: abrir directamente para el jugador p
                    Player2MenuView.RequestOpenAsPlayer(p);
                    Player2MenuView.LastMenuToggleFrame = Time.frameCount;
                    Gameplay.UI.UIController.instance.ToggleInventoryMenu();
                }
                else if (Player2MenuView.ActivePlayerIndex == p)
                {
                    // Ya está viendo este jugador: cerrar menú
                    Player2MenuView.LastMenuToggleFrame = Time.frameCount;
                    Gameplay.UI.UIController.instance.ToggleInventoryMenu();
                }
                else
                {
                    // Menú ya abierto en otro jugador: cambiar la vista directamente al jugador p
                    Player2MenuView.SetActivePlayer(p);
                    object currentTab = CurrentTabTypeField.GetValue(widget);
                    SelectTabMethod.Invoke(widget, new object[] { currentTab, false });
                    RefreshDeviceAssignment();
                    CoopMenuOwnerBanner.Refresh(widget);
                    Player2MenuView.LastMenuToggleFrame = Time.frameCount;
                }
                break;
            }
        }
// Ronda 49: ocultado a pedido del usuario - el tuner interactivo usa flechas/./-/+, las
        // mismas teclas con historial de solaparse con P1. La clase sigue intacta en
        // HUD/HudPositionTuner.cs por si se necesita retomar el ajuste manual del HUD más adelante;
        // solo se dejó de invocar su Tick().
        // Player2HudPositionTuner.Tick();
Player2PurgePoints.Tick();
        }

        // Opening the shared inventory/prayer menu isn't per-player state (there's only one
        // save's worth of inventory/prayers), so this just calls the same public method the
        // game's own menu button does - no Ability/Rewired-owner scoping needed.
// Ronda 49: ocultado a pedido del usuario - el tuner interactivo usa flechas/./-/+, las

    private static readonly System.Reflection.FieldInfo NewInventoryMenuField =
        AccessTools.Field(typeof(Gameplay.UI.UIController), "newInventoryMenu");
    private static readonly System.Reflection.FieldInfo CurrentTabTypeField =
        AccessTools.Field(typeof(NewInventoryWidget), "currentTabType");
    private static readonly System.Reflection.MethodInfo SelectTabMethod =
        AccessTools.Method(typeof(NewInventoryWidget), "SelectTab",
            new[] { typeof(NewInventoryWidget.TabType), typeof(bool) });

    internal static void RefreshDeviceAssignment()
    {
        ApplyExclusiveDevices();
    }

    private static Rewired.ControllerType ExcludedFromPlayer0
    {
        get
        {
            bool guestDriving = Player2MenuView.IsInventoryP2View;
            if (guestDriving)
            {
                int drivingPlayer = Player2MenuView.ActivePlayerIndex;
                bool isKeyboardPlayer = (drivingPlayer == ActiveKeyboardPlayerIndex);
                return isKeyboardPlayer ? Rewired.ControllerType.Joystick : Rewired.ControllerType.Keyboard;
            }

            // Si P1 tiene el teclado (ActiveKeyboardPlayerIndex == 0), excluimos Joystick de Player 0.
            // Si P2, P3 o P4 tienen el teclado (ActiveKeyboardPlayerIndex != 0), excluimos Teclado de Player 0.
            return (ActiveKeyboardPlayerIndex == 0) ? Rewired.ControllerType.Joystick : Rewired.ControllerType.Keyboard;
        }
    }

    private static void ApplyExclusiveDevices()
    {
        Rewired.Player p1Rewired = GetP1Rewired();
        if (p1Rewired == null)
        {
            return;
        }
        p1Rewired.controllers.excludeFromControllerAutoAssignment = true;
        p1Rewired.controllers.ClearControllersOfType(ExcludedFromPlayer0);
        ReattachAllowedDevice(p1Rewired);
    }

    private static void ReattachAllowedDevice(Rewired.Player p1Rewired)
    {
        Rewired.ControllerType allowed = ExcludedFromPlayer0 == Rewired.ControllerType.Joystick
            ? Rewired.ControllerType.Keyboard
            : Rewired.ControllerType.Joystick;

        if (allowed == Rewired.ControllerType.Keyboard)
        {
            var keyboard = Rewired.ReInput.controllers.Keyboard;
            if (keyboard != null && !p1Rewired.controllers.ContainsController(keyboard))
            {
                p1Rewired.controllers.AddController(keyboard, false);
            }
        }
        else
        {
            int drivingPlayer = Player2MenuView.ActivePlayerIndex;
            Rewired.Joystick pad = (drivingPlayer > 0)
                ? Player2Pad.GetPadForSlot(drivingPlayer)
                : Player2Pad.P1Pad;
            if (pad != null && !p1Rewired.controllers.ContainsController(pad))
            {
                p1Rewired.controllers.AddController(pad, false);
            }
        }
    }

    // Re-clears every frame - see class comment above for why a single one-time clear (round
    // 33's original approach) wasn't enough on its own.
    private static void EnsureExclusiveDevices()
    {
        Rewired.Player p1Rewired = GetP1Rewired();
        if (p1Rewired == null)
        {
            return;
        }
        Rewired.ControllerType excluded = ExcludedFromPlayer0;
        int before = excluded == Rewired.ControllerType.Joystick
            ? p1Rewired.controllers.joystickCount
            : (p1Rewired.controllers.ContainsController(Rewired.ControllerType.Keyboard, 0) ? 1 : 0);
        if (before > 0)
        {
            p1Rewired.controllers.ClearControllersOfType(excluded);
            LogModeAndControllers(
                $"Player 0 still had a {excluded} controller assigned - cleared again (swapping for menu/gameplay).");
        }
        ReattachAllowedDevice(p1Rewired);
    }

    private static void LogModeAndControllers(string message)
    {
        if (Main.CoopLocal == null)
        {
            return;
        }
        Rewired.Player p1Rewired = GetP1Rewired();
        string controllerList = p1Rewired == null
            ? "n/a"
            : string.Join(", ", System.Linq.Enumerable.ToArray(
                System.Linq.Enumerable.Select(p1Rewired.controllers.Controllers, c => $"{c.type}:{c.name}")));
        Blasphemous.ModdingAPI.ModLog.Info($"{message} P1's Rewired controllers now: [{controllerList}]", Main.CoopLocal);
    }

    private static Rewired.Player GetP1Rewired()
    {
        if (Rewired.ReInput.players != null && Rewired.ReInput.players.playerCount > 0)
        {
            return Rewired.ReInput.players.GetPlayer(0);
        }
        Penitent p1 = Core.Logic.Penitent;
        return p1 != null ? p1.PlatformCharacterInput?.Rewired : null;
    }
}

// On-screen "Controller mode" / "Keyboard mode" text, top-right corner - so P2's active input
// scheme never has to be guessed from feel/trial-and-error alone. Built from a bare Canvas +
// UI.Text rather than TextMeshPro (avoids needing a font asset reference) since this mod has no
// existing Canvas/uGUI infrastructure to build on. Created once, lazily, and left alive for the
// whole session (DontDestroyOnLoad) rather than tied to either player's own lifetime - the mode
// itself isn't tied to a spawned player either.
internal static class Player2ModeIndicator
{
    // Round 50: confirmed via a one-shot dump of every font asset loaded in memory
    // (Resources.FindObjectsOfTypeAll<TMP_FontAsset>()) that the game's real Latin-alphabet TMP
    // font is "MajesticExtended_FullLatin" - the other names found were either locale-specific
    // font swaps (Russian/Chinese/Korean/Japanese) or TMP's own LiberationSans SDF fallback. It's
    // a serialized asset reference, not something loadable by path/code, so it has to be found
    // this way rather than referenced directly.
    private const string GameFontName = "MajesticExtended_FullLatin";

    private static TMPro.TextMeshProUGUI label;

    internal static void SetMenuOverlayHidden(bool hidden)
    {
        if (label != null)
        {
            label.gameObject.SetActive(!hidden);
        }
    }

    internal static void ShowPlayer(int playerIndex, string playerName)
    {
        EnsureCreated();
        if (label != null)
        {
            int total = CoopGameConfig.IsCoopEnabled ? Mathf.Clamp(CoopGameConfig.PlayerCount, 2, 4) : 1;
            label.text = $"[F9] Teclado: P{playerIndex + 1} ({playerName}) [Partida: {total}P]";
            label.color = CoopPlayerRegistry.GetLabelColor(playerIndex);
        }
    }

    internal static void Show(Player2InputMode mode)
    {
        EnsureCreated();
        if (label != null)
        {
            int kbIdx = Player2Input.ActiveKeyboardPlayerIndex;
            string pName = CoopPlayerRegistry.GetDisplayName(kbIdx);
            ShowPlayer(kbIdx, pName);
        }
    }

    private static void EnsureCreated()
    {
        if (label != null)
        {
            return;
        }

        GameObject canvasObject = new GameObject("CoopLocalModeIndicatorCanvas");
        UnityEngine.Object.DontDestroyOnLoad(canvasObject);
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;

        GameObject textObject = new GameObject("ModeText");
        textObject.transform.SetParent(canvasObject.transform, worldPositionStays: false);
        RectTransform rect = textObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-16f, -16f);
        rect.sizeDelta = new Vector2(480f, 40f);

        label = textObject.AddComponent<TMPro.TextMeshProUGUI>();
        TMPro.TMP_FontAsset gameFont = Array.Find(
            Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>(),
            f => f.name == GameFontName);
        if (gameFont != null)
        {
            label.font = gameFont;
        }
        label.fontSize = 22;
        label.alignment = TMPro.TextAlignmentOptions.TopRight;
        label.color = Color.white;
        label.text = "";
    }
}


