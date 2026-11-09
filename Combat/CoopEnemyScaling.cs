using System;
using System.Collections;
using System.Collections.Generic;
using Framework.FrameworkCore;
using Framework.Managers;
using Gameplay.GameControllers.Entities;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.CoopLocal.Combat;

/// <summary>
/// Marca componentes para identificar enemigos clonados por el sistema de cooperativo.
/// Evita re-clonaciones infinitas y previene interferencias con guardados persistentes.
/// </summary>
internal sealed class CoopClonedEnemyTag : MonoBehaviour
{
}

/// <summary>
/// Marca enemigos cuya salud ya ha sido escalada por el multiplicador de dificultad.
/// </summary>
internal sealed class CoopScaledEnemyTag : MonoBehaviour
{
}

internal sealed class CoopEnemyScalingRunner : MonoBehaviour
{
    private static CoopEnemyScalingRunner _instance;
    public static CoopEnemyScalingRunner Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("CoopEnemyScalingRunner");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _instance = go.AddComponent<CoopEnemyScalingRunner>();
            }
            return _instance;
        }
    }
}

/// <summary>
/// Perfil de resistencia específico de cada Jefe para estados alterados (Veneno y Sangrado).
/// </summary>
public struct BossResistanceProfile
{
    public string BossName;
    public float BasePoisonResistance; // 0.0f a 0.90f (porcentaje de absorción/mitigación)
    public float BaseBleedResistance;  // 0.0f a 0.90f (porcentaje de mitigación de daño de estallido)
    public int ExtraBleedHits;         // Golpes adicionales requeridos para detonar el sangrado

    public BossResistanceProfile(string name, float poisonRes, float bleedRes, int extraBleedHits)
    {
        BossName = name;
        BasePoisonResistance = poisonRes;
        BaseBleedResistance = bleedRes;
        ExtraBleedHits = extraBleedHits;
    }
}

/// <summary>
/// Sistema integral de escalado de dificultad cooperativa:
/// 1. Multiplicación de cantidad de enemigos por sala (evita duplicar jefes o NPCs).
/// 2. Multiplicador de vida para enemigos comunes y jefes.
/// 3. Tabla de resistencias personalizadas a veneno y sangrado para cada Jefe de Blasphemous.
/// </summary>
public static class CoopEnemyScaling
{
    // Opciones disponibles de multiplicadores
    public static readonly float[] EnemyCountMultipliers = { 1.0f, 1.5f, 2.0f, 2.5f, 3.0f };
    public static readonly string[] EnemyCountLabels = { "1.0x (Normal)", "1.5x (+50%)", "2.0x (Doble)", "2.5x (+150%)", "3.0x (Triple)" };

    public static readonly float[] HealthMultipliers = { 1.5f, 1.75f, 2.0f, 2.25f, 2.5f };
    public static readonly string[] HealthLabels = { "+50% (x1.5)", "+75% (x1.75)", "+100% (x2.0)", "+125% (x2.25)", "+150% (x2.5)" };

    public static readonly float[] BossResistanceLevels = { 2.0f, 2.75f, 3.5f };
    public static readonly string[] BossResistanceLabels = { "Estándar (x2.0)", "Alta (x2.75)", "Extrema (x3.5)" };

    // Configuración activa
    public static float ActiveEnemyMultiplier = 1.0f;
    public static float ActiveHealthMultiplier = 1.5f;
    public static float ActiveBossResistanceFactor = 2.0f;

