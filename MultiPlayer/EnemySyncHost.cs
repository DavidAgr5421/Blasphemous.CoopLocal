using System;
using Framework.FrameworkCore;
using System.Collections.Generic;
using Framework.Managers;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using Steamworks;
using UnityEngine;

namespace Blasphemous.CoopLocal.MultiPlayer;

/// <summary>
/// Host side of the enemy sync (step B): 20 Hz EnemyState broadcaster + SceneReady handler / EnemyFullSync reply.
/// See Modding/ENEMY_SYNC_SPEC.md.
/// </summary>
internal static class EnemySyncHost
{
    private const float BroadcastInterval = 1f / 20f;
    private const float FullRefreshInterval = 1f;
    private const float RangeSqr = 40f * 40f;
    private const float PosDelta = 0.02f;

    private static readonly Dictionary<uint, EnemyStateEntry> LastSent = new Dictionary<uint, EnemyStateEntry>();
    private static readonly List<Vector2> PlayerPositions = new List<Vector2>();
    private static float _nextBroadcast;
    private static float _nextFullRefresh;
    private static ushort _sequence;
    private static ushort _lastSceneHash;

    private static bool _deathSubscribed;

    private static void EnsureDeathSubscribed()
    {
        if (_deathSubscribed) return;
        _deathSubscribed = true;
        Entity.Death += OnEntityDeath;
    }

    private static void OnEntityDeath(Entity entity)
    {
        try
        {
            if (!CoopNetworkManager.IsOnlineHost) return;
            Enemy enemy = entity as Enemy;
            if (enemy == null) return;
            if (!EnemySyncRegistry.TryGetNetId(enemy, out uint netId)) return;

            byte[] packet = CoopNetworkProtocol.SerializeEnemyEvent(new EnemyEventData
            {
                Kind = EnemyEventData.KIND_DEATH,
                NetId = netId,
                SceneHash = EnemySyncRegistry.SceneHash16(),
                KillerSlot = -1
            });
            foreach (CSteamID id in CoopNetworkManager.ConnectedClientIds())
                CoopNetworkManager.SendPacket(id, packet, CoopNetworkManager.CHANNEL_RELIABLE, EP2PSend.k_EP2PSendReliable);
        }
        catch (Exception ex)
        {
            Blasphemous.ModdingAPI.ModLog.Error($"[EnemySync] OnEntityDeath failed: {ex}", Main.CoopLocal);
        }
    }

    /// <summary>Host: EnemySpawner.RespawnDeadEnemies (respawn = true) or Reset (false) happened.</summary>
    internal static void BroadcastRespawn(bool respawn)
    {
        try
        {
            if (!CoopNetworkManager.IsOnlineHost) return;
            LastSent.Clear();
            byte[] packet = CoopNetworkProtocol.SerializeEnemyEvent(new EnemyEventData
            {
                Kind = EnemyEventData.KIND_RESPAWN_ALL,
                NetId = 0,
                SceneHash = EnemySyncRegistry.SceneHash16(),
                KillerSlot = (sbyte)(respawn ? 1 : 0)
            });
            foreach (CSteamID id in CoopNetworkManager.ConnectedClientIds())
                CoopNetworkManager.SendPacket(id, packet, CoopNetworkManager.CHANNEL_RELIABLE, EP2PSend.k_EP2PSendReliable);
            Blasphemous.ModdingAPI.ModLog.Info($"[EnemySync] RespawnAll enviado (respawn={respawn})", Main.CoopLocal);
        }
        catch (Exception ex)
        {
            Blasphemous.ModdingAPI.ModLog.Error($"[EnemySync] BroadcastRespawn failed: {ex}", Main.CoopLocal);
        }
    }

