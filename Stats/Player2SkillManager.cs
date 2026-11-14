using Framework.FrameworkCore;
using Framework.Managers;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Blasphemous.CoopLocal;

// Round 61: shadow per-P2 del Skill Tree. SkillManager es singleton global (allSkills: Dictionary<string,UnlockableSkill>)
// con un solo bool `unlocked` por id. Ability.GetLastUnlockedSkill() recorre `unlocableSkill` (List<string> privada)
// y pregunta a Core.SkillManager.IsSkillUnlocked(id) -> hardcodea P1. Para P2 sombreamos solo el bool
// centralizado en CoopSaveManager / coop_savedata_slot{slot}.txt.

internal static class Player2SkillManager
{
    // Los 21 ids reales de UnlockableSkillId (Gameplay.GameControllers.Penitent.Abilities) + ramas elementales
    internal static readonly string[] AllIds =
    {
        "CHARGED_1", "CHARGED_2", "CHARGED_3",
        "COMBO_1", "COMBO_2", "COMBO_3",
        "LUNGE_1", "LUNGE_2", "LUNGE_3",
        "RANGED_1", "RANGED_2", "RANGED_3",
        "VERTICAL_1", "VERTICAL_2", "VERTICAL_3",
        "VENENO_1", "VENENO_2", "VENENO_3",
        "SANGRADO_1", "SANGRADO_2", "SANGRADO_3",
        "ELECTRICO_1", "ELECTRICO_2", "ELECTRICO_3",
        "MALDICION_1", "MALDICION_2", "MALDICION_3",
    };

    private static PlayerCoopData P2Data => CoopSaveManager.CurrentSave.GetPlayer(1);

    internal static bool IsUnlocked(string id)
    {
        return IsUnlocked(1, id);
    }

    internal static bool IsUnlocked(int playerIndex, string id)
    {
        var pData = CoopSaveManager.CurrentSave.GetPlayer(playerIndex);
        return pData.Skills.TryGetValue(id, out bool v) && v;
    }

    internal static void SetUnlocked(string id, bool value)
    {
        SetUnlocked(1, id, value);
    }

    internal static void SetUnlocked(int playerIndex, string id, bool value)
    {
        var pData = CoopSaveManager.CurrentSave.GetPlayer(playerIndex);
        pData.Skills[id] = value;
    }

    internal static void LoadForSlot(int slot)
    {
        CoopSaveManager.LoadSlot(slot);
    }

    internal static void SaveForSlot(int slot)
    {
        CoopSaveManager.SaveSlot(slot);
    }

    internal static void Persist()
    {
        CoopSaveManager.SaveCurrentSlot();
    }

    // Llamado desde CoopLocal.OnPlayerSpawn cada vez que (re)spawnea P2, con el slot actual.
    internal static void EnsureLoadedForCurrentSlot()
    {
        CoopSaveManager.EnsureLoadedForCurrentSlot();
    }
}

// Unico punto de gating de skills: Ability.GetLastUnlockedSkill() recorre el campo privado
// unlocableSkill y pregunta a Core.SkillManager.IsSkillUnlocked. Para P2 sombreamos
// el bool y seguimos usando Core.SkillManager.GetSkill(id) para la definicion (global).
[HarmonyPatch(typeof(Ability), "GetLastUnlockedSkill")]
internal static class Ability_GetLastUnlockedSkill_P2_Patch
{
    private static readonly Dictionary<Ability, string> lastLoggedResult = new Dictionary<Ability, string>();
    private static readonly Dictionary<Ability, string> lastLoggedIdentity = new Dictionary<Ability, string>();

    private static bool Prefix(Ability __instance, ref UnlockableSkill __result, List<string> ___unlocableSkill)
    {
        Penitent owner = __instance.EntityOwner as Penitent;
        string identity;
        if (owner == null) identity = "null";
        else if (owner == Core.Logic.Penitent) identity = "P1";
        else if (owner == CoopLocal.Player2) identity = "P2-current";
        else identity = "STALE";

        string instanceName = __instance.GetType().Name;
        string identityKey = identity;
        if (!lastLoggedIdentity.TryGetValue(__instance, out string lastIdentity) || lastIdentity != identityKey)
        {
            lastLoggedIdentity[__instance] = identityKey;
            int ownerId = owner != null ? owner.GetInstanceID() : -1;
            int p1Id = Core.Logic.Penitent != null ? Core.Logic.Penitent.GetInstanceID() : -1;
            int p2Id = CoopLocal.Player2 != null ? CoopLocal.Player2.GetInstanceID() : -1;
            DashParryDebugLog.Log($"[Ability] {instanceName} EntityOwner identity={identity} ownerId={ownerId} P1Id={p1Id} P2Id={p2Id}");
        }

        int guestSlot = CoopPlayerRegistry.GetPlayerIndex(owner);
        if (guestSlot <= 0)
        {
            return true;
        }

        UnlockableSkill result = null;
        if (___unlocableSkill != null)
        {
            foreach (string id in ___unlocableSkill)
            {
                if (Player2SkillManager.IsUnlocked(guestSlot, id))
                {
                    result = Core.SkillManager.GetSkill(id);
                    continue;
                }
                break;
            }
        }
        __result = result;
        string resultId = result != null ? result.id : "null";
        string listStr = ___unlocableSkill != null ? string.Join(",", ___unlocableSkill.ToArray()) : "(null)";
        string cacheKey = resultId;
        if (!lastLoggedResult.TryGetValue(__instance, out string last) || last != cacheKey)
        {
            lastLoggedResult[__instance] = cacheKey;
            DashParryDebugLog.Log($"[Ability] {instanceName} branch=shadow list=[{listStr}] -> result={resultId}");
        }
        return false;
    }
}
