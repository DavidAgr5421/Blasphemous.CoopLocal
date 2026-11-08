using Framework.FrameworkCore;
using CreativeSpore.SmartColliders;
using Framework.Managers;
using Gameplay.GameControllers.AnimationBehaviours.Player.Dead;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using Gameplay.GameControllers.Penitent.Abilities;
using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace Blasphemous.CoopLocal;

internal static class PlayerDeathFixShared
{
    internal static readonly MethodInfo GetPurgeMethod = AccessTools.Method(typeof(Penitent), "GetPurge");
    internal static readonly MethodInfo EnableAbilitiesMethod = AccessTools.Method(typeof(Penitent), "EnableAbilities");
    internal static readonly MethodInfo EnableTraitsMethod = AccessTools.Method(typeof(Penitent), "EnableTraits");

    internal static bool IsRespawningAllPlayers = false;

    internal static bool AreAnyOtherPlayersAlive(Penitent excludedPlayer)
    {
        if (!CoopGameConfig.IsCoopEnabled || CoopGameConfig.PlayerCount <= 1)
        {
            return false;
        }

        Penitent p1 = Core.Logic != null ? Core.Logic.Penitent : null;
        if (p1 != null && p1 != excludedPlayer && !p1.Status.Dead && !Player1DeathState.IsPendingRevive())
        {
            return true;
        }

        foreach (var extra in CoopLocal.GetAllExtraPlayers())
        {
            if (extra != null && extra != excludedPlayer && !extra.Status.Dead)
            {
                int extraSlot = CoopPlayerRegistry.GetPlayerIndex(extra);
                if (extraSlot > 0 && !Player2DeathState.IsPendingRevive(extraSlot))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal static void PutP1InDownedHiddenState(Penitent p1)
    {
        if (p1 == null) return;

        p1.Status.Dead = true;
        p1.DeathEventLaunched = true;

        if (p1.SpriteRenderer != null)
        {
            p1.SpriteRenderer.enabled = false;
        }
        if (p1.Status != null)
        {
            p1.Status.CastShadow = false;
        }

        Transform labelTr = p1.transform.Find("CoopLocalNameLabel");
        if (labelTr != null)
        {
            labelTr.gameObject.SetActive(false);
        }

        if (p1.Shadow != null)
        {
            p1.Shadow.gameObject.SetActive(false);
        }

        if (p1.DamageArea != null)
        {
            p1.DamageArea.enabled = false;
            if (p1.DamageArea.DamageAreaCollider != null)
            {
                p1.DamageArea.DamageAreaCollider.enabled = false;
            }
            p1.DamageArea.IncludeEnemyLayer(false);
        }

        if (p1.Physics != null)
        {
            p1.Physics.EnablePhysics(false);
            p1.Physics.EnableColliders(false);
        }

        if (p1.PlatformCharacterController != null)
        {
            p1.PlatformCharacterController.InstantVelocity = Vector3.zero;
            p1.PlatformCharacterController.enabled = false;
        }

        EnableAbilitiesMethod?.Invoke(p1, new object[] { false });
        EnableTraitsMethod?.Invoke(p1, new object[] { false });
        PlayerLogicBlocker.SetBlocked(p1, true);

        CameraTargetDebugToggle.Apply();
    }

    internal static void RestoreP1FromDownedHiddenState(Penitent p1)
    {
        if (p1 == null) return;

        Player1DeathState.ClearPendingRevive();
        p1.Status.Dead = false;
        p1.DeathEventLaunched = false;
        if (p1.Stats?.Life != null)
        {
            p1.Stats.Life.SetToCurrentMax();
        }

        if (p1.SpriteRenderer != null)
        {
            p1.SpriteRenderer.enabled = true;
        }
        if (p1.Status != null)
        {
            p1.Status.CastShadow = true;
        }

        Transform labelTr = p1.transform.Find("CoopLocalNameLabel");
        if (labelTr != null)
        {
            labelTr.gameObject.SetActive(true);
        }

        if (p1.Shadow != null)
        {
            p1.Shadow.gameObject.SetActive(true);
        }

        if (p1.DamageArea != null)
        {
            p1.DamageArea.enabled = true;
            if (p1.DamageArea.DamageAreaCollider != null)
            {
                p1.DamageArea.DamageAreaCollider.enabled = true;
            }
            p1.DamageArea.IncludeEnemyLayer(true);
        }

        if (p1.Physics != null)
        {
            p1.Physics.EnablePhysics(true);
            p1.Physics.EnableColliders(true);
        }

        if (p1.PlatformCharacterController != null)
        {
            p1.PlatformCharacterController.enabled = true;
        }

        EnableAbilitiesMethod?.Invoke(p1, new object[] { true });
        EnableTraitsMethod?.Invoke(p1, new object[] { true });
        PlayerLogicBlocker.SetBlocked(p1, false);

        if (p1.Animator != null)
        {
            p1.Animator.enabled = true;
            p1.Animator.Play("Idle");
        }

        CameraTargetDebugToggle.Apply();
    }

    internal static void TemporarilyReviveP1ForTransition(Penitent p1)
    {
        if (p1 == null) return;

        p1.Status.Dead = false;
        if (p1.Stats?.Life != null)
        {
            p1.Stats.Life.Current = Mathf.Max(1f, p1.Stats.Life.MaxValue);
        }
        if (p1.Animator != null)
        {
            p1.Animator.enabled = true;
        }
        if (p1.PlatformCharacterController != null)
        {
            p1.PlatformCharacterController.enabled = true;
        }
        if (p1.SpriteRenderer != null)
        {
            p1.SpriteRenderer.enabled = false;
        }
    }

    internal static void ResetAllDeathStatesForRespawn()
    {
        IsRespawningAllPlayers = true;
        Penitent_OnUpdate_P2Death_Patch.IsAllDeadEventLaunched = false;
        DeadScreenWidget_OnDead_Guard_Patch.IsDeadScreenActive = false;
        Player1DeathState.ClearPendingRevive();
        for (int p = 1; p < CoopPlayerRegistry.MaxPlayers; p++)
        {
            Player2DeathState.ClearPendingRevive(p);
        }
        Player2StatsSync.ResetVitalsForRespawn();
        CameraTargetDebugToggle.Apply();
        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info("[CoopDeath] ResetAllDeathStatesForRespawn -> wipe reset and revived all players.", Main.CoopLocal);
        }
    }
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
            PlayerDeathFixShared.EnableAbilitiesMethod?.Invoke(__instance, new object[] { false });
            PlayerDeathFixShared.EnableTraitsMethod?.Invoke(__instance, new object[] { false });
            if (__instance.DamageArea != null)
            {
                __instance.DamageArea.IncludeEnemyLayer(include: false);
            }

            bool anyOtherAlive = PlayerDeathFixShared.AreAnyOtherPlayersAlive(__instance);
            if (!anyOtherAlive)
            {
                Core.Events.SetFlag("CHERUB_RESPAWN", b: true);
            }
            else if (__instance == Core.Logic.Penitent)
            {
                Player1DeathState.MarkDeadPendingRevive();
            }
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
        if ((Core.LevelManager != null && Core.LevelManager.InsideChangeLevel) || PlayerDeathFixShared.IsRespawningAllPlayers)
        {
            return false;
        }

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

        bool anyOtherAlive = PlayerDeathFixShared.AreAnyOtherPlayersAlive(__instance);

        if (anyOtherAlive)
        {
            if (isP1)
            {
                AnimatorStateInfo animState = __instance.Animator != null && __instance.Animator.isInitialized ? __instance.Animator.GetCurrentAnimatorStateInfo(0) : default;
                bool isImpaled = __instance.IsImpaled;
                bool isDeadAnimDone = (!__instance.Animator.enabled) || (animState.IsName("Death") && animState.normalizedTime >= 0.95f) || isImpaled;
                if (isDeadAnimDone)
                {
                    PlayerDeathFixShared.PutP1InDownedHiddenState(__instance);
                }
            }
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
        PlayerDeathFixShared.ResetAllDeathStatesForRespawn();
    }
}

[HarmonyPatch(typeof(SpawnManager), "Respawn")]
internal static class SpawnManager_Respawn_Patch
{
    private static void Prefix()
    {
        PlayerDeathFixShared.ResetAllDeathStatesForRespawn();
    }
}

[HarmonyPatch(typeof(SpawnManager), "RespawnMiriamSameLevel")]
internal static class SpawnManager_RespawnMiriamSameLevel_Patch
{
    private static void Prefix()
    {
        PlayerDeathFixShared.ResetAllDeathStatesForRespawn();
    }
}

[HarmonyPatch(typeof(SpawnManager), "RespawnSafePosition")]
internal static class SpawnManager_RespawnSafePosition_Patch
{
    private static void Prefix()
    {
        PlayerDeathFixShared.ResetAllDeathStatesForRespawn();
    }
}

[HarmonyPatch(typeof(PersistentManager), "LoadGame")]
internal static class PersistentManager_LoadGame_CoopDeath_Patch
{
    private static void Prefix()
    {
        PlayerDeathFixShared.ResetAllDeathStatesForRespawn();
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

[HarmonyPatch(typeof(PlayerDeathAnimationBehaviour), "OnStateUpdate")]
internal static class PlayerDeathAnimationBehaviour_OnStateUpdate_P1Fix_Patch
{
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(PlayerDeathAnimationBehaviour), "_penitent");

    private static void Postfix(Animator animator, AnimatorStateInfo stateInfo, PlayerDeathAnimationBehaviour __instance)
    {
        if (stateInfo.normalizedTime >= 0.95f)
        {
            Penitent owner = (Penitent)PenitentField.GetValue(__instance);
            if (owner == null) owner = animator.GetComponentInParent<Penitent>();
            if (owner != null && owner.Status.Dead)
            {
                if (owner == Core.Logic.Penitent && PlayerDeathFixShared.AreAnyOtherPlayersAlive(owner))
                {
                    PlayerDeathFixShared.PutP1InDownedHiddenState(owner);
                }
            }
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

// --------------------------------------------------------------------------------------
// Parches de CheckTrap: Evita NRE de BlobShadow y muertes indebidas durante cambios de nivel
// --------------------------------------------------------------------------------------

[HarmonyPatch(typeof(CheckTrap), "OnTriggerEnter2D")]
internal static class CheckTrap_OnTriggerEnter2D_Patch
{
    private static bool Prefix(CheckTrap __instance)
    {
        if (__instance.EntityOwner == null || __instance.EntityOwner.Status.Dead) return false;
        if (Core.LevelManager != null && Core.LevelManager.InsideChangeLevel) return false;
        if (PlayerDeathFixShared.IsRespawningAllPlayers) return false;
        return true;
    }
}

[HarmonyPatch(typeof(CheckTrap), "OnTriggerStay2D")]
internal static class CheckTrap_OnTriggerStay2D_Patch
{
    private static bool Prefix(CheckTrap __instance)
    {
        if (__instance.EntityOwner == null || __instance.EntityOwner.Status.Dead) return false;
        if (Core.LevelManager != null && Core.LevelManager.InsideChangeLevel) return false;
        if (PlayerDeathFixShared.IsRespawningAllPlayers) return false;
        return true;
    }
}

[HarmonyPatch(typeof(CheckTrap), "SpikeTrapDamage")]
internal static class CheckTrap_SpikeTrapDamage_Patch
{
    private static bool Prefix(CheckTrap __instance)
    {
        if (__instance.EntityOwner == null || __instance.EntityOwner.Status.Dead) return false;
        if (Core.LevelManager != null && Core.LevelManager.InsideChangeLevel) return false;
        if (PlayerDeathFixShared.IsRespawningAllPlayers) return false;
        return true;
    }
}

[HarmonyPatch(typeof(CheckTrap), "AbyssTrapDamage")]
internal static class CheckTrap_AbyssTrapDamage_Patch
{
    private static bool Prefix(CheckTrap __instance)
    {
        if (__instance.EntityOwner == null || __instance.EntityOwner.Status.Dead) return false;
        if (Core.LevelManager != null && Core.LevelManager.InsideChangeLevel) return false;
        if (PlayerDeathFixShared.IsRespawningAllPlayers) return false;

        Penitent penitent = __instance.EntityOwner as Penitent;
        if (penitent != null && PlayerDeathFixShared.AreAnyOtherPlayersAlive(penitent))
        {
            if (penitent.Stats.Life.Current > 0f)
            {
                penitent.Stats.Life.Current = 0f;
            }
            penitent.KillInstanteneously();
            PlatformCharacterController controller = penitent.GetComponentInChildren<PlatformCharacterController>();
            if (controller != null)
            {
                controller.enabled = false;
            }
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(CheckTrap), "DeathByHWTrap")]
internal static class CheckTrap_DeathByHWTrap_Patch
{
    private static bool Prefix(CheckTrap __instance)
    {
        Penitent penitent = __instance.EntityOwner as Penitent;
        if (penitent == null || penitent.Status.Dead) return false;
        if (Core.LevelManager != null && Core.LevelManager.InsideChangeLevel) return false;
        if (PlayerDeathFixShared.IsRespawningAllPlayers) return false;

        Hit hit = default;
        hit.DamageAmount = penitent.Stats.Life.Current;
        hit.AttackingEntity = penitent.gameObject;
        hit.DamageType = DamageArea.DamageType.Heavy;
        penitent.DamageArea.TakeDamage(hit, force: true);
        return false;
    }
}

// --------------------------------------------------------------------------------------
// Parche de DrivePlayer: Evita bloqueos en interacciones automáticas cuando P1 está muerto
// --------------------------------------------------------------------------------------

[HarmonyPatch(typeof(DrivePlayer), "MoveToPosition")]
internal static class DrivePlayer_MoveToPosition_DeadP1_Patch
{
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(DrivePlayer), "_penitent");
    private static readonly FieldInfo CastingField = AccessTools.Field(typeof(DrivePlayer), "<Casting>k__BackingField");

    private static bool Prefix(DrivePlayer __instance, Vector3 position, EntityOrientation orientation, bool autoRun, Action onStopMotion)
    {
        Penitent owner = (Penitent)PenitentField.GetValue(__instance);
        if (owner == null) owner = Core.Logic?.Penitent;
        if (owner != null && (owner.Status.Dead || Player1DeathState.IsPendingRevive()))
        {
            owner.transform.position = position;
            owner.SetOrientation(orientation);
            CastingField?.SetValue(__instance, false);
            onStopMotion?.Invoke();
            return false;
        }
        return true;
    }
}
