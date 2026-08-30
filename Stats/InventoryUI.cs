using Framework.Inventory;
using Framework.Managers;
using Gameplay.UI.Others.MenuLogic;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.CoopLocal;

// Fase 2 — UI Grid sombra P2 (Prayers/Beads/Swords). Toggle F7 igual que skills.
[HarmonyPatch(typeof(NewInventory_LayoutGrid), "ShowLayout")]
internal static class Grid_ShowLayout_P2_Patch
{
    private static void Postfix(NewInventory_LayoutGrid __instance, NewInventoryWidget.TabType tabType, bool editMode)
    {
        // Si es P2 view, refrescar indicador (maxSlots ya pintado por vanilla, lo dejamos)
        if (!Player2MenuView.IsInventoryP2View) return;
        // No hay texto dedicado; el debug F8 ya muestra equipped, pero podemos loggear
        if (tabType == NewInventoryWidget.TabType.Prayers || tabType == NewInventoryWidget.TabType.Rosary || tabType == NewInventoryWidget.TabType.Sword)
        {
            // Forzar refresh visual de equipped tras toggle
            var mi = AccessTools.Method(typeof(NewInventory_LayoutGrid), "UpdateEquipped");
            // UpdateEquipped es privado y toma InventoryManager.ItemType; lo invocamos via reflection con el tipo actual
            var curTypeField = AccessTools.Field(typeof(NewInventory_LayoutGrid), "currentItemType");
            if (curTypeField != null && mi != null)
            {
                var cur = curTypeField.GetValue(__instance);
                mi.Invoke(__instance, new object[] { cur });
            }
        }
    }
}

[HarmonyPatch(typeof(NewInventory_LayoutGrid), "IsEquipped")]
internal static class Grid_IsEquipped_P2_Patch
{
    private static bool Prefix(NewInventory_LayoutGrid __instance, BaseInventoryObject obj, ref bool __result)
    {
        if (!Player2MenuView.IsInventoryP2View) return true;
        if (obj == null) { __result = false; return false; }
        string id = obj.id;
        // Determinar tipo por tab actual
        var typeField = AccessTools.Field(typeof(NewInventory_LayoutGrid), "currentItemType");
        var cur = (InventoryManager.ItemType)typeField.GetValue(__instance);
        switch (cur)
        {
            case InventoryManager.ItemType.Prayer: __result = Player2InventoryManager.IsPrayerEquipped(id); return false;
            case InventoryManager.ItemType.Bead: __result = Player2InventoryManager.IsBeadEquipped(id); return false;
            case InventoryManager.ItemType.Sword: __result = Player2InventoryManager.IsSwordEquipped(id); return false;
            case InventoryManager.ItemType.Relic:
                // Para relics, por ahora delegar a P2 si quisiéramos, pero no hay store específico; usar vanilla
                return true;
        }
        return true;
    }
}

[HarmonyPatch(typeof(NewInventory_LayoutGrid), "EquipObject")]
internal static class Grid_EquipObject_P2_Patch
{
    private static bool Prefix(NewInventory_LayoutGrid __instance, BaseInventoryObject obj)
    {
        if (!Player2MenuView.IsInventoryP2View) return true;
        if (obj == null) return false;
        var typeField = AccessTools.Field(typeof(NewInventory_LayoutGrid), "currentItemType");
        var cur = (InventoryManager.ItemType)typeField.GetValue(__instance);
        switch (cur)
        {
            case InventoryManager.ItemType.Prayer:
                Player2InventoryManager.EquipPrayer(obj.id);
                return false;
            case InventoryManager.ItemType.Bead:
                int slot = Player2InventoryManager.FindFreeBeadSlot();
                if (slot < 0) return false;
                Player2InventoryManager.EquipBead(obj.id, slot);
                return false;
            case InventoryManager.ItemType.Sword:
                Player2InventoryManager.EquipSword(obj.id);
                return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(NewInventory_LayoutGrid), "UnEquipObject")]
internal static class Grid_UnEquipObject_P2_Patch
{
    private static bool Prefix(NewInventory_LayoutGrid __instance, BaseInventoryObject obj)
    {
        if (!Player2MenuView.IsInventoryP2View) return true;
        if (obj == null) return false;
        var typeField = AccessTools.Field(typeof(NewInventory_LayoutGrid), "currentItemType");
        var cur = (InventoryManager.ItemType)typeField.GetValue(__instance);
        switch (cur)
        {
            case InventoryManager.ItemType.Prayer: Player2InventoryManager.UnequipPrayer(); return false;
            case InventoryManager.ItemType.Bead: Player2InventoryManager.UnequipBead(obj.id); return false;
            case InventoryManager.ItemType.Sword: Player2InventoryManager.UnequipSword(); return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(NewInventory_LayoutGrid), "GetFirstEmptySlot")]
internal static class Grid_GetFirstEmptySlot_P2_Patch
{
    private static bool Prefix(NewInventory_LayoutGrid __instance, ref int __result)
    {
        if (!Player2MenuView.IsInventoryP2View) return true;
        var typeField = AccessTools.Field(typeof(NewInventory_LayoutGrid), "currentItemType");
        var cur = (InventoryManager.ItemType)typeField.GetValue(__instance);
        if (cur == InventoryManager.ItemType.Bead)
        {
            __result = Player2InventoryManager.FindFreeBeadSlot();
            // Si no hay slot libre por BeadSlots de P2 menor, respetar límite de p2
            int max = CoopLocal.Player2 != null ? (int)CoopLocal.Player2.Stats.BeadSlots.Final : 8;
            if (__result >= max) __result = -1;
            return false;
        }
        if (cur == InventoryManager.ItemType.Prayer || cur == InventoryManager.ItemType.Sword)
        {
            // 1 slot siempre libre si no equipado
            __result = 0;
            return false;
        }
        return true;
    }
}
