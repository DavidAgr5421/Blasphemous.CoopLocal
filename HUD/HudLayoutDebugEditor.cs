using System.Collections.Generic;
using System;
using UnityEngine;
using Framework.Managers;
using Gameplay.GameControllers.Penitent;

namespace Blasphemous.CoopLocal;

/// <summary>
/// Editor interactivo en tiempo real para calibrar y posicionar las barras de vida/fervor,
/// decoraciones y los indicadores de monedas de todos los jugadores cooperativos (P1, P2, P3, P4).
/// </summary>
public class HudLayoutDebugEditor : MonoBehaviour
{
    public static HudLayoutDebugEditor Instance { get; private set; }

    public static bool IsOpen { get; private set; }

    private static readonly string[] ElementNames = new string[]
    {
        "P1 Vida",
        "P1 Fervor",
        "P1 Dec. Vida",
        "P1 Monedas",

        "P2 Vida",
        "P2 Fervor",
        "P2 Dec. Vida",
        "P2 Monedas",

        "P3 Vida",
        "P3 Fervor",
        "P3 Dec. Vida",
        "P3 Monedas",

        "P4 Vida",
        "P4 Fervor",
        "P4 Dec. Vida",
        "P4 Monedas"
    };

    private int selectedIndex = 4; // Por defecto P2 Vida
    private string statusMessage = "";
    private float statusTimer = 0f;
    private bool isMultiLayout = CoopGameConfig.IsHudLayout3Plus;

    // Repetición suave al mantener presionadas teclas de movimiento
    private float keyRepeatTimer = 0f;
    private const float KeyRepeatInitialDelay = 0.25f;
    private const float KeyRepeatRate = 0.035f;

    private static Texture2D boxTexture;
    private static Texture2D outlineTexture;
    private static int lastToggleFrame = -1;

    // Pestañas del editor: HUD (barras/monedas de cada jugador) y Árbol (calibración del árbol elemental y panel de descripción)
    private enum EditorTab { Hud, Tree }
    private EditorTab currentTab = EditorTab.Hud;

    private const int TreeParamCount = 11;
    private static readonly string[] TreeParamLabels = new string[]
    {
        "TreeOffsetX",
        "TreeOffsetY",
        "TreeScale",
        "ElementalBranchSpacing",
        "ElementalNodeVerticalSpacing",
        "ElectricoColumnOffset",
        "SkillTextScale",
        "DescPanelOffsetX",
        "DescPanelOffsetY",
        "DescPanelScale",
        "MaldicionColumnOffset"
    };
    private static readonly float[] TreeParamStep = { 1f, 1f, 0.01f, 2f, 1f, 1f, 0.02f, 1f, 1f, 0.01f, 1f };
    private static readonly float[] TreeParamFastStep = { 10f, 10f, 0.05f, 10f, 5f, 10f, 0.1f, 10f, 10f, 0.05f, 10f };
    // Límites: los mismos que aplica CoopGameConfig.Load() donde existen; el resto sin límite
    private static readonly float[] TreeParamMin = { float.NegativeInfinity, float.NegativeInfinity, 0.3f, 10f, 15f, 0f, 0.5f, float.NegativeInfinity, float.NegativeInfinity, 0.3f, 0f };
    private static readonly float[] TreeParamMax = { float.PositiveInfinity, float.PositiveInfinity, 3.0f, 1500f, 100f, 400f, 3.5f, float.PositiveInfinity, float.PositiveInfinity, 3.0f, 400f };
    private int treeSelectedIndex = 0;

    // Capas (orden de dibujo) de los elementos HUD: se reaplican periódicamente porque los HUD se recrean al cambiar de sala/jugadores.
    private float layerApplyTimer = 0f;
    private bool layersDirty = true;
    private const int MinLayer = -20, MaxLayer = 20;
    private float treeRepeatTimer = 0f;
    private Rect treeWindowRect = new Rect(16f, 100f, 420f, 395f);
    private const int TreeWindowId = 574830;

    public static void EnsureCreated()
    {
        if (Instance != null && Instance.gameObject != null) return;

        GameObject obj = new GameObject("HudLayoutDebugEditor");
        DontDestroyOnLoad(obj);
        Instance = obj.AddComponent<HudLayoutDebugEditor>();
        DashParryDebugLog.Log("HudLayoutDebugEditor inicializado. Presiona F7 para abrir/cerrar.");
    }

    internal static void Initialize()
    {
        EnsureCreated();
    }

    public static void Toggle()
    {
        if (Time.frameCount == lastToggleFrame) return;
        lastToggleFrame = Time.frameCount;

        EnsureCreated();
        SetOpen(!IsOpen);
    }

