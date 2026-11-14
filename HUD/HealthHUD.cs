using Com.LuisPedroFonseca.ProCamera2D;
using CreativeSpore.SmartColliders;
using Framework.FrameworkCore;
using Framework.Managers;
using System;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using Gameplay.UI.Others.UIGameLogic;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace Blasphemous.CoopLocal;

internal class HealthBarSlotInfo
{
    public int SlotIndex;
    public PlayerHealth Instance;
    public GameObject InstanceRoot;
    public float Scale = 0.65f;
    public Vector2 AnchoredPosition;

    public RectTransform CloneRect => InstanceRoot != null ? InstanceRoot.GetComponent<RectTransform>() : null;

    public void BringToFront()
    {
        if (InstanceRoot != null)
        {
            InstanceRoot.transform.SetAsLastSibling();
        }
    }

    public void SetVisible(bool visible, bool instant = false)
    {
        HudFade.SetVisible(InstanceRoot, visible, instant);
    }
}

internal static class CoopHealthBars
{
    private static readonly MethodInfo OnPenitentReadyMethod = AccessTools.Method(typeof(PlayerHealth), "OnPenitentReady");
    private static readonly FieldInfo LastBarWidthField = AccessTools.Field(typeof(PlayerHealth), "lastBarWidth");
    private static readonly FieldInfo BackgroundMidField = AccessTools.Field(typeof(PlayerHealth), "backgroundMid");
    private static readonly MethodInfo CalculateHealthBarSizeMethod = AccessTools.Method(typeof(PlayerHealth), "CalculateHealthBarSize");
    private static readonly MethodInfo CalculateHealthBarMethod = AccessTools.Method(typeof(PlayerHealth), "CalculateHealthBar");
    private static readonly MethodInfo CalculateLossBarMethod = AccessTools.Method(typeof(PlayerHealth), "CalculateLossBar");
    private static readonly HealthBarSlotInfo[] slots = new HealthBarSlotInfo[4];
    private static PlayerHealth originalCache;
    private static Vector2 p1InitialPos;
    private static bool p1InitialPosRecorded;

    static CoopHealthBars()
    {
        for (int i = 0; i < 4; i++)
        {
            slots[i] = new HealthBarSlotInfo { SlotIndex = i };
        }
    }

