using System;
using System.Collections.Generic;
using System.Reflection;
using Framework.FrameworkCore;
using Framework.Managers;
using Gameplay.GameControllers.Bosses.Amanecidas;
using Gameplay.GameControllers.Bosses.Crisanta;
using Gameplay.GameControllers.Bosses.EcclesiaBros.Esdras;
using Gameplay.GameControllers.Bosses.PontiffOldman;
using Gameplay.GameControllers.Enemies.AshCharger.AI;
using Gameplay.GameControllers.Enemies.BellCarrier;
using Gameplay.GameControllers.Enemies.BellCarrier.IA;
using Gameplay.GameControllers.Enemies.ChainedAngel;
using Gameplay.GameControllers.Enemies.Fool.AI;
using Gameplay.GameControllers.Enemies.Framework.Damage;
using Gameplay.GameControllers.Enemies.Framework.IA;
using Gameplay.GameControllers.Enemies.NewFlagellant.AI;
using Gameplay.GameControllers.Enemies.Processioner;
using Gameplay.GameControllers.Enemies.Runner.AI;
using Gameplay.GameControllers.Enemies.WalkingTomb.AI;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.CoopLocal.Combat;

/// <summary>
/// Sistema de Enfoque y Selección Dinámica de Objetivo para Enemigos y Jefes (P1, P2, P3, P4).
/// Permite que los enemigos detecten, persigan, miren y ataquen a cualquiera de los jugadores,
/// basándose en proximidad (jugador vivo más cercano) y agresividad/retaliación al recibir daño.
/// Ronda 96: Soporte completo para enemigos con poste/tronco (BellCarrier, Processioner, ChainedAngel, Flagellant, etc.)
/// interceptando Entity.Update para cubrir el 100% de subclases que omiten Enemy.OnUpdate.
/// </summary>
internal static class EnemyTargetingSystem
{
    private class AggroData
    {
        public Penitent Attacker;
        public float ExpireTime;
    }

    private static readonly Dictionary<Enemy, AggroData> EnemyAggroTable = new Dictionary<Enemy, AggroData>();

    /// <summary>
    /// Registra agresión / represalia: cuando un jugador hiere a un enemigo,
    /// dicho enemigo fija como prioridad atacar a ese jugador durante 'duration' segundos.
    /// </summary>
    public static void RecordDamageAggro(Enemy enemy, Penitent attacker, float duration = 5.0f)
    {
        if (enemy == null || attacker == null || attacker.Status.Dead) return;

        EnemyAggroTable[enemy] = new AggroData
        {
            Attacker = attacker,
            ExpireTime = Time.time + duration
        };

        UpdateEnemyTarget(enemy);

        // Giro inmediato hacia el atacante para porteadores y enemigos estándar
        if (enemy is BellCarrier bellCarrier && bellCarrier.BellCarrierBehaviour != null)
        {
            bellCarrier.BellCarrierBehaviour.LookAtTarget(attacker.transform);
            bellCarrier.BellCarrierBehaviour.ResetTimeChasing();
            bellCarrier.AnimatorInyector?.Chasing();
        }
        else if (enemy.EnemyBehaviour != null)
        {
            enemy.EnemyBehaviour.LookAtTarget(attacker.transform.position);
        }
    }

    /// <summary>
    /// Obtiene todos los Penitentes activos y vivos en la sala (P1, P2 y futuros P3/P4).
    /// </summary>
    public static List<Penitent> GetActiveLivingPlayers()
    {
        List<Penitent> players = new List<Penitent>(4);

        Penitent p1 = Core.Logic?.Penitent;
        if (p1 != null && p1.gameObject.activeInHierarchy && !p1.Status.Dead)
        {
            players.Add(p1);
        }

        Penitent p2 = CoopLocal.Player2;
        if (p2 != null && p2.gameObject.activeInHierarchy && !p2.Status.Dead)
        {
            players.Add(p2);
        }

        // Soporte extensible para P3 y P4 desde CoopPlayerRegistry
        for (int i = 0; i < CoopPlayerRegistry.MaxPlayers; i++)
        {
            var slot = CoopPlayerRegistry.GetSlot(i);
            if (slot != null && slot.Instance != null &&
                !players.Contains(slot.Instance) &&
                slot.Instance.gameObject.activeInHierarchy &&
                !slot.Instance.Status.Dead)
            {
                players.Add(slot.Instance);
            }
        }

        return players;
    }

