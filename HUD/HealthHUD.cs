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

// Second HUD health bar for P2, per the user's request ("reutilizar el hud de P1 y ponerlo abajo
// con un tamaño reducido"). Gameplay.UI.Others.UIGameLogic.PlayerHealth is a single HUD widget
// hardcoded to read Core.Logic.Penitent - there's no per-Penitent instancing built into it - so
// the second bar has to be a real runtime clone of the same GameObject (Unity's Instantiate()
// correctly remaps a cloned hierarchy's own internal SerializeField references - health/loss
// Image, backgroundMid/backgroundFillTransform RectTransform - to point at the clone's own
// children, not the original's), then redirected via the patches below wherever it reads
// Core.Logic.Penitent. CalculateLossBar()/CalculateHealthBar() aren't patched directly - both
// only depend on BarTarget (redirected below) and this instance's own Image fields (already
// correctly re-pointed by Instantiate), so they work correctly through the clone unmodified.
//
// Positioning: anchored to the top-right corner of whatever Canvas the original bar lives in
// The top-right-corner attempt anchored the clone relative to original.transform.parent directly
// ("Health Bar") - if that's a small sub-container rather than the actual screen-sized Canvas,
// anchoring to its own (1,1) corner lands wherever that container happens to sit, not the screen's
// corner - which is almost certainly why it showed up far to one side instead. Now walks up to the
// outermost Canvas ancestor and parents the clone there, then centers it on screen for now (per
// the user's own suggestion) purely to visually confirm the clone mechanism itself works before
// worrying about a less obtrusive final position.
internal static class Player2HealthBar
{
    // Round 44: was a const - promoted to a mutable field so Player2HudPositionTuner's "." / "-"
    // scale keys can adjust it live.
    internal static float Scale = 0.65f;

    // Round 45: final position, re-confirmed by the user via live Player2HudPositionTuner testing.
    internal static Vector2 AnchoredPosition = new Vector2(-119f, -20f);

    private static readonly MethodInfo OnPenitentReadyMethod = AccessTools.Method(typeof(PlayerHealth), "OnPenitentReady");

    internal static PlayerHealth Instance { get; private set; }
    internal static RectTransform CloneRect => instanceRoot != null ? instanceRoot.GetComponent<RectTransform>() : null;

    // Round 41: user reported Health not visually rendering on top of Fervour's own group
    // (which drags in the whole "LeftPart" portrait/frame - see Player2FervourBar's class
    // comment). Unity UI renders later siblings on top of earlier ones, and Fervour is created
    // *after* Health in CoopLocal.OnPlayerSpawn, so Health's smaller clone was sitting behind
    // Fervour's larger one regardless of anchored position. Called from CoopLocal.cs after all
    // three P2 HUD clones exist (not from inside Health's own EnsureCreated, which runs before
    // Fervour is even created yet and so can't fix this from within itself).
    internal static void BringToFront()
    {
        if (instanceRoot != null)
        {
            instanceRoot.transform.SetAsLastSibling();
        }
    }

    // Round 56: see Player2HudFadeSync's own class comment - P2's clone has no equivalent to
    // P1's "sits behind the vanilla load/fade overlay" occlusion, so it stays visible straight
    // through a room-transition's fade to black unless something explicitly hides it.
    // Round 57: was a plain SetActive pop - now an alpha fade via HudFade, matching the vanilla
    // screen fade's own look/timing. `instant` is only used by Player2HudFadeSync.
    // ApplyCurrentFadeState's defensive correction - see HudFade.SetVisible's own comment.
    internal static void SetVisible(bool visible, bool instant = false)
    {
        HudFade.SetVisible(instanceRoot, visible, instant);
    }

    // Cached on first use and never looked up again. Object.Destroy() only *marks* a GameObject
    // for destruction - the real removal happens at the end of the current frame - so calling
    // FindObjectOfType<PlayerHealth>() again right after destroying the previous clone (same
    // synchronous call, same frame) would still find that not-yet-actually-gone clone, since at
    // that instant there are legitimately two PlayerHealth components in the scene and nothing
    // besides object identity tells them apart. Confirmed exactly this way in the field: the
    // second and third respawns each cloned from the *previous* P2 clone instead of P1's real
    // bar, compounding the Offset/Scale adjustment every time (position drifting down another 40
    // units and shrinking another 0.65x per respawn) until it was scaled down and pushed off
    // enough to be effectively invisible. The real original bar is a stable, persistent UI
    // element that's never destroyed - so finding it once, ever, and reusing that same reference
    // for every later respawn is both correct and simpler than trying to filter it out by name.
    private static PlayerHealth originalCache;

