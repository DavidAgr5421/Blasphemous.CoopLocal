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

// Round 40: P2's potion (Flask) HUD - user reported it showing a static 4 potions (P1's own count,
// frozen at whatever it was the instant Player2FervourBar's wholesale "LeftPart" clone was made)
// instead of P2's real 2, and never decreasing on use. PlayerFlask rides along inside that same
// clone as an untouched duplicate (see Player2FervourBar.FlaskInstance's own comment) - decompiled
// via ICSharpCode.Decompiler (real C#, not raw IL) to get an exact reimplementation: RefreshFlask()
// hardcodes Core.Logic.Penitent in three reads (Stats.Flask, Stats.FlaskHealth.PermanetBonus,
// Stats.FlaskHealthUpgrade) - redirected to P2 here, called unconditionally every frame from
// Update() with no inlining-gate risk (unlike Fervour's BarTarget/Update() saga), so a direct
// Prefix on RefreshFlask() itself is sufficient.
[HarmonyPatch(typeof(PlayerFlask), "RefreshFlask")]
internal static class PlayerFlask_RefreshFlask_P2_Patch
{
    private static readonly FieldInfo FlasksField = AccessTools.Field(typeof(PlayerFlask), "flasks");
    private static readonly FieldInfo FlasksFullField = AccessTools.Field(typeof(PlayerFlask), "flasksFull");
    private static readonly FieldInfo FlasksEmptyField = AccessTools.Field(typeof(PlayerFlask), "flasksEmpty");
    private static readonly FieldInfo FlasksFullFervourField = AccessTools.Field(typeof(PlayerFlask), "flasksFullFervour");
    private static readonly FieldInfo CurrentFlaskNumberField = AccessTools.Field(typeof(PlayerFlask), "currentFlaskNumber");
    private static readonly FieldInfo CurrentFlaskFullField = AccessTools.Field(typeof(PlayerFlask), "currentFlaskFull");
    private static readonly FieldInfo CurrentFlaskLevelField = AccessTools.Field(typeof(PlayerFlask), "currentFlaskLevel");
    private static readonly FieldInfo CurrentFlaskIsFervourField = AccessTools.Field(typeof(PlayerFlask), "currentFlaskIsFervour");
    private static readonly FieldInfo SwordHeart06Field = AccessTools.Field(typeof(PlayerFlask), "swordHeart06");

    private static bool Prefix(PlayerFlask __instance)
    {
        if (__instance != Player2FervourBar.FlaskInstance)
        {
            return true;
        }
        Penitent p2 = CoopLocal.Player2;
        if (p2 == null)
        {
            return false;
        }

        List<Image> flasks = (List<Image>)FlasksField.GetValue(__instance);
        List<Sprite> flasksFull = (List<Sprite>)FlasksFullField.GetValue(__instance);
        List<Sprite> flasksEmpty = (List<Sprite>)FlasksEmptyField.GetValue(__instance);
        List<Sprite> flasksFullFervour = (List<Sprite>)FlasksFullFervourField.GetValue(__instance);
        if (flasks == null || flasks.Count == 0)
        {
            return false;
        }

        Framework.FrameworkCore.Attributes.Flask flask = p2.Stats.Flask;
        int level = (int)(p2.Stats.FlaskHealth.PermanetBonus / p2.Stats.FlaskHealthUpgrade);
        if (level > flasksEmpty.Count)
        {
            level = flasksEmpty.Count;
        }

        Framework.Inventory.Sword swordHeart06 = (Framework.Inventory.Sword)SwordHeart06Field.GetValue(__instance);
        if (swordHeart06 == null)
        {
            swordHeart06 = Core.InventoryManager.GetSword("HE06");
            SwordHeart06Field.SetValue(__instance, swordHeart06);
        }

        if (swordHeart06 != null && swordHeart06.IsEquiped)
        {
            for (int i = 0; i < flasks.Count; i++)
            {
                flasks[i].gameObject.SetActive(false);
            }
            flask.Current = 0f;
            return false;
        }

        float currentFlaskNumber = (float)CurrentFlaskNumberField.GetValue(__instance);
        float currentFlaskFull = (float)CurrentFlaskFullField.GetValue(__instance);
        float currentFlaskLevel = (float)CurrentFlaskLevelField.GetValue(__instance);
        bool currentFlaskIsFervour = (bool)CurrentFlaskIsFervourField.GetValue(__instance);

        if (currentFlaskNumber == flask.Final && currentFlaskFull == flask.Current && currentFlaskLevel == (float)level
            && flasks[0].gameObject.activeInHierarchy && currentFlaskIsFervour == Core.PenitenceManager.UseFervourFlasks)
        {
            return false;
        }

        CurrentFlaskIsFervourField.SetValue(__instance, Core.PenitenceManager.UseFervourFlasks);
        CurrentFlaskNumberField.SetValue(__instance, flask.Final);
        CurrentFlaskFullField.SetValue(__instance, flask.Current);
        CurrentFlaskLevelField.SetValue(__instance, (float)level);

        for (int j = 0; j < flasks.Count; j++)
        {
            if ((float)j < flask.Current)
            {
                flasks[j].sprite = Core.PenitenceManager.UseFervourFlasks ? flasksFullFervour[level] : flasksFull[level];
                flasks[j].gameObject.SetActive(true);
            }
            else if ((float)j < flask.Final)
            {
                flasks[j].sprite = flasksEmpty[level];
                flasks[j].gameObject.SetActive(true);
            }
            else
            {
                flasks[j].gameObject.SetActive(false);
            }
        }
        return false;
    }
}

