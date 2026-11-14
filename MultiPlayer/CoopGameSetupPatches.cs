using System;
using System.Collections.Generic;
using System.IO;
using Gameplay.UI.Others.MenuLogic;
using Gameplay.UI.Others.Buttons;
using Gameplay.UI.Widgets;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.CoopLocal.MultiPlayer;

/// <summary>
/// Provee control centralizado de los modales cooperativos en la pantalla de ranuras de guardado.
/// </summary>
internal static class CoopGameSetupPatches
{
    public static bool IsAnyCoopModalOpen =>
        CoopGameSetupUI.IsOpen || CoopJoinLobbyUI.IsOpen || CoopMatchConfigUI.IsOpen;

    internal static void RestoreSaveSlotsMenu(SelectSaveSlots instance, int capturedSlot)
    {
        SelectSaveSlots_OnAcceptSlots_CoopSetup_Patch.SuppressVanillaSaveSlotsFrames = 5;
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
}

/// <summary>
/// Parches de integración para mostrar la pantalla de configuración cooperativa
/// antes de crear una nueva partida (pre-match setup) o de configurar una partida ya existente.
/// </summary>
[HarmonyPatch(typeof(SelectSaveSlots), nameof(SelectSaveSlots.OnAcceptSlots))]
internal static class SelectSaveSlots_OnAcceptSlots_CoopSetup_Patch
{
    internal static bool BypassIntercept;
    internal static int SuppressVanillaSaveSlotsFrames = 0;

    private static bool Prefix(SelectSaveSlots __instance, int idxSlot)
    {
        if (BypassIntercept) return true;
        if (CoopGameSetupPatches.IsAnyCoopModalOpen || SuppressVanillaSaveSlotsFrames > 0) return false;
        if (!__instance.IsShowing) return true;

        var slotsField = AccessTools.Field(typeof(SelectSaveSlots), "slots");
        var slots = slotsField?.GetValue(__instance) as List<SaveSlot>;
        if (slots != null && idxSlot >= 0 && idxSlot < slots.Count)
        {
            SaveSlot targetSlot = slots[idxSlot];
            int capturedSlot = idxSlot;
            SelectSaveSlots instance = __instance;
            bool isNewGame = targetSlot.IsEmpty;

            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info($"[CoopGameSetupPatches] Interceptando OnAcceptSlots: Slot={capturedSlot}, isNewGame={isNewGame}", Main.CoopLocal);
            }

            CoopSaveManager.TargetSetupSlot = capturedSlot;
            var slotData = CoopSaveManager.GetSlotData(capturedSlot);
            CoopSaveManager.ApplyConfigToGame(slotData);

            // Si es un slot sin mandos asignados, asignar por defecto
            if (CoopGameConfig.Slots[0].Device == CoopDeviceType.Unassigned)
                CoopGameConfig.Slots[0].Device = CoopDeviceType.Gamepad1;
            if (CoopGameConfig.Slots[1].Device == CoopDeviceType.Unassigned)
                CoopGameConfig.Slots[1].Device = CoopDeviceType.Keyboard;
            CoopGameConfig.ApplyToRegistry();

            if (isNewGame)
            {
                // NUEVA PARTIDA -> Menú pre-partida inicial (dificultad, número inicial de jugadores, senda elemental)
                CoopGameSetupUI.Show(
                    onConfirm: () =>
                    {
                        BypassIntercept = true;

                        // Reiniciar progreso previo del slot
                        Player2SlotResetHelper.ResetAllSlotData(capturedSlot);

                        var currentSlotData = CoopSaveManager.GetSlotData(capturedSlot);
                        CoopSaveManager.SyncConfigFromGame(currentSlotData);
                        currentSlotData.HasCompletedSetup = true;
                        CoopSaveManager.SaveSlot(capturedSlot);

                        // Si algún jugador está configurado como RemoteSteam, crear el lobby de Steam como Host
                        if (CoopGameConfig.IsCoopEnabled && (
                            CoopGameConfig.Slots[1].Device == CoopDeviceType.RemoteSteam ||
                            CoopGameConfig.Slots[2].Device == CoopDeviceType.RemoteSteam ||
                            CoopGameConfig.Slots[3].Device == CoopDeviceType.RemoteSteam))
                        {
                            CoopLobbyManager.Instance.CreateHostLobby((success, code) =>
                            {
                                if (success && Main.CoopLocal != null)
                                {
                                    Blasphemous.ModdingAPI.ModLog.Info($"[Host] Sala Steam creada para slot {capturedSlot} con código: {code}", Main.CoopLocal);
                                }
                            });
                        }

                        instance.OnAcceptSlots(capturedSlot);
                        BypassIntercept = false;
                    },
                    onCancel: () =>
                    {
                        CoopGameSetupPatches.RestoreSaveSlotsMenu(instance, capturedSlot);
                    }
                );
            }
            else
            {
                // PARTIDA EXISTENTE -> Menú de configuración de partida ya creada
                // (añadir/eliminar jugadores, alternar local/online con confirmación, verificar código de sala)
                CoopMatchConfigUI.Show(
                    slotIndex: capturedSlot,
                    slot: targetSlot,
                    onPlay: () =>
                    {
                        BypassIntercept = true;

                        var currentSlotData = CoopSaveManager.GetSlotData(capturedSlot);
                        CoopSaveManager.SyncConfigFromGame(currentSlotData);
                        currentSlotData.HasCompletedSetup = true;
                        CoopSaveManager.SaveSlot(capturedSlot);

                        if (CoopGameConfig.IsCoopEnabled && (
                            CoopGameConfig.Slots[1].Device == CoopDeviceType.RemoteSteam ||
                            CoopGameConfig.Slots[2].Device == CoopDeviceType.RemoteSteam ||
                            CoopGameConfig.Slots[3].Device == CoopDeviceType.RemoteSteam))
                        {
                            if (CoopLobbyManager.State != LobbyConnectionState.Hosting)
                            {
                                CoopLobbyManager.Instance.CreateHostLobby((success, code) =>
                                {
                                    if (success && Main.CoopLocal != null)
                                    {
                                        Blasphemous.ModdingAPI.ModLog.Info($"[Host] Sala Steam creada para slot {capturedSlot} con código: {code}", Main.CoopLocal);
                                    }
                                });
                            }
                        }

                        instance.OnAcceptSlots(capturedSlot);
                        BypassIntercept = false;
                    },
                    onCancel: () =>
                    {
                        CoopGameSetupPatches.RestoreSaveSlotsMenu(instance, capturedSlot);
                    }
                );
            }
            return false;
        }
        return true;
    }
}

