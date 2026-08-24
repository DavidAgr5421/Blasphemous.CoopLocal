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
    // Round 46: moved from U to Q per explicit user request - U is being kept free for something
    // planned later, not yet assigned to anything.
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

    internal static Rewired.Joystick Pad
    {
        get
        {
            var joysticks = Rewired.ReInput.controllers.Joysticks;
            return joysticks.Count > 0 ? joysticks[0] : null;
        }
    }

    // Round 46: left stick only originally - user explicitly asked for the D-Pad to also work as
    // a fixed digital movement option alongside the analog stick, not replacing it. The pad's own
    // "known gamepad buttons" log (confirmed earlier this session) lists "D-Pad Up/Right/Down/
    // Left" as real button names, same by-name resolution as every other digital button here.
    internal static bool Left => (Pad != null && Pad.GetAxis(AxisLeftStickX) <= -AxisThreshold) || ButtonHeld("D-Pad Left", "DPad Left", "Dpad Left");
    internal static bool Right => (Pad != null && Pad.GetAxis(AxisLeftStickX) >= AxisThreshold) || ButtonHeld("D-Pad Right", "DPad Right", "Dpad Right");
    internal static bool Up => (Pad != null && Pad.GetAxis(AxisLeftStickY) >= AxisThreshold) || ButtonHeld("D-Pad Up", "DPad Up", "Dpad Up");
    internal static bool Down => (Pad != null && Pad.GetAxis(AxisLeftStickY) <= -AxisThreshold) || ButtonHeld("D-Pad Down", "DPad Down", "Dpad Down");

    internal static bool JumpHeld => ButtonHeld("A Button", "A");
    internal static bool AttackDown => ButtonDown("X Button", "X");
    internal static bool AttackUp => ButtonUp("X Button", "X");
    internal static bool ParryDown => ButtonDown("Left Bumper", "LB", "L1", "Left Shoulder");
    internal static bool HealDown => ButtonDown("Right Bumper", "RB", "R1", "Right Shoulder");
    internal static bool InteractDown => ButtonDown("Y Button", "Y");

    internal static bool DashDown => AxisDown("DashTrigger", "Right Trigger", "RT", "R2");
    internal static bool PrayerActivateDown => AxisDown("PrayerTrigger", "Left Trigger", "LT", "L2");
    internal static bool PrayerActivateUp => AxisUp("PrayerTrigger", "Left Trigger", "LT", "L2");

    private static readonly Dictionary<string, int> resolvedButtonIds = new Dictionary<string, int>();
    private static readonly Dictionary<string, int> resolvedAxisIds = new Dictionary<string, int>();
    private static readonly Dictionary<string, bool> lastAxisState = new Dictionary<string, bool>();
    private static bool loggedKnownButtons;
    private static bool loggedKnownAxes;

    private static bool ButtonHeld(params string[] candidateNames)
    {
        int id = ResolveButtonId(candidateNames);
        return id >= 0 && Pad.GetButtonById(id);
    }

    private static bool ButtonDown(params string[] candidateNames)
    {
        int id = ResolveButtonId(candidateNames);
        return id >= 0 && Pad.GetButtonDownById(id);
    }

    private static bool ButtonUp(params string[] candidateNames)
    {
        int id = ResolveButtonId(candidateNames);
        return id >= 0 && Pad.GetButtonUpById(id);
    }

    // cacheKey is a stable name for this *action* (not itself a candidate to match against),
    // since the same physical axis can back both a Down and an Up read (PrayerActivate) and both
    // need to observe the exact same edge-tracking state.
    private static bool AxisDown(string cacheKey, params string[] candidateNames)
    {
        return TrackAxisEdge(cacheKey, candidateNames) == 1;
    }

    private static bool AxisUp(string cacheKey, params string[] candidateNames)
    {
        return TrackAxisEdge(cacheKey, candidateNames) == -1;
    }

    // Returns 1 on the press edge, -1 on the release edge, 0 otherwise (held or not-pressed).
    private static int TrackAxisEdge(string cacheKey, string[] candidateNames)
    {
        int id = ResolveAxisId(cacheKey, candidateNames);
        bool wasPressed;
        lastAxisState.TryGetValue(cacheKey, out wasPressed);
        bool isPressed = id >= 0 && Mathf.Abs(Pad.GetAxisById(id)) >= AxisThreshold;
        lastAxisState[cacheKey] = isPressed;
        if (isPressed && !wasPressed)
        {
            return 1;
        }
        if (!isPressed && wasPressed)
        {
            return -1;
        }
        return 0;
    }

    private static int ResolveButtonId(string[] candidateNames)
    {
        Rewired.Joystick pad = Pad;
        if (pad == null)
        {
            return -1;
        }

        string cacheKey = candidateNames[0];
        int cached;
        if (resolvedButtonIds.TryGetValue(cacheKey, out cached))
        {
            return cached;
        }

        LogKnownButtonsOnce(pad);

        int resolved = FindElementId(pad.ButtonElementIdentifiers, candidateNames);
        resolvedButtonIds[cacheKey] = resolved;
        if (resolved < 0 && Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info(
                $"[Player2Pad] could not find a gamepad BUTTON named like '{string.Join("/", candidateNames)}' - " +
                "see the button list above/below and add the real name to the candidates.",
                Main.CoopLocal);
        }
        return resolved;
    }

    private static int ResolveAxisId(string cacheKey, string[] candidateNames)
    {
        Rewired.Joystick pad = Pad;
        if (pad == null)
        {
            return -1;
        }

        int cached;
        if (resolvedAxisIds.TryGetValue(cacheKey, out cached))
        {
            return cached;
        }

        LogKnownAxesOnce(pad);

        int resolved = FindElementId(pad.AxisElementIdentifiers, candidateNames);
        resolvedAxisIds[cacheKey] = resolved;
        if (resolved < 0 && Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info(
                $"[Player2Pad] could not find a gamepad AXIS named like '{string.Join("/", candidateNames)}' - " +
                "see the axis list above/below and add the real name to the candidates.",
                Main.CoopLocal);
        }
        return resolved;
    }

    private static int FindElementId(System.Collections.Generic.IList<Rewired.ControllerElementIdentifier> identifiers, string[] candidateNames)
    {
        foreach (Rewired.ControllerElementIdentifier identifier in identifiers)
        {
            foreach (string candidate in candidateNames)
            {
                if (identifier.name.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0)
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

    internal static Player2InputMode Mode { get; private set; } = Player2InputMode.Gamepad;

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
    internal static bool DashDown => Mode == Player2InputMode.Gamepad ? Player2Pad.DashDown : Input.GetKeyDown(Player2Keys.Dash);
    internal static bool ParryDown => Mode == Player2InputMode.Gamepad ? Player2Pad.ParryDown : Input.GetKeyDown(Player2Keys.Parry);
    internal static bool HealDown => Mode == Player2InputMode.Gamepad ? Player2Pad.HealDown : Input.GetKeyDown(Player2Keys.Heal);
    internal static bool InteractDown => Mode == Player2InputMode.Gamepad ? Player2Pad.InteractDown : Input.GetKeyDown(Player2Keys.Interact);
    internal static bool PrayerActivateDown => Mode == Player2InputMode.Gamepad ? Player2Pad.PrayerActivateDown : Input.GetKeyDown(Player2Keys.PrayerActivate);
    internal static bool PrayerActivateUp => Mode == Player2InputMode.Gamepad ? Player2Pad.PrayerActivateUp : Input.GetKeyUp(Player2Keys.PrayerActivate);

    // Keyboard-only - opening the shared inventory/prayer menu isn't gamepad-mapped (not asked
    // for), so this is a plain false in Gamepad mode rather than a guessed button.
    internal static bool MenuDown => Mode == Player2InputMode.Keyboard && Input.GetKeyDown(Player2Keys.Menu);

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
            ApplyExclusiveDevices();
            Player2ModeIndicator.Show(Mode);
            LogModeAndControllers($"P2 input mode starting as -> {Mode}");
        }

        if (Input.GetKeyDown(ToggleKey))
        {
            Mode = Mode == Player2InputMode.Gamepad ? Player2InputMode.Keyboard : Player2InputMode.Gamepad;
            ApplyExclusiveDevices();
            Player2ModeIndicator.Show(Mode);
            string suffix = Mode == Player2InputMode.Gamepad && Player2Pad.Pad == null ? " (no gamepad detected!)" : "";
            LogModeAndControllers($"P2 input mode -> {Mode}{suffix}");
        }
        else
        {
            EnsureExclusiveDevices();
        }

        if (Mode == Player2InputMode.Gamepad)
        {
            RawButtonScanLog.Tick();
        }

        Player2HudPositionTuner.Tick();
        Player2PurgePoints.Tick();

        // Opening the shared inventory/prayer menu isn't per-player state (there's only one
        // save's worth of inventory/prayers), so this just calls the same public method the
        // game's own menu button does - no Ability/Rewired-owner scoping needed.
        if (MenuDown && Gameplay.UI.UIController.instance != null)
        {
            Gameplay.UI.UIController.instance.ToggleInventoryMenu();
        }
    }

    private static Rewired.ControllerType ExcludedFromPlayer1 =>
        Mode == Player2InputMode.Gamepad ? Rewired.ControllerType.Joystick : Rewired.ControllerType.Keyboard;

    private static void ApplyExclusiveDevices()
    {
        Rewired.Player p1Rewired = GetP1Rewired();
        if (p1Rewired == null)
        {
            return;
        }
        // excludeFromControllerAutoAssignment blocks Rewired's auto-assigner from giving P1
        // *any* controller, not just the excluded type - confirmed live (round 34 testing):
        // toggling Keyboard mode then back to Gamepad mode left P1 with neither Keyboard nor
        // Joystick, since the flag being on the whole time meant nothing ever got auto-reattached.
        // So the allowed type has to be re-added explicitly here rather than left to Rewired.
        p1Rewired.controllers.excludeFromControllerAutoAssignment = true;
        p1Rewired.controllers.ClearControllersOfType(ExcludedFromPlayer1);
        ReattachAllowedDevice(p1Rewired);
    }

    private static void ReattachAllowedDevice(Rewired.Player p1Rewired)
    {
        if (Mode == Player2InputMode.Gamepad)
        {
            var keyboard = Rewired.ReInput.controllers.Keyboard;
            if (keyboard != null && !p1Rewired.controllers.ContainsController(keyboard))
            {
                p1Rewired.controllers.AddController(keyboard, false);
            }
        }
        else
        {
            Rewired.Joystick pad = Player2Pad.Pad;
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
        Rewired.ControllerType excluded = ExcludedFromPlayer1;
        int before = excluded == Rewired.ControllerType.Joystick
            ? p1Rewired.controllers.joystickCount
            : (p1Rewired.controllers.ContainsController(Rewired.ControllerType.Keyboard, 0) ? 1 : 0);
        if (before > 0)
        {
            p1Rewired.controllers.ClearControllersOfType(excluded);
            LogModeAndControllers(
                $"P1's Rewired player still had a {excluded} controller assigned - cleared again " +
                "(Rewired's auto-assigner likely re-attached it since the last check).");
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
        Penitent p1 = Core.Logic.Penitent;
        return p1 != null ? p1.PlatformCharacterInput.Rewired : null;
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
    private static Text label;

    internal static void Show(Player2InputMode mode)
    {
        EnsureCreated();
        if (label != null)
        {
            label.text = mode == Player2InputMode.Gamepad ? "Controller mode" : "Keyboard mode";
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
        rect.sizeDelta = new Vector2(320f, 40f);

        label = textObject.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        label.fontSize = 22;
        label.alignment = TextAnchor.UpperRight;
        label.color = Color.white;
        label.text = "";
    }
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
        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info("[DashParryDebug] " + message, Main.CoopLocal);
        }
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
        bool rawParryKeyDown = Player2Input.ParryDown;
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
// A stale keyboard-numpad concern from the pre-gamepad-split era (see Player2Pad's comment for
// current history) - CameraPan's own numpad-driven manual camera panning (Rewired axes 20/21,
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
    private static bool unattacableBefore;
    private static bool invulnerableBefore;
    private static bool isHurtBefore;

    // Round 48: user reports P2 taking damage specifically while performing an upward/side
    // attack ("parece daño por contacto al atacar hacia arriba o al lado") - a fresh live log
    // showed 13 of 16 P2 damage events landing exactly 1-2 frames after P2 entered
    // "Player_Upward_Attack_Clamped_anim" specifically. PenitentDamageArea.TakeDamage/CanTakeHit
    // are both confirmed correctly per-instance already (no hardcoded Core.Logic.Penitent
    // anywhere in that chain), and GroundHurtBehaviour/AirHurtBehaviour's own owner-fix patches
    // (which set Status.Unattacable during the post-hit invulnerability window) were checked and
    // are structurally correct too - no code-level cause has been confirmed yet, so capturing
    // Unattacable/Invulnerable/IsHurt state *before* the hit resolves (Prefix) is the next
    // concrete thing needed: either these flags were already true and got bypassed somehow (a
    // real bug), or they were genuinely false (meaning this really is just BellGhost's own attack
    // landing at the same moment the player swings - ordinary difficulty, not a mod bug).
    private static void Prefix(PenitentDamageArea __instance)
    {
        Penitent owner = PenitentField.GetValue(__instance) as Penitent;
        lifeBefore = owner != null ? owner.Stats.Life.Current : -1f;
        unattacableBefore = owner != null && owner.Status.Unattacable;
        invulnerableBefore = owner != null && owner.Status.Invulnerable;
        isHurtBefore = owner != null && owner.Status.IsHurt;
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
            $"damageType={hit.DamageType} lifeBefore={lifeBefore:F1} lifeAfter={lifeAfter:F1} " +
            $"unattacableBefore={unattacableBefore} invulnerableBefore={invulnerableBefore} isHurtBefore={isHurtBefore} | {ownerLabel}Pos={ownerPos} " +
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
    // Round 44: was a const - promoted to a mutable field so Player2HudPositionTuner's "." / "-"
    // scale keys can adjust it live.
    internal static float Scale = 0.65f;

    // Round 45: final position, re-confirmed by the user via live Player2HudPositionTuner testing.
    internal static Vector2 AnchoredPosition = new Vector2(-119f, -20f);

    private static readonly MethodInfo OnPenitentReadyMethod = AccessTools.Method(typeof(PlayerHealth), "OnPenitentReady");

    internal static PlayerHealth Instance { get; private set; }
    internal static RectTransform CloneRect => instanceRoot != null ? instanceRoot.GetComponent<RectTransform>() : null;

    // Round 41: user reported Health not visually rendering on top of Fervour's own group
    // (which drags in the whole "LeftPart" portrait/frame - see Player2FervourBar's class
    // comment). Unity UI renders later siblings on top of earlier ones, and Fervour is created
    // *after* Health in CoopLocal.OnPlayerSpawn, so Health's smaller clone was sitting behind
    // Fervour's larger one regardless of anchored position. Called from CoopLocal.cs after all
    // three P2 HUD clones exist (not from inside Health's own EnsureCreated, which runs before
    // Fervour is even created yet and so can't fix this from within itself).
    internal static void BringToFront()
    {
        if (instanceRoot != null)
        {
            instanceRoot.transform.SetAsLastSibling();
        }
    }

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
            // Round 38: visual now confirmed complete (portrait/frame/bar all show correctly) -
            // moved to the bottom-right corner as originally asked, aligned with
            // Player2FervourBar below (same X inset, Fervour stacked directly under Health by
            // FervourVerticalOffset). Pivot (1,0) = anchoredPosition is measured from this
            // object's own bottom-right corner, so a negative X / positive Y inset pulls it away
            // from the screen's actual corner instead of clipping off it - the exact inset values
            // are a best-effort guess (this environment can't screenshot the live HUD to check),
            // so this will likely still need one more visual tuning pass.
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = AnchoredPosition;
            rect.localScale *= Scale;
            DashParryDebugLog.Log($"Player2HealthBar.EnsureCreated: positioned clone at bottom-right, anchoredPosition={rect.anchoredPosition}, localScale={rect.localScale}");
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

// Round 37: second HUD widget for P2, per the user's request to extend Player2HealthBar's
// approach to Fervour too. Same clone-then-redirect mechanism, with one extra wrinkle
// PlayerHealth didn't have: PlayerFervour.Awake() does `Instance = this` unconditionally (a
// static singleton - other code, e.g. Healing.Heal()'s spark effect, calls
// PlayerFervour.Instance.ShowSpark() expecting P1's real bar). Cloning it would otherwise
// silently steal the global Instance for the clone and break that for P1 - EnsureCreated resets
// Instance back to the original immediately after creating the clone to prevent this;
// Player2FervourBar.Instance (not the static PlayerFervour.Instance) is what every patch below
// actually checks against.
//
// Decompiling PlayerFervour turned up FIVE separate methods independently hardcoding
// Core.Logic.Penitent (BarTarget, CalculateBarSize, CalculateFillsBars, CalculateMarks,
// CalculateBarPentalty) - more than PlayerHealth's two. Reimplemented here, following the same
// proven approach: CalculateBarSize (controls the bar's rendered width - the most visually
// broken without a fix) and CalculateFillsBars (the actual fill-amount animation, computed
// directly from P2's own stats rather than through the possibly-inlined BarTarget getter, same
// as PlayerHealth's CalculateHealthBar/CalculateLossBar). CalculateMarks (segment tick marks) and
// CalculateBarPentalty (the "guilt" overlay bar) are NOT reimplemented yet - known gap, left
// running unmodified (so they'll still read P1's numbers for those two specific visual details)
// rather than guessing their IL translations blind on top of everything else this round already
// covers; revisit if the user reports those specific pieces looking wrong for P2.
internal static class Player2FervourBar
{
    // Round 44: was a const - promoted to a mutable field so Player2HudPositionTuner's "." / "-"
    // scale keys can adjust it live.
    internal static float Scale = 0.65f;

    // PlayerFervour.Instance's setter is private (Awake() calls it on itself) - reflection is the
    // only way to reset the global singleton back to the original after the clone's own Awake()
    // steals it.
    private static readonly FieldInfo GlobalInstanceField =
        AccessTools.Field(typeof(PlayerFervour), "<Instance>k__BackingField");

    // Round 45: final position, re-confirmed by the user via live Player2HudPositionTuner testing
    // (stacked below Health, which renders on top per the z-order fix).
    internal static Vector2 AnchoredPosition = new Vector2(-75f, 7f);

    internal static PlayerFervour Instance { get; private set; }

    // Round 40: PlayerFlask ("Flask0"/"Flask1"/... potion sprites) lives as a sibling inside the
    // same "LeftPart" hierarchy this class already clones wholesale (see the class comment above -
    // follow-up #8's sibling dump listed "Flask" alongside "Fervour Bar"/"Penitence"/etc) - it
    // rides along as an unpatched, un-redirected duplicate unless something registers and
    // redirects it too, which is exactly why P2's potion count was frozen showing P1's count from
    // the moment of cloning (4 slots, never decreasing) instead of P2's own (2 slots, live).
    internal static PlayerFlask FlaskInstance { get; private set; }

    internal static RectTransform CloneRect => instanceRoot != null ? instanceRoot.GetComponent<RectTransform>() : null;
    private static PlayerFervour originalCache;
    private static GameObject instanceRoot;

    internal static void EnsureCreated(Penitent p2)
    {
        if (instanceRoot != null)
        {
            UnityEngine.Object.Destroy(instanceRoot);
            instanceRoot = null;
            Instance = null;
            FlaskInstance = null;
        }

        if (originalCache == null)
        {
            originalCache = UnityEngine.Object.FindObjectOfType<PlayerFervour>();
        }
        PlayerFervour original = originalCache;
        if (original == null || p2 == null)
        {
            DashParryDebugLog.Log($"Player2FervourBar.EnsureCreated: aborted - original PlayerFervour found={original != null}, p2 found={p2 != null}");
            return;
        }

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

        Transform originalParent = original.transform.parent;
        GameObject sourceToClone = originalParent != null ? originalParent.gameObject : original.gameObject;

        GameObject cloneObject = UnityEngine.Object.Instantiate(sourceToClone, cloneParent);
        cloneObject.name = "PlayerFervour_P2";
        instanceRoot = cloneObject;
        Instance = cloneObject.GetComponentInChildren<PlayerFervour>();
        FlaskInstance = cloneObject.GetComponentInChildren<PlayerFlask>();

        // Undo the clone's own Awake() stealing the global static Instance - see class comment.
        if (Instance != null)
        {
            GlobalInstanceField.SetValue(null, original);
        }

        RectTransform rect = cloneObject.GetComponent<RectTransform>();
        if (rect != null)
        {
            // Same bottom-right corner as Player2HealthBar, positioned via AnchoredPosition above.
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = AnchoredPosition;
            rect.localScale *= Scale;
        }

        DashParryDebugLog.Log(
            $"Player2FervourBar.EnsureCreated: cloned from '{sourceToClone.name}', foundPlayerFervour={Instance != null}, " +
            $"globalInstanceRestored={(PlayerFervour.Instance == original)}, foundPlayerFlask={FlaskInstance != null}");
    }
}

[HarmonyPatch(typeof(PlayerFervour), "get_BarTarget")]
internal static class PlayerFervour_BarTarget_P2_Patch
{
    private static void Postfix(PlayerFervour __instance, ref float __result)
    {
        if (__instance != Player2FervourBar.Instance)
        {
            return;
        }
        Penitent p2 = CoopLocal.Player2;
        __result = p2 != null ? p2.Stats.Fervour.Current / p2.Stats.Fervour.CurrentMaxWithoutFactor : 0f;
    }
}

// Round 41: the user reported the cloned Fervour bar frozen - never changing or resetting, even
// though CalculateBarSize/CalculateFillsBars below were already correctly redirected. Root cause:
// those two are only ever CALLED from Update() when `lastValue != this.BarTarget` - and Update()
// itself was never patched, so it's still running vanilla, reading BarTarget through a plain
// `call` to the small property getter. That's the exact same "small property likely gets JIT-
// inlined past a Harmony Postfix" risk already proven real for PlayerHealth's own BarTarget (see
// that class's comments) - if Update()'s own BarTarget read is inlined, it keeps comparing
// against *P1's* ratio internally, so unless P1's Fervour happens to change too, the "did it
// change" check never trips and CalculateBarSize/CalculateFillsBars simply never get called for
// P2's clone at all - leaving it stuck at whatever it displayed at spawn. Fixed by reimplementing
// Update() itself (mirroring PlayerHealth's CalculateHealthBar/CalculateLossBar being fully
// reimplemented rather than just patching what they call) - computing barTarget directly from
// P2's own stats, then invoking the *already-patched* CalculateBarSize/CalculateFillsBars
// methods via reflection (Harmony patches the underlying method itself, so a reflection Invoke()
// call from here still runs through those Prefixes correctly - no inlining risk for this call
// site since it's our own C# code, not vanilla's). CalculateMarks/CalculateBarPentalty are still
// unpatched (existing known gap - they'll run with their own internal Core.Logic.Penitent reads).
[HarmonyPatch(typeof(PlayerFervour), "Update")]
internal static class PlayerFervour_Update_P2_Patch
{
    private static readonly FieldInfo NormalPrayerInUseField = AccessTools.Field(typeof(PlayerFervour), "normalPrayerInUse");
    private static readonly FieldInfo Pe02PrayerInUseField = AccessTools.Field(typeof(PlayerFervour), "pe02PrayerInUse");
    private static readonly FieldInfo PrayerTimerField = AccessTools.Field(typeof(PlayerFervour), "prayerTimer");
    private static readonly FieldInfo LastValueField = AccessTools.Field(typeof(PlayerFervour), "lastValue");
    private static readonly FieldInfo FillsIncreaseField = AccessTools.Field(typeof(PlayerFervour), "fillsIncrease");
    private static readonly FieldInfo TimeElapsedField = AccessTools.Field(typeof(PlayerFervour), "_timeElapsed");
    private static readonly FieldInfo LastMaxFervourField = AccessTools.Field(typeof(PlayerFervour), "lastMaxFervour");
    private static readonly MethodInfo CalculateBarSizeMethod = AccessTools.Method(typeof(PlayerFervour), "CalculateBarSize");
    private static readonly MethodInfo CalculateFillsBarsMethod = AccessTools.Method(typeof(PlayerFervour), "CalculateFillsBars");
    private static readonly MethodInfo CalculateMarksMethod = AccessTools.Method(typeof(PlayerFervour), "CalculateMarks");
    private static readonly MethodInfo CalculateNotEnoughMethod = AccessTools.Method(typeof(PlayerFervour), "CalculateNotEnough");
    private static readonly MethodInfo CalculateBarPentaltyMethod = AccessTools.Method(typeof(PlayerFervour), "CalculateBarPentalty");
    private static readonly FieldInfo DiagFillExactField = AccessTools.Field(typeof(PlayerFervour), "fillExact");
    private static readonly FieldInfo DiagFillAnimableField = AccessTools.Field(typeof(PlayerFervour), "fillAnimable");

    // Round 43: the HIT and MISS branches previously shared one throttle counter - since both
    // P1's real instance (MISS) and P2's clone (HIT) call Update() every frame, whichever one
    // Unity happened to process first each frame "won" the shared 60-frame window and starved the
    // other branch's log out entirely - confirmed live (an entire test session only ever logged
    // MISS lines, never once HIT, even though the user's own report proves P2's bar *does*
    // respond). Separate counters per branch so both get logged independently.
    private static int lastLoggedMissFrame = -999;
    private static int lastLoggedHitFrame = -999;

    private static bool Prefix(PlayerFervour __instance)
    {
        if (__instance != Player2FervourBar.Instance)
        {
            if (Main.CoopLocal != null && Time.frameCount - lastLoggedMissFrame >= 60)
            {
                lastLoggedMissFrame = Time.frameCount;
                Penitent owner = __instance.GetComponentInParent<Penitent>();
                // Fervour bars live under a UI Canvas, not physically parented under the Penitent
                // transform - GetComponentInParent<Penitent>() reliably returns null for *every*
                // PlayerFervour instance (P1's real one included), confirmed live, so it can't be
                // used to identify which instance this is. Not chasing that further this round.
                Blasphemous.ModdingAPI.ModLog.Info(
                    $"[FervourDiag] Update() MISS: instance={__instance.GetInstanceID()} owner={DashParryDebugLog.Label(owner)} " +
                    $"Player2FervourBar.Instance={(Player2FervourBar.Instance != null ? Player2FervourBar.Instance.GetInstanceID().ToString() : "null")} " +
                    $"gameObject='{__instance.gameObject.name}' active={__instance.gameObject.activeInHierarchy}",
                    Main.CoopLocal);
            }
            return true;
        }
        Penitent p2 = CoopLocal.Player2;
        if (p2 == null)
        {
            return false;
        }

        if (Main.CoopLocal != null && Time.frameCount - lastLoggedHitFrame >= 60)
        {
            lastLoggedHitFrame = Time.frameCount;
            Image diagFillExact = (Image)DiagFillExactField.GetValue(__instance);
            Image diagFillAnimable = (Image)DiagFillAnimableField.GetValue(__instance);
            Blasphemous.ModdingAPI.ModLog.Info(
                $"[FervourDiag] Update() HIT: instance={__instance.GetInstanceID()} P2.Fervour.Current={p2.Stats.Fervour.Current:F1} " +
                $"P2.Fervour.CurrentMaxWithoutFactor={p2.Stats.Fervour.CurrentMaxWithoutFactor:F1} lastValue={LastValueField.GetValue(__instance)} " +
                $"fillExact.fillAmount={(diagFillExact != null ? diagFillExact.fillAmount.ToString("F3") : "null")} " +
                $"fillAnimable.fillAmount={(diagFillAnimable != null ? diagFillAnimable.fillAmount.ToString("F3") : "null")} " +
                $"gameObject='{__instance.gameObject.name}' active={__instance.gameObject.activeInHierarchy}",
                Main.CoopLocal);
        }

        PrayerUse prayerCast = p2.PrayerCast;
        bool isUsing = prayerCast != null && prayerCast.IsUsingAbility;
        bool useStocksOfHealth = Core.PenitenceManager.UseStocksOfHealth;
        ((GameObject)NormalPrayerInUseField.GetValue(__instance)).SetActive(isUsing && !useStocksOfHealth);
        ((GameObject)Pe02PrayerInUseField.GetValue(__instance)).SetActive(isUsing && useStocksOfHealth);

        float castFillAmount = isUsing ? 1f - prayerCast.GetPercentTimeCasting() : 0f;
        ((Image)PrayerTimerField.GetValue(__instance)).fillAmount = castFillAmount;

        // Round 40 fix: decompiled the REAL Update() body with ICSharpCode.Decompiler (actual C#,
        // not raw IL) and it does NOT gate CalculateBarSize/CalculateFillsBars/CalculateMarks/
        // CalculateNotEnough behind "did barTarget change" the way round 41's version (and this
        // Prefix, until now) assumed - vanilla calls all four UNCONDITIONALLY every single Update()
        // tick. The "if (lastValue != barTarget)" check only resets fillsIncrease/lastValue/
        // _timeElapsed (the direction/timer for the lerp animation) - it is NOT a call-gate. Putting
        // the four Calculate calls inside that gate (as before) meant CalculateFillsBars - which
        // does the actual per-frame Mathf.Lerp animation toward BarTarget - only ever ran ONCE per
        // change instead of continuously, so the fill visually took one lerp step and then froze
        // until the target changed again. This was the best explanation found for "no se actualiza
        // en tiempo real" via static analysis - **the user still reports it broken after this fix**
        // (round 42), so either this wasn't the whole story or something else is also wrong; the
        // enriched [FervourDiag] log above is there to pin down which from real data.
        float barTarget = p2.Stats.Fervour.CurrentMaxWithoutFactor > 0f
            ? p2.Stats.Fervour.Current / p2.Stats.Fervour.CurrentMaxWithoutFactor
            : 0f;
        float lastValue = (float)LastValueField.GetValue(__instance);
        if (!Mathf.Approximately(lastValue, barTarget))
        {
            FillsIncreaseField.SetValue(__instance, barTarget > lastValue);
            LastValueField.SetValue(__instance, barTarget);
            TimeElapsedField.SetValue(__instance, 0f);
        }
        CalculateBarSizeMethod.Invoke(__instance, null);
        CalculateFillsBarsMethod.Invoke(__instance, null);
        CalculateMarksMethod.Invoke(__instance, null);
        CalculateNotEnoughMethod.Invoke(__instance, null);

        float maxFervour = p2.Stats.Fervour.CurrentMaxWithoutFactor;
        float lastMaxFervour = (float)LastMaxFervourField.GetValue(__instance);
        if (!Mathf.Approximately(maxFervour, lastMaxFervour))
        {
            LastMaxFervourField.SetValue(__instance, maxFervour);
            CalculateBarPentaltyMethod.Invoke(__instance, null);
        }
        return false;
    }
}

[HarmonyPatch(typeof(PlayerFervour), "CalculateBarSize")]
internal static class PlayerFervour_CalculateBarSize_P2_Patch
{
    private static readonly FieldInfo LastBarWidthField = AccessTools.Field(typeof(PlayerFervour), "lastBarWidth");
    private static readonly FieldInfo BackgroundStartSizeField = AccessTools.Field(typeof(PlayerFervour), "backgroundStartSize");
    private static readonly FieldInfo EndFillSizeField = AccessTools.Field(typeof(PlayerFervour), "endFillSize");
    private static readonly FieldInfo BackgroundMidField = AccessTools.Field(typeof(PlayerFervour), "backgroundMid");
    private static readonly FieldInfo FillExactTransformField = AccessTools.Field(typeof(PlayerFervour), "fillExactTransform");
    private static readonly FieldInfo FillExactFullTransformField = AccessTools.Field(typeof(PlayerFervour), "fillExactFullTransform");
    private static readonly FieldInfo FillAnimableTransformField = AccessTools.Field(typeof(PlayerFervour), "fillAnimableTransform");
    private static readonly FieldInfo BackgroundField = AccessTools.Field(typeof(PlayerFervour), "background");
    private static readonly FieldInfo FillNotEnoughTransformField = AccessTools.Field(typeof(PlayerFervour), "fillNotEnoughTransform");

    private static bool Prefix(PlayerFervour __instance)
    {
        if (__instance != Player2FervourBar.Instance)
        {
            return true;
        }
        Penitent p2 = CoopLocal.Player2;
        if (p2 == null)
        {
            return false;
        }

        float maxWithoutFactor = p2.Stats.Fervour.CurrentMaxWithoutFactor;
        float lastBarWidth = (float)LastBarWidthField.GetValue(__instance);
        if (maxWithoutFactor == lastBarWidth)
        {
            return false;
        }
        LastBarWidthField.SetValue(__instance, maxWithoutFactor);

        float backgroundStartSize = (float)BackgroundStartSizeField.GetValue(__instance);
        float endFillSize = (float)EndFillSizeField.GetValue(__instance);
        float width = Mathf.Max(maxWithoutFactor - backgroundStartSize - endFillSize, 0f);

        SetWidth(BackgroundMidField, __instance, width);
        SetWidth(FillExactTransformField, __instance, maxWithoutFactor);
        SetWidth(FillExactFullTransformField, __instance, maxWithoutFactor);
        SetWidth(FillAnimableTransformField, __instance, maxWithoutFactor);
        SetWidth(BackgroundField, __instance, maxWithoutFactor);
        SetWidth(FillNotEnoughTransformField, __instance, maxWithoutFactor);
        return false;
    }

    private static void SetWidth(FieldInfo field, PlayerFervour instance, float width)
    {
        RectTransform rect = (RectTransform)field.GetValue(instance);
        rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
    }
}

[HarmonyPatch(typeof(PlayerFervour), "CalculateFillsBars")]
internal static class PlayerFervour_CalculateFillsBars_P2_Patch
{
    private static readonly FieldInfo TimeElapsedField = AccessTools.Field(typeof(PlayerFervour), "_timeElapsed");
    private static readonly FieldInfo FillsIncreaseField = AccessTools.Field(typeof(PlayerFervour), "fillsIncrease");
    private static readonly FieldInfo FillExactField = AccessTools.Field(typeof(PlayerFervour), "fillExact");
    private static readonly FieldInfo FillAnimableField = AccessTools.Field(typeof(PlayerFervour), "fillAnimable");
    private static readonly FieldInfo FillNotEnoughField = AccessTools.Field(typeof(PlayerFervour), "fillNotEnough");
    private static readonly FieldInfo AddAnimationCurveField = AccessTools.Field(typeof(PlayerFervour), "AddAnimationCurve");
    private static readonly FieldInfo LossAnimationCurveField = AccessTools.Field(typeof(PlayerFervour), "LossAnimationCurve");
    private static readonly FieldInfo FervourSparkField = AccessTools.Field(typeof(PlayerFervour), "fervourSpark");

    private static bool Prefix(PlayerFervour __instance)
    {
        if (__instance != Player2FervourBar.Instance)
        {
            return true;
        }
        Penitent p2 = CoopLocal.Player2;
        if (p2 == null)
        {
            return false;
        }

        float barTarget = p2.Stats.Fervour.Current / p2.Stats.Fervour.CurrentMaxWithoutFactor;
        float maxWithoutFactor = p2.Stats.Fervour.CurrentMaxWithoutFactor;
        float timeElapsed = (float)TimeElapsedField.GetValue(__instance) + Time.deltaTime;
        TimeElapsedField.SetValue(__instance, timeElapsed);

        Image fillExact = (Image)FillExactField.GetValue(__instance);
        Image fillAnimable = (Image)FillAnimableField.GetValue(__instance);
        Image fillNotEnough = (Image)FillNotEnoughField.GetValue(__instance);
        bool fillsIncrease = (bool)FillsIncreaseField.GetValue(__instance);

        if (fillsIncrease)
        {
            if (Mathf.Approximately(fillExact.fillAmount, barTarget))
            {
                fillExact.fillAmount = barTarget;
                TimeElapsedField.SetValue(__instance, 0f);
            }
            else
            {
                AnimationCurve addCurve = (AnimationCurve)AddAnimationCurveField.GetValue(__instance);
                fillExact.fillAmount = Mathf.Lerp(fillExact.fillAmount, barTarget, addCurve.Evaluate(timeElapsed));

                float sparkX = (float)(int)maxWithoutFactor * fillExact.fillAmount - 1f;
                GameObject spark = (GameObject)FervourSparkField.GetValue(__instance);
                Vector3 sparkPos = spark.transform.localPosition;
                spark.transform.localPosition = new Vector3(sparkX, sparkPos.y);
            }
            // Round 40: real decompiled source sets this unconditionally at the end of the
            // fillsIncrease branch (both the "reached target" and "still lerping" paths), not only
            // inside the lerping else - the previous version left fillAnimable one step stale on
            // the exact frame the target is reached.
            fillAnimable.fillAmount = fillExact.fillAmount;
        }
        else
        {
            fillExact.fillAmount = barTarget;
            if (Mathf.Approximately(fillAnimable.fillAmount, barTarget))
            {
                fillAnimable.fillAmount = barTarget;
                TimeElapsedField.SetValue(__instance, 0f);
            }
            else
            {
                AnimationCurve lossCurve = (AnimationCurve)LossAnimationCurveField.GetValue(__instance);
                fillAnimable.fillAmount = Mathf.Lerp(fillAnimable.fillAmount, barTarget, lossCurve.Evaluate(timeElapsed));
            }
        }
        fillNotEnough.fillAmount = fillExact.fillAmount;
        return false;
    }
}

// Round 43: found the real cause of "reduce el fervor a 0 igual aparece en el HUD como a la
// mitad" - CalculateMarks() was the one remaining unredirected Calculate method (documented as a
// "tick marks" known gap since round 37/38, but it turns out to control far more than cosmetic
// tick marks). It computes `fillExactFull.fillAmount` - a *visible* fill layer rendered alongside
// fillExact/fillAnimable (both already correctly redirected) - straight from
// Core.Logic.Penitent.Stats.Fervour.Current (always P1). Since P1 and P2's Fervour *max* now
// matches after the stat-sync feature, the segment/tick-mark *positions* this method computes
// (based on CurrentMax) happen to come out identical either way - but the *fill ratio itself*
// (based on Current, which genuinely differs per player) was still showing P1's percentage
// regardless of P2's real value, which is exactly the "stuck at half" symptom reported. Full
// reimplementation, mirroring CalculateBarSize/CalculateFillsBars's own approach - every
// Core.Logic.Penitent read redirected to p2, private fields/method accessed via reflection.
[HarmonyPatch(typeof(PlayerFervour), "CalculateMarks")]
internal static class PlayerFervour_CalculateMarks_P2_Patch
{
    private static readonly FieldInfo FillExactFullField = AccessTools.Field(typeof(PlayerFervour), "fillExactFull");
    private static readonly FieldInfo EpsilonToShowLastBarField = AccessTools.Field(typeof(PlayerFervour), "epsilonToShowLastBar");
    private static readonly FieldInfo CurrentMarksField = AccessTools.Field(typeof(PlayerFervour), "currentMarks");
    private static readonly FieldInfo CurrentMarksSeparationField = AccessTools.Field(typeof(PlayerFervour), "currentMarksSeparation");
    private static readonly FieldInfo CurrentSegmentsFilledField = AccessTools.Field(typeof(PlayerFervour), "currentSegmentsFilled");
    private static readonly FieldInfo MarksParentField = AccessTools.Field(typeof(PlayerFervour), "marksParent");
    private static readonly FieldInfo BarMaskChildNameField = AccessTools.Field(typeof(PlayerFervour), "barMaskChildName");
    private static readonly FieldInfo BarBarChildNameField = AccessTools.Field(typeof(PlayerFervour), "barBarChildName");
    private static readonly FieldInfo BarAnimChildNameField = AccessTools.Field(typeof(PlayerFervour), "barAnimChildName");
    private static readonly FieldInfo BarAnimEndPositionField = AccessTools.Field(typeof(PlayerFervour), "barAnimEndPosition");
    private static readonly FieldInfo BarAnimMovementPerElapsedField = AccessTools.Field(typeof(PlayerFervour), "barAnimMovementPerElapsed");
    private static readonly FieldInfo BarAnimUpdatedElapsedField = AccessTools.Field(typeof(PlayerFervour), "barAnimUpdatedElapsed");
    private static readonly FieldInfo CurrentAnimPositionField = AccessTools.Field(typeof(PlayerFervour), "currentAnimPosition");
    private static readonly FieldInfo CurrentAnimElapsedField = AccessTools.Field(typeof(PlayerFervour), "currentAnimElapsed");
    private static readonly FieldInfo AnimsField = AccessTools.Field(typeof(PlayerFervour), "anims");
    private static readonly MethodInfo SetBarPositionMethod = AccessTools.Method(typeof(PlayerFervour), "SetBarPosition");

    private static bool Prefix(PlayerFervour __instance)
    {
        if (__instance != Player2FervourBar.Instance)
        {
            return true;
        }
        Penitent p2 = CoopLocal.Player2;
        if (p2 == null)
        {
            return false;
        }

        int num = 0;
        float num2 = 0f;
        Framework.Inventory.Prayer prayerInSlot = Core.InventoryManager.GetPrayerInSlot(0);
        int num3 = prayerInSlot != null ? prayerInSlot.fervourNeeded + (int)p2.Stats.PrayerCostAddition.Final : 0;
        Image fillExactFull = (Image)FillExactFullField.GetValue(__instance);
        if (num3 > 0)
        {
            num = (int)p2.Stats.Fervour.CurrentMax / num3;
            num2 = (int)p2.Stats.Fervour.Current / num3;
            fillExactFull.fillAmount = num2 * num3 / p2.Stats.Fervour.CurrentMaxWithoutFactor;
        }
        else
        {
            fillExactFull.fillAmount = 0f;
        }

        float epsilonToShowLastBar = (float)EpsilonToShowLastBarField.GetValue(__instance);
        bool showLastBar = p2.Stats.Fervour.CurrentMax - num3 * num > epsilonToShowLastBar;
        bool skippedAnimReset = false;
        float restPosition = -num3 + 1f;

        int currentMarks = (int)CurrentMarksField.GetValue(__instance);
        int currentMarksSeparation = (int)CurrentMarksSeparationField.GetValue(__instance);
        float currentSegmentsFilled = (float)CurrentSegmentsFilledField.GetValue(__instance);

        if (num != currentMarks || num3 != currentMarksSeparation || num2 != currentSegmentsFilled)
        {
            float currentAnimPosition = (float)CurrentAnimPositionField.GetValue(__instance);
            int barAnimEndPosition = (int)BarAnimEndPositionField.GetValue(__instance);
            List<RectTransform> anims = (List<RectTransform>)AnimsField.GetValue(__instance);

            if (num == 0)
            {
                currentAnimPosition = restPosition;
                CurrentAnimElapsedField.SetValue(__instance, 0f);
                skippedAnimReset = true;
            }
            anims.Clear();
            if (currentAnimPosition > barAnimEndPosition)
            {
                currentAnimPosition = restPosition;
            }
            CurrentAnimPositionField.SetValue(__instance, currentAnimPosition);

            CurrentMarksField.SetValue(__instance, num);
            CurrentMarksSeparationField.SetValue(__instance, num3);
            CurrentSegmentsFilledField.SetValue(__instance, num2);

            Transform marksParent = (Transform)MarksParentField.GetValue(__instance);
            string barMaskChildName = (string)BarMaskChildNameField.GetValue(__instance);
            string barBarChildName = (string)BarBarChildNameField.GetValue(__instance);
            string barAnimChildName = (string)BarAnimChildNameField.GetValue(__instance);

            float xPos = 0f;
            for (int i = 0; i < marksParent.childCount; i++)
            {
                RectTransform rectTransform = (RectTransform)marksParent.GetChild(i);
                bool active = i < num;
                rectTransform.gameObject.SetActive(active);
                if (!active)
                {
                    continue;
                }
                rectTransform.sizeDelta = new Vector2(num3, rectTransform.sizeDelta.y);
                rectTransform.localPosition = new Vector3(xPos, 0f, 0f);
                xPos += num3;
                RectTransform mask = (RectTransform)rectTransform.Find(barMaskChildName);
                mask.sizeDelta = new Vector2(num3 - 1f, mask.sizeDelta.y);
                RectTransform bar = (RectTransform)rectTransform.Find(barBarChildName);
                bar.gameObject.SetActive(showLastBar || i != num - 1);
                bool filled = i < currentSegmentsFilled;
                RectTransform anim = (RectTransform)mask.Find(barAnimChildName);
                anim.gameObject.SetActive(filled);
                if (filled)
                {
                    SetBarPositionMethod.Invoke(__instance, new object[] { anim });
                    anims.Add(anim);
                }
            }
        }

        if (skippedAnimReset || num <= 0)
        {
            return false;
        }

        float elapsed = (float)CurrentAnimElapsedField.GetValue(__instance) + Time.deltaTime;
        float barAnimUpdatedElapsed = (float)BarAnimUpdatedElapsedField.GetValue(__instance);
        if (elapsed >= barAnimUpdatedElapsed)
        {
            elapsed = 0f;
            float pos = (float)CurrentAnimPositionField.GetValue(__instance) + (float)BarAnimMovementPerElapsedField.GetValue(__instance);
            int barAnimEndPosition = (int)BarAnimEndPositionField.GetValue(__instance);
            if (pos > barAnimEndPosition)
            {
                pos = restPosition;
            }
            CurrentAnimPositionField.SetValue(__instance, pos);
            List<RectTransform> anims = (List<RectTransform>)AnimsField.GetValue(__instance);
            foreach (RectTransform anim in anims)
            {
                SetBarPositionMethod.Invoke(__instance, new object[] { anim });
            }
        }
        CurrentAnimElapsedField.SetValue(__instance, elapsed);
        return false;
    }
}

// Round 40: P2's potion (Flask) HUD - user reported it showing a static 4 potions (P1's own count,
// frozen at whatever it was the instant Player2FervourBar's wholesale "LeftPart" clone was made)
// instead of P2's real 2, and never decreasing on use. PlayerFlask rides along inside that same
// clone as an untouched duplicate (see Player2FervourBar.FlaskInstance's own comment) - decompiled
// via ICSharpCode.Decompiler (real C#, not raw IL) to get an exact reimplementation: RefreshFlask()
// hardcodes Core.Logic.Penitent in three reads (Stats.Flask, Stats.FlaskHealth.PermanetBonus,
// Stats.FlaskHealthUpgrade) - redirected to P2 here, called unconditionally every frame from
// Update() with no inlining-gate risk (unlike Fervour's BarTarget/Update() saga), so a direct
// Prefix on RefreshFlask() itself is sufficient.
[HarmonyPatch(typeof(PlayerFlask), "RefreshFlask")]
internal static class PlayerFlask_RefreshFlask_P2_Patch
{
    private static readonly FieldInfo FlasksField = AccessTools.Field(typeof(PlayerFlask), "flasks");
    private static readonly FieldInfo FlasksFullField = AccessTools.Field(typeof(PlayerFlask), "flasksFull");
    private static readonly FieldInfo FlasksEmptyField = AccessTools.Field(typeof(PlayerFlask), "flasksEmpty");
    private static readonly FieldInfo FlasksFullFervourField = AccessTools.Field(typeof(PlayerFlask), "flasksFullFervour");
    private static readonly FieldInfo CurrentFlaskNumberField = AccessTools.Field(typeof(PlayerFlask), "currentFlaskNumber");
    private static readonly FieldInfo CurrentFlaskFullField = AccessTools.Field(typeof(PlayerFlask), "currentFlaskFull");
    private static readonly FieldInfo CurrentFlaskLevelField = AccessTools.Field(typeof(PlayerFlask), "currentFlaskLevel");
    private static readonly FieldInfo CurrentFlaskIsFervourField = AccessTools.Field(typeof(PlayerFlask), "currentFlaskIsFervour");
    private static readonly FieldInfo SwordHeart06Field = AccessTools.Field(typeof(PlayerFlask), "swordHeart06");

    private static bool Prefix(PlayerFlask __instance)
    {
        if (__instance != Player2FervourBar.FlaskInstance)
        {
            return true;
        }
        Penitent p2 = CoopLocal.Player2;
        if (p2 == null)
        {
            return false;
        }

        List<Image> flasks = (List<Image>)FlasksField.GetValue(__instance);
        List<Sprite> flasksFull = (List<Sprite>)FlasksFullField.GetValue(__instance);
        List<Sprite> flasksEmpty = (List<Sprite>)FlasksEmptyField.GetValue(__instance);
        List<Sprite> flasksFullFervour = (List<Sprite>)FlasksFullFervourField.GetValue(__instance);
        if (flasks == null || flasks.Count == 0)
        {
            return false;
        }

        Framework.FrameworkCore.Attributes.Flask flask = p2.Stats.Flask;
        int level = (int)(p2.Stats.FlaskHealth.PermanetBonus / p2.Stats.FlaskHealthUpgrade);
        if (level > flasksEmpty.Count)
        {
            level = flasksEmpty.Count;
        }

        Framework.Inventory.Sword swordHeart06 = (Framework.Inventory.Sword)SwordHeart06Field.GetValue(__instance);
        if (swordHeart06 == null)
        {
            swordHeart06 = Core.InventoryManager.GetSword("HE06");
            SwordHeart06Field.SetValue(__instance, swordHeart06);
        }

        if (swordHeart06 != null && swordHeart06.IsEquiped)
        {
            for (int i = 0; i < flasks.Count; i++)
            {
                flasks[i].gameObject.SetActive(false);
            }
            flask.Current = 0f;
            return false;
        }

        float currentFlaskNumber = (float)CurrentFlaskNumberField.GetValue(__instance);
        float currentFlaskFull = (float)CurrentFlaskFullField.GetValue(__instance);
        float currentFlaskLevel = (float)CurrentFlaskLevelField.GetValue(__instance);
        bool currentFlaskIsFervour = (bool)CurrentFlaskIsFervourField.GetValue(__instance);

        if (currentFlaskNumber == flask.Final && currentFlaskFull == flask.Current && currentFlaskLevel == (float)level
            && flasks[0].gameObject.activeInHierarchy && currentFlaskIsFervour == Core.PenitenceManager.UseFervourFlasks)
        {
            return false;
        }

        CurrentFlaskIsFervourField.SetValue(__instance, Core.PenitenceManager.UseFervourFlasks);
        CurrentFlaskNumberField.SetValue(__instance, flask.Final);
        CurrentFlaskFullField.SetValue(__instance, flask.Current);
        CurrentFlaskLevelField.SetValue(__instance, (float)level);

        for (int j = 0; j < flasks.Count; j++)
        {
            if ((float)j < flask.Current)
            {
                flasks[j].sprite = Core.PenitenceManager.UseFervourFlasks ? flasksFullFervour[level] : flasksFull[level];
                flasks[j].gameObject.SetActive(true);
            }
            else if ((float)j < flask.Final)
            {
                flasks[j].sprite = flasksEmpty[level];
                flasks[j].gameObject.SetActive(true);
            }
            else
            {
                flasks[j].gameObject.SetActive(false);
            }
        }
        return false;
    }
}

// Round 39: the user asked to check whether currency ("Tears"/Purge) could be separated per
// player. Turned out to be much more tractable than first assessed: currency is stored as
// Core.Logic.Penitent.Stats.Purge - a VariableAttribute on EntityStats, the *exact* same
// per-instance mechanism Life and Fervour already use. P2 (a full Penitent clone) already has
// its own separate Stats.Purge, sitting unused - this is the same "wrong owner" bug class
// already fixed throughout this file all session, just not yet applied to currency.
//
// The catch: unlike Life/Fervour (touched from a handful of C# classes), every currency EARN in
// the entire game runs through one of four PlayMaker actions (TearsAddition, and the newer
// Playmaker2 Purge/PurgeAdd/PurgeSet - level-scripted, used by enemy drops, pickups, chests,
// everywhere) - decompiling all four confirms each one unconditionally reads/writes
// Core.Logic.Penitent.Stats.Purge with **no notion of "which player" caused it at all**. PlayMaker
// FSMs don't carry per-Penitent context the way a C# call site normally would, so there is no
// cheap way to determine "P2 specifically earned this one" the way Hit.AttackingEntity lets
// damage be attributed elsewhere in this file. Rather than leave P2's pool permanently empty
// (unusable) or invest in a much larger "track last damager per enemy" plumbing project just for
// this, both players are credited the *same* amount independently whenever any of these actions
// fire - two genuinely separate running totals, not a shared/split pool, which is what "no
// compartan monedas" asked for; it just means both earn from every source rather than only
// whoever specifically caused it. Revisit if the user wants strict per-causer attribution instead
// - that's a real feature, not a quick follow-up.
//
// Spending (shops/Alms) is NOT touched here - shop UI/dialogue is still P1-only in this mod (no
// P2 shop-interaction exists at all yet), so there's nothing to redirect on that side yet; P2's
// pool just accumulates for now.
[HarmonyPatch(typeof(Tools.PlayMaker.Action.TearsAddition), "OnEnter")]
internal static class TearsAddition_CreditPlayer2_Patch
{
    private static void Postfix(Tools.PlayMaker.Action.TearsAddition __instance)
    {
        Penitent p2 = CoopLocal.Player2;
        if (p2 == null)
        {
            return;
        }
        float delta = __instance.Tears != null ? __instance.Tears.Value : 0f;
        p2.Stats.Purge.Current = Mathf.Max(0f, p2.Stats.Purge.Current + delta);
    }
}

[HarmonyPatch(typeof(Tools.Playmaker2.Action.PurgeAdd), "OnEnter")]
internal static class PurgeAdd_CreditPlayer2_Patch
{
    private static void Postfix(Tools.Playmaker2.Action.PurgeAdd __instance)
    {
        Penitent p2 = CoopLocal.Player2;
        if (p2 == null)
        {
            return;
        }
        float delta = __instance.value != null ? __instance.value.Value : 0f;
        p2.Stats.Purge.Current = Mathf.Max(0f, p2.Stats.Purge.Current + delta);
    }
}

// PurgeSet is an absolute assignment (not a delta) - almost certainly used for rare story-level
// resets rather than routine pickups, so mirroring it to P2 as an absolute set too (rather than
// treating it like an add) keeps both players' pools consistent for whatever story moment this
// actually is.
[HarmonyPatch(typeof(Tools.Playmaker2.Action.PurgeSet), "OnEnter")]
internal static class PurgeSet_CreditPlayer2_Patch
{
    private static void Postfix(Tools.Playmaker2.Action.PurgeSet __instance)
    {
        Penitent p2 = CoopLocal.Player2;
        if (p2 == null)
        {
            return;
        }
        float value = __instance.value != null ? __instance.value.Value : 0f;
        p2.Stats.Purge.Current = Mathf.Max(0f, value);
    }
}

// Round 42: after three rounds of trying to clone-and-redirect various real PlayerPurgePoints
// widgets (shop popup, then GameplayWidget.purgePoints, then a source-cycling tool to compare all
// of them) the user decided cloning isn't worth it here - PlayerPurgePoints drags along baked-in
// animation/background machinery that keeps looking wrong for reasons that were never fully
// pinned down. Simpler and more reliable: a single plain UI.Text we own outright, showing P2's
// Purge value as a number, styled with the *real* game font read once off the actual combat-HUD
// counter (Core.UI.GameplayUI's private "purgePoints" field, found via the Mono.Cecil scan two
// rounds ago) rather than guessed - no clone, no redirect patch, no inherited animation/background
// quirks to fight.
internal static class Player2PurgePoints
{
    private static readonly FieldInfo GameplayWidgetPurgePointsField =
        AccessTools.Field(typeof(Gameplay.UI.Widgets.GameplayWidget), "purgePoints");
    private static readonly FieldInfo PurgePointsTextField = AccessTools.Field(typeof(PlayerPurgePoints), "text");

    // Round 45: final position, re-confirmed by the user via live Player2HudPositionTuner testing
    // (moved from the original bottom-left placement to sit with the rest of P2's HUD block).
    internal static Vector2 AnchoredPosition = new Vector2(-157f, -4f);

    // Round 44: scale multipliers for the text/icon, adjustable live via Player2HudPositionTuner's
    // "." / "-" keys - these two never had a Scale field before since they were created at native
    // size (unlike Health/Fervour's cloned widgets, which always applied a fixed 0.65 shrink).
    // Round 45: final values, confirmed by the user via live testing.
    internal static float TextScale = 0.945f;
    internal static float IconScale = 0.855f;

    // Round 43: the coin/tears icon that sits behind the real HUD's currency text - independently
    // positionable from the text itself via Player2HudPositionTuner's new CurrencyIcon target.
    // Round 45: final position, re-confirmed by the user via live tuning.
    internal static Vector2 IconAnchoredPosition = new Vector2(16f, -13f);

    private static GameObject textRoot;
    private static GameObject iconRoot;
    private static Text label;

    internal static RectTransform CloneRect => textRoot != null ? textRoot.GetComponent<RectTransform>() : null;
    internal static RectTransform IconRect => iconRoot != null ? iconRoot.GetComponent<RectTransform>() : null;

    internal static void EnsureCreated(Penitent p2)
    {
        if (textRoot != null)
        {
            UnityEngine.Object.Destroy(textRoot);
            textRoot = null;
            label = null;
        }
        if (iconRoot != null)
        {
            UnityEngine.Object.Destroy(iconRoot);
            iconRoot = null;
        }

        Gameplay.UI.Widgets.GameplayWidget gameplayWidget = Core.UI != null ? Core.UI.GameplayUI : null;
        PlayerPurgePoints original = gameplayWidget != null
            ? (PlayerPurgePoints)GameplayWidgetPurgePointsField.GetValue(gameplayWidget)
            : null;
        Text originalText = original != null ? (Text)PurgePointsTextField.GetValue(original) : null;

        Canvas canvas = original != null ? original.GetComponentInParent<Canvas>() : null;
        while (canvas != null && canvas.transform.parent != null)
        {
            Canvas parentCanvas = canvas.transform.parent.GetComponentInParent<Canvas>();
            if (parentCanvas == null)
            {
                break;
            }
            canvas = parentCanvas;
        }
        Transform parent = canvas != null ? canvas.transform : (original != null ? original.transform.parent : null);
        if (parent == null)
        {
            DashParryDebugLog.Log("Player2PurgePoints.EnsureCreated: aborted - no Canvas parent found to attach the text to.");
            return;
        }

        // Round 43: the user asked to add the coin/tears icon sprite that sits behind the real
        // currency text. Looking for an Image sibling next to originalText's own GameObject
        // (logging every candidate sibling found, same technique proven earlier this session for
        // finding the HUD portrait) rather than guessing a hierarchy path blind.
        Sprite iconSprite = null;
        Color iconColor = Color.white;
        if (originalText != null && originalText.transform.parent != null)
        {
            Transform textParent = originalText.transform.parent;
            System.Text.StringBuilder siblingLog = new System.Text.StringBuilder();
            for (int i = 0; i < textParent.childCount; i++)
            {
                Transform child = textParent.GetChild(i);
                Image childImage = child.GetComponent<Image>();
                siblingLog.Append($"[{i}] '{child.name}' hasImage={childImage != null} ");
                if (childImage != null && childImage.sprite != null && child.gameObject != originalText.gameObject && iconSprite == null)
                {
                    iconSprite = childImage.sprite;
                    iconColor = childImage.color;
                }
            }
            DashParryDebugLog.Log($"Player2PurgePoints.EnsureCreated: '{textParent.name}' children (looking for the coin/tears icon): {siblingLog}");
        }

        // Icon created FIRST so the text (created after) ends up as the later sibling and renders
        // on top - the same "later sibling wins" rule this file's Z-order fixes already rely on.
        if (iconSprite != null)
        {
            GameObject iconObject = new GameObject("Player2PurgePointsIcon");
            iconObject.transform.SetParent(parent, worldPositionStays: false);
            iconRoot = iconObject;

            RectTransform iconRect = iconObject.AddComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0f, 0f);
            iconRect.anchorMax = new Vector2(0f, 0f);
            iconRect.pivot = new Vector2(0f, 0f);
            iconRect.sizeDelta = new Vector2(iconSprite.rect.width, iconSprite.rect.height);
            iconRect.anchoredPosition = IconAnchoredPosition;

            Image icon = iconObject.AddComponent<Image>();
            icon.sprite = iconSprite;
            icon.color = iconColor;
            icon.SetNativeSize();
            iconRect.localScale = Vector3.one * IconScale;
        }
        else
        {
            DashParryDebugLog.Log("Player2PurgePoints.EnsureCreated: no coin/tears icon sprite found next to the real currency text.");
        }

        GameObject textObject = new GameObject("Player2PurgePointsText");
        textObject.transform.SetParent(parent, worldPositionStays: false);
        textRoot = textObject;

        RectTransform rect = textObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 0f);
        rect.pivot = new Vector2(0f, 0f);
        rect.sizeDelta = new Vector2(220f, 60f);
        rect.anchoredPosition = AnchoredPosition;
        rect.localScale = Vector3.one * TextScale;

        label = textObject.AddComponent<Text>();
        if (originalText != null && originalText.font != null)
        {
            label.font = originalText.font;
            label.fontSize = originalText.fontSize;
            label.fontStyle = originalText.fontStyle;
            label.color = originalText.color;
            label.material = originalText.font.material;
            label.alignment = originalText.alignment;
        }
        else
        {
            label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.fontSize = 28;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleLeft;
        }
        label.text = "0";

        DashParryDebugLog.Log(
            $"Player2PurgePoints.EnsureCreated: custom text created, foundRealFont={(originalText != null && originalText.font != null)}, foundIcon={iconSprite != null}");
    }

    // Called every frame from Player2Input.Tick() - just a number display, no animation/inlining
    // risk to worry about, unlike everything else this session.
    internal static void Tick()
    {
        if (label == null)
        {
            return;
        }
        Penitent p2 = CoopLocal.Player2;
        if (p2 == null)
        {
            return;
        }
        label.text = Mathf.FloorToInt(p2.Stats.Purge.Current).ToString();
    }
}

// Round 40: temporary dev tool, NOT meant to ship long-term - every position value in
// Player2HealthBar/Player2FervourBar/Player2PurgePoints so far has been a guess, since this
// environment can't screenshot the live HUD to check placement. Lets the user interactively
// reposition each cloned widget in-game instead: arrow keys nudge whichever one is currently
// selected, "+" (Keypad or the plain top-row key, either works) cycles Life -> Fervour ->
// Currency -> Life, an on-screen label shows which one is selected, and every move is logged as
// "[HudTuner] <Target> position now: (x, y)" - once a widget looks right, copy that line's
// coordinates into the matching AnchoredPosition field above (Player2HealthBar.AnchoredPosition
// etc.) to make it permanent, and this whole class can be deleted afterwards. Caveat: arrow keys
// may double as P1's own alternate movement binding in Rewired (same keyboard-map overlap this
// file's history is full of) - fine for a one-off tuning session, just don't expect to also
// actively play P1 with arrows at the same time as tuning.
internal static class Player2HudPositionTuner
{
    private enum Target
    {
        Life,
        Fervour,
        Currency,
        CurrencyIcon,
    }

    private const float MoveStep = 1f;

    // Round 44: "." grows, "-" shrinks the currently selected widget by 5% per press - lets the
    // user compare sizes live instead of guessing a fixed scale blind.
    private const float ScaleStep = 0.05f;

    private static Target current = Target.Life;
    private static Text label;

    internal static void Tick()
    {
        if (Input.GetKeyDown(KeyCode.KeypadPlus) || Input.GetKeyDown(KeyCode.Equals))
        {
            current = current == Target.CurrencyIcon ? Target.Life : current + 1;
            ShowLabel();
        }

        EnsureLabelShown();

        if (Input.GetKeyDown(KeyCode.Period))
        {
            AdjustScale(1f + ScaleStep);
        }
        if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
        {
            AdjustScale(1f - ScaleStep);
        }

        RectTransform rect = GetTargetRect();
        if (rect == null)
        {
            return;
        }

        Vector2 move = Vector2.zero;
        if (Input.GetKeyDown(KeyCode.UpArrow))
        {
            move.y += MoveStep;
        }
        if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            move.y -= MoveStep;
        }
        if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            move.x -= MoveStep;
        }
        if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            move.x += MoveStep;
        }
        if (move == Vector2.zero)
        {
            return;
        }

        rect.anchoredPosition += move;
        SaveAndLog(rect.anchoredPosition);
    }

    private static void AdjustScale(float factor)
    {
        RectTransform rect = GetTargetRect();
        if (rect == null)
        {
            return;
        }
        rect.localScale *= factor;
        float newScale;
        switch (current)
        {
            case Target.Life:
                Player2HealthBar.Scale *= factor;
                newScale = Player2HealthBar.Scale;
                break;
            case Target.Fervour:
                Player2FervourBar.Scale *= factor;
                newScale = Player2FervourBar.Scale;
                break;
            case Target.CurrencyIcon:
                Player2PurgePoints.IconScale *= factor;
                newScale = Player2PurgePoints.IconScale;
                break;
            default:
                Player2PurgePoints.TextScale *= factor;
                newScale = Player2PurgePoints.TextScale;
                break;
        }
        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info($"[HudTuner] {current} scale now: {newScale:F3}", Main.CoopLocal);
        }
    }

    private static RectTransform GetTargetRect()
    {
        switch (current)
        {
            case Target.Life:
                return Player2HealthBar.CloneRect;
            case Target.Fervour:
                return Player2FervourBar.CloneRect;
            case Target.CurrencyIcon:
                return Player2PurgePoints.IconRect;
            default:
                return Player2PurgePoints.CloneRect;
        }
    }

    private static void SaveAndLog(Vector2 position)
    {
        switch (current)
        {
            case Target.Life:
                Player2HealthBar.AnchoredPosition = position;
                break;
            case Target.Fervour:
                Player2FervourBar.AnchoredPosition = position;
                break;
            case Target.CurrencyIcon:
                Player2PurgePoints.IconAnchoredPosition = position;
                break;
            default:
                Player2PurgePoints.AnchoredPosition = position;
                break;
        }
        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info($"[HudTuner] {current} position now: ({position.x:F0}, {position.y:F0})", Main.CoopLocal);
        }
    }

    private static void EnsureLabelShown()
    {
        if (label != null)
        {
            return;
        }

        GameObject canvasObject = new GameObject("HudTunerLabelCanvas");
        UnityEngine.Object.DontDestroyOnLoad(canvasObject);
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;

        GameObject textObject = new GameObject("HudTunerLabel");
        textObject.transform.SetParent(canvasObject.transform, worldPositionStays: false);
        RectTransform rect = textObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -16f);
        rect.sizeDelta = new Vector2(500f, 40f);

        label = textObject.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        label.fontSize = 22;
        label.alignment = TextAnchor.UpperCenter;
        label.color = Color.yellow;
        ShowLabel();
    }

    private static void ShowLabel()
    {
        if (label == null)
        {
            return;
        }
        label.text = $"HUD Tuner: {current} Mode  (arrows = move, + = switch, . / - = scale)";
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

        __result = Player2Input.ParryDown;
        return false;
    }
}

