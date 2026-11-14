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

/// <summary>
/// Sistema de mejora elemental de Mea Culpa para P1 y P2:
/// - Árbol de Habilidades: Senda del Veneno (VENENO_1..3) vs Senda del Sangrado (SANGRADO_1..3), mutuamente excluyentes por jugador.
/// - Corazones de Mea Culpa: Ponzoña Viva (HE_VENENO), Hemorragia Sagrada (HE_SANGRADO), Relámpago Consagrado (HE_ELECTRICO), Maldición Penitente (HE_MALDICION).
/// - Los efectos solo se aplican cuando el jugador tiene la mejora en su Árbol de Habilidades Y lleva equipado el Corazón correspondiente.
/// - Veneno: DPS plano + %vida máx./s (T1 1.5+0.25%/6s, T2 3+0.4%/8s, T3 4.5+0.6%/10s); corazón "Contagio" propaga al morir (T2+).
/// - Sangrado: estallido 3/4.5/6% vida máx. (tope 15% de vida base en no-jefes) + DoT 3/4.5/6 x5s; bonus +20%/aplicación (máx +100%); corazón "Sangria" extiende el DoT.
/// - Eléctrico: N golpes (6/5/4) -> congela 1.5/2/2.5s (jefe 0.4/0.6/0.8s) + descarga 10/20/32; T2/T3 con "Conductor" arcos a 1/2 enemigos cercanos; inmunidad 4s (jefe 8s).
/// - Maldición: -10/18/25% daño enemigo (x1.4 en umbral, máx 35%, x0.6 vs jefes), drenaje 0.5/0.75/1% vida máx. (3s CD global y 4s por enemigo), 6/7/8s; corazón "Pacto" comparte 35% de la cura.
/// </summary>
internal static class SwordElementalUpgrades
{
    internal const string SkillVeneno1 = "VENENO_1";
    internal const string SkillVeneno2 = "VENENO_2";
    internal const string SkillVeneno3 = "VENENO_3";

    internal const string SkillSangrado1 = "SANGRADO_1";
    internal const string SkillSangrado2 = "SANGRADO_2";
    internal const string SkillSangrado3 = "SANGRADO_3";

    internal const string SkillElectrico1 = "ELECTRICO_1";
    internal const string SkillElectrico2 = "ELECTRICO_2";
    internal const string SkillElectrico3 = "ELECTRICO_3";

    internal const string SkillMaldicion1 = "MALDICION_1";
    internal const string SkillMaldicion2 = "MALDICION_2";
    internal const string SkillMaldicion3 = "MALDICION_3";

    internal const string HeartVenenoId = "HE_VENENO";
    internal const string HeartSangradoId = "HE_SANGRADO";
    internal const string HeartElectricoId = "HE_ELECTRICO";
    internal const string HeartMaldicionId = "HE_MALDICION";

