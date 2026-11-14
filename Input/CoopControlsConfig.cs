using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Blasphemous.CoopLocal;

public enum CoopAction
{
    Up,
    Down,
    Left,
    Right,
    Attack,
    Jump,
    Dash,
    Parry,
    Heal,
    Interact,
    Prayer,
    Pause,
    Menu
}

[Serializable]
public class PlayerControlBindings
{
    // Mapeo para teclado
    public KeyCode KeyUp = KeyCode.W;
    public KeyCode KeyDown = KeyCode.S;
    public KeyCode KeyLeft = KeyCode.A;
    public KeyCode KeyRight = KeyCode.D;
    public KeyCode KeyAttack = KeyCode.K;
    public KeyCode KeyJump = KeyCode.Space;
    public KeyCode KeyDash = KeyCode.LeftShift;
    public KeyCode KeyParry = KeyCode.J;
    public KeyCode KeyHeal = KeyCode.R;
    public KeyCode KeyInteract = KeyCode.E;
    public KeyCode KeyPrayer = KeyCode.Q;
    public KeyCode KeyPause = KeyCode.Escape;
    public KeyCode KeyMenu = KeyCode.I;

    // Mapeo para mando (nombres de botones/ejes de Rewired)
    public string PadAttack = "X Button";
    public string PadJump = "A Button";
    public string PadDash = "Right Trigger";
    public string PadParry = "Left Bumper";
    public string PadHeal = "Right Bumper";
    public string PadInteract = "Y Button";
    public string PadPrayer = "Left Trigger";
    public string PadPause = "Start";
    public string PadMenu = "Back";

    public KeyCode GetKey(CoopAction action)
    {
        switch (action)
        {
            case CoopAction.Up: return KeyUp;
            case CoopAction.Down: return KeyDown;
            case CoopAction.Left: return KeyLeft;
            case CoopAction.Right: return KeyRight;
            case CoopAction.Attack: return KeyAttack;
            case CoopAction.Jump: return KeyJump;
            case CoopAction.Dash: return KeyDash;
            case CoopAction.Parry: return KeyParry;
            case CoopAction.Heal: return KeyHeal;
            case CoopAction.Interact: return KeyInteract;
            case CoopAction.Prayer: return KeyPrayer;
            case CoopAction.Pause: return KeyPause;
            case CoopAction.Menu: return KeyMenu;
            default: return KeyCode.None;
        }
    }

    public void SetKey(CoopAction action, KeyCode key)
    {
        switch (action)
        {
            case CoopAction.Up: KeyUp = key; break;
            case CoopAction.Down: KeyDown = key; break;
            case CoopAction.Left: KeyLeft = key; break;
            case CoopAction.Right: KeyRight = key; break;
            case CoopAction.Attack: KeyAttack = key; break;
            case CoopAction.Jump: KeyJump = key; break;
            case CoopAction.Dash: KeyDash = key; break;
            case CoopAction.Parry: KeyParry = key; break;
            case CoopAction.Heal: KeyHeal = key; break;
            case CoopAction.Interact: KeyInteract = key; break;
            case CoopAction.Prayer: KeyPrayer = key; break;
            case CoopAction.Pause: KeyPause = key; break;
            case CoopAction.Menu: KeyMenu = key; break;
        }
    }

    public string GetPadButton(CoopAction action)
    {
        switch (action)
        {
            case CoopAction.Attack: return PadAttack;
            case CoopAction.Jump: return PadJump;
            case CoopAction.Dash: return PadDash;
            case CoopAction.Parry: return PadParry;
            case CoopAction.Heal: return PadHeal;
            case CoopAction.Interact: return PadInteract;
            case CoopAction.Prayer: return PadPrayer;
            case CoopAction.Pause: return PadPause;
            case CoopAction.Menu: return PadMenu;
            default: return "";
        }
    }

    public void SetPadButton(CoopAction action, string buttonName)
    {
        switch (action)
        {
            case CoopAction.Attack: PadAttack = buttonName; break;
            case CoopAction.Jump: PadJump = buttonName; break;
            case CoopAction.Dash: PadDash = buttonName; break;
            case CoopAction.Parry: PadParry = buttonName; break;
            case CoopAction.Heal: PadHeal = buttonName; break;
            case CoopAction.Interact: PadInteract = buttonName; break;
            case CoopAction.Prayer: PadPrayer = buttonName; break;
            case CoopAction.Pause: PadPause = buttonName; break;
            case CoopAction.Menu: PadMenu = buttonName; break;
        }
    }
}

[Serializable]
public class CoopAllControlsData
{
    public Dictionary<int, PlayerControlBindings> Slots = new Dictionary<int, PlayerControlBindings>();
}

public static class CoopControlsConfig
{
    private static CoopAllControlsData _data;

    public static string ActionDisplayName(CoopAction action)
    {
        switch (action)
        {
            case CoopAction.Up: return "Mover Arriba / Subir";
            case CoopAction.Down: return "Mover Abajo / Agacharse";
            case CoopAction.Left: return "Mover Izquierda";
            case CoopAction.Right: return "Mover Derecha";
            case CoopAction.Attack: return "Ataque con Espada";
            case CoopAction.Jump: return "Salto";
            case CoopAction.Dash: return "Esquivar / Deslizarse";
            case CoopAction.Parry: return "Bloqueo / Parada";
            case CoopAction.Heal: return "Matraz de Bilis";
            case CoopAction.Interact: return "Interactuar / Hablar";
            case CoopAction.Prayer: return "Activar Rezo";
            case CoopAction.Pause: return "Pausa / Opciones";
            case CoopAction.Menu: return "Menú de Inventario / Habilidades";
            default: return action.ToString();
        }
    }

