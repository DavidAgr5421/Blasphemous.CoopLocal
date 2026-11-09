using System;
using System.Collections.Generic;
using System.IO;
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
    internal static int SuppressVanillaSaveSlotsFrames = 0;

    private static bool Prefix(SelectSaveSlots __instance, int idxSlot)
    {
        if (BypassIntercept) return true;
        if (CoopGameSetupUI.IsOpen || CoopJoinLobbyUI.IsOpen || SuppressVanillaSaveSlotsFrames > 0) return false;
        if (!__instance.IsShowing) return true;

        var slotsField = AccessTools.Field(typeof(SelectSaveSlots), "slots");
        var slots = slotsField?.GetValue(__instance) as List<SaveSlot>;
        if (slots != null && idxSlot >= 0 && idxSlot < slots.Count)
        {
            SaveSlot targetSlot = slots[idxSlot];
            int capturedSlot = idxSlot;
            SelectSaveSlots instance = __instance;

            // Si la partida ya está iniciada (slot no vacío):
            // NO pedir configuración ni mostrar menús. Entrar DIRECTAMENTE con la config guardada de este slot.
            if (!targetSlot.IsEmpty)
            {
                CoopSaveManager.TargetSetupSlot = idxSlot;
                var slotData = CoopSaveManager.GetSlotData(idxSlot);
                CoopSaveManager.ApplyConfigToGame(slotData);

                // Si no tiene mandos configurados, asignar P1 Mando 1 y P2 Teclado por defecto
                if (CoopGameConfig.Slots[0].Device == CoopDeviceType.Unassigned)
                    CoopGameConfig.Slots[0].Device = CoopDeviceType.Gamepad1;
                if (CoopGameConfig.Slots[1].Device == CoopDeviceType.Unassigned)
                    CoopGameConfig.Slots[1].Device = CoopDeviceType.Keyboard;
                CoopGameConfig.ApplyToRegistry();

                int activePlayers = Mathf.Clamp(CoopGameConfig.PlayerCount, 2, 4);
                if (Combat.CoopEnemyScaling.ActiveEnemyMultiplier <= 1.01f && Combat.CoopEnemyScaling.ActiveHealthMultiplier <= 1.01f)
                {
                    Combat.CoopEnemyScaling.SetDefaultsForPlayerCount(activePlayers);
                }

                return true;
            }

            // Si el slot está vacío (NUEVA PARTIDA) -> Mostrar CoopGameSetupUI para configurar desde cero
            CoopSaveManager.TargetSetupSlot = capturedSlot;
            CoopGameSetupUI.Show(
                onConfirm: () =>
                {
                    BypassIntercept = true;
                    Player2SlotResetHelper.ResetAllSlotData(capturedSlot);

                    // Guardar la configuración seleccionada en el archivo unificado de la ranura
                    var slotData = CoopSaveManager.GetSlotData(capturedSlot);
                    CoopSaveManager.SyncConfigFromGame(slotData);
                    slotData.HasCompletedSetup = true;
                    CoopSaveManager.SaveSlot(capturedSlot);

                    instance.OnAcceptSlots(capturedSlot);
                    BypassIntercept = false;
                },
                onCancel: () =>
                {
                    SuppressVanillaSaveSlotsFrames = 4;
                    try
                    {
                        var isShowingProp = AccessTools.Property(typeof(SelectSaveSlots), "IsShowing");
                        isShowingProp?.SetValue(instance, true, null);
                    }
                    catch { }

                    try
                    {
                        var parentField = AccessTools.Field(typeof(SelectSaveSlots), "parentMainMenu");
                        var parentMenu = parentField?.GetValue(instance) as NewMainMenu;
                        var modeField = AccessTools.Field(typeof(SelectSaveSlots), "CurrentSlotsMode");
                        var mode = modeField != null ? (SelectSaveSlots.SlotsModes)modeField.GetValue(instance) : 0;
                        if (parentMenu != null)
                        {
                            instance.SetAllData(parentMenu, mode);
                        }
                    }
                    catch { }
                    instance.OnSelectedSlots(capturedSlot);
                }
            );
            return false;
        }
        return true;
    }
}

