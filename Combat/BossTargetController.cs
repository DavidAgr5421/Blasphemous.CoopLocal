using System;
using System.Collections.Generic;
using System.Reflection;
using Framework.FrameworkCore;
using Framework.Managers;
using Gameplay.GameControllers.Bosses.Amanecidas;
using Gameplay.GameControllers.Bosses.BejeweledSaint;
using Gameplay.GameControllers.Bosses.BejeweledSaint.IA;
using Gameplay.GameControllers.Bosses.BlindBaby;
using Gameplay.GameControllers.Bosses.BurntFace;
using Gameplay.GameControllers.Bosses.BurntFace.AI;
using Gameplay.GameControllers.Bosses.Crisanta;
using Gameplay.GameControllers.Bosses.Crisanta.AI;
using Gameplay.GameControllers.Bosses.EcclesiaBros.Esdras;
using Gameplay.GameControllers.Bosses.EcclesiaBros.Perpetua;
using Gameplay.GameControllers.Bosses.ElderBrother;
using Gameplay.GameControllers.Bosses.HighWills;
using Gameplay.GameControllers.Bosses.Isidora;
using Gameplay.GameControllers.Bosses.Lesmes;
using Gameplay.GameControllers.Bosses.PietyMonster;
using Gameplay.GameControllers.Bosses.PietyMonster.IA;
using Gameplay.GameControllers.Bosses.PontiffGiant;
using Gameplay.GameControllers.Bosses.PontiffHusk;
using Gameplay.GameControllers.Bosses.PontiffOldman;
using Gameplay.GameControllers.Bosses.PontiffSword;
using Gameplay.GameControllers.Bosses.Quirce;
using Gameplay.GameControllers.Bosses.Snake;
using Gameplay.GameControllers.Bosses.TresAngustias;
using Gameplay.GameControllers.Enemies.Framework.IA;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.CoopLocal.Combat;

/// <summary>
/// Controlador Especializado de Objetivo y Bucle de Decisión para Jefes en Modo Cooperativo.
/// Resuelve de raíz los problemas de indecisión, vacilación ("dudando") y bloqueos ("trabado") cuando hay 2 a 4 jugadores:
/// 1. Histéresis estricta de 10 a 14 segundos: el jefe mantiene fijado a un jugador sin oscilaciones frame-a-frame.
/// 2. Protección de combos y animaciones: si el tiempo expira pero el jefe está en mitad de un ataque o combo,
///    se pospone el cambio hasta terminar la acción para evitar cancelaciones abruptas o estados corruptos.
/// 3. Reasignación instantánea ante la muerte del jugador targeteado: si el objetivo muere o queda incapacitado,
///    el jefe rompe la fijación y redirige inmediatamente sus ataques al siguiente jugador vivo más cercano.
/// 4. Selección cíclica inteligente: alterna entre jugadores vivos priorizando represalia/daño reciente recibido
///    y distancia relativa en cada transición de turno.
/// 5. Redirección de llamadas hardcodeadas a Core.Logic.Penitent en comportamientos de jefes (Crisanta, Escribar,
///    Amanecidas, Isidora, Quirce, Expósito, Sierpes, etc.).
/// </summary>
public static class BossTargetController
{
    public const float MinLockDuration = 10.0f;
    public const float MaxLockDuration = 14.0f;

    internal class BossSession
    {
        public Enemy Boss;
        public Penitent CurrentTarget;
        public float LockUntilTime;
        public float LockStartTime;
        public int LastTargetPlayerIndex = -1;
        public Dictionary<Penitent, float> AccumulatedDamage = new Dictionary<Penitent, float>();
    }

    private static readonly Dictionary<Enemy, BossSession> ActiveSessions = new Dictionary<Enemy, BossSession>();

