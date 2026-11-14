using Com.LuisPedroFonseca.ProCamera2D;
using CreativeSpore.SmartColliders;
using Framework.FrameworkCore;
using Framework.Managers;
using System;
using Gameplay.GameControllers.AnimationBehaviours.Player.Attack;
using Gameplay.GameControllers.AnimationBehaviours.Player.ClimbClifLede;
using Gameplay.GameControllers.AnimationBehaviours.Player.ClimbLadder;
using Gameplay.GameControllers.AnimationBehaviours.Player.Crouch;
using Gameplay.GameControllers.AnimationBehaviours.Player.Dash;
using Gameplay.GameControllers.AnimationBehaviours.Player.Hurt;
using Gameplay.GameControllers.AnimationBehaviours.Player.Jump;
using Gameplay.GameControllers.AnimationBehaviours.Player.Dead;
using Gameplay.GameControllers.AnimationBehaviours.Player.Prayer;
using Gameplay.GameControllers.AnimationBehaviours.Player.RangeAttack;
using Gameplay.GameControllers.AnimationBehaviours.Player.Run;
using Gameplay.GameControllers.AnimationBehaviours.Player.SubStatesBehaviours;
using Gameplay.GameControllers.Camera;
using Gameplay.GameControllers.Effects.Player.Recolor;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Enemies.Framework.Attack;
using Gameplay.GameControllers.Environment.AreaEffects;
using Gameplay.GameControllers.Penitent;
using Gameplay.GameControllers.Penitent.Abilities;
using Gameplay.GameControllers.Penitent.Attack;
using Gameplay.GameControllers.Penitent.Damage;
using Gameplay.GameControllers.Penitent.Gizmos;
using Gameplay.GameControllers.Penitent.InputSystem;
using Gameplay.GameControllers.Penitent.Sensor;
using Gameplay.UI.Others.UIGameLogic;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace Blasphemous.CoopLocal;

internal class FervourBarSlotInfo
{
    public int SlotIndex;
    public PlayerFervour Instance;
    public PlayerFlask FlaskInstance;
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

internal static class CoopFervourBars
{
    private static readonly FervourBarSlotInfo[] slots = new FervourBarSlotInfo[4];
    private static PlayerFervour originalCache;
    private static readonly FieldInfo GlobalInstanceField = Reflect.Field(typeof(PlayerFervour), "<Instance>k__BackingField");
    private static readonly FieldInfo LastBarWidthField = Reflect.Field(typeof(PlayerFervour), "lastBarWidth");
    private static readonly MethodInfo CalculateBarSizeMethod = Reflect.Method(typeof(PlayerFervour), "CalculateBarSize");
    private static readonly MethodInfo CalculateFillsBarsMethod = Reflect.Method(typeof(PlayerFervour), "CalculateFillsBars");

    static CoopFervourBars()
    {
        for (int i = 0; i < 4; i++)
        {
            slots[i] = new FervourBarSlotInfo { SlotIndex = i };
        }
    }

    internal static FervourBarSlotInfo GetSlot(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < slots.Length)
            return slots[slotIndex];
        return null;
    }

    internal static PlayerFervour OriginalFervour
    {
        get
        {
            if (originalCache == null)
            {
                originalCache = UnityEngine.Object.FindObjectOfType<PlayerFervour>();
            }
            return originalCache;
        }
    }

    internal static RectTransform P1FervourRect
    {
        get
        {
            var orig = OriginalFervour;
            if (orig == null) return null;
            Transform parent = orig.transform.parent;
            return (parent != null ? parent : orig.transform) as RectTransform;
        }
    }

    internal static int GetSlotForInstance(PlayerFervour fervour)
    {
        if (fervour == null) return -1;
        for (int i = 1; i < slots.Length; i++)
        {
            if (slots[i] != null && slots[i].Instance == fervour)
                return i;
        }
        if (fervour == OriginalFervour) return 0;
        return -1;
    }

