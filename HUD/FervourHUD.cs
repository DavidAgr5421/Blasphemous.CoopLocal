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

// Round 37: second HUD widget for P2, per the user's request to extend Player2HealthBar's
// approach to Fervour too. Same clone-then-redirect mechanism, with one extra wrinkle
// PlayerHealth didn't have: PlayerFervour.Awake() does `Instance = this` unconditionally (a
// static singleton - other code, e.g. Healing.Heal()'s spark effect, calls
// PlayerFervour.Instance.ShowSpark() expecting P1's real bar). Cloning it would otherwise
// silently steal the global Instance for the clone and break that for P1 - EnsureCreated resets
// Instance back to the original immediately after creating the clone to prevent this;
// Player2FervourBar.Instance (not the static PlayerFervour.Instance) is what every patch below
// actually checks against.
//
// Decompiling PlayerFervour turned up FIVE separate methods independently hardcoding
// Core.Logic.Penitent (BarTarget, CalculateBarSize, CalculateFillsBars, CalculateMarks,
// CalculateBarPentalty) - more than PlayerHealth's two. Reimplemented here, following the same
// proven approach: CalculateBarSize (controls the bar's rendered width - the most visually
// broken without a fix) and CalculateFillsBars (the actual fill-amount animation, computed
// directly from P2's own stats rather than through the possibly-inlined BarTarget getter, same
// as PlayerHealth's CalculateHealthBar/CalculateLossBar). CalculateMarks (segment tick marks) and
// CalculateBarPentalty (the "guilt" overlay bar) are NOT reimplemented yet - known gap, left
// running unmodified (so they'll still read P1's numbers for those two specific visual details)
// rather than guessing their IL translations blind on top of everything else this round already
// covers; revisit if the user reports those specific pieces looking wrong for P2.
internal static class Player2FervourBar
{
    // Round 44: was a const - promoted to a mutable field so Player2HudPositionTuner's "." / "-"
    // scale keys can adjust it live.
    internal static float Scale = 0.65f;

    // PlayerFervour.Instance's setter is private (Awake() calls it on itself) - reflection is the
    // only way to reset the global singleton back to the original after the clone's own Awake()
    // steals it.
    private static readonly FieldInfo GlobalInstanceField =
        AccessTools.Field(typeof(PlayerFervour), "<Instance>k__BackingField");

    // Round 45: final position, re-confirmed by the user via live Player2HudPositionTuner testing
    // (stacked below Health, which renders on top per the z-order fix).
    internal static Vector2 AnchoredPosition = new Vector2(-75f, 7f);

    internal static PlayerFervour Instance { get; private set; }

    // Round 40: PlayerFlask ("Flask0"/"Flask1"/... potion sprites) lives as a sibling inside the
    // same "LeftPart" hierarchy this class already clones wholesale (see the class comment above -
    // follow-up #8's sibling dump listed "Flask" alongside "Fervour Bar"/"Penitence"/etc) - it
    // rides along as an unpatched, un-redirected duplicate unless something registers and
    // redirects it too, which is exactly why P2's potion count was frozen showing P1's count from
    // the moment of cloning (4 slots, never decreasing) instead of P2's own (2 slots, live).
    internal static PlayerFlask FlaskInstance { get; private set; }

    internal static RectTransform CloneRect => instanceRoot != null ? instanceRoot.GetComponent<RectTransform>() : null;
    private static PlayerFervour originalCache;
    private static GameObject instanceRoot;

    internal static void EnsureCreated(Penitent p2)
    {
        if (instanceRoot != null)
        {
            UnityEngine.Object.Destroy(instanceRoot);
            instanceRoot = null;
            Instance = null;
            FlaskInstance = null;
        }

        if (originalCache == null)
        {
            originalCache = UnityEngine.Object.FindObjectOfType<PlayerFervour>();
        }
        PlayerFervour original = originalCache;
        if (original == null || p2 == null)
        {
            DashParryDebugLog.Log($"Player2FervourBar.EnsureCreated: aborted - original PlayerFervour found={original != null}, p2 found={p2 != null}");
            return;
        }

        Canvas canvas = original.GetComponentInParent<Canvas>();
        while (canvas != null && canvas.transform.parent != null)
        {
            Canvas parentCanvas = canvas.transform.parent.GetComponentInParent<Canvas>();
            if (parentCanvas == null)
            {
                break;
            }
            canvas = parentCanvas;
        }
        Transform cloneParent = canvas != null ? canvas.transform : original.transform.parent;

        Transform originalParent = original.transform.parent;
        GameObject sourceToClone = originalParent != null ? originalParent.gameObject : original.gameObject;

        GameObject cloneObject = UnityEngine.Object.Instantiate(sourceToClone, cloneParent);
        cloneObject.name = "PlayerFervour_P2";
        instanceRoot = cloneObject;
        Instance = cloneObject.GetComponentInChildren<PlayerFervour>();
        FlaskInstance = cloneObject.GetComponentInChildren<PlayerFlask>();

        // Undo the clone's own Awake() stealing the global static Instance - see class comment.
        if (Instance != null)
        {
            GlobalInstanceField.SetValue(null, original);
        }

        RectTransform rect = cloneObject.GetComponent<RectTransform>();
        if (rect != null)
        {
            // Same bottom-right corner as Player2HealthBar, positioned via AnchoredPosition above.
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = AnchoredPosition;
            rect.localScale *= Scale;
        }

        DashParryDebugLog.Log(
            $"Player2FervourBar.EnsureCreated: cloned from '{sourceToClone.name}', foundPlayerFervour={Instance != null}, " +
            $"globalInstanceRestored={(PlayerFervour.Instance == original)}, foundPlayerFlask={FlaskInstance != null}");
    }
}

