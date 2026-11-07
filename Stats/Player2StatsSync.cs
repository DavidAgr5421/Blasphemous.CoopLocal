using Framework.FrameworkCore;
using Framework.Managers;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Blasphemous.CoopLocal;

// Sincronización y persistencia de Atributos/Stats de jugadores cooperativos (P2, P3, P4).
// Integrado de forma centralizada en CoopSaveManager y los archivos coop_savedata_slot{slot}.txt.
internal static class Player2StatsSync
{
    internal static void PersistPermanentBonus(Penitent guest, int slotIndex = -1)
    {
        if (guest == null) return;
        int targetSlot = slotIndex > 0 ? slotIndex : CoopPlayerRegistry.GetPlayerIndex(guest);
        if (targetSlot <= 0) targetSlot = 1;

        int saveSlot = PersistentManager.GetAutomaticSlot();
        if (saveSlot < 0) return;

        EntityStats.StatsTypes[] allTypes = (EntityStats.StatsTypes[])Enum.GetValues(typeof(EntityStats.StatsTypes));
        SaveSnapshot(saveSlot, guest, allTypes, targetSlot);

        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info(
                $"[StatsSync] persisted P{targetSlot + 1}'s PermanentBonus after upgrade (slot {saveSlot}).",
                Main.CoopLocal);
        }
    }

    internal static void EnsureSynced(Penitent p1, Penitent guest, int slotIndex = -1)
    {
        if (p1 == null || guest == null) return;
        int targetSlot = slotIndex > 0 ? slotIndex : CoopPlayerRegistry.GetPlayerIndex(guest);
        if (targetSlot <= 0) targetSlot = 1;

        int saveSlot = PersistentManager.GetAutomaticSlot();
        if (saveSlot < 0) return;

        var pData = CoopSaveManager.GetSlotData(saveSlot).GetPlayer(targetSlot);
        if (pData.HasStatsSnapshot)
        {
            ApplySnapshot(guest, pData);
            return;
        }

        guest.StartCoroutine(DelayedFirstSync(p1, guest, targetSlot));
    }

    private static System.Collections.IEnumerator DelayedFirstSync(Penitent p1, Penitent guest, int targetSlot)
    {
        for (int i = 0; i < 5; i++)
        {
            yield return null;
        }
        if (p1 == null || guest == null) yield break;
        PerformFirstSync(p1, guest, targetSlot);
    }

    private static void PerformFirstSync(Penitent p1, Penitent guest, int targetSlot)
    {
        int saveSlot = PersistentManager.GetAutomaticSlot();
        if (saveSlot < 0) return;

        var pData = CoopSaveManager.GetSlotData(saveSlot).GetPlayer(targetSlot);
        EntityStats.StatsTypes[] allTypes = (EntityStats.StatsTypes[])Enum.GetValues(typeof(EntityStats.StatsTypes));

        if (pData.HasStatsSnapshot)
        {
            ApplySnapshot(guest, pData);
            return;
        }

        foreach (EntityStats.StatsTypes type in allTypes)
        {
            Framework.FrameworkCore.Attributes.Logic.Attribute p1Attr = p1.Stats.GetByType(type);
            Framework.FrameworkCore.Attributes.Logic.Attribute guestAttr = guest.Stats.GetByType(type);
            if (p1Attr == null || guestAttr == null) continue;
            guestAttr.SetPermanentBonus(p1Attr.PermanetBonus);
        }

        guest.Stats.Life.SetToCurrentMax();
        guest.Stats.Flask.SetToCurrentMax();
        guest.Stats.Fervour.SetToCurrentMax();
        guest.Stats.Purge.Current = Mathf.Clamp(p1.Stats.Purge.Current, 0f, 9999999f);

        SaveSnapshot(saveSlot, guest, allTypes, targetSlot);

        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info(
                $"[StatsSync] first-ever sync for save slot {saveSlot} (P{targetSlot + 1}): cloned P1's progression onto guest. " +
                $"Life.Final={guest.Stats.Life.Final:F0}, Strength.Final={guest.Stats.Strength.Final:F1}, Flask.Final={guest.Stats.Flask.Final:F0}",
                Main.CoopLocal);
        }
    }

    private static void SaveSnapshot(int saveSlot, Penitent guest, EntityStats.StatsTypes[] allTypes, int targetSlot)
    {
        try
        {
            var pData = CoopSaveManager.GetSlotData(saveSlot).GetPlayer(targetSlot);
            foreach (EntityStats.StatsTypes type in allTypes)
            {
                Framework.FrameworkCore.Attributes.Logic.Attribute attr = guest.Stats.GetByType(type);
                if (attr == null) continue;
                pData.PermanentBonuses[type.ToString()] = attr.PermanetBonus;
            }

            pData.CurrentPurge = Mathf.Clamp(guest.Stats.Purge.Current, 0f, 9999999f);
            pData.CurrentLife = guest.Stats.Life.Current;
            pData.CurrentFervour = guest.Stats.Fervour.Current;
            pData.CurrentFlask = guest.Stats.Flask.Current;
            pData.FlaskHealthBonus = guest.Stats.FlaskHealth.PermanetBonus;
            pData.HasStatsSnapshot = true;

            CoopSaveManager.SaveSlot(saveSlot);
        }
        catch (Exception ex)
        {
            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info($"[StatsSync] failed to save baseline for P{targetSlot + 1}: {ex.Message}", Main.CoopLocal);
            }
        }
    }

    internal static void SaveCurrentVitals(Penitent outgoing, int slotIndex = -1)
    {
        if (outgoing == null) return;
        int targetSlot = slotIndex > 0 ? slotIndex : CoopPlayerRegistry.GetPlayerIndex(outgoing);
        if (targetSlot <= 0) targetSlot = 1;

        int saveSlot = PersistentManager.GetAutomaticSlot();
        if (saveSlot < 0) return;

        var pData = CoopSaveManager.GetSlotData(saveSlot).GetPlayer(targetSlot);
        if (!pData.HasStatsSnapshot) return;

        try
        {
            pData.CurrentPurge = Mathf.Clamp(outgoing.Stats.Purge.Current, 0f, 9999999f);
            pData.CurrentLife = outgoing.Stats.Life.Current;
            pData.CurrentFervour = outgoing.Stats.Fervour.Current;
            pData.CurrentFlask = outgoing.Stats.Flask.Current;

            CoopSaveManager.SaveSlot(saveSlot);

            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info(
                    $"[StatsSync] saved P{targetSlot + 1}'s current vitals before respawn: Life={outgoing.Stats.Life.Current:F0} " +
                    $"Fervour={outgoing.Stats.Fervour.Current:F0} Flask={outgoing.Stats.Flask.Current:F0} " +
                    $"Purge={outgoing.Stats.Purge.Current:F0}",
                    Main.CoopLocal);
            }
        }
        catch (Exception ex)
        {
            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info($"[StatsSync] failed to save vitals before respawn for P{targetSlot + 1}: {ex.Message}", Main.CoopLocal);
            }
        }
    }

    internal static void ResetVitalsForRespawn()
    {
        int saveSlot = PersistentManager.GetAutomaticSlot();
        if (saveSlot < 0) return;
        try
        {
            for (int i = 0; i < CoopPlayerRegistry.MaxPlayers; i++)
            {
                var pData = CoopSaveManager.GetSlotData(saveSlot).GetPlayer(i);
                pData.CurrentLife = -1f;
                pData.CurrentFlask = -1f;
                pData.CurrentFervour = -1f;
                pData.PendingRevive = false;
            }
            CoopSaveManager.SaveSlot(saveSlot);
            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info("[StatsSync] Reset vitals and PendingRevive flags for respawn on all players.", Main.CoopLocal);
            }
        }
        catch (Exception ex)
        {
            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info($"[StatsSync] failed to reset vitals for respawn: {ex.Message}", Main.CoopLocal);
            }
        }
    }

    internal static void HealAtPrieDieu(Penitent guest, bool healFervour, int slotIndex = -1)
    {
        if (guest == null) return;
        int targetSlot = slotIndex > 0 ? slotIndex : CoopPlayerRegistry.GetPlayerIndex(guest);
        if (targetSlot <= 0) targetSlot = 1;

        guest.Stats.Life.SetToCurrentMax();
        guest.Stats.Flask.SetToCurrentMax();
        if (healFervour) { guest.Stats.Fervour.SetToCurrentMax(); }
        SaveCurrentVitals(guest, targetSlot);
        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info(
                $"[StatsSync] healed P{targetSlot + 1} at Prie Dieu (Life/Flask to max, Fervour healed={healFervour}).",
                Main.CoopLocal);
        }
    }

    private static void ApplySnapshot(Penitent guest, PlayerCoopData pData)
    {
        if (guest == null || pData == null) return;
        try
        {
            int applied = 0;
            EntityStats.StatsTypes[] allTypes = (EntityStats.StatsTypes[])Enum.GetValues(typeof(EntityStats.StatsTypes));
            foreach (EntityStats.StatsTypes type in allTypes)
            {
                if (pData.PermanentBonuses.TryGetValue(type.ToString(), out float bonus))
                {
                    var attr = guest.Stats.GetByType(type);
                    if (attr != null)
                    {
                        attr.SetPermanentBonus(bonus);
                        applied++;
                    }
                }
            }

            guest.Stats.Purge.Current = Mathf.Clamp(pData.CurrentPurge, 0f, 9999999f);
            if (pData.CurrentLife > 0f) guest.Stats.Life.Current = pData.CurrentLife;
            else guest.Stats.Life.SetToCurrentMax();

            if (pData.CurrentFervour >= 0f) guest.Stats.Fervour.Current = pData.CurrentFervour;
            else guest.Stats.Fervour.SetToCurrentMax();

            if (pData.CurrentFlask >= 0f) guest.Stats.Flask.Current = pData.CurrentFlask;
            else guest.Stats.Flask.SetToCurrentMax();

            if (pData.FlaskHealthBonus > 0f)
            {
                guest.Stats.FlaskHealth.SetPermanentBonus(pData.FlaskHealthBonus);
            }

            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info(
                    $"[StatsSync] restored P{pData.PlayerIndex + 1}'s saved baseline ({applied} stats) from CoopSaveManager. " +
                    $"Life={guest.Stats.Life.Current:F0}/{guest.Stats.Life.Final:F0} Fervour={guest.Stats.Fervour.Current:F0}/{guest.Stats.Fervour.CurrentMax:F0} " +
                    $"Flask={guest.Stats.Flask.Current:F0}/{guest.Stats.Flask.Final:F0} Purge={guest.Stats.Purge.Current:F0}",
                    Main.CoopLocal);
            }
        }
        catch (Exception ex)
        {
            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info($"[StatsSync] failed to restore baseline for P{pData.PlayerIndex + 1}: {ex.Message}", Main.CoopLocal);
            }
        }
    }
}