    /// <summary>
    /// Comprueba si una entidad enemiga es un Jefe (por jerarquía de clases, namespace o perfil de escalado).
    /// </summary>
    public static bool IsBoss(Enemy enemy)
    {
        if (enemy == null) return false;
        if (enemy is TestDummyEnemy) return false;
        if (CoopEnemyScaling.IsBoss(enemy)) return true;

        string ns = enemy.GetType().Namespace ?? "";
        if (ns.IndexOf("Bosses", StringComparison.OrdinalIgnoreCase) >= 0) return true;

        string typeName = enemy.GetType().Name ?? "";
        if (typeName.IndexOf("Boss", StringComparison.OrdinalIgnoreCase) >= 0) return true;

        return false;
    }

    /// <summary>
    /// Obtiene el objetivo actual fijado para un jefe sin forzar recálculos o cambios de estado.
    /// </summary>
    public static Penitent GetCurrentLockedTarget(Enemy boss)
    {
        if (boss == null) return Core.Logic?.Penitent;

        if (ActiveSessions.TryGetValue(boss, out BossSession session) && session != null)
        {
            if (session.CurrentTarget != null &&
                session.CurrentTarget.gameObject.activeInHierarchy &&
                !session.CurrentTarget.Status.Dead)
            {
                return session.CurrentTarget;
            }
        }

        return GetOrUpdateBossTarget(boss);
    }

    /// <summary>
    /// Obtiene o actualiza el objetivo fijado de un jefe respetando la histéresis estricta de 10-14s,
    /// la protección contra cambios a mitad de combo y la reasignación instantánea si el objetivo muere.
    /// </summary>
    public static Penitent GetOrUpdateBossTarget(Enemy boss)
    {
        if (boss == null || boss.Status.Dead) return Core.Logic?.Penitent;

        List<Penitent> livingPlayers = EnemyTargetingSystem.GetActiveLivingPlayers();
        if (livingPlayers.Count == 0)
        {
            return Core.Logic?.Penitent;
        }

        if (livingPlayers.Count == 1)
        {
            Penitent single = livingPlayers[0];
            if (ActiveSessions.TryGetValue(boss, out BossSession singleSession) && singleSession != null)
            {
                singleSession.CurrentTarget = single;
            }
            return single;
        }

        if (!ActiveSessions.TryGetValue(boss, out BossSession session) || session == null)
        {
            session = new BossSession
            {
                Boss = boss,
                CurrentTarget = null,
                LockUntilTime = 0f,
                LockStartTime = 0f,
                LastTargetPlayerIndex = -1
            };
            ActiveSessions[boss] = session;
        }

        float now = Time.time;

        // 1. REASIGNACIÓN INMEDIATA POR MUERTE: si el objetivo actual murió o ya no está disponible
        bool targetInvalid = session.CurrentTarget == null ||
                             session.CurrentTarget.Status.Dead ||
                             !session.CurrentTarget.gameObject.activeInHierarchy ||
                             !livingPlayers.Contains(session.CurrentTarget);

        if (targetInvalid)
        {
            Penitent immediateTarget = PickClosestLivingPlayer(boss, livingPlayers, null);
            LockNewTarget(session, immediateTarget, UnityEngine.Random.Range(MinLockDuration, MaxLockDuration), livingPlayers);
            return immediateTarget;
        }

        // 2. HISTÉRESIS ESTRICTA: si el tiempo fijado (10-14s) aún no ha expirado, mantener firmemente el objetivo
        if (now < session.LockUntilTime)
        {
            return session.CurrentTarget;
        }

        // 3. PROTECCIÓN DE COMBOS Y ACCIONES: si el tiempo expiró pero el jefe está atacando o ejecutando combo,
        // no cambiar todavía; extender brevemente el bloqueo hasta que vuelva al estado disponible/idle.
        if (IsBossMidAction(boss))
        {
            session.LockUntilTime = now + 0.6f;
            return session.CurrentTarget;
        }

        // 4. CAMBIO CÍCLICO / REPRESALIA: el jefe está libre para una nueva decisión.
        Penitent nextTarget = SelectNextCyclicOrAggroTarget(boss, session, livingPlayers);
        LockNewTarget(session, nextTarget, UnityEngine.Random.Range(MinLockDuration, MaxLockDuration), livingPlayers);
        return nextTarget;
    }

