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
/// - Corazones de Mea Culpa: Corazón de Ponzoña Viva (HE_VENENO) y Corazón de la Hemorragia Sagrada (HE_SANGRADO).
/// - El daño de Veneno o Sangrado solo se aplica cuando el jugador tiene la mejora en su Árbol de Habilidades
///   Y lleva equipado el Corazón de Mea Culpa correspondiente.
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

    private static readonly FieldInfo AllSkillsField = AccessTools.Field(typeof(SkillManager), "allSkills");
    private static readonly FieldInfo AllSwordsField = AccessTools.Field(typeof(InventoryManager), "allSwords");
    private static readonly FieldInfo OwnSwordsField = AccessTools.Field(typeof(InventoryManager), "ownSwords");

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

    internal static void GetPoisonParams(int tier, out float dps, out float duration)
    {
        switch (tier)
        {
            case 1:
                dps = 1.0f;
                duration = 5.0f;
                break;
            case 2:
                dps = 2.5f;
                duration = 7.5f;
                break;
            case 3:
            default:
                dps = 4.0f;
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
                maxHpPercent = 0.04f; // 4% vida máxima
                progressiveDps = 2.5f; // 2.5 daño/s por 5s
                break;
            case 2:
                maxHpPercent = 0.055f; // 5.5% vida máxima
                progressiveDps = 3.5f; // 3.5 daño/s por 5s
                break;
            case 3:
            default:
                maxHpPercent = 0.07f; // 7% vida máxima (tope nerfeado a 7% a pedido del usuario)
                progressiveDps = 4.5f; // 4.5 daño/s por 5s
                break;
        }
    }

    internal static void GetElectricoParams(int tier, out float instantDamage, out float stunHitDamage, out float stunCooldown)
    {
        stunCooldown = 3.0f;
        switch (tier)
        {
            case 1:
                instantDamage = 6f;
                stunHitDamage = 4f;
                break;
            case 2:
                instantDamage = 18f;
                stunHitDamage = 7f;
                break;
            case 3:
            default:
                instantDamage = 30f;
                stunHitDamage = 10f;
                break;
        }
    }

    /// <summary>Umbral eléctrico: golpes acumulados por enemigo antes de congelarlo, y duración del congelamiento (normal / jefe).</summary>
    internal static void GetElectricoFreezeParams(int tier, out int hitThreshold, out float freezeSeconds, out float bossFreezeSeconds)
    {
        hitThreshold = 5;
        freezeSeconds = 2.0f;
        bossFreezeSeconds = 0.5f;
    }

    /// <summary>Maldición: reducción base del daño que el enemigo maldito inflige a jugadores, drenaje (% vida máx. del dueño por golpe) y cooldown del drenaje.</summary>
    internal static void GetMaldicionParams(int tier, out float damageReduction, out float drainPercentMaxLife, out float drainCooldown, out float curseDuration, out int hitThreshold, out float thresholdMultiplier)
    {
        curseDuration = 8.0f;
        drainCooldown = 0.5f;
        hitThreshold = 5;
        thresholdMultiplier = 1.5f;
        switch (tier)
        {
            case 1:
                damageReduction = 0.10f;
                drainPercentMaxLife = 0.04f;
                break;
            case 2:
                damageReduction = 0.20f;
                drainPercentMaxLife = 0.06f;
                break;
            case 3:
            default:
                damageReduction = 0.30f;
                drainPercentMaxLife = 0.08f;
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
        if (maldicionHeartSprite == null)
        {
            maldicionHeartSprite = maldicionSmallBuyedSprite;
        }
        if (venenoHeartSprite == null)
        {
            venenoHeartSprite = venenoSmallBuyedSprite;
        }
        if (sangradoHeartSprite == null)
        {
            sangradoHeartSprite = sangradoSmallBuyedSprite;
        }
        if (electricoHeartSprite == null)
        {
            electricoHeartSprite = electricoSmallBuyedSprite;
        }
    }

    internal static void EnsureSkillsRegistered(SkillManager manager)
    {
        if (manager == null || AllSkillsField == null) return;
        var allSkills = AllSkillsField.GetValue(manager) as Dictionary<string, UnlockableSkill>;
        if (allSkills == null) return;

        EnsureSpritesCreated();

        Sprite baseBig = null;
        if (allSkills.TryGetValue("CHARGED_1", out UnlockableSkill refSkill) && refSkill != null)
        {
            baseBig = refSkill.bigImage;
        }

        RegisterOrUpdateSkill(
            allSkills,
            SkillVeneno1,
            "Ponzoña de Mea Culpa I",
            "Imbuye la espada con toxina: inflige 1 de daño/s por 5s.\nRequiere equipar el Corazón de Ponzoña Viva.",
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
            "Aumenta la potencia tóxica: inflige 2.5 de daño/s por 7.5s.\nRequiere equipar el Corazón de Ponzoña Viva.",
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
            "Ponzoña letal máxima: inflige 4 de daño/s por 10s.\nRequiere equipar el Corazón de Ponzoña Viva.",
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
            "Acumula sangrado en 6 golpes (15+ en jefes): inflige 4% de salud máx. y 2.5 de daño/s por 5s.\nRequiere equipar el Corazón de la Hemorragia Sagrada.",
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
            "Heridas profundas tras el umbral: inflige 5.5% de salud máx. y 3.5 de daño/s por 5s.\nRequiere equipar el Corazón de la Hemorragia Sagrada.",
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
            "Hemorragia devastadora tras el umbral: inflige 7% de salud máx. y 4.5 de daño/s por 5s.\nRequiere equipar el Corazón de la Hemorragia Sagrada.",
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
            "Cada golpe de espada descarga un rayo (+6 daño instantáneo) y 1s después cae un segundo rayo real (4 daño) capaz de aturdir.\nRequiere equipar el Corazón del Relámpago Consagrado.",
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
            "Intensifica la descarga: +18 daño instantáneo y un segundo rayo real a 1s (7 daño) capaz de aturdir.\nRequiere equipar el Corazón del Relámpago Consagrado.",
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
            "Tormenta fulminante máxima: +30 daño instantáneo y un segundo rayo real a 1s (10 daño) capaz de aturdir.\nRequiere equipar el Corazón del Relámpago Consagrado.",
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
            "Cada golpe maldice al enemigo 8s: su daño a los jugadores se reduce un 10%. Tras 5 golpes sobre el mismo enemigo la reducción se multiplica x1.5 mientras dure la maldición. Cada golpe además cura al portador un 4% de su salud máx. (0.5s de enfriamiento).\nRequiere equipar el Corazón de la Maldición Penitente.",
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
            "Maldición más profunda: -20% al daño del enemigo (x1.5 tras 5 golpes) y drenaje del 6% de salud máx. por golpe.\nRequiere equipar el Corazón de la Maldición Penitente.",
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
            "Maldición suprema: -30% al daño del enemigo (x1.5 tras 5 golpes) y drenaje del 8% de salud máx. por golpe.\nRequiere equipar el Corazón de la Maldición Penitente.",
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
            "Canaliza la Senda del Veneno desbloqueada en el Árbol de Habilidades.\n• Veneno I: 1 de daño/s durante 5s.\n• Veneno II: 2.5 de daño/s durante 7.5s.\n• Veneno III: 4 de daño/s durante 10s.\n(El daño de veneno solo se aplica mientras este corazón está equipado y se posee al menos Ponzoña de Mea Culpa I).",
            "Un corazón cristalizado en bilis esmeralda que late al unísono con las espinas del puño de Mea Culpa.",
            venenoHeartSprite);

        RegisterOrUpdateSwordHeart(
            allSwords,
            ownSwords,
            HeartSangradoId,
            "Corazón de la Hemorragia Sagrada",
            "Canaliza la Senda del Sangrado desbloqueada en el Árbol de Habilidades.\nCada impacto acumula sangrado (Umbral: 6 golpes en enemigos normales, 15+ en jefes).\n• Sangrado I: 4% Vida Máx + 2.5 de daño/s por 5s.\n• Sangrado II: 5.5% Vida Máx + 3.5 de daño/s por 5s.\n• Sangrado III: 7% Vida Máx + 4.5 de daño/s por 5s.\n(El sangrado solo se aplica mientras este corazón está equipado y se posee al menos Filo Sangrante I).",
            "Forjado en sangre coagulada del Milagro, desgarra el interior de quienes sufren el castigo reiterado del acero.",
            sangradoHeartSprite);

        RegisterOrUpdateSwordHeart(
            allSwords,
            ownSwords,
            HeartElectricoId,
            "Corazón del Relámpago Consagrado",
            "Canaliza la Senda de la Electricidad desbloqueada en el Árbol de Habilidades.\nCada espadazo descarga un rayo instantáneo y 1s después un segundo rayo real capaz de aturdir (3s de enfriamiento por enemigo).\n• Centella I: +6 daño instantáneo + 4 daño de rayo a 1s.\n• Centella II: +18 daño instantáneo + 7 daño de rayo a 1s.\n• Centella III: +30 daño instantáneo + 10 daño de rayo a 1s.\n(El rayo solo se activa mientras este corazón está equipado y se posee al menos Centella de Mea Culpa I).",
            "Un corazón atravesado por centellas benditas del Milagro que resuena con cada tajo de Mea Culpa.",
            electricoHeartSprite);

        RegisterOrUpdateSwordHeart(
            allSwords,
            ownSwords,
            HeartMaldicionId,
            "Corazón de la Maldición Penitente",
            "Canaliza la Senda de la Maldición desbloqueada en el Árbol de Habilidades.\nCada impacto maldice al enemigo durante 8s (renovable): reduce el daño que inflige a los jugadores.\n• Maldición I: -10% daño enemigo + drenaje 4% de vida máx.\n• Maldición II: -20% daño enemigo + drenaje 6% de vida máx.\n• Maldición III: -30% daño enemigo + drenaje 8% de vida máx.\nTras 5 golpes sobre el mismo enemigo la reducción se multiplica x1.5 mientras dure la maldición. El drenaje tiene 0.5s de enfriamiento.\n(Solo se aplica mientras este corazón está equipado y se posee al menos Maldición de Mea Culpa I).",
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
                modeDescription = "Elemental: VENENO I (1 DPS / 5s) + Corazón Ponzoña";
                break;
            case 2:
                // Modo 2: Veneno III en ambos
                ApplyDebugElemental(SkillVeneno3, HeartVenenoId);
                modeDescription = "Elemental: VENENO III (4 DPS / 10s) + Corazón Ponzoña";
                break;
            case 3:
                // Modo 3: Sangrado I en ambos
                ApplyDebugElemental(SkillSangrado1, HeartSangradoId);
                modeDescription = "Elemental: SANGRADO I (5% Max HP + 3 DPS / 5s) + Corazón Hemorragia";
                break;
            case 4:
                // Modo 4: Sangrado III en ambos
                ApplyDebugElemental(SkillSangrado3, HeartSangradoId);
                modeDescription = "Elemental: SANGRADO III (7% Max HP + 5 DPS / 5s) + Corazón Hemorragia";
                break;
            case 5:
                // Modo 5: Electricidad I en ambos
                ApplyDebugElemental(SkillElectrico1, HeartElectricoId);
                modeDescription = "Elemental: ELECTRICIDAD I (+6 instantáneo / 4 rayo 1s) + Corazón Relámpago";
                break;
            case 6:
                // Modo 6: Electricidad III en ambos
                ApplyDebugElemental(SkillElectrico3, HeartElectricoId);
                modeDescription = "Elemental: ELECTRICIDAD III (+30 instantáneo / 10 rayo 1s) + Corazón Relámpago";
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
    private static readonly FieldInfo SkillsRootField = AccessTools.Field(typeof(NewInventory_LayoutSkill), "skillsRoot");
    private static readonly FieldInfo CachedSkillsField = AccessTools.Field(typeof(NewInventory_LayoutSkill), "cachedSkills");
    private static readonly FieldInfo SkillIdField = AccessTools.Field(typeof(NewInventory_Skill), "skill");
    private static readonly MethodInfo SelectSkillMethod = AccessTools.Method(typeof(NewInventory_LayoutSkill), "SelectSkill");
    private static readonly MethodInfo ActivateSkillMethod = AccessTools.Method(typeof(NewInventory_LayoutSkill), "ActivateSkill");

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

    private static readonly FieldInfo LayoutDescriptionField = AccessTools.Field(typeof(NewInventory_LayoutSkill), "description");
    private static readonly FieldInfo DescCaptionField = AccessTools.Field(typeof(NewInventory_Description), "caption");
    private static readonly FieldInfo DescDescriptionField = AccessTools.Field(typeof(NewInventory_Description), "description");
    private static readonly FieldInfo DescInstructionsField = AccessTools.Field(typeof(NewInventory_Description), "instructions");
    private static readonly FieldInfo DescInstructionsProField = AccessTools.Field(typeof(NewInventory_Description), "instructionsPro");
    private static readonly FieldInfo DescScrollViewField = AccessTools.Field(typeof(NewInventory_Description), "customScrollView");

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
        catch { }
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

// =========================================================================
// Parches de SkillManager e InventoryManager
// =========================================================================
[HarmonyPatch(typeof(SkillManager), "LoadAllSkills")]
internal static class SkillManager_LoadAllSkills_Elemental_Patch
{
    private static void Postfix(SkillManager __instance)
    {
        SwordElementalUpgrades.EnsureSkillsRegistered(__instance);
    }
}

[HarmonyPatch(typeof(SkillManager), "SetCurrentPersistentState")]
internal static class SkillManager_SetCurrentPersistentState_Elemental_Patch
{
    private static void Prefix(SkillManager __instance)
    {
        SwordElementalUpgrades.EnsureSkillsRegistered(__instance);
    }
}

[HarmonyPatch(typeof(SkillManager), "OnLocalizationChange")]
internal static class SkillManager_OnLocalizationChange_Elemental_Patch
{
    private static void Postfix(SkillManager __instance)
    {
        SwordElementalUpgrades.EnsureSkillsRegistered(__instance);
    }
}

[HarmonyPatch(typeof(SkillManager), "CanUnlockSkillNoCheckPoints")]
internal static class SkillManager_CanUnlockSkillNoCheckPoints_Elemental_Patch
{
    private static void Postfix(string skill, ref bool __result)
    {
        // Ambas sendas (Veneno y Sangrado) pueden desbloquearse libremente sin bloquearse entre sí
    }
}

[HarmonyPatch(typeof(SkillManager), "UnlockSkill")]
internal static class SkillManager_UnlockSkill_Elemental_Patch
{
    private static void Postfix(string skill, bool __result)
    {
        if (__result && SwordElementalUpgrades.IsElementalSkill(skill))
        {
            SwordElementalUpgrades.GrantMatchingHeartOnUnlock(skill, isPlayer2: false);
        }
    }
}

[HarmonyPatch(typeof(InventoryManager), "InitializeObjects")]
internal static class InventoryManager_InitializeObjects_Elemental_Patch
{
    private static void Postfix(InventoryManager __instance)
    {
        SwordElementalUpgrades.EnsureSwordsRegistered(__instance);
    }
}

[HarmonyPatch(typeof(InventoryManager), "SetCurrentPersistentState")]
internal static class InventoryManager_SetCurrentPersistentState_Elemental_Patch
{
    private static void Prefix(InventoryManager __instance)
    {
        SwordElementalUpgrades.EnsureSwordsRegistered(__instance);
    }

    private static void Postfix(InventoryManager __instance)
    {
        SwordElementalUpgrades.EnsureSwordsRegistered(__instance);
    }
}

[HarmonyPatch(typeof(InventoryManager), "OnLocalizationChange")]
internal static class InventoryManager_OnLocalizationChange_Elemental_Patch
{
    private static void Postfix(InventoryManager __instance)
    {
        SwordElementalUpgrades.EnsureSwordsRegistered(__instance);
    }
}

// Permitir equipar los Corazones de Mea Culpa (especialmente HE_VENENO y HE_SANGRADO) en cualquier momento desde el menú
[HarmonyPatch(typeof(NewInventory_LayoutGrid), "GetInventoryObjectAction")]
internal static class NewInventory_LayoutGrid_GetInventoryObjectAction_Elemental_Patch
{
    private static readonly MethodInfo IsEquippedMethod = AccessTools.Method(typeof(NewInventory_LayoutGrid), "IsEquipped");

    private static void Postfix(NewInventory_LayoutGrid __instance, BaseInventoryObject obj, ref NewInventoryWidget.EquipAction __result)
    {
        if (obj is Sword && __result == NewInventoryWidget.EquipAction.None)
        {
            bool isEquipped = IsEquippedMethod != null && (bool)IsEquippedMethod.Invoke(__instance, new object[] { obj });
            __result = isEquipped ? NewInventoryWidget.EquipAction.UnEquip : NewInventoryWidget.EquipAction.Equip;
        }
    }
}

[HarmonyPatch(typeof(NewInventory_LayoutGrid), "UpdateItemsBySwordHeartEquipability")]
internal static class NewInventory_LayoutGrid_UpdateItemsBySwordHeartEquipability_Elemental_Patch
{
    private static readonly FieldInfo CachedGridElementsField = AccessTools.Field(typeof(NewInventory_LayoutGrid), "cachedGridElements");

    private static void Postfix(NewInventory_LayoutGrid __instance, NewInventoryWidget.TabType tabType)
    {
        if (tabType != NewInventoryWidget.TabType.Sword) return;
        if (__instance.swordHeartText != null) __instance.swordHeartText.SetActive(false);
        if (__instance.swordHeartTextLocked != null) __instance.swordHeartTextLocked.SetActive(false);

        var elements = CachedGridElementsField?.GetValue(__instance) as List<NewInventory_GridItem>;
        if (elements != null)
        {
            foreach (var elem in elements)
            {
                elem?.DeactivateGrayscale();
            }
        }
    }
}

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

// Parche maestro sobre Weapon.AttackDamageableEntities: cubre TODAS las entidades golpeadas por la espada,
// incluyendo enemigos con escudo o campana (BellCarrier, ShieldMaiden, GhostKnight) cuyos colliders frontales desvían el daño físico
[HarmonyPatch(typeof(Gameplay.GameControllers.Entities.Weapon.Weapon), "AttackDamageableEntities")]
internal static class Weapon_AttackDamageableEntities_ElementalStatus_Patch
{
    private static readonly FieldInfo DamageableEntitiesField = AccessTools.Field(typeof(Gameplay.GameControllers.Entities.Weapon.Weapon), "DamageableEntities");

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

    // Estado de Electricidad (umbral -> congelamiento)
    private int electricHitCount;
    private int lastElectricHitFrame = -1;
    private Penitent lastElectricAttacker;
    private bool electricFrozen;
    private Coroutine electricFreezeRoutine;
    private Animator[] frozenAnimators;
    private float[] frozenAnimatorSpeeds;
    private bool frozeBehaviour;

    // Estado de Maldición
    private float curseRemaining;
    private int curseTier;
    private float curseBaseReduction;
    private int curseHitCount;
    private int curseHitThreshold = 5;
    private float curseThresholdMultiplier = 1.5f;
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

        electricHitCount = 0;
        lastElectricHitFrame = -1;
        lastElectricAttacker = null;
        EndElectricFreeze();

        curseRemaining = 0f;
        curseTier = 0;
        curseBaseReduction = 0f;
        curseHitCount = 0;
        curseEnhanced = false;
        lastCurseHitFrame = -1;
        lastCurseAttacker = null;

        poisonBar?.Hide();
        bleedBar?.Hide();
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
        catch { }
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

        SwordElementalUpgrades.GetPoisonParams(tier, out float dps, out float duration);
        float bossPoisonRes = Combat.CoopEnemyScaling.GetBossPoisonResistance(target);
        dps = Mathf.Max(1f, dps * (1f - bossPoisonRes));

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
                catch { }
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

        // Si el enemigo ya se encuentra con el estado de sangrado progresivo,
        // no se le puede volver a aplicar sangrado hasta que se acabe
        if (bleedProgressiveRemainingTime > 0f) return;

        if (lastBleedAttacker == attacker && lastBleedHitFrame == Time.frameCount)
        {
            return;
        }
        lastBleedHitFrame = Time.frameCount;
        lastBleedAttacker = attacker;

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

            // Escalado de porcentaje de sangrado por umbral: +25% por aplicación (hasta máx +150%)
            bleedBonusPercent = Mathf.Min(150, bleedApplicationsCount * 25);
            float bleedMultiplier = 1.0f + (bleedBonusPercent / 100f);

            SwordElementalUpgrades.GetBleedParams(activeBleedTier, out float maxHpPercent, out float progDps, out float progDuration);

            // Estallido inmediato de % de vida máxima mitigado por resistencia de jefe y escalado por bonus
            float bossBleedRes = Combat.CoopEnemyScaling.GetBossBleedResistance(target);
            float burstDamage = Mathf.Max(1f, maxHp * maxHpPercent * (1f - bossBleedRes) * bleedMultiplier);
            DealDirectStatusDamage(burstDamage, "event:/SFX/Penitent/Damage/PenitentMagicDamage", isPoison: false, isBurst: true);

            // Activar daño progresivo de sangrado (5 segundos de 2.5 / 3.5 / 4.5 DPS * multiplicador de mejora)
            bleedProgressiveDps = progDps * bleedMultiplier;
            bleedProgressiveRemainingTime = progDuration;
            bleedProgressiveMaxDuration = progDuration;
            bleedTickTimer = 1.0f;

            PulseEnemyColor(new Color(1f, 0.2f, 0.25f, 1f), 0.35f);

            // Notificación visual de estallido y mejora de sangrado: Cráneo demoníaco con porcentaje correspondiente (+25%, +50%, etc.)
            try
            {
                Vector3 skullPos = enemy.transform.position + new Vector3(0f, Mathf.Clamp(GetEnemyTopY(), 1.0f, 3.8f) + 0.60f, 0f);
                FloatingUpgradeSkull.Spawn(skullPos, ElementSkill.Bleed, $"+{bleedBonusPercent}%");
            }
            catch { }

            if (attacker != null && attacker.PenitentAttack?.CurrentPenitentWeapon is PenitentSword sword)
            {
                try
                {
                    sword.SpawnBlood(enemy.transform.position, new Hit { DamageType = DamageArea.DamageType.Heavy });
                }
                catch { }
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
    internal void AddElectricHit(Enemy target, Penitent attacker, int tier, int hitWeight = 1)
    {
        if (!CoopGameConfig.EnableElementalUpgrades) return;
        if (Blasphemous.CoopLocal.MultiPlayer.CoopNetworkManager.IsOnlineClient) return;
        if (target == null || target.Status.Dead || Execution_OnUse_Patch.IsEnemyBeingExecuted(target)) return;
        if (electricFrozen) return;

        if (lastElectricAttacker == attacker && lastElectricHitFrame == Time.frameCount) return;
        lastElectricHitFrame = Time.frameCount;
        lastElectricAttacker = attacker;

        enemy = target;
        SwordElementalUpgrades.GetElectricoFreezeParams(tier, out int threshold, out float freezeSeconds, out float bossFreezeSeconds);

        electricHitCount += Mathf.Max(1, hitWeight);
        DashParryDebugLog.LogVerbose($"[Electric] golpe {electricHitCount}/{threshold} en {target.name} (tier {tier})");
        if (electricHitCount < threshold) return;

        electricHitCount = 0;
        bool isBoss = IsBossEnemy(target, GetEnemyMaxHealth());
        float seconds = isBoss ? bossFreezeSeconds : freezeSeconds;

        // No hay una señal fiable de "en mitad de un ataque" común a todos los enemigos: no se bloquea el congelamiento.
        DashParryDebugLog.LogVerbose($"[Electric] detección de ataque en curso no disponible; se congela igualmente a {target.name}");
        DashParryDebugLog.Log($"[Electric] Umbral alcanzado: congelando {target.name} {seconds:0.0}s (boss={isBoss}, atacante={CoopPlayerRegistry.GetPlayerIndex(attacker)})");

        StartElectricFreeze(seconds);
        CoopLightningVfx.Play(target, GetEnemyTopY());

        try
        {
            Vector3 skullPos = target.transform.position + new Vector3(0f, Mathf.Clamp(GetEnemyTopY(), 1.0f, 3.8f) + 0.60f, 0f);
            FloatingUpgradeSkull.Spawn(skullPos, ElementSkill.Electric, $"{seconds:0.#}s");
        }
        catch { }
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
                bool wasRunning = beh.BehaviourTree != null && beh.BehaviourTree.isRunning;
                beh.PauseBehaviour();
                beh.StopMovement();
                frozeBehaviour = wasRunning;
            }
        }
        catch (Exception ex)
        {
            DashParryDebugLog.LogVerbose("[Electric] Error pausando comportamiento: " + ex.Message);
        }

        frozenAnimators = enemy.GetComponentsInChildren<Animator>(true);
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
        float reduction = curseBaseReduction * (curseEnhanced ? curseThresholdMultiplier : 1f);
        return 1f - Mathf.Clamp(reduction, 0f, 0.9f);
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

        if (!wasCursed)
        {
            DashParryDebugLog.LogVerbose($"[Curse] {target.name} maldito (tier {tier}, -{reduction * 100f:0}% daño, {duration:0.#}s) por P{playerIdx + 1}");
        }

        // Umbral: cada N golpes la reducción se multiplica x1.5 mientras dure esta maldición
        curseHitCount += Mathf.Max(1, hitWeight);
        if (curseHitCount >= curseHitThreshold)
        {
            curseHitCount = 0;
            if (!curseEnhanced)
            {
                curseEnhanced = true;
                float effective = Mathf.Min(0.9f, curseBaseReduction * curseThresholdMultiplier);
                DashParryDebugLog.Log($"[Curse] Umbral alcanzado en {target.name}: reducción {curseBaseReduction * 100f:0}% -> {effective * 100f:0}%");
                try
                {
                    Vector3 skullPos = target.transform.position + new Vector3(0f, Mathf.Clamp(GetEnemyTopY(), 1.0f, 3.8f) + 0.60f, 0f);
                    FloatingUpgradeSkull.Spawn(skullPos, ElementSkill.Curse, $"-{Mathf.RoundToInt(effective * 100f)}%");
                }
                catch { }
            }
        }

        PulseEnemyColor(new Color(0.75f, 0.5f, 1f, 1f), 0.15f);

        TryCurseDrain(attacker, playerIdx, drainPct, drainCooldown);
    }

    private static void TryCurseDrain(Penitent owner, int playerIdx, float percentMaxLife, float cooldown)
    {
        if (owner == null || owner.Status == null || owner.Status.Dead) return;
        if (owner.Stats == null || owner.Stats.Life == null) return;

        float now = Time.time;
        if (NextCurseDrainTime.TryGetValue(playerIdx, out float next) && now < next) return;

        float max = owner.Stats.Life.CurrentMax;
        float cur = owner.Stats.Life.Current;
        if (max <= 0f || cur <= 0f || cur >= max) return;

        float amount = max * percentMaxLife;
        float newLife = Mathf.Min(max, cur + amount);
        owner.Stats.Life.Current = newLife;
        NextCurseDrainTime[playerIdx] = now + cooldown;

        DashParryDebugLog.LogVerbose($"[Curse] Drenaje: P{playerIdx + 1} +{newLife - cur:0.#} vida ({percentMaxLife * 100f:0}% de {max:0})");
        try
        {
            FloatingDamageNumber.Spawn(owner.transform.position + new Vector3(0f, 2.2f, 0f), "+" + Mathf.RoundToInt(newLife - cur), new Color(0.78f, 0.45f, 1f, 1f), isLarge: false);
        }
        catch { }
    }

    private void Update()
    {
        if (!CoopGameConfig.EnableElementalUpgrades)
        {
            ResetAllStatuses();
            return;
        }

        if (enemy == null || enemy.Status.Dead || Execution_OnUse_Patch.IsEnemyBeingExecuted(enemy))
        {
            poisonBar?.Hide();
            bleedBar?.Hide();
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
            }
        }

        // Maldición: expira tras su duración (renovable por golpe)
        if (curseRemaining > 0f)
        {
            curseRemaining -= dt;
            if (curseRemaining <= 0f)
            {
                DashParryDebugLog.LogVerbose($"[Curse] Maldición expirada en {name} (umbral x1.5 era {curseEnhanced})");
                curseRemaining = 0f;
                curseTier = 0;
                curseBaseReduction = 0f;
                curseHitCount = 0;
                curseEnhanced = false;
            }
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
                    MethodInfo deathMethod = AccessTools.Method(injector.GetType(), "Death", Type.EmptyTypes);
                    deathMethod?.Invoke(injector, null);
                }

                var beh = enemy.GetComponentInChildren<Gameplay.GameControllers.Enemies.Framework.IA.EnemyBehaviour>();
                if (beh != null)
                {
                    MethodInfo deathMethod = AccessTools.Method(beh.GetType(), "Death", Type.EmptyTypes);
                    deathMethod?.Invoke(beh, null);
                }
            }
        }
        catch { }
    }

    private void DealDirectStatusDamage(float amount, string audioId, bool isPoison, bool isBurst)
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
                Penitent killer = (isPoison ? (lastPoisonAttacker ?? lastBleedAttacker) : (lastBleedAttacker ?? lastPoisonAttacker)) ?? Core.Logic?.Penitent;

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
                catch { }
            }

            if (!willBeLethal || (!enemy.Status.Dead && enemy.Stats?.Life != null && enemy.Stats.Life.Current > 0f))
            {
                try
                {
                    enemy.Damage(effectiveAmount, string.Empty);
                }
                catch { }

                float lifeAfter = (enemy.Stats?.Life != null) ? enemy.Stats.Life.Current : 0f;

                if (lifeAfter >= lifeBefore && enemy.Stats?.Life != null && lifeBefore > 0f)
                {
                    float newLife = Mathf.Max(0f, lifeBefore - amount);
                    enemy.Stats.Life.Current = newLife;
                    if (newLife <= 0f)
                    {
                        enemy.Status.Dead = true;
                        try { enemy.KillInstanteneously(); } catch { }
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
                try { Core.Audio?.PlayOneShot(audioId); } catch { }
            }

            // 4. Parpadeo de shader (protegido con try-catch para no fallar en enemigos sin material/renderers)
            try
            {
                enemy.GetComponentInChildren<MasterShaderEffects>()?.DamageEffectBlink(0f, 0.12f);
            }
            catch { }

            // 5. Destello de color sobre los sprites del enemigo
            PulseEnemyColor(isPoison ? new Color(0.35f, 1f, 0.45f, 1f) : new Color(1f, 0.25f, 0.3f, 1f), 0.18f);

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
                    FloatingDamageNumber.Spawn(spawnPos, txt, new Color(1f, 0.2f, 0.2f, 1f), isLarge: isBurst);
                }
            }
            catch { }
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

        if (!poisonActive && !bleedActive)
        {
            poisonBar?.Hide();
            bleedBar?.Hide();
            if (barsContainerObj.activeSelf) barsContainerObj.SetActive(false);
            return;
        }

        if (!barsContainerObj.activeSelf) barsContainerObj.SetActive(true);

        float topY = Mathf.Clamp(GetEnemyTopY(), 1.1f, 4.0f);

        if (poisonActive && bleedActive)
        {
            float poisonFill = Mathf.Clamp01(poisonRemainingTime / Mathf.Max(0.1f, poisonMaxDuration));
            poisonBar.SetFill(poisonFill, topY + 0.16f);

            if (bleedProgressiveRemainingTime > 0f)
            {
                float bleedFill = Mathf.Clamp01(bleedProgressiveRemainingTime / Mathf.Max(0.1f, bleedProgressiveMaxDuration));
                bleedBar.SetColor(new Color(1f, 0.22f, 0.25f, 1f));
                bleedBar.SetFill(bleedFill, topY);
            }
            else
            {
                float stackFill = Mathf.Clamp01((float)bleedStacks / Mathf.Max(1, bleedThreshold));
                bleedBar.SetColor(new Color(0.95f, 0.5f, 0.25f, 0.85f));
                bleedBar.SetFill(stackFill, topY);
            }
        }
        else if (poisonActive)
        {
            float poisonFill = Mathf.Clamp01(poisonRemainingTime / Mathf.Max(0.1f, poisonMaxDuration));
            poisonBar.SetFill(poisonFill, topY);
            bleedBar.Hide();
        }
        else // Solo sangrado
        {
            poisonBar.Hide();
            if (bleedProgressiveRemainingTime > 0f)
            {
                float bleedFill = Mathf.Clamp01(bleedProgressiveRemainingTime / Mathf.Max(0.1f, bleedProgressiveMaxDuration));
                bleedBar.SetColor(new Color(1f, 0.22f, 0.25f, 1f));
                bleedBar.SetFill(bleedFill, topY);
            }
            else
            {
                float stackFill = Mathf.Clamp01((float)bleedStacks / Mathf.Max(1, bleedThreshold));
                bleedBar.SetColor(new Color(0.95f, 0.5f, 0.25f, 0.85f));
                bleedBar.SetFill(stackFill, topY);
            }
        }
    }
}

