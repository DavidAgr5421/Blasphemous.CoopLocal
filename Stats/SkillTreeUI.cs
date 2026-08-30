using Framework.Managers;
using Gameplay.UI.Others.MenuLogic;
using HarmonyLib;
using Rewired;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace Blasphemous.CoopLocal;

// Fase 1 — Skill Tree UI sombra P2. No toca SkillManager global, solo UI.
internal static class SkillTreeUIHelper
{
    internal static readonly FieldInfo UnlockMaskField = AccessTools.Field(typeof(NewInventory_LayoutSkill), "unlockMask");
    internal static readonly FieldInfo TimePressingField = AccessTools.Field(typeof(NewInventory_LayoutSkill), "timePressingUnlock");
    internal static readonly FieldInfo TimeToUnlockField = AccessTools.Field(typeof(NewInventory_LayoutSkill), "timeToUnlockSKill");
    internal static readonly FieldInfo CachedSkillsField = AccessTools.Field(typeof(NewInventory_LayoutSkill), "cachedSkills");
    internal static readonly FieldInfo CurrentSelectedField = AccessTools.Field(typeof(NewInventory_LayoutSkill), "currentSelected");
    internal static readonly FieldInfo InEditModeField = AccessTools.Field(typeof(NewInventory_LayoutSkill), "inEditMode");
    internal static readonly FieldInfo PurgeControlField = AccessTools.Field(typeof(NewInventory_LayoutSkill), "purgeControl");
    internal static readonly FieldInfo MaxTierField = AccessTools.Field(typeof(NewInventory_LayoutSkill), "maxTier");
    internal static readonly FieldInfo RewiredField = AccessTools.Field(typeof(NewInventory_LayoutSkill), "rewired");
    internal static readonly FieldInfo LoadingFxField = AccessTools.Field(typeof(NewInventory_LayoutSkill), "_loadingPurchaseFxEvent");
    internal static readonly MethodInfo PlayLoadingFxMethod = AccessTools.Method(typeof(NewInventory_LayoutSkill), "PlayLoadingPurchaseFx");
    internal static readonly MethodInfo StopLoadingFxMethod = AccessTools.Method(typeof(NewInventory_LayoutSkill), "StopLoadingPurchaseFx");
    internal static readonly FieldInfo SoundUnlockField = AccessTools.Field(typeof(NewInventory_LayoutSkill), "soundUnlockSkill");
}

[HarmonyPatch(typeof(NewInventory_LayoutSkill), "ShowLayout")]
internal static class SkillLayout_ShowLayout_P2_Patch
{
    private static void Postfix(NewInventory_LayoutSkill __instance, NewInventoryWidget.TabType tabType, bool editMode)
    {
        // Mostrar indicador de vista actual en maxTier text
        if (tabType != NewInventoryWidget.TabType.Abilities) return;
        var maxTier = (Text)SkillTreeUIHelper.MaxTierField.GetValue(__instance);
        if (maxTier == null) return;
        string prefix = Player2MenuView.IsSkillP2View ? "[P2] " : "[P1] ";
        // maxTier ya fue seteado a GetCurrentMeaCulpa() por vanilla; anteponer prefijo y valor correcto
        float mea = Player2MenuView.IsSkillP2View && CoopLocal.Player2 != null ? CoopLocal.Player2.Stats.MeaCulpa.Final : Core.SkillManager.GetCurrentMeaCulpa();
        string suffix = Player2MenuView.IsSkillP2View ? " (F7=P1)" : " (F7=P2)";
        maxTier.text = prefix + ((int)mea).ToString() + suffix;
        // purgeControl también refleja P2 si aplica
        var purgeControl = SkillTreeUIHelper.PurgeControlField.GetValue(__instance);
        // purgeControl lee Core.Logic.Penitent.Stats.Purge global; el costo se descuenta de p2.Stats.Purge.
        if (purgeControl != null && Player2MenuView.IsSkillP2View && CoopLocal.Player2 != null)
        {
        }
    }
}

