using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Framework.Managers;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using UnityEngine;

namespace Blasphemous.CoopLocal;

/// <summary>
/// Representa todos los datos persistentes de un jugador cooperativo (P1 a P4):
/// Configuración, Identidad, Aspecto, Estados de muerte, Atributos/Stats, Habilidades e Inventario.
/// </summary>
public sealed class PlayerCoopData
{
    public int PlayerIndex; // 0 = P1 (Host), 1 = P2, 2 = P3, 3 = P4
    public string Name = "";
    public int ColorIndex = 0;
    public CoopDeviceType Device = CoopDeviceType.Unassigned;
    public string SkinId = "";
    public bool PendingRevive = false;

    // --- Progresión / Atributos ---
    public bool HasStatsSnapshot = false;
    public float CurrentLife = 100f;
    public float CurrentFervour = 100f;
    public float CurrentFlask = 2f;
    public float FlaskHealthBonus = 0f;
    public float CurrentPurge = 0f;
    public readonly Dictionary<string, float> PermanentBonuses = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

    // --- Habilidades Desbloqueadas ---
    public readonly Dictionary<string, bool> Skills = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

    // --- Inventario / Loadout Propio ---
    public readonly HashSet<string> OwnedBeads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public readonly string[] EquippedBeads = new string[8];
    public readonly HashSet<string> OwnedPrayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public string EquippedPrayer = null;
    public readonly HashSet<string> OwnedSwords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public string EquippedSword = null;
    public readonly HashSet<string> OwnedRelics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public readonly string[] EquippedRelics = new string[3];

    public PlayerCoopData(int playerIndex)
    {
        PlayerIndex = playerIndex;
        ResetToDefaults();
    }

    public void ResetToDefaults()
    {
        if (playerIndexMatchesConfig())
        {
            var def = CoopGameConfig.Slots[PlayerIndex];
            Name = def.Name;
            ColorIndex = def.ColorIndex;
            Device = def.Device;
            SkinId = def.SkinId;
        }
        else
        {
            Name = $"P{PlayerIndex + 1}";
            ColorIndex = Mathf.Clamp(PlayerIndex, 0, CoopGameConfig.Palette.Length - 1);
            Device = CoopDeviceType.Unassigned;
            SkinId = "PENITENT_DEFAULT";
        }

        PendingRevive = false;
        HasStatsSnapshot = false;
        CurrentLife = 100f;
        CurrentFervour = 100f;
        CurrentFlask = 2f;
        FlaskHealthBonus = 0f;
        CurrentPurge = 0f;
        PermanentBonuses.Clear();

        Skills.Clear();
        foreach (string id in Player2SkillManager.AllIds)
        {
            Skills[id] = false;
        }

        OwnedBeads.Clear();
        for (int i = 0; i < 8; i++) EquippedBeads[i] = null;
        OwnedPrayers.Clear();
        EquippedPrayer = null;
        OwnedSwords.Clear();
        EquippedSword = null;
        OwnedRelics.Clear();
        for (int i = 0; i < 3; i++) EquippedRelics[i] = null;
    }

    private bool playerIndexMatchesConfig()
    {
        return PlayerIndex >= 0 && PlayerIndex < CoopGameConfig.Slots.Length;
    }
}

/// <summary>
/// Contenedor unificado de datos guardados de un slot específico (coop_savedata_slot{slot}.txt).
/// Incluye la configuración de la partida cooperativa y los datos de P1..P4.
/// </summary>
public sealed class CoopSlotSaveData
{
    public int SlotIndex; // 0, 1, 2... (correspondiente a savegame_0, savegame_1, savegame_2)
    public bool HasCompletedSetup = false;
    public bool IsCoopEnabled = true;
    public bool EnableElementalUpgrades = true;
    public int PlayerCount = 2;
    public float EnemyMultiplier = 1.0f;
    public float EnemyHealthMultiplier = 1.0f;
    public float BossResistanceFactor = 1.0f;
    public string LastSaved = "";

    public readonly PlayerCoopData[] Players = new PlayerCoopData[4];

    public CoopSlotSaveData(int slotIndex)
    {
        SlotIndex = slotIndex;
        for (int i = 0; i < Players.Length; i++)
        {
            Players[i] = new PlayerCoopData(i);
        }
    }

