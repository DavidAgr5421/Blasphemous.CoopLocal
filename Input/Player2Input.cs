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

    /// <summary>
    /// F9: intercambia teclado y mando SOLO entre los jugadores locales de esta PC (los puestos online no cuentan).
    /// Con un único jugador local no hace nada (se controla como en vanilla).
    /// </summary>
    internal static void CycleKeyboardPlayer()
    {
        int activeCount = CoopGameConfig.IsCoopEnabled ? Mathf.Clamp(CoopGameConfig.PlayerCount, 2, 4) : 1;
        if (activeCount <= 1) return;

        var local = new List<int>();
        for (int i = 0; i < activeCount; i++)
        {
            if (CoopGameConfig.Slots[i].Device != CoopDeviceType.RemoteSteam) local.Add(i);
        }
        if (local.Count <= 1) return;

        // El punto de partida es el puesto local que realmente tiene el teclado.
        int pos = local.IndexOf(ActiveKeyboardPlayerIndex);
        for (int k = 0; k < local.Count; k++)
        {
            if (CoopGameConfig.Slots[local[k]].Device == CoopDeviceType.Keyboard) { pos = k; break; }
        }
        if (pos < 0) pos = 0;
        ApplyKeyboardPlayer(local[(pos + 1) % local.Count], announce: true);
    }

    internal static void ApplyKeyboardPlayer(int playerIndex, bool announce = false)
    {
        int activeCount = CoopGameConfig.IsCoopEnabled ? Mathf.Clamp(CoopGameConfig.PlayerCount, 2, 4) : 1;
        if (playerIndex >= activeCount)
        {
            playerIndex = 0;
        }
        ActiveKeyboardPlayerIndex = Mathf.Clamp(playerIndex, 0, activeCount - 1);

        // Swap devices instead of renumbering every pad: the player who held the keyboard takes
        // the pad of the player who receives it, so every other player keeps the SAME physical
        // gamepad. (Renumbering shifted every pad by one on each F9 press, which made P2's pad
        // open P1's menu, P3's pad open P2's, etc.)
        int oldHolder = -1;
        for (int i = 0; i < 4; i++)
        {
            if (CoopGameConfig.Slots[i].Device == CoopDeviceType.Keyboard) { oldHolder = i; break; }
        }

        if (oldHolder != ActiveKeyboardPlayerIndex)
        {
            CoopDeviceType targetDevice = CoopGameConfig.Slots[ActiveKeyboardPlayerIndex].Device;
            if (oldHolder >= 0)
            {
                bool targetIsPad = targetDevice >= CoopDeviceType.Gamepad1 && targetDevice <= CoopDeviceType.Gamepad4;
                CoopGameConfig.Slots[oldHolder].Device = targetIsPad ? targetDevice : CoopDeviceType.Unassigned;
            }
            CoopGameConfig.Slots[ActiveKeyboardPlayerIndex].Device = CoopDeviceType.Keyboard;
        }

        // Make sure no non-keyboard local slot is left without a gamepad (or shares one).
        bool[] padUsed = new bool[4];
        for (int i = 0; i < 4; i++)
        {
            CoopDeviceType d = CoopGameConfig.Slots[i].Device;
            if (d >= CoopDeviceType.Gamepad1 && d <= CoopDeviceType.Gamepad4)
            {
                int padIdx = (int)d - (int)CoopDeviceType.Gamepad1;
                if (padUsed[padIdx]) CoopGameConfig.Slots[i].Device = CoopDeviceType.Unassigned;
                else padUsed[padIdx] = true;
            }
        }
        for (int i = 0; i < 4; i++)
        {
            CoopDeviceType d = CoopGameConfig.Slots[i].Device;
            if (d != CoopDeviceType.Unassigned) continue;
            for (int g = 0; g < 4; g++)
            {
                if (!padUsed[g])
                {
                    padUsed[g] = true;
                    CoopGameConfig.Slots[i].Device = (CoopDeviceType)((int)CoopDeviceType.Gamepad1 + g);
                    break;
                }
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

        if (announce) Player2ModeIndicator.ShowPlayer(ActiveKeyboardPlayerIndex, pName);

        Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
        LogModeAndControllers($"F9 -> Teclado asignado a P{ActiveKeyboardPlayerIndex + 1} ({pName})");
    }

    // Métodos para consultar input por ranura de jugador (P2=1, P3=2, P4=3)
    internal static bool GetLeft(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < 4 && CoopGameConfig.Slots[slotIndex].Device == CoopDeviceType.RemoteSteam)
            return CoopNetworkManager.GetRemoteButton(slotIndex, CoopButtonFlags.Left) || CoopNetworkManager.GetRemoteAxisX(slotIndex) < -0.3f;
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKey(CoopControlsConfig.GetBindings(slotIndex).KeyLeft);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetLeft(pad);
    }

    internal static bool GetRight(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < 4 && CoopGameConfig.Slots[slotIndex].Device == CoopDeviceType.RemoteSteam)
            return CoopNetworkManager.GetRemoteButton(slotIndex, CoopButtonFlags.Right) || CoopNetworkManager.GetRemoteAxisX(slotIndex) > 0.3f;
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKey(CoopControlsConfig.GetBindings(slotIndex).KeyRight);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetRight(pad);
    }

    internal static bool GetUp(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < 4 && CoopGameConfig.Slots[slotIndex].Device == CoopDeviceType.RemoteSteam)
            return CoopNetworkManager.GetRemoteButton(slotIndex, CoopButtonFlags.Up) || CoopNetworkManager.GetRemoteAxisY(slotIndex) > 0.3f;
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKey(CoopControlsConfig.GetBindings(slotIndex).KeyUp);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetUp(pad);
    }

    internal static bool GetDown(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < 4 && CoopGameConfig.Slots[slotIndex].Device == CoopDeviceType.RemoteSteam)
            return CoopNetworkManager.GetRemoteButton(slotIndex, CoopButtonFlags.Down) || CoopNetworkManager.GetRemoteAxisY(slotIndex) < -0.3f;
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKey(CoopControlsConfig.GetBindings(slotIndex).KeyDown);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetDown(pad);
    }

    internal static bool GetJumpHeld(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < 4 && CoopGameConfig.Slots[slotIndex].Device == CoopDeviceType.RemoteSteam)
            return CoopNetworkManager.GetRemoteButton(slotIndex, CoopButtonFlags.Jump);
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKey(CoopControlsConfig.GetBindings(slotIndex).KeyJump);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetJumpHeld(pad, slotIndex);
    }

    internal static bool GetAttackDown(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < 4 && CoopGameConfig.Slots[slotIndex].Device == CoopDeviceType.RemoteSteam)
            return CoopNetworkManager.GetRemoteButtonDown(slotIndex, CoopButtonFlags.Attack);
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKeyDown(CoopControlsConfig.GetBindings(slotIndex).KeyAttack);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetAttackDown(pad, slotIndex);
    }

    internal static bool GetAttackUp(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < 4 && CoopGameConfig.Slots[slotIndex].Device == CoopDeviceType.RemoteSteam)
            return CoopNetworkManager.GetRemoteButtonUp(slotIndex, CoopButtonFlags.Attack);
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKeyUp(CoopControlsConfig.GetBindings(slotIndex).KeyAttack);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetAttackUp(pad, slotIndex);
    }

    // Ataque a Distancia: botón propio (KeyRange / PadRange). Mantener/soltar ATAQUE no lo dispara.
    internal static bool GetRangeAttackDown(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < 4 && CoopGameConfig.Slots[slotIndex].Device == CoopDeviceType.RemoteSteam)
            return CoopNetworkManager.GetRemoteButtonDown(slotIndex, CoopButtonFlags.Range);
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKeyDown(CoopControlsConfig.GetBindings(slotIndex).KeyRange);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetRangeDown(pad, slotIndex);
    }

    internal static bool GetRangeAttackUp(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < 4 && CoopGameConfig.Slots[slotIndex].Device == CoopDeviceType.RemoteSteam)
            return CoopNetworkManager.GetRemoteButtonUp(slotIndex, CoopButtonFlags.Range);
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKeyUp(CoopControlsConfig.GetBindings(slotIndex).KeyRange);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetRangeUp(pad, slotIndex);
    }

    internal static bool GetAttackHeld(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < 4 && CoopGameConfig.Slots[slotIndex].Device == CoopDeviceType.RemoteSteam)
            return CoopNetworkManager.GetRemoteButton(slotIndex, CoopButtonFlags.Attack);
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKey(CoopControlsConfig.GetBindings(slotIndex).KeyAttack);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetAttackHeld(pad, slotIndex);
    }

    internal static bool GetDashDown(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < 4 && CoopGameConfig.Slots[slotIndex].Device == CoopDeviceType.RemoteSteam)
            return CoopNetworkManager.GetRemoteButtonDown(slotIndex, CoopButtonFlags.Dash);
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKeyDown(CoopControlsConfig.GetBindings(slotIndex).KeyDash);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetDashDown(pad, slotIndex);
    }

    internal static bool GetDashHeld(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < 4 && CoopGameConfig.Slots[slotIndex].Device == CoopDeviceType.RemoteSteam)
            return CoopNetworkManager.GetRemoteButton(slotIndex, CoopButtonFlags.Dash);
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKey(CoopControlsConfig.GetBindings(slotIndex).KeyDash);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetDashHeld(pad, slotIndex);
    }

    internal static bool GetParryDown(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < 4 && CoopGameConfig.Slots[slotIndex].Device == CoopDeviceType.RemoteSteam)
            return CoopNetworkManager.GetRemoteButtonDown(slotIndex, CoopButtonFlags.Parry);
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKeyDown(CoopControlsConfig.GetBindings(slotIndex).KeyParry);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetParryDown(pad, slotIndex);
    }

    internal static bool GetHealDown(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < 4 && CoopGameConfig.Slots[slotIndex].Device == CoopDeviceType.RemoteSteam)
            return CoopNetworkManager.GetRemoteButtonDown(slotIndex, CoopButtonFlags.Heal);
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKeyDown(CoopControlsConfig.GetBindings(slotIndex).KeyHeal);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetHealDown(pad, slotIndex);
    }

    internal static bool GetInteractDown(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < 4 && CoopGameConfig.Slots[slotIndex].Device == CoopDeviceType.RemoteSteam)
            return CoopNetworkManager.GetRemoteButtonDown(slotIndex, CoopButtonFlags.Interact);
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKeyDown(CoopControlsConfig.GetBindings(slotIndex).KeyInteract);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetInteractDown(pad, slotIndex);
    }

    internal static bool GetPrayerActivateDown(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < 4 && CoopGameConfig.Slots[slotIndex].Device == CoopDeviceType.RemoteSteam)
            return CoopNetworkManager.GetRemoteButtonDown(slotIndex, CoopButtonFlags.Prayer);
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKeyDown(CoopControlsConfig.GetBindings(slotIndex).KeyPrayer);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetPrayerActivateDown(pad, slotIndex);
    }

    internal static bool GetPrayerActivateUp(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < 4 && CoopGameConfig.Slots[slotIndex].Device == CoopDeviceType.RemoteSteam)
            return CoopNetworkManager.GetRemoteButtonUp(slotIndex, CoopButtonFlags.Prayer);
        if (slotIndex == ActiveKeyboardPlayerIndex) return Input.GetKeyUp(CoopControlsConfig.GetBindings(slotIndex).KeyPrayer);
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetPrayerActivateUp(pad, slotIndex);
    }

    internal static bool GetPauseDown(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < 4 && CoopGameConfig.Slots[slotIndex].Device == CoopDeviceType.RemoteSteam)
            return CoopNetworkManager.GetRemoteButtonDown(slotIndex, CoopButtonFlags.Pause);
        if (slotIndex == ActiveKeyboardPlayerIndex)
        {
            KeyCode k = CoopControlsConfig.GetBindings(slotIndex).KeyPause;
            return Input.GetKeyDown(k != KeyCode.None ? k : KeyCode.Escape);
        }
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        if (pad != null)
        {
            string mapped = CoopControlsConfig.GetBindings(slotIndex).PadPause;
            if (!string.IsNullOrEmpty(mapped))
            {
                if (Player2Pad.GetButtonDownMapped(pad, mapped)) return true;
            }
            return Player2Pad.GetStartDown(pad);
        }
        return false;
    }

    internal static bool HasAnyInput(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < 4 && CoopGameConfig.Slots[slotIndex].Device == CoopDeviceType.RemoteSteam)
            return CoopNetworkManager.HasRemoteInput(slotIndex);
        if (slotIndex == ActiveKeyboardPlayerIndex && Input.anyKey) return true;
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        if (pad != null)
        {
            if (pad.GetAnyButton() || pad.GetAnyButtonDown()) return true;
            if (Mathf.Abs(pad.GetAxis(0)) >= 0.2f || Mathf.Abs(pad.GetAxis(1)) >= 0.2f) return true;
        }
        return GetLeft(slotIndex) || GetRight(slotIndex) || GetUp(slotIndex) || GetDown(slotIndex)
            || GetAttackDown(slotIndex) || GetAttackHeld(slotIndex)
            || GetJumpHeld(slotIndex)
            || GetDashDown(slotIndex) || GetDashHeld(slotIndex)
            || GetParryDown(slotIndex)
            || GetHealDown(slotIndex)
            || GetInteractDown(slotIndex)
            || GetPrayerActivateDown(slotIndex)
            || GetPauseDown(slotIndex)
            || GetMenuDown(slotIndex);
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

    // Igual que JumpDown pero por ranura (P2-P4): flanco de subida de GetJumpHeld(slot), calculado una sola vez por fotograma
    // (varios consumidores en el mismo fotograma ven el mismo valor) sin depender de Tick(), que solo sigue a P2.
    private static readonly FrameEdge[] jumpEdgeBySlot = { new FrameEdge(), new FrameEdge(), new FrameEdge(), new FrameEdge() };

    internal static bool GetJumpDown(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= 4) return false;
        FrameEdge edge = jumpEdgeBySlot[slotIndex];
        int frame = Time.frameCount;
        return edge.IsStale(frame) ? edge.Update(frame, GetJumpHeld(slotIndex)) : edge.Down;
    }

    // Flanco de subida de "Arriba" por ranura (una vez por fotograma): para interactuar (Prie-Dieu, puertas) hace falta una
    // pulsacion nueva; con GetUp (mantenido) bastaba con llegar al santuario ya con Arriba pulsado.
    private static readonly FrameEdge[] upEdgeBySlot = { new FrameEdge(), new FrameEdge(), new FrameEdge(), new FrameEdge() };

    internal static bool GetUpDown(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= 4) return false;
        FrameEdge edge = upEdgeBySlot[slotIndex];
        int frame = Time.frameCount;
        return edge.IsStale(frame) ? edge.Update(frame, GetUp(slotIndex)) : edge.Down;
    }
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
        if (slotIndex >= 0 && slotIndex < 4 && CoopGameConfig.Slots[slotIndex].Device == CoopDeviceType.RemoteSteam)
            return CoopNetworkManager.GetRemoteButtonDown(slotIndex, CoopButtonFlags.Menu);
        if (slotIndex == ActiveKeyboardPlayerIndex)
        {
            KeyCode k = CoopControlsConfig.GetBindings(slotIndex).KeyMenu;
            if (k != KeyCode.None && Input.GetKeyDown(k)) return true;
            if (Input.GetKeyDown(Player2Keys.Menu) || Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.M)) return true;
            return false;
        }
        var pad = Player2Pad.GetPadForSlot(slotIndex);
        return Player2Pad.GetMenuDown(pad, slotIndex);
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
        if (Input.GetKeyDown(KeyCode.F5) && CoopSettings.Admin)
        {
            Combat.TestDummyManager.SpawnOrResetDummy();
        }

        // Atajo F2: Selector de jefes para práctica / depuración (solo cuando CoopGameSetupUI no esté abierto)
        if (Input.GetKeyDown(KeyCode.F2) && CoopSettings.Admin && !CoopGameSetupUI.IsOpen)
        {
            Combat.BossDebugTeleportManager.ToggleMenu();
        }

        // Menú de Pausa / Opciones / Configuración de Controles independiente para cada jugador
        if (!PrieDieuCoopSync.IsKneeling && !CoopGameSetupUI.IsOpen && !HudLayoutDebugEditor.IsOpen && !CoopDebugMenu.IsOpen && !Combat.BossDebugTeleportManager.IsMenuOpen && !(Gameplay.UI.UIController.instance != null && Gameplay.UI.UIController.instance.IsShowingMenu))
        {
            // Menú de sala del anfitrión (P1): Tab en teclado, Start+Back en mando. Permite abrir/cerrar la sala online y copiar el código.
            if (!CoopPlayerPauseMenu.IsOpen && Framework.Managers.Core.Logic != null && Framework.Managers.Core.Logic.Penitent != null
                && (Input.GetKeyDown(KeyCode.Tab) || Player2Pad.GetHostMenuDown(Player2Pad.P1Pad)))
            {
                CoopPlayerPauseMenu.OpenMenu(0);
            }

            if (!CoopPlayerPauseMenu.IsOpen && CoopGameConfig.IsCoopEnabled)
            {
                int maxPlayers = Mathf.Clamp(CoopGameConfig.PlayerCount, 2, 4);
                for (int slot = 1; slot < maxPlayers; slot++)
                {
                    if (GetPauseDown(slot))
                    {
                        CoopPlayerPauseMenu.OpenMenu(slot);
                        break;
                    }
                }
            }
        }

        // Dentro de Tick(), lógica P2 para abrir menú de inventario y atajo F6/F7 de fervor máximo:
        // Atajo de prueba Hot-Plug dinámico (F10 + 2 / 3 / 4 para conectar/desconectar P2, P3, P4)
        if (Input.GetKey(KeyCode.F10) && CoopSettings.Admin)
        {
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
            {
                CoopLocal.ToggleGuestPlayerDynamically(1);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
            {
                CoopLocal.ToggleGuestPlayerDynamically(2);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4))
            {
                CoopLocal.ToggleGuestPlayerDynamically(3);
            }
        }

        if (Input.GetKeyDown(KeyCode.F6) && CoopSettings.Admin)
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
                Blasphemous.ModdingAPI.ModLog.Info("[FervourShortcut] F6 pressed -> P1 & P2 Fervour set to CurrentMax", Main.CoopLocal);
            }
        }
        if (!PrieDieuCoopSync.IsKneeling && !CoopGameSetupUI.IsOpen && !CoopPlayerPauseMenu.IsOpen && !HudLayoutDebugEditor.IsOpen && !Combat.BossDebugTeleportManager.IsMenuOpen)
        {
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
        }