// Healing has its own separate, *un-gated* input path - Ability_UpdateInput_Patch above only
// disables the generic Ability.UpdateInput() dispatcher for P2, but Healing.LateUpdate() calls
// its own GetHealingInput() every frame for every instance regardless, which (like Parry's
// ParryInput before it was patched) reads straight off the shared Rewired Player 0
// (Rewired.GetButtonDown(23) in the decompiled vanilla method) *and* hardcodes
// Core.Logic.Penitent (always P1) for its "not already performing another action" gate - the
// same wrong-owner bug already fixed elsewhere in this file for other abilities, just not yet
// for this one. Net effect before this patch: P2's own Healing reacted to whatever the shared
// Player 0 read for that button, gated on *P1's* controller state instead of P2's own.
// Reimplemented the same way ParryInput was: P2's own gamepad heal button (see
// Player2Pad/Player2Input - the exact button is an unconfirmed guess, verify against
// RawButtonScanLog's log output), gated on P2's own PlatformCharacterController instead of the
// hardcoded one. P1's own instance keeps running the untouched original.
[HarmonyPatch(typeof(Healing), "GetHealingInput")]
internal static class Healing_GetHealingInput_Patch
{
    private static bool Prefix(Healing __instance, ref bool __result)
    {
        Penitent owner = __instance.GetComponentInParent<Penitent>();
        if (owner == null || owner != CoopLocal.Player2)
        {
            return true;
        }

        // The vanilla method's own second gate - !GetActionState((eControllerActions)16) - is
        // deliberately NOT enforced here. It's untested against P2's own controller state (only
        // ever checked, in vanilla, against the hardcoded Core.Logic.Penitent/P1), and the user
        // reported Heal not firing for P2 at all - this gate being permanently true for P2 for
        // some unrelated reason is the prime suspect, so it's dropped rather than risk it silently
        // blocking every press again. Still logged (once per press) so this can be confirmed.
        bool healPressed = Player2Input.HealDown;
        if (healPressed && Main.CoopLocal != null)
        {
            bool vanillaGateWasBlocking = owner.PlatformCharacterController.GetActionState((eControllerActions)16);
            Blasphemous.ModdingAPI.ModLog.Info(
                $"[Healing] P2 heal button pressed - vanilla's own action-16 gate is currently " +
                $"{(vanillaGateWasBlocking ? "TRUE (would have blocked this press)" : "false (harmless)")}.",
                Main.CoopLocal);
        }
        __result = healPressed;
        return false;
    }
}

