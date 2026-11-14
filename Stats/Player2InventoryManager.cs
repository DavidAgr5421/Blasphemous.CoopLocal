using Gameplay.GameControllers.Penitent;

using Framework.Managers;

using Gameplay.GameControllers.Entities;

using System;

using System.Collections.Generic;

using UnityEngine;



namespace Blasphemous.CoopLocal;



// Fase 0 — Infra multi-jugador inventario/loadout (P2, P3, P4).

// Stores sombra por cada jugador: owned + equipped. Persistencia unificada en coop_savedata_slot{slot}.txt.

internal static class Player2InventoryManager

{

    private static int DefaultPlayerIndex => (Player2MenuView.ActivePlayerIndex > 0) ? Player2MenuView.ActivePlayerIndex : 1;



    internal static PlayerCoopData GetPlayerData(int playerIndex = -1)

    {

        int p = playerIndex > 0 ? playerIndex : DefaultPlayerIndex;

        return CoopSaveManager.CurrentSave.GetPlayer(p);

    }



    internal static void EnsureLoadedForCurrentSlot()

    {

        CoopSaveManager.EnsureLoadedForCurrentSlot();

    }



    internal static void LoadForSlot(int slot)

    {

        CoopSaveManager.LoadSlot(slot);

    }



    internal static void SaveForSlot(int slot)

    {

        CoopSaveManager.SaveSlot(slot);

    }



    internal static void Persist()

    {

        CoopSaveManager.SaveCurrentSlot();

    }



    // Owned helpers — si el jugador no tiene shadow owned, fallback a global owned (colección compartida)

    internal static bool IsOwnedBead(string id, int playerIndex = -1)

    {

        var data = GetPlayerData(playerIndex);

        return data.OwnedBeads.Contains(id) || (Core.InventoryManager != null && IsGlobalOwnedBead(id));

    }



    private static bool IsGlobalOwnedBead(string id)

    {

        try { foreach (var b in Core.InventoryManager.GetRosaryBeadOwned()) if (b.id == id) return true; } catch { }

        return false;

    }



    internal static void AddOwnedBead(string id, int playerIndex = -1)

    {

        if (string.IsNullOrEmpty(id)) return;

        var data = GetPlayerData(playerIndex);

        if (data.OwnedBeads.Add(id))

        {

            Persist();

            if (Main.CoopLocal != null)

            {

                Blasphemous.ModdingAPI.ModLog.Info($"[P{data.PlayerIndex + 1}Inv] Rosary bead unlocked: {id}", Main.CoopLocal);

            }

        }

    }



    internal static bool IsRelicOwned(string id, int playerIndex = -1)

    {

        if (string.IsNullOrEmpty(id)) return false;

        var data = GetPlayerData(playerIndex);

        if (data.OwnedRelics.Contains(id)) return true;

        if (Core.InventoryManager != null)

        {

            try

            {

                foreach (var r in Core.InventoryManager.GetRelicsOwned())

                {

                    if (r != null && string.Equals(r.id, id, StringComparison.OrdinalIgnoreCase)) return true;

                }

            }

            catch { }

        }

        return false;

    }



    internal static void AddOwnedRelic(string id, int playerIndex = -1)

    {

        if (string.IsNullOrEmpty(id)) return;

        var data = GetPlayerData(playerIndex);

        if (data.OwnedRelics.Add(id))

        {

            Persist();

            if (Main.CoopLocal != null)

            {

                Blasphemous.ModdingAPI.ModLog.Info($"[P{data.PlayerIndex + 1}Inv] Relic unlocked: {id}", Main.CoopLocal);

            }

        }

    }



    internal static bool IsPrayerOwned(string id, int playerIndex = -1)

