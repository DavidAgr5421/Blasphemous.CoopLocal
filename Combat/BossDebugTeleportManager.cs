using System;
using System.Collections.Generic;
using Framework.FrameworkCore;
using Framework.Managers;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using Gameplay.UI;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.CoopLocal.Combat;

/// <summary>
/// Entrada que describe un jefe disponible para depuración y práctica.
/// </summary>
internal sealed class BossDebugEntry
{
    public string Name { get; }
    public string SceneName { get; }
    public string[] FlagsToSetFalse { get; }
    public string[] FlagsToSetTrue { get; }

    public BossDebugEntry(string name, string sceneName, string[] flagsToSetFalse, string[] flagsToSetTrue = null)
    {
        Name = name;
        SceneName = sceneName;
        FlagsToSetFalse = flagsToSetFalse ?? new string[0];
        FlagsToSetTrue = flagsToSetTrue ?? new string[0];
    }
}

/// <summary>
/// Gestor de teletransporte a peleas de jefes en modo de prueba / práctica (F2).
/// Permite acceder a cualquier combate sin otorgar dinero (Purge) y ofrece un botón
/// para regresar de inmediato a la ubicación exacta donde se encontraba el jugador.
/// </summary>
internal static class BossDebugTeleportManager
{
    private static GameObject driverObject;

    public static bool IsMenuOpen { get; private set; }
    public static bool IsDebugBossFightActive { get; private set; }
    public static bool HasSavedOrigin { get; private set; }

    public static string SavedLevelName => savedLevelName;
    private static string savedLevelName = string.Empty;
    private static Vector3 savedPosition = Vector3.zero;
    private static EntityOrientation savedOrientation = EntityOrientation.Right;

    private static Dictionary<string, bool> originalFlags = new Dictionary<string, bool>();
    private static readonly float[] savedPurge = new float[CoopPlayerRegistry.MaxPlayers];
    private static bool isReturningFromBoss = false;

    /// <summary>
    /// Catálogo completo de los 15 jefes del juego base y expansiones.
    /// </summary>
    public static readonly BossDebugEntry[] Bosses = new BossDebugEntry[]
    {
        new BossDebugEntry("Guardián del Silencio", "D17Z01S01",
            new[] { "D17Z01_BOSSDEAD", "D17Z01S01_BOSSDEAD" }),

        new BossDebugEntry("Ten Piedad", "D01Z06S01",
            new[] { "D01Z06S01_BOSSDEAD" }),

        new BossDebugEntry("Tres Angustias", "D02Z05S01",
            new[] { "D02Z05S01_BOSSDEAD" }),

        new BossDebugEntry("Ntra. Sra. de la Faz Desfigurada", "D02Z03S21",
            new[] { "D02Z03S21_BOSSDEAD" }),

        new BossDebugEntry("Melquíades, el Arzobispo Exhumado", "D04Z04S01",
            new[] { "D04Z04S01_BOSSDEAD" }),

        new BossDebugEntry("Expósito, Vástago de la Abjuración", "D05Z01S24",
            new[] { "D05Z01S24_BOSSDEAD" }),

        new BossDebugEntry("Esdras, de la Legión Ungida", "D03Z04S01",
            new[] { "D03Z04S01_BOSSDEAD", "BROTHERS_EVENT1_COMPLETED", "BROTHERS_EVENTPERPETVUA_COMPLETED" }),

        new BossDebugEntry("Quirce, Devuelto por las Llamas", "D08Z01S01",
            new[] { "D08Z01S01_BOSSDEAD" }),

        new BossDebugEntry("Crisanta de la Agonía Vendada", "D06Z01S25",
            new[] { "D06Z01S25_BOSSDEAD", "CRISANTA_DEFEATED" }),

        new BossDebugEntry("Su Santidad Escribar", "D07Z01S04",
            new[] { "D07Z01S04_BOSSDEAD", "PONTIFF_HUSK_DEFEATED" }),

        new BossDebugEntry("Último Hijo del Milagro", "D07Z01S05",
            new[] { "D07Z01S05_BOSSDEAD" }),

        new BossDebugEntry("Sierpes", "D20Z02S08",
            new[] { "D20Z02S08_BOSSDEAD" },
            new[] { "SNAKE_UNLOCK" }),

        new BossDebugEntry("Isidora, Voz de los Muertos", "D01BZ08S01",
            new[] { "D01BZ08S01_BOSSDEAD" },
            new[] { "ISIDORA_UNLEASHED", "OSSUARY_COMPLETED" }),

        new BossDebugEntry("Amanecida del Cincel Labrado", "D08Z02S03",
            new[] { "D08Z02S03_BOSSDEAD", "AMANECIDA_AXE_DEFEATED", "SANTOS_AMANECIDA_BOW_DEFEATED" }),

        new BossDebugEntry("Laudes, Primera de las Amanecidas", "D21Z01S01",
            new[] { "D21Z01S01_BOSSDEAD", "SANTOS_LAUDES_DEFEATED", "LAUDES_DEFEATED" })
    };

