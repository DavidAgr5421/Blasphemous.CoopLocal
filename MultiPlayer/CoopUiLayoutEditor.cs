using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Blasphemous.CoopLocal;

/// <summary>
/// Editor de layout en vivo de los menús del mod (pre-partida, unirse, config de partida, pausa, debug F8).
/// F11 abre/cierra. Permite elegir cualquier elemento del menú visible y moverlo / escalarlo (el texto se escala con él).
///
///  - Clic DERECHO sobre el menú: selecciona el elemento bajo el cursor; mantener y arrastrar lo mueve.
///  - Re/Av Pág: elemento anterior/siguiente  |  Ctrl+flechas o teclado numérico 4/6/8/2: mover  |  Ctrl+(+/-) o numérico +/-: escalar
///  - R: restablecer el elemento  |  Ctrl+S: guardar  |  botón "Copiar" deja las líneas listas para pegar en BakedDefaults.
/// Los ajustes se guardan en coop_ui_layout.cfg (persistentDataPath/CoopLocalMod) y se reaplican cada vez que el menú se muestra.
/// Complementa a F4 (tamaño de fuente por menú): aquí es posición y escala por elemento.
/// </summary>
public class CoopUiLayoutEditor : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    // Pega aquí las líneas copiadas con el botón "Copiar": clave \t dx \t dy \t escala
    private static readonly string[] BakedDefaults =
    {
        "JoinLobbyCanvas/Panel/Row_3/Value	16	-1	1",
        "JoinLobbyCanvas/Panel/Row_0/Label	-46	0	1",
        "JoinLobbyCanvas/Panel/Row_1/Label	-43	-1	1",
        "JoinLobbyCanvas/Panel/Row_2/Label	-26	-2	1",
        "CoopMatchConfigCanvas/MainPanel/RoomCodeBanner/CopyTip	-229	-4	1",
        "JoinLobbyCanvas/Panel/Row_3/Label	-25	0	1",
        "CoopMatchConfigCanvas/MainPanel/RoomCodeBanner/Value	88	-9	1.25",
        "CoopMatchConfigCanvas/MainPanel/RoomCodeBanner/Label	-129	1	1",
    };

    private class Ov { public float dx, dy, scale = 1f; public bool IsIdentity => Mathf.Abs(dx) < 0.01f && Mathf.Abs(dy) < 0.01f && Mathf.Abs(scale - 1f) < 0.0005f; }

    private class Info
    {
        public string key;
        public int depth;
        public Vector2 basePos, appliedPos;
        public Vector3 baseScale, appliedScale;
        public bool modified;
    }

    private static CoopUiLayoutEditor _instance;
    private readonly Dictionary<string, Ov> overrides = new Dictionary<string, Ov>();
    private readonly Dictionary<RectTransform, Info> infos = new Dictionary<RectTransform, Info>();
    private readonly List<RectTransform> elements = new List<RectTransform>();   // elementos visibles del canvas activo
    private readonly List<RectTransform> tmp = new List<RectTransform>();
    private readonly List<KeyValuePair<Canvas, string>> canvases = new List<KeyValuePair<Canvas, string>>();
    private readonly List<RectTransform> applyList = new List<RectTransform>();  // elementos con override aplicado
    private Canvas activeCanvas;
    private string activeCanvasName = "";

    private RectTransform selected;
    private bool dragging;
    private Vector2 lastMouse;
    private float refreshTimer, statusTimer;
    private bool dirty = true;
    private string status = "";
    private string filter = "";
    private Vector2 scroll;
    private int stepIdx = 1;
    private static readonly float[] Steps = { 1f, 5f, 20f };
    private Rect window = new Rect(16f, 60f, 430f, 560f);
    private const int WindowId = 574832;
    private Texture2D pixel;

    private static readonly string[] CanvasNames =
    {
        "CoopGameSetupCanvas", "JoinLobbyCanvas", "CoopMatchConfigCanvas", "CoopPauseCanvas", "CoopDebugCanvas"
    };

    internal static void EnsureCreated()
    {
        if (_instance != null) return;
        var go = new GameObject("CoopUiLayoutEditor");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<CoopUiLayoutEditor>();
        _instance.LoadAll();
    }

    /// <summary>Los menús lo llaman al mostrarse (vía el aviso del escalador de fuentes) para reaplicar los ajustes en el acto.</summary>
    internal static void NotifyMenuShown()
    {
        if (_instance == null) return;
        _instance.dirty = true;
        _instance.RefreshNow();
    }

    // ------------------------------------------------------------------
    // Ciclo de vida
    // ------------------------------------------------------------------
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F11) && CoopSettings.Ui) { IsOpen = !IsOpen; dragging = false; if (IsOpen) RefreshNow(); }

        if (statusTimer > 0f) { statusTimer -= Time.unscaledDeltaTime; if (statusTimer <= 0f) status = ""; }

        refreshTimer -= Time.unscaledDeltaTime;
        if (dirty || refreshTimer <= 0f) { RefreshNow(); }

        if (!IsOpen) return;
        HandleKeys();
        HandleMouse();
    }

    private void LateUpdate()
    {
        // Los LayoutGroup / animaciones del menú pueden recolocar elementos: se reimponen los ajustes cada frame
        // (solo los elementos con override, así que el coste es mínimo).
        for (int i = 0; i < applyList.Count; i++)
        {
            RectTransform rt = applyList[i];
            if (rt == null) continue;
            if (infos.TryGetValue(rt, out Info info)) Apply(rt, info);
        }
    }

    private void RefreshNow()
    {
        refreshTimer = IsOpen ? 0.5f : 1.5f;
        dirty = false;

        canvases.Clear();
        Canvas[] all = Resources.FindObjectsOfTypeAll<Canvas>();
        for (int i = 0; i < all.Length; i++)
        {
            Canvas c = all[i];
            if (c == null || c.hideFlags != HideFlags.None) continue;
            string n = c.gameObject.name;
            for (int m = 0; m < CanvasNames.Length; m++)
                if (n == CanvasNames[m]) { canvases.Add(new KeyValuePair<Canvas, string>(c, n)); break; }
        }

        activeCanvas = null;
        activeCanvasName = "";
        elements.Clear();
        applyList.Clear();

        for (int k = 0; k < canvases.Count; k++)
        {
            Canvas c = canvases[k].Key;
            if (c == null || !c.gameObject.activeInHierarchy) continue;
            bool first = activeCanvas == null;
            if (first) { activeCanvas = c; activeCanvasName = canvases[k].Value; }

            tmp.Clear();
            c.GetComponentsInChildren(true, tmp);
            for (int i = 0; i < tmp.Count; i++)
            {
                RectTransform rt = tmp[i];
                if (rt == null || rt == c.transform) continue;
                Info info = GetInfo(rt, c, canvases[k].Value);
                if (info == null) continue;
                bool hasOv = overrides.TryGetValue(info.key, out Ov ov) && !ov.IsIdentity;
                if (hasOv || info.modified) applyList.Add(rt);
                if (first && rt.gameObject.activeInHierarchy && !IsDecor(rt)) elements.Add(rt);
            }
        }
        if (selected != null && !elements.Contains(selected)) selected = null;
        if (selected == null && elements.Count > 0) selected = elements[Mathf.Min(1, elements.Count - 1)];
    }

    private static bool IsDecor(RectTransform rt)
    {
        string n = rt.name;
        return n.StartsWith("CoopFrame") || n.StartsWith("CoopCorner") || n.StartsWith("CoopCardBevel");
    }

    private Info GetInfo(RectTransform rt, Canvas canvas, string canvasName)
    {
        if (infos.TryGetValue(rt, out Info info)) return info;
        var sb = new StringBuilder();
        int depth = 0;
        Transform t = rt;
        var parts = new List<string>();
        while (t != null && t != canvas.transform)
        {
            string name = t.name;
            int dup = 0;
            Transform p = t.parent;
            if (p != null)
            {
                for (int i = 0; i < p.childCount; i++)
                {
                    Transform s = p.GetChild(i);
                    if (s == t) break;
                    if (s.name == name) dup++;
                }
            }
            parts.Add(dup > 0 ? name + "[" + dup + "]" : name);
            t = t.parent;
            depth++;
        }
        sb.Append(canvasName);
        for (int i = parts.Count - 1; i >= 0; i--) sb.Append('/').Append(parts[i]);
        info = new Info
        {
            key = sb.ToString(),
            depth = depth - 1,
            basePos = rt.anchoredPosition, appliedPos = rt.anchoredPosition,
            baseScale = rt.localScale, appliedScale = rt.localScale
        };
        infos[rt] = info;
        return info;
    }

    private void Apply(RectTransform rt, Info info)
    {
        // Si el menú movió/escaló el elemento por su cuenta, esa posición pasa a ser la nueva base.
        if ((rt.anchoredPosition - info.appliedPos).sqrMagnitude > 0.0001f) info.basePos = rt.anchoredPosition;
        if ((rt.localScale - info.appliedScale).sqrMagnitude > 0.0000001f) info.baseScale = rt.localScale;

        bool hasOv = overrides.TryGetValue(info.key, out Ov ov) && !ov.IsIdentity;
        Vector2 pos = hasOv ? info.basePos + new Vector2(ov.dx, ov.dy) : info.basePos;
        Vector3 sc = hasOv ? info.baseScale * ov.scale : info.baseScale;
        if (!hasOv && !info.modified) { info.appliedPos = rt.anchoredPosition; info.appliedScale = rt.localScale; return; }

        if ((rt.anchoredPosition - pos).sqrMagnitude > 0.0001f) rt.anchoredPosition = pos;
        if ((rt.localScale - sc).sqrMagnitude > 0.0000001f) rt.localScale = sc;
        info.appliedPos = pos;
        info.appliedScale = sc;
        info.modified = hasOv;
    }

    // ------------------------------------------------------------------
    // Edición
    // ------------------------------------------------------------------
    private Ov GetOv(RectTransform rt, bool create)
    {
        if (rt == null || !infos.TryGetValue(rt, out Info info)) return null;
        if (overrides.TryGetValue(info.key, out Ov ov)) return ov;
        if (!create) return null;
        ov = new Ov();
        overrides[info.key] = ov;
        return ov;
    }

    private void Nudge(float dx, float dy, float dscale)
    {
        if (selected == null) return;
        Ov ov = GetOv(selected, true);
        if (ov == null) return;
        ov.dx += dx; ov.dy += dy;
        ov.scale = Mathf.Clamp(ov.scale + dscale, 0.1f, 5f);
        MarkChanged(selected);
    }

    private void MarkChanged(RectTransform rt)
    {
        if (rt != null && !applyList.Contains(rt)) applyList.Add(rt);
    }

    private void ResetSelected()
    {
        if (selected == null || !infos.TryGetValue(selected, out Info info)) return;
        overrides.Remove(info.key);
        Apply(selected, info);   // restaura (modified = true hasta este punto)
        info.modified = false;
        SetStatus("Elemento restablecido.");
    }

    private void ResetMenu()
    {
        var keysToRemove = new List<string>();
        string prefix = activeCanvasName + "/";
        foreach (var kv in overrides) if (kv.Key.StartsWith(prefix)) keysToRemove.Add(kv.Key);
        for (int i = 0; i < keysToRemove.Count; i++) overrides.Remove(keysToRemove[i]);
        for (int i = 0; i < applyList.Count; i++)
        {
            RectTransform rt = applyList[i];
            if (rt != null && infos.TryGetValue(rt, out Info info) && info.key.StartsWith(prefix)) Apply(rt, info);
        }
        dirty = true;
        SetStatus("Menú restablecido (sin guardar).");
    }

    private void HandleKeys()
    {
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        float step = Steps[stepIdx] * (shift ? 5f : 1f);
        float sstep = Steps[stepIdx] * 0.01f * (shift ? 5f : 1f);

        if (Input.GetKeyDown(KeyCode.PageDown)) Select(+1);
        if (Input.GetKeyDown(KeyCode.PageUp)) Select(-1);

        if (ctrl && Input.GetKeyDown(KeyCode.S)) { SaveNow(); return; }
        if (!ctrl && Input.GetKeyDown(KeyCode.R)) ResetSelected();

        if ((ctrl && Input.GetKeyDown(KeyCode.LeftArrow)) || Input.GetKeyDown(KeyCode.Keypad4)) Nudge(-step, 0f, 0f);
        if ((ctrl && Input.GetKeyDown(KeyCode.RightArrow)) || Input.GetKeyDown(KeyCode.Keypad6)) Nudge(step, 0f, 0f);
        if ((ctrl && Input.GetKeyDown(KeyCode.UpArrow)) || Input.GetKeyDown(KeyCode.Keypad8)) Nudge(0f, step, 0f);
        if ((ctrl && Input.GetKeyDown(KeyCode.DownArrow)) || Input.GetKeyDown(KeyCode.Keypad2)) Nudge(0f, -step, 0f);
        if ((ctrl && (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.Plus))) || Input.GetKeyDown(KeyCode.KeypadPlus)) Nudge(0f, 0f, sstep);
        if ((ctrl && Input.GetKeyDown(KeyCode.Minus)) || Input.GetKeyDown(KeyCode.KeypadMinus)) Nudge(0f, 0f, -sstep);
    }

    private void Select(int dir)
    {
        if (elements.Count == 0) return;
        int idx = elements.IndexOf(selected);
        idx = idx < 0 ? 0 : (idx + dir + elements.Count) % elements.Count;
        selected = elements[idx];
    }

    private bool MouseOverWindow()
    {
        Vector2 m = Input.mousePosition;
        return window.Contains(new Vector2(m.x, Screen.height - m.y));
    }

    private Camera CanvasCamera()
    {
        if (activeCanvas == null || activeCanvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
        return activeCanvas.worldCamera;
    }

    private void HandleMouse()
    {
        Vector2 mouse = Input.mousePosition;
        if (Input.GetMouseButtonDown(1) && !MouseOverWindow())
        {
            Camera cam = CanvasCamera();
            for (int i = elements.Count - 1; i >= 0; i--)
            {
                RectTransform rt = elements[i];
                if (rt == null) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(rt, mouse, cam))
                {
                    selected = rt;
                    dragging = true;
                    break;
                }
            }
        }
        if (Input.GetMouseButtonUp(1)) dragging = false;

        if (dragging && Input.GetMouseButton(1) && selected != null && selected.parent is RectTransform parent)
        {
            Camera cam = CanvasCamera();
            Vector2 a, b;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, lastMouse, cam, out a) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, mouse, cam, out b))
            {
                Vector2 d = b - a;
                if (d.sqrMagnitude > 0f) Nudge(d.x, d.y, 0f);
            }
        }
        lastMouse = mouse;
    }

    // ------------------------------------------------------------------
    // Persistencia
    // ------------------------------------------------------------------
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
                return Path.Combine(dir, "coop_ui_layout.cfg");
            }
            catch { return null; }
        }
    }

    private void ParseLine(string raw)
    {
        string line = raw.Trim();
        if (line.Length == 0 || line.StartsWith("#")) return;
        string[] p = line.Split('\t');
        if (p.Length < 4) return;
        float dx, dy, s;
        if (!float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out dx)) return;
        if (!float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out dy)) return;
        if (!float.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out s)) return;
        overrides[p[0]] = new Ov { dx = dx, dy = dy, scale = Mathf.Clamp(s, 0.1f, 5f) };
    }

    private void LoadAll()
    {
        try
        {
            for (int i = 0; i < BakedDefaults.Length; i++) ParseLine(BakedDefaults[i]);
            string path = ConfigPath;
            if (path != null && File.Exists(path))
                foreach (string l in File.ReadAllLines(path)) ParseLine(l);
        }
        catch (Exception e) { Log("Error leyendo coop_ui_layout.cfg: " + e.Message); }
    }

    private string BuildLines()
    {
        var sb = new StringBuilder();
        foreach (var kv in overrides)
        {
            if (kv.Value.IsIdentity) continue;
            sb.Append(kv.Key).Append('\t')
              .Append(kv.Value.dx.ToString("0.##", CultureInfo.InvariantCulture)).Append('\t')
              .Append(kv.Value.dy.ToString("0.##", CultureInfo.InvariantCulture)).Append('\t')
              .Append(kv.Value.scale.ToString("0.###", CultureInfo.InvariantCulture)).Append('\n');
        }
        return sb.ToString();
    }

    private void SaveNow()
    {
        try
        {
            string path = ConfigPath;
            if (path == null) { SetStatus("ERROR: sin ruta de guardado."); return; }
            string body = "# Layout de menús del mod: clave<TAB>dx<TAB>dy<TAB>escala. Editar con F11 en el juego.\n" + BuildLines();
            File.WriteAllText(path, body);
            SetStatus("Guardado en coop_ui_layout.cfg.");
        }
        catch (Exception e) { Log("Error guardando: " + e.Message); SetStatus("ERROR al guardar (ver log)."); }
    }

    private void CopyBaked()
    {
        var sb = new StringBuilder();
        foreach (string line in BuildLines().Split('\n'))
            if (line.Length > 0) sb.Append("    \"").Append(line.Replace("\t", "\\t")).Append("\",\n");
        GUIUtility.systemCopyBuffer = sb.ToString();
        SetStatus("Copiado: pega en BakedDefaults de CoopUiLayoutEditor.cs.");
    }

    private void SetStatus(string s) { status = s; statusTimer = 4f; }

    private static void Log(string msg)
    {
        try { if (Main.CoopLocal != null) Blasphemous.ModdingAPI.ModLog.Info("[CoopUiLayoutEditor] " + msg, Main.CoopLocal); }
        catch (Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "CoopUiLayoutEditor.cs:Log"); }
    }

    // ------------------------------------------------------------------
    // IMGUI
    // ------------------------------------------------------------------
    private void OnGUI()
    {
        if (!IsOpen) return;
        DrawSelectionOutline();
        window = GUILayout.Window(WindowId, window, DrawWindow, "Layout de menús (F11)");
    }

    private void DrawSelectionOutline()
    {
        if (selected == null || !selected.gameObject.activeInHierarchy) return;
        if (pixel == null)
        {
            pixel = new Texture2D(1, 1);
            pixel.SetPixel(0, 0, Color.white);
            pixel.Apply();
        }
        var corners = new Vector3[4];
        selected.GetWorldCorners(corners);
        Camera cam = CanvasCamera();
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            Vector3 sp = cam != null ? cam.WorldToScreenPoint(corners[i]) : corners[i];
            minX = Mathf.Min(minX, sp.x); maxX = Mathf.Max(maxX, sp.x);
            minY = Mathf.Min(minY, sp.y); maxY = Mathf.Max(maxY, sp.y);
        }
        float top = Screen.height - maxY;
        var r = new Rect(minX, top, maxX - minX, maxY - minY);
        Color prev = GUI.color;
        GUI.color = new Color(1f, 0.85f, 0.1f, 1f);
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, 2f), pixel);
        GUI.DrawTexture(new Rect(r.x, r.yMax - 2f, r.width, 2f), pixel);
        GUI.DrawTexture(new Rect(r.x, r.y, 2f, r.height), pixel);
        GUI.DrawTexture(new Rect(r.xMax - 2f, r.y, 2f, r.height), pixel);
        GUI.color = prev;
    }

    private void DrawWindow(int id)
    {
        GUILayout.Label("Menú activo: " + (activeCanvas != null ? activeCanvasName : "(ninguno visible — abre un menú del mod)"));
        GUILayout.Label("Clic DERECHO sobre el menú: elegir/arrastrar  |  Re/Av Pág: siguiente  |  Ctrl+flechas / numérico: mover\nCtrl +/-: escalar  |  R: reset elemento  |  Ctrl+S: guardar");

        GUILayout.BeginHorizontal();
        GUILayout.Label("Filtro:", GUILayout.Width(40));
        filter = GUILayout.TextField(filter, GUILayout.Width(150));
        GUILayout.Label("Paso:", GUILayout.Width(36));
        stepIdx = GUILayout.Toolbar(stepIdx, new[] { "1", "5", "20" }, GUILayout.Width(110));
        GUILayout.EndHorizontal();

        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(240));
        string f = filter.Trim().ToLowerInvariant();
        for (int i = 0; i < elements.Count; i++)
        {
            RectTransform rt = elements[i];
            if (rt == null) continue;
            if (f.Length > 0 && rt.name.ToLowerInvariant().IndexOf(f, StringComparison.Ordinal) < 0) continue;
            Info info;
            infos.TryGetValue(rt, out info);
            Ov ov = GetOv(rt, false);
            bool changed = ov != null && !ov.IsIdentity;
            string indent = new string(' ', Mathf.Clamp(info != null ? info.depth : 0, 0, 10) * 2);
            string label = (rt == selected ? "► " : "   ") + indent + rt.name + (rt.GetComponent<Text>() != null ? "  (texto)" : "") + (changed ? "  *" : "");
            if (GUILayout.Button(label, GUI.skin.label))
            {
                selected = rt;
            }
        }
        GUILayout.EndScrollView();

        GUILayout.Space(4);
        if (selected != null && infos.TryGetValue(selected, out Info si))
        {
            GUILayout.Label("Elemento: " + selected.name);
            Ov ov = GetOv(selected, false);
            float dx = ov != null ? ov.dx : 0f, dy = ov != null ? ov.dy : 0f, sc = ov != null ? ov.scale : 1f;
            GUILayout.Label("Desplazamiento: X " + dx.ToString("0.#") + "  Y " + dy.ToString("0.#") + "   Escala: " + sc.ToString("0.00") + "x");
            float step = Steps[stepIdx];
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◄", GUILayout.Width(40))) Nudge(-step, 0f, 0f);
            if (GUILayout.Button("►", GUILayout.Width(40))) Nudge(step, 0f, 0f);
            if (GUILayout.Button("▲", GUILayout.Width(40))) Nudge(0f, step, 0f);
            if (GUILayout.Button("▼", GUILayout.Width(40))) Nudge(0f, -step, 0f);
            GUILayout.Space(14);
            if (GUILayout.Button("Escala -", GUILayout.Width(70))) Nudge(0f, 0f, -step * 0.01f);
            if (GUILayout.Button("Escala +", GUILayout.Width(70))) Nudge(0f, 0f, step * 0.01f);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Restablecer elemento (R)")) ResetSelected();
        }

        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(0.2f, 0.7f, 0.2f);
        if (GUILayout.Button("GUARDAR", GUILayout.Height(26))) SaveNow();
        GUI.backgroundColor = new Color(0.25f, 0.5f, 0.8f);
        if (GUILayout.Button("Copiar", GUILayout.Height(26))) CopyBaked();
        GUI.backgroundColor = new Color(0.7f, 0.4f, 0.1f);
        if (GUILayout.Button("Reset menú", GUILayout.Height(26))) ResetMenu();
        GUI.backgroundColor = new Color(0.7f, 0.2f, 0.2f);
        if (GUILayout.Button("Cerrar (F11)", GUILayout.Height(26))) IsOpen = false;
        GUI.backgroundColor = Color.white;
        GUILayout.EndHorizontal();
        if (!string.IsNullOrEmpty(status)) GUILayout.Label(status);
        GUI.DragWindow();
    }
}