// Round 36: the user reported P2 getting stuck with a lingering healing-aura sprite and unable
// to Parry after drinking a flask - a real bug, and the exact same "_penitent falls back to P1"
// family already fixed throughout this file for Dash/AirDash/RunAfterDash, just not yet for this
// one. HealingBehaviour (an Animator StateMachineBehaviour, one instance per Animator, so P2's
// clone genuinely has its own) resolves its `_penitent` field lazily on first OnStateEnter -
// `if (_penitent == null) _penitent = Core.Logic.Penitent;` - hardcoded to P1 regardless of whose
// Animator is actually entering the healing state. OnStateEnter then caches
// `HealingAbility = _penitent.GetComponentInChildren<Healing>()` from that (wrong, P1's own)
// Penitent, so when the healing animation naturally finishes and OnStateExit fires
// `HealingAbility.StopHeal()`, it's stopping *P1's* Healing (usually a harmless no-op, since P1
// probably isn't healing) instead of P2's own - P2's IsHealing/aura/Invulnerable never get reset
// by StopHeal, which is exactly the "stuck healing state, aura won't go away, can't Parry"
// (Ability.StopCast() - which clears whatever cast-lock blocks Parry - lives inside StopHeal(),
// so skipping it for P2 skips that cleanup too) symptom reported. Fixed the same way as the
// existing Dash/AirDash patches: pre-set `_penitent` correctly (via the Animator parameter
// OnStateEnter already receives) before the original method's own null-check ever runs, so it
// sees an already-correct value and never overwrites it with P1.
[HarmonyPatch(typeof(HealingBehaviour), "OnStateEnter")]
internal static class HealingBehaviour_OnStateEnter_Patch
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