    internal static bool IsElementalHeart(string id)
    {
        return string.Equals(id, HeartVenenoId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(id, HeartSangradoId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(id, HeartElectricoId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(id, HeartMaldicionId, StringComparison.OrdinalIgnoreCase);
    }

    internal static readonly string[] VenenoSkills = { SkillVeneno1, SkillVeneno2, SkillVeneno3 };
    internal static readonly string[] SangradoSkills = { SkillSangrado1, SkillSangrado2, SkillSangrado3 };
    internal static readonly string[] ElectricoSkills = { SkillElectrico1, SkillElectrico2, SkillElectrico3 };
    internal static readonly string[] MaldicionSkills = { SkillMaldicion1, SkillMaldicion2, SkillMaldicion3 };

    private static readonly FieldInfo AllSkillsField = Reflect.Field(typeof(SkillManager), "allSkills");
    private static readonly FieldInfo AllSwordsField = Reflect.Field(typeof(InventoryManager), "allSwords");
    private static readonly FieldInfo OwnSwordsField = Reflect.Field(typeof(InventoryManager), "ownSwords");

    private static Sprite venenoSmallSprite;
    private static Sprite venenoSmallBuyedSprite;
    private static Sprite sangradoSmallSprite;
    private static Sprite sangradoSmallBuyedSprite;
    private static Sprite electricoSmallSprite;
    private static Sprite electricoSmallBuyedSprite;
    private static Sprite maldicionSmallSprite;
    private static Sprite maldicionSmallBuyedSprite;
    private static Sprite venenoHeartSprite;
    private static Sprite sangradoHeartSprite;
    private static Sprite electricoHeartSprite;
    private static Sprite maldicionHeartSprite;

    internal static bool IsElementalSkill(string skillId)
    {
        return IsVenenoSkill(skillId) || IsSangradoSkill(skillId) || IsElectricoSkill(skillId) || IsMaldicionSkill(skillId);
    }

    internal static bool IsVenenoSkill(string skillId)
    {
        return !string.IsNullOrEmpty(skillId) && skillId.StartsWith("VENENO_", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsSangradoSkill(string skillId)
    {
        return !string.IsNullOrEmpty(skillId) && skillId.StartsWith("SANGRADO_", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsElectricoSkill(string skillId)
    {
        return !string.IsNullOrEmpty(skillId) && skillId.StartsWith("ELECTRICO_", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsMaldicionSkill(string skillId)
    {
        return !string.IsNullOrEmpty(skillId) && skillId.StartsWith("MALDICION_", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool HasOppositePathUnlockedP1(string skillId)
    {
        return false;
    }

    internal static bool HasOppositePathUnlockedP2(string skillId)
    {
        return false;
    }

    internal static int GetUnlockedVenenoTier(bool isPlayer2) => GetUnlockedVenenoTier(isPlayer2 ? 1 : 0);
    internal static int GetUnlockedVenenoTier(int playerIndex)
    {
        if (playerIndex > 0)
        {
            if (Player2SkillManager.IsUnlocked(playerIndex, SkillVeneno3)) return 3;
            if (Player2SkillManager.IsUnlocked(playerIndex, SkillVeneno2)) return 2;
            if (Player2SkillManager.IsUnlocked(playerIndex, SkillVeneno1)) return 1;
            return 0;
        }
        if (Core.SkillManager == null) return 0;
        if (Core.SkillManager.IsSkillUnlocked(SkillVeneno3)) return 3;
        if (Core.SkillManager.IsSkillUnlocked(SkillVeneno2)) return 2;
        if (Core.SkillManager.IsSkillUnlocked(SkillVeneno1)) return 1;
        return 0;
    }

    internal static int GetUnlockedSangradoTier(bool isPlayer2) => GetUnlockedSangradoTier(isPlayer2 ? 1 : 0);
    internal static int GetUnlockedSangradoTier(int playerIndex)
    {
        if (playerIndex > 0)
        {
            if (Player2SkillManager.IsUnlocked(playerIndex, SkillSangrado3)) return 3;
            if (Player2SkillManager.IsUnlocked(playerIndex, SkillSangrado2)) return 2;
            if (Player2SkillManager.IsUnlocked(playerIndex, SkillSangrado1)) return 1;
            return 0;
        }
        if (Core.SkillManager == null) return 0;
        if (Core.SkillManager.IsSkillUnlocked(SkillSangrado3)) return 3;
        if (Core.SkillManager.IsSkillUnlocked(SkillSangrado2)) return 2;
        if (Core.SkillManager.IsSkillUnlocked(SkillSangrado1)) return 1;
        return 0;
    }

    internal static int GetUnlockedElectricoTier(bool isPlayer2) => GetUnlockedElectricoTier(isPlayer2 ? 1 : 0);
    internal static int GetUnlockedElectricoTier(int playerIndex)
    {
        if (playerIndex > 0)
        {
            if (Player2SkillManager.IsUnlocked(playerIndex, SkillElectrico3)) return 3;
            if (Player2SkillManager.IsUnlocked(playerIndex, SkillElectrico2)) return 2;
            if (Player2SkillManager.IsUnlocked(playerIndex, SkillElectrico1)) return 1;
            return 0;
        }
        if (Core.SkillManager == null) return 0;
        if (Core.SkillManager.IsSkillUnlocked(SkillElectrico3)) return 3;
        if (Core.SkillManager.IsSkillUnlocked(SkillElectrico2)) return 2;
        if (Core.SkillManager.IsSkillUnlocked(SkillElectrico1)) return 1;
        return 0;
    }

    internal static int GetUnlockedMaldicionTier(bool isPlayer2) => GetUnlockedMaldicionTier(isPlayer2 ? 1 : 0);
    internal static int GetUnlockedMaldicionTier(int playerIndex)
    {
        if (playerIndex > 0)
        {
            if (Player2SkillManager.IsUnlocked(playerIndex, SkillMaldicion3)) return 3;
            if (Player2SkillManager.IsUnlocked(playerIndex, SkillMaldicion2)) return 2;
            if (Player2SkillManager.IsUnlocked(playerIndex, SkillMaldicion1)) return 1;
            return 0;
        }
        if (Core.SkillManager == null) return 0;
        if (Core.SkillManager.IsSkillUnlocked(SkillMaldicion3)) return 3;
        if (Core.SkillManager.IsSkillUnlocked(SkillMaldicion2)) return 2;
        if (Core.SkillManager.IsSkillUnlocked(SkillMaldicion1)) return 1;
        return 0;
    }

    internal static bool IsHeartEquipped(bool isPlayer2, string heartId) => IsHeartEquipped(isPlayer2 ? 1 : 0, heartId);
    internal static bool IsHeartEquipped(int playerIndex, string heartId)
    {
        if (playerIndex > 0)
        {
            return string.Equals(Player2InventoryManager.GetEquippedSwordId(playerIndex), heartId, StringComparison.OrdinalIgnoreCase);
        }
        if (Core.InventoryManager == null) return false;
        Sword equipped = Core.InventoryManager.GetSwordInSlot(0);
        return equipped != null && string.Equals(equipped.id, heartId, StringComparison.OrdinalIgnoreCase);
    }

    internal static void GetPoisonParams(int tier, out float dps, out float maxHpPctPerSec, out float duration)
    {
        switch (tier)
        {
            case 1:
                dps = 1.5f;
                maxHpPctPerSec = 0.0025f;
                duration = 6.0f;
                break;
            case 2:
                dps = 3.0f;
                maxHpPctPerSec = 0.004f;
                duration = 8.0f;
                break;
            case 3:
            default:
                dps = 4.5f;
                maxHpPctPerSec = 0.006f;
                duration = 10.0f;
                break;
        }
    }

    internal static void GetBleedParams(int tier, out float maxHpPercent, out float progressiveDps, out float progressiveDuration)
    {
        progressiveDuration = 5.0f;
        switch (tier)
        {
            case 1:
                maxHpPercent = 0.03f; // 3% vida maxima
                progressiveDps = 3.0f;
                break;
            case 2:
                maxHpPercent = 0.045f; // 4.5% vida maxima
                progressiveDps = 4.5f;
                break;
            case 3:
            default:
                maxHpPercent = 0.06f; // 6% vida maxima
                progressiveDps = 6.0f;
                break;
        }
    }

    /// <summary>Electrico: golpes acumulados por enemigo antes de congelarlo, duracion del congelamiento (normal / jefe), dano de descarga al congelar y numero de arcos (tier>=2 + corazon Conductor).</summary>
    /// <summary>Daño eléctrico extra en CADA golpe con el Corazón del Relámpago: plano + % de vida máx. (con tope).</summary>
    internal static float GetElectricoHitDamage(int tier, float enemyMaxHp, bool isBoss)
    {
        float flat = tier >= 3 ? 9f : (tier == 2 ? 6f : 4f);
        float pct = tier >= 3 ? 0.009f : (tier == 2 ? 0.006f : 0.004f);
        float dmg = flat + Mathf.Min(enemyMaxHp * pct, flat * 2.5f);
        return isBoss ? dmg * 0.6f : dmg;
    }

    internal static void GetElectricoFreezeParams(int tier, out int hitThreshold, out float freezeSeconds, out float bossFreezeSeconds, out float shockDamage, out int chainTargets)
    {
        switch (tier)
        {
            case 1:
                hitThreshold = 6; freezeSeconds = 1.5f; bossFreezeSeconds = 0.4f; shockDamage = 10f; chainTargets = 0;
                break;
            case 2:
                hitThreshold = 5; freezeSeconds = 2.0f; bossFreezeSeconds = 0.6f; shockDamage = 20f; chainTargets = 1;
                break;
            case 3:
            default:
                hitThreshold = 4; freezeSeconds = 2.5f; bossFreezeSeconds = 0.8f; shockDamage = 32f; chainTargets = 2;
                break;
        }
    }

    /// <summary>Maldicion: reduccion base del dano que el enemigo maldito inflige a jugadores, drenaje (% vida max. del dueno por golpe) y cooldown del drenaje.</summary>
    internal static void GetMaldicionParams(int tier, out float damageReduction, out float drainPercentMaxLife, out float drainCooldown, out float curseDuration, out int hitThreshold, out float thresholdMultiplier)
    {
        drainCooldown = 3.0f;
        hitThreshold = 5;
        thresholdMultiplier = 1.4f;
        switch (tier)
        {
            case 1:
                damageReduction = 0.10f;
                drainPercentMaxLife = 0.005f;
                curseDuration = 6.0f;
                break;
            case 2:
                damageReduction = 0.18f;
                drainPercentMaxLife = 0.0075f;
                curseDuration = 7.0f;
                break;
            case 3:
            default:
                damageReduction = 0.25f;
                drainPercentMaxLife = 0.010f;
                curseDuration = 8.0f;
                break;
        }
    }

// Procedurally generated tree/heart icons: Poison Bubbles & Sacred Blood Symbol
    internal static Sprite CreatePoisonBubblesIcon(bool unlocked)
    {
        int w = 32, h = 32;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        Color[] pixels = new Color[w * h];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;

        void SetPx(int x, int y, Color c)
        {
            if (x >= 0 && x < w && y >= 0 && y < h) pixels[y * w + x] = c;
        }

        // Paleta de Burbujas de Veneno
        Color cB = unlocked ? new Color(0.18f, 0.45f, 0.22f, 0.95f) : new Color(0.20f, 0.25f, 0.20f, 0.85f);
        Color cDot = new Color(0.04f, 0.07f, 0.05f, 0.90f);
        Color cb = unlocked ? new Color(0.12f, 0.35f, 0.16f, 1f) : new Color(0.15f, 0.22f, 0.15f, 1f);
        Color cG = unlocked ? new Color(0.28f, 0.85f, 0.38f, 0.95f) : new Color(0.22f, 0.42f, 0.25f, 0.90f);
        Color cL = unlocked ? new Color(0.85f, 1f, 0.88f, 1f) : new Color(0.45f, 0.65f, 0.48f, 1f);
        Color cF = unlocked ? new Color(0.18f, 0.60f, 0.25f, 0.95f) : new Color(0.16f, 0.30f, 0.18f, 0.90f);

        // 'B' -> cB (140 px)
        SetPx(13, 2, cB); SetPx(14, 2, cB); SetPx(15, 2, cB); SetPx(16, 2, cB);
        SetPx(17, 2, cB); SetPx(18, 2, cB); SetPx(10, 3, cB); SetPx(11, 3, cB);
        SetPx(12, 3, cB); SetPx(13, 3, cB); SetPx(14, 3, cB); SetPx(15, 3, cB);
        SetPx(16, 3, cB); SetPx(17, 3, cB); SetPx(18, 3, cB); SetPx(19, 3, cB);
        SetPx(20, 3, cB); SetPx(21, 3, cB); SetPx(8, 4, cB); SetPx(9, 4, cB);
        SetPx(10, 4, cB); SetPx(11, 4, cB); SetPx(20, 4, cB); SetPx(21, 4, cB);
        SetPx(22, 4, cB); SetPx(23, 4, cB); SetPx(7, 5, cB); SetPx(8, 5, cB);
        SetPx(9, 5, cB); SetPx(22, 5, cB); SetPx(23, 5, cB); SetPx(24, 5, cB);
        SetPx(6, 6, cB); SetPx(7, 6, cB); SetPx(24, 6, cB); SetPx(25, 6, cB);
        SetPx(5, 7, cB); SetPx(6, 7, cB); SetPx(25, 7, cB); SetPx(26, 7, cB);
        SetPx(4, 8, cB); SetPx(5, 8, cB); SetPx(26, 8, cB); SetPx(27, 8, cB);
        SetPx(4, 9, cB); SetPx(5, 9, cB); SetPx(26, 9, cB); SetPx(27, 9, cB);
        SetPx(3, 10, cB); SetPx(4, 10, cB); SetPx(27, 10, cB); SetPx(28, 10, cB);
        SetPx(3, 11, cB); SetPx(4, 11, cB); SetPx(27, 11, cB); SetPx(28, 11, cB);
        SetPx(3, 12, cB); SetPx(28, 12, cB); SetPx(2, 13, cB); SetPx(3, 13, cB);
        SetPx(28, 13, cB); SetPx(29, 13, cB); SetPx(2, 14, cB); SetPx(3, 14, cB);
        SetPx(28, 14, cB); SetPx(29, 14, cB); SetPx(2, 15, cB); SetPx(3, 15, cB);
        SetPx(28, 15, cB); SetPx(29, 15, cB); SetPx(2, 16, cB); SetPx(3, 16, cB);
        SetPx(28, 16, cB); SetPx(29, 16, cB); SetPx(2, 17, cB); SetPx(3, 17, cB);
        SetPx(28, 17, cB); SetPx(29, 17, cB); SetPx(2, 18, cB); SetPx(3, 18, cB);
        SetPx(28, 18, cB); SetPx(29, 18, cB); SetPx(3, 19, cB); SetPx(28, 19, cB);
        SetPx(3, 20, cB); SetPx(4, 20, cB); SetPx(27, 20, cB); SetPx(28, 20, cB);
        SetPx(3, 21, cB); SetPx(4, 21, cB); SetPx(27, 21, cB); SetPx(28, 21, cB);
        SetPx(4, 22, cB); SetPx(5, 22, cB); SetPx(26, 22, cB); SetPx(27, 22, cB);
        SetPx(4, 23, cB); SetPx(5, 23, cB); SetPx(26, 23, cB); SetPx(27, 23, cB);
        SetPx(5, 24, cB); SetPx(6, 24, cB); SetPx(25, 24, cB); SetPx(26, 24, cB);
        SetPx(6, 25, cB); SetPx(7, 25, cB); SetPx(24, 25, cB); SetPx(25, 25, cB);
        SetPx(7, 26, cB); SetPx(8, 26, cB); SetPx(9, 26, cB); SetPx(22, 26, cB);
        SetPx(23, 26, cB); SetPx(24, 26, cB); SetPx(8, 27, cB); SetPx(9, 27, cB);
        SetPx(10, 27, cB); SetPx(11, 27, cB); SetPx(20, 27, cB); SetPx(21, 27, cB);
        SetPx(22, 27, cB); SetPx(23, 27, cB); SetPx(10, 28, cB); SetPx(11, 28, cB);
        SetPx(12, 28, cB); SetPx(13, 28, cB); SetPx(14, 28, cB); SetPx(15, 28, cB);
        SetPx(16, 28, cB); SetPx(17, 28, cB); SetPx(18, 28, cB); SetPx(19, 28, cB);
        SetPx(20, 28, cB); SetPx(21, 28, cB); SetPx(13, 29, cB); SetPx(14, 29, cB);
        SetPx(15, 29, cB); SetPx(16, 29, cB); SetPx(17, 29, cB); SetPx(18, 29, cB);
        // '.' -> cDot (227 px)
        SetPx(7, 7, cDot); SetPx(24, 7, cDot); SetPx(6, 8, cDot); SetPx(7, 8, cDot);
        SetPx(8, 8, cDot); SetPx(11, 8, cDot); SetPx(12, 8, cDot); SetPx(13, 8, cDot);
        SetPx(14, 8, cDot); SetPx(15, 8, cDot); SetPx(16, 8, cDot); SetPx(17, 8, cDot);
        SetPx(18, 8, cDot); SetPx(19, 8, cDot); SetPx(20, 8, cDot); SetPx(21, 8, cDot);
        SetPx(22, 8, cDot); SetPx(23, 8, cDot); SetPx(24, 8, cDot); SetPx(25, 8, cDot);
        SetPx(6, 9, cDot); SetPx(7, 9, cDot); SetPx(12, 9, cDot); SetPx(13, 9, cDot);
        SetPx(14, 9, cDot); SetPx(15, 9, cDot); SetPx(16, 9, cDot); SetPx(17, 9, cDot);
        SetPx(18, 9, cDot); SetPx(19, 9, cDot); SetPx(20, 9, cDot); SetPx(21, 9, cDot);
        SetPx(22, 9, cDot); SetPx(23, 9, cDot); SetPx(24, 9, cDot); SetPx(25, 9, cDot);
        SetPx(5, 10, cDot); SetPx(6, 10, cDot); SetPx(13, 10, cDot); SetPx(18, 10, cDot);
        SetPx(19, 10, cDot); SetPx(20, 10, cDot); SetPx(21, 10, cDot); SetPx(22, 10, cDot);
        SetPx(23, 10, cDot); SetPx(24, 10, cDot); SetPx(25, 10, cDot); SetPx(26, 10, cDot);
        SetPx(5, 11, cDot); SetPx(6, 11, cDot); SetPx(20, 11, cDot); SetPx(21, 11, cDot);
        SetPx(22, 11, cDot); SetPx(24, 11, cDot); SetPx(25, 11, cDot); SetPx(26, 11, cDot);
        SetPx(4, 12, cDot); SetPx(5, 12, cDot); SetPx(6, 12, cDot); SetPx(7, 12, cDot);
        SetPx(21, 12, cDot); SetPx(22, 12, cDot); SetPx(23, 12, cDot); SetPx(25, 12, cDot);
        SetPx(26, 12, cDot); SetPx(27, 12, cDot); SetPx(4, 13, cDot); SetPx(5, 13, cDot);
        SetPx(6, 13, cDot); SetPx(7, 13, cDot); SetPx(8, 13, cDot); SetPx(21, 13, cDot);
        SetPx(22, 13, cDot); SetPx(23, 13, cDot); SetPx(24, 13, cDot); SetPx(25, 13, cDot);
        SetPx(26, 13, cDot); SetPx(27, 13, cDot); SetPx(4, 14, cDot); SetPx(5, 14, cDot);
        SetPx(6, 14, cDot); SetPx(7, 14, cDot); SetPx(8, 14, cDot); SetPx(9, 14, cDot);
        SetPx(10, 14, cDot); SetPx(21, 14, cDot); SetPx(22, 14, cDot); SetPx(23, 14, cDot);
        SetPx(24, 14, cDot); SetPx(25, 14, cDot); SetPx(26, 14, cDot); SetPx(27, 14, cDot);
        SetPx(4, 15, cDot); SetPx(5, 15, cDot); SetPx(6, 15, cDot); SetPx(7, 15, cDot);
        SetPx(8, 15, cDot); SetPx(9, 15, cDot); SetPx(24, 15, cDot); SetPx(25, 15, cDot);
        SetPx(26, 15, cDot); SetPx(27, 15, cDot); SetPx(4, 16, cDot); SetPx(5, 16, cDot);
        SetPx(6, 16, cDot); SetPx(7, 16, cDot); SetPx(8, 16, cDot); SetPx(9, 16, cDot);
        SetPx(25, 16, cDot); SetPx(26, 16, cDot); SetPx(27, 16, cDot); SetPx(4, 17, cDot);
        SetPx(5, 17, cDot); SetPx(6, 17, cDot); SetPx(7, 17, cDot); SetPx(25, 17, cDot);
        SetPx(26, 17, cDot); SetPx(27, 17, cDot); SetPx(4, 18, cDot); SetPx(5, 18, cDot);
        SetPx(6, 18, cDot); SetPx(25, 18, cDot); SetPx(26, 18, cDot); SetPx(27, 18, cDot);
        SetPx(4, 19, cDot); SetPx(5, 19, cDot); SetPx(6, 19, cDot); SetPx(25, 19, cDot);
        SetPx(26, 19, cDot); SetPx(27, 19, cDot); SetPx(5, 20, cDot); SetPx(6, 20, cDot);
        SetPx(25, 20, cDot); SetPx(26, 20, cDot); SetPx(5, 21, cDot); SetPx(6, 21, cDot);
        SetPx(14, 21, cDot); SetPx(15, 21, cDot); SetPx(16, 21, cDot); SetPx(17, 21, cDot);
        SetPx(18, 21, cDot); SetPx(24, 21, cDot); SetPx(25, 21, cDot); SetPx(26, 21, cDot);
        SetPx(6, 22, cDot); SetPx(7, 22, cDot); SetPx(13, 22, cDot); SetPx(14, 22, cDot);
        SetPx(15, 22, cDot); SetPx(16, 22, cDot); SetPx(17, 22, cDot); SetPx(18, 22, cDot);
        SetPx(19, 22, cDot); SetPx(20, 22, cDot); SetPx(21, 22, cDot); SetPx(22, 22, cDot);
        SetPx(23, 22, cDot); SetPx(24, 22, cDot); SetPx(25, 22, cDot); SetPx(6, 23, cDot);
        SetPx(7, 23, cDot); SetPx(8, 23, cDot); SetPx(9, 23, cDot); SetPx(11, 23, cDot);
        SetPx(12, 23, cDot); SetPx(13, 23, cDot); SetPx(14, 23, cDot); SetPx(15, 23, cDot);
        SetPx(17, 23, cDot); SetPx(18, 23, cDot); SetPx(19, 23, cDot); SetPx(20, 23, cDot);
        SetPx(21, 23, cDot); SetPx(22, 23, cDot); SetPx(23, 23, cDot); SetPx(24, 23, cDot);
        SetPx(25, 23, cDot); SetPx(7, 24, cDot); SetPx(8, 24, cDot); SetPx(9, 24, cDot);
        SetPx(10, 24, cDot); SetPx(11, 24, cDot); SetPx(12, 24, cDot); SetPx(13, 24, cDot);
        SetPx(14, 24, cDot); SetPx(15, 24, cDot); SetPx(16, 24, cDot); SetPx(18, 24, cDot);
        SetPx(19, 24, cDot); SetPx(20, 24, cDot); SetPx(21, 24, cDot); SetPx(22, 24, cDot);
        SetPx(23, 24, cDot); SetPx(24, 24, cDot); SetPx(8, 25, cDot); SetPx(9, 25, cDot);
        SetPx(10, 25, cDot); SetPx(11, 25, cDot); SetPx(12, 25, cDot); SetPx(13, 25, cDot);
        SetPx(14, 25, cDot); SetPx(15, 25, cDot); SetPx(16, 25, cDot); SetPx(17, 25, cDot);
        SetPx(18, 25, cDot); SetPx(20, 25, cDot); SetPx(21, 25, cDot); SetPx(22, 25, cDot);
        SetPx(23, 25, cDot); SetPx(10, 26, cDot); SetPx(11, 26, cDot); SetPx(12, 26, cDot);
        SetPx(14, 26, cDot); SetPx(15, 26, cDot); SetPx(16, 26, cDot); SetPx(17, 26, cDot);
        SetPx(18, 26, cDot); SetPx(19, 26, cDot); SetPx(20, 26, cDot); SetPx(21, 26, cDot);
        SetPx(12, 27, cDot); SetPx(13, 27, cDot); SetPx(15, 27, cDot); SetPx(16, 27, cDot);
        SetPx(17, 27, cDot); SetPx(18, 27, cDot); SetPx(19, 27, cDot);
        // 'F' -> cF (41 px)
        SetPx(12, 4, cF); SetPx(13, 4, cF); SetPx(14, 4, cF); SetPx(15, 4, cF);
        SetPx(16, 4, cF); SetPx(17, 4, cF); SetPx(18, 4, cF); SetPx(19, 4, cF);
        SetPx(10, 5, cF); SetPx(11, 5, cF); SetPx(12, 5, cF); SetPx(13, 5, cF);
        SetPx(14, 5, cF); SetPx(15, 5, cF); SetPx(16, 5, cF); SetPx(17, 5, cF);
        SetPx(18, 5, cF); SetPx(19, 5, cF); SetPx(20, 5, cF); SetPx(21, 5, cF);
        SetPx(8, 6, cF); SetPx(9, 6, cF); SetPx(10, 6, cF); SetPx(11, 6, cF);
        SetPx(12, 6, cF); SetPx(13, 6, cF); SetPx(14, 6, cF); SetPx(15, 6, cF);
        SetPx(16, 6, cF); SetPx(17, 6, cF); SetPx(18, 6, cF); SetPx(19, 6, cF);
        SetPx(20, 6, cF); SetPx(21, 6, cF); SetPx(22, 6, cF); SetPx(23, 6, cF);
        SetPx(9, 7, cF); SetPx(12, 7, cF); SetPx(16, 7, cF); SetPx(20, 7, cF);
        SetPx(22, 7, cF);
        // 'b' -> cb (85 px)
        SetPx(8, 7, cb); SetPx(10, 7, cb); SetPx(11, 7, cb); SetPx(13, 7, cb);
        SetPx(14, 7, cb); SetPx(15, 7, cb); SetPx(17, 7, cb); SetPx(18, 7, cb);
        SetPx(19, 7, cb); SetPx(21, 7, cb); SetPx(23, 7, cb); SetPx(9, 8, cb);
        SetPx(10, 8, cb); SetPx(8, 9, cb); SetPx(11, 9, cb); SetPx(7, 10, cb);
        SetPx(12, 10, cb); SetPx(14, 10, cb); SetPx(15, 10, cb); SetPx(16, 10, cb);
        SetPx(17, 10, cb); SetPx(7, 11, cb); SetPx(12, 11, cb); SetPx(13, 11, cb);
        SetPx(18, 11, cb); SetPx(19, 11, cb); SetPx(8, 12, cb); SetPx(11, 12, cb);
        SetPx(12, 12, cb); SetPx(19, 12, cb); SetPx(20, 12, cb); SetPx(9, 13, cb);
        SetPx(10, 13, cb); SetPx(11, 13, cb); SetPx(20, 13, cb); SetPx(11, 14, cb);
        SetPx(20, 14, cb); SetPx(10, 15, cb); SetPx(19, 15, cb); SetPx(20, 15, cb);
        SetPx(21, 15, cb); SetPx(22, 15, cb); SetPx(23, 15, cb); SetPx(10, 16, cb);
        SetPx(11, 16, cb); SetPx(18, 16, cb); SetPx(19, 16, cb); SetPx(23, 16, cb);
        SetPx(24, 16, cb); SetPx(8, 17, cb); SetPx(9, 17, cb); SetPx(11, 17, cb);
        SetPx(12, 17, cb); SetPx(18, 17, cb); SetPx(24, 17, cb); SetPx(7, 18, cb);
        SetPx(13, 18, cb); SetPx(18, 18, cb); SetPx(24, 18, cb); SetPx(7, 19, cb);
        SetPx(13, 19, cb); SetPx(18, 19, cb); SetPx(24, 19, cb); SetPx(7, 20, cb);
        SetPx(13, 20, cb); SetPx(14, 20, cb); SetPx(15, 20, cb); SetPx(16, 20, cb);
        SetPx(17, 20, cb); SetPx(18, 20, cb); SetPx(19, 20, cb); SetPx(23, 20, cb);
        SetPx(24, 20, cb); SetPx(7, 21, cb); SetPx(13, 21, cb); SetPx(19, 21, cb);
        SetPx(20, 21, cb); SetPx(21, 21, cb); SetPx(22, 21, cb); SetPx(23, 21, cb);
        SetPx(8, 22, cb); SetPx(9, 22, cb); SetPx(11, 22, cb); SetPx(12, 22, cb);
        SetPx(10, 23, cb);
        // 'G' -> cG (102 px)
        SetPx(9, 9, cG); SetPx(10, 9, cG); SetPx(8, 10, cG); SetPx(9, 10, cG);
        SetPx(10, 10, cG); SetPx(11, 10, cG); SetPx(9, 11, cG); SetPx(10, 11, cG);
        SetPx(11, 11, cG); SetPx(14, 11, cG); SetPx(15, 11, cG); SetPx(16, 11, cG);
        SetPx(17, 11, cG); SetPx(9, 12, cG); SetPx(10, 12, cG); SetPx(13, 12, cG);
        SetPx(14, 12, cG); SetPx(15, 12, cG); SetPx(16, 12, cG); SetPx(17, 12, cG);
        SetPx(18, 12, cG); SetPx(12, 13, cG); SetPx(13, 13, cG); SetPx(14, 13, cG);
        SetPx(15, 13, cG); SetPx(16, 13, cG); SetPx(17, 13, cG); SetPx(18, 13, cG);
        SetPx(19, 13, cG); SetPx(12, 14, cG); SetPx(13, 14, cG); SetPx(14, 14, cG);
        SetPx(15, 14, cG); SetPx(16, 14, cG); SetPx(17, 14, cG); SetPx(18, 14, cG);
        SetPx(19, 14, cG); SetPx(11, 15, cG); SetPx(12, 15, cG); SetPx(13, 15, cG);
        SetPx(14, 15, cG); SetPx(15, 15, cG); SetPx(16, 15, cG); SetPx(17, 15, cG);
        SetPx(18, 15, cG); SetPx(12, 16, cG); SetPx(14, 16, cG); SetPx(15, 16, cG);
        SetPx(16, 16, cG); SetPx(17, 16, cG); SetPx(20, 16, cG); SetPx(21, 16, cG);
        SetPx(22, 16, cG); SetPx(10, 17, cG); SetPx(15, 17, cG); SetPx(16, 17, cG);
        SetPx(17, 17, cG); SetPx(19, 17, cG); SetPx(20, 17, cG); SetPx(21, 17, cG);
        SetPx(22, 17, cG); SetPx(23, 17, cG); SetPx(8, 18, cG); SetPx(9, 18, cG);
        SetPx(10, 18, cG); SetPx(11, 18, cG); SetPx(12, 18, cG); SetPx(14, 18, cG);
        SetPx(15, 18, cG); SetPx(16, 18, cG); SetPx(17, 18, cG); SetPx(19, 18, cG);
        SetPx(20, 18, cG); SetPx(21, 18, cG); SetPx(22, 18, cG); SetPx(23, 18, cG);
        SetPx(8, 19, cG); SetPx(9, 19, cG); SetPx(10, 19, cG); SetPx(11, 19, cG);
        SetPx(12, 19, cG); SetPx(14, 19, cG); SetPx(15, 19, cG); SetPx(16, 19, cG);
        SetPx(17, 19, cG); SetPx(20, 19, cG); SetPx(21, 19, cG); SetPx(22, 19, cG);
        SetPx(23, 19, cG); SetPx(9, 20, cG); SetPx(10, 20, cG); SetPx(11, 20, cG);
        SetPx(12, 20, cG); SetPx(20, 20, cG); SetPx(21, 20, cG); SetPx(22, 20, cG);
        SetPx(8, 21, cG); SetPx(9, 21, cG); SetPx(10, 21, cG); SetPx(11, 21, cG);
        SetPx(12, 21, cG); SetPx(10, 22, cG);
        // 'L' -> cL (13 px)
        SetPx(8, 11, cL); SetPx(23, 11, cL); SetPx(24, 12, cL); SetPx(13, 16, cL);
        SetPx(13, 17, cL); SetPx(14, 17, cL); SetPx(19, 19, cL); SetPx(8, 20, cL);
        SetPx(16, 23, cL); SetPx(17, 24, cL); SetPx(19, 25, cL); SetPx(13, 26, cL);
        SetPx(14, 27, cL);

        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 32f);
    }

    internal static Sprite CreateBloodSymbolIcon(bool unlocked)
    {
        int w = 32, h = 32;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        Color[] pixels = new Color[w * h];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;

        void SetPx(int x, int y, Color c)
        {
            if (x >= 0 && x < w && y >= 0 && y < h) pixels[y * w + x] = c;
        }

        // Paleta de Símbolo de Sangre (Gota Sagrada)
        Color cB = unlocked ? new Color(0.55f, 0.15f, 0.15f, 0.95f) : new Color(0.28f, 0.18f, 0.18f, 0.85f);
        Color cDot = new Color(0.07f, 0.03f, 0.04f, 0.90f);
        Color ck = unlocked ? new Color(0.38f, 0.06f, 0.08f, 1f) : new Color(0.22f, 0.10f, 0.10f, 1f);
        Color cR = unlocked ? new Color(0.92f, 0.18f, 0.20f, 0.98f) : new Color(0.45f, 0.16f, 0.18f, 0.90f);
        Color cH = unlocked ? new Color(1f, 0.65f, 0.68f, 1f) : new Color(0.60f, 0.35f, 0.36f, 1f);
        // 'B' -> cB (136 px)
        SetPx(13, 2, cB); SetPx(14, 2, cB); SetPx(15, 2, cB); SetPx(16, 2, cB);
        SetPx(17, 2, cB); SetPx(18, 2, cB); SetPx(10, 3, cB); SetPx(11, 3, cB);
        SetPx(12, 3, cB); SetPx(13, 3, cB); SetPx(14, 3, cB); SetPx(17, 3, cB);
        SetPx(18, 3, cB); SetPx(19, 3, cB); SetPx(20, 3, cB); SetPx(21, 3, cB);
        SetPx(8, 4, cB); SetPx(9, 4, cB); SetPx(10, 4, cB); SetPx(21, 4, cB);
        SetPx(22, 4, cB); SetPx(23, 4, cB); SetPx(7, 5, cB); SetPx(8, 5, cB);
        SetPx(9, 5, cB); SetPx(22, 5, cB); SetPx(23, 5, cB); SetPx(24, 5, cB);
        SetPx(6, 6, cB); SetPx(7, 6, cB); SetPx(24, 6, cB); SetPx(25, 6, cB);
        SetPx(5, 7, cB); SetPx(6, 7, cB); SetPx(25, 7, cB); SetPx(26, 7, cB);
        SetPx(4, 8, cB); SetPx(5, 8, cB); SetPx(26, 8, cB); SetPx(27, 8, cB);
        SetPx(4, 9, cB); SetPx(5, 9, cB); SetPx(26, 9, cB); SetPx(27, 9, cB);
        SetPx(3, 10, cB); SetPx(4, 10, cB); SetPx(27, 10, cB); SetPx(28, 10, cB);
        SetPx(3, 11, cB); SetPx(4, 11, cB); SetPx(27, 11, cB); SetPx(28, 11, cB);
        SetPx(3, 12, cB); SetPx(28, 12, cB); SetPx(2, 13, cB); SetPx(3, 13, cB);
        SetPx(28, 13, cB); SetPx(29, 13, cB); SetPx(2, 14, cB); SetPx(3, 14, cB);
        SetPx(28, 14, cB); SetPx(29, 14, cB); SetPx(2, 15, cB); SetPx(3, 15, cB);
        SetPx(28, 15, cB); SetPx(29, 15, cB); SetPx(2, 16, cB); SetPx(3, 16, cB);
        SetPx(28, 16, cB); SetPx(29, 16, cB); SetPx(2, 17, cB); SetPx(3, 17, cB);
        SetPx(28, 17, cB); SetPx(29, 17, cB); SetPx(2, 18, cB); SetPx(3, 18, cB);
        SetPx(28, 18, cB); SetPx(29, 18, cB); SetPx(3, 19, cB); SetPx(28, 19, cB);
        SetPx(3, 20, cB); SetPx(4, 20, cB); SetPx(27, 20, cB); SetPx(28, 20, cB);
        SetPx(3, 21, cB); SetPx(4, 21, cB); SetPx(27, 21, cB); SetPx(28, 21, cB);
        SetPx(4, 22, cB); SetPx(5, 22, cB); SetPx(26, 22, cB); SetPx(27, 22, cB);
        SetPx(4, 23, cB); SetPx(5, 23, cB); SetPx(26, 23, cB); SetPx(27, 23, cB);
        SetPx(5, 24, cB); SetPx(6, 24, cB); SetPx(25, 24, cB); SetPx(26, 24, cB);
        SetPx(6, 25, cB); SetPx(7, 25, cB); SetPx(24, 25, cB); SetPx(25, 25, cB);
        SetPx(7, 26, cB); SetPx(8, 26, cB); SetPx(9, 26, cB); SetPx(22, 26, cB);
        SetPx(23, 26, cB); SetPx(24, 26, cB); SetPx(8, 27, cB); SetPx(9, 27, cB);
        SetPx(10, 27, cB); SetPx(11, 27, cB); SetPx(20, 27, cB); SetPx(21, 27, cB);
        SetPx(22, 27, cB); SetPx(23, 27, cB); SetPx(10, 28, cB); SetPx(11, 28, cB);
        SetPx(12, 28, cB); SetPx(13, 28, cB); SetPx(14, 28, cB); SetPx(15, 28, cB);
        SetPx(16, 28, cB); SetPx(17, 28, cB); SetPx(18, 28, cB); SetPx(19, 28, cB);
        SetPx(20, 28, cB); SetPx(21, 28, cB); SetPx(13, 29, cB); SetPx(14, 29, cB);
        SetPx(15, 29, cB); SetPx(16, 29, cB); SetPx(17, 29, cB); SetPx(18, 29, cB);
        // '.' -> cDot (300 px)
        SetPx(12, 4, cDot); SetPx(13, 4, cDot); SetPx(14, 4, cDot); SetPx(17, 4, cDot);
        SetPx(18, 4, cDot); SetPx(19, 4, cDot); SetPx(10, 5, cDot); SetPx(12, 5, cDot);
        SetPx(13, 5, cDot); SetPx(14, 5, cDot); SetPx(17, 5, cDot); SetPx(18, 5, cDot);
        SetPx(19, 5, cDot); SetPx(21, 5, cDot); SetPx(8, 6, cDot); SetPx(9, 6, cDot);
        SetPx(10, 6, cDot); SetPx(12, 6, cDot); SetPx(13, 6, cDot); SetPx(14, 6, cDot);
        SetPx(17, 6, cDot); SetPx(18, 6, cDot); SetPx(19, 6, cDot); SetPx(21, 6, cDot);
        SetPx(22, 6, cDot); SetPx(23, 6, cDot); SetPx(7, 7, cDot); SetPx(8, 7, cDot);
        SetPx(9, 7, cDot); SetPx(10, 7, cDot); SetPx(11, 7, cDot); SetPx(12, 7, cDot);
        SetPx(13, 7, cDot); SetPx(14, 7, cDot); SetPx(17, 7, cDot); SetPx(18, 7, cDot);
        SetPx(19, 7, cDot); SetPx(20, 7, cDot); SetPx(21, 7, cDot); SetPx(22, 7, cDot);
        SetPx(23, 7, cDot); SetPx(24, 7, cDot); SetPx(6, 8, cDot); SetPx(7, 8, cDot);
        SetPx(8, 8, cDot); SetPx(9, 8, cDot); SetPx(10, 8, cDot); SetPx(11, 8, cDot);
        SetPx(12, 8, cDot); SetPx(13, 8, cDot); SetPx(14, 8, cDot); SetPx(15, 8, cDot);
        SetPx(16, 8, cDot); SetPx(17, 8, cDot); SetPx(18, 8, cDot); SetPx(19, 8, cDot);
        SetPx(20, 8, cDot); SetPx(21, 8, cDot); SetPx(22, 8, cDot); SetPx(23, 8, cDot);
        SetPx(24, 8, cDot); SetPx(25, 8, cDot); SetPx(6, 9, cDot); SetPx(7, 9, cDot);
        SetPx(8, 9, cDot); SetPx(9, 9, cDot); SetPx(10, 9, cDot); SetPx(11, 9, cDot);
        SetPx(20, 9, cDot); SetPx(21, 9, cDot); SetPx(22, 9, cDot); SetPx(23, 9, cDot);
        SetPx(24, 9, cDot); SetPx(25, 9, cDot); SetPx(5, 10, cDot); SetPx(6, 10, cDot);
        SetPx(7, 10, cDot); SetPx(8, 10, cDot); SetPx(9, 10, cDot); SetPx(10, 10, cDot);
        SetPx(21, 10, cDot); SetPx(22, 10, cDot); SetPx(23, 10, cDot); SetPx(24, 10, cDot);
        SetPx(25, 10, cDot); SetPx(26, 10, cDot); SetPx(5, 11, cDot); SetPx(6, 11, cDot);
        SetPx(7, 11, cDot); SetPx(8, 11, cDot); SetPx(9, 11, cDot); SetPx(22, 11, cDot);
        SetPx(23, 11, cDot); SetPx(24, 11, cDot); SetPx(25, 11, cDot); SetPx(26, 11, cDot);
        SetPx(4, 12, cDot); SetPx(5, 12, cDot); SetPx(6, 12, cDot); SetPx(7, 12, cDot);
        SetPx(8, 12, cDot); SetPx(23, 12, cDot); SetPx(24, 12, cDot); SetPx(25, 12, cDot);
        SetPx(26, 12, cDot); SetPx(27, 12, cDot); SetPx(4, 13, cDot); SetPx(5, 13, cDot);
        SetPx(6, 13, cDot); SetPx(7, 13, cDot); SetPx(8, 13, cDot); SetPx(23, 13, cDot);
        SetPx(24, 13, cDot); SetPx(25, 13, cDot); SetPx(26, 13, cDot); SetPx(27, 13, cDot);
        SetPx(4, 14, cDot); SetPx(5, 14, cDot); SetPx(6, 14, cDot); SetPx(7, 14, cDot);
        SetPx(8, 14, cDot); SetPx(23, 14, cDot); SetPx(24, 14, cDot); SetPx(25, 14, cDot);
        SetPx(26, 14, cDot); SetPx(27, 14, cDot); SetPx(4, 15, cDot); SetPx(5, 15, cDot);
        SetPx(6, 15, cDot); SetPx(7, 15, cDot); SetPx(8, 15, cDot); SetPx(9, 15, cDot);
        SetPx(22, 15, cDot); SetPx(23, 15, cDot); SetPx(24, 15, cDot); SetPx(25, 15, cDot);
        SetPx(26, 15, cDot); SetPx(27, 15, cDot); SetPx(4, 16, cDot); SetPx(5, 16, cDot);
        SetPx(6, 16, cDot); SetPx(7, 16, cDot); SetPx(8, 16, cDot); SetPx(9, 16, cDot);
        SetPx(22, 16, cDot); SetPx(23, 16, cDot); SetPx(24, 16, cDot); SetPx(25, 16, cDot);
        SetPx(26, 16, cDot); SetPx(27, 16, cDot); SetPx(4, 17, cDot); SetPx(5, 17, cDot);
        SetPx(6, 17, cDot); SetPx(7, 17, cDot); SetPx(8, 17, cDot); SetPx(9, 17, cDot);
        SetPx(10, 17, cDot); SetPx(21, 17, cDot); SetPx(22, 17, cDot); SetPx(23, 17, cDot);
        SetPx(24, 17, cDot); SetPx(25, 17, cDot); SetPx(26, 17, cDot); SetPx(27, 17, cDot);
        SetPx(4, 18, cDot); SetPx(5, 18, cDot); SetPx(6, 18, cDot); SetPx(7, 18, cDot);
        SetPx(8, 18, cDot); SetPx(9, 18, cDot); SetPx(10, 18, cDot); SetPx(21, 18, cDot);
        SetPx(22, 18, cDot); SetPx(23, 18, cDot); SetPx(24, 18, cDot); SetPx(25, 18, cDot);
        SetPx(26, 18, cDot); SetPx(27, 18, cDot); SetPx(4, 19, cDot); SetPx(5, 19, cDot);
        SetPx(6, 19, cDot); SetPx(7, 19, cDot); SetPx(8, 19, cDot); SetPx(9, 19, cDot);
        SetPx(10, 19, cDot); SetPx(11, 19, cDot); SetPx(20, 19, cDot); SetPx(21, 19, cDot);
        SetPx(22, 19, cDot); SetPx(23, 19, cDot); SetPx(24, 19, cDot); SetPx(25, 19, cDot);
        SetPx(26, 19, cDot); SetPx(27, 19, cDot); SetPx(5, 20, cDot); SetPx(6, 20, cDot);
        SetPx(7, 20, cDot); SetPx(8, 20, cDot); SetPx(9, 20, cDot); SetPx(10, 20, cDot);
        SetPx(11, 20, cDot); SetPx(20, 20, cDot); SetPx(21, 20, cDot); SetPx(22, 20, cDot);
        SetPx(23, 20, cDot); SetPx(24, 20, cDot); SetPx(25, 20, cDot); SetPx(26, 20, cDot);
        SetPx(5, 21, cDot); SetPx(6, 21, cDot); SetPx(7, 21, cDot); SetPx(8, 21, cDot);
        SetPx(9, 21, cDot); SetPx(10, 21, cDot); SetPx(11, 21, cDot); SetPx(12, 21, cDot);
        SetPx(19, 21, cDot); SetPx(20, 21, cDot); SetPx(21, 21, cDot); SetPx(22, 21, cDot);
        SetPx(23, 21, cDot); SetPx(24, 21, cDot); SetPx(25, 21, cDot); SetPx(26, 21, cDot);
        SetPx(6, 22, cDot); SetPx(7, 22, cDot); SetPx(8, 22, cDot); SetPx(9, 22, cDot);
        SetPx(10, 22, cDot); SetPx(11, 22, cDot); SetPx(12, 22, cDot); SetPx(19, 22, cDot);
        SetPx(20, 22, cDot); SetPx(21, 22, cDot); SetPx(22, 22, cDot); SetPx(23, 22, cDot);
        SetPx(24, 22, cDot); SetPx(25, 22, cDot); SetPx(6, 23, cDot); SetPx(7, 23, cDot);
        SetPx(8, 23, cDot); SetPx(9, 23, cDot); SetPx(10, 23, cDot); SetPx(11, 23, cDot);
        SetPx(12, 23, cDot); SetPx(13, 23, cDot); SetPx(18, 23, cDot); SetPx(19, 23, cDot);
        SetPx(20, 23, cDot); SetPx(21, 23, cDot); SetPx(22, 23, cDot); SetPx(23, 23, cDot);
        SetPx(24, 23, cDot); SetPx(25, 23, cDot); SetPx(7, 24, cDot); SetPx(8, 24, cDot);
        SetPx(9, 24, cDot); SetPx(10, 24, cDot); SetPx(11, 24, cDot); SetPx(12, 24, cDot);
        SetPx(13, 24, cDot); SetPx(18, 24, cDot); SetPx(19, 24, cDot); SetPx(20, 24, cDot);
        SetPx(21, 24, cDot); SetPx(22, 24, cDot); SetPx(23, 24, cDot); SetPx(24, 24, cDot);
        SetPx(8, 25, cDot); SetPx(9, 25, cDot); SetPx(10, 25, cDot); SetPx(11, 25, cDot);
        SetPx(12, 25, cDot); SetPx(13, 25, cDot); SetPx(14, 25, cDot); SetPx(17, 25, cDot);
        SetPx(18, 25, cDot); SetPx(19, 25, cDot); SetPx(20, 25, cDot); SetPx(21, 25, cDot);
        SetPx(22, 25, cDot); SetPx(23, 25, cDot); SetPx(10, 26, cDot); SetPx(11, 26, cDot);
        SetPx(12, 26, cDot); SetPx(13, 26, cDot); SetPx(14, 26, cDot); SetPx(17, 26, cDot);
        SetPx(18, 26, cDot); SetPx(19, 26, cDot); SetPx(20, 26, cDot); SetPx(21, 26, cDot);
        SetPx(12, 27, cDot); SetPx(13, 27, cDot); SetPx(14, 27, cDot); SetPx(15, 27, cDot);
        SetPx(16, 27, cDot); SetPx(17, 27, cDot); SetPx(18, 27, cDot); SetPx(19, 27, cDot);
        // 'k' -> ck (40 px)
        SetPx(15, 3, ck); SetPx(16, 3, ck); SetPx(11, 4, ck); SetPx(20, 4, ck);
        SetPx(15, 7, ck); SetPx(16, 7, ck); SetPx(12, 9, ck); SetPx(19, 9, ck);
        SetPx(11, 10, ck); SetPx(20, 10, ck); SetPx(10, 11, ck); SetPx(21, 11, ck);
        SetPx(9, 12, ck); SetPx(22, 12, ck); SetPx(9, 13, ck); SetPx(22, 13, ck);
        SetPx(9, 14, ck); SetPx(22, 14, ck); SetPx(10, 16, ck); SetPx(21, 16, ck);
        SetPx(11, 17, ck); SetPx(20, 17, ck); SetPx(11, 18, ck); SetPx(20, 18, ck);
        SetPx(12, 19, ck); SetPx(19, 19, ck); SetPx(12, 20, ck); SetPx(19, 20, ck);
        SetPx(13, 21, ck); SetPx(18, 21, ck); SetPx(13, 22, ck); SetPx(18, 22, ck);
        SetPx(14, 23, ck); SetPx(17, 23, ck); SetPx(14, 24, ck); SetPx(17, 24, ck);
        SetPx(15, 25, ck); SetPx(16, 25, ck); SetPx(15, 26, ck); SetPx(16, 26, ck);
        // 'R' -> cR (117 px)
        SetPx(16, 4, cR); SetPx(15, 5, cR); SetPx(16, 5, cR); SetPx(20, 5, cR);
        SetPx(11, 6, cR); SetPx(15, 6, cR); SetPx(16, 6, cR); SetPx(20, 6, cR);
        SetPx(13, 9, cR); SetPx(14, 9, cR); SetPx(15, 9, cR); SetPx(16, 9, cR);
        SetPx(17, 9, cR); SetPx(18, 9, cR); SetPx(12, 10, cR); SetPx(13, 10, cR);
        SetPx(14, 10, cR); SetPx(15, 10, cR); SetPx(16, 10, cR); SetPx(17, 10, cR);
        SetPx(18, 10, cR); SetPx(19, 10, cR); SetPx(11, 11, cR); SetPx(12, 11, cR);
        SetPx(13, 11, cR); SetPx(14, 11, cR); SetPx(15, 11, cR); SetPx(16, 11, cR);
        SetPx(17, 11, cR); SetPx(18, 11, cR); SetPx(19, 11, cR); SetPx(20, 11, cR);
        SetPx(10, 12, cR); SetPx(11, 12, cR); SetPx(12, 12, cR); SetPx(13, 12, cR);
        SetPx(14, 12, cR); SetPx(15, 12, cR); SetPx(16, 12, cR); SetPx(17, 12, cR);
        SetPx(18, 12, cR); SetPx(19, 12, cR); SetPx(20, 12, cR); SetPx(21, 12, cR);
        SetPx(10, 13, cR); SetPx(12, 13, cR); SetPx(13, 13, cR); SetPx(14, 13, cR);
        SetPx(15, 13, cR); SetPx(16, 13, cR); SetPx(17, 13, cR); SetPx(18, 13, cR);
        SetPx(19, 13, cR); SetPx(20, 13, cR); SetPx(21, 13, cR); SetPx(10, 14, cR);
        SetPx(12, 14, cR); SetPx(13, 14, cR); SetPx(16, 14, cR); SetPx(17, 14, cR);
        SetPx(18, 14, cR); SetPx(19, 14, cR); SetPx(20, 14, cR); SetPx(21, 14, cR);
        SetPx(10, 15, cR); SetPx(12, 15, cR); SetPx(13, 15, cR); SetPx(16, 15, cR);
        SetPx(17, 15, cR); SetPx(18, 15, cR); SetPx(19, 15, cR); SetPx(20, 15, cR);
        SetPx(21, 15, cR); SetPx(11, 16, cR); SetPx(13, 16, cR); SetPx(16, 16, cR);
        SetPx(17, 16, cR); SetPx(18, 16, cR); SetPx(19, 16, cR); SetPx(20, 16, cR);
        SetPx(13, 17, cR); SetPx(14, 17, cR); SetPx(15, 17, cR); SetPx(16, 17, cR);
        SetPx(17, 17, cR); SetPx(18, 17, cR); SetPx(19, 17, cR); SetPx(13, 18, cR);
        SetPx(14, 18, cR); SetPx(15, 18, cR); SetPx(16, 18, cR); SetPx(17, 18, cR);
        SetPx(18, 18, cR); SetPx(19, 18, cR); SetPx(13, 19, cR); SetPx(14, 19, cR);
        SetPx(15, 19, cR); SetPx(16, 19, cR); SetPx(17, 19, cR); SetPx(18, 19, cR);
        SetPx(14, 20, cR); SetPx(15, 20, cR); SetPx(16, 20, cR); SetPx(17, 20, cR);
        SetPx(18, 20, cR); SetPx(14, 21, cR); SetPx(15, 21, cR); SetPx(16, 21, cR);
        SetPx(17, 21, cR); SetPx(14, 22, cR); SetPx(15, 22, cR); SetPx(16, 22, cR);
        SetPx(17, 22, cR); SetPx(15, 23, cR); SetPx(16, 23, cR); SetPx(15, 24, cR);
        SetPx(16, 24, cR);
        // 'H' -> cH (15 px)
        SetPx(15, 4, cH); SetPx(11, 5, cH); SetPx(11, 13, cH); SetPx(11, 14, cH);
        SetPx(14, 14, cH); SetPx(15, 14, cH); SetPx(11, 15, cH); SetPx(14, 15, cH);
        SetPx(15, 15, cH); SetPx(12, 16, cH); SetPx(14, 16, cH); SetPx(15, 16, cH);
        SetPx(12, 17, cH); SetPx(12, 18, cH); SetPx(13, 20, cH);

        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 32f);
    }

    internal static Sprite CreateCurseIcon(bool unlocked)
    {
        int w = 32, h = 32;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        Color[] pixels = new Color[w * h];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;

        Color cMain = unlocked ? new Color(0.62f, 0.30f, 0.90f, 1f) : new Color(0.28f, 0.20f, 0.36f, 0.90f);
        Color cLight = unlocked ? new Color(0.88f, 0.70f, 1f, 1f) : new Color(0.45f, 0.38f, 0.55f, 1f);
        Color cDark = new Color(0.06f, 0.02f, 0.10f, 1f);

        // Cráneo maldito: cráneo redondo + mandíbula, cuencas oscuras y nariz
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float dx = x - 15.5f;
                float dy = y - 18f;
                bool head = dx * dx + dy * dy <= 10.5f * 10.5f;
                bool jaw = y >= 4 && y <= 10 && x >= 10 && x <= 21;
                if (!(head || jaw)) continue;

                float d = Mathf.Sqrt(dx * dx + dy * dy);
                Color c = cMain;
                if (head && d > 8.5f) c = cLight;
                pixels[y * w + x] = c;
            }
        }
        void Hole(int x0, int y0, int x1, int y1)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (x >= 0 && x < w && y >= 0 && y < h) pixels[y * w + x] = cDark;
        }
        Hole(10, 16, 13, 20);
        Hole(18, 16, 21, 20);
        Hole(15, 12, 16, 14);
        for (int x = 11; x <= 20; x += 3) Hole(x, 4, x, 7);

        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 32f);
    }

    internal static Sprite CreateLightningBoltIcon(bool unlocked)
    {
        int w = 32, h = 32;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        Color[] pixels = new Color[w * h];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;

        void SetPx(int x, int y, Color c)
        {
            if (x >= 0 && x < w && y >= 0 && y < h) pixels[y * w + x] = c;
        }

        void FillRect(int x0, int y0, int x1, int y1, Color c)
        {
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    SetPx(x, y, c);
                }
            }
        }

        Color cBorder = new Color(0.05f, 0.08f, 0.14f, 0.92f);
        Color cGlow = unlocked ? new Color(0.22f, 0.65f, 0.95f, 0.85f) : new Color(0.18f, 0.30f, 0.42f, 0.70f);
        Color cMain = unlocked ? new Color(0.45f, 0.92f, 1.0f, 1f) : new Color(0.32f, 0.52f, 0.65f, 0.95f);
        Color cCore = unlocked ? new Color(1.0f, 0.97f, 0.72f, 1f) : new Color(0.58f, 0.72f, 0.80f, 0.95f);

        // Halo / borde exterior del rayo (y=0 abajo .. y=31 arriba en Texture2D)
        for (int row = 0; row < 24; row++)
        {
            int y = 4 + row;
            int cx;
            int halfW;
            if (row >= 14) // Tramo superior inclinado hacia la derecha -> centro
            {
                cx = 19 - (row - 14) / 2;
                halfW = 2;
            }
            else if (row >= 11) // Quiebre central ancho
            {
                cx = 16;
                halfW = 5;
            }
            else // Punta inferior hacia la izquierda
            {
                cx = 11 + (row * 5) / 11;
                halfW = Mathf.Max(1, row / 4 + 1);
            }

            FillRect(cx - halfW - 2, y - 1, cx + halfW + 2, y + 1, cBorder);
        }

        // Relleno eléctrico y núcleo brillante
        for (int row = 0; row < 24; row++)
        {
            int y = 4 + row;
            int cx;
            int halfW;
            if (row >= 14)
            {
                cx = 19 - (row - 14) / 2;
                halfW = 2;
            }
            else if (row >= 11)
            {
                cx = 16;
                halfW = 5;
            }
            else
            {
                cx = 11 + (row * 5) / 11;
                halfW = Mathf.Max(1, row / 4 + 1);
            }

            FillRect(cx - halfW - 1, y, cx + halfW + 1, y, cGlow);
            FillRect(cx - halfW, y, cx + halfW, y, cMain);
            if (halfW >= 2)
            {
                FillRect(cx - halfW + 1, y, cx + halfW - 1, y, cCore);
            }
            else
            {
                SetPx(cx, y, cCore);
            }
        }

        // Chispas laterales
        SetPx(8, 22, cCore); SetPx(9, 23, cMain);
        SetPx(23, 10, cCore); SetPx(22, 9, cMain);

        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 32f);
    }

    private static void EnsureSpritesCreated()
    {
        if (venenoSmallSprite == null)
        {
            venenoSmallSprite = CreatePoisonBubblesIcon(unlocked: false);
            UnityEngine.Object.DontDestroyOnLoad(venenoSmallSprite.texture);
            UnityEngine.Object.DontDestroyOnLoad(venenoSmallSprite);
        }
        if (venenoSmallBuyedSprite == null)
        {
            venenoSmallBuyedSprite = CreatePoisonBubblesIcon(unlocked: true);
            UnityEngine.Object.DontDestroyOnLoad(venenoSmallBuyedSprite.texture);
            UnityEngine.Object.DontDestroyOnLoad(venenoSmallBuyedSprite);
        }
        if (sangradoSmallSprite == null)
        {
            sangradoSmallSprite = CreateBloodSymbolIcon(unlocked: false);
            UnityEngine.Object.DontDestroyOnLoad(sangradoSmallSprite.texture);
            UnityEngine.Object.DontDestroyOnLoad(sangradoSmallSprite);
        }
        if (sangradoSmallBuyedSprite == null)
        {
            sangradoSmallBuyedSprite = CreateBloodSymbolIcon(unlocked: true);
            UnityEngine.Object.DontDestroyOnLoad(sangradoSmallBuyedSprite.texture);
            UnityEngine.Object.DontDestroyOnLoad(sangradoSmallBuyedSprite);
        }
        if (electricoSmallSprite == null)
        {
            electricoSmallSprite = CreateLightningBoltIcon(unlocked: false);
            UnityEngine.Object.DontDestroyOnLoad(electricoSmallSprite.texture);
            UnityEngine.Object.DontDestroyOnLoad(electricoSmallSprite);
        }
        if (electricoSmallBuyedSprite == null)
        {
            electricoSmallBuyedSprite = CreateLightningBoltIcon(unlocked: true);
            UnityEngine.Object.DontDestroyOnLoad(electricoSmallBuyedSprite.texture);
            UnityEngine.Object.DontDestroyOnLoad(electricoSmallBuyedSprite);
        }
        if (maldicionSmallSprite == null)
        {
            maldicionSmallSprite = CreateCurseIcon(unlocked: false);
            UnityEngine.Object.DontDestroyOnLoad(maldicionSmallSprite.texture);
            UnityEngine.Object.DontDestroyOnLoad(maldicionSmallSprite);
        }
        if (maldicionSmallBuyedSprite == null)
        {
            maldicionSmallBuyedSprite = CreateCurseIcon(unlocked: true);
            UnityEngine.Object.DontDestroyOnLoad(maldicionSmallBuyedSprite.texture);
            UnityEngine.Object.DontDestroyOnLoad(maldicionSmallBuyedSprite);
        }
        // Corazones de Mea Culpa: arte propio (64x64) en lugar de reutilizar el icono pequeño del árbol.
        if (maldicionHeartSprite == null) maldicionHeartSprite = PersistSprite(ElementalArt.GetHeart(ElementSkill.Curse));
        if (venenoHeartSprite == null) venenoHeartSprite = PersistSprite(ElementalArt.GetHeart(ElementSkill.Poison));
        if (sangradoHeartSprite == null) sangradoHeartSprite = PersistSprite(ElementalArt.GetHeart(ElementSkill.Bleed));
        if (electricoHeartSprite == null) electricoHeartSprite = PersistSprite(ElementalArt.GetHeart(ElementSkill.Electric));
    }

    private static Sprite vanillaIconRef;

    // Referencia de tamaño: el icono pequeño vanilla (para que los iconos nuevos ocupen lo mismo en pantalla).
    private static void TryBuildVanillaStyledIcons(Dictionary<string, UnlockableSkill> allSkills)
    {
        if (vanillaIconRef != null || allSkills == null) return;
        if (allSkills.TryGetValue("CHARGED_1", out UnlockableSkill v) && v != null)
        {
            vanillaIconRef = v.smallImage != null ? v.smallImage : v.smallImageBuyed;
        }
    }

    // Iconos pixel-art del árbol por elemento y nivel (1-3): cada nivel suma detalle y presencia del elemento.
    private static bool TryGetTierIcons(string id, out Sprite locked, out Sprite bought)
    {
        locked = bought = null;
        ElementSkill element;
        if (id.StartsWith("VENENO_")) element = ElementSkill.Poison;
        else if (id.StartsWith("SANGRADO_")) element = ElementSkill.Bleed;
        else if (id.StartsWith("ELECTRICO_")) element = ElementSkill.Electric;
        else if (id.StartsWith("MALDICION_")) element = ElementSkill.Curse;
        else return false;
        int tierNum = id[id.Length - 1] - '0';
        try
        {
            if (element == ElementSkill.Curse)
            {
                // Maldición: calavera de referencia recoloreada (con aura, grietas y puntos blancos)
                locked = SkullArt.GetIcon(element, tierNum, false, vanillaIconRef);
                bought = SkullArt.GetIcon(element, tierNum, true, vanillaIconRef);
            }
            if (locked == null || bought == null)
            {
                locked = ElementalSkillIcons.Get(element, tierNum, false, vanillaIconRef);
                bought = ElementalSkillIcons.Get(element, tierNum, true, vanillaIconRef);
            }
            return locked != null && bought != null;
        }
        catch (Exception ex)
        {
            DashParryDebugLog.Log("[Elemental] Error creando icono de " + id + ": " + ex);
            return false;
        }
    }

    private static Sprite PersistSprite(Sprite s)
    {
        if (s != null)
        {
            UnityEngine.Object.DontDestroyOnLoad(s.texture);
            UnityEngine.Object.DontDestroyOnLoad(s);
        }
        return s;
    }

    internal static void EnsureSkillsRegistered(SkillManager manager)
    {
        if (manager == null || AllSkillsField == null) return;
        var allSkills = AllSkillsField.GetValue(manager) as Dictionary<string, UnlockableSkill>;
        if (allSkills == null) return;

        EnsureSpritesCreated();
        TryBuildVanillaStyledIcons(allSkills);

        Sprite baseBig = null;
        if (allSkills.TryGetValue("CHARGED_1", out UnlockableSkill refSkill) && refSkill != null)
        {
            baseBig = refSkill.bigImage;
        }

        RegisterOrUpdateSkill(
            allSkills,
            SkillVeneno1,
            "Ponzoña de Mea Culpa I",
            "Imbuye la espada con toxina: 1.5 de daño/s + 0.25% de salud máx./s durante 6s. Sube +50% cada 4 golpes (hasta +150%; 12 en jefes).\nRequiere equipar el Corazón de Ponzoña Viva.",
            "Requiere equipar el Corazón de Ponzoña Viva en el menú de Mea Culpa.",
            tier: 1,
            cost: 600,
            parent: "",
            venenoSmallSprite,
            venenoSmallBuyedSprite,
            baseBig);

        RegisterOrUpdateSkill(
            allSkills,
            SkillVeneno2,
            "Ponzoña de Mea Culpa II",
            "Aumenta la potencia tóxica: 3 de daño/s + 0.4% de salud máx./s durante 8s. Con el Corazón, el enemigo envenenado que muere contagia a los cercanos (Contagio).\nRequiere equipar el Corazón de Ponzoña Viva.",
            "Requiere equipar el Corazón de Ponzoña Viva en el menú de Mea Culpa.",
            tier: 2,
            cost: 6500,
            parent: SkillVeneno1,
            venenoSmallSprite,
            venenoSmallBuyedSprite,
            baseBig);

        RegisterOrUpdateSkill(
            allSkills,
            SkillVeneno3,
            "Ponzoña de Mea Culpa III",
            "Ponzoña letal máxima: 4.5 de daño/s + 0.6% de salud máx./s durante 10s. Con el Corazón, contagia a los cercanos al morir (Contagio).\nRequiere equipar el Corazón de Ponzoña Viva.",
            "Requiere equipar el Corazón de Ponzoña Viva en el menú de Mea Culpa.",
            tier: 3,
            cost: 16000,
            parent: SkillVeneno2,
            venenoSmallSprite,
            venenoSmallBuyedSprite,
            baseBig);

        RegisterOrUpdateSkill(
            allSkills,
            SkillSangrado1,
            "Filo Sangrante I",
            "Acumula sangrado en 6 golpes (10 en jefes): estalla un 3% de salud máx. (tope 15% de la vida base en enemigos normales) y 3 de daño/s por 5s.\nRequiere equipar el Corazón de la Hemorragia Sagrada.",
            "Requiere equipar el Corazón de la Hemorragia Sagrada en el menú de Mea Culpa.",
            tier: 1,
            cost: 600,
            parent: "",
            sangradoSmallSprite,
            sangradoSmallBuyedSprite,
            baseBig);

        RegisterOrUpdateSkill(
            allSkills,
            SkillSangrado2,
            "Filo Sangrante II",
            "Heridas profundas tras el umbral: estalla un 4.5% de salud máx. y 4.5 de daño/s por 5s. Cada aplicación suma +20% de daño (máx +100%).\nRequiere equipar el Corazón de la Hemorragia Sagrada.",
            "Requiere equipar el Corazón de la Hemorragia Sagrada en el menú de Mea Culpa.",
            tier: 2,
            cost: 6500,
            parent: SkillSangrado1,
            sangradoSmallSprite,
            sangradoSmallBuyedSprite,
            baseBig);

        RegisterOrUpdateSkill(
            allSkills,
            SkillSangrado3,
            "Filo Sangrante III",
            "Hemorragia devastadora tras el umbral: estalla un 6% de salud máx. y 6 de daño/s por 5s. Con +100% acumulado, cada golpe extiende el sangrado 0.5s (Sangria, máx 8s).\nRequiere equipar el Corazón de la Hemorragia Sagrada.",
            "Requiere equipar el Corazón de la Hemorragia Sagrada en el menú de Mea Culpa.",
            tier: 3,
            cost: 16000,
            parent: SkillSangrado2,
            sangradoSmallSprite,
            sangradoSmallBuyedSprite,
            baseBig);

        RegisterOrUpdateSkill(
            allSkills,
            SkillElectrico1,
            "Centella de Mea Culpa I",
            "Tras 6 golpes sobre el mismo enemigo lo congela 1.5s (0.4s en jefes) y lo golpea con una descarga de 10 de daño. Cada golpe añade además 4 de daño eléctrico (+0.4% de la vida máx.). Inmune a nuevos golpes eléctricos 4s tras descongelar (8s en jefes).\nRequiere equipar el Corazón del Relámpago Consagrado.",
            "Requiere equipar el Corazón del Relámpago Consagrado en el menú de Mea Culpa.",
            tier: 1,
            cost: 600,
            parent: "",
            electricoSmallSprite,
            electricoSmallBuyedSprite,
            baseBig);

        RegisterOrUpdateSkill(
            allSkills,
            SkillElectrico2,
            "Centella de Mea Culpa II",
            "Tras 5 golpes: congela 2s (0.6s en jefes) y descarga 20 de daño. La descarga salta como arco a 1 enemigo cercano (12 de daño, +2 golpes eléctricos). Cada golpe añade además 6 de daño eléctrico (+0.6% de la vida máx.).\nRequiere equipar el Corazón del Relámpago Consagrado.",
            "Requiere equipar el Corazón del Relámpago Consagrado en el menú de Mea Culpa.",
            tier: 2,
            cost: 6500,
            parent: SkillElectrico1,
            electricoSmallSprite,
            electricoSmallBuyedSprite,
            baseBig);

        RegisterOrUpdateSkill(
            allSkills,
            SkillElectrico3,
            "Centella de Mea Culpa III",
            "Tras 4 golpes: congela 2.5s (0.8s en jefes) y descarga 32 de daño. La descarga salta como arco a 2 enemigos cercanos (19 de daño, +2 golpes eléctricos). Cada golpe añade además 9 de daño eléctrico (+0.9% de la vida máx.).\nRequiere equipar el Corazón del Relámpago Consagrado.",
            "Requiere equipar el Corazón del Relámpago Consagrado en el menú de Mea Culpa.",
            tier: 3,
            cost: 16000,
            parent: SkillElectrico2,
            electricoSmallSprite,
            electricoSmallBuyedSprite,
            baseBig);

        RegisterOrUpdateSkill(
            allSkills,
            SkillMaldicion1,
            "Maldición de Mea Culpa I",
            "Cada golpe maldice al enemigo 6s: su daño a los jugadores se reduce un 10%. Tras 5 golpes sobre el mismo enemigo la reducción se multiplica x1.4 mientras dure la maldición (máx. 35%; x0.6 contra jefes). Cada golpe además cura al portador un 0.5% de su salud máx. (3s de enfriamiento; cada enemigo solo puede curar cada 4s).\nRequiere equipar el Corazón de la Maldición Penitente.",
            "Requiere equipar el Corazón de la Maldición Penitente en el menú de Mea Culpa.",
            tier: 1,
            cost: 600,
            parent: "",
            maldicionSmallSprite,
            maldicionSmallBuyedSprite,
            baseBig);

        RegisterOrUpdateSkill(
            allSkills,
            SkillMaldicion2,
            "Maldición de Mea Culpa II",
            "Maldición más profunda (7s): -18% al daño del enemigo (x1.4 tras 5 golpes) y drenaje del 0.75% de salud máx. por golpe (3s de enfriamiento).\nRequiere equipar el Corazón de la Maldición Penitente.",
            "Requiere equipar el Corazón de la Maldición Penitente en el menú de Mea Culpa.",
            tier: 2,
            cost: 6500,
            parent: SkillMaldicion1,
            maldicionSmallSprite,
            maldicionSmallBuyedSprite,
            baseBig);

        RegisterOrUpdateSkill(
            allSkills,
            SkillMaldicion3,
            "Maldición de Mea Culpa III",
            "Maldición suprema (8s): -25% al daño del enemigo (x1.4 tras 5 golpes, máx. 35%) y drenaje del 1% de salud máx. por golpe (3s de enfriamiento).\nRequiere equipar el Corazón de la Maldición Penitente.",
            "Requiere equipar el Corazón de la Maldición Penitente en el menú de Mea Culpa.",
            tier: 3,
            cost: 16000,
            parent: SkillMaldicion2,
            maldicionSmallSprite,
            maldicionSmallBuyedSprite,
            baseBig);
    }

    private static void RegisterOrUpdateSkill(
        Dictionary<string, UnlockableSkill> allSkills,
        string id,
        string caption,
        string description,
        string instructions,
        int tier,
        int cost,
        string parent,
        Sprite smallSprite,
        Sprite smallBuyedSprite,
        Sprite bigSprite)
    {
        if (!allSkills.TryGetValue(id, out UnlockableSkill skill) || skill == null)
        {
            skill = ScriptableObject.CreateInstance<UnlockableSkill>();
            UnityEngine.Object.DontDestroyOnLoad(skill);
            skill.name = id;
            skill.id = id;
            skill.unlocked = false;
            allSkills[id] = skill;
        }

        skill.caption = caption;
        skill.description = description;
        skill.instructions = instructions;
        skill.tier = tier;
        skill.cost = cost;
        skill.parentSkill = parent;
        if (TryGetTierIcons(id, out Sprite tierLocked, out Sprite tierBought))
        {
            smallSprite = tierLocked;
            smallBuyedSprite = tierBought;
        }
        skill.smallImage = smallSprite;
        skill.smallImageBuyed = smallBuyedSprite;
        skill.bigImage = bigSprite;
    }

    internal static void EnsureSwordsRegistered(InventoryManager invManager)
    {
        if (invManager == null || AllSwordsField == null || OwnSwordsField == null) return;
        var allSwords = AllSwordsField.GetValue(invManager) as Dictionary<string, Sword>;
        var ownSwords = OwnSwordsField.GetValue(invManager) as List<Sword>;
        if (allSwords == null || ownSwords == null) return;

        if (!CoopGameConfig.EnableElementalUpgrades)
        {
            // Si el mod elemental está desactivado en la pre-partida:
            // 1. Desequipar si P1 lo tiene equipado
            if (Core.InventoryManager != null)
            {
                Sword equippedP1 = Core.InventoryManager.GetSwordInSlot(0);
                if (equippedP1 != null && IsElementalHeart(equippedP1.id))
                {
                    Core.InventoryManager.SetSwordInSlot(0, (Sword)null);
                }
            }

            // 2. Remover de ownSwords para P1
            ownSwords.RemoveAll(s => s != null && IsElementalHeart(s.id));

            // 3. Remover y desequipar para P2, P3 y P4
            for (int p = 1; p < 4; p++)
            {
                Player2InventoryManager.RemoveOwnedSword(HeartVenenoId, p);
                Player2InventoryManager.RemoveOwnedSword(HeartSangradoId, p);
                Player2InventoryManager.RemoveOwnedSword(HeartElectricoId, p);
                Player2InventoryManager.RemoveOwnedSword(HeartMaldicionId, p);
            }
            return;
        }

        EnsureSpritesCreated();

        RegisterOrUpdateSwordHeart(
            allSwords,
            ownSwords,
            HeartVenenoId,
            "Corazón de Ponzoña Viva",
            "Canaliza la Senda del Veneno desbloqueada en el Árbol de Habilidades.\n• Veneno I: 1.5 de daño/s + 0.25% vida máx./s durante 6s.\n• Veneno II: 3 de daño/s + 0.4% vida máx./s durante 8s.\n• Veneno III: 4.5 de daño/s + 0.6% vida máx./s durante 10s.\nContagio (Veneno II+): al morir un enemigo envenenado, propaga el veneno a los enemigos a 4 unidades con 50% de la duración restante.\n(El daño de veneno solo se aplica mientras este corazón está equipado y se posee al menos Ponzoña de Mea Culpa I).",
            "Un corazón cristalizado en bilis esmeralda que late al unísono con las espinas del puño de Mea Culpa.",
            venenoHeartSprite);

        RegisterOrUpdateSwordHeart(
            allSwords,
            ownSwords,
            HeartSangradoId,
            "Corazón de la Hemorragia Sagrada",
            "Canaliza la Senda del Sangrado desbloqueada en el Árbol de Habilidades.\nCada impacto acumula sangrado (Umbral: 6 golpes en enemigos normales, 10 en jefes).\n• Sangrado I: 3% Vida Máx + 3 de daño/s por 5s.\n• Sangrado II: 4.5% Vida Máx + 4.5 de daño/s por 5s.\n• Sangrado III: 6% Vida Máx + 6 de daño/s por 5s.\nSangria: con el bonus al máximo (+100%), cada golpe durante el sangrado lo extiende 0.5s (máx. 8s).\n(El sangrado solo se aplica mientras este corazón está equipado y se posee al menos Filo Sangrante I).",
            "Forjado en sangre coagulada del Milagro, desgarra el interior de quienes sufren el castigo reiterado del acero.",
            sangradoHeartSprite);

        RegisterOrUpdateSwordHeart(
            allSwords,
            ownSwords,
            HeartElectricoId,
            "Corazón del Relámpago Consagrado",
            "Canaliza la Senda de la Electricidad desbloqueada en el Árbol de Habilidades.\nLos golpes acumulan carga eléctrica en el enemigo; al llegar al umbral lo congela y lo fulmina con una descarga.\n• Centella I: 6 golpes, congela 1.5s (jefe 0.4s), descarga 10.\n• Centella II: 5 golpes, congela 2s (jefe 0.6s), descarga 20 + arco a 1 enemigo.\n• Centella III: 4 golpes, congela 2.5s (jefe 0.8s), descarga 32 + arcos a 2 enemigos.\nConductor: los arcos saltan a enemigos a 5 unidades (60% de la descarga). Tras descongelar el enemigo es inmune 4s (8s en jefes).\n(La descarga solo se activa mientras este corazón está equipado y se posee al menos Centella de Mea Culpa I).",
            "Un corazón atravesado por centellas benditas del Milagro que resuena con cada tajo de Mea Culpa.",
            electricoHeartSprite);

        RegisterOrUpdateSwordHeart(
            allSwords,
            ownSwords,
            HeartMaldicionId,
            "Corazón de la Maldición Penitente",
            "Canaliza la Senda de la Maldición desbloqueada en el Árbol de Habilidades.\nCada impacto maldice al enemigo (renovable): reduce el daño que inflige a los jugadores.\n• Maldición I: 6s, -10% daño enemigo + drenaje 0.5% de vida máx.\n• Maldición II: 7s, -18% daño enemigo + drenaje 0.75% de vida máx.\n• Maldición III: 8s, -25% daño enemigo + drenaje 1% de vida máx.\nTras 5 golpes sobre el mismo enemigo la reducción se multiplica x1.4 (máx. 35%; x0.6 contra jefes). El drenaje tiene 3s de enfriamiento (4s por enemigo).\nPacto: con la maldición potenciada, el 35% de la curación también se da a los demás jugadores vivos.\n(Solo se aplica mientras este corazón está equipado y se posee al menos Maldición de Mea Culpa I).",
            "Un corazón envuelto en cera negra y plegarias retorcidas: marchita la furia de quien osa herir al Penitente.",
            maldicionHeartSprite);

        for (int p = 1; p < 4; p++)
        {
            Player2InventoryManager.AddOwnedSword(HeartVenenoId, p);
            Player2InventoryManager.AddOwnedSword(HeartSangradoId, p);
            Player2InventoryManager.AddOwnedSword(HeartElectricoId, p);
            Player2InventoryManager.AddOwnedSword(HeartMaldicionId, p);
        }
    }

    private static void RegisterOrUpdateSwordHeart(
        Dictionary<string, Sword> allSwords,
        List<Sword> ownSwords,
        string id,
        string caption,
        string description,
        string lore,
        Sprite icon)
    {
        if (!allSwords.TryGetValue(id, out Sword sword) || sword == null)
        {
            GameObject holder = new GameObject("SwordHeart_" + id);
            UnityEngine.Object.DontDestroyOnLoad(holder);
            sword = holder.AddComponent<Sword>();
            sword.name = id;
            sword.id = id;
            sword.carryonstart = true;
            sword.preserveInNewGamePlus = true;
            allSwords[id] = sword;
        }

        sword.caption = caption;
        sword.description = description;
        sword.lore = lore;
        sword.picture = icon;

        if (!ownSwords.Contains(sword))
        {
            ownSwords.Add(sword);
        }
    }

    internal static void GrantMatchingHeartOnUnlock(string skillId, bool isPlayer2) =>
        GrantMatchingHeartOnUnlock(skillId, isPlayer2, isPlayer2 ? 1 : 0);

    internal static void GrantMatchingHeartOnUnlock(string skillId, bool isPlayer2, int playerIndex)
    {
        if (!CoopGameConfig.EnableElementalUpgrades) return;

        if (Core.InventoryManager != null)
        {
            EnsureSwordsRegistered(Core.InventoryManager);
        }

        int targetIdx = playerIndex > 0 ? playerIndex : (isPlayer2 ? 1 : 0);

        if (IsVenenoSkill(skillId))
        {
            if (targetIdx > 0)
            {
                Player2InventoryManager.AddOwnedSword(HeartVenenoId, targetIdx);
                if (string.IsNullOrEmpty(Player2InventoryManager.GetEquippedSwordId(targetIdx)))
                {
                    Player2InventoryManager.EquipSword(HeartVenenoId, targetIdx);
                }
            }
            else if (Core.InventoryManager != null)
            {
                Core.InventoryManager.AddSword(HeartVenenoId);
                if (Core.InventoryManager.GetSwordInSlot(0) == null)
                {
                    Core.InventoryManager.SetSwordInSlot(0, HeartVenenoId);
                }
            }
        }
        else if (IsSangradoSkill(skillId))
        {
            if (targetIdx > 0)
            {
                Player2InventoryManager.AddOwnedSword(HeartSangradoId, targetIdx);
                if (string.IsNullOrEmpty(Player2InventoryManager.GetEquippedSwordId(targetIdx)))
                {
                    Player2InventoryManager.EquipSword(HeartSangradoId, targetIdx);
                }
            }
            else if (Core.InventoryManager != null)
            {
                Core.InventoryManager.AddSword(HeartSangradoId);
                if (Core.InventoryManager.GetSwordInSlot(0) == null)
                {
                    Core.InventoryManager.SetSwordInSlot(0, HeartSangradoId);
                }
            }
        }
        else if (IsElectricoSkill(skillId))
        {
            if (targetIdx > 0)
            {
                Player2InventoryManager.AddOwnedSword(HeartElectricoId, targetIdx);
                if (string.IsNullOrEmpty(Player2InventoryManager.GetEquippedSwordId(targetIdx)))
                {
                    Player2InventoryManager.EquipSword(HeartElectricoId, targetIdx);
                }
            }
            else if (Core.InventoryManager != null)
            {
                Core.InventoryManager.AddSword(HeartElectricoId);
                if (Core.InventoryManager.GetSwordInSlot(0) == null)
                {
                    Core.InventoryManager.SetSwordInSlot(0, HeartElectricoId);
                }
            }
        }
        else if (IsMaldicionSkill(skillId))
        {
            if (targetIdx > 0)
            {
                Player2InventoryManager.AddOwnedSword(HeartMaldicionId, targetIdx);
                if (string.IsNullOrEmpty(Player2InventoryManager.GetEquippedSwordId(targetIdx)))
                {
                    Player2InventoryManager.EquipSword(HeartMaldicionId, targetIdx);
                }
            }
            else if (Core.InventoryManager != null)
            {
                Core.InventoryManager.AddSword(HeartMaldicionId);
                if (Core.InventoryManager.GetSwordInSlot(0) == null)
                {
                    Core.InventoryManager.SetSwordInSlot(0, HeartMaldicionId);
                }
            }
        }
    }

    internal static void ResetElementalPath(bool isPlayer2) => ResetElementalPath(isPlayer2 ? 1 : 0);

    internal static void ResetElementalPath(int playerIndex)
    {
        float refunded = 0f;
        string[] allElem =
        {
            SkillVeneno1, SkillVeneno2, SkillVeneno3,
            SkillSangrado1, SkillSangrado2, SkillSangrado3,
            SkillElectrico1, SkillElectrico2, SkillElectrico3,
            SkillMaldicion1, SkillMaldicion2, SkillMaldicion3
        };

        bool isGuest = playerIndex > 0;
        foreach (string id in allElem)
        {
            UnlockableSkill sk = Core.SkillManager != null ? Core.SkillManager.GetSkill(id) : null;
            int cost = sk != null ? sk.cost : 0;
            if (isGuest)
            {
                if (Player2SkillManager.IsUnlocked(playerIndex, id))
                {
                    Player2SkillManager.SetUnlocked(playerIndex, id, false);
                    refunded += cost;
                }
            }
            else if (Core.SkillManager != null && Core.SkillManager.IsSkillUnlocked(id))
            {
                Core.SkillManager.LockSkill(id);
                refunded += cost;
            }
        }

        if (refunded > 0f)
        {
            if (isGuest)
            {
                Penitent guest = CoopPlayerRegistry.GetPenitent(playerIndex);
                if (guest != null && guest.Stats?.Purge != null)
                {
                    guest.Stats.Purge.Current += refunded;
                    Player2StatsSync.SaveCurrentVitals(guest, playerIndex);
                }
                Player2SkillManager.Persist();
            }
            else if (Core.Logic?.Penitent != null)
            {
                Core.Logic.Penitent.Stats.Purge.Current += refunded;
            }
            Core.Audio?.PlayOneShot("event:/SFX/UI/UnequipItem");
        }
    }

    // =========================================================================
    // Atajo F8 de Debug: Cicla y prueba estados de Veneno / Sangrado y Rezos
    // =========================================================================
    private static int debugCycleIndex = 0;
    private static readonly string[] TestPrayers = { "PR01", "PR03", "PR04", "PR10", "PR12", "PR14", "PR15", "PR16", "PR201", "PR202", "PR203" };
    private static int debugPrayerIndex = 0;

    internal static void CycleDebugElementalAndPrayers()
    {
        if (!CoopGameConfig.EnableElementalUpgrades)
        {
            if (Main.CoopLocal != null) Blasphemous.ModdingAPI.ModLog.Info("[F8 Debug] Mejoras elementales desactivadas en la configuración pre-partida.", Main.CoopLocal);
            return;
        }
        if (Core.SkillManager == null || Core.InventoryManager == null) return;
        EnsureSkillsRegistered(Core.SkillManager);
        EnsureSwordsRegistered(Core.InventoryManager);

        debugCycleIndex = (debugCycleIndex + 1) % 9;
        string modeDescription = "";

        switch (debugCycleIndex)
        {
            case 0:
                // Modo 0: Resetear sendas elementales en ambos jugadores
                ResetElementalPath(0);
                ResetElementalPath(1);
                if (Core.InventoryManager.GetSwordInSlot(0) != null) Core.InventoryManager.SetSwordInSlot(0, (Sword)null);
                Player2InventoryManager.UnequipSword();
                modeDescription = "Elemental: NINGUNO (Espada estándar)";
                break;
            case 1:
                // Modo 1: Veneno I en ambos
                ApplyDebugElemental(SkillVeneno1, HeartVenenoId);
                modeDescription = "Elemental: VENENO I (1.5 DPS + 0.25% HP/s / 6s) + Corazón Ponzoña";
                break;
            case 2:
                // Modo 2: Veneno III en ambos
                ApplyDebugElemental(SkillVeneno3, HeartVenenoId);
                modeDescription = "Elemental: VENENO III (4.5 DPS + 0.6% HP/s / 10s) + Corazón Ponzoña";
                break;
            case 3:
                // Modo 3: Sangrado I en ambos
                ApplyDebugElemental(SkillSangrado1, HeartSangradoId);
                modeDescription = "Elemental: SANGRADO I (3% Max HP + 3 DPS / 5s) + Corazón Hemorragia";
                break;
            case 4:
                // Modo 4: Sangrado III en ambos
                ApplyDebugElemental(SkillSangrado3, HeartSangradoId);
                modeDescription = "Elemental: SANGRADO III (6% Max HP + 6 DPS / 5s) + Corazón Hemorragia";
                break;
            case 5:
                // Modo 5: Electricidad I en ambos
                ApplyDebugElemental(SkillElectrico1, HeartElectricoId);
                modeDescription = "Elemental: ELECTRICIDAD I (6 golpes -> congela 1.5s + descarga 10) + Corazón Relámpago";
                break;
            case 6:
                // Modo 6: Electricidad III en ambos
                ApplyDebugElemental(SkillElectrico3, HeartElectricoId);
                modeDescription = "Elemental: ELECTRICIDAD III (4 golpes -> congela 2.5s + descarga 32 + 2 arcos) + Corazón Relámpago";
                break;
            case 7:
                // Modo 7: P1 Veneno III, P2 Sangrado III (Híbrido)
                ApplyDebugElementalPerPlayer(p1Skill: SkillVeneno3, p1Heart: HeartVenenoId, p2Skill: SkillSangrado3, p2Heart: HeartSangradoId);
                modeDescription = "Elemental: HÍBRIDO (P1 Veneno III | P2 Sangrado III)";
                break;
            case 8:
                // Modo 8: P1 Sangrado III, P2 Electricidad III (Híbrido inverso)
                ApplyDebugElementalPerPlayer(p1Skill: SkillSangrado3, p1Heart: HeartSangradoId, p2Skill: SkillElectrico3, p2Heart: HeartElectricoId);
                modeDescription = "Elemental: HÍBRIDO (P1 Sangrado III | P2 Electricidad III)";
                break;
        }

        // Además, en cada pulsación de F8 avanzar al siguiente rezo de prueba para P1 y P2
        string nextPrayerId = TestPrayers[debugPrayerIndex];
        debugPrayerIndex = (debugPrayerIndex + 1) % TestPrayers.Length;
        ApplyDebugPrayer(nextPrayerId);

        // Rellenar Fervor al máximo para probar el rezo inmediatamente
        if (Core.Logic?.Penitent?.Stats?.Fervour != null) Core.Logic.Penitent.Stats.Fervour.SetToCurrentMax();
        if (CoopLocal.Player2?.Stats?.Fervour != null)
        {
            CoopLocal.Player2.Stats.Fervour.SetToCurrentMax();
            Player2StatsSync.SaveCurrentVitals(CoopLocal.Player2);
        }

        Prayer prayerObj = Core.InventoryManager.GetPrayer(nextPrayerId);
        string prayerName = prayerObj != null ? prayerObj.caption : nextPrayerId;
        string logMsg = $"[F8 Debug] {modeDescription} | Rezo equipado: {nextPrayerId} ({prayerName}) | Fervor al 100%";

        Core.Audio?.PlayOneShot("event:/SFX/UI/EquipPrayer");
        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info(logMsg, Main.CoopLocal);
        }
    }

    private static void ApplyDebugElemental(string skillId, string heartId)
    {
        ApplyDebugElementalPerPlayer(skillId, heartId, skillId, heartId);
    }

    private static void ApplyDebugElementalPerPlayer(string p1Skill, string p1Heart, string p2Skill, string p2Heart)
    {
        // P1
        ResetElementalPath(false);
        if (!string.IsNullOrEmpty(p1Skill))
        {
            if (p1Skill.EndsWith("_3"))
            {
                string prefix = p1Skill.Substring(0, p1Skill.Length - 1);
                Core.SkillManager.UnlockSkill(prefix + "1", ignoreChecks: true);
                Core.SkillManager.UnlockSkill(prefix + "2", ignoreChecks: true);
                Core.SkillManager.UnlockSkill(prefix + "3", ignoreChecks: true);
            }
            else
            {
                Core.SkillManager.UnlockSkill(p1Skill, ignoreChecks: true);
            }
            Core.InventoryManager.AddSword(p1Heart);
            Core.InventoryManager.SetSwordInSlot(0, p1Heart);
        }

        // P2
        ResetElementalPath(true);
        if (!string.IsNullOrEmpty(p2Skill))
        {
            if (p2Skill.EndsWith("_3"))
            {
                string prefix = p2Skill.Substring(0, p2Skill.Length - 1);
                Player2SkillManager.SetUnlocked(prefix + "1", true);
                Player2SkillManager.SetUnlocked(prefix + "2", true);
                Player2SkillManager.SetUnlocked(prefix + "3", true);
            }
            else
            {
                Player2SkillManager.SetUnlocked(p2Skill, true);
            }
            Player2SkillManager.Persist();
            Player2InventoryManager.AddOwnedSword(p2Heart);
            Player2InventoryManager.EquipSword(p2Heart);
        }
    }

    private static void ApplyDebugPrayer(string prayerId)
    {
        if (string.IsNullOrEmpty(prayerId) || Core.InventoryManager == null) return;
        Prayer prayer = Core.InventoryManager.GetPrayer(prayerId);
        if (prayer != null)
        {
            // P1
            Core.InventoryManager.AddPrayer(prayer);
            Core.InventoryManager.SetPrayerInSlot(0, prayer);

            // P2
            Player2InventoryManager.AddOwnedPrayer(prayerId);
            Player2InventoryManager.EquipPrayer(prayerId);
        }
    }

    // =========================================================================
    // Inyección visual de los 6 nodos (VENENO I-III y SANGRADO I-III) en NewInventory_LayoutSkill
    // =========================================================================
    private static readonly FieldInfo SkillsRootField = Reflect.Field(typeof(NewInventory_LayoutSkill), "skillsRoot");
    private static readonly FieldInfo CachedSkillsField = Reflect.Field(typeof(NewInventory_LayoutSkill), "cachedSkills");
    private static readonly FieldInfo SkillIdField = Reflect.Field(typeof(NewInventory_Skill), "skill");
    private static readonly MethodInfo SelectSkillMethod = Reflect.Method(typeof(NewInventory_LayoutSkill), "SelectSkill");
    private static readonly MethodInfo ActivateSkillMethod = Reflect.Method(typeof(NewInventory_LayoutSkill), "ActivateSkill");

    internal static void EnsureSkillTreeNodesInjected(NewInventory_LayoutSkill layout)
    {
        if (layout == null || Core.SkillManager == null) return;
        EnsureSkillsRegistered(Core.SkillManager);

        var skillsRoot = SkillsRootField?.GetValue(layout) as GameObject;
        var cachedSkills = CachedSkillsField?.GetValue(layout) as List<NewInventory_Skill>;
        if (skillsRoot == null || cachedSkills == null || cachedSkills.Count == 0) return;

        // Guardado diferido de la migración de versión de layout (ver CoopGameConfig.MigrateTreeLayoutIfOutdated)
        if (CoopGameConfig.TreeLayoutMigrationPending) CoopGameConfig.Save();

        Transform containerTrans = skillsRoot.transform.Find("ElementalTreeContainer");
        if (containerTrans != null)
        {
            containerTrans.gameObject.SetActive(CoopGameConfig.EnableElementalUpgrades);
        }

        NewInventory_Skill leftTier1 = cachedSkills.Find(s => s != null && s.GetSkillId() == "COMBO_1");
        NewInventory_Skill leftTier2 = cachedSkills.Find(s => s != null && s.GetSkillId() == "COMBO_2");
        NewInventory_Skill leftTier3 = cachedSkills.Find(s => s != null && s.GetSkillId() == "COMBO_3");

        NewInventory_Skill rightTier1 = cachedSkills.Find(s => s != null && s.GetSkillId() == "LUNGE_1");
        NewInventory_Skill rightTier2 = cachedSkills.Find(s => s != null && s.GetSkillId() == "LUNGE_2");
        NewInventory_Skill rightTier3 = cachedSkills.Find(s => s != null && s.GetSkillId() == "LUNGE_3");

        NewInventory_Skill v1Node = cachedSkills.Find(s => s != null && s.GetSkillId() == SkillVeneno1);
        NewInventory_Skill v2Node = cachedSkills.Find(s => s != null && s.GetSkillId() == SkillVeneno2);
        NewInventory_Skill v3Node = cachedSkills.Find(s => s != null && s.GetSkillId() == SkillVeneno3);

        NewInventory_Skill s1Node = cachedSkills.Find(s => s != null && s.GetSkillId() == SkillSangrado1);
        NewInventory_Skill s2Node = cachedSkills.Find(s => s != null && s.GetSkillId() == SkillSangrado2);
        NewInventory_Skill s3Node = cachedSkills.Find(s => s != null && s.GetSkillId() == SkillSangrado3);

        NewInventory_Skill e1Node = cachedSkills.Find(s => s != null && s.GetSkillId() == SkillElectrico1);
        NewInventory_Skill e2Node = cachedSkills.Find(s => s != null && s.GetSkillId() == SkillElectrico2);
        NewInventory_Skill e3Node = cachedSkills.Find(s => s != null && s.GetSkillId() == SkillElectrico3);

        NewInventory_Skill m1Node = cachedSkills.Find(s => s != null && s.GetSkillId() == SkillMaldicion1);
        NewInventory_Skill m2Node = cachedSkills.Find(s => s != null && s.GetSkillId() == SkillMaldicion2);
        NewInventory_Skill m3Node = cachedSkills.Find(s => s != null && s.GetSkillId() == SkillMaldicion3);

        // Comprobar si ya fueron inyectados
        if (v1Node != null)
        {
            if (containerTrans != null)
            {
                Transform oldVen = containerTrans.Find("Lbl_Veneno");
                if (oldVen != null) UnityEngine.Object.Destroy(oldVen.gameObject);
                Transform oldSan = containerTrans.Find("Lbl_Sangrado");
                if (oldSan != null) UnityEngine.Object.Destroy(oldSan.gameObject);

                containerTrans.gameObject.SetActive(CoopGameConfig.EnableElementalUpgrades);
                UpdateElementalTreeTransform(skillsRoot);
            }

            if (CoopGameConfig.EnableElementalUpgrades)
            {
                WireNodeNavigation(v1Node, up: null, down: v2Node, left: m1Node, right: leftTier1);
                WireNodeNavigation(v2Node, up: v1Node, down: v3Node, left: m2Node, right: leftTier2 ?? leftTier1);
                WireNodeNavigation(v3Node, up: v2Node, down: null, left: m3Node, right: leftTier3 ?? leftTier2 ?? leftTier1);

                WireNodeNavigation(m1Node, up: null, down: m2Node, left: null, right: v1Node);
                WireNodeNavigation(m2Node, up: m1Node, down: m3Node, left: null, right: v2Node);
                WireNodeNavigation(m3Node, up: m2Node, down: null, left: null, right: v3Node);

                ConnectLeftNeighbor(leftTier1, v1Node);
                ConnectLeftNeighbor(leftTier2, v2Node);
                ConnectLeftNeighbor(leftTier3, v3Node);

                WireNodeNavigation(s1Node, up: null, down: s2Node, left: rightTier1, right: e1Node);
                WireNodeNavigation(s2Node, up: s1Node, down: s3Node, left: rightTier2 ?? rightTier1, right: e2Node);
                WireNodeNavigation(s3Node, up: s2Node, down: null, left: rightTier3 ?? rightTier2 ?? rightTier1, right: e3Node);

                ConnectRightNeighbor(rightTier1, s1Node);
                ConnectRightNeighbor(rightTier2, s2Node);
                ConnectRightNeighbor(rightTier3, s3Node);

                WireNodeNavigation(e1Node, up: null, down: e2Node, left: s1Node, right: null);
                WireNodeNavigation(e2Node, up: e1Node, down: e3Node, left: s2Node, right: null);
                WireNodeNavigation(e3Node, up: e2Node, down: null, left: s3Node, right: null);

                SanitizeVanillaNodeNavigation(cachedSkills, v1Node, v2Node, v3Node, s1Node, s2Node, s3Node, e1Node, e2Node, e3Node, m1Node, m2Node, m3Node);
            }
            else
            {
                DisconnectLeftNeighbor(leftTier1);
                DisconnectLeftNeighbor(leftTier2);
                DisconnectLeftNeighbor(leftTier3);

                DisconnectRightNeighbor(rightTier1);
                DisconnectRightNeighbor(rightTier2);
                DisconnectRightNeighbor(rightTier3);
            }
            return;
        }

        // Si el mod elemental está desactivado y aún no se han inyectado, no crearlos
        if (!CoopGameConfig.EnableElementalUpgrades)
        {
            return;
        }

        // Localizar explícitamente los nodos extremos izquierdo (Combo: Last Words) y derecho (Lunge: Sacred Thrust)
        NewInventory_Skill templateNode = cachedSkills[0];
        float yTier2 = 0f;

        if (leftTier2 != null) yTier2 = leftTier2.transform.localPosition.y;
        else if (rightTier2 != null) yTier2 = rightTier2.transform.localPosition.y;

        // Contenedor dedicado para el árbol elemental completo (permite mover y escalar en vivo)
        GameObject containerObj;
        if (containerTrans == null)
        {
            containerObj = new GameObject("ElementalTreeContainer", typeof(RectTransform));
            containerObj.transform.SetParent(skillsRoot.transform, false);
            containerTrans = containerObj.transform;
        }
        else
        {
            containerObj = containerTrans.gameObject;
        }

        containerObj.SetActive(CoopGameConfig.EnableElementalUpgrades);

        // Si TreeOffsetX aún tiene centinela o valor inválido, inicializarlo a la posición calibrada permanente
        if (CoopGameConfig.TreeOffsetX < -500f || CoopGameConfig.TreeOffsetX > 500f)
        {
            CoopGameConfig.TreeOffsetX = CoopGameConfig.DefaultTreeOffsetX;
            CoopGameConfig.TreeOffsetY = CoopGameConfig.DefaultTreeOffsetY;
            CoopGameConfig.Save();
        }

        containerTrans.localPosition = new Vector3(CoopGameConfig.TreeOffsetX, CoopGameConfig.TreeOffsetY, 0f);
        containerTrans.localScale = Vector3.one * CoopGameConfig.TreeScale;

        if (CoopGameConfig.ElementalBranchSpacing < 20f)
        {
            CoopGameConfig.ElementalBranchSpacing = CoopGameConfig.DefaultElementalBranchSpacing;
        }
        if (CoopGameConfig.ElementalNodeVerticalSpacing < 10f)
        {
            CoopGameConfig.ElementalNodeVerticalSpacing = CoopGameConfig.DefaultElementalNodeVerticalSpacing;
        }

        // Posicionar columnas: Veneno (extremo izquierdo), Sangrado (derecha vanilla) y Electricidad (a la derecha de Sangrado)
        float halfSpacing = CoopGameConfig.ElementalBranchSpacing * 0.5f;
        float localVenenoX = -halfSpacing;
        float localSangradoX = halfSpacing;
        float localElectricoX = halfSpacing + CoopGameConfig.ElectricoColumnOffset;
        float localMaldicionX = -halfSpacing - CoopGameConfig.MaldicionColumnOffset;

        // Separación vertical entre niveles I, II y III según configuración (Nivel 1 ARRIBA, Nivel 3 ABAJO)
        float elemTier2Y = yTier2;
        float elemTier1Y = elemTier2Y + CoopGameConfig.ElementalNodeVerticalSpacing;
        float elemTier3Y = elemTier2Y - CoopGameConfig.ElementalNodeVerticalSpacing;

        NewInventory_Skill v1 = CloneSkillNode(layout, containerTrans, templateNode, SkillVeneno1, new Vector3(localVenenoX, elemTier1Y, 0f), new Color(0.55f, 1f, 0.6f));
        NewInventory_Skill v2 = CloneSkillNode(layout, containerTrans, templateNode, SkillVeneno2, new Vector3(localVenenoX, elemTier2Y, 0f), new Color(0.55f, 1f, 0.6f));
        NewInventory_Skill v3 = CloneSkillNode(layout, containerTrans, templateNode, SkillVeneno3, new Vector3(localVenenoX, elemTier3Y, 0f), new Color(0.55f, 1f, 0.6f));

        NewInventory_Skill s1 = CloneSkillNode(layout, containerTrans, templateNode, SkillSangrado1, new Vector3(localSangradoX, elemTier1Y, 0f), new Color(1f, 0.55f, 0.55f));
        NewInventory_Skill s2 = CloneSkillNode(layout, containerTrans, templateNode, SkillSangrado2, new Vector3(localSangradoX, elemTier2Y, 0f), new Color(1f, 0.55f, 0.55f));
        NewInventory_Skill s3 = CloneSkillNode(layout, containerTrans, templateNode, SkillSangrado3, new Vector3(localSangradoX, elemTier3Y, 0f), new Color(1f, 0.55f, 0.55f));

        NewInventory_Skill e1 = CloneSkillNode(layout, containerTrans, templateNode, SkillElectrico1, new Vector3(localElectricoX, elemTier1Y, 0f), new Color(0.45f, 0.9f, 1f));
        NewInventory_Skill e2 = CloneSkillNode(layout, containerTrans, templateNode, SkillElectrico2, new Vector3(localElectricoX, elemTier2Y, 0f), new Color(0.45f, 0.9f, 1f));
        NewInventory_Skill e3 = CloneSkillNode(layout, containerTrans, templateNode, SkillElectrico3, new Vector3(localElectricoX, elemTier3Y, 0f), new Color(0.45f, 0.9f, 1f));

        NewInventory_Skill m1 = CloneSkillNode(layout, containerTrans, templateNode, SkillMaldicion1, new Vector3(localMaldicionX, elemTier1Y, 0f), new Color(0.78f, 0.55f, 1f));
        NewInventory_Skill m2 = CloneSkillNode(layout, containerTrans, templateNode, SkillMaldicion2, new Vector3(localMaldicionX, elemTier2Y, 0f), new Color(0.78f, 0.55f, 1f));
        NewInventory_Skill m3 = CloneSkillNode(layout, containerTrans, templateNode, SkillMaldicion3, new Vector3(localMaldicionX, elemTier3Y, 0f), new Color(0.78f, 0.55f, 1f));

        // Configurar navegación explícita:
        // Maldición (extremo izquierdo) <-> Veneno <-> Rama Izquierda Vanilla (v1 arriba -> down v2 -> down v3)
        WireNodeNavigation(m1, up: null, down: m2, left: null, right: v1);
        WireNodeNavigation(m2, up: m1, down: m3, left: null, right: v2);
        WireNodeNavigation(m3, up: m2, down: null, left: null, right: v3);

        WireNodeNavigation(v1, up: null, down: v2, left: m1, right: leftTier1);
        WireNodeNavigation(v2, up: v1, down: v3, left: m2, right: leftTier2 ?? leftTier1);
        WireNodeNavigation(v3, up: v2, down: null, left: m3, right: leftTier3 ?? leftTier2 ?? leftTier1);

        if (CoopGameConfig.EnableElementalUpgrades)
        {
            ConnectLeftNeighbor(leftTier1, v1);
            ConnectLeftNeighbor(leftTier2, v2);
            ConnectLeftNeighbor(leftTier3, v3);
        }

        // Rama Derecha Vanilla <-> Sangrado <-> Electricidad (extremo derecho)
        WireNodeNavigation(s1, up: null, down: s2, left: rightTier1, right: e1);
        WireNodeNavigation(s2, up: s1, down: s3, left: rightTier2 ?? rightTier1, right: e2);
        WireNodeNavigation(s3, up: s2, down: null, left: rightTier3 ?? rightTier2 ?? rightTier1, right: e3);

        if (CoopGameConfig.EnableElementalUpgrades)
        {
            ConnectRightNeighbor(rightTier1, s1);
            ConnectRightNeighbor(rightTier2, s2);
            ConnectRightNeighbor(rightTier3, s3);
        }

        WireNodeNavigation(e1, up: null, down: e2, left: s1, right: null);
        WireNodeNavigation(e2, up: e1, down: e3, left: s2, right: null);
        WireNodeNavigation(e3, up: e2, down: null, left: s3, right: null);

        SanitizeVanillaNodeNavigation(cachedSkills, v1, v2, v3, s1, s2, s3, e1, e2, e3, m1, m2, m3);
        UpdateElementalTreeTransform(skillsRoot);

        NewInventory_Description desc = LayoutDescriptionField?.GetValue(layout) as NewInventory_Description;
        ApplyDescriptionTextScale(desc);
    }

    // Último layout de habilidades que ha corrido su Update (marca de menú abierto para el editor HUD/Árbol)
    private static NewInventory_LayoutSkill lastSkillLayout = null;
    private static int lastSkillLayoutFrame = -100;

    internal static void MarkSkillLayoutActive(NewInventory_LayoutSkill layout)
    {
        lastSkillLayout = layout;
        lastSkillLayoutFrame = Time.frameCount;
    }

    /// <summary>True si el menú de habilidades está en pantalla (su Update corrió en el último par de frames).</summary>
    internal static bool IsSkillMenuActive => lastSkillLayout != null && Time.frameCount - lastSkillLayoutFrame <= 2;

    // Posición/escala base del panel de descripción, capturadas la primera vez para poder restablecerlas
    private static NewInventory_Description descPanelOwner = null;
    private static Vector3 descPanelBasePos;
    private static Vector3 descPanelBaseScale;

    /// <summary>Aplica DescPanelOffsetX/Y y DescPanelScale sobre el RectTransform del panel de descripción (relativo a su base).</summary>
    internal static void ApplyDescPanelTransform(NewInventory_Description desc)
    {
        if (desc == null) return;
        RectTransform rt = desc.transform as RectTransform;
        if (rt == null) return;

        if (descPanelOwner != desc)
        {
            descPanelOwner = desc;
            descPanelBasePos = rt.anchoredPosition3D;
            descPanelBaseScale = rt.localScale;
        }

        rt.anchoredPosition3D = descPanelBasePos + new Vector3(CoopGameConfig.DescPanelOffsetX, CoopGameConfig.DescPanelOffsetY, 0f);
        rt.localScale = descPanelBaseScale * CoopGameConfig.DescPanelScale;
    }

    /// <summary>Aplica en vivo los parámetros del árbol y de la descripción sobre el menú abierto (si lo hay).</summary>
    internal static void ApplyTreeLayoutLive()
    {
        if (!IsSkillMenuActive) return;
        var layout = lastSkillLayout;

        var skillsRoot = SkillsRootField?.GetValue(layout) as GameObject;
        UpdateElementalTreeTransform(skillsRoot);

        NewInventory_Description desc = LayoutDescriptionField?.GetValue(layout) as NewInventory_Description;
        ApplyDescPanelTransform(desc);
        ApplyDescriptionTextScale(desc);
    }

    private static readonly FieldInfo LayoutDescriptionField = Reflect.Field(typeof(NewInventory_LayoutSkill), "description");
    private static readonly FieldInfo DescCaptionField = Reflect.Field(typeof(NewInventory_Description), "caption");
    private static readonly FieldInfo DescDescriptionField = Reflect.Field(typeof(NewInventory_Description), "description");
    private static readonly FieldInfo DescInstructionsField = Reflect.Field(typeof(NewInventory_Description), "instructions");
    private static readonly FieldInfo DescInstructionsProField = Reflect.Field(typeof(NewInventory_Description), "instructionsPro");
    private static readonly FieldInfo DescScrollViewField = Reflect.Field(typeof(NewInventory_Description), "customScrollView");

    private static int baseDescSize = -1;
    private static int baseCaptionSize = -1;
    private static int baseInstructSize = -1;
    private static float baseProSize = -1f;

    internal static void ApplyDescriptionTextScale(NewInventory_Description desc)
    {
        if (desc == null) return;
        try
        {
            Text capText = DescCaptionField?.GetValue(desc) as Text;
            Text descText = DescDescriptionField?.GetValue(desc) as Text;
            Text instText = DescInstructionsField?.GetValue(desc) as Text;
            Component proComp = DescInstructionsProField?.GetValue(desc) as Component;
            CustomScrollView scrollView = DescScrollViewField?.GetValue(desc) as CustomScrollView;

            if (descText != null && baseDescSize < 0) baseDescSize = descText.fontSize > 0 ? descText.fontSize : 11;
            if (capText != null && baseCaptionSize < 0) baseCaptionSize = capText.fontSize > 0 ? capText.fontSize : 17;
            if (instText != null && baseInstructSize < 0) baseInstructSize = instText.fontSize > 0 ? instText.fontSize : 10;

            float scale = CoopGameConfig.SkillTextScale;

            if (descText != null && baseDescSize > 0)
            {
                descText.fontSize = Mathf.Clamp(Mathf.RoundToInt(baseDescSize * scale), 8, 36);
                descText.horizontalOverflow = HorizontalWrapMode.Wrap;
                descText.verticalOverflow = VerticalWrapMode.Overflow;
            }
            if (capText != null && baseCaptionSize > 0)
            {
                capText.fontSize = Mathf.Clamp(Mathf.RoundToInt(baseCaptionSize * scale), 10, 42);
            }
            if (instText != null && baseInstructSize > 0)
            {
                instText.fontSize = Mathf.Clamp(Mathf.RoundToInt(baseInstructSize * scale), 8, 36);
                instText.horizontalOverflow = HorizontalWrapMode.Wrap;
                instText.verticalOverflow = VerticalWrapMode.Overflow;
            }

            if (proComp != null)
            {
                PropertyInfo prop = proComp.GetType().GetProperty("fontSize");
                if (prop != null)
                {
                    if (baseProSize < 0f)
                    {
                        baseProSize = Convert.ToSingle(prop.GetValue(proComp, null));
                        if (baseProSize <= 0f) baseProSize = 11f;
                    }
                    prop.SetValue(proComp, baseProSize * scale, null);
                }
            }

            scrollView?.NewContentSetted();
        }
        catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "SwordElementalUpgrades.cs:1893"); }
    }

    private static void UpdateNodeLocalPos(Transform container, string nodeName, float x, float y)
    {
        Transform t = container.Find(nodeName);
        if (t != null)
        {
            t.localPosition = new Vector3(x, y, 0f);
        }
    }

    internal static void UpdateElementalTreeTransform(GameObject skillsRoot)
    {
        if (skillsRoot == null) return;
        Transform containerTrans = skillsRoot.transform.Find("ElementalTreeContainer");
        if (containerTrans != null)
        {
            containerTrans.gameObject.SetActive(CoopGameConfig.EnableElementalUpgrades);
            if (!CoopGameConfig.EnableElementalUpgrades) return;

            containerTrans.localPosition = new Vector3(CoopGameConfig.TreeOffsetX, CoopGameConfig.TreeOffsetY, 0f);
            containerTrans.localScale = Vector3.one * CoopGameConfig.TreeScale;

            float halfSpacing = CoopGameConfig.ElementalBranchSpacing * 0.5f;
            float localVenenoX = -halfSpacing;
            float localSangradoX = halfSpacing;
            float localElectricoX = halfSpacing + CoopGameConfig.ElectricoColumnOffset;
            float localMaldicionX = -halfSpacing - CoopGameConfig.MaldicionColumnOffset;

            Transform tV2 = containerTrans.Find("Skill_VENENO_2");
            float centerTier2Y = tV2 != null ? tV2.localPosition.y : 0f;
            float elemTier1Y = centerTier2Y + CoopGameConfig.ElementalNodeVerticalSpacing;
            float elemTier3Y = centerTier2Y - CoopGameConfig.ElementalNodeVerticalSpacing;

            UpdateNodeLocalPos(containerTrans, "Skill_VENENO_1", localVenenoX, elemTier1Y);
            UpdateNodeLocalPos(containerTrans, "Skill_VENENO_2", localVenenoX, centerTier2Y);
            UpdateNodeLocalPos(containerTrans, "Skill_VENENO_3", localVenenoX, elemTier3Y);

            UpdateNodeLocalPos(containerTrans, "Skill_SANGRADO_1", localSangradoX, elemTier1Y);
            UpdateNodeLocalPos(containerTrans, "Skill_SANGRADO_2", localSangradoX, centerTier2Y);
            UpdateNodeLocalPos(containerTrans, "Skill_SANGRADO_3", localSangradoX, elemTier3Y);

            UpdateNodeLocalPos(containerTrans, "Skill_ELECTRICO_1", localElectricoX, elemTier1Y);
            UpdateNodeLocalPos(containerTrans, "Skill_ELECTRICO_2", localElectricoX, centerTier2Y);
            UpdateNodeLocalPos(containerTrans, "Skill_ELECTRICO_3", localElectricoX, elemTier3Y);

            UpdateNodeLocalPos(containerTrans, "Skill_MALDICION_1", localMaldicionX, elemTier1Y);
            UpdateNodeLocalPos(containerTrans, "Skill_MALDICION_2", localMaldicionX, centerTier2Y);
            UpdateNodeLocalPos(containerTrans, "Skill_MALDICION_3", localMaldicionX, elemTier3Y);

            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info($"[ElementalTree] Posicionado: Offset=({CoopGameConfig.TreeOffsetX:F1}, {CoopGameConfig.TreeOffsetY:F1}) | Escala={CoopGameConfig.TreeScale:F2} | SepH={CoopGameConfig.ElementalBranchSpacing:F0} | SepV={CoopGameConfig.ElementalNodeVerticalSpacing:F0} | Tier2Y={centerTier2Y:F1}", Main.CoopLocal);
            }

            Transform lblVeneno = containerTrans.Find("Lbl_Veneno");
            Transform lblSangrado = containerTrans.Find("Lbl_Sangrado");
            if (lblVeneno != null) UnityEngine.Object.Destroy(lblVeneno.gameObject);
            if (lblSangrado != null) UnityEngine.Object.Destroy(lblSangrado.gameObject);
        }
    }

    private static NewInventory_Skill CloneSkillNode(
        NewInventory_LayoutSkill layout,
        Transform parent,
        NewInventory_Skill template,
        string skillId,
        Vector3 localPos,
        Color frameTint)
    {
        var cachedSkills = CachedSkillsField.GetValue(layout) as List<NewInventory_Skill>;
        int index = cachedSkills.Count;

        GameObject go = UnityEngine.Object.Instantiate(template.gameObject, parent);
        go.name = "Skill_" + skillId;
        go.transform.localScale = Vector3.one;
        go.transform.localPosition = localPos;

        NewInventory_Skill skillComp = go.GetComponent<NewInventory_Skill>();
        SkillIdField.SetValue(skillComp, skillId);

        // Teñir ligeramente los marcos para distinguir visualmente Veneno vs Sangrado
        foreach (Image img in go.GetComponentsInChildren<Image>(true))
        {
            if (img.gameObject.name.IndexOf("focus", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (img.gameObject.name.IndexOf("skillImage", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            img.color = new Color(
                Mathf.Clamp01(img.color.r * frameTint.r),
                Mathf.Clamp01(img.color.g * frameTint.g),
                Mathf.Clamp01(img.color.b * frameTint.b),
                img.color.a);
        }

        EventsButton eventButton = skillComp.GetEventButton();
        if (eventButton != null)
        {
            int capturedIndex = index;
            eventButton.onClick = new EventsButton.ButtonClickedEvent();
            eventButton.onClick.AddListener(delegate
            {
                ActivateSkillMethod?.Invoke(layout, new object[] { capturedIndex });
            });
            eventButton.onSelected = new EventsButton.ButtonSelectedEvent();
            eventButton.onSelected.AddListener(delegate
            {
                SelectSkillMethod?.Invoke(layout, new object[] { capturedIndex, true });
            });
        }

        cachedSkills.Add(skillComp);
        skillComp.UpdateStatus();
        skillComp.SetFocus(false, true);
        return skillComp;
    }

    private static void WireNodeNavigation(
        NewInventory_Skill node,
        NewInventory_Skill up,
        NewInventory_Skill down,
        NewInventory_Skill left,
        NewInventory_Skill right)
    {
        if (node == null) return;
        EventsButton btn = node.GetEventButton();
        if (btn == null) return;

        Navigation nav = btn.navigation;
        nav.mode = Navigation.Mode.Explicit;
        nav.selectOnUp = up != null ? up.GetEventButton() : null;
        nav.selectOnDown = down != null ? down.GetEventButton() : null;
        nav.selectOnLeft = left != null ? left.GetEventButton() : null;
        nav.selectOnRight = right != null ? right.GetEventButton() : null;
        btn.navigation = nav;
    }

    private static void ConnectLeftNeighbor(NewInventory_Skill vanillaLeftNode, NewInventory_Skill targetElemNode)
    {
        if (vanillaLeftNode == null || targetElemNode == null) return;
        EventsButton btn = vanillaLeftNode.GetEventButton();
        if (btn == null) return;
        Navigation nav = btn.navigation;
        nav.mode = Navigation.Mode.Explicit;
        nav.selectOnLeft = targetElemNode.GetEventButton();
        btn.navigation = nav;
    }

    private static void ConnectRightNeighbor(NewInventory_Skill vanillaRightNode, NewInventory_Skill targetElemNode)
    {
        if (vanillaRightNode == null || targetElemNode == null) return;
        EventsButton btn = vanillaRightNode.GetEventButton();
        if (btn == null) return;
        Navigation nav = btn.navigation;
        nav.mode = Navigation.Mode.Explicit;
        nav.selectOnRight = targetElemNode.GetEventButton();
        btn.navigation = nav;
    }

    private static void DisconnectLeftNeighbor(NewInventory_Skill vanillaLeftNode)
    {
        if (vanillaLeftNode == null) return;
        EventsButton btn = vanillaLeftNode.GetEventButton();
        if (btn == null) return;
        Navigation nav = btn.navigation;
        nav.selectOnLeft = null;
        btn.navigation = nav;
    }

    private static void DisconnectRightNeighbor(NewInventory_Skill vanillaRightNode)
    {
        if (vanillaRightNode == null) return;
        EventsButton btn = vanillaRightNode.GetEventButton();
        if (btn == null) return;
        Navigation nav = btn.navigation;
        nav.selectOnRight = null;
        btn.navigation = nav;
    }

    private static void SanitizeVanillaNodeNavigation(
        List<NewInventory_Skill> cachedSkills,
        params NewInventory_Skill[] elemNodes)
    {
        if (cachedSkills == null) return;
        var elemButtons = new HashSet<Selectable>();
        if (elemNodes != null)
        {
            for (int i = 0; i < elemNodes.Length; i++)
            {
                var node = elemNodes[i];
                if (node != null && node.GetEventButton() != null)
                {
                    elemButtons.Add(node.GetEventButton());
                }
            }
        }

        foreach (var skill in cachedSkills)
        {
            if (skill == null) continue;
            string sid = skill.GetSkillId();
            if (IsElementalSkill(sid)) continue;
            EventsButton btn = skill.GetEventButton();
            if (btn == null) continue;

            Navigation nav = btn.navigation;
            bool changed = false;

            if (sid != "COMBO_1" && sid != "COMBO_2" && sid != "COMBO_3")
            {
                if (nav.selectOnLeft != null && elemButtons.Contains(nav.selectOnLeft))
                {
                    nav.selectOnLeft = null;
                    changed = true;
                }
            }

            if (sid != "LUNGE_1" && sid != "LUNGE_2" && sid != "LUNGE_3")
            {
                if (nav.selectOnRight != null && elemButtons.Contains(nav.selectOnRight))
                {
                    nav.selectOnRight = null;
                    changed = true;
                }
            }

            if (nav.selectOnUp != null && elemButtons.Contains(nav.selectOnUp))
            {
                nav.selectOnUp = null;
                changed = true;
            }

            if (nav.selectOnDown != null && elemButtons.Contains(nav.selectOnDown))
            {
                nav.selectOnDown = null;
                changed = true;
            }

            if (changed)
            {
                btn.navigation = nav;
            }
        }
    }

    private static float navCooldown = 0f;
    private static int lastObservedSkillIndex = -1;
    private static NewInventory_LayoutSkill lastObservedLayout = null;

    internal static void HandleSkillTreeShortcuts(NewInventory_LayoutSkill layout)
    {
        if (layout == null || !CoopGameConfig.EnableElementalUpgrades) return;
        // El editor HUD/Árbol (F7) captura el input mientras está abierto: sin navegación del árbol
        if (HudLayoutDebugEditor.IsOpen) return;
        var cachedSkills = CachedSkillsField?.GetValue(layout) as List<NewInventory_Skill>;
        if (cachedSkills == null || cachedSkills.Count == 0) return;

        float dt = Time.unscaledDeltaTime;

        int currentSelected = (int)SkillTreeUIHelper.CurrentSelectedField.GetValue(layout);
        if (navCooldown > 0f) navCooldown -= dt;

        if (layout != lastObservedLayout)
        {
            lastObservedLayout = layout;
            lastObservedSkillIndex = currentSelected;
        }

        // Si EventSystem (Unity UI) movió la selección en este frame, registramos el índice y salimos
        // inmediatamente para evitar saltos dobles accidentales.
        if (currentSelected != lastObservedSkillIndex)
        {
            lastObservedSkillIndex = currentSelected;
            navCooldown = 0.18f;
            return;
        }

        // =====================================================================
        // NAVEGACIÓN NATURAL: WASD y Gamepad secundario (cuando EventSystem no actúa)
        // =====================================================================
        if (navCooldown <= 0f)
        {
            bool eventSystemActive = EventSystem.current != null;

            bool wantLeft = Input.GetKeyDown(KeyCode.A) || Player2Pad.Left;
            bool wantRight = Input.GetKeyDown(KeyCode.D) || Player2Pad.Right;
            bool wantUp = Input.GetKeyDown(KeyCode.W) || Player2Pad.Up;
            bool wantDown = Input.GetKeyDown(KeyCode.S) || Player2Pad.Down;

            // Si EventSystem no está presente, usar flechas y Rewired como respaldo
            if (!eventSystemActive)
            {
                wantLeft |= Input.GetKeyDown(KeyCode.LeftArrow) || CheckRewiredLeft();
                wantRight |= Input.GetKeyDown(KeyCode.RightArrow) || CheckRewiredRight();
                wantUp |= Input.GetKeyDown(KeyCode.UpArrow) || CheckRewiredUp();
                wantDown |= Input.GetKeyDown(KeyCode.DownArrow) || CheckRewiredDown();
            }

            if (wantLeft || wantRight || wantUp || wantDown)
            {
                if (currentSelected >= 0 && currentSelected < cachedSkills.Count)
                {
                    NewInventory_Skill curSkill = cachedSkills[currentSelected];
                    string curId = curSkill != null ? curSkill.GetSkillId() : "";
                    int targetIdx = -1;

                    // 1. DENTRO DE LA RAMA DE VENENO (extremo izquierdo)
                    if (IsVenenoSkill(curId))
                    {
                        if (wantRight)
                        {
                            // De Veneno hacia la derecha -> vuelve exactamente al nodo combo correspondiente en el árbol vanilla
                            if (curId == SkillVeneno1) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == "COMBO_1");
                            else if (curId == SkillVeneno2) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == "COMBO_2");
                            else if (curId == SkillVeneno3) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == "COMBO_3");
                        }
                        else if (wantLeft)
                        {
                            // De Veneno hacia la izquierda -> entra en la columna Maldición
                            if (curId == SkillVeneno1) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillMaldicion1);
                            else if (curId == SkillVeneno2) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillMaldicion2);
                            else if (curId == SkillVeneno3) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillMaldicion3);
                        }
                        else if (wantUp)
                        {
                            // Nivel 1 arriba, 2 centro, 3 abajo: Up sube hacia Nivel 1
                            if (curId == SkillVeneno3) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillVeneno2);
                            else if (curId == SkillVeneno2) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillVeneno1);
                        }
                        else if (wantDown)
                        {
                            // Down baja hacia Nivel 3
                            if (curId == SkillVeneno1) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillVeneno2);
                            else if (curId == SkillVeneno2) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillVeneno3);
                        }
                    }
                    // 1b. DENTRO DE LA RAMA DE MALDICIÓN (extremo izquierdo, a la izquierda de Veneno)
                    else if (IsMaldicionSkill(curId))
                    {
                        if (wantRight)
                        {
                            if (curId == SkillMaldicion1) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillVeneno1);
                            else if (curId == SkillMaldicion2) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillVeneno2);
                            else if (curId == SkillMaldicion3) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillVeneno3);
                        }
                        else if (wantUp)
                        {
                            if (curId == SkillMaldicion3) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillMaldicion2);
                            else if (curId == SkillMaldicion2) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillMaldicion1);
                        }
                        else if (wantDown)
                        {
                            if (curId == SkillMaldicion1) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillMaldicion2);
                            else if (curId == SkillMaldicion2) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillMaldicion3);
                        }
                    }
                    // 2. DENTRO DE LA RAMA DE SANGRADO (columna media derecha)
                    else if (IsSangradoSkill(curId))
                    {
                        if (wantLeft)
                        {
                            // De Sangrado hacia la izquierda -> vuelve exactamente al nodo lunge correspondiente en el árbol vanilla
                            if (curId == SkillSangrado1) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == "LUNGE_1");
                            else if (curId == SkillSangrado2) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == "LUNGE_2");
                            else if (curId == SkillSangrado3) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == "LUNGE_3");
                        }
                        else if (wantRight)
                        {
                            // De Sangrado hacia la derecha -> entra en la columna Electricidad
                            if (curId == SkillSangrado1) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillElectrico1);
                            else if (curId == SkillSangrado2) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillElectrico2);
                            else if (curId == SkillSangrado3) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillElectrico3);
                        }
                        else if (wantUp)
                        {
                            // Nivel 1 arriba, 2 centro, 3 abajo: Up sube hacia Nivel 1
                            if (curId == SkillSangrado3) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillSangrado2);
                            else if (curId == SkillSangrado2) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillSangrado1);
                        }
                        else if (wantDown)
                        {
                            // Down baja hacia Nivel 3
                            if (curId == SkillSangrado1) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillSangrado2);
                            else if (curId == SkillSangrado2) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillSangrado3);
                        }
                    }
                    // 3. DENTRO DE LA RAMA DE ELECTRICIDAD (extremo derecho)
                    else if (IsElectricoSkill(curId))
                    {
                        if (wantLeft)
                        {
                            if (curId == SkillElectrico1) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillSangrado1);
                            else if (curId == SkillElectrico2) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillSangrado2);
                            else if (curId == SkillElectrico3) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillSangrado3);
                        }
                        else if (wantUp)
                        {
                            if (curId == SkillElectrico3) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillElectrico2);
                            else if (curId == SkillElectrico2) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillElectrico1);
                        }
                        else if (wantDown)
                        {
                            if (curId == SkillElectrico1) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillElectrico2);
                            else if (curId == SkillElectrico2) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillElectrico3);
                        }
                    }
                    // 4. EN EL ÁRBOL VANILLA
                    else
                    {
                        // Acceso a Veneno: ÚNICAMENTE desde Last Words (COMBO_1), Ascending Edge (COMBO_2) y Azure Cyclone (COMBO_3)
                        if (wantLeft)
                        {
                            if (curId == "COMBO_1") targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillVeneno1);
                            else if (curId == "COMBO_2") targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillVeneno2);
                            else if (curId == "COMBO_3") targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillVeneno3);
                        }
                        // Acceso a Sangrado: ÚNICAMENTE desde Sacred Thrust (LUNGE_1), Sacred Lunge (LUNGE_2) y Sacred Onslaught (LUNGE_3)
                        else if (wantRight)
                        {
                            if (curId == "LUNGE_1") targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillSangrado1);
                            else if (curId == "LUNGE_2") targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillSangrado2);
                            else if (curId == "LUNGE_3") targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillSangrado3);
                        }
                    }

                    if (targetIdx >= 0 && targetIdx < cachedSkills.Count && targetIdx != currentSelected)
                    {
                        SelectSkillDirect(layout, cachedSkills, targetIdx);
                        return;
                    }
                }
            }
        }
    }

    private static void SelectSkillDirect(NewInventory_LayoutSkill layout, List<NewInventory_Skill> cachedSkills, int newIdx)
    {
        if (newIdx < 0 || newIdx >= cachedSkills.Count) return;
        int curSelected = (int)SkillTreeUIHelper.CurrentSelectedField.GetValue(layout);
        if (curSelected >= 0 && curSelected < cachedSkills.Count && cachedSkills[curSelected] != null)
        {
            cachedSkills[curSelected].SetFocus(false, true);
        }

        SkillTreeUIHelper.CurrentSelectedField.SetValue(layout, newIdx);
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(cachedSkills[newIdx].gameObject);
        }
        SkillTreeUIHelper.SelectSkillMethod?.Invoke(layout, new object[] { newIdx, true });
        lastObservedSkillIndex = newIdx;
        navCooldown = 0.20f;
    }

    private static bool CheckRewiredLeft()
    {
        if (!Rewired.ReInput.isReady) return false;
        Rewired.Player p = Rewired.ReInput.players.GetPlayer(0);
        return p != null && p.GetAxis(0) < -0.5f;
    }

    private static bool CheckRewiredRight()
    {
        if (!Rewired.ReInput.isReady) return false;
        Rewired.Player p = Rewired.ReInput.players.GetPlayer(0);
        return p != null && p.GetAxis(0) > 0.5f;
    }

    private static bool CheckRewiredUp()
    {
        if (!Rewired.ReInput.isReady) return false;
        Rewired.Player p = Rewired.ReInput.players.GetPlayer(0);
        return p != null && p.GetAxis(1) > 0.5f;
    }

    private static bool CheckRewiredDown()
    {
        if (!Rewired.ReInput.isReady) return false;
        Rewired.Player p = Rewired.ReInput.players.GetPlayer(0);
        return p != null && p.GetAxis(1) < -0.5f;
    }

    private static void CycleSkillGroup(NewInventory_LayoutSkill layout, List<NewInventory_Skill> cachedSkills, int currentSelected, string[] targetGroup)
    {
        string curId = (currentSelected >= 0 && currentSelected < cachedSkills.Count)
            ? cachedSkills[currentSelected].GetSkillId()
            : "";

        int nextGroupIndex = 0;
        for (int i = 0; i < targetGroup.Length; i++)
        {
            if (string.Equals(curId, targetGroup[i], StringComparison.OrdinalIgnoreCase))
            {
                nextGroupIndex = (i + 1) % targetGroup.Length;
                break;
            }
        }

        string targetId = targetGroup[nextGroupIndex];
        for (int i = 0; i < cachedSkills.Count; i++)
        {
            if (cachedSkills[i] != null && string.Equals(cachedSkills[i].GetSkillId(), targetId, StringComparison.OrdinalIgnoreCase))
            {
                if (EventSystem.current != null)
                {
                    EventSystem.current.SetSelectedGameObject(cachedSkills[i].gameObject);
                }
                SkillTreeUIHelper.SelectSkillMethod?.Invoke(layout, new object[] { i, true });
                break;
            }
        }
    }
}