    /// <summary>
    /// Selecciona el mejor jugador objetivo para un enemigo:
    /// 1. Prioriza represalia si un jugador lo dañó recientemente y sigue vivo y a distancia razonable.
    /// 2. En caso contrario, elige al jugador vivo más cercano.
    /// </summary>
    public static Penitent GetBestTarget(Enemy enemy)
    {
        if (enemy == null) return Core.Logic?.Penitent;

        List<Penitent> players = GetActiveLivingPlayers();
        if (players.Count == 0)
        {
            return Core.Logic?.Penitent;
        }

        Vector3 enemyPos = enemy.transform.position;

        // 1. Comprobar si hay aggro activo por haber recibido daño
        if (EnemyAggroTable.TryGetValue(enemy, out AggroData aggro))
        {
            if (Time.time < aggro.ExpireTime && aggro.Attacker != null &&
                aggro.Attacker.gameObject.activeInHierarchy && !aggro.Attacker.Status.Dead)
            {
                float distToAttacker = Vector2.Distance(enemyPos, aggro.Attacker.transform.position);
                // Si el atacante no se ha alejado excesivamente (> 24 unidades), seguir persiguiéndolo
                if (distToAttacker <= 24f)
                {
                    return aggro.Attacker;
                }
            }
            EnemyAggroTable.Remove(enemy);
        }

        // 2. Si solo hay un jugador vivo, retornar ese
        if (players.Count == 1)
        {
            return players[0];
        }

        // 3. Proximidad: seleccionar al jugador vivo más cercano
        Penitent closest = players[0];
        float minDistanceSqr = (enemyPos - closest.transform.position).sqrMagnitude;

        for (int i = 1; i < players.Count; i++)
        {
            float distSqr = (enemyPos - players[i].transform.position).sqrMagnitude;
            if (distSqr < minDistanceSqr)
            {
                minDistanceSqr = distSqr;
                closest = players[i];
            }
        }

        return closest;
    }

    /// <summary>
    /// Actualiza Target y DistanceToTarget para cualquier enemigo (incluyendo subclases complejas y querubines).
    /// </summary>
    public static void UpdateEnemyTarget(Enemy enemy)
    {
        if (enemy == null || enemy.Status.Dead) return;

        Penitent target = GetBestTarget(enemy);
        if (target == null) return;

        enemy.Target = target.gameObject;
        enemy.DistanceToTarget = Vector2.Distance(enemy.transform.position, target.transform.position);

        // Processioner y ChainedAngel (querubín con la cadena)
        if (enemy is Processioner processioner)
        {
            if (processioner.ChainedAngel != null)
            {
                processioner.ChainedAngel.Target = target.gameObject;
                processioner.ChainedAngel.DistanceToTarget = Vector2.Distance(processioner.ChainedAngel.transform.position, target.transform.position);
            }
        }
        else if (enemy is ChainedAngel chainedAngel)
        {
            chainedAngel.Target = target.gameObject;
            chainedAngel.DistanceToTarget = Vector2.Distance(chainedAngel.transform.position, target.transform.position);
        }

        // BellCarrier (porteador con campana en poste): orientar si el objetivo está detrás y está despierto o persiguiendo
        if (enemy is BellCarrier bellCarrier)
        {
            if (bellCarrier.BellCarrierBehaviour != null && !bellCarrier.BellCarrierBehaviour.TurningAround)
            {
                float dx = target.transform.position.x - bellCarrier.transform.position.x;
                var orient = bellCarrier.Status.Orientation;
                bool isBehind = (orient == EntityOrientation.Right && dx < -1.0f) || (orient == EntityOrientation.Left && dx > 1.0f);
                if (isBehind && (bellCarrier.IsChasing || bellCarrier.EnemyBehaviour.IsHurt || bellCarrier.BellCarrierBehaviour.IsAwaken))
                {
                    bellCarrier.BellCarrierBehaviour.LookAtTarget(target.transform);
                }
            }
        }
    }

