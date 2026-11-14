using System;
using System.Collections.Generic;
using Blasphemous.CoopLocal.Combat;
using Framework.Managers;
using Gameplay.GameControllers.Enemies.Framework.Attack;
using Gameplay.GameControllers.Enemies.Framework.IA;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using Gameplay.GameControllers.Penitent.Damage;
using HarmonyLib;
using Tools.Level.Layout;
using UnityEngine;

namespace Blasphemous.CoopLocal.MultiPlayer;

/// <summary>
/// Per-scene registry netId -> (Enemy, EnemySpawnPoint). Filled by a Postfix on EnemySpawnPoint.CreateEnemy
/// (both host and client), cleared on LevelManager.OnBeforeLevelLoad. See Modding/ENEMY_SYNC_SPEC.md.
/// </summary>
internal static class EnemySyncRegistry
{
    internal struct Entry
    {
        public uint NetId;
        public Enemy Enemy;
        public EnemySpawnPoint SpawnPoint;
    }

    private static readonly Dictionary<uint, Entry> ById = new Dictionary<uint, Entry>();
    private static readonly Dictionary<EnemySpawnPoint, uint> BySpawn = new Dictionary<EnemySpawnPoint, uint>();
    private static readonly Dictionary<uint, int> OrdinalCounters = new Dictionary<uint, int>();
    private static string _sceneName = "";
    private static bool _loggedThisScene;

    public static string SceneName => _sceneName;

    public static IEnumerable<Entry> GetAll() => ById.Values;

    public static int Count => ById.Count;

    public static bool TryGet(uint netId, out Entry entry) => ById.TryGetValue(netId, out entry);

    public static bool TryGetNetId(EnemySpawnPoint spawn, out uint netId) => BySpawn.TryGetValue(spawn, out netId);

    public static ushort SceneHash16() => SceneHash16(GetCurrentSceneName());

    public static ushort SceneHash16(string sceneName)
    {
        uint h = Fnv1a(0x811C9DC5u, sceneName ?? "");
        return (ushort)((h >> 16) ^ (h & 0xFFFF));
    }

    internal static string GetCurrentSceneName()
    {
        try
        {
            var lvl = Core.LevelManager != null ? Core.LevelManager.currentLevel : null;
            return lvl != null && lvl.LevelName != null ? lvl.LevelName : "";
        }
        catch { return ""; }
    }

    internal static bool TryGetNetId(Enemy enemy, out uint netId)
    {
        netId = 0;
        if (enemy == null) return false;
        foreach (var kv in ById)
        {
            if (kv.Value.Enemy == enemy) { netId = kv.Key; return true; }
        }
        return false;
    }

    /// <summary>A spawn point re-created its enemy (respawn): keep the same netId, point it at the new instance.</summary>
    internal static void ReplaceEnemy(uint netId, Enemy enemy)
    {
        if (ById.TryGetValue(netId, out Entry e))
        {
            e.Enemy = enemy;
            ById[netId] = e;
        }
    }

    internal static void Clear()
    {
        ById.Clear();
        BySpawn.Clear();
        OrdinalCounters.Clear();
        _loggedThisScene = false;
        _sceneName = "";
    }