    // Catálogo de resistencias por ID de Jefe
    private static readonly Dictionary<string, BossResistanceProfile> BossProfiles = new(StringComparer.OrdinalIgnoreCase)
    {
        // Guardián del Silencio (Warden) - gigante carnal inicial (débil a sangrado, 30% en nivel bajo)
        { "BS01", new BossResistanceProfile("Guardián del Silencio", 0.38f, 0.30f, 4) },
        // Ten Piedad (Bestia arbórea/carnal) - resistente a toxinas vegetales, débil a sangrado 30%
        { "BS02", new BossResistanceProfile("Ten Piedad", 0.44f, 0.30f, 5) },
        // Tres Angustias (Espectros etéreos) - espectros sin sangre densa: alta resistencia a sangrado, menor a veneno/corrosión
        { "BS03", new BossResistanceProfile("Tres Angustias", 0.32f, 0.54f, 6) },
        // Nuestra Señora de la Faz Desfigurada (Coraza áurea) - más resistente a sangrado por su rostro metálico, menor a veneno
        { "BS04", new BossResistanceProfile("Faz Desfigurada", 0.34f, 0.50f, 6) },
        // Melquíades, el Arzobispo Exhumado (Esqueleto óseo) - esqueleto sin sangre: muy resistente a sangrado, vulnerable a corrosión ácida de veneno 30%
        { "BS05", new BossResistanceProfile("Melquíades", 0.30f, 0.62f, 8) },
        // Expósito, Vástago de la Abjuración (Serpiente de mimbre y vástago) - más resistente a veneno, vulnerable a sangrado
        { "BS06", new BossResistanceProfile("Expósito", 0.52f, 0.33f, 5) },
        // Quirce, Devuelto por las Llamas (Fuego inquisidor) - las llamas purgan el veneno, pero sangra en combate cuerpo a cuerpo
        { "BS07", new BossResistanceProfile("Quirce", 0.58f, 0.36f, 6) },
        // Esdras, de la Legión Ungida (Guerrero humano acorazado) - humano vulnerable a veneno, armadura mitiga sangrado
        { "BS08", new BossResistanceProfile("Esdras", 0.34f, 0.48f, 5) },
        // Crisanta de la Agonía Vendada (Penitente acorazada late-game)
        { "BS09", new BossResistanceProfile("Crisanta", 0.50f, 0.64f, 8) },
        // Su Santidad Escribar (Jefe final)
        { "BS10", new BossResistanceProfile("Escribar", 0.65f, 0.60f, 8) },
        // Sierpes (Coloso de escamas y ponzoña) - altísima resistencia a veneno, menor a sangrado
        { "BS11", new BossResistanceProfile("Sierpes", 0.68f, 0.45f, 7) },
        // Isidora, Voz de los Muertos (Cantora del osario) - altísima resistencia a sangrado
        { "BS12", new BossResistanceProfile("Isidora", 0.52f, 0.68f, 9) },
        // Amanecidas (Guardianas cristalinas del Milagro) - cuerpo cristalino resistente a sangrado
        { "AMANECIDA", new BossResistanceProfile("Amanecida", 0.46f, 0.64f, 8) }
    };

    private static readonly BossResistanceProfile DefaultBossProfile = new("Jefe Genérico", 0.35f, 0.35f, 5);

    /// <summary>
    /// Aplica la configuración por defecto recomendada según la cantidad de jugadores.
    /// </summary>
    public static void SetDefaultsForPlayerCount(int playerCount)
    {
        switch (playerCount)
        {
            case 1:
                ActiveEnemyMultiplier = 1.0f;
                ActiveHealthMultiplier = 1.0f;
                ActiveBossResistanceFactor = 1.0f;
                break;
            case 2:
                ActiveEnemyMultiplier = 1.5f;
                ActiveHealthMultiplier = 1.5f;
                ActiveBossResistanceFactor = 2.0f;
                break;
            case 3:
                ActiveEnemyMultiplier = 2.0f;
                ActiveHealthMultiplier = 2.0f;
                ActiveBossResistanceFactor = 2.75f;
                break;
            case 4:
            default:
                ActiveEnemyMultiplier = 2.5f;
                ActiveHealthMultiplier = 2.5f;
                ActiveBossResistanceFactor = 3.5f;
                break;
        }
    }

