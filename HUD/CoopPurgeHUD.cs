using System;
using Framework.FrameworkCore.Attributes;
using Framework.Managers;
using Gameplay.GameControllers.Penitent;
using Gameplay.UI.Others.UIGameLogic;
using HarmonyLib;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace Blasphemous.CoopLocal;

internal class PurgeSlotWidget
{
    public int SlotIndex;
    public GameObject Root;
    public RectTransform RootRect;
    public Image Background;
    public Image Icon;
    public Text Label;
    public float Scale = 1f;
    public Vector2 AnchoredPosition;

    public void SetVisible(bool visible, bool instant = false)
    {
        HudFade.SetVisible(Root, visible, instant);
    }
}

internal static class CoopPurgeHUD
{
    private static readonly FieldInfo GameplayWidgetPurgePointsField =
        AccessTools.Field(typeof(Gameplay.UI.Widgets.GameplayWidget), "purgePoints");
    private static readonly FieldInfo PurgePointsTextField =
        AccessTools.Field(typeof(PlayerPurgePoints), "text");
    private static readonly FieldInfo PurgePointsBackgroundField =
        AccessTools.Field(typeof(PlayerPurgePoints), "background");

    private static readonly PurgeSlotWidget[] slots = new PurgeSlotWidget[4];
    private static Sprite cachedBgSprite;
    private static Color cachedBgColor = Color.white;
    private static Image.Type cachedBgType = Image.Type.Simple;
    private static Sprite cachedIconSprite;
    private static Color cachedIconColor = Color.white;
    private static Font cachedFont;
    private static int cachedFontSize = 24;
    private static Material cachedFontMaterial;
    private static Color cachedFontColor = new Color(0.96f, 0.92f, 0.72f, 1f);

    static CoopPurgeHUD()
    {
        for (int i = 0; i < 4; i++)
        {
            slots[i] = new PurgeSlotWidget { SlotIndex = i };
        }
    }