    /// <summary>
    /// Fija un nuevo objetivo en la sesión del jefe con la duración calculada.
    /// </summary>
    private static void LockNewTarget(BossSession session, Penitent target, float duration, List<Penitent> livingPlayers)
    {
        session.CurrentTarget = target;
        session.LockStartTime = Time.time;
        session.LockUntilTime = Time.time + duration;
        session.AccumulatedDamage.Clear();

        if (target != null && livingPlayers != null)
        {
            session.LastTargetPlayerIndex = livingPlayers.IndexOf(target);
        }
    }

    /// <summary>
    /// Registra el daño recibido de un jugador para ponderar la agresividad en el próximo turno de targeteo.
    /// No interrumpe la fijación actual para evitar flip-flopping.
    /// </summary>
    public static void RecordBossDamage(Enemy boss, Penitent attacker, float damage)
    {
        if (boss == null || attacker == null || attacker.Status.Dead) return;

        if (!ActiveSessions.TryGetValue(boss, out BossSession session) || session == null)
        {
            session = new BossSession
            {
                Boss = boss,
                CurrentTarget = attacker,
                LockUntilTime = Time.time + UnityEngine.Random.Range(MinLockDuration, MaxLockDuration),
                LockStartTime = Time.time
            };
            ActiveSessions[boss] = session;
        }

        if (!session.AccumulatedDamage.ContainsKey(attacker))
        {
            session.AccumulatedDamage[attacker] = 0f;
        }
        session.AccumulatedDamage[attacker] += Mathf.Max(1f, damage);
    }

