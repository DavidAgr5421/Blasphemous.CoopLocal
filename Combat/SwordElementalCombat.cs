using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Framework.FrameworkCore;
using Framework.Inventory;
using Framework.Managers;
using Gameplay.GameControllers.Effects.Entity;
using Gameplay.GameControllers.Enemies.Framework.Damage;
using Gameplay.GameControllers.Enemies.Framework.IA;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using Gameplay.GameControllers.Penitent.Attack;
using Gameplay.UI.Others.Buttons;
using Gameplay.UI.Others.MenuLogic;
using HarmonyLib;
using Tools.Items;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Blasphemous.CoopLocal;

// =========================================================================
// Aplicación en Combate: Veneno (I-III) y Sangrado (I-III) sobre Enemigos
// =========================================================================
internal static class SwordElementalCombatBridge
{
    internal static int GetThresholdHitWeight(Penitent attacker, Hit hit, Gameplay.GameControllers.Entities.Weapon.Weapon weapon = null)
    {
        if (attacker == null) return 1;

        bool isCharged = attacker.ReleaseChargedAttack
            || (attacker.Animator != null && attacker.Animator.GetCurrentAnimatorStateInfo(0).IsName("Charged Attack"))
            || (weapon is Gameplay.GameControllers.Penitent.Attack.ChargedAttackProjectile)
            || (hit.AttackingEntity != null && hit.AttackingEntity.GetComponentInChildren<Gameplay.GameControllers.Penitent.Attack.ChargedAttackProjectile>() != null);
        if (isCharged) return 4;

        bool isParryHit = (attacker.Parry != null && attacker.Parry.SuccessParry)
            || (attacker.ActiveRiposte != null && (attacker.ActiveRiposte.IsTriggeredRiposte || attacker.ActiveRiposte.Casting))
            || (attacker.Animator != null && (
                attacker.Animator.GetBool("PARRY") ||
                attacker.Animator.GetCurrentAnimatorStateInfo(0).IsName("ParrySuccess") ||
                attacker.Animator.GetCurrentAnimatorStateInfo(0).IsName("ParryRepost")));
        if (isParryHit) return 2;

        return 1;
    }

    internal static void TryApplyElementalStatus(Enemy enemy, Penitent attacker, int hitWeight = 1)
    {
        if (!CoopGameConfig.EnableElementalUpgrades) return;
        if (EnemyDamageArea_TakeDamage_ElementalStatus_Patch.IsApplyingStatusDamage) return;
        if (enemy == null || enemy.Status.Dead || attacker == null) return;

        int playerIdx = CoopPlayerRegistry.GetPlayerIndex(attacker);
        if (playerIdx < 0) return;

        EnemyElementalStatusController ctrl = enemy.gameObject.GetComponent<EnemyElementalStatusController>()
            ?? enemy.gameObject.AddComponent<EnemyElementalStatusController>();

        // 1. Comprobar Veneno: requiere tener equipado HE_VENENO + tener desbloqueado VENENO_1..3 en el árbol
        if (SwordElementalUpgrades.IsHeartEquipped(playerIdx, SwordElementalUpgrades.HeartVenenoId))
        {
            int poisonTier = SwordElementalUpgrades.GetUnlockedVenenoTier(playerIdx);
            if (poisonTier > 0)
            {
                ctrl.ApplyPoison(enemy, attacker, poisonTier, hitWeight);
            }
        }

        // 2. Comprobar Sangrado: requiere tener equipado HE_SANGRADO + tener desbloqueado SANGRADO_1..3 en el árbol
        if (SwordElementalUpgrades.IsHeartEquipped(playerIdx, SwordElementalUpgrades.HeartSangradoId))
        {
            int bleedTier = SwordElementalUpgrades.GetUnlockedSangradoTier(playerIdx);
            if (bleedTier > 0)
            {
                ctrl.AddBleedHit(enemy, attacker, bleedTier, hitWeight);
            }
        }

        // 3. Electricidad y 4. Maldición: efectos host-only (en cliente online el host los resuelve)
        if (Blasphemous.CoopLocal.MultiPlayer.CoopNetworkManager.IsOnlineClient) return;

        if (SwordElementalUpgrades.IsHeartEquipped(playerIdx, SwordElementalUpgrades.HeartElectricoId))
        {
            int electricTier = SwordElementalUpgrades.GetUnlockedElectricoTier(playerIdx);
            if (electricTier > 0)
            {
                ctrl.AddElectricHit(enemy, attacker, electricTier, hitWeight);
            }
        }

        if (SwordElementalUpgrades.IsHeartEquipped(playerIdx, SwordElementalUpgrades.HeartMaldicionId))
        {
            int curseTier = SwordElementalUpgrades.GetUnlockedMaldicionTier(playerIdx);
            if (curseTier > 0)
            {
                ctrl.ApplyCurseHit(enemy, attacker, playerIdx, curseTier, hitWeight);
            }
        }
    }
}