    public static void EnsureCreated()
    {
        if (driverObject != null) return;

        driverObject = new GameObject("BossDebugTeleportUI_Driver");
        UnityEngine.Object.DontDestroyOnLoad(driverObject);
        driverObject.AddComponent<BossDebugTeleportUI>();
    }

    public static void Dispose()
    {
        if (driverObject != null)
        {
            UnityEngine.Object.Destroy(driverObject);
            driverObject = null;
        }
    }

    public static void ToggleMenu()
    {
        if (Core.Logic?.Penitent == null) return;
        SetMenuOpen(!IsMenuOpen);
    }

    public static void SetMenuOpen(bool open)
    {
        if (IsMenuOpen == open) return;

        IsMenuOpen = open;
        Player2MenuView.LastMenuToggleFrame = Time.frameCount;

        if (Core.Input != null)
        {
            Core.Input.SetBlocker("BOSS_DEBUG_MENU", IsMenuOpen);
        }

        if (Core.Audio != null)
        {
            Core.Audio.PlayOneShot(IsMenuOpen ? "event:/SFX/UI/ChangeOption" : "event:/SFX/UI/UnequipItem");
        }
    }

    public static void TeleportToBoss(BossDebugEntry entry)
    {
        if (entry == null) return;
        if (Core.Logic?.Penitent == null) return;

        // Guardar origen únicamente si no estamos ya dentro del modo práctica de jefes
        if (!IsDebugBossFightActive)
        {
            Core.SpawnManager.SetCurrentToCustomSpawnData(IsMiriam: false);
            savedLevelName = Core.LevelManager.currentLevel != null ? Core.LevelManager.currentLevel.LevelName : string.Empty;
            savedPosition = Core.Logic.Penitent.transform.position;
            savedOrientation = Core.Logic.Penitent.Status.Orientation;
            HasSavedOrigin = true;

            SavePurgeBalances();
        }

        // Guardar banderas previas y fijar las necesarias para que el jefe aparezca vivo
        if (originalFlags == null)
        {
            originalFlags = new Dictionary<string, bool>();
        }

        if (entry.FlagsToSetFalse != null)
        {
            foreach (string flag in entry.FlagsToSetFalse)
            {
                if (!string.IsNullOrEmpty(flag))
                {
                    if (!originalFlags.ContainsKey(flag))
                    {
                        originalFlags[flag] = Core.Events.GetFlag(flag);
                    }
                    Core.Events.SetFlag(flag, false);
                }
            }
        }

        if (entry.FlagsToSetTrue != null)
        {
            foreach (string flag in entry.FlagsToSetTrue)
            {
                if (!string.IsNullOrEmpty(flag))
                {
                    if (!originalFlags.ContainsKey(flag))
                    {
                        originalFlags[flag] = Core.Events.GetFlag(flag);
                    }
                    Core.Events.SetFlag(flag, true);
                }
            }
        }

        IsDebugBossFightActive = true;
        SetMenuOpen(false);

        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info($"[BossDebug] Iniciando modo práctica contra: {entry.Name} ({entry.SceneName})", Main.CoopLocal);
        }

