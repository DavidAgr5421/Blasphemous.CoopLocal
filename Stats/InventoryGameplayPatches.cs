using Framework.Inventory;
using Framework.Managers;
using HarmonyLib;

namespace Blasphemous.CoopLocal;

// Fase 3 — Gameplay pasivo: si cualquier jugador invitado (P2, P3, P4) tiene equipado el bead/prayer/sword, que cuente en gameplay.
// Ronda 88: Mientras el menú de inventario está abierto para P1, NO superponer los equipados de guests
// para que la cuadrícula de P1 no marque erróneamente como equipados los objetos exclusivos de los demás.
[HarmonyPatch(typeof(InventoryManager), "IsRosaryBeadEquipped", new[] { typeof(string) })]
internal static class Inv_IsBeadEquipped_P2_Patch
{
    private static void Postfix(string idRosaryBead, ref bool __result)
    {
        if (__result) return;
        if (Gameplay.UI.UIController.instance != null && Gameplay.UI.UIController.instance.IsShowingInventory) return;
        if (Player2InventoryManager.IsAnyGuestBeadEquipped(idRosaryBead)) __result = true;
    }
}

[HarmonyPatch(typeof(InventoryManager), "IsRosaryBeadEquipped", new[] { typeof(RosaryBead) })]
internal static class Inv_IsBeadObjEquipped_P2_Patch
{
    private static void Postfix(RosaryBead bead, ref bool __result)
    {
        if (__result) return;
        if (Gameplay.UI.UIController.instance != null && Gameplay.UI.UIController.instance.IsShowingInventory) return;
        if (bead != null && Player2InventoryManager.IsAnyGuestBeadEquipped(bead.id)) __result = true;
    }
}

[HarmonyPatch(typeof(InventoryManager), "IsPrayerEquipped", new[] { typeof(string) })]
internal static class Inv_IsPrayerEquipped_P2_Patch
{
    private static void Postfix(string idPrayer, ref bool __result)
    {
        if (__result) return;
        if (Gameplay.UI.UIController.instance != null && Gameplay.UI.UIController.instance.IsShowingInventory) return;
        if (Player2InventoryManager.IsAnyGuestPrayerEquipped(idPrayer)) __result = true;
    }
}

[HarmonyPatch(typeof(InventoryManager), "IsPrayerEquipped", new[] { typeof(Prayer) })]
internal static class Inv_IsPrayerObjEquipped_P2_Patch
{
    private static void Postfix(Prayer prayer, ref bool __result)
    {
        if (__result) return;
        if (Gameplay.UI.UIController.instance != null && Gameplay.UI.UIController.instance.IsShowingInventory) return;
        if (prayer != null && Player2InventoryManager.IsAnyGuestPrayerEquipped(prayer.id)) __result = true;
    }
}

[HarmonyPatch(typeof(InventoryManager), "IsSwordEquipped", new[] { typeof(string) })]
internal static class Inv_IsSwordEquipped_P2_Patch
{
    private static void Postfix(string idSword, ref bool __result)
    {
        if (__result) return;
        if (Gameplay.UI.UIController.instance != null && Gameplay.UI.UIController.instance.IsShowingInventory) return;
        if (Player2InventoryManager.IsAnyGuestSwordEquipped(idSword)) __result = true;
    }
}