// Maldición: choke point de daño al jugador. Entity.GetReducedDamage(Hit) no es virtual y lo llama PenitentDamageArea.RaiseDamageEvent
// (P1 y clones). Si el atacante es un Enemy maldito, el daño resultante se multiplica por (1 - reducción).
[HarmonyPatch(typeof(Entity), "GetReducedDamage")]
internal static class Entity_GetReducedDamage_Curse_Patch
{
    private static void Postfix(Entity __instance, Hit hit, ref float __result)
    {
        try
        {
            if (!CoopGameConfig.EnableElementalUpgrades) return;
            if (!(__instance is Penitent)) return;
            if (Blasphemous.CoopLocal.MultiPlayer.CoopNetworkManager.IsOnlineClient) return;
            if (hit.AttackingEntity == null) return;

            Enemy attackerEnemy = hit.AttackingEntity.GetComponent<Enemy>() ?? hit.AttackingEntity.GetComponentInParent<Enemy>();
            if (attackerEnemy == null) return;

            EnemyElementalStatusController ctrl = attackerEnemy.GetComponent<EnemyElementalStatusController>();
            if (ctrl == null) return;

            float mult = ctrl.GetCurseDamageMultiplier();
            if (mult >= 0.999f) return;

            float before = __result;
            __result = before * mult;
            DashParryDebugLog.LogVerbose($"[Curse] {attackerEnemy.name} -> {__instance.name}: daño {before:F1} -> {__result:F1} (x{mult:F2})");
        }
        catch (Exception ex)
        {
            DashParryDebugLog.LogVerbose("[Curse] Error en GetReducedDamage postfix: " + ex.Message);
        }
    }
}

// Escalado de daño enemigo por sala/zona + jugadores. Prioridad Last: corre después del parche de Maldición.
// Multiplicativo: un resultado 0 (parry/guard) sigue en 0.
[HarmonyPatch(typeof(Entity), "GetReducedDamage")]
internal static class Entity_GetReducedDamage_RoomScaling_Patch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(Entity __instance, Hit hit, ref float __result)
    {
        try
        {
            if (!(__instance is Penitent)) return;
            if (Blasphemous.CoopLocal.MultiPlayer.CoopNetworkManager.IsOnlineClient) return;
            if (!CoopGameConfig.IsCoopEnabled) return;
            if (__result <= 0f) return;
            if (hit.AttackingEntity == null) return;

            Enemy attackerEnemy = hit.AttackingEntity.GetComponent<Enemy>() ?? hit.AttackingEntity.GetComponentInParent<Enemy>();
            if (attackerEnemy == null) return;
            if (attackerEnemy is Combat.TestDummyEnemy || (!string.IsNullOrEmpty(attackerEnemy.Id) && attackerEnemy.Id.StartsWith("DUMMY", StringComparison.OrdinalIgnoreCase))) return;

            float f = Combat.CoopEnemyScaling.GetRoomDmgFactor();
            if (Mathf.Approximately(f, 1f)) return;

            float before = __result;
            __result = before * f;
            DashParryDebugLog.LogVerbose($"[RoomScaling] {attackerEnemy.name} -> {__instance.name}: daño {before:F1} -> {__result:F1} (x{f:F2})");
        }
        catch (Exception ex)
        {
            DashParryDebugLog.LogVerbose("[RoomScaling] Error en GetReducedDamage postfix: " + ex.Message);
        }
    }
}