        // Carga directa y segura de la escena del jefe
        Core.SpawnManager.PrepareForCommandSpawn(entry.SceneName);
        Core.LevelManager.ChangeLevel(entry.SceneName, false, true, true);
    }

    public static void ReturnToSavedOrigin()
    {
        if (!HasSavedOrigin || string.IsNullOrEmpty(savedLevelName))
        {
            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Warn("[BossDebug] No hay ubicación de origen guardada para regresar.", Main.CoopLocal);
            }
            return;
        }

        SetMenuOpen(false);

        // Restaurar todas las banderas originales de los jefes
        if (originalFlags != null)
        {
            foreach (KeyValuePair<string, bool> kvp in originalFlags)
            {
                Core.Events.SetFlag(kvp.Key, kvp.Value);
            }
            originalFlags.Clear();
        }

        // Limpiar el estado de muerte pendiente de todos los jugadores para que reaparezcan vivos
        Player1DeathState.ClearPendingRevive();
        for (int i = 1; i < CoopPlayerRegistry.MaxPlayers; i++)
        {
            Player2DeathState.ClearPendingRevive(i);
        }

        // Restaurar saldos de dinero
        RestorePurgeBalances();
        if (UIController.instance != null)
        {
            UIController.instance.UpdatePurgePoints();
        }

        isReturningFromBoss = true;

        // Asegurar que SpawnManager use los datos de spawn del origen guardado
        var customLevelField = AccessTools.Field(typeof(SpawnManager), "customLevel");
        var customPosField = AccessTools.Field(typeof(SpawnManager), "customPosition");
        var customOrientField = AccessTools.Field(typeof(SpawnManager), "customOrientation");
        var customMiriamField = AccessTools.Field(typeof(SpawnManager), "customIsMiriam");

        if (customLevelField != null && customPosField != null && customOrientField != null)
        {
            customMiriamField?.SetValue(Core.SpawnManager, false);
            customLevelField.SetValue(Core.SpawnManager, savedLevelName);
            customPosField.SetValue(Core.SpawnManager, savedPosition);
            customOrientField.SetValue(Core.SpawnManager, savedOrientation);
        }

        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info($"[BossDebug] Regresando a la ubicación original: {savedLevelName} en {savedPosition}", Main.CoopLocal);
        }

        try
        {
            Core.SpawnManager.SpawnFromCustom(usefade: true);
        }
        finally
        {
            IsDebugBossFightActive = false;
            HasSavedOrigin = false;
        }
    }

    public static void OnLevelLoaded(Level oldLevel, Level newLevel)
    {
        if (isReturningFromBoss)
        {
            isReturningFromBoss = false;

            // Reposicionar y restaurar vitales de P1
            if (Core.Logic?.Penitent != null)
            {
                Core.Logic.Penitent.transform.position = savedPosition;
                Core.Logic.Penitent.SetOrientation(savedOrientation);
                if (Core.Logic.Penitent.Stats?.Life != null)
                {
                    Core.Logic.Penitent.Stats.Life.SetToCurrentMax();
                }
                if (Core.Logic.Penitent.Stats?.Fervour != null)
                {
                    Core.Logic.Penitent.Stats.Fervour.SetToCurrentMax();
                }
            }

            // Reposicionar y restaurar vitales de los jugadores cooperativos (P2..P4)
            foreach (Penitent extra in CoopLocal.GetAllExtraPlayers())
            {
                if (extra != null)
                {
                    extra.transform.position = savedPosition;
                    extra.SetOrientation(savedOrientation);
                    if (extra.Stats?.Life != null)
                    {
                        extra.Stats.Life.SetToCurrentMax();
                    }
                    if (extra.Stats?.Fervour != null)
                    {
                        extra.Stats.Fervour.SetToCurrentMax();
                    }
                }
            }

            RestorePurgeBalances();
            if (UIController.instance != null)
            {
                UIController.instance.UpdatePurgePoints();
            }
        }
    }

    public static void SavePurgeBalances()
    {
        for (int i = 0; i < CoopPlayerRegistry.MaxPlayers; i++)
        {
            Penitent p = CoopPlayerRegistry.GetPenitent(i);
            if (p != null && p.Stats != null && p.Stats.Purge != null)
            {
                savedPurge[i] = p.Stats.Purge.Current;
            }
        }
    }

    public static void EnforcePurgeBalances()
    {
        if (!IsDebugBossFightActive) return;

        bool updated = false;
        for (int i = 0; i < CoopPlayerRegistry.MaxPlayers; i++)
        {
            Penitent p = CoopPlayerRegistry.GetPenitent(i);
            if (p != null && p.Stats != null && p.Stats.Purge != null)
            {
                if (p.Stats.Purge.Current > savedPurge[i])
                {
                    p.Stats.Purge.Current = savedPurge[i];
                    updated = true;
                }
            }
        }
        if (updated && UIController.instance != null)
        {
            UIController.instance.UpdatePurgePoints();
        }
    }

    public static void RestorePurgeBalances()
    {
        for (int i = 0; i < CoopPlayerRegistry.MaxPlayers; i++)
        {
            Penitent p = CoopPlayerRegistry.GetPenitent(i);
            if (p != null && p.Stats != null && p.Stats.Purge != null)
            {
                p.Stats.Purge.Current = savedPurge[i];
            }
        }
    }
}

