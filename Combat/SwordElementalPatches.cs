using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Framework.FrameworkCore;
using Framework.Inventory;
using Framework.Managers;
using Gameplay.GameControllers.Effects.Entity;
using Gameplay.GameControllers.Enemies.Framework.Damage;
using Gameplay.GameControllers.Enemies.Framework.IA;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using Gameplay.GameControllers.Penitent.Attack;
using Gameplay.UI.Others.Buttons;
using Gameplay.UI.Others.MenuLogic;
using HarmonyLib;
using Tools.Items;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Blasphemous.CoopLocal;

// =========================================================================
// Parches de SkillManager e InventoryManager
// =========================================================================
[HarmonyPatch(typeof(SkillManager), "LoadAllSkills")]
internal static class SkillManager_LoadAllSkills_Elemental_Patch
{
    private static void Postfix(SkillManager __instance)
    {
        SwordElementalUpgrades.EnsureSkillsRegistered(__instance);
    }
}

[HarmonyPatch(typeof(SkillManager), "SetCurrentPersistentState")]
internal static class SkillManager_SetCurrentPersistentState_Elemental_Patch
{
    private static void Prefix(SkillManager __instance)
    {
        SwordElementalUpgrades.EnsureSkillsRegistered(__instance);
    }
}

[HarmonyPatch(typeof(SkillManager), "OnLocalizationChange")]
internal static class SkillManager_OnLocalizationChange_Elemental_Patch
{
    private static void Postfix(SkillManager __instance)
    {
        SwordElementalUpgrades.EnsureSkillsRegistered(__instance);
    }
}

[HarmonyPatch(typeof(SkillManager), "CanUnlockSkillNoCheckPoints")]
internal static class SkillManager_CanUnlockSkillNoCheckPoints_Elemental_Patch
{
    private static void Postfix(string skill, ref bool __result)
    {
        // Ambas sendas (Veneno y Sangrado) pueden desbloquearse libremente sin bloquearse entre sí
    }
}

[HarmonyPatch(typeof(SkillManager), "UnlockSkill")]
internal static class SkillManager_UnlockSkill_Elemental_Patch
{
    private static void Postfix(string skill, bool __result)
    {
        if (__result && SwordElementalUpgrades.IsElementalSkill(skill))
        {
            SwordElementalUpgrades.GrantMatchingHeartOnUnlock(skill, isPlayer2: false);
        }
    }
}

[HarmonyPatch(typeof(InventoryManager), "InitializeObjects")]
internal static class InventoryManager_InitializeObjects_Elemental_Patch
{
    private static void Postfix(InventoryManager __instance)
    {
        SwordElementalUpgrades.EnsureSwordsRegistered(__instance);
    }
}

[HarmonyPatch(typeof(InventoryManager), "SetCurrentPersistentState")]
internal static class InventoryManager_SetCurrentPersistentState_Elemental_Patch
{
    private static void Prefix(InventoryManager __instance)
    {
        SwordElementalUpgrades.EnsureSwordsRegistered(__instance);
    }

    private static void Postfix(InventoryManager __instance)
    {
        SwordElementalUpgrades.EnsureSwordsRegistered(__instance);
    }
}

[HarmonyPatch(typeof(InventoryManager), "OnLocalizationChange")]
internal static class InventoryManager_OnLocalizationChange_Elemental_Patch
{
    private static void Postfix(InventoryManager __instance)
    {
        SwordElementalUpgrades.EnsureSwordsRegistered(__instance);
    }
}

// Permitir equipar los Corazones de Mea Culpa (especialmente HE_VENENO y HE_SANGRADO) en cualquier momento desde el menú
[HarmonyPatch(typeof(NewInventory_LayoutGrid), "GetInventoryObjectAction")]
internal static class NewInventory_LayoutGrid_GetInventoryObjectAction_Elemental_Patch
{
    private static readonly MethodInfo IsEquippedMethod = Reflect.Method(typeof(NewInventory_LayoutGrid), "IsEquipped");

    private static void Postfix(NewInventory_LayoutGrid __instance, BaseInventoryObject obj, ref NewInventoryWidget.EquipAction __result)
    {
        if (obj is Sword && __result == NewInventoryWidget.EquipAction.None)
        {
            bool isEquipped = IsEquippedMethod != null && (bool)IsEquippedMethod.Invoke(__instance, new object[] { obj });
            __result = isEquipped ? NewInventoryWidget.EquipAction.UnEquip : NewInventoryWidget.EquipAction.Equip;
        }
    }
}

[HarmonyPatch(typeof(NewInventory_LayoutGrid), "UpdateItemsBySwordHeartEquipability")]
internal static class NewInventory_LayoutGrid_UpdateItemsBySwordHeartEquipability_Elemental_Patch
{
    private static readonly FieldInfo CachedGridElementsField = Reflect.Field(typeof(NewInventory_LayoutGrid), "cachedGridElements");

    private static void Postfix(NewInventory_LayoutGrid __instance, NewInventoryWidget.TabType tabType)
    {
        if (tabType != NewInventoryWidget.TabType.Sword) return;
        if (__instance.swordHeartText != null) __instance.swordHeartText.SetActive(false);
        if (__instance.swordHeartTextLocked != null) __instance.swordHeartTextLocked.SetActive(false);

        var elements = CachedGridElementsField?.GetValue(__instance) as List<NewInventory_GridItem>;
        if (elements != null)
        {
            foreach (var elem in elements)
            {
                elem?.DeactivateGrayscale();
            }
        }
    }
}