    {

        if (string.IsNullOrEmpty(id)) return false;

        var data = GetPlayerData(playerIndex);

        if (data.OwnedPrayers.Contains(id)) return true;

        if (Core.InventoryManager != null)

        {

            try

            {

                foreach (var p in Core.InventoryManager.GetPrayersOwned())

                {

                    if (p != null && string.Equals(p.id, id, StringComparison.OrdinalIgnoreCase)) return true;

                }

            }

            catch { }

        }

        return false;

    }



    internal static void AddOwnedPrayer(string id, int playerIndex = -1)

    {

        if (string.IsNullOrEmpty(id)) return;

        var data = GetPlayerData(playerIndex);

        if (data.OwnedPrayers.Add(id))

        {

            Persist();

            if (Main.CoopLocal != null)

            {

                Blasphemous.ModdingAPI.ModLog.Info($"[P{data.PlayerIndex + 1}Inv] Prayer unlocked: {id}", Main.CoopLocal);

            }

        }

    }



    internal static bool IsPrayerEquipped(string id, int playerIndex = -1)

    {

        var data = GetPlayerData(playerIndex);

        return data.EquippedPrayer != null && data.EquippedPrayer == id;

    }



    internal static void AddOwnedSword(string id, int playerIndex = -1)

    {

        if (string.IsNullOrEmpty(id)) return;

        string clean = id.TrimStart(':');

        var data = GetPlayerData(playerIndex);

        if (data.OwnedSwords.Add(clean))

        {

            Persist();

        }

    }



    internal static void RemoveOwnedSword(string id, int playerIndex = -1)

    {

        if (string.IsNullOrEmpty(id)) return;

        string clean = id.TrimStart(':');

        var data = GetPlayerData(playerIndex);

        if (data.OwnedSwords.Remove(clean))

        {

            if (data.EquippedSword == clean) data.EquippedSword = null;

            Persist();

        }

    }



    internal static bool IsSwordOwned(string id, int playerIndex = -1)

    {

        string clean = id?.TrimStart(':') ?? "";

        var data = GetPlayerData(playerIndex);

        if (data.OwnedSwords.Contains(clean)) return true;

        // Los corazones elementales nunca se comparten desde P1 a guests

        if (SwordElementalUpgrades.IsElementalHeart(clean)) return false;

        if (Core.InventoryManager != null)

        {

            try { foreach (var s in Core.InventoryManager.GetSwordsOwned()) if (s.id == clean) return true; } catch { }

        }

        return false;

    }



    internal static bool IsPrayerEquipped(Framework.Inventory.Prayer p, int playerIndex = -1) => p != null && IsPrayerEquipped(p.id, playerIndex);

    internal static string GetEquippedPrayerId(int playerIndex = -1) => GetPlayerData(playerIndex).EquippedPrayer;



    internal static Framework.Inventory.Prayer GetEquippedPrayerObj(int playerIndex = -1)

    {

        var data = GetPlayerData(playerIndex);

        if (Core.InventoryManager == null || string.IsNullOrEmpty(data.EquippedPrayer)) return null;

        return Core.InventoryManager.GetPrayer(data.EquippedPrayer);

    }



    internal static bool IsBeadEquipped(string id, int playerIndex = -1)

    {

        var data = GetPlayerData(playerIndex);

        foreach (var s in data.EquippedBeads) if (s == id) return true;

        return false;

    }

    internal static bool IsBeadEquipped(Framework.Inventory.RosaryBead b, int playerIndex = -1) => b != null && IsBeadEquipped(b.id, playerIndex);



    internal static bool IsSwordEquipped(string id, int playerIndex = -1)

    {

        string clean = id?.TrimStart(':');

        var data = GetPlayerData(playerIndex);

        return data.EquippedSword != null && data.EquippedSword == clean;

    }

    internal static bool IsSwordEquipped(Framework.Inventory.Sword s, int playerIndex = -1) => s != null && IsSwordEquipped(s.id, playerIndex);



    internal static void EquipPrayer(string id, int playerIndex = -1)

    {

        var data = GetPlayerData(playerIndex);

        data.EquippedPrayer = id;

        if (!string.IsNullOrEmpty(id)) data.OwnedPrayers.Add(id);

        Persist();

    }