/// <summary>
/// Driver visual para la ventana en F2 y el indicador de práctica durante la pelea.
/// </summary>
internal sealed class BossDebugTeleportUI : MonoBehaviour
{
    private int selectedNavIndex = 0; // 0 = Botón Regresar, 1..N = Jefes
    private Vector2 scrollPosition = Vector2.zero;
    private float navCooldown = 0f;

    private static Texture2D texBlackOverlay;
    private static Texture2D texWindowBg;
    private static Texture2D texReturnBtn;
    private static Texture2D texReturnBtnHover;
    private static Texture2D texRowNormal;
    private static Texture2D texRowSelected;
    private static Texture2D texBannerBg;

    private static GUIStyle styleOverlay;
    private static GUIStyle styleWindow;
    private static GUIStyle styleTitle;
    private static GUIStyle styleSubtitle;
    private static GUIStyle styleReturnBtn;
    private static GUIStyle styleReturnBtnDisabled;
    private static GUIStyle styleRowNormal;
    private static GUIStyle styleRowSelected;
    private static GUIStyle styleBannerText;
    private static GUIStyle styleBannerBtn;

    private static Texture2D MakeSolidTex(int width, int height, Color col)
    {
        Color[] pix = new Color[width * height];
        for (int i = 0; i < pix.Length; i++)
        {
            pix[i] = col;
        }
        Texture2D result = new Texture2D(width, height);
        result.SetPixels(pix);
        result.Apply();
        return result;
    }