    // The clone's root is now "Health Bar" (the whole decorated container - see EnsureCreated),
    // not the "Bar" sub-object PlayerHealth itself lives on, so Instance.gameObject alone is no
    // longer the right thing to destroy on the next respawn - that would only remove the inner
    // "Bar" and leave the outer "Health Bar" wrapper (and any decorative siblings) orphaned in the
    // scene forever. Tracked separately instead of trying to derive it from Instance each time.
    private static GameObject instanceRoot;

    private static void LogChildren(string label, Transform parent)
    {
        System.Text.StringBuilder log = new System.Text.StringBuilder();
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            Graphic graphic = child.GetComponent<Graphic>();
            log.Append($"[{i}] '{child.name}' active={child.gameObject.activeSelf} hasGraphic={graphic != null} ");
        }
        DashParryDebugLog.Log($"Player2HealthBar.EnsureCreated: {label}: {log}");
    }

    internal static void EnsureCreated(Penitent p2)
    {
        if (instanceRoot != null)
        {
            UnityEngine.Object.Destroy(instanceRoot);
            instanceRoot = null;
            Instance = null;
        }

        if (originalCache == null)
        {
            originalCache = UnityEngine.Object.FindObjectOfType<PlayerHealth>();
        }
        PlayerHealth original = originalCache;
        if (original == null || p2 == null)
        {
            DashParryDebugLog.Log($"Player2HealthBar.EnsureCreated: aborted - original PlayerHealth found={original != null}, p2 found={p2 != null}");
            return;
        }

        // Anchoring/insetting only means "screen corner" if the parent itself is the full-screen
        // Canvas. The previous attempt anchored the clone to (1,1) of original.transform.parent
        // directly ("Health Bar") - if that's actually a small sub-container hugging one part of
        // the HUD rather than the screen-sized Canvas itself, (1,1) means "top-right of that small
        // container", which could visually land almost anywhere, including off to one side - which
        // is what the user saw. Walking up to the outermost Canvas ancestor and parenting the
        // clone there instead makes the anchor genuinely relative to the whole screen.
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

        // Round 32: the user reported the clone looks like "a piece of the real sprite", not a
        // complete bar - cloning only original.gameObject ("Bar", the PlayerHealth component's own
        // GameObject) was the suspect, since a polished HUD bar is often composed of an ornate
        // frame/border as a *sibling* decoration next to the bare fill-mechanism object, not a
        // child of it - "Bar" holds the fill Images (health/loss/background* are all its own
        // children, per PlayerHealth's own fields) but the decorative frame around it could easily
        // live one level up, as another child of "Health Bar" alongside "Bar". Logging every
        // sibling under "Health Bar" (name/active/whether it renders anything) to see what's
        // actually there, and cloning that whole parent container instead of just "Bar" so nothing
        // decorative gets left behind.
        Transform originalParent = original.transform.parent;
        if (originalParent != null)
        {
            LogChildren("'Health Bar' children", originalParent);

            // Round 33: "Health Bar" itself only has 'Health Fills' and 'Bar' as children - no
            // frame/icon in there. The user confirmed the clone shows *some* bar but still lacks
            // the decorative border and the Penitent portrait icon P1's real HUD shows alongside
            // it - meaning those live even further out, as *siblings of "Health Bar" itself* under
            // whatever groups the whole HUD widget (icon + bar + frame) together, not inside it.
            // Logging one level further up to find them before guessing what to clone next.
            Transform grandparent = originalParent.parent;
            if (grandparent != null)
            {
                LogChildren("'Health Bar' siblings (under '" + grandparent.name + "')", grandparent);
            }
        }
        GameObject sourceToClone = originalParent != null ? originalParent.gameObject : original.gameObject;

        GameObject cloneObject = UnityEngine.Object.Instantiate(sourceToClone, cloneParent);
        cloneObject.name = "PlayerHealth_P2";
        instanceRoot = cloneObject;
        Instance = cloneObject.GetComponentInChildren<PlayerHealth>();

        // Round 57: start hidden (alpha 0, still active) instead of Instantiate's default
        // fully-visible pop - Player2HudFadeSync.ApplyCurrentFadeState (called right after all
        // three P2 HUD clones exist, from CoopLocal.OnPlayerSpawn) decides whether to fade this
        // in immediately or leave it at 0 until the next screen-fade-in.
        HudFade.PrepareHidden(instanceRoot);

        RectTransform originalRect = (originalParent != null ? originalParent : original.transform) as RectTransform;
        RectTransform rect = cloneObject.GetComponent<RectTransform>();
        DashParryDebugLog.Log(
            $"Player2HealthBar.EnsureCreated: cloned from '{sourceToClone.name}' (parent={(original.transform.parent != null ? original.transform.parent.name : "none")}, " +
            $"canvasRoot={(canvas != null ? canvas.gameObject.name : "not found")}, active={sourceToClone.activeInHierarchy}, componentEnabled={original.enabled}, " +
            $"original anchorMin={originalRect?.anchorMin} anchorMax={originalRect?.anchorMax} pivot={originalRect?.pivot} anchoredPosition={originalRect?.anchoredPosition} sizeDelta={originalRect?.sizeDelta}) " +
            $"-> clone active={cloneObject.activeInHierarchy}, hasRectTransform={rect != null}, foundPlayerHealth={Instance != null}" +
            (rect != null ? $", anchoredPosition={rect.anchoredPosition}, localScale={rect.localScale}" : ""));
        if (rect != null)
        {
            // Round 38: visual now confirmed complete (portrait/frame/bar all show correctly) -
            // moved to the bottom-right corner as originally asked, aligned with
            // Player2FervourBar below (same X inset, Fervour stacked directly under Health by
            // FervourVerticalOffset). Pivot (1,0) = anchoredPosition is measured from this
            // object's own bottom-right corner, so a negative X / positive Y inset pulls it away
            // from the screen's actual corner instead of clipping off it - the exact inset values
            // are a best-effort guess (this environment can't screenshot the live HUD to check),
            // so this will likely still need one more visual tuning pass.
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = AnchoredPosition;
            rect.localScale *= Scale;
            DashParryDebugLog.Log($"Player2HealthBar.EnsureCreated: positioned clone at bottom-right, anchoredPosition={rect.anchoredPosition}, localScale={rect.localScale}");
        }

        // The clone's own Awake() already subscribed its own OnPenitentReady to the shared
        // SpawnManager.OnPlayerSpawn static event (the same one CoopLocal itself hooks) - but
        // that event has already finished firing for this spawn by the time we get here (we're
        // running from inside CoopLocal's own handler for it), so the clone would otherwise sit
        // completely unwired until the *next* time P1 respawns. Call it once, right now,
        // ourselves instead - PlayerHealth_OnPenitentReady_P2_Patch redirects the argument to P2
        // for this specific instance regardless of what gets passed in, including this call.
        OnPenitentReadyMethod.Invoke(Instance, new object[] { p2 });
    }
}

