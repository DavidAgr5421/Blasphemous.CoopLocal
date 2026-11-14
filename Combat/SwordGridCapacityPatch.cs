using System.Collections.Generic;
using System.Reflection;
using Gameplay.UI.Others.MenuLogic;
using HarmonyLib;

namespace Blasphemous.CoopLocal;

/// <summary>
/// Corrección: el grid de Corazones de Mea Culpa del inventario solo activa `TypeConfiguration.slots` celdas
/// para el tipo Sword (tantas como corazones tiene el juego base). Con los corazones de vanilla + los 4 elementales
/// la lista supera esa cantidad y el último corazón (Maldición) quedaba oculto. Se amplía la capacidad del tipo
/// Sword en 4 celdas (una sola vez) para que quepan los corazones elementales; el grid real tiene 72 celdas.
/// </summary>
[HarmonyPatch(typeof(NewInventory_LayoutGrid), "ShowMaxSlotsForCurrentTabType")]
internal static class NewInventory_LayoutGrid_ShowMaxSlots_ElementalHearts_Patch
{
    private const int ExtraSwordSlots = 4;
    private static readonly FieldInfo CurrentConfigField = Reflect.Field(typeof(NewInventory_LayoutGrid), "currentTypeConfiguration");
    private static readonly FieldInfo CurrentItemTypeField = Reflect.Field(typeof(NewInventory_LayoutGrid), "currentItemType");
    private static readonly List<object> BumpedConfigs = new List<object>();

    private static void Prefix(NewInventory_LayoutGrid __instance)
    {
        try
        {
            if (!CoopGameConfig.EnableElementalUpgrades) return;
            if (CurrentConfigField == null || CurrentItemTypeField == null) return;
            object itemType = CurrentItemTypeField.GetValue(__instance);
            if (itemType == null || itemType.ToString() != "Sword") return;

            object cfg = CurrentConfigField.GetValue(__instance);
            if (cfg == null) return;
            foreach (object done in BumpedConfigs)
                if (ReferenceEquals(done, cfg)) return;

            FieldInfo slotsField = cfg.GetType().GetField("slots", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (slotsField == null) return;
            int slots = (int)slotsField.GetValue(cfg);
            if (slots <= 0) return;   // 0 = sin límite

            slotsField.SetValue(cfg, slots + ExtraSwordSlots);
            BumpedConfigs.Add(cfg);
            DashParryDebugLog.Log($"[Elemental] Capacidad del grid de corazones ampliada: {slots} -> {slots + ExtraSwordSlots}");
        }
        catch (System.Exception ex)
        {
            DashParryDebugLog.Log("[Elemental] Error ampliando grid de corazones: " + ex.Message);
        }
    }
}