    private static void EnsureStyles()
    {
        if (texBlackOverlay == null)
        {
            texBlackOverlay = MakeSolidTex(2, 2, new Color(0f, 0f, 0f, 0.65f));
            texWindowBg = MakeSolidTex(2, 2, new Color(0.08f, 0.08f, 0.10f, 0.96f));
            texReturnBtn = MakeSolidTex(2, 2, new Color(0.48f, 0.12f, 0.12f, 0.95f));
            texReturnBtnHover = MakeSolidTex(2, 2, new Color(0.70f, 0.18f, 0.18f, 1f));
            texRowNormal = MakeSolidTex(2, 2, new Color(0.14f, 0.14f, 0.17f, 0.85f));
            texRowSelected = MakeSolidTex(2, 2, new Color(0.45f, 0.36f, 0.12f, 0.95f));
            texBannerBg = MakeSolidTex(2, 2, new Color(0.06f, 0.06f, 0.08f, 0.92f));

            styleOverlay = new GUIStyle();
            styleOverlay.normal.background = texBlackOverlay;

            styleWindow = new GUIStyle();
            styleWindow.normal.background = texWindowBg;

            styleTitle = new GUIStyle();
            styleTitle.alignment = TextAnchor.MiddleCenter;
            styleTitle.fontSize = 16;
            styleTitle.fontStyle = FontStyle.Bold;
            styleTitle.normal.textColor = new Color(0.95f, 0.82f, 0.35f);

            styleSubtitle = new GUIStyle();
            styleSubtitle.alignment = TextAnchor.MiddleCenter;
            styleSubtitle.fontSize = 11;
            styleSubtitle.normal.textColor = new Color(0.75f, 0.75f, 0.75f);

            styleReturnBtn = new GUIStyle();
            styleReturnBtn.alignment = TextAnchor.MiddleCenter;
            styleReturnBtn.fontSize = 13;
            styleReturnBtn.fontStyle = FontStyle.Bold;
            styleReturnBtn.normal.textColor = Color.white;
            styleReturnBtn.normal.background = texReturnBtn;
            styleReturnBtn.hover.background = texReturnBtnHover;

            styleReturnBtnDisabled = new GUIStyle();
            styleReturnBtnDisabled.alignment = TextAnchor.MiddleCenter;
            styleReturnBtnDisabled.fontSize = 12;
            styleReturnBtnDisabled.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
            styleReturnBtnDisabled.normal.background = texRowNormal;

            styleRowNormal = new GUIStyle();
            styleRowNormal.alignment = TextAnchor.MiddleLeft;
            styleRowNormal.fontSize = 12;
            styleRowNormal.padding = new RectOffset(14, 10, 2, 2);
            styleRowNormal.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
            styleRowNormal.normal.background = texRowNormal;

            styleRowSelected = new GUIStyle();
            styleRowSelected.alignment = TextAnchor.MiddleLeft;
            styleRowSelected.fontSize = 12;
            styleRowSelected.fontStyle = FontStyle.Bold;
            styleRowSelected.padding = new RectOffset(14, 10, 2, 2);
            styleRowSelected.normal.textColor = new Color(1f, 0.95f, 0.5f);
            styleRowSelected.normal.background = texRowSelected;

            styleBannerText = new GUIStyle();
            styleBannerText.alignment = TextAnchor.MiddleLeft;
            styleBannerText.fontSize = 11;
            styleBannerText.fontStyle = FontStyle.Bold;
            styleBannerText.normal.textColor = new Color(0.95f, 0.82f, 0.35f);

            styleBannerBtn = new GUIStyle();
            styleBannerBtn.alignment = TextAnchor.MiddleCenter;
            styleBannerBtn.fontSize = 11;
            styleBannerBtn.fontStyle = FontStyle.Bold;
            styleBannerBtn.normal.textColor = Color.white;
            styleBannerBtn.normal.background = texReturnBtn;
            styleBannerBtn.hover.background = texReturnBtnHover;
        }
    }

    private void Update()
    {
        if (BossDebugTeleportManager.IsDebugBossFightActive)
        {
            BossDebugTeleportManager.EnforcePurgeBalances();
        }

        if (!BossDebugTeleportManager.IsMenuOpen)
        {
            return;
        }

        // Bloquear permanentemente el registro de inputs de ataque/dash mientras el menú esté visible
        Player2MenuView.LastMenuToggleFrame = Time.frameCount;

        if (navCooldown > 0f)
        {
            navCooldown -= Time.unscaledDeltaTime;
        }

        // Tecla de retorno directo (Backspace o R)
        if (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.R))
        {
            if (BossDebugTeleportManager.HasSavedOrigin)
            {
                BossDebugTeleportManager.ReturnToSavedOrigin();
                return;
            }
        }

        // Salir / Cerrar con Escape
        if (Input.GetKeyDown(KeyCode.Escape) || CheckGamepadCancel())
        {
            BossDebugTeleportManager.SetMenuOpen(false);
            return;
        }

