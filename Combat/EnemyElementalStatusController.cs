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

internal sealed class EnemyElementalStatusController : MonoBehaviour
{
    private Enemy enemy;
    private float maxObservedLife;

    // Estado de Veneno
    private int activePoisonTier;
    private float basePoisonDps;
    private float poisonDps;
    private float poisonRemainingTime;
    private float poisonMaxDuration = 5f;
    private float poisonTickTimer;
    private int poisonHitCount = 0;
    private int poisonHitThreshold = 4;
    private int poisonBonusPercent = 0; // 0, 50, 100, 150
    private int lastPoisonHitFrame = -1;
    private Penitent lastPoisonAttacker;
    private bool poisonSpreadDone;

    // Estado de Sangrado
    private int activeBleedTier;
    private int bleedStacks;
    private int bleedThreshold = 6;
    private int bleedApplicationsCount = 0;
    private int bleedBonusPercent = 0;
    private float bleedStackDecayTimer;
    private float bleedProgressiveDps;
    private float bleedProgressiveRemainingTime;
    private float bleedProgressiveMaxDuration = 5f;
    private float bleedTickTimer;
    private int lastBleedHitFrame = -1;
    private Penitent lastBleedAttacker;
    private float bleedAppDecayTimer;
    private float lastSangriaFxTime = -10f;

    // Estado de Electricidad (umbral -> congelamiento)
    private int electricHitCount;
    private int lastElectricHitFrame = -1;
    private Penitent lastElectricAttacker;
    private bool electricFrozen;
    private Coroutine electricFreezeRoutine;
    private Animator[] frozenAnimators;
    private float[] frozenAnimatorSpeeds;
    private bool frozeBehaviour;
    private float electricImmuneUntil;
    private int electricThresholdCache = 5;

    // Estado de Maldición
    private float curseRemaining;
    private int curseTier;
    private float curseBaseReduction;
    private float nextCurseDrainFromThisEnemy;
    private int curseHitCount;
    private int curseHitThreshold = 5;
    private float curseThresholdMultiplier = 1.4f;
    private float curseMaxDuration = 6f;
    private bool curseEnhanced;
    private int lastCurseHitFrame = -1;
    private Penitent lastCurseAttacker;
    private static readonly Dictionary<int, float> NextCurseDrainTime = new Dictionary<int, float>();

    // Feedback visual sobre el enemigo
    private SpriteRenderer[] spriteRenderers;
    private Color[] originalColors;
    private float tintRestoreTimer;

    // Barras visuales de duración descendente sobre el enemigo
    private GameObject barsContainerObj;
    private StatusBarVisual poisonBar;
    private StatusBarVisual bleedBar;
    private StatusBarVisual curseBar;
    private StatusBarVisual electricBar;