    internal static void UnequipPrayer(int playerIndex = -1) { GetPlayerData(playerIndex).EquippedPrayer = null; Persist(); }



    internal static void EquipBead(string id, int slot, int playerIndex = -1)

    {

        if (slot < 0 || slot >= 8) return;

        var data = GetPlayerData(playerIndex);

        // Unequip si ya estaba en otro slot

        for (int i = 0; i < 8; i++) if (data.EquippedBeads[i] == id) data.EquippedBeads[i] = null;

        data.EquippedBeads[slot] = id;

        if (!string.IsNullOrEmpty(id)) data.OwnedBeads.Add(id);

        Persist();

    }

    internal static void UnequipBead(string id, int playerIndex = -1)

    {

        var data = GetPlayerData(playerIndex);

        for (int i = 0; i < 8; i++) if (data.EquippedBeads[i] == id) data.EquippedBeads[i] = null;

        Persist();

    }

    internal static void UnequipBeadSlot(int slot, int playerIndex = -1)

    {

        if (slot >= 0 && slot < 8) { GetPlayerData(playerIndex).EquippedBeads[slot] = null; Persist(); }

    }



    internal static void EquipSword(string id, int playerIndex = -1)

    {

        string clean = id?.TrimStart(':');

        var data = GetPlayerData(playerIndex);

        data.EquippedSword = clean;

        if (!string.IsNullOrEmpty(clean)) data.OwnedSwords.Add(clean);

        Persist();

    }

    internal static void UnequipSword(int playerIndex = -1) { GetPlayerData(playerIndex).EquippedSword = null; Persist(); }



    internal static string GetBeadInSlot(int slot, int playerIndex = -1)

    {

        if (slot >= 0 && slot < 8) return GetPlayerData(playerIndex).EquippedBeads[slot];

        return null;

    }



    internal static string GetSwordInSlot(int slot, int playerIndex = -1)

    {

        if (slot == 0) return GetPlayerData(playerIndex).EquippedSword;

        return null;

    }



    internal static string GetRelicInSlot(int slot, int playerIndex = -1)

    {

        if (slot >= 0 && slot < 3) return GetPlayerData(playerIndex).EquippedRelics[slot];

        return null;

    }



    internal static string GetEquippedSwordId(int playerIndex = -1) => GetPlayerData(playerIndex).EquippedSword;



    internal static int FindFreeBeadSlot(int playerIndex = -1)

    {

        int p = playerIndex > 0 ? playerIndex : DefaultPlayerIndex;

        Penitent penitent = CoopPlayerRegistry.GetPenitent(p);

        int maxSlots = penitent != null ? (int)penitent.Stats.BeadSlots.Final : 8;

        if (maxSlots <= 0) maxSlots = 2;

        var data = GetPlayerData(p);

        for (int i = 0; i < maxSlots && i < 8; i++)

        {

            if (string.IsNullOrEmpty(data.EquippedBeads[i])) return i;

        }

        return -1;

    }



    internal static int FindBeadSlot(string id, int playerIndex = -1)

    {

        var data = GetPlayerData(playerIndex);

        for (int i = 0; i < 8; i++) if (data.EquippedBeads[i] == id) return i;

        return -1;

    }



    internal static string[] GetEquippedBeads(int playerIndex = -1) => (string[])GetPlayerData(playerIndex).EquippedBeads.Clone();



    // Verificaciones globales para todos los invitados (gameplay pasivo)

    internal static bool IsAnyGuestBeadEquipped(string id)

    {

        for (int p = 0; p < CoopPlayerRegistry.MaxPlayers; p++)

        {

            if (IsBeadEquipped(id, p)) return true;

        }

        return false;

    }



    internal static bool IsAnyGuestPrayerEquipped(string id)

    {

        for (int p = 0; p < CoopPlayerRegistry.MaxPlayers; p++)

        {

            if (IsPrayerEquipped(id, p)) return true;

        }

        return false;

    }



