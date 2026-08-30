using Gameplay.GameControllers.Effects.Player.Dash;
using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace Blasphemous.CoopLocal;

// Parte 2 — NRE DashDustGenerator en 3ra transición D02Z01S01.
// Decompilado confirma _penitent seteado solo en OnStart via base.EntityOwner; si EntityOwner
// es null en ese momento (prefab Instantiate antes de que Trait.Awake resuelva EntityOwner)
// o si el coroutine DelayStopDash sobrevive a la destrucción del GO viejo (P1 recreado por
// SpawnManager.CreatePlayer), _penitent queda null y GetDashDustPosition() hace
// _penitent.DamageArea... -> NRE cada frame hasta que la nueva escena crea nuevo P1.
// Fix: re-resolver owner por GetComponentInParent si _penitent == null, y si sigue null o
// DamageArea null, no ejecutar (return early) en vez de tirar NRE.
internal static class DashDustFixShared
{
    internal static readonly FieldInfo PenitentField = AccessTools.Field(typeof(DashDustGenerator), "_penitent");
}

[HarmonyPatch(typeof(DashDustGenerator), "GetDashDustPosition")]
internal static class DashDust_GetPosition_Patch
{
    private static bool Prefix(DashDustGenerator __instance, ref Vector3 __result)
    {
        var penitent = DashDustFixShared.PenitentField.GetValue(__instance) as Gameplay.GameControllers.Penitent.Penitent;
        if (penitent == null)
        {
            penitent = __instance.GetComponentInParent<Gameplay.GameControllers.Penitent.Penitent>();
            if (penitent != null) DashDustFixShared.PenitentField.SetValue(__instance, penitent);
        }
        if (penitent == null || penitent.DamageArea == null || penitent.DamageArea.DamageAreaCollider == null)
        {
            __result = Vector3.zero;
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(DashDustGenerator), "GetStopDashDust", new[] { typeof(float) })]
internal static class DashDust_GetStopDustDelay_Patch
{
    private static bool Prefix(DashDustGenerator __instance, float delay)
    {
        var penitent = DashDustFixShared.PenitentField.GetValue(__instance) as Gameplay.GameControllers.Penitent.Penitent;
        if (penitent == null)
        {
            penitent = __instance.GetComponentInParent<Gameplay.GameControllers.Penitent.Penitent>();
            if (penitent != null) DashDustFixShared.PenitentField.SetValue(__instance, penitent);
            else return false;
        }
        if (penitent.DamageArea == null) return false;
        return true;
    }
}

[HarmonyPatch(typeof(DashDustGenerator), "GetStopDashDust", new System.Type[] { })]
internal static class DashDust_GetStopDust_NoDelay_Patch
{
    private static bool Prefix(DashDustGenerator __instance)
    {
        var penitent = DashDustFixShared.PenitentField.GetValue(__instance) as Gameplay.GameControllers.Penitent.Penitent;
        if (penitent == null)
        {
            penitent = __instance.GetComponentInParent<Gameplay.GameControllers.Penitent.Penitent>();
            if (penitent != null) DashDustFixShared.PenitentField.SetValue(__instance, penitent);
            else return false;
        }
        if (penitent.DamageArea == null || penitent.DamageArea.DamageAreaCollider == null) return false;
        return true;
    }
}

[HarmonyPatch(typeof(DashDustGenerator), "GetStartDashDust")]
internal static class DashDust_GetStartDust_Patch
{
    private static bool Prefix(DashDustGenerator __instance)
    {
        var penitent = DashDustFixShared.PenitentField.GetValue(__instance) as Gameplay.GameControllers.Penitent.Penitent;
        if (penitent == null)
        {
            penitent = __instance.GetComponentInParent<Gameplay.GameControllers.Penitent.Penitent>();
            if (penitent != null) DashDustFixShared.PenitentField.SetValue(__instance, penitent);
            else return false;
        }
        if (penitent.DamageArea == null) return false;
        return true;
    }
}
