using Framework.FrameworkCore;
using Framework.Managers;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Blasphemous.CoopLocal;

// Round 61: shadow per-P2 del Skill Tree. SkillManager es singleton global (allSkills: Dictionary<string,UnlockableSkill>)
// con un solo bool `unlocked` por id. Ability.GetLastUnlockedSkill() recorre `unlocableSkill` (List<string> privada)
// y pregunta a Core.SkillManager.IsSkillUnlocked(id) -> hardcodea P1. Para P2 sombreamos solo el bool
// en un dict + archivo p2_skills_slot{slot}.txt (mismo patrón que Player2StatsSync: SnapshotPath/SaveSnapshot/ApplySnapshot).

internal static class Player2SkillManager
{
    // Los 15 ids reales de UnlockableSkillId (Gameplay.GameControllers.Penitent.Abilities) - orden para UI/debug.
    internal static readonly string[] AllIds =
    {
        "CHARGED_1", "CHARGED_2", "CHARGED_3",
        "COMBO_1", "COMBO_2", "COMBO_3",
        "LUNGE_1", "LUNGE_2", "LUNGE_3",
        "RANGED_1", "RANGED_2", "RANGED_3",
        "VERTICAL_1", "VERTICAL_2", "VERTICAL_3",
    };

    private static readonly Dictionary<string, bool> shadow = new Dictionary<string, bool>();
    private static bool initialized;

    private static string MarkerDirectory => Path.Combine(Application.persistentDataPath, "CoopLocalMod");
    private static string SnapshotPath(int slot) => Path.Combine(MarkerDirectory, $"p2_skills_slot{slot}.txt");

    private static void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;
        foreach (string id in AllIds)
        {
            if (!shadow.ContainsKey(id))
                shadow[id] = false;
        }
    }

    internal static bool IsUnlocked(string id)
    {
        EnsureInitialized();
        return shadow.TryGetValue(id, out bool v) && v;
    }

    internal static void SetUnlocked(string id, bool value)
    {
        EnsureInitialized();
        shadow[id] = value;
    }

    internal static void LoadForSlot(int slot)
    {
        EnsureInitialized();
        // reset a false antes de aplicar snapshot para no arrastrar valores de slot anterior
        foreach (string id in AllIds) shadow[id] = false;

        string path = SnapshotPath(slot);
        if (!File.Exists(path))
        {
            // Partida nueva: todo bloqueado. No se clona de P1 (Core.SkillManager.IsSkillUnlocked)
            // aunque sería trivial iterar AllIds y copiar el bool de P1 al shadow. Decisión consciente
            // (Ronda 60): sin flujo de compra real para P2 todavía, clonar daría a P2 progresión gratis.
            // Si en el futuro se quiere clonado inicial, reemplazar este return por:
            //   foreach(string id in AllIds) shadow[id]=Core.SkillManager.IsSkillUnlocked(id);
            //   SaveForSlot(slot);
            return;
        }

        try
        {
            foreach (string line in File.ReadAllLines(path))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                if (!shadow.ContainsKey(key)) continue;
                if (bool.TryParse(val, out bool b)) shadow[key] = b;
                else if (val == "1") shadow[key] = true;
                else if (val == "0") shadow[key] = false;
            }
            if (Main.CoopLocal != null)
                Blasphemous.ModdingAPI.ModLog.Info($"[P2Skills] loaded {path}", Main.CoopLocal);
        }
        catch (Exception ex)
        {
            if (Main.CoopLocal != null)
                Blasphemous.ModdingAPI.ModLog.Info($"[P2Skills] failed to load {path}: {ex.Message}", Main.CoopLocal);
        }
    }

    internal static void SaveForSlot(int slot)
    {
        EnsureInitialized();
        string path = SnapshotPath(slot);
        try
        {
            Directory.CreateDirectory(MarkerDirectory);
            List<string> lines = new List<string>();
            foreach (string id in AllIds)
            {
                shadow.TryGetValue(id, out bool v);
                lines.Add($"{id}={v.ToString().ToLowerInvariant()}");
            }
            File.WriteAllLines(path, lines.ToArray());
            if (Main.CoopLocal != null)
                Blasphemous.ModdingAPI.ModLog.Info($"[P2Skills] saved {path}", Main.CoopLocal);
        }
        catch (Exception ex)
        {
            if (Main.CoopLocal != null)
                Blasphemous.ModdingAPI.ModLog.Info($"[P2Skills] failed to save {path}: {ex.Message}", Main.CoopLocal);
        }
    }

    internal static void Persist()
    {
        int slot = PersistentManager.GetAutomaticSlot();
        if (slot < 0) return;
        SaveForSlot(slot);
    }

    // Llamado desde CoopLocal.OnPlayerSpawn cada vez que (re)spawnea P2, con el slot actual.
    internal static void EnsureLoadedForCurrentSlot()
    {
        int slot = PersistentManager.GetAutomaticSlot();
        if (slot < 0) return;
        LoadForSlot(slot);
    }
}

