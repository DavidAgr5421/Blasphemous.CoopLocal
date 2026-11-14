using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Blasphemous.CoopLocal;

[Serializable]
public class HudElementPosition
{
    public float x;
    public float y;
    public float scale = 1f;

    public HudElementPosition() { }

    public HudElementPosition(float x, float y, float scale = 1f)
    {
        this.x = x;
        this.y = y;
        this.scale = scale;
    }

    [JsonIgnore]
    public Vector2 Position => new Vector2(x, y);
}

[Serializable]
public class CoopHudLayoutConfig
{
    public bool isCustomSaved = false;

    // --- Configuración P1 (Izquierda superior) ---
    public HudElementPosition p1_health = new HudElementPosition(-61.56955f, -22.6314621f, 0.9200001f);
    public HudElementPosition p1_fervour = new HudElementPosition(36.0f, -24.75f, 0.6800003f);
    public HudElementPosition p1_health_deco = new HudElementPosition(124.0f, 42.0f, 1.0f);
    public HudElementPosition p1_coins = new HudElementPosition(92.0f, -32.0f, 0.5500003f);

    // --- Configuración P2 (Derecha inferior) ---
    public HudElementPosition p2_health = new HudElementPosition(-113.0f, -305.0f, 0.65f);
    public HudElementPosition p2_fervour = new HudElementPosition(-66.0f, -297.75f, 0.65f);
    public HudElementPosition p2_health_deco = new HudElementPosition(124.0f, 42.0f, 1.0f);
    public HudElementPosition p2_coins = new HudElementPosition(-11.0f, -331.0f, 0.5900003f);

    // --- Configuración P3 (Izquierda inferior) ---
    public HudElementPosition p3_health = new HudElementPosition(-45.0f, -303.25f, 0.65f);
    public HudElementPosition p3_fervour = new HudElementPosition(2.0f, -295.75f, 0.65f);
    public HudElementPosition p3_health_deco = new HudElementPosition(124.0f, 42.0f, 1.0f);
    public HudElementPosition p3_coins = new HudElementPosition(81.0f, -330.0f, 0.5900003f);

    // --- Configuración P4 (Derecha superior) ---
    public HudElementPosition p4_health = new HudElementPosition(-113.25f, -8.0f, 0.65f);
    public HudElementPosition p4_fervour = new HudElementPosition(-69.0f, -1.25f, 0.63f);
    public HudElementPosition p4_health_deco = new HudElementPosition(124.0f, 42.0f, 1.0f);
    public HudElementPosition p4_coins = new HudElementPosition(-7.0f, -30.0f, 0.5300003f);

    // --- Configuración 2 Jugadores (Comportamiento original preservado) ---
    public HudElementPosition p2_health_2p = new HudElementPosition(-119.0f, -20.0f, 0.65f);
    public HudElementPosition p2_fervour_2p = new HudElementPosition(-75.0f, 7.0f, 0.65f);
    public HudElementPosition p2_coins_2p = new HudElementPosition(-170.0f, -8.0f, 1.0f);
    public HudElementPosition p2_coins_icon_2p = new HudElementPosition(5.0f, -21.0f, 1.0f);

    public HudElementPosition GetHealthConfig(int slotIndex)
    {
        switch (slotIndex)
        {
            case 0: return p1_health;
            case 1: return p2_health;
            case 2: return p3_health;
            case 3: return p4_health;
            default: return null;
        }
    }

    public HudElementPosition GetFervourConfig(int slotIndex)
    {
        switch (slotIndex)
        {
            case 0: return p1_fervour;
            case 1: return p2_fervour;
            case 2: return p3_fervour;
            case 3: return p4_fervour;
            default: return null;
        }
    }

    public HudElementPosition GetHealthDecoConfig(int slotIndex)
    {
        switch (slotIndex)
        {
            case 0: return p1_health_deco;
            case 1: return p2_health_deco;
            case 2: return p3_health_deco;
            case 3: return p4_health_deco;
            default: return null;
        }
    }

    public HudElementPosition GetCoinsConfig(int slotIndex)
    {
        switch (slotIndex)
        {
            case 0: return p1_coins;
            case 1: return p2_coins;
            case 2: return p3_coins;
            case 3: return p4_coins;
            default: return null;
        }
    }