    internal static HealthBarSlotInfo GetSlot(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < slots.Length)
            return slots[slotIndex];
        return null;
    }

    internal static PlayerHealth OriginalHealth
    {
        get
        {
            if (originalCache == null)
            {
                originalCache = UnityEngine.Object.FindObjectOfType<PlayerHealth>();
                if (originalCache != null && !p1InitialPosRecorded)
                {
                    var rect = P1HealthRect;
                    if (rect != null)
                    {
                        p1InitialPos = rect.anchoredPosition;
                        p1InitialPosRecorded = true;
                    }
                }
            }
            return originalCache;
        }
    }

    internal static RectTransform P1HealthRect
    {
        get
        {
            var orig = OriginalHealth;
            if (orig == null) return null;
            Transform parent = orig.transform.parent; // Contenedor "Health Bar"
            return (parent != null ? parent : orig.transform) as RectTransform;
        }
    }

    internal static RectTransform GetDecorationRect(int slotIndex)
    {
        try
        {
            if (slotIndex == 0)
            {
                var orig = OriginalHealth;
                if (orig == null) return null;
                Transform parent = orig.transform.parent;
                if (parent != null)
                {
                    for (int i = 0; i < parent.childCount; i++)
                    {
                        Transform c = parent.GetChild(i);
                        if (c != orig.transform && c.GetComponent<RectTransform>() != null)
                        {
                            return c as RectTransform;
                        }
                    }
                }
                return (RectTransform)BackgroundMidField?.GetValue(orig);
            }
            else
            {
                var slot = GetSlot(slotIndex);
                if (slot == null || slot.InstanceRoot == null) return null;
                Transform root = slot.InstanceRoot.transform;
                for (int i = 0; i < root.childCount; i++)
                {
                    Transform c = root.GetChild(i);
                    if (slot.Instance != null && c != slot.Instance.transform && c.GetComponent<RectTransform>() != null)
                    {
                        return c as RectTransform;
                    }
                }
                if (slot.Instance != null)
                {
                    return (RectTransform)BackgroundMidField?.GetValue(slot.Instance);
                }
                return null;
            }
        }
        catch
        {
            return null;
        }
    }

    internal static int GetSlotForInstance(PlayerHealth health)
    {
        if (health == null) return -1;
        for (int i = 1; i < slots.Length; i++)
        {
            if (slots[i] != null && slots[i].Instance == health)
                return i;
        }
        if (health == OriginalHealth) return 0;
        return -1;
    }

    internal static void BringToFrontAll()
    {
        for (int i = 1; i < slots.Length; i++)
        {
            slots[i]?.BringToFront();
        }
    }

    internal static void SetVisibleAll(bool visible, bool instant = false)
    {
        for (int i = 1; i < slots.Length; i++)
        {
            slots[i]?.SetVisible(visible, instant);
        }
    }

    internal static void DestroySlot(int slotIndex)
    {
        if (slotIndex <= 0 || slotIndex >= slots.Length) return;
        HealthBarSlotInfo slot = slots[slotIndex];
        if (slot?.InstanceRoot != null)
        {
            UnityEngine.Object.Destroy(slot.InstanceRoot);
            slot.InstanceRoot = null;
            slot.Instance = null;
        }
    }

    internal static void EnsureCreated(int slotIndex, Penitent penitent)
    {
        if (slotIndex <= 0 || slotIndex >= slots.Length) return;
        HealthBarSlotInfo slot = slots[slotIndex];

        if (slot.InstanceRoot != null)
        {
            UnityEngine.Object.Destroy(slot.InstanceRoot);
            slot.InstanceRoot = null;
            slot.Instance = null;
        }

        PlayerHealth original = OriginalHealth;
        if (original == null)
        {
            DashParryDebugLog.Log($"CoopHealthBars.EnsureCreated: cancelado para P{slotIndex + 1} - original={original != null}");
            return;
        }

        if (penitent == null)
        {
            penitent = Core.Logic?.Penitent;
            if (penitent == null)
            {
                DashParryDebugLog.Log($"CoopHealthBars.EnsureCreated: cancelado para P{slotIndex + 1} - penitent nulo");
                return;
            }
        }

        Canvas canvas = original.GetComponentInParent<Canvas>();
        while (canvas != null && canvas.transform.parent != null)
        {
            Canvas parentCanvas = canvas.transform.parent.GetComponentInParent<Canvas>();
            if (parentCanvas == null) break;
            canvas = parentCanvas;
        }
        Transform cloneParent = canvas != null ? canvas.transform : original.transform.parent;
        Transform originalParent = original.transform.parent;
        GameObject sourceToClone = originalParent != null ? originalParent.gameObject : original.gameObject;

        GameObject cloneObject = UnityEngine.Object.Instantiate(sourceToClone, cloneParent);
        cloneObject.name = $"PlayerHealth_P{slotIndex + 1}";
        slot.InstanceRoot = cloneObject;
        slot.Instance = cloneObject.GetComponentInChildren<PlayerHealth>();

        if (slot.Instance != null && LastBarWidthField != null)
        {
            LastBarWidthField.SetValue(slot.Instance, -1f);
        }

        HudFade.PrepareHidden(slot.InstanceRoot);

        RectTransform rect = cloneObject.GetComponent<RectTransform>();
        if (rect != null)
        {
            bool is3Plus = CoopGameConfig.IsHudLayout3Plus;
            var cfg = HudLayoutConfig.Current;

            if (slotIndex == 1) // P2
            {
                if (!is3Plus)
                {
                    // Modo 2 Jugadores: esquina inferior derecha (comportamiento original idéntico)
                    rect.anchorMin = new Vector2(1f, 0f);
                    rect.anchorMax = new Vector2(1f, 0f);
                    rect.pivot = new Vector2(1f, 0f);
                    slot.Scale = cfg.p2_health_2p.scale;
                    slot.AnchoredPosition = cfg.p2_health_2p.Position;
                }
                else
                {
                    // Modo 3-4 Jugadores: esquina superior derecha
                    rect.anchorMin = new Vector2(1f, 1f);
                    rect.anchorMax = new Vector2(1f, 1f);
                    rect.pivot = new Vector2(1f, 1f);
                    slot.Scale = cfg.p2_health.scale;
                    slot.AnchoredPosition = cfg.p2_health.Position;
                }
            }
            else if (slotIndex == 2) // P3
            {
                // Modo 3-4 Jugadores: lado izquierdo, debajo de P1
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                slot.Scale = cfg.p3_health.scale;
                slot.AnchoredPosition = cfg.p3_health.Position;
            }
            else if (slotIndex == 3) // P4
            {
                // Modo 3-4 Jugadores: lado derecho, debajo de P2
                rect.anchorMin = new Vector2(1f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 1f);
                slot.Scale = cfg.p4_health.scale;
                slot.AnchoredPosition = cfg.p4_health.Position;
            }

            rect.anchoredPosition = slot.AnchoredPosition;
            rect.localScale = Vector3.one * slot.Scale;
        }

        // Aplicar posición personalizada guardada para P1 si existe (solo si no es 0,0 por defecto)
        if (HudLayoutConfig.Current.isCustomSaved && (HudLayoutConfig.Current.p1_health.x != 0f || HudLayoutConfig.Current.p1_health.y != 0f))
        {
            var p1Rect = P1HealthRect;
            if (p1Rect != null)
            {
                p1Rect.anchoredPosition = HudLayoutConfig.Current.p1_health.Position;
                p1Rect.localScale = Vector3.one * HudLayoutConfig.Current.p1_health.scale;
            }
        }

        OnPenitentReadyMethod.Invoke(slot.Instance, new object[] { penitent });

        if (slot.Instance != null)
        {
            if (CalculateHealthBarSizeMethod != null) CalculateHealthBarSizeMethod.Invoke(slot.Instance, null);
            if (CalculateHealthBarMethod != null) CalculateHealthBarMethod.Invoke(slot.Instance, null);
            if (CalculateLossBarMethod != null) CalculateLossBarMethod.Invoke(slot.Instance, null);

            // Asegurar que la barra de vida y de pérdida comiencen 100% llenas de forma inmediata
            FieldInfo hField = AccessTools.Field(typeof(PlayerHealth), "health");
            FieldInfo lField = AccessTools.Field(typeof(PlayerHealth), "loss");
            float targetRatio = (penitent.Stats?.Life != null && penitent.Stats.Life.Final > 0f)
                ? (penitent.Stats.Life.Current / penitent.Stats.Life.Final)
                : 1f;

            Image hImg = hField?.GetValue(slot.Instance) as Image;
            Image lImg = lField?.GetValue(slot.Instance) as Image;
            if (hImg != null) hImg.fillAmount = targetRatio;
            if (lImg != null) lImg.fillAmount = targetRatio;
        }
    }
}