[HarmonyPatch(typeof(NewInventory_LayoutSkill), "Update")]
internal static class SkillLayout_Update_P2_Patch
{
    private static bool Prefix(NewInventory_LayoutSkill __instance)
    {
        // Toggle F7 dentro del menú de skills (Abilities tab)
        if (Input.GetKeyDown(KeyCode.F7))
        {
            Player2MenuView.SkillViewPlayer = Player2MenuView.SkillViewPlayer == 0 ? 1 : 0;
            // Refrescar UI inmediato
            var mi = AccessTools.Method(typeof(NewInventory_LayoutSkill), "UpdateAllSKills");
            mi?.Invoke(__instance, null);
            var maxTier = (Text)SkillTreeUIHelper.MaxTierField.GetValue(__instance);
            if (maxTier != null)
            {
                float mea = Player2MenuView.IsSkillP2View && CoopLocal.Player2 != null ? CoopLocal.Player2.Stats.MeaCulpa.Final : Core.SkillManager.GetCurrentMeaCulpa();
                string prefix = Player2MenuView.IsSkillP2View ? "[P2] " : "[P1] ";
                string suffix = Player2MenuView.IsSkillP2View ? " (F7=P1)" : " (F7=P2)";
                maxTier.text = prefix + ((int)mea).ToString() + suffix;
            }
        }

        if (!Player2MenuView.IsSkillP2View) return true; // P1 vanilla intacto

        // P2 view: reimplementar Update completo con sombra
        bool inEditMode = (bool)SkillTreeUIHelper.InEditModeField.GetValue(__instance);
        if (!inEditMode) return false;

        var cachedSkills = SkillTreeUIHelper.CachedSkillsField.GetValue(__instance) as System.Collections.Generic.List<NewInventory_Skill>;
        int currentSelected = (int)SkillTreeUIHelper.CurrentSelectedField.GetValue(__instance);
        if (cachedSkills == null || currentSelected < 0 || currentSelected >= cachedSkills.Count) return false;

        string skillId = cachedSkills[currentSelected].GetSkillId();
        bool canUnlock = !Player2SkillManager.IsUnlocked(skillId);
        // Check parent/tier similar a SkillManager.CanUnlockSkillNoCheckPoints pero usando p2 MeaCulpa
        if (canUnlock)
        {
            var skill = Core.SkillManager.GetSkill(skillId);
            if (skill != null)
            {
                float mea = CoopLocal.Player2 != null ? CoopLocal.Player2.Stats.MeaCulpa.Final : 0;
                if (mea < skill.tier) canUnlock = false;
                string parent = skill.GetParentSkill();
                if (!string.IsNullOrEmpty(parent) && !Player2SkillManager.IsUnlocked(parent)) canUnlock = false;
            }
        }
        // También chequear coste si p2 Purge insuficiente (opcional, permitir igual con ignore)
        // Para UI usamos canUnlock para mostrar hold; el unlock real verifica coste.

        float timePressing = (float)SkillTreeUIHelper.TimePressingField.GetValue(__instance);
        float timeToUnlock = (float)SkillTreeUIHelper.TimeToUnlockField.GetValue(__instance);
        var unlockMask = (Image)SkillTreeUIHelper.UnlockMaskField.GetValue(__instance);
        Player rewired = (Player)SkillTreeUIHelper.RewiredField.GetValue(__instance);
        if (rewired == null) { rewired = ReInput.players.GetPlayer(0); SkillTreeUIHelper.RewiredField.SetValue(__instance, rewired); }

        if (canUnlock)
        {
            if (rewired != null && rewired.GetButton(52))
            {
                timePressing += Time.unscaledDeltaTime;
                SkillTreeUIHelper.TimePressingField.SetValue(__instance, timePressing);
                var ev = (FMOD.Studio.EventInstance)SkillTreeUIHelper.LoadingFxField.GetValue(__instance);
                SkillTreeUIHelper.PlayLoadingFxMethod.Invoke(__instance, new object[] { ev });
                if (timePressing >= timeToUnlock)
                {
                    timePressing = 0f;
                    SkillTreeUIHelper.TimePressingField.SetValue(__instance, 0f);
                    // Coste
                    var skill = Core.SkillManager.GetSkill(skillId);
                    float cost = skill != null ? skill.cost : 0;
                    if (CoopLocal.Player2 != null && CoopLocal.Player2.Stats.Purge.Current >= cost)
                    {
                        CoopLocal.Player2.Stats.Purge.Current -= cost;
                    }
                    Player2SkillManager.SetUnlocked(skillId, true);
                    Player2SkillManager.Persist();
                    var upd = AccessTools.Method(typeof(NewInventory_LayoutSkill), "UpdateAllSKills");
                    upd?.Invoke(__instance, null);
                    var sel = AccessTools.Method(typeof(NewInventory_LayoutSkill), "SelectSkill");
                    sel?.Invoke(__instance, new object[] { currentSelected, false });
                    string snd = (string)SkillTreeUIHelper.SoundUnlockField.GetValue(__instance);
                    Core.Audio.PlayOneShot(snd);
                    var ev2 = (FMOD.Studio.EventInstance)SkillTreeUIHelper.LoadingFxField.GetValue(__instance);
                    SkillTreeUIHelper.StopLoadingFxMethod.Invoke(__instance, new object[] { ev2 });
                }
            }
            else
            {
                SkillTreeUIHelper.TimePressingField.SetValue(__instance, 0f);
                var ev = (FMOD.Studio.EventInstance)SkillTreeUIHelper.LoadingFxField.GetValue(__instance);
                SkillTreeUIHelper.StopLoadingFxMethod.Invoke(__instance, new object[] { ev });
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
    private static bool Prefix(NewInventory_Skill __instance)
    {
        if (!Player2MenuView.IsSkillP2View) return true;
        var skillField = AccessTools.Field(typeof(NewInventory_Skill), "skill");
        string skillId = (string)skillField.GetValue(__instance);
        var backLocked = AccessTools.Field(typeof(NewInventory_Skill), "backgorundLocked").GetValue(__instance) as GameObject;
        var backUnlocked = AccessTools.Field(typeof(NewInventory_Skill), "backgorundUnLocked").GetValue(__instance) as GameObject;
        var backEnabled = AccessTools.Field(typeof(NewInventory_Skill), "backgorundEnabled").GetValue(__instance) as GameObject;
        var skillImage = AccessTools.Field(typeof(NewInventory_Skill), "skillImage").GetValue(__instance) as Image;
        var tierText = AccessTools.Field(typeof(NewInventory_Skill), "tierText").GetValue(__instance) as Text;
        var disabledColor = (Color)AccessTools.Field(typeof(NewInventory_Skill), "disabledColor").GetValue(__instance);

        var skill = Core.SkillManager.GetSkill(skillId);
        if (backLocked != null) backLocked.SetActive(false);
        if (backUnlocked != null) backUnlocked.SetActive(false);
        if (backEnabled != null) backEnabled.SetActive(false);
        if (tierText != null) tierText.text = "";

        if (skillImage != null && skill != null) skillImage.sprite = skill.smallImage;
        bool flag = false;
        if (Player2SkillManager.IsUnlocked(skillId))
        {
            if (backUnlocked != null) backUnlocked.SetActive(true);
            if (skillImage != null && skill != null) skillImage.sprite = skill.smallImageBuyed;
        }
        else
        {
            // Check si podría desbloquearse (parent + tier) sin chequear coste
            bool canNoPoints = false;
            if (skill != null)
            {
                float mea = CoopLocal.Player2 != null ? CoopLocal.Player2.Stats.MeaCulpa.Final : 0;
                if (mea >= skill.tier)
                {
                    string parent = skill.GetParentSkill();
                    if (string.IsNullOrEmpty(parent) || Player2SkillManager.IsUnlocked(parent)) canNoPoints = true;
                }
            }
            if (canNoPoints)
            {
                if (backEnabled != null) backEnabled.SetActive(true);
            }
            else
            {
                if (tierText != null && skill != null) { tierText.text = skill.tier.ToString(); tierText.color = disabledColor; }
                flag = true;
            }
        }
        if (backLocked != null) backLocked.SetActive(flag);
        if (skillImage != null) skillImage.gameObject.SetActive(!flag);
        return false;
    }
}

[HarmonyPatch(typeof(NewInventory_Skill), "SetFocus")]
internal static class SkillItem_SetFocus_P2_Patch
{
    private static bool Prefix(NewInventory_Skill __instance, bool bFocus, bool editMode)
    {
        if (!Player2MenuView.IsSkillP2View) return true;
        var skillField = AccessTools.Field(typeof(NewInventory_Skill), "skill");
        string skillId = (string)skillField.GetValue(__instance);
        var focus = AccessTools.Field(typeof(NewInventory_Skill), "focus").GetValue(__instance) as GameObject;
        var cost = AccessTools.Field(typeof(NewInventory_Skill), "cost").GetValue(__instance) as GameObject;
        var costText = AccessTools.Field(typeof(NewInventory_Skill), "costText").GetValue(__instance) as Text;
        var nomalColor = (Color)AccessTools.Field(typeof(NewInventory_Skill), "nomalColor").GetValue(__instance);
        var disabledColor = (Color)AccessTools.Field(typeof(NewInventory_Skill), "disabledColor").GetValue(__instance);

        var skill = Core.SkillManager.GetSkill(skillId);
        bool flag = bFocus && !Player2SkillManager.IsUnlocked(skillId) && (editMode || CanUnlockNoPointsP2(skillId));
        if (focus != null) focus.SetActive(bFocus);
        if (cost != null) cost.SetActive(flag);
        if (costText != null) costText.gameObject.SetActive(flag);
        bool flag2 = true;
        if (flag)
        {
            if (!CanUnlockNoPointsP2(skillId))
            {
                if (costText != null) costText.text = "???";
            }
            else
            {
                if (costText != null && skill != null) costText.text = skill.cost.ToString();
                bool canAfford = CoopLocal.Player2 != null && skill != null && CoopLocal.Player2.Stats.Purge.Current >= skill.cost;
                flag2 = !canAfford || !editMode;
            }
        }
        if (costText != null) costText.color = (!flag2 ? nomalColor : disabledColor);
        return false;
    }
    private static bool CanUnlockNoPointsP2(string skillId)
    {
        var skill = Core.SkillManager.GetSkill(skillId);
        if (skill == null || Player2SkillManager.IsUnlocked(skillId)) return false;
        float mea = CoopLocal.Player2 != null ? CoopLocal.Player2.Stats.MeaCulpa.Final : 0;
        if (mea < skill.tier) return false;
        string parent = skill.GetParentSkill();
        if (!string.IsNullOrEmpty(parent) && !Player2SkillManager.IsUnlocked(parent)) return false;
        return true;
    }
}