    public void ValidateDefaults()
    {
        if (p1_health == null) p1_health = new HudElementPosition(-61.56955f, -22.6314621f, 0.9200001f);
        if (p1_fervour == null) p1_fervour = new HudElementPosition(36.0f, -24.75f, 0.6800003f);
        if (p1_health_deco == null) p1_health_deco = new HudElementPosition(124.0f, 42.0f, 1.0f);
        if (p1_coins == null) p1_coins = new HudElementPosition(92.0f, -32.0f, 0.5500003f);

        if (p2_health == null) p2_health = new HudElementPosition(-113.0f, -305.0f, 0.65f);
        if (p2_health.y > -150f && p2_fervour != null && p2_fervour.y < -200f)
        {
            p2_health.y = -305.0f;
        }
        if (p2_fervour == null) p2_fervour = new HudElementPosition(-66.0f, -297.75f, 0.65f);
        if (p2_health_deco == null) p2_health_deco = new HudElementPosition(124.0f, 42.0f, 1.0f);
        if (p2_coins == null) p2_coins = new HudElementPosition(-11.0f, -331.0f, 0.5900003f);

        if (p3_health == null) p3_health = new HudElementPosition(-45.0f, -303.25f, 0.65f);
        if (p3_fervour == null) p3_fervour = new HudElementPosition(2.0f, -295.75f, 0.65f);
        if (p3_health_deco == null) p3_health_deco = new HudElementPosition(124.0f, 42.0f, 1.0f);
        if (p3_coins == null) p3_coins = new HudElementPosition(81.0f, -330.0f, 0.5900003f);

        if (p4_health == null) p4_health = new HudElementPosition(-113.25f, -8.0f, 0.65f);
        if (p4_fervour == null) p4_fervour = new HudElementPosition(-69.0f, -1.25f, 0.63f);
        if (p4_health_deco == null) p4_health_deco = new HudElementPosition(124.0f, 42.0f, 1.0f);
        if (p4_coins == null) p4_coins = new HudElementPosition(-7.0f, -30.0f, 0.5300003f);

        if (p2_health_2p == null) p2_health_2p = new HudElementPosition(-119.0f, -20.0f, 0.65f);
        if (p2_fervour_2p == null) p2_fervour_2p = new HudElementPosition(-75.0f, 7.0f, 0.65f);
        if (p2_coins_2p == null) p2_coins_2p = new HudElementPosition(-170.0f, -8.0f, 1.0f);
        if (p2_coins_icon_2p == null) p2_coins_icon_2p = new HudElementPosition(5.0f, -21.0f, 1.0f);
    }
}

public static class HudLayoutConfig
{
    private static CoopHudLayoutConfig current;

    public static CoopHudLayoutConfig Current
    {
        get
        {
            if (current == null)
            {
                Load();
            }
            return current;
        }
    }

    public static string PrimaryConfigPath
    {
        get
        {
            try
            {
                string p = Application.persistentDataPath;
                if (!string.IsNullOrEmpty(p))
                {
                    return Path.Combine(Path.Combine(p, "CoopLocalMod"), "coop_hud_layout.json");
                }
            }
            catch { }
            return null;
        }
    }

    public static string SecondaryConfigPath
    {
        get
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrEmpty(baseDir))
                {
                    return Path.Combine(Path.Combine(Path.Combine(baseDir, "Modding"), "config"), "coop_hud_layout.json");
                }
            }
            catch { }
            try
            {
                string curDir = Directory.GetCurrentDirectory();
                if (!string.IsNullOrEmpty(curDir))
                {
                    return Path.Combine(Path.Combine(Path.Combine(curDir, "Modding"), "config"), "coop_hud_layout.json");
                }
            }
            catch { }
            return null;
        }
    }

    public static void Load()
    {
        string path = null;
        string primary = PrimaryConfigPath;
        if (!string.IsNullOrEmpty(primary) && File.Exists(primary))
        {
            path = primary;
        }
        else
        {
            string secondary = SecondaryConfigPath;
            if (!string.IsNullOrEmpty(secondary) && File.Exists(secondary))
            {
                path = secondary;
            }
        }

        if (path != null)
        {
            try
            {
                string json = File.ReadAllText(path);
                current = JsonConvert.DeserializeObject<CoopHudLayoutConfig>(json);
                if (current != null)
                {
                    current.ValidateDefaults();
                    if (Main.CoopLocal != null)
                    {
                        Blasphemous.ModdingAPI.ModLog.Info($"[HudLayout] Layout cargado exitosamente desde {path}", Main.CoopLocal);
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                DashParryDebugLog.Log($"[HudLayout] Error al leer layout desde {path}: {ex.Message}");
            }
        }

        // Si no existe, crear con valores por defecto
        current = new CoopHudLayoutConfig();
        current.ValidateDefaults();
    }

    public static void Save()
    {
        if (current == null) current = new CoopHudLayoutConfig();
        current.ValidateDefaults();
        current.isCustomSaved = true;

        try
        {
            string json = JsonConvert.SerializeObject(current, Formatting.Indented);

            string primary = PrimaryConfigPath;
            if (!string.IsNullOrEmpty(primary))
            {
                string primaryDir = Path.GetDirectoryName(primary);
                if (!string.IsNullOrEmpty(primaryDir) && !Directory.Exists(primaryDir))
                {
                    Directory.CreateDirectory(primaryDir);
                }
                File.WriteAllText(primary, json);
            }

            string secondary = SecondaryConfigPath;
            if (!string.IsNullOrEmpty(secondary))
            {
                string secDir = Path.GetDirectoryName(secondary);
                if (!string.IsNullOrEmpty(secDir) && Directory.Exists(secDir))
                {
                    File.WriteAllText(secondary, json);
                }
            }

            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info($"[HudLayout] Layout guardado exitosamente en {PrimaryConfigPath}", Main.CoopLocal);
            }
            DashParryDebugLog.Log($"[HudLayout] Layout guardado en {PrimaryConfigPath}");
        }
        catch (Exception ex)
        {
            DashParryDebugLog.Log($"[HudLayout] Error al guardar layout: {ex.Message}");
        }
    }
}
