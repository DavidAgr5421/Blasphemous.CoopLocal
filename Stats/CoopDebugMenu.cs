using System;
using System.Collections.Generic;
using Framework.Managers;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using UnityEngine;
using UnityEngine.UI;

namespace Blasphemous.CoopLocal;

// Menú de debug F8 con interfaz in-game (uGUI, mismo estilo que CoopPlayerPauseMenu) navegable
// solo con teclado/mando. Reúne: selección de jugador objetivo, habilidades del árbol, guardado
// coop, stats permanentes (PermanetBonus, antes en PermanentStatsDebugPanel IMGUI) y el ciclador
// de Elemental + Rezo (SwordElementalUpgrades.CycleDebugElementalAndPrayers).
//
// Controles (acepta teclado + cualquier mando conectado):
//   Arriba/Abajo (W/S, flechas, stick/D-pad) : mover selección (con wrap)
//   Izq/Der      (A/D, flechas, stick/D-pad) : cambia jugador en la fila "Jugador";
//                                              cambia el valor en las filas de stat; nada en el resto
//   Confirmar    (Enter/Espacio, A)          : activar/desactivar habilidad / ejecutar acción / reset stat
//   Cancelar     (Esc/Backspace/F8, B)       : cerrar
// Shift+F8 sigue abriendo el panel IMGUI antiguo (PermanentStatsDebugPanel).
public class CoopDebugMenu : MonoBehaviour
{
    private static CoopDebugMenu _instance;
    public static bool IsOpen { get; private set; }

    private const int VisibleRows = 12;
    private const float RepeatDelay = 0.16f;
    private const float PlayerRepeatDelay = 0.22f;
    private const float StatRepeatDelay = 0.12f;

    private GameObject rootObj;
    private Text titleText;
    private Text subtitleText;
    private Text legendText;
    private Text scrollText;
    private readonly List<Text> rowTexts = new List<Text>();
    private Font gameFont;

    private int targetPlayer = 0;
    private int selected = 0;
    private int scrollTop = 0;
    private float cooldown = 0f;

    // Layout explícito de filas (orden de la lista). Las 7 filas de stat siguen el orden de
    // PermanentStatsDebugPanel.StatsTypes (Life, Fervour, Strength, FlaskHealth, Flask, BeadSlots, MeaCulpa).
    private const int RowPlayer = 0;
    private const int RowUnlockAll = 1;
    private const int RowLockAll = 2;
    private const int RowSaveCoop = 3;
    private const int FirstStatRow = 4;
    private const int StatCount = 7;
    private const int RowSaveStatsBaseline = FirstStatRow + StatCount; // 11
    private const int RowElementalCycle = RowSaveStatsBaseline + 1;    // 12
    private const int FirstSkillRow = RowElementalCycle + 1;           // 13
    private static int TotalRows => FirstSkillRow + Player2SkillManager.AllIds.Length;

    internal static void Toggle()
    {
        EnsureCreated();
        if (IsOpen) _instance.Hide();
        else _instance.Show();
    }

    internal static void EnsureCreated()
    {
        if (_instance != null) return;
        var go = new GameObject("CoopDebugMenu");
        UnityEngine.Object.DontDestroyOnLoad(go);
        _instance = go.AddComponent<CoopDebugMenu>();
    }

    private void Awake()
    {
        _instance = this;
    }

    private void Show()
    {
        if (Core.Logic?.Penitent == null) return; // sin partida cargada
        CreateUI();
        IsOpen = true;
        selected = 0;
        scrollTop = 0;
        cooldown = 0.25f;
        targetPlayer = 0;

        Core.Logic.PauseGame();
        Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
        CoopGameSetupUI.ApplyGameFont(rootObj);
        rootObj.SetActive(true);
        CoopUiFontScaler.NotifyMenuShown();
        Refresh();
    }

    private void Hide()
    {
        IsOpen = false;
        if (rootObj != null) rootObj.SetActive(false);
        Core.Logic?.ResumeGame();
        Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
    }