// Round 37: the user reported dash+attack (the "lunge"/estoque combo) leaving P2 unable to Dash
// or Heal again afterward. LungeAttackBehaviour has the exact same bug shape as HealingBehaviour
// above, just caching a different field: OnStateEnter does
// `if (_lungeAttack == null) _lungeAttack = Core.Logic.Penitent.GetComponentInChildren<LungeAttack>();`
// - hardcoded to P1 - then OnStateExit calls `_lungeAttack.StopCast()` on whatever that resolved
// to. For P2's own Animator entering this state, that's P1's LungeAttack, not P2's - so P2's own
// LungeAttack ability's cast-lock (Ability.StopCast(), which also lives inside StopHeal() for
// Healing - same family) never gets cleared, leaving P2 stuck exactly as reported. Same fix as
// HealingBehaviour, just targeting the ability-typed field instead of a Penitent-typed one.
[HarmonyPatch(typeof(LungeAttackBehaviour), "OnStateEnter")]
internal static class LungeAttackBehaviour_OnStateEnter_Patch
{
    private static void Prefix(Animator animator, ref LungeAttack ____lungeAttack)
    {
        Penitent owner = animator.GetComponentInParent<Penitent>();
        if (owner == null)
        {
            return;
        }
        LungeAttack ownAbility = owner.GetComponentInChildren<LungeAttack>();
        if (ownAbility != null)
        {
            ____lungeAttack = ownAbility;
        }
    }
}