[HarmonyPatch(typeof(PlayerFervour), "get_BarTarget")]
internal static class PlayerFervour_BarTarget_P2_Patch
{
    private static void Postfix(PlayerFervour __instance, ref float __result)
    {
        if (__instance != Player2FervourBar.Instance)
        {
            return;
        }
        Penitent p2 = CoopLocal.Player2;
        __result = p2 != null ? p2.Stats.Fervour.Current / p2.Stats.Fervour.CurrentMaxWithoutFactor : 0f;
    }
}

// Round 41: the user reported the cloned Fervour bar frozen - never changing or resetting, even
// though CalculateBarSize/CalculateFillsBars below were already correctly redirected. Root cause:
// those two are only ever CALLED from Update() when `lastValue != this.BarTarget` - and Update()
// itself was never patched, so it's still running vanilla, reading BarTarget through a plain
// `call` to the small property getter. That's the exact same "small property likely gets JIT-
// inlined past a Harmony Postfix" risk already proven real for PlayerHealth's own BarTarget (see
// that class's comments) - if Update()'s own BarTarget read is inlined, it keeps comparing
// against *P1's* ratio internally, so unless P1's Fervour happens to change too, the "did it
// change" check never trips and CalculateBarSize/CalculateFillsBars simply never get called for
// P2's clone at all - leaving it stuck at whatever it displayed at spawn. Fixed by reimplementing
// Update() itself (mirroring PlayerHealth's CalculateHealthBar/CalculateLossBar being fully
// reimplemented rather than just patching what they call) - computing barTarget directly from
// P2's own stats, then invoking the *already-patched* CalculateBarSize/CalculateFillsBars
// methods via reflection (Harmony patches the underlying method itself, so a reflection Invoke()
// call from here still runs through those Prefixes correctly - no inlining risk for this call
// site since it's our own C# code, not vanilla's). CalculateMarks/CalculateBarPentalty are still
// unpatched (existing known gap - they'll run with their own internal Core.Logic.Penitent reads).
[HarmonyPatch(typeof(PlayerFervour), "Update")]
internal static class PlayerFervour_Update_P2_Patch
{
    private static readonly FieldInfo NormalPrayerInUseField = AccessTools.Field(typeof(PlayerFervour), "normalPrayerInUse");
    private static readonly FieldInfo Pe02PrayerInUseField = AccessTools.Field(typeof(PlayerFervour), "pe02PrayerInUse");
    private static readonly FieldInfo PrayerTimerField = AccessTools.Field(typeof(PlayerFervour), "prayerTimer");
    private static readonly FieldInfo LastValueField = AccessTools.Field(typeof(PlayerFervour), "lastValue");
    private static readonly FieldInfo FillsIncreaseField = AccessTools.Field(typeof(PlayerFervour), "fillsIncrease");
    private static readonly FieldInfo TimeElapsedField = AccessTools.Field(typeof(PlayerFervour), "_timeElapsed");
    private static readonly FieldInfo LastMaxFervourField = AccessTools.Field(typeof(PlayerFervour), "lastMaxFervour");
    private static readonly MethodInfo CalculateBarSizeMethod = AccessTools.Method(typeof(PlayerFervour), "CalculateBarSize");
    private static readonly MethodInfo CalculateFillsBarsMethod = AccessTools.Method(typeof(PlayerFervour), "CalculateFillsBars");
    private static readonly MethodInfo CalculateMarksMethod = AccessTools.Method(typeof(PlayerFervour), "CalculateMarks");
    private static readonly MethodInfo CalculateNotEnoughMethod = AccessTools.Method(typeof(PlayerFervour), "CalculateNotEnough");
    private static readonly MethodInfo CalculateBarPentaltyMethod = AccessTools.Method(typeof(PlayerFervour), "CalculateBarPentalty");
    private static readonly FieldInfo DiagFillExactField = AccessTools.Field(typeof(PlayerFervour), "fillExact");
    private static readonly FieldInfo DiagFillAnimableField = AccessTools.Field(typeof(PlayerFervour), "fillAnimable");

