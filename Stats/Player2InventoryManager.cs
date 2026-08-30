using Framework.Managers;
using Gameplay.GameControllers.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Blasphemous.CoopLocal;

// Fase 0 — Infra P2 inventario/loadout. Mismo patrón que Player2StatsSync / Player2SkillManager.
// Tres stores sombra: owned (bool por id) + equipped. Persistencia p2_inventory_slot{slot}.txt.
// No se clona de P1 por defecto (misma decisión que Player2SkillManager.cs:70).
internal static class Player2InventoryManager
{
    private static readonly HashSet<string> ownedBeads = new HashSet<string>();
    private static readonly HashSet<string> ownedPrayers = new HashSet<string>();
    private static readonly HashSet<string> ownedSwords = new HashSet<string>();
    private static readonly HashSet<string> ownedRelics = new HashSet<string>();

    // equipped - Beads tamaño dinámico = p2 Stats.BeadSlots.Final (max 8 vanilla)
    private static string[] equippedBeads = new string[8];
    private static string equippedPrayer = null; // MAX_PRAYERS_SLOTS =1
    private static string equippedSword = null; // NUM_SWORDS_SLOTS=1 (corazones)
    private static string[] equippedRelics = new string[3];

    private static bool initialized;
    private static string MarkerDirectory => Path.Combine(Application.persistentDataPath, "CoopLocalMod");
    private static string SnapshotPath(int slot) => Path.Combine(MarkerDirectory, $"p2_inventory_slot{slot}.txt");

