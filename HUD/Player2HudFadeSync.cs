using Framework.Managers;
using Gameplay.UI;
using Gameplay.UI.Widgets;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.CoopLocal;

// Sincronización integral de visibilidad del HUD de P2 (salud, fervor, matraces y lágrimas):
// Se oculta automáticamente cuando el HUD de P1 está oculto por:
// - Fundidos de pantalla (FadeWidget)
// - Cinemáticas y Cutscenes (CinematicBars / MustShowGamePlayUI == false)
// - Diálogos con NPCs (DialogManager.InDialog)
// - Menús de jugador (Inventario, Árbol de habilidades, Pausa, Mapa)
// - Escenas no jugables (Menú principal, escenas de atracción)
// - Ocultamiento parcial o total de la interfaz de gameplay (GameplayWidget.canvas.alpha <= 0.05 / HideLeftPart)
internal static class Player2HudFadeSync
{
    private static bool subscribed;
    private static bool isP2HudCurrentlyVisible = true;

    private static readonly System.Reflection.FieldInfo CanvasGroupField =
        AccessTools.Field(typeof(GameplayWidget), "canvas");
    private static readonly System.Reflection.FieldInfo LeftPartRootField =
        AccessTools.Field(typeof(GameplayWidget), "leftPartRoot");
    private static readonly System.Reflection.FieldInfo RightPartRootField =
        AccessTools.Field(typeof(GameplayWidget), "rightPartRoot");

    internal static void Initialize()
    {
        if (subscribed) return;
        FadeWidget.OnFadeShowStart += OnFadeShowStart;
        FadeWidget.OnFadeHidedEnd += OnFadeHidedEnd;
        subscribed = true;
    }

    internal static void Dispose()
    {
        if (!subscribed) return;
        FadeWidget.OnFadeShowStart -= OnFadeShowStart;
        FadeWidget.OnFadeHidedEnd -= OnFadeHidedEnd;
        subscribed = false;
        isP2HudCurrentlyVisible = true;
    }

    private static void OnFadeShowStart()
    {
        HideAllInstant();
    }

    private static void OnFadeHidedEnd()
    {
        SyncVisibility(instant: false);
    }

    private static bool IsScreenFading()
    {
        return FadeWidget.instance != null && FadeWidget.instance.IsActive;
    }

    internal static bool IsMenuBlockingHud()
    {
        return UIController.instance != null && (UIController.instance.IsShowingMenu || UIController.instance.IsShowingInventory);
    }

    /// <summary>
    /// Comprueba si el HUD de P1 (vida y fervor) debe estar y está efectivamente visible.
    /// Si P1 está en cinemática, diálogo, menú o con el HUD oculto, devuelve false.
    /// </summary>
    internal static bool ShouldP2HudBeVisible()
    {
        // 1. Fundido activo a negro o cargando
        if (IsScreenFading()) return false;

        // 2. Menú de inventario / pausa abierto
        if (IsMenuBlockingHud()) return false;

        // 3. Escenas no jugables (menú inicial, etc.)
        if (Core.Logic == null || Core.Logic.IsMenuScene() || Core.Logic.IsAttrackScene()) return false;

        // 4. Cinemáticas / cutscenes / eventos según el gestor oficial de UI
        if (Core.UI == null || !Core.UI.MustShowGamePlayUI()) return false;

        // 5. Diálogo activo con NPCs
        if (Core.Dialog != null && Core.Dialog.InDialog) return false;

        // 6. Modo cinemático de bandas negras
        if (Core.UI.Cinematic != null && Core.UI.Cinematic.InCinematicMode) return false;

        // 7. Estado del widget principal de Gameplay de P1
        GameplayWidget gw = Core.UI.GameplayUI;
        if (gw != null)
        {
            if (CanvasGroupField != null)
            {
                var cg = CanvasGroupField.GetValue(gw) as CanvasGroup;
                if (cg != null && cg.alpha <= 0.05f) return false;
            }
            if (!gw.gameObject.activeInHierarchy) return false;

            // Comprobar si la parte izquierda de P1 (vida y fervor) fue ocultada explícitamente
            if (LeftPartRootField != null)
            {
                var leftRoot = LeftPartRootField.GetValue(gw) as GameObject;
                if (leftRoot != null && !leftRoot.activeInHierarchy) return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Comprueba si la parte de lágrimas/monedas de P1 está visible en pantalla.
    /// </summary>
    internal static bool ShouldP2PurgeBeVisible()
    {
        if (!ShouldP2HudBeVisible()) return false;
        GameplayWidget gw = Core.UI?.GameplayUI;
        if (gw != null && RightPartRootField != null)
        {
            var rightRoot = RightPartRootField.GetValue(gw) as GameObject;
            if (rightRoot != null && !rightRoot.activeInHierarchy) return false;
        }
        return true;
    }

    internal static void HideAllInstant()
    {
        isP2HudCurrentlyVisible = false;
        Player2HealthBar.SetVisible(false, instant: true);
        Player2FervourBar.SetVisible(false, instant: true);
        Player2PurgePoints.SetVisible(false, instant: true);
        Player2ModeIndicator.SetMenuOverlayHidden(true);
    }

    internal static void ShowAllInstant()
    {
        if (IsScreenFading()) return;
        isP2HudCurrentlyVisible = true;
        Player2HealthBar.SetVisible(true, instant: true);
        Player2FervourBar.SetVisible(true, instant: true);
        Player2PurgePoints.SetVisible(ShouldP2PurgeBeVisible(), instant: true);
        Player2ModeIndicator.SetMenuOverlayHidden(false);
    }

    internal static void SyncVisibility(bool instant = false)
    {
        bool shouldBeVisible = ShouldP2HudBeVisible();

        if (shouldBeVisible != isP2HudCurrentlyVisible)
        {
            isP2HudCurrentlyVisible = shouldBeVisible;
            if (shouldBeVisible)
            {
                Player2HealthBar.SetVisible(true, instant);
                Player2FervourBar.SetVisible(true, instant);
                Player2PurgePoints.SetVisible(ShouldP2PurgeBeVisible(), instant);
                Player2ModeIndicator.SetMenuOverlayHidden(false);
            }
            else
            {
                Player2HealthBar.SetVisible(false, instant);
                Player2FervourBar.SetVisible(false, instant);
                Player2PurgePoints.SetVisible(false, instant);
                Player2ModeIndicator.SetMenuOverlayHidden(true);
            }
        }
        else if (shouldBeVisible)
        {
            // Sincronización fina de Purge (monedas) según el estado de rightPartRoot
            bool purgeVisible = ShouldP2PurgeBeVisible();
            Player2PurgePoints.SetVisible(purgeVisible, instant);
        }
    }

    internal static void RefreshMenuOcclusion()
    {
        SyncVisibility(instant: true);
    }

    internal static void ApplyCurrentFadeState()
    {
        SyncVisibility(instant: true);
    }
}

[HarmonyPatch(typeof(UIController), "Update")]
internal static class UIController_HudMenuOcclusion_Patch
{
    private static void Postfix()
    {
        try
        {
            Player2HudFadeSync.RefreshMenuOcclusion();
        }
        catch (System.Exception ex)
        {
            if (Main.CoopLocal != null) Blasphemous.ModdingAPI.ModLog.Info("[Player2HudFadeSync] Error in Update: " + ex.Message, Main.CoopLocal);
        }
    }
}
