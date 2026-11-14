using System;
using System.Collections;
using System.Collections.Generic;
using Framework.Managers;
using Gameplay.GameControllers.Enemies.Framework.Damage;
using Gameplay.GameControllers.Entities;
using HarmonyLib;
using Steamworks;
using UnityEngine;

namespace Blasphemous.CoopLocal.MultiPlayer;

/// <summary>
/// Client side of the enemy sync (step B): SceneReady sender and EnemyState / EnemyFullSync applier.
/// See Modding/ENEMY_SYNC_SPEC.md.
/// </summary>
internal static class EnemySyncClient
{
    private static int _sentCount = -1;
    private static ushort _sentHash;

    /// <summary>Called from CoopLocal.OnLevelLoaded (online client only).</summary>
    internal static void SendSceneReady()
    {
        if (!CoopNetworkManager.IsOnlineClient) return;
        SendSceneReadyInternal();
        var mgr = CoopNetworkManager.Instance;
        if (mgr != null) mgr.StartCoroutine(ResendIfChanged());
    }

    private static void SendSceneReadyInternal()
    {
        var ids = new List<uint>();
        foreach (var reg in EnemySyncRegistry.GetAll()) ids.Add(reg.NetId);
        ushort hash = EnemySyncRegistry.SceneHash16();
        _sentCount = ids.Count;
        _sentHash = hash;

        byte[] packet = CoopNetworkProtocol.SerializeSceneReady(new SceneReadyData { SceneHash = hash, NetIds = ids.ToArray() });
        CoopNetworkManager.SendPacket(CoopLobbyManager.HostSteamId, packet, CoopNetworkManager.CHANNEL_RELIABLE, EP2PSend.k_EP2PSendReliable);
        Blasphemous.ModdingAPI.ModLog.Info($"[EnemySync] SceneReady enviado: scene='{EnemySyncRegistry.GetCurrentSceneName()}' hash={hash} count={ids.Count}", Main.CoopLocal);
    }

    private static IEnumerator ResendIfChanged()
    {
        ushort hashAtStart = EnemySyncRegistry.SceneHash16();
        yield return new WaitForSecondsRealtime(1f);
        if (!CoopNetworkManager.IsOnlineClient) yield break;
        if (EnemySyncRegistry.SceneHash16() != hashAtStart) yield break; // another load took over
        if (EnemySyncRegistry.Count != _sentCount || EnemySyncRegistry.SceneHash16() != _sentHash)
            SendSceneReadyInternal();
    }

    internal static void ApplyState(EnemyStateData d)
    {
        if (d.SceneHash != EnemySyncRegistry.SceneHash16()) return;
        DashParryDebugLog.LogOnce("EnemySync.state." + d.SceneHash,
            $"[EnemySync] primer EnemyState en escena '{EnemySyncRegistry.GetCurrentSceneName()}' ({d.Enemies.Length} entradas, seq {d.Sequence})");

        for (int i = 0; i < d.Enemies.Length; i++)
        {
            EnemyStateEntry e = d.Enemies[i];
            CoopEnemyPuppet puppet = FindPuppet(e.NetId);
            if (puppet == null) continue;
            puppet.ApplyState(e);
        }
    }

    internal static void ApplyFullSync(EnemyFullSyncData d)
    {
        if (d.SceneHash != EnemySyncRegistry.SceneHash16())
        {
            Blasphemous.ModdingAPI.ModLog.Info($"[EnemySync] EnemyFullSync descartado (hash {d.SceneHash} != {EnemySyncRegistry.SceneHash16()})", Main.CoopLocal);
            return;
        }

        var listed = new Dictionary<uint, EnemyFullSyncEntry>();
        for (int i = 0; i < d.Enemies.Length; i++) listed[d.Enemies[i].NetId] = d.Enemies[i];

        int applied = 0, deactivatedAbsent = 0, deactivatedDead = 0, notFoundLocally = 0;

        foreach (var kv in listed)
        {
            if (!EnemySyncRegistry.TryGet(kv.Key, out _)) notFoundLocally++;
        }

        var toProcess = new List<EnemySyncRegistry.Entry>(EnemySyncRegistry.GetAll());
        foreach (var reg in toProcess)
        {
            Enemy enemy = reg.Enemy;
            if (enemy == null) continue;
            CoopEnemyPuppet puppet = enemy.GetComponent<CoopEnemyPuppet>();
            if (puppet == null) continue;

            if (!listed.TryGetValue(reg.NetId, out EnemyFullSyncEntry f))
            {
                puppet.Deactivate();
                deactivatedAbsent++;
            }
            else if (!f.Alive)
            {
                puppet.Deactivate();
                try { Core.Logic.EnemySpawner.AddConsumedSpawner(puppet.SpawnPoint); }
                catch (Exception ex) { Blasphemous.ModdingAPI.ModLog.Error($"[EnemySync] AddConsumedSpawner (fullsync) failed: {ex.Message}", Main.CoopLocal); }
                deactivatedDead++;
            }
            else
            {
                puppet.ApplyFull(f);
                applied++;
            }
        }

        Blasphemous.ModdingAPI.ModLog.Info(
            $"[EnemySync] EnemyFullSync aplicado: listed={listed.Count} applied={applied} deactivated-absent={deactivatedAbsent} deactivated-dead={deactivatedDead} host-only(not-found-locally)={notFoundLocally}",
            Main.CoopLocal);
    }

