using Framework.Managers;
using Gameplay.GameControllers.AnimationBehaviours.Player.Dead;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace Blasphemous.CoopLocal;

// Ronda 63: P2 muerto no debe tocar P1 ni estado global.
internal static class PlayerDeathFixShared
{
    internal static readonly MethodInfo GetPurgeMethod = AccessTools.Method(typeof(Penitent), "GetPurge");
    internal static readonly MethodInfo EnableAbilitiesMethod = AccessTools.Method(typeof(Penitent), "EnableAbilities");
    internal static readonly MethodInfo EnableTraitsMethod = AccessTools.Method(typeof(Penitent), "EnableTraits");
}

[HarmonyPatch(typeof(Penitent), "OnEntityDead")]
internal static class Penitent_OnEntityDead_P2Fix_Patch
{
    private static bool Prefix(Penitent __instance, Entity entity)
    {
        Enemy enemy = entity as Enemy;
        if ((bool)enemy)
        {
            PlayerDeathFixShared.GetPurgeMethod.Invoke(__instance, new object[] { enemy });
        }
        Penitent penitent = entity as Penitent;
        if (penitent != null && penitent == __instance)
        {
            // reflection para privados EnableAbilities/EnableTraits/DamageArea (cacheados, ver arriba)
            PlayerDeathFixShared.EnableAbilitiesMethod?.Invoke(__instance, new object[] { false });
            PlayerDeathFixShared.EnableTraitsMethod?.Invoke(__instance, new object[] { false });
            if (__instance.DamageArea != null)
            {
                __instance.DamageArea.IncludeEnemyLayer(include: false);
            }
            // vanilla tambien hace Core.Events.SetFlag("CHERUB_RESPAWN", true) dentro del if
            Core.Events.SetFlag("CHERUB_RESPAWN", b: true);
        }
        return false;
    }
}

[HarmonyPatch(typeof(Penitent), "OnUpdate")]
internal static class Penitent_OnUpdate_P2Death_Patch
{
    private static bool Prefix(Penitent __instance)
    {
        if (__instance != CoopLocal.Player2)
        {
            return true;
        }
        // Solo Status.IsVisibleOnCamera + DeathEventLaunched -> MarkDeadPendingRevive, sin SetState global ni OnDead
        __instance.Status.IsVisibleOnCamera = __instance.IsVisible();
        if (!__instance.Status.Dead)
        {
            return false;
        }
        if (!__instance.DeathEventLaunched)
        {
            __instance.DeathEventLaunched = true;
            Player2DeathState.MarkDeadPendingRevive();
        }
        return false;
    }
}

[HarmonyPatch(typeof(PlayerDeathAnimationBehaviour), "OnStateEnter")]
internal static class PlayerDeathAnimationBehaviour_OnStateEnter_P2Fix_Patch
{
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(PlayerDeathAnimationBehaviour), "_penitent");
    private static void Prefix(Animator animator, PlayerDeathAnimationBehaviour __instance)
    {
        Penitent owner = animator.GetComponentInParent<Penitent>();
        if (owner != null)
        {
            PenitentField.SetValue(__instance, owner);
        }
    }
}

[HarmonyPatch(typeof(PlayerDeathFallBehaviour), "OnStateEnter")]
internal static class PlayerDeathFallBehaviour_OnStateEnter_P2Fix_Patch
{
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(PlayerDeathFallBehaviour), "_penitent");
    private static void Prefix(Animator animator, PlayerDeathFallBehaviour __instance)
    {
        Penitent owner = animator.GetComponentInParent<Penitent>();
        if (owner != null)
        {
            PenitentField.SetValue(__instance, owner);
        }
    }
}

[HarmonyPatch(typeof(PlayerDeathSpikeBehaviour), "OnStateEnter")]
internal static class PlayerDeathSpikeBehaviour_OnStateEnter_P2Fix_Patch
{
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(PlayerDeathSpikeBehaviour), "_penitent");
    private static void Prefix(Animator animator, PlayerDeathSpikeBehaviour __instance)
    {
        Penitent owner = animator.GetComponentInParent<Penitent>();
        if (owner != null)
        {
            PenitentField.SetValue(__instance, owner);
        }
    }
}
