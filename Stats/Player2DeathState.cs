using Framework.Managers;
using System;
using UnityEngine;

namespace Blasphemous.CoopLocal;

/// <summary>
/// Estado de muerte de jugadores invitados (P2, P3, P4): deben permanecer "muertos" sin reaparecer hasta el próximo Prie Dieu.
/// Persistido centralmente en coop_savedata_slot{slot}.txt.
/// </summary>
internal static class Player2DeathState
{
    internal static bool IsPendingRevive(int playerIndex = 1)
    {
        return CoopSaveManager.CurrentSave.GetPlayer(playerIndex).PendingRevive;
    }

    internal static void MarkDeadPendingRevive(int playerIndex = 1)
    {
        var p = CoopSaveManager.CurrentSave.GetPlayer(playerIndex);
        p.PendingRevive = true;
        CoopSaveManager.SaveCurrentSlot();
        if (Main.CoopLocal != null)
            Blasphemous.ModdingAPI.ModLog.Info($"[DeathState] P{playerIndex + 1} marcado como PendingRevive=true en slot {CoopSaveManager.CurrentSlot}", Main.CoopLocal);
    }

    internal static void ClearPendingRevive(int playerIndex = 1)
    {
        var p = CoopSaveManager.CurrentSave.GetPlayer(playerIndex);
        p.PendingRevive = false;
        CoopSaveManager.SaveCurrentSlot();
        if (Main.CoopLocal != null)
            Blasphemous.ModdingAPI.ModLog.Info($"[DeathState] P{playerIndex + 1} PendingRevive limpiado en slot {CoopSaveManager.CurrentSlot}", Main.CoopLocal);
    }
}