    private static Sprite _whiteBarSprite;
    private static Sprite GetBarSprite()
    {
        if (_whiteBarSprite == null)
        {
            Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            _whiteBarSprite = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0f, 0.5f), 1f);
        }
        return _whiteBarSprite;
    }

    private class StatusBarVisual
    {
        public GameObject rootObj;
        public SpriteRenderer bgRenderer;
        public SpriteRenderer fillRenderer;
        public float barWidth = 1.15f;
        public float barHeight = 0.10f;

        public StatusBarVisual(Transform parent, string name, Color fillColor)
        {
            rootObj = new GameObject(name);
            rootObj.transform.SetParent(parent, false);

            // Fondo (borde oscuro semitransparente)
            GameObject bgObj = new GameObject("Bg");
            bgObj.transform.SetParent(rootObj.transform, false);
            bgObj.transform.localPosition = new Vector3(-barWidth * 0.5f - 0.02f, 0f, 0f);
            bgObj.transform.localScale = new Vector3(barWidth + 0.04f, barHeight + 0.04f, 1f);
            bgRenderer = bgObj.AddComponent<SpriteRenderer>();
            bgRenderer.sprite = GetBarSprite();
            bgRenderer.color = new Color(0f, 0f, 0f, 0.85f);
            bgRenderer.sortingOrder = 30000;

            // Relleno (verde o rojo)
            GameObject fillObj = new GameObject("Fill");
            fillObj.transform.SetParent(rootObj.transform, false);
            fillObj.transform.localPosition = new Vector3(-barWidth * 0.5f, 0f, 0f);
            fillObj.transform.localScale = new Vector3(barWidth, barHeight, 1f);
            fillRenderer = fillObj.AddComponent<SpriteRenderer>();
            fillRenderer.sprite = GetBarSprite();
            fillRenderer.color = fillColor;
            fillRenderer.sortingOrder = 30001;

            rootObj.SetActive(false);
        }

        public void SetFill(float fillRatio, float yOffset)
        {
            fillRatio = Mathf.Clamp01(fillRatio);
            rootObj.transform.localPosition = new Vector3(0f, yOffset, 0f);
            fillRenderer.transform.localScale = new Vector3(barWidth * fillRatio, barHeight, 1f);
            if (!rootObj.activeSelf) rootObj.SetActive(true);
        }

        public void SetColor(Color c)
        {
            if (fillRenderer != null) fillRenderer.color = c;
        }

        public void Hide()
        {
            if (rootObj != null && rootObj.activeSelf) rootObj.SetActive(false);
        }
    }

    private void Awake()
    {
        enemy = GetComponent<Enemy>();
        CacheRenderers();
        CreateWorldStatusBars();
    }

    public void ResetAllStatuses()
    {
        activePoisonTier = 0;
        basePoisonDps = 0f;
        poisonDps = 0f;
        poisonRemainingTime = 0f;
        poisonTickTimer = 0f;
        poisonHitCount = 0;
        poisonHitThreshold = 4;
        poisonBonusPercent = 0;
        lastPoisonHitFrame = -1;
        lastPoisonAttacker = null;
        poisonSpreadDone = false;

        activeBleedTier = 0;
        bleedStacks = 0;
        bleedThreshold = 6;
        bleedApplicationsCount = 0;
        bleedBonusPercent = 0;
        bleedStackDecayTimer = 0f;
        bleedProgressiveDps = 0f;
        bleedProgressiveRemainingTime = 0f;
        bleedTickTimer = 0f;
        lastBleedHitFrame = -1;
        lastBleedAttacker = null;
        bleedAppDecayTimer = 0f;

        electricHitCount = 0;
        lastElectricHitFrame = -1;
        lastElectricAttacker = null;
        EndElectricFreeze();
        electricImmuneUntil = 0f;

        curseRemaining = 0f;
        curseTier = 0;
        curseBaseReduction = 0f;
        curseHitCount = 0;
        curseEnhanced = false;
        lastCurseHitFrame = -1;
        lastCurseAttacker = null;

        poisonBar?.Hide();
        bleedBar?.Hide();
        curseBar?.Hide();
        electricBar?.Hide();
        if (barsContainerObj != null && barsContainerObj.activeSelf)
        {
            barsContainerObj.SetActive(false);
        }
        RestoreOriginalColors();
    }

    private void CacheRenderers()
    {
        if (tintRestoreTimer > 0f && originalColors != null && originalColors.Length > 0)
        {
            return;
        }
        var all = GetComponentsInChildren<SpriteRenderer>(true);
        if (all != null)
        {
            var list = new List<SpriteRenderer>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                if (barsContainerObj != null && all[i].transform.IsChildOf(barsContainerObj.transform))
                {
                    continue;
                }
                // El objeto Execution (finisher) y sus sprites son hijos del enemigo: teñirlos pintaba
                // de azul/verde/rojo la animación de ejecución del jugador. Nunca se tocan.
                if (all[i].GetComponentInParent<Tools.Level.Interactables.Execution>() != null
                    || all[i].GetComponentInParent<Tools.Level.Interactables.FakeExecution>() != null)
                {
                    continue;
                }
                list.Add(all[i]);
            }
            spriteRenderers = list.ToArray();
            originalColors = new Color[spriteRenderers.Length];
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                if (spriteRenderers[i] != null)
                {
                    originalColors[i] = spriteRenderers[i].color;
                }
            }
        }
    }

    private void CreateWorldStatusBars()
    {
        barsContainerObj = new GameObject("CoopElementalBarsContainer");
        barsContainerObj.transform.SetParent(transform, false);
        barsContainerObj.transform.localPosition = Vector3.zero;

        poisonBar = new StatusBarVisual(barsContainerObj.transform, "PoisonBar", new Color(0.25f, 0.95f, 0.35f, 1f));
        bleedBar = new StatusBarVisual(barsContainerObj.transform, "BleedBar", new Color(1f, 0.22f, 0.25f, 1f));
        curseBar = new StatusBarVisual(barsContainerObj.transform, "CurseBar", new Color(0.7f, 0.4f, 1f, 1f));
        electricBar = new StatusBarVisual(barsContainerObj.transform, "ElectricBar", new Color(0.45f, 0.9f, 1f, 1f));
    }

    private float GetEnemyTopY()
    {
        try
        {
            Collider2D col = GetComponentInChildren<Collider2D>();
            if (col != null && col.bounds.size.y > 0.1f)
            {
                return col.bounds.max.y - transform.position.y + 0.3f;
            }
            SpriteRenderer sr = GetComponentInChildren<SpriteRenderer>();
            if (sr != null && sr.bounds.size.y > 0.1f)
            {
                return sr.bounds.max.y - transform.position.y + 0.3f;
            }
        }
        catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemyElementalStatusController.cs:290"); }
        return 2.3f;
    }

    private float GetEnemyMaxHealth()
    {
        if (enemy == null || enemy.Stats == null || enemy.Stats.Life == null) return 100f;
        float finalLife = enemy.Stats.Life.Final;
        float currentLife = enemy.Stats.Life.Current;
        if (currentLife > maxObservedLife) maxObservedLife = currentLife;
        if (finalLife > maxObservedLife) maxObservedLife = finalLife;
        return Mathf.Max(1f, maxObservedLife);
    }

    private static bool IsBossEnemy(Enemy target, float maxHp)
    {
        if (target == null) return false;
        if (target is Combat.TestDummyEnemy || (!string.IsNullOrEmpty(target.Id) && target.Id.StartsWith("DUMMY", StringComparison.OrdinalIgnoreCase))) return false;
        // Flag cacheado al escalar (evita que un normal escalado >=350 se lea como jefe)
        var scaledTag = target.GetComponent<Combat.CoopScaledEnemyTag>();
        if (scaledTag != null) return scaledTag.IsBossCached;
        if (!string.IsNullOrEmpty(target.Id) && target.Id.StartsWith("BS", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        string typeName = target.GetType().FullName ?? "";
        if (typeName.IndexOf("Boss", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }
        return maxHp >= 350f;
    }

    private int ComputeBleedThreshold(Enemy target)
    {
        float maxHp = GetEnemyMaxHealth();
        int baseHits;
        if (IsBossEnemy(target, maxHp))
        {
            // En bosses el número de golpes para detonar la primera etapa es 10 (ajustado para ser más accesible)
            baseHits = 10;
        }
        else
        {
            // Enemigos normales: exactamente 6 golpes para la primera etapa
            baseHits = 6;
        }

        // Cada etapa subsiguiente incrementa los golpes requeridos multiplicando por 1.35 con función piso (floor)
        int threshold = baseHits;
        for (int i = 0; i < bleedApplicationsCount; i++)
        {
            threshold = Mathf.Max(threshold + 1, Mathf.FloorToInt(threshold * 1.35f));
        }
        return threshold;
    }

    private int ComputePoisonThreshold(Enemy target)
    {
        float maxHp = GetEnemyMaxHealth();
        if (IsBossEnemy(target, maxHp))
        {
            return 12;
        }
        // Reducido en 2 golpes para la primera fase: 6 -> 4 golpes
        return 4;
    }

    internal void ApplyPoison(Enemy target, Penitent attacker, int tier, int hitWeight = 1)
    {
        if (!CoopGameConfig.EnableElementalUpgrades) return;
        if (target == null || target.Status.Dead || Execution_OnUse_Patch.IsEnemyBeingExecuted(target)) return;

        if (lastPoisonAttacker == attacker && lastPoisonHitFrame == Time.frameCount)
        {
            return;
        }
        lastPoisonHitFrame = Time.frameCount;
        lastPoisonAttacker = attacker;

        enemy = target;
        GetEnemyMaxHealth();

        // Configurar umbral según sea jefe o enemigo regular si aún no ha escalado
        if (poisonBonusPercent == 0 && poisonHitCount == 0)
        {
            poisonHitThreshold = ComputePoisonThreshold(target);
        }
        else if (IsBossEnemy(target, GetEnemyMaxHealth()) && poisonHitThreshold < 12)
        {
            poisonHitThreshold = ComputePoisonThreshold(target);
        }

        SwordElementalUpgrades.GetPoisonParams(tier, out float dps, out float maxHpPctPerSec, out float duration);
        float bossPoisonRes = Combat.CoopEnemyScaling.GetBossPoisonResistance(target);
        dps = Mathf.Max(1.5f, (dps + GetEnemyMaxHealth() * maxHpPctPerSec) * (1f - bossPoisonRes));

        if (tier >= activePoisonTier || poisonRemainingTime <= 0f)
        {
            activePoisonTier = tier;
            basePoisonDps = dps;
            poisonMaxDuration = duration;
        }
        else
        {
            basePoisonDps = Mathf.Max(basePoisonDps, dps);
        }

        // Aumentar daño de veneno en 50% cada 6 golpes, escalando el umbral en 50% cada vez hasta máx +150%
        if (poisonBonusPercent < 150)
        {
            poisonHitCount += Mathf.Max(1, hitWeight);
            while (poisonBonusPercent < 150 && poisonHitCount >= poisonHitThreshold)
            {
                poisonHitCount -= poisonHitThreshold;
                poisonBonusPercent = Mathf.Min(150, poisonBonusPercent + 50);
                if (poisonBonusPercent >= 150)
                {
                    poisonHitCount = 0;
                }
                poisonHitThreshold = Mathf.Max(1, Mathf.RoundToInt(poisonHitThreshold * 1.5f));

                try
                {
                    Vector3 skullPos = enemy.transform.position + new Vector3(0f, Mathf.Clamp(GetEnemyTopY(), 1.0f, 3.8f) + 0.60f, 0f);
                    FloatingUpgradeSkull.Spawn(skullPos, ElementSkill.Poison, $"+{poisonBonusPercent}%");
                }
                catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemyElementalStatusController.cs:417"); }
                CoopElementalVfx.PlayPoisonBurst(enemy.transform.position + Vector3.up * Mathf.Clamp(GetEnemyTopY() * 0.5f, 0.4f, 2f), 1f);
            }
        }

        float damageMultiplier = 1.0f + (poisonBonusPercent / 100f);
        poisonDps = basePoisonDps * damageMultiplier;

        poisonRemainingTime = Mathf.Max(poisonRemainingTime, duration);
        poisonMaxDuration = Mathf.Max(poisonMaxDuration, poisonRemainingTime);
        if (poisonTickTimer <= 0f)
        {
            poisonTickTimer = 1.0f;
        }

        PulseEnemyColor(new Color(0.4f, 1f, 0.45f, 1f), 0.2f);
        UpdateStatusBars();
    }

    internal void AddBleedHit(Enemy target, Penitent attacker, int tier, int hitWeight = 1)
    {
        if (!CoopGameConfig.EnableElementalUpgrades) return;
        if (target == null || target.Status.Dead || Execution_OnUse_Patch.IsEnemyBeingExecuted(target)) return;

        if (lastBleedAttacker == attacker && lastBleedHitFrame == Time.frameCount)
        {
            return;
        }

        // Si el enemigo ya se encuentra con el estado de sangrado progresivo,
        // no se le puede volver a aplicar sangrado hasta que se acabe.
        // Corazon "Sangria": con bonus al maximo (+100%) cada golpe extiende el DoT +0.5s (max 8s).
        if (bleedProgressiveRemainingTime > 0f)
        {
            int sangriaIdx = CoopPlayerRegistry.GetPlayerIndex(attacker);
            if (bleedBonusPercent >= 100 && sangriaIdx >= 0 && SwordElementalUpgrades.IsHeartEquipped(sangriaIdx, SwordElementalUpgrades.HeartSangradoId))
            {
                lastBleedHitFrame = Time.frameCount;
                lastBleedAttacker = attacker;
                bleedProgressiveRemainingTime = Mathf.Min(8f, bleedProgressiveRemainingTime + 0.5f);
                bleedProgressiveMaxDuration = Mathf.Max(bleedProgressiveMaxDuration, bleedProgressiveRemainingTime);
                bleedAppDecayTimer = 10f;
                if (Time.time - lastSangriaFxTime > 1.0f)
                {
                    lastSangriaFxTime = Time.time;
                    try
                    {
                        Vector3 skullPos = target.transform.position + new Vector3(0f, Mathf.Clamp(GetEnemyTopY(), 1.0f, 3.8f) + 0.60f, 0f);
                        FloatingUpgradeSkull.Spawn(skullPos, ElementSkill.Bleed, "Sangria");
                    }
                    catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemyElementalStatusController.cs:467"); }
                }
                UpdateStatusBars();
            }
            return;
        }

        lastBleedHitFrame = Time.frameCount;
        lastBleedAttacker = attacker;
        bleedAppDecayTimer = 10f;

        enemy = target;
        float maxHp = GetEnemyMaxHealth();
        bleedThreshold = ComputeBleedThreshold(target);

        activeBleedTier = Mathf.Max(activeBleedTier, tier);
        bleedStacks += Mathf.Max(1, hitWeight);
        bleedStackDecayTimer = 8.0f;

        if (bleedStacks >= bleedThreshold)
        {
            bleedStacks = 0;
            bleedApplicationsCount++;
            bleedThreshold = ComputeBleedThreshold(target);

            // Escalado de porcentaje de sangrado por umbral: +20% por aplicación (hasta máx +100%)
            bleedBonusPercent = Mathf.Min(100, bleedApplicationsCount * 20);
            float bleedMultiplier = 1.0f + (bleedBonusPercent / 100f);

            SwordElementalUpgrades.GetBleedParams(activeBleedTier, out float maxHpPercent, out float progDps, out float progDuration);

            // Estallido inmediato de % de vida máxima mitigado por resistencia de jefe y escalado por bonus
            float bossBleedRes = Combat.CoopEnemyScaling.GetBossBleedResistance(target);
            float burstDamage = Mathf.Max(1f, maxHp * maxHpPercent * (1f - bossBleedRes) * bleedMultiplier);
            if (!IsBossEnemy(target, maxHp))
            {
                // Tope en enemigos normales: 15% de la vida base (sin escalado coop)
                float hpMult = target.GetComponent<Combat.CoopScaledEnemyTag>()?.AppliedHpMult ?? 1f;
                float baseHp = hpMult > 0.01f ? maxHp / hpMult : maxHp;
                burstDamage = Mathf.Min(burstDamage, baseHp * 0.15f);
            }
            DealDirectStatusDamage(burstDamage, "event:/SFX/Penitent/Damage/PenitentMagicDamage", isPoison: false, isBurst: true);
            CoopElementalVfx.PlayBleedBurst(target.transform.position + Vector3.up * Mathf.Clamp(GetEnemyTopY() * 0.5f, 0.4f, 2f), 1f);

            // Activar daño progresivo de sangrado (5 segundos de 3 / 4.5 / 6 DPS * multiplicador de mejora)
            bleedProgressiveDps = progDps * bleedMultiplier * (1f - bossBleedRes);
            bleedProgressiveRemainingTime = progDuration;
            bleedProgressiveMaxDuration = progDuration;
            bleedTickTimer = 1.0f;

            PulseEnemyColor(new Color(1f, 0.2f, 0.25f, 1f), 0.35f);

            // Notificación visual de estallido y mejora de sangrado: Cráneo demoníaco con porcentaje correspondiente (+20%, +40%, etc.)
            try
            {
                Vector3 skullPos = enemy.transform.position + new Vector3(0f, Mathf.Clamp(GetEnemyTopY(), 1.0f, 3.8f) + 0.60f, 0f);
                FloatingUpgradeSkull.Spawn(skullPos, ElementSkill.Bleed, $"+{bleedBonusPercent}%");
            }
            catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemyElementalStatusController.cs:525"); }

            if (attacker != null && attacker.PenitentAttack?.CurrentPenitentWeapon is PenitentSword sword)
            {
                try
                {
                    sword.SpawnBlood(enemy.transform.position, new Hit { DamageType = DamageArea.DamageType.Heavy });
                }
                catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemyElementalStatusController.cs:533"); }
            }
        }
        else
        {
            PulseEnemyColor(new Color(1f, 0.55f, 0.55f, 1f), 0.12f);
        }

        UpdateStatusBars();
    }

    private void OnDisable()
    {
        EndElectricFreeze();
    }

    // ---------------------------------------------------------------------
    // Electricidad: N golpes -> congelar al enemigo (host-only)
    // ---------------------------------------------------------------------
    private int lastElectricFxFrame = -1;
    private Penitent lastElectricFxAttacker;

    // Chispas de relámpago en el enemigo golpeado + daño eléctrico extra por golpe.
    private void ApplyElectricHitEffects(Enemy target, Penitent attacker, int tier)
    {
        if (lastElectricFxAttacker == attacker && lastElectricFxFrame == Time.frameCount) return;
        lastElectricFxFrame = Time.frameCount;
        lastElectricFxAttacker = attacker;
        enemy = target;

        try { CoopElementalVfx.PlayElectricSpark(target, GetEnemyTopY()); }
        catch (Exception ex) { DashParryDebugLog.Log("[Electric] ERROR chispas: " + ex.Message); }

        try
        {
            float maxHp = GetEnemyMaxHealth();
            float dmg = SwordElementalUpgrades.GetElectricoHitDamage(tier, maxHp, IsBossEnemy(target, maxHp));
            if (dmg > 0f && !EnemyDamageArea_TakeDamage_ElementalStatus_Patch.IsApplyingStatusDamage)
            {
                DealDirectStatusDamage(dmg, "", isPoison: false, isBurst: false, numberColor: new Color(0.45f, 0.9f, 1f, 1f), killerOverride: attacker);
            }
        }
        catch (Exception ex) { DashParryDebugLog.Log("[Electric] ERROR daño extra: " + ex.Message); }
    }

    internal void AddElectricHit(Enemy target, Penitent attacker, int tier, int hitWeight = 1, bool fromArc = false)
    {
        if (!CoopGameConfig.EnableElementalUpgrades) return;
        if (Blasphemous.CoopLocal.MultiPlayer.CoopNetworkManager.IsOnlineClient) return;
        if (target == null || target.Status.Dead || Execution_OnUse_Patch.IsEnemyBeingExecuted(target)) return;

        // Cada golpe con el Corazón del Relámpago: chispas visibles + daño eléctrico extra (también congelado/inmune)
        if (!fromArc) ApplyElectricHitEffects(target, attacker, tier);
        if (target.Status.Dead) return;

        if (electricFrozen) return;
        // Anti-lock: tras descongelar, el enemigo es inmune a acumular golpes electricos un tiempo
        if (Time.time < electricImmuneUntil) return;

        if (!fromArc)
        {
            if (lastElectricAttacker == attacker && lastElectricHitFrame == Time.frameCount) return;
            lastElectricHitFrame = Time.frameCount;
            lastElectricAttacker = attacker;
        }

        enemy = target;
        SwordElementalUpgrades.GetElectricoFreezeParams(tier, out int threshold, out float freezeSeconds, out float bossFreezeSeconds, out float shockDamage, out int chainTargets);
        electricThresholdCache = threshold;

        if (fromArc)
        {
            // Los arcos acumulan golpes pero nunca congelan (sin encadenado)
            electricHitCount = Mathf.Min(Mathf.Max(0, threshold - 1), electricHitCount + Mathf.Max(1, hitWeight));
            UpdateStatusBars();
            return;
        }

        electricHitCount += Mathf.Max(1, hitWeight);
        DashParryDebugLog.LogVerbose($"[Electric] golpe {electricHitCount}/{threshold} en {target.name} (tier {tier})");
        if (electricHitCount < threshold)
        {
            UpdateStatusBars();
            return;
        }

        electricHitCount = 0;
        bool isBoss = IsBossEnemy(target, GetEnemyMaxHealth());
        float seconds = isBoss ? bossFreezeSeconds : freezeSeconds;

        // No hay una señal fiable de "en mitad de un ataque" común a todos los enemigos: no se bloquea el congelamiento.
        DashParryDebugLog.LogVerbose($"[Electric] detección de ataque en curso no disponible; se congela igualmente a {target.name}");
        DashParryDebugLog.Log($"[Electric] Umbral alcanzado: congelando {target.name} {seconds:0.0}s (boss={isBoss}, atacante={CoopPlayerRegistry.GetPlayerIndex(attacker)})");

        try { StartElectricFreeze(seconds); }
        catch (Exception ex) { DashParryDebugLog.Log("[Electric] ERROR StartElectricFreeze: " + ex); }
        try { CoopLightningVfx.Play(target, GetEnemyTopY()); }
        catch (Exception ex) { DashParryDebugLog.Log("[Electric] ERROR CoopLightningVfx.Play: " + ex); }
        DashParryDebugLog.Log($"[Electric] Lanzando animación de aturdimiento en {target.name} (topY={GetEnemyTopY():0.00})");
        CoopElementalVfx.PlayElectricStun(target, seconds, GetEnemyTopY());

        try
        {
            Vector3 skullPos = target.transform.position + new Vector3(0f, Mathf.Clamp(GetEnemyTopY(), 1.0f, 3.8f) + 0.60f, 0f);
            FloatingUpgradeSkull.Spawn(skullPos, ElementSkill.Electric, $"{seconds:0.#}s");
        }
        catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemyElementalStatusController.cs:608"); }

        // Descarga al congelar
        DealDirectStatusDamage(shockDamage, "", isPoison: false, isBurst: false, numberColor: new Color(0.45f, 0.9f, 1f, 1f), killerOverride: attacker);

        // Arcos (tier>=2): corazon "Conductor"
        int playerIdx = CoopPlayerRegistry.GetPlayerIndex(attacker);
        if (chainTargets > 0 && tier >= 2 && playerIdx >= 0
            && SwordElementalUpgrades.IsHeartEquipped(playerIdx, SwordElementalUpgrades.HeartElectricoId)
            && !EnemyDamageArea_TakeDamage_ElementalStatus_Patch.IsApplyingStatusDamage)
        {
            DoElectricArcs(attacker, tier, chainTargets, shockDamage * 0.6f);
        }
        UpdateStatusBars();
    }

    private void DoElectricArcs(Penitent attacker, int tier, int chainTargets, float arcDamage)
    {
        if (enemy == null) return;
        Collider2D[] cols = Physics2D.OverlapCircleAll(enemy.transform.position, 5f);
        if (cols == null || cols.Length == 0) return;

        var found = new List<Enemy>();
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] == null) continue;
            Enemy e = cols[i].GetComponentInParent<Enemy>();
            if (e == null || e == enemy || e.Status.Dead || found.Contains(e)) continue;
            if (Execution_OnUse_Patch.IsEnemyBeingExecuted(e)) continue;
            found.Add(e);
        }
        if (found.Count == 0) return;

        Vector3 origin = enemy.transform.position;
        found.Sort((a, b) => (a.transform.position - origin).sqrMagnitude.CompareTo((b.transform.position - origin).sqrMagnitude));

        int n = Mathf.Min(chainTargets, found.Count);
        for (int i = 0; i < n; i++)
        {
            Enemy e = found[i];
            EnemyElementalStatusController c = e.GetComponent<EnemyElementalStatusController>() ?? e.gameObject.AddComponent<EnemyElementalStatusController>();
            c.ReceiveElectricArc(e, attacker, tier, arcDamage);
        }
    }

    private void ReceiveElectricArc(Enemy target, Penitent attacker, int tier, float damage)
    {
        if (target == null || target.Status.Dead) return;
        enemy = target;
        try
        {
            CoopLightningVfx.Play(target, GetEnemyTopY());
            Vector3 skullPos = target.transform.position + new Vector3(0f, Mathf.Clamp(GetEnemyTopY(), 1.0f, 3.8f) + 0.60f, 0f);
            FloatingUpgradeSkull.Spawn(skullPos, ElementSkill.Electric, "ARCO");
        }
        catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemyElementalStatusController.cs:663"); }
        DealDirectStatusDamage(damage, "", isPoison: false, isBurst: false, numberColor: new Color(0.45f, 0.9f, 1f, 1f), killerOverride: attacker);
        if (!target.Status.Dead)
        {
            AddElectricHit(target, attacker, tier, 2, fromArc: true);
        }
    }

    // Contagio: al morir un enemigo envenenado (tier>=2) propaga veneno a enemigos cercanos con 50% de duracion restante
    private void TrySpreadPoisonOnDeath()
    {
        poisonSpreadDone = true;
        if (Blasphemous.CoopLocal.MultiPlayer.CoopNetworkManager.IsOnlineClient) return;
        if (activePoisonTier < 2 || poisonRemainingTime <= 0f || enemy == null) return;
        int idx = CoopPlayerRegistry.GetPlayerIndex(lastPoisonAttacker);
        if (idx < 0 || !SwordElementalUpgrades.IsHeartEquipped(idx, SwordElementalUpgrades.HeartVenenoId)) return;

        Collider2D[] cols = Physics2D.OverlapCircleAll(enemy.transform.position, 4f);
        if (cols == null) return;
        var done = new List<Enemy>();
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] == null) continue;
            Enemy e = cols[i].GetComponentInParent<Enemy>();
            if (e == null || e == enemy || e.Status.Dead || done.Contains(e)) continue;
            if (Execution_OnUse_Patch.IsEnemyBeingExecuted(e)) continue;
            done.Add(e);
            EnemyElementalStatusController c = e.GetComponent<EnemyElementalStatusController>() ?? e.gameObject.AddComponent<EnemyElementalStatusController>();
            c.ReceiveSpreadPoison(e, lastPoisonAttacker, activePoisonTier, basePoisonDps, poisonRemainingTime * 0.5f);
        }
        if (done.Count > 0)
        {
            try
            {
                Vector3 skullPos = enemy.transform.position + new Vector3(0f, Mathf.Clamp(GetEnemyTopY(), 1.0f, 3.8f) + 0.60f, 0f);
                FloatingUpgradeSkull.Spawn(skullPos, ElementSkill.Poison, "Contagio");
            }
            catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemyElementalStatusController.cs:700"); }
            CoopElementalVfx.PlayPoisonBurst(enemy.transform.position, 1f);
        }
    }

    private void ReceiveSpreadPoison(Enemy target, Penitent attacker, int tier, float baseDps, float remaining)
    {
        if (target == null || target.Status.Dead || remaining <= 0f) return;
        enemy = target;
        GetEnemyMaxHealth();
        lastPoisonAttacker = attacker;
        if (poisonRemainingTime >= remaining && basePoisonDps >= baseDps) return;
        if (activePoisonTier <= tier || poisonRemainingTime <= 0f)
        {
            activePoisonTier = tier;
            basePoisonDps = Mathf.Max(basePoisonDps, baseDps);
        }
        poisonDps = basePoisonDps * (1f + poisonBonusPercent / 100f);
        poisonRemainingTime = Mathf.Max(poisonRemainingTime, remaining);
        poisonMaxDuration = Mathf.Max(poisonMaxDuration, poisonRemainingTime);
        if (poisonTickTimer <= 0f) poisonTickTimer = 1.0f;
        PulseEnemyColor(new Color(0.4f, 1f, 0.45f, 1f), 0.2f);
        UpdateStatusBars();
    }

    // EnemyBehaviour.BehaviourTree es inaccesible directamente (MethodAccessException en runtime): se lee por reflexión.
    private static bool IsBehaviourTreeRunning(EnemyBehaviour beh)
    {
        try
        {
            object tree = Reflect.Property(typeof(EnemyBehaviour), "BehaviourTree")?.GetValue(beh, null)
                          ?? Reflect.Field(typeof(EnemyBehaviour), "BehaviourTree")?.GetValue(beh);
            if (tree == null) return false;
            object running = Reflect.Property(tree.GetType(), "isRunning")?.GetValue(tree, null)
                             ?? Reflect.Field(tree.GetType(), "isRunning")?.GetValue(tree);
            return running is bool b && b;
        }
        catch { return false; }
    }

    private void StartElectricFreeze(float seconds)
    {
        if (electricFrozen || enemy == null) return;
        electricFrozen = true;

        // Patrón vanilla: pausar BehaviourTree + detener movimiento + congelar Animators (velocidad 0)
        frozeBehaviour = false;
        try
        {
            EnemyBehaviour beh = enemy.EnemyBehaviour != null ? enemy.EnemyBehaviour : enemy.GetComponentInChildren<EnemyBehaviour>();
            if (beh != null)
            {
                bool wasRunning = IsBehaviourTreeRunning(beh);
                beh.PauseBehaviour();
                beh.StopMovement();
                frozeBehaviour = wasRunning;
            }
        }
        catch (Exception ex)
        {
            DashParryDebugLog.LogVerbose("[Electric] Error pausando comportamiento: " + ex.Message);
        }

        var allAnimators = enemy.GetComponentsInChildren<Animator>(true);
        var enemyAnimators = new List<Animator>();
        foreach (var an in allAnimators)
        {
            // Los animators de la ejecución (hijos del enemigo) no se congelan: se quedarían a velocidad 0.
            if (an != null && an.GetComponentInParent<Tools.Level.Interactables.Execution>() == null
                && an.GetComponentInParent<Tools.Level.Interactables.FakeExecution>() == null)
            {
                enemyAnimators.Add(an);
            }
        }
        frozenAnimators = enemyAnimators.ToArray();
        frozenAnimatorSpeeds = new float[frozenAnimators.Length];
        for (int i = 0; i < frozenAnimators.Length; i++)
        {
            if (frozenAnimators[i] == null) continue;
            frozenAnimatorSpeeds[i] = frozenAnimators[i].speed;
            frozenAnimators[i].speed = 0f;
        }

        PulseEnemyColor(new Color(0.55f, 0.9f, 1f, 1f), seconds);
        electricFreezeRoutine = StartCoroutine(ElectricFreezeRoutine(seconds));
    }

    private IEnumerator ElectricFreezeRoutine(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            if (this == null || enemy == null || enemy.Status.Dead) break;
            t += Time.deltaTime;
            yield return null;
        }
        electricFreezeRoutine = null;
        EndElectricFreeze();
    }

    private void EndElectricFreeze()
    {
        if (!electricFrozen) return;
        electricFrozen = false;
        try
        {
            bool bossImmune = enemy != null && IsBossEnemy(enemy, GetEnemyMaxHealth());
            electricImmuneUntil = Time.time + (bossImmune ? 8f : 4f);
        }
        catch { electricImmuneUntil = Time.time + 4f; }

        if (electricFreezeRoutine != null)
        {
            StopCoroutine(electricFreezeRoutine);
            electricFreezeRoutine = null;
        }

        if (frozenAnimators != null)
        {
            for (int i = 0; i < frozenAnimators.Length; i++)
            {
                if (frozenAnimators[i] != null)
                {
                    frozenAnimators[i].speed = frozenAnimatorSpeeds[i];
                }
            }
        }
        frozenAnimators = null;
        frozenAnimatorSpeeds = null;

        try
        {
            if (enemy != null && !enemy.Status.Dead)
            {
                EnemyBehaviour beh = enemy.EnemyBehaviour != null ? enemy.EnemyBehaviour : enemy.GetComponentInChildren<EnemyBehaviour>();
                if (beh != null && frozeBehaviour) beh.StartBehaviour();
                DashParryDebugLog.LogVerbose($"[Electric] {enemy.name} descongelado");
            }
        }
        catch (Exception ex)
        {
            DashParryDebugLog.LogVerbose("[Electric] Error reanudando comportamiento: " + ex.Message);
        }
        frozeBehaviour = false;
    }

    // ---------------------------------------------------------------------
    // Maldición: reduce el daño del enemigo a jugadores + drenaje al dueño (host-only)
    // ---------------------------------------------------------------------
    internal float GetCurseDamageMultiplier()
    {
        if (curseRemaining <= 0f || curseBaseReduction <= 0f) return 1f;
        float reduction = Mathf.Min(0.35f, curseBaseReduction * (curseEnhanced ? curseThresholdMultiplier : 1f));
        if (IsBossEnemy(enemy, GetEnemyMaxHealth())) reduction *= 0.6f;
        return 1f - Mathf.Clamp(reduction, 0f, 0.35f);
    }

    internal void ApplyCurseHit(Enemy target, Penitent attacker, int playerIdx, int tier, int hitWeight = 1)
    {
        if (!CoopGameConfig.EnableElementalUpgrades) return;
        if (Blasphemous.CoopLocal.MultiPlayer.CoopNetworkManager.IsOnlineClient) return;
        if (target == null || target.Status.Dead || Execution_OnUse_Patch.IsEnemyBeingExecuted(target)) return;

        if (lastCurseAttacker == attacker && lastCurseHitFrame == Time.frameCount) return;
        lastCurseHitFrame = Time.frameCount;
        lastCurseAttacker = attacker;

        enemy = target;
        SwordElementalUpgrades.GetMaldicionParams(tier, out float reduction, out float drainPct, out float drainCooldown, out float duration, out int threshold, out float thresholdMult);

        bool wasCursed = curseRemaining > 0f;
        if (!wasCursed || tier >= curseTier)
        {
            curseTier = tier;
            curseBaseReduction = reduction;
        }
        curseHitThreshold = threshold;
        curseThresholdMultiplier = thresholdMult;
        curseRemaining = duration; // renovable por golpe
        curseMaxDuration = duration;

        if (!wasCursed)
        {
            DashParryDebugLog.LogVerbose($"[Curse] {target.name} maldito (tier {tier}, -{reduction * 100f:0}% daño, {duration:0.#}s) por P{playerIdx + 1}");
        }

        // Umbral: cada N golpes la reducción se multiplica x1.4 mientras dure esta maldición
        curseHitCount += Mathf.Max(1, hitWeight);
        if (curseHitCount >= curseHitThreshold)
        {
            curseHitCount = 0;
            if (!curseEnhanced)
            {
                curseEnhanced = true;
                float effective = Mathf.Min(0.35f, curseBaseReduction * curseThresholdMultiplier);
                DashParryDebugLog.Log($"[Curse] Umbral alcanzado en {target.name}: reducción {curseBaseReduction * 100f:0}% -> {effective * 100f:0}%");
                try
                {
                    Vector3 skullPos = target.transform.position + new Vector3(0f, Mathf.Clamp(GetEnemyTopY(), 1.0f, 3.8f) + 0.60f, 0f);
                    FloatingUpgradeSkull.Spawn(skullPos, ElementSkill.Curse, $"-{Mathf.RoundToInt(effective * 100f)}%");
                }
                catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemyElementalStatusController.cs:901"); }
            }
        }

        PulseEnemyColor(new Color(0.75f, 0.5f, 1f, 1f), 0.15f);

        // Cada enemigo solo puede alimentar un drenaje cada 4s (evita farmear cura con un solo objetivo)
        float healed = 0f;
        if (Time.time >= nextCurseDrainFromThisEnemy)
        {
            healed = TryCurseDrain(attacker, playerIdx, drainPct, drainCooldown);
            if (healed > 0f) nextCurseDrainFromThisEnemy = Time.time + 4f;
        }

        // Corazon "Pacto": con la maldicion potenciada, 35% de la curacion tambien a los demas jugadores vivos
        if (healed > 0f && curseEnhanced && SwordElementalUpgrades.IsHeartEquipped(playerIdx, SwordElementalUpgrades.HeartMaldicionId))
        {
            float share = healed * 0.35f;
            List<Penitent> players = CoopPlayerRegistry.GetActivePlayers();
            for (int i = 0; i < players.Count; i++)
            {
                Penitent p = players[i];
                if (p == null || p == attacker || p.Status == null || p.Status.Dead) continue;
                if (p.Stats == null || p.Stats.Life == null) continue;
                float pMax = p.Stats.Life.CurrentMax;
                float pCur = p.Stats.Life.Current;
                if (pMax <= 0f || pCur <= 0f || pCur >= pMax) continue;
                float pNew = Mathf.Min(pMax, pCur + share);
                p.Stats.Life.Current = pNew;
                try
                {
                    FloatingDamageNumber.Spawn(p.transform.position + new Vector3(0f, 2.2f, 0f), "+" + Mathf.RoundToInt(pNew - pCur), new Color(0.78f, 0.45f, 1f, 1f), isLarge: false);
                }
                catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemyElementalStatusController.cs:934"); }
            }
        }
        UpdateStatusBars();
    }

    private static float TryCurseDrain(Penitent owner, int playerIdx, float percentMaxLife, float cooldown)
    {
        if (owner == null || owner.Status == null || owner.Status.Dead) return 0f;
        if (owner.Stats == null || owner.Stats.Life == null) return 0f;

        float now = Time.time;
        if (NextCurseDrainTime.TryGetValue(playerIdx, out float next) && now < next) return 0f;

        float max = owner.Stats.Life.CurrentMax;
        float cur = owner.Stats.Life.Current;
        if (max <= 0f || cur <= 0f || cur >= max) return 0f;

        float amount = max * percentMaxLife;
        float newLife = Mathf.Min(max, cur + amount);
        owner.Stats.Life.Current = newLife;
        NextCurseDrainTime[playerIdx] = now + cooldown;

        DashParryDebugLog.LogVerbose($"[Curse] Drenaje: P{playerIdx + 1} +{newLife - cur:0.#} vida ({percentMaxLife * 100f:0}% de {max:0})");
        try
        {
            FloatingDamageNumber.Spawn(owner.transform.position + new Vector3(0f, 2.2f, 0f), "+" + Mathf.RoundToInt(newLife - cur), new Color(0.78f, 0.45f, 1f, 1f), isLarge: false);
        }
        catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemyElementalStatusController.cs:962"); }
        return newLife - cur;
    }

    private void Update()
    {
        if (!CoopGameConfig.EnableElementalUpgrades)
        {
            ResetAllStatuses();
            return;
        }

        if (enemy != null && enemy.Status.Dead && !poisonSpreadDone)
        {
            TrySpreadPoisonOnDeath();
        }

        if (enemy == null || enemy.Status.Dead || Execution_OnUse_Patch.IsEnemyBeingExecuted(enemy))
        {
            poisonBar?.Hide();
            bleedBar?.Hide();
            curseBar?.Hide();
            electricBar?.Hide();
            if (barsContainerObj != null && barsContainerObj.activeSelf)
            {
                barsContainerObj.SetActive(false);
            }
            RestoreOriginalColors();
            return;
        }

        float dt = Time.deltaTime;
        bool barsDirty = false;

        // Mantener barras derechas aunque el enemigo gire (scale.x negativo)
        if (barsContainerObj != null)
        {
            Vector3 lossy = transform.lossyScale;
            barsContainerObj.transform.localScale = new Vector3(lossy.x < 0f ? -1f : 1f, 1f, 1f);
        }

        if (tintRestoreTimer > 0f)
        {
            tintRestoreTimer -= dt;
            if (tintRestoreTimer <= 0f)
            {
                RestoreOriginalColors();
            }
        }

        // Actualizar Veneno (DoT)
        if (poisonRemainingTime > 0f)
        {
            poisonRemainingTime -= dt;
            poisonTickTimer -= dt;
            barsDirty = true;
            if (poisonTickTimer <= 0f)
            {
                poisonTickTimer += 1.0f;
                DealDirectStatusDamage(poisonDps, "", isPoison: true, isBurst: false);
                PulseEnemyColor(new Color(0.35f, 1f, 0.45f, 1f), 0.18f);
            }
            if (poisonRemainingTime <= 0f)
            {
                poisonRemainingTime = 0f;
                activePoisonTier = 0;
            }
        }

        // Actualizar Sangrado progresivo (DoT tras superar el umbral)
        if (bleedProgressiveRemainingTime > 0f)
        {
            bleedProgressiveRemainingTime -= dt;
            bleedTickTimer -= dt;
            barsDirty = true;
            if (bleedTickTimer <= 0f)
            {
                bleedTickTimer += 1.0f;
                DealDirectStatusDamage(bleedProgressiveDps, "", isPoison: false, isBurst: false);
                PulseEnemyColor(new Color(1f, 0.25f, 0.3f, 1f), 0.18f);
            }
            if (bleedProgressiveRemainingTime <= 0f)
            {
                bleedProgressiveRemainingTime = 0f;
                bleedAppDecayTimer = 10f;
            }
        }
        else if (bleedApplicationsCount > 0)
        {
            // Sin DoT y 10s sin golpes: decae una aplicacion acumulada
            bleedAppDecayTimer -= dt;
            if (bleedAppDecayTimer <= 0f)
            {
                bleedApplicationsCount = Mathf.Max(0, bleedApplicationsCount - 1);
                bleedBonusPercent = Mathf.Min(100, bleedApplicationsCount * 20);
                bleedAppDecayTimer = 10f;
            }
        }

        // Maldición: expira tras su duración (renovable por golpe)
        if (curseRemaining > 0f)
        {
            curseRemaining -= dt;
            if (curseRemaining <= 0f)
            {
                DashParryDebugLog.LogVerbose($"[Curse] Maldición expirada en {name} (umbral x1.4 era {curseEnhanced})");
                curseRemaining = 0f;
                curseTier = 0;
                curseBaseReduction = 0f;
                curseHitCount = 0;
                curseEnhanced = false;
            }
            barsDirty = true;
        }

        // Decaimiento gradual de cargas de sangrado si pasa mucho tiempo sin recibir golpes
        if (bleedStacks > 0)
        {
            bleedStackDecayTimer -= dt;
            if (bleedStackDecayTimer <= 0f)
            {
                bleedStacks = Mathf.Max(0, bleedStacks - 1);
                bleedStackDecayTimer = 3.0f;
                barsDirty = true;
            }
        }

        if (barsDirty)
        {
            UpdateStatusBars();
        }
    }

    private void TriggerEnemyDeathVisuals(Enemy enemy, bool lethalHandledByDamageable)
    {
        if (enemy == null) return;
        try
        {
            var dmgArea = enemy.GetComponentInChildren<EnemyDamageArea>();
            if (dmgArea?.DamageAreaCollider != null)
            {
                dmgArea.DamageAreaCollider.enabled = false;
            }
            enemy.GetComponentInChildren<MotionLerper>()?.StopLerping();

            if (!lethalHandledByDamageable)
            {
                var injector = enemy.GetComponentInChildren<Gameplay.GameControllers.Entities.Animations.EnemyAnimatorInyector>();
                if (injector != null)
                {
                    MethodInfo deathMethod = Reflect.Method(injector.GetType(), "Death", Type.EmptyTypes);
                    deathMethod?.Invoke(injector, null);
                }

                var beh = enemy.GetComponentInChildren<Gameplay.GameControllers.Enemies.Framework.IA.EnemyBehaviour>();
                if (beh != null)
                {
                    MethodInfo deathMethod = Reflect.Method(beh.GetType(), "Death", Type.EmptyTypes);
                    deathMethod?.Invoke(beh, null);
                }
            }
        }
        catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemyElementalStatusController.cs:1124"); }
    }

    private void DealDirectStatusDamage(float amount, string audioId, bool isPoison, bool isBurst, Color? numberColor = null, Penitent killerOverride = null)
    {
        if (enemy == null || enemy.Status.Dead || amount <= 0f) return;

        EnemyDamageArea_TakeDamage_ElementalStatus_Patch.IsApplyingStatusDamage = true;
        try
        {
            float lifeBefore = (enemy.Stats?.Life != null) ? enemy.Stats.Life.Current : 0f;
            float def = (enemy.Stats?.Defense != null) ? enemy.Stats.Defense.Final : 0f;
            float effectiveAmount = amount + def;

            bool willBeLethal = lifeBefore > 0f && amount >= lifeBefore;
            bool lethalHandledByDamageable = false;

            if (willBeLethal)
            {
                Penitent killer = killerOverride ?? (isPoison ? (lastPoisonAttacker ?? lastBleedAttacker) : (lastBleedAttacker ?? lastPoisonAttacker)) ?? Core.Logic?.Penitent;

                enemy.IsGuarding = false;
                enemy.Status.Unattacable = false;
                enemy.GetComponentInChildren<MotionLerper>()?.StopLerping();
                enemy.GetComponentInChildren<EnemyDamageArea>()?.ClearAccumDamage();

                Hit lethalHit = new Hit
                {
                    AttackingEntity = killer != null ? killer.gameObject : enemy.gameObject,
                    DamageAmount = effectiveAmount + 10f,
                    DamageType = DamageArea.DamageType.Normal,
                    DamageElement = DamageArea.DamageElement.Normal,
                    HitSoundId = string.Empty
                };

                try
                {
                    (enemy as IDamageable)?.Damage(lethalHit);
                    lethalHandledByDamageable = true;
                }
                catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemyElementalStatusController.cs:1164"); }
            }

            if (!willBeLethal || (!enemy.Status.Dead && enemy.Stats?.Life != null && enemy.Stats.Life.Current > 0f))
            {
                try
                {
                    enemy.Damage(effectiveAmount, string.Empty);
                }
                catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemyElementalStatusController.cs:1173"); }

                float lifeAfter = (enemy.Stats?.Life != null) ? enemy.Stats.Life.Current : 0f;

                if (lifeAfter >= lifeBefore && enemy.Stats?.Life != null && lifeBefore > 0f)
                {
                    float newLife = Mathf.Max(0f, lifeBefore - amount);
                    enemy.Stats.Life.Current = newLife;
                    if (newLife <= 0f)
                    {
                        enemy.Status.Dead = true;
                        try { enemy.KillInstanteneously(); } catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemyElementalStatusController.cs:1184"); }
                    }
                }
            }

            if (enemy.Status.Dead || (enemy.Stats?.Life != null && enemy.Stats.Life.Current <= 0f))
            {
                TriggerEnemyDeathVisuals(enemy, lethalHandledByDamageable);
            }

            // 3. Audio de impacto
            if (!string.IsNullOrEmpty(audioId))
            {
                try { Core.Audio?.PlayOneShot(audioId); } catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemyElementalStatusController.cs:1197"); }
            }

            // 4. Parpadeo de shader (protegido con try-catch para no fallar en enemigos sin material/renderers)
            try
            {
                enemy.GetComponentInChildren<MasterShaderEffects>()?.DamageEffectBlink(0f, 0.12f);
            }
            catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemyElementalStatusController.cs:1205"); }

            // 5. Destello de color sobre los sprites del enemigo
            PulseEnemyColor(isPoison ? new Color(0.35f, 1f, 0.45f, 1f) : (numberColor ?? new Color(1f, 0.25f, 0.3f, 1f)), 0.18f);

            // 6. Números flotantes de daño
            try
            {
                Vector3 spawnPos = enemy.transform.position + new Vector3(0f, Mathf.Clamp(GetEnemyTopY(), 1.0f, 3.8f) + 0.25f, 0f);
                if (isPoison)
                {
                    string txt = "-" + (Mathf.Approximately(amount % 1f, 0f) ? ((int)amount).ToString() : amount.ToString("0.#"));
                    FloatingDamageNumber.Spawn(spawnPos, txt, new Color(0.25f, 1f, 0.35f, 1f), isLarge: false);
                }
                else
                {
                    string txt = isBurst
                        ? $"-{Mathf.RoundToInt(amount)}"
                        : "-" + (Mathf.Approximately(amount % 1f, 0f) ? ((int)amount).ToString() : amount.ToString("0.#"));
                    FloatingDamageNumber.Spawn(spawnPos, txt, numberColor ?? new Color(1f, 0.2f, 0.2f, 1f), isLarge: isBurst);
                }
            }
            catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemyElementalStatusController.cs:1227"); }
        }
        finally
        {
            EnemyDamageArea_TakeDamage_ElementalStatus_Patch.IsApplyingStatusDamage = false;
        }
    }

    private void PulseEnemyColor(Color tint, float duration)
    {
        if (spriteRenderers == null || spriteRenderers.Length == 0)
        {
            CacheRenderers();
        }
        if (spriteRenderers == null) return;

        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] != null)
            {
                spriteRenderers[i].color = tint;
            }
        }
        tintRestoreTimer = Mathf.Max(tintRestoreTimer, duration);
    }

    private void RestoreOriginalColors()
    {
        if (spriteRenderers == null || originalColors == null) return;
        for (int i = 0; i < spriteRenderers.Length && i < originalColors.Length; i++)
        {
            if (spriteRenderers[i] != null)
            {
                spriteRenderers[i].color = originalColors[i];
            }
        }
    }

    private void UpdateStatusBars()
    {
        if (barsContainerObj == null) return;

        bool poisonActive = poisonRemainingTime > 0f;
        bool bleedActive = bleedProgressiveRemainingTime > 0f || bleedStacks > 0;
        bool curseActive = curseRemaining > 0f;
        bool electricActive = electricHitCount > 0 && !electricFrozen;

        if (!poisonActive && !bleedActive && !curseActive && !electricActive)
        {
            poisonBar?.Hide();
            bleedBar?.Hide();
            curseBar?.Hide();
            electricBar?.Hide();
            if (barsContainerObj.activeSelf) barsContainerObj.SetActive(false);
            return;
        }

        if (!barsContainerObj.activeSelf) barsContainerObj.SetActive(true);

        float topY = Mathf.Clamp(GetEnemyTopY(), 1.1f, 4.0f);
        int slot = 0;

        if (poisonActive)
        {
            float poisonFill = Mathf.Clamp01(poisonRemainingTime / Mathf.Max(0.1f, poisonMaxDuration));
            poisonBar.SetFill(poisonFill, topY + 0.16f * slot++);
        }
        else poisonBar.Hide();

        if (bleedActive)
        {
            if (bleedProgressiveRemainingTime > 0f)
            {
                float bleedFill = Mathf.Clamp01(bleedProgressiveRemainingTime / Mathf.Max(0.1f, bleedProgressiveMaxDuration));
                bleedBar.SetColor(new Color(1f, 0.22f, 0.25f, 1f));
                bleedBar.SetFill(bleedFill, topY + 0.16f * slot++);
            }
            else
            {
                float stackFill = Mathf.Clamp01((float)bleedStacks / Mathf.Max(1, bleedThreshold));
                bleedBar.SetColor(new Color(0.95f, 0.5f, 0.25f, 0.85f));
                bleedBar.SetFill(stackFill, topY + 0.16f * slot++);
            }
        }
        else bleedBar.Hide();

        if (curseActive)
        {
            curseBar.SetFill(curseRemaining / Mathf.Max(0.1f, curseMaxDuration), topY + 0.16f * slot++);
        }
        else curseBar.Hide();

        if (electricActive)
        {
            electricBar.SetFill((float)electricHitCount / Mathf.Max(1, electricThresholdCache), topY + 0.16f * slot++);
        }
        else electricBar.Hide();
    }
}