    public static void Clear()
    {
        EnemyAggroTable.Clear();
    }

    public static void CleanDeadOrInvalidEnemies()
    {
        List<Enemy> toRemove = null;
        foreach (var kvp in EnemyAggroTable)
        {
            if (kvp.Key == null || kvp.Key.Status.Dead)
            {
                toRemove ??= new List<Enemy>();
                toRemove.Add(kvp.Key);
            }
        }
        if (toRemove != null)
        {
            foreach (var e in toRemove) EnemyAggroTable.Remove(e);
        }
    }
}

// =========================================================================
// Parches de Enfoque y Selección de Objetivo para Enemigos
// =========================================================================

/// <summary>
/// Parche maestro en Entity.Update: se ejecuta para TODAS las entidades vivas del juego.
/// Dado que 48 de las 77 clases de enemigos sobrescriben OnUpdate() saltándose Enemy.OnUpdate(),
/// enganchar Entity.Update garantiza que el 100% de enemigos (BellCarrier, Processioner, Flagellant, etc.)
/// actualicen dinámicamente su objetivo (P1, P2, P3, P4) en cada fotograma.
/// </summary>
[HarmonyPatch(typeof(Entity), "Update")]
internal static class Entity_Update_Targeting_Patch
{
    private static void Postfix(Entity __instance)
    {
        if (__instance is Enemy enemy && !enemy.Status.Dead)
        {
            EnemyTargetingSystem.UpdateEnemyTarget(enemy);
        }
    }
}

/// <summary>
/// Parche adicional en Enemy.OnUpdate para respaldar a las clases de enemigos que sí llaman a base.OnUpdate().
/// </summary>
[HarmonyPatch(typeof(Enemy), "OnUpdate")]
internal static class Enemy_OnUpdate_Targeting_Patch
{
    private static void Postfix(Enemy __instance)
    {
        if (__instance != null && !__instance.Status.Dead)
        {
            EnemyTargetingSystem.UpdateEnemyTarget(__instance);
        }
    }
}

/// <summary>
/// Parche en EnemyBehaviour.GetTarget: intercepta las consultas de la IA / BehaviourTree
/// para retornar el jugador objetivo real en lugar de asumir a Core.Logic.Penitent.
/// </summary>
[HarmonyPatch(typeof(EnemyBehaviour), nameof(EnemyBehaviour.GetTarget))]
internal static class EnemyBehaviour_GetTarget_MultiPlayer_Patch
{
    private static readonly FieldInfo EntityField = AccessTools.Field(typeof(EnemyBehaviour), "Entity");

    private static bool Prefix(EnemyBehaviour __instance, ref Transform __result)
    {
        Enemy entity = EntityField?.GetValue(__instance) as Enemy ?? __instance.GetComponentInParent<Enemy>();
        if (entity != null && !entity.Status.Dead)
        {
            Penitent target = EnemyTargetingSystem.GetBestTarget(entity);
            if (target != null)
            {
                entity.Target = target.gameObject;
                entity.DistanceToTarget = Vector2.Distance(entity.transform.position, target.transform.position);
                __result = target.transform;
                return false;
            }
        }
        return true;
    }
}

/// <summary>
/// Parche en VisionCone.CanSeeTarget: permite que el cono de visión del enemigo
/// compruebe la visibilidad hacia el jugador objetivo actual (sea P1 o P2).
/// </summary>
[HarmonyPatch(typeof(VisionCone), nameof(VisionCone.CanSeeTarget))]
internal static class VisionCone_CanSeeTarget_MultiPlayer_Patch
{
    private static void Prefix(VisionCone __instance, ref Transform t)
    {
        if (__instance == null || __instance.EntityOwner is not Enemy enemy) return;

        Penitent target = EnemyTargetingSystem.GetBestTarget(enemy);
        if (target != null)
        {
            t = target.transform;
        }
    }
}