        int totalItems = BossDebugTeleportManager.Bosses.Length;

        // Navegación Arriba / Abajo
        bool moveUp = Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W) || (navCooldown <= 0f && CheckGamepadUp());
        bool moveDown = Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S) || (navCooldown <= 0f && CheckGamepadDown());

        if (moveUp)
        {
            navCooldown = 0.20f;
            if (selectedNavIndex == 0)
            {
                selectedNavIndex = totalItems;
            }
            else if (selectedNavIndex == 1)
            {
                selectedNavIndex = BossDebugTeleportManager.HasSavedOrigin ? 0 : totalItems;
            }
            else
            {
                selectedNavIndex--;
            }
            Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeOption");
            EnsureScrollVisible(selectedNavIndex);
        }
        else if (moveDown)
        {
            navCooldown = 0.20f;
            if (selectedNavIndex == 0)
            {
                selectedNavIndex = 1;
            }
            else if (selectedNavIndex >= totalItems)
            {
                selectedNavIndex = BossDebugTeleportManager.HasSavedOrigin ? 0 : 1;
            }
            else
            {
                selectedNavIndex++;
            }
            Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeOption");
            EnsureScrollVisible(selectedNavIndex);
        }

        // Confirmar selección (Enter, KeypadEnter, Espacio, K, Botón A / X del mando)
        bool confirm = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) ||
                       Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.K) ||
                       CheckGamepadConfirm();

        if (confirm)
        {
            if (selectedNavIndex == 0)
            {
                if (BossDebugTeleportManager.HasSavedOrigin)
                {
                    BossDebugTeleportManager.ReturnToSavedOrigin();
                }
            }
            else if (selectedNavIndex >= 1 && selectedNavIndex <= totalItems)
            {
                BossDebugEntry boss = BossDebugTeleportManager.Bosses[selectedNavIndex - 1];
                BossDebugTeleportManager.TeleportToBoss(boss);
            }
        }
    }

    private void EnsureScrollVisible(int index)
    {
        if (index <= 0)
        {
            scrollPosition.y = 0f;
            return;
        }

        float itemHeight = 36f;
        float viewHeight = 360f;
        float targetY = (index - 1) * itemHeight;

        if (targetY < scrollPosition.y)
        {
            scrollPosition.y = targetY;
        }
        else if (targetY + itemHeight > scrollPosition.y + viewHeight)
        {
            scrollPosition.y = targetY + itemHeight - viewHeight;
        }
    }

    private static bool CheckGamepadUp()
    {
        if (!Rewired.ReInput.isReady || Rewired.ReInput.controllers == null) return false;
        var joysticks = Rewired.ReInput.controllers.Joysticks;
        if (joysticks == null) return false;
        for (int i = 0; i < joysticks.Count; i++)
        {
            if (Player2Pad.GetUp(joysticks[i])) return true;
        }
        return false;
    }

    private static bool CheckGamepadDown()
    {
        if (!Rewired.ReInput.isReady || Rewired.ReInput.controllers == null) return false;
        var joysticks = Rewired.ReInput.controllers.Joysticks;
        if (joysticks == null) return false;
        for (int i = 0; i < joysticks.Count; i++)
        {
            if (Player2Pad.GetDown(joysticks[i])) return true;
        }
        return false;
    }

    private static bool CheckGamepadConfirm()
    {
        if (!Rewired.ReInput.isReady || Rewired.ReInput.controllers == null) return false;
        var joysticks = Rewired.ReInput.controllers.Joysticks;
        if (joysticks == null) return false;
        for (int i = 0; i < joysticks.Count; i++)
        {
            if (Player2Pad.GetSelectDown(joysticks[i]) || Player2Pad.GetAttackDown(joysticks[i])) return true;
        }
        return false;
    }

    private static bool CheckGamepadCancel()
    {
        if (!Rewired.ReInput.isReady || Rewired.ReInput.controllers == null) return false;
        var joysticks = Rewired.ReInput.controllers.Joysticks;
        if (joysticks == null) return false;
        for (int i = 0; i < joysticks.Count; i++)
        {
            if (Player2Pad.GetCancelDown(joysticks[i])) return true;
        }
        return false;
    }

    private void OnGUI()
    {
        EnsureStyles();

        // 1. Si el menú F2 está abierto, dibujar la ventana principal interactiva
        if (BossDebugTeleportManager.IsMenuOpen)
        {
            DrawMenuWindow();
            return;
        }

        // 2. Si estamos en una pelea de práctica de jefe, mostrar el banner superior discreto
        if (BossDebugTeleportManager.IsDebugBossFightActive)
        {
            DrawDiscreetPracticeBanner();
        }
    }

    private void DrawMenuWindow()
    {
        // Fondo semi-transparente que bloquea clics al juego
        GUI.Box(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none, styleOverlay);

        float winWidth = Mathf.Min(740f, Screen.width - 40f);
        float winHeight = Mathf.Min(580f, Screen.height - 40f);
        float winX = (Screen.width - winWidth) * 0.5f;
        float winY = (Screen.height - winHeight) * 0.5f;

        Rect windowRect = new Rect(winX, winY, winWidth, winHeight);
        GUI.Box(windowRect, GUIContent.none, styleWindow);

        // Cabecera / Título
        GUI.Label(new Rect(winX + 20f, winY + 12f, winWidth - 40f, 24f), "⚔ SELECTOR DE JEFES — MODO PRÁCTICA ⚔", styleTitle);
        GUI.Label(new Rect(winX + 20f, winY + 36f, winWidth - 40f, 18f), "Entrena sin otorgar lágrimas | Arriba/Abajo/W/S/Stick para navegar | Enter/K/A para confirmar", styleSubtitle);

        // Botón Prominente de Regreso
        Rect returnRect = new Rect(winX + 24f, winY + 62f, winWidth - 48f, 38f);
        bool hasSaved = BossDebugTeleportManager.HasSavedOrigin;
        string returnText = hasSaved
            ? $"⮌ REGRESAR A LA UBICACIÓN ANTERIOR ({BossDebugTeleportManager.SavedLevelName})"
            : "⮌ REGRESAR A LA UBICACIÓN ANTERIOR (Sin punto de retorno)";

        if (selectedNavIndex == 0 && hasSaved)
        {
            returnText = $"►  {returnText}  ◄";
        }

        GUIStyle retStyle = hasSaved ? styleReturnBtn : styleReturnBtnDisabled;
        if (GUI.Button(returnRect, returnText, retStyle) && hasSaved)
        {
            BossDebugTeleportManager.ReturnToSavedOrigin();
            return;
        }

        // Subtítulo de Lista
        GUI.Label(new Rect(winX + 24f, winY + 108f, winWidth - 48f, 20f), "<b><color=#D4AF37>LISTA DE ENFRENTAMIENTOS DISPONIBLES:</color></b>", styleSubtitle);

        // Lista de jefes con ScrollView
        float scrollY = winY + 130f;
        float scrollHeight = winHeight - 165f;
        float scrollWidth = winWidth - 48f;
        Rect scrollAreaRect = new Rect(winX + 24f, scrollY, scrollWidth, scrollHeight);

        var bosses = BossDebugTeleportManager.Bosses;
        float contentHeight = bosses.Length * 36f;
        Rect viewRect = new Rect(0f, 0f, scrollWidth - 20f, contentHeight);

        scrollPosition = GUI.BeginScrollView(scrollAreaRect, scrollPosition, viewRect);

        Vector2 mousePos = Event.current.mousePosition;

        for (int i = 0; i < bosses.Length; i++)
        {
            BossDebugEntry boss = bosses[i];
            Rect rowRect = new Rect(0f, i * 36f, scrollWidth - 20f, 32f);

            bool isSelected = (selectedNavIndex == (i + 1));
            GUIStyle rowStyle = isSelected ? styleRowSelected : styleRowNormal;

            string labelText = isSelected
                ? $"►  [{i + 1:D2}]  {boss.Name}  <color=#CCCCCC>({boss.SceneName})</color>  ◄"
                : $"    [{i + 1:D2}]  {boss.Name}  <color=#888888>({boss.SceneName})</color>";

            if (GUI.Button(rowRect, labelText, rowStyle))
            {
                selectedNavIndex = i + 1;
                BossDebugTeleportManager.TeleportToBoss(boss);
                break;
            }

            // Detección de puntero de ratón sobre la fila
            if (Event.current.type == EventType.Repaint && rowRect.Contains(mousePos))
            {
                if (selectedNavIndex != (i + 1))
                {
                    selectedNavIndex = i + 1;
                }
            }
        }

        GUI.EndScrollView();

        // Pie informativo
        GUI.Label(new Rect(winX + 20f, winY + winHeight - 26f, winWidth - 40f, 20f),
            "<color=#888888>[F2 / Esc]: Cerrar menú   |   [R / Backspace]: Regresar directo a tu punto anterior</color>", styleSubtitle);
    }

    private void DrawDiscreetPracticeBanner()
    {
        float bannerWidth = 560f;
        float bannerHeight = 32f;
        float bannerX = (Screen.width - bannerWidth) * 0.5f;
        float bannerY = 8f;

        Rect bannerRect = new Rect(bannerX, bannerY, bannerWidth, bannerHeight);
        GUI.Box(bannerRect, GUIContent.none, styleWindow);

        // Texto informativo a la izquierda
        GUI.Label(new Rect(bannerX + 16f, bannerY + 6f, bannerWidth - 140f, 20f),
            "⚔ MODO PRÁCTICA JEFE (Sin Lágrimas) | [F2] Menú de Jefes", styleBannerText);

        // Botón de Regreso a la derecha
        Rect btnRect = new Rect(bannerX + bannerWidth - 116f, bannerY + 4f, 106f, 24f);
        if (GUI.Button(btnRect, "⮌ Regresar", styleBannerBtn))
        {
            BossDebugTeleportManager.ReturnToSavedOrigin();
        }
    }
}

