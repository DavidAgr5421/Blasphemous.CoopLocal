using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace Blasphemous.CoopLocal;

/// <summary>
/// Sistema de escala de texto de los menús del mod (pre-partida, unirse, config de partida, pausa por jugador, debug F8).
/// Cada menú tiene un multiplicador propio (más uno global). Se aplica a TODOS los Text hijos del canvas del menú,
/// recordando el tamaño base original, así que no hay que tocar el código de cada menú.
/// Editor en vivo: F4 (ventana IMGUI). Se guarda en coop_ui_fonts.cfg (persistentDataPath/CoopLocalMod).
/// </summary>
internal static class CoopUiFontSettings
{
    internal const float Min = 0.6f, Max = 2.5f, Step = 0.05f;

    // Índice 0 = global; el resto, un menú cada uno (ver MenuNames/CanvasNames: mismo orden).
    internal static readonly string[] MenuNames =
    {
        "Global (todos)",
        "Pre-partida (Setup)",
        "Unirse (Join)",
        "Config de partida",
        "Pausa por jugador",
        "Menú debug F8"
    };

    // Nombre del GameObject Canvas de cada menú (índice i+1 de MenuNames).
    internal static readonly string[] CanvasNames =
    {
        "CoopGameSetupCanvas",
        "JoinLobbyCanvas",
        "CoopMatchConfigCanvas",
        "CoopPauseCanvas",
        "CoopDebugCanvas"
    };

    internal static readonly float[] Scales = { 1f, 1f, 1f, 1f, 1f, 1f };

    // Defaults horneados: si calibras y guardas, copia aquí los valores de coop_ui_fonts.cfg.
    internal static readonly float[] Defaults = { 1.5f, 1.1f, 1f, 1f, 1f, 1f };

    private static bool loaded;

    private static string ConfigPath
    {
        get
        {
            try
            {
                string p = Application.persistentDataPath;
                if (string.IsNullOrEmpty(p)) return null;
                string dir = Path.Combine(p, "CoopLocalMod");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return Path.Combine(dir, "coop_ui_fonts.cfg");
            }
            catch { return null; }
        }
    }

    internal static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        Array.Copy(Defaults, Scales, Scales.Length);
        try
        {
            string path = ConfigPath;
            if (path == null || !File.Exists(path)) return;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0 || line.StartsWith("#")) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                if (!float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) continue;
                for (int i = 0; i < MenuNames.Length; i++)
                    if (key.Equals(KeyFor(i), StringComparison.OrdinalIgnoreCase))
                        Scales[i] = Mathf.Clamp(f, Min, Max);
            }
        }
        catch (Exception e) { Log("Error leyendo coop_ui_fonts.cfg: " + e.Message); }
    }

    internal static bool Save()
    {
        try
        {
            string path = ConfigPath;
            if (path == null) return false;
            var lines = new List<string> { "# Escala de texto de los menús del mod (1.0 = tamaño original). Editar con F4 en el juego." };
            for (int i = 0; i < MenuNames.Length; i++)
                lines.Add(KeyFor(i) + "=" + Scales[i].ToString("0.###", CultureInfo.InvariantCulture));
            File.WriteAllLines(path, lines.ToArray());
            return true;
        }
        catch (Exception e) { Log("Error guardando coop_ui_fonts.cfg: " + e.Message); return false; }
    }

    internal static void Reset()
    {
        Array.Copy(Defaults, Scales, Scales.Length);
    }

    private static string KeyFor(int i) => i == 0 ? "Global" : CanvasNames[i - 1].Replace("Canvas", "");

    internal static float EffectiveFor(int menuIndex) => Scales[0] * Scales[menuIndex];

    internal static void Log(string msg)
    {
        try { if (Main.CoopLocal != null) Blasphemous.ModdingAPI.ModLog.Info("[CoopUiFontScale] " + msg, Main.CoopLocal); } catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "CoopUiFontScale.cs:115"); }
    }
}