/// <summary>
/// VFX de rayo reutilizable: usa el prefab prefabIntoEnemies del rezo PR15 (PenitentAreaAttack) vía PoolManager.
/// Fallback (prefab no accesible): destello amarillo sobre los sprites del enemigo + sonido existente.
/// </summary>
internal static class CoopLightningVfx
{
    private const string FallbackSound = "event:/SFX/Penitent/Damage/PenitentMagicDamage";
    private static GameObject cachedPrefab;
    private static Vector2 cachedOffset;
    private static string cachedHitSound;
    private static bool resolved;

    private static void Resolve()
    {
        if (resolved) return;
        try
        {
            Prayer pr15 = Core.InventoryManager != null ? Core.InventoryManager.GetPrayer("PR15") : null;
            PenitentAreaAttack area = pr15 != null ? pr15.GetComponent<PenitentAreaAttack>() : null;
            if (area == null) return;

            cachedPrefab = AccessTools.Field(typeof(PenitentAreaAttack), "prefabIntoEnemies")?.GetValue(area) as GameObject;
            object off = AccessTools.Field(typeof(PenitentAreaAttack), "intoEnemiesOffset")?.GetValue(area);
            cachedOffset = off is Vector2 v ? v : Vector2.zero;
            cachedHitSound = AccessTools.Field(typeof(PenitentAreaAttack), "HitSound")?.GetValue(area) as string;
            resolved = true;
            DashParryDebugLog.LogVerbose($"[Electric] VFX PR15 resuelto: prefab={(cachedPrefab != null ? cachedPrefab.name : "null")} sonido={cachedHitSound}");
        }
        catch (Exception ex)
        {
            DashParryDebugLog.LogVerbose("[Electric] Error resolviendo VFX PR15: " + ex.Message);
        }
    }