// Facade compatible con todo el código existente de Player2HealthBar
internal static class Player2HealthBar
{
    internal static float Scale
    {
        get => CoopHealthBars.GetSlot(1).Scale;
        set => CoopHealthBars.GetSlot(1).Scale = value;
    }

    internal static Vector2 AnchoredPosition
    {
        get => CoopHealthBars.GetSlot(1).AnchoredPosition;
        set => CoopHealthBars.GetSlot(1).AnchoredPosition = value;
    }

    internal static PlayerHealth Instance => CoopHealthBars.GetSlot(1)?.Instance;
    internal static RectTransform CloneRect => CoopHealthBars.GetSlot(1)?.CloneRect;

    internal static void BringToFront() => CoopHealthBars.BringToFrontAll();
    internal static void SetVisible(bool visible, bool instant = false) => CoopHealthBars.SetVisibleAll(visible, instant);
    internal static void EnsureCreated(Penitent p2) => CoopHealthBars.EnsureCreated(1, p2);
}

[HarmonyPatch(typeof(PlayerHealth), "OnPenitentReady")]
internal static class PlayerHealth_OnPenitentReady_P2_Patch
{
    private static void Prefix(PlayerHealth __instance, ref Penitent penitent)
    {
        int slot = CoopHealthBars.GetSlotForInstance(__instance);
        if (slot > 0)
        {
            Penitent p = CoopPlayerRegistry.GetPenitent(slot);
            if (p != null)
            {
                penitent = p;
            }
        }
    }
}

[HarmonyPatch(typeof(PlayerHealth), "BarTarget", MethodType.Getter)]
internal static class PlayerHealth_BarTarget_P2_Patch
{
    private static void Postfix(PlayerHealth __instance, ref float __result)
    {
        int slot = CoopHealthBars.GetSlotForInstance(__instance);
        if (slot <= 0)
        {
            return;
        }
        Penitent p = CoopPlayerRegistry.GetPenitent(slot);
        __result = (p != null && p.Stats != null && p.Stats.Life != null && p.Stats.Life.Final > 0f)
            ? (p.Stats.Life.Current / p.Stats.Life.Final)
            : 0f;
    }
}

[HarmonyPatch(typeof(PlayerHealth), "CalculateHealthBarSize")]
internal static class PlayerHealth_CalculateHealthBarSize_P2_Patch
{
    private static readonly FieldInfo LastBarWidthField = AccessTools.Field(typeof(PlayerHealth), "lastBarWidth");
    private static readonly FieldInfo BackgroundStartSizeField = AccessTools.Field(typeof(PlayerHealth), "backgroundStartSize");
    private static readonly FieldInfo EndFillSizeField = AccessTools.Field(typeof(PlayerHealth), "endFillSize");
    private static readonly FieldInfo BackgroundMidField = AccessTools.Field(typeof(PlayerHealth), "backgroundMid");
    private static readonly FieldInfo HealthTransformField = AccessTools.Field(typeof(PlayerHealth), "healthTransform");
    private static readonly FieldInfo LossTransformField = AccessTools.Field(typeof(PlayerHealth), "lossTransform");
    private static readonly FieldInfo BackgroundFillTransformField = AccessTools.Field(typeof(PlayerHealth), "backgroundFillTransform");