// Unico punto de gating de skills: Ability.GetLastUnlockedSkill() recorre el campo privado
// unlocableSkill y pregunta a Core.SkillManager.IsSkillUnlocked. Para P2 sombreamos
// el bool y seguimos usando Core.SkillManager.GetSkill(id) para la definicion (global).
//
// Round 62 fix: el campo real en Framework.FrameworkCore.Ability es
// `private List<string> unlocableSkill;` - SIN guion bajo propio. La convencion de Harmony para
// reversed-fields es siempre exactamente 3 guiones bajos + el nombre real del campo tal cual esta
// declarado - los "____penitent" (4 guiones) usados en el resto del repo son 3 + el propio "_"
// inicial del campo `_penitent` (confirmado contra el decompilado de Dash.cs: `private Penitent
// _penitent;`), no una regla especial de 4. Con 4 guiones aca, Harmony buscaba un campo
// "_unlocableSkill" que no existe - confirmado en BepInEx/LogOutput.log de una sesion real:
// "[Error: HarmonyX] Failed to patch ... GetLastUnlockedSkill(): ArgumentException: No such field
// defined in class Framework.FrameworkCore.Ability / Parameter name: _unlocableSkill". HarmonyX
// (a diferencia de Lib.Harmony clasico) usa ILHook via PatchClassProcessor.ProcessPatchJob, que
// atrapa la excepcion por patch individual y sigue con el resto (no aborta todo PatchAll) - por
// eso el resto del mod seguia funcionando y este patch en particular simplemente nunca se
// aplicaba: Ability.GetLastUnlockedSkill() corria 100% vanilla para P1 Y P2, ambos leyendo el
// mismo Core.SkillManager global - explica el bug reportado ("si P1 tiene la skill, P2 tambien
// puede usarla" y "P2 no puede usar su propia skill, depende de que P1 la tenga tambien": el
// shadow dict de Player2SkillManager nunca se consultaba porque el Prefix nunca corria).
[HarmonyPatch(typeof(Ability), "GetLastUnlockedSkill")]
internal static class Ability_GetLastUnlockedSkill_P2_Patch
{
    private static readonly Dictionary<Ability, string> lastLoggedResult = new Dictionary<Ability, string>();
    private static readonly Dictionary<Ability, string> lastLoggedIdentity = new Dictionary<Ability, string>();

    private static bool Prefix(Ability __instance, ref UnlockableSkill __result, List<string> ___unlocableSkill)
    {
        // Round 76 audit fix: the identity-mismatch log this round was added *for* (Player2SkillManager
        // Ronda 76 comment: "descartar identidad de referencia, P2 recreado Ronda 55/63") was originally
        // written *after* the `EntityOwner != CoopLocal.Player2 => return true` branch below - but that
        // branch is exactly what discards the one case the log claims to check: if EntityOwner is a STALE
        // Penitent (an orphaned P2 Ability component whose EntityOwner never got updated to the current
        // CoopLocal.Player2 after a respawn/room transition), this method returns `true` right there and
        // vanilla runs silently - the log statement past that point is unreachable for that exact case, so
        // the previous version could only ever print "==" (dead code, never "!="). Moved here, before the
        // early return, and widened to cover every Ability instance (not only ones already confirmed ==
        // Player2) so a stale/orphaned instance is actually visible instead of silently falling through to
        // vanilla/global gating (which would explain "P2 executes whatever P1 has unlocked" if it happens).
        Penitent owner = __instance.EntityOwner as Penitent;
        string identity;
        if (owner == null) identity = "null";
        else if (owner == Core.Logic.Penitent) identity = "P1";
        else if (owner == CoopLocal.Player2) identity = "P2-current";
        else identity = "STALE"; // neither today's P1 nor today's P2 - the smoking gun this log exists for
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

        if (__instance.EntityOwner != CoopLocal.Player2)
        {
            return true;
        }
        UnlockableSkill result = null;
        if (___unlocableSkill != null)
        {
            foreach (string id in ___unlocableSkill)
            {
                if (Player2SkillManager.IsUnlocked(id))
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
        // Edge-triggered por instancia de Ability - resultado real del shadow (identidad ya logueada arriba)
        if (!lastLoggedResult.TryGetValue(__instance, out string last) || last != cacheKey)
        {
            lastLoggedResult[__instance] = cacheKey;
            DashParryDebugLog.Log($"[Ability] {instanceName} branch=shadow list=[{listStr}] -> result={resultId}");
        }
        return false;
    }
}