[HarmonyPatch(typeof(PlayerHealth), "OnPenitentReady")]
internal static class PlayerHealth_OnPenitentReady_P2_Patch
{
    private static void Prefix(PlayerHealth __instance, ref Penitent penitent)
    {
        if (__instance == Player2HealthBar.Instance && CoopLocal.Player2 != null)
        {
            penitent = CoopLocal.Player2;
        }
    }
}

// Root cause of "the clone shows up centered but never displays real info" (see Modding/NOTES.md):
// BarTarget is a small *private* property, and CalculateLossBar()/CalculateHealthBar() call it
// internally, from methods in the exact same class, on `this`. That's precisely the shape the
// Mono JIT is most likely to inline directly into the caller's compiled code - the same "trivial
// property inlines past a Harmony Postfix on its getter" gotcha already found once in this file
// for PlatformCharacterInput.Blocked (see BlockerOverrideHelper's comment). Patching the getter
// here still matters for any genuinely external caller, but for the clone's own Update() loop -
// which only ever calls CalculateLossBar()/CalculateHealthBar() on itself - it likely never goes
// through this patched getter at all, so the P2 clone's fill Images kept lerping toward P1's
// BarTarget (Core.Logic.Penitent's own ratio) instead of P2's. Left in place for any external
// caller, but PlayerHealth_CalculateLossBar_P2_Patch/PlayerHealth_CalculateHealthBar_P2_Patch
// below are the actual fix, using the same reimplement-the-caller approach already proven for
// CalculateHealthBarSize() just below this.
[HarmonyPatch(typeof(PlayerHealth), "BarTarget", MethodType.Getter)]
internal static class PlayerHealth_BarTarget_P2_Patch
{
    private static string lastLoggedState;

    private static void Postfix(PlayerHealth __instance, ref float __result)
    {
        if (__instance != Player2HealthBar.Instance)
        {
            return;
        }
        Penitent p2 = CoopLocal.Player2;
        __result = (p2 != null) ? (p2.Stats.Life.Current / p2.Stats.Life.Final) : 0f;

        // Diagnostic for "the clone shows up but doesn't display real info" - if Life.Final is 0
        // or NaN at this point, __result itself becomes 0/NaN/Infinity, which would make the fill
        // Images collapse to nothing even though the bar's background/frame sprite is still
        // visible - looking exactly like "a sprite with no info" instead of a missing bar.
        string state = p2 != null ? $"Life.Current={p2.Stats.Life.Current:F1} Life.Final={p2.Stats.Life.Final:F1} BarTarget={__result:F3}" : "p2 is null";
        if (state != lastLoggedState)
        {
            lastLoggedState = state;
            DashParryDebugLog.Log($"Player2HealthBar.BarTarget: {state}");
        }
    }
}