    internal static int GetSlotForFlaskInstance(PlayerFlask flask)
    {
        if (flask == null) return -1;
        for (int i = 1; i < slots.Length; i++)
        {
            if (slots[i] != null && slots[i].FlaskInstance == flask)
                return i;
        }
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
        FervourBarSlotInfo slot = slots[slotIndex];
        if (slot?.InstanceRoot != null)
        {
            UnityEngine.Object.Destroy(slot.InstanceRoot);
            slot.InstanceRoot = null;
            slot.Instance = null;
            slot.FlaskInstance = null;
        }
    }

    // Hijos de "LeftPart" que necesita la barra de fervor/matraces (PlayerFervour referencia PrayerDetail*, Spark y PrayerTimer;
    // PlayerFlask busca Flask0..N dentro de "Flask"; "Image Fore" es el marco del retrato, como hasta ahora).
    private static readonly string[] FervourCloneParts =
    {
        "Fervour Bar", "Flask", "PrayerDetail", "PrayerDetail PE02", "Spark", "PrayerTimer", "Image Fore"
    };

    internal static void EnsureCreated(int slotIndex, Penitent penitent)
    {
        if (slotIndex <= 0 || slotIndex >= slots.Length) return;
        FervourBarSlotInfo slot = slots[slotIndex];

        if (slot.InstanceRoot != null)
        {
            UnityEngine.Object.Destroy(slot.InstanceRoot);
            slot.InstanceRoot = null;
            slot.Instance = null;
            slot.FlaskInstance = null;
        }

        PlayerFervour original = OriginalFervour;
        if (original == null)
        {
            DashParryDebugLog.Log($"CoopFervourBars.EnsureCreated: cancelado para P{slotIndex + 1} - original={original != null}");
            return;
        }

        if (penitent == null)
        {
            penitent = Core.Logic?.Penitent;
            if (penitent == null)
            {
                DashParryDebugLog.Log($"CoopFervourBars.EnsureCreated: cancelado para P{slotIndex + 1} - penitent nulo");
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
        cloneObject.name = $"PlayerFervour_P{slotIndex + 1}";
        HudCloneCleaner.DumpOnce("Fervour ORIGINAL parent", sourceToClone.transform);
        // PlayerFervour cuelga de "Fervour Bar", cuyo padre es "LeftPart" = TODO el HUD izquierdo de P1 (verificado en los datos del
        // juego): al clonarlo venía también la barra de vida roja de P1, los matraces, la penitencia, las llaves de jefe, etc.
        // Se conservan solo las piezas del fervor (lista explícita); si el juego renombrara "Fervour Bar", se usa el método antiguo.
        int stripped = HudCloneCleaner.KeepOnlyChildren(cloneObject, FervourCloneParts, "Fervour Bar");
        if (stripped < 0)
        {
            stripped = HudCloneCleaner.StripForeign(cloneObject,
                new[] { typeof(PlayerHealth), typeof(PlayerHealthDemake), typeof(PlayerHealthPE02) },
                new[] { typeof(PlayerFervour), typeof(PlayerFlask) });
            DashParryDebugLog.Log($"[HudClone] Fervour P{slotIndex + 1}: no se encontró 'Fervour Bar'; usado limpiado por tipos ({stripped}).");
        }
        else if (stripped > 0) DashParryDebugLog.Log($"[HudClone] Fervour P{slotIndex + 1}: conservadas las piezas de fervor, eliminados {stripped} objetos ajenos del HUD de P1.");
        HudCloneCleaner.DumpOnce("Fervour CLONE (tras limpiar)", cloneObject.transform);
        slot.InstanceRoot = cloneObject;
        slot.Instance = cloneObject.GetComponentInChildren<PlayerFervour>();
        slot.FlaskInstance = cloneObject.GetComponentInChildren<PlayerFlask>();

        HudFade.PrepareHidden(slot.InstanceRoot);

        // Restaurar el singleton estático de P1
        if (slot.Instance != null && GlobalInstanceField != null)
        {
            GlobalInstanceField.SetValue(null, original);
        }

        if (slot.Instance != null && LastBarWidthField != null)
        {
            LastBarWidthField.SetValue(slot.Instance, -1f);
        }

        RectTransform rect = cloneObject.GetComponent<RectTransform>();
        if (rect != null)
        {
            bool is3Plus = CoopGameConfig.IsHudLayout3Plus;
            // Ancla, posición y escala salen del contexto del jugador (layout guardado + modo 2P / 3-4P).
            CoopPlayerContext.HudPlacement placement = CoopPlayers.Get(slotIndex).GetHudPlacement(CoopPlayerContext.HudElement.Fervour, is3Plus);
            rect.anchorMin = placement.Anchor;
            rect.anchorMax = placement.Anchor;
            rect.pivot = placement.Anchor;
            slot.Scale = placement.Scale;
            slot.AnchoredPosition = placement.Position;

            rect.anchoredPosition = slot.AnchoredPosition;
            rect.localScale = Vector3.one * slot.Scale;
        }

        if (HudLayoutConfig.Current.isCustomSaved && (HudLayoutConfig.Current.p1_fervour.x != 0f || HudLayoutConfig.Current.p1_fervour.y != 0f))
        {
            var p1Fervour = P1FervourRect;
            if (p1Fervour != null)
            {
                p1Fervour.anchoredPosition = HudLayoutConfig.Current.p1_fervour.Position;
                p1Fervour.localScale = Vector3.one * HudLayoutConfig.Current.p1_fervour.scale;
            }
        }

        if (slot.Instance != null)
        {
            // PlayerFervour vanilla solo se habilita en LevelManager.OnLevelLoaded; un clon creado
            // después de cargar la sala quedaba congelado (sin Update) hasta el siguiente cambio de sala.
            slot.Instance.enabled = true;
            if (CalculateBarSizeMethod != null) CalculateBarSizeMethod.Invoke(slot.Instance, null);
            if (CalculateFillsBarsMethod != null) CalculateFillsBarsMethod.Invoke(slot.Instance, null);
        }
    }

    internal static void UpdatePositionAttachedToHealth(int slotIndex, Vector2 healthPos, float healthScale)
    {
        if (slotIndex <= 0 || slotIndex >= slots.Length) return;
        FervourBarSlotInfo slot = slots[slotIndex];
        if (slot == null || slot.CloneRect == null) return;

        bool is3Plus = CoopGameConfig.IsHudLayout3Plus;
        if (slotIndex == 1 && !is3Plus)
        {
            return;
        }

        var cfg = HudLayoutConfig.Current;
        var fPos = cfg.GetFervourConfig(slotIndex);
        if (cfg.isCustomSaved && fPos != null && (fPos.x != 0f || fPos.y != 0f))
        {
            slot.Scale = fPos.scale;
            slot.AnchoredPosition = fPos.Position;
        }
        else
        {
            slot.Scale = healthScale;
            slot.AnchoredPosition = healthPos + new Vector2(44f * healthScale, 27f * healthScale);
        }
        slot.CloneRect.anchoredPosition = slot.AnchoredPosition;
        slot.CloneRect.localScale = Vector3.one * slot.Scale;
    }
}

// Facade compatible con el código existente
internal static class Player2FervourBar
{
    internal static float Scale
    {
        get => CoopFervourBars.GetSlot(1).Scale;
        set => CoopFervourBars.GetSlot(1).Scale = value;
    }

    internal static Vector2 AnchoredPosition
    {
        get => CoopFervourBars.GetSlot(1).AnchoredPosition;
        set => CoopFervourBars.GetSlot(1).AnchoredPosition = value;
    }

    internal static PlayerFervour Instance => CoopFervourBars.GetSlot(1)?.Instance;
    internal static PlayerFlask FlaskInstance => CoopFervourBars.GetSlot(1)?.FlaskInstance;
    internal static RectTransform CloneRect => CoopFervourBars.GetSlot(1)?.CloneRect;

    internal static void SetVisible(bool visible, bool instant = false) => CoopFervourBars.SetVisibleAll(visible, instant);
    internal static void EnsureCreated(Penitent p2) => CoopFervourBars.EnsureCreated(1, p2);
}

[HarmonyPatch(typeof(PlayerFervour), "get_BarTarget")]
internal static class PlayerFervour_BarTarget_P2_Patch
{
    private static void Postfix(PlayerFervour __instance, ref float __result)
    {
        int slot = CoopFervourBars.GetSlotForInstance(__instance);
        if (slot <= 0) return;
        Penitent p = CoopPlayerRegistry.GetPenitent(slot);
        __result = (p != null && p.Stats != null && p.Stats.Fervour != null && p.Stats.Fervour.CurrentMaxWithoutFactor > 0f)
            ? p.Stats.Fervour.Current / p.Stats.Fervour.CurrentMaxWithoutFactor
            : 0f;
    }
}

[HarmonyPatch(typeof(PlayerFervour), "Update")]
internal static class PlayerFervour_Update_P2_Patch
{
    private static readonly FieldInfo NormalPrayerInUseField = Reflect.Field(typeof(PlayerFervour), "normalPrayerInUse");
    private static readonly FieldInfo Pe02PrayerInUseField = Reflect.Field(typeof(PlayerFervour), "pe02PrayerInUse");
    private static readonly FieldInfo PrayerTimerField = Reflect.Field(typeof(PlayerFervour), "prayerTimer");
    private static readonly FieldInfo LastValueField = Reflect.Field(typeof(PlayerFervour), "lastValue");
    private static readonly FieldInfo FillsIncreaseField = Reflect.Field(typeof(PlayerFervour), "fillsIncrease");
    private static readonly FieldInfo TimeElapsedField = Reflect.Field(typeof(PlayerFervour), "_timeElapsed");
    private static readonly FieldInfo LastMaxFervourField = Reflect.Field(typeof(PlayerFervour), "lastMaxFervour");
    private static readonly MethodInfo CalculateBarSizeMethod = Reflect.Method(typeof(PlayerFervour), "CalculateBarSize");
    private static readonly MethodInfo CalculateFillsBarsMethod = Reflect.Method(typeof(PlayerFervour), "CalculateFillsBars");
    private static readonly MethodInfo CalculateMarksMethod = Reflect.Method(typeof(PlayerFervour), "CalculateMarks");
    private static readonly MethodInfo CalculateNotEnoughMethod = Reflect.Method(typeof(PlayerFervour), "CalculateNotEnough");
    private static readonly MethodInfo CalculateBarPentaltyMethod = Reflect.Method(typeof(PlayerFervour), "CalculateBarPentalty");
    private static readonly FieldInfo DiagFillExactField = Reflect.Field(typeof(PlayerFervour), "fillExact");
    private static readonly FieldInfo DiagFillAnimableField = Reflect.Field(typeof(PlayerFervour), "fillAnimable");

    private static bool Prefix(PlayerFervour __instance)
    {
        int slot = CoopFervourBars.GetSlotForInstance(__instance);
        if (slot <= 0)
        {
            return true;
        }
        Penitent p = CoopPlayerRegistry.GetPenitent(slot);
        if (p == null || p.Stats == null || p.Stats.Fervour == null)
        {
            return false;
        }

        PrayerUse prayerCast = p.PrayerCast;
        bool isUsing = prayerCast != null && prayerCast.IsUsingAbility;
        bool useStocksOfHealth = Core.PenitenceManager.UseStocksOfHealth;
        ((GameObject)NormalPrayerInUseField.GetValue(__instance)).SetActive(isUsing && !useStocksOfHealth);
        ((GameObject)Pe02PrayerInUseField.GetValue(__instance)).SetActive(isUsing && useStocksOfHealth);

        float castFillAmount = isUsing ? 1f - prayerCast.GetPercentTimeCasting() : 0f;
        ((Image)PrayerTimerField.GetValue(__instance)).fillAmount = castFillAmount;

        float barTarget = p.Stats.Fervour.CurrentMaxWithoutFactor > 0f
            ? p.Stats.Fervour.Current / p.Stats.Fervour.CurrentMaxWithoutFactor
            : 0f;
        float lastValue = (float)LastValueField.GetValue(__instance);
        if (!Mathf.Approximately(lastValue, barTarget))
        {
            FillsIncreaseField.SetValue(__instance, barTarget > lastValue);
            LastValueField.SetValue(__instance, barTarget);
            TimeElapsedField.SetValue(__instance, 0f);
        }
        CalculateBarSizeMethod.Invoke(__instance, null);
        CalculateFillsBarsMethod.Invoke(__instance, null);
        CalculateMarksMethod.Invoke(__instance, null);
        CalculateNotEnoughMethod.Invoke(__instance, null);

        float maxFervour = p.Stats.Fervour.CurrentMaxWithoutFactor;
        float lastMaxFervour = (float)LastMaxFervourField.GetValue(__instance);
        if (!Mathf.Approximately(maxFervour, lastMaxFervour))
        {
            LastMaxFervourField.SetValue(__instance, maxFervour);
            CalculateBarPentaltyMethod.Invoke(__instance, null);
        }
        return false;
    }
}

[HarmonyPatch(typeof(PlayerFervour), "CalculateBarSize")]
internal static class PlayerFervour_CalculateBarSize_P2_Patch
{
    private static readonly FieldInfo LastBarWidthField = Reflect.Field(typeof(PlayerFervour), "lastBarWidth");
    private static readonly FieldInfo BackgroundStartSizeField = Reflect.Field(typeof(PlayerFervour), "backgroundStartSize");
    private static readonly FieldInfo EndFillSizeField = Reflect.Field(typeof(PlayerFervour), "endFillSize");
    private static readonly FieldInfo BackgroundMidField = Reflect.Field(typeof(PlayerFervour), "backgroundMid");
    private static readonly FieldInfo FillExactTransformField = Reflect.Field(typeof(PlayerFervour), "fillExactTransform");
    private static readonly FieldInfo FillExactFullTransformField = Reflect.Field(typeof(PlayerFervour), "fillExactFullTransform");
    private static readonly FieldInfo FillAnimableTransformField = Reflect.Field(typeof(PlayerFervour), "fillAnimableTransform");
    private static readonly FieldInfo BackgroundField = Reflect.Field(typeof(PlayerFervour), "background");
    private static readonly FieldInfo FillNotEnoughTransformField = Reflect.Field(typeof(PlayerFervour), "fillNotEnoughTransform");

    private static bool Prefix(PlayerFervour __instance)
    {
        int slot = CoopFervourBars.GetSlotForInstance(__instance);
        if (slot <= 0)
        {
            return true;
        }
        Penitent p = CoopPlayerRegistry.GetPenitent(slot);
        if (p == null || p.Stats == null || p.Stats.Fervour == null)
        {
            return false;
        }

        float maxWithoutFactor = p.Stats.Fervour.CurrentMaxWithoutFactor;
        float lastBarWidth = (float)LastBarWidthField.GetValue(__instance);
        if (maxWithoutFactor == lastBarWidth)
        {
            return false;
        }
        LastBarWidthField.SetValue(__instance, maxWithoutFactor);

        float backgroundStartSize = (float)BackgroundStartSizeField.GetValue(__instance);
        float endFillSize = (float)EndFillSizeField.GetValue(__instance);
        float width = Mathf.Max(maxWithoutFactor - backgroundStartSize - endFillSize, 0f);

        SetWidth(BackgroundMidField, __instance, width);
        SetWidth(FillExactTransformField, __instance, maxWithoutFactor);
        SetWidth(FillExactFullTransformField, __instance, maxWithoutFactor);
        SetWidth(FillAnimableTransformField, __instance, maxWithoutFactor);
        SetWidth(BackgroundField, __instance, maxWithoutFactor);
        SetWidth(FillNotEnoughTransformField, __instance, maxWithoutFactor);
        return false;
    }

    private static void SetWidth(FieldInfo field, PlayerFervour instance, float width)
    {
        RectTransform rect = (RectTransform)field.GetValue(instance);
        rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
    }
}

[HarmonyPatch(typeof(PlayerFervour), "CalculateFillsBars")]
internal static class PlayerFervour_CalculateFillsBars_P2_Patch
{
    private static readonly FieldInfo TimeElapsedField = Reflect.Field(typeof(PlayerFervour), "_timeElapsed");
    private static readonly FieldInfo FillsIncreaseField = Reflect.Field(typeof(PlayerFervour), "fillsIncrease");
    private static readonly FieldInfo FillExactField = Reflect.Field(typeof(PlayerFervour), "fillExact");
    private static readonly FieldInfo FillAnimableField = Reflect.Field(typeof(PlayerFervour), "fillAnimable");
    private static readonly FieldInfo FillNotEnoughField = Reflect.Field(typeof(PlayerFervour), "fillNotEnough");
    private static readonly FieldInfo AddAnimationCurveField = Reflect.Field(typeof(PlayerFervour), "AddAnimationCurve");
    private static readonly FieldInfo LossAnimationCurveField = Reflect.Field(typeof(PlayerFervour), "LossAnimationCurve");
    private static readonly FieldInfo FervourSparkField = Reflect.Field(typeof(PlayerFervour), "fervourSpark");

    private static bool Prefix(PlayerFervour __instance)
    {
        int slot = CoopFervourBars.GetSlotForInstance(__instance);
        if (slot <= 0)
        {
            return true;
        }
        Penitent p = CoopPlayerRegistry.GetPenitent(slot);
        if (p == null || p.Stats == null || p.Stats.Fervour == null)
        {
            return false;
        }

        float barTarget = p.Stats.Fervour.Current / p.Stats.Fervour.CurrentMaxWithoutFactor;
        float maxWithoutFactor = p.Stats.Fervour.CurrentMaxWithoutFactor;
        float timeElapsed = (float)TimeElapsedField.GetValue(__instance) + Time.deltaTime;
        TimeElapsedField.SetValue(__instance, timeElapsed);

        Image fillExact = (Image)FillExactField.GetValue(__instance);
        Image fillAnimable = (Image)FillAnimableField.GetValue(__instance);
        Image fillNotEnough = (Image)FillNotEnoughField.GetValue(__instance);
        bool fillsIncrease = (bool)FillsIncreaseField.GetValue(__instance);

        if (fillsIncrease)
        {
            if (Mathf.Approximately(fillExact.fillAmount, barTarget))
            {
                fillExact.fillAmount = barTarget;
                TimeElapsedField.SetValue(__instance, 0f);
            }
            else
            {
                AnimationCurve addCurve = (AnimationCurve)AddAnimationCurveField.GetValue(__instance);
                fillExact.fillAmount = Mathf.Lerp(fillExact.fillAmount, barTarget, addCurve.Evaluate(timeElapsed));

                float sparkX = (float)(int)maxWithoutFactor * fillExact.fillAmount - 1f;
                GameObject spark = (GameObject)FervourSparkField.GetValue(__instance);
                Vector3 sparkPos = spark.transform.localPosition;
                spark.transform.localPosition = new Vector3(sparkX, sparkPos.y);
            }
            fillAnimable.fillAmount = fillExact.fillAmount;
        }
        else
        {
            fillExact.fillAmount = barTarget;
            if (Mathf.Approximately(fillAnimable.fillAmount, barTarget))
            {
                fillAnimable.fillAmount = barTarget;
                TimeElapsedField.SetValue(__instance, 0f);
            }
            else
            {
                AnimationCurve lossCurve = (AnimationCurve)LossAnimationCurveField.GetValue(__instance);
                fillAnimable.fillAmount = Mathf.Lerp(fillAnimable.fillAmount, barTarget, lossCurve.Evaluate(timeElapsed));
            }
        }
        fillNotEnough.fillAmount = fillExact.fillAmount;
        return false;
    }
}

[HarmonyPatch(typeof(PlayerFervour), "CalculateMarks")]
internal static class PlayerFervour_CalculateMarks_P2_Patch
{
    private static readonly FieldInfo FillExactFullField = Reflect.Field(typeof(PlayerFervour), "fillExactFull");
    private static readonly FieldInfo EpsilonToShowLastBarField = Reflect.Field(typeof(PlayerFervour), "epsilonToShowLastBar");
    private static readonly FieldInfo CurrentMarksField = Reflect.Field(typeof(PlayerFervour), "currentMarks");
    private static readonly FieldInfo CurrentMarksSeparationField = Reflect.Field(typeof(PlayerFervour), "currentMarksSeparation");
    private static readonly FieldInfo CurrentSegmentsFilledField = Reflect.Field(typeof(PlayerFervour), "currentSegmentsFilled");
    private static readonly FieldInfo MarksParentField = Reflect.Field(typeof(PlayerFervour), "marksParent");
    private static readonly FieldInfo BarMaskChildNameField = Reflect.Field(typeof(PlayerFervour), "barMaskChildName");
    private static readonly FieldInfo BarBarChildNameField = Reflect.Field(typeof(PlayerFervour), "barBarChildName");
    private static readonly FieldInfo BarAnimChildNameField = Reflect.Field(typeof(PlayerFervour), "barAnimChildName");
    private static readonly FieldInfo BarAnimEndPositionField = Reflect.Field(typeof(PlayerFervour), "barAnimEndPosition");
    private static readonly FieldInfo BarAnimMovementPerElapsedField = Reflect.Field(typeof(PlayerFervour), "barAnimMovementPerElapsed");
    private static readonly FieldInfo BarAnimUpdatedElapsedField = Reflect.Field(typeof(PlayerFervour), "barAnimUpdatedElapsed");
    private static readonly FieldInfo CurrentAnimPositionField = Reflect.Field(typeof(PlayerFervour), "currentAnimPosition");
    private static readonly FieldInfo CurrentAnimElapsedField = Reflect.Field(typeof(PlayerFervour), "currentAnimElapsed");
    private static readonly FieldInfo AnimsField = Reflect.Field(typeof(PlayerFervour), "anims");
    private static readonly MethodInfo SetBarPositionMethod = Reflect.Method(typeof(PlayerFervour), "SetBarPosition");

    private static bool Prefix(PlayerFervour __instance)
    {
        int slot = CoopFervourBars.GetSlotForInstance(__instance);
        if (slot <= 0)
        {
            return true;
        }
        Penitent p = CoopPlayerRegistry.GetPenitent(slot);
        if (p == null || p.Stats == null || p.Stats.Fervour == null)
        {
            return false;
        }

        int num = 0;
        float num2 = 0f;
        Framework.Inventory.Prayer prayerInSlot = (slot == 1)
            ? Player2InventoryManager.GetEquippedPrayerObj()
            : Player2InventoryManager.GetEquippedPrayerObj(slot);
        int num3 = prayerInSlot != null ? prayerInSlot.fervourNeeded + (int)p.Stats.PrayerCostAddition.Final : 0;
        Image fillExactFull = (Image)FillExactFullField.GetValue(__instance);
        if (num3 > 0)
        {
            num = (int)p.Stats.Fervour.CurrentMax / num3;
            num2 = (int)p.Stats.Fervour.Current / num3;
            fillExactFull.fillAmount = num2 * num3 / p.Stats.Fervour.CurrentMaxWithoutFactor;
        }
        else
        {
            fillExactFull.fillAmount = 0f;
        }

        float epsilonToShowLastBar = (float)EpsilonToShowLastBarField.GetValue(__instance);
        bool showLastBar = p.Stats.Fervour.CurrentMax - num3 * num > epsilonToShowLastBar;
        bool skippedAnimReset = false;
        float restPosition = -num3 + 1f;

        int currentMarks = (int)CurrentMarksField.GetValue(__instance);
        int currentMarksSeparation = (int)CurrentMarksSeparationField.GetValue(__instance);
        float currentSegmentsFilled = (float)CurrentSegmentsFilledField.GetValue(__instance);

        if (num != currentMarks || num3 != currentMarksSeparation || num2 != currentSegmentsFilled)
        {
            float currentAnimPosition = (float)CurrentAnimPositionField.GetValue(__instance);
            int barAnimEndPosition = (int)BarAnimEndPositionField.GetValue(__instance);
            List<RectTransform> anims = (List<RectTransform>)AnimsField.GetValue(__instance);

            if (num == 0)
            {
                currentAnimPosition = restPosition;
                CurrentAnimElapsedField.SetValue(__instance, 0f);
                skippedAnimReset = true;
            }
            anims.Clear();
            if (currentAnimPosition > barAnimEndPosition)
            {
                currentAnimPosition = restPosition;
            }
            CurrentAnimPositionField.SetValue(__instance, currentAnimPosition);

            CurrentMarksField.SetValue(__instance, num);
            CurrentMarksSeparationField.SetValue(__instance, num3);
            CurrentSegmentsFilledField.SetValue(__instance, num2);

            Transform marksParent = (Transform)MarksParentField.GetValue(__instance);
            string barMaskChildName = (string)BarMaskChildNameField.GetValue(__instance);
            string barBarChildName = (string)BarBarChildNameField.GetValue(__instance);
            string barAnimChildName = (string)BarAnimChildNameField.GetValue(__instance);

            float xPos = 0f;
            for (int i = 0; i < marksParent.childCount; i++)
            {
                RectTransform rectTransform = (RectTransform)marksParent.GetChild(i);
                bool active = i < num;
                rectTransform.gameObject.SetActive(active);
                if (!active)
                {
                    continue;
                }
                rectTransform.sizeDelta = new Vector2(num3, rectTransform.sizeDelta.y);
                rectTransform.localPosition = new Vector3(xPos, 0f, 0f);
                xPos += num3;
                RectTransform mask = (RectTransform)rectTransform.Find(barMaskChildName);
                mask.sizeDelta = new Vector2(num3 - 1f, mask.sizeDelta.y);
                RectTransform bar = (RectTransform)rectTransform.Find(barBarChildName);
                bar.gameObject.SetActive(showLastBar || i != num - 1);
                bool filled = i < currentSegmentsFilled;
                RectTransform anim = (RectTransform)mask.Find(barAnimChildName);
                anim.gameObject.SetActive(filled);
                if (filled)
                {
                    SetBarPositionMethod.Invoke(__instance, new object[] { anim });
                    anims.Add(anim);
                }
            }
        }

        if (skippedAnimReset || num <= 0)
        {
            return false;
        }

        float elapsed = (float)CurrentAnimElapsedField.GetValue(__instance) + Time.deltaTime;
        float barAnimUpdatedElapsed = (float)BarAnimUpdatedElapsedField.GetValue(__instance);
        if (elapsed >= barAnimUpdatedElapsed)
        {
            elapsed = 0f;
            float pos = (float)CurrentAnimPositionField.GetValue(__instance) + (float)BarAnimMovementPerElapsedField.GetValue(__instance);
            int barAnimEndPosition = (int)BarAnimEndPositionField.GetValue(__instance);
            if (pos > barAnimEndPosition)
            {
                pos = restPosition;
            }
            CurrentAnimPositionField.SetValue(__instance, pos);
            List<RectTransform> anims = (List<RectTransform>)AnimsField.GetValue(__instance);
            foreach (RectTransform anim in anims)
            {
                SetBarPositionMethod.Invoke(__instance, new object[] { anim });
            }
        }
        CurrentAnimElapsedField.SetValue(__instance, elapsed);
        return false;
    }
}