    internal static void ApplyEvent(EnemyEventData d)
    {
        try
        {
            if (d.Kind == EnemyEventData.KIND_DEATH)
            {
                if (d.SceneHash != EnemySyncRegistry.SceneHash16()) return;
                CoopEnemyPuppet puppet = FindPuppet(d.NetId);
                if (puppet == null) return;
                puppet.Deactivate();
                Core.Logic.EnemySpawner.AddConsumedSpawner(puppet.SpawnPoint);
            }
            else if (d.Kind == EnemyEventData.KIND_RESPAWN_ALL)
            {
                ApplyRespawn(d);
            }
        }
        catch (Exception ex)
        {
            Blasphemous.ModdingAPI.ModLog.Error($"[EnemySync] ApplyEvent kind={d.Kind} failed: {ex}", Main.CoopLocal);
        }
    }

    private static void ApplyRespawn(EnemyEventData d)
    {
        var spawner = Core.Logic.EnemySpawner;
        bool respawn = d.KillerSlot == 1;
        if (!respawn || d.SceneHash != EnemySyncRegistry.SceneHash16())
        {
            spawner.Reset();
            return;
        }

        // Dead puppets keep SpawnPoint.HasEnemySpawned = true (OnDeath never fired locally): clear it so
        // RespawnDeadEnemies re-creates them; the CreateEnemy postfix re-registers and neutralizes the new instance.
        var toProcess = new List<EnemySyncRegistry.Entry>(EnemySyncRegistry.GetAll());
        int reset = 0;
        foreach (var reg in toProcess)
        {
            try
            {
                CoopEnemyPuppet puppet = reg.Enemy != null ? reg.Enemy.GetComponent<CoopEnemyPuppet>() : null;
                bool dead = reg.Enemy == null || (puppet != null && !puppet.Alive);
                if (!dead || reg.SpawnPoint == null) continue;
                Traverse.Create(reg.SpawnPoint).Property("HasEnemySpawned").SetValue(false);
                if (reg.Enemy != null) UnityEngine.Object.Destroy(reg.Enemy.gameObject);
                reset++;
            }
            catch (Exception ex)
            {
                Blasphemous.ModdingAPI.ModLog.Error($"[EnemySync] respawn prep failed: {ex.Message}", Main.CoopLocal);
            }
        }
        spawner.RespawnDeadEnemies();
        Blasphemous.ModdingAPI.ModLog.Info($"[EnemySync] RespawnAll aplicado: puppets muertos reiniciados={reset}", Main.CoopLocal);
    }

    private static CoopEnemyPuppet FindPuppet(uint netId)
    {
        if (!EnemySyncRegistry.TryGet(netId, out EnemySyncRegistry.Entry reg)) return null;
        if (reg.Enemy == null) return null;
        return reg.Enemy.GetComponent<CoopEnemyPuppet>();
    }
}

/// <summary>Online client: the local (mirror) penitent can never damage/kill puppets.</summary>
[HarmonyPatch(typeof(EnemyDamageArea), nameof(EnemyDamageArea.TakeDamage))]
internal static class EnemyDamageArea_TakeDamage_OnlineClientGuard_Patch
{
    private static bool Prefix(Hit hit, bool force)
    {
        return !CoopNetworkManager.IsOnlineClient;
    }
}

[HarmonyPatch(typeof(Entity), nameof(Entity.Damage), new[] { typeof(float), typeof(string) })]
internal static class Entity_Damage_PuppetGuard_Patch
{
    private static bool Prefix(Entity __instance, float amount, string impactAudioId)
    {
        if (!CoopNetworkManager.IsOnlineClient) return true;
        return __instance.GetComponent<CoopEnemyPuppet>() == null;
    }
}