    internal static bool IsAnyGuestSwordEquipped(string id)

    {

        for (int p = 0; p < CoopPlayerRegistry.MaxPlayers; p++)

        {

            if (IsSwordEquipped(id, p)) return true;

        }

        return false;

    }


    internal static bool IsRelicEquipped(string id, int playerIndex = -1)
    {
        if (string.IsNullOrEmpty(id)) return false;
        var data = GetPlayerData(playerIndex);
        for (int i = 0; i < 3; i++)
        {
            if (string.Equals(data.EquippedRelics[i], id, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    internal static bool IsRelicEquipped(Framework.Inventory.Relic r, int playerIndex = -1) => r != null && IsRelicEquipped(r.id, playerIndex);

    internal static void EquipRelic(string id, int slot, int playerIndex = -1)
    {
        if (slot < 0 || slot >= 3) return;
        var data = GetPlayerData(playerIndex);
        for (int i = 0; i < 3; i++)
        {
            if (string.Equals(data.EquippedRelics[i], id, StringComparison.OrdinalIgnoreCase))
                data.EquippedRelics[i] = null;
        }
        data.EquippedRelics[slot] = id;
        if (!string.IsNullOrEmpty(id)) data.OwnedRelics.Add(id);
        Persist();
    }

    internal static void UnequipRelic(string id, int playerIndex = -1)
    {
        if (string.IsNullOrEmpty(id)) return;
        var data = GetPlayerData(playerIndex);
        for (int i = 0; i < 3; i++)
        {
            if (string.Equals(data.EquippedRelics[i], id, StringComparison.OrdinalIgnoreCase))
                data.EquippedRelics[i] = null;
        }
        Persist();
    }

    internal static void UnequipRelicSlot(int slot, int playerIndex = -1)
    {
        if (slot >= 0 && slot < 3)
        {
            GetPlayerData(playerIndex).EquippedRelics[slot] = null;
            Persist();
        }
    }

    internal static bool IsAnyGuestRelicEquipped(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        for (int p = 1; p < CoopPlayerRegistry.MaxPlayers; p++)
        {
            if (IsRelicEquipped(id, p)) return true;
        }
        return false;
    }

    private static readonly System.Reflection.FieldInfo VanillaOwnPrayersField =
        HarmonyLib.AccessTools.Field(typeof(InventoryManager), "ownPrayers");
    private static readonly System.Reflection.FieldInfo VanillaOwnBeadsField =
        HarmonyLib.AccessTools.Field(typeof(InventoryManager), "ownBeads");
    private static readonly System.Reflection.FieldInfo VanillaOwnRelicsField =
        HarmonyLib.AccessTools.Field(typeof(InventoryManager), "ownRellics");
    private static readonly System.Reflection.FieldInfo VanillaOwnSwordsField =
        HarmonyLib.AccessTools.Field(typeof(InventoryManager), "ownSwords");

    internal static void SyncAllVanillaInventoryToCoopPlayers()
    {
        if (Core.InventoryManager == null) return;
        // InventoryManager.ownPrayers/ownBeads/ownRellics/ownSwords stay null until InitializeObjects() runs;
        // GetXOwned() would NRE on startup before that.
        if (VanillaOwnPrayersField == null || VanillaOwnPrayersField.GetValue(Core.InventoryManager) == null ||
            VanillaOwnBeadsField.GetValue(Core.InventoryManager) == null ||
            VanillaOwnRelicsField.GetValue(Core.InventoryManager) == null ||
            VanillaOwnSwordsField.GetValue(Core.InventoryManager) == null)
        {
            DashParryDebugLog.LogVerbose("[CoopInventory] SyncAllVanillaInventoryToCoopPlayers skipped: vanilla inventory lists not initialized yet.");
            return;
        }
        try
        {
            bool changed = false;
            var prayers = Core.InventoryManager.GetPrayersOwned();
            var beads = Core.InventoryManager.GetRosaryBeadOwned();
            var relics = Core.InventoryManager.GetRelicsOwned();
            var swords = Core.InventoryManager.GetSwordsOwned();

            for (int p = 0; p < CoopPlayerRegistry.MaxPlayers; p++)
            {
                var data = CoopSaveManager.CurrentSave.GetPlayer(p);
                if (data == null) continue;

                if (prayers != null)
                {
                    foreach (var pr in prayers)
                    {
                        if (pr != null && !string.IsNullOrEmpty(pr.id))
                        {
                            if (data.OwnedPrayers.Add(pr.id)) changed = true;
                        }
                    }
                }

                if (beads != null)
                {
                    foreach (var bd in beads)
                    {
                        if (bd != null && !string.IsNullOrEmpty(bd.id))
                        {
                            if (data.OwnedBeads.Add(bd.id)) changed = true;
                        }
                    }
                }

                if (relics != null)
                {
                    foreach (var rl in relics)
                    {
                        if (rl != null && !string.IsNullOrEmpty(rl.id))
                        {
                            if (data.OwnedRelics.Add(rl.id)) changed = true;
                        }
                    }
                }

                if (swords != null)
                {
                    foreach (var sw in swords)
                    {
                        if (sw != null && !string.IsNullOrEmpty(sw.id) && !SwordElementalUpgrades.IsElementalHeart(sw.id))
                        {
                            string clean = sw.id.TrimStart(':');
                            if (data.OwnedSwords.Add(clean)) changed = true;
                        }
                    }
                }
            }

            if (changed)
            {
                Persist();
                if (Main.CoopLocal != null)
                {
                    Blasphemous.ModdingAPI.ModLog.Info("[CoopInventory] Synced vanilla inventory across all player slots.", Main.CoopLocal);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CoopInventory] SyncAllVanillaInventoryToCoopPlayers error: {ex.Message}");
        }
    }

}

[HarmonyLib.HarmonyPatch(typeof(InventoryManager), "AddPrayer", new[] { typeof(Framework.Inventory.Prayer) })]

internal static class InventoryManager_AddPrayer_P2_Patch

{

    private static void Postfix(Framework.Inventory.Prayer prayer, bool __result)

    {

        if (prayer != null)

        {

            for (int p = 0; p < CoopPlayerRegistry.MaxPlayers; p++)

            {

                Player2InventoryManager.AddOwnedPrayer(prayer.id, p);

            }

        }

    }

}



[HarmonyLib.HarmonyPatch(typeof(InventoryManager), "AddPrayer", new[] { typeof(string) })]

internal static class InventoryManager_AddPrayerById_P2_Patch

{

    private static void Postfix(string idPrayer, bool __result)

    {

        if (!string.IsNullOrEmpty(idPrayer))

        {

            for (int p = 0; p < CoopPlayerRegistry.MaxPlayers; p++)

            {

                Player2InventoryManager.AddOwnedPrayer(idPrayer.ToUpper(), p);

            }

        }

    }

}



[HarmonyLib.HarmonyPatch(typeof(InventoryManager), "AddRosaryBead", new[] { typeof(Framework.Inventory.RosaryBead) })]

internal static class InventoryManager_AddRosaryBead_P2_Patch

{

    private static void Postfix(Framework.Inventory.RosaryBead rosaryBead, bool __result)

    {

        if (rosaryBead != null)

        {

            for (int p = 0; p < CoopPlayerRegistry.MaxPlayers; p++)

            {

                Player2InventoryManager.AddOwnedBead(rosaryBead.id, p);

            }

        }

    }

}



[HarmonyLib.HarmonyPatch(typeof(InventoryManager), "AddRosaryBead", new[] { typeof(string) })]

internal static class InventoryManager_AddRosaryBeadById_P2_Patch

{

    private static void Postfix(string idRosaryBead, bool __result)

    {

        if (!string.IsNullOrEmpty(idRosaryBead))

        {

            for (int p = 0; p < CoopPlayerRegistry.MaxPlayers; p++)

            {

                Player2InventoryManager.AddOwnedBead(idRosaryBead.ToUpper(), p);

            }

        }

    }

}



[HarmonyLib.HarmonyPatch(typeof(InventoryManager), "AddSword", new[] { typeof(Framework.Inventory.Sword) })]

internal static class InventoryManager_AddSword_P2_Patch

{

    private static void Postfix(Framework.Inventory.Sword sword, bool __result)

    {

        if (sword != null && !SwordElementalUpgrades.IsElementalHeart(sword.id))

        {

            for (int p = 0; p < CoopPlayerRegistry.MaxPlayers; p++)

            {

                Player2InventoryManager.AddOwnedSword(sword.id, p);

            }

        }

    }

}



[HarmonyLib.HarmonyPatch(typeof(InventoryManager), "AddSword", new[] { typeof(string) })]

internal static class InventoryManager_AddSwordById_P2_Patch

{

    private static void Postfix(string idSword, bool __result)

    {

        if (!string.IsNullOrEmpty(idSword) && !SwordElementalUpgrades.IsElementalHeart(idSword))

        {

            for (int p = 0; p < CoopPlayerRegistry.MaxPlayers; p++)

            {

                Player2InventoryManager.AddOwnedSword(idSword.ToUpper(), p);

            }

        }

    }

}



[HarmonyLib.HarmonyPatch(typeof(InventoryManager), "AddRelic", new[] { typeof(Framework.Inventory.Relic) })]

internal static class InventoryManager_AddRelic_P2_Patch

{

    private static void Postfix(Framework.Inventory.Relic relic, bool __result)

    {

        if (relic != null)

        {

            for (int p = 0; p < CoopPlayerRegistry.MaxPlayers; p++)

            {

                Player2InventoryManager.AddOwnedRelic(relic.id, p);

            }

        }

    }

}



[HarmonyLib.HarmonyPatch(typeof(InventoryManager), "AddRelic", new[] { typeof(string) })]

internal static class InventoryManager_AddRelicById_P2_Patch

{

    private static void Postfix(string idRelic, bool __result)

    {

        if (!string.IsNullOrEmpty(idRelic))

        {

            for (int p = 0; p < CoopPlayerRegistry.MaxPlayers; p++)

            {

                Player2InventoryManager.AddOwnedRelic(idRelic.ToUpper(), p);

            }

        }

    }

}


[HarmonyLib.HarmonyPatch(typeof(InventoryManager), "AddBaseObject", new[] { typeof(Framework.Inventory.BaseInventoryObject) })]
internal static class InventoryManager_AddBaseObject_P2_Patch
{
    private static void Postfix(Framework.Inventory.BaseInventoryObject inbObject, bool __result)
    {
        if (inbObject == null) return;
        if (inbObject is Framework.Inventory.Prayer prayer)
        {
            for (int p = 0; p < CoopPlayerRegistry.MaxPlayers; p++)
                Player2InventoryManager.AddOwnedPrayer(prayer.id, p);
        }
        else if (inbObject is Framework.Inventory.RosaryBead bead)
        {
            for (int p = 0; p < CoopPlayerRegistry.MaxPlayers; p++)
                Player2InventoryManager.AddOwnedBead(bead.id, p);
        }
        else if (inbObject is Framework.Inventory.Relic relic)
        {
            for (int p = 0; p < CoopPlayerRegistry.MaxPlayers; p++)
                Player2InventoryManager.AddOwnedRelic(relic.id, p);
        }
        else if (inbObject is Framework.Inventory.Sword sword && !SwordElementalUpgrades.IsElementalHeart(sword.id))
        {
            for (int p = 0; p < CoopPlayerRegistry.MaxPlayers; p++)
                Player2InventoryManager.AddOwnedSword(sword.id, p);
        }
    }
}
