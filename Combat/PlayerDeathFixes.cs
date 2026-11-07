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
    private static readonly FieldInfo OnDeadField = AccessTools.Field(typeof(Penitent), "OnDead");
    internal static bool IsAllDeadEventLaunched = false;

    private static bool Prefix(Penitent __instance)
    {
        bool isP1 = (Core.Logic != null && __instance == Core.Logic.Penitent);
        bool isGuest = CoopLocal.IsGuestPlayer(__instance, out int guestSlot);

        if (!isP1 && !isGuest)
        {
            return true;
        }

        __instance.Status.IsVisibleOnCamera = __instance.IsVisible();
        if (!__instance.Status.Dead)
        {
            // Si P1 está vivo, ejecutar su OnUpdate normal
            // Si un guest está vivo, saltar OnUpdate de Penitent (se maneja por su controller)
            return isP1;
        }

        // --- Jugador Muerto ---
        if (!__instance.DeathEventLaunched)
        {
            __instance.DeathEventLaunched = true;
            if (isGuest)
            {
                Player2DeathState.MarkDeadPendingRevive(guestSlot);
            }
            else if (isP1)
            {
                Player1DeathState.MarkDeadPendingRevive();
            }
            CameraTargetDebugToggle.Apply();
        }

        // Comprobar si al menos un jugador sigue con vida en partida cooperativa
        bool isCoop = CoopGameConfig.IsCoopEnabled && CoopGameConfig.PlayerCount > 1;
        bool anyOtherAlive = false;

        if (isCoop)
        {
            Penitent p1 = Core.Logic != null ? Core.Logic.Penitent : null;
            if (p1 != null && p1 != __instance && !p1.Status.Dead && !Player1DeathState.IsPendingRevive())
            {
                anyOtherAlive = true;
            }
            if (!anyOtherAlive)
            {
                foreach (var extra in CoopLocal.GetAllExtraPlayers())
                {
                    if (extra != null && extra != __instance && !extra.Status.Dead)
                    {
                        int extraSlot = CoopPlayerRegistry.GetPlayerIndex(extra);
                        if (extraSlot > 0 && !Player2DeathState.IsPendingRevive(extraSlot))
                        {
                            anyOtherAlive = true;
                            break;
                        }
                    }
                }
            }
        }

        if (anyOtherAlive)
        {
            // Hay al menos un jugador vivo: NO mostrar pantalla de muerte ni cambiar estado a PlayerDead
            return false;
        }

        // --- TODOS LOS JUGADORES HAN MUERTO (Fin de partida / Game Over) ---
        if (!IsAllDeadEventLaunched)
        {
            IsAllDeadEventLaunched = true;
            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info("[CoopDeath] All players dead. Triggering Game Over and DeadScreen once.", Main.CoopLocal);
            }

            if (Core.Logic.CurrentState != LogicStates.PlayerDead)
            {
                Core.Logic.SetState(LogicStates.PlayerDead);
            }
            Core.Logic.PlayerCurrentLife = -1f;

            if (Core.Logic.Penitent != null)
            {
                var del = OnDeadField?.GetValue(Core.Logic.Penitent) as Core.SimpleEvent;
                del?.Invoke();
            }
        }

        return false;
    }
}

[HarmonyPatch(typeof(Penitent), "Respawn")]
internal static class Penitent_Respawn_Patch
{
    private static void Prefix(Penitent __instance)
    {
        Penitent_OnUpdate_P2Death_Patch.IsAllDeadEventLaunched = false;
        Player1DeathState.ClearPendingRevive();
        for (int p = 1; p < CoopPlayerRegistry.MaxPlayers; p++)
        {
            Player2DeathState.ClearPendingRevive(p);
        }
        Player2StatsSync.ResetVitalsForRespawn();
        CameraTargetDebugToggle.Apply();
        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info("[CoopDeath] Penitent.Respawn called -> wipe reset and revived all players.", Main.CoopLocal);
        }
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