/// <summary>
/// Parche en BellCarrierBehaviour.CheckCollision: asegura que TargetInLine se marque como verdadero
/// cuando P2 se encuentra en la línea frontal de ataque (hasta 15 unidades de distancia),
/// evitando que fallos de etiquetas en colliders secundarios impidan la detección.
/// </summary>
[HarmonyPatch(typeof(BellCarrierBehaviour), "CheckCollision")]
internal static class BellCarrierBehaviour_CheckCollision_Patch
{
    private static void Postfix(BellCarrierBehaviour __instance)
    {
        if (__instance.TargetInLine) return;

        Enemy enemy = __instance.Entity;
        if (enemy == null || enemy.Target == null) return;

        Vector2 enemyPos = enemy.transform.position;
        Vector2 targetPos = enemy.Target.transform.position;
        float dx = targetPos.x - enemyPos.x;
        float dy = Mathf.Abs(targetPos.y - enemyPos.y);

        bool inFront = (enemy.Status.Orientation == EntityOrientation.Right && dx > 0f && dx <= 15f) ||
                       (enemy.Status.Orientation == EntityOrientation.Left && dx < 0f && dx >= -15f);

        if (inFront && dy < 3.5f)
        {
            __instance.TargetInLine = true;
        }
    }
}

/// <summary>
/// Parche en NewFlagellantBehaviour.CanGoDownToReachPlayer: comprueba la posición del Target actual
/// (P1 o P2) en lugar de hardcodear Core.Logic.Penitent para decidir si bajar de plataforma.
/// </summary>
[HarmonyPatch(typeof(NewFlagellantBehaviour), "CanGoDownToReachPlayer")]
internal static class NewFlagellantBehaviour_CanGoDownToReachPlayer_Patch
{
    private static bool Prefix(NewFlagellantBehaviour __instance, ref bool __result)
    {
        var enemy = __instance.NewFlagellant;
        if (enemy == null || enemy.Target == null) return true;

        Vector2 enemyPos = enemy.transform.position;
        Vector2 targetPos = enemy.Target.transform.position;
        Vector2 dir = (enemy.Status.Orientation == EntityOrientation.Right) ? Vector2.right : Vector2.left;

        Vector2 toTarget = targetPos - enemyPos;
        if (Math.Sign(toTarget.x) != Math.Sign(dir.x))
        {
            __result = false;
            return false;
        }

        if (targetPos.y > enemyPos.y)
        {
            __result = false;
            return false;
        }

        Vector2 checkPos = enemyPos + dir;
        Vector2 hitFloor;
        __result = enemy.MotionChecker.HitsFloorInPosition(checkPos, 2f, out hitFloor, false);
        return false;
    }
}

/// <summary>
/// Parche en FoolBehaviour.Chase: evita que el enemigo 'Fool' deje de moverse si P1 muere pero P2 sigue vivo.
/// </summary>
[HarmonyPatch(typeof(FoolBehaviour), "Chase")]
internal static class FoolBehaviour_Chase_Patch
{
    private static bool Prefix(FoolBehaviour __instance, Vector3 targetPos)
    {
        if (__instance.Fool == null) return true;

        var living = EnemyTargetingSystem.GetActiveLivingPlayers();
        if (living.Count == 0)
        {
            __instance.StopMovement();
            return false;
        }

        return true;
    }
}

/// <summary>
/// Parches para evitar que enemigos como NewFlagellant, WalkingTomb, AshCharger y Runner
/// se desactiven o se queden inertes al morir P1 si P2 sigue vivo en la sala.
/// </summary>
[HarmonyPatch(typeof(NewFlagellantBehaviour), "OnPlayerDead")]
internal static class NewFlagellantBehaviour_OnPlayerDead_Patch
{
    private static bool Prefix()
    {
        return EnemyTargetingSystem.GetActiveLivingPlayers().Count == 0;
    }
}

