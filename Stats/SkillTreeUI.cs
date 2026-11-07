using Gameplay.GameControllers.Penitent;
using Framework.Managers;
using Gameplay.UI.Others.MenuLogic;
using Gameplay.UI.Others.UIGameLogic;
using HarmonyLib;
using Rewired;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace Blasphemous.CoopLocal;

// Fase 1 / Ronda 88 — Skill Tree UI sombra P2 (pestaña Habilidades y Santuario Mea Culpa).
// Sincroniza visualmente nodos desbloqueados, nivel de Mea Culpa, lágrimas de enmienda (Purge),
// botón de compra (buttonUnlock) y desbloqueo de habilidades propio de P2.
internal static class SkillTreeUIHelper
{
    internal static readonly FieldInfo UnlockMaskField = AccessTools.Field(typeof(NewInventory_LayoutSkill), "unlockMask");
    internal static readonly FieldInfo TimePressingField = AccessTools.Field(typeof(NewInventory_LayoutSkill), "timePressingUnlock");
    internal static readonly FieldInfo TimeToUnlockField = AccessTools.Field(typeof(NewInventory_LayoutSkill), "timeToUnlockSKill");
    internal static readonly FieldInfo CachedSkillsField = AccessTools.Field(typeof(NewInventory_LayoutSkill), "cachedSkills");
    internal static readonly FieldInfo CurrentSelectedField = AccessTools.Field(typeof(NewInventory_LayoutSkill), "currentSelected");
    internal static readonly FieldInfo InEditModeField = AccessTools.Field(typeof(NewInventory_LayoutSkill), "inEditMode");
    internal static readonly FieldInfo PurgeControlField = AccessTools.Field(typeof(NewInventory_LayoutSkill), "purgeControl");
    internal static readonly FieldInfo PurgeTextField = AccessTools.Field(typeof(PlayerPurgePoints), "text");
    internal static readonly FieldInfo MaxTierField = AccessTools.Field(typeof(NewInventory_LayoutSkill), "maxTier");
    internal static readonly FieldInfo RewiredField = AccessTools.Field(typeof(NewInventory_LayoutSkill), "rewired");
    internal static readonly FieldInfo LoadingFxField = AccessTools.Field(typeof(NewInventory_LayoutSkill), "_loadingPurchaseFxEvent");
    internal static readonly MethodInfo PlayLoadingFxMethod = AccessTools.Method(typeof(NewInventory_LayoutSkill), "PlayLoadingPurchaseFx");
    internal static readonly MethodInfo StopLoadingFxMethod = AccessTools.Method(typeof(NewInventory_LayoutSkill), "StopLoadingPurchaseFx");
    internal static readonly FieldInfo SoundUnlockField = AccessTools.Field(typeof(NewInventory_LayoutSkill), "soundUnlockSkill");
    internal static readonly MethodInfo UpdateAllSkillsMethod = AccessTools.Method(typeof(NewInventory_LayoutSkill), "UpdateAllSKills");
    internal static readonly MethodInfo SelectSkillMethod = AccessTools.Method(typeof(NewInventory_LayoutSkill), "SelectSkill");
    internal static readonly FieldInfo DescriptionButtonUnlockField = AccessTools.Field(typeof(NewInventory_Description), "buttonUnlock");

    internal static bool CanUnlockNoPointsP2(string skillId)
    {
        if (string.IsNullOrEmpty(skillId) || Core.SkillManager == null) return false;
        var skill = Core.SkillManager.GetSkill(skillId);
        int activeIdx = Player2MenuView.ActivePlayerIndex;
        if (skill == null || Player2SkillManager.IsUnlocked(activeIdx, skillId)) return false;
        Penitent activePenitent = CoopPlayerRegistry.GetPenitent(activeIdx);
        float mea = activePenitent != null ? activePenitent.Stats.MeaCulpa.Final : 0f;
        if (mea < skill.tier) return false;
        string parent = skill.GetParentSkill();
        if (!string.IsNullOrEmpty(parent) && !Player2SkillManager.IsUnlocked(activeIdx, parent)) return false;
        return true;
    }

    internal static bool CanUnlockWithPointsP2(string skillId)
    {
        if (!CanUnlockNoPointsP2(skillId)) return false;
        var skill = Core.SkillManager.GetSkill(skillId);
        int activeIdx = Player2MenuView.ActivePlayerIndex;
        Penitent activePenitent = CoopPlayerRegistry.GetPenitent(activeIdx);
        if (skill == null || activePenitent == null) return false;
        return activePenitent.Stats.Purge.Current >= skill.cost;
    }

