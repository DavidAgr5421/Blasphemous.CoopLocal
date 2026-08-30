using Framework.Inventory;
using Framework.Managers;
using Gameplay.UI.Others.MenuLogic;
using HarmonyLib;
using System.Reflection;
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

[HarmonyPatch(typeof(NewInventory_LayoutGrid), "ShowLayout")]
internal static class Grid_ShowLayout_P2_ItemSource_Patch
{
    private static readonly MethodInfo FillGridElementsGeneric =
        AccessTools.Method(typeof(NewInventory_LayoutGrid), "FillGridElements");

    private static bool Prefix(NewInventory_LayoutGrid __instance, NewInventoryWidget.TabType tabType, bool editMode)
    {
        if (!Player2MenuView.IsInventoryP2View) return true; // vista P1 - vanilla intacto
        if (Core.InventoryManager == null) return true;

        switch (tabType)
        {
            case NewInventoryWidget.TabType.Prayers:
                Invoke<Prayer>(InventoryManager.ItemType.Prayer,
                    Core.InventoryManager.GetPrayersOwned(), p => Player2InventoryManager.IsPrayerOwned(p.id));
                return false;
            case NewInventoryWidget.TabType.Rosary:
                Invoke<RosaryBead>(InventoryManager.ItemType.Bead,
                    Core.InventoryManager.GetRosaryBeadOwned(), b => Player2InventoryManager.IsOwnedBead(b.id));
                return false;
            case NewInventoryWidget.TabType.Sword:
                Invoke<Sword>(InventoryManager.ItemType.Sword,
                    Core.InventoryManager.GetSwordsOwned(), s => Player2InventoryManager.IsSwordOwned(s.id));
                return false;
            default:
                // Collectables/Reliquary/Quest - sin concepto per-player todavía, vanilla intacto
                return true;
        }

        void Invoke<T>(InventoryManager.ItemType type, System.Collections.ObjectModel.ReadOnlyCollection<T> all, System.Func<T, bool> ownedByP2) where T : BaseInventoryObject
        {
            var list = new System.Collections.Generic.List<T>();
            foreach (var item in all) if (ownedByP2(item)) list.Add(item);
            var closed = FillGridElementsGeneric.MakeGenericMethod(typeof(T));
            closed.Invoke(__instance, new object[] { type, list.AsReadOnly() });
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