    internal static void Tick()
    {
        EnsureDeathSubscribed();
        if (Time.time < _nextBroadcast) return;
        _nextBroadcast = Time.time + BroadcastInterval;

        if (EnemySyncRegistry.Count == 0) return;

        ushort sceneHash = EnemySyncRegistry.SceneHash16();
        if (sceneHash != _lastSceneHash)
        {
            _lastSceneHash = sceneHash;
            LastSent.Clear();
        }

        CollectPlayerPositions();
        if (PlayerPositions.Count == 0) return;

        bool full = Time.time >= _nextFullRefresh;
        if (full) _nextFullRefresh = Time.time + FullRefreshInterval;

        var entries = new List<EnemyStateEntry>();
        foreach (var reg in EnemySyncRegistry.GetAll())
        {
            Enemy enemy = reg.Enemy;
            if (enemy == null || !enemy.gameObject.activeInHierarchy) continue;

            Vector3 p = enemy.transform.position;
            if (!InRange(p)) continue;

            EnemyStateEntry e = BuildEntry(reg.NetId, enemy, p);

            if (!full && LastSent.TryGetValue(reg.NetId, out EnemyStateEntry prev))
            {
                float dx = e.X - prev.X, dy = e.Y - prev.Y;
                bool changed = (dx * dx + dy * dy) > PosDelta * PosDelta
                    || e.AnimShortHash != prev.AnimShortHash
                    || e.Hp != prev.Hp
                    || e.Flags != prev.Flags;
                if (!changed) continue;
            }

            LastSent[reg.NetId] = e;
            entries.Add(e);
        }

        if (entries.Count == 0) return;

        foreach (EnemyStateEntry[] chunk in CoopNetworkProtocol.ChunkEnemyStates(entries))
        {
            byte[] packet = CoopNetworkProtocol.SerializeEnemyState(new EnemyStateData
            {
                Sequence = ++_sequence,
                SceneHash = sceneHash,
                Enemies = chunk
            });
            foreach (CSteamID id in CoopNetworkManager.ConnectedClientIds())
                CoopNetworkManager.SendPacket(id, packet, CoopNetworkManager.CHANNEL_STATE, EP2PSend.k_EP2PSendUnreliableNoDelay);
        }
    }

    private static void CollectPlayerPositions()
    {
        PlayerPositions.Clear();
        for (int i = 0; i < CoopPlayerRegistry.MaxPlayers; i++)
        {
            Penitent pen = CoopPlayerRegistry.GetSlot(i)?.Instance;
            if (pen == null && i == 0) pen = Core.Logic?.Penitent;
            if (pen == null || !pen.gameObject.activeInHierarchy) continue;
            Vector3 pp = pen.transform.position;
            PlayerPositions.Add(new Vector2(pp.x, pp.y));
        }
    }

    private static bool InRange(Vector3 p)
    {
        for (int i = 0; i < PlayerPositions.Count; i++)
        {
            float dx = PlayerPositions[i].x - p.x, dy = PlayerPositions[i].y - p.y;
            if (dx * dx + dy * dy <= RangeSqr) return true;
        }
        return false;
    }

    private static ushort ClampHp(float v)
    {
        if (v <= 0f) return 0;
        if (v >= ushort.MaxValue) return ushort.MaxValue;
        return (ushort)v;
    }

    private static EnemyStateEntry BuildEntry(uint netId, Enemy enemy, Vector3 p)
    {
        float life = 0f;
        try { life = enemy.Stats.Life.Current; } catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemySyncHost.cs:182"); }

        bool dead = false;
        try { dead = enemy.Status.Dead; } catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemySyncHost.cs:185"); }

        EnemyStateFlags flags = EnemyStateFlags.None;
        if (!dead && life > 0f) flags |= EnemyStateFlags.Alive;
        if (enemy.Status.Orientation == EntityOrientation.Left) flags |= EnemyStateFlags.FacingLeft;
        SpriteRenderer sr = enemy.SpriteRenderer;
        if (sr == null || sr.enabled) flags |= EnemyStateFlags.Visible;

        int hash = 0;
        byte norm = 0;
        Animator anim = enemy.Animator;
        if (anim != null)
        {
            try
            {
                AnimatorStateInfo info = anim.GetCurrentAnimatorStateInfo(0);
                hash = info.shortNameHash;
                float t = info.normalizedTime;
                t = t - Mathf.Floor(t);
                norm = (byte)Mathf.Clamp(Mathf.RoundToInt(t * 255f), 0, 255);
            }
            catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemySyncHost.cs:206"); }
        }

        return new EnemyStateEntry
        {
            NetId = netId,
            X = p.x,
            Y = p.y,
            Flags = flags,
            AnimShortHash = hash,
            AnimNormTime = norm,
            Hp = ClampHp(life)
        };
    }