    internal static PurgeSlotWidget GetSlot(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < slots.Length) return slots[slotIndex];
        return null;
    }

    internal static RectTransform GetSlotRect(int slotIndex)
    {
        return GetSlot(slotIndex)?.RootRect;
    }

    internal static void SetVisible(bool visible, bool instant = false)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i]?.SetVisible(visible, instant);
        }
    }

    internal static void CacheResources()
    {
        try
        {
            if (cachedIconSprite != null && cachedFont != null) return;

            Gameplay.UI.Widgets.GameplayWidget gameplayWidget = Core.UI != null ? Core.UI.GameplayUI : null;
            PlayerPurgePoints original = gameplayWidget != null
                ? (PlayerPurgePoints)GameplayWidgetPurgePointsField?.GetValue(gameplayWidget)
                : null;
            if (original == null)
            {
                original = UnityEngine.Object.FindObjectOfType<PlayerPurgePoints>();
            }

            if (original != null)
            {
                Image origBg = null;
                try
                {
                    if (PurgePointsBackgroundField != null)
                    {
                        origBg = (Image)PurgePointsBackgroundField.GetValue(original);
                    }
                }
                catch (Exception ex)
                {
                    if (Main.CoopLocal != null)
                    {
                        Blasphemous.ModdingAPI.ModLog.Warn($"[CoopPurgeHUD] Background reflection exception: {ex.Message}", Main.CoopLocal);
                    }
                }

                if (origBg != null && origBg.sprite != null)
                {
                    cachedBgSprite = origBg.sprite;
                    cachedBgColor = origBg.color;
                    cachedBgType = origBg.type;
                    if (cachedIconSprite == null)
                    {
                        cachedIconSprite = origBg.sprite;
                        cachedIconColor = origBg.color;
                    }
                }

                Text originalText = (Text)PurgePointsTextField?.GetValue(original);
                if (originalText != null && originalText.font != null)
                {
                    cachedFont = originalText.font;
                    cachedFontSize = originalText.fontSize > 0 ? originalText.fontSize : 24;
                    cachedFontMaterial = originalText.font.material;
                    cachedFontColor = originalText.color;

                    Transform textParent = originalText.transform.parent;
                    if (textParent != null)
                    {
                        for (int i = 0; i < textParent.childCount; i++)
                        {
                            Transform child = textParent.GetChild(i);
                            Image childImage = child.GetComponent<Image>();
                            if (childImage != null && childImage.sprite != null && child.gameObject != originalText.gameObject)
                            {
                                cachedIconSprite = childImage.sprite;
                                cachedIconColor = childImage.color;
                                break;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Warn($"[CoopPurgeHUD] Error in CacheResources: {ex.Message}", Main.CoopLocal);
            }
        }
    }

    internal static void DestroySlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= slots.Length) return;
        PurgeSlotWidget slot = slots[slotIndex];
        if (slot?.Root != null)
        {
            UnityEngine.Object.Destroy(slot.Root);
            slot.Root = null;
            slot.RootRect = null;
            slot.Background = null;
            slot.Icon = null;
            slot.Label = null;
        }
    }

    internal static void DestroyAll()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i]?.Root != null)
            {
                UnityEngine.Object.Destroy(slots[i].Root);
                slots[i].Root = null;
                slots[i].RootRect = null;
                slots[i].Background = null;
                slots[i].Icon = null;
                slots[i].Label = null;
            }
        }
    }

    internal static void EnsureCreatedAll()
    {
        try
        {
            if (CoopGameConfig.PlayerCount < 3)
            {
                DestroyAll();
                return;
            }

            CacheResources();

            PlayerHealth originalHealth = CoopHealthBars.OriginalHealth;
            Canvas canvas = null;
            if (originalHealth != null)
            {
                Canvas c = originalHealth.GetComponentInParent<Canvas>();
                while (c != null && c.transform.parent != null)
                {
                    Canvas pc = c.transform.parent.GetComponentInParent<Canvas>();
                    if (pc == null) break;
                    c = pc;
                }
                if (c != null) canvas = c;
            }
            if (canvas == null)
            {
                canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
            }

            if (canvas == null)
            {
                DashParryDebugLog.Log("CoopPurgeHUD.EnsureCreatedAll: Canvas no encontrado.");
                return;
            }

            int activeCount = Mathf.Clamp(CoopGameConfig.PlayerCount, 3, 4);
            var layoutCfg = HudLayoutConfig.Current;

            for (int i = 0; i < activeCount; i++)
            {
                EnsureSlotCreated(i, canvas.transform, layoutCfg);
            }

            if (activeCount < 4 && slots[3]?.Root != null)
            {
                UnityEngine.Object.Destroy(slots[3].Root);
                slots[3].Root = null;
                slots[3].RootRect = null;
                slots[3].Background = null;
                slots[3].Icon = null;
                slots[3].Label = null;
            }

            Tick();
        }
        catch (Exception ex)
        {
            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Error($"[CoopPurgeHUD] Error in EnsureCreatedAll: {ex}", Main.CoopLocal);
            }
        }
    }

    private static void EnsureSlotCreated(int slotIndex, Transform parent, CoopHudLayoutConfig layoutCfg)
    {
        PurgeSlotWidget slot = slots[slotIndex];
        if (slot.Root != null)
        {
            UnityEngine.Object.Destroy(slot.Root);
            slot.Root = null;
            slot.RootRect = null;
            slot.Background = null;
            slot.Icon = null;
            slot.Label = null;
        }

        GameObject root = new GameObject($"PlayerPurgeWidget_P{slotIndex + 1}");
        root.transform.SetParent(parent, worldPositionStays: false);
        slot.Root = root;

        RectTransform rootRect = root.AddComponent<RectTransform>();
        slot.RootRect = rootRect;

        bool isLeft = (slotIndex == 0 || slotIndex == 2); // P1 y P3: lado izquierdo
        HudElementPosition posCfg;
        if (slotIndex == 0) posCfg = layoutCfg.p1_coins;
        else if (slotIndex == 1) posCfg = layoutCfg.p2_coins;
        else if (slotIndex == 2) posCfg = layoutCfg.p3_coins;
        else posCfg = layoutCfg.p4_coins;

        slot.Scale = posCfg.scale;
        slot.AnchoredPosition = posCfg.Position;

        // Dimensiones base compactas del widget de monedas
        rootRect.sizeDelta = new Vector2(84f, 22f);
        rootRect.localScale = Vector3.one * slot.Scale;

        if (isLeft)
        {
            // P1 y P3: Anclado arriba a la izquierda. Monedas a la DERECHA del HUD.
            rootRect.anchorMin = new Vector2(0f, 1f);
            rootRect.anchorMax = new Vector2(0f, 1f);
            rootRect.pivot = new Vector2(0f, 1f);
        }
        else
        {
            // P2 y P4: Anclado arriba a la derecha. Monedas a la IZQUIERDA del HUD.
            rootRect.anchorMin = new Vector2(1f, 1f);
            rootRect.anchorMax = new Vector2(1f, 1f);
            rootRect.pivot = new Vector2(1f, 1f);
        }
        rootRect.anchoredPosition = slot.AnchoredPosition;

        // Fondo del indicador de monedas: sombra negra sutil y muy compacta
        GameObject bgObj = new GameObject("Background");
        bgObj.transform.SetParent(root.transform, worldPositionStays: false);
        RectTransform bgRect = bgObj.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.pivot = new Vector2(0.5f, 0.5f);
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        Image bgImg = bgObj.AddComponent<Image>();
        slot.Background = bgImg;
        bgImg.sprite = null;
        bgImg.color = new Color(0.02f, 0.02f, 0.04f, 0.70f);

        Outline bgOutline = bgObj.AddComponent<Outline>();
        bgOutline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        bgOutline.effectDistance = new Vector2(1f, -1f);

        if (isLeft)
        {
            // P1 y P3: Ícono a la izquierda, número a la derecha
            GameObject iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(root.transform, worldPositionStays: false);
            RectTransform iconRect = iconObj.AddComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0f, 0.5f);
            iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.anchoredPosition = new Vector2(11f, 0f);

            Image iconImg = iconObj.AddComponent<Image>();
            slot.Icon = iconImg;
            if (cachedIconSprite != null)
            {
                iconImg.sprite = cachedIconSprite;
                iconImg.color = cachedIconColor;
                iconRect.sizeDelta = new Vector2(16f, 16f);
            }
            else
            {
                iconRect.sizeDelta = new Vector2(16f, 16f);
                iconImg.color = new Color(0.9f, 0.8f, 0.3f, 1f);
            }

            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(root.transform, worldPositionStays: false);
            RectTransform textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0f, 0f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.pivot = new Vector2(0f, 0.5f);
            textRect.offsetMin = new Vector2(23f, 0f);
            textRect.offsetMax = new Vector2(-4f, 0f);

            Text textComp = textObj.AddComponent<Text>();
            slot.Label = textComp;
            ApplyTextStyle(textComp, TextAnchor.MiddleLeft);
        }
        else
        {
            // P2 y P4: Número a la izquierda, ícono a la derecha
            GameObject iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(root.transform, worldPositionStays: false);
            RectTransform iconRect = iconObj.AddComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(1f, 0.5f);
            iconRect.anchorMax = new Vector2(1f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.anchoredPosition = new Vector2(-11f, 0f);

            Image iconImg = iconObj.AddComponent<Image>();
            slot.Icon = iconImg;
            if (cachedIconSprite != null)
            {
                iconImg.sprite = cachedIconSprite;
                iconImg.color = cachedIconColor;
                iconRect.sizeDelta = new Vector2(16f, 16f);
            }
            else
            {
                iconRect.sizeDelta = new Vector2(16f, 16f);
                iconImg.color = new Color(0.9f, 0.8f, 0.3f, 1f);
            }

            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(root.transform, worldPositionStays: false);
            RectTransform textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0f, 0f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.pivot = new Vector2(1f, 0.5f);
            textRect.offsetMin = new Vector2(4f, 0f);
            textRect.offsetMax = new Vector2(-23f, 0f);

            Text textComp = textObj.AddComponent<Text>();
            slot.Label = textComp;
            ApplyTextStyle(textComp, TextAnchor.MiddleRight);
        }

        CanvasGroup cg = HudFade.EnsureCanvasGroup(root);
        cg.alpha = 1f;
        root.SetActive(true);
    }

    private static void ApplyTextStyle(Text label, TextAnchor alignment)
    {
        // Fuente limpia de alta legibilidad a tamaños reducidos
        Font cleanFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (cleanFont == null)
        {
            cleanFont = Font.CreateDynamicFontFromOSFont("Arial", 16);
        }

        label.font = cleanFont != null ? cleanFont : cachedFont;
        label.material = null; // Sin shader de pixelado para máxima nitidez
        label.fontSize = 15;
        label.fontStyle = FontStyle.Bold;
        label.color = new Color(0.98f, 0.94f, 0.76f, 1f); // Tono oro pergamino nítido
        label.alignment = alignment;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;

        Outline outline = label.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.95f);
        outline.effectDistance = new Vector2(1f, -1f);

        Shadow shadow = label.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.65f);
        shadow.effectDistance = new Vector2(1f, -1f);

        label.text = "0";
    }

    internal static void Tick()
    {
        if (CoopGameConfig.PlayerCount < 3)
        {
            return;
        }

        int activeCount = Mathf.Clamp(CoopGameConfig.PlayerCount, 3, 4);
        for (int i = 0; i < activeCount; i++)
        {
            PurgeSlotWidget slot = slots[i];
            if (slot == null || slot.Label == null) continue;

            Penitent p = CoopPlayerRegistry.GetPenitent(i);
            if (p != null && p.Stats != null && p.Stats.Purge != null)
            {
                float cur = Mathf.Clamp(p.Stats.Purge.Current, 0f, 9999999f);
                slot.Label.text = Mathf.FloorToInt(cur).ToString();
            }
        }
    }
}
