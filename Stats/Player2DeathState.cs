using Framework.Managers;
using System;
using System.IO;
using UnityEngine;

namespace Blasphemous.CoopLocal;

// Ronda 63: P2 debe permanecer "muerto" sin reaparecer hasta el proximo Prie Dieu.
// Mismo patron exacto que Player2StatsSync/Player2SkillManager: MarkerDirectory,
// PersistentManager.GetAutomaticSlot(), try/catch-log "[P2DeathState]".
internal static class Player2DeathState
{
    private static string MarkerDirectory => Path.Combine(Application.persistentDataPath, "CoopLocalMod");
    private static string SnapshotPath(int slot) => Path.Combine(MarkerDirectory, $"p2_deathstate_slot{slot}.txt");

    private const string PendingKey = "PendingRevive";

    internal static bool IsPendingRevive()
    {
        int slot = PersistentManager.GetAutomaticSlot();
        if (slot < 0) return false;
        string path = SnapshotPath(slot);
        if (!File.Exists(path)) return false;
        try
        {
            foreach (string line in File.ReadAllLines(path))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                if (key != PendingKey) continue;
                if (bool.TryParse(val, out bool b)) return b;
                if (val == "1") return true;
                if (val == "0") return false;
            }
        }
        catch (Exception ex)
        {
            if (Main.CoopLocal != null)
                Blasphemous.ModdingAPI.ModLog.Info($"[P2DeathState] failed to read {path}: {ex.Message}", Main.CoopLocal);
        }
        return false;
    }

    internal static void MarkDeadPendingRevive()
    {
        int slot = PersistentManager.GetAutomaticSlot();
        if (slot < 0) return;
        string path = SnapshotPath(slot);
        try
        {
            Directory.CreateDirectory(MarkerDirectory);
            File.WriteAllLines(path, new[] { $"{PendingKey}=true" });
            if (Main.CoopLocal != null)
                Blasphemous.ModdingAPI.ModLog.Info($"[P2DeathState] marked PendingRevive=true slot {slot}", Main.CoopLocal);
        }
        catch (Exception ex)
        {
            if (Main.CoopLocal != null)
                Blasphemous.ModdingAPI.ModLog.Info($"[P2DeathState] failed to mark dead {path}: {ex.Message}", Main.CoopLocal);
        }
    }

    internal static void ClearPendingRevive()
    {
        int slot = PersistentManager.GetAutomaticSlot();
        if (slot < 0) return;
        string path = SnapshotPath(slot);
        try
        {
            Directory.CreateDirectory(MarkerDirectory);
            File.WriteAllLines(path, new[] { $"{PendingKey}=false" });
            if (Main.CoopLocal != null)
                Blasphemous.ModdingAPI.ModLog.Info($"[P2DeathState] cleared PendingRevive slot {slot}", Main.CoopLocal);
        }
        catch (Exception ex)
        {
            if (Main.CoopLocal != null)
                Blasphemous.ModdingAPI.ModLog.Info($"[P2DeathState] failed to clear {path}: {ex.Message}", Main.CoopLocal);
        }
    }
}
