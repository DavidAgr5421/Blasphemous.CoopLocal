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
                        // Suprimir los próximos frames para que la tecla ESC no cierre el menú de slots original
                        SuppressVanillaSaveSlotsFrames = 4;
                        instance.IsShowing = true;
                        // Restaurar selección visual en la lista de slots
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
                return false; // Interceptamos para mostrar la pantalla de configuración antes de crear la partida
            }
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
        if (CoopGameSetupUI.Instance != null && CoopGameSetupUI.Instance.IsOpen)
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
/// Sincronización automática de partidas guardadas entre perfiles de Steam.
/// Si Steam cambia de cuenta o se inicia en un perfil sin partidas existentes,
/// busca automáticamente en las otras carpetas de Savegames y restaura las partidas
/// para que el usuario nunca pierda el acceso a su progreso.
/// </summary>
[HarmonyPatch(typeof(Framework.Managers.PersistentManager), nameof(Framework.Managers.PersistentManager.GetSaveGameFile))]
internal static class PersistentManager_GetSaveGameFile_MultiProfileSync_Patch
{
    private static void Postfix(int slot, ref string __result)
    {
        try
        {
            if (string.IsNullOrEmpty(__result)) return;

            if (!File.Exists(__result))
            {
                // El archivo no existe en el SteamID activo.
                string activeDir = Path.GetDirectoryName(__result);
                string savegamesRoot = Path.GetDirectoryName(activeDir);

                if (Directory.Exists(savegamesRoot))
                {
                    string targetFileName = Path.GetFileName(__result);
                    string[] profileDirs = Directory.GetDirectories(savegamesRoot);

                    foreach (string dir in profileDirs)
                    {
                        if (string.Equals(dir, activeDir, StringComparison.OrdinalIgnoreCase))
                            continue;

                        string candidateFile = Path.Combine(dir, targetFileName);
                        if (File.Exists(candidateFile) && new FileInfo(candidateFile).Length > 0)
                        {
                            if (!Directory.Exists(activeDir))
                            {
                                Directory.CreateDirectory(activeDir);
                            }

                            File.Copy(candidateFile, __result, true);
                            Debug.LogWarning($"[CoopLocal] Partida restaurada desde perfil alternativo: {candidateFile} -> {__result}");

                            // Copiar también el archivo de backup si existe
                            string backupCand = candidateFile + "_backup";
                            if (File.Exists(backupCand))
                            {
                                File.Copy(backupCand, __result + "_backup", true);
                            }

                            // Copiar subdirectorio de datos adicionales si existe
                            string subDirCand = Path.Combine(dir, Path.GetFileNameWithoutExtension(targetFileName));
                            if (Directory.Exists(subDirCand))
                            {
                                string destSub = Path.Combine(activeDir, Path.GetFileNameWithoutExtension(targetFileName));
                                CopyDirectoryRecursive(subDirCand, destSub);
                            }
                            break;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CoopLocal] Error sincronizando partidas guardadas: {ex.Message}");
        }
    }

    private static void CopyDirectoryRecursive(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), true);
        }
        foreach (string directory in Directory.GetDirectories(sourceDir))
        {
            CopyDirectoryRecursive(directory, Path.Combine(targetDir, Path.GetFileName(directory)));
        }
    }
}