// Parche maestro sobre Weapon.AttackDamageableEntities: cubre TODAS las entidades golpeadas por la espada,
// incluyendo enemigos con escudo o campana (BellCarrier, ShieldMaiden, GhostKnight) cuyos colliders frontales desvían el daño físico
[HarmonyPatch(typeof(Gameplay.GameControllers.Entities.Weapon.Weapon), "AttackDamageableEntities")]
internal static class Weapon_AttackDamageableEntities_ElementalStatus_Patch
{
    private static readonly FieldInfo DamageableEntitiesField = Reflect.Field(typeof(Gameplay.GameControllers.Entities.Weapon.Weapon), "DamageableEntities");

    private static void Prefix(Gameplay.GameControllers.Entities.Weapon.Weapon __instance, Hit weaponHit, List<IDamageable> ___DamageableEntities)
    {
        try
        {
            if (!CoopGameConfig.EnableElementalUpgrades) return;
            if (EnemyDamageArea_TakeDamage_ElementalStatus_Patch.IsApplyingStatusDamage) return;

            Penitent attacker = Blasphemous.CoopLocal.PrayerAttackOwnerTracker.ResolveAttacker(weaponHit.AttackingEntity)
                ?? __instance.GetComponentInParent<Penitent>();
            if (attacker == null) return;

            int hitWeight = SwordElementalCombatBridge.GetThresholdHitWeight(attacker, weaponHit, __instance);

            List<IDamageable> targets = ___DamageableEntities ?? (DamageableEntitiesField?.GetValue(__instance) as List<IDamageable>);
            if (targets == null || targets.Count == 0) return;

            for (int i = 0; i < targets.Count; i++)
            {
                var d = targets[i];
                if (d == null) continue;

                Enemy enemy = (d as Enemy) ?? (d as Component)?.GetComponentInParent<Enemy>() ?? (d as Component)?.GetComponentInChildren<Enemy>();
                if (enemy == null && d is Component comp)
                {
                    enemy = comp.gameObject.GetComponentInParent<Enemy>() ?? comp.gameObject.GetComponentInChildren<Enemy>();
                }

                if (enemy != null && !enemy.Status.Dead)
                {
                    SwordElementalCombatBridge.TryApplyElementalStatus(enemy, attacker, hitWeight);
                }
            }
        }
        catch (Exception ex)
        {
            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Error($"[ElementalStatus] Error en Weapon.AttackDamageableEntities prefix: {ex.Message}", Main.CoopLocal);
            }
        }
    }
}

[HarmonyPatch(typeof(EnemyDamageArea), "TakeDamage")]
internal static class EnemyDamageArea_TakeDamage_ElementalStatus_Patch
{
    internal static bool IsApplyingStatusDamage;

    private static void Postfix(EnemyDamageArea __instance, Hit hit)
    {
        if (!CoopGameConfig.EnableElementalUpgrades) return;
        if (IsApplyingStatusDamage) return;

        Penitent attacker = Blasphemous.CoopLocal.PrayerAttackOwnerTracker.ResolveAttacker(hit.AttackingEntity);
        if (attacker == null) return;

        Enemy enemy = __instance.GetComponent<Enemy>() ?? __instance.GetComponentInParent<Enemy>();
        if (enemy == null || enemy.Status.Dead) return;

        int hitWeight = SwordElementalCombatBridge.GetThresholdHitWeight(attacker, hit, null);
        SwordElementalCombatBridge.TryApplyElementalStatus(enemy, attacker, hitWeight);
    }
}
