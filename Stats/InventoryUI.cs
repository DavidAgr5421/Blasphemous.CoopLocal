using Framework.Inventory;
using Framework.Managers;
using Gameplay.UI.Others.MenuLogic;
using Gameplay.UI.Others.UIGameLogic;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.UI;

namespace Blasphemous.CoopLocal;

// Fase 2 / Ronda 88 — UI Grid sombra P2 (Prayers/Beads/Swords/Relics).
// Muestra visualmente los rezos, cuentas de rosario y corazones de Mea Culpa equipados por P2,
// sincroniza las lágrimas de enmienda (Purge) de P2 y actualiza los encabezados con el nombre del jugador.
[HarmonyPatch(typeof(NewInventory_LayoutGrid), "ShowLayout")]
internal static class Grid_ShowLayout_P2_Patch
{
    private static readonly FieldInfo PurgeControlField =
        AccessTools.Field(typeof(NewInventory_LayoutGrid), "purgeControl");
    private static readonly FieldInfo PurgeTextField =
        AccessTools.Field(typeof(PlayerPurgePoints), "text");
    private static readonly FieldInfo CurrentItemTypeField =
        AccessTools.Field(typeof(NewInventory_LayoutGrid), "currentItemType");
    private static readonly MethodInfo UpdateEquippedMethod =
        AccessTools.Method(typeof(NewInventory_LayoutGrid), "UpdateEquipped");

    private static void Postfix(NewInventory_LayoutGrid __instance, NewInventoryWidget.TabType tabType, bool editMode)
    {
        if (!Player2MenuView.IsInventoryP2View) return;

        // Actualizar contador de lágrimas de enmienda (Purge) con el valor real de P2
        if (CoopLocal.Player2 != null && PurgeControlField != null && PurgeTextField != null)
        {
            var purgeControl = PurgeControlField.GetValue(__instance) as PlayerPurgePoints;
            if (purgeControl != null)
            {
                var purgeText = PurgeTextField.GetValue(purgeControl) as Text;
                if (purgeText != null)
                {
                    purgeText.text = ((int)CoopLocal.Player2.Stats.Purge.Current).ToString();
                }
            }
        }

        // Asegurar que la ranura superior de equipados (Rezo activo, Cuentas de Rosario, Corazón de Espada)
        // refleje el estado de P2 tras haber corrido CacheEquipped() en ShowLayout vanilla.
        if (CurrentItemTypeField != null && UpdateEquippedMethod != null)
        {
            var cur = CurrentItemTypeField.GetValue(__instance);
            UpdateEquippedMethod.Invoke(__instance, new object[] { cur });
        }
    }
}

// Actualiza los slots superiores (los equipados en el rosario, rezo activo, corazón de espada)
// para mostrar visualmente los de P2 en vez de los de P1.
[HarmonyPatch(typeof(NewInventory_LayoutGrid), "UpdateEquipped")]
internal static class Grid_UpdateEquipped_P2_Patch
{
    private static readonly FieldInfo AvailableEquipablesField =
        AccessTools.Field(typeof(NewInventory_LayoutGrid), "availableEquipables");
    private static readonly FieldInfo CachedEquippedField =
        AccessTools.Field(typeof(NewInventory_LayoutGrid), "cachedEquipped");
    private static readonly FieldInfo CurrentSelectedEquipedField =
        AccessTools.Field(typeof(NewInventory_LayoutGrid), "currentSelectedEquiped");

    private static bool Prefix(NewInventory_LayoutGrid __instance, InventoryManager.ItemType itemType)
    {
        if (!Player2MenuView.IsInventoryP2View) return true; // P1 vanilla
        if (Core.InventoryManager == null) return true;

        int p2BeadSlots = CoopLocal.Player2 != null ? (int)CoopLocal.Player2.Stats.BeadSlots.Final : 2;
        if (p2BeadSlots <= 0) p2BeadSlots = 2;
        int avail = itemType != InventoryManager.ItemType.Bead ? 9999 : p2BeadSlots;
        AvailableEquipablesField?.SetValue(__instance, avail);

        var cachedEquipped = CachedEquippedField?.GetValue(__instance) as List<NewInventory_GridItem>;
        if (cachedEquipped == null) return true;
        int curSelectedEquiped = CurrentSelectedEquipedField != null ? (int)CurrentSelectedEquipedField.GetValue(__instance) : -1;

        for (int i = 0; i < cachedEquipped.Count; i++)
        {
            NewInventory_GridItem gridItem = cachedEquipped[i];
            if (gridItem == null) continue;

            BaseInventoryObject obj = null;
            switch (itemType)
            {
                case InventoryManager.ItemType.Bead:
                    string beadId = Player2InventoryManager.GetBeadInSlot(i);
                    if (!string.IsNullOrEmpty(beadId)) obj = Core.InventoryManager.GetRosaryBead(beadId);
                    break;
                case InventoryManager.ItemType.Prayer:
                    if (i == 0) obj = Player2InventoryManager.GetEquippedPrayerObj();
                    break;
                case InventoryManager.ItemType.Sword:
                    if (i == 0)
                    {
                        string swordId = Player2InventoryManager.GetSwordInSlot(0);
                        if (!string.IsNullOrEmpty(swordId)) obj = Core.InventoryManager.GetSword(swordId);
                    }
                    break;
                case InventoryManager.ItemType.Relic:
                    string relicId = Player2InventoryManager.GetRelicInSlot(i);
                    if (!string.IsNullOrEmpty(relicId)) obj = Core.InventoryManager.GetRelic(relicId);
                    else obj = Core.InventoryManager.GetRelicInSlot(i);
                    break;
            }
            gridItem.SetObject(obj);
            bool isSelectedSlot = curSelectedEquiped == i;
            gridItem.UpdateStatus(i < avail, isSelectedSlot, obj != null || isSelectedSlot);
        }
        return false;
    }
}