// CalculateHealthBarSize() reads Core.Logic.Penitent as a bare local variable (not exposed via
// any field), so it can't be redirected with a simple Postfix the way BarTarget's getter is -
// reimplemented instead, substituting P2 for Core.Logic.Penitent, against the clone's own private
// fields via reflection.
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
        if (__instance != Player2HealthBar.Instance)
        {
            return true;
        }
        Penitent p2 = CoopLocal.Player2;
        if (p2 == null)
        {
            return false;
        }

        float final = p2.Stats.Life.Final;
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
        DashParryDebugLog.Log(
            $"Player2HealthBar.CalculateHealthBarSize: final={final:F1} backgroundStartSize={backgroundStartSize:F1} endFillSize={endFillSize:F1} " +
            $"-> backgroundMid.sizeDelta={backgroundMid.sizeDelta} healthTransform.sizeDelta={healthTransform.sizeDelta}");
        return false;
    }
}

// The actual fix for the clone showing a frame but no fill (see the comment on
// PlayerHealth_BarTarget_P2_Patch above for why patching the getter alone doesn't reach these two
// callers): reimplemented against the clone's own private fields via reflection, computing the
// target ratio from P2's own stats directly instead of going through the (likely-inlined) private
// BarTarget property at all - same approach already proven for CalculateHealthBarSize().
[HarmonyPatch(typeof(PlayerHealth), "CalculateLossBar")]
internal static class PlayerHealth_CalculateLossBar_P2_Patch
{
    private static readonly FieldInfo LossField = AccessTools.Field(typeof(PlayerHealth), "loss");
    private static readonly FieldInfo CurveField = AccessTools.Field(typeof(PlayerHealth), "HealthLossAnimationCurve");
    private static readonly FieldInfo DamageTimeElapsedField = AccessTools.Field(typeof(PlayerHealth), "_damageTimeElapsed");

    private static bool Prefix(PlayerHealth __instance)
    {
        if (__instance != Player2HealthBar.Instance)
        {
            return true;
        }
        Penitent p2 = CoopLocal.Player2;
        if (p2 == null)
        {
            return false;
        }

        float target = p2.Stats.Life.Current / p2.Stats.Life.Final;
        Image loss = (Image)LossField.GetValue(__instance);
        if (!Mathf.Approximately(loss.fillAmount, target))
        {
            float elapsed = (float)DamageTimeElapsedField.GetValue(__instance) + Time.deltaTime;
            DamageTimeElapsedField.SetValue(__instance, elapsed);
            AnimationCurve curve = (AnimationCurve)CurveField.GetValue(__instance);
            loss.fillAmount = Mathf.Lerp(loss.fillAmount, target, curve.Evaluate(elapsed));
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

    // Diagnostic for the round-30 report "still looks like one shared bar, P1's, drops when P2 is
    // hit" - if this Prefix is genuinely running and reading P2's own numbers (which it should,
    // being a direct Prefix on the real method Update() calls, not a getter that could be
    // JIT-inlined past), the log below should show *this instance*'s (the clone's) target ratio
    // tracking P2's own Stats.Life independently of whatever P1's real bar is doing. If this line
    // never appears at all, the Prefix isn't running (return-true path / __instance mismatch,
    // worth knowing directly instead of guessing further).
    private static float lastLoggedTarget = -1f;

    private static bool Prefix(PlayerHealth __instance)
    {
        if (__instance != Player2HealthBar.Instance)
        {
            return true;
        }
        Penitent p2 = CoopLocal.Player2;
        if (p2 == null)
        {
            return false;
        }

        float target = p2.Stats.Life.Current / p2.Stats.Life.Final;
        if (!Mathf.Approximately(target, lastLoggedTarget))
        {
            lastLoggedTarget = target;
            DashParryDebugLog.Log(
                $"Player2HealthBar.CalculateHealthBar: instance={__instance.GetInstanceID()} P2.Life.Current={p2.Stats.Life.Current:F1} " +
                $"P2.Life.Final={p2.Stats.Life.Final:F1} target={target:F3} (frame {Time.frameCount})");
        }

        Image health = (Image)HealthField.GetValue(__instance);
        if (!Mathf.Approximately(health.fillAmount, target))
        {
            float elapsed = (float)DamageTimeElapsedField.GetValue(__instance) + Time.deltaTime;
            DamageTimeElapsedField.SetValue(__instance, elapsed);
            float speed = (float)SpeedField.GetValue(__instance);
            health.fillAmount = Mathf.Lerp(health.fillAmount, target, elapsed * speed);
        }
        return false;
    }
}


