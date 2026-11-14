using Gameplay.UI;
using Gameplay.UI.Others.MenuLogic;
using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace Blasphemous.CoopLocal;

// Ronda 88: Indicador visual arriba a la derecha con la fuente del juego ("[nombre jugador] menu",
// ej. "Baby menu" / "Pan-chan menu") para identificar de qué jugador es el menú abierto.
internal static class CoopMenuOwnerBanner
{
    private const string BannerObjectName = "CoopLocalMenuOwnerBanner";
    private const string GameTmpFontName = "MajesticExtended_FullLatin";

    private static readonly FieldInfo TabLabelField =
        AccessTools.Field(typeof(NewInventoryWidget), "tabLabel");
    private static readonly FieldInfo NewInventoryMenuField =
        AccessTools.Field(typeof(UIController), "newInventoryMenu");

    private static GameObject inventoryBannerObj;
    private static Text inventoryBannerText;

    private static GameObject overlayCanvasObj;
    private static TMPro.TextMeshProUGUI overlayBannerTmp;

    internal static void Refresh(NewInventoryWidget widget = null)
    {
        if (widget == null && UIController.instance != null)
        {
            widget = NewInventoryMenuField?.GetValue(UIController.instance) as NewInventoryWidget;
        }

        bool inventoryActive = widget != null && widget.currentlyActive;
        bool pauseActive = UIController.instance != null && UIController.instance.IsShowingMenu && !inventoryActive;

        int playerIndex = Player2MenuView.ActivePlayerIndex;
        CoopPlayerSlot slot = CoopPlayerRegistry.GetSlot(playerIndex);
        string title = slot.MenuTitle; // ej. "Baby menu" o "Pan-chan menu"

        Color goldenColor = new Color(0.808f, 0.722f, 0.498f, 1f);
        if (widget != null && TabLabelField != null)
        {
            Text templateText = TabLabelField.GetValue(widget) as Text;
            if (templateText != null && templateText.color.a > 0.1f)
            {
                goldenColor = templateText.color;
            }
        }

        // 1. Etiqueta nativa con UnityEngine.UI.Text ("Majestic") dentro de NewInventoryWidget
        if (widget != null)
        {
            EnsureInventoryBannerCreated(widget);
            if (inventoryBannerObj != null && inventoryBannerText != null)
            {
                inventoryBannerObj.SetActive(inventoryActive);
                if (inventoryActive)
                {
                    inventoryBannerText.text = title;
                    inventoryBannerText.color = goldenColor;
                }
            }
        }

        // 2. Respaldo en Canvas superior para cuando se abre el menú de Pausa / Mapa (PauseWidget)
        EnsureOverlayBannerCreated();
        if (overlayCanvasObj != null && overlayBannerTmp != null)
        {
            bool showOverlay = pauseActive || (inventoryActive && inventoryBannerText == null);
            overlayCanvasObj.SetActive(showOverlay);
            if (showOverlay)
            {
                overlayBannerTmp.text = title;
                overlayBannerTmp.color = goldenColor;
            }
        }
    }

    internal static void HideAll()
    {
        if (inventoryBannerObj != null)
        {
            inventoryBannerObj.SetActive(false);
        }
        if (overlayCanvasObj != null)
        {
            overlayCanvasObj.SetActive(false);
        }
    }

    private static void EnsureInventoryBannerCreated(NewInventoryWidget widget)
    {
        if (inventoryBannerObj != null && inventoryBannerText != null)
        {
            return;
        }

        Text templateText = TabLabelField?.GetValue(widget) as Text;
        if (templateText == null)
        {
            return;
        }

        Transform existing = widget.transform.Find(BannerObjectName);
        if (existing != null)
        {
            inventoryBannerObj = existing.gameObject;
            inventoryBannerText = inventoryBannerObj.GetComponent<Text>();
            return;
        }

        inventoryBannerObj = new GameObject(BannerObjectName);
        inventoryBannerObj.transform.SetParent(widget.transform, worldPositionStays: false);
        inventoryBannerObj.transform.SetAsLastSibling();

        RectTransform rect = inventoryBannerObj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(24f, -14f);
        rect.sizeDelta = new Vector2(260f, 36f);
        rect.localScale = Vector3.one;

        inventoryBannerText = inventoryBannerObj.AddComponent<Text>();
        inventoryBannerText.font = templateText.font;
        inventoryBannerText.fontSize = templateText.fontSize > 0 ? templateText.fontSize : 16;
        inventoryBannerText.fontStyle = templateText.fontStyle;
        inventoryBannerText.material = templateText.material;
        inventoryBannerText.alignment = TextAnchor.UpperLeft;
        inventoryBannerText.horizontalOverflow = HorizontalWrapMode.Overflow;
        inventoryBannerText.verticalOverflow = VerticalWrapMode.Overflow;
        inventoryBannerText.raycastTarget = false;

        Outline outline = inventoryBannerObj.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        outline.effectDistance = new Vector2(1f, -1f);
    }

