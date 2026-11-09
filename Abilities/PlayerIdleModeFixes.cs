using System;
using System.Reflection;
using Framework.FrameworkCore;
using Framework.Managers;
using Gameplay.GameControllers.Penitent;
using Gameplay.GameControllers.Penitent.Abilities;
using Gameplay.GameControllers.Penitent.InputSystem;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.CoopLocal;

/// <summary>
/// Helper para manipular de forma segura PlayerIdleMode en Penitent clones y P1.
/// </summary>
internal static class PlayerIdleModeHelper
{
    internal static readonly FieldInfo PlayerInputField = AccessTools.Field(typeof(PlayerIdleMode), "_playerInput");
    internal static readonly FieldInfo CurrentPlayerModeField = AccessTools.Field(typeof(PlayerIdleMode), "_currentPlayerMode");
    internal static readonly FieldInfo CurrentIdleTimeField = AccessTools.Field(typeof(PlayerIdleMode), "_currentIdleTime");
    internal static readonly MethodInfo ResetIdleTimerMethod = AccessTools.Method(typeof(PlayerIdleMode), "ResetIdleTimer");
    internal static readonly MethodInfo SetToPlayModeMethod = AccessTools.Method(typeof(PlayerIdleMode), "SetToPlayMode");

    internal static Penitent GetOwner(PlayerIdleMode instance)
    {
        if (instance == null) return null;
        Penitent owner = instance.EntityOwner as Penitent;
        if (owner == null)
        {
            owner = instance.GetComponentInParent<Penitent>();
        }
        return owner;
    }

    internal static void ForceSetPlayMode(PlayerIdleMode idleMode, Penitent owner)
    {
        if (idleMode == null || owner == null) return;

        try
        {
            CurrentIdleTimeField?.SetValue(idleMode, 0f);
            CurrentPlayerModeField?.SetValue(idleMode, 0); // PlayerMode.Play = 0
            if (owner.Animator != null)
            {
                owner.Animator.SetBool("IS_IDLE_MODE", false);
                owner.Animator.Play("Idle");
            }
            if (owner.Audio != null)
            {
                owner.Audio.StopIdleModeBlood();
            }
            ResetIdleTimerMethod?.Invoke(idleMode, null);
        }
        catch (Exception ex)
        {
            DashParryDebugLog.Log($"[PlayerIdleMode] Error in ForceSetPlayMode: {ex.Message}");
        }
    }
}

/// <summary>
/// Ronda 91: Corrige PlayerIdleMode.OnStart para clones de P2-P4.
/// En el código original de Blasphemous, `_playerInput = Core.Logic.Penitent.PlatformCharacterInput;`
/// está cableado de forma rígida a P1, dejando a P2-P4 con la referencia al input de P1.
/// </summary>
[HarmonyPatch(typeof(PlayerIdleMode), "OnStart")]
internal static class PlayerIdleMode_OnStart_Patch
{
    private static void Postfix(PlayerIdleMode __instance)
    {
        Penitent owner = PlayerIdleModeHelper.GetOwner(__instance);
        if (owner != null && owner.PlatformCharacterInput != null)
        {
            PlayerIdleModeHelper.PlayerInputField?.SetValue(__instance, owner.PlatformCharacterInput);
        }
    }
}

/// <summary>
/// Ronda 91: Corrige el chequeo de entrada InputReceived en PlayerIdleMode.
/// En el juego base, comprueba `_playerInput.Rewired.GetAnyButton() || flag || flag2`,
/// lo cual siempre apunta al gamepad de P1. Para P2-P4 nunca detectaba entrada y por tanto
/// nunca salía de IdleMode por control propio.
/// </summary>
[HarmonyPatch(typeof(PlayerIdleMode), "InputReceived", MethodType.Getter)]
internal static class PlayerIdleMode_InputReceived_Patch
{
    private static bool Prefix(PlayerIdleMode __instance, ref bool __result)
    {
        Penitent owner = PlayerIdleModeHelper.GetOwner(__instance);
        if (owner == null) return true;

        if (CoopLocal.IsGuestPlayer(owner, out int guestSlot))
        {
            __result = Player2Input.HasAnyInput(guestSlot);
            return false;
        }

        // Para P1 (ranura 0), también verificamos si hay input mapeado
        if (Player2Input.HasAnyInput(0))
        {
            __result = true;
            return false;
        }

        return true;
    }
}