    internal static void RefreshP2PurgeDisplay(NewInventory_LayoutSkill instance)
    {
        int activeIdx = Player2MenuView.ActivePlayerIndex;
        Penitent activePenitent = CoopPlayerRegistry.GetPenitent(activeIdx);
        if (instance == null || activePenitent == null || PurgeControlField == null || PurgeTextField == null)
        {
            return;
        }
        var purgeControl = PurgeControlField.GetValue(instance) as PlayerPurgePoints;
        if (purgeControl != null)
        {
            var purgeText = PurgeTextField.GetValue(purgeControl) as Text;
            if (purgeText != null)
            {
                purgeText.text = ((int)activePenitent.Stats.Purge.Current).ToString();
            }
        }
    }
}

[HarmonyPatch(typeof(NewInventory_LayoutSkill), "ShowLayout")]
internal static class SkillLayout_ShowLayout_P2_Patch
{
    private static void Postfix(NewInventory_LayoutSkill __instance, NewInventoryWidget.TabType tabType, bool editMode)
    {
        if (tabType != NewInventoryWidget.TabType.Abilities)
        {
            return;
        }

        SwordElementalUpgrades.EnsureSkillTreeNodesInjected(__instance);

        var maxTier = SkillTreeUIHelper.MaxTierField?.GetValue(__instance) as Text;
        if (maxTier != null)
        {
            int activeIdx = Player2MenuView.ActivePlayerIndex;
            Penitent activePenitent = CoopPlayerRegistry.GetPenitent(activeIdx);
            float mea = Player2MenuView.IsSkillP2View && activePenitent != null
                ? activePenitent.Stats.MeaCulpa.Final
                : Core.SkillManager.GetCurrentMeaCulpa();
            maxTier.text = $"{Player2MenuView.CurrentBracketTag} {(int)mea}";
        }

        if (Player2MenuView.IsSkillP2View)
        {
            SkillTreeUIHelper.RefreshP2PurgeDisplay(__instance);
            SkillTreeUIHelper.UpdateAllSkillsMethod?.Invoke(__instance, null);
            int curSelected = (int)SkillTreeUIHelper.CurrentSelectedField.GetValue(__instance);
            if (curSelected >= 0)
            {
                SkillTreeUIHelper.SelectSkillMethod?.Invoke(__instance, new object[] { curSelected, false });
            }
        }
    }
}

[HarmonyPatch(typeof(NewInventory_Description), "SetKill")]
internal static class SkillDescription_SetKill_P2_Patch
{
    private static void Postfix(NewInventory_Description __instance, string skillId, bool editmode)
    {
        SwordElementalUpgrades.ApplyDescriptionTextScale(__instance);

        if (!Player2MenuView.IsSkillP2View) return;
        var buttonUnlock = SkillTreeUIHelper.DescriptionButtonUnlockField?.GetValue(__instance) as GameObject;
        if (buttonUnlock != null)
        {
            buttonUnlock.SetActive(editmode && SkillTreeUIHelper.CanUnlockWithPointsP2(skillId));
        }
    }
}

[HarmonyPatch(typeof(NewInventory_Description), "SetObject")]
internal static class SkillDescription_SetObject_TextScale_Patch
{
    private static void Postfix(NewInventory_Description __instance)
    {
        SwordElementalUpgrades.ApplyDescriptionTextScale(__instance);
    }
}