    private static void EnsureOverlayBannerCreated()
    {
        if (overlayCanvasObj != null && overlayBannerTmp != null)
        {
            return;
        }

        overlayCanvasObj = new GameObject("CoopLocalMenuOwnerOverlayCanvas");
        UnityEngine.Object.DontDestroyOnLoad(overlayCanvasObj);
        Canvas canvas = overlayCanvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue - 1;

        GameObject textObj = new GameObject("MenuOwnerText");
        textObj.transform.SetParent(overlayCanvasObj.transform, worldPositionStays: false);
        RectTransform rect = textObj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(24f, -18f);
        rect.sizeDelta = new Vector2(360f, 44f);

        overlayBannerTmp = textObj.AddComponent<TMPro.TextMeshProUGUI>();
        TMPro.TMP_FontAsset gameFont = Array.Find(
            Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>(),
            f => f != null && f.name == GameTmpFontName);
        if (gameFont != null)
        {
            overlayBannerTmp.font = gameFont;
        }
        overlayBannerTmp.fontSize = 24f;
        overlayBannerTmp.alignment = TMPro.TextAlignmentOptions.TopLeft;
        overlayBannerTmp.raycastTarget = false;
        overlayCanvasObj.SetActive(false);
    }
}

// Patch central sobre NewInventoryWidget.Show(bool p_active) para fijar la vista
// P1/P2 ANTES de que Show() renderice el primer frame.
[HarmonyPatch(typeof(NewInventoryWidget), nameof(NewInventoryWidget.Show))]
internal static class NewInventoryWidget_Show_P2View_Patch
{
    private static void Prefix(bool p_active)
    {
        if (p_active)
        {
            int targetPlayer = Player2MenuView.ConsumePendingOpenPlayer();
            Player2MenuView.SetActivePlayer(targetPlayer > 0 ? targetPlayer : 0);
        }
        else
        {
            Player2MenuView.SetActivePlayer(0);
            Player2MenuView.LastMenuToggleFrame = Time.frameCount;
        }
        Player2Input.RefreshDeviceAssignment();
    }

    private static void Postfix(NewInventoryWidget __instance)
    {
        if (!__instance.currentlyActive)
        {
            Player2MenuView.SetActivePlayer(0);
            Player2Input.RefreshDeviceAssignment();
            CoopMenuOwnerBanner.HideAll();
        }
        else
        {
            CoopMenuOwnerBanner.Refresh(__instance);
            MenuPurgeDisplaySync.RefreshAllMenuPurgeCounters(__instance);
        }
        Player2HudFadeSync.RefreshMenuOcclusion();
    }
}

// Ronda 88: Detectar si P2 es quien activa el santuario Mea Culpa (PlayMaker UnlockSkillsMenu)
[HarmonyPatch(typeof(Tools.Playmaker2.Action.UnlockSkillsMenu), "OnEnter")]
internal static class UnlockSkillsMenu_OnEnter_P2_Patch
{
    private static void Prefix(Tools.Playmaker2.Action.UnlockSkillsMenu __instance)
    {
        Gameplay.GameControllers.Penitent.Penitent toucher = UpgradeTouchResolver.Resolve(__instance.Fsm);
        int touchedSlot = CoopPlayerRegistry.GetPlayerIndex(toucher);
        if (touchedSlot > 0)
        {
            Player2MenuView.RequestOpenAsPlayer(touchedSlot);
        }
    }
}

