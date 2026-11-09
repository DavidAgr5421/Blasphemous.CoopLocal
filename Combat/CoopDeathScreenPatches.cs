using Framework.Managers;
using HarmonyLib;
using Rewired;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace Blasphemous.CoopLocal;

/// <summary>
/// Helper para detectar si cualquier jugador (P1, P2, P3, P4) presiona cualquier botn en la pantalla de muerte
/// para avanzar / respawnear en el Prie-Dieu, ya sea mediante teclado, gamepad Rewired o gamepad de Unity.
/// </summary>
internal static class CoopDeathScreenInputHelper
{
    internal static bool IsAnyPlayerPressingAnyButton()
    {
        // 1. Unity standard anyKey / anyKeyDown (detecta cualquier tecla de teclado y botones del joystick 0)
        try
        {
            if (Input.anyKey || Input.anyKeyDown)
            {
                return true;
            }
        }
        catch { }

        // 2. Rewired players (todos los jugadores activos en Rewired: Player 0, Player 1, etc.)
        try
        {
            if (ReInput.isReady && ReInput.players != null)
            {
                var players = ReInput.players.Players;
                if (players != null)
                {
                    int count = players.Count;
                    for (int i = 0; i < count; i++)
                    {
                        var p = players[i];
                        if (p != null && (p.GetAnyButton() || p.GetAnyButtonDown()))
                        {
                            return true;
                        }
                    }
                }
            }
        }
        catch { }

        // 3. Controladores Rewired directamente (todos los Joysticks / Mandos conectados)
        try
        {
            if (ReInput.isReady && ReInput.controllers != null)
            {
                var joysticks = ReInput.controllers.Joysticks;
                if (joysticks != null)
                {
                    int count = joysticks.Count;
                    for (int i = 0; i < count; i++)
                    {
                        var j = joysticks[i];
                        if (j != null && (j.GetAnyButton() || j.GetAnyButtonDown()))
                        {
                            return true;
                        }
                    }
                }
            }
        }
        catch { }

        // 4. Mapeo específico de Player2Pad (botones y gatillos de P2)
        try
        {
            if (Player2Pad.Pad != null)
            {
                if (Player2Pad.AttackDown || Player2Pad.AttackHeld ||
                    Player2Pad.JumpHeld ||
                    Player2Pad.DashDown ||
                    Player2Pad.ParryDown ||
                    Player2Pad.InteractDown ||
                    Player2Pad.HealDown ||
                    Player2Pad.SelectDown ||
                    Player2Pad.CancelDown ||
                    Player2Pad.StartDown)
                {
                    return true;
                }
            }
        }
        catch { }

        // 5. Mapeo de Unity KeyCodes para mandos secundarios (Joystick 1 a 8, botones 0 a 19)
        try
        {
            for (int key = (int)KeyCode.JoystickButton0; key <= (int)KeyCode.Joystick8Button19; key++)
            {
                if (Input.GetKey((KeyCode)key))
                {
                    return true;
                }
            }
        }
        catch { }

        return false;
    }
}

[HarmonyPatch]
internal static class DeadScreenWidget_OnDeadAction_Transpiler_Patch
{
    private static MethodBase TargetMethod()
    {
        Type iterType = AccessTools.TypeByName("Gameplay.UI.Others.Buttons.DeadScreenWidget+<OnDeadAction>c__Iterator0");
        return AccessTools.Method(iterType, "MoveNext");
    }

    private static readonly MethodInfo InputAnyKeyGetter = AccessTools.PropertyGetter(typeof(Input), "anyKey");
    private static readonly MethodInfo Replacement = AccessTools.Method(typeof(CoopDeathScreenInputHelper), nameof(CoopDeathScreenInputHelper.IsAnyPlayerPressingAnyButton));

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        bool found = false;
        foreach (var instr in instructions)
        {
            if (instr.opcode == OpCodes.Call && instr.operand is MethodInfo m && m == InputAnyKeyGetter)
            {
                instr.operand = Replacement;
                found = true;
            }
            yield return instr;
        }
        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info($"[CoopDeath] Transpiled DeadScreenWidget OnDeadAction MoveNext: anyKey replaced={found}", Main.CoopLocal);
        }
    }
}

[HarmonyPatch]
internal static class DeadScreenWidget_OnDeadDemake_Transpiler_Patch
{
    private static MethodBase TargetMethod()
    {
        Type iterType = AccessTools.TypeByName("Gameplay.UI.Others.Buttons.DeadScreenWidget+<OnDeadDemake>c__Iterator3");
        return AccessTools.Method(iterType, "MoveNext");
    }

    private static readonly MethodInfo InputAnyKeyGetter = AccessTools.PropertyGetter(typeof(Input), "anyKey");
    private static readonly MethodInfo Replacement = AccessTools.Method(typeof(CoopDeathScreenInputHelper), nameof(CoopDeathScreenInputHelper.IsAnyPlayerPressingAnyButton));

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        bool found = false;
        foreach (var instr in instructions)
        {
            if (instr.opcode == OpCodes.Call && instr.operand is MethodInfo m && m == InputAnyKeyGetter)
            {
                instr.operand = Replacement;
                found = true;
            }
            yield return instr;
        }
        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info($"[CoopDeath] Transpiled DeadScreenWidget OnDeadDemake MoveNext: anyKey replaced={found}", Main.CoopLocal);
        }
    }
}

[HarmonyPatch(typeof(Gameplay.UI.Others.Buttons.DeadScreenWidget), "OnPenitentReady")]
internal static class DeadScreenWidget_OnPenitentReady_Patch
{
    private static void Prefix(Gameplay.UI.Others.Buttons.DeadScreenWidget __instance, Gameplay.GameControllers.Penitent.Penitent penitent)
    {
        if (penitent != null && penitent.OnDead != null)
        {
            try
            {
                Delegate[] invocations = penitent.OnDead.GetInvocationList();
                for (int i = 0; i < invocations.Length; i++)
                {
                    Delegate del = invocations[i];
                    if (del != null && del.Target == (object)__instance && del.Method != null && del.Method.Name == "OnDead")
                    {
                        penitent.OnDead = (Framework.Managers.Core.SimpleEvent)Delegate.Remove(penitent.OnDead, del);
                    }
                }
            }
            catch (Exception ex)
            {
                DashParryDebugLog.Log($"[CoopDeath] Error removing DeadScreenWidget.OnDead: {ex.Message}");
            }
        }
        DeadScreenWidget_OnDead_Guard_Patch.IsDeadScreenActive = false;
    }
}

[HarmonyPatch(typeof(Gameplay.UI.Others.Buttons.DeadScreenWidget), "OnDead")]
internal static class DeadScreenWidget_OnDead_Guard_Patch
{
    internal static bool IsDeadScreenActive = false;

    private static bool Prefix()
    {
        if (IsDeadScreenActive)
        {
            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Warn("[CoopDeath] DeadScreenWidget.OnDead skipped: screen already active.", Main.CoopLocal);
            }
            return false;
        }
        IsDeadScreenActive = true;
        return true;
    }
}