[HarmonyPatch(typeof(NewInventory_LayoutGrid), "IsEquipped")]
internal static class Grid_IsEquipped_P2_Patch
{
    private static readonly FieldInfo CurrentItemTypeField =
        AccessTools.Field(typeof(NewInventory_LayoutGrid), "currentItemType");

    private static bool Prefix(NewInventory_LayoutGrid __instance, BaseInventoryObject obj, ref bool __result)
    {
        if (!Player2MenuView.IsInventoryP2View) return true;
        if (obj == null)
        {
            __result = false;
            return false;
        }
        string id = obj.id;
        var cur = (InventoryManager.ItemType)CurrentItemTypeField.GetValue(__instance);
        switch (cur)
        {
            case InventoryManager.ItemType.Prayer:
                __result = Player2InventoryManager.IsPrayerEquipped(id);
                return false;
            case InventoryManager.ItemType.Bead:
                __result = Player2InventoryManager.IsBeadEquipped(id);
                return false;
            case InventoryManager.ItemType.Sword:
                __result = Player2InventoryManager.IsSwordEquipped(id);
                return false;
            case InventoryManager.ItemType.Relic:
                return true;
        }
        return true;
    }
}

[HarmonyPatch(typeof(NewInventory_LayoutGrid), "EquipObject")]
internal static class Grid_EquipObject_P2_Patch
{
    private static readonly FieldInfo CurrentItemTypeField =
        AccessTools.Field(typeof(NewInventory_LayoutGrid), "currentItemType");
    private static readonly FieldInfo EquipSoundsField =
        AccessTools.Field(typeof(NewInventory_LayoutGrid), "equipSounds");
    private static readonly MethodInfo UpdateEquippedMethod =
        AccessTools.Method(typeof(NewInventory_LayoutGrid), "UpdateEquipped");
    private static readonly MethodInfo UpdateItemByIdMethod =
        AccessTools.Method(typeof(NewInventory_LayoutGrid), "UpdateItemById");

    private static bool Prefix(NewInventory_LayoutGrid __instance, BaseInventoryObject obj)
    {
        if (!Player2MenuView.IsInventoryP2View) return true;
        if (obj == null) return false;

        var cur = (InventoryManager.ItemType)CurrentItemTypeField.GetValue(__instance);
        switch (cur)
        {
            case InventoryManager.ItemType.Prayer:
                string prevPrayer = Player2InventoryManager.GetEquippedPrayerId();
                if (!string.IsNullOrEmpty(prevPrayer) && prevPrayer != obj.id)
                {
                    UpdateItemByIdMethod?.Invoke(__instance, new object[] { prevPrayer });
                }
                Player2InventoryManager.EquipPrayer(obj.id);
                break;
            case InventoryManager.ItemType.Bead:
                int slot = Player2InventoryManager.FindFreeBeadSlot();
                if (slot < 0) return false;
                Player2InventoryManager.EquipBead(obj.id, slot);
                break;
            case InventoryManager.ItemType.Sword:
                string prevSword = Player2InventoryManager.GetEquippedSwordId();
                if (!string.IsNullOrEmpty(prevSword) && prevSword != obj.id)
                {
                    UpdateItemByIdMethod?.Invoke(__instance, new object[] { prevSword });
                }
                Player2InventoryManager.EquipSword(obj.id);
                break;
            default:
                return true;
        }

        UpdateEquippedMethod?.Invoke(__instance, new object[] { cur });

        var equipSounds = EquipSoundsField?.GetValue(__instance) as Dictionary<InventoryManager.ItemType, string>;
        if (equipSounds != null && equipSounds.TryGetValue(cur, out string sfx) && !string.IsNullOrEmpty(sfx))
        {
            Core.Audio.PlayOneShot(sfx);
        }
        return false;
    }
}