/// <summary>
/// Ronda 91: Corrige la transición a PlayMode.
/// 1. Detiene el audio en owner.Audio (en vez de Core.Logic.Penitent.Audio).
/// 2. Fuerza Animator.Play("Idle") instantáneo para cancelar cualquier tiempo de salida
///    de la animación de IdleMode, permitiendo atacar o moverse en el mismo frame exacto.
/// </summary>
[HarmonyPatch(typeof(PlayerIdleMode), "SetToPlayMode")]
internal static class PlayerIdleMode_SetToPlayMode_Patch
{
    private static bool Prefix(PlayerIdleMode __instance, Action callBack)
    {
        Penitent owner = PlayerIdleModeHelper.GetOwner(__instance);
        if (owner == null) return true;

        PlayerIdleModeHelper.CurrentPlayerModeField?.SetValue(__instance, 0); // PlayerMode.Play = 0
        if (owner.Animator != null)
        {
            owner.Animator.SetBool("IS_IDLE_MODE", false);
            owner.Animator.Play("Idle");
        }
        if (owner.Audio != null)
        {
            owner.Audio.StopIdleModeBlood();
        }
        PlayerIdleModeHelper.ResetIdleTimerMethod?.Invoke(__instance, null);
        callBack?.Invoke();
        return false;
    }
}

/// <summary>
/// Ronda 91: Corrige SetToIdleMode.
/// 1. Si el jugador tiene algún input activo, se cancela la entrada a IdleMode.
/// 2. Utiliza owner.Audio para reproducir el sonido en vez del audio exclusivo de P1.
/// </summary>
[HarmonyPatch(typeof(PlayerIdleMode), "SetToIdleMode")]
internal static class PlayerIdleMode_SetToIdleMode_Patch
{
    private static bool Prefix(PlayerIdleMode __instance, Action callBack)
    {
        Penitent owner = PlayerIdleModeHelper.GetOwner(__instance);
        if (owner == null) return true;

        int slotIndex = CoopLocal.IsGuestPlayer(owner, out int s) ? s : 0;
        if (Player2Input.HasAnyInput(slotIndex))
        {
            PlayerIdleModeHelper.CurrentIdleTimeField?.SetValue(__instance, 0f);
            return false;
        }

        PlayerIdleModeHelper.CurrentPlayerModeField?.SetValue(__instance, 1); // PlayerMode.Idle = 1
        if (owner.Animator != null)
        {
            owner.Animator.SetBool("IS_IDLE_MODE", true);
        }
        if (owner.Audio != null)
        {
            owner.Audio.PlayIdleModeBlood();
        }
        callBack?.Invoke();
        return false;
    }
}

/// <summary>
/// Ronda 91: En OnUpdate de PlayerIdleMode, si el jugador está enviando cualquier input,
/// reinicia el temporizador de inactividad de inmediato, y si ya estaba en IdleMode,
/// despierta instantáneamente a PlayMode.
/// </summary>
[HarmonyPatch(typeof(PlayerIdleMode), "OnUpdate")]
internal static class PlayerIdleMode_OnUpdate_Patch
{
    private static void Prefix(PlayerIdleMode __instance)
    {
        Penitent owner = PlayerIdleModeHelper.GetOwner(__instance);
        if (owner == null) return;

        int slotIndex = CoopLocal.IsGuestPlayer(owner, out int s) ? s : 0;
        bool hasInput = Player2Input.HasAnyInput(slotIndex);

        if (hasInput)
        {
            PlayerIdleModeHelper.CurrentIdleTimeField?.SetValue(__instance, 0f);

            int currentMode = (int)(PlayerIdleModeHelper.CurrentPlayerModeField?.GetValue(__instance) ?? 0);
            if (currentMode == 1) // PlayerMode.Idle = 1
            {
                PlayerIdleModeHelper.SetToPlayModeMethod?.Invoke(__instance, new object[] { null });
            }
        }
    }
}