// -------------------------------------------------------------------------------------------------
// PARCHES HARMONY: Desactivar recompensas de lágrimas / Purge mientras IsDebugBossFightActive == true
// -------------------------------------------------------------------------------------------------

/// <summary>
/// Evita la adición de lágrimas mediante el evento PlayMaker de final de jefe.
/// </summary>
[HarmonyPatch(typeof(Tools.PlayMaker.Action.TearsAddition), "OnEnter")]
internal static class TearsAddition_OnEnter_BossDebugPatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(Tools.PlayMaker.Action.TearsAddition __instance)
    {
        if (BossDebugTeleportManager.IsDebugBossFightActive && __instance.Tears != null && __instance.Tears.Value > 0f)
        {
            if (__instance.onSuccess != null && __instance.Fsm != null)
            {
                __instance.Fsm.Event(__instance.onSuccess);
            }
            __instance.Finish();
            return false;
        }
        return true;
    }
}

/// <summary>
/// Evita la adición de Purge mediante acciones PlayMaker2 en eventos de victoria.
/// </summary>
[HarmonyPatch(typeof(Tools.Playmaker2.Action.PurgeAdd), "OnEnter")]
internal static class PurgeAdd_OnEnter_BossDebugPatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(Tools.Playmaker2.Action.PurgeAdd __instance)
    {
        if (BossDebugTeleportManager.IsDebugBossFightActive && __instance.value != null && __instance.value.Value > 0f)
        {
            __instance.Finish();
            return false;
        }
        return true;
    }
}

/// <summary>
/// Evita la obtención directa de lágrimas al morir el jefe o esbirros en modo práctica.
/// </summary>
[HarmonyPatch(typeof(Penitent), "GetPurge")]
internal static class Penitent_GetPurge_BossDebugPatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix()
    {
        if (BossDebugTeleportManager.IsDebugBossFightActive)
        {
            return false;
        }
        return true;
    }
}