// Round 37: an exhaustive scan of every Player AnimationBehaviour (StateMachineBehaviour) class
// in the game found the exact same "_penitent starts null, defaults to Core.Logic.Penitent
// (always P1) on first use" bug in roughly fifty separate classes - the same pattern already
// individually fixed, one reported symptom at a time, for Dash/AirDash/RunAfterDash/Attack/
// Crouch*/Ladder*/CliffLede*/Hurt*/Falling/Idle/Move/RunStart/Healing/LungeAttack (their own
// patches are scattered throughout this file). Rather than keep adding one narrowly-scoped patch
// per newly-reported symptom, this single patch covers every *remaining* class at once via
// Harmony's TargetMethods() - the exact same fix (pre-set `_penitent` from the Animator parameter
// every one of these methods already receives, before the original's own null-check runs and
// overwrites it with P1), just applied wholesale instead of piecemeal. This is what actually
// fixes the reported "P2 does a charged attack whenever P1 does" - StartChargingAttackBehaviour
// is in this list, and was the real cause (its OnStateEnter calls `_penitent.ChargedAttack.Cast()`
// - resolving to P1's ChargedAttack instead of P2's own when it's P2's Animator entering the
// state - a wrong-owner Cast() call, not a shared-input-read bug like Healing's was). The rest
// (air/ground attack variants, jump/fall/landing, death, a few Prayer-cutscene states, range
// attack) weren't specifically reported broken, but share the identical bug shape, so they're
// fixed proactively here rather than waiting for each to surface as its own bug report.
[HarmonyPatch]
internal static class ManyPlayerAnimationBehaviours_PenitentOwnerFix_Patch
{
    private static readonly Type[] TargetTypes =
    {
        typeof(AirAttackBehaviour), typeof(AirUpwardAttackBehaviour), typeof(ChargedAttackBehaviour),
        typeof(ChargedAttackEffectBehaviour), typeof(ChargingAttackBehaviour), typeof(FinishingComboStarterBehaviour),
        typeof(GroundUpwardAttackBehaviour), typeof(StartChargingAttackBehaviour),
        typeof(PlayerDeathAnimationBehaviour), typeof(PlayerDeathFallBehaviour), typeof(PlayerDeathSpikeBehaviour),
        typeof(FallingOverBehaviour), typeof(GroundingOverBehaviour),
        typeof(JumpBehaviour), typeof(JumpForwardBehaviour), typeof(JumpOffBehaviour),
        typeof(LandingBehaviour), typeof(LandingRunningBehaviour),
        typeof(AuraTransformBehaviour), typeof(HighWillsRespawnBehaviour), typeof(PR202TeleportBehaviour),
        typeof(GroundRangeAttackBehaviour), typeof(MidAirRangeAttackBehaviour),
        typeof(AirAttackSubStateBehaviour), typeof(ChargeAttackSubStateBehaviour), typeof(CliffLedeSubStateBehaviour),
        typeof(CrouchSubStateBehaviour), typeof(DashSubStateBehaviour),
    };