/// <summary>
/// Bloquea el Update vanilla de SelectSaveSlots mientras cualquier interfaz cooperativa está abierta,
/// evitando que inputs de fondo (WASD, Flechas, Escape, etc.) interactúen con el menú de ranuras.
/// </summary>
[HarmonyPatch(typeof(SelectSaveSlots), "Update")]
internal static class SelectSaveSlots_BlockUpdate_WhenCoopSetupOpen_Patch
{
    private static bool Prefix(SelectSaveSlots __instance)
    {
        if (__instance != null && __instance.GetComponent<CoopSaveSlotHintGUI>() == null)
        {
            __instance.gameObject.AddComponent<CoopSaveSlotHintGUI>();
        }

        if (CoopGameSetupPatches.IsAnyCoopModalOpen)
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
/// Carga la configuración del slot correspondiente cada vez que el usuario resalta una ranura en el menú,
/// bloqueándose mientras cualquier modal esté abierto.
/// </summary>
[HarmonyPatch(typeof(SelectSaveSlots), nameof(SelectSaveSlots.OnSelectedSlots))]
internal static class SelectSaveSlots_OnSelectedSlots_LoadConfig_Patch
{
    private static bool Prefix()
    {
        if (CoopGameSetupPatches.IsAnyCoopModalOpen || SelectSaveSlots_OnAcceptSlots_CoopSetup_Patch.SuppressVanillaSaveSlotsFrames > 0)
        {
            return false;
        }
        return true;
    }

    private static void Postfix(int idxSlot)
    {
        if (CoopGameSetupPatches.IsAnyCoopModalOpen || SelectSaveSlots_OnAcceptSlots_CoopSetup_Patch.SuppressVanillaSaveSlotsFrames > 0)
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
/// Permite pulsar la tecla 'U' (o 'J' / botón Y de mando) en la pantalla de selección de slots
/// para abrir la interfaz modal de unirse a una partida online.
/// </summary>
[HarmonyPatch(typeof(SelectSaveSlots), "Update")]
internal static class SelectSaveSlots_Update_CoopSetup_Shortcut_Patch
{
    private static void Postfix(SelectSaveSlots __instance)
    {
        if (!__instance.IsShowing || __instance.IsConfirming || CoopGameSetupPatches.IsAnyCoopModalOpen) return;

        // Tecla U o J (o botón Y/Triángulo en mando): Unirse a Partida Online (Join Lobby)
        if (Input.GetKeyDown(KeyCode.U) || Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.JoystickButton3))
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
                    SelectSaveSlots_OnAcceptSlots_CoopSetup_Patch.SuppressVanillaSaveSlotsFrames = 5;
                }
            );
        }
    }
}