    private Text MakeText(Transform parent, string name, Vector2 pos, Vector2 size, int fontSize, TextAnchor anchor, Color color, bool outline)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var t = obj.AddComponent<Text>();
        t.font = gameFont;
        t.fontSize = fontSize;
        t.alignment = anchor;
        t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        if (outline)
        {
            var o = obj.AddComponent<Outline>();
            o.effectColor = Color.black;
            o.effectDistance = new Vector2(1.5f, -1.5f);
        }
        return t;
    }

    private void CreateUI()
    {
        if (rootObj != null) return;
        gameFont = CoopGameSetupUI.GetGameFont();

        rootObj = new GameObject("CoopDebugCanvas");
        rootObj.transform.SetParent(transform, false);
        var canvas = rootObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32001;
        var scaler = rootObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var overlay = new GameObject("DarkOverlay");
        overlay.transform.SetParent(rootObj.transform, false);
        var oRt = overlay.AddComponent<RectTransform>();
        oRt.anchorMin = Vector2.zero;
        oRt.anchorMax = Vector2.one;
        oRt.sizeDelta = Vector2.zero;
        overlay.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);

        var panel = new GameObject("Panel");
        panel.transform.SetParent(rootObj.transform, false);
        var pRt = panel.AddComponent<RectTransform>();
        pRt.anchorMin = pRt.anchorMax = pRt.pivot = new Vector2(0.5f, 0.5f);
        pRt.sizeDelta = new Vector2(900f, 650f);
        panel.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.09f, 0.98f);
        var pOut = panel.AddComponent<Outline>();
        pOut.effectColor = new Color(0.70f, 0.58f, 0.32f, 0.90f);
        pOut.effectDistance = new Vector2(3f, -3f);
        CoopUiStyle.StylePanel(panel);

        titleText = MakeText(panel.transform, "Title", new Vector2(0f, 275f), new Vector2(860f, 45f), 28, TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.65f), true);
        subtitleText = MakeText(panel.transform, "Subtitle", new Vector2(0f, 232f), new Vector2(860f, 30f), 18, TextAnchor.MiddleCenter, new Color(0.8f, 0.8f, 0.8f), false);

        var line = new GameObject("Divider");
        line.transform.SetParent(panel.transform, false);
        var lRt = line.AddComponent<RectTransform>();
        lRt.anchoredPosition = new Vector2(0f, 207f);
        lRt.sizeDelta = new Vector2(820f, 2f);
        line.AddComponent<Image>().color = new Color(0.60f, 0.50f, 0.28f, 0.80f);

        for (int i = 0; i < VisibleRows; i++)
        {
            rowTexts.Add(MakeText(panel.transform, $"Row_{i}", new Vector2(0f, 175f - i * 34f), new Vector2(760f, 32f), 21, TextAnchor.MiddleCenter, Color.white, true));
        }

        scrollText = MakeText(panel.transform, "Scroll", new Vector2(390f, -190f), new Vector2(100f, 30f), 15, TextAnchor.MiddleRight, new Color(0.6f, 0.6f, 0.6f), false);
        legendText = MakeText(panel.transform, "Legend", new Vector2(0f, -285f), new Vector2(860f, 35f), 15, TextAnchor.MiddleCenter, new Color(0.7f, 0.7f, 0.7f), false);

        rootObj.SetActive(false);
    }

    // ---- Datos ----
    private static bool IsSkillOn(int player, string id)
    {
        try
        {
            if (player == 0) return Core.SkillManager != null && Core.SkillManager.IsSkillUnlocked(id);
            return Player2SkillManager.IsUnlocked(player, id);
        }
        catch { return false; }
    }

    private static void SetSkill(int player, string id, bool on)
    {
        try
        {
            if (player == 0)
            {
                if (Core.SkillManager == null) return;
                SwordElementalUpgrades.EnsureSkillsRegistered(Core.SkillManager);
                if (on) Core.SkillManager.UnlockSkill(id, ignoreChecks: true);
                else Core.SkillManager.LockSkill(id);
            }
            else
            {
                Player2SkillManager.SetUnlocked(player, id, on);
            }
        }
        catch (Exception ex)
        {
            DashParryDebugLog.Log($"[CoopDebugMenu] Error cambiando {id} en P{player + 1}: {ex.Message}");
        }
    }

    // Attr de PermanetBonus del jugador dado para la stat de la fila (null si el penitent no existe).
    private static Framework.FrameworkCore.Attributes.Logic.Attribute GetStatAttr(int player, int row)
    {
        Penitent p = CoopPlayerRegistry.GetPenitent(player);
        if (p == null) return null;
        return p.Stats.GetByType(PermanentStatsDebugPanel.StatsTypes[row - FirstStatRow]);
    }

    private static bool IsStatRow(int row) => row >= FirstStatRow && row < FirstStatRow + StatCount;

    private static string SkillLabel(string id)
    {
        int us = id.LastIndexOf('_');
        string family = id.Substring(0, us);
        string tier = id.Substring(us + 1);
        string name;
        switch (family)
        {
            case "CHARGED": name = "Golpe Cargado"; break;
            case "COMBO": name = "Combo"; break;
            case "LUNGE": name = "Estocada"; break;
            case "RANGED": name = "Ataque a Distancia"; break;
            case "VERTICAL": name = "Ataque Vertical"; break;
            case "VENENO": name = "Veneno"; break;
            case "SANGRADO": name = "Sangrado"; break;
            case "ELECTRICO": name = "Electricidad"; break;
            case "MALDICION": name = "Maldición"; break;
            default: name = family; break;
        }
        return $"{name}  Nv.{tier}";
    }

    private List<int> AvailablePlayers()
    {
        var list = new List<int> { 0 };
        int max = Mathf.Clamp(CoopGameConfig.PlayerCount, 1, 4);
        for (int i = 1; i < max; i++)
        {
            if (CoopPlayerRegistry.GetPenitent(i) != null) list.Add(i);
        }
        return list;
    }

    private void SwitchPlayer(int dir)
    {
        var avail = AvailablePlayers();
        int idx = avail.IndexOf(targetPlayer);
        if (idx < 0) idx = 0;
        idx = (idx + dir + avail.Count) % avail.Count;
        targetPlayer = avail[idx];
    }

    // ---- Render ----
    private void Refresh()
    {
        Color pc = CoopPlayerRegistry.GetLabelColor(targetPlayer);
        titleText.text = $"[P{targetPlayer + 1} - {CoopPlayerRegistry.GetDisplayName(targetPlayer)}] MENÚ DEBUG (F8)";
        titleText.color = pc;

        int on = 0;
        foreach (string id in Player2SkillManager.AllIds) if (IsSkillOn(targetPlayer, id)) on++;
        subtitleText.text = $"Stats permanentes, habilidades y utilidades   |   Jugador {targetPlayer + 1}   |   Habilidades activas: {on}/{Player2SkillManager.AllIds.Length}";

        if (selected < scrollTop) scrollTop = selected;
        if (selected >= scrollTop + VisibleRows) scrollTop = selected - VisibleRows + 1;

        for (int i = 0; i < VisibleRows; i++)
        {
            int row = scrollTop + i;
            Text t = rowTexts[i];
            if (row >= TotalRows) { t.text = ""; continue; }

            bool sel = row == selected;
            string label;
            Color col;
            if (row == RowPlayer)
            {
                label = $"Jugador:  ◄ P{targetPlayer + 1} - {CoopPlayerRegistry.GetDisplayName(targetPlayer)} ►";
                col = pc;
            }
            else if (row == RowUnlockAll) { label = "Desbloquear TODAS habilidades"; col = new Color(0.85f, 0.78f, 0.55f); }
            else if (row == RowLockAll) { label = "Bloquear TODAS habilidades"; col = new Color(0.85f, 0.78f, 0.55f); }
            else if (row == RowSaveCoop) { label = "Guardar partida coop"; col = new Color(0.85f, 0.78f, 0.55f); }
            else if (IsStatRow(row))
            {
                string statName = PermanentStatsDebugPanel.StatsTypes[row - FirstStatRow].ToString();
                var attr = GetStatAttr(targetPlayer, row);
                if (attr == null)
                {
                    label = $"{statName.PadRight(14)}(no disponible)";
                    col = new Color(0.45f, 0.45f, 0.45f);
                }
                else
                {
                    label = $"{statName.PadRight(14)}{attr.PermanetBonus.ToString("F1").PadLeft(5)}      (◄ -1 | +1 ►)";
                    col = new Color(0.9f, 0.9f, 0.9f);
                }
            }
            else if (row == RowSaveStatsBaseline)
            {
                label = targetPlayer == 0 ? "Guardar stats baseline (P2-P4)  [solo P2-P4]" : "Guardar stats baseline (P2-P4)";
                col = new Color(0.85f, 0.78f, 0.55f);
            }
            else if (row == RowElementalCycle) { label = "Ciclar Elemental + Rezo (prueba)"; col = new Color(0.85f, 0.78f, 0.55f); }
            else
            {
                string id = Player2SkillManager.AllIds[row - FirstSkillRow];
                bool unlocked = IsSkillOn(targetPlayer, id);
                label = $"{SkillLabel(id),-28}{(unlocked ? "[ ACTIVA ]" : "[  ---   ]")}";
                col = unlocked ? new Color(0.55f, 1f, 0.55f) : new Color(0.75f, 0.75f, 0.75f);
            }
            if (sel) col = new Color(1f, 0.90f, 0.40f);
            t.text = sel ? $"►  {label}  ◄" : label;
            t.color = col;
        }

        scrollText.text = $"{selected + 1}/{TotalRows}";
        legendText.text = "[↑/↓] Navegar  |  [←/→] Cambiar valor/jugador  |  [A/Enter] Activar/Reset  |  [B/Esc/F8] Cerrar";
    }

    // ---- Input ----
    private void Update()
    {
        if (!IsOpen) return;
        if (cooldown > 0f) { cooldown -= Time.unscaledDeltaTime; return; }

        bool up = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
        bool down = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
        bool left = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
        bool right = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);
        bool confirm = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space);
        bool cancel = Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.F8);

        if (Rewired.ReInput.isReady && Rewired.ReInput.controllers != null)
        {
            foreach (var pad in Rewired.ReInput.controllers.Joysticks)
            {
                up |= Player2Pad.GetUp(pad);
                down |= Player2Pad.GetDown(pad);
                left |= Player2Pad.GetLeft(pad);
                right |= Player2Pad.GetRight(pad);
                confirm |= Player2Pad.GetSelectDown(pad);
                cancel |= Player2Pad.GetCancelDown(pad);
            }
        }

        if (cancel) { cooldown = 0.2f; Hide(); return; }

        if (up || down)
        {
            selected = (selected + (down ? 1 : -1) + TotalRows) % TotalRows;
            cooldown = RepeatDelay;
            Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
            Refresh();
            return;
        }

        if (left || right)
        {
            int dir = right ? 1 : -1;
            if (selected == RowPlayer)
            {
                SwitchPlayer(dir);
                cooldown = PlayerRepeatDelay;
            }
            else if (IsStatRow(selected))
            {
                AdjustStat(dir);
                cooldown = StatRepeatDelay;
            }
            else
            {
                return;
            }
            Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
            Refresh();
            return;
        }

        if (confirm)
        {
            cooldown = 0.2f;
            Execute(selected);
            Refresh();
        }
    }

    // dir > 0: +1 (attr.Upgrade). dir < 0: -1 (SetPermanentBonus, mínimo 0).
    private void AdjustStat(int dir)
    {
        var attr = GetStatAttr(targetPlayer, selected);
        if (attr == null) return;
        if (dir > 0) attr.Upgrade();
        else attr.SetPermanentBonus(Mathf.Max(0f, attr.PermanetBonus - 1f));
    }

    private void Execute(int row)
    {
        if (row == RowPlayer)
        {
            return;
        }
        if (row == RowUnlockAll || row == RowLockAll)
        {
            bool on = row == RowUnlockAll;
            foreach (string id in Player2SkillManager.AllIds) SetSkill(targetPlayer, id, on);
            Core.Audio?.PlayOneShot(on ? "event:/SFX/UI/EquipItem" : "event:/SFX/UI/UnequipItem");
        }
        else if (row == RowSaveCoop)
        {
            Player2SkillManager.Persist();
            Core.Audio?.PlayOneShot("event:/SFX/UI/ButtonPress");
        }
        else if (IsStatRow(row))
        {
            var attr = GetStatAttr(targetPlayer, row);
            if (attr == null) return;
            attr.SetPermanentBonus(0f);
            Core.Audio?.PlayOneShot("event:/SFX/UI/ButtonPress");
        }
        else if (row == RowSaveStatsBaseline)
        {
            // Solo P2-P4: P1 no tiene baseline de prueba (ver NOTES, PermanentStatsDebugPanel).
            if (targetPlayer == 0) return;
            Penitent p = CoopPlayerRegistry.GetPenitent(targetPlayer);
            if (p == null) return;
            Player2StatsSync.PersistPermanentBonus(p);
            Core.Audio?.PlayOneShot("event:/SFX/UI/ButtonPress");
        }
        else if (row == RowElementalCycle)
        {
            SwordElementalUpgrades.CycleDebugElementalAndPrayers();
            Core.Audio?.PlayOneShot("event:/SFX/UI/ButtonPress");
        }
        else
        {
            string id = Player2SkillManager.AllIds[row - FirstSkillRow];
            bool now = IsSkillOn(targetPlayer, id);
            SetSkill(targetPlayer, id, !now);
            Core.Audio?.PlayOneShot(!now ? "event:/SFX/UI/EquipItem" : "event:/SFX/UI/UnequipItem");
        }
    }
}