    /// <summary>
    /// Identifica si una entidad enemiga es considerada un Jefe.
    /// </summary>
    public static bool IsBoss(Enemy enemy)
    {
        if (enemy == null) return false;
        if (enemy is TestDummyEnemy || (!string.IsNullOrEmpty(enemy.Id) && enemy.Id.StartsWith("DUMMY", StringComparison.OrdinalIgnoreCase))) return false;
        if (!string.IsNullOrEmpty(enemy.Id) && enemy.Id.StartsWith("BS", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        string fullName = enemy.GetType().FullName ?? "";
        if (fullName.IndexOf("Boss", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }
        if (enemy.name.IndexOf("Boss", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }
        float hp = enemy.Stats?.Life?.Final ?? 0f;
        return hp >= 350f;
    }

    /// <summary>
    /// Resuelve el perfil de resistencia asignado al jefe actual.
    /// </summary>
    public static BossResistanceProfile GetBossProfile(Enemy enemy)
    {
        if (enemy == null) return DefaultBossProfile;

        if (!string.IsNullOrEmpty(enemy.Id))
        {
            foreach (var kvp in BossProfiles)
            {
                if (enemy.Id.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Value;
                }
            }
        }

        string typeName = enemy.GetType().Name;
        foreach (var kvp in BossProfiles)
        {
            if (typeName.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return kvp.Value;
            }
        }

        return DefaultBossProfile;
    }

    public static float GetBossPoisonResistance(Enemy enemy)
    {
        if (!IsBoss(enemy)) return 0f;
        BossResistanceProfile profile = GetBossProfile(enemy);
        float effectiveFactor = Mathf.Clamp(ActiveBossResistanceFactor, 2.0f, 3.5f);
        float mult = Mathf.Lerp(1.0f, 1.35f, Mathf.InverseLerp(2.0f, 3.5f, effectiveFactor));
        float rawRes = profile.BasePoisonResistance * mult;
        return Mathf.Clamp(rawRes, 0.20f, 0.90f);
    }

    public static float GetBossBleedResistance(Enemy enemy)
    {
        if (!IsBoss(enemy)) return 0f;
        BossResistanceProfile profile = GetBossProfile(enemy);
        float effectiveFactor = Mathf.Clamp(ActiveBossResistanceFactor, 2.0f, 3.5f);
        float mult = Mathf.Lerp(1.0f, 1.35f, Mathf.InverseLerp(2.0f, 3.5f, effectiveFactor));
        float rawRes = profile.BaseBleedResistance * mult;
        return Mathf.Clamp(rawRes, 0.20f, 0.90f);
    }

    public static int GetBossExtraBleedHits(Enemy enemy)
    {
        return 0;
    }

    /// <summary>
    /// Disparado automáticamente por LevelManager.OnLevelLoaded.
    /// </summary>
    public static void OnLevelLoaded()
    {
        CoopEnemyScalingRunner.Instance.StartCoroutine(ProcessSceneEnemiesCoroutine());
    }

    private static IEnumerator ProcessSceneEnemiesCoroutine()
    {
        // Breve espera de 0.15 segundos para que todas las entidades de la sala se despierten e inicialicen sus componentes
        yield return new WaitForSeconds(0.15f);
        ProcessSceneEnemies();
    }

    /// <summary>
    /// Recorre la sala actual aplicando el escalado de salud y la multiplicación de enemigos regulares.
    /// </summary>
    public static void ProcessSceneEnemies()
    {
        // Si el cooperativo no está activo y la dificultad está en 1x, no modificar nada
        if (!CoopGameConfig.IsCoopEnabled && Mathf.Approximately(ActiveEnemyMultiplier, 1.0f) && Mathf.Approximately(ActiveHealthMultiplier, 1.0f))
        {
            return;
        }

        Enemy[] sceneEnemies = UnityEngine.Object.FindObjectsOfType<Enemy>();
        if (sceneEnemies == null || sceneEnemies.Length == 0) return;

        List<Enemy> regularEnemiesToDuplicate = new List<Enemy>();

        for (int i = 0; i < sceneEnemies.Length; i++)
        {
            Enemy enemy = sceneEnemies[i];
            if (enemy == null) continue;

            // Ignorar muñecos de prueba y clones ya creados
            if (enemy.GetComponent<CoopClonedEnemyTag>() != null || enemy.name.Contains("DummyEnemy"))
            {
                continue;
            }

            // 1. Aplicar multiplicador de vida a la entidad (sea jefe o regular)
            ScaleEnemyHp(enemy, ActiveHealthMultiplier);

            // 2. Determinar si califica para duplicación de sala (no duplicar jefes ni partes hijas)
            if (!IsBoss(enemy) && !enemy.Status.Dead && enemy.gameObject.activeInHierarchy)
            {
                // Asegurar que no sea una sub-parte de otro enemigo
                if (enemy.transform.parent == null || enemy.transform.parent.GetComponentInParent<Enemy>() == null)
                {
                    regularEnemiesToDuplicate.Add(enemy);
                }
            }
        }

        // 3. Duplicación de enemigos regulares según ActiveEnemyMultiplier
        if (ActiveEnemyMultiplier > 1.01f && regularEnemiesToDuplicate.Count > 0)
        {
            for (int i = 0; i < regularEnemiesToDuplicate.Count; i++)
            {
                Enemy original = regularEnemiesToDuplicate[i];
                if (original == null || original.Status.Dead) continue;

                int clonesCount = 0;
                if (Mathf.Approximately(ActiveEnemyMultiplier, 1.5f))
                {
                    // 50% de probabilidad balanceada (1 clone cada 2 enemigos)
                    if (i % 2 == 0) clonesCount = 1;
                }
                else if (Mathf.Approximately(ActiveEnemyMultiplier, 2.0f))
                {
                    // Doble: 1 clon exacto por enemigo
                    clonesCount = 1;
                }
                else if (Mathf.Approximately(ActiveEnemyMultiplier, 2.5f))
                {
                    // 1 clon garantizado + 50% de un segundo clon
                    clonesCount = (i % 2 == 0) ? 2 : 1;
                }
                else if (ActiveEnemyMultiplier >= 3.0f)
                {
                    // Triple: 2 clones por cada enemigo
                    clonesCount = 2;
                }

                for (int c = 1; c <= clonesCount; c++)
                {
                    CloneRegularEnemy(original, c);
                }
            }
        }
    }

    public static void ScaleEnemyHp(Enemy enemy, float mult)
    {
        if (enemy == null) return;
        if (enemy.GetComponent<CoopScaledEnemyTag>() != null) return;

        float effectiveMult = mult;
        if (IsBoss(enemy) && CoopGameConfig.IsCoopEnabled)
        {
            // Para jefes: escala de x2.0 (2 jugadores) hasta x3.5 (4 jugadores) máximo
            int players = Mathf.Clamp(CoopGameConfig.PlayerCount, 2, 4);
            float playerBaseline = 2.0f + (players - 2) * 0.75f; // 2P: 2.0f, 3P: 2.75f, 4P: 3.5f

            if (mult > 1.01f)
            {
                // Mapear proporcionalmente la dificultad de enemigos normales [1.5x, 2.5x] a la escala de jefes [2.0x, 3.5x]
                float t = Mathf.InverseLerp(1.5f, 2.5f, mult);
                float customBossMult = Mathf.Lerp(2.0f, 3.5f, t);
                effectiveMult = Mathf.Clamp(Mathf.Max(customBossMult, playerBaseline), 2.0f, 3.5f);
            }
            else
            {
                effectiveMult = Mathf.Clamp(playerBaseline, 2.0f, 3.5f);
            }
        }
        else if (!IsBoss(enemy) && CoopGameConfig.IsCoopEnabled && mult > 1.01f)
        {
            // Para enemigos normales: inicia en x1.5 hasta x2.5 máximo
            effectiveMult = Mathf.Clamp(mult, 1.5f, 2.5f);
        }

        if (effectiveMult <= 1.01f) return;

        enemy.gameObject.AddComponent<CoopScaledEnemyTag>();

        if (enemy.Stats != null && enemy.Stats.Life != null)
        {
            float baseHp = enemy.Stats.Life.Base;
            if (baseHp <= 0f)
            {
                baseHp = enemy.Stats.Life.CurrentMax > 0f ? enemy.Stats.Life.CurrentMax : enemy.Stats.Life.Current;
            }

            if (baseHp > 0f)
            {
                float newMaxHp = baseHp * effectiveMult;

                // CRÍTICO: En VariableAttribute (Life), set_MaxValue(newMaxHp) ya actualiza internamente
                // _maxValue = newMaxHp y llama a base.set_Base(newMaxHp), recalculando la vida.
                // Si adicionalmente se llamara a SetPermanentBonus(bonus), ese bono se sumaría dos veces
                // a Final (Final = Base + PermanetBonus = newMaxHp + bonus), mientras Current se clampea
                // a MaxValue (newMaxHp). Como resultado, la barra de vida del jefe leía:
                // BarTarget = Current / Final = newMaxHp / (newMaxHp + bonus) (~60%), apareciendo incompleta.
                //
                // Por ello:
                // 1) Asignamos MaxValue = newMaxHp (lo que setea Base = newMaxHp).
                // 2) Seteamos PermanetBonus a 0 para que Final == Base == newMaxHp.
                // 3) SetToCurrentMax() setea Current a newMaxHp.
                // 4) Actualizamos LifeBase y LifeMaximun en EntityStats.
                enemy.Stats.Life.MaxValue = newMaxHp;
                enemy.Stats.Life.SetPermanentBonus(0f);
                enemy.Stats.Life.SetToCurrentMax();
                enemy.Stats.Life.Current = newMaxHp;

                enemy.Stats.LifeBase = newMaxHp;
                enemy.Stats.LifeMaximun = newMaxHp;

                if (IsBoss(enemy) && Main.CoopLocal != null)
                {
                    Blasphemous.ModdingAPI.ModLog.Info(
                        $"[CoopEnemyScaling] Boss '{enemy.name}' escalado: Base={enemy.Stats.Life.Base:F0}, Final={enemy.Stats.Life.Final:F0}, Current={enemy.Stats.Life.Current:F0}, CurrentMax={enemy.Stats.Life.CurrentMax:F0} (x{effectiveMult:F2})",
                        Main.CoopLocal);
                }
            }
        }
    }

    private static void CloneRegularEnemy(Enemy original, int cloneIndex)
    {
        try
        {
            GameObject cloneObj = UnityEngine.Object.Instantiate(original.gameObject, original.transform.parent);
            cloneObj.name = original.name + $"_CoopClone_{cloneIndex}";

            // Desvincular de persistencia para no sobreescribir el estado de la sala al morir
            foreach (var p in cloneObj.GetComponentsInChildren<PersistentObject>(true))
            {
                UnityEngine.Object.Destroy(p);
            }
            foreach (var pe in cloneObj.GetComponentsInChildren<Gameplay.GameControllers.Enemies.Framework.Persistence.PersistentEnemy>(true))
            {
                UnityEngine.Object.Destroy(pe);
            }

            // Marcar con tags para no re-clonar ni re-escalar
            cloneObj.AddComponent<CoopClonedEnemyTag>();
            cloneObj.AddComponent<CoopScaledEnemyTag>();

            // Desplazar ligeramente en horizontal para no solaparse en el mismo píxel
            float offsetDir = (cloneIndex % 2 == 1) ? 1.0f : -1.0f;
            Vector3 spawnPos = original.transform.position + new Vector3(offsetDir * 1.15f, 0f, 0f);
            cloneObj.transform.position = spawnPos;

            Enemy cloneEnemy = cloneObj.GetComponent<Enemy>();
            if (cloneEnemy != null)
            {
                cloneEnemy.Status.Dead = false;
                if (cloneEnemy.Stats?.Life != null)
                {
                    float baseHp = cloneEnemy.Stats.Life.Base;
                    if (baseHp <= 0f)
                    {
                        baseHp = cloneEnemy.Stats.Life.CurrentMax > 0f ? cloneEnemy.Stats.Life.CurrentMax : cloneEnemy.Stats.Life.Current;
                    }
                    if (baseHp > 0f && ActiveHealthMultiplier > 1.01f)
                    {
                        float newMaxHp = baseHp * ActiveHealthMultiplier;
                        cloneEnemy.Stats.Life.MaxValue = newMaxHp;
                        cloneEnemy.Stats.Life.SetPermanentBonus(0f);
                        cloneEnemy.Stats.Life.SetToCurrentMax();
                        cloneEnemy.Stats.Life.Current = newMaxHp;
                        cloneEnemy.Stats.LifeBase = newMaxHp;
                        cloneEnemy.Stats.LifeMaximun = newMaxHp;
                    }
                    else
                    {
                        cloneEnemy.Stats.Life.SetToCurrentMax();
                    }
                }
            }
        }
        catch { }
    }
}

/// <summary>
/// Intercepta el inicio de la barra de vida de cualquier Jefe para asegurar que esté completamente llena (100%)
/// y que el escalado de salud se aplique correctamente incluso si el jefe apareció después de una cinemática.
/// </summary>
[HarmonyPatch(typeof(Gameplay.UI.UIController), "ShowBossHealth")]
internal static class UIController_ShowBossHealth_CoopScaling_Patch
{
    private static void Prefix(Entity entity)
    {
        if (entity is Enemy enemy)
        {
            CoopEnemyScaling.ScaleEnemyHp(enemy, CoopEnemyScaling.ActiveHealthMultiplier);

            // Garantizar que la vida actual iguale a la vida máxima final
            if (enemy.Stats != null && enemy.Stats.Life != null)
            {
                if (enemy.Stats.Life.Current < enemy.Stats.Life.Final)
                {
                    enemy.Stats.Life.Current = enemy.Stats.Life.Final;
                }
            }
        }
    }

    private static void Postfix(Gameplay.UI.UIController __instance)
    {
        if (__instance == null) return;

        // Asegurar que las barras de relleno del widget BossHealth comiencen al 100%
        try
        {
            var bhField = AccessTools.Field(typeof(Gameplay.UI.UIController), "bossHealth");
            var bh = bhField?.GetValue(__instance) as Gameplay.UI.Others.UIGameLogic.BossHealth;
            if (bh != null)
            {
                var healthImg = AccessTools.Field(typeof(Gameplay.UI.Others.UIGameLogic.BossHealth), "health")?.GetValue(bh) as UnityEngine.UI.Image;
                var lossImg = AccessTools.Field(typeof(Gameplay.UI.Others.UIGameLogic.BossHealth), "loss")?.GetValue(bh) as UnityEngine.UI.Image;
                if (healthImg != null) healthImg.fillAmount = 1f;
                if (lossImg != null) lossImg.fillAmount = 1f;
            }
        }
        catch { }
    }
}

/// <summary>
/// Garantiza que el ratio de vida del jefe nunca devuelva menos de 1.0f si el jefe tiene toda su vida.
/// </summary>
[HarmonyPatch(typeof(Gameplay.UI.Others.UIGameLogic.BossHealth), "get_BarTarget")]
internal static class BossHealth_BarTarget_Clamp_Patch
{
    private static void Postfix(Gameplay.UI.Others.UIGameLogic.BossHealth __instance, ref float __result)
    {
        if (__result > 1f)
        {
            __result = 1f;
        }
        else if (__result < 0f)
        {
            __result = 0f;
        }
    }
}