    public static void SetOpen(bool open)
    {
        IsOpen = open;

        if (Core.Input != null)
        {
            Core.Input.SetBlocker("HUD_LAYOUT_EDITOR", IsOpen);
        }

        if (IsOpen)
        {
            if (Instance != null)
            {
                Instance.isMultiLayout = CoopGameConfig.IsHudLayout3Plus;
                Instance.EnsureAllSlotsCreatedForDebug();
                // Abierto desde el menú de habilidades -> pestaña Árbol
                bool inSkillMenu = SwordElementalUpgrades.IsSkillMenuActive;
                Instance.currentTab = inSkillMenu ? EditorTab.Tree : EditorTab.Hud;
                Instance.SetStatus(inSkillMenu
                    ? "Editor abierto (Árbol). Tab/PgUp/PgDn: parámetro, flechas: ajustar, Enter: guardar."
                    : "Editor de HUD abierto. Tab: navegar, Flechas/WASD: mover, +/-: escala, F8: guardar.");
            }
            Player2HudFadeSync.ShowAllInstant();
            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info("[HudEditor] Editor abierto.", Main.CoopLocal);
            }
        }
    }

    private void Awake()
    {
        Instance = this;
    }

    public void EnsureAllSlotsCreatedForDebug()
    {
        try
        {
            Penitent p1 = Core.Logic != null ? Core.Logic.Penitent : null;
            if (p1 == null) return;

            CoopPlayerRegistry.RegisterInstance(0, p1);

            int total = isMultiLayout ? 4 : 2;
            for (int i = 1; i < total; i++)
            {
                Penitent guest = CoopPlayerRegistry.GetPenitent(i) ?? p1;
                CoopHealthBars.EnsureCreated(i, guest);
                CoopFervourBars.EnsureCreated(i, guest);
            }

            if (isMultiLayout)
            {
                CoopPurgeHUD.EnsureCreatedAll();
                CoopPurgeHUD.SetVisible(true, instant: true);
            }
            else
            {
                Penitent p2 = CoopPlayerRegistry.GetPenitent(1) ?? p1;
                Player2PurgePoints.EnsureCreated(p2);
                Player2PurgePoints.SetVisible(true, instant: true);
            }

            CoopHealthBars.BringToFrontAll();
            CoopFervourBars.BringToFrontAll();
            CoopHealthBars.SetVisibleAll(true, instant: true);
            CoopFervourBars.SetVisibleAll(true, instant: true);
        }
        catch (Exception ex)
        {
            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Error($"[HudEditor] Error en EnsureAllSlotsCreatedForDebug: {ex}", Main.CoopLocal);
            }
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F7))
        {
            Toggle();
            return;
        }

        layerApplyTimer -= Time.unscaledDeltaTime;
        if (layersDirty || layerApplyTimer <= 0f)
        {
            layerApplyTimer = 0.5f;
            layersDirty = false;
            ApplyLayers();
        }

        if (!IsOpen) return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            SetOpen(false);
            return;
        }

        bool saveRequested = Input.GetKeyDown(KeyCode.F8) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);

        if (currentTab == EditorTab.Hud)
        {
            HandleSelectionInput();
            if (Input.GetKeyDown(KeyCode.PageUp)) ChangeLayer(selectedIndex, +1);
            if (Input.GetKeyDown(KeyCode.PageDown)) ChangeLayer(selectedIndex, -1);
            HandleMovementInput();
            HandleScaleInput();

            if (saveRequested)
            {
                SaveCurrentLayout();
            }
        }
        else
        {
            HandleTreeInput();

            if (saveRequested)
            {
                SaveTreeLayout();
            }
        }

        if (statusTimer > 0f)
        {
            statusTimer -= Time.unscaledDeltaTime;
            if (statusTimer <= 0f)
            {
                statusMessage = "";
            }
        }
    }

    private static int GetLayer(int index) => HudLayoutConfig.Current.GetLayer(index);

    private void ChangeLayer(int index, int delta)
    {
        var cfg = HudLayoutConfig.Current;
        cfg.ValidateDefaults();
        if (index < 0 || index >= cfg.layers.Length) return;
        cfg.layers[index] = Mathf.Clamp(cfg.layers[index] + delta, MinLayer, MaxLayer);
        layersDirty = true;
        SetStatus($"Capa de {ElementNames[index]}: {cfg.layers[index]} (mayor = al frente). Guarda con F8/Enter.");
    }

    private void SetLayer(int index, int value)
    {
        var cfg = HudLayoutConfig.Current;
        cfg.ValidateDefaults();
        if (index < 0 || index >= cfg.layers.Length) return;
        cfg.layers[index] = Mathf.Clamp(value, MinLayer, MaxLayer);
        layersDirty = true;
    }

    /// <summary>
    /// Reordena los elementos HUD que comparten padre según su capa (mayor = encima), sin tocar al resto de hijos del canvas:
    /// los elementos ocupan los mismos huecos de sibling-index que ya tenían, solo cambia quién va en cada hueco.
    /// Con todas las capas en 0 el orden queda como estaba (desempate por orden actual).
    /// </summary>
    private void ApplyLayers()
    {
        try
        {
            var cfg = HudLayoutConfig.Current;
            var groups = new Dictionary<Transform, List<KeyValuePair<int, RectTransform>>>();
            for (int i = 4; i < ElementNames.Length; i++) // P1 es el HUD vanilla: solo P2-P4
            {
                RectTransform rect = GetTargetRect(i);
                if (rect == null || rect.parent == null) continue;
                if (!groups.TryGetValue(rect.parent, out var list))
                {
                    list = new List<KeyValuePair<int, RectTransform>>();
                    groups[rect.parent] = list;
                }
                bool dup = false;
                for (int k = 0; k < list.Count; k++) if (list[k].Value == rect) { dup = true; break; }
                if (!dup) list.Add(new KeyValuePair<int, RectTransform>(i, rect));
            }

            foreach (var kv in groups)
            {
                var list = kv.Value;
                if (list.Count < 2) continue;
                var slots = new List<int>();
                foreach (var e in list) slots.Add(e.Value.GetSiblingIndex());
                slots.Sort();
                list.Sort((a, b) =>
                {
                    int c = cfg.GetLayer(a.Key).CompareTo(cfg.GetLayer(b.Key));
                    return c != 0 ? c : a.Value.GetSiblingIndex().CompareTo(b.Value.GetSiblingIndex());
                });
                for (int k = 0; k < list.Count; k++)
                {
                    if (list[k].Value.GetSiblingIndex() != slots[k]) list[k].Value.SetSiblingIndex(slots[k]);
                }
            }
        }
        catch (Exception ex)
        {
            if (Main.CoopLocal != null) Blasphemous.ModdingAPI.ModLog.Warn($"[HudEditor] ApplyLayers: {ex.Message}", Main.CoopLocal);
        }
    }

    private void HandleSelectionInput()
    {
        int maxIndex = isMultiLayout ? ElementNames.Length : 8;

        // Ciclar con Tab o Shift+Tab
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                selectedIndex = (selectedIndex - 1 + maxIndex) % maxIndex;
            }
            else
            {
                selectedIndex = (selectedIndex + 1) % maxIndex;
            }
        }

        // Teclas directas 1, 2, 3, 4 saltan al jugador correspondiente
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) selectedIndex = 0; // P1
        else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) selectedIndex = 4; // P2
        else if (isMultiLayout && (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))) selectedIndex = 8; // P3
        else if (isMultiLayout && (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4))) selectedIndex = 12; // P4

        if (selectedIndex >= maxIndex) selectedIndex = 0;
    }

    private void HandleMovementInput()
    {
        RectTransform target = GetTargetRect(selectedIndex);
        if (target == null) return;

        float step = 1f;
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
        {
            step = 10f; // Movimiento rápido
        }
        else if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
        {
            step = 0.25f; // Movimiento de precisión
        }

        Vector2 delta = Vector2.zero;

        bool up = Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W);
        bool down = Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S);
        bool left = Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A);
        bool right = Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D);

        bool upInitial = Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W);
        bool downInitial = Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S);
        bool leftInitial = Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A);
        bool rightInitial = Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D);

        if (upInitial) delta.y += step;
        if (downInitial) delta.y -= step;
        if (leftInitial) delta.x -= step;
        if (rightInitial) delta.x += step;

        bool anyHeld = (up || down || left || right) && !upInitial && !downInitial && !leftInitial && !rightInitial;
        if (anyHeld)
        {
            keyRepeatTimer += Time.unscaledDeltaTime;
            if (keyRepeatTimer >= KeyRepeatInitialDelay)
            {
                if (up) delta.y += step;
                if (down) delta.y -= step;
                if (left) delta.x -= step;
                if (right) delta.x += step;
                keyRepeatTimer = KeyRepeatInitialDelay - KeyRepeatRate;
            }
        }
        else if (!up && !down && !left && !right)
        {
            keyRepeatTimer = 0f;
        }

        if (delta != Vector2.zero)
        {
            target.anchoredPosition += delta;
            SyncToConfig(selectedIndex, target.anchoredPosition, target.localScale.x);
        }
    }

    private void HandleScaleInput()
    {
        float scaleStep = 0.02f;
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
        {
            scaleStep = 0.05f;
        }

        float deltaScale = 0f;
        if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus) || Input.GetKeyDown(KeyCode.Plus))
        {
            deltaScale += scaleStep;
        }
        if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
        {
            deltaScale -= scaleStep;
        }

        if (deltaScale != 0f)
        {
            AdjustScale(selectedIndex, deltaScale);
        }
    }

    public void AdjustScale(int index, float deltaScale)
    {
        RectTransform target = GetTargetRect(index);
        if (target == null) return;

        float newScale = Mathf.Clamp(target.localScale.x + deltaScale, 0.20f, 2.50f);
        target.localScale = Vector3.one * newScale;
        SyncToConfig(index, target.anchoredPosition, newScale);
    }

    // Índice del editor = jugador * 4 + elemento (Vida, Fervor, Dec. Vida, Monedas).
    private static CoopPlayerContext PlayerOf(int index) => CoopPlayers.Get(index / 4);
    private static CoopPlayerContext.HudElement ElementOf(int index) => (CoopPlayerContext.HudElement)(index % 4);

    private RectTransform GetTargetRect(int index)
    {
        if (index < 0 || index >= ElementNames.Length) return null;
        return PlayerOf(index).HudRect(ElementOf(index), isMultiLayout);
    }

    private void SyncToConfig(int index, Vector2 pos, float scale)
    {
        if (index < 0 || index >= ElementNames.Length) return;
        PlayerOf(index).ApplyHudTransform(ElementOf(index), pos, scale, isMultiLayout);
    }

    public void SaveCurrentLayout()
    {
        for (int i = 0; i < ElementNames.Length; i++)
        {
            RectTransform rect = GetTargetRect(i);
            if (rect != null)
            {
                SyncToConfig(i, rect.anchoredPosition, rect.localScale.x);
            }
        }

        HudLayoutConfig.Save();
        SetStatus("¡Layout guardado exitosamente en coop_hud_layout.json!");
    }

    public void ResetToDefaults()
    {
        System.Array.Clear(HudLayoutConfig.Current.layers, 0, HudLayoutConfig.Current.layers.Length);
        layersDirty = true;
        var cfg = new CoopHudLayoutConfig();
        cfg.ValidateDefaults();

        for (int i = 0; i < ElementNames.Length; i++)
        {
            RectTransform rect = GetTargetRect(i);
            if (rect == null) continue;

            HudElementPosition defPos = null;
            switch (i)
            {
                // P1
                case 0: defPos = cfg.p1_health; break;
                case 1: defPos = cfg.p1_fervour; break;
                case 2: defPos = cfg.p1_health_deco; break;
                case 3: defPos = cfg.p1_coins; break;

                // P2
                case 4: defPos = isMultiLayout ? cfg.p2_health : cfg.p2_health_2p; break;
                case 5: defPos = isMultiLayout ? cfg.p2_fervour : cfg.p2_fervour_2p; break;
                case 6: defPos = cfg.p2_health_deco; break;
                case 7: defPos = isMultiLayout ? cfg.p2_coins : cfg.p2_coins_2p; break;

                // P3
                case 8: defPos = cfg.p3_health; break;
                case 9: defPos = cfg.p3_fervour; break;
                case 10: defPos = cfg.p3_health_deco; break;
                case 11: defPos = cfg.p3_coins; break;

                // P4
                case 12: defPos = cfg.p4_health; break;
                case 13: defPos = cfg.p4_fervour; break;
                case 14: defPos = cfg.p4_health_deco; break;
                case 15: defPos = cfg.p4_coins; break;
            }

            if (defPos != null)
            {
                rect.anchoredPosition = defPos.Position;
                rect.localScale = Vector3.one * defPos.scale;
                SyncToConfig(i, defPos.Position, defPos.scale);
            }
        }

        HudLayoutConfig.Save();
        SetStatus("Coordenadas restablecidas a valores por defecto.");
    }

    private void SetStatus(string message)
    {
        statusMessage = message;
        statusTimer = 4.5f;
    }

    private void OnGUI()
    {
        if (!IsOpen) return;

        try
        {
            GUI.depth = -1000;
            EnsureTextures();

            // 1. Dibujar recuadro resaltado alrededor del elemento seleccionado (solo pestaña HUD)
            if (currentTab == EditorTab.Hud)
            {
                DrawSelectionOutline();
            }

            // 2. Ventana de control superior (selector de pestaña + cuerpo de la pestaña activa)
            float windowWidth = 740f;
            float windowHeight = currentTab == EditorTab.Hud ? 316f : 96f;
            float windowX = (Screen.width - windowWidth) * 0.5f;
            float windowY = 16f;

            GUI.color = new Color(0.08f, 0.08f, 0.12f, 0.94f);
            GUI.DrawTexture(new Rect(windowX, windowY, windowWidth, windowHeight), boxTexture);
            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(windowX + 12f, windowY + 8f, windowWidth - 24f, windowHeight - 16f));

            // Selector de pestaña
            GUILayout.BeginHorizontal();
            DrawTabButton("HUD", EditorTab.Hud);
            DrawTabButton("Árbol", EditorTab.Tree);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (currentTab == EditorTab.Hud)
            {
                DrawHudTab();
            }
            else
            {
                GUILayout.Label("<b><size=15>=== CALIBRACIÓN DEL ÁRBOL DE HABILIDADES ===</size></b>");
                GUILayout.Label("<size=11>Parámetros en la ventana flotante \"Árbol de habilidades\" (arrástrala por su barra de título). Cambios en vivo con el menú de habilidades abierto.</size>");
            }

            // Mensaje de estado
            if (!string.IsNullOrEmpty(statusMessage))
            {
                GUI.color = Color.yellow;
                GUILayout.Label($"<b>{statusMessage}</b>");
                GUI.color = Color.white;
            }

            GUILayout.EndArea();

            if (currentTab == EditorTab.Tree)
            {
                DrawTreeWindow();
            }
        }
        catch (Exception ex)
        {
            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Error($"[HudEditor] Error en OnGUI: {ex.Message}", Main.CoopLocal);
            }
        }
    }

    private void DrawTabButton(string label, EditorTab tab)
    {
        GUI.backgroundColor = currentTab == tab ? Color.yellow : Color.gray;
        if (GUILayout.Button(label, GUILayout.Width(90), GUILayout.Height(22)))
        {
            currentTab = tab;
        }
        GUI.backgroundColor = Color.white;
    }

    private void DrawHudTab()
    {
        // Título con selector de modo
        GUILayout.BeginHorizontal();
        GUILayout.Label($"<b><size=15>=== COOP HUD LAYOUT DEBUG EDITOR ({ (isMultiLayout ? "3-4 Jugadores" : "2 Jugadores") }) ===</size></b>");
        GUILayout.FlexibleSpace();
        if (GUILayout.Button(isMultiLayout ? "Cambiar a Modo 2P" : "Cambiar a Modo 3-4P", GUILayout.Width(170), GUILayout.Height(22)))
        {
            isMultiLayout = !isMultiLayout;
            EnsureAllSlotsCreatedForDebug();
            if (selectedIndex >= (isMultiLayout ? ElementNames.Length : 8))
            {
                selectedIndex = 4;
            }
        }
        GUILayout.EndHorizontal();

        // Fila P1
        GUILayout.BeginHorizontal();
        GUILayout.Label("<b>P1:</b>", GUILayout.Width(30));
        for (int i = 0; i < 4; i++) DrawElementButton(i);
        GUILayout.EndHorizontal();

        // Fila P2
        GUILayout.BeginHorizontal();
        GUILayout.Label("<b>P2:</b>", GUILayout.Width(30));
        for (int i = 4; i < 8; i++) DrawElementButton(i);
        GUILayout.EndHorizontal();

        if (isMultiLayout)
        {
            // Fila P3
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>P3:</b>", GUILayout.Width(30));
            for (int i = 8; i < 12; i++) DrawElementButton(i);
            GUILayout.EndHorizontal();

            // Fila P4
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>P4:</b>", GUILayout.Width(30));
            for (int i = 12; i < 16; i++) DrawElementButton(i);
            GUILayout.EndHorizontal();
        }

        // Información del elemento actual
        RectTransform currentTarget = GetTargetRect(selectedIndex);
        string posText = currentTarget != null
            ? $"Pos: ({currentTarget.anchoredPosition.x:F1}, {currentTarget.anchoredPosition.y:F1}) | Escala: {currentTarget.localScale.x:F2}"
            : "No activo en escena actual";

        GUI.color = Color.cyan;
        GUILayout.Label($"<b>Seleccionado [{selectedIndex + 1}/{(isMultiLayout ? ElementNames.Length : 8)}]: {ElementNames[selectedIndex]}</b>  |  {posText}");
        GUI.color = Color.white;

        // Capas (orden de dibujo): solo P2-P4; P1 es el HUD original del juego
        GUILayout.BeginHorizontal();
        if (selectedIndex >= 4)
        {
            GUILayout.Label($"<b>Capa:</b> {GetLayer(selectedIndex)}  (mayor = al frente)", GUILayout.Width(220));
            if (GUILayout.Button("▼ Atrás (PgDn)", GUILayout.Width(110))) ChangeLayer(selectedIndex, -1);
            if (GUILayout.Button("▲ Frente (PgUp)", GUILayout.Width(115))) ChangeLayer(selectedIndex, +1);
            if (GUILayout.Button("Capa 0", GUILayout.Width(60))) SetLayer(selectedIndex, 0);
            var lc = HudLayoutConfig.Current;
            string all = "";
            for (int i = 4; i < (isMultiLayout ? ElementNames.Length : 8); i++)
                if (lc.GetLayer(i) != 0) all += $"{ElementNames[i]}:{lc.GetLayer(i)}  ";
            GUILayout.Label(all.Length > 0 ? $"<size=10>{all}</size>" : "<size=10>(todas las capas en 0)</size>");
        }
        else
        {
            GUILayout.Label("<size=11>Las capas solo aplican a P2-P4 (el HUD de P1 es el original del juego).</size>");
        }
        GUILayout.EndHorizontal();

        // Controles de ayuda
        GUILayout.Label("<size=11>Controles: <b>Tab/Click</b>: Seleccionar | <b>PgUp/PgDn</b>: Capa | <b>Flechas/WASD</b>: Mover (Shift: x10, Ctrl: x0.25) | <b>+/-</b>: Escala | <b>F8/Enter</b>: Guardar</size>");

        // Botones de acción inferior
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("◄", GUILayout.Width(35))) Nudge(selectedIndex, new Vector2(-1f, 0f));
        if (GUILayout.Button("►", GUILayout.Width(35))) Nudge(selectedIndex, new Vector2(1f, 0f));
        if (GUILayout.Button("▲", GUILayout.Width(35))) Nudge(selectedIndex, new Vector2(0f, 1f));
        if (GUILayout.Button("▼", GUILayout.Width(35))) Nudge(selectedIndex, new Vector2(0f, -1f));

        GUILayout.Space(8);
        if (GUILayout.Button("- Escala", GUILayout.Width(65))) AdjustScale(selectedIndex, -0.05f);
        if (GUILayout.Button("+ Escala", GUILayout.Width(65))) AdjustScale(selectedIndex, 0.05f);

        GUILayout.Space(10);
        GUI.backgroundColor = new Color(0.2f, 0.7f, 0.2f);
        if (GUILayout.Button("GUARDAR (F8)", GUILayout.Height(24)))
        {
            SaveCurrentLayout();
        }
        GUI.backgroundColor = new Color(0.7f, 0.4f, 0.1f);
        if (GUILayout.Button("Restablecer", GUILayout.Height(24)))
        {
            ResetToDefaults();
        }
        GUI.backgroundColor = new Color(0.7f, 0.2f, 0.2f);
        if (GUILayout.Button("Cerrar (F7)", GUILayout.Height(24)))
        {
            SetOpen(false);
        }
        GUI.backgroundColor = Color.white;
        GUILayout.EndHorizontal();
    }

    // =====================================================================
    // PESTAÑA ÁRBOL: calibración de TreeOffset/Scale/Separaciones/Electricidad y panel de descripción
    // =====================================================================

    private float GetTreeParam(int i)
    {
        switch (i)
        {
            case 0: return CoopGameConfig.TreeOffsetX;
            case 1: return CoopGameConfig.TreeOffsetY;
            case 2: return CoopGameConfig.TreeScale;
            case 3: return CoopGameConfig.ElementalBranchSpacing;
            case 4: return CoopGameConfig.ElementalNodeVerticalSpacing;
            case 5: return CoopGameConfig.ElectricoColumnOffset;
            case 6: return CoopGameConfig.SkillTextScale;
            case 7: return CoopGameConfig.DescPanelOffsetX;
            case 8: return CoopGameConfig.DescPanelOffsetY;
            case 9: return CoopGameConfig.DescPanelScale;
            default: return CoopGameConfig.MaldicionColumnOffset;
        }
    }

    private static float GetTreeDefault(int i)
    {
        switch (i)
        {
            case 0: return CoopGameConfig.DefaultTreeOffsetX;
            case 1: return CoopGameConfig.DefaultTreeOffsetY;
            case 2: return CoopGameConfig.DefaultTreeScale;
            case 3: return CoopGameConfig.DefaultElementalBranchSpacing;
            case 4: return CoopGameConfig.DefaultElementalNodeVerticalSpacing;
            case 5: return CoopGameConfig.DefaultElectricoColumnOffset;
            case 6: return CoopGameConfig.DefaultSkillTextScale;
            case 7: return CoopGameConfig.DefaultDescPanelOffsetX;
            case 8: return CoopGameConfig.DefaultDescPanelOffsetY;
            case 9: return CoopGameConfig.DefaultDescPanelScale;
            default: return CoopGameConfig.DefaultMaldicionColumnOffset;
        }
    }

    private void SetTreeParam(int i, float value)
    {
        value = Mathf.Clamp(value, TreeParamMin[i], TreeParamMax[i]);
        // Escalas: redondeo a 2 decimales para no acumular error de coma flotante al ajustar
        if (TreeParamStep[i] < 1f) value = Mathf.Round(value * 100f) / 100f;

        switch (i)
        {
            case 0: CoopGameConfig.TreeOffsetX = value; break;
            case 1: CoopGameConfig.TreeOffsetY = value; break;
            case 2: CoopGameConfig.TreeScale = value; break;
            case 3: CoopGameConfig.ElementalBranchSpacing = value; break;
            case 4: CoopGameConfig.ElementalNodeVerticalSpacing = value; break;
            case 5: CoopGameConfig.ElectricoColumnOffset = value; break;
            case 6: CoopGameConfig.SkillTextScale = value; break;
            case 7: CoopGameConfig.DescPanelOffsetX = value; break;
            case 8: CoopGameConfig.DescPanelOffsetY = value; break;
            case 9: CoopGameConfig.DescPanelScale = value; break;
            default: CoopGameConfig.MaldicionColumnOffset = value; break;
        }
    }

    private void HandleTreeInput()
    {
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        // Tab / PageDown: siguiente parámetro. Shift+Tab / PageUp: anterior
        bool next = Input.GetKeyDown(KeyCode.PageDown) || (Input.GetKeyDown(KeyCode.Tab) && !shift);
        bool prev = Input.GetKeyDown(KeyCode.PageUp) || (Input.GetKeyDown(KeyCode.Tab) && shift);
        if (next) treeSelectedIndex = (treeSelectedIndex + 1) % TreeParamCount;
        else if (prev) treeSelectedIndex = (treeSelectedIndex - 1 + TreeParamCount) % TreeParamCount;

        // Derecha/Arriba suben el valor, Izquierda/Abajo lo bajan; mantener pulsado repite
        bool incKey = Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.UpArrow);
        bool decKey = Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.DownArrow);
        bool incDown = Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.UpArrow);
        bool decDown = Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.DownArrow);

        int dir = 0;
        if (incDown) dir = 1;
        else if (decDown) dir = -1;

        if (incDown || decDown)
        {
            treeRepeatTimer = 0f;
        }
        else if (incKey != decKey)
        {
            treeRepeatTimer += Time.unscaledDeltaTime;
            if (treeRepeatTimer >= KeyRepeatInitialDelay)
            {
                dir = incKey ? 1 : -1;
                treeRepeatTimer = KeyRepeatInitialDelay - KeyRepeatRate;
            }
        }
        else
        {
            treeRepeatTimer = 0f;
        }

        if (dir != 0)
        {
            float step = shift ? TreeParamFastStep[treeSelectedIndex] : TreeParamStep[treeSelectedIndex];
            SetTreeParam(treeSelectedIndex, GetTreeParam(treeSelectedIndex) + dir * step);
            SwordElementalUpgrades.ApplyTreeLayoutLive();
        }

        // Backspace: restablece el parámetro seleccionado a su default (sin guardar)
        if (Input.GetKeyDown(KeyCode.Backspace))
        {
            ResetTreeParam(treeSelectedIndex);
        }
    }

    private void ResetTreeParam(int i)
    {
        SetTreeParam(i, GetTreeDefault(i));
        SwordElementalUpgrades.ApplyTreeLayoutLive();
        SetStatus($"{TreeParamLabels[i]} restablecido a default (sin guardar; Enter guarda).");
    }

    private void SaveTreeLayout()
    {
        CoopGameConfig.Save();
        SetStatus("Árbol y panel de descripción guardados en coop_game_settings.cfg.");
    }

    private string FormatTreeParam(int i)
    {
        bool isScale = TreeParamStep[i] < 1f;
        string fmt = isScale ? "F2" : "F1";
        return $"{GetTreeParam(i).ToString(fmt)}  (def {GetTreeDefault(i).ToString(fmt)})";
    }

    private void DrawTreeWindow()
    {
        // Ventana flotante arrastrable, mantenida dentro de la pantalla
        treeWindowRect.width = 420f;
        treeWindowRect.height = 395f;
        treeWindowRect = GUI.Window(TreeWindowId, treeWindowRect, DrawTreeWindowContent, "Árbol de habilidades - calibración");
        treeWindowRect.x = Mathf.Clamp(treeWindowRect.x, 0f, Mathf.Max(0f, Screen.width - treeWindowRect.width));
        treeWindowRect.y = Mathf.Clamp(treeWindowRect.y, 0f, Mathf.Max(0f, Screen.height - treeWindowRect.height));
    }

    private void DrawTreeWindowContent(int id)
    {
        GUILayout.Space(4);

        for (int i = 0; i < TreeParamCount; i++)
        {
            bool selected = i == treeSelectedIndex;

            GUILayout.BeginHorizontal();
            GUI.backgroundColor = selected ? Color.yellow : Color.gray;
            if (GUILayout.Button(TreeParamLabels[i], GUILayout.Width(220), GUILayout.Height(20)))
            {
                treeSelectedIndex = i;
            }
            GUI.backgroundColor = Color.white;

            GUI.color = selected ? Color.cyan : Color.white;
            GUILayout.Label(FormatTreeParam(i), GUILayout.Width(150));
            GUI.color = Color.white;
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(4);
        GUILayout.Label("<size=10>Tab/Shift+Tab o PgUp/PgDn: parámetro | ←→ / ↑↓: ajustar (mantener repite; Shift: rápido) | Backspace: default | Enter/F8: guardar</size>");
        if (!SwordElementalUpgrades.IsSkillMenuActive)
        {
            GUILayout.Label("<size=10>Abre el menú de habilidades para ver el cambio en vivo (los valores se guardan igual).</size>");
        }

        GUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(0.2f, 0.7f, 0.2f);
        if (GUILayout.Button("GUARDAR (Enter)", GUILayout.Height(24)))
        {
            SaveTreeLayout();
        }
        GUI.backgroundColor = new Color(0.7f, 0.4f, 0.1f);
        if (GUILayout.Button("Default param. (Backspace)", GUILayout.Height(24)))
        {
            ResetTreeParam(treeSelectedIndex);
        }
        GUI.backgroundColor = Color.white;
        GUILayout.EndHorizontal();

        GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
    }

    private void DrawElementButton(int index)
    {
        bool isSelected = (index == selectedIndex);
        if (isSelected)
        {
            GUI.backgroundColor = Color.yellow;
        }
        else
        {
            GUI.backgroundColor = Color.gray;
        }

        string name = ElementNames[index].Replace("P1 ", "").Replace("P2 ", "").Replace("P3 ", "").Replace("P4 ", "");
        if (GUILayout.Button(name, GUILayout.Height(20)))
        {
            selectedIndex = index;
        }

        GUI.backgroundColor = Color.white;
    }

    private void Nudge(int index, Vector2 delta)
    {
        RectTransform target = GetTargetRect(index);
        if (target != null)
        {
            float step = 1f;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) step = 10f;
            else if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) step = 0.25f;

            target.anchoredPosition += delta * step;
            SyncToConfig(index, target.anchoredPosition, target.localScale.x);
        }
    }

    private void DrawSelectionOutline()
    {
        try
        {
            RectTransform target = GetTargetRect(selectedIndex);
            if (target == null) return;

            Vector3[] corners = new Vector3[4];
            target.GetWorldCorners(corners);

            Canvas canvas = target.GetComponentInParent<Canvas>();
            Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

            Vector2 p0 = WorldToGUIPoint(cam, corners[0]);
            Vector2 p1 = WorldToGUIPoint(cam, corners[1]);
            Vector2 p2 = WorldToGUIPoint(cam, corners[2]);
            Vector2 p3 = WorldToGUIPoint(cam, corners[3]);

            float xMin = Mathf.Min(Mathf.Min(p0.x, p1.x), Mathf.Min(p2.x, p3.x));
            float xMax = Mathf.Max(Mathf.Max(p0.x, p1.x), Mathf.Max(p2.x, p3.x));
            float yMin = Mathf.Min(Mathf.Min(p0.y, p1.y), Mathf.Min(p2.y, p3.y));
            float yMax = Mathf.Max(Mathf.Max(p0.y, p1.y), Mathf.Max(p2.y, p3.y));

            Rect screenRect = new Rect(xMin - 4f, yMin - 4f, (xMax - xMin) + 8f, (yMax - yMin) + 8f);

            // Parpadeo suave
            float pulse = Mathf.PingPong(Time.unscaledTime * 3f, 1f);
            Color outlineColor = Color.Lerp(Color.yellow, Color.white, pulse);

            DrawRectOutline(screenRect, outlineColor, 2f);
        }
        catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "HudLayoutDebugEditor.cs:969"); }
    }

    private static Vector2 WorldToGUIPoint(Camera cam, Vector3 worldPoint)
    {
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(cam, worldPoint);
        return new Vector2(screenPoint.x, Screen.height - screenPoint.y);
    }

    private static void DrawRectOutline(Rect rect, Color color, float thickness)
    {
        if (outlineTexture == null) return;
        GUI.color = color;
        // Borde superior
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), outlineTexture);
        // Borde inferior
        GUI.DrawTexture(new Rect(rect.x, rect.y + rect.height - thickness, rect.width, thickness), outlineTexture);
        // Borde izquierdo
        GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), outlineTexture);
        // Borde derecho
        GUI.DrawTexture(new Rect(rect.x + rect.width - thickness, rect.y, thickness, rect.height), outlineTexture);
        GUI.color = Color.white;
    }

    private static void EnsureTextures()
    {
        if (boxTexture == null)
        {
            boxTexture = new Texture2D(1, 1);
            boxTexture.hideFlags = HideFlags.HideAndDontSave;
            boxTexture.SetPixel(0, 0, Color.white);
            boxTexture.Apply();
        }
        if (outlineTexture == null)
        {
            outlineTexture = new Texture2D(1, 1);
            outlineTexture.hideFlags = HideFlags.HideAndDontSave;
            outlineTexture.SetPixel(0, 0, Color.white);
            outlineTexture.Apply();
        }
    }
}