[HarmonyPatch(typeof(WalkingTombBehaviour), "TargetOnDead")]
internal static class WalkingTombBehaviour_TargetOnDead_Patch
{
    private static bool Prefix()
    {
        return EnemyTargetingSystem.GetActiveLivingPlayers().Count == 0;
    }
}

[HarmonyPatch(typeof(AshChargerBehaviour), "TargetOnDead")]
internal static class AshChargerBehaviour_TargetOnDead_Patch
{
    private static bool Prefix()
    {
        return EnemyTargetingSystem.GetActiveLivingPlayers().Count == 0;
    }
}

[HarmonyPatch(typeof(RunnerBehaviour), "OnPlayerDead")]
internal static class RunnerBehaviour_OnPlayerDead_Patch
{
    private static bool Prefix()
    {
        return EnemyTargetingSystem.GetActiveLivingPlayers().Count == 0;
    }
}

/// <summary>
/// Parche en EnemyDamageArea.TakeDamage: cuando un enemigo es golpeado por un jugador,
/// registra represalia inmediata (aggro) hacia dicho atacante para que el enemigo se gire y lo ataque.
/// </summary>
[HarmonyPatch(typeof(EnemyDamageArea), nameof(EnemyDamageArea.TakeDamage))]
internal static class EnemyDamageArea_TakeDamage_Aggro_Patch
{
    private static void Postfix(EnemyDamageArea __instance, Hit hit)
    {
        if (hit.AttackingEntity == null) return;

        Penitent attacker = hit.AttackingEntity.GetComponent<Penitent>() ?? hit.AttackingEntity.GetComponentInParent<Penitent>();
        if (attacker == null || attacker.Status.Dead) return;

        Enemy enemy = __instance.GetComponent<Enemy>() ?? __instance.GetComponentInParent<Enemy>();
        if (enemy == null || enemy.Status.Dead) return;

        EnemyTargetingSystem.RecordDamageAggro(enemy, attacker, 5.0f);
    }
}

// =========================================================================
// Parches de Enfoque en Jefes específicos (LookAtPenitent -> LookAtTarget)
// =========================================================================

[HarmonyPatch(typeof(AmanecidasBehaviour), "LookAtPenitent")]
internal static class AmanecidasBehaviour_LookAtPenitent_Patch
{
    private static bool Prefix(AmanecidasBehaviour __instance)
    {
        Enemy enemy = __instance.GetComponentInParent<Enemy>();
        Penitent target = EnemyTargetingSystem.GetBestTarget(enemy);
        if (target != null)
        {
            __instance.LookAtTarget(target.transform.position);
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(CrisantaBehaviour), "LookAtPenitent")]
internal static class CrisantaBehaviour_LookAtPenitent_Patch
{
    private static bool Prefix(CrisantaBehaviour __instance)
    {
        Enemy enemy = __instance.GetComponentInParent<Enemy>();
        Penitent target = EnemyTargetingSystem.GetBestTarget(enemy);
        if (target != null)
        {
            __instance.LookAtTarget(target.transform.position);
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(EsdrasBehaviour), "LookAtPenitent")]
internal static class EsdrasBehaviour_LookAtPenitent_Patch
{
    private static bool Prefix(EsdrasBehaviour __instance)
    {
        Enemy enemy = __instance.GetComponentInParent<Enemy>();
        Penitent target = EnemyTargetingSystem.GetBestTarget(enemy);
        if (target != null)
        {
            __instance.LookAtTarget(target.transform.position);
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(PontiffOldmanBehaviour), "LookAtPenitent")]
internal static class PontiffOldmanBehaviour_LookAtPenitent_Patch
{
    private static bool Prefix(PontiffOldmanBehaviour __instance)
    {
        Enemy enemy = __instance.GetComponentInParent<Enemy>();
        Penitent target = EnemyTargetingSystem.GetBestTarget(enemy);
        if (target != null)
        {
            __instance.LookAtTarget(target.transform.position);
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(LevelManager), "ChangeLevel", new Type[] { typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(Color?) })]
internal static class LevelManager_ChangeLevel_Targeting_Patch
{
    private static void Prefix()
    {
        EnemyTargetingSystem.Clear();
    }
}