    public PlayerCoopData GetPlayer(int playerIndex)
    {
        if (playerIndex >= 0 && playerIndex < Players.Length)
            return Players[playerIndex];
        return Players[0];
    }

    public void Reset()
    {
        HasCompletedSetup = false;
        IsCoopEnabled = true;
        EnableElementalUpgrades = true;
        PlayerCount = 2;
        EnemyMultiplier = 1.0f;
        EnemyHealthMultiplier = 1.0f;
        BossResistanceFactor = 1.0f;
        LastSaved = "";
        for (int i = 0; i < Players.Length; i++)
        {
            Players[i].ResetToDefaults();
        }
    }
}

/// <summary>
/// Sistema Central de Persistencia Unificada para el Modo Cooperativo.
/// Centraliza la lectura y escritura del archivo único 'coop_savedata_slot{slot}.txt' por ranura,
/// unificando inventarios, habilidades, atributos, muerte y configuración de mandos/enemigos para todos los jugadores.
/// </summary>
internal static class CoopSaveManager
{
    private static readonly Dictionary<int, CoopSlotSaveData> loadedSlots = new Dictionary<int, CoopSlotSaveData>();
    private static int activeSetupSlot = -1;

    internal static string MarkerDirectory => Path.Combine(Application.persistentDataPath, "CoopLocalMod");

    /// <summary>
    /// Ranura temporal que se está configurando en la interfaz de selección de slots.
    /// Si es &gt;= 0, tiene prioridad sobre PersistentManager.GetAutomaticSlot().
    /// </summary>
    internal static int TargetSetupSlot
    {
        get => activeSetupSlot;
        set => activeSetupSlot = value;
    }

    /// <summary>
    /// Ranura activa actual (0 = primer slot, 1 = segundo slot, etc.).
    /// </summary>
    internal static int CurrentSlot
    {
        get
        {
            if (activeSetupSlot >= 0) return activeSetupSlot;
            int auto = PersistentManager.GetAutomaticSlot();
            return auto >= 0 ? auto : 0;
        }
    }

    /// <summary>
    /// Retorna los datos unificados del slot activo actual.
    /// </summary>
    internal static CoopSlotSaveData CurrentSave => GetSlotData(CurrentSlot);

    /// <summary>
    /// Nombre de archivo primario (1-based para coincidir con la UI del juego: slot 0 -> coop_savedata_slot1.txt).
    /// </summary>
    internal static string GetPrimaryFilePath(int slot)
    {
        int fileNumber = slot + 1;
        return Path.Combine(MarkerDirectory, $"coop_savedata_slot{fileNumber}.txt");
    }

    /// <summary>
    /// Nombre de archivo alternativo (0-based: slot 0 -> coop_savedata_slot0.txt).
    /// </summary>
    internal static string GetSecondaryFilePath(int slot)
    {
        return Path.Combine(MarkerDirectory, $"coop_savedata_slot{slot}.txt");
    }

    /// <summary>
    /// Retorna los datos de un slot (cargándolos desde disco o migrando archivos antiguos si es necesario).
    /// </summary>
    internal static CoopSlotSaveData GetSlotData(int slot)
    {
        if (slot < 0) slot = 0;
        if (!loadedSlots.TryGetValue(slot, out var data))
        {
            data = LoadSlot(slot);
        }
        return data;
    }

    /// <summary>
    /// Asegura que el slot actual esté cargado y sincronizado.
    /// </summary>
    internal static void EnsureLoadedForCurrentSlot()
    {
        int auto = PersistentManager.GetAutomaticSlot();
        if (auto >= 0)
        {
            activeSetupSlot = -1;
        }
        int slot = CurrentSlot;
        if (slot < 0) return;
        LoadSlot(slot);
    }