    /// <summary>
    /// Determina si un jefe se encuentra a mitad de un ataque, animación o combo.
    /// </summary>
    public static bool IsBossMidAction(Enemy boss)
    {
        if (boss == null || boss.Status.Dead) return false;

        // 1. EnemyBehaviour.IsAttacking estándar
        if (boss.EnemyBehaviour != null && boss.EnemyBehaviour.IsAttacking)
        {
            return true;
        }

        // 2. Comprobaciones específicas de máquinas de estado de jefes
        if (boss.EnemyBehaviour is CrisantaBehaviour crisanta)
        {
            if (crisanta.currentState == Gameplay.GameControllers.Bosses.Crisanta.BOSS_STATES.MID_ACTION || !crisanta.CanExecuteNewAction())
                return true;
        }
        else if (boss.EnemyBehaviour is ElderBrotherBehaviour elder)
        {
            if (!elder.CanExecuteNewAction())
                return true;
        }
        else if (boss.EnemyBehaviour is PietyMonsterBehaviour piety)
        {
            if (piety.Attacking || piety.Spiting)
                return true;
        }
        else if (boss.EnemyBehaviour is QuirceBehaviour quirce)
        {
            if (!quirce.CanExecuteNewAction())
                return true;
        }
        else if (boss.EnemyBehaviour is PontiffGiantBehaviour giant)
        {
            if (!giant.CanExecuteNewAction())
                return true;
        }
        else if (boss.EnemyBehaviour is PontiffOldmanBehaviour oldman)
        {
            if (!oldman.CanExecuteNewAction())
                return true;
        }
        else if (boss.EnemyBehaviour is BurntFaceBehaviour burnt)
        {
            if (!burnt.CanExecuteNewAction())
                return true;
        }

        // 3. Inspección del Animator
        if (boss.Animator != null)
        {
            AnimatorStateInfo state = boss.Animator.GetCurrentAnimatorStateInfo(0);
            if (state.IsTag("Attack")) return true;

            // Nombres típicos de animaciones de ataque / combo
            if (state.IsName("Attack") || state.IsName("Slash") || state.IsName("Smash") ||
                state.IsName("Dash") || state.IsName("Blink") || state.IsName("Cast") ||
                state.IsName("Combo") || state.IsName("Stomp") || state.IsName("Claw"))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Selecciona el siguiente objetivo cuando el turno expira:
    /// Alterna cíclicamente entre los demás jugadores vivos, ponderando aggro reciente y cercanía.
    /// </summary>
    private static Penitent SelectNextCyclicOrAggroTarget(Enemy boss, BossSession session, List<Penitent> livingPlayers)
    {
        List<Penitent> otherCandidates = new List<Penitent>(livingPlayers.Count);
        for (int i = 0; i < livingPlayers.Count; i++)
        {
            if (livingPlayers[i] != session.CurrentTarget)
            {
                otherCandidates.Add(livingPlayers[i]);
            }
        }

        if (otherCandidates.Count == 0)
        {
            return session.CurrentTarget ?? livingPlayers[0];
        }

        if (otherCandidates.Count == 1)
        {
            return otherCandidates[0];
        }

        // Si algún candidato ha infligido daño significativo en este ciclo, priorizarlo como represalia
        Penitent topDamager = null;
        float maxDamage = 0f;
        foreach (var candidate in otherCandidates)
        {
            if (session.AccumulatedDamage.TryGetValue(candidate, out float dmg) && dmg > maxDamage)
            {
                maxDamage = dmg;
                topDamager = candidate;
            }
        }

        if (topDamager != null && maxDamage >= 30f)
        {
            return topDamager;
        }

        // Cambio cíclico round-robin
        int nextIndex = (session.LastTargetPlayerIndex + 1) % livingPlayers.Count;
        Penitent cyclicCandidate = livingPlayers[nextIndex];
        if (cyclicCandidate != session.CurrentTarget)
        {
            return cyclicCandidate;
        }

        // Si el cíclico coincidiera con el actual, tomar el más cercano de los otros candidatos
        return PickClosestLivingPlayer(boss, otherCandidates, null);
    }

    /// <summary>
    /// Encuentra al jugador vivo más cercano al jefe de una lista de candidatos.
    /// </summary>
    public static Penitent PickClosestLivingPlayer(Enemy boss, List<Penitent> candidates, Penitent exclude)
    {
        if (candidates == null || candidates.Count == 0) return Core.Logic?.Penitent;

        Vector3 bossPos = boss != null ? boss.transform.position : Vector3.zero;
        Penitent closest = null;
        float minDistanceSqr = float.MaxValue;

        for (int i = 0; i < candidates.Count; i++)
        {
            Penitent p = candidates[i];
            if (p == null || p.Status.Dead || p == exclude) continue;

            float distSqr = (bossPos - p.transform.position).sqrMagnitude;
            if (distSqr < minDistanceSqr)
            {
                minDistanceSqr = distSqr;
                closest = p;
            }
        }

        return closest ?? candidates[0];
    }

    /// <summary>
    /// Limpia todas las sesiones de jefes (llamado al cambiar de sala o nivel).
    /// </summary>
    public static void Clear()
    {
        ActiveSessions.Clear();
    }
}

// =========================================================================
// Parches de Soporte para Daño a Jefes cuando P1 está Caído / Muerto
// =========================================================================

/// <summary>
/// En el juego base, 21 clases de Jefes implementan "if (Core.Logic.Penitent.Stats.Life.Current menor o igual a 0) return;"
/// dentro de Damage(Hit hit). Si P1 cae abatido, los jefes se volvían 100% inmunes a los ataques de P2, P3 y P4.
/// Este parche temporalmente valida la comprobación permitiendo que el daño de los demás jugadores vivos impacte.
/// </summary>
[HarmonyPatch]
internal static class BossDamageCoopPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        Type[] bossEnemyTypes = new Type[]
        {
            typeof(Amanecidas),
            typeof(BejeweledSaintHead),
            typeof(WickerWurm),
            typeof(WickerWurmHeart),
            typeof(BurntFace),
            typeof(Crisanta),
            typeof(Esdras),
            typeof(Perpetua),
            typeof(ElderBrother),
            typeof(HighWills),
            typeof(Isidora),
            typeof(Lesmes),
            typeof(PietyMonster),
            typeof(PontiffGiant),
            typeof(PontiffSword),
            typeof(PontiffHuskBoss),
            typeof(PontiffOldman),
            typeof(Quirce),
            typeof(Snake),
            typeof(SingleAnguish),
            typeof(TresAngustiasMaster)
        };

        foreach (var type in bossEnemyTypes)
        {
            MethodInfo m = AccessTools.Method(type, "Damage", new Type[] { typeof(Hit) });
            if (m != null)
            {
                yield return m;
            }
        }
    }