    public static string KeyDisplayName(KeyCode key)
    {
        switch (key)
        {
            case KeyCode.Space: return "ESPACIO";
            case KeyCode.LeftShift: return "SHIFT IZQ";
            case KeyCode.RightShift: return "SHIFT DER";
            case KeyCode.LeftControl: return "CTRL IZQ";
            case KeyCode.RightControl: return "CTRL DER";
            case KeyCode.LeftAlt: return "ALT IZQ";
            case KeyCode.RightAlt: return "ALT DER";
            case KeyCode.Return: return "ENTER";
            case KeyCode.Escape: return "ESC";
            case KeyCode.Backspace: return "RETROCESO";
            case KeyCode.Tab: return "TAB";
            case KeyCode.UpArrow: return "FLECHA ARRIBA";
            case KeyCode.DownArrow: return "FLECHA ABAJO";
            case KeyCode.LeftArrow: return "FLECHA IZQ";
            case KeyCode.RightArrow: return "FLECHA DER";
            default: return key.ToString().ToUpperInvariant();
        }
    }

    public static string PadButtonDisplayName(string padName)
    {
        if (string.IsNullOrEmpty(padName)) return "(Sin asignar)";
        string lower = padName.ToLowerInvariant();
        if (lower.Contains("cross") || lower == "a" || lower.Contains("a button")) return "A (Cruz)";
        if (lower.Contains("circle") || lower == "b" || lower.Contains("b button")) return "B (Círculo)";
        if (lower.Contains("square") || lower == "x" || lower.Contains("x button")) return "X (Cuadrado)";
        if (lower.Contains("triangle") || lower == "y" || lower.Contains("y button")) return "Y (Triángulo)";
        if (lower.Contains("left bumper") || lower.Contains("left shoulder") || lower == "lb" || lower == "l1") return "LB / L1";
        if (lower.Contains("right bumper") || lower.Contains("right shoulder") || lower == "rb" || lower == "r1") return "RB / R1";
        if (lower.Contains("left trigger") || lower == "lt" || lower == "l2") return "LT / L2 (Gatillo)";
        if (lower.Contains("right trigger") || lower == "rt" || lower == "r2") return "RT / R2 (Gatillo)";
        if (lower.Contains("start") || lower.Contains("options")) return "START / OPCIONES";
        if (lower.Contains("back") || lower.Contains("select") || lower.Contains("view") || lower.Contains("share") || lower.Contains("touchpad")) return "BACK / SELECT";
        if (lower.Contains("menu")) return "MENÚ / START";
        if (lower.Contains("left stick") || lower.Contains("l3")) return "STICK IZQUIERDO";
        if (lower.Contains("right stick") || lower.Contains("r3")) return "STICK DERECHO";
        if (lower.Contains("dpad") || lower.Contains("d-pad")) return padName.ToUpperInvariant();
        return padName.ToUpperInvariant();
    }

    public static PlayerControlBindings GetBindings(int slotIndex)
    {
        EnsureLoaded();
        if (!_data.Slots.TryGetValue(slotIndex, out var bindings) || bindings == null)
        {
            bindings = new PlayerControlBindings();
            _data.Slots[slotIndex] = bindings;
        }
        return bindings;
    }

    public static void ResetBindingsToDefault(int slotIndex)
    {
        EnsureLoaded();
        _data.Slots[slotIndex] = new PlayerControlBindings();
        Save();
    }

    private static string PrimaryPath
    {
        get
        {
            try
            {
                string p = Application.persistentDataPath;
                if (!string.IsNullOrEmpty(p))
                {
                    return Path.Combine(Path.Combine(p, "CoopLocalMod"), "coop_controls.json");
                }
            }
            catch { }
            return null;
        }
    }

    private static string SecondaryPath
    {
        get
        {
            try
            {
                string b = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrEmpty(b))
                {
                    return Path.Combine(Path.Combine(Path.Combine(b, "Modding"), "config"), "coop_controls.json");
                }
            }
            catch { }
            return null;
        }
    }

    public static void EnsureLoaded()
    {
        if (_data != null) return;
        _data = new CoopAllControlsData();

        string path = PrimaryPath;
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path);
                var loaded = JsonConvert.DeserializeObject<CoopAllControlsData>(json);
                if (loaded != null && loaded.Slots != null)
                {
                    _data = loaded;
                    return;
                }
            }
            catch (Exception ex)
            {
                DashParryDebugLog.Log($"[CoopControlsConfig] Error cargando desde {path}: {ex.Message}");
            }
        }

        string sec = SecondaryPath;
        if (!string.IsNullOrEmpty(sec) && File.Exists(sec))
        {
            try
            {
                string json = File.ReadAllText(sec);
                var loaded = JsonConvert.DeserializeObject<CoopAllControlsData>(json);
                if (loaded != null && loaded.Slots != null)
                {
                    _data = loaded;
                    return;
                }
            }
            catch { }
        }

        // Crear valores iniciales por defecto para los slots 0, 1, 2, 3
        for (int i = 0; i < 4; i++)
        {
            _data.Slots[i] = new PlayerControlBindings();
        }
    }

    public static void Save()
    {
        if (_data == null) EnsureLoaded();
        try
        {
            string json = JsonConvert.SerializeObject(_data, Formatting.Indented);
            string prim = PrimaryPath;
            if (!string.IsNullOrEmpty(prim))
            {
                string dir = Path.GetDirectoryName(prim);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(prim, json);
            }

            string sec = SecondaryPath;
            if (!string.IsNullOrEmpty(sec))
            {
                string dir = Path.GetDirectoryName(sec);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(sec, json);
            }
        }
        catch (Exception ex)
        {
            DashParryDebugLog.Log($"[CoopControlsConfig] Error guardando config: {ex.Message}");
        }
    }
}