/// <summary>
/// Bloquea el Update vanilla de SelectSaveSlots mientras la interfaz de configuración cooperativa está abierta,
/// evitando que inputs de fondo (Escape, Ataque, etc.) borren partidas o cierren el menú.
/// </summary>
[HarmonyPatch(typeof(SelectSaveSlots), "Update")]
internal static class SelectSaveSlots_BlockUpdate_WhenCoopSetupOpen_Patch
{
    private static bool Prefix()
    {
        if (CoopGameSetupUI.IsOpen)
        {
            return false;
        }

        if (SelectSaveSlots_OnAcceptSlots_CoopSetup_Patch.SuppressVanillaSaveSlotsFrames > 0)
        {
            SelectSaveSlots_OnAcceptSlots_CoopSetup_Patch.SuppressVanillaSaveSlotsFrames--;
            return false;
        }

        return true;
    }
}

/// <summary>
/// Carga la configuración del slot correspondiente cada vez que el usuario resalta una ranura en el menú.
/// </summary>
[HarmonyPatch(typeof(SelectSaveSlots), nameof(SelectSaveSlots.OnSelectedSlots))]
internal static class SelectSaveSlots_OnSelectedSlots_LoadConfig_Patch
{
    private static bool Prefix()
    {
        if (CoopGameSetupUI.IsOpen || SelectSaveSlots_OnAcceptSlots_CoopSetup_Patch.SuppressVanillaSaveSlotsFrames > 0)
        {
            return false;
        }
        return true;
    }

    private static void Postfix(int idxSlot)
    {
        if (CoopGameSetupUI.IsOpen || SelectSaveSlots_OnAcceptSlots_CoopSetup_Patch.SuppressVanillaSaveSlotsFrames > 0)
        {
            return;
        }

        if (idxSlot >= 0)
        {
            CoopSaveManager.TargetSetupSlot = idxSlot;
            var slotData = CoopSaveManager.GetSlotData(idxSlot);
            CoopSaveManager.ApplyConfigToGame(slotData);
        }
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
        if (!__instance.IsShowing || __instance.IsConfirming || CoopGameSetupUI.IsOpen || CoopJoinLobbyUI.IsOpen) return;

        // Tecla J: Unirse a Partida Online (Join Lobby)
        if (Input.GetKeyDown(KeyCode.J))
        {
            CoopJoinLobbyUI.Show(
                onJoined: () =>
                {
                    SelectSaveSlots_OnAcceptSlots_CoopSetup_Patch.SuppressVanillaSaveSlotsFrames = 5;
                    if (Main.CoopLocal != null)
                    {
                        Blasphemous.ModdingAPI.ModLog.Info("[JoinUI] Jugador unido a sala online. Esperando sesión...", Main.CoopLocal);
                    }
                },
                onBack: () =>
                {
                    SelectSaveSlots_OnAcceptSlots_CoopSetup_Patch.SuppressVanillaSaveSlotsFrames = 3;
                }
            );
            return;
        }

        // Tecla C / F4: Configurar partida Coop (Host)
        if (Input.GetKeyDown(KeyCode.C) || Input.GetKeyDown(KeyCode.F4))
        {
            int curSlot = __instance.SelectedSlot;
            CoopSaveManager.TargetSetupSlot = curSlot;
            if (curSlot >= 0)
            {
                var slotData = CoopSaveManager.GetSlotData(curSlot);
                CoopSaveManager.ApplyConfigToGame(slotData);
            }

            CoopGameSetupUI.Show(
                onConfirm: () =>
                {
                    if (curSlot >= 0)
                    {
                        var slotData = CoopSaveManager.GetSlotData(curSlot);
                        CoopSaveManager.SyncConfigFromGame(slotData);
                        slotData.HasCompletedSetup = true;
                        CoopSaveManager.SaveSlot(curSlot);
                    }

                    // Si algún jugador está configurado como RemoteSteam, crear el lobby de Steam como Host
                    if (CoopGameConfig.Slots[1].Device == CoopDeviceType.RemoteSteam ||
                        CoopGameConfig.Slots[2].Device == CoopDeviceType.RemoteSteam ||
                        CoopGameConfig.Slots[3].Device == CoopDeviceType.RemoteSteam)
                    {
                        CoopLobbyManager.Instance.CreateHostLobby((success, code) =>
                        {
                            if (success && Main.CoopLocal != null)
                            {
                                Blasphemous.ModdingAPI.ModLog.Info($"[Host] Sala Steam creada con código: {code}", Main.CoopLocal);
                            }
                        });
                    }

                    SelectSaveSlots_OnAcceptSlots_CoopSetup_Patch.SuppressVanillaSaveSlotsFrames = 3;
                    if (curSlot >= 0) __instance.OnSelectedSlots(curSlot);
                },
                onCancel: () =>
                {
                    SelectSaveSlots_OnAcceptSlots_CoopSetup_Patch.SuppressVanillaSaveSlotsFrames = 3;
                    if (curSlot >= 0) __instance.OnSelectedSlots(curSlot);
                }
            );
        }
    }
}