// Round 39: the user asked to check whether currency ("Tears"/Purge) could be separated per
// player. Turned out to be much more tractable than first assessed: currency is stored as
// Core.Logic.Penitent.Stats.Purge - a VariableAttribute on EntityStats, the *exact* same
// per-instance mechanism Life and Fervour already use. P2 (a full Penitent clone) already has
// its own separate Stats.Purge, sitting unused - this is the same "wrong owner" bug class
// already fixed throughout this file all session, just not yet applied to currency.
//
// The catch: unlike Life/Fervour (touched from a handful of C# classes), every currency EARN in
// the entire game runs through one of four PlayMaker actions (TearsAddition, and the newer
// Playmaker2 Purge/PurgeAdd/PurgeSet - level-scripted, used by enemy drops, pickups, chests,
// everywhere) - decompiling all four confirms each one unconditionally reads/writes
// Core.Logic.Penitent.Stats.Purge with **no notion of "which player" caused it at all**. PlayMaker
// FSMs don't carry per-Penitent context the way a C# call site normally would, so there is no
// cheap way to determine "P2 specifically earned this one" the way Hit.AttackingEntity lets
// damage be attributed elsewhere in this file. Rather than leave P2's pool permanently empty
// (unusable) or invest in a much larger "track last damager per enemy" plumbing project just for
// this, both players are credited the *same* amount independently whenever any of these actions
// fire - two genuinely separate running totals, not a shared/split pool, which is what "no
// compartan monedas" asked for; it just means both earn from every source rather than only
// whoever specifically caused it. Revisit if the user wants strict per-causer attribution instead
// - that's a real feature, not a quick follow-up.
//
// Spending (shops/Alms) is NOT touched here - shop UI/dialogue is still P1-only in this mod (no
// P2 shop-interaction exists at all yet), so there's nothing to redirect on that side yet; P2's
// pool just accumulates for now.
[HarmonyPatch(typeof(Tools.PlayMaker.Action.TearsAddition), "OnEnter")]
internal static class TearsAddition_CreditPlayer2_Patch
{
    private static void Postfix(Tools.PlayMaker.Action.TearsAddition __instance)
    {
        Penitent p2 = CoopLocal.Player2;
        if (p2 == null)
        {
            return;
        }
        float delta = __instance.Tears != null ? __instance.Tears.Value : 0f;
        p2.Stats.Purge.Current = Mathf.Max(0f, p2.Stats.Purge.Current + delta);
    }
}

[HarmonyPatch(typeof(Tools.Playmaker2.Action.PurgeAdd), "OnEnter")]
internal static class PurgeAdd_CreditPlayer2_Patch
{
    private static void Postfix(Tools.Playmaker2.Action.PurgeAdd __instance)
    {
        Penitent p2 = CoopLocal.Player2;
        if (p2 == null)
        {
            return;
        }
        float delta = __instance.value != null ? __instance.value.Value : 0f;
        p2.Stats.Purge.Current = Mathf.Max(0f, p2.Stats.Purge.Current + delta);
    }
}