[HarmonyPatch(typeof(NewInventory_LayoutSkill), "Update")]
internal static class SkillLayout_Update_P2_Patch
{
    private static bool Prefix(NewInventory_LayoutSkill __instance)
    {
        SwordElementalUpgrades.HandleSkillTreeShortcuts(__instance);

        if (!Player2MenuView.IsSkillP2View) return true; // P1 vanilla intacto

        bool inEditMode = (bool)SkillTreeUIHelper.InEditModeField.GetValue(__instance);
        if (!inEditMode) return false;

        var cachedSkills = SkillTreeUIHelper.CachedSkillsField.GetValue(__instance) as List<NewInventory_Skill>;
        int currentSelected = (int)SkillTreeUIHelper.CurrentSelectedField.GetValue(__instance);
        if (cachedSkills == null || currentSelected < 0 || currentSelected >= cachedSkills.Count) return false;

        string skillId = cachedSkills[currentSelected].GetSkillId();
        bool canUnlock = SkillTreeUIHelper.CanUnlockWithPointsP2(skillId);

        float timePressing = (float)SkillTreeUIHelper.TimePressingField.GetValue(__instance);
        float timeToUnlock = (float)SkillTreeUIHelper.TimeToUnlockField.GetValue(__instance);
        var unlockMask = SkillTreeUIHelper.UnlockMaskField.GetValue(__instance) as Image;
        Player rewired = SkillTreeUIHelper.RewiredField.GetValue(__instance) as Player;
        if (rewired == null)
        {
            rewired = ReInput.players.GetPlayer(0);
            SkillTreeUIHelper.RewiredField.SetValue(__instance, rewired);
        }

        if (canUnlock)
        {
            int activeIdx = Player2MenuView.ActivePlayerIndex;
            Penitent activePenitent = CoopPlayerRegistry.GetPenitent(activeIdx);
            bool guestAttackHeld = Player2Input.GetAttackHeld(activeIdx);
            bool holdingBuy = (rewired != null && rewired.GetButton(52)) || guestAttackHeld;
            if (holdingBuy)
            {
                timePressing += Time.unscaledDeltaTime;
                SkillTreeUIHelper.TimePressingField.SetValue(__instance, timePressing);
                var ev = (FMOD.Studio.EventInstance)SkillTreeUIHelper.LoadingFxField.GetValue(__instance);
                SkillTreeUIHelper.PlayLoadingFxMethod?.Invoke(__instance, new object[] { ev });
                if (timePressing >= timeToUnlock)
                {
                    SkillTreeUIHelper.TimePressingField.SetValue(__instance, 0f);
                    var skill = Core.SkillManager.GetSkill(skillId);
                    float cost = skill != null ? skill.cost : 0f;
                    if (activePenitent != null && activePenitent.Stats.Purge.Current >= cost)
                    {
                        activePenitent.Stats.Purge.Current -= cost;
                    }
                    Player2SkillManager.SetUnlocked(activeIdx, skillId, true);
                    Player2SkillManager.Persist();
                    if (SwordElementalUpgrades.IsElementalSkill(skillId))
                    {
                        SwordElementalUpgrades.GrantMatchingHeartOnUnlock(skillId, isPlayer2: true, playerIndex: activeIdx);
                    }
                    if (activePenitent != null)
                    {
                        Player2StatsSync.SaveCurrentVitals(activePenitent, activeIdx);
                    }
                    SkillTreeUIHelper.RefreshP2PurgeDisplay(__instance);
                    SkillTreeUIHelper.UpdateAllSkillsMethod?.Invoke(__instance, null);
                    SkillTreeUIHelper.SelectSkillMethod?.Invoke(__instance, new object[] { currentSelected, false });
                    string snd = (string)SkillTreeUIHelper.SoundUnlockField.GetValue(__instance);
                    Core.Audio.PlayOneShot(snd);
                    var ev2 = (FMOD.Studio.EventInstance)SkillTreeUIHelper.LoadingFxField.GetValue(__instance);
                    SkillTreeUIHelper.StopLoadingFxMethod?.Invoke(__instance, new object[] { ev2 });
                }
            }
            else
            {
                SkillTreeUIHelper.TimePressingField.SetValue(__instance, 0f);
                var ev = (FMOD.Studio.EventInstance)SkillTreeUIHelper.LoadingFxField.GetValue(__instance);
                SkillTreeUIHelper.StopLoadingFxMethod?.Invoke(__instance, new object[] { ev });
            }
        }
        else
        {
            SkillTreeUIHelper.TimePressingField.SetValue(__instance, 0f);
        }

        if (unlockMask != null)
        {
            float cur = (float)SkillTreeUIHelper.TimePressingField.GetValue(__instance);
            unlockMask.fillAmount = cur / timeToUnlock;
        }
        return false;
    }
}

[HarmonyPatch(typeof(NewInventory_Skill), "UpdateStatus")]
internal static class SkillItem_UpdateStatus_P2_Patch
{
    private static readonly FieldInfo SkillIdField = AccessTools.Field(typeof(NewInventory_Skill), "skill");
    private static readonly FieldInfo BackLockedField = AccessTools.Field(typeof(NewInventory_Skill), "backgorundLocked");
    private static readonly FieldInfo BackUnlockedField = AccessTools.Field(typeof(NewInventory_Skill), "backgorundUnLocked");
    private static readonly FieldInfo BackEnabledField = AccessTools.Field(typeof(NewInventory_Skill), "backgorundEnabled");
    private static readonly FieldInfo SkillImageField = AccessTools.Field(typeof(NewInventory_Skill), "skillImage");
    private static readonly FieldInfo TierTextField = AccessTools.Field(typeof(NewInventory_Skill), "tierText");
    private static readonly FieldInfo DisabledColorField = AccessTools.Field(typeof(NewInventory_Skill), "disabledColor");