/// <summary>Aplica la escala a los Text de los canvases de los menús y ofrece el editor F4.</summary>
public class CoopUiFontScaler : MonoBehaviour
{
    private static CoopUiFontScaler _instance;
    public static bool IsOpen { get; private set; }

    private class TextInfo { public int baseSize; public int applied; }
    private readonly Dictionary<Text, TextInfo> infos = new Dictionary<Text, TextInfo>();
    private readonly List<Text> tmp = new List<Text>();
    private readonly List<KeyValuePair<Canvas, int>> canvases = new List<KeyValuePair<Canvas, int>>();
    private float canvasRefreshTimer, applyTimer, cleanTimer, statusTimer;
    private bool dirty = true;

    private int selected = 1;
    private string status = "";
    private Rect window = new Rect(16f, 100f, 400f, 330f);
    private const int WindowId = 574831;

    internal static void EnsureCreated()
    {
        if (_instance != null) return;
        CoopUiFontSettings.EnsureLoaded();
        var go = new GameObject("CoopUiFontScaler");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<CoopUiFontScaler>();
    }

    private void Update()
    {
        bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        if (Input.GetKeyDown(KeyCode.F4) && !alt) { IsOpen = !IsOpen; status = ""; }

        if (IsOpen)
        {
            if (Input.GetKeyDown(KeyCode.PageDown)) selected = (selected + 1) % CoopUiFontSettings.MenuNames.Length;
            if (Input.GetKeyDown(KeyCode.PageUp)) selected = (selected - 1 + CoopUiFontSettings.MenuNames.Length) % CoopUiFontSettings.MenuNames.Length;
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus)) Adjust(selected, -CoopUiFontSettings.Step);
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus)) Adjust(selected, CoopUiFontSettings.Step);
            if (Input.GetKeyDown(KeyCode.S) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))) SaveNow();
        }

        if (statusTimer > 0f) { statusTimer -= Time.unscaledDeltaTime; if (statusTimer <= 0f) status = ""; }

        canvasRefreshTimer -= Time.unscaledDeltaTime;
        if (canvasRefreshTimer <= 0f) { canvasRefreshTimer = 0.25f; RefreshCanvases(); }

        applyTimer -= Time.unscaledDeltaTime;
        if (dirty || applyTimer <= 0f) { applyTimer = 0.1f; ApplyAll(); dirty = false; }

        cleanTimer -= Time.unscaledDeltaTime;
        if (cleanTimer <= 0f) { cleanTimer = 5f; CleanDead(); }
    }

    private void Adjust(int idx, float delta)
    {
        float v = Mathf.Round((CoopUiFontSettings.Scales[idx] + delta) / CoopUiFontSettings.Step) * CoopUiFontSettings.Step;
        CoopUiFontSettings.Scales[idx] = Mathf.Clamp(v, CoopUiFontSettings.Min, CoopUiFontSettings.Max);
        dirty = true;
    }

    private void SaveNow()
    {
        SetStatus(CoopUiFontSettings.Save() ? "Guardado en coop_ui_fonts.cfg." : "ERROR al guardar (ver log).");
    }

    private void SetStatus(string s) { status = s; statusTimer = 4f; }

    private void RefreshCanvases()
    {
        canvases.Clear();
        var all = Resources.FindObjectsOfTypeAll<Canvas>();
        for (int i = 0; i < all.Length; i++)
        {
            Canvas c = all[i];
            if (c == null || !c.gameObject.scene.IsValid()) continue;
            string n = c.gameObject.name;
            for (int m = 0; m < CoopUiFontSettings.CanvasNames.Length; m++)
            {
                if (n == CoopUiFontSettings.CanvasNames[m]) { canvases.Add(new KeyValuePair<Canvas, int>(c, m + 1)); break; }
            }
        }
        // DontDestroyOnLoad objects report an invalid scene in some Unity versions: fall back to name match without scene check.
        if (canvases.Count == 0)
        {
            for (int i = 0; i < all.Length; i++)
            {
                Canvas c = all[i];
                if (c == null || c.hideFlags != HideFlags.None) continue;
                string n = c.gameObject.name;
                for (int m = 0; m < CoopUiFontSettings.CanvasNames.Length; m++)
                    if (n == CoopUiFontSettings.CanvasNames[m]) { canvases.Add(new KeyValuePair<Canvas, int>(c, m + 1)); break; }
            }
        }
    }

    private void ApplyAll()
    {
        for (int k = 0; k < canvases.Count; k++)
        {
            Canvas c = canvases[k].Key;
            if (c == null) continue;
            float scale = CoopUiFontSettings.EffectiveFor(canvases[k].Value);
            tmp.Clear();
            c.GetComponentsInChildren(true, tmp);
            for (int i = 0; i < tmp.Count; i++)
            {
                Text t = tmp[i];
                if (t == null) continue;
                if (!infos.TryGetValue(t, out TextInfo info))
                {
                    info = new TextInfo { baseSize = t.fontSize, applied = t.fontSize };
                    infos[t] = info;
                }
                else if (t.fontSize != info.applied)
                {
                    // El menú cambió el tamaño por su cuenta: es el nuevo tamaño base.
                    info.baseSize = t.fontSize;
                }
                int target = Mathf.Max(1, Mathf.RoundToInt(info.baseSize * scale));
                if (t.fontSize != target) t.fontSize = target;
                info.applied = target;
            }
        }
    }

    private void CleanDead()
    {
        List<Text> dead = null;
        foreach (var kv in infos)
            if (kv.Key == null) (dead ??= new List<Text>()).Add(kv.Key);
        if (dead != null) for (int i = 0; i < dead.Count; i++) infos.Remove(dead[i]);
    }

    // ---------------- Editor IMGUI ----------------
    private void OnGUI()
    {
        if (!IsOpen) return;
        window = GUILayout.Window(WindowId, window, DrawWindow, "Tamaño de texto de menús (F4)");
    }

    private void DrawWindow(int id)
    {
        GUILayout.Label("Av/Re Pág: elegir menú  |  - / + : tamaño  |  Ctrl+S: guardar\nAbre el menú que quieres ajustar y verás el cambio en vivo.");
        GUILayout.Space(4);
        for (int i = 0; i < CoopUiFontSettings.MenuNames.Length; i++)
        {
            GUILayout.BeginHorizontal();
            string mark = i == selected ? "► " : "   ";
            if (GUILayout.Button(mark + CoopUiFontSettings.MenuNames[i], GUILayout.Width(190))) selected = i;
            if (GUILayout.Button("-", GUILayout.Width(28))) { selected = i; Adjust(i, -CoopUiFontSettings.Step); }
            float v = GUILayout.HorizontalSlider(CoopUiFontSettings.Scales[i], CoopUiFontSettings.Min, CoopUiFontSettings.Max, GUILayout.Width(100));
            if (!Mathf.Approximately(v, CoopUiFontSettings.Scales[i])) { selected = i; Adjust(i, v - CoopUiFontSettings.Scales[i]); }
            if (GUILayout.Button("+", GUILayout.Width(28))) { selected = i; Adjust(i, CoopUiFontSettings.Step); }
            GUILayout.Label(CoopUiFontSettings.Scales[i].ToString("0.00") + "x", GUILayout.Width(44));
            GUILayout.EndHorizontal();
        }
        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(0.2f, 0.7f, 0.2f);
        if (GUILayout.Button("GUARDAR", GUILayout.Height(26))) SaveNow();
        GUI.backgroundColor = new Color(0.7f, 0.4f, 0.1f);
        if (GUILayout.Button("Restablecer todo", GUILayout.Height(26))) { CoopUiFontSettings.Reset(); dirty = true; SetStatus("Restablecido (sin guardar)."); }
        GUI.backgroundColor = new Color(0.7f, 0.2f, 0.2f);
        if (GUILayout.Button("Cerrar (F4)", GUILayout.Height(26))) IsOpen = false;
        GUI.backgroundColor = Color.white;
        GUILayout.EndHorizontal();
        if (!string.IsNullOrEmpty(status)) GUILayout.Label(status);
        GUI.DragWindow();
    }
}