// PurgeSet is an absolute assignment (not a delta) - almost certainly used for rare story-level
// resets rather than routine pickups, so mirroring it to P2 as an absolute set too (rather than
// treating it like an add) keeps both players' pools consistent for whatever story moment this
// actually is.
[HarmonyPatch(typeof(Tools.Playmaker2.Action.PurgeSet), "OnEnter")]
internal static class PurgeSet_CreditPlayer2_Patch
{
    private static void Postfix(Tools.Playmaker2.Action.PurgeSet __instance)
    {
        Penitent p2 = CoopLocal.Player2;
        if (p2 == null)
        {
            return;
        }
        float value = __instance.value != null ? __instance.value.Value : 0f;
        p2.Stats.Purge.Current = Mathf.Max(0f, value);
    }
}

// Round 42: after three rounds of trying to clone-and-redirect various real PlayerPurgePoints
// widgets (shop popup, then GameplayWidget.purgePoints, then a source-cycling tool to compare all
// of them) the user decided cloning isn't worth it here - PlayerPurgePoints drags along baked-in
// animation/background machinery that keeps looking wrong for reasons that were never fully
// pinned down. Simpler and more reliable: a single plain UI.Text we own outright, showing P2's
// Purge value as a number, styled with the *real* game font read once off the actual combat-HUD
// counter (Core.UI.GameplayUI's private "purgePoints" field, found via the Mono.Cecil scan two
// rounds ago) rather than guessed - no clone, no redirect patch, no inherited animation/background
// quirks to fight.
internal static class Player2PurgePoints
{
    private static readonly FieldInfo GameplayWidgetPurgePointsField =
        AccessTools.Field(typeof(Gameplay.UI.Widgets.GameplayWidget), "purgePoints");
    private static readonly FieldInfo PurgePointsTextField = AccessTools.Field(typeof(PlayerPurgePoints), "text");

    // Round 45: final position, re-confirmed by the user via live Player2HudPositionTuner testing
    // (moved from the original bottom-left placement to sit with the rest of P2's HUD block).
    internal static Vector2 AnchoredPosition = new Vector2(-170f, -8f);

    // Round 44: scale multipliers for the text/icon, adjustable live via Player2HudPositionTuner's
    // "." / "-" keys - these two never had a Scale field before since they were created at native
    // size (unlike Health/Fervour's cloned widgets, which always applied a fixed 0.65 shrink).
    // Round 45: final values, confirmed by the user via live testing.
    internal static float TextScale = 1f;
    internal static float IconScale = 1f;

    // Round 43: the coin/tears icon that sits behind the real HUD's currency text - independently
    // positionable from the text itself via Player2HudPositionTuner's new CurrencyIcon target.
    // Round 45: final position, re-confirmed by the user via live tuning.
    internal static Vector2 IconAnchoredPosition = new Vector2(5f, -21f);

    private static GameObject textRoot;
    private static GameObject iconRoot;
    private static Text label;

    internal static RectTransform CloneRect => textRoot != null ? textRoot.GetComponent<RectTransform>() : null;
    internal static RectTransform IconRect => iconRoot != null ? iconRoot.GetComponent<RectTransform>() : null;

    // Round 56: see Player2HudFadeSync - same "clone doesn't hide with the vanilla fade" fix as
    // Player2HealthBar.SetVisible. Two separate GameObjects here (text + icon), both toggled.
    // Round 57: now an alpha fade via HudFade instead of a binary pop, one independent tween per
    // GameObject (they're siblings, not parent/child, so each needs its own CanvasGroup) - see
    // HealthHUD.cs's own SetVisible comment.
    internal static void SetVisible(bool visible, bool instant = false)
    {
        HudFade.SetVisible(textRoot, visible, instant);
        HudFade.SetVisible(iconRoot, visible, instant);
    }