    internal static void Play(Enemy target, float topY)
    {
        if (target == null) return;
        Resolve();

        bool spawned = false;
        try
        {
            if (cachedPrefab != null && PoolManager.Instance != null)
            {
                Vector3 pos = target.transform.position + new Vector3(0f, Mathf.Clamp(topY * 0.5f, 0.3f, 2f), 0f) + (Vector3)cachedOffset;
                PoolManager.Instance.ReuseObject(cachedPrefab, pos, Quaternion.identity, createPoolIfNeeded: true, poolSize: 2);
                spawned = true;
            }
        }
        catch (Exception ex)
        {
            DashParryDebugLog.LogVerbose("[Electric] Error instanciando rayo PR15: " + ex.Message);
        }

        try
        {
            string snd = !string.IsNullOrEmpty(cachedHitSound) ? cachedHitSound : FallbackSound;
            Core.Audio?.PlayOneShot(snd);
        }
        catch { }

        if (!spawned)
        {
            // Fallback: el destello amarillo lo aplica el propio controlador (color de congelación) — aquí se tiñe el sprite principal
            try
            {
                SpriteRenderer sr = target.SpriteRenderer;
                if (sr != null) sr.color = new Color(1f, 0.95f, 0.35f, 1f);
            }
            catch { }
        }
    }
}