    private static void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;
    }

    internal static void EnsureLoadedForCurrentSlot()
    {
        EnsureInitialized();
        int slot = PersistentManager.GetAutomaticSlot();
        if (slot < 0) return;
        LoadForSlot(slot);
    }

    internal static void LoadForSlot(int slot)
    {
        EnsureInitialized();
        // No clonar de Core.InventoryManager por defecto — partida nueva vacía (solo lo que P2 equipe).
        // Si se quiere clonado inicial, descomentar:
        // foreach(var b in Core.InventoryManager.GetRosaryBeadOwned()) ownedBeads.Add(b.id);
        // etc. + SaveForSlot(slot);
        equippedBeads = new string[8];
        equippedPrayer = null;
        equippedSword = null;
        equippedRelics = new string[3];
        ownedBeads.Clear(); ownedPrayers.Clear(); ownedSwords.Clear(); ownedRelics.Clear();

        string path = SnapshotPath(slot);
        if (!File.Exists(path)) return;
        try
        {
            foreach (string line in File.ReadAllLines(path))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                if (key.StartsWith("bead:owned:")) { if (val == "1" || val.ToLower() == "true") ownedBeads.Add(key.Substring(11)); }
                else if (key.StartsWith("prayer:owned:")) { if (val == "1" || val.ToLower() == "true") ownedPrayers.Add(key.Substring(13)); }
                else if (key.StartsWith("sword:owned:")) { if (val == "1" || val.ToLower() == "true") ownedSwords.Add(key.Substring(11)); }
                else if (key.StartsWith("relic:owned:")) { if (val == "1" || val.ToLower() == "true") ownedRelics.Add(key.Substring(11)); }
                else if (key.StartsWith("bead:slot:")) { int idx = int.Parse(key.Substring(10)); if (idx >= 0 && idx < 8) equippedBeads[idx] = string.IsNullOrEmpty(val) ? null : val; }
                else if (key == "prayer:slot:0") equippedPrayer = string.IsNullOrEmpty(val) ? null : val;
                else if (key == "sword:slot:0") equippedSword = string.IsNullOrEmpty(val) ? null : val;
                else if (key.StartsWith("relic:slot:")) { int idx = int.Parse(key.Substring(11)); if (idx >= 0 && idx < 3) equippedRelics[idx] = string.IsNullOrEmpty(val) ? null : val; }
            }
            if (Main.CoopLocal != null) Blasphemous.ModdingAPI.ModLog.Info($"[P2Inv] loaded {path}", Main.CoopLocal);
        }
        catch (Exception ex)
        {
            if (Main.CoopLocal != null) Blasphemous.ModdingAPI.ModLog.Info($"[P2Inv] failed load {path}: {ex.Message}", Main.CoopLocal);
        }
    }

    internal static void SaveForSlot(int slot)
    {
        EnsureInitialized();
        string path = SnapshotPath(slot);
        try
        {
            Directory.CreateDirectory(MarkerDirectory);
            List<string> lines = new List<string>();
            foreach (var id in ownedBeads) lines.Add($"bead:owned:{id}=1");
            foreach (var id in ownedPrayers) lines.Add($"prayer:owned:{id}=1");
            foreach (var id in ownedSwords) lines.Add($"sword:owned:{id}=1");
            foreach (var id in ownedRelics) lines.Add($"relic:owned:{id}=1");
            for (int i = 0; i < 8; i++) lines.Add($"bead:slot:{i}={equippedBeads[i] ?? ""}");
            lines.Add($"prayer:slot:0={equippedPrayer ?? ""}");
            lines.Add($"sword:slot:0={equippedSword ?? ""}");
            for (int i = 0; i < 3; i++) lines.Add($"relic:slot:{i}={equippedRelics[i] ?? ""}");
            File.WriteAllLines(path, lines.ToArray());
            if (Main.CoopLocal != null) Blasphemous.ModdingAPI.ModLog.Info($"[P2Inv] saved {path}", Main.CoopLocal);
        }
        catch (Exception ex)
        {
            if (Main.CoopLocal != null) Blasphemous.ModdingAPI.ModLog.Info($"[P2Inv] failed save {path}: {ex.Message}", Main.CoopLocal);
        }
    }

    internal static void Persist()
    {
        int slot = PersistentManager.GetAutomaticSlot();
        if (slot < 0) return;
        SaveForSlot(slot);
    }

    // Owned helpers — si P2 no tiene shadow owned, fallback a global owned (colección compartida)
    internal static bool IsOwnedBead(string id) => ownedBeads.Contains(id) || (Core.InventoryManager != null && Core.InventoryManager.GetRosaryBead(id) != null && ownedBeads.Count == 0 && IsGlobalOwnedBead(id));
    private static bool IsGlobalOwnedBead(string id)
    {
        try { foreach (var b in Core.InventoryManager.GetRosaryBeadOwned()) if (b.id == id) return true; } catch { }
        return false;
    }

    internal static bool IsPrayerOwned(string id)
    {
        if (ownedPrayers.Contains(id)) return true;
        if (ownedPrayers.Count == 0 && Core.InventoryManager != null)
        {
            try { foreach (var p in Core.InventoryManager.GetPrayersOwned()) if (p.id == id) return true; } catch { }
        }
        return false;
    }

    internal static bool IsPrayerEquipped(string id) => equippedPrayer != null && equippedPrayer == id;
    internal static bool IsPrayerEquipped(Framework.Inventory.Prayer p) => p != null && IsPrayerEquipped(p.id);
    internal static string GetEquippedPrayerId() => equippedPrayer;
    internal static Framework.Inventory.Prayer GetEquippedPrayerObj()
    {
        if (string.IsNullOrEmpty(equippedPrayer) || Core.InventoryManager == null) return null;
        return Core.InventoryManager.GetPrayer(equippedPrayer);
    }

    internal static bool IsBeadEquipped(string id)
    {
        foreach (var s in equippedBeads) if (s == id) return true;
        return false;
    }
    internal static bool IsBeadEquipped(Framework.Inventory.RosaryBead b) => b != null && IsBeadEquipped(b.id);

    internal static bool IsSwordEquipped(string id) => equippedSword != null && equippedSword == id;
    internal static bool IsSwordEquipped(Framework.Inventory.Sword s) => s != null && IsSwordEquipped(s.id);

    internal static void EquipPrayer(string id)
    {
        equippedPrayer = id;
        if (!string.IsNullOrEmpty(id)) ownedPrayers.Add(id);
        Persist();
    }
    internal static void UnequipPrayer() { equippedPrayer = null; Persist(); }

    internal static void EquipBead(string id, int slot)
    {
        if (slot < 0 || slot >= 8) return;
        // Unequip si ya estaba en otro slot
        for (int i = 0; i < 8; i++) if (equippedBeads[i] == id) equippedBeads[i] = null;
        equippedBeads[slot] = id;
        if (!string.IsNullOrEmpty(id)) ownedBeads.Add(id);
        Persist();
    }
    internal static void UnequipBead(string id)
    {
        for (int i = 0; i < 8; i++) if (equippedBeads[i] == id) equippedBeads[i] = null;
        Persist();
    }
    internal static void UnequipBeadSlot(int slot) { if (slot >= 0 && slot < 8) { equippedBeads[slot] = null; Persist(); } }

    internal static void EquipSword(string id) { equippedSword = id; if (!string.IsNullOrEmpty(id)) ownedSwords.Add(id); Persist(); }
    internal static void UnequipSword() { equippedSword = null; Persist(); }

    internal static int FindFreeBeadSlot()
    {
        for (int i = 0; i < 8; i++) if (string.IsNullOrEmpty(equippedBeads[i])) return i;
        return -1;
    }
    internal static int FindBeadSlot(string id)
    {
        for (int i = 0; i < 8; i++) if (equippedBeads[i] == id) return i;
        return -1;
    }
    internal static string[] GetEquippedBeads() => (string[])equippedBeads.Clone();
}
