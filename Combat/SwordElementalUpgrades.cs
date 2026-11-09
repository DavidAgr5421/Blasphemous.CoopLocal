using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Framework.FrameworkCore;
using Framework.Inventory;
using Framework.Managers;
using Gameplay.GameControllers.Effects.Entity;
using Gameplay.GameControllers.Enemies.Framework.Damage;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using Gameplay.GameControllers.Penitent.Attack;
using Gameplay.UI.Others.Buttons;
using Gameplay.UI.Others.MenuLogic;
using HarmonyLib;
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

    internal const string HeartVenenoId = "HE_VENENO";
    internal const string HeartSangradoId = "HE_SANGRADO";

    internal static bool IsElementalHeart(string id)
    {
        return string.Equals(id, HeartVenenoId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(id, HeartSangradoId, StringComparison.OrdinalIgnoreCase);
    }

    internal static readonly string[] VenenoSkills = { SkillVeneno1, SkillVeneno2, SkillVeneno3 };
    internal static readonly string[] SangradoSkills = { SkillSangrado1, SkillSangrado2, SkillSangrado3 };

    private static readonly FieldInfo AllSkillsField = AccessTools.Field(typeof(SkillManager), "allSkills");
    private static readonly FieldInfo AllSwordsField = AccessTools.Field(typeof(InventoryManager), "allSwords");
    private static readonly FieldInfo OwnSwordsField = AccessTools.Field(typeof(InventoryManager), "ownSwords");

    private static Sprite venenoSmallSprite;
    private static Sprite venenoSmallBuyedSprite;
    private static Sprite sangradoSmallSprite;
    private static Sprite sangradoSmallBuyedSprite;
    private static Sprite venenoHeartSprite;
    private static Sprite sangradoHeartSprite;

    internal static bool IsElementalSkill(string skillId)
    {
        return IsVenenoSkill(skillId) || IsSangradoSkill(skillId);
    }

    internal static bool IsVenenoSkill(string skillId)
    {
        return !string.IsNullOrEmpty(skillId) && skillId.StartsWith("VENENO_", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsSangradoSkill(string skillId)
    {
        return !string.IsNullOrEmpty(skillId) && skillId.StartsWith("SANGRADO_", StringComparison.OrdinalIgnoreCase);
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
        if (venenoHeartSprite == null)
        {
            venenoHeartSprite = venenoSmallBuyedSprite;
        }
        if (sangradoHeartSprite == null)
        {
            sangradoHeartSprite = sangradoSmallBuyedSprite;
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

        Player2InventoryManager.AddOwnedSword(HeartVenenoId);
        Player2InventoryManager.AddOwnedSword(HeartSangradoId);
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
    }

    internal static void ResetElementalPath(bool isPlayer2)
    {
        float refunded = 0f;
        string[] allElem = { SkillVeneno1, SkillVeneno2, SkillVeneno3, SkillSangrado1, SkillSangrado2, SkillSangrado3 };

        foreach (string id in allElem)
        {
            UnlockableSkill sk = Core.SkillManager != null ? Core.SkillManager.GetSkill(id) : null;
            int cost = sk != null ? sk.cost : 0;
            if (isPlayer2)
            {
                if (Player2SkillManager.IsUnlocked(id))
                {
                    Player2SkillManager.SetUnlocked(id, false);
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
            if (isPlayer2 && CoopLocal.Player2 != null)
            {
                CoopLocal.Player2.Stats.Purge.Current += refunded;
                Player2SkillManager.Persist();
                Player2StatsSync.SaveCurrentVitals(CoopLocal.Player2);
            }
            else if (!isPlayer2 && Core.Logic?.Penitent != null)
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

        debugCycleIndex = (debugCycleIndex + 1) % 7;
        string modeDescription = "";

        switch (debugCycleIndex)
        {
            case 0:
                // Modo 0: Resetear sendas elementales en ambos jugadores
                ResetElementalPath(false);
                ResetElementalPath(true);
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
                // Modo 5: P1 Veneno III, P2 Sangrado III (Híbrido)
                ApplyDebugElementalPerPlayer(p1Skill: SkillVeneno3, p1Heart: HeartVenenoId, p2Skill: SkillSangrado3, p2Heart: HeartSangradoId);
                modeDescription = "Elemental: HÍBRIDO (P1 Veneno III | P2 Sangrado III)";
                break;
            case 6:
                // Modo 6: P1 Sangrado III, P2 Veneno III (Híbrido inverso)
                ApplyDebugElementalPerPlayer(p1Skill: SkillSangrado3, p1Heart: HeartSangradoId, p2Skill: SkillVeneno3, p2Heart: HeartVenenoId);
                modeDescription = "Elemental: HÍBRIDO (P1 Sangrado III | P2 Veneno III)";
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
            }

            if (CoopGameConfig.EnableElementalUpgrades)
            {
                WireNodeNavigation(v1Node, up: null, down: v2Node, left: null, right: leftTier1);
                WireNodeNavigation(v2Node, up: v1Node, down: v3Node, left: null, right: leftTier2 ?? leftTier1);
                WireNodeNavigation(v3Node, up: v2Node, down: null, left: null, right: leftTier3 ?? leftTier2 ?? leftTier1);

                ConnectLeftNeighbor(leftTier1, v1Node);
                ConnectLeftNeighbor(leftTier2, v2Node);
                ConnectLeftNeighbor(leftTier3, v3Node);

                WireNodeNavigation(s1Node, up: null, down: s2Node, left: rightTier1, right: null);
                WireNodeNavigation(s2Node, up: s1Node, down: s3Node, left: rightTier2 ?? rightTier1, right: null);
                WireNodeNavigation(s3Node, up: s2Node, down: null, left: rightTier3 ?? rightTier2 ?? rightTier1, right: null);

                ConnectRightNeighbor(rightTier1, s1Node);
                ConnectRightNeighbor(rightTier2, s2Node);
                ConnectRightNeighbor(rightTier3, s3Node);

                SanitizeVanillaNodeNavigation(cachedSkills, v1Node, v2Node, v3Node, s1Node, s2Node, s3Node);
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

        // Si TreeOffsetX aún tiene el centinela (-9999f), inicializarlo a la posición calibrada permanente
        if (CoopGameConfig.TreeOffsetX < -500f)
        {
            CoopGameConfig.TreeOffsetX = -2.0f;
            CoopGameConfig.TreeOffsetY = 4.0f;
            CoopGameConfig.Save();
        }

        containerTrans.localPosition = new Vector3(CoopGameConfig.TreeOffsetX, CoopGameConfig.TreeOffsetY, 0f);
        containerTrans.localScale = Vector3.one * CoopGameConfig.TreeScale;

        if (CoopGameConfig.ElementalBranchSpacing < 20f)
        {
            CoopGameConfig.ElementalBranchSpacing = 178f;
        }

        // Posicionar columnas simétricas a los extremos: Veneno (extremo izquierdo) y Sangrado (extremo derecho)
        float halfSpacing = CoopGameConfig.ElementalBranchSpacing * 0.5f;
        float localVenenoX = -halfSpacing;
        float localSangradoX = halfSpacing;

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

        // Configurar navegación explícita simétrica:
        // Veneno (extremo izquierdo) <-> Rama Izquierda Vanilla (v1 arriba -> down v2 -> down v3)
        WireNodeNavigation(v1, up: null, down: v2, left: null, right: leftTier1);
        WireNodeNavigation(v2, up: v1, down: v3, left: null, right: leftTier2 ?? leftTier1);
        WireNodeNavigation(v3, up: v2, down: null, left: null, right: leftTier3 ?? leftTier2 ?? leftTier1);

        if (CoopGameConfig.EnableElementalUpgrades)
        {
            ConnectLeftNeighbor(leftTier1, v1);
            ConnectLeftNeighbor(leftTier2, v2);
            ConnectLeftNeighbor(leftTier3, v3);
        }

        // Rama Derecha Vanilla <-> Sangrado (extremo derecho) (s1 arriba -> down s2 -> down s3)
        WireNodeNavigation(s1, up: null, down: s2, left: rightTier1, right: null);
        WireNodeNavigation(s2, up: s1, down: s3, left: rightTier2 ?? rightTier1, right: null);
        WireNodeNavigation(s3, up: s2, down: null, left: rightTier3 ?? rightTier2 ?? rightTier1, right: null);

        if (CoopGameConfig.EnableElementalUpgrades)
        {
            ConnectRightNeighbor(rightTier1, s1);
            ConnectRightNeighbor(rightTier2, s2);
            ConnectRightNeighbor(rightTier3, s3);
        }

        SanitizeVanillaNodeNavigation(cachedSkills, v1, v2, v3, s1, s2, s3);

        NewInventory_Description desc = LayoutDescriptionField?.GetValue(layout) as NewInventory_Description;
        ApplyDescriptionTextScale(desc);
        UpdateDebugBanner(layout, visible: true);
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

            Transform lblVeneno = containerTrans.Find("Lbl_Veneno");
            Transform lblSangrado = containerTrans.Find("Lbl_Sangrado");
            if (lblVeneno != null) UnityEngine.Object.Destroy(lblVeneno.gameObject);
            if (lblSangrado != null) UnityEngine.Object.Destroy(lblSangrado.gameObject);
        }
    }

    private static float saveConfigCooldown = 0f;

    internal static void UpdateDebugBanner(NewInventory_LayoutSkill layout, bool visible)
    {
        if (layout == null) return;
        Transform bannerTrans = layout.transform.Find("ElementalDebugBanner");
        GameObject bannerObj;
        Text bannerText;

        if (bannerTrans == null)
        {
            bannerObj = new GameObject("ElementalDebugBanner", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(Outline));
            bannerObj.transform.SetParent(layout.transform, false);
            RectTransform rt = bannerObj.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 18f);
            rt.sizeDelta = new Vector2(920f, 40f);

            bannerText = bannerObj.GetComponent<Text>();
            Text refTxt = layout.GetComponentInChildren<Text>(true);
            if (refTxt != null) bannerText.font = refTxt.font;
            bannerText.fontSize = 12;
            bannerText.alignment = TextAnchor.MiddleCenter;
            bannerText.color = new Color(1f, 0.92f, 0.45f, 1f);

            Outline ol = bannerObj.GetComponent<Outline>();
            ol.effectColor = new Color(0f, 0f, 0f, 0.95f);
            ol.effectDistance = new Vector2(1f, -1f);
        }
        else
        {
            bannerObj = bannerTrans.gameObject;
            bannerText = bannerObj.GetComponent<Text>();
        }

        bannerObj.SetActive(visible);
        if (visible && bannerText != null)
        {
            bannerText.text = $"[DEBUG ÁRBOL] Pos: X={CoopGameConfig.TreeOffsetX:F1}, Y={CoopGameConfig.TreeOffsetY:F1} | Sep H={CoopGameConfig.ElementalBranchSpacing:F0} | Sep V={CoopGameConfig.ElementalNodeVerticalSpacing:F0} | Escala={CoopGameConfig.TreeScale:F2}\n[Flechas]: Mover | [Shift+Flechas]: Rápido | [N / M]: Separación H | [U / J]: Separación V | [. / -]: Escala | [F8/Enter]: Guardar";
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
        NewInventory_Skill v1,
        NewInventory_Skill v2,
        NewInventory_Skill v3,
        NewInventory_Skill s1,
        NewInventory_Skill s2,
        NewInventory_Skill s3)
    {
        if (cachedSkills == null) return;
        var elemButtons = new HashSet<Selectable>();
        if (v1 != null && v1.GetEventButton() != null) elemButtons.Add(v1.GetEventButton());
        if (v2 != null && v2.GetEventButton() != null) elemButtons.Add(v2.GetEventButton());
        if (v3 != null && v3.GetEventButton() != null) elemButtons.Add(v3.GetEventButton());
        if (s1 != null && s1.GetEventButton() != null) elemButtons.Add(s1.GetEventButton());
        if (s2 != null && s2.GetEventButton() != null) elemButtons.Add(s2.GetEventButton());
        if (s3 != null && s3.GetEventButton() != null) elemButtons.Add(s3.GetEventButton());

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
        var cachedSkills = CachedSkillsField?.GetValue(layout) as List<NewInventory_Skill>;
        if (cachedSkills == null || cachedSkills.Count == 0) return;

        var skillsRoot = SkillsRootField?.GetValue(layout) as GameObject;
        float dt = Time.unscaledDeltaTime;

        // =====================================================================
        // 1. SISTEMA DEBUG ÁRBOL ELEMENTAL (Mover con Flechas, Escala . / -, Sep H: N/M, Sep V: U/J)
        // =====================================================================
        if (skillsRoot != null)
        {
            float moveStep = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) ? 8f : 1.5f;
            bool treeTransformChanged = false;

            if (Input.GetKey(KeyCode.LeftArrow))
            {
                CoopGameConfig.TreeOffsetX -= moveStep;
                treeTransformChanged = true;
            }
            if (Input.GetKey(KeyCode.RightArrow))
            {
                CoopGameConfig.TreeOffsetX += moveStep;
                treeTransformChanged = true;
            }
            if (Input.GetKey(KeyCode.UpArrow))
            {
                CoopGameConfig.TreeOffsetY += moveStep;
                treeTransformChanged = true;
            }
            if (Input.GetKey(KeyCode.DownArrow))
            {
                CoopGameConfig.TreeOffsetY -= moveStep;
                treeTransformChanged = true;
            }

            if (Input.GetKeyDown(KeyCode.Period) || Input.GetKeyDown(KeyCode.KeypadPeriod) || Input.GetKeyDown(KeyCode.Plus) || Input.GetKeyDown(KeyCode.KeypadPlus))
            {
                CoopGameConfig.TreeScale = Mathf.Clamp(CoopGameConfig.TreeScale + 0.05f, 0.35f, 2.50f);
                treeTransformChanged = true;
            }
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
            {
                CoopGameConfig.TreeScale = Mathf.Clamp(CoopGameConfig.TreeScale - 0.05f, 0.35f, 2.50f);
                treeTransformChanged = true;
            }

            if (Input.GetKey(KeyCode.M))
            {
                CoopGameConfig.ElementalBranchSpacing += moveStep * 2f;
                treeTransformChanged = true;
            }
            if (Input.GetKey(KeyCode.N))
            {
                CoopGameConfig.ElementalBranchSpacing = Mathf.Max(20f, CoopGameConfig.ElementalBranchSpacing - moveStep * 2f);
                treeTransformChanged = true;
            }

            if (Input.GetKey(KeyCode.U))
            {
                CoopGameConfig.ElementalNodeVerticalSpacing += moveStep;
                treeTransformChanged = true;
            }
            if (Input.GetKey(KeyCode.J))
            {
                CoopGameConfig.ElementalNodeVerticalSpacing = Mathf.Max(15f, CoopGameConfig.ElementalNodeVerticalSpacing - moveStep);
                treeTransformChanged = true;
            }

            if (treeTransformChanged)
            {
                UpdateElementalTreeTransform(skillsRoot);
                UpdateDebugBanner(layout, visible: true);
                saveConfigCooldown = 0.5f;
            }

            if (Input.GetKeyDown(KeyCode.F8) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                CoopGameConfig.Save();
                UpdateDebugBanner(layout, visible: true);
                if (Main.CoopLocal != null)
                {
                    Blasphemous.ModdingAPI.ModLog.Info($"[ElementalTree Debug] Guardado permanente: Offset=({CoopGameConfig.TreeOffsetX:F1}, {CoopGameConfig.TreeOffsetY:F1}) | Sep H={CoopGameConfig.ElementalBranchSpacing:F0} | Sep V={CoopGameConfig.ElementalNodeVerticalSpacing:F0} | Escala={CoopGameConfig.TreeScale:F2}", Main.CoopLocal);
                }
            }

            if (saveConfigCooldown > 0f)
            {
                saveConfigCooldown -= dt;
                if (saveConfigCooldown <= 0f)
                {
                    CoopGameConfig.Save();
                }
            }
        }

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
                    // 2. DENTRO DE LA RAMA DE SANGRADO (extremo derecho)
                    else if (IsSangradoSkill(curId))
                    {
                        if (wantLeft)
                        {
                            // De Sangrado hacia la izquierda -> vuelve exactamente al nodo lunge correspondiente en el árbol vanilla
                            if (curId == SkillSangrado1) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == "LUNGE_1");
                            else if (curId == SkillSangrado2) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == "LUNGE_2");
                            else if (curId == SkillSangrado3) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == "LUNGE_3");
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
                    // 3. EN EL ÁRBOL VANILLA
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

            Penitent attacker = weaponHit.AttackingEntity?.GetComponent<Penitent>()
                ?? weaponHit.AttackingEntity?.GetComponentInParent<Penitent>()
                ?? __instance.GetComponentInParent<Penitent>()
                ?? Core.Logic?.Penitent;
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

        Penitent attacker = hit.AttackingEntity?.GetComponent<Penitent>()
            ?? hit.AttackingEntity?.GetComponentInParent<Penitent>()
            ?? Core.Logic?.Penitent;
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
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        if (spriteRenderers != null)
        {
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
        if (target == null || target.Status.Dead) return;

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
                    FloatingUpgradeSkull.Spawn(skullPos, isPoison: true, $"+{poisonBonusPercent}%");
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
        if (target == null || target.Status.Dead) return;

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
                FloatingUpgradeSkull.Spawn(skullPos, isPoison: false, $"+{bleedBonusPercent}%");
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

    private void Update()
    {
        if (!CoopGameConfig.EnableElementalUpgrades)
        {
            ResetAllStatuses();
            return;
        }

        if (enemy == null || enemy.Status.Dead)
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

    private static Sprite _poisonSkullSprite;
    private static Sprite _bleedSkullSprite;
    private static Sprite _poisonBackdropSprite;
    private static Sprite _bleedBackdropSprite;

    public static Sprite GetPoisonSkullSprite()
    {
        if (_poisonSkullSprite == null)
        {
            _poisonSkullSprite = LoadSkullSprite(true);
        }
        return _poisonSkullSprite;
    }

    public static Sprite GetBleedSkullSprite()
    {
        if (_bleedSkullSprite == null)
        {
            _bleedSkullSprite = LoadSkullSprite(false);
        }
        return _bleedSkullSprite;
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
        "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";

    private static readonly string BleedSkullBase64 = 
        "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";

    private static Sprite LoadSkullSprite(bool isPoison)
    {
        try
        {
            byte[] data = null;
            string fileName = isPoison ? "skull_poison_96.png" : "skull_bleed_96.png";

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
                string base64 = isPoison ? PoisonSkullBase64 : BleedSkullBase64;
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

        return isPoison ? SwordElementalUpgrades.CreatePoisonBubblesIcon(true) : SwordElementalUpgrades.CreateBloodSymbolIcon(true);
    }

    public static void Spawn(Vector3 worldPos, bool isPoison, string text = "+")
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
            sr.sprite = isPoison ? GetPoisonSkullSprite() : GetBleedSkullSprite();
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
            Color fontColor = isPoison ? new Color(0.35f, 1f, 0.45f, 1f) : new Color(1f, 0.28f, 0.30f, 1f);
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
