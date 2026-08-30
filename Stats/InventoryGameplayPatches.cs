using Framework.Inventory;
using Framework.Managers;
using Gameplay.GameControllers.Penitent;
using Gameplay.GameControllers.Penitent.Abilities;
using HarmonyLib;

namespace Blasphemous.CoopLocal;

// Fase 3 — Gameplay pasivo: si P2 tiene equipado el bead/prayer/sword, que cuente.
// MVP: si cualquiera de los dos lo tiene equipado, IsEquipped devuelve true (overlay).
// Luego se puede refinar a per-entity con stack context.
[HarmonyPatch(typeof(InventoryManager), "IsRosaryBeadEquipped", new[] { typeof(string) })]
internal static class Inv_IsBeadEquipped_P2_Patch
{
    private static void Postfix(string idRosaryBead, ref bool __result)
    {
        if (__result) return;
        if (Player2InventoryManager.IsBeadEquipped(idRosaryBead)) __result = true;
    }
}
[HarmonyPatch(typeof(InventoryManager), "IsRosaryBeadEquipped", new[] { typeof(RosaryBead) })]
internal static class Inv_IsBeadObjEquipped_P2_Patch
{
    private static void Postfix(RosaryBead bead, ref bool __result)
    {
        if (__result) return;
        if (bead != null && Player2InventoryManager.IsBeadEquipped(bead.id)) __result = true;
    }
}
[HarmonyPatch(typeof(InventoryManager), "IsPrayerEquipped", new[] { typeof(string) })]
internal static class Inv_IsPrayerEquipped_P2_Patch
{
    private static void Postfix(string idPrayer, ref bool __result)
    {
        if (__result) return;
        if (Player2InventoryManager.IsPrayerEquipped(idPrayer)) __result = true;
    }
}
[HarmonyPatch(typeof(InventoryManager), "IsPrayerEquipped", new[] { typeof(Prayer) })]
internal static class Inv_IsPrayerObjEquipped_P2_Patch
{
    private static void Postfix(Prayer prayer, ref bool __result)
    {
        if (__result) return;
        if (prayer != null && Player2InventoryManager.IsPrayerEquipped(prayer.id)) __result = true;
    }
}
[HarmonyPatch(typeof(InventoryManager), "IsSwordEquipped", new[] { typeof(string) })]
internal static class Inv_IsSwordEquipped_P2_Patch
{
    private static void Postfix(string idSword, ref bool __result)
    {
        if (__result) return;
        if (Player2InventoryManager.IsSwordEquipped(idSword)) __result = true;
    }
}
[HarmonyPatch(typeof(PrayerUse), "GetEquippedPrayer")]
internal static class PrayerUse_GetEquippedPrayer_P2_Patch
{
    private static void Postfix(PrayerUse __instance, ref Prayer __result)
    {
        Penitent owner = __instance.EntityOwner as Penitent;
        if (owner == null || owner != CoopLocal.Player2) return;
        var p2 = Player2InventoryManager.GetEquippedPrayerObj();
        // Para P2, devolver su propio rezo (null si no tiene nada equipado, sin fallback a P1)
        __result = p2;
    }
}