    internal static void HandleSceneReady(CSteamID sender, byte[] data)
    {
        SceneReadyData d = CoopNetworkProtocol.DeserializeSceneReady(data);
        ushort hostHash = EnemySyncRegistry.SceneHash16();
        int slot = CoopNetworkManager.TryGetClientSlot(sender, out int s) ? s : -1;

        if (d.SceneHash != hostHash)
        {
            Blasphemous.ModdingAPI.ModLog.Warn($"[EnemySync] SceneReady slot {slot}: scene hash mismatch host={hostHash} client={d.SceneHash} (host scene '{EnemySyncRegistry.GetCurrentSceneName()}'), ignorado", Main.CoopLocal);
            return;
        }

        var clientIds = new HashSet<uint>(d.NetIds);
        var hostIds = new HashSet<uint>();
        var full = new List<EnemyFullSyncEntry>();
        foreach (var reg in EnemySyncRegistry.GetAll())
        {
            hostIds.Add(reg.NetId);
            Enemy enemy = reg.Enemy;
            bool alive = false;
            ushort hp = 0;
            float x, y;
            if (enemy != null)
            {
                Vector3 p = enemy.transform.position;
                x = p.x; y = p.y;
                try
                {
                    float life = enemy.Stats.Life.Current;
                    alive = !enemy.Status.Dead && life > 0f;
                    hp = ClampHp(life);
                }
                catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemySyncHost.cs:253"); }
            }
            else
            {
                Vector3 p = reg.SpawnPoint != null ? reg.SpawnPoint.transform.position : Vector3.zero;
                x = p.x; y = p.y;
            }
            full.Add(new EnemyFullSyncEntry { NetId = reg.NetId, Alive = alive, Hp = hp, X = x, Y = y });
        }

        var missing = new List<uint>();
        foreach (uint id in hostIds) if (!clientIds.Contains(id)) missing.Add(id);
        var extra = new List<uint>();
        foreach (uint id in clientIds) if (!hostIds.Contains(id)) extra.Add(id);

        Blasphemous.ModdingAPI.ModLog.Info(
            $"[EnemySync] SceneReady slot {slot}: host={hostIds.Count} client={clientIds.Count} missing-on-client={missing.Count} extra-on-client={extra.Count}"
            + (missing.Count > 0 ? " missingIds=[" + JoinIds(missing) + "]" : "")
            + (extra.Count > 0 ? " extraIds=[" + JoinIds(extra) + "]" : ""), Main.CoopLocal);

        // Single reliable packet (Steam allows up to 1 MB); not chunked so the client can treat absence as "deactivate".
        byte[] packet = CoopNetworkProtocol.SerializeEnemyFullSync(new EnemyFullSyncData
        {
            SceneHash = hostHash,
            Enemies = full.ToArray()
        });
        CoopNetworkManager.SendPacket(sender, packet, CoopNetworkManager.CHANNEL_RELIABLE, EP2PSend.k_EP2PSendReliable);

        EnemySyncWorld.SendSceneStateTo(sender);
    }

    private static string JoinIds(List<uint> ids)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < ids.Count && i < 10; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(ids[i]);
        }
        if (ids.Count > 10) sb.Append(",...");
        return sb.ToString();
    }
}

/// <summary>Host: broadcast respawn / reset of the enemy spawner so clients mirror consumedSpawns.</summary>
[HarmonyLib.HarmonyPatch(typeof(Gameplay.GameControllers.Enemies.Framework.IA.EnemySpawner), nameof(Gameplay.GameControllers.Enemies.Framework.IA.EnemySpawner.RespawnDeadEnemies))]
internal static class EnemySpawner_RespawnDeadEnemies_EnemySync_Patch
{
    private static void Postfix() { EnemySyncHost.BroadcastRespawn(true); }
}

[HarmonyLib.HarmonyPatch(typeof(Gameplay.GameControllers.Enemies.Framework.IA.EnemySpawner), nameof(Gameplay.GameControllers.Enemies.Framework.IA.EnemySpawner.Reset))]
internal static class EnemySpawner_Reset_EnemySync_Patch
{
    private static void Postfix() { EnemySyncHost.BroadcastRespawn(false); }
}