/// <summary>
/// Muestra un banner explicativo en la parte inferior de la pantalla de selección de slots
/// para que el usuario conozca los atajos para crear o unirse a partidas.
/// </summary>
[HarmonyPatch(typeof(SelectSaveSlots), "OnGUI")]
internal static class SelectSaveSlots_OnGUI_CoopHint_Patch
{
    private static GUIStyle hintStyle;

    private static void Postfix(SelectSaveSlots __instance)
    {
        if (!__instance.IsShowing || CoopGameSetupUI.IsOpen || CoopJoinLobbyUI.IsOpen) return;

        if (hintStyle == null)
        {
            hintStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16,
                fontStyle = FontStyle.Bold
            };
            hintStyle.normal.textColor = new Color(1f, 0.88f, 0.45f, 0.95f);
        }

        string hint = "[C / F4] Crear/Configurar Partida Co-op   |   [J] Unirse a Partida Online (Join)";
        float w = 720f;
        float h = 32f;
        float x = (Screen.width - w) * 0.5f;
        float y = Screen.height - 45f;

        GUI.color = new Color(0.04f, 0.04f, 0.06f, 0.78f);
        GUI.DrawTexture(new Rect(x - 12f, y - 4f, w + 24f, h + 8f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(x, y, w, h), hint, hintStyle);
    }
}

/// <summary>
/// Limpieza y reset de datos de P2 y estados del mod cuando se borra un slot o se inicia una nueva partida.
/// </summary>
internal static class Player2SlotResetHelper
{
    internal static void ResetAllSlotData(int slot)
    {
        try
        {
            CoopSaveManager.ResetSlot(slot);
            SwordElementalUpgrades.ResetElementalPath(false);
            SwordElementalUpgrades.ResetElementalPath(true);

            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info($"[CoopLocal] Datos de slot {slot} reseteados limpiamente desde cero.", Main.CoopLocal);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CoopLocal] Error reseteando slot {slot}: {ex.Message}");
        }
    }
}

/// <summary>
/// Al borrar una partida en el menú vanilla, borrar también todos los archivos asociados de P2 y del mod.
/// </summary>
[HarmonyPatch(typeof(SelectSaveSlots), nameof(SelectSaveSlots.OnConfirmationDelete))]
internal static class SelectSaveSlots_OnConfirmationDelete_CoopCleanup_Patch
{
    private static void Prefix(SelectSaveSlots __instance)
    {
        if (!__instance.IsConfirmingUpgrade)
        {
            int slot = __instance.SelectedSlot;
            Player2SlotResetHelper.ResetAllSlotData(slot);
        }
    }
}