    private static bool Prefix(PlayerHealth __instance)
    {
        int slot = CoopHealthBars.GetSlotForInstance(__instance);
        if (slot <= 0)
        {
            return true;
        }
        Penitent p = CoopPlayerRegistry.GetPenitent(slot);
        if (p == null || p.Stats == null || p.Stats.Life == null)
        {
            return false;
        }

        float final = p.Stats.Life.Final;
        float lastBarWidth = (float)LastBarWidthField.GetValue(__instance);
        if (final == lastBarWidth)
        {
            return false;
        }
        LastBarWidthField.SetValue(__instance, final);

        float backgroundStartSize = (float)BackgroundStartSizeField.GetValue(__instance);
        float endFillSize = (float)EndFillSizeField.GetValue(__instance);
        float num = Mathf.Max(final - backgroundStartSize - endFillSize, 0f);

        RectTransform backgroundMid = (RectTransform)BackgroundMidField.GetValue(__instance);
        RectTransform healthTransform = (RectTransform)HealthTransformField.GetValue(__instance);
        RectTransform lossTransform = (RectTransform)LossTransformField.GetValue(__instance);
        RectTransform backgroundFillTransform = (RectTransform)BackgroundFillTransformField.GetValue(__instance);

        backgroundMid.sizeDelta = new Vector2(num, backgroundMid.sizeDelta.y);
        lossTransform.sizeDelta = new Vector2(final, lossTransform.sizeDelta.y);
        healthTransform.sizeDelta = new Vector2(final, healthTransform.sizeDelta.y);
        backgroundFillTransform.sizeDelta = new Vector2(final, healthTransform.sizeDelta.y);
        return false;
    }
}

[HarmonyPatch(typeof(PlayerHealth), "CalculateLossBar")]
internal static class PlayerHealth_CalculateLossBar_P2_Patch
{
    private static readonly FieldInfo LossField = AccessTools.Field(typeof(PlayerHealth), "loss");
    private static readonly FieldInfo CurveField = AccessTools.Field(typeof(PlayerHealth), "HealthLossAnimationCurve");
    private static readonly FieldInfo DamageTimeElapsedField = AccessTools.Field(typeof(PlayerHealth), "_damageTimeElapsed");

    private static bool Prefix(PlayerHealth __instance)
    {
        int slot = CoopHealthBars.GetSlotForInstance(__instance);
        if (slot <= 0)
        {
            return true;
        }
        Penitent p = CoopPlayerRegistry.GetPenitent(slot);
        if (p == null || p.Stats == null || p.Stats.Life == null)
        {
            return false;
        }

        float target = p.Stats.Life.Current / p.Stats.Life.Final;
        Image loss = (Image)LossField.GetValue(__instance);
        if (loss != null && !Mathf.Approximately(loss.fillAmount, target))
        {
            float elapsed = (float)DamageTimeElapsedField.GetValue(__instance) + Time.deltaTime;
            DamageTimeElapsedField.SetValue(__instance, elapsed);
            AnimationCurve curve = (AnimationCurve)CurveField.GetValue(__instance);
            if (curve != null)
            {
                loss.fillAmount = Mathf.Lerp(loss.fillAmount, target, curve.Evaluate(elapsed));
            }
        }
        return false;
    }
}

[HarmonyPatch(typeof(PlayerHealth), "CalculateHealthBar")]
internal static class PlayerHealth_CalculateHealthBar_P2_Patch
{
    private static readonly FieldInfo HealthField = AccessTools.Field(typeof(PlayerHealth), "health");
    private static readonly FieldInfo SpeedField = AccessTools.Field(typeof(PlayerHealth), "speed");
    private static readonly FieldInfo DamageTimeElapsedField = AccessTools.Field(typeof(PlayerHealth), "_damageTimeElapsed");

    private static bool Prefix(PlayerHealth __instance)
    {
        int slot = CoopHealthBars.GetSlotForInstance(__instance);
        if (slot <= 0)
        {
            return true;
        }
        Penitent p = CoopPlayerRegistry.GetPenitent(slot);
        if (p == null || p.Stats == null || p.Stats.Life == null)
        {
            return false;
        }

        float target = p.Stats.Life.Current / p.Stats.Life.Final;
        Image health = (Image)HealthField.GetValue(__instance);
        if (health != null && !Mathf.Approximately(health.fillAmount, target))
        {
            float elapsed = (float)DamageTimeElapsedField.GetValue(__instance) + Time.deltaTime;
            DamageTimeElapsedField.SetValue(__instance, elapsed);
            float speed = (float)SpeedField.GetValue(__instance);
            health.fillAmount = Mathf.Lerp(health.fillAmount, target, elapsed * speed);
        }
        return false;
    }
}
