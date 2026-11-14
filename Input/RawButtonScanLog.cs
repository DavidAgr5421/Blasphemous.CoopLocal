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