// Ronda 49: ocultado a pedido del usuario - el tuner interactivo usa flechas/./-/+, las
        // mismas teclas con historial de solaparse con P1. La clase sigue intacta en
        // HUD/HudPositionTuner.cs por si se necesita retomar el ajuste manual del HUD más adelante;
        // solo se dejó de invocar su Tick().
        // Player2HudPositionTuner.Tick();
        Player2PurgePoints.Tick();
        CoopPurgeHUD.Tick();
        }

        // Opening the shared inventory/prayer menu isn't per-player state (there's only one
        // save's worth of inventory/prayers), so this just calls the same public method the
        // game's own menu button does - no Ability/Rewired-owner scoping needed.
// Ronda 49: ocultado a pedido del usuario - el tuner interactivo usa flechas/./-/+, las

    private static readonly System.Reflection.FieldInfo NewInventoryMenuField =
        Reflect.Field(typeof(Gameplay.UI.UIController), "newInventoryMenu");
    private static readonly System.Reflection.FieldInfo CurrentTabTypeField =
        Reflect.Field(typeof(NewInventoryWidget), "currentTabType");
    private static readonly System.Reflection.MethodInfo SelectTabMethod =
        Reflect.Method(typeof(NewInventoryWidget), "SelectTab",
            new[] { typeof(NewInventoryWidget.TabType), typeof(bool) });

    internal static void RefreshDeviceAssignment()
    {
        ApplyExclusiveDevices();
    }

    private static Rewired.ControllerType ExcludedFromPlayer0
    {
        get
        {
            if (Core.GameModeManager != null && Core.GameModeManager.IsCurrentMode(GameModeManager.GAME_MODES.MENU))
            {
                return (Rewired.ControllerType)(-1);
            }

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
        Rewired.ControllerType excluded = ExcludedFromPlayer0;
        if ((int)excluded < 0)
        {
            p1Rewired.controllers.excludeFromControllerAutoAssignment = false;
            return;
        }
        p1Rewired.controllers.excludeFromControllerAutoAssignment = true;
        p1Rewired.controllers.ClearControllersOfType(excluded);
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
        if ((int)excluded < 0)
        {
            return;
        }
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

    internal static void ResetToMainMenuInput()
    {
        everTicked = false;
        ActiveKeyboardPlayerIndex = 0;
        Mode = Player2InputMode.Gamepad;

        try
        {
            Player2ModeIndicator.SetMenuOverlayHidden(true);

            Rewired.Player p1Rewired = GetP1Rewired();
            if (p1Rewired != null)
            {
                p1Rewired.controllers.excludeFromControllerAutoAssignment = false;

                if (Rewired.ReInput.isReady && Rewired.ReInput.controllers != null)
                {
                    var keyboard = Rewired.ReInput.controllers.Keyboard;
                    if (keyboard != null && !p1Rewired.controllers.ContainsController(keyboard))
                    {
                        p1Rewired.controllers.AddController(keyboard, false);
                    }

                    if (Rewired.ReInput.controllers.Joysticks != null)
                    {
                        foreach (var joy in Rewired.ReInput.controllers.Joysticks)
                        {
                            if (joy != null && !p1Rewired.controllers.ContainsController(joy))
                            {
                                p1Rewired.controllers.AddController(joy, false);
                            }
                        }
                    }
                }
            }

            CoopGameConfig.Slots[0].Device = CoopDeviceType.Keyboard;
            for (int i = 1; i < CoopGameConfig.Slots.Length; i++)
            {
                if (CoopGameConfig.Slots[i].Device == CoopDeviceType.Keyboard)
                {
                    CoopGameConfig.Slots[i].Device = (CoopDeviceType)(i + 1);
                }
            }
            CoopGameConfig.ApplyToRegistry();

            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info("[Player2Input] Teclado restaurado a P1 para navegación en el Menú Principal.", Main.CoopLocal);
            }
        }
        catch (Exception ex)
        {
            DashParryDebugLog.Log($"[Player2Input] Error restoring P1 main menu input: {ex.Message}");
        }
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