internal sealed class FloatingDamageNumber : MonoBehaviour
{
    private TextMesh textMesh;
    private Color baseColor;
    private float lifetime;
    private float age;
    private float floatSpeed;

    public static void Spawn(Vector3 worldPos, string text, Color color, bool isLarge = false)
    {
        try
        {
            GameObject go = new GameObject("Coop_FloatingDamage");
            go.transform.position = worldPos + new Vector3(UnityEngine.Random.Range(-0.25f, 0.25f), UnityEngine.Random.Range(-0.05f, 0.1f), 0f);

            TextMesh tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.fontSize = isLarge ? 36 : 28;
            tm.characterSize = isLarge ? 0.13f : 0.10f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;

            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sortingOrder = 30005;
            }

            FloatingDamageNumber comp = go.AddComponent<FloatingDamageNumber>();
            comp.textMesh = tm;
            comp.baseColor = color;
            comp.lifetime = isLarge ? 1.1f : 0.85f;
            comp.floatSpeed = isLarge ? 1.2f : 0.95f;
        }
        catch (Exception ex)
        {
            Blasphemous.ModdingAPI.ModLog.Error("Error spawning FloatingDamageNumber: " + ex, Main.CoopLocal);
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        age += dt;
        transform.position += Vector3.up * (floatSpeed * dt);

        float progress = age / lifetime;
        if (progress >= 1f)
        {
            Destroy(gameObject);
            return;
        }

        float alpha = progress < 0.4f ? 1f : Mathf.Lerp(1f, 0f, (progress - 0.4f) / 0.6f);
        if (textMesh != null)
        {
            textMesh.color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
        }
    }
}

/// <summary>Elemento de Mea Culpa asociado a una calavera flotante de mejora.</summary>
internal enum ElementSkill { Poison, Bleed, Electric, Curse }

internal sealed class FloatingUpgradeSkull : MonoBehaviour
{
    private SpriteRenderer bgRenderer;
    private SpriteRenderer skullRenderer;
    private TextMesh textMesh;
    private TextMesh shadowTextMesh;
    private Color bgColor;
    private Color skullColor;
    private Color textColor;
    private float lifetime = 3.0f;
    private float age;
    private float floatSpeed = 0.45f;
    private Vector3 initialScale;

    private static Sprite _poisonBackdropSprite;
    private static Sprite _bleedBackdropSprite;

    private static readonly Dictionary<ElementSkill, Sprite> _skullSprites = new Dictionary<ElementSkill, Sprite>();

    public static Sprite GetSkullSprite(ElementSkill element)
    {
        if (!_skullSprites.TryGetValue(element, out Sprite sp) || sp == null)
        {
            sp = LoadSkullSprite(element);
            _skullSprites[element] = sp;
        }
        return sp;
    }

    public static Sprite GetBackdropSprite(bool isPoison)
    {
        if (isPoison)
        {
            if (_poisonBackdropSprite == null) _poisonBackdropSprite = GenerateBackdropSprite(true);
            return _poisonBackdropSprite;
        }
        else
        {
            if (_bleedBackdropSprite == null) _bleedBackdropSprite = GenerateBackdropSprite(false);
            return _bleedBackdropSprite;
        }
    }

    private static Sprite GenerateBackdropSprite(bool isPoison)
    {
        int w = 48, h = 24;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point
        };

        Color[] pixels = new Color[w * h];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;

