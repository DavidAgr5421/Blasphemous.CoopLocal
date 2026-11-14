using System;
using Framework.Inventory;
using Framework.Managers;
using Gameplay.GameControllers.Penitent;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.CoopLocal;

// Comprobaciones de objetos equipados (beads / prayers / swords / relics) en gameplay.
//
// Vanilla solo conoce el inventario de P1 y sus consultas IsXEquipped no reciben "quién pregunta". El mod resuelve la pregunta
// según el CONTEXTO de la llamada (EquipContext), de forma explícita y por autor:
//   - Contexto de dueño (Owner):   la consulta es de un penitente concreto (p. ej. aterrizaje fuerte de P3) => se responde con SU equipo.
//   - Contexto global (AnyPlayer):  lógica de mundo/puzles (condiciones Playmaker, geometría de reliquia) => vale si CUALQUIERA lo tiene.
//   - Sin contexto:                 llamada vanilla sobre P1 (rezos/efectos que usan Core.Logic.Penitent) => respuesta vanilla de P1.
// Antes todo se trataba como "global" y el objeto de un invitado afectaba a todos los jugadores (incluido P1).
internal static class EquipContext
{
    internal enum Kind { Bead, Prayer, Sword, Relic }

    [ThreadStatic] private static Penitent owner;
    [ThreadStatic] private static int anyDepth;

    internal static void EnterOwner(Penitent p) { owner = p; }
    internal static void ExitOwner() { owner = null; }
    internal static void EnterAny() { anyDepth++; }
    internal static void ExitAny() { if (anyDepth > 0) anyDepth--; }

    /// <summary>Ajusta el resultado vanilla (equipo de P1) según el contexto activo.</summary>
    internal static void Resolve(Kind kind, string id, ref bool result)
    {
        if (string.IsNullOrEmpty(id)) return;
        // Mientras el inventario de P1 está abierto no superponer equipo de invitados (la cuadrícula marcaría objetos ajenos).
        if (Gameplay.UI.UIController.instance != null && Gameplay.UI.UIController.instance.IsShowingInventory) return;

        if (owner != null)
        {
            CoopPlayerContext ctx = CoopPlayers.Of(owner);
            if (ctx != null && ctx.IsGuest) result = Has(kind, id, ctx); // P1: vanilla
            return;
        }
        if (anyDepth > 0 && !result)
        {
            foreach (CoopPlayerContext ctx in CoopPlayers.All)
            {
                if (ctx.IsGuest && Has(kind, id, ctx)) { result = true; return; }
            }
        }
    }

    private static bool Has(Kind kind, string id, CoopPlayerContext ctx)
    {
        switch (kind)
        {
            case Kind.Bead: return ctx.HasBead(id);
            case Kind.Prayer: return ctx.HasPrayer(id);
            case Kind.Sword: return ctx.HasSword(id);
            default: return ctx.HasRelic(id);
        }
    }
}

// ---------------- Resolución de las consultas vanilla ----------------
[HarmonyPatch(typeof(InventoryManager), "IsRosaryBeadEquipped", new[] { typeof(string) })]
internal static class Inv_IsBeadEquipped_Ctx_Patch
{
    private static void Postfix(string idRosaryBead, ref bool __result) => EquipContext.Resolve(EquipContext.Kind.Bead, idRosaryBead, ref __result);
}

[HarmonyPatch(typeof(InventoryManager), "IsRosaryBeadEquipped", new[] { typeof(RosaryBead) })]
internal static class Inv_IsBeadObjEquipped_Ctx_Patch
{
    private static void Postfix(RosaryBead bead, ref bool __result) { if (bead != null) EquipContext.Resolve(EquipContext.Kind.Bead, bead.id, ref __result); }
}

[HarmonyPatch(typeof(InventoryManager), "IsPrayerEquipped", new[] { typeof(string) })]
internal static class Inv_IsPrayerEquipped_Ctx_Patch
{
    private static void Postfix(string idPrayer, ref bool __result) => EquipContext.Resolve(EquipContext.Kind.Prayer, idPrayer, ref __result);
}

