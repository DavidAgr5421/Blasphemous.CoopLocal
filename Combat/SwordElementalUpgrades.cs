using System;
using System.Collections.Generic;
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

    internal static int GetUnlockedVenenoTier(bool isPlayer2)
    {
        if (isPlayer2)
        {
            if (Player2SkillManager.IsUnlocked(SkillVeneno3)) return 3;
            if (Player2SkillManager.IsUnlocked(SkillVeneno2)) return 2;
            if (Player2SkillManager.IsUnlocked(SkillVeneno1)) return 1;
            return 0;
        }
        if (Core.SkillManager == null) return 0;
        if (Core.SkillManager.IsSkillUnlocked(SkillVeneno3)) return 3;
        if (Core.SkillManager.IsSkillUnlocked(SkillVeneno2)) return 2;
        if (Core.SkillManager.IsSkillUnlocked(SkillVeneno1)) return 1;
        return 0;
    }

    internal static int GetUnlockedSangradoTier(bool isPlayer2)
    {
        if (isPlayer2)
        {
            if (Player2SkillManager.IsUnlocked(SkillSangrado3)) return 3;
            if (Player2SkillManager.IsUnlocked(SkillSangrado2)) return 2;
            if (Player2SkillManager.IsUnlocked(SkillSangrado1)) return 1;
            return 0;
        }
        if (Core.SkillManager == null) return 0;
        if (Core.SkillManager.IsSkillUnlocked(SkillSangrado3)) return 3;
        if (Core.SkillManager.IsSkillUnlocked(SkillSangrado2)) return 2;
        if (Core.SkillManager.IsSkillUnlocked(SkillSangrado1)) return 1;
        return 0;
    }

    internal static bool IsHeartEquipped(bool isPlayer2, string heartId)
    {
        if (isPlayer2)
        {
            return string.Equals(Player2InventoryManager.GetEquippedSwordId(), heartId, StringComparison.OrdinalIgnoreCase);
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
                maxHpPercent = 0.05f; // 5% vida máxima
                progressiveDps = 3.0f; // 3 daño/s por 5s
                break;
            case 2:
                maxHpPercent = 0.075f; // 7.5% vida máxima
                progressiveDps = 4.0f; // 4 daño/s por 5s
                break;
            case 3:
            default:
                maxHpPercent = 0.10f; // 10% vida máxima
                progressiveDps = 5.0f; // 5 daño/s por 5s
                break;
        }
    }

    private static Sprite CreateIconSprite(Color primary, Color border, bool filledCenter)
    {
        int size = 32;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs(x - center.x);
                float dy = Mathf.Abs(y - center.y);
                float diamond = dx + dy;
                Color c = Color.clear;
                if (diamond <= 13f)
                {
                    if (diamond >= 10.5f)
                    {
                        c = border;
                    }
                    else if (filledCenter || diamond <= 6.5f)
                    {
                        float t = Mathf.Clamp01(1f - (diamond / 11f));
                        c = Color.Lerp(primary * 0.65f, primary, t);
                        c.a = 1f;
                    }
                    else
                    {
                        c = new Color(primary.r * 0.25f, primary.g * 0.25f, primary.b * 0.25f, 0.85f);
                    }
                }
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 32f);
    }

    private static void EnsureSpritesCreated()
    {
        if (venenoSmallSprite == null)
        {
            venenoSmallSprite = CreateIconSprite(new Color(0.25f, 0.85f, 0.35f, 1f), new Color(0.55f, 0.52f, 0.38f, 1f), false);
            UnityEngine.Object.DontDestroyOnLoad(venenoSmallSprite.texture);
            UnityEngine.Object.DontDestroyOnLoad(venenoSmallSprite);
        }
        if (venenoSmallBuyedSprite == null)
        {
            venenoSmallBuyedSprite = CreateIconSprite(new Color(0.35f, 1f, 0.45f, 1f), new Color(0.85f, 1f, 0.75f, 1f), true);
            UnityEngine.Object.DontDestroyOnLoad(venenoSmallBuyedSprite.texture);
            UnityEngine.Object.DontDestroyOnLoad(venenoSmallBuyedSprite);
        }
        if (sangradoSmallSprite == null)
        {
            sangradoSmallSprite = CreateIconSprite(new Color(0.85f, 0.2f, 0.25f, 1f), new Color(0.55f, 0.52f, 0.38f, 1f), false);
            UnityEngine.Object.DontDestroyOnLoad(sangradoSmallSprite.texture);
            UnityEngine.Object.DontDestroyOnLoad(sangradoSmallSprite);
        }
        if (sangradoSmallBuyedSprite == null)
        {
            sangradoSmallBuyedSprite = CreateIconSprite(new Color(1f, 0.25f, 0.3f, 1f), new Color(1f, 0.8f, 0.8f, 1f), true);
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
            "Senda del Veneno (Nivel I).\nImbuye el filo con toxina: al equipar el Corazón de Ponzoña Viva, cada impacto de espada inflige 1 de daño por segundo durante 5 segundos.\n(El corazón requiere al menos Nivel I para activarse).",
            "[Cruceta / WASD] Navegar | [Q] Senda Veneno | [E] Senda Sangrado\nRequiere equipar el Corazón de Ponzoña Viva en la pestaña de Corazón de Mea Culpa.",
            tier: 1,
            cost: 0,
            parent: "",
            venenoSmallSprite,
            venenoSmallBuyedSprite,
            baseBig);

        RegisterOrUpdateSkill(
            allSkills,
            SkillVeneno2,
            "Ponzoña de Mea Culpa II",
            "Senda del Veneno (Nivel II).\nConcentra la ponzoña de Mea Culpa: al equipar el Corazón de Ponzoña Viva, cada golpe envenena causando 2.5 de daño por segundo durante 7.5 segundos.",
            "[Cruceta / WASD] Navegar | [Q] Senda Veneno | [E] Senda Sangrado\nRequiere equipar el Corazón de Ponzoña Viva en la pestaña de Corazón de Mea Culpa.",
            tier: 2,
            cost: 0,
            parent: SkillVeneno1,
            venenoSmallSprite,
            venenoSmallBuyedSprite,
            baseBig);

        RegisterOrUpdateSkill(
            allSkills,
            SkillVeneno3,
            "Ponzoña de Mea Culpa III",
            "Senda del Veneno (Nivel Máximo).\nPonzoña letal: al equipar el Corazón de Ponzoña Viva, cada impacto de espada inflige 4 de daño por segundo a lo largo de 10 segundos.",
            "[Cruceta / WASD] Navegar | [Q] Senda Veneno | [E] Senda Sangrado\nRequiere equipar el Corazón de Ponzoña Viva en la pestaña de Corazón de Mea Culpa.",
            tier: 3,
            cost: 0,
            parent: SkillVeneno2,
            venenoSmallSprite,
            venenoSmallBuyedSprite,
            baseBig);

        RegisterOrUpdateSkill(
            allSkills,
            SkillSangrado1,
            "Filo Sangrante I",
            "Senda del Sangrado (Nivel I).\nAl equipar el Corazón de la Hemorragia Sagrada, los golpes acumulan sangrado (Umbral: 6 golpes en enemigos normales, 10 en jefes). Al superar el umbral, inflige un 5% de la vida máxima del objetivo y aplica sangrado progresivo de 3 de daño por segundo durante 5 segundos.\n(El corazón requiere al menos Nivel I para activarse).",
            "[Cruceta / WASD] Navegar | [Q] Senda Veneno | [E] Senda Sangrado\nRequiere equipar el Corazón de la Hemorragia Sagrada en la pestaña de Corazón de Mea Culpa.",
            tier: 1,
            cost: 0,
            parent: "",
            sangradoSmallSprite,
            sangradoSmallBuyedSprite,
            baseBig);

        RegisterOrUpdateSkill(
            allSkills,
            SkillSangrado2,
            "Filo Sangrante II",
            "Senda del Sangrado (Nivel II).\nProfundiza las heridas: al superar el umbral de sangrado (6 golpes en enemigos, 10 en jefes) con el Corazón de la Hemorragia Sagrada, inflige un 7.5% de la vida máxima del objetivo y 4 de daño por segundo durante 5 segundos.",
            "[Cruceta / WASD] Navegar | [Q] Senda Veneno | [E] Senda Sangrado\nRequiere equipar el Corazón de la Hemorragia Sagrada en la pestaña de Corazón de Mea Culpa.",
            tier: 2,
            cost: 0,
            parent: SkillSangrado1,
            sangradoSmallSprite,
            sangradoSmallBuyedSprite,
            baseBig);

        RegisterOrUpdateSkill(
            allSkills,
            SkillSangrado3,
            "Filo Sangrante III",
            "Senda del Sangrado (Nivel Máximo).\nHemorragia devastadora: al superar el umbral de sangrado (6 golpes en enemigos, 10 en jefes) con el Corazón de la Hemorragia Sagrada, inflige un 10% de la vida máxima del objetivo y un daño progresivo de 5 de daño por segundo durante 5 segundos.",
            "[Cruceta / WASD] Navegar | [Q] Senda Veneno | [E] Senda Sangrado\nRequiere equipar el Corazón de la Hemorragia Sagrada en la pestaña de Corazón de Mea Culpa.",
            tier: 3,
            cost: 0,
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
            "Canaliza la Senda del Sangrado desbloqueada en el Árbol de Habilidades.\nCada impacto acumula sangrado (Umbral: 6 golpes en enemigos normales, 10 en jefes).\n• Sangrado I: 5% Vida Máx + 3 de daño/s por 5s.\n• Sangrado II: 7.5% Vida Máx + 4 de daño/s por 5s.\n• Sangrado III: 10% Vida Máx + 5 de daño/s por 5s.\n(El sangrado solo se aplica mientras este corazón está equipado y se posee al menos Filo Sangrante I).",
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

    internal static void GrantMatchingHeartOnUnlock(string skillId, bool isPlayer2)
    {
        if (Core.InventoryManager != null)
        {
            EnsureSwordsRegistered(Core.InventoryManager);
        }

        if (IsVenenoSkill(skillId))
        {
            if (isPlayer2)
            {
                Player2InventoryManager.AddOwnedSword(HeartVenenoId);
                if (string.IsNullOrEmpty(Player2InventoryManager.GetEquippedSwordId()))
                {
                    Player2InventoryManager.EquipSword(HeartVenenoId);
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
            if (isPlayer2)
            {
                Player2InventoryManager.AddOwnedSword(HeartSangradoId);
                if (string.IsNullOrEmpty(Player2InventoryManager.GetEquippedSwordId()))
                {
                    Player2InventoryManager.EquipSword(HeartSangradoId);
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
                modeDescription = "Elemental: SANGRADO III (10% Max HP + 5 DPS / 5s) + Corazón Hemorragia";
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

        // Comprobar si ya fueron inyectados
        foreach (var s in cachedSkills)
        {
            if (s != null && s.GetSkillId() == SkillVeneno1)
            {
                return;
            }
        }

        // Buscar nodos de referencia por tier (1, 2, 3) para alinear alturas (Y) y espaciado (X)
        NewInventory_Skill templateNode = cachedSkills[0];
        float yTier1 = -60f, yTier2 = 0f, yTier3 = 60f;
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        NewInventory_Skill leftTier1 = null, leftTier2 = null, leftTier3 = null;

        foreach (var node in cachedSkills)
        {
            if (node == null) continue;
            Vector3 pos = node.transform.localPosition;
            if (pos.x < minX) minX = pos.x;
            if (pos.x > maxX) maxX = pos.x;

            string id = node.GetSkillId() ?? "";
            if (id.EndsWith("_1"))
            {
                yTier1 = pos.y;
                if (leftTier1 == null || pos.x < leftTier1.transform.localPosition.x) leftTier1 = node;
            }
            else if (id.EndsWith("_2"))
            {
                yTier2 = pos.y;
                if (leftTier2 == null || pos.x < leftTier2.transform.localPosition.x) leftTier2 = node;
            }
            else if (id.EndsWith("_3"))
            {
                yTier3 = pos.y;
                if (leftTier3 == null || pos.x < leftTier3.transform.localPosition.x) leftTier3 = node;
            }
        }

        // Contenedor dedicado para el árbol elemental completo (permite mover y escalar en vivo)
        Transform containerTrans = skillsRoot.transform.Find("ElementalTreeContainer");
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

        // Si TreeOffsetX aún tiene el centinela (-9999f), inicializarlo a la izquierda del árbol vanilla
        if (CoopGameConfig.TreeOffsetX < -500f)
        {
            CoopGameConfig.TreeOffsetX = minX - 60f;
            CoopGameConfig.TreeOffsetY = 0f;
            CoopGameConfig.Save();
        }

        containerTrans.localPosition = new Vector3(CoopGameConfig.TreeOffsetX, CoopGameConfig.TreeOffsetY, 0f);
        containerTrans.localScale = Vector3.one * CoopGameConfig.TreeScale;

        // Posicionar columnas relativas dentro del contenedor: Veneno (izquierda) y Sangrado (derecha)
        float localVenenoX = -40f;
        float localSangradoX = 20f;

        // Reducir la distancia vertical entre los 3 tiers a un 60%
        float elemTier2Y = yTier2;
        float elemTier1Y = yTier2 + (yTier1 - yTier2) * 0.60f;
        float elemTier3Y = yTier2 + (yTier3 - yTier2) * 0.60f;

        NewInventory_Skill v1 = CloneSkillNode(layout, containerTrans, templateNode, SkillVeneno1, new Vector3(localVenenoX, elemTier1Y, 0f), new Color(0.55f, 1f, 0.6f));
        NewInventory_Skill v2 = CloneSkillNode(layout, containerTrans, templateNode, SkillVeneno2, new Vector3(localVenenoX, elemTier2Y, 0f), new Color(0.55f, 1f, 0.6f));
        NewInventory_Skill v3 = CloneSkillNode(layout, containerTrans, templateNode, SkillVeneno3, new Vector3(localVenenoX, elemTier3Y, 0f), new Color(0.55f, 1f, 0.6f));

        NewInventory_Skill s1 = CloneSkillNode(layout, containerTrans, templateNode, SkillSangrado1, new Vector3(localSangradoX, elemTier1Y, 0f), new Color(1f, 0.55f, 0.55f));
        NewInventory_Skill s2 = CloneSkillNode(layout, containerTrans, templateNode, SkillSangrado2, new Vector3(localSangradoX, elemTier2Y, 0f), new Color(1f, 0.55f, 0.55f));
        NewInventory_Skill s3 = CloneSkillNode(layout, containerTrans, templateNode, SkillSangrado3, new Vector3(localSangradoX, elemTier3Y, 0f), new Color(1f, 0.55f, 0.55f));

        // Configurar navegación explícita bidireccional entre Veneno <-> Sangrado <-> Árbol Vanilla
        WireNodeNavigation(v1, up: v2, down: null, left: null, right: s1);
        WireNodeNavigation(v2, up: v3, down: v1, left: null, right: s2);
        WireNodeNavigation(v3, up: null, down: v2, left: null, right: s3);

        WireNodeNavigation(s1, up: s2, down: null, left: v1, right: leftTier1);
        WireNodeNavigation(s2, up: s3, down: s1, left: v2, right: leftTier2 ?? leftTier1);
        WireNodeNavigation(s3, up: null, down: s2, left: v3, right: leftTier3 ?? leftTier2 ?? leftTier1);

        ConnectLeftNeighbor(leftTier1, s1);
        ConnectLeftNeighbor(leftTier2, s2);
        ConnectLeftNeighbor(leftTier3, s3);

        CreateColumnLabel(containerTrans, "Lbl_Veneno", "VENENO\n[Q]", new Vector3(localVenenoX, elemTier3Y + 28f, 0f), new Color(0.45f, 0.95f, 0.5f));
        CreateColumnLabel(containerTrans, "Lbl_Sangrado", "SANGRADO\n[E]", new Vector3(localSangradoX, elemTier3Y + 28f, 0f), new Color(0.95f, 0.4f, 0.4f));

        UpdateDebugBanner(layout, visible: true);

        NewInventory_Description desc = LayoutDescriptionField?.GetValue(layout) as NewInventory_Description;
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

    internal static void UpdateElementalTreeTransform(GameObject skillsRoot)
    {
        if (skillsRoot == null) return;
        Transform containerTrans = skillsRoot.transform.Find("ElementalTreeContainer");
        if (containerTrans != null)
        {
            containerTrans.localPosition = new Vector3(CoopGameConfig.TreeOffsetX, CoopGameConfig.TreeOffsetY, 0f);
            containerTrans.localScale = Vector3.one * CoopGameConfig.TreeScale;

            Transform lblVeneno = containerTrans.Find("Lbl_Veneno");
            Transform lblSangrado = containerTrans.Find("Lbl_Sangrado");
            int lblSize = Mathf.Clamp(Mathf.RoundToInt(10 * CoopGameConfig.SkillTextScale), 8, 26);
            if (lblVeneno != null)
            {
                Text t = lblVeneno.GetComponent<Text>();
                if (t != null) t.fontSize = lblSize;
            }
            if (lblSangrado != null)
            {
                Text t = lblSangrado.GetComponent<Text>();
                if (t != null) t.fontSize = lblSize;
            }
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
        if (up != null) nav.selectOnUp = up.GetEventButton();
        if (down != null) nav.selectOnDown = down.GetEventButton();
        if (left != null) nav.selectOnLeft = left.GetEventButton();
        if (right != null) nav.selectOnRight = right.GetEventButton();
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
        // Preservar la navegación hacia arriba, abajo y derecha para no bloquear el árbol vanilla
        if (nav.selectOnRight == null) nav.selectOnRight = btn.FindSelectableOnRight();
        if (nav.selectOnUp == null) nav.selectOnUp = btn.FindSelectableOnUp();
        if (nav.selectOnDown == null) nav.selectOnDown = btn.FindSelectableOnDown();
        btn.navigation = nav;
    }

    private static void CreateColumnLabel(Transform parent, string name, string text, Vector3 localPos, Color color)
    {
        if (parent.Find(name) != null) return;
        Text referenceText = parent.GetComponentInParent<Canvas>()?.GetComponentInChildren<Text>(true);

        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(Outline));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(64f, 24f);
        rt.localPosition = localPos;
        rt.localScale = Vector3.one * 0.65f;

        Text label = go.GetComponent<Text>();
        if (referenceText != null)
        {
            label.font = referenceText.font;
        }
        label.text = text;
        label.fontSize = 10;
        label.alignment = TextAnchor.MiddleCenter;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.color = color;
        label.raycastTarget = false;

        Outline outline = go.GetComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        outline.effectDistance = new Vector2(1f, -1f);
    }

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
            rt.sizeDelta = new Vector2(750f, 32f);

            bannerText = bannerObj.GetComponent<Text>();
            Text refTxt = layout.GetComponentInChildren<Text>(true);
            if (refTxt != null) bannerText.font = refTxt.font;
            bannerText.fontSize = 11;
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
            bannerText.text = $"[DEBUG ÁRBOL] Pos: X={CoopGameConfig.TreeOffsetX:F1}, Y={CoopGameConfig.TreeOffsetY:F1} | Escala Árbol: {CoopGameConfig.TreeScale:F2} | Escala Texto: {Mathf.RoundToInt(CoopGameConfig.SkillTextScale * 100)}%\n[Flechas]: Mover | [. / -]: Tamaño Árbol | [{{ / }}]: Tamaño Texto";
        }
    }

    private static float navCooldown = 0f;
    private static float saveConfigCooldown = -1f;

    internal static void HandleSkillTreeShortcuts(NewInventory_LayoutSkill layout)
    {
        if (layout == null) return;
        var skillsRoot = SkillsRootField?.GetValue(layout) as GameObject;
        var cachedSkills = CachedSkillsField?.GetValue(layout) as List<NewInventory_Skill>;
        if (cachedSkills == null || cachedSkills.Count == 0 || skillsRoot == null) return;

        int currentSelected = (int)SkillTreeUIHelper.CurrentSelectedField.GetValue(layout);
        float dt = Time.unscaledDeltaTime;
        if (navCooldown > 0f) navCooldown -= dt;

        // =====================================================================
        // 1. SISTEMA DEBUG: Mover con Flechas y Escalar con '.' y '-'
        // =====================================================================
        float moveStep = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) ? 8f : 2f;
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

        // =====================================================================
        // 1b. AJUSTE DE TAMAÑO DE TEXTO: '{' para aumentar, '}' para disminuir
        // =====================================================================
        bool textScaleChanged = false;
        string typed = Input.inputString ?? "";
        bool wantIncText = typed.Contains("{") || typed.Contains("[") ||
                           Input.GetKeyDown(KeyCode.LeftBracket);
        bool wantDecText = typed.Contains("}") || typed.Contains("]") ||
                           Input.GetKeyDown(KeyCode.RightBracket);

        if (wantIncText)
        {
            CoopGameConfig.SkillTextScale = Mathf.Clamp(CoopGameConfig.SkillTextScale + 0.10f, 0.60f, 3.00f);
            textScaleChanged = true;
        }
        else if (wantDecText)
        {
            CoopGameConfig.SkillTextScale = Mathf.Clamp(CoopGameConfig.SkillTextScale - 0.10f, 0.60f, 3.00f);
            textScaleChanged = true;
        }

        if (textScaleChanged)
        {
            NewInventory_Description desc = LayoutDescriptionField?.GetValue(layout) as NewInventory_Description;
            ApplyDescriptionTextScale(desc);
            UpdateElementalTreeTransform(skillsRoot);
            UpdateDebugBanner(layout, visible: true);
            saveConfigCooldown = 0.5f;
            Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeSelection");
        }

        if (treeTransformChanged)
        {
            UpdateElementalTreeTransform(skillsRoot);
            UpdateDebugBanner(layout, visible: true);
            saveConfigCooldown = 0.5f;
        }

        if (saveConfigCooldown > 0f)
        {
            saveConfigCooldown -= dt;
            if (saveConfigCooldown <= 0f)
            {
                CoopGameConfig.Save();
                if (Main.CoopLocal != null)
                {
                    Blasphemous.ModdingAPI.ModLog.Info($"[ElementalTree Debug] Guardado: Offset=({CoopGameConfig.TreeOffsetX:F1}, {CoopGameConfig.TreeOffsetY:F1}) | Escala={CoopGameConfig.TreeScale:F2}", Main.CoopLocal);
                }
            }
        }

        // =====================================================================
        // 2. NAVEGACIÓN NATURAL: WASD + Gamepad (Stick y D-Pad)
        // =====================================================================
        if (navCooldown <= 0f)
        {
            bool wantLeft = Input.GetKeyDown(KeyCode.A) || Player2Pad.Left || CheckRewiredLeft();
            bool wantRight = Input.GetKeyDown(KeyCode.D) || Player2Pad.Right || CheckRewiredRight();
            bool wantUp = Input.GetKeyDown(KeyCode.W) || Player2Pad.Up || CheckRewiredUp();
            bool wantDown = Input.GetKeyDown(KeyCode.S) || Player2Pad.Down || CheckRewiredDown();

            if (wantLeft || wantRight || wantUp || wantDown)
            {
                if (currentSelected >= 0 && currentSelected < cachedSkills.Count)
                {
                    NewInventory_Skill curSkill = cachedSkills[currentSelected];
                    string curId = curSkill != null ? curSkill.GetSkillId() : "";
                    int targetIdx = -1;

                    if (IsVenenoSkill(curId))
                    {
                        if (wantRight)
                        {
                            // De Veneno a Sangrado en el mismo tier
                            string sangradoTarget = curId.Replace("VENENO_", "SANGRADO_");
                            targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == sangradoTarget);
                        }
                        else if (wantUp)
                        {
                            if (curId == SkillVeneno1) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillVeneno2);
                            else if (curId == SkillVeneno2) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillVeneno3);
                        }
                        else if (wantDown)
                        {
                            if (curId == SkillVeneno3) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillVeneno2);
                            else if (curId == SkillVeneno2) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillVeneno1);
                        }
                    }
                    else if (IsSangradoSkill(curId))
                    {
                        if (wantLeft)
                        {
                            // De Sangrado a Veneno en el mismo tier
                            string venenoTarget = curId.Replace("SANGRADO_", "VENENO_");
                            targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == venenoTarget);
                        }
                        else if (wantRight)
                        {
                            // De Sangrado al árbol vanilla (nodo del tier correspondiente)
                            targetIdx = GetVanillaLeftNodeIndex(cachedSkills, curId);
                        }
                        else if (wantUp)
                        {
                            if (curId == SkillSangrado1) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillSangrado2);
                            else if (curId == SkillSangrado2) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillSangrado3);
                        }
                        else if (wantDown)
                        {
                            if (curId == SkillSangrado3) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillSangrado2);
                            else if (curId == SkillSangrado2) targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == SkillSangrado1);
                        }
                    }
                    else if (wantLeft)
                    {
                        // Estamos en el árbol vanilla y pulsamos Izquierda: entrar a Sangrado si estamos cerca del borde izquierdo
                        if (IsVanillaLeftmostNode(cachedSkills, curSkill))
                        {
                            string targetSangrado = GetSangradoTierForVanillaNode(curSkill);
                            targetIdx = cachedSkills.FindIndex(s => s != null && s.GetSkillId() == targetSangrado);
                        }
                    }

                    if (targetIdx >= 0 && targetIdx < cachedSkills.Count && targetIdx != currentSelected)
                    {
                        SelectSkillDirect(layout, cachedSkills, targetIdx);
                        navCooldown = 0.20f;
                        return;
                    }
                }
            }
        }

        // 3. Atajos directos Q y E para saltar entre Veneno y Sangrado
        if (Input.GetKeyDown(KeyCode.Q))
        {
            CycleSkillGroup(layout, cachedSkills, currentSelected, VenenoSkills);
        }
        else if (Input.GetKeyDown(KeyCode.E))
        {
            CycleSkillGroup(layout, cachedSkills, currentSelected, SangradoSkills);
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
    }

    private static int GetVanillaLeftNodeIndex(List<NewInventory_Skill> cachedSkills, string sangradoId)
    {
        string suffix = sangradoId.EndsWith("_3") ? "_3" : (sangradoId.EndsWith("_2") ? "_2" : "_1");
        int bestIdx = -1;
        float bestMinX = float.MaxValue;

        for (int i = 0; i < cachedSkills.Count; i++)
        {
            var s = cachedSkills[i];
            if (s == null) continue;
            string id = s.GetSkillId();
            if (IsElementalSkill(id)) continue;

            if (id.EndsWith(suffix))
            {
                float x = s.transform.localPosition.x;
                if (x < bestMinX)
                {
                    bestMinX = x;
                    bestIdx = i;
                }
            }
        }

        if (bestIdx < 0)
        {
            for (int i = 0; i < cachedSkills.Count; i++)
            {
                var s = cachedSkills[i];
                if (s == null || IsElementalSkill(s.GetSkillId())) continue;
                float x = s.transform.localPosition.x;
                if (x < bestMinX)
                {
                    bestMinX = x;
                    bestIdx = i;
                }
            }
        }

        return bestIdx;
    }

    private static bool IsVanillaLeftmostNode(List<NewInventory_Skill> cachedSkills, NewInventory_Skill curSkill)
    {
        if (curSkill == null) return false;
        float minX = float.MaxValue;
        foreach (var s in cachedSkills)
        {
            if (s == null || IsElementalSkill(s.GetSkillId())) continue;
            float x = s.transform.localPosition.x;
            if (x < minX) minX = x;
        }
        return curSkill.transform.localPosition.x <= (minX + 85f);
    }

    private static string GetSangradoTierForVanillaNode(NewInventory_Skill curSkill)
    {
        string id = curSkill != null ? curSkill.GetSkillId() : "";
        if (id.EndsWith("_3")) return SkillSangrado3;
        if (id.EndsWith("_2")) return SkillSangrado2;
        if (id.EndsWith("_1")) return SkillSangrado1;
        float y = curSkill != null ? curSkill.transform.localPosition.y : 0f;
        if (y > 30f) return SkillSangrado3;
        if (y < -30f) return SkillSangrado1;
        return SkillSangrado2;
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
[HarmonyPatch(typeof(EnemyDamageArea), "TakeDamage")]
internal static class EnemyDamageArea_TakeDamage_ElementalStatus_Patch
{
    internal static bool IsApplyingStatusDamage;

    private static void Postfix(EnemyDamageArea __instance, Hit hit)
    {
        if (IsApplyingStatusDamage) return;
        if (hit.AttackingEntity == null) return;

        Penitent attacker = hit.AttackingEntity.GetComponent<Penitent>() ?? hit.AttackingEntity.GetComponentInParent<Penitent>();
        if (attacker == null) return;

        bool isPlayer2 = attacker == CoopLocal.Player2;
        if (!isPlayer2 && attacker != Core.Logic?.Penitent) return;

        Enemy enemy = __instance.GetComponent<Enemy>() ?? __instance.GetComponentInParent<Enemy>();
        if (enemy == null || enemy.Status.Dead) return;

        // 1. Comprobar Veneno: requiere tener equipado HE_VENENO + tener desbloqueado VENENO_1..3 en el árbol
        if (SwordElementalUpgrades.IsHeartEquipped(isPlayer2, SwordElementalUpgrades.HeartVenenoId))
        {
            int poisonTier = SwordElementalUpgrades.GetUnlockedVenenoTier(isPlayer2);
            if (poisonTier > 0)
            {
                EnemyElementalStatusController ctrl = enemy.gameObject.GetComponent<EnemyElementalStatusController>()
                    ?? enemy.gameObject.AddComponent<EnemyElementalStatusController>();
                ctrl.ApplyPoison(enemy, attacker, poisonTier);
            }
        }

        // 2. Comprobar Sangrado: requiere tener equipado HE_SANGRADO + tener desbloqueado SANGRADO_1..3 en el árbol
        if (SwordElementalUpgrades.IsHeartEquipped(isPlayer2, SwordElementalUpgrades.HeartSangradoId))
        {
            int bleedTier = SwordElementalUpgrades.GetUnlockedSangradoTier(isPlayer2);
            if (bleedTier > 0)
            {
                EnemyElementalStatusController ctrl = enemy.gameObject.GetComponent<EnemyElementalStatusController>()
                    ?? enemy.gameObject.AddComponent<EnemyElementalStatusController>();
                ctrl.AddBleedHit(enemy, attacker, bleedTier);
            }
        }
    }
}

internal sealed class EnemyElementalStatusController : MonoBehaviour
{
    private Enemy enemy;
    private float maxObservedLife;

    // Estado de Veneno
    private int activePoisonTier;
    private float poisonDps;
    private float poisonRemainingTime;
    private float poisonMaxDuration = 5f;
    private float poisonTickTimer;

    // Estado de Sangrado
    private int activeBleedTier;
    private int bleedStacks;
    private int bleedThreshold = 6;
    private float bleedStackDecayTimer;
    private float bleedProgressiveDps;
    private float bleedProgressiveRemainingTime;
    private float bleedProgressiveMaxDuration = 5f;
    private float bleedTickTimer;

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
        if (IsBossEnemy(target, maxHp))
        {
            return 10; // Jefes: 10 golpes
        }
        // Enemigos normales: 6 golpes base (+ resistencia adicional si tiene defensa alta)
        int resistanceBonus = 0;
        if (target != null && target.Stats != null && target.Stats.Defense != null && target.Stats.Defense.Final > 0f)
        {
            resistanceBonus = Mathf.Clamp(Mathf.RoundToInt(target.Stats.Defense.Final * 0.5f), 0, 3);
        }
        return 6 + resistanceBonus;
    }

    internal void ApplyPoison(Enemy target, Penitent attacker, int tier)
    {
        if (target == null || target.Status.Dead) return;
        enemy = target;
        GetEnemyMaxHealth();

        SwordElementalUpgrades.GetPoisonParams(tier, out float dps, out float duration);
        if (tier >= activePoisonTier || poisonRemainingTime <= 0f)
        {
            activePoisonTier = tier;
            poisonDps = dps;
            poisonMaxDuration = duration;
        }
        poisonRemainingTime = Mathf.Max(poisonRemainingTime, duration);
        poisonMaxDuration = Mathf.Max(poisonMaxDuration, poisonRemainingTime);
        if (poisonTickTimer <= 0f)
        {
            poisonTickTimer = 1.0f;
        }

        PulseEnemyColor(new Color(0.4f, 1f, 0.45f, 1f), 0.2f);
        UpdateStatusBars();
    }

    internal void AddBleedHit(Enemy target, Penitent attacker, int tier)
    {
        if (target == null || target.Status.Dead) return;
        enemy = target;
        float maxHp = GetEnemyMaxHealth();
        bleedThreshold = ComputeBleedThreshold(target);

        activeBleedTier = Mathf.Max(activeBleedTier, tier);
        bleedStacks++;
        bleedStackDecayTimer = 8.0f;

        if (bleedStacks >= bleedThreshold)
        {
            bleedStacks = 0;
            SwordElementalUpgrades.GetBleedParams(activeBleedTier, out float maxHpPercent, out float progDps, out float progDuration);

            // Estallido inmediato de % de vida máxima (5% en I, 7.5% en II, 10% en III)
            float burstDamage = Mathf.Max(1f, maxHp * maxHpPercent);
            DealDirectStatusDamage(burstDamage, "PenitentHeavyEnemyHit", isPoison: false, isBurst: true);

            // Activar daño progresivo de sangrado (5 segundos de 3 / 4 / 5 DPS)
            bleedProgressiveDps = progDps;
            bleedProgressiveRemainingTime = progDuration;
            bleedProgressiveMaxDuration = progDuration;
            bleedTickTimer = 1.0f;

            PulseEnemyColor(new Color(1f, 0.2f, 0.25f, 1f), 0.35f);
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

    private void DealDirectStatusDamage(float amount, string audioId, bool isPoison, bool isBurst)
    {
        if (enemy == null || enemy.Status.Dead || amount <= 0f) return;

        EnemyDamageArea_TakeDamage_ElementalStatus_Patch.IsApplyingStatusDamage = true;
        try
        {
            // Asegurar que la defensa del enemigo no anule los ticks pequeños de 1 DPS del Veneno I
            float defense = enemy.Stats?.Defense != null ? enemy.Stats.Defense.Final : 0f;
            float adjustedAmount = amount + Mathf.Max(0f, defense);
            enemy.Damage(adjustedAmount, audioId);
            enemy.GetComponentInChildren<MasterShaderEffects>()?.DamageEffectBlink(0f, 0.12f);

            // Texto emergente flotante con color verde (veneno) o rojo (sangrado) que flota y se desvanece
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