        Color cFill = isPoison ? new Color(0.04f, 0.08f, 0.05f, 0.88f) : new Color(0.08f, 0.04f, 0.05f, 0.88f);
        Color cBorder = isPoison ? new Color(0.25f, 0.85f, 0.35f, 0.80f) : new Color(0.95f, 0.22f, 0.22f, 0.80f);
        Color cOuterBlack = new Color(0f, 0f, 0f, 0.95f);
        Color cInnerGlow = isPoison ? new Color(0.12f, 0.35f, 0.16f, 0.30f) : new Color(0.40f, 0.10f, 0.12f, 0.30f);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float dy = y - 11.5f;
                float dx = 0f;
                if (x < 12) dx = x - 12f;
                else if (x > 35) dx = x - 35f;

                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d <= 11.2f)
                {
                    if (d > 10.0f)
                    {
                        pixels[y * w + x] = cOuterBlack;
                    }
                    else if (d > 8.8f)
                    {
                        pixels[y * w + x] = cBorder;
                    }
                    else
                    {
                        if (x < 22)
                        {
                            pixels[y * w + x] = Color.Lerp(cInnerGlow, cFill, (float)x / 22f);
                        }
                        else
                        {
                            pixels[y * w + x] = cFill;
                        }
                    }
                }
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 32f);
    }

    // Uploaded skull images encoded directly in Base64 for 100% reliable standalone loading
    private static readonly string PoisonSkullBase64 = 
        "iVBORw0KGgoAAAANSUhEUgAAAGAAAABgCAYAAADimHc4AAA5rUlEQVR42u29d5ScV3k//nnufcv0sjPbu7RaSauulWW5yrjbgA22RSe0kJAEvkAg4ZuEfI1y"
        + "IL8QSCMQigMmhGKwTXHBNm6SiyzLlmW11RZtL7N1+sxb772/PyQTwxc3LNsi37znzDm7Z96Zue/z3Kc/z+cCr/5FALSTf0fiTfrfpTtCTqLFGAZw9gt8DvF2"
        + "JGLL0YX/Jhd/jX5XAkjGGow7GPF3ME348UaW1nXzUj3MX+f7qqp8dfzk+uSzGRCII+hImFqAXVO7ip9Zu0HryA+JY7gZBhZ+ee9L2Qz0WjKAvco7nwFIkI7P"
        + "GDF+UEmcpxQ8qyS1zIDrhaNGc0tn7I26gSsACCiIk0wgAAoAChPIORMYFQ6CDNpXvSKdC4JCH9yT99D/SMDz7DYWpF8EQvrvBUJaPN0S9kJhjWXnbfie4ozB"
        + "VxLKV8LxXTGm/hp5ANVnbZZnCMylrR4XHqRw6cOhGu1y4kr3qmoAgPu7xAR6FRktwHFxNGXeSyCnti3EknUhVi24WJqrQAFULXiwK56K1Bg8EOKwqv5BtyqO"
        + "WlnxpwDmfvk9/7V2ZcZwWbjG/HSqOXju4nT1vtyYex2A0klmqd+CFuq/owRwAFILs+tiieBFUirhO0Izg5rwPQmNk0o3hSlWGwAxwHOk9BwpYjVGkxnW1kkl"
        + "z/eq6hCAqV/51nYExDwGrLwYT7cH3x2vNZcvzVuHlIfDJw29Ot0lgL2qlteXGdvymGForFL01PCRJWOsL6ePDxT0qaECjABDy4ooYukASQkmBbza5rDTuiq+"
        + "NdqoPwITF6MFwV+uexw2gNXJdmOnGeIiP2uDfOSwHdqOH0ERQb3IZ3zNjDF/Fb0eUj6OeJ6fkUpdRZy4W/VvkFL+PWe04LpiSyxpqlLOZUsZi5J1JkpLLgUi"
        + "GoskNV8zGZNSXevMizkI7AcQAcM18Sb9e03d0TWLU1VamrJzehA3+Mcw1XczACB00ia8GAao14IZ2qvIbAVASgdfs7nnMIWosPGlE9xRN7OAeku14qWTDYaI"
        + "piNUzvoUSRoqkuJYnLJ4IMS8llWhaGHSbQagtCh+v2lV+J/qOwJy/HAF+VmnaEbY+8sZMa0l8JerXhe6pLwk02OP2e+Ch0MniSqfZ214LWwAe8UlrAvms36H"
        + "ASBZxY2+hS8BMAGAh3Eh11kkGGMikubwPQCcUNcZQDZjkR4Arbs4xmO1GphJlwJIpdqNP19/SVR4vifnj9sf1zTVY5WFbDlb77tuZ/pz5707fkHvdaG1deu1"
        + "jwJQvb3gL1Hi6dWQCO2V9HqCDfzNKNOYBf9JADoAD4ARrNM2w4eysv7jPIxLGGM/jtboemO3qRbHfFbTqqFpVRicAfNTXAlbqeP7ytrx/cVRpvNPgPlvXHde"
        + "orH7TNM6ticXhI85axGZxi3aWy98XzIeSNJ/HNiX/a6hybnmbfrS/H4f+/fDAwDsAMfNkM9yaZ9LMl4VqdBeQZ3PLE08EJbskmArb7Ymxc9C9dpfBIP6exmn"
        + "bt+TUIRPKaHepxss0rYu4gWSxNesMFQ0pcMtERxHYm7YwsAjZSalsv2yuhrA4XCj9unaZQYCUUabL0/I4uLS1xZG/K2OLe9mXO78xnWLQ/+1lBN0DzfiEp9B"
        + "ODfjASLg/0iwv2EnGKHUr7i3z2cbTjkz+Cuq3oqoajG2EgoamVowHjN/yBil7LLvKgmlm/wyKVW6aXlINK8OsuVnMqw+j1GikSi/5GFhVGKyryrsstC4jr1e"
        + "VX1+xw7wI8eQ8H35ukBQ19rXBdTKs8Kh+m5jW2VJXb37xgKUg70AbAC4XoHt7gPTM2x7JMW+G2kiq5JRfbt3wgYQAFAPoPCsQO/54qRTro7oZXxO/Uai7wA9"
        + "S8RP7KwoUsma4Iiu8aBVdnXXFlBS+UpBpppN1rIqzFJtBs58i4ZgTKKUU7j7n0sqNyVgV4QoZn3Ds9V1wTbxQOEwSgB8aPjw+jfE/jUQYSIcY6WuswJBXeP8"
        + "0INlbarPOmJnZaaQ86atjPoIgDIAhFvZR5Mt/J9rmsxhw2AT1bzsAVex47sqf+tV8FmlwH4t96Seg07qtZQAep4FKPT9ynscAMwEe2c4GrhO+YpbVf8eMMwy"
        + "Ru1SKNW8KsR1ncN1JJjOsWwdw/Enpep/xEE4wSAEUMn7XJTEZ0Ot/I8ijfzLiuRDZ2/EXQefchYmDtn/NrbH+vTIkeo7u7YE4q2rg248HmxafX5kuRaljbYj"
        + "36oA5VfVPq+oJotTcleonl/qu2gszHo/i9XpXYlO7eKFfu9mIiz+WsrjxWxUOh1UEAegYu36OwIJ7ZNOQdz+rFQAExV1WAthixQo2RX3r4JB7a1CqJRSQLol"
        + "hGiDhkrBRaWgqGdbALE0AxkcnqOgBKRjSe44sqpDfk4yFVOEg0NPYV5UsQ8ehpLd/LOpVuPC6WOObF2j87ouLowwyUjEFFqA1VZK/hWuJw4IC08CGMyP+Tct"
        + "DXtfKs+JW+eOuT9vXGu8t2Gtee3sUfeHSqG8c+dvVEeviBpip1KVCQsIRrXfi7ZoHwcA9EAHoMJ1+krfk1Sx3S+E4+atUmKVlEpqOlEwBjJDJ9zOZZu5itcz"
        + "le4Ezni9idbVAeghxuuWBbxYrfaJ4gI+mTusrvcL2JhYzm5lEXww3s5vSrXoHw2FGPkumBQKyUbG7DwxIaAZQebE63XhLiF2cq0GERaIUGAnnr7RLih9y+tj"
        + "nake7Z+IwHbseE4Cn3JDrL0Me/Fs/SgAwIyxN0lf5ryqXMAOcMxDApDE8AcSeNjgfCVjlC4VPauxK2QGwhyFRRfRBg1KKaw80wS4RJIlqYAiZo47sHK+ijeZ"
        + "LBTj/sK4/Wmr6n04lNASwTBDTZO6RjMAXSe/klcs3cmRbCPVt8tHeQloXMFU20qDN3aaYITPHbur0g+F/Yqg79gBefPN6Oy6IPQP570rodU0Kae+W79uqc//"
        + "i1tuwdjJzSlf6cQle4mR7HMZIQUAmkHn2rb4kJ2V/4GbIbAbUQBpCflzxthbOaftRCSIk0Yg1bgyiFSHjlhaI0iF4oIPTWNUZ9SjvS2ErrM4jDCnwqwD4StK"
        + "t5peutlIBEPMl75yIzHuBsPcs6uKtaxheNMnosrQGMpFAFxBSoW6ZToLx7nsvTLZllqp3QSCef31EDffDAET58Xr9fWj+xy9b5dnNneHNC3J3qIU9Od4fnWq"
        + "mcFPpQoy4/w9TMeD1pKYM5L09XiT8Tkzxj9u5/0vEUEPBPXf0zTuQ0E5tmD1nQFq7QkhlGRoWKbR1ksNKuUEPOEiFtMxOFRFrNGEGeDkViUlmjXmlJWQPiiS"
        + "1lmkVuehRICF4sCOP4shENJpZpTQ0MEw2ecjPy8ApSjRwIg8rmaG7EhuwrvzoYcwDcCAwOHMcWd09AnriZlBJ9l9VqRBQm5dOu59mwjFF+manhaBGAfgO2Vx"
        + "Yyim3dK4JjSz6pxwU02DicO7Spi2xbcrc2KLUt7aQFhdDQbYFYG+3UXpVJVKtemoadXoyQcV8hmBwsI81p6rY8NWEwICTiWkxgd9ZOcV1XVz5loCBKY6u4PQ"
        + "AgrxtI5qScfIgEBPLyNNgzq0C3CqwNyEr5qW65gZsWUx6xvhBnZbZVauBbB0xp+H1zzx95UbAaCSEd/MDDlT0bg2DGBGShDRC0bIp40ESADkleVeKaUdivHL"
        + "mlcGdM+R1sKIr5cWvf4N/yx/UJ2WtxfG/Unfk2NQSEiBVC7jyGpeUDRlYG7IJxDAGeHAva7yHUldmxk625NorY9RIOJh09ak2rqljjaub6ING5NwtBIGn5Zq"
        + "bkrR6k0a6psYnvyFi8KSRCRFaFmpgWkMY0/bNDviSK5TTJFcIQBv9ilvY+Ma4wupldrO0pKorV+hb1mY9J4qTPo/vOMOaJnMS64xv+YVsWcM1+/FWrW/SNQE"
        + "l1dKbnZpxLnwTV+Orc1nXH3X5+zvAQAP0T5NpzOUIjfdYnIzxCkQ5WhZHVRGhGCVfeQyPpJNHFf+YQCbV3eQ5/iYWFpUqVQIOiPKVctQpFDKk+JcUDh6Im54"
        + "+KeWijUoqm3iKC4xtTQhUS0A1bKP4qKnZo/bWnHeR+vaIJZvjoBphImBCo49VqwuDvrXiiLufhExz2mZC5Inw/vvWEXR07Gefap0VB4B0OcvhMce+rtiFcAa"
        + "LYzPhGJ6L+fc933BFydtMB0q3WJS/yMetW8MI5Tiqrad4FiSMuMC812TOPiEj0hMkJF01cKYxPgxQrKWo2stI6UYhCB4jkKiSaf6VgkpGSpZHwP7LEwPWOAc"
        + "iNcZrKk75J19bVBFagA9rJCd86m9J0wzx2x7rujvja9Cr1PBcnsKP4IEndym6ncjGbcDHm6GZoRZ07KzTEwerm4JxNhb79iZuRnAXyY7jU/XtoSCkaQunKpk"
        + "2WkHRIBd8ZVVkKq+I0CTRyowYwzVog/PkVi2MY7FKQNL44Q1VwUxOwLqf4oBTKGUF7DKUq3eysFAVC0rRBIKi7MErinE6kC5jK/8qkTVlcjP+mqxxmGeI5Du"
        + "NFDbwrDxfE7DhxTCCb0GQAoKMeJogAIDQXtWoV+dLgx4rsVwugVCAWs71gff2rFB959u5vFSTnwvWMs/07kpsqptTRjxeubm53xezWsqlGA01Qf4rkS17EMP"
        + "MsWqjGb6qyBGcB2ljAhocgDKLjA8dp9PytdgFQWsssCKrQZcW5BdBCIJhVgNQ02TrvJZB0MHBIIhjqaVJjgHGSFSqRYDC+MushkXviKQ0ike19XKzWC3/Ys9"
        + "CaBcHMCDCniwZp12t10Vleqwet8z+aQXiJBfsqRoL6O69ZvfUIAWYVe3rg4ZsbTmXvHJGnHwNpsCprFKM8mbOmJRbppp668I4cAdVWXloVKNBkkhqFLwVSnr"
        + "Id0SABEoO2vDJMC3Ccl2IL9A8B2OzICN/XcsQQEw9ZQ648oARgYlrdrAwDSFSi6AulgEka0ujo+UqOciUo1dQVVe5AABtefqcCwJz/PpnDfp0EzC+OMW5cZF"
        + "qx5GrVvGPIXw+d5rIpdpIeCu/5O/Bw6+ge3QsBv+S6XJq5WOZifdtvrmNeYtHWsjfHHSR3GamJJcTQ86YnBPiS1OOKxhpYF0WwDFeUl20SfhKxQWPCihyLEE"
        + "CU/BKgkIXyrhg0YPOBg/ZJOSREtTDh770RLMEEfDChPJ5gCt2MAoWQdwjUEPMgR1hnozhXgoSt2tKbS3hinZ5UEaPuVmAUYA04FzrzZIM5jae6dD3/rk/Fxx"
        + "Qv7Bqi488uHdAPoxlJv14l1nhtdVq8IqjIpbdnwB1Hfz6VsPUABYby+cuUWqaVoROCtRb0jpQ86NuDR5uEogBd9Vsn6ZqZKNOku1KUTSBqQkOJYECJACqJZ8"
        + "CF8BilDTrJNnK2SGXGSGHEweqWLFtii2vimJ5lUhgBOEAtqWadBMhql+hcd+UaWhiSUaX1iCAwsxLYkViWUo6fNEQVBxXiFVx7D2XB3/8L55yQ2TS19NLQ55"
        + "H19chIOjYCSRtebVzwRXn3I9WS2NiW+tAVhf32lekJmdhbAK8t7svHMRJ95kxpiRatWFHuBiftSCcKU+P+Jxw+RYfkYI8SYg3kDo3hZEPBVQUoAYO2GAk00G"
        + "NXYFMT9qwwwRhCehGYSO9RFMD1rgGkfI4Co3r1CxQLE44cGbbDCulG8TslOMKtJCMKnUinQzHZ2cQrnsKzsPPHlniTQKkqqG6NCurL/6vFjD0pzTUl2SP+vp"
        + "gb6wAGHU4/XlrDgvOyA+BB/TfX2vbSBGL/o+grKy8paJw9XvTA1YXdlpd+VUX1Wzi5ILT94OKQ8uTXnLnTLT9QBH+waOeEpBCE6OBRTmfETTGjo2BRGv07Ew"
        + "YUN4QMOyAMCAxRkHsZQOM8xU/yMFWEVJPWcbWJqRamFGoGUNIyPEyBeEdAvDGWvr6MjoNI6NLamm+iBZGR2sGsYjt2axtrcOnV212HfPLFuYdHKt9e0/On68"
        + "4BhNWKkF2H9a4+oc+Bj4Dcm507wkSXCJIes68mhhzrvMdeRXGdF3/Yr6pAAOc419yAhyVtNqIN3CqFxiOL7XxdgBG7WdJpZtCYM4YJWUiiQ5cQOIpDTUtgTQ"
        + "uSGMRJMOJYHstIvaTg2JeoPGjgikmhmaVjEYQYVwgrB+Y4h4wFHz+QrVpExqajCRnzXQ0ZyGGdQweqxEZ13YymprYyo/b3eMHJ+9CgLf4zri4Wb+v8wkNdkL"
        + "6giA7HPkhk7bmjBBgSGJcgS4Ua/HBBnqoF9ECRFENUbvbl4VDgUjhPb1nCYP+3jiJ2Uk6jla1wZQzPjIDNgIxBg1rTLQvjkITyoM7SmrYs5HNKkjGGEUrdUo"
        + "GNJRLRHqexTVdSloIoB0zEDPRpOCIY5cBmjrMIn5Jvof5UpWAmiqj1OlqKi1PY6aZFgVcy7auuK0OFNtzC84t0sbB0hXwdbe4MeEgRVWRv7g+utBu3c/b3KO"
        + "TrfOOAULruui6uWx5BdRAJCEgxZfqksbl5v1RFxOHXZp2Vkc+YxArNZUvq8w3e9QTZuBNdtNbL04iOU9OhradIRqdQw/WYH0ga1Xh7F6k0nBJKFmpYWGZgNn"
        + "di1HfkTH6CGF0pwG4SsaOaTR4iRh+GmgJhinztY0QixKoZCGZcvSOLRnCXseHKe2tho1v1BSuQX7MlNTN1UXcWekmb3fjNGW/JD4+UMPYepUS4H2Cjd9PTuH"
        + "LgC0RhqM2yNxfUMp54rRpyryde8PsdwMIXMM2HJNEFZBUbXI0LY+hKZlhOWrORZmBfoP+zj33DqEwlVEUhId3TqalxESgRjqzSaq2DYSwaiam3YhXInOliiV"
        + "K1U1/JBCbkKia3ME9WGBUt7D6mQzmRQGr5nFaF9JfeebT2DF6jTOOacbuXmHHdw13xStCbJq1VL5BV/3S/LfARz6taL9a66C6IW6oQEguYo/xAPaE15R5qON"
        + "2v11LeGNwlNuMKwzrnOaGbCQatcwfsDFTL9QHb2MerYTauoYiosMczNAZlyiXBWIhENIxQxMLxSQiIbo0dsdeurRCqRmobupHbNTHslinDY1bqW0Vo9YIIxA"
        + "mMPKES0cF9jQ3YmEmUR9rAmtda2oFG3cffdRzOWySKcilM/ZdP7FK+TsZJEd71/at/5d9eNLo5VPuBX1+6KEuVciHcFfoZ0vA00420iwdyuFRw1N7LVz8OL1"
        + "5uerJV/PzVVhlXzGOSnhS0weqcr8nMPcEsiraGA6Q36e1NSgwmSfi8yAC+kSRWsU1q9P0cSUTY/cUcKBO0ooZgQ2bWmBLVxV662mNektGJucQdiIYXnDCqqW"
        + "XETiOqRDdPxQDts29qCtuRGcM1RLFh567Bj1rGvEBeesxpFD0/T0gUm1ZlMDyy/YvYOPLqhYu/am0rz8GjlYQC80vPgUNb0WDOAAVKiBX24EtTuFp1CdlJ+w"
        + "c6BAkn81nDA2ERGLJk1mhjQUlxw4lmBSgLuWUpteH4UQUNNHJBbGFRaGPQghoRRDYVpAiymqa9FwZm8aWsCHGedoWxPB6D4bKztaqFL1VSYzT6YeRn0yjenZ"
        + "aWQmC9SYqCNP2idzSw4GB6cQCHBUqlVlahrWr+2AX2BUzSo0NMTo0IEJUSza6cKce1+4nbUHErS9Oq1+ggyqUCDsPF2bc7efnFqJsCulVLutGXn5jh3gCGBH"
        + "XVfo/TUthkzU6r4e5AhEdERrTAaFqrDUd5vXBETvVSFKdzBkp13MD1mQroRTkChMu1S3zCCvwvGz700gU1zE8jVRrDrXVJnjNuanXMhqEHXhWsT1FCI8gtGJ"
        + "CRw9Moqv/9uDGBqYR3O6DpwRlnXWQvoS375xF7JLZVqxvJ7aahtx9dXbUC5bmB0v4p1vPwvBgKkkZGtlTH4k1Ezbl73FeDq0iv016JcNZy9m59OrKwEdYBiH"
        + "DCb5+dxg51lL4nt9fagEkvyP29dENjIGUw8yLZLUVDnrKykVWSW/Klw1dPEf1axbvkXH/d8o0MRhm6I1OkJJDSP7y9AMojXnR3Ds4Sq6NmpYzHnoP1LB8F6f"
        + "+u/38I53bMHqlpU4vHeODjw1Bm56+OEP9uHrX30Ymdm8uu++IwgEdIqFNeTzFs4/fw2EJ/DE4yPq3PNWYHAgQ9/+1i78/OcH8dRTk5gazdMVV66nseHFtqxt"
        + "tS07J7Bs9dnBpFHDLlyc8yyZxyPYDg3jz6uOXpS9OLVe0EkfuTTv/SxeH/hE/TrzsHBpPFFnbMvOOJgdqf6rsKXfdWb8I8lGnZYmICNJI17w3HdaeSG5yWBG"
        + "GBgjcAOUatYAhNGyMojJww5K8z6KOQbHB6pLCqkOhTe9bwVSwZT6/o17aWxiTqXTEex+6Bg9sW8MgYCG1tY4KlVHfe1rD1IyHkT3ynqEggHEEiG1f/8Estky"
        + "RkaW8PTTE/CFAtNJHTo0TVNzWbVsc6Kpc61oWn9hWNgV11u9LcAyw94np0fcb9NDmFenIDrmr8AQBpMuJq2q/5BusMsDUW15OeePZfqr/9svq89JF0+4nvxo"
        + "w/KQYYSZhCQwjYnpAYfVdOjo3GgoI6Ih3RqkVIuBaNLAzICN+REPxICG7gCExdTR+yzaeEYDGuIp/PyHIzQ8Moep6Tw98vBxmpnOK6YTfE+iUvEQDOqsvj6C"
        + "YslBqWjj4IFx3PXzQ7R2bTNd9eYtZFU8+L7EwmIJPEoINDBi9T5aXkeya4vpjx32mOcQW71Fk4yz2ELWO8daULsgkHseNUN4EV102ivUms5hY3d+zOvOw0uf"
        + "jAFyALgRoy9KoUKA8gJRxjLHfegmY8Ij9fC3igglNDR1hSmW0lDNSxTnBNo2hBFM2CCuYIY1jDzsIWgElL7UQAeOljE0NIehY7MKBDIaGDSdYOckSCfYjo+Z"
        + "mZJKxgNUVxtBJlNAKGSiqbkGb75mGx56aBB7HhvGpRevUYMTs7A0j6LtDBuvDKOtW6dwDFr3GQZCEbBYHfiGC0xndkKe/cRg8UPKwZ8/R42AXqtA7BmOCzPN"
        + "/4OUqrWX5OufHYwJKKzojSGSMFQh68AIMnhVqXxfULo5gnSbeSKretxFccGHU5WqkhXo2KKTFiQcvbOKACIkKz5U2cDS7Dymp5fA04T2MwwVbWYYuMcBaSAI"
        + "KGYQoIBcwUY8EUJDQwKrVjWjoz2FSy/dhO9//3H098+gUqnSuWd3q+HoCK74QBi1zcDSolIjBxyMP+mhqUMvHYlJTB4R2sSj3r/pQdzgnhj2EL9t8eqVYAAD"
        + "IAK1/OsgrGGKjFA930U6jUlH3WYtiNtTzeaGeJ2OqWMVZlsC0lPcqUiEktw3w5wmD9tEDMq1Ac0kVPIuqlmPylkP5YKHhpoEpqYKSiNOR57I4On943C4j+RK"
        + "jgv/LIA937Vhz0nwAJT0CMRPKmoOTE3lsHlzpwqFdXrj1WsxPj6LoaEZGEGObKUK4dn0zc//MeYDffjCF+9TQw9IIK8JVGEcmS19GMAtANYC2AcAuPlld5Of"
        + "UgbwEzufvV832R/YOW+9x9EQTenfZIydXy04XwNQyxTb7DlKVgq+8hypLU5XPwDFokzX/9kuSYfppPm2gh4AlFTIZxxUij4iYQPRqIm5fBWs1kPXeSaefrQf"
        + "paCHYD2DHfapNmGqS97C0feYA77EoISCFiC4ZQkGhnXrWxGPh7ByVQPqGhL46Y/3YWBgBom6IISQeOrpadz8lSdw8/69WHjSAymgLmGyC1/frXbfP/y+Yr5a"
        + "qJbkz+Q1ii83zU4Ov2vwu+Ke52jdfFE1YnYqo189jDXhpP51KLnTq+CwKOJe31PfVFDC9lFrxOl/L03bqrjk+slmkzgnGU4af6yb8EoL7vzSlGsaGgPXCLpJ"
        + "cCo+VUo+lANIl0HXdPS81ccHb6ihzW/QcOlH47jgg3H1tj9N41+/vhmXrV5Bf3/Z9fjwX2+DiACBmA7fUwhpBn3+b6/DG964EWvWN1FtQxTzi3kMDMyAiBAN"
        + "BfDWN52B/KKN/qfn8bY15+Ld12yjAHSam6yyaskX176r94JqWfww2aWiuBki2UBvSjZqtwCIKPXb91hpp5ABvh7VrommdW1u3Os7aZxa29eFPpxqM9iem5b+"
        + "k2kkm1aGVaXg85pGE2aYSSNm9C6MylW+779n7FDpH/IzTmv72rBixChaq6t1F8dhhhiYJtDQDZz39jhycxKNrQxtK4G1nkGtNTXY/3hO3XBDHy47X9H42KwS"
        + "RYlqxYUuNPrYJ16P3q2teHD3ILae04HJ+UVk5ovYs2dYKaXoj/7wYmxY14LH9w3j/PO70HdoFu989zYIBzBNjk29raxxWcp78qwJuffhkS8opf6obg19rajj"
        + "FgDlkwPhz6f7n1MatFMylgT4ADpCNfwjRpgpxuha7MaPADTVNBjp1lUhr3OTHc0M2mpZb8QPhDgbe7pCSoHCMe5UklrYsXzBgMFizmsf3Ffwlm1OMNsSsKse"
        + "tr87rTrPEVTXCjVzjHDgHoVko8K2N2hULQh8+Zujat9dRdhzhD3f36NQAd7xhrMpHgrh8kvWoKu7AXv2DiGVDsGyPJjMQLHq4OIre+jsrd24+KL1WFgo4M3X"
        + "9uLqN25FQ3pYHXh6jFrbU+jpqcWGdcux5/FB+uifXxqcm7vlGiL6MyIqKaXKL7GL5P+i4SmJA5QC7dyJcG23/o41FweT08Nuhy+gonXqroVJ73U1jYG2rjMi"
        + "orknRKRBY7pioSRXlayAESayy0IVZ/y+UFo/zwhoad9TKj/rkPQUchkPZoSBBMfjP7WQ6SeEExocRyC3QHT0UUdNPe2jMOzDm1OgEuGvPnY1dbXWo39gCp0r"
        + "07jxmw+q0fElqm+Kwy4LdHe2olyo4vzt3Ugl4rjppseQSkWx7exu/OhHj+OpJ8doabGMtRtb0dVVh7vuOoxP//UtCAdNv60tFXj6qUkiogfodMmG7uwDRx8K"
        + "rlJnX/T7oZ7lW4OqUPAvXxr2H6tMy8+VLe8dUAjMjzn+kz9d+n5pSaS7esNx11HCtXx9cdKtOEX5/UiN8UHN4BKM4PsSdtmH9EDhoEnBtIHsuEAooYFpwMG7"
        + "cuhdt5yq8xoO7JpBdVZh88Z2+vz/91aan83jy1++T/Vu7aCJiSzu+fkRuuzKdZidLqEmEUVDQxRdy1px3z2H8YmPfweeJ/HUU6OYy5QwPDSB+fklbN7cjtt/"
        + "dgC7dx1H7+ZOHDg4RiPHF8WGzc2mpXvdc7nijXDQBCD/2kfCfeBKQX36r9Wqxm7jomv/JOHE25naf6dV6+XV98okbpx61Prm/IDzFWHh21bZb0626dusrJIA"
        + "UM76mpUX/6kIwWBEW60bTLqWIK8q0d1ZR2HTRLlso3F1gJgiGnioTL09y+h/vfcKrG1ZQZtXdtL733MhXX7ZWjzyyBCefHIUl1+5jnrWtuDuOw6hLh1F+7I6"
        + "HDucwY63nIGZmSUMDc7g2NEJvPe95+IHN+3Du97Zi4X5Mgyd4aMfuwrVqouNGztx6OAQFhZLGB3Jqo0bW/RjfRnML5T/lAJqse0y7YAKUM7OyCexAxy/RcvK"
        + "qfKCBJ3Qbjc+8O3S0Jf+ZC5czSrz2uvTl614Q6BPL6CFCCNEGLtegXlF9beLE+5cw0pDl4JEbafJtCD9k132P1VYsm/wfal5rpRtzUlqbo2rUtHB1BMOzT3G"
        + "1NhuDywXQF00jd2/GAMEx/YLNsAVLn5y25PoH8jgLe/YgqbWOL70j/dgdjYPCWD/vjFs2dyKql3B04cmccM3HoSmA+//wGW47LKNSCYS6lOfugZTUwU01Cdw"
        + "/HgWMzMZLO+qw3XX9qpYLIj9+yeeOnJo5j2Z4cINpYJrRWu1sG7SWgDYPv/beUH8lEbBPopOVt06/oR7/9FH7VJNo9aRatGbBu+z/3XHj5A9ehT67g/D12rp"
        + "S91nh89vXsW9qaMer2s3peeqVDnrX0kM3KmKdoM0WrOqCdm8hemJPLq668grSUpGo9jU2wHXlghHAyCmIKWLfLGKcNhEW0cNTJPjczvvxPj4kgoENIpGg0gm"
        + "wuCc4+jRGSRSIfzirqNYvboOZ2xdhnA4jv6BMaRTYfrHf7oXjzzSj/vvP4b77u3H1FQO7W0x1dPTxH/xi2PvB/BjpcDD3XpLftzbkT3mfwQe5sfHX9Dnp5fD"
        + "AHoJ95UYw3E3r+4c7rO7cllvJY/r//70v4l5tIHRLCQEgi2rgzvWbA/I7JygYkbS8i1hwTRKe7bqtMq+aK5LUCIRxvj4Elqbk1StuCoUDuDSN6wn2/JR1xhD"
        + "MGhg7yPH0XdkBkFDR0NzDATC/b8YwNxsQYVCOqRQSMRDCIUNuK6g6ek8BvomkMtbMA2GSy/bpH7606foK1+5D/fee4Tm5kuYns4jGGKQEqhWfUxN5USpbLOJ"
        + "qcITcqV6GgtQHmTZGVHfhofhl1Oq5K/AIIemFCjUhUuIIeiW1A3Sk58NpLDf68M8MSjlQYt1sg9tvToE0wQygz5cC9TWE5LxBl1mhixWG4tTwNQBJYkRQ11d"
        + "nC6/agMdOTClqmWXNp/Rjvn5grr7jsN4+sAE7r77EGxbkGkY2HX/Maxc3Ygzz1qOzHQewYBJqXSUAIlH9wzhDa9fjeVdKbVnzxht374Mu3YP0eHDU1Qo2ooI"
        + "xDlDsiaA1vYYfF/Csjx17NiCRlA/xDyeAmCgAguAgR1w0fecKekXzIaeegbsAF2/A2rPEZxthmh7dRifZWns44QerxGDtAABhgvPeV/k2q0XhaSmMdb3uIPc"
        + "jA+rIlHXFmC6CWSGLISCJhLxCLV3pJCuj2Gwfw6DR6dxwSU9MAyNxoeXyKp6KJdtOK7A0OAs9R2dhpQSxYJFqZoIUo0hWiznMT1aQDZbgsYlli9Lo7e3nW65"
        + "9SCsqkOP7hmB5brgRFBSkaaTKpc8amqOIhEP+K4ndNuS1/u+/A6ArQBGwt3sT+LL+V3V3fIWCCw+U459qZWxU1+U74PavRvKy+J4ooff4GtqjzuMfV4OR6//"
        + "Y6hdu6DS67R/feOH4+2rl9XJoUGLqlUFuyzhlAVxjZBqCpAeB3TSKWwG1cT4Eh7fexyHDk6RLxRZVYf2Pnwcj+05jrm5ArjGUChYkFLBslyqVF3UJEMoFmyK"
        + "J0PIuXmsXJ7G+GgOW7e2oX9wHpwx7Ns/iSNHZlW5bEMzCFIpRCIG6psicF2hSkUHvid1pjHKLlo7azaza+p79e/mR8UxPaQd1IJ4Bwsq6WVx/29QQ69JUR7v"
        + "ubE90HUN1h3LOTNiVD3gONiPMgQAtvsCQO0C/uZvaVvjan3Lxs010rYEc6REdlqgOCsQCHEEkxo0jVGig5BfcuBmgWy+Qp4nYVkuxseWMDtXUJblkRCKVnQ1"
        + "kG4w6DqjxoYEpFTKcQU1NCQwO1XE0KEFvPGqtfA8BSkEymUXt93eB2aSIoPAdIKCgsYJrZ0JOI4PjRMDETIz5QElEXAccWDVlcG/ueQjCSr54m2Zp51Ra1b+"
        + "cSDJv8Ji/DE/J8dfQApeWQZcfz3Y7t1QXW+mnkBQ3N9Q735r8Bb0owLvlw1au0F/8zdQblHdMT7ibWvYoFYu6yE/HFdsctAjKwfkMx7snEC8zqTslIvChKDs"
        + "vEXZbAVEBMPkCCVMaAaDkiDfF3AdD2vXtmJutohoJECre5ppfHwRk1NZzC7kIXyFutoI2jrT+P73DqBgOfAgFDdOUkkDGGNoaApDSoXsrA3XkeTYIudz+Tqn"
        + "Ir4LYFCP0R8VF0Qw3awj0++Sv4gvszBWkKI5v6CO/YauuRfMiJ4yBjzTMxm6ws7SPG47qywmdu/+lTjjhIjG0VHby7dmjwiratF5sRadMaZYXRepWKOOsSc9"
        + "5KZdmh22UJjy4VR8LMyVoWkMgZAOpQCn6kE3NSIGJX1FpqEjnY5SMGjA9QTq6mJ05MgUJJNKC3AijTA9WYRtCczmCoCplKYDmsEAAhkmp/r2IHEQZWcdGQ4b"
        + "vFiwP+e64u8gcAgnMEuzxXFx8/Q+90kK0euYxnLlKfEdhFiP9OWgLGP8BSBuXh0VlLkD8vi9YuEkQ04s5now7AaSW3D2mdfG7117XvRDwTrtkuP7qlr/ow4/"
        + "+rAn8hlFHZs5MsPAwphNuSkXXGdQpOA7AkRQQoJ0gyvPFqTrDFIpUgqIRoLgnNB7RgeWFstoakngyOFpcE6QUsEIcxKQGB/JqmANP+GaEEFKEKCQqDXBJKlC"
        + "xgXXOJdSimrZ+wARDj4bOpkYsgQcRIT1Jtq0ywtz3kFGaq8/h4Mny67qeYCeXj3Qvuuv/9Xv3dF34gE614c/Ub8sWD+8vzpRnPVGhFT/PrHH+kR51tdmjgov"
        + "O6bU9vcaWHluGIwDhUUblZKvJAAzqJFhcKWkAggwdA0h04Suc5gmBxRQKjp0xrZlFI0FAIDi9YETJU9PIhDREIhrpNR/aQMjwFSizoAeZCCfIVUXFpwTuZb/"
        + "AIBRpWCcrHFLALT596ErBVJMNaaW69JIskZvHoeDy3F5uBP1v5bxVK9Zc+7Onb/qF//oR5BEQCTOOw7enX964Db7qpP99hXTRFd5SXwx2qAFnviJjXSbJppX"
        + "hxB5v47hvVXkMx4FEwFwxjA7VYKUCpwxrFjWgIXFslJS0uxcEeFwAFMTWZxxVhuKOZsAKE1nCMZ0eEIqM8IInKFakmCaQqLeRCDEsTTtKOkQJeMaArpBVslX"
        + "RU/YAK4AcNcvffleaPu/Ac9spr/f8ProubkpD9aYnLj+erCd38ODGEXleUBN6LnS+q8Gci6dLFhopSW/zXXlIcYw+Qdfh4sumM5VGM1Nu28fe6J608ShyuHZ"
        + "AUGjT1lyZH9VhWo0NK8Ow4wy5ApVEANJoVBXF0OyJgJNY/A8Adf1MZPJg3GOkYFFrFzZpIgRsnNVeLaAEhK+EEg0GghENTCDwQwzeEKCmYoUV8ryPAjd54yT"
        + "VDreEOngt4HjRENBLzTshxeowzu6zg/+2fyA4x29o/RNCNy1cycUjqP4G0D/6MXUVF4V5NyTHpKsXW1emGjUz5x6wrlp/x0oIAuBY1AQOOpV1K1ORV7ODVoV"
        + "bmD+8N4Ky025sEo+iAN6kINxgmMLdLbXEWcMlYoD1/UpHDZRLtlYtaoJVcvHmjVNOHRwgpayFfjiBGyN50oYYYZonUZWVZDnSRhxpup7TPhSIhBh1L7VxNKS"
        + "jZp1XHVuC8KyxVutojyACfSbzeyDyQ7928VZ/8DE/c61soKvPo+qeS4bQK8JdPHu3Sfa1QsFN9zUHXhHepV+uS39gu2pJliYfqahy4yyiGerN7aviSAY00CS"
        + "IZY+Ad2Tn3fhOwqKKUgHaGtNUSQSIAUgEglCKanqG+JoX5aG73m049ozcdcvDiKQIphhjRpWBKh9XYgauk0IqVRuzkMwyihayxFIcpSXfEq3G1h2ToC6eg2K"
        + "prmrNG4ujbsZM8YaEq36tyoL8v78Mf8yIoyeTD/L03U+4DfiiIoinlyc8Qbizexdy3tDb2/vDb3L5fK88oT4rlIQf/0XahSED6TbAoGuLVHlOpL0CEPTahPJ"
        + "5hPD1VIQUcSjkaElNDWk0NlZC8f2YJgaKmWbLr18LYVDBhobU9j75BDpjQ61rgxDQKm5UYc8W6p4sw67LFHXZaA4K6AkKFijof+RsqrkFG2+xFSBCPGjuyx3"
        + "/oD3n9FO/mk7Jz5XGhZ/BUIBChx9L9gPdNod4KCgQP4n1ZGFo/73c+P+bdWSmCdOV0iO9F99TN0DwOIhttzOi62MiPQIVDjOVbpdU2ftCFLPRQFqWBVAstlE"
        + "MS8wcGhWKQFqaUmCM4ZSyaLtF3Sjs6MRY2NzqEsncc+9fSAwTB2zUMn5yM/5WL45iGgTR2bQhR5gEK5CQ4eJxhUm6pdxrNpiqL69Pj/+mO1UJsWXQ438bX6F"
        + "HnAL8iF6acAddHqdIbPzBHQwHUPOLqqx4qS4tzgubqIAfVYIdSt8lKSp9lQX/OOLM/aaalamF8ZdPnvc4uMHXeFVFUu16Ljvqzll5wRq20M4fmyBGBiamhLw"
        + "PIGGxhh1dzfiph88jtdfuRkPP9yPCiyEohopBZBGaFltoH1NEE4FtOqMEDrWBhCv06m20UDrao0O7XEwctiTCwO2qRS6XUdEzSS9XUq6QFTV/cAvPZ7fMQac"
        + "TNad9L7Yjh1gfceQY5pyZAD9qKIMBxYUnhJc3UIKCQmVKMz4S7kpUdv/aFU6JUaJWpOmj1UQSwXIDGvITObR0pSC4/iIxQNIJAL00EPHEYsaUJJwYM80XCUA"
        + "xRBNaVStSDJMRpGkhkStjp6tGtU2AY5DNNnvq6lhj4rTLi0MeEowtHe3NcU7ahvcxUp+eVCZvY7j33iqPMjX6hQlBUCdnDyHsrEf1i/RSAiAAQ8FT8lZtypv"
        + "l5b6TDjN14Si+iq3KkS6LcBmhyzoOlfBiI5SwUE4EKD6hjiWd9dhciJHmekcbMdHc2MS69e14c2XbMHMVI5GpnIwTaZmhhyaOFxV0TqOlk6DahsYmjoZSAfN"
        + "jHtq+oiDmlCInLznX33FJly6fRU7fDgjtp+zquHIkekRxujwyaE9dbrB19NL7DPqUsAFz3oMhWfweSzsg41HAVSKM97nzAgjSFITh6tSOOqLriMWfFcS15ma"
        + "my+ACKiUHXiep4gATWfoWV+Pq69ej1KporLzZZy5chmkxSAFlFNVWJry0XdA4ME7PWQXBNpXcLQuM6CFiBRXaGyMcl2XdNe9T1NUD9BZ57SHAkHta1Kq9mec"
        + "i9ONAepFIqsoAFvP+dPQweu+lXgwtYn/LQDt19IY7Jk1BlN0VTSlQYG0xUm7CIZf2EU/IRzFhC9JCgXfF3h41yCIgGg8qBYXimhqiuNo3wxu+sGTdPbWbtzw"
        + "pfeCO4wKSy4xTshN+1iYcNTAk1UMHvLx0I8tpRhR99lBzM9byq1KPLpnAMf68lizuoU99mi/3doZjwH4wEmDzH4XVdAzlyc1eYkUamZhzM8787h79+7/y72T"
        + "LIy3NXaFvxSO63YlJ3Sr5P1Cuvi8EirrO2KLa0tjw7o2HosGMDCQge8DtbURHD08ja4VDbSqpxEDg7M484xWPPJoP2YyOTTWx5CZLSK34GEuUyV4CosZoZ56"
        + "uAC3KsguSNg5EBOkhvrydMEFK/D6K9billv3o1h2eLno7gKw6+ViSPDXGBCwUppQ35p5wv+GM487fi2c5ziBP+clW/QvdG2NdeVmPJWftfvtvLwO22Fjgh73"
        + "XTm4oqv+XZs2t0khIG3bxeTUEm3c3IaAqcF3BWLxAB08MInHHh3A6MgSSBKCug6n4NNnP7WDyNYxMJFRdl7iktXbMDq6iIVpm5bGXZy7ZQW9/z2vQ//AhOro"
        + "TNCdd/Q583OVHZEIbnVd2C93RIm9wsSnF5EnEsROnmxxYtJSA4D6Xv7F+m3afwBAotXoYppCbs7VmcZuAjCvdkEoqQDAsW1v/OiRaZXNlvXly+u1RCyMfK5K"
        + "msagGZwsy0c4GFCVqsTx4UVYVR+jw1nVUhfHiq4ELj57DQoTArxk0ic/dCXFZBxL4576w7ddhJ4VLWhqCSKfq+L2nz2NfL6qTBOHymUsngocUfYKejkvrq1U"
        + "gZQE/XLSZPeJHWWX8TNZVf8OIBRO6nx6sDpRyjn/oiXkOwF0EGGZnqYvRtv5NZOz2e8+8cTomQ880HfVwUPjC4lEmAiQhqHTnbcdxMJ8EUZAo9//4PlIpaNw"
        + "bB+lokO2LXDffUfwre/cB8PW6L3XbgM0C6lIEkGX0xvfuBJtbfW44RsPYGIyj1giIs49f2XEcdChlDolKOraK+xq/jb3SQAoDIhdJ/9faZrUWbc5lB1+qHKn"
        + "FRbJcz4UP9i0QhehBCWdIsfQkxb67yuL6oK8fnh44S/WrG3+95bmlNd3dJrWrm+jfXsnUChUwTcTPvbxi/Hpv/yJ2tTbTvlsGaPDC5gcy+OM3lYIV+Dhh4+g"
        + "NhmkTeuaVa5QoIOHj2NkOIe21hRdfsV6fPd7jwNAPedMnf5wNS9yUvA517YdHOMo2Y7obV0bXF/bZb47WqsHa5p4UyDKg7VtzF++hXv1LQExO+VftDjk3hsM"
        + "ciMaD73Ftj1NCWKNzQnfMDkbH1tSkxNZXHLlelRKNjU2pJAvWBgbXUI0HkE4wLE4X4FtOZiby6NaciF90KFDM7BsgdrauBwZXsDDDw1mpJTDQuCRnTtfvgp6"
        + "rb2g5zPYCuNQAHwrK2+bHrRysXp9nxJq6ODPS3cfvLWyb3LQW9t1Rjhq2UJzLajMuFOOG/LrA8dyNw8OZh71PHmJrrFgKh2G6wo1PraEUFCnD/3JReg/Nov5"
        + "+aJqbU1TQ30Cx/ozKJdsVKseho/PIx4PYnBoDm97+7l0+RXr1NBAhmfmCt7UVP48gH78mc+cGuQUfpoQ//lPsSO4fkXtmet3HpgfcO/zyuqhUAh96TX6+yeO"
        + "ud7eH5QyV3wkGZsb8TeMH/T+hXMakxKHcrnKzePjSwld1zrb2mv0QsGiQsGlVDqI5V1pHB9cxMx0FlbVo6bGJLp7GrFhUxvKFR/9A3MUCgfpdRd14/HHR9XE"
        + "eJYvLBbnCgX789dfD/c3ADed/mdJvshzCdRvuIOwHdr2k6+uy2FWq1hYGBf7Rvc7GTPMjw48IrTctJgBIK65RnEAGmN03HX99xw+PDnme77WvbLelVKo239y"
        + "CJ4nsP3iFQQiFIuWam6pURrT1P5901CKVDhsIJWKYnBoCYMDCyJXqGJqIveV7du3v9ARJ6clA15MhYiel0m74e8++Rq6Cy4AP1FjtPacF1tTKYhND3x98ZPz"
        + "h52rAZRuPnGKk5BSsa6uLrNctu975JHj1WzWCiqpmJTK/9mtBxGNBNCztpEMkyNeE6bFxQpi8YBqbIzBtjzV2pZUM1NZ5XkuiBQkMP7www/5p+MBDqdC9bzo"
        + "do6TEzlKMnVm95bYJrssB+eO2O8j/NI3/2VnQjabVUrhnkLB2jMyMm9ks+WWxqZ4RNM0MdiXUWvWNxNjDKvW1pNpGuS7gjZtacfYyCLOPGcZDfbP0aqeJlqx"
        + "qp72PzFWQ8QWADXw34EBLyelTUoBf/lxOV6tiLd2boksd6X/FrsgdksP1q+dqq0AcCKMAvix6/p7pyYWl4RUZ3POuJTK615VD41rqG+Mkm35kEJSW2cNNbfF"
        + "ISWQrg1T96p6GQzoy8dGFq5yXfFtnMCSZv9dvKCXbDd27gQBmC3N+bctzdiGEaQNZkz7lB5mH5cMJWGpx5/Vq/lMDYIDGJUK92az5cfnF4pmqWivn8vk+dR4"
        + "jqVrY37nilqWy1bR0p6AGdTRu6kds5kijh6co0BAc9K1keDMZD7p+eLnpyISJvxuX4xONDUDQK0RwXksxDoYo0/4FXGBW8LQb0iWsRMSQZ5Sqg7ANgC6afBP"
        + "Luuo27b9oh4RDOrU0BJHPBFEPGLg5u8fwNJSWRkGV2aQaw8+cMyvVN01RBh8uUB+/HfgzHt6IXgc7ADDMZSFi2N+RT0Ghmukj0H1X4i3v+5pyZPPXibCABGO"
        + "+ULdns9V1kyOZ5dzxqFrHNnFMo0cX8LQwKwsly02NrbA+/sz5UrV/eiOHbj/6NHnPaP4d14C6CWmNTgA6GH0kMbudAtyPU6MkNILYP6zZ8Fqggj/2NaS+vjm"
        + "zR1+TToiJ8ayanY2Z07P5PxsrvIeAA8DmMT/Q9dzScFzbR4dBlb/FnkuOnnmMQD8fV06qnpWNanlnbUqHg1kAVxGdFrFT686A+hVqE888zscwAbO6Zuc07cA"
        + "dD1Lyv6fIv5vS2j28htaX52glf6HXy9K6tQrAV3/u06g/7n+53r51/8PYFTApvsB/Q4AAAAASUVORK5CYII=";

    private static readonly string BleedSkullBase64 = 
        "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";

    private static readonly string ElectricSkullBase64 = 
        "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";

    private static readonly string CurseSkullBase64 = 
        "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";

    private static Sprite LoadSkullSprite(ElementSkill element)
    {
        try
        {
            byte[] data = null;
            string fileName;
            switch (element)
            {
                case ElementSkill.Poison: fileName = "skull_poison_96.png"; break;
                case ElementSkill.Electric: fileName = "skull_electric_96.png"; break;
                case ElementSkill.Curse: fileName = "skull_curse_96.png"; break;
                default: fileName = "skull_bleed_96.png"; break;
            }

            // 1. Intentar cargar desde el directorio de plugins / skulls si existe en disco
            try
            {
                string asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string localPath = Path.Combine(asmDir, Path.Combine("skulls", fileName));
                if (File.Exists(localPath))
                {
                    data = File.ReadAllBytes(localPath);
                }
            }
            catch { }

            // 2. Si no está en disco, usar los datos Base64 embebidos de las imágenes oficiales del usuario
            if (data == null || data.Length == 0)
            {
                string base64;
                switch (element)
                {
                    case ElementSkill.Poison: base64 = PoisonSkullBase64; break;
                    case ElementSkill.Electric: base64 = ElectricSkullBase64; break;
                    case ElementSkill.Curse: base64 = CurseSkullBase64; break;
                    default: base64 = BleedSkullBase64; break;
                }
                data = Convert.FromBase64String(base64);
            }

            if (data != null && data.Length > 0)
            {
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                if (ImageConversion.LoadImage(tex, data))
                {
                    return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 96f);
                }
            }
        }
        catch (Exception ex)
        {
            Blasphemous.ModdingAPI.ModLog.Error("Error loading skull sprite: " + ex, Main.CoopLocal);
        }

        switch (element)
        {
            case ElementSkill.Poison: return SwordElementalUpgrades.CreatePoisonBubblesIcon(true);
            case ElementSkill.Electric: return SwordElementalUpgrades.CreateLightningBoltIcon(true);
            case ElementSkill.Curse: return SwordElementalUpgrades.CreateCurseIcon(true);
            default: return SwordElementalUpgrades.CreateBloodSymbolIcon(true);
        }
    }

    public static void Spawn(Vector3 worldPos, ElementSkill element, string text = "+")
    {
        try
        {
            GameObject root = new GameObject("Coop_UpgradeSkull");
            root.transform.position = worldPos;

            // Sprite del Cráneo
            GameObject skullObj = new GameObject("SkullSprite");
            skullObj.transform.SetParent(root.transform, false);
            skullObj.transform.localScale = new Vector3(0.30f, 0.30f, 1f);
            skullObj.transform.localPosition = new Vector3(-0.14f, 0f, 0f);

            SpriteRenderer sr = skullObj.AddComponent<SpriteRenderer>();
            sr.sprite = GetSkullSprite(element);
            sr.sortingOrder = 30016;

            // Texto principal "+" / "+50%" con brillo elemental
            GameObject textObj = new GameObject("PlusText");
            textObj.transform.SetParent(root.transform, false);
            textObj.transform.localPosition = new Vector3(0.02f, 0.01f, 0f);

            TextMesh tm = textObj.AddComponent<TextMesh>();
            tm.text = text;
            tm.fontSize = 28;
            tm.characterSize = 0.05f;
            tm.fontStyle = FontStyle.Bold;
            tm.anchor = TextAnchor.MiddleLeft;
            tm.alignment = TextAlignment.Left;
            Color fontColor;
            switch (element)
            {
                case ElementSkill.Poison: fontColor = new Color(0.35f, 1f, 0.45f, 1f); break;
                case ElementSkill.Electric: fontColor = new Color(0.45f, 0.9f, 1f, 1f); break;
                case ElementSkill.Curse: fontColor = new Color(0.78f, 0.45f, 1f, 1f); break;
                default: fontColor = new Color(1f, 0.28f, 0.30f, 1f); break;
            }
            tm.color = fontColor;

            MeshRenderer mr = textObj.GetComponent<MeshRenderer>();
            if (mr != null) mr.sortingOrder = 30017;

            FloatingUpgradeSkull comp = root.AddComponent<FloatingUpgradeSkull>();
            comp.bgRenderer = null;
            comp.skullRenderer = sr;
            comp.textMesh = tm;
            comp.shadowTextMesh = null;
            comp.bgColor = Color.white;
            comp.skullColor = Color.white;
            comp.textColor = fontColor;

            comp.initialScale = new Vector3(1.44f, 1.44f, 1f);
            root.transform.localScale = Vector3.zero;
        }
        catch (Exception ex)
        {
            Blasphemous.ModdingAPI.ModLog.Error("Error spawning FloatingUpgradeSkull: " + ex, Main.CoopLocal);
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        age += dt;

        // Movimiento ascendente flotante suave
        transform.position += Vector3.up * (floatSpeed * dt);

        float progress = age / lifetime;
        if (progress >= 1f)
        {
            Destroy(gameObject);
            return;
        }

        // Animación de escala: punch elástico inicial
        if (age < 0.18f)
        {
            float t = age / 0.18f;
            float scaleMult = Mathf.Sin(t * Mathf.PI * 0.5f) * 1.30f;
            transform.localScale = initialScale * scaleMult;
        }
        else if (age < 0.35f)
        {
            float t = (age - 0.18f) / 0.17f;
            float scaleMult = Mathf.Lerp(1.30f, 1.0f, t);
            transform.localScale = initialScale * scaleMult;
        }
        else
        {
            transform.localScale = initialScale;
        }

        // Desvanecimiento alfa gradual hacia el final de la vida
        float alpha = progress < 0.80f ? 1f : Mathf.Lerp(1f, 0f, (progress - 0.80f) / 0.20f);
        if (bgRenderer != null)
        {
            bgRenderer.color = new Color(bgColor.r, bgColor.g, bgColor.b, alpha);
        }
        if (skullRenderer != null)
        {
            skullRenderer.color = new Color(skullColor.r, skullColor.g, skullColor.b, alpha);
        }
        if (shadowTextMesh != null)
        {
            shadowTextMesh.color = new Color(0f, 0f, 0f, alpha * 0.85f);
        }
        if (textMesh != null)
        {
            textMesh.color = new Color(textColor.r, textColor.g, textColor.b, alpha);
        }
    }
}
