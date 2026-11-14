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
    public bool isCustomSaved = true; // los defaults son el layout calibrado y horneado

    // --- Configuración P1 (Izquierda superior) ---
    public HudElementPosition p1_health = new HudElementPosition(-69.06955f, -26.3814621f, 1.0f);
    public HudElementPosition p1_fervour = new HudElementPosition(39.0f, -24.0f, 0.7000003f);
    public HudElementPosition p1_health_deco = new HudElementPosition(124.0f, 42.0f, 1.0f);
    public HudElementPosition p1_coins = new HudElementPosition(52.0f, -30.0f, 0.6300002f);

    // --- Configuración P2 (Derecha inferior) ---
    public HudElementPosition p2_health = new HudElementPosition(-121.25f, -312.0f, 0.65f);
    public HudElementPosition p2_fervour = new HudElementPosition(-79.0f, -304.25f, 0.65f);
    public HudElementPosition p2_health_deco = new HudElementPosition(124.0f, 42.0f, 1.0f);
    public HudElementPosition p2_coins = new HudElementPosition(-4.0f, -343.0f, 0.5900003f);

    // --- Configuración P3 (Izquierda inferior) ---
    public HudElementPosition p3_health = new HudElementPosition(-42.0f, -317.75f, 0.65f);
    public HudElementPosition p3_fervour = new HudElementPosition(3.0f, -310.75f, 0.65f);
    public HudElementPosition p3_health_deco = new HudElementPosition(124.0f, 42.0f, 1.0f);
    public HudElementPosition p3_coins = new HudElementPosition(81.0f, -330.0f, 0.5900003f);

    // --- Configuración P4 (Derecha superior) ---
    public HudElementPosition p4_health = new HudElementPosition(-113.25f, -7.75f, 0.65f);
    public HudElementPosition p4_fervour = new HudElementPosition(-70.0f, -1.25f, 0.63f);
    public HudElementPosition p4_health_deco = new HudElementPosition(124.0f, 42.0f, 1.0f);
    public HudElementPosition p4_coins = new HudElementPosition(-4.0f, -33.0f, 0.5900003f);

    // --- Configuración 2 Jugadores (Comportamiento original preservado) ---
    public HudElementPosition p2_health_2p = new HudElementPosition(-119.0f, -18.25f, 0.65f);
    public HudElementPosition p2_fervour_2p = new HudElementPosition(-75.0f, 8.0f, 0.65f);
    public HudElementPosition p2_coins_2p = new HudElementPosition(-170.0f, -7.0f, 1.0f);
    public HudElementPosition p2_coins_icon_2p = new HudElementPosition(5.0f, -21.0f, 1.0f);

    // Capas (orden de dibujo) de los 16 elementos del editor F7: [jugador*4 + (0 vida, 1 fervor, 2 dec. vida, 3 monedas)].
    // Mayor valor = dibujado por encima. 0 = orden natural. Solo reordena entre hermanos del mismo canvas.
    public int[] layers = new int[16];

    public int GetLayer(int elementIndex)
    {
        if (layers == null || elementIndex < 0 || elementIndex >= layers.Length) return 0;
        return layers[elementIndex];
    }

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
        if (layers == null) layers = new int[16];
        else if (layers.Length != 16) System.Array.Resize(ref layers, 16);

        if (p1_health == null) p1_health = new HudElementPosition(-69.06955f, -26.3814621f, 1.0f);
        if (p1_fervour == null) p1_fervour = new HudElementPosition(39.0f, -24.0f, 0.7000003f);
        if (p1_health_deco == null) p1_health_deco = new HudElementPosition(124.0f, 42.0f, 1.0f);
        if (p1_coins == null) p1_coins = new HudElementPosition(52.0f, -30.0f, 0.6300002f);

        if (p2_health == null) p2_health = new HudElementPosition(-121.25f, -312.0f, 0.65f);
        if (p2_health.y > -150f && p2_fervour != null && p2_fervour.y < -200f)
        {
            p2_health.y = -305.0f;
        }
        if (p2_fervour == null) p2_fervour = new HudElementPosition(-79.0f, -304.25f, 0.65f);
        if (p2_health_deco == null) p2_health_deco = new HudElementPosition(124.0f, 42.0f, 1.0f);
        if (p2_coins == null) p2_coins = new HudElementPosition(-4.0f, -343.0f, 0.5900003f);

        if (p3_health == null) p3_health = new HudElementPosition(-42.0f, -317.75f, 0.65f);
        if (p3_fervour == null) p3_fervour = new HudElementPosition(3.0f, -310.75f, 0.65f);
        if (p3_health_deco == null) p3_health_deco = new HudElementPosition(124.0f, 42.0f, 1.0f);
        if (p3_coins == null) p3_coins = new HudElementPosition(81.0f, -330.0f, 0.5900003f);

        if (p4_health == null) p4_health = new HudElementPosition(-113.25f, -7.75f, 0.65f);
        if (p4_fervour == null) p4_fervour = new HudElementPosition(-70.0f, -1.25f, 0.63f);
        if (p4_health_deco == null) p4_health_deco = new HudElementPosition(124.0f, 42.0f, 1.0f);
        if (p4_coins == null) p4_coins = new HudElementPosition(-4.0f, -33.0f, 0.5900003f);

        if (p2_health_2p == null) p2_health_2p = new HudElementPosition(-119.0f, -18.25f, 0.65f);
        if (p2_fervour_2p == null) p2_fervour_2p = new HudElementPosition(-75.0f, 8.0f, 0.65f);
        if (p2_coins_2p == null) p2_coins_2p = new HudElementPosition(-170.0f, -7.0f, 1.0f);
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
            catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "HudLayoutConfig.cs:185"); }
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
            catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "HudLayoutConfig.cs:202"); }
            try
            {
                string curDir = Directory.GetCurrentDirectory();
                if (!string.IsNullOrEmpty(curDir))
                {
                    return Path.Combine(Path.Combine(Path.Combine(curDir, "Modding"), "config"), "coop_hud_layout.json");
                }
            }
            catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "HudLayoutConfig.cs:211"); }
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
