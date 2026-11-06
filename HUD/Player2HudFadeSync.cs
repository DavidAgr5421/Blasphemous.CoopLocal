using Gameplay.UI;
using Gameplay.UI.Widgets;
using HarmonyLib;

namespace Blasphemous.CoopLocal;

// Round 56 / Ronda 88: Sincronización de visibilidad del HUD de P2 tanto durante fundidos a negro
// (transiciones de sala, FadeWidget) como cuando se abre cualquier menú de jugador (Inventario,
// Árbol de Habilidades, Pausa/Mapa). Evita que la barra de vida, fervor, matraces y lágrimas
// de P2 se superpongan encima de la interfaz del menú.
internal static class Player2HudFadeSync
{
    private static bool subscribed;
    private static bool hiddenByMenu;

    internal static void Initialize()
    {
        if (subscribed)
        {
            return;
        }
        FadeWidget.OnFadeShowStart += HideAll;
        FadeWidget.OnFadeHidedEnd += ShowAll;
        subscribed = true;
    }

    internal static void Dispose()
    {
        if (!subscribed)
        {
            return;
        }
        FadeWidget.OnFadeShowStart -= HideAll;
        FadeWidget.OnFadeHidedEnd -= ShowAll;
        subscribed = false;
        hiddenByMenu = false;
    }

    private static bool IsScreenFading()
    {
        return FadeWidget.instance != null && FadeWidget.instance.IsActive;
    }

    internal static bool IsMenuBlockingHud()
    {
        if (UIController.instance == null)
        {
            return false;
        }
        return UIController.instance.IsShowingMenu || UIController.instance.IsShowingInventory;
    }

    private static void HideAll()
    {
        DashParryDebugLog.Log("Player2HudFadeSync: FadeWidget.OnFadeShowStart -> hiding P2 HUD");
        Player2HealthBar.SetVisible(false);
        Player2FervourBar.SetVisible(false);
        Player2PurgePoints.SetVisible(false);
    }

    private static void ShowAll()
    {
        if (IsMenuBlockingHud())
        {
            HideAllInstant();
            return;
        }
        DashParryDebugLog.Log("Player2HudFadeSync: FadeWidget.OnFadeHidedEnd -> showing P2 HUD");
        Player2HealthBar.SetVisible(true);
        Player2FervourBar.SetVisible(true);
        Player2PurgePoints.SetVisible(true);
    }

    internal static void HideAllInstant()
    {
        Player2HealthBar.SetVisible(false, instant: true);
        Player2FervourBar.SetVisible(false, instant: true);
        Player2PurgePoints.SetVisible(false, instant: true);
    }

    internal static void ShowAllInstant()
    {
        if (IsScreenFading())
        {
            return;
        }
        Player2HealthBar.SetVisible(true, instant: true);
        Player2FervourBar.SetVisible(true, instant: true);
        Player2PurgePoints.SetVisible(true, instant: true);
    }

    internal static void RefreshMenuOcclusion()
    {
        bool menuOpen = IsMenuBlockingHud();
        if (menuOpen && !hiddenByMenu)
        {
            hiddenByMenu = true;
            HideAllInstant();
            Player2ModeIndicator.SetMenuOverlayHidden(true);
        }
        else if (!menuOpen && hiddenByMenu)
        {
            hiddenByMenu = false;
            Player2ModeIndicator.SetMenuOverlayHidden(false);
            if (!IsScreenFading())
            {
                ShowAllInstant();
            }
        }
    }

    internal static void ApplyCurrentFadeState()
    {
        if (IsScreenFading() || IsMenuBlockingHud())
        {
            hiddenByMenu = IsMenuBlockingHud();
            HideAllInstant();
            return;
        }
        ShowAll();
    }
}

[HarmonyPatch(typeof(UIController), "Update")]
internal static class UIController_HudMenuOcclusion_Patch
{
    private static void Postfix()
    {
        Player2HudFadeSync.RefreshMenuOcclusion();
    }
}
