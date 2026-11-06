using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Blasphemous.CoopLocal;

public enum CoopDeviceType
{
    Unassigned = 0,
    Keyboard = 1,
    Gamepad1 = 2,
    Gamepad2 = 3,
    Gamepad3 = 4,
    Gamepad4 = 5,
}

[Serializable]
internal sealed class PlayerSlotConfig
{
    public int PlayerIndex;
    public string Name;
    public int ColorIndex;
    public CoopDeviceType Device;
    public string SkinId;

    public PlayerSlotConfig(int index, string name, int colorIndex, CoopDeviceType device, string skinId = "")
    {
        PlayerIndex = index;
        Name = name;
        ColorIndex = colorIndex;
        Device = device;
        SkinId = skinId;
    }
}

/// <summary>
/// Configuración persistente del modo cooperativo (habilitado, cantidad de jugadores,
/// asignación de mandos/teclado, nombres, colores y skins cosméticas de cada jugador).
/// </summary>
internal static class CoopGameConfig
{
    public static bool IsCoopEnabled = true;
    public static int PlayerCount = 2; // 1 a 4

    // Configuración visual del árbol elemental (posición y escala ajustables en vivo)
    public static float TreeOffsetX = -9999f; // Centinela: se inicializa según el árbol si no está en config
    public static float TreeOffsetY = 0f;
    public static float TreeScale = 1.10f;
    public static float SkillTextScale = 1.0f; // Escala del texto de descripciones ({ aumenta, } disminuye)

    public static readonly PlayerSlotConfig[] Slots = new PlayerSlotConfig[4]
    {
        new PlayerSlotConfig(0, "Pan-chan", 0, CoopDeviceType.Gamepad1, "PENITENT_DEFAULT"),
        new PlayerSlotConfig(1, "Baby",     1, CoopDeviceType.Keyboard, "PENITENT_ENDING_B"),
        new PlayerSlotConfig(2, "P3",       2, CoopDeviceType.Gamepad2, "PENITENT_ENDING_A"),
        new PlayerSlotConfig(3, "P4",       3, CoopDeviceType.Gamepad3, "PENITENT_OSSUARY"),
    };

    public struct PaletteEntry
    {
        public string Name;
        public Color32 Color;
        public Color32 Outline;

        public PaletteEntry(string name, Color32 color, Color32 outline)
        {
            Name = name;
            Color = color;
            Outline = outline;
        }
    }

    // Paleta de colores predefinida vibrante y contrastada
    public static readonly PaletteEntry[] Palette = new PaletteEntry[]
    {
        new PaletteEntry("Carmesí",  new Color32(235, 36, 10, 255),   new Color32(140, 14, 0, 190)),
        new PaletteEntry("Verde",    new Color32(55, 247, 70, 255),   new Color32(39, 94, 73, 190)),
        new PaletteEntry("Azul",     new Color32(65, 185, 255, 255),  new Color32(20, 60, 120, 190)),
        new PaletteEntry("Amarillo", new Color32(255, 215, 60, 255),  new Color32(120, 85, 15, 190)),
        new PaletteEntry("Púrpura",  new Color32(184, 77, 255, 255),  new Color32(90, 25, 140, 190)),
        new PaletteEntry("Naranja",  new Color32(255, 133, 38, 255),  new Color32(130, 55, 10, 190)),
        new PaletteEntry("Cian",     new Color32(0, 240, 255, 255),    new Color32(10, 95, 110, 190)),
        new PaletteEntry("Blanco",   new Color32(245, 245, 245, 255),  new Color32(60, 60, 60, 190)),
        new PaletteEntry("Rosa",     new Color32(255, 102, 178, 255),  new Color32(130, 30, 80, 190)),
    };

    public struct SkinDefinition
    {
        public string Id;
        public string Name;

        public SkinDefinition(string id, string name)
        {
            Id = id;
            Name = name;
        }
    }