[HarmonyPatch(typeof(InventoryManager), "IsPrayerEquipped", new[] { typeof(Prayer) })]
internal static class Inv_IsPrayerObjEquipped_Ctx_Patch
{
    private static void Postfix(Prayer prayer, ref bool __result) { if (prayer != null) EquipContext.Resolve(EquipContext.Kind.Prayer, prayer.id, ref __result); }
}

[HarmonyPatch(typeof(InventoryManager), "IsSwordEquipped", new[] { typeof(string) })]
internal static class Inv_IsSwordEquipped_Ctx_Patch
{
    private static void Postfix(string idSword, ref bool __result) => EquipContext.Resolve(EquipContext.Kind.Sword, idSword, ref __result);
}

[HarmonyPatch(typeof(InventoryManager), "IsRelicEquipped", new[] { typeof(string) })]
internal static class Inv_IsRelicEquipped_Ctx_Patch
{
    private static void Postfix(string idRelic, ref bool __result) => EquipContext.Resolve(EquipContext.Kind.Relic, idRelic, ref __result);
}

[HarmonyPatch(typeof(InventoryManager), "IsRelicEquipped", new[] { typeof(Relic) })]
internal static class Inv_IsRelicObjEquipped_Ctx_Patch
{
    private static void Postfix(Relic relic, ref bool __result) { if (relic != null) EquipContext.Resolve(EquipContext.Kind.Relic, relic.id, ref __result); }
}

// ---------------- Contextos de DUEÑO: consultas que pertenecen a un penitente concreto ----------------
// Aterrizaje fuerte: RB15 evita acelerar la animación. Debe valer para quien aterriza.
[HarmonyPatch(typeof(Gameplay.GameControllers.AnimationBehaviours.Player.Jump.HardLandingBehaviour), "OnStateUpdate")]
internal static class HardLanding_OnStateUpdate_OwnerCtx_Patch
{
    private static void Prefix(Penitent ____penitent) => EquipContext.EnterOwner(____penitent);
    private static Exception Finalizer(Exception __exception) { EquipContext.ExitOwner(); return __exception; }
}

// PR04 (drenar vida al golpear): se cura el dueño de la espada que golpea.
[HarmonyPatch(typeof(Gameplay.GameControllers.Penitent.Attack.PenitentSword), "OnHit")]
internal static class PenitentSword_OnHit_OwnerCtx_Patch
{
    private static void Prefix(Penitent ____penitent) => EquipContext.EnterOwner(____penitent);
    private static Exception Finalizer(Exception __exception) { EquipContext.ExitOwner(); return __exception; }
}

// ---------------- Contextos GLOBALES: lógica de mundo (cualquier jugador que lo tenga equipado cuenta) ----------------
[HarmonyPatch(typeof(Tools.Playmaker2.Action.IsItemEquipped), "executeAction")]
internal static class Playmaker_IsItemEquipped_AnyCtx_Patch
{
    private static void Prefix() => EquipContext.EnterAny();
    private static Exception Finalizer(Exception __exception) { EquipContext.ExitAny(); return __exception; }
}

[HarmonyPatch(typeof(Tools.Playmaker2.Condition.ItemIsEquiped), "executeAction")]
internal static class Playmaker_ItemIsEquiped_AnyCtx_Patch
{
    private static void Prefix() => EquipContext.EnterAny();
    private static Exception Finalizer(Exception __exception) { EquipContext.ExitAny(); return __exception; }
}

// Geometría revelada por la reliquia RE10 (muros ocultos): si algún jugador la lleva, se revela cerca de P1.
[HarmonyPatch(typeof(Tools.Level.Actionables.TileableGeo), "CheckDistanceToPenitent")]
internal static class TileableGeo_CheckDistance_AnyCtx_Patch
{
    private static void Prefix() => EquipContext.EnterAny();
    private static Exception Finalizer(Exception __exception) { EquipContext.ExitAny(); return __exception; }
}