    private static uint Fnv1a(uint hash, string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            hash ^= s[i];
            hash *= 16777619u;
        }
        return hash;
    }

    private static uint Fnv1aInt(uint hash, int v)
    {
        for (int i = 0; i < 4; i++)
        {
            hash ^= (uint)((v >> (i * 8)) & 0xFF);
            hash *= 16777619u;
        }
        return hash;
    }

    internal static uint Register(EnemySpawnPoint spawn, Enemy enemy)
    {
        if (BySpawn.TryGetValue(spawn, out uint existing)) return existing;

        string scene = GetCurrentSceneName();
        if (_sceneName != scene)
        {
            // First registration of this scene (or level name changed without an OnBeforeLevelLoad).
            ById.Clear();
            BySpawn.Clear();
            OrdinalCounters.Clear();
            _loggedThisScene = false;
            _sceneName = scene;
        }

        Vector3 p = spawn.transform.position;
        uint h = Fnv1a(0x811C9DC5u, scene);
        h = Fnv1aInt(h, Mathf.RoundToInt(p.x * 100f));
        h = Fnv1aInt(h, Mathf.RoundToInt(p.y * 100f));

        OrdinalCounters.TryGetValue(h, out int ordinal);
        OrdinalCounters[h] = ordinal + 1;
        uint netId = Fnv1aInt(h, ordinal);

        ById[netId] = new Entry { NetId = netId, Enemy = enemy, SpawnPoint = spawn };
        BySpawn[spawn] = netId;
        return netId;
    }

    internal static void OnLevelLoadedLog()
    {
        if (_loggedThisScene || !CoopNetworkManager.IsOnlineHost) return;
        _loggedThisScene = true;
        if (Main.CoopLocal != null)
            Blasphemous.ModdingAPI.ModLog.Info($"[EnemySync] registered {ById.Count} enemies in {_sceneName}", Main.CoopLocal);
    }

    // ---- client neutralization ----

    private static void TryDisable(Behaviour b)
    {
        try { if (b != null) b.enabled = false; } catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemySyncRegistry.cs:154"); }
    }

    private static bool TypeNameInChain(Type t, string name)
    {
        for (; t != null; t = t.BaseType)
            if (t.Name == name) return true;
        return false;
    }

    internal static void NeutralizeAsPuppet(Enemy enemy, EnemySpawnPoint spawn, uint netId)
    {
        GameObject root = enemy.gameObject;

        try
        {
            Behaviour[] behaviours = root.GetComponentsInChildren<Behaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                Behaviour b = behaviours[i];
                if (b == null) continue;
                try
                {
                    if (b is Enemy || b is EnemyBehaviour || b is NPCInputs || b is EnemyAttack
                        || b is ContactDamage || b is VisionCone
                        || TypeNameInChain(b.GetType(), "BehaviourTreeOwner")
                        || TypeNameInChain(b.GetType(), "PlatformCharacterController"))
                    {
                        b.enabled = false;
                    }
                }
                catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemySyncRegistry.cs:185"); }
            }
        }
        catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemySyncRegistry.cs:188"); }

        try
        {
            foreach (var aa in root.GetComponentsInChildren<AttackArea>(true))
            {
                try { var c = aa.GetComponent<Collider2D>(); if (c != null) c.enabled = false; } catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemySyncRegistry.cs:194"); }
            }
        }
        catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemySyncRegistry.cs:197"); }

        try
        {
            foreach (var cd in root.GetComponentsInChildren<ContactDamage>(true))
            {
                try { var c = cd.GetComponent<Collider2D>(); if (c != null) c.enabled = false; } catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemySyncRegistry.cs:203"); }
            }
        }
        catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemySyncRegistry.cs:206"); }

        try
        {
            foreach (var rb in root.GetComponentsInChildren<Rigidbody2D>(true))
            {
                try { if (rb != null) rb.simulated = false; } catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemySyncRegistry.cs:212"); }
            }
        }
        catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemySyncRegistry.cs:215"); }

        bool isBoss = false;
        try { isBoss = BossTargetController.IsBoss(enemy); } catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemySyncRegistry.cs:218"); }

        try
        {
            var puppet = root.GetComponent<CoopEnemyPuppet>();
            if (puppet == null) puppet = root.AddComponent<CoopEnemyPuppet>();
            puppet.NetId = netId;
            puppet.Enemy = enemy;
            puppet.SpawnPoint = spawn;
            puppet.IsBoss = isBoss;
            puppet.TargetPos = root.transform.position;
        }
        catch (Exception ex)
        {
            if (Main.CoopLocal != null)
                Blasphemous.ModdingAPI.ModLog.Error($"[EnemySync] puppet attach failed for {root.name}: {ex.Message}", Main.CoopLocal);
        }

        if (isBoss && Main.CoopLocal != null)
            Blasphemous.ModdingAPI.ModLog.Info($"[EnemySync] neutralized boss {root.name}", Main.CoopLocal);
    }
}

/// <summary>
/// Registers every enemy created by EnemySpawnPoint.CreateEnemy (host and client) and, on the online client,
/// neutralizes it and attaches CoopEnemyPuppet.
/// </summary>
[HarmonyPatch(typeof(EnemySpawnPoint), nameof(EnemySpawnPoint.CreateEnemy))]
internal static class EnemySpawnPoint_CreateEnemy_EnemySync_Patch
{
    private static void Postfix(EnemySpawnPoint __instance)
    {
        try
        {
            Enemy enemy = __instance.SpawnedEnemy;
            if (enemy == null) return;

            bool already = EnemySyncRegistry.TryGetNetId(__instance, out uint prevId);
            bool replaced = false;
            if (already && EnemySyncRegistry.TryGet(prevId, out EnemySyncRegistry.Entry old) && old.Enemy != enemy)
            {
                // Respawn: same spawn point, new Enemy instance.
                EnemySyncRegistry.ReplaceEnemy(prevId, enemy);
                replaced = true;
            }
            uint netId = EnemySyncRegistry.Register(__instance, enemy);
            if (already && !replaced) return;

            if (CoopNetworkManager.IsOnlineClient)
                EnemySyncRegistry.NeutralizeAsPuppet(enemy, __instance, netId);

        }
        catch (Exception ex)
        {
            if (Main.CoopLocal != null)
                Blasphemous.ModdingAPI.ModLog.Error($"[EnemySync] CreateEnemy postfix failed: {ex}", Main.CoopLocal);
        }
    }
}

/// <summary>Online client never takes damage locally; its HP is mirrored from the host.</summary>
[HarmonyPatch(typeof(PenitentDamageArea), nameof(PenitentDamageArea.TakeDamage))]
internal static class PenitentDamageArea_TakeDamage_OnlineClientGuard_Patch
{
    private static bool Prefix(Hit hit, bool force)
    {
        return !CoopNetworkManager.IsOnlineClient;
    }
}

[HarmonyPatch(typeof(Penitent), nameof(Penitent.Damage), new[] { typeof(Hit) })]
internal static class Penitent_Damage_OnlineClientGuard_Patch
{
    private static bool Prefix(Hit hit)
    {
        return !CoopNetworkManager.IsOnlineClient;
    }
}