    internal static void EnsureCreated(Penitent p2)
    {
        if (textRoot != null)
        {
            UnityEngine.Object.Destroy(textRoot);
            textRoot = null;
            label = null;
        }
        if (iconRoot != null)
        {
            UnityEngine.Object.Destroy(iconRoot);
            iconRoot = null;
        }

        Gameplay.UI.Widgets.GameplayWidget gameplayWidget = Core.UI != null ? Core.UI.GameplayUI : null;
        PlayerPurgePoints original = gameplayWidget != null
            ? (PlayerPurgePoints)GameplayWidgetPurgePointsField.GetValue(gameplayWidget)
            : null;
        Text originalText = original != null ? (Text)PurgePointsTextField.GetValue(original) : null;

        Canvas canvas = original != null ? original.GetComponentInParent<Canvas>() : null;
        while (canvas != null && canvas.transform.parent != null)
        {
            Canvas parentCanvas = canvas.transform.parent.GetComponentInParent<Canvas>();
            if (parentCanvas == null)
            {
                break;
            }
            canvas = parentCanvas;
        }
        Transform parent = canvas != null ? canvas.transform : (original != null ? original.transform.parent : null);
        if (parent == null)
        {
            DashParryDebugLog.Log("Player2PurgePoints.EnsureCreated: aborted - no Canvas parent found to attach the text to.");
            return;
        }

        // Round 43: the user asked to add the coin/tears icon sprite that sits behind the real
        // currency text. Looking for an Image sibling next to originalText's own GameObject
        // (logging every candidate sibling found, same technique proven earlier this session for
        // finding the HUD portrait) rather than guessing a hierarchy path blind.
        Sprite iconSprite = null;
        Color iconColor = Color.white;
        if (originalText != null && originalText.transform.parent != null)
        {
            Transform textParent = originalText.transform.parent;
            System.Text.StringBuilder siblingLog = new System.Text.StringBuilder();
            for (int i = 0; i < textParent.childCount; i++)
            {
                Transform child = textParent.GetChild(i);
                Image childImage = child.GetComponent<Image>();
                siblingLog.Append($"[{i}] '{child.name}' hasImage={childImage != null} ");
                if (childImage != null && childImage.sprite != null && child.gameObject != originalText.gameObject && iconSprite == null)
                {
                    iconSprite = childImage.sprite;
                    iconColor = childImage.color;
                }
            }
            DashParryDebugLog.Log($"Player2PurgePoints.EnsureCreated: '{textParent.name}' children (looking for the coin/tears icon): {siblingLog}");
        }

        // Icon created FIRST so the text (created after) ends up as the later sibling and renders
        // on top - the same "later sibling wins" rule this file's Z-order fixes already rely on.
        if (iconSprite != null)
        {
            GameObject iconObject = new GameObject("Player2PurgePointsIcon");
            iconObject.transform.SetParent(parent, worldPositionStays: false);
            iconRoot = iconObject;

            RectTransform iconRect = iconObject.AddComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0f, 0f);
            iconRect.anchorMax = new Vector2(0f, 0f);
            iconRect.pivot = new Vector2(0f, 0f);
            iconRect.sizeDelta = new Vector2(iconSprite.rect.width, iconSprite.rect.height);
            iconRect.anchoredPosition = IconAnchoredPosition;

            Image icon = iconObject.AddComponent<Image>();
            icon.sprite = iconSprite;
            icon.color = iconColor;
            icon.SetNativeSize();
            iconRect.localScale = Vector3.one * IconScale;
        }
        else
        {
            DashParryDebugLog.Log("Player2PurgePoints.EnsureCreated: no coin/tears icon sprite found next to the real currency text.");
        }

        GameObject textObject = new GameObject("Player2PurgePointsText");
        textObject.transform.SetParent(parent, worldPositionStays: false);
        textRoot = textObject;

        RectTransform rect = textObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 0f);
        rect.pivot = new Vector2(0f, 0f);
        rect.sizeDelta = new Vector2(220f, 60f);
        rect.anchoredPosition = AnchoredPosition;
        rect.localScale = Vector3.one * TextScale;

        label = textObject.AddComponent<Text>();
        if (originalText != null && originalText.font != null)
        {
            label.font = originalText.font;
            label.fontSize = originalText.fontSize;
            label.fontStyle = originalText.fontStyle;
            label.color = originalText.color;
            label.material = originalText.font.material;
            label.alignment = originalText.alignment;
        }
        else
        {
            label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.fontSize = 28;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleLeft;
        }
        label.text = "0";

        // Round 57: start both roots hidden (alpha 0, still active) - see Player2HealthBar.
        // EnsureCreated's own comment. PrepareHidden no-ops on a null root, so this is safe even
        // when no icon sprite was found above.
        HudFade.PrepareHidden(textRoot);
        HudFade.PrepareHidden(iconRoot);

        DashParryDebugLog.Log(
            $"Player2PurgePoints.EnsureCreated: custom text created, foundRealFont={(originalText != null && originalText.font != null)}, foundIcon={iconSprite != null}");
    }

    // Called every frame from Player2Input.Tick() - just a number display, no animation/inlining
    // risk to worry about, unlike everything else this session.
    internal static void Tick()
    {
        if (label == null)
        {
            return;
        }
        Penitent p2 = CoopLocal.Player2;
        if (p2 == null)
        {
            return;
        }
        label.text = Mathf.FloorToInt(p2.Stats.Purge.Current).ToString();
    }
}