[HarmonyPatch(typeof(NewInventory_LayoutGrid), "UnEquipObject")]
internal static class Grid_UnEquipObject_P2_Patch
{
    private static readonly FieldInfo CurrentItemTypeField =
        AccessTools.Field(typeof(NewInventory_LayoutGrid), "currentItemType");
    private static readonly FieldInfo UnequipSoundsField =
        AccessTools.Field(typeof(NewInventory_LayoutGrid), "unequipSounds");
    private static readonly MethodInfo UpdateEquippedMethod =
        AccessTools.Method(typeof(NewInventory_LayoutGrid), "UpdateEquipped");
    private static readonly MethodInfo UpdateItemByIdMethod =
        AccessTools.Method(typeof(NewInventory_LayoutGrid), "UpdateItemById");

    private static bool Prefix(NewInventory_LayoutGrid __instance, BaseInventoryObject obj)
    {
        if (!Player2MenuView.IsInventoryP2View) return true;
        if (obj == null) return false;

        var cur = (InventoryManager.ItemType)CurrentItemTypeField.GetValue(__instance);
        switch (cur)
        {
            case InventoryManager.ItemType.Bead:
                Player2InventoryManager.UnequipBead(obj.id);
                break;
            case InventoryManager.ItemType.Prayer:
                Player2InventoryManager.UnequipPrayer();
                break;
            case InventoryManager.ItemType.Sword:
                Player2InventoryManager.UnequipSword();
                break;
            default:
                return true;
        }

        UpdateEquippedMethod?.Invoke(__instance, new object[] { cur });
        UpdateItemByIdMethod?.Invoke(__instance, new object[] { obj.id });

        var unequipSounds = UnequipSoundsField?.GetValue(__instance) as Dictionary<InventoryManager.ItemType, string>;
        if (unequipSounds != null && unequipSounds.TryGetValue(cur, out string sfx) && !string.IsNullOrEmpty(sfx))
        {
            Core.Audio.PlayOneShot(sfx);
        }
        return false;
    }
}

[HarmonyPatch(typeof(NewInventory_LayoutGrid), "GetFirstEmptySlot")]
internal static class Grid_GetFirstEmptySlot_P2_Patch
{
    private static readonly FieldInfo CurrentItemTypeField =
        AccessTools.Field(typeof(NewInventory_LayoutGrid), "currentItemType");

    private static bool Prefix(NewInventory_LayoutGrid __instance, ref int __result)
    {
        if (!Player2MenuView.IsInventoryP2View) return true;
        var cur = (InventoryManager.ItemType)CurrentItemTypeField.GetValue(__instance);
        switch (cur)
        {
            case InventoryManager.ItemType.Bead:
                __result = Player2InventoryManager.FindFreeBeadSlot();
                return false;
            case InventoryManager.ItemType.Prayer:
                __result = string.IsNullOrEmpty(Player2InventoryManager.GetEquippedPrayerId()) ? 0 : -1;
                return false;
            case InventoryManager.ItemType.Sword:
                __result = string.IsNullOrEmpty(Player2InventoryManager.GetEquippedSwordId()) ? 0 : -1;
                return false;
            default:
                return true;
        }
    }
}

// Ronda 88: Parche sobre SelectTab (el método real que cambia de pestaña en NewInventoryWidget)
// para mostrar el nombre del jugador en el título de la pestaña y refrescar el cartel "[nombre jugador] menu".
[HarmonyPatch(typeof(NewInventoryWidget), "SelectTab")]
internal static class NewInventoryWidget_SelectTab_P2_Patch
{
    private static readonly FieldInfo TabLabelField = AccessTools.Field(typeof(NewInventoryWidget), "tabLabel");

    private static void Postfix(NewInventoryWidget __instance, NewInventoryWidget.TabType tabType)
    {
        CoopMenuOwnerBanner.Refresh(__instance);

        var tabLabel = TabLabelField?.GetValue(__instance) as Text;
        if (tabLabel == null) return;

        string currentText = tabLabel.text ?? string.Empty;
        for (int i = 0; i < CoopPlayerRegistry.MaxPlayers; i++)
        {
            string tag = CoopPlayerRegistry.GetSlot(i).BracketTag + " ";
            if (currentText.StartsWith(tag))
            {
                currentText = currentText.Substring(tag.Length);
                break;
            }
        }
        if (currentText.StartsWith("[P1] ") || currentText.StartsWith("[P2] "))
        {
            currentText = currentText.Substring(5);
        }

        tabLabel.text = $"{Player2MenuView.CurrentBracketTag} {currentText}";
    }
}
