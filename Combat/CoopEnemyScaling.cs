using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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
    /// <summary>Vida base original (antes de cualquier escalado); nunca se re-infla a partir de la vida ya escalada.</summary>
    public float BaseLife;
    /// <summary>Multiplicador final aplicado a la vida (coop x sala, con topes).</summary>
    public float AppliedHpMult = 1f;
    /// <summary>Clasificacion jefe/normal calculada UNA vez antes de tocar la vida (la heuristica life>=350 es fragil tras escalar).</summary>
    public bool IsBossCached;
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

    // ---------------------------------------------------------------------------------
    // Escalado por sala / zona (EDITAR AQUI). Mapeo D## -> tier SIN verificar contra el juego:
    // cada carga de nivel loguea "[CoopEnemyScaling] Nivel 'X' -> D## tier N" para corregirlo via playtest.
    // ---------------------------------------------------------------------------------
    public static float[] TierHp = { 0.90f, 1.00f, 1.10f, 1.20f };
    public static float[] TierDmg = { 0.90f, 1.00f, 1.10f, 1.20f };
    public const int UnparseableTier = -1; // factor 1.0
    public const int DefaultParseableTier = 3;
    private static readonly Dictionary<int, int> ZoneTiers = BuildZoneTiers();
    private static Dictionary<int, int> BuildZoneTiers()
    {
        var d = new Dictionary<int, int>();
        foreach (int z in new[] { 1, 2, 3, 4 }) d[z] = 0;
        foreach (int z in new[] { 5, 6, 8, 9, 17 }) d[z] = 1;
        foreach (int z in new[] { 7, 10, 11, 12, 13, 14, 15, 16, 20 }) d[z] = 2;
        foreach (int z in new[] { 21, 22, 23, 24 }) d[z] = 3;
        return d;
    }
    private static readonly Regex LevelNameRegex = new Regex(@"^D(\d\d)(B?)Z(\d\d)S(\d\d)", RegexOptions.IgnoreCase);

    public const float ElemHpNormal = 1.10f;
    public static readonly float[] ElemHpBossByPlayers = { 1.20f, 1.15f, 1.10f }; // 2P, 3P, 4P
    public const float MaxNormalHp = 3.0f;
    public const float MaxBossHp = 4.5f;
    public const float MaxEnemyDmg = 1.35f;
    public static readonly float[] PlayerDmgFactor = { 1.00f, 1.05f, 1.10f }; // 2P, 3P, 4P

    private static string GetCurrentLevelName()
    {
        try { return Core.LevelManager?.currentLevel?.LevelName; } catch { return null; }
    }

    /// <summary>Tier de la sala actual (0-3) o -1 si el nombre de escena no se puede parsear.</summary>
    public static int GetCurrentRoomTier()
    {
        return GetTierForLevelName(GetCurrentLevelName(), out _);
    }

    private static int GetTierForLevelName(string levelName, out int zoneNum)
    {
        zoneNum = -1;
        if (string.IsNullOrEmpty(levelName)) return UnparseableTier;
        Match m = LevelNameRegex.Match(levelName);
        if (!m.Success) return UnparseableTier;
        zoneNum = int.Parse(m.Groups[1].Value);
        return ZoneTiers.TryGetValue(zoneNum, out int tier) ? tier : DefaultParseableTier;
    }

    private static int PlayersIdx => Mathf.Clamp(CoopGameConfig.PlayerCount, 2, 4) - 2;

    private static float ZoneHp(int tier) => tier < 0 ? 1f : TierHp[Mathf.Clamp(tier, 0, TierHp.Length - 1)];
    private static float ZoneDmg(int tier) => tier < 0 ? 1f : TierDmg[Mathf.Clamp(tier, 0, TierDmg.Length - 1)];

    private static float ElemHp(bool isBoss)
    {
        if (!CoopGameConfig.EnableElementalUpgrades) return 1f;
        return isBoss ? ElemHpBossByPlayers[PlayersIdx] : ElemHpNormal;
    }

    public static float GetRoomHpFactor(bool isBoss)
    {
        if (!CoopGameConfig.IsCoopEnabled || !CoopGameConfig.EnableRoomScaling) return 1f;
        return ZoneHp(GetCurrentRoomTier()) * ElemHp(isBoss);
    }

    public static float GetRoomDmgFactor()
    {
        if (!CoopGameConfig.IsCoopEnabled || !CoopGameConfig.EnableRoomScaling) return 1f;
        return Mathf.Min(ZoneDmg(GetCurrentRoomTier()) * PlayerDmgFactor[PlayersIdx], MaxEnemyDmg);
    }

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

    private static readonly HashSet<Type> DuplicationExcludedTypes = new HashSet<Type>
    {
        typeof(Gameplay.GameControllers.Enemies.TrinityMinion.TrinityMinion),
        typeof(Gameplay.GameControllers.Enemies.ChasingHead.ChasingHead),
        typeof(Gameplay.GameControllers.Enemies.WaxCrawler.WaxCrawler),
        typeof(Gameplay.GameControllers.Enemies.CrossCrawler.CrossCrawler)
    };

    /// <summary>
    /// Enemigos excluidos de la duplicación de sala (mantienen escalado de vida).
    /// </summary>
    public static bool IsExcludedFromDuplication(Enemy enemy)
    {
        if (enemy == null) return true;
        if (DuplicationExcludedTypes.Contains(enemy.GetType())) return true;
        if (enemy is Gameplay.GameControllers.Enemies.ExplodingEnemy.ExplodingEnemy exploding && exploding.IsSummoned)
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// Identifica si una entidad enemiga es considerada un Jefe.
    /// </summary>
    public static bool IsBoss(Enemy enemy)
    {
        if (enemy == null) return false;
        if (enemy is TestDummyEnemy || (!string.IsNullOrEmpty(enemy.Id) && enemy.Id.StartsWith("DUMMY", StringComparison.OrdinalIgnoreCase))) return false;
        // Resultado cacheado antes del escalado: evita que un normal escalado (life>=350) se reclasifique como jefe
        var scaledTag = enemy.GetComponent<CoopScaledEnemyTag>();
        if (scaledTag != null) return scaledTag.IsBossCached;
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
        try
        {
            string ln = GetCurrentLevelName();
            int tier = GetTierForLevelName(ln, out int zone);
            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info(
                    $"[CoopEnemyScaling] Nivel '{ln}' -> D{zone:00} tier {tier} (zoneHp x{ZoneHp(tier):F2}, zoneDmg x{ZoneDmg(tier):F2}) elemental={CoopGameConfig.EnableElementalUpgrades} roomScaling={CoopGameConfig.EnableRoomScaling}",
                    Main.CoopLocal);
            }
        }
        catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "CoopEnemyScaling.cs:349"); }
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
        // Online client: enemies are host-authoritative puppets; no scaling, no cloning.
        if (Blasphemous.CoopLocal.MultiPlayer.CoopNetworkManager.IsOnlineClient) return;

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

            // Clasificar UNA vez antes de tocar la vida (la heuristica life>=350 cambia tras escalar)
            bool isBoss = IsBoss(enemy);

            // 1. Aplicar multiplicador de vida a la entidad (sea jefe o regular)
            ScaleEnemyHp(enemy, ActiveHealthMultiplier);

            // 2. Determinar si califica para duplicación de sala (no duplicar jefes, excluidos ni partes hijas)
            if (!isBoss && !IsExcludedFromDuplication(enemy) && !enemy.Status.Dead && enemy.gameObject.activeInHierarchy)
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

    private static bool IsDummy(Enemy enemy)
    {
        return enemy is TestDummyEnemy || (!string.IsNullOrEmpty(enemy.Id) && enemy.Id.StartsWith("DUMMY", StringComparison.OrdinalIgnoreCase));
    }

    private static void ApplyLife(Enemy enemy, float baseHp, float totalMult)
    {
        float newMaxHp = baseHp * totalMult;
        // CRITICO: MaxValue = newMaxHp ya setea Base; el bono permanente se pone a 0 para que Final == Base == newMaxHp
        // (si no, Final = Base + bono y la barra de jefe se leeria incompleta).
        enemy.Stats.Life.MaxValue = newMaxHp;
        enemy.Stats.Life.SetPermanentBonus(0f);
        enemy.Stats.Life.SetToCurrentMax();
        enemy.Stats.Life.Current = newMaxHp;
        enemy.Stats.LifeBase = newMaxHp;
        enemy.Stats.LifeMaximun = newMaxHp;
    }

    public static void ScaleEnemyHp(Enemy enemy, float mult)
    {
        if (enemy == null) return;
        if (enemy.GetComponent<CoopScaledEnemyTag>() != null) return;
        if (IsDummy(enemy)) return;

        // Clasificar UNA vez, antes de tocar la vida
        bool isBoss = IsBoss(enemy);

        float effectiveMult = mult;
        if (isBoss && CoopGameConfig.IsCoopEnabled)
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
        else if (!isBoss && CoopGameConfig.IsCoopEnabled && mult > 1.01f)
        {
            // Para enemigos normales: inicia en x1.5 hasta x2.5 máximo
            effectiveMult = Mathf.Clamp(mult, 1.5f, 2.5f);
        }

        if (effectiveMult <= 1.01f) return;

        if (enemy.Stats == null || enemy.Stats.Life == null) return;

        float baseHp = enemy.Stats.Life.Base;
        if (baseHp <= 0f)
        {
            baseHp = enemy.Stats.Life.CurrentMax > 0f ? enemy.Stats.Life.CurrentMax : enemy.Stats.Life.Current;
        }
        if (baseHp <= 0f) return;

        float roomFactor = GetRoomHpFactor(isBoss);
        float finalMult = Mathf.Min(effectiveMult * roomFactor, isBoss ? MaxBossHp : MaxNormalHp);

        var tag = enemy.gameObject.AddComponent<CoopScaledEnemyTag>();
        tag.IsBossCached = isBoss;
        tag.BaseLife = baseHp;
        tag.AppliedHpMult = finalMult;

        ApplyLife(enemy, baseHp, finalMult);

        int tier = GetCurrentRoomTier();
        DashParryDebugLog.LogVerbose($"[CoopEnemyScaling] {enemy.name} sala='{GetCurrentLevelName()}' tier={tier} zoneHp={ZoneHp(tier):F2} elemHp={ElemHp(isBoss):F2} coopMult={effectiveMult:F2} final=x{finalMult:F2} vida {baseHp:F0}->{enemy.Stats.Life.Final:F0}");

        if (isBoss && Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info(
                $"[CoopEnemyScaling] Boss '{enemy.name}' sala='{GetCurrentLevelName()}' tier={tier} escalado: Base={baseHp:F0}, Final={enemy.Stats.Life.Final:F0}, Current={enemy.Stats.Life.Current:F0}, CurrentMax={enemy.Stats.Life.CurrentMax:F0} (coop x{effectiveMult:F2} * sala x{roomFactor:F2} = x{finalMult:F2})",
                Main.CoopLocal);
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
            // Instantiate ya copia el tag del original; se reutiliza para no duplicarlo
            CoopScaledEnemyTag cloneTag = cloneObj.GetComponent<CoopScaledEnemyTag>() ?? cloneObj.AddComponent<CoopScaledEnemyTag>();
            CoopScaledEnemyTag origTag = original.GetComponent<CoopScaledEnemyTag>();

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
                    // Mismo factor y misma vida base que el original (Life.Base del clon puede venir ya escalado por Instantiate)
                    if (origTag != null && origTag.BaseLife > 0f && origTag.AppliedHpMult > 1.01f)
                    {
                        cloneTag.IsBossCached = false;
                        cloneTag.BaseLife = origTag.BaseLife;
                        cloneTag.AppliedHpMult = origTag.AppliedHpMult;
                        ApplyLife(cloneEnemy, origTag.BaseLife, origTag.AppliedHpMult);
                        DashParryDebugLog.LogVerbose($"[CoopEnemyScaling] clon {cloneObj.name} final=x{origTag.AppliedHpMult:F2} vida {origTag.BaseLife:F0}->{cloneEnemy.Stats.Life.Final:F0}");
                    }
                    else
                    {
                        cloneEnemy.Stats.Life.SetToCurrentMax();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DashParryDebugLog.LogOnce($"CoopEnemyScaling.Clone:{original?.name}", $"[CoopEnemyScaling] Error clonando {original?.name}: {ex.Message}", isWarning: true);
        }
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
            var bhField = Reflect.Field(typeof(Gameplay.UI.UIController), "bossHealth");
            var bh = bhField?.GetValue(__instance) as Gameplay.UI.Others.UIGameLogic.BossHealth;
            if (bh != null)
            {
                var healthImg = Reflect.Field(typeof(Gameplay.UI.Others.UIGameLogic.BossHealth), "health")?.GetValue(bh) as UnityEngine.UI.Image;
                var lossImg = Reflect.Field(typeof(Gameplay.UI.Others.UIGameLogic.BossHealth), "loss")?.GetValue(bh) as UnityEngine.UI.Image;
                if (healthImg != null) healthImg.fillAmount = 1f;
                if (lossImg != null) lossImg.fillAmount = 1f;
            }
        }
        catch (Exception ex)
        {
            DashParryDebugLog.LogOnce("CoopEnemyScaling.ShowBossHealth", $"[CoopEnemyScaling] ShowBossHealth error: {ex.Message}", isWarning: true);
        }
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