    [HarmonyPrefix]
    private static void Prefix(out float __state)
    {
        __state = -1f;
        var p1 = Core.Logic?.Penitent;
        if (p1 != null && p1.Stats?.Life != null)
        {
            if (p1.Stats.Life.Current <= 0f && EnemyTargetingSystem.GetActiveLivingPlayers().Count > 0)
            {
                __state = p1.Stats.Life.Current;
                p1.Stats.Life.Current = 1f;
            }
        }
    }

    [HarmonyPostfix]
    private static void Postfix(float __state)
    {
        if (__state >= 0f)
        {
            var p1 = Core.Logic?.Penitent;
            if (p1 != null && p1.Stats?.Life != null)
            {
                p1.Stats.Life.Current = __state;
            }
        }
    }
}

// =========================================================================
// Parches de Redirección de Objetivo en Comportamientos de Jefes
// =========================================================================

// --- Crisanta ---
[HarmonyPatch(typeof(CrisantaBehaviour), "GetDirToPenitent")]
internal static class CrisantaBehaviour_GetDirToPenitent_Patch
{
    private static bool Prefix(CrisantaBehaviour __instance, ref Vector2 __result)
    {
        Penitent target = BossTargetController.GetCurrentLockedTarget(__instance.GetComponentInParent<Enemy>());
        if (target != null)
        {
            __result = target.transform.position + Vector3.up - __instance.transform.position;
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(CrisantaBehaviour), "DistanceToPenitent")]
internal static class CrisantaBehaviour_DistanceToPenitent_Patch
{
    private static bool Prefix(CrisantaBehaviour __instance, ref float __result)
    {
        Penitent target = BossTargetController.GetCurrentLockedTarget(__instance.GetComponentInParent<Enemy>());
        if (target != null)
        {
            __result = Vector2.Distance(target.transform.position + Vector3.up, __instance.transform.position);
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(CrisantaBehaviour), "IsPenitentOnRightSide")]
internal static class CrisantaBehaviour_IsPenitentOnRightSide_Patch
{
    private static bool Prefix(CrisantaBehaviour __instance, ref bool __result)
    {
        Penitent target = BossTargetController.GetCurrentLockedTarget(__instance.GetComponentInParent<Enemy>());
        if (target != null && __instance.fightCenterTransform != null)
        {
            __result = target.transform.position.x > __instance.fightCenterTransform.position.x;
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(CrisantaBehaviour), "IsPenitentCloseToCenter")]
internal static class CrisantaBehaviour_IsPenitentCloseToCenter_Patch
{
    private static bool Prefix(CrisantaBehaviour __instance, ref bool __result)
    {
        Penitent target = BossTargetController.GetCurrentLockedTarget(__instance.GetComponentInParent<Enemy>());
        if (target != null && __instance.fightCenterTransform != null)
        {
            __result = Mathf.Abs(target.transform.position.x - __instance.fightCenterTransform.position.x) < 3f;
            return false;
        }
        return true;
    }
}

// --- Amanecidas ---
[HarmonyPatch(typeof(AmanecidasBehaviour), "GetPointBelowPenitent")]
internal static class AmanecidasBehaviour_GetPointBelowPenitent_Patch
{
    private static bool Prefix(AmanecidasBehaviour __instance, bool stopOnOneWayDowns, ref Vector2 __result)
    {
        Penitent target = BossTargetController.GetCurrentLockedTarget(__instance.GetComponentInParent<Enemy>());
        if (target != null)
        {
            __result = __instance.GetPointBelow(target.transform.position + Vector3.up * 0.25f, stopOnOneWayDowns);
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(AmanecidasBehaviour), "GetDirToPenitent", new Type[0])]
internal static class AmanecidasBehaviour_GetDirToPenitent_NoArgs_Patch
{
    private static bool Prefix(AmanecidasBehaviour __instance, ref Vector2 __result)
    {
        Penitent target = BossTargetController.GetCurrentLockedTarget(__instance.GetComponentInParent<Enemy>());
        if (target != null)
        {
            __result = (Vector2)target.transform.position - (Vector2)__instance.transform.position;
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(AmanecidasBehaviour), "GetDirToPenitent", new Type[] { typeof(Vector2) })]
internal static class AmanecidasBehaviour_GetDirToPenitent_Point_Patch
{
    private static bool Prefix(AmanecidasBehaviour __instance, Vector2 point, ref Vector2 __result)
    {
        Penitent target = BossTargetController.GetCurrentLockedTarget(__instance.GetComponentInParent<Enemy>());
        if (target != null)
        {
            __result = (Vector2)target.transform.position - point;
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(AmanecidasBehaviour), "AimToPenitentWithBow")]
internal static class AmanecidasBehaviour_AimToPenitentWithBow_Patch
{
    private static bool Prefix(AmanecidasBehaviour __instance)
    {
        Penitent target = BossTargetController.GetCurrentLockedTarget(__instance.GetComponentInParent<Enemy>());
        if (target != null)
        {
            Vector2 dir = (Vector2)target.transform.position - (Vector2)__instance.transform.position;
            __instance.AimToPointWithBow((Vector2)__instance.transform.position + dir);
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(AmanecidasBehaviour), "LookAtPenitentUsingOrientation")]
internal static class AmanecidasBehaviour_LookAtPenitentUsingOrientation_Patch
{
    private static bool Prefix(AmanecidasBehaviour __instance)
    {
        Penitent target = BossTargetController.GetCurrentLockedTarget(__instance.GetComponentInParent<Enemy>());
        if (target != null)
        {
            Vector2 dir = (Vector2)target.transform.position - (Vector2)__instance.transform.position;
            __instance.LookAtDirUsingOrientation(dir);
            return false;
        }
        return true;
    }
}

// --- Guardián del Silencio (Elder Brother) ---
[HarmonyPatch(typeof(ElderBrotherBehaviour), "GetTargetPredictedPos")]
internal static class ElderBrotherBehaviour_GetTargetPredictedPos_Patch
{
    private static bool Prefix(ElderBrotherBehaviour __instance, ref Vector3 __result)
    {
        Penitent target = BossTargetController.GetCurrentLockedTarget(__instance.GetComponentInParent<Enemy>());
        if (target != null)
        {
            Vector2 vector = target.transform.position;
            vector.x += target.GetVelocity().x;
            __result = vector;
            return false;
        }
        return true;
    }
}

// --- Su Santidad Escribar Fase 1 (Pontiff Oldman) ---
[HarmonyPatch(typeof(PontiffOldmanBehaviour), "GetDirToPenitent")]
internal static class PontiffOldmanBehaviour_GetDirToPenitent_Patch
{
    private static bool Prefix(PontiffOldmanBehaviour __instance, Vector3 from, ref Vector2 __result)
    {
        Penitent target = BossTargetController.GetCurrentLockedTarget(__instance.GetComponentInParent<Enemy>());
        if (target != null)
        {
            __result = (Vector2)target.transform.position - (Vector2)from;
            return false;
        }
        return true;
    }
}

// --- Su Santidad Escribar Fase 2 (Pontiff Giant) ---
[HarmonyPatch(typeof(PontiffGiantBehaviour), "GetDirToPenitent")]
internal static class PontiffGiantBehaviour_GetDirToPenitent_Patch
{
    private static bool Prefix(PontiffGiantBehaviour __instance, Vector3 from, ref Vector2 __result)
    {
        Penitent target = BossTargetController.GetCurrentLockedTarget(__instance.GetComponentInParent<Enemy>());
        if (target != null)
        {
            __result = (Vector2)target.transform.position - (Vector2)from;
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(PontiffGiantBehaviour), "GetPointAroundPenitent")]
internal static class PontiffGiantBehaviour_GetPointAroundPenitent_Patch
{
    private static bool Prefix(PontiffGiantBehaviour __instance, ref Vector2 __result)
    {
        Penitent target = BossTargetController.GetCurrentLockedTarget(__instance.GetComponentInParent<Enemy>());
        if (target != null)
        {
            float num = 5f;
            Vector2 randomVec = new Vector2(UnityEngine.Random.Range(-0.8f, 0.8f), UnityEngine.Random.Range(0f, 1f)).normalized;
            __result = (Vector2)target.transform.position + randomVec * num;
            return false;
        }
        return true;
    }
}

// --- Escribar Fase 3 / Pontiff Husk ---
[HarmonyPatch(typeof(PontiffHuskBossBehaviour), "GetDirToPenitent")]
internal static class PontiffHuskBossBehaviour_GetDirToPenitent_Patch
{
    private static bool Prefix(PontiffHuskBossBehaviour __instance, ref Vector2 __result)
    {
        Penitent target = BossTargetController.GetCurrentLockedTarget(__instance.GetComponentInParent<Enemy>());
        if (target != null)
        {
            __result = (Vector2)target.transform.position - (Vector2)__instance.transform.position;
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(PontiffHuskBossBehaviour), "LookAtTarget")]
internal static class PontiffHuskBossBehaviour_LookAtTarget_Patch
{
    private static bool Prefix(PontiffHuskBossBehaviour __instance)
    {
        Penitent target = BossTargetController.GetCurrentLockedTarget(__instance.GetComponentInParent<Enemy>());
        if (target != null)
        {
            Vector2 dir = (Vector2)target.transform.position - (Vector2)__instance.transform.position;
            __instance.LookAtDir(dir);
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(PontiffHuskBossBehaviour), "ShouldTurnToLookAtTarget")]
internal static class PontiffHuskBossBehaviour_ShouldTurnToLookAtTarget_Patch
{
    private static bool Prefix(PontiffHuskBossBehaviour __instance, ref bool __result)
    {
        Penitent target = BossTargetController.GetCurrentLockedTarget(__instance.GetComponentInParent<Enemy>());
        if (target != null)
        {
            Vector2 dir = (Vector2)target.transform.position - (Vector2)__instance.transform.position;
            __result = __instance.ShouldTurn(dir);
            return false;
        }
        return true;
    }
}

// --- Espada de Escribar (Pontiff Sword) ---
[HarmonyPatch(typeof(PontiffSwordBehaviour), "GetDirToPenitent")]
internal static class PontiffSwordBehaviour_GetDirToPenitent_Patch
{
    private static bool Prefix(PontiffSwordBehaviour __instance, Vector3 from, ref Vector2 __result)
    {
        Penitent target = BossTargetController.GetCurrentLockedTarget(__instance.GetComponentInParent<Enemy>());
        if (target != null)
        {
            __result = (Vector2)target.transform.position - (Vector2)from;
            return false;
        }
        return true;
    }
}

// --- Isidora ---
[HarmonyPatch(typeof(IsidoraBehaviour), "GetDirToPenitent")]
internal static class IsidoraBehaviour_GetDirToPenitent_Patch
{
    private static bool Prefix(IsidoraBehaviour __instance, ref Vector2 __result)
    {
        Penitent target = BossTargetController.GetCurrentLockedTarget(__instance.GetComponentInParent<Enemy>());
        if (target != null)
        {
            __result = (Vector2)target.transform.position - (Vector2)__instance.transform.position;
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(IsidoraBehaviour), "LookAtTarget")]
internal static class IsidoraBehaviour_LookAtTarget_Patch
{
    private static bool Prefix(IsidoraBehaviour __instance)
    {
        Penitent target = BossTargetController.GetCurrentLockedTarget(__instance.GetComponentInParent<Enemy>());
        if (target != null)
        {
            Vector2 dir = (Vector2)target.transform.position - (Vector2)__instance.transform.position;
            __instance.LookAtDirUsingOrientation(dir);
            return false;
        }
        return true;
    }
}

// --- Sierpes (Snake) ---
[HarmonyPatch(typeof(SnakeBehaviour), "GetDirToPenitent")]
internal static class SnakeBehaviour_GetDirToPenitent_Patch
{
    private static bool Prefix(SnakeBehaviour __instance, ref Vector2 __result)
    {
        Penitent target = BossTargetController.GetCurrentLockedTarget(__instance.GetComponentInParent<Enemy>());
        if (target != null)
        {
            __result = (Vector2)target.transform.position - (Vector2)__instance.transform.position;
            return false;
        }
        return true;
    }
}

// --- Faz Desfigurada (Burnt Face) ---
[HarmonyPatch(typeof(BurntFaceBehaviour), "GetDirToPenitent")]
internal static class BurntFaceBehaviour_GetDirToPenitent_Patch
{
    private static bool Prefix(BurntFaceBehaviour __instance, Vector3 from, ref Vector2 __result)
    {
        Penitent target = BossTargetController.GetCurrentLockedTarget(__instance.GetComponentInParent<Enemy>());
        if (target != null)
        {
            __result = (Vector2)target.transform.position + Vector2.up * 0.8f - (Vector2)from;
            return false;
        }
        return true;
    }
}

// --- Quirce ---
[HarmonyPatch(typeof(QuirceBehaviour), "StartWaitingPeriod")]
internal static class QuirceBehaviour_StartWaitingPeriod_Patch
{
    private static void Prefix(QuirceBehaviour __instance)
    {
        Penitent target = BossTargetController.GetCurrentLockedTarget(__instance.GetComponentInParent<Enemy>());
        if (target != null)
        {
            __instance.LookAtTarget(target.transform.position);
        }
    }
}

// --- Expósito (Wicker Wurm) ---
[HarmonyPatch(typeof(WickerWurmBehaviour), "UpdateLookAtPlayer")]
internal static class WickerWurmBehaviour_UpdateLookAtPlayer_Patch
{
    private static bool Prefix(WickerWurmBehaviour __instance)
    {
        Penitent target = BossTargetController.GetCurrentLockedTarget(__instance.GetComponentInParent<Enemy>());
        if (target != null && __instance.ChainMaster != null)
        {
            Vector2 vector = (Vector2)target.transform.position + Vector2.up * 3.5f;
            var currentSideField = AccessTools.Field(typeof(WickerWurmBehaviour), "currentSide");
            object sideVal = currentSideField?.GetValue(__instance);
            bool isRight = sideVal != null && sideVal.ToString() == "RIGHT";
            if ((isRight && vector.x < __instance.transform.position.x - 2f) || (!isRight && vector.x < __instance.transform.position.x + 2f))
            {
                __instance.ChainMaster.LookAtTarget(vector, 2f);
            }
            else
            {
                __instance.ChainMaster.LookAtTarget(__instance.transform.position + Vector3.right * (isRight ? -1 : 1), 2f);
            }
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(WickerWurmBehaviour), "QuickStingAttack")]
internal static class WickerWurmBehaviour_QuickStingAttack_Patch
{
    private static bool Prefix(WickerWurmBehaviour __instance, bool right)
    {
        Penitent target = BossTargetController.GetCurrentLockedTarget(__instance.GetComponentInParent<Enemy>());
        if (target != null)
        {
            __instance.QuickStingAttackToPoint(right, target.transform.position);
            return false;
        }
        return true;
    }
}
