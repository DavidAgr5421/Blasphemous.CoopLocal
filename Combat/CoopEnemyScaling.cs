using System;
using System.Collections;
using System.Collections.Generic;
using Framework.FrameworkCore;
using Framework.Managers;
using Gameplay.GameControllers.Entities;
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

    public static readonly float[] HealthMultipliers = { 2.5f, 3.25f, 4.0f, 5.0f };
    public static readonly string[] HealthLabels = { "+150% (x2.5)", "+225% (x3.25)", "+300% (x4.0)", "+400% (x5.0)" };

    public static readonly float[] BossResistanceLevels = { 2.5f, 3.25f, 4.0f };
    public static readonly string[] BossResistanceLabels = { "Estándar (30% - 65%)", "Alta (45% - 80%)", "Extrema (60% - 90%)" };

    // Configuración activa
    public static float ActiveEnemyMultiplier = 1.0f;
    public static float ActiveHealthMultiplier = 2.5f;
    public static float ActiveBossResistanceFactor = 2.5f;

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
                ActiveHealthMultiplier = 2.5f;
                ActiveBossResistanceFactor = 2.5f;
                break;
            case 3:
                ActiveEnemyMultiplier = 2.0f;
                ActiveHealthMultiplier = 3.25f;
                ActiveBossResistanceFactor = 3.25f;
                break;
            case 4:
            default:
                ActiveEnemyMultiplier = 2.5f;
                ActiveHealthMultiplier = 4.0f;
                ActiveBossResistanceFactor = 4.0f;
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
        float effectiveFactor = Mathf.Clamp(ActiveBossResistanceFactor, 2.5f, 4.0f);
        float mult = Mathf.Lerp(1.0f, 1.35f, Mathf.InverseLerp(2.5f, 4.0f, effectiveFactor));
        float rawRes = profile.BasePoisonResistance * mult;
        return Mathf.Clamp(rawRes, 0.30f, 0.90f);
    }

    public static float GetBossBleedResistance(Enemy enemy)
    {
        if (!IsBoss(enemy)) return 0f;
        BossResistanceProfile profile = GetBossProfile(enemy);
        float effectiveFactor = Mathf.Clamp(ActiveBossResistanceFactor, 2.5f, 4.0f);
        float mult = Mathf.Lerp(1.0f, 1.35f, Mathf.InverseLerp(2.5f, 4.0f, effectiveFactor));
        float rawRes = profile.BaseBleedResistance * mult;
        return Mathf.Clamp(rawRes, 0.30f, 0.90f);
    }

    public static int GetBossExtraBleedHits(Enemy enemy)
    {
        if (!IsBoss(enemy)) return 0;
        BossResistanceProfile profile = GetBossProfile(enemy);
        float effectiveFactor = Mathf.Max(2.5f, ActiveBossResistanceFactor);
        return Mathf.RoundToInt(profile.ExtraBleedHits * (effectiveFactor / 2.5f));
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

    private static void ScaleEnemyHp(Enemy enemy, float mult)
    {
        if (enemy == null) return;
        if (enemy.GetComponent<CoopScaledEnemyTag>() != null) return;

        float effectiveMult = mult;
        if (IsBoss(enemy) && CoopGameConfig.IsCoopEnabled)
        {
            int players = Mathf.Max(2, CoopGameConfig.PlayerCount);
            float bossMinMult = 2.5f + (players - 2) * 1.0f; // 2P: 2.5f, 3P: 3.5f, 4P: 4.5f
            effectiveMult = Mathf.Max(mult, bossMinMult);
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
                float bonus = newMaxHp - baseHp;

                // CRÍTICO: En VariableAttribute, CurrentMaxWithoutFactor = Mathf.Min(CalculateValue(), MaxValue).
                // En los enemigos de Blasphemous, Life.MaxValue se inicializa a LifeMaximun (suele ser igual a Base o 500f).
                // Si MaxValue NO se incrementa junto con el bonus, SetPermanentBonus se topa con el techo MaxValue y la vida NO sube.
                if (enemy.Stats.Life.MaxValue < newMaxHp)
                {
                    enemy.Stats.Life.MaxValue = newMaxHp;
                }
                enemy.Stats.Life.SetPermanentBonus(bonus);
                enemy.Stats.Life.SetToCurrentMax();
                if (enemy.Stats.Life.Current < newMaxHp)
                {
                    enemy.Stats.Life.Current = newMaxHp;
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
                        float bonus = newMaxHp - baseHp;
                        if (cloneEnemy.Stats.Life.MaxValue < newMaxHp)
                        {
                            cloneEnemy.Stats.Life.MaxValue = newMaxHp;
                        }
                        cloneEnemy.Stats.Life.SetPermanentBonus(bonus);
                        cloneEnemy.Stats.Life.SetToCurrentMax();
                        if (cloneEnemy.Stats.Life.Current < newMaxHp)
                        {
                            cloneEnemy.Stats.Life.Current = newMaxHp;
                        }
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