    private static bool Prefix(NewInventory_Skill __instance)
    {
        string skillId = (string)SkillIdField.GetValue(__instance);
        bool isP2View = Player2MenuView.IsSkillP2View;
        bool isElemental = SwordElementalUpgrades.IsElementalSkill(skillId);

        // Nodos vanilla para P1: intactos al flujo original del juego
        if (!isP2View && !isElemental) return true;

        var backLocked = BackLockedField.GetValue(__instance) as GameObject;
        var backUnlocked = BackUnlockedField.GetValue(__instance) as GameObject;
        var backEnabled = BackEnabledField.GetValue(__instance) as GameObject;
        var skillImage = SkillImageField.GetValue(__instance) as Image;
        var tierText = TierTextField.GetValue(__instance) as Text;
        var disabledColor = (Color)DisabledColorField.GetValue(__instance);

        var skill = Core.SkillManager.GetSkill(skillId);
        if (backLocked != null) backLocked.SetActive(false);
        if (backUnlocked != null) backUnlocked.SetActive(false);
        if (backEnabled != null) backEnabled.SetActive(false);
        if (tierText != null) tierText.text = "";

        if (skillImage != null && skill != null) skillImage.sprite = skill.smallImage;

        int activeIdx = Player2MenuView.ActivePlayerIndex;
        Penitent activePenitent = CoopPlayerRegistry.GetPenitent(activeIdx);

        bool isUnlocked = isP2View
            ? Player2SkillManager.IsUnlocked(activeIdx, skillId)
            : (Core.SkillManager != null && Core.SkillManager.IsSkillUnlocked(skillId));

        bool canUnlock = false;
        if (!isUnlocked && skill != null)
        {
            float mea = isP2View && activePenitent != null
                ? activePenitent.Stats.MeaCulpa.Final
                : (Core.SkillManager != null ? Core.SkillManager.GetCurrentMeaCulpa() : 1f);

            if (mea >= skill.tier)
            {
                string parent = skill.GetParentSkill();
                if (string.IsNullOrEmpty(parent))
                {
                    canUnlock = true;
                }
                else
                {
                    canUnlock = isP2View
                        ? Player2SkillManager.IsUnlocked(activeIdx, parent)
                        : (Core.SkillManager != null && Core.SkillManager.IsSkillUnlocked(parent));
                }
            }
        }

        bool isLocked = false;
        if (isUnlocked)
        {
            if (backUnlocked != null) backUnlocked.SetActive(true);
            if (skillImage != null && skill != null) skillImage.sprite = skill.smallImageBuyed;
        }
        else if (canUnlock)
        {
            if (backEnabled != null) backEnabled.SetActive(true);
        }
        else
        {
            if (tierText != null && skill != null)
            {
                tierText.text = skill.tier.ToString();
                tierText.color = disabledColor;
            }
            isLocked = true;
        }
        if (backLocked != null) backLocked.SetActive(isLocked);
        if (skillImage != null) skillImage.gameObject.SetActive(!isLocked);
        return false;
    }
}

[HarmonyPatch(typeof(NewInventory_Skill), "SetFocus")]
internal static class SkillItem_SetFocus_P2_Patch
{
    private static readonly FieldInfo SkillIdField = AccessTools.Field(typeof(NewInventory_Skill), "skill");
    private static readonly FieldInfo FocusField = AccessTools.Field(typeof(NewInventory_Skill), "focus");
    private static readonly FieldInfo CostField = AccessTools.Field(typeof(NewInventory_Skill), "cost");
    private static readonly FieldInfo CostTextField = AccessTools.Field(typeof(NewInventory_Skill), "costText");
    private static readonly FieldInfo NormalColorField = AccessTools.Field(typeof(NewInventory_Skill), "nomalColor");
    private static readonly FieldInfo DisabledColorField = AccessTools.Field(typeof(NewInventory_Skill), "disabledColor");

    private static bool Prefix(NewInventory_Skill __instance, bool bFocus, bool editMode)
    {
        if (!Player2MenuView.IsSkillP2View) return true;
        string skillId = (string)SkillIdField.GetValue(__instance);
        var focus = FocusField.GetValue(__instance) as GameObject;
        var cost = CostField.GetValue(__instance) as GameObject;
        var costText = CostTextField.GetValue(__instance) as Text;
        var normalColor = (Color)NormalColorField.GetValue(__instance);
        var disabledColor = (Color)DisabledColorField.GetValue(__instance);

        int activeIdx = Player2MenuView.ActivePlayerIndex;
        var skill = Core.SkillManager.GetSkill(skillId);
        bool canNoPoints = SkillTreeUIHelper.CanUnlockNoPointsP2(skillId);
        bool showCost = bFocus && !Player2SkillManager.IsUnlocked(activeIdx, skillId) && (editMode || canNoPoints);
        if (focus != null) focus.SetActive(bFocus);
        if (cost != null) cost.SetActive(showCost);
        if (costText != null) costText.gameObject.SetActive(showCost);
        bool useDisabledColor = true;
        if (showCost)
        {
            if (!canNoPoints)
            {
                if (costText != null) costText.text = "???";
            }
            else
            {
                if (costText != null && skill != null) costText.text = skill.cost.ToString();
                bool canAfford = SkillTreeUIHelper.CanUnlockWithPointsP2(skillId);
                useDisabledColor = !canAfford || !editMode;
            }
        }
        if (costText != null) costText.color = !useDisabledColor ? normalColor : disabledColor;
        return false;
    }
}
