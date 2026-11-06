using System;
using System.Collections.Generic;
using Gameplay.UI.Others.MenuLogic;
using Gameplay.UI.Widgets;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.CoopLocal.MultiPlayer;

/// <summary>
/// Parches de integración para mostrar la pantalla de configuración cooperativa
/// antes de crear una nueva partida en los slots de guardado, o mediante el atajo 'C' en el menú.
/// </summary>
[HarmonyPatch(typeof(SelectSaveSlots), nameof(SelectSaveSlots.OnAcceptSlots))]
internal static class SelectSaveSlots_OnAcceptSlots_CoopSetup_Patch
{
    internal static bool BypassIntercept;

    private static bool Prefix(SelectSaveSlots __instance, int idxSlot)
    {
        if (BypassIntercept) return true;
        if (!__instance.IsShowing) return true;

        var slotsField = AccessTools.Field(typeof(SelectSaveSlots), "slots");
        var slots = slotsField?.GetValue(__instance) as List<SaveSlot>;
        if (slots != null && idxSlot >= 0 && idxSlot < slots.Count)
        {
            SaveSlot targetSlot = slots[idxSlot];
            // Si el slot está vacío -> Es una NUEVA PARTIDA
            if (targetSlot.IsEmpty)
            {
                int capturedSlot = idxSlot;
                SelectSaveSlots instance = __instance;

                CoopGameSetupUI.Show(
                    onConfirm: () =>
                    {
                        BypassIntercept = true;
                        SwordElementalUpgrades.ResetElementalPath(false);
                        SwordElementalUpgrades.ResetElementalPath(true);
                        instance.OnAcceptSlots(capturedSlot);
                        BypassIntercept = false;
                    },
                    onCancel: () =>
                    {
                        // Restaurar selección visual en la lista de slots
                        instance.OnSelectedSlots(capturedSlot);
                    }
                );
                return false; // Interceptamos para mostrar la pantalla de configuración antes de crear la partida
            }
        }
        return true;
    }
}

/// <summary>
/// Permite pulsar la tecla 'C' o F4 en la pantalla de selección de slots para abrir
/// la configuración de cooperativo en cualquier momento.
/// </summary>
[HarmonyPatch(typeof(SelectSaveSlots), "Update")]
internal static class SelectSaveSlots_Update_CoopSetup_Shortcut_Patch
{
    private static void Postfix(SelectSaveSlots __instance)
    {
        if (!__instance.IsShowing || __instance.IsConfirming || CoopGameSetupUI.Instance.IsOpen) return;

        if (Input.GetKeyDown(KeyCode.C) || Input.GetKeyDown(KeyCode.F4))
        {
            int curSlot = __instance.SelectedSlot;
            CoopGameSetupUI.Show(
                onConfirm: () =>
                {
                    if (curSlot >= 0) __instance.OnSelectedSlots(curSlot);
                },
                onCancel: () =>
                {
                    if (curSlot >= 0) __instance.OnSelectedSlots(curSlot);
                }
            );
        }
    }
}