/// <summary>
/// Bloquea la navegación, foco y selección de los botones vanilla de ranura (EventsButton)
/// mientras cualquier modal cooperativo está abierto, garantizando que WASD no mueva el selector de ranuras de fondo.
/// </summary>
[HarmonyPatch(typeof(EventsButton), nameof(EventsButton.OnSelect))]
internal static class EventsButton_OnSelect_BlockWhenCoopModalOpen_Patch
{
    private static bool Prefix()
    {
        if (CoopGameSetupPatches.IsAnyCoopModalOpen)
        {
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(EventsButton), nameof(EventsButton.OnSubmit))]
internal static class EventsButton_OnSubmit_BlockWhenCoopModalOpen_Patch
{
    private static bool Prefix()
    {
        if (CoopGameSetupPatches.IsAnyCoopModalOpen)
        {
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(EventsButton), nameof(EventsButton.OnPointerClick))]
internal static class EventsButton_OnPointerClick_BlockWhenCoopModalOpen_Patch
{
    private static bool Prefix()
    {
        if (CoopGameSetupPatches.IsAnyCoopModalOpen)
        {
            return false;
        }
        return true;
    }
}

/// <summary>
/// Componente auxiliar para mostrar el botón de Unirse a Partida Online con la tipografía auténtica del juego
/// en la pantalla de selección de ranuras de guardado.
/// </summary>
internal class CoopSaveSlotHintGUI : MonoBehaviour
{
    private SelectSaveSlots slotsMenu;
    private GUIStyle buttonStyle;

    private void Awake()
    {
        slotsMenu = GetComponent<SelectSaveSlots>();
    }

    private void OnGUI()
    {
        if (slotsMenu == null || !slotsMenu.IsShowing || CoopGameSetupPatches.IsAnyCoopModalOpen) return;

        Font gameFont = CoopGameSetupUI.GetGameFont();

        if (buttonStyle == null || buttonStyle.font != gameFont)
        {
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                font = gameFont
            };
            buttonStyle.normal.textColor = new Color(1f, 0.90f, 0.45f, 1f);
            buttonStyle.hover.textColor = new Color(1f, 1f, 0.75f, 1f);
        }

        string hint = "[ U ]  UNIRSE A PARTIDA ONLINE (JOIN)";
        float w = 540f;
        float h = 36f;
        float x = (Screen.width - w) * 0.5f;
        float y = Screen.height - 48f;

        // Fondo oscuro estilizado con marco dorado tenue
        GUI.color = new Color(0.04f, 0.04f, 0.06f, 0.88f);
        GUI.DrawTexture(new Rect(x - 8f, y - 4f, w + 16f, h + 8f), Texture2D.whiteTexture);
        GUI.color = new Color(0.65f, 0.52f, 0.25f, 0.85f);
        DrawBorder(new Rect(x - 8f, y - 4f, w + 16f, h + 8f), 2);
        GUI.color = Color.white;

        if (GUI.Button(new Rect(x, y, w, h), hint, buttonStyle))
        {
            CoopJoinLobbyUI.Show(
                onJoined: () =>
                {
                    SelectSaveSlots_OnAcceptSlots_CoopSetup_Patch.SuppressVanillaSaveSlotsFrames = 5;
                },
                onBack: () =>
                {
                    SelectSaveSlots_OnAcceptSlots_CoopSetup_Patch.SuppressVanillaSaveSlotsFrames = 5;
                }
            );
        }
    }

    private static void DrawBorder(Rect r, int thickness)
    {
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.y + r.height - thickness, r.width, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.y, thickness, r.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x + r.width - thickness, r.y, thickness, r.height), Texture2D.whiteTexture);
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
        if (__instance == null) return;

        try
        {
            int slotIdx = __instance.SelectedSlot;
            if (slotIdx >= 0)
            {
                Player2SlotResetHelper.ResetAllSlotData(slotIdx);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CoopLocal] Error en OnConfirmationDelete: {ex.Message}");
        }
    }
}