    // Catálogo completo de skins oficiales de Blasphemous para selección en P2, P3 y P4
    public static readonly SkinDefinition[] AvailableSkins = new SkinDefinition[]
    {
        new SkinDefinition("PENITENT_DEFAULT",    "Original (Por Defecto)"),
        new SkinDefinition("PENITENT_ENDING_B",    "Verdadera Apostasía"),
        new SkinDefinition("PENITENT_ENDING_A",    "Ruta Inmisericorde"),
        new SkinDefinition("PENITENT_ENDING_C",    "Hijo del Milagro"),
        new SkinDefinition("PENITENT_OSSUARY",     "El Osario"),
        new SkinDefinition("PENITENT_BACKER",      "Aleación del Pecado (Mecenas)"),
        new SkinDefinition("PENITENT_DELUXE",      "Carga Dorada (Deluxe)"),
        new SkinDefinition("PENITENT_ALMS",        "Limosnero de la Iglesia"),
        new SkinDefinition("PENITENT_PE01",        "Corazón Sangrante"),
        new SkinDefinition("PENITENT_PE02",        "Fe Inquebrantable"),
        new SkinDefinition("PENITENT_PE03",        "Verdadera Culpa"),
        new SkinDefinition("PENITENT_BOSSRUSH",    "Canto de Sirena (Boss Rush)"),
        new SkinDefinition("PENITENT_BOSSRUSH_S",  "Fervor de Acero (Rango S)"),
        new SkinDefinition("PENITENT_DEMAKE",      "8-Bits (Alcázar del Silencio)"),
        new SkinDefinition("PENITENT_SIERPES",     "Sierpes"),
        new SkinDefinition("PENITENT_ISIDORA",     "Isidora"),
        new SkinDefinition("PENITENT_GAMEBOY",     "Game Boy (Demake 2)"),
        new SkinDefinition("PENITENT_KONAMI",      "Konami / Anónimo"),
    };

