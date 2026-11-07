using Framework.Managers;
using System;
using UnityEngine;

namespace Blasphemous.CoopLocal;

/// <summary>
/// Estado de muerte de P1: cuando P1 muere pero P2/3/4 sigue vivo, P1 queda en estado
/// "muerto pendiente de revivir" hasta que un jugador vivo alcance un Prie-Dieu o Altar.
/// Solo si todos los jugadores mueren se lanza el Game Over global y la pantalla de muerte.
/// Persistido centralmente en coop_savedata_slot{slot}.txt.
/// </summary>
internal static class Player1DeathState
{
    internal static bool IsPendingRevive()
    {
        return CoopSaveManager.CurrentSave.GetPlayer(0).PendingRevive;
    }

    internal static void MarkDeadPendingRevive()
    {
        var p0 = CoopSaveManager.CurrentSave.GetPlayer(0);
        p0.PendingRevive = true;
        CoopSaveManager.SaveCurrentSlot();
        if (Main.CoopLocal != null)
            Blasphemous.ModdingAPI.ModLog.Info($"[P1DeathState] P1 marcado como PendingRevive=true en slot {CoopSaveManager.CurrentSlot}", Main.CoopLocal);
    }

    internal static void ClearPendingRevive()
    {
        var p0 = CoopSaveManager.CurrentSave.GetPlayer(0);
        p0.PendingRevive = false;
        CoopSaveManager.SaveCurrentSlot();
        if (Main.CoopLocal != null)
            Blasphemous.ModdingAPI.ModLog.Info($"[P1DeathState] P1 PendingRevive limpiado en slot {CoopSaveManager.CurrentSlot}", Main.CoopLocal);
    }
}