// Ronda 88: Patch gemelo sobre NewInventoryWidget.ShowSkills(bool p_active) (santuarios Mea Culpa).
// Permite que cuando P2 abre o interactúa con el menú de mejora de habilidades se muestre su propio árbol
// y se actualice el cartel "[nombre jugador] menu" y el ocultamiento del HUD.
[HarmonyPatch(typeof(NewInventoryWidget), nameof(NewInventoryWidget.ShowSkills))]
internal static class NewInventoryWidget_ShowSkills_P2View_Patch
{
    private static void Prefix(bool p_active)
    {
        if (p_active)
        {
            int targetPlayer = Player2MenuView.ConsumePendingOpenPlayer();
            Player2MenuView.SetActivePlayer(targetPlayer > 0 ? targetPlayer : 0);
        }
        else
        {
            Player2MenuView.SetActivePlayer(0);
            Player2MenuView.LastMenuToggleFrame = Time.frameCount;
        }
        Player2Input.RefreshDeviceAssignment();
    }

    private static void Postfix(NewInventoryWidget __instance)
    {
        if (!__instance.currentlyActive)
        {
            Player2MenuView.SetActivePlayer(0);
            Player2Input.RefreshDeviceAssignment();
            CoopMenuOwnerBanner.HideAll();
        }
        else
        {
            CoopMenuOwnerBanner.Refresh(__instance);
            MenuPurgeDisplaySync.RefreshAllMenuPurgeCounters(__instance);
        }
        Player2HudFadeSync.RefreshMenuOcclusion();
    }
}

// Navegación de P2 y sincronización de banner mientras el menú está activo.
[HarmonyPatch(typeof(UIController), "Update")]
internal static class UIController_MenuNavigation_P2_Patch
{
    private static readonly FieldInfo NewInventoryMenuField =
        AccessTools.Field(typeof(UIController), "newInventoryMenu");
    private static readonly FieldInfo CurrentTabTypeField =
        AccessTools.Field(typeof(NewInventoryWidget), "currentTabType");
    private static readonly MethodInfo SelectTabMethod =
        AccessTools.Method(typeof(NewInventoryWidget), "SelectTab",
            new[] { typeof(NewInventoryWidget.TabType), typeof(bool) });

    private static void Postfix(UIController __instance)
    {
        var widget = NewInventoryMenuField?.GetValue(__instance) as NewInventoryWidget;
        bool inventoryOpen = widget != null && widget.currentlyActive;
        bool pauseOpen = __instance.IsShowingMenu && !inventoryOpen;

        if (!inventoryOpen && !pauseOpen)
        {
            CoopMenuOwnerBanner.HideAll();
            return;
        }

        CoopMenuOwnerBanner.Refresh(widget);

        if (!inventoryOpen)
        {
            return;
        }

        MenuPurgeDisplaySync.RefreshAllMenuPurgeCounters(widget);

        // Si un guest tiene el menú abierto y el jugador con teclado presiona LeftShift (su tecla de Dash/Cancelar rápida),
        // permitir volver atrás sin duplicar Escape (que ya maneja Rewired Player 0 en UIController.Update).
        if (Player2MenuView.IsInventoryP2View)
        {
            int activeIdx = Player2MenuView.ActivePlayerIndex;
            bool activeUsesKeyboard = (activeIdx == Player2Input.ActiveKeyboardPlayerIndex);

            if (activeUsesKeyboard &&
                Input.GetKeyDown(KeyCode.LeftShift) &&
                !Input.GetKeyDown(KeyCode.Escape))
            {
                Player2MenuView.LastMenuToggleFrame = Time.frameCount;
                widget.GoBack();
                return;
            }

            // Permitir que P1 recupere la vista del menú si presiona su propio botón de inventario mientras está abierto en un guest
            bool p1SwitchPressed = (Player2Input.ActiveKeyboardPlayerIndex != 0)
                ? (Player2Pad.P1Pad != null ? (Player2Pad.GetMenuDown(Player2Pad.P1Pad, 0) || Player2Pad.GetStartDown(Player2Pad.P1Pad)) : false)
                : (Input.GetKeyDown(KeyCode.I) || Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.Escape));

            if (p1SwitchPressed && Time.frameCount != Player2MenuView.LastMenuToggleFrame)
            {
                Player2MenuView.SetActivePlayer(0);
                Player2Input.RefreshDeviceAssignment();
                object currentTab = CurrentTabTypeField?.GetValue(widget);
                if (currentTab != null)
                {
                    SelectTabMethod?.Invoke(widget, new object[] { currentTab, false });
                }
                CoopMenuOwnerBanner.Refresh(widget);
                Player2MenuView.LastMenuToggleFrame = Time.frameCount;
            }
        }
    }
}