    /// <summary>
    /// Carga el archivo unificado de guardado de un slot.
    /// Si no existe, realiza migración automática de archivos legacy (p2_inventory, p2_skills, p2_stats, etc.).
    /// </summary>
    internal static CoopSlotSaveData LoadSlot(int slot)
    {
        if (slot < 0) slot = 0;

        CoopSlotSaveData data = new CoopSlotSaveData(slot);
        loadedSlots[slot] = data;

        string primaryPath = GetPrimaryFilePath(slot);
        string secondaryPath = GetSecondaryFilePath(slot);
        string targetPath = null;

        if (File.Exists(primaryPath))
        {
            targetPath = primaryPath;
        }
        else if (File.Exists(secondaryPath))
        {
            targetPath = secondaryPath;
        }

        if (targetPath != null)
        {
            try
            {
                ParseSaveDataFile(targetPath, data);
                if (Main.CoopLocal != null)
                {
                    Blasphemous.ModdingAPI.ModLog.Info($"[CoopSaveManager] Slot {slot} cargado exitosamente desde {targetPath}", Main.CoopLocal);
                }
                return data;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CoopSaveManager] Error leyendo {targetPath}: {ex.Message}");
            }
        }

        // Si no existe coop_savedata, verificar si existen archivos legacy para migrar
        bool migrated = MigrateLegacyFilesIfPresent(slot, data);
        if (migrated)
        {
            SaveSlot(slot);
            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info($"[CoopSaveManager] Slot {slot} migrado exitosamente a {primaryPath}", Main.CoopLocal);
            }
        }
        else
        {
            // Inicializar configuración con la activa si existe
            SyncConfigFromGame(data);
        }

        return data;
    }

    /// <summary>
    /// Guarda los datos del slot especificado en el archivo unificado coop_savedata_slot{slot+1}.txt.
    /// </summary>
    internal static void SaveSlot(int slot)
    {
        if (slot < 0) slot = 0;
        if (!loadedSlots.TryGetValue(slot, out var data))
        {
            data = new CoopSlotSaveData(slot);
            loadedSlots[slot] = data;
        }

        data.LastSaved = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string primaryPath = GetPrimaryFilePath(slot);

        try
        {
            Directory.CreateDirectory(MarkerDirectory);
            string content = SerializeSaveDataFile(data);
            File.WriteAllText(primaryPath, content, Encoding.UTF8);

            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info($"[CoopSaveManager] Guardado coop completado: {primaryPath}", Main.CoopLocal);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CoopSaveManager] Error guardando {primaryPath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Guarda el slot activo actual.
    /// </summary>
    internal static void SaveCurrentSlot()
    {
        SaveSlot(CurrentSlot);
    }

    /// <summary>
    /// Resetea completamente los datos de un slot al crear nueva partida o borrarla.
    /// </summary>
    internal static void ResetSlot(int slot)
    {
        if (slot < 0) return;

        try
        {
            string primaryPath = GetPrimaryFilePath(slot);
            string secondaryPath = GetSecondaryFilePath(slot);
            if (File.Exists(primaryPath)) File.Delete(primaryPath);
            if (File.Exists(secondaryPath)) File.Delete(secondaryPath);

            // Eliminar archivos legacy si quedaron
            string[] legacyFiles = new string[]
            {
                Path.Combine(MarkerDirectory, $"p2_inventory_slot{slot}.txt"),
                Path.Combine(MarkerDirectory, $"p2_skills_slot{slot}.txt"),
                Path.Combine(MarkerDirectory, $"p2_stats_slot{slot}.txt"),
                Path.Combine(MarkerDirectory, $"p2_deathstate_slot{slot}.txt"),
                Path.Combine(MarkerDirectory, $"p1_deathstate_slot{slot}.txt"),
            };
            foreach (var f in legacyFiles)
            {
                if (File.Exists(f)) File.Delete(f);
            }

            var data = new CoopSlotSaveData(slot);
            data.Reset();
            loadedSlots[slot] = data;
            SaveSlot(slot);

            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info($"[CoopSaveManager] Slot {slot} reseteado limpiamente.", Main.CoopLocal);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CoopSaveManager] Error al resetear slot {slot}: {ex.Message}");
        }
    }

    /// <summary>
    /// Aplica la configuración cooperativa del slot al motor de juego (jugadores, mandos, dificultad).
    /// </summary>
    internal static void ApplyConfigToGame(CoopSlotSaveData data)
    {
        if (data == null) return;

        CoopGameConfig.IsCoopEnabled = data.IsCoopEnabled;
        CoopGameConfig.EnableElementalUpgrades = data.EnableElementalUpgrades;
        CoopGameConfig.PlayerCount = Mathf.Clamp(data.PlayerCount, 1, 4);

        // Escalado de enemigos / jefes
        Combat.CoopEnemyScaling.ActiveEnemyMultiplier = data.EnemyMultiplier;
        Combat.CoopEnemyScaling.ActiveHealthMultiplier = data.EnemyHealthMultiplier;
        Combat.CoopEnemyScaling.ActiveBossResistanceFactor = data.BossResistanceFactor;

        // Configuración de cada jugador
        for (int i = 0; i < 4; i++)
        {
            var pData = data.GetPlayer(i);
            if (pData != null && i < CoopGameConfig.Slots.Length)
            {
                if (!string.IsNullOrEmpty(pData.Name)) CoopGameConfig.Slots[i].Name = pData.Name;
                CoopGameConfig.Slots[i].ColorIndex = Mathf.Clamp(pData.ColorIndex, 0, CoopGameConfig.Palette.Length - 1);
                CoopGameConfig.Slots[i].Device = pData.Device;
                if (!string.IsNullOrEmpty(pData.SkinId)) CoopGameConfig.Slots[i].SkinId = pData.SkinId;
            }
        }

        CoopGameConfig.ApplyToRegistry();
    }

    /// <summary>
    /// Sincroniza la configuración activa del juego hacia la estructura de datos del slot.
    /// </summary>
    internal static void SyncConfigFromGame(CoopSlotSaveData data)
    {
        if (data == null) return;

        data.IsCoopEnabled = CoopGameConfig.IsCoopEnabled;
        data.EnableElementalUpgrades = CoopGameConfig.EnableElementalUpgrades;
        data.PlayerCount = Mathf.Clamp(CoopGameConfig.PlayerCount, 1, 4);
        data.EnemyMultiplier = Combat.CoopEnemyScaling.ActiveEnemyMultiplier;
        data.EnemyHealthMultiplier = Combat.CoopEnemyScaling.ActiveHealthMultiplier;
        data.BossResistanceFactor = Combat.CoopEnemyScaling.ActiveBossResistanceFactor;

        for (int i = 0; i < 4; i++)
        {
            var pData = data.GetPlayer(i);
            if (i < CoopGameConfig.Slots.Length)
            {
                pData.Name = CoopGameConfig.Slots[i].Name;
                pData.ColorIndex = CoopGameConfig.Slots[i].ColorIndex;
                pData.Device = CoopGameConfig.Slots[i].Device;
                pData.SkinId = CoopGameConfig.Slots[i].SkinId;
            }
        }
    }

    // =========================================================================
    // PARSER & SERIALIZADOR INI/TEXT UNIFICADO
    // =========================================================================

    private static string SerializeSaveDataFile(CoopSlotSaveData data)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("# ========================================================");
        sb.AppendLine($"# Blasphemous Coop Mod - Datos Unificados de Ranura {data.SlotIndex + 1}");
        sb.AppendLine($"# Guardado: {data.LastSaved}");
        sb.AppendLine("# ========================================================");
        sb.AppendLine();

        // [GLOBAL]
        sb.AppendLine("[GLOBAL]");
        sb.AppendLine($"EnableElementalUpgrades={data.EnableElementalUpgrades}");
        sb.AppendLine();

        // [CoopConfig]
        sb.AppendLine("[CoopConfig]");
        sb.AppendLine($"IsCoopEnabled={data.IsCoopEnabled}");
        sb.AppendLine($"EnableElementalUpgrades={data.EnableElementalUpgrades}");
        sb.AppendLine($"PlayerCount={data.PlayerCount}");
        sb.AppendLine($"HasCompletedSetup={data.HasCompletedSetup}");
        sb.AppendLine($"EnemyMultiplier={data.EnemyMultiplier.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"EnemyHealthMultiplier={data.EnemyHealthMultiplier.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"BossResistanceFactor={data.BossResistanceFactor.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine();

        // [Player0] .. [Player3]
        for (int i = 0; i < data.Players.Length; i++)
        {
            var p = data.Players[i];
            sb.AppendLine($"[Player{i}]");
            sb.AppendLine($"Name={p.Name}");
            sb.AppendLine($"ColorIndex={p.ColorIndex}");
            sb.AppendLine($"Device={p.Device}");
            sb.AppendLine($"SkinId={p.SkinId}");
            sb.AppendLine($"PendingRevive={p.PendingRevive}");
            sb.AppendLine($"HasStatsSnapshot={p.HasStatsSnapshot}");
            sb.AppendLine($"LifeCurrent={p.CurrentLife.ToString(CultureInfo.InvariantCulture)}");
            sb.AppendLine($"FervourCurrent={p.CurrentFervour.ToString(CultureInfo.InvariantCulture)}");
            sb.AppendLine($"FlaskCurrent={p.CurrentFlask.ToString(CultureInfo.InvariantCulture)}");
            sb.AppendLine($"FlaskHealthBonus={p.FlaskHealthBonus.ToString(CultureInfo.InvariantCulture)}");
            sb.AppendLine($"PurgeCurrent={p.CurrentPurge.ToString(CultureInfo.InvariantCulture)}");

            // Permanent Bonuses
            List<string> bonuses = new List<string>();
            foreach (var kvp in p.PermanentBonuses)
            {
                bonuses.Add($"{kvp.Key}:{kvp.Value.ToString(CultureInfo.InvariantCulture)}");
            }
            sb.AppendLine($"PermanentBonuses={string.Join(",", bonuses.ToArray())}");

            // Skills
            List<string> unlockedSkills = new List<string>();
            foreach (var kvp in p.Skills)
            {
                if (kvp.Value) unlockedSkills.Add(kvp.Key);
            }
            sb.AppendLine($"Skills={string.Join(",", unlockedSkills.ToArray())}");

            // Inventory
            sb.AppendLine($"OwnedBeads={string.Join(",", new List<string>(p.OwnedBeads).ToArray())}");
            sb.AppendLine($"EquippedBeads={string.Join(",", p.EquippedBeads ?? new string[8])}");
            sb.AppendLine($"OwnedPrayers={string.Join(",", new List<string>(p.OwnedPrayers).ToArray())}");
            sb.AppendLine($"EquippedPrayer={p.EquippedPrayer ?? ""}");
            sb.AppendLine($"OwnedSwords={string.Join(",", new List<string>(p.OwnedSwords).ToArray())}");
            sb.AppendLine($"EquippedSword={p.EquippedSword ?? ""}");
            sb.AppendLine($"OwnedRelics={string.Join(",", new List<string>(p.OwnedRelics).ToArray())}");
            sb.AppendLine($"EquippedRelics={string.Join(",", p.EquippedRelics ?? new string[3])}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void ParseSaveDataFile(string path, CoopSlotSaveData data)
    {
        string[] lines = File.ReadAllLines(path);
        string currentSection = "";
        PlayerCoopData currentPlayer = null;

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

            // Secciones: [CoopConfig], [Player0], [Player1], etc.
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                currentSection = line.Substring(1, line.Length - 2).Trim();
                currentPlayer = null;

                if (currentSection.StartsWith("Player", StringComparison.OrdinalIgnoreCase))
                {
                    string idxStr = currentSection.Substring(6).Trim();
                    if (int.TryParse(idxStr, out int pIdx))
                    {
                        currentPlayer = data.GetPlayer(pIdx);
                    }
                }
                else if (currentSection.StartsWith("P", StringComparison.OrdinalIgnoreCase) && currentSection.Length == 2)
                {
                    int pNum = currentSection[1] - '0';
                    int pIdx = pNum >= 1 ? pNum - 1 : 0; // P1 -> 0, P2 -> 1
                    currentPlayer = data.GetPlayer(pIdx);
                }
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line.Substring(0, eq).Trim();
            string val = line.Substring(eq + 1).Trim();

            if (currentSection.Equals("CoopConfig", StringComparison.OrdinalIgnoreCase) ||
                currentSection.Equals("GLOBAL", StringComparison.OrdinalIgnoreCase))
            {
                if (key.Equals("IsCoopEnabled", StringComparison.OrdinalIgnoreCase))
                {
                    if (bool.TryParse(val, out bool b)) data.IsCoopEnabled = b;
                }
                else if (key.Equals("EnableElementalUpgrades", StringComparison.OrdinalIgnoreCase))
                {
                    if (bool.TryParse(val, out bool b)) data.EnableElementalUpgrades = b;
                }
                else if (key.Equals("PlayerCount", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(val, out int pc)) data.PlayerCount = Mathf.Clamp(pc, 1, 4);
                }
                else if (key.Equals("HasCompletedSetup", StringComparison.OrdinalIgnoreCase))
                {
                    if (bool.TryParse(val, out bool b)) data.HasCompletedSetup = b;
                }
                else if (key.Equals("EnemyMultiplier", StringComparison.OrdinalIgnoreCase))
                {
                    if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) data.EnemyMultiplier = f;
                }
                else if (key.Equals("EnemyHealthMultiplier", StringComparison.OrdinalIgnoreCase))
                {
                    if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) data.EnemyHealthMultiplier = f;
                }
                else if (key.Equals("BossResistanceFactor", StringComparison.OrdinalIgnoreCase))
                {
                    if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) data.BossResistanceFactor = f;
                }
            }
            else if (currentPlayer != null)
            {
                ParsePlayerKeyValue(currentPlayer, key, val);
            }
        }
    }

    private static void ParsePlayerKeyValue(PlayerCoopData p, string key, string val)
    {
        if (key.Equals("Name", StringComparison.OrdinalIgnoreCase)) p.Name = val;
        else if (key.Equals("ColorIndex", StringComparison.OrdinalIgnoreCase)) { if (int.TryParse(val, out int c)) p.ColorIndex = c; }
        else if (key.Equals("Device", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (int.TryParse(val, out int d)) p.Device = (CoopDeviceType)d;
                else p.Device = (CoopDeviceType)Enum.Parse(typeof(CoopDeviceType), val, true);
            }
            catch (Exception ex)
            {
                DashParryDebugLog.LogOnce($"CoopSaveManager.ParseDevice:{val}", $"[CoopSaveManager] Error parseando Device='{val}': {ex.Message}", isWarning: true);
            }
        }
        else if (key.Equals("SkinId", StringComparison.OrdinalIgnoreCase)) p.SkinId = val;
        else if (key.Equals("PendingRevive", StringComparison.OrdinalIgnoreCase)) { if (bool.TryParse(val, out bool b)) p.PendingRevive = b; }
        else if (key.Equals("HasStatsSnapshot", StringComparison.OrdinalIgnoreCase)) { if (bool.TryParse(val, out bool b)) p.HasStatsSnapshot = b; }
        else if (key.Equals("LifeCurrent", StringComparison.OrdinalIgnoreCase)) { if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) p.CurrentLife = f; }
        else if (key.Equals("FervourCurrent", StringComparison.OrdinalIgnoreCase)) { if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) p.CurrentFervour = f; }
        else if (key.Equals("FlaskCurrent", StringComparison.OrdinalIgnoreCase)) { if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) p.CurrentFlask = f; }
        else if (key.Equals("FlaskHealthBonus", StringComparison.OrdinalIgnoreCase)) { if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) p.FlaskHealthBonus = f; }
        else if (key.Equals("PurgeCurrent", StringComparison.OrdinalIgnoreCase)) { if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) p.CurrentPurge = Mathf.Clamp(f, 0f, 9999999f); }
        else if (key.Equals("PermanentBonuses", StringComparison.OrdinalIgnoreCase))
        {
            p.PermanentBonuses.Clear();
            string[] items = val.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var item in items)
            {
                string[] parts = item.Split(':');
                if (parts.Length == 2 && float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float bonus))
                {
                    p.PermanentBonuses[parts[0].Trim()] = bonus;
                }
            }
        }
        else if (key.Equals("Skills", StringComparison.OrdinalIgnoreCase))
        {
            string[] ids = val.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var id in ids)
            {
                string cleanId = id.Trim();
                if (!string.IsNullOrEmpty(cleanId)) p.Skills[cleanId] = true;
            }
        }
        else if (key.StartsWith("Skill.", StringComparison.OrdinalIgnoreCase))
        {
            string skillId = key.Substring(6).Trim();
            if (bool.TryParse(val, out bool b)) p.Skills[skillId] = b;
        }
        else if (key.Equals("OwnedBeads", StringComparison.OrdinalIgnoreCase))
        {
            p.OwnedBeads.Clear();
            string[] beads = val.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var b in beads) p.OwnedBeads.Add(b.Trim());
        }
        else if (key.Equals("EquippedBeads", StringComparison.OrdinalIgnoreCase))
        {
            string[] slots = val.Split(',');
            for (int i = 0; i < 8; i++)
            {
                p.EquippedBeads[i] = (i < slots.Length && !string.IsNullOrEmpty(slots[i].Trim())) ? slots[i].Trim() : null;
            }
        }
        else if (key.Equals("OwnedPrayers", StringComparison.OrdinalIgnoreCase))
        {
            p.OwnedPrayers.Clear();
            string[] prayers = val.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var pr in prayers) p.OwnedPrayers.Add(pr.Trim());
        }
        else if (key.Equals("EquippedPrayer", StringComparison.OrdinalIgnoreCase))
        {
            p.EquippedPrayer = string.IsNullOrEmpty(val) ? null : val;
        }
        else if (key.Equals("OwnedSwords", StringComparison.OrdinalIgnoreCase))
        {
            p.OwnedSwords.Clear();
            string[] swords = val.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var sw in swords)
            {
                string clean = sw.Trim().TrimStart(':');
                if (!string.IsNullOrEmpty(clean)) p.OwnedSwords.Add(clean);
            }
        }
        else if (key.Equals("EquippedSword", StringComparison.OrdinalIgnoreCase))
        {
            string clean = val.Trim().TrimStart(':');
            p.EquippedSword = string.IsNullOrEmpty(clean) ? null : clean;
        }
        else if (key.Equals("OwnedRelics", StringComparison.OrdinalIgnoreCase))
        {
            p.OwnedRelics.Clear();
            string[] relics = val.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var r in relics) p.OwnedRelics.Add(r.Trim());
        }
        else if (key.Equals("EquippedRelics", StringComparison.OrdinalIgnoreCase))
        {
            string[] slots = val.Split(',');
            for (int i = 0; i < 3; i++)
            {
                p.EquippedRelics[i] = (i < slots.Length && !string.IsNullOrEmpty(slots[i].Trim())) ? slots[i].Trim() : null;
            }
        }
        // Compatibilidad con sintaxis antigua si se copia o pega directamente
        else if (key.StartsWith("prayer:owned:"))
        {
            string id = key.Substring(13).Trim();
            if (val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase)) p.OwnedPrayers.Add(id);
        }
        else if (key.StartsWith("sword:owned:"))
        {
            string id = key.Substring(12).Trim().TrimStart(':');
            if (val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase)) p.OwnedSwords.Add(id);
        }
        else if (key.StartsWith("bead:owned:"))
        {
            string id = key.Substring(11).Trim();
            if (val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase)) p.OwnedBeads.Add(id);
        }
        else if (key.StartsWith("relic:owned:"))
        {
            string id = key.Substring(12).Trim();
            if (val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase)) p.OwnedRelics.Add(id);
        }
    }

    /// <summary>
    /// Migra datos existentes desde los archivos legacy sueltos (p2_inventory, p2_skills, p2_stats, p1/p2_deathstate).
    /// </summary>
    private static bool MigrateLegacyFilesIfPresent(int slot, CoopSlotSaveData data)
    {
        string p2Inv = Path.Combine(MarkerDirectory, $"p2_inventory_slot{slot}.txt");
        string p2Skills = Path.Combine(MarkerDirectory, $"p2_skills_slot{slot}.txt");
        string p2Stats = Path.Combine(MarkerDirectory, $"p2_stats_slot{slot}.txt");
        string p1Death = Path.Combine(MarkerDirectory, $"p1_deathstate_slot{slot}.txt");
        string p2Death = Path.Combine(MarkerDirectory, $"p2_deathstate_slot{slot}.txt");

        bool anyFile = File.Exists(p2Inv) || File.Exists(p2Skills) || File.Exists(p2Stats) || File.Exists(p1Death) || File.Exists(p2Death);
        if (!anyFile) return false;

        var p2 = data.GetPlayer(1);
        var p1 = data.GetPlayer(0);
        data.HasCompletedSetup = true;

        // 1. Migrar inventario P2
        if (File.Exists(p2Inv))
        {
            try
            {
                foreach (string line in File.ReadAllLines(p2Inv))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();

                    if (key.StartsWith("bead:owned:")) { if (val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase)) p2.OwnedBeads.Add(key.Substring(11)); }
                    else if (key.StartsWith("prayer:owned:")) { if (val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase)) p2.OwnedPrayers.Add(key.Substring(13)); }
                    else if (key.StartsWith("sword:owned:"))
                    {
                        string cleanId = key.Substring(12).Trim().TrimStart(':');
                        if (val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase)) p2.OwnedSwords.Add(cleanId);
                    }
                    else if (key.StartsWith("relic:owned:")) { if (val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase)) p2.OwnedRelics.Add(key.Substring(12)); }
                    else if (key.StartsWith("bead:slot:")) { int idx = int.Parse(key.Substring(10)); if (idx >= 0 && idx < 8) p2.EquippedBeads[idx] = string.IsNullOrEmpty(val) ? null : val; }
                    else if (key == "prayer:slot:0") p2.EquippedPrayer = string.IsNullOrEmpty(val) ? null : val;
                    else if (key == "sword:slot:0") p2.EquippedSword = string.IsNullOrEmpty(val) ? null : val.Trim().TrimStart(':');
                    else if (key.StartsWith("relic:slot:")) { int idx = int.Parse(key.Substring(11)); if (idx >= 0 && idx < 3) p2.EquippedRelics[idx] = string.IsNullOrEmpty(val) ? null : val; }
                }
            }
            catch (Exception ex) { Debug.LogError($"[CoopSaveManager] Error migrando p2Inv: {ex.Message}"); }
        }

        // 2. Migrar habilidades P2
        if (File.Exists(p2Skills))
        {
            try
            {
                foreach (string line in File.ReadAllLines(p2Skills))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    if (bool.TryParse(val, out bool b)) p2.Skills[key] = b;
                    else if (val == "1") p2.Skills[key] = true;
                    else if (val == "0") p2.Skills[key] = false;
                }
            }
            catch (Exception ex) { Debug.LogError($"[CoopSaveManager] Error migrando p2Skills: {ex.Message}"); }
        }

        // 3. Migrar atributos / stats P2
        if (File.Exists(p2Stats))
        {
            try
            {
                p2.HasStatsSnapshot = true;
                foreach (string line in File.ReadAllLines(p2Stats))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();

                    if (key == "__PurgeCurrent__") { if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) p2.CurrentPurge = Mathf.Clamp(f, 0f, 9999999f); }
                    else if (key == "__LifeCurrent__") { if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) p2.CurrentLife = f; }
                    else if (key == "__FervourCurrent__") { if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) p2.CurrentFervour = f; }
                    else if (key == "__FlaskCurrent__") { if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) p2.CurrentFlask = f; }
                    else if (key == "__FlaskHealthCurrent__") { if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) p2.FlaskHealthBonus = f; }
                    else
                    {
                        if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                        {
                            p2.PermanentBonuses[key] = f;
                        }
                    }
                }
            }
            catch (Exception ex) { Debug.LogError($"[CoopSaveManager] Error migrando p2Stats: {ex.Message}"); }
        }

        // 4. Migrar estados de muerte
        if (File.Exists(p1Death))
        {
            try
            {
                string txt = File.ReadAllText(p1Death);
                if (txt.IndexOf("PendingRevive=true", StringComparison.OrdinalIgnoreCase) >= 0) p1.PendingRevive = true;
            }
            catch { /* ignorado intencionalmente: migración legacy de deathstate opcional */ }
        }
        if (File.Exists(p2Death))
        {
            try
            {
                string txt = File.ReadAllText(p2Death);
                if (txt.IndexOf("PendingRevive=true", StringComparison.OrdinalIgnoreCase) >= 0) p2.PendingRevive = true;
            }
            catch { /* ignorado intencionalmente: migración legacy de deathstate opcional */ }
        }

        // Heredar configuración activa de mandos y colores
        SyncConfigFromGame(data);

        return true;
    }
}