    // StateMachineBehaviour declares two OnStateEnter overloads (with and without a trailing
    // AnimatorControllerPlayable parameter) - AccessTools.Method(type, "OnStateEnter") alone is
    // ambiguous between them and throws at patch time (confirmed live: it took down this entire
    // patch, silently skipping all ~24 fixes below it). Parameter types must be given explicitly
    // to pick the plain 3-parameter overload every one of these classes actually overrides.
    private static readonly Type[] OnStateEnterParams = { typeof(Animator), typeof(AnimatorStateInfo), typeof(int) };

    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (Type type in TargetTypes)
        {
            MethodInfo method = AccessTools.Method(type, "OnStateEnter", OnStateEnterParams);
            if (method != null)
            {
                yield return method;
            }
        }
    }

    private static void Prefix(Animator animator, ref Penitent ____penitent)
    {
        Penitent owner = animator.GetComponentInParent<Penitent>();
        if (owner != null)
        {
            ____penitent = owner;
        }
    }
}

// Round 45: found a real, previously-unknown side effect of the batch patch above, from a
// NullReferenceException that fired 10 times in one live P2 test session (upward attacks
// specifically). GroundUpwardAttackBehaviour.OnStateEnter's real body is
// `if (_penitent == null) { _penitent = Core.Logic.Penitent; ...also compute _defaultAttackAreaOffset/
// _defaultAttackAreaSize/_penitentSword/_swordAnimatorInyector... }` - the batch Prefix above
// pre-sets ____penitent to the correct owner *before* vanilla's own null-check runs, which fixes
// the owner but has a side effect for this specific class: since ____penitent is never null by
// the time vanilla checks, that whole init block - including the three fields OnStateUpdate/
// OnStateExit actually depend on - never runs for P2 at all, leaving them permanently null and
// crashing the moment OnStateUpdate reaches `_swordAnimatorInyector.PlayAttackDesiredTime(...)`.
// This is a real gap in the batch-patch technique itself: it silently breaks any class that
// bundles *other* cached-once state inside the same guard as `_penitent`, not just this one - a
// full audit of the other ~23 classes for the same shape is still open (only this one has actually
// been proven broken via a live crash log). Fixed with a Postfix that recomputes the three skipped
// fields directly from the correct P2 owner, mirroring vanilla's own logic exactly.
[HarmonyPatch(typeof(GroundUpwardAttackBehaviour), "OnStateEnter")]
internal static class GroundUpwardAttackBehaviour_FixSkippedInit_P2_Patch
{
    private static readonly FieldInfo PenitentSwordField = AccessTools.Field(typeof(GroundUpwardAttackBehaviour), "_penitentSword");
    private static readonly FieldInfo SwordAnimatorInyectorField = AccessTools.Field(typeof(GroundUpwardAttackBehaviour), "_swordAnimatorInyector");
    private static readonly FieldInfo DefaultAttackAreaOffsetField = AccessTools.Field(typeof(GroundUpwardAttackBehaviour), "_defaultAttackAreaOffset");
    private static readonly FieldInfo DefaultAttackAreaSizeField = AccessTools.Field(typeof(GroundUpwardAttackBehaviour), "_defaultAttackAreaSize");

    private static void Postfix(GroundUpwardAttackBehaviour __instance, Penitent ____penitent)
    {
        if (____penitent == null || ____penitent != CoopLocal.Player2)
        {
            return;
        }
        if (SwordAnimatorInyectorField.GetValue(__instance) != null)
        {
            return;
        }
        Vector2 offset = new Vector2(____penitent.AttackArea.WeaponCollider.offset.x, ____penitent.AttackArea.WeaponCollider.offset.y);
        Vector2 size = new Vector2(____penitent.AttackArea.WeaponCollider.bounds.size.x, ____penitent.AttackArea.WeaponCollider.bounds.size.y);
        DefaultAttackAreaOffsetField.SetValue(__instance, offset);
        DefaultAttackAreaSizeField.SetValue(__instance, size);
        PenitentSword sword = (PenitentSword)____penitent.PenitentAttack.CurrentPenitentWeapon;
        PenitentSwordField.SetValue(__instance, sword);
        SwordAnimatorInyectorField.SetValue(__instance, sword.SlashAnimator);
    }
}

// PrayerUse (the "activate equipped prayer" ability, distinct from Healing above) has no
// dedicated input method of its own the way Healing does - it relies entirely on the base
// Ability's generic UpdateInput() dispatcher, which Ability_UpdateInput_Patch (further down this
// file) disables outright for P2 (see that patch's own comment - "abilities we haven't
// explicitly wired for P2 yet simply won't be castable"). So P2's own PrayerUse currently never
// casts at all. Wired here the same way Dash/Parry/Healing were: a dedicated per-instance check
// reading P2's own input instead of the disabled generic path - Postfixed onto OnUpdate() (runs
// every frame per-instance already) rather than UpdateInput() itself, since that stays
// intentionally disabled for P2 by the patch further down. P1's own instance is untouched - its
// PrayerUse still casts through the normal generic dispatcher exactly as before.
[HarmonyPatch(typeof(PrayerUse), "OnUpdate")]
internal static class PrayerUse_P2Input_Patch
{
    private static readonly FieldInfo CastInformationField = AccessTools.Field(typeof(Ability), "castInformation");

    // Round 39 follow-up: __instance.CanUsePrayer compiles fine against the NuGet reference
    // assembly (which marks it public) but the REAL shipped Assembly-CSharp.dll has the getter as
    // non-public - calling it directly threw a runtime MethodAccessException ("get_CanUsePrayer is
    // inaccessible"), confirmed via LogOutput.log. AccessTools.Property + PropertyInfo.GetValue
    // bypasses the compile-time accessibility check, same trick already relied on throughout this
    // file for private fields.
    private static readonly PropertyInfo CanUsePrayerProperty = AccessTools.Property(typeof(PrayerUse), "CanUsePrayer");

    // Round 38: static analysis of Ability.Cast()/PrayerUse.OnCastStart()/StartUsingPrayer() all
    // came back correctly per-instance (EntityOwner/_penitent-based throughout, no hardcoded
    // Core.Logic.Penitent found anywhere in that chain) - yet the user reports the prayer effect
    // visibly originating from P1 and consuming *neither* player's Fervour when triggered from
    // P2. Since nothing in the code this patch can see explains that, logging Cast()'s own
    // castInformation field (a string Ability.Cast() itself sets to exactly why it
    // succeeded/failed - "SUCCESSFULLY EXECUTED", "ALREADY CASTING", "ABILITY NOT READY",
    // "CONDITION NOT MET", "ENTITY DEAD", "ABILITIES DISABLED", "INVALID OWNER" - see its
    // decompiled source) plus P2's own Fervour before/after, to find out directly which of those
    // it actually is rather than guessing further blind. The specific prayer effect classes
    // (multishotPrayer/lightBeamPrayer/shieldPrayer/cherubPrayer/etc, all boss-attack classes
    // reused for player prayers) haven't been individually audited for their own owner/position
    // logic yet - if castInformation comes back "SUCCESSFULLY EXECUTED" with Fervour genuinely
    // dropping on P2, the bug is in one of *those* classes instead, not in PrayerUse itself.
    private static void Postfix(PrayerUse __instance)
    {
        Penitent owner = __instance.GetComponentInParent<Penitent>();
        if (owner == null || owner != CoopLocal.Player2)
        {
            return;
        }
        if (Player2Input.PrayerActivateDown)
        {
            // Round 39: PrayerUse.get_CanUsePrayer (non-virtual, decompiled from the real shipped
            // Assembly-CSharp.dll) is the property that actually checks fervourNeeded against the
            // per-instance _penitent.Stats.Fervour.Current before P1 is allowed to cast - it was
            // never being consulted here, so P2's Cast() fired unconditionally regardless of
            // P2's own Fervour, and with no floor P2's Fervour could go arbitrarily negative.
            // Gating on it here mirrors P1's real logic exactly ("misma logica que rezo P1").
            bool canUsePrayer = (bool)CanUsePrayerProperty.GetValue(__instance, null);
            if (!canUsePrayer)
            {
                return;
            }
            float fervourBefore = owner.Stats.Fervour.Current;
            __instance.Cast();
            string info = (string)CastInformationField.GetValue(__instance);
            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info(
                    $"[PrayerUse] P2 Cast() -> castInformation='{info}', P2 Fervour {fervourBefore:F1} -> {owner.Stats.Fervour.Current:F1}, " +
                    $"equippedPrayer={(__instance.GetEquippedPrayer() != null ? __instance.GetEquippedPrayer().name : "null")}",
                    Main.CoopLocal);
            }
        }
        if (Player2Input.PrayerActivateUp)
        {
            __instance.StopCast();
        }
    }
}

// Round 43: found the actual cause of "el origen del rezo es en P1" - PrayerUse itself
// (Cast()/OnCastStart()/StartUsingPrayer()) is genuinely per-instance and correctly casts from
// whichever Penitent owns it (confirmed since Fervour drains correctly from P2's own pool). But
// StartUsingPrayer() ends by calling `prayer.Use()` on the equipped Prayer *item* - a single
// object shared game-wide (there's only one "equipped prayer" inventory entry, not one per
// Penitent) - which does `SendMessage("OnUseInventoryObject")`. The specific prayer-power effect
// classes that receive that message (decompiled via ICSharpCode.Decompiler from the real
// Assembly-CSharp.dll) each independently hardcode `_owner = Core.Logic.Penitent;` as their own
// first line - the exact same "wrong owner" bug class found ~50 times already this session in
// AnimationBehaviours, just living in a completely different part of the codebase
// (Framework.Inventory's ObjectEffect system) that a per-Penitent-component scan would never
// reach. Since the shared Prayer item has no way to know who actually triggered it, this patch
// tracks the real caster itself: a Prefix on PrayerUse's own (already correctly per-instance)
// StartUsingPrayer() records `_penitent` into a static field *before* prayer.Use() fires the
// SendMessage chain - by the time OnApplyEffect() runs (synchronously, same call stack), the
// tracker reliably holds the real caster.
internal static class PrayerCasterTracker
{
    internal static Penitent LastCaster;
}

[HarmonyPatch(typeof(PrayerUse), "StartUsingPrayer")]
internal static class PrayerUse_StartUsingPrayer_TrackCaster_Patch
{
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(PrayerUse), "_penitent");

    private static void Prefix(PrayerUse __instance)
    {
        PrayerCasterTracker.LastCaster = (Penitent)PenitentField.GetValue(__instance);
    }
}

// PrayerAlliedCherubEffect/PrayerShieldEffect both derive from ObjectEffect_Stat and end their
// OnApplyEffect/OnRemoveEffect with `base.OnApplyEffect()`/`base.OnRemoveEffect()` - a generic
// stat-bonus applier that *also* hardcodes Core.Logic.Penitent internally. Reflection can't safely
// invoke "just the base implementation" here (MethodInfo.Invoke on a virtual method always
// re-dispatches to the most-derived override via the CLR's normal vtable lookup, regardless of
// which declaring type's MethodInfo was used to look it up - invoking it from inside this very
// Prefix would recurse into itself). Rather than risk a broken reimplementation of
// ObjectEffect_Stat's full logic (PenitencePE02 special-casing, RawBonus tracking, etc) blind,
// this patch fixes only the part the user actually reported - the visible cherub/shield spawn
// itself - and deliberately skips (via `return false`) the inherited stat-bonus call, a known,
// narrow, documented gap rather than an attempted full fix.
[HarmonyPatch(typeof(Framework.Inventory.PrayerAlliedCherubEffect), "OnApplyEffect")]
internal static class PrayerAlliedCherubEffect_OnApplyEffect_P2_Patch
{
    private static bool Prefix(Framework.Inventory.PrayerAlliedCherubEffect __instance, ref bool __result)
    {
        Penitent caster = PrayerCasterTracker.LastCaster;
        if (caster == null || caster != CoopLocal.Player2)
        {
            return true;
        }
        PrayerUse prayerUse = caster.GetComponentInChildren<PrayerUse>();
        AlliedCherubPrayer cherubPrayer = prayerUse != null ? prayerUse.cherubPrayer : null;
        if (cherubPrayer != null)
        {
            cherubPrayer.InstantiateCherubs();
        }
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(Framework.Inventory.PrayerAlliedCherubEffect), "OnRemoveEffect")]
internal static class PrayerAlliedCherubEffect_OnRemoveEffect_P2_Patch
{
    private static bool Prefix(Framework.Inventory.PrayerAlliedCherubEffect __instance)
    {
        Penitent caster = PrayerCasterTracker.LastCaster;
        if (caster == null || caster != CoopLocal.Player2)
        {
            return true;
        }
        PrayerUse prayerUse = caster.GetComponentInChildren<PrayerUse>();
        AlliedCherubPrayer cherubPrayer = prayerUse != null ? prayerUse.cherubPrayer : null;
        if (cherubPrayer != null)
        {
            cherubPrayer.DisposeCherubs();
        }
        return false;
    }
}

[HarmonyPatch(typeof(Framework.Inventory.PrayerShieldEffect), "OnApplyEffect")]
internal static class PrayerShieldEffect_OnApplyEffect_P2_Patch
{
    private static bool Prefix(Framework.Inventory.PrayerShieldEffect __instance, ref bool __result)
    {
        Penitent caster = PrayerCasterTracker.LastCaster;
        if (caster == null || caster != CoopLocal.Player2)
        {
            return true;
        }
        PrayerUse prayerUse = caster.GetComponentInChildren<PrayerUse>();
        ShieldSystemPrayer shieldPrayer = prayerUse != null ? prayerUse.shieldPrayer : null;
        if (shieldPrayer != null)
        {
            shieldPrayer.InstantiateShield();
        }
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(Framework.Inventory.PrayerShieldEffect), "OnRemoveEffect")]
internal static class PrayerShieldEffect_OnRemoveEffect_P2_Patch
{
    private static bool Prefix(Framework.Inventory.PrayerShieldEffect __instance)
    {
        Penitent caster = PrayerCasterTracker.LastCaster;
        if (caster == null || caster != CoopLocal.Player2)
        {
            return true;
        }
        PrayerUse prayerUse = caster.GetComponentInChildren<PrayerUse>();
        ShieldSystemPrayer shieldPrayer = prayerUse != null ? prayerUse.shieldPrayer : null;
        if (shieldPrayer != null)
        {
            shieldPrayer.DisposeShield();
        }
        return false;
    }
}

// PenitentLightBeamEffect derives straight from ObjectEffect (not ObjectEffect_Stat) - its
// OnApplyEffect is fully self-contained with no base-call recursion risk, so this is a complete
// reimplementation rather than a partial one.
[HarmonyPatch(typeof(Tools.Items.PenitentLightBeamEffect), "OnApplyEffect")]
internal static class PenitentLightBeamEffect_OnApplyEffect_P2_Patch
{
    private static readonly FieldInfo OwnerField = AccessTools.Field(typeof(Tools.Items.PenitentLightBeamEffect), "_owner");
    private static readonly FieldInfo AreaSummonAttackField = AccessTools.Field(typeof(Tools.Items.PenitentLightBeamEffect), "_areaSummonAttack");
    private static readonly FieldInfo DamageAmountField = AccessTools.Field(typeof(Tools.Items.PenitentLightBeamEffect), "DamageAmount");
    private static readonly MethodInfo PushPlayerColorMethod = AccessTools.Method(typeof(Tools.Items.PenitentLightBeamEffect), "PushPlayerColor");
    private static readonly MethodInfo PopPlayerColorMethod = AccessTools.Method(typeof(Tools.Items.PenitentLightBeamEffect), "PopPlayerColor");