    // Round 43: the HIT and MISS branches previously shared one throttle counter - since both
    // P1's real instance (MISS) and P2's clone (HIT) call Update() every frame, whichever one
    // Unity happened to process first each frame "won" the shared 60-frame window and starved the
    // other branch's log out entirely - confirmed live (an entire test session only ever logged
    // MISS lines, never once HIT, even though the user's own report proves P2's bar *does*
    // respond). Separate counters per branch so both get logged independently.
    private static int lastLoggedMissFrame = -999;
    private static int lastLoggedHitFrame = -999;

    private static bool Prefix(PlayerFervour __instance)
    {
        if (__instance != Player2FervourBar.Instance)
        {
            if (Main.CoopLocal != null && Time.frameCount - lastLoggedMissFrame >= 60)
            {
                lastLoggedMissFrame = Time.frameCount;
                Penitent owner = __instance.GetComponentInParent<Penitent>();
                // Fervour bars live under a UI Canvas, not physically parented under the Penitent
                // transform - GetComponentInParent<Penitent>() reliably returns null for *every*
                // PlayerFervour instance (P1's real one included), confirmed live, so it can't be
                // used to identify which instance this is. Not chasing that further this round.
                Blasphemous.ModdingAPI.ModLog.Info(
                    $"[FervourDiag] Update() MISS: instance={__instance.GetInstanceID()} owner={DashParryDebugLog.Label(owner)} " +
                    $"Player2FervourBar.Instance={(Player2FervourBar.Instance != null ? Player2FervourBar.Instance.GetInstanceID().ToString() : "null")} " +
                    $"gameObject='{__instance.gameObject.name}' active={__instance.gameObject.activeInHierarchy}",
                    Main.CoopLocal);
            }
            return true;
        }
        Penitent p2 = CoopLocal.Player2;
        if (p2 == null)
        {
            return false;
        }

        if (Main.CoopLocal != null && Time.frameCount - lastLoggedHitFrame >= 60)
        {
            lastLoggedHitFrame = Time.frameCount;
            Image diagFillExact = (Image)DiagFillExactField.GetValue(__instance);
            Image diagFillAnimable = (Image)DiagFillAnimableField.GetValue(__instance);
            Blasphemous.ModdingAPI.ModLog.Info(
                $"[FervourDiag] Update() HIT: instance={__instance.GetInstanceID()} P2.Fervour.Current={p2.Stats.Fervour.Current:F1} " +
                $"P2.Fervour.CurrentMaxWithoutFactor={p2.Stats.Fervour.CurrentMaxWithoutFactor:F1} lastValue={LastValueField.GetValue(__instance)} " +
                $"fillExact.fillAmount={(diagFillExact != null ? diagFillExact.fillAmount.ToString("F3") : "null")} " +
                $"fillAnimable.fillAmount={(diagFillAnimable != null ? diagFillAnimable.fillAmount.ToString("F3") : "null")} " +
                $"gameObject='{__instance.gameObject.name}' active={__instance.gameObject.activeInHierarchy}",
                Main.CoopLocal);
        }

        PrayerUse prayerCast = p2.PrayerCast;
        bool isUsing = prayerCast != null && prayerCast.IsUsingAbility;
        bool useStocksOfHealth = Core.PenitenceManager.UseStocksOfHealth;
        ((GameObject)NormalPrayerInUseField.GetValue(__instance)).SetActive(isUsing && !useStocksOfHealth);
        ((GameObject)Pe02PrayerInUseField.GetValue(__instance)).SetActive(isUsing && useStocksOfHealth);

        float castFillAmount = isUsing ? 1f - prayerCast.GetPercentTimeCasting() : 0f;
        ((Image)PrayerTimerField.GetValue(__instance)).fillAmount = castFillAmount;

        // Round 40 fix: decompiled the REAL Update() body with ICSharpCode.Decompiler (actual C#,
        // not raw IL) and it does NOT gate CalculateBarSize/CalculateFillsBars/CalculateMarks/
        // CalculateNotEnough behind "did barTarget change" the way round 41's version (and this
        // Prefix, until now) assumed - vanilla calls all four UNCONDITIONALLY every single Update()
        // tick. The "if (lastValue != barTarget)" check only resets fillsIncrease/lastValue/
        // _timeElapsed (the direction/timer for the lerp animation) - it is NOT a call-gate. Putting
        // the four Calculate calls inside that gate (as before) meant CalculateFillsBars - which
        // does the actual per-frame Mathf.Lerp animation toward BarTarget - only ever ran ONCE per
        // change instead of continuously, so the fill visually took one lerp step and then froze
        // until the target changed again. This was the best explanation found for "no se actualiza
        // en tiempo real" via static analysis - **the user still reports it broken after this fix**
        // (round 42), so either this wasn't the whole story or something else is also wrong; the
        // enriched [FervourDiag] log above is there to pin down which from real data.
        float barTarget = p2.Stats.Fervour.CurrentMaxWithoutFactor > 0f
            ? p2.Stats.Fervour.Current / p2.Stats.Fervour.CurrentMaxWithoutFactor
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

        float maxFervour = p2.Stats.Fervour.CurrentMaxWithoutFactor;
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
    private static readonly FieldInfo LastBarWidthField = AccessTools.Field(typeof(PlayerFervour), "lastBarWidth");
    private static readonly FieldInfo BackgroundStartSizeField = AccessTools.Field(typeof(PlayerFervour), "backgroundStartSize");
    private static readonly FieldInfo EndFillSizeField = AccessTools.Field(typeof(PlayerFervour), "endFillSize");
    private static readonly FieldInfo BackgroundMidField = AccessTools.Field(typeof(PlayerFervour), "backgroundMid");
    private static readonly FieldInfo FillExactTransformField = AccessTools.Field(typeof(PlayerFervour), "fillExactTransform");
    private static readonly FieldInfo FillExactFullTransformField = AccessTools.Field(typeof(PlayerFervour), "fillExactFullTransform");
    private static readonly FieldInfo FillAnimableTransformField = AccessTools.Field(typeof(PlayerFervour), "fillAnimableTransform");
    private static readonly FieldInfo BackgroundField = AccessTools.Field(typeof(PlayerFervour), "background");
    private static readonly FieldInfo FillNotEnoughTransformField = AccessTools.Field(typeof(PlayerFervour), "fillNotEnoughTransform");

    private static bool Prefix(PlayerFervour __instance)
    {
        if (__instance != Player2FervourBar.Instance)
        {
            return true;
        }
        Penitent p2 = CoopLocal.Player2;
        if (p2 == null)
        {
            return false;
        }

        float maxWithoutFactor = p2.Stats.Fervour.CurrentMaxWithoutFactor;
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
    private static readonly FieldInfo TimeElapsedField = AccessTools.Field(typeof(PlayerFervour), "_timeElapsed");
    private static readonly FieldInfo FillsIncreaseField = AccessTools.Field(typeof(PlayerFervour), "fillsIncrease");
    private static readonly FieldInfo FillExactField = AccessTools.Field(typeof(PlayerFervour), "fillExact");
    private static readonly FieldInfo FillAnimableField = AccessTools.Field(typeof(PlayerFervour), "fillAnimable");
    private static readonly FieldInfo FillNotEnoughField = AccessTools.Field(typeof(PlayerFervour), "fillNotEnough");
    private static readonly FieldInfo AddAnimationCurveField = AccessTools.Field(typeof(PlayerFervour), "AddAnimationCurve");
    private static readonly FieldInfo LossAnimationCurveField = AccessTools.Field(typeof(PlayerFervour), "LossAnimationCurve");
    private static readonly FieldInfo FervourSparkField = AccessTools.Field(typeof(PlayerFervour), "fervourSpark");

    private static bool Prefix(PlayerFervour __instance)
    {
        if (__instance != Player2FervourBar.Instance)
        {
            return true;
        }
        Penitent p2 = CoopLocal.Player2;
        if (p2 == null)
        {
            return false;
        }

        float barTarget = p2.Stats.Fervour.Current / p2.Stats.Fervour.CurrentMaxWithoutFactor;
        float maxWithoutFactor = p2.Stats.Fervour.CurrentMaxWithoutFactor;
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
            // Round 40: real decompiled source sets this unconditionally at the end of the
            // fillsIncrease branch (both the "reached target" and "still lerping" paths), not only
            // inside the lerping else - the previous version left fillAnimable one step stale on
            // the exact frame the target is reached.
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

// Round 43: found the real cause of "reduce el fervor a 0 igual aparece en el HUD como a la
// mitad" - CalculateMarks() was the one remaining unredirected Calculate method (documented as a
// "tick marks" known gap since round 37/38, but it turns out to control far more than cosmetic
// tick marks). It computes `fillExactFull.fillAmount` - a *visible* fill layer rendered alongside
// fillExact/fillAnimable (both already correctly redirected) - straight from
// Core.Logic.Penitent.Stats.Fervour.Current (always P1). Since P1 and P2's Fervour *max* now
// matches after the stat-sync feature, the segment/tick-mark *positions* this method computes
// (based on CurrentMax) happen to come out identical either way - but the *fill ratio itself*
// (based on Current, which genuinely differs per player) was still showing P1's percentage
// regardless of P2's real value, which is exactly the "stuck at half" symptom reported. Full
// reimplementation, mirroring CalculateBarSize/CalculateFillsBars's own approach - every
// Core.Logic.Penitent read redirected to p2, private fields/method accessed via reflection.
[HarmonyPatch(typeof(PlayerFervour), "CalculateMarks")]
internal static class PlayerFervour_CalculateMarks_P2_Patch
{
    private static readonly FieldInfo FillExactFullField = AccessTools.Field(typeof(PlayerFervour), "fillExactFull");
    private static readonly FieldInfo EpsilonToShowLastBarField = AccessTools.Field(typeof(PlayerFervour), "epsilonToShowLastBar");
    private static readonly FieldInfo CurrentMarksField = AccessTools.Field(typeof(PlayerFervour), "currentMarks");
    private static readonly FieldInfo CurrentMarksSeparationField = AccessTools.Field(typeof(PlayerFervour), "currentMarksSeparation");
    private static readonly FieldInfo CurrentSegmentsFilledField = AccessTools.Field(typeof(PlayerFervour), "currentSegmentsFilled");
    private static readonly FieldInfo MarksParentField = AccessTools.Field(typeof(PlayerFervour), "marksParent");
    private static readonly FieldInfo BarMaskChildNameField = AccessTools.Field(typeof(PlayerFervour), "barMaskChildName");
    private static readonly FieldInfo BarBarChildNameField = AccessTools.Field(typeof(PlayerFervour), "barBarChildName");
    private static readonly FieldInfo BarAnimChildNameField = AccessTools.Field(typeof(PlayerFervour), "barAnimChildName");
    private static readonly FieldInfo BarAnimEndPositionField = AccessTools.Field(typeof(PlayerFervour), "barAnimEndPosition");
    private static readonly FieldInfo BarAnimMovementPerElapsedField = AccessTools.Field(typeof(PlayerFervour), "barAnimMovementPerElapsed");
    private static readonly FieldInfo BarAnimUpdatedElapsedField = AccessTools.Field(typeof(PlayerFervour), "barAnimUpdatedElapsed");
    private static readonly FieldInfo CurrentAnimPositionField = AccessTools.Field(typeof(PlayerFervour), "currentAnimPosition");
    private static readonly FieldInfo CurrentAnimElapsedField = AccessTools.Field(typeof(PlayerFervour), "currentAnimElapsed");
    private static readonly FieldInfo AnimsField = AccessTools.Field(typeof(PlayerFervour), "anims");
    private static readonly MethodInfo SetBarPositionMethod = AccessTools.Method(typeof(PlayerFervour), "SetBarPosition");

    private static bool Prefix(PlayerFervour __instance)
    {
        if (__instance != Player2FervourBar.Instance)
        {
            return true;
        }
        Penitent p2 = CoopLocal.Player2;
        if (p2 == null)
        {
            return false;
        }

        int num = 0;
        float num2 = 0f;
        Framework.Inventory.Prayer prayerInSlot = Core.InventoryManager.GetPrayerInSlot(0);
        int num3 = prayerInSlot != null ? prayerInSlot.fervourNeeded + (int)p2.Stats.PrayerCostAddition.Final : 0;
        Image fillExactFull = (Image)FillExactFullField.GetValue(__instance);
        if (num3 > 0)
        {
            num = (int)p2.Stats.Fervour.CurrentMax / num3;
            num2 = (int)p2.Stats.Fervour.Current / num3;
            fillExactFull.fillAmount = num2 * num3 / p2.Stats.Fervour.CurrentMaxWithoutFactor;
        }
        else
        {
            fillExactFull.fillAmount = 0f;
        }

        float epsilonToShowLastBar = (float)EpsilonToShowLastBarField.GetValue(__instance);
        bool showLastBar = p2.Stats.Fervour.CurrentMax - num3 * num > epsilonToShowLastBar;
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