    public static int GetSkinIndex(string skinId)
    {
        if (string.IsNullOrEmpty(skinId)) return 0;
        for (int i = 0; i < AvailableSkins.Length; i++)
        {
            if (AvailableSkins[i].Id.Equals(skinId, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return 0;
    }

    public static string GetSkinDisplayName(string skinId)
    {
        int idx = GetSkinIndex(skinId);
        return AvailableSkins[idx].Name;
    }

    // Nombres sugeridos por ranura para ciclar rápidamente
    public static readonly string[][] SuggestedNames = new string[][]
    {
        new string[] { "Pan-chan", "Penitent", "Penitente 1", "El Silencioso", "Guardián", "Solitario", "Alba" },
        new string[] { "Baby", "Dark Penitent", "Penitente 2", "Hermano Oscuro", "Sombra", "Compañero", "Ocaso" },
        new string[] { "P3", "Peregrino", "Penitente 3", "Escriba", "Custodio", "Ceniza" },
        new string[] { "P4", "Cruzado", "Penitente 4", "Heraldo", "Martirio", "Milagro" }
    };

    private static string ConfigPath => Path.Combine(
        Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "",
        "coop_game_settings.cfg");

    static CoopGameConfig()
    {
        Load();
        ApplyToRegistry();
    }

    /// <summary>
    /// Asigna un dispositivo a un jugador garantizando que el Teclado solo lo posea uno solo.
    /// </summary>
    public static void AssignDevice(int playerIndex, CoopDeviceType device)
    {
        if (playerIndex < 0 || playerIndex >= Slots.Length) return;

        // Regla: "Teclado solo puede tenerlo uno solo"
        if (device == CoopDeviceType.Keyboard)
        {
            for (int i = 0; i < Slots.Length; i++)
            {
                if (i != playerIndex && Slots[i].Device == CoopDeviceType.Keyboard)
                {
                    Slots[i].Device = CoopDeviceType.Unassigned;
                }
            }
        }
        else if (device != CoopDeviceType.Unassigned)
        {
            // Si es un mando específico (ej. Gamepad 1), desasignarlo si otro lo tenía
            for (int i = 0; i < Slots.Length; i++)
            {
                if (i != playerIndex && Slots[i].Device == device)
                {
                    Slots[i].Device = CoopDeviceType.Unassigned;
                }
            }
        }

        Slots[playerIndex].Device = device;
        ApplyToRegistry();
        Save();
    }

    /// <summary>
    /// Aplica los nombres, colores y skins configurados a CoopPlayerRegistry y sincroniza los modos de input.
    /// </summary>
    public static void ApplyToRegistry()
    {
        for (int i = 0; i < Slots.Length; i++)
        {
            CoopPlayerSlot regSlot = CoopPlayerRegistry.GetSlot(i);
            if (regSlot != null)
            {
                if (!string.IsNullOrEmpty(Slots[i].Name))
                {
                    regSlot.DisplayName = Slots[i].Name;
                }
                int cIdx = Mathf.Clamp(Slots[i].ColorIndex, 0, Palette.Length - 1);
                regSlot.LabelColor = Palette[cIdx].Color;
                regSlot.OutlineColor = Palette[cIdx].Outline;

                if (!string.IsNullOrEmpty(Slots[i].SkinId))
                {
                    regSlot.SkinPaletteId = Slots[i].SkinId;
                    if (regSlot.Instance != null)
                    {
                        var swapper = regSlot.Instance.GetComponentInChildren<Gameplay.GameControllers.Effects.Player.Recolor.ColorPaletteSwapper>();
                        if (swapper != null)
                        {
                            ColorPaletteSwapper_ForcePlayer2TrueApostasy_Patch.ApplySkinToSwapper(swapper, Slots[i].SkinId);
                        }
                    }
                }
            }
        }

        // Sincronizar el modo de Player2Input (Keyboard vs Gamepad)
        if (Slots[1].Device == CoopDeviceType.Keyboard)
        {
            Player2Input.Mode = Player2InputMode.Keyboard;
        }
        else
        {
            Player2Input.Mode = Player2InputMode.Gamepad;
        }
    }

    public static void Save()
    {
        try
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"IsCoopEnabled={IsCoopEnabled}");
            sb.AppendLine($"PlayerCount={PlayerCount}");
            sb.AppendLine($"TreeOffsetX={TreeOffsetX.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            sb.AppendLine($"TreeOffsetY={TreeOffsetY.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            sb.AppendLine($"TreeScale={TreeScale.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            sb.AppendLine($"SkillTextScale={SkillTextScale.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            for (int i = 0; i < Slots.Length; i++)
            {
                sb.AppendLine($"P{i}_Name={Slots[i].Name}");
                sb.AppendLine($"P{i}_Color={Slots[i].ColorIndex}");
                sb.AppendLine($"P{i}_Device={(int)Slots[i].Device}");
                sb.AppendLine($"P{i}_Skin={Slots[i].SkinId}");
            }
            File.WriteAllText(ConfigPath, sb.ToString());
        }
        catch { }
    }

    public static void Load()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return;
            string[] lines = File.ReadAllLines(ConfigPath);
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                string[] parts = line.Split('=');
                if (parts.Length != 2) continue;
                string key = parts[0].Trim();
                string val = parts[1].Trim();

                if (key.Equals("IsCoopEnabled", StringComparison.OrdinalIgnoreCase))
                {
                    if (bool.TryParse(val, out bool b)) IsCoopEnabled = b;
                }
                else if (key.Equals("PlayerCount", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(val, out int pc)) PlayerCount = Mathf.Clamp(pc, 1, 4);
                }
                else if (key.Equals("TreeOffsetX", StringComparison.OrdinalIgnoreCase))
                {
                    if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ox)) TreeOffsetX = ox;
                }
                else if (key.Equals("TreeOffsetY", StringComparison.OrdinalIgnoreCase))
                {
                    if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float oy)) TreeOffsetY = oy;
                }
                else if (key.Equals("TreeScale", StringComparison.OrdinalIgnoreCase))
                {
                    if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sc)) TreeScale = Mathf.Clamp(sc, 0.3f, 3.0f);
                }
                else if (key.Equals("SkillTextScale", StringComparison.OrdinalIgnoreCase))
                {
                    if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ts)) SkillTextScale = Mathf.Clamp(ts, 0.5f, 3.5f);
                }
                else if (key.StartsWith("P") && key.Length >= 4)
                {
                    int pIdx = key[1] - '0';
                    if (pIdx >= 0 && pIdx < 4)
                    {
                        if (key.EndsWith("_Name"))
                        {
                            Slots[pIdx].Name = val;
                        }
                        else if (key.EndsWith("_Color"))
                        {
                            if (int.TryParse(val, out int c)) Slots[pIdx].ColorIndex = Mathf.Clamp(c, 0, Palette.Length - 1);
                        }
                        else if (key.EndsWith("_Device"))
                        {
                            if (int.TryParse(val, out int d)) Slots[pIdx].Device = (CoopDeviceType)Mathf.Clamp(d, 0, 5);
                        }
                        else if (key.EndsWith("_Skin"))
                        {
                            Slots[pIdx].SkinId = val;
                        }
                    }
                }
            }
        }
        catch { }
    }

    public static string GetDeviceDisplayName(CoopDeviceType dev)
    {
        return dev switch
        {
            CoopDeviceType.Keyboard => "TECLADO",
            CoopDeviceType.Gamepad1 => "MANDO 1",
            CoopDeviceType.Gamepad2 => "MANDO 2",
            CoopDeviceType.Gamepad3 => "MANDO 3",
            CoopDeviceType.Gamepad4 => "MANDO 4",
            _ => "[ Presiona Botón / Tecla ]"
        };
    }
}