    private static bool Prefix(Tools.Items.PenitentLightBeamEffect __instance, ref bool __result)
    {
        Penitent caster = PrayerCasterTracker.LastCaster;
        if (caster == null || caster != CoopLocal.Player2)
        {
            return true;
        }
        OwnerField.SetValue(__instance, caster);
        PrayerUse prayerUse = caster.GetComponentInChildren<PrayerUse>();
        Gameplay.GameControllers.Bosses.Quirce.Attack.BossAreaSummonAttack areaSummonAttack =
            prayerUse != null ? prayerUse.lightBeamPrayer : null;
        if (areaSummonAttack == null)
        {
            __result = false;
            return false;
        }
        AreaSummonAttackField.SetValue(__instance, areaSummonAttack);
        if (Core.Logic.CameraManager != null && Core.Logic.CameraManager.ProCamera2DShake != null)
        {
            Core.Logic.CameraManager.ProCamera2DShake.ShakeUsingPreset("SimpleHit");
        }
        Vector3 position = areaSummonAttack.transform.position;
        float strengthFinal = caster.Stats.PrayerStrengthMultiplier.Final;
        GameObject spawned = areaSummonAttack.SummonAreaOnPoint(position, 0f, strengthFinal);
        int damageAmount = (int)DamageAmountField.GetValue(__instance);
        Gameplay.GameControllers.Bosses.Quirce.Attack.BossSpawnedAreaAttack spawnedAttack =
            spawned.GetComponent<Gameplay.GameControllers.Bosses.Quirce.Attack.BossSpawnedAreaAttack>();
        if (spawnedAttack != null)
        {
            spawnedAttack.SetDamage(damageAmount);
        }
        __instance.StartCoroutine(VerticalBeamCoroutine(__instance));
        __result = true;
        return false;
    }

    private static System.Collections.IEnumerator VerticalBeamCoroutine(Tools.Items.PenitentLightBeamEffect instance)
    {
        yield return new WaitForSeconds(0.4f);
        PushPlayerColorMethod.Invoke(instance, null);
        yield return new WaitForSeconds(0.8f);
        PopPlayerColorMethod.Invoke(instance, null);
    }
}

// Interactable (level props: doors, levers, item pickups, NPC dialogue triggers) is a per-OBJECT
// input check, not per-Penitent like the Ability classes above - InteractionTriggered reads
// button 8 off the shared Rewired Player 0 directly, plus hardcodes Core.Logic.Penitent (always
// P1) for its own "not currently jumping/grabbing a cliff ledge" gates, then returns
// !OverlappedInteractor as its final result. OverlappedInteractor is NOT "a player is in range"
// (that's the separate PlayerInRange property, set correctly for both P1 and P2 via a generic
// CompareTag("Penitent") check in OnEntityEnter/Exit - no owner bug there) - it's only ever
// written by the narrow Execution/GuiltDropCollectibleItem subsystems (finishers/guilt drops),
// meaning it's false for every ordinary door/lever/chest, and vanilla's own logic *requires* it
// to be false to succeed. (Round 36 fix: an earlier version of this patch had that inverted -
// checking `!OverlappedInteractor` as if it were a required-true gate - which meant this Postfix
// bailed out on almost every ordinary interactable and Interact silently never worked for P2.)
// PlayerInRange itself doesn't need rechecking here since Door/Lever/etc.'s own OnUpdate() (the
// caller) already ANDs InteractionTriggered together with its own PlayerInRange check.
[HarmonyPatch(typeof(Tools.Level.Interactable), "get_InteractionTriggered")]
internal static class Interactable_InteractionTriggered_Patch
{
    private static readonly FieldInfo InteractableWhileJumpingField =
        AccessTools.Field(typeof(Tools.Level.Interactable), "interactableWhileJumping");

    private static void Postfix(Tools.Level.Interactable __instance, ref bool __result)
    {
        if (__result || CoopLocal.Player2 == null || !Player2Input.InteractDown)
        {
            return;
        }
        if (__instance.OverlappedInteractor || Core.Input.InputBlocked)
        {
            return;
        }
        bool interactableWhileJumping = (bool)InteractableWhileJumpingField.GetValue(__instance);
        if (CoopLocal.Player2.IsJumping && !interactableWhileJumping)
        {
            return;
        }
        if (CoopLocal.Player2.IsGrabbingCliffLede)
        {
            return;
        }
        __result = true;
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

// Forces P2 to always wear the "True Apostasy" ("Verdadera Apostasia") skin, independent of
// whatever skin P1 has selected from the Extras menu. ColorPaletteSwapper.SetMaterial() (on
// the same GameObject as the character's own SpriteRenderer, so it's genuinely per-instance)
// reads Core.ColorPaletteManager's single *global* current-skin id and writes the matching
// texture into the "_PaletteTex" slot on this instance's own material - since that id isn't
// per-character, P1 and P2 would otherwise always end up wearing the exact same skin. This
// Postfix runs after the original (harmless - it only overwrites the texture a second time)
// and, only for P2's own instance, re-applies the True Apostasy palette instead. Runs on every
// call rather than just the initial Start() one, so P2 stays forced even if SetMaterial() is
// ever invoked again later (menu skin change, respawn, etc).
[HarmonyPatch(typeof(ColorPaletteSwapper), "SetMaterial")]
internal static class ColorPaletteSwapper_ForcePlayer2TrueApostasy_Patch
{
    // Round 36: "PAL_Penitent_ALT2" (a community modding doc's id) was confirmed WRONG - the
    // [ColorPalette] log dump of this game's real ids came back as: PENITENT_DEFAULT,
    // PENITENT_ENDING_A, PENITENT_ENDING_B, PENITENT_OSSUARY, PENITENT_BACKER, PENITENT_DELUXE,
    // PENITENT_ALMS, PENITENT_PE01/02/03, PENITENT_BOSSRUSH(_S), PENITENT_DEMAKE,
    // PENITENT_ENDING_C, PENITENT_SIERPES, PENITENT_ISIDORA, PENITENT_GAMEBOY, PENITENT_KONAMI -
    // no "ALT2" anywhere, so the ids are clearly named per-ending, not per "ALT" slot like the
    // community doc assumed. Per external research (blasphemous.wiki.gg/wiki/Skins), True
    // Apostasy unlocks from completing Ending B ("The Path of the Unworthy") - or from Ending A
    // specifically on a first playthrough, a secondary special case - so PENITENT_ENDING_B is the
    // best-effort match for the *general* unlock path. Still not visually confirmed - if this
    // renders the wrong (but validly-existing, so no fallback/log fires) palette, it's most likely
    // actually PENITENT_ENDING_A instead; there's no way to tell which without a screenshot.
    private const string TrueApostasyPaletteId = "PENITENT_ENDING_B";

    private static bool resolveAttempted;
    private static string resolvedPaletteId;

    private static string ResolveTrueApostasyPaletteId()
    {
        if (resolveAttempted)
        {
            return resolvedPaletteId;
        }
        resolveAttempted = true;

        List<string> allIds = Core.ColorPaletteManager.GetAllColorPalettesId();
        if (allIds == null)
        {
            return null;
        }

        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info(
                $"[ColorPalette] all known palette ids: {string.Join(", ", allIds.ToArray())}", Main.CoopLocal);
        }

        if (allIds.Contains(TrueApostasyPaletteId))
        {
            resolvedPaletteId = TrueApostasyPaletteId;
            return resolvedPaletteId;
        }

        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info(
                $"[ColorPalette] could not find '{TrueApostasyPaletteId}' in the list above - " +
                "P2's skin will NOT be forced. Pick the right id from that list.",
                Main.CoopLocal);
        }
        return null;
    }

    private static void Postfix(ColorPaletteSwapper __instance)
    {
        Penitent owner = __instance.GetComponentInParent<Penitent>();
        if (owner == null || owner != CoopLocal.Player2)
        {
            return;
        }

        string paletteId = ResolveTrueApostasyPaletteId();
        if (paletteId == null)
        {
            return;
        }

        Sprite paletteSprite = Core.ColorPaletteManager.GetColorPaletteById(paletteId);
        if (paletteSprite == null)
        {
            return;
        }

        SpriteRenderer spriteRenderer = __instance.GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            return;
        }

        Texture2D paletteTexture = paletteSprite.texture;
        spriteRenderer.material.SetTexture("_PaletteTex", paletteTexture);
        if (__instance.extraMaterial != null)
        {
            __instance.extraMaterial.SetTexture("_PaletteTex", paletteTexture);
        }
    }
}

// Round 41: user reported P2 spawning with less max life and no damage/flask upgrades than P1 -
// this is architectural, not a per-instance-owner bug like almost everything else this session:
// CoopLocal.OnPlayerSpawn creates P2 via Object.Instantiate(Resources.Load<Penitent>("Core/Penitent"),
// ...), a completely fresh copy of the base prefab with none of P1's collected Rosary Beads/Mea
// Culpa/flask upgrades/etc ever applied.
//
// Decompiled Gameplay.GameControllers.Entities.EntityStats (real C# via ICSharpCode.Decompiler, not
// raw IL) to find the right generic API: every single stat - Life, Strength, DamageMultiplier,
// FlaskHealth, BeadSlots, CriticalChance, all of it - is a Framework.FrameworkCore.Attributes.Logic.
// Attribute with a `PermanetBonus` float (publicly gettable, privately settable - raised over time by
// Upgrade()/SetPermanentBonus(), i.e. exactly "story-earned progression", as opposed to temporary
// RawBonus/FinalBonus buffs from equipped relics/active effects which are deliberately NOT copied
// here). EntityStats.GetByType(StatsTypes) + SetPermanentBonus(float) - the *same* generic API the
// game's own GetCurrentPersistentState/SetCurrentPersistentState use for save/load - lets one loop
// over every EntityStats.StatsTypes enum value cover the whole stat surface at once, no per-stat
// special-casing needed. All of GetByType/PermanetBonus/SetPermanentBonus/SetToCurrentMax were
// confirmed public directly in the decompiled *real* Assembly-CSharp.dll (not just the NuGet
// reference stub) - unlike PrayerUse.CanUsePrayer earlier this round, there's no reflection
// workaround needed to call them directly.
//
// The user's explicit ask - clone once, "y ya luego esta copia de todo esto no se vuelva a hacer sin
// importar que" (never re-copy after that, no matter what) - can't be a simple did-this-run-before
// flag: CoopLocal.OnPlayerSpawn destroys and recreates P2 from the bare prefab on *every* respawn
// (level load, teleport, death), so P2's own EntityStats object (with all its PermanetBonus values)
// is thrown away and rebuilt from scratch far more often than "once per game". A flag alone would
// mean the correct stats get applied exactly once ever and then every later respawn reverts P2 to
// the weak prefab defaults again - worse than doing nothing. Instead this persists the actual
// baseline values (not just a yes/no marker) to a small per-save-slot text file under
// Application.persistentDataPath: the FIRST spawn for a given save slot (Framework.Managers.
// PersistentManager.GetAutomaticSlot(), the same public static int the game's own save system keys
// its files by) clones P1's current stats onto P2 and writes that snapshot to disk; every later
// spawn - same session or a future one, respawn or fresh launch - restores P2's *own* saved
// baseline onto the fresh instance instead of touching P1 again, so P2 keeps its starting power
// forever after that first sync without perpetually re-mirroring P1's own ongoing progress.
internal static class Player2StatsSync
{
    private static string MarkerDirectory =>
        System.IO.Path.Combine(Application.persistentDataPath, "CoopLocalMod");

    private static string SnapshotPath(int slot) =>
        System.IO.Path.Combine(MarkerDirectory, $"p2_stats_slot{slot}.txt");

    // Round 43/45: Purge (currency), Life, Fervour and Flask all need their *Current* value
    // persisted separately from the PermanetBonus loop - PermanetBonus only covers max-capacity
    // upgrades, not the live value itself, and (round 45) forcing these to max on every single
    // respawn turned out to be actively wrong: SpawnManager.OnPlayerSpawn fires on *ordinary room
    // transitions* too, not just death/checkpoint respawns, so P2 was silently getting fully
    // healed and refilled on every room change ("todo de P2 se resetea al cambiar de sala") while
    // a *real* Prie Dieu rest - which should heal P2 - did nothing at all (PrieDieu's own heal
    // logic never routes through OnPlayerSpawn). Keys deliberately don't match any real
    // EntityStats.StatsTypes enum name, so ApplySnapshot's normal per-stat loop skips over them.
    private const string PurgeCurrentKey = "__PurgeCurrent__";
    private const string LifeCurrentKey = "__LifeCurrent__";
    private const string FervourCurrentKey = "__FervourCurrent__";
    private const string FlaskCurrentKey = "__FlaskCurrent__";

    // Round 42: the first-ever sync (previous round) ran synchronously inside CoopLocal's
    // OnPlayerSpawn handler and captured every one of P1's stats as PermanetBonus=0 - confirmed by
    // reading the actual saved snapshot file, which was all zeros despite the user testing on a
    // save with real progression. Root cause: SpawnManager.OnPlayerSpawn fires as soon as P1's
    // Penitent object exists, but the save file's own EntityStats.SetCurrentPersistentState (which
    // populates the *real* PermanetBonus values from disk) evidently hasn't necessarily run yet at
    // that exact moment - reading p1.Stats synchronously in the same frame can race it. Delaying a
    // handful of frames via a coroutine (hosted on p2, since Penitent is a real MonoBehaviour) before
    // reading P1's stats avoids the race without needing to detect it - correctly delays even into a
    // second/third frame if needed, cheap and imperceptible since this only ever runs once per save
    // slot. The synchronous version below now runs from PerformSync, not directly from
    // OnPlayerSpawn - always go through EnsureSynced.
    // Round 46: the 5-frame delay only exists to dodge the race described above, which only
    // matters for the genuinely-first-ever sync (reading P1's live stats before the save file has
    // necessarily finished restoring them). Every *later* respawn goes through ApplySnapshot,
    // which never reads p1 at all - so routing it through the same delayed coroutine was pure
    // unnecessary lag, and on an ordinary room transition (which can involve a real loading pause,
    // during which yield-return-null-based frame counting can take a perceptible chunk of wall-
    // clock time to advance 5 times) that lag was long enough for the user to see P2's HUD
    // genuinely show fresh/base Life/Fervour/Purge for a moment before snapping to the restored
    // values - read by the user as "todo de P2 se resetea al cambiar de sala". Checking file
    // existence synchronously here and restoring immediately (no coroutine, no delay at all) for
    // the common case removes that window entirely; the delay now only ever applies to the
    // once-per-save first-time sync.
    internal static void EnsureSynced(Penitent p1, Penitent p2)
    {
        if (p1 == null || p2 == null)
        {
            return;
        }
        int slot = PersistentManager.GetAutomaticSlot();
        if (slot < 0)
        {
            return;
        }
        string path = SnapshotPath(slot);
        if (System.IO.File.Exists(path))
        {
            ApplySnapshot(path, p2, (EntityStats.StatsTypes[])Enum.GetValues(typeof(EntityStats.StatsTypes)));
            return;
        }
        p2.StartCoroutine(DelayedFirstSync(p1, p2));
    }

    private static System.Collections.IEnumerator DelayedFirstSync(Penitent p1, Penitent p2)
    {
        for (int i = 0; i < 5; i++)
        {
            yield return null;
        }
        if (p1 == null || p2 == null)
        {
            yield break;
        }
        PerformFirstSync(p1, p2);
    }

    private static void PerformFirstSync(Penitent p1, Penitent p2)
    {
        int slot = PersistentManager.GetAutomaticSlot();
        if (slot < 0)
        {
            // No save slot active yet - shouldn't normally happen once P1 exists, but skip rather
            // than write a marker under a meaningless bucket.
            return;
        }

        EntityStats.StatsTypes[] allTypes = (EntityStats.StatsTypes[])Enum.GetValues(typeof(EntityStats.StatsTypes));
        string path = SnapshotPath(slot);

        if (System.IO.File.Exists(path))
        {
            // Another spawn's own sync (e.g. a very fast second room change) already wrote the
            // baseline while this one was mid-delay - just restore it instead of double-syncing.
            ApplySnapshot(path, p2, allTypes);
            return;
        }

        foreach (EntityStats.StatsTypes type in allTypes)
        {
            Framework.FrameworkCore.Attributes.Logic.Attribute p1Attr = p1.Stats.GetByType(type);
            Framework.FrameworkCore.Attributes.Logic.Attribute p2Attr = p2.Stats.GetByType(type);
            if (p1Attr == null || p2Attr == null)
            {
                continue;
            }
            p2Attr.SetPermanentBonus(p1Attr.PermanetBonus);
        }
        // First-ever sync only: full heal makes sense as a fresh starting point (and lets the
        // user test prayers immediately) - every *later* respawn restores the persisted current
        // values instead (see ApplySnapshot), it does not force max again.
        p2.Stats.Life.SetToCurrentMax();
        p2.Stats.Flask.SetToCurrentMax();
        p2.Stats.Fervour.SetToCurrentMax();
        // Round 43: the user explicitly asked for P1's current currency to be copied too - P2
        // previously always started at 0 since Purge.Current isn't part of the PermanetBonus
        // loop above (see PurgeCurrentKey's own comment).
        p2.Stats.Purge.Current = p1.Stats.Purge.Current;

        SaveSnapshot(path, p2, allTypes);

        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info(
                $"[P2StatsSync] first-ever sync for save slot {slot}: cloned P1's progression onto P2 and saved a baseline. " +
                $"P2.Life.Final={p2.Stats.Life.Final:F0}, P2.Strength.Final={p2.Stats.Strength.Final:F1}, P2.Flask.Final={p2.Stats.Flask.Final:F0}",
                Main.CoopLocal);
        }
    }

    private static void SaveSnapshot(string path, Penitent p2, EntityStats.StatsTypes[] allTypes)
    {
        try
        {
            System.IO.Directory.CreateDirectory(MarkerDirectory);
            List<string> lines = new List<string>();
            foreach (EntityStats.StatsTypes type in allTypes)
            {
                Framework.FrameworkCore.Attributes.Logic.Attribute attr = p2.Stats.GetByType(type);
                if (attr == null)
                {
                    continue;
                }
                lines.Add($"{type}={attr.PermanetBonus.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            }
            lines.Add($"{PurgeCurrentKey}={p2.Stats.Purge.Current.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            lines.Add($"{LifeCurrentKey}={p2.Stats.Life.Current.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            lines.Add($"{FervourCurrentKey}={p2.Stats.Fervour.Current.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            lines.Add($"{FlaskCurrentKey}={p2.Stats.Flask.Current.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            System.IO.File.WriteAllLines(path, lines.ToArray());
        }
        catch (Exception ex)
        {
            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info($"[P2StatsSync] failed to save baseline: {ex.Message}", Main.CoopLocal);
            }
        }
    }

    // Round 43/45: P2's currency/life/fervour/flasks all change continuously during gameplay, but
    // P2's whole EntityStats gets recreated from scratch on every respawn (same architectural
    // issue the PermanetBonus snapshot exists to work around) - without this, all four would
    // silently reset to a stale earlier value on every subsequent respawn. Called from
    // CoopLocal.OnPlayerSpawn right before the outgoing P2 is destroyed, so the *next* spawn's
    // ApplySnapshot picks up the freshest values rather than stale ones.
    internal static void SaveCurrentVitals(Penitent outgoingP2)
    {
        if (outgoingP2 == null)
        {
            return;
        }
        int slot = PersistentManager.GetAutomaticSlot();
        if (slot < 0)
        {
            return;
        }
        string path = SnapshotPath(slot);
        if (!System.IO.File.Exists(path))
        {
            // No baseline yet for this slot - the upcoming first-ever sync will capture P1's
            // current values directly, nothing to update here.
            return;
        }
        try
        {
            List<string> lines = new List<string>(System.IO.File.ReadAllLines(path));
            UpsertLine(lines, PurgeCurrentKey, outgoingP2.Stats.Purge.Current);
            UpsertLine(lines, LifeCurrentKey, outgoingP2.Stats.Life.Current);
            UpsertLine(lines, FervourCurrentKey, outgoingP2.Stats.Fervour.Current);
            UpsertLine(lines, FlaskCurrentKey, outgoingP2.Stats.Flask.Current);
            System.IO.File.WriteAllLines(path, lines.ToArray());
        }
        catch (Exception ex)
        {
            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info($"[P2StatsSync] failed to save vitals before respawn: {ex.Message}", Main.CoopLocal);
            }
        }
    }

    // Round 45: PrieDieu.ShallowActivationLogic (the real "resting at a shrine" heal, patched
    // separately below) calls this to give P2 the same treatment P1 gets - full life/flasks, and
    // Fervour only if the same Alms upgrade condition P1's own heal checks is met. Persists
    // immediately so the healed values survive the very next respawn correctly.
    internal static void HealAtPrieDieu(Penitent p2, bool healFervour)
    {
        if (p2 == null)
        {
            return;
        }
        p2.Stats.Life.SetToCurrentMax();
        p2.Stats.Flask.SetToCurrentMax();
        if (healFervour)
        {
            p2.Stats.Fervour.SetToCurrentMax();
        }
        SaveCurrentVitals(p2);
        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info(
                $"[P2StatsSync] healed P2 at Prie Dieu (Life/Flask to max, Fervour healed={healFervour}).",
                Main.CoopLocal);
        }
    }

    private static void UpsertLine(List<string> lines, string key, float value)
    {
        string newLine = $"{key}={value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        int existingIndex = lines.FindIndex(l => l.StartsWith(key + "=", StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            lines[existingIndex] = newLine;
        }
        else
        {
            lines.Add(newLine);
        }
    }

    private static void ApplySnapshot(string path, Penitent p2, EntityStats.StatsTypes[] allTypes)
    {
        try
        {
            string[] lines = System.IO.File.ReadAllLines(path);
            int applied = 0;
            foreach (string line in lines)
            {
                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }
                string key = line.Substring(0, eq);
                string valueText = line.Substring(eq + 1);
                float value;
                if (!float.TryParse(valueText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
                {
                    continue;
                }
                if (key == PurgeCurrentKey)
                {
                    p2.Stats.Purge.Current = value;
                    applied++;
                    continue;
                }
                if (key == LifeCurrentKey)
                {
                    p2.Stats.Life.Current = value;
                    applied++;
                    continue;
                }
                if (key == FervourCurrentKey)
                {
                    p2.Stats.Fervour.Current = value;
                    applied++;
                    continue;
                }
                if (key == FlaskCurrentKey)
                {
                    p2.Stats.Flask.Current = value;
                    applied++;
                    continue;
                }
                if (!Enum.IsDefined(typeof(EntityStats.StatsTypes), key))
                {
                    continue;
                }
                EntityStats.StatsTypes type = (EntityStats.StatsTypes)Enum.Parse(typeof(EntityStats.StatsTypes), key);
                Framework.FrameworkCore.Attributes.Logic.Attribute attr = p2.Stats.GetByType(type);
                if (attr == null)
                {
                    continue;
                }
                attr.SetPermanentBonus(value);
                applied++;
            }
            // Round 45: no longer forces Life/Flask to max here - that was the actual cause of
            // "todo de P2 se resetea al cambiar de sala" (every ordinary room transition fires
            // OnPlayerSpawn, not just death/checkpoint respawns). Those two are restored from the
            // snapshot above instead; a real heal only happens via PrieDieu.ShallowActivationLogic's
            // own Postfix (HealAtPrieDieu) or the first-ever sync.
            //
            // Round 46: Fervour is a deliberate exception, per explicit user request - always
            // force it to max on spawn so prayers can be tested immediately, overriding whatever
            // FervourCurrentKey just restored above. Remove this line (and the matching one in
            // PerformFirstSync) if/when the user wants Fervour to persist like Life/Flask do.
            p2.Stats.Fervour.SetToCurrentMax();

            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info(
                    $"[P2StatsSync] restored P2's saved baseline ({applied} stats) for save slot. " +
                    $"Life={p2.Stats.Life.Current:F0}/{p2.Stats.Life.Final:F0} Fervour={p2.Stats.Fervour.Current:F0}/{p2.Stats.Fervour.CurrentMax:F0} " +
                    $"Flask={p2.Stats.Flask.Current:F0}/{p2.Stats.Flask.Final:F0} Purge={p2.Stats.Purge.Current:F0}",
                    Main.CoopLocal);
            }
        }
        catch (Exception ex)
        {
            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info($"[P2StatsSync] failed to restore baseline: {ex.Message}", Main.CoopLocal);
            }
        }
    }
}

// Round 45: the real "rest at a shrine" heal - PrieDieu.ShallowActivationLogic (private, called
// from both first-time and repeat-use activation coroutines) hardcodes Core.Logic.Penitent for
// Life/Flask/Fervour healing, same as everywhere else this session, but this one matters for a
// different reason than "wrong owner": P2 doesn't have its OWN PrieDieu component at all (P1's is
// the only one, tied to the single shared shrine), so there's nothing to "fix the owner of" - P2
// simply never got healed here. Postfix (not Prefix, since vanilla's own P1 heal should still run
// normally) adds the same treatment for P2, gating Fervour on the identical
// Core.Alms.GetPrieDieuLevel() > 1 condition P1's own heal checks.
[HarmonyPatch(typeof(Tools.Level.Interactables.PrieDieu), "ShallowActivationLogic")]
internal static class PrieDieu_ShallowActivationLogic_HealPlayer2_Patch
{
    private static void Postfix()
    {
        Penitent p2 = CoopLocal.Player2;
        if (p2 == null)
        {
            return;
        }
        bool healFervour = Core.Alms.GetPrieDieuLevel() > 1;
        Player2StatsSync.HealAtPrieDieu(p2, healFervour);
    }
}

// Round 44: user reported P2 getting "stuck" to walls (cliff-ledge grab) whenever *P1* presses
// attack, and jumping off ladders whenever *P1* presses jump - two separate bugs in two separate
// ability classes, both previously untouched since neither is an AnimationBehaviour (the ~50-class
// batch scan from earlier this session only covered StateMachineBehaviour subclasses).
//
// GrabCliffLede.Start() does `_penitent = Core.Logic.Penitent;` - the exact same "wrong owner"
// hardcode found dozens of times already, just in a per-Penitent MonoBehaviour component instead
// of an AnimationBehaviour. Every method in the class (Update/OnTriggerStay2D/grabCliffLede/etc)
// reads P1's IsFalling/IsGrounded/animator state through this one field, so P2's own wall-cling
// eligibility was being decided by P1's movement state instead of P2's own.
//
// Round 47 correction: originally "fixed" here with a Prefix (removed - it never actually did
// anything). Turns out this exact bug, on this exact method, was *already* fixed much earlier
// this session by GrabCliffLede_Start_Patch (search this file - a Postfix using
// GetComponentInParent<Penitent>()), whose own comment explicitly explains why a Prefix can't
// work here: Start()'s real assignment has no null-guard at all (`_penitent = Core.Logic.Penitent;`
// unconditionally, every single call, not "only if null" like the AnimationBehaviour family), so
// any Prefix pre-setting the field just gets silently overwritten by vanilla's own body a moment
// later - only a Postfix (running *after* vanilla overwrites it) can actually stick. The Prefix
// added here was therefore dead code the whole time - confirmed harmless (the pre-existing Postfix
// still corrected the field correctly afterward either way) but misleading, so removed. The
// diagnostic Postfix below (added the same round as the dead Prefix) is unaffected by any of this
// and remains accurate - its own log lines already prove the owner resolves to P2 correctly.
//
// Round 45: no log data existed yet to confirm what was/wasn't working here (unlike GrabLadder,
// which already had its own debug logger from earlier in the session), so this diagnostic was
// added rather than guessing blind. Mirrors GrabLadder_OnUpdate_DebugLogger_Patch's own approach -
// logs P2's own grab-eligibility state (the exact fields OnTriggerStay2D's condition checks)
// every time it changes. This is what actually found the real cause (see CoopLocal.cs's
// SetLayerRecursively / LevelManager.OnLevelLoaded re-sync, round 46/47) - _grabbedCliffLede
// stayed null across thousands of airborne frames, which OnTriggerEnter2D only ever sets from
// pure Unity physics-layer filtering, no ownership logic involved.
[HarmonyPatch(typeof(GrabCliffLede), "Update")]
internal static class GrabCliffLede_Update_DebugLogger_Patch
{
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(GrabCliffLede), "_penitent");
    private static readonly FieldInfo GrabbedCliffLedeField = AccessTools.Field(typeof(GrabCliffLede), "_grabbedCliffLede");
    private static readonly FieldInfo IsGrabbedCliffLedeField = AccessTools.Field(typeof(GrabCliffLede), "_isGrabbedCliffLede");
    private static readonly FieldInfo IsAirAttackingField = AccessTools.Field(typeof(GrabCliffLede), "_isAirAttacking");
    private static readonly FieldInfo RemainCooldownField = AccessTools.Field(typeof(GrabCliffLede), "remainCooldown");
    private static string lastLoggedState;

    private static void Postfix(GrabCliffLede __instance)
    {
        Penitent owner = (Penitent)PenitentField.GetValue(__instance);
        if (owner == null || owner != CoopLocal.Player2)
        {
            return;
        }
        Collider2D grabbedCliffLede = (Collider2D)GrabbedCliffLedeField.GetValue(__instance);
        bool isGrabbed = (bool)IsGrabbedCliffLedeField.GetValue(__instance);
        bool isAirAttacking = (bool)IsAirAttackingField.GetValue(__instance);
        float remainCooldown = (float)RemainCooldownField.GetValue(__instance);
        string state = $"grabbedCliffLede={(grabbedCliffLede != null ? grabbedCliffLede.name : "null")} isGrabbed={isGrabbed} " +
            $"isAirAttacking={isAirAttacking} remainCooldown={remainCooldown:F2} IsGrabbingCliffLede={owner.IsGrabbingCliffLede} " +
            $"IsJumpingOff={owner.IsJumpingOff} IsDashing={owner.IsDashing} IsFalling={owner.AnimatorInyector.IsFalling} " +
            $"IsGrounded={owner.Status.IsGrounded} canClimbCliffLede={owner.canClimbCliffLede}";
        if (state != lastLoggedState)
        {
            lastLoggedState = state;
            DashParryDebugLog.Log($"P2 GrabCliffLede.Update: {state} (frame {Time.frameCount})");
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